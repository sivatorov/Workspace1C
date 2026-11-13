using System;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика видимости кнопок колонки «Действия» (issue #128 «Действия
/// при наведении»): при включённой настройке <c>ActionsHoverOnly</c> кнопки
/// в строке базы показываются только когда курсор над строкой; кнопки запуска
/// (чипы-индикаторы) остаются видимыми без наведения, пока у базы запущен
/// Конфигуратор или Предприятие — «в колонке сразу видно, где запущено что-то,
/// но запуск остаётся доступен». При выключенной настройке — как раньше,
/// кнопки видны всегда. Не зависит от платформы и UI — покрыта юнит-тестами;
/// WPF-разметка (MultiDataTrigger) и Avalonia-конвертер — обёртки над ней.
/// </summary>
public static class ActionsHoverPlanner
{
    /// <summary>
    /// Видима ли кнопка режима запуска (Предприятие/Конфигуратор) в строке базы:
    /// опция выключена — всегда видна; включена — при наведении на строку или
    /// когда соответствующий режим запущен (чип-индикатор без наведения).
    /// </summary>
    /// <param name="hoverOnlyEnabled">Включена ли настройка ActionsHoverOnly.</param>
    /// <param name="rowHovered">Курсор сейчас над строкой (hover).</param>
    /// <param name="modeRunning">Запущен ли соответствующий режим у этой базы.</param>
    public static bool IsLaunchActionVisible(bool hoverOnlyEnabled, bool rowHovered, bool modeRunning)
        => !hoverOnlyEnabled || rowHovered || modeRunning;

    /// <summary>
    /// Видима ли служебная кнопка колонки «Действия» (очистка кеша): опция
    /// выключена — всегда видна; включена — только при наведении на строку.
    /// </summary>
    /// <param name="hoverOnlyEnabled">Включена ли настройка ActionsHoverOnly.</param>
    /// <param name="rowHovered">Курсор сейчас над строкой (hover).</param>
    public static bool IsUtilityActionVisible(bool hoverOnlyEnabled, bool rowHovered)
        => !hoverOnlyEnabled || rowHovered;

    /// <summary>
    /// Видима ли кнопка в строке ГРУППЫ (папки) колонки «Действия» («править»/
    /// «удалить») (issue #128, v1.0.1.60, 7OH «для папок подвиг повторить»):
    /// опция выключена — как раньше, всегда видна; включена — только когда курсор
    /// над строкой группы. У групп нет кнопок запуска (Предприятие/Конфигуратор)
    /// и чипов-индикаторов запущенных режимов, поэтому «видимой без наведения»
    /// кнопки у папки не бывает вовсе — правило совпадает со служебной кнопкой
    /// строки базы (<see cref="IsUtilityActionVisible"/>); отдельный метод нужен,
    /// чтобы тесты и исходники обеих платформ выражали намерение про папки явно.
    /// </summary>
    /// <param name="hoverOnlyEnabled">Включена ли настройка ActionsHoverOnly.</param>
    /// <param name="rowHovered">Курсор сейчас над строкой группы (hover).</param>
    public static bool IsGroupActionVisible(bool hoverOnlyEnabled, bool rowHovered)
        => IsUtilityActionVisible(hoverOnlyEnabled, rowHovered);
}
