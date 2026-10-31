using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Импорт из кластера 1С» (0.3.9.174, Windows/WPF; issue #27): импорт
    /// информационных баз из кластера сервера 1С через утилиту rac. Параметры
    /// подключения (адрес/порт/логин/пароль) берутся из выбранной записи справочника
    /// «Серверы 1С» (вместо четырёх полей — список выбора); пароль живёт только
    /// в памяти окна (на диск не пишется, см. решения планирования цикла
    /// 0.3.9.172–0.3.9.175). Выбор кластера, чеклист найденных баз с пометкой
    /// дубликатов, сводка «будет добавлено» и импорт отмеченных баз. Вся логика —
    /// в чистой ViewModel <see cref="ClusterImportViewModel"/>; окно — тонкая
    /// обёртка: источник серверов (<see cref="IServerListStore"/>), подстановка
    /// выбранного сервера и автоподключение при открытии из окна «Серверы 1С».
    /// После <c>ShowDialog()==true</c> выбранные базы доступны через
    /// <see cref="SelectedBases"/>.
    /// </summary>
    public partial class ClusterImportWindow : Window
    {
        private readonly IRacClient _rac;
        private readonly ClusterImportViewModel _vm;
        private readonly IInfobaseRepository _repository;

        /// <param name="rac">Клиент rac (кластеры, «cluster info», базы кластера).</param>
        /// <param name="repository">Репозиторий настроек (адрес/порт/логин RAS сохраняются при успешном подключении).</param>
        /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для пометки дубликатов.</param>
        /// <param name="initialServer">
        /// Сервер, открытый из окна «Серверы 1С» (issue #27): подставляется в список
        /// выбора; при <paramref name="autoConnect"/> подключение выполняется сразу.
        /// Null — обычное открытие из меню «Утилиты» (без автоподключения).
        /// </param>
        /// <param name="autoConnect">Выполнить «Подключиться» сразу после показа.</param>
        /// <param name="initialClusterId">
        /// Кластер, выбранный в окне «Серверы 1С» (issue #25): при автоподключении
        /// выбирается он (если присутствует среди кластеров сервера). Null — автовыбор
        /// единственного кластера, при нескольких — подсказка «выберите кластер».
        /// </param>
        public ClusterImportWindow(
            IRacClient rac,
            IInfobaseRepository repository,
            IReadOnlyList<Infobase> existingInfobases,
            ManagementServer? initialServer = null,
            bool autoConnect = false,
            Guid? initialClusterId = null)
        {
            InitializeComponent();
            _rac = rac;
            _repository = repository;

            _vm = new ClusterImportViewModel(
                rac,
                existingInfobases,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            // Справочник «Серверы 1С» — источник подключения (issue #27); при пустом
            // списке в разметке подсказка и «Подключиться» недоступна.
            _vm.AvailableServers = AppServices.GetRequiredService<IServerListStore>().Load();

            DataContext = _vm;
            Title = LocalizationManager.T("ClusterImport.Title");
            BasesGrid.ItemsSource = _vm.Rows;

            _vm.PropertyChanged += OnVmPropertyChanged;
            // Подсказка над кнопками зависит и от числа строк (issue #41, 1.0.0.61).
            System.Collections.Specialized.NotifyCollectionChangedEventHandler onRows = (_, _) => UpdateCommandStates();
            _vm.Rows.CollectionChanged += onRows;
            Closed += (_, _) =>
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.Rows.CollectionChanged -= onRows;
            };
            UpdateSummary();
            UpdateCommandStates();

            // Открытие из окна «Серверы 1С»: выбранный сервер + автоподключение.
            if (initialServer is not null)
                _ = _vm.InitializeFromServer(initialServer, autoConnect, initialClusterId);
            else
                ConnectButton.IsEnabled = _vm.HasAvailableServers;
        }

        /// <summary>
        /// Результат импорта: отмеченные новые базы (без дубликатов).
        /// Читается после <c>ShowDialog() == true</c>.
        /// </summary>
        public IReadOnlyList<Infobase> SelectedBases => _vm.SelectedBases;

        private async void OnConnect_Click(object sender, RoutedEventArgs e)
        {
            // Пароль приходит из выбранной записи справочника (issue #27) — PasswordBox нет.
            // Исключения не теряются (аудит гонок): ConnectAsync глотает ошибки rac,
            // но сбои сохранения настроек и прочие сбои показываем пользователю.
            try
            {
                await _vm.ConnectAsync();
                if (_vm.Clusters.Count > 0)
                    SaveRacSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnLoadBases_Click(object sender, RoutedEventArgs e) =>
            _vm.LoadBasesCommand.Execute(null);

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectAllCommand.Execute(null);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectNoneCommand.Execute(null);

        /// <summary>
        /// Переключатель «Создавать группу по имени кластера»: строки строятся с группой
        /// из текущего значения <see cref="ClusterImportViewModel.UseClusterGrouping"/>,
        /// поэтому после смены значения чеклист перечитывается (перестроение строк).
        /// </summary>
        private void OnGroupByCluster_Toggled(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _vm.Clusters.Count == 0 || _vm.SelectedClusterId is null)
                return;
            _vm.LoadBasesCommand.Execute(null);
        }

        private void OnImport_Click(object sender, RoutedEventArgs e)
        {
            // Проверка выбора ДО запуска команды (аудит гонок, синхронно с Avalonia-версией):
            // команда выполняется только когда есть отмеченные новые базы.
            if (_vm.ReadyToImportCount == 0)
            {
                MessageBox.Show(
                    LocalizationManager.T("Selection.NothingSelected"),
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _vm.ImportCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterImportViewModel.IsBusy):
                case nameof(ClusterImportViewModel.HasAvailableServers):
                case nameof(ClusterImportViewModel.SelectedServer):
                    // Смена сервера сбрасывает состояние подключения (issue #25):
                    // «Подключиться» снова доступна, прогресс скрыт.
                    UpdateCommandStates();
                    break;
                case nameof(ClusterImportViewModel.SelectedClusterId):
                    // Автовыбор кластера после подключения (issue #25): пересчёт кнопок
                    // и индикатора занятости, чтобы окно не «зависло» в состоянии после
                    // автоподключения при единственном кластере (аналог Avalonia-версии).
                    // Комбобокс синхронизируется сам — двухсторонняя привязка SelectedValue.
                    UpdateCommandStates();
                    break;
                case nameof(ClusterImportViewModel.ReadyToImportCount):
                case nameof(ClusterImportViewModel.CheckedCount):
                case nameof(ClusterImportViewModel.DuplicateCount):
                    UpdateSummary();
                    UpdateCommandStates();
                    break;
            }
        }

        /// <summary>
        /// Единый пересчёт кнопок и индикатора занятости из состояния VM (issue #25):
        /// вызывается при изменении флага занятости, наполнении справочника и смене
        /// сервера. «Подключиться» доступна при выбранном сервере и неактивном запросе;
        /// «Импортировать» — только при отмеченных новых базах.
        /// </summary>
        private void UpdateCommandStates()
        {
            var busy = _vm.IsBusy;
            ConnectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            LoadBasesButton.IsEnabled = !busy;
            SelectAllButton.IsEnabled = !busy;
            SelectNoneButton.IsEnabled = !busy;
            ImportButton.IsEnabled = !busy && _vm.ReadyToImportCount > 0;
            ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            // Причина недоступности кнопки «Импортировать» — над кнопками.
            var hint = _vm.ActionHint;
            ActionHintText.Text = hint;
            ActionHintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateSummary()
        {
            SummaryText.Text = string.Format(
                LocalizationManager.T("ClusterImport.SummaryFormat"), _vm.ReadyToImportCount);

            var duplicates = _vm.DuplicateCount;
            DuplicateSummaryText.Text = duplicates > 0
                ? string.Format(LocalizationManager.T("ClusterImport.DuplicatesFormat"), duplicates)
                : string.Empty;
        }

        /// <summary>
        /// Сохраняет адрес/порт/логин после успешного подключения (без пароля — см.
        /// решения планирования; пароль rac не хранится на диске).
        /// </summary>
        private void SaveRacSettings()
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.RacServerAddress = _vm.ServerAddress;
                settings.RacServerPort = _vm.ServerPort;
                settings.RacUserName = _vm.UserName;
                _repository.SaveSettings(settings);
            }
            catch
            {
                // Сохранение настроек не критично для работы окна.
            }
        }

    }
}