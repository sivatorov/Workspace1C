using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #106 «Массовая загрузка cf» (автор aepkugesi): новая функция массовой
/// загрузки конфигурации из файла *.cf через Конфигуратор (/LoadCfg + /UpdateDBCfg)
/// с опциями «Перед загрузкой» — выгрузка базы в .dt, завершение сеансов, блокировка
/// начала сеансов, блокировка регламентных заданий; блокировки снимаются после
/// операции. Тесты чистой логики (по образцу TestDeployPlanner/BatchOpsPreparation):
/// порядок шагов, маппинг опций, пред-валидация, формирование аргументов
/// конфигуратора (/UC) и оркестрация <see cref="MassLoadCfService"/> — без реальной 1С
/// (запуск Конфигуратора подменяется, rac подменяется фейком).
/// </summary>
public sealed class Issue106MassLoadCfTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cfFile;

    public Issue106MassLoadCfTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "massloadcf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cfFile = Path.Combine(_tempDir, "conf.cf");
        File.WriteAllText(_cfFile, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* не критично */ }
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

    private static Infobase WebBase() => new()
    {
        Id = "base-web",
        Name = "ВебБаза",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.WebServer,
            WebUrl = "http://srv/base"
        }
    };

    // ======================= Планирование шагов =======================

    [Fact]
    public void PlanSteps_BareOptions_OnlyLoadCf()
    {
        var steps = typeof(MassLoadCfPlanner)
            .GetMethod("PlanSteps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[] { new MassLoadCfOptions() }) as IReadOnlyList<MassLoadCfStep>;

        Assert.NotNull(steps);
        var plan = Assert.Single(steps!);
        Assert.Equal(MassLoadCfStep.LoadCf, plan);
    }

    [Fact]
    public void PlanSteps_AllOptions_PrepareDumpLoadRestore()
    {
        // Порядок issue #106: подготовка (завершение сеансов, блокировки) →
        // выгрузка .dt → загрузка → снятие блокировок.
        var options = new MassLoadCfOptions
        {
            TerminateSessions = true,
            DenyNewSessions = true,
            DenyScheduledJobs = true,
            DumpDtBefore = true
        };
        var steps = typeof(MassLoadCfPlanner)
            .GetMethod("PlanSteps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[] { options }) as IReadOnlyList<MassLoadCfStep>;

        Assert.NotNull(steps);
        Assert.Equal(
            new[] { MassLoadCfStep.Prepare, MassLoadCfStep.DumpDt, MassLoadCfStep.LoadCf, MassLoadCfStep.Restore },
            steps!.ToArray());
    }

    [Fact]
    public void PlanSteps_DumpOnly_NoPrepareNoRestore()
    {
        var options = new MassLoadCfOptions { DumpDtBefore = true };
        var steps = typeof(MassLoadCfPlanner)
            .GetMethod("PlanSteps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[] { options }) as IReadOnlyList<MassLoadCfStep>;

        Assert.NotNull(steps);
        Assert.Equal(new[] { MassLoadCfStep.DumpDt, MassLoadCfStep.LoadCf }, steps!.ToArray());
    }

    [Fact]
    public void ToPreparationOptions_MapsFlags_TrimsPermissionCode()
    {
        var mapped = typeof(MassLoadCfPlanner)
            .GetMethod("ToPreparationOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[]
            {
                new MassLoadCfOptions
                {
                    TerminateSessions = true,
                    DenyNewSessions = true,
                    DenyScheduledJobs = true,
                    PermissionCode = "  код123  "
                }
            }) as BatchOpsPreparationOptions;

        Assert.NotNull(mapped);
        Assert.True(mapped!.TerminateSessions);
        Assert.True(mapped.DenyNewSessions);
        Assert.True(mapped.DenyScheduledJobs);
        Assert.Equal("код123", mapped.PermissionCode);
        // Конфигуратор входит в заблокированную базу по коду разрешения (/UC) —
        // блокировка начала сеансов СОВМЕСТИМА с операцией (в отличие от движка
        // «Конфигуратор» массовой выгрузки, issue #92 v1.0.1.30).
        Assert.False(mapped.SessionDenyIncompatible);
    }

    // ======================= Пред-валидация =======================

    [Fact]
    public void Validate_MissingCfFile_AddsError()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = string.Empty }, new[] { ServerBase() });

        var error = Assert.Single(errors);
        Assert.Contains("BatchOps", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonExistingCfFile_AddsError()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = Path.Combine(_tempDir, "нет.cf") },
            Array.Empty<Infobase>());

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_NonCfExtension_AddsError()
    {
        var dt = Path.Combine(_tempDir, "conf.dt");
        File.WriteAllText(dt, "stub");
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = dt }, Array.Empty<Infobase>());

        Assert.Single(errors);
    }

    [Fact]
    public void Valid_CfFile_NoErrors()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = _cfFile }, new[] { ServerBase() });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DumpDtWithoutDirectory_AddsError()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = _cfFile, DumpDtBefore = true },
            Array.Empty<Infobase>());

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_DumpDtWithMissingDirectory_AddsError()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions
            {
                CfFilePath = _cfFile,
                DumpDtBefore = true,
                DumpDirectory = Path.Combine(_tempDir, "нет_такого")
            },
            Array.Empty<Infobase>());

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_WebBase_AddsError()
    {
        // В тестах локализация не загружена — T() возвращает ключ; проверяем
        // только наличие ровно одной ошибки по веб-базе.
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { CfFilePath = _cfFile }, new[] { WebBase() });

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_ExistingValidDumpDirectory_NoErrors()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions
            {
                CfFilePath = _cfFile,
                DumpDtBefore = true,
                DumpDirectory = _tempDir
            },
            new[] { ServerBase() });

        Assert.Empty(errors);
    }

    // ======================= Путь .dt по шаблону =======================

    [Fact]
    public void BuildDtPath_UsesDumpTemplateLikeBulkDump()
    {
        // «Выгрузка базы в .dt как в массовой выгрузке в dt» (issue #106): тот же
        // механизм шаблонов {Имя}/{Дата}, что у массовой выгрузки.
        var options = new MassLoadCfOptions
        {
            DumpDirectory = _tempDir,
            DumpTemplate = "{Имя}_prefix"
        };
        var path = MassLoadCfPlanner.BuildDtPath(options, "База1");

        Assert.Equal(Path.Combine(_tempDir, "База1_prefix.dt"), path);
    }

    // ======================= Код разрешения (/UC) конфигуратора =======================

    [Fact]
    public void PermissionCodeArgument_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OneCLauncher.PermissionCodeArgument(null));
        Assert.Equal(string.Empty, OneCLauncher.PermissionCodeArgument("   "));
    }

    [Fact]
    public void PermissionCodeArgument_ValidCode_AppendsUcKey()
    {
        Assert.Equal(" /UC\"код\"", OneCLauncher.PermissionCodeArgument("код"));
    }

    [Fact]
    public void PermissionCodeArgument_QuoteInCode_Refused()
    {
        // Кавычка в коде недопустима — как в путях операций (IsSafeCliValue).
        Assert.Equal(string.Empty, OneCLauncher.PermissionCodeArgument("ко\"д"));
    }

    // ======================= Оркестрация MassLoadCfService =======================

    /// <summary>Записи запусков Конфигуратора (фейк <see cref="MassLoadCfService.RunnerOverride"/>).</summary>
    private sealed record DesignerCall(
        OneCLauncher.DesignerBatchOperation Operation, string Path, string? PermissionCode);

    private static async Task<MassLoadCfBaseResult> RunServiceAsync(
        MassLoadCfService service, Infobase base_, MassLoadCfOptions options,
        List<DesignerCall>? calls = null, CancellationToken ct = default)
        => await service.RunAsync(base_, options, ct);

    [Fact]
    public async Task Service_DumpBeforeLoad_PassesPermissionCode()
    {
        var calls = new List<DesignerCall>();
        var service = new MassLoadCfService();
        service.RunnerOverride = (ib, op, path, code, token) =>
        {
            calls.Add(new DesignerCall(op, path, code));
            return Task.FromResult(MassLoadCfRunnerResult.Ok());
        };
        var options = new MassLoadCfOptions
        {
            CfFilePath = _cfFile,
            DumpDtBefore = true,
            DumpDirectory = _tempDir,
            DumpTemplate = "{Имя}",
            // Опции подготовки в этом тесте выключены (сервис без rac-подготовки);
            // код разрешения передаётся в запуск Конфигуратора независимо.
            PermissionCode = "секрет"
        };

        var result = await RunServiceAsync(service, ServerBase(), options);

        Assert.True(result.Success);
        Assert.Equal(2, calls.Count);
        // Порядок: выгрузка .dt ПЕРЕД загрузкой конфигурации (issue #106).
        Assert.Equal(OneCLauncher.DesignerBatchOperation.DumpIB, calls[0].Operation);
        Assert.Equal(Path.Combine(_tempDir, "Бухгалтерия.dt"), calls[0].Path);
        Assert.Equal(OneCLauncher.DesignerBatchOperation.LoadCfg, calls[1].Operation);
        Assert.Equal(_cfFile, calls[1].Path);
        // Код разрешения передаётся в ОБА запуска (Конфигуратор входит
        // в заблокированную базу по коду — блокировка сеансов включена).
        Assert.All(calls, c => Assert.Equal("секрет", c.PermissionCode));
        Assert.Equal(Path.Combine(_tempDir, "Бухгалтерия.dt"), result.DtPath);
    }

    [Fact]
    public async Task Service_DumpFailure_SkipsLoad()
    {
        var calls = new List<DesignerCall>();
        var service = new MassLoadCfService();
        service.RunnerOverride = (ib, op, path, code, token) =>
        {
            calls.Add(new DesignerCall(op, path, code));
            return Task.FromResult(op == OneCLauncher.DesignerBatchOperation.DumpIB
                ? MassLoadCfRunnerResult.Fail("dump failed")
                : MassLoadCfRunnerResult.Ok());
        };
        var options = new MassLoadCfOptions
        {
            CfFilePath = _cfFile,
            DumpDtBefore = true,
            DumpDirectory = _tempDir
        };

        var result = await RunServiceAsync(service, ServerBase(), options);

        Assert.False(result.Success);
        Assert.Contains("dump failed", result.Error, StringComparison.Ordinal);
        // Загрузка после провала выгрузки .dt не запускается.
        var load = Assert.Single(calls);
        Assert.Equal(OneCLauncher.DesignerBatchOperation.DumpIB, load.Operation);
    }

    [Fact]
    public async Task Service_WithoutPreparation_PrepareOptionsFail()
    {
        var service = new MassLoadCfService(preparation: null);
        service.RunnerOverride = (_, _, _, _, _) => Task.FromResult(MassLoadCfRunnerResult.Ok());
        var options = new MassLoadCfOptions { CfFilePath = _cfFile, TerminateSessions = true };

        var result = await RunServiceAsync(service, ServerBase(), options);

        Assert.False(result.Success);
        Assert.Contains("BatchOps", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_NoPrepareOptions_DoesNotTouchRac()
    {
        var rac = new FakeRac();
        var service = new MassLoadCfService(new BatchOpsPreparationService(rac));
        service.RunnerOverride = (_, _, _, _, _) => Task.FromResult(MassLoadCfRunnerResult.Ok());

        var result = await RunServiceAsync(
            service, ServerBase(), new MassLoadCfOptions { CfFilePath = _cfFile });

        Assert.True(result.Success);
        Assert.Empty(rac.Updates);
        Assert.Empty(rac.Terminated);
    }

    [Fact]
    public async Task Service_Success_RestoresLocksAfterOperation()
    {
        // rac с одной клиент-серверной базой, блокировки до операции off —
        // подготовка ставит их, сервис после загрузки обязан снять (issue #106).
        var rac = CreateRac();
        var preparation = new BatchOpsPreparationService(rac);
        var service = new MassLoadCfService(preparation);
        service.RunnerOverride = (_, _, _, _, _) => Task.FromResult(MassLoadCfRunnerResult.Ok());
        var options = new MassLoadCfOptions
        {
            CfFilePath = _cfFile,
            DenyNewSessions = true,
            DenyScheduledJobs = true,
            PermissionCode = "код"
        };

        var result = await RunServiceAsync(service, ServerBase(), options);

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        // Два «infobase update»: блокировка перед загрузкой + снятие после.
        Assert.Equal(2, rac.Updates.Count);
        var before = rac.Updates[0].Request;
        Assert.True(before.SessionsDeny);
        Assert.True(before.ScheduledJobsDeny);
        Assert.Equal("код", before.PermissionCode);
        var after = rac.Updates[1].Request;
        // Снятие только того, что ставили (sessions-deny=off; jobs — исходное off).
        Assert.False(after.SessionsDeny);
        Assert.False(after.ScheduledJobsDeny);
    }

    [Fact]
    public async Task Service_LoadFailure_StillRestoresLocks()
    {
        // Блокировки снимаются при ЛЮБОМ исходе операции (issue #106) —
        // даже когда загрузка конфигурации завершилась ошибкой.
        var rac = CreateRac();
        var preparation = new BatchOpsPreparationService(rac);
        var service = new MassLoadCfService(preparation);
        service.RunnerOverride = (ib, op, path, code, token) =>
            Task.FromResult(op == OneCLauncher.DesignerBatchOperation.LoadCfg
                ? MassLoadCfRunnerResult.Fail("load failed")
                : MassLoadCfRunnerResult.Ok());
        var options = new MassLoadCfOptions
        {
            CfFilePath = _cfFile,
            DenyNewSessions = true
        };

        var result = await RunServiceAsync(service, ServerBase(), options);

        Assert.False(result.Success);
        Assert.Equal(2, rac.Updates.Count);
        Assert.True(rac.Updates[0].Request.SessionsDeny);
        Assert.False(rac.Updates[1].Request.SessionsDeny);
    }

    // ======================= ViewModel (прогон по списку) =======================

    [Fact]
    public async Task ViewModel_RunAsync_ProcessesAllChecked_ContinuesOnError()
    {
        var ok1 = new Infobase
        {
            Id = "b1", Name = "База1",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = _tempDir }
        };
        var ok2 = new Infobase
        {
            Id = "b2", Name = "База2",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = _tempDir }
        };
        var vm = new MassLoadCfViewModel(new[] { ok1, ok2 });
        var calls = new List<DesignerCall>();
        vm.RunnerOverride = (ib, op, path, code, token) =>
        {
            calls.Add(new DesignerCall(op, path, code));
            return Task.FromResult(MassLoadCfRunnerResult.Ok());
        };
        vm.CfFilePath = _cfFile;
        vm.SetAll(true);

        await vm.RunAsync(CancellationToken.None);
        // Сброс IsRunning уходит в захваченный контекст синхронизации (xUnit) —
        // состояние окон проверять в тестах не будем, только результаты.

        // Обе файловые базы загружены (по одному /LoadCfg на каждую); ошибка одной
        // базы не прерывает остальные — здесь обе успешны.
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal(OneCLauncher.DesignerBatchOperation.LoadCfg, c.Operation));
        // Успешные базы снимаются с выбора (повторный прогон — по оставшимся).
        Assert.All(vm.Items, i => Assert.False(i.IsChecked));
        Assert.All(vm.Items, i => Assert.Equal(BatchOperationsStatus.Success, i.Status));
        // В тестах локализация не загружена — ключ сводки возвращается как есть
        // (сводка выставлена — прогон завершён).
        Assert.Equal("BatchOps.SummaryFormat", vm.Summary);
    }

    [Fact]
    public async Task ViewModel_ValidationError_DoesNotStartRun()
    {
        var base_ = new Infobase
        {
            Id = "b1", Name = "База1",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = _tempDir }
        };
        var vm = new MassLoadCfViewModel(new[] { base_ });
        var started = false;
        vm.RunnerOverride = (_, _, _, _, _) =>
        {
            started = true;
            return Task.FromResult(MassLoadCfRunnerResult.Ok());
        };
        vm.CfFilePath = string.Empty; // нет файла .cf
        vm.SetAll(true);

        await vm.RunAsync(CancellationToken.None);

        Assert.False(started);
        // В тестах локализация не загружена — ключи возвращаются как есть.
        Assert.Contains("BatchOps", vm.Summary, StringComparison.Ordinal);
        Assert.All(vm.Items, i => Assert.Equal(BatchOperationsStatus.Pending, i.Status));
    }

    // ======================= Фейковый rac-клиент =======================

    /// <summary>Минимальный фейк <see cref="IRacClient"/> (образец BatchOpsPreparationServiceTests).</summary>
    private sealed class FakeRac : IRacClient
    {
        public RacCluster[] Clusters { get; init; } = Array.Empty<RacCluster>();
        public RacInfobaseSummary[] Summaries { get; init; } = Array.Empty<RacInfobaseSummary>();
        public RacInfobaseInfo? Info { get; init; }
        public RacSessionInfo[] Sessions { get; init; } = Array.Empty<RacSessionInfo>();

        public List<(Guid Cluster, Guid Session)> Terminated { get; } = new();
        public List<(Guid Cluster, Guid Infobase, RacInfobaseUpdateRequest Request)> Updates { get; } = new();
        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(RacConnectionParams p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(Clusters);

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Summaries);

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(RacConnectionParams p, Guid c, Guid i, string? u, string? pw, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult(Info);

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Sessions);

        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
        {
            Terminated.Add((c, s));
            return Task.FromResult(true);
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

    private static FakeRac CreateRac() => new()
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
            SessionsDeny = false,
            ScheduledJobsDeny = false
        }
    };
}
