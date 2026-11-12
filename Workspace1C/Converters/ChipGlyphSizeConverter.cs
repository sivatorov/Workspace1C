using System;
using System.Globalization;
using System.Windows.Data;
using Workspace1C.Themes;

namespace Workspace1C.Converters;

/// <summary>
/// Размер значка в кружках запуска по активному набору иконок (issue #125):
/// «Предприятие» у Fluent и Material Design и «Конфигуратор» у Material Design
/// рисуются крупнее внутри того же кружка (эталон — набор Windows XP);
/// остальные наборы и глифы — без изменений. Правила масштаба — в
/// <see cref="ChipGlyphScale"/>.
/// <para>values[0] — активный набор (<see cref="IconSetState.Current"/>);
/// values[1] (необязательный) — флаг компактного режима строки.
/// ConverterParameter — спецификация «Вид:Обычный:Компактный» (компактный
/// размер можно опустить), где Вид — Enterprise / Configurator / None.</para>
/// </summary>
public sealed class ChipGlyphSizeConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = ChipGlyphKind.None;
        double normal = 16, compact = normal;
        if (parameter is string spec)
        {
            var parts = spec.Split(':');
            if (parts.Length > 0 && Enum.TryParse(parts[0], out ChipGlyphKind parsed))
                kind = parsed;
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                normal = n;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
                compact = c;
        }

        var set = values.Length > 0 && values[0] is IconSet s ? s : IconSet.Fluent;
        var isCompact = values.Length > 1 && values[1] is true;
        return ChipGlyphScale.Scale(isCompact ? compact : normal, set, kind);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
