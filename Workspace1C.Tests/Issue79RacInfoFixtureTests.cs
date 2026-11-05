using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Fixture-тесты issue #79 «Развернуть тестовую копию» (v1.0.1.34, racinfo.txt 7OH):
/// вывод «rac infobase info» утилиты rac 8.5.4.1878 приходит в UTF-16LE (посимвольно
/// с NUL-байтами), rac ЛОКАЛИЗУЕТ ключи полей, а профиль из rac всегда без пароля СУБД.
/// Проверяются: декодирование UTF-16LE (с BOM и без), санкция NUL/BOM в декодере,
/// разбор полей СУБД из вывода с английскими и русскими ключами (в кавычках и без,
/// mojibake-варианты), а также дополнение профиля логином/паролем СУБД из ручных
/// полей мастера («Данные СУБД источника»).
/// </summary>
public sealed class Issue79RacInfoFixtureTests
{
    static Issue79RacInfoFixtureTests()
    {
        // Кодовые страницы 866/1251 нужны для mojibake-вариантов русских ключей;
        // ConsoleOutputDecoder регистрирует провайдер в своём статическом конструкторе.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Реалистичный вывод «rac infobase info» (rac 8.5.4.1878, данные 7OH из
    /// racinfo.txt): ключи английские, разделитель « : », значения заполнены,
    /// кавычек нет — вывод rac сам по себе валиден.
    /// </summary>
    private const string RacInfoOutputEnglish = """
        infobase        : 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
        name            : Demo_8.3.27
        descr           : Демонстрационная база
        dbms            : MSSQLServer
        db-server       : localhost
        db-name         : Demo_8.3.27
        db-user         : sa
        locale          : ru_RU
        sessions-deny   : off
        scheduled-jobs-deny : off
        """;

    /// <summary>Тот же вывод с РУССКИМИ ключами (rac локализует вывод на машинах
    /// с русской локалью сообщений).</summary>
    private const string RacInfoOutputRussian = """
        информационная база : 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
        имя             : Demo_8.3.27
        описание        : Демонстрационная база
        субд            : MSSQLServer
        сервер бд       : localhost
        имя базы данных : Demo_8.3.27
        пользователь бд : sa
        локаль          : ru_RU
        блокировка начала сеансов : off
        блокировка регламентных заданий : off
        """;

    // ===================== Декодер: UTF-16LE fixture racinfo.txt =====================

    [Fact]
    public void Decode_Utf16LeWithBom_FixtureRacInfo_IsCleanText()
    {
        // Перенаправление «rac … > file» на Windows даёт файл UTF-16LE с BOM «FF FE».
        var payload = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(RacInfoOutputEnglish)).ToArray();

        var decoded = ConsoleOutputDecoder.Decode(payload);

        Assert.Equal(RacInfoOutputEnglish, decoded);
        Assert.DoesNotContain('\0', decoded);
        Assert.DoesNotContain('\uFEFF', decoded);
    }

    [Fact]
    public void Decode_Utf16LeWithoutBom_FixtureRacInfo_IsCleanText()
    {
        // БЕЗ BOM: у UTF-16LE каждый второй байт — NUL; однобайтовые кодировки
        // NUL не дают → распознаётся UTF-16LE.
        var bytes = Encoding.Unicode.GetBytes(RacInfoOutputEnglish);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(RacInfoOutputEnglish, decoded);
    }

    [Fact]
    public void Decode_Utf16Le_FixtureRacInfo_ToInfobaseInfo_ParsesDbmsFields()
    {
        // Главный сценарий issue #79: rac отдал валидные поля СУБД, но раньше
        // вывод «не дочитывался» до полей. После декодирования все поля СУБД
        // распознаются из fixture-вывода.
        var bytes = Encoding.Unicode.GetBytes(RacInfoOutputEnglish);

        var info = RacOutputParser.ToInfobaseInfo(ConsoleOutputDecoder.Decode(bytes));

        Assert.NotNull(info);
        Assert.Equal(Guid.Parse("425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e"), info!.InfobaseId);
        Assert.Equal("Demo_8.3.27", info.Name);
        Assert.Equal("MSSQLServer", info.Dbms);
        Assert.Equal("localhost", info.DbServer);
        Assert.Equal("Demo_8.3.27", info.DbName);
        Assert.Equal("sa", info.DbUser);
        Assert.Equal("ru_RU", info.Locale);
        Assert.False(info.SessionsDeny);
        Assert.False(info.ScheduledJobsDeny);
    }

    // ===================== Декодер: санкция NUL/BOM =====================

    [Fact]
    public void Decode_DecodedTextWithEmbeddedNulChar_IsSanitized()
    {
        // Остатки «чужой» декодировки UTF-16 как однобайтовой кодировки дают
        // NUL-символы прямо в строке — декодер вычищает их в единой точке
        // для ВСЕХ потребителей (rac, ibcmd, ring).
        var bytes = Encoding.Unicode.GetBytes("name : test")
            .Concat(new byte[] { 0x00, 0x00 }).ToArray();

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal("name : test", decoded);
        Assert.DoesNotContain('\0', decoded);
    }

    [Fact]
    public void Decode_DecodedTextWithBomCharInside_IsSanitized()
    {
        // U+FEFF, попавший в середину декодированного текста (BOM без файловой
        // обвязки), вычищается вместе с NUL-символами.
        var bytes = Encoding.Unicode.GetBytes("\uFEFFdbms : MSSQLServer");

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal("dbms : MSSQLServer", decoded);
        Assert.DoesNotContain('\uFEFF', decoded);
    }

    // ===================== Парсер: NUL-символы в тексте =====================

    [Fact]
    public void ToInfobaseInfo_TextWithNulCharacters_ParsesFields()
    {
        // Если текст с NUL-символами попал в парсер в обход декодера (двойное
        // декодирование, другой путь чтения) — ключи «db\0m\0s» всё равно
        // совпадают с «dbms»: NUL/FEFF вычищаются при разборе строк.
        const string nulText = "infobase\0  \0: 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e\n" +
                               "name\uFEFF : Demo_8.3.27\0\n" +
                               "db\0m\0s : MSSQLServer\n" +
                               "db-server : localhost\n" +
                               "db-name : Demo_8.3.27\n" +
                               "db-user : sa\0\n";

        var info = RacOutputParser.ToInfobaseInfo(nulText);

        Assert.NotNull(info);
        Assert.Equal("Demo_8.3.27", info!.Name);
        Assert.Equal("MSSQLServer", info.Dbms);
        Assert.Equal("localhost", info.DbServer);
        Assert.Equal("Demo_8.3.27", info.DbName);
        Assert.Equal("sa", info.DbUser);
    }

    [Fact]
    public void LooksLikeKeyValueOutput_TextWithNulCharacters_IsRecognized()
    {
        const string nulText = "db\0m\0s : MSSQLServer\nname : Demo\n";

        Assert.True(RacOutputParser.LooksLikeKeyValueOutput(nulText));
    }

    [Fact]
    public void ParseKeyValueBlocks_TextWithNulCharacters_ParsesBlock()
    {
        const string nulText = "cluster\0 : 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e\n" +
                               "n\0a\0me : Локальный кластер\n" +
                               "port : 1541\n";

        var blocks = RacOutputParser.ParseKeyValueBlocks(nulText, "cluster");

        Assert.Single(blocks);
        Assert.Equal("Локальный кластер", RacOutputParser.ToClusters(nulText).Single().Name);
    }

    // ===================== Парсер: русские ключи полей =====================

    [Fact]
    public void ToInfobaseInfo_RussianKeys_ParsesDbmsFields()
    {
        // rac локализует вывод (прецедент — русская справка «Использование:»):
        // вместо «dbms» приходит «субд», вместо «db-server» — «сервер бд» и т.д.
        var info = RacOutputParser.ToInfobaseInfo(RacInfoOutputRussian);

        Assert.NotNull(info);
        Assert.Equal(Guid.Parse("425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e"), info!.InfobaseId);
        Assert.Equal("Demo_8.3.27", info.Name);
        Assert.Equal("Демонстрационная база", info.Descr);
        Assert.Equal("MSSQLServer", info.Dbms);
        Assert.Equal("localhost", info.DbServer);
        Assert.Equal("Demo_8.3.27", info.DbName);
        Assert.Equal("sa", info.DbUser);
        Assert.Equal("ru_RU", info.Locale);
        Assert.False(info.SessionsDeny);
        Assert.False(info.ScheduledJobsDeny);
    }

    [Fact]
    public void ToInfobaseInfo_RussianKeys_QuotedValues_AreUnquoted()
    {
        // rac 8.5.4 заключает значения с пробелами в двойные кавычки — русские
        // алиасы работают и с кавычками.
        const string quoted = """
            информационная база : 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
            имя : "Demo 8.3.27"
            субд : "MSSQLServer"
            сервер бд : "sql01,1433"
            имя базы данных : "Demo 8.3.27"
            пользователь бд : "sa"
            """;

        var info = RacOutputParser.ToInfobaseInfo(quoted);

        Assert.NotNull(info);
        Assert.Equal("Demo 8.3.27", info!.Name);
        Assert.Equal("MSSQLServer", info.Dbms);
        Assert.Equal("sql01,1433", info.DbServer);
        Assert.Equal("Demo 8.3.27", info.DbName);
        Assert.Equal("sa", info.DbUser);
    }

    [Fact]
    public void ToInfobaseInfo_RussianKeysMojibake_Forms_AreRecognized()
    {
        // cp866-вывод, декодированный как cp1251, даёт mojibake-ключи («шь Їрщыр»):
        // для коротких известных ключей парсер пробует перезакодирование
        // cp866 → cp1251 по образцу FixCp1251ToCp866Mojibake.
        const string guid = "425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e";
        var mojiDbms = MakeMojibake("субд");
        var mojiName = MakeMojibake("имя");

        var output = $"infobase : {guid}\n" +
                     $"{mojiName} : Demo_8.3.27\n" +
                     $"{mojiDbms} : MSSQLServer\n";

        var info = RacOutputParser.ToInfobaseInfo(output);

        Assert.NotNull(info);
        Assert.Equal("Demo_8.3.27", info!.Name);
        Assert.Equal("MSSQLServer", info.Dbms);
    }

    [Fact]
    public void ToInfobaseInfo_EnglishKeyTakesPriorityOverRussianAlias()
    {
        // Английский ключ приоритетен: алиасы применяются ТОЛЬКО когда английский
        // ключ не найден/пуст — коллизии с англоязычными значениями исключены.
        const string output = """
            infobase : 425f2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
            name : EnglishName
            имя : РусскоеИмя
            dbms : MSSQLServer
            db-server : localhost
            db-name : Demo_8.3.27
            db-user : sa
            """;

        var info = RacOutputParser.ToInfobaseInfo(output);

        Assert.NotNull(info);
        Assert.Equal("EnglishName", info!.Name);
    }

    // ===================== Merge логина/пароля СУБД в rac-профиль =====================

    [Fact]
    public void ToProfile_FillsEmptyPasswordFromMasterFallback()
    {
        // rac пароль СУБД не возвращает никогда — профиль из «infobase info» всегда
        // с пустым паролем. Fallback из ручных полей мастера дополняет ПУСТЫЕ поля.
        var info = new RacInfobaseInfo
        {
            Dbms = "MSSQLServer",
            DbServer = "localhost",
            DbName = "Demo_8.3.27",
            DbUser = "sa"
        };

        var profile = ClusterInfobaseCredentialsResolver.ToProfile(
            info, "id-1", "Demo_8.3.27", dbmsPasswordFallback: "S3cret");

        Assert.NotNull(profile);
        Assert.Equal("sa", profile!.User);
        Assert.Equal("S3cret", profile.Password);
    }

    [Fact]
    public void ToProfile_FillsEmptyUserAndPasswordFromMasterFallback()
    {
        var info = new RacInfobaseInfo
        {
            Dbms = "PostgreSQL",
            DbServer = "pg-host port=5433",
            DbName = "buh_db"
        };

        var profile = ClusterInfobaseCredentialsResolver.ToProfile(
            info, "id-1", "Бухгалтерия",
            dbmsUserFallback: "postgres", dbmsPasswordFallback: "pg-pwd");

        Assert.NotNull(profile);
        Assert.Equal("postgres", profile!.User);
        Assert.Equal("pg-pwd", profile.Password);
        Assert.Equal(DbmsKind.PostgreSql, profile.Dbms);
    }

    [Fact]
    public void ToProfile_RacValuesTakePriority_OverMasterFallback()
    {
        // rac-значения приоритетны: непустой пользователь из rac НЕ перезаписывается.
        var info = new RacInfobaseInfo
        {
            Dbms = "MSSQLServer",
            DbServer = "localhost",
            DbName = "Demo_8.3.27",
            DbUser = "sa"
        };

        var profile = ClusterInfobaseCredentialsResolver.ToProfile(
            info, "id-1", "Demo_8.3.27",
            dbmsUserFallback: "other-user", dbmsPasswordFallback: "S3cret");

        Assert.NotNull(profile);
        Assert.Equal("sa", profile!.User);
        Assert.Equal("S3cret", profile.Password);
    }

    [Fact]
    public void ToProfileWithoutFallback_DoesNotTouchCredentials()
    {
        // Без fallback (массовые операции, где пароль подставляется окном отдельно)
        // профиль остаётся с пустым паролем — прежнее поведение.
        var info = new RacInfobaseInfo
        {
            Dbms = "MSSQLServer",
            DbServer = "localhost",
            DbName = "Demo_8.3.27",
            DbUser = "sa"
        };

        var profile = ClusterInfobaseCredentialsResolver.ToProfile(info, "id-1", "Demo_8.3.27");

        Assert.NotNull(profile);
        Assert.Equal(string.Empty, profile!.Password);
    }

    [Fact]
    public void MergeSourceDbmsCredentials_FillsEmptyFields_AndReportsChange()
    {
        var profile = new DbmsBackupProfile
        {
            InfobaseId = "id-1",
            Dbms = DbmsKind.MsSql,
            Host = "localhost",
            Database = "Demo_8.3.27",
            User = "sa"
        };
        var scenario = new TestDeployScenario { SourceDbUser = "sa", SourceDbPassword = "S3cret" };

        var changed = TestDeployService.MergeSourceDbmsCredentials(profile, scenario);

        Assert.True(changed);
        Assert.Equal("sa", profile.User);
        Assert.Equal("S3cret", profile.Password);
    }

    [Fact]
    public void MergeSourceDbmsCredentials_RacValuesArePriority()
    {
        // Непустые rac-значения не перезаписываются; если менять нечего — false.
        var profile = new DbmsBackupProfile
        {
            InfobaseId = "id-1",
            Dbms = DbmsKind.MsSql,
            Host = "localhost",
            Database = "Demo_8.3.27",
            User = "sa",
            Password = "from-rac"
        };
        var scenario = new TestDeployScenario { SourceDbUser = "other", SourceDbPassword = "S3cret" };

        var changed = TestDeployService.MergeSourceDbmsCredentials(profile, scenario);

        Assert.False(changed);
        Assert.Equal("sa", profile.User);
        Assert.Equal("from-rac", profile.Password);
    }

    [Fact]
    public void MergeSourceDbmsCredentials_EmptyScenarioFields_NoChange()
    {
        var profile = new DbmsBackupProfile
        {
            InfobaseId = "id-1",
            Dbms = DbmsKind.MsSql,
            Host = "localhost",
            Database = "Demo_8.3.27"
        };
        var scenario = new TestDeployScenario();

        var changed = TestDeployService.MergeSourceDbmsCredentials(profile, scenario);

        Assert.False(changed);
        Assert.Equal(string.Empty, profile.User);
        Assert.Equal(string.Empty, profile.Password);
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_RacProfile_GetsPasswordFromMasterFields()
    {
        // Сквозной сценарий issue #79-в: rac отдал сервер/имя БД/пользователя (sa),
        // но без пароля — мастер подставляет пароль из ручных полей, ibcmd не падает
        // на авторизации. В журнал пишется происхождение кред.
        var source = new Infobase
        {
            Id = "src-1",
            Name = "Demo_8.3.27",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "Demo_8.3.27"
            }
        };
        var scenario = new TestDeployScenario
        {
            SourceInfobaseId = "src-1",
            SourceDbUser = "sa",
            SourceDbPassword = "S3cret"
        };
        var rac = new RacSummaryFakeRacClient();
        var log = new List<string>();
        var service = new TestDeployService();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log.Add, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => new ClusterInfobaseCredentialsResolver(rac, servers: null, log: null));

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.MsSql, profile!.Dbms);
        Assert.Equal("localhost", profile.Host);
        Assert.Equal("Demo_8.3.27", profile.Database);
        Assert.Equal("sa", profile.User);
        // Пароль СУБД источника взят из полей мастера (rac его не возвращает).
        Assert.Equal("S3cret", profile.Password);
        Assert.Contains("TestDeploy.SourceDbmsCredentialsFromMaster", log);
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_RacProfile_WithoutMasterPassword_KeepsEmpty()
    {
        // Поля мастера пусты — профиль остаётся без пароля (прежнее поведение).
        var source = new Infobase
        {
            Id = "src-1",
            Name = "Demo_8.3.27",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "Demo_8.3.27"
            }
        };
        var scenario = new TestDeployScenario { SourceInfobaseId = "src-1" };
        var rac = new RacSummaryFakeRacClient();
        var log = new List<string>();
        var service = new TestDeployService();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log.Add, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => new ClusterInfobaseCredentialsResolver(rac, servers: null, log: null));

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal("sa", profile!.User);
        Assert.Equal(string.Empty, profile.Password);
        Assert.DoesNotContain("TestDeploy.SourceDbmsCredentialsFromMaster", log);
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_RacProfile_GetsPasswordFromTargetDbFields()
    {
        // Issue #79 (v1.0.1.35, ZIP diagnostics_20261105_082423: профиль из rac ушёл
        // в ibcmd без пароля): ручной блок «Данные СУБД источника» не заполнен —
        // fallback логина/пароля берётся из ВИДИМЫХ полей формы «Пользователь СУБД»/
        // «Пароль СУБД» (у одного SQL-сервера креды совпадают; rac пароль не возвращает
        // никогда). Унификация с окном массовой выгрузки issue #92, где хватает
        // одного пароля с формы.
        var source = new Infobase
        {
            Id = "src-1",
            Name = "Demo_8.3.27",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "Demo_8.3.27"
            }
        };
        var scenario = new TestDeployScenario
        {
            SourceInfobaseId = "src-1",
            DbUser = "sa",
            DbPassword = "FormPwd"
        };
        var rac = new RacSummaryFakeRacClient();
        var log = new List<string>();
        var service = new TestDeployService();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log.Add, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => new ClusterInfobaseCredentialsResolver(rac, servers: null, log: null));

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.MsSql, profile!.Dbms);
        Assert.Equal("localhost", profile.Host);
        Assert.Equal("Demo_8.3.27", profile.Database);
        Assert.Equal("sa", profile.User);
        // Пароль СУБД взят из видимых полей формы (rac его не возвращает).
        Assert.Equal("FormPwd", profile.Password);
        Assert.Contains("TestDeploy.SourceDbmsCredentialsFromMaster", log);
    }

    [Fact]
    public void ResolveDbmsCredentialFallback_SourceFieldsHavePriority()
    {
        // Ручные поля «Данные СУБД источника» приоритетнее целевых полей формы.
        var scenario = new TestDeployScenario
        {
            SourceDbUser = "src-user",
            SourceDbPassword = "src-pwd",
            DbUser = "target-user",
            DbPassword = "target-pwd"
        };

        var (user, password) = TestDeployService.ResolveDbmsCredentialFallback(scenario);

        Assert.Equal("src-user", user);
        Assert.Equal("src-pwd", password);
    }

    [Fact]
    public void ResolveDbmsCredentialFallback_EmptyScenario_ReturnsNulls()
    {
        var (user, password) = TestDeployService.ResolveDbmsCredentialFallback(new TestDeployScenario());

        Assert.Null(user);
        Assert.Null(password);
        var (nullUser, nullPassword) = TestDeployService.ResolveDbmsCredentialFallback(null);
        Assert.Null(nullUser);
        Assert.Null(nullPassword);
    }

    /// <summary>
    /// Фейковый rac-клиент: отдаёт кластер и summary базы с заполненными полями СУБД
    /// (dbms/db-server/db-name/db-user), пароль rac не возвращает — как реальный rac.
    /// </summary>
    private sealed class RacSummaryFakeRacClient : IRacClient
    {
        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(
                new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } });

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(
                new[]
                {
                    new RacInfobaseSummary
                    {
                        InfobaseId = Guid.NewGuid(),
                        Name = "Demo_8.3.27",
                        Dbms = "MSSQLServer",
                        DbServer = "localhost",
                        DbName = "Demo_8.3.27",
                        DbUser = "sa"
                    }
                });

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams p, Guid c, Guid i, string? u, string? pw,
            CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<RacInfobaseInfo?>(null);

        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> UpdateClusterAsync(RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> SetJobStateAsync(RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public string LastActionError => string.Empty;
    }

    /// <summary>Mojibake-форма ключа: cp866-байты, декодированные как cp1251.</summary>
    private static string MakeMojibake(string alias) =>
        Encoding.GetEncoding(1251).GetString(Encoding.GetEncoding(866).GetBytes(alias));
}
