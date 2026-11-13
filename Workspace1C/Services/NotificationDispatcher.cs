using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Мультиканальный диспетчер уведомлений (функция №5): рассылает сообщение всем включённым
/// каналам (<see cref="INotificationChannel"/>) параллельно. Остаётся единственной
/// реализацией <see cref="INotificationService"/> в контейнере — все существующие точки
/// вызова (резервная копия, задания по расписанию, обновление приложения) продолжают
/// работать через него без изменений.
///
/// Гарантии:
/// - не блокирует вызывающий поток: <see cref="Show(string,string)"/> и перегрузка —
///   fire-and-forget, весь сетевой/платформенный ввод-вывод выполняется в фоне;
/// - тихо деградирует: ошибки отдельного канала не всплывают наружу и не влияют на
///   остальные каналы; канал возвращает false — диспетчер только пишет предупреждение
///   в журнал с маскированием секретов (<see cref="SensitiveDataMasker"/>, этап 0.3.9.182);
/// - настройки читаются один раз на сообщение (<see cref="IInfobaseRepository.LoadSettings"/>),
///   поэтому изменения в окне настроек применяются без перезапуска.
/// </summary>
public sealed class NotificationDispatcher : INotificationService
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly IInfobaseRepository _repository;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Создаёт диспетчер. Каналы резолвятся контейнером из всех регистраций
    /// <see cref="INotificationChannel"/> (множественная регистрация в AppServices).
    /// </summary>
    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        IInfobaseRepository repository,
        IAppLogger? logger = null)
    {
        _channels = channels;
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Show(string title, string message)
    {
        // С 0.3.12.69 — General: интерактивные сообщения не уходят во внешние каналы.
        Show(title, message, NotificationKind.Info, NotificationEvent.General);
    }

    /// <inheritdoc/>
    public void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest)
    {
        try
        {
            var notification = new NotificationMessage
            {
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                Kind = kind,
                Event = evt
            };

            // Fire-and-forget: уведомление не должно блокировать фоновую операцию
            // (планировщик, автообновление) даже на время сетевых таймаутов.
            _ = SendCoreAsync(notification);
        }
        catch (Exception ex)
        {
            // Исключение до первого await (например, отказ контейнера) — гасим.
            _logger?.Warn($"Уведомления не отправлены: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Show(NotificationMessage message)
    {
        if (message is null)
            return;
        try
        {
            // Готовое сообщение (issue #131): сохраняем необязательные поля
            // (ChatTargetOverride) без пересборки из заголовка и текста.
            _ = SendCoreAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Уведомления не отправлены: {ex.Message}");
        }
    }

    /// <summary>
    /// Рассылает сообщение включённым каналам. Ошибки каналов логируются и не влияют
    /// друг на друга; все сетевые операции используют ConfigureAwait(false).
    /// </summary>
    private async Task SendCoreAsync(NotificationMessage message)
    {
        try
        {
            AppSettings settings;
            try
            {
                settings = _repository.LoadSettings();
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Не удалось прочитать настройки для уведомлений: {ex.Message}");
                return;
            }

            List<INotificationChannel> enabled;
            if (message.Event == NotificationEvent.LongOperation)
            {
                // issue #131: уведомления о длительных операциях идут только в
                // выбранный канал (системные уведомления или Telegram). Email и
                // прочие внешние каналы для этого события не используются;
                // системный канал управляется галочкой «Уведомлять».
                var longOperation = settings.LongOperationNotifications;
                if (longOperation is null || !longOperation.Enabled)
                    return;

                var channelName = string.Equals(
                    LongOperationNotificationChannels.Normalize(longOperation.Channel),
                    LongOperationNotificationChannels.Telegram,
                    StringComparison.Ordinal)
                    ? "telegram"
                    : "system";

                enabled = _channels
                    .Where(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase)
                                && c.IsEnabled(settings))
                    .ToList();
            }
            else
            {
                enabled = _channels.Where(c => c.IsEnabled(settings)).ToList();
            }

            if (enabled.Count == 0)
                return;

            await Task.WhenAll(enabled.Select(c => SendSafelyAsync(c, message))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Непредвиденная ошибка оркестрации — не должна ронять фоновый поток.
            _logger?.Warn($"Ошибка рассылки уведомлений: {ex.Message}");
        }
    }

    /// <summary>
    /// Отправляет сообщение одним каналом с полной изоляцией ошибок: исключение канала
    /// или возврат false превращаются в предупреждение журнала, наружу не всплывают.
    /// </summary>
    private async Task SendSafelyAsync(INotificationChannel channel, NotificationMessage message)
    {
        try
        {
            var sent = await channel.SendAsync(message).ConfigureAwait(false);
            if (!sent)
                _logger?.Warn($"Канал «{channel.Name}» не смог отправить уведомление.");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Канал «{channel.Name}»: ошибка отправки: {ex.Message}");
        }
    }
}