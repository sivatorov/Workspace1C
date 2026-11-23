using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика сопоставления командной строки процесса 1С с информационной базой
/// (индикатор «база сейчас запущена»). Не зависит от платформы и UI — покрыта
/// юнит-тестами. Ключи подключения: файловая — <c>/F &lt;каталог&gt;</c>,
/// клиент-серверная — <c>/S &lt;сервер&gt;\&lt;база&gt;</c>; после ключа путь может
/// идти через пробел или без него в кавычках (<c>/F"C:\Bases\ib"</c>). Конфигуратор
/// запускает клиент серверной базы в формате <c>/S"сервер/база"</c> — без пробела
/// и с прямыми слешами (issue #103), такой формат тоже распознаётся.
/// Веб-базы не сопоставляются в <see cref="MatchesCommandLine"/> (ключ /WS встречается
/// и у прочих сценариев), но инспектор процессов использует <see cref="FindOwner"/>
/// и сопоставляет их по /WS/WS= (issue #140, 1.0.1.84).
/// </summary>
public static class RunningInfobaseMatcher
{
    /// <summary>
    /// Сопоставляет базу с командной строкой процесса 1С: сперва прямые ключи /F и /S,
    /// затем — строка соединения /IBConnectionString (issue #103: запуск отладки
    /// серверной базы из Конфигуратора передаёт клиенту полную строку соединения).
    /// Прямые ключи имеют приоритет над /IBConnectionString.
    /// </summary>
    public static bool MatchesCommandLine(Infobase infobase, string? commandLine)
    {
        if (infobase?.Connection is null || string.IsNullOrWhiteSpace(commandLine))
            return false;

        return infobase.Connection.Type switch
        {
            ConnectionType.File => MatchesValue(commandLine, "F", infobase.Connection.FilePath ?? "")
                || MatchesIbConnectionString(infobase, commandLine),
            ConnectionType.ClientServer => MatchesServerValue(
                    commandLine,
                    infobase.Connection.Server ?? "",
                    infobase.Connection.DatabaseName ?? "")
                // issue #134 (1.0.1.80): у базы, добавленной только по адресу сервера,
                // DatabaseName может быть пустым — тогда имя базы в кластере берём из
                // названия карточки (сравнение по серверу сохраняется).
                || (string.IsNullOrWhiteSpace(infobase.Connection.DatabaseName)
                    && MatchesServerNameValue(
                        commandLine,
                        infobase.Connection.Server ?? "",
                        infobase.Name))
                || MatchesIbConnectionString(infobase, commandLine),
            _ => false
        };
    }

    /// <summary>
    /// Issue #140 (1.0.1.84): владелец командной строки для инспектора процессов.
    /// Объединяет все проходы сопоставления:
    /// <list type="number">
    /// <item>прямые ключи <c>/F</c>, <c>/S</c>, <c>/IBConnectionString</c> и веб-ключ
    /// <c>/WS</c>/<c>WS=</c>;</item>
    /// <item><c>/IBName</c> — только если имя уникально в списке баз (Name или
    /// DatabaseName);</item>
    /// <item>файловый fallback по уникальному последнему сегменту пути (<c>/F</c> или
    /// <c>File=</c>) — покрывает UNC ↔ смонтированный диск и короткие имена.</item>
    /// </list>
    /// Возвращает null, если база не определена. В отличие от
    /// <see cref="MatchesCommandLine"/>, веб-базы сопоставляются — это нужно инспектору,
    /// но не индикатору запуска (там поведение сохранено).
    /// </summary>
    public static Infobase? FindOwner(IEnumerable<Infobase> infobases, string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var bases = (infobases ?? Enumerable.Empty<Infobase>())
            .Where(ib => ib?.Connection is not null)
            .ToList();
        if (bases.Count == 0)
            return null;

        // Проход 1: прямые ключи. Сначала обычные подключения, затем веб-базы.
        var owner = bases.FirstOrDefault(ib => MatchesCommandLine(ib, commandLine));
        if (owner is null)
            owner = bases.FirstOrDefault(ib => MatchesWebBase(ib, commandLine));
        if (owner is not null)
        {
            if (RunningMatchDebug.IsEnabled)
                RunningMatchDebug.Log($"  find: direct match -> id={owner.Id} name=\"{owner.Name}\"");
            return owner;
        }

        // Проход 2: /IBName — только при уникальном имени в списке баз.
        if (TryExtractValue(commandLine, "IBName", out var rawName))
        {
            var name = NormalizeName(rawName);
            if (name.Length > 0)
            {
                var nameCandidates = new Dictionary<string, List<Infobase>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ib in bases)
                {
                    AddNameCandidate(nameCandidates, NormalizeName(ib.Name), ib);
                    AddNameCandidate(nameCandidates, NormalizeName(ib.Connection.DatabaseName), ib);
                }

                if (nameCandidates.TryGetValue(name, out var candidates) && candidates.Count == 1)
                {
                    if (RunningMatchDebug.IsEnabled)
                        RunningMatchDebug.Log($"  find: /IBName \"{name}\" -> id={candidates[0].Id}");
                    return candidates[0];
                }

                if (RunningMatchDebug.IsEnabled)
                {
                    RunningMatchDebug.Log(
                        $"  find: /IBName \"{name}\" skipped — " +
                        (candidates is null
                            ? "no candidate"
                            : $"ambiguous ({candidates.Count} candidates)"));
                }
            }
        }

        // Проход 3: файловый fallback по уникальному последнему сегменту пути.
        var commandPath = ExtractCommandFilePath(commandLine);
        var leaf = GetPathLeaf(commandPath);
        if (leaf.Length > 0)
        {
            var candidates = bases
                .Where(ib => ib.Connection.Type == ConnectionType.File)
                .Where(ib => PathLeafEquals(ib.Connection.FilePath, leaf))
                .ToList();
            if (candidates.Count == 1)
            {
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  find: file leaf \"{leaf}\" -> id={candidates[0].Id}");
                return candidates[0];
            }
            if (candidates.Count > 1 && RunningMatchDebug.IsEnabled)
                RunningMatchDebug.Log($"  find: file leaf \"{leaf}\" ambiguous ({candidates.Count} candidates)");
        }

        // issue #140 (1.0.1.84): итоговая запись — на чужой машине по логу видно,
        // что ни один проход не дал владельца (а не что диагностика не включена).
        if (RunningMatchDebug.IsEnabled)
            RunningMatchDebug.Log("  find: no match");
        return null;
    }

    /// <summary>
    /// Все базы списка, чьё подключение совпадает с командной строкой процесса
    /// (issue #153). Функция оставлена ТОЛЬКО ДЛЯ ДИАГНОСТИКИ (инспектор процессов,
    /// журнал CM_DEBUG_RUNNING_MATCH): индикатор запуска больше не использует её
    /// напрямую — один процесс 1С не может быть запущен под несколькими учётными
    /// записями одновременно, поэтому <see cref="MatchProcesses"/> выбирает ровно
    /// одну запись через <see cref="SelectOwnerForProcess"/>.
    /// Одна и та же база может быть заведена в списке несколько раз под разными
    /// учётными записями (например, «РАБОЧАЯ БАЗА» с авторизацией ОС и
    /// «РАБОЧАЯ БАЗА - АДМИН» с именем/паролем). Проверяются прямые ключи
    /// <c>/F</c>, <c>/S</c>, <c>/IBConnectionString</c> и веб-ключ <c>/WS</c>;
    /// fallback <c>/IBName</c> и файловый хвост пути выполняет вызывающий через
    /// <see cref="FindOwner"/>.
    /// </summary>
    public static IReadOnlyList<Infobase> FindDirectOwners(
        IEnumerable<Infobase> infobases, string? commandLine) =>
        FindDirectOwnerCandidates(infobases, commandLine);

    /// <summary>
    /// Кандидаты прямого сопоставления (issue #153): все записи с тем же подключением,
    /// без выбора владельца. Внутренний список для <see cref="SelectOwnerForProcess"/>;
    /// дубликаты по ключу базы отбрасываются.
    /// </summary>
    private static List<Infobase> FindDirectOwnerCandidates(
        IEnumerable<Infobase> infobases, string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return new List<Infobase>();

        var bases = (infobases ?? Enumerable.Empty<Infobase>())
            .Where(ib => ib?.Connection is not null)
            .ToList();
        if (bases.Count == 0)
            return new List<Infobase>();

        var owners = bases.Where(ib => MatchesCommandLine(ib, commandLine)).ToList();
        if (owners.Count == 0)
            owners = bases.Where(ib => MatchesWebBase(ib, commandLine)).ToList();
        if (owners.Count <= 1)
            return owners;

        // Одна и та же запись, добавленная в список дважды, не должна давать два
        // одинаковых совпадения: ключ базы (Id либо подпись подключения) уникален.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return owners.Where(ib => seen.Add(GetBaseKey(ib))).ToList();
    }

    /// <summary>
    /// Issue #153: выбирает РОВНО ОДНУ запись списка, под учётными данными которой
    /// запущен процесс 1С. Один процесс не может одновременно работать под разными
    /// пользователями, поэтому возвращать несколько владельцев нельзя: иначе
    /// индикатор запуска появляется у всех записей с общим подключением, двойной
    /// щелчок активирует чужое окно, а вкладка «Запущенные» показывает дубли.
    /// <para>Правила:</para>
    /// <list type="number">
    /// <item>параметр <c>/N</c> задан — выбираются записи, у которых эффективный
    /// пользователь Предприятия совпадает без учёта регистра (trim/кавычки);
    /// если совпала одна — она, если несколько — первая по ключу базы;</item>
    /// <item><c>/N</c> нет — выбираются записи с авторизацией ОС
    /// (<see cref="AuthenticationMode.Windows"/>/<see cref="ConnectionSettings.UseOsAuthentication"/>)
    /// или без явных учётных данных; если одна — она, иначе первая по ключу;</item>
    /// <item>если <c>/N</c> есть, но ни одна запись не задаёт такого пользователя
    /// явно (сценарий «Запрос»), детерминированно предпочитается запись без явных
    /// учётных данных, иначе первая по ключу базы.</item>
    /// </list>
    /// <paramref name="resolveEnterpriseUser"/> позволяет подставить общую
    /// авторизацию из настроек (делегат передаёт <see cref="ViewModels.MainViewModel"/>);
    /// без делегата используется чистый <see cref="InfobaseAuthResolver"/>.
    /// </summary>
    public static Infobase? SelectOwnerForProcess(
        IReadOnlyList<Infobase> candidates,
        string? commandLine,
        Func<Infobase, string?>? resolveEnterpriseUser = null)
    {
        if (candidates is null || candidates.Count == 0)
            return null;

        var resolveUser = resolveEnterpriseUser ?? ResolveEnterpriseUserDefault;

        // Детерминированный порядок: ключ базы. Дубликаты одной записи не дают
        // неоднозначности выбора.
        var ordered = candidates
            .Where(ib => ib?.Connection is not null)
            .GroupBy(GetBaseKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(GetBaseKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count == 0)
            return null;
        if (ordered.Count == 1)
            return ordered[0];

        var processUser = NormalizeAuthUser(ProcessCommandLineUserParser.ExtractUserName(commandLine));

        if (processUser.Length > 0)
        {
            var explicitMatches = ordered
                .Where(ib => string.Equals(
                    NormalizeAuthUser(resolveUser(ib)), processUser, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (explicitMatches.Count > 0)
                return explicitMatches[0];

            // /N есть, но запись не задаёт этого пользователя явно — значит, имя
            // введено в окне «Запрос». Предпочитаем запись без явных учётных данных.
            var promptCandidates = ordered
                .Where(ib => NormalizeAuthUser(resolveUser(ib)).Length == 0)
                .ToList();
            return promptCandidates.Count > 0 ? promptCandidates[0] : ordered[0];
        }

        // /N нет — вход по авторизации ОС либо запрос без явных учётных данных.
        var osCandidates = ordered
            .Where(ib => NormalizeAuthUser(resolveUser(ib)).Length == 0)
            .ToList();
        return osCandidates.Count > 0 ? osCandidates[0] : ordered[0];
    }

    /// <summary>
    /// Совпадает ли учётная запись процесса с учётной записью записи списка
    /// (issue #153): используется при перечислении окон, чтобы окно второй записи
    /// с тем же подключением не активировалось по первой. Пустой <c>/N</c>
    /// сопоставляется только с записью без явных учётных данных (ОС/«Запрос»).
    /// </summary>
    public static bool MatchesProcessAccount(
        Infobase infobase,
        string? commandLine,
        Func<Infobase, string?>? resolveEnterpriseUser = null)
    {
        if (infobase?.Connection is null)
            return false;

        var resolveUser = resolveEnterpriseUser ?? ResolveEnterpriseUserDefault;
        var processUser = NormalizeAuthUser(ProcessCommandLineUserParser.ExtractUserName(commandLine));
        var baseUser = NormalizeAuthUser(resolveUser(infobase));
        return string.Equals(processUser, baseUser, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Эффективный пользователь «1С:Предприятия» без обращения к статическим
    /// настройкам приложения: только собственная авторизация базы/режима.
    /// Вызывающий с общими настройками передаёт делегат (issue #153).
    /// </summary>
    private static string? ResolveEnterpriseUserDefault(Infobase infobase)
    {
        InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Enterprise,
            out _, out var user, out _, null);
        return user;
    }

    /// <summary>Нормализация логина: trim, без обрамляющих кавычек.</summary>
    private static string NormalizeAuthUser(string? user) =>
        (user ?? string.Empty).Trim().Trim('"').Trim();

    /// <summary>
    /// Путь из командной строки для файлового fallback (issue #140): прямой ключ
    /// <c>/F</c>, затем <c>File=</c> из <c>/IBConnectionString</c>.
    /// </summary>
    private static string ExtractCommandFilePath(string commandLine)
    {
        if (TryExtractValue(commandLine, "F", out var direct) && direct.Length > 0)
            return direct;

        if (TryExtractValue(commandLine, "IBConnectionString", out var connectionString))
        {
            var parts = ParseConnectionString(connectionString);
            if (parts.TryGetValue("File", out var file) && !string.IsNullOrWhiteSpace(file))
                return file;
        }

        return string.Empty;
    }

    /// <summary>
    /// Ключ базы для словарей сопоставления (issue #134, 1.0.1.80). Раньше матчер и план
    /// подменю требовали непустой <see cref="Infobase.Id"/> и молча пропускали остальные
    /// базы: у импортированных из кластера баз Id пуст, поэтому они не получали ни
    /// индикатор запуска, ни пункты в подменю, ни имена в сводке завершения процессов.
    /// Теперь ключ строится из Id, а при его отсутствии — из подписи подключения,
    /// поэтому такие базы участвуют в сопоставлении на равных.
    /// </summary>
    public static string GetBaseKey(Infobase infobase)
    {
        if (infobase is null)
            throw new ArgumentNullException(nameof(infobase));

        var id = (infobase.Id ?? string.Empty).Trim();
        if (id.Length > 0)
            return id;

        var connection = infobase.Connection;
        if (connection is null)
            return "name:" + NormalizeName(infobase.Name);

        return connection.Type switch
        {
            ConnectionType.File => "file:" + NormalizePath(connection.FilePath ?? ""),
            ConnectionType.ClientServer =>
                "srv:" + NormalizeServerHost(connection.Server ?? "")
                + "\\" + (connection.DatabaseName ?? string.Empty).Trim()
                + "|" + NormalizeName(infobase.Name),
            _ => connection.Type + ":" + (connection.ToConnectionString() ?? string.Empty)
        };
    }

    /// <summary>
    /// Issue #103 (v1.0.1.49): есть ли в командной строке ключ отладки <c>/DEBUG</c>.
    /// Ключ без значения; должен стоять отдельным токеном (начало строки или
    /// разделитель перед ним) и не быть префиксом другого ключа: «/DEBUGGER»,
    /// «/DEBUG-KEY», «/debugkey» и прочие не считаются ключом /DEBUG.
    /// Регистр не важен; также принимается форма «-DEBUG». Чистая функция,
    /// покрыта юнит-тестами.
    /// </summary>
    public static bool HasDebugKey(string? commandLine)
    {
        var line = commandLine ?? string.Empty;
        foreach (var prefix in new[] { "/", "-" })
        {
            var search = prefix + "DEBUG";
            var i = 0;
            while ((i = line.IndexOf(search, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                // Начало токена: начало строки, пробел/табуляция или кавычка перед ключом.
                var tokenStart = i == 0
                    || line[i - 1] == ' ' || line[i - 1] == '\t' || line[i - 1] == '"';
                i += search.Length;
                if (!tokenStart)
                    continue;
                // Конец токена: конец строки или разделитель — отсекает
                // «/DEBUGGER», «/DEBUG-KEY» и прочие ключи-продолжения.
                var tokenEnd = i >= line.Length
                    || line[i] == ' ' || line[i] == '\t' || line[i] == '"';
                if (tokenEnd)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Сопоставление по ключу /IBConnectionString (issue #103): при отладке из
    /// Конфигуратора клиент серверной базы запускается с полной строкой соединения
    /// вида «Srvr="host[:port]";Ref="имя";…» (файловая — «File="путь";…»), которую
    /// прямые ключи /S//F и /IBName не покрывают. Сравнение: Srvr с адресом сервера
    /// базы (через <see cref="NormalizeServerHost"/>), Ref с именем базы в кластере;
    /// регистр не важен, порт игнорируется.
    /// </summary>
    internal static bool MatchesIbConnectionString(Infobase infobase, string? commandLine)
    {
        if (infobase?.Connection is null || string.IsNullOrWhiteSpace(commandLine))
            return false;

        if (!TryExtractValue(commandLine, "IBConnectionString", out var connectionString))
            return false;

        var parts = ParseConnectionString(connectionString);
        return infobase.Connection.Type switch
        {
            ConnectionType.ClientServer => MatchesCsServer(parts, infobase),
            ConnectionType.File => MatchesCsFile(parts, infobase.Connection.FilePath ?? ""),
            _ => false
        };
    }

    /// <summary>
    /// Issue #140 (1.0.1.84): сопоставление веб-базы по ключу <c>/WS</c> или
    /// <c>WS=</c> из <c>/IBConnectionString</c>. URL нормализуется (нижний регистр
    /// схемы/хоста, без хвостового «/»); пустое ожидаемое значение не сопоставляется.
    /// Если <see cref="ConnectionSettings.WebUrl"/> пуст, сравнивается
    /// <see cref="ConnectionSettings.DatabaseName"/> (как в отображении базы).
    /// </summary>
    private static bool MatchesWebBase(Infobase infobase, string? commandLine)
    {
        var connection = infobase.Connection;
        if (connection is null || connection.Type != ConnectionType.WebServer)
            return false;

        var expected = NormalizeWebUrl(
            !string.IsNullOrWhiteSpace(connection.WebUrl) ? connection.WebUrl : connection.DatabaseName);
        if (expected.Length == 0)
            return false;

        var line = commandLine ?? string.Empty;
        if (TryExtractValue(line, "WS", out var ws) && WebUrlsEqual(ws, expected))
            return true;

        if (TryExtractValue(line, "IBConnectionString", out var connectionString))
        {
            var parts = ParseConnectionString(connectionString);
            if (parts.TryGetValue("WS", out var csWs) && WebUrlsEqual(csWs, expected))
                return true;
        }

        return false;
    }

    /// <summary>Сравнение URL веб-баз без учёта регистра; пустые значения не равны.</summary>
    private static bool WebUrlsEqual(string? a, string? b)
    {
        var na = NormalizeWebUrl(a);
        var nb = NormalizeWebUrl(b);
        return na.Length > 0
            && nb.Length > 0
            && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Нормализация URL веб-базы (issue #140): trim/кавычки, удаление схемы
    /// (<c>http://</c>/<c>https://</c> — адрес публикации может быть сохранён в карточке
    /// без схемы или с другой схемой), нижний регистр хоста и удаление хвостового «/».
    /// Путь сохраняется как есть: сравнение в <see cref="WebUrlsEqual"/> идёт без учёта
    /// регистра, поэтому различия в регистре пути публикации не мешают.
    /// </summary>
    internal static string NormalizeWebUrl(string? url)
    {
        var s = (url ?? string.Empty).Trim().Trim('"').Trim();
        if (s.Length == 0)
            return string.Empty;

        // Схема не участвует в сравнении: /WS может содержать http, а карточка — https.
        var schemeIndex = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex > 0)
            s = s[(schemeIndex + 3)..];

        var slash = s.IndexOf('/');
        if (slash < 0)
            return s.ToLowerInvariant().TrimEnd('/');

        var host = s[..slash].ToLowerInvariant();
        var path = s[slash..];
        return (host + path).TrimEnd('/');
    }

    /// <summary>
    /// Сравнение строки соединения из cmdline с карточкой клиент-серверной базы
    /// (issue #103, расширение):
    /// <list type="bullet">
    /// <item>Srvr может быть списком адресов через запятую (отказоустойчивый кластер,
    /// «Srvr=""host1,host2:1541""») — совпадение с любым адресом списка;</item>
    /// <item>если <c>DatabaseName</c> в карточке пуст — попытка извлечь Ref/Srvr из
    /// собственной строки соединения базы (<see cref="ConnectionSettings.ToConnectionString"/>)
    /// и сравнить Ref↔Ref, Srvr↔Srvr (по любому адресу из списков).</item>
    /// </list>
    /// </summary>
    private static bool MatchesCsServer(IReadOnlyDictionary<string, string> parts, Infobase infobase)
    {
        if (!parts.TryGetValue("Srvr", out var srvr) || !parts.TryGetValue("Ref", out var reference))
            return false;

        if (reference.Trim().Length == 0)
            return false;

        var connection = infobase.Connection!;
        var server = (connection.Server ?? "").Trim();
        var dbName = (connection.DatabaseName ?? "").Trim();

        if (dbName.Length > 0)
        {
            // Обычный случай: Ref сравнивается с DatabaseName, Srvr — с адресом базы
            // (по любому адресу из списка, порт игнорируется).
            return ServerListContains(srvr, server)
                && string.Equals(reference.Trim(), dbName, StringComparison.OrdinalIgnoreCase);
        }

        // Г3 (issue #103): имя базы в кластере в карточке не заполнено — сравниваем
        // с собственной строкой соединения базы: Ref↔Ref, Srvr↔Srvr. Собственная
        // строка собирается из полей карточки, поэтому если Ref в ней пуст —
        // используем отображаемое имя базы, но всегда В ПАРЕ с адресом сервера
        // (защита от ложных срабатываний сохраняется).
        if (server.Length == 0)
            return false;

        var own = ParseConnectionString(connection.ToConnectionString());
        var ownRef = own.TryGetValue("Ref", out var refValue) ? refValue.Trim() : string.Empty;
        var expectedRef = ownRef.Length > 0 ? ownRef : NormalizeName(infobase.Name);
        if (expectedRef.Length == 0)
            return false;
        if (!string.Equals(reference.Trim(), expectedRef, StringComparison.OrdinalIgnoreCase))
            return false;

        return own.TryGetValue("Srvr", out var ownSrvr) && ownSrvr.Trim().Length > 0
            ? ServerListsIntersect(srvr, ownSrvr)
            : ServerListContains(srvr, server);
    }

    /// <summary>True — в списке адресов Srvr (через запятую) есть адрес, совпадающий
    /// с <paramref name="expectedServer"/> после нормализации (порт/протокол сняты).</summary>
    private static bool ServerListContains(string srvrList, string expectedServer)
    {
        var expected = NormalizeServerHost(expectedServer);
        if (expected.Length == 0)
            return false;
        return SplitServerList(srvrList).Any(addr =>
            string.Equals(NormalizeServerHost(addr), expected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True — списки адресов Srvr пересекаются хотя бы одним адресом
    /// (после нормализации; issue #103, fallback при пустом DatabaseName).</summary>
    private static bool ServerListsIntersect(string srvrListA, string srvrListB)
    {
        var a = SplitServerList(srvrListA)
            .Select(NormalizeServerHost)
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (a.Count == 0)
            return false;
        return SplitServerList(srvrListB).Any(addr =>
        {
            var n = NormalizeServerHost(addr);
            return n.Length > 0 && a.Contains(n);
        });
    }

    /// <summary>Разбивает значение Srvr на отдельные адреса: разделитель — запятая
    /// (отказоустойчивый кластер «host1,host2:1541»); пустые элементы отбрасываются.</summary>
    private static IEnumerable<string> SplitServerList(string srvrList)
        => (srvrList ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesCsFile(IReadOnlyDictionary<string, string> parts, string expectedPath)
    {
        if (!parts.TryGetValue("File", out var file))
            return false;

        var value = (expectedPath ?? "").Trim();
        return value.Length > 0 && PathsEqual(file, value);
    }

    /// <summary>
    /// Разбирает строку соединения 1С «Ключ="Значение";Ключ2="Значение2";…» в словарь
    /// (регистр ключей не важен). Учтено 1С-экранирование кавычек внутри значений
    /// («""» → «"») и «;» внутри значения в кавычках (например пароль); пустые и
    /// неполные части игнорируются, первый ключ побеждает.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseConnectionString(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var s = (connectionString ?? "").Trim();
        if (s.Length == 0)
            return result;

        // 1С удваивает кавычки внутри значений строки соединения — снимаем
        // экранирование до разбора.
        s = s.Replace("\"\"", "\"", StringComparison.Ordinal);

        // Разбивка по «;» вне кавычек: точка с запятой внутри значения
        // («Pwd="p;ass"») является частью значения.
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in s)
        {
            if (ch == '"')
                inQuotes = !inQuotes;
            else if (ch == ';' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        parts.Add(current.ToString());

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim().Trim('"').Trim();
            if (key.Length > 0 && !result.ContainsKey(key))
                result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Двухпроходное сопоставление списка баз с процессами платформы 1С (монитор
    /// запущенных баз, issue #103): первый проход — прямые ключи подключения (/F, /S,
    /// а также /IBConnectionString — серверные базы при отладке из Конфигуратора);
    /// второй — ключ /IBName по имени базы. Запуск отладки из Конфигуратора порождает
    /// клиентский процесс вида <c>ENTERPRISE /IBName <имя></c> без /F//S — раньше
    /// такие процессы не связывались с базой и значок «Предприятие» не появлялся.
    /// Матч по /IBName выполняется ТОЛЬКО когда имя базы уникально в списке
    /// (одинаковые имена в разных серверах/папках не сопоставляются — защита от
    /// ложных срабатываний). Возвращает словарь «Id базы → сопоставленные процессы».
    /// </summary>
    public static IReadOnlyDictionary<string, List<RunningOneCProcess>> MatchProcesses(
        IEnumerable<Infobase> infobases,
        IReadOnlyList<RunningOneCProcess> processes,
        Func<Infobase, string?>? resolveEnterpriseUser = null)
    {
        // issue #134 (1.0.1.80): Id больше не обязателен — ключ базы строит GetBaseKey,
        // иначе базы из кластера (Id = "") выпадали из сопоставления целиком.
        var result = new Dictionary<string, List<RunningOneCProcess>>(StringComparer.OrdinalIgnoreCase);
        // issue #140 (1.0.1.84): веб-базы намеренно не участвуют в индикаторе запуска
        // (прежнее поведение: ключ /WS встречается и у прочих сценариев). Инспектор
        // процессов вызывает FindOwner и сопоставляет их отдельно.
        var bases = (infobases ?? Enumerable.Empty<Infobase>())
            .Where(ib => ib?.Connection is not null && ib.Connection.Type != ConnectionType.WebServer)
            .ToList();
        var processList = processes ?? (IReadOnlyList<RunningOneCProcess>)Array.Empty<RunningOneCProcess>();
        if (bases.Count == 0 || processList.Count == 0)
            return result;

        // Диагностика (issue #103): при CM_DEBUG_RUNNING_MATCH=1 фиксируем входные
        // данные матчера — реальные командные строки процессов и параметры баз.
        if (RunningMatchDebug.IsEnabled)
        {
            RunningMatchDebug.Log($"MatchProcesses: bases={bases.Count}, processes={processList.Count}");
            foreach (var ib in bases)
                RunningMatchDebug.Log(
                    $"  base key={GetBaseKey(ib)} id=\"{ib.Id}\" name=\"{ib.Name}\" db=\"{ib.Connection.DatabaseName}\" " +
                    $"server=\"{ib.Connection.Server}\" file=\"{ib.Connection.FilePath}\" type={ib.Connection.Type}");
            foreach (var p in processList)
                RunningMatchDebug.Log($"  process exe=\"{p.ProcessName}\" cmdline=\"{p.CommandLine}\"");
        }

        // issue #140 (1.0.1.84): прямые ключи /F//S, /IBConnectionString, затем
        // /IBName (только при уникальном имени) и файловый fallback по уникальному
        // последнему сегменту пути. Issue #153: если подключению соответствуют
        // НЕСКОЛЬКО записей списка (одна база заведена под разными учётками),
        // процесс привязывается РОВНО К ОДНОЙ записи — под чьими учётными данными
        // он запущен (SelectOwnerForProcess). Иначе индикатор горел бы у обеих,
        // двойной щелчок активировал чужое окно, а вкладка «Запущенные» дублировала
        // строки. Веб-базы сюда не передаются (см. фильтр выше).
        foreach (var process in processList)
        {
            var candidates = FindDirectOwnerCandidates(bases, process.CommandLine);
            var owner = candidates.Count > 0
                ? SelectOwnerForProcess(candidates, process.CommandLine, resolveEnterpriseUser)
                // Fallback: /IBName (уникальное имя) и файловый хвост пути.
                : FindOwner(bases, process.CommandLine);

            if (owner is null)
            {
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  match: NO owner for exe=\"{process.ProcessName}\"");
                continue;
            }

            AddToResult(result, GetBaseKey(owner), process);
            if (RunningMatchDebug.IsEnabled)
                RunningMatchDebug.Log($"  match: base id={owner.Id} name=\"{owner.Name}\"");
        }

        return result;
    }

    /// <summary>Добавляет базу в словарь кандидатов по имени (issue #103, Г1).
    /// Одна база может попасть в список дважды (Name == DatabaseName) — дубликат
    /// по Id отбрасывается, чтобы не портить проверку уникальности.</summary>
    private static void AddNameCandidate(
        Dictionary<string, List<Infobase>> candidates, string name, Infobase infobase)
    {
        if (name.Length == 0)
            return;
        if (!candidates.TryGetValue(name, out var list))
            candidates[name] = list = new List<Infobase>();
        // Дубликат определяем по ключу базы (issue #134, 1.0.1.80): у баз без Id
        // сравнение по пустой строке ошибочно склеивало разные базы в одну.
        var key = GetBaseKey(infobase);
        if (!list.Any(b => string.Equals(GetBaseKey(b), key, StringComparison.OrdinalIgnoreCase)))
            list.Add(infobase);
    }

    private static void AddToResult(
        Dictionary<string, List<RunningOneCProcess>> result, string infobaseId, RunningOneCProcess process)
    {
        if (!result.TryGetValue(infobaseId, out var list))
            result[infobaseId] = list = new List<RunningOneCProcess>();
        list.Add(process);
    }

    /// <summary>Нормализация имени для сравнения: trim, без кавычек, регистр не важен.</summary>
    private static string NormalizeName(string? name)
    {
        var n = (name ?? string.Empty).Trim().Trim('"').Trim();
        return n;
    }

    /// <summary>
    /// Ищет в командной строке ключ «/буква» (или «-буква») и сравнивает следующее
    /// за ним значение с ожидаемым. Значение в cmdline: остаток строки до следующего
    /// ключа «/», без кавычек. Сравнение путей — без учёта регистра и хвостовых
    /// разделителей; пустое ожидаемое значение не сопоставляется никогда.
    /// </summary>
    internal static bool MatchesValue(string commandLine, string key, string expected)
    {
        var value = (expected ?? "").Trim();
        if (value.Length == 0)
            return false;

        return TryExtractValue(commandLine, key, out var actual) && PathsEqual(actual, value);
    }

    /// <summary>
    /// Сопоставляет клиент-серверную базу с командной строкой процесса (ключ «/S сервер\база»).
    /// Сравнение устойчивое (issue #342 — «инспектор процессов показывает неизвестную базу»):
    /// <list type="bullet">
    /// <item>порт, указанный в адресе сервера базы («srv:1541»), игнорируется — в командной
    /// строке процесса порт, как правило, отсутствует («/S srv\БД»);</item>
    /// <item>разделитель «сервер\база» — последний из «\» или «/»: Конфигуратор
    /// передаёт клиенту <c>/S"сервер/база"</c> с прямыми слешами (issue #103);</item>
    /// <item>хост и имя базы сравниваются без учёта регистра;</item>
    /// <item>имя базы берётся последним сегментом после «\» — кавычки и пробелы внутри
    /// имени не ломают разбор.</item>
    /// </list>
    /// </summary>
    internal static bool MatchesServerValue(string commandLine, string expectedServer, string expectedDbName)
    {
        var server = (expectedServer ?? "").Trim();
        var dbName = (expectedDbName ?? "").Trim();
        if (server.Length == 0 || dbName.Length == 0)
            return false;

        if (!TryExtractValue(commandLine, "S", out var raw))
            return false;

        var (cmdServer, cmdDb) = SplitServerValue(raw);
        if (cmdDb.Length == 0)
            return false;

        return string.Equals(cmdDb, dbName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizeServerHost(cmdServer),
                NormalizeServerHost(server),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Сопоставление клиент-серверной базы по ключу <c>/S</c>, когда имя базы в кластере
    /// в карточке не заполнено (issue #134, 1.0.1.80): имя из командной строки
    /// сравнивается с названием карточки, адрес сервера — как в
    /// <see cref="MatchesServerValue"/>. Применяется только при пустом
    /// <see cref="ConnectionSettings.DatabaseName"/>, чтобы не давать ложных совпадений.
    /// </summary>
    internal static bool MatchesServerNameValue(
        string commandLine, string expectedServer, string expectedName)
    {
        var server = (expectedServer ?? "").Trim();
        var name = (expectedName ?? "").Trim();
        if (server.Length == 0 || name.Length == 0)
            return false;

        if (!TryExtractValue(commandLine, "S", out var raw))
            return false;

        var (cmdServer, cmdDb) = SplitServerValue(raw);
        if (cmdDb.Length == 0)
            return false;

        return string.Equals(cmdDb, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizeServerHost(cmdServer),
                NormalizeServerHost(server),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Извлекает значение ключа «/буква» (или «-буква») из командной строки:
    /// остаток строки до следующего ключа «/...»/«-...», без обрамляющих кавычек.
    /// Ключ должен стоять отдельным токеном, чтобы не поймать «/S» внутри «/SD…»
    /// или «/Out». Возвращает false, если ключ отсутствует или значение пусто.
    /// </summary>
    internal static bool TryExtractValue(string commandLine, string key, out string value)
    {
        value = string.Empty;
        var line = commandLine ?? "";
        var search = "/" + key;

        var index = 0;
        while ((index = line.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Ключ должен стоять отдельным токеном: начало строки или пробел перед ним,
            // чтобы не поймать «/S» внутри «/SDanything» (сравнение ниже всё равно
            // отсечёт лишнее, но пробел-условие убирает ложные ключи вида «/Out»).
            var tokenStart = index == 0 || line[index - 1] == ' ' || line[index - 1] == '\t';
            index += search.Length;

            if (!tokenStart)
                continue;

            // После ключа допускается кавычка и/или пробел: /F"path", /F "path", /Fpath.
            var rest = line[index..].TrimStart();

            string actual;
            if (rest.StartsWith('"'))
            {
                // Прежняя эвристика (сохранена для совместимости): значение до
                // следующего ключа « /»/« -»; в cmdline одиночные кавычки внутри
                // значения могут быть ЛИТЕРАЛЬНЫМИ (не удвоенными), поэтому нельзя
                // безусловно считать первую кавычку закрывающей.
                var trimmed = rest.TrimStart('"');
                var heuristicEnd = trimmed.Length;
                for (var i = 1; i < trimmed.Length; i++)
                {
                    if ((trimmed[i] == '/' || trimmed[i] == '-') && (trimmed[i - 1] == ' ' || trimmed[i - 1] == '"'))
                    {
                        heuristicEnd = i;
                        break;
                    }
                }
                var heuristic = trimmed[..heuristicEnd].Trim().TrimEnd('"').Trim();

                // Г4 (issue #103): значение в кавычках может содержать « /» и « -»
                // (например пользователь/пароль внутри /IBConnectionString). Если
                // прежняя эвристика оборвала значение РАНЬШЕ закрывающей кавычки
                // (кавычки в значении экранированы удвоением «""»), продолжаем до
                // закрывающей кавычки — значение извлекается целиком.
                var closing = -1;
                var j = 1;
                while (j < rest.Length)
                {
                    if (rest[j] == '"')
                    {
                        if (j + 1 < rest.Length && rest[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }
                        closing = j;
                        break;
                    }
                    j++;
                }

                // Позиция обрыва эвристики в координатах rest (rest начинается с кавычки).
                var heuristicCutInRest = heuristicEnd + 1;
                actual = closing > heuristicCutInRest
                    ? rest[1..closing].Replace("\"\"", "\"", StringComparison.Ordinal).Trim()
                    : heuristic;
            }
            else
            {
                // Значение без кавычек заканчивается перед следующим ключом «/...»
                // или «-...».
                var end = rest.Length;
                for (var i = 1; i < rest.Length; i++)
                {
                    if ((rest[i] == '/' || rest[i] == '-') && (rest[i - 1] == ' '))
                    {
                        end = i;
                        break;
                    }
                }

                actual = rest[..end].Trim().TrimEnd('"').Trim();
            }

            if (actual.Length > 0)
            {
                value = actual;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Разделяет строку подключения «сервер\база» на хост и имя базы. Разделителем
    /// считается ПОСЛЕДНИЙ из «\» или «/»: Конфигуратор запускает клиент серверной
    /// базы с ключом вида <c>/S"сервер/база"</c> — без пробела между ключом и
    /// значением и с прямыми слешами (issue #103). Имена сервера и базы не
    /// содержат «/», поэтому для ключа /S это безопасно.
    /// </summary>
    private static (string Server, string DbName) SplitServerValue(string value)
    {
        var v = (value ?? "").Trim();
        var slash = Math.Max(v.LastIndexOf('\\'), v.LastIndexOf('/'));
        if (slash < 0)
            return (string.Empty, v);
        return (v[..slash].Trim(), v[(slash + 1)..].Trim());
    }

    /// <summary>
    /// Нормализация адреса сервера для сравнения (issue #103): снимает префикс
    /// протокола («tcp://srv» → «srv», встречается в Srvr строки соединения) и
    /// порт («srv:1541» → «srv»); применяется симметрично к обеим сторонам
    /// сравнения — и к значению из cmdline, и к адресу базы в списке.
    /// </summary>
    private static string NormalizeServerHost(string server)
    {
        var s = (server ?? "").Trim();

        // Префикс протокола: Srvr="tcp://host:1541".
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0)
            s = s[(scheme + 3)..].Trim();

        var colon = s.LastIndexOf(':');
        if (colon > 0 && colon < s.Length - 1 && int.TryParse(s[(colon + 1)..], out _))
            return s[..colon].Trim();
        return s;
    }

    /// <summary>Сравнение путей/строк подключения без регистра и хвостовых разделителей.</summary>
    internal static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        var na = NormalizePath(a);
        var nb = NormalizePath(b);
        return na.Length > 0
            && nb.Length > 0
            && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Issue #140 (1.0.1.84): сравнение последнего сегмента пути без учёта регистра.
    /// Используется файловым fallback (UNC ↔ смонтированный диск, короткие имена).
    /// </summary>
    internal static bool PathLeafEquals(string? a, string? b)
    {
        var la = GetPathLeaf(a);
        var lb = GetPathLeaf(b);
        return la.Length > 0
            && lb.Length > 0
            && string.Equals(la, lb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Последний сегмент пути (issue #140): учитывает и «\», и «/» на любой платформе,
    /// поэтому работает и с UNC-путями Windows в Linux-сборке.
    /// </summary>
    internal static string GetPathLeaf(string? path)
    {
        var p = (path ?? string.Empty).Trim().Trim('"').Trim();
        if (p.Length == 0)
            return string.Empty;

        p = p.TrimEnd('\\', '/');
        var index = p.LastIndexOfAny(new[] { '\\', '/' });
        return index >= 0 ? p[(index + 1)..].Trim() : p;
    }

    /// <summary>
    /// Нормализация пути (issue #140, 1.0.1.84): trim/кавычки, раскрытие переменных
    /// окружения, снятие префикса длинных путей Windows, замена «/» на разделитель
    /// платформы, полная форма через <see cref="Path.GetFullPath(string)"/> (в try/catch),
    /// раскрытие коротких имён 8.3 на Windows и обрезка хвостовых разделителей.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        var p = (path ?? string.Empty).Trim().Trim('"').Trim();
        if (p.Length == 0)
            return string.Empty;

        try
        {
            // Карточка базы может хранить %USERPROFILE%/%APPDATA%, а командная
            // строка — уже раскрытый абсолютный путь.
            p = Environment.ExpandEnvironmentVariables(p);
        }
        catch
        {
            // Некорректная переменная — оставляем строку как есть.
        }

        // Длинные пути Windows: «\\?\C:\...» и «\\?\UNC\server\share».
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p[4..];

        // Прямые слеши в пути → разделитель текущей платформы.
        p = p.Replace('/', Path.DirectorySeparatorChar);

        try
        {
            p = Path.GetFullPath(p);
        }
        catch
        {
            // Некорректный путь — сравниваем в исходном виде (ниже только trim).
        }

        // Короткие имена 8.3 (C:\BASES~1\IB): на Windows раскрываем до длинного
        // пути, если каталог существует. На других платформах и для несуществующих
        // путей остаётся исходная форма — разницу покрывает fallback по листу.
        p = ExpandShortPath(p);

        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Issue #140 (1.0.1.84): раскрытие коротких имён 8.3 в длинный путь Windows.
    /// Вызывается только для путей с «~» (признак короткого имени): так не трогаем
    /// обычные и UNC-пути и не провоцируем сетевые обращения к недоступному серверу.
    /// Для несуществующих путей и на других платформах возвращает вход без изменений.
    /// </summary>
    private static string ExpandShortPath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || !OperatingSystem.IsWindows()
            || path.IndexOf('~') < 0)
        {
            return path;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = GetLongPathNameW(path, buffer, (uint)buffer.Capacity);
            if (length == 0)
                return path;
            if (length > buffer.Capacity)
            {
                buffer = new StringBuilder((int)length);
                length = GetLongPathNameW(path, buffer, (uint)buffer.Capacity);
                if (length == 0)
                    return path;
            }

            var expanded = buffer.ToString();
            return string.IsNullOrEmpty(expanded) ? path : expanded;
        }
        catch
        {
            // Нет kernel32/путь некорректен — сравниваем в исходном виде.
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(
        string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);
}
