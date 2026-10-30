using System.Globalization;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;

namespace Workspace1C.Services;

/// <summary>Правило оповещения для сервера 1С из справочника «Серверы 1С».</summary>
public sealed class ServerAlertRule
{
    /// <summary>Идентификатор сервера (<see cref="ManagementServer.Id"/>).</summary>
    public string ServerId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Порог числа сеансов в кластере (0 — не проверять).</summary>
    public int MaxSessions { get; set; }

    /// <summary>Порог памяти одного рабочего процесса, МБ (0 — не проверять).</summary>
    public int MaxProcessMemoryMb { get; set; }

    /// <summary>Порог числа ожидающих блокировок (0 — не проверять).</summary>
    public int MaxWaitingLocks { get; set; }
}

/// <summary>Наблюдение за ошибками журнала регистрации файловой базы.</summary>
public sealed class EventLogWatch
{
    public string InfobaseId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Порог числа новых ошибок за интервал (1 — о каждой ошибке).</summary>
    public int MinErrors { get; set; } = 1;
}

/// <summary>Настройки мониторинга (<c>server_alerts.json</c>).</summary>
public sealed class ServerAlertSettings
{
    public bool Enabled { get; set; }

    /// <summary>Интервал проверки, минут.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Не повторять одинаковое оповещение чаще, чем раз в N минут.</summary>
    public int RepeatMinutes { get; set; } = 60;

    public List<ServerAlertRule> Rules { get; set; } = new();

    public List<EventLogWatch> EventLogWatches { get; set; } = new();
}

/// <summary>
/// Оповещения мониторинга (выпуск 1.0.0.69): фоновая проверка кластеров серверов 1С
/// (число сеансов, память рабочих процессов, ожидающие блокировки) и новых ошибок
/// в журналах регистрации файловых баз. Превышения отправляются через
/// <see cref="INotificationService"/> (системное уведомление, Telegram, email)
/// с подавлением повторов. Правила проверки — чистые функции (тесты).
/// </summary>
public sealed class ServerAlertsService : IDisposable
{
    public const string FileName = "server_alerts.json";

    /// <summary>
    /// Тег источника rac-команд фоновых проверок в CM_RAC-логе (issue #90):
    /// по метке «RAC [ALERTS]: …» видно, что вызов пришёл из сервиса оповещений.
    /// </summary>
    internal const string AlertsLogSourceTag = "ALERTS";

    private readonly IRacClient _rac;
    private readonly IServerListStore _servers;
    private readonly INotificationService _notifications;
    private readonly IInfobaseRepository? _infobases;
    private readonly IAppLogger? _logger;

    /// <summary>Установленные программные лицензии («ring license list»/licensetool, п.5, 1.0.0.87).</summary>
    private readonly IRingLicenseService? _ringLicenses;

    /// <summary>Настройки каналов уведомлений (порог «свободно меньше N», п.5, 1.0.0.87).</summary>
    private readonly INotificationSettingsStore? _notificationSettings;

    /// <summary>История размеров баз (п.7 «История размеров и прогноз диска», 1.0.0.89).</summary>
    private readonly SizeHistoryStore? _sizeHistory;

    private readonly Dictionary<string, DateTime> _lastSent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _eventLogChecked = new(StringComparer.Ordinal);
    private Timer? _timer;
    private int _running;

    public ServerAlertsService(IRacClient rac, IServerListStore servers, INotificationService notifications,
        IInfobaseRepository? infobases = null, IAppLogger? logger = null,
        IRingLicenseService? ringLicenses = null, INotificationSettingsStore? notificationSettings = null,
        SizeHistoryStore? sizeHistory = null)
    {
        _rac = rac;
        _servers = servers;
        _notifications = notifications;
        _infobases = infobases;
        _logger = logger;
        _ringLicenses = ringLicenses;
        _notificationSettings = notificationSettings;
        _sizeHistory = sizeHistory;
    }

    public static ServerAlertSettings Load() => FeatureDataStore.Load<ServerAlertSettings>(FileName);

    public static void Save(ServerAlertSettings settings) => FeatureDataStore.Save(FileName, settings);

    /// <summary>Запускает фоновые проверки (перечитывает настройки на каждом цикле).</summary>
    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private DateTime _nextRun = DateTime.MinValue;

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        // Диагностика источника rac-команд (issue #90): фоновые проверки помечаются
        // «ALERTS» — записи CM_RAC отличимы от вызовов окна «Серверы 1С» («MONITOR»).
        RacClient.LogSource.Value = AlertsLogSourceTag;
        try
        {
            var settings = Load();
            if (!settings.Enabled || DateTime.Now < _nextRun)
                return;
            _nextRun = DateTime.Now.AddMinutes(Math.Max(1, settings.IntervalMinutes));
            await CheckNowAsync(settings, notify: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка проверки мониторинга серверов", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// Выполняет все проверки один раз. Возвращает тексты найденных превышений
    /// (для кнопки «Проверить сейчас»); при <paramref name="notify"/> отправляет оповещения.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckNowAsync(ServerAlertSettings settings, bool notify,
        CancellationToken ct = default)
    {
        var alerts = new List<(string Key, string Text)>();
        var servers = _servers.Load();
        foreach (var rule in settings.Rules.Where(r => r.Enabled))
        {
            var server = servers.FirstOrDefault(s => s.Id == rule.ServerId);
            if (server is null)
                continue;
            try
            {
                alerts.AddRange(await CheckServerAsync(server, rule, ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                alerts.Add(($"unreachable:{server.Id}", string.Format(
                    LocalizationManager.T("Alerts.Unreachable"), server, ex.Message)));
            }
        }

        // п.5 «Мониторинг лицензий» (1.0.0.87): порог «свободно меньше N» — из настроек
        // уведомлений (notifications.json); проверяется параллельно с остальными
        // alert-задачами. При недоступности ring/licensetool проверка тихо пропускается
        // (сводки нет — оповещать не о чем; логика LicenseCalculator.CheckThreshold).
        if (_ringLicenses is not null && _notificationSettings is not null)
        {
            var notificationSettings = _notificationSettings.Load();
            var threshold = notificationSettings.LicenseFreeThreshold;
            if (threshold > 0)
            {
                var repeatHours = Math.Max(1, notificationSettings.LicenseAlertRepeatHours);
                foreach (var server in servers)
                {
                    try
                    {
                        alerts.AddRange(await CheckLicensesAsync(
                            server, threshold, repeatHours, ct).ConfigureAwait(false));
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"ЛИЦЕНЗИИ: {server} — проверка не удалась: {ex.Message}");
                    }
                }
            }
        }

        if (_infobases is not null)
        {
            var all = _infobases.Load();
            foreach (var watch in settings.EventLogWatches.Where(w => w.Enabled))
            {
                var ib = all.FirstOrDefault(b => b.Id == watch.InfobaseId);
                if (ib is null)
                    continue;
                var text = CheckEventLog(ib, watch);
                if (text is not null)
                    alerts.Add(($"eventlog:{ib.Id}:{DateTime.Now:yyyyMMddHHmm}", text));
            }

            // п.7 «История размеров и прогноз диска» (1.0.0.89): дневные снимки размеров
            // по расписанию (не чаще раза в день) + оповещение «диск под копиями закончится
            // примерно через N дней», когда прогноз ≤ порога из настроек уведомлений
            // (0 — отключено). Защита от повторов — общий _lastSent (ключ
            // «diskforecast:{имя диска}»).
            if (_sizeHistory is not null)
            {
                try
                {
                    CollectDailySizeSnapshots(all);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Снимки размеров по расписанию не собраны: {ex.Message}");
                }

                try
                {
                    var notificationSettings = _notificationSettings?.Load();
                    var thresholdDays = notificationSettings?.DiskForecastThresholdDays ?? 0;
                    if (thresholdDays > 0)
                    {
                        var repeatDays = Math.Max(1, notificationSettings!.DiskForecastAlertRepeatDays);
                        alerts.AddRange(CheckDiskForecastAsync(all, thresholdDays, repeatDays));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Проверка прогноза диска не выполнена: {ex.Message}");
                }
            }
        }

        if (notify)
        {
            var repeat = TimeSpan.FromMinutes(Math.Max(1, settings.RepeatMinutes));
            // Период повтора оповещений о лицензиях — из настроек уведомлений (часы);
            // о прогнозе диска (п.7, 1.0.0.89) — из настроек уведомлений (дни).
            var licenseRepeat = TimeSpan.FromHours(24);
            var diskForecastRepeat = TimeSpan.FromDays(1);
            if (_notificationSettings is not null)
            {
                var ns = _notificationSettings.Load();
                licenseRepeat = TimeSpan.FromHours(Math.Max(1, ns.LicenseAlertRepeatHours));
                diskForecastRepeat = TimeSpan.FromDays(Math.Max(1, ns.DiskForecastAlertRepeatDays));
            }
            foreach (var (key, text) in alerts)
            {
                var effectiveRepeat = key.StartsWith("license:", StringComparison.Ordinal)
                    ? licenseRepeat
                    : key.StartsWith(DiskForecastService.DiskForecastAlertKey(""), StringComparison.Ordinal)
                        ? diskForecastRepeat
                        : repeat;
                if (_lastSent.TryGetValue(key, out var last) && DateTime.Now - last < effectiveRepeat)
                    continue;
                _lastSent[key] = DateTime.Now;
                _notifications.Show(LocalizationManager.T("Alerts.NotificationTitle"), text,
                    NotificationKind.Warning, NotificationEvent.Monitoring);
            }
        }
        return alerts.Select(a => a.Text).ToList();
    }

    private async Task<List<(string Key, string Text)>> CheckServerAsync(ManagementServer server, ServerAlertRule rule,
        CancellationToken ct)
    {
        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        var parameters = new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = server.User ?? string.Empty,
            Password = server.Password ?? string.Empty
        };
        var result = new List<(string, string)>();
        var clusters = await _rac.GetClustersAsync(parameters, ct).ConfigureAwait(false);
        foreach (var cluster in clusters)
        {
            IReadOnlyList<RacSessionInfo> sessions = rule.MaxSessions > 0
                ? await _rac.GetSessionsAsync(parameters, cluster.Id, null, ct).ConfigureAwait(false)
                : Array.Empty<RacSessionInfo>();
            IReadOnlyList<RacProcessInfo> processes = rule.MaxProcessMemoryMb > 0
                ? await _rac.GetProcessesAsync(parameters, cluster.Id, ct).ConfigureAwait(false)
                : Array.Empty<RacProcessInfo>();
            IReadOnlyList<RacLockInfo> locks = rule.MaxWaitingLocks > 0
                ? await _rac.GetLocksAsync(parameters, cluster.Id, null, ct).ConfigureAwait(false)
                : Array.Empty<RacLockInfo>();

            var where = $"{server} / {(string.IsNullOrWhiteSpace(cluster.Name) ? cluster.Host : cluster.Name)}";
            foreach (var text in Evaluate(rule, where, sessions.Count, processes, locks))
                result.Add(($"{server.Id}:{cluster.Id}:{text.GetHashCode()}", text));
        }
        return result;
    }

    /// <summary>
    /// Проверка порога «свободно меньше N» для сервера (п.5, 1.0.0.87): сеансы с
    /// лицензиями по всем кластерам сервера + установленные программные лицензии
    /// (один вызов ring/licensetool на проверку). Порог/период повтора — из настроек
    /// уведомлений; защита от спама — общий <see cref="_lastSent"/> (ключ
    /// «license:{имя сервера}»). Возвращает тексты, которые пора отправить.
    /// </summary>
    private async Task<List<(string Key, string Text)>> CheckLicensesAsync(
        ManagementServer server, int threshold, int repeatHours, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        var parameters = new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = server.User ?? string.Empty,
            Password = server.Password ?? string.Empty
        };

        var installed = (await _ringLicenses!.GetInstalledLicensesAsync(ct).ConfigureAwait(false)).Licenses;
        var clusters = await _rac.GetClustersAsync(parameters, ct).ConfigureAwait(false);
        var allLicensed = new List<RacSessionInfo>();
        foreach (var cluster in clusters)
        {
            allLicensed.AddRange(
                await _rac.GetLicensedSessionsAsync(parameters, cluster.Id, ct).ConfigureAwait(false));
        }

        var snapshot = LicenseCalculator.ComputeSnapshot(allLicensed, installed);
        // Имя сервера в оповещении: описание из справочника + адрес:порт (как у других алертов).
        var label = $"{server.Description} ({host}:{port.ToString(CultureInfo.InvariantCulture)})".Trim();
        var texts = LicenseCalculator.CheckThreshold(
            label, snapshot.TotalInstalled, snapshot.TotalUsed,
            snapshot.TotalFree, threshold, DateTime.Now, _lastSent, repeatHours);
        foreach (var text in texts)
            result.Add((LicenseCalculator.LicenseAlertKey(label), text));
        return result;
    }

    /// <summary>Проверка порогов правила по данным кластера (чистая функция).</summary>
    public static IReadOnlyList<string> Evaluate(ServerAlertRule rule, string where, int sessionCount,
        IEnumerable<RacProcessInfo> processes, IEnumerable<RacLockInfo> locks)
    {
        var result = new List<string>();
        if (rule.MaxSessions > 0 && sessionCount > rule.MaxSessions)
            result.Add(string.Format(LocalizationManager.T("Alerts.TooManySessions"), where, sessionCount, rule.MaxSessions));

        if (rule.MaxProcessMemoryMb > 0)
        {
            foreach (var p in processes)
            {
                // memory-size в rac — килобайты.
                var mb = p.MemorySize / 1024;
                if (mb > rule.MaxProcessMemoryMb)
                    result.Add(string.Format(LocalizationManager.T("Alerts.ProcessMemory"), where,
                        string.IsNullOrWhiteSpace(p.Host) ? "?" : p.Host, p.Pid,
                        mb.ToString(CultureInfo.InvariantCulture), rule.MaxProcessMemoryMb));
            }
        }

        if (rule.MaxWaitingLocks > 0)
        {
            var waiting = locks.Count(l => l.Waiting);
            if (waiting > rule.MaxWaitingLocks)
                result.Add(string.Format(LocalizationManager.T("Alerts.WaitingLocks"), where, waiting, rule.MaxWaitingLocks));
        }
        return result;
    }

    /// <summary>Новые ошибки журнала регистрации базы с прошлой проверки.</summary>
    private string? CheckEventLog(Infobase ib, EventLogWatch watch)
    {
        var now = DateTime.Now;
        if (!_eventLogChecked.TryGetValue(ib.Id, out var since))
        {
            // Первая проверка после запуска — только с текущего момента, без «старых» ошибок.
            _eventLogChecked[ib.Id] = now;
            return null;
        }
        _eventLogChecked[ib.Id] = now;
        var location = LgdJournalLocator.Resolve(ib);
        var count = EventLogQueryService.CountErrors(location, since, now, 1000);
        if (count is null || count.Value < Math.Max(1, watch.MinErrors))
            return null;
        return string.Format(LocalizationManager.T("Alerts.EventLogErrors"), ib.Name, count.Value,
            since.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    // ---- п.7 «История размеров баз и прогноз диска» (1.0.0.89) ----

    /// <summary>
    /// Собирает дневные снимки размеров по расписанию: один снимок на базу в сутки
    /// (не чаще). Тяжёлое сканирование каталога копий — здесь же (фоновый цикл
    /// мониторинга); ошибки отдельной базы не роняют весь цикл.
    /// </summary>
    private void CollectDailySizeSnapshots(IReadOnlyList<Infobase> bases)
    {
        if (_sizeHistory is null || bases is null || bases.Count == 0)
            return;

        var scenarios = AppServices.TryGetService<IBackupScenarioStore>()?.LoadAll()
            ?? Array.Empty<BackupScenario>();
        AppSettings settings;
        try
        {
            settings = _infobases!.LoadSettings();
        }
        catch
        {
            settings = new AppSettings();
        }
        var maxDays = settings.SizeHistoryMaxDays > 0
            ? settings.SizeHistoryMaxDays
            : SizeHistoryStore.DefaultMaxDays;
        var nowUtc = DateTime.UtcNow;

        foreach (var ib in bases)
        {
            try
            {
                if (ib is null || ib.Connection.Type != ConnectionType.File)
                    continue;
                if (_sizeHistory.HasSnapshot(ib.Id, nowUtc))
                    continue; // Снимок за сегодня уже есть.
                var size = InfobaseMaintenanceService.CalculateFileBaseSize(ib);
                var entry = SizeSnapshotCollector.Build(ib, size, nowUtc, scenarios, settings);
                _sizeHistory.AddDailySnapshot(entry, nowUtc, maxDays);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Снимок размеров «{ib?.Name}» (расписание) не сохранён: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Проверка прогноза «диск под копиями закончится через N дней»: по снимкам
    /// каталогов копий (объём — рост, свободное место — последний замер) для каждого
    /// диска. Возвращает тексты, которые пора отправить (подавление повторов — в
    /// общей <see cref="_lastSent"/> через ключи «diskforecast:{диск}» в цикле отправки).
    /// </summary>
    private IReadOnlyList<(string Key, string Text)> CheckDiskForecastAsync(
        IReadOnlyList<Infobase> bases, int thresholdDays, int repeatDays)
    {
        var result = new List<(string, string)>();
        var allSnapshots = _sizeHistory!.GetAll();
        if (allSnapshots.Count < 2)
            return result;

        // Снимки по дискам каталогов копий (объём копий + свободное место на диске).
        var byDrive = allSnapshots
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.CopiesDrive)
                        && s.CopiesBytes.HasValue && s.CopiesBytes > 0
                        && s.CopiesDiskFreeBytes.HasValue)
            .GroupBy(s => s.CopiesDrive!)
            .ToList();

        var nowUtc = DateTime.UtcNow;
        foreach (var group in byDrive)
        {
            var drive = group.Key;
            var ordered = group.OrderBy(s => s.DateUtc).ToList();
            var latestFree = ordered.Last().CopiesDiskFreeBytes;
            if (latestFree is null or <= 0)
                continue;
            var forecast = DiskForecastService.ForecastDaysUntilFull(
                ordered, latestFree, s => s.CopiesBytes, nowUtc);
            if (!forecast.Valid)
                continue;

            var texts = DiskForecastService.CheckThreshold(
                forecast, drive, thresholdDays, DateTime.Now,
                _lastSent, repeatDays);
            foreach (var text in texts)
                result.Add((DiskForecastService.DiskForecastAlertKey(drive), text));
        }
        return result;
    }
}
