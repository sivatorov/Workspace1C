using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        /// Меню «Колонки» (issue #134): чек-лист всех колонок инспектора.
        /// Скрытые колонки не отображаются, выбор сохраняется в настройках.
        /// </summary>
        private void OnColumns_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Style = TryFindResource("ModernContextMenu") as Style,
                PlacementTarget = ColumnsButton,
                Placement = PlacementMode.Bottom
            };

            foreach (var info in ProcessInspectorViewModel.AllColumns)
            {
                var column = info.Column;
                var item = new MenuItem
                {
                    Header = LocalizationManager.T(info.TitleKey),
                    Style = TryFindResource("ModernMenuItem") as Style,
                    IsCheckable = true,
                    IsChecked = _vm.IsColumnVisible(column)
                };
                item.Click += (_, _) => _vm.SetColumnVisible(column, item.IsChecked);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

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
