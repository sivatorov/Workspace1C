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
    /// <summary>Толщина линии сетки (1 px в обеих платформах).</summary>
    public const double LineThickness = 1.0;

    /// <summary>Прозрачность линий в обычном режиме — сетка не должна спорить с текстом.</summary>
    public const double NormalOpacity = 0.75;

    /// <summary>Прозрачность линий в компактном режиме — строки плотнее, линия заметнее.</summary>
    public const double CompactOpacity = 1.0;

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
            compactMode ? CompactOpacity : NormalOpacity);
    }
}
