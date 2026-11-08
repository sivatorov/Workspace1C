#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Workspace1C.Services;

/// <summary>
/// Список запущенных процессов платформы 1С (Linux): обход /proc/[0-9]*/cmdline —
/// аргументы процесса разделены нулевыми байтами. Клиентские процессы: 1cv8,
/// 1cv8c (тонкий клиент). Ошибки доступа к отдельным процессам пропускаются,
/// любые проблемы каталога дают пустой список.
/// </summary>
public sealed class RunningInfobasesService : IRunningInfobasesService
{
    public System.Collections.Generic.IReadOnlyList<RunningOneCProcess> GetRunning()
    {
        var result = new List<RunningOneCProcess>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(dir);
                if (name.Length == 0 || name[0] < '0' || name[0] > '9')
                    continue;
                int.TryParse(name, out var pid);

                string content;
                try { content = File.ReadAllText(Path.Combine(dir, "cmdline")); }
                catch { continue; /* процесс завершился или нет прав */ }

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var args = content.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 0)
                    continue;

                var exe = Path.GetFileName(args[0]);
                if (!exe.StartsWith("1cv8", StringComparison.Ordinal))
                    continue;

                // issue #310: эвристика «не отвечает» — state 'D' в /proc/<pid>/stat
                // (uninterruptible sleep); подробности и ограничение — в LinuxProcessStateInspector.
                var stat = pid > 0 ? ReadStat(pid) : null;
                var responding = pid <= 0 || LinuxProcessStateInspector.StatIndicatesResponding(stat);
                // issue #103: родитель (поле 4 /proc/<pid>/stat) — связь клиента отладки с Конфигуратором.
                result.Add(new RunningOneCProcess(exe, string.Join(" ", args), responding, pid,
                    RunningInfobaseMatcher.ParseParentPidFromStat(stat)));
            }
        }
        catch
        {
            // /proc недоступен (нестандартное окружение): тихо возвращаем пусто.
        }

        return result;
    }

    /// <summary>Содержимое /proc/<pid>/stat; null — процесс исчез или нет прав.</summary>
    private static string? ReadStat(int pid)
    {
        try { return File.ReadAllText($"/proc/{pid}/stat"); }
        catch { return null; }
    }

    /// <summary>
    /// Подробности процессов 1С (инспектор процессов): переиспользует фильтрацию
    /// <see cref="LinuxProc.Enumerate1C"/> (comm/путь исполняемого файла в установках
    /// платформы). Время старта — через /proc (токен для сверки при завершении)
    /// и System.Diagnostics.Process.StartTime (локальное); владелец — Uid из
    /// /proc/<pid>/status, сопоставленный с /etc/passwd.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails()
    {
        var result = new List<RunningOneCProcessDetails>();
        try
        {
            var passwd = LoadPasswd();
            foreach (var p in LinuxProc.Enumerate1C())
            {
                try
                {
                    var startTime = ReadStartTime(p.Pid);
                    var userName = ReadUserName(p.Pid, passwd);
                    result.Add(new RunningOneCProcessDetails(
                        p.Pid, p.Name, p.CmdLine, startTime, p.StartTime, userName));
                }
                catch { /* процесс мог завершиться во время опроса */ }
            }
        }
        catch
        {
            // /proc недоступен: инспектор показывает пустой список.
        }

        return result;
    }

    /// <summary>Время запуска процесса (локальное) через Process.StartTime; null — не удалось.</summary>
    private static DateTime? ReadStartTime(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Имя владельца процесса: первый (реальный) Uid из /proc/<pid>/status →
    /// имя из /etc/passwd. Чужой процесс без прав — null.
    /// </summary>
    private static string? ReadUserName(int pid, IReadOnlyDictionary<uint, string> passwd)
    {
        try
        {
            var status = File.ReadAllText($"/proc/{pid}/status");
            foreach (var line in status.Split('\n'))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                    continue;
                var uidText = line.Substring(4).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (uint.TryParse(uidText, out var uid) && passwd.TryGetValue(uid, out var name))
                    return name;
                return uidText;
            }
        }
        catch
        {
            // нет прав на /proc/<pid>/status — владелец остаётся неизвестным
        }

        return null;
    }

    /// <summary>Кэш имён пользователей из /etc/passwd (uid → имя).</summary>
    private static IReadOnlyDictionary<uint, string> LoadPasswd()
    {
        var map = new Dictionary<uint, string>();
        try
        {
            foreach (var line in File.ReadAllLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                // name:x:uid:gid:gecos:home:shell
                if (parts.Length < 3)
                    continue;
                if (uint.TryParse(parts[2], out var uid) && parts[0].Length > 0)
                    map[uid] = parts[0];
            }
        }
        catch
        {
            // /etc/passwd недоступен — владельцы останутся числовыми Uid.
        }

        return map;
    }
}
#endif
