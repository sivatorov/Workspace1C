using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Полнота наборов иконок тем (0.3.12.59): словари IconsMaterial/MacOS/WindowsXp/Glass
/// (.xaml и .axaml) сгенерированы tools/icongen и должны покрывать КАЖДЫЙ ключ
/// геометрии базового набора (Icons.xaml / Icons.axaml) и — в WPF — каждый
/// используемый в разметке и коде вид значка materialDesign:PackIcon (Pack.&lt;Kind&gt;).
/// Иначе тема молча показывала бы Fluent-иконки вперемешку со своими.
/// Тесты читают файлы проекта (как SettingsWindowXamlResourcesTests) и не требуют UI.
/// </summary>
public sealed class IconSetCoverageTests
{
    private static readonly string[] GeneratedSets = { "Material", "MacOS", "WindowsXp", "Glass" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Themes")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }

    private static string Themes(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Workspace1C", "Themes", file));

    private static Dictionary<string, string> Geometries(string file)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Themes(file),
                     "<(?:Stream)?Geometry x:Key=\"([^\"]+)\">([^<]+)</(?:Stream)?Geometry>"))
            result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return result;
    }

    private static IEnumerable<string> BaseKeys(string file) =>
        Geometries(file).Keys.Where(k => k.StartsWith("Icon", StringComparison.Ordinal));

    private static IReadOnlyCollection<string> UsedPackKinds()
    {
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var root = Path.Combine(RepoRoot(), "Workspace1C");
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                continue;
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text,
                             "PackIcon[^>]*?\\sKind=\"([A-Za-z0-9]+)\"", RegexOptions.Singleline))
                    kinds.Add(m.Groups[1].Value);
                // Kind в сеттерах стилей и триггеров PackIcon.
                foreach (Match m in Regex.Matches(text, "Property=\"Kind\" Value=\"([A-Za-z0-9]+)\""))
                    kinds.Add(m.Groups[1].Value);
            }
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "PackIconKind\\.([A-Za-z0-9]+)"))
                    kinds.Add(m.Groups[1].Value);
            }
        }
        return kinds;
    }

    public static IEnumerable<object[]> SetsAndPlatforms() =>
        from set in GeneratedSets
        from ext in new[] { "xaml", "axaml" }
        select new object[] { set, ext };

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void EveryBaseIconKey_HasVariantInSet(string set, string ext)
    {
        var dict = Geometries($"Icons{set}.{ext}");
        var missing = BaseKeys($"Icons.{ext}")
            .Where(k => !dict.ContainsKey(k + "." + set))
            .ToList();
        Assert.True(missing.Count == 0,
            $"В Icons{set}.{ext} нет вариантов для: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("Material")]
    [InlineData("MacOS")]
    [InlineData("WindowsXp")]
    [InlineData("Glass")]
    [InlineData("Outline")]
    public void EveryUsedPackIconKind_HasVariantInSet(string set)
    {
        var dict = Geometries($"Icons{set}.xaml");
        var missing = UsedPackKinds().Where(k => !dict.ContainsKey($"Pack.{k}.{set}")).ToList();
        Assert.True(missing.Count == 0,
            $"В Icons{set}.xaml нет значков PackIcon: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void WindowGlyphs_ArePresentInSet(string set, string ext)
    {
        var dict = Geometries($"Icons{set}.{ext}");
        foreach (var glyph in new[] { "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowRestore", "GlyphWindowClose" })
            Assert.True(dict.ContainsKey(glyph + "." + set), $"Icons{set}.{ext}: нет глифа {glyph}.{set}");
    }

    [Fact]
    public void GlassSet_HasTranslucentBackLayers()
    {
        // Duotone-набор Glass: у иконок есть фоновый слой *.Back.Glass.
        var dict = Geometries("IconsGlass.xaml");
        Assert.Contains("IconSettings.Back.Glass", dict.Keys);
        Assert.Contains("Pack.Cog.Back.Glass", dict.Keys);
        Assert.True(dict.Keys.Count(k => k.Contains(".Back.Glass", StringComparison.Ordinal)) > 100);
    }

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void GeneratedPaths_AreWellFormed(string set, string ext)
    {
        // Путь — мини-язык геометрии WPF/Avalonia: необязательный префикс правила
        // заливки «F1», затем команды и числа. Мусор (теги, буквы вне команд) ломал
        // бы загрузку словаря целиком.
        var valid = new Regex(@"^(F[01]\s+)?[Mm][\s\dMmLlHhVvCcSsQqTtAaZz.,eE+\-]*$");
        foreach (var (key, data) in Geometries($"Icons{set}.{ext}"))
            Assert.True(valid.IsMatch(data), $"Icons{set}.{ext}: некорректный путь у {key}");
    }

    [Theory]
    [InlineData("xaml")]
    [InlineData("axaml")]
    public void BaseDictionary_HasLockAndUnlockIcons(string ext)
    {
        // P1a аудита UI: эмодзи 🔒/🔓 заменены ключами IconLock/IconUnlock.
        var dict = Geometries($"Icons.{ext}");
        Assert.Contains("IconLock", dict.Keys);
        Assert.Contains("IconUnlock", dict.Keys);
    }

    [Theory]
    [InlineData("xaml")]
    [InlineData("axaml")]
    public void BaseDictionary_HasWindowAndTemplateGlyphs(string ext)
    {
        // 0.3.12.61: глифы кнопок окна и шаблонов — отдельные ключи базового словаря.
        var dict = Geometries($"Icons.{ext}");
        foreach (var glyph in new[]
                 {
                     "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowRestore", "GlyphWindowClose",
                     "GlyphComboArrow", "GlyphSubmenuArrow", "GlyphCheckMark", "GlyphCheckDash"
                 })
            Assert.True(dict.ContainsKey(glyph), $"Icons.{ext}: нет глифа {glyph}");
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    [InlineData("Controls.axaml")]
    public void ControlTemplates_UseThemeGlyphs_NotLiteralPaths(string file)
    {
        // Шаблоны элементов (ComboBox, CheckBox, подменю) берут глифы из набора иконок
        // темы DynamicResource'ом; литеральный путь не менялся бы вместе с темой.
        var text = Themes(file);
        Assert.DoesNotMatch(new Regex("Data=\"[Mm][\\s\\d]"), text);
        Assert.DoesNotContain("{StaticResource IconCheck}", text);
    }

    [Theory]
    [InlineData("Icons.xaml")]
    [InlineData("Icons.axaml")]
    [InlineData("IconsOutline.xaml")]
    [InlineData("IconsOutline.axaml")]
    [InlineData("IconsMaterial.xaml")]
    [InlineData("IconsMaterial.axaml")]
    [InlineData("IconsMacOS.xaml")]
    [InlineData("IconsMacOS.axaml")]
    [InlineData("IconsWindowsXp.xaml")]
    [InlineData("IconsWindowsXp.axaml")]
    [InlineData("IconsGlass.xaml")]
    [InlineData("IconsGlass.axaml")]
    public void IconDictionaries_HaveNoDuplicateKeys(string file)
    {
        // Повтор x:Key ломает загрузку словаря WPF при старте приложения
        // (XamlParseException «Задание свойства DeferrableContent вызвало исключение»).
        var duplicates = Regex.Matches(Themes(file), "x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, $"{file}: повторяющиеся ключи {string.Join(", ", duplicates.Take(10))}");
    }
}
