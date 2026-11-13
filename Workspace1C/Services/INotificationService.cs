namespace Workspace1C.Services;

/// <summary>
/// Уведомления о завершении фоновых операций (функция №4): резервная копия,
/// задание по расписанию, найденное обновление приложения. Вызывается из фоновых
/// потоков (планировщик заданий, автообновление), когда приложение может быть свёрнуто
/// в трей — именно там уведомление и показывается.
///
/// С функции №5 (цикл 0.3.9.180+) единственной реализацией является мультиканальный
/// <see cref="NotificationDispatcher"/>: системное уведомление ОС (balloon-tip/notify-send)
/// дополняется внешними каналами (Telegram, email). Реализации обязаны тихо деградировать:
/// при недоступности платформенного механизма вызов — no-op, исключения наружу не всплывают.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Показывает уведомление (заголовок + текст). Fire-and-forget: метод не
    /// блокирует вызывающий поток, любые ошибки платформы гасятся внутри реализации.
    /// Эквивалентен <see cref="Show(string,string,NotificationKind,NotificationEvent)"/>
    /// с kind = <see cref="NotificationKind.Info"/> и evt = <see cref="NotificationEvent.General"/>
    /// (с 0.3.12.69; такие сообщения внешние каналы не получают).
    /// </summary>
    void Show(string title, string message);

    /// <summary>
    /// Показывает уведомление с видом события и категорией-источником (функция №5).
    /// Fire-and-forget: метод не блокирует вызывающий поток; диспетчер рассылает
    /// сообщение всем включённым каналам параллельно, ошибки каналов гасятся внутри.
    /// </summary>
    /// <param name="title">Заголовок уведомления.</param>
    /// <param name="message">Текст уведомления.</param>
    /// <param name="kind">Вид: успех/ошибка/предупреждение/информация.</param>
    /// <param name="evt">Категория события-источника (для фильтра «какие события отправлять»).</param>
    void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest);

    /// <summary>
    /// Показывает готовое сообщение (issue #131): нужно для необязательных полей
    /// <see cref="NotificationMessage"/>, в частности <see cref="NotificationMessage.ChatTargetOverride"/>
    /// (отдельная группа/подтема Telegram для длительных операций).
    /// Реализация по умолчанию разворачивает сообщение в прежний вызов —
    /// существующие реализации интерфейса менять не требуется.
    /// </summary>
    void Show(NotificationMessage message) =>
        Show(message.Title, message.Message, message.Kind, message.Event);
}