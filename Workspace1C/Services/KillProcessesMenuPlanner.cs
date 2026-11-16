using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

/// <summary>Вид элемента плана подменю «Завершить процессы» (issue #134).</summary>
public enum KillProcessesMenuEntryKind
{
    /// <summary>Верхний пункт «Завершить все» (несколько баз с процессами).</summary>
    KillAll = 0,

    /// <summary>Пункт конкретного процесса.</summary>
    Process,

    /// <summary>Группа базы с вложенными пунктами процессов.</summary>
    BaseGroup
}

/// <summary>
/// Неизменяемый элемент плана подменю «Завершить процессы» (issue #134): пункт
/// процесса, группа базы с вложенными пунктами либо верхний пункт «Завершить все».
/// Подпись уже локализована чистым слоем — окна её не собирают.
/// </summary>
/// <param name="Kind">Вид элемента.</param>
/// <param name="Label">Готовая подпись пункта или заголовка группы.</param>
/// <param name="Process">Процесс для пункта <see cref="KillProcessesMenuEntryKind.Process"/>.</param>
/// <param name="Children">Вложенные пункты группы (только для <see cref="KillProcessesMenuEntryKind.BaseGroup"/>).</param>
public sealed record KillProcessesMenuEntry(
    KillProcessesMenuEntryKind Kind,
    string Label,
    RunningOneCProcessDetails? Process = null,
    IReadOnlyList<KillProcessesMenuEntry>? Children = null);

/// <summary>
/// Неизменяемый план подменю «Завершить процессы» (issue #134, доработки автора
/// ArtyoZ от 16.11.2026):
/// <list type="bullet">
/// <item>одна база с процессами — плоский список, как в v1.0.1.74;</item>
/// <item>несколько баз с процессами — верхний пункт «Завершить все» и группы по
/// базам в порядке выделения, внутри группы процессы отсортированы по времени
/// старта; базы без запущенных процессов не показываются.</item>
/// </list>
/// </summary>
/// <param name="Entries">Элементы верхнего уровня в порядке показа.</param>
/// <param name="AllProcesses">Все процессы плана (для «Завершить все»), без дублей PID.</param>
/// <param name="IsGrouped">True — план сгруппирован по базам (баз с процессами больше одной).</param>
public sealed record KillProcessesMenuPlan(
    IReadOnlyList<KillProcessesMenuEntry> Entries,
    IReadOnlyList<RunningOneCProcessDetails> AllProcesses,
    bool IsGrouped)
{
    /// <summary>Пустой план — подменю скрывается (процессов нет).</summary>
    public static KillProcessesMenuPlan Empty { get; } = new(
        Array.Empty<KillProcessesMenuEntry>(),
        Array.Empty<RunningOneCProcessDetails>(),
        IsGrouped: false);

    /// <summary>True — показывать в подменю нечего.</summary>
    public bool IsEmpty => Entries.Count == 0;
}

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

    /// <summary>Ключ заголовка верхнего пункта «Завершить все» (несколько баз, issue #134).</summary>
    public const string KillAllTitleKey = "KillProcesses.KillAllTitle";

    /// <summary>Ключ подтверждения «Завершить все» (подставляется число процессов).</summary>
    public const string KillAllConfirmFormatKey = "KillProcesses.KillAllConfirmFormat";

    /// <summary>Ключ сводки «Завершить все»: сколько завершено и сколько не удалось.</summary>
    public const string KillAllSummaryFormatKey = "KillProcesses.KillAllSummaryFormat";

    /// <summary>Ключ формата заголовка группы базы: «{0} ({1})» — имя базы и число процессов.</summary>
    public const string GroupHeaderFormatKey = "KillProcesses.GroupHeaderFormat";

    /// <summary>Ключ подписи базы без имени (в заголовке группы).</summary>
    public const string UnknownBaseKey = "KillProcesses.UnknownBase";

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
    /// Пользователь — владелец процесса, иначе имя из параметра <c>/N</c>
    /// командной строки (issue #134); оба источника пусты → ключ
    /// <see cref="UnknownUserKey"/>. Время неизвестно → прочерк
    /// (<see cref="UnknownStartTimeKey"/>). Формат и все тексты — из словаря
    /// локализации (без хардкода русского текста).
    /// </summary>
    public static string FormatItemLabel(
        RunningOneCProcessDetails process, Func<string, string> translate)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));
        if (translate is null)
            throw new ArgumentNullException(nameof(translate));

        var user = ResolveUserName(process) ?? translate(UnknownUserKey);

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
    /// Сопоставление процессов с базами за один проход
    /// <see cref="RunningInfobaseMatcher.MatchProcesses"/> (issue #134): возвращает
    /// словарь «Id базы → подробности процессов». Используется планом меню, чтобы
    /// группировка по базам совпадала с индикаторами запуска.
    /// </summary>
    private static Dictionary<string, List<RunningOneCProcessDetails>> MatchByBase(
        IReadOnlyList<Infobase> bases, IReadOnlyList<RunningOneCProcessDetails> processes)
    {
        var result = new Dictionary<string, List<RunningOneCProcessDetails>>(StringComparer.Ordinal);
        if (bases.Count == 0 || processes.Count == 0)
            return result;

        // Матчер работает с облегчённым RunningOneCProcess; подробности находим
        // по ссылке на прокси (записи с одинаковой командной строкой не склеиваются).
        var proxies = new List<RunningOneCProcess>(processes.Count);
        var detailsByProxy =
            new Dictionary<RunningOneCProcess, RunningOneCProcessDetails>(ReferenceEqualityComparer.Instance);
        foreach (var process in processes)
        {
            if (process is null)
                continue;
            var proxy = new RunningOneCProcess(
                process.ProcessName ?? string.Empty,
                process.CommandLine ?? string.Empty);
            proxies.Add(proxy);
            detailsByProxy[proxy] = process;
        }

        var matched = RunningInfobaseMatcher.MatchProcesses(bases, proxies);
        foreach (var pair in matched)
        {
            var details = new List<RunningOneCProcessDetails>(pair.Value.Count);
            foreach (var proxy in pair.Value)
            {
                if (detailsByProxy.TryGetValue(proxy, out var detail))
                    details.Add(detail);
            }

            if (details.Count > 0)
                result[pair.Key] = details;
        }

        return result;
    }

    /// <summary>
    /// Полный план подменю (issue #134): при одной базе с процессами — плоский
    /// список как раньше; при нескольких — верхний пункт «Завершить все» и группы
    /// по базам в порядке выделения. Внутри группы процессы отсортированы по
    /// времени старта; базы без запущенных процессов не показываются. Пустой
    /// план — подменю скрывается.
    /// </summary>
    public static KillProcessesMenuPlan BuildMenuPlan(
        IEnumerable<Infobase>? infobases,
        IReadOnlyList<RunningOneCProcessDetails>? processes,
        Func<string, string> translate)
    {
        if (translate is null)
            throw new ArgumentNullException(nameof(translate));

        if (processes is null || processes.Count == 0)
            return KillProcessesMenuPlan.Empty;

        // Порядок баз в выделении сохраняется; дубли Id отбрасываются.
        var targets = new List<Infobase>();
        var seenBaseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var infobase in infobases ?? Enumerable.Empty<Infobase>())
        {
            if (infobase?.Connection is null)
                continue;
            if (!string.IsNullOrWhiteSpace(infobase.Id) && !seenBaseIds.Add(infobase.Id))
                continue;
            targets.Add(infobase);
        }

        if (targets.Count == 0)
            return KillProcessesMenuPlan.Empty;

        // Процессы каждой базы — одним проходом матчера по всем выделенным базам
        // (сохраняется его семантика: прямые ключи, /IBConnectionString и /IBName
        // только при уникальном имени). PID, уже отнесённый к более ранней базе,
        // повторно не показывается.
        var matchedByBase = MatchByBase(targets, processes);
        var groups = new List<(Infobase Infobase, IReadOnlyList<RunningOneCProcessDetails> Processes)>();
        var seenPids = new HashSet<int>();
        foreach (var infobase in targets)
        {
            if (string.IsNullOrWhiteSpace(infobase.Id)
                || !matchedByBase.TryGetValue(infobase.Id, out var matched))
                continue;

            var unique = matched.Where(process => !seenPids.Contains(process.Pid)).ToList();
            if (unique.Count == 0)
                continue;

            foreach (var process in unique)
                seenPids.Add(process.Pid);
            groups.Add((infobase, SortByStartTime(unique)));
        }

        if (groups.Count == 0)
            return KillProcessesMenuPlan.Empty;

        var allProcesses = groups.SelectMany(group => group.Processes).ToList();

        // Одна база с процессами — прежний плоский список (v1.0.1.74).
        if (groups.Count == 1)
        {
            var flat = groups[0].Processes
                .Select(process => new KillProcessesMenuEntry(
                    KillProcessesMenuEntryKind.Process, FormatItemLabel(process, translate), process))
                .ToList();
            return new KillProcessesMenuPlan(flat, allProcesses, IsGrouped: false);
        }

        // Несколько баз — «Завершить все» первым пунктом, затем группы по базам.
        var entries = new List<KillProcessesMenuEntry>(groups.Count + 1)
        {
            new(KillProcessesMenuEntryKind.KillAll, translate(KillAllTitleKey))
        };

        foreach (var (infobase, groupProcesses) in groups)
        {
            var children = groupProcesses
                .Select(process => new KillProcessesMenuEntry(
                    KillProcessesMenuEntryKind.Process, FormatItemLabel(process, translate), process))
                .ToList();
            var header = string.Format(
                translate(GroupHeaderFormatKey),
                BaseDisplayName(infobase, translate),
                children.Count);
            entries.Add(new KillProcessesMenuEntry(
                KillProcessesMenuEntryKind.BaseGroup, header, Children: children));
        }

        return new KillProcessesMenuPlan(entries, allProcesses, IsGrouped: true);
    }

    /// <summary>
    /// Плоский список пунктов (обратная совместимость v1.0.1.74, issue #134):
    /// строится поверх <see cref="BuildMenuPlan"/> — пункты процессов в порядке
    /// плана (порядок баз, внутри базы — по времени старта).
    /// </summary>
    public static IReadOnlyList<KillProcessMenuItem> BuildMenuItems(
        IEnumerable<Infobase>? infobases,
        IReadOnlyList<RunningOneCProcessDetails>? processes,
        Func<string, string> translate)
    {
        var plan = BuildMenuPlan(infobases, processes, translate);
        var items = new List<KillProcessMenuItem>(plan.AllProcesses.Count);
        foreach (var entry in plan.Entries)
        {
            if (entry.Kind == KillProcessesMenuEntryKind.Process && entry.Process is not null)
            {
                items.Add(new KillProcessMenuItem(entry.Process, entry.Label));
            }
            else if (entry.Kind == KillProcessesMenuEntryKind.BaseGroup && entry.Children is not null)
            {
                foreach (var child in entry.Children)
                {
                    if (child.Kind == KillProcessesMenuEntryKind.Process && child.Process is not null)
                        items.Add(new KillProcessMenuItem(child.Process, child.Label));
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Имя пользователя для подписи пункта (issue #134): сперва владелец процесса
    /// (<see cref="RunningOneCProcessDetails.UserName"/>), затем имя из параметра
    /// <c>/N</c> командной строки — автор issue отметил, что оно там может быть
    /// указано в виде <c>/N"superadmin"</c>. null — оба источника пусты.
    /// </summary>
    public static string? ResolveUserName(RunningOneCProcessDetails process)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));

        var owner = process.UserName?.Trim();
        if (!string.IsNullOrWhiteSpace(owner))
            return owner;

        return TryExtractUserNameFromCommandLine(process.CommandLine, out var fromCommandLine)
            ? fromCommandLine
            : null;
    }

    /// <summary>
    /// Извлекает имя пользователя из параметра командной строки 1С <c>/N</c>
    /// (issue #134): поддерживаются формы <c>/N"superadmin"</c>,
    /// <c>/N superadmin</c>, <c>/Nsuperadmin</c> и <c>-N</c>; регистр ключа не
    /// важен, кавычки и пробелы обрабатываются (в том числе удвоенные кавычки).
    /// Ключ должен стоять отдельным токеном: «/N» внутри пути или другого ключа
    /// (например «/Out/Nfoo») не считается. Пустое значение или отсутствие ключа —
    /// false.
    /// </summary>
    public static bool TryExtractUserNameFromCommandLine(string? commandLine, out string? userName)
    {
        userName = null;
        var line = commandLine ?? string.Empty;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '/' && line[i] != '-')
                continue;

            // Начало токена: начало строки, пробел/табуляция или кавычка перед ключом.
            if (i > 0 && !char.IsWhiteSpace(line[i - 1]) && line[i - 1] != '"')
                continue;

            if (i + 1 >= line.Length || (line[i + 1] != 'N' && line[i + 1] != 'n'))
                continue;

            var value = ReadCommandLineValue(line, i + 2);
            if (!string.IsNullOrWhiteSpace(value))
            {
                userName = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Значение параметра командной строки после ключа: остаток до разделителя
    /// (пробел/табуляция) либо содержимое кавычек. Пустая строка — значения нет
    /// (сразу следующий ключ или конец строки).
    /// </summary>
    private static string ReadCommandLineValue(string line, int start)
    {
        var index = start;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length)
            return string.Empty;

        // Сразу следующий ключ — значение пустое.
        if (line[index] == '/' || line[index] == '-')
            return string.Empty;

        if (line[index] == '"')
        {
            index++;
            var value = new StringBuilder();
            while (index < line.Length)
            {
                if (line[index] == '"')
                {
                    // Удвоенная кавычка внутри значения — литеральная кавычка.
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                value.Append(line[index]);
                index++;
            }

            return value.ToString().Trim();
        }

        var end = index;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
            end++;

        return line[index..end].Trim().Trim('"').Trim();
    }

    /// <summary>
    /// Отображаемое имя базы для заголовка группы; пустое имя — локализованная
    /// подпись <see cref="UnknownBaseKey"/>.
    /// </summary>
    private static string BaseDisplayName(Infobase? infobase, Func<string, string> translate)
    {
        var name = (infobase?.Name ?? string.Empty).Trim();
        return name.Length > 0 ? name : translate(UnknownBaseKey);
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
