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
    /// <summary>
    /// Минимальная ширина диалога (совпадает с MinWidth в разметке).
    /// Issue #139 (1.0.1.103): 680 DIP — окно стало заметно шире, чтобы описание
    /// релиза читалось без тесноты; строка кнопок «Перезапустить сейчас» +
    /// «Обновить после закрытия» (230 + 10 + 240) по-прежнему помещается в одну
    /// строку вместе с полями и оформлением окна.
    /// </summary>
    public const double MinWidth = 680;

    /// <summary>Максимальная ширина диалога по содержимому (1.0.1.103: 900).</summary>
    public const double MaxWidth = 900;

    /// <summary>
    /// Минимальная высота диалога, DIP. Ниже опускаться нельзя: окно с заголовком,
    /// иконкой и строкой кнопок становится нечитаемым. Совпадает с MinHeight в
    /// разметке WPF/Avalonia (1.0.1.103: 160).
    /// </summary>
    public const double MinHeight = 160;

    /// <summary>Максимальная высота диалога по содержимому (1.0.1.103: 760).</summary>
    public const double MaxHeight = 760;

    /// <summary>Отступ от краёв рабочей области монитора, DIP.</summary>
    public const double WorkAreaMargin = 24;

    /// <summary>
    /// Ширина вертикальной полосы прокрутки по умолчанию, DIP. Используется,
    /// когда платформа не сообщила собственную ширину (WPF:
    /// <c>SystemParameters.VerticalScrollBarWidth</c>, Avalonia — оценка).
    /// Issue #139 (1.0.1.102).
    /// </summary>
    public const double DefaultScrollBarWidth = 17;

    /// <summary>
    /// Допуск сравнения высот, DIP. Разница в пределах этого значения считается
    /// округлением раскладки/DPI и не должна включать полосу прокрутки.
    /// </summary>
    public const double ScrollBarEpsilon = 0.5;

    /// <summary>
    /// Высота строки кнопок диалога, DIP (совпадает с Height=34 у кнопок в разметке).
    /// </summary>
    public const double ButtonHeight = 34;

    /// <summary>
    /// Межстрочный зазор, который добавляет <c>WrapPanel</c> при переносе кнопок
    /// на новую строку (задаётся отступом панели, DIP).
    /// </summary>
    public const double ButtonRowGap = 8;

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
    /// Начальный размер: по содержимому, но не меньше <see cref="MinWidth"/>/<see cref="MinHeight"/>
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

        var width = double.IsNaN(desiredWidth) ? MinWidth : desiredWidth;

        return new UpdateDialogSize(
            Math.Clamp(width, MinWidth, maxWidth),
            ClampWindowHeight(desiredHeight, workAreaHeight));
    }

    /// <summary>
    /// Итоговая высота окна: не меньше <see cref="MinHeight"/>, не больше
    /// <see cref="MaxHeight"/> и рабочей области минус 2×<see cref="WorkAreaMargin"/>.
    /// Если рабочая область настолько мала, что меньше <see cref="MinHeight"/>,
    /// приоритет у рабочей области — окно должно поместиться на экране, а длинное
    /// содержимое прокручивается внутри (issue #139, 1.0.1.95).
    /// Расчёт высоты переиспользует <see cref="WindowSizeMath.ClampHeight"/>, чтобы
    /// не дублировать клампинг в WPF и Avalonia.
    /// </summary>
    /// <param name="desiredHeight">Желаемая (по содержимому) высота окна, DIP.</param>
    /// <param name="workAreaHeight">Высота рабочей области монитора, DIP; 0/отрицательное —
    /// ограничение по рабочей области неизвестно и не применяется.</param>
    public static double ClampWindowHeight(double desiredHeight, double workAreaHeight)
    {
        var maxHeight = MaxHeight;
        if (workAreaHeight > 0)
            maxHeight = Math.Min(maxHeight, workAreaHeight - 2 * WorkAreaMargin);
        if (maxHeight < 0)
            maxHeight = 0;

        var minHeight = Math.Min(MinHeight, maxHeight);
        var height = double.IsNaN(desiredHeight) ? maxHeight : desiredHeight;
        return WindowSizeMath.ClampHeight(height, minHeight, maxHeight);
    }

    /// <summary>
    /// Высота панели кнопок с учётом переноса <c>WrapPanel</c> по доступной ширине
    /// (issue #139, 1.0.1.95). Именно перенос вызывал обрезание низа: при
    /// <c>SizeToContent="WidthAndHeight"</c> контейнер измерялся с бесконечной
    /// шириной (одна строка), затем ширина клампилась, кнопки переносились, а
    /// высота окна уже была посчитана.
    /// </summary>
    /// <param name="buttonWidths">Ширины кнопок в одной панели, DIP.</param>
    /// <param name="availableWidth">Доступная ширина панели (ширина окна минус
    /// отступы и оформление), DIP; <c><= 0</c> — ширина неизвестна, переноса нет.</param>
    /// <param name="spacing">Горизонтальный отступ между соседними кнопками, DIP.</param>
    /// <param name="rowHeight">Высота одной строки кнопок, DIP.</param>
    /// <param name="rowGap">Вертикальный зазор между строками, DIP.</param>
    /// <returns>Высота панели: <c>rows × rowHeight + (rows − 1) × rowGap</c>;
    /// 0, если кнопок нет.</returns>
    public static double ButtonPanelHeight(
        IEnumerable<double> buttonWidths,
        double availableWidth,
        double spacing,
        double rowHeight,
        double rowGap = ButtonRowGap)
    {
        ArgumentNullException.ThrowIfNull(buttonWidths);

        var rows = 0;
        var rowWidth = 0.0;
        var hasButtons = false;

        foreach (var buttonWidth in buttonWidths)
        {
            var width = buttonWidth > 0 ? buttonWidth : 0;

            if (!hasButtons)
            {
                rows = 1;
                rowWidth = width;
                hasButtons = true;
                continue;
            }

            var withNext = rowWidth + (spacing > 0 ? spacing : 0) + width;
            if (availableWidth > 0 && withNext > availableWidth)
            {
                // Кнопка не помещается в текущую строку — WrapPanel переносит её.
                rows++;
                rowWidth = width;
            }
            else
            {
                rowWidth = withNext;
            }
        }

        if (!hasButtons)
            return 0;

        var height = rows * (rowHeight > 0 ? rowHeight : 0);
        if (rows > 1)
            height += (rows - 1) * (rowGap > 0 ? rowGap : 0);
        return height;
    }

    /// <summary>
    /// True — точку попадания на границу окна нужно трактовать как клиентскую
    /// (запрет изменения размера). Актуально для окна с <c>ResizeMode="NoResize"</c>
    /// и размером, который вычисляется явно: перетаскивание границы уменьшало
    /// ширину, а контент не пересчитывался и обрезался (issue #139).
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

    // ===================== Вертикальная полоса прокрутки (issue #139, 1.0.1.102) =====================

    /// <summary>
    /// Нужна ли вертикальная полоса прокрутки телу диалога. Полоса появляется
    /// только при реальном переполнении: разница высот в пределах
    /// <paramref name="epsilon"/> считается округлением раскладки/DPI и полосу не
    /// включает. Единое решение для WPF и Avalonia.
    /// </summary>
    /// <param name="contentHeight">Измеренная высота содержимого тела, DIP.</param>
    /// <param name="viewportHeight">Доступная высота тела (viewport), DIP.</param>
    /// <param name="epsilon">Допуск сравнения, DIP.</param>
    public static bool ShouldShowVerticalScrollBar(
        double contentHeight, double viewportHeight, double epsilon = ScrollBarEpsilon)
    {
        if (double.IsNaN(contentHeight) || double.IsNaN(viewportHeight))
            return false;

        var tolerance = epsilon > 0 ? epsilon : 0;
        return contentHeight > viewportHeight + tolerance;
    }

    /// <summary>
    /// Ширина, которую полоса прокрутки реально занимает у тела диалога:
    /// 0, пока полоса скрыта; ширина полосы, когда она видима. Отрицательная
    /// или неизвестная ширина трактуется как 0.
    /// </summary>
    public static double ScrollBarWidth(bool visible, double scrollBarWidth)
        => visible && scrollBarWidth > 0 ? scrollBarWidth : 0;

    /// <summary>
    /// Ширина, по которой нужно измерять тело при решении о полосе. Если полоса
    /// учитывается, из доступной ширины вычитается её ширина — тогда перенос
    /// текста уже соответствует состоянию «полоса видима» и после её появления
    /// высота не растёт (нет обратной связи «полоса → перенос → полоса»).
    /// </summary>
    /// <param name="bodyWidth">Доступная ширина тела без полосы, DIP.</param>
    /// <param name="scrollBarVisible">Резервировать ли ширину полосы при измерении.</param>
    /// <param name="scrollBarWidth">Ширина полосы, DIP.</param>
    public static double MeasureContentWidth(
        double bodyWidth, bool scrollBarVisible, double scrollBarWidth = DefaultScrollBarWidth)
    {
        var width = bodyWidth > 0 ? bodyWidth : 0;
        return Math.Max(0, width - ScrollBarWidth(scrollBarVisible, scrollBarWidth));
    }

    /// <summary>
    /// Высота, доступная телу диалога для прокрутки (viewport): высота клиентской
    /// области минус внешние отступы, высота панели кнопок и зазор над ней.
    /// Панель кнопок всегда закреплена снизу и в прокрутке не участвует.
    /// </summary>
    /// <param name="clientHeight">Высота клиентской области окна, DIP.</param>
    /// <param name="verticalPadding">Суммарные внешние отступы тела по вертикали, DIP.</param>
    /// <param name="buttonPanelHeight">Высота панели кнопок, DIP.</param>
    /// <param name="buttonGap">Зазор между телом и панелью кнопок, DIP.</param>
    public static double MeasureContentHeight(
        double clientHeight,
        double verticalPadding,
        double buttonPanelHeight,
        double buttonGap)
    {
        var height = clientHeight > 0 ? clientHeight : 0;
        return Math.Max(0,
            height
            - (verticalPadding > 0 ? verticalPadding : 0)
            - (buttonPanelHeight > 0 ? buttonPanelHeight : 0)
            - (buttonGap > 0 ? buttonGap : 0));
    }
}
