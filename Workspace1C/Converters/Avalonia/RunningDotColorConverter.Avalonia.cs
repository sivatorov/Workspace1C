#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Workspace1C.Models;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Avalonia-версия: кисть точки индикатора «база сейчас запущена» (issue #310):
    /// зелёная #22C55E (отвечает), оранжевая #F59E0B (завис — первый опрос без отклика),
    /// красная #EF4444 (не отвечает несколько опросов подряд). Для индикатора режимов
    /// (Play/ключ у имени базы) ConverterParameter задаёт «базовый» цвет значка: пока
    /// процесс отвечает, значок остаётся в своём цвете, при зависании перекрашивается.
    /// </summary>
    public class RunningDotColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush Responding = Create("#22C55E");
        private static readonly SolidColorBrush Hung = Create("#F59E0B");
        private static readonly SolidColorBrush Critical = Create("#EF4444");

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                RunningDotStatus.Hung => Hung,
                RunningDotStatus.Critical => Critical,
                // Для индикатора режимов (Play/ключ у имени базы) параметр задаёт «базовый»
                // цвет значка: строка со спецификацией «@InfoBrush» — кисть темы (обновляется
                // вместе со схемой), иначе hex; при зависании/критическом статусе значок
                // перекрашивается как точка (issue #310).
                _ => ParseParameterBrush(parameter) ?? Responding
            };
        }

        /// <summary>Разбирает параметр-цвет (спецификация «@Кисть» или hex); без параметра — null.</summary>
        private static IBrush? ParseParameterBrush(object? parameter)
        {
            if (parameter is string spec && !string.IsNullOrWhiteSpace(spec))
            {
                if (spec[0] == '@')
                    return Themes.ThemeBrushes.FromSpec(spec);

                try
                {
                    return Create(spec);
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Create(string hex) => new(Color.Parse(hex));
    }
}
#endif