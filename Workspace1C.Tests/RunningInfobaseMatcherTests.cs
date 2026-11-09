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
    // Формат Конфигуратора (issue #103): ключ без пробела, кавычки, прямые слеши.
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S""server1/trade"" /N Иван", true)]
    [InlineData(@"""1cv8.exe"" DESIGNER /S""server1/trade""", true)]
    // Пробел + прямые слеши без кавычек.
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S server1/trade /N Иван", true)]
    // Регресс: классический формат с «\» по-прежнему сопоставляется.
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\trade""", true)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S server1\trade", true)]
    // Сервер с портом: порт игнорируется, сопоставляется.
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S""server1:1541/trade""", true)]
    // Негатив: другая база.
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S""other/trade""", false)]
    public void ServerBase_MatchesSlashSWithForwardSlashes(string commandLine, bool expected)
    {
        // issue #103: Конфигуратор запускает клиент серверной базы с ключом
        // вида /S"server/base" — без пробела между ключом и значением и с
        // прямыми слешами. Раньше такой формат не сопоставлялся.
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(ServerBase("server1", "trade"), commandLine));
    }

    [Fact]
    public void ServerBase_MixedSeparator_SplitsByLastOne()
    {
        // Смешанное значение «host\db/trade»: разделитель — последний из «\»/«/»,
        // то есть «/»; сервер «host\db», база «trade».
        Assert.True(RunningInfobaseMatcher.MatchesCommandLine(
            ServerBase(@"host\db", "trade"),
            @"""1cv8c.exe"" ENTERPRISE /S""host\db/trade"""));
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

    private static Infobase NamedServerBase(string id, string name, string server, string dbName) => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = dbName
        }
    };

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

    // ============ Issue #103: /IBConnectionString (отладка серверных баз из Конфигуратора) ============

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""trade"";""", true)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""""server1"""";Ref=""""trade"""";""", true)] // 1С-экранирование кавычек
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1:1541"";Ref=""trade"";""", true)]   // порт у сервера
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""tcp://server1:1541"";Ref=""trade"";""", true)] // префикс протокола
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""SERVER1"";Ref=""TRADE"";""", true)]         // регистр
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""other"";""", false)]        // другая база на сервере
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server2"";Ref=""trade"";""", false)]        // тот же Ref на другом сервере
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";""", false)]                      // нет Ref
    [InlineData(@"""1cv8c.exe"" ENTERPRISE", false)]                                                                // ключа нет вовсе
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S server1\trade", true)]                                                // прямые ключи не сломаны
    public void ServerBase_MatchesByIbConnectionString(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(ServerBase("server1", "trade"), commandLine));
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""File=""C:\Bases\ib"";""", true)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""File=""C:\Bases\other"";""", false)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""ib"";""", false)] // серверная строка для файловой базы
    public void FileBase_MatchesByIbConnectionString(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(FileBase(@"C:\Bases\ib"), commandLine));
    }

    [Fact]
    public void MatchProcesses_IbConnectionString_ServerBaseDuringDebugFromConfigurator()
    {
        // Отладка серверной базы из Конфигуратора: клиент получает строку соединения
        // целиком, без /S и без /IBName — процесс должен связаться с базой (issue #103).
        var base1 = NamedServerBase("s1", "Trade", "server1", "trade");
        var processes = new[]
        {
            Process(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""trade"";""")
        };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1 }, processes);

        Assert.True(matched.TryGetValue("s1", out var list));
        Assert.Single(list!);
    }

    [Fact]
    public void MatchProcesses_IbConnectionString_DuplicateNames_MatchedWhenServerHintPresent()
    {
        // Неуникальные имена баз-«тёзок» на разных серверах: сопоставление выполняется
        // по уточнению сервера из /IBConnectionString; процесс без уточнения не
        // сопоставляется никому (защита от ложных срабатываний сохраняется).
        var base1 = NamedServerBase("s1", "Бухгалтерия", "server1", "trade");
        var base2 = NamedServerBase("s2", "Бухгалтерия", "server2", "trade");
        var processes = new[]
        {
            Process(@"""1cv8c.exe"" ENTERPRISE /IBName Бухгалтерия /IBConnectionString ""Srvr=""server2"";Ref=""trade"";"""),
            Process(@"""1cv8c.exe"" ENTERPRISE /IBName Бухгалтерия")
        };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.True(matched.TryGetValue("s2", out var list2));
        Assert.Single(list2!);
        Assert.False(matched.ContainsKey("s1"));
    }

    [Fact]
    public void MatchProcesses_DuplicateNames_MatchedWhenSlashSHintPresent()
    {
        // То же уточнение через прямой ключ /S у баз-«тёзок».
        var base1 = NamedServerBase("s1", "Бухгалтерия", "server1", "trade");
        var base2 = NamedServerBase("s2", "Бухгалтерия", "server2", "trade");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName Бухгалтерия /S server2\trade") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.True(matched.TryGetValue("s2", out var list2));
        Assert.Single(list2!);
        Assert.False(matched.ContainsKey("s1"));
    }

    [Fact]
    public void ParseConnectionString_HandlesEscapedQuotesAndSemicolonsInValues()
    {
        // Рваные строки: 1С-экранирование кавычек и «;» внутри значения в кавычках.
        var parts = RunningInfobaseMatcher.ParseConnectionString(@"Srvr=""server1"";Ref=""Trade"";Usr=""iv;an"";");
        Assert.Equal("server1", parts["srvr"]);  // ключи без учёта регистра
        Assert.Equal("Trade", parts["REF"]);
        Assert.Equal("iv;an", parts["usr"]);
    }

    // ============ Issue #103 (v1.0.1.47): расширение матчинга серверных баз ============

    [Fact]
    public void MatchProcesses_IbName_MatchesDatabaseName_InsteadOfDisplayName()
    {
        // Г1: имя в Конфигураторе = имя базы в кластере (DatabaseName), а не
        // отображаемое имя — проход /IBName должен сопоставить и такой случай.
        var base1 = NamedServerBase("s1", "Торговля (внешний)", "server1", "trade_db");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName trade_db") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1 }, processes);

        Assert.True(matched.TryGetValue("s1", out var list));
        Assert.Single(list!);
    }

    [Fact]
    public void MatchProcesses_IbName_MatchesDisplayName_StillWorks()
    {
        // Регресс: совпадение с отображаемым именем (прежнее поведение) сохранено.
        var base1 = NamedServerBase("s1", "Торговля (внешний)", "server1", "trade_db");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName ""Торговля (внешний)""") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1 }, processes);

        Assert.True(matched.TryGetValue("s1", out var list));
        Assert.Single(list!);
    }

    [Fact]
    public void MatchProcesses_IbName_NonUniqueName_ButUniqueDatabaseName_Matches()
    {
        // Г1/Г2: отображаемые имена неуникальны (пары «локальный/внешний сервер»),
        // но имена баз в кластере различаются — сопоставляем по уникальному DatabaseName.
        var base1 = NamedServerBase("s1", "Бухгалтерия", "server1", "trade1");
        var base2 = NamedServerBase("s2", "Бухгалтерия", "server2", "trade2");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName trade2") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.True(matched.TryGetValue("s2", out var list2));
        Assert.Single(list2!);
        Assert.False(matched.ContainsKey("s1"));
    }

    [Fact]
    public void MatchProcesses_IbName_AmbiguousAcrossNameAndDatabaseName_Skipped()
    {
        // Имя процесса совпадает и с именем одной базы, и с DatabaseName другой —
        // неоднозначно, не сопоставляем никому (безопаснее ложных значков).
        var base1 = NamedServerBase("s1", "Общее имя", "server1", "trade1");
        var base2 = NamedServerBase("s2", "Другая", "server2", "Общее имя");
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /IBName Общее имя") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { base1, base2 }, processes);

        Assert.Empty(matched);
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1,host2:1541"";Ref=""trade"";""", true)]  // любой адрес списка
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""host2,server1"";Ref=""trade"";""", true)]       // порядок не важен
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""host3,host4"";Ref=""trade"";""", false)]      // список без адреса базы
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""other"";""", false)]          // Ref на другом... другая база
    public void ServerBase_MatchesByIbConnectionString_ServerList(string commandLine, bool expected)
    {
        // Г3: Srvr — список адресов через запятую (отказоустойчивый кластер).
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(ServerBase("server1", "trade"), commandLine));
    }

    [Fact]
    public void ServerBase_EmptyDatabaseName_MatchesByOwnConnectionString()
    {
        // Г3: DatabaseName в карточке пуст — сопоставление Ref↔Ref (из отображаемого
        // имени/собственной строки соединения) в паре с адресом сервера из списка Srvr.
        var base1 = new Infobase
        {
            Id = "s1",
            Name = "trade",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "server1",
                DatabaseName = ""
            }
        };
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""host2,server1:1541"";Ref=""trade"";""";

        Assert.True(RunningInfobaseMatcher.MatchesCommandLine(base1, commandLine));

        // Другой Ref на том же сервере — не сопоставляется.
        var wrongRef = @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""host2,server1:1541"";Ref=""other"";""";
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(base1, wrongRef));

        // Тот же Ref, но ни один адрес списка не совпадает с сервером базы — не сопоставляется.
        var wrongServer = @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""host2,host3"";Ref=""trade"";""";
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(base1, wrongServer));
    }

    [Fact]
    public void TryExtractValue_QuotedValueWithSlashInside_IsExtractedWhole()
    {
        // Г4: « /» внутри значения в кавычках (с 1С-экранированием удвоением) не
        // обрывает извлечение /IBConnectionString — значение берётся до закрывающей
        // кавычки.
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""""server1"""";Ref=""""trade"""";Usr=""""iv an /x"""";"" /N Иван";
        Assert.True(RunningInfobaseMatcher.TryExtractValue(commandLine, "IBConnectionString", out var value));
        Assert.Contains("Usr=\"iv an /x\"", value);

        var parts = RunningInfobaseMatcher.ParseConnectionString(value);
        Assert.Equal("server1", parts["Srvr"]);
        Assert.Equal("trade", parts["Ref"]);
        Assert.Equal("iv an /x", parts["Usr"]);

        // Регресс: одиночные (не удвоенные) кавычки внутри значения — прежняя
        // эвристика сохранена, значение не «схлопывается» до Srvr=.
        var singleQuotes = @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""Srvr=""server1"";Ref=""trade"";""";
        Assert.True(RunningInfobaseMatcher.TryExtractValue(singleQuotes, "IBConnectionString", out var value2));
        var parts2 = RunningInfobaseMatcher.ParseConnectionString(value2);
        Assert.Equal("server1", parts2["Srvr"]);
        Assert.Equal("trade", parts2["Ref"]);
    }

    [Fact]
    public void MatchProcesses_DirectKeysStillTakePriority()
    {
        // Регресс: прямые ключи /F и /S имеют приоритет над /IBConnectionString и /IBName.
        var fileBase = NamedBase("f1", "Файловая");
        var serverBase = NamedServerBase("s1", "Серверная", "server1", "trade");
        var processes = new[]
        {
            Process(@"""1cv8c.exe"" ENTERPRISE /F ""C:\Bases\f1"""),
            Process(@"""1cv8c.exe"" ENTERPRISE /S server1\trade")
        };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { fileBase, serverBase }, processes);

        Assert.True(matched.TryGetValue("f1", out var list1));
        Assert.Single(list1!);
        Assert.True(matched.TryGetValue("s1", out var list2));
        Assert.Single(list2!);
    }

    [Fact]
    public void WebBase_StillNeverMatchesInMatchProcesses()
    {
        // Регресс: веб-базы по-прежнему не сопоставляются.
        var web = new Infobase
        {
            Id = "w1",
            Name = "Веб",
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://srv/base" }
        };
        var processes = new[] { Process(@"""1cv8c.exe"" ENTERPRISE /WS http://srv/base") };

        var matched = RunningInfobaseMatcher.MatchProcesses(new[] { web }, processes);

        Assert.Empty(matched);
    }
}
