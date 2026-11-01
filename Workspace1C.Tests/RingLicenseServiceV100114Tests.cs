using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Итерация v1.0.1.14: TTL-кэш пути ring (п.4.1 плана) и диагностика по issue #83
/// («в логе ring найден, а на вкладке Лицензии — Не найдена»). Внешние процессы
/// подменяются точками инъекции RingLicenseService (RingPathProbeAsync /
/// RingPathAlternateProbeAsync / RunProcessAsync / ReadLicenseDirectory) — без запуска
/// утилит и без файловой системы.
/// </summary>
public sealed class RingLicenseServiceV100114Tests
{
    private static RingLicenseService CreateService(
        Func<CancellationToken, Task<string?>>? probe = null,
        Func<string, string[], CancellationToken, Task<ProcessRunResult>>? runProcess = null)
    {
        var service = new RingLicenseService();
        service.RingPathProbeAsync = probe ?? (_ => Task.FromResult<string?>("C:\\bin\\ring.exe"));
        service.RingPathAlternateProbeAsync = _ => Task.FromResult<string?>(null);
        service.RunProcessAsync = runProcess ?? ((_, _, _) => Task.FromResult(new ProcessRunResult()));
        service.ReadLicenseDirectory = () => Array.Empty<ProgramLicense>();
        return service;
    }

    [Fact]
    public async Task ResolveRingPathAsync_CachesPositiveResult_ProbeRunsOnce()
    {
        // п.4.1: где/which + реестр + каталоги — не при каждом опросе монитора (~5 с),
        // а один раз за TTL.
        var service = CreateService();
        var probeCalls = 0;
        service.RingPathProbeAsync = _ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult<string?>("C:\\bin\\ring.exe");
        };

        var first = await service.ResolveRingPathAsync();
        var second = await service.ResolveRingPathAsync();

        Assert.Equal("C:\\bin\\ring.exe", first);
        Assert.Equal(first, second);
        Assert.Equal(1, probeCalls);
    }

    [Fact]
    public async Task ResolveRingPathAsync_CachesNegativeResult_AlternateProbeRunsOnce()
    {
        // Отрицательный результат («ring нет») тоже кэшируется — иначе каждый опрос
        // монитора порождает where.exe + ring --version + реестровый PATH + перебор.
        var service = CreateService();
        var probeCalls = 0;
        var alternateCalls = 0;
        service.RingPathProbeAsync = _ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult<string?>(null);
        };
        service.RingPathAlternateProbeAsync = _ =>
        {
            Interlocked.Increment(ref alternateCalls);
            return Task.FromResult<string?>(null);
        };

        var first = await service.ResolveRingPathAsync();
        var second = await service.ResolveRingPathAsync();

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, probeCalls);
        Assert.Equal(1, alternateCalls);
    }

    [Fact]
    public async Task ResolveRingPathAsync_TtlExpired_ProbeRunsAgain()
    {
        var service = CreateService();
        service.RingPathCacheTtl = TimeSpan.FromMilliseconds(30);
        var probeCalls = 0;
        service.RingPathProbeAsync = _ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult<string?>("C:\\bin\\ring.exe");
        };

        await service.ResolveRingPathAsync();
        Thread.Sleep(60); // TTL истёк.
        await service.ResolveRingPathAsync();

        Assert.Equal(2, probeCalls);
    }

    [Fact]
    public async Task ResolveRingPathAsync_Invalidate_ForcesReprobeWithinTtl()
    {
        var service = CreateService();
        var probeCalls = 0;
        service.RingPathProbeAsync = _ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult<string?>("C:\\bin\\ring.exe");
        };

        await service.ResolveRingPathAsync();
        service.InvalidateRingPathCache();
        await service.ResolveRingPathAsync();

        Assert.Equal(2, probeCalls);
    }

    [Fact]
    public async Task GetInstalledLicensesAsync_StartError_InvalidatesRingPathCache()
    {
        // Кандидат «есть» (кэш положительный), но запустить не удалось (StartError —
        // например, кэшированный путь стал неактуален): кэш сбрасывается, следующий
        // опрос заново резолвит путь, несмотря на неистёкший TTL.
        var service = CreateService();
        service.RingPathCacheTtl = TimeSpan.FromMinutes(5);
        var probeCalls = 0;
        service.RingPathProbeAsync = _ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult<string?>("C:\\stale\\ring.exe");
        };
        service.RunProcessAsync = (_, _, _) =>
            Task.FromResult(new ProcessRunResult { StartError = "C:\\stale\\ring.exe: файл не найден" });

        await service.GetInstalledLicensesAsync();
        await service.GetInstalledLicensesAsync();

        Assert.Equal(2, probeCalls);
    }

    [Fact]
    public async Task GetInstalledLicensesAsync_CandidateSuccessWithNoLicenses_WarningContainsCandidateOutput()
    {
        // issue #83 (репорт 7OH от 2026-10-31 19:20): ring найден, вернул Success с
        // выводом без серийных номеров — на вкладке Warning с выводом кандидата,
        // а не вводящее в заблуждение «утилита не найдена».
        var service = CreateService();
        service.RunProcessAsync = (_, _, _) => Task.FromResult(new ProcessRunResult
        {
            ExitCode = 0,
            StdOut = "some ring output without serial numbers"
        });

        var result = await service.GetInstalledLicensesAsync();

        Assert.Empty(result.Licenses);
        Assert.NotNull(result.Warning);
        Assert.Contains("some ring output without serial numbers", result.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain(BuildUnavailableMarker(), result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInstalledLicensesAsync_CandidateSuccessEmptyOutput_WarningExplainsRingWasFound()
    {
        var service = CreateService();
        service.RunProcessAsync = (_, _, _) => Task.FromResult(new ProcessRunResult
        {
            ExitCode = 0,
            StdOut = string.Empty
        });

        var result = await service.GetInstalledLicensesAsync();

        Assert.Empty(result.Licenses);
        Assert.NotNull(result.Warning);
        Assert.Contains("ring найдена и запущена", result.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain(BuildUnavailableMarker(), result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInstalledLicensesAsync_ParseableOutput_ReturnsLicenses()
    {
        var service = CreateService();
        var runCalls = 0;
        service.RunProcessAsync = (_, _, _) =>
        {
            Interlocked.Increment(ref runCalls);
            return Task.FromResult(new ProcessRunResult
            {
                ExitCode = 0,
                StdOut = "Серийный номер: 01234567-89AB-CDEF-0123-456789ABCDEF"
            });
        };

        var result = await service.GetInstalledLicensesAsync();

        Assert.Equal(1, runCalls); // первый кандидат дал лицензии — остальные не запускаются.
        Assert.Single(result.Licenses);
        Assert.True(string.IsNullOrWhiteSpace(result.Warning));
    }

    [Fact]
    public void BuildNoLicensesMessage_ContainsTruncatedOutput()
    {
        var message = RingLicenseService.BuildNoLicensesMessage("ring: ok");
        Assert.Contains("ring: ok", message, StringComparison.Ordinal);
        Assert.Contains("ring найдена и запущена", message, StringComparison.Ordinal);

        var empty = RingLicenseService.BuildNoLicensesMessage(null);
        Assert.Contains("(пусто)", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInstalledLicensesAsync_LogsEveryCandidateResult_SourceCheck()
    {
        // issue #83: результат каждого кандидата логируется, даже при Success/пустом
        // выводе (файловая проверка по образцу Issue83LicenseTabTests).
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "Workspace1C", "Services", "RingLicenseService.cs"));
        Assert.Contains("RING: кандидат «{source}»", source, StringComparison.Ordinal);
    }

    private static string BuildUnavailableMarker() =>
        // Ключевая фраза BuildUnavailableMessage, которой НЕ должно быть в диагностике
        // «ring найден, но лицензий не получено».
        "не найдена утилита ring";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }
}
