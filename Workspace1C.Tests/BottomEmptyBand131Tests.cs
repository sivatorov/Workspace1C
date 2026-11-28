using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Дефект «серая область внизу программы» (1.0.1.131).
/// <para>
/// Симптом: окно пристёгнуто к правой половине экрана на всю высоту рабочей области
/// (состояние <c>Normal</c>, не <c>Maximized</c>); под строкой состояния остаётся
/// незакрашенная светло-серая полоса высотой ровно 48 DIP.
/// </para>
/// <para>
/// Причина: и WPF (<see cref="Workspace1C.WindowWorkAreaLimiter"/>), и Avalonia
/// (<c>ModalWindowBase.ClampWindowToWorkArea</c>) назначали окну
/// <c>MaxWidth</c>/<c>MaxHeight</c> по рабочей области <b>минус отступ</b>
/// (<see cref="WindowBoundsPolicy.DefaultMargin"/> = 48 DIP). Оконный менеджер при
/// пристёгивании делает HWND выше предела (1392 px при рабочей области 1392 px), а
/// WPF применяет <c>MaxHeight</c> к разметке — контент заканчивается на 1344 px, и
/// оставшиеся 48 px клиентской области не закрашиваются.
/// </para>
/// <para>
/// Исправление: главному окну отступ не назначается (рабочая область целиком), а для
/// уже показанного окна предел поднимается до фактического размера
/// (<see cref="WindowBoundsPolicy.EffectiveMax"/>) — при изменении размера границы
/// пересчитываются повторно.
/// </para>
/// </summary>
public sealed class BottomEmptyBand131Tests
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

    // ---------- Чистая политика: EffectiveMax ----------

    [Fact]
    public void EffectiveMax_BeforeShow_ReturnsRecommended()
    {
        // До показа окна размер ещё не задан оконным менеджером — действует
        // рекомендация (защита issue #119 от «гигантских» окон).
        Assert.Equal(1344, WindowBoundsPolicy.EffectiveMax(1344, 1392, windowShown: false));
    }

    [Fact]
    public void EffectiveMax_ShownAndFits_ReturnsRecommended()
    {
        Assert.Equal(1344, WindowBoundsPolicy.EffectiveMax(1344, 1000, windowShown: true));
    }

    [Fact]
    public void EffectiveMax_ShownAndLarger_RaisesToActual()
    {
        // Главный сценарий дефекта: окно пристёгнуто на всю высоту рабочей области
        // (1392), а предел — 1392 − 48 = 1344. Предел поднимается до 1392, иначе
        // WPF оставит снизу незакрашенные 48 px.
        Assert.Equal(1392, WindowBoundsPolicy.EffectiveMax(1344, 1392, windowShown: true));
    }

    [Fact]
    public void EffectiveMax_EqualSizes_ReturnsRecommended()
    {
        Assert.Equal(1392, WindowBoundsPolicy.EffectiveMax(1392, 1392, windowShown: true));
    }

    [Fact]
    public void EffectiveMax_InfiniteRecommended_StaysInfinite()
    {
        // Рабочая область неизвестна — ограничения нет.
        Assert.True(double.IsPositiveInfinity(
            WindowBoundsPolicy.EffectiveMax(double.PositiveInfinity, 1392, windowShown: true)));
    }

    [Fact]
    public void EffectiveMax_NaNActual_ReturnsRecommended()
    {
        Assert.Equal(1344, WindowBoundsPolicy.EffectiveMax(1344, double.NaN, windowShown: true));
    }

    // ---------- Политика размеров: отступ для главного окна ----------

    [Fact]
    public void Recommend_DefaultMargin_ReproducesBandHeight()
    {
        // Воспроизведение дефекта: рабочая область 1392, стартовый размер 1392.
        // С отступом 48 предел равен 1344 — ровно высота серой полосы на скриншоте.
        var withMargin = WindowBoundsPolicy.Recommend(
            designWidth: 1720,
            designHeight: 1392,
            minWidth: 900,
            minHeight: 600,
            workWidth: 3440,
            workHeight: 1392);

        Assert.Equal(1344, withMargin.MaxHeight);
        Assert.Equal(WindowBoundsPolicy.DefaultMargin, 1392 - withMargin.MaxHeight);
    }

    [Fact]
    public void Recommend_ZeroMargin_MainWindowFillsWorkArea()
    {
        // Главное окно: отступ 0 — предел равен рабочей области, пристёгивание
        // на всю высоту/ширину не оставляет полосы.
        var noMargin = WindowBoundsPolicy.Recommend(
            designWidth: 1720,
            designHeight: 1392,
            minWidth: 900,
            minHeight: 600,
            workWidth: 3440,
            workHeight: 1392,
            margin: 0);

        Assert.Equal(3440, noMargin.MaxWidth);
        Assert.Equal(1392, noMargin.MaxHeight);
        Assert.True(noMargin.Height <= noMargin.MaxHeight);
    }

    // ---------- Стражи исходников ----------

    [Fact]
    public void Wpf_Limiter_NoMarginForMainWindow()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        Assert.Contains("window is MainWindow ? 0 : WindowBoundsPolicy.DefaultMargin", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Limiter_RaisesLimitToActualSize()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        Assert.Contains("WindowBoundsPolicy.EffectiveMax(", code, StringComparison.Ordinal);
        Assert.Contains("window.IsLoaded", code, StringComparison.Ordinal);
        Assert.Contains("window.ActualHeight", code, StringComparison.Ordinal);
        Assert.Contains("window.ActualWidth", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Limiter_RefreshesConstraintsOnSizeChanged()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        // Пристёгивание Win+стрелками меняет размер уже после Loaded — границы
        // должны пересчитываться, иначе предел остаётся от прежнего положения.
        Assert.Contains("window.SizeChanged += (_, _) => RefreshSizeConstraints(window);", code, StringComparison.Ordinal);
        Assert.Contains("private static void RefreshSizeConstraints(Window window)", code, StringComparison.Ordinal);
        Assert.Contains("private static Rect? ApplySizeConstraints(Window window)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Limiter_KeepsMaximizedConstraintsCleared()
    {
        // Регрессия issue #171/#176 не возвращается: для развёрнутого окна
        // ограничения по-прежнему снимаются.
        var code = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        Assert.Contains("ClearSizeConstraints(window);", code, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = double.PositiveInfinity", code, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = double.PositiveInfinity", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Clamp_NoMarginForMainWindowAndRaisesLimit()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");

        Assert.Contains("window is MainWindow", code, StringComparison.Ordinal);
        Assert.Contains("Services.WindowBoundsPolicy.DefaultMargin", code, StringComparison.Ordinal);
        Assert.Contains("Services.WindowBoundsPolicy.EffectiveMax(", code, StringComparison.Ordinal);
        Assert.Contains("window.IsVisible", code, StringComparison.Ordinal);
    }
}
