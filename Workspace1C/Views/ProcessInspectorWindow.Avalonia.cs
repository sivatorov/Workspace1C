#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
    /// «Обновить» и завершением выбранного процесса с подтверждением. Двойной клик
    /// по строке с известной базой — переход к базе в главном окне (FindInList-механика).
    /// Вся логика — в чистой ViewModel <see cref="ProcessInspectorViewModel"/>.
    /// </summary>
    public sealed class ProcessInspectorWindow : ModalWindowBase
    {
        private readonly ProcessInspectorViewModel _vm;
        private readonly ListBox _grid = new();

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

            _vm = new ProcessInspectorViewModel(
                service,
                killer,
                dialogs,
                infobases,
                openBase,
                action => Dispatcher.UIThread.Post(action));

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

            // Таблица процессов: шапка с фиксированными колонками и строки.
            _grid.ItemsSource = _vm.Processes;
            // Двусторонняя привязка выбора (issue #342): клик по строке обновляет
            // SelectedRow во ViewModel, а восстановление после автообновления возвращает
            // выделение в контрол. Без неё выделение сбрасывалось при каждом опросе (~5 с)
            // и «Завершить процесс» находила SelectedRow == null.
            _grid.Bind(ListBox.SelectedItemProperty, new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
            _grid.ItemTemplate = new FuncDataTemplate<ProcessRowViewModel>((row, _) => BuildRow(row));
            _grid.DoubleTapped += OnGrid_DoubleTapped;
            ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);

            // Подсказка внизу.
            var hint = new TextBlock
            {
                Text = LocalizationManager.T("ProcessInspector.Hint"),
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // Кнопки: «Обновить», «Завершить процесс», «Закрыть».
            var refreshButton = BuildActionButton(LocalizationManager.T("ProcessInspector.Refresh"), () => _vm.Refresh(), "⟳");
            var killButton = BuildActionButton(LocalizationManager.T("ProcessInspector.KillProcess"), _vm.KillSelected, "✕");
            killButton.Width = 170;
            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { refreshButton, killButton, closeButton }
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
            Place(grid, _grid, 2);
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

        /// <summary>Строка таблицы: база, режим, пользователь, время старта, PID, строка подключения, значок.</summary>
        private Control BuildRow(ProcessRowViewModel row)
        {
            var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.TextProperty, new Binding("BaseName"));
            var subtitle = new TextBlock { FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.Bind(TextBlock.TextProperty, new Binding("Subtitle"));

            var nameCell = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 8, 2),
                Children = { name, subtitle }
            };

            var mode = CellText("ModeText", bold: true);
            var user = CellText("UserName");
            var startTime = CellText("StartTimeText");

            var pid = CellText("Pid");

            var connection = CellText("ConnectionText");
            ToolTip.SetTip(connection, new Binding("FullCommandLine"));

            // Значок-флаг: зелёный «●» — известная база, серый «○» — неизвестная.
            var knownDot = new TextBlock
            {
                Text = row.IsKnown ? "●" : "○",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Themes.ThemeBrushes.FromSpec(row.IsKnown ? "@SuccessBrush" : "@TextSecondaryBrush")
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(96))
                },
                Children = { nameCell, mode, user, startTime, pid, connection, knownDot }
            };
            Grid.SetColumn(nameCell, 0);
            Grid.SetColumn(mode, 1);
            Grid.SetColumn(user, 2);
            Grid.SetColumn(startTime, 3);
            Grid.SetColumn(pid, 4);
            Grid.SetColumn(connection, 5);
            Grid.SetColumn(knownDot, 6);
            return rowGrid;
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
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        private void OnGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_grid.SelectedItem is ProcessRowViewModel row)
                _vm.OpenBase(row);
        }
    }
}
#endif