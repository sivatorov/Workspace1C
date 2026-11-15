using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #106 «Массовая загрузка cf», доработка 1.0.1.64: на стенде aepkugesi
/// подготовка базы к загрузке падала с «база не найдена в кластерах rac или тип
/// СУБД не определён», хотя rac просто не ответил за 30 с, а профиль СУБД для
/// блокировок вообще не нужен.
/// <list type="bullet">
/// <item>«cluster list» получает увеличенный таймаут и ОДИН повтор при таймауте —
/// холодный/загруженный сервер успевает ответить;</item>
/// <item>резолвер возвращает найденные кластер и ИБ даже когда профиль СУБД не
/// собран (Profile = null) — блокировкам и завершению сеансов профиль не нужен;</item>
/// <item><see cref="BatchOpsPreparationService.ApplyBeforeAsync"/> больше не требует
/// профиль, а причину неудачи берёт из <c>LastFailureReason</c> резолвера;</item>
/// <item>в причине неудачи есть подсказка проверить адрес/порт ras в справочнике
/// «Серверы 1С».</item>
/// </list>
/// </summary>
public sealed class Issue106PreparationRacTests
{
    /// <summary>
    /// Фейковый rac: первые <see cref="ClusterFailures"/> вызовов «cluster list»
    /// завершаются заданной ошибкой, затем возвращается кластер. Счётчик
    /// <see cref="ClusterCalls"/> показывает, был ли повтор.
    /// </summary>
    private sealed class FlakyRac : IRacClient
    {
        public int ClusterFailures { get; init; }
        public Exception ClusterFailure { get; init; } =
            new RacClientException("Превышен таймаут ожидания ответа rac (45 с).");
        public RacCluster[] Clusters { get; init; } = Array.Empty<RacCluster>();
        public RacInfobaseSummary[] Summaries { get; init; } = Array.Empty<RacInfobaseSummary>();
        public RacInfobaseInfo? Info { get; init; }
        public RacSessionInfo[] Sessions { get; init; } = Array.Empty<RacSessionInfo>();

        public int ClusterCalls { get; private set; }
        public List<(Guid Cluster, Guid Infobase, RacInfobaseUpdateRequest Request)> Updates { get; } = new();
        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams p, CancellationToken ct = default)
        {
            ClusterCalls++;
            if (ClusterCalls <= ClusterFailures)
                throw ClusterFailure;
            return Task.FromResult<IReadOnlyList<RacCluster>>(Clusters);
        }

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Summaries);

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams p, Guid c, Guid i, string? u, string? pw,
            CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult(Info);

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Sessions);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null,
            string? pw = null, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r,
            CancellationToken ct = default)
        {
            Updates.Add((c, i, r));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> SetJobStateAsync(
            RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null,
            string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null,
            string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static Infobase ServerBase() => new()
    {
        Id = "base-1",
        Name = "Бухгалтерия",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv1c",
            DatabaseName = "Бухгалтерия"
        }
    };

    private static FlakyRac RacWithBase(string dbms = "PostgreSQL", string dbServer = "pg-host") => new()
    {
        Clusters = new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } },
        Summaries = new[]
        {
            new RacInfobaseSummary
            {
                InfobaseId = Guid.NewGuid(),
                Name = "Бухгалтерия",
                Dbms = dbms,
                DbServer = dbServer,
                DbName = "buh_db"
            }
        },
        Info = new RacInfobaseInfo
        {
            Dbms = dbms,
            DbServer = dbServer,
            DbName = "buh_db",
            SessionsDeny = false,
            ScheduledJobsDeny = false
        }
    };

    [Fact]
    public async Task ApplyBefore_BaseFoundWithoutDbmsProfile_AppliesLocks()
    {
        // rac нашёл ИБ, но профиль СУБД собрать не удалось (пустые dbms/db-server,
        // «infobase info» ничего не вернула). Для блокировки регламентных заданий
        // профиль не нужен — подготовка должна пройти, а не пропустить базу.
        var rac = new FlakyRac
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } },
            Summaries = new[]
            {
                new RacInfobaseSummary
                {
                    InfobaseId = Guid.NewGuid(),
                    Name = "Бухгалтерия",
                    Dbms = string.Empty,
                    DbServer = string.Empty,
                    DbName = string.Empty
                }
            },
            Info = null
        };
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(
            ServerBase(), new BatchOpsPreparationOptions { DenyScheduledJobs = true });

        Assert.NotNull(undo);
        Assert.True(undo!.Success);
        var update = Assert.Single(rac.Updates);
        Assert.True(update.Request.ScheduledJobsDeny);
        Assert.Null(update.Request.SessionsDeny);
    }

    [Fact]
    public async Task Resolver_ClusterListTimeoutOnce_RetriesAndFindsBase()
    {
        // Первая попытка «cluster list» не уложилась в таймаут (на стенде — 30 с),
        // вторая успешна: резолвер обязан повторить запрос, а не пропустить базу.
        var rac = RacWithBase();
        var flaky = new FlakyRac
        {
            ClusterFailures = 1,
            ClusterFailure = new OperationCanceledException(),
            Clusters = rac.Clusters,
            Summaries = rac.Summaries,
            Info = rac.Info
        };
        var resolver = new ClusterInfobaseCredentialsResolver(flaky, servers: null, log: null);

        var resolution = await resolver.ResolveDetailedAsync(ServerBase());

        Assert.Equal(2, flaky.ClusterCalls);
        Assert.NotEqual(Guid.Empty, resolution.InfobaseId);
        Assert.NotNull(resolution.Profile);
    }

    [Fact]
    public async Task Resolver_ClusterListAlwaysFails_ReportsAddressAndHint()
    {
        // rac недоступен: после повтора причина попадает в LastFailureReason вместе
        // с подсказкой, где задать адрес/порт ras и креды администратора кластера.
        var rac = new FlakyRac { ClusterFailures = int.MaxValue };
        var resolver = new ClusterInfobaseCredentialsResolver(rac, servers: null, log: null);

        var resolution = await resolver.ResolveDetailedAsync(ServerBase());

        Assert.Equal(Guid.Empty, resolution.InfobaseId);
        Assert.Equal(2, rac.ClusterCalls); // ровно один повтор
        Assert.False(string.IsNullOrWhiteSpace(resolver.LastFailureReason));
        Assert.Contains("BatchOps.DbmsResolveClustersFailed", resolver.LastFailureReason);
        Assert.Contains("BatchOps.Prepare.RacUnavailableHint", resolver.LastFailureReason);
    }

    [Fact]
    public async Task ApplyBefore_RacUnavailable_ErrorCarriesResolverReason()
    {
        // Окно массовой операции показывает причину резолвера, а не общее
        // «база не найдена в кластерах rac или тип СУБД не определён».
        var rac = new FlakyRac { ClusterFailures = int.MaxValue };
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(
            ServerBase(), new BatchOpsPreparationOptions { DenyScheduledJobs = true });

        Assert.NotNull(undo);
        Assert.False(undo!.Success);
        Assert.Contains("BatchOps", undo.Error);
        Assert.DoesNotContain("Prepare.ResolveFailed", undo.Error);
    }
}
