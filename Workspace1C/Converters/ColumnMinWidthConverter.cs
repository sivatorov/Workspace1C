using System;
using System.Globalization;
using System.Windows.Data;

namespace Workspace1C.Converters;

/// <summary>
/// Минимальная ширина колонки списка баз (аудит UI, пункт P0-3). Принимает признак
/// «показывать колонку», параметр — ключ колонки (<c>Version</c>, <c>LastBackup</c>…).
/// Возвращает минимум из <see cref="ColumnHeaderMetrics.MinWidthFor"/>, чтобы подпись
/// заголовка не обрезалась, либо 0 для скрытой колонки: скрытая колонка должна
/// схлопываться в ноль, иначе <c>MinWidth</c> держал бы её на экране.
/// Применяется одинаково к колонкам заголовка и строкам (базы и группы), поэтому
/// шапка и значения остаются выровненными при любой ширине.
/// </summary>
public class ColumnMinWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var show = value is bool b && b;
        if (!show || parameter is not string key || string.IsNullOrEmpty(key))
            return 0d;

        return ColumnHeaderMetrics.MinWidthFor(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
