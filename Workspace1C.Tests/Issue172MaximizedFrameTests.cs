using System;
using System.IO;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #172 «Прозрачная рамка вокруг максимизированного окна»: на основном экране с
/// масштабом 125% вокруг развёрнутого окна видна прозрачная рамка, на экране 100% — нет.
/// <para>
/// Причина: геометрия рамки считалась без учёта фактического DPI, а невидимая рамка
/// ресайза (<c>WindowChrome.ResizeBorderThickness = 6</c> DIP) оставалась ненулевой в
/// <c>Maximized</c>. WPF пересчитывает DIP в физические пиксели (7.5 px при 125%,
/// 9 px при 150%), а DWM максимизирует окно на целое число пикселей — расхождение
/// оставляло прозрачную полосу по краям. Дополнительно рабочая область при отсутствии
/// <c>PresentationSource</c> трактовалась как DIP (масштаб 1.0), и на 125% «прижатое»
/// окно ошибочно получало расширенную стеклянную рамку.
/// </para>
/// <para>
/// Проверяется чистая политика <see cref="WindowGlassFramePolicy.ResolveState"/>:
/// в <c>Maximized</c> при 100%/125%/150% рамка полностью обнуляется (толщина, рамка
/// ресайза, скругление), в <c>Normal</c> поведение и скругление issue #126 сохраняются,
/// а смена DPI пересчитывает решение по фактическому масштабу.
/// </para>
/// </summary>
public sealed class Issue172MaximizedFrameTests
{
    private static readonly (double Left, double Top, double Width, double Height) Work =
        (0, 0, 1920, 1040);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    // ---------- Maximized: прозрачной рамки нет при любом DPI ----------

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ResolveState_Maximized_NoGlassFrameAtAnyDpi(double dpiScale)
    {
        // В развёрнутом окне стеклянная рамка должна отсутствовать при 100%/125%/150%.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: true,
            window: (-8, -8, 1936, 1056),
            workArea: Work,
            dpiScale: dpiScale);

        Assert.Equal(0, state.GlassFrameThickness);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ResolveState_Maximized_NoResizeBorderAtAnyDpi(double dpiScale)
    {
        // Невидимая рамка ресайза (6 DIP) в maximized на HiDPI давала прозрачную полосу
        // по краям: WPF пересчитывал её в 7.5/9 px, а окно разворачивалось на целые px.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: true,
            window: (-8, -8, 1936, 1056),
            workArea: Work,
            dpiScale: dpiScale);

        Assert.Equal(0, state.ResizeBorderThickness);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ResolveState_Maximized_NoCornerRoundingAtAnyDpi(double dpiScale)
    {
        // В maximized скругление принудительно обнуляется (прямые углы у краёв экрана).
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: true,
            window: (-8, -8, 1936, 1056),
            workArea: Work,
            dpiScale: dpiScale);

        Assert.Equal(0d, state.CornerRadiusOverride);
    }

    [Fact]
    public void ResolveState_MaximizedWithUnknownWorkArea_StillZero()
    {
        // Развёрнутое окно обрабатывается до проверки рабочей области.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: true,
            window: (double.NaN, double.NaN, double.NaN, double.NaN),
            workArea: (0, 0, 0, 0),
            dpiScale: 1.25);

        Assert.Equal(0, state.GlassFrameThickness);
        Assert.Equal(0, state.ResizeBorderThickness);
        Assert.Equal(0d, state.CornerRadiusOverride);
    }

    // ---------- Normal: поведение и скругление issue #126 сохраняются ----------

    [Fact]
    public void ResolveState_NormalWithGap_KeepsExtendedFramePer126()
    {
        // Окно не прижато к границам рабочей области — acrylic сохраняется (#126/#170),
        // толщина ненулевая, рамка ресайза прежняя, скругление не навязывается.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: false,
            window: (100, 100, 1200, 700),
            workArea: Work,
            dpiScale: 1.0);

        Assert.Equal(WindowGlassFramePolicy.ExtendedThickness, state.GlassFrameThickness);
        Assert.NotEqual(0d, state.GlassFrameThickness);
        Assert.Equal(WindowGlassFramePolicy.ResizeBorderThicknessDip, state.ResizeBorderThickness);
        Assert.Null(state.CornerRadiusOverride);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ResolveState_NormalFlushToBottom_ZeroFrameAtAnyDpi(double dpiScale)
    {
        // Правило #170 (окно занимает всю высоту рабочей области, низ прижат к её нижней
        // границе → 0) сохраняется на HiDPI. Прижатие только к верхней границе рамку
        // не обнуляет — см. регрессию #170 в Issue170ListArtifactsTests.
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: false,
            window: (100, 340, 1200, 700),
            workArea: Work,
            dpiScale: dpiScale);

        Assert.Equal(0, state.GlassFrameThickness);
        Assert.Equal(WindowGlassFramePolicy.ResizeBorderThicknessDip, state.ResizeBorderThickness);
        Assert.Null(state.CornerRadiusOverride);
    }

    [Fact]
    public void ResolveState_NormalFlushToBottom_ZeroFrame()
    {
        var state = WindowGlassFramePolicy.ResolveState(
            maximized: false,
            window: (100, 340, 1200, 700),
            workArea: Work,
            dpiScale: 1.25);

        Assert.Equal(0, state.GlassFrameThickness);
    }

    // ---------- Пересчёт при смене DPI ----------

    [Fact]
    public void ResolveState_MaximizedRecomputedAtEachDpi_StaysZero()
    {
        // Пересчёт при смене DPI (перенос между мониторами) не должен возвращать рамку:
        // решение в maximized одинаково нулевое на 100%, 125% и 150%.
        var at100 = WindowGlassFramePolicy.ResolveState(true, (-8, -8, 1936, 1056), Work, 1.0);
        var at125 = WindowGlassFramePolicy.ResolveState(true, (-8, -8, 1936, 1056), Work, 1.25);
        var at150 = WindowGlassFramePolicy.ResolveState(true, (-8, -8, 1936, 1056), Work, 1.5);

        Assert.Equal(at100, at125);
        Assert.Equal(at125, at150);
        Assert.Equal(0, at150.GlassFrameThickness);
    }

    [Fact]
    public void ResolveState_ToleranceUsesActualDpi()
    {
        // Допуск «прижатия» учитывает точность DIP: 0.5 физического пикселя = 0.5/dpi DIP.
        // Зазор 1.45 DIP при 100% (допуск 1.5) считается прижатием, при 125% (1.4) — уже нет.
        var window = (Left: 100d, Top: 100d, Width: 1200d, Height: 938.55d);
        var work = (Left: 0d, Top: 0d, Width: 1920d, Height: 1040d);

        var at100 = WindowGlassFramePolicy.ResolveState(false, window, work, 1.0);
        var at125 = WindowGlassFramePolicy.ResolveState(false, window, work, 1.25);

        Assert.Equal(0, at100.GlassFrameThickness);
        Assert.Equal(WindowGlassFramePolicy.ExtendedThickness, at125.GlassFrameThickness);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResolveState_InvalidDpi_FallsBackTo100(double dpiScale)
    {
        // Неизвестный DPI (окно ещё не показано) трактуется как 1.0 и не ломает расчёт.
        var baseline = WindowGlassFramePolicy.ResolveState(
            maximized: false, window: (100, 100, 1200, 700), workArea: Work, dpiScale: 1.0);
        var actual = WindowGlassFramePolicy.ResolveState(
            maximized: false, window: (100, 100, 1200, 700), workArea: Work, dpiScale: dpiScale);

        Assert.Equal(baseline, actual);
    }

    // ---------- Гварды правок: код использует DPI и обнуляет рамку в maximized ----------

    [Fact]
    public void MainWindow_UsesDpiAwareFrameStateAndRecomputesOnDpiChange()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("WindowGlassFramePolicy.ResolveState", code, StringComparison.Ordinal);
        Assert.Contains("GetWindowDpiScale()", code, StringComparison.Ordinal);
        Assert.Contains("DpiChanged += (_, _) => UpdateGlassFrameForMaximize()", code, StringComparison.Ordinal);
        Assert.Contains("chrome.ResizeBorderThickness", code, StringComparison.Ordinal);
        Assert.Contains("state.CornerRadiusOverride", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChromeHelper_UsesDpiAwareFrameStateAndRecomputesOnDpiChange()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs"));

        Assert.Contains("WindowGlassFramePolicy.ResolveState", code, StringComparison.Ordinal);
        Assert.Contains("window.DpiChanged += (_, _) => UpdateGlassFrameForMaximize(window)",
            code, StringComparison.Ordinal);
        Assert.Contains("chrome.ResizeBorderThickness", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkAreaLimiter_ConvertsPhysicalPixelsWithActualDpi()
    {
        // Причина HiDPI-артефакта: рабочая область (физические пиксели) переводилась в
        // DIP с масштабом 1.0, когда PresentationSource ещё не готов. Теперь масштаб
        // берётся из VisualTreeHelper.GetDpi.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowWorkAreaLimiter.cs"));

        Assert.Contains("VisualTreeHelper.GetDpi(window)", code, StringComparison.Ordinal);
        Assert.Contains("GetDpiScale(window)", code, StringComparison.Ordinal);
    }
}
