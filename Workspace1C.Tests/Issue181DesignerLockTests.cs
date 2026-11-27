using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #181 «Необходимо предупреждение при выгрузке в .CF о том, что открыт
/// конфигуратор». Пакетный запуск конфигуратора уже умел определять блокировку,
/// но вызывающий код терял причину и показывал generic-ошибку (WPF) либо молчал
/// (Avalonia). Здесь проверяются:
/// <list type="bullet">
/// <item>чистая классификация причины (конфигуратор запущен / другая операция /
/// занятая база / занятый файл выгрузки) по локализованным текстам и логу 1С;</item>
/// <item>нормализация токена базы и сравнение с командной строкой процесса
/// (файловая и клиент-серверная формы);</item>
/// <item>определение занятости файла/базы;</item>
/// <item>source-scan: команды выгрузки используют перегрузку с <c>out startError</c>
/// и показывают понятное предупреждение (WPF и Avalonia);</item>
/// <item>наличие ключей локализации в ru/en.</item>
/// </list>
/// </summary>
public sealed class Issue181DesignerLockTests : IDisposable
{
    private readonly string _tempDir;

    public Issue181DesignerLockTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "issue181_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    // ===================== Классификация причины =====================

    [Fact]
    public void Classify_ConfiguratorRunning_FromRussianAndEnglishTexts()
    {
        Assert.Equal(DesignerFailureKind.ConfiguratorRunning,
            DesignerLockClassifier.Classify(
                "Запуск конфигуратора для операции невозможен.\n\nКонфигуратор этой базы уже запущен."));
        Assert.Equal(DesignerFailureKind.ConfiguratorRunning,
            DesignerLockClassifier.Classify(
                "The configurator for this infobase is already running."));
    }

    [Fact]
    public void Classify_AnotherOperation_IsDetected()
    {
        Assert.Equal(DesignerFailureKind.AnotherOperation,
            DesignerLockClassifier.Classify(
                "Уже запущена другая выгрузка или операция конфигуратора (процесс 1cv8.exe)."));
        Assert.Equal(DesignerFailureKind.AnotherOperation,
            DesignerLockClassifier.Classify(
                "Another export or configurator operation is already running (process 1cv8)."));
    }

    [Fact]
    public void Classify_BaseLocked_From1CLogMessages()
    {
        Assert.Equal(DesignerFailureKind.BaseLocked,
            DesignerLockClassifier.Classify(
                "Ошибка получения монопольного доступа к информационной базе."));
        Assert.Equal(DesignerFailureKind.BaseLocked,
            DesignerLockClassifier.Classify("Не удалось заблокировать файл базы данных."));
        Assert.Equal(DesignerFailureKind.BaseLocked,
            DesignerLockClassifier.Classify("The database file is in use by another user."));
    }

    [Fact]
    public void Classify_OutputFileLocked_IsDetected()
    {
        Assert.Equal(DesignerFailureKind.OutputFileLocked,
            DesignerLockClassifier.Classify(
                "Файл назначения занят другой программой: C:\\out\\base.cf"));
        Assert.Equal(DesignerFailureKind.OutputFileLocked,
            DesignerLockClassifier.Classify("The output file is locked by another program."));
    }

    [Fact]
    public void Classify_UnknownOrEmpty_IsOtherOrNone()
    {
        Assert.Equal(DesignerFailureKind.None, DesignerLockClassifier.Classify(null));
        Assert.Equal(DesignerFailureKind.None, DesignerLockClassifier.Classify("   "));
        Assert.Equal(DesignerFailureKind.Other, DesignerLockClassifier.Classify("Не найден 1cv8.exe"));
    }

    [Fact]
    public void IsLockRelated_CoversAllBlockingKinds()
    {
        Assert.True(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.ConfiguratorRunning));
        Assert.True(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.AnotherOperation));
        Assert.True(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.BaseLocked));
        Assert.True(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.OutputFileLocked));
        Assert.False(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.Other));
        Assert.False(DesignerLockClassifier.IsLockRelated(DesignerFailureKind.None));
    }

    // ===================== Токен базы и командная строка =====================

    [Theory]
    [InlineData(@"C:\Bases\Base", @"C:\Bases\Base")]
    [InlineData(@"C:\Bases\Base\", @"C:\Bases\Base")]
    [InlineData("File=\"C:\\Bases\\Base\"", @"C:\Bases\Base")]
    public void NormalizeBaseToken_UnifiesForms(string input, string expected)
    {
        Assert.Equal(expected.ToLowerInvariant(), DesignerLockClassifier.NormalizeBaseToken(input));
    }

    [Fact]
    public void CommandLineMatchesBase_FileFormAndClientServerForm()
    {
        const string fileCommand =
            "\"C:\\Program Files\\1cv8\\8.3.27.2325\\bin\\1cv8.exe\" DESIGNER /F\"C:\\Bases\\Base\" /DisableStartupDialogs";
        Assert.True(DesignerLockClassifier.CommandLineMatchesBase(fileCommand, @"C:\Bases\Base"));

        const string serverCommand =
            "\"1cv8.exe\" DESIGNER /S\"srv-1c\\trade\" /DisableStartupDialogs";
        Assert.True(DesignerLockClassifier.CommandLineMatchesBase(serverCommand, @"srv-1c\trade"));

        const string srvRefCommand =
            "\"1cv8.exe\" DESIGNER Srvr=\"srv-1c\";Ref=\"trade\";";
        Assert.True(DesignerLockClassifier.CommandLineMatchesBase(srvRefCommand, @"srv-1c\trade"));

        Assert.False(DesignerLockClassifier.CommandLineMatchesBase(fileCommand, @"C:\Bases\Other"));
        Assert.False(DesignerLockClassifier.CommandLineMatchesBase(null, @"C:\Bases\Base"));
    }

    // ===================== Занятость файлов =====================

    [Fact]
    public void IsFileLocked_FreeAndMissingFilesAreNotLocked()
    {
        Assert.False(DesignerLockClassifier.IsFileLocked(null));
        Assert.False(DesignerLockClassifier.IsFileLocked(""));
        Assert.False(DesignerLockClassifier.IsFileLocked(Path.Combine(_tempDir, "missing.cf")));

        var free = Path.Combine(_tempDir, "free.cf");
        File.WriteAllText(free, "test");
        Assert.False(DesignerLockClassifier.IsFileLocked(free));
    }

    [Fact]
    public void IsFileLocked_HeldFileIsReportedLocked()
    {
        var path = Path.Combine(_tempDir, "held.cf");
        File.WriteAllText(path, "test");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var locked = DesignerLockClassifier.IsFileLocked(path);

        // На Unix .NET реализует FileShare через advisory-блокировки, поведение
        // зависит от ФС; строгую проверку выполняем на Windows (целевая ОС релиза).
        if (OperatingSystem.IsWindows())
            Assert.True(locked);
    }

    [Fact]
    public void IsInfobaseLocked_NonFileBaseAndMissingMarkerAreNotLocked()
    {
        Assert.False(DesignerLockClassifier.IsInfobaseLocked(null));
        Assert.False(DesignerLockClassifier.IsInfobaseLocked(new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv", DatabaseName = "db" }
        }));
        Assert.False(DesignerLockClassifier.IsInfobaseLocked(new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = _tempDir }
        }));
    }

    [Fact]
    public void IsInfobaseLocked_FileBaseMarkerIsChecked()
    {
        var marker = Path.Combine(_tempDir, "1Cv8.1CD");
        File.WriteAllText(marker, "db");

        var infobase = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = _tempDir }
        };

        Assert.False(DesignerLockClassifier.IsInfobaseLocked(infobase));

        using var hold = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var locked = DesignerLockClassifier.IsInfobaseLocked(infobase);
        if (OperatingSystem.IsWindows())
            Assert.True(locked);
    }

    // ===================== Вызывающий код (source-scan) =====================

    [Fact]
    public void WpfDumpCommands_UseOutErrorAndShowWarning()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Tools.cs");

        Assert.Contains("StartDesignerDump(", code, StringComparison.Ordinal);
        Assert.Contains("out var startError", code, StringComparison.Ordinal);
        Assert.Contains("DesignerLockClassifier.Classify(startError)", code, StringComparison.Ordinal);
        Assert.Contains("Launcher.DumpBlockedWarningTitle", code, StringComparison.Ordinal);
        Assert.Contains("Launcher.DumpBlockedWarningBody", code, StringComparison.Ordinal);
        Assert.Contains("_dialogs.ShowWarning", code, StringComparison.Ordinal);
        Assert.Contains("Launcher.DumpRetryPrompt", code, StringComparison.Ordinal);
        Assert.Contains("CacheFolderOpener.TryOpenDirectory", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDumpCommands_UseOutErrorAndShowWarning()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Launch.cs");

        Assert.Contains("StartDesignerDump(", code, StringComparison.Ordinal);
        Assert.Contains("out var startError", code, StringComparison.Ordinal);
        Assert.Contains("Launcher.DumpBlockedWarningTitle", code, StringComparison.Ordinal);
        Assert.Contains("_dialog.ShowWarning", code, StringComparison.Ordinal);
        Assert.Contains("OnDesignerBatchCompletedLockAware", code, StringComparison.Ordinal);

        // Прежний «молчаливый» выход при отказе запуска не должен вернуться.
        Assert.DoesNotContain("if (!OneCLauncher.RunDesignerBatch(ib, operation, path))",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMainViewModel_SubscribesToAsyncCompletion()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs");
        Assert.Contains("OneCLauncher.DesignerBatchCompleted += OnDesignerBatchCompletedLockAware",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherDetector_UsesClassifierForTokenAndFileLocks()
    {
        var windows = ReadProjectFile("Workspace1C", "Services", "OneCLauncher.DesignerBatch.cs");
        Assert.Contains("DesignerLockClassifier.CommandLineMatchesBase", windows, StringComparison.Ordinal);
        Assert.Contains("DesignerLockClassifier.IsInfobaseLocked", windows, StringComparison.Ordinal);
        Assert.Contains("DesignerLockClassifier.IsFileLocked", windows, StringComparison.Ordinal);
        Assert.Contains("Launcher.BaseLockedFormat", windows, StringComparison.Ordinal);
        Assert.Contains("Launcher.OutputFileLockedFormat", windows, StringComparison.Ordinal);

        var linux = ReadProjectFile("Workspace1C", "Services", "OneCLauncher.Linux.DesignerBatch.cs");
        Assert.Contains("DesignerLockClassifier.CommandLineMatchesBase", linux, StringComparison.Ordinal);
        Assert.Contains("DesignerLockClassifier.IsInfobaseLocked", linux, StringComparison.Ordinal);
        Assert.Contains("DesignerLockClassifier.IsFileLocked", linux, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_ContainsIssue181KeysInBothLanguages()
    {
        var keys = new[]
        {
            "Launcher.BaseLockedFormat",
            "Launcher.OutputFileLockedFormat",
            "Launcher.DumpBlockedWarningTitle",
            "Launcher.DumpBlockedWarningBody",
            "Launcher.DumpRetryPrompt",
            "Launcher.DumpOpenFolderPrompt",
            "Launcher.DumpStillBlocked"
        };

        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");
        foreach (var key in keys)
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }
    }
}
