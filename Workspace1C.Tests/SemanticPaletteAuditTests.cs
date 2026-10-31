using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Гарантии единства стиля из аудита UI 1.0.1.10 (ui-audit-report-v100110.md):
/// (1) семантические кисти Success/Warning/Error/Info присутствуют во всех четырёх
/// файлах тем (Light/Dark × WPF .xaml и Avalonia .axaml) и в палитрах ColorScheme
/// (включая генератор), (2) мягкие кисти DangerSoft/PinnedBadge/IconPicker тоже,
/// (3) разметка окон не содержит жёстких вхождений разнобойных красных/зелёных/серых
/// хардкодов, заменённых семантическими кистями, (4) корни WPF-окон не задают шрифт
/// литералом Segoe UI (следуют пользовательской настройке, п. 1.9).
/// </summary>
public sealed class SemanticPaletteAuditTests
{
    private static string RepoRoot()
    {
        // Тесты запускаются из bin-каталога; поднимаемся до корня репозитория.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    public static IEnumerable<object[]> ThemeFiles()
    {
        yield return new[] { "Themes", "LightTheme.xaml" };
        yield return new[] { "Themes", "DarkTheme.xaml" };
        yield return new[] { "Themes", "LightTheme.axaml" };
        yield return new[] { "Themes", "DarkTheme.axaml" };
    }

    [Theory]
    [MemberData(nameof(ThemeFiles))]
    public void ThemeFiles_ContainSemanticBrushes(params string[] parts)
    {
        var text = Read("Workspace1C", Path.Combine(parts));
        foreach (var key in new[] { "SuccessBrush", "WarningBrush", "ErrorBrush", "InfoBrush",
                     "PurpleAccentBrush", "PinkAccentBrush",
                     "DangerSoftBrush", "DangerSoftBorderBrush",
                     "PinnedBadgeBrush", "PinnedBadgeTextBrush",
                     "IconPickerBaseBrush", "IconPickerHoverBrush" })
        {
            Assert.True(text.Contains($"x:Key=\"{key}\""),
                $"{Path.Combine(parts)} обязан объявлять кисть {key} (аудит UI, пп. 2.1–2.6)");
        }
    }

    [Fact]
    public void ColorScheme_Palettes_ContainSemanticColors()
    {
        var light = ColorScheme.CreateLight();
        var dark = ColorScheme.CreateDark();
        foreach (var key in new[] { "SuccessColor", "WarningColor", "ErrorColor", "InfoColor",
                     "PurpleAccentBrush", "PinkAccentBrush",
                     "DangerSoftBrush", "DangerSoftBorderBrush",
                     "PinnedBadgeBrush", "PinnedBadgeTextBrush" })
        {
            Assert.True(light.Palette(false).ContainsKey(key),
                $"Светлая палитра обязана содержать {key}");
            Assert.True(dark.Palette(true).ContainsKey(key),
                $"Тёмная палитра обязана содержать {key}");
        }
    }

    [Fact]
    public void ColorScheme_Generator_ContainsSemanticColors()
    {
        var scheme = ColorScheme.GenerateFromColor("#3B82F6");
        foreach (var key in new[] { "SuccessColor", "WarningColor", "ErrorColor", "InfoColor",
                     "PurpleAccentBrush", "PinkAccentBrush" })
        {
            Assert.True(scheme.Palette(false).ContainsKey(key),
                $"Генератор светлой палитры обязан содержать {key}");
            Assert.True(scheme.Palette(true).ContainsKey(key),
                $"Генератор тёмной палитры обязан содержать {key}");
        }
    }

    public static IEnumerable<object[]> HardcodedColors()
    {
        // Разнобойные хардкоды (аудит UI, пп. 2.2, 2.3, 2.8, 2.10), заменённые
        // семантическими кистями: красные #E53935/#2E7D32-пары, зелёные, серые.
        yield return new[] { "#E53935" };  // красный ошибки → ErrorBrush
        yield return new[] { "#2E7D32" };  // зелёный статуса → SuccessBrush
        yield return new[] { "#22C55E" };  // зелёный → SuccessBrush
        yield return new[] { "#64748B" };  // серый → TextSecondaryBrush
        yield return new[] { "#94A3B8" };  // серый → TextSecondaryBrush
        yield return new[] { "#6B7280" };  // серый → TextSecondaryBrush
    }

    public static IEnumerable<object[]> HardcodedAccentColors()
    {
        // Разнобойные хардкоды v100112 (план v100112, подзадача B), заменённые
        // семантическими/акцентными кистями тем на обеих платформах (разметка и код).
        yield return new[] { "#E53935" };  // красный → ErrorBrush
        yield return new[] { "#2E7D32" };  // зелёный → SuccessBrush
        yield return new[] { "#2E8B57" };  // зелёный → SuccessBrush
        yield return new[] { "#22C55E" };  // зелёный → SuccessBrush
        yield return new[] { "#EF4444" };  // красный → ErrorBrush
        yield return new[] { "#F59E0B" };  // янтарь → WarningBrush
        yield return new[] { "#F97316" };  // оранжевый → WarningBrush
        yield return new[] { "#FBBF24" };  // янтарный значок → WarningBrush
        yield return new[] { "#3B82F6" };  // синий → InfoBrush
        yield return new[] { "#2563EB" };  // синий → InfoBrush
        yield return new[] { "#0EA5E9" };  // sky → InfoBrush
        yield return new[] { "#06B6D4" };  // cyan → InfoBrush
        yield return new[] { "#14B8A6" };  // teal → InfoBrush
        yield return new[] { "#7C3AED" };  // фиолетовый → PurpleAccentBrush
        yield return new[] { "#8B5CF6" };  // фиолетовый → PurpleAccentBrush
        yield return new[] { "#A855F7" };  // фиолетовый → PurpleAccentBrush
        yield return new[] { "#6366F1" };  // индиго → PurpleAccentBrush
        yield return new[] { "#EC4899" };  // розовый → PinkAccentBrush
        yield return new[] { "#1E293B" };  // тёмная панель → CardBackgroundColorBrush
        yield return new[] { "#B91C1C" };  // прижатый «крестик» → DangerPressedBrush
    }

    [Theory]
    [MemberData(nameof(HardcodedColors))]
    public void Views_DoNotUseHardcodedSemanticColors(string hex)
    {
        var viewsDir = Path.Combine(RepoRoot(), "Workspace1C", "Views");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.EnumerateFiles(viewsDir, "*.axaml", SearchOption.TopDirectoryOnly)))
        {
            // Блочные комментарии XML игнорируются: в них допустимы упоминания
            // прежних цветов («заменено с #22C55E на SuccessBrush»).
            var text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", "", RegexOptions.Singleline);
            var index = 0;
            foreach (var line in text.Split('\n'))
            {
                index++;
                if (line.Contains(hex) && !line.TrimStart().StartsWith("//"))
                    offenders.Add($"{Path.GetFileName(file)}:{index}");
            }
        }
        Assert.True(offenders.Count == 0,
            $"Хардкод {hex} должен быть заменён семантической кистью (аудит UI, пп. 2.2–2.10). Вхождения: {string.Join(", ", offenders)}");
    }

    [Theory]
    [MemberData(nameof(HardcodedAccentColors))]
    public void Views_CodeAndMarkup_DoNotUseHardcodedAccentColors(string hex)
    {
        // v100112 (подзадача B): ни разметка (*.xaml/*.axaml), ни код построения
        // интерфейса (*.cs) в Views не должны возвращаться к запрещённым HEX.
        // Осознанные запасные значения разрешены: строки-комментарии «//», строки
        // с явной пометкой «fallback» и строки вызовов хелперов чтения кисти темы
        // (запасной HEX — их штатный аргумент: ThemeBrushes.Get/TryBrush/TryGetBrush/
        // ThemeBrush/ThemeResource/TryFindResource/FromSpec).
        var viewsDir = Path.Combine(RepoRoot(), "Workspace1C", "Views");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(viewsDir, "*", SearchOption.TopDirectoryOnly)
                     .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            // Блочные комментарии XML игнорируются: в них допустимы упоминания
            // прежних цветов («заменено с #22C55E на SuccessBrush»).
            var text = File.ReadAllText(file);
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
            var index = 0;
            foreach (var line in text.Split('\n'))
            {
                index++;
                var isThemeBrushLookup = line.Contains("ThemeBrushes.Get(", StringComparison.Ordinal)
                    || line.Contains(".FromSpec(", StringComparison.Ordinal)
                    || line.Contains("TryBrush(", StringComparison.Ordinal)
                    || line.Contains("TryGetBrush(", StringComparison.Ordinal)
                    || line.Contains("ThemeBrush(", StringComparison.Ordinal)
                    || line.Contains("ThemeResource(", StringComparison.Ordinal)
                    || line.Contains("TryFindResource(", StringComparison.Ordinal);
                if (line.Contains(hex, StringComparison.OrdinalIgnoreCase)
                    && !line.TrimStart().StartsWith("//")
                    && !line.Contains("fallback", StringComparison.OrdinalIgnoreCase)
                    && !isThemeBrushLookup)
                    offenders.Add($"{Path.GetFileName(file)}:{index}");
            }
        }
        Assert.True(offenders.Count == 0,
            $"Хардкод {hex} должен быть заменён кистью темы (v100112, подзадача B). Вхождения: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void WpfWindowRoots_DoNotHardcodeSegoeUiFont()
    {
        // П. 1.9: шрифт окон задаётся настройками пользователя (ThemeManager.ApplyFont /
        // WindowChromeHelper.GlobalFontApplier), а не литералом в разметке.
        var viewsDir = Path.Combine(RepoRoot(), "Workspace1C", "Views");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            if (File.ReadAllText(file).Contains("FontFamily=\"Segoe UI\""))
                offenders.Add(Path.GetFileName(file));
        }
        Assert.True(offenders.Count == 0,
            $"Литералы FontFamily=\"Segoe UI\" должны быть убраны из окон (аудит UI, п. 1.9): {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Localization_Files_ContainNewCommonKeys()
    {
        // П. 5.4: новые ключи футеров диалогов добавлены в оба файла синхронно.
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = Read("Workspace1C", "Localization", "Languages", $"{lang}.json");
            foreach (var key in new[] { "Common.Run", "Common.Restore", "Common.OpenFolder",
                         "Schedule.Name", "Schedule.Enabled", "Schedule.Kind", "Schedule.Time",
                         "Schedule.DaysTitle", "Schedule.BackupScenario", "Schedule.Infobase",
                         "Schedule.ConfigFile" })
            {
                Assert.True(Regex.IsMatch(json, $"\"{Regex.Escape(key)}\"\\s*:"),
                    $"{lang}.json обязан содержать ключ {key}");
            }
        }
    }
}
