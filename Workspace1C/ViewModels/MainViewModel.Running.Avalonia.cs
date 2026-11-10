#if LINUX
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Avalonia-часть монитора запущенных баз: доставка результата в UI-поток.</summary>
public partial class MainViewModel
{
    partial void DispatchOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>Активация окна 1С на Linux не поддержана (нужны wmctrl/xdotool) — no-op.</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase)
    {
        // OneCWindowActivator.Activate возвращает false; тихо пропускаем.
        _ = infobase;
    }

    /// <summary>
    /// Перечисление окон 1С на Linux не поддержано (нужны wmctrl/xdotool) —
    /// список остаётся пустым; graceful fallback issue #120 (обычный запуск,
    /// клик по индикатору тихо игнорируется).
    /// </summary>
    partial void GetRunningWindows(Infobase infobase, RunningKind kind, List<RunningWindowInfo> windows)
    {
        _ = infobase;
        _ = kind;
        _ = windows;
    }

    /// <summary>Активация окна по хэндлу на Linux не поддержана — no-op (issue #120).</summary>
    partial void ActivateWindowHandle(long handle)
    {
        _ = handle;
    }
}
#endif
