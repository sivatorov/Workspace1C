#if WINDOWS
using System;
using System.Threading.Tasks;
using System.Windows;
using Workspace1C.Models;
using Forms = System.Windows.Forms;

namespace Workspace1C.Services;

/// <summary>
/// Системный канал уведомлений (Windows/WPF, функция №5): balloon-tip значка в системном
/// трее. Используется уже существующий <see cref="Forms.NotifyIcon"/> главного окна
/// (<see cref="MainWindow.TrayIconInstance"/>) — это самый надёжный путь без AUMID/WinRT:
/// для Toast-уведомлений Windows 10/11 потребовался бы AppUserModelID, здесь он не нужен.
/// Если значок трея недоступен (приложение запущено без трея) — тихий no-op (не ошибка).
/// Показ выполняется через Dispatcher главного окна: вызовы приходят из фоновых потоков
/// (таймер планировщика, задача автообновления), а WinForms-контрол требует UI-потока.
/// </summary>
public sealed class SystemNotificationChannel : INotificationChannel
{
    private static readonly TimeSpan BalloonTimeout = TimeSpan.FromSeconds(5);

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
            // при false все уведомления подавляются (страховка для прямых вызовов —
            // диспетчер уже отфильтровал через IsEnabled). issue #131: длительные
            // операции дополнительно требуют включённую галочку и канал «Система».
            if (!ShouldShow(settings, message))
                return Task.FromResult(true);

            var tray = MainWindow.TrayIconInstance;
            if (tray is null)
                return Task.FromResult(true);

            var app = Application.Current;
            if (app is null)
                return Task.FromResult(true);

            if (app.Dispatcher.CheckAccess())
            {
                ShowBalloon(tray, message.Title, message.Message);
            }
            else
            {
                // Не блокируем вызывающий поток: откладываем показ на UI-поток.
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ShowBalloon(tray, message.Title, message.Message); }
                    catch { /* уведомление не должно ронять приложение */ }
                }));
            }

            return Task.FromResult(true);
        }
        catch
        {
            // Трей недоступен / нет UI-потока — тихий no-op.
            return Task.FromResult(true);
        }
    }

    private static void ShowBalloon(Forms.NotifyIcon tray, string title, string message)
    {
        if (!tray.Visible)
            return;
        tray.ShowBalloonTip((int)BalloonTimeout.TotalMilliseconds, title, message, Forms.ToolTipIcon.Info);
    }
}
#endif