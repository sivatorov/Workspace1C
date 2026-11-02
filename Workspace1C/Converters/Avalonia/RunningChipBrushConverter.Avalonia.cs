#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Workspace1C.Models;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Кисти чипа-подложки значков режимов запуска в строке списка баз
    /// (issue #101, v1.0.1.25, Вариант 4 — по выбору 7OH): подложка из Варианта 1
    /// плюс индикация статуса отклика. Пока процесс отвечает — чип цвета карточки
    /// с тонкой обводкой, значок в собственном цвете; как только процесс завис
    /// (<see cref="RunningDotStatus.Hung"/> или <see cref="RunningDotStatus.Critical"/>),
    /// чип перекрашивается в красный, а значок — в белый.
    /// <para>ConverterParameter: «bg» — фон чипа, «border» — обводка,
    /// «glyph:@Кисть» — цвет значка (базовый цвет — кисть темы).</para>
    /// </summary>
    public sealed class RunningChipBrushConverter : IValueConverter
    {
        /// <summary>Красный чип зависшего процесса (тот же тон, что у точки #310).</summary>
        public static readonly SolidColorBrush HungChip = new(Color.Parse("#EF4444"));

        /// <summary>Значок на красном чипе.</summary>
        public static readonly SolidColorBrush HungGlyph = new(Colors.White);

        /// <summary>Процесс завис (любая степень).</summary>
        public static bool IsHung(object? value)
            => value is RunningDotStatus.Hung or RunningDotStatus.Critical;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var spec = parameter as string ?? "bg";
            var hung = IsHung(value);

            if (spec.StartsWith("glyph:", StringComparison.Ordinal))
                return hung ? HungGlyph : Themes.ThemeBrushes.FromSpec(spec["glyph:".Length..]);

            return spec switch
            {
                "border" => hung ? HungChip : Themes.ThemeBrushes.FromSpec("@BorderBrushColor"),
                _ => hung ? HungChip : Themes.ThemeBrushes.FromSpec("@CardBackgroundBrush")
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif
