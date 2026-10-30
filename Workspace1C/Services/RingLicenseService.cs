using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат попытки получить список установленных программных лицензий
/// (п.5 «Мониторинг лицензий», выпуск 1.0.0.87): при успехе заполнен
/// <see cref="Licenses"/>; если утилиты (ring/licensetool) недоступны, но запасной
/// источник (каталог файлов лицензий) дал хоть что-то — лицензии из каталога с
/// <see cref="Warning"/> о запасном источнике.
/// </summary>
public sealed class RingLicenseResult
{
    public IReadOnlyList<ProgramLicense> Licenses { get; init; } = Array.Empty<ProgramLicense>();

    /// <summary>Предупреждение (утилита не найдена, использован запасной источник и т.п.); null/пусто — штатно.</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Сервис получения установленных программных лицензий 1С (п.5, выпуск 1.0.0.87):
/// <para>
/// - Windows: утилита <c>ring license list</c> (ищется в PATH и в каталоге bin платформы
///   рядом с rac). Запасной источник — каталог файлов лицензий
///   <c>%ProgramData%\1C\1Cv8\licenses</c>.
/// </para>
/// <para>
/// - Linux: сначала <c>ring license list</c> (если ring установлен в PATH); иначе утилита
///   лицензий платформы <c>/opt/1cv8/<версия>/x86_64/utils/licensetool</c> (или
///   <c>.../utils/licensetool</c> без архитектуры); запасной источник — каталог
///   <c>/etc/1cv8/License</c>.
/// </para>
/// Чистый разбор вывода — в <see cref="RingLicenseParser"/> (тестируется отдельно).
/// </summary>
public interface IRingLicenseService
{
    /// <summary>Список установленных программных лицензий; не бросает исключений — при сбое возвращает пустой список с пояснением в <see cref="RingLicenseResult.Warning"/>.</summary>
    Task<RingLicenseResult> GetInstalledLicensesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ЧИСТЫЙ разбор вывода утилит «ring license list» / «licensetool» (п.5, 1.0.0.87):
/// без I/O и UI — только строки. Вывод зависит от платформы и локали окружения, поэтому
/// парсер допускает два формата:
/// <list type="bullet">
/// <item>таблицу с заголовком (разделители «|» или табуляция/два пробела) — колонки типа,
/// серийного номера, версии, модуля, пользователя, состояния и срока распознаются по
/// именам заголовков (ru/en, по ключевым словам);</item>
/// <item>произвольный текст — каждая строка, содержащая серийный номер вида
/// <c>XXXX-XXXX-...-XXXXXXXXXXXX</c>, становится отдельной лицензией с сырым текстом
/// (остальные поля — по ключевым словам в строке).</item>
/// </list>
/// </summary>
public static class RingLicenseParser
{
    /// <summary>Серийный номер программной лицензии 1С: 8-4-4-4-12 (hex), без учёта регистра.</summary>
    public static readonly Regex SerialNumberRegex = new(
        @"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b",
        RegexOptions.Compiled);

    /// <summary>Число рабочих мест/лицензий, встречающееся рядом со словом «лиценз»/«licens» (/л всего и т.п.).</summary>
    private static readonly Regex LicenseCountRegex = new(
        @"(?i)\b(?:на\s+)?(\d{1,4})\s*(?:рабочих\s+мест|пользовател\w+|licenses?|seats?)\b",
        RegexOptions.Compiled);

    private static readonly Regex DateRegex = new(
        @"\b(0[1-9]|[12]\d|3[01])\.(0[1-9]|1[0-2])\.(\d{4})\b",
        RegexOptions.Compiled);

    private static readonly Regex IsoDateRegex = new(
        @"\b(19|20)\d{2}[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    /// <summary>Разбирает вывод утилиты в список лицензий. Пустой/неизвестный вывод → пустой список.</summary>
    public static IReadOnlyList<ProgramLicense> Parse(string output)
    {
        var licenses = new List<ProgramLicense>();
        if (string.IsNullOrWhiteSpace(output))
            return licenses;

        ParseTable(output, licenses);

        // Запасной режим: строки с серийными номерами (если табличный разбор ничего не дал).
        if (licenses.Count == 0)
            ParseFreeForm(output, licenses);

        return licenses;
    }

    /// <summary>Пытается разобрать таблицу с заголовком. Возвращает кратки статус (распознан ли заголовок лицензий).</summary>
    private static void ParseTable(string output, List<ProgramLicense> licenses)
    {
        var lines = SplitNonEmpty(output);
        if (lines.Count < 2)
            return;

        // Заголовок таблицы — строка, содержащая как минимум два «именованных» столбца
        // (серийный номер И что-то ещё: тип/версия/модуль/срок/пользователь).
        var headerIndex = lines.FindIndex(IsLikelyLicenseHeader);
        if (headerIndex < 0 || headerIndex + 1 >= lines.Count)
            return;

        var headerCells = SplitCells(lines[headerIndex]);
        var serialCol = IndexOfColumn(headerCells, "serial", "серий", "номер", "номером", "сер.номер");
        var typeCol = IndexOfColumn(headerCells, "type", "тип", "вид");
        var versionCol = IndexOfColumn(headerCells, "version", "версия", "версии");
        var moduleCol = IndexOfColumn(headerCells, "module", "модуль", "модуля");
        var userCol = IndexOfColumn(headerCells, "user", "пользовател", "владел", "кому", "выдан");
        var stateCol = IndexOfColumn(headerCells, "state", "статус", "состоян", "доступн");
        var expiryCol = IndexOfColumn(headerCells, "expires", "действ", "срок", "до");
        var countCol = IndexOfColumn(headerCells, "count", "кол-во", "количеств", "мест", "лиценз");

        if (serialCol < 0 && typeCol < 0 && versionCol < 0)
            return; // таблица не про лицензии

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var cells = SplitCells(lines[i]);
            if (cells.Count < 2)
                continue;

            var serial = serialCol >= 0 ? cells[serialCol].Trim() : string.Empty;
            // Серийный номер может содержать пробелы после каждой группы — схлопываем.
            serial = serial.Replace(" ", string.Empty);
            if (serial.Length == 0 && !SerialNumberRegex.IsMatch(lines[i]))
                continue; // строка данных без серийника — не лицензия

            licenses.Add(new ProgramLicense
            {
                SerialNumber = serial,
                Type = typeCol >= 0 ? Normalize(cells[typeCol]) : string.Empty,
                Version = versionCol >= 0 ? Normalize(cells[versionCol]) : string.Empty,
                Module = moduleCol >= 0 ? Normalize(cells[moduleCol]) : string.Empty,
                User = userCol >= 0 ? Normalize(cells[userCol]) : string.Empty,
                State = stateCol >= 0 ? Normalize(cells[stateCol]) : string.Empty,
                ExpiryDate = expiryCol >= 0 ? ParseDate(cells[expiryCol]) : null,
                LicenseCount = countCol >= 0 ? ParseInt(cells[countCol]) : 0,
                RawText = lines[i].Trim()
            });
        }
    }

    /// <summary>Свободный разбор: строки с серийным номером (без заголовка).</summary>
    private static void ParseFreeForm(string output, List<ProgramLicense> licenses)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            var serialMatch = SerialNumberRegex.Match(line);
            if (!serialMatch.Success)
            {
                // Упрощённый формат серийного номера (licensetool): 16 hex-символов или
                // «XXXX XXXX XXXX XXXX XXXX XXXX» с пробелами.
                var compact = Regex.Match(line, @"\b[0-9A-Fa-f]{4}(?:[ -][0-9A-Fa-f]{4}){4,}\b");
                if (!compact.Success)
                    continue;
                serialMatch = compact;
            }

            licenses.Add(new ProgramLicense
            {
                SerialNumber = serialMatch.Value.Replace(" ", string.Empty),
                Type = KeywordValue(line, "тип", "type"),
                Version = KeywordValue(line, "версия", "version"),
                Module = KeywordValue(line, "модуль", "module"),
                User = KeywordValue(line, "пользовател", "user", "владел"),
                State = KeywordValue(line, "состоян", "статус", "state"),
                ExpiryDate = ParseDate(line),
                LicenseCount = ParseLicenseCount(line),
                RawText = line
            });
        }
    }

    /// <summary>Разбивает строку таблицы на ячейки (разделители: «|», табуляция, 2+ пробела).</summary>
    private static IReadOnlyList<string> SplitCells(string line)
    {
        var cells = (string?)null;
        if (line.IndexOf('|') >= 0)
            cells = line;
        else if (line.IndexOf('\t') >= 0)
            cells = line;
        if (cells is not null)
            return cells.Split(new[] { '|', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();

        // Fallback: 2+ пробела.
        return Regex.Split(line, @" {2,}").Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
    }

    private static bool IsLikelyLicenseHeader(string line)
    {
        var lowered = line.ToLowerInvariant();
        var hasSerial = lowered.Contains("serial") || lowered.Contains("серий");
        var hasOther = lowered.Contains("type") || lowered.Contains("тип") ||
                       lowered.Contains("version") || lowered.Contains("версия") ||
                       lowered.Contains("module") || lowered.Contains("модуль") ||
                       lowered.Contains("expires") || lowered.Contains("срок") ||
                       lowered.Contains("действ") || lowered.Contains("держ");
        return hasSerial && hasOther;
    }

    private static int IndexOfColumn(IReadOnlyList<string> headerCells, params string[] keywords)
    {
        for (var i = 0; i < headerCells.Count; i++)
        {
            var cell = headerCells[i].ToLowerInvariant();
            foreach (var keyword in keywords)
            {
                if (cell.Contains(keyword))
                    return i;
            }
        }
        return -1;
    }

    private static string Normalize(string value) => value.Trim().Trim('|', ' ');

    /// <summary>
    /// Значение после ключевого слова в свободной строке («Тип: клиентская»; если в той же
    /// строке дальше идёт другое ключевое слово — значение заканчивается перед ним).
    /// </summary>
    private static string KeywordValue(string line, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            var pattern = $@"(?i)\b{Regex.Escape(keyword)}\s*[:=]\s*([^|;\r\n]+?)(?=\s+(?:Version|Тип|Версия|Модуль|Модуля|Пользовател|Состоян|Статус|До|Expires|State|Module|User)\s*[:=]|$)";
            var match = Regex.Match(line, pattern);
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }
        return string.Empty;
    }

    private static int ParseLicenseCount(string line)
    {
        var match = LicenseCountRegex.Match(line);
        return match.Success ? ParseInt(match.Groups[1].Value) : 0;
    }

    private static DateTime? ParseDate(string value)
    {
        var match = DateRegex.Match(value);
        if (match.Success &&
            DateTime.TryParseExact(match.Value, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var ruDate))
            return ruDate;

        match = IsoDateRegex.Match(value);
        if (match.Success &&
            DateTime.TryParse(match.Value.Replace('/', '-'), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var isoDate))
            return isoDate;

        return null;
    }

    private static int ParseInt(string value)
    {
        var digits = Regex.Replace(value, @"[^\d]", string.Empty);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static List<string> SplitNonEmpty(string output)
    {
        var lines = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
    }
}

/// <summary>
/// Реализация <see cref="IRingLicenseService"/>: запуск «ring license list» /
/// «licensetool» через <see cref="ProcessRunner"/>, запасной источник — каталог
/// файлов лицензий (Linux — <c>/etc/1cv8/License</c>, Windows — каталог
/// <c>%ProgramData%\1C\1Cv8\licenses</c>). Ошибки не бросаются наружу — возвращается
/// <see cref="RingLicenseResult"/> с пустым списком и <see cref="RingLicenseResult.Warning"/>.
/// </summary>
public sealed class RingLicenseService : IRingLicenseService
{
    private readonly IAppLogger? _logger;

    public RingLicenseService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Tаймаут выполнения утилиты лицензий.</summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<RingLicenseResult> GetInstalledLicensesAsync(CancellationToken cancellationToken = default)
    {
        // issue #83 (последний комментарий 7OH от 2026-10-29): утилиту ring НЕ ищем
        // ручным перебором PATH/PATHEXT с подменой полного пути — доступность проверяем
        // запуском «where.exe ring»/«which ring» (запасной способ — «ring --version»),
        // а вызываем «ring» как есть, поиск по PATH выполняет сама ОС.
        var ringInPath = await FindRingInPathAsync(cancellationToken).ConfigureAwait(false);
        if (ringInPath is null)
        {
            // issue #83: недоступность утилиты — прогнозируемая внешняя причина, пишем
            // INFO (не WARN-спам при автообновлении каждые ~5 с).
            // issue #80: запись направления RAC — через категорийный гейт CM_RAC + CM_ON.
            Log.Info(LogCategory.Rac,
                "RING: утилита ring не найдена в PATH (where.exe ring / which ring) — переходим к запасным вариантам.");
        }

        // 1) ring license list (Windows и Linux — если ring доступен в PATH).
        foreach (var (command, arguments, source) in CommandCandidates(includePathRing: ringInPath is not null))
        {
            var result = await ProcessRunner.RunAsync(
                    command, arguments, timeout: CommandTimeout, ct: cancellationToken)
                .ConfigureAwait(false);
            if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
            {
                var licenses = RingLicenseParser.Parse(result.StdOut);
                if (licenses.Count > 0)
                {
                    // issue #80: INFO-записи RING — через категорийный гейт CM_RAC + CM_ON
                    // (не «капают» в журнал при выключенном логировании).
                    Log.Info(LogCategory.Rac,
                        $"RING: распознано программных лицензий: {licenses.Count} (источник: {source}).");
                    return new RingLicenseResult { Licenses = licenses };
                }
            }
            else if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
            {
                // issue #83 (комментарий 7OH от 2026-10-28): недоступность утилиты лицензий
                // (ring не найден в PATH/рядом с rac — «An error occurred trying to start
                // process 'ring'… Не удается найти указанный файл») — ПРОГНОЗИРУЕМАЯ внешняя
                // причина, а не ошибка приложения: при автообновлении каждые ~5 с в общий
                // журнал писать WARN-спам больше не нужно — уровень INFO/DEBUG; понятное
                // сообщение для вкладки «Лицензии» формирует следующий блок.
                // issue #80: запись направления RAC — через категорийный гейт CM_RAC + CM_ON.
                Log.Info(LogCategory.Rac, $"RING: {source} — утилита лицензий недоступна: {result.CombinedOutput}");
            }
        }

        // 2) Запасной источник — каталог файлов лицензий.
        var fallback = TryReadLicenseDirectory();
        if (fallback.Count > 0)
        {
            const string warning =
                "Не удалось получить список лицензий через утилиту ring (не найдена); показаны " +
                "записи из каталога файлов лицензий платформы. Установите ring (обычно лежит " +
                "в каталоге bin платформы рядом с rac) и нажмите «Обновить».";
            // issue #80: запись направления RAC — через категорийный гейт CM_RAC + CM_ON.
            Log.Info(LogCategory.Rac, $"RING: {warning} Найдено файлов: {fallback.Count}.");
            return new RingLicenseResult { Licenses = fallback, Warning = warning };
        }

        return new RingLicenseResult { Warning = BuildUnavailableMessage() };
    }

    /// <summary>
    /// Понятное сообщение вкладки «Лицензии», когда утилита ring/licensetool недоступна
    /// (issue #83): в тексте — где проверялась утилита и что сделать, чтобы её найти,
    /// вместо пустой вкладки. Internal — для юнит-тестов читаемого сообщения.
    /// </summary>
    internal static string BuildUnavailableMessage()
    {
#if WINDOWS
        const string searched =
            "проверка: where.exe ring / ring --version, ring.exe в каталоге bin платформы рядом с rac";
#else
        const string searched =
            "проверка: which ring / ring --version, licensetool в каталогах /opt/1cv8";
#endif
        return "Не удалось получить список лицензий: не найдена утилита ring (" + searched +
               "; каталог файлов лицензий платформы пуст). Установите ring (обычно лежит в " +
               "каталоге bin платформы рядом с rac) либо добавьте его в PATH и нажмите «Обновить».";
    }

    /// <summary>
    /// Кандидаты на выполнение (имя, аргументы, описание источника):
    /// ring из PATH (приоритет — issue #83, комментарий 7OH: «просто вызывать ring как
    /// есть», без подмены полного пути), затем ring.exe в каталоге bin платформы и
    /// licensetool платформы Linux как запасные. Кандидат «ring (PATH)» добавляется
    /// только если проверка доступности (<see cref="FindRingInPathAsync"/>) подтвердила,
    /// что утилита есть в PATH. Internal — для юнит-тестов без запуска процессов.
    /// </summary>
    internal static IReadOnlyList<(string Command, string[] Arguments, string Source)> CommandCandidates(
        bool includePathRing = true)
    {
        var candidates = new List<(string, string[], string)>();

        // issue #83 (последний комментарий 7OH): вызываем «ring» КАК ЕСТЬ — без ручного
        // резолва полного пути по PATH/PATHEXT (поиск по PATH выполняет сама ОС при
        // Process.Start); доступность подтверждена заранее через where/which или
        // «ring --version».
        if (includePathRing)
            candidates.Add(("ring", new[] { "license", "list" }, "ring (PATH)"));

#if WINDOWS
        // Запасной источник — каталог bin платформы (рядом с rac): только когда ring
        // в PATH не найден; больше НЕ ставится первым «вместо» PATH (issue #83).
        var rac = OneCPlatformLocator.FindRacExecutable();
        if (!string.IsNullOrWhiteSpace(rac))
        {
            var dir = Path.GetDirectoryName(rac);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var ringExe = OneCPlatformLocator.FindInBinDir(dir, "ring");
                if (!string.IsNullOrWhiteSpace(ringExe))
                    candidates.Add((ringExe, new[] { "license", "list" }, "ring (bin платформы)"));
            }
        }
#else
        // Linux: licensetool платформы /opt/1cv8/<версия>/[x86_64/]utils/licensetool.
        foreach (var tool in FindLicenseToolPaths())
            candidates.Add((tool, Array.Empty<string>(), $"licensetool ({tool})"));
#endif

        return candidates;
    }

    /// <summary>
    /// Проверяет доступность утилиты ring в PATH (issue #83, последний комментарий 7OH
    /// от 2026-10-29 19:58:55): НЕ ручной перебор PATH/PATHEXT с подменой полного пути,
    /// а запуск самой утилиты поиска — «where.exe ring» (Windows) / «which ring» (Linux);
    /// запасной способ — прямой запуск «ring --version». Возвращает первый найденный
    /// путь (маркер доступности; сам вызов ring выполняется «как есть») или null, если
    /// утилита недоступна. Internal — для юнит-тестов.
    /// </summary>
    internal static async Task<string?> FindRingInPathAsync(CancellationToken ct = default)
    {
#if WINDOWS
        var probe = await ProcessRunner.RunAsync("where.exe", new[] { "ring" },
                timeout: CommandTimeout, ct: ct)
            .ConfigureAwait(false);
        if (probe.Success)
        {
            var line = FirstNonEmptyLine(probe.StdOut);
            if (line is not null)
                return line;
        }
#else
        var probe = await ProcessRunner.RunAsync("which", new[] { "ring" },
                timeout: CommandTimeout, ct: ct)
            .ConfigureAwait(false);
        if (probe.Success)
        {
            var line = FirstNonEmptyLine(probe.StdOut);
            if (line is not null)
                return line;
        }
#endif

        // Запасная проверка (если where/which нет в системе или не дала результата) —
        // прямой запуск «ring --version»: работает — утилита доступна (issue #83,
        // комментарий 7OH: «Запустили "ring --version" и посмотрели - ошибка или работает»).
        var version = await ProcessRunner.RunAsync("ring", new[] { "--version" },
                timeout: CommandTimeout, ct: ct)
            .ConfigureAwait(false);
        return version.Success ? "ring" : null;
    }

    /// <summary>Первая непустая строка вывода (путь, найденный where/which). Internal — тесты.</summary>
    internal static string? FirstNonEmptyLine(string output)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length > 0)
                return line;
        }
        return null;
    }

    /// <summary>Ищет исполняемые файлы licensetool в каталогах /opt/1cv8 (Linux). Internal — тесты.</summary>
    internal static IReadOnlyList<string> FindLicenseToolPaths()
    {
        const string root = "/opt/1cv8";
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var versionDir in Directory.EnumerateDirectories(root).OrderByDescending(d => d))
        {
            // Глубина 2: <версия>/x86_64/utils/licensetool и <версия>/utils/licensetool.
            var candidates = new[]
            {
                Path.Combine(versionDir, "x86_64", "utils", "licensetool"),
                Path.Combine(versionDir, "x86_64", "utils", "license_tool"),
                Path.Combine(versionDir, "utils", "licensetool")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    results.Add(candidate);
            }
            if (results.Count > 0)
                break; // берём первую (самую свежую) установленную версию платформы.
        }
        return results;
    }

    /// <summary>Читает каталог файлов лицензий платформы (Linux и Windows). Internal — тесты.</summary>
    internal static IReadOnlyList<ProgramLicense> TryReadLicenseDirectory()
    {
        var directories = new List<string>();
#if WINDOWS
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            directories.Add(Path.Combine(programData, "1C", "1Cv8", "licenses"));
            directories.Add(Path.Combine(programData, "1C", "1cv8", "licenses"));
        }
#else
        directories.Add("/etc/1cv8/License");
        directories.Add("/var/1C/licenses");
#endif

        var licenses = new List<ProgramLicense>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory)
                         .Where(f => Path.GetExtension(f).Length > 0)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                licenses.Add(new ProgramLicense
                {
                    RawText = Path.GetFileName(file),
                    Module = $"файл лицензии: {Path.GetFileName(file)}"
                });
            }
            break; // берём первый существующий каталог.
        }
        return licenses;
    }
}
