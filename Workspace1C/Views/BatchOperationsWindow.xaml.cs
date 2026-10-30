using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Массовые операции по списку баз» (issue #91, Windows/WPF): выгрузка списка
    /// баз в файлы .dt в выбранный каталог и загрузка конфигурации из .cf по списку баз.
    /// Список баз с флажками, путь (каталог/.cf), последовательный прогон через ibcmd
    /// с построчным логом. Вся логика — в чистой ViewModel
    /// <see cref="BatchOperationsViewModel"/>. Третья фича issue (массовая загрузка
    /// внешних отчётов/обработок) сознательно не включена — отложена.
    /// </summary>
    public partial class BatchOperationsWindow : Window
    {
        private readonly BatchOperationsViewModel _vm;
        private CancellationTokenSource? _cts;

        public BatchOperationsWindow(IEnumerable<Infobase> infobases, BatchOperationKind kind)
        {
            InitializeComponent();
            _vm = new BatchOperationsViewModel(infobases, kind);
            DataContext = _vm;

            Title = LocalizationManager.T(kind == BatchOperationKind.DumpDt
                ? "BatchOps.TitleDumpDt" : "BatchOps.TitleLoadCf");
            HintText.Text = LocalizationManager.T(kind == BatchOperationKind.DumpDt
                ? "BatchOps.HintDumpDt" : "BatchOps.HintLoadCf");
            PathLabel.Text = _vm.PathLabel;
            LogHeader.Text = LocalizationManager.T("BatchOps.Log");
            RunButtonText.Text = LocalizationManager.T("BatchOps.Execute");

            ItemsList.ItemsSource = _vm.Items;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchOperationsViewModel.IsRunning))
            {
                RunButton.IsEnabled = !_vm.IsRunning;
                BrowseButton.IsEnabled = !_vm.IsRunning;
                StopButton.IsEnabled = _vm.IsRunning;
                // Пока идёт прогон, нельзя менять состав выбранных баз.
                foreach (var item in _vm.Items)
                    item.IsBusy = _vm.IsRunning;
            }
        }

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.LogLines.Count > 0)
            {
                var last = _vm.LogLines[_vm.LogLines.Count - 1];
                LogList.ScrollIntoView(last);
            }
        }

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) => _vm.SetAll(true);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) => _vm.SetAll(false);

        private void OnBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.Kind == BatchOperationKind.DumpDt)
            {
                // Каталог выгрузки .dt (OpenFolderDialog — net8+/WPF).
                var dialog = new OpenFolderDialog
                {
                    Title = LocalizationManager.T("BatchOps.BrowseDirectoryTitle")
                };
                if (!string.IsNullOrWhiteSpace(_vm.PathText) && Directory.Exists(_vm.PathText.Trim()))
                    dialog.InitialDirectory = _vm.PathText.Trim();
                if (dialog.ShowDialog(this) == true)
                    _vm.PathText = dialog.FolderName;
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = LocalizationManager.T("BatchOps.BrowseCfTitle"),
                    Filter = "Файл конфигурации (*.cf)|*.cf|Все файлы (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) == true)
                    _vm.PathText = dialog.FileName;
            }
        }

        private async void OnRun_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            try
            {
                await _vm.RunAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Остановка пользователем: строки помечены, окно остаётся открытым.
            }
            catch
            {
                // VM уже пишет ошибки в лог; здесь только страхуемся от падения окна.
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        private void OnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
