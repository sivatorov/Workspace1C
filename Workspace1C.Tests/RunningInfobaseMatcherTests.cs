using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сопоставления командной строки процесса 1С с информационной базой
/// (индикатор «база сейчас запущена», <see cref="RunningInfobaseMatcher"/>).
/// </summary>
public sealed class RunningInfobaseMatcherTests
{
    private static Infobase FileBase(string path) => new()
    {
        Id = "f1",
        Name = "Файловая",
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static Infobase ServerBase(string server, string dbName) => new()
    {
        Id = "s1",
        Name = "Серверная",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = dbName
        }
    };

    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /F ""C:\Bases\Моя база"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8.exe"" DESIGNER /F""C:\Bases\Моя база"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /FC:\Bases\Моя база", true)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /F ""c:\bases\МОЯ БАЗА\ """, true)] // регистр и хвост-разделитель
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server1\trade /N Иван", false)] // другая база
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE", false)]                          // без ключа /F
    [InlineData("", false)]
    public void FileBase_MatchesBySlashF(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(FileBase(@"C:\Bases\Моя база"), commandLine));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /S ""server1\trade"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8.exe"" DESIGNER /Sserver1\trade", true)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /F ""C:\Bases\ib""", false)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server2\trade", false)] // другой сервер
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server1\other", false)] // другая база на том же сервере
    public void ServerBase_MatchesBySlashS(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(ServerBase("server1", "trade"), commandLine));
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\trade"" /N Иван", true)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S server1\TRADE", true)]                 // регистр имени базы
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\База с пробелами""", true)]  // кавычки и пробелы в имени
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server2\trade""", false)]            // другой сервер
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\other""", false)]            // другая база
    [InlineData(@"""1cv8c.exe"" ENTERPRISE", false)]                                 // нет ключа /S
    public void ServerBase_WithPortInAddress_MatchesSlashS(string commandLine, bool expected)
    {
        // issue #342: у базы в списке адрес сервера с портом («server1:1541»), а командная
        // строка процесса содержит «/S server1\БД» без порта — раньше это не сопоставлялось.
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(
            ServerBase("server1:1541", commandLine.Contains("База с пробелами") ? "База с пробелами" : "trade"),
            commandLine));
    }

    [Fact]
    public void WebBase_NeverMatches()
    {
        var web = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://srv/base" }
        };

        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(web, @"""1cv8c.exe"" ENTERPRISE /WS http://srv/base"));
    }

    [Fact]
    public void NullCommandLine_DoesNotThrow()
    {
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(FileBase(@"C:\Bases\ib"), null));
    }

    [Fact]
    public void SlashOutKey_IsNotConfusedWithSlashS()
    {
        // /Out — не /S: ключ должен быть отдельным токеном.
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(
            ServerBase("server1", "trade"),
            @"""1cv8.exe"" DESIGNER /Out C:\log.txt /S server1\trade-mismatch"));
    }

    [Fact]
    public void PathsEqual_IgnoresCaseAndTrailingSeparators()
    {
        Assert.True(RunningInfobaseMatcher.PathsEqual(@"C:\Bases\ib\", @"c:\Bases\IB"));
        Assert.False(RunningInfobaseMatcher.PathsEqual(@"C:\Bases\ib", @"C:\Bases\ib2"));
        Assert.False(RunningInfobaseMatcher.PathsEqual(null, @"C:\x"));
    }

    // ===================== Issue #103: запуск отладки из Конфигуратора =====================

    private static Infobase NamedBase(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\" + id }
    };

    private static RunningOneCProcess Process(string commandLine) => new("1cv8c.exe", commandLine);

    [Fact]
    public void MatchProcesses_DebugLaunchFromConfigurator_MatchesByIbName()
    {
        // Отладка из Конфигуратора: клиент запускается «ENTERPRISE /IBName <имя>» без
        // /F//S — процесс должен связаться с базой по уникальному имени (issue #103).
        var base1 = NamedBase("f1", "Demo 27");
        var processes = new[]
        {
            Process(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /IBName ""Demo 27"""),
            Process(@"""C:\Program Files\1cv8\1cv8.exe"" ENTERPRISE /IBName Demo 27 /N Иван")
        };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1 }, processes);

        Assert.True(matched.TryGetValue("f1", out var list));
        Assert.Equal(2, list!.Count);
    }

    [Fact]
    public void MatchProcesses_DirectKeysTakePriorityOverIbName()
    {
        // Процесс с прямым ключом /F не должен попасть во второй проход: база «Файловая»
        // получает его по /F, а процесс с /IBName другой базы — не сопоставляется никому.
        var base1 = NamedBase("f1", "Файловая");
        var base2 = NamedBase("f2", "Другая");
        var processes = new[]
        {
            Process(@"""1cv8c.exe"" ENTERPRISE /F ""C:\Bases\f1"""),
            Process(@"""1cv8c.exe"" ENTERPRISE /IBName Чужая")
        };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.True(matched.TryGetValue("f1", out var list1));
        Assert.Single(list1!);
        Assert.False(matched.ContainsKey("f2"));
    }

    [Fact]
    public void MatchProcesses_DuplicateNames_AreNotMatchedByIbName()
    {
        // Одинаковые имена в разных базах — матч по /IBName не выполняется (защита от
        // ложных срабатываний): процесс не должен «пометить» обе базы запущенными.
        var base1 = NamedBase("f1", "Бухгалтерия");
        var base2 = NamedBase("f2", "Бухгалтерия");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName Бухгалтерия") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.Empty(matched);
    }

    [Fact]
    public void MatchProcesses_IbNameIsCaseAndQuotesInsensitive()
    {
        var base1 = NamedBase("f1", "Demo 27");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName ""demo 27"" /C Разрешение") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1 }, processes);

        Assert.True(matched.TryGetValue("f1", out var list));
        Assert.Single(list!);
    }

    [Fact]
    public void MatchProcesses_EmptyInputs_ReturnEmpty()
    {
        var matched1 = RunningInfobaseMatcher.MatchProcesses(Array.Empty<Infobase>(),
            new[] { Process("1cv8c.exe ENTERPRISE") });
        Assert.Empty(matched1);

        var matched2 = RunningInfobaseMatcher.MatchProcesses(new[] { NamedBase("f1", "X") },
            Array.Empty<RunningOneCProcess>());
        Assert.Empty(matched2);
    }
}
