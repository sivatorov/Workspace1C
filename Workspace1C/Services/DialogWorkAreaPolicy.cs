using System;

namespace Workspace1C.Services;

/// <summary>
/// Issue #146 (1.0.1.106): общая политика размеров диалоговых окон по рабочей области
/// монитора. Окна правки меню («Утилиты», «Меню трея», «Мои программы») имели
/// фиксированную высоту 560–580 DIP и на экранах 768 px с масштабированием не
/// помещались — поясняющий текст уходил за нижнюю границу.
/// <para>
/// Политика чистая (без WPF/Avalonia), поэтому обе оболочки считают одинаково и
/// поведение покрыто тестами без запуска окон.
/// </para>
/// </summary>
public static class DialogWorkAreaPolicy
{
    /// <summary>Отступ от краёв рабочей области по умолчанию, DIP.</summary>
    public const double DefaultMargin = 48;

    /// <summary>
    /// Максимально допустимая высота окна в рабочей области. Если рабочая область
    /// неизвестна (0/NaN/бесконечность), ограничение не накладывается.
    /// </summary>
    public static double MaxHeight(double workAreaHeight, double margin = DefaultMargin)
    {
        if (double.IsNaN(workAreaHeight) || double.IsInfinity(workAreaHeight) || workAreaHeight <= 0)
            return double.PositiveInfinity;

        var effectiveMargin = double.IsNaN(margin) || margin < 0 ? 0 : margin;
        return Math.Max(0, workAreaHeight - effectiveMargin);
    }

    /// <summary>
    /// Клампит желаемую высоту окна в <c>[minHeight, maxHeight]</c>, где
    /// <c>maxHeight</c> — рабочая область минус отступ. На очень маленьком экране
    /// приоритет отдаётся тому, чтобы окно целиком помещалось: верхняя граница
    /// побеждает нижнюю.
    /// </summary>
    public static double ClampHeight(
        double requested,
        double minHeight,
        double workAreaHeight,
        double margin = DefaultMargin)
    {
        if (double.IsNaN(requested) || requested <= 0)
            requested = minHeight;
        if (double.IsNaN(minHeight) || minHeight < 0)
            minHeight = 0;

        var max = MaxHeight(workAreaHeight, margin);
        if (double.IsPositiveInfinity(max))
            return Math.Max(requested, minHeight);

        var lowerBound = Math.Min(minHeight, max);
        return Math.Min(Math.Max(requested, lowerBound), max);
    }
}
