using System.IO.Compression;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сборки диагностики в ZIP (<see cref="DiagnosticsBundleService"/>):
/// состав архива (логи, systeminfo, trace.json, cm_modal_trace), счётчик файлов,
/// предупреждения о недостающих источниках и защита от лишних файлов.
/// </summary>
public sealed class DiagnosticsBundleServiceTests : IDisposable
{
    private readonly string _root;

    public DiagnosticsBundleServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm_diag_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* временный каталог — не критично */ }
    }

    private string MakeDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task CreateBundle_CollectsLogsAndSystemInfo()
    {
        var logs = MakeDir("logs");
        var temp = MakeDir("temp");
        var data = MakeDir("data");
        var output = MakeDir("out");
        File.WriteAllText(Path.Combine(logs, "app-20261101.log"), "log line");
        File.WriteAllText(Path.Combine(logs, "app-20261031.log"), "old log line");
        File.WriteAllText(Path.Combine(temp, "cm_modal_trace.log"), "trace");
        File.WriteAllText(Path.Combine(data, "trace.json"), "{}");

        var result = await DiagnosticsBundleService.CreateBundleAsync(
            outputDirectory: output, logDirectory: logs, tempDirectory: temp,
            appDataDirectory: data, timestamp: new DateTime(2026, 11, 1, 12, 0, 0));

        Assert.True(File.Exists(result.ZipPath));
        Assert.Equal("diagnostics_20261101_120000.zip", Path.GetFileName(result.ZipPath));
        Assert.True(result.FileCount >= 3);
        // systeminfo.txt пишется всегда — предупреждений о нём быть не может.
        using var archive = ZipFile.OpenRead(result.ZipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("systeminfo.txt", names);
        Assert.Contains("logs/app-20261101.log", names);
        Assert.Contains("logs/app-20261031.log", names);
        Assert.Contains("cm_modal_trace.log", names);
        Assert.Contains("trace.json", names);
        var sysInfo = archive.Entries.First(e => e.FullName == "systeminfo.txt");
        using var reader = new StreamReader(sysInfo.Open());
        var text = reader.ReadToEnd();
        Assert.Contains("Workspace 1C diagnostics bundle", text);
        Assert.Contains("App version:", text);
    }

    [Fact]
    public void CreateBundle_MissingSources_CreatesArchiveWithWarnings()
    {
        var output = MakeDir("out_empty");
        var empty = MakeDir("empty");

        var result = DiagnosticsBundleService.CreateBundle(
            outputDirectory: output, logDirectory: empty, tempDirectory: empty,
            appDataDirectory: empty, timestamp: new DateTime(2026, 11, 1, 12, 0, 0));

        Assert.True(File.Exists(result.ZipPath));
        // Минимум systeminfo.txt попадает в архив всегда.
        Assert.Equal(1, result.FileCount);
        Assert.Contains(result.Warnings, w => w.Contains("cm_modal_trace.log"));
        Assert.Contains(result.Warnings, w => w.Contains("trace.json"));
        using var archive = ZipFile.OpenRead(result.ZipPath);
        Assert.Single(archive.Entries);
        Assert.Equal("systeminfo.txt", archive.Entries[0].FullName);
    }

    [Fact]
    public void CreateBundle_SkipsOversizedFiles_WithWarning()
    {
        var logs = MakeDir("logs_big");
        var output = MakeDir("out_big");
        var bigPath = Path.Combine(logs, "app-20261101.log");

        // Файл больше MaxEntryBytes — запись пропускается, предупреждение фиксируется.
        // Пишем разрежённый файл быстро: усечение до размера больше лимита.
        using (var stream = new FileStream(bigPath, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(DiagnosticsBundleService.MaxEntryBytes + 1);
        }

        var result = DiagnosticsBundleService.CreateBundle(
            outputDirectory: output, logDirectory: logs,
            tempDirectory: MakeDir("t2"), appDataDirectory: MakeDir("d2"),
            timestamp: new DateTime(2026, 11, 1, 12, 0, 0));

        Assert.Contains(result.Warnings, w => w.Contains("too large"));
        using var archive = ZipFile.OpenRead(result.ZipPath);
        Assert.DoesNotContain(archive.Entries, e => e.FullName == "logs/app-20261101.log");
    }
}
