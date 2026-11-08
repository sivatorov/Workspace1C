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
        // Ключи из ControlTheme кнопки Avalonia 11 (FluentTheme) — фиксированный
        // перечень; disabled-вариант не перекрывается (читаем в обеих темах).
        Assert.Contains("app.Resources[\"ButtonForeground\"]", code, StringComparison.Ordinal);
        Assert.Contains("app.Resources[\"ButtonForegroundPointerOver\"]", code, StringComparison.Ordinal);
        Assert.Contains("app.Resources[\"ButtonForegroundPressed\"]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("app.Resources[\"ButtonForegroundDisabled\"]", code, StringComparison.Ordinal);
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
