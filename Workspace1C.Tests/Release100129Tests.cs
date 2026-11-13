using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Tests.TestSupport;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты релиза v1.0.1.29:
/// <list type="bullet">
/// <item>issue #79 — ручные данные СУБД источника в мастере «Развернуть тестовую копию»:
/// приоритет override в <see cref="TestDeployService.ResolveSourceDumpProfileAsync"/>,
/// pre-валидация без rac-проб, JSON-круговорот новых полей <see cref="TestDeployScenario"/>,
/// автозаполнение ручных полей в <see cref="TestDeployViewModel"/>;</item>
/// <item>issue #83 — лицензии: время в датах (dd.MM.yyyy HH:mm:ss, без AM/PM),
/// «Количество пользователей» в реальном формате файла, чистка окна деталей
/// (<see cref="RingLicenseParser.StripFileNoise"/>, BuildReadableDetails без ключа/шапки);</item>
/// <item>issue #92 — массовая выгрузка: гарантированный сброс состояния окна после
/// исключения исполнителя и при отмене (сводка/итог в журнале), снятие галочки базы
/// после успешной выгрузки.</item>
/// </list>
/// </summary>
public sealed class Release100129Tests
{
    // ============================ issue #79 ============================

    private static Infobase ServerSource(string name = "Бухгалтерия") => new()
    {
        Id = "src-129",
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
        SourceInfobaseId = "src-129",
        SourceInfobaseName = sourceName,
        TargetName = "Тест: " + sourceName,
        TargetMode = TestDeployTargetMode.Server,
        ServerAddress = "srv1c",
        DatabaseName = "Тест: " + sourceName,
        Dbms = "PostgreSQL",
        DbServer = "pg-target",
        DbName = sourceName + "_t",
        DumpEngine = BackupEngine.Ibcmd
    };

    [Fact]
    public void HasSourceDbmsOverride_RequiresCheckboxAndFilledFields()
    {
        var scenario = TargetScenario();
        scenario.UseSourceDbmsOverride = false;
        scenario.SourceDbServer = "pg-src";
        scenario.SourceDbName = "buh_db";
        Assert.False(TestDeployService.HasSourceDbmsOverride(scenario));

        scenario.UseSourceDbmsOverride = true;
        Assert.True(TestDeployService.HasSourceDbmsOverride(scenario));

        scenario.SourceDbServer = " ";
        Assert.False(TestDeployService.HasSourceDbmsOverride(scenario));
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_ManualOverride_Wins_WithoutFormHeuristicsOrRac()
    {
        // Сценарий 7OH: rac отдал пустые свойства (резолвер вернул бы null), поля формы
        // описывают ЦЕЛЕВУЮ базу (эвристика дала бы null) — ручной override должен
        // построить профиль напрямую.
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.UseSourceDbmsOverride = true;
        scenario.SourceDbms = "PostgreSQL";
        scenario.SourceDbServer = "pg-src";
        scenario.SourceDbName = "buh_db";
        scenario.SourceDbUser = "postgres";
        scenario.SourceDbPassword = "secret";

        var service = new TestDeployService();
        var log = new System.Collections.Generic.List<string>();
        // Резолвер/загрузчик профиля СУДЬБЫ: если они ВЫЗЫВАЮТСЯ — приоритет нарушен.
        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log.Add, CancellationToken.None,
            savedProfileLoader: _ => null,
            resolverFactory: () => throw new InvalidOperationException("rac не должен вызываться при override"));

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal("buh_db", profile!.Database);
        Assert.Equal("pg-src", profile.Host);
        Assert.Equal("postgres", profile.User);
        Assert.Equal(DbmsKind.PostgreSql, profile.Dbms);
        // Override журналируется (локаль тестового окружения может быть любой —
        // сверяемся по локализованному тексту).
        Assert.Contains(log, l => !string.IsNullOrWhiteSpace(l));
    }

    [Fact]
    public async Task ResolveSourceDumpProfileAsync_SavedProfile_StillWinsOverManualOverride()
    {
        var source = ServerSource();
        var scenario = TargetScenario();
        scenario.UseSourceDbmsOverride = true;
        scenario.SourceDbServer = "pg-manual";
        scenario.SourceDbName = "manual_db";

        var service = new TestDeployService();
        var (profile, error) = await service.ResolveSourceDumpProfileAsync(
            source, scenario, log: null, CancellationToken.None,
            savedProfileLoader: _ => new DbmsBackupProfile
            {
                InfobaseId = "src-129",
                Dbms = DbmsKind.MsSql,
                Host = "sql-src",
                Database = "buh_saved"
            });

        Assert.Null(error);
        Assert.Equal("buh_saved", profile!.Database);
    }

    [Fact]
    public void Validate_WithManualOverride_PassesWithoutRacProbe()
    {
        var source = ServerSource();
        var scenario = TargetScenario(); // поля формы — целевые («…_t»), профиля нет
        scenario.UseSourceDbmsOverride = true;
        scenario.SourceDbServer = "pg-src";
        scenario.SourceDbName = "buh_db";

        var errors = TestDeployPlanner.Validate(
            scenario, source, existingTarget: null, dbmsProfileResolver: _ => null);

        Assert.DoesNotContain(TestDeployPlanner.NeedDbmsProfileErrorKey, errors);
    }

    [Fact]
    public void TestDeployScenario_NewOverrideFields_JsonRoundTrip()
    {
        var scenario = TargetScenario();
        scenario.UseSourceDbmsOverride = true;
        scenario.SourceDbms = "PostgreSQL";
        scenario.SourceDbServer = "pg-src";
        scenario.SourceDbName = "buh_db";
        scenario.SourceDbUser = "postgres";
        scenario.SourceDbPassword = "secret";

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(scenario, options);
        var restored = JsonSerializer.Deserialize<TestDeployScenario>(json, options);

        Assert.NotNull(restored);
        Assert.True(restored!.UseSourceDbmsOverride);
        Assert.Equal("PostgreSQL", restored.SourceDbms);
        Assert.Equal("pg-src", restored.SourceDbServer);
        Assert.Equal("buh_db", restored.SourceDbName);
        Assert.Equal("postgres", restored.SourceDbUser);
        Assert.Equal("secret", restored.SourceDbPassword);
    }

    [Fact]
    public void TestDeployViewModel_BuildScenario_MapsManualOverrideFields()
    {
        var source = ServerSource();
        var vm = new TestDeployViewModel(
            source,
            new[] { source },
            Array.Empty<Group>(),
            proposedTargetName: "Тест: Бухгалтерия",
            proposedServerAddress: "srv1c",
            proposedDatabaseName: "Бухгалтерия_t",
            proposedFilePath: string.Empty);
        vm.UseSourceDbmsOverride = true;
        vm.SourceDbms = "PostgreSQL";
        vm.SourceDbServer = "pg-src";
        vm.SourceDbName = "buh_db";
        vm.SourceDbUser = "postgres";
        vm.SourceDbPassword = "secret";

        var scenario = vm.BuildScenario();

        Assert.True(scenario.UseSourceDbmsOverride);
        Assert.Equal("PostgreSQL", scenario.SourceDbms);
        Assert.Equal("pg-src", scenario.SourceDbServer);
        Assert.Equal("buh_db", scenario.SourceDbName);
        Assert.Equal("postgres", scenario.SourceDbUser);
        Assert.Equal("secret", scenario.SourceDbPassword);
    }

    [Fact]
    public void TestDeployViewModel_FillSourceDbmsOverrideDefaults_UsesFormAndSourceRef()
    {
        var source = ServerSource();
        var vm = new TestDeployViewModel(
            source,
            new[] { source },
            Array.Empty<Group>(),
            proposedTargetName: "Тест: Бухгалтерия",
            proposedServerAddress: "srv1c",
            proposedDatabaseName: "Бухгалтерия_t",
            proposedFilePath: string.Empty);
        vm.Dbms = "PostgreSQL";
        vm.DbServer = "pg-target";
        vm.DbUser = "sa";
        vm.SourceDbName = ""; // имя БД должно взяться из Ref источника

        vm.FillSourceDbmsOverrideDefaults();

        Assert.Equal("PostgreSQL", vm.SourceDbms);
        Assert.Equal("pg-target", vm.SourceDbServer);
        Assert.Equal("sa", vm.SourceDbUser);
        Assert.Equal("Бухгалтерия", vm.SourceDbName);
    }

    // ============================ issue #83 ============================

    /// <summary>Фрагмент реального файла *.lic (формат 7OH), замаскированный: длинный
    /// ключ в начале, «шапка», поля со временем в дате и «Количество пользователей».</summary>
    private const string RealLicSample =
        "4994596542705901234567890ABCDEF012345678\n" +
        "{\n" +
        "\t{\n" +
        "\t\t{\n" +
        "\t\t\tРег.Номер: 0000000000\n" +
        "\t\t\tСерия: S\n" +
        "\t\t\tНаименование: 8.3: ООО «Ромашка»\n" +
        "\t\t\tТип лицензии: ПРОФ\n" +
        "\t\t\tКоличество пользователей: 5\n" +
        "\t\t\tДата выдачи: 07.09.2026 08:57:31\n" +
        "\t\t\tСрок действия: 30.09.2026 23:59:59\n" +
        "\t\t}\n" +
        "\t}\n" +
        "}";

    [Fact]
    public void ParseLicFile_ExpiryDate_KeepsTime()
    {
        var license = RingLicenseParser.ParseLicFile(RealLicSample, "20260907085731.lic");

        Assert.NotNull(license.ExpiryDate);
        Assert.Equal(23, license.ExpiryDate!.Value.Hour);
        Assert.Equal(59, license.ExpiryDate!.Value.Minute);
        Assert.Equal(59, license.ExpiryDate!.Value.Second);
        // Формат отображения: dd.MM.yyyy HH:mm:ss, без AM/PM.
        Assert.Equal("30.09.2026 23:59:59", license.ExpiryDateText);
    }

    [Fact]
    public void ParseLicFile_ExpiryDateWithoutTime_FormatsDateOnly()
    {
        var license = RingLicenseParser.ParseLicFile(
            "Срок действия: 30.09.2026\n", "sample.lic");

        Assert.NotNull(license.ExpiryDate);
        Assert.Equal("30.09.2026", license.ExpiryDateText);
    }

    [Fact]
    public void ParseLicFile_UserCount_RealFileFormat()
    {
        // Колонка «Пользователей» должна заполняться из «Количество пользователей».
        var license = RingLicenseParser.ParseLicFile(RealLicSample, "sample.lic");
        Assert.Equal(5, license.LicenseCount);
    }

    [Fact]
    public void FindValue_FindsKeyAfterLeadingKeyOnSameLine()
    {
        // Ключ стоит НЕ в начале строки — после длинного ключа файла (issue #83).
        const string content = "4994596542705901234567890ABCDEF012345678 Количество пользователей: 10\n";
        Assert.Equal("10", RingLicenseParser.FindValue(content, "количество пользователей"));
    }

    [Fact]
    public void StripFileNoise_RemovesLeadingKeyAndHeader()
    {
        var stripped = RingLicenseParser.StripFileNoise(RealLicSample);

        Assert.DoesNotContain("4994596542705901234567890", stripped);
        Assert.DoesNotContain("{", stripped);
        Assert.Contains("Рег.Номер: 0000000000", stripped);
        Assert.Contains("Количество пользователей: 5", stripped);
    }

    [Fact]
    public void BuildReadableDetails_RealSample_NoKeyNoHeader_KeepsFileLine()
    {
        var license = RingLicenseParser.ParseLicFile(RealLicSample, "20260907085731.lic");
        var details = RingLicenseParser.BuildReadableDetails(license);

        // Длинный ключ и «шапка» файла отсутствуют (issue #83).
        Assert.DoesNotContain("4994596542705901234567890", details);
        Assert.DoesNotContain("{", details);
        // Issue #83 (v1.0.1.30): строка «Файл лицензии» — ПЕРВАЯ строка деталей,
        // распознанные поля перед ней не добавляются (похожие строки внутри
        // оригинального содержимого файла остаются — это формат файла).
        Assert.StartsWith("Файл лицензии: ", details, StringComparison.Ordinal);
        // Содержимое — в формате файла.
        Assert.Contains("Рег.Номер: 0000000000", details);
    }

    // ============================ issue #92 ============================

    private static Infobase FileBase(string name) => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    [Fact]
    public async Task RunAsync_ExecutorThrows_WindowStateRestored()
    {
        // Issue #92 (репорт 7OH «заново выгрузить уже невозможно»): исключение исполнителя
        // конвертируется в ошибку базы, окно НЕ остаётся заблокированным — IsRunning
        // сбрасывается, сводка/итог в журнале, повторный запуск возможен без
        // переоткрытия окна.
        var a = FileBase("Альфа");
        var vm = new BatchOperationsViewModel(new[] { a }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}"
        };
        vm.Items[0].IsChecked = true;
        vm.ExecutorOverride = (_, _, _) => throw new InvalidOperationException("boom");

        await vm.RunAsync();

        Assert.False(vm.IsRunning);
        Assert.All(vm.Items, i => Assert.False(i.IsBusy));
        Assert.Equal(BatchOperationsStatus.Failed, vm.Items[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(vm.Summary));

        // Повторный запуск (уже с успешным исполнителем) проходит без переоткрытия окна.
        vm.Items[0].IsChecked = true;
        vm.ExecutorOverride = (_, _, _) => Task.FromResult(new IbcmdResult { Success = true });
        await vm.RunAsync();
        Assert.Equal(BatchOperationsStatus.Success, vm.Items[0].Status);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunAsync_Cancelled_SetsSummaryAndLog()
    {
        // Issue #92 (репорт 7OH «остановить не работает»): при отмене сводка и итог
        // в журнале выставляются, IsRunning сбрасывается.
        var a = FileBase("Альфа");
        var vm = new BatchOperationsViewModel(new[] { a }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}"
        };
        vm.Items[0].IsChecked = true;
        vm.ExecutorOverride = (_, _, ct) => throw new OperationCanceledException(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => vm.RunAsync(new CancellationToken(canceled: true)));

        Assert.False(vm.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(vm.Summary));
        var log = AsyncLogAssert.WaitForNonEmpty(
            vm.LogLines, "Журнал отменённого прогона должен содержать строку");
        Assert.Contains(log, l => !string.IsNullOrWhiteSpace(l));
    }

    [Fact]
    public async Task RunAsync_Success_UnchecksBase()
    {
        // Issue #92 (пожелание 7OH): галочка успешной базы снимается.
        var a = FileBase("Альфа");
        var vm = new BatchOperationsViewModel(new[] { a }, BatchOperationKind.DumpDt)
        {
            PathText = Path.GetTempPath(),
            Template = "{Имя}"
        };
        vm.Items[0].IsChecked = true;
        vm.ExecutorOverride = (_, _, _) => Task.FromResult(new IbcmdResult { Success = true });

        await vm.RunAsync();

        Assert.True(vm.Items[0].Status == BatchOperationsStatus.Success);
        Assert.False(vm.Items[0].IsChecked);
    }
}
