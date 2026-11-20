using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    /// по строке с известной базой — переход к базе в главном окне (FindInList-механика).
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
            Closed += (_, _) => _vm.Dispose();
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnKillProcess_Click(object sender, RoutedEventArgs e) => _vm.KillSelectedMany();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // issue #134: мультивыделение DataGrid → SelectedRows ViewModel.
            _vm.SetSelectedRows(ProcessesGrid.SelectedItems.OfType<ProcessRowViewModel>().ToList());
        }

        private void OnGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessRowViewModel row)
                _vm.OpenBase(row);
        }

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
        /// </summary>
        private void RestoreSelectedRows()
        {
            if (ProcessesGrid.SelectedItems is null)
                return;

            var desired = _vm.SelectedRows.ToList();
            var current = ProcessesGrid.SelectedItems.OfType<ProcessRowViewModel>().ToList();
            if (current.Count == desired.Count && current.All(desired.Contains))
                return;

            ProcessesGrid.SelectedItems.Clear();
            foreach (var row in desired)
            {
                if (_vm.Processes.Contains(row))
                    ProcessesGrid.SelectedItems.Add(row);
            }
        }
    }
}
