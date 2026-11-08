using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты дефолтной темы на Linux (issue #107, комментарий 7OH 2026-11-07: «стартовое
/// окно с темой по умолчанию — куча кнопок с белым текстом при яркой подложке»).
/// Стражи-сканы исходников: Avalonia-код собирается только в Linux-конфигурации,
/// поэтому юнит-тесты — source-scan по образцу <see cref="Issue79ScheduledJobsResilienceTests"/>.
/// <list type="bullet">
/// <item><c>ApplyFullTheme</c> всегда ставит явный <c>RequestedThemeVariant</c>
/// (Light/Dark — никогда не Default из App.axaml, который на тёмной системе давал
/// белый текст Fluent-кнопок на яркой светлой палитре);</item>
/// <item><c>ApplyColors</c> перекрывает кисти содержимого Fluent-кнопок
/// (<c>ButtonForeground*</c>) цветом текста схемы — страховка читаемого текста
/// при любом рассогласовании варианта и палитры;</item>
/// <item>старт: вариант не остаётся Default (страховка в App.axaml.cs) и фактический
/// вариант/тема пишутся в лог старта (диагностика).</item>
/// </list>
/// </summary>
public sealed class Issue107LinuxDefaultThemeTests
{
    [Fact]
    public void ApplyFullTheme_SetsExplicitVariant_NeverDefault()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.Avalonia.cs"));
        // ApplyFullTheme задаёт вариант явно — по фактическому варианту темы.
        Assert.Contains(
            "app.RequestedThemeVariant = CurrentFullTheme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light",
            code, StringComparison.Ordinal);
        // ApplyTheme тоже ставит вариант явно (страж против регрессии).
        Assert.Contains(
            "app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyColors_OverridesFluentButtonForegroundKeys()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.Avalonia.cs"));
        // v1.0.1.45 (issue #107, «Результата не замечено»): перекрывается ПОЛНЫЙ набор
        // кнопочных контролов Fluent по их ControlTheme в Avalonia 11.3
        // (FluentControlResources.xaml): Button, ToggleButton, RepeatButton.
        // Ключи задаются через SetFluentBrushes (Application.Resources — они выше
        // словарей FluentTheme по порядку поиска ресурсов).
        Assert.Contains("SetFluentBrushes(app,", code, StringComparison.Ordinal);
        Assert.Contains("\"ButtonForeground\",", code, StringComparison.Ordinal);
        Assert.Contains("\"ButtonForegroundPointerOver\",", code, StringComparison.Ordinal);
        Assert.Contains("\"ButtonForegroundPressed\",", code, StringComparison.Ordinal);
        // v1.0.1.44 покрывал только Button — Toggle-/Repeat-кнопки оставались белыми.
        Assert.Contains("\"ToggleButtonForeground\",", code, StringComparison.Ordinal);
        Assert.Contains("\"ToggleButtonForegroundPointerOver\",", code, StringComparison.Ordinal);
        Assert.Contains("\"ToggleButtonForegroundPressed\",", code, StringComparison.Ordinal);
        Assert.Contains("\"RepeatButtonForeground\",", code, StringComparison.Ordinal);
        Assert.Contains("\"RepeatButtonForegroundPointerOver\",", code, StringComparison.Ordinal);
        Assert.Contains("\"RepeatButtonForegroundPressed\")", code, StringComparison.Ordinal);
        // Акцентные кнопки Fluent: фиксированный белый Fluent заменяется контрастом
        // по WCAG к фактическому акценту (как TextOnAccentBrush).
        Assert.Contains("\"AccentButtonForeground\",", code, StringComparison.Ordinal);
        Assert.Contains("ColorScheme.ContrastTextHex(accentHex)", code, StringComparison.Ordinal);
        // Disabled-варианты не перекрываются (приглушённый Fluent читаем в обеих темах).
        Assert.DoesNotContain("ButtonForegroundDisabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleButtonForegroundDisabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RepeatButtonForegroundDisabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentButtonForegroundDisabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_NeverLeavesDefaultVariant_AndLogsActualVariant()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "App.axaml.cs"));
        // Страховка: после ApplyScheme вариант не остаётся Default (дефолт — светлая тема).
        Assert.Contains("RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Default", code, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.ApplyTheme(dark: false)", code, StringComparison.Ordinal);
        // Диагностика в лог старта: фактический вариант и активная тема.
        Assert.Contains("variant={Application.Current.RequestedThemeVariant}", code, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
