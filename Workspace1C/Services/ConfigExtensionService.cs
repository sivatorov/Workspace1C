using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Операции с расширениями конфигурации (.cfe) через пакетный режим конфигуратора
/// (выпуск 1.0.0.69): список расширений базы, загрузка/обновление расширения
/// (<c>/LoadCfg -Extension</c> + <c>/UpdateDBCfg -Extension</c>), выгрузка в файл
/// (<c>/DumpCfg -Extension</c>) и удаление (<c>/DeleteCfg -Extension</c>).
/// Построение ключей — чистые функции (покрыты тестами), запуск —
/// <see cref="DesignerCommandRunner"/>.
/// </summary>
public static class ConfigExtensionService
{
    /// <summary>
    /// Ключи загрузки расширения из файла и применения его к базе данных.
    /// Issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»):
    /// у <c>/UpdateDBCfg</c> ключа <c>-Extension</c> НЕТ (он определен для
    /// <c>/LoadCfg</c>/<c>/DumpCfg</c>) — повторный <c>-Extension</c> у операции
    /// применения даёт «Error in command-line options» при запуске конфигуратора
    /// на клиент-серверной базе. Расширение, загруженное через
    /// <c>/LoadCfg "файл.cfe" -Extension "имя"</c>, применяется к базе данных
    /// последующим <c>/UpdateDBCfg</c> (без повторения ключа).
    /// </summary>
    public static string? BuildInstallArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        var name = extensionName.Trim();
        return $"/LoadCfg\"{cfePath}\" -Extension \"{name}\" /UpdateDBCfg";
    }

    /// <summary>Ключи выгрузки расширения в файл .cfe.</summary>
    public static string? BuildDumpArguments(string cfePath, string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(cfePath) || !DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DumpCfg\"{cfePath}\" -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи удаления расширения из базы.</summary>
    public static string? BuildDeleteArguments(string extensionName)
    {
        if (!DesignerCommandRunner.IsSafeValue(extensionName))
            return null;
        return $"/DeleteCfg -Extension \"{extensionName.Trim()}\"";
    }

    /// <summary>Ключи вывода списка расширений базы данных в файл /Out.</summary>
    public const string ListArguments = "/DumpDBCfgList -AllExtensions";

    /// <summary>
    /// Имя расширения по умолчанию из имени файла: «Доработки_1.2.cfe» → «Доработки_1.2».
    /// </summary>
    public static string SuggestName(string? cfePath)
    {
        if (string.IsNullOrWhiteSpace(cfePath))
            return string.Empty;
        return Path.GetFileNameWithoutExtension(cfePath.Trim());
    }

    /// <summary>
    /// Автоподстановка имени по файлу: сначала НАСТОЯЩЕЕ имя расширения из .cfe
    /// (issue #117), затем, если прочитать его не удалось, имя файла без расширения.
    /// </summary>
    public static string ResolveName(string? cfePath) =>
        TryReadExtensionName(cfePath) ?? SuggestName(cfePath);

    /// <summary>
    /// Issue #117 (защита от дубля): имя, под которым выполняется установка расширения.
    /// По умолчанию — НАСТОЯЩЕЕ имя расширения из .cfe (тогда <c>/LoadCfg -Extension</c>
    /// обновит уже установленное расширение вместо создания копии под именем файла).
    /// Если пользователь ввёл имя вручную — остаётся введённое имя (отдельное расширение,
    /// в журнале выводится предупреждение). Если настоящее имя прочитать не удалось —
    /// введённое/автоподставленное имя файла. Чистая функция — для юнит-тестов.
    /// </summary>
    public static string ResolveInstallName(string? cfeRealName, string enteredName, bool nameEdited)
    {
        if (string.IsNullOrWhiteSpace(cfeRealName) || nameEdited ||
            NameEquals(cfeRealName, enteredName))
            return enteredName;
        return cfeRealName;
    }

    /// <summary>
    /// Сравнение имён расширений: без учёта регистра и с обрезкой пробелов
    /// (issue #117 — имена из .cfe и из списка базы могут отличаться пробелами).
    /// </summary>
    public static bool NameEquals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Issue #117 (защита от дубля): находит установленное расширение, совпадающее
    /// с именем установки по имени (OrdinalIgnoreCase, с обрезкой пробелов).
    /// Возвращает имя из списка базы или <c>null</c>. Чистая функция — для тестов.
    /// </summary>
    public static string? FindInstalledMatch(string? installName, IEnumerable<string>? installedNames)
    {
        if (string.IsNullOrWhiteSpace(installName) || installedNames is null)
            return null;
        foreach (var installed in installedNames)
        {
            if (NameEquals(installed, installName))
                return installed.Trim();
        }
        return null;
    }

    /// <summary>
    /// Issue #117 (требование 7OH): нужно ли спросить пользователя перед установкой.
    /// Истина, когда настоящее имя из .cfe прочитать НЕ удалось, а в базе уже есть
    /// расширения с другими именами — иначе установка молча создаст дубль под именем
    /// файла, и платформа завершится ошибкой «такое расширение уже есть».
    /// Если имя из файла прочитано или совпадает с установленным — подтверждение
    /// не требуется. Чистая функция — для юнит-тестов и окна расширений.
    /// </summary>
    public static bool RequiresUnknownNameConfirmation(string? cfeRealName, string? installName,
        IEnumerable<string>? installedNames)
    {
        if (!string.IsNullOrWhiteSpace(cfeRealName))
            return false;
        if (installedNames is null)
            return false;
        var names = installedNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0)
            return false;
        // Имя файла/ручное имя совпало с установленным — это обновление, не дубль.
        return FindInstalledMatch(installName, names) is null;
    }

    // ---------------------------------------------------------------------------
    // Чтение настоящего имени расширения из .cfe (issue #117).
    //
    // Файл .cfe — НЕ zip-архив, а нативный контейнер 1С (как .cf): страница-заголовок
    // с сигнатурой 0x7FFFFFFF, дальше блоки с ASCII-заголовками вида «0d0a + hex-размеры».
    // Элементы контейнера (таблица адресов, заголовки и данные элементов) сжаты deflate;
    // корневой элемент метаданных содержит текстовое дерево, в котором после GUID-класса
    // «Конфигурация» (9cd510cd-abfc-11d4-9434-004095e12fc7) идёт первое строковое
    // свойство — имя расширения: «{1,0,<guid>},"НастоящееИмя"». Формат проверен на
    // реальных .cfe (платформа 8.3.x); служебный индекс контейнера называется
    // «configinfo» и содержит только список GUID-файлов с их хэшами.
    //
    // Issue #117 (расширение «ИР», реальный файл ir-8.23.1e.cfe): контейнер содержит
    // тысячи служебных элементов — объекты метаданных, формы, модули, картинки, — и
    // почти в каждом есть своя строка «{1,0,<guid>},"Имя"». Прежний разбор брал
    // ПЕРВУЮ такую строку и показывал имя служебного объекта вместо имени расширения.
    // Поэтому разбор:
    //  * читает файл потоково (FileStream + блочное чтение), а не File.ReadAllBytes;
    //  * обходит вложенные контейнеры до MaxContainerDepth и НЕ ищет имя в их
    //    двоичных данных (только в элементах);
    //  * выбирает имя по приоритету якорей: GUID-класс «Конфигурация» → маркер
    //    ConfigurationExtensionPurpose → именованное свойство (Name/Синоним) →
    //    общий шаблон только как «последний шанс», если профильных элементов нет;
    //  * определяет кодировку (UTF-8/UTF-16) ПО БЛОКУ, а не по первым 4 КБ файла;
    //  * при неудаче возвращает null — вызывающий код откатывается к имени файла,
    //    но установка без подтверждения не выполняется (см. ExtensionsViewModel).
    // ---------------------------------------------------------------------------

    /// <summary>GUID-класс «Конфигурация» в текстовом дереве корневого элемента .cfe.</summary>
    private const string ConfigurationClassGuid = "9cd510cd-abfc-11d4-9434-004095e12fc7";

    /// <summary>Маркер назначения расширения (в части .cfe стоит рядом с именем).</summary>
    private const string ConfigurationExtensionPurposeMarker = "ConfigurationExtensionPurpose";

    /// <summary>Сигнатура нативного контейнера 1С (первое LE-слово файла .cfe).</summary>
    private const uint ContainerSignature = 0x7FFFFFFF;

    /// <summary>Сигнатура zip-архива («PK\x03\x04») — встречается у перепакованных .cfe.</summary>
    private const uint ZipSignature = 0x04034B50;

    /// <summary>Размер ASCII-заголовка блока контейнера 1С.</summary>
    private const int BlockHeaderSize = 31;

    /// <summary>Заголовок элемента контейнера: даты создания/изменения + 4 нулевых байта.</summary>
    private const int ElementHeaderSize = 20;

    /// <summary>Максимальная глубина вложенных контейнеров при поиске имени.</summary>
    private const int MaxContainerDepth = 4;

    /// <summary>Предел размера таблицы адресов (защита от повреждённого файла).</summary>
    private const long MaxTableBlockBytes = 32L * 1024 * 1024;

    /// <summary>Предел размера заголовка элемента контейнера.</summary>
    private const long MaxElementHeaderBytes = 256 * 1024;

    /// <summary>
    /// Предел размера данных элемента, читаемых в память целиком. Блоки больше
    /// распаковываются и просматриваются потоково (без загрузки всего файла).
    /// </summary>
    private const long MaxElementBlockBytes = 64L * 1024 * 1024;

    /// <summary>Предел размера элемента zip-архива, читаемого в память.</summary>
    private const long MaxZipEntryBytes = 32L * 1024 * 1024;

    /// <summary>Размер порции при потоковом просмотре текста.</summary>
    private const int StreamScanBufferSize = 64 * 1024;

    /// <summary>Сколько символов держать «внахлёст» между порциями потока.</summary>
    private const int StreamScanWindow = 4096;

    /// <summary>Шаблон строкового свойства «{1,0,<guid>},"Имя"» в текстовом дереве 1С.</summary>
    private static readonly Regex QuotedNameRegex = new(
        "\\{1,0,[0-9a-fA-F-]{36}\\},\"([^\"\\r\\n]{1,100})\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Явные именованные свойства в JSON-подобных дампах и служебных блоках.</summary>
    private static readonly Regex NamedPropertyRegex = new(
        "\"(?:Name|Наименование|Синоним|Synonym)\"\\s*[:=]\\s*\"([^\"\\r\\n]{1,100})\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Читает НАСТОЯЩЕЕ имя расширения из файла .cfe (issue #117): при выборе файла
    /// «Обзором» пользователь должен увидеть имя, под которым расширение установлено
    /// в базе, а не имя файла. Возвращает <c>null</c>, если файл не распознан, имя
    /// прочитать не удалось или кандидаты противоречат друг другу (тогда callers
    /// откатываются к имени файла и требуют подтверждения).
    /// </summary>
    public static string? TryReadExtensionName(string? cfePath)
        => TryReadExtensionName(cfePath, out _);

    /// <summary>
    /// Issue #117 (диагностика выбора имени): читает имя и возвращает ВСЕХ найденных
    /// кандидатов с путями в дереве контейнеров и типом якоря — окно расширений пишет
    /// их в журнал, чтобы было видно, почему выбрано то или иное имя.
    /// </summary>
    internal static string? TryReadExtensionName(string? cfePath,
        out IReadOnlyList<ExtensionNameCandidate> candidates)
    {
        candidates = Array.Empty<ExtensionNameCandidate>();
        if (string.IsNullOrWhiteSpace(cfePath))
            return null;
        try
        {
            if (!File.Exists(cfePath))
                return null;

            // Issue #117: файл читается потоково (без File.ReadAllBytes) — крупные .cfe
            // не загружаются в память целиком, читаются только нужные блоки.
            using var stream = new FileStream(cfePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.RandomAccess);
            if (stream.Length < BlockHeaderSize + 16)
                return null;

            var signatureBytes = new byte[4];
            if (!ReadAt(stream, 0, signatureBytes, signatureBytes.Length))
                return null;
            var signature = BitConverter.ToUInt32(signatureBytes, 0);

            // Перепакованный .cfe может оказаться обычным zip-архивом.
            if (signature == ZipSignature)
                return TryReadNameFromZip(cfePath, out candidates);
            if (signature != ContainerSignature)
                return null;

            var elements = new List<ContainerElement>();
            ReadContainerElements(stream, stream.Length, 16, elements, MaxContainerDepth, depth: 0);
            var found = new List<ExtensionNameCandidate>();
            var name = SelectExtensionName(elements, found);
            candidates = found;
            return name;
        }
        catch
        {
            // Любая ошибка чтения — откат к имени файла (не мешаем установке).
            return null;
        }
    }

    /// <summary>Элемент контейнера .cfe с найденными якорями имени (issue #117).</summary>
    private sealed class ContainerElement
    {
        /// <summary>Имя элемента в контейнере (GUID объекта или служебное «form»/«module»).</summary>
        public string Name = string.Empty;

        /// <summary>Путь элемента в дереве контейнеров (например, «138» или «3/0»).</summary>
        public string Path = string.Empty;

        /// <summary>Глубина вложенности контейнера (0 — корневой).</summary>
        public int Depth;

        /// <summary>Порядковый номер обхода — для детерминированного выбора кандидата.</summary>
        public int Order;

        /// <summary>Имя из профильного якоря (GUID-класс «Конфигурация»/маркер назначения/Name).</summary>
        public string? FoundName;

        /// <summary>Имя по общему шаблону — «последний шанс», если профильных элементов нет.</summary>
        public string? GenericName;

        /// <summary>В элементе найден GUID-класс «Конфигурация».</summary>
        public bool HasConfigurationClass;

        /// <summary>В элементе найден маркер ConfigurationExtensionPurpose.</summary>
        public bool HasPurposeMarker;

        /// <summary>В элементе найдено явное именованное свойство (Name/Синоним).</summary>
        public bool HasNamedProperty;
    }

    /// <summary>Результат зондирования блока: имя и найденные якоря раскладки.</summary>
    private readonly record struct ElementNameProbe(
        string? Name,
        string? GenericName,
        bool HasConfigurationClass,
        bool HasPurposeMarker,
        bool HasNamedProperty);

    /// <summary>
    /// Найденный кандидат имени расширения (issue #117): значение, путь элемента в дереве
    /// контейнеров, глубина и тип якоря («class» / «purpose» / «named»). Нужен для журнала:
    /// при нескольких кандидатах видно, откуда взято имя и какие варианты отвергнуты.
    /// </summary>
    internal readonly record struct ExtensionNameCandidate(string Name, string Path, int Depth, string Anchor)
    {
        /// <summary>Строка для журнала: путь, глубина, якорь и значение.</summary>
        public string Describe() => $"path={Path}, depth={Depth}, anchor={Anchor}: '{Name}'";
    }

    /// <summary>
    /// Выбирает имя из найденных элементов (issue #117). Приоритет якорей:
    /// GUID-класс «Конфигурация» → маркер назначения → именованное свойство →
    /// общий шаблон (только если профильных элементов в файле нет вовсе).
    /// В контейнере .cfe сотни служебных элементов (объекты метаданных, формы,
    /// модули, картинки) со своими именами — брать первое попавшееся нельзя:
    /// именно из-за этого расширение «ИР» показывалось под чужим именем.
    /// Если на одном уровне приоритета нашлись РАЗНЫЕ имена — возвращается <c>null</c>
    /// (сомнение): окно расширений запросит подтверждение вместо подстановки догадки.
    /// </summary>
    private static string? SelectExtensionName(IReadOnlyList<ContainerElement> elements,
        List<ExtensionNameCandidate> candidates)
    {
        // 1. Основной якорь: элемент метаданных расширения с GUID-классом «Конфигурация».
        var name = SelectByAnchor(elements, e => e.HasConfigurationClass, "class", candidates,
            out var ambiguous);
        if (name is not null)
            return name;
        if (ambiguous)
            return null;

        // 2. Маркер назначения расширения (часть .cfe «ИР»).
        name = SelectByAnchor(elements, e => e.HasPurposeMarker, "purpose", candidates, out ambiguous);
        if (name is not null)
            return name;
        if (ambiguous)
            return null;

        // 3. Явное именованное свойство — только в элементе ВЕРХНЕГО уровня (метаданные):
        //    в текстах модулей вложенных контейнеров тоже встречается «Name»:"ПриЗаписи".
        name = SelectByAnchor(elements, e => e.HasNamedProperty && e.Depth == 0, "named", candidates,
            out ambiguous);
        if (name is not null)
            return name;
        if (ambiguous)
            return null;

        // Профильный элемент найден, но имя в нём не распознано — лучше вернуть null
        // (вызывающий код запросит подтверждение), чем подставить служебное имя.
        if (elements.Any(e => e.HasConfigurationClass || e.HasPurposeMarker || e.HasNamedProperty))
            return null;

        return SelectBest(elements, e => e.GenericName is not null, generic: true);
    }

    /// <summary>
    /// Выбирает имя по одному якорю (issue #117): собирает ВСЕ элементы с этим якорем
    /// в журнал кандидатов, а имя возвращает только если оно единственное —
    /// при разных значениях на одном приоритете фиксирует сомнение (<paramref name="ambiguous"/>).
    /// </summary>
    private static string? SelectByAnchor(IReadOnlyList<ContainerElement> elements,
        Func<ContainerElement, bool> isAnchor, string anchor,
        List<ExtensionNameCandidate> candidates, out bool ambiguous)
    {
        ambiguous = false;
        var matched = new List<ContainerElement>();
        foreach (var element in elements)
        {
            if (!isAnchor(element) || string.IsNullOrWhiteSpace(element.FoundName))
                continue;
            matched.Add(element);
            candidates.Add(new ExtensionNameCandidate(element.FoundName!.Trim(),
                element.Path, element.Depth, anchor));
        }
        if (matched.Count == 0)
            return null;

        var distinct = matched
            .Select(e => e.FoundName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count > 1)
        {
            // Разные имена на одном приоритете — догадка хуже подтверждения.
            ambiguous = true;
            return null;
        }

        var best = matched[0];
        for (var i = 1; i < matched.Count; i++)
            if (RanksBefore(matched[i], best))
                best = matched[i];
        return best.FoundName!.Trim();
    }

    /// <summary>Выбирает лучшего кандидата: ближе к корню, не версионный, раньше по обходу.</summary>
    private static string? SelectBest(IReadOnlyList<ContainerElement> elements,
        Func<ContainerElement, bool> isCandidate, bool generic)
    {
        ContainerElement? best = null;
        foreach (var element in elements)
        {
            if (!isCandidate(element))
                continue;
            var value = generic ? element.GenericName : element.FoundName;
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (best is null || RanksBefore(element, best))
                best = element;
        }
        if (best is null)
            return null;
        return (generic ? best.GenericName : best.FoundName)?.Trim();
    }

    /// <summary>
    /// Сравнение кандидатов: элемент ближе к корню контейнера предпочтительнее,
    /// не версионный («.N») — предпочтительнее, при равенстве — раньше по обходу.
    /// </summary>
    private static bool RanksBefore(ContainerElement candidate, ContainerElement current)
    {
        if (candidate.Depth != current.Depth)
            return candidate.Depth < current.Depth;
        var candidateVersioned = IsVersionedElement(candidate.Name);
        var currentVersioned = IsVersionedElement(current.Name);
        if (candidateVersioned != currentVersioned)
            return !candidateVersioned;
        return candidate.Order < current.Order;
    }

    /// <summary>Истина, если имя элемента имеет суффикс версии «.N» (старая версия файла).</summary>
    private static bool IsVersionedElement(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
            return false;
        for (var i = dot + 1; i < name.Length; i++)
            if (!char.IsAsciiDigit(name[i]))
                return false;
        return true;
    }

    /// <summary>Данные элемента — вложенный контейнер 1С (имя ищется в его элементах).</summary>
    private static bool IsContainerData(byte[] data) =>
        data.Length > 16 && BitConverter.ToUInt32(data, 0) == ContainerSignature;

    /// <summary>
    /// Зондирует данные элемента (issue #117): кодировка определяется ПО БЛОКУ
    /// (BOM / плотность нулевых старших байтов), а не по первым 4 КБ всего файла.
    /// Для UTF-16 блок декодируется целиком, с fallback на UTF-8.
    /// </summary>
    private static ElementNameProbe ProbeElementName(byte[] data)
    {
        if (data.Length < 16)
            return default;

        // Вложенный контейнер — двоичные данные: имя ищется в его элементах,
        // иначе из служебных строк внутри находились чужие имена.
        if (IsContainerData(data))
            return default;

        if (LooksLikeUtf16(data))
        {
            var utf16 = ProbeNameFromText(Encoding.Unicode.GetString(data));
            if (utf16.Name is not null || utf16.HasConfigurationClass)
                return utf16;
            return MergeProbes(utf16, ProbeNameFromText(Encoding.UTF8.GetString(data)));
        }

        var utf8 = ProbeNameFromText(Encoding.UTF8.GetString(data));
        if (utf8.Name is not null || utf8.HasConfigurationClass)
            return utf8;

        // Без BOM: блок может быть UTF-16LE — проверяем плотность нулевых старших байтов.
        if (LooksLikeUtf16Loose(data))
            return MergeProbes(utf8, ProbeNameFromText(Encoding.Unicode.GetString(data)));
        return utf8;
    }

    private static ElementNameProbe MergeProbes(ElementNameProbe first, ElementNameProbe second) =>
        new(first.Name ?? second.Name,
            first.GenericName ?? second.GenericName,
            first.HasConfigurationClass || second.HasConfigurationClass,
            first.HasPurposeMarker || second.HasPurposeMarker,
            first.HasNamedProperty || second.HasNamedProperty);

    /// <summary>Признак UTF-16LE-текста: BOM или высокая доля нулевых старших байтов.</summary>
    private static bool LooksLikeUtf16(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return true;
        return HasUtf16ZeroPattern(data, oddDivisor: 4, evenDivisor: 10);
    }

    /// <summary>Мягкий признак UTF-16LE (блок без BOM, но с нулевыми старшими байтами).</summary>
    private static bool LooksLikeUtf16Loose(byte[] data) =>
        HasUtf16ZeroPattern(data, oddDivisor: 8, evenDivisor: 8);

    private static bool HasUtf16ZeroPattern(byte[] data, int oddDivisor, int evenDivisor)
    {
        // Плотность нулей считается по ВСЕМУ блоку (не по первым 4 КБ файла):
        // у крупных блоков заголовок может быть в UTF-8, а текст — в UTF-16.
        var sample = Math.Min(data.Length, 256 * 1024);
        if (sample < 32)
            return false;
        var pairs = sample / 2;
        var oddZeros = 0;
        var evenZeros = 0;
        for (var i = 0; i + 1 < sample; i += 2)
        {
            if (data[i] == 0) evenZeros++;
            if (data[i + 1] == 0) oddZeros++;
        }
        return oddZeros > pairs / oddDivisor && evenZeros < pairs / evenDivisor;
    }

    /// <summary>
    /// Ищет имя по якорям: после GUID-класса «Конфигурация», рядом с маркером
    /// ConfigurationExtensionPurpose или в явном именованном свойстве.
    /// Общий шаблон «{1,0,<guid>},"Имя"» возвращается отдельно
    /// (<see cref="ElementNameProbe.GenericName"/>) и используется только как
    /// «последний шанс» на уровне выбора элемента — когда профильных элементов нет.
    /// </summary>
    private static ElementNameProbe ProbeNameFromText(string text)
    {
        if (text.Length == 0)
            return default;

        var hasClass = false;
        var hasPurpose = false;
        var hasNamed = false;
        string? name = null;

        // 1. Основной якорь: после GUID-класса «Конфигурация» первое строковое
        //    свойство — имя расширения:
        //    …9cd510cd-…,{1,{79,{0,{4,{1,0,<guid>},"ИмяРасширения",{1,"ru",…
        var classPos = text.IndexOf(ConfigurationClassGuid, StringComparison.OrdinalIgnoreCase);
        if (classPos >= 0)
        {
            hasClass = true;
            name = FindFirstQuotedName(text, classPos, text.Length - classPos);
        }

        // 2. Issue #117 («ИР»): имя может стоять рядом с маркером назначения
        //    расширения, а не сразу после класса «Конфигурация».
        if (name is null)
        {
            var purposePos = text.IndexOf(ConfigurationExtensionPurposeMarker, StringComparison.OrdinalIgnoreCase);
            if (purposePos >= 0)
            {
                hasPurpose = true;
                name = FindFirstQuotedName(text, purposePos,
                    Math.Min(text.Length - purposePos, 200_000));
            }
        }

        // 3. Явные именованные свойства (JSON-подобные дампы и служебные блоки).
        if (name is null)
        {
            var named = NamedPropertyRegex.Match(text);
            if (named.Success && IsPlausibleNameValue(named.Groups[1].Value, generic: true))
            {
                hasNamed = true;
                name = named.Groups[1].Value.Trim();
            }
        }

        var generic = name is null ? FindGenericName(text) : null;
        return new ElementNameProbe(name, generic, hasClass, hasPurpose, hasNamed);
    }

    /// <summary>Общий шаблон «{1,0,<guid>},"…"» — только как «последний шанс».</summary>
    private static string? FindGenericName(string text)
    {
        foreach (Match match in QuotedNameRegex.Matches(text))
        {
            var value = match.Groups[1].Value;
            if (IsPlausibleNameValue(value, generic: true))
                return value.Trim();
        }
        return null;
    }

    /// <summary>Ищет первое правдоподобное имя «{1,0,<guid>},"…"» в указанном окне текста.</summary>
    private static string? FindFirstQuotedName(string text, int start, int length)
    {
        if (start < 0 || length <= 0 || start >= text.Length)
            return null;
        length = Math.Min(length, text.Length - start);
        var match = QuotedNameRegex.Match(text, start, length);
        while (match.Success)
        {
            var value = match.Groups[1].Value;
            if (IsPlausibleNameValue(value, generic: false))
                return value.Trim();
            match = match.NextMatch();
        }
        return null;
    }

    /// <summary>
    /// Проверка правдоподобности строкового значения как имени расширения:
    /// отсекает GUID, языковые коды локализованных синонимов, пути и мусор.
    /// </summary>
    private static bool IsPlausibleNameValue(string? value, bool generic)
    {
        if (value is null)
            return false;
        var name = value.Trim().TrimEnd('\0');
        if (name.Length is 0 or > 100)
            return false;
        if (name.IndexOf('"') >= 0 || name.IndexOf('{') >= 0 || name.IndexOf('}') >= 0)
            return false;
        if (name.Any(char.IsControl))
            return false;
        if (Guid.TryParse(name, out _))
            return false;
        if (!generic)
            return true;
        if (name.All(char.IsDigit))
            return false;
        if (name.Length <= 3 && name.All(c => c is >= 'a' and <= 'z'))
            return false;
        if (name.Contains('/') || name.Contains('\\'))
            return false;
        return true;
    }

    /// <summary>Поиск имени в перепакованном .cfe (zip-архив).</summary>
    private static string? TryReadNameFromZip(string cfePath, out IReadOnlyList<ExtensionNameCandidate> candidates)
    {
        candidates = Array.Empty<ExtensionNameCandidate>();
        using var archive = ZipFile.OpenRead(cfePath);
        string? generic = null;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length <= 0 || entry.Length > MaxZipEntryBytes)
                continue;
            try
            {
                using var entryStream = entry.Open();
                using var memory = new MemoryStream((int)entry.Length);
                entryStream.CopyTo(memory);
                var bytes = memory.ToArray();

                // Запись может быть вложенным контейнером 1С — разбираем его элементы.
                if (IsContainerData(bytes))
                {
                    var elements = new List<ContainerElement>();
                    using var nestedStream = new MemoryStream(bytes, writable: false);
                    ReadContainerElements(nestedStream, bytes.Length, 16, elements, MaxContainerDepth, depth: 0);
                    var found = new List<ExtensionNameCandidate>();
                    var nestedName = SelectExtensionName(elements, found);
                    candidates = found;
                    if (nestedName is not null)
                        return nestedName;
                    continue;
                }

                var probe = ProbeElementName(bytes);
                if (probe.Name is not null)
                {
                    candidates = new[]
                    {
                        new ExtensionNameCandidate(probe.Name, entry.FullName, 0,
                            probe.HasConfigurationClass ? "class" : "entry")
                    };
                    return probe.Name;
                }
                generic ??= probe.GenericName;
            }
            catch
            {
                // Повреждённая запись архива — пропускаем.
            }
        }
        return generic;
    }

    /// <summary>
    /// Разбирает контейнер 1С (включая вложенные до <paramref name="maxDepth"/>)
    /// и наполняет <paramref name="elements"/> найденными якорями имени расширения.
    /// </summary>
    private static void ReadContainerElements(Stream source, long containerLength, long tableAddr,
        List<ContainerElement> elements, int maxDepth, int depth, string pathPrefix = "")
    {
        // Заголовок контейнера: 16 байт (сигнатура, размер страницы, версия, резерв),
        // затем блок-таблица адресов элементов.
        var tableRaw = ReadBlock(source, tableAddr, MaxTableBlockBytes);
        if (tableRaw is null)
            return;
        var table = TryInflate(tableRaw);

        var index = 0;
        for (var offset = 0; offset + 12 <= table.Length; offset += 12)
        {
            var signature = BitConverter.ToUInt32(table, offset + 8);
            if (signature != ContainerSignature)
                break;
            var headerAddr = BitConverter.ToUInt32(table, offset);
            var dataAddr = BitConverter.ToUInt32(table, offset + 4);
            if (headerAddr == ContainerSignature || headerAddr == 0 || dataAddr >= containerLength)
                break;

            var elementHeader = TryInflate(ReadBlock(source, headerAddr, MaxElementHeaderBytes) ?? Array.Empty<byte>());
            var element = new ContainerElement
            {
                Name = GetElementName(elementHeader),
                Path = pathPrefix.Length == 0 ? index.ToString() : pathPrefix + "/" + index,
                Depth = depth,
                Order = elements.Count
            };

            var dataRaw = ReadBlock(source, dataAddr, MaxElementBlockBytes);
            if (dataRaw is not null)
            {
                var data = TryInflate(dataRaw);
                var nested = GetNestedContainer(data, dataRaw);
                if (nested is not null)
                {
                    // Данные элемента — вложенный контейнер: имя ищется в его элементах,
                    // а не в двоичном содержимом (иначе находились служебные строки).
                    if (maxDepth > 0)
                    {
                        using var nestedStream = new MemoryStream(nested, writable: false);
                        ReadContainerElements(nestedStream, nested.Length, 16, elements,
                            maxDepth - 1, depth + 1, element.Path);
                    }
                }
                else
                {
                    var probe = ProbeElementName(data);
                    if (probe.Name is null && !ReferenceEquals(data, dataRaw))
                        probe = MergeProbes(probe, ProbeElementName(dataRaw));
                    ApplyProbe(element, probe);
                }
            }
            else
            {
                // Элемент больше MaxElementBlockBytes — просматриваем потоково.
                ApplyProbe(element, ScanLargeBlock(source, dataAddr));
            }

            elements.Add(element);
            index++;
        }
    }

    /// <summary>Возвращает данные вложенного контейнера (распакованные или как есть).</summary>
    private static byte[]? GetNestedContainer(byte[] data, byte[] dataRaw)
    {
        if (IsContainerData(data))
            return data;
        if (IsContainerData(dataRaw))
            return dataRaw;
        return null;
    }

    private static void ApplyProbe(ContainerElement element, ElementNameProbe probe)
    {
        element.FoundName = probe.Name;
        element.GenericName = probe.GenericName;
        element.HasConfigurationClass = probe.HasConfigurationClass;
        element.HasPurposeMarker = probe.HasPurposeMarker;
        element.HasNamedProperty = probe.HasNamedProperty;
    }

    /// <summary>Потоковый поиск имени в очень крупном элементе контейнера.</summary>
    private static ElementNameProbe ScanLargeBlock(Stream source, long dataAddr)
    {
        // Блок может быть сжат deflate или лежать как есть, а текст — в UTF-8 или
        // UTF-16LE. Перебираем варианты с переоткрытием блока на каждую попытку.
        foreach (var inflate in new[] { true, false })
        {
            var utf8 = ScanBlockWithEncoding(source, dataAddr, inflate, Encoding.UTF8);
            if (utf8.Name is not null || utf8.HasConfigurationClass)
                return utf8;

            var utf16 = ScanBlockWithEncoding(source, dataAddr, inflate, Encoding.Unicode);
            var merged = MergeProbes(utf8, utf16);
            if (merged.Name is not null || merged.HasConfigurationClass)
                return merged;
        }
        return default;
    }

    private static ElementNameProbe ScanBlockWithEncoding(Stream source, long dataAddr,
        bool inflate, Encoding encoding)
    {
        try
        {
            using var block = new BlockDataStream(source, dataAddr);
            if (inflate)
            {
                using var deflate = new DeflateStream(block, CompressionMode.Decompress, leaveOpen: true);
                return ScanTextStream(deflate, encoding);
            }
            return ScanTextStream(block, encoding);
        }
        catch
        {
            // Не сжатый/повреждённый блок — вариантов для этой кодировки нет.
            return default;
        }
    }

    /// <summary>Потоковое чтение текста с окном перекрытия (имя может быть на границе порций).</summary>
    private static ElementNameProbe ScanTextStream(Stream stream, Encoding encoding)
    {
        var decoder = encoding.GetDecoder();
        var bytes = new byte[StreamScanBufferSize];
        var chars = new char[encoding.GetMaxCharCount(bytes.Length)];
        var text = new StringBuilder();
        var hasClass = false;
        var hasPurpose = false;
        var hasNamed = false;
        string? generic = null;
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            var charCount = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
            if (charCount > 0)
                text.Append(chars, 0, charCount);
            if (text.Length < StreamScanWindow * 2)
                continue;
            var probe = ProbeNameFromText(text.ToString());
            if (probe.Name is not null)
                return probe;
            hasClass |= probe.HasConfigurationClass;
            hasPurpose |= probe.HasPurposeMarker;
            hasNamed |= probe.HasNamedProperty;
            generic ??= probe.GenericName;
            text.Remove(0, text.Length - StreamScanWindow);
        }

        var last = ProbeNameFromText(text.ToString());
        if (last.Name is not null)
            return last;
        return new ElementNameProbe(null, generic ?? last.GenericName,
            hasClass || last.HasConfigurationClass,
            hasPurpose || last.HasPurposeMarker,
            hasNamed || last.HasNamedProperty);
    }

    /// <summary>Имя элемента контейнера: UTF-16LE после 20-байтового заголовка.</summary>
    private static string GetElementName(byte[] elementHeader)
    {
        if (elementHeader.Length <= ElementHeaderSize)
            return string.Empty;
        var charCount = (elementHeader.Length - ElementHeaderSize) / 2;
        if (charCount > 256)
            charCount = 256;
        return Encoding.Unicode.GetString(elementHeader, ElementHeaderSize, charCount * 2)
            .TrimEnd('\0').TrimEnd(' ');
    }

    /// <summary>
    /// Читает блок контейнера по адресу его ASCII-заголовка, поддерживая блоки,
    /// разбитые на несколько страниц (переход по адресу следующей страницы).
    /// </summary>
    private static byte[]? ReadBlock(Stream source, long addr, long maxBytes)
    {
        if (!TryReadHeader(source, addr, out var dataSize, out _, out _))
            return null;
        if (dataSize <= 0 || dataSize > maxBytes)
            return null;
        try
        {
            using var block = new BlockDataStream(source, addr);
            using var result = new MemoryStream((int)Math.Min(dataSize, int.MaxValue));
            block.CopyTo(result);
            return result.Length > 0 ? result.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Разбор ASCII-заголовка блока контейнера 1С.</summary>
    private static bool TryReadHeader(Stream source, long addr,
        out long dataSize, out long pageSize, out long nextAddr)
    {
        dataSize = 0;
        pageSize = 0;
        nextAddr = 0;
        if (addr < 0 || addr + BlockHeaderSize > source.Length)
            return false;
        var header = new byte[BlockHeaderSize];
        if (!ReadAt(source, addr, header, header.Length))
            return false;
        var text = Encoding.ASCII.GetString(header);
        if (!text.StartsWith("\r\n", StringComparison.Ordinal))
            return false;
        if (!TryParseHex(text, 2, 8, out dataSize) ||
            !TryParseHex(text, 11, 8, out pageSize) ||
            !TryParseHex(text, 20, 8, out nextAddr))
            return false;
        if (pageSize <= 0)
            pageSize = dataSize;
        return dataSize >= 0;
    }

    /// <summary>Чтение ровно <paramref name="count"/> байт по абсолютному смещению.</summary>
    private static bool ReadAt(Stream source, long offset, byte[] buffer, int count)
        => ReadAt(source, offset, buffer, 0, count) == count;

    private static int ReadAt(Stream source, long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > source.Length)
            return 0;
        source.Position = offset;
        var total = 0;
        while (total < count)
        {
            var read = source.Read(buffer, bufferOffset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }
        return total;
    }

    /// <summary>
    /// Потоковое чтение данных блока контейнера 1С (с переходом по страницам),
    /// без загрузки всего файла в память.
    /// </summary>
    private sealed class BlockDataStream : Stream
    {
        private readonly Stream _source;
        private long _currentDataOffset;
        private long _nextAddr;
        private long _remaining;
        private int _pageSize;
        private int _pageRemaining;

        public BlockDataStream(Stream source, long addr)
        {
            _source = source;
            if (!TryReadHeader(source, addr, out var dataSize, out var pageSize, out var nextAddr))
                throw new InvalidDataException("Повреждён заголовок блока контейнера 1С.");
            _remaining = dataSize;
            _pageSize = (int)Math.Min(pageSize, int.MaxValue);
            _nextAddr = nextAddr;
            _currentDataOffset = addr + BlockHeaderSize;
            _pageRemaining = (int)Math.Min(_pageSize, _remaining);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (count > 0 && _remaining > 0)
            {
                if (_pageRemaining == 0)
                {
                    if (_nextAddr == ContainerSignature)
                        break;
                    if (!TryReadHeader(_source, _nextAddr, out _, out var pageSize, out var nextAddr))
                        break;
                    _currentDataOffset = _nextAddr + BlockHeaderSize;
                    _nextAddr = nextAddr;
                    _pageSize = (int)Math.Min(pageSize, int.MaxValue);
                    _pageRemaining = (int)Math.Min(_pageSize, _remaining);
                    if (_pageRemaining <= 0)
                        break;
                }
                var take = Math.Min(count, _pageRemaining);
                var read = ReadAt(_source, _currentDataOffset, buffer, offset, take);
                if (read <= 0)
                    break;
                _currentDataOffset += read;
                _pageRemaining -= read;
                _remaining -= read;
                offset += read;
                count -= read;
                total += read;
            }
            return total;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Разбор 8-символьного hex-числа из ASCII-заголовка блока.</summary>
    private static bool TryParseHex(string text, int start, int length, out long value)
    {
        value = 0;
        if (start + length > text.Length)
            return false;
        var chunk = text.Substring(start, length).Trim();
        return long.TryParse(chunk, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Распаковывает deflate-данные; если данные не сжаты — возвращает как есть.</summary>
    private static byte[] TryInflate(byte[] data)
    {
        if (data.Length < 2)
            return data;
        try
        {
            using var source = new MemoryStream(data);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream();
            deflate.CopyTo(result);
            return result.Length > 0 ? result.ToArray() : data;
        }
        catch
        {
            return data;
        }
    }

    // ---------------------------------------------------------------------------
    // Определение фактического отказа в журнале конфигуратора (issue #117): пакетный
    // конфигуратор возвращает код 0 даже при отказе применять расширение («Загрузка
    // конфигурации успешно завершена» при неуспешном /UpdateDBCfg) — поэтому журнал
    // окна не должен утверждать «успешно», если в выводе есть сообщение об отказе.
    // ---------------------------------------------------------------------------

    /// <summary>Маркеры отказа в выводе конфигуратора (русские и английские локали).</summary>
    private static readonly string[] RefusalMarkers =
    {
        "не выполнено",
        "не может быть выполнено",
        "выполнено с ошибками",
        "обнаружены ошибки",
        "ошибка обновления",
        "ошибка при обновлении",
        // Issue #117 (7OH): платформа ругается на уже установленное расширение —
        // это отказ, а не «успешно завершено», даже если код возврата 0.
        "уже существует",
        "уже установлено",
        "already exists",
        "already installed",
        "has failed",
        "cannot be performed",
        "cannot be completed",
        "completed with errors",
    };

    /// <summary>
    /// Ищет в выводе конфигуратора (/Out) сообщение об отказе и возвращает его строку
    /// (или <c>null</c>, если отказ не обнаружен). Чистая функция — для юнит-тестов
    /// и журнала результатов установки расширений.
    /// </summary>
    public static string? FindRefusalMessage(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
            return null;
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            foreach (var marker in RefusalMarkers)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return line;
            }
        }
        return null;
    }

    /// <summary>
    /// Разбор вывода <c>/DumpDBCfgList</c>: имена расширений по одному в строке.
    /// Служебные строки конфигуратора (пустые, с двоеточием в начале, сообщения
    /// о завершении) отбрасываются; повторы убираются.
    /// </summary>
    public static IReadOnlyList<string> ParseList(string? log)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(log))
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim().Trim('﻿');
            if (line.Length == 0)
                continue;
            if (line.StartsWith(":", StringComparison.Ordinal) || line.EndsWith(".", StringComparison.Ordinal))
                continue;
            if (line.Contains(' ') && !line.Contains('_') && line.Split(' ').Length > 3)
                continue; // похоже на фразу-сообщение, а не на имя (имена — идентификаторы)
            if (seen.Add(line))
                result.Add(line);
        }
        return result;
    }

    public static async Task<DesignerCommandResult> ListAsync(Infobase infobase, CancellationToken ct = default) =>
        await DesignerCommandRunner.RunAsync(infobase, ListArguments, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);

    public static async Task<DesignerCommandResult> InstallAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        var result = await InstallCoreAsync(infobase, cfePath, extensionName, ct).ConfigureAwait(false);
        Audit.AuditHub.Write(Audit.AuditActions.ExtensionInstall, infobase?.Name ?? string.Empty, result.Success,
            $"{extensionName} ← {cfePath}" + (result.Success ? string.Empty : "; " + result.Error));
        return result;
    }

    private static async Task<DesignerCommandResult> InstallCoreAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfePath) || !File.Exists(cfePath))
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.FileNotFound"));
        var args = BuildInstallArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Признак отказа конфигуратора по причине «Error in command-line options» (issue #87):
    /// конфигуратор не принял строку параметров (например, на клиент-серверной базе).
    /// Возвращает <c>true</c>, если результат неуспешен и в тексте ошибки/вывода есть
    /// признак ошибки параметров командной строки. Чистая функция — для юнит-тестов
    /// и автоматического fallback'а на ibcmd-ветку.
    /// </summary>
    public static bool IsCommandLineOptionsError(DesignerCommandResult result)
    {
        if (result is null || result.Success || result.ExitCode == 0)
            return false;
        var text = (result.Error ?? string.Empty) + "\n" + (result.Log ?? string.Empty);
        return text.IndexOf("command-line options", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("command line options", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Признак отказа конфигуратора по причине сбоя аутентификации (issue #87): база без
    /// собственных учётных данных при незаданной авторизации запуска — конфигуратор падает
    /// с «The infobase user is not authenticated». Чистая функция — для юнит-тестов
    /// и автоматического fallback'а на ibcmd-ветку.
    /// </summary>
    public static bool IsAuthenticationError(DesignerCommandResult result)
    {
        if (result is null || result.Success || result.ExitCode == 0)
            return false;
        var text = (result.Error ?? string.Empty) + "\n" + (result.Log ?? string.Empty);
        return text.IndexOf("infobase user is not authenticated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Нужен ли автоматический переход на ibcmd-ветку после отказа конфигуратора (issue #87):
    /// либо конфигуратор не принял параметры командной строки, либо произошёл сбой
    /// аутентификации пользователя ИБ. Чистая функция — для юнит-тестов и fallback'а в
    /// <see cref="Workspace1C.ViewModels.ExtensionsViewModel"/>.
    /// </summary>
    public static bool ShouldFallbackToIbcmd(DesignerCommandResult result)
        => IsCommandLineOptionsError(result) || IsAuthenticationError(result);

    public static async Task<DesignerCommandResult> DumpAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        var args = BuildDumpArguments(cfePath, extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        var dir = Path.GetDirectoryName(cfePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var result = await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
        if (result.Success && (!File.Exists(cfePath) || new FileInfo(cfePath).Length == 0))
        {
            return new DesignerCommandResult
            {
                ExitCode = result.ExitCode,
                Log = result.Log,
                Error = LocalizationManager.T("Extensions.DumpEmpty")
            };
        }
        return result;
    }

    public static async Task<DesignerCommandResult> DeleteAsync(Infobase infobase, string extensionName,
        CancellationToken ct = default)
    {
        var result = await DeleteCoreAsync(infobase, extensionName, ct).ConfigureAwait(false);
        Audit.AuditHub.Write(Audit.AuditActions.ExtensionDelete, infobase?.Name ?? string.Empty, result.Success,
            extensionName + (result.Success ? string.Empty : "; " + result.Error));
        return result;
    }

    private static async Task<DesignerCommandResult> DeleteCoreAsync(Infobase infobase, string extensionName,
        CancellationToken ct)
    {
        var args = BuildDeleteArguments(extensionName);
        if (args is null)
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.BadName"));
        return await DesignerCommandRunner.RunAsync(infobase, args, null, ct).ConfigureAwait(false);
    }
}
