namespace Workspace1C.Services;

/// <summary>Запущенный процесс платформы 1С (клиент/конфигуратор).</summary>
/// <param name="ProcessName">Имя процесса (например «1cv8c.exe»).</param>
/// <param name="CommandLine">Полная командная строка процесса (может быть пуста — нет прав на чтение).</param>
/// <param name="IsResponding">Процесс отвечает на запросы (Windows: <c>Process.Responding</c>;
/// Linux: эвристика по /proc/<pid>/stat). По умолчанию true — если проверить не удалось,
/// процесс считается отвечающим.</param>
/// <param name="WindowTitle">Полный заголовок главного окна процесса (issue #131): Windows —
/// <c>Process.MainWindowTitle</c>; Linux — пусто (заголовок окна недоступен). Может быть
/// пустым и на Windows (нет прав/нет окна) — уведомление подставит «окно без заголовка».</param>
public sealed record RunningOneCProcess(
    string ProcessName,
    string CommandLine,
    bool IsResponding = true,
    string WindowTitle = "");

/// <summary>
/// Запущенный процесс платформы 1С с подробностями для инспектора процессов.
/// </summary>
/// <param name="Pid">Идентификатор процесса.</param>
/// <param name="ProcessName">Имя процесса (например «1cv8c.exe»).</param>
/// <param name="CommandLine">Полная командная строка процесса (может быть пуста — нет прав на чтение).</param>
/// <param name="StartTime">Время запуска процесса (локальное); null — определить не удалось.</param>
/// <param name="StartTimeToken">Сырое значение времени старта (Linux: поле starttime из /proc/stat),
/// по которому перед завершением сверяется, что PID не переиспользован. На Windows не используется.</param>
/// <param name="UserName">Имя пользователя, от которого запущен процесс; null — определить не удалось.</param>
public sealed record RunningOneCProcessDetails(
    int Pid,
    string ProcessName,
    string CommandLine,
    DateTime? StartTime,
    string? StartTimeToken,
    string? UserName);

/// <summary>
/// Список запущенных процессов платформы 1С на текущей машине. Используется
/// индикатором «база сейчас запущена»: командные строки процессов сопоставляются
/// с параметрами подключения баз (<see cref="RunningInfobaseMatcher"/>).
/// Реализация обязана тихо деградировать: при любой ошибке (нет прав, WMI
/// недоступен) возвращается пустой список, а не исключение.
/// </summary>
public interface IRunningInfobasesService
{
    /// <summary>Текущие процессы 1С (1cv8/1cv8c и совместимые) с их командными строками.</summary>
    System.Collections.Generic.IReadOnlyList<RunningOneCProcess> GetRunning();

    /// <summary>
    /// Подробности о каждом запущенном процессе 1С: PID, время старта, командная строка
    /// и владелец процесса (инспектор процессов). Та же политика тихой деградации: при
    /// любой ошибке возвращается пустой список.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails();
}
