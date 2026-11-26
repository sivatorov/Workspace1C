using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистый парсер каталога технологической платформы 1С на <c>releases.1c.ru</c>:
/// список версий со страницы <c>project/<ник></c> (все строки таблицы
/// <c>#versionsTable</c>, не только первая) и файлы дистрибутива из ответа
/// <c>version_files?nick=…&ver=…</c>. Не выполняет сетевых запросов, устойчив
/// к изменению структуры HTML/JSON портала и не бросает исключений на битом входе.
/// </summary>
public static class OneCPlatformCatalogParser
{
    /// <summary>
    /// Ник каталога технологической платформы 8.3 на <c>releases.1c.ru/project/<ник></c>.
    /// Значение сверено с фактическим адресом каталога на портале (п. 2.7 плана 0.3.9.208–216).
    /// </summary>
    public const string Platform83Nick = "Platform83";

    /// <summary>
    /// Ник каталога технологической платформы 8.5 на <c>releases.1c.ru/project/<ник></c>
    /// (issue #334: «стоит проверять и releases.1c.ru/project/Platform85»).
    /// </summary>
    public const string Platform85Nick = "Platform85";

    /// <summary>Алиас для обратной совместимости (использовался до введения списка ников).</summary>
    public const string PlatformNick = Platform83Nick;

    /// <summary>Поддерживаемые ники каталогов технологической платформы на releases.1c.ru.</summary>
    public static readonly IReadOnlyList<string> SupportedPlatformNicks =
        new[] { Platform83Nick, Platform85Nick };

    /// <summary>Регулярное выражение для ссылки на страницу файлов релиза вида
    /// <c>/version_files?nick=…&ver=…</c> с захватом адреса и текста (номера версии).</summary>
    private static readonly Regex VersionFilesLinkRegex = new(
        @"href\s*=\s*[""'](?<href>[^""']*version_files[^""']*ver\s*=[^""']*)[""'][^>]*>\s*(?<ver>[^<]+?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Регулярное выражение таблицы <c>id="versionsTable"</c> (список версий каталога).</summary>
    private static readonly Regex VersionsTableRegex = new(
        @"<table[^>]*id\s*=\s*[""']versionsTable[""'][^>]*>(?<table>.*?)</table>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Регулярное выражение строки <c><tr>…</tr></c> таблицы версий.</summary>
    private static readonly Regex TableRowRegex = new(
        @"<tr[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Регулярное выражение для поиска прямых ссылок на файлы дистрибутивов платформы
    /// (<c>.zip</c>/<c>.rar</c>/<c>.7z</c>/<c>.exe</c>/<c>.arj</c>/<c>.deb</c>/<c>.rpm</c>/
    /// <c>.tar.gz</c>) в ответе <c>version_files</c> (JSON или HTML), устойчиво к неизвестной
    /// структуре. Query-часть ссылки учитывается. Список расширений синхронизирован с
    /// <c>OneCUpdatesService.DistributionFileRegex</c> (issue #352): дистрибутивы платформы
    /// 8.3/8.5 отдаются в том числе архивами <c>.rar</c>/<c>.7z</c> (issue #330 — страница
    /// Platform85 8.5.1.1522 при прежнем наборе расширений давала «распознано файлов 0»).</summary>
    private static readonly Regex DistributionFileLinkRegex = new(
        @"(?<url>(?:https?://|/)[^""'\s<>]*?\.(?:zip|rar|7z|exe|arj|deb|rpm|tar\.gz)(?:[?#][^""'\s<>]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Регулярное выражение для ссылок на передачу файла дистрибутива
    /// (<c>transfer_file?…</c>/<c>additional_file?…</c>/<c>version_file?…</c>, issue #330):
    /// альтернативная разметка страницы <c>version_files</c>, где дистрибутив отдаётся
    /// эндпоинтом передачи файла, а имя/расширение файла находятся в query-параметрах
    /// <c>path</c>/<c>file</c>/<c>filename</c>. Паттерн повторяет
    /// <c>OneCUpdatesService.FileEndpointUrlRegex</c> (issue #352): поиск ведётся
    /// в любом месте ответа, включая JS-редиректы и встроенный JSON. Эндпоинт в
    /// единственном числе (<c>version_file</c>) не путается со страницей
    /// <c>version_files?…</c> (во множественном — после «files» идёт «s», а не «?»).</summary>
    private static readonly Regex FileEndpointUrlRegex = new(
        @"(?<url>(?:https?://[^\s""'<>]*)?(?:transfer_file|additional_file|version_file)\?[^\s""'<>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Регулярное выражение для извлечения размера файла из JSON-полей
    /// <c>size</c>/<c>filesize</c>.</summary>
    private static readonly Regex SizeFieldRegex = new(
        @"""(?:size|filesize)""\s*:\s*(?<bytes>\d{1,15})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Регулярное выражение жирного заголовка группы на странице
    /// <c>version_files</c> (issue #330 п.2/#334 п.2): <c><b>…</b></c>/
    /// <c><strong>…</strong></c> — «Технологическая платформа», «Тонкий
    /// клиент 1С:Предприятия» и т.п. Захват ограничен 120 символами текста, чтобы
    /// не схлопнуть в «заголовок» большой кусок разметки.</summary>
    private static readonly Regex GroupHeaderRegex = new(
        @"<(?:b|strong)(?:\s[^>]*)?>(?<text>(?:(?!</(?:b|strong)>)[\s\S]){1,120}?)</(?:b|strong)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Регулярное выражение заголовка группы в семантических тегах (issue #167):
    /// <c><h1>…</h1></c>…<c><h4></c>, <c><summary></c>,
    /// <c><caption></c>, <c><legend></c>. Страница <c>version_files</c>
    /// размечает разделы («Windows (64-bit)», «Linux (64-bit)» и т.п.) не только
    /// жирным текстом, поэтому прежний парсер не находил ни одного раздела и строка
    /// выбора раздела в окне скачивания не появлялась.
    /// </summary>
    private static readonly Regex TagGroupHeaderRegex = new(
        @"<(?<tag>h[1-4]|summary|caption|legend)\b[^>]*>(?<text>[\s\S]{1,200}?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Регулярное выражение заголовка группы по CSS-классу (issue #167): элементы
    /// с классом, содержащим <c>group</c>/<c>section</c>/<c>header</c>
    /// (например, <c><div class="group">Windows (64-bit)</div></c>).
    /// Слишком длинный текст такого контейнера отбрасывается нормализацией.
    /// </summary>
    private static readonly Regex ClassGroupHeaderRegex = new(
        @"<(?<tag>[a-z][a-z0-9]*)\b[^>]*\bclass\s*=\s*[""'][^""']*(?:group|section|header)[^""']*[""'][^>]*>(?<text>[\s\S]{1,200}?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Известные названия разделов страницы загрузок (issue #167): «Windows (64-bit)»,
    /// «Windows (32-bit)», «Linux (64-bit)», «Linux (32-bit)» (в т.ч. «64-бит»).
    /// Используются как запасной путь, когда семантических заголовков на странице нет —
    /// разделы могут быть просто текстом внутри ячеек таблицы.
    /// </summary>
    private static readonly Regex KnownSectionNameRegex = new(
        @"(?<text>(?:Windows|Linux)\s*\(\s*(?:32|64)\s*(?:-\s*bit|бит[а]?)\s*\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Регулярное выражение расширения дистрибутива (текст заголовка,
    /// содержащий имя файла/ссылку на дистрибутив, группой не является).</summary>
    private static readonly Regex DistributionExtensionRegex = new(
        @"\.(?:zip|rar|7z|exe|arj|deb|rpm|tar\.gz)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Регулярное выражение HTML-тегов (для снятия разметки строки таблицы при
    /// извлечении колонки «Список версий», issue #352).</summary>
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Регулярное выражение числового токена версии («3.0.167.18», «8.3.27.2214») —
    /// поиск версий в тексте ячейки «Список версий» (issue #352).</summary>
    private static readonly Regex VersionTokenRegex = new(
        @"\b(?<v>\d{1,4}(\.\d{1,4}){1,3})\b", RegexOptions.Compiled);

    /// <summary>Регулярное выражение ячейки <c><td>…</td></c> строки таблицы
    /// (для пропуска первой ячейки с версией при извлечении «Списка версий», issue #352).</summary>
    private static readonly Regex TableCellRegex = new(
        @"<td[^>]*>(?<cell>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Маркер тестовой (предрелизной) сборки в данных каталога (issue #364): пометка
    /// в строке таблицы версий/в тексте версии — «тестовая версия», «тест», «test»,
    /// «beta», «preview», «alpha», «RC1», «предрелизная сборка», «pre-release».
    /// Ищется ТОЛЬКО в тексте (без разметки), чтобы не срабатывать на атрибуты
    /// <c>class</c>/<c>href</c>. Слова ограничены границами слов: «latest» не совпадает
    /// с «test», «src» — с «rc».
    /// </summary>
    private static readonly Regex TestMarkerRegex = new(
        @"\bтест[а-яё]*\b|\btest\b|\bbeta\b|\bpreview\b|\balpha\b|\brc\d*\b|предрелиз\w*|pre-?release",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Регулярное выражение даты «дд.мм.гггг»: токены вида «01.10.2026» из соседних
    /// колонок (например, «Дата выхода») не считаются версиями «Списка версий» (issue #352).</summary>
    private static readonly Regex DateLikeTokenRegex = new(
        @"^\d{1,2}\.\d{1,2}\.(?:19|20)\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Возвращает все версии со страницы каталога <c>releases.1c.ru/project/Platform83</c>:
    /// перебирает все строки таблицы <c>id="versionsTable"</c> и извлекает из каждой первый
    /// элемент <c><a href="/version_files?nick=…&ver=…">ВЕРСИЯ</a></c>. Если
    /// таблица не найдена — ищет все ссылки <c>version_files?...&ver=</c> по всему HTML
    /// (запасной путь). Версии нормализуются (HTML-сущности, пробелы/переносы), дубликаты
    /// удаляются, результат сортируется по убыванию числовыми сегментами.
    /// Пустой/битый HTML возвращает пустой список без исключений.
    /// </summary>
    public static IReadOnlyList<PlatformRelease> ParseVersions(string html)
        => ParseVersions(html, null);

    /// <summary>
    /// Вариант <see cref="ParseVersions(string)"/> с указанием ника каталога
    /// (<c>Platform83</c>/<c>Platform85</c>, issue #334): ник сохраняется в
    /// <see cref="PlatformRelease.Nick"/> и используется при построении адреса
    /// страницы файлов релиза, если у релиза нет ссылки <c>version_files</c>.
    /// </summary>
    public static IReadOnlyList<PlatformRelease> ParseVersions(string html, string? nick)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Array.Empty<PlatformRelease>();

        // Тройка «версия → ссылка на страницу файлов → список версий для обновления»
        // (колонка «Список версий», issue #352) + признак тестовой сборки (issue #364);
        // сохраняем порядок появления в HTML.
        var entries = new List<(string Version, string Href, List<string> Sources, bool IsTest)>();

        // 1) Основной путь: таблица #versionsTable, все строки <tr>, первый version_files-линк.
        var tableMatch = VersionsTableRegex.Match(html);
        if (tableMatch.Success)
        {
            var table = tableMatch.Groups["table"].Value;
            foreach (Match rowMatch in TableRowRegex.Matches(table))
            {
                var row = rowMatch.Groups["row"].Value;
                var link = VersionFilesLinkRegex.Match(row);
                if (!link.Success)
                    continue;

                // issue #364: тестовая (предрелизная) сборка — по маркеру в тексте строки
                // («тестовая версия» в ячейке/бейдже) либо в тексте самой версии
                // («8.3.27.1688 (тестовая)»).
                var rawVersion = link.Groups["ver"].Value;
                var isTest = IsTestMarker(rawVersion) || IsTestMarker(ExtractText(row));
                var version = isTest ? ExtractLeadingVersion(rawVersion) : NormalizeVersion(rawVersion);
                if (!IsValidVersionText(version))
                    continue;

                // Колонка «Список версий»: остальной текст строки (версии, из которых
                // можно обновиться напрямую до этой версии).
                var sources = ExtractSources(row, version);
                entries.Add((version, NormalizeHref(link.Groups["href"].Value), sources, isTest));
            }
        }

        // 2) Запасной путь (урезанные каталоги и карточный дизайн Розницы/УПП, issue #371):
        //    единый сборщик кандидатов — ссылки version_files/version_file по всему HTML
        //    (версия из текста или ver= query), атрибуты data-ver/data-version, версионные
        //    токены текста с ужесточённым фильтром (см. OneCUpdatesService.CollectVersionCandidatesFromHtml).
        if (entries.Count == 0)
        {
            foreach (var candidate in OneCUpdatesService.CollectVersionCandidatesFromHtml(html))
            {
                var isTest = IsTestMarker(candidate.Version);
                var version = isTest ? ExtractLeadingVersion(candidate.Version) : NormalizeVersion(candidate.Version);
                if (!IsValidVersionText(version))
                    continue;
                entries.Add((version, NormalizeHref(candidate.VersionFilesUrl), new List<string>(), isTest));
            }
        }

        // 3) Тестовые сборки в отдельной секции страницы (issue #364): ссылки
        // version_files ВНЕ #versionsTable, рядом с которыми в тексте есть маркер
        // («Тестовые версии», «тестовая версия»). Берутся ТОЛЬКО помеченные — новые
        // непомеченные версии сюда не попадают, поведение без тестовых секций не меняется.
        if (tableMatch.Success)
        {
            foreach (Match link in VersionFilesLinkRegex.Matches(html))
            {
                if (link.Index >= tableMatch.Index &&
                    link.Index < tableMatch.Index + tableMatch.Length)
                    continue;

                var rawVersion = link.Groups["ver"].Value;
                var version = NormalizeVersion(rawVersion);
                if (!IsValidVersionText(version))
                    continue;
                if (!IsTestMarker(rawVersion) &&
                    !IsTestMarkerNear(html, link.Index, link.Length))
                    continue;

                entries.Add((version, NormalizeHref(link.Groups["href"].Value), new List<string>(), true));
            }
        }

        // Дедупликация по версии: сохраняем первую встреченную ссылку и список версий.
        var byVersion = new Dictionary<string, (string Href, List<string> Sources, bool IsTest)>(StringComparer.Ordinal);
        foreach (var (version, href, sources, isTest) in entries)
        {
            if (!byVersion.ContainsKey(version))
                byVersion[version] = (href, sources, isTest);
        }

        var releases = byVersion
            .Select(kv => new PlatformRelease
            {
                Version = kv.Key,
                VersionFilesUrl = kv.Value.Href,
                Sources = kv.Value.Sources,
                IsTest = kv.Value.IsTest,
                Nick = nick ?? string.Empty,
            })
            .ToList();
        releases.Sort((x, y) => CompareVersions(y.Version, x.Version));
        return releases;
    }

    /// <summary>
    /// True, если в тексте есть маркер тестовой (предрелизной) сборки (issue #364):
    /// «тестовая версия», «test», «beta», «preview», «RC1», «предрелизная сборка» и т.п.
    /// Разметка не снимается автоматически — передавайте уже очищенный текст
    /// (или исходную строку: маркеры ищутся в тексте, атрибуты <c>class</c>/<c>href</c>
    /// содержат слово «тест»/«test» только в исключительных случаях).
    /// null/пусто — false.
    /// </summary>
    public static bool IsTestMarker(string? text)
        => !string.IsNullOrWhiteSpace(text) && TestMarkerRegex.IsMatch(text);

    /// <summary>Снимает HTML-разметку и декодирует сущности (текст строки таблицы
    /// для поиска маркера тестовой сборки, issue #364).</summary>
    private static string ExtractText(string html)
        => WebUtility.HtmlDecode(HtmlTagRegex.Replace(html ?? string.Empty, " "));

    /// <summary>
    /// True, если маркер тестовой сборки есть в текстовой окрестности позиции
    /// <paramref name="index"/> ответа (±500 символов до/после ссылку, без учёта
    /// разметки) — секции «Тестовые версии» живут вне таблицы #versionsTable.
    /// </summary>
    private static bool IsTestMarkerNear(string html, int index, int length)
    {
        if (string.IsNullOrEmpty(html))
            return false;

        const int window = 500;
        var start = Math.Max(0, index - window);
        var end = Math.Min(html.Length, index + length + window);
        return IsTestMarker(ExtractText(html.Substring(start, end - start)));
    }

    /// <summary>
    /// Извлекает первый числовой токен версии из текста ссылки («8.3.27.1688 (тестовая)»
    /// → «8.3.27.1688», issue #364); если токена нет — нормализованная строка целиком.
    /// </summary>
    private static string ExtractLeadingVersion(string raw)
    {
        var decoded = WebUtility.HtmlDecode(raw ?? string.Empty);
        var match = VersionTokenRegex.Match(decoded ?? string.Empty);
        var token = match.Success ? match.Groups["v"].Value : null;
        return !string.IsNullOrEmpty(token) ? NormalizeVersion(token) : NormalizeVersion(decoded ?? string.Empty);
    }

    /// <summary>
    /// Извлекает «Список версий» из строки таблицы #versionsTable (issue #352): перебирает
    /// ячейки строки ПОСЛЕ первой (первая содержит ссылку version_files с самой версией),
    /// снимает HTML-разметку и ищет числовые токены версий вида «3.0.167.18». Даты
    /// («01.10.2026») из соседних колонок не считаются версиями. Устойчиво к форматам
    /// ячейки: список через запятую, диапазон через «—», произвольный текст вокруг.
    /// Пустая/отсутствующая колонка даёт пустой список без исключений.
    /// </summary>
    private static List<string> ExtractSources(string row, string ownVersion)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(row))
            return result;

        var cells = TableCellRegex.Matches(row);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < cells.Count; i++)
        {
            var cellText = HtmlTagRegex.Replace(cells[i].Groups["cell"].Value, " ");
            foreach (Match match in VersionTokenRegex.Matches(cellText))
            {
                var candidate = NormalizeVersion(match.Groups["v"].Value);
                if (!IsValidVersionText(candidate))
                    continue;
                // Даты («01.10.2026») версиями не являются.
                if (DateLikeTokenRegex.IsMatch(candidate))
                    continue;
                if (!string.IsNullOrWhiteSpace(ownVersion) &&
                    string.Equals(candidate, ownVersion, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add(candidate))
                    continue;
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// Возвращает файлы дистрибутива релиза из ответа <c>version_files?nick=…&ver=…</c>
    /// (JSON или HTML): ищет ссылки на <c>.zip</c>/<c>.rar</c>/<c>.7z</c>/<c>.exe</c>/
    /// <c>.arj</c>/<c>.deb</c>/<c>.rpm</c>/<c>.tar.gz</c> (набор синхронизирован с
    /// <c>OneCUpdatesService</c>, issue #330 — дистрибутивы платформы 8.3/8.5 отдаются
    /// в том числе архивами <c>.rar</c>/<c>.7z</c>), классифицирует тип по расширению,
    /// разрядность — по токенам имени файла, размер — из JSON-полей <c>size</c>/
    /// <c>filesize</c> (иначе 0). Ссылки с query-частью учитываются. Неизвестные
    /// расширения пропускаются; битый ответ возвращает пустой список.
    /// </summary>
    public static IReadOnlyList<PlatformReleaseFile> ParseDistributionFiles(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<PlatformReleaseFile>();

        var result = new List<PlatformReleaseFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Заголовки групп страницы (issue #330 п.2/#334 п.2): каждому файлу присваивается
        // заголовок, последний перед его позицией в ответе. На страницах без групп
        // (старые релизы, JSON) у всех файлов Group = null — фолбэк «Файлы релиза».
        var groupHeaders = ExtractGroupHeaders(body);

        foreach (Match match in DistributionFileLinkRegex.Matches(body))
        {
            // HTML-сущности в href декодируются (issue #330): иначе адрес с «&»
            // уходит на портал с параметром «amp;…» и файл не скачивается.
            var url = NormalizeUrl(match.Groups["url"].Value);
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var fileName = ExtractFileNameFromUrl(url);
            // Расширение дистрибутива в ИМЕНИ файла обязательно (issue #330): адрес вида
            // «/transfer_file?…path=Distr%2Ffile.deb» без этого guard давал бы мусорную
            // запись «transfer_file» — расширение в нём находится в query, а не в пути.
            if (string.IsNullOrWhiteSpace(fileName) ||
                ClassifyKind(fileName) == PlatformDistributionKind.Other)
                continue;
            if (!seen.Add(url))
                continue;

            result.Add(new PlatformReleaseFile
            {
                FileName = fileName,
                Url = url,
                SizeBytes = FindSizeNear(body, fileName),
                Architecture = DetectArchitecture(fileName),
                Kind = ClassifyKind(fileName),
                Group = AssignGroup(groupHeaders, match.Index),
            });
        }

        // Альтернативная разметка (issue #330, комментарий 7OH от 2026-10-09): страница
        // version_files может перечислять дистрибутивы ссылками на эндпоинты передачи
        // файла (transfer_file/additional_file) — без прямого адреса архива в href.
        // Имя/расширение файла берём из query-параметров path/file/filename.
        foreach (Match match in FileEndpointUrlRegex.Matches(body))
        {
            var url = NormalizeUrl(match.Groups["url"].Value);
            if (string.IsNullOrWhiteSpace(url))
                continue;
            if (!seen.Add(url))
                continue;

            var fileName = ExtractFileEndpointFileName(url);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;
            if (ClassifyKind(fileName) == PlatformDistributionKind.Other)
                continue;

            result.Add(new PlatformReleaseFile
            {
                FileName = fileName,
                Url = url,
                SizeBytes = FindSizeNear(body, fileName),
                Architecture = DetectArchitecture(fileName),
                Kind = ClassifyKind(fileName),
                Group = AssignGroup(groupHeaders, match.Index),
            });
        }

        // Issue #167: если заголовков на странице нет, но в релизе есть файлы разных
        // семейств ОС (Windows и Linux), разделы синтезируются по типу/разрядности —
        // «Windows (64-bit)», «Linux (64-bit)» и т.п. Одно семейство разделов не даёт.
        ApplyFamilyFallback(result);

        return result;
    }

    /// <summary>
    /// Извлекает заголовки групп из HTML-ответа страницы <c>version_files</c>
    /// (issue #330 п.2/#334 п.2, расширено в issue #167): жирные заголовки
    /// (<c><b></c>/<c><strong></c>), семантические (<c><h1>…<h4></c>,
    /// <c><summary></c>, <c><caption></c>, <c><legend></c>) и элементы
    /// с CSS-классом, содержащим <c>group</c>/<c>section</c>/<c>header</c>. Если
    /// семантических заголовков нет, разделы ищутся по известным названиям
    /// («Windows (64-bit)», «Linux (64-bit)» и т.п.) в тексте страницы.
    /// Мусор отфильтровывается: ссылки, имена файлов дистрибутивов, чисто
    /// числовые/версионные строки, слишком короткий/длинный текст.
    /// Возвращает список «позиция в ответе → текст заголовка» в порядке появления.
    /// </summary>
    private static List<(int Index, string Text)> ExtractGroupHeaders(string body)
    {
        var headers = new List<(int Index, string Text)>();
        if (string.IsNullOrWhiteSpace(body) || body.IndexOf('<') < 0)
            return headers;

        AddHeaders(headers, GroupHeaderRegex.Matches(body), body);
        AddHeaders(headers, TagGroupHeaderRegex.Matches(body), body);
        AddHeaders(headers, ClassGroupHeaderRegex.Matches(body), body);

        // Запасной путь: разделы размечены простым текстом (например, в ячейках
        // таблицы) — используем известные названия разделов страницы загрузок.
        if (headers.Count == 0)
        {
            foreach (Match match in KnownSectionNameRegex.Matches(body))
            {
                var text = NormalizeGroupHeaderText(match.Groups["text"].Value);
                if (!string.IsNullOrWhiteSpace(text))
                    headers.Add((match.Index, text));
            }
        }

        headers.Sort((a, b) => a.Index.CompareTo(b.Index));
        return headers;
    }

    /// <summary>Добавляет в список заголовки, найденные регулярным выражением
    /// <paramref name="matches"/>: пропускает ссылки («<b><a …>Скачать</a></b>»),
    /// нормализует текст и отбрасывает мусор.</summary>
    private static void AddHeaders(
        List<(int Index, string Text)> headers, MatchCollection matches, string body)
    {
        foreach (Match match in matches)
        {
            // Ссылки внутри заголовка («<b><a href=…>Скачать</a></b>») группой не являются.
            if (Regex.IsMatch(match.Groups["text"].Value, @"<a\s", RegexOptions.IgnoreCase))
                continue;
            var text = NormalizeGroupHeaderText(match.Groups["text"].Value);
            if (!string.IsNullOrWhiteSpace(text))
                headers.Add((match.Index, text));
        }
    }

    /// <summary>Нормализует текст заголовка группы (снятие вложенной разметки,
    /// декодирование сущностей, схлопывание пробелов) или null, если текст не похож
    /// на заголовок группы.</summary>
    private static string? NormalizeGroupHeaderText(string raw)
    {
        var text = WebUtility.HtmlDecode(HtmlTagRegex.Replace(raw ?? string.Empty, " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length < 2 || text.Length > 120)
            return null;
        if (text.Contains("://", StringComparison.Ordinal) ||
            text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return null;
        if (DistributionExtensionRegex.IsMatch(text))
            return null;
        // Чисто числовой/версионный текст («8.3.27») группой не является.
        if (Regex.IsMatch(text, @"^[\d.,\s\-–—%()]+$"))
            return null;
        // Полный номер версии в заголовке («Файлы версии 8.5.1.1522») — это заголовок
        // страницы/релиза, а не раздел списка файлов.
        if (Regex.IsMatch(text, @"\b\d{1,4}\.\d{1,4}\.\d{1,4}\b"))
            return null;
        return text;
    }

    /// <summary>
    /// Фолбэк разделов (issue #167): если на странице не найдено ни одного заголовка
    /// группы, но файлы относятся к разным семействам ОС (Windows и Linux),
    /// разделы синтезируются по семейству и разрядности — «Windows (64-bit)»,
    /// «Windows (32-bit)», «Linux (64-bit)», «Linux (32-bit)». Если семейство одно
    /// (например, только Windows) — разделы не создаются, строка выбора скрыта.
    /// Файлы без определённой разрядности остаются без группы.
    /// </summary>
    private static void ApplyFamilyFallback(List<PlatformReleaseFile> files)
    {
        if (files is null || files.Count == 0)
            return;
        // Заголовки уже назначены хотя бы одному файлу — фолбэк не нужен.
        if (files.Any(f => !string.IsNullOrWhiteSpace(f.Group)))
            return;

        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var family = DistributionFamily(file);
            if (family is not null)
                families.Add(family);
        }

        // Одно семейство ОС — разделы не несут информации (разрядность фильтруется отдельно).
        if (families.Count < 2)
            return;

        foreach (var file in files)
        {
            var family = DistributionFamily(file);
            if (family is null)
                continue;
            var bits = string.Equals(file.Architecture, PlatformDistributionPicker.Arch32,
                StringComparison.OrdinalIgnoreCase)
                ? "32-bit"
                : string.Equals(file.Architecture, PlatformDistributionPicker.Arch64,
                    StringComparison.OrdinalIgnoreCase)
                    ? "64-bit"
                    : null;
            if (bits is null)
                continue;
            file.Group = $"{family} ({bits})";
        }
    }

    /// <summary>Семейство ОС файла дистрибутива: «Windows»/«Linux» или null.</summary>
    private static string? DistributionFamily(PlatformReleaseFile file)
        => file?.Kind switch
        {
            PlatformDistributionKind.WindowsSetupZip => "Windows",
            PlatformDistributionKind.LinuxDeb
                or PlatformDistributionKind.LinuxRpm
                or PlatformDistributionKind.LinuxTarGz => "Linux",
            _ => null,
        };

    /// <summary>Возвращает заголовок группы, действующий на позиции <paramref name="position"/>
    /// в ответе (последний заголовок до этой позиции), или null — файлы до первого
    /// заголовка и страницы без групп получают фолбэк-группу на слое отображения.</summary>
    private static string? AssignGroup(List<(int Index, string Text)> headers, int position)
    {
        string? group = null;
        foreach (var (index, text) in headers)
        {
            if (index <= position)
                group = text;
            else
                break;
        }

        return group;
    }

    /// <summary>
    /// Нормализует ссылку на страницу файлов релиза: декодирование HTML-сущностей
    /// (реальный HTML портала содержит <c>&amp;</c> в атрибутах href) и удаление
    /// пробельных символов. БЕЗ декодирования адрес <c>/version_files?nick=…&amp;ver=…</c>
    /// уходит на портал с параметром «amp;ver», сервер не находит версию и отвечает
    /// страницей без дистрибутивов — список «Выбор файла» оставался пустым
    /// (issue #330, комментарий 7OH от 2026-10-08).
    /// </summary>
    private static string NormalizeHref(string? raw)
    {
        var decoded = WebUtility.HtmlDecode(raw ?? string.Empty);
        var sb = new StringBuilder(decoded.Length);
        foreach (var c in decoded)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Численное сравнение версий («8.3.27.2214» vs «8.3.27.1688»): >0, если
    /// <paramref name="a"/> новее <paramref name="b"/>. Переиспользует
    /// <see cref="OneCUpdatesService.TryParseVersion"/>; если хотя бы одна сторона не
    /// разбирается как версия — сравнивает числовые сегменты (паттерн
    /// <c>OneCPlatformLocator.CompareVersions</c>).
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        if (OneCUpdatesService.TryParseVersion(a, out var parsedA) &&
            OneCUpdatesService.TryParseVersion(b, out var parsedB))
            return parsedA.CompareTo(parsedB);

        return CompareBySegments(a, b);
    }

    /// <summary>Нормализует текст версии из ссылки: HTML-сущности, затем удаление всех
    /// пробельных символов (переносы разметки), trim.</summary>
    private static string NormalizeVersion(string raw)
    {
        var decoded = WebUtility.HtmlDecode(raw ?? string.Empty);
        if (string.IsNullOrEmpty(decoded))
            return string.Empty;

        var sb = new StringBuilder(decoded.Length);
        foreach (var c in decoded)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>True, если строка похожа на номер версии («8.3.27.2214»): непустая,
    /// содержит точку и хотя бы одну цифру.</summary>
    private static bool IsValidVersionText(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;
        return version.IndexOf('.') >= 0 && version.Any(char.IsDigit);
    }

    /// <summary>Нормализует URL из HTML/JSON ответа: декодирование HTML-сущностей
    /// («&amp;» → «&», issue #330 — иначе запрос уходит с параметром «amp;…»),
    /// удаление кавычек, экранирований и внешних пробелов.</summary>
    private static string NormalizeUrl(string raw)
    {
        var decoded = WebUtility.HtmlDecode(raw ?? string.Empty);
        return decoded.Trim().Trim('"', '\'', '\\');
    }

    /// <summary>Извлекает имя файла из адреса эндпоинта передачи файла
    /// (<c>transfer_file?…</c>/<c>additional_file?…</c>, issue #330): имя ищется в
    /// query-параметрах <c>path</c>/<c>file</c>/<c>filename</c> (последний сегмент
    /// после <c>/</c> или <c>\</c>), с декодированием процент-экранирования и
    /// HTML-сущностей. Возвращает null, если параметры не содержат имени файла.</summary>
    private static string? ExtractFileEndpointFileName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var question = url.IndexOf('?');
        if (question < 0)
            return null;

        var query = url[(question + 1)..];
        var hash = query.IndexOf('#');
        if (hash >= 0)
            query = query[..hash];

        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = pair[..eq].ToLowerInvariant();
            if (name is not ("path" or "file" or "filename"))
                continue;

            var value = WebUtility.HtmlDecode(pair[(eq + 1)..]);
            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch
            {
                // Оставляем значение как есть при некорректном экранировании.
            }

            var slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            var fileName = slash >= 0 ? value[(slash + 1)..] : value;
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }

        return null;
    }

    /// <summary>Извлекает имя файла из ссылки: последний сегмент пути до <c>?</c>/<c>#</c>,
    /// с декодированием процент-экранирования и HTML-сущностей.</summary>
    private static string ExtractFileNameFromUrl(string url)
    {
        var query = url.IndexOfAny(new[] { '?', '#' });
        var path = query >= 0 ? url.Substring(0, query) : url;
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path.Substring(slash + 1) : path;

        try
        {
            name = Uri.UnescapeDataString(name);
        }
        catch
        {
            // Оставляем имя как есть при некорректном экранировании.
        }

        return WebUtility.HtmlDecode(name);
    }

    /// <summary>Тип дистрибутива по расширению имени файла. Windows-архивы
    /// (<c>.zip</c>/<c>.rar</c>/<c>.7z</c>/<c>.arj</c>) и одиночные <c>.exe</c> считаются
    /// установочными дистрибутивами Windows (issue #330: платформа 8.3/8.5 отдаётся
    /// в том числе архивами <c>.rar</c>/<c>.7z</c>, ранее они отбрасывались и страница
    /// files выглядела «пустой»).</summary>
    private static PlatformDistributionKind ClassifyKind(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".zip") || lower.EndsWith(".rar") ||
            lower.EndsWith(".7z") || lower.EndsWith(".arj") || lower.EndsWith(".exe"))
            return PlatformDistributionKind.WindowsSetupZip;
        if (lower.EndsWith(".deb"))
            return PlatformDistributionKind.LinuxDeb;
        if (lower.EndsWith(".rpm"))
            return PlatformDistributionKind.LinuxRpm;
        if (lower.EndsWith(".tar.gz"))
            return PlatformDistributionKind.LinuxTarGz;
        return PlatformDistributionKind.Other;
    }

    /// <summary>Разрядность дистрибутива по токенам имени файла
    /// (<c>x64</c>/<c>x86_64</c>/<c>amd64</c>/<c>64</c> → «x64»;
    /// <c>x86</c>/<c>32</c>/<c>i386</c> → «x86») или null, если не определена.</summary>
    private static string? DetectArchitecture(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("x86_64") || lower.Contains("amd64") || lower.Contains("x64"))
            return "x64";
        if (lower.Contains("i386") || lower.Contains("i686") || lower.Contains("x86"))
            return "x86";
        if (lower.Contains("64"))
            return "x64";
        if (lower.Contains("32"))
            return "x86";
        return null;
    }

    /// <summary>Ищет размер файла в JSON-полях <c>size</c>/<c>filesize</c> в окрестности
    /// первого упоминания имени файла в теле ответа. Устойчиво к неизвестной схеме JSON.
    /// Возвращает 0, если размер не найден.</summary>
    private static long FindSizeNear(string body, string fileName)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(fileName))
            return 0;

        foreach (Match occurrence in Regex.Matches(body, Regex.Escape(fileName), RegexOptions.IgnoreCase))
        {
            // Ищем размер только ПОСЛЕ вхождения имени файла и до конца JSON-объекта (ближайший «}»),
            // чтобы не захватить size соседнего файла в списке.
            var start = occurrence.Index + occurrence.Length;
            var end = Math.Min(body.Length, start + 500);
            var window = body.Substring(start, end - start);
            var closeBrace = window.IndexOf('}');
            if (closeBrace >= 0)
                window = window.Substring(0, closeBrace);

            var sizeMatch = SizeFieldRegex.Match(window);
            if (sizeMatch.Success && long.TryParse(sizeMatch.Groups["bytes"].Value, out var bytes))
                return bytes;
        }

        return 0;
    }

    /// <summary>Численное сравнение по сегментам (паттерн
    /// <c>OneCPlatformLocator.CompareVersions</c>): нечисловые сегменты игнорируются,
    /// недостающие части считаются нулями.</summary>
    private static int CompareBySegments(string a, string b)
    {
        static int[] Parts(string v)
        {
            return (v ?? string.Empty)
                .Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => int.TryParse(p, out _))
                .Select(int.Parse)
                .ToArray();
        }

        var pa = Parts(a);
        var pb = Parts(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }

        return 0;
    }
}