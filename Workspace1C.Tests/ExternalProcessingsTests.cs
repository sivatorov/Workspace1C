using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Загрузка внешних отчётов и обработок по списку баз (issue #91, 2.0): чистая логика
/// сервиса (файлы, параметры, разбор ответа COM) и ViewModel с подменённым загрузчиком.
/// Сама COM-операция в тестах не выполняется.
/// </summary>
public sealed class ExternalProcessingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_extproc_" + Guid.NewGuid().ToString("N"));

    public ExternalProcessingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* временный каталог */ }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    private static Infobase Base(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = "/bases/" + id }
    };

    [Theory]
    [InlineData("a.epf", true, false)]
    [InlineData("a.ERF", true, true)]
    [InlineData("a.cfe", false, false)]
    [InlineData("", false, false)]
    public void Extensions_Recognized(string path, bool supported, bool report)
    {
        Assert.Equal(supported, ExternalProcessingsService.IsSupported(path));
        if (supported)
            Assert.Equal(report, ExternalProcessingsService.IsReport(path));
    }

    [Fact]
    public void FindInFolder_OnlySupported_SortedByName()
    {
        Touch("b.erf");
        Touch("a.epf");
        Touch("c.txt");
        var files = ExternalProcessingsService.FindInFolder(_dir).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "a.epf", "b.erf" }, files);
        Assert.Empty(ExternalProcessingsService.FindInFolder(Path.Combine(_dir, "нет")));
    }

    [Fact]
    public void Validate_Cases()
    {
        Assert.NotNull(ExternalProcessingsService.Validate(Array.Empty<string>()));
        Assert.NotNull(ExternalProcessingsService.Validate(new[] { Touch("x.txt") }));
        Assert.NotNull(ExternalProcessingsService.Validate(new[] { Path.Combine(_dir, "нет.epf") }));
        Assert.Null(ExternalProcessingsService.Validate(new[] { Touch("ok.epf"), Touch("ok.erf") }));
    }

    [Fact]
    public void BuildParameters_MarksReports_FullPaths()
    {
        var epf = Touch("Обработка.epf");
        var erf = Touch("Отчёт.erf");
        var p = ExternalProcessingsService.BuildParameters(new[] { epf, erf }, update: false);
        Assert.Equal("load", p["op"]);
        Assert.Equal(false, p["update"]);
        var json = JsonSerializer.Serialize(p);
        using var doc = JsonDocument.Parse(json);
        var files = doc.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.False(files[0].GetProperty("report").GetBoolean());
        Assert.True(files[1].GetProperty("report").GetBoolean());
        Assert.Equal(Path.GetFullPath(erf), files[1].GetProperty("path").GetString());
    }

    [Fact]
    public void Script_FitsEncodedCommandLimit()
    {
        var encoded = ComScriptRunner.EncodeCommand(ComScriptRunner.BuildScript(ExternalProcessingsService.OperationBody));
        Assert.True(encoded.Length < ComScriptRunner.MaxEncodedCommandLength,
            $"Скрипт {encoded.Length} > {ComScriptRunner.MaxEncodedCommandLength}");
    }

    [Fact]
    public void ParseResult_FilesAndWarnings()
    {
        using var doc = JsonDocument.Parse("""
            {"files":[
              {"file":"C:\\x\\a.epf","action":"created","objectName":"ЗагрузкаПрайса","title":"Загрузка прайса","version":"1.2","warnings":["sections","dest:Документ.Заказ"]},
              {"file":"C:\\x\\b.epf","action":"","objectName":"","title":"","version":"","warnings":[],"error":"noinfo: Метод объекта не обнаружен"},
              {"file":"C:\\x\\c.erf","action":"skipped","objectName":"Отчёт","title":"Отчёт","version":"","warnings":[]}
            ]}
            """);
        var r = ExternalProcessingsService.ParseResult(doc.RootElement);
        Assert.True(r.Success);
        Assert.Equal(3, r.Files.Count);
        Assert.True(r.Files[0].Success);
        Assert.Equal("created", r.Files[0].Action);
        Assert.Equal("1.2", r.Files[0].Version);
        Assert.Equal(2, r.Files[0].Warnings.Count);
        Assert.False(r.Files[1].Success);
        Assert.Contains("Метод объекта не обнаружен", r.Files[1].Error);
        Assert.True(r.Files[2].Success);
    }

    [Fact]
    public void ParseResult_NoBsp()
    {
        using var doc = JsonDocument.Parse("""{"noBsp":true,"files":[]}""");
        var r = ExternalProcessingsService.ParseResult(doc.RootElement);
        Assert.False(r.Success);
        Assert.True(r.NoBsp);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void DescribeWarning_KnownCodes()
    {
        Assert.Contains("Документ.Заказ", ExternalProcessingsService.DescribeWarning("dest:Документ.Заказ"));
        Assert.Contains("ДополнительнаяОбработка", ExternalProcessingsService.DescribeWarning("kind:ДополнительнаяОбработка"));
        Assert.Equal("другое", ExternalProcessingsService.DescribeWarning("другое"));
    }

    [Fact]
    public void ViewModel_AddFiles_SkipsDuplicatesAndUnsupported()
    {
        var vm = new ExternalProcessingsViewModel(new[] { Base("1", "А") }, null);
        var a = Touch("a.epf");
        Assert.Equal(1, vm.AddFiles(new[] { a, a, Touch("x.txt") }));
        Assert.Single(vm.Files);
        Assert.Equal(0, vm.AddFolder(_dir)); // a.epf уже есть, x.txt не подходит
        Assert.Single(vm.Files);
    }

    [Fact]
    public async Task ViewModel_Load_CallsLoaderPerCheckedBase_AndCountsSuccess()
    {
        var b1 = Base("1", "Бухгалтерия");
        var b2 = Base("2", "ЗУП");
        var b3 = Base("3", "УТ");
        var vm = new ExternalProcessingsViewModel(new[] { b1, b2, b3 }, new[] { b1, b2 });
        vm.AddFiles(new[] { Touch("a.epf") });
        vm.UpdateExisting = false;

        var calls = 0;
        bool? updateSeen = null;
        Task<ExternalProcessingsBaseResult> Loader(Infobase ib, System.Collections.Generic.IReadOnlyList<string> files, bool update, CancellationToken ct)
        {
            calls++;
            updateSeen = update;
            var result = ib.Id == "1"
                ? new ExternalProcessingsBaseResult { Success = true, Files = { new ExternalProcessingFileResult { File = files[0], Action = "created", Title = "A" } } }
                : new ExternalProcessingsBaseResult { Success = true, Files = { new ExternalProcessingFileResult { File = files[0], Error = "ошибка" } } };
            return Task.FromResult(result);
        }

        var ok = await vm.LoadToCheckedAsync(Loader);

        Assert.Equal(2, calls);
        Assert.Equal(false, updateSeen);
        Assert.Equal(1, ok);
        Assert.False(vm.IsBusy);
        Assert.Contains("ошибка", vm.LogText);
    }

    [Fact]
    public async Task ViewModel_Load_WithoutFiles_DoesNothing()
    {
        var b1 = Base("1", "А");
        var vm = new ExternalProcessingsViewModel(new[] { b1 }, new[] { b1 });
        var calls = 0;
        var ok = await vm.LoadToCheckedAsync((_, _, _, _) => { calls++; return Task.FromResult(new ExternalProcessingsBaseResult()); });
        Assert.Equal(0, ok);
        Assert.Equal(0, calls);
    }
}
