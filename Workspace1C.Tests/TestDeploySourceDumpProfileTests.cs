using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты исправления issue #79 (v1.0.1.21): выгрузка ИСХОДНОЙ базы через ibcmd
/// больше не выполняется по параметрам СУБД новой (целевой) базы. Проверяются:
/// <list type="bullet">
/// <item>поля формы с целевым именем SQL-БД («источник_t») не попадают в профиль выгрузки;</item>
/// <item>валидация <see cref="TestDeployPlanner"/> проходит при наличии профиля источника,
/// даже если поля формы заполнены целевыми значениями;</item>
/// <item>полное отсутствие данных СУБД источника — понятная ошибка ДО старта операций.</item>
/// </list>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class TestDeploySourceDumpProfileTests
{
    private static Infobase ServerSource(string name = "Бухгалтерия") => new()
    {
        Id = "src-1",
        Name = name,
        PlatformVersion = "8.3.25.1000",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv1c",
            DatabaseName = name
        }
    };

    private static TestDeployScenario TargetScenario(string sourceName = "Бухгалтерия") => new()
    {
        SourceInfobaseId = "src-1",
        SourceInfobaseName = sourceName,
        TargetName = "Тест: " + sourceName,
        TargetMode = TestDeployTargetMode.Server,
        ServerAddress = "srv1c",
        DatabaseName = "Тест: " + sourceName,
        Dbms = "PostgreSQL",
        DbServer = "pg-target",
        DbName = sourceName + "_t",
        DbUser = "sa",
        DbPassword = "pwd",
        CreateSqlDatabase = true,
        DumpEngine = BackupEngine.Ibcmd
    };

    // ============ FormFieldsDescribeSource / BuildFormProfileIfSource ============

    [Fact]
    public void FormFieldsDescribeSource_TargetDbName_ReturnsFalse()
    {
        // Автоподстановка «Имя базы данных в СУБД» из имени новой базы («…_t»):
        // такие поля выгрузку источника обеспечить не могут (issue #79).
        var source = ServerSource();
        var scenario = TargetScenario();

        Assert.False(TestDeployPlanner.FormFieldsDescribeSource(scenario, source, savedProfile: null));
        Assert.Null(TestDeployService.BuildFormProfileIfSource(scenario, source, savedProfile: null));
    }

    [Fact]
    public void FormFieldsDescribeSource_MatchesRef_ReturnsTrue()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.DbName = "Бухгалтерия"; // совпадает с Ref исходной базы

        Assert.True(TestDeployPlanner.FormFieldsDescribeSource(scenario, source, savedProfile: null));
        var profile = TestDeployService.BuildFormProfileIfSource(scenario, source, savedProfile: null);
        Assert.NotNull(profile);
        Assert.Equal("Бухгалтерия", profile!.Database);
        Assert.Equal("pg-target", profile.Host);
    }

    [Fact]
    public void FormFieldsDescribeSource_MatchesSavedProfile_ReturnsTrue()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.DbName = "buh_db"; // совпадает с сохранённым профилем
        var saved = new DbmsBackupProfile { InfobaseId = "src-1", Dbms = DbmsKind.PostgreSql, Database = "buh_db" };

        Assert.True(TestDeployPlanner.FormFieldsDescribeSource(scenario, source, saved));
    }

    [Fact]
    public void FormFieldsDescribeSource_EmptyFields_ReturnsFalse()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.DbName = string.Empty;
        Assert.False(TestDeployPlanner.FormFieldsDescribeSource(scenario, source, savedProfile: null));
        scenario.DbName = "Бухгалтерия";
        scenario.DbServer = string.Empty;
        Assert.False(TestDeployPlanner.FormFieldsDescribeSource(scenario, source, savedProfile: null));
    }

    // ============ Валидация планировщика по источнику ============

    [Fact]
    public void Validate_WithSourceProfile_Passes_EvenWithTargetFormFields()
    {
        var source = ServerSource();
        var scenario = TargetScenario(); // поля формы — целевые («…_t»)
        scenario.AddToTestGroup = false;

        var errors = TestDeployPlanner.Validate(
            scenario, source, existingTarget: null,
            dbmsProfileResolver: _ => new DbmsBackupProfile
            {
                InfobaseId = "src-1",
                Dbms = DbmsKind.PostgreSql,
                Host = "pg-src",
                Database = "buh_db"
            });

        Assert.DoesNotContain("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_TargetFormFieldsOnly_ReportsNeedDbmsProfile()
    {
        var source = ServerSource();
        var scenario = TargetScenario(); // DbName = «Бухгалтерия_t», профиля нет
        scenario.AddToTestGroup = false;

        var errors = TestDeployPlanner.Validate(
            scenario, source, existingTarget: null, dbmsProfileResolver: _ => null);

        // Валидация «зелёной» не будет: целевые поля формы не считаются данными источника.
        Assert.Contains("TestDeploy.NeedDbmsProfile", errors);
    }

    // ============ ResolveSourceDumpProfileAsync (цепочка профиль → rac → форма) ============

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_TargetFieldsOnly_ReturnsError()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        var service = new TestDeployService();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log: null, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => null);

        Assert.Null(profile);
        // Понятная ошибка ДО шага «Выгрузка»: в тестовом окружении локализация не
        // загружена — возвращается ключ строки. v1.0.1.29 (issue #79): к ошибке
        // добавлена подсказка «задайте данные СУБД источника вручную ИЛИ движок
        // „Конфигуратор"».
        Assert.NotNull(error);
        Assert.StartsWith("TestDeploy.DbmsProfileNotFound", error!, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.DbmsResolveAdviseManual", error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_SavedProfileUsed_TargetFieldsIgnored()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        var service = new TestDeployService();
        var saved = new DbmsBackupProfile
        {
            InfobaseId = "src-1",
            Dbms = DbmsKind.PostgreSql,
            Host = "pg-src",
            Database = "buh_db",
            User = "postgres"
        };

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log: null, CancellationToken.None,
            savedProfileLoader: _ => saved,
            resolverFactory: () => null);

        Assert.Null(error);
        Assert.NotNull(profile);
        // Выгрузка идёт по данным ИСТОЧНИКА, а не целевой базы «…_t».
        Assert.Equal("buh_db", profile!.Database);
        Assert.Equal("pg-src", profile.Host);
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_FormFieldsMatchingRef_Used()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.DbName = "Бухгалтерия"; // поля заведомо описывают источник
        var service = new TestDeployService();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log: null, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => null);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal("Бухгалтерия", profile!.Database);
    }

    // ============ Журналирование резолва (issue #79, v1.0.1.22) ============
    // Репорт 7OH: «после ОК ничего не происходит, в логах тихо» — шаги pre-валидации
    // и резолва СУБД нигде не фиксировались. Теперь они видны в коллбэке журнала окна
    // и в result.Log.

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_StepsAreLogged()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        var service = new TestDeployService();
        var log = new List<string>();

        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log: log.Add, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => null);

        Assert.Null(profile);
        // Проверка «описывают ли поля формы источник» журналируется, даже когда
        // rac-резолвер недоступен.
        Assert.Contains("TestDeploy.SourceDbmsFormCheck", log);
        // v1.0.1.29 (issue #79): к ошибке добавлена подсказка о ручных данных СУБД
        // источника / движке «Конфигуратор» (см. ResolveSourceDumpProfileAsync_..._ReturnsError).
        Assert.NotNull(error);
        Assert.StartsWith("TestDeploy.DbmsProfileNotFound", error!, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.DbmsResolveAdviseManual", error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_NoSourceDbms_FailsBeforeAnyStepWithClearError()
    {
        // Репорт 7OH (issue #79): после ОК «НИЧЕГО не происходит» — на деле сценарий
        // прерывался pre-валидацией ДО шага «Выгрузка» с ранним возвратом и пустым
        // журналом. Проверяем: ошибка понятная («чего не хватает») и никаких шагов
        // (в т.ч. создания базы) не выполняется.
        var source = ServerSource();
        var scenario = TargetScenario();
        var service = new TestDeployService();

        var result = await service.DeployAsync(
            scenario, source, Array.Empty<Infobase>(), groups: null,
            log: null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("TestDeploy.NeedDbmsProfile", result.ErrorMessage);
        // Журнал и шаги пусты: до шага «Выгрузка» сценарий не дошёл, база не создавалась.
        Assert.Empty(result.Log);
        Assert.True(result.Steps is null || result.Steps.Count == 0);
        Assert.Null(result.Target);
    }
}
