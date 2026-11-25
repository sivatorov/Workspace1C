namespace Workspace1C;

/// <summary>
/// Чистая политика толщины расширенной стеклянной рамки WPF-окна
/// (<c>WindowChrome.GlassFrameThickness</c>) — исправление дефекта «пустое поле внизу
/// программы» (1.0.1.122).
/// <para>
/// Причина дефекта: окно с <c>WindowChrome</c> и <c>GlassFrameThickness = -1</c>
/// (расширенная системная рамка + acrylic/mica) при положении «на всю рабочую область»
/// оставляло снизу прозрачную полосу (~44 px), через которую был виден рабочий стол.
/// Поэтому толщина рамки вычисляется не только по состоянию окна, но и по фактическому
/// положению окна относительно рабочей области монитора:
/// </para>
/// <list type="bullet">
/// <item>окно развёрнуто (maximized) — прежнее поведение: <c>0</c> (обходное решение,
/// иначе расширенная рамка перекрывает панель задач Windows 11);</item>
/// <item>окно прижато к верхней или нижней границе рабочей области — <c>0</c>
/// (расширенная рамка не нужна и даёт прозрачную полосу);</item>
/// <item>во всех остальных случаях — <c>-1</c> (расширенная рамка, acrylic сохраняется).</item>
/// </list>
/// <para>
/// Класс не зависит от UI-платформы (только числа и кортежи), поэтому используется
/// и главным окном, и общим оформлением диалогов, и покрыт юнит-тестами
/// (<c>Issue170WindowGlassFrameTests</c>).
/// </para>
/// </summary>
public static class WindowGlassFramePolicy
{
    /// <summary>Толщина расширенной стеклянной рамки (<c>GlassFrameThickness = -1</c>).</summary>
    public const double ExtendedThickness = -1;

    /// <summary>Рамка без расширения (<c>GlassFrameThickness = 0</c>).</summary>
    public const double NoneThickness = 0;

    /// <summary>
    /// Допуск «прижатия» окна к границе рабочей области в DIP: окно, подогнанное
    /// пользователем вручную, редко совпадает с границей до пикселя.
    /// </summary>
    public const double DefaultTolerance = 1.0;

    /// <summary>
    /// Вычисляет толщину стеклянной рамки окна.
    /// </summary>
    /// <param name="maximized">Окно развёрнуто (<see cref="System.Windows.WindowState.Maximized"/>).</param>
    /// <param name="window">Фактические границы окна в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="workArea">Рабочая область монитора в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="tolerance">Допуск прижатия к границе рабочей области, DIP.</param>
    /// <returns>
    /// <see cref="NoneThickness"/> (0) для развёрнутого окна и окна, прижатого к верхней
    /// или нижней границе рабочей области; <see cref="ExtendedThickness"/> (-1) иначе,
    /// а также когда рабочая область или границы окна неизвестны/некорректны
    /// (прежнее поведение — расширенная рамка).
    /// </returns>
    public static double Resolve(
        bool maximized,
        (double Left, double Top, double Width, double Height) window,
        (double Left, double Top, double Width, double Height) workArea,
        double tolerance = DefaultTolerance)
    {
        // Развёрнутое окно — прежнее поведение: расширенная рамка обнуляется.
        if (maximized)
            return NoneThickness;

        // Нет достоверных данных о рабочей области или окне — прежнее поведение.
        if (!IsValidBounds(window) || !IsValidBounds(workArea))
            return ExtendedThickness;

        var tol = double.IsFinite(tolerance) && tolerance >= 0
            ? tolerance
            : DefaultTolerance;

        var windowBottom = window.Top + window.Height;
        var workBottom = workArea.Top + workArea.Height;

        // Окно занимает всю рабочую область по высоте: прижато к верхней или нижней
        // границе — расширенная рамка дала бы прозрачную полосу (дефект 1.0.1.122).
        if (Math.Abs(window.Top - workArea.Top) <= tol ||
            Math.Abs(windowBottom - workBottom) <= tol)
        {
            return NoneThickness;
        }

        return ExtendedThickness;
    }

    /// <summary>
    /// Границы пригодны для расчёта: конечные координаты и положительные размеры.
    /// </summary>
    private static bool IsValidBounds((double Left, double Top, double Width, double Height) bounds)
    {
        return double.IsFinite(bounds.Left) &&
               double.IsFinite(bounds.Top) &&
               double.IsFinite(bounds.Width) &&
               double.IsFinite(bounds.Height) &&
               bounds.Width > 0 &&
               bounds.Height > 0;
    }
}
