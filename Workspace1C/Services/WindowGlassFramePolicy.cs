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
/// <item>окно занимает всю рабочую область по высоте, то есть его <b>низ</b> прижат
/// к нижней границе рабочей области — <c>0</c> (именно там расширенная рамка давала
/// прозрачную полосу снизу);</item>
/// <item>во всех остальных случаях — <c>-1</c> (расширенная рамка, acrylic сохраняется).</item>
/// </list>
/// <para>
/// Важно (регрессия 1.0.1.122 → issue #170): прижатие окна только к <b>верхней</b>
/// границе рабочей области (типичное положение окна с <c>Top = 0</c>) <b>не</b>
/// обнуляет рамку. Иначе расширенная стеклянная рамка/acrylic отключались бы почти
/// всегда, а её пересчёт на каждом <c>SizeChanged</c>/<c>LocationChanged</c> заставлял
/// DWM пересоздавать стекло — под полупрозрачной подложкой окна (alpha ~0xE8) не
/// оставалось системного backdrop, и на списке баз появлялись «видеоартефакты»
/// (смазанные/неперерисованные участки при прокрутке и ресайзе).
/// </para>
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
    /// Толщина невидимой рамки ресайза <c>WindowChrome.ResizeBorderThickness</c> (DIP),
    /// совпадает с разметкой главного окна и диалогов (<c>ResizeBorderThickness="6"</c>).
    /// В развёрнутом окне она обнуляется: иначе на HiDPI (125%/150%) невидимая рамка
    /// ресайза, пересчитанная WPF в физические пиксели, оставляет вокруг
    /// максимизированного окна прозрачную полосу (issue #172).
    /// </summary>
    public const double ResizeBorderThicknessDip = 6;

    /// <summary>
    /// Вычисляет толщину стеклянной рамки окна.
    /// </summary>
    /// <param name="maximized">Окно развёрнуто (<see cref="System.Windows.WindowState.Maximized"/>).</param>
    /// <param name="window">Фактические границы окна в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="workArea">Рабочая область монитора в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="tolerance">Допуск прижатия к границе рабочей области, DIP.</param>
    /// <returns>
    /// <see cref="NoneThickness"/> (0) для развёрнутого окна и окна, низ которого прижат
    /// к нижней границе рабочей области (окно занимает всю её высоту);
    /// <see cref="ExtendedThickness"/> (-1) иначе — в том числе когда окно прижато только
    /// к верхней границе (регрессия #170: иначе acrylic отключался почти всегда) — а также
    /// когда рабочая область или границы окна неизвестны/некорректны.
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

        // Окно занимает всю рабочую область по высоте — его НИЗ прижат к нижней границе:
        // именно там расширенная рамка оставляла прозрачную полосу (дефект 1.0.1.122).
        // Прижатие только к верхней границе (Top = 0 — типичное положение окна) рамку
        // НЕ обнуляет: иначе acrylic отключался почти всегда и DWM на каждом
        // SizeChanged/LocationChanged пересоздавал стекло, давая «видеоартефакты» на
        // списке баз (регрессия #170).
        if (Math.Abs(windowBottom - workBottom) <= tol)
            return NoneThickness;

        return ExtendedThickness;
    }

    /// <summary>
    /// Итоговая геометрия рамки окна, которую нужно применить к <c>WindowChrome</c>
    /// (issue #172). Толщина стеклянной рамки, толщина невидимой рамки ресайза и
    /// (необязательное) принудительное скругление <c>WindowChrome.CornerRadius</c>.
    /// </summary>
    /// <param name="GlassFrameThickness">
    /// <c>GlassFrameThickness</c> в DIP: 0 — прозрачной рамки нет, -1 — расширенная.
    /// </param>
    /// <param name="ResizeBorderThickness"><c>ResizeBorderThickness</c> в DIP.</param>
    /// <param name="CornerRadiusOverride">
    /// Скругление <c>WindowChrome.CornerRadius</c>: <c>0</c> — принудительно прямые углы
    /// (развёрнутое окно); <c>null</c> — не трогать (в обычном состоянии скруглением
    /// управляет механизм issue #126 по фактическому состоянию DWM).
    /// </param>
    public readonly record struct WindowGlassFrameState(
        double GlassFrameThickness,
        double ResizeBorderThickness,
        double? CornerRadiusOverride);

    /// <summary>
    /// Вычисляет полную геометрию рамки с учётом фактического DPI окна (issue #172).
    /// <para>
    /// Причина дефекта «прозрачная рамка вокруг максимизированного окна на 125%»:
    /// толщина рамки и рабочая область сравнивались без учёта DPI, а
    /// <c>ResizeBorderThickness</c> оставался ненулевым в развёрнутом окне. WPF
    /// пересчитывает его в физические пиксели (6 DIP = 7.5 px при 125%, 9 px при 150%),
    /// а DWM максимизирует окно на целое число пикселей — расхождение оставляло
    /// прозрачную полосу по краям. Поэтому в <c>Maximized</c> при любом масштабе
    /// (100%, 125%, 150%) рамка обнуляется полностью: стеклянная рамка 0, рамка
    /// ресайза 0, скругление 0.
    /// </para>
    /// <para>
    /// В обычном состоянии поведение issue #126 сохраняется: толщина стеклянной рамки
    /// считается по <see cref="Resolve"/> (0 только когда окно занимает всю высоту
    /// рабочей области и прижато к нижней границе, иначе -1 — acrylic сохраняется),
    /// рамка ресайза остаётся 6 DIP, а скругление не навязывается
    /// (<see cref="WindowGlassFrameState.CornerRadiusOverride"/> = null) — им управляет
    /// синхронизация с DWM.
    /// </para>
    /// </summary>
    /// <param name="maximized">Окно развёрнуто.</param>
    /// <param name="window">Фактические границы окна в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="workArea">Рабочая область монитора в DIP: <c>(Left, Top, Width, Height)</c>.</param>
    /// <param name="dpiScale">Фактический масштаб DPI окна (VisualTreeHelper.GetDpi), 1.0 = 96 DPI.</param>
    /// <param name="tolerance">Допуск прижатия к границе рабочей области, DIP.</param>
    public static WindowGlassFrameState ResolveState(
        bool maximized,
        (double Left, double Top, double Width, double Height) window,
        (double Left, double Top, double Width, double Height) workArea,
        double dpiScale = 1.0,
        double tolerance = DefaultTolerance)
    {
        var dpi = NormalizeDpi(dpiScale);

        // Развёрнутое окно: прозрачной рамки нет ни при каком DPI. Рамка ресайза и
        // скругление тоже обнуляются — окно прижато к краям рабочей области.
        if (maximized)
            return new WindowGlassFrameState(NoneThickness, NoneThickness, NoneThickness);

        // Точность перевода физических пикселей в DIP: положение окна и рабочая область
        // приходят в DIP, но были получены округлением до целого физического пикселя.
        // Максимальная погрешность — 0.5 физического пикселя = 0.5 / dpi DIP, поэтому
        // допуск «прижатия» увеличивается на эту величину. На 125%/150% это не даёт
        // ложному зазору превратить прижатую рамку в расширенную (прозрачную) полосу.
        var baseTolerance = double.IsFinite(tolerance) && tolerance >= 0
            ? tolerance
            : DefaultTolerance;
        var effectiveTolerance = baseTolerance + 0.5 / dpi;

        var thickness = Resolve(
            maximized: false,
            window: window,
            workArea: workArea,
            tolerance: effectiveTolerance);

        return new WindowGlassFrameState(
            GlassFrameThickness: thickness,
            ResizeBorderThickness: ResizeBorderThicknessDip,
            CornerRadiusOverride: null);
    }

    /// <summary>
    /// Приводит масштаб DPI к пригодному значению: нечисловые/нулевые/отрицательные
    /// значения (окно ещё не показано, DPI неизвестен) трактуются как 1.0.
    /// </summary>
    private static double NormalizeDpi(double dpiScale)
        => double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0;

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
