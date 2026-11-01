using System;
using Workspace1C.Services;

namespace Workspace1C.Models;

/// <summary>
/// Режим запуска процесса платформы 1С, сопоставленного с базой
/// (индикатор «база сейчас запущена» различает Предприятие и Конфигуратор).
/// </summary>
public enum RunningKind
{
    /// <summary>Процесс не является клиентом платформы 1С.</summary>
    None,

    /// <summary>
    /// 1С:Предприятие: толстый клиент (1cv8/1cv8x64 без DESIGNER) или тонкий клиент
    /// (1cv8c — всегда Предприятие, даже с ключами Конфигуратора в cmdline).
    /// </summary>
    Enterprise,

    /// <summary>Конфигуратор: толстый клиент 1cv8/1cv8x64 с ключами DESIGNER/CONFIG.</summary>
    Configurator
}

/// <summary>
/// Чистый классификатор режима запуска процесса платформы 1С по имени процесса и
/// командной строке. Используется монитором запущенных баз
/// (<see cref="ViewModels.MainViewModel"/>, <c>ApplyRunningProcesses</c>), чтобы
/// индикатор у имени базы показывал, что именно запущено: 1С:Предприятие (Play),
/// Конфигуратор (ключ) или оба. Не зависит от платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class RunningKindClassifier
{
    /// <summary>
    /// Определяет режим запуска процесса 1С:
    /// <list type="bullet">
    /// <item><c>1cv8c</c>/<c>1cv8c.exe</c> — тонкий клиент: всегда 1С:Предприятие;</item>
    /// <item><c>1cv8</c>/<c>1cv8.exe</c> и старые <c>1cv8x64</c> — толстый клиент:
    /// ключи DESIGNER/CONFIG в командной строке → Конфигуратор, иначе
    /// (ENTERPRISE, пустая cmdline, служебные запуски /Execute и т.п.) → Предприятие;</item>
    /// <item>любое другое имя процесса → <see cref="RunningKind.None"/>.</item>
    /// </list>
    /// Сравнение имени процесса — без учёта регистра, каталог и расширение «.exe»
    /// отбрасываются. Режим командной строки определяется переиспользуемым
    /// <see cref="ProcessCommandLineParser.DetectMode(string?)"/>.
    /// </summary>
    public static RunningKind Classify(string? processName, string? commandLine)
    {
        var name = ProcessFileName(processName);

        // Тонкий клиент всегда Предприятие: проверяется раньше «1cv8»,
        // чтобы точное сравнение имён не зависело от порядка.
        if (EqualsProcess(name, "1cv8c"))
            return RunningKind.Enterprise;

        if (EqualsProcess(name, "1cv8") || EqualsProcess(name, "1cv8x64"))
            return ProcessCommandLineParser.DetectMode(commandLine) == OneCProcessLaunchMode.Configurator
                ? RunningKind.Configurator
                : RunningKind.Enterprise;

        return RunningKind.None;
    }

    /// <summary>Имя файла процесса без каталога и расширения «.exe».</summary>
    private static string ProcessFileName(string? processName)
    {
        var name = (processName ?? string.Empty).Trim();

        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
            name = name[(slash + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name;
    }

    /// <summary>Сравнение имени процесса без учёта регистра.</summary>
    private static bool EqualsProcess(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
}
