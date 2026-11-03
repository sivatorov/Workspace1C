using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Автоподстановка учётных данных СУБД из кластера (rac) для клиент-серверных баз
/// без профиля СУБД (issue #92, план v1.0.1.14 п.3.1): при массовой выгрузке ibcmd
/// не требуется создавать профиль на каждую базу — поля dbms/db-server/db-name/db-user
/// берутся из вывода rac «infobase summary list» уже найденного кластера.
/// <para>
/// Пароль СУБД rac НЕ возвращает (<see cref="RacInfobaseInfo"/>: «rac не возвращает
/// пароль») — он остаётся пустым: ibcmd подключится, если вход в СУБД разрешён без
/// пароля, иначе операция завершится ошибкой подключения (как и без профиля).
/// Ошибки rac не прерывают массовую операцию: <see cref="ResolveAsync"/> логирует
/// причину и возвращает null — вызывающий код продолжает со старым поведением.
/// Сопоставление базы списка с ИБ кластера — по имени (Ref строки подключения =
/// колонке «name»), по образцу <see cref="ClusterInfobaseCredentials.Find"/>.
/// </para>
/// </summary>
/// <summary>
/// Расширенный результат автоподстановки (issue #92, v1.0.1.21, п.3 фичи блокировок):
/// профиль СУБД плюс идентификаторы кластера/ИБ и параметры подключения — чтобы
/// подготовка массовых операций (<see cref="BatchOpsPreparationService"/>) не делала
/// rac-запросы повторно. <see cref="Profile"/> может быть null (например, тип СУБД
/// не поддерживается ibcmd), а идентификаторы при этом заполнены.
/// </summary>
public sealed record ClusterInfobaseCredentialsResolution
{
    /// <summary>Профиль СУБД для ibcmd (пароль пуст); null — данные не собраны.</summary>
    public DbmsBackupProfile? Profile { get; init; }

    /// <summary>Идентификатор кластера, в котором найдена ИБ.</summary>
    public Guid ClusterId { get; init; }

    /// <summary>Идентификатор ИБ в кластере.</summary>
    public Guid InfobaseId { get; init; }

    /// <summary>Параметры подключения rac, с которыми выполнялся поиск.</summary>
    public RacConnectionParams Parameters { get; init; } = new();
}

public sealed class ClusterInfobaseCredentialsResolver
{
    /// <summary>Общий таймаут резолва одной базы (issue #92, v1.0.1.21): перебор
    /// кластеров не может тянуться дольше минуты — при недоступных серверах несколько
    /// rac-проб подряд по 30 с выглядят как зависание без внятного прогресса.</summary>
    public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(60);

    private readonly IRacClient _racClient;
    private readonly IReadOnlyList<ManagementServer> _servers;
    private readonly Action<string>? _log;

    /// <summary>
    /// Явные учётные данные пользователя ИБ (issue #79, v1.0.1.25): мастер
    /// «Развернуть тестовую копию» передаёт логин/пароль формы, если «авторизация
    /// базы» выключена. null — авторизация «Конфигуратора» из свойств базы.
    /// </summary>
    public BackupCredential? CredentialOverride { get; set; }

    /// <summary>
    /// Тип СУБД «по умолчанию» (issue #79, v1.0.1.25): rac 8.5.x отдаёт пустой dbms в
    /// «infobase summary list»; если и «infobase info» не дал типа, но сервер и имя
    /// SQL-БД известны — берётся этот тип (в мастере — тип СУБД с формы). null — не
    /// подставлять (прежнее поведение).
    /// </summary>
    public string? FallbackDbms { get; set; }

    /// <summary>
    /// Добавлять ли к сообщению о неудаче подсказку «задайте данные СУБД источника
    /// вручную или выберите движок „Конфигуратор"» (issue #79, v1.0.1.29). Включается
    /// вызывающим кодом мастера «Развернуть тестовую копию»; в массовых операциях
    /// подсказка не показывается (там свои механизмы выбора движка/пароля СУБД).
    /// </summary>
    public bool AdviseManualSourceDbms { get; set; }

    /// <summary>Причина последней неудачи резолва (для понятного сообщения в окне).</summary>
    public string? LastFailureReason { get; private set; }

    /// <param name="racClient">Клиент rac (инъекция — для юнит-тестов с фейком).</param>
    /// <param name="servers">Справочник «Сервера управления»: при совпадении хоста базы
    /// с записью справочника берутся её адрес/порт/логин/пароль кластера; иначе —
    /// адрес базы с портом агента по умолчанию (1540).</param>
    /// <param name="log">Приёмник диагностических сообщений (журнал/лог окна); null — без журнала.</param>
    public ClusterInfobaseCredentialsResolver(IRacClient racClient,
        IReadOnlyList<ManagementServer>? servers = null, Action<string>? log = null)
    {
        _racClient = racClient ?? throw new ArgumentNullException(nameof(racClient));
        _servers = servers ?? Array.Empty<ManagementServer>();
        _log = log;
    }

    /// <summary>
    /// Строит резолвер с сервисами приложения (rac-клиент и справочник серверов
    /// из <see cref="AppServices"/>). Сообщения уходят в <paramref name="log"/>.
    /// Ошибка построения (сервисы недоступны) не бросается — возвращается null,
    /// чтобы массовая операция шла со старым поведением.
    /// </summary>
    public static ClusterInfobaseCredentialsResolver? CreateDefault(Action<string>? log = null)
    {
        try
        {
            return new ClusterInfobaseCredentialsResolver(
                AppServices.GetRequiredService<IRacClient>(),
                AppServices.GetRequiredService<IServerListStore>().Load(),
                log);
        }
        catch
        {
            // rac-клиент/справочник недоступны (например, CLI-контекст) — автоподстановка выключена.
            return null;
        }
    }

    /// <summary>
    /// Пробует получить данные СУБД серверной базы из кластера (rac). Возвращает
    /// профиль СУБД для передачи в ibcmd (<c>--dbms/--db-server/--db-name/--db-user</c>,
    /// без пароля) или null, если база не серверная, не найдена в кластере или rac
    /// недоступен (причина — в журнале). Исключения наружу не пробрасываются
    /// (кроме отмены операции <see cref="OperationCanceledException"/>).
    /// </summary>
    public async Task<DbmsBackupProfile?> ResolveAsync(Infobase? infobase, CancellationToken ct = default)
        => (await ResolveDetailedAsync(infobase, ct).ConfigureAwait(false)).Profile;

    /// <summary>
    /// То же, что <see cref="ResolveAsync"/>, но вместе с профилем возвращаются
    /// идентификаторы кластера/ИБ и параметры подключения (v1.0.1.21, для подготовки
    /// массовых операций). Общий таймаут последовательности кластеров —
    /// <see cref="ResolveTimeout"/> (60 с); превышение трактуется как «данные не
    /// получены» (лог + null), отмена операции пользователем — пробрасывается.
    /// </summary>
    public async Task<ClusterInfobaseCredentialsResolution> ResolveDetailedAsync(
        Infobase? infobase, CancellationToken ct = default)
    {
        var connection = infobase?.Connection;
        if (connection?.Type != ConnectionType.ClientServer)
            return new(); // файловые/веб-базы не требуют данных СУБД.

        var infobaseName = connection.DatabaseName?.Trim();
        if (string.IsNullOrWhiteSpace(infobaseName))
        {
            _log?.Invoke(LocalizationManager.T("BatchOps.DbmsResolveNoName"));
            return new();
        }

        // Issue #92 (v1.0.1.25, репорт 7OH «не тот кластер для списка баз взят»): на одном
        // хосте может работать несколько агентов/кластеров (например 8.3.27 на 1541 и
        // 8.5 на 27541) и в справочнике «Сервера управления» — несколько записей с тем же
        // хостом. Раньше бралась ПЕРВАЯ запись по хосту и ВСЕ её кластеры по порядку:
        // одноимённая база чужого кластера подставлялась вместо нужной. Теперь
        // опрашиваются все записи справочника с этим хостом, а кластеры с портом
        // менеджера из строки подключения базы (Srvr="host:port") идут первыми и
        // единственными, если такие нашлись.
        var candidates = BuildConnectionCandidates(connection.Server, _servers);
        var parameters = candidates[0];
        var managerPort = ResolveManagerPort(connection);
        // Issue #92/#79 (v1.0.1.25): «infobase info» требует учётных данных
        // администратора ИБ — берутся из свойств базы (авторизация «Конфигуратора»),
        // иначе rac отвечает «Недостаточно прав пользователя на информационную базу».
        var (ibUser, ibPassword) = InfobaseAdminCredentials(infobase!, CredentialOverride);
        // Issue #79 (v1.0.1.26): источник кред ИБ — в журнал. По ZIP-диагностике
        // сразу видно, нашлись ли креды ИБ и откуда (форма/Конфигуратор/Предприятие).
        var (_, _, ibCredentialsSource) = InfobaseAdminCredentialsWithSource(infobase!, CredentialOverride);
        _log?.Invoke(string.Format(
            LocalizationManager.T("BatchOps.DbmsResolveIbCredentialsSource"), ibCredentialsSource));
        LastFailureReason = null;
        // Issue #92 (v1.0.1.22): причина последней неудачи сборки профиля по НАЙДЕННОЙ
        // базе (пустой dbms в summary + ошибка «infobase info») — для честного итогового
        // сообщения вместо «база не найдена».
        string? profileFailureReason = null;
        try
        {
            // Issue #92 (v1.0.1.21): общий таймаут на резолв одной базы — linked CTS
            // на всю последовательность кластеров; rac-клиенту передаётся этот токен.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResolveTimeout);

            var targets = new List<(RacConnectionParams Parameters, RacCluster Cluster)>();
            Exception? clustersError = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    var found = await _racClient.GetClustersAsync(candidate, timeoutCts.Token).ConfigureAwait(false);
                    targets.AddRange(found.Select(c => (candidate, c)));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (candidates.Count > 1)
                {
                    // Одна из записей справочника недоступна — остальные могут ответить.
                    clustersError = ex;
                    _log?.Invoke(string.Format(
                        LocalizationManager.T("BatchOps.DbmsResolveClustersFailed"),
                        $"{candidate.Address}:{candidate.Port}", ex.Message));
                }
            }
            if (targets.Count == 0 && clustersError is not null)
                throw clustersError;
            targets = OrderClustersByManagerPort(targets, managerPort);

            foreach (var (targetParameters, cluster) in targets)
            {
                parameters = targetParameters;
                IReadOnlyList<RacInfobaseSummary> summaries;
                var probeStopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    summaries = await GetInfobasesWithRetryAsync(parameters, cluster.Id, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log?.Invoke(string.Format(
                        LocalizationManager.T("BatchOps.DbmsResolveClusterListFailed"),
                        cluster.Id, ex.Message));
                    continue; // другой кластер может ответить.
                }
                finally
                {
                    // Длительность пробы логируется только для МЕДЛЕННЫХ кластеров
                    // (>= 1 с): длительные rac-пробы при недоступном сервере выглядят
                    // как зависание (issue #92, v1.0.1.21); при штатной работе журнал
                    // остаётся чистым.
                    probeStopwatch.Stop();
                    if (probeStopwatch.ElapsedMilliseconds >= 1000)
                    {
                        _log?.Invoke(string.Format(
                            LocalizationManager.T("BatchOps.DbmsResolveProbeDurationFormat"),
                            cluster.Id, probeStopwatch.ElapsedMilliseconds));
                    }
                }

                var match = summaries.FirstOrDefault(s =>
                    NormalizedNamesEqual(s.Name, infobaseName));
                if (match is null)
                {
                    // Issue #92: в журнале остаётся только «база не найдена», без
                    // признаков, почему. Показываем фактическое содержимое кластера —
                    // количество и имена ИБ, вернувшиеся из rac: сопоставление идёт
                    // по имени, и по списку видно расхождение имён.
                    _log?.Invoke(BuildClusterContents(cluster.Id, summaries));
                    continue;
                }

                // Issue #92 (v1.0.1.22): сырье для диагностики — фактические свойства
                // найденной базы из rac. ZIP 7OH (diagnostics_20261102_104926) показал:
                // rac 8.5.4 отдаёт пустой dbms в «infobase summary list», а «infobase
                // info» падает «Недостаточно прав пользователя» — по строке журнала с
                // полем dbms сразу видно, на чём сломался резолв.
                _log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveSummaryFieldsFormat"),
                    match.Name?.Trim(), match.Dbms, match.DbServer, match.DbName, match.DbUser));

                var profile = ToProfile(match, infobase!.Id, infobaseName);
                if (profile is not null)
                {
                    return new ClusterInfobaseCredentialsResolution
                    {
                        Profile = profile,
                        ClusterId = cluster.Id,
                        InfobaseId = match.InfobaseId,
                        Parameters = parameters
                    };
                }

                // Issue #92 (v1.0.1.21): rac 8.5.4 в «infobase summary list» отдаёт ПУСТОЙ
                // тип СУБД («тип СУБД «» не поддерживается») — догружаем полные свойства
                // командой «infobase info», где dbms/db-server/db-name присутствуют полнее.
                _log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveUnsupportedDbms"),
                    infobaseName, match.Dbms));
                _log?.Invoke(LocalizationManager.T("BatchOps.DbmsResolveInfoFallback"));
                try
                {
                    var info = await GetInfobaseInfoWithRetryAsync(
                        parameters, cluster.Id, match.InfobaseId,
                        infobaseUser: ibUser, infobasePassword: ibPassword, timeoutCts.Token).ConfigureAwait(false);

                    // Issue #79 (v1.0.1.26): fallback на кластерные креды. Если «infobase info»
                    // с кредами ИБ упал с «Недостаточно прав», а у записи «Сервера управления»
                    // есть логин администратора кластера — повторная проба с ними. На таймауты
                    // и прочие ошибки фолбэк не делается (только явная ошибка прав).
                    if (info is null && HasInsufficientRightsError(_racClient.LastActionError) &&
                        !string.IsNullOrEmpty(parameters.User) &&
                        !string.Equals(parameters.User, ibUser, StringComparison.Ordinal))
                    {
                        _log?.Invoke(LocalizationManager.T("BatchOps.DbmsResolveClusterCredsFallback"));
                        info = await GetInfobaseInfoWithRetryAsync(
                            parameters, cluster.Id, match.InfobaseId,
                            infobaseUser: parameters.User, infobasePassword: parameters.Password,
                            timeoutCts.Token).ConfigureAwait(false);
                    }

                    profile = info is null ? null : ToProfile(info, infobase.Id, infobaseName);
                    if (profile is not null)
                    {
                        _log?.Invoke(string.Format(
                            LocalizationManager.T("BatchOps.DbmsResolveInfoOkFormat"),
                            $"{profile.Dbms}/{profile.Host}/{profile.Database}" +
                            (string.IsNullOrEmpty(profile.User) ? "" : $"/{profile.User}")));
                        return new ClusterInfobaseCredentialsResolution
                        {
                            Profile = profile,
                            ClusterId = cluster.Id,
                            InfobaseId = match.InfobaseId,
                            Parameters = parameters
                        };
                    }

                    // Issue #79 (v1.0.1.25): тип СУБД неизвестен, но сервер и имя SQL-БД есть —
                    // подставляем тип «по умолчанию» (тип СУБД с формы мастера).
                    var fallbackProfile = ToProfileWithFallbackDbms(match, info, FallbackDbms, infobase.Id, infobaseName);
                    if (fallbackProfile is not null)
                    {
                        _log?.Invoke(string.Format(
                            LocalizationManager.T("BatchOps.DbmsResolveFallbackDbmsFormat"),
                            fallbackProfile.Dbms, $"{fallbackProfile.Host}/{fallbackProfile.Database}"));
                        return new ClusterInfobaseCredentialsResolution
                        {
                            Profile = fallbackProfile,
                            ClusterId = cluster.Id,
                            InfobaseId = match.InfobaseId,
                            Parameters = parameters
                        };
                    }

                    // Issue #92 (v1.0.1.22): rac при ошибке «infobase info» (в т.ч.
                    // «Недостаточно прав пользователя на информационную базу», ZIP 7OH)
                    // возвращает null, текст ошибки — в LastActionError. Ранее причина
                    // терялась: в журнале оставалось только «не вернула поддерживаемый
                    // тип СУБД», а итоговое «база не найдена» вводило в заблуждение.
                    profileFailureReason = string.IsNullOrWhiteSpace(_racClient.LastActionError)
                        ? LocalizationManager.T("BatchOps.DbmsResolveInfoFailed")
                        : string.Format(
                            LocalizationManager.T("BatchOps.DbmsResolveInfoFailedFormat"),
                            _racClient.LastActionError);
                    _log?.Invoke(profileFailureReason);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    profileFailureReason = string.Format(
                        LocalizationManager.T("BatchOps.DbmsResolveInfoFailedFormat"), ex.Message);
                    _log?.Invoke(profileFailureReason);
                }
            }

            // Issue #92 (v1.0.1.22): если база НАЙДЕНА, но профиль не собран (пустой dbms
            // в «infobase summary list» + ошибка «infobase info») — честное сообщение
            // с причиной по каждой базе вместо вводящего в заблуждение «не найдена».
            if (profileFailureReason is not null)
            {
                LastFailureReason = profileFailureReason;
                // Issue #79 (v1.0.1.29): когда база найдена, но rac отдал пустые свойства
                // СУБД, а «infobase info» не помог — резолвер прямо советует оба выхода:
                // ручные данные СУБД источника в мастере ИЛИ движок «Конфигуратор»
                // (подсказка включается вызывающим кодом — только из мастера тест-копии).
                if (AdviseManualSourceDbms)
                    LastFailureReason += " " + LocalizationManager.T("TestDeploy.DbmsResolveAdviseManual");
                if (ibUser is null)
                    LastFailureReason += " " + LocalizationManager.T("BatchOps.DbmsResolveNeedIbCredentials");
                // Issue #79 (v1.0.1.26): ошибка прав при отсутствии кластерных кред —
                // подсказка, где их задать, вместо голого rac-текста.
                if (HasInsufficientRightsError(LastFailureReason) && string.IsNullOrEmpty(parameters.User))
                    LastFailureReason += " " + LocalizationManager.T("BatchOps.DbmsResolveNeedClusterCredentials");
                _log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveNoProfileFormat"),
                    infobaseName, parameters.Address, LastFailureReason));
            }
            else
            {
                LastFailureReason = string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveNotFound"),
                    infobaseName, parameters.Address);
                _log?.Invoke(LastFailureReason);
            }
            return new();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Сработал общий таймаут резолва (не отмена операции пользователем) —
            // трактуем как «данные не получены» с записью в журнал (issue #92, v1.0.1.21).
            _log?.Invoke(string.Format(
                LocalizationManager.T("BatchOps.DbmsResolveTimeoutFormat"),
                (int)ResolveTimeout.TotalSeconds));
            return new();
        }
        catch (OperationCanceledException)
        {
            throw; // отмена операции — не ошибка rac.
        }
        catch (Exception ex)
        {
            _log?.Invoke(string.Format(
                LocalizationManager.T("BatchOps.DbmsResolveClustersFailed"),
                parameters.Address, ex.Message));
            return new();
        }
    }

    /// <summary>
    /// «infobase summary list» с увеличенным таймаутом и ОДНОКРАТНЫМ повтором
    /// (issue #79, v1.0.1.28, образец — GetInfobasesWithRetryAsync импорта из
    /// кластера, issue #25): холодный rac / загруженный сервер не укладывается в
    /// 30 с, результат отбрасывался и мастер показывал «база не найдена». Повтор
    /// только для таймаутов/стартовых сбо rac (см. <see cref="IsRetryableRacFailure"/>);
    /// ошибки прав не повторяются — это не таймаут.
    /// </summary>
    private async Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesWithRetryAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken ct)
    {
        try
        {
            return await _racClient.GetInfobasesAsync(
                parameters, clusterId, ct, RacClient.InfobaseInfoTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRetryableRacFailure(ex.Message))
        {
            _log?.Invoke(string.Format(
                LocalizationManager.T("BatchOps.DbmsResolveRacRetry"), ex.Message));
            return await _racClient.GetInfobasesAsync(
                parameters, clusterId, ct, RacClient.InfobaseInfoTimeout).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// «infobase info» с увеличенным таймаутом и ОДНОКРАТНЫМ повтором (issue #79,
    /// v1.0.1.28): rac при сбое возвращает null, текст ошибки — в
    /// <see cref="IRacClient.LastActionError"/>; повторяем только таймауты/стартовые
    /// сбо rac, ошибки прав («Недостаточно прав пользователя») не повторяются.
    /// </summary>
    private async Task<RacInfobaseInfo?> GetInfobaseInfoWithRetryAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        string? infobaseUser, string? infobasePassword, CancellationToken ct)
    {
        var info = await _racClient.GetInfobaseInfoAsync(
            parameters, clusterId, infobaseId,
            infobaseUser: infobaseUser, infobasePassword: infobasePassword, ct,
            RacClient.InfobaseInfoTimeout).ConfigureAwait(false);
        if (info is not null || !IsRetryableRacFailure(_racClient.LastActionError))
            return info;
        _log?.Invoke(string.Format(
            LocalizationManager.T("BatchOps.DbmsResolveRacRetry"), _racClient.LastActionError));
        return await _racClient.GetInfobaseInfoAsync(
            parameters, clusterId, infobaseId,
            infobaseUser: infobaseUser, infobasePassword: infobasePassword, ct,
            RacClient.InfobaseInfoTimeout).ConfigureAwait(false);
    }

    /// <summary>
    /// Повторяем ли сбой rac (issue #79, v1.0.1.28): только таймауты и стартовые
    /// сбо (код -1 без объяснения). Ошибки прав доступа («Недостаточно прав
    /// пользователя») НЕ повторяются — это не таймаут, повтор лишь удвоил бы время.
    /// Чистый helper — покрыт юнит-тестом.
    /// </summary>
    internal static bool IsRetryableRacFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        if (message.Contains("Недостаточно прав", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Insufficient rights", StringComparison.OrdinalIgnoreCase))
            return false;
        return message.Contains("таймаут", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Не удалось выполнить команду rac", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Нормализация имени ИБ для сопоставления (issue #92/#79, v1.0.1.22): Trim,
    /// нижний регистр, удаление пробелов/подчерков/дефисов/табуляций. rac и список
    /// приложения могут именовать одну базу по-разному («Demo 27» vs «demo_27») —
    /// строгое сравнение по Trim+OrdinalIgnoreCase такое расхождение не находило,
    /// и автоподстановка (а за ней и мастер «Развернуть тестовую копию») падала
    /// «база не найдена». Слишком агрессивная нормализация (сравнение без разделителей
    /// вообще) опасна подстановкой чужой базы — убираются только эквивалентные
    /// разделители. Чистый helper — для юнит-теста.
    /// </summary>
    internal static string NormalizeInfobaseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var normalized = name.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch is ' ' or '\t' or '_' or '-')
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>True — имена ИБ совпадают после нормализации (issue #92/#79).</summary>
    internal static bool NormalizedNamesEqual(string? left, string? right) =>
        string.Equals(NormalizeInfobaseName(left), NormalizeInfobaseName(right), StringComparison.Ordinal);

    /// <summary>
    /// Строка диагностики «содержимое кластера» (issue #92): количество и имена
    /// ИБ, вернувшиеся из rac по кластеру. Чистый helper — для юнит-теста и журнала.
    /// </summary>
    internal static string BuildClusterContents(Guid clusterId, IEnumerable<RacInfobaseSummary>? summaries)
    {
        var names = (summaries ?? Enumerable.Empty<RacInfobaseSummary>())
            .Select(s => s.Name?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        return string.Format(
            LocalizationManager.T("BatchOps.DbmsResolveClusterContentsFormat"),
            clusterId, names.Count, string.Join(", ", names));
    }

    /// <summary>
    /// Учётные данные администратора ИБ для rac («--infobase-user/--infobase-pwd»,
    /// issue #92/#79, v1.0.1.25): авторизация «Конфигуратора» из свойств базы с учётом
    /// общей авторизации (тот же резолвер, что у ibcmd). Не заданы — null/null
    /// (rac выполнит команду без них, как раньше).
    /// </summary>
    public static (string? User, string? Password) InfobaseAdminCredentials(Infobase? infobase,
        BackupCredential? credential = null)
    {
        var (user, password, _) = InfobaseAdminCredentialsWithSource(infobase, credential);
        return (user, password);
    }

    /// <summary>
    /// То же, что <see cref="InfobaseAdminCredentials"/>, плюс ИСТОЧНИК кред для
    /// журналирования (issue #79, v1.0.1.26): «form» — поля формы мастера,
    /// «configurator:<база>»/«enterprise:<база>» — авторизация из свойств базы,
    /// «app-auth» — общая авторизация приложения, «none» — кред не нашлось. По строке
    /// журнала сразу видно, почему rac шёл без учётных данных ИБ.
    /// </summary>
    public static (string? User, string? Password, string Source) InfobaseAdminCredentialsWithSource(
        Infobase? infobase, BackupCredential? credential = null)
    {
        if (infobase is null)
            return (null, null, "none");
        try
        {
            // Явные логин/пароль формы мастера — высший приоритет.
            if (credential is { UseInfobaseAuth: false } && !string.IsNullOrWhiteSpace(credential.User))
                return (credential.User.Trim(), credential.Password ?? string.Empty, "form");

            // Тот же источник, что у окна «Серверы 1С» → «Свойства информационной базы»
            // («Логин и пароль администратора ИБ взяты из списка баз»): авторизация
            // «Конфигуратора», иначе «1С:Предприятия»; вход ОС и пустой логин не в счёт.
            var found = ClusterInfobaseCredentials.Find(new[] { infobase },
                infobase.Connection?.Server, infobase.Connection?.DatabaseName);
            if (found is not null)
                return (found.User, found.Password, "configurator:" + (infobase.Name ?? string.Empty));

            // Общая авторизация приложения (как у ibcmd).
            var (user, password) = IbcmdService.ResolveUser(infobase, credential);
            return string.IsNullOrWhiteSpace(user)
                ? (null, null, "none")
                : (user, password ?? string.Empty, "app-auth");
        }
        catch
        {
            return (null, null, "none");
        }
    }

    /// <summary>
    /// Ошибка rac — «не хватает прав» (issue #79, v1.0.1.26): по тексту ошибки решается,
    /// делать ли fallback на кластерные креды. Internal — для юнит-теста.
    /// </summary>
    internal static bool HasInsufficientRightsError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;
        return error.Contains("Недостаточно прав", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Insufficient privileges", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Insufficient rights", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Порт менеджера кластера из строки подключения базы (Srvr="host:port" или
    /// поле Port; по умолчанию 1541). 0 — порт определить не удалось.
    /// Для «srv1:1541,srv2» берётся первый адрес. Чистый helper — для юнит-теста.
    /// </summary>
    internal static int ResolveManagerPort(ConnectionSettings? connection)
    {
        if (connection is null)
            return 0;
        var first = (connection.Server ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .FirstOrDefault() ?? string.Empty;
        var probe = new ConnectionSettings { Port = connection.Port > 0 ? connection.Port : 1541 };
        ConnectionSettings.ParseServerAndPort(first, probe);
        return probe.Port > 0 ? probe.Port : 0;
    }

    /// <summary>
    /// Упорядочивает кластеры для поиска базы (issue #92, v1.0.1.25): если среди них
    /// есть кластеры с портом менеджера <paramref name="managerPort"/> — остаются только
    /// они (одноимённая база соседнего кластера не подставится); иначе — все в
    /// исходном порядке (прежнее поведение). Чистый helper — для юнит-теста.
    /// </summary>
    internal static List<(RacConnectionParams Parameters, RacCluster Cluster)> OrderClustersByManagerPort(
        IEnumerable<(RacConnectionParams Parameters, RacCluster Cluster)> targets, int managerPort)
    {
        var list = targets.ToList();
        if (managerPort <= 0)
            return list;
        var matching = list.Where(t => t.Cluster.Port == managerPort).ToList();
        return matching.Count > 0 ? matching : list;
    }

    /// <summary>
    /// Все варианты параметров rac для сервера базы (issue #92, v1.0.1.25): каждая
    /// запись справочника «Сервера управления» с тем же хостом (в порядке справочника),
    /// иначе — один вариант по умолчанию (<see cref="BuildConnectionParams"/>).
    /// </summary>
    internal static List<RacConnectionParams> BuildConnectionCandidates(
        string? serverAddress, IReadOnlyList<ManagementServer>? servers)
    {
        var host = (serverAddress ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ClusterInfobaseCredentials.HostOf)
            .FirstOrDefault(h => h.Length > 0) ?? "localhost";
        var result = new List<RacConnectionParams>();
        foreach (var server in servers ?? Array.Empty<ManagementServer>())
        {
            if (!ClusterInfobaseCredentials.SameHost(ClusterInfobaseCredentials.HostOf(server.Address), host))
                continue;
            if (result.Any(r => string.Equals(r.Address, server.Address, StringComparison.OrdinalIgnoreCase)
                                && r.Port == server.Port))
                continue;
            result.Add(new RacConnectionParams
            {
                Address = server.Address,
                Port = server.Port,
                User = server.User ?? string.Empty,
                Password = server.Password ?? string.Empty
            });
        }
        if (result.Count == 0)
            result.Add(BuildConnectionParams(serverAddress, servers));
        return result;
    }

    /// <summary>
    /// Параметры подключения rac для сервера базы: хост берётся из адреса базы
    /// («srv1:1541,srv2» — кластер с резервированием, берётся первый хост);
    /// при совпадении хоста с записью справочника «Сервера управления» используются
    /// её адрес/порт/логин/пароль, иначе — порт агента по умолчанию (1540).
    /// </summary>
    internal static RacConnectionParams BuildConnectionParams(
        string? serverAddress, IReadOnlyList<ManagementServer>? servers)
    {
        var host = (serverAddress ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ClusterInfobaseCredentials.HostOf)
            .FirstOrDefault(h => h.Length > 0) ?? "localhost";

        if (servers is not null)
        {
            foreach (var server in servers)
            {
                if (ClusterInfobaseCredentials.SameHost(
                        ClusterInfobaseCredentials.HostOf(server.Address), host))
                {
                    return new RacConnectionParams
                    {
                        Address = server.Address,
                        Port = server.Port,
                        User = server.User ?? string.Empty,
                        Password = server.Password ?? string.Empty
                    };
                }
            }
        }

        return new RacConnectionParams { Address = host, Port = IRacClient.DefaultPort };
    }

    /// <summary>
    /// Профиль СУБД с подстановкой типа СУБД (issue #79, v1.0.1.25): сервер/имя SQL-БД
    /// берутся из «infobase info» (если есть) или «infobase summary list», тип — из
    /// <paramref name="fallbackDbms"/>. Нужен хотя бы сервер СУБД; иначе null.
    /// </summary>
    internal static DbmsBackupProfile? ToProfileWithFallbackDbms(RacInfobaseSummary? summary, RacInfobaseInfo? info,
        string? fallbackDbms, string? infobaseId, string infobaseName)
    {
        var dbms = ParseDbms(fallbackDbms);
        if (dbms is null)
            return null;
        var dbServer = !string.IsNullOrWhiteSpace(info?.DbServer) ? info!.DbServer : summary?.DbServer;
        if (string.IsNullOrWhiteSpace(dbServer))
            return null;
        var dbName = !string.IsNullOrWhiteSpace(info?.DbName) ? info!.DbName : summary?.DbName;
        var dbUser = !string.IsNullOrWhiteSpace(info?.DbUser) ? info!.DbUser : summary?.DbUser;
        var (host, port) = ParseDbServer(dbServer);
        return new DbmsBackupProfile
        {
            InfobaseId = infobaseId ?? string.Empty,
            Dbms = dbms.Value,
            Host = host,
            Port = port,
            Database = string.IsNullOrWhiteSpace(dbName) ? infobaseName : dbName!.Trim(),
            User = dbUser?.Trim() ?? string.Empty,
            Password = string.Empty
        };
    }

    /// <summary>
    /// Профиль СУБД из полных свойств ИБ («infobase info», issue #92 v1.0.1.21) —
    /// fallback при пустой колонке «dbms» в «infobase summary list» (rac 8.5.4).
    /// Правила те же: поддерживаемые СУБД, пароль остаётся пустым.
    /// </summary>
    internal static DbmsBackupProfile? ToProfile(RacInfobaseInfo info, string? infobaseId, string infobaseName)
    {
        if (info is null)
            return null;
        var dbms = ParseDbms(info.Dbms);
        if (dbms is null)
            return null;

        var (host, port) = ParseDbServer(info.DbServer);
        return new DbmsBackupProfile
        {
            InfobaseId = infobaseId ?? string.Empty,
            Dbms = dbms.Value,
            Host = host,
            Port = port,
            Database = string.IsNullOrWhiteSpace(info.DbName) ? infobaseName : info.DbName.Trim(),
            User = info.DbUser?.Trim() ?? string.Empty,
            Password = string.Empty
        };
    }

    /// <summary>
    /// Профиль СУБД из строки «infobase summary list». Тип СУБД ограничен поддерживаемыми
    /// ibcmd (PostgreSQL/MS SQL) — прочие значения (IBMDB2, OracleDatabase) не резолвятся.
    /// У части версий rac колонка «db-name» отсутствует (issue #41): при создании базы
    /// сервер 1С именует БД в СУБД так же, как ИБ — берётся имя ИБ. Пароль остаётся пустым
    /// (rac его не возвращает).
    /// </summary>
    internal static DbmsBackupProfile? ToProfile(RacInfobaseSummary summary, string? infobaseId, string infobaseName)
    {
        if (summary is null)
            return null;
        var dbms = ParseDbms(summary.Dbms);
        if (dbms is null)
            return null;

        var (host, port) = ParseDbServer(summary.DbServer);
        return new DbmsBackupProfile
        {
            InfobaseId = infobaseId ?? string.Empty,
            Dbms = dbms.Value,
            Host = host,
            Port = port,
            Database = string.IsNullOrWhiteSpace(summary.DbName) ? infobaseName : summary.DbName.Trim(),
            User = summary.DbUser?.Trim() ?? string.Empty,
            Password = string.Empty
        };
    }

    /// <summary>Вид СУБД из значения rac («PostgreSQL», «MSSQLServer», «MS»); null — не поддерживается ibcmd.</summary>
    internal static DbmsKind? ParseDbms(string? dbms) =>
        (dbms?.Trim() ?? string.Empty).ToLowerInvariant() switch
        {
            "postgresql" or "postgres" or "postgre" => DbmsKind.PostgreSql,
            "mssqlserver" or "mssql" or "ms" => DbmsKind.MsSql,
            _ => null
        };

    /// <summary>
    /// Хост и порт из значения «db-server»: PostgreSQL — «host port=5433», MS SQL —
    /// «host,1433» или «host\instance» (порт не распознаётся), без порта — 0
    /// (значение по умолчанию для СУБД подставит <see cref="IbcmdService.DbServer"/>).
    /// </summary>
    internal static (string Host, int Port) ParseDbServer(string? dbServer)
    {
        var value = (dbServer ?? string.Empty).Trim();
        if (value.Length == 0)
            return ("localhost", 0);

        // PostgreSQL: «host port=5433».
        var portMarker = value.IndexOf(" port=", StringComparison.OrdinalIgnoreCase);
        if (portMarker > 0 && int.TryParse(value[(portMarker + 6)..].Trim(), out var pgPort))
            return (value[..portMarker].Trim(), pgPort);

        // MS SQL: «host,1433».
        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), out var msPort))
            return (value[..comma].Trim(), msPort);

        return (value, 0);
    }
}
