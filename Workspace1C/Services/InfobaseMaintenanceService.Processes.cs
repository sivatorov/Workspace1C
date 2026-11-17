using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Workspace1C.Services;

/// <summary>
/// Завершение и подсчёт процессов платформы 1С (WPF-часть).
/// </summary>
public static partial class InfobaseMaintenanceService
{
    private static readonly string[] OneCProcessNames =
    {
        "1cv8", "1cv8c", "1cv8s", "1cv8a", "ragent", "rmngr", "rphost"
    };

    /// <summary>
    /// Завершает процессы платформы 1С (1cv8, 1cv8c, …).
    /// Возвращает число завершённых процессов.
    /// </summary>
    public static int KillOneCProcesses()
    {
        var killed = 0;
        foreach (var name in OneCProcessNames)
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var p in list)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                    }
                }
                catch
                {
                    // нет прав / уже завершён
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return killed;
    }

    /// <summary>
    /// Снимок процессов для сводки массового завершения (issue #134, v1.0.1.79):
    /// подробности WMI-снимка (клиентские процессы «1cv8%» с командными строками —
    /// по ним строится карта «PID → имя базы») плюс серверные процессы кластера
    /// (ragent/rmngr/rphost), которых в выборке «1cv8%» нет, хотя
    /// <see cref="KillOneCProcesses()"/> их завершает. Без них счётчик сводки был бы
    /// меньше числа фактически завершённых процессов и расходился бы с Linux-версией,
    /// где <c>SnapshotOneCProcesses</c> уже содержит серверные процессы.
    /// </summary>
    /// <param name="details">Подробности процессов из <c>IRunningInfobasesService.GetRunningDetails()</c>.</param>
    public static IReadOnlyList<RunningOneCProcessDetails> ExtendSnapshotForKillReport(
        IReadOnlyList<RunningOneCProcessDetails>? details)
    {
        var result = (details ?? Array.Empty<RunningOneCProcessDetails>())
            .Where(process => process is not null)
            .ToList();
        var knownPids = new HashSet<int>(result.Select(process => process.Pid));

        foreach (var name in OneCProcessNames)
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in list)
            {
                try
                {
                    if (process.HasExited || !knownPids.Add(process.Id))
                        continue;
                    // Командной строки нет: серверные процессы к базам не относятся,
                    // в карте имён баз они не участвуют — важен только счётчик.
                    result.Add(new RunningOneCProcessDetails(
                        process.Id, name, string.Empty, null, null, null));
                }
                catch
                {
                    // процесс мог исчезнуть во время опроса — пропускаем
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// PID из снимка, которые всё ещё живы после попытки завершения (issue #134,
    /// v1.0.1.79): процессы опрашиваются повторно по тем же именам платформы, и
    /// оставшиеся живыми считаются ошибками завершения — сводка перечислит их базы.
    /// </summary>
    /// <param name="pids">PID снимка, который пытались завершить.</param>
    public static IReadOnlyList<int> FindAliveOneCProcessIds(IEnumerable<int>? pids)
    {
        var expected = pids is null ? new HashSet<int>() : new HashSet<int>(pids);
        if (expected.Count == 0)
            return Array.Empty<int>();

        var alive = new HashSet<int>();
        foreach (var name in OneCProcessNames)
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in list)
            {
                try
                {
                    if (!process.HasExited && expected.Contains(process.Id))
                        alive.Add(process.Id);
                }
                catch
                {
                    // нет доступа к процессу — считаем, что он ещё жив
                    alive.Add(process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return expected.Where(alive.Contains).ToList();
    }

    /// <summary>Число запущенных процессов 1С.</summary>
    public static int CountOneCProcesses()
    {
        var count = 0;
        foreach (var name in OneCProcessNames)
        {
            try
            {
                count += Process.GetProcessesByName(name).Length;
            }
            catch
            {
                // ignore
            }
        }
        return count;
    }
}