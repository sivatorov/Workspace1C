using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты массовой выгрузки в DT/CF с шаблоном имени файла (issue #92):
/// чистая логика построения пути по шаблону ({Имя}/{Дата}), разбор вида
/// операции, пред-валидация DumpCf и цикл прогона ViewModel с подменённым
/// исполнителем (без запуска ibcmd и без процессов).
/// </summary>
public sealed class Issue92BatchDumpTests
{
    private static Infobase FileBase(string name) => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    // ===================== Шаблон имени файла =====================

    [Fact]
    public void BuildDumpFilePath_DefaultTemplate_NameAndDate()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, BatchOperationsService.DefaultDumpTemplate, stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30_17-45.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_CfKind_UsesCfExtension()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpCf, "{Имя}", stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа.cf"), path);
    }

    [Fact]
    public void BuildDumpFilePath_EnglishTokens_Work()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, "{Name}_{Date}", stamp);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30_17-45.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_EmptyTemplate_FallsBackToBaseName()
    {
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt, template: "   ", timestamp: null);

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TemplateResult_IsSanitized()
    {
        // Недопустимые символы, оставшиеся в шаблоне/имени, заменяются,
        // файл остаётся корректным именем.
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "База: 1/2", BatchOperationKind.DumpDt, "{Имя}", null);

        Assert.EndsWith(".dt", path, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(@"C:\dumps", "База_ 1_2.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_ExistingFile_GetsNumericSuffix()
    {
        var dir = Directory.CreateTempSubdirectory("ws1c_dump").FullName;
        try
        {
            var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
            File.WriteAllText(Path.Combine(dir, "Альфа_2026-10-30_17-45.dt"), "x");

            var path = BatchOperationsService.BuildDumpFilePath(
                dir, "Альфа", BatchOperationKind.DumpDt, BatchOperationsService.DefaultDumpTemplate, stamp);

            Assert.Equal(Path.Combine(dir, "Альфа_2026-10-30_17-45_2.dt"), path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BuildDumpFilePath_TimestampFormatParameter_ChangesDefaultStamp()
    {
        // Формат из настроек приложения («Формат даты в имени файла выгрузки»)
        // применяется к токену {Дата} без собственного формата (issue #92, 1.0.1.11).
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            BatchOperationsService.DefaultDumpTemplate, stamp,
            timestampFormat: "yyyyMMdd_HHmmss");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_20261030_174500.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TokenWithOwnFormat_OverridesDefault()
    {
        // Токен {Дата:формат} задаёт собственный формат (issue #92, 7OH:
        // «формат даты задать можно») и перекрывает формат из настроек.
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            "{Имя}_{Дата:yyyy-MM-dd}", stamp,
            timestampFormat: "yyyyMMdd_HHmmss");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_2026-10-30.dt"), path);
    }

    [Fact]
    public void BuildDumpFilePath_TokenWithInvalidFormat_FallsBackToDefault()
    {
        // Некорректный формат токена не ломает операцию: fallback на формат по умолчанию.
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var path = BatchOperationsService.BuildDumpFilePath(
            @"C:\dumps", "Альфа", BatchOperationKind.DumpDt,
            "{Имя}_{Дата:QQQ}", stamp,
            timestampFormat: "yyyyMMdd");

        Assert.Equal(Path.Combine(@"C:\dumps", "Альфа_20261030.dt"), path);
    }

    [Fact]
    public void ExpandDumpTemplate_MixedTokensWithSpacesAroundColon_Work()
    {
        var stamp = new DateTime(2026, 10, 30, 17, 45, 0);
        var expanded = BatchOperationsService.ExpandDumpTemplate(
            "{Имя} {Date : dd.MM.yyyy}", "Бета", stamp, "yyyyMMdd");

        Assert.Equal("Бета 30.10.2026", expanded);
    }

    // ===================== Вид операции DT/CF =====================

    [Fact]
    public void ParseDumpKind_String_MapsCorrectly()
    {
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind("DT"));
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(null));
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(""));
        Assert.Equal(BatchOperationKind.DumpCf, BatchOperationsService.ParseDumpKind("CF"));
        Assert.Equal(BatchOperationKind.DumpCf, BatchOperationsService.ParseDumpKind("cf"));
    }

    [Fact]
    public void DumpKindToString_RoundTrips()
    {
        Assert.Equal("DT", BatchOperationsService.DumpKindToString(BatchOperationKind.DumpDt));
        Assert.Equal("CF", BatchOperationsService.DumpKindToString(BatchOperationKind.DumpCf));

        Assert.Equal(BatchOperationKind.DumpCf,
            BatchOperationsService.ParseDumpKind(
                BatchOperationsService.DumpKindToString(BatchOperationKind.DumpCf)));
    }

    // ===================== Пред-валидация DumpCf =====================

    [Fact]
    public void Validate_DumpCf_WithoutDirectory_Fails()
    {
        var errors = BatchOperationsService.Validate(
            Array.Empty<Infobase>(), BatchOperationKind.DumpCf, path: null);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_DumpCf_WithValidDirectory_Passes()
    {
        var file = FileBase("Файловая");

        var errors = BatchOperationsService.Validate(
            new[] { file }, BatchOperationKind.DumpCf, path: Path.GetTempPath());

        Assert.Empty(errors);
    }

    // ===================== ViewModel: шаблон и переключатель DT/CF =====================

    [Fact]
    public void ViewModel_DefaultTemplate_IsFromService()
    {
        var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt);

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, vm.Template);
        Assert.True(vm.IsDumpKind);
    }

    [Fact]
    public void ViewModel_Kind_IsSettable_AndUpdatesPathLabel()
    {
        var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt);
        Assert.True(vm.IsDumpKind);

        vm.Kind = BatchOperationKind.DumpCf;
        Assert.True(vm.IsDumpKind);

        vm.Kind = BatchOperationKind.LoadConfig;
        Assert.False(vm.IsDumpKind);
    }

    [Fact]
    public async Task RunAsync_DumpCf_BuildsPerBaseCfPaths_ByTemplate()
    {
        var dir = Directory.CreateTempSubdirectory("ws1c_dumpcf").FullName;
        try
        {
            var a = FileBase("Альфа");
            var b = FileBase("Бета");
            var vm = new BatchOperationsViewModel(new[] { a, b }, BatchOperationKind.DumpCf)
            {
                PathText = dir,
                Template = "{Имя}",
                ExecutorOverride = (_, path, _) =>
                {
                    Assert.EndsWith(".cf", path, StringComparison.Ordinal);
                    return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
                }
            };

            await vm.RunAsync(CancellationToken.None);

            Assert.True(vm.Items.All(i => i.Status == BatchOperationsStatus.Success));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_BatchDumpDefaults_AreSane()
    {
        var settings = new AppSettings();

        Assert.Equal(BatchOperationsService.DefaultDumpTemplate, settings.BatchDumpTemplate);
        Assert.Equal(BatchOperationKind.DumpDt, BatchOperationsService.ParseDumpKind(settings.BatchDumpKind));
        Assert.Equal(string.Empty, settings.BatchDumpDirectory);
    }

    [Fact]
    public void ViewModel_TimestampFormat_IsPassedToFilePathBuilding()
    {
        // Формат из окна (настройка приложения) доходит до построителя пути (issue #92):
        // имя файла перед исполнителем построено с настроенным форматом {Дата}
        // «yyyyMMdd_HHmmss» (отметка времени берётся в момент прогона).
        var dir = Directory.CreateTempSubdirectory("ws1c_dumptf").FullName;
        try
        {
            var builtPaths = new List<string>();
            var vm = new BatchOperationsViewModel(new[] { FileBase("Альфа") }, BatchOperationKind.DumpDt)
            {
                PathText = dir,
                Template = "{Имя}_{Дата}",
                TimestampFormat = "yyyyMMdd_HHmmss",
                ExecutorOverride = (_, path, _) =>
                {
                    builtPaths.Add(path);
                    return Task.FromResult(new IbcmdResult { Success = true, ExitCode = 0 });
                }
            };

            vm.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

            var fileName = Path.GetFileName(Assert.Single(builtPaths));
            Assert.Matches(@"^Альфа_\d{8}_\d{6}\.dt$", fileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
