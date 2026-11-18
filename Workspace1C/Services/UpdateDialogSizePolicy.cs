using System;
using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Размер диалога обновления в DIP (issue #139, 1.0.1.84).
/// </summary>
public readonly record struct UpdateDialogSize(double Width, double Height);

/// <summary>
/// Чистая (без WPF/Avalonia) политика размеров окна «Доступна новая версия…»
/// (issue #139, 1.0.1.84). Окно подгоняется под содержимое, но не должно
/// выходить за рабочую область и не должно ужиматься уже минимальной ширины:
/// при клике по границе WPF/оконный менеджер уменьшал ширину, а контент
/// обрезался справа (оставалась видна только кнопка «Перезапустить»).
/// </summary>
public static class UpdateDialogSizePolicy
{
    /// <summary>Минимальная ширина диалога (совпадает с MinWidth в разметке).</summary>
    public const double MinWidth = 440;

    /// <summary>Максимальная ширина диалога по содержимому.</summary>
    public const double MaxWidth = 760;

    /// <summary>Максимальная высота диалога по содержимому.</summary>
    public const double MaxHeight = 640;

    /// <summary>Отступ от краёв рабочей области монитора, DIP.</summary>
    public const double WorkAreaMargin = 24;

    // Коды HT* из WM_NCHITTEST (WinUser.h): границы окна.
    public const int HtClient = 1;
    public const int HtCaption = 2;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    /// <summary>
    /// Начальный размер: по содержимому, но не меньше <see cref="MinWidth"/>
    /// и не больше <see cref="MaxWidth"/>/<see cref="MaxHeight"/> и рабочей
    /// области минус 2×<see cref="WorkAreaMargin"/>.
    /// </summary>
    /// <param name="desiredWidth">Желаемая (по содержимому) ширина, DIP.</param>
    /// <param name="desiredHeight">Желаемая (по содержимому) высота, DIP.</param>
    /// <param name="workAreaWidth">Ширина рабочей области монитора, DIP; 0/отрицательное —
    /// ограничение по рабочей области неизвестно и не применяется.</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP.</param>
    public static UpdateDialogSize Clamp(
        double desiredWidth, double desiredHeight,
        double workAreaWidth, double workAreaHeight)
    {
        var maxWidth = MaxWidth;
        if (workAreaWidth > 0)
            maxWidth = Math.Min(maxWidth, workAreaWidth - 2 * WorkAreaMargin);
        // Минимальная ширина важнее размера экрана: иначе контент обрежется.
        if (maxWidth < MinWidth)
            maxWidth = MinWidth;

        var maxHeight = MaxHeight;
        if (workAreaHeight > 0)
            maxHeight = Math.Min(maxHeight, workAreaHeight - 2 * WorkAreaMargin);
        if (maxHeight < 0)
            maxHeight = 0;

        var width = double.IsNaN(desiredWidth) ? MinWidth : desiredWidth;
        var height = double.IsNaN(desiredHeight) ? maxHeight : desiredHeight;

        return new UpdateDialogSize(
            Math.Clamp(width, MinWidth, maxWidth),
            Math.Clamp(height, 0, maxHeight));
    }

    /// <summary>
    /// True — точку попадания на границу окна нужно трактовать как клиентскую
    /// (запрет изменения размера). Актуально только для окна с
    /// <c>SizeToContent="WidthAndHeight"</c> и <c>ResizeMode="NoResize"</c>:
    /// перетаскивание границы уменьшало ширину, а контент не пересчитывался.
    /// Заголовок (<see cref="HtCaption"/>) и клиентская область
    /// (<see cref="HtClient"/>) не блокируются.
    /// </summary>
    public static bool ShouldBlockResize(int hitTestCode, bool sizeToContent, bool noResize)
        => sizeToContent
           && noResize
           && IsBorderHitTestCode(hitTestCode);

    /// <summary>
    /// True — код <c>WM_NCHITTEST</c> соответствует границе окна
    /// (<c>HTLEFT</c>…<c>HTBOTTOMRIGHT</c>, коды 10–17). Заголовок
    /// (<see cref="HtCaption"/>) и клиентская область (<see cref="HtClient"/>)
    /// границами не считаются.
    /// </summary>
    public static bool IsBorderHitTestCode(int hitTestCode)
        => hitTestCode >= HtLeft && hitTestCode <= HtBottomRight;

    /// <summary>
    /// Минимальная ширина контента по ширинам кнопок и отступам: сумма ширин
    /// кнопок + <paramref name="spacing"/> между соседними + <paramref name="chrome"/>
    /// (иконка, поля, рамка окна).
    /// </summary>
    public static double ContentMinWidth(
        IEnumerable<double> buttonWidths, double spacing, double chrome)
    {
        ArgumentNullException.ThrowIfNull(buttonWidths);

        double total = 0;
        var count = 0;
        foreach (var width in buttonWidths)
        {
            total += width;
            count++;
        }

        return total + spacing * Math.Max(0, count - 1) + chrome;
    }
}
