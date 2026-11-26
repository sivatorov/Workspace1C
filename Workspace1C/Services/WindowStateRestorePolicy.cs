using System;

namespace Workspace1C.Services;

/// <summary>
/// Issue #171: состояние главного окна (<c>Maximized</c>/<c>Normal</c>), которое нужно
/// восстановить при старте. <see cref="None"/> — сохранённое состояние не применяется
/// (пусто/неизвестно/свёрнутое окно: свёрнутое состояние не восстанавливается).
/// </summary>
public enum WindowRestoreState
{
    /// <summary>Состояние не восстанавливается.</summary>
    None = 0,

    /// <summary>Обычное (плавающее) окно.</summary>
    Normal = 1,

    /// <summary>Развёрнутое на весь экран окно.</summary>
    Maximized = 2
}

/// <summary>
/// Границы окна: размеры и позиция в тех единицах, которые использует платформа
/// (WPF — DIP, Avalonia — клиентский размер и физические пиксели позиции).
/// Политика не интерпретирует единицы — она только решает, какие из них брать.
/// </summary>
public readonly record struct WindowLayoutBounds(double Width, double Height, double Left, double Top);

/// <summary>
/// План восстановления макета при старте: какое состояние применить, нужно ли вообще
/// применять сохранённые границы и являются ли они restore-bounds (размер «до разворота»).
/// </summary>
public readonly record struct WindowRestorePlan(
    WindowRestoreState State,
    bool ApplyBounds,
    bool UseRestoreBounds);

/// <summary>
/// План сохранения макета при закрытии: сохранять ли вообще, какое состояние записать
/// и какие границы. <see cref="BoundsValid"/> = false означает, что валидных границ нет
/// (тогда платформа записывает нули, оставляя «размер не сохранён», но состояние
/// всё равно фиксируется — иначе развёрнутое окно «теряло» состояние, issue #171).
/// </summary>
public readonly record struct WindowLayoutSavePlan(
    bool Save,
    WindowRestoreState State,
    WindowLayoutBounds Bounds,
    bool BoundsValid)
{
    /// <summary>Ничего не сохранять (макет остаётся прежним).</summary>
    public static WindowLayoutSavePlan Skip => new(false, WindowRestoreState.None, default, false);
}

/// <summary>
/// Issue #171 («Не восстанавливается WindowState окна, только его размеры»): единая
/// политика сохранения/восстановления состояния и границ главного окна для WPF и
/// Avalonia.
/// <para>
/// Причина дефекта: границы (размер/позиция) применялись безусловно и ДО состояния,
/// а состояние затем «перебивалось» платформой (WPF пересчитывал размещение окна с
/// <c>WindowStartupLocation=Manual</c>, Avalonia — ограничителем рабочей области,
/// который выставлял <c>Width</c>/<c>Height</c> уже развёрнутому окну). В результате
/// окно открывалось размером как развёрнутое, но в состоянии <c>Normal</c>.
/// Дополнительно сохранение отбрасывалось целиком, если границы оказывались
/// невалидными (например <c>RestoreBounds</c> окна, развёрнутого с самого старта),
/// поэтому на диск оставалось прежнее состояние <c>Normal</c>.
/// </para>
/// <para>
/// Класс не зависит от WPF/Avalonia (только строки и числа), поэтому обе оболочки
/// принимают одинаковое решение, а поведение покрыто юнит-тестами без запуска окон.
/// </para>
/// </summary>
public static class WindowStateRestorePolicy
{
    /// <summary>Каноническое имя обычного состояния (как в настройках и WPF/Avalonia).</summary>
    public const string NormalStateName = "Normal";

    /// <summary>Каноническое имя развёрнутого состояния.</summary>
    public const string MaximizedStateName = "Maximized";

    /// <summary>Каноническое имя свёрнутого состояния (в настройки не пишется).</summary>
    public const string MinimizedStateName = "Minimized";

    /// <summary>
    /// Разбирает сохранённое состояние окна. <c>Minimized</c>, пустая строка и любые
    /// неизвестные значения дают <see cref="WindowRestoreState.None"/>: свёрнутое окно
    /// не восстанавливается, а мусор в настройках не должен менять поведение запуска.
    /// </summary>
    public static WindowRestoreState ParseSavedState(string? savedState)
    {
        var value = savedState?.Trim();
        if (string.IsNullOrEmpty(value))
            return WindowRestoreState.None;

        if (string.Equals(value, MaximizedStateName, StringComparison.OrdinalIgnoreCase))
            return WindowRestoreState.Maximized;

        if (string.Equals(value, NormalStateName, StringComparison.OrdinalIgnoreCase))
            return WindowRestoreState.Normal;

        // Minimized и любые неизвестные значения: состояние не применяем.
        return WindowRestoreState.None;
    }

    /// <summary>Каноническое имя состояния для записи в настройки.</summary>
    public static string ToStateName(WindowRestoreState state) => state switch
    {
        WindowRestoreState.Maximized => MaximizedStateName,
        WindowRestoreState.Normal => NormalStateName,
        _ => string.Empty
    };

    /// <summary>
    /// Валидны ли границы окна: положительные конечные размеры и конечные координаты.
    /// NaN/бесконечность (например <c>RestoreBounds</c> до первого показа окна) и нули
    /// считаются невалидными — их нельзя ни применять, ни сериализовать в JSON.
    /// </summary>
    public static bool IsValidBounds(WindowLayoutBounds bounds)
    {
        return IsFinitePositive(bounds.Width)
            && IsFinitePositive(bounds.Height)
            && IsFinite(bounds.Left)
            && IsFinite(bounds.Top);
    }

    /// <summary>
    /// Решение «что применять при старте» (issue #171). Для <c>Maximized</c> сохранённые
    /// границы — это restore-bounds (размер, к которому окно вернётся при восстановлении),
    /// поэтому они применяются, но состояние выставляется ПОСЛЕ них и подтверждается
    /// после показа окна; для <c>Normal</c> — обычные размеры/позиция.
    /// </summary>
    public static WindowRestorePlan PlanRestore(string? savedState)
    {
        var state = ParseSavedState(savedState);
        return state switch
        {
            WindowRestoreState.Maximized => new WindowRestorePlan(state, ApplyBounds: true, UseRestoreBounds: true),
            WindowRestoreState.Normal => new WindowRestorePlan(state, ApplyBounds: true, UseRestoreBounds: false),
            _ => new WindowRestorePlan(WindowRestoreState.None, ApplyBounds: false, UseRestoreBounds: false)
        };
    }

    /// <summary>
    /// Решение «что сохранять при закрытии» (issue #171).
    /// <list type="bullet">
    /// <item>Свёрнутое окно ничего не говорит о геометрии — макет не перезаписывается.</item>
    /// <item>Обычное окно сохраняется фактическими границами (если они валидны).</item>
    /// <item>Развёрнутое окно сохраняется состоянием <c>Maximized</c>, а границами —
    /// restore-bounds; если restore-bounds невалидны (окно развёрнуто с самого старта),
    /// берутся фактические, затем ранее сохранённые. Состояние сохраняется ВСЕГДА —
    /// именно его потеря приводила к открытию окна «размером как развёрнутое, но Normal».</item>
    /// </list>
    /// </summary>
    /// <param name="currentState">Текущее состояние окна (Normal/Maximized/Minimized).</param>
    /// <param name="actualBounds">Фактические границы окна (null — недоступны).</param>
    /// <param name="restoreBounds">Restore-bounds развёрнутого окна (null — недоступны).</param>
    /// <param name="savedBounds">Ранее сохранённые границы (страховка от потери размера).</param>
    public static WindowLayoutSavePlan PlanSave(
        string? currentState,
        WindowLayoutBounds? actualBounds,
        WindowLayoutBounds? restoreBounds,
        WindowLayoutBounds? savedBounds)
    {
        var value = currentState?.Trim();
        if (string.Equals(value, MinimizedStateName, StringComparison.OrdinalIgnoreCase))
            return WindowLayoutSavePlan.Skip;

        var state = ParseSavedState(currentState);
        if (state == WindowRestoreState.None)
            state = WindowRestoreState.Normal; // неизвестное/пустое состояние — обычное окно

        var candidates = state == WindowRestoreState.Maximized
            ? new[] { restoreBounds, actualBounds, savedBounds }
            : new[] { actualBounds, savedBounds };

        foreach (var candidate in candidates)
        {
            if (candidate is { } bounds && IsValidBounds(bounds))
                return new WindowLayoutSavePlan(true, state, bounds, BoundsValid: true);
        }

        // Валидных границ нет — состояние всё равно фиксируем (размер «не сохранён»).
        return new WindowLayoutSavePlan(true, state, default, BoundsValid: false);
    }

    private static bool IsFinitePositive(double value) => IsFinite(value) && value > 0;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
