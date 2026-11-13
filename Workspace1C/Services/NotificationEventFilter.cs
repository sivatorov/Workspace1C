using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика фильтра внешних каналов уведомлений (Telegram, email — выпуск 0.3.12.69):
/// какие события отправлять. Системный канал ОС этот фильтр не использует.
/// </summary>
public static class NotificationEventFilter
{
    /// <summary>
    /// true — сообщение должно уйти во внешние каналы.
    /// <list type="bullet">
    /// <item><see cref="NotificationEvent.ManualTest"/> — всегда (кнопка «Проверить»);</item>
    /// <item><see cref="NotificationEvent.General"/> — никогда (интерактивные операции в окне);</item>
    /// <item>Backup / ScheduledTask / Update — по соответствующей галочке;</item>
    /// <item>«Только ошибки и предупреждения» отсекает Info/Success.</item>
    /// </list>
    /// </summary>
    public static bool ShouldSend(NotificationChannelSettings settings, NotificationMessage message)
    {
        if (settings is null || message is null)
            return false;

        if (message.Event == NotificationEvent.ManualTest)
            return true;

        var byEvent = message.Event switch
        {
            NotificationEvent.Backup => settings.NotifyOnBackup,
            NotificationEvent.ScheduledTask => settings.NotifyOnScheduledTasks,
            NotificationEvent.Update => settings.NotifyOnUpdates,
            NotificationEvent.Monitoring => settings.NotifyOnMonitoring,
            // issue #131: длительные операции во внешние каналы — по отдельной галочке.
            NotificationEvent.LongOperation => settings.NotifyOnLongOperations,
            _ => false
        };
        if (!byEvent)
            return false;

        if (settings.OnlyProblems)
            return message.Kind is NotificationKind.Error or NotificationKind.Warning;

        return true;
    }

    /// <summary>Префикс-маркер вида сообщения для текстовых каналов.</summary>
    public static string KindMarker(NotificationKind kind) => kind switch
    {
        NotificationKind.Success => "✅",
        NotificationKind.Warning => "⚠️",
        NotificationKind.Error => "❌",
        _ => "ℹ️"
    };
}
