#if WINDOWS
using System;
using System.Windows.Threading;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Платформенная обвязка сторожа UI-потока (issue #143, WPF): <see cref="DispatcherTimer"/>
    /// раз в 500 мс отмечает heartbeat, выделенный поток <see cref="UiThreadWatchdogCore"/>
    /// пишет WARN, если UI-поток не отвечает больше 3 секунд, с последним этапом операции.
    /// </summary>
    internal static class UiThreadWatchdog
    {
        private static readonly object Sync = new();
        private static UiThreadWatchdogCore? _core;
        private static DispatcherTimer? _timer;

        /// <summary>Однократно запускает сторож на диспетчере окна.</summary>
        public static void EnsureStarted(Dispatcher dispatcher)
        {
            if (dispatcher is null)
                return;

            lock (Sync)
            {
                if (_core is not null)
                    return;

                _core = new UiThreadWatchdogCore(
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(500),
                    () => DbmsBackupTrace.LastStageDescription,
                    message => DbmsBackupTrace.Warn("ui-stall", message));
                _core.Start();

                _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _timer.Tick += (_, _) => _core?.Beat();
                _timer.Start();
            }
        }
    }
}
#elif LINUX
using System;
using Avalonia.Threading;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Платформенная обвязка сторожа UI-потока (issue #143, Avalonia): <see cref="DispatcherTimer"/>
    /// раз в 500 мс отмечает heartbeat, выделенный поток <see cref="UiThreadWatchdogCore"/>
    /// пишет WARN, если UI-поток не отвечает больше 3 секунд, с последним этапом операции.
    /// </summary>
    internal static class UiThreadWatchdog
    {
        private static readonly object Sync = new();
        private static UiThreadWatchdogCore? _core;
        private static DispatcherTimer? _timer;

        /// <summary>Однократно запускает сторож на UI-диспетчере Avalonia.</summary>
        public static void EnsureStarted()
        {
            lock (Sync)
            {
                if (_core is not null)
                    return;

                _core = new UiThreadWatchdogCore(
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(500),
                    () => DbmsBackupTrace.LastStageDescription,
                    message => DbmsBackupTrace.Warn("ui-stall", message));
                _core.Start();

                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _timer.Tick += (_, _) => _core?.Beat();
                _timer.Start();
            }
        }
    }
}
#endif
