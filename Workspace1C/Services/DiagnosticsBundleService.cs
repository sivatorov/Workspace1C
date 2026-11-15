using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
#if WINDOWS
using System.Runtime.InteropServices;
#endif
using Workspace1C.Themes;

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
        // Issue #126: рамка окна невидима в удалённом сеансе/на Windows 10, где DWM
        // не рисует ребро/тень. По этим строкам поддержка видит, в каком сеансе
        // запущено приложение, что сообщает DWM и какие цвета рамки/фона активны.
        sb.AppendLine("Session:            " + DescribeSessionKind());
        sb.AppendLine("Session name:       " + (Environment.GetEnvironmentVariable("SESSIONNAME") ?? "unknown"));
        sb.AppendLine("Session id:         " + DescribeSessionId());
        sb.AppendLine("DWM composition:    " + DescribeDwmComposition());
        sb.AppendLine("DWM corner state:   " + DescribeDwmCornerState());
        sb.AppendLine("DPI scale:          " + DescribeDpiScale());
        sb.AppendLine("Active theme:       " + DescribeTheme());
        sb.AppendLine("Active scheme:      " + DescribeScheme());
        sb.AppendLine("BorderColor:        " + DescribePaletteColor("BorderColor"));
        sb.AppendLine("ContentBackgroundColor: " + DescribePaletteColor("ContentBackgroundColor"));
        sb.AppendLine("Frame contrast:     " + DescribeFrameContrast());
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

    // ---------- Сведения о сеансе/DWM/теме для диагностики issue #126 ----------

#if WINDOWS
    private const int SmRemoteSession = 0x1000;

    // DWMWA_WINDOW_CORNER_STATE (34): 1 = не скруглять, 2 = скруглено.
    private const int DwmWindowCornerState = 34;
    private const int DwmStateDoNotRound = 1;
    private const int DwmStateRounded = 2;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
#endif

    /// <summary>Локальный или удалённый (RDP) сеанс — SM_REMOTESESSION (issue #126).</summary>
    private static string DescribeSessionKind()
    {
#if WINDOWS
        try
        {
            return GetSystemMetrics(SmRemoteSession) != 0 ? "remote (RDP)" : "local";
        }
        catch
        {
            // API недоступен — сеанс неизвестен, сбор диагностики не ломается.
        }
#endif
        return "unknown (non-Windows)";
    }

    /// <summary>Идентификатор сеанса Windows (для сопоставления с журналом событий).</summary>
    private static string DescribeSessionId()
    {
        try
        {
            return Process.GetCurrentProcess().SessionId.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>Состояние композиции DWM с кодом возврата API (issue #126).</summary>
    private static string DescribeDwmComposition()
    {
#if WINDOWS
        try
        {
            var hr = DwmIsCompositionEnabled(out var enabled);
            return hr == 0 ? (enabled ? "enabled" : "disabled") : $"unavailable (hr=0x{hr:X8})";
        }
        catch
        {
            return "unavailable";
        }
#else
        return "not applicable (non-Windows)";
#endif
    }

    /// <summary>
    /// Фактическое состояние скругления углов главного окна по данным DWM
    /// (DWMWA_WINDOW_CORNER_STATE). На Windows 10 атрибут не поддерживается —
    /// «unknown», без ложного «do-not-round» (issue #126).
    /// </summary>
    private static string DescribeDwmCornerState()
    {
#if WINDOWS
        try
        {
            var hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd == IntPtr.Zero)
                return "unknown (no main window)";

            var hr = DwmGetWindowAttribute(hwnd, DwmWindowCornerState, out var state, sizeof(int));
            if (hr != 0)
                return $"unknown (hr=0x{hr:X8})";

            return state switch
            {
                DwmStateRounded => "rounded",
                DwmStateDoNotRound => "do-not-round",
                _ => $"other ({state})"
            };
        }
        catch
        {
            return "unknown";
        }
#else
        return "not applicable (non-Windows)";
#endif
    }

    /// <summary>Масштаб DPI системы (issue #126: на нецелом DPI линия рамки «исчезает»).</summary>
    private static string DescribeDpiScale()
    {
#if WINDOWS
        try
        {
            var dpi = GetDpiForSystem();
            return dpi > 0 ? (dpi / 96.0).ToString("0.##") + $" ({dpi} DPI)" : "unknown";
        }
        catch
        {
            return "unknown";
        }
#else
        return "1.0 (assumed)";
#endif
    }

    private static string DescribeTheme()
    {
        try
        {
            return ThemeManager.CurrentTheme;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string DescribeScheme()
    {
        try
        {
            return ThemeManager.CurrentScheme.Name;
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>Фактическое значение цвета активной палитры (BorderColor/ContentBackgroundColor).</summary>
    private static string DescribePaletteColor(string key)
    {
        try
        {
            var dark = string.Equals(ThemeManager.CurrentTheme, ThemeManager.DarkThemeName, StringComparison.Ordinal);
            var palette = ThemeManager.CurrentScheme.Palette(dark);
            return palette.TryGetValue(key, out var hex) ? hex : "(not set)";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Текущий контраст рамки к фону содержимого по WCAG и цвет после усиления
    /// (issue #126): если значение ниже 3:1, линия рамки невидима.
    /// </summary>
    private static string DescribeFrameContrast()
    {
        try
        {
            var frame = WindowFrameColor.FromHex(
                DescribePaletteColor("BorderColor"), new WindowFrameColor(0xE2, 0xE8, 0xF0));
            var background = WindowFrameColor.FromHex(
                DescribePaletteColor("ContentBackgroundColor"), new WindowFrameColor(0xF1, 0xF5, 0xF9));

            var ratio = WindowFramePolicy.ContrastRatio(frame, background);
            var strengthened = WindowFramePolicy.EnsureContrast(frame, background);
            var strengthenedRatio = WindowFramePolicy.ContrastRatio(strengthened, background);
            return $"{ratio:0.00}:1 (strengthened {strengthened.ToHex()} → {strengthenedRatio:0.00}:1)";
        }
        catch
        {
            return "unknown";
        }
    }
}
