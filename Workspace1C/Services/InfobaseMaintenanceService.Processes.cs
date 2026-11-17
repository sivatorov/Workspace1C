using System.Diagnostics;

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
