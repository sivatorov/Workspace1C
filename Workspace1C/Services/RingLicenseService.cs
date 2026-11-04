using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
#if WINDOWS
using Microsoft.Win32;
#endif

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
/// (остальные поля — по ключевым словам в строке);</item>
/// <item>локале-независимый формат ring (issue #83, v1.0.1.23): строка с номером
/// лицензии <c>NNNNNNNNNNNNN-NNNNNNNNNNNNN</c> (дефис, 10–16 цифр) и именем файла
/// <c>*.lic</c> в кавычках — распознаётся по СТРУКТУРЕ, без локализованных
/// маркеров («Имя файла» и т.п.), поэтому заполняет таблицу даже если локаль
/// вывода ring изменится или заголовок пришёл mojibake.</item>
/// </list>
/// </summary>
public static class RingLicenseParser
{
    /// <summary>Серийный номер программной лицензии 1С: 8-4-4-4-12 (hex), без учёта регистра.</summary>
    public static readonly Regex SerialNumberRegex = new(
        @"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Номер программной лицензии 1С в формате ring (issue #83, v1.0.1.23):
    /// «499459654270590-500000000037» — две группы по 10–16 цифр через дефис.
    /// Локале-независимый признак: не зависит от языка вывода утилиты.
    /// </summary>
    public static readonly Regex LicenseNumberRegex = new(
        @"\b\d{10,16}-\d{10,16}\b",
        RegexOptions.Compiled);

    /// <summary>Имя файла лицензии *.lic — в кавычках или без (issue #83, v1.0.1.23).</summary>
    private static readonly Regex LicenseFileRegex = new(
        @"(?i)""[^""]*\.lic""|\S*\.lic\b",
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

    /// <summary>Пары «ключ: значение» / «ключ=значение» из текста файла *.lic (issue #83, v1.0.1.26).</summary>
    private static readonly Regex LicKeyValueRegex = new(
        @"^\s*(?<key>[^:=\[\r\n]{2,40}?)\s*[:=]\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Пара «ключ: значение» ВНУТРИ строки (issue #83, v1.0.1.29): реальный файл *.lic
    /// может начинаться с длинного ключа, после которого в ОДНОЙ строке идут поля
    /// («ключ… Количество пользователей: 5»), либо содержать ключ после «(»/«;».
    /// Применяется как запасной проход, если якорный <see cref="LicKeyValueRegex"/>
    /// маркер не нашёл.
    /// </summary>
    private static readonly Regex LicKeyValueInlineRegex = new(
        @"(?<key>[^:=\[\]\r\n]{2,60}?)\s*[:=]\s*(?<value>[^\r\n]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Запасной проход количества пользователей (issue #83, v1.0.1.31): ключ количества
    /// БЕЗ разделителя «:»/«=» («Количество пользователей 5», «… выдана на 5 рабочих
    /// мест», «Users count 5»). Явные формулировки (без короткого «пользователей»/«мест»,
    /// чтобы не цеплять имя пользователя и прочие числа) + число в пределах строки.
    /// </summary>
    private static readonly Regex LicUserCountFallbackRegex = new(
        @"(?i)(?<key>количество\s+пользователей|кол-во\s+пользователей|число\s+пользователей|" +
        @"рабочих\s+мест|users\s+count|seats)[^\r\n:=(]{0,30}?(?<value>\d{1,4})",
        RegexOptions.Compiled);

    /// <summary>
    /// Запасной проход количества пользователей (issue #83, v1.0.1.31), вариант 2:
    /// число ПЕРЕД ключом-формулировкой — «лицензия выдана на 10 рабочих мест»,
    /// «на 5 пользователей».
    /// </summary>
    private static readonly Regex LicUserCountPrepositionRegex = new(
        @"(?i)\bна\s+(?<value>\d{1,4})\s+(?:рабочих\s+мест|пользовател(?:ей|я)|users|seats)",
        RegexOptions.Compiled);

    /// <summary>
    /// Дата в файле лицензии: dd.MM.yyyy или yyyy-MM-dd, с необязательным временем
    /// «HH:mm[:ss]» (issue #83, v1.0.1.29 — в реальном файле репортёра дата со временем;
    /// ранее время терялось, а UI показывал AM/PM локали ОС).
    /// </summary>
    private static readonly Regex LicDateRegex = new(
        @"\b(?:(?<d1>0[1-9]|[12]\d|3[01])\.(?<m1>0[1-9]|1[0-2])\.(?<y1>\d{4})|(?<y2>(19|20)\d{2})-(?<m2>0[1-9]|1[0-2])-(?<d2>0[1-9]|[12]\d|3[01]))(?:\s+(?<hh>[01]?\d|2[0-3]):(?<mi>[0-5]\d)(?::(?<ss>[0-5]\d))?)?",
        RegexOptions.Compiled);

    /// <summary>
    /// Компактная дата/метка времени в файле лицензии (issue #83, v1.0.1.28 — реальный
    /// файл репортёра «20260907085731.lic»): yyyyMMdd или yyyyMMddHHmmss, 8–14 цифр
    /// подряд, не часть более длинного числа. С v1.0.1.29 время (если есть) сохраняется.
    /// </summary>
    private static readonly Regex LicCompactDateRegex = new(
        @"(?<!\d)(?<y>(19|20)\d{2})(?<m>0[1-9]|1[0-2])(?<d>0[1-9]|[12]\d|3[01])(?:(?<hh>[01]?\d|2[0-3])(?<mi>[0-5]\d)(?<ss>[0-5]\d))?(?!\d)",
        RegexOptions.Compiled);

    /// <summary>
    /// ЧИСТЫЙ разбор содержимого файла лицензии *.lic (issue #83, v1.0.1.26): извлекает
    /// владельца, рег. номер, пользователя/компьютер и даты. Формат файла 1С не
    /// документирован и зависит от версии платформы, поэтому разбор толерантный:
    /// распознаются пары «ключ=значение»/«ключ: значение» по ключевым словам (ru/en),
    /// всё нераспознанное уходит в <see cref="ProgramLicense.RawDetails"/> — однострочную
    /// выжимку для подсказки по строке таблицы лицензий.
    /// </summary>
    public static ProgramLicense ParseLicFile(string content, string? fileName = null)
    {
        var license = new ProgramLicense();
        if (string.IsNullOrWhiteSpace(content))
            return license;

        var owner = FindValue(content, "владел", "owner", "организац", "organization", "компан", "company");
        if (owner is not null)
            license.Owner = owner;
        // «рег.номер»/«рег. номер»/«рег номер»/«регномер» — форматы ключа различаются.
        var regNumber = FindValue(content, "рег.номер", "рег. номер", "рег номер", "регномер", "регистраци", "reg. number", "regnumber", "registration");
        if (regNumber is not null)
            license.RegNumber = regNumber;
        var user = FindValue(content, "пользовател", "user");
        if (user is not null)
            license.User = user;
        var computer = FindValue(content, "компьютер", "computer", "машина", "machine");
        if (computer is not null)
            license.Computer = computer;
        var series = FindValue(content, "серия", "series");
        if (series is not null)
            license.Type = series;
        var type = FindValue(content, "тип", "вид лиценз", "type");
        if (type is not null && license.Type.Length == 0)
            license.Type = type;

        // Issue #83 (v1.0.1.27): поле «Наименование» формата «версия: владелец» —
        // разбиваем по ПЕРВОМУ «:»: левая часть → Version, правая → Owner
        // (явный владелец из ключа «владел/организац» не затирается).
        var name = FindValue(content, "наименование", "название", "name");
        if (name is not null)
        {
            var separatorIndex = name.IndexOf(':');
            if (separatorIndex > 0)
            {
                var versionPart = name.Substring(0, separatorIndex).Trim();
                var ownerPart = name.Substring(separatorIndex + 1).Trim();
                if (license.Version.Length == 0 && versionPart.Length > 0)
                    license.Version = versionPart;
                if (license.Owner.Length == 0 && ownerPart.Length > 0)
                    license.Owner = ownerPart;
            }
            else if (license.Version.Length == 0 && name.Trim().Length > 0)
            {
                license.Version = name.Trim();
            }
        }

        // Срок действия — отдельная дата (не путать с датой выдачи).
        var expiry = FindValue(content, "срок действия", "срок действия лицензии", "действует до", "истекает", "expires", "valid until");
        if (expiry is not null)
        {
            var expiryDate = ParseLicDate(expiry);
            if (expiryDate is not null)
                license.ExpiryDate = expiryDate;
        }

        // Количество пользователей/рабочих мест (issue #83, v1.0.1.29 — колонка
        // «Пользователей» была пустой: в реальном файле репортёра ключ «Количество
        // пользователей» не находился из-за формата/окружения; добавлены варианты
        // написания; сам поиск устойчив к ключу внутри строки — см. FindValue;
        // нечисловое значение → 0, без исключений).
        // Issue #83 (v1.0.1.31): у реальных файлов пользователя ключ количества может
        // быть БЕЗ разделителя «:»/«=» («Количество пользователей 5», «… на 5 рабочих
        // мест») — пара «ключ: значение» не образуется и FindValue не срабатывает.
        // Добавлен запасной проход LicUserCountFallbackRegex по явным формулировкам.
        var countRaw = FindValue(content,
            "количество пользователей", "кол-во пользователей", "число пользователей",
            "пользователей", "рабочих мест", "мест", "users count", "seats");
        var count = ParseLicUserCount(countRaw);
        if (count is null)
        {
            var fallbackMatch = LicUserCountFallbackRegex.Match(content);
            if (fallbackMatch.Success)
                count = ParseLicUserCount(fallbackMatch.Groups["value"].Value);
        }
        if (count is null)
        {
            var prepositionMatch = LicUserCountPrepositionRegex.Match(content);
            if (prepositionMatch.Success)
                count = ParseLicUserCount(prepositionMatch.Groups["value"].Value);
        }
        if (count is not null)
            license.LicenseCount = count.Value;

        // Дата выдачи: сначала по явным маркерам, чтобы «Срок действия» не подменял её;
        // при отсутствии маркеров — первая дата файла, НЕ равная сроку действия.
        var issuedRaw = FindValue(content, "выдана", "дата выдачи", "дата регистрации", "issued", "issue date");
        if (issuedRaw is not null)
            license.IssueDate = ParseLicDate(issuedRaw);
        if (license.IssueDate is null)
        {
            foreach (Match dateMatch in LicDateRegex.Matches(content))
            {
                var candidate = ParseLicDateValue(dateMatch);
                if (candidate is null)
                    continue;
                if (license.ExpiryDate is not null && candidate.Value == license.ExpiryDate.Value)
                    continue;
                license.IssueDate = candidate;
                break;
            }
        }

        // Выжимка файла для тултипа: непустые строки, схлопнутые в одну (до ~400 символов).
        var details = string.Join("  •  ", content
            .Split('\n')
            .Select(l => l.Trim().TrimEnd('\r'))
            .Where(l => l.Length > 0));
        if (details.Length > 400)
            details = details.Substring(0, 400) + "…";
        // Issue #83 (v1.0.1.31): диагностическая подстраховка — когда количество
        // пользователей не распознано, в выжимку добавляется список ВСЕХ ключей
        // «ключ: значение» файла: по следующей диагностике сразу видны фактические
        // формулировки пользователя, не покрытые парсером.
        if (license.LicenseCount == 0)
        {
            var unknownKeys = LicKeyValueRegex.Matches(content)
                .Select(m => m.Groups["key"].Value.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unknownKeys.Count > 0)
                details += "  •  Ключи файла: " + string.Join("; ", unknownKeys);
        }
        license.RawDetails = details;
        // Issue #83 (v1.0.1.28): ПОЛНОЕ содержимое файла (с переводами строк) —
        // для многострочного окна деталей; RawDetails остаётся однострочной
        // выжимкой для тултипа строки таблицы.
        license.RawFullText = content;
        if (!string.IsNullOrWhiteSpace(fileName))
            license.RawText = fileName!;
        return license;
    }

    /// <summary>
    /// Дата (dd.MM.yyyy, yyyy-MM-dd или компактная yyyyMMdd[HHmmss], issue #83,
    /// v1.0.1.28) внутри строки значения ключа; null — даты нет.
    /// </summary>
    private static DateTime? ParseLicDate(string value)
    {
        var match = LicDateRegex.Match(value);
        if (match.Success)
            return ParseLicDateValue(match);
        var compact = LicCompactDateRegex.Match(value);
        return compact.Success ? ParseLicCompactDateValue(compact) : null;
    }

    /// <summary>
    /// Разбирает значение количества пользователей/рабочих мест (issue #83, v1.0.1.31):
    /// берёт первое число в строке значения (пробелы/неразрывные пробелы/скобки вокруг —
    /// не помеха). null — числа нет или оно вне диапазона 1…9999.
    /// </summary>
    private static int? ParseLicUserCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // Первое число в значении («5 (пять)», «5 из 10», «на 5 рабочих мест») —
        // а не склейка всех цифр строки.
        var number = Regex.Match(value, @"\d{1,4}");
        if (!number.Success ||
            !int.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count is < 1 or > 9999)
            return null;
        return count;
    }

    /// <summary>Разобранный <see cref="LicDateRegex"/>-совпадение в DateTime; null — группа не распознана.
    /// Время в совпадении (если есть, issue #83 v1.0.1.29) сохраняется.</summary>
    private static DateTime? ParseLicDateValue(Match match)
    {
        if (match.Groups["y1"].Success)
        {
            return ApplyTime(new DateTime(
                int.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["m1"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["d1"].Value, CultureInfo.InvariantCulture)), match);
        }
        if (match.Groups["y2"].Success)
        {
            return ApplyTime(new DateTime(
                int.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["m2"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["d2"].Value, CultureInfo.InvariantCulture)), match);
        }
        return null;
    }

    /// <summary>Разобранное совпадение <see cref="LicCompactDateRegex"/> в DateTime; null — группа не распознана.
    /// Компактное время в совпадении (если есть, issue #83 v1.0.1.29) сохраняется.</summary>
    private static DateTime? ParseLicCompactDateValue(Match match)
    {
        if (!match.Groups["y"].Success)
            return null;
        try
        {
            return ApplyTime(new DateTime(
                int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture)), match);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Несуществующая дата в потоке цифр — не дата.
            return null;
        }
    }

    /// <summary>Время из групп hh/mi/ss совпадения (если распознано) поверх даты.</summary>
    private static DateTime ApplyTime(DateTime date, Match match)
    {
        var hh = match.Groups["hh"];
        var mi = match.Groups["mi"];
        if (!hh.Success || !mi.Success)
            return date;
        try
        {
            return new DateTime(
                date.Year, date.Month, date.Day,
                int.Parse(hh.Value, CultureInfo.InvariantCulture),
                int.Parse(mi.Value, CultureInfo.InvariantCulture),
                match.Groups["ss"].Success
                    ? int.Parse(match.Groups["ss"].Value, CultureInfo.InvariantCulture)
                    : 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return date;
        }
    }

    /// <summary>
    /// Текст окна деталей лицензии (issue #83, v1.0.1.30 по комментарию 7OH от
    /// 2026-11-03: «строки до "Файл лицензии" лишние… можно начинать с полного имени
    /// файла»): распознанные поля (тип/версия/владелец/пользователь/рег. номер/даты)
    /// НЕ включаются — они уже видны в колонках таблицы лицензий. В окне остаются:
    /// строка «Файл лицензии: <полный путь>» и ОРИГИНАЛЬНОЕ содержимое файла *.lic
    /// (в формате файла, без начального длинного ключа и «шапки» — StripFileNoise).
    /// Internal — юнит-тесты.
    /// </summary>
    internal static string BuildReadableDetails(ProgramLicense license)
    {
        if (license is null)
            return string.Empty;

        // Строка «Файл лицензии» — полное имя файла лицензии.
        var lines = new List<string>
        {
            "Файл лицензии: " + (string.IsNullOrWhiteSpace(license.FilePath)
                ? license.RawText
                : license.FilePath)
        };

        // Issue #83 (v1.0.1.28): в окне деталей показывается ПОЛНОЕ содержимое файла
        // (RawFullText, с переводами строк), а не однострочная выжимка до 400 символов;
        // если содержимого нет вообще — явный текст «файл не прочитан/пуст» с путём.
        var details = string.IsNullOrWhiteSpace(license.RawFullText)
            ? (string.IsNullOrWhiteSpace(license.RawDetails) ? license.RawText : license.RawDetails)
            : license.RawFullText;
        // Из содержимого срезаются и начальный длинный ключ, и ВСЯ «шапка» файла до
        // первой содержательной строки «ключ=значение» (StripFileNoise, v1.0.1.29);
        // остаётся содержимое в формате файла.
        var stripped = StripFileNoise(details);
        if (stripped.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add(stripped);
        }
        else
        {
            lines.Add(string.Empty);
            lines.Add("Файл не прочитан или пуст"
                      + (string.IsNullOrWhiteSpace(license.FilePath) ? "" : $": {license.FilePath}"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Дата лицензии для отображения (issue #83, v1.0.1.29): «dd.MM.yyyy HH:mm:ss», когда
    /// в файле было время (без AM/PM локали ОС), иначе «dd.MM.yyyy». Public-доступен
    /// из модели/окон (внутри сборки).
    /// </summary>
    internal static string FormatLicDate(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Вырезает из текста файла лицензии начальный длинный ключ — токен вида
    /// «NNNNNNNNNNNNNNNN…» (20+ символов подряд из цифр/букв) в начале текста.
    /// </summary>
    internal static string StripLeadingKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = text.TrimStart();
        var leadingKey = Regex.Match(trimmed, @"^[A-Za-z0-9+/=]{20,}\s*(?:•|\r?\n|\s{2,})?");
        return leadingKey.Success ? trimmed.Substring(leadingKey.Length).TrimStart(' ', '•', '\r', '\n') : trimmed;
    }

    /// <summary>
    /// Чистит текст файла лицензии от «шума» (issue #83, v1.0.1.29): срезает ведущий
    /// длинный ключ И предшествующие/служебные строки («шапку» файла) — остаётся
    /// содержимое, начиная с первой содержательной строки «ключ=значение»/
    /// «ключ: значение». Если содержательных строк нет — возвращается текст без
    /// начального ключа (как раньше).
    /// </summary>
    internal static string StripFileNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = StripLeadingKey(text);
        var lines = trimmed.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            // Первая содержательная строка «ключ=значение»/«ключ: значение» —
            // с неё начинается полезное содержимое файла.
            if (LicKeyValueRegex.IsMatch(line))
                return string.Join(Environment.NewLine, lines.Skip(i)).Trim();
        }
        return trimmed;
    }

    /// <summary>
    /// Значение первой пары «ключ: значение», чей ключ содержит одно из слов-маркеров.
    /// Issue #83 (v1.0.1.29): в реальном файле ключ может стоять НЕ в начале строки
    /// (после длинного ключа в той же строке, после «(»/«;») — если якорный поиск
    /// не нашёл маркер, выполняется запасной проход по парам ВНУТРИ строк
    /// (<see cref="LicKeyValueInlineRegex"/>). Internal — тесты.
    /// </summary>
    internal static string? FindValue(string content, params string[] keyMarkers)
    {
        var anchored = FindValueAnchored(content, keyMarkers);
        if (anchored is not null)
            return anchored;
        return FindValueInline(content, keyMarkers);
    }

    private static string? FindValueAnchored(string content, params string[] keyMarkers)
    {
        foreach (Match match in LicKeyValueRegex.Matches(content))
        {
            var key = match.Groups["key"].Value;
            if (keyMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase)))
                return match.Groups["value"].Value.Trim();
        }
        return null;
    }

    private static string? FindValueInline(string content, params string[] keyMarkers)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;
            // На строке ищем пару, чей ключ содержит маркер, даже если перед ним
            // стоит длинный ключ файла или другой текст.
            var best = (string?)null;
            foreach (Match match in LicKeyValueInlineRegex.Matches(line))
            {
                var key = match.Groups["key"].Value;
                if (!keyMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var value = match.Groups["value"].Value.Trim();
                if (value.Length > 0)
                    best = value; // последнее вхождение на строке — «ключ: значение» в конце.
            }
            if (best is not null)
                return best;
        }
        return null;
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
                if (compact.Success)
                {
                    serialMatch = compact;
                }
                else if (IsRingLicenseStructureLine(line))
                {
                    // Локале-независимый формат ring (issue #83, v1.0.1.23): номер
                    // лицензии «NNNN…-NNNN…» + имя файла *.lic. Строка распознаётся
                    // даже если локализованные маркеры («Имя файла») пришли mojibake
                    // или в другой локали — таблица лицензий не остаётся пустой.
                    var number = LicenseNumberRegex.Match(line);
                    licenses.Add(new ProgramLicense
                    {
                        SerialNumber = number.Success ? number.Value : string.Empty,
                        Module = LicenseFileRegex.Match(line).Value,
                        RawText = line
                    });
                    continue;
                }
                else
                {
                    continue;
                }
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

    /// <summary>
    /// Строка с лицензией в структурном формате ring (issue #83, v1.0.1.23): номер
    /// лицензии «NNNN…-NNNN…» (две группы по 10–16 цифр через дефис) И имя файла
    /// лицензии *.lic (в кавычках или без). Оба признака не зависят от локали вывода.
    /// Internal — юнит-тесты.
    /// </summary>
    internal static bool IsRingLicenseStructureLine(string line)
    {
        return LicenseNumberRegex.IsMatch(line) && LicenseFileRegex.IsMatch(line);
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

    /// <summary>
    /// TTL кэша найденного пути ring (п.4.1 плана v100114): where.exe/which, реестровый
    /// PATH и перебор каталогов выполняются не при каждом опросе монитора (~5 с), а не
    /// чаще одного раза за <see cref="RingPathCacheTtl"/>. Кэш сбрасывается при неудачном
    /// запуске найденного кандидата (StartError — путь стал неактуален). Сеттер internal —
    /// для юнит-тестов (в тестах TTL укорачивается).
    /// </summary>
    internal TimeSpan RingPathCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    // TTL-кэш пути ring: найденное значение (включая отрицательный результат) и срок годности.
    private readonly object _ringPathCacheGate = new();
    private string? _ringPathCachePath;
    private bool _ringPathCacheFound;
    private DateTimeOffset _ringPathCacheExpiresAt;
    private bool _ringPathCacheHasValue;

    /// <summary>Точка подмены пробы PATH (where.exe/which/ring --version) — юнит-тесты.</summary>
    internal Func<CancellationToken, Task<string?>> RingPathProbeAsync { get; set; } = FindRingInPathAsync;

    /// <summary>Точка подмены поиска вне PATH (реестр/каталоги 1С) — юнит-тесты.</summary>
    internal Func<CancellationToken, Task<string?>> RingPathAlternateProbeAsync { get; set; } =
        FindRingViaRegistryAndKnownDirsAsync;

    /// <summary>Точка подмены запуска внешнего процесса — юнит-тесты.</summary>
    internal Func<string, string[], CancellationToken, Task<ProcessRunResult>> RunProcessAsync { get; set; } =
        (command, arguments, ct) => ProcessRunner.RunAsync(command, arguments, timeout: CommandTimeout, ct: ct);

    /// <summary>Точка подмены чтения каталога файлов лицензий — юнит-тесты.</summary>
    internal Func<IReadOnlyList<ProgramLicense>> ReadLicenseDirectory { get; set; } = TryReadLicenseDirectory;

    /// <summary>
    /// Резолвит путь ring с TTL-кэшем (п.4.1 плана v100114): первое обращение выполняет
    /// пробу PATH (<see cref="FindRingInPathAsync"/>) и поиск вне PATH
    /// (<see cref="FindRingViaRegistryAndKnownDirsAsync"/>), последующие обращения в течение
    /// TTL отдают закэшированный результат (включая отрицательный — «ring нет»).
    /// Internal — юнит-тесты.
    /// </summary>
    internal async Task<string?> ResolveRingPathAsync(CancellationToken ct = default)
    {
        lock (_ringPathCacheGate)
        {
            if (_ringPathCacheHasValue && DateTimeOffset.UtcNow < _ringPathCacheExpiresAt)
                return _ringPathCacheFound ? _ringPathCachePath : null;
        }

        var path = await RingPathProbeAsync(ct).ConfigureAwait(false);
        if (path is null)
        {
            // issue #83 (репорт 7OH от 2026-10-30 «в окне - не найдено, в командной строке
            // работает»): PATH процесса приложения может отличаться от PATH консоли
            // пользователя (запуск из ярлыка/планировщика не наследует свежий
            // пользовательский PATH). Пробуем найти ring по СВЕЖЕМУ PATH из реестра и
            // известным каталогам установки 1С — при находке запускаем по полному пути
            // без перезапуска приложения.
            path = await RingPathAlternateProbeAsync(ct).ConfigureAwait(false);
        }

        StoreRingPathCache(path);
        return path;
    }

    /// <summary>Кладёт результат резолва в TTL-кэш (в том числе отрицательный). Internal — юнит-тесты.</summary>
    internal void StoreRingPathCache(string? path)
    {
        lock (_ringPathCacheGate)
        {
            _ringPathCachePath = path;
            _ringPathCacheFound = path is not null;
            _ringPathCacheExpiresAt = DateTimeOffset.UtcNow + RingPathCacheTtl;
            _ringPathCacheHasValue = true;
        }
    }

    /// <summary>Сбрасывает TTL-кэш пути ring (например, при неудачном запуске кандидата).</summary>
    internal void InvalidateRingPathCache()
    {
        lock (_ringPathCacheGate)
        {
            _ringPathCacheHasValue = false;
            _ringPathCachePath = null;
            _ringPathCacheFound = false;
        }
    }

    /// <inheritdoc />
    public async Task<RingLicenseResult> GetInstalledLicensesAsync(CancellationToken cancellationToken = default)
    {
        // issue #83 (последний комментарий 7OH от 2026-10-29): утилиту ring НЕ ищем
        // ручным перебором PATH/PATHEXT с подменой полного пути — доступность проверяем
        // запуском «where.exe ring»/«which ring» (запасной способ — «ring --version»),
        // а вызываем «ring» как есть, поиск по PATH выполняет сама ОС.
        // п.4.1 плана v100114: резолв пути обёрнут в TTL-кэш (where.exe + реестр +
        // перебор каталогов — не при каждом опросе монитора ~5 с).
        var ringInPath = await ResolveRingPathAsync(cancellationToken).ConfigureAwait(false);

        // 1) ring license list (Windows и Linux — если ring доступен в PATH).
        var candidatesRan = false;
        string? lastCandidateOutput = null;
        foreach (var (command, arguments, source) in CommandCandidates(
            includePathRing: ringInPath is not null, ringPath: ringInPath))
        {
            candidatesRan = true;
            var result = await RunProcessAsync(command, arguments, cancellationToken)
                .ConfigureAwait(false);

            // issue #83 (репорт 7OH от 2026-10-31 «в логе ring найден, а на вкладке
            // Лицензии — Не найдена»): результат КАЖДОГО кандидата пишется в журнал —
            // даже при Success/пустом выводе и нуле распознанных лицензий (раньше этот
            // случай молча пропускался, и причину было не установить).
            var licenses = result.Success && !string.IsNullOrWhiteSpace(result.StdOut)
                ? RingLicenseParser.Parse(result.StdOut)
                : Array.Empty<ProgramLicense>();
            Log.Info(LogCategory.Rac,
                $"RING: кандидат «{source}»: код=" +
                $"{(result.TimedOut ? "таймаут" : result.ExitCode.ToString(CultureInfo.InvariantCulture))}, " +
                $"байт вывода={(result.StdOut ?? string.Empty).Length}, " +
                $"распознано лицензий={licenses.Count}{(result.TimedOut ? " (таймаут)" : string.Empty)}.");

            // п.4.1 плана v100114: найденного кандидата не удалось запустить (например,
            // кэшированный путь стал неактуален) — сбрасываем TTL-кэш пути ring.
            if (!string.IsNullOrWhiteSpace(result.StartError))
            {
                InvalidateRingPathCache();
                Log.Info(LogCategory.Rac,
                    $"RING: кандидат «{source}» не запущен: {result.StartError} — кэш пути ring сброшен.");
            }

            if (licenses.Count > 0)
            {
                // issue #80: INFO-записи RING — через категорийный гейт CM_RAC + CM_ON
                // (не «капают» в журнал при выключенном логировании).
                Log.Info(LogCategory.Rac,
                    $"RING: распознано программных лицензий: {licenses.Count} (источник: {source}).");
                // Issue #83 (v1.0.1.26): дополняем лицензии полями файла *.lic
                // (владелец, рег. номер, пользователь/компьютер, даты, RawDetails).
                EnrichWithLicFiles(licenses);
                return new RingLicenseResult { Licenses = licenses };
            }

            lastCandidateOutput = result.CombinedOutput;

            // issue #83 (репорт 7OH от 2026-10-30): ring отвечает «Module "license" is not
            // registered in the command registry» — утилита ЕСТЬ, но без модуля лицензий.
            // Перебирать остальных кандидатов бессмысленно (тот же ring, тот же ответ) —
            // прекращаем перебор и показываем на вкладке понятное предупреждение про
            // модуль, а не вводящее в заблуждение «утилита не найдена».
            if (IsLicenseModuleNotRegisteredOutput(result.CombinedOutput))
            {
                var moduleWarning = BuildModuleNotRegisteredMessage();
                // issue #80: запись направления RAC — через категорийный гейт CM_RAC + CM_ON.
                Log.Info(LogCategory.Rac,
                    $"RING: {source} — модуль лицензий ring не установлен: {result.CombinedOutput}");
                return new RingLicenseResult { Warning = moduleWarning };
            }

            if (!result.Success && !string.IsNullOrWhiteSpace(result.CombinedOutput))
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
        var fallback = ReadLicenseDirectory();
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

        // issue #83 (репорт 7OH от 2026-10-31 19:20): ring НАЙДЕН и запускался, но список
        // лицензий не получен — в Warning идёт вывод последнего кандидата, а не вводящее
        // в заблуждение «утилита не найдена».
        if (candidatesRan)
            return new RingLicenseResult { Warning = BuildNoLicensesMessage(lastCandidateOutput) };

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
            "проверка: PATH процесса (where.exe ring / ring --version), PATH из реестра " +
            "(HKCU\\Environment и HKLM\\…\\Session Manager\\Environment), каталог bin платформы " +
            "рядом с rac, известные каталоги 1С (Program Files\\1cv8\\<версия>\\bin, %LOCALAPPDATA%\\1C\\1cv8)";
#else
        const string searched =
            "проверка: which ring / ring --version, licensetool в каталогах /opt/1cv8";
#endif
        // issue #83 (последний комментарий 7OH): ring может быть установлен как .exe в
        // каталоге bin платформы либо добавлен в PATH — в том числе как командный файл
        // (.cmd/.bat); проверка доступности выполняется так же, как в командной строке.
        // issue #83 (репорт 7OH от 2026-10-30): если ring был установлен недавно
        // (например, модуль лицензий после репорта «Module license is not registered»),
        // работающий процесс всё равно не видит его в PATH — PATH процесса фиксируется
        // при запуске, нужно перезапустить приложение.
        return "Не удалось получить список лицензий: не найдена утилита ring (" + searched +
               "; каталог файлов лицензий платформы пуст). Установите ring (обычно лежит в " +
               "каталоге bin платформы рядом с rac) либо добавьте его в PATH (в т.ч. как " +
               "командный файл .cmd/.bat; проверка выполняется так же, как в командной строке). " +
               "Если утилита установлена недавно и в командной строке работает — перезапустите " +
               "Workspace 1C: PATH процесса приложения фиксируется при его старте. После " +
               "установки нажмите «Обновить».";
    }

    /// <summary>
    /// Маркер ответа ring «Module "license" is not registered in the command registry»
    /// (issue #83): утилита доступна, но модуль лицензий в ней не установлен. Сравнение —
    /// по инвариантной к регистру подстроке (префикс может локализоваться 1С).
    /// Internal — тесты.
    /// </summary>
    internal const string LicenseModuleNotRegisteredMarker =
        "is not registered in the command registry";

    /// <summary>Определяет, отвечает ли вывод ring отсутствием модуля лицензий. Internal — тесты.</summary>
    internal static bool IsLicenseModuleNotRegisteredOutput(string? output) =>
        !string.IsNullOrWhiteSpace(output) &&
        output.Contains(LicenseModuleNotRegisteredMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Понятное сообщение вкладки «Лицензии» для случая «ring есть, модуля лицензий нет»
    /// (issue #83): как установить модуль — вместо вводящего в заблуждение «утилита не
    /// найдена». Internal — для юнит-тестов читаемого сообщения.
    /// </summary>
    internal static string BuildModuleNotRegisteredMessage()
    {
        return "Утилита ring найдена, но в ней не установлен модуль лицензий (ring ответил: " +
               "Module \"license\" is not registered). Установите модуль лицензий ring — " +
               "однократно, командой «ring module install license» (см. документацию 1С по " +
               "утилите ring), затем нажмите «Обновить». Если после установки модуля " +
               "утилита по-прежнему не находится — перезапустите Workspace 1C: запущенное " +
               "приложение видит PATH, зафиксированный при его старте.";
    }

    /// <summary>
    /// Понятное сообщение вкладки «Лицензии» для случая «ring найден и запускался, но
    /// список лицензий не получен» (issue #83, репорт 7OH от 2026-10-31 19:20): вывод
    /// последнего кандидата вместо вводящего в заблуждение «утилита не найдена».
    /// Internal — для юнит-тестов читаемого сообщения.
    /// </summary>
    internal static string BuildNoLicensesMessage(string? lastCandidateOutput)
    {
        // Issue #83 (репорт 7OH 2026-11-01: «вывод в консоли тоже пустой»): при пустом
        // ответе ring прежний текст давал бессмыслицу «Ответ последней проверенной
        // команды: .» — вместо неё специализированный текст с командами самопроверки.
        if (string.IsNullOrWhiteSpace(lastCandidateOutput))
        {
            return "Утилита ring найдена и запущена, но вернула пустой ответ. Проверьте " +
                   "в командной строке: «ring module list» (установлен ли модуль лицензий) " +
                   "и «ring license list». Если в консоли лицензии есть, а здесь нет — " +
                   "пришлите лог категории CM_RAC в issue Workspace 1C.";
        }
        // issue #83 (v1.0.1.23): кракозябры в UI-сообщении недопустимы. Вывод
        // нормализуется: (а) характерный mojibake «cp866-текст, декодированный как
        // cp1251» перезакодируется обратно (raw→cp866), если после этого распознаётся
        // маркер вывода ring; (б) оставшиеся символы замены U+FFFD заменяются на «?» —
        // читаемость цифробуквенных фрагментов (номеров лицензий, имён файлов)
        // сохраняется.
        var output = ConsoleOutputDecoder.FixCp1251ToCp866Mojibake(lastCandidateOutput);
        if (output.Contains('\uFFFD', StringComparison.Ordinal))
            output = output.Replace('\uFFFD', '?');

        return "Утилита ring найдена и запущена, но список лицензий не получен. Ответ последней " +
               "проверенной команды: " + TruncateForLog(output) +
               ". Если модуль лицензий ring установлен и этот ответ выглядит ошибочным — " +
               "пришлите этот текст в issue Workspace 1C.";
    }

    /// <summary>
    /// Разлагает найденный путь к утилите ring на «исполняемый вызов» (issue #83):
    /// командные файлы Windows (.cmd/.bat) запускаются через <c>cmd.exe /d /c call</c> —
    /// именно так их выполняет командная строка пользователя, а прямой <c>CreateProcess</c>
    /// (UseShellExecute=false) не находит их по PATHEXT — «Не удается найти указанный
    /// файл». Всё остальное (.exe/.com, без расширения на Linux) запускается напрямую
    /// по полному пути. Internal — для юнит-тестов без запуска процессов.
    /// </summary>
    internal static (string Command, string[] Arguments) ToLaunchCommand(string ringPath, params string[] toolArgs)
    {
#if WINDOWS
        var extension = Path.GetExtension(ringPath);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            // v100114 (issue #83): форма «cmd.exe /d /c call "<путь>" args». Хвост после
            // /c начинается со слова «call», а не с кавычки — поэтому cmd.exe НЕ применяет
            // старое правило срезания первой/последней кавычек (оно срабатывает, когда в
            // пути есть «особые» символы &<>()@^| — например «Program Files (x86)» — и
            // условие сохранения кавычек из справки cmd /? нарушается). «/d» отключает
            // исполнение реестровых AutoRun-скриптов.
            return ("cmd.exe", new[] { "/d", "/c", "call", ringPath }.Concat(toolArgs).ToArray());
        }
#endif
        return (ringPath, (toolArgs ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>
    /// Кандидаты на выполнение (имя, аргументы, описание источника):
    /// ring из PATH (приоритет — issue #83, комментарий 7OH: «просто вызывать ring как
    /// есть»), затем ring.exe в каталоге bin платформы и licensetool платформы Linux
    /// как запасные. Кандидат «ring (PATH)» добавляется только если проверка доступности
    /// (<see cref="FindRingInPathAsync"/>) подтвердила, что утилита есть в PATH; при
    /// переданном полном пути <paramref name="ringPath"/> строится «исполняемый вызов»
    /// через <see cref="ToLaunchCommand"/> (командные файлы .cmd/.bat — через cmd.exe /c).
    /// Internal — для юнит-тестов без запуска процессов.
    /// </summary>
    internal static IReadOnlyList<(string Command, string[] Arguments, string Source)> CommandCandidates(
        bool includePathRing = true, string? ringPath = null)
    {
        var candidates = new List<(string, string[], string)>();

        // issue #83 (последний комментарий 7OH): вызываем «ring» — поиск по PATH выполняет
        // сама ОС; доступность подтверждена заранее через where/which или «ring --version».
        // Если where/which вернули полный путь (в т.ч. командный файл .cmd/.bat) — запускаем
        // именно его в виде, который может выполнить ОС (см. ToLaunchCommand).
        if (includePathRing)
        {
            if (!string.IsNullOrWhiteSpace(ringPath))
            {
                var (command, args) = ToLaunchCommand(ringPath!, "license", "list");
                candidates.Add((command, args, "ring (PATH)"));
            }
            else
            {
                candidates.Add(("ring", new[] { "license", "list" }, "ring (PATH)"));
            }
        }

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
    /// утилита недоступна.
    /// <para>
    /// Issue #83 (1.0.1.11, репорт 7OH «в лог писать вывод where.exe ring»): вывод
    /// утилиты поиска логируется в CM_RAC и при успехе, и при неудаче — чтобы
    /// расхождение «в консоли найдено / в приложении не найдено» было видно сразу
    /// (PATH процесса может отличаться от PATH консоли пользователя).
    /// </para>
    /// </summary>
    internal static async Task<string?> FindRingInPathAsync(CancellationToken ct = default)
    {
#if WINDOWS
        const string probeTool = "where.exe";
        var probeArgs = new[] { "ring" };
#else
        const string probeTool = "which";
        var probeArgs = new[] { "ring" };
#endif
        var probe = await ProcessRunner.RunAsync(probeTool, probeArgs,
                timeout: CommandTimeout, ct: ct)
            .ConfigureAwait(false);
        if (probe.Success)
        {
            var line = FirstNonEmptyLine(probe.StdOut);
            if (line is not null)
            {
                Log.Info(LogCategory.Rac,
                    $"RING: {probeTool} {probeArgs[0]} — найдено: {line}");
                return line;
            }
        }

        // При неудаче — вывод утилиты поиска в журнал целиком (issue #83, 7OH:
        // «можно в лог писать вывод where.exe ring?»), усечённо до 500 символов.
        Log.Info(LogCategory.Rac,
            $"RING: {probeTool} {probeArgs[0]} не дал результата (код={probe.ExitCode}): " +
            TruncateForLog(probe.CombinedOutput));

        // Запасная проверка (если where/which нет в системе или не дала результата) —
        // прямой запуск «ring --version»: работает — утилита доступна (issue #83,
        // комментарий 7OH: «Запустили "ring --version" и посмотрели - ошибка или работает»).
        var version = await ProcessRunner.RunAsync("ring", new[] { "--version" },
                timeout: CommandTimeout, ct: ct)
            .ConfigureAwait(false);
        if (version.Success)
            return "ring";
        Log.Info(LogCategory.Rac,
            $"RING: ring --version не дал результата (код={version.ExitCode}): " +
            TruncateForLog(version.CombinedOutput));
        return null;
    }

    /// <summary>Усекает вывод внешней утилиты для записи в журнал. Internal — тесты.</summary>
    internal static string TruncateForLog(string? output)
    {
        var text = (output ?? string.Empty).Trim();
        if (text.Length == 0)
            return "(пусто)";
        return text.Length <= 500 ? text : text[..500] + "…";
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

    /// <summary>
    /// Разбивает строку PATH на каталоги (разделитель «;», пустые элементы отбрасываются).
    /// Internal — юнит-тесты.
    /// </summary>
    internal static IReadOnlyList<string> SplitPathDirectories(string? pathValue) =>
        (pathValue ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Разворачивает переменные окружения в значении реестрового PATH типа
    /// REG_EXPAND_SZ («%SystemRoot%\...»). Функция-расширитель инъектится —
    /// юнит-тесты не зависят от машины. Internal — юнит-тесты.
    /// </summary>
    internal static string ExpandRegistryPath(string raw, Func<string, string>? expander = null) =>
        (expander ?? Environment.ExpandEnvironmentVariables)(raw);

    /// <summary>
    /// Чистая функция «список каталогов → единый список поиска ring» (issue #83):
    /// PATH процесса → свежий PATH из реестра → известные каталоги. Пустые элементы
    /// отбрасываются, дубликаты (регистронезависимо) схлопываются с сохранением порядка.
    /// Internal — юнит-тесты.
    /// </summary>
    internal static IReadOnlyList<string> BuildRingSearchDirectories(
        string? processPath, string? registryPath, IEnumerable<string>? knownDirs)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { SplitPathDirectories(processPath), SplitPathDirectories(registryPath) })
        {
            foreach (var dir in source)
            {
                if (seen.Add(dir))
                    dirs.Add(dir);
            }
        }
        foreach (var dir in knownDirs ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(dir) && seen.Add(dir))
                dirs.Add(dir);
        }
        return dirs;
    }

    /// <summary>
    /// Ищет утилиту ring в переданных каталогах (issue #83): первый существующий файл
    /// <c>ring.exe</c> / <c>ring.cmd</c> / <c>ring.bat</c> (Windows) или <c>ring</c>
    /// (Linux). Проверка существования файла инъектится — юнит-тесты без файловой системы.
    /// Возвращает полный путь или null. Internal — юнит-тесты.
    /// </summary>
    internal static string? FindRingInDirectories(
        IEnumerable<string> directories, Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? (path => File.Exists(path));
#if WINDOWS
        string[] fileNames = { "ring.exe", "ring.cmd", "ring.bat" };
#else
        string[] fileNames = { "ring" };
#endif
        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            foreach (var fileName in fileNames)
            {
                try
                {
                    var candidate = Path.Combine(dir, fileName);
                    if (exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Невалидный путь — пробуем следующий каталог.
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Свежий PATH из реестра Windows (issue #83): HKCU\Environment + HKLM\SYSTEM\...
    /// Session Manager\Environment — PATH «будущей» консоли пользователя. Значения типа
    /// REG_EXPAND_SZ разворачиваются. Ошибки чтения не бросаются (возвращается null).
    /// Internal — юнит-тесты вызывают <see cref="ExpandRegistryPath"/> на фикстурах,
    /// сам доступ к реестру тестируется только на Windows-машине разработчика.
    /// </summary>
    internal static string? ReadRegistryPathValue()
    {
#if WINDOWS
        try
        {
            const string machineKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
            var parts = new List<string>();
            foreach (var (hive, keyPath) in new[] { (Registry.CurrentUser, "Environment"), (Registry.LocalMachine, machineKeyPath) })
            {
                using var key = hive.OpenSubKey(keyPath, writable: false);
                if (key is null)
                    continue;
                // DoNotExpandEnvironmentNames: разворачиваем сами через
                // ExpandRegistryPath (REG_EXPAND_SZ; REG_SZ разворачивается без изменений).
                if (key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value
                    || string.IsNullOrWhiteSpace(value))
                    continue;
                var expanded = ExpandRegistryPath(value);
                if (!string.IsNullOrWhiteSpace(expanded))
                    parts.Add(expanded);
            }
            return parts.Count > 0 ? string.Join(";", parts) : null;
        }
        catch (Exception ex)
        {
            // Реестр недоступен (ограничения среды) — поиск продолжается без него.
            Log.Info(LogCategory.Rac, $"RING: PATH из реестра недоступен: {ex.Message}");
            return null;
        }
#else
        return null;
#endif
    }

    /// <summary>
    /// Известные каталоги установки 1С, где может лежать ring (issue #83): каталог bin
    /// платформы рядом с rac, %LOCALAPPDATA%\1C\1cv8 и C:\Program Files[ (x86)]\1cv8\
    /// <версия>\bin (последняя установленная версия). Internal — юнит-тесты.
    /// </summary>
    internal static IReadOnlyList<string> KnownRingDirectories()
    {
        var dirs = new List<string>();
        try
        {
            var rac = OneCPlatformLocator.FindRacExecutable();
            if (!string.IsNullOrWhiteSpace(rac))
            {
                var dir = Path.GetDirectoryName(rac);
                if (!string.IsNullOrWhiteSpace(dir))
                    dirs.Add(dir);
            }
        }
        catch
        {
            // Локатор платформ недоступен — пропускаем кандидат.
        }

#if WINDOWS
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                dirs.Add(Path.Combine(localAppData, "1C", "1cv8"));

            foreach (var root in new[] { @"C:\Program Files\1cv8", @"C:\Program Files (x86)\1cv8" })
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var versionDir in Directory.EnumerateDirectories(root).OrderByDescending(d => d))
                {
                    var bin = Path.Combine(versionDir, "bin");
                    if (Directory.Exists(bin))
                    {
                        dirs.Add(bin);
                        break; // последняя установленная версия платформы.
                    }
                }
            }
        }
        catch
        {
            // Каталоги платформ недоступны — пропускаем кандидатов.
        }
#endif
        return dirs;
    }

    /// <summary>
    /// Поиск ring вне PATH процесса (issue #83, пятая итерация): свежий PATH из реестра
    /// (HKCU + HKLM) и известные каталоги установки 1С. Пишет диагностику CM_RAC: размеры
    /// обоих PATH, каталоги поиска и результат. Возвращает полный путь к утилите или null.
    /// Internal — вызывается из <see cref="GetInstalledLicensesAsync"/> после неудачи
    /// <see cref="FindRingInPathAsync"/>.
    /// </summary>
    internal static async Task<string?> FindRingViaRegistryAndKnownDirsAsync(CancellationToken ct = default)
    {
        var processPath = Environment.GetEnvironmentVariable("PATH");
        var registryPath = ReadRegistryPathValue();
        var knownDirs = KnownRingDirectories();
        var directories = BuildRingSearchDirectories(processPath, registryPath, knownDirs);

        // issue #83: диагностика — где искали и чем PATH процесса отличается от
        // реестрового (отличие окружения должно быть видно из журнала сразу).
        Log.Info(LogCategory.Rac,
            $"RING: поиск ring вне PATH процесса: PATH процесса: {SplitPathDirectories(processPath).Count} элементов, " +
            $"PATH реестра: {SplitPathDirectories(registryPath).Count} элементов, " +
            $"известных каталогов 1С: {knownDirs.Count}, всего каталогов к проверке: {directories.Count}.");
        Log.Info(LogCategory.Rac,
            "RING: каталоги поиска ring: " +
            (directories.Count > 0 ? string.Join("; ", directories) : "(нет доступных каталогов)") +
            (string.IsNullOrWhiteSpace(registryPath) ? "; PATH реестра: (пусто/недоступен)" : string.Empty));

        var found = await Task.Run(() => FindRingInDirectories(directories), ct).ConfigureAwait(false);
        if (found is not null)
            Log.Info(LogCategory.Rac, $"RING: ring найден вне PATH процесса — запуск по полному пути: {found}");
        else
            Log.Info(LogCategory.Rac, "RING: ring не найден вне PATH процесса (реестровый PATH и известные каталоги 1С проверены).");
        return found;
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
        var licenses = new List<ProgramLicense>();
        foreach (var (file, fileName) in EnumerateLicFiles())
        {
            var license = TryReadLicFile(file, fileName);
            if (license is not null)
                licenses.Add(license);
        }
        return licenses;
    }

    /// <summary>
    /// Каталоги файлов лицензий платформы (issue #83, v1.0.1.28): Windows — все места
    /// хранения 1С (%ProgramData%\1C\1Cv8\licenses, %ProgramData%\1C\licenses и
    /// пер-пользовательские %LOCALAPPDATA%/%APPDATA%\1C\1cv8\licenses); Linux — без
    /// изменений. Дубли путей отбрасываются. Internal — тесты.
    /// </summary>
    internal static List<string> LicenseDirectories()
    {
        var directories = new List<string>();
#if WINDOWS
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            directories.Add(Path.Combine(programData, "1C", "1Cv8", "licenses"));
            directories.Add(Path.Combine(programData, "1C", "1cv8", "licenses"));
            // Issue #83 (репорт 7OH, пустая таблица лицензий): 1С кладёт *.lic также
            // в %ProgramData%\1C\licenses (без 1Cv8) и в пер-пользовательские каталоги.
            directories.Add(Path.Combine(programData, "1C", "licenses"));
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            directories.Add(Path.Combine(localAppData, "1C", "1cv8", "licenses"));
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roamingAppData))
            directories.Add(Path.Combine(roamingAppData, "1C", "1cv8", "licenses"));
#else
        directories.Add("/etc/1cv8/License");
        directories.Add("/var/1C/licenses");
#endif
        // Дедупликация: регистр каталогов на Windows может дать повтор (1Cv8/1cv8).
        return directories
            .Select(d => d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Файлы лицензий по ВСЕМ существующим каталогам (issue #83, v1.0.1.28 — раньше
    /// брался только первый существующий каталог, и лицензии из per-user каталога
    /// не попадали в таблицу): (полный путь, имя файла). Дубли по имени файла
    /// отбрасываются (одна и та же .lic, скопированная в несколько мест).
    /// </summary>
    private static IEnumerable<(string File, string FileName)> EnumerateLicFiles()
    {
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in LicenseDirectories())
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory)
                         .Where(f => Path.GetExtension(f).Length > 0)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(file);
                if (seenFileNames.Add(fileName))
                    yield return (file, fileName);
            }
        }
    }

    /// <summary>
    /// Читает файл лицензии *.lic и разбирает его <see cref="RingLicenseParser.ParseLicFile"/>
    /// (issue #83, v1.0.1.26). Кодировка файла заранее неизвестна (UTF-8 / UTF-16 / CP1251),
    /// поэтому пробуется детект по BOM и валидности декодирования. Ошибки не бросаются.
    /// </summary>
    internal static ProgramLicense? TryReadLicFile(string path, string fileName)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            string content;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            {
                content = System.Text.Encoding.Unicode.GetString(bytes);
            }
            else if (bytes.Length > 0 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                content = System.Text.Encoding.UTF8.GetString(bytes);
            }
            else
            {
                content = System.Text.Encoding.UTF8.GetString(bytes);
                if (content.Contains('\uFFFD', StringComparison.Ordinal))
                {
                    // Не UTF-8 (скорее всего CP1251) — перечитываем в CP1251.
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    content = System.Text.Encoding.GetEncoding(1251).GetString(bytes);
                }
            }

            var license = RingLicenseParser.ParseLicFile(content, fileName);
            license.Module = $"файл лицензии: {fileName}";
            // Полный путь — для клика «открыть папку с файлом» (issue #83, v1.0.1.27).
            license.FilePath = path;
            return license;
        }
        catch (Exception ex)
        {
            // Файл недоступен/не читается — всё равно показываем имя файла в таблице
            // (путь сохраняем: папку можно открыть даже по битому файлу).
            // Issue #83 (v1.0.1.28): причина сбоя НЕ молчит — она попадает в RawDetails
            // и видна в окне деталей вместо пустого «только имя файла».
            return new ProgramLicense
            {
                RawText = fileName,
                RawDetails = $"файл не прочитан ({ex.GetType().Name}): {ex.Message}",
                Module = $"файл лицензии: {fileName}",
                FilePath = path
            };
        }
    }

    /// <summary>
    /// Обогащает лицензии из ring полями файла *.lic (issue #83, v1.0.1.26):
    /// владелец, рег. номер, пользователь/компьютер, даты и RawDetails. Сопоставление —
    /// по имени файла (в выводе ring «номер (Имя файла: "X.lic")») либо по серийному
    /// номеру. Точка подмены — юнит-тесты.
    /// </summary>
    internal Func<IReadOnlyList<ProgramLicense>> LicFilesProvider { get; set; } = TryReadLicenseDirectory;

    internal void EnrichWithLicFiles(IReadOnlyList<ProgramLicense> licenses)
    {
        if (licenses is null || licenses.Count == 0)
            return;
        IReadOnlyList<ProgramLicense> files;
        try
        {
            files = LicFilesProvider();
        }
        catch
        {
            return;
        }
        if (files.Count == 0)
            return;

        foreach (var license in licenses)
        {
            // Имя файла из вывода ring: «(Имя файла: "X.lic")» либо «X.lic» в Module.
            var moduleMatch = Regex.Match(license.Module ?? string.Empty, @"(?i)(?<name>[\w\-.]+\.lic)");
            // Issue #83 (v1.0.1.27): Module файловой лицензии из запасного источника —
            // «файл лицензии: X.lic»; Path.GetFileName не извлекает имя из такой строки
            // (двоеточие — не разделитель пути), поэтому дополнительно ищем имя файла
            // подстрокой в Module.
            var fileMatch = moduleMatch.Success
                ? files.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f.Module), moduleMatch.Groups["name"].Value, StringComparison.OrdinalIgnoreCase) ||
                    (f.Module ?? string.Empty).Contains(moduleMatch.Groups["name"].Value, StringComparison.OrdinalIgnoreCase))
                : null;
            var bySerial = fileMatch is null && license.SerialNumber.Length > 0
                ? files.FirstOrDefault(f =>
                    string.Equals(f.RegNumber, license.SerialNumber, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.SerialNumber, license.SerialNumber, StringComparison.OrdinalIgnoreCase))
                : null;
            var source = fileMatch ?? bySerial;
            if (source is null)
                continue;

            if (license.Owner.Length == 0) license.Owner = source.Owner;
            if (license.RegNumber.Length == 0) license.RegNumber = source.RegNumber;
            if (license.User.Length == 0) license.User = source.User;
            if (license.Computer.Length == 0) license.Computer = source.Computer;
            if (license.IssueDate is null) license.IssueDate = source.IssueDate;
            if (license.RawDetails.Length == 0) license.RawDetails = source.RawDetails;
            // Issue #83 (v1.0.1.28): полное содержимое файла переносится тоже.
            if (license.RawFullText.Length == 0) license.RawFullText = source.RawFullText;
            // Новые поля из файла *.lic (issue #83, v1.0.1.27): тип, версия
            // (левая часть «Наименования»), срок действия, пользователей, путь к файлу.
            if (license.Type.Length == 0) license.Type = source.Type;
            if (license.Version.Length == 0) license.Version = source.Version;
            if (license.ExpiryDate is null) license.ExpiryDate = source.ExpiryDate;
            if (license.LicenseCount == 0) license.LicenseCount = source.LicenseCount;
            if (license.FilePath.Length == 0) license.FilePath = source.FilePath;
        }
    }
}
