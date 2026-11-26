using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #171 («Не восстанавливается WindowState окна, только его размеры»):
/// после разворота окна на весь экран, закрытия и повторного запуска окно
/// открывалось размером как развёрнутое, но в состоянии <c>Normal</c>.
/// <para>
/// Проверяется чистая политика <see cref="WindowStateRestorePolicy"/> (общая для WPF и
/// Avalonia): развёрнутое окно сохраняется как <c>Maximized</c> и восстанавливается как
/// <c>Maximized</c> (границы при этом — restore-bounds), обычное окно — <c>Normal</c> с
/// размерами, свёрнутое — не восстанавливается и не перезаписывает макет. Дополнительно
/// source-guard проверяет точки подключения политики в обеих оболочках и то, что
/// ограничители размера не трогают развёрнутое окно.
/// </para>
/// </summary>
public sealed class Issue171WindowStateRestoreTests
{
    private static readonly WindowLayoutBounds NormalBounds = new(1200, 760, 100, 80);

    // ===================== Восстановление при старте =====================

    [Fact]
    public void PlanRestore_Maximized_RestoresMaximizedWithRestoreBounds()
    {
        var plan = WindowStateRestorePolicy.PlanRestore("Maximized");

        Assert.Equal(WindowRestoreState.Maximized, plan.State);
        Assert.True(plan.ApplyBounds);
        // Для развёрнутого окна сохранённые границы — restore-bounds (размер до разворота).
        Assert.True(plan.UseRestoreBounds);
    }

    [Fact]
    public void PlanRestore_Normal_RestoresNormalWithSavedBounds()
    {
        var plan = WindowStateRestorePolicy.PlanRestore("Normal");

        Assert.Equal(WindowRestoreState.Normal, plan.State);
        Assert.True(plan.ApplyBounds);
        Assert.False(plan.UseRestoreBounds);
    }

    [Fact]
    public void PlanRestore_Minimized_IsNotRestored()
    {
        var plan = WindowStateRestorePolicy.PlanRestore("Minimized");

        Assert.Equal(WindowRestoreState.None, plan.State);
        Assert.False(plan.ApplyBounds);
        Assert.False(plan.UseRestoreBounds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("999")]
    [InlineData("Something")]
    public void PlanRestore_UnknownOrEmpty_IsNotRestored(string? savedState)
    {
        var plan = WindowStateRestorePolicy.PlanRestore(savedState);

        Assert.Equal(WindowRestoreState.None, plan.State);
        Assert.False(plan.ApplyBounds);
    }

    [Fact]
    public void PlanRestore_IsCaseInsensitiveAndTrims()
    {
        Assert.Equal(WindowRestoreState.Maximized, WindowStateRestorePolicy.PlanRestore("  maximized ").State);
        Assert.Equal(WindowRestoreState.Normal, WindowStateRestorePolicy.PlanRestore("normal").State);
    }

    // ===================== Сохранение при закрытии =====================

    [Fact]
    public void PlanSave_Maximized_SavesMaximizedStateWithRestoreBounds()
    {
        var maximizedBounds = new WindowLayoutBounds(1920, 1040, -8, -8);

        var plan = WindowStateRestorePolicy.PlanSave(
            "Maximized",
            actualBounds: maximizedBounds,
            restoreBounds: NormalBounds,
            savedBounds: null);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Maximized, plan.State);
        Assert.True(plan.BoundsValid);
        // Размер — «до разворота», чтобы следующий запуск не взял размер во весь экран
        // как обычный.
        Assert.Equal(NormalBounds, plan.Bounds);
        Assert.Equal("Maximized", WindowStateRestorePolicy.ToStateName(plan.State));
    }

    [Fact]
    public void PlanSave_MaximizedWithoutRestoreBounds_StillSavesState()
    {
        // Окно развёрнуто с самого старта: RestoreBounds/фактические границы недоступны.
        var plan = WindowStateRestorePolicy.PlanSave(
            "Maximized",
            actualBounds: null,
            restoreBounds: null,
            savedBounds: null);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Maximized, plan.State);
        Assert.False(plan.BoundsValid);
        Assert.Equal("Maximized", WindowStateRestorePolicy.ToStateName(plan.State));
    }

    [Fact]
    public void PlanSave_MaximizedWithInvalidRestoreBounds_FallsBackToSavedBounds()
    {
        var invalidRestore = new WindowLayoutBounds(double.NaN, double.NaN, double.NaN, double.NaN);

        var plan = WindowStateRestorePolicy.PlanSave(
            "Maximized",
            actualBounds: invalidRestore,
            restoreBounds: invalidRestore,
            savedBounds: NormalBounds);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Maximized, plan.State);
        Assert.True(plan.BoundsValid);
        Assert.Equal(NormalBounds, plan.Bounds);
    }

    [Fact]
    public void PlanSave_Normal_SavesNormalWithActualBounds()
    {
        var plan = WindowStateRestorePolicy.PlanSave(
            "Normal",
            actualBounds: NormalBounds,
            restoreBounds: null,
            savedBounds: null);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Normal, plan.State);
        Assert.True(plan.BoundsValid);
        Assert.Equal(NormalBounds, plan.Bounds);
        Assert.Equal("Normal", WindowStateRestorePolicy.ToStateName(plan.State));
    }

    [Fact]
    public void PlanSave_NormalWithNaN_UsesSavedBoundsInstead()
    {
        var nan = new WindowLayoutBounds(double.NaN, double.NaN, double.NaN, double.NaN);

        var plan = WindowStateRestorePolicy.PlanSave("Normal", nan, null, NormalBounds);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Normal, plan.State);
        Assert.Equal(NormalBounds, plan.Bounds);
    }

    [Fact]
    public void PlanSave_Minimized_DoesNotOverwriteLayout()
    {
        var plan = WindowStateRestorePolicy.PlanSave(
            "Minimized",
            actualBounds: NormalBounds,
            restoreBounds: NormalBounds,
            savedBounds: NormalBounds);

        Assert.False(plan.Save);
        Assert.Equal(WindowRestoreState.None, plan.State);
    }

    [Fact]
    public void PlanSave_UnknownState_TreatedAsNormal()
    {
        var plan = WindowStateRestorePolicy.PlanSave("", NormalBounds, null, null);

        Assert.True(plan.Save);
        Assert.Equal(WindowRestoreState.Normal, plan.State);
    }

    // ===================== Сквозной сценарий issue #171 =====================

    [Fact]
    public void MaximizedWindow_RoundTrip_SavesAndRestoresMaximized()
    {
        // Закрытие развёрнутого окна: сохраняется состояние Maximized + restore-bounds.
        var save = WindowStateRestorePolicy.PlanSave(
            "Maximized",
            actualBounds: new WindowLayoutBounds(1920, 1040, -8, -8),
            restoreBounds: NormalBounds,
            savedBounds: null);

        Assert.True(save.Save);
        Assert.Equal(WindowRestoreState.Maximized, save.State);

        // Следующий запуск: применяется именно Maximized, границы — restore-bounds.
        var restore = WindowStateRestorePolicy.PlanRestore(
            WindowStateRestorePolicy.ToStateName(save.State));

        Assert.Equal(WindowRestoreState.Maximized, restore.State);
        Assert.True(restore.ApplyBounds);
        Assert.True(restore.UseRestoreBounds);
    }

    [Fact]
    public void NormalWindow_RoundTrip_SavesAndRestoresNormal()
    {
        var save = WindowStateRestorePolicy.PlanSave("Normal", NormalBounds, null, null);
        var restore = WindowStateRestorePolicy.PlanRestore(
            WindowStateRestorePolicy.ToStateName(save.State));

        Assert.Equal(WindowRestoreState.Normal, restore.State);
        Assert.True(restore.ApplyBounds);
        Assert.False(restore.UseRestoreBounds);
    }

    [Fact]
    public void MinimizedWindow_RoundTrip_IsNotRestoredAsMinimized()
    {
        // Свёрнутое окно макет не перезаписывает…
        var save = WindowStateRestorePolicy.PlanSave("Minimized", NormalBounds, NormalBounds, NormalBounds);
        Assert.False(save.Save);

        // …а сохранённое «Minimized» (гипотетически из старых настроек) не применяется.
        var restore = WindowStateRestorePolicy.PlanRestore("Minimized");
        Assert.Equal(WindowRestoreState.None, restore.State);
    }

    // ===================== Валидация границ =====================

    [Fact]
    public void IsValidBounds_RejectsNaNZeroAndNegative()
    {
        Assert.True(WindowStateRestorePolicy.IsValidBounds(NormalBounds));
        Assert.False(WindowStateRestorePolicy.IsValidBounds(new WindowLayoutBounds(double.NaN, 760, 0, 0)));
        Assert.False(WindowStateRestorePolicy.IsValidBounds(new WindowLayoutBounds(double.PositiveInfinity, 760, 0, 0)));
        Assert.False(WindowStateRestorePolicy.IsValidBounds(new WindowLayoutBounds(0, 760, 0, 0)));
        Assert.False(WindowStateRestorePolicy.IsValidBounds(new WindowLayoutBounds(1200, -10, 0, 0)));
        Assert.False(WindowStateRestorePolicy.IsValidBounds(new WindowLayoutBounds(1200, 760, double.NaN, 0)));
    }

    // ===================== Точки подключения (source-guard) =====================

    [Fact]
    public void WpfMainWindow_UsesSharedPolicyForRestoreAndSave()
    {
        var events = ReadProjectFile("Workspace1C", "Views", "MainWindow.Events.cs");

        // Восстановление: решение «применять ли состояние/границы» — из политики.
        Assert.Contains("WindowStateRestorePolicy.PlanRestore", events, StringComparison.Ordinal);
        // Сохранение: состояние больше не теряется при невалидных границах.
        Assert.Contains("WindowStateRestorePolicy.PlanSave", events, StringComparison.Ordinal);
        // Состояние подтверждается после показа окна (Manual/CenterScreen его сбрасывали).
        Assert.Contains("EnsureSavedWindowState(finalAttempt: true)", events, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMainWindow_UsesSharedPolicyForRestoreAndSave()
    {
        var window = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");

        Assert.Contains("WindowStateRestorePolicy.PlanRestore", window, StringComparison.Ordinal);
        Assert.Contains("WindowStateRestorePolicy.PlanSave", window, StringComparison.Ordinal);
        // Состояние подтверждается после показа окна (OnOpened).
        Assert.Contains("EnsureSavedWindowState();", window, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaClamps_DoNotTouchMaximizedWindow()
    {
        var modalBase = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");

        // Ограничители размера/рабочей области обязаны пропускать не-Normal окно:
        // присвоение Width/Height развёрнутому окну сбрасывало WindowState в Normal.
        Assert.Contains("if (window.WindowState != WindowState.Normal)", modalBase, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(modalBase, "if (window.WindowState != WindowState.Normal)") >= 2,
            "Ожидались проверки состояния и в ClampWindowToMinSize, и в ApplyWorkAreaClamp.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
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
