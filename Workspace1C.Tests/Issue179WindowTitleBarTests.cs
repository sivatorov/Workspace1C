using System;
using System.IO;
using System.Linq;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #179 (1.0.1.128): «Разные рамки заголовка у окон и смещены системные
/// кнопки». У дочерних окон полоса заголовка была ниже, чем у главного окна
/// (34 против 46 DIP), а кнопки «свернуть/развернуть/закрыть» стояли вплотную
/// к правому краю и визуально выше центра — выглядели смещёнными вправо и вверх.
/// <para>
/// Исправление: единый источник метрик <see cref="WindowChromeMetrics"/>
/// (высота полосы = высоте кнопки, правый отступ группы кнопок), который
/// используют и WPF, и Avalonia; высота полосы главного окна приведена к высоте
/// полосы диалогов (вариант из комментария 7OH), кнопки занимают полосу целиком
/// (вертикальное центрирование) и имеют одинаковый правый отступ. Компактный
/// режим (issue #296) сжимает полосу и кнопки согласованно.
/// </para>
/// Тесты — source-scan/метрические, в стиле <c>Issue159EditorSizeTests</c> и
/// <c>Issue146WindowHeightTests</c>: UI-дерево в тестах не поднимается.
/// </summary>
public sealed class Issue179WindowTitleBarTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    // ===================== Метрики =====================

    [Fact]
    public void Metrics_ButtonHeightEqualsTitleBarHeight()
    {
        // Кнопка занимает полосу целиком — нет вертикального смещения.
        Assert.Equal(WindowChromeMetrics.TitleBarHeight, WindowChromeMetrics.WindowButtonHeight);
        Assert.True(WindowChromeMetrics.TitleBarHeight > 0);
    }

    [Fact]
    public void Metrics_RightInsetIsSharedAndPositive()
    {
        // Одинаковый правый отступ кнопок у главного окна и диалогов.
        Assert.True(WindowChromeMetrics.WindowButtonRightInset > 0);
        Assert.True(WindowChromeMetrics.TitleBarLeftInset > 0);
    }

    [Fact]
    public void Metrics_CompactModeShrinksBarAndButtonsTogether()
    {
        // Issue #296: компактный режим не ломает выравнивание — полоса и кнопки
        // уменьшаются согласованно.
        Assert.Equal(WindowChromeMetrics.CompactTitleBarHeight, WindowChromeMetrics.CompactWindowButtonHeight);
        Assert.True(WindowChromeMetrics.CompactTitleBarHeight < WindowChromeMetrics.TitleBarHeight);
        Assert.True(WindowChromeMetrics.CompactTitleBarSideInset < WindowChromeMetrics.TitleBarLeftInset);
    }

    // ===================== WPF =====================

    [Fact]
    public void WpfDialogChrome_UsesSharedTitleBarMetrics()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "WindowChromeHelper.cs");

        // Высота полосы диалога — из общей метрики, без локального 34.
        Assert.Contains("Height = WindowChromeMetrics.TitleBarHeight", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 34", code, StringComparison.Ordinal);
        // Правый отступ группы кнопок и левый отступ заголовка — из метрик.
        Assert.Contains("WindowChromeMetrics.WindowButtonRightInset", code, StringComparison.Ordinal);
        Assert.Contains("WindowChromeMetrics.TitleBarLeftInset", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMainWindowTitleBar_UsesSharedMetrics()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");

        // Полоса главного окна — ровно высота диалоговой (без вертикальных отступов).
        Assert.Contains("{x:Static themes:WindowChromeMetrics.TitleBarHeight}", xaml, StringComparison.Ordinal);
        Assert.Contains("{x:Static themes:WindowChromeMetrics.CompactTitleBarHeight}", xaml, StringComparison.Ordinal);
        Assert.Contains("{x:Static themes:WindowChromeMetrics.CompactWindowButtonHeight}", xaml, StringComparison.Ordinal);
        // Прежние отступы 12,6 делали шапку главного окна выше диалоговой.
        Assert.DoesNotContain("Value=\"12,6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"8,0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindowControlButtonHeight_ComesFromMetrics()
    {
        var appXaml = ReadProjectFile("Workspace1C", "App.xaml");

        // Стиль кнопок окна (используется и главным окном, и диалогами) берёт
        // высоту из общей метрики — WPF и Avalonia не могут разойтись.
        Assert.Contains("{x:Static themes:WindowChromeMetrics.WindowButtonHeight}",
            appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfAllChildWindows_UseGlobalChrome()
    {
        // Дочерние окна не строят свою полосу: оформление навешивается глобально.
        var appCode = ReadProjectFile("Workspace1C", "App.xaml.cs");
        Assert.Contains("WindowChromeHelper.RegisterGlobalWindowStyling()", appCode, StringComparison.Ordinal);

        // Собственная полоса заголовка есть только у главного окна.
        var titleBarOwners = Directory
            .GetFiles(Path.Combine(FindRepoRoot(), "Workspace1C", "Views"), "*.xaml")
            .Where(path => File.ReadAllText(path).Contains("x:Name=\"TitleBarBorder\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(new[] { "MainWindow.xaml" }, titleBarOwners);
    }

    // ===================== Avalonia =====================

    [Fact]
    public void AvaloniaMetrics_DeriveFromSharedMetrics()
    {
        var uiMetrics = ReadProjectFile("Workspace1C", "Controls", "UiMetrics.Avalonia.cs");
        Assert.Contains("Themes.WindowChromeMetrics.TitleBarHeight", uiMetrics, StringComparison.Ordinal);
        Assert.Contains("Themes.WindowChromeMetrics.WindowButtonRightInset", uiMetrics, StringComparison.Ordinal);
        // Вертикальных отступов у полосы нет — иначе шапка выше диалоговой.
        Assert.Contains("TitleBarPadV => 0", uiMetrics, StringComparison.Ordinal);

        var buttonMetrics = ReadProjectFile(
            "Workspace1C", "Themes", "WindowButtonThemeMetrics.Avalonia.cs");
        Assert.Contains("WindowChromeMetrics.WindowButtonHeight", buttonMetrics, StringComparison.Ordinal);
        Assert.DoesNotContain("ButtonHeight = 34", buttonMetrics, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMainTitleBar_UsesSharedMetrics()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");

        // Главное окно Avalonia — та же высота полосы, что у диалогов.
        Assert.Contains("Height = UiMetrics.TitleBarHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDialogChrome_UsesSharedMetrics()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");

        // Полоса диалога — из общей метрики, кнопки — во всю высоту полосы
        // и с правым отступом, как в главном окне.
        Assert.Contains("Height = UiMetrics.TitleBarHeight", code, StringComparison.Ordinal);
        Assert.Contains("UiMetrics.WindowButtonRightInset", code, StringComparison.Ordinal);
        Assert.Contains("Height = UiMetrics.Scaled(WindowChromeMetrics.WindowButtonHeight)",
            code, StringComparison.Ordinal);
        Assert.Contains("WindowChromeMetrics.TitleBarLeftInset", code, StringComparison.Ordinal);
        // Локальная высота кнопки диалога (30) больше не задаётся: высота берётся
        // из общей метрики (строка с точкой с запятой — именно кнопка, не заголовок).
        Assert.DoesNotContain("Height = UiMetrics.Scaled(30);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaAllChildWindows_UseSharedChrome()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        var files = Directory.GetFiles(views, "*.cs", SearchOption.AllDirectories);

        // Полосу диалогов строит только база ModalWindowBase — все дочерние окна
        // (настройки, «Назначить тег», «Переместить в группу» и т. д.) наследуют её.
        var titleStripBuilders = files
            .Where(path => File.ReadAllText(path).Contains("private Control BuildTitleStrip()", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Equal(new[] { "ModalWindowBase.cs" }, titleStripBuilders);

        // Собственную шапку главного окна строит только MainWindow.Avalonia.cs.
        var titleBarBuilders = files
            .Where(path => File.ReadAllText(path).Contains("private Control BuildTitleBar()", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Equal(new[] { "MainWindow.Avalonia.cs" }, titleBarBuilders);

        // Ключевые дочерние окна наследуют общий chrome.
        var settings = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.Contains("SettingsWindow : ModalWindowBase", settings, StringComparison.Ordinal);
    }
}
