using System;

namespace Workspace1C.Services;

/// <summary>
/// Issue #119 (1.0.1.119): единая политика адаптивных размеров модальных окон.
/// Окно не должно выходить за рабочую область монитора и не должно открываться
/// «гигантским» на больших экранах: стартовый размер ограничивается разумным
/// потолком, а длинное содержимое прокручивается внутри окна.
/// <para>
/// Класс не зависит от WPF/Avalonia, поэтому обе оболочки считают одинаково,
/// а поведение покрыто юнит-тестами без запуска окон.
/// </para>
/// </summary>
public static class WindowBoundsPolicy
{
    /// <summary>Отступ от краёв рабочей области по умолчанию, DIP.</summary>
    public const double DefaultMargin = 48;

    /// <summary>
    /// Потолок стартовой ширины модального окна, DIP. Окна с более широкой
    /// разметкой (таблицы, монитор серверов) получают горизонтальную прокрутку
    /// внутри содержимого, а не окно шире экрана.
    /// </summary>
    public const double DefaultMaxWidth = 1200;

    /// <summary>Потолок стартовой высоты модального окна, DIP.</summary>
    public const double DefaultMaxHeight = 800;

    /// <summary>
    /// Рекомендованные размеры окна: стартовые <see cref="Width"/>/<see cref="Height"/>,
    /// границы <see cref="MinWidth"/>/<see cref="MinHeight"/>/<see cref="MaxWidth"/>/
    /// <see cref="MaxHeight"/> и признак <see cref="RequiresScrolling"/>.
    /// </summary>
    /// <param name="Width">Стартовая ширина (DIP).</param>
    /// <param name="Height">Стартовая высота (DIP).</param>
    /// <param name="MinWidth">Минимальная ширина (DIP).</param>
    /// <param name="MinHeight">Минимальная высота (DIP).</param>
    /// <param name="MaxWidth">Максимальная ширина по рабочей области (DIP).</param>
    /// <param name="MaxHeight">Максимальная высота по рабочей области (DIP).</param>
    /// <param name="RequiresScrolling">
    /// Истина, если стартовая высота меньше желаемой высоты содержимого:
    /// содержимое должно прокручиваться.
    /// </param>
    public readonly record struct WindowBounds(
        double Width,
        double Height,
        double MinWidth,
        double MinHeight,
        double MaxWidth,
        double MaxHeight,
        bool RequiresScrolling);

    /// <summary>
    /// Считает размеры окна по рабочей области монитора.
    /// </summary>
    /// <param name="designWidth">Желаемая (дизайнерская) ширина окна, DIP.</param>
    /// <param name="designHeight">Желаемая (дизайнерская) высота окна, DIP.</param>
    /// <param name="minWidth">Минимальная ширина окна, DIP (0 — не задана).</param>
    /// <param name="minHeight">Минимальная высота окна, DIP (0 — не задана).</param>
    /// <param name="workWidth">Ширина рабочей области монитора, DIP.</param>
    /// <param name="workHeight">Высота рабочей области монитора, DIP.</param>
    /// <param name="margin">Отступ от краёв рабочей области, DIP.</param>
    public static WindowBounds Recommend(
        double designWidth,
        double designHeight,
        double minWidth,
        double minHeight,
        double workWidth,
        double workHeight,
        double margin = DefaultMargin)
    {
        var maxWidth = MaxSize(workWidth, margin);
        var maxHeight = MaxSize(workHeight, margin);

        // Стартовый размер не больше потолка и не больше рабочей области.
        var desiredWidth = NormalizeDesign(designWidth, DefaultMaxWidth);
        var desiredHeight = NormalizeDesign(designHeight, DefaultMaxHeight);

        var effectiveMinWidth = ClampMin(minWidth, maxWidth);
        var effectiveMinHeight = ClampMin(minHeight, maxHeight);

        var width = Clamp(desiredWidth, effectiveMinWidth, maxWidth);
        var height = Clamp(desiredHeight, effectiveMinHeight, maxHeight);

        // Содержимое выше окна — включается прокрутка. Допуск 0.5 DIP:
        // округление DPI не должно включать прокрутку «на глаз».
        var requiresScrolling = !double.IsNaN(designHeight)
            && designHeight > 0
            && height + 0.5 < designHeight;

        return new WindowBounds(
            width,
            height,
            effectiveMinWidth,
            effectiveMinHeight,
            maxWidth,
            maxHeight,
            requiresScrolling);
    }

    /// <summary>
    /// Максимально допустимый размер по рабочей области. Если рабочая область
    /// неизвестна (0/NaN/бесконечность), ограничение не накладывается.
    /// </summary>
    public static double MaxSize(double workSize, double margin = DefaultMargin)
    {
        if (double.IsNaN(workSize) || double.IsInfinity(workSize) || workSize <= 0)
            return double.PositiveInfinity;

        var effectiveMargin = double.IsNaN(margin) || margin < 0 ? 0 : margin;
        return Math.Max(0, workSize - effectiveMargin);
    }

    private static double NormalizeDesign(double value, double ceiling)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return ceiling;

        return Math.Min(value, ceiling);
    }

    private static double ClampMin(double min, double max)
    {
        if (double.IsNaN(min) || min < 0)
            min = 0;

        return double.IsPositiveInfinity(max) ? min : Math.Min(min, max);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || value <= 0)
            value = min;

        if (!double.IsPositiveInfinity(max) && value > max)
            value = max;

        if (value < min)
            value = min;

        return value;
    }
}
