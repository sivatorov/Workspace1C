namespace Workspace1C.Models;

/// <summary>
/// Каналы уведомлений о длительных операциях (issue #131). Хранятся строкой
/// для обратной совместимости с файлом настроек: неизвестное значение
/// трактуется как <see cref="System"/>.
/// </summary>
public static class LongOperationNotificationChannels
{
    /// <summary>Системные уведомления ОС (трей/notify-send).</summary>
    public const string System = "System";

    /// <summary>Уведомление в Telegram (группа/подгруппа).</summary>
    public const string Telegram = "Telegram";

    /// <summary>Приводит значение из файла настроек к каноническому виду.</summary>
    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Telegram, StringComparison.OrdinalIgnoreCase)
            ? Telegram
            : System;
}

/// <summary>
/// Единицы измерения порога «Длительность простоя» (issue #131). Хранятся
/// строкой для обратной совместимости; неизвестное значение — секунды.
/// </summary>
public static class LongOperationThresholdUnits
{
    public const string Seconds = "Seconds";
    public const string Minutes = "Minutes";
    public const string Hours = "Hours";

    /// <summary>Приводит значение из файла настроек к каноническому виду.</summary>
    public static string Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, Minutes, StringComparison.OrdinalIgnoreCase))
            return Minutes;
        if (string.Equals(trimmed, Hours, StringComparison.OrdinalIgnoreCase))
            return Hours;
        return Seconds;
    }
}

/// <summary>
/// Настройки «Уведомления о длительных операциях» (issue #131): мониторинг
/// запущенных Предприятий/Конфигураторов и оповещение после того, как окно
/// перестало быть занятым дольше указанного порога.
/// <para>
/// Хранится вложенной секцией <c>LongOperationNotifications</c> в
/// <c>settings.json</c> (см. <see cref="AppSettings.LongOperationNotifications"/>);
/// читается на каждом опросе монитора, поэтому применяется без перезапуска.
/// </para>
/// </summary>
public sealed class LongOperationNotificationsSettings
{
    /// <summary>Галочка «Уведомлять».</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// «Куда уведомлять»: <see cref="LongOperationNotificationChannels.System"/>
    /// или <see cref="LongOperationNotificationChannels.Telegram"/>.
    /// </summary>
    public string Channel { get; set; } = LongOperationNotificationChannels.System;

    /// <summary>
    /// Отдельная группа/подгруппа Telegram для этих уведомлений: число,
    /// <c>@channel</c> или ссылка <c>t.me/c/<id>/<thread></c>.
    /// Пусто — используется общий <see cref="NotificationChannelSettings.TelegramChatId"/>.
    /// </summary>
    public string TelegramTarget { get; set; } = "";

    /// <summary>Значение поля «Длительность простоя» (минимум 1).</summary>
    public int ThresholdValue { get; set; } = 30;

    /// <summary>
    /// Единица измерения порога: <see cref="LongOperationThresholdUnits.Seconds"/>,
    /// <see cref="LongOperationThresholdUnits.Minutes"/> или
    /// <see cref="LongOperationThresholdUnits.Hours"/>.
    /// </summary>
    public string ThresholdUnit { get; set; } = LongOperationThresholdUnits.Seconds;

    /// <summary>Мониторить запущенные Предприятия (по умолчанию включено).</summary>
    public bool MonitorEnterprise { get; set; } = true;

    /// <summary>Мониторить запущенные Конфигураторы (по умолчанию включено).</summary>
    public bool MonitorConfigurator { get; set; } = true;

    /// <summary>Приводит строковые поля к каноническому виду (после загрузки).</summary>
    public void Normalize()
    {
        Channel = LongOperationNotificationChannels.Normalize(Channel);
        ThresholdUnit = LongOperationThresholdUnits.Normalize(ThresholdUnit);
        TelegramTarget ??= "";
        if (ThresholdValue <= 0)
            ThresholdValue = 30;
    }

    /// <summary>Глубокая копия (для редактирования в окне без изменения исходника).</summary>
    public LongOperationNotificationsSettings Clone() =>
        (LongOperationNotificationsSettings)MemberwiseClone();
}
