using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Workspace1C.Models;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace Workspace1C.Services;

/// <summary>
/// Тип локального кеша платформы 1С.
/// </summary>
[Flags]
public enum OneCCacheKind
{
    /// <summary>Не очищать кеш.</summary>
    None = 0,

    /// <summary>
    /// Программный кеш: %LOCALAPPDATA%\1C\1cv8…
    /// </summary>
    Program = 1,

    /// <summary>
    /// Пользовательский кеш: %APPDATA%\1C\1cv8…
    /// </summary>
    User = 2,

    /// <summary>Программный и пользовательский кеш одновременно.</summary>
    All = Program | User
}

/// <summary>
/// Сервис очистки локального кеша платформы 1С для одной или нескольких информационных баз.
/// </summary>
public static class OneCCacheCleaner
{
    // Дополнительные корни кеша из настроек пользователя (issue #109): каталоги, где 1С
    // хранит кеш в нестандартном расположении (перенос копированием, junction на уровне
    // родителя «1C» и т.п.). Применяются дополнительно к штатным корням платформы;
    // заполнение — Settings.SetAdditionalCacheRoots → OneCCacheCleaner.SetAdditionalRoots.
    private static readonly object s_additionalRootsLock = new();
    private static List<string> s_additionalRoots = new();

    /// <summary>
    /// Задаёт дополнительные корни кеша 1С (issue #109). Вызывается при старте приложения
    /// и после изменения настроек. Пустые/нулевые значения игнорируются, дубликаты
    /// (без учёта регистра) убираются. Null сбрасывает список.
    /// </summary>
    public static void SetAdditionalRoots(IEnumerable<string?>? roots)
    {
        var normalized = (roots ?? Enumerable.Empty<string?>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        lock (s_additionalRootsLock)
        {
            s_additionalRoots = normalized;
        }
    }

    /// <summary>Снимок дополнительных корней (для тестов и диагностики).</summary>
    internal static IReadOnlyList<string> GetAdditionalRootsSnapshot()
    {
        lock (s_additionalRootsLock)
        {
            return s_additionalRoots.ToList();
        }
    }

    /// <summary>
    /// Возвращает все реально существующие каталоги кеша указанной базы (issue #110):
    /// публичная обёртка над перечислением кеша для команд «Открыть папку кеша».
    /// Пути дедуплицированы и отсортированы.
    /// </summary>
    public static IReadOnlyList<string> GetCacheDirectories(Infobase infobase, OneCCacheKind kind)
    {
        if (infobase is null || kind == OneCCacheKind.None)
            return Array.Empty<string>();
        return EnumerateCacheDirectories(infobase, kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Очищает программный и пользовательский кеш 1С для указанной информационной базы.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase)
    {
        return Clear(infobase, OneCCacheKind.All);
    }

    /// <summary>
    /// Очищает кеш указанного типа для одной информационной базы.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase, OneCCacheKind kind)
    {
        return Clear(new[] { infobase }, kind);
    }

    /// <summary>
    /// Очищает кеш указанного типа для набора информационных баз.
    /// </summary>
    /// <param name="infobases">Набор информационных баз, кеш которых нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(IEnumerable<Infobase> infobases, OneCCacheKind kind)
    {
        if (infobases is null || kind == OneCCacheKind.None)
            return 0;

        var removed = 0;
        foreach (var infobase in infobases)
        {
            if (infobase is null)
                continue;
            removed += ClearSingle(infobase, kind);
        }

        return removed;
    }

    /// <summary>
    /// Очищает кеш 1С указанного типа для конкретной информационной базы.
    /// Каталоги кеша ищутся по карте IdConnStrMap из 1cv8u.pfl (имя каталога — собственный
    /// GUID платформы), а также по имени клиент-серверного каталога Srvr__…__Ref__…__,
    /// ID базы и имени базы. Удаляются только реально существующие и действительно удалённые
    /// каталоги.
    /// </summary>
    private static int ClearSingle(Infobase infobase, OneCCacheKind kind)
    {
        var removed = 0;
        foreach (var dir in EnumerateCacheDirectories(infobase, kind).Distinct())
        {
            if (TryDeleteDirectory(dir))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Определяет имя каталога кеша базы по старым правилам (используется как запасной
    /// вариант, если карту IdConnStrMap прочитать не удалось).
    /// Для клиент-серверной базы — имя базы на сервере, для файловой — имя файла без расширения.
    /// </summary>
    private static string GetCacheName(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            ConnectionType.File => Path.GetFileNameWithoutExtension(conn.FilePath),
            _ => conn.DatabaseName
        };
    }

    /// <summary>
    /// Запись карты IdConnStrMap: строка соединения, разобранная в настройки, и GUID каталога кеша.
    /// </summary>
    private sealed class IdConnStrEntry
    {
        public required ConnectionSettings Settings { get; init; }
        public required string CacheGuid { get; init; }
    }

    // Кэш разобранной карты IdConnStrMap: повторно файл 1cv8u.pfl читается, только если он
    // изменился. Доступ синхронизируется — чтение может выполняться из фоновых потоков.
    private static readonly object PflLock = new();
    private static string? _pflCachedPath;
    private static DateTime _pflCachedMtimeUtc;
    private static IReadOnlyList<IdConnStrEntry>? _pflCachedEntries;

    /// <summary>
    /// Возвращает путь к файлу карты IdConnStrMap (1cv8u.pfl) в корне пользовательского кеша,
    /// если файл существует, иначе null.
    /// </summary>
    private static string? GetPflPath()
    {
#if LINUX
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            foreach (var rel in new[] { ".1cv8/1C/1cv8/1cv8u.pfl", ".1cv8/1cv8/1cv8u.pfl" })
            {
                var p = Path.Combine(home, rel);
                if (File.Exists(p))
                    return p;
            }
        }
#else
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            var p = Path.Combine(appData, "1C", "1cv8", "1cv8u.pfl");
            if (File.Exists(p))
                return p;
        }
#endif
        return null;
    }

    /// <summary>
    /// Читает и разбирает карту IdConnStrMap из файла 1cv8u.pfl. Карта содержит пары
    /// «строка соединения → GUID каталога кеша». Результат кэшируется до изменения файла.
    /// </summary>
    private static IReadOnlyList<IdConnStrEntry>? LoadIdConnStrMap()
    {
        var path = GetPflPath();
        if (path is null)
            return null;

        lock (PflLock)
        {
            try
            {
                var mtime = File.GetLastWriteTimeUtc(path);
                if (_pflCachedPath == path && _pflCachedMtimeUtc == mtime && _pflCachedEntries is not null)
                    return _pflCachedEntries;
            }
            catch
            {
                return null;
            }

            List<IdConnStrEntry>? entries = null;
            try
            {
                // ReadAllText автоматически распознаёт BOM и декодирует UTF-8.
                var text = File.ReadAllText(path);

                // Пары «{"S","<строка соединения>"},{"S","<GUID>"}». Кавычки внутри
                // строки соединения удвоены (""), их нужно развернуть обратно в одну.
                const string pattern =
                    @"\{\s*""S""\s*,\s*""(?<conn>(?:[^""]|"""")*)""\s*\}\s*,\s*" +
                    @"\{\s*""S""\s*,\s*""(?<guid>[0-9A-Fa-f\-]{36})""\s*\}";
                var options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

                foreach (Match m in Regex.Matches(text, pattern, options))
                {
                    var connRaw = m.Groups["conn"].Value.Replace("\"\"", "\"");
                    var settings = ConnectionSettings.ParseConnectionString(connRaw);
                    if (settings is null || string.IsNullOrWhiteSpace(m.Groups["guid"].Value))
                        continue;

                    (entries ??= new List<IdConnStrEntry>()).Add(new IdConnStrEntry
                    {
                        Settings = settings,
                        CacheGuid = m.Groups["guid"].Value
                    });
                }
            }
            catch
            {
                entries = null;
            }

            if (entries is not null)
            {
                try { _pflCachedMtimeUtc = File.GetLastWriteTimeUtc(path); } catch { _pflCachedMtimeUtc = DateTime.MinValue; }
                _pflCachedPath = path;
                _pflCachedEntries = entries;
            }
            return entries;
        }
    }

    /// <summary>
    /// Проверяет, соответствует ли запись карты IdConnStrMap информационной базе.
    /// Хост сравнивается без учёта порта (одна база встречается в карте и с портом,
    /// и без него), имя базы — без учёта регистра.
    /// </summary>
    private static bool MatchesBase(IdConnStrEntry entry, Infobase infobase)
    {
        var conn = infobase.Connection;
        switch (entry.Settings.Type)
        {
            case ConnectionType.File:
                if (conn.Type != ConnectionType.File)
                    return false;
                return string.Equals(
                    NormalizePathForCompare(entry.Settings.FilePath),
                    NormalizePathForCompare(conn.FilePath),
                    StringComparison.OrdinalIgnoreCase);

            case ConnectionType.WebServer:
                if (conn.Type != ConnectionType.WebServer)
                    return false;
                return string.Equals(
                    (entry.Settings.WebUrl ?? string.Empty).Trim(),
                    (conn.WebUrl ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);

            default: // ClientServer
                if (conn.Type != ConnectionType.ClientServer)
                    return false;
                if (!string.Equals(
                        (entry.Settings.DatabaseName ?? string.Empty).Trim(),
                        (conn.DatabaseName ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                // Хост сравниваем без учёта порта с обеих сторон: платформа в 1cv8u.pfl
                // хранит одну и ту же базу и как «host», и как «host:port», а в настройках
                // приложения порт может быть как вынесен в отдельное поле, так и вписан
                // прямо в имя сервера (issue #178).
                return string.Equals(
                    NormalizeHost(entry.Settings.Server),
                    NormalizeHost(conn.Server),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Приводит имя хоста к единому виду без учёта порта: «host», «host:1541»,
    /// «[2001:db8::1]:1541» → «host» / «2001:db8::1» соответственно. Пустое значение
    /// возвращается как есть.
    /// </summary>
    private static string NormalizeHost(string? server)
    {
        var temp = new ConnectionSettings();
        ConnectionSettings.ParseServerAndPort(server, temp);
        return (temp.Server ?? string.Empty).Trim();
    }

    /// <summary>
    /// Проверяет, принадлежит ли запись карты какой-либо из текущих баз. Кроме точного
    /// <see cref="MatchesBase"/> клиент-серверная база засчитывается и при совпадении
    /// одного имени базы: одна и та же база попадает в карту столько раз, сколькими
    /// написаниями хоста к ней подключались (localhost, короткое имя, FQDN, имя с портом),
    /// и её кеш не должен становиться «остатком» из-за написания, которого нет в списке.
    /// </summary>
    private static bool BelongsToAnyBase(IdConnStrEntry entry, IReadOnlyList<Infobase> bases)
    {
        foreach (var ib in bases)
        {
            if (MatchesBase(entry, ib))
                return true;

            if (entry.Settings.Type == ConnectionType.ClientServer
                && ib.Connection.Type == ConnectionType.ClientServer
                && !string.IsNullOrWhiteSpace(entry.Settings.DatabaseName)
                && string.Equals(
                    entry.Settings.DatabaseName.Trim(),
                    (ib.Connection.DatabaseName ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Приводит путь к единому виду для сравнения: прямой слэш, без завершающего слэша.
    /// </summary>
    private static string NormalizePathForCompare(string path)
    {
        var s = (path ?? string.Empty).Trim();
        if (s.Length == 0)
            return s;
        s = s.Replace('\\', '/');
        while (s.EndsWith('/'))
            s = s.Substring(0, s.Length - 1);
        return s;
    }

    /// <summary>
    /// Возвращает GUID каталогов кеша, которые платформа сопоставила строке соединения базы.
    /// </summary>
    private static IEnumerable<string> GetGuidCacheNames(Infobase infobase)
    {
        var map = LoadIdConnStrMap();
        if (map is null)
            yield break;
        foreach (var entry in map)
        {
            if (MatchesBase(entry, infobase))
                yield return entry.CacheGuid;
        }
    }

    /// <summary>
    /// Возвращает имя клиент-серверного каталога кеша вида Srvr__<сервер>__Ref__<база>__
    /// (для сервера с портом и без — два варианта имени) или пустую строку для не
    /// клиент-серверной базы.
    /// </summary>
    private static IEnumerable<string> GetClientServerDirNames(Infobase infobase)
    {
        var conn = infobase.Connection;
        if (conn.Type != ConnectionType.ClientServer)
            yield break;

        var db = SanitizeDirPart(conn.DatabaseName);
        if (string.IsNullOrWhiteSpace(db))
        {
            // Issue #109а: у клиент-серверной базы может быть пустое имя БД (строка
            // соединения без Ref=). Раньше имя каталога вообще не строилось и кэш
            // такой базы не находился; платформа в этом случае называет каталог по
            // имени базы из списка — пробуем его как запасной вариант.
            db = SanitizeDirPart(infobase.Name);
            if (string.IsNullOrWhiteSpace(db))
                yield break;
        }

        var server = SanitizeDirPart(conn.Server);
        if (!string.IsNullOrWhiteSpace(server))
            yield return $"Srvr__{server}__Ref__{db}__";

        var serverWithPort = SanitizeDirPart(conn.GetServerWithPort());
        if (!string.IsNullOrWhiteSpace(serverWithPort) &&
            !string.Equals(serverWithPort, server, StringComparison.OrdinalIgnoreCase))
            yield return $"Srvr__{serverWithPort}__Ref__{db}__";
    }

    /// <summary>
    /// Заменяет символы, недопустимые в имени каталога, на подчёркивание (как делает платформа).
    /// </summary>
    private static string SanitizeDirPart(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(IsInvalidDirChar(c) ? '_' : c);
        return sb.ToString();
    }

    private static bool IsInvalidDirChar(char c) =>
        c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
        c == '"' || c == '<' || c == '>' || c == '|';

    /// <summary>
    /// Собирает множество имён каталогов кеша, соответствующих информационной базе:
    /// GUID из карты IdConnStrMap, клиент-серверное имя Srvr__…__Ref__…__, ID базы и имя базы.
    /// </summary>
    private static IEnumerable<string> GetCacheNames(Infobase infobase)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in GetGuidCacheNames(infobase))
            set.Add(g);

        foreach (var cs in GetClientServerDirNames(infobase))
            set.Add(cs);

        // Запасные варианты: если карту не удалось прочитать, а также для баз,
        // чьи каталоги называются по ID или по имени (созданные вручную).
        if (!string.IsNullOrWhiteSpace(infobase.Id))
            set.Add(infobase.Id);

        var legacy = GetCacheName(infobase);
        if (!string.IsNullOrWhiteSpace(legacy))
            set.Add(legacy);

        return set;
    }

    /// <summary>
    /// Собирает множество «защищённых» имён каталогов кеша — имён, соответствующих текущим
    /// информационным базам (GUID из карты IdConnStrMap, имена Srvr__…__Ref__…__,
    /// ID и имена баз). Каталоги с такими именами не считаются «остатками» от удалённых
    /// баз и не подлежат автоматической очистке.
    /// </summary>
    private static HashSet<string> BuildProtectedNames(IEnumerable<Infobase> allBases)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allBases is null)
            return set;

        foreach (var ib in allBases)
        {
            if (ib is null)
                continue;
            foreach (var name in GetCacheNames(ib))
                set.Add(name);
        }

        return set;
    }

    /// <summary>
    /// Определяет, является ли имя каталога именем версии платформы (например, «8.3.24.1234»).
    /// Каталоги версий не являются каталогами кеша отдельных баз — внутри них хранятся кеши.
    /// </summary>
    private static bool IsVersionDirName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        foreach (var c in name)
        {
            if (!char.IsDigit(c) && c != '.' && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Определяет, является ли имя каталога именем кеша информационной базы: платформа
    /// называет такой каталог собственным GUID. Служебные каталоги платформы (conf, logs,
    /// ExtCompT, STT, tmplts, distr) названы словами, под правило не подходят и остатками
    /// не считаются. Имена вида Srvr__<сервер>__Ref__<база>__ здесь намеренно не
    /// распознаются: как имя каталога, создаваемого платформой, они не подтверждены,
    /// а такой каталог может принадлежать стороннему инструменту.
    /// </summary>
    private static bool IsBaseCacheDirName(string name)
        => !string.IsNullOrWhiteSpace(name) && Guid.TryParseExact(name, "D", out _);

    /// <summary>
    /// Определяет, является ли имя каталога временным именем удаления (<c><имя>.deleting_<guid></c>).
    /// Такие каталоги — остатки прерванного удаления; их не нужно ни считать остатками
    /// от удалённых баз, ни переименовывать повторно (иначе имя растёт на 41 символ за раз).
    /// </summary>
    private static bool IsDeletingName(string name) =>
        name is not null && name.Contains(".deleting_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Перечисляет каталоги кеша, не принадлежащие ни одной текущей информационной базе.
    /// Это «остатки» от удалённых из списка или созданных вне приложения баз: каталоги,
    /// имя которых не совпадает ни с одним «защищённым» именем (см. <see cref="BuildProtectedNames"/>),
    /// а в корне профиля (~/.1cv8/1C/1cv8) — каталоги из карты IdConnStrMap без совпадения
    /// по строке соединения (см. <see cref="EnumerateProfileRootOrphans"/>). Каталоги версий
    /// платформы (например, «8.3.24.1234») не удаляются — они анализируются, и удаляются
    /// только их вложенные каталоги-кеши. Временные каталоги *.deleting_* чистятся как
    /// следы оборванного удаления (issue #178).
    /// </summary>
    private static IEnumerable<string> EnumerateOrphanDirectories(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        var protectedNames = BuildProtectedNames(allBases);
        var mapGuids = GetMapCacheGuids();

        // Без карты IdConnStrMap защита баз неполна: каталог кеша живой базы называется
        // GUID, который взять больше неоткуда, и остатками стали бы каталоги всех баз
        // сразу. В этом случае остатки не ищутся вовсе.
        var map = LoadIdConnStrMap();
        if (map is null)
            yield break;

        // Каталоги, которые карта сопоставила базам из списка: они не остатки, даже если
        // написание хоста в карте и в списке разное.
        var bases = (allBases ?? Enumerable.Empty<Infobase>()).Where(b => b is not null).ToList();
        var mappedToBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in map)
        {
            if (BelongsToAnyBase(entry, bases))
                mappedToBase.Add(entry.CacheGuid);
        }

        foreach (var root in GetCacheRoots(kind, forOrphanScan: true))
        {
            if (!Directory.Exists(root))
                continue;

            // Корень профиля 1С (~/.1cv8/1C/1cv8 на Linux) рядом с кешем баз содержит
            // служебные каталоги платформы, поэтому «остатком» здесь считается только
            // каталог, который есть в карте IdConnStrMap и строка соединения которого
            // не соответствует ни одной текущей базе (issue #178). Клиент-серверные
            // каталоги Srvr__…__Ref__…__ разбираются по имени; *.deleting_* чистятся
            // как следы оборванного удаления.
            if (IsLinuxProfileCacheRoot(root))
            {
                foreach (var dir in EnumerateProfileRootOrphans(root, protectedNames, mapGuids))
                    yield return dir;
                continue;
            }

            string[] versionDirs;
            try { versionDirs = Directory.GetDirectories(root); }
            catch { continue; }

            foreach (var versionDir in versionDirs)
            {
                var versionName = Path.GetFileName(versionDir);

                if (IsDeletingName(versionName))
                {
                    // След оборванного удаления — находим и чистим (issue #178).
                    yield return versionDir;
                    continue;
                }

                if (IsVersionDirName(versionName))
                {
                    // Внутри каталога версии находятся каталоги кеша отдельных баз.
                    string[] cacheDirs;
                    try { cacheDirs = Directory.GetDirectories(versionDir); }
                    catch { continue; }

                    foreach (var cd in cacheDirs)
                    {
                        var n = Path.GetFileName(cd);
                        if (IsDeletingName(n))
                        {
                            // След оборванного удаления — находим и чистим (issue #178).
                            yield return cd;
                            continue;
                        }
                        if (protectedNames.Contains(n) || mappedToBase.Contains(n)
                            || !IsBaseCacheDirName(n))
                            continue;
                        yield return cd;
                    }
                }
                else if (!protectedNames.Contains(versionName)
                    && !mappedToBase.Contains(versionName)
                    && IsBaseCacheDirName(versionName))
                {
                    // Прямой каталог кеша в корне (без версии).
                    yield return versionDir;
                }
            }
        }
    }

    /// <summary>
    /// Проверяет, является ли путь корнем профиля 1С (~/.1cv8/1C/1cv8 на Linux), где
    /// рядом с кешем баз лежат служебные каталоги платформы. Для такого корня поиск
    /// остатков ведётся по карте IdConnStrMap, а не по жёсткому списку исключений.
    /// </summary>
    private static bool IsLinuxProfileCacheRoot(string path)
    {
#if LINUX
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return false;
        return string.Equals(
            NormalizePathForCompare(path),
            NormalizePathForCompare(Path.Combine(home, ".1cv8", "1C", "1cv8")),
            StringComparison.OrdinalIgnoreCase);
#else
        return false;
#endif
    }

    /// <summary>
    /// Возвращает множество GUID каталогов кеша из карты IdConnStrMap (1cv8u.pfl).
    /// Пустое, если карту прочитать не удалось.
    /// </summary>
    private static HashSet<string> GetMapCacheGuids()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = LoadIdConnStrMap();
        if (map is null)
            return set;
        foreach (var entry in map)
            set.Add(entry.CacheGuid);
        return set;
    }

    /// <summary>
    /// Перечисляет «остатки» кеша в корне профиля 1С (~/.1cv8/1C/1cv8 на Linux).
    /// Остатком считается каталог из карты IdConnStrMap, строка соединения которого не
    /// соответствует ни одной текущей базе (GUID нет в защищённых), либо клиент-серверный
    /// каталог Srvr__…__Ref__…__ без совпадения по имени. Служебные каталоги платформы
    /// (conf, logs, ExtCompT, STT, standalone-server и т. п.) в карте не значатся и под
    /// очистку не попадают. Каталоги *.deleting_* чистятся как следы оборванного удаления.
    /// </summary>
    private static IEnumerable<string> EnumerateProfileRootOrphans(
        string root, HashSet<string> protectedNames, HashSet<string> mapGuids)
    {
        if (!Directory.Exists(root))
            yield break;

        string[] dirs;
        try { dirs = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);

            if (IsDeletingName(name))
            {
                yield return dir;
                continue;
            }

            if (IsVersionDirName(name))
            {
                foreach (var child in EnumerateProfileRootOrphans(dir, protectedNames, mapGuids))
                    yield return child;
                continue;
            }

            var orphan = (mapGuids.Contains(name) && !protectedNames.Contains(name))
                         || (name.StartsWith("Srvr__", StringComparison.OrdinalIgnoreCase) && !protectedNames.Contains(name));
            if (orphan)
                yield return dir;
        }
    }

    /// <summary>
    /// Вычисляет суммарный размер «остатков» кеша от удалённых баз — каталогов кеша,
    /// не принадлежащих ни одной текущей информационной базе.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="allBases">Все текущие информационные базы (для определения «защищённых» имён).</param>
    /// <returns>Суммарный размер в байтах.</returns>
    public static long GetOrphanSize(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        long total = 0;
        foreach (var dir in EnumerateOrphanDirectories(kind, allBases))
            total += GetDirectorySize(dir);
        return total;
    }

    /// <summary>
    /// Удаляет «остатки» кеша от удалённых баз — каталоги кеша, не принадлежащие ни одной
    /// текущей информационной базе.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="allBases">Все текущие информационные базы (для определения «защищённых» имён).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int ClearOrphans(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        if (kind == OneCCacheKind.None)
            return 0;

        var removed = 0;
        foreach (var dir in EnumerateOrphanDirectories(kind, allBases))
        {
            if (TryDeleteDirectory(dir))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Вычисляет честный общий объём кэша указанного типа (issue #109а): сумму ВСЕХ
    /// каталогов в корнях кэша — включая каталоги, не сопоставленные ни одной базе
    /// (нестандартные имена, «осиротевший» кэш, кэши версий), и служебные файлы платформы.
    /// <para>
    /// Используется для строк «общий объём» в окне очистки кэша: прежний расчёт
    /// <see cref="GetSize(OneCCacheKind, IEnumerable{Infobase})"/> суммировал только
    /// каталоги, найденные по именам текущих баз, из-за чего окно показывало «15 МБ»
    /// при фактических 15 ГБ. Размер по базам считается отдельно (см. перегрузки
    /// <see cref="GetSize"/>).
    /// </para>
    /// Корни дедуплицируются канонизацией путей: junction/symlink-дубликаты корней
    /// (в т. ч. перенесённый на другой диск кэш, issue #109б) считаются один раз.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <returns>Суммарный размер в байтах.</returns>
    public static long GetRootTotalSize(OneCCacheKind kind)
    {
        if (kind == OneCCacheKind.None)
            return 0;

        long total = 0;
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetCacheRoots(kind))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            // Два корня, ведущие в один каталог (например, junction и его цель),
            // дают один и тот же канонический путь — считаем только первый.
            if (!seenRoots.Add(ResolveCanonicalPath(root)))
                continue;

            // Свежее множество visited на корень: рекурсия внутри корня защищена
            // своим набором (циклы через ссылки внутри цели перенесённого кэша).
            total += GetDirectorySize(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return total;
    }

    /// <summary>
    /// Вычисляет суммарный размер кеша указанного типа (программного и/или пользовательского)
    /// по каталогам, принадлежащим текущим информационным базам. Служебные файлы платформы
    /// в корнях кеша (helpsynt.dat, логи и т. п.) не учитываются.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="infobases">Все текущие информационные базы.</param>
    /// <returns>Суммарный размер в байтах.</returns>
    public static long GetSize(OneCCacheKind kind, IEnumerable<Infobase> infobases)
    {
        if (kind == OneCCacheKind.None || infobases is null)
            return 0;

        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ib in infobases)
        {
            if (ib is null)
                continue;
            foreach (var dir in EnumerateCacheDirectories(ib, kind))
            {
                if (seen.Add(dir))
                    total += GetDirectorySize(dir);
            }
        }

        return total;
    }

    /// <summary>
    /// Вычисляет суммарный размер кеша указанного типа для одной информационной базы.
    /// Учитываются только реально существующие каталоги кеша этой базы.
    /// </summary>
    /// <param name="infobase">Информационная база, для которой вычисляется размер кеша.</param>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <returns>Суммарный размер кеша базы в байтах.</returns>
    public static long GetSize(Infobase infobase, OneCCacheKind kind)
    {
        if (infobase is null || kind == OneCCacheKind.None)
            return 0;

        long total = 0;
        var dirs = EnumerateCacheDirectories(infobase, kind).Distinct().ToList();
        // Диагностика «нулей» (issue #109): при включённой категории CM_CACHE журнал
        // показывает, какие корни проверялись и сколько каталогов кеша найдено —
        // чтобы по diagnostics-ZIP сразу было видно, где искали и почему 0.
        Log.Info(LogCategory.Cache,
            $"CACHE size «{infobase.Name}» kind={kind}: найдено каталогов={dirs.Count}, " +
            $"корни=[{string.Join("; ", GetCacheRoots(kind))}]");
        foreach (var dir in dirs)
            total += GetDirectorySize(dir);

        return total;
    }

    /// <summary>
    /// Перечисляет реально существующие каталоги кеша указанной информационной базы
    /// для заданного типа кеша. Имена каталогов берутся из карты IdConnStrMap (GUID
    /// платформы), клиент-серверных имён Srvr__…__Ref__…__, а также ID и имени базы.
    /// Каталоги ищутся и непосредственно в корне кеша, и в подкаталогах версий
    /// (1cv8\<версия>\<имя>) — включая вложенные каталоги версий глубже одного уровня
    /// (issue #109а: обход поддерева версий рекурсивно).
    /// </summary>
    private static IEnumerable<string> EnumerateCacheDirectories(Infobase infobase, OneCCacheKind kind)
    {
        var names = GetCacheNames(infobase).ToList();
        if (names.Count == 0)
            yield break;

        foreach (var root in GetCacheRoots(kind))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var dir in EnumerateCacheDirsUnder(root, names, CacheDirScanDepth))
                yield return dir;
        }
    }

    /// <summary>
    /// Глубина рекурсивного обхода поддерева версий при поиске каталогов кеша
    /// (1cv8\<версия>\<имя>; вложенные версии 1cv8\<версия>\<версия-подкаталог>\<имя>
    /// и глубже — issue #109а). Ограничение защищает от бесконечной рекурсии по циклам
    /// ссылок: реальная раскладка кеша 1С не превышает трёх-четырёх уровней.
    /// </summary>
    private const int CacheDirScanDepth = 4;

    /// <summary>
    /// Обход поддерева кеша под <paramref name="root"/>: прямые каталоги с именами из
    /// <paramref name="names"/> считаются каталогами кеша, каталоги версий (имя — версия
    /// платформы) обходятся рекурсивно, остальное игнорируется.
    /// </summary>
    private static IEnumerable<string> EnumerateCacheDirsUnder(string root, List<string> names, int depth)
    {
        string[] subDirs;
        try { subDirs = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var sub in subDirs)
        {
            var name = Path.GetFileName(sub);

            // Сначала проверяем совпадение с именами каталогов кеша: GUID-имена каталогов
            // баз состоят только из цифр и дефисов и формально подходят под правило
            // IsVersionDirName — проверка версии до совпадения имён сделала бы каталоги
            // баз в корне ненайденными (регресс старого теста issue #109/#110).
            foreach (var n in names)
            {
                if (string.Equals(name, n, StringComparison.OrdinalIgnoreCase))
                    yield return sub;
            }

            if (!IsVersionDirName(name))
                continue;

            // Каталог версии: внутри него лежат кеши баз (и, возможно, вложенные версии).
            if (depth > 0)
            {
                foreach (var nested in EnumerateCacheDirsUnder(sub, names, depth - 1))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Рекурсивно вычисляет суммарный размер всех файлов в каталоге (в байтах).
    /// Ошибки доступа к отдельным файлам игнорируются.
    /// <para>
    /// Рекурсия выполняется вручную, а не через <c>SearchOption.AllDirectories</c>:
    /// встроенная рекурсия .NET не заходит в reparse points (junction/symlink), из-за
    /// чего размер кэша, перенесённого на другой диск ссылкой (issue #109), считался
    /// нулевым. Здесь ссылки обходятся по их цели; защита от циклов — множество
    /// уже посещённых полных путей.
    /// </para>
    /// </summary>
    /// <remarks>internal (не private): юнит-тесты junction/symlink (issue #109) в Workspace1C.Tests.</remarks>
    internal static long GetDirectorySize(string path)
        => GetDirectorySize(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static long GetDirectorySize(string path, HashSet<string> visited)
    {
        long total = 0;
        try
        {
            if (!visited.Add(ResolveCanonicalPath(path)))
                return 0;

            foreach (var file in Directory.EnumerateFiles(path, "*"))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Игнорируем недоступные файлы (могут быть заняты запущенной 1С).
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*"))
            {
                try
                {
                    total += GetDirectorySize(dir, visited);
                }
                catch
                {
                    // Игнорируем недоступные подкаталоги.
                }
            }
        }
        catch
        {
            // Игнорируем ошибки перечисления (каталог может исчезнуть или быть недоступен).
        }

        return total;
    }

    /// <summary>
    /// Канонический путь каталога для защиты от циклов при обходе ссылок: для reparse
    /// points (junction/symlink) возвращается финальная цель ссылки, иначе — сам путь.
    /// Ссылки могут достигаться по разным путям («real\sub» и «link\sub», где link —
    /// ссылка на real), и без канонизации цикл не распознаётся (issue #109).
    /// </summary>
    private static string ResolveCanonicalPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var final = info.ResolveLinkTarget(returnFinalTarget: true);
                if (final is not null && !string.IsNullOrEmpty(final.FullName))
                    return NormalizePathForCompare(final.FullName);
            }
        }
        catch
        {
            // Недоступный каталог — используем путь как есть.
        }

        return NormalizePathForCompare(path);
    }

    /// <summary>
    /// Возвращает корневые каталоги, где 1С хранит кеш, с учётом выбранного типа кеша.
    /// </summary>
    /// <param name="forOrphanScan">
    /// True, если корни запрашиваются для поиска «осиротевшего» кеша. В этом режиме
    /// корень профиля ~/.1cv8/1C/1cv8 отдаётся всегда, но остатки в нём определяются
    /// по карте IdConnStrMap (см. <see cref="EnumerateProfileRootOrphans"/>), поэтому
    /// служебные каталоги платформы под очистку не попадают (issue #178).
    /// </param>
    /// <remarks>internal (не private): юнит-тесты доп. корней и легаси 1Cv82 (issue #109)
    /// в Workspace1C.Tests проверяют состав корней.</remarks>
    internal static IEnumerable<string> GetCacheRoots(OneCCacheKind kind, bool forOrphanScan = false)
    {
        var roots = new List<string>();

#if LINUX
        // Linux: пользовательский кеш баз платформа держит в ~/.1cv8/1C/1cv8,
        // каталоги баз по GUID лежат прямо в нём (проверено на 8.3.27).
        // ~/.cache/1cv8 и ~/.local/share/1cv8 это данные встроенного браузера,
        // а не кеш баз; оставлены как были, вместе с прежним ~/.1cv8/1cv8
        // на случай других раскладок дистрибутива.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (kind.HasFlag(OneCCacheKind.Program))
        {
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCache))
                roots.Add(Path.Combine(xdgCache, "1cv8"));
            else if (!string.IsNullOrEmpty(home))
                roots.Add(Path.Combine(home, ".cache", "1cv8"));
        }

        if (kind.HasFlag(OneCCacheKind.User))
        {
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgData))
                roots.Add(Path.Combine(xdgData, "1cv8"));
            else if (!string.IsNullOrEmpty(home))
                roots.Add(Path.Combine(home, ".local", "share", "1cv8"));
            // Общая для всех версий каталог кэша в профиле 1С.
            if (!string.IsNullOrEmpty(home))
            {
                // В ~/.1cv8/1C/1cv8 рядом с кешем баз платформа держит свои служебные
                // каталоги (conf, logs, ExtCompT, STT, standalone-server), а каталоги
                // кеша серверных баз называются Srvr__<сервер>__Ref__<база>__. Поиск
                // осиротевшего кеша в этом корне ведётся не по именам, а по карте
                // IdConnStrMap (см. EnumerateProfileRootOrphans): остатком считается
                // только каталог удалённой базы из карты, поэтому корень включается
                // в скан остатков (issue #178), а служебные каталоги под очистку
                // не попадают.
                roots.Add(Path.Combine(home, ".1cv8", "1C", "1cv8"));
                roots.Add(Path.Combine(home, ".1cv8", "1cv8"));
            }
        }
#else
        // Программный кеш — %LOCALAPPDATA%\1C\1cv8.
        if (kind.HasFlag(OneCCacheKind.Program))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                roots.Add(Path.Combine(localAppData, "1C", "1cv8"));
        }

        // Пользовательский кеш — %APPDATA%\1C\1cv8.
        if (kind.HasFlag(OneCCacheKind.User))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                roots.Add(Path.Combine(appData, "1C", "1cv8"));
                // Легаси-корень платформы 8.2 (issue #109): часть старых установок
                // держит кеш в 1Cv82.
                roots.Add(Path.Combine(appData, "1C", "1Cv82"));
            }
        }

        // Легаси-корень платформы 8.2 для программного кеша (issue #109).
        if (kind.HasFlag(OneCCacheKind.Program))
        {
            var localAppDataLegacy = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppDataLegacy))
                roots.Add(Path.Combine(localAppDataLegacy, "1C", "1Cv82"));
        }
#endif

        // Реестровые параметры переноса кэша 1С и переменная среды 1SCACHE (issue #109б,
        // Windows): кэш, перенесённый на другой диск штатными средствами платформы
        // (параметры в HKCU/HKLM Software\1C\1Cv8) или через 1SCACHE, раньше не был виден
        // приложению. Найденные пути — дополнение к AdditionalCacheRoots, не замена;
        // дубликаты отфильтровываются канонизацией в GetRootTotalSize и Distinct при
        // перечислении каталогов.
#if WINDOWS
        if (kind != OneCCacheKind.None)
        {
            foreach (var registryRoot in GetRegistryCacheRoots())
                roots.Add(registryRoot);

            // Переменная среды 1SCACHE — официально поддерживаемый платформой способ
            // переноса каталога кэша (значение — полный путь к каталогу кэша).
            var envCache = Environment.GetEnvironmentVariable("1SCACHE");
            if (!string.IsNullOrWhiteSpace(envCache))
                roots.Add(envCache.Trim());
        }
#endif

        // Дополнительные корни из настроек пользователя (issue #109): ищем в них кеш
        // любого типа — пользователь объявил каталог как корень кеша 1С целиком.
        if (kind != OneCCacheKind.None)
        {
            string[] additional;
            lock (s_additionalRootsLock)
            {
                additional = s_additionalRoots.ToArray();
            }
            foreach (var root in additional)
                roots.Add(root);
        }

        return roots;
    }

#if WINDOWS
    // Кэш реестровых корней на сеанс: реестр читается один раз (issue #109б).
    private static readonly object s_registryRootsLock = new();
    private static IReadOnlyList<string>? s_registryRootsCache;

    /// <summary>
    /// Базовые ветки реестра, в которых 1С хранит параметры расположения кэша.
    /// Перебираются варианты написания с «cv8» и «Cv8», как в OneCTemplateService.
    /// </summary>
    private static readonly string[] RegistryBaseKeys =
    {
        @"Software\1C\1cv8",
        @"Software\1C\1Cv8",
        @"Software\1C\1cestart"
    };

    /// <summary>
    /// Возвращает корни кэша из реестровых параметров переноса 1С (issue #109б, Windows).
    /// Считываются строковые значения веток HKCU и HKLM Software\1C\1cv8|1Cv8 (включая
    /// подветки версий 8.3…, Common и 1cestart), имя которых говорит о расположении кэша
    /// (CacheLocation, CacheDir и т. п.). Все строковые значения веток дампятся в журнал
    /// CM_CACHE — по diagnostics-ZIP видно, что нашлось и почему путь не распознан.
    /// Консервативно: значение добавляется как корень, только если это абсолютный путь
    /// Windows; результат кэшируется на сеанс.
    /// </summary>
    /// <remarks>internal (не private): юнит-тесты кэширования на сеанс в Workspace1C.Tests.</remarks>
    internal static IReadOnlyList<string> GetRegistryCacheRoots()
    {
        lock (s_registryRootsLock)
        {
            if (s_registryRootsCache is not null)
                return s_registryRootsCache;

            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var basePath in RegistryBaseKeys)
            foreach (var sub in EnumerateRegistrySubKeys(hive, basePath, RegistryScanDepth))
            {
                try
                {
                    using var key = hive.OpenSubKey(sub);
                    if (key is null)
                        continue;

                    foreach (var name in key.GetValueNames())
                    {
                        if (key.GetValue(name) is not string value || string.IsNullOrWhiteSpace(value))
                            continue;

                        // Диагностика (issue #109б): дамп всех строковых значений веток 1С.
                        Log.Info(LogCategory.Cache, $"CACHE registry: {sub}\\{name} = {value}");

                        if (!IsCacheLocationValueName(name))
                            continue;

                        var dir = NormalizeRegistryDir(value);
                        if (dir is null)
                        {
                            Log.Info(LogCategory.Cache,
                                $"CACHE registry: значение «{name}» не похоже на каталог, пропущено: {value}");
                            continue;
                        }

                        // Дедупликация: один путь из разных веток/с разным регистром.
                        if (!seen.Add(NormalizePathForCompare(dir)))
                            continue;

                        found.Add(dir);
                        Log.Info(LogCategory.Cache,
                            $"CACHE registry root добавлен: {dir} (из {sub}\\{name})");
                    }
                }
                catch
                {
                    // Ключ недоступен (права, выгрузка на лету) — пропускаем.
                }
            }

            s_registryRootsCache = found;
            return s_registryRootsCache;
        }
    }

    /// <summary>
    /// Глубина обхода подветок реестра: базовая ветка + версии 8.3.x.x / Common / 1cestart
    /// и их подветки — реестровые параметры переноса кэша лежат не глубже.
    /// </summary>
    private const int RegistryScanDepth = 2;

    /// <summary>
    /// Перечисляет <paramref name="path"/> и его подветки до глубины <paramref name="depth"/>
    /// (включительно). Отсутствующие/недоступные ветки пропускаются.
    /// </summary>
    private static IEnumerable<string> EnumerateRegistrySubKeys(RegistryKey hive, string path, int depth)
    {
        // Материализуем список: yield запрещён в блоке try с catch, а ветка реестра
        // может исчезнуть/быть недоступна в любой момент — ошибки пропускаются.
        var results = new List<string>();
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null)
                return results;

            results.Add(path);

            if (depth > 0)
            {
                foreach (var child in key.GetSubKeyNames())
                {
                    results.AddRange(EnumerateRegistrySubKeys(hive, path + "\\" + child, depth - 1));
                }
            }
        }
        catch
        {
            // Недоступная ветка — отдаём то, что успели собрать.
        }

        return results;
    }
#endif

    /// <summary>
    /// Определяет, является ли имя реестрового значения параметром расположения кэша
    /// (issue #109б). Консервативно: имя должно явно говорить о кэше — CacheLocation,
    /// CacheDir, CachedDataLocation и т. п. Прочие значения (InstalledLocation,
    /// DistributionLocation, DefaultVersion и т. д.) корнями кэша не считаются, чтобы
    /// не очистить лишнее.
    /// </summary>
    /// <remarks>internal: юнит-тесты фильтра имён (без реестра) в Workspace1C.Tests.</remarks>
    internal static bool IsCacheLocationValueName(string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           name.Contains("cache", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Нормализует значение реестрового параметра в путь каталога кэша или null, если
    /// значение не похоже на абсолютный путь Windows (issue #109б, консервативное
    /// поведение: не распознано — не добавляем в корни очистки).
    /// </summary>
    /// <remarks>internal: юнит-тесты нормализации значений в Workspace1C.Tests.</remarks>
    internal static string? NormalizeRegistryDir(string? value)
    {
        var dir = value?.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        dir = dir.TrimEnd('\\', '/');
        if (dir.Length == 0)
            return null;

        // Абсолютный путь Windows: «X:\…», «X:\» или UNC «\\server\share\…».
        if (dir.Length == 2 && dir[1] == ':')
            return dir + Path.DirectorySeparatorChar;
        if (dir.Length > 2 && dir[1] == ':')
            return dir;
        if (dir.StartsWith(@"\\", StringComparison.Ordinal) && dir.Length > 2)
            return dir;

        return null;
    }

    /// <summary>
    /// Удаляет каталог кеша. Каталог сначала переименовывается во временное имя
    /// (<c><имя>.deleting_<guid></c>), у всех файлов рекурсивно снимается атрибут
    /// ReadOnly (платформа кладёт в кеш файлы с этим атрибутом, из-за которого обычное
    /// рекурсивное удаление падает с UnauthorizedAccessException), после чего каталог удаляется.
    /// <para>
    /// Если каталог — ссылка (junction/symlink, issue #109: пользователь перенёс кэш 1С
    /// на другой диск через <c>mklink /j</c>), ссылка НЕ переименовывается и НЕ удаляется —
    /// очищается только содержимое её цели. Иначе пользователь потерял бы ссылку и весь
    /// перенесённый кэш перестал бы быть виден ни платформе, ни приложению.
    /// </para>
    /// </summary>
    /// <returns>True, если каталог был найден и удалён (или очищен как ссылка); иначе False.</returns>
    /// <remarks>internal (не private): юнит-тесты junction/symlink (issue #109) в Workspace1C.Tests.</remarks>
    internal static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            // Ссылка (junction/symlink) не переименовывается и не удаляется — удаляем
            // только содержимое цели (issue #109). Вызывается для каталогов кеша баз,
            // которые по построению не совпадают с корнями кэша (GetCacheRoots),
            // поэтому сами корни со ссылками здесь недостижимы.
            if (IsReparsePoint(path))
                return TryDeleteDirectoryContents(path);

            // Переименовываем каталог во временное имя — это мгновенная операция,
            // и если удаление прервётся, каталог больше не будет найден как кеш базы.
            var tempPath = path + ".deleting_" + Guid.NewGuid().ToString("N");
            Directory.Move(path, tempPath);

            ClearReadOnlyAttributes(tempPath);

            Directory.Delete(tempPath, recursive: true);
            return true;
        }
        catch
        {
            // Игнорируем ошибки (каталог может быть занят запущенной 1С или
            // отсутствовать); считаем, что каталог не удалён.
            return false;
        }
    }

    /// <summary>
    /// Определяет, является ли каталог ссылкой (junction/symlink/reparse point).
    /// </summary>
    /// <remarks>internal (не private): юнит-тесты junction/symlink (issue #109) в Workspace1C.Tests.</remarks>
    internal static bool IsReparsePoint(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.LinkTarget is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Очищает содержимое каталога-ссылки (issue #109): удаляет все вложенные файлы
    /// и каталоги, не затрагивая саму ссылку. Вложенные ссылки удаляются как ссылки
    /// (цель не затрагивается), обычные каталоги — рекурсивно.
    /// </summary>
    private static bool TryDeleteDirectoryContents(string path)
    {
        try
        {
            string[] children;
            try { children = Directory.GetFileSystemEntries(path); }
            catch { return false; }

            foreach (var child in children)
                TryDeleteAnyEntry(child);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Удаляет файл, каталог или ссылку. Ссылка (junction/symlink) удаляется как ссылка —
    /// её цель не затрагивается; обычный каталог удаляется рекурсивно со снятием ReadOnly.
    /// </summary>
    private static void TryDeleteAnyEntry(string path)
    {
        try
        {
            if (File.Exists(path) && !Directory.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                File.Delete(path);
                return;
            }

            if (Directory.Exists(path))
            {
                if (IsReparsePoint(path))
                {
                    // Directory.Delete без recursive удаляет только саму ссылку.
                    Directory.Delete(path, recursive: false);
                    return;
                }

                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Игнорируем ошибки (элемент может быть занят запущенной 1С).
        }
    }

    /// <summary>
    /// Рекурсивно снимает атрибут ReadOnly со всех файлов и каталогов внутри <paramref name="path"/>.
    /// </summary>
    private static void ClearReadOnlyAttributes(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* Игнорируем отдельные недоступные файлы. */ }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attributes = File.GetAttributes(dir);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
                }
                catch { /* Игнорируем отдельные недоступные каталоги. */ }
            }
        }
        catch
        {
            // Игнорируем ошибки перечисления.
        }
    }
}