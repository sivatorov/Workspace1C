namespace Workspace1C.Models;

/// <summary>
/// Настройки внешних каналов уведомлений (функция №5, этап 2 — выпуск 0.3.12.69):
/// Telegram-бот и электронная почта (SMTP), а также фильтр «какие события отправлять».
/// Хранится отдельным файлом <c>notifications.json</c> в каталоге данных профиля
/// (см. <see cref="Services.NotificationSettingsStore"/>), а не в <c>settings.json</c>:
/// окно настроек уведомлений сохраняет файл независимо от главного окна.
/// Секреты (токен бота, пароль SMTP) хранятся как пароли ИТС — открыто в файле профиля
/// и маскируются при журналировании.
/// </summary>
public sealed class NotificationChannelSettings
{
    // ---------- Telegram ----------

    /// <summary>Канал Telegram включён.</summary>
    public bool TelegramEnabled { get; set; }

    /// <summary>Токен бота (выдаёт @BotFather), вида <c>123456:ABC-DEF…</c>.</summary>
    public string TelegramBotToken { get; set; } = "";

    /// <summary>
    /// Идентификатор чата/группы/канала: число (для групп — отрицательное), <c>@channel</c>
    /// или приватная ссылка <c>t.me/c/1234567890</c> (для темы — <c>t.me/c/1234567890/1234</c>),
    /// которая разбирается при отправке (к chat_id добавляется <c>-100</c>, см. issue #2).
    /// </summary>
    public string TelegramChatId { get; set; } = "";

    // ---------- Email (SMTP) ----------

    /// <summary>Канал email включён.</summary>
    public bool EmailEnabled { get; set; }

    /// <summary>Адрес SMTP-сервера (например <c>smtp.yandex.ru</c>).</summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>Порт SMTP: 587 — STARTTLS (по умолчанию), 25 — без шифрования.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Использовать STARTTLS (шифрование соединения).</summary>
    public bool SmtpUseStartTls { get; set; } = true;

    /// <summary>Логин SMTP (пусто — без авторизации).</summary>
    public string SmtpUser { get; set; } = "";

    /// <summary>Пароль SMTP (для почтовых сервисов — пароль приложения).</summary>
    public string SmtpPassword { get; set; } = "";

    /// <summary>Адрес отправителя (пусто — используется логин SMTP).</summary>
    public string EmailFrom { get; set; } = "";

    /// <summary>Получатели через запятую или точку с запятой.</summary>
    public string EmailTo { get; set; } = "";

    // ---------- Фильтр событий ----------

    /// <summary>Отправлять результаты резервного копирования.</summary>
    public bool NotifyOnBackup { get; set; } = true;

    /// <summary>Отправлять результаты заданий по расписанию (в т.ч. проверки обновлений).</summary>
    public bool NotifyOnScheduledTasks { get; set; } = true;

    /// <summary>Отправлять сообщение о новой версии приложения.</summary>
    public bool NotifyOnUpdates { get; set; } = true;

    /// <summary>Отправлять оповещения мониторинга серверов и журналов регистрации (1.0.0.69).</summary>
    public bool NotifyOnMonitoring { get; set; } = true;

    /// <summary>
    /// Отправлять уведомления о длительных операциях (issue #131) во внешние
    /// каналы. Системный канал этим флагом не управляется — у него отдельная
    /// настройка <see cref="AppSettings.LongOperationNotifications"/>.
    /// </summary>
    public bool NotifyOnLongOperations { get; set; } = true;

    /// <summary>
    /// Отдельная группа/подгруппа Telegram для уведомлений о длительных
    /// операциях (issue #131). Пусто — используется общий
    /// <see cref="TelegramChatId"/>; поддерживается ссылка
    /// <c>t.me/c/<id>/<thread></c> (тема/подгруппа).
    /// </summary>
    public string LongOperationTelegramChatId { get; set; } = "";

    /// <summary>Отправлять только ошибки и предупреждения (успешные операции пропускаются).</summary>
    public bool OnlyProblems { get; set; }

    // ---------- Лицензии (п.5 «Мониторинг лицензий», выпуск 1.0.0.87) ----------

    /// <summary>
    /// Порог «свободных программных лицензий осталось меньше N»: при достижении
    /// отправляется оповещение в каналы мониторинга (Telegram/email/системное).
    /// 0 — проверка отключена.
    /// </summary>
    public int LicenseFreeThreshold { get; set; }

    /// <summary>
    /// Не повторять оповещение о нехватке лицензий одного сервера чаще, чем раз в N часов
    /// (защита от спама на каждом цикле мониторинга); по умолчанию 24 ч.
    /// </summary>
    public int LicenseAlertRepeatHours { get; set; } = 24;

    // ---------- Прогноз диска под копиями (п.7 «История размеров», выпуск 1.0.0.89) ----------

    /// <summary>
    /// Порог «диск под копиями закончится не позднее, чем через N дней»: при прогнозе
    /// меньше порога отправляется оповещение в каналы мониторинга. 0 — проверка отключена.
    /// </summary>
    public int DiskForecastThresholdDays { get; set; }

    /// <summary>
    /// Не повторять оповещение о том же диске чаще, чем раз в N дней (защита от спама
    /// на каждом цикле мониторинга); по умолчанию 1 день.
    /// </summary>
    public int DiskForecastAlertRepeatDays { get; set; } = 1;

    /// <summary>Глубокая копия (для редактирования в окне без изменения исходника).</summary>
    public NotificationChannelSettings Clone() => (NotificationChannelSettings)MemberwiseClone();
}
