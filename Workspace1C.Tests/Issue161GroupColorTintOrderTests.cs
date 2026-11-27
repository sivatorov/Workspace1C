using System;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #161 «Положение блока „Доля цвета папки“»: в Linux-раскладке блок стоит
/// сверху (сразу виден), а в Windows/WPF был в самом низу — под списком всех цветов.
/// Блок перенесён перед редактором цветов; порядок «тема → доли цвета → цвета»
/// одинаков на обеих платформах. Имена контролов и обработчики не менялись.
/// Разметочные проверки читают исходники (как Issue71SettingsWindowLayoutTests),
/// поведение выбора доли — чистые функции настроек/расчёта фона.
/// </summary>
public sealed class Issue161GroupColorTintOrderTests
{
    private const string GroupColor = "#2D6CDF";
    private const string LightSurface = "#F1F5F9";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }

    private static string ReadProjectFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static int IndexOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"В разметке не найдено: {needle}");
        return index;
    }

    private static int Count(string text, string needle)
        => Regex.Matches(text, Regex.Escape(needle)).Count;

    // ===================== Порядок блоков =====================

    [Fact]
    public void Wpf_TintBlock_BeforeColorsBlock()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");

        // Блок «Доля цвета папки» присутствует ровно один раз — он перемещён, а не скопирован.
        Assert.Equal(1, Count(xaml, "Settings.GroupColorTintGroup"));

        var theme = IndexOf(xaml, "Header=\"{loc:Loc Settings.Theme}\"");
        var tint = IndexOf(xaml, "Settings.GroupColorTintGroup");
        var colors = IndexOf(xaml, "Header=\"{loc:Loc Settings.Colors}\"");

        Assert.True(tint < colors,
            "WPF: блок «Доля цвета папки» должен идти перед редактором цветов (issue #161).");
        Assert.True(theme < tint,
            "WPF: блок «Доля цвета папки» должен идти после блока темы и перед цветами.");
    }

    [Fact]
    public void Avalonia_TintBlock_BeforeColorsBlock()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #175 (1.0.1.128): группы оформлены карточками через SettingsGroup,
        // порядок «тема → доли цвета → цвета» сохранён.
        var theme = IndexOf(code, "LocalizationManager.T(\"Settings.Theme\")");
        var tint = IndexOf(code, "LocalizationManager.T(\"Settings.GroupColorTintGroup\")");
        var colors = IndexOf(code, "LocalizationManager.T(\"Settings.Colors\")");

        Assert.True(tint < colors,
            "Avalonia: блок «Доля цвета папки» должен идти перед редактором цветов (issue #161).");
        Assert.True(theme < tint,
            "Avalonia: порядок должен совпадать с WPF — тема, доли цвета, цвета.");
    }

    [Fact]
    public void WpfAndAvalonia_HaveSameBlockOrder()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        static string Order(int theme, int tint, int colors)
            => theme < tint && tint < colors ? "theme>tint>colors" : "other";

        var wpfOrder = Order(
            xaml.IndexOf("Header=\"{loc:Loc Settings.Theme}\"", StringComparison.Ordinal),
            xaml.IndexOf("Settings.GroupColorTintGroup", StringComparison.Ordinal),
            xaml.IndexOf("Header=\"{loc:Loc Settings.Colors}\"", StringComparison.Ordinal));
        var avaloniaOrder = Order(
            code.IndexOf("LocalizationManager.T(\"Settings.Theme\")", StringComparison.Ordinal),
            code.IndexOf("LocalizationManager.T(\"Settings.GroupColorTintGroup\")", StringComparison.Ordinal),
            code.IndexOf("LocalizationManager.T(\"Settings.Colors\")", StringComparison.Ordinal));

        Assert.Equal("theme>tint>colors", wpfOrder);
        Assert.Equal(wpfOrder, avaloniaOrder);
    }

    // ===================== Работоспособность контролов =====================

    [Fact]
    public void Sliders_StillBound_AndHandlersPresent()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        var wpfCode = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml.cs");

        // Имена контролов и обработчики не менялись при переносе блока.
        Assert.Contains("x:Name=\"GroupColorTintSlider\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupColorHoverTintSlider\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnGroupColorTintChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnGroupColorHoverTintChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private void OnGroupColorTintChanged(", wpfCode, StringComparison.Ordinal);
        Assert.Contains("private void OnGroupColorHoverTintChanged(", wpfCode, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorTintPercent = (int)Math.Round(e.NewValue);",
            wpfCode, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorHoverTintPercent = (int)Math.Round(e.NewValue);",
            wpfCode, StringComparison.Ordinal);
        // Начальные значения и «Отмена» по-прежнему работают.
        Assert.Contains("_suppressGroupTintEvent", wpfCode, StringComparison.Ordinal);
        Assert.Contains("_originalGroupColorTintPercent", wpfCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Sliders_StillApplyValuesAndRestoreOnCancel()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        Assert.Contains("AddTintSlider(", code, StringComparison.Ordinal);
        Assert.Contains("Slider.ValueProperty", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorTintPercent = v;", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorHoverTintPercent = v;", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorTintPercent = _originalGroupColorTintPercent;",
            code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.GroupColorHoverTintPercent = _originalGroupColorHoverTintPercent;",
            code, StringComparison.Ordinal);
    }

    // ===================== Поведение выбора доли цвета =====================

    [Theory]
    [InlineData(-3.0, 0)]
    [InlineData(0.0, 0)]
    [InlineData(29.6, 30)]
    [InlineData(30.4, 30)]
    [InlineData(45.5, 46)]
    [InlineData(100.0, 100)]
    [InlineData(104.9, 100)]
    public void SliderValue_IsRoundedAndClamped(double raw, int expected)
    {
        // Тот же путь, что у обработчиков WPF/Avalonia: округление слайдера и клампинг 0–100.
        var applied = AppSettings.NormalizeGroupColorTintPercent((int)Math.Round(raw));

        Assert.Equal(expected, applied);
        Assert.InRange(applied, 0, 100);
    }

    [Fact]
    public void ShareChange_UpdatesFolderRowBackground()
    {
        // Выбор доли реально влияет на фон строки-папки: 0 % — фон темы, 100 % — чистый цвет.
        var atZero = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 0.0);
        var atHalf = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 0.5);
        var atFull = ColorScheme.GroupRowBackgroundHex(GroupColor, LightSurface, 1.0);

        Assert.Equal(ColorScheme.NormalizeRgbHex(LightSurface), atZero);
        Assert.Equal(ColorScheme.NormalizeRgbHex(GroupColor), atFull);
        Assert.NotEqual(atZero, atHalf);
        Assert.NotEqual(atHalf, atFull);
    }
}
