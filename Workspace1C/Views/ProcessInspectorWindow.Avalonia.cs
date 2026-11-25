#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Workspace1C.Controls;
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
    /// по строке и клавиша Enter завершают выбранные процессы (issue #134, 1.0.1.100).
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

        /// <summary>
        /// Фокус был внутри таблицы (issue #134, 1.0.1.102): после реального изменения
        /// строк его нужно вернуть, а при клике по кнопке — нет.
        /// </summary>
        private bool _gridHadFocus;

        /// <summary>
        /// После завершения процесса фокус возвращается в таблицу принудительно,
        /// даже если она опустела или диалог подтверждения сбросил
        /// <see cref="_gridHadFocus"/> (issue #134, замечание 7OH от 22.11.2026).
        /// </summary>
        private bool _restoreFocusAfterKill;

        /// <summary>Фокус уже успешно установлен при открытии — повторные попытки не нужны.</summary>
        private bool _focusApplied;

        /// <summary>Окно открыто (Opened уже наступил) — можно ставить фокус.</summary>
        private bool _opened;

        /// <summary>Ключи локализации заголовков колонок по значению перечисления.</summary>
        private static readonly Dictionary<ProcessInspectorColumn, string> ColumnTitleKeys =
            ProcessInspectorViewModel.AllColumns.ToDictionary(info => info.Column, info => info.TitleKey);

        /// <param name="infobases">Все базы списка (для сопоставления командных строк).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public ProcessInspectorWindow(IEnumerable<Infobase> infobases, Action<Infobase> openBase)
        {
            Title = LocalizationManager.T("ProcessInspector.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 1000;
            Height = 560;
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
            _grid.KeyDown += OnGrid_KeyDown;
            // issue #134 (1.0.1.104, замечание 7OH): если фокус оказался на кнопке
            // («Колонки» и т.п.), стрелки должны управлять таблицей, а не переводить
            // фокус на соседний элемент. Перехватываем клавиши на уровне окна.
            // 1.0.1.106: туннельная фаза — кнопки не успевают «съесть» стрелки.
            AddHandler(KeyDownEvent, OnWindow_KeyDown, RoutingStrategies.Tunnel);
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
                "IconViewColumn");
            var refreshButton = BuildActionButton(LocalizationManager.T("ProcessInspector.Refresh"), () => _vm.Refresh(), "IconRefresh");
            var killButton = BuildActionButton(LocalizationManager.T("ProcessInspector.KillProcess"), KillSelectedAndRestoreFocus, "IconClose");
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

            // issue #134 (1.0.1.102, замечание 7OH): фокус ставится после загрузки
            // данных и первого прохода разметки (Dispatcher.UIThread.Post + повтор,
            // если окно ещё не активировано). Прежний синхронный Focus() в Opened
            // не доходил до списка — Ctrl+A не работал без клика мышью.
            _grid.GotFocus += (_, _) =>
            {
                _gridHadFocus = true;
                // issue #134 (1.0.1.110): фокус считается установленным только когда
                // он реально в контейнере строки, а не на внешней оболочке ListBox.
                if (IsGridItemFocused())
                    _focusApplied = true;
            };
            // Фокус ушёл из таблицы (например, клик по кнопке) — не возвращаем его
            // принудительно при автообновлении.
            _grid.LostFocus += (_, _) => Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_grid.IsKeyboardFocusWithin)
                        _gridHadFocus = false;
                },
                DispatcherPriority.Background);
            Deactivated += (_, _) => _gridHadFocus = false;
            Activated += (_, _) =>
            {
                if (_grid.IsKeyboardFocusWithin)
                    _gridHadFocus = true;
                if (!_focusApplied)
                    FocusGridAttempt(0);
            };
            Opened += (_, _) =>
            {
                _opened = true;
                FocusGridAttempt(0);
            };
            // Данные приходят асинхронно (/proc): ставим фокус и по первому заполнению.
            _vm.Processes.CollectionChanged += (_, e) =>
            {
                if (!_focusApplied && e.Action == NotifyCollectionChangedAction.Add)
                    FocusGridAttempt(0);
            };
        }

        /// <summary>
        /// Отложенная установка фокуса в список процессов (issue #134, 1.0.1.102):
        /// после загрузки данных и первого прохода разметки. Повторяем попытки, пока
        /// клавиатурный фокус фактически не окажется в списке (1.0.1.107): активация
        /// окна завершается асинхронно, а после диалога подтверждения фокус может
        /// вернуться кнопке «Колонки».
        /// </summary>
        /// <param name="attempt">Номер попытки (0 — первая).</param>
        /// <param name="pendingKey">
        /// Навигационная клавиша, которую нужно применить ровно один раз (на первой
        /// попытке), или <c>null</c>, если достаточно просто поставить фокус.
        /// </param>
        private void FocusGridAttempt(int attempt, InspectorNavigationKey? pendingKey = null)
        {
            if (!_opened)
                return;

            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_opened)
                        return;

                    // Пока окно не активно, Focus() не передаёт клавиатурный фокус —
                    // активируем окно; активация завершится позже, поэтому ниже
                    // предусмотрен повтор.
                    if (!IsActive)
                        Activate();

                    if (attempt == 0)
                    {
                        if (pendingKey is { } key)
                        {
                            var target = ProcessInspectorFocusPolicy.ResolveTargetIndex(
                                _grid.SelectedIndex, _grid.ItemCount, key);
                            if (target >= 0)
                                _grid.SelectedIndex = target;
                        }
                        // Первая строка становится выбранной, чтобы стрелки и Enter
                        // работали сразу после открытия окна.
                        else if ((_grid.SelectedItems?.Count ?? 0) == 0 && _grid.ItemCount > 0)
                        {
                            _grid.SelectedIndex = 0;
                        }
                    }

                    // issue #134 (1.0.1.110): фокусируем контейнер строки, а не
                    // только ListBox. Иначе первое «вниз» остаётся на кнопке «Колонки».
                    FocusFirstItem();

                    var focused = IsGridItemFocused();
                    if (focused)
                    {
                        _gridHadFocus = true;
                        // Пустой список не считается «фокус применён»: после
                        // заполнения данных фокус нужно поставить заново.
                        if (_grid.ItemCount > 0)
                            _focusApplied = true;
                    }

                    if (ProcessInspectorFocusPolicy.ShouldRetryFocus(focused, attempt))
                        FocusGridAttempt(attempt + 1, pendingKey);
                },
                DispatcherPriority.Input);
        }

        /// <summary>
        /// Issue #134 (1.0.1.110): фокус находится в контейнере строки списка,
        /// а не на внешней оболочке <see cref="ListBox"/>. Оболочка не даёт
        /// навигации по строкам, поэтому её нельзя считать успешной установкой фокуса.
        /// </summary>
        private bool IsGridItemFocused()
        {
            if (_grid.ItemCount > 0)
            {
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
                return focused is ListBoxItem
                    || focused?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null;
            }

            return _grid.IsKeyboardFocusWithin;
        }

        /// <summary>
        /// Issue #134 (1.0.1.110): ставит клавиатурный фокус в первый контейнер
        /// строки. Для пустого списка фокусируется сам <see cref="ListBox"/>,
        /// чтобы стрелка не уводила фокус на соседнюю кнопку.
        /// </summary>
        private void FocusFirstItem()
        {
            if (!_opened)
                return;

            if (_grid.ItemCount > 0)
            {
                if (_grid.SelectedIndex < 0)
                    _grid.SelectedIndex = 0;

                var index = Math.Max(0, _grid.SelectedIndex);
                _grid.ScrollIntoView(_grid.SelectedItem);
                _grid.UpdateLayout();

                if (_grid.ContainerFromIndex(index) is ListBoxItem item)
                {
                    item.Focus(NavigationMethod.Directional);
                    return;
                }
            }

            // Пустой список или контейнер ещё не сгенерирован — фокусируем ListBox;
            // повторная попытка выполнится по таймеру.
            _grid.Focus(NavigationMethod.Directional);
        }

        /// <summary>
        /// issue #134 (1.0.1.104, замечание 7OH): навигационные клавиши работают,
        /// даже если после открытия окна или завершения процесса фокус оказался на
        /// кнопке. Раньше первое нажатие «вниз» лишь переводило фокус на «Колонки».
        /// </summary>
        private void OnWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            var navigation = MapNavigationKey(e.Key);
            if (!ProcessInspectorFocusPolicy.IsNavigationKey(navigation))
                return;
            // Не перехватываем клавиши у полей ввода, если они когда-либо появятся.
            var focusedIsTextInput = e.Source is TextBox or ComboBox;
            // issue #134 (1.0.1.110): проверяем фокус в контейнере строки, а не только
            // внутри ListBox. Фокус на оболочке списка тоже нужно перенаправлять.
            if (!ProcessInspectorFocusPolicy.ShouldRedirectNavigation(
                    IsGridItemFocused(), navigation, focusedIsTextInput))
                return;

            e.Handled = true;
            // issue #134 (1.0.1.107): если окно ещё не открыто/не активно, сначала
            // активируем его, а навигацию выполняем отложенно — активация завершается
            // асинхронно, синхронный Focus() не передаёт фокус.
            if (!_opened || !IsActive)
            {
                Activate();
                Dispatcher.UIThread.Post(
                    () => NavigateGridFromButton(navigation),
                    DispatcherPriority.Input);
                return;
            }

            NavigateGridFromButton(navigation);
        }

        /// <summary>
        /// Переносит фокус в список и выполняет запрошенное перемещение по строкам
        /// (issue #134, 1.0.1.104/1.0.1.107). Для пустого списка фокус всё равно
        /// ставится в список — стрелка не уводит его на соседнюю кнопку.
        /// </summary>
        private void NavigateGridFromButton(InspectorNavigationKey key)
        {
            _gridHadFocus = true;

            // issue #134 (1.0.1.107): Focus() может вернуть false, пока окно не
            // активировалось — тогда повторяем фокус и навигацию отложенно.
            if (!_grid.Focus(NavigationMethod.Directional))
            {
                FocusGridAttempt(0, key);
                return;
            }

            var target = ProcessInspectorFocusPolicy.ResolveTargetIndex(
                _grid.SelectedIndex, _grid.ItemCount, key);
            if (target < 0)
                return;

            _grid.SelectedIndex = target;
            if (_grid.SelectedItem is ProcessRowViewModel row)
                _grid.ScrollIntoView(row);
        }

        /// <summary>Avalonia-клавиша → навигационная клавиша политики (issue #134).</summary>
        private static InspectorNavigationKey MapNavigationKey(Key key) => key switch
        {
            Key.Up => InspectorNavigationKey.Up,
            Key.Down => InspectorNavigationKey.Down,
            Key.PageUp => InspectorNavigationKey.PageUp,
            Key.PageDown => InspectorNavigationKey.PageDown,
            Key.Home => InspectorNavigationKey.Home,
            Key.End => InspectorNavigationKey.End,
            _ => InspectorNavigationKey.None
        };

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
                        // P1b аудита UI: ключ Icon* — векторная иконка активного набора.
                        icon.StartsWith("Icon", StringComparison.Ordinal)
                            ? IconHelper.MakeIcon(icon, UiMetrics.IconControl, "TextPrimaryBrush")
                            : new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
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
                    // P1b: значок-флаг — векторная иконка (известная/неизвестная база).
                    return IconHelper.MakeIcon(
                        row.IsKnown ? "IconCheck" : "IconUnknown",
                        UiMetrics.IconControl,
                        Themes.ThemeBrushes.FromSpec(row.IsKnown ? "@SuccessBrush" : "@TextSecondaryBrush"));

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
        /// Геометрия галочки — та же, что <c>IconCheck</c> в Icons.axaml. Константа в
        /// коде убирает зависимость индикатора от активного набора иконок и ресурсов
        /// приложения (issue #134, 1.0.1.96).
        /// </summary>
        private const string CheckGeometryData = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";

        /// <summary>
        /// Меню «Колонки» (issue #134, доработка ArtyoZ в 1.0.1.84; индикатор
        /// переделан в 1.0.1.96): чек-лист всех колонок инспектора.
        /// <para>
        /// Индикатор — часть <see cref="MenuItem.Header"/>: рамка 16×16 и векторная
        /// галочка внутри. Видимость задаётся ЯВНО из
        /// <see cref="ProcessInspectorViewModel.IsColumnVisible"/> при построении меню
        /// и обновляется в обработчике клика. Ни шаблон Fluent, ни
        /// <c>PART_ToggleIconPresenter</c>, ни <see cref="MenuItem.IsChecked"/>, ни
        /// привязки не участвуют в отрисовке — поэтому галочка видна при любом наборе
        /// иконок и теме.
        /// </para>
        /// </summary>
        private void ShowColumnsMenu(Button? anchor)
        {
            var menu = new ContextMenu();
            menu.Styled(ControlThemes.ModernContextMenu);

            foreach (var info in _vm.GetColumnMenuItems())
            {
                var column = info.Column;
                var header = BuildColumnHeader(info, out var check);
                var item = new MenuItem
                {
                    Header = header,
                    // Индикатор рисует Header: шаблонный чекбокс не нужен.
                    ToggleType = MenuItemToggleType.None,
                    // Меню не закрывается после клика — можно переключить несколько колонок.
                    StaysOpenOnClick = true
                };
                item.Styled(ControlThemes.ModernMenuItem);
                item.Click += (_, _) =>
                {
                    _vm.ToggleColumn(column);
                    var visible = _vm.IsColumnVisible(column);
                    check.IsVisible = visible;
                    UiAutomation.SetName(header, BuildColumnAccessibleName(info.TitleKey, visible));
                };
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
        /// Заголовок пункта меню «Колонки» (issue #134, 1.0.1.96): индикатор 16×16
        /// (рамка + галочка) и подпись. Пустая рамка у скрытой колонки сохраняет
        /// ширину, поэтому подписи пунктов не «прыгают».
        /// </summary>
        private static Grid BuildColumnHeader(ProcessInspectorColumnMenuItem info,
            out Avalonia.Controls.Shapes.Path check)
        {
            var box = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = Themes.ThemeBrushes.Get("TextSecondaryBrush", "#888888"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            check = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(CheckGeometryData),
                Fill = Themes.ThemeBrushes.Get("AccentBrush", "#2D6CDF"),
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = info.IsVisible
            };
            box.Child = check;

            var title = new TextBlock
            {
                Text = LocalizationManager.T(info.TitleKey),
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { box, title }
            };
            Grid.SetColumn(title, 1);
            UiAutomation.SetName(header, BuildColumnAccessibleName(info.TitleKey, info.IsVisible));
            return header;
        }

        /// <summary>Доступное имя пункта: «База: видима» / «База: скрыта».</summary>
        private static string BuildColumnAccessibleName(string titleKey, bool visible) =>
            LocalizationManager.T(titleKey) + ": " + LocalizationManager.T(
                visible ? "ProcessInspector.ColumnVisible" : "ProcessInspector.ColumnHidden");

        /// <summary>
        /// Переставляет выделение ListBox по восстановленному набору ViewModel
        /// (issue #134: восстанавливаются все ранее выбранные строки).
        /// <para>
        /// 1.0.1.102: инкрементальное обновление переиспользует экземпляры строк,
        /// поэтому при штатном автообновлении контрол не трогается вовсе — фокус
        /// и мультивыделение сохраняются. При реальных изменениях выделение
        /// восстанавливается и фокус возвращается в таблицу.
        /// </para>
        /// </summary>
        private void RestoreSelectedRows()
        {
            var selected = _grid.SelectedItems;
            if (selected is null)
                return;

            // issue #134 (1.0.1.103): после завершения процесса фокус возвращаем
            // принудительно, даже если таблица опустела.
            var forceFocus = _restoreFocusAfterKill;
            var hadFocus = forceFocus || _gridHadFocus || IsGridItemFocused();

            var desired = _vm.SelectedRows.ToList();
            var current = selected.Cast<ProcessRowViewModel>().ToList();
            if (!(current.Count == desired.Count && current.All(desired.Contains)))
            {
                selected.Clear();
                foreach (var row in desired)
                {
                    if (_vm.Processes.Contains(row))
                        selected.Add(row);
                }
            }

            if (!hadFocus || IsGridItemFocused())
            {
                if (IsGridItemFocused())
                    _restoreFocusAfterKill = false;
                return;
            }

            // Реальные изменения пересоздали контейнеры — возвращаем фокус и прокрутку.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_gridHadFocus && !IsGridItemFocused() && !forceFocus)
                        return;
                    // issue #134 (1.0.1.107): повторяем до фактического получения фокуса.
                    FocusGridAttempt(0);
                    var row = _vm.SelectedRow ?? _vm.Processes.FirstOrDefault();
                    if (row is not null)
                        _grid.ScrollIntoView(row);
                    _restoreFocusAfterKill = false;
                },
                DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Завершает выбранные процессы и гарантированно возвращает фокус в таблицу
        /// (issue #134, 1.0.1.103). Диалог подтверждения забирает фокус, а при
        /// удалении последней строки список теряет его совсем.
        /// </summary>
        private void KillSelectedAndRestoreFocus()
        {
            _restoreFocusAfterKill = true;
            _gridHadFocus = true;
            _vm.KillSelectedMany();
            RestoreFocusAfterKill();
        }

        /// <summary>
        /// Возврат фокуса в список после завершения процесса (issue #134, 1.0.1.107):
        /// повторяем попытки до фактического получения фокуса, в том числе при пустом
        /// списке и после закрытия диалога подтверждения. Если строки остались без
        /// выделения, помощник сделает первую строку текущей.
        /// </summary>
        private void RestoreFocusAfterKill()
        {
            FocusGridAttempt(0);

            // issue #134 (1.0.1.110): диалог подтверждения закрывается после
            // возврата из KillSelectedMany; повторяем установку фокуса, когда
            // очередь ввода/диалога уже обработана.
            Dispatcher.UIThread.Post(
                () => FocusGridAttempt(0),
                DispatcherPriority.ApplicationIdle);

            // Страховка: если набор строк не изменился и SelectionRestored не придёт,
            // флаг не должен влиять на будущие обновления.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _restoreFocusAfterKill = false;
            };
            timer.Start();
        }

        private void OnGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            // issue #134 (1.0.1.100, замечание 7OH): двойной клик завершает
            // выбранные процессы (с подтверждением), а не активирует базу в
            // главном списке, до которого из модального окна не добраться.
            if (_grid.SelectedItem is ProcessRowViewModel)
                KillSelectedAndRestoreFocus();
        }

        /// <summary>
        /// issue #134 (1.0.1.100, замечание 7OH): Enter завершает выбранные
        /// процессы — то же действие, что и двойной клик по строке. С 1.0.1.102
        /// навигационные клавиши дополнительно приостанавливают автообновление
        /// (короткий debounce), чтобы таблица не «выдёргивалась» из-под пользователя.
        /// </summary>
        private void OnGrid_KeyDown(object? sender, KeyEventArgs e)
        {
            if (IsNavigationKey(e.Key) ||
                (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                _vm.NotifyKeyboardNavigation();
            }

            if (e.Key != Key.Enter)
                return;
            if (_grid.SelectedItem is not ProcessRowViewModel)
                return;
            e.Handled = true;
            KillSelectedAndRestoreFocus();
        }

        /// <summary>Клавиши навигации по таблице (issue #134, 1.0.1.102).</summary>
        private static bool IsNavigationKey(Key key) => key is
            Key.Up or Key.Down or Key.Left or Key.Right or
            Key.PageUp or Key.PageDown or Key.Home or Key.End;
    }
}
#endif
