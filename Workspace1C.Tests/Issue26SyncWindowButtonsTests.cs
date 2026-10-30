using System;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #26 «Исправить отображение сохраненных акцентных цветов и контрастность
/// текста в тёмной теме» — последний комментарий 7OH от 2026-10-29 19:44:15:
///  (3) «Синхронизация настроек - без изменений - надписи в наличии черные на тёмном»;
///  (4) «Подсветка кнопок управления основным окном при неактивности - так - белая подсветка».
/// Стражи против возврата дефектов 1.0.1.0/1.0.1.3 (Avalonia-часть пункта 3 уже была
/// исправлена, здесь добита WPF-версия окна «Синхронизация настроек» и неактивное
/// состояние кнопок управления окном на обеих платформах).
/// По образцу Issue26ContrastToggleZebraGuardTests / Issue60SearchIconColorTests.
/// </summary>
public sealed class Issue26SyncWindowButtonsTests
{
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

    private static string ReadProjectFileIfExists(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
        return code;
    }

    // ===================== (3) Надписи окна «Синхронизация настроек» =====================

    [Fact]
    public void SettingsSyncWindow_Wpf_LabelsUseThemeBrushes()
    {
        // WPF-версия окна «Синхронизация настроек» после 1.0.1.0 оставалась без
        // привязок к теме: MakeLabel не биндил Foreground (чёрные надписи в тёмной
        // теме), значение «последняя синхронизация» тоже. Теперь — как Avalonia.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsSyncWindow.cs"));

        // Подписи полей привязаны к единой кисти подписи формы (контраст в обеих темах).
        Assert.Contains("label.SetResourceReference(ForegroundProperty, \"FormLabelBrush\")", code, StringComparison.Ordinal);
        // Значение «последняя синхронизация» — вторичная кисть (не чёрная).
        Assert.Contains("_lastSyncText.SetResourceReference(ForegroundProperty, \"TextSecondaryBrush\")", code, StringComparison.Ordinal);
        // В окне больше нет «бесхозных» подписей, создаваемых без кисти темы.
        Assert.Contains("FormLabelBrush", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSyncWindow_Avalonia_StillBoundToThemeBrushes()
    {
        // Регрессионный гард на уже сделанное в 1.0.1.0 (Linux): подписи и значения
        // окна не должны вернуться к дефолтному чёрному.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsSyncWindow.Avalonia.cs"));
        Assert.Contains("\"FormLabelBrush\"", code, StringComparison.Ordinal);
        Assert.Contains("\"TextSecondaryColorBrush\"", code, StringComparison.Ordinal);
    }

    // ===================== (4) Кнопки управления окном при неактивности =====================

    [Theory]
    [InlineData(WindowButtonStyle.Fluent)]
    [InlineData(WindowButtonStyle.Minimal)]
    [InlineData(WindowButtonStyle.Classic)]
    [InlineData(WindowButtonStyle.Material)]
    [InlineData(WindowButtonStyle.WindowsXp)]
    public void InactiveHover_DarkTheme_IsDarkNotWhite(WindowButtonStyle style)
    {
        // «Подсветка кнопок управления основным окном при неактивности - белая»:
        // подложка неактивного окна в тёмной теме — приглушённый тёмный slate,
        // а не полупрозрачный белый #33FFFFFF (который на тёмной шапке карточки
        // по-прежнему выглядел «белой подсветкой»).
        var hover = ThemeFeatureMap.InactiveHoverHex(style, dark: true);
        Assert.Equal("#334155", hover);
        Assert.True(ColorScheme.RelativeLuminance(hover) < 0.35,
            $"Подложка неактивных кнопок {hover} в тёмной теме слишком светлая (L={ColorScheme.RelativeLuminance(hover):F2})");

        // Светлее фона карточки — подложка наведения остаётся различимой на шапке.
        Assert.True(ColorScheme.RelativeLuminance(hover) > ColorScheme.RelativeLuminance("#1E293B"),
            "Подложка неактивных кнопок должна быть заметна на фоне шапки карточки");
    }

    [Theory]
    [InlineData(WindowButtonStyle.Fluent)]
    [InlineData(WindowButtonStyle.Minimal)]
    [InlineData(WindowButtonStyle.Classic)]
    [InlineData(WindowButtonStyle.Material)]
    [InlineData(WindowButtonStyle.WindowsXp)]
    public void InactiveHover_LightTheme_KeepsStyleHover(WindowButtonStyle style)
    {
        // В светлой теме неактивная подложка сохраняет прежние «дневные» цвета стиля.
        Assert.Equal(ThemeFeatureMap.ButtonMetrics(style, dark: false).HoverHex,
            ThemeFeatureMap.InactiveHoverHex(style, dark: false));
    }

    [Fact]
    public void InactiveHover_MacOSAndGlass_DoNotChange()
    {
        // «Светофор» macOS и полупрозрачный Glass сохраняют собственные подложки.
        Assert.Equal("#FF5F57", ThemeFeatureMap.InactiveHoverHex(WindowButtonStyle.MacOS, dark: true));
        Assert.Equal("#40FFFFFF", ThemeFeatureMap.InactiveHoverHex(WindowButtonStyle.Glass, dark: true));
    }

    [Fact]
    public void WpfAppXaml_InactiveStyle_UsesThemedHoverNotWhite()
    {
        // Базовый стиль WindowControlButton (неактивное главное окно) обязан брать
        // приглушённую кисть WindowButtonInactiveHoverBrush, а значок под курсором —
        // вторичный TextSecondaryBrush (без «белой подсветки с белыми элементами»).
        var app = ReadProjectFile(Path.Combine("Workspace1C", "App.xaml"));

        var baseStart = app.IndexOf("x:Key=\"WindowControlButton\"", StringComparison.Ordinal);
        var onAccentStart = app.IndexOf("x:Key=\"WindowControlButtonOnAccent\"", StringComparison.Ordinal);
        Assert.True(baseStart >= 0 && onAccentStart > baseStart, "Стили кнопок окна не найдены в App.xaml.");
        var baseRegion = app.Substring(baseStart, onAccentStart - baseStart);

        Assert.Contains("WindowButtonInactiveHoverBrush", baseRegion, StringComparison.Ordinal);
        // В неактивном стиле нет белой подложки WindowButtonHoverBrush (#33FFFFFF в тёмной).
        Assert.DoesNotContain("WindowButtonHoverBrush", baseRegion, StringComparison.Ordinal);
        // Значок под курсором — вторичный (не TextPrimaryBrush ≈ белый в тёмной теме).
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TextSecondaryBrush}\"/>",
            baseRegion, StringComparison.Ordinal);

        // Акцентная шапка (активное окно) сохраняет полупрозрачную белую подложку —
        // на акценте она читается, жалоб не было.
        Assert.Contains("#33FFFFFF", app, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeManagers_ExposeInactiveHoverBrush()
    {
        // Обе платформы выставляют WindowButtonInactiveHoverBrush при применении темы.
        foreach (var path in new[]
        {
            Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"),
            Path.Combine("Workspace1C", "Themes", "ThemeManager.Avalonia.cs")
        })
        {
            var code = ReadProjectFile(path);
            Assert.Contains("WindowButtonInactiveHoverBrushKey", code, StringComparison.Ordinal);
            Assert.Contains("InactiveHoverHex", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowButtonThemeMetrics_Avalonia_ReadsInactiveHover()
    {
        // Метрики Avalonia-кнопок окна читают неактивную подложку из ресурсов темы.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "WindowButtonThemeMetrics.Avalonia.cs"));
        Assert.Contains("InactiveHover", code, StringComparison.Ordinal);
        Assert.Contains("WindowButtonInactiveHoverBrushKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowAvalonia_ControlButtons_UseInactiveHover()
    {
        // Avalonia-кнопки главного окна при неактивном окне берут тёмную подложку
        // и не перекрашивают глиф в основной/белый цвет под курсором.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Controls.cs"));
        Assert.Contains("_inactiveHoverBg = metrics.InactiveHover", code, StringComparison.Ordinal);
        Assert.Contains("var hoverBg = _onAccent ? _hoverBg : _inactiveHoverBg", code, StringComparison.Ordinal);
        // Неактивная ветка глифа: hover-кисть равна вторичной (не TextPrimary ≈ белый).
        Assert.Contains("var hoverBrush = _onAccent ? _accentGlyphBrush : _restGlyphBrush", code, StringComparison.Ordinal);
    }
}