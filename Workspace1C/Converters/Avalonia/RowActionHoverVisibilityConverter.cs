#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Workspace1C.Services;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Видимость кнопки в колонке «Действия» при настройке «Действия только при
    /// наведении» (issue #128): обёртка над чистым <see cref="ActionsHoverPlanner"/>.
    /// <para>values[0] — <c>MainViewModel.ActionsHoverOnly</c> (включена ли настройка);
    /// values[1] — <c>IsPointerOver</c> карточки строки (курсор над строкой);
    /// values[2] — запущен ли соответствующий режим (для служебной кнопки —
    /// константа false). Результат — <c>IsVisible</c> кнопки.</para>
    /// </summary>
    public sealed class RowActionHoverVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var hoverOnly = values.Count > 0 && values[0] is true;
            var rowHovered = values.Count > 1 && values[1] is true;
            var modeRunning = values.Count > 2 && values[2] is true;
            return ActionsHoverPlanner.IsLaunchActionVisible(hoverOnly, rowHovered, modeRunning);
        }
    }
}
#endif
