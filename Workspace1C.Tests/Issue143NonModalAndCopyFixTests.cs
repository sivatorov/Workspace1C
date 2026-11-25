using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (комментарий RafaIS 2026-11-25):
/// <list type="bullet">
/// <item>подпись «Каталог с программой pg_dump / sqlcmd» вместо неочевидной;</item>
/// <item>ошибка после успешных проверок, файл копии «начал создаваться» и исчез —
/// причина: проверки не подтверждали наличие утилиты, а невнятное сообщение не
/// содержало код возврата/хвост stderr; ротация могла удалить свежий файл;</item>
/// <item>окно должно быть немодальным, со своей плашкой на панели задач, задание — в
/// фоне, отмена — только явной кнопкой (закрытие окна не отменяет копию).</item>
/// </list>
/// Тесты — в коллекции с отключённой параллельностью: подменяют статические
/// <see cref="DbmsBackupService.RunToolOverride"/> и
/// <see cref="DbmsBackupService.SaveOverride"/>.
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143NonModalAndCopyFixTests
{
    // ===================== Немодальное окно =====================

    [Fact]
    public void WpfMenu_ShowsWindowNonModally_WithTaskbarButton()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Features.cs");
        var section = Section(code, "private async void ExecuteDbmsBackup()", "Команды Telegram-бота");

        Assert.Contains("window.Show();", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", section, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = true", section, StringComparison.Ordinal);
        Assert.Contains("_dbmsBackupWindow", section, StringComparison.Ordinal);
        Assert.Contains("existing.Activate()", section, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMenu_ShowsWindowNonModally_WithTaskbarButton()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Features.cs");
        var section = Section(code, "private async void ExecuteDbmsBackup()", "Команды Telegram-бота");

        Assert.Contains("window.Show(owner)", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialogSync", section, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = true", section, StringComparison.Ordinal);
        Assert.Contains("_dbmsBackupWindow", section, StringComparison.Ordinal);
        Assert.Contains("existing.Activate()", section, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DbmsBackupWindow.cs")]
    [InlineData("DbmsBackupWindow.Avalonia.cs")]
    public void Window_HasTaskbarButton_AndCloseDoesNotCancelCopy(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains("ShowInTaskbar = true", source, StringComparison.Ordinal);
        // Закрытие окна не отменяет фоновое задание: отмена — только кнопкой.
        Assert.DoesNotContain("Closed += (_, _) => _vm.Cancel();", source, StringComparison.Ordinal);
        // Подпись каталога утилиты и пояснение к ней.
        Assert.Contains("Dbms.Tools", source, StringComparison.Ordinal);
        Assert.Contains("Dbms.ToolsHint", source, StringComparison.Ordinal);
    }

    // ===================== Подпись каталога утилиты =====================

    [Theory]
    [InlineData("ru.json", "Каталог с программой", "Dbms.ToolsHint")]
    [InlineData("en.json", "Folder with pg_dump", "Dbms.ToolsHint")]
    public void ToolsLabel_MentionsProgramFolder(string file, string expected, string hintKey)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Dbms.Tools\":", json, StringComparison.Ordinal);
        Assert.Contains(expected, json, StringComparison.Ordinal);
        Assert.Contains("\"" + hintKey + "\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.ToolFailed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.FileNotCreated\"", json, StringComparison.Ordinal);
    }

    // ===================== Копия создаётся после успешных проверок =====================

    /// <summary>
    /// Сценарий пользователя: обе проверки проходят успешно, после чего копия
    /// действительно создаётся (а не «ошибка после успешных проверок»).
    /// </summary>
    [Fact]
    public async Task ChecksSucceed_ThenRunCreatesFile()
    {
        var dir = NewTempDirectory();
        var toolsDir = Path.Combine(dir, "tools");
        Directory.CreateDirectory(toolsDir);
        var toolName = OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump";
        File.WriteAllText(Path.Combine(toolsDir, toolName), "stub");
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, args, _, _, _) =>
        {
            // Проверка утилиты (--version) и сам дамп используют одну заглушку.
            if (args.Contains("--version"))
                return Task.FromResult(new ProcessRunResult
                {
                    ExitCode = 0,
                    StdOut = "pg_dump (PostgreSQL) 16.2"
                });
            File.WriteAllText(TargetPath(args), "backup");
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        try
        {
            var profile = Profile(dir);
            profile.ToolsDirectory = toolsDir;
            profile.KeepCount = 0;

            var directoryCheck = await DbmsBackupService.CheckOutputDirectoryAsync(profile);
            var toolsCheck = await DbmsBackupService.CheckToolsAsync(profile);
            Assert.True(directoryCheck.Success, directoryCheck.Detail);
            Assert.True(toolsCheck.Success, toolsCheck.Detail);

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.True(result.Success, result.ErrorMessage);
            var created = Assert.Single(result.CreatedFiles);
            Assert.True(File.Exists(created), "Копия не создана после успешных проверок.");
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Run_Success_CreatesFile_AndRotationKeepsIt()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, args, _, _, _) =>
        {
            var path = TargetPath(args);
            File.WriteAllText(path, "backup");
            // Имитируем неточную отметку времени на сетевом/FAT-каталоге: свежий
            // файл выглядит «старее» прежних копий. Ротация не должна его удалять.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 2;
            for (var i = 0; i < 3; i++)
            {
                var old = Path.Combine(dir,
                    DbmsBackupService.BuildFilePrefix(profile) + $"2020-01-0{i + 1}_000000.backup");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow);
            }

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.True(result.Success, result.ErrorMessage);
            var created = Assert.Single(result.CreatedFiles);
            Assert.True(File.Exists(created), "Созданный файл копии удалён ротацией.");
            Assert.Equal(3, Directory.GetFiles(dir).Length);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Rotation_KeepsProtectedFile_EvenWhenItLooksOldest()
    {
        var dir = NewTempDirectory();
        try
        {
            const string prefix = "db_";
            var target = Path.Combine(dir, prefix + "new.backup");
            File.WriteAllText(target, "new");
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(-10));
            for (var i = 0; i < 3; i++)
            {
                var old = Path.Combine(dir, prefix + $"old{i}.backup");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow);
            }

            var removed = BackupRotation.Apply(dir, prefix, ".backup", 1, 0, target);

            Assert.True(File.Exists(target), "Защищённый свежий файл удалён ротацией.");
            Assert.NotEmpty(removed);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Run_ToolFailsAfterPartialWrite_RemovesFile_AndReportsReason()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        DbmsBackupService.RunToolOverride = (_, args, _, _, _) =>
        {
            // Утилита успела создать файл и упала: недописанный файл должен быть
            // удалён, а сообщение — содержать код возврата и причину из stderr.
            File.WriteAllText(TargetPath(args), "partial");
            return Task.FromResult(new ProcessRunResult
            {
                ExitCode = 2,
                StdErr = "pg_dump: error: password authentication failed for user \"postgres\""
            });
        };
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0;

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.False(result.Success);
            Assert.Empty(Directory.GetFiles(dir));
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("password authentication failed", result.ErrorMessage!, StringComparison.Ordinal);
            Assert.Contains("2", result.ErrorMessage!, StringComparison.Ordinal);
            Assert.Contains(dir, result.ErrorMessage!, StringComparison.Ordinal);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Run_SuccessButFileMissing_ReportsConcreteReason()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        // Код возврата 0, но файл не создан (например, утилита «чужой» версии).
        DbmsBackupService.RunToolOverride = (_, _, _, _, _) =>
            Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        try
        {
            var profile = Profile(dir);
            profile.KeepCount = 0;

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.False(result.Success);
            Assert.Empty(Directory.GetFiles(dir));
            Assert.NotNull(result.ErrorMessage);
            Assert.NotEqual(LocalizationManager.T("Dbms.Failed"), result.ErrorMessage);
            // В тестовом окружении локализация может не загружаться — T вернёт
            // ключ; принимаем и ключ, и отформатированное сообщение с путём.
            Assert.True(
                result.ErrorMessage!.Contains("Dbms.FileNotCreated", StringComparison.Ordinal) ||
                result.ErrorMessage.Contains(dir, StringComparison.Ordinal),
                result.ErrorMessage);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Run_MissingTool_ReportsConcretePath_WithoutStartingDump()
    {
        var dir = NewTempDirectory();
        var missingTools = Path.Combine(dir, "no-such-tools");
        try
        {
            var profile = Profile(dir);
            profile.ToolsDirectory = missingTools;
            profile.KeepCount = 0;

            var result = await DbmsBackupService.RunAsync(profile);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("no-such-tools", result.ErrorMessage!, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    /// <summary>
    /// Сбой вспомогательной записи профиля (ограниченный пользователь, недоступный
    /// каталог профиля) не должен отменять саму копию: проверки уже прошли, файл
    /// создаётся по текущим параметрам.
    /// </summary>
    [Fact]
    public async Task ViewModel_SaveFailure_DoesNotBlockCopy()
    {
        var dir = NewTempDirectory();
        var dataDir = NewTempDirectory();
        var previousTool = DbmsBackupService.RunToolOverride;
        var previousSave = DbmsBackupService.SaveOverride;
        var previousData = FeatureDataStore.DirectoryOverride;
        DbmsBackupService.RunToolOverride = (_, args, _, _, _) =>
        {
            File.WriteAllText(TargetPath(args), "backup");
            return Task.FromResult(new ProcessRunResult { ExitCode = 0 });
        };
        DbmsBackupService.SaveOverride = (_, _) =>
            Task.FromException(new IOException("access denied"));
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            var vm = CreateViewModel(dir);

            await vm.RunAsync();

            Assert.False(vm.IsBusy);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previousTool;
            DbmsBackupService.SaveOverride = previousSave;
            FeatureDataStore.DirectoryOverride = previousData;
            TryDeleteDirectory(dir);
            TryDeleteDirectory(dataDir);
        }
    }

    // ===================== Повторный вход (немодальное окно) =====================

    [Fact]
    public async Task Service_SecondRunWhileFirstActive_ReturnsReentrancy()
    {
        var dir = NewTempDirectory();
        var previous = DbmsBackupService.RunToolOverride;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DbmsBackupService.RunToolOverride = async (_, _, _, _, ct) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessRunResult();
        };
        using var cts = new CancellationTokenSource();
        try
        {
            var first = DbmsBackupService.RunAsync(Profile(dir), null, null, cts.Token);
            try
            {
                Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                    "Первый запуск не дошёл до утилиты.");

                var second = await DbmsBackupService.RunAsync(Profile(dir));

                Assert.False(second.Success);
                Assert.Equal(LocalizationManager.T("Dbms.Reentrancy"), second.ErrorMessage);
            }
            finally
            {
                // Снимаем барьер при любом исходе проверок, иначе следующий тест
                // получит «копирование уже выполняется».
                cts.Cancel();
                try { await first.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }

            Assert.True(first.IsCanceled || first.IsFaulted);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task ViewModel_SecondInstance_DoesNotStartSecondCopy()
    {
        var dir = NewTempDirectory();
        var dataDir = NewTempDirectory();
        var previousTool = DbmsBackupService.RunToolOverride;
        var previousSave = DbmsBackupService.SaveOverride;
        var previousData = FeatureDataStore.DirectoryOverride;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        DbmsBackupService.RunToolOverride = async (_, _, _, _, ct) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
            return new ProcessRunResult();
        };
        DbmsBackupService.SaveOverride = (_, _) => Task.CompletedTask;
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            var first = CreateViewModel(dir);
            var second = CreateViewModel(dir);

            var firstRun = first.RunAsync();
            try
            {
                Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                    "Первый экземпляр окна не дошёл до утилиты.");

                await second.RunAsync();

                Assert.Equal(1, calls);
                // Журнал окна может обновляться через SynchronizationContext —
                // даём записи доехать.
                Assert.True(WaitFor(
                    () => second.LogText.Contains(LocalizationManager.T("Dbms.Reentrancy"), StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3)), second.LogText);
                Assert.False(second.IsBusy);
            }
            finally
            {
                // Отмена — только явной кнопкой; в тесте снимаем барьер при любом
                // исходе, чтобы следующий тест не получил «копирование уже идёт».
                first.Cancel();
                try { await firstRun.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }
            Assert.False(first.IsBusy);
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previousTool;
            DbmsBackupService.SaveOverride = previousSave;
            FeatureDataStore.DirectoryOverride = previousData;
            TryDeleteDirectory(dir);
            TryDeleteDirectory(dataDir);
        }
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

    /// <summary>Путь копии из аргументов pg_dump (<c>-f</c>) или sqlcmd (<c>-Q</c>).</summary>
    private static string TargetPath(IReadOnlyList<string> args)
    {
        var list = args.ToList();
        var index = list.IndexOf("-f");
        if (index >= 0 && index + 1 < list.Count)
            return list[index + 1];
        var queryIndex = list.IndexOf("-Q");
        if (queryIndex >= 0 && queryIndex + 1 < list.Count)
        {
            var query = list[queryIndex + 1];
            var marker = "TO DISK = N'";
            var start = query.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                var end = query.IndexOf('\'', start);
                if (end > start)
                    return query.Substring(start, end - start).Replace("''", "'");
            }
        }
        throw new InvalidOperationException("Не найден путь копии в аргументах: " + string.Join(" ", args));
    }

    private static string Section(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        }
        return condition();
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
}
