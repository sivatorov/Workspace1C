using System.Collections.ObjectModel;
using System.ComponentModel;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка дашборда «Центр обслуживания» (0.3.9.89): сводка состояния одной базы
/// по колонкам «Доступность / последняя копия / размер ИБ / кэш / конфигурация /
/// возраст данных / проверка обновлений». Чистый .NET без платформенных
/// зависимостей — подключается в Linux-сборку явно.
/// </summary>
public sealed class MaintenanceCenterRowViewModel : ViewModelBase, IDisposable
{
    /// <summary>Возраст резервной копии, после которого база попадает в «Только проблемы».</summary>
    public static readonly TimeSpan BackupMaxAge = TimeSpan.FromDays(7);

    /// <summary>Размер кэша 1С, после которого база попадает в «Только проблемы» (1 ГБ).</summary>
    public const long CacheProblemBytes = 1024L * 1024 * 1024;

    private readonly Infobase _infobase;
    private readonly int _freeSpaceWarningGb;
    private DateTime? _lastCheckedAt;

    /// <param name="infobase">Информационная база.</param>
    /// <param name="updateResult">Последний результат проверки обновлений конфигурации
    /// (из UpdateCheckCache по UpdateConfigCode; null — проверка не выполнялась).</param>
    /// <param name="freeSpaceWarningGb">Порог предупреждения «Свободно на диске» в ГБ
    /// (0 — не предупреждать). По умолчанию — 10 ГБ (0.3.9.96).</param>
    public MaintenanceCenterRowViewModel(
        Infobase infobase,
        ConfigUpdateCheckResult? updateResult,
        int freeSpaceWarningGb = DiskFreeSpaceHelper.DefaultWarningGb)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _freeSpaceWarningGb = Math.Max(0, freeSpaceWarningGb);
        UpdateResult = updateResult;
        _infobase.PropertyChanged += OnInfobasePropertyChanged;
        RefreshFromInfobase();
    }

    /// <summary>База, для которой построена строка.</summary>
    public Infobase Infobase => _infobase;

    /// <summary>Результат последней проверки обновлений конфигурации (по UpdateConfigCode).</summary>
    public ConfigUpdateCheckResult? UpdateResult { get; }

    /// <summary>Имя базы.</summary>
    public string Name => _infobase.Name;

    /// <summary>Пояснение под именем: группа и сервер/база.</summary>
    public string Subtitle { get; private set; } = string.Empty;

    /// <summary>Доступность базы (расчётная либо результат ручной проверки).</summary>
    public bool IsAvailable => _infobase.IsAvailable;

    /// <summary>Колонка «Доступность»: статус и время последней проверки (если была).</summary>
    public string AvailabilityText { get; private set; } = string.Empty;

    /// <summary>Колонка «Последняя копия» (значение из 0.3.9.86).</summary>
    public string LastBackupText { get; private set; } = string.Empty;

    /// <summary>Колонка «Размер ИБ».</summary>
    public string SizeText { get; private set; } = string.Empty;

    /// <summary>Колонка «Кэш» (обновляется фоновым RefreshCacheSizeAsync при открытии окна).</summary>
    public string CacheText { get; private set; } = string.Empty;

    /// <summary>Колонка «Конфигурация / версия».</summary>
    public string ConfigurationText { get; private set; } = string.Empty;

    /// <summary>Колонка «Возраст данных» (FileLastWriteTimeUtc + возраст в днях).</summary>
    public string DataAgeText { get; private set; } = string.Empty;

    /// <summary>Колонка «Обновление» — последний результат проверки из UpdateCheckCache.</summary>
    public string UpdateText { get; private set; } = string.Empty;

    /// <summary>Колонка «Свободно на диске» (0.3.9.96): «C:\ — 23,4 ГБ (12%)» для
    /// файловых баз, «—» для клиент-серверных/веб-баз и недоступных дисков.</summary>
    public string FreeSpaceDisplay { get; private set; } = string.Empty;

    /// <summary>True — свободное место на диске файловой базы ниже порога
    /// MaintenanceFreeSpaceWarningGb (0 — предупреждение отключено).</summary>
    public bool FreeSpaceIsProblem { get; private set; }

    /// <summary>True — база попадает под фильтр «Только проблемы».</summary>
    public bool HasProblem { get; private set; }

    /// <inheritdoc />
    public void Dispose() => _infobase.PropertyChanged -= OnInfobasePropertyChanged;

    private void OnInfobasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ручная проверка доступности меняет IsAvailable (SetCheckedAvailability) —
        // фиксируем время последней проверки для колонки «Доступность».
        if (e.PropertyName == nameof(Infobase.IsAvailable))
            _lastCheckedAt = DateTime.Now;
        RefreshFromInfobase();
    }

    private void RefreshFromInfobase()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_infobase.Group))
            parts.Add(_infobase.Group);
        var server = _infobase.ServerDatabaseDisplay;
        if (!string.IsNullOrWhiteSpace(server) && server != "—")
            parts.Add(server);
        Subtitle = string.Join("  •  ", parts);

        var available = LocalizationManager.T(
            _infobase.IsAvailable ? "Maintenance.Available" : "Maintenance.Unavailable");
        AvailabilityText = _lastCheckedAt.HasValue
            ? $"{available} · {_lastCheckedAt.Value:HH:mm:ss}"
            : available;

        LastBackupText = _infobase.LastBackupDisplay;
        SizeText = _infobase.FileSizeDisplay;
        CacheText = _infobase.CacheSizeDisplay;

        var config = (_infobase.ConfigurationName ?? string.Empty).Trim();
        var version = (_infobase.ConfigurationVersion ?? string.Empty).Trim();
        ConfigurationText = config.Length == 0 && version.Length == 0
            ? "—"
            : string.Join(" ", new[] { config, version }.Where(s => s.Length > 0));

        DataAgeText = FormatDataAge(_infobase.FileLastWriteTimeUtc);
        UpdateText = FormatUpdate(UpdateResult);

        // Свободное место на диске (0.3.9.96): только для файловых баз с путём.
        // Ошибки резолвера (недоступный сетевой диск, исключения DriveInfo) —
        // тихая деградация в «—».
        if (_infobase.Connection.Type == ConnectionType.File
            && !string.IsNullOrWhiteSpace(_infobase.Connection.FilePath))
        {
            var driveName = DiskFreeSpaceHelper.ResolveDriveName(_infobase.Connection.FilePath);
            var info = DiskFreeSpaceHelper.TryGetInfo(
                _infobase.Connection.FilePath, DiskFreeSpaceHelper.DefaultDriveResolver);
            FreeSpaceDisplay = driveName is null || info is null
                ? "—"
                : DiskFreeSpaceHelper.FormatDisplay(driveName, info);
            FreeSpaceIsProblem = info is not null
                && DiskFreeSpaceHelper.IsWarning(info.FreeBytes, _freeSpaceWarningGb);
        }
        else
        {
            FreeSpaceDisplay = "—";
            FreeSpaceIsProblem = false;
        }

        var backupTooOld = !_infobase.LastBackupUtc.HasValue
            || DateTime.UtcNow - _infobase.LastBackupUtc.Value > BackupMaxAge;
        var cacheTooBig = _infobase.CacheSizeBytes > CacheProblemBytes;
        HasProblem = !IsAvailable || backupTooOld || cacheTooBig
            || FreeSpaceIsProblem || UpdateResult?.HasNewer == true;

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(LastBackupText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CacheText));
        OnPropertyChanged(nameof(ConfigurationText));
        OnPropertyChanged(nameof(DataAgeText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(FreeSpaceDisplay));
        OnPropertyChanged(nameof(FreeSpaceIsProblem));
        OnPropertyChanged(nameof(HasProblem));
    }

    private static string FormatDataAge(DateTime? lastWriteUtc)
    {
        if (!lastWriteUtc.HasValue)
            return "—";
        var local = lastWriteUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        var age = (int)Math.Round((DateTime.UtcNow - lastWriteUtc.Value).TotalDays);
        return age <= 0
            ? $"{local} · {LocalizationManager.T("Maintenance.Today")}"
            : $"{local} · {age} {LocalizationManager.T("Maintenance.DaysAgo")}";
    }

    private static string FormatUpdate(ConfigUpdateCheckResult? result)
    {
        if (result is null)
            return "—";
        return result.Status switch
        {
            ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
            ConfigUpdateStatus.NewerAvailable => string.IsNullOrWhiteSpace(result.LatestVersion)
                ? LocalizationManager.T("Updates.NewerAvailable")
                : $"{LocalizationManager.T("Updates.NewerAvailable")} · {result.LatestVersion}",
            ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
            ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
            _ => "—"
        };
    }
}

/// <summary>
/// Строка списка «Топ баз по росту» в секции «История размеров» (п.7, 1.0.0.89):
/// выбор базы показывает график роста её размера ИБ.
/// </summary>
public sealed class MaintenanceHistoryRow
{
    /// <summary>База, для которой построена строка.</summary>
    public required Infobase Infobase { get; init; }

    /// <summary>Имя базы.</summary>
    public required string Name { get; init; }

    /// <summary>Текст темпа роста: «+0,3 ГБ в день» или «≈ стабильна».</summary>
    public required string GrowthText { get; init; }

    /// <summary>Последний известный размер ИБ.</summary>
    public required string CurrentSizeText { get; init; }
}

/// <summary>
/// ViewModel окна «Центр обслуживания» (0.3.9.89): таблица всех баз со сводкой
/// состояния (доступность, последняя копия, размер, кэш, конфигурация, возраст
/// данных, проверка обновлений), фильтр-переключатель «Только проблемы» и кнопки
/// «Проверить доступность» / «Открыть базу». Чистый .NET, обе платформы
/// (WPF и Avalonia). Окна только вызывают методы и привязываются к коллекциям —
/// вся логика здесь.
/// </summary>
public sealed class MaintenanceCenterViewModel : ViewModelBase
{
    private readonly Action _checkAvailability;
    private readonly Action<Infobase> _openBase;
    private bool _onlyProblems;

    /// <param name="infobases">Все базы списка.</param>
    /// <param name="checkAvailability">Запуск общей проверки доступности (команда главного окна).</param>
    /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
    /// <param name="freeSpaceWarningGb">Порог предупреждения «Свободно на диске» в ГБ
    /// (0 — не предупреждать), по умолчанию 10 ГБ (0.3.9.96).</param>
    public MaintenanceCenterViewModel(
        IEnumerable<Infobase> infobases,
        Action checkAvailability,
        Action<Infobase> openBase,
        int freeSpaceWarningGb = DiskFreeSpaceHelper.DefaultWarningGb)
        : this(infobases, checkAvailability, openBase, freeSpaceWarningGb, null)
    {
    }

    internal MaintenanceCenterViewModel(
        IEnumerable<Infobase> infobases,
        Action checkAvailability,
        Action<Infobase> openBase,
        int freeSpaceWarningGb,
        SizeHistoryStore? historyStore)
    {
        _checkAvailability = checkAvailability;
        _openBase = openBase;
        _historyStore = historyStore;

        var cache = LoadUpdateCache();
        foreach (var ib in infobases)
        {
            if (ib is null)
                continue;
            var row = new MaintenanceCenterRowViewModel(
                ib, ResolveUpdateResult(ib, cache), freeSpaceWarningGb);
            AllRows.Add(row);
            // Размер кэша вычисляется асинхронно при открытии окна (0.3.9.89).
            ib.RefreshCacheSizeAsync();
        }
        ApplyFilter();

        // Секция «История размеров» (п.7, 1.0.0.89): топ баз по росту за выбранный
        // период и прогноз диска под копиями.
        RefreshHistory();
    }

    /// <summary>Все строки (до применения фильтра).</summary>
    public ObservableCollection<MaintenanceCenterRowViewModel> AllRows { get; } = new();

    /// <summary>Строки, видимые в таблице (с учётом фильтра «Только проблемы»).</summary>
    public ObservableCollection<MaintenanceCenterRowViewModel> Rows { get; } = new();

    /// <summary>Фильтр-переключатель «Только проблемы».</summary>
    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            if (SetProperty(ref _onlyProblems, value))
                ApplyFilter();
        }
    }

    /// <summary>Итоговая строка: сколько баз с проблемами из общего числа.</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Есть ли хоть одна база в списке.</summary>
    public bool HasBases => AllRows.Count > 0;

    /// <summary>«Проверить доступность» — запускает общую проверку в главном окне.</summary>
    public void CheckAvailability() => _checkAvailability();

    /// <summary>«Открыть базу» — переход к строке в главном окне (FindInList-механика).</summary>
    public void OpenBase(MaintenanceCenterRowViewModel row)
    {
        if (row is not null)
            _openBase(row.Infobase);
    }

    // ---- Секция «История размеров и прогноз диска» (п.7, 1.0.0.89) ----

    private const int MaxHistoryTopRows = 5;

    /// <summary>Хранилище дневных снимков размеров (лениво, через DI; изоляция — в тестах).</summary>
    private SizeHistoryStore? _historyStore;

    /// <summary>Идентификаторы всех баз списка (для быстрого поиска при выборе строки).</summary>
    private readonly Dictionary<string, Infobase> _infobaseById = new(StringComparer.Ordinal);

    /// <summary>Период графика «История размеров», дней (7/30/90).</summary>
    public int HistoryPeriodDays { get; private set; } = 30;

    /// <summary>Топ баз по росту за выбранный период.</summary>
    public ObservableCollection<MaintenanceHistoryRow> HistoryRows { get; } = new();

    private MaintenanceHistoryRow? _selectedHistoryRow;

    /// <summary>Выбранная база в «Топе баз по росту» (показывает график).</summary>
    public MaintenanceHistoryRow? SelectedHistoryRow
    {
        get => _selectedHistoryRow;
        set
        {
            if (SetProperty(ref _selectedHistoryRow, value))
                RefreshHistoryChart();
        }
    }

    /// <summary>Нормализованные точки графика роста выбранной базы (рисование — в окнах).</summary>
    public SizeChartData? HistoryChart { get; private set; }

    /// <summary>Заголовок графика: имя базы + период.</summary>
    public string HistoryChartCaption { get; private set; } = string.Empty;

    /// <summary>Сообщение секции графика, когда данных нет.</summary>
    public string HistoryEmptyText { get; private set; } = string.Empty;

    /// <summary>Текст прогноза роста выбранной базы («+0,3 ГБ в день»).</summary>
    public string ForecastText { get; private set; } = string.Empty;

    /// <summary>Текст прогноза «Диск под копиями закончится примерно через N дней».</summary>
    public string DiskForecastText { get; private set; } = string.Empty;

    /// <summary>Выбор периода истории (7/30/90 дней) и пересчёт секции.</summary>
    public void SetHistoryPeriod(int days)
    {
        if (days is not 7 and not 30 and not 90)
            days = 30;
        if (HistoryPeriodDays == days)
            return;
        HistoryPeriodDays = days;
        OnPropertyChanged(nameof(HistoryPeriodDays));
        RefreshHistory();
    }

    /// <summary>Выбор базы из «Топа» (также по одиночному клику строки таблицы).</summary>
    public void SelectHistory(MaintenanceHistoryRow row)
    {
        if (row is not null)
            SelectedHistoryRow = row;
    }

    /// <summary>Выбор базы по идентификатору (выделение строки в таблице → график).</summary>
    public void SelectHistoryById(string? infobaseId)
    {
        if (string.IsNullOrWhiteSpace(infobaseId))
            return;
        var row = HistoryRows.FirstOrDefault(r =>
            string.Equals(r.Infobase.Id, infobaseId, StringComparison.Ordinal));
        if (row is not null)
            SelectedHistoryRow = row;
    }

    private SizeHistoryStore HistoryStore => _historyStore
        ??= AppServices.TryGetService<SizeHistoryStore>() ?? new SizeHistoryStore();

    /// <summary>
    /// Пересчитывает «Топ баз по росту» за выбранный период и прогноз диска под копиями
    /// (по всем снимкам стабильных дисков копий). Вызывается при открытии окна и при
    /// смене периода.
    /// </summary>
    private void RefreshHistory()
    {
        _infobaseById.Clear();
        foreach (var row in AllRows)
        {
            if (row.Infobase is not null)
                _infobaseById[row.Infobase.Id] = row.Infobase;
        }

        IReadOnlyList<SizeSnapshotEntry> all;
        try
        {
            all = HistoryStore.GetAll();
        }
        catch
        {
            all = Array.Empty<SizeSnapshotEntry>();
        }

        var from = SizeHistoryStore.PeriodFromUtc(DateTime.UtcNow, HistoryPeriodDays);

        // Темп роста размера ИБ по снимкам за период (последние N ≥ 2).
        var candidates = new List<(MaintenanceHistoryRow Row, double Growth)>();
        foreach (var group in all.Where(s => !string.IsNullOrWhiteSpace(s.InfobaseId))
                     .GroupBy(s => s.InfobaseId!))
        {
            var fullHistory = group.OrderBy(s => s.DateUtc).ToList();
            var lastId = fullHistory[fullHistory.Count - 1].InfobaseId;
            if (!_infobaseById.TryGetValue(lastId, out var ib) || ib is null)
                continue;
            var period = fullHistory.Where(s => s.DateUtc >= from).ToList();
            if (period.Count < 2)
                continue;
            var growth = DiskForecastService.EstimateGrowthRate(period, s => s.SizeBytes);
            if (growth is null)
                continue;
            var last = period[period.Count - 1].SizeBytes;
            candidates.Add((new MaintenanceHistoryRow
            {
                Infobase = ib,
                Name = ib.Name,
                GrowthText = "+" + DiskForecastService.FormatBytes(growth.Value) + " / " +
                    LocalizationManager.T("Maintenance.HistoryPerDay"),
                CurrentSizeText = last.HasValue
                    ? Services.DiskFreeSpaceHelper.FormatBytes(last.Value)
                    : "—"
            }, growth.Value));
        }

        // Топ по темпу роста (базы без истории не попадают в список).
        candidates.Sort((a, b) => b.Growth.CompareTo(a.Growth));
        HistoryRows.Clear();
        foreach (var item in candidates.Take(MaxHistoryTopRows))
            HistoryRows.Add(item.Row);

        // Прогноз диска под копиями: общий по снимкам каталогов копий.
        DiskForecastText = BuildDiskForecastText(all);

        if (HistoryRows.Count == 0)
        {
            SelectedHistoryRow = null;
            HistoryChart = null;
            HistoryChartCaption = string.Empty;
            ForecastText = string.Empty;
            HistoryEmptyText = LocalizationManager.T("Maintenance.HistoryEmpty");
        }
        else if (SelectedHistoryRow is null || !HistoryRows.Contains(SelectedHistoryRow))
        {
            SelectedHistoryRow = HistoryRows[0]; // триггерит RefreshHistoryChart
        }
        else
        {
            RefreshHistoryChart();
        }
        OnPropertyChanged(nameof(HistoryRows));
        OnPropertyChanged(nameof(HistoryChart));
        OnPropertyChanged(nameof(HistoryChartCaption));
        OnPropertyChanged(nameof(HistoryEmptyText));
        OnPropertyChanged(nameof(ForecastText));
        OnPropertyChanged(nameof(DiskForecastText));
    }

    /// <summary>Пересчитывает график и прогноз роста выбранной базы.</summary>
    private void RefreshHistoryChart()
    {
        var row = SelectedHistoryRow;
        if (row is null)
        {
            HistoryChart = null;
            HistoryChartCaption = string.Empty;
            ForecastText = string.Empty;
            HistoryEmptyText = LocalizationManager.T("Maintenance.HistoryEmpty");
            return;
        }

        var from = SizeHistoryStore.PeriodFromUtc(DateTime.UtcNow, HistoryPeriodDays);
        IReadOnlyList<SizeSnapshotEntry> history;
        try
        {
            history = HistoryStore.GetHistory(row.Infobase.Id, from);
        }
        catch
        {
            history = Array.Empty<SizeSnapshotEntry>();
        }

        HistoryChart = SizeHistoryChartModel.Build(history);
        HistoryChartCaption = string.Format(
            LocalizationManager.T("Maintenance.HistoryCaptionFormat"),
            row.Name, HistoryPeriodDays);
        HistoryEmptyText = HistoryChart is null && history.Count < 2
            ? LocalizationManager.T("Maintenance.HistoryEmpty")
            : string.Empty;

        var growth = DiskForecastService.EstimateGrowthRate(history, s => s.SizeBytes);
        var last = history.Count > 0 ? history[history.Count - 1].SizeBytes : null;
        ForecastText = growth.HasValue
            ? string.Format(LocalizationManager.T("Maintenance.ForecastGrowthFormat"),
                "+" + DiskForecastService.FormatBytes(growth.Value) + " " +
                LocalizationManager.T("Maintenance.HistoryPerDay"),
                last.HasValue ? Services.DiskFreeSpaceHelper.FormatBytes(last.Value) : "—")
            : LocalizationManager.T("Maintenance.ForecastNoData");

        OnPropertyChanged(nameof(HistoryChart));
        OnPropertyChanged(nameof(HistoryChartCaption));
        OnPropertyChanged(nameof(HistoryEmptyText));
        OnPropertyChanged(nameof(ForecastText));
    }

    /// <summary>
    /// Строит текст прогноза «Диск под копиями закончится примерно через N дней» по
    /// снимкам каталогов копий: рост объёма копий на каждом диске против свободного
    /// места последнего снимка. Чистая логика — <see cref="DiskForecastService"/>.
    /// </summary>
    internal static string BuildDiskForecastText(IReadOnlyList<SizeSnapshotEntry> all)
    {
        if (all is null || all.Count < 2)
            return Workspace1C.Localization.LocalizationManager.T("Maintenance.ForecastNoData");

        var byDrive = all
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.CopiesDrive)
                        && s.CopiesBytes.HasValue && s.CopiesBytes > 0
                        && s.CopiesDiskFreeBytes.HasValue)
            .GroupBy(s => s.CopiesDrive!)
            .ToList();
        if (byDrive.Count == 0)
            return Workspace1C.Localization.LocalizationManager.T("Maintenance.ForecastNoData");

        var parts = new List<string>();
        foreach (var group in byDrive)
        {
            var ordered = group.OrderBy(s => s.DateUtc).ToList();
            var latest = ordered[ordered.Count - 1];
            var forecast = DiskForecastService.ForecastDaysUntilFull(
                ordered, latest.CopiesDiskFreeBytes, s => s.CopiesBytes, DateTime.UtcNow);
            if (!forecast.Valid)
            {
                parts.Add(string.Format(
                    Workspace1C.Localization.LocalizationManager.T("Maintenance.ForecastStableFormat"),
                    group.Key));
                continue;
            }
            var days = (int)Math.Ceiling(forecast.DaysUntilFull);
            var date = forecast.DateWhenFull!.Value.ToLocalTime().ToString("dd.MM.yyyy");
            parts.Add(string.Format(
                Workspace1C.Localization.LocalizationManager.T("Maintenance.ForecastDaysFormat"),
                group.Key, days, forecast.DaysMin, forecast.DaysMax, date));
        }
        return string.Join(Environment.NewLine, parts);
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in AllRows)
        {
            if (!_onlyProblems || row.HasProblem)
                Rows.Add(row);
        }

        SummaryText = string.Format(
            LocalizationManager.T("Maintenance.ProblemsCountFormat"),
            AllRows.Count(r => r.HasProblem),
            AllRows.Count);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasBases));
    }

    /// <summary>
    /// Читает кэш проверок обновлений конфигураций из настроек приложения.
    /// Публичный для переиспользования HTML-отчётом по базам (0.3.9.131) — тот же
    /// источник «без пересчёта», что и окно «Центр обслуживания».
    /// </summary>
    public static IReadOnlyDictionary<string, ConfigUpdateCheckResult> LoadUpdateCache()
    {
        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            return settings.UpdateCheckCache
                ?? new Dictionary<string, ConfigUpdateCheckResult>();
        }
        catch
        {
            // Настройки не читаются — считаем, что кэша нет (окно остаётся рабочим).
            return new Dictionary<string, ConfigUpdateCheckResult>();
        }
    }

    /// <summary>
    /// Находит последний результат проверки обновлений по коду связи конфигурации.
    /// Публичный для переиспользования HTML-отчётом по базам (0.3.9.131).
    /// </summary>
    public static ConfigUpdateCheckResult? ResolveUpdateResult(
        Infobase ib, IReadOnlyDictionary<string, ConfigUpdateCheckResult> cache)
    {
        var code = ib.UpdateConfigCode;
        if (string.IsNullOrWhiteSpace(code))
            return null;
        return cache.TryGetValue(code, out var result) ? result : null;
    }
}
