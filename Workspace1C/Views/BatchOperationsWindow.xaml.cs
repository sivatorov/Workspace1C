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
            string? initialDirectory = null, string? template = null,
            string? timestampFormat = null,
            Func<Infobase, System.Threading.CancellationToken, Task<DbmsBackupProfile?>>? dbmsProfileResolver = null)
        {
            InitializeComponent();
            _vm = new BatchOperationsViewModel(infobases, kind);
            // Автоподстановка данных СУБД из кластера (rac) для серверных баз без
            // профиля (issue #92, v100114); null — старое поведение.
            _vm.DbmsProfileResolver = dbmsProfileResolver;
            // Сохранённые каталог и шаблон массовой выгрузки (issue #92).
            if (!string.IsNullOrWhiteSpace(initialDirectory) && _vm.IsDumpKind)
                _vm.PathText = initialDirectory!.Trim();
            if (!string.IsNullOrWhiteSpace(template) && _vm.IsDumpKind)
                _vm.Template = template!.Trim();
            // Формат метки времени токена {Дата} — из настроек приложения
            // «Формат даты в имени файла выгрузки» (issue #92, 1.0.1.11).
            _vm.TimestampFormat = timestampFormat;
            // Сервис подготовки «Перед операцией» (issue #92, v1.0.1.21); null —
            // rac-клиент недоступен, опции подготовки сообщат об этом в журнал.
            _vm.PreparationService = BatchOpsPreparationService.CreateDefault(
                line => _vm.WriteLog(line));
            DataContext = _vm;

            TemplateLabel.Text = LocalizationManager.T("BatchOps.TemplateLabel");
            KindDtRadio.Content = LocalizationManager.T("BatchOps.KindDtRadio");
            KindCfRadio.Content = LocalizationManager.T("BatchOps.KindCfRadio");
            // Движок выгрузки (issue #92, v1.0.1.28).
            EngineLabel.Text = LocalizationManager.T("BatchOps.EngineLabel");
            EngineIbcmdRadio.Content = LocalizationManager.T("BatchOps.EngineIbcmd");
            EngineDesignerRadio.Content = LocalizationManager.T("BatchOps.EngineDesigner");
            LogHeader.Text = LocalizationManager.T("BatchOps.Log");
            RunButtonText.Text = LocalizationManager.T("BatchOps.Execute");

            // Блок «Перед операцией» (issue #92, v1.0.1.21, пожелание aepkugesi).
            PrepareHeader.Text = LocalizationManager.T("BatchOps.Prepare.Header");
            PrepareTerminateCheck.Content = LocalizationManager.T("BatchOps.Prepare.TerminateSessions");
            PrepareDenySessionsCheck.Content = LocalizationManager.T("BatchOps.Prepare.DenyNewSessions");
            PreparePermissionLabel.Text = LocalizationManager.T("BatchOps.Prepare.PermissionCode");
            PrepareDenyJobsCheck.Content = LocalizationManager.T("BatchOps.Prepare.DenyScheduledJobs");
            PrepareHint.Text = LocalizationManager.T("BatchOps.Prepare.Hint");
            // Выгрузка запущенных баз (issue #92, v1.0.1.25).
            AllowRunningText.Text = LocalizationManager.T("BatchOps.AllowRunningBases");
            // Issue #92 (v1.0.1.28): подсказка многострочная — строковый ToolTip
            // не переносится и уезжает за экран.
            AllowRunningCheck.ToolTip = MakeWrappingToolTip(
                LocalizationManager.T("BatchOps.AllowRunningBasesHint"));
            // Поле «Пароль пользователя СУБД» (issue #92, v1.0.1.26).
            SqlPasswordLabel.Text = LocalizationManager.T("BatchOps.SqlPasswordLabel");
            SqlPasswordHint.Text = LocalizationManager.T("BatchOps.SqlPasswordHint");

            // Issue #92 (v1.0.1.29, 7OH: «подсказки для остальных элементов надо тоже
            // сделать с переносами»): ВСЕ строковые ToolTip окна (из XAML через loc:Loc)
            // заменяются многострочными — строковый ToolTip не переносится и уезжает
            // за экран. Строка «Очистить поиск» короткая — остаётся как есть.
            TemplateBox.ToolTip = MakeWrappingToolTip(LocalizationManager.T("BatchOps.TemplateHint"));
            var engineTip = MakeWrappingToolTip(LocalizationManager.T("BatchOps.EngineHint"));
            EngineLabel.ToolTip = engineTip;
            EngineIbcmdRadio.ToolTip = engineTip;
            EngineDesignerRadio.ToolTip = engineTip;
            PreparePermissionBox.ToolTip = MakeWrappingToolTip(
                LocalizationManager.T("BatchOps.Prepare.PermissionCodeHint"));

            UpdateKindDependentUi();
            // issue #92 (v1.0.1.24): список показывает строки ПОД ФИЛЬТРОМ поиска;
            // прогон и выбор идут по полному списку Items в ViewModel.
            ItemsList.ItemsSource = _vm.FilteredItems;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        /// <summary>
        /// Многострочный ToolTip (issue #92, v1.0.1.28): TextBlock с переносом
        /// вместо строки — длинная подсказка не уезжает за экран.
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

        /// <summary>Обновляет заголовок, подсказку и подпись поля пути по текущему виду операции.</summary>
        private void UpdateKindDependentUi()
        {
            // Issue #92 (v1.0.1.31): ключи заголовка/подсказки — единые хелперы VM,
            // заголовок пересчитывается при КАЖДОМ изменении вида DT/CF.
            Title = LocalizationManager.T(BatchOperationsViewModel.TitleKeyForKind(_vm.Kind));
            HintText.Text = LocalizationManager.T(BatchOperationsViewModel.HintKeyForKind(_vm.Kind));
            PathLabel.Text = _vm.PathLabel;
            // Опции шаблона и DT/CF видны только у выгрузок (issue #92).
            DumpOptionsPanel.Visibility = _vm.IsDumpKind
                ? Visibility.Visible : Visibility.Collapsed;
            // Issue #85 (v1.0.1.32): блокировкам/разблокировке сеансов путь не нужен —
            // строка каталога/файла скрывается.
            PathPanel.Visibility = _vm.NeedsPath
                ? Visibility.Visible : Visibility.Collapsed;
            // Issue #85: блок опций «Перед операцией» для Блокировки сеансов — это
            // ПАРАМЕТРЫ блокировки (заголовок меняется); для Разблокировки не нужен.
            var isLockKind = _vm.Kind == BatchOperationKind.LockSessions;
            var isUnlockKind = _vm.Kind == BatchOperationKind.UnlockSessions;
            PreparePanel.Visibility = isUnlockKind
                ? Visibility.Collapsed : Visibility.Visible;
            PrepareHeader.Text = LocalizationManager.T(isLockKind
                ? "BatchOps.LockOptionsHeader"
                : "BatchOps.Prepare.Header");
            PrepareHint.Text = LocalizationManager.T(isLockKind
                ? "BatchOps.Prepare.LockHint"
                : "BatchOps.Prepare.Hint");
            // Блок «Пароль пользователя СУБД» — только для движка ibcmd (issue #92, v1.0.1.28).
            SqlPasswordPanel.Visibility = _vm.IsDbmsPasswordVisible
                ? Visibility.Visible : Visibility.Collapsed;
            // Issue #92 (v1.0.1.34, репорт 7OH: при выключенном RAS выгрузка Конфигуратором
            // падала с rac-ошибкой): фаза подготовки при движке «Конфигуратор» не
            // выполняется вовсе — все rac-опции «Перед операцией» дизейблятся с
            // подсказкой-объяснением (для движка ibcmd опции остаются активными).
            var designerEngine = _vm.IsDumpKind && _vm.DumpEngine == BatchDumpEngine.Designer;
            PrepareTerminateCheck.IsEnabled = !designerEngine;
            PrepareDenyRow.IsEnabled = !designerEngine;
            PrepareDenyJobsCheck.IsEnabled = !designerEngine;
            ToolTipService.SetShowOnDisabled(PrepareTerminateCheck, true);
            ToolTipService.SetShowOnDisabled(PrepareDenyRow, true);
            ToolTipService.SetShowOnDisabled(PrepareDenyJobsCheck, true);
            var designerPrepareTip = designerEngine
                ? MakeWrappingToolTip(LocalizationManager.T("BatchOps.Prepare.DesignerUnavailableHint"))
                : null;
            PrepareTerminateCheck.ToolTip = designerPrepareTip;
            PrepareDenyRow.ToolTip = designerPrepareTip;
            PrepareDenyJobsCheck.ToolTip = designerPrepareTip;
            _suppressKindEvents = true;
            KindDtRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpDt;
            KindCfRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpCf;
            EngineIbcmdRadio.IsChecked = _vm.DumpEngine == BatchDumpEngine.Ibcmd;
            EngineDesignerRadio.IsChecked = _vm.DumpEngine == BatchDumpEngine.Designer;
            _suppressKindEvents = false;
        }

        private bool _suppressKindEvents;

        private void OnKindDt_Checked(object sender, RoutedEventArgs e) => OnKindChecked(BatchOperationKind.DumpDt);

        private void OnKindCf_Checked(object sender, RoutedEventArgs e) => OnKindChecked(BatchOperationKind.DumpCf);

        private void OnEngineIbcmd_Checked(object sender, RoutedEventArgs e) => OnEngineChecked(BatchDumpEngine.Ibcmd);

        private void OnEngineDesigner_Checked(object sender, RoutedEventArgs e) => OnEngineChecked(BatchDumpEngine.Designer);

        private void OnKindChecked(BatchOperationKind kind)
        {
            if (_suppressKindEvents || _vm.Kind == kind || _vm is null)
                return;
            _vm.Kind = kind;
            UpdateKindDependentUi();
        }

        private void OnEngineChecked(BatchDumpEngine engine)
        {
            if (_suppressKindEvents || _vm.DumpEngine == engine || _vm is null)
                return;
            _vm.DumpEngine = engine;
            UpdateKindDependentUi();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchOperationsViewModel.IsRunning))
            {
                // Issue #92 (v1.0.1.31): PropertyChanged может прийти с фонового потока
                // (прогон уходит с UI-потока после ConfigureAwait(false)). Обновление
                // кнопок — ТОЛЬКО на UI-потоке, иначе WPF бросает исключение и состояние
                // «Выполнить/Остановить» залипает до закрытия окна.
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(ApplyRunButtonsState);
                    return;
                }
                ApplyRunButtonsState();
            }
            else if (e.PropertyName == nameof(BatchOperationsViewModel.PathLabel))
            {
                PathLabel.Text = _vm.PathLabel;
            }
            // Issue #92 (v1.0.1.28): заголовок/подсказка обновляются по изменению вида
            // операции в модели — события радиокнопок ненадёжны как единственный триггер
            // (программная установка, восстановление вида из настроек, uncheck соседа).
            else if (e.PropertyName == nameof(BatchOperationsViewModel.Kind))
            {
                UpdateKindDependentUi();
            }
            // Issue #92 (v1.0.1.35, репорт 7OH: «поле появляется только при двойном
            // переключении»): движок выгрузки выставляется вызывающим кодом ПОСЛЕ
            // конструктора окна (восстановление из настроек). Без этой ветки радиокнопки
            // движка не ресинхронизировались: в UI оставался отмеченным «ibcmd», а в
            // модели — «Конфигуратор». Клик по уже отмеченной радиокнопке «ibcmd» события
            // Checked не даёт — движок молча оставался «Конфигуратор» и поле пароля СУБД
            // не появлялось. Любое изменение DumpEngine в модели (программное или от
            // радиокнопки) теперь пересобирает весь видозависимый UI.
            else if (e.PropertyName == nameof(BatchOperationsViewModel.DumpEngine))
            {
                UpdateKindDependentUi();
            }
            else if (e.PropertyName == nameof(BatchOperationsViewModel.IsDbmsPasswordVisible))
            {
                SqlPasswordPanel.Visibility = _vm.IsDbmsPasswordVisible
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Атомарно выставляет состояние кнопок «Выполнить/Остановить/Обзор» и признак
        /// занятости строк по <see cref="BatchOperationsViewModel.IsRunning"/> (issue #92,
        /// v1.0.1.31). Вызывается только с UI-потока.
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

        // issue #92 (v1.0.1.26): «умный» автоскролл журнала — прокручиваем к последней
        // строке ТОЛЬКО пока пользователь стоит на конце списка. Если он прокрутил
        // журнал вверх, чтобы прочитать ошибку, — не дёргаем список, пока он сам не
        // вернётся к концу.
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

        /// <summary>Подключает отслеживание прокрутки журнала (один раз, после загрузки шаблона).</summary>
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
            // Прокрутку содержимого (добавление строк) игнорируем — флаг меняется
            // только при сдвиге СМЕЩЕНИЯ (пользователь / автоскролл).
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

        /// <summary>Кнопка очистки поля поиска (issue #92, v1.0.1.25).</summary>
        private void OnSearchClear_Click(object sender, RoutedEventArgs e)
        {
            _vm.SearchText = string.Empty;
            SearchBox.Focus();
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
            // Пароль PasswordBox не биндится (безопасность) — забираем на старте прогона.
            _vm.SqlPassword = SqlPasswordBox.Password;
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
                // Issue #92 (v1.0.1.31): страховка от потерянного уведомления — состояние
                // кнопок синхронизируется с IsRunning при ЛЮБОМ исходе прогона
                // (успех/ошибка/отмена), «Остановить» гаснет, «Выполнить» возвращается.
                ApplyRunButtonsState();
            }
        }

        private void OnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
