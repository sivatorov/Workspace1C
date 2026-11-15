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
