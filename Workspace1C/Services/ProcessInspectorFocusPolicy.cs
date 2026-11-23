using System;

namespace Workspace1C.Services;

/// <summary>Навигационная клавиша инспектора процессов (issue #134).</summary>
public enum InspectorNavigationKey
{
    /// <summary>Не навигационная клавиша.</summary>
    None = 0,
    Up,
    Down,
    PageUp,
    PageDown,
    Home,
    End
}

/// <summary>
/// Issue #134 (1.0.1.106): чистая политика клавиатурной навигации инспектора процессов.
/// <para>
/// Пользователь видел фокус на кнопке «Колонки»: первое нажатие «вниз» сразу после
/// открытия окна (пока данные ещё не загрузились) и после завершения последнего
/// процесса уходило на соседний элемент, потому что обработчик пропускал навигацию
/// при пустом списке. Политика больше не зависит от количества строк: если фокус
/// не в таблице, навигационная клавиша всегда перенаправляется в неё, а перемещение
/// выполняется только при наличии строк.
/// </para>
/// </summary>
public static class ProcessInspectorFocusPolicy
{
    /// <summary>
    /// Сколько раз окно повторяет попытку установки фокуса в таблицу (issue #134,
    /// 1.0.1.107). Активация окна завершается асинхронно, поэтому одного
    /// синхронного <c>Focus()</c> недостаточно.
    /// </summary>
    public const int MaxFocusAttempts = 12;

    /// <summary>
    /// Пауза между повторными попытками установки фокуса в миллисекундах
    /// (issue #134, 1.0.1.107). 12 × 120 мс ≈ 1,4 с — достаточно, чтобы окно
    /// активировалось и диалог подтверждения вернул управление.
    /// </summary>
    public const int FocusRetryDelayMs = 120;

    /// <summary>Является ли клавиша навигационной (стрелки, PageUp/PageDown, Home/End).</summary>
    public static bool IsNavigationKey(InspectorNavigationKey key) =>
        key != InspectorNavigationKey.None;

    /// <summary>
    /// Нужно ли повторить попытку установки фокуса (issue #134, 1.0.1.107): повтор
    /// продолжается, пока клавиатурный фокус не оказался в таблице и лимит попыток
    /// не исчерпан. Не зависит от количества строк — таблица с пустым списком тоже
    /// должна получать фокус.
    /// </summary>
    public static bool ShouldRetryFocus(bool gridHasKeyboardFocus, int attempt) =>
        !gridHasKeyboardFocus && attempt < MaxFocusAttempts;

    /// <summary>
    /// Нужно ли перенаправить навигационную клавишу в таблицу процессов. Пока фокус
    /// в таблице, поведение не перехватывается; у полей ввода клавиши тоже не трогаем.
    /// </summary>
    public static bool ShouldRedirectNavigation(
        bool gridHasKeyboardFocus,
        InspectorNavigationKey key,
        bool focusedElementIsTextInput) =>
        !gridHasKeyboardFocus &&
        !focusedElementIsTextInput &&
        IsNavigationKey(key);

    /// <summary>
    /// Целевой индекс строки для навигационной клавиши. Для пустого списка — <c>-1</c>
    /// (фокус всё равно переносится в таблицу, но выделять нечего).
    /// </summary>
    public static int ResolveTargetIndex(
        int currentIndex,
        int itemCount,
        InspectorNavigationKey key)
    {
        if (itemCount <= 0)
            return -1;

        var current = currentIndex < 0 ? -1 : Math.Min(currentIndex, itemCount - 1);
        return key switch
        {
            InspectorNavigationKey.Home => 0,
            InspectorNavigationKey.End => itemCount - 1,
            InspectorNavigationKey.Up => current > 0 ? current - 1 : 0,
            InspectorNavigationKey.PageUp => Math.Max(0, (current < 0 ? 0 : current) - 10),
            InspectorNavigationKey.PageDown => Math.Min(itemCount - 1, (current < 0 ? 0 : current) + 10),
            InspectorNavigationKey.Down => current < 0 ? 0 : Math.Min(itemCount - 1, current + 1),
            _ => current < 0 ? 0 : current
        };
    }
}
