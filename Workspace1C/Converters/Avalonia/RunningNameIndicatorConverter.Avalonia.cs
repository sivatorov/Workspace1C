#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Видимость индикаторов запуска в наименовании базы (issue #121, v1.0.1.56,
    /// предложение 7OH): индикаторы («Предприятие»/«Конфигуратор» после имени)
    /// показываются только когда база запущена И колонка «Действия» выключена.
    /// Когда колонка «Действия» включена — чипы занятости видны на кнопках
    /// запуска этой колонки, дублирование в наименовании убрано.
    /// <para>values[0] — <c>Infobase.IsRunning</c>, values[1] —
    /// <c>MainViewModel.ShowActionsColumn</c>.</para>
    /// </summary>
    public sealed class RunningNameIndicatorConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isRunning = values.Count > 0 && values[0] is true;
            var showActionsColumn = values.Count > 1 && values[1] is true;
            return isRunning && !showActionsColumn;
        }
    }
}
#endif
