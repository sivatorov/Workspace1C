using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #177 «Серые пункты подменю»: если программа запущена в тёмной теме и после
/// запуска сменить тему, пункты подменю, вынесенные на верхний уровень и показывающие
/// надпись, становятся серыми (выглядят недоступными, хотя доступны).
/// <para>
/// Причина — подпись такой кнопки получала разовую (статическую) кисть
/// <c>TextSecondaryBrush</c> на момент построения. В тёмной теме это светлый
/// <c>#CBD5E1</c>; после переключения на светлую кисть не переприменялась, и подпись
/// оставалась светлой на светлом фоне. Исправление: подпись привязана к основному
/// цвету текста активной темы (<c>TextPrimaryBrush</c>, DynamicResource) и кнопки
/// разделов пересобираются при смене темы. Реально недоступные пункты по-прежнему
/// гасятся триггером темы (Opacity), а не подменой цвета.
/// </para>
/// Тесты: стражи исходников обеих платформ + проверка цветовой семантики на палитрах тем.
/// </summary>
public sealed class Issue177MenuThemeTests
{
    private const string PrimaryBrushKey = "TextPrimaryBrush";
    private const string PrimaryColorKey = "TextPrimaryColor";
    private const string SecondaryColorKey = "TextSecondaryColor";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ======================= WPF =======================

    [Fact]
    public void Wpf_SectionLabelButton_UsesDynamicPrimaryForeground()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));

        // Активная подпись вынесенного пункта следует теме через DynamicResource.
        Assert.Contains("label.SetResourceReference(TextBlock.ForegroundProperty, \"TextPrimaryBrush\")", code);
        // Регресс исходного дефекта: разовая кисть вторичного (серого) цвета не должна
        // возвращаться в построитель подписи.
        Assert.DoesNotContain("Foreground = TryFindResource(\"TextSecondaryBrush\") as Brush", code);
    }

    [Fact]
    public void Wpf_ThemeChanged_RebuildsSectionButtons()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));

        // Подписи и значки кнопок разделов строятся кодом с кистями активной темы,
        // поэтому при смене темы их нужно пересобрать (иначе цвета прежней темы).
        var index = code.IndexOf("private void OnThemeChanged", StringComparison.Ordinal);
        Assert.True(index > 0, "Обработчик OnThemeChanged не найден");
        var region = code.Substring(index, Math.Min(1500, code.Length - index));
        Assert.Contains("RebuildSectionButtons()", region);
    }

    // ======================= Avalonia =======================

    [Fact]
    public void Avalonia_SectionLabelButton_UsesDynamicPrimaryForeground()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        // Паритет с WPF: подпись привязана к основному цвету текста темы, а не
        // наследует/фиксирует цвет на момент построения.
        Assert.Contains("ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, \"TextPrimaryBrush\")", code);
    }

    // ======================= Цветовая семантика =======================

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ActiveSectionLabelColor_MatchesThemePrimary_AndDiffersFromDisabled(string themeId)
    {
        var theme = ThemeCatalog.Find(themeId);
        Assert.NotNull(theme);

        var active = theme!.Scheme.PaletteValue(theme.IsDark, PrimaryColorKey);
        var disabled = theme.Scheme.PaletteValue(theme.IsDark, SecondaryColorKey);

        // Активный пункт красится основным цветом текста темы...
        Assert.False(string.IsNullOrWhiteSpace(active));
        // ...и этот цвет отличим от цвета «недоступного» (вторичного) текста.
        Assert.NotEqual(active, disabled);
    }

    [Fact]
    public void AfterThemeSwitch_ActiveColorFollowsTheme_NotStaleDisabledColor()
    {
        var light = ThemeCatalog.Find("light");
        var dark = ThemeCatalog.Find("dark");
        Assert.NotNull(light);
        Assert.NotNull(dark);

        var lightActive = light!.Scheme.PaletteValue(false, PrimaryColorKey);
        var darkActive = dark!.Scheme.PaletteValue(true, PrimaryColorKey);
        var staleDarkDisabled = dark.Scheme.PaletteValue(true, SecondaryColorKey);

        // После смены тёмной темы на светлую активная подпись обязана взять светлый
        // основной цвет — он отличается и от прежнего (тёмного) основного, и от
        // вторичного цвета тёмной темы, из-за которого подпись выглядела серой.
        Assert.NotEqual(darkActive, lightActive);
        Assert.NotEqual(staleDarkDisabled, lightActive);
    }
}
