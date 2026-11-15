using System;
using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// План отрисовки сетки списка баз (issue #130): какие линии рисовать и с какими
/// параметрами. Чистая логика без UI — используется и WPF-версией, и Avalonia,
/// поэтому покрыта юнит-тестами (<c>Issue130GridLinesTests</c>).
/// </summary>
public sealed class GridLinesPlan
{
    public GridLinesPlan(
        bool showHorizontalLine,
        IReadOnlyList<int> verticalBoundaryColumns,
        double thickness,
        double opacity)
    {
        ShowHorizontalLine = showHorizontalLine;
        VerticalBoundaryColumns = verticalBoundaryColumns ?? Array.Empty<int>();
        Thickness = thickness;
        Opacity = opacity;
    }

    /// <summary>Рисовать горизонтальную линию по нижней границе строки.</summary>
    public bool ShowHorizontalLine { get; }

    /// <summary>
    /// Индексы колонок, у правого края которых рисуется вертикальная линия.
    /// Последняя видимая колонка в список не входит — наружная рамка не нужна.
    /// </summary>
    public IReadOnlyList<int> VerticalBoundaryColumns { get; }

    /// <summary>Толщина линии в аппаратно-независимых пикселях.</summary>
    public double Thickness { get; }

    /// <summary>Прозрачность линии: в компактном режиме линии чуть контрастнее.</summary>
    public double Opacity { get; }

    /// <summary>Есть ли хотя бы одна вертикальная линия.</summary>
    public bool ShowVerticalLines => VerticalBoundaryColumns.Count > 0;

    /// <summary>Нужно ли вообще что-то рисовать.</summary>
    public bool HasAnyLines => ShowHorizontalLine || ShowVerticalLines;
}

/// <summary>
/// Планировщик линий сетки списка баз (issue #130). По настройкам
/// «Выводить линии — по горизонтали/по вертикали» и составу видимых колонок
/// вычисляет, какие линии рисовать. По умолчанию обе настройки выключены —
/// поведение списка не меняется.
/// </summary>
public static class GridLinesPlanner
{
    /// <summary>
    /// Базовая толщина линии: ровно один физический пиксель. В аппаратно-независимых
    /// точках это значение делится на масштаб DPI (см. <see cref="ResolveLineThickness"/>),
    /// иначе на 125/150 % линия занимает две полупрозрачные колонки пикселей и читается
    /// как «двойная» (issue #130, замечание 7OH).
    /// </summary>
    public const double LineThickness = 1.0;

    /// <summary>Прозрачность линий в обычном режиме — сетка не должна спорить с текстом.</summary>
    public const double NormalOpacity = 0.75;

    /// <summary>Прозрачность линий в компактном режиме — строки плотнее, линия заметнее.</summary>
    public const double CompactOpacity = 1.0;

    /// <summary>
    /// Толщина линии в аппаратно-независимых точках, дающая ровно один физический
    /// пиксель при заданном масштабе DPI: <c>1 / dpiScale</c>.
    /// </summary>
    public static double ResolveLineThickness(double dpiScale)
        => dpiScale > 0 ? LineThickness / dpiScale : LineThickness;

    /// <summary>Прозрачность линии по режиму плотности (та же, что в <see cref="Resolve"/>).</summary>
    public static double ResolveOpacity(bool compactMode)
        => compactMode ? CompactOpacity : NormalOpacity;

    /// <summary>
    /// Округляет координату к физическому пикселю: <c>round(value * scale) / scale</c>.
    /// Без этого дробные позиции (звёздная колонка «Название») дают полупрозрачные
    /// края линии и визуально «две линии» на 125/150 % DPI (issue #130).
    /// </summary>
    public static double SnapToPhysicalPixel(double value, double dpiScale)
        => dpiScale > 0 ? Math.Round(value * dpiScale) / dpiScale : value;

    /// <summary>
    /// Позиции вертикальных линий (X, в DIP) для единого оверлея сетки (issue #130).
    /// Один источник истины — ширины колонок заголовка: позиция границы = сумма ширин
    /// предыдущих колонок, округлённая к физическому пикселю. Линия рисуется только
    /// между двумя видимыми колонками; после последней видимой линии нет (наружной
    /// рамки не делаем). Скрытые колонки нулевой ширины линий не дают.
    /// </summary>
    /// <param name="columnWidths">Фактические ширины колонок слева направо.</param>
    /// <param name="visibleColumns">Видимость колонок слева направо.</param>
    /// <param name="showVertical">Настройка «Выводить линии — по вертикали».</param>
    /// <param name="dpiScale">Масштаб DPI (1.0, 1.25, 1.5, 2.0…).</param>
    public static IReadOnlyList<double> ResolveVerticalOffsets(
        IReadOnlyList<double>? columnWidths,
        IReadOnlyList<bool>? visibleColumns,
        bool showVertical,
        double dpiScale)
    {
        var offsets = new List<double>();
        if (!showVertical || columnWidths is null || visibleColumns is null)
            return offsets;

        var count = Math.Min(columnWidths.Count, visibleColumns.Count);
        if (count == 0)
            return offsets;

        var lastVisible = -1;
        for (var i = 0; i < count; i++)
            if (visibleColumns[i])
                lastVisible = i;
        if (lastVisible <= 0)
            return offsets;

        double x = 0;
        for (var i = 0; i < count; i++)
        {
            x += Math.Max(0, columnWidths[i]);
            if (i >= lastVisible)
                break;
            if (visibleColumns[i] && HasVisibleAfter(visibleColumns, i + 1, count))
                offsets.Add(SnapToPhysicalPixel(x, dpiScale));
        }

        return offsets;
    }

    private static bool HasVisibleAfter(IReadOnlyList<bool> visibleColumns, int start, int count)
    {
        for (var i = start; i < count; i++)
            if (visibleColumns[i])
                return true;
        return false;
    }

    /// <summary>
    /// Оставляет низы только тех реализованных строк, которые реально видны
    /// (issue #130, замечание 7OH): строки внутри свёрнутой группы остаются в
    /// визуальном дереве с координатами развёрнутого состояния, но не видны.
    /// Без фильтра после сворачивания группы низ такой строки продолжал удлинять
    /// линии, и они «уходили в самый низ». Недостающие значения видимости
    /// считаются видимыми (безопасный fallback); <c>NaN</c> и
    /// отрицательные низы (строки выше вьюпорта) отбрасываются.
    /// </summary>
    /// <param name="rowBottoms">Низы реализованных строк в координатах оверлея.</param>
    /// <param name="rowVisibility">
    /// Видимость строк в том же порядке; <c>null</c> — считать все строки видимыми.
    /// </param>
    public static IReadOnlyList<double> FilterVisibleRowBottoms(
        IReadOnlyList<double>? rowBottoms,
        IReadOnlyList<bool>? rowVisibility)
    {
        var result = new List<double>();
        if (rowBottoms is null)
            return result;

        for (var i = 0; i < rowBottoms.Count; i++)
        {
            if (rowVisibility is not null && i < rowVisibility.Count && !rowVisibility[i])
                continue;
            var bottom = rowBottoms[i];
            if (double.IsNaN(bottom) || bottom < 0)
                continue;
            result.Add(bottom);
        }

        return result;
    }

    /// <summary>
    /// Низ последней реализованной строки списка (issue #130, замечание 7OH): максимум
    /// из низа заголовка и низов строк. Строки выше вьюпорта дают отрицательные
    /// координаты и на максимум не влияют. Отрицательные и <see cref="double.NaN"/>
    /// значения игнорируются; заголовок тоже может отсутствовать.
    /// </summary>
    /// <param name="headerBottom">Низ заголовка в координатах оверлея; <c>null</c> — нет данных.</param>
    /// <param name="rowBottoms">Низы реализованных строк баз и групп в координатах оверлея.</param>
    public static double ResolveBottomOfRows(double? headerBottom, IReadOnlyList<double>? rowBottoms)
    {
        var extent = headerBottom is { } header && header > 0 && !double.IsNaN(header) ? header : 0;
        if (rowBottoms is not null)
        {
            for (var i = 0; i < rowBottoms.Count; i++)
            {
                var bottom = rowBottoms[i];
                if (!double.IsNaN(bottom) && bottom > extent)
                    extent = bottom;
            }
        }
        return extent;
    }

    /// <summary>
    /// Высота вертикальных линий сетки (issue #130): линии заканчиваются низом последней
    /// реализованной строки списка (включая заголовок), а не растягиваются до низа окна.
    /// Пустой список — линии заканчиваются низом заголовка. Высота не превышает доступную
    /// (прокрутка/длинный список) и округляется к физическому пикселю, чтобы торец линии
    /// не был полупрозрачным на 125/150 % DPI. Если разметка ещё не готова (нет ни
    /// заголовка, ни строк), возвращается <paramref name="availableHeight"/> — прежнее
    /// поведение как безопасный fallback, чтобы линии не исчезали при неготовой разметке.
    /// </summary>
    /// <param name="headerBottom">Низ заголовка в координатах оверлея; <c>null</c> — нет данных.</param>
    /// <param name="rowBottoms">Низы реализованных строк баз и групп в координатах оверлея.</param>
    /// <param name="availableHeight">Доступная высота оверлея (вьюпорт списка).</param>
    /// <param name="dpiScale">Масштаб DPI (1.0, 1.25, 1.5, 2.0…).</param>
    public static double ResolveVerticalExtent(
        double? headerBottom,
        IReadOnlyList<double>? rowBottoms,
        double availableHeight,
        double dpiScale)
    {
        // Нет доступной высоты (или она некорректна) — рисовать нечего.
        if (!(availableHeight > 0))
            return 0;

        var extent = ResolveBottomOfRows(headerBottom, rowBottoms);
        // Разметка ещё не готова: ни заголовка, ни строк — сохраняем прежнее поведение.
        if (extent <= 0)
            return availableHeight;

        extent = Math.Min(extent, availableHeight);
        var snapped = SnapToPhysicalPixel(extent, dpiScale);
        // После округления линия тоньше физического пикселя не видна — не создаём её.
        var scale = dpiScale > 0 ? dpiScale : 1.0;
        return snapped >= 1.0 / scale ? snapped : 0;
    }

    /// <summary>
    /// Перегрузка <c>ResolveVerticalExtent</c> с учётом видимости строк
    /// (issue #130, замечание 7OH). Низы строк внутри
    /// свёрнутой группы не участвуют в расчёте: после сворачивания группы они
    /// остаются в визуальном дереве с прежними координатами, и линии сохраняли
    /// высоту развёрнутого дерева — «уходили в самый низ». <paramref name="rowVisibility"/>
    /// = <c>null</c> — все строки видимы (поведение прежней перегрузки).
    /// </summary>
    /// <param name="headerBottom">Низ заголовка в координатах оверлея; <c>null</c> — нет данных.</param>
    /// <param name="rowBottoms">Низы реализованных строк баз и групп в координатах оверлея.</param>
    /// <param name="rowVisibility">Видимость строк в том же порядке; <c>null</c> — все видимы.</param>
    /// <param name="availableHeight">Доступная высота оверлея (вьюпорт списка).</param>
    /// <param name="dpiScale">Масштаб DPI (1.0, 1.25, 1.5, 2.0…).</param>
    public static double ResolveVerticalExtent(
        double? headerBottom,
        IReadOnlyList<double>? rowBottoms,
        IReadOnlyList<bool>? rowVisibility,
        double availableHeight,
        double dpiScale)
        => ResolveVerticalExtent(
            headerBottom,
            FilterVisibleRowBottoms(rowBottoms, rowVisibility),
            availableHeight,
            dpiScale);

    /// <summary>
    /// Строит план линий сетки.
    /// </summary>
    /// <param name="showHorizontal">Настройка «Выводить линии — по горизонтали».</param>
    /// <param name="showVertical">Настройка «Выводить линии — по вертикали».</param>
    /// <param name="compactMode">Компактный режим интерфейса.</param>
    /// <param name="visibleColumns">
    /// Видимость колонок слева направо. Скрытые колонки (нулевой ширины) линий не дают;
    /// граница рисуется только между двумя видимыми колонками.
    /// </param>
    public static GridLinesPlan Resolve(
        bool showHorizontal,
        bool showVertical,
        bool compactMode,
        IReadOnlyList<bool>? visibleColumns)
    {
        var boundaries = new List<int>();
        if (showVertical && visibleColumns is { Count: > 0 })
        {
            // Последняя видимая колонка: после неё линия не нужна — это внешняя
            // граница таблицы, а не разделитель между столбцами.
            var lastVisible = -1;
            for (var i = 0; i < visibleColumns.Count; i++)
                if (visibleColumns[i])
                    lastVisible = i;

            for (var i = 0; i < visibleColumns.Count - 1 && i < lastVisible; i++)
                if (visibleColumns[i])
                    boundaries.Add(i);
        }

        return new GridLinesPlan(
            showHorizontal,
            boundaries,
            LineThickness,
            ResolveOpacity(compactMode));
    }
}
