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

        var parameters = BuildConnectionParams(connection.Server, _servers);
        try
        {
            // Issue #92 (v1.0.1.21): общий таймаут на резолв одной базы — linked CTS
            // на всю последовательность кластеров; rac-клиенту передаётся этот токен.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResolveTimeout);

            var clusters = await _racClient.GetClustersAsync(parameters, timeoutCts.Token).ConfigureAwait(false);
            foreach (var cluster in clusters)
            {
                IReadOnlyList<RacInfobaseSummary> summaries;
                var probeStopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    summaries = await _racClient
                        .GetInfobasesAsync(parameters, cluster.Id, timeoutCts.Token)
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
                    string.Equals(s.Name?.Trim(), infobaseName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    // Issue #92: в журнале остаётся только «база не найдена», без
                    // признаков, почему. Показываем фактическое содержимое кластера —
                    // количество и имена ИБ, вернувшиеся из rac: сопоставление идёт
                    // по имени, и по списку видно расхождение имён.
                    _log?.Invoke(BuildClusterContents(cluster.Id, summaries));
                    continue;
                }

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
                    var info = await _racClient.GetInfobaseInfoAsync(
                        parameters, cluster.Id, match.InfobaseId,
                        infobaseUser: null, infobasePassword: null, timeoutCts.Token).ConfigureAwait(false);
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
                    _log?.Invoke(LocalizationManager.T("BatchOps.DbmsResolveInfoFailed"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log?.Invoke(string.Format(
                        LocalizationManager.T("BatchOps.DbmsResolveInfoFailedFormat"), ex.Message));
                }
            }

            _log?.Invoke(string.Format(
                LocalizationManager.T("BatchOps.DbmsResolveNotFound"),
                infobaseName, parameters.Address));
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
