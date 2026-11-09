using System;
using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Отладочный флаг сопоставления запущенных процессов 1С с базами (issue #103):
/// при переменной окружения <c>CM_DEBUG_RUNNING_MATCH=1</c> подробные диагностические
/// записи (список процессов с командными строками, список баз с параметрами
/// подключения, результат каждого прохода и причины отказа) пишутся в файл
/// <c>running-match-debug.log</c> рядом с журналом приложения
/// (<see cref="PlatformPaths.LogDirectory"/>). По образцу
/// <see cref="PlatformUpdateDebug"/> (issue #361): статический доступ — чтобы
/// логировать из статического <see cref="RunningInfobaseMatcher"/>, у которого
/// нет инжектируемого логгера.
/// </summary>
public static class RunningMatchDebug
{
    private static readonly bool _enabled = IsEnabledByEnvironment();
    private static readonly object _sync = new();

    /// <summary>True — отладочный флаг включён (переменная окружения
    /// <c>CM_DEBUG_RUNNING_MATCH=1</c>).</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>Пишет строку в файл отладочного журнала (только при включённом флаге).
    /// Сбой записи не роняет приложение — диагностика молча пропускается.</summary>
    public static void Log(string message)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (_sync)
            {
                var directory = PlatformPaths.LogDirectory;
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "running-match-debug.log"),
                    line,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Диагностика не должна ронять приложение.
        }
    }

    private static bool IsEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("CM_DEBUG_RUNNING_MATCH");
        return value is "1" or "true" or "True" or "TRUE";
    }
}
