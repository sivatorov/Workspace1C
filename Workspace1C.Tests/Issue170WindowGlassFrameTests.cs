using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты политики толщины стеклянной рамки окна (дефект «пустое поле внизу программы»,
/// 1.0.1.122). Проверяется, что окно, занимающее всю рабочую область (в том числе
/// вручную растянутое, не развёрнутое), получает <c>GlassFrameThickness = 0</c> и не
/// оставляет прозрачную полосу снизу, а развёрнутое окно сохраняет прежнее поведение.
/// Крайние случаи (нулевые/отрицательные размеры, NaN/бесконечность, отсутствие
/// рабочей области) не должны приводить к исключениям.
/// </summary>
public sealed class Issue170WindowGlassFrameTests
{
    private static readonly (double Left, double Top, double Width, double Height) Work =
        (0, 0, 1920, 1040);

    [Fact]
    public void Resolve_WindowFillsWholeWorkArea_ReturnsZero()
    {
        // Окно вручную растянуто на всю рабочую область (не maximized) — дефект:
        // расширенная рамка (-1) оставляла прозрачную полосу ~44 px снизу.
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 1920, 1040),
            workArea: Work);

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_MaximizedWindow_KeepsPreviousBehavior()
    {
        // Прежнее поведение для развёрнутого окна — 0 (обходное решение, иначе
        // расширенная рамка перекрывает панель задач Windows 11).
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: true,
            window: (-8, -8, 1936, 1056),
            workArea: Work);

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_WindowFlushToTopEdge_ReturnsZero()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 0, 1200, 700),
            workArea: Work);

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_WindowFlushToBottomEdge_ReturnsZero()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 340, 1200, 700),
            workArea: Work);

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_WindowWithinToleranceOfBottomEdge_ReturnsZero()
    {
        // Пользователь подогнал окно вручную: зазор меньше допуска (1 DIP).
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 340.5, 1200, 699.5),
            workArea: Work);

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_WindowWithBottomGap_ReturnsExtended()
    {
        // Окно не прижато к границам рабочей области — acrylic сохраняется.
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 100, 1200, 700),
            workArea: Work);

        Assert.Equal(-1, thickness);
    }

    [Fact]
    public void Resolve_WindowWithTopGap_ReturnsExtended()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (100, 100, 1200, 900),
            workArea: Work);

        Assert.Equal(-1, thickness);
    }

    [Fact]
    public void Resolve_WorkAreaOffsetByTaskbarOnTop_StillDetectsFlush()
    {
        // Панель задач сверху: рабочая область начинается с Top = 40.
        var workArea = (Left: 0d, Top: 40d, Width: 1920d, Height: 1000d);

        var flushTop = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 40, 1920, 1000),
            workArea: workArea);
        var gap = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 200, 1200, 500),
            workArea: workArea);

        Assert.Equal(0, flushTop);
        Assert.Equal(-1, gap);
    }

    [Fact]
    public void Resolve_ZeroSizedWorkArea_DoesNotThrow_ReturnsExtended()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 800, 600),
            workArea: (0, 0, 0, 0));

        Assert.Equal(-1, thickness);
    }

    [Fact]
    public void Resolve_ZeroSizedWindow_DoesNotThrow_ReturnsExtended()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 0, 0),
            workArea: Work);

        Assert.Equal(-1, thickness);
    }

    [Fact]
    public void Resolve_NegativeSizes_DoNotThrow_ReturnExtended()
    {
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, -800, -600),
            workArea: (0, 0, -1920, -1040));

        Assert.Equal(-1, thickness);
    }

    [Fact]
    public void Resolve_NaNAndInfinity_DoNotThrow_ReturnExtended()
    {
        var nan = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (double.NaN, double.NaN, double.NaN, double.NaN),
            workArea: Work);
        var infinity = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 800, 600),
            workArea: (0, 0, double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(-1, nan);
        Assert.Equal(-1, infinity);
    }

    [Fact]
    public void Resolve_MaximizedWithUnknownWorkArea_StillReturnsZero()
    {
        // Развёрнутое окно обрабатывается до проверки рабочей области.
        var thickness = WindowGlassFramePolicy.Resolve(
            maximized: true,
            window: (double.NaN, double.NaN, double.NaN, double.NaN),
            workArea: (0, 0, 0, 0));

        Assert.Equal(0, thickness);
    }

    [Fact]
    public void Resolve_InvalidTolerance_FallsBackToDefault()
    {
        // Отрицательный/NaN допуск не должен ломать расчёт: берётся DefaultTolerance.
        var negative = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 1920, 1039.5),
            workArea: Work,
            tolerance: -5);
        var nan = WindowGlassFramePolicy.Resolve(
            maximized: false,
            window: (0, 0, 1920, 1039.5),
            workArea: Work,
            tolerance: double.NaN);

        Assert.Equal(0, negative);
        Assert.Equal(0, nan);
    }
}
