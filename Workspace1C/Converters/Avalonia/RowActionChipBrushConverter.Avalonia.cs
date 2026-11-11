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
    /// Кисти круглой подложки-кружка кнопок колонки «Действия» строки базы
    /// (issue #121, v1.0.1.54): единый стиль с индикаторами запуска (issue #101/#103 —
    /// <see cref="RunningChipBrushConverter"/>). Покой — фон карточки с тонкой обводкой;
    /// выделенная строка подсвечивает обводку акцентом; зависший процесс (только кнопка
    /// запуска Предприятия) перекрашивает подложку в красный — как у чипов индикаторов.
    /// <para>Мульти-конвертер: values[0] — статус отклика процесса
    /// (<see cref="RunningDotStatus"/>), values[1] — признак выделенной строки.
    /// ConverterParameter — «кистьПокоя|кистьВыделеннойСтроки» (спецификации кистей
    /// <see cref="Themes.ThemeBrushes.FromSpec"/>); суффикс «-plain» отключает подсветку
    /// занятости (кнопки Конфигуратора и очистки кеша) — см. константы
    /// <see cref="BackgroundParameter"/>, <see cref="BorderParameter"/>,
    /// <see cref="BackgroundPlainParameter"/>, <see cref="BorderPlainParameter"/>.</para>
    /// </summary>
    public sealed class RowActionChipBrushConverter : IMultiValueConverter
    {
        /// <summary>Фон подложки: карточка и в покое, и на выделенной строке (меняется только обводка).</summary>
        public const string BackgroundParameter = "@CardBackgroundBrush|@CardBackgroundBrush";

        /// <summary>Обводка: тонкая в покое, акцентная на выделенной строке.</summary>
        public const string BorderParameter = "@BorderBrushColor|@AccentBrush";

        /// <summary>Суффикс параметра «без подсветки занятости» (Wrench/Broom).</summary>
        public const string PlainSuffix = "-plain";

        /// <summary>Фон подложки кнопок без индикации занятости.</summary>
        public const string BackgroundPlainParameter = BackgroundParameter + PlainSuffix;

        /// <summary>Обводка кнопок без индикации занятости.</summary>
        public const string BorderPlainParameter = BorderParameter + PlainSuffix;

        /// <summary>Красная подложка зависшего процесса — кисть темы ErrorBrush.</summary>
        private static IBrush HungChip => Themes.ThemeBrushes.FromSpec("@ErrorBrush");

        public object? Convert(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
        {
            var spec = parameter as string ?? BackgroundParameter;
            var plain = spec.EndsWith(PlainSuffix, StringComparison.Ordinal);
            var brushes = (plain ? spec[..^PlainSuffix.Length] : spec).Split('|');

            // Подсветка занятости приоритетнее выделения строки — как у чипов
            // индикаторов запуска (issue #101/#103).
            if (!plain && RunningChipBrushConverter.IsHung(values is { Count: > 0 } ? values[0] : null))
                return HungChip;

            var selected = values is { Count: > 1 } && values[1] is true;
            return Themes.ThemeBrushes.FromSpec(selected && brushes.Length > 1 ? brushes[1] : brushes[0]);
        }

        public object? ConvertBack(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif
