using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика сопоставления командной строки процесса 1С с информационной базой
/// (индикатор «база сейчас запущена»). Не зависит от платформы и UI — покрыта
/// юнит-тестами. Ключи подключения: файловая — <c>/F &lt;каталог&gt;</c>,
/// клиент-серверная — <c>/S &lt;сервер&gt;\&lt;база&gt;</c>; после ключа путь может
/// идти через пробел или без него в кавычках (<c>/F"C:\Bases\ib"</c>).
/// Веб-базы не сопоставляются (ключ /WS встречается и у прочих сценариев).
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
                || MatchesIbConnectionString(infobase, commandLine),
            _ => false
        };
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
        IEnumerable<Infobase> infobases, IReadOnlyList<RunningOneCProcess> processes)
    {
        var result = new Dictionary<string, List<RunningOneCProcess>>();
        var bases = (infobases ?? Enumerable.Empty<Infobase>())
            .Where(ib => ib?.Connection is not null && !string.IsNullOrWhiteSpace(ib.Id))
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
                    $"  base id={ib.Id} name=\"{ib.Name}\" db=\"{ib.Connection.DatabaseName}\" " +
                    $"server=\"{ib.Connection.Server}\" file=\"{ib.Connection.FilePath}\" type={ib.Connection.Type}");
            foreach (var p in processList)
                RunningMatchDebug.Log($"  process exe=\"{p.ProcessName}\" cmdline=\"{p.CommandLine}\"");
        }

        // Первый проход: прямые ключи /F//S — самый надёжный признак.
        var unmatched = new List<RunningOneCProcess>();
        foreach (var process in processList)
        {
            var owner = bases.FirstOrDefault(ib => MatchesCommandLine(ib, process.CommandLine));
            if (owner is null)
            {
                unmatched.Add(process);
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  pass1: NO direct match for exe=\"{process.ProcessName}\"");
            }
            else
            {
                AddToResult(result, owner.Id, process);
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  pass1: matched to base id={owner.Id} (direct keys)");
            }
        }

        // Второй проход: /IBName — запуск отладки из Конфигуратора (issue #103).
        // Г1 (issue #103): имя процесса может быть как отображаемым именем базы (Name),
        // так и именем базы в кластере (Connection.DatabaseName), поэтому кандидаты
        // собираются из ОБОИХ источников; сопоставление — только когда имя однозначно.
        var nameCandidates = new Dictionary<string, List<Infobase>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ib in bases)
        {
            AddNameCandidate(nameCandidates, NormalizeName(ib.Name), ib);
            AddNameCandidate(nameCandidates, NormalizeName(ib.Connection.DatabaseName), ib);
        }
        if (RunningMatchDebug.IsEnabled)
        {
            foreach (var pair in nameCandidates)
                RunningMatchDebug.Log(
                    $"  pass2: name \"{pair.Key}\" -> {pair.Value.Count} candidate(s) " +
                    $"[{string.Join(", ", pair.Value.Select(b => b.Id))}]");
        }

        foreach (var process in unmatched)
        {
            if (!TryExtractValue(process.CommandLine ?? "", "IBName", out var rawName))
            {
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  pass2: no /IBName in exe=\"{process.ProcessName}\"");
                continue;
            }
            var name = NormalizeName(rawName);
            if (name.Length == 0)
                continue;

            if (nameCandidates.TryGetValue(name, out var candidates) && candidates.Count == 1)
            {
                var owner = candidates[0];
                AddToResult(result, owner.Id, process);
                if (RunningMatchDebug.IsEnabled)
                    RunningMatchDebug.Log($"  pass2: /IBName \"{name}\" matched to base id={owner.Id}");
            }
            else if (RunningMatchDebug.IsEnabled)
            {
                RunningMatchDebug.Log(
                    $"  pass2: /IBName \"{name}\" skipped — " +
                    (candidates is null ? "no candidate" : $"ambiguous ({candidates.Count} candidates)"));
            }
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
        if (!list.Any(b => string.Equals(b.Id, infobase.Id, StringComparison.Ordinal)))
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

    /// <summary>Разделяет строку подключения «сервер\база» на хост и имя базы (по последней «\»).</summary>
    private static (string Server, string DbName) SplitServerValue(string value)
    {
        var v = (value ?? "").Trim();
        var slash = v.LastIndexOf('\\');
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
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Нормализация пути: полная форма, без хвостовых разделителей; при ошибке — trim.</summary>
    internal static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
