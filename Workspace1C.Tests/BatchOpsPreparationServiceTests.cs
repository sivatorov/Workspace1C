using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сервиса подготовки «Перед операцией» массовых операций (issue #92,
/// v1.0.1.21, пожелание aepkugesi): завершение сеансов, блокировка начала сеансов
/// (с кодом разрешения), блокировка регламентных заданий и автоматическое снятие
/// блокировок после операции. rac подменяется фейком — без запуска процессов.
/// </summary>
public sealed class BatchOpsPreparationServiceTests
{
    // ======================= Фейковый rac-клиент =======================

    private sealed class FakeRac : IRacClient
    {
        public RacCluster[] Clusters { get; init; } = Array.Empty<RacCluster>();
        public RacInfobaseSummary[] Summaries { get; init; } = Array.Empty<RacInfobaseSummary>();
        public RacInfobaseInfo? Info { get; init; }
        public RacSessionInfo[] Sessions { get; init; } = Array.Empty<RacSessionInfo>();
        public Exception? ClustersError { get; init; }

        // Issue #92 (v1.0.1.30): фейк неудачного «session terminate» (rac отвергает команду).
        public bool TerminateSucceeds { get; init; } = true;

        public List<(Guid Cluster, Guid Session)> Terminated { get; } = new();
        public List<(Guid Cluster, Guid Infobase, RacInfobaseUpdateRequest Request)> Updates { get; } = new();
        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(RacConnectionParams p, CancellationToken ct = default)
        {
            if (ClustersError is not null)
                throw ClustersError;
            return Task.FromResult<IReadOnlyList<RacCluster>>(Clusters);
        }

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Summaries);

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(RacConnectionParams p, Guid c, Guid i, string? u, string? pw, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult(Info);

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Sessions);

        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
        {
            Terminated.Add((c, s));
            return Task.FromResult(TerminateSucceeds);
        }

        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
        {
            Updates.Add((c, i, r));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> UpdateClusterAsync(RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> SetJobStateAsync(RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
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

    private static FakeRac CreateRac(bool sessionsDeny = false, bool jobsDeny = false, int sessionCount = 2) => new()
    {
        Clusters = new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } },
        Summaries = new[]
        {
            new RacInfobaseSummary
            {
                InfobaseId = Guid.NewGuid(),
                Name = "Бухгалтерия",
                Dbms = "PostgreSQL",
                DbServer = "pg-host",
                DbName = "buh_db"
            }
        },
        Info = new RacInfobaseInfo
        {
            Dbms = "PostgreSQL",
            DbServer = "pg-host",
            DbName = "buh_db",
            SessionsDeny = sessionsDeny,
            ScheduledJobsDeny = jobsDeny
        },
        Sessions = Enumerable.Range(0, sessionCount)
            .Select(_ => new RacSessionInfo { Id = Guid.NewGuid(), User = "Пользователь" })
            .ToArray()
    };

    private static BatchOpsPreparationOptions FullOptions(string? permissionCode = null) => new()
    {
        TerminateSessions = true,
        DenyNewSessions = true,
        PermissionCode = permissionCode,
        DenyScheduledJobs = true
    };

    // ======================= Клиент-серверная база =======================

    [Fact]
    public async Task ApplyBefore_TerminatesSessions_SetsLocks()
    {
        var rac = CreateRac(sessionCount: 2);
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(ServerBase(), FullOptions(permissionCode: "123"));

        Assert.NotNull(undo);
        Assert.True(undo!.Success);
        Assert.Equal(2, rac.Terminated.Count);
        var update = Assert.Single(rac.Updates);
        Assert.True(update.Request.SessionsDeny);
        Assert.True(update.Request.ScheduledJobsDeny);
        Assert.Equal("123", update.Request.PermissionCode);
        Assert.True(undo.SetSessionsDeny);
        Assert.True(undo.SetScheduledJobsDeny);
    }

    [Fact]
    public async Task RestoreAfter_ReleasesOnlyLocksWeSet()
    {
        var rac = CreateRac(sessionCount: 0);
        var service = new BatchOpsPreparationService(rac);
        var undo = await service.ApplyBeforeAsync(ServerBase(), FullOptions());

        Assert.NotNull(undo);
        var restoreError = await service.RestoreAfterAsync(ServerBase(), undo);

        Assert.Null(restoreError);
        Assert.Equal(2, rac.Updates.Count);
        var restore = rac.Updates[1].Request;
        Assert.False(restore.SessionsDeny!.Value);
        Assert.False(restore.ScheduledJobsDeny);
    }

    [Fact]
    public async Task ApplyBefore_AdminLockedBase_NotTouched()
    {
        // База, уже заблокированная администратором, не трогается: обновлений нет,
        // восстанавливать нечего (issue #92, v1.0.1.21).
        var rac = CreateRac(sessionsDeny: true, jobsDeny: true, sessionCount: 0);
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(ServerBase(), FullOptions());

        Assert.NotNull(undo);
        Assert.True(undo!.Success);
        Assert.Empty(rac.Updates);
        Assert.False(undo.SetSessionsDeny);
        Assert.False(undo.SetScheduledJobsDeny);
    }

    [Fact]
    public async Task ApplyBefore_DesignerEngine_SessionDenyNotApplied()
    {
        // Issue #92 (v1.0.1.30): при движке «Конфигуратор» блокировка начала сеансов
        // НЕ применяется (/DumpIB не может войти в базу: «Начало сеанса запрещено»),
        // а блокировка регламентных заданий остаётся совместимой.
        var rac = CreateRac(sessionCount: 0);
        var log = new List<string>();
        var service = new BatchOpsPreparationService(rac, log: log.Add);
        var options = new BatchOpsPreparationOptions
        {
            DenyNewSessions = true,
            DenyScheduledJobs = true,
            SessionDenyIncompatible = true
        };

        var undo = await service.ApplyBeforeAsync(ServerBase(), options);

        Assert.NotNull(undo);
        Assert.True(undo!.Success);
        var update = Assert.Single(rac.Updates);
        // sessions-deny не ставится; scheduled-jobs-deny ставится.
        Assert.Null(update.Request.SessionsDeny);
        Assert.True(update.Request.ScheduledJobsDeny);
        Assert.False(undo.SetSessionsDeny);
        Assert.True(undo.SetScheduledJobsDeny);
        // В журнале — объяснение, почему блокировка сеансов не применялась.
        Assert.Contains(log, l => l.Contains("BatchOps.Prepare.SessionDenyDesignerLog"));
    }

    [Fact]
    public async Task ApplyBefore_TerminateIncomplete_LocksNotApplied()
    {
        // Issue #92 (v1.0.1.30): если «session terminate» не прошёл (rac 8.5.4 отвергает
        // команду), блокировки НЕ включаются — сеансы остались, база не остаётся
        // заблокированной после ошибки; основание пропуска — в результате.
        var rac = new FakeRac
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Summaries = new[]
            {
                new RacInfobaseSummary
                {
                    InfobaseId = Guid.NewGuid(), Name = "Бухгалтерия",
                    Dbms = "PostgreSQL", DbServer = "pg-host", DbName = "buh_db"
                }
            },
            Sessions = new[] { new RacSessionInfo { Id = Guid.NewGuid(), User = "Пользователь" } },
            TerminateSucceeds = false
        };
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(ServerBase(), FullOptions());

        Assert.NotNull(undo);
        Assert.False(undo!.Success);
        Assert.False(string.IsNullOrWhiteSpace(undo.Error));
        Assert.Empty(rac.Updates); // блокировки не ставились — восстанавливать нечего.
    }

    [Fact]
    public async Task ApplyBefore_RacUnavailable_ReturnsFailure()
    {
        var rac = new FakeRac { ClustersError = new RacClientException("rac недоступен") };
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(ServerBase(), FullOptions());

        Assert.NotNull(undo);
        Assert.False(undo!.Success);
        Assert.False(string.IsNullOrWhiteSpace(undo.Error));
    }

    [Fact]
    public async Task ApplyBefore_NoOptions_ReturnsNull()
    {
        var rac = CreateRac();
        var service = new BatchOpsPreparationService(rac);

        var undo = await service.ApplyBeforeAsync(ServerBase(), new BatchOpsPreparationOptions());

        Assert.Null(undo);
        Assert.Empty(rac.Terminated);
        Assert.Empty(rac.Updates);
    }

    // ======================= Файловая база (1cv8.cdn) =======================

    [Fact]
    public async Task FileBase_LockAndUnlock_CdnFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "batchops_prep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fileBase = new Infobase
            {
                Id = "file-1",
                Name = "Файловая",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = directory }
            };
            var service = new BatchOpsPreparationService(new FakeRac());

            var undo = await service.ApplyBeforeAsync(fileBase, new BatchOpsPreparationOptions { DenyNewSessions = true });
            Assert.NotNull(undo);
            Assert.True(undo!.Success);
            Assert.True(undo.FileLockApplied);
            Assert.True(File.Exists(Path.Combine(directory, FileBaseSessionLock.CdnFileName)));

            var restoreError = await service.RestoreAfterAsync(fileBase, undo);
            Assert.Null(restoreError);
            Assert.False(File.Exists(Path.Combine(directory, FileBaseSessionLock.CdnFileName)));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* занято */ }
        }
    }

    // ======================= Интеграция с ViewModel массовых операций =======================

    [Fact]
    public async Task BatchVm_PreparationFailure_SkipsBaseAndContinues()
    {
        var rac = new FakeRac { ClustersError = new RacClientException("rac недоступен") };
        var server = new Infobase
        {
            Name = "Серверная",
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv1c", DatabaseName = "b1" }
        };
        var vm = new BatchOperationsViewModel(new[] { server }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}",
            PreparationService = new BatchOpsPreparationService(rac)
        };
        vm.DbmsProfileResolver = (_, _) => Task.FromResult<DbmsBackupProfile?>(null);
        vm.PrepareDenyNewSessions = true;

        var executed = false;
        vm.ExecutorOverride = (_, _, _) => { executed = true; return Task.FromResult(new IbcmdResult { Success = true }); };

        await vm.RunAsync();

        Assert.Equal(BatchOperationsStatus.Skipped, vm.Items[0].Status);
        Assert.False(executed);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task BatchVm_PreparationNull_WithEnabledOptions_SkipsBase()
    {
        // Сервис подготовки недоступен (rac-клиент не найден) — база пропускается
        // с понятной записью в журнал, окно не блокируется.
        var server = new Infobase
        {
            Name = "Серверная",
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv1c", DatabaseName = "b1" }
        };
        var vm = new BatchOperationsViewModel(new[] { server }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}",
            PreparationService = null
        };
        vm.DbmsProfileResolver = (_, _) => Task.FromResult<DbmsBackupProfile?>(null);
        vm.PrepareTerminateSessions = true;

        await vm.RunAsync();

        Assert.Equal(BatchOperationsStatus.Skipped, vm.Items[0].Status);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task BatchVm_NoPreparationOptions_OperationRunsAsBefore()
    {
        var file = new Infobase
        {
            Name = "Файловая",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\demo" }
        };
        var vm = new BatchOperationsViewModel(new[] { file }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}"
        };
        var executed = false;
        vm.ExecutorOverride = (_, _, _) => { executed = true; return Task.FromResult(new IbcmdResult { Success = true }); };

        await vm.RunAsync();

        Assert.True(executed);
        Assert.Equal(BatchOperationsStatus.Success, vm.Items[0].Status);
    }
}
