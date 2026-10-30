using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #83 (волна 2, итерация 6): поиск ring по свежему PATH из реестра и известным
/// каталогам 1С, когда PATH процесса приложения отличается от PATH консоли пользователя
/// (запуск из ярлыка/планировщика). Чистые функции тестируются с инъекцией источников
/// (строки PATH, функция-расширитель REG_EXPAND_SZ, проверка существования файлов) —
/// тесты не зависят от машины и реестра.
/// </summary>
public sealed class Issue83RingPathRegistryTests
{
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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ----------------------- SplitPathDirectories -----------------------

    [Fact]
    public void SplitPathDirectories_SplitsBySemicolon_AndSkipsEmptyItems()
    {
        var dirs = RingLicenseService.SplitPathDirectories(@"C:\bin; ;C:\tools\;  ;D:\ring");

        Assert.Equal(new[] { @"C:\bin", @"C:\tools\", @"D:\ring" }, dirs);
    }

    [Fact]
    public void SplitPathDirectories_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(RingLicenseService.SplitPathDirectories(null));
        Assert.Empty(RingLicenseService.SplitPathDirectories(string.Empty));
    }

    // ----------------------- ExpandRegistryPath (REG_EXPAND_SZ) -----------------------

    [Fact]
    public void ExpandRegistryPath_ExpandsVariables_WithInjectedExpander()
    {
        // Фикстура REG_EXPAND_SZ-значения из HKLM\...\Session Manager\Environment.
        const string raw = @"%SystemRoot%\system32;%ProgramFiles%\1cv8\bin;%LOCALAPPDATA%\1C";

        var expanded = RingLicenseService.ExpandRegistryPath(raw, value =>
            value.Replace("%SystemRoot%", "C:\\WINDOWS")
                 .Replace("%ProgramFiles%", "C:\\Program Files")
                 .Replace("%LOCALAPPDATA%", "C:\\Users\\u\\AppData\\Local"));

        Assert.Equal(@"C:\WINDOWS\system32;C:\Program Files\1cv8\bin;C:\Users\u\AppData\Local\1C", expanded);
    }

    [Fact]
    public void ExpandRegistryPath_WithoutExpander_UsesEnvironmentExpand()
    {
        // Environment.ExpandEnvironmentVariables на строке без переменных — без изменений.
        const string raw = @"C:\bin\1cv8;D:\ring";
        Assert.Equal(raw, RingLicenseService.ExpandRegistryPath(raw));
    }

    // ----------------------- BuildRingSearchDirectories -----------------------

    [Fact]
    public void BuildRingSearchDirectories_OrderIs_ProcessPath_RegistryPath_KnownDirs()
    {
        var dirs = RingLicenseService.BuildRingSearchDirectories(
            processPath: @"C:\ProcessPath\bin",
            registryPath: @"C:\RegistryPath\bin",
            knownDirs: new[] { @"C:\Program Files\1cv8\8.3.27\bin" });

        Assert.Equal(new[]
        {
            @"C:\ProcessPath\bin",
            @"C:\RegistryPath\bin",
            @"C:\Program Files\1cv8\8.3.27\bin"
        }, dirs);
    }

    [Fact]
    public void BuildRingSearchDirectories_DeduplicatesCaseInsensitively_KeepingFirstOccurrence()
    {
        var dirs = RingLicenseService.BuildRingSearchDirectories(
            processPath: @"C:\Apache\bin;c:\APACHE\BIN",
            registryPath: @"C:\APACHE\bin;D:\other",
            knownDirs: new[] { @"d:\OTHER" });

        Assert.Equal(new[] { @"C:\Apache\bin", @"D:\other" }, dirs);
    }

    [Fact]
    public void BuildRingSearchDirectories_NullSources_AreSkipped()
    {
        var dirs = RingLicenseService.BuildRingSearchDirectories(null, null, new[] { @"C:\bin" });
        Assert.Equal(new[] { @"C:\bin" }, dirs);
    }

    // ----------------------- FindRingInDirectories -----------------------

    [Fact]
    public void FindRingInDirectories_FindsRingExe_InProvidedDirectory()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\1cv8\8.3.27\bin\ring.exe"
        };

        var found = RingLicenseService.FindRingInDirectories(
            new[] { @"C:\Apache\bin", @"C:\Program Files\1cv8\8.3.27\bin" },
            path => existing.Contains(path));

        Assert.Equal(@"C:\Program Files\1cv8\8.3.27\bin\ring.exe", found);
    }

#if !LINUX
    [Fact]
    public void FindRingInDirectories_CmdFile_FoundWhenNoExe()
    {
        // issue #83: ring может быть установлен как командный файл (.cmd/.bat) —
        // находится тоже (запуск — через ToLaunchCommand/cmd.exe /c).
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\1c\ring.cmd"
        };

        var found = RingLicenseService.FindRingInDirectories(
            new[] { @"C:\1c" }, path => existing.Contains(path));

        Assert.Equal(@"C:\1c\ring.cmd", found);
    }

    [Fact]
    public void FindRingInDirectories_PrefersExe_OverCmdAndBat()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\1c\ring.cmd",
            @"C:\1c\ring.exe"
        };

        var found = RingLicenseService.FindRingInDirectories(
            new[] { @"C:\1c" }, path => existing.Contains(path));

        Assert.Equal(@"C:\1c\ring.exe", found);
    }
#endif

    [Fact]
    public void FindRingInDirectories_NothingExists_ReturnsNull()
    {
        var found = RingLicenseService.FindRingInDirectories(
            new[] { @"C:\nowhere", @"D:\void" }, _ => false);
        Assert.Null(found);
    }

    // ----------------------- Диагностика и сообщение вкладки -----------------------

    [Fact]
    public void RingSearch_UsesRegistryPathAndKnownDirs_WithDiagnostics()
    {
        // issue #83: поиск вне PATH процесса — через реестр (HKCU + HKLM) и известные
        // каталоги 1С; в журнал CM_RAC пишется диагностика (размеры PATH, каталоги).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "RingLicenseService.cs"));

        Assert.Contains("FindRingViaRegistryAndKnownDirsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReadRegistryPathValue", source, StringComparison.Ordinal);
        Assert.Contains("Environment", source, StringComparison.Ordinal); // HKCU\Environment
        Assert.Contains(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", source, StringComparison.Ordinal);
        Assert.Contains("DoNotExpandEnvironmentNames", source, StringComparison.Ordinal);
        Assert.Contains("BuildRingSearchDirectories", source, StringComparison.Ordinal);
        Assert.Contains("FindRingInDirectories", source, StringComparison.Ordinal);
        // Диагностика: размеры PATH процесса и реестра + список каталогов поиска.
        Assert.Contains("PATH процесса:", source, StringComparison.Ordinal);
        Assert.Contains("PATH реестра:", source, StringComparison.Ordinal);
        Assert.Contains("каталоги поиска ring:", source, StringComparison.Ordinal);
        Assert.Contains("ring найден вне PATH процесса", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableMessage_MentionsRegistryPathSources()
    {
        // issue #83: сообщение вкладки «Лицензии» перечисляет проверенные источники PATH.
        var message = RingLicenseService.BuildUnavailableMessage();

        Assert.Contains("PATH из реестра", message, StringComparison.Ordinal);
        Assert.Contains("ring", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rac", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Обновить", message, StringComparison.Ordinal);
    }
}
