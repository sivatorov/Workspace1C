using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// issue #143 (1.0.1.96): <see cref="ProcessRunner"/> не должен позволять дочернему
/// процессу «зависнуть» на вводе, а таймаут и отмена обязаны завершать его вместе
/// с чтением вывода.
/// </summary>
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ClosesStandardInput_SoChildCannotWaitForInput()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "set /p x= & echo done" })
            : ("sh", new[] { "-c", "read x; echo done" });

        var watch = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(fileName, args, timeout: TimeSpan.FromSeconds(10));
        watch.Stop();

        Assert.False(result.TimedOut, "Процесс не должен ждать ввод: stdin закрывается сразу.");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15),
            $"Запуск занял слишком много времени: {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndReturnsTimedOut()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "ping -n 30 127.0.0.1" })
            : ("sh", new[] { "-c", "sleep 30" });

        var watch = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(fileName, args, timeout: TimeSpan.FromSeconds(1));
        watch.Stop();

        Assert.True(result.TimedOut, "Ожидался TimedOut.");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20),
            $"Таймаут не прервал процесс: {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunAsync_Cancellation_ThrowsOperationCanceled()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "ping -n 30 127.0.0.1" })
            : ("sh", new[] { "-c", "sleep 30" });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProcessRunner.RunAsync(fileName, args, timeout: TimeSpan.FromMinutes(5), ct: cts.Token));
    }

    [Fact]
    public async Task RunAsync_CapturesOutput()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "echo issue143" })
            : ("sh", new[] { "-c", "echo issue143" });

        var result = await ProcessRunner.RunAsync(fileName, args, timeout: TimeSpan.FromSeconds(10));

        Assert.True(result.Success);
        Assert.Contains("issue143", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_MissingTool_ReturnsStartError()
    {
        var missing = OperatingSystem.IsWindows()
            ? @"Z:\definitely-missing-issue143\pg_dump.exe"
            : "/definitely-missing-issue143/pg_dump";

        var result = await ProcessRunner.ProbeAsync(missing, new[] { "--version" });

        Assert.NotNull(result.StartError);
        Assert.False(result.Success);
    }
}
