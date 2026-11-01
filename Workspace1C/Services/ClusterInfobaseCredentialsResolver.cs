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
public sealed class ClusterInfobaseCredentialsResolver
{
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
    {
        var connection = infobase?.Connection;
        if (connection?.Type != ConnectionType.ClientServer)
            return null; // файловые/веб-базы не требуют данных СУБД.

        var infobaseName = connection.DatabaseName?.Trim();
        if (string.IsNullOrWhiteSpace(infobaseName))
        {
            _log?.Invoke(LocalizationManager.T("BatchOps.DbmsResolveNoName"));
            return null;
        }

        var parameters = BuildConnectionParams(connection.Server, _servers);
        try
        {
            var clusters = await _racClient.GetClustersAsync(parameters, ct).ConfigureAwait(false);
            foreach (var cluster in clusters)
            {
                IReadOnlyList<RacInfobaseSummary> summaries;
                try
                {
                    summaries = await _racClient
                        .GetInfobasesAsync(parameters, cluster.Id, ct)
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
                    return profile;

                _log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveUnsupportedDbms"),
                    infobaseName, match.Dbms));
            }

            _log?.Invoke(string.Format(
                LocalizationManager.T("BatchOps.DbmsResolveNotFound"),
                infobaseName, parameters.Address));
            return null;
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
            return null;
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
