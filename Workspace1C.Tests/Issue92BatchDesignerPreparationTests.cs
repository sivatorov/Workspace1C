using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Tests.TestSupport;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #92 (v1.0.1.34, репорт 7OH: «если выключить службы RAS — выгрузка через
/// конфигуратор всегда с ошибкой»): движку «Конфигуратор» rac не требуется, поэтому
/// фаза подготовки «Перед операцией» (завершение сеансов/блокировки через rac) для
/// Designer-выгрузок НЕ запускается вовсе — rac-ошибка не переводит базу в Skipped.
/// Для движка ibcmd поведение прежнее: сбой подготовки → база пропускается.
/// rac подменяется фейком с ошибкой «Ошибка соединения с сервером» — без процессов.
/// </summary>
public sealed class Issue92BatchDesignerPreparationTests
{
    /// <summary>Фейковый rac-клиент: «server 1C недоступен» на любой вызов кластеров.</summary>
    private sealed class UnavailableRacClient : IRacClient
    {
        public int ClustersCallCount;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams p, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ClustersCallCount);
            throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        }

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams p, Guid c, Guid i, string? u, string? pw,
            CancellationToken ct = default, TimeSpan? timeout = null)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> UpdateClusterAsync(RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> SetJobStateAsync(RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Ошибка соединения с сервером 1С:Предприятия");
        public string LastActionError => string.Empty;
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

    private static BatchOperationsViewModel CreateVm(Infobase ib, string dir) => new(
        new[] { ib }, BatchOperationKind.DumpDt)
    {
        PathText = dir,
        // Заглушка резолвера профиля СУБД — только чтобы pre-валидация пропустила
        // серверную базу без сохранённого профиля (реальный резолв на проверяемых
        // путях не вызывается: у Designer он пропускается, у ibcmd база уходит
        // в Skipped на фазе подготовки раньше).
        DbmsProfileResolver = (_, _) => Task.FromResult<DbmsBackupProfile?>(null),
        ExecutorOverride = (_, _, _) => Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 })
    };

    /// <summary>Помечает все базы выбранными — прогон идёт только по IsChecked (как «Выполнить» в окне).</summary>
    private static void SelectAll(BatchOperationsViewModel vm)
    {
        foreach (var item in vm.Items)
            item.IsChecked = true;
    }

    private static string TempDir()
    {
        var dir = Directory.CreateTempSubdirectory("ws1c_issue92").FullName;
        return dir;
    }

    [Fact]
    public async Task RunAsync_DesignerEngine_RacUnavailable_PreparationIsSkipped_BaseIsDumped()
    {
        var dir = TempDir();
        try
        {
            var rac = new UnavailableRacClient();
            var vm = CreateVm(ServerBase(), dir);
            vm.DumpEngine = BatchDumpEngine.Designer;
            vm.PrepareTerminateSessions = true;
            vm.PrepareDenyScheduledJobs = true;
            vm.PreparationService = new BatchOpsPreparationService(rac);
            SelectAll(vm);

            await vm.RunAsync(CancellationToken.None);

            // Rac-подготовка не запускалась вовсе — «Ошибка соединения с сервером»
            // не блокирует выгрузку: операция выполнена, статус не Skipped.
            var item = Assert.Single(vm.Items);
            Assert.Equal(BatchOperationsStatus.Success, item.Status);
            Assert.Equal(0, rac.ClustersCallCount);
            // В журнале — предупреждение о пропуске подготовки (в тестовом окружении
            // локализация не загружена — возвращается ключ строки).
            var log = AsyncLogAssert.WaitForLine(
                vm.LogLines,
                l => l.Contains("BatchOps.PrepareSkippedDesigner", StringComparison.Ordinal),
                "В журнале ожидалось предупреждение о пропуске подготовки (Designer)");
            Assert.Contains(log, l => l.Contains("BatchOps.PrepareSkippedDesigner", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_IbcmdEngine_RacUnavailable_PreparationFails_BaseIsSkipped()
    {
        var dir = TempDir();
        try
        {
            var rac = new UnavailableRacClient();
            var vm = CreateVm(ServerBase(), dir);
            // Движок ibcmd — по умолчанию; подготовка обязательна, сбой rac → Skipped
            // (прежнее поведение, issue #92 v1.0.1.21).
            vm.PrepareTerminateSessions = true;
            vm.PreparationService = new BatchOpsPreparationService(rac);
            SelectAll(vm);

            await vm.RunAsync(CancellationToken.None);

            var item = Assert.Single(vm.Items);
            Assert.Equal(BatchOperationsStatus.Skipped, item.Status);
            Assert.Equal(0, vm.Items.Count(i => i.Status == BatchOperationsStatus.Success));
            // rac реально опрашивался — подготовка выполнялась.
            Assert.True(rac.ClustersCallCount > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DesignerEngine_FileBase_IsDumpedWithoutRac()
    {
        // Файловая база + движок «Конфигуратор»: rac не нужен ни для чего —
        // выгрузка проходит, предупреждение о пропуске подготовки в журнале.
        var dir = TempDir();
        try
        {
            var rac = new UnavailableRacClient();
            var vm = CreateVm(new Infobase
            {
                Id = "file-1",
                Name = "Архив",
                Connection = new ConnectionSettings
                {
                    Type = ConnectionType.File,
                    FilePath = @"C:\bases\Архив"
                }
            }, dir);
            vm.DumpEngine = BatchDumpEngine.Designer;
            vm.PrepareTerminateSessions = true;
            vm.PreparationService = new BatchOpsPreparationService(rac);
            SelectAll(vm);

            await vm.RunAsync(CancellationToken.None);

            var item = Assert.Single(vm.Items);
            Assert.Equal(BatchOperationsStatus.Success, item.Status);
            Assert.Equal(0, rac.ClustersCallCount);
            var log = AsyncLogAssert.WaitForLine(
                vm.LogLines,
                l => l.Contains("BatchOps.PrepareSkippedDesigner", StringComparison.Ordinal),
                "В журнале ожидалось предупреждение о пропуске подготовки (Designer, файловая база)");
            Assert.Contains(log, l => l.Contains("BatchOps.PrepareSkippedDesigner", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
