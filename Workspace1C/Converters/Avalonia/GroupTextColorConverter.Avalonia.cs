#if LINUX
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Avalonia-версия: контрастный цвет текста для фона группы (кэшируется по hex).
    /// </summary>
    public class GroupTextColorConverter : IMultiValueConverter
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SolidColorBrush Black = new(Colors.Black);
        private static readonly SolidColorBrush White = new(Colors.White);

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is { Count: > 0 } && values[0] is ISolidColorBrush ready)
                return ready;

            var hex = values is { Count: > 0 } ? values[0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
                hex = "#2D6CDF";

            return GetBrush(hex);
        }

        public static SolidColorBrush GetBrush(string? hex)
            => Cache.GetOrAdd(hex ?? "#2D6CDF", key =>
            {
                var color = ParseColor(key);
                // Issue #107: в тёмной теме светлая заливка плашки группы адаптируется
                // (GroupColorConverter.GetBrush делает её полупрозрачной, тёмной) — текст
                // на такой плашке должен быть светлым, а не чёрным.
                if (IsLight(color) &&
                    Workspace1C.Themes.ThemeManager.CurrentTheme == Workspace1C.Themes.ThemeManager.DarkThemeName)
                    return White;
                return IsLight(color) ? Black : White;
            });

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Color ParseColor(string hex)
        {
            try { return Color.Parse(hex); }
            catch { return Color.Parse("#2D6CDF"); }
        }

        private static bool IsLight(Color color)
        {
            var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
            return luminance > 150;
        }
    }
}
#endif