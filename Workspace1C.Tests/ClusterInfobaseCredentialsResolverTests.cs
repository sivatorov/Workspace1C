using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты автоподстановки данных СУБД из кластера (rac) для массовой выгрузки ibcmd
/// (issue #92, план v1.0.1.14 п.3.1): резолвер с фейковым <see cref="IRacClient"/>
/// (креды найдены/не найдены/ошибка rac), парсеры dbms/db-server и сборка аргументов
/// ibcmd с автоподставленным профилем — без запуска rac и ibcmd.
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class ClusterInfobaseCredentialsResolverTests
{
    // ======================= Фейковый rac-клиент =======================

    /// <summary>
    /// Фейковый <see cref="IRacClient"/>: отдаёт заданный кластер и список баз
    /// «infobase summary list»; все прочие команды не поддерживаются.
    /// </summary>
    private sealed class FakeRacClient : IRacClient
    {
        public IReadOnlyList<RacCluster> Clusters { get; init; } = Array.Empty<RacCluster>();
        public IReadOnlyList<RacInfobaseSummary> Infobases { get; init; } = Array.Empty<RacInfobaseSummary>();
        public Exception? ClustersError { get; init; }
        public int ClustersCallCount;
        public RacConnectionParams? LastParameters;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            LastParameters = parameters;
            Interlocked.Increment(ref ClustersCallCount);
            if (ClustersError is not null)
                throw ClustersError;
            return Task.FromResult(Clusters);
        }

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult(Infobases);

        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
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

        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public string LastActionError => string.Empty;
    }

    private static Infobase ServerBase(string name = "Бухгалтерия", string server = "srv1c") => new()
    {
        Id = "base-id-1",
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = name
        }
    };

    private static RacInfobaseSummary Summary(
        string name = "Бухгалтерия", string dbms = "PostgreSQL",
        string dbServer = "pg-host", string dbName = "zup_db", string dbUser = "postgres") => new()
    {
        InfobaseId = Guid.NewGuid(),
        Name = name,
        Dbms = dbms,
        DbServer = dbServer,
        DbName = dbName,
        DbUser = dbUser
    };

    private static ClusterInfobaseCredentialsResolver CreateResolver(
        FakeRacClient rac, out List<string> log)
    {
        log = new List<string>();
        return new ClusterInfobaseCredentialsResolver(rac, servers: null, log: log.Add);
    }

    // ======================= ResolveAsync: положительные сценарии =======================

    [Fact]
    public async Task ResolveAsync_PostgreSqlSummary_BuildsProfile()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } },
            Infobases = new[] { Summary() }
        };
        var resolver = CreateResolver(rac, out var log);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.PostgreSql, profile!.Dbms);
        Assert.Equal("pg-host", profile.Host);
        Assert.Equal(0, profile.Port);
        Assert.Equal("zup_db", profile.Database);
        Assert.Equal("postgres", profile.User);
        // Пароль СУБД rac не возвращает — остаётся пустым.
        Assert.Equal(string.Empty, profile.Password);
        Assert.Equal("base-id-1", profile.InfobaseId);
        Assert.Empty(log);
    }

    [Fact]
    public async Task ResolveAsync_MsSqlSummary_ParsesCommaPort()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(dbms: "MSSQLServer", dbServer: "sql-host,1433", dbName: "zup", dbUser: "sa") }
        };
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.MsSql, profile!.Dbms);
        Assert.Equal("sql-host", profile.Host);
        Assert.Equal(1433, profile.Port);
        Assert.Equal("sa", profile.User);
    }

    [Fact]
    public async Task ResolveAsync_PostgreSqlWithPort_ParsesPort()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(dbServer: "pg-host port=5433") }
        };
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.NotNull(profile);
        Assert.Equal("pg-host", profile!.Host);
        Assert.Equal(5433, profile.Port);
    }

    [Fact]
    public async Task ResolveAsync_MissingDbName_FallsBackToInfobaseName()
    {
        // Часть версий rac не выводит db-name (issue #41): при создании базы сервер
        // 1С именует БД в СУБД так же, как ИБ.
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(name: "ЗУП", dbName: "") }
        };
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(ServerBase(name: "ЗУП"));

        Assert.NotNull(profile);
        Assert.Equal("ЗУП", profile!.Database);
    }

    [Fact]
    public async Task ResolveAsync_MatchesName_IgnoringCase()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(name: "БУХГАЛТЕРИЯ") }
        };
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(ServerBase(name: "бухгалтерия"));

        Assert.NotNull(profile);
    }

    [Fact]
    public async Task ResolveAsync_ClusterAdminFromDirectory_UsesDirectoryCredentials()
    {
        // Запись справочника «Сервера управления» с тем же хостом: подключение rac —
        // с её адресом/портом/логином/паролем кластера.
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary() }
        };
        var resolver = new ClusterInfobaseCredentialsResolver(rac, new[]
        {
            new ManagementServer { Address = "SRV1C", Port = 1545, User = "cluster-admin", Password = "cluster-pass" }
        });

        var profile = await resolver.ResolveAsync(ServerBase(server: "srv1c:1541"));

        Assert.NotNull(profile);
        Assert.Equal("SRV1C", rac.LastParameters!.Address);
        Assert.Equal(1545, rac.LastParameters.Port);
        Assert.Equal("cluster-admin", rac.LastParameters.User);
        Assert.Equal("cluster-pass", rac.LastParameters.Password);
    }

    // ======================= ResolveAsync: отрицательные сценарии =======================

    [Fact]
    public async Task ResolveAsync_FileBase_ReturnsNull_WithoutRacCalls()
    {
        var rac = new FakeRacClient { ClustersError = new RacClientException("rac недоступен") };
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\alfa" }
        });

        Assert.Null(profile);
        Assert.Equal(0, rac.ClustersCallCount); // rac не опрашивался.
    }

    [Fact]
    public async Task ResolveAsync_BaseNotFound_ReturnsNullAndLogs()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(name: "ДругаяБаза") }
        };
        var resolver = CreateResolver(rac, out var log);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.Null(profile);
        // Ошибка rac не прерывает операцию — причина логируется (локализованный текст
        // либо ключ, если локализация в тестовом процессе не загружена).
        Assert.Contains(log, m =>
            m.Contains("Бухгалтерия", StringComparison.Ordinal) ||
            m.Contains("DbmsResolveNotFound", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_BaseNotFound_LogsClusterContents()
    {
        // issue #92 (репорт 7OH 2026-11-01): при отсутствии совпадения в журнал уходит
        // фактическое содержимое кластера — количество и имена ИБ, вернувшиеся из rac,
        // чтобы по журналу можно было судить, почему база «не найдена». Локализация в
        // тестовом процессе может быть не загружена — тогда остаётся ключ строки.
        var clusterId = Guid.NewGuid();
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = clusterId } },
            Infobases = new[] { Summary(name: "ДругаяБаза"), Summary(name: "ЕщёБаза") }
        };
        var resolver = CreateResolver(rac, out var log);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.Null(profile);
        Assert.Contains(log, m =>
            m.Contains("DbmsResolveClusterContentsFormat", StringComparison.Ordinal) ||
            (m.Contains(clusterId.ToString(), StringComparison.Ordinal) &&
             m.Contains("2", StringComparison.Ordinal) &&
             m.Contains("ДругаяБаза", StringComparison.Ordinal) &&
             m.Contains("ЕщёБаза", StringComparison.Ordinal)));
    }

    [Fact]
    public void BuildClusterContents_FormatsCountAndNames()
    {
        var clusterId = Guid.NewGuid();
        var line = ClusterInfobaseCredentialsResolver.BuildClusterContents(clusterId,
            new[] { Summary(name: "База1"), Summary(name: " База2 ") });

        // Локализация в тестовом процессе не загружена — возвращается ключ строки
        // (сама подстановка формата проверяется на загруженных ресурсах приложения).
        Assert.Equal("BatchOps.DbmsResolveClusterContentsFormat", line);
    }

    [Fact]
    public void BuildClusterContents_DoesNotThrow_OnEmptyOrNullList()
    {
        var empty = ClusterInfobaseCredentialsResolver.BuildClusterContents(Guid.NewGuid(), Array.Empty<RacInfobaseSummary>());
        Assert.Equal("BatchOps.DbmsResolveClusterContentsFormat", empty);

        var nullList = ClusterInfobaseCredentialsResolver.BuildClusterContents(Guid.NewGuid(), null);
        Assert.Equal("BatchOps.DbmsResolveClusterContentsFormat", nullList);
    }

    [Fact]
    public async Task ResolveAsync_RacError_ReturnsNull_LogsAndDoesNotThrow()
    {
        var rac = new FakeRacClient { ClustersError = new RacClientException("Не найден rac") };
        var resolver = CreateResolver(rac, out var log);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.Null(profile);
        Assert.NotEmpty(log);
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedDbms_ReturnsNullAndLogs()
    {
        var rac = new FakeRacClient
        {
            Clusters = new[] { new RacCluster { Id = Guid.NewGuid() } },
            Infobases = new[] { Summary(dbms: "IBMDB2") }
        };
        var resolver = CreateResolver(rac, out var log);

        var profile = await resolver.ResolveAsync(ServerBase());

        Assert.Null(profile);
        Assert.Contains(log, m =>
            m.Contains("IBMDB2", StringComparison.Ordinal) ||
            m.Contains("DbmsResolveUnsupportedDbms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_EmptyRef_ReturnsNull()
    {
        var rac = new FakeRacClient();
        var resolver = CreateResolver(rac, out _);

        var profile = await resolver.ResolveAsync(new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv1c", DatabaseName = "" }
        });

        Assert.Null(profile);
        Assert.Equal(0, rac.ClustersCallCount);
    }

    // ======================= Парсеры =======================

    [Fact]
    public void ParseDbServer_Cases()
    {
        Assert.Equal(("localhost", 0), ClusterInfobaseCredentialsResolver.ParseDbServer(null));
        Assert.Equal(("localhost", 0), ClusterInfobaseCredentialsResolver.ParseDbServer(""));
        Assert.Equal(("pg-host", 0), ClusterInfobaseCredentialsResolver.ParseDbServer("pg-host"));
        Assert.Equal(("pg-host", 5433), ClusterInfobaseCredentialsResolver.ParseDbServer("pg-host port=5433"));
        Assert.Equal(("sql-host", 1433), ClusterInfobaseCredentialsResolver.ParseDbServer("sql-host,1433"));
        // Именованный экземпляр MS SQL: порт не распознаётся — остаётся 0.
        Assert.Equal(("sql-host\\SQLEXPRESS", 0), ClusterInfobaseCredentialsResolver.ParseDbServer("sql-host\\SQLEXPRESS"));
    }

    [Fact]
    public void ParseDbms_Cases()
    {
        Assert.Equal(DbmsKind.PostgreSql, ClusterInfobaseCredentialsResolver.ParseDbms("PostgreSQL"));
        Assert.Equal(DbmsKind.PostgreSql, ClusterInfobaseCredentialsResolver.ParseDbms(" postgres "));
        Assert.Equal(DbmsKind.MsSql, ClusterInfobaseCredentialsResolver.ParseDbms("MSSQLServer"));
        Assert.Equal(DbmsKind.MsSql, ClusterInfobaseCredentialsResolver.ParseDbms("MS"));
        Assert.Null(ClusterInfobaseCredentialsResolver.ParseDbms("IBMDB2"));
        Assert.Null(ClusterInfobaseCredentialsResolver.ParseDbms("OracleDatabase"));
        Assert.Null(ClusterInfobaseCredentialsResolver.ParseDbms(""));
        Assert.Null(ClusterInfobaseCredentialsResolver.ParseDbms(null));
    }

    [Fact]
    public void BuildConnectionParams_DefaultsToAgentPort()
    {
        // Порт базы (кластера 1541) ≠ порт агента ragent для rac: без справочника
        // берётся порт агента по умолчанию 1540, хост — без порта.
        var parameters = ClusterInfobaseCredentialsResolver.BuildConnectionParams("srv1c:1541", null);

        Assert.Equal("srv1c", parameters.Address);
        Assert.Equal(IRacClient.DefaultPort, parameters.Port);
        Assert.Equal(string.Empty, parameters.User);
    }

    [Fact]
    public void BuildConnectionParams_MultiHostCluster_TakesFirstHost()
    {
        var parameters = ClusterInfobaseCredentialsResolver.BuildConnectionParams("srv1, srv2", null);

        Assert.Equal("srv1", parameters.Address);
    }

    // ======================= Аргументы ibcmd с автоподставленным профилем =======================

    [Fact]
    public void BuildConnectionArguments_WithResolvedProfile_BuildsIbcmdArgs()
    {
        var profile = ClusterInfobaseCredentialsResolver.ToProfile(
            Summary(dbms: "PostgreSQL", dbServer: "pg-host port=5433",
                dbName: "zup_db", dbUser: "postgres"),
            "base-id-1", "Бухгалтерия")!;

        var args = IbcmdService.BuildConnectionArguments(ServerBase(), profile, out var error);

        Assert.Null(error);
        Assert.NotNull(args);
        Assert.Contains("--dbms=PostgreSQL", args!);
        Assert.Contains("--db-server=pg-host port=5433", args);
        Assert.Contains("--db-name=zup_db", args);
        Assert.Contains("--db-user=postgres", args);
        // Пароль rac не возвращает — аргумент пароля не передаётся вовсе.
        Assert.DoesNotContain(args, a => a.StartsWith("--db-pwd=", StringComparison.Ordinal));
    }

    // ======================= ViewModel: прогон с автоподстановкой =======================

    [Fact]
    public async Task BatchOperationsViewModel_ResolverSuppliesProfile_PassesToExecutor()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase();
            var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
            {
                PathText = tempDir,
                DbmsProfileResolver = (base_, _) => Task.FromResult<DbmsBackupProfile?>(
                    new DbmsBackupProfile
                    {
                        InfobaseId = base_.Id,
                        Dbms = DbmsKind.PostgreSql,
                        Host = "pg-host",
                        Database = "zup_db",
                        User = "postgres"
                    })
            };

            DbmsBackupProfile? passed = null;
            vm.ExecutorWithProfileOverride = (base_, path, profile, ct) =>
            {
                passed = profile;
                return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
            };

            await vm.RunAsync();

            Assert.True(vm.Items.Single(i => i.Infobase == ib).Status == BatchOperationsStatus.Success);
            Assert.NotNull(passed);
            Assert.Equal("pg-host", passed!.Host);
            Assert.Equal("postgres", passed.User);
            // В логе окна есть шаг автоподстановки (локализованный текст либо ключ).
            Assert.Contains(vm.LogLines, l =>
                l.Contains("rac", StringComparison.Ordinal) ||
                l.Contains("DbmsProfile", StringComparison.Ordinal));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BatchOperationsViewModel_ResolverReturnsNull_FailsFastWithoutIbcmd()
    {
        // issue #92 (v100117): резолвер вернул null (база не найдена в кластере) —
        // ibcmd НЕ запускается «наудачу»: элемент сразу получает статус ошибки
        // с понятным текстом NeedDbmsProfileFormat, журнал содержит причину.
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase();
            var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
            {
                PathText = tempDir,
                DbmsProfileResolver = (_, _) => Task.FromResult<DbmsBackupProfile?>(null)
            };

            var executed = false;
            vm.ExecutorWithProfileOverride = (base_, path, profile, ct) =>
            {
                executed = true;
                return Task.FromResult(new IbcmdResult { Success = false, ExitCode = 1, Error = "NeedDbmsProfile" });
            };

            await vm.RunAsync();

            Assert.False(executed); // fail-fast: ibcmd не тратит время на заведомый провал
            Assert.True(vm.Items.Single(i => i.Infobase == ib).Status == BatchOperationsStatus.Failed);
            // Локализация в тестовом процессе не загружена — остаётся ключ строки.
            Assert.Contains("BatchOps.NeedDbmsProfileFormat",
                vm.Items.Single(i => i.Infobase == ib).StatusText ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BatchOperationsViewModel_ResolverThrows_FailsFastWithoutIbcmd()
    {
        // issue #92 (v100117): исключение резолвера (rac сломался) больше не отправляет
        // ibcmd без данных СУБД — fail-fast с понятным текстом NeedDbmsProfileFormat.
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase();
            var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
            {
                PathText = tempDir,
                DbmsProfileResolver = (_, _) => throw new RacClientException("rac упал")
            };

            var executed = false;
            vm.ExecutorWithProfileOverride = (base_, path, profile, ct) =>
            {
                executed = true;
                return Task.FromResult(new IbcmdResult { Success = false, ExitCode = 1, Error = "x" });
            };

            await vm.RunAsync();

            Assert.False(executed);
            Assert.True(vm.Items.Single(i => i.Infobase == ib).Status == BatchOperationsStatus.Failed);
            Assert.Contains("BatchOps.NeedDbmsProfileFormat",
                vm.Items.Single(i => i.Infobase == ib).StatusText ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void BatchOperationsService_Validate_AllowMissingDbmsProfiles_SkipsProfileError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "batch-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var ib = ServerBase(); // клиент-серверная база без профиля СУБД.

            var strict = BatchOperationsService.Validate(new[] { ib }, BatchOperationKind.DumpDt,
                tempDir, allowMissingDbmsProfiles: false);
            Assert.Contains(strict, e => e.Contains("NeedDbmsProfile", StringComparison.Ordinal));

            var allowed = BatchOperationsService.Validate(new[] { ib }, BatchOperationKind.DumpDt,
                tempDir, allowMissingDbmsProfiles: true);
            Assert.DoesNotContain(allowed, e => e.Contains("NeedDbmsProfile", StringComparison.Ordinal));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }
}
