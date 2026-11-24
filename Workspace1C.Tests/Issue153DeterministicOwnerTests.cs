using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #153 (1.0.1.115): недетерминированный индикатор запуска при двух записях
/// базы с полностью одинаковыми настройками запуска. Раньше владелец процесса
/// выбирался через <c>GroupBy(GetBaseKey).First()</c> и <c>FirstOrDefault</c> по
/// порядку коллекции — результат зависел от порядка перечисления баз/процессов и
/// от хеширования в <c>Dictionary</c>/<c>HashSet</c>, поэтому индикатор «прыгал»
/// между записями. Теперь выбор идёт по стабильному ключу записи
/// (<see cref="RunningInfobaseMatcher.GetRecordKey"/>), а при полном совпадении —
/// по индексу в сохранённом списке.
/// </summary>
public sealed class Issue153DeterministicOwnerTests
{
    private const string ConnectionCommand =
        @"""C:\Program Files\1cv8\8.3.27.2214\bin\1cv8.exe"" ENTERPRISE /S ""srvc-1c-doc\doc3""";

    private const string FileCommand =
        @"""C:\Program Files\1cv8\8.3.27.2214\bin\1cv8.exe"" ENTERPRISE /F ""C:\Bases\ib""";

    private static Infobase ServerBase(
        string id,
        string name,
        AuthenticationMode mode = AuthenticationMode.Prompt,
        string user = "",
        string database = "doc3") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srvc-1c-doc",
            DatabaseName = database,
            AuthenticationMode = mode,
            User = user
        }
    };

    private static Infobase OsAuthBase(string id, string name = "1С ДО 3.0 - РАБОЧАЯ БАЗА") =>
        ServerBase(id, name, AuthenticationMode.Windows);

    private static Infobase AdminBase(
        string id = "id-admin", string name = "1С ДО 3.0 - РАБОЧАЯ БАЗА - АДМИН") =>
        ServerBase(id, name, AuthenticationMode.Credentials, "Салихов Администратор");

    private static Infobase FileBase(string id, string name = "Файловая") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\ib" }
    };

    private static RunningOneCProcess Process(string? rawUser = null, string command = ConnectionCommand)
    {
        var line = command;
        if (!string.IsNullOrEmpty(rawUser))
            line += " /N " + rawUser;
        return new RunningOneCProcess("1cv8.exe", line);
    }

    // ===================== Выбор владельца =====================

    [Fact]
    public void SelectOwner_ShuffledInput_SameOwnerAcrossPermutations()
    {
        // Две записи с одинаковыми настройками и разными Id. Порядок входа не
        // должен влиять: владелец — запись с минимальным стабильным ключом (Id).
        var first = OsAuthBase("id-b");
        var second = OsAuthBase("id-a");
        var commandLine = Process().CommandLine;

        var ownerFromFirst = RunningInfobaseMatcher.SelectOwnerForProcess(
            new[] { first, second }, commandLine);
        var ownerFromSecond = RunningInfobaseMatcher.SelectOwnerForProcess(
            new[] { second, first }, commandLine);

        Assert.NotNull(ownerFromFirst);
        Assert.NotNull(ownerFromSecond);
        Assert.Equal("id-a", ownerFromFirst!.Id);
        Assert.Same(ownerFromFirst, ownerFromSecond);
    }

    [Fact]
    public void SelectOwner_IdenticalSettings_DifferentIds_StableByRecordKey()
    {
        // Полностью одинаковые настройки запуска, разные Id: правило — меньший
        // GetRecordKey по OrdinalIgnoreCase (не «случайная» запись).
        var recordA = OsAuthBase("guid-b");
        var recordB = OsAuthBase("guid-a");
        var commandLine = Process().CommandLine;

        var expectedKey = new[] { recordA, recordB }
            .Select(ib => RunningInfobaseMatcher.GetRecordKey(ib))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .First();

        foreach (var permutation in Permutations(recordA, recordB))
        {
            var owner = RunningInfobaseMatcher.SelectOwnerForProcess(permutation, commandLine);
            Assert.NotNull(owner);
            Assert.Equal(expectedKey, RunningInfobaseMatcher.GetRecordKey(owner!));
        }
    }

    [Fact]
    public void SelectOwner_IdenticalSettings_EmptyIds_StableByStoredIndex()
    {
        // Полностью одинаковые записи без Id различаются только индексом в
        // сохранённом списке: владелец — первая запись сохранённого порядка,
        // независимо от порядка перечисления кандидатов.
        var storedFirst = OsAuthBase(string.Empty);
        var storedSecond = OsAuthBase(string.Empty);
        var storedOrder = new Dictionary<Infobase, int>
        {
            [storedFirst] = 0,
            [storedSecond] = 1
        };
        int ResolveStoredIndex(Infobase infobase) => storedOrder[infobase];

        var commandLine = Process().CommandLine;

        foreach (var permutation in Permutations(storedFirst, storedSecond))
        {
            var owner = RunningInfobaseMatcher.SelectOwnerForProcess(
                permutation, commandLine, resolveEnterpriseUser: null,
                recordIndexResolver: ResolveStoredIndex);

            Assert.Same(storedFirst, owner);
        }
    }

    // ===================== MatchProcesses =====================

    [Fact]
    public void MatchProcesses_IdenticalRecords_OnlyOneRowGetsIndicator()
    {
        // Две полностью одинаковые записи без Id: результат содержит ровно один
        // ключ — записи с индексом 0. Вторая строка индикатор не получает.
        var first = OsAuthBase(string.Empty);
        var second = OsAuthBase(string.Empty);

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { first, second }, new[] { Process() });

        Assert.Single(result);
        Assert.True(result.ContainsKey(RunningInfobaseMatcher.GetRecordKey(first, 0)));
        Assert.False(result.ContainsKey(RunningInfobaseMatcher.GetRecordKey(second, 1)));
    }

    [Fact]
    public void MatchProcesses_DuplicateSettingsDifferentIds_OnlyOneEntryGetsIndicator()
    {
        // Две записи с одинаковыми настройками, разные Id: индикатор — у записи с
        // минимальным стабильным ключом, у второй ключа в результате нет.
        var recordB = OsAuthBase("id-b");
        var recordA = OsAuthBase("id-a");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { recordB, recordA }, new[] { Process() });

        Assert.Single(result);
        Assert.True(result.ContainsKey("id-a"));
        Assert.False(result.ContainsKey("id-b"));
    }

    [Fact]
    public void MatchProcesses_Repeated100Times_SameOwner()
    {
        // Повторяемость: 100 прогонов с перемешанным порядком входа дают одного
        // и того же владельца.
        var recordB = OsAuthBase("id-b");
        var recordA = OsAuthBase("id-a");
        var random = new Random(153);
        string? expectedKey = null;

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var order = random.Next(2) == 0
                ? new[] { recordA, recordB }
                : new[] { recordB, recordA };

            var key = RunningInfobaseMatcher.MatchProcesses(order, new[] { Process() })
                .Keys.Single();

            expectedKey ??= key;
            Assert.Equal(expectedKey, key);
        }

        Assert.Equal("id-a", expectedKey);
    }

    [Fact]
    public void MatchProcesses_Idempotent_SameResultOnRepeatedCalls()
    {
        var recordB = OsAuthBase("id-b");
        var recordA = OsAuthBase("id-a");

        var first = RunningInfobaseMatcher.MatchProcesses(
            new[] { recordB, recordA }, new[] { Process() });
        var second = RunningInfobaseMatcher.MatchProcesses(
            new[] { recordB, recordA }, new[] { Process() });

        Assert.Equal(first.Keys.OrderBy(k => k, StringComparer.Ordinal),
            second.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(first.Values.Sum(list => list.Count), second.Values.Sum(list => list.Count));
    }

    [Fact]
    public void MatchProcesses_ProcessOrderIndependent()
    {
        // Порядок процессов в списке не влияет на привязку: у каждой записи
        // остаётся свой процесс (сценарий «две учётки»).
        var osAuth = OsAuthBase("id-os");
        var admin = AdminBase();
        var osProcess = Process();
        var adminProcess = Process("Салихов Администратор");

        var forward = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin }, new[] { osProcess, adminProcess });
        var reverse = RunningInfobaseMatcher.MatchProcesses(
            new[] { admin, osAuth }, new[] { adminProcess, osProcess });

        Assert.Single(forward["id-os"]);
        Assert.Single(forward["id-admin"]);
        Assert.Single(reverse["id-os"]);
        Assert.Single(reverse["id-admin"]);

        Assert.DoesNotContain("/N", reverse["id-os"][0].CommandLine, StringComparison.Ordinal);
        Assert.Contains("/N", reverse["id-admin"][0].CommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FindOwner_MultipleDirectMatches_DeterministicAcrossCollectionOrder()
    {
        // Fallback-путь (FindOwner) тоже не должен зависеть от порядка коллекции:
        // при двух подходящих записях возвращается одна и та же (меньший ключ).
        var recordB = OsAuthBase("id-b");
        var recordA = OsAuthBase("id-a");
        var commandLine = Process().CommandLine;

        var forward = RunningInfobaseMatcher.FindOwner(new[] { recordB, recordA }, commandLine);
        var reverse = RunningInfobaseMatcher.FindOwner(new[] { recordA, recordB }, commandLine);

        Assert.NotNull(forward);
        Assert.NotNull(reverse);
        Assert.Equal("id-a", forward!.Id);
        Assert.Same(forward, reverse);
    }

    [Fact]
    public void GetRecordKey_SameId_SameKey_DifferentIndexIgnored()
    {
        // Одинаковый непустой Id — одна и та же запись: ключ не зависит от индекса,
        // поэтому в результате и в применении флагов она схлопывается в одну строку.
        var first = OsAuthBase("id-same");
        var second = OsAuthBase("id-same");

        Assert.Equal(
            RunningInfobaseMatcher.GetRecordKey(first, 0),
            RunningInfobaseMatcher.GetRecordKey(second, 5));
        Assert.Equal("id-same", RunningInfobaseMatcher.GetRecordKey(first, 0));
    }

    [Fact]
    public void GetRecordKey_NoId_DifferentIndex_DifferentKey()
    {
        // Без Id ключ записи различает полностью одинаковые записи по индексу:
        // индикатор достаётся первой строке, вторая остаётся без него.
        var first = OsAuthBase(string.Empty);
        var second = OsAuthBase(string.Empty);

        var firstKey = RunningInfobaseMatcher.GetRecordKey(first, 0);
        var secondKey = RunningInfobaseMatcher.GetRecordKey(second, 1);

        Assert.NotEqual(firstKey, secondKey);
        Assert.StartsWith("rec#", firstKey, StringComparison.Ordinal);
        Assert.Contains(RunningInfobaseMatcher.GetConnectionKey(first), firstKey, StringComparison.Ordinal);
    }

    // ===================== Сохранённые сценарии 1.0.1.113 =====================

    [Fact]
    public void MatchProcesses_FileBase_StillMatchedToSingleEntry()
    {
        var file = FileBase("id-file");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { file }, new[] { Process(command: FileCommand) });

        Assert.Single(result);
        Assert.True(result.ContainsKey("id-file"));
        Assert.Single(result["id-file"]);
    }

    [Fact]
    public void MatchProcesses_ServerBase_UserCaseQuotesSpaces_StillMatchesCredentialsEntry()
    {
        var osAuth = OsAuthBase("id-os");
        var admin = AdminBase();

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { admin, osAuth },
            new[] { Process("\"  салихов администратор  \"") });

        Assert.False(result.ContainsKey("id-os"));
        Assert.True(result.ContainsKey("id-admin"));
        Assert.Single(result["id-admin"]);
    }

    // ===================== Source-гварды =====================

    [Fact]
    public void Source_RecordsDeterministicRuleAndDebugFields()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "RunningInfobaseMatcher.cs");

        Assert.Contains("GetConnectionKey", code, StringComparison.Ordinal);
        Assert.Contains("GetRecordKey", code, StringComparison.Ordinal);
        Assert.Contains("детерминирован", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recordKey=", code, StringComparison.Ordinal);
        Assert.Contains("connectionKey=", code, StringComparison.Ordinal);
        Assert.Contains("index=", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_ApplyRunningProcesses_LooksUpByRecordKey()
    {
        var code = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Running.cs");

        Assert.Contains("RunningInfobaseMatcher.GetRecordKey(ib, baseIndex)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetValue(RunningInfobaseMatcher.GetBaseKey(ib)", code, StringComparison.Ordinal);
    }

    // ===================== Вспомогательное =====================

    private static IEnumerable<Infobase[]> Permutations(Infobase first, Infobase second)
    {
        yield return new[] { first, second };
        yield return new[] { second, first };
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
