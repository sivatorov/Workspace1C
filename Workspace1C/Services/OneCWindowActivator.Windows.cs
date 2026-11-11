#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Активация окна уже запущенной базы 1С (issue #339): при включённом отборе
/// «Только запущенные» двойной клик/Enter поднимают окно процесса 1С этой базы
/// вместо повторного запуска. PID процесса находится сопоставлением командной
/// строки (<see cref="RunningInfobaseMatcher"/>), затем через Win32-перечисление
/// окон выбирается видимое главное окно процесса и выводится на передний план.
/// <para>
/// Issue #120 «Переход к открытому окну Конфигуратора/Предприятия»: помимо
/// активации первого окна добавлено перечисление видимых окон процессов базы
/// (<see cref="GetWindows"/>) с фильтром по режиму запуска
/// (<see cref="RunningKindClassifier"/>) — на нём построены активация открытого
/// Конфигуратора вместо повторного запуска и выбор окна при нескольких
/// запущенных экземплярах Предприятия.
/// </para>
/// </summary>
public static class OneCWindowActivator
{
    private const int SwRestore = 9;

    /// <summary>Активирует видимое окно процесса 1С, подключённого к базе. Возвращает true при успехе.</summary>
    public static bool Activate(Infobase infobase)
    {
        // Issue #120: перечисление вынесено в GetWindows — единый механизм для
        // активации «Только запущенные» (issue #339) и перехода к открытым окнам.
        var windows = GetWindows(infobase, null);
        if (windows.Count == 0)
            return false;

        return Activate(windows[0].Handle);
    }

    /// <summary>
    /// Issue #120: перечисляет видимые окна с заголовком процессов 1С, подключённых
    /// к базе. kind — фильтр по режиму запуска (Предприятие/Конфигуратор через
    /// <see cref="RunningKindClassifier"/>); null — все процессы базы.
    /// Служебные окна без заголовка не считаются главными окнами 1С.
    /// <para>
    /// Issue #120 (v1.0.1.55, 7OH): каждое окно несёт режим запуска процесса
    /// (<see cref="RunningKind"/>) и время старта процесса — в пунктах выбора окна
    /// показывается «Предприятие/Конфигуратор + время запуска» вместо заголовка.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RunningWindowInfo> GetWindows(Infobase infobase, RunningKind? kind)
    {
        if (infobase is null)
            return Array.Empty<RunningWindowInfo>();

        // Сведения монитора процессов: режим запуска и время старта по PID
        // (для пунктов выбора окна «Предприятие/Конфигуратор + время запуска»).
        var details = AppServices.TryGetService<IRunningInfobasesService>()?.GetRunningDetails()
            ?? Array.Empty<RunningOneCProcessDetails>();
        var pidInfo = new Dictionary<int, (RunningKind Kind, DateTime? StartedAt)>();
        foreach (var d in details)
        {
            if (!RunningInfobaseMatcher.MatchesCommandLine(infobase, d.CommandLine))
                continue;
            var processKind = RunningKindClassifier.Classify(d.ProcessName, d.CommandLine);
            if (kind is not null && processKind != kind)
                continue;
            pidInfo[d.Pid] = (processKind, d.StartTime);
        }

        if (pidInfo.Count == 0)
            return Array.Empty<RunningWindowInfo>();
        var pids = new int[pidInfo.Count];
        pidInfo.Keys.CopyTo(pids, 0);

        var windows = new List<RunningWindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true; // продолжаем перечисление

            GetWindowThreadProcessId(hWnd, out var pid);
            if (!pidInfo.TryGetValue((int)pid, out var info))
                return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true; // окна без заголовка — служебные, пропускаем

            var buffer = new StringBuilder(length + 1);
            _ = GetWindowText(hWnd, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            if (title.Length == 0)
                return true;

            windows.Add(new RunningWindowInfo(hWnd.ToInt64(), (int)pid, title, info.Kind, info.StartedAt));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>Активирует окно по хэндлу (issue #120): восстановление + передний план.</summary>
    public static bool Activate(long handle)
    {
        if (handle == 0)
            return false;

        var hWnd = new IntPtr(handle);
        ShowWindow(hWnd, SwRestore);
        return SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// PID процессов 1С, подключённых к базе, с опциональным фильтром по режиму
    /// запуска (issue #120). Сопоставление командной строки — как в мониторе.
    /// </summary>
    private static int[] GetProcessIds(Infobase infobase, RunningKind? kind)
    {
        var service = AppServices.TryGetService<IRunningInfobasesService>();
        var details = service?.GetRunningDetails() ?? Array.Empty<RunningOneCProcessDetails>();

        return details
            .Where(d => RunningInfobaseMatcher.MatchesCommandLine(infobase, d.CommandLine))
            .Where(d => kind is null || RunningKindClassifier.Classify(d.ProcessName, d.CommandLine) == kind)
            .Select(d => d.Pid)
            .Distinct()
            .ToArray();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
#endif