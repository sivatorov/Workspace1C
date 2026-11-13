using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Единица измерения порога «Длительность простоя» (issue #131).
/// Порядок значений соответствует порядку пунктов поля выбора в окне настроек.
/// </summary>
public enum LongOperationThresholdUnit
{
    /// <summary>Секунды.</summary>
    Seconds,

    /// <summary>Минуты.</summary>
    Minutes,

    /// <summary>Часы.</summary>
    Hours
}

/// <summary>
/// Чистая логика порога длительной операции (issue #131): перевод «значение +
/// единица» в <see cref="TimeSpan"/>. Нулевое, отрицательное и отсутствующее
/// значение защищены минимумом в одну секунду — иначе трекер срабатывал бы на
/// каждом опросе.
/// </summary>
public static class LongOperationThreshold
{
    /// <summary>Минимальный порог — одна секунда.</summary>
    public const int MinimumSeconds = 1;

    /// <summary>Верхняя граница порога — 30 суток (защита от опечаток).</summary>
    public const int MaximumSeconds = 30 * 24 * 60 * 60;

    /// <summary>
    /// Переводит значение и единицу в длительность. Значение ≤ 0 даёт одну
    /// секунду, слишком большое — клампится к <see cref="MaximumSeconds"/>.
    /// </summary>
    public static TimeSpan ToTimeSpan(int value, LongOperationThresholdUnit unit)
    {
        if (value <= 0)
            return TimeSpan.FromSeconds(MinimumSeconds);

        var seconds = unit switch
        {
            LongOperationThresholdUnit.Hours => value * 3600d,
            LongOperationThresholdUnit.Minutes => value * 60d,
            _ => (double)value
        };

        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumSeconds));
    }

    /// <summary>Перегрузка для строкового значения из файла настроек.</summary>
    public static TimeSpan ToTimeSpan(int value, string? unit) =>
        ToTimeSpan(value, ParseUnit(unit));

    /// <summary>Разбор строкового значения единицы (неизвестное — секунды).</summary>
    public static LongOperationThresholdUnit ParseUnit(string? unit)
    {
        var normalized = LongOperationThresholdUnits.Normalize(unit);
        if (string.Equals(normalized, LongOperationThresholdUnits.Minutes, StringComparison.Ordinal))
            return LongOperationThresholdUnit.Minutes;
        if (string.Equals(normalized, LongOperationThresholdUnits.Hours, StringComparison.Ordinal))
            return LongOperationThresholdUnit.Hours;
        return LongOperationThresholdUnit.Seconds;
    }

    /// <summary>Каноническая строка для сохранения в настройках.</summary>
    public static string ToSettingString(LongOperationThresholdUnit unit) => unit switch
    {
        LongOperationThresholdUnit.Hours => LongOperationThresholdUnits.Hours,
        LongOperationThresholdUnit.Minutes => LongOperationThresholdUnits.Minutes,
        _ => LongOperationThresholdUnits.Seconds
    };
}
