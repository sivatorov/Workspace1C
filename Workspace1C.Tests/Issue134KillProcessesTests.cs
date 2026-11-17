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

    // ===================== Имя пользователя из командной строки (/N) =====================

    [Theory]
    [InlineData("/N\"superadmin\"", "superadmin")]
    [InlineData("/N superadmin", "superadmin")]
    [InlineData("/Nsuperadmin", "superadmin")]
    [InlineData("-N\"superadmin\"", "superadmin")]
    [InlineData("-N superadmin", "superadmin")]
    [InlineData("-Nsuperadmin", "superadmin")]
    [InlineData("ENTERPRISE /F\"C:\\Bases\\Alpha\" /N\"superadmin\"", "superadmin")]
    [InlineData("ENTERPRISE /N\"Иван Петров\" /P\"secret\"", "Иван Петров")]
    [InlineData("ENTERPRISE /n\"superadmin\"", "superadmin")]
    [InlineData("-n superadmin", "superadmin")]
    [InlineData("/N\"a\"\"b\"", "a\"b")]
    public void TryExtractUserNameFromCommandLine_SupportedForms(
        string commandLine, string expected)
    {
        Assert.True(KillProcessesMenuPlanner.TryExtractUserNameFromCommandLine(commandLine, out var userName));
        Assert.Equal(expected, userName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ENTERPRISE /F\"C:\\Bases\\Alpha\"")]
    [InlineData("ENTERPRISE /N")]
    [InlineData("ENTERPRISE /N \"\"")]
    [InlineData("ENTERPRISE /N /P secret")]
    [InlineData("ENTERPRISE /Out/Nfoo")]
    [InlineData("ENTERPRISE /IBNameN")]
    public void TryExtractUserNameFromCommandLine_NoUser_ReturnsFalse(string? commandLine)
    {
        Assert.False(KillProcessesMenuPlanner.TryExtractUserNameFromCommandLine(commandLine, out var userName));
        Assert.Null(userName);
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

    [Fact]
    public void FormatItemLabel_UserNameTakesPriorityOverCommandLine()
    {
        // Владелец процесса важнее значения /N из командной строки.
        var process = Process(
            commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\" /N\"from-command-line\"",
            userName: "owner",
            startTime: new DateTime(2026, 11, 16, 9, 9, 55));

        Assert.Equal(
            "owner, тонкий клиент, 09:09:55 16.11.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Fact]
    public void FormatItemLabel_UserNameFromCommandLine_WhenOwnerUnknown()
    {
        // Пункт 1 автора issue: имя пользователя из параметра /N.
        var process = Process(
            commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\" /N\"superadmin\"",
            userName: null,
            startTime: new DateTime(2026, 11, 16, 9, 9, 55));

        Assert.Equal(
            "superadmin, тонкий клиент, 09:09:55 16.11.2026, 5566",
            KillProcessesMenuPlanner.FormatItemLabel(process, Translate));
    }

    [Fact]
    public void ResolveUserName_PrefersOwner_ThenCommandLine_ThenNull()
    {
        Assert.Equal("owner", KillProcessesMenuPlanner.ResolveUserName(
            Process(userName: "owner", commandLine: "ENTERPRISE /N\"cmd\"")));
        Assert.Equal("cmd", KillProcessesMenuPlanner.ResolveUserName(
            Process(userName: "  ", commandLine: "ENTERPRISE /N\"cmd\"")));
        Assert.Null(KillProcessesMenuPlanner.ResolveUserName(
            Process(userName: null, commandLine: "ENTERPRISE")));
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

    // ===================== План меню: одна и несколько баз =====================

    [Fact]
    public void BuildMenuPlan_OneBaseWithSingleProcess_IsFlatWithoutKillAll()
    {
        var alpha = FileBase("alpha", @"C:\Bases\Alpha");
        var processes = new[]
        {
            Process(pid: 1, startTime: new DateTime(2026, 11, 16, 8, 0, 0),
                commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"")
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { alpha }, processes, Translate);

        Assert.False(plan.IsEmpty);
        Assert.False(plan.IsGrouped);
        Assert.All(plan.Entries, entry => Assert.Equal(KillProcessesMenuEntryKind.Process, entry.Kind));
        Assert.Equal(new[] { 1 }, plan.Entries.Select(entry => entry.Process!.Pid));
        Assert.Equal(new[] { 1 }, plan.AllProcesses.Select(process => process.Pid));
    }

    [Fact]
    public void BuildMenuPlan_OneBaseWithSeveralProcesses_KillAllFirst()
    {
        // Доработка автора issue #134 от 16.11.2026: одна выделенная база с двумя
        // процессами — «Завершить все» первым пунктом, затем сами процессы.
        var alpha = FileBase("alpha", @"C:\Bases\Alpha");
        var processes = new[]
        {
            Process(pid: 2, startTime: new DateTime(2026, 11, 16, 10, 0, 0),
                commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 1, startTime: new DateTime(2026, 11, 16, 8, 0, 0),
                commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"")
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { alpha }, processes, Translate);

        Assert.False(plan.IsEmpty);
        Assert.False(plan.IsGrouped);
        Assert.Equal(3, plan.Entries.Count);
        Assert.Equal(KillProcessesMenuEntryKind.KillAll, plan.Entries[0].Kind);
        Assert.Equal("Завершить все", plan.Entries[0].Label);
        Assert.Equal(new[] { 1, 2 }, plan.Entries[0].Processes!.Select(process => process.Pid));
        Assert.Equal(new[] { 1, 2 }, plan.Entries.Skip(1).Select(entry => entry.Process!.Pid));
        Assert.Equal(new[] { 1, 2 }, plan.AllProcesses.Select(process => process.Pid));
    }

    [Fact]
    public void BuildMenuPlan_MultipleBases_KillAllFirstThenGroupsInSelectionOrder()
    {
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 3, processName: "1cv8.exe", commandLine: "DESIGNER /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 11, 0, 0)),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 9, 0, 0)),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"",
                startTime: new DateTime(2026, 11, 16, 8, 0, 0))
        };

        // Порядок групп — порядок баз в выделении: ЗКГУ, затем БГУ.
        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);

        Assert.True(plan.IsGrouped);
        Assert.Equal(3, plan.Entries.Count);

        // Пункт 2 автора issue: «Завершить все» — отдельный верхний пункт.
        Assert.Equal(KillProcessesMenuEntryKind.KillAll, plan.Entries[0].Kind);
        Assert.Equal("Завершить все", plan.Entries[0].Label);
        Assert.Equal(new[] { 2, 3, 1 }, plan.Entries[0].Processes!.Select(process => process.Pid));

        // ЗКГУ: два процесса — «Завершить все» внутри группы, затем сами процессы.
        Assert.Equal(KillProcessesMenuEntryKind.BaseGroup, plan.Entries[1].Kind);
        Assert.Equal("zkgu (2)", plan.Entries[1].Label);
        Assert.Equal(KillProcessesMenuEntryKind.KillAll, plan.Entries[1].Children![0].Kind);
        Assert.Equal(
            new[] { 2, 3 },
            plan.Entries[1].Children![0].Processes!.Select(process => process.Pid));
        Assert.Equal(
            new[] { 2, 3 },
            plan.Entries[1].Children!.Skip(1).Select(entry => entry.Process!.Pid));

        // БГУ: один процесс — вложенного «Завершить все» нет.
        Assert.Equal(KillProcessesMenuEntryKind.BaseGroup, plan.Entries[2].Kind);
        Assert.Equal("bgu (1)", plan.Entries[2].Label);
        Assert.All(
            plan.Entries[2].Children!,
            entry => Assert.Equal(KillProcessesMenuEntryKind.Process, entry.Kind));
        Assert.Equal(new[] { 1 }, plan.Entries[2].Children!.Select(entry => entry.Process!.Pid));

        Assert.Equal(new[] { 2, 3, 1 }, plan.AllProcesses.Select(process => process.Pid));
    }

    [Fact]
    public void BuildMenuPlan_GroupKillAll_ContainsOnlyGroupProcesses()
    {
        // «Завершить все» внутри группы завершает процессы этой базы, а не всего плана.
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 3, processName: "1cv8.exe", commandLine: "DESIGNER /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 11, 0, 0)),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 9, 0, 0)),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"",
                startTime: new DateTime(2026, 11, 16, 8, 0, 0))
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);
        var groupKillAll = plan.Entries[1].Children![0];

        Assert.Equal(KillProcessesMenuEntryKind.KillAll, groupKillAll.Kind);
        Assert.DoesNotContain(groupKillAll.Processes!, process => process.Pid == 1);
        Assert.Equal(2, groupKillAll.Processes!.Count);
    }

    [Fact]
    public void BuildMenuPlan_BasesWithoutProcesses_AreHidden()
    {
        var withProcess = FileBase("with", @"C:\Bases\With");
        var withoutProcess = FileBase("without", @"C:\Bases\Without");
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\With\"")
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(
            new[] { withoutProcess, withProcess }, processes, Translate);

        // Процессы есть только у одной базы — прежний плоский список.
        Assert.False(plan.IsGrouped);
        Assert.Single(plan.Entries);
        Assert.Equal(KillProcessesMenuEntryKind.Process, plan.Entries[0].Kind);
    }

    [Fact]
    public void BuildMenuPlan_NoProcesses_IsEmpty()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");

        Assert.True(KillProcessesMenuPlanner.BuildMenuPlan(
            new[] { file }, Array.Empty<RunningOneCProcessDetails>(), Translate).IsEmpty);
        Assert.True(KillProcessesMenuPlanner.BuildMenuPlan(
            new[] { file }, null, Translate).IsEmpty);
        Assert.True(KillProcessesMenuPlanner.BuildMenuPlan(
            null, new[] { Process() }, Translate).IsEmpty);
    }

    [Fact]
    public void BuildMenuPlan_UnnamedBase_UsesLocalizedPlaceholder()
    {
        var unnamed = new Infobase
        {
            Id = "unnamed",
            Name = "   ",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\Alpha" }
        };
        var other = FileBase("other", @"C:\Bases\Other");

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(
            new[] { unnamed, other },
            new[]
            {
                Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
                Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\Other\"")
            },
            Translate);

        Assert.Equal("база без имени (1)", plan.Entries[1].Label);
    }

    // ===================== Сводка «Завершить все»: названия баз =====================

    [Fact]
    public void BuildMenuPlan_ProvidesBaseNamesByPid()
    {
        // Доработка автора issue #134 от 16.11.2026: сводка массового завершения
        // перечисляет базы, поэтому план несёт карту «PID → имя базы».
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 3, processName: "1cv8.exe", commandLine: "DESIGNER /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 11, 0, 0)),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 9, 0, 0)),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"",
                startTime: new DateTime(2026, 11, 16, 8, 0, 0))
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);

        Assert.NotNull(plan.BaseNamesByPid);
        Assert.Equal("zkgu", plan.BaseNamesByPid![2]);
        Assert.Equal("zkgu", plan.BaseNamesByPid[3]);
        Assert.Equal("bgu", plan.BaseNamesByPid[1]);
    }

    [Fact]
    public void FormatKillAllSummary_MultipleBases_ListsBaseNamesInSelectionOrder()
    {
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 3, processName: "1cv8.exe", commandLine: "DESIGNER /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 11, 0, 0)),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 9, 0, 0)),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"",
                startTime: new DateTime(2026, 11, 16, 8, 0, 0))
        };
        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            plan.AllProcesses, Array.Empty<int>(), Translate, plan.BaseNamesByPid);

        Assert.Contains("Завершено: 3, ошибок: 0", summary, StringComparison.Ordinal);
        Assert.Contains("Базы: zkgu, bgu", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Не удалось завершить процессы баз", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_FailedProcesses_ListFailedBases()
    {
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\""),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"")
        };
        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);

        // Не удалось завершить процесс базы БГУ — она попадает в строку ошибок.
        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            plan.AllProcesses, new[] { 1 }, Translate, plan.BaseNamesByPid);

        Assert.Contains("Завершено: 1, ошибок: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Базы: zkgu, bgu", summary, StringComparison.Ordinal);
        Assert.Contains("Не удалось завершить процессы баз: bgu", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("zkgu", summary[(summary.IndexOf(
            "Не удалось завершить процессы баз", StringComparison.Ordinal))..], StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_WithoutBaseNames_DerivesNamesFromCommandLine()
    {
        // issue #134 (1.0.1.80): карты «PID → имя базы» нет (WMI не отдал подробности),
        // но командная строка содержит /F или /S — имя базы извлекается из неё,
        // и отчёт больше не ограничивается счётчиками.
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 2, processName: "1cv8.exe", commandLine: "ENTERPRISE /S\"srv1\\beta\"")
        };

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            processes, new[] { 2 }, Translate);

        Assert.Contains("Завершено: 1, ошибок: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Базы: Alpha, beta", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Не удалось определить базы", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_NoCommandLine_ReportsUndeterminedClientProcesses()
    {
        // Если базу определить нельзя, отчёт явно перечисляет PID клиентских процессов,
        // а не показывает «только счётчики» без пояснения (issue #134, 1.0.1.80).
        var processes = new[]
        {
            Process(pid: 1, commandLine: string.Empty),
            Process(pid: 2, commandLine: "   ")
        };

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            processes, new[] { 2 }, Translate);

        Assert.Contains("Завершено: 1, ошибок: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Не удалось определить базы для процессов: 1, 2", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_ServerProcesses_AreNotListedAsUndetermined()
    {
        // Серверные процессы (ragent/rmngr/rphost) к базам не относятся — их PID
        // в строку «не удалось определить базы» не попадают.
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 2, processName: "rphost.exe", commandLine: string.Empty)
        };

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            processes, Array.Empty<int>(), Translate);

        Assert.Contains("Базы: Alpha", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Не удалось определить базы", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_SameBaseSeveralProcesses_BaseListedOnce()
    {
        var alpha = FileBase("alpha", @"C:\Bases\Alpha");
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"")
        };
        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { alpha }, processes, Translate);

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            plan.AllProcesses, Array.Empty<int>(), Translate, plan.BaseNamesByPid);

        Assert.Contains("Базы: alpha", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha, alpha", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMenuItems_GroupedPlan_FlattensProcessesInGroupOrder()
    {
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\"",
                startTime: new DateTime(2026, 11, 16, 10, 0, 0)),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\"",
                startTime: new DateTime(2026, 11, 16, 8, 0, 0))
        };

        var items = KillProcessesMenuPlanner.BuildMenuItems(
            new[] { zkgu, bgu }, processes, Translate);

        Assert.Equal(new[] { 2, 1 }, items.Select(item => item.Process.Pid));
    }

    // ============ Карта «PID → имя базы» для пункта «Утилиты» (v1.0.1.79) ============

    [Fact]
    public void BuildBaseNamesByPid_MapsOnlyMatchedProcesses()
    {
        // issue #134 (v1.0.1.79): пункт «Утилиты → Завершить процессы 1С» завершает
        // все процессы, поэтому карта строится по всему списку баз приложения.
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\""),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\""),
            // Процесс вне списка баз (серверный или чужая база) — без имени базы.
            Process(pid: 3, processName: "rphost.exe", commandLine: "")
        };

        var map = KillProcessesMenuPlanner.BuildBaseNamesByPid(
            new[] { bgu, zkgu }, processes, Translate);

        Assert.Equal(2, map.Count);
        Assert.Equal("bgu", map[1]);
        Assert.Equal("zkgu", map[2]);
        Assert.False(map.ContainsKey(3));
    }

    [Fact]
    public void BuildBaseNamesByPid_SameRulesAsMenuPlan()
    {
        // Карта и план подменю строятся одним шагом — правила совпадают:
        // порядок баз, отбор матчером, PID только у первой найденной базы.
        var bgu = FileBase("bgu", @"C:\Bases\BGU");
        var zkgu = FileBase("zkgu", @"C:\Bases\ZKGU");
        var processes = new[]
        {
            Process(pid: 3, processName: "1cv8.exe", commandLine: "DESIGNER /F\"C:\\Bases\\ZKGU\""),
            Process(pid: 2, commandLine: "ENTERPRISE /F\"C:\\Bases\\ZKGU\""),
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\BGU\""),
            Process(pid: 4, commandLine: "")
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { zkgu, bgu }, processes, Translate);
        var map = KillProcessesMenuPlanner.BuildBaseNamesByPid(new[] { zkgu, bgu }, processes, Translate);

        Assert.NotNull(plan.BaseNamesByPid);
        Assert.Equal(
            plan.BaseNamesByPid!.OrderBy(pair => pair.Key),
            map.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void BuildBaseNamesByPid_UnnamedBase_UsesLocalizedPlaceholder()
    {
        var unnamed = new Infobase
        {
            Id = "unnamed",
            Name = "   ",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\Alpha" }
        };

        var map = KillProcessesMenuPlanner.BuildBaseNamesByPid(
            new[] { unnamed },
            new[] { Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\"") },
            Translate);

        Assert.Equal("база без имени", map[1]);
    }

    [Fact]
    public void BuildBaseNamesByPid_NoBasesOrProcesses_IsEmpty()
    {
        var file = FileBase("file", @"C:\Bases\Alpha");

        Assert.Empty(KillProcessesMenuPlanner.BuildBaseNamesByPid(null, new[] { Process() }, Translate));
        Assert.Empty(KillProcessesMenuPlanner.BuildBaseNamesByPid(new[] { file }, null, Translate));
        Assert.Empty(KillProcessesMenuPlanner.BuildBaseNamesByPid(
            new[] { file }, Array.Empty<RunningOneCProcessDetails>(), Translate));
    }

    [Fact]
    public void FormatKillAllSummary_UtilityPath_WithHelperMap_ListsBases()
    {
        // Сводка пункта «Утилиты → Завершить процессы 1С»: карта из хелпера,
        // базы перечислены, ошибки — отдельной строкой (issue #134, v1.0.1.79).
        var alpha = FileBase("alpha", @"C:\Bases\Alpha");
        var beta = FileBase("beta", @"C:\Bases\Beta");
        var processes = new[]
        {
            Process(pid: 10, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 11, commandLine: "ENTERPRISE /F\"C:\\Bases\\Beta\""),
            Process(pid: 12, processName: "rphost.exe", commandLine: "")
        };

        var map = KillProcessesMenuPlanner.BuildBaseNamesByPid(
            new[] { alpha, beta }, processes, Translate);
        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            processes, new[] { 11 }, Translate, map);

        Assert.Contains("Завершено: 2, ошибок: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Базы: alpha, beta", summary, StringComparison.Ordinal);
        Assert.Contains("Не удалось завершить процессы баз: beta", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKillAllSummary_UtilityPath_WithoutMap_UsesCommandLineFallback()
    {
        // Снимка нет (WMI недоступен) — карты нет, но имена извлекаются из командных
        // строк процессов (issue #134, 1.0.1.80).
        var processes = new[]
        {
            Process(pid: 1, commandLine: "ENTERPRISE /F\"C:\\Bases\\Alpha\""),
            Process(pid: 2, processName: "1cv8.exe", commandLine: "ENTERPRISE /S\"srv1\\Beta\"")
        };

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            processes, new[] { 2 }, Translate, baseNamesByPid: null);

        Assert.Contains("Завершено: 1, ошибок: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Базы: Alpha, Beta", summary, StringComparison.Ordinal);
        Assert.Contains("Не удалось завершить процессы баз: Beta", summary, StringComparison.Ordinal);
    }

    // ===== issue #134 (1.0.1.80): базы без Id и имена из командной строки =====

    [Fact]
    public void BuildMenuPlan_BaseWithoutId_IsGroupedAndNamed()
    {
        // Импорт из кластера (RacInfobaseMapper) создаёт базу с Id = "" — раньше она
        // выпадала из плана и из карты имён, и отчёт показывал только счётчики.
        var clusterBase = new Infobase
        {
            Id = string.Empty,
            Name = "bgu",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1",
                DatabaseName = "bgu"
            }
        };
        var processes = new[]
        {
            Process(pid: 7, processName: "1cv8.exe", commandLine: "ENTERPRISE /S\"srv1\\bgu\"")
        };

        var plan = KillProcessesMenuPlanner.BuildMenuPlan(new[] { clusterBase }, processes, Translate);

        Assert.False(plan.IsEmpty);
        Assert.Single(plan.AllProcesses);
        Assert.NotNull(plan.BaseNamesByPid);
        Assert.Equal("bgu", plan.BaseNamesByPid![7]);

        var summary = KillProcessesMenuPlanner.FormatKillAllSummary(
            plan.AllProcesses, Array.Empty<int>(), Translate, plan.BaseNamesByPid);
        Assert.Contains("Базы: bgu", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBaseNamesByPid_ServerBaseWithoutDatabaseName_MatchesByBaseName()
    {
        // Клиент-серверная база, добавленная только по адресу сервера: DatabaseName пуст,
        // имя базы в кластере совпадает с названием карточки (issue #134, 1.0.1.80).
        var baseByName = new Infobase
        {
            Id = "no-db-name",
            Name = "bgu",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1",
                DatabaseName = string.Empty
            }
        };
        var processes = new[]
        {
            Process(pid: 5, processName: "1cv8.exe", commandLine: "ENTERPRISE /S\"srv1\\bgu\"")
        };

        var map = KillProcessesMenuPlanner.BuildBaseNamesByPid(new[] { baseByName }, processes, Translate);

        Assert.Equal("bgu", map[5]);
    }

    [Theory]
    [InlineData("ENTERPRISE /F\"C:\\Bases\\BGU\"", "BGU")]
    [InlineData("ENTERPRISE /F C:\\Bases\\BGU\\", "BGU")]
    [InlineData("ENTERPRISE /S\"srv1\\bgu\"", "bgu")]
    [InlineData("ENTERPRISE /IBName\"bgu\"", "bgu")]
    [InlineData("ENTERPRISE /IBConnectionString\"Srvr=srv1;Ref=bgu;\"", "bgu")]
    [InlineData("ENTERPRISE /IBConnectionString\"File=C:\\Bases\\BGU;\"", "BGU")]
    [InlineData("ENTERPRISE", null)]
    [InlineData("", null)]
    public void DescribeBaseFromCommandLine_ExtractsBaseName(string commandLine, string? expected)
    {
        Assert.Equal(expected, KillProcessesMenuPlanner.DescribeBaseFromCommandLine(commandLine));
    }

    [Fact]
    public void BothPlatforms_UtilityKillProcesses_LogReportDiagnostics()
    {
        // Диагностика причины «только счётчики» пишется в журнал в обоих путях.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Tools.cs"));
        var avalonia = ReadProjectFile(
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Tools.cs"));

        Assert.Contains("LogKillReportDiagnostics(", wpf, StringComparison.Ordinal);
        Assert.Contains("LogKillReportDiagnostics(", avalonia, StringComparison.Ordinal);
        Assert.Contains("имён баз", wpf, StringComparison.Ordinal);
        Assert.Contains("имён баз", avalonia, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("ru.json", "Завершить все", "база без имени")]
    [InlineData("en.json", "Terminate all", "unnamed infobase")]
    public void Localization_HasKillAllAndGroupingKeys(
        string file, string killAllTitle, string unknownBase)
    {
        var strings = LoadStrings(file);

        Assert.Equal(killAllTitle, strings[KillProcessesMenuPlanner.KillAllTitleKey]);
        Assert.Equal(unknownBase, strings[KillProcessesMenuPlanner.UnknownBaseKey]);
        Assert.Equal("{0} ({1})", strings[KillProcessesMenuPlanner.GroupHeaderFormatKey]);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillAllConfirmFormatKey], StringComparison.Ordinal);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillAllSummaryFormatKey], StringComparison.Ordinal);
        Assert.Contains("{1}", strings[KillProcessesMenuPlanner.KillAllSummaryFormatKey], StringComparison.Ordinal);

        // Сводка массового завершения перечисляет базы (доработка автора issue #134).
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillAllBasesFormatKey], StringComparison.Ordinal);
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillAllFailedBasesFormatKey], StringComparison.Ordinal);
        // issue #134 (1.0.1.80): строка о процессах, для которых база не определена.
        Assert.Contains("{0}", strings[KillProcessesMenuPlanner.KillAllUndeterminedFormatKey], StringComparison.Ordinal);
        Assert.Equal(", ", strings[KillProcessesMenuPlanner.BasesSeparatorKey]);
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
        Assert.Contains(KillProcessesMenuPlanner.KillAllTitleKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillAllConfirmFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillAllSummaryFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillAllBasesFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillAllFailedBasesFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.KillAllUndeterminedFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.BasesSeparatorKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.GroupHeaderFormatKey, ru);
        Assert.Contains(KillProcessesMenuPlanner.UnknownBaseKey, ru);
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
        Assert.Contains("KillProcessesMenuPlanner.BuildMenuPlan(", source, StringComparison.Ordinal);
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
        Assert.Contains("KillProcessesMenuPlanner.BuildMenuPlan(", source, StringComparison.Ordinal);
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
    public void BothPlatforms_RenderGroupedPlanAndKillAll()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            // Вложенные группы по базам и верхний пункт «Завершить все» — в обеих оболочках.
            Assert.Contains("KillProcessesMenuEntryKind.KillAll", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessesMenuEntryKind.BaseGroup", source, StringComparison.Ordinal);
            Assert.Contains("plan.AllProcesses", source, StringComparison.Ordinal);
            // Имена баз для сводки берутся из плана (доработка автора issue #134).
            Assert.Contains("plan.BaseNamesByPid", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessesMenuPlanner.KillAllConfirmFormatKey", source, StringComparison.Ordinal);
            // Сводку собирает общий слой — в оболочках нет хардкода текста отчёта.
            Assert.Contains("KillProcessesMenuPlanner.FormatKillAllSummary(", source, StringComparison.Ordinal);
            Assert.Contains("dialogs.ShowWarning(summary, title);", source, StringComparison.Ordinal);
            Assert.Contains("dialogs.ShowInfo(summary, title);", source, StringComparison.Ordinal);
            // «Завершить все» внутри подменю базы: список процессов берётся из элемента
            // плана (entry.Processes), а не из всего плана (issue #134, доработка автора).
            Assert.Contains("entry.Processes", source, StringComparison.Ordinal);
        }

        Assert.Contains("CreateKillAllMenuItem", wpf, StringComparison.Ordinal);
        Assert.Contains("OnKillAllProcesses_Click", wpf, StringComparison.Ordinal);
        Assert.Contains("OnKillAllProcessesClick", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPlatforms_GroupChildrenGoThroughKillAllAwareFactory()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        // Дочерние пункты группы создаются тем же методом, что понимает KillAll —
        // иначе вложенный «Завершить все» отрисовался бы как пункт процесса.
        Assert.Contains("CreateKillProcessMenuItem(", wpf, StringComparison.Ordinal);
        Assert.Contains("child, plan.AllProcesses", wpf, StringComparison.Ordinal);
        Assert.Contains("CreateAvaloniaKillProcessMenuItem(", avalonia, StringComparison.Ordinal);
        Assert.Contains("child, plan.AllProcesses", avalonia, StringComparison.Ordinal);
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

    // ====== Source-гварды: пункт «Утилиты → Завершить процессы 1С» (v1.0.1.79) ======

    [Fact]
    public void Wpf_UtilityKillProcesses_ReportsBasesAndFailedPids()
    {
        // issue #134: автор не подтвердил исправление 1.0.1.78 — пункт «Утилиты»
        // показывал только счётчик. Теперь сводка строится общим слоем.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Tools.cs"));
        var method = ExtractMethod(source, "private void KillOneCProcesses(object? parameter)");

        // Снимок с командными строками, карта «PID → имя базы», сводка общего слоя.
        Assert.Contains("GetRunningDetails()", method, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.BuildBaseNamesByPid(", method, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.FormatKillAllSummary(", method, StringComparison.Ordinal);
        // Ошибки завершения определяются повторным опросом процессов.
        Assert.Contains("FindAliveOneCProcessIds(", method, StringComparison.Ordinal);

        // Сообщение собирает общий слой: прежний текст со счётчиком остался только
        // запасным вариантом, когда снимка процессов получить не удалось.
        var summaryIndex = method.IndexOf(
            "KillProcessesMenuPlanner.FormatKillAllSummary(", StringComparison.Ordinal);
        var legacyIndex = method.IndexOf("Main.ProcessesKilled", StringComparison.Ordinal);
        Assert.True(summaryIndex >= 0, "Сводка общего слоя не используется.");
        Assert.True(legacyIndex > summaryIndex,
            "Сообщение только из Main.ProcessesKilled осталось основным путём.");
    }

    [Fact]
    public void Avalonia_UtilityKillProcesses_ReportsBasesAndFailedPids()
    {
        var source = ReadProjectFile(
            Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Tools.cs"));
        var method = ExtractMethod(source, "public void KillOneCProcesses()");

        Assert.Contains("GetRunningDetails()", method, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.BuildBaseNamesByPid(", method, StringComparison.Ordinal);
        Assert.Contains("KillProcessesMenuPlanner.FormatKillAllSummary(", method, StringComparison.Ordinal);
        Assert.Contains("FindStillRunningPids(", method, StringComparison.Ordinal);

        // Прежнего сообщения «только счётчик» в этом пункте больше нет.
        Assert.DoesNotContain("Main.ProcessesKilled", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Main.ProcessesKillFailed", method, StringComparison.Ordinal);
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
        [KillProcessesMenuPlanner.KillAllTitleKey] = "Завершить все",
        [KillProcessesMenuPlanner.KillAllConfirmFormatKey] = "Завершить все процессы ({0})?",
        [KillProcessesMenuPlanner.KillAllSummaryFormatKey] = "Завершено: {0}, ошибок: {1}",
        [KillProcessesMenuPlanner.KillAllBasesFormatKey] = "Базы: {0}",
        [KillProcessesMenuPlanner.KillAllFailedBasesFormatKey] = "Не удалось завершить процессы баз: {0}",
        [KillProcessesMenuPlanner.KillAllUndeterminedFormatKey] = "Не удалось определить базы для процессов: {0}",
        [KillProcessesMenuPlanner.BasesSeparatorKey] = ", ",
        [KillProcessesMenuPlanner.GroupHeaderFormatKey] = "{0} ({1})",
        [KillProcessesMenuPlanner.UnknownBaseKey] = "база без имени",
    };

    private static string Translate(string key) =>
        RuStrings.TryGetValue(key, out var value) ? value : key;

    /// <summary>
    /// Тело метода из исходника: от подписи до закрывающей скобки метода. Нужен
    /// source-гвардам, чтобы проверять конкретный метод, а не весь файл.
    /// </summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Метод не найден: {signature}");

        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"Не найдено тело метода: {signature}");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        Assert.Fail($"Не закрыто тело метода: {signature}");
        return string.Empty;
    }

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
