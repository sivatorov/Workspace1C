#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Workspace1C.Models;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Цвет значка «Предприятие» в чипе-индикаторе запуска (issue #103, v1.0.1.54):
    /// оранжевый (WarningBrush) при обычном запуске, синий (InfoBrush) при запуске
    /// с отладкой /DEBUG, белый на красном чипе зависшего процесса (приоритет
    /// подсветки занятости — как у Конфигуратора, issue #101/#310).
    /// <para>Мульти-конвертер: values[0] — <see cref="Infobase.EnterpriseRunningStatus"/>,
    /// values[1] — <see cref="Infobase.IsEnterpriseDebugRunning"/>.
    /// ConverterParameter — «обычный|отладка» (спецификации кистей
    /// <see cref="Themes.ThemeBrushes.FromSpec"/>; по умолчанию
    /// <see cref="DefaultParameter"/>) — заменяет прежний отдельный янтарный
    /// маркер отладки: цвет значка теперь сам кодирует отладку.</para>
    /// </summary>
    public sealed class EnterpriseGlyphBrushConverter : IMultiValueConverter
    {
        /// <summary>«обычный запуск|запуск с /DEBUG»: оранжевый / синий.</summary>
        public const string DefaultParameter = "@WarningBrush|@InfoBrush";

        public object? Convert(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (RunningChipBrushConverter.IsHung(values is { Count: > 0 } ? values[0] : null))
                return RunningChipBrushConverter.HungGlyph;

            var spec = parameter as string ?? DefaultParameter;
            var separator = spec.IndexOf('|');
            var normalSpec = separator >= 0 ? spec[..separator] : spec;
            var debugSpec = separator >= 0 ? spec[(separator + 1)..] : "@InfoBrush";

            var isDebug = values is { Count: > 1 } && values[1] is true;
            return Themes.ThemeBrushes.FromSpec(isDebug ? debugSpec : normalSpec);
        }

        public object? ConvertBack(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif
