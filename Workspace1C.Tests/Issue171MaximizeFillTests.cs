using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #171/#176 «окно разворачивается не на весь экран, белые полосы справа и снизу».
/// <para>
/// Причина: и WPF (<see cref="Workspace1C.WindowWorkAreaLimiter"/>), и Avalonia
/// (<c>ModalWindowBase.ClampWindowToWorkArea</c>) назначали окну
/// <c>MaxWidth</c>/<c>MaxHeight</c> по рабочей области <b>минус отступ</b>
/// (<see cref="WindowBoundsPolicy.DefaultMargin"/>). Оконные менеджеры учитывают эти
/// ограничения при развороте, поэтому максимизированное окно оказывалось меньше
/// рабочей области — справа и снизу оставались полосы.
/// </para>
/// <para>
/// Исправление: при развороте ограничения максимума снимаются, при возврате в
/// обычное состояние — применяются снова. Тесты — стражи исходников обеих платформ
/// и проверка политики отступов.
/// </para>
/// </summary>
public sealed class Issue171MaximizeFillTests
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

    [Fact]
    public void Wpf_Limiter_ClearsSizeConstraintsWhenMaximized()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        Assert.Contains("ClearSizeConstraints", code);
        Assert.Contains("MaxWidth = double.PositiveInfinity", code);
        Assert.Contains("MaxHeight = double.PositiveInfinity", code);
        Assert.Contains("window.StateChanged", code);
    }

    [Fact]
    public void Avalonia_Clamp_ClearsSizeConstraintsWhenMaximized()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");

        Assert.Contains("OnWindowStateChangedForClamp", code);
        Assert.Contains("Window.WindowStateProperty", code);
        Assert.Contains("MaxWidth = double.PositiveInfinity", code);
        Assert.Contains("MaxHeight = double.PositiveInfinity", code);
    }

    [Fact]
    public void WorkAreaPolicy_StillReservesMarginForNormalWindow()
    {
        // Ограничение стартового размера обычного окна сохраняется: полосы появлялись
        // не из-за отступа как такового, а из-за применения его к развёрнутому окну.
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 2000,
            designHeight: 1500,
            minWidth: 900,
            minHeight: 600,
            workWidth: 1920,
            workHeight: 1040);

        Assert.Equal(1920 - WindowBoundsPolicy.DefaultMargin, bounds.MaxWidth);
        Assert.Equal(1040 - WindowBoundsPolicy.DefaultMargin, bounds.MaxHeight);
        // Стартовый размер зажат рабочей областью.
        Assert.True(bounds.Width <= bounds.MaxWidth);
        Assert.True(bounds.Height <= bounds.MaxHeight);
    }
}
