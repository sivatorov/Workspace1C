using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Массовая загрузка cf через Конфигуратор» (issue #106, Windows/WPF,
    /// автор aepkugesi): выбор списка баз (группа/мультивыделение — как в пакетных
    /// операциях), файл .cf и опции перед загрузкой (выгрузка базы в .dt, завершение
    /// сеансов, блокировка начала сеансов, блокировка регламентных заданий).
    /// Вся логика — в чистой ViewModel <see cref="MassLoadCfViewModel"/>; выполнение —
    /// <see cref="MassLoadCfService"/> (Конфигуратор /LoadCfg + /UpdateDBCfg).
    /// </summary>
    public partial class MassLoadCfWindow : Window
    {
        private readonly MassLoadCfViewModel _vm;
        private CancellationTokenSource? _cts;

        /// <summary>ViewModel окна — для чтения значений после закрытия (настройки).</summary>
        public MassLoadCfViewModel ViewModel => _vm;

        public MassLoadCfWindow(IEnumerable<Infobase> infobases,
            string? initialCfPath = null, string? dumpDirectory = null,
            string? dumpTemplate = null, string? timestampFormat = null)
        {
            InitializeComponent();
            _vm = new MassLoadCfViewModel(infobases);
            // Сохранённые значения (как в массовых операциях): файл .cf, каталог и
            // шаблон .dt-выгрузки; формат метки времени токена {Дата}.
            if (!string.IsNullOrWhiteSpace(initialCfPath))
                _vm.CfFilePath = initialCfPath!.Trim();
            if (!string.IsNullOrWhiteSpace(dumpDirectory))
                _vm.DumpDirectory = dumpDirectory!.Trim();
            if (!string.IsNullOrWhiteSpace(dumpTemplate))
                _vm.DumpTemplate = dumpTemplate!.Trim();
            _vm.TimestampFormat = timestampFormat;
            // Сервис подготовки «Перед загрузкой» (issue #106 переиспользует
            // инфраструктуру «Перед операцией» issue #92); null — rac-клиент
            // недоступен, базы с включёнными опциями получат понятную ошибку.
            _vm.Service = new MassLoadCfService(
                BatchOpsPreparationService.CreateDefault(line => _vm.WriteLog(line)),
                line => _vm.WriteLog(line));
            DataContext = _vm;

            Title = LocalizationManager.T("MassLoadCf.Title");
            HintText.Text = LocalizationManager.T("MassLoadCf.Hint");
            CfPathLabel.Text = LocalizationManager.T("BatchOps.PathLabelLoadCf");
            DumpDtCheck.Content = LocalizationManager.T("MassLoadCf.DumpDtBefore");
            DumpDirLabel.Text = LocalizationManager.T("BatchOps.PathLabelDumpDt");
            TemplateLabel.Text = LocalizationManager.T("BatchOps.TemplateLabel");
            LogHeader.Text = LocalizationManager.T("BatchOps.Log");
            RunButtonText.Text = LocalizationManager.T("BatchOps.Execute");

            // Блок «Перед загрузкой» (issue #106) — подписи как в блоке
            // «Перед операцией» массовых операций (issue #92).
            PrepareHeader.Text = LocalizationManager.T("MassLoadCf.PrepareHeader");
            PrepareTerminateCheck.Content = LocalizationManager.T("BatchOps.Prepare.TerminateSessions");
            PrepareDenySessionsCheck.Content = LocalizationManager.T("BatchOps.Prepare.DenyNewSessions");
            PreparePermissionLabel.Text = LocalizationManager.T("BatchOps.Prepare.PermissionCode");
            PrepareDenyJobsCheck.Content = LocalizationManager.T("BatchOps.Prepare.DenyScheduledJobs");
            PrepareHint.Text = LocalizationManager.T("MassLoadCf.PrepareHint");
            PreparePermissionBox.ToolTip = MakeWrappingToolTip(
                LocalizationManager.T("BatchOps.Prepare.PermissionCodeHint"));
            TemplateBox.ToolTip = MakeWrappingToolTip(LocalizationManager.T("BatchOps.TemplateHint"));

            UpdateDumpDtUi();
            ItemsList.ItemsSource = _vm.FilteredItems;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        /// <summary>
        /// Многострочный ToolTip (как в окне массовых операций): длинная подсказка
        /// с переносами не уезжает за экран.
        /// </summary>
        private static ToolTip MakeWrappingToolTip(string text) => new()
        {
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            }
        };

        /// <summary>Показ/скрытие блока каталога и шаблона .dt-выгрузки по флажку.</summary>
        private void UpdateDumpDtUi()
        {
            DumpDtPanel.Visibility = Visibility.Visible;
            var visible = _vm.DumpDtBefore ? Visibility.Visible : Visibility.Collapsed;
            DumpDirRow.Visibility = visible;
            DumpTemplateRow.Visibility = visible;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MassLoadCfViewModel.IsRunning))
            {
                // PropertyChanged может прийти с фонового потока (прогон уходит с
                // UI-потока после ConfigureAwait(false)) — обновление кнопок только
                // на UI-потоке (issue #92, v1.0.1.31).
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(ApplyRunButtonsState);
                    return;
                }
                ApplyRunButtonsState();
            }
            else if (e.PropertyName == nameof(MassLoadCfViewModel.DumpDtBefore))
            {
                UpdateDumpDtUi();
            }
        }

        /// <summary>
        /// Атомарно выставляет состояние кнопок «Выполнить/Остановить/Обзор» и признак
        /// занятости строк по <see cref="MassLoadCfViewModel.IsRunning"/>.
        /// Вызывается только с UI-потока.
        /// </summary>
        private void ApplyRunButtonsState()
        {
            RunButton.IsEnabled = !_vm.IsRunning;
            BrowseButton.IsEnabled = !_vm.IsRunning;
            StopButton.IsEnabled = _vm.IsRunning;
            // Пока идёт прогон, нельзя менять состав выбранных баз.
            foreach (var item in _vm.Items)
                item.IsBusy = _vm.IsRunning;
        }

        // «Умный» автоскролл журнала — прокручиваем к последней строке ТОЛЬКО пока
        // пользователь стоит на конце списка (как в окне массовых операций).
        private bool _logStickToBottom = true;
        private ScrollViewer? _logScrollViewer;

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || _vm.LogLines.Count == 0)
                return;

            HookLogScrollViewer();
            if (!_logStickToBottom)
                return;

            LogList.ScrollIntoView(_vm.LogLines[_vm.LogLines.Count - 1]);
        }

        /// <summary>Подключает отслеживание прокрутки журнала (один раз).</summary>
        private void HookLogScrollViewer()
        {
            if (_logScrollViewer is not null)
                return;
            _logScrollViewer = FindDescendantScrollViewer(LogList);
            if (_logScrollViewer is not null)
                _logScrollViewer.ScrollChanged += OnLogScrollChanged;
        }

        private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 && _logScrollViewer is not null)
            {
                _logStickToBottom = _logScrollViewer.VerticalOffset + _logScrollViewer.ViewportHeight
                    >= _logScrollViewer.ExtentHeight - 4;
            }
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject? parent)
        {
            if (parent is null)
                return null;
            if (parent is ScrollViewer sv)
                return sv;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(parent, i));
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>Кнопка очистки поля поиска.</summary>
        private void OnSearchClear_Click(object sender, RoutedEventArgs e)
        {
            _vm.SearchText = string.Empty;
            SearchBox.Focus();
        }

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) => _vm.SetAll(true);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) => _vm.SetAll(false);

        /// <summary>Выбор файла конфигурации .cf.</summary>
        private void OnBrowseCf_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("BatchOps.BrowseCfTitle"),
                Filter = "Файл конфигурации (*.cf)|*.cf|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
                _vm.CfFilePath = dialog.FileName;
        }

        /// <summary>Выбор каталога для .dt-выгрузки перед загрузкой.</summary>
        private void OnBrowseDumpDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = LocalizationManager.T("BatchOps.BrowseDirectoryTitle")
            };
            if (!string.IsNullOrWhiteSpace(_vm.DumpDirectory) && Directory.Exists(_vm.DumpDirectory.Trim()))
                dialog.InitialDirectory = _vm.DumpDirectory.Trim();
            if (dialog.ShowDialog(this) == true)
                _vm.DumpDirectory = dialog.FolderName;
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
                // Страховка от потерянного уведомления — состояние кнопок
                // синхронизируется с IsRunning при ЛЮБОМ исходе прогона.
                ApplyRunButtonsState();
            }
        }

        private void OnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
