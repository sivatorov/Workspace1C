#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Импорт из кластера 1С» (0.3.9.174, Avalonia/Linux; issue #27): импорт
    /// информационных баз из кластера сервера 1С через утилиту rac. Параметры
    /// подключения (адрес/порт/логин/пароль) берутся из выбранной записи справочника
    /// «Серверы 1С» (вместо четырёх полей — список выбора); пароль живёт только
    /// в памяти окна (на диск не пишется, см. решения планирования цикла
    /// 0.3.9.172–0.3.9.175). Выбор кластера, чеклист найденных баз с пометкой
    /// дубликатов, сводка «будет добавлено» и импорт отмеченных баз. Вся логика —
    /// в чистой ViewModel <see cref="ClusterImportViewModel"/>; окно — тонкая
    /// обёртка (аналогично <see cref="ServerMonitorWindow"/> и
    /// <see cref="BaseSelectionWindow"/>): источник серверов
    /// (<see cref="IServerListStore"/>), подстановка выбранного сервера и
    /// автоподключение при открытии из окна «Серверы 1С».
    /// После <c>ShowDialogSync()==true</c> выбранные базы доступны через
    /// <see cref="SelectedBases"/>.
    /// </summary>
    public partial class ClusterImportWindow : ModalWindowBase
    {
        private readonly IRacClient _rac;
        private readonly ClusterImportViewModel _vm;
        private readonly IInfobaseRepository _repository;
        private readonly ComboBox _clusterCombo;
        private readonly ComboBox _serverCombo;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _duplicateSummaryText;
        private TextBlock? _actionHintText;
        private readonly ProgressBar _progressBar;
        private readonly Button _connectButton;
        private readonly Button _loadBasesButton;
        private readonly Button _selectAllButton;
        private readonly Button _selectNoneButton;
        private readonly Button _importButton;
        private readonly Button _closeButton;

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
            AvaloniaXamlLoader.Load(this);

            _rac = rac;
            _repository = repository;

            _vm = new ClusterImportViewModel(
                rac,
                existingInfobases,
                action => Dispatcher.UIThread.Post(action),
                AppServices.GetRequiredService<IServerListStore>().Load());

            Title = LocalizationManager.T("ClusterImport.Title");
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            // Подсказка над кнопкой «Импортировать» зависит и от числа строк.
            System.Collections.Specialized.NotifyCollectionChangedEventHandler onRows = (_, _) =>
                UpdateCommandStates();
            _vm.Rows.CollectionChanged += onRows;
            Closed += (_, _) =>
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.Rows.CollectionChanged -= onRows;
            };

            // ---- Выбор сервера из справочника (issue #27): адрес/порт/логин/пароль
            // заполняются из записи через SelectedServer. ----
            _serverCombo = this.FindControl<ComboBox>("ServerCombo")!;
            _serverCombo.ItemsSource = _vm.AvailableServers;
            _serverCombo.ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = server.Description, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{server.Address}:{server.Port}", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _serverCombo.SelectionChanged += (_, _) =>
            {
                if (_serverCombo.SelectedItem is ManagementServer server)
                    _vm.SelectedServer = server;
            };
            this.FindControl<TextBlock>("ServersEmptyHint")!
                .Bind(IsVisibleProperty, new Binding("!HasAvailableServers"));

            // ---- Выбор кластера: список из VM, синхронизация выбора в обе стороны. ----
            _clusterCombo = this.FindControl<ComboBox>("ClusterCombo")!;
            _clusterCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding("Clusters"));
            _clusterCombo.ItemTemplate = new FuncDataTemplate<RacCluster>((cluster, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = cluster.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"[{cluster.Port}]", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _clusterCombo.SelectionChanged += (_, _) =>
            {
                if (_clusterCombo.SelectedItem is RacCluster cluster)
                    _vm.SelectedClusterId = cluster.Id;
            };

            // ---- Чеклист баз и статусные строки. ----
            this.FindControl<ListBox>("BasesList")!.ItemsSource = _vm.Rows;
            this.FindControl<TextBlock>("StatusText")!
                .Bind(TextBlock.TextProperty, new Binding("StatusText"));
            this.FindControl<TextBlock>("ErrorText")!
                .Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));

            _summaryText = this.FindControl<TextBlock>("SummaryText")!;
            _duplicateSummaryText = this.FindControl<TextBlock>("DuplicateSummaryText")!;
            _progressBar = this.FindControl<ProgressBar>("BusyProgress")!;
            UpdateSummary();

            // ---- Переключатель «Создавать группу по имени кластера»: строки строятся
            // с группой из текущего значения UseClusterGrouping, поэтому после смены
            // значения чеклист перечитывается (перестроение строк). ----
            var groupCheck = this.FindControl<CheckBox>("GroupByClusterCheck")!;
            groupCheck.IsChecked = _vm.UseClusterGrouping;
            groupCheck.IsCheckedChanged += (_, _) =>
            {
                _vm.UseClusterGrouping = groupCheck.IsChecked == true;
                if (_vm.Clusters.Count > 0 && _vm.SelectedClusterId is not null)
                    _vm.LoadBases();
            };

            // ---- Кнопки (паттерн BaseSelectionWindow: строятся в коде, хост — в разметке). ----
            // Пароль приходит из выбранной записи справочника (issue #27) — PasswordBox нет.
            _connectButton = BuildActionButton(LocalizationManager.T("ClusterImport.Connect"), "🔌",
                () => _ = ConnectAndSaveAsync());
            this.FindControl<ContentControl>("ConnectHost")!.Content = _connectButton;

            _loadBasesButton = BuildActionButton(LocalizationManager.T("ClusterImport.LoadBases"), "⟳",
                () => _vm.LoadBases());
            this.FindControl<ContentControl>("LoadHost")!.Content = _loadBasesButton;

            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _selectAllButton = BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"),
                () => _vm.SelectAllCommand.Execute(null));
            _selectNoneButton = BuildSecondaryButton(LocalizationManager.T("Selection.None"),
                () => _vm.SelectNoneCommand.Execute(null));
            bulk.Children.Add(_selectAllButton);
            bulk.Children.Add(_selectNoneButton);
            this.FindControl<ContentControl>("BulkHost")!.Content = bulk;

            // «Импортировать» — единственная кнопка действия (миграция вынесена
            // в отдельное окно «Миграция между кластерами 1С», issue #41).
            _importButton = BuildImportButton();
            this.FindControl<ContentControl>("ImportHost")!.Content = _importButton;

            // «Закрыть» (issue #41): отодвинута дальше, красный шрифт и иконка — по образцу
            // Windows-версии (окно не обязан закрывать только системной кнопкой).
            _closeButton = BuildCloseButton();
            this.FindControl<ContentControl>("CloseHost")!.Content = _closeButton;

            // Открытие из окна «Серверы 1С» (issue #27): выбранный сервер + автоподключение.
            _connectButton.IsEnabled = _vm.HasAvailableServers;
            _actionHintText = this.FindControl<TextBlock>("ActionHintText")!;
            UpdateCommandStates();
            if (initialServer is not null)
                _ = _vm.InitializeFromServer(initialServer, autoConnect, initialClusterId);
        }

        /// <summary>
        /// Результат импорта: отмеченные новые базы (без дубликатов).
        /// Читается после <c>ShowDialogSync() == true</c>.
        /// </summary>
        public IReadOnlyList<Infobase> SelectedBases => _vm.SelectedBases;

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private async System.Threading.Tasks.Task ConnectAndSaveAsync()
        {
            await _vm.ConnectAsync();
            if (_vm.Clusters.Count > 0)
                SaveRacSettings();
        }

        private void OnImportClick()
        {
            _vm.ImportCommand.Execute(null);
            if (_vm.SelectedBases.Count == 0)
            {
                AppServices.GetRequiredService<IDialogService>()
                    .ShowInfo(LocalizationManager.T("Selection.NothingSelected"), Title);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterImportViewModel.IsBusy):
                case nameof(ClusterImportViewModel.HasAvailableServers):
                    // «Подключиться» недоступно при пустом справочнике (issue #27) и во время запроса.
                    UpdateCommandStates();
                    break;
                case nameof(ClusterImportViewModel.SelectedServer):
                    // Смена сервера сбрасывает состояние подключения (issue #25):
                    // синхронизация комбобокса и пересчёт кнопок/прогресса.
                    SyncSelectedServer();
                    UpdateCommandStates();
                    break;
                case nameof(ClusterImportViewModel.SelectedClusterId):
                    SyncSelectedCluster();
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
            _connectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            _loadBasesButton.IsEnabled = !busy;
            _selectAllButton.IsEnabled = !busy;
            _selectNoneButton.IsEnabled = !busy;
            _importButton.IsEnabled = !busy && _vm.ReadyToImportCount > 0;
            _progressBar.IsVisible = busy;
            // Причина недоступности кнопки «Импортировать» — над кнопками.
            if (_actionHintText is not null)
            {
                var hint = _vm.ActionHint;
                _actionHintText.Text = hint;
                _actionHintText.IsVisible = !string.IsNullOrEmpty(hint);
            }
        }

        /// <summary>
        /// Автовыбор первого кластера VM синхронизируется с комбобоксом (комбобокс
        /// не биндится на SelectedValue: коллекция Clusters заменяется после каждого
        /// подключения, а явная синхронизация исключает обратные срабатывания).
        /// </summary>
        private void SyncSelectedCluster()
        {
            if (_vm.SelectedClusterId is not Guid id)
                return;
            if (_clusterCombo.SelectedItem is RacCluster current && current.Id == id)
                return;
            _clusterCombo.SelectedItem = _vm.Clusters.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Подстановка сервера из VM (InitializeFromServer, issue #27) синхронизируется
        /// с комбобоксом — выбранная запись справочника видна в списке сразу.
        /// </summary>
        private void SyncSelectedServer()
        {
            if (_vm.SelectedServer is not { } selected)
                return;
            if (ReferenceEquals(_serverCombo.SelectedItem, selected))
                return;
            _serverCombo.SelectedItem = selected;
        }

        private void UpdateSummary()
        {
            _summaryText.Text = string.Format(
                LocalizationManager.T("ClusterImport.SummaryFormat"), _vm.ReadyToImportCount);

            var duplicates = _vm.DuplicateCount;
            _duplicateSummaryText.Text = duplicates > 0
                ? string.Format(LocalizationManager.T("ClusterImport.DuplicatesFormat"), duplicates)
                : string.Empty;
        }

        private Button BuildImportButton()
        {
            // Жёсткая циановая заливка и белый текст убраны: акцент темы (issue #26).
            var button = BuildActionButton(LocalizationManager.T("ClusterImport.ImportButton"), "⇩", OnImportClick);
            return button;
        }

        /// <summary>«Закрыть»: отодвинута дальше (хост CloseHost справа),
        /// красный шрифт и иконка — по образцу Windows-версии.</summary>
        private Button BuildCloseButton()
        {
            var button = BuildActionButton(LocalizationManager.T("Common.Close"), "✖", () => Close());
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            // Красный шрифт — на семантической кисти темы (аудит UI, п. 2.9).
            ThemeBrushes.Bind(button, Button.ForegroundProperty, "ErrorBrush");
            return button;
        }

        private Button BuildActionButton(string text, string icon, Action onClick)
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
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildSecondaryButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6) };
            button.Styled(ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
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
#endif