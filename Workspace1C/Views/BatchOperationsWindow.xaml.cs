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

        /// <summary>ViewModel окна — для чтения шаблона/пути/вида после закрытия (issue #92).</summary>
        public BatchOperationsViewModel ViewModel => _vm;

        public BatchOperationsWindow(IEnumerable<Infobase> infobases, BatchOperationKind kind,
            string? initialDirectory = null, string? template = null)
        {
            InitializeComponent();
            _vm = new BatchOperationsViewModel(infobases, kind);
            // Сохранённые каталог и шаблон массовой выгрузки (issue #92).
            if (!string.IsNullOrWhiteSpace(initialDirectory) && _vm.IsDumpKind)
                _vm.PathText = initialDirectory!.Trim();
            if (!string.IsNullOrWhiteSpace(template) && _vm.IsDumpKind)
                _vm.Template = template!.Trim();
            DataContext = _vm;

            TemplateLabel.Text = LocalizationManager.T("BatchOps.TemplateLabel");
            KindDtRadio.Content = LocalizationManager.T("BatchOps.KindDtRadio");
            KindCfRadio.Content = LocalizationManager.T("BatchOps.KindCfRadio");
            LogHeader.Text = LocalizationManager.T("BatchOps.Log");
            RunButtonText.Text = LocalizationManager.T("BatchOps.Execute");

            UpdateKindDependentUi();
            ItemsList.ItemsSource = _vm.Items;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        /// <summary>Обновляет заголовок, подсказку и подпись поля пути по текущему виду операции.</summary>
        private void UpdateKindDependentUi()
        {
            Title = LocalizationManager.T(_vm.Kind switch
            {
                BatchOperationKind.DumpDt => "BatchOps.TitleDumpDt",
                BatchOperationKind.DumpCf => "BatchOps.TitleDumpCf",
                _ => "BatchOps.TitleLoadCf"
            });
            HintText.Text = LocalizationManager.T(_vm.Kind switch
            {
                BatchOperationKind.DumpDt => "BatchOps.HintDumpDt",
                BatchOperationKind.DumpCf => "BatchOps.HintDumpCf",
                _ => "BatchOps.HintLoadCf"
            });
            PathLabel.Text = _vm.PathLabel;
            // Опции шаблона и DT/CF видны только у выгрузок (issue #92).
            DumpOptionsPanel.Visibility = _vm.IsDumpKind
                ? Visibility.Visible : Visibility.Collapsed;
            _suppressKindEvents = true;
            KindDtRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpDt;
            KindCfRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpCf;
            _suppressKindEvents = false;
        }

        private bool _suppressKindEvents;

        private void OnKindDt_Checked(object sender, RoutedEventArgs e) => OnKindChecked(BatchOperationKind.DumpDt);

        private void OnKindCf_Checked(object sender, RoutedEventArgs e) => OnKindChecked(BatchOperationKind.DumpCf);

        private void OnKindChecked(BatchOperationKind kind)
        {
            if (_suppressKindEvents || _vm.Kind == kind || _vm is null)
                return;
            _vm.Kind = kind;
            UpdateKindDependentUi();
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
            else if (e.PropertyName == nameof(BatchOperationsViewModel.PathLabel))
            {
                PathLabel.Text = _vm.PathLabel;
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
            if (_vm.IsDumpKind)
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
