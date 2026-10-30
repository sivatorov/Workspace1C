using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #26 (1.0.1.6), п.4 «белая подсветка кнопок неактивного главного окна»:
/// <list type="bullet">
/// <item><see cref="ThemeFeatureMap.InactiveHoverHex"/> в тёмной теме возвращает
/// тёмный slate (#334155) для ВСЕХ стилей, кроме macOS-«светофора» — исключение Glass
/// убрано (полупрозрачно-белая подложка Glass на тёмной шапке и была «белой подсветкой»);</item>
/// <item>в светлой теме — прежние «дневные» подложки (регресс);</item>
/// <item>Avalonia-метрики (<c>WindowButtonThemeMetrics</c>) больше не захардкожены
/// на dark: false — проверяется исходником;</item>
/// <item><c>ApplyWindowButtonStyle</c> вызывается из всех путей смены темы —
/// проверяется исходником;</item>
/// <item>п.3: WPF/Avalonia «Синхронизация настроек» — контролы получают
/// ModernTextBox/ModernComboBox (проверка исходником).</item>
/// </list>
/// </summary>
public sealed class Issue26WindowButtonTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var fullPath = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ---- InactiveHoverHex: тёмная тема ----

    [Theory]
    [InlineData(WindowButtonStyle.Fluent)]
    [InlineData(WindowButtonStyle.Minimal)]
    [InlineData(WindowButtonStyle.Classic)]
    [InlineData(WindowButtonStyle.Material)]
    [InlineData(WindowButtonStyle.WindowsXp)]
    [InlineData(WindowButtonStyle.Glass)]
    public void InactiveHoverHex_DarkTheme_ReturnsDarkSlate(WindowButtonStyle style)
    {
        Assert.Equal("#334155", ThemeFeatureMap.InactiveHoverHex(style, dark: true));
    }

    [Fact]
    public void InactiveHoverHex_DarkTheme_MacOSKeepsTrafficLight()
    {
        // «Светофор» macOS — цветные круги, не «белая подсветка»: не трогаем.
        Assert.Equal("#FF5F57", ThemeFeatureMap.InactiveHoverHex(WindowButtonStyle.MacOS, dark: true));
    }

    // ---- InactiveHoverHex: светлая тема (регресс) ----

    [Theory]
    [InlineData(WindowButtonStyle.Fluent, "#FFF3CD")]
    [InlineData(WindowButtonStyle.Minimal, "#E2E8F0")]
    [InlineData(WindowButtonStyle.Classic, "#E2E8F0")]
    [InlineData(WindowButtonStyle.Material, "#EDE7F6")]
    [InlineData(WindowButtonStyle.WindowsXp, "#D6E4F0")]
    [InlineData(WindowButtonStyle.Glass, "#40FFFFFF")]
    public void InactiveHoverHex_LightTheme_KeepsDaytimeHex(WindowButtonStyle style, string expected)
    {
        Assert.Equal(expected, ThemeFeatureMap.InactiveHoverHex(style, dark: false));
    }

    // ---- Avalonia-метрики: fallback учитывает фактический вариант темы ----

    [Fact]
    public void AvaloniaMetrics_FallbackUsesActualDarkFlag()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "WindowButtonThemeMetrics.Avalonia.cs"));
        // Захардкоженный dark: false в InactiveHoverHex — источник бага, больше не допускается.
        Assert.DoesNotContain("dark: false", code, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.CurrentTheme == ThemeManager.DarkThemeName", code, StringComparison.Ordinal);
    }

    // ---- ApplyWindowButtonStyle во всех путях смены темы ----

    [Fact]
    public void ApplyTheme_InvokesApplyWindowButtonStyle_BothPlatforms()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.Avalonia.cs"));

        // issue #26 (1.0.1.6): ApplyTheme(bool) — путь Light/Dark-переключения — обязан
        // обновлять ресурсы кнопок окна, иначе после смены темы остаётся подложка прошлой.
        Assert.Contains("ApplyWindowButtonStyle();", wpf, StringComparison.Ordinal);
        Assert.Contains("ApplyWindowButtonStyle();", avalonia, StringComparison.Ordinal);
    }

    // ---- п.3: стилизация контролов «Синхронизация настроек» ----

    [Fact]
    public void SettingsSyncWindow_Wpf_AppliesModernStyles()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsSyncWindow.cs"));

        // Все поля ввода получают тематические стили (issue #26, 7OH: «Интервал
        // автозапуска» и «Файлы для синхронизации» чёрные на тёмном).
        Assert.Contains("\"ModernTextBox\"", code, StringComparison.Ordinal);
        Assert.Contains("\"ModernComboBox\"", code, StringComparison.Ordinal);
        Assert.Contains("\"ModernListBoxItem\"", code, StringComparison.Ordinal);
        Assert.Contains("ApplyModernStyles(grid)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSyncWindow_Avalonia_AppliesModernThemes()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsSyncWindow.Avalonia.cs"));

        Assert.Contains("ControlThemes.ModernTextBox", code, StringComparison.Ordinal);
        Assert.Contains("ControlThemes.ModernComboBox", code, StringComparison.Ordinal);
    }
}
