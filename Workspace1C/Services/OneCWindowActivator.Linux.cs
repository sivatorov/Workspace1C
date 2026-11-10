#if LINUX
using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Linux-зеркало активации окна запущенной базы (issue #339, расширено issue #120).
/// Активация чужих окон на X11/Wayland требует внешних утилит (wmctrl/xdotool),
/// поэтому на Linux активация окон считается не поддержанной: перечисление окон
/// (<see cref="GetWindows"/>) возвращает пустой список, <see cref="Activate(long)"/>
/// — false. Вызывающий код выполняет graceful fallback: при запуске Конфигуратора
/// выполняется обычный запуск нового экземпляра, клик по значку-индикатору
/// Предприятия тихо игнорируется.
/// </summary>
public static class OneCWindowActivator
{
    /// <summary>На Linux активация окна 1С не поддержана — всегда false.</summary>
    public static bool Activate(Infobase infobase) => false;

    /// <summary>На Linux перечисление окон 1С не поддержано — всегда пустой список (issue #120).</summary>
    public static IReadOnlyList<RunningWindowInfo> GetWindows(Infobase infobase, RunningKind? kind)
        => Array.Empty<RunningWindowInfo>();

    /// <summary>На Linux активация окна по хэндлу не поддержана — всегда false (issue #120).</summary>
    public static bool Activate(long handle) => false;
}
#endif
