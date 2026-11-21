using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI P1-2 (v1.0.1.89): строки групп в главном окне больше не заливаются
/// насыщенным цветом группы с белым текстом. Вместо этого:
/// <list type="bullet">
/// <item>фон строки — пастель: цвет группы, смешанный с ContentBackground темы
/// (issue #145: доля повышена с 14 % до 30 %, чтобы папка не выглядела бледной);</item>
/// <item>слева — вертикальная полоса 3 px исходным цветом группы (сохраняет
/// идентичность выбранного пользователем цвета);</item>
/// <item>текст — TextPrimary темы или WCAG-контраст ≥ 4.5:1 к фактическому фону.</item>
/// </list>
/// Чистые функции <see cref="Workspace1C.Models.ColorScheme"/> проверяются расчётом
/// для светлой/тёмной темы и пользовательских цветов; WPF/Avalonia-обвязка —
/// source-гвардами (Avalonia собирается только в Linux-конфигурации).
/// </summary>
public sealed class GroupRowAppearanceTests
{
    /// <summary>Пользовательские цвета групп из задачи: красный, зелёный, синий, жёлтый.</summary>
    private static readonly string[] UserGroupColors =
    {
        "#DC2626", // красный
        "#16A34A", // зелёный
        "#2D6CDF", // синий
        "#FACC15", // жёлтый
        "#8B5CF6", // фиолетовый (цвет «Закреплённые»)
    };

    /// <summary>Поверхности схем: ContentBackground/CardBackground + TextPrimary.</summary>
    private static readonly (string Surface, string TextPrimary)[] Surfaces =
    {
        ("#F1F5F9", "#000000"), // светлая тема: ContentBackground
        ("#FFFFFF", "#000000"), // светлая тема: CardBackground
        ("#0F172A", "#F1F5F9"), // тёмная тема: ContentBackground
        ("#1E293B", "#F1F5F9"), // тёмная тема: CardBackground
        ("#FAFAFA", "#000000"), // Material (светлая)
        ("#121212", "#F1F5F9"), // Material (тёмная)
        ("#DCE9F7", "#000000"), // Windows XP (светлая)
    };

    public static IEnumerable<object[]> GroupColors()
    {
        foreach (var color in UserGroupColors)
            yield return new object[] { color };
    }

    /// <summary>Декартово произведение «цвет группы × поверхность схемы».</summary>
    public static IEnumerable<object[]> GroupColorSurfaceCases()
    {
        foreach (var color in UserGroupColors)
            foreach (var (surface, textPrimary) in Surfaces)
                yield return new object[] { color, surface, textPrimary };
    }

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

    private static string ReadProjectFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static (int R, int G, int B) Rgb(string hex)
    {
        var raw = hex.TrimStart('#');
        return (Convert.ToInt32(raw.Substring(0, 2), 16),
                Convert.ToInt32(raw.Substring(2, 2), 16),
                Convert.ToInt32(raw.Substring(4, 2), 16));
    }

    private static double Distance(string a, string b)
    {
        var (ar, ag, ab) = Rgb(a);
        var (br, bg, bb) = Rgb(b);
        return Math.Sqrt(Math.Pow(ar - br, 2) + Math.Pow(ag - bg, 2) + Math.Pow(ab - bb, 2));
    }

    // ===================== (а) фон — смесь, а не исходный насыщенный цвет =====================

    [Theory]
    [MemberData(nameof(GroupColorSurfaceCases))]
    public void Background_IsBlendWithThemeSurface_NotRawGroupColor(
        string groupHex, string surfaceHex, string textPrimaryHex)
    {
        _ = textPrimaryHex;
        var background = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(groupHex, surfaceHex);
        var normalizedGroup = Workspace1C.Models.ColorScheme.NormalizeRgbHex(groupHex);

        // Это не исходный насыщенный цвет и не чистый фон темы.
        Assert.NotEqual(normalizedGroup, background);
        Assert.NotEqual(Workspace1C.Models.ColorScheme.NormalizeRgbHex(surfaceHex), background);

        // Пастель ближе к фону темы, чем исходный цвет: смесь «разбавлена» фоном.
        Assert.True(Distance(background, surfaceHex) < Distance(normalizedGroup, surfaceHex),
            $"Фон {background} должен быть ближе к фону темы {surfaceHex}, чем цвет группы {normalizedGroup}");

        // Точная смесь от текущего веса (issue #145: 30 % вместо прежних 14 %).
        var weight = Workspace1C.Models.ColorScheme.GroupRowTintWeight;
        var (gr, gg, gb) = Rgb(normalizedGroup);
        var (sr, sg, sb) = Rgb(surfaceHex);
        var (br, bg, bb) = Rgb(background);
        int Mix(int g, int s) => (int)Math.Round(g * weight + s * (1.0 - weight));
        Assert.Equal(Mix(gr, sr), br);
        Assert.Equal(Mix(gg, sg), bg);
        Assert.Equal(Mix(gb, sb), bb);
    }

    [Theory]
    [MemberData(nameof(GroupColors))]
    public void Background_StaysWithinRecommendedPastelRange(string groupHex)
    {
        // Пастель при любом весе из диапазона остаётся ближе к фону, чем исходный
        // цвет (насыщенная заливка не возвращается). Issue #145: рабочий вес — 30 %.
        const string surface = "#F1F5F9";
        foreach (var weight in new[] { 0.12, 0.14, 0.16, 0.30 })
        {
            var background = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(groupHex, surface, weight);
            Assert.True(Distance(background, surface) < Distance(groupHex, surface),
                $"Вес {weight}: фон {background} должен оставаться пастельным");
        }
    }

    [Theory]
    [MemberData(nameof(GroupColors))]
    public void Hover_IsStrongerThanIdle_ButStillNotSolidFill(string groupHex)
    {
        const string surface = "#F1F5F9";
        var idle = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(groupHex, surface);
        var hover = Workspace1C.Models.ColorScheme.GroupRowHoverBackgroundHex(groupHex, surface);

        // Hover заметно отличается от покоя, но остаётся смесью: ближе к фону,
        // чем к исходному насыщенному цвету (не превращается в сплошную заливку).
        Assert.NotEqual(idle, hover);
        Assert.True(Distance(hover, groupHex) < Distance(idle, groupHex),
            "Hover должен быть чуть насыщеннее покоя");
        Assert.True(Distance(hover, surface) < Distance(hover, groupHex),
            "Hover должен оставаться пастельным, а не сплошной заливкой");
    }

    // ===================== (б) контраст текста ≥ 4.5:1 =====================

    [Theory]
    [MemberData(nameof(GroupColorSurfaceCases))]
    public void Text_ContrastAtLeast4_5_ForUserColors(
        string groupHex, string surfaceHex, string textPrimaryHex)
    {
        var text = Workspace1C.Models.ColorScheme.GroupRowTextHex(groupHex, surfaceHex, textPrimaryHex);
        var background = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(groupHex, surfaceHex);
        var contrast = Workspace1C.Models.ColorScheme.ContrastRatio(text, background);

        Assert.True(contrast >= 4.5,
            $"Контраст текста {text} на фоне {background} (цвет {groupHex}, поверхность {surfaceHex}) " +
            $"составляет {contrast:F2}:1 — требуется ≥ 4.5:1");
    }

    [Theory]
    [InlineData("#F1F5F9", "#F1F5F9")] // TextPrimary совпал с фоном — нечитаем
    [InlineData("#0F172A", "#0F172A")]
    [InlineData("#FACC15", "#FACC15")]
    public void Text_FallsBackToBlackOrWhite_WhenThemeTextUnreadable(string textPrimaryHex, string surfaceHex)
    {
        var text = Workspace1C.Models.ColorScheme.GroupRowTextHex("#DC2626", surfaceHex, textPrimaryHex);
        var background = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex("#DC2626", surfaceHex);

        Assert.True(Workspace1C.Models.ColorScheme.ContrastRatio(text, background) >= 4.5);
        Assert.True(text is "#000000" or "#FFFFFF",
            "При нечитаемом TextPrimary используется чёрный/белый WCAG-фолбэк");
    }

    // ===================== (в) полоса 3 px исходным цветом группы =====================

    [Theory]
    [MemberData(nameof(GroupColors))]
    public void Stripe_UsesOriginalGroupColor(string groupHex)
    {
        var stripe = Workspace1C.Models.ColorScheme.GroupRowStripeHex(groupHex);
        Assert.Equal(Workspace1C.Models.ColorScheme.NormalizeRgbHex(groupHex), stripe);
        Assert.Equal(groupHex.ToUpperInvariant(), stripe);
    }

    [Theory]
    [InlineData("#dc2626", "#DC2626")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#E8EAF2FB", "#EAF2FB")] // #AARRGGBB (Glass): альфа отбрасывается
    public void NormalizeRgbHex_HandlesShortAndAlphaForms(string input, string expected)
        => Assert.Equal(expected, Workspace1C.Models.ColorScheme.NormalizeRgbHex(input));

    [Fact]
    public void NormalizeRgbHex_FallsBackForInvalidInput()
        => Assert.Equal("#2D6CDF", Workspace1C.Models.ColorScheme.NormalizeRgbHex("не-цвет"));

    // ===================== Обвязка WPF и Avalonia (source-гварды) =====================

    [Fact]
    public void Wpf_GroupRowTemplate_UsesPastelBackgroundHoverAndStripe()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");

        // Фон строки — пастельная кисть узла, при наведении — мягкий hover.
        Assert.Contains("Value=\"{Binding HeaderBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding HeaderHoverBrush}\"", xaml, StringComparison.Ordinal);
        // Полоса 3 px исходным цветом группы.
        Assert.Contains("Background=\"{Binding HeaderStripeBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=\"group-stripe\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ThemeManager_ComputesGroupRowBrushesFromTheme()
    {
        var manager = ReadProjectFile("Workspace1C", "Themes", "ThemeManager.cs");
        Assert.Contains("GetGroupRowBackgroundBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowHoverBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowTextBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowStripeBrush", manager, StringComparison.Ordinal);
        Assert.Contains("InvalidateGroupRowBrushCache", manager, StringComparison.Ordinal);
        Assert.Contains("ContentBackgroundColor", manager, StringComparison.Ordinal);
        Assert.Contains("TextPrimaryColor", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ThemeManager_ComputesGroupRowBrushesFromTheme()
    {
        var manager = ReadProjectFile("Workspace1C", "Themes", "ThemeManager.Avalonia.cs");
        Assert.Contains("GetGroupRowBackgroundBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowHoverBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowTextBrush", manager, StringComparison.Ordinal);
        Assert.Contains("GetGroupRowStripeBrush", manager, StringComparison.Ordinal);
        Assert.Contains("InvalidateGroupRowBrushCache", manager, StringComparison.Ordinal);
        Assert.Contains("ContentBackgroundColor", manager, StringComparison.Ordinal);
        Assert.Contains("TextPrimaryColor", manager, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GroupNodeViewModel.cs")]
    [InlineData("GroupNodeViewModel.Avalonia.cs")]
    public void ViewModels_UseThemeManagerBrushes_AndRefreshOnThemeChange(string file)
    {
        var vm = ReadProjectFile("Workspace1C", "ViewModels", file);
        Assert.Contains("ThemeManager.GetGroupRowBackgroundBrush", vm, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.GetGroupRowTextBrush", vm, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.GetGroupRowHoverBrush", vm, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.GetGroupRowStripeBrush", vm, StringComparison.Ordinal);
        Assert.Contains("ThemeChanged += OnThemeChangedBrushes", vm, StringComparison.Ordinal);
        // Сплошная адаптированная заливка больше не используется для фона строки.
        Assert.DoesNotContain("HeaderBrush = GroupColorConverter.GetBrush(_color, adaptForDark: true)", vm,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_GroupRow_BindsHoverAndStripe()
    {
        var tree = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.Contains("header.IsPointerOver ? group.HeaderHoverBrush : group.HeaderBrush", tree, StringComparison.Ordinal);
        Assert.Contains("stripe.Bind(Border.BackgroundProperty, new Binding(\"HeaderStripeBrush\")", tree, StringComparison.Ordinal);
        Assert.Contains("Width = 3", tree, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GroupPickerWindow.xaml", "Background=\"{Binding HeaderStripeBrush}\"")]
    [InlineData("GroupPickerWindow.Avalonia.cs", "Background = node.HeaderStripeBrush")]
    public void GroupPicker_KeepsRawGroupColorChip(string file, string expected)
    {
        // Пикер цвета группы не меняется: в списке выбора чип остаётся исходным
        // цветом группы, чтобы выбор цвета был наглядным.
        var text = ReadProjectFile("Workspace1C", "Views", file);
        Assert.Contains(expected, text, StringComparison.Ordinal);
    }
}
