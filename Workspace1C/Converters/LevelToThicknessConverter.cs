using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Workspace1C.Converters;

/// <summary>
/// Преобразует уровень вложенности дерева (int) в отступ слева (Thickness).
/// <list type="bullet">
/// <item>Без parameter (или не "base"): Level * IndentStep — для Expander групп.</item>
/// <item>parameter = "base": (Level - 1) * IndentStep + ExpanderWidth —
/// для блока ★/📌/название, чтобы ★ совпадала по горизонтали с иконкой папки
/// родительской группы (иконка идёт сразу после кнопки разворота шириной ExpanderWidth).</item>
/// </list>
/// </summary>
public class LevelToThicknessConverter : IMultiValueConverter
{
    /// <summary>Шаг отступа на один уровень вложенности (px). Синхронизирован с GroupTreeIndentStep.</summary>
    public const double IndentStep = 18.0;

    /// <summary>Ширина кнопки разворота группы (px), см. Expander Width в MainWindow.xaml.</summary>
    public const double ExpanderWidth = 26.0;

    /// <summary>Небольшой зазор слева до начала подсветки выбранной базы (px), чтобы фон
    /// выделения начинался немного раньше значка «избранное» (см. параметр "base-hl").</summary>
    public const double HighlightGap = 8.0;

    /// <summary>
    /// Вертикальный отступ полосы-идентификатора группы (px). Полоса рисуется внутри
    /// скруглённой рамки строки; без отступа её прямые торцы выходили за скруглённые
    /// углы и «торчали» из рамки (issue #145).
    /// </summary>
    public const double GroupStripeVerticalInset = 4.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var level = values.Length > 0 && values[0] is int i ? i : 0;
        var param = parameter as string;

        double offset;
        if (string.Equals(param, "base", StringComparison.OrdinalIgnoreCase))
        {
            // База на уровне L — внутри группы уровня L-1.
            // Expander группы: margin (L-1)*step, ширина ExpanderWidth → иконка папки начинается
            // на (L-1)*step + ExpanderWidth. Тот же отступ у блока ★/📌/название.
            var parentLevel = level > 0 ? level - 1 : 0;
            offset = parentLevel * IndentStep + ExpanderWidth;
        }
        else if (string.Equals(param, "base-hl", StringComparison.OrdinalIgnoreCase))
        {
            // Подсветка выбранной базы: тот же сдвиг, что у блока ★/📌/названия, но с небольшим
            // зазором HighlightGap, чтобы выделение начиналось немного раньше значка «избранное»
            // и не уходило влево на всю длину строки.
            var parentLevel = level > 0 ? level - 1 : 0;
            offset = parentLevel * IndentStep + ExpanderWidth - HighlightGap;
        }
        else if (string.Equals(param, "group-hl", StringComparison.OrdinalIgnoreCase))
        {
            // Подсветка/фон заголовка группы: тот же сдвиг, что у названия ("group"), но с зазором
            // HighlightGap, чтобы карточка группы начиналась сразу после кнопки свернуть/развернуть
            // (оформление 0.3.5.1) и не заходила под кнопку и левее неё.
            offset = level * IndentStep + ExpanderWidth - HighlightGap;
        }
        else if (string.Equals(param, "group-stripe", StringComparison.OrdinalIgnoreCase))
        {
            // Аудит UI P1-2 / issue #145: вертикальная полоса 3 px исходным цветом
            // группы. Тот же сдвиг, что у фона строки ("group-hl"), плюс 2 px —
            // полоса рисуется внутри рамки толщиной 2. Вертикальный отступ не даёт
            // торцам полосы выходить за скруглённые углы рамки.
            offset = level * IndentStep + ExpanderWidth - HighlightGap + 2;
            return new Thickness(offset, GroupStripeVerticalInset, 0, GroupStripeVerticalInset);
        }
        else if (string.Equals(param, "group", StringComparison.OrdinalIgnoreCase))
        {
            // Заголовок группы: сдвиг названия по уровню + ширина расширителя,
            // чтобы название начиналось сразу после кнопки разворота.
            offset = level * IndentStep + ExpanderWidth;
        }
        else
        {
            offset = level * IndentStep;
        }

        return new Thickness(offset, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
