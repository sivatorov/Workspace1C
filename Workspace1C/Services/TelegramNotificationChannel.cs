using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Канал уведомлений Telegram (функция №5, этап 2 — выпуск 0.3.12.69): сообщение
/// отправляется методом Bot API <c>sendMessage</c> в чат <see cref="NotificationChannelSettings.TelegramChatId"/>.
/// Текст — простой (без parse_mode), поэтому экранирование MarkdownV2 не требуется.
/// Тихая деградация: ошибки сети/API → false (диспетчер пишет предупреждение), токен
/// в журнале маскируется.
/// <para>
/// Поле «ID чата» (issue #2) принимает не только число/отрицательное число/<c>@channel</c>,
/// но и приватную ссылку вида <c>t.me/c/1234567890</c> (с/без <c>https://</c>): число после
/// <c>/c/</c> дополняется префиксом «-100» (chat_id группы/канала), а число после второго
/// слэша — <c>message_thread_id</c> (тема/подгруппа). См. <see cref="ParseChatTarget"/>.
/// </para>
/// </summary>
public sealed class TelegramNotificationChannel : INotificationChannel
{
    /// <summary>Базовый адрес Bot API.</summary>
    public const string ApiBase = "https://api.telegram.org";

    /// <summary>Лимит длины сообщения Telegram (символов).</summary>
    public const int MaxMessageLength = 4096;

    /// <summary>
    /// Шаблон приватной ссылки Telegram: <c>t.me/c/<id>[/<thread>]</c>
    /// (с префиксом <c>https://</c>/<c>http://</c> и без, допускается <c>www.</c> и хвост после номера темы).
    /// </summary>
    private static readonly Regex PrivateChatLink = new(
        @"(?:https?://)?(?:www\.)?t\.me/c/(?<chat>\d+)(?:/(?<thread>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly INotificationSettingsStore _store;
    private readonly HttpMessageHandler? _handler;
    private readonly IAppLogger? _logger;

    /// <param name="store">Хранилище настроек каналов.</param>
    /// <param name="logger">Журнал (может быть null).</param>
    /// <param name="handler">Обработчик HTTP (подменяется в тестах; null — стандартный).</param>
    public TelegramNotificationChannel(INotificationSettingsStore store, IAppLogger? logger = null, HttpMessageHandler? handler = null)
    {
        _store = store;
        _logger = logger;
        _handler = handler;
    }

    /// <inheritdoc/>
    public string Name => "telegram";

    /// <inheritdoc/>
    public bool IsEnabled(AppSettings settings)
    {
        try { return IsConfigured(_store.Load()); }
        catch { return false; }
    }

    /// <summary>Канал включён и заполнены токен и чат.</summary>
    public static bool IsConfigured(NotificationChannelSettings s)
        => s is not null && s.TelegramEnabled
           && !string.IsNullOrWhiteSpace(s.TelegramBotToken)
           && !string.IsNullOrWhiteSpace(s.TelegramChatId);

    /// <summary>
    /// Разбор значения поля «ID чата» (issue #2). Поддерживаются:
    /// <list type="bullet">
    /// <item>число (в т.ч. отрицательное) — возвращается без изменений;</item>
    /// <item><c>@channel</c> — возвращается без изменений;</item>
    /// <item>приватная ссылка <c>t.me/c/1234567890</c> (с/без <c>https://</c>) → chat_id «-1001234567890»;</item>
    /// <item>ссылка на тему <c>t.me/c/1234567890/1234</c> → chat_id «-1001234567890» и message_thread_id «1234».</item>
    /// </list>
    /// </summary>
    /// <param name="raw">Сырое значение поля «ID чата».</param>
    /// <returns>Итоговый chat_id и (опционально) message_thread_id темы/подгруппы.</returns>
    public static (string ChatId, string? MessageThreadId) ParseChatTarget(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            return (string.Empty, null);

        var link = PrivateChatLink.Match(value);
        if (link.Success)
        {
            var chatId = "-100" + link.Groups["chat"].Value;
            var thread = link.Groups["thread"].Success ? link.Groups["thread"].Value : null;
            return (chatId, thread);
        }

        // Прежние форматы (число, отрицательное число, @channel) — без изменений.
        return (value, null);
    }

    // ------------------------------------------------------------------
    // Интерактивные подтверждения (выпуск 1.0.0.36+): callback-data кнопок
    // ------------------------------------------------------------------

    /// <summary>Префикс callback-data кнопок подтверждения (лимит Bot API — 64 байта).</summary>
    public const string CallbackPrefix = "wc1c";

    /// <summary>
    /// Callback-data кнопки подтверждения: <c>wc1c:<guid>:yes|no</c>. Формат — часть
    /// контракта между кнопкой и <see cref="TelegramConfirmationService"/>, поэтому
    /// <see cref="TryParseCallbackData"/> симметричен.
    /// </summary>
    public static string BuildCallbackData(Guid requestId, bool approve)
        => $"{CallbackPrefix}:{requestId}:{(approve ? "yes" : "no")}";

    /// <summary>Разбор callback-data кнопки подтверждения (<see cref="BuildCallbackData"/>).</summary>
    public static bool TryParseCallbackData(string? data, out Guid requestId, out bool approve)
    {
        requestId = Guid.Empty;
        approve = false;
        if (string.IsNullOrWhiteSpace(data))
            return false;
        var parts = data.Split(':');
        if (parts.Length != 3 || !string.Equals(parts[0], CallbackPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Guid.TryParse(parts[1], out requestId))
            return false;
        approve = string.Equals(parts[2], "yes", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// Сравнение идентификаторов чатов из настроек и из апдейтов Bot API: настройка может
    /// быть <c>@channel</c>, числом или «-100…» из приватной ссылки (см. <see cref="ParseChatTarget"/>),
    /// тогда как Telegram отдаёт chat.id числом (в т.ч. отрицательным для групп/каналов).
    /// Числа сравниваются по значению, остальное — без учёта регистра.
    /// </summary>
    public static bool SameChatId(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;
        var e = expected.Trim();
        var a = actual.Trim();
        if (long.TryParse(e, out var eNum) && long.TryParse(a, out var aNum))
            return eNum == aNum;
        return string.Equals(e, a, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        NotificationChannelSettings settings;
        try { settings = _store.Load(); }
        catch { return false; }

        // issue #131: для уведомлений о длительных операциях чат/тема может быть
        // переопределена в самом сообщении или отдельной настройкой; пусто —
        // используется общий TelegramChatId.
        var chatTarget = ResolveChatTarget(settings, message);

        if (!IsConfigured(settings, chatTarget) || !NotificationEventFilter.ShouldSend(settings, message))
            return true; // отфильтровано — это не ошибка канала

        var (ok, _) = await SendCoreAsync(settings, message, cancellationToken, chatTarget).ConfigureAwait(false);
        return ok;
    }

    /// <summary>
    /// Итоговый чат/тема для сообщения (issue #131): явное переопределение в
    /// сообщении, затем отдельная настройка длительных операций, затем общий чат.
    /// </summary>
    public static string? ResolveChatTarget(NotificationChannelSettings settings, NotificationMessage message)
    {
        if (message is null)
            return null;
        if (!string.IsNullOrWhiteSpace(message.ChatTargetOverride))
            return message.ChatTargetOverride!.Trim();
        if (message.Event == NotificationEvent.LongOperation &&
            !string.IsNullOrWhiteSpace(settings?.LongOperationTelegramChatId))
            return settings!.LongOperationTelegramChatId.Trim();
        return null;
    }

    /// <summary>
    /// Канал настроен: заполнен токен и есть чат — общий либо переопределённый
    /// для длительных операций (issue #131).
    /// </summary>
    public static bool IsConfigured(NotificationChannelSettings s, string? chatTarget)
        => s is not null && s.TelegramEnabled
           && !string.IsNullOrWhiteSpace(s.TelegramBotToken)
           && (!string.IsNullOrWhiteSpace(chatTarget) || !string.IsNullOrWhiteSpace(s.TelegramChatId));

    /// <summary>
    /// Тестовая отправка с переданными (ещё не сохранёнными) настройками — для кнопки
    /// «Проверить Telegram». Возвращает признак успеха и текст ошибки.
    /// </summary>
    public async Task<(bool Ok, string Error)> TestAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
            return (false, "token/chat_id");
        return await SendCoreAsync(settings, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправка «рабочего» сообщения (CLI-команда <c>--telegram-send</c>, выпуск 1.0.0.36+)
    /// в ОБХОД фильтра «какие события отправлять»: разработчик явно запросил уведомление,
    /// поэтому галочки бэкапов/заданий/обновлений и «только ошибки» к нему не применяются.
    /// Требует заполненных токена и чата (иначе — ошибка <c>not_configured</c>, а не тихий успех).
    /// </summary>
    public async Task<(bool Ok, string Error)> SendDevAsync(NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        NotificationChannelSettings settings;
        try { settings = _store.Load(); }
        catch (Exception ex) { return (false, ex.Message); }
        if (!IsConfigured(settings))
            return (false, "not_configured");
        return await SendCoreAsync(settings, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Error)> SendCoreAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken, string? chatTarget = null)
    {
        var token = settings.TelegramBotToken.Trim();
        try
        {
            // Внешний handler (тесты) приоритетнее; иначе — прокси из переменных окружения
            // (W1C_TELEGRAM_PROXY/HTTPS_PROXY...) для сетей с блокировкой api.telegram.org.
            var proxyHandler = _handler ?? TelegramHttpProxy.CreateHandler();
            using var http = proxyHandler is null
                ? new HttpClient()
                : new HttpClient(proxyHandler, disposeHandler: _handler is null);
            http.Timeout = RequestTimeout;
            // issue #131: переопределённый чат/тема (группа/подгруппа) приоритетнее общего.
            var (chatId, threadId) = ParseChatTarget(
                string.IsNullOrWhiteSpace(chatTarget) ? settings.TelegramChatId : chatTarget);
            using var request = BuildRequest(token, chatId, BuildText(message), threadId);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return (true, string.Empty);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = $"HTTP {(int)response.StatusCode}: {Truncate(body, 300)}";
            _logger?.Warn("Telegram: " + SensitiveDataMasker.MaskValue(error, token));
            return (false, SensitiveDataMasker.MaskValue(error, token));
        }
        catch (Exception ex)
        {
            var error = SensitiveDataMasker.MaskValue(ex.Message, token);
            _logger?.Warn("Telegram: " + error);
            return (false, error);
        }
    }

    /// <summary>Текст сообщения: маркер вида + заголовок, пустая строка, текст; обрезка до лимита.</summary>
    public static string BuildText(NotificationMessage message)
    {
        var marker = NotificationEventFilter.KindMarker(message?.Kind ?? NotificationKind.Info);
        var title = (message?.Title ?? string.Empty).Trim();
        var body = (message?.Message ?? string.Empty).Trim();
        var text = string.IsNullOrEmpty(title) ? $"{marker} {body}" : $"{marker} {title}\n\n{body}";
        return Truncate(text.Trim(), MaxMessageLength);
    }

    /// <summary>
    /// POST-запрос <c>/bot<token>/sendMessage</c> с form-urlencoded телом.
    /// Если задан <paramref name="messageThreadId"/>, добавляется параметр
    /// <c>message_thread_id</c> (отправка в тему/подгруппу, issue #2).
    /// </summary>
    public static HttpRequestMessage BuildRequest(string token, string chatId, string text, string? messageThreadId = null)
    {
        var url = $"{ApiBase}/bot{token.Trim()}/sendMessage";
        var fields = new List<KeyValuePair<string, string>>
        {
            new("chat_id", (chatId ?? string.Empty).Trim()),
            new("text", text ?? string.Empty),
            new("disable_web_page_preview", "true"),
        };
        if (!string.IsNullOrWhiteSpace(messageThreadId))
            fields.Add(new("message_thread_id", messageThreadId.Trim()));
        return new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text.Substring(0, max - 1) + "…";
    }
}
