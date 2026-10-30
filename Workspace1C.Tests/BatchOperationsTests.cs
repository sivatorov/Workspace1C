using System;
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
/// Тесты массовых операций по списку баз (issue #91): чистая логика планирования
/// (<see cref="BatchOperationsService"/>) и цикл прогона ViewModel с подменённым
/// исполнителем (без запуска ibcmd и без процессов).
/// </summary>
public sealed class BatchOperationsTests
{
    // ===================== Планировщик: путь файла .dt =====================

    [Fact]
    public void BuildDtFilePath_UsesSanitizedBaseName()
    {
        // Имя с недопустимыми в имени файла символами очищается; путь собирается
        // из каталога выгрузки + «ИмяБазы.dt».
        var path = BatchOperationsService.BuildDtFilePath(@"C:\dumps", "База: тест/1");

        Assert.Equal(
            Path.Combine(@"C:\dumps", "База_ тест_1.dt"),
            path);
    }

    [Fact]
    public void BuildDtFilePath_EmptyName_FallsBackToBase()
    {
        var path = BatchOperationsService.BuildDtFilePath(@"C:\dumps", "  ");
        Assert.Equal(Path.Combine(@"C:\dumps", "base.dt"), path);
    }

    // ===================== Планировщик: пред-валидация =====================

    [Fact]
    public void Validate_DumpDt_WithoutDirectory_Fails()
    {
        var errors = BatchOperationsService.Validate(
            Array.Empty<Infobase>(), BatchOperationKind.DumpDt, path: null);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_DumpDt_WithMissingDirectory_Fails()
    {
        var errors = BatchOperationsService.Validate(
            Array.Empty<Infobase>(), BatchOperationKind.DumpDt, path: @"Z:\нет\такого\каталога");

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_LoadCf_WithoutExistingFile_Fails()
    {
        var errors = BatchOperationsService.Validate(
            Array.Empty<Infobase>(), BatchOperationKind.LoadConfig, path: @"Z:\нет\такого\файла.cf");

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_WebBase_IsReportedEvenWithValidPaths()
    {
        // Веб-базы ibcmd не поддерживает — ошибка по каждой такой базе ДО старта.
        var web = new Infobase { Name = "Веб", Connection = new ConnectionSettings { Type = ConnectionType.WebServer } };

        var errors = BatchOperationsService.Validate(
            new[] { web }, BatchOperationKind.DumpDt, path: Path.GetTempPath());

        // В тестовом окружении LocalizationManager может возвращать ключи вместо
        // текстов — проверяем, что ошибка по веб-базе ровно одна (до старта).
        Assert.Single(errors);
    }

    [Fact]
    public void Validate_FileBase_WithValidPaths_Passes()
    {
        var file = new Infobase
        {
            Name = "Файловая",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\demo" }
        };

        var errors = BatchOperationsService.Validate(
            new[] { file }, BatchOperationKind.DumpDt, path: Path.GetTempPath());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullTargets_AndNullPath_DoesNotThrow()
    {
        var errors = BatchOperationsService.Validate(null, BatchOperationKind.LoadConfig, null);
        Assert.NotEmpty(errors);
    }

    // ===================== ViewModel: цикл прогона с подменой исполнителя =====================

    private static Infobase FileBase(string name) => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    [Fact]
    public async Task RunAsync_DumpDt_ExecutesPerBase_AndWritesPerBasePaths()
    {
        var a = FileBase("Альфа");
        var b = FileBase("Бета");
        // issue #92: имя файла выгрузки строится по шаблону; для сохранения прежнего
        // поведения «ИмяБазы.dt» задаём шаблон из одного токена {Имя}.
        var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}"
        };

        var calls = new System.Collections.Generic.List<(string Base, string Path)>();
        vm.ExecutorOverride = (ib, path, _) =>
        {
            calls.Add((ib.Name, path));
            return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
        };

        await vm.RunAsync();

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
            Path.GetDirectoryName(c.Path)));
        Assert.Equal(
            new[] { "Альфа.dt", "Бета.dt" },
            calls.Select(c => Path.GetFileName(c.Path)).ToArray());
        Assert.All(vm.Items, i => Assert.Equal(BatchOperationsStatus.Success, i.Status));
        Assert.False(vm.IsRunning);
        // Итоговая строка сформирована (в тестовом окружении локаль может не
        // подгружаться — проверяем только непустоту).
        Assert.False(string.IsNullOrWhiteSpace(vm.Summary));
    }

    [Fact]
    public async Task RunAsync_MixedResults_MarksStatusesAndCounts()
    {
        var ok = FileBase("Ок");
        var bad = FileBase("Плохая");
        var vm = new BatchOperationsViewModel(new[] { ok, bad }, BatchOperationKind.LoadConfig)
        {
            PathText = Path.GetTempPath()
        };
        // Для LoadConfig каталог не важен, но Validate требует существующий ФАЙЛ .cf —
        // создаём временный, чтобы пред-валидация прошла.
        var cf = Path.Combine(Path.GetTempPath(), "batchops_test_" + Guid.NewGuid().ToString("N") + ".cf");
        await File.WriteAllTextAsync(cf, "test");
        try
        {
            vm.PathText = cf;

            vm.ExecutorOverride = (ib, _, _) => Task.FromResult(new IbcmdResult
            {
                Success = ib.Name != "Плохая",
                ExitCode = ib.Name != "Плохая" ? 0 : 1,
                Error = ib.Name != "Плохая" ? null : "ibcmd: ошибка загрузки"
            });

            await vm.RunAsync();

            Assert.Equal(BatchOperationsStatus.Success, vm.Items[0].Status);
            Assert.Equal(BatchOperationsStatus.Failed, vm.Items[1].Status);
            Assert.Contains("ibcmd: ошибка загрузки", vm.Items[1].StatusText, StringComparison.Ordinal);
            // Строки по базам: «[время] «Имя» → путь» (2 шт.); строка старта тоже
            // содержит «», поэтому фильтруем по стрелке целевого пути.
            Assert.Equal(2, vm.LogLines.Count(line => line.Contains("→", StringComparison.Ordinal)));
            Assert.False(string.IsNullOrWhiteSpace(vm.Summary));
            Assert.False(vm.IsRunning);
        }
        finally
        {
            File.Delete(cf);
        }
    }

    [Fact]
    public async Task RunAsync_NothingSelected_DoesNotCallExecutor()
    {
        var ib = FileBase("База");
        var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath()
        };
        vm.Items[0].IsChecked = false;
        var called = false;
        vm.ExecutorOverride = (_, _, _) => { called = true; return Task.FromResult(new IbcmdResult { Success = true }); };

        await vm.RunAsync();

        Assert.False(called);
        Assert.False(vm.IsRunning);
        Assert.Contains(vm.LogLines, l => !string.IsNullOrWhiteSpace(l));
    }

    [Fact]
    public async Task RunAsync_InvalidPath_StopsBeforeFirstExecution()
    {
        var ib = FileBase("База");
        var vm = new BatchOperationsViewModel(new[] { ib }, BatchOperationKind.DumpDt)
        {
            PathText = @"Z:\нет\такого\каталога"
        };
        var called = false;
        vm.ExecutorOverride = (_, _, _) => { called = true; return Task.FromResult(new IbcmdResult { Success = true }); };

        await vm.RunAsync();

        Assert.False(called);
        Assert.False(vm.IsRunning);
        Assert.All(vm.Items, i => Assert.Equal(BatchOperationsStatus.Pending, i.Status));
    }

    [Fact]
    public async Task RunAsync_LoadConfig_PassesSameCfToEveryBase()
    {
        var a = FileBase("Альфа");
        var b = FileBase("Бета");
        var cf = Path.Combine(Path.GetTempPath(), "batchops_test_" + Guid.NewGuid().ToString("N") + ".cf");
        await File.WriteAllTextAsync(cf, "test");
        try
        {
            var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.LoadConfig)
            {
                PathText = cf
            };
            var paths = new System.Collections.Generic.List<string>();
            vm.ExecutorOverride = (_, path, _) =>
            {
                lock (paths) paths.Add(path);
                return Task.FromResult(new IbcmdResult { Success = true });
            };

            await vm.RunAsync();

            Assert.Equal(new[] { cf, cf }, paths);
        }
        finally
        {
            File.Delete(cf);
        }
    }

    [Fact]
    public async Task RunAsync_Cancellation_StopsLoop()
    {
        var a = FileBase("Альфа");
        var b = FileBase("Бета");
        var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath()
        };
        using var cts = new CancellationTokenSource();
        var firstCall = true;
        vm.ExecutorOverride = (ib, _, ct) =>
        {
            if (firstCall)
            {
                firstCall = false;
                return Task.FromResult(new IbcmdResult { Success = true });
            }
            // Вторая база: пользователь нажал «Остановить» во время первой операции.
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new IbcmdResult { Success = true });
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.RunAsync(cts.Token));

        // Первая база успела выполниться успешно, вторая осталась в статусе Running/Skipped.
        Assert.Equal(BatchOperationsStatus.Success, vm.Items[0].Status);
        Assert.NotEqual(BatchOperationsStatus.Success, vm.Items[1].Status);
    }
}
