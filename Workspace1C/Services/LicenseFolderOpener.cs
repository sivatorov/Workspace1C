using System;
using System.Diagnostics;
using System.IO;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Открытие папки с файлом лицензии (issue #83, v1.0.1.27). Общий для WPF и
/// Avalonia (обе платформы живут в одной сборке): Windows — <c>explorer.exe /select</c>
/// с выделением файла, Linux — <c>xdg-open</c> по каталогу файла. Ошибки не
/// бросаются — результат в <see cref="TryOpenFolderWithFile"/>; запуск процесса
/// вынесен в <see cref="ProcessStarter"/> — подменяется в юнит-тестах.
/// </summary>
public static class LicenseFolderOpener
{
    /// <summary>Точка подмены запуска процесса в тестах; true — запуск удался.</summary>
    internal static Func<ProcessStartInfo, bool>? ProcessStarter { get; set; }

    /// <summary>Сбрасывает подмену запуска процесса (очистка после теста).</summary>
    internal static void ResetProcessStarter() => ProcessStarter = null;

    /// <summary>
    /// Путь к файлу лицензии, если он задан и файл существует; иначе null
    /// (лицензия только из вывода ring, файл не найден и т.п.).
    /// </summary>
    public static string? GetExistingFilePath(ProgramLicense? license)
        => license is null || string.IsNullOrWhiteSpace(license.FilePath) || !File.Exists(license.FilePath)
            ? null
            : license.FilePath;

    /// <summary>
    /// Открывает папку с выделенным файлом лицензии; false — файл не найден или
    /// запуск не удался (вызывающий код показывает тихое сообщение в статус-строку).
    /// </summary>
    public static bool TryOpenFolderWithFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var startInfo = CreateStartInfo(filePath);
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

    private static ProcessStartInfo CreateStartInfo(string filePath)
    {
#if WINDOWS
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        };
#else
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = "/";
        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        };
#endif
    }
}
