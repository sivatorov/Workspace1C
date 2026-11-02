using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Статус строки базы в окне массовых операций (issue #91).</summary>
public enum BatchOperationsStatus
{
    /// <summary>Ожидание (база ещё не обработана).</summary>
    Pending,
    /// <summary>Идёт операция по базе.</summary>
    Running,
    /// <summary>Операция выполнена успешно.</summary>
    Success,
    /// <summary>Операция завершилась ошибкой.</summary>
    Failed,
    /// <summary>База пропущена (запущена, веб-база, нет профиля СУБД и т.п.).</summary>
    Skipped
}

/// <summary>
/// Строка окна массовых операций (issue #91): имя базы, флажок выбора, текущий
/// статус (в т.ч. целевой файл .dt), затраченное время. Чистый .NET без
/// платформенных зависимостей — обе платформы (WPF и Avalonia).
/// </summary>
public sealed class BatchOperationsItem : ViewModelBase
{
    private bool _isChecked;
    private bool _isBusy;
    private BatchOperationsStatus _status = BatchOperationsStatus.Pending;
    private string _statusText = string.Empty;
    private string _durationText = string.Empty;

    public BatchOperationsItem(Infobase infobase)
    {
        Infobase = infobase;
        // Базы, которые уже запущены, не выбираются по умолчанию (монитор 0.3.9.82):
        // выгрузка/загрузка по работающей базе может конфликтовать с сеансами.
        _isChecked = !infobase.IsRunning;
    }

    /// <summary>Информационная база.</summary>
    public Infobase Infobase { get; }

    /// <summary>Имя базы.</summary>
    public string Title => Infobase.Name;

    /// <summary>Пояснение: группа и вид подключения.</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Infobase.Group))
                parts.Add(Infobase.Group);
            if (Infobase.IsRunning)
                parts.Add(LocalizationManager.T("BatchOps.BaseRunning"));
            return string.Join("  •  ", parts);
        }
    }

    /// <summary>Отмечена ли база для операции.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Идёт ли по базе операция (блокирует флажок во время прогона).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsSelectable));
        }
    }

    /// <summary>Можно ли менять флажок (не идёт ли операция по базе).</summary>
    public bool IsSelectable => !_isBusy;

    /// <summary>Статус обработки базы.</summary>
    public BatchOperationsStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Человекочитаемый текст статуса.</summary>
    public string StatusText
    {
        get
        {
            if (!string.IsNullOrEmpty(_statusText))
                return _statusText;
            return Status switch
            {
                BatchOperationsStatus.Pending => LocalizationManager.T("BatchOps.StatusPending"),
                BatchOperationsStatus.Running => LocalizationManager.T("BatchOps.StatusRunning"),
                BatchOperationsStatus.Success => LocalizationManager.T("BatchOps.StatusSuccess"),
                BatchOperationsStatus.Failed => LocalizationManager.T("BatchOps.StatusFailed"),
                BatchOperationsStatus.Skipped => LocalizationManager.T("BatchOps.StatusSkipped"),
                _ => string.Empty
            };
        }
    }

    /// <summary>Произвольный текст статуса (причина пропуска, текст ошибки, целевой файл).</summary>
    public void SetStatusText(string text) => SetProperty(ref _statusText, text, nameof(StatusText));

    /// <summary>Затраченное время (для строки статуса).</summary>
    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value);
    }
}

/// <summary>
/// ViewModel окна «Массовые операции по списку баз» (issue #91): выгрузка списка баз
/// в файлы .dt (ibcmd infobase dump) и загрузка конфигурации из .cf по списку баз
/// (ibcmd config load + apply). Чистый .NET, обе платформы; окна только привязываются
/// к коллекциям и вызывают методы. Выполнение — <see cref="IbcmdService"/>; в тестах
/// исполнитель подменяется делегатом <see cref="ExecutorOverride"/> (без процессов).
/// </summary>
public sealed class BatchOperationsViewModel : ViewModelBase
{
    private readonly SynchronizationContext? _syncContext;
    private bool _isRunning;
    private string _summary = string.Empty;
    private string _pathText = string.Empty;
    private string _template = BatchOperationsService.DefaultDumpTemplate;
    private string? _timestampFormat;
    private BatchOperationKind _kind;

    /// <summary>
    /// Подмена исполнителя операции для юнит-тестов (без запуска ibcmd):
    /// (база, целевой путь) → результат. null — реальный исполнитель. Исполнитель
    /// без профиля СУБД: <see cref="IbcmdService"/> сам загрузит сохранённый профиль базы.
    /// </summary>
    internal Func<Infobase, string, CancellationToken, Task<IbcmdResult>>? ExecutorOverride { get; set; }

    /// <summary>
    /// Подмена исполнителя с уже разрешённым профилем СУБД (юнит-тесты автоподстановки,
    /// issue #92): (база, целевой путь, профиль СУБД или null) → результат. Приоритетнее
    /// <see cref="ExecutorOverride"/>; null — используется <see cref="ExecutorOverride"/>
    /// либо реальный исполнитель.
    /// </summary>
    internal Func<Infobase, string, DbmsBackupProfile?, CancellationToken, Task<IbcmdResult>>? ExecutorWithProfileOverride { get; set; }

    /// <summary>
    /// Автоподстановка данных СУБД из кластера (rac) для клиент-серверных баз без
    /// профиля (issue #92, v100114): (база, токен отмены) → профиль или null.
    /// null — автоподстановка выключена (старое поведение: отсутствие профиля —
    /// ошибка пред-валидации). Ошибки резолвера не прерывают прогон.
    /// </summary>
    internal Func<Infobase, CancellationToken, Task<DbmsBackupProfile?>>? DbmsProfileResolver { get; set; }

    /// <summary>
    /// Сервис подготовки «Перед операцией» (issue #92, v1.0.1.21, пожелание aepkugesi):
    /// завершение сеансов, блокировки сеансов/регламентных заданий и восстановление
    /// после операции. null — опции недоступны (rac-клиент не найден); в этом случае
    /// базы с включёнными опциями пропускаются с записью в журнал.
    /// </summary>
    internal BatchOpsPreparationService? PreparationService { get; set; }

    private bool _prepareTerminateSessions;

    /// <summary>Опция «Завершить сеансы пользователей» перед операцией (не сохраняется в настройках автоматически — сохраняет окно).</summary>
    public bool PrepareTerminateSessions
    {
        get => _prepareTerminateSessions;
        set => SetProperty(ref _prepareTerminateSessions, value);
    }

    private bool _prepareDenyNewSessions;

    /// <summary>Опция «Установить блокировку начала сеансов» перед операцией.</summary>
    public bool PrepareDenyNewSessions
    {
        get => _prepareDenyNewSessions;
        set => SetProperty(ref _prepareDenyNewSessions, value);
    }

    private string _preparePermissionCode = string.Empty;

    /// <summary>Код разрешения для входа при блокировке сеансов. Секретоподобное
    /// значение: в настройки приложения не сохраняется (только в памяти сессии).</summary>
    public string PreparePermissionCode
    {
        get => _preparePermissionCode;
        set => SetProperty(ref _preparePermissionCode, value ?? string.Empty);
    }

    private bool _prepareDenyScheduledJobs;

    /// <summary>Опция «Блокировать регламентные задания» перед операцией.</summary>
    public bool PrepareDenyScheduledJobs
    {
        get => _prepareDenyScheduledJobs;
        set => SetProperty(ref _prepareDenyScheduledJobs, value);
    }

    /// <summary>Собирает опции подготовки из состояния чекбоксов окна.</summary>
    private BatchOpsPreparationOptions BuildPreparationOptions() => new()
    {
        TerminateSessions = PrepareTerminateSessions,
        DenyNewSessions = PrepareDenyNewSessions,
        PermissionCode = string.IsNullOrWhiteSpace(PreparePermissionCode) ? null : PreparePermissionCode.Trim(),
        DenyScheduledJobs = PrepareDenyScheduledJobs
    };

    /// <summary>Публичная запись строки в журнал окна (для сообщений сервиса подготовки).</summary>
    public void WriteLog(string line) => AppendLog(line);

    /// <summary>
    /// Вид операции окна (issue #92): выгрузка в .dt, выгрузка конфигурации в .cf
    /// или загрузка .cf. Переключатель DT/CF в окне меняет его на лету
    /// (для выгрузок), поэтому свойство — с публичным сеттером.
    /// </summary>
    public BatchOperationKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
                OnPropertyChanged(nameof(PathLabel));
        }
    }

    public BatchOperationsViewModel(IEnumerable<Infobase> infobases, BatchOperationKind kind)
    {
        _syncContext = SynchronizationContext.Current;
        _kind = kind;
        foreach (var ib in infobases ?? Enumerable.Empty<Infobase>())
        {
            if (ib is not null)
                Items.Add(new BatchOperationsItem(ib));
        }
        ApplyFilter();
    }

    /// <summary>Строки списка (все базы; заведомо неподдерживаемые отсеиваются при запуске).</summary>
    public ObservableCollection<BatchOperationsItem> Items { get; } = new();

    /// <summary>
    /// Строки списка ПОД ФИЛЬТРОМ поиска (issue #92, v1.0.1.24): окно показывает
    /// именно эту коллекцию; прогон (<see cref="RunAsync"/>) по-прежнему идёт по всем
    /// отмеченным строкам <see cref="Items"/> независимо от активного фильтра.
    /// </summary>
    public ObservableCollection<BatchOperationsItem> FilteredItems { get; } = new();

    private string _searchText = string.Empty;

    /// <summary>Текст поиска по списку баз (issue #92, v1.0.1.24). Пусто — показываются все строки.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>
    /// Пересобирает <see cref="FilteredItems"/> по текущему <see cref="SearchText"/>.
    /// Выбор галочек НЕ сбрасывается — фильтр влияет только на видимость строк.
    /// </summary>
    private void ApplyFilter()
    {
        var query = _searchText?.Trim() ?? string.Empty;
        FilteredItems.Clear();
        foreach (var item in Items)
        {
            if (MatchesFilter(item.Title, query))
                FilteredItems.Add(item);
        }
    }

    /// <summary>
    /// Чистая функция фильтра строки по поисковому запросу: подстрока в имени базы
    /// без учёта регистра; пустой/белый запрос матчит всё (issue #92, v1.0.1.24).
    /// </summary>
    internal static bool MatchesFilter(string? itemTitle, string? query)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q))
            return true;
        return itemTitle?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Строки лога прогона (по одной строке на шаг).</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Идёт ли прогон в данный момент (блокирует кнопки окна).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>Итоговая строка по завершении прогона.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Каталог для .dt/.cf (выгрузка) или файл .cf (загрузка) — задаётся в окне.</summary>
    public string PathText
    {
        get => _pathText;
        set => SetProperty(ref _pathText, value);
    }

    /// <summary>Шаблон имени файла выгрузки с токенами {Имя} и {Дата} (issue #92).</summary>
    public string Template
    {
        get => _template;
        set => SetProperty(ref _template, value ?? string.Empty);
    }

    /// <summary>
    /// Формат метки времени для токена <c>{Дата}</c> (issue #92, комментарий 7OH
    /// от 2026-10-31: «формат даты задать можно»): настройка приложения
    /// «Формат даты в имени файла выгрузки», передаётся окном. null/пусто —
    /// встроенный формат сервиса; токен <c>{Дата:формат}</c> перекрывает его.
    /// </summary>
    public string? TimestampFormat
    {
        get => _timestampFormat;
        set => SetProperty(ref _timestampFormat, value);
    }

    /// <summary>Это операция выгрузки (в .dt или .cf) — в окне показывается шаблон имени.</summary>
    public bool IsDumpKind => Kind is BatchOperationKind.DumpDt or BatchOperationKind.DumpCf;

    /// <summary>Заголовок поля пути (каталог выгрузки / файл конфигурации).</summary>
    public string PathLabel => Kind == BatchOperationKind.LoadConfig
        ? LocalizationManager.T("BatchOps.PathLabelLoadCf")
        : LocalizationManager.T("BatchOps.PathLabelDumpDt");

    /// <summary>
    /// Отметить/снять все ВИДИМЫЕ строки (занятые операции не трогаются). С активным
    /// поиском (issue #92, v1.0.1.24) «Выделить все» относится к строкам под фильтром —
    /// так пользователь массово отмечает именно найденные базы; с пустым фильтром
    /// поведение прежнее (все строки).
    /// </summary>
    public void SetAll(bool value)
    {
        foreach (var item in FilteredItems)
        {
            if (!item.Infobase.IsRunning)
                item.IsChecked = value;
        }
    }

    /// <summary>
    /// Последовательно выполняет операцию по всем отмеченным базам (issue #91):
    /// пред-валидация параметров и заведомо неподдерживаемых баз, затем для каждой
    /// базы — ibcmd-операция (выгрузка в свой .dt / загрузка одного .cf), результат
    /// и время — построчно в лог. Отмена — через <paramref name="ct"/> (кнопка «Остановить»).
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Summary = string.Empty;
        LogLines.Clear();
        foreach (var item in Items)
        {
            item.IsBusy = false;
            item.DurationText = string.Empty;
            item.SetStatusText(string.Empty);
            if (item.Status != BatchOperationsStatus.Running)
                item.Status = BatchOperationsStatus.Pending;
        }

        var selected = Items.Where(i => i.IsChecked).ToList();
        if (selected.Count == 0)
        {
            AppendLog(LocalizationManager.T("BatchOps.NothingSelected"));
            IsRunning = false;
            return;
        }

        // Пред-валидация ДО старта (issue #91): ошибки параметров и заведомо
        // неподдерживаемых баз помечаются сразу, без запуска ibcmd. При включённой
        // автоподстановке СУБД из rac (issue #92) отсутствие профиля не блокирует.
        var errors = BatchOperationsService.Validate(
            selected.Select(i => i.Infobase), Kind, PathText,
            allowMissingDbmsProfiles: DbmsProfileResolver is not null);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                AppendLog("✖ " + error);
            Summary = LocalizationManager.T("BatchOps.ValidationFailed");
            IsRunning = false;
            return;
        }

        int ok = 0, failed = 0;
        AppendLog(string.Format(
            LocalizationManager.T("BatchOps.StartedFormat"), selected.Count,
            Kind switch
            {
                BatchOperationKind.DumpDt => LocalizationManager.T("BatchOps.KindDumpDt"),
                BatchOperationKind.DumpCf => LocalizationManager.T("BatchOps.KindDumpCf"),
                _ => LocalizationManager.T("BatchOps.KindLoadCf")
            }));

        // Issue #92 (v1.0.1.21): строка журнала «запущен ibcmd (PID …)» на каждый запуск —
        // «повисло» неотличимо от работы, если не видно стартовавших процессов.
        void OnIbcmdProcessStarted(string exePath, int pid) =>
            AppendLog($"[{DateTime.Now:HH:mm:ss}] " +
                      string.Format(LocalizationManager.T("BatchOps.IbcmdStartedFormat"), pid));

        IbcmdService.ProcessStarted += OnIbcmdProcessStarted;
        try
        {
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            var ib = item.Infobase;
            item.IsBusy = true;
            item.DurationText = string.Empty;
            item.Status = BatchOperationsStatus.Running;

            // Пропуск запущенных баз: операция по работающей базе может конфликтовать.
            if (ib.IsRunning)
            {
                failed++;
                item.Status = BatchOperationsStatus.Skipped;
                item.SetStatusText(LocalizationManager.T("BatchOps.SkipRunning"));
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.SkipRunning")}");
                item.IsBusy = false;
                continue;
            }

            var startedAt = DateTime.Now;

            // ---- Фича «Перед операцией» (issue #92, v1.0.1.21, пожелание aepkugesi):
            // завершение сеансов и блокировки сеансов/регламентных заданий. Ошибка
            // подготовки — база пропускается (Skipped) с записью в журнал; блокировки
            // после операции снимаются в finally ниже (и только те, что были off).
            BatchOpsPreparationResult? prepareUndo = null;
            var prepareOptions = BuildPreparationOptions();
            if (prepareOptions.Any)
            {
                var preparation = PreparationService;
                if (preparation is null)
                {
                    failed++;
                    item.Status = BatchOperationsStatus.Skipped;
                    var unavailable = LocalizationManager.T("BatchOps.Prepare.Unavailable");
                    item.SetStatusText(unavailable);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {unavailable}");
                    item.IsBusy = false;
                    continue;
                }

                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                          LocalizationManager.T("BatchOps.Prepare.Applying"));
                try
                {
                    prepareUndo = await preparation.ApplyBeforeAsync(ib, prepareOptions, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    item.IsBusy = false;
                    throw;
                }
                catch (Exception ex)
                {
                    prepareUndo = new BatchOpsPreparationResult { Success = false, Error = ex.Message };
                }
                // null — подготовка не требовалась (например, файловой базе нечего
                // завершать): операция продолжается штатно (issue #92, v1.0.1.21).
                if (prepareUndo is not null && !prepareUndo.Success)
                {
                    failed++;
                    var reason = prepareUndo.Error ?? LocalizationManager.T("BatchOps.Prepare.ResolveFailed");
                    item.Status = BatchOperationsStatus.Skipped;
                    item.SetStatusText(reason);
                    item.DurationText = FormatElapsed(DateTime.Now - startedAt);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {reason}");
                    item.IsBusy = false;
                    continue;
                }
            }

            // Блокировки (если ставились) снимаются при любом исходе операции —
            // успехе, ошибке или отмене (issue #92, v1.0.1.21).
            try
            {
                // Целевой путь: для выгрузки — свой файл на каждую базу по шаблону
                // имени (issue #92); для загрузки .cf — общий файл.
                var targetPath = Kind switch
                {
                    BatchOperationKind.DumpDt => BatchOperationsService.BuildDumpFilePath(
                        PathText, ib.Name, Kind, Template, startedAt, TimestampFormat),
                    BatchOperationKind.DumpCf => BatchOperationsService.BuildDumpFilePath(
                        PathText, ib.Name, Kind, Template, startedAt, TimestampFormat),
                    _ => PathText.Trim()
                };
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» → {targetPath}");

                // Профиль СУБД для клиент-серверной базы (issue #92, v100114): сохранённый
                // профиль берёт сам <see cref="IbcmdService"/>; если его нет и включена
                // автоподстановка — данные СУБД пробуются из кластера (rac). Ошибка rac
                // не прерывает прогон: операция идёт со старым поведением (ошибку вернёт
                // ibcmd по конкретной базе).
                DbmsBackupProfile? resolvedProfile = null;
                // Issue #92: флаг «автоподстановка реально запускалась и не дала данных» —
                // для fail-fast вместо заведомо провального запуска ibcmd (см. ниже).
                var dbmsResolveFailed = false;
                if (ib.Connection?.Type == ConnectionType.ClientServer && DbmsProfileResolver is not null)
                {
                    var hasSavedProfile = false;
                    try { hasSavedProfile = DbmsBackupService.Load().Find(ib.Id) is not null; }
                    catch { /* профиль недоступен — пробуем автоподстановку */ }
                    if (!hasSavedProfile)
                    {
                        dbmsResolveFailed = true;
                        AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                                  LocalizationManager.T("BatchOps.DbmsProfileResolving"));
                        try
                        {
                            resolvedProfile = await DbmsProfileResolver(ib, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            item.IsBusy = false;
                            throw;
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " + string.Format(
                                LocalizationManager.T("BatchOps.DbmsResolveClustersFailed"), ib.Connection.Server, ex.Message));
                            resolvedProfile = null;
                        }
                        AppendLog(resolvedProfile is not null
                            ? $"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " + string.Format(
                                LocalizationManager.T("BatchOps.DbmsProfileResolved"),
                                $"{resolvedProfile.Dbms}/{resolvedProfile.Host}/{resolvedProfile.Database}" +
                                (string.IsNullOrEmpty(resolvedProfile.User) ? "" : $"/{resolvedProfile.User}"))
                            : $"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " + string.Format(
                                LocalizationManager.T("BatchOps.DbmsResolveNotFound"),
                                ib.Connection.DatabaseName, ib.Connection.Server));
                    }
                }

                // Issue #92: fail-fast. Если база клиент-серверная, сохранённого профиля
                // СУБД нет, а автоподстановка из rac не дала данных — ibcmd запускался
                // «наудачу» и гарантированно падал с малопонятной ошибкой подключения.
                // Вместо траты времени на ibcmd фиксируем статус ошибки сразу с понятным
                // текстом, каким его показал бы <see cref="BatchOperationsService"/>.
                if (dbmsResolveFailed && resolvedProfile is null)
                {
                    failed++;
                    item.Status = BatchOperationsStatus.Failed;
                    var needProfileReason = string.Format(
                        LocalizationManager.T("BatchOps.NeedDbmsProfileFormat"), ib.Name);
                    item.SetStatusText(needProfileReason);
                    item.DurationText = FormatElapsed(DateTime.Now - startedAt);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                              $"{LocalizationManager.T("BatchOps.OperationFailed")} ({item.DurationText}): {needProfileReason}");
                    item.IsBusy = false;
                    // Issue #92 (v1.0.1.21, репорт 7OH «на первой ошибку выдало, на второй
                    // просто повисло»): было `return` — прерывал ВЕСЬ прогон, остальные
                    // базы оставались в ожидании, IsRunning не сбрасывался и окно
                    // блокировалось. Переход к следующей базе, итоговая строка — в finally.
                    continue;
                }

                IbcmdResult result;
                try
                {
                    Func<Infobase, string, DbmsBackupProfile?, CancellationToken, Task<IbcmdResult>> executor;
                    if (ExecutorWithProfileOverride is not null)
                        executor = ExecutorWithProfileOverride;
                    else if (ExecutorOverride is not null)
                        executor = (base_, path, _, token) => ExecutorOverride(base_, path, token);
                    else
                        executor = DefaultExecutorAsync;
                    result = await executor(ib, targetPath, resolvedProfile, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    item.Status = BatchOperationsStatus.Skipped;
                    item.SetStatusText(LocalizationManager.T("BatchOps.Cancelled"));
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.Cancelled")}");
                    item.IsBusy = false;
                    throw;
                }
                catch (Exception ex)
                {
                    result = IbcmdResult.Fail(ex.Message);
                }

                var elapsed = DateTime.Now - startedAt;
                if (result.Success)
                {
                    ok++;
                    item.Status = BatchOperationsStatus.Success;
                    item.SetStatusText(targetPath);
                    item.DurationText = FormatElapsed(elapsed);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.OperationOk")} ({FormatElapsed(elapsed)})");
                }
                else
                {
                    failed++;
                    item.Status = BatchOperationsStatus.Failed;
                    // Issue #92: причина ошибки видна и в журнале окна, а не только
                    // в статусе элемента списка: пользователь не должен гадать,
                    // почему «завершилась ошибкой» без текста.
                    var failureReason = string.IsNullOrWhiteSpace(result.Error)
                        ? result.Output.Trim()
                        : result.Error!.Trim();
                    item.SetStatusText(failureReason);
                    item.DurationText = FormatElapsed(elapsed);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                              $"{LocalizationManager.T("BatchOps.OperationFailed")} ({FormatElapsed(elapsed)})"
                              + (failureReason.Length > 0 ? $": {failureReason}" : string.Empty));
                }
            }
            finally
            {
                if (prepareUndo is not null)
                {
                    try
                    {
                        var restoreError = await PreparationService!.RestoreAfterAsync(ib, prepareUndo, ct).ConfigureAwait(false);
                        if (restoreError is not null)
                            AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {restoreError}");
                    }
                    catch (OperationCanceledException)
                    {
                        // Отмена при восстановлении: блокировка могла остаться — фиксируем.
                        AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                                  LocalizationManager.T("BatchOps.Prepare.RestoreCancelled"));
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " + string.Format(
                            LocalizationManager.T("BatchOps.Prepare.RestoreFailedFormat"), ex.Message));
                    }
                }
                item.IsBusy = false;
            }
        }

        Summary = string.Format(LocalizationManager.T("BatchOps.SummaryFormat"), ok, failed, selected.Count);
        AppendLog("— " + Summary + " —");
        }
        finally
        {
            IbcmdService.ProcessStarted -= OnIbcmdProcessStarted;
            // Issue #92 (v1.0.1.21): IsRunning сбрасывается ВСЕГДА — при отмене и любом
            // исключении окно разблокируется (ранее отмена оставляла кнопки заблокированными).
            IsRunning = false;
        }
    }

    /// <summary>
    /// Реальный исполнитель через ibcmd (обе платформы, без Конфигуратора).
    /// <paramref name="dbmsProfile"/> — профиль СУБД, разрешённый при прогоне
    /// (сохранённый или автоподставленный из rac, issue #92); null — профиль
    /// загрузит сам <see cref="IbcmdService"/>.
    /// </summary>
    private Task<IbcmdResult> DefaultExecutorAsync(Infobase infobase, string targetPath,
        DbmsBackupProfile? dbmsProfile, CancellationToken ct)
        => Kind switch
        {
            BatchOperationKind.DumpDt => IbcmdService.DumpAsync(infobase, targetPath, credential: null, ct,
                dbmsProfileOverride: dbmsProfile),
            // Выгрузка конфигурации в .cf (issue #92): ibcmd config dump.
            BatchOperationKind.DumpCf => IbcmdService.SaveConfigAsync(infobase, targetPath, credential: null, ct,
                dbmsProfileOverride: dbmsProfile),
            _ => IbcmdService.LoadConfigAsync(infobase, targetPath, ct, dbmsProfileOverride: dbmsProfile)
        };

    /// <summary>Добавляет строку в лог потокобезопасно (через захваченный контекст синхронизации).</summary>
    private void AppendLog(string line)
    {
        if (_syncContext is null)
        {
            LogLines.Add(line);
            return;
        }
        _syncContext.Post(_ =>
        {
            LogLines.Add(line);
            OnPropertyChanged(nameof(LogLines));
        }, null);
    }

    /// <summary>Форматирует интервал времени «м:сс» (например «12 сек», «3:05»).</summary>
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
        return $"{Math.Max(1, (int)elapsed.TotalSeconds)} {LocalizationManager.T("BatchOps.SecondsShort")}";
    }
}
