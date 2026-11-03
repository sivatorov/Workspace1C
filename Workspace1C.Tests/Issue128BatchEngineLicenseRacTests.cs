using System;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты v1.0.1.28: движок массовой выгрузки ibcmd/Конфигуратор (issue #92),
/// per-call таймаут rac и фильтр повторов в резолвере СУБД (issue #79),
/// расширение каталогов лицензий и парсер *.lic (issue #83).
/// </summary>
public sealed class Issue128BatchEngineLicenseRacTests
{
    // ===================== Issue #92: движок выгрузки =====================

    [Theory]
    [InlineData(null, BatchDumpEngine.Ibcmd)]
    [InlineData("", BatchDumpEngine.Ibcmd)]
    [InlineData("Ibcmd", BatchDumpEngine.Ibcmd)]
    [InlineData("ibcmd", BatchDumpEngine.Ibcmd)]
    [InlineData("Designer", BatchDumpEngine.Designer)]
    [InlineData("designer ", BatchDumpEngine.Designer)]
    [InlineData("Мусор", BatchDumpEngine.Ibcmd)]
    public void ParseDumpEngine_KnownAndUnknownValues(string? value, BatchDumpEngine expected)
    {
        Assert.Equal(expected, BatchOperationsService.ParseDumpEngine(value));
    }

    [Fact]
    public void DumpEngineToString_RoundTrip()
    {
        Assert.Equal("Ibcmd", BatchOperationsService.DumpEngineToString(BatchDumpEngine.Ibcmd));
        Assert.Equal("Designer", BatchOperationsService.DumpEngineToString(BatchDumpEngine.Designer));
        Assert.Equal(BatchDumpEngine.Designer,
            BatchOperationsService.ParseDumpEngine(
                BatchOperationsService.DumpEngineToString(BatchDumpEngine.Designer)));
    }

    private static Infobase RunningFileBase(string name)
    {
        var ib = new Infobase
        {
            Name = name,
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
        };
        ib.IsEnterpriseRunning = true;
        return ib;
    }

    [Fact]
    public void ShouldSkipRunning_DesignerEngine_SkipsRunningEvenWithAllowRunning()
    {
        var vm = new BatchOperationsViewModel(new[] { RunningFileBase("База") }, BatchOperationKind.DumpDt)
        {
            AllowRunningBases = true,
            DumpEngine = BatchDumpEngine.Designer
        };
        // Конфигуратор требует монопольный доступ к файловой базе — запущенные пропускаются.
        Assert.True(vm.ShouldSkipRunning(vm.Items[0].Infobase));
    }

    [Fact]
    public void ShouldSkipRunning_IbcmdWithAllowRunning_DoesNotSkip()
    {
        var vm = new BatchOperationsViewModel(new[] { RunningFileBase("База") }, BatchOperationKind.DumpDt)
        {
            AllowRunningBases = true,
            DumpEngine = BatchDumpEngine.Ibcmd
        };
        Assert.False(vm.ShouldSkipRunning(vm.Items[0].Infobase));
    }

    [Fact]
    public void ShouldSkipRunning_LoadConfig_AlwaysSkips()
    {
        var vm = new BatchOperationsViewModel(new[] { RunningFileBase("База") }, BatchOperationKind.LoadConfig)
        {
            AllowRunningBases = true,
            DumpEngine = BatchDumpEngine.Ibcmd
        };
        Assert.True(vm.ShouldSkipRunning(vm.Items[0].Infobase));
    }

    [Fact]
    public void IsDbmsPasswordVisible_OnlyForIbcmdDumps()
    {
        var dumpIbcmd = new BatchOperationsViewModel(Array.Empty<Infobase>(), BatchOperationKind.DumpDt)
        {
            DumpEngine = BatchDumpEngine.Ibcmd
        };
        var dumpDesigner = new BatchOperationsViewModel(Array.Empty<Infobase>(), BatchOperationKind.DumpCf)
        {
            DumpEngine = BatchDumpEngine.Designer
        };
        var load = new BatchOperationsViewModel(Array.Empty<Infobase>(), BatchOperationKind.LoadConfig)
        {
            DumpEngine = BatchDumpEngine.Ibcmd
        };
        Assert.True(dumpIbcmd.IsDbmsPasswordVisible);
        Assert.False(dumpDesigner.IsDbmsPasswordVisible);
        Assert.False(load.IsDbmsPasswordVisible);
    }

    // ===================== Issue #79: повтор rac по типу ошибки =====================

    [Theory]
    [InlineData("Превышен таймаут ожидания ответа rac (90 с).", true)]
    [InlineData("rac: command timeout after 30 s", true)]
    [InlineData("Не удалось выполнить команду rac: процесс не запустился", true)]
    [InlineData("rac (host:1541) завершился с кодом 1. Недостаточно прав пользователя на информационную базу", false)]
    [InlineData("Insufficient rights to operate with infobase", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRetryableRacFailure_OnlyTimeoutsAndStartFailures(string? message, bool expected)
    {
        Assert.Equal(expected, ClusterInfobaseCredentialsResolver.IsRetryableRacFailure(message));
    }

    // ===================== Issue #83: каталоги лицензий и парсер *.lic =====================

    [Fact]
    public void LicenseDirectories_NoDuplicates_AtLeastProgramDataAndUserPaths()
    {
        var directories = RingLicenseService.LicenseDirectories();
        Assert.True(directories.Count >= 2, "ожидается минимум 2 каталога лицензий");
        Assert.Equal(directories.Count, directories.Distinct(StringComparer.OrdinalIgnoreCase).Count());
#if WINDOWS
        // Windows: общий каталог 1Cv8\licenses, %ProgramData%\1C\licenses (issue #83,
        // репорт 7OH «пустая таблица») и пер-пользовательские каталоги — все присутствуют.
        Assert.Contains(directories, d => d.EndsWith(@"1C\1cv8\licenses", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directories, d => d.EndsWith(@"1C\licenses", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directories, d => d.Contains(@"1C\1cv8\licenses", StringComparison.OrdinalIgnoreCase)
            && !d.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                StringComparison.OrdinalIgnoreCase));
#endif
    }

    [Fact]
    public void ParseLicFile_StoresFullContent_AndCollapsedDetails()
    {
        var content = string.Join("\n",
            "ААААББББВВВВГГГГДДДДЖЖЖЖ",
            "Наименование: 8.3.27: ООО «Ромашка»",
            "Регистрационный номер: 499459654270590-500000000037",
            "Пользователей: 5",
            "Срок действия: 01.01.2027",
            "Дата выдачи: 20260907085731");

        var license = RingLicenseParser.ParseLicFile(content, "20260907085731.lic");

        // Полное содержимое файла сохраняется многострочно (issue #83, v1.0.1.28).
        Assert.Equal(content, license.RawFullText);
        Assert.Contains("Срок действия: 01.01.2027", license.RawFullText);
        // Однострочная выжимка для тултипа — без переводов строк.
        Assert.DoesNotContain("\n", license.RawDetails);

        // Распознанные поля.
        Assert.Equal("ООО «Ромашка»", license.Owner);
        Assert.Equal("499459654270590-500000000037", license.RegNumber);
        Assert.Equal(5, license.LicenseCount);
        Assert.Equal(new DateTime(2027, 1, 1), license.ExpiryDate);
        // Компактная дата «20260907085731» (реальный файл репортёра) распознаётся;
        // v1.0.1.29 (issue #83): время в дате сохраняется (а не отбрасывается).
        Assert.Equal(new DateTime(2026, 9, 7, 8, 57, 31), license.IssueDate);
    }

    [Fact]
    public void BuildReadableDetails_IncludesFullFileContent()
    {
        var content = string.Join("\n", "строка один", "строка два", "строка три");
        var license = new ProgramLicense
        {
            SerialNumber = "12345678-1234-1234-1234-123456789012",
            Type = "клиентская",
            RawFullText = content
        };

        var details = RingLicenseParser.BuildReadableDetails(license);

        Assert.Contains("Тип лицензии: клиентская", details);
        Assert.Contains("строка один", details);
        Assert.Contains("строка два", details);
        Assert.Contains("строка три", details);
    }

    [Fact]
    public void BuildReadableDetails_EmptyContent_ExplicitNotReadMessage()
    {
        var license = new ProgramLicense
        {
            SerialNumber = "12345678-1234-1234-1234-123456789012",
            FilePath = @"C:\licenses\broken.lic"
        };

        var details = RingLicenseParser.BuildReadableDetails(license);

        Assert.Contains("Файл не прочитан или пуст", details);
        Assert.Contains("broken.lic", details);
    }

}
