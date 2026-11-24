using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (1.0.1.96): «Сделать копию сейчас» наглухо вешало окно.
/// <para>
/// Проверяем: проверки окружения (утилита, каталог, подключение), таймаут и отмену
/// с удалением недописанного файла, отчёт о этапах, защиту от повторного запуска и
/// отсутствие блокировки вызывающего потока. Запуск утилит подменяется
/// <see cref="DbmsBackupService.RunToolOverride"/>, поэтому реальные pg_dump/sqlcmd
/// в тестах не нужны.
/// </para>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143DbmsBackupTests
{
    [Theory]
    [InlineData("DbmsBackupWindow.cs")]
    [InlineData("DbmsBackupWindow.Avalonia.cs")]
    public void ProgressPercent_IsBoundOneWay(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Matches(
            @"new Binding\(nameof\(DbmsBackupViewModel\.ProgressPercent\)\)\s*\{\s*Mode\s*=\s*BindingMode\.OneWay\s*\}",
            source);
    }

    // ===================== Проверка каталога =====================

    [Fact]
    public async Task CheckOutputDirectory_WritableTemp_Succeeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue143_dir_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await DbmsBackupService.CheckOutputDirectoryAsync(Profile(dir));

            Assert.True(result.Success, result.Detail);
            Assert.True(Directory.Exists(dir));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task CheckOutputDirectory_PathIsFile_Fails()
    {
        var file = Path.Combine(Path.GetTempPath(), "issue143_file_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a directory");
        try
        {
            var result = await DbmsBackupService.CheckOutputDirectoryAsync(Profile(file));

            Assert.False(result.Success);
            Assert.Contains(result.MessageKey, new[] { "Dbms.DirUnavailable", "Dbms.NoWriteAccess" });
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ===================== Проверка утилиты =====================

    [Fact]
    public async Task CheckTools_MissingTool_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "issue143_missing_" + Guid.NewGuid().ToString("N"));
        var profile = Profile(Path.GetTempPath());
        profile.ToolsDirectory = missing;

        var result = await DbmsBackupService.CheckToolsAsync(profile);

        Assert.False(result.Success);
        Assert.Equal("Dbms.ToolNotFound", result.MessageKey);
    }

    [Fact]
    public async Task CheckTools_OverrideReportsVersion_Succeeds()
    {
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, _, _, _, _) =>
            Task.FromResult(new ProcessRunResult { ExitCode = 0, StdOut = "pg_dump (PostgreSQL) 16.2" });
        try
        {
            var result = await DbmsBackupService.CheckToolsAsync(Profile(Path.GetTempPath()));

            Assert.True(result.Success, result.Detail);
            Assert.Contains("pg_dump", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
        }
    }

    // ===================== Проверка подключения =====================

    [Fact]
    public async Task CheckConnection_ClosedPort_Fails()
    {
        var profile = Profile(Path.GetTempPath());
        profile.Host = "127.0.0.1";
        profile.Port = ReserveClosedPort();

        var result = await DbmsBackupService.CheckConnectionAsync(profile);

        Assert.False(result.Success);
        Assert.Equal("Dbms.ConnectionFailed", result.MessageKey);
    }

    [Fact]
    public async Task CheckConnection_OpenPort_Succeeds()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var previous = DbmsBackupService.RunToolOverride;
        // pg_isready в окружении может отсутствовать — TCP-проверки достаточно.
        DbmsBackupService.RunToolOverride = (fileName, _, _, _, _) =>
            Task.FromResult(new ProcessRunResult { StartError = fileName + ": not found" });
        try
        {
            var profile = Profile(Path.GetTempPath());
            profile.Host = "127.0.0.1";
            profile.Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var result = await DbmsBackupService.CheckConnectionAsync(profile);

            Assert.True(result.Success, result.Detail);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            listener.Stop();
        }
    }

    // ===================== Запуск копирования =====================

    [Fact]
    public async Task RunAsync_DoesNotBlockCaller()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = async (_, _, _, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new ProcessRunResult();
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            var task = DbmsBackupService.RunAsync(Profile(dir), null, null, cts.Token);

            // Синхронная часть не должна доводить вызов до завершения.
            Assert.False(task.IsCompleted);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task RunAsync_Timeout_ReportsTimedOut_AndDeletesPartialFile()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, _, _, _, _) =>
            Task.FromResult(new ProcessRunResult { TimedOut = true });
        try
        {
            var result = await DbmsBackupService.RunAsync(Profile(dir));

            Assert.False(result.Success);
            Assert.Equal(LocalizationManager.T("Dbms.TimedOut"), result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task RunAsync_Cancellation_DeletesPartialFile()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = async (_, args, _, _, ct) =>
        {
            var list = args.ToList();
            var index = list.IndexOf("-f");
            if (index >= 0 && index + 1 < list.Count)
                await File.WriteAllTextAsync(list[index + 1], "partial", ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new ProcessRunResult();
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => DbmsBackupService.RunAsync(Profile(dir), null, null, cts.Token));

            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsStageSequence()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, args, _, _, _) =>
        {
            var list = args.ToList();
            var index = list.IndexOf("-f");
            if (index >= 0 && index + 1 < list.Count)
                File.WriteAllText(list[index + 1], "backup");
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0; // без ротации
            var progress = new RecordingProgress();

            var result = await DbmsBackupService.RunAsync(profile, null, progress);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(
                new[]
                {
                    DbmsBackupStage.Prepare, DbmsBackupStage.Check,
                    DbmsBackupStage.Dump, DbmsBackupStage.Done
                },
                progress.Stages);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    // ===================== Защита от повторного запуска =====================

    [Fact]
    public async Task ViewModel_SecondRun_IsIgnored()
    {
        var dir = NewTempDirectory();
        var dataDir = NewTempDirectory();
        var previousOverride = DbmsBackupService.RunToolOverride;
        var previousData = FeatureDataStore.DirectoryOverride;
        var calls = 0;

        DbmsBackupService.RunToolOverride = async (_, args, _, _, ct) =>
        {
            Interlocked.Increment(ref calls);
            var list = args.ToList();
            var index = list.IndexOf("-f");
            if (index >= 0 && index + 1 < list.Count)
                await File.WriteAllTextAsync(list[index + 1], "backup", ct);
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            return new ProcessRunResult { ExitCode = 0 };
        };

        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            var vm = CreateViewModel(dir);
            var first = vm.RunAsync();
            var second = vm.RunAsync();

            await Task.WhenAll(first, second);

            Assert.Equal(1, calls);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previousOverride;
            FeatureDataStore.DirectoryOverride = previousData;
            TryDeleteDirectory(dir);
            TryDeleteDirectory(dataDir);
        }
    }

    // ===================== Source-гварды и локализация =====================

    [Theory]
    [InlineData("DbmsBackupWindow.cs")]
    [InlineData("DbmsBackupWindow.Avalonia.cs")]
    public void Windows_HaveChecksAndProgress(string file)
    {
        var code = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains("Dbms.CheckConnection", code, StringComparison.Ordinal);
        Assert.Contains("Dbms.CheckDirectory", code, StringComparison.Ordinal);
        Assert.Contains("ProgressBar", code, StringComparison.Ordinal);
        Assert.Contains("IsProgressIndeterminate", code, StringComparison.Ordinal);
        Assert.Contains("ProgressStageText", code, StringComparison.Ordinal);
        Assert.Contains("CheckConnectionAsync", code, StringComparison.Ordinal);
        Assert.Contains("CheckDirectoryAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_GuardsReentrancy_AndRunsHeavyWorkOffUiThread()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");

        Assert.Contains("Interlocked.Exchange(ref _running", code, StringComparison.Ordinal);
        Assert.Contains("Dbms.Reentrancy", code, StringComparison.Ordinal);
        // issue #143 (1.0.1.115): запись профиля — без sync-over-async, через
        // сторожевой таймаут, не зависящий от пула потоков.
        Assert.Contains("DbmsBackupService.SaveAsync", code, StringComparison.Ordinal);
        Assert.Contains("DbmsTimeoutGuard.RunAsync", code, StringComparison.Ordinal);
        Assert.Contains("TryEnterOperation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveCore", code, StringComparison.Ordinal);
        Assert.Contains("new Progress<DbmsBackupProgress>", code, StringComparison.Ordinal);
        Assert.Contains("TrackElapsedAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_RunsFileSystemAndProcessWorkOffCallerThread()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "DbmsBackupService.cs");

        // issue #143 (1.0.1.115): файловые операции и запуск процессов — только на
        // выделенном потоке (BlockingIo), не на UI-потоке и не на потоке пула.
        Assert.Contains("BlockingIo.Run(() => Directory.CreateDirectory", code, StringComparison.Ordinal);
        Assert.Contains("BlockingIo.RunAsync(() => RunToolOverride", code, StringComparison.Ordinal);
        Assert.Contains("RunToolAsync", code, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupStage.Dump", code, StringComparison.Ordinal);
        Assert.Contains("Dbms.TimedOut", code, StringComparison.Ordinal);
        Assert.Contains("TryDeleteAsync(target", code, StringComparison.Ordinal);
        // Таймаут дампа берётся из профиля, а не жёсткие 6 часов.
        Assert.Contains("profile.TimeoutMinutes", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromHours(6), env", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessRunner_RedirectsStdin_AndCancelsReads()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "ProcessRunner.cs");

        Assert.Contains("RedirectStandardInput = true", code, StringComparison.Ordinal);
        Assert.Contains("process.StandardInput.Close()", code, StringComparison.Ordinal);
        Assert.Contains("CopyToAsync(buffer, ct)", code, StringComparison.Ordinal);
        Assert.Contains("Kill(process)", code, StringComparison.Ordinal);
        Assert.Contains("ProbeAsync", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasIssue143Keys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);
        var keys = new[]
        {
            "Dbms.CheckConnection", "Dbms.CheckDirectory", "Dbms.CheckOk", "Dbms.CheckFailed",
            "Dbms.CheckTimeout", "Dbms.ToolNotFound", "Dbms.NoWriteAccess", "Dbms.DirUnavailable",
            "Dbms.ConnectionFailed", "Dbms.TimedOut", "Dbms.Reentrancy",
            "Dbms.Progress.Prepare", "Dbms.Progress.Check", "Dbms.Progress.Dump",
            "Dbms.Progress.Rotate", "Dbms.Progress.Upload", "Dbms.Progress.Done"
        };

        foreach (var key in keys)
            Assert.Contains("\"" + key + "\"", json, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static DbmsBackupProfile Profile(string dir) => new()
    {
        InfobaseId = "base-1",
        Dbms = DbmsKind.PostgreSql,
        Host = "127.0.0.1",
        Port = 5432,
        Database = "demo",
        User = "postgres",
        Password = "secret",
        OutputDirectory = dir,
        KeepCount = 7
    };

    private static DbmsBackupViewModel CreateViewModel(string outputDirectory)
    {
        var infobase = new Infobase
        {
            Id = "base-1",
            Name = "Demo",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = "demo"
            }
        };
        var vm = new DbmsBackupViewModel(new[] { infobase }, infobase, new DbmsBackupData());
        vm.DbmsIndex = 0;
        vm.Host = "127.0.0.1";
        vm.Port = "5432";
        vm.Database = "demo";
        vm.User = "postgres";
        vm.Password = "secret";
        vm.OutputDirectory = outputDirectory;
        vm.KeepCount = "0";
        return vm;
    }

    private static int ReserveClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue143_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Тестовый каталог мог остаться занятым — на результат не влияет.
        }
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    private sealed class RecordingProgress : IProgress<DbmsBackupProgress>
    {
        public List<DbmsBackupStage> Stages { get; } = new();

        public void Report(DbmsBackupProgress value) => Stages.Add(value.Stage);
    }
}
