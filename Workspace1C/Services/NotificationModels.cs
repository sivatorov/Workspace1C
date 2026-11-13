namespace Workspace1C.Services;

/// <summary>
/// Вид уведомления (функция №5). Используется каналами для оформления сообщения:
/// например, пометки эмодзи/заголовка письма или цвета системного уведомления.
/// </summary>
public enum NotificationKind
{
    /// <summary>Информационное уведомление (по умолчанию).</summary>
    Info,

    /// <summary>Успешное завершение фоновой операции.</summary>
    Success,

    /// <summary>Предупреждение (частичный успех, требующее внимания).</summary>
    Warning,

    /// <summary>Ошибка фоновой операции.</summary>
    Error
}

/// <summary>
/// Категория события-источника уведомления (функция №5). Используется для фильтра
/// «какие события отправлять» (NotifyOnBackup/NotifyOnScheduledTasks/NotifyOnUpdates):
/// внешние каналы получают только события, включённые в настройках.
/// </summary>
public enum NotificationEvent
{
    /// <summary>Резервная копия (завершение успехом/ошибкой).</summary>
    Backup,

    /// <summary>Задание по расписанию (в т.ч. догоняющее выполнение пропусков).</summary>
    ScheduledTask,

    /// <summary>Обнаружение новой версии приложения.</summary>
    Update,

    /// <summary>Тестовое сообщение («Проверить подключение»); не фильтруется.</summary>
    ManualTest,

    /// <summary>
    /// Интерактивная операция в окне (скрипты, замена строк подключения, пользовательские
    /// действия, окна платформы) — выпуск 0.3.12.69. Показывается только системным каналом ОС,
    /// внешние каналы (Telegram, email) такие сообщения не получают.
    /// </summary>
    General,

    /// <summary>
    /// Оповещения мониторинга (выпуск 1.0.0.69): превышения порогов на серверах 1С
    /// и новые ошибки журнала регистрации. Во внешние каналы уходят при включённом
    /// <see cref="NotificationChannelSettings.NotifyOnMonitoring"/>.
    /// </summary>
    Monitoring,

    /// <summary>
    /// Завершение длительной операции в окне 1С (issue #131): окно было занято
    /// дольше порога и снова отвечает. Маршрутизируется в канал, выбранный в
    /// <see cref="AppSettings.LongOperationNotifications"/>; внешние каналы
    /// дополнительно фильтруются <see cref="NotificationChannelSettings.NotifyOnLongOperations"/>.
    /// </summary>
    LongOperation
}

/// <summary>
/// Сообщение уведомления, рассылаемое диспетчером по включённым каналам (функция №5).
/// Формируется в точке вызова и далее не меняется: каналы сами оформляют текст
/// (Telegram — MarkdownV2, email — письмо) и обрезают по своим лимитам.
/// </summary>
public sealed class NotificationMessage
{
    /// <summary>Заголовок уведомления (например, имя приложения).</summary>
    public string Title { get; init; } = "";

    /// <summary>Текст уведомления (может содержать форматные аргументы уже подставленными).</summary>
    public string Message { get; init; } = "";

    /// <summary>Вид уведомления (успех/ошибка/информация).</summary>
    public NotificationKind Kind { get; init; } = NotificationKind.Info;

    /// <summary>Категория события-источника (для фильтра NotifyOn*).</summary>
    public NotificationEvent Event { get; init; } = NotificationEvent.ManualTest;

    /// <summary>Метка времени события (UTC). Для шапки письма и логов.</summary>
    public DateTimeOffset UtcTimestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Переопределение чата/темы для конкретного сообщения (issue #131): позволяет
    /// отправить уведомление о длительной операции в отдельную группу/подгруппу
    /// Telegram, не меняя сигнатуры каналов. Пусто — канал использует настройку.
    /// </summary>
    public string? ChatTargetOverride { get; init; }
}