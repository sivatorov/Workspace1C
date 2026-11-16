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
/// Issue #134 «Завершение процессов 1С» (v1.0.1.71, автор ArtyoZ): в блоке
/// «Для выделенных (N)…» контекстного меню списка баз появляется подменю
/// «Завершить процессы» с процессами выделенных баз (для группы — всех баз
/// группы), отсортированными по времени старта.
/// <list type="bullet">
/// <item>чистый слой <see cref="KillProcessesMenuPlanner"/> — классификатор
/// приложения, подпись «пользователь, приложение, время старта, PID», сортировка,
/// отбор процессов по базам через <see cref="RunningInfobaseMatcher"/>;</item>
/// <item>WPF- и Avalonia-оболочки используют общий слой и завершают ровно
/// выбранный процесс (source-гварды: Avalonia собирается только в Linux);</item>
/// <item>ключи локализации ru/en совпадают.</item>
/// </list>
/// </summary>
public sealed class Issue134KillProcessesTests
{
    // ===================== Классификатор приложения =====================

    [Theory]
    [InlineData("1cv8c.exe", "", OneCApplicationKind.ThinClient)]
    [InlineData("1cv8c", "", OneCApplicationKind.ThinClient)] // Linux — имя без .exe
    [InlineData(@"C:\Program Files\1cv8\8.3.27\bin\1cv8c.exe", "ENTERPRISE /F\"C:\\Bases\\Alpha\"", OneCApplicationKind.ThinClient)]
    [InlineData("1cv8.exe", "ENTERPRISE /S server1\\trade", OneCApplicationKind.ThickClient)]
    [InlineData("1cv8", "", OneCApplicationKind.ThickClient)]
    [InlineData("1cv8x64.exe", "ENTERPRISE /F\"C:\\Bases\\Alpha\"", OneCApplicationKind.ThickClient)]
    [InlineData("1cv8.exe", "\"C:\\1cv8\\1cv8.exe\" DESIGNER /F\"C:\\Bases\\Alpha\"", OneCApplicationKind.Configurator)]
    [InlineData("1cv8x64", "CONFIG /S server1/trade", OneCApplicationKind.Configurator)]
    [InlineData("1cv8s", "", OneCApplicationKind.WebClient)]
    [InlineData("1cv8s.exe", "ENTERPRISE", OneCApplicationKind.WebClient)]
    [InlineData("1cv8cx64.exe", "ENTERPRISE", OneCApplicationKind.Unknown)]
    [InlineData("notepad.exe", "", OneCApplicationKind.Unknown)]
    [InlineData("", "", OneCApplicationKind.Unknown)]
    public void ClassifyApplication_ByProcessNameAndCommandLine(
        string processName, string commandLine, OneCApplicationKind expected)
    {
        Assert.Equal(expected, KillProcessesMenuPlanner.ClassifyApplication(processName, commandLine));
    }

    [Fact]
    public void ClassifyApplication_IsCaseInsensitive()
    {
        Assert.Equal(OneCApplicationKind.ThinClient,
            KillProcessesMenuPlanner.ClassifyApplication("1CV8C.EXE", ""));
        Assert.Equal(OneCApplicationKind.Configurator,
            KillProcessesMenuPlanner.ClassifyApplication("1CV8.EXE", "designer"));
    }

    [Fact]
    public void ApplicationDisplayName_KnownKinds_UseLocalization()
    {
        Assert.Equal("тонкий клиент", KillProcessesMenuPlanner.ApplicationDisplayName("1cv8c.exe", "", Translate));
        Assert.Equal("толстый клиент", KillProcessesMenuPlanner.ApplicationDisplayName("1cv8.exe", "ENTERPRISE", Translate));
        Assert.Equal("конфигуратор", KillProcessesMenuPlanner.ApplicationDisplayName("1cv8.exe", "DESIGNER", Translate));
        Assert.Equal("веб-клиент", KillProcessesMenuPlanner.ApplicationDisplayName("1cv8s", "", Translate));
    }

    [Fact]
    public void ApplicationDisplayName_Unknown_ReturnsProcessNameWithoutExtension()
    {
        // «неизвестное — имя процесса»; каталог и «.exe» отбрасываются.
        Assert.Equal("myproc", KillProcessesMenuPlanner.ApplicationDisplayName(@"C:\tools\myproc.exe", "", Translate));
        Assert.Equal("weird-proc", KillProcessesMenuPlanner.ApplicationDisplayName("weird-proc", "", Translate));
    }

    // ===================== Подпись пункта меню =====================

    private static RunningOneCProcessDetails Process(
        int pid = 5566,
        string processName = "1cv8c.exe",
        string commandLine = "ENTERPRISE /F\"C:\\Bases\\Alpha\"",
        DateTime? startTime = null,
        string? startTimeToken = null,
        string? userName = "superadmin") =>
        new(pid, processName, commandLine, startTime, startTimeToken, userName);

    [Fact]
    public void FormatItemLabel_AllFields_MatchesIssueExample()
    {
        // Пример из issue #134: «superadmin, тонкий клиент, 09:09:55 16.11.2026, 5566».
        var process = Process(startTime: new DateTime(2026, 11, 16, 9, 9, 55));

        Assert.Equal(
            "superadmin, тонкий клиент, 09:09:55 16.11.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatItemLabel_UnknownUser_UsesLocalizedPlaceholder(string? userName)
    {
        var process = Process(userName: userName, startTime: new DateTime(2026, 11, 16, 9, 9, 55));

        Assert.Equal(
            "пользователь не определён, тонкий клиент, 09:09:55 16.11.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Fact]
    public void FormatItemLabel_UnknownStartTime_UsesDash()
    {
        var process = Process(processName: "1cv8.exe", commandLine: "ENTERPRISE", startTime: null);

        Assert.Equal(
            "superadmin, толстый клиент, —, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Fact]
    public void FormatItemLabel_CyrillicUserName_IsPreserved()
    {
        var process = Process(userName: "Иван Петров", startTime: new DateTime(2026, 11, 16, 23, 5, 1));

        Assert.Equal(
            "Иван Петров, тонкий клиент, 23:05:01 16.11.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Fact]
    public void FormatItemLabel_UnknownProcess_UsesProcessName()
    {
        var process = Process(processName: "myproc.exe", commandLine: "", startTime: new DateTime(2026, 1, 2, 3, 4, 5));

        Assert.Equal(
            "superadmin, myproc, 03:04:05 02.01.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    // ===================== Сортировка по времени старта =====================

    [Fact]
    public void SortByStartTime_Ascending_UnknownTimesLast()
    {
        var early = Process(pid: 1, startTime: new DateTime(2026, 11, 16, 8, 0, 0));
        var late = Process(pid: 2, startTime: new DateTime(2026, 11, 16, 10, 0, 0));
        var noTime = Process(pid: 3, startTime: null);

        var sorted = KillProcessesMenuPlanner.SortByStartTime(new[] { late, noTime, early });

        Assert.Equal(new[] { 1, 2, 3 }, sorted.Select(p => p.Pid));
    }

    [Fact]
    public void SortByStartTime_EqualTimes_IsStable()
    {
        var time = new DateTime(2026, 11, 16, 9, 0, 0);
        var first = Process(pid: 10, startTime: time);
        var second = Process(pid: 11, startTime: time);
        var third = Process(pid: 12, startTime: time);

        var sorted = KillProcessesMenuPlanner.SortByStartTime(new[] { first, second, third });

        // Равные значения — исходный порядок (стабильная сортировка LINQ).
        Assert.Equal(new[] { 10, 11, 12 }, sorted.Select(p => p.Pid));
    }

    [Fact]
    public void SortByStartTime_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(KillProcessesMenuPlanner.SortByStartTime(null));
        Assert.Empty(KillProcessesMenuPlanner.SortByStartTime(Array.Empty<RunningOneCProcessDetails>()));
    }

    // ===================== Отбор по базам и группе =====================

    private static Infobase FileBase(string id, string path, string? group = null) => new()
    {
        Id = id,
        Name = id,
        Group = group ?? string.Empty,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static Infobase ServerBase(string id, string server, string dbName, string? group = null) => new()
    {
        Id = id,
        Name = id,
        Group = group ?? string.Empty,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = dbName
        }
    };

    [Fact]
    public void SelectForBases_FileAndServerBases_OnlySelected()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");
        var server = ServerBase("server", "server1", "trade");

        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 2, commandLine: "ENTERPRISE /S \"server1\\trade\""),
            Process(pid: 3, commandLine: "ENTERPRISE /F\"C:\\Bases\\Other\""),
            Process(pid: 4, commandLine: "ENTERPRISE"),
        };

        var selected = KillProcessesMenuPlanner.SelectForBases(new[] { file, server }, processes);

        Assert.Equal(new[] { 1, 2 }, selected.Select(p => p.Pid));
    }

    [Fact]
    public void SelectForBases_GroupTargets_SelectOnlyGroupBases()
    {
        // Для выделенной группы MainViewModel передаёт все базы группы (включая
        // вложенные); процессы баз вне группы в подменю не попадают.
        var groupBases = new[]
        {
            FileBase("g1", @"C:\Bases\Group1", "Работа"),
            FileBase("g2", @"C:\Bases\Group2", "Работа / Вложенная")
        };
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Group1\""),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\Group2\""),
            Process(pid: 3, commandLine: "ENTERPRISE /F\"C:\\Bases\\Outside\"")
        };

        var selected = KillProcessesMenuPlanner.SelectForBases(groupBases, processes);

        Assert.Equal(new[] { 1, 2 }, selected.Select(p => p.Pid));
    }

    [Fact]
    public void SelectForBases_NoDuplicatePids()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");
        var process = Process(pid: 7, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"");

        // Один и тот же PID в списке дважды (например, повторный опрос) — в
        // результате он должен быть один раз.
        var selected = KillProcessesMenuPlanner.SelectForBases(
            new[] { file }, new[] { process, process });

        Assert.Single(selected);
        Assert.Equal(7, selected[0].Pid);
    }

    [Fact]
    public void SelectForBases_NoBasesOrProcesses_ReturnsEmpty()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");
        var process = Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"");

        Assert.Empty(KillProcessesMenuPlanner.SelectForBases(null, new[] { process }));
        Assert.Empty(KillProcessesMenuPlanner.SelectForBases(new[] { file }, null));
        Assert.Empty(KillProcessesMenuPlanner.SelectForBases(
            new[] { new Infobase { Id = "no-connection", Connection = null! } }, new[] { process }));
    }

    [Fact]
    public void SelectForBases_ProcessWithoutConnectionKeys_IsNotSelected()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");

        var selected = KillProcessesMenuPlanner.SelectForBases(
            new[] { file }, new[] { Process(pid: 1, commandLine: "") });

        Assert.Empty(selected);
    }

    [Fact]
    public void BuildMenuItems_SortsAndLabels()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");
        var processes = new[]
        {
            Process(pid: 2, startTime: new DateTime(2026, 11, 16, 10, 0, 0),
                commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 1, startTime: new DateTime(2026, 11, 16, 8, 0, 0),
                commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"")
        };

        var items = KillProcessesMenuPlanner.BuildMenuItems(new[] { file }, processes, Translate);

        Assert.Equal(new[] { 1, 2 }, items.Select(i => i.Process.Pid));
        Assert.Equal(
            "superadmin, тонкий клиент, 08:00:00 16.11.2026, 1",
            items[0].Label);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json", "Завершить процессы", "пользователь не определён", "тонкий клиент",
        "толстый клиент", "конфигуратор", "веб-клиент")]
    [InlineData("en.json", "Terminate processes", "user not determined", "thin client",
        "thick client", "configurator", "web client")]
    public void Localization_HasKillProcessesKeys(
        string file, string menuTitle, string unknownUser, string thin, string thick,
        string configurator, string web)
    {
        var strings = LoadStrings(file);

        Assert.Equal(menuTitle, strings[KillProcessesMenuPlanner.MenuTitleKey]);
        Assert.Equal(unknownUser, strings[KillProcessesMenuPlanner.UnknownUserKey]);
        Assert.Equal(thin, strings[KillProcessesMenuPlanner.ThinClientKey]);
        Assert.Equal(thick, strings[KillProcessesMenuPlanner.ThickClientKey]);
        Assert.Equal(configurator, strings[KillProcessesMenuPlanner.ConfiguratorKey]);
        Assert.Equal(web, strings[KillProcessesMenuPlanner.WebClientKey]);

        // Формат подписи и прочерк времени, подтверждение и обе формы ошибки.
        Assert.Equal("{0}, {1}, {2}, {3}", strings[KillProcessesMenuPlanner.ItemFormatKey]);
        Assert.Equal("—", strings[KillProcessesMenuPlanner.UnknownStartTimeKey]);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.ConfirmKey], StringComparison.Ordinal);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillFailedKey], StringComparison.Ordinal);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillFailedDetailKey], StringComparison.Ordinal);
        Assert.Contains("{1}", strings[KillProcessesMenuPlanner.KillFailedDetailKey], StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RuAndEn_HaveSameKillProcessesKeySet()
    {
        var ru = LoadStrings("ru.json").Keys
            .Where(k => k.StartsWith("KillProcesses.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var en = LoadStrings("en.json").Keys
            .Where(k => k.StartsWith("KillProcesses.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ru, en);
        Assert.Contains(KillProcessesMenuPlanner.MenuTitleKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.ItemFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.ConfirmKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillFailedKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillFailedDetailKey, ru);
    }

    [Fact]
    public void Localization_NoRussianTextHardcodedInPlanner()
    {
        var planner = ReadProjectFile(Path.Combine("Workspace1C", "Services", "KillProcessesMenuPlanner.cs"));

        // Тексты — только в словарях: в коде нет русских строковых литералов.
        Assert.DoesNotContain("\"Завершить процессы\"", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("\"пользователь не определён\"", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("\"тонкий клиент\"", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("\"конфигуратор\"", planner, StringComparison.Ordinal);
    }

    // ===================== Source-гварды: WPF =====================

    [Fact]
    public void Wpf_BatchMenu_HasKillProcessesSubmenuInSelectedBlock()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));

        // Подменю создаётся в блоке «Для выделенных (N)…» и использует общий слой.
        Assert.Contains("PopulateBatchOperationsMenu", source, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.MenuTitleKey", source, StringComparison.Ordinal);
        Assert.Contains("_killProcessesMenu = killProcessesMenu;", source, StringComparison.Ordinal);
        Assert.Contains("batchMenu.Items.Add(killProcessesMenu);", source, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.BuildMenuItems(", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel.BatchOperationTargetsIncludingGroup", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_KillProcess_UsesKillerWithStartTimeToken_AndRefreshesIndicators()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));

        Assert.Contains("IOneCProcessKiller", source, StringComparison.Ordinal);
        Assert.Contains("killer.Kill(", source, StringComparison.Ordinal);
        Assert.Contains("process.StartTimeToken", source, StringComparison.Ordinal);
        Assert.Contains("killer.LastError", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel?.RefreshRunningFlags();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_MenuOpened_PopulatesKillProcessesSubmenu()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        Assert.Contains("PopulateKillProcessesMenu(restricted);", source, StringComparison.Ordinal);
    }

    // ===================== Source-гварды: Avalonia =====================

    [Fact]
    public void Avalonia_BatchMenu_HasKillProcessesSubmenuInSelectedBlock()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        Assert.Contains("PopulateRowAvaloniaBatchMenu", source, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.MenuTitleKey", source, StringComparison.Ordinal);
        Assert.Contains("_killProcessesMenu = killProcessesMenu;", source, StringComparison.Ordinal);
        Assert.Contains("batchMenu.Items.Add(killProcessesMenu);", source, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.BuildMenuItems(", source, StringComparison.Ordinal);
        Assert.Contains("_vm.BatchOperationTargetsIncludingGroup", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_KillProcess_UsesKillerWithStartTimeToken_AndRefreshesIndicators()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        Assert.Contains("IOneCProcessKiller", source, StringComparison.Ordinal);
        Assert.Contains("killer.Kill(", source, StringComparison.Ordinal);
        Assert.Contains("process.StartTimeToken", source, StringComparison.Ordinal);
        Assert.Contains("killer.LastError", source, StringComparison.Ordinal);
        Assert.Contains("_vm?.RefreshRunningFlags();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPlatforms_UseSamePlannerAndConfirmKey()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            Assert.Contains("KillProcessesMenuPlanner.FormatItemLabel(", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessesMenuPlanner.ConfirmKey", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessesMenuPlanner.KillFailedKey", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessesMenuPlanner.KillFailedDetailKey", source, StringComparison.Ordinal);
        }
    }

    // ===================== Вспомогательное =====================

    /// <summary>Мини-словарь русских значений новых ключей (без инициализации LocalizationManager).</summary>
    private static readonly Dictionary<string, string> RuStrings = new(StringComparer.Ordinal)
    {
        [KillProcessesMenuPlanner.ItemFormatKey] = "{0}, {1}, {2}, {3}",
        [KillProcessesMenuPlanner.UnknownUserKey] = "пользователь не определён",
        [KillProcessesMenuPlanner.UnknownStartTimeKey] = "—",
        [KillProcessesMenuPlanner.ThinClientKey] = "тонкий клиент",
        [KillProcessesMenuPlanner.ThickClientKey] = "толстый клиент",
        [KillProcessesMenuPlanner.ConfiguratorKey] = "конфигуратор",
        [KillProcessesMenuPlanner.WebClientKey] = "веб-клиент",
    };

    private static string Translate(string key) =>
        RuStrings.TryGetValue(key, out var value) ? value : key;

    private static string ReadProjectFile(string relativePath)
    {
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

    /// <summary>Словарь строк языка: <c>strings</c> из Languages/ru.json и Languages/en.json.</summary>
    private static Dictionary<string, string> LoadStrings(string fileName)
    {
        using var doc = JsonDocument.Parse(
            ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", fileName)));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }
}
