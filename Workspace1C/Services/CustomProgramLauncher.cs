using System;
using System.Diagnostics;
using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Запуск пользовательских программ группы «Мои программы» (issue #91). Чистый сервис
/// без UI-зависимостей: формирует <see cref="ProcessStartInfo"/> (файл, аргументы,
/// рабочая папка) и запускает процесс; отсутствующий файл и ошибки запуска возвращаются
/// понятным сообщением (локализованный ключ), без падения интерфейса. На Windows процесс
/// запускается через shell (<c>UseShellExecute = true</c>), на Linux — тем же вызовом с
/// откатом на прямой запуск, если платформа не поддерживает shell-execute.
/// </summary>
public static class CustomProgramLauncher
{
    /// <summary>
    /// Можно ли запустить программу: путь к файлу задан и файл существует. Нечитаемый
    /// (пустой) путь или отсутствующий файл — <c>false</c>.
    /// </summary>
    public static bool IsRunnable(CustomProgram? program)
    {
        if (program is null || string.IsNullOrWhiteSpace(program.FilePath))
            return false;
        try
        {
            return File.Exists(program.FilePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Командная строка программы «файл + аргументы» (для журнала и подсказок).
    /// Аргументы передаются как есть, без разбора и экранирования.
    /// </summary>
    public static string BuildCommandLine(CustomProgram program)
    {
        if (program is null)
            return string.Empty;
        return string.IsNullOrWhiteSpace(program.Arguments)
            ? program.FilePath
            : program.FilePath + " " + program.Arguments;
    }

    /// <summary>
    /// Рабочая папка процесса: явно заданная либо каталог исполняемого файла.
    /// <c>null</c> — определить не удалось (процесс наследует текущий каталог).
    /// </summary>
    public static string? ResolveWorkingDirectory(CustomProgram program)
    {
        if (program is null)
            return null;
        if (!string.IsNullOrWhiteSpace(program.WorkingDirectory))
            return program.WorkingDirectory;
        try
        {
            var dir = Path.GetDirectoryName(program.FilePath);
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Нужен ли повышенный уровень прав для запуска (issue #178): флаг программы
    /// и только Windows — на других платформах повышение прав через UAC/runas
    /// не поддерживается, флаг игнорируется.
    /// </summary>
    public static bool RequiresElevation(CustomProgram? program)
        => program is not null
           && program.RunAsAdministrator
           && OperatingSystem.IsWindows();

    /// <summary>
    /// Аргументы <c>runas.exe</c> для запуска с сохранёнными учётными данными
    /// (issue #178): <c>/savecred /user:<account> "<file>" <args></c>.
    /// Пароль приложение не хранит — его сохраняет Windows при первом запуске.
    /// </summary>
    public static string BuildRunAsArguments(CustomProgram program)
    {
        var args = $"/savecred /user:{program.RunAsUserName} \"{program.FilePath}\"";
        if (!string.IsNullOrWhiteSpace(program.Arguments))
            args += " " + program.Arguments;
        return args;
    }

    /// <summary>
    /// Формирует параметры запуска: исполняемый файл, аргументы, рабочая папка.
    /// <c>UseShellExecute = true</c> — как у родного стартера 1С (Windows).
    /// Issue #178: при <see cref="CustomProgram.RunAsAdministrator"/> на Windows
    /// запуск идёт с повышением прав — UAC-запрос (Verb = "runas") либо
    /// <c>runas /savecred</c>, если задана учётная запись.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(CustomProgram program)
    {
        if (RequiresElevation(program) && !string.IsNullOrWhiteSpace(program.RunAsUserName))
        {
            // runas /savecred: пароль запрашивается один раз и сохраняется Windows.
            var elevated = new ProcessStartInfo
            {
                FileName = "runas.exe",
                Arguments = BuildRunAsArguments(program),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var runAsDir = ResolveWorkingDirectory(program);
            if (!string.IsNullOrWhiteSpace(runAsDir))
                elevated.WorkingDirectory = runAsDir!;
            return elevated;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = program?.FilePath ?? string.Empty,
            Arguments = program?.Arguments ?? string.Empty,
            UseShellExecute = true
        };
        if (RequiresElevation(program))
            startInfo.Verb = "runas"; // UAC-запрос от текущего пользователя
        var workDir = ResolveWorkingDirectory(program!);
        if (!string.IsNullOrWhiteSpace(workDir))
            startInfo.WorkingDirectory = workDir!;
        return startInfo;
    }

    /// <summary>
    /// Запускает программу. Возвращает <c>true</c> при успехе; иначе <paramref name="error"/>
    /// содержит понятное пользователю сообщение (локализованный ключ
    /// <c>Launch.CustomPrograms.Error*</c>), запуск записывается в журнал. Исключения
    /// наружу не выходят — интерфейс не падает.
    /// </summary>
    public static bool TryLaunch(CustomProgram? program, IAppLogger? logger, out string error)
    {
        error = string.Empty;
        if (program is null || string.IsNullOrWhiteSpace(program.FilePath))
        {
            error = LocalizationManager.T("Launch.CustomPrograms.ErrorFileRequired");
            return false;
        }
        if (!IsRunnable(program))
        {
            error = FormatLocalized("Launch.CustomPrograms.ErrorNotFound", program.FilePath);
            TryLog(() => logger?.Warn($"[custom-programs] Файл программы не найден: {program.FilePath}"));
            return false;
        }

        var startInfo = BuildStartInfo(program);
        try
        {
            Process.Start(startInfo);
            TryLog(() => logger?.Info(
                $"[custom-programs] Запущена программа «{program.Name}»: {BuildCommandLine(program)}" +
                (string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                    ? string.Empty
                    : $" (рабочая папка: {startInfo.WorkingDirectory})")));
            return true;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException)
        {
            // На части Unix-платформ shell-execute недоступен: пробуем прямой запуск.
            try
            {
                var direct = BuildStartInfo(program);
                direct.UseShellExecute = false;
                Process.Start(direct);
                TryLog(() => logger?.Info(
                    $"[custom-programs] Запущена программа «{program.Name}» (прямой запуск): {BuildCommandLine(program)}"));
                return true;
            }
            catch (Exception inner)
            {
                error = FormatLocalized(
                    "Launch.CustomPrograms.ErrorStart", program.Name, inner.Message);
                TryLog(() => logger?.Error(
                    $"[custom-programs] Не удалось запустить программу «{program.Name}»", inner));
                return false;
            }
        }
        catch (Exception ex)
        {
            error = FormatLocalized(
                "Launch.CustomPrograms.ErrorStart", program.Name, ex.Message);
            TryLog(() => logger?.Error(
                $"[custom-programs] Не удалось запустить программу «{program.Name}»", ex));
            return false;
        }
    }

    /// <summary>
    /// Локализованное сообщение с подстановкой. Если словарь ещё не инициализирован и
    /// вернул сам ключ (без плейсхолдеров), значения добавляются через пробел — текст
    /// ошибки в любом случае содержит имя программы/путь.
    /// </summary>
    private static string FormatLocalized(string key, params object?[] args)
    {
        var template = LocalizationManager.T(key);
        if (args.Length == 0)
            return template;
        if (!template.Contains('{'))
            return template + " " + string.Join(" ", args);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template + " " + string.Join(" ", args);
        }
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Журнал не должен влиять на запуск программы.
        }
    }
}
