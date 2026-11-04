using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Опции подготовки базы к массовой операции (issue #92, v1.0.1.21, пожелание
/// aepkugesi): завершение сеансов пользователей, блокировка начала сеансов
/// (с кодом разрешения) и блокировка регламентных заданий. Код разрешения —
/// секретоподобное значение: в настройках приложения НЕ сохраняется.
/// </summary>
public sealed class BatchOpsPreparationOptions
{
    /// <summary>Завершить сеансы пользователей (rac «session terminate» по всем сеансам ИБ).</summary>
    public bool TerminateSessions { get; set; }

    /// <summary>Установить блокировку начала сеансов (rac «infobase update --sessions-deny=on»).</summary>
    public bool DenyNewSessions { get; set; }

    /// <summary>Код разрешения для входа при блокировке (rac «--permission-code»); null/пусто — не задан.</summary>
    public string? PermissionCode { get; set; }

    /// <summary>Блокировать регламентные задания (rac «infobase update --scheduled-jobs-deny=on»).</summary>
    public bool DenyScheduledJobs { get; set; }

    /// <summary>
    /// Движок операции несовместим с блокировкой начала сеансов (issue #92, v1.0.1.30):
    /// движок «Конфигуратор» обязан ВОЙТИ в базу, а /DumpIB при --sessions-deny=on
    /// падает «Начало сеанса с информационной базой запрещено». true — блокировка
    /// начала сеансов не применяется (с пояснением в журнале).
    /// </summary>
    public bool SessionDenyIncompatible { get; set; }

    /// <summary>Включена ли хотя бы одна опция подготовки.</summary>
    public bool Any => TerminateSessions || DenyNewSessions || DenyScheduledJobs;
}

/// <summary>
/// Результат подготовки одной базы: состояние для восстановления после операции
/// (<see cref="BatchOpsPreparationService.RestoreAfterAsync"/>) либо причина пропуска.
/// Блокировки снимаются только те, что были установлены этой подготовкой, и только
/// на то состояние, что было «off» до операции: базы, уже заблокированные
/// администратором, не трогаются.
/// </summary>
public sealed class BatchOpsPreparationResult
{
    /// <summary>Подготовка выполнена успешно.</summary>
    public bool Success { get; init; }

    /// <summary>Причина пропуска/ошибки подготовки (локализованный текст); null при успехе.</summary>
    public string? Error { get; init; }

    /// <summary>Идентификатор кластера, в котором найдена ИБ.</summary>
    public Guid ClusterId { get; init; }

    /// <summary>Идентификатор ИБ в кластере.</summary>
    public Guid InfobaseId { get; init; }

    /// <summary>Параметры подключения rac, с которыми выполнялась подготовка.</summary>
    public RacConnectionParams Parameters { get; init; } = new();

    /// <summary>Мы установили блокировку начала сеансов (до операции была off) — снять после операции.</summary>
    public bool SetSessionsDeny { get; init; }

    /// <summary>Мы установили блокировку регламентных заданий (до операции была off) — снять после операции.</summary>
    public bool SetScheduledJobsDeny { get; init; }

    /// <summary>Блокировка регламентных заданий была включена ДО операции (администратором) —
    /// при восстановлении сохраняется как есть (не сбрасывается).</summary>
    public bool WasScheduledJobsDeny { get; init; }

    /// <summary>Для файловой базы создан файл блокировки 1cv8.cdn — удалить после операции.</summary>
    public bool FileLockApplied { get; init; }
}

/// <summary>
/// Подготовка клиент-серверной/файловой базы к массовой операции и восстановление
/// после неё (issue #92, v1.0.1.21, блок «Перед операцией» окна массовых операций):
/// <list type="number">
/// <item>найти ИБ в кластере (расширенный резолв <see cref="ClusterInfobaseCredentialsResolver"/>);</item>
/// <item>завершить сеансы (rac «session terminate»), если включено;</item>
/// <item>установить блокировки (rac «infobase update --sessions-deny=on
/// [--permission-code=…] --scheduled-jobs-deny=on»), если включено и ещё не включено;</item>
/// <item>после операции — «infobase update» обратно (deny off) только для того,
/// что мы включали.</item>
/// </list>
/// Для файловых баз блокировка — файл 1cv8.cdn (<see cref="FileBaseSessionLock"/>) без rac.
/// Ошибки подготовки возвращаются результатом (базу вызывающий код пропускает),
/// исключения не пробрасываются (кроме отмены операции).
/// </summary>
public sealed class BatchOpsPreparationService
{
    private readonly IRacClient _racClient;
    private readonly IReadOnlyList<ManagementServer> _servers;
    private readonly Action<string>? _log;
    private readonly FileBaseSessionLock _fileLock;

    /// <param name="racClient">Клиент rac (инъекция — для юнит-тестов с фейком).</param>
    /// <param name="servers">Справочник «Сервера управления» (как в резолвере).</param>
    /// <param name="log">Приёмник диагностических сообщений (журнал окна); null — без журнала.</param>
    /// <param name="fileLock">Блокировка файловых баз (1cv8.cdn); null — создаётся.</param>
    public BatchOpsPreparationService(IRacClient racClient,
        IReadOnlyList<ManagementServer>? servers = null, Action<string>? log = null,
        FileBaseSessionLock? fileLock = null)
    {
        _racClient = racClient ?? throw new ArgumentNullException(nameof(racClient));
        _servers = servers ?? Array.Empty<ManagementServer>();
        _log = log;
        _fileLock = fileLock ?? new FileBaseSessionLock();
    }

    /// <summary>
    /// Строит сервис с сервисами приложения (rac-клиент и справочник серверов из
    /// <see cref="AppServices"/>). Ошибка построения не бросается — возвращается null
    /// (опции подготовки тогда недоступны, вызывающий код сообщает об этом в журнал).
    /// </summary>
    public static BatchOpsPreparationService? CreateDefault(Action<string>? log = null)
    {
        try
        {
            return new BatchOpsPreparationService(
                AppServices.GetRequiredService<IRacClient>(),
                AppServices.GetRequiredService<IServerListStore>().Load(),
                log);
        }
        catch
        {
            // rac-клиент/справочник недоступны (например, CLI-контекст).
            return null;
        }
    }

    /// <summary>
    /// Выполняет подготовку базы перед операцией. Возвращает null, когда делать
    /// нечего (опции не включены, база не серверная и файловая блокировка не нужна);
    /// результат с <see cref="BatchOpsPreparationResult.Success"/> == false — подготовка
    /// не удалась, базу следует пропустить (причина — в <see cref="BatchOpsPreparationResult.Error"/>).
    /// </summary>
    public async Task<BatchOpsPreparationResult?> ApplyBeforeAsync(
        Infobase infobase, BatchOpsPreparationOptions options, CancellationToken ct = default)
    {
        if (infobase is null || options is null || !options.Any)
            return null;

        // Файловая база: блокировка начала сеансов — файлом 1cv8.cdn (завершать
        // нечего, регламентные задания в файловой базе отключаются иначе).
        if (infobase.Connection?.Type == ConnectionType.File)
        {
            if (!options.DenyNewSessions)
                return null;
            var lockOptions = new SessionLockOptions
            {
                StartTime = DateTime.Now,
                Duration = TimeSpan.FromHours(8),
                Message = LocalizationManager.T("BatchOps.Prepare.FileLockMessage"),
                PermissionCode = options.PermissionCode ?? string.Empty
            };
            var locked = _fileLock.Lock(infobase, lockOptions);
            if (!locked.Success)
                return new BatchOpsPreparationResult
                {
                    Success = false,
                    Error = string.Format(
                        LocalizationManager.T("BatchOps.Prepare.FailedFormat"),
                        locked.ErrorMessage ?? LocalizationManager.T("SessionLock.Failed"))
                };
            _log?.Invoke(LocalizationManager.T("BatchOps.Prepare.FileLock"));
            return new BatchOpsPreparationResult { Success = true, FileLockApplied = true };
        }

        if (infobase.Connection?.Type != ConnectionType.ClientServer)
            return null; // веб-базы не поддерживаются (как и сам ibcmd).

        // Кластер/ИБ + текущие параметры СУБД — одним расширенным резолвом (v1.0.1.21):
        // отдельные rac-запросы не дублируются.
        var resolver = new ClusterInfobaseCredentialsResolver(_racClient, _servers, _log);
        var resolution = await resolver.ResolveDetailedAsync(infobase, ct).ConfigureAwait(false);
        if (resolution.InfobaseId == Guid.Empty || resolution.Profile is null)
        {
            return new BatchOpsPreparationResult
            {
                Success = false,
                Error = string.Format(
                    LocalizationManager.T("BatchOps.Prepare.FailedFormat"),
                    LocalizationManager.T("BatchOps.Prepare.ResolveFailed"))
            };
        }

        var parameters = resolution.Parameters;
        // Issue #92/#79 (v1.0.1.25): учётные данные администратора ИБ из свойств базы
        // (авторизация «Конфигуратора») — без них rac отвечает «Недостаточно прав
        // пользователя на информационную базу» на «infobase info/update» и завершение сеансов.
        var (ibUser, ibPassword) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentials(infobase);
        var clusterId = resolution.ClusterId;
        var infobaseId = resolution.InfobaseId;

        // Завершение сеансов (если включено): по всем сеансам ИБ, с логом количества.
        if (options.TerminateSessions)
        {
            try
            {
                var sessions = await _racClient
                    .GetSessionsAsync(parameters, clusterId, infobaseId, ct)
                    .ConfigureAwait(false);
                var candidates = sessions.Where(s => s.Id != Guid.Empty).ToList();
                var terminated = 0;
                foreach (var session in candidates)
                {
                    if (await _racClient.TerminateSessionAsync(
                            parameters, clusterId, session.Id, processId: null,
                            infobaseUser: ibUser, infobasePassword: ibPassword, ct).ConfigureAwait(false))
                        terminated++;
                }
                _log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.Prepare.TerminatedFormat"), terminated));
                // Issue #92 (v1.0.1.30, логи 7OH): если часть сеансов завершить НЕ удалось
                // (rac 8.5.4 отвергал «session terminate --infobase-user=…»), блокировки
                // НЕ включаются: сеансы остались, монопольная операция всё равно не пройдёт,
                // а база осталась бы заблокированной после ошибки.
                if (candidates.Count > 0 && terminated < candidates.Count)
                {
                    return new BatchOpsPreparationResult
                    {
                        Success = false,
                        Error = string.Format(
                            LocalizationManager.T("BatchOps.Prepare.FailedFormat"),
                            string.Format(
                                LocalizationManager.T("BatchOps.Prepare.TerminateIncompleteFormat"),
                                terminated, candidates.Count))
                    };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new BatchOpsPreparationResult
                {
                    Success = false,
                    Error = string.Format(
                        LocalizationManager.T("BatchOps.Prepare.FailedFormat"),
                        string.Format(
                            LocalizationManager.T("BatchOps.Prepare.TerminateFailedFormat"), ex.Message))
                };
            }
        }

        // Блокировки: текущее состояние догружается «infobase info» — включаем только
        // то, что было off (базы, заблокированные администратором, не трогаем).
        var setSessionsDeny = false;
        var setScheduledJobsDeny = false;
        var jobsDenyNow = false;
        if (options.DenyNewSessions || options.DenyScheduledJobs)
        {
            var sessionsDenyNow = false;
            try
            {
                var info = await _racClient.GetInfobaseInfoAsync(
                    parameters, clusterId, infobaseId,
                    infobaseUser: ibUser, infobasePassword: ibPassword, ct).ConfigureAwait(false);
                sessionsDenyNow = info?.SessionsDeny ?? false;
                jobsDenyNow = info?.ScheduledJobsDeny ?? false;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Информация о текущем состоянии недоступна: считаем, что блокировки off
                // (после операции они будут сняты — безопаснее, чем оставить «навсегда»).
            }

            // Issue #92 (v1.0.1.30): при движке «Конфигуратор» блокировка начала сеансов
            // несовместима с операцией (/DumpIB не может войти в базу) — не применяется,
            // в журнале объяснение. «Завершить сеансы» и «Блокировать регламентные
            // задания» совместимы и работают как раньше.
            if (options.DenyNewSessions && options.SessionDenyIncompatible)
                _log?.Invoke(LocalizationManager.T("BatchOps.Prepare.SessionDenyDesignerLog"));

            setSessionsDeny = options.DenyNewSessions && !sessionsDenyNow && !options.SessionDenyIncompatible;
            setScheduledJobsDeny = options.DenyScheduledJobs && !jobsDenyNow;
            if (setSessionsDeny || setScheduledJobsDeny)
            {
                var request = new RacInfobaseUpdateRequest
                {
                    SessionsDeny = setSessionsDeny ? true : null,
                    ScheduledJobsDeny = setScheduledJobsDeny || jobsDenyNow,
                    InfobaseUser = ibUser ?? string.Empty,
                    InfobasePassword = ibPassword ?? string.Empty,
                    // Код разрешения имеет смысл только при блокировке начала сеансов.
                    PermissionCode = setSessionsDeny && !string.IsNullOrEmpty(options.PermissionCode)
                        ? options.PermissionCode
                        : null
                };
                var updated = await _racClient.UpdateInfobaseAsync(
                    parameters, clusterId, infobaseId, request, ct).ConfigureAwait(false);
                if (!updated)
                    return new BatchOpsPreparationResult
                    {
                        Success = false,
                        Error = string.Format(
                            LocalizationManager.T("BatchOps.Prepare.FailedFormat"),
                            _racClient.LastActionError ?? string.Empty)
                    };
                _log?.Invoke(LocalizationManager.T("BatchOps.Prepare.DenySet"));
            }
        }

        return new BatchOpsPreparationResult
        {
            Success = true,
            ClusterId = clusterId,
            InfobaseId = infobaseId,
            Parameters = parameters,
            SetSessionsDeny = setSessionsDeny,
            SetScheduledJobsDeny = setScheduledJobsDeny,
            WasScheduledJobsDeny = jobsDenyNow
        };
    }

    /// <summary>
    /// Восстановление состояния после операции: снимает только те блокировки,
    /// которые установила подготовка (и которые были off до операции). Ошибки
    /// восстановления возвращаются строкой (в журнал), не бросаются.
    /// </summary>
    public async Task<string?> RestoreAfterAsync(
        Infobase infobase, BatchOpsPreparationResult? undo, CancellationToken ct = default)
    {
        if (infobase is null || undo is null)
            return null;

        if (undo.FileLockApplied)
        {
            var unlocked = _fileLock.Unlock(infobase);
            if (!unlocked.Success)
                return string.Format(
                    LocalizationManager.T("BatchOps.Prepare.RestoreFailedFormat"),
                    unlocked.ErrorMessage ?? LocalizationManager.T("SessionLock.Failed"));
            _log?.Invoke(LocalizationManager.T("BatchOps.Prepare.FileUnlocked"));
            return null;
        }

        if (!undo.SetSessionsDeny && !undo.SetScheduledJobsDeny)
            return null;

        try
        {
            _log?.Invoke(LocalizationManager.T("BatchOps.Prepare.Restoring"));
            var (restoreUser, restorePassword) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentials(infobase);
            var request = new RacInfobaseUpdateRequest
            {
                // Снимаем только то, что включали: sessions-deny=off передаётся, только
                // когда мы её ставили; scheduled-jobs-deny восстанавливается на исходное
                // состояние (блокировка администратора не сбрасывается).
                SessionsDeny = undo.SetSessionsDeny ? false : null,
                ScheduledJobsDeny = undo.SetScheduledJobsDeny ? false : undo.WasScheduledJobsDeny,
                InfobaseUser = restoreUser ?? string.Empty,
                InfobasePassword = restorePassword ?? string.Empty
            };
            var updated = await _racClient.UpdateInfobaseAsync(
                undo.Parameters, undo.ClusterId, undo.InfobaseId, request, ct).ConfigureAwait(false);
            return updated
                ? null
                : string.Format(
                    LocalizationManager.T("BatchOps.Prepare.RestoreFailedFormat"),
                    _racClient.LastActionError ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return string.Format(
                LocalizationManager.T("BatchOps.Prepare.RestoreFailedFormat"), ex.Message);
        }
    }
}
