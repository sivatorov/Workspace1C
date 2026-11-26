using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (1.0.1.125): «ошибка после 3 минут ожидания» при создании копии
/// средствами СУБД и отсутствие UI-переключателя диагностики CM_DEBUG_DBMS.
/// <para>
/// Проверяем: копия не обрывается жёстким таймаутом (0 — без ограничения),
/// настраиваемый таймаут доходит до утилиты, отмена пользователем завершает
/// операцию и удаляет недописанный файл, настройка диагностики сохраняется в
/// <c>settings.json</c> и включает <see cref="DbmsBackupTrace"/>, а подписи и
/// переключатель есть в WPF и Avalonia.
/// </para>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143BackupTimeoutTests
{
    // ===================== Таймаут копирования =====================

    [Fact]
    public async Task RunAsync_WithoutLimit_DoesNotPassTimeout_AndWaitsForLongCopy()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        TimeSpan? captured = TimeSpan.FromSeconds(1);
        DbmsBackupService.RunToolOverride = async (_, args, timeout, _, ct) =>
        {
            captured = timeout;
            var target = TargetFromArguments(args);
            if (target is not null)
                await File.WriteAllTextAsync(target, "backup", ct);
            // Долгая копия: без жёсткого лимита она не должна обрываться.
            await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
            return new ProcessRunResult { ExitCode = 0 };
        };
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0;
            Assert.Equal(0, profile.TimeoutMinutes);

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Null(captured);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task RunAsync_ConfiguredTimeout_IsPassedToUtility()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        TimeSpan? captured = null;
        DbmsBackupService.RunToolOverride = (_, args, timeout, _, _) =>
        {
            captured = timeout;
            var target = TargetFromArguments(args);
            if (target is not null)
                File.WriteAllText(target, "backup");
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0;
            profile.TimeoutMinutes = 45;

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(TimeSpan.FromMinutes(45), captured);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Normalize_NegativeTimeout_MeansUnlimited()
    {
        var profile = Profile(Path.GetTempPath());
        profile.TimeoutMinutes = -5;

        DbmsBackupService.Normalize(profile);

        Assert.Equal(0, profile.TimeoutMinutes);
    }

    [Fact]
    public async Task RunAsync_UserCancellation_StopsOperation_AndDeletesPartialFile()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DbmsBackupService.RunToolOverride = async (_, args, _, _, ct) =>
        {
            var target = TargetFromArguments(args);
            if (target is not null)
                await File.WriteAllTextAsync(target, "partial", ct);
            started.TrySetResult(true);
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new ProcessRunResult { ExitCode = 0 };
        };
        using var cts = new CancellationTokenSource();
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0;
            var task = DbmsBackupService.RunAsync(profile, null, null, cts.Token);
            await started.Task;
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    // ===================== Диагностика CM_DEBUG_DBMS =====================

    [Fact]
    public void ApplySettings_EnablesDiagnostics_ThroughEnabledOverride()
    {
        var previous = DbmsBackupTrace.EnabledOverride;
        try
        {
            DbmsBackupTrace.ApplySettings(true);
            Assert.True(DbmsBackupTrace.EnabledOverride);
            Assert.True(DbmsBackupTrace.IsEnabled);

            DbmsBackupTrace.ApplySettings(false);
            Assert.Null(DbmsBackupTrace.EnabledOverride);
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previous;
        }
    }

    [Fact]
    public void Settings_PersistDiagnosticsToggle_AndApplyOnLoad()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupTrace.EnabledOverride;
        try
        {
            DbmsBackupTrace.EnabledOverride = null;
            var repository = new InfobaseRepository(directory: dir);
            repository.SaveSettings(new AppSettings { DbmsBackupDiagnosticsEnabled = true });

            Assert.True(DbmsBackupTrace.EnabledOverride);

            // Новый экземпляр репозитория читает сохранённый файл и применяет настройку.
            DbmsBackupTrace.EnabledOverride = null;
            var loaded = new InfobaseRepository(directory: dir).LoadSettings();

            Assert.True(loaded.DbmsBackupDiagnosticsEnabled);
            Assert.True(DbmsBackupTrace.EnabledOverride);
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    // ===================== Локализация и паритет WPF/Avalonia =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasTimeoutAndDiagnosticsKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);
        var keys = new[]
        {
            "Dbms.TimeoutMinutes",
            "Dbms.TimeoutMinutesUnit",
            "Dbms.TimeoutMinutesHint",
            "Settings.Logging.DbmsDiagnostics",
            "Settings.Logging.DbmsDiagnosticsDesc"
        };

        foreach (var key in keys)
            Assert.Contains("\"" + key + "\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DbmsBackupWindow.cs")]
    [InlineData("DbmsBackupWindow.Avalonia.cs")]
    public void BackupWindows_HaveConfigurableTimeout(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains(nameof(Workspace1C.ViewModels.DbmsBackupViewModel.TimeoutMinutes), source, StringComparison.Ordinal);
        Assert.Contains("Dbms.TimeoutMinutesHint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindows_HaveDbmsDiagnosticsToggle_Parity()
    {
        var wpfXaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        var wpfCode = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Logging.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs");

        Assert.Contains("DbmsBackupDiagnosticsCheck", wpfXaml, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupDiagnosticsCheck", wpfCode, StringComparison.Ordinal);
        Assert.Contains("Settings.Logging.DbmsDiagnostics", wpfXaml, StringComparison.Ordinal);
        // Avalonia: поле переключателя называется _dbmsBackupDiagnosticsCheck.
        Assert.Contains("_dbmsBackupDiagnosticsCheck", avalonia, StringComparison.Ordinal);
        Assert.Contains("Settings.Logging.DbmsDiagnostics", avalonia, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupDiagnosticsEnabled", wpfCode, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupDiagnosticsEnabled", avalonia, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static string? TargetFromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "-f", StringComparison.Ordinal))
                return args[i + 1];
        }
        return null;
    }

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
        KeepCount = 0
    };

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue143_timeout_" + Guid.NewGuid().ToString("N"));
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
}
