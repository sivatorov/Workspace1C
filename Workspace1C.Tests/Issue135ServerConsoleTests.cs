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
    public void Service_UsesMmcWithFallbackAndRegistersDlls()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "InfobaseAdminService.cs");

        Assert.Contains("ServerConsolePlanner.BuildMmcArguments", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
        Assert.Contains("Verb = \"runas\"", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ListRegistrationDlls", source, StringComparison.Ordinal);
        Assert.Contains("ServerConsolePlanner.ErrorWindowsOnly", source, StringComparison.Ordinal);
        // Откат на прежний shell-запуск .msc при сбое mmc.
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
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
                     "Admin.ServerConsoleOfferRegister"
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
