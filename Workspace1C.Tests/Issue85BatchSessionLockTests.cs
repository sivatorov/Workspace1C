using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #85 (v1.0.1.32): пакетные Блокировка/Разблокировка сеансов в окне
/// массовых операций (запрос leonvlas: «команда "Блокировка" (разблокировка) сеансов
/// информационных баз могла применяться к группам или к выделенным строкам») —
/// виды операции, пред-валидация (путь не нужен, веб-базы не поддерживаются,
/// профиль СУБД не требуется) и прогон по списку с журналом «N из M».
/// </summary>
public class Issue85BatchSessionLockTests
{
    private static Infobase FileBase(string name = "Файловая") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    private static Infobase ServerBase(string name = "Серверная") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv-01:27545", DatabaseName = name }
    };

    private static Infobase WebBase(string name = "Веб") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://srv/app" }
    };

    // ---------- Виды операции: заголовки/подсказки ----------

    [Theory]
    [InlineData(BatchOperationKind.LockSessions, "BatchOps.TitleLockSessions", "BatchOps.HintLockSessions")]
    [InlineData(BatchOperationKind.UnlockSessions, "BatchOps.TitleUnlockSessions", "BatchOps.HintUnlockSessions")]
    public void TitleAndHintKeys_ForSessionLockKinds(BatchOperationKind kind, string expectedTitle, string expectedHint)
    {
        Assert.Equal(expectedTitle, BatchOperationsViewModel.TitleKeyForKind(kind));
        Assert.Equal(expectedHint, BatchOperationsViewModel.HintKeyForKind(kind));
    }

    [Fact]
    public void SessionLockKinds_Properties()
    {
        // Блокировкам путь не нужен; это не выгрузки (шаблон/движок скрываются).
        var lockVm = new BatchOperationsViewModel(new[] { ServerBase() }, BatchOperationKind.LockSessions);
        Assert.True(lockVm.IsSessionLockOperation);
        Assert.False(lockVm.IsDumpKind);
        Assert.False(lockVm.NeedsPath);

        var unlockVm = new BatchOperationsViewModel(new[] { ServerBase() }, BatchOperationKind.UnlockSessions);
        Assert.True(unlockVm.IsSessionLockOperation);
        Assert.False(unlockVm.NeedsPath);

        var dumpVm = new BatchOperationsViewModel(new[] { ServerBase() }, BatchOperationKind.DumpDt);
        Assert.False(dumpVm.IsSessionLockOperation);
        Assert.True(dumpVm.NeedsPath);
    }

    // ---------- Пред-валидация ----------

    [Fact]
    public void Validate_LockSessions_PathNotRequired()
    {
        // Путь (каталог/файл .cf) блокировкам не нужен — ошибки пути быть не должно.
        var errors = BatchOperationsService.Validate(
            new[] { FileBase(), ServerBase() }, BatchOperationKind.LockSessions, path: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_UnlockSessions_PathNotRequired()
    {
        var errors = BatchOperationsService.Validate(
            new[] { FileBase() }, BatchOperationKind.UnlockSessions, path: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_LockSessions_ServerBaseWithoutDbmsProfile_NotRequired()
    {
        // В отличие от выгрузок, блокировке сеансов профиль СУБД не нужен.
        var server = ServerBase();
        var errors = BatchOperationsService.Validate(
            new[] { server }, BatchOperationKind.LockSessions, path: null,
            allowMissingDbmsProfiles: false);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_LockSessions_WebBase_ReportedWithLockMessage()
    {
        // В тестах LocalizationManager возвращает сырой ключ — проверяем именно
        // ключ сообщения о блокировке (не общий «веб-базы не поддерживаются ibcmd»).
        var web = WebBase();
        var errors = BatchOperationsService.Validate(
            new[] { web }, BatchOperationKind.LockSessions, path: null);

        Assert.Equal("BatchOps.WebLockNotSupportedFormat", Assert.Single(errors));
    }

    // ---------- Прогон по списку ----------

    [Fact]
    public async Task RunAsync_LockSessions_ExecutorReceivesEmptyPath()
    {
        // Исполнитель вызывается по каждой отмеченной базе с ПУСТЫМ путём
        // (блокировке путь не нужен); успешные базы снимаются с выбора.
        var a = FileBase("Альфа");
        var b = FileBase("Бета");
        var calls = new List<(string Name, string Path)>();
        var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.LockSessions)
        {
            ExecutorOverride = (ib, path, _) =>
            {
                calls.Add((ib.Name, path));
                return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
            }
        };
        vm.Items.First(i => i.Infobase.Name == "Альфа").IsChecked = true;
        vm.Items.First(i => i.Infobase.Name == "Бета").IsChecked = true;

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal(string.Empty, c.Path));
        Assert.All(vm.Items, i => Assert.Equal(BatchOperationsStatus.Success, i.Status));
        Assert.False(vm.IsRunning);
        Assert.False(string.IsNullOrEmpty(vm.Summary));
    }

    [Fact]
    public async Task RunAsync_LockSessions_FailureLogged()
    {
        // Ошибка по базе (например, rac недоступен) — статус Failed, причина в
        // статусе строки; остальные базы продолжаются (журнал «N из M»).
        var ok = FileBase("Ок");
        var bad = FileBase("Плохая");
        var vm = new BatchOperationsViewModel(new[] { ok, bad }, BatchOperationKind.LockSessions)
        {
            ExecutorOverride = (ib, _, _) => Task.FromResult(
                ib.Name == "Плохая"
                    ? IbcmdResult.Fail("rac: ошибка блокировки")
                    : new IbcmdResult { Success = true })
        };
        foreach (var item in vm.Items)
            item.IsChecked = true;

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(BatchOperationsStatus.Success, vm.Items.First(i => i.Infobase.Name == "Ок").Status);
        var failedItem = vm.Items.First(i => i.Infobase.Name == "Плохая");
        Assert.Equal(BatchOperationsStatus.Failed, failedItem.Status);
        Assert.Contains("rac: ошибка блокировки", failedItem.StatusText);
    }
}
