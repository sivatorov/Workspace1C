using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Серверы 1С» (0.3.9.124, цикл 0.3.9.123–0.3.9.126): подключение
/// к серверу 1С через rac (адрес/порт/логин/пароль), список кластеров, вкладки
/// «Рабочие процессы / Сеансы / Соединения / Блокировки / Информация о кластере»,
/// ручное обновление, действия (завершение сеанса / разрыв соединения с
/// подтверждением) и автообновление по таймеру 5 с (этап 3, 0.3.9.125).
/// Чистый .NET без платформенных зависимостей — обе платформы (WPF и Avalonia);
/// окна только привязываются.
/// </summary>
public sealed class ServerMonitorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Период автообновления данных кластера, миллисекунды (~5 секунд).</summary>
    public const int AutoRefreshIntervalMs = 5000;

    /// <summary>
    /// Тег источника rac-команд окна «Серверы 1С» в CM_RAC-логе (issue #90): по метке
    /// «RAC [MONITOR]: …» в журнале видно, что вызов пришёл из монитора, а не из
    /// фонового ServerAlertsService («RAC [ALERTS]: …»).
    /// </summary>
    internal const string MonitorLogSourceTag = "MONITOR";

    private readonly IRacClient _rac;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IServerListStore? _serverListStore;

    /// <summary>
    /// Получение установленных программных лицензий (п.5 «Мониторинг лицензий»,
    /// выпуск 1.0.0.87: «ring license list» / licensetool на Linux). null — сервис
    /// недоступен (тесты) — вкладка «Лицензии» показывает только занятые сеансами
    /// лицензии без сводки «всего/свободно».
    /// </summary>
    private readonly IRingLicenseService? _ringLicenses;

    /// <summary>
    /// Оповещения о нехватке лицензий (п.5, 1.0.0.87): при загрузке данных кластера
    /// проверяется порог «свободно меньше N» из настроек уведомлений. null — недоступно
    /// (тесты) — проверка порога пропускается.
    /// </summary>
    private readonly LicenseManager? _licenseAlerts;

    /// <summary>
    /// Показ диалога повтора удаления базы из кластера (issue #69): реализует окно
    /// монитора (WPF/Avalonia), чтобы VM оставалась тестируемой. Получает текст
    /// ошибки, логин и пароль администратора ИБ с прошлой попытки (пароль сохраняется
    /// между попытками, комментарий 7OH от 2026-10-26). null — диалог недоступен
    /// (тесты без колбэка): используется прежнее поведение — предупреждение с одной
    /// кнопкой OK.
    /// </summary>
    private readonly Func<string, string?, string?, DropRetryResult>? _dropRetryDialog;

    /// <summary>
    /// Провайдер асинхронной паузы (issue #69): в продакшене null (используется
    /// Task.Delay), в тестах — мгновенная задержка, чтобы ретраи завершения сеансов
    /// не ждали реальные 1–2 секунды.
    /// </summary>
    private readonly Func<TimeSpan, Task>? _delayAsync;

    private Timer? _autoRefreshTimer;
    private int _busy;

    /// <summary>
    /// Токен текущего цикла загрузки вкладок (issue #90, репорт 7OH «RAC-команды
    /// продолжают сыпаться в лог после закрытия окна»): выключение автообновления и
    /// закрытие окна отменяют выполняющийся цикл rac-вызовов, а не только таймер.
    /// </summary>
    private CancellationTokenSource? _refreshCts;

    /// <summary>
    /// Guard «окно закрыто» (issue #90): после <see cref="Dispose"/> таймер автообновления
    /// не возрождается (защита от гонки «Refresh завершился после закрытия окна» — п.2089
    /// прежнего кода), а обновления свойств UI пропускаются.
    /// </summary>
    private bool _disposed;

    private string _serverAddress = "localhost";
    private int _serverPort = IRacClient.DefaultPort;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private ManagementServer? _selectedServer;
    private ManagementServer? _connectedServer;
    private Guid? _selectedClusterId;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasConnected;
    private RacClusterInfo? _clusterInfo;
    private string _clusterInfoText = string.Empty;
    private string _licenseSummaryText = string.Empty;
    private string _licenseStatusText = string.Empty;
    private RacSessionRow? _selectedSession;
    private RacConnectionRow? _selectedConnection;

    /// <summary>Мультивыделение сеансов (issue #72): набор строк вкладки «Сеансы».</summary>
    private IReadOnlyList<RacSessionRow> _selectedSessions = Array.Empty<RacSessionRow>();

    /// <summary>Мультивыделение соединений (issue #72): набор строк вкладки «Соединения».</summary>
    private IReadOnlyList<RacConnectionRow> _selectedConnections = Array.Empty<RacConnectionRow>();
    private Guid? _selectedInfobaseId;
    private RacInfobaseRow? _selectedInfobase;

    private ICommand? _connectCommand;
    private ICommand? _refreshCommand;
    private ICommand? _terminateSessionCommand;
    private ICommand? _disconnectConnectionCommand;
    private ICommand? _toggleAutoRefreshCommand;
    private ICommand? _saveClusterPropertiesCommand;
    private ICommand? _dropInfobaseCommand;
    private ICommand? _clearInfobaseFilterCommand;

    /// <summary>Переключатель автообновления (issue #324): включено по умолчанию.</summary>
    private bool _isAutoRefreshEnabled = true;

    /// <summary>Интервал автообновления, секунды (по умолчанию 5; диапазон 1–60).</summary>
    private int _autoRefreshIntervalSeconds = AutoRefreshIntervalMs / 1000;

    /// <summary>
    /// Кэш имён информационных баз выбранного кластера (GUID из «job list» → имя из
    /// «infobase summary list»). Заполняется при загрузке данных кластера; задания без
    /// базы и неизвестные GUID показываются как «—».
    /// </summary>
    private readonly Dictionary<Guid, string> _infobaseNames = new();

    /// <param name="rac">Клиент rac (список кластеров, данные кластера).</param>
    /// <param name="dialogs">Диалоги (сообщения об ошибках; подтверждения действий — этап 3).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    /// <param name="serverListStore">Хранилище справочника «Сервера управления» (issue #3);
    /// null — справочник недоступен (тесты без хранилища).</param>
    /// <param name="dropRetryDialog">
    /// Показ диалога повтора удаления базы из кластера (issue #69): получает текст
    /// ошибки, логин и пароль администратора ИБ с прошлой попытки, возвращает
    /// решение пользователя. Реализует окно монитора — чтобы VM оставалась
    /// тестируемой; null — диалог недоступен, при ошибке удаления показывается
    /// прежнее предупреждение с одной кнопкой OK.
    /// </param>
    public ServerMonitorViewModel(
        IRacClient rac,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null,
        IServerListStore? serverListStore = null,
        Func<string, string?, string?, DropRetryResult>? dropRetryDialog = null,
        IRingLicenseService? ringLicenses = null,
        LicenseManager? licenseAlerts = null,
        int licenseFreeThreshold = 0,
        int licenseAlertRepeatHours = 24,
        Func<TimeSpan, Task>? asyncDelay = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;
        _serverListStore = serverListStore;
        _dropRetryDialog = dropRetryDialog;
        _ringLicenses = ringLicenses;
        _licenseAlerts = licenseAlerts;
        _licenseFreeThreshold = licenseFreeThreshold;
        _licenseAlertRepeatHours = licenseAlertRepeatHours;
        _delayAsync = asyncDelay;
        LoadServers();
    }

    // ===================== Лицензии (п.5, выпуск 1.0.0.87) =====================

    /// <summary>Порог «свободно меньше N» из настроек уведомлений (0 — отключено).</summary>
    private readonly int _licenseFreeThreshold;

    /// <summary>Не повторять оповещение о лицензиях чаще, чем раз в N часов (настройка уведомлений).</summary>
    private readonly int _licenseAlertRepeatHours;

    /// <summary>
    /// Текст сводки вкладки «Лицензии»: «Всего: N, занято: N, свободно: N» — по
    /// установленным программным лицензиям и сеансам («session list --licenses»);
    /// если установленные лицензии недоступны (нет ring/licensetool) — «Занято
    /// сеансами: N». Internal — для юнит-тестов форматирования.
    /// </summary>
    internal static string BuildLicenseSummaryText(
        IReadOnlyList<RacSessionInfo> licensedSessions, IReadOnlyList<ProgramLicense> installed)
    {
        var snapshot = LicenseCalculator.ComputeSnapshot(licensedSessions, installed);
        if (snapshot.TotalFree is not int free)
        {
            return string.Format(
                LocalizationManager.T("ServerMonitor.Licenses.UsedOnlyFormat"),
                snapshot.TotalUsed);
        }
        return string.Format(
            LocalizationManager.T("ServerMonitor.Licenses.SummaryFormat"),
            snapshot.TotalInstalled, snapshot.TotalUsed, free);
    }

    /// <summary>
    /// Проверка порога «свободно меньше N» при загрузке данных кластера (п.5, 1.0.0.87):
    /// оповещение через <see cref="LicenseManager"/> уходит не чаще раза в N часов.
    /// </summary>
    private void CheckLicenseThresholdAlert(
        IReadOnlyList<RacSessionInfo> licensedSessions, IReadOnlyList<ProgramLicense> installed)
    {
        if (_licenseAlerts is null || _licenseFreeThreshold <= 0)
            return;
        var snapshot = LicenseCalculator.ComputeSnapshot(licensedSessions, installed);
        var label = $"{ServerAddress}{(ServerPort > 0 ? ":" + ServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}";
        _licenseAlerts.CheckAndNotify(label, snapshot, _licenseFreeThreshold, DateTime.Now, _licenseAlertRepeatHours);
    }

    // ===================== Подключение =====================

    /// <summary>
    /// Адрес сервера 1С (host или IP). Нестандартный порт можно указать полем
    /// <see cref="ServerPort"/> или прямо здесь как «host:port» (например «localhost:27545»,
    /// как в командной строке rac) — порт из адреса имеет приоритет (issue #324).
    /// </summary>
    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Порт агента сервера 1С (ragent), по умолчанию 1540; для RAS — 1545.
    /// Некорректные значения (<see cref="IRacClient.DefaultPort"/> при ≤ 0).
    /// Если порт указан в <see cref="ServerAddress"/> как «host:port» — приоритет у него.
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value > 0 ? value : IRacClient.DefaultPort);
    }

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль администратора кластера. Живёт ТОЛЬКО в памяти окна (в тестах сеттер
    /// открытый; в UI пароль передаётся вручную из PasswordBox при подключении),
    /// на диск не сохраняется (см. решения планирования, раздел 5 плана).
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    // ===================== Справочник «Сервера управления» (issue #3) =====================

    /// <summary>
    /// Список серверов справочника «Сервера управления» (issue #3): в окне выводится
    /// слева отдельным списком (только <see cref="ManagementServer.Description"/>);
    /// при активации строки выполняется подключение к кластеру. Хранится в файле
    /// <c>server_list.json</c> через <see cref="IServerListStore"/>.
    /// </summary>
    public ObservableCollection<ManagementServer> Servers { get; } = new();

    /// <summary>Выбранный сервер справочника (строка слева).</summary>
    public ManagementServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                // Доступность «Вверх»/«Вниз» зависит от позиции выбранной строки (issue #3).
                OnPropertyChanged(nameof(CanMoveServerUp));
                OnPropertyChanged(nameof(CanMoveServerDown));
            }
        }
    }

    /// <summary>Можно ли переместить выбранный сервер вверх (есть выбор и не первая строка).</summary>
    public bool CanMoveServerUp => SelectedServer is not null && Servers.IndexOf(SelectedServer) > 0;

    /// <summary>Можно ли переместить выбранный сервер вниз (есть выбор и не последняя строка).</summary>
    public bool CanMoveServerDown
    {
        get
        {
            if (SelectedServer is null || Servers.Count == 0)
                return false;
            var index = Servers.IndexOf(SelectedServer);
            return index >= 0 && index < Servers.Count - 1;
        }
    }

    /// <summary>Перемещает выбранный сервер на одну позицию вверх и сохраняет справочник (issue #3).</summary>
    public void MoveServerUp()
    {
        if (!CanMoveServerUp || _serverListStore is null)
            return;
        var index = Servers.IndexOf(SelectedServer!);
        Servers.Move(index, index - 1);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Перемещает выбранный сервер на одну позицию вниз и сохраняет справочник (issue #3).</summary>
    public void MoveServerDown()
    {
        if (!CanMoveServerDown || _serverListStore is null)
            return;
        var index = Servers.IndexOf(SelectedServer!);
        Servers.Move(index, index + 1);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>
    /// Запись справочника «Сервера управления», к которой установлено текущее подключение
    /// (issue #3, комментарий 7OH 19.10: строка подключённого сервера подсвечивается
    /// жирным). Устанавливается при успешном подключении: в <see cref="ConnectToServerAsync"/>
    /// — выбранной записью, в <see cref="ConnectAsync"/> — записью, совпадающей по
    /// адресу:порту. Сбрасывается при новом подключении и при ошибке подключения.
    /// </summary>
    public ManagementServer? ConnectedServer
    {
        get => _connectedServer;
        private set
        {
            if (ReferenceEquals(_connectedServer, value))
                return;
            _connectedServer = value;
            OnPropertyChanged(nameof(ConnectedServer));
            OnPropertyChanged(nameof(HasConnectedServer));
        }
    }

    /// <summary>Есть ли активное подключение к записи справочника (для подсветки списка).</summary>
    public bool HasConnectedServer => ConnectedServer is not null;

    /// <summary>Есть ли хотя бы один сервер в справочнике (для подсказки пустого списка).</summary>
    public bool HasServers => Servers.Count > 0;

    /// <summary>Перечитывает список серверов из хранилища (при открытии окна).</summary>
    public void LoadServers()
    {
        Servers.Clear();
        if (_serverListStore is null)
        {
            OnPropertyChanged(nameof(HasServers));
            OnPropertyChanged(nameof(CanMoveServerUp));
            OnPropertyChanged(nameof(CanMoveServerDown));
            return;
        }
        foreach (var server in _serverListStore.Load())
            Servers.Add(server);
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Добавляет сервер в справочник и сохраняет файл.</summary>
    public void AddServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        if (string.IsNullOrWhiteSpace(server.Id))
            server.Id = Guid.NewGuid().ToString("N");
        Servers.Add(server);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Обновляет запись справочника (правка) и сохраняет файл.</summary>
    public void UpdateServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        var index = Servers.ToList().FindIndex(s => s.Id == server.Id);
        if (index >= 0)
            Servers[index] = server;
        _serverListStore.Save(Servers.ToList());
    }

    /// <summary>Удаляет сервер из справочника и сохраняет файл.</summary>
    public void RemoveServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        Servers.Remove(server);
        if (ReferenceEquals(SelectedServer, server))
            SelectedServer = null;
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>
    /// Ищет запись справочника по точке подключения (issue #3, комментарий 7OH 19.10):
    /// совпадение хоста и порта. Порт из адреса «host:port» приоритетнее поля записи —
    /// та же нормализация, что в <see cref="BuildParams"/> (issue #324). Возвращает null,
    /// если подходящей записи нет (обычное подключение к «сырому» адресу).
    /// </summary>
    private ManagementServer? FindServerByAddress(string address, int port)
    {
        var (host, targetPort) = RacConnectionAddress.Split(address, port);
        foreach (var server in Servers)
        {
            var (serverHost, serverPort) = RacConnectionAddress.Split(server.Address, server.Port);
            if (serverPort == targetPort &&
                string.Equals(serverHost, host, StringComparison.OrdinalIgnoreCase))
            {
                return server;
            }
        }
        return null;
    }

    /// <summary>
    /// Подключение к кластеру выбранного сервера справочника (issue #3): адрес/порт/
    /// логин/пароль берутся из записи, затем выполняется обычное подключение
    /// (<see cref="ConnectAsync"/>). Вызывается окном при активации строки списка.
    /// В статус-строке указывается имя сервера (описание), чтобы было видно,
    /// к какому кластеру идёт подключение.
    /// </summary>
    public async Task ConnectToServerAsync(ManagementServer server)
    {
        if (server is null)
            return;
        ServerAddress = server.Address;
        ServerPort = server.Port;
        UserName = server.User;
        Password = server.Password;

        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        StatusText = string.Format(
            LocalizationManager.T("ServerMonitor.Servers.ConnectingFormat"),
            server.Description, host, port);

        await ConnectAsync();

        if (!HasConnected)
        {
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Servers.ConnectFailedFormat"),
                server.Description);
        }
        else
        {
            // Успех: подсвечиваем именно кликнутую запись справочника (жирным; issue #3,
            // комментарий 7OH 19.10). При совпадении адреса:порта у нескольких записей
            // выигрывает выбранная пользователем (ConnectAsync нашёл бы первую).
            ConnectedServer = server;
        }
    }

    // ===================== Кластеры =====================

    /// <summary>Кластеры сервера (модели из rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters { get; private set; } = Array.Empty<RacCluster>();

    /// <summary>Кластеры для выпадающего списка (форматированные строки).</summary>
    public IReadOnlyList<RacClusterRow> ClusterRows { get; private set; } = Array.Empty<RacClusterRow>();

    /// <summary>
    /// Выбранный кластер. При установке значения после подключения запускается
    /// загрузка данных кластера (процессы/сеансы/соединения/блокировки/инфо).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set
        {
            if (!SetProperty(ref _selectedClusterId, value))
                return;

            // Смена кластера делает фильтр по базе бессмысленным (идентификаторы
            // баз другого кластера) — сбрасываем на «Все базы». Сбрасываем через
            // поля напрямую, чтобы не запускать перезагрузку данных из сеттера
            // выбора базы (данные нового кластера загрузит ниже).
            if (_selectedInfobaseId is not null || _selectedInfobase is not null)
            {
                _selectedInfobase = null;
                _selectedInfobaseId = null;
                OnPropertyChanged(nameof(SelectedInfobase));
                OnPropertyChanged(nameof(SelectedInfobaseId));
                ApplyInfobaseFilter();
            }

            if (value is Guid id && HasConnected)
                _ = LoadClusterDataAsync(id);
        }
    }

    // ===================== Команды =====================

    /// <summary>
    /// «Подключиться» (issue #3): к кластеру выбранного сервера справочника
    /// (адрес/порт/логин/пароль из записи); при отсутствии выбора — обычное
    /// подключение по текущим полям (дефолт — localhost).
    /// </summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () =>
        {
            if (SelectedServer is { } server)
                await ConnectToServerAsync(server);
            else
                await ConnectAsync();
        });

    /// <summary>«Обновить»: перечитать данные выбранного кластера (после подключения).</summary>
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(Refresh);

    // ===================== Действия =====================

    /// <summary>«Завершить сеанс»: подтверждение → session terminate → обновление списков.</summary>
    public ICommand TerminateSessionCommand =>
        _terminateSessionCommand ??= new RelayCommand(async () => await TerminateSessionAsync());

    /// <summary>«Разорвать соединение»: подтверждение → connection disconnect → обновление списков.</summary>
    public ICommand DisconnectConnectionCommand =>
        _disconnectConnectionCommand ??= new RelayCommand(async () => await DisconnectConnectionAsync());

    // ===================== Вкладки =====================

    /// <summary>Рабочие процессы кластера (rphost/rmngr).</summary>
    public ObservableCollection<RacProcessRow> Processes { get; } = new();

    /// <summary>Сеансы пользователей кластера.</summary>
    public ObservableCollection<RacSessionRow> Sessions { get; } = new();

    /// <summary>Соединения клиентов кластера.</summary>
    public ObservableCollection<RacConnectionRow> Connections { get; } = new();

    /// <summary>Блокировки объектов данных кластера.</summary>
    public ObservableCollection<RacLockRow> Locks { get; } = new();

    /// <summary>
    /// Занятые лицензии по сеансам кластера (п.5 «Мониторинг лицензий», выпуск 1.0.0.87):
    /// данные rac «session list --licenses». Заполняется только распознанными типами
    /// лицензий (сеансы без lic-type не попадают в коллекцию).
    /// </summary>
    public ObservableCollection<RacLicensedSessionRow> LicensedSessions { get; } = new();

    /// <summary>
    /// Установленные программные лицензии (п.5, 1.0.0.87): разбор «ring license list» /
    /// «licensetool». Если утилиты нет — коллекция пуста, причина — в
    /// <see cref="LicenseStatusText"/>.
    /// </summary>
    public ObservableCollection<ProgramLicense> InstalledLicenses { get; } = new();

    /// <summary>Сеансы с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacSessionRow> FilteredSessions { get; } = new();

    /// <summary>Соединения с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacConnectionRow> FilteredConnections { get; } = new();

    /// <summary>Блокировки с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacLockRow> FilteredLocks { get; } = new();

    // ===================== Заголовки вкладок с количеством строк (issue #3) =====================

    /// <summary>Заголовок вкладки «Рабочие процессы» с количеством строк («Рабочие процессы (3)»).</summary>
    public string ProcessesTabText => TabHeader("ServerMonitor.Tabs.Processes", Processes.Count);

    /// <summary>Заголовок вкладки «Сеансы» с количеством показываемых строк («Сеансы (12)»).</summary>
    public string SessionsTabText => TabHeader("ServerMonitor.Tabs.Sessions", FilteredSessions.Count);

    /// <summary>Заголовок вкладки «Соединения» с количеством показываемых строк.</summary>
    public string ConnectionsTabText => TabHeader("ServerMonitor.Tabs.Connections", FilteredConnections.Count);

    /// <summary>Заголовок вкладки «Блокировки» с количеством показываемых строк.</summary>
    public string LocksTabText => TabHeader("ServerMonitor.Tabs.Locks", FilteredLocks.Count);

    /// <summary>
    /// Заголовок вкладки «Лицензии» с количеством строк («Лицензии (3)»): число —
    /// суммарно занятых лицензий в учётных строках (п.5, 1.0.0.87).
    /// </summary>
    public string LicensesTabText => TabHeader("ServerMonitor.Tabs.Licenses", LicensedSessions.Count);

    /// <summary>Заголовок списка информационных баз с количеством строк («Базы (35)»).</summary>
    public string InfobasesHeaderText => TabHeader("ServerMonitor.Tabs.Infobases", Infobases.Count);

    private static string TabHeader(string key, int count) =>
        string.Format(
            LocalizationManager.T("ServerMonitor.Tabs.CountFormat"),
            LocalizationManager.T(key),
            count);

    /// <summary>
    /// Уведомляет UI об изменении заголовков вкладок (количества строк). Вызывается
    /// после пересборки коллекций и после клиентской фильтрации по базе.
    /// </summary>
    private void RaiseTabHeaderChanges()
    {
        OnPropertyChanged(nameof(ProcessesTabText));
        OnPropertyChanged(nameof(SessionsTabText));
        OnPropertyChanged(nameof(ConnectionsTabText));
        OnPropertyChanged(nameof(LocksTabText));
        OnPropertyChanged(nameof(LicensesTabText));
        OnPropertyChanged(nameof(InfobasesHeaderText));
    }

    /// <summary>
    /// Свойства кластера «свойство — значение» на вкладке «Информация о кластере»
    /// (issue #324, C3): ограниченный набор параметров доступен для правки
    /// (см. <see cref="RacClusterPropertyRow.IsEditable"/>), остальные — только чтение.
    /// </summary>
    public ObservableCollection<RacClusterPropertyRow> ClusterProperties { get; } = new();

    /// <summary>«Сохранить изменения» на вкладке «Информация о кластере» (rac «cluster update»).</summary>
    public ICommand SaveClusterPropertiesCommand =>
        _saveClusterPropertiesCommand ??= new RelayCommand(async () => await SaveClusterPropertiesAsync());

    /// <summary>Информация о кластере (команда «cluster info»); null, если не получена.</summary>
    public RacClusterInfo? ClusterInfo
    {
        get => _clusterInfo;
        private set => SetProperty(ref _clusterInfo, value);
    }

    /// <summary>Текст вкладки «Информация о кластере» («ключ: значение» построчно).</summary>
    public string ClusterInfoText
    {
        get => _clusterInfoText;
        private set => SetProperty(ref _clusterInfoText, value);
    }

    /// <summary>
    /// Сводка лицензий вкладки «Лицензии» (п.5, 1.0.0.87):
    /// «Всего: 10, занято: 7, свободно: 3» (по установленным лицензиям и сеансам);
    /// если установленные лицензии недоступны — «Занято сеансами: N».
    /// </summary>
    public string LicenseSummaryText
    {
        get => _licenseSummaryText;
        private set => SetProperty(ref _licenseSummaryText, value);
    }

    /// <summary>
    /// Статус источника установленных лицензий (п.5, 1.0.0.87): предупреждение при
    /// недоступности «ring license list»/«licensetool»/каталога лицензий; пусто — штатно.
    /// </summary>
    public string LicenseStatusText
    {
        get => _licenseStatusText;
        private set => SetProperty(ref _licenseStatusText, value);
    }

    /// <summary>Выбранный сеанс на вкладке «Сеансы» (кнопка «Завершить сеанс»).</summary>
    public RacSessionRow? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (!SetProperty(ref _selectedSession, value))
                return;
            // issue #72: доступность действия зависит и от одиночного выбора.
            OnPropertyChanged(nameof(CanTerminateSessions));
        }
    }

    /// <summary>Выбранное соединение на вкладке «Соединения» (кнопка «Разорвать соединение»).</summary>
    public RacConnectionRow? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (!SetProperty(ref _selectedConnection, value))
                return;
            // issue #72: доступность действия зависит и от одиночного выбора.
            OnPropertyChanged(nameof(CanDisconnectConnections));
        }
    }

    // ===================== Мультивыделение (issue #72) =====================

    /// <summary>
    /// Выбранные сеансы вкладки «Сеансы» (мультивыделение, issue #72): Ctrl — инверсия
    /// выбора, Shift — диапазон. Множество ставит форма (WPF DataGrid.SelectedItems /
    /// Avalonia ListBox.SelectedItems); кнопка «Завершить сеансы…» действует на весь
    /// список последовательно, при одной строке — как одиночное завершение. Для связи
    /// с прежним одиночным выбором первый сеанс дублируется в <see cref="SelectedSession"/>.
    /// </summary>
    public IReadOnlyList<RacSessionRow> SelectedSessions
    {
        get => _selectedSessions;
        set
        {
            if (!SetProperty(ref _selectedSessions, value ?? Array.Empty<RacSessionRow>()))
                return;
            SelectedSession = _selectedSessions.FirstOrDefault();
            OnPropertyChanged(nameof(CanTerminateSessions));
        }
    }

    /// <summary>
    /// Выбранные соединения вкладки «Соединения» (мультивыделение, issue #72): Ctrl —
    /// инверсия, Shift — диапазон. Множество ставит форма (WPF DataGrid.SelectedItems /
    /// Avalonia ListBox.SelectedItems); кнопка «Разорвать соединения» действует на весь
    /// список; при одной строке — как одиночное действие. Первое соединение дублируется
    /// в <see cref="SelectedConnection"/>.
    /// </summary>
    public IReadOnlyList<RacConnectionRow> SelectedConnections
    {
        get => _selectedConnections;
        set
        {
            if (!SetProperty(ref _selectedConnections, value ?? Array.Empty<RacConnectionRow>()))
                return;
            SelectedConnection = _selectedConnections.FirstOrDefault();
            OnPropertyChanged(nameof(CanDisconnectConnections));
        }
    }

    /// <summary>
    /// Доступность «Завершить сеансы» (issue #72): есть выбранные сеансы (либо одиночная
    /// строка) и установлено подключение. При пустом выделении кнопка недоступна — как раньше.
    /// </summary>
    public bool CanTerminateSessions => HasConnected && (SelectedSessions.Count > 0 || SelectedSession is not null);

    /// <summary>
    /// Доступность «Разорвать соединения» (issue #72): есть выбранные соединения (либо
    /// одиночная строка) и установлено подключение. При пустом выделении кнопка недоступна.
    /// </summary>
    public bool CanDisconnectConnections => HasConnected && (SelectedConnections.Count > 0 || SelectedConnection is not null);

    // ===================== Базы (issue #3) =====================

    /// <summary>
    /// Информационные базы выбранного кластера (команда rac «infobase summary list»):
    /// вкладка «Базы». При выборе базы сеансы/соединения/блокировки загружаются
    /// в разрезе базы (параметр rac «--infobase=...»).
    /// </summary>
    public ObservableCollection<RacInfobaseRow> Infobases { get; } = new();

    /// <summary>Есть ли хотя бы одна база в списке (для подсказки пустого списка).</summary>
    public bool HasInfobases => Infobases.Count > 0;

    private bool _infobasesSortDescending;

    /// <summary>
    /// Порядок списка баз (issue #3, 7OH 17.10): базы всегда отсортированы по имени —
    /// по алфавиту (А→Я) или в обратном порядке (Я→А); переключается кнопками над списком.
    /// </summary>
    public bool InfobasesSortDescending
    {
        get => _infobasesSortDescending;
        private set => SetProperty(ref _infobasesSortDescending, value);
    }

    /// <summary>
    /// Сортирует список баз по имени (кнопки «по алфавиту» / «обратная», issue #3).
    /// Выбранная база и фильтр по ней сохраняются; rac не вызывается.
    /// </summary>
    public void SortInfobases(bool descending)
    {
        InfobasesSortDescending = descending;
        var selected = _selectedInfobase;
        var sorted = SortInfobaseRows(Infobases.ToList(), descending);
        ReplaceRows(Infobases, sorted);
        if (selected is not null && Infobases.Contains(selected))
        {
            _selectedInfobase = selected;
            OnPropertyChanged(nameof(SelectedInfobase));
        }
    }

    /// <summary>Чистая сортировка строк баз по имени без учёта регистра (issue #3).</summary>
    internal static IReadOnlyList<RacInfobaseRow> SortInfobaseRows(
        IEnumerable<RacInfobaseRow> rows, bool descending)
    {
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        var list = rows.ToList();
        return (descending
                ? list.OrderByDescending(r => r.Name ?? string.Empty, comparer)
                : list.OrderBy(r => r.Name ?? string.Empty, comparer))
            .ToList();
    }

    /// <summary>
    /// Выбранная база в списке «Базы»: при изменении сеансы/соединения/блокировки
    /// фильтруются на клиенте по выбранной базе (issue #3). Одновременно обновляется
    /// доступность кнопок «Изменить»/«Удалить» (issue #3: раньше кнопка «Удалить»
    /// оставалась неактивной, потому что <see cref="CanDropInfobase"/> не уведомлял
    /// об изменении выбора).
    /// </summary>
    public RacInfobaseRow? SelectedInfobase
    {
        get => _selectedInfobase;
        set
        {
            if (!SetProperty(ref _selectedInfobase, value))
                return;
            ApplySelectedInfobase(value?.InfobaseId);
            OnPropertyChanged(nameof(CanDropInfobase));
        }
    }

    /// <summary>Идентификатор выбранной базы (null — все базы кластера).</summary>
    public Guid? SelectedInfobaseId
    {
        get => _selectedInfobaseId;
        private set => SetProperty(ref _selectedInfobaseId, value);
    }

    /// <summary>
    /// Применяет выбор базы (issue #3): обновляет идентификатор и фильтрует УЖЕ
    /// загруженный снимок кластера на клиенте (сеансы/соединения/блокировки) —
    /// БЕЗ rac-вызовов и без флага занятости. Параметр rac «--infobase=...» на
    /// платформе пользователя не работает (зависание до CommandTimeout), поэтому
    /// данные кластера всегда запрашиваются целиком, а выбор базы лишь скрывает
    /// строки других баз.
    /// </summary>
    private void ApplySelectedInfobase(Guid? infobaseId)
    {
        if (!SetProperty(ref _selectedInfobaseId, infobaseId))
            return;
        ApplyInfobaseFilter();
    }

    /// <summary>Разрешено ли удаление базы (есть выбор и подключение).</summary>
    public bool CanDropInfobase => SelectedInfobase is not null && HasConnected;

    /// <summary>
    /// Можно ли удалить базу вместе с БД на сервере СУБД (комментарий 7OH от 2026-10-17):
    /// вариант диалога удаления доступен ВСЕМ базам списка кластера. В кластере 1С бывают
    /// только клиент-серверные базы (комментарий 7OH от 2026-10-18 20:24 и 19.10 19:12),
    /// поэтому «--drop-database» предлагается всегда; распознанный тип СУБД (белый список
    /// <see cref="IsClientServerDbms"/>) остаётся только диагностической подписью в диалоге
    /// удаления и НЕ блокирует вариант (до 1.0.0.21 блокировал: на версиях rac с нестандартным
    /// значением «dbms» второй пункт не показывался).
    /// </summary>
    public static bool CanDropDatabaseOnServer(RacInfobaseRow? row) => row is not null;

    /// <summary>
    /// Известные клиент-серверные СУБД rac — «белый список» для команды rac
    /// «infobase drop --drop-database»: ровно значения rac «infobase create --dbms»
    /// (комментарий 7OH от 2026-10-18 20:24). До 1.0.0.14 использовался «анти-FILE»
    /// (не пусто И не FILE): на версиях rac, отдающих для файловой базы кластера
    /// иное значение «dbms», файловая база ошибочно получала вариант «с удалением
    /// на сервере СУБД» (комментарий 7OH 18.10 17:16 — «Как может быть файловая
    /// база в кластере?»). Пустое, «File» и незнакомое значения считаются
    /// неизвестными — вариант «с удалением на сервере СУБД» им не предлагается.
    /// </summary>
    internal static bool IsClientServerDbms(string? dbms) =>
        !string.IsNullOrWhiteSpace(dbms) && ClientServerDbmsValues.Contains(dbms.Trim());

    private static readonly HashSet<string> ClientServerDbmsValues = new(StringComparer.OrdinalIgnoreCase)
    {
        RacDbms.MssqlServer,
        RacDbms.PostgreSql,
        RacDbms.IbmDb2,
        RacDbms.OracleDatabase
    };

    /// <summary>
    /// Создание информационной базы в кластере (issue #3): rac «infobase create» по
    /// полям диалога → перечитывание списков. Ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task CreateInfobaseAsync(RacInfobaseCreateRequest request)
    {
        if (request is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        try
        {
            var ok = await _rac.CreateInfobaseAsync(BuildParams(), clusterId, request).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Infobase.CreateFailed") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Infobase.CreateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Status.CreatedFormat"), request.Name);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Infobase.CreateFailed") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Infobase.CreateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Удаление выбранной базы (issue #3): rac «infobase drop» → перечитывание списков.
    /// Вопрос и выбор варианта («просто удалить из кластера» / «удалить с удалением БД на
    /// сервере СУБД», комментарий 7OH от 2026-10-17) задаёт диалог удаления в окне —
    /// сюда попадает уже выбранный флаг <paramref name="dropDatabase"/>. При неудаче
    /// (защищённая база, issue #69) показывается диалог повтора с полями «Логин»/
    /// «Пароль» администратора ИБ: «Повторить попытку» выполняет ещё один
    /// «infobase drop» с этими учётными данными (при повторной ошибке — снова тот же
    /// диалог: логин и пароль сохраняются, а текст помечается
    /// «Повторная попытка не удалась»), «Отмена» — выход. Типовая ошибка rac
    /// распознаётся (<see cref="ClassifyDropError"/>): если базу занимают активные
    /// сеансы/соединения, диалог предлагает «Завершить сеансы и повторить».
    /// Перед КАЖДЫМ «infobase drop» (и при повторе) гарантированно выполняется разрыв
    /// соединений и завершение сеансов именно этой базы
    /// (<see cref="ReleaseInfobaseActivityAsync"/>, комментарий 7OH от 2026-10-26:
    /// «кто-то забыл удалить текущие соединения»); кнопка диалога «Завершить сеансы
    /// и повторить» делает то же самое заранее, с пометкой «Повторная попытка
    /// завершения сеансов не удалась…» при неудаче. Галочка «Установить блокировку
    /// регламентных заданий» (<see cref="DropRetryResult.BlockScheduledJobs"/>, комментарий
    /// 7OH от 2026-10-26) выполняет rac «infobase update --scheduled-jobs-deny=on» ДО
    /// завершения сеансов. Неудача любого из подготовительных шагов цикл НЕ прерывает
    /// (комментарий 7OH от 2026-10-26: «Не выходить после первой попытки... иногда
    /// повторная попытка может помочь») — снова показывается диалог с соответствующей
    /// пометкой. Без колбэка диалога (тесты) — прежнее поведение:
    /// предупреждение + статус-строка.
    /// </summary>
    public async Task DropSelectedInfobaseAsync(bool dropDatabase = false)
    {
        var row = SelectedInfobase;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        // issue #69, комментарий 7OH от 2026-10-27: «отключать автообновление данных
        // на время, если оно было включено» — тик таймера перезапрашивает сеансы/
        // соединения и может пересоздавать соединения во время удаления базы.
        // В finally состояние переключателя восстанавливается.
        var autoRefreshWasEnabled = IsAutoRefreshEnabled;
        if (autoRefreshWasEnabled)
            SetAutoRefreshEnabled(false);

        // Учётные данные администратора ИБ для повторных попыток (issue #69):
        // первый вызов — без кредов; дальше — из ответа диалога повтора.
        string? infobaseUser = null;
        string? infobasePassword = null;
        var attempts = 0;
        // Пометка для следующего показа диалога (issue #69, комментарий 7OH от
        // 2026-10-26): «Повторная попытка завершения сеансов не удалась: <детали rac>» —
        // видно, что завершение сеансов применено, но rac снова вернул ошибку.
        string? retryNote = null;
        try
        {
            while (true)
            {
                var (ok, detail) = await TryDropInfobaseAsync(
                    row, clusterId, dropDatabase, infobaseUser, infobasePassword).ConfigureAwait(false);
                if (ok)
                {
                    StatusText = string.Format(
                        LocalizationManager.T("ServerMonitor.Infobase.Status.DroppedFormat"), row.Name);
                    return;
                }

                attempts++;
                var answer = AskDropRetryOnUi(detail, infobaseUser, infobasePassword, attempts, retryNote);
                retryNote = null;
                if (!answer.Retry)
                {
                    StatusText = attempts > 1
                        ? string.Format(
                            LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryFailedFormat"), detail)
                        : detail;
                    return;
                }

                // Учётные данные администратора ИБ применяются к следующему
                // «infobase drop»; сохраняются между попытками ДО подготовки, чтобы
                // при неудачном завершении сеансов повторный диалог был предзаполнен
                // (комментарий 7OH от 2026-10-26: «Пароль сохранять между попытками»).
                infobaseUser = answer.User;
                infobasePassword = answer.Password;

                // issue #69, комментарий 7OH от 2026-10-26: «сначала ставить блокировку,
                // а потом снимать сеансы» — rac «infobase update --scheduled-jobs-deny=on»
                // перед завершением сеансов и повторным удалением занятой базы (галочка
                // «Установить блокировку регламентных заданий» в диалоге повтора).
                if (answer.BlockScheduledJobs)
                {
                    // Учётные данные администратора ИБ из диалога применяются к rac
                    // «infobase info» (догрузка параметров СУБД) и к «infobase update
                    // --scheduled-jobs-deny=on» (issue #69, комментарий 7OH от 2026-10-27:
                    // «Не удаётся теперь установить блокировку» — на защищённой базе rac
                    // без --infobase-user/--infobase-pwd возвращает «Недостаточно прав
                    // пользователя на информационную базу»).
                    var (blockOk, blockDetail) = await SetScheduledJobsBlockAsync(
                        row, clusterId, infobaseUser, infobasePassword)
                        .ConfigureAwait(false);
                    StatusText = blockDetail;
                    if (!blockOk)
                    {
                        // issue #69, комментарий 7OH от 2026-10-26: отдельного окна об
                        // ошибке блокировки быть НЕ должно — детали показываются строкой
                        // в следующем диалоге повтора (готовое сообщение «Повторная
                        // попытка удаления не удалась: …не удалось установить блокировку
                        // регламентных заданий…»). Цикл НЕ прерывается: пользователь может
                        // повторить попытку либо отменить.
                        retryNote = string.Format(
                            LocalizationManager.T("ServerMonitor.Infobase.Drop.BlockRetryFailedFormat"),
                            detail, blockDetail);
                        continue;
                    }
                }

                if (answer.TerminateSessions)
                {
                    // issue #69: rac не удаляет базу, пока её занимают активные сеансы
                    // (соединения, HTTP-сервисы). Кнопка диалога «Завершить сеансы и
                    // повторить» — явное согласие пользователя на разрыв сеансов; сам
                    // повторный «infobase drop» разрывает соединения/сеансы ВСЕГДА
                    // (комментарий 7OH от 2026-10-26: «кто-то забыл удалить текущие
                    // соединения») — здесь это делается заранее, с готовой пометкой
                    // в диалоге при неудаче.
                    // Учётные данные администратора ИБ передаются в «connection
                    // disconnect»/«session terminate» (issue #69, комментарий 7OH от
                    // 2026-10-27: «удалять сеансы не хочет» — rac на защищённой базе
                    // без кредов возвращает «Недостаточно прав пользователя»).
                    // issue #69, комментарий 7OH от 2026-10-28: после УСПЕШНОЙ установки
                    // блокировки регламентных заданий выполняется НЕСКОЛЬКО попыток
                    // завершения сеансов/разрыва соединений с небольшими паузами и
                    // индикацией «Попытка N из M…» в статус-строке: rac может вернуть
                    // «Информационная база имеет непустой список соединений» или «Попытка
                    // подключения к освобождаемому контексту…» — следующий проход (после
                    // короткой паузы) добивает остаток.
                    var (termOk, termDetail) = await ReleaseInfobaseActivityWithRetriesAsync(
                        row.InfobaseId, clusterId, infobaseUser, infobasePassword)
                        .ConfigureAwait(false);
                    StatusText = termDetail;
                    if (!termOk)
                    {
                        // issue #69, комментарий 7OH от 2026-10-26: НЕ выходить после
                        // первой неудачной попытки — число попыток ограничено
                        // (MaxSessionReleaseAttempts); при исчерпании — понятное сообщение
                        // «Не удалось завершить сеансы за N попыток: <последняя ошибка>»
                        // и снова диалог повтора («Завершить сеансы и повторить» / «Отмена»).
                        retryNote = string.Format(
                            LocalizationManager.T("ServerMonitor.Infobase.Drop.SessionReleaseExhaustedFormat"),
                            MaxSessionReleaseAttempts, termDetail);
                        continue;
                    }
                }
            }
        }
        finally
        {
            Refresh();
            // issue #69, комментарий 7OH от 2026-10-27: автообновление возвращается
            // в исходное состояние после операции удаления базы.
            if (autoRefreshWasEnabled)
                SetAutoRefreshEnabled(true);
        }
    }

    /// <summary>
    /// Одна попытка «infobase drop» (исходная или повторная с учётными данными
    /// администратора ИБ, issue #69): исключения rac превращаются в текст ошибки,
    /// наружу не пробрасываются — цикл повтора решает, пробовать ли ещё.
    /// </summary>
    private async Task<(bool Ok, string Detail)> TryDropInfobaseAsync(
        RacInfobaseRow row, Guid clusterId, bool dropDatabase, string? infobaseUser, string? infobasePassword)
    {
        try
        {
            // issue #69, комментарий 7OH от 2026-10-26: перед КАЖДЫМ «infobase drop»
            // (и при повторе) гарантированно разрываем соединения и завершаем сеансы
            // именно этой базы — иначе rac возвращает «Информационная база имеет
            // непустой список соединений» (в логе: drop → «непустой список соединений»
            // → infobase info → снова drop → опять «непустой список соединений»).
            // Список соединений/сеансов запрашивается ЗАНОВО до трёх проходов
            // (race-free) — новые соединения, появившиеся между «connection list»
            // и «connection disconnect», добиваются следующей итерацией.
            var (releaseOk, releaseDetail, disconnected, terminated) = await ReleaseInfobaseActivityAsync(
                row.InfobaseId, clusterId, infobaseUser, infobasePassword).ConfigureAwait(false);
            if (!releaseOk)
                return (false, releaseDetail);
            if (disconnected > 0 || terminated > 0)
            {
                // Сводка шагов остаётся в статус-строке: «Перед удалением завершено
                // соединений: N, сеансов: M.»
                StatusText = releaseDetail;
            }

            var ok = await _rac.DropInfobaseAsync(
                BuildParams(), clusterId, row.InfobaseId, dropDatabase,
                infobaseUser, infobasePassword).ConfigureAwait(false);
            return ok
                ? (true, string.Empty)
                : (false, BuildActionError(_rac.LastActionError));
        }
        catch (Exception ex)
        {
            return (false, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// Показ диалога повтора удаления (issue #69) в UI-потоке. Первый показ (после
    /// исходной неудачи) — заголовок «Не удалось удалить информационную базу.» +
    /// детали rac; повторные показы помечаются «Повторная попытка удаления не
    /// удалась: ...», чтобы пользователь видел, что креды применены, но rac снова
    /// вернул ошибку (комментарий 7OH от 2026-10-26). После неудачного завершения
    /// сеансов или установки блокировки регламентных заданий — подготовленная
    /// пометка <paramref name="retryNote"/> — уже готовое сообщение с подставленным
    /// текстом ошибки («Повторная попытка завершения сеансов не удалась: ...»,
    /// «Повторная попытка удаления не удалась: ... не удалось установить блокировку
    /// регламентных заданий ...», комментарии 7OH от 2026-10-26): отдельного
    /// окна-предупреждения при этом НЕ показывается. Логин и пароль с прошлой
    /// попытки передаются в диалог предзаполненными (пароль сохраняется между
    /// попытками — комментарий 7OH от 2026-10-26). Если колбэк диалога не назначен
    /// (тесты/окно без поддержки) — прежнее поведение: предупреждение с одной
    /// кнопкой OK, повтор не выполняется. Модальное окно открывается через
    /// dispatch-маршалер (как <see cref="ShowWarningOnUi"/>), рабочий поток ждёт
    /// результат диалога.
    /// </summary>
    private DropRetryResult AskDropRetryOnUi(
        string detail, string? login, string? password, int attempts, string? retryNote = null)
    {
        // Заголовок диалога повтора: на исходной неудаче — «Не удалось удалить
        // информационную базу.» + детали rac; на повторной — «Повторная попытка
        // удаления не удалась: <детали rac>» (issue #69, комментарий 7OH). После
        // неудачного завершения сеансов / установки блокировки регламентных заданий
        // retryNote — уже готовое сообщение (показывается как есть, без отдельного
        // окна-предупреждения; комментарий 7OH от 2026-10-26: не выходить после
        // первой попытки, дать нажать «Завершить сеансы и повторить» ещё раз).
        var message = retryNote is not null
            ? retryNote
            : attempts > 1
                ? string.Format(
                    LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryFailedFormat"), detail)
                : LocalizationManager.T("ServerMonitor.Infobase.DropFailed") + "\n" + detail;
        if (_dropRetryDialog is null)
        {
            ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Infobase.DropTitle"));
            return DropRetryResult.Cancel;
        }

        if (_dispatchToUi is null)
            return _dropRetryDialog(message, login, password);

        DropRetryResult result = DropRetryResult.Cancel;
        using var gate = new ManualResetEventSlim(false);
        _dispatchToUi(() =>
        {
            try
            {
                result = _dropRetryDialog(message, login, password);
            }
            finally
            {
                gate.Set();
            }
        });
        gate.Wait();
        return result;
    }

    /// <summary>
    /// Разрыв активных соединений и завершение сеансов информационной базы перед
    /// «infobase drop» (issue #69): rac не удаляет базу, пока её используют
    /// сеансы/соединения (включая HTTP-сервисы). Комментарий 7OH от 2026-10-26:
    /// «кто-то забыл удалить текущие соединения» — перед drop соединения базы должны
    /// быть разорваны, иначе rac возвращает «Информационная база имеет непустой
    /// список соединений». Порядок в каждом проходе — сначала «connection disconnect»,
    /// затем «session terminate» (соединения указывают на сеансы). Списки запрашиваются
    /// ЗАНОВО на каждом проходе (до <see cref="MaxReleasePasses"/> проходов,
    /// race-free): между «connection list» и «connection disconnect» могут появиться
    /// новые соединения — повторные проходы их добивают. Учётные данные администратора
    /// кластера берутся из <see cref="BuildParams"/> (как у всех действий уровня
    /// кластера). Учётные данные администратора ИБ
    /// (<paramref name="infobaseUser"/>/<paramref name="infobasePassword"/>) передаются
    /// в «connection disconnect»/«session terminate»: без них защищённая база возвращает
    /// «Недостаточно прав пользователя на информационную базу», и сеансы не снимаются
    /// (комментарий 7OH от 2026-10-27: «удалять сеансы не хочет»).
    /// <para>
    /// «СЛУЖЕБНОЕ соединение» («Служебное соединение не может быть удалено»,
    /// <see cref="IsServiceConnectionError"/>) не является ошибкой удаления: его нельзя
    /// разорвать, но и «infobase drop» оно не блокирует — оно ПРОПУСКАЕТСЯ, разрыв
    /// остальных соединений и сеансов продолжается (в логе 7OH от 2026-10-27 disconnect
    /// служебного соединения обрывал весь цикл, до остальных соединений, сеансов и
    /// самого «infobase drop» дело не доходило). Остальные ошибки разрыва, как и раньше,
    /// прерывают цикл (пользователь видит диалог повтора — «Не выходить после первой
    /// неудачной попытки» реализовано на уровне диалога).
    /// </para>
    /// Возвращает текст статуса об успехе либо текст ошибки, а также суммарное число
    /// разорванных соединений и завершённых сеансов (для сводки в статус-строке).
    /// </summary>
    private async Task<(bool Ok, string Detail, int Disconnected, int Terminated)> ReleaseInfobaseActivityAsync(
        Guid infobaseId, Guid clusterId,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        try
        {
            var parameters = BuildParams();
            var disconnected = 0;
            var terminated = 0;
            for (var pass = 0; pass < MaxReleasePasses; pass++)
            {
                var connections = await _rac.GetConnectionsAsync(parameters, clusterId, infobaseId)
                    .ConfigureAwait(false);
                var sessions = await _rac.GetSessionsAsync(parameters, clusterId, infobaseId)
                    .ConfigureAwait(false);
                if (connections.Count == 0 && sessions.Count == 0)
                    break;

                // Рабочий процесс сеанса — из соединения, которое его обслуживает
                // (в выводе «session list» номера процесса нет). После разрыва соединений
                // такие сеансы уже не активны; сеансы без найденного процесса не терминируем.
                var processByConnectionId = new Dictionary<Guid, Guid>(connections.Count);

                foreach (var connection in connections)
                {
                    // issue #69, комментарий 7OH от 2026-10-26 («повторить — ошибка»):
                    // rac 8.5.4 отклоняет «connection disconnect» без параметра
                    // --process=<pid> («Ошибка разбора параметра: process»); pid — рабочий
                    // процесс из «connection list». Креды администратора ИБ — для
                    // защищённых баз (комментарий 7OH от 2026-10-27).
                    if (connection.ProcessId != Guid.Empty)
                        processByConnectionId[connection.Id] = connection.ProcessId;

                    var ok = await _rac.DisconnectConnectionAsync(
                            parameters, clusterId, connection.Id, connection.ProcessId,
                            infobaseUser, infobasePassword)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        disconnected++;
                        continue;
                    }

                    // «Служебное соединение не может быть удалено» — штатный отказ rac:
                    // такое соединение нельзя разорвать, но «infobase drop» оно НЕ
                    // блокирует — пропускаем, разрыв остальных продолжается
                    // (комментарий 7OH от 2026-10-27: в его логе disconnect служебного
                    // соединения обрывал весь цикл, до «infobase drop» дело не доходило).
                    var error = BuildActionError(_rac.LastActionError);
                    if (IsServiceConnectionError(error))
                        continue;
                    return (false, error, disconnected, terminated);
                }

                foreach (var session in sessions)
                {
                    var processId = processByConnectionId.TryGetValue(session.ConnectionId, out var pid)
                        ? pid
                        : (Guid?)null;
                    var ok = await _rac.TerminateSessionAsync(
                            parameters, clusterId, session.Id, processId,
                            infobaseUser, infobasePassword)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        terminated++;
                        continue;
                    }

                    // Служебный сеанс, как и служебное соединение, пропускаем; штатная
                    // ошибка завершения прерывает цикл — диалог повтора даёт пользователю
                    // решить, пробовать ли ещё раз (комментарий 7OH от 2026-10-26).
                    var error = BuildActionError(_rac.LastActionError);
                    if (IsServiceConnectionError(error))
                        continue;
                    return (false, error, disconnected, terminated);
                }
            }

            return (true, string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.TerminatedFormat"),
                disconnected, terminated), disconnected, terminated);
        }
        catch (Exception ex)
        {
            return (false, BuildErrorMessage(ex), 0, 0);
        }
    }

    /// <summary>
    /// Признак того, что rac отказал в разрыве СЛУЖЕБНОГО соединения/завершении
    /// служебного сеанса («Служебное соединение не может быть удалено»): такие
    /// соединения нельзя разорвать командой «connection disconnect», но и «infobase
    /// drop» они не блокируют — ошибка НЕ считается фатальной (issue #69, комментарий
    /// 7OH от 2026-10-27; в его логе disconnect служебного соединения обрывал весь
    /// цикл разрыва сеансов перед удалением, до drop дело не доходило).
    /// Internal — для юнит-тестов.
    /// </summary>
    internal static bool IsServiceConnectionError(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        var text = detail.ToLowerInvariant();
        return text.Contains("служебн", StringComparison.Ordinal) ||
               text.Contains("service connection", StringComparison.Ordinal);
    }

    /// <summary>
    /// Признак ошибки rac «Попытка подключения к освобождаемому контексту информационной
    /// базы. Требуется повторить попытку подключения.» (issue #69, лог 7OH от 2026-10-28):
    /// разрыв соединений столкнулся с ещё освобождаемым (drop-совместимым) контекстом базы.
    /// Такая ошибка — НЕ финал: после короткой паузы (освобождение контекста) следующий
    /// проход завершения сеансов обычно проходит. Internal — для юнит-тестов.
    /// </summary>
    internal static bool IsReleaseContextError(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        var text = detail.ToLowerInvariant();
        return text.Contains("освобождаем", StringComparison.Ordinal) ||
               text.Contains("released context", StringComparison.Ordinal) ||
               text.Contains("being released", StringComparison.Ordinal);
    }

    /// <summary>
    /// Число ВНЕШНИХ попыток завершения сеансов/разрыва соединений в одном нажатии
    /// «Завершить сеансы и повторить» (issue #69, комментарий 7OH от 2026-10-28:
    /// «несколько попыток завершения с небольшим перерывом и индикацией»). При
    /// исчерпании показывается «Не удалось завершить сеансы за N попыток: …»
    /// и снова диалог повтора — программа не зацикливается.
    /// </summary>
    private const int MaxSessionReleaseAttempts = 5;

    /// <summary>Пауза между попытками завершения сеансов (issue #69, «с небольшим перерывом»).</summary>
    internal static readonly TimeSpan SessionReleaseRetryDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Пауза после ошибки «освобождаемый контекст» (issue #69): дать rac время
    /// освободить контекст информационной базы перед следующей попыткой.</summary>
    internal static readonly TimeSpan ReleaseContextRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Максимальное число проходов разрыва соединений/сеансов базы перед
    /// «infobase drop» (issue #69): списки запрашиваются заново на каждом проходе
    /// (race-free), чтобы соединения, появившиеся между «connection list» и
    /// «connection disconnect», не оставили rac-ошибку «непустой список соединений».</summary>
    private const int MaxReleasePasses = 3;

    /// <summary>
    /// Завершение активных сеансов/разрыва соединений базы НЕСКОЛЬКИМИ попытками
    /// (issue #69, комментарий 7OH от 2026-10-28): используется после УСПЕШНОЙ установки
    /// блокировки регламентных заданий и внутри одного нажатия «Завершить сеансы и
    /// повторить». Каждая попытка запрашивает список соединений/сеансов ЗАНОВО
    /// (через <see cref="ReleaseInfobaseActivityAsync"/>) и выполняет разрыв; между
    /// попытками — пауза <see cref="SessionReleaseRetryDelay"/>, а после ошибки
    /// «освобождаемый контекст» (<see cref="IsReleaseContextError"/>) — пауза
    /// <see cref="ReleaseContextRetryDelay"/>. В статус-строку окна монитора пишется
    /// индикация «Попытка N из M завершить сеансы…». При успехе возвращается сводка,
    /// при исчерпании — последняя ошибка rac (готовое сообщение «Не удалось завершить
    /// сеансы за N попыток: …» формирует вызывающий).
    /// </summary>
    private async Task<(bool Ok, string Detail)> ReleaseInfobaseActivityWithRetriesAsync(
        Guid infobaseId, Guid clusterId,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        var lastDetail = string.Empty;
        for (var attempt = 1; attempt <= MaxSessionReleaseAttempts; attempt++)
        {
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.SessionReleaseAttemptFormat"),
                attempt, MaxSessionReleaseAttempts);
            var (ok, detail, _, _) = await ReleaseInfobaseActivityAsync(
                    infobaseId, clusterId, infobaseUser, infobasePassword)
                .ConfigureAwait(false);
            if (ok)
            {
                StatusText = detail;
                return (true, detail);
            }
            lastDetail = detail;
            // Пауза между попытками; после «освобождаемого контекста» ждём дольше —
            // rac нужно время, чтобы освободить контекст информационной базы.
            await DelayAsync(IsReleaseContextError(detail)
                    ? ReleaseContextRetryDelay
                    : SessionReleaseRetryDelay)
                .ConfigureAwait(false);
        }
        return (false, lastDetail);
    }

    /// <summary>Ожидание паузы (issue #69): в тестах подменяется мгновенной задержкой.</summary>
    private Task DelayAsync(TimeSpan delay)
        => _delayAsync is not null ? _delayAsync(delay) : Task.Delay(delay);

    /// <summary>
    /// Блокировка регламентных заданий информационной базы перед повторным удалением
    /// занятой базы (issue #69, комментарий 7OH от 2026-10-26): rac «infobase update
    /// --scheduled-jobs-deny=on». Выполняется ДО завершения сеансов — «сначала ставить
    /// блокировку, а потом снимать сеансы». Учётные данные администратора кластера —
    /// из <see cref="BuildParams"/> (как у всех действий уровня кластера); параметры
    /// СУБД — из строки summary базы, а при пустых — догружаются через rac «infobase
    /// info» (<see cref="EnsureDbParametersAsync"/>, комментарий 7OH от 2026-10-26:
    /// «Тип СУБД в удаляемой базе явно же прописан?»). Учётные данные администратора ИБ
    /// (<paramref name="infobaseUser"/>/<paramref name="infobasePassword"/>, issue #69,
    /// комментарий 7OH от 2026-10-27) передаются и в «infobase info», и в «infobase
    /// update»: на защищённой базе rac возвращает «Недостаточно прав пользователя на
    /// информационную базу», если креды не переданы — «установить блокировку не
    /// удаётся». Возвращает текст статуса об успехе либо текст ошибки.
    /// </summary>
    private async Task<(bool Ok, string Detail)> SetScheduledJobsBlockAsync(
        RacInfobaseRow row, Guid clusterId,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        try
        {
            // issue #69, комментарий 7OH от 2026-10-26: «infobase update --scheduled-jobs-deny=on»
            // требует параметры СУБД; сводка summary их может не содержать — догружаем через
            // rac «infobase info» (метод уже есть; у интерфейса default-реализация для тестов).
            var (dbms, dbServer, dbName) = await EnsureDbParametersAsync(
                row, clusterId, infobaseUser, infobasePassword)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(dbms) || string.IsNullOrWhiteSpace(dbServer) ||
                string.IsNullOrWhiteSpace(dbName))
            {
                // Ни summary, ни «infobase info» не дали параметров СУБД — блокировку не
                // ставим; текст (понятная ошибка) попадает в диалог повтора строкой, без
                // исключения и без отдельного окна-предупреждения.
                return (false, LocalizationManager.T("ServerMonitor.Infobase.Drop.BlockNoDbParams"));
            }

            var request = new RacInfobaseUpdateRequest
            {
                Dbms = dbms,
                DbServer = dbServer,
                DbName = dbName,
                // Креды администратора ИБ для защищённой базы (комментарий 7OH от
                // 2026-10-27: «Не удаётся теперь установить блокировку»).
                InfobaseUser = infobaseUser ?? string.Empty,
                InfobasePassword = infobasePassword ?? string.Empty,
                ScheduledJobsDeny = true
            };
            var ok = await _rac.UpdateInfobaseAsync(
                BuildParams(), clusterId, row.InfobaseId, request).ConfigureAwait(false);
            return ok
                ? (true, LocalizationManager.T("ServerMonitor.Infobase.Drop.BlockedFormat"))
                : (false, BuildActionError(_rac.LastActionError));
        }
        catch (Exception ex)
        {
            return (false, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// Гарантированное получение параметров СУБД информационной базы для rac «infobase
    /// update» (issue #69): значения из строки summary используются как есть, а при
    /// пустом хотя бы одном из них (типично для summary) догружаются полные свойства
    /// через rac «infobase info» (<see cref="IRacClient.GetInfobaseInfoAsync"/>). На
    /// защищённой базе догрузка выполняется с учётными данными администратора ИБ
    /// (<paramref name="infobaseUser"/>/<paramref name="infobasePassword"/>, issue #69,
    /// комментарий 7OH от 2026-10-27: rac без кредов возвращает «Недостаточно прав
    /// пользователя на информационную базу» — раньше из-за этого блокировка РЗ не
    /// ставилась). Метод не бросает исключений: при неудаче догрузки возвращаются уже
    /// известные значения (возможно, пустые) — вызывающий код обрабатывает их понятным
    /// текстом.
    /// </summary>
    private async Task<(string Dbms, string DbServer, string DbName)> EnsureDbParametersAsync(
        RacInfobaseRow row, Guid clusterId,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        var dbms = row.Dbms?.Trim() ?? string.Empty;
        var dbServer = row.DbServer?.Trim() ?? string.Empty;
        var dbName = row.DbName?.Trim() ?? string.Empty;
        if (dbms.Length > 0 && dbServer.Length > 0 && dbName.Length > 0)
            return (dbms, dbServer, dbName);

        RacInfobaseInfo? info = null;
        try
        {
            info = await _rac.GetInfobaseInfoAsync(
                BuildParams(), clusterId, row.InfobaseId,
                infobaseUser, infobasePassword).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Заглушки тестов и сетевая ошибка не должны ронять цикл удаления.
        }

        if (info is null)
            return (dbms, dbServer, dbName);

        return (
            dbms.Length > 0 ? dbms : info.Dbms?.Trim() ?? string.Empty,
            dbServer.Length > 0 ? dbServer : info.DbServer?.Trim() ?? string.Empty,
            dbName.Length > 0 ? dbName : info.DbName?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Тип типовой ошибки rac при «infobase drop» (issue #69): от него зависят
    /// подсказка в диалоге повтора и доступность кнопки «Завершить сеансы и
    /// повторить» (базу занимают активные сеансы/соединения/HTTP-сервисы).
    /// </summary>
    public enum RacDropErrorKind
    {
        /// <summary>Ошибка не распознана — общий текст без подсказки.</summary>
        Unknown,

        /// <summary>Защищённая ИБ / недостаточно прав: нужны креды администратора ИБ
        /// (или администратора кластера, если база не защищена).</summary>
        Authentication,

        /// <summary>Базу используют активные сеансы/соединения (включая HTTP-сервисы):
        /// rac не удаляет занятую базу — перед повтором нужно их завершить.</summary>
        Sessions,

        /// <summary>Ошибка сервера СУБД при «--drop-database»: rac использует учётные
        /// данные СУБД из настроек базы.</summary>
        Database
    }

    /// <summary>
    /// Классифицирует текст ошибки rac при «infobase drop» (issue #69): защищённая
    /// ИБ/права (<see cref="RacDropErrorKind.Authentication"/>), активные сеансы и
    /// соединения (<see cref="RacDropErrorKind.Sessions"/>), сервер СУБД
    /// (<see cref="RacDropErrorKind.Database"/>), прочее — Unknown. Диалог повтора
    /// использует результат для подсказки и кнопки «Завершить сеансы и повторить».
    /// Internal — для юнит-тестов классификации без запуска rac.
    /// </summary>
    internal static RacDropErrorKind ClassifyDropError(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return RacDropErrorKind.Unknown;

        var text = detail.ToLowerInvariant();
        if (ContainsAny(text, DropErrorSessionsMarkers))
            return RacDropErrorKind.Sessions;
        if (ContainsAny(text, DropErrorAuthMarkers))
            return RacDropErrorKind.Authentication;
        if (ContainsAny(text, DropErrorDatabaseMarkers))
            return RacDropErrorKind.Database;
        return RacDropErrorKind.Unknown;
    }

    /// <summary>
    /// Локализованная подсказка для диалога повтора по типу ошибки
    /// (<see cref="ClassifyDropError"/>): Unknown — пустая строка (подсказки нет).
    /// Internal — для юнит-тестов читаемости текстов.
    /// </summary>
    internal static string BuildDropRetryHint(RacDropErrorKind kind) => kind switch
    {
        RacDropErrorKind.Authentication => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintAuth"),
        RacDropErrorKind.Sessions => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintSessions"),
        RacDropErrorKind.Database => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintDatabase"),
        _ => string.Empty
    };

    /// <summary>Маркеры занятости базы в тексте ошибки rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorSessionsMarkers =
    {
        "session", "connection", "сеанс", "соединени", "используется", "используют",
        "used by", "busy", "занят", "http-сервис", "http service", "публикац"
    };

    /// <summary>Маркеры ошибки прав/аутентификации в тексте rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorAuthMarkers =
    {
        "access", "authentication", "auth", "denied", "permission", "rights",
        "authoriz", "доступ", "аутентификац", "авториз", "защищен", "защищён",
        "права", "прав на", "прав доступа", "no rights"
    };

    /// <summary>Маркеры ошибки сервера СУБД в тексте rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorDatabaseMarkers =
    {
        "database", "db user", "db-user", "db pwd", "db-pwd", "субд", "база данных",
        "базаданных", "сервер субд"
    };

    private static bool ContainsAny(string text, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Изменение выбранной базы (issue #3, кнопка «Изменить»): rac «infobase update»
    /// по полям диалога → перечитывание списков. Ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task UpdateSelectedInfobaseAsync(RacInfobaseUpdateRequest request)
    {
        var row = SelectedInfobase;
        if (request is null || row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        try
        {
            var ok = await _rac.UpdateInfobaseAsync(
                BuildParams(), clusterId, row.InfobaseId, request).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Infobase.UpdateFailed") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Infobase.UpdateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Status.UpdatedFormat"), request.Name);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Infobase.UpdateFailed") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Infobase.UpdateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Полные свойства базы для диалога правки (issue #3, 7OH 17.10): rac «infobase info»
    /// с учётными данными администратора ИБ. Возвращает свойства либо текст ошибки rac
    /// (например, нужна аутентификация администратора ИБ).
    /// </summary>
    public async Task<(RacInfobaseInfo? Info, string Error)> LoadInfobaseInfoAsync(
        Guid infobaseId, string? infobaseUser, string? infobasePassword)
    {
        if (!HasConnected || SelectedClusterId is not Guid clusterId || infobaseId == Guid.Empty)
            return (null, LocalizationManager.T("ServerMonitor.Infobase.Props.NotConnected"));
        try
        {
            var info = await _rac.GetInfobaseInfoAsync(
                BuildParams(), clusterId, infobaseId, infobaseUser, infobasePassword).ConfigureAwait(false);
            return info is null
                ? (null, BuildActionError(_rac.LastActionError))
                : (info, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// ViewModel диалога «Свойства информационной базы» (issue #3): учётные данные
    /// администратора ИБ ищутся в списке баз приложения (та же база на том же сервере),
    /// загрузка полных свойств — <see cref="LoadInfobaseInfoAsync"/>.
    /// </summary>
    public RacInfobasePropertiesViewModel CreateInfobasePropertiesViewModel(
        RacInfobaseSummary summary, IEnumerable<Infobase>? appInfobases)
    {
        var saved = ClusterInfobaseCredentials.Find(appInfobases, ServerAddress, summary?.Name);
        var infobaseId = summary?.InfobaseId ?? Guid.Empty;
        return new RacInfobasePropertiesViewModel(
            summary ?? new RacInfobaseSummary(),
            (user, password) => LoadInfobaseInfoAsync(infobaseId, user, password),
            saved);
    }

    /// <summary>«Удалить базу»: подтверждение → rac «infobase drop» → обновление.</summary>
    public ICommand DropInfobaseCommand =>
        _dropInfobaseCommand ??= new RelayCommand(async () => await DropSelectedInfobaseAsync());

    /// <summary>
    /// Текст индикатора фильтра по базе (issue #3): «Фильтр по базе: <Имя>» при
    /// активном фильтре; «Без фильтра» — фильтр снят. Надпись и кнопка «Сбросить»
    /// видны ВСЕГДА (в отличие от прежней версии, где панель скрывалась при снятом
    /// фильтре).
    /// </summary>
    public string InfobaseFilterText
    {
        get
        {
            if (_selectedInfobaseId is not Guid id)
                return LocalizationManager.T("ServerMonitor.Infobase.NoFilter");
            var name = _infobaseNames.TryGetValue(id, out var n) ? n : string.Empty;
            return string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.FilterFormat"),
                string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
        }
    }

    /// <summary>Активен ли фильтр по базе (для видимости индикатора и кнопки «Сбросить»).</summary>
    public bool HasInfobaseFilter => _selectedInfobaseId is not null;

    /// <summary>«Сбросить»: снимает фильтр по базе — показываются все базы кластера.</summary>
    public ICommand ClearInfobaseFilterCommand =>
        _clearInfobaseFilterCommand ??= new RelayCommand(ClearInfobaseFilter);

    /// <summary>Снимает фильтр по базе (клиентская операция, без rac-вызовов).</summary>
    public void ClearInfobaseFilter()
    {
        if (_selectedInfobase is null && _selectedInfobaseId is null)
            return;
        _selectedInfobase = null;
        _selectedInfobaseId = null;
        OnPropertyChanged(nameof(SelectedInfobase));
        OnPropertyChanged(nameof(SelectedInfobaseId));
        ApplyInfobaseFilter();
    }

    // ===================== Автообновление =====================

    /// <summary>Запущен ли таймер автообновления (после успешного подключения).</summary>
    public bool AutoRefreshActive => _autoRefreshTimer is not null;

    /// <summary>Переключатель автообновления (issue #324): вкл/выкл на форме монитора.</summary>
    public bool IsAutoRefreshEnabled
    {
        get => _isAutoRefreshEnabled;
        set => SetAutoRefreshEnabled(value);
    }

    /// <summary>
    /// Интервал автообновления, секунды (1–60; по умолчанию 5). Смена значения при работающем
    /// таймере перезапускает его (issue #324, комментарий 17/18: «как отключить Автообновления»).
    /// </summary>
    public int AutoRefreshIntervalSeconds
    {
        get => _autoRefreshIntervalSeconds;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (!SetProperty(ref _autoRefreshIntervalSeconds, clamped))
                return;
            if (_autoRefreshTimer is not null && IsAutoRefreshEnabled)
            {
                StopAutoRefresh();
                StartAutoRefresh();
            }
        }
    }

    /// <summary>Команда переключения автообновления (вкл/выкл).</summary>
    public ICommand ToggleAutoRefreshCommand =>
        _toggleAutoRefreshCommand ??= new RelayCommand(() => SetAutoRefreshEnabled(!IsAutoRefreshEnabled));

    /// <summary>
    /// Включает/выключает автообновление. Выключение останавливает таймер; включение
    /// запускает его только при установленном подключении (ручное «Обновить» доступно всегда).
    /// </summary>
    public void SetAutoRefreshEnabled(bool enabled)
    {
        if (!SetProperty(ref _isAutoRefreshEnabled, enabled))
            return;

        if (enabled)
        {
            if (HasConnected && _autoRefreshTimer is null)
                StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
        // Статусная строка внизу окна различает «выключено переключателем» и
        // «остановлено после ошибки» (issue #324): при переключении она обязана
        // обновиться даже если таймер не стартовал/не останавливался.
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    /// <summary>
    /// Подпись состояния автообновления для статусной строки окна (issue #324):
    /// «вкл (N с)» — таймер реально работает; «выкл» — переключатель снят;
    /// «остановлено (ошибка)» — переключатель включён, но таймер остановлен из-за
    /// сбоя загрузки данных (показывает ФАКТИЧЕСКОЕ состояние, а не положение галки).
    /// </summary>
    public string AutoRefreshText
    {
        get
        {
            if (!IsAutoRefreshEnabled)
                return LocalizationManager.T("ServerMonitor.AutoRefreshOff");
            return AutoRefreshActive
                ? string.Format(LocalizationManager.T("ServerMonitor.AutoRefreshOnFormat"), AutoRefreshIntervalSeconds)
                : LocalizationManager.T("ServerMonitor.AutoRefreshStopped");
        }
    }
    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Кластеров: N», ошибки).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (для статус-строки и диалога).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Успешно ли установлено подключение (список кластеров получен).</summary>
    public bool HasConnected
    {
        get => _hasConnected;
        private set
        {
            if (!SetProperty(ref _hasConnected, value))
                return;
            // issue #72: доступность массовых действий зависит от подключения.
            OnPropertyChanged(nameof(CanTerminateSessions));
            OnPropertyChanged(nameof(CanDisconnectConnections));
        }
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение, см. ProcessInspectorViewModel).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// «Подключиться»: получить список кластеров (rac «cluster list»); при успехе —
    /// выбрать первый кластер и загрузить его данные. Ошибки не роняют окно —
    /// пишутся в статус-строку/ErrorMessage.
    /// </summary>
    public async Task ConnectAsync()
    {
        // issue #77 (комментарий 7OH от 2026-10-28): ПЕРЕД началом подключения таймер
        // автообновления останавливается — иначе во время подключения к новому серверу
        // тик каждые N секунд перезапрашивает данные со СТАРЫМ контекстом (адрес уже
        // сменился, а SelectedClusterId ещё от прежнего сервера) и выплёвывает ошибку
        // («микс»). Галка «Автообновление» при этом НЕ трогается: после успешного
        // подключения таймер перезапускается внизу (когда галка стоит), при ошибке —
        // остаётся остановленным (StopAutoRefresh в catch). Повторные нажатия/переключение
        // на тот же сервер обрабатываются штатно (первый вызов перезапустит таймер).
        StopAutoRefresh();

        if (!TryEnterBusy())
            return;

        // Диагностика источника rac-команд (issue #90): команда подключения — из монитора.
        RacClient.LogSource.Value = MonitorLogSourceTag;

        Guid? initialClusterId = null;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Connecting");
            // Новое подключение сбрасывает подсветку предыдущего сервера справочника
            // (issue #3, комментарий 7OH 19.10): при сбое подключения или обращении к
            // адресу вне справочника жирной не должна оставаться запись, к которой
            // соединение уже не установлено.
            ConnectedServer = null;

            // issue #77: при переключении на другой сервер выбор кластера, список
            // кластеров и данные вкладок прежнего сервера сбрасываются СИНХРОННО —
            // до первого await (в потоке вызова: UI-поток WPF/Avalonia, тесты).
            // ApplyClusters применяется В UI-потоки позднее (Dispatcher.BeginInvoke /
            // UIThread.Post), а ConnectAsync после ConfigureAwait(false) продолжается
            // в фоне — без сброса здесь после ExitBusy запускалась
            // LoadClusterDataAsync со СТАРЫМ идентификатором кластера прежнего
            // сервера, и переключение всегда заканчивалось ошибкой rac.
            ResetClusterState();

            var clusters = await _rac.GetClustersAsync(BuildParams()).ConfigureAwait(false);

            ApplyClusters(clusters);
            HasConnected = true;
            // Подсветка записи справочника, к которой установлено подключение (issue #3,
            // комментарий 7OH 19.10): поиск по совпадению адреса:порта для обычного
            // подключения по полям; ConnectToServerAsync затем уточняет запись.
            ConnectedServer = FindServerByAddress(ServerAddress, ServerPort);
            OnPropertyChanged(nameof(CanDropInfobase));
            StatusText = clusters.Count == 0
                ? LocalizationManager.T("ServerMonitor.Status.NoClusters")
                : string.Format(LocalizationManager.T("ServerMonitor.Status.ConnectedFormat"), clusters.Count);

            // Автообновление запускается только после успешного подключения И при
            // включённом переключателе: галку могли снять ДО подключения — таймер
            // в этом случае не должен стартовать (issue #324, комментарий 7OH).
            if (IsAutoRefreshEnabled)
                StartAutoRefresh();

            // Первый кластер выбираем автоматически. Сам выбор (и запускаемая им
            // загрузка данных) делаем ПОСЛЕ выхода из busy: назначение внутри
            // ConnectAsync молча пропускало LoadClusterDataAsync через TryEnterBusy,
            // и данные приходили только через таймер ~5 с спустя (issue #324:
            // «подключение локально занимает почти 5 секунд»).
            // issue #77: id берём из ТОЛЬКО ЧТО полученного списка (clusters), а не
            // из ClusterRows/SelectedClusterId — в WPF/Avalonia ApplyClusters применяется
            // в UI-потоке позже, и эти свойства на момент проверки ещё устаревшие.
            if (clusters.Count > 0)
                initialClusterId = clusters[0].Id;
        }
        catch (Exception ex)
        {
            HasConnected = false;
            OnPropertyChanged(nameof(CanDropInfobase));
            // issue #324: в сообщении об ошибке указываем ЦЕЛЕВОЙ адрес:порт, к которому
            // шло подключение (поля могли быть заполнены сохранёнными значениями, и
            // пользователю должно быть видно, куда именно «ушёл» запрос), а не только
            // текст rac, который при повторном нажатии может отличаться.
            var (host, port) = RacConnectionAddress.Split(ServerAddress, ServerPort);
            ErrorMessage = string.Format(
                LocalizationManager.T("ServerMonitor.Status.ConnectFailedDetail"),
                host, port, BuildErrorMessage(ex));
            StatusText = LocalizationManager.T("ServerMonitor.Status.ConnectFailed");
            // Без подключения таймер автообновления не работает.
            StopAutoRefresh();
        }
        finally
        {
            ExitBusy();
        }

        // busy освобождён — запускам первичную загрузку данных выбранного кластера.
        // issue #77: за основу берём ТОЛЬКО что полученный список кластеров, а не
        // SelectedClusterId — в WPF/Avalonia ApplyClusters мог ещё не примениться
        // в UI-потоке, и это свойство содержит кластер ПРЕЖНЕГО сервера (переключение
        // на другой сервер ранее всегда заканчивалось ошибкой). Когда единственный/
        // первый кластер уже выбран синхронно (тесты: dispatch==null — ApplyClusters
        // применился внутри busy и сеттер молча пропустил загрузку через TryEnterBusy),
        // запускаем загрузку напрямую — без этого блока данные приходили бы только
        // от таймера ~5 с спустя (issue #324).
        if (initialClusterId is { } initialId)
        {
            if (SelectedClusterId == initialId)
                _ = LoadClusterDataAsync(initialId);
            else
                SelectedClusterId = initialId;
        }
        else if (SelectedClusterId is { } clusterId && HasConnected)
        {
            // initialClusterId пуст только при пустом списке кластеров; ветка сохранена
            // для симметрии синхронного пути (Dispatch==null), когда выбор мог остаться
            // от ApplyClusters, но загрузка была пропущена из-за занятости.
            _ = LoadClusterDataAsync(clusterId);
        }
    }

    /// <summary>
    /// Загружает данные выбранного кластера: процессы, сеансы, соединения, блокировки,
    /// регламентные задания, информационные базы (для имён владельцев заданий) и
    /// «cluster info» (параллельно). Результаты применяются через <see cref="_dispatchToUi"/>
    /// (null — напрямую, тесты). Каждая вкладка загружается ИЗОЛИРОВАННО (issue #324,
    /// лог 7OH от 2026-10-08): сбой ОДНОЙ rac-команды (например, нераспознанная схема
    /// вывода при exit=0 и непустом stdout) больше не проваливает общий
    /// <see cref="Task.WhenAll"/> — остальные вкладки заполняются тем, что удалось
    /// разобрать, а сбойные перечисляются в <see cref="ErrorMessage"/>; автообновление
    /// останавливается, ручное «Обновить» остаётся доступным.
    /// </summary>
    public async Task LoadClusterDataAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        // issue #90: после закрытия окна (Dispose) циклы загрузки rac-команд не выполняются.
        if (_disposed)
            return;
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Loading");

            var parameters = BuildParams();
            var errors = new List<string>();
            var hasParseError = false;

            // Изолированная загрузка одной вкладки: исключение не роняет общий цикл.
            async Task<IReadOnlyList<T>> LoadTabAsync<T>(
                string tabLabel, Func<Task<IReadOnlyList<T>>> load)
            {
                try
                {
                    return await load().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RacOutputParseException ex)
                {
                    hasParseError = true;
                    errors.Add($"{tabLabel}: {BuildErrorMessage(ex)}");
                    return Array.Empty<T>();
                }
                catch (Exception ex)
                {
                    errors.Add($"{tabLabel}: {BuildErrorMessage(ex)}");
                    return Array.Empty<T>();
                }
            }

            async Task<RacClusterInfo?> LoadInfoAsync()
            {
                try
                {
                    return await _rac.GetClusterInfoAsync(parameters, clusterId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"{LocalizationManager.T("ServerMonitor.Tabs.Info")}: {BuildErrorMessage(ex)}");
                    return null;
                }
            }

            var processesTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Processes"),
                () => _rac.GetProcessesAsync(parameters, clusterId, cancellationToken));
            // issue #3: сеансы/соединения/блокировки ВСЕГДА запрашиваются целиком
            // (без параметра rac «--infobase=...» — на платформе пользователя он не
            // работает и вызывает зависание до CommandTimeout). Выбор базы фильтрует
            // уже загруженный снимок на клиенте (см. ApplyInfobaseFilter).
            var sessionsTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Sessions"),
                () => _rac.GetSessionsAsync(parameters, clusterId, null, cancellationToken));
            var connectionsTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Connections"),
                () => _rac.GetConnectionsAsync(parameters, clusterId, null, cancellationToken));
            var locksTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Locks"),
                () => _rac.GetLocksAsync(parameters, clusterId, null, cancellationToken));
            var infobasesTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Infobases"),
                () => _rac.GetInfobasesAsync(parameters, clusterId, cancellationToken));
            var infoTask = LoadInfoAsync();

            // п.5 «Мониторинг лицензий» (1.0.0.87): сеансы с лицензиями (rac «session list
            // --licenses») и установленные программные лицензии («ring license list»).
            // Загрузка изолирована от остальных вкладок: недоступность ring/licensetool
            // не роняет общий цикл — вкладка покажет занятые сеансами лицензии, а
            // предупреждение source будет в LicenseStatusText.
            IReadOnlyList<RacSessionInfo> licensedSessions = Array.Empty<RacSessionInfo>();
            IReadOnlyList<ProgramLicense> installedLicenses = Array.Empty<ProgramLicense>();
            string? licenseWarning = null;
            async Task LoadLicensesTabAsync()
            {
                try
                {
                    licensedSessions = await _rac.GetLicensedSessionsAsync(
                        parameters, clusterId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"{LocalizationManager.T("ServerMonitor.Tabs.Licenses")}: {BuildErrorMessage(ex)}");
                    return;
                }

                try
                {
                    // Установленные программные лицензии (ring/licensetool/каталог).
                    var ringResult = _ringLicenses is not null
                        ? await _ringLicenses.GetInstalledLicensesAsync(cancellationToken).ConfigureAwait(false)
                        : null;
                    installedLicenses = ringResult?.Licenses ?? Array.Empty<ProgramLicense>();
                    licenseWarning = ringResult?.Warning;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Недоступность утилиты лицензий — не ошибка вкладки (запасной режим).
                    licenseWarning = BuildErrorMessage(ex);
                }
            }

            var licensesTask = LoadLicensesTabAsync();

            await Task.WhenAll(
                processesTask, sessionsTask, connectionsTask, locksTask, infobasesTask, infoTask,
                licensesTask)
                .ConfigureAwait(false);

            var processes = await processesTask.ConfigureAwait(false);
            var sessions = await sessionsTask.ConfigureAwait(false);
            var connections = await connectionsTask.ConfigureAwait(false);
            var locks = await locksTask.ConfigureAwait(false);
            var infobases = await infobasesTask.ConfigureAwait(false);
            var info = await infoTask.ConfigureAwait(false);

            // issue #90: окно закрыто (Dispose) — данные по-прежнему получены, но UI
            // обновлять не нужно (свойства и списки уже никому не видны).
            if (_disposed)
                return;

            ApplyClusterData(
                processes, sessions, connections, locks, infobases, info,
                licensedSessions, installedLicenses, licenseWarning);

            if (errors.Count > 0)
            {
                // Часть данных не загружена (issue #324): сбойные вкладки перечислены
                // в ошибке, остальные заполнены. Автообновление останавливается — как
                // и раньше при полной ошибке; ручное «Обновить» остаётся доступным,
                // после успеха таймер возобновится.
                ErrorMessage = LocalizationManager.T("ServerMonitor.Status.PartialLoad") +
                               "\n" + string.Join("\n", errors);
                StatusText = hasParseError
                    ? LocalizationManager.T("ServerMonitor.Status.ParseFailed")
                    : LocalizationManager.T("ServerMonitor.Status.LoadFailed");
                StopAutoRefresh();
            }
            else
            {
                StatusText = string.Format(
                    LocalizationManager.T("ServerMonitor.Status.LoadedFormat"),
                    processes.Count, sessions.Count, connections.Count, locks.Count);

                // issue #324: после УСПЕШНОЙ загрузки автообновление возобновляется, если
                // переключатель включён (например, ручное «Обновить» после ошибки разбора).
                if (IsAutoRefreshEnabled && _autoRefreshTimer is null)
                    StartAutoRefresh();
            }
        }
        catch (OperationCanceledException)
        {
            // issue #90: отмена цикла при закрытом окне не должна трогать UI
            // (статус «Отменено» в закрытом окне никто не увидит).
            if (!_disposed)
                StatusText = LocalizationManager.T("ServerMonitor.Status.Cancelled");
        }
        catch (Exception ex)
        {
            // issue #324: любая ошибка загрузки останавливает таймер автообновления
            // (бесконечный ретрай каждые 5 с после разрыва соединения не нужен —
            // «после ошибки продолжает пытаться получить данные»). Ручное «Обновить»
            // остаётся доступным; после успешной загрузки таймер возобновляется,
            // если переключатель включён.
            // issue #90: после Dispose обновления свойств UI не выполняются.
            if (_disposed)
                return;
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("ServerMonitor.Status.LoadFailed");
            StopAutoRefresh();
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// «Обновить»: перечитать данные выбранного кластера (без подключения — no-op).
    /// Каждый цикл загрузки идёт под собственным <see cref="CancellationToken"/> (issue #90):
    /// выключение автообновления и закрытие окна отменяют его сразу.
    /// </summary>
    public void Refresh()
    {
        if (_disposed || !HasConnected || SelectedClusterId is not Guid id)
            return;
        CancelRefreshCycle();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        // Диагностика источника rac-команд (issue #90): CM_RAC-лог помечается «MONITOR».
        RacClient.LogSource.Value = MonitorLogSourceTag;
        _ = LoadClusterDataAsync(id, cts.Token);
    }

    /// <summary>
    /// «Завершить сеансы» (issue #72): действие распространяется на ВСЕ выбранные строки
    /// вкладки «Сеансы» (мультивыделение Ctrl/Shift). При одной строке — прежнее поведение:
    /// подтверждение с именем пользователя, статус «Сеанс "N" завершён», при ошибке —
    /// предупреждение. При нескольких — единое подтверждение о количестве и
    /// ПОСЛЕДОВАТЕЛЬНОЕ завершение каждого сеанса (rac «session terminate», подтверждение
    /// не запрашивается повторно); частичная ошибка не прерывает остальных, в
    /// статус-строке — сводка «Завершено сеансов: N из M» (+ число ошибок), детали
    /// собираются в <see cref="ErrorMessage"/>. После действия списки перечитываются.
    /// </summary>
    public async Task TerminateSessionAsync()
    {
        var rows = EffectiveSelectedSessions();
        if (rows.Count == 0 || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        var single = rows.Count == 1;
        var confirm = single
            ? string.Format(LocalizationManager.T("ServerMonitor.TerminateConfirmFormat"), rows[0].User)
            : string.Format(LocalizationManager.T("ServerMonitor.TerminateBulkConfirmFormat"), rows.Count);
        if (!_dialogs.Confirm(confirm, LocalizationManager.T("ServerMonitor.TerminateTitle")))
            return;

        if (single)
        {
            // Прежнее поведение для одной строки: подробное предупреждение об ошибке (если
            // будет) и статус «Сеанс "N" завершён».
            var row = rows[0];
            try
            {
                var ok = await _rac.TerminateSessionAsync(BuildParams(), clusterId, row.Id).ConfigureAwait(false);
                if (!ok)
                {
                    var detail = BuildActionError(_rac.LastActionError);
                    ShowWarningOnUi(
                        string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + detail,
                        LocalizationManager.T("ServerMonitor.TerminateTitle"));
                    StatusText = detail;
                    return;
                }

                StatusText = string.Format(
                    LocalizationManager.T("ServerMonitor.Status.TerminatedFormat"), row.User);
            }
            catch (Exception ex)
            {
                ShowWarningOnUi(
                    string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + BuildErrorMessage(ex),
                    LocalizationManager.T("ServerMonitor.TerminateTitle"));
                StatusText = BuildErrorMessage(ex);
            }
            finally
            {
                // После действия списки перечитываются (сеанс мог исчезнуть).
                Refresh();
            }
            return;
        }

        // Массовое завершение (issue #72): каждый сеанс — свой rac-вызов; неудачи
        // собираются и не останавливают остальные строки.
        var parameters = BuildParams();
        var succeeded = 0;
        var failures = new List<string>();
        foreach (var session in rows)
        {
            try
            {
                var ok = await _rac.TerminateSessionAsync(parameters, clusterId, session.Id).ConfigureAwait(false);
                if (ok)
                {
                    succeeded++;
                }
                else
                {
                    failures.Add($"{session.User}: {BuildActionError(_rac.LastActionError)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{session.User}: {BuildErrorMessage(ex)}");
            }
        }

        StatusText = BuildBulkStatusText(
            "ServerMonitor.Status.TerminatedBulkFormat", succeeded, rows.Count, failures.Count);
        if (failures.Count > 0)
            ErrorMessage = string.Join("\n", failures.Take(10));
        Refresh();
    }

    /// <summary>
    /// «Разорвать соединения» (issue #72): действие распространяется на ВСЕ выбранные
    /// строки вкладки «Соединения» (мультивыделение Ctrl/Shift). При одной строке —
    /// прежнее поведение: подтверждение с именем клиента, статус «Соединение "N" разорвано»,
    /// при ошибке — предупреждение. При нескольких — единое подтверждение о количестве и
    /// последовательный разрыв каждого соединения (rac «connection disconnect», рабочий
    /// процесс строки, issue #69); частичная ошибка не прерывает остальных, статус-строка
    /// получает сводку «Разорвано соединений: N из M» (+ число ошибок), детали — в
    /// <see cref="ErrorMessage"/>. После действия списки перечитываются.
    /// </summary>
    public async Task DisconnectConnectionAsync()
    {
        var rows = EffectiveSelectedConnections();
        if (rows.Count == 0 || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        var single = rows.Count == 1;
        var confirm = single
            ? string.Format(LocalizationManager.T("ServerMonitor.DisconnectConfirmFormat"), rows[0].Host)
            : string.Format(LocalizationManager.T("ServerMonitor.DisconnectBulkConfirmFormat"), rows.Count);
        if (!_dialogs.Confirm(confirm, LocalizationManager.T("ServerMonitor.DisconnectTitle")))
            return;

        if (single)
        {
            // Прежнее поведение для одной строки.
            var row = rows[0];
            try
            {
                // issue #69: rac 8.5.4 без --process=<pid> отклоняет «connection disconnect»
                // («Ошибка разбора параметра: process») — pid известен из «connection list».
                var ok = await _rac.DisconnectConnectionAsync(
                    BuildParams(), clusterId, row.Id, row.ProcessId).ConfigureAwait(false);
                if (!ok)
                {
                    var detail = BuildActionError(_rac.LastActionError);
                    ShowWarningOnUi(
                        string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + detail,
                        LocalizationManager.T("ServerMonitor.DisconnectTitle"));
                    StatusText = detail;
                    return;
                }

                StatusText = string.Format(
                    LocalizationManager.T("ServerMonitor.Status.DisconnectedFormat"), row.Host);
            }
            catch (Exception ex)
            {
                ShowWarningOnUi(
                    string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + BuildErrorMessage(ex),
                    LocalizationManager.T("ServerMonitor.DisconnectTitle"));
                StatusText = BuildErrorMessage(ex);
            }
            finally
            {
                Refresh();
            }
            return;
        }

        // Массовый разрыв (issue #72): каждый вызов независим, неудачи не останавливают цикл.
        var parameters = BuildParams();
        var succeeded = 0;
        var failures = new List<string>();
        foreach (var connection in rows)
        {
            try
            {
                var ok = await _rac.DisconnectConnectionAsync(
                    parameters, clusterId, connection.Id, connection.ProcessId).ConfigureAwait(false);
                if (ok)
                {
                    succeeded++;
                }
                else
                {
                    failures.Add($"{connection.Host}: {BuildActionError(_rac.LastActionError)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{connection.Host}: {BuildErrorMessage(ex)}");
            }
        }

        StatusText = BuildBulkStatusText(
            "ServerMonitor.Status.DisconnectedBulkFormat", succeeded, rows.Count, failures.Count);
        if (failures.Count > 0)
            ErrorMessage = string.Join("\n", failures.Take(10));
        Refresh();
    }

    /// <summary>
    /// Фактический набор сеансов для действия (issue #72): мультивыделение вкладки; при
    /// пустом множестве (прежние сценарии/тесты) — только одиночная выбранная строка.
    /// </summary>
    private IReadOnlyList<RacSessionRow> EffectiveSelectedSessions()
    {
        if (_selectedSessions.Count > 0)
            return _selectedSessions;
        return _selectedSession is { } row ? new[] { row } : Array.Empty<RacSessionRow>();
    }

    /// <summary>Аналог <see cref="EffectiveSelectedSessions"/> для соединений.</summary>
    private IReadOnlyList<RacConnectionRow> EffectiveSelectedConnections()
    {
        if (_selectedConnections.Count > 0)
            return _selectedConnections;
        return _selectedConnection is { } row ? new[] { row } : Array.Empty<RacConnectionRow>();
    }

    /// <summary>
    /// Сводка массовой операции в статус-строку (issue #72): «Завершено сеансов: N из M»
    /// (или «Разорвано соединений: N из M») и суффикс «. Ошибок: K» при частичных неудачах.
    /// Internal — для юнит-тестов читаемости сводки.
    /// </summary>
    internal static string BuildBulkStatusText(string statusKey, int succeeded, int total, int errors)
    {
        var summary = string.Format(LocalizationManager.T(statusKey), succeeded, total);
        return errors > 0
            ? string.Format(LocalizationManager.T("ServerMonitor.Status.BulkErrorsFormat"), summary, errors)
            : summary;
    }

    /// <summary>
    /// Запускает таймер автообновления данных выбранного кластера (5 с). Создаётся при
    /// успешном подключении, если переключатель автообновления включён; повторный запуск —
    /// no-op. Тик идёт через <see cref="Refresh"/> с тем же флагом занятости, что и ручное
    /// «Обновить» (наложение исключено). При выключенном переключателе — no-op: единая
    /// защита всех точек запуска (issue #324, «галка снята, а таймер работает»).
    /// </summary>
    private void StartAutoRefresh()
    {
        // issue #90: после закрытия окна таймер не возрождается (даже если гонка
        // «Refresh завершился после Dispose» успела вызвать StartAutoRefresh).
        if (_disposed || !IsAutoRefreshEnabled)
            return;
        var intervalMs = AutoRefreshIntervalSeconds * 1000;
        _autoRefreshTimer ??= new Timer(_ => Refresh(), null, intervalMs, intervalMs);
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
    }

    /// <summary>
    /// Останавливает таймер автообновления (Dispose окна, ошибка подключения, выключение
    /// переключателя) и ОТМЕНЯЕТ выполняющийся цикл загрузки вкладок (issue #90):
    /// запущенные rac-команды завершаются, новые не стартуют.
    /// </summary>
    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        CancelRefreshCycle();
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
    }

    /// <summary>Отменяет токен текущего цикла загрузки вкладок (issue #90).</summary>
    private void CancelRefreshCycle()
    {
        var previous = _refreshCts;
        _refreshCts = null;
        if (previous is null)
            return;
        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Токен уже отменён/освобождён — цель достигнута.
        }
        previous.Dispose();
    }

    /// <inheritdoc />
    /// <summary>
    /// Останавливает таймер автообновления и выполняющийся цикл rac-команд
    /// (вызывается окном при закрытии, issue #90).
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        StopAutoRefresh();
    }

    /// <summary>
    /// Показывает предупреждение в UI-потоке (issue #3, 1.0.0.22; замечание 7OH 19.10):
    /// async-методы используют <c>ConfigureAwait(false)</c>, поэтому продолжение после
    /// await выполняется в потоке пула, а WPF-окно MaterialMessageWindow можно
    /// создавать только в STA-потоке UI — иначе «Вызывающим потоком должен быть STA».
    /// При отсутствии маршалера (тесты) предупреждение показывается сразу, как раньше.
    /// </summary>
    private void ShowWarningOnUi(string message, string title)
    {
        if (_dispatchToUi is null)
        {
            _dialogs.ShowWarning(message, title);
            return;
        }
        _dispatchToUi(() => _dialogs.ShowWarning(message, title));
    }

    // ===================== Внутреннее =====================

    private RacConnectionParams BuildParams()
    {
        // Порт можно указать полем «Порт» или прямо в адресе как «host:port»
        // (например «localhost:27545» — как в командной строке rac, issue #324):
        // порт из адреса имеет приоритет над значением поля.
        var (host, port) = RacConnectionAddress.Split(ServerAddress, ServerPort);
        return new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = UserName?.Trim() ?? string.Empty,
            Password = Password ?? string.Empty
        };
    }

    /// <summary>
    /// Сбрасывает выбор кластера, список кластеров и данные вкладок при старте нового
    /// подключения (issue #77). Выполняется СИНХРОННО до первого await в
    /// <see cref="ConnectAsync"/> (поток вызова: UI-поток WPF/Avalonia, тесты) — иначе
    /// ApplyClusters, отложенный в UI-поток (Dispatcher.BeginInvoke / UIThread.Post),
    /// в связке с проверкой устаревшего <see cref="SelectedClusterId"/> запускал
    /// LoadClusterDataAsync со старым идентификатором кластера прежнего сервера, и
    /// переключение на другой сервер всегда заканчивалось ошибкой rac. Коллекции и
    /// списки очищаются напрямую (без dispatch): метода вызывается из UI-потока.
    /// </summary>
    private void ResetClusterState()
    {
        // Выбор кластера: сеттер при null загрузку не запускает, при этом сбрасывает
        // фильтр по базе (идентификаторы баз другого кластера бессмысленны).
        SelectedClusterId = null;

        // Список кластеров очищается сразу, а не отложенным dispatch: фоновая
        // проверка «ClusterRows.Count > 0» в ConnectAsync не должна видеть кластеры
        // прежнего сервера.
        Clusters = Array.Empty<RacCluster>();
        ClusterRows = Array.Empty<RacClusterRow>();
        OnPropertyChanged(nameof(Clusters));
        OnPropertyChanged(nameof(ClusterRows));

        // Кэш имён информационных баз и клиентский фильтр по базе.
        _infobaseNames.Clear();
        _selectedInfobase = null;
        _selectedInfobaseId = null;
        OnPropertyChanged(nameof(SelectedInfobase));
        OnPropertyChanged(nameof(SelectedInfobaseId));

        // Данные вкладок прежнего сервера больше не актуальны.
        Processes.Clear();
        Sessions.Clear();
        Connections.Clear();
        Locks.Clear();
        Infobases.Clear();
        LicensedSessions.Clear();
        InstalledLicenses.Clear();
        ClusterProperties.Clear();
        FilteredSessions.Clear();
        FilteredConnections.Clear();
        FilteredLocks.Clear();
        SelectedSessions = Array.Empty<RacSessionRow>();
        SelectedConnections = Array.Empty<RacConnectionRow>();
        SelectedSession = null;
        SelectedConnection = null;
        ClusterInfo = null;
        ClusterInfoText = string.Empty;
        LicenseSummaryText = string.Empty;
        LicenseStatusText = string.Empty;
        OnPropertyChanged(nameof(HasInfobases));
        OnPropertyChanged(nameof(HasInfobaseFilter));
        OnPropertyChanged(nameof(InfobaseFilterText));
        OnPropertyChanged(nameof(CanDropInfobase));
        RaiseTabHeaderChanges();
    }

    private void ApplyClusters(IReadOnlyList<RacCluster> clusters)
    {
        void Apply()
        {
            Clusters = clusters;
            ClusterRows = clusters
                .Where(c => c is not null)
                .Select(c => new RacClusterRow(c))
                .ToList();
            OnPropertyChanged(nameof(Clusters));
            OnPropertyChanged(nameof(ClusterRows));

            // issue #324: единственный кластер выбирается в списке СРАЗУ (до завершения
            // ConnectAsync); при нескольких — выбор не навязывается (прежнее поведение);
            // при отсутствии — сбрасывается.
            if (ClusterRows.Count == 1)
                SelectedClusterId = ClusterRows[0].Id;
            else if (ClusterRows.Count == 0)
                SelectedClusterId = null;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyClusterData(
        IReadOnlyList<RacProcessInfo> processes,
        IReadOnlyList<RacSessionInfo> sessions,
        IReadOnlyList<RacConnectionInfo> connections,
        IReadOnlyList<RacLockInfo> locks,
        IReadOnlyList<RacInfobaseSummary> infobases,
        RacClusterInfo? info,
        IReadOnlyList<RacSessionInfo> licensedSessions = null!,
        IReadOnlyList<ProgramLicense> installedLicenses = null!,
        string? licenseWarning = null)
    {
        void Apply()
        {
            // Выбор сохраняем по идентификатору: после автообновления строка с тем же
            // Id остаётся выбранной, исчезнувшая (завершённый сеанс) — сбрасывается.
            // issue #72: множественный выбор сохраняется по идентификаторам (как и
            // одиночный) — после автообновления выбранные строки остаются выбранными.
            var sessionIds = SelectedSessions.Select(s => s.Id).ToHashSet();
            var connectionIds = SelectedConnections.Select(c => c.Id).ToHashSet();
            var sessionId = SelectedSession?.Id;
            var connectionId = SelectedConnection?.Id;

            // Кэш имён информационных баз кластера: колонка «База» и фильтр по базе
            // используют имя из «infobase summary list».
            _infobaseNames.Clear();
            foreach (var ib in infobases)
            {
                if (ib.InfobaseId != Guid.Empty && !_infobaseNames.ContainsKey(ib.InfobaseId))
                    _infobaseNames[ib.InfobaseId] = ib.Name;
            }

            ReplaceRows(Processes, processes.Select(p => new RacProcessRow(p)));
            // issue #324, C2: в строку сеанса передаётся имя информационной базы
            // (сопоставление infobase-id из «session list» со «infobase summary list»).
            ReplaceRows(Sessions, sessions.Select(s => new RacSessionRow(s, SessionInfobaseName(s))));
            ReplaceRows(Connections, connections.Select(c => new RacConnectionRow(c)));
            ReplaceRows(Locks, locks.Select(l => new RacLockRow(l)));
            ClusterInfo = info;
            ClusterInfoText = FormatClusterInfo(info);
            ReplaceRows(ClusterProperties, BuildClusterPropertyRows(info));

            // п.5 «Мониторинг лицензий» (1.0.0.87): занятые сеансами лицензии
            // (только сеансы с распознанным типом) + установленные программные лицензии.
            var curatedSessions = licensedSessions ?? Array.Empty<RacSessionInfo>();
            var curatedInstalled = installedLicenses ?? Array.Empty<ProgramLicense>();
            ReplaceRows(LicensedSessions, curatedSessions
                .Where(s => !string.IsNullOrWhiteSpace(s.LicType))
                .Select(s => new RacLicensedSessionRow(s, SessionInfobaseName(s))));
            ReplaceRows(InstalledLicenses, curatedInstalled);
            LicenseStatusText = licenseWarning ?? string.Empty;
            LicenseSummaryText = BuildLicenseSummaryText(curatedSessions, curatedInstalled);
            CheckLicenseThresholdAlert(curatedSessions, curatedInstalled);

            // Вкладка «Базы» (issue #3): строки из «infobase summary list». Выбор
            // восстанавливается по Id; если база исчезла (например, удалена) — выбор
            // и фильтр сеансов/соединений/блокировок сбрасываются на «Все базы».
            // Сеттеры не используем: восстановление НЕ должно запускать повторную
            // загрузку данных (иначе цикл «выбор → загрузка → восстановление»).
            var infobaseFilter = _selectedInfobaseId;
            ReplaceRows(Infobases, SortInfobaseRows(
                infobases.Select(ib => new RacInfobaseRow(ib)), InfobasesSortDescending));
            OnPropertyChanged(nameof(HasInfobases));
            if (infobaseFilter is Guid fid2 && !_infobaseNames.ContainsKey(fid2))
                infobaseFilter = null;
            _selectedInfobaseId = infobaseFilter;
            OnPropertyChanged(nameof(SelectedInfobaseId));
            _selectedInfobase = infobaseFilter is Guid iid
                ? Infobases.FirstOrDefault(r => r.InfobaseId == iid)
                : null;
            OnPropertyChanged(nameof(SelectedInfobase));
            OnPropertyChanged(nameof(CanDropInfobase));

            // Клиентская фильтрация по выбранной базе (issue #3): пересобираем
            // отфильтрованные коллекции из полного снимка кластера.
            ApplyInfobaseFilter();

            // issue #72: восстановление мультивыделения по Id; исчезнувшие строки
            // (завершённый сеанс / разорванное соединение) сбрасываются.
            SelectedSessions = Sessions.Where(x => sessionIds.Contains(x.Id)).ToList();
            SelectedConnections = Connections.Where(x => connectionIds.Contains(x.Id)).ToList();
            SelectedSession = sessionId is Guid s
                ? Sessions.FirstOrDefault(x => x.Id == s)
                : SelectedSessions.FirstOrDefault();
            SelectedConnection = connectionId is Guid c
                ? Connections.FirstOrDefault(x => x.Id == c)
                : SelectedConnections.FirstOrDefault();

            // Заголовки вкладок с количеством строк (issue #3): «Сеансы (12)» и т.д.
            RaiseTabHeaderChanges();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Имя информационной базы сеанса из кэша (issue #324, C2: колонка «Информационная
    /// база» на вкладке «Сеансы»); «—» для сеансов без базы/неизвестных GUID.
    /// </summary>
    private string SessionInfobaseName(RacSessionInfo session) =>
        session.InfobaseId is Guid id && _infobaseNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : LocalizationManager.T("ServerMonitor.Session.UnknownBase");

    /// <summary>
    /// Пересобирает отфильтрованные коллекции (сеансы/соединения/блокировки) по
    /// выбранной базе (issue #3). Фильтрация КЛИЕНТСКАЯ — по уже загруженному снимку
    /// кластера, без rac-вызовов и без флага занятости: сеансы — по InfobaseId строки,
    /// соединения — через сопоставление SessionId соединения к сеансу (по сеансу
    /// узнаём базу), блокировки — по InfobaseId строки. null — «Все базы».
    /// </summary>
    private void ApplyInfobaseFilter()
    {
        var filter = _selectedInfobaseId;
        if (filter is Guid id)
        {
            ReplaceRows(FilteredSessions, Sessions.Where(s => s.InfobaseId == id));
            var sessionInfobase = Sessions
                .Where(s => s.InfobaseId is not null)
                .ToDictionary(s => s.Id, s => s.InfobaseId!.Value);
            ReplaceRows(
                FilteredConnections,
                Connections.Where(c => sessionInfobase.TryGetValue(c.SessionId, out var ib) && ib == id));
            ReplaceRows(FilteredLocks, Locks.Where(l => l.InfobaseId == id));
        }
        else
        {
            ReplaceRows(FilteredSessions, Sessions);
            ReplaceRows(FilteredConnections, Connections);
            ReplaceRows(FilteredLocks, Locks);
        }

        OnPropertyChanged(nameof(InfobaseFilterText));
        OnPropertyChanged(nameof(HasInfobaseFilter));
        // Количество строк во вкладках сеансов/соединений/блокировок могло измениться
        // после фильтрации по базе (issue #3: «Сеансы (0)» при пустом отборе).
        RaiseTabHeaderChanges();
    }

    private static void ReplaceRows<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
            target.Add(row);
    }

    /// <summary>
    /// Строки «свойство — значение» для вкладки «Информация о кластере» (issue #324, C3):
    /// редактируется безопасный набор параметров (имя, таймауты/лимиты, уровень
    /// безопасности, пинг, аутентификация); остальные свойства — только чтение.
    /// Ключи rac сопоставляются регистронезависимо с нормализацией дефисов
    /// («expiration-timeout» ≡ «expirationTimeout»).
    /// </summary>
    private static IReadOnlyList<RacClusterPropertyRow> BuildClusterPropertyRows(RacClusterInfo? info)
    {
        var rows = new List<RacClusterPropertyRow>();
        if (info is null)
            return rows;

        foreach (var pair in info.Properties)
            rows.Add(new RacClusterPropertyRow(pair.Key, pair.Value, IsEditableClusterProperty(pair.Key)));

        // Резерв: словарь пуст (не распознан) — типизированные поля как только-читаемые.
        if (rows.Count == 0 && info.Name.Length > 0)
        {
            rows.Add(new RacClusterPropertyRow("name", info.Name, editable: true));
            rows.Add(new RacClusterPropertyRow("hostName", info.HostName, editable: false));
        }

        return rows;
    }

    /// <summary>Ключ свойства rac (регистронезависимо, дефисы нормализуются) в канонической форме.</summary>
    internal static string NormalizeClusterPropertyKey(string rawKey) =>
        rawKey?.Replace("-", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Входит ли свойство кластера в безопасный набор правки (rac «cluster update»).</summary>
    internal static bool IsEditableClusterProperty(string rawKey) =>
        NormalizeClusterPropertyKey(rawKey) switch
        {
            "name" or "expirationtimeout" or "lifetimelimit" or "maxmemorysize" or
            "maxmemorytimelimit" or "securitylevel" or "pingperiod" or "pingtimeout" or
            "maxauthattempts" or "authlockduration" => true,
            _ => false
        };

    /// <summary>
    /// Собирает изменения параметров кластера из правленых строк вкладки
    /// «Информация о кластере» (issue #324, C3). Числовые значения разбираются
    /// инвариантно; при ошибке разбора возвращает null и показывает предупреждение.
    /// </summary>
    private RacClusterUpdate? BuildClusterUpdateFromEdits()
    {
        var update = new RacClusterUpdate();
        foreach (var row in ClusterProperties)
        {
            if (!row.IsChanged)
                continue;

            var value = row.EditValue?.Trim() ?? string.Empty;
            switch (NormalizeClusterPropertyKey(row.RawKey))
            {
                case "name":
                    update.Name = value;
                    break;
                case "expirationtimeout":
                case "lifetimelimit":
                case "maxmemorysize":
                case "maxmemorytimelimit":
                case "pingperiod":
                case "pingtimeout":
                case "authlockduration":
                    if (!TryParseLong(row, value, out var longValue))
                        return null;
                    AssignLong(row.RawKey, longValue);
                    break;
                case "securitylevel":
                case "maxauthattempts":
                    if (!TryParseInt(row, value, out var intValue))
                        return null;
                    AssignInt(row.RawKey, intValue);
                    break;
            }
        }

        return update;

        void AssignLong(string rawKey, long value)
        {
            switch (NormalizeClusterPropertyKey(rawKey))
            {
                case "expirationtimeout": update.ExpirationTimeout = value; break;
                case "lifetimelimit": update.LifetimeLimit = value; break;
                case "maxmemorysize": update.MaxMemorySize = value; break;
                case "maxmemorytimelimit": update.MaxMemoryTimeLimit = value; break;
                case "pingperiod": update.PingPeriod = value; break;
                case "pingtimeout": update.PingTimeout = value; break;
                case "authlockduration": update.AuthLockDuration = value; break;
            }
        }

        void AssignInt(string rawKey, int value)
        {
            switch (NormalizeClusterPropertyKey(rawKey))
            {
                case "securitylevel": update.SecurityLevel = value; break;
                case "maxauthattempts": update.MaxAuthAttempts = value; break;
            }
        }
    }

    /// <summary>Разбор числового значения поля правки (инвариантно) с понятной ошибкой.</summary>
    private bool TryParseLong(RacClusterPropertyRow row, string value, out long result)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return true;

        var message = string.Format(
            LocalizationManager.T("ServerMonitor.Cluster.InvalidNumberFormat"),
            row.DisplayName, value);
        ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
        return false;
    }

    /// <summary>Разбор целочисленного значения поля правки (инвариантно) с понятной ошибкой.</summary>
    private bool TryParseInt(RacClusterPropertyRow row, string value, out int result)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return true;

        var message = string.Format(
            LocalizationManager.T("ServerMonitor.Cluster.InvalidNumberFormat"),
            row.DisplayName, value);
        ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
        return false;
    }

    /// <summary>
    /// «Сохранить изменения» на вкладке «Информация о кластере» (issue #324, C3):
    /// подтверждение → rac «cluster update» с ТОЛЬКО изменёнными параметрами →
    /// перечитывание данных кластера. Ошибки не роняют окно.
    /// </summary>
    public async Task SaveClusterPropertiesAsync()
    {
        if (!HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        var update = BuildClusterUpdateFromEdits();
        if (update is null)
            return; // предупреждение о неверном числе уже показано
        if (update.IsEmpty)
        {
            StatusText = LocalizationManager.T("ServerMonitor.Cluster.NothingToSave");
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(
                    LocalizationManager.T("ServerMonitor.Cluster.ConfirmFormat"),
                    CountChanges(update)),
                LocalizationManager.T("ServerMonitor.Cluster.SaveTitle")))
            return;

        try
        {
            var ok = await _rac.UpdateClusterAsync(BuildParams(), clusterId, update).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Cluster.SaveFailedFormat") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
                StatusText = detail;
                return;
            }

            StatusText = LocalizationManager.T("ServerMonitor.Cluster.SavedFormat");
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Cluster.SaveFailedFormat") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            // После обновления параметры кластера перечитываются (и строки правки
            // пересобираются из фактического вывода rac).
            Refresh();
        }
    }

    /// <summary>Число изменённых параметров для текста подтверждения.</summary>
    private static int CountChanges(RacClusterUpdate update) =>
        (update.Name is null ? 0 : 1) +
        (update.ExpirationTimeout is null ? 0 : 1) +
        (update.LifetimeLimit is null ? 0 : 1) +
        (update.MaxMemorySize is null ? 0 : 1) +
        (update.MaxMemoryTimeLimit is null ? 0 : 1) +
        (update.SecurityLevel is null ? 0 : 1) +
        (update.PingPeriod is null ? 0 : 1) +
        (update.PingTimeout is null ? 0 : 1) +
        (update.MaxAuthAttempts is null ? 0 : 1) +
        (update.AuthLockDuration is null ? 0 : 1);

    private static string FormatClusterInfo(RacClusterInfo? info)
    {
        if (info is null)
            return LocalizationManager.T("ServerMonitor.Empty.Info");

        var sb = new StringBuilder();
        if (info.Properties.Count > 0)
        {
            foreach (var pair in info.Properties)
                sb.AppendLine($"{pair.Key}: {pair.Value}");
        }
        else
        {
            // Резерв: если словарь пуст (не распознан), выводим типизированные поля.
            sb.AppendLine($"name: {info.Name}");
            sb.AppendLine($"hostName: {info.HostName}");
            sb.AppendLine($"port: {info.Port}");
            sb.AppendLine($"expirationTimeout: {info.ExpirationTimeout}");
            sb.AppendLine($"lifetimeLimit: {info.LifetimeLimit}");
            sb.AppendLine($"maxMemorySize: {info.MaxMemorySize}");
            sb.AppendLine($"maxMemoryTimeLimit: {info.MaxMemoryTimeLimit}");
            sb.AppendLine($"securityLevel: {info.SecurityLevel}");
            sb.AppendLine($"sessionIdleTimeout: {info.SessionIdleTimeout}");
            sb.AppendLine($"sessionMaxMemorySize: {info.SessionMaxMemorySize}");
            sb.AppendLine($"sessionMaxTimeLimit: {info.SessionMaxTimeLimit}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var message = ex.Message;
        return string.IsNullOrWhiteSpace(message)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : message;
    }

    /// <summary>Текст ошибки действия rac (из <see cref="IRacClient.LastActionError"/>).</summary>
    private static string BuildActionError(string? lastActionError) =>
        string.IsNullOrWhiteSpace(lastActionError)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : lastActionError;

    private bool TryEnterBusy()
    {
        // Один запрос за раз: длительная rac-команда не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        OnPropertyChanged(nameof(IsBusy));
    }
}