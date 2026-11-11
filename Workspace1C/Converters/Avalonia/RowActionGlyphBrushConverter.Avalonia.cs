#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Workspace1C.Models;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Цвет значка кнопки запуска Предприятия в колонке «Действия» строки базы
    /// (issue #121/#120, v1.0.1.54): индикатор запуска перенесён с отдельного чипа
    /// на кнопку запуска. Не запущено — вторичный цвет; запущено — оранжевый при
    /// обычном запуске и синий при запуске с /DEBUG (договорённость issue #103);
    /// зависший процесс (Hung/Critical) — белый на красном чипе, приоритет подсветки
    /// занятости (как у значков в чипах-индикаторах, <see cref="EnterpriseGlyphBrushConverter"/>).
    /// <para>Мульти-конвертер: values[0] — <see cref="Infobase.IsEnterpriseRunning"/>,
    /// values[1] — <see cref="Infobase.EnterpriseRunningStatus"/>,
    /// values[2] — <see cref="Infobase.IsEnterpriseDebugRunning"/>.
    /// ConverterParameter — «неЗапущено|обычный|отладка» (спецификации кистей
    /// <see cref="Themes.ThemeBrushes.FromSpec"/>; по умолчанию
    /// <see cref="DefaultParameter"/>).</para>
    /// </summary>
    public sealed class RowActionGlyphBrushConverter : IMultiValueConverter
    {
        /// <summary>«не запущено|обычный запуск|запуск с /DEBUG»: вторичный / оранжевый / синий.</summary>
        public const string DefaultParameter = "@TextSecondaryBrush|@WarningBrush|@InfoBrush";

        public object? Convert(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (RunningChipBrushConverter.IsHung(values is { Count: > 1 } ? values[1] : null))
                return RunningChipBrushConverter.HungGlyph;

            var spec = parameter as string ?? DefaultParameter;
            var parts = spec.Split('|');

            var isRunning = values is { Count: > 0 } && values[0] is true;
            if (!isRunning)
                return Themes.ThemeBrushes.FromSpec(parts[0]);

            var isDebug = values is { Count: > 2 } && values[2] is true;
            return Themes.ThemeBrushes.FromSpec(isDebug
                ? parts.Length > 2 ? parts[2] : "@InfoBrush"
                : parts.Length > 1 ? parts[1] : "@WarningBrush");
        }

        public object? ConvertBack(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif
