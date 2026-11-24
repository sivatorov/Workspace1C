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
    /// Issue #146 (1.0.1.118): профиль высот диалогового окна. Семантика:
    /// <c>MinHeight < DefaultHeight = ContentHeight</c>.
    /// <para>
    /// <c>DefaultHeight</c> — стартовая высота окна и одновременно высота, при которой
    /// содержимое (панель операций + пояснение) помещается без прокрутки; запас сверх
    /// содержимого не закладывается. <c>MinHeight</c> — нижняя граница, заметно ниже
    /// <c>DefaultHeight</c>: окно можно уменьшать, не упираясь в «минимальный» размер,
    /// равный стартовому (жалоба 7OH в issue #146). Прокрутка
    /// (<see cref="DialogSize.RequiresScrolling"/>) включается только после клампа по
    /// маленькой рабочей области.
    /// </para>
    /// </summary>
    /// <param name="DefaultHeight">Высота окна по умолчанию (по содержимому), DIP.</param>
    /// <param name="MinHeight">Минимальная высота окна (нижняя граница), DIP.</param>
    /// <param name="ContentHeight">Высота, при которой содержимое помещается без прокрутки, DIP.</param>
    public readonly record struct DialogProfile(
        double DefaultHeight,
        double MinHeight,
        double ContentHeight);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Настройка меню утилиты» — 680 DIP по содержимому,
    /// минимальная высота 440 DIP. Пояснение перенесено в конец прокручиваемого
    /// содержимого, поэтому запас перед ним не нужен.
    /// </summary>
    public static readonly DialogProfile UtilitiesEditor = new(680, 440, 680);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Настройка меню трея» — 680 DIP по содержимому,
    /// минимальная высота 440 DIP (самый длинный текст пояснения).
    /// </summary>
    public static readonly DialogProfile TrayEditor = new(680, 440, 680);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Контекстное меню» — 540 DIP по содержимому,
    /// минимальная высота 400 DIP (панель операций короче, чем у «Утилит»).
    /// </summary>
    public static readonly DialogProfile ContextMenuEditor = new(540, 400, 540);

    /// <summary>
    /// Issue #146 (1.0.1.118): окно «Мои программы» — 600 DIP по содержимому,
    /// минимальная высота 440 DIP.
    /// </summary>
    public static readonly DialogProfile CustomPrograms = new(600, 440, 600);

    /// <summary>
    /// Issue #146 (1.0.1.115): рекомендуемые размеры диалогового окна с учётом
    /// рабочей области монитора. <see cref="RequiresScrolling"/> = true только
    /// тогда, когда рабочая область не позволяет окну достичь высоты содержимого
    /// (<c>contentHeight</c>), то есть на действительно маленьком экране.
    /// </summary>
    public readonly record struct DialogSize(
        double Height,
        double MinHeight,
        double MaxHeight,
        bool RequiresScrolling);

    /// <summary>
    /// Issue #146 (1.0.1.116): рекомендованные размеры окна по его профилю. Профиль
    /// избавляет окна от «магических чисел» и гарантирует, что WPF и Avalonia
    /// считают одинаково.
    /// </summary>
    /// <param name="profile">Профиль высот окна (например, <see cref="UtilitiesEditor"/>).</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP.</param>
    /// <param name="margin">Отступ от краёв рабочей области, DIP.</param>
    public static DialogSize Recommend(
        DialogProfile profile,
        double workAreaHeight,
        double margin = DefaultMargin) =>
        Recommend(
            profile.DefaultHeight,
            profile.MinHeight,
            profile.ContentHeight,
            workAreaHeight,
            margin);

    /// <summary>
    /// Считает итоговые размеры окна: <c>Height</c> клампится в рабочую область,
    /// <c>MinHeight</c> не превышает её верхнюю границу, а <c>RequiresScrolling</c>
    /// сообщает, что содержимое не помещается и нужна прокрутка (аварийный режим).
    /// </summary>
    /// <param name="requestedHeight">Желаемая высота окна (дефолт из XAML/кода).</param>
    /// <param name="preferredMinHeight">Желаемая минимальная высота окна.</param>
    /// <param name="contentHeight">Высота, при которой содержимое помещается без прокрутки.</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP.</param>
    /// <param name="margin">Отступ от краёв рабочей области, DIP.</param>
    public static DialogSize Recommend(
        double requestedHeight,
        double preferredMinHeight,
        double contentHeight,
        double workAreaHeight,
        double margin = DefaultMargin)
    {
        var max = MaxHeight(workAreaHeight, margin);
        var min = ClampMinHeight(preferredMinHeight, workAreaHeight, margin);
        var height = ClampHeight(requestedHeight, min, workAreaHeight, margin);

        if (double.IsNaN(contentHeight) || contentHeight <= 0)
            contentHeight = height;

        // Допуск 0.5 DIP: округление DPI не должно включать прокрутку «на глаз».
        return new DialogSize(height, min, max, height + 0.5 < contentHeight);
    }

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
    /// Клампит минимальную высоту окна верхней границей рабочей области: на очень
    /// маленьком экране <c>MinHeight</c> не должен превышать <c>MaxHeight</c>, иначе
    /// в WPF/Avalonia нижняя граница побеждает верхнюю и окно всё равно выходит за
    /// экран (issue #146). Неизвестная рабочая область (0/NaN/бесконечность)
    /// оставляет минимум как есть; отрицательный или NaN минимум считается нулевым.
    /// </summary>
    public static double ClampMinHeight(
        double minHeight,
        double workAreaHeight,
        double margin = DefaultMargin)
    {
        if (double.IsNaN(minHeight) || minHeight < 0)
            minHeight = 0;

        var max = MaxHeight(workAreaHeight, margin);
        return double.IsPositiveInfinity(max) ? minHeight : Math.Min(minHeight, max);
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
