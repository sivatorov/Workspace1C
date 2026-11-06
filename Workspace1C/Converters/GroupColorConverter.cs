using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Workspace1C.Converters;

/// <summary>
/// Кисть фона заголовка группы. Предпочитает готовое значение Brush из values[0];
/// иначе берёт цвет из строки (hex). Кисти кэшируются и Freeze().
/// </summary>
public class GroupColorConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is SolidColorBrush ready)
            return ready;

        // values[0] — hex-цвет группы (или FullPath в старых привязках — тогда fallback).
        var hex = values.Length > 0 ? values[0]?.ToString() : null;
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
            hex = "#2D6CDF";

        return GetBrush(hex);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static SolidColorBrush GetBrush(string hex) => GetBrush(hex, adaptForDark: false);

    /// <summary>
    /// Кисть фона плашки группы. При <paramref name="adaptForDark"/> = true (плашка в дереве,
    /// issue #107) в тёмной теме приложения светлая заливка группы заменяется полупрозрачным
    /// вариантом того же цвета: поверх тёмного фона она выглядит как приглушённая тёмная
    /// плашка, сохраняя оттенок группы (иначе светлая плашка «ослепляет» на тёмном фоне).
    /// </summary>
    public static SolidColorBrush GetBrush(string hex, bool adaptForDark)
    {
        var rawKey = hex ?? "#2D6CDF";
        // Ключ кэша различает обычную и адаптированную кисти одного цвета.
        var key = adaptForDark ? "\u0001" + rawKey : rawKey;
        return Cache.GetOrAdd(key, _ =>
        {
            var color = ParseColor(rawKey);
            if (adaptForDark &&
                Workspace1C.Themes.ThemeManager.CurrentTheme == Workspace1C.Themes.ThemeManager.DarkThemeName &&
                IsLightColor(color))
            {
                color = Color.FromArgb(DarkChipAlpha, color.R, color.G, color.B);
            }
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        });
    }

    /// <summary>Альфа адаптированной плашки в тёмной теме (~25 %): поверх тёмного фона
    /// даёт тёмный приглушённый оттенок цвета группы.</summary>
    private const byte DarkChipAlpha = 64;

    internal static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString("#2D6CDF"); }
    }

    /// <summary>Светлый ли цвет (та же метрика, что в GroupTextColorConverter): светлая
    /// заливка в тёмной теме заменяется полупрозрачным вариантом (issue #107).</summary>
    internal static bool IsLightColor(Color color)
        => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 150;
}
