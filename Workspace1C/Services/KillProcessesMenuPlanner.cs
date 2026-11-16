using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Тип приложения платформы 1С для подписи пункта подменю «Завершить процессы»
/// (issue #134): тонкий/толстый клиент, конфигуратор, веб-клиент. Неизвестное имя
/// процесса отображается как есть.
/// </summary>
public enum OneCApplicationKind
{
    /// <summary>Имя процесса не распознано — в подписи показывается само имя.</summary>
    Unknown = 0,

    /// <summary>Тонкий клиент (<c>1cv8c</c>).</summary>
    ThinClient,

    /// <summary>Толстый клиент (<c>1cv8</c>/<c>1cv8x64</c> без DESIGNER/CONFIG).</summary>
    ThickClient,

    /// <summary>Конфигуратор (<c>1cv8</c>/<c>1cv8x64</c> с DESIGNER/CONFIG).</summary>
    Configurator,

    /// <summary>Веб-клиент (<c>1cv8s</c>).</summary>
    WebClient
}

/// <summary>
/// Готовый пункт подменю завершения процессов (issue #134): сам процесс и его
/// локализованная подпись «пользователь, приложение, время старта, PID».
/// </summary>
/// <param name="Process">Запущенный процесс 1С.</param>
/// <param name="Label">Подпись пункта меню.</param>
public sealed record KillProcessMenuItem(RunningOneCProcessDetails Process, string Label);

/// <summary>
/// Чистая логика подменю «Завершить процессы» контекстного меню списка баз
/// (issue #134, автор ArtyoZ). Не зависит от платформы и UI — используется и
/// WPF-версией, и Avalonia, поэтому покрыта юнит-тестами
/// (<c>Issue134KillProcessesTests</c>).
/// <list type="bullet">
/// <item>классификация приложения по имени процесса и командной строке
/// (<see cref="ClassifyApplication"/>);</item>
/// <item>подпись пункта «пользователь, приложение, время старта, PID»
/// (<see cref="FormatItemLabel"/>) — тексты только через ключи локализации;</item>
/// <item>сортировка по времени старта, процессы без времени — в конце
/// (<see cref="SortByStartTime"/>);</item>
/// <item>отбор процессов выделенных баз (для группы — всех баз группы) через
/// <see cref="RunningInfobaseMatcher"/>, без дублей PID (<see cref="SelectForBases"/>).</item>
/// </list>
/// </summary>
public static class KillProcessesMenuPlanner
{
    /// <summary>Ключ заголовка подменю «Завершить процессы».</summary>
    public const string MenuTitleKey = "KillProcesses.MenuTitle";

    /// <summary>Ключ формата подписи пункта: «{0}, {1}, {2}, {3}».</summary>
    public const string ItemFormatKey = "KillProcesses.ItemFormat";

    /// <summary>Ключ подписи неизвестного пользователя.</summary>
    public const string UnknownUserKey = "KillProcesses.UnknownUser";

    /// <summary>Ключ подписи неизвестного времени старта (прочерк).</summary>
    public const string UnknownStartTimeKey = "KillProcesses.UnknownStartTime";

    /// <summary>Ключ подписи тонкого клиента.</summary>
    public const string ThinClientKey = "KillProcesses.ThinClient";

    /// <summary>Ключ подписи толстого клиента.</summary>
    public const string ThickClientKey = "KillProcesses.ThickClient";

    /// <summary>Ключ подписи конфигуратора.</summary>
    public const string ConfiguratorKey = "KillProcesses.Configurator";

    /// <summary>Ключ подписи веб-клиента.</summary>
    public const string WebClientKey = "KillProcesses.WebClient";

    /// <summary>Ключ подтверждения завершения процесса (подставляется подпись пункта).</summary>
    public const string ConfirmKey = "KillProcesses.Confirm";

    /// <summary>Ключ ошибки завершения без причины.</summary>
    public const string KillFailedKey = "KillProcesses.KillFailedFormat";

    /// <summary>Ключ ошибки завершения с текстом причины.</summary>
    public const string KillFailedDetailKey = "KillProcesses.KillFailedDetailFormat";

    /// <summary>Формат времени старта в подписи: «09:09:55 16.11.2026».</summary>
    public const string StartTimeFormat = "HH:mm:ss dd.MM.yyyy";

    /// <summary>
    /// Определяет тип приложения 1С по имени процесса и командной строке:
    /// <list type="bullet">
    /// <item><c>1cv8c</c> → тонкий клиент;</item>
    /// <item><c>1cv8</c>/<c>1cv8x64</c> с ключами DESIGNER/CONFIG → конфигуратор,
    /// без них → толстый клиент;</item>
    /// <item><c>1cv8s</c> → веб-клиент;</item>
    /// <item>прочее имя → <see cref="OneCApplicationKind.Unknown"/>.</item>
    /// </list>
    /// Каталог и расширение «.exe» отбрасываются (Linux — имена без расширения),
    /// регистр не важен. Режим командной строки определяется переиспользуемым
    /// <see cref="RunningKindClassifier"/> / <see cref="ProcessCommandLineParser"/>.
    /// </summary>
    public static OneCApplicationKind ClassifyApplication(string? processName, string? commandLine)
    {
        var name = ProcessFileName(processName);

        if (EqualsProcess(name, "1cv8c"))
            return OneCApplicationKind.ThinClient;

        if (EqualsProcess(name, "1cv8s"))
            return OneCApplicationKind.WebClient;

        if (EqualsProcess(name, "1cv8") || EqualsProcess(name, "1cv8x64"))
        {
            return RunningKindClassifier.Classify(processName, commandLine) == RunningKind.Configurator
                ? OneCApplicationKind.Configurator
                : OneCApplicationKind.ThickClient;
        }

        return OneCApplicationKind.Unknown;
    }

    /// <summary>
    /// Ключ локализации подписи типа приложения; null — тип не распознан
    /// (в подписи показывается имя процесса).
    /// </summary>
    public static string? ApplicationTextKey(OneCApplicationKind kind) => kind switch
    {
        OneCApplicationKind.ThinClient => ThinClientKey,
        OneCApplicationKind.ThickClient => ThickClientKey,
        OneCApplicationKind.Configurator => ConfiguratorKey,
        OneCApplicationKind.WebClient => WebClientKey,
        _ => null
    };

    /// <summary>
    /// Подпись типа приложения: локализованный текст для известных типов,
    /// имя процесса (без каталога и «.exe») — для неизвестных.
    /// </summary>
    public static string ApplicationDisplayName(
        string? processName, string? commandLine, Func<string, string> translate)
    {
        if (translate is null)
            throw new ArgumentNullException(nameof(translate));

        var key = ApplicationTextKey(ClassifyApplication(processName, commandLine));
        return key is null ? ProcessFileName(processName) : translate(key);
    }

    /// <summary>
    /// Подпись пункта меню: «пользователь, приложение, время старта, PID».
    /// Пользователь неизвестен → ключ <see cref="UnknownUserKey"/>; время
    /// неизвестно → прочерк (<see cref="UnknownStartTimeKey"/>). Формат и все
    /// тексты — из словаря локализации (без хардкода русского текста).
    /// </summary>
    public static string FormatItemLabel(
        RunningOneCProcessDetails process, Func<string, string> translate)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));
        if (translate is null)
            throw new ArgumentNullException(nameof(translate));

        var user = string.IsNullOrWhiteSpace(process.UserName)
            ? translate(UnknownUserKey)
            : process.UserName!.Trim();

        var application = ApplicationDisplayName(process.ProcessName, process.CommandLine, translate);

        var startTime = process.StartTime is { } time
            ? time.ToString(StartTimeFormat, CultureInfo.InvariantCulture)
            : translate(UnknownStartTimeKey);

        return string.Format(
            translate(ItemFormatKey), user, application, startTime, process.Pid);
    }

    /// <summary>
    /// Сортировка по времени старта по возрастанию; процессы без времени — в конце.
    /// Сортировка стабильная: при равных значениях сохраняется исходный порядок.
    /// </summary>
    public static IReadOnlyList<RunningOneCProcessDetails> SortByStartTime(
        IEnumerable<RunningOneCProcessDetails>? processes)
    {
        if (processes is null)
            return Array.Empty<RunningOneCProcessDetails>();

        return processes
            .Where(p => p is not null)
            .OrderBy(p => p.StartTime ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Отбирает процессы выделенных баз (для группы сюда приходят все базы группы).
    /// Сопоставление командных строк — существующим <see cref="RunningInfobaseMatcher"/>
    /// (прямые ключи /F//S, /IBConnectionString и /IBName по уникальному имени базы).
    /// Один процесс попадает в результат один раз (без дублей PID).
    /// </summary>
    public static IReadOnlyList<RunningOneCProcessDetails> SelectForBases(
        IEnumerable<Infobase>? infobases,
        IReadOnlyList<RunningOneCProcessDetails>? processes)
    {
        if (processes is null || processes.Count == 0)
            return Array.Empty<RunningOneCProcessDetails>();

        var bases = (infobases ?? Enumerable.Empty<Infobase>())
            .Where(ib => ib?.Connection is not null)
            .ToList();
        if (bases.Count == 0)
            return Array.Empty<RunningOneCProcessDetails>();

        // Матчер работает с облегчённым RunningOneCProcess; строим параллельный
        // список и запоминаем индекс каждого процесса (ссылочное сравнение —
        // записи с одинаковой командной строкой не должны «склеиваться»).
        var proxies = new List<RunningOneCProcess>(processes.Count);
        var indexByProxy = new Dictionary<RunningOneCProcess, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            if (process is null)
                continue;
            var proxy = new RunningOneCProcess(
                process.ProcessName ?? string.Empty,
                process.CommandLine ?? string.Empty);
            proxies.Add(proxy);
            indexByProxy[proxy] = i;
        }

        var matched = RunningInfobaseMatcher.MatchProcesses(bases, proxies);
        var matchedIndexes = new HashSet<int>();
        foreach (var list in matched.Values)
        {
            foreach (var proxy in list)
            {
                if (indexByProxy.TryGetValue(proxy, out var index))
                    matchedIndexes.Add(index);
            }
        }

        var result = new List<RunningOneCProcessDetails>(matchedIndexes.Count);
        var seenPids = new HashSet<int>();
        foreach (var index in matchedIndexes.OrderBy(i => i))
        {
            var process = processes[index];
            if (seenPids.Add(process.Pid))
                result.Add(process);
        }

        return result;
    }

    /// <summary>
    /// Полный план подменю: процессы выделенных баз (для группы — всех баз группы),
    /// отсортированные по времени старта, с готовыми подписями. Пустой список —
    /// подменю скрывается.
    /// </summary>
    public static IReadOnlyList<KillProcessMenuItem> BuildMenuItems(
        IEnumerable<Infobase>? infobases,
        IReadOnlyList<RunningOneCProcessDetails>? processes,
        Func<string, string> translate)
    {
        if (translate is null)
            throw new ArgumentNullException(nameof(translate));

        var selected = SortByStartTime(SelectForBases(infobases, processes));
        var items = new List<KillProcessMenuItem>(selected.Count);
        foreach (var process in selected)
            items.Add(new KillProcessMenuItem(process, FormatItemLabel(process, translate)));
        return items;
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
