using System;
using System.IO;
using System.Text.Json;
using Workspace1C.Converters;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #145 «Что случилось с цветом папок» (последние комментарии @7OH):
/// <list type="bullet">
/// <item>две настройки долей цвета — «Доля цвета папки» и «Доля цвета папки при наведении»
/// (0–100 %, по умолчанию 30/45); 0 % — без подмешивания, 100 % — чистый цвет группы;</item>
/// <item>цветная вертикальная полоса строки-папки имеет одинаковый отступ слева и справа
/// от иконки свёртки и иконки папки;</item>
/// <item>контраст текста строки группы остаётся ≥ 4.5:1 при любых долях в светлой и тёмной теме.</item>
/// </list>
/// Чистые функции проверяются расчётом, обвязка WPF/Avalonia — source-гвардами
/// (Avalonia собирается только в Linux-конфигурации).
/// </summary>
public sealed class Issue145GroupColorSettingsTests
{
    private const string GroupColor = "#2D6CDF";
    private const string LightSurface = "#F1F5F9";
    private const string DarkSurface = "#0F172A";

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

    // ===================== Настройки: значения по умолчанию и клампинг =====================

    [Fact]
    public void Settings_Defaults_AreThirtyAndFortyFive()
    {
        var settings = new AppSettings();
        Assert.Equal(30, settings.GroupColorTintPercent);
        Assert.Equal(45, settings.GroupColorHoverTintPercent);
        Assert.Equal(ColorScheme.DefaultGroupRowTintPercent, settings.GroupColorTintPercent);
        Assert.Equal(ColorScheme.DefaultGroupRowHoverTintPercent, settings.GroupColorHoverTintPercent);
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public void Settings_Normalize_ClampsToRange(int input, int expected)
        => Assert.Equal(expected, AppSettings.NormalizeGroupColorTintPercent(input));

    [Fact]
    public void Settings_NormalizeForLoad_ClampsInvalidValues()
    {
        var settings = new AppSettings
        {
            GroupColorTintPercent = -5,
            GroupColorHoverTintPercent = 200
        };

        settings.NormalizeForLoad();

        Assert.Equal(0, settings.GroupColorTintPercent);
        Assert.Equal(100, settings.GroupColorHoverTintPercent);
    }

    [Fact]
    public void Settings_LegacyFileWithoutFields_UsesDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"ru\"}")!;
        settings.NormalizeForLoad();

        Assert.Equal(30, settings.GroupColorTintPercent);
        Assert.Equal(45, settings.GroupColorHoverTintPercent);
    }

    [Fact]
    public void Settings_RoundTrip_PreservesPercents()
    {
        var settings = new AppSettings
        {
            GroupColorTintPercent = 0,
            GroupColorHoverTintPercent = 100
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(0, restored.GroupColorTintPercent);
        Assert.Equal(100, restored.GroupColorHoverTintPercent);
    }

    // ===================== Доли цвета: 0 %, 100 %, смесь =====================

    [Theory]
    [InlineData(LightSurface)]
    [InlineData(DarkSurface)]
    public void TintPercent_Zero_ReturnsSurfaceColor(string surface)
    {
        var background = ColorScheme.GroupRowBackgroundHex(GroupColor, surface, 0);
        Assert.Equal(ColorScheme.NormalizeRgbHex(surface), background);
    }

    [Theory]
    [InlineData(LightSurface)]
    [InlineData(DarkSurface)]
    public void TintPercent_Hundred_ReturnsPureGroupColor(string surface)
    {
        var background = ColorScheme.GroupRowBackgroundHex(GroupColor, surface, 1);
        Assert.Equal(ColorScheme.NormalizeRgbHex(GroupColor), background);
    }

    [Fact]
    public void TintPercent_Fifty_IsBlend()
    {
        var background = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 0.5);
        Assert.NotEqual(ColorScheme.NormalizeRgbHex(GroupColor), background);
        Assert.NotEqual(ColorScheme.NormalizeRgbHex(LightSurface), background);
    }

    [Fact]
    public void HoverTint_IsIndependentFromIdle()
    {
        var idle = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 0);
        var hover = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 1);
        Assert.NotEqual(idle, hover);
    }

    // ===================== Контраст текста ≥ 4.5:1 при любых долях =====================

    [Theory]
    [InlineData(LightSurface, "#000000")]
    [InlineData(DarkSurface, "#F1F5F9")]
    public void Text_ContrastAtLeast4_5_ForAllWeights(string surface, string textPrimary)
    {
        foreach (var weight in new[] { 0.0, 0.1, 0.3, 0.45, 0.75, 1.0 })
        {
            var text = ColorScheme.GroupRowTextHex(GroupColor, surface, textPrimary, weight);
            var background = ColorScheme.GroupRowBackgroundHex(GroupColor, surface, weight);
            var contrast = ColorScheme.ContrastRatio(text, background);

            Assert.True(contrast >= 4.5,
                $"Вес {weight}: контраст {text} на {background} = {contrast:F2}:1 — требуется ≥ 4.5:1");
        }
    }

    [Fact]
    public void Text_AtHundredPercent_UsesWcagFallbackWhenThemeTextUnreadable()
    {
        // Насыщенный цвет группы при 100 % может не сочетаться с TextPrimary темы —
        // тогда выбирается чёрный/белый с максимальным контрастом.
        var text = ColorScheme.GroupRowTextHex(GroupColor, LightSurface, LightSurface, 1.0);
        var background = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 1.0);

        Assert.True(ColorScheme.ContrastRatio(text, background) >= 4.5);
        Assert.True(text is "#000000" or "#FFFFFF");
    }

    // ===================== Симметричные отступы полосы =====================

    [Theory]
    [InlineData(26)]   // корневая группа: кнопка свёртки шириной 26
    [InlineData(44)]   // вложенная группа: 26 + 18 (один уровень)
    [InlineData(62)]   // вложенная группа: 26 + 2*18
    public void StripeLayout_LeftAndRightGapsAreEqual(double expanderRight)
    {
        Assert.Equal(GroupRowStripeLayout.Gap, GroupRowStripeLayout.LeftGap(expanderRight));
        Assert.Equal(GroupRowStripeLayout.Gap, GroupRowStripeLayout.RightGap(expanderRight));
        Assert.Equal(GroupRowStripeLayout.ContentOffset,
            GroupRowStripeLayout.ContentLeft(expanderRight) - expanderRight);
        Assert.Equal(GroupRowStripeLayout.StripeWidth + 2 * GroupRowStripeLayout.Gap,
            GroupRowStripeLayout.ContentOffset);
    }

    [Fact]
    public void StripeLayout_WpfAndAvalonia_UseSameConstants()
    {
        var converter = ReadProjectFile("Workspace1C", "Converters", "LevelToThicknessConverter.cs");
        Assert.Contains("GroupRowStripeLayout.Gap", converter, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.ContentOffset", converter, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.VerticalInset", converter, StringComparison.Ordinal);

        var tree = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.Contains("GroupRowStripeLayout.Gap", tree, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.ContentOffset", tree, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.VerticalInset", tree, StringComparison.Ordinal);
    }

    // ===================== UI настроек и локализация =====================

    [Fact]
    public void SettingsWindow_Wpf_HasBothTintControls()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        Assert.Contains("GroupColorTintSlider", xaml, StringComparison.Ordinal);
        Assert.Contains("GroupColorHoverTintSlider", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.GroupColorTint", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.GroupColorHoverTint", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_Avalonia_HasBothTintControls()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.Contains("\"Settings.GroupColorTint\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Settings.GroupColorHoverTint\"", code, StringComparison.Ordinal);
        Assert.Contains("Slider", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasAllTintKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);
        foreach (var key in new[]
                 {
                     "Settings.GroupColorTintGroup",
                     "Settings.GroupColorTint",
                     "Settings.GroupColorTintHint",
                     "Settings.GroupColorHoverTint",
                     "Settings.GroupColorHoverTintHint",
                     "Settings.GroupColorTintPercentFormat"
                 })
        {
            Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
        }
    }

    // ===================== ThemeManager: применение и сброс кэша =====================

    [Theory]
    [InlineData("ThemeManager.cs")]
    [InlineData("ThemeManager.Avalonia.cs")]
    public void ThemeManager_UsesConfigurableWeightsAndInvalidatesCache(string file)
    {
        var manager = ReadProjectFile("Workspace1C", "Themes", file);
        Assert.Contains("ConfigureGroupRowTint", manager, StringComparison.Ordinal);
        Assert.Contains("GroupRowTintWeight", manager, StringComparison.Ordinal);
        Assert.Contains("GroupRowHoverTintWeight", manager, StringComparison.Ordinal);
        Assert.Contains("GroupRowTintChanged", manager, StringComparison.Ordinal);
        Assert.Contains("InvalidateGroupRowBrushCache", manager, StringComparison.Ordinal);
        // Кисть фона больше не использует зашитые константы ColorScheme.*.
        Assert.DoesNotContain("GetGroupRowBrush(\"bg\", groupHex, ColorScheme.GroupRowTintWeight)", manager,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetGroupRowBrush(\"hover\", groupHex, ColorScheme.GroupRowHoverTintWeight)", manager,
            StringComparison.Ordinal);
    }

#if !LINUX
    [Fact]
    public void ThemeManager_ConfigureGroupRowTint_UpdatesWeightsAndClamps()
    {
        var idleBefore = Workspace1C.Themes.ThemeManager.GroupRowTintWeight;
        var hoverBefore = Workspace1C.Themes.ThemeManager.GroupRowHoverTintWeight;
        try
        {
            Workspace1C.Themes.ThemeManager.ConfigureGroupRowTint(-10, 200);
            Assert.Equal(0.0, Workspace1C.Themes.ThemeManager.GroupRowTintWeight);
            Assert.Equal(1.0, Workspace1C.Themes.ThemeManager.GroupRowHoverTintWeight);

            Workspace1C.Themes.ThemeManager.ConfigureGroupRowTint(30, 45);
            Assert.Equal(0.30, Workspace1C.Themes.ThemeManager.GroupRowTintWeight, 3);
            Assert.Equal(0.45, Workspace1C.Themes.ThemeManager.GroupRowHoverTintWeight, 3);
        }
        finally
        {
            Workspace1C.Themes.ThemeManager.ConfigureGroupRowTint(
                (int)Math.Round(idleBefore * 100), (int)Math.Round(hoverBefore * 100));
        }
    }

    [Fact]
    public void ThemeManager_ConfigureGroupRowTint_RaisesChangedEvent()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        Workspace1C.Themes.ThemeManager.GroupRowTintChanged += handler;
        try
        {
            Workspace1C.Themes.ThemeManager.ConfigureGroupRowTint(25, 55);
        }
        finally
        {
            Workspace1C.Themes.ThemeManager.GroupRowTintChanged -= handler;
            Workspace1C.Themes.ThemeManager.ConfigureGroupRowTint(30, 45);
        }

        Assert.True(raised >= 1, "Изменение долей должно уведомлять уже построенные строки.");
    }
#endif
}
