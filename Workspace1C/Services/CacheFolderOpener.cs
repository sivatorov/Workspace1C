using System;
using System.Diagnostics;
using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Открытие папок кеша 1С в проводнике/файловом менеджере (issue #110, v1.0.1.40).
/// Общий для WPF и Avalonia (обе платформы живут в одной сборке): Windows —
/// <c>explorer.exe</c> по каталогу, Linux — <c>xdg-open</c>. Ошибки не бросаются —
/// результат в <see cref="TryOpenDirectory"/>; запуск процесса вынесен в
/// <see cref="ProcessStarter"/> — подменяется в юнит-тестах.
/// </summary>
public static class CacheFolderOpener
{
    /// <summary>Точка подмены запуска процесса в тестах; true — запуск удался.</summary>
    internal static Func<ProcessStartInfo, bool>? ProcessStarter { get; set; }

    /// <summary>Сбрасывает подмену запуска процесса (очистка после теста).</summary>
    internal static void ResetProcessStarter() => ProcessStarter = null;

    /// <summary>
    /// Открывает каталог кеша в проводнике/файловом менеджере; false — каталог не
    /// найден или запуск не удался (вызывающий код решает, что показать пользователю).
    /// </summary>
    public static bool TryOpenDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        try
        {
            var startInfo = CreateStartInfo(directory);
            if (ProcessStarter is { } starter)
                return starter(startInfo);

            using var process = Process.Start(startInfo);
            return true;
        }
        catch
        {
            // xdg-open/explorer может отсутствовать в минимальных окружениях — тихо игнорируем.
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string directory)
    {
#if WINDOWS
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        };
#else
        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        };
#endif
    }
}
