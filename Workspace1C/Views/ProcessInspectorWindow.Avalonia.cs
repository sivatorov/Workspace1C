#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Инспектор процессов 1С» (0.3.9.93, Avalonia/Linux): таблица всех запущенных
    /// процессов платформы 1С (база/режим/пользователь/время старта/PID/строка
    /// подключения/значок сопоставления) с автообновлением по таймеру, кнопкой
    /// «Обновить» и завершением выбранных процессов с подтверждением. Двойной клик
    /// по строке с известной базой — переход к базе в главном окне (FindInList-механика).
    /// Вся логика — в чистой ViewModel <see cref="ProcessInspectorViewModel"/>.
    /// <para>
    /// issue #134 (доработка ArtyoZ): мультивыделение (Ctrl/Shift) и массовое
    /// завершение, меню «Колонки» для скрытия ненужных колонок (выбор сохраняется
    /// в настройках). Шапка и строки перестраиваются при изменении набора колонок.
    /// </para>
    /// </summary>
    public sealed class ProcessInspectorWindow : ModalWindowBase
    {
        private readonly ProcessInspectorViewModel _vm;
        private readonly ListBox _grid = new();
        private readonly Grid _header = new() { Margin = new Thickness(0, 0, 0, 4) };

        /// <summary>Ключи локализации заголовков колонок по значению перечисления.</summary>
        private static readonly Dictionary<ProcessInspectorColumn, string> ColumnTitleKeys =
            ProcessInspectorViewModel.AllColumns.ToDictionary(info => info.Column, info => info.TitleKey);

        /// <param name="infobases">Все базы списка (для сопоставления командных строк).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public ProcessInspectorWindow(IEnumerable<Infobase> infobases, Action<Infobase> openBase)
        {
            Title = LocalizationManager.T("ProcessInspector.Title");
            Width = 1080;
            Height = 600;
            MinWidth = 820;
            MinHeight = 420;
            FontSize = 13;
            CanResize = true;

            var service = AppServices.GetRequiredService<IRunningInfobasesService>();
            var killer = AppServices.GetRequiredService<IOneCProcessKiller>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            // issue #134: скрытые колонки хранятся в настройках приложения.
            var repository = AppServices.TryGetService<IInfobaseRepository>();
            var settings = repository?.LoadSettings() ?? new AppSettings();
            Action? saveSettings = repository is null ? null : () => repository.SaveSettings(settings);

            _vm = new ProcessInspectorViewModel(
                service,
                killer,
                dialogs,
                infobases,
                openBase,
                action => Dispatcher.UIThread.Post(action),
                settings,
                saveSettings);

            DataContext = _vm;
            Closed += (_, _) => _vm.Dispose();

            // Заголовок.
            var title = new TextBlock
            {
                Text = LocalizationManager.T("ProcessInspector.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Итоговая строка («Всего процессов: N»).
            var summary = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(0, 6, 0, 10)
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));

            // Таблица процессов: шапка с видимыми колонками и строки (issue #134).
            _grid.ItemsSource = _vm.Processes;
            // issue #140 (1.0.1.84): явно назначаем тему контейнера строки, чтобы
            // выделение (в том числе мультивыделение) не зависело от неявного стиля
            // и было видно после автообновления/перестроения таблицы.
            _grid.ItemContainerTheme = (ControlTheme?)Application.Current?.FindResource("ModernListBoxItem");
            // «Зебра» чётных строк: класс zebra вешается на контейнер при его подготовке.
            // Стиль ^.zebra объявлен в ModernListBoxItem до ^:selected, поэтому
            // выделение всегда перекрывает зебру (как в WPF RowStyle инспектора).
            _grid.ContainerPrepared += OnContainerPrepared;
            // Мультивыделение (issue #134): Ctrl/Shift выделяют несколько строк,
            // кнопка «Завершить процесс» действует на весь набор.
            _grid.SelectionMode = SelectionMode.Multiple;
            // Двусторонняя привязка выбора (issue #342): клик по строке обновляет
            // SelectedRow во ViewModel, а восстановление после автообновления возвращает
            // выделение в контрол. Без неё выделение сбрасывалось при каждом опросе (~5 с)
            // и «Завершить процесс» находила SelectedRow == null.
            _grid.Bind(ListBox.SelectedItemProperty, new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
            _grid.SelectionChanged += (_, _) =>
            {
                var selected = _grid.SelectedItems;
                _vm.SetSelectedRows(selected is null
                    ? Array.Empty<ProcessRowViewModel>()
                    : selected.Cast<ProcessRowViewModel>().ToList());
            };
            _grid.DoubleTapped += OnGrid_DoubleTapped;
            ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);

            // issue #134: набор видимых колонок изменён — перестраиваем шапку и строки,
            // выделение восстановлено после автообновления — переставляем SelectedItems.
            _vm.ColumnsChanged += (_, _) => RebuildTable();
            _vm.SelectionRestored += (_, _) => RestoreSelectedRows();

            var tableLayout = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_header, Dock.Top);
            tableLayout.Children.Add(_header);
            tableLayout.Children.Add(_grid);
            RebuildTable();

            // Подсказка внизу.
            var hint = new TextBlock
            {
                Text = LocalizationManager.T("ProcessInspector.Hint"),
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // Кнопки: «Колонки», «Обновить», «Завершить процесс», «Закрыть».
            Button? columnsButton = null;
            columnsButton = BuildActionButton(
                LocalizationManager.T("ProcessInspector.Columns"),
                () => ShowColumnsMenu(columnsButton),
                "▦");
            var refreshButton = BuildActionButton(LocalizationManager.T("ProcessInspector.Refresh"), () => _vm.Refresh(), "⟳");
            var killButton = BuildActionButton(LocalizationManager.T("ProcessInspector.KillProcess"), _vm.KillSelectedMany, "✕");
            killButton.Width = 170;
            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { columnsButton, refreshButton, killButton, closeButton }
            };

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, title, 0);
            Place(grid, summary, 1);
            Place(grid, tableLayout, 2);
            Place(grid, buttons, 3);
            Place(grid, hint, 4);
            Content = grid;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildActionButton(string text, System.Action onClick, string icon)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), 14, "TextPrimaryBrush"),
                Width = 110,
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>
        /// Issue #140 (1.0.1.84): при подготовке контейнера строки выставляет класс
        /// <c>zebra</c> для чётных строк (индекс 1, 3, …). Класс пересчитывается при
        /// каждом переиспользовании контейнера, поэтому зебра не «съезжает» после
        /// автообновления и <see cref="RebuildTable"/>.
        /// </summary>
        private static void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (e.Container is not ListBoxItem item)
                return;

            item.Classes.Remove("zebra");
            if (e.Index % 2 == 1)
                item.Classes.Add("zebra");
        }

        /// <summary>
        /// Перестраивает шапку и шаблон строк по текущему набору видимых колонок
        /// (issue #134). Скрытые колонки не создаются вовсе.
        /// </summary>
        private void RebuildTable()
        {
            var columns = ProcessInspectorViewModel.AllColumns
                .Select(info => info.Column)
                .Where(_vm.IsColumnVisible)
                .ToList();

            _header.Children.Clear();
            _header.ColumnDefinitions.Clear();
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                _header.ColumnDefinitions.Add(new ColumnDefinition(WidthOf(column))
                {
                    MinWidth = MinWidthOf(column)
                });

                var title = new TextBlock
                {
                    Text = LocalizationManager.T(ColumnTitleKeys[column]),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    Margin = new Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(title, i);
                _header.Children.Add(title);
            }

            _grid.ItemTemplate = new FuncDataTemplate<ProcessRowViewModel>(
                (row, _) => BuildRow(row, columns));

            // После смены шаблона контейнеры пересоздаются — возвращаем выделение.
            RestoreSelectedRows();
        }

        /// <summary>Ширина колонки в разметке шапки и строк (issue #134).</summary>
        private static GridLength WidthOf(ProcessInspectorColumn column) => column switch
        {
            ProcessInspectorColumn.Base => new GridLength(2, GridUnitType.Star),
            ProcessInspectorColumn.Mode => new GridLength(1.1, GridUnitType.Star),
            ProcessInspectorColumn.User => new GridLength(1.1, GridUnitType.Star),
            ProcessInspectorColumn.StartTime => GridLength.Auto,
            ProcessInspectorColumn.Pid => new GridLength(60),
            ProcessInspectorColumn.Connection => new GridLength(1.6, GridUnitType.Star),
            ProcessInspectorColumn.Known => new GridLength(96),
            _ => new GridLength(1, GridUnitType.Star)
        };

        /// <summary>Минимальная ширина колонки: заголовок не обрезается полностью.</summary>
        private static double MinWidthOf(ProcessInspectorColumn column) => column switch
        {
            ProcessInspectorColumn.Base => 160,
            ProcessInspectorColumn.Mode => 110,
            ProcessInspectorColumn.User => 110,
            ProcessInspectorColumn.StartTime => 130,
            ProcessInspectorColumn.Pid => 60,
            ProcessInspectorColumn.Connection => 160,
            ProcessInspectorColumn.Known => 96,
            _ => 80
        };

        /// <summary>Строка таблицы: только видимые колонки (issue #134).</summary>
        private Control BuildRow(ProcessRowViewModel row, IReadOnlyList<ProcessInspectorColumn> columns)
        {
            var rowGrid = new Grid();
            for (var i = 0; i < columns.Count; i++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(WidthOf(columns[i]))
                {
                    MinWidth = MinWidthOf(columns[i])
                });

                var cell = BuildCell(row, columns[i]);
                Grid.SetColumn(cell, i);
                rowGrid.Children.Add(cell);
            }

            return rowGrid;
        }

        /// <summary>Ячейка строки для колонки (issue #134).</summary>
        private Control BuildCell(ProcessRowViewModel row, ProcessInspectorColumn column)
        {
            switch (column)
            {
                case ProcessInspectorColumn.Base:
                    var name = new TextBlock
                    {
                        FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    name.Bind(TextBlock.TextProperty, new Binding("BaseName"));
                    var subtitle = new TextBlock
                    {
                        FontSize = 11,
                        Opacity = 0.6,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    subtitle.Bind(TextBlock.TextProperty, new Binding("Subtitle"));
                    return new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 2, 8, 2),
                        Children = { name, subtitle }
                    };

                case ProcessInspectorColumn.Mode:
                    return CellText("ModeText", bold: true);

                case ProcessInspectorColumn.User:
                    return CellText("UserName");

                case ProcessInspectorColumn.StartTime:
                    return CellText("StartTimeText");

                case ProcessInspectorColumn.Pid:
                    return CellText("Pid");

                case ProcessInspectorColumn.Connection:
                    var connection = CellText("ConnectionText");
                    ToolTip.SetTip(connection, new Binding("FullCommandLine"));
                    return connection;

                case ProcessInspectorColumn.Known:
                    // Значок-флаг: зелёный «●» — известная база, серый «○» — неизвестная.
                    return new TextBlock
                    {
                        Text = row.IsKnown ? "●" : "○",
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Themes.ThemeBrushes.FromSpec(row.IsKnown ? "@SuccessBrush" : "@TextSecondaryBrush")
                    };

                default:
                    return CellText(string.Empty);
            }
        }

        private static Control CellText(string property, bool bold = false)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
            };
            if (property.Length > 0)
                text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        /// <summary>
        /// Меню «Колонки» (issue #134, доработка ArtyoZ в 1.0.1.84): чек-лист всех
        /// колонок инспектора. Видимость обозначает настоящий чекбокс
        /// <see cref="MenuItemToggleType.CheckBox"/> (индикатор галочки рисует шаблон
        /// <see cref="ControlThemes.ModernMenuItem"/>), а не текстовый префикс галочки.
        /// </summary>
        private void ShowColumnsMenu(Button? anchor)
        {
            var menu = new ContextMenu();
            menu.Styled(ControlThemes.ModernContextMenu);

            foreach (var info in ProcessInspectorViewModel.AllColumns)
            {
                var column = info.Column;
                // issue #134 (1.0.1.84): IsChecked отражает видимость колонки — у видимых
                // галочка стоит, у скрытых её нет. Клик переключает состояние и сохраняет
                // выбор в настройках.
                var item = new MenuItem
                {
                    Header = LocalizationManager.T(info.TitleKey),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = _vm.IsColumnVisible(column)
                };
                item.Styled(ControlThemes.ModernMenuItem);
                // Интеракция меню переключает IsChecked до события Click,
                // поэтому передаём уже новое состояние флажка.
                item.Click += (_, _) => _vm.SetColumnVisible(column, item.IsChecked);
                menu.Items.Add(item);
            }

            if (anchor is not null)
            {
                menu.PlacementTarget = anchor;
                menu.Placement = PlacementMode.Bottom;
                menu.Open(anchor);
            }
        }

        /// <summary>
        /// Переставляет выделение ListBox по восстановленному набору ViewModel
        /// (issue #134: восстанавливаются все ранее выбранные строки).
        /// </summary>
        private void RestoreSelectedRows()
        {
            var selected = _grid.SelectedItems;
            if (selected is null)
                return;

            var desired = _vm.SelectedRows.ToList();
            var current = selected.Cast<ProcessRowViewModel>().ToList();
            if (current.Count == desired.Count && current.All(desired.Contains))
                return;

            selected.Clear();
            foreach (var row in desired)
            {
                if (_vm.Processes.Contains(row))
                    selected.Add(row);
            }
        }

        private void OnGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_grid.SelectedItem is ProcessRowViewModel row)
                _vm.OpenBase(row);
        }
    }
}
#endif
