using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI P2-2 (v1.0.1.92): единый набор стилей кнопок.
/// <list type="bullet">
/// <item>в <c>Workspace1C/Views/*.xaml</c> нет локальных объявлений стилей кнопок;</item>
/// <item>все ключи кнопочных стилей, на которые ссылаются окна, есть в словарях WPF и Avalonia;</item>
/// <item>hover вторичной кнопки меняет только рамку (обе платформы, issue #107);</item>
/// <item>IconButton — 32×32 и подсветка ItemHoverBrush;</item>
/// <item>PrimaryButton/SecondaryButton/DangerButton — padding 14,8 при MinHeight 36.</item>
/// </list>
/// </summary>
public sealed class ButtonStyleUnificationP22Tests
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

    private static string StripComments(string xaml) =>
        Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string ExtractStyleBlock(string xaml, string key)
    {
        var start = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start > 0, $"Стиль {key} не найден");
        var open = xaml.LastIndexOf('<', start);
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        var close = "</Style>";
        if (end < 0)
        {
            end = xaml.IndexOf("</ControlTheme>", start, StringComparison.Ordinal);
            close = "</ControlTheme>";
        }
        Assert.True(end > start, $"Не найден конец стиля {key}");
        return xaml[open..(end + close.Length)];
    }

    [Fact]
    public void WpfViews_HaveNoLocalButtonStyleDeclarations()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(views, "*.xaml"))
        {
            var text = StripComments(File.ReadAllText(file));
            if (Regex.IsMatch(text, @"<Style\s+x:Key=""[^""]*Button[^""]*""", RegexOptions.IgnoreCase))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "Локальные стили кнопок должны быть перенесены в тему (P2-2): " +
            string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void WpfThemes_DefineCanonicalButtonKeys(string themeFile)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        foreach (var key in new[]
                 {
                     "PrimaryButton", "SecondaryButton", "DangerButton",
                     "IconButton", "SelectAllButton", "RowActionButton"
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AvaloniaTheme_DefinesCanonicalButtonKeys()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        foreach (var key in new[]
                 {
                     "PrimaryButton", "SecondaryButton", "DangerButton",
                     "IconButton", "SelectAllButton", "RowActionButton"
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", axaml, StringComparison.Ordinal);
        }

        var constants = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ControlThemes.Avalonia.cs"));
        foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "DangerButton", "IconButton", "SelectAllButton", "RowActionButton" })
        {
            Assert.Contains($"public const string {name} = \"{name}\";", constants, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WpfViews_ReferencedButtonStyles_ExistInBothThemes()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(views, "*.xaml"))
        {
            var text = StripComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(text, @"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_]+)\}"))
            {
                var key = m.Groups[1].Value;
                if (Regex.IsMatch(key, "^[A-Za-z0-9_]*Button$", RegexOptions.IgnoreCase))
                    used.Add(key);
            }
        }

        Assert.NotEmpty(used);
        // Стили кнопок управления окном объявлены в App.xaml (общие ресурсы),
        // остальные — в словарях LightTheme/DarkTheme.
        var appKeys = StripComments(ReadProjectFile(Path.Combine("Workspace1C", "App.xaml")));
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            var missing = used.Where(key =>
                !xaml.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal) &&
                !appKeys.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                $"{theme}: окна ссылаются на отсутствующие стили кнопок: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void AvaloniaViews_ReferencedThemeKeys_ExistInControls()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        var constantsSource = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ControlThemes.Avalonia.cs"));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(constantsSource, @"public const string (\w+) = ""([^""]+)"";"))
            map[m.Groups[1].Value] = m.Groups[2].Value;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(views, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"ControlThemes\.(\w+)"))
            {
                if (map.TryGetValue(m.Groups[1].Value, out var themeKey))
                    used.Add(themeKey);
            }
        }

        Assert.NotEmpty(used);
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var missing = used.Where(key => !axaml.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            "Avalonia-код ссылается на отсутствующие темы контролов: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void Wpf_SecondaryButton_HoverChangesOnlyBorder(string themeFile)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        var style = ExtractStyleBlock(xaml, "SecondaryButton");
        var hover = Regex.Match(style,
            @"<Trigger\s+Property=""IsMouseOver""\s+Value=""True"">(?<body>.*?)</Trigger>",
            RegexOptions.Singleline);
        Assert.True(hover.Success, $"hover вторичной кнопки не найден ({themeFile})");
        Assert.DoesNotContain("Background", hover.Groups["body"].Value);
        Assert.Contains("BorderBrush", hover.Groups["body"].Value);
    }

    [Fact]
    public void Avalonia_SecondaryButton_HoverChangesOnlyBorder()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = ExtractStyleBlock(axaml, "SecondaryButton");
        var hover = Regex.Match(theme,
            @"<Style\s+Selector=""\^:pointerover\s+/template/\s+Border#PART_Surface"">(?<body>.*?)</Style>",
            RegexOptions.Singleline);
        Assert.True(hover.Success, "hover вторичной кнопки Avalonia не найден");
        Assert.DoesNotContain("Background", hover.Groups["body"].Value);
        Assert.Contains("BorderBrush", hover.Groups["body"].Value);
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void Wpf_IconButton_Is32x32WithItemHover(string themeFile)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        var style = ExtractStyleBlock(xaml, "IconButton");
        Assert.Contains("<Setter Property=\"Width\" Value=\"32\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"32\"/>", style, StringComparison.Ordinal);
        Assert.Contains("ItemHoverBrush", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_IconButton_Is32x32WithItemHover()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = ExtractStyleBlock(axaml, "IconButton");
        Assert.Contains("<Setter Property=\"Width\" Value=\"32\"/>", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"32\"/>", theme, StringComparison.Ordinal);
        Assert.Contains("ItemHoverBrush", theme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void Wpf_TextButtons_UsePadding14_8(string themeFile)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        foreach (var key in new[] { "ModernButton", "PrimaryButton", "SecondaryButton", "DangerButton" })
        {
            var style = ExtractStyleBlock(xaml, key);
            if (key == "PrimaryButton")
                Assert.Contains("<Setter Property=\"Padding\" Value=\"14,8\"/>", style, StringComparison.Ordinal);
            if (key == "ModernButton")
                Assert.Contains("<Setter Property=\"Padding\" Value=\"14,8\"/>", style, StringComparison.Ordinal);
        }
    }
}
