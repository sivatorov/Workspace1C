using System.Text.RegularExpressions;
using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI, пункт P1-1 (v1.0.1.88): выделение в навигации, вкладках и
/// переключателях следует акценту темы.
/// <list type="bullet">
/// <item>стиль выбранного пункта не содержит фиксированных янтарных литералов
/// (#B45309/#FFD54D) и не использует TreeSelectedBrush;</item>
/// <item>фон выбранного пункта берётся из акцента схемы (подмешивание 14 %);</item>
/// <item>текст на выбранном фоне даёт контраст ≥ 4.5:1 для всех встроенных
/// и нескольких сгенерированных схем;</item>
/// <item>включённый тумблер использует AccentBrush, а не зелёный.</item>
/// </list>
/// Проверки контраста — расчётом по WCAG (без UI): цвета берутся из той же
/// чистой функции <see cref="ColorScheme.NavSelectedBackgroundHex"/>, которую
/// применяет ThemeManager обеих платформ.
/// </summary>
public sealed class NavSelectionAccentTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Возвращает подстроку между маркерами без XML-комментариев.</summary>
    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        text = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Маркер не найден: {startMarker}");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Маркер не найден после {startMarker}: {endMarker}");
        return text.Substring(start, end - start);
    }

    // ===================== Контраст и зависимость от акцента =====================

    [Fact]
    public void NavSelectedBackground_IsDerivedFromAccent_NotFixedAmber()
    {
        const string background = "#FFFFFF";

        var blue = ColorScheme.NavSelectedBackgroundHex("#2563EB", background);
        var material = ColorScheme.NavSelectedBackgroundHex("#7C4DFF", background);
        var amber = ColorScheme.NavSelectedBackgroundHex("#FDBF00", background);

        Assert.NotEqual(blue, material);
        Assert.NotEqual(blue, amber);
        // 14 % синего на белом: R=0.86*255+0.14*37=224.5, G=0.86*255+0.14*99=233.2,
        // B=0.86*255+0.14*235=252.2 → #E0E9FC (округление до байта).
        Assert.Equal("#E0E9FC", blue);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("blue")]
    [InlineData("green")]
    [InlineData("material")]
    [InlineData("macos")]
    [InlineData("windowsxp")]
    [InlineData("glass")]
    public void NavSelectedText_MeetsWcag_ForEveryBuiltInTheme(string themeId)
    {
        var theme = ThemeCatalog.Find(themeId);
        Assert.NotNull(theme);

        foreach (var dark in new[] { false, true })
        {
            var accent = theme!.Scheme.PaletteValue(dark, "AccentColor");
            var background = theme.Scheme.PaletteValue(dark, "ContentBackgroundColor");
            var navBackground = ColorScheme.NavSelectedBackgroundHex(accent, background);
            var navText = ColorScheme.NavSelectedTextHex(accent, background);

            var ratio = ColorScheme.ContrastRatio(navText, navBackground);
            Assert.True(ratio >= 4.5,
                $"Тема {themeId} (dark={dark}): текст {navText} на фоне {navBackground} " +
                $"даёт {ratio:0.00}:1 — меньше 4.5:1.");
        }
    }

    [Theory]
    [InlineData("#2563EB")]
    [InlineData("#7C4DFF")]
    [InlineData("#16A34A")]
    [InlineData("#DC2626")]
    [InlineData("#FDBF00")]
    [InlineData("#0EA5E9")]
    [InlineData("#EC4899")]
    [InlineData("#13522C")]
    public void NavSelectedText_MeetsWcag_ForGeneratedSchemes(string baseColor)
    {
        var scheme = ColorScheme.GenerateFromColor(baseColor);

        foreach (var dark in new[] { false, true })
        {
            var accent = scheme.PaletteValue(dark, "AccentColor");
            var background = scheme.PaletteValue(dark, "ContentBackgroundColor");
            var navBackground = ColorScheme.NavSelectedBackgroundHex(accent, background);
            var navText = ColorScheme.NavSelectedTextHex(accent, background);

            var ratio = ColorScheme.ContrastRatio(navText, navBackground);
            Assert.True(ratio >= 4.5,
                $"Сгенерированная схема из {baseColor} (dark={dark}): текст {navText} на фоне " +
                $"{navBackground} даёт {ratio:0.00}:1 — меньше 4.5:1.");
        }
    }

    // ===================== Стили: нет фиксированного янтаря =====================

    [Fact]
    public void WpfSettingsNavSelectedStyle_UsesAccentBrushes_WithoutAmberLiterals()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var style = ExtractBetween(xaml, "x:Key=\"SettingsTabItem\"", "x:Key=\"SettingsSubTabControl\"");

        Assert.Contains("{DynamicResource NavSelectedBrush}", style);
        Assert.Contains("{DynamicResource NavSelectedTextBrush}", style);
        Assert.Contains("{DynamicResource AccentBrush}", style);
        Assert.DoesNotContain("TreeSelectedBrush", style);
        Assert.DoesNotContain("TreeSelectedTextBrush", style);
        Assert.DoesNotContain("#B45309", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFD54D", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WpfConnectionTabsSelectedStyle_UsesAccentBrushes_WithoutAmberLiterals()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));
        var style = ExtractBetween(xaml, "x:Key=\"ConnTabItem\"", "</Window.Resources>");

        Assert.Contains("{DynamicResource NavSelectedBrush}", style);
        Assert.Contains("{DynamicResource NavSelectedTextBrush}", style);
        Assert.Contains("{DynamicResource AccentBrush}", style);
        Assert.DoesNotContain("TreeSelectedBrush", style);
        Assert.DoesNotContain("TreeSelectedTextBrush", style);
        Assert.DoesNotContain("#B45309", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFD54D", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AvaloniaNavigationSelectedTheme_UsesAccentBrushes_WithoutAmberLiterals()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = ExtractBetween(axaml, "x:Key=\"ConnTabItem\"", "x:Key=\"OptionCard\"");

        Assert.Contains("{DynamicResource NavSelectedBrush}", theme);
        Assert.Contains("{DynamicResource NavSelectedTextBrush}", theme);
        Assert.Contains("{DynamicResource AccentBrush}", theme);
        Assert.DoesNotContain("TreeSelectedBrush", theme);
        Assert.DoesNotContain("TreeSelectedTextBrush", theme);
        Assert.DoesNotContain("#B45309", theme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFD54D", theme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsGroupHeader_IsNeutral_WithoutSelectedPlate()
    {
        // Аудит UI P1-1 п.2: заголовок раздела «Настройки» не выглядит выбранным
        // пунктом — у него нет заливки-плашки, только SemiBold-подпись.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var wpfGroup = ExtractBetween(wpf, "x:Key=\"SettingsGroupTabItem\"", "x:Key=\"SettingsSubTabControl\"");
        Assert.DoesNotContain("NavSelectedBrush", wpfGroup);
        Assert.DoesNotContain("TreeSelectedBrush", wpfGroup);

        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var avaloniaGroup = ExtractBetween(axaml, "x:Key=\"SettingsGroupTabItem\"", "x:Key=\"SettingsToggle\"");
        Assert.DoesNotContain("NavSelectedBrush", avaloniaGroup);
        Assert.DoesNotContain("TreeSelectedBrush", avaloniaGroup);

        // Первая вкладка окна настроек действительно использует нейтральный стиль.
        Assert.Contains("Style=\"{StaticResource SettingsGroupTabItem}\"", wpf);
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("groupHeader: true", source);
        Assert.Contains("ControlThemes.SettingsGroupTabItem", source);
    }

    // ===================== Тумблеры: акцент вместо зелёного =====================

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void WpfSettingsToggles_CheckedUseAccent_NotGreen(string themeFile)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        var toggle = ExtractBetween(xaml, "x:Key=\"SettingsToggle\"", "</ResourceDictionary>");

        Assert.Contains("Property=\"IsChecked\" Value=\"True\"", toggle);
        Assert.Contains("{DynamicResource AccentBrush}", toggle);
        Assert.DoesNotContain("#16A34A", toggle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#22C55E", toggle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WpfColumnVisibilitySwitch_CheckedUsesAccent_NotGreen()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var style = ExtractBetween(xaml, "x:Key=\"ColumnVisibilitySwitch\"", "x:Key=\"OutlineButtonStyle\"");

        Assert.Contains("Property=\"IsChecked\" Value=\"True\"", style);
        Assert.Contains("{DynamicResource AccentBrush}", style);
        Assert.DoesNotContain("#16A34A", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#22C55E", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AvaloniaSettingsToggles_CheckedUseAccent_NotGreen()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var settingsToggle = ExtractBetween(axaml, "x:Key=\"SettingsToggle\"", "x:Key=\"ColumnVisibilitySwitch\"");
        var columnSwitch = ExtractBetween(axaml, "x:Key=\"ColumnVisibilitySwitch\"", "x:Key=\"IconPickButton\"");

        foreach (var block in new[] { settingsToggle, columnSwitch })
        {
            Assert.Contains(":checked", block);
            Assert.Contains("{DynamicResource AccentBrush}", block);
            Assert.DoesNotContain("#16A34A", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#22C55E", block, StringComparison.OrdinalIgnoreCase);
        }
    }
}
