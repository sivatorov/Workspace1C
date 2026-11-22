using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Инспектор процессов 1С» (0.3.9.93, Windows/WPF): таблица всех запущенных
    /// процессов платформы 1С (база/режим/пользователь/время старта/PID/строка
    /// подключения/значок сопоставления) с автообновлением по таймеру, кнопкой
    /// «Обновить» и завершением выбранных процессов с подтверждением. Двойной клик
    /// по строке и клавиша Enter завершают выбранные процессы (issue #134, 1.0.1.100).
    /// Вся логика — в чистой ViewModel <see cref="ProcessInspectorViewModel"/>.
    /// <para>
    /// issue #134 (доработка ArtyoZ): мультивыделение (Ctrl/Shift) и массовое
    /// завершение, меню «Колонки» для скрытия ненужных колонок (выбор сохраняется
    /// в настройках).
    /// </para>
    /// </summary>
    public partial class ProcessInspectorWindow : Window
    {
        private readonly ProcessInspectorViewModel _vm;

        /// <summary>
        /// Фокус был внутри таблицы (issue #134, 1.0.1.102): после реального изменения
        /// строк его нужно вернуть, а при клике по кнопке — нет.
        /// </summary>
        private bool _gridHadKeyboardFocus;

        /// <summary>
        /// После завершения процесса фокус нужно вернуть в таблицу даже тогда, когда
        /// она опустела или диалог подтверждения сбросил <see cref="_gridHadKeyboardFocus"/>
        /// (issue #134, замечание 7OH от 22.11.2026). Сбрасывается после восстановления
        /// строк или по таймеру-страховке.
        /// </summary>
        private bool _restoreFocusAfterKill;

        /// <summary>Фокус уже успешно установлен при открытии — повторные попытки не нужны.</summary>
        private bool _focusApplied;

        /// <summary>Номер попытки установки фокуса при открытии (issue #134).</summary>
        private int _focusAttempts;

        /// <param name="infobases">Все базы списка (для сопоставления командных строк).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public ProcessInspectorWindow(IEnumerable<Infobase> infobases, Action<Infobase> openBase)
        {
            InitializeComponent();

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
                action => Application.Current?.Dispatcher.BeginInvoke(action),
                settings,
                saveSettings);

            DataContext = _vm;
            Title = LocalizationManager.T("ProcessInspector.Title");
            ProcessesGrid.ItemsSource = _vm.Processes;

            // issue #134: выделение восстанавливается для ВСЕХ строк, колонки
            // перестраиваются при изменении набора видимых.
            _vm.SelectionRestored += (_, _) => RestoreSelectedRows();
            _vm.ColumnsChanged += (_, _) => ApplyColumnVisibility();

            ApplyColumnVisibility();

            // issue #134 (1.0.1.104, замечание 7OH): если фокус оказался на кнопке
            // («Колонки» и т.п.), стрелки должны управлять таблицей, а не переводить
            // фокус на соседний элемент. Перехватываем навигационные клавиши на
            // уровне окна и передаём их списку.
            PreviewKeyDown += OnWindow_PreviewKeyDown;

            // issue #134 (1.0.1.102, замечание 7OH): фокус ставится ПОСЛЕ загрузки
            // данных и первого прохода разметки, с повторной попыткой, если окно ещё
            // не активировано. Прежний синхронный Focus() в Loaded не передавал
            // клавиатурный фокус таблице — Ctrl+A не работал без клика мышью.
            ProcessesGrid.GotKeyboardFocus += (_, _) =>
            {
                _gridHadKeyboardFocus = true;
                // Пустая таблица ещё не готова к навигации: фокус будет переставлен
                // при первом заполнении (issue #134, 1.0.1.104).
                if (ProcessesGrid.Items.Count > 0)
                    _focusApplied = true;
            };
            // Фокус ушёл из таблицы (например, клик по кнопке) — не возвращаем его
            // принудительно при автообновлении.
            ProcessesGrid.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!ProcessesGrid.IsKeyboardFocusWithin)
                        _gridHadKeyboardFocus = false;
                }),
                DispatcherPriority.Background);
            Deactivated += (_, _) => _gridHadKeyboardFocus = false;
            Activated += (_, _) =>
            {
                if (ProcessesGrid.IsKeyboardFocusWithin)
                    _gridHadKeyboardFocus = true;
                if (!_focusApplied)
                    QueueGridFocus();
            };
            ContentRendered += (_, _) => QueueGridFocus();
            // Данные приходят асинхронно (WMI): ставим фокус и по первому заполнению.
            _vm.Processes.CollectionChanged += (_, e) =>
            {
                if (!_focusApplied && e.Action == NotifyCollectionChangedAction.Add)
                    QueueGridFocus();
            };

            Closed += (_, _) => _vm.Dispose();
        }

        /// <summary>
        /// Отложенная установка фокуса в таблицу процессов (issue #134, 1.0.1.102):
        /// после загрузки данных и первого прохода разметки, а не синхронно в Loaded.
        /// </summary>
        private void QueueGridFocus() =>
            Dispatcher.BeginInvoke(new Action(FocusProcessesGrid), DispatcherPriority.Input);

        private void FocusProcessesGrid()
        {
            if (!IsLoaded)
                return;

            ProcessesGrid.Focus();
            Keyboard.Focus(ProcessesGrid);

            if (ProcessesGrid.IsKeyboardFocusWithin && ProcessesGrid.Items.Count > 0)
            {
                _gridHadKeyboardFocus = true;
                _focusApplied = true;
            }

            // Первая строка становится текущей, чтобы стрелки и Enter работали сразу.
            if (_vm.SelectedRows.Count == 0 && ProcessesGrid.Items.Count > 0)
            {
                ProcessesGrid.SelectedIndex = 0;
                SetCurrentRow(ProcessesGrid.Items[0] as ProcessRowViewModel);
            }

            // Окно могло быть ещё не активировано — повторяем попытку после активации.
            if (!_focusApplied && _focusAttempts++ < 10)
            {
                var timer = new DispatcherTimer(DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(120)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    FocusProcessesGrid();
                };
                timer.Start();
            }
        }

        /// <summary>
        /// issue #134 (1.0.1.104, замечание 7OH): навигационные клавиши работают,
        /// даже если после открытия окна или завершения процесса фокус оказался на
        /// кнопке. Раньше первое нажатие «вниз» лишь переводило фокус на «Колонки»,
        /// и таблица не реагировала.
        /// </summary>
        private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ProcessesGrid.IsKeyboardFocusWithin)
                return;
            if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End))
                return;
            if (ProcessesGrid.Items.Count == 0)
                return;
            // Не перехватываем клавиши у полей ввода, если они когда-либо появятся.
            if (Keyboard.FocusedElement is TextBoxBase or ComboBox)
                return;

            e.Handled = true;
            NavigateGridFromButton(e.Key);
        }

        /// <summary>
        /// Переносит фокус в таблицу и выполняет запрошенное перемещение по строкам
        /// (issue #134, 1.0.1.104). Если строка ещё не выбрана, «вниз»/«вверх»
        /// выбирают первую, «End» — последнюю.
        /// </summary>
        private void NavigateGridFromButton(Key key)
        {
            ProcessesGrid.Focus();
            Keyboard.Focus(ProcessesGrid);
            _gridHadKeyboardFocus = true;

            var count = ProcessesGrid.Items.Count;
            if (count == 0)
                return;

            var current = ProcessesGrid.SelectedIndex;
            var target = key switch
            {
                Key.Home => 0,
                Key.End => count - 1,
                Key.Up => current > 0 ? current - 1 : 0,
                Key.PageUp => Math.Max(0, (current < 0 ? 0 : current) - 10),
                Key.PageDown => Math.Min(count - 1, (current < 0 ? 0 : current) + 10),
                _ => current < 0 ? 0 : Math.Min(count - 1, current + 1)
            };

            ProcessesGrid.SelectedIndex = target;
            SetCurrentRow(ProcessesGrid.Items[target] as ProcessRowViewModel);
        }

        /// <summary>
        /// Делает строку текущей (<c>CurrentItem</c> + <c>CurrentCell</c>), не меняя
        /// выделение: при мультивыделении трогать <c>SelectedItem</c> нельзя
        /// (issue #134, 1.0.1.102).
        /// </summary>
        private void SetCurrentRow(ProcessRowViewModel? row)
        {
            if (row is null || !ProcessesGrid.Items.Contains(row))
                return;

            ProcessesGrid.CurrentItem = row;
            var column = ProcessesGrid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible);
            if (column is not null)
                ProcessesGrid.CurrentCell = new DataGridCellInfo(row, column);
            ProcessesGrid.ScrollIntoView(row);
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnKillProcess_Click(object sender, RoutedEventArgs e) => KillSelectedAndRestoreFocus();

        /// <summary>
        /// Завершает выбранные процессы и гарантированно возвращает фокус в таблицу
        /// (issue #134, 1.0.1.103). Диалог подтверждения на время забирает фокус,
        /// а при удалении последней строки DataGrid теряет его совсем — из-за этого
        /// клавиатурная навигация «умирала» до следующего клика мышью.
        /// </summary>
        private void KillSelectedAndRestoreFocus()
        {
            _restoreFocusAfterKill = true;
            _gridHadKeyboardFocus = true;
            _vm.KillSelectedMany();
            QueueGridFocusAfterKill();
        }

        /// <summary>
        /// Немедленная и отложенная установка фокуса после завершения процесса:
        /// таблица может быть пустой, но фокус всё равно должен остаться в ней.
        /// </summary>
        private void QueueGridFocusAfterKill()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                    return;
                // Если после завершения процесса строки остались, но выделение
                // сброшено, ставим текущую строку — иначе первое нажатие «вниз»
                // снова уводит фокус на кнопку (issue #134, 1.0.1.104).
                if (ProcessesGrid.Items.Count > 0 && ProcessesGrid.SelectedIndex < 0)
                {
                    ProcessesGrid.SelectedIndex = 0;
                    SetCurrentRow(ProcessesGrid.Items[0] as ProcessRowViewModel);
                }
                ProcessesGrid.Focus();
                Keyboard.Focus(ProcessesGrid);
            }), DispatcherPriority.Input);

            // Страховка: если автообновление не изменило набор строк и событие
            // SelectionRestored не придёт, флаг не должен влиять на будущие обновления.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _restoreFocusAfterKill = false;
            };
            timer.Start();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // issue #134: мультивыделение DataGrid → SelectedRows ViewModel.
            _vm.SetSelectedRows(ProcessesGrid.SelectedItems.OfType<ProcessRowViewModel>().ToList());
        }

        private void OnGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // issue #134 (1.0.1.100, замечание 7OH): двойной клик завершает
            // выбранные процессы (с подтверждением), а не активирует базу в
            // главном списке, до которого из модального окна не добраться.
            if (ProcessesGrid.SelectedItem is ProcessRowViewModel)
                KillSelectedAndRestoreFocus();
        }

        /// <summary>
        /// issue #134 (1.0.1.100, замечание 7OH): Enter завершает выбранные
        /// процессы — то же действие, что и двойной клик по строке. С 1.0.1.102
        /// навигационные клавиши дополнительно приостанавливают автообновление
        /// (короткий debounce), чтобы таблица не «выдёргивалась» из-под пользователя.
        /// </summary>
        private void OnGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (IsNavigationKey(e.Key) ||
                (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                _vm.NotifyKeyboardNavigation();
            }

            if (e.Key != Key.Enter)
                return;
            if (ProcessesGrid.SelectedItem is not ProcessRowViewModel)
                return;
            e.Handled = true;
            KillSelectedAndRestoreFocus();
        }

        /// <summary>Клавиши навигации по таблице (issue #134, 1.0.1.102).</summary>
        private static bool IsNavigationKey(Key key) => key is
            Key.Up or Key.Down or Key.Left or Key.Right or
            Key.PageUp or Key.PageDown or Key.Home or Key.End;

        /// <summary>
        /// Геометрия галочки — та же, что <c>IconCheck</c> в Icons.xaml. Держим её
        /// константой в коде: индикатор не должен зависеть от активного набора иконок,
        /// ресурсов приложения и разрешения геометрии по ключу (issue #134, 1.0.1.96).
        /// </summary>
        private const string CheckGeometryData = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";

        /// <summary>
        /// Меню «Колонки» (issue #134; индикатор переделан в 1.0.1.96).
        /// <para>
        /// Чек-лист всех колонок инспектора. Индикатор — часть <see cref="MenuItem.Header"/>:
        /// рамка 16×16 и векторная галочка внутри, видимость задаётся ЯВНО из
        /// <see cref="ProcessInspectorViewModel.IsColumnVisible"/> при построении меню и
        /// обновляется в обработчике клика. Ни шаблон <c>ModernMenuItem</c>, ни
        /// <see cref="MenuItem.IsChecked"/>, ни привязки не участвуют в отрисовке —
        /// поэтому галочка видна при любом наборе иконок и теме.
        /// </para>
        /// </summary>
        private void OnColumns_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Style = TryFindResource("ModernContextMenu") as Style,
                PlacementTarget = ColumnsButton,
                Placement = PlacementMode.Bottom
            };

            foreach (var info in _vm.GetColumnMenuItems())
            {
                var column = info.Column;
                var header = BuildColumnHeader(info, out var check);
                var item = new MenuItem
                {
                    Header = header,
                    Style = TryFindResource("ModernMenuItem") as Style,
                    // Индикатор рисует Header: шаблонный чекбокс не нужен и только
                    // добавлял бы второй (невидимый) индикатор.
                    IsCheckable = false,
                    // Меню не закрывается после клика — можно переключить несколько колонок.
                    StaysOpenOnClick = true
                };
                item.Click += (_, _) =>
                {
                    _vm.ToggleColumn(column);
                    var visible = _vm.IsColumnVisible(column);
                    check.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                    UiAutomation.SetName(header, BuildColumnAccessibleName(info.TitleKey, visible));
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        /// <summary>
        /// Заголовок пункта меню «Колонки» (issue #134, 1.0.1.96): индикатор 16×16
        /// (рамка + галочка) и подпись. У скрытой колонки остаётся пустая рамка того же
        /// размера, поэтому подписи пунктов не сдвигаются.
        /// </summary>
        private static StackPanel BuildColumnHeader(ProcessInspectorColumnMenuItem info,
            out System.Windows.Shapes.Path check)
        {
            var box = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = Themes.WpfBrushes.Get("TextSecondaryBrush")
                    ?? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            check = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(CheckGeometryData),
                Fill = Themes.WpfBrushes.Get("AccentBrush")
                    ?? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x2D, 0x6C, 0xDF)),
                Width = 12,
                Height = 12,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = info.IsVisible ? Visibility.Visible : Visibility.Hidden
            };
            box.Child = check;

            var title = new TextBlock
            {
                Text = LocalizationManager.T(info.TitleKey),
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { box, title }
            };
            UiAutomation.SetName(header, BuildColumnAccessibleName(info.TitleKey, info.IsVisible));
            return header;
        }

        /// <summary>Доступное имя пункта: «База: видима» / «База: скрыта».</summary>
        private static string BuildColumnAccessibleName(string titleKey, bool visible) =>
            LocalizationManager.T(titleKey) + ": " + LocalizationManager.T(
                visible ? "ProcessInspector.ColumnVisible" : "ProcessInspector.ColumnHidden");

        /// <summary>
        /// Применяет видимость колонок DataGrid по набору скрытых (issue #134).
        /// Порядок колонок совпадает с разметкой окна: база, режим, пользователь,
        /// время запуска, PID, строка подключения, значок.
        /// </summary>
        private void ApplyColumnVisibility()
        {
            var order = new[]
            {
                ProcessInspectorColumn.Base,
                ProcessInspectorColumn.Mode,
                ProcessInspectorColumn.User,
                ProcessInspectorColumn.StartTime,
                ProcessInspectorColumn.Pid,
                ProcessInspectorColumn.Connection,
                ProcessInspectorColumn.Known
            };

            for (var i = 0; i < order.Length && i < ProcessesGrid.Columns.Count; i++)
            {
                ProcessesGrid.Columns[i].Visibility = _vm.IsColumnVisible(order[i])
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Переставляет выделение DataGrid по восстановленному набору ViewModel
        /// (issue #134: восстанавливаются все ранее выбранные строки).
        /// <para>
        /// 1.0.1.102: инкрементальное обновление переиспользует экземпляры строк,
        /// поэтому при штатном автообновлении контрол не трогается вовсе — фокус,
        /// текущая ячейка и мультивыделение сохраняются. При реальных изменениях
        /// выделение восстанавливается и фокус возвращается в таблицу.
        /// </para>
        /// </summary>
        private void RestoreSelectedRows()
        {
            if (ProcessesGrid.SelectedItems is null)
                return;

            // issue #134 (1.0.1.103): после завершения процесса фокус возвращаем
            // принудительно, даже если таблица опустела и _gridHadKeyboardFocus
            // был сброшен диалогом подтверждения.
            var forceFocus = _restoreFocusAfterKill;
            var hadFocus = forceFocus || _gridHadKeyboardFocus || ProcessesGrid.IsKeyboardFocusWithin;

            var desired = _vm.SelectedRows.ToList();
            var current = ProcessesGrid.SelectedItems.OfType<ProcessRowViewModel>().ToList();
            if (!(current.Count == desired.Count && current.All(desired.Contains)))
            {
                ProcessesGrid.SelectedItems.Clear();
                foreach (var row in desired)
                {
                    if (_vm.Processes.Contains(row))
                        ProcessesGrid.SelectedItems.Add(row);
                }
            }

            if (!hadFocus || ProcessesGrid.IsKeyboardFocusWithin)
            {
                if (ProcessesGrid.IsKeyboardFocusWithin)
                    _restoreFocusAfterKill = false;
                return;
            }

            // Реальные изменения пересоздали контейнеры — возвращаем фокус и текущую строку.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_gridHadKeyboardFocus && !ProcessesGrid.IsKeyboardFocusWithin && !forceFocus)
                    return;
                ProcessesGrid.Focus();
                Keyboard.Focus(ProcessesGrid);
                SetCurrentRow(_vm.SelectedRow ?? ProcessesGrid.Items.OfType<ProcessRowViewModel>().FirstOrDefault());
                _restoreFocusAfterKill = false;
            }), DispatcherPriority.Input);
        }
    }
}
