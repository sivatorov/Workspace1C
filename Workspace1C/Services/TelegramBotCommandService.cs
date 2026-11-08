using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Настройки команд Telegram-бота (<c>telegram_bot.json</c>).</summary>
public sealed class TelegramBotSettings
{
    /// <summary>Принимать команды (по умолчанию выключено).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Чаты, которым разрешено управление (chat_id через запятую). Пусто — только
    /// чат из настроек уведомлений.
    /// </summary>
    public string AllowedChatIds { get; set; } = string.Empty;

    /// <summary>Разрешить команду /run (запуск заданий по расписанию).</summary>
    public bool AllowRunTasks { get; set; }
}

/// <summary>
/// Входящая команда бота. <c>MessageThreadId</c> (issue #64) — тема/подгруппа канала:
/// сообщения из тем приходят с chat.id канала (<c>-100…</c>) и <c>message_thread_id</c>;
/// обычные сообщения темы не имеют (null).
/// </summary>
public sealed record TelegramCommand(long UpdateId, string ChatId, string? MessageThreadId, string Text);

/// <summary>
/// Команды Telegram-бота (выпуск 1.0.0.69; темы/подгруппы канала — выпуск 1.0.0.71, issue #64):
/// двусторонняя связь с приложением — /status, /tasks, /run N, /alerts, /help. Бот
/// уведомлений (токен и чат из «Уведомления в Telegram и по email») опрашивается long
/// polling только при включённой настройке; команды принимаются только из разрешённых
/// чатов (в т.ч. из тем/подгрупп канала). Разбор команд и формирование ответов — чистые
/// функции (тесты).
/// </summary>
public sealed class TelegramBotCommandService
{
    public const string FileName = "telegram_bot.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(70) };

    private readonly INotificationSettingsStore _notificationSettings;
    private readonly IScheduledTaskStore _tasks;
    private readonly SchedulerService _scheduler;
    private readonly ServerAlertsService _alerts;
    private readonly IAppLogger? _logger;
    private CancellationTokenSource? _cts;
    private long? _offset;

    public TelegramBotCommandService(INotificationSettingsStore notificationSettings, IScheduledTaskStore tasks,
        SchedulerService scheduler, ServerAlertsService alerts, IAppLogger? logger = null)
    {
        _notificationSettings = notificationSettings;
        _tasks = tasks;
        _scheduler = scheduler;
        _alerts = alerts;
        _logger = logger;
    }

    public static TelegramBotSettings Load() => FeatureDataStore.Load<TelegramBotSettings>(FileName);

    public static void Save(TelegramBotSettings settings) => FeatureDataStore.Save(FileName, settings);

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = Load();
                var channel = _notificationSettings.Load();
                // Команды бота — функция редакции ПРО (docs/pro/PRO-EDITION.md).
                if (!settings.Enabled || string.IsNullOrWhiteSpace(channel.TelegramBotToken)
                    || !Licensing.EditionService.Has(Licensing.ProFeature.TelegramBot))
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
                    continue;
                }

                var commands = await GetCommandsAsync(channel.TelegramBotToken.Trim(), ct).ConfigureAwait(false);
                foreach (var command in commands)
                {
                    _offset = command.UpdateId + 1;
                    if (string.IsNullOrEmpty(command.ChatId) || string.IsNullOrWhiteSpace(command.Text))
                        continue;
                    if (!IsAllowedChat(settings, channel.TelegramChatId, command.ChatId, command.MessageThreadId))
                    {
                        _logger?.Warn($"Telegram: команда из неразрешённого чата {command.ChatId}" +
                            $" (тема {command.MessageThreadId ?? "—"}) проигнорирована");
                        continue;
                    }
                    var reply = await ExecuteAsync(command.Text, settings, ct).ConfigureAwait(false);
                    await SendAsync(channel.TelegramBotToken.Trim(), command.ChatId, reply, command.MessageThreadId, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Telegram: ошибка обработки команд: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Разрешённые чаты: из настроек бота или чат уведомлений.</summary>
    public static HashSet<string> AllowedChats(TelegramBotSettings settings, string? notificationChatId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in (settings.AllowedChatIds ?? string.Empty)
                     .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            result.Add(id.Trim());
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(notificationChatId))
            result.Add(notificationChatId.Trim());
        return result;
    }

    /// <summary>
    /// Разрешён ли чат (issue #64): кандидаты — значения <see cref="TelegramBotSettings.AllowedChatIds"/>
    /// (разделители «,», «;», пробел; Trim; удаление пустых), если список не пуст, иначе — чат
    /// уведомлений <paramref name="notificationChatId"/>. Каждый кандидат разбирается через
    /// <see cref="TelegramNotificationChannel.ParseChatTarget"/> (число, @channel, ссылка
    /// <c>t.me/c/<id>[/<thread>]</c>) и сравнивается с фактическим чатом числовым
    /// сравнением (<see cref="TelegramNotificationChannel.SameChatId"/>). Кандидат без темы
    /// разрешает весь чат (основной чат и любые темы), кандидат с темой — только эту тему
    /// (<paramref name="threadId"/> — из <see cref="TelegramCommand.MessageThreadId"/>).
    /// </summary>
    public static bool IsAllowedChat(TelegramBotSettings settings, string? notificationChatId, string chatId, string? threadId)
    {
        foreach (var candidate in ChatCandidates(settings, notificationChatId))
        {
            var (expectedChat, expectedThread) = TelegramNotificationChannel.ParseChatTarget(candidate);
            if (!TelegramNotificationChannel.SameChatId(expectedChat, chatId))
                continue;
            if (expectedThread is null || string.Equals(expectedThread, threadId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Кандидаты на разрешённый чат: список из настроек или (при пустом списке) чат уведомлений.</summary>
    private static IEnumerable<string> ChatCandidates(TelegramBotSettings settings, string? notificationChatId)
    {
        var list = (settings.AllowedChatIds ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim()).ToList();
        if (list.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(notificationChatId))
                yield return notificationChatId.Trim();
            yield break;
        }
        foreach (var candidate in list)
            yield return candidate;
    }

    private async Task<IReadOnlyList<TelegramCommand>> GetCommandsAsync(string token, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{token}/getUpdates?timeout=50&allowed_updates=%5B%22message%22%5D"
                  + (_offset is null ? string.Empty : "&offset=" + _offset.Value.ToString(CultureInfo.InvariantCulture));
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 409 — getUpdates занят другим процессом (например, --telegram-confirm): ждём.
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            return Array.Empty<TelegramCommand>();
        }
        var commands = ParseUpdates(json);
        if (_offset is null && commands.Count > 0)
        {
            // Первый опрос после запуска: старые команды не выполняем, только подтверждаем.
            _offset = commands.Max(c => c.UpdateId) + 1;
            return Array.Empty<TelegramCommand>();
        }
        if (_offset is null)
            _offset = 0;
        return commands;
    }

    /// <summary>
    /// Разбор ответа getUpdates: текстовые сообщения с chat_id и (issue #64) message_thread_id
    /// темы/подгруппы канала.
    /// </summary>
    public static IReadOnlyList<TelegramCommand> ParseUpdates(string json)
    {
        var result = new List<TelegramCommand>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var items) || items.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("update_id", out var idElement))
                    continue;
                var updateId = idElement.GetInt64();
                if (!item.TryGetProperty("message", out var message))
                {
                    result.Add(new TelegramCommand(updateId, string.Empty, null, string.Empty));
                    continue;
                }
                var text = message.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var cid)
                    ? cid.GetRawText()
                    : string.Empty;
                // Сообщение из темы приходит с chat_id канала и message_thread_id; без темы — null.
                var threadId = message.TryGetProperty("message_thread_id", out var thread)
                               && thread.ValueKind == JsonValueKind.Number
                    ? thread.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : null;
                result.Add(new TelegramCommand(updateId, chatId, threadId, text));
            }
        }
        catch (JsonException)
        {
        }
        return result;
    }

    /// <summary>Нормализует команду: «/run@MyBot 2» → («/run», «2»).</summary>
    public static (string Command, string Argument) SplitCommand(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            return (string.Empty, string.Empty);
        var space = trimmed.IndexOf(' ');
        var command = space < 0 ? trimmed : trimmed.Substring(0, space);
        var argument = space < 0 ? string.Empty : trimmed.Substring(space + 1).Trim();
        var at = command.IndexOf('@');
        if (at > 0)
            command = command.Substring(0, at);
        return (command.ToLowerInvariant(), argument);
    }

    /// <summary>Список заданий для ответа /tasks (нумерация с 1, как в команде /run).</summary>
    public static string FormatTasks(IReadOnlyList<ScheduledTask> tasks)
    {
        if (tasks.Count == 0)
            return LocalizationManager.T("Bot.NoTasks");
        var sb = new StringBuilder();
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var status = task.LastRunAt is null
                ? "—"
                : (task.LastRunSuccess ? "✅ " : "❌ ") + task.LastRunAt.Value.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
            sb.Append(i + 1).Append(". ").Append(task.Name)
                .Append(task.Enabled ? string.Empty : " (" + LocalizationManager.T("Bot.Disabled") + ")")
                .Append(" — ").AppendLine(status);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Выполняет команду и возвращает текст ответа.</summary>
    public async Task<string> ExecuteAsync(string text, TelegramBotSettings settings, CancellationToken ct)
    {
        var (command, argument) = SplitCommand(text);
        // Журнал аудита: команды, которые что-то запускают (справочные команды не пишутся).
        if (command == "/run")
            Audit.AuditHub.Write(Audit.AuditActions.TelegramCommand, argument ?? string.Empty, true, text,
                Audit.AuditSources.Telegram);
        switch (command)
        {
            case "/start":
            case "/help":
                return LocalizationManager.T("Bot.Help");
            case "/tasks":
                return FormatTasks(_tasks.LoadAll());
            case "/status":
            {
                var tasks = _tasks.LoadAll();
                var failed = tasks.Count(t => t.LastRunAt is not null && !t.LastRunSuccess);
                return string.Format(LocalizationManager.T("Bot.Status"),
                    Environment.MachineName, VersionInfo.Display(), tasks.Count, failed,
                    DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
            }
            case "/alerts":
            {
                var alertSettings = ServerAlertsService.Load();
                alertSettings.Enabled = true;
                var alerts = await _alerts.CheckNowAsync(alertSettings, notify: false, ct).ConfigureAwait(false);
                return alerts.Count == 0
                    ? LocalizationManager.T("Alerts.AllGood")
                    : string.Join("\n", alerts.Select(a => "⚠️ " + a));
            }
            case "/run":
            {
                if (!settings.AllowRunTasks)
                    return LocalizationManager.T("Bot.RunDisabled");
                var tasks = _tasks.LoadAll();
                if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    || number < 1 || number > tasks.Count)
                    return LocalizationManager.T("Bot.RunUsage") + "\n\n" + FormatTasks(tasks);
                var task = tasks[number - 1];
                var result = await _scheduler.RunNowAsync(task).ConfigureAwait(false);
                return result is { Success: true }
                    ? string.Format(LocalizationManager.T("Bot.RunDone"), task.Name)
                    : string.Format(LocalizationManager.T("Bot.RunFailed"), task.Name, result?.ErrorMessage ?? "—");
            }
            default:
                return LocalizationManager.T("Bot.Unknown");
        }
    }

    private async Task SendAsync(string token, string chatId, string text, string? messageThreadId = null, CancellationToken ct = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("chat_id", chatId),
            new("text", text.Length > 4000 ? text.Substring(0, 4000) + "…" : text),
            new("disable_web_page_preview", "true")
        };
        // Ответ — в ту же тему/подгруппу, из которой пришла команда (issue #64).
        if (!string.IsNullOrWhiteSpace(messageThreadId))
            fields.Add(new("message_thread_id", messageThreadId.Trim()));
        using var content = new FormUrlEncodedContent(fields);
        using var response = await Http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            _logger?.Warn($"Telegram: не удалось отправить ответ ({(int)response.StatusCode})");
    }
}
