#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Windows-часть монитора запущенных баз: доставка результата в UI-поток WPF.</summary>
public partial class MainViewModel
{
    partial void DispatchOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    /// <summary>Активирует окно процесса 1С базы через Win32 (issue #339).</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase)
    {
        // GetRunningDetails может занять время (обход процессов) — запускаем вне UI-потока.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                OneCWindowActivator.Activate(infobase);
            }
            catch
            {
                // Активация не должна ломать интерфейс.
            }
        });
    }

    /// <summary>
    /// Перечисление окон 1С базы через EnumWindows с фильтром по режиму запуска
    /// (issue #120). GetRunningDetails может занять время — вызывать вне UI-потока.
    /// </summary>
    partial void GetRunningWindows(Infobase infobase, RunningKind kind, List<RunningWindowInfo> windows)
        => windows.AddRange(OneCWindowActivator.GetWindows(infobase, kind));

    /// <summary>Активация окна по хэндлу через Win32 (issue #120).</summary>
    partial void ActivateWindowHandle(long handle)
        => OneCWindowActivator.Activate(handle);
}
#endif
