using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI P1a: в интерфейсе не должно быть эмодзи и текстовых глифов, заменённых
/// векторными иконками. Тест сканирует разметку и UI-код, исключая комментарии
/// (в них допустимы пояснения), и проверяет покрытие новых ключей наборами иконок.
/// </summary>
public sealed class UiGlyphBanTests
{
    /// <summary>
    /// Запрещённые в UI символы, заменённые на Icon*:
    /// P1a — 🌐 (U+1F310), 🔍 (U+1F50D), 🔒 (U+1F512), 🔓 (U+1F513),
    /// ✖ (U+2716), ⚙ (U+2699), ➜ (U+279C);
    /// P1b — ⟳, 🔌, 🛡, 📦, 💾, 🗑, ⇄, ⇅, ☰, ✔, ▲/▼, ✎, ●, ↑/↓.
    /// </summary>
    private static readonly string[] ForbiddenUiGlyphs =
    {
        "\U0001F310",
        "\U0001F50D",
        "\U0001F512",
        "\U0001F513",
        "\u2716",
        "\u2699",
        "\u279C",
        "\u27F3",
        "\U0001F50C",
        "\U0001F6E1",
        "\U0001F4E6",
        "\U0001F4BE",
        "\U0001F5D1",
        "\u21C4",
        "\u21C5",
        "\u2630",
        "\u2714",
        "\u25B2",
        "\u25BC",
        "\u270E",
        "\u25CF",
        "\u2191",
        "\u2193",
    };

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

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    private static IEnumerable<string> UiFiles(string relativeDir, params string[] extensions)
    {
        var root = Path.Combine(RepoRoot(), relativeDir);
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                continue;
            if (extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                yield return file;
        }
    }

    /// <summary>
    /// Убирает комментарии XAML и C#, чтобы пояснения в коде не давали ложных
    /// срабатываний. Строки с «//» внутри литералов не затрагиваются: удаляются
    /// только строки, начинающиеся с «//» (включая XML-doc «///»).
    /// </summary>
    private static string StripComments(string text)
    {
        text = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        text = Regex.Replace(text, @"(?m)^\s*//.*$", string.Empty);
        return text;
    }

    /// <summary>
    /// Убирает допустимые не-UI использования символа «●»: маску пароля
    /// (<c>PasswordChar = '●'</c> / <c>PasswordChar="●"</c>). Это не глиф интерфейса.
    /// </summary>
    private static string StripAllowedLiterals(string text)
        => Regex.Replace(text, @"PasswordChar\s*=\s*['""]●['""]", string.Empty);

    [Fact]
    public void XamlUi_HasNoBannedEmojiOrTextGlyphs()
    {
        var offenders = new List<string>();
        var files = UiFiles("Workspace1C/Views", ".xaml", ".axaml")
            .Concat(new[] { Path.Combine(RepoRoot(), "Workspace1C", "Themes", "Controls.axaml") });

        foreach (var file in files)
        {
            var text = StripAllowedLiterals(StripComments(File.ReadAllText(file)));
            foreach (var glyph in ForbiddenUiGlyphs)
            {
                if (text.Contains(glyph, StringComparison.Ordinal))
                    offenders.Add($"{Relative(file)}: {glyph}");
            }
        }

        Assert.True(offenders.Count == 0,
            "В XAML остались эмодзи/текстовые глифы (P1a): " + string.Join(", ", offenders));
    }

    [Fact]
    public void CSharpUiCode_HasNoBannedEmojiOrTextGlyphs()
    {
        var offenders = new List<string>();
        foreach (var file in UiFiles("Workspace1C/Views", ".cs"))
        {
            var text = StripAllowedLiterals(StripComments(File.ReadAllText(file)));
            foreach (var glyph in ForbiddenUiGlyphs)
            {
                if (text.Contains(glyph, StringComparison.Ordinal))
                    offenders.Add($"{Relative(file)}: {glyph}");
            }
        }

        Assert.True(offenders.Count == 0,
            "В UI-коде остались эмодзи/текстовые глифы (P1a): " + string.Join(", ", offenders));
    }

    /// <summary>
    /// P1b: в кнопках не должно оставаться текстового многоточия «…» — для «Обзора»
    /// используется векторная иконка папки.
    /// </summary>
    [Fact]
    public void Buttons_DoNotUseEllipsisGlyph()
    {
        var offenders = new List<string>();
        foreach (var file in UiFiles("Workspace1C/Views", ".cs", ".xaml", ".axaml"))
        {
            var text = StripComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(text, "Content\\s*=\\s*\"…\""))
                offenders.Add($"{Relative(file)}: {m.Value}");
        }

        Assert.True(offenders.Count == 0,
            "В кнопках осталось текстовое «…» (P1b): " + string.Join(", ", offenders));
    }

    /// <summary>
    /// P1b: размеры иконок в UI задаются единой шкалой <c>UiMetrics.Icon*</c>,
    /// а не разрозненными литералами 13/14/15/18/22/28/34/44.
    /// </summary>
    [Fact]
    public void UiIconSizes_UseUiMetricsScale()
    {
        var metrics = File.ReadAllText(
            Path.Combine(RepoRoot(), "Workspace1C", "Controls", "UiMetrics.Avalonia.cs"));
        foreach (var expected in new[]
                 {
                     "public const double IconInline = 12;",
                     "public const double IconControl = 16;",
                     "public const double IconToolbar = 20;",
                     "public const double IconHeader = 24;",
                     "public const double IconEmpty = 32;",
                     "public const double IconEmptyLarge = 48;"
                 })
            Assert.Contains(expected, metrics);

        var forbidden = new HashSet<int> { 13, 14, 15, 18, 22, 28, 34, 44 };
        var offenders = new List<string>();
        var files = UiFiles("Workspace1C/Views", ".cs")
            .Concat(UiFiles("Workspace1C/Controls", ".cs"))
            .Concat(UiFiles("Workspace1C/Services", ".cs"));

        foreach (var file in files)
        {
            var text = StripComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(text, @"(?:MakeIcon|IconAndText)\s*\([^,]+,\s*(\d+)\s*[,)]"))
            {
                if (int.TryParse(m.Groups[1].Value, out var size) && forbidden.Contains(size))
                    offenders.Add($"{Relative(file)}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Размеры иконок вне шкалы UiMetrics (P1b): " + string.Join(", ", offenders));
    }

    [Fact]
    public void EventLogSeverity_UsesIconKeys_NotTextGlyphs()
    {
        var text = StripComments(File.ReadAllText(
            Path.Combine(RepoRoot(), "Workspace1C", "Services", "EventLog", "EventLogPresentation.cs")));

        foreach (var glyph in new[] { "\u25B2", "\u2716", "\u270E", "\u2139" })
            Assert.DoesNotContain(glyph, text);

        Assert.Contains("\"IconWarning\"", text);
        Assert.Contains("\"IconError\"", text);
        Assert.Contains("\"IconNote\"", text);
        Assert.Contains("\"IconInfo\"", text);
    }

    public static IEnumerable<object[]> IconSetsAndExtensions() =>
        from set in new[] { "Material", "MacOS", "WindowsXp", "Glass", "Outline" }
        from ext in new[] { "xaml", "axaml" }
        select new object[] { set, ext };

    [Theory]
    [MemberData(nameof(IconSetsAndExtensions))]
    public void LockAndUnlockIcons_ExistInEverySet(string set, string ext)
    {
        var text = File.ReadAllText(
            Path.Combine(RepoRoot(), "Workspace1C", "Themes", $"Icons{set}.{ext}"));

        Assert.Contains($"x:Key=\"IconLock.{set}\"", text);
        Assert.Contains($"x:Key=\"IconUnlock.{set}\"", text);
    }

    [Theory]
    [InlineData("xaml")]
    [InlineData("axaml")]
    public void GlassLockIcons_HaveBackLayer(string ext)
    {
        var text = File.ReadAllText(
            Path.Combine(RepoRoot(), "Workspace1C", "Themes", $"IconsGlass.{ext}"));

        Assert.Contains("x:Key=\"IconLock.Back.Glass\"", text);
        Assert.Contains("x:Key=\"IconUnlock.Back.Glass\"", text);
    }

    [Fact]
    public void TreeExpanders_UseUnifiedVectorChevron()
    {
        var text = File.ReadAllText(
            Path.Combine(RepoRoot(), "Workspace1C", "Themes", "Controls.axaml"));

        // Текстовые «+»/«−» и старый TextBlock-раскрыватель больше не используются.
        Assert.DoesNotContain("ExpanderSymbol", text);
        Assert.DoesNotContain("Text=\"+\"", text);
        Assert.DoesNotContain("Text=\"-\"", text);

        // Оба дерева (GroupPickerTreeItem и ModernTreeItem) рисуют один и тот же
        // векторный шеврон 16 px в колонке 24.
        Assert.Equal(2, Regex.Matches(text, "x:Name=\"ExpanderGlyph\"").Count);
        Assert.Equal(2, Regex.Matches(text, "Data=\"\\{DynamicResource IconChevronRight\\}\"").Count);
        Assert.True(Regex.Matches(text, "<Grid ColumnDefinitions=\"24,\\*\">").Count >= 2,
            "Оба раскрывателя дерева должны использовать колонку 24");
    }
}
