#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Converters;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Серверы 1С» (0.3.9.124, Avalonia/Linux): встроенный монитор серверов 1С
    /// через утилиту rac. Слева — справочник «Сервера управления» (issue #3): список
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; подключение —
    /// по кнопке «Подключиться» или двойному клику по строке (НЕ по активации строки).
    /// Первая строка правой панели — кнопки «Подключиться/Обновить/Диагностика сети»,
    /// вторая — выбор кластера, автообновление и статус. Вкладки «Рабочие процессы /
    /// Сеансы / Соединения / Блокировки / Регламентные задания / Базы / Информация о
    /// кластере». Сортировка колонок — кликом по заголовку (issue #3). Вся логика —
    /// в чистой ViewModel <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль
    /// хранятся в справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public sealed class ServerMonitorWindow : ModalWindowBase
    {
        // Окно большое и изменяемого размера — показываем кнопку «Развернуть»
        // в собственном chrome (issue #324; на Windows системная кнопка уже есть).
        protected override bool SupportsMaximizeButton => true;

        private readonly ServerMonitorViewModel _vm;
        private readonly ComboBox _clusterCombo;
        private readonly ListBox _serversList;
        private ListBox _infobasesList = new();
        private TextBlock? _infobasesEmptyHint;
        private TextBlock? _infobasesTitle;

        /// <summary>
        /// Открытие «Импорта из кластера 1С» (issue #27): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterImport;

        /// <summary>
        /// Открытие «Миграции между кластерами 1С» (issue #41): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterMigration;

        /// <summary>Заголовки вкладок с количеством строк (обновляются по PropertyChanged).</summary>
        private readonly List<(TabItem Tab, string Property)> _dynamicTabHeaders = new();

        /// <summary>
        /// Настройки окна «Серверы 1С» (issue #75): загружаются в конструкторе, при закрытии
        /// в них сохраняются ширины колонок вкладок (переживают закрытие программы).
        /// </summary>
        private Models.AppSettings _windowSettings = new();

        /// <summary>
        /// Текущие фактические ширины колонок по вкладкам (issue #75): ключ — вкладка
        /// (Sessions/Connections/Locks/Processes), значение — пиксельные ширины (0 —
        /// гибкая/не сохранявшаяся колонка). Заполняется при перетаскивании границ
        /// колонок и сохраняется в настройки при закрытии окна.
        /// </summary>
        private readonly Dictionary<string, double[]> _columnWidths = new();

        /// <summary>Фоновое обновление кэша статусов служб (5 с; issue #3, часть 2).</summary>
        private System.Threading.Timer? _servicesStatusTimer;

        private static readonly TimeSpan ServicesStatusRefreshInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Ширина «ручки»-зоны захвата границы колонки, px (issue #75, 1.0.1.4): 21 px + выступ
        /// 3 px за правый край колонки (Margin 0,0,-3,0) = 24 px — та же зона перетаскивания,
        /// что в WPF-версии (<c>HeaderResizeHotZone</c>=24). Прежняя ручка 7 px была слишком
        /// узкой: пользователи целятся в границу и по краю заголовка, узкая зона пропускала
        /// часть кликов в сортировку вместо перетаскивания (комментарий 7OH, issue #75).
        /// </summary>
        private const double ColumnGripWidth = 21.0;

        /// <summary>
        /// Диагностика CM_COLUMNS (issue #75): первая попытка перетаскивания границы
        /// колонок уже залогирована (лог под гейтом CM_RAC + CM_ON — без спама).
        /// </summary>
        private bool _columnDragDiagLogged;

        /// <param name="openClusterImport">
        /// Открытие окна импорта с сервером, текущим на момент нажатия «Импорт»
        /// (issue #27); null — кнопка «Импорт» под списком баз не показывает импорт.
        /// </param>
        /// <param name="openClusterMigration">
        /// Открытие окна миграции с сервером, текущим на момент нажатия «Миграция»
        /// (issue #41); null — кнопка «Миграция» под списком баз не показывает миграцию.
        /// </param>
        public ServerMonitorWindow(
            Action<ManagementServer?, Guid?>? openClusterImport = null,
            Action<ManagementServer?, Guid?>? openClusterMigration = null)
        {
            _openClusterImport = openClusterImport;
            _openClusterMigration = openClusterMigration;
            Title = LocalizationManager.T("ServerMonitor.Title");
            Width = 1400;
            Height = 760;
            MinWidth = 1120;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;
            // Монитор — немодальное рабочее окно (issue #3), в панели задач ему быть.
            ShowInTaskbar = true;

            var rac = AppServices.GetRequiredService<IRacClient>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var serverListStore = AppServices.GetRequiredService<IServerListStore>();
            // п.5 «Мониторинг лицензий» (1.0.0.87): установленные программные лицензии
            // (ring/licensetool) и порог «свободно меньше N» из настроек уведомлений.
            var ringLicenses = AppServices.GetRequiredService<IRingLicenseService>();
            var licenseAlerts = AppServices.GetRequiredService<Workspace1C.Services.LicenseManager>();
            var licenseSettings = AppServices.GetRequiredService<INotificationSettingsStore>().Load();

            // issue #69: диалог повтора удаления базы открывается из VM (после неудачного
            // rac «infobase drop») — колбэк реализует окно, чтобы VM оставалась тестируемой.
            _vm = new ServerMonitorViewModel(
                rac,
                dialogs,
                action => Dispatcher.UIThread.Post(action),
                serverListStore,
                (errorText, login, password) =>
                {
                    var retry = new RacInfobaseDropRetryWindow(errorText, login, password);
                    return retry.ShowSync(this)
                        ? new ViewModels.DropRetryResult(
                            retry.Retry, retry.User, retry.Password,
                            retry.TerminateSessions, retry.BlockScheduledJobs)
                        : ViewModels.DropRetryResult.Cancel;
                },
                ringLicenses,
                licenseAlerts,
                licenseSettings.LicenseFreeThreshold,
                licenseSettings.LicenseAlertRepeatHours);

            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;

            // Состояние окна запоминается (issue #3, комментарий 7OH): максимизировано или нет.
            // Ширины колонок вкладок (issue #75): загружаются при открытии, сохраняются
            // при закрытии (переживают закрытие программы).
            try
            {
                var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
                _windowSettings = settings;
                if (settings.ServerMonitorMaximized)
                    WindowState = WindowState.Maximized;
            }
            catch
            {
                // Запоминание состояния — вспомогательная возможность, сбой не важен.
            }

            Closed += (_, _) =>
            {
                // Фоновый таймер статусов служб останавливается при закрытии окна (без утечки).
                _servicesStatusTimer?.Dispose();
                _servicesStatusTimer = null;
                _vm.Dispose();
                try
                {
                    _windowSettings.ServerMonitorMaximized = WindowState == WindowState.Maximized;
                    foreach (var (tabKey, widths) in _columnWidths)
                        ServerMonitorColumnWidthsStore.Save(_windowSettings, tabKey, widths);
                    AppServices.GetRequiredService<IInfobaseRepository>().SaveSettings(_windowSettings);
                }
                catch
                {
                    // Запоминание состояния — вспомогательная возможность, сбой не важен.
                }
            };

            // ---- Справочник «Сервера управления» (issue #3): список слева. ----
            _serversList = new ListBox
            {
                // Строки списка серверов крупнее (issue #3): как строки в списке баз —
                // выше высота и крупнее шрифт (строку удобно выделять для правки).
                ItemTemplate = new FuncDataTemplate<ManagementServer>((server, _) =>
                {
                    var text = new TextBlock
                    {
                        Text = server.Description,
                        FontSize = 14,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    // Подсветка подключённого сервера (issue #3, комментарий 7OH 19.10):
                    // строка записи, к которой установлено подключение, — жирная. Привязка —
                    // к ConnectedServer из DataContext окна (= VM, как у списка) через
                    // конвертер, сверяющий Id записи строки с подключённым сервером.
                    text.Bind(TextBlock.FontWeightProperty, new Binding
                    {
                        Path = nameof(ServerMonitorViewModel.ConnectedServer),
                        Source = _vm,
                        Converter = new ServerConnectedFontWeightConverter(server)
                    });
                    return text;
                })
            };
            _serversList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 40d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 4)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            _serversList.Bind(ListBox.ItemsSourceProperty, new Binding("Servers"));
            _serversList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedServer", BindingMode.TwoWay));
            // Подключение по двойному клику (issue #3) — простое выделение строки для
            // правки подключение НЕ запускает (SelectionChanged остаётся для выбора).
            _serversList.DoubleTapped += (_, _) =>
            {
                if (_serversList.SelectedItem is ManagementServer server)
                    _ = _vm.ConnectToServerAsync(server);
            };

            // Кнопки управления серверами — иконками без надписей (issue #3, комментарий
            // 7OH 20.10): Добавить, Изменить | Вверх, Вниз | Удалить (как у списка баз).
            var addServerButton = BuildIconButton(
                "＋", LocalizationManager.T("ServerMonitor.Servers.Add"), OnAddServer);
            var editServerButton = BuildIconButton(
                "✎", LocalizationManager.T("ServerMonitor.Servers.Edit"), OnEditServer);
            var moveUpServerButton = BuildIconButton(
                "↑", LocalizationManager.T("ServerMonitor.Servers.MoveUp"), _vm.MoveServerUp);
            var moveDownServerButton = BuildIconButton(
                "↓", LocalizationManager.T("ServerMonitor.Servers.MoveDown"), _vm.MoveServerDown);
            // Кнопке «Удалить» — красная иконка (issue #3, комментарий 7OH).
            var deleteServerButton = BuildIconButton(
                "🗑", LocalizationManager.T("ServerMonitor.Servers.Delete"), OnDeleteServer,
                red: true);

            // Доступность «Вверх»/«Вниз» зависит от позиции выбранной строки (issue #3).
            moveUpServerButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveServerUp"));
            moveDownServerButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveServerDown"));

            // Кнопки управления серверами НАД списком (issue #3): между заголовком и списком.
            var serverButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    addServerButton, editServerButton, BuildToolbarSeparator(),
                    moveUpServerButton, moveDownServerButton, BuildToolbarSeparator(),
                    deleteServerButton
                }
            };

            var serversPanel = new DockPanel { LastChildFill = true };
            // Кнопка «СЛУЖБЫ» (issue #3, 18.10 08:57): внизу левой колонки, на всю
            // ширину; показывается то же подменю служб, что и в трее (ServiceTrayMenuBuilder).
            var servicesButton = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("ServerMonitor.Servers.Services"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeight.SemiBold
                },
                Padding = new Thickness(0, 8),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            servicesButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(servicesButton, Button.BackgroundProperty, "ItemHoverBrush");
            servicesButton.Click += (_, _) => ShowServicesMenu(servicesButton);
            DockPanel.SetDock(servicesButton, Dock.Bottom);

            var serversTitle = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Servers.Title"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            // Подсказка при пустом списке (issue #3): добавьте сервер и выберите его.
            var serversEmptyHint = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Servers.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                [!IsVisibleProperty] = new Binding("!HasServers")
            };
            DockPanel.SetDock(serversTitle, Dock.Top);
            DockPanel.SetDock(serverButtons, Dock.Top);
            DockPanel.SetDock(serversEmptyHint, Dock.Top);
            serversPanel.Children.Add(serversTitle);
            serversPanel.Children.Add(serverButtons);
            serversPanel.Children.Add(serversEmptyHint);
            // «СЛУЖБЫ» — до списка (последний ребёнок заполняет остаток).
            serversPanel.Children.Add(servicesButton);
            serversPanel.Children.Add(_serversList);

            // ---- Список информационных баз кластера (issue #3, 7OH от 2026-10-17):
            // НЕ вкладка, а отдельный список слева от вкладок, на одной горизонтали с ними.
            // Клик по строке включает фильтр; три кнопки управления — иконками в одну строку. ----
            var infobasesPanel = BuildInfobasesPanel();

            // ---- Первая строка панели (issue #3): «Подключиться», «Обновить», «Диагностика». ----
            var connectButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Connect"), "🔗",
                () => _vm.ConnectCommand.Execute(null));
            var refreshButton = BuildActionButton(LocalizationManager.T("ServerMonitor.Refresh"), "⟳", _vm.Refresh);

            // «Диагностика сети…» (0.3.9.232, функция 12): DNS/ICMP/TCP-проверка
            // адреса:порт монитора и стандартных портов 1С. Работает до подключения rac.
            var diagnosticsButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.NetworkDiagnostics"), "🛜", OpenNetworkDiagnostics);

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { connectButton, refreshButton, diagnosticsButton }
            };

            // ---- Вторая строка панели: кластер, автообновление, статус подключения. ----
            // Автообновление (issue #324, комментарий 17/18): переключатель вкл/выкл + интервал.
            var autoRefreshCheck = new CheckBox
            {
                Content = LocalizationManager.T("ServerMonitor.AutoRefreshToggle"),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _vm.IsAutoRefreshEnabled
            };
            autoRefreshCheck.IsCheckedChanged += (_, _) => _vm.SetAutoRefreshEnabled(autoRefreshCheck.IsChecked == true);
            var intervalLabel = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.AutoRefreshInterval"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var intervalBox = new ComboBox { Width = 72, VerticalAlignment = VerticalAlignment.Center };
            intervalBox.ItemsSource = new[] { 5, 10, 15, 30, 60 };
            intervalBox.SelectedItem = _vm.AutoRefreshIntervalSeconds;
            intervalBox.SelectionChanged += (_, _) =>
            {
                if (intervalBox.SelectedItem is int seconds)
                    _vm.AutoRefreshIntervalSeconds = seconds;
            };

            // ---- Выбор кластера + статус. ----
            _clusterCombo = new ComboBox { Width = 300, VerticalContentAlignment = VerticalAlignment.Center };
            _clusterCombo.Styled(ControlThemes.ModernComboBox);
            _clusterCombo.ItemsSource = _vm.ClusterRows;
            _clusterCombo.ItemTemplate = new FuncDataTemplate<RacClusterRow>((row, _) => new TextBlock { Text = row.DisplayText });
            _clusterCombo.SelectionChanged += (_, _) =>
            {
                if (_clusterCombo.SelectedItem is RacClusterRow row)
                    _vm.SelectedClusterId = row.Id;
            };

            var status = new TextBlock { FontSize = 12, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));
            // Ошибка — на семантической кисти темы (аудит UI, п. 2.9).
            var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            ThemeBrushes.Bind(error, TextBlock.ForegroundProperty, "ErrorBrush");
            error.Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));

            var clusterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 10),
                Children =
                {
                    ClusterLabel(),
                    _clusterCombo,
                    // Автообновление + интервал (issue #324): рядом с выбором кластера.
                    autoRefreshCheck,
                    intervalLabel,
                    intervalBox,
                    status
                }
            };

            // ---- Вкладки данных кластера (issue #3, комментарий 7OH): порядок
            // «Сеансы / Соединения / Блокировки / Рабочие процессы / Информация о кластере»;
            // вкладка «Базы» перенесена в отдельный список слева; в заголовках количество
            // строк («Сеансы (12)»). Сеансы/соединения/блокировки биндятся на
            // ОТФИЛЬТРОВАННЫЕ коллекции — выбор базы фильтрует их на клиенте. ----
            var tabs = new TabControl { Margin = new Thickness(0) };
            // Тема вкладок (issue #3, комментарий 7OH 20.10): в тёмном режиме вкладки
            // были белыми и надписи не читались — применяем тему с фоном/текстом из темы.
            tabs.Styled(ControlThemes.ServerMonitorTabControl);
            // issue #72: мультивыделение сеансов (Ctrl — инверсия, Shift — диапазон) —
            // ListBox в режиме SelectionMode.Multiple, набор выбранных строк синхронизируется
            // в ViewModel, кнопка «Завершить сеансы» действует на весь список.
            var terminateButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.TerminateSession"), "✕",
                () => _vm.TerminateSessionCommand.Execute(null));
            terminateButton.Bind(Button.IsEnabledProperty, new Binding("CanTerminateSessions"));
            var sessionsTab = CreateTab(_vm.SessionsTabText,
                BuildTabWithAction(
                    BuildSortableTable(_vm.FilteredSessions, SessionColumns, BuildSessionRow,
                        multiSelect: true,
                        selectionSource: () => _vm.SelectedSessions,
                        selectionHandler: items => _vm.SelectedSessions = items.OfType<RacSessionRow>().ToList(),
                        tabKey: ServerMonitorColumnWidthsStore.SessionsKey),
                    terminateButton));

            // issue #72: то же для соединений («Разорвать соединения» — по всем выбранным).
            var disconnectButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.DisconnectConnection"), "⛓",
                () => _vm.DisconnectConnectionCommand.Execute(null));
            disconnectButton.Bind(Button.IsEnabledProperty, new Binding("CanDisconnectConnections"));
            var connectionsTab = CreateTab(_vm.ConnectionsTabText,
                BuildTabWithAction(
                    BuildSortableTable(_vm.FilteredConnections, ConnectionColumns, BuildConnectionRow,
                        multiSelect: true,
                        selectionSource: () => _vm.SelectedConnections,
                        selectionHandler: items => _vm.SelectedConnections = items.OfType<RacConnectionRow>().ToList(),
                        tabKey: ServerMonitorColumnWidthsStore.ConnectionsKey),
                    disconnectButton));
            var locksTab = CreateTab(_vm.LocksTabText,
                BuildSortableTable(_vm.FilteredLocks, LockColumns, BuildLockRow,
                    tabKey: ServerMonitorColumnWidthsStore.LocksKey));
            var processesTab = CreateTab(_vm.ProcessesTabText,
                BuildContent(() => BuildSortableTable(_vm.Processes, ProcessColumns, BuildProcessRow,
                    tabKey: ServerMonitorColumnWidthsStore.ProcessesKey)));
            // issue #83 (комментарий 7OH): вкладка «Лицензии» идёт ПЕРЕД «Информация о
            // кластере» — данные по лицензиям интереснее свойств кластера (п.5, 1.0.0.87).
            var licensesTab = CreateTab(_vm.LicensesTabText, BuildLicensesPane());
            var infoTab = CreateTab(LocalizationManager.T("ServerMonitor.Tabs.Info"), BuildInfoPane());
            tabs.Items.Add(sessionsTab);
            tabs.Items.Add(connectionsTab);
            tabs.Items.Add(locksTab);
            tabs.Items.Add(processesTab);
            tabs.Items.Add(licensesTab);
            tabs.Items.Add(infoTab);
            // Заголовки с количеством строк обновляются при изменении коллекций.
            RegisterTabHeader(sessionsTab, nameof(ServerMonitorViewModel.SessionsTabText));
            RegisterTabHeader(connectionsTab, nameof(ServerMonitorViewModel.ConnectionsTabText));
            RegisterTabHeader(locksTab, nameof(ServerMonitorViewModel.LocksTabText));
            RegisterTabHeader(processesTab, nameof(ServerMonitorViewModel.ProcessesTabText));
            RegisterTabHeader(licensesTab, nameof(ServerMonitorViewModel.LicensesTabText));

            // Индикатор фильтра по базе (issue #3, 7OH от 2026-10-17): надпись и кнопка
            // «Сбросить» видны ВСЕГДА; при снятом фильтре — «Без фильтра». Фильтрация
            // клиентская, без rac-вызовов.
            var filterText = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.TextProperty] = new Binding("InfobaseFilterText")
            };
            var clearFilterButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.Infobase.FilterReset"), "✕", _vm.ClearInfobaseFilter);
            var filterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { filterText, clearFilterButton }
            };

            var autoRefreshHint = new TextBlock { FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
            autoRefreshHint.Bind(TextBlock.TextProperty, new Binding("AutoRefreshText"));

            var root = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(280, GridUnitType.Pixel)),
                    // Колонка «Базы» расширена (issue #27): кнопки шире (MinWidth=40 после
                    // 1.0.0.25) — 260 px, чтобы все пять кнопок и разделители помещались.
                    new ColumnDefinition(new GridLength(260, GridUnitType.Pixel)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                }
            };
            // Слева — справочник «Сервера управления», далее — список баз (issue #3),
            // справа — данные кластера.
            // Компоновка (issue #3, скриншот 7OH 17.10): кнопки действий и строка кластера —
            // на всю ширину над базами и вкладками; список баз начинается на одном уровне
            // со строкой фильтра и вкладками («базы и вкладки на одном уровне»).
            Place(root, serversPanel, 0, 0, 5);
            Place(root, actionPanel, 1, 0);
            Grid.SetColumnSpan(actionPanel, 2);
            Place(root, clusterPanel, 1, 1);
            Grid.SetColumnSpan(clusterPanel, 2);
            infobasesPanel.Margin = new Thickness(0, 0, 12, 0);
            Place(root, infobasesPanel, 1, 2, 3);
            Place(root, filterPanel, 2, 2);
            Place(root, tabs, 2, 3);
            Place(root, new StackPanel { Children = { error, autoRefreshHint }, Spacing = 2 }, 2, 4);
            Content = root;
        }

        private TabItem CreateTab(string header, Control content) => new TabItem
        {
            Header = header,
            Padding = new Thickness(14, 6),
            FontSize = 12.5,
            Content = content
        }.Styled(ControlThemes.ServerMonitorTabItem);

        /// <summary>
        /// Вкладка «Лицензии» (п.5 «Мониторинг лицензий», выпуск 1.0.0.87): сводка,
        /// занятые сеансами лицензии («session list --licenses») и установленные
        /// программные лицензии («ring license list» / licensetool). Простые таблицы
        /// без сортировки — ListBox со строкой-Grid по фиксированным колонкам.
        /// </summary>
        private Control BuildLicensesPane()
        {
            var summary = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 4) };
            summary.Bind(TextBlock.TextProperty, new Binding("LicenseSummaryText"));
            var status = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = Themes.ThemeBrushes.Get("WarningBrush", "#D97706")
            };
            status.Bind(TextBlock.TextProperty, new Binding("LicenseStatusText"));
            // issue #83: предупреждение о недоступности утилиты лицензий показывается,
            // когда текст непустой (при пустом — не занимает место).
            status.Bind(TextBlock.IsVisibleProperty, new Binding("LicenseStatusText")
            {
                Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty
            });

            var headerPanel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8), Children = { summary, status } };

            var inUseTitle = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Licenses.InUseHeader"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 2, 0, 4)
            };
            // Ширины колонок: отрицательные — «звёздные» (1 / -w), положительные — пиксели.
            var inUseWidths = new[] { -1.3, -1.3, -1.0, 110d, 80d, 90d, 90d };
            var inUseTable = BuildLicenseList(
                _vm.LicensedSessions,
                new[] { "User", "InfobaseName", "Host", "LicType", "LicensesUsedText", "MaxLicensesText", "AppId" },
                inUseWidths,
                fixedHeight: 240);

            var installedTitle = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Licenses.InstalledHeader"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var installedWidths = new[] { -1.5, 110d, 90d, -1.0, -1.0, 90d, 100d };
            var installedTable = BuildLicenseList(
                _vm.InstalledLicenses,
                new[] { "SerialNumber", "Type", "Version", "Module", "User", "State", "ExpiryDate" },
                installedWidths,
                fixedHeight: 200);

            var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(headerPanel, Dock.Top);
            panel.Children.Add(headerPanel);
            DockPanel.SetDock(inUseTitle, Dock.Top);
            panel.Children.Add(inUseTitle);
            DockPanel.SetDock(installedTitle, Dock.Bottom);
            panel.Children.Add(installedTitle);
            DockPanel.SetDock(inUseTable, Dock.Top);
            panel.Children.Add(inUseTable);
            panel.Children.Add(installedTable);
            return panel;
        }

        /// <summary>Таблица лицензий: строка-заголовок + ListBox со строками-Grid.</summary>
        private Control BuildLicenseList<T>(
            ObservableCollection<T> items, IReadOnlyList<string> propertyPaths,
            IReadOnlyList<double> widths, double fixedHeight = 0)
        {
            var headerCells = propertyPaths.Select(p =>
            {
                // Значения T/ProgamLicense отображаются напрямую; лицензионные строки
                // сеансов локализованы в модели строки. Заголовки — по ключам локализации.
                return p switch
                {
                    "User" => LocalizationManager.T("ServerMonitor.Licenses.Columns.User"),
                    "InfobaseName" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Infobase"),
                    "Host" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Host"),
                    "LicType" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Type"),
                    "LicensesUsedText" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Used"),
                    "MaxLicensesText" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Max"),
                    "AppId" => LocalizationManager.T("ServerMonitor.Licenses.Columns.App"),
                    "SerialNumber" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Serial"),
                    "Type" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Type"),
                    "Version" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Version"),
                    "Module" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Module"),
                    "State" => LocalizationManager.T("ServerMonitor.Licenses.Columns.State"),
                    "ExpiryDate" => LocalizationManager.T("ServerMonitor.Licenses.Columns.Expiry"),
                    _ => p
                };
            }).ToList();

            var list = new ListBox
            {
                ItemsSource = items,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12.5
            };
            // Разная высота двух таблиц задаётся вызывающим кодом (по умолчанию — авто).
            if (fixedHeight > 0)
                list.Height = fixedHeight;
            list.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                LicenseRow(
                    propertyPaths.Select(p => FormatCell(item, p)).ToList(),
                    widths,
                    header: false));

            var headerRow = LicenseRow(headerCells, widths, header: true);
            var root = new StackPanel { Children = { headerRow, list } };
            return root;
        }

        /// <summary>Значение ячейки таблицы лицензий (по пути свойства или текст года).</summary>
        private static string FormatCell(object item, string propertyPath)
        {
            if (item is RacLicensedSessionRow row)
            {
                return propertyPath switch
                {
                    "User" => row.User,
                    "InfobaseName" => row.InfobaseName,
                    "Host" => row.Host,
                    "LicType" => row.LicType,
                    "LicensesUsedText" => row.LicensesUsedText,
                    "MaxLicensesText" => row.MaxLicensesText,
                    "AppId" => row.AppId,
                    _ => string.Empty
                };
            }
            if (item is ProgramLicense license)
            {
                return propertyPath switch
                {
                    "SerialNumber" => license.SerialNumber,
                    "Type" => license.Type,
                    "Version" => license.Version,
                    "Module" => license.Module,
                    "User" => license.User,
                    "State" => license.State,
                    "ExpiryDate" => license.ExpiryDate?.ToString("dd.MM.yyyy") ?? "—",
                    _ => string.Empty
                };
            }
            return Convert.ToString(item) ?? string.Empty;
        }

        /// <summary>Строка таблицы лицензий: Grid с ячейками по ширинам колонок.</summary>
        private static Control LicenseRow(IReadOnlyList<string> cells, IReadOnlyList<double> widths, bool header)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            foreach (var width in widths)
            {
                grid.ColumnDefinitions.Add(width < 0
                    ? new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                    : new ColumnDefinition(new GridLength(width, GridUnitType.Pixel)));
            }
            for (var i = 0; i < cells.Count && i < widths.Count; i++)
            {
                var cell = new TextBlock
                {
                    Text = cells[i],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                    FontSize = header ? 12 : 12.5
                };
                ThemeBrushes.Bind(cell, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        /// <summary>Регистрирует заголовок вкладки для обновления при смене количества строк.</summary>
        private void RegisterTabHeader(TabItem tab, string property)
            => _dynamicTabHeaders.Add((tab, property));

        /// <summary>Обновляет заголовки вкладок с количеством строк (issue #3: «Сеансы (12)»).</summary>
        private void UpdateDynamicTabHeaders(string changedProperty)
        {
            foreach (var (tab, property) in _dynamicTabHeaders)
            {
                if (property != changedProperty)
                    continue;
                tab.Header = changedProperty switch
                {
                    nameof(ServerMonitorViewModel.SessionsTabText) => _vm.SessionsTabText,
                    nameof(ServerMonitorViewModel.ConnectionsTabText) => _vm.ConnectionsTabText,
                    nameof(ServerMonitorViewModel.LocksTabText) => _vm.LocksTabText,
                    nameof(ServerMonitorViewModel.LicensesTabText) => _vm.LicensesTabText,
                    _ => _vm.ProcessesTabText
                };
            }
        }

        /// <summary>
        /// «Диагностика сети…» (0.3.9.232, функция 12): открывает окно диагностики
        /// с текущими адресом/портом монитора. Не требует успешного подключения rac —
        /// диагностика нужна именно до подключения.
        /// </summary>
        private void OpenNetworkDiagnostics()
        {
            var target = NetworkDiagnosticsTargets.FromServerMonitor(_vm.ServerAddress, _vm.ServerPort);
            var vm = new NetworkDiagnosticsViewModel(
                AppServices.GetRequiredService<INetworkDiagnosticsService>(),
                target,
                action => Dispatcher.UIThread.Post(action));
            new NetworkDiagnosticsWindow(vm).ShowDialog(this);
        }

        // ===================== Построители =====================

        private static void Place(Grid grid, Control control, int column, int row, int rowSpan = 1)
        {
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            if (rowSpan > 1)
                Grid.SetRowSpan(control, rowSpan);
            grid.Children.Add(control);
        }

        /// <summary>
        /// Колонка таблицы: заголовок, свойство отображения, свойство сортировки (может быть null),
        /// ширина и минимальная ширина (issue #75): нижний предел, достаточный для заголовка
        /// и стрелки сортировки (расчёт — <see cref="ServerMonitorColumnWidthsStore.HeaderMinWidth"/>).
        /// </summary>
        private sealed record TableColumn(string Header, string DisplayPath, string? SortPath, GridLength Width, double MinWidth = 0);

        /// <summary>
        /// Таблица с заголовками колонок и сортировкой по клику (issue #3): шапка из
        /// кнопок поверх ListBox; при клике данные пересобираются в отсортированном
        /// порядке (та же схема, что в MainViewModel — без ICollectionView). При
        /// изменении исходной коллекции (автообновление) сортировка применяется заново.
        /// </summary>
        private Control BuildSortableTable<T>(
            ObservableCollection<T> source,
            IReadOnlyList<TableColumn> columns,
            Func<object, Control> rowFactory,
            string? selectedBinding = null,
            bool multiSelect = false,
            Func<IEnumerable<object>>? selectionSource = null,
            Action<IReadOnlyList<object>>? selectionHandler = null,
            string? tabKey = null)
        {
            // Текущие ширины колонок (issue #3, 7OH 17.10): общие для шапки и всех строк,
            // меняются перетаскиванием границы заголовка. Строки создаются шаблоном и
            // получают ширины отсюда — свои ColumnDefinitions построителей перекрываются.
            // issue #75: при наличии сохранённых ширин вкладки они применяются (пиксельные),
            // иначе — дефолтные ширины из спецификации колонки (звёздные делят свободное место).
            var widths = ResolveTableWidths(tabKey, columns);
            var liveRows = new List<WeakReference<Grid>>();
            Grid? headerGridRef = null;
            // Горизонтальная прокрутка таблицы (issue #75): заголовок и строки лежат в общем
            // горизонтальном ScrollViewer, поэтому колонки прокручиваются за границу области
            // вместе с заголовком. Минимальная ширина контента — не меньше суммы ширин колонок
            // (чтобы колонки не «схлопывались») и не меньше вьюпорта (при широком окне колонки
            // по-прежнему занимают всю ширину области).
            double contentMinWidth = 0;
            DockPanel? tableLayout = null;
            ScrollViewer? tableScroll = null;

            void UpdateTableMinWidth()
            {
                if (tableLayout is null || tableScroll is null)
                    return;
                tableLayout.MinWidth = Math.Max(contentMinWidth, tableScroll.Viewport.Width);
            }

            void ApplyWidths()
            {
                if (headerGridRef is not null)
                {
                    for (var i = 0; i < widths.Length && i < headerGridRef.ColumnDefinitions.Count; i++)
                    {
                        headerGridRef.ColumnDefinitions[i].Width = widths[i];
                        headerGridRef.ColumnDefinitions[i].MinWidth = columns[i].MinWidth;
                    }
                }
                liveRows.RemoveAll(r => !r.TryGetTarget(out _));
                foreach (var weak in liveRows)
                {
                    if (!weak.TryGetTarget(out var rowGrid))
                        continue;
                    for (var i = 0; i < widths.Length && i < rowGrid.ColumnDefinitions.Count; i++)
                    {
                        rowGrid.ColumnDefinitions[i].Width = widths[i];
                        rowGrid.ColumnDefinitions[i].MinWidth = columns[i].MinWidth;
                    }
                }
                // Минимальная ширина контента таблицы (issue #75): сумма дефолтных/сохранённых
                // ширин (для звёздных — их MinWidth), чтобы при узком окне содержимое не
                // «схлопывалось», а прокручивалось вместе с заголовком.
                contentMinWidth = ComputeContentMinWidth(columns, widths);
                UpdateTableMinWidth();
            }

            var list = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                {
                    var control = rowFactory(item);
                    if (control is Grid rowGrid && rowGrid.ColumnDefinitions.Count == widths.Length)
                    {
                        for (var i = 0; i < widths.Length; i++)
                        {
                            rowGrid.ColumnDefinitions[i].Width = widths[i];
                            // issue #75: строки не «схлопываются» ниже минимума, как шапка.
                            rowGrid.ColumnDefinitions[i].MinWidth = columns[i].MinWidth;
                        }
                        // Вертикальные линии между колонками (issue #3, 7OH 17.10).
                        AddColumnLines(rowGrid, widths.Length);
                        // «_» здесь — параметр шаблона (INameScope), поэтому не discard.
                        if (liveRows.Count > 4000)
                            liveRows.RemoveAll(r => !r.TryGetTarget(out Grid? _dead));
                        liveRows.Add(new WeakReference<Grid>(rowGrid));
                    }
                    return control;
                })
            };
            // issue #72: мультивыделение (Ctrl — инверсия, Shift — диапазон) — так же,
            // как WPF DataGrid SelectionMode=Extended на вкладках «Сеансы»/«Соединения».
            if (multiSelect)
                list.SelectionMode = SelectionMode.Multiple;
            // Строка занимает всю высоту контейнера (линии колонок — от края до края),
            // тексты ячеек центрируются сами (issue #324: единая высота строки 36).
            // Нулевой отступ контейнера — чтобы колонки строк совпадали с шапкой.
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 36d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Stretch)
                }
            });

            string? sortPath = null;
            var sortDescending = false;

            // Пересобирает ItemsSource с учётом текущей сортировки (или возвращает источник).
            void Rebuild()
            {
                IReadOnlyList<object> items;
                if (sortPath is null)
                {
                    items = source.Cast<object>().ToList();
                }
                else
                {
                    var comparer = Comparer<object?>.Create(CompareSortValues);
                    var keyed = source.Cast<object>().Select(x => (Item: x, Key: GetSortValue(x, sortPath)));
                    keyed = sortDescending
                        ? keyed.OrderByDescending(t => t.Key, comparer)
                        : keyed.OrderBy(t => t.Key, comparer);
                    items = keyed.Select(t => t.Item).ToList();
                }

                list.ItemsSource = items;
                if (multiSelect)
                {
                    // issue #72: множественный выбор восстанавливается из ViewModel (набор
                    // строк после автообновления) и текущих SelectedItems, оставшихся в списке.
                    var keep = ((selectionSource?.Invoke() ?? list.SelectedItems.Cast<object>())
                            .Concat(list.SelectedItems.Cast<object>()))
                        .Distinct()
                        .Where(items.Contains)
                        .ToList();
                    list.SelectedItems.Clear();
                    foreach (var item in keep)
                        list.SelectedItems.Add(item);
                }
                else if (list.SelectedItem is { } previous && items.Contains(previous))
                {
                    // Восстанавливаем одиночный выбор, если объект остался в списке
                    // (после автообновления выбор по идентификатору делает сама ViewModel).
                    list.SelectedItem = previous;
                }
            }

            // Заголовки-кнопки: клик сортирует по колонке (повторный клик меняет направление).
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerGridRef = headerGrid;
            for (var i = 0; i < columns.Count; i++)
            {
                // issue #75: MinWidth колонки — заголовок и стрелка сортировки не обрезаются.
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition(widths[i]) { MinWidth = columns[i].MinWidth });
            }
            var arrows = new Dictionary<TableColumn, TextBlock>();

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                // Стрелка сортировки (issue #3): акцентный цвет, не переносится и не
                // «съедается» длинным заголовком — заголовок усекается (TextTrimming),
                // а стрелка всегда видна справа в отдельной авто-колонке.
                var arrow = new TextBlock
                {
                    Text = string.Empty,
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };
                ThemeBrushes.Bind(arrow, TextBlock.ForegroundProperty, "AccentBrush");
                arrows[col] = arrow;
                var headerText = new TextBlock
                {
                    Text = col.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var headerPanel = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                        new ColumnDefinition(GridLength.Auto)
                    }
                };
                Grid.SetColumn(headerText, 0);
                Grid.SetColumn(arrow, 1);
                headerPanel.Children.Add(headerText);
                headerPanel.Children.Add(arrow);
                var button = new Button
                {
                    Content = headerPanel,
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Styled(ControlThemes.ModernButton);
                ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
                button.Click += (_, _) =>
                {
                    if (col.SortPath is null)
                        return;
                    if (sortPath == col.SortPath)
                        sortDescending = !sortDescending;
                    else
                    {
                        sortPath = col.SortPath;
                        sortDescending = false;
                    }
                    foreach (var pair in arrows)
                        pair.Value.Text = pair.Key == col
                            ? (sortDescending ? "▼" : "▲")
                            : string.Empty;
                    Rebuild();
                };
                Grid.SetColumn(button, i);
                headerGrid.Children.Add(button);

                // Граница колонки в заголовке (issue #3, 7OH 17.10): видимая вертикальная
                // линия + «ручка»-зона захвата — перетаскиванием меняется ширина колонки
                // в шапке и во всех строках. Ширина зоны — ColumnGripWidth (21 px) + выступ
                // 3 px = 24 px, та же зона, что в WPF (HeaderResizeHotZone=24, issue #75, 1.0.1.4).
                var index = i;
                // Граница, рассчитанная по координате клика (issue #75): используется
                // в PointerMoved (применение ширин) вместо «колонки грипа» — паритет
                // с WPF, где граница определяется чистой функцией по координате.
                var dragIndex = index;
                var line = new Border
                {
                    Width = 1,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    IsHitTestVisible = false
                };
                ThemeBrushes.Bind(line, Border.BackgroundProperty, "BorderColorBrush");
                var grip = new Border
                {
                    Width = ColumnGripWidth,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, -3, 0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast),
                    ZIndex = 1
                };
                ToolTip.SetTip(grip, LocalizationManager.T("ServerMonitor.ColumnResizeHint"));
                var dragging = false;
                var startX = 0d;
                var startWidths = Array.Empty<double>();
                var ctrlMode = false;
                grip.PointerPressed += (_, e) =>
                {
                    dragging = true;
                    startX = e.GetPosition(headerGrid).X;
                    startWidths = headerGrid.ColumnDefinitions.Select(d => d.ActualWidth).ToArray();
                    ctrlMode = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
                    // issue #75 (репорт 7OH от 2026-10-30): индекс границы определяется
                    // ПО КООРДИНАТЕ X клика той же чистой функцией, что и в WPF
                    // (ResolveDragBoundary) — паритет платформ: граница колонки i —
                    // правый край колонки i; при промахе (ширины не измерены) —
                    // колонка грипа, как раньше.
                    var resolved = ServerMonitorColumnWidthsStore.ResolveDragBoundary(
                        startWidths, startX, ColumnGripWidth);
                    dragIndex = resolved >= 0 ? resolved : index;
                    // issue #75 (1.0.0.96): обычное перетаскивание — как в 1С: граница
                    // двигается, ВСЕ колонки справа едут как блок (ширины не меняются).
                    // «Заморозка» гибких (звёздных) колонок фактической шириной: иначе
                    // звёздные колонки перераспределяли бы ширину на себя («двигает одну-две,
                    // упираются в третью»), а ширину контента надо РАСТИТЬ — тогда появится
                    // горизонтальная прокрутка и границу можно двигать дальше.
                    if (!ctrlMode)
                    {
                        for (var j = 0; j < widths.Length; j++)
                        {
                            if (widths[j].IsStar)
                                widths[j] = new GridLength(headerGrid.ColumnDefinitions[j].ActualWidth, GridUnitType.Pixel);
                        }
                        ApplyWidths();
                    }
                    e.Pointer.Capture(grip);
                    // Диагностика (issue #75, усиленная): какая граница определена —
                    // под гейтом CM_RAC + CM_ON (выключен по умолчанию), первая попытка.
                    if (!_columnDragDiagLogged)
                    {
                        Log.Info(LogCategory.Rac,
                            $"CM_COLUMNS: старт перетаскивания границы колонки {dragIndex} " +
                            $"(по координате x={Math.Round(startX, 1)}, грип колонки {index}, " +
                            $"resolved={resolved}, Ctrl: {ctrlMode}).");
                        _columnDragDiagLogged = true;
                    }
                    e.Handled = true;
                };
                grip.PointerMoved += (_, e) =>
                {
                    if (!dragging)
                        return;
                    var dx = e.GetPosition(headerGrid).X - startX;
                    // issue #75: единая с WPF семантика «как в 1С» из чистой математики
                    // ServerMonitorColumnWidthsStore: обычное перетаскивание двигает ВСЕ
                    // колонки справа как блок (ширины не меняются, при переполнении области
                    // появляется горизонтальная прокрутка), с Ctrl меняются только ДВЕ
                    // смежные колонки. Минимальные ширины колонок не дают сжать заголовок.
                    var minWidths = columns.Select(c => (double)Math.Max(30, c.MinWidth)).ToArray();
                    var newWidths = ctrlMode && dragIndex + 1 < columns.Count
                        ? ServerMonitorColumnWidthsStore.ApplyDragResizePair(startWidths, dragIndex, dx, minWidths)
                        : ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(startWidths, dragIndex, dx, minWidths);
                    for (var j = 0; j < widths.Length; j++)
                        widths[j] = new GridLength(newWidths[j], GridUnitType.Pixel);
                    ApplyWidths();
                    e.Handled = true;
                };
                grip.PointerReleased += (_, e) => FinishGripDrag(e);
                // Обрыв захвата (переключение окна, отмена системы) — обязательно закрывает
                // цикл перетаскивания: без этого после потери захвата ширины «замирают».
                // Повторный вызов (после штатного PointerReleased) безопасен: dragging уже false.
                grip.PointerCaptureLost += (_, _) => FinishGripDrag();

                void FinishGripDrag(Avalonia.Input.PointerEventArgs? e = null)
                {
                    if (!dragging)
                        return;
                    dragging = false;
                    e?.Pointer.Capture(null);
                    // issue #75: фиксируем фактические ширины вкладки — при закрытии окна
                    // они сохранятся в настройки (переживают закрытие программы). Гибкие
                    // колонки записываются нулём («не сохранять» — останутся звёздными).
                    if (tabKey is not null)
                    {
                        _columnWidths[tabKey] = widths
                            .Select(w => w.IsStar ? 0.0 : w.Value)
                            .ToArray();
                    }
                }
                Grid.SetColumn(line, i);
                Grid.SetColumn(grip, i);
                headerGrid.Children.Add(line);
                headerGrid.Children.Add(grip);
            }

            // Исходная коллекция меняется (автообновление/переключение баз): при сортировке
            // применяем её заново; при мультивыделении пересборка нужна и без сортировки —
            // чтобы восстановить выбор по обновлённым строкам (issue #72).
            if (source is INotifyCollectionChanged notify)
            {
                notify.CollectionChanged += (_, _) =>
                {
                    if (sortPath is not null || multiSelect)
                        Rebuild();
                };
            }

            list.ItemsSource = source;
            if (selectedBinding is not null && !multiSelect)
                list.Bind(ListBox.SelectedItemProperty, new Binding(selectedBinding, BindingMode.TwoWay));

            // issue #72: изменение набора выбранных строк (Ctrl/Shift) синхронизируется в VM.
            if (multiSelect && selectionHandler is not null)
            {
                list.SelectionChanged += (_, _) =>
                    selectionHandler(list.SelectedItems.Cast<object>().ToList());
            }

            // issue #75: горизонтальная прокрутка таблицы — заголовок и строки лежат в общем
            // ScrollViewer, поэтому колонки прокручиваются за границу области вместе с
            // заголовком (как DataGrid), вертикальную прокрутку ведёт сам ListBox. Ширина
            // контента держится не меньше суммы ширин колонок (колонки не «схлопываются»)
            // и не меньше вьюпорта (при широком окне колонки занимают всю ширину области).
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);
            dock.Children.Add(list);
            tableLayout = dock;
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = dock
            };
            tableScroll = scroll;
            scroll.GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(new PropertyObserver<Size>(_ => UpdateTableMinWidth()));
            // Применяем ширины/минимумы после создания контейнеров прокрутки.
            ApplyWidths();
            return scroll;
        }

        /// <summary>
        /// Собирает стартовые ширины колонок вкладки (issue #75): сохранённые пиксельные
        /// ширины (валидные и не меньше минимума) либо дефолтные из спецификации колонки —
        /// гибкие остаются звёздными и делят свободное место окна.
        /// </summary>
        private GridLength[] ResolveTableWidths(string? tabKey, IReadOnlyList<TableColumn> columns)
        {
            var saved = tabKey is not null
                ? ServerMonitorColumnWidthsStore.TryLoad(_windowSettings, tabKey)
                : null;
            var result = new GridLength[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var width = col.Width;
                if (saved is not null && i < saved.Length && ServerMonitorColumnWidthsStore.IsValid(saved[i]))
                    width = new GridLength(Math.Max(saved[i], col.MinWidth), GridUnitType.Pixel);
                result[i] = width;
            }
            return result;
        }

        /// <summary>
        /// Минимальная ширина контента таблицы (issue #75): сумма фактических ширин колонок;
        /// для гибких (звёздных) берётся их MinWidth — заголовок и стрелка сортировки не
        /// обрезаются, при узком окне появляется горизонтальная прокрутка.
        /// </summary>
        private static double ComputeContentMinWidth(IReadOnlyList<TableColumn> columns, IReadOnlyList<GridLength> widths)
        {
            double sum = 0;
            for (var i = 0; i < columns.Count; i++)
            {
                var width = widths[i];
                sum += width.IsStar || width.Value <= 0 ? columns[i].MinWidth : Math.Max(width.Value, columns[i].MinWidth);
            }
            return sum;
        }

        private static object? GetSortValue(object item, string path)
        {
            var type = item.GetType();
            var prop = type.GetProperty(path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null || !prop.CanRead)
                return null;
            try
            {
                return prop.GetValue(item);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Сравнение значений сортировки: числа/даты — как IComparable, строки — без учёта регистра.</summary>
        private static int CompareSortValues(object? a, object? b)
        {
            if (a is null || b is null)
                return (a is null, b is null) switch
                {
                    (true, true) => 0,
                    (true, false) => -1,
                    _ => 1
                };
            if (a is IComparable comparable && a.GetType() == b.GetType())
                return comparable.CompareTo(b);
            return string.Compare(
                Convert.ToString(a, CultureInfo.InvariantCulture),
                Convert.ToString(b, CultureInfo.InvariantCulture),
                StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Простой наблюдатель значения свойства для асинхронных подписок (issue #75).</summary>
        private sealed class PropertyObserver<T> : IObserver<T>
        {
            private readonly Action<T> _apply;
            public PropertyObserver(Action<T> apply) => _apply = apply;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _apply(value);
        }

        /// <summary>Внутренний LayoutRoot обёртки (для прямого включения без SortableTable).</summary>
        private static Control BuildContent(Func<Control> factory) => factory();

        /// <summary>Вкладка с действием: кнопка снизу (завершение сеанса / разрыв соединения), список заполняет остальное.</summary>
        private static Control BuildTabWithAction(Control content, Button button)
        {
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(button, Dock.Bottom);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 8, 0, 0);
            dock.Children.Add(button);
            dock.Children.Add(content);
            return dock;
        }

        /// <summary>Кнопка действия: значок + подпись; <paramref name="red"/> — красные текст/иконка
        /// (кнопки «Удалить», issue #3).</summary>
        private static Button BuildActionButton(
            string text, string icon, System.Action onClick, bool red = false)
        {
            var redBrush = Themes.ThemeBrushes.Get("ErrorBrush", "#DC2626");
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = red ? redBrush : null
                        },
                        new TextBlock
                        {
                            Text = text,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = red ? redBrush : null
                        }
                    }
                },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Подпись поля «Кластер» (issue #61): единая кисть FormLabelBrush —
        /// читаема в обеих темах и пользовательских схемах.</summary>
        private static TextBlock ClusterLabel()
        {
            var block = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Cluster"),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Узкая кнопка-иконка (для строки управления базами, issue #3): только
        /// значок, подсказка; красная — для «Удалить». MinWidth (комментарий 7OH 20.10):
        /// «Вверх»/«Вниз» (глифы ↑/↓ уже соседних) получают ту же ширину, что и остальные.</summary>
        private static Button BuildIconButton(string icon, string tooltip, System.Action onClick, bool red = false)
        {
            var redBrush = Themes.ThemeBrushes.Get("ErrorBrush", "#DC2626");
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = red ? redBrush : null,
                    // Кнопки-иконки шире (issue #3, комментарий 7OH 20.10): раза в полтора.
                    FontSize = 14
                },
                Padding = new Thickness(9, 7),
                MinWidth = 40
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        // ===================== Колонки вкладок =====================

        private static GridLength Px(double value) => new(value, GridUnitType.Pixel);
        private static GridLength Star(double value) => new(value, GridUnitType.Star);

        /// <summary>
        /// Колонки вкладки «Рабочие процессы». Минимальные ширины (issue #75) — по длине
        /// локализованного заголовка, худший случай ru/en плюс стрелка сортировки
        /// (<see cref="ServerMonitorColumnWidthsStore.HeaderMinWidth"/>); «звёздные» колонки
        /// не сжимают заголовок, при узком окне таблица прокручивается по горизонтали.
        /// </summary>
        private static readonly IReadOnlyList<TableColumn> ProcessColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Type"), "Type", "Type", Px(110), 70),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Host"), "Host", "Host", Star(1.2), 70),
            new TableColumn("PID", "Pid", "Pid", Px(90), 60),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Port"), "Port", "Port", Px(80), 70),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.StartedAt"), "StartedAtText", "StartedAtText", Px(150), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Memory"), "MemorySizeText", "MemorySizeText", Px(110), 85),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Threads"), "Threads", "Threads", Px(100), 93),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Cpu"), "CpuText", "CpuText", Px(100), 63),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Running"), "RunningText", "Running", Px(100), 93),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Process.Infobases"), "Infobases", "Infobases", Px(80), 63),
            new TableColumn("●", string.Empty, null, Px(30))
        };

        /// <summary>
        /// Колонки вкладки «Сеансы» (MinWidth — issue #75, см. <see cref="ProcessColumns"/>).
        /// </summary>
        private static readonly IReadOnlyList<TableColumn> SessionColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.User"), "User", "User", Star(1.2), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Infobase"), "InfobaseName", "InfobaseName", Star(1.1), 183),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Host"), "Host", "Host", Star(1.1), 70),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.App"), "AppId", "AppId", Px(123), 123),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.StartedAt"), "StartedAtText", "StartedAtText", Px(150), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.LastActive"), "LastActiveAtText", "LastActiveAtText", Px(160), 160),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.State"), "StateText", "State", Px(120), 108),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Blocked"), "BlockedSymbol", "BlockedByLs", Px(93), 93),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Memory"), "MemoryText", "MemoryText", Px(110), 85),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Duration"), "DurationAllText", "DurationAllText", Px(130), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.DurationCurrent"), "DurationCurrentText", "DurationCurrentText", Px(130), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Session.Hibernate"), "HibernateText", "Hibernate", Px(108), 108)
        };

        /// <summary>
        /// Колонки вкладки «Соединения» (MinWidth — issue #75, см. <see cref="ProcessColumns"/>).
        /// </summary>
        private static readonly IReadOnlyList<TableColumn> ConnectionColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Host"), "Host", "Host", Star(1.2), 70),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Port"), "Port", "Port", Px(90), 70),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Connector"), "Connector", "Connector", Px(120), 108),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Established"), "EstablishedAtText", "EstablishedAtText", Px(150), 123),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.LastActive"), "LastConnectionTimeText", "LastConnectionTimeText", Px(153), 153),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Duration"), "DurationText", "DurationText", Px(130), 130),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Descr"), "Descr", "Descr", Star(1.3), 123),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Connection.Blocked"), "BlockedText", "Blocked", Px(138), 138)
        };

        /// <summary>
        /// Колонки вкладки «Блокировки» (MinWidth — issue #75, см. <see cref="ProcessColumns"/>).
        /// </summary>
        private static readonly IReadOnlyList<TableColumn> LockColumns = new[]
        {
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Object"), "Object", "Object", Star(2), 85),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Waiting"), "WaitingSymbol", "Waiting", Px(110), 93),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Blocking"), "BlockingSymbol", "Blocking", Px(110), 108),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Session"), "SessionId", "SessionId", Star(1.1), 93),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Connection"), "ConnectionId", "ConnectionId", Star(1.1), 115),
            new TableColumn(LocalizationManager.T("ServerMonitor.Columns.Lock.Transaction"), "TransactionId", "TransactionId", Star(1.1), 123)
        };

        // ===================== Вкладки-конструкторы =====================

        /// <summary>
        /// Вкладка «Информация о кластере» (issue #324, C3; issue #3 от 2026-10-17 —
        /// человекочитаемость): свойства сгруппированы в секции (заголовки групп),
        /// значения отформатированы («1 ч 30 мин», «512 МБ», «Да/Нет» — см.
        /// <see cref="RacClusterPropertyRow.DisplayValue"/>); правится безопасный набор
        /// параметров (TextBox на ИСХОДНОМ значении rac).
        /// </summary>
        private Control BuildInfoPane()
        {
            var saveButton = BuildActionButton(
                LocalizationManager.T("ServerMonitor.Cluster.Save"), "💾",
                () => _vm.SaveClusterPropertiesCommand.Execute(null));

            var panel = new StackPanel { Spacing = 2 };

            // Пересобирает секции по группам (порядок фиксирован).
            void Rebuild()
            {
                panel.Children.Clear();
                foreach (var group in new[] { "General", "Security", "Sessions", "Other" })
                {
                    var rows = _vm.ClusterProperties
                        .Where(r => RacClusterPropertyRow.GroupKey(r.RawKey) == group)
                        .ToList();
                    if (rows.Count == 0)
                        continue;

                    panel.Children.Add(new TextBlock
                    {
                        Text = RacClusterPropertyRow.LocalizeGroup(group),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 13,
                        Margin = new Thickness(0, 10, 0, 4),
                        Foreground = Themes.ThemeBrushes.Get("PurpleAccentBrush", "#7C3AED") // fallback
                    });
                    foreach (var row in rows)
                        panel.Children.Add(BuildClusterPropertyRow(row));
                }
            }

            // Исходная коллекция меняется (загрузка/автообновление) — пересобираем секции.
            _vm.ClusterProperties.CollectionChanged += (_, _) => Rebuild();
            Rebuild();

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(saveButton, Dock.Top);
            saveButton.HorizontalAlignment = HorizontalAlignment.Left;
            saveButton.Margin = new Thickness(0, 0, 0, 8);
            dock.Children.Add(saveButton);
            dock.Children.Add(new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            });
            return dock;
        }

        /// <summary>Строка «свойство — значение» вкладки «Информация о кластере»:
        /// правка — только для безопасного набора (ИСХОДНОЕ значение rac),
        /// остальные свойства — человекочитаемое <see cref="RacClusterPropertyRow.DisplayValue"/>.</summary>
        private static Control BuildClusterPropertyRow(RacClusterPropertyRow row)
        {
            var name = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Width = 340,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = row.DisplayName
            };
            ToolTip.SetTip(name, row.RawKey);

            var editBox = new TextBox
            {
                Width = 340,
                Padding = new Thickness(4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = row.EditValue,
                IsVisible = row.IsEditable
            };
            // Ручной перенос текста в ViewModel (валидация чисел/сборка «cluster update»).
            editBox.TextChanged += (_, _) =>
            {
                if (row.EditValue != editBox.Text)
                    row.EditValue = editBox.Text ?? string.Empty;
            };

            var readOnly = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                Width = 340,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = row.DisplayValue,
                IsVisible = !row.IsEditable
            };

            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(editBox, 1);
            Grid.SetColumn(readOnly, 1);
            grid.Children.Add(name);
            grid.Children.Add(editBox);
            grid.Children.Add(readOnly);
            return grid;
        }

        /// <summary>
        /// Панель «Базы» (issue #3, 7OH от 2026-10-17): список баз СЛЕВА от вкладок, на одной
        /// горизонтали с ними. В списке — только имя; клик по строке включает фильтр. Три
        /// кнопки управления (Добавить/Изменить/Удалить) — иконками в одну строку, у удаления
        /// красная иконка.
        /// </summary>
        private Control BuildInfobasesPanel()
        {
            var addButton = BuildIconButton(
                "＋", LocalizationManager.T("ServerMonitor.Infobase.Add"), OnAddInfobase);
            var editButton = BuildIconButton(
                "✎", LocalizationManager.T("ServerMonitor.Infobase.Edit"), OnEditInfobase);
            var deleteButton = BuildIconButton(
                "🗑", LocalizationManager.T("ServerMonitor.Infobase.Delete"),
                OnDeleteInfobase, red: true);
            // Кнопки «Изменить»/«Удалить» активны только при выбранной базе и подключении
            // (issue #3: раньше кнопка «Удалить» оставалась неактивной — CanDropInfobase
            // теперь уведомляет об изменении выбора).
            editButton.Bind(Button.IsEnabledProperty, new Binding("CanDropInfobase"));
            deleteButton.Bind(Button.IsEnabledProperty, new Binding("CanDropInfobase"));

            _infobasesTitle = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                Text = _vm.InfobasesHeaderText
            };

            _infobasesEmptyHint = new TextBlock
            {
                Text = LocalizationManager.T("ServerMonitor.Infobase.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                IsVisible = !_vm.HasInfobases
            };

            // Список баз: одна колонка с именем; клик по строке включает фильтр.
            _infobasesList = BuildInfobaseList();
            _infobasesList.Bind(ListBox.ItemsSourceProperty, new Binding("Infobases"));
            _infobasesList.Bind(ListBox.SelectedItemProperty, new Binding("SelectedInfobase", BindingMode.TwoWay));

            // Сортировка списка баз (issue #3, 7OH 17.10): по алфавиту и обратная — иконки.
            // Языконезависимые глифы сортировки (аудит UI, п. 4.4): «А↓/Я↓» → «A↓/Z↓».
            var sortAscButton = BuildIconButton(
                "A↓", LocalizationManager.T("ServerMonitor.Infobase.SortAsc"), () => _vm.SortInfobases(false));
            var sortDescButton = BuildIconButton(
                "Z↓", LocalizationManager.T("ServerMonitor.Infobase.SortDesc"), () => _vm.SortInfobases(true));

            // Порядок кнопок (issue #3): Добавить, Правка | А→Я, Я→А | Удалить.
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    addButton, editButton, BuildToolbarSeparator(),
                    sortAscButton, sortDescButton, BuildToolbarSeparator(),
                    deleteButton
                }
            };

            // Кнопки «Импорт» и «Миграция» (issue #41): внизу панели баз, по аналогии
            // с «Службы» под списком серверов; открывают «Импорт из кластера 1С» /
            // «Миграцию между кластерами 1С» с текущим сервером.
            var importButton = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("ServerMonitor.Infobase.Import"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeight.SemiBold
                },
                Padding = new Thickness(0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            importButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(importButton, Button.BackgroundProperty, "ItemHoverBrush");
            importButton.Click += (_, _) => OpenInfobaseImport();

            var migrateButton = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("ServerMonitor.Infobase.Migrate"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeight.SemiBold
                },
                Padding = new Thickness(0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            migrateButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(migrateButton, Button.BackgroundProperty, "ItemHoverBrush");
            migrateButton.Click += (_, _) => OpenInfobaseMigration();

            // Кнопки растягиваются и делят ширину колонки пополам с учётом отступа
            // (issue #41, комментарий 7OH): в горизонтальном StackPanel Stretch не
            // работает, контейнер — Grid с двумя звездными колонками.
            var bottomButtons = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(8, GridUnitType.Pixel)),
                    new ColumnDefinition(GridLength.Star)
                },
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetColumn(importButton, 0);
            Grid.SetColumn(migrateButton, 2);
            bottomButtons.Children.Add(importButton);
            bottomButtons.Children.Add(migrateButton);
            DockPanel.SetDock(bottomButtons, Dock.Bottom);

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_infobasesTitle, Dock.Top);
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(_infobasesEmptyHint, Dock.Top);
            dock.Children.Add(_infobasesTitle);
            dock.Children.Add(toolbar);
            dock.Children.Add(_infobasesEmptyHint);
            dock.Children.Add(bottomButtons);
            dock.Children.Add(_infobasesList);
            return dock;
        }

        /// <summary>
        /// «Импорт» (issue #27): открывает «Импорт из кластера 1С» с текущим сервером —
        /// выбранный в списке, иначе подключённый, иначе первый из справочника. При
        /// пустом справочнике — подсказка и выход. Сам импорт выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OpenInfobaseImport()
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                AppServices.GetRequiredService<IDialogService>()
                    .ShowInfo(
                        LocalizationManager.T("ClusterImport.ServersEmpty"),
                        LocalizationManager.T("ServerMonitor.Title"));
                return;
            }

            _openClusterImport?.Invoke(server, _vm.SelectedClusterId);
        }

        /// <summary>
        /// «Миграция» (issue #41): открывает «Миграцию между кластерами 1С» с текущим
        /// сервером — выбранный в списке, иначе подключённый, иначе первый из справочника.
        /// При пустом справочнике — подсказка и выход. Сам перенос выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OpenInfobaseMigration()
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                AppServices.GetRequiredService<IDialogService>()
                    .ShowInfo(
                        LocalizationManager.T("ClusterImport.ServersEmpty"),
                        LocalizationManager.T("ServerMonitor.Title"));
                return;
            }

            _openClusterMigration?.Invoke(server, _vm.SelectedClusterId);
        }

        /// <summary>Вертикальный разделитель групп кнопок панели баз (issue #3).</summary>
        private static Control BuildToolbarSeparator()
        {
            var separator = new Border
            {
                Width = 1,
                Margin = new Thickness(3, 4),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            ThemeBrushes.Bind(separator, Border.BackgroundProperty, "BorderColorBrush");
            return separator;
        }

        /// <summary>Список баз: одна колонка с именем; выбор строки включает фильтр.</summary>
        private ListBox BuildInfobaseList() => new()
        {
            ItemTemplate = new FuncDataTemplate<RacInfobaseRow>((infobase, _) =>
            {
                var text = new TextBlock
                {
                    Text = infobase.Name,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return text;
            })
        };

        /// <summary>«Добавить базу»: диалог создания (rac «infobase create») → VM → обновление списков.</summary>
        private void OnAddInfobase()
        {
            var dialog = new RacInfobaseCreateWindow();
            if (!dialog.ShowSync(this) || dialog.Result is not { } request)
                return;
            _ = _vm.CreateInfobaseAsync(request);
        }

        /// <summary>«Изменить базу» (issue #3): диалог правки (поля предзаполнены,
        /// rac «infobase update») → VM → обновление списков.</summary>
        private void OnEditInfobase()
        {
            if (_vm.SelectedInfobase is not { } row || row.InfobaseId == System.Guid.Empty)
                return;
            // Полный диалог свойств (issue #3, 7OH 17.10): «infobase info» с учётными
            // данными администратора ИБ из списка баз приложения либо введёнными вручную.
            System.Collections.Generic.IEnumerable<Infobase>? bases = null;
            try
            {
                bases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
            }
            catch
            {
                // Список баз недоступен — учётные данные пользователь введёт сам.
            }
            var propertiesVm = _vm.CreateInfobasePropertiesViewModel(SummaryFromRow(row), bases);
            var dialog = new RacInfobasePropertiesWindow(propertiesVm);
            if (!dialog.ShowSync(this) || dialog.Result is not { } update)
                return;
            _ = _vm.UpdateSelectedInfobaseAsync(update);
        }

        /// <summary>
        /// «Удалить базу» (issue #3, комментарий 7OH от 2026-10-17): диалог удаления
        /// с выбором «Просто удалить из кластера» / «Удалить с удалением на сервере
        /// СУБД» (клиент-серверные базы) → rac «infobase drop [--drop-database]».
        /// </summary>
        private async void OnDeleteInfobase()
        {
            if (_vm.SelectedInfobase is not { } row)
                return;
            var dialog = new RacInfobaseDropWindow(
                row.Name, ServerMonitorViewModel.CanDropDatabaseOnServer(row), row.Dbms, row.DbServer);
            if (!dialog.ShowSync(this))
                return;
            await _vm.DropSelectedInfobaseAsync(dialog.DropDatabase);
        }

        // ===================== Кнопка «СЛУЖБЫ» (issue #3, 18.10 08:57) =====================
        // Кнопка внизу левой колонки справочника серверов показывает подменю служб —
        // то же, что в трее (ServiceTrayMenuBuilder); результат адаптируется к ContextMenu.

        /// <summary>«СЛУЖБЫ»: показывает подменю служб у кнопки (скин — как у меню
        /// служб главного окна, issue #3, часть 2).</summary>
        private void ShowServicesMenu(Button anchor)
        {
            var menu = new ContextMenu().Styled(Themes.ControlThemes.ModernContextMenu);
            foreach (var item in BuildServicesMenuItems())
                menu.Items.Add(ToAvaloniaMenuItem(item));
            menu.Open(anchor);
        }

        /// <summary>Пункты подменю «Службы» по services.json и статусам из кэша
        /// (issue #3, часть 2): построение не блокирует UI — статусы читаются
        /// из <see cref="ServiceStatusCache"/> и обновляются фоном (как в трее).</summary>
        private List<ServiceMenuItem> BuildServicesMenuItems()
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            var store = AppServices.TryGetService<IServicesStore>();
            var data = store?.Load() ?? new ServiceStoreData();
            var builder = new ServiceTrayMenuBuilder();
            if (services is null)
            {
                // Контроллер недоступен — команды неактивны; остаются «Настроить…»
                // и «Все сервисы».
                return builder.Build(data, _ => new ServiceInfo { Status = ServiceStatus.Unknown }, ServicePlatform.Linux);
            }
            // Мгновенный показ: статусы из кэша (фоновый таймер держит их свежими).
            EnsureServicesStatusTimer();
            return builder.Build(data,
                name => ServicesStatusCache()?.GetStatus(name) ?? new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown },
                ServicePlatform.Linux);
        }

        /// <summary>Единый кэш статусов из DI (issue #3, часть 2): тот же экземпляр,
        /// что у трея и главного окна.</summary>
        private Services.ServiceStatusCache? ServicesStatusCache()
            => AppServices.TryGetService<Services.ServiceStatusCache>();

        /// <summary>Запускает фоновый таймер обновления кэша статусов (5 с).
        /// Опросы — в рабочих потоках, UI не блокируется.</summary>
        private void EnsureServicesStatusTimer()
        {
            if (_servicesStatusTimer is not null)
                return;
            _servicesStatusTimer = new System.Threading.Timer(
                _ => RefreshServicesStatusesInBackground(),
                null,
                TimeSpan.Zero, // первый опрос сразу — прогрев кэша
                ServicesStatusRefreshInterval);
        }

        /// <summary>Фоновое обновление кэша статусов по списку из services.json.</summary>
        private void RefreshServicesStatusesInBackground()
        {
            var cache = ServicesStatusCache();
            var store = AppServices.TryGetService<IServicesStore>();
            if (cache is null || store is null)
                return;
            var data = store.Load();
            var names = ServiceStoreData.NormalizeOrder(data)
                .Where(k => !ServiceStoreData.IsGroupKey(k))
                .Concat(data.Groups.SelectMany(g => g.Services))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
                return;
            _ = cache.RefreshAsync(names);
        }

        /// <summary>Адаптер пункта модели меню служб к элементу Avalonia ContextMenu:
        /// те же иконки, что в контекстном меню основного окна (1.0.0.14); стиль
        /// скина ModernMenuItem — как у остальных контекстных меню (issue #3, часть 2).</summary>
        private object ToAvaloniaMenuItem(ServiceMenuItem item)
        {
            switch (item.Kind)
            {
                case ServiceMenuItemKind.Separator:
                    return new Separator();

                case ServiceMenuItemKind.Settings:
                {
                    var settings = new MenuItem { Header = item.Text }.Styled(Themes.ControlThemes.ModernMenuItem);
                    settings.Icon = MenuIcon("⚙", "#6B7280");
                    settings.Click += (_, _) => ShowServicesSettings();
                    return settings;
                }

                case ServiceMenuItemKind.ServiceSubmenu:
                case ServiceMenuItemKind.GroupSubmenu:
                case ServiceMenuItemKind.AllServicesSubmenu:
                {
                    var submenu = new MenuItem { Header = item.Text, IsEnabled = item.IsEnabled }
                        .Styled(Themes.ControlThemes.ModernMenuItem);
                    // Группа — «микс» папка + кружок статуса (замечание 7OH 19.10),
                    // как в трее и в меню главного окна; «Все сервисы» — ☰.
                    submenu.Icon = item.Kind switch
                    {
                        ServiceMenuItemKind.ServiceSubmenu => StatusCircle(item.StatusColorHex),
                        ServiceMenuItemKind.GroupSubmenu => MenuGroupIcon(item.StatusColorHex),
                        _ => MenuIcon("☰", "@PurpleAccentBrush")
                    };
                    foreach (var child in item.Children)
                        submenu.Items.Add(ToAvaloniaMenuItem(child));
                    return submenu;
                }

                case ServiceMenuItemKind.ServiceCommand:
                case ServiceMenuItemKind.GroupCommand:
                case ServiceMenuItemKind.AllServicesCommand:
                {
                    var command = new MenuItem { Header = item.Text, IsEnabled = item.IsEnabled }
                        .Styled(Themes.ControlThemes.ModernMenuItem);
                    var action = item.Action ?? Services.ServiceAction.Start;
                    command.Icon = MenuIcon(
                        ViewModels.ServiceCommandGlyphs.Glyph(action),
                        ViewModels.ServiceCommandGlyphs.ColorHex(action));
                    var target = item.Target;
                    var targetName = item.TargetName;
                    var names = item.ServiceNames;
                    command.Click += (_, _) => ExecuteServicesOperation(action, target, targetName, names);
                    return command;
                }

                default:
                    return new MenuItem { Header = item.Text, IsEnabled = item.IsEnabled }
                        .Styled(Themes.ControlThemes.ModernMenuItem);
            }
        }

        /// <summary>Цветной глиф-иконка пункта меню (Avalonia).</summary>
        private static Control MenuIcon(string text, string colorHex) => new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse(colorHex))
        };

        /// <summary>Кружок статуса службы в пункте меню (цвет StatusColorHex).</summary>
        private static Control StatusCircle(string colorHex)
        {
            var circle = new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(4.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(colorHex))
            {
                try { circle.Background = new SolidColorBrush(Color.Parse(colorHex)); }
                catch { circle.Background = Themes.ThemeBrushes.Get("TextSecondaryBrush", "#64748B"); }
            }
            else
            {
                // Без статуса — тематизированная нейтральная кисть (аудит UI) вместо Brushes.Gray.
                circle.Background = Themes.ThemeBrushes.Get("TextSecondaryBrush", "#64748B");
            }
            return circle;
        }

        /// <summary>
        /// Иконка группы служб в пункте меню — «микс» (замечание 7OH 19.10): папка 📦 +
        /// кружок статуса группы справа-снизу (цвет StatusColorHex — зелёный/жёлтый/серый),
        /// как в трее и в меню главного окна. При отсутствии статуса (кэш пуст, локальная
        /// машина) — обычная папка без кружка.
        /// </summary>
        private static Control MenuGroupIcon(string statusColorHex)
        {
            var grid = new Grid { Width = 16, Height = 16 };
            grid.Children.Add(new TextBlock
            {
                Text = "📦",
                FontSize = 13,
                Foreground = Themes.ThemeBrushes.Get("PurpleAccentBrush", "#7C3AED"), // fallback
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrWhiteSpace(statusColorHex))
            {
                try
                {
                    grid.Children.Add(new Border
                    {
                        Width = 6,
                        Height = 6,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.Parse(statusColorHex)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 1, 1),
                        BorderBrush = Themes.ThemeBrushes.Get("CardBackgroundBrush", "#FFFFFF"),
                        BorderThickness = new Thickness(1)
                    });
                }
                catch
                {
                    // Неизвестный цвет статуса не должен ломать построение меню.
                }
            }
            return grid;
        }

        /// <summary>«Настроить…»: окно «Управление службами» (модально к монитору).</summary>
        private void ShowServicesSettings()
        {
            var window = new ServicesWindow();
            window.ShowSync(this);
        }

        /// <summary>
        /// Выполняет операцию над целевыми службами (отдельная служба / члены группы /
        /// все службы списка): systemctl блокирующий — работа в фоновом потоке.
        /// После завершения кэш статусов сбрасывается и опрашивается заново (как в трее).
        /// </summary>
        private void ExecuteServicesOperation(
            ServiceAction action,
            ServiceMenuItemTarget target,
            string targetName,
            IReadOnlyList<string> serviceNames)
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            if (services is null)
                return;

            var names = target switch
            {
                ServiceMenuItemTarget.Group or ServiceMenuItemTarget.All => serviceNames.ToList(),
                _ => new List<string> { targetName }
            };
            if (names.Count == 0)
                return;

            Task.Run(() =>
            {
                foreach (var name in names)
                {
                    try
                    {
                        // Статусы актуализируем на момент операции: доступность могла измениться.
                        var info = services.GetStatus(name);
                        if (!ServiceActionAvailability.CanExecute(
                                action, info.Status, info.CanPauseAndContinue, info.CanStop, ServicePlatform.Linux))
                            continue;
                        switch (action)
                        {
                            case Services.ServiceAction.Start: services.Start(name); break;
                            case Services.ServiceAction.Stop: services.Stop(name); break;
                            case Services.ServiceAction.Pause: services.Pause(name); break;
                            case Services.ServiceAction.Continue: services.Continue(name); break;
                            case Services.ServiceAction.Restart: services.Restart(name); break;
                        }
                    }
                    catch
                    {
                        // Отдельная служба не должна останавливать остальные.
                    }
                }
            }).ContinueWith(_ =>
            {
                // Статусы изменились — сбрасываем кэш и опрашиваем заново в фоне.
                AppServices.TryGetService<ServiceStatusCache>()?.Invalidate();
                RefreshServicesStatusesInBackground();
            }, TaskScheduler.Default);
        }

        /// <summary>Строит «infobase summary» из строки списка для предзаполнения диалога правки.</summary>
        private static Workspace1C.Models.RacInfobaseSummary SummaryFromRow(RacInfobaseRow row) => new()
        {
            InfobaseId = row.InfobaseId,
            Name = row.Name,
            Descr = Unplacehold(row.Descr),
            Dbms = row.Dbms,
            DbServer = row.DbServer,
            DbName = row.DbName,
            DbUser = Unplacehold(row.DbUser),
            Locale = Unplacehold(row.Locale),
            SecurityLevel = row.SecurityLevel
        };

        /// <summary>Заменяет плейсхолдер «—» (пустая строка в выводе rac) на пустоту.</summary>
        private static string Unplacehold(string value) =>
            string.Equals(value, "—", StringComparison.Ordinal) ? string.Empty : value;

        // ===================== Строки вкладок =====================

        private static Control CellText(string property, bool bold = false, string? colorHex = null)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = colorHex is null ? null : new SolidColorBrush(Color.Parse(colorHex))
            };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        private static Control BuildProcessRow(object item)
        {
            var row = (RacProcessRow)item;
            var runningDot = new TextBlock
            {
                Text = row.Running ? "●" : "○",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse(row.StateColorHex))
            };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(80)),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(30))
                }
            };
            AddCell(grid, CellText("Type"), 0);
            AddCell(grid, CellText("Host"), 1);
            AddCell(grid, CellText("Pid", bold: true), 2);
            AddCell(grid, CellText("Port"), 3);
            AddCell(grid, CellText("StartedAtText"), 4);
            AddCell(grid, CellText("MemorySizeText"), 5);
            AddCell(grid, CellText("Threads"), 6);
            AddCell(grid, CellText("CpuText"), 7);
            AddCell(grid, CellText("RunningText"), 8);
            AddCell(grid, CellText("Infobases"), 9);
            AddCell(grid, runningDot, 10);
            return grid;
        }

        private static Control BuildSessionRow(object item)
        {
            var row = (RacSessionRow)item;
            var blockedDot = new TextBlock
            {
                Text = row.BlockedSymbol,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Themes.ThemeBrushes.Get("WarningBrush", "#D97706")
            };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    // issue #324, C2: колонка «Информационная база» (имя из
                    // «infobase summary list» по infobase-id сеанса).
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(40)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(80))
                }
            };
            AddCell(grid, CellText("User", bold: true), 0);
            AddCell(grid, CellText("InfobaseName"), 1);
            AddCell(grid, CellText("Host"), 2);
            AddCell(grid, CellText("AppId"), 3);
            AddCell(grid, CellText("StartedAtText"), 4);
            AddCell(grid, CellText("LastActiveAtText"), 5);
            AddCell(grid, CellText("StateText", colorHex: row.StateColorHex), 6);
            AddCell(grid, blockedDot, 7);
            AddCell(grid, CellText("MemoryText"), 8);
            AddCell(grid, CellText("DurationAllText"), 9);
            AddCell(grid, CellText("DurationCurrentText"), 10);
            AddCell(grid, CellText("HibernateText"), 11);
            return grid;
        }

        private static Control BuildConnectionRow(object item)
        {
            var row = (RacConnectionRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(130)),
                    new ColumnDefinition(new GridLength(100)),
                    new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(110))
                }
            };
            AddCell(grid, CellText("Host"), 0);
            AddCell(grid, CellText("Port"), 1);
            AddCell(grid, CellText("Connector"), 2);
            AddCell(grid, CellText("EstablishedAtText"), 3);
            AddCell(grid, CellText("LastConnectionTimeText"), 4);
            AddCell(grid, CellText("DurationText"), 5);
            AddCell(grid, CellText("Descr"), 6);
            AddCell(grid, CellText("BlockedText", colorHex: row.BlockedColorHex), 7);
            return grid;
        }

        private static Control BuildLockRow(object item)
        {
            var row = (RacLockRow)item;
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.1, GridUnitType.Star))
                }
            };
            AddCell(grid, CellText("Object", bold: true), 0);
            AddCell(grid, CellText("WaitingSymbol", colorHex: "#D97706"), 1);
            AddCell(grid, CellText("BlockingSymbol", colorHex: "#DC2626"), 2);
            AddCell(grid, CellText("SessionId"), 3);
            AddCell(grid, CellText("ConnectionId"), 4);
            AddCell(grid, CellText("TransactionId"), 5);
            return grid;
        }

        /// <summary>
        /// Вертикальные линии между колонками строки таблицы (issue #3, 7OH 17.10):
        /// тонкая граница у правого края каждой ячейки, на всю высоту строки.
        /// </summary>
        private static void AddColumnLines(Grid grid, int columnCount)
        {
            for (var i = 0; i < columnCount; i++)
            {
                var line = new Border
                {
                    Width = 1,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false
                };
                ThemeBrushes.Bind(line, Border.BackgroundProperty, "BorderColorBrush");
                Grid.SetColumn(line, i);
                grid.Children.Add(line);
            }
        }

        private static void AddCell(Grid grid, Control control, int column)
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        // ===================== Синхронизация =====================

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Заголовки вкладок с количеством строк (issue #3: «Сеансы (12)»).
            if (e.PropertyName is nameof(ServerMonitorViewModel.SessionsTabText)
                or nameof(ServerMonitorViewModel.ConnectionsTabText)
                or nameof(ServerMonitorViewModel.LocksTabText)
                or nameof(ServerMonitorViewModel.ProcessesTabText))
            {
                UpdateDynamicTabHeaders(e.PropertyName);
            }

            // Заголовок списка баз с количеством строк («Базы (35)»).
            if (e.PropertyName == nameof(ServerMonitorViewModel.InfobasesHeaderText) && _infobasesTitle is not null)
            {
                _infobasesTitle.Text = _vm.InfobasesHeaderText;
            }

            if (e.PropertyName == nameof(ServerMonitorViewModel.HasInfobases) && _infobasesEmptyHint is not null)
            {
                // Подсказка «баз нет» видна только при пустом списке.
                _infobasesEmptyHint.IsVisible = !_vm.HasInfobases;
                return;
            }

            // issue #324 (0.3.9.330): ItemsSource комбо кластеров задаётся в конструкторе,
            // когда ClusterRows ещё пуст, — после подключения список надо обновить,
            // иначе в выпадающем списке нечего выбирать.
            if (e.PropertyName == nameof(ServerMonitorViewModel.ClusterRows))
            {
                _clusterCombo.ItemsSource = _vm.ClusterRows;
                return;
            }

            if (e.PropertyName != nameof(ServerMonitorViewModel.SelectedClusterId))
                return;
            if (_vm.SelectedClusterId is Guid id)
            {
                var row = _vm.ClusterRows.FirstOrDefault(r => r.Id == id);
                if (row is not null && !ReferenceEquals(_clusterCombo.SelectedItem, row))
                    _clusterCombo.SelectedItem = row;
            }
        }

        // ===================== Справочник «Сервера управления» (issue #3) =====================

        /// <summary>«Добавить»: модальный редактор новой записи → добавление в справочник.</summary>
        private void OnAddServer()
        {
            var edit = new ServerEditWindow();
            if (!edit.ShowSync(this) || edit.Result is not { } created)
                return;
            _vm.AddServer(created);
        }

        /// <summary>«Изменить»: модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditServer()
        {
            if (_serversList.SelectedItem is not ManagementServer server)
                return;
            var edit = new ServerEditWindow(CloneServer(server));
            if (!edit.ShowSync(this) || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.
            _vm.UpdateServer(updated);
        }

        /// <summary>«Удалить»: подтверждение → удаление записи из справочника.</summary>
        private void OnDeleteServer()
        {
            if (_serversList.SelectedItem is not ManagementServer server)
                return;
            if (!AppServices.GetRequiredService<IDialogService>().Confirm(
                    string.Format(LocalizationManager.T("ServerMonitor.Servers.ConfirmDelete"), server.Description),
                    LocalizationManager.T("ServerMonitor.Servers.Title")))
                return;
            _vm.RemoveServer(server);
        }

        /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
        private static ManagementServer CloneServer(ManagementServer source) => new()
        {
            Id = source.Id,
            Description = source.Description,
            Address = source.Address,
            Port = source.Port,
            User = source.User,
            Password = source.Password,
        };
    }
}
#endif