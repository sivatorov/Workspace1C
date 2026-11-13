using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Текст уведомления о длительной операции (issue #131): имя компьютера, режим
/// (Конфигуратор/Предприятие), полный заголовок окна и сколько окно было
/// недоступно. Чистые функции — используются системным каналом и Telegram.
/// </summary>
public static class LongOperationNotificationText
{
    /// <summary>Заголовок уведомления (системный канал и Telegram).</summary>
    public static string BuildTitle() =>
        T("LongOperation.NotificationTitle", "Длительная операция завершена");

    /// <summary>
    /// Формирует многострочный текст уведомления. Пустое имя компьютера
    /// заменяется на <see cref="Environment.MachineName"/>, пустой заголовок —
    /// на локализованное «окно без заголовка».
    /// </summary>
    public static string Build(string? machineName, RunningKind mode, string? windowTitle, TimeSpan duration)
    {
        var machine = string.IsNullOrWhiteSpace(machineName)
            ? Environment.MachineName
            : machineName.Trim();

        var modeText = mode == RunningKind.Configurator
            ? T("LongOperation.Mode.Configurator", "Конфигуратор")
            : T("LongOperation.Mode.Enterprise", "Предприятие");

        var title = string.IsNullOrWhiteSpace(windowTitle)
            ? T("LongOperation.NoWindowTitle", "окно без заголовка")
            : windowTitle.Trim();

        return string.Join(Environment.NewLine, new[]
        {
            $"{T("LongOperation.Label.Computer", "Компьютер")}: {machine}",
            $"{T("LongOperation.Label.Mode", "Режим")}: {modeText}",
            $"{T("LongOperation.Label.Window", "Заголовок окна")}: {title}",
            $"{T("LongOperation.Label.Duration", "Окно было недоступно")}: {FormatDuration(duration)}"
        });
    }

    /// <summary>
    /// Человекочитаемая длительность: «1 ч 5 мин 3 с», «2 мин 5 с», «45 с».
    /// Отрицательные значения трактуются как ноль.
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        var totalSeconds = (long)Math.Floor(duration.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        var hourUnit = T("LongOperation.Duration.HoursShort", "ч");
        var minuteUnit = T("LongOperation.Duration.MinutesShort", "мин");
        var secondUnit = T("LongOperation.Duration.SecondsShort", "с");

        if (hours > 0)
        {
            return minutes > 0
                ? $"{hours} {hourUnit} {minutes} {minuteUnit}"
                : $"{hours} {hourUnit}";
        }

        if (minutes > 0)
            return $"{minutes} {minuteUnit} {seconds} {secondUnit}";

        return $"{seconds} {secondUnit}";
    }

    /// <summary>
    /// Локализованная строка с запасным значением: если ключ ещё не добавлен в
    /// словарь, возвращается <paramref name="fallback"/> (текст не ломается).
    /// </summary>
    private static string T(string key, string fallback)
    {
        try
        {
            var value = LocalizationManager.T(key);
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
                ? fallback
                : value;
        }
        catch
        {
            return fallback;
        }
    }
}
