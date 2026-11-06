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
    /// Avalonia-версия: кисть фона заголовка группы (hex-цвет → SolidColorBrush).
    /// </summary>
    public class GroupColorConverter : IMultiValueConverter
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is { Count: > 0 } && values[0] is ISolidColorBrush ready)
                return ready;

            var hex = values is { Count: > 0 } ? values[0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
                hex = "#2D6CDF";

            return GetBrush(hex);
        }

        public static SolidColorBrush GetBrush(string? hex) => GetBrush(hex, adaptForDark: false);

        /// <summary>
        /// Кисть фона плашки группы (issue #107): при <paramref name="adaptForDark"/> = true
        /// в тёмной теме приложения светлая заливка группы заменяется полупрозрачным
        /// вариантом того же цвета — поверх тёмного фона это приглушённая тёмная плашка.
        /// </summary>
        public static SolidColorBrush GetBrush(string? hex, bool adaptForDark)
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
                    color = new Color(64, color.R, color.G, color.B);
                }
                return new SolidColorBrush(color);
            });
        }

        private const byte DarkChipAlpha = 64;

        private static Color ParseColor(string hex)
        {
            try { return Color.Parse(hex); }
            catch { return Color.Parse("#2D6CDF"); }
        }

        private static bool IsLightColor(Color color)
            => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B > 150;

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif