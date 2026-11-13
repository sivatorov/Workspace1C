#if LINUX
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Системный канал уведомлений (Linux/Avalonia, функция №5): команда <c>notify-send</c>
/// из libnotify — стандартный способ показать уведомление в десктоп-окружениях
/// (GNOME, KDE и др.). Запускается отдельным процессом без окна (fire-and-forget),
/// имя приложения задаётся через <c>--app-name</c>. Если notify-send не установлен
/// или уведомления запрещены — тихий no-op (не ошибка).
/// </summary>
public sealed class SystemNotificationChannel : INotificationChannel
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);

    private readonly IInfobaseRepository _repository;

    public SystemNotificationChannel(IInfobaseRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public string Name => "system";

    /// <inheritdoc/>
    public bool IsEnabled(AppSettings settings) => settings.ShowSystemNotifications;

    /// <summary>
    /// Чистая проверка «показывать ли сообщение системным каналом» (issue #131):
    /// общая настройка системных уведомлений плюс для длительных операций —
    /// включённая галочка и выбранный канал «Системные уведомления».
    /// </summary>
    public static bool ShouldShow(AppSettings settings, NotificationMessage message)
    {
        if (settings is null || !settings.ShowSystemNotifications)
            return false;
        if (message?.Event != NotificationEvent.LongOperation)
            return true;

        var longOperation = settings.LongOperationNotifications;
        return longOperation is not null
               && longOperation.Enabled
               && string.Equals(LongOperationNotificationChannels.Normalize(longOperation.Channel),
                   LongOperationNotificationChannels.System, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _repository.LoadSettings();

            // Настройка «Системные уведомления» (AppSettings.ShowSystemNotifications):
            // при false все уведомления подавляются (страховка для прямых вызовов).
            // issue #131: длительные операции дополнительно требуют галочку и канал «Система».
            if (!ShouldShow(settings, message))
                return Task.FromResult(true);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList =
                    {
                        "--app-name=Workspace1C",
                        message.Title ?? string.Empty,
                        message.Message ?? string.Empty
                    }
                }
            };

            if (!process.Start())
                return Task.FromResult(true);

            // Дожидаемся завершения с ограничением по времени: убивать не нужно,
            // но и держать поток планировщика дольше пары секунд тоже не стоит.
            if (!process.WaitForExit((int)StartTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* процесс мог завершиться сам */ }
            }

            return Task.FromResult(true);
        }
        catch
        {
            // notify-send отсутствует / окружение без уведомлений — тихий no-op.
            return Task.FromResult(true);
        }
    }
}
#endif