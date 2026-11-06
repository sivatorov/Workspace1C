using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Экспорт диагностики в ZIP (v100120, новый функционал п.1): по запросу поддержки
/// пользователь одной кнопкой собирает журналы приложения и сведения об окружении
/// в один архив: <c>logs/app-YYYYMMDD.log</c> (14 дней ротации), <c>trace.json</c>
/// (диагностические флаги), <c>cm_modal_trace.log</c> (трассировка модальных окон,
/// issue #94) и сгенерированный <c>systeminfo.txt</c> (версия приложения, ОС, .NET,
/// культура, аптайм). Паролей и строк подключения в архив не попадает: перечисленные
/// файлы их не содержат, а настройки (settings.json, infobases.json) намеренно
/// не включаются. Чистый .NET — обе платформы (WPF и Avalonia); аргументы каталогов
/// задаются для тестируемости.
/// </summary>
public static class DiagnosticsBundleService
{
    /// <summary>Защита от гигантских файлов: отдельная запись архива больше этого
    /// размера пропускается с предупреждением (обычно логи ограничены 5 МБ).</summary>
    public const long MaxEntryBytes = 20 * 1024 * 1024;

    /// <summary>Итог сборки: путь к архиву, число файлов и предупреждения
    /// (что не удалось включить — поддержка увидит, чего не хватает).</summary>
    public sealed record BundleResult(string ZipPath, int FileCount, IReadOnlyList<string> Warnings);

    /// <summary>Каталог по умолчанию для архивов диагностики (внутри каталога данных).</summary>
    public static string DefaultOutputDirectory => Path.Combine(PlatformPaths.AppDataDirectory, "diagnostics");

    /// <summary>
    /// Собирает архив диагностики. Не выбрасывает исключений сбоя отдельных источников:
    /// недоступный файл уходит в предупреждения, архив создаётся из того, что удалось.
    /// </summary>
    /// <param name="outputDirectory">Каталог для архива (null — по умолчанию).</param>
    /// <param name="logDirectory">Каталог журналов приложения (null — <see cref="PlatformPaths.LogDirectory"/>).</param>
    /// <param name="tempDirectory">Каталог временных файлов (null — <see cref="Path.GetTempPath"/>).</param>
    /// <param name="appDataDirectory">Каталог данных (null — <see cref="PlatformPaths.AppDataDirectory"/>).</param>
    /// <param name="timestamp">Метка времени имени архива (null — текущее время).</param>
    public static Task<BundleResult> CreateBundleAsync(
        string? outputDirectory = null,
        string? logDirectory = null,
        string? tempDirectory = null,
        string? appDataDirectory = null,
        DateTime? timestamp = null,
        IReadOnlyList<string>? windowJournalFiles = null)
        => Task.Run(() => CreateBundle(
            outputDirectory, logDirectory, tempDirectory, appDataDirectory, timestamp, windowJournalFiles));

    /// <summary>Синхронное тело сборки (см. <see cref="CreateBundleAsync"/>).</summary>
    public static BundleResult CreateBundle(
        string? outputDirectory = null,
        string? logDirectory = null,
        string? tempDirectory = null,
        string? appDataDirectory = null,
        DateTime? timestamp = null,
        IReadOnlyList<string>? windowJournalFiles = null)
    {
        var stamp = (timestamp ?? DateTime.Now).ToString("yyyyMMdd_HHmmss");
        var outDir = outputDirectory ?? DefaultOutputDirectory;
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"diagnostics_{stamp}.zip");

        var warnings = new List<string>();
        var entries = new List<(string EntryName, string SourcePath)>();

        // 1. Журналы приложения: logs/app-*.log (+ обрезанный .log1 при ротации).
        var logsDir = logDirectory ?? PlatformPaths.LogDirectory;
        try
        {
            if (Directory.Exists(logsDir))
            {
                foreach (var file in Directory.EnumerateFiles(logsDir, "app-*.log*")
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    var name = Path.GetFileName(file);
                    entries.Add((Path.Combine("logs", name), file));
                }
            }
            else
            {
                warnings.Add("logs directory not found: " + logsDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add("logs enumeration failed: " + ex.Message);
        }

        // 2. Трассировка модальных окон (issue #94): %TEMP%/cm_modal_trace.log.
        var tempDir = tempDirectory ?? Path.GetTempPath();
        var modalTrace = Path.Combine(tempDir, "cm_modal_trace.log");
        if (File.Exists(modalTrace))
            entries.Add(("cm_modal_trace.log", modalTrace));
        else
            warnings.Add("cm_modal_trace.log not found: " + modalTrace);

        // 2.5. Журналы немодальных окон (issue #79, v1.0.1.42): logs/windows/*.log
        // (в т.ч. журнал окна «Развернуть тестовую копию» со стеком ошибки шага).
        // null — реальные файлы WindowJournalService; список — для тестируемости.
        try
        {
            var journals = windowJournalFiles ?? WindowJournalService.EnumerateJournalFiles();
            foreach (var file in journals)
            {
                if (!File.Exists(file))
                {
                    warnings.Add("window journal not found: " + file);
                    continue;
                }
                if (new FileInfo(file).Length > MaxEntryBytes)
                {
                    warnings.Add("window journal too large, skipped: " + file);
                    continue;
                }
                entries.Add((Path.Combine("logs", "windows", Path.GetFileName(file)), file));
            }
        }
        catch (Exception ex)
        {
            warnings.Add("window journals enumeration failed: " + ex.Message);
        }

        // 3. Конфиг диагностических флагов trace.json (issue #347) из каталога данных.
        var dataDir = appDataDirectory ?? PlatformPaths.AppDataDirectory;
        var traceConfig = Path.Combine(dataDir, "trace.json");
        if (File.Exists(traceConfig))
            entries.Add(("trace.json", traceConfig));
        else
            warnings.Add("trace.json not found: " + traceConfig);

        // 4. Сведения об окружении (всегда доступны — без чтения внешних файлов).
        var systemInfo = Path.Combine(Path.GetTempPath(), $"workspace1c_sysinfo_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(systemInfo, BuildSystemInfo(dataDir), new UTF8Encoding(false));
            entries.Add(("systeminfo.txt", systemInfo));
        }
        catch (Exception ex)
        {
            warnings.Add("systeminfo generation failed: " + ex.Message);
        }

        // 5. Архив: перезапись невозможна (имя с секундами), но на всякий случай удаляем прежний.
        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var added = 0;
            foreach (var (entryName, sourcePath) in entries)
            {
                try
                {
                    if (!File.Exists(sourcePath))
                    {
                        warnings.Add("missing: " + sourcePath);
                        continue;
                    }
                    if (new FileInfo(sourcePath).Length > MaxEntryBytes)
                    {
                        warnings.Add("too large, skipped: " + sourcePath);
                        continue;
                    }
                    // Замена разделителей: имя записи архива — всегда косая черта.
                    archive.CreateEntryFromFile(sourcePath, entryName.Replace('\\', '/'), CompressionLevel.Optimal);
                    added++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"entry failed ({entryName}): {ex.Message}");
                }
            }

            return new BundleResult(zipPath, added, warnings);
        }
        finally
        {
            // Временный systeminfo.txt больше не нужен: копия в архиве.
            try { File.Delete(systemInfo); } catch { /* не критично */ }
        }
    }

    /// <summary>Текст сведений об окружении для <c>systeminfo.txt</c>.</summary>
    private static string BuildSystemInfo(string dataDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Workspace 1C diagnostics bundle");
        sb.AppendLine("Created:            " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("App version:        " + VersionInfo.Display());
        sb.AppendLine("OS:                 " + Environment.OSVersion);
        sb.AppendLine("64-bit OS:          " + Environment.Is64BitOperatingSystem);
        sb.AppendLine("64-bit process:     " + Environment.Is64BitProcess);
        sb.AppendLine(".NET runtime:       " + Environment.Version);
        sb.AppendLine("Logical processors: " + Environment.ProcessorCount);
        sb.AppendLine("Culture (current):  " + System.Globalization.CultureInfo.CurrentCulture.Name);
        sb.AppendLine("Culture (ui):       " + System.Globalization.CultureInfo.CurrentUICulture.Name);
        sb.AppendLine("Data directory:     " + dataDir);
        try
        {
            sb.AppendLine("App directory:      " + AppContext.BaseDirectory);
            sb.AppendLine("Process uptime:     " + (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"d\.hh\:mm\:ss"));
        }
        catch
        {
            // Сведения о процессе не критичны для диагностики.
        }
        return sb.ToString();
    }
}
