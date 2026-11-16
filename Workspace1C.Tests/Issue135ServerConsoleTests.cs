using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #135 «Не запускается консоль администрирования серверов» (v1.0.1.72):
/// пункт «Утилиты → Консоль администрирования серверов (внешняя)» больше не
/// «молча ничего не делает» — он запускает оснастку выбранной версии платформы
/// либо показывает точную причину отказа.
/// <list type="bullet">
/// <item>чистый <see cref="ServerConsolePlanner"/>: отбор версий с 1CV8Servers.msc,
/// выбор версии, команды mmc/regsvr32, список radmin.dll/comctrl.dll, причины отказа;</item>
/// <item>сервис сообщает причину (ключ + подробности) вместо bool, пишет её в журнал;</item>
/// <item>WPF/Avalonia: команда привязана к пункту меню, есть действие регистрации
/// компонентов, на Linux пункт недоступен с подсказкой;</item>
/// <item>ключи локализации ru/en совпадают.</item>
/// </list>
/// </summary>
public sealed class Issue135ServerConsoleTests
{
    private const string SnapIn = "1CV8Servers.msc";

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

    private static Func<string, bool> Files(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return path => set.Contains(path);
    }

    /// <summary>
    /// Делегат перечисления файлов каталога (issue #135): для маски и регистронезависимого
    /// поиска. Возвращает содержимое только для перечисленных каталогов.
    /// </summary>
    private static Func<string, IEnumerable<string>> Enumerate(
        params (string Directory, string[] Files)[] entries)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (directory, files) in entries)
            map[directory] = files;
        return directory => map.TryGetValue(directory, out var files) ? files : Array.Empty<string>();
    }

    private static string VersionDir(string version, string arch = "64")
        => Path.GetDirectoryName(Bin(version, arch))!;

    private static string Bin(string version, string arch = "64")
        => Path.Combine(
            "C:",
            arch == "32" ? "Program Files (x86)" : "Program Files",
            "1cv8",
            version,
            "bin");

    private static ServerConsoleCandidate Candidate(string version, string arch = "64")
    {
        var bin = Bin(version, arch);
        return new ServerConsoleCandidate(version, bin, Path.Combine(bin, SnapIn), arch);
    }

    // ===================== Кандидаты платформы (есть/нет .msc) =====================

    [Fact]
    public void CreateCandidate_WithoutSnapIn_ReturnsNull()
    {
        var candidate = ServerConsolePlanner.CreateCandidate(
            "8.3.27.1688", Bin("8.3.27.1688"), "64", Files());

        Assert.Null(candidate);
    }

    [Fact]
    public void CreateCandidate_WithSnapIn_ReturnsCandidateWithDisplay()
    {
        var bin = Bin("8.3.27.1688");
        var candidate = ServerConsolePlanner.CreateCandidate(
            "8.3.27.1688", bin, "64", Files(Path.Combine(bin, SnapIn)));

        Assert.NotNull(candidate);
        Assert.Equal("8.3.27.1688", candidate!.Version);
        Assert.Equal(bin, candidate.BinDir);
        Assert.Equal(Path.Combine(bin, SnapIn), candidate.SnapInPath);
        Assert.Equal("8.3.27.1688 (64)", candidate.Display);
    }

    [Fact]
    public void CreateCandidate_EmptyVersionOrBin_ReturnsNull()
    {
        Assert.Null(ServerConsolePlanner.CreateCandidate("", Bin("8.3"), "64", _ => true));
        Assert.Null(ServerConsolePlanner.CreateCandidate("8.3", "", "64", _ => true));
    }

    [Fact]
    public void ListCandidates_FiltersPlatformsWithoutSnapIn()
    {
        var withSnapIn = Bin("8.5.4.1878");
        var without = Bin("8.3.27.1688");
        var candidates = ServerConsolePlanner.ListCandidates(
            new[]
            {
                ("8.5.4.1878", withSnapIn, "64"),
                ("8.3.27.1688", without, "64")
            },
            Files(Path.Combine(withSnapIn, SnapIn)));

        var only = Assert.Single(candidates);
        Assert.Equal("8.5.4.1878", only.Version);
    }

    [Fact]
    public void ListCandidates_SingleVersion_ReturnsOne()
    {
        var bin = Bin("8.3.27.1688");
        var candidates = ServerConsolePlanner.ListCandidates(
            new[] { ("8.3.27.1688", bin, "64") },
            Files(Path.Combine(bin, SnapIn)));

        Assert.Single(candidates);
    }

    [Fact]
    public void ListCandidates_MultipleVersions_NewestFirst()
    {
        var older = Bin("8.3.27.1688");
        var newer = Bin("8.5.4.1878");
        var candidates = ServerConsolePlanner.ListCandidates(
            new[]
            {
                ("8.3.27.1688", older, "64"),
                ("8.5.4.1878", newer, "64")
            },
            Files(Path.Combine(older, SnapIn), Path.Combine(newer, SnapIn)));

        Assert.Equal(new[] { "8.5.4.1878", "8.3.27.1688" },
            candidates.Select(c => c.Version).ToArray());
    }

    [Fact]
    public void ListCandidates_SameVersion_PrefersX64()
    {
        var x64 = Bin("8.3.27.1688", "64");
        var x32 = Bin("8.3.27.1688", "32");
        var candidates = ServerConsolePlanner.ListCandidates(
            new[]
            {
                ("8.3.27.1688", x32, "32"),
                ("8.3.27.1688", x64, "64")
            },
            Files(Path.Combine(x64, SnapIn), Path.Combine(x32, SnapIn)));

        Assert.Equal(2, candidates.Count);
        Assert.Equal("64", candidates[0].Architecture);
        Assert.Equal("32", candidates[1].Architecture);
    }

    [Fact]
    public void ListCandidates_DuplicateBinDir_ReportedOnce()
    {
        var bin = Bin("8.3.27.1688");
        var candidates = ServerConsolePlanner.ListCandidates(
            new[]
            {
                ("8.3.27.1688", bin, "64"),
                ("8.3.27.1688", bin, "64")
            },
            Files(Path.Combine(bin, SnapIn)));

        Assert.Single(candidates);
    }

    [Fact]
    public void ListCandidates_NullSources_Empty()
    {
        Assert.Empty(ServerConsolePlanner.ListCandidates(null, _ => true));
    }

    // ===================== Выбор версии =====================

    [Fact]
    public void ChoosePreferred_ExactDisplay()
    {
        var candidates = new[] { Candidate("8.5.4.1878"), Candidate("8.3.27.1688") };

        var chosen = ServerConsolePlanner.ChoosePreferred(candidates, "8.3.27.1688 (64)");

        Assert.NotNull(chosen);
        Assert.Equal("8.3.27.1688", chosen!.Version);
    }

    [Fact]
    public void ChoosePreferred_CleanVersion()
    {
        var candidates = new[] { Candidate("8.5.4.1878"), Candidate("8.3.27.1688") };

        var chosen = ServerConsolePlanner.ChoosePreferred(candidates, "8.3.27.1688");

        Assert.Equal("8.3.27.1688", chosen!.Version);
    }

    [Fact]
    public void ChoosePreferred_ExplicitArchitecture()
    {
        var candidates = new[]
        {
            Candidate("8.3.27.1688", "64"),
            Candidate("8.3.27.1688", "32")
        };

        var chosen = ServerConsolePlanner.ChoosePreferred(candidates, "8.3.27.1688 (32)");

        Assert.Equal("32", chosen!.Architecture);
    }

    [Fact]
    public void ChoosePreferred_PartialVersion_NewestMatchingBuild()
    {
        var candidates = new[] { Candidate("8.5.4.1878"), Candidate("8.3.27.1688"), Candidate("8.3.24.1000") };

        var chosen = ServerConsolePlanner.ChoosePreferred(candidates, "8.3.27");

        Assert.Equal("8.3.27.1688", chosen!.Version);
    }

    [Fact]
    public void ChoosePreferred_UnknownVersion_FallsBackToNewest()
    {
        var candidates = new[] { Candidate("8.5.4.1878"), Candidate("8.3.27.1688") };

        var chosen = ServerConsolePlanner.ChoosePreferred(candidates, "7.7.7.7");

        Assert.Equal("8.5.4.1878", chosen!.Version);
    }

    [Fact]
    public void ChoosePreferred_EmptyOrNull_NewestOrNull()
    {
        var candidates = new[] { Candidate("8.5.4.1878") };
        Assert.Equal("8.5.4.1878", ServerConsolePlanner.ChoosePreferred(candidates, "")!.Version);
        Assert.Null(ServerConsolePlanner.ChoosePreferred(Array.Empty<ServerConsoleCandidate>(), "8.3"));
        Assert.Null(ServerConsolePlanner.ChoosePreferred(null, "8.3"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ShouldShowPicker_OnlyForMultipleVersions(int count, bool expected)
    {
        var candidates = Enumerable.Range(0, count)
            .Select(i => Candidate($"8.3.2{i}.1000"))
            .ToArray();

        Assert.Equal(expected, ServerConsolePlanner.ShouldShowPicker(candidates));
    }

    // ===================== Команды mmc.exe / regsvr32 =====================

    [Fact]
    public void BuildMmcArguments_QuotesPathWithSpaces()
    {
        var args = ServerConsolePlanner.BuildMmcArguments(@"C:\Program Files\1cv8\8.3\bin\1CV8Servers.msc", false);

        Assert.Equal("\"C:\\Program Files\\1cv8\\8.3\\bin\\1CV8Servers.msc\"", args);
    }

    [Fact]
    public void BuildMmcArguments_32Bit_AppendsSwitch()
    {
        var args = ServerConsolePlanner.BuildMmcArguments(@"C:\1cv8\8.3\bin\1CV8Servers.msc", true);

        Assert.EndsWith(" /32", args, StringComparison.Ordinal);
        Assert.StartsWith("\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegSvr32Arguments_SilentAndQuoted()
    {
        var args = ServerConsolePlanner.BuildRegSvr32Arguments(@"C:\Program Files\1cv8\8.3\bin\radmin.dll");

        Assert.Equal("/s \"C:\\Program Files\\1cv8\\8.3\\bin\\radmin.dll\"", args);
    }

    [Fact]
    public void ResolveMmcExecutable_UsesSystemDirectoryWhenExists()
    {
        var path = Path.Combine(@"C:\Windows\System32", "mmc.exe");

        Assert.Equal(path, ServerConsolePlanner.ResolveMmcExecutable(@"C:\Windows\System32", Files(path)));
        Assert.Equal("mmc.exe", ServerConsolePlanner.ResolveMmcExecutable(@"C:\Windows\System32", Files()));
        Assert.Equal("mmc.exe", ServerConsolePlanner.ResolveMmcExecutable(null, _ => true));
    }

    [Fact]
    public void ResolveRegSvr32Executable_ArchitectureMatters()
    {
        var system32 = Path.Combine(@"C:\Windows\System32", "regsvr32.exe");
        var sysWow64 = Path.Combine(@"C:\Windows\SysWOW64", "regsvr32.exe");

        Assert.Equal(system32,
            ServerConsolePlanner.ResolveRegSvr32Executable(@"C:\Windows", true, Files(system32)));
        Assert.Equal(sysWow64,
            ServerConsolePlanner.ResolveRegSvr32Executable(@"C:\Windows", false, Files(sysWow64)));
        Assert.Equal("regsvr32.exe",
            ServerConsolePlanner.ResolveRegSvr32Executable(@"C:\Windows", true, Files()));
    }

    [Theory]
    [InlineData("32", true)]
    [InlineData("64", false)]
    [InlineData("x86", true)]
    [InlineData(null, true)]
    public void ShouldUse32Bit_MapsArchitecture(string? architecture, bool expected)
    {
        Assert.Equal(expected, ServerConsolePlanner.ShouldUse32Bit(architecture));
    }

    // ===================== Регистрация DLL (есть/нет файла) =====================

    [Fact]
    public void ListRegistrationDlls_BothPresent()
    {
        var bin = Bin("8.3.27.1688");
        var dlls = ServerConsolePlanner.ListRegistrationDlls(
            bin,
            Files(Path.Combine(bin, "radmin.dll"), Path.Combine(bin, "comctrl.dll")));

        Assert.Equal(new[] { "radmin.dll", "comctrl.dll" },
            dlls.Select(d => Path.GetFileName(d.DllPath)).ToArray());
        Assert.All(dlls, d => Assert.True(d.Exists));
    }

    [Fact]
    public void ListRegistrationDlls_MissingFile_ReportedAsNotExisting()
    {
        var bin = Bin("8.3.27.1688");
        var dlls = ServerConsolePlanner.ListRegistrationDlls(
            bin, Files(Path.Combine(bin, "radmin.dll")));

        Assert.Equal(2, dlls.Count);
        Assert.True(dlls[0].Exists);
        Assert.False(dlls[1].Exists);
    }

    [Fact]
    public void ListRegistrationDlls_EmptyBin_EmptyList()
    {
        Assert.Empty(ServerConsolePlanner.ListRegistrationDlls(null, _ => true));
        Assert.Empty(ServerConsolePlanner.ListRegistrationDlls("", _ => true));
    }

    // ===================== Причины отказа по веткам =====================

    [Fact]
    public void EvaluateAvailability_NoPlatforms_PlatformNotFound()
    {
        var result = ServerConsolePlanner.EvaluateAvailability(
            Array.Empty<ServerConsoleCandidate>(), null, anyPlatformFound: false);

        Assert.False(result.Success);
        Assert.Equal(ServerConsolePlanner.ErrorPlatformNotFound, result.ErrorKey);
    }

    [Fact]
    public void EvaluateAvailability_PlatformsWithoutSnapIn_SnapInNotFoundWithPath()
    {
        var bin = Bin("8.3.27.1688");
        var result = ServerConsolePlanner.EvaluateAvailability(
            Array.Empty<ServerConsoleCandidate>(), bin, anyPlatformFound: true);

        Assert.False(result.Success);
        Assert.Equal(ServerConsolePlanner.ErrorSnapInNotFound, result.ErrorKey);
        Assert.Equal(bin, result.Details);
    }

    [Fact]
    public void EvaluateAvailability_Candidates_Success()
    {
        var result = ServerConsolePlanner.EvaluateAvailability(
            new[] { Candidate("8.3.27.1688") }, Bin("8.3.27.1688"), anyPlatformFound: true);

        Assert.True(result.Success);
        Assert.Null(result.ErrorKey);
    }

    [Fact]
    public void ServerConsoleResult_Factories()
    {
        Assert.True(ServerConsoleResult.Ok(@"C:\1cv8\1CV8Servers.msc").Success);
        Assert.Equal(@"C:\1cv8\1CV8Servers.msc", ServerConsoleResult.Ok(@"C:\1cv8\1CV8Servers.msc").Details);

        var fail = ServerConsoleResult.Fail(ServerConsolePlanner.ErrorLaunchFailed, "текст");
        Assert.False(fail.Success);
        Assert.Equal(ServerConsolePlanner.ErrorLaunchFailed, fail.ErrorKey);
        Assert.Equal("текст", fail.Details);
    }

    // ===================== Настройки и каталог меню =====================

    [Fact]
    public void AppSettings_ServerConsolePlatformVersion_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, new AppSettings().ServerConsolePlatformVersion);
    }

    [Fact]
    public void UtilitiesMenu_HasExternalConsoleAndRegisterItems()
    {
        var console = UtilitiesMenuCatalog.Find("ServerConsoleExternal");
        Assert.NotNull(console);
        Assert.Equal(UtilitiesMenuCommandKind.OpenServerConsole, console!.CommandKind);

        var register = UtilitiesMenuCatalog.Find("ServerConsoleRegister");
        Assert.NotNull(register);
        Assert.Equal(UtilitiesMenuCommandKind.RegisterServerConsoleComponents, register!.CommandKind);
        Assert.Equal("Admin.ServerConsoleRegister", register.TextKey);
    }

    // ===================== Source-гварды WPF/Avalonia =====================

    [Fact]
    public void Wpf_UtilitiesMenu_BindsConsoleAndRegistrationCommands()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");

        Assert.Contains("case UtilitiesMenuCommandKind.OpenServerConsole:", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel.OpenServerConsoleCommand", source, StringComparison.Ordinal);
        Assert.Contains("case UtilitiesMenuCommandKind.RegisterServerConsoleComponents:", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel.RegisterServerConsoleComponentsCommand", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_UtilitiesMenu_BindsCommandsAndDisablesConsoleOnLinux()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");

        Assert.Contains("case UtilitiesMenuCommandKind.OpenServerConsole:", source, StringComparison.Ordinal);
        Assert.Contains("_vm!.OpenServerConsoleCommand", source, StringComparison.Ordinal);
        Assert.Contains("case UtilitiesMenuCommandKind.RegisterServerConsoleComponents:", source, StringComparison.Ordinal);
        Assert.Contains("_vm!.RegisterServerConsoleComponentsCommand", source, StringComparison.Ordinal);
        // На Linux пункт недоступен с локализованной подсказкой, а не молчит.
        Assert.Contains("item.IsEnabled = false;", source, StringComparison.Ordinal);
        Assert.Contains("Admin.ServerConsoleWindowsOnlyHint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ViewModel_UsesVersionPickerAndDiagnosticResult()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Admin.cs");

        Assert.Contains("OpenServerAdminConsole(", source, StringComparison.Ordinal);
        Assert.Contains("RegisterServerConsoleComponents(", source, StringComparison.Ordinal);
        Assert.Contains("PlatformVersionPickerWindow", source, StringComparison.Ordinal);
        Assert.Contains("useProvidedVersionsOnly: true", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlatformVersion", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsoleMessages.Format(result)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.ServerConsoleOfferRegister", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ViewModel_UsesVersionPickerAndReportsWindowsOnly()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Admin.cs");

        Assert.Contains("OpenServerAdminConsole(", source, StringComparison.Ordinal);
        Assert.Contains("RegisterServerConsoleComponents(", source, StringComparison.Ordinal);
        Assert.Contains("PlatformVersionPickerWindow", source, StringComparison.Ordinal);
        Assert.Contains("useProvidedVersionsOnly: true", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ErrorWindowsOnly", source, StringComparison.Ordinal);
        Assert.Contains("Admin.ServerConsoleOfferRegister", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_UsesLaunchCascadeAndRegistersDlls()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "InfobaseAdminService.cs");

        // Каскад стратегий запуска (issue #135): план строит чистый планировщик,
        // сервис исполняет попытки и логирует каждую.
        Assert.Contains("ServerConsolePlanner.BuildLaunchPlan", source, StringComparison.Ordinal);
        Assert.Contains("attempt.UseShellExecute", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.BuildDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateMscFiles", source, StringComparison.Ordinal);
        Assert.Contains("GetServerConsolePlatformOptions", source, StringComparison.Ordinal);
        Assert.Contains("Verb = \"runas\"", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ListRegistrationDlls", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ErrorWindowsOnly", source, StringComparison.Ordinal);
        // Откат на прежний shell-запуск .msc при сбое mmc.
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_LogsEveryLaunchAttempt()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "InfobaseAdminService.cs");

        Assert.Contains("попытка запуска", source, StringComparison.Ordinal);
        Assert.Contains("попытка {attempt.Strategy}", source, StringComparison.Ordinal);
        Assert.Contains("все стратегии запуска не удались", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PlatformVersionPickerWindow.xaml.cs")]
    [InlineData("PlatformVersionPickerWindow.Avalonia.cs")]
    public void VersionPicker_SupportsProvidedVersionsOnly(string fileName)
    {
        var source = ReadProjectFile("Workspace1C", "Views", fileName);

        Assert.Contains("useProvidedVersionsOnly", source, StringComparison.Ordinal);
        Assert.Contains("if (!useProvidedVersionsOnly)", source, StringComparison.Ordinal);
    }

    // ===================== Расширенный поиск оснастки (issue #135) =====================

    [Fact]
    public void FindSnapIn_AlternativeName_IsFound()
    {
        var bin = Bin("8.3.27.1688");
        var alternative = Path.Combine(bin, "1CV8Servers64.msc");

        var search = ServerConsolePlanner.FindSnapIn(bin, Files(alternative));

        Assert.Equal(alternative, search.SnapInPath);
        Assert.Equal(alternative, Assert.Single(search.FoundSnapIns));
    }

    [Fact]
    public void FindSnapIn_SearchesParentAndCommonDirectories()
    {
        var bin = Bin("8.3.27.1688");
        var common = Path.Combine(VersionDir("8.3.27.1688"), "common");
        var snapIn = Path.Combine(common, SnapIn);

        var search = ServerConsolePlanner.FindSnapIn(bin, Files(snapIn));

        Assert.Equal(snapIn, search.SnapInPath);
        Assert.Contains(bin, search.CheckedDirectories);
        Assert.Contains(VersionDir("8.3.27.1688"), search.CheckedDirectories);
        Assert.Contains(common, search.CheckedDirectories);
    }

    [Fact]
    public void FindSnapIn_MaskSearch_IsCaseInsensitive()
    {
        var bin = Bin("8.3.27.1688");
        var actual = Path.Combine(bin, "1cv8SERVERS64.MSC");

        var search = ServerConsolePlanner.FindSnapIn(
            bin, _ => false, Enumerate((bin, new[] { actual })));

        Assert.Equal(actual, search.SnapInPath);
    }

    [Fact]
    public void FindSnapIn_MaskSearch_MatchesAdminMask()
    {
        var bin = Bin("8.3.27.1688");
        var actual = Path.Combine(bin, "1CV8ClusterAdmin.msc");

        var search = ServerConsolePlanner.FindSnapIn(
            bin, _ => false, Enumerate((bin, new[] { actual, Path.Combine(bin, "readme.txt") })));

        Assert.Equal(actual, search.SnapInPath);
    }

    [Fact]
    public void FindSnapIn_DeduplicatesByFullPath()
    {
        var bin = Bin("8.3.27.1688");
        var canonical = Path.Combine(bin, SnapIn);

        var search = ServerConsolePlanner.FindSnapIn(
            bin, Files(canonical), Enumerate((bin, new[] { canonical, canonical })));

        Assert.Equal(canonical, search.SnapInPath);
        Assert.Single(search.FoundSnapIns);
        Assert.Empty(search.Alternatives);
    }

    [Fact]
    public void FindSnapIn_PrefersCanonicalNameOverAlternatives()
    {
        var bin = Bin("8.3.27.1688");
        var canonical = Path.Combine(bin, SnapIn);
        var alternative = Path.Combine(bin, "1CV8Servers64.msc");

        var search = ServerConsolePlanner.FindSnapIn(bin, Files(alternative, canonical));

        Assert.Equal(canonical, search.SnapInPath);
        Assert.Equal(2, search.FoundSnapIns.Count);
        Assert.Equal(alternative, Assert.Single(search.Alternatives));
    }

    [Fact]
    public void FindSnapIn_EmptyBin_ReturnsEmptySearch()
    {
        var search = ServerConsolePlanner.FindSnapIn(null, _ => true);

        Assert.Null(search.SnapInPath);
        Assert.Empty(search.CheckedDirectories);
        Assert.Empty(search.FoundSnapIns);
        Assert.False(search.FoundSnapIns.Count > 0);
    }

    [Fact]
    public void ListCandidates_FindsSnapInInCommonDirectory()
    {
        var bin = Bin("8.3.27.1688");
        var common = Path.Combine(VersionDir("8.3.27.1688"), "common");
        var snapIn = Path.Combine(common, SnapIn);

        var candidates = ServerConsolePlanner.ListCandidates(
            new[] { ("8.3.27.1688", bin, "64") }, Files(snapIn));

        var candidate = Assert.Single(candidates);
        Assert.Equal(snapIn, candidate.SnapInPath);
        Assert.Equal(snapIn, Assert.Single(candidate.AllSnapIns));
    }

    [Fact]
    public void Candidate_AllSnapIns_PrimaryFirstWithoutDuplicates()
    {
        var bin = Bin("8.3.27.1688");
        var primary = Path.Combine(bin, SnapIn);
        var alternative = Path.Combine(bin, "1CV8Servers64.msc");

        var candidate = new ServerConsoleCandidate(
            "8.3.27.1688", bin, primary, "64", new[] { alternative, primary });

        Assert.Equal(new[] { primary, alternative }, candidate.AllSnapIns.ToArray());
    }

    // ===================== Диагностика (issue #135) =====================

    [Fact]
    public void ListPlatformOptions_KeepsPlatformsWithoutSnapIn()
    {
        var bin = Bin("8.3.27.1688");

        var options = ServerConsolePlanner.ListPlatformOptions(
            new[] { ("8.3.27.1688", bin, "64") }, Files());

        var only = Assert.Single(options);
        Assert.False(only.HasSnapIn);
        Assert.Null(only.ToCandidate());
        Assert.Equal("8.3.27.1688 (64)", only.Display);
    }

    [Fact]
    public void BuildDiagnostics_PlatformWithoutSnapIn_ListsCheckedDirectories()
    {
        var bin = Bin("8.3.27.1688");
        var options = ServerConsolePlanner.ListPlatformOptions(
            new[] { ("8.3.27.1688", bin, "64") }, Files());

        var diagnostics = ServerConsolePlanner.BuildDiagnostics(
            options, ServerConsoleLaunchStrategies.None, "8.3.27.1688 (64)");

        Assert.Equal(1, diagnostics.PlatformCount);
        Assert.Contains(bin, diagnostics.CheckedDirectories);
        Assert.Contains(Path.Combine(VersionDir("8.3.27.1688"), "common"), diagnostics.CheckedDirectories);
        Assert.Empty(diagnostics.FoundSnapIns);
        Assert.False(diagnostics.HasSnapIns);
        Assert.Equal(ServerConsoleLaunchStrategies.None, diagnostics.LaunchStrategy);
    }

    [Fact]
    public void BuildDiagnostics_CollectsAllFoundSnapIns()
    {
        var withSnapIn = Bin("8.3.27.1688");
        var alternative = Path.Combine(withSnapIn, "1CV8Servers64.msc");
        var options = ServerConsolePlanner.ListPlatformOptions(
            new[] { ("8.3.27.1688", withSnapIn, "64") }, Files(Path.Combine(withSnapIn, SnapIn), alternative));

        var diagnostics = ServerConsolePlanner.BuildDiagnostics(options);

        Assert.Equal(2, diagnostics.FoundSnapIns.Count);
        Assert.True(diagnostics.HasSnapIns);
        Assert.Equal(Path.Combine(withSnapIn, SnapIn), diagnostics.FoundSnapIns[0]);
    }

    [Fact]
    public void Diagnostics_WithStrategy_ReturnsCopy()
    {
        var diagnostics = ServerConsoleDiagnostics.Empty.WithStrategy(ServerConsoleLaunchStrategies.Shell);

        Assert.Equal(ServerConsoleLaunchStrategies.Shell, diagnostics.LaunchStrategy);
        Assert.Equal(ServerConsoleLaunchStrategies.None, ServerConsoleDiagnostics.Empty.LaunchStrategy);
    }

    [Fact]
    public void EvaluateAvailability_WithDiagnostics_CarriesItInDetails()
    {
        var bin = Bin("8.3.27.1688");
        var options = ServerConsolePlanner.ListPlatformOptions(
            new[] { ("8.3.27.1688", bin, "64") }, Files());
        var diagnostics = ServerConsolePlanner.BuildDiagnostics(options);

        var result = ServerConsolePlanner.EvaluateAvailability(
            Array.Empty<ServerConsoleCandidate>(), bin, anyPlatformFound: true, diagnostics);

        Assert.False(result.Success);
        Assert.Equal(ServerConsolePlanner.ErrorSnapInNotFound, result.ErrorKey);
        Assert.NotNull(result.Diagnostics);
        Assert.NotNull(result.Details);
        Assert.Contains(bin, result.Details!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.CanOfferRegistration);
        Assert.True(result.CanOfferPlatformPicker);
    }

    [Theory]
    [InlineData(ServerConsolePlanner.ErrorLaunchFailed, true, false)]
    [InlineData(ServerConsolePlanner.ErrorSnapInNotFound, true, true)]
    [InlineData(ServerConsolePlanner.ErrorPlatformNotFound, true, true)]
    [InlineData(ServerConsolePlanner.ErrorWindowsOnly, false, false)]
    public void ServerConsoleResult_OffersActions(string errorKey, bool canRegister, bool canPick)
    {
        var result = ServerConsoleResult.Fail(errorKey);

        Assert.Equal(canRegister, result.CanOfferRegistration);
        Assert.Equal(canPick, result.CanOfferPlatformPicker);
    }

    [Fact]
    public void FormatDiagnostics_IncludesCheckedDirectories()
    {
        var bin = Bin("8.3.27.1688");
        var diagnostics = new ServerConsoleDiagnostics(
            new[] { bin }, Array.Empty<string>(), 1,
            ServerConsoleLaunchStrategies.Shell, "8.3.27.1688 (64)", null);

        var text = ServerConsoleMessages.FormatDiagnostics(diagnostics);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(bin, text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlatformStatuses_ReportsSnapInPresence()
    {
        var withSnapIn = Bin("8.3.27.1688");
        var withoutSnapIn = Bin("8.5.4.1878");
        var options = ServerConsolePlanner.ListPlatformOptions(
            new[]
            {
                ("8.3.27.1688", withSnapIn, "64"),
                ("8.5.4.1878", withoutSnapIn, "64")
            },
            Files(Path.Combine(withSnapIn, SnapIn)));

        var statuses = ServerConsoleMessages.BuildPlatformStatuses(options);

        Assert.Equal(2, statuses.Count);
        Assert.Contains("1CV8Servers.msc", statuses["8.3.27.1688 (64)"], StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(statuses["8.3.27.1688 (64)"], statuses["8.5.4.1878 (64)"]);
    }

    [Fact]
    public void ShouldShowPlatformPicker_TrueWhenMoreThanOnePlatform()
    {
        var bin1 = Bin("8.3.27.1688");
        var bin2 = Bin("8.5.4.1878");
        var options = ServerConsolePlanner.ListPlatformOptions(
            new[] { ("8.3.27.1688", bin1, "64"), ("8.5.4.1878", bin2, "64") }, Files());

        Assert.True(ServerConsolePlanner.ShouldShowPlatformPicker(options));
        Assert.False(ServerConsolePlanner.ShouldShowPlatformPicker(options.Take(1).ToList()));
        Assert.False(ServerConsolePlanner.ShouldShowPlatformPicker(null));
    }

    // ===================== Каскад стратегий запуска (issue #135) =====================

    [Fact]
    public void BuildLaunchPlan_64Bit_PrimaryThenAlternativesThenShell()
    {
        var bin = Bin("8.3.27.1688");
        var primary = Path.Combine(bin, SnapIn);
        var alternative = Path.Combine(bin, "1CV8Servers64.msc");
        var candidate = new ServerConsoleCandidate(
            "8.3.27.1688", bin, primary, "64", new[] { primary, alternative });

        var plan = ServerConsolePlanner.BuildLaunchPlan(candidate, @"C:\Windows\System32\mmc.exe");

        Assert.True(plan.Count >= 4);
        Assert.Equal(ServerConsoleLaunchStrategies.Mmc, plan[0].Strategy);
        Assert.Equal(@"C:\Windows\System32\mmc.exe", plan[0].FileName);
        Assert.Equal("\"" + primary + "\"", plan[0].Arguments);
        Assert.False(plan[0].UseShellExecute);

        // Альтернативный файл пробуется и без /32, и с явным /32.
        Assert.Contains(plan, a =>
            a.Strategy == ServerConsoleLaunchStrategies.MmcAlternative && a.SnapInPath == alternative);
        Assert.Contains(plan, a =>
            a.Strategy == ServerConsoleLaunchStrategies.MmcAlternative32 && a.SnapInPath == alternative);

        // Последняя стратегия — запуск .msc через оболочку.
        var last = plan[^1];
        Assert.Equal(ServerConsoleLaunchStrategies.Shell, last.Strategy);
        Assert.True(last.UseShellExecute);
        Assert.Equal(primary, last.FileName);
    }

    [Fact]
    public void BuildLaunchPlan_32Bit_PrimaryUsesSlash32()
    {
        var bin = Bin("8.3.27.1688", "32");
        var primary = Path.Combine(bin, SnapIn);
        var candidate = new ServerConsoleCandidate("8.3.27.1688", bin, primary, "32");

        var plan = ServerConsolePlanner.BuildLaunchPlan(candidate, "mmc.exe");

        Assert.Equal(ServerConsoleLaunchStrategies.Mmc32, plan[0].Strategy);
        Assert.EndsWith(" /32", plan[0].Arguments, StringComparison.Ordinal);
        Assert.Equal(2, plan.Count); // основная попытка + запуск через оболочку
    }

    [Fact]
    public void BuildLaunchPlan_NullCandidate_Empty()
    {
        Assert.Empty(ServerConsolePlanner.BuildLaunchPlan(null, "mmc.exe"));
    }

    // ===================== UI: выбор версии и диагностика в обеих оболочках =====================

    [Fact]
    public void Wpf_ViewModel_ShowsPickerForEveryPlatformWithStatuses()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Admin.cs");

        Assert.Contains("GetServerConsolePlatformOptions()", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ShouldShowPlatformPicker(options)", source, StringComparison.Ordinal);
        Assert.Contains("statusByVariant: ServerConsoleMessages.BuildPlatformStatuses(options)", source, StringComparison.Ordinal);
        Assert.Contains("result.CanOfferRegistration", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsoleMessages.Format(result)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ViewModel_ShowsPickerForEveryPlatformWithStatuses()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Admin.cs");

        Assert.Contains("GetServerConsolePlatformOptions()", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ShouldShowPlatformPicker(options)", source, StringComparison.Ordinal);
        Assert.Contains("statusByVariant: ServerConsoleMessages.BuildPlatformStatuses(options)", source, StringComparison.Ordinal);
        Assert.Contains("result.CanOfferRegistration", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsoleMessages.Format(result)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PlatformVersionPickerWindow.xaml.cs")]
    [InlineData("PlatformVersionPickerWindow.Avalonia.cs")]
    public void VersionPicker_SupportsStatusHints(string fileName)
    {
        var source = ReadProjectFile("Workspace1C", "Views", fileName);

        Assert.Contains("statusByVariant", source, StringComparison.Ordinal);
        Assert.Contains("ApplyStatusHints", source, StringComparison.Ordinal);
        Assert.Contains("StatusHint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformVersionGroup_HasStatusHint()
    {
        var source = ReadProjectFile("Workspace1C", "Models", "PlatformVersionGroup.cs");

        Assert.Contains("StatusHint", source, StringComparison.Ordinal);
        Assert.Contains("issue #135", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Picker_RendersStatusHint()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "PlatformVersionPickerWindow.xaml");

        Assert.Contains("{Binding StatusHint}", xaml, StringComparison.Ordinal);
    }

    // ===================== Локализация ru/en =====================

    [Fact]
    public void Localization_ServerConsoleKeys_MatchInBothLanguages()
    {
        var ru = LoadServerConsoleKeys("ru.json");
        var en = LoadServerConsoleKeys("en.json");

        Assert.Equal(ru, en);

        foreach (var key in new[]
                 {
                     ServerConsolePlanner.ErrorPlatformNotFound,
                     ServerConsolePlanner.ErrorSnapInNotFound,
                     ServerConsolePlanner.ErrorLaunchFailed,
                     ServerConsolePlanner.ErrorWindowsOnly,
                     "Admin.ServerConsoleWindowsOnlyHint",
                     "Admin.ServerConsoleRegister",
                     ServerConsolePlanner.ErrorRegisterNoPlatform,
                     ServerConsolePlanner.ErrorRegisterNoFiles,
                     "Admin.ServerConsoleRegisterReport",
                     "Admin.ServerConsoleOfferRegister",
                     "Admin.ServerConsoleDiagHeader",
                     "Admin.ServerConsoleDiagPlatforms",
                     "Admin.ServerConsoleDiagPlatformLine",
                     "Admin.ServerConsoleDiagCheckedDirs",
                     "Admin.ServerConsoleDiagFoundSnapIns",
                     "Admin.ServerConsoleDiagNothing",
                     "Admin.ServerConsoleDiagStrategy",
                     "Admin.ServerConsoleStrategyMmc",
                     "Admin.ServerConsoleStrategyMmc32",
                     "Admin.ServerConsoleStrategyMmcAlternative",
                     "Admin.ServerConsoleStrategyMmcAlternative32",
                     "Admin.ServerConsoleStrategyShell",
                     "Admin.ServerConsoleStrategyNone",
                     "Admin.ServerConsoleStrategyWindowsOnly",
                     "Admin.ServerConsolePickAnyHint",
                     "Admin.ServerConsolePickAnySnapIn",
                     "Admin.ServerConsolePickAnyNoSnapIn",
                     "Admin.ServerConsoleSnapInAlternatives"
                 })
        {
            Assert.Contains(key, ru);
        }
    }

    private static HashSet<string> LoadServerConsoleKeys(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var document = JsonDocument.Parse(json);
        var strings = document.RootElement.GetProperty("strings");
        return strings.EnumerateObject()
            .Select(p => p.Name)
            .Where(name => name.StartsWith("Admin.ServerConsole", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }
}
