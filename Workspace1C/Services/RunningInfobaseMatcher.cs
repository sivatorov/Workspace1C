using System;
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
    /// <summary>Сопоставляет базу с командной строкой процесса 1С (прямые ключи /F и /S).</summary>
    public static bool MatchesCommandLine(Infobase infobase, string? commandLine)
    {
        if (infobase?.Connection is null || string.IsNullOrWhiteSpace(commandLine))
            return false;

        return infobase.Connection.Type switch
        {
            ConnectionType.File => MatchesValue(commandLine, "F", infobase.Connection.FilePath ?? ""),
            ConnectionType.ClientServer => MatchesServerValue(
                commandLine,
                infobase.Connection.Server ?? "",
                infobase.Connection.DatabaseName ?? ""),
            _ => false
        };
    }

    /// <summary>
    /// Двухпроходное сопоставление списка баз с процессами платформы 1С (монитор
    /// запущенных баз, issue #103): первый проход — прямые ключи подключения (/F, /S);
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
        if (bases.Count == 0 || processes is null || processes.Count == 0)
            return result;

        // Первый проход: прямые ключи /F//S — самый надёжный признак.
        var unmatched = new List<RunningOneCProcess>();
        foreach (var process in processes)
        {
            var owner = bases.FirstOrDefault(ib => MatchesCommandLine(ib, process.CommandLine));
            if (owner is null)
                unmatched.Add(process);
            else
                AddToResult(result, owner.Id, process);
        }

        // Второй проход: /IBConnectionString "File=…;" / "Srvr=…;Ref=…;" — так клиент
        // запускается из Конфигуратора и внешними обработками (issue #103).
        unmatched = MatchByConnectionString(bases, unmatched, result);

        // Третий проход: родительский процесс. Клиент отладки — дочерний процесс
        // Конфигуратора; если Конфигуратор уже сопоставлен с базой, клиент принадлежит
        // той же базе — независимо от совпадения имён (issue #103).
        unmatched = MatchByParent(processes, unmatched, result);

        // Четвёртый проход: /IBName — только уникальные имена (issue #103, v1.0.1.35).
        var uniqueByName = bases
            .GroupBy(ib => NormalizeName(ib.Name))
            .Where(g => g.Key.Length > 0 && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (uniqueByName.Count == 0)
            return result;

        foreach (var process in unmatched)
        {
            if (!TryExtractValue(process.CommandLine ?? "", "IBName", out var rawName))
                continue;
            var name = NormalizeName(rawName);
            if (name.Length == 0)
                continue;
            if (uniqueByName.TryGetValue(name, out var owner))
                AddToResult(result, owner.Id, process);
        }

        return result;
    }

    /// <summary>
    /// Проход по ключу <c>/IBConnectionString</c>: строка подключения разбирается на
    /// <c>File</c> или <c>Srvr</c>+<c>Ref</c> и сравнивается с параметрами баз.
    /// Возвращает процессы, оставшиеся несопоставленными.
    /// </summary>
    private static List<RunningOneCProcess> MatchByConnectionString(
        List<Infobase> bases, List<RunningOneCProcess> unmatched,
        Dictionary<string, List<RunningOneCProcess>> result)
    {
        var rest = new List<RunningOneCProcess>();
        foreach (var process in unmatched)
        {
            var parsed = ParseIbConnectionString(process.CommandLine);
            Infobase? owner = null;
            if (parsed is { } p)
            {
                owner = bases.FirstOrDefault(ib => ib.Connection!.Type switch
                {
                    ConnectionType.File => p.File is not null && PathsEqual(p.File, ib.Connection.FilePath),
                    ConnectionType.ClientServer => p.Server is not null && p.Ref is not null
                        && string.Equals(p.Ref, (ib.Connection.DatabaseName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(NormalizeServerHost(p.Server), NormalizeServerHost(ib.Connection.Server ?? ""),
                            StringComparison.OrdinalIgnoreCase),
                    _ => false
                });
            }
            if (owner is null)
                rest.Add(process);
            else
                AddToResult(result, owner.Id, process);
        }
        return rest;
    }

    /// <summary>
    /// Проход по родительскому процессу: несопоставленный процесс, родитель которого
    /// уже сопоставлен с базой, относится к той же базе. Повторяется, пока находятся
    /// новые связи (цепочка «Конфигуратор → клиент → дочерний клиент»).
    /// </summary>
    private static List<RunningOneCProcess> MatchByParent(
        IReadOnlyList<RunningOneCProcess> all, List<RunningOneCProcess> unmatched,
        Dictionary<string, List<RunningOneCProcess>> result)
    {
        var ownerByPid = new Dictionary<int, string>();
        foreach (var pair in result)
            foreach (var p in pair.Value)
                if (p.Pid > 0)
                    ownerByPid[p.Pid] = pair.Key;

        var rest = unmatched;
        bool changed;
        do
        {
            changed = false;
            var next = new List<RunningOneCProcess>();
            foreach (var process in rest)
            {
                if (process.ParentPid > 0 && process.Pid != process.ParentPid
                    && ownerByPid.TryGetValue(process.ParentPid, out var ownerId))
                {
                    AddToResult(result, ownerId, process);
                    if (process.Pid > 0)
                        ownerByPid[process.Pid] = ownerId;
                    changed = true;
                }
                else
                {
                    next.Add(process);
                }
            }
            rest = next;
        } while (changed && rest.Count > 0);
        return rest;
    }

    /// <summary>
    /// Разбирает значение ключа <c>/IBConnectionString</c> командной строки: возвращает
    /// каталог файловой базы (<c>File</c>) или сервер и имя базы (<c>Srvr</c>/<c>Ref</c>).
    /// Значения допускают удвоенные кавычки, как их пишет платформа:
    /// <c>/IBConnectionString "File=""C:\Bases\ib"";"</c>. null — ключа нет.
    /// </summary>
    internal static (string? File, string? Server, string? Ref)? ParseIbConnectionString(string? commandLine)
    {
        var line = commandLine ?? string.Empty;
        var index = line.IndexOf("/IBConnectionString", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var rest = line[(index + "/IBConnectionString".Length)..];

        // Значение — до следующего ключа « /…» вне кавычек.
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < rest.Length; i++)
        {
            var ch = rest[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue; // кавычки (в т.ч. удвоенные) в значении не нужны
            }
            if (!inQuotes && (ch == '/' || ch == '-') && i > 0 && rest[i - 1] == ' ' && sb.ToString().Trim().Length > 0)
                break;
            sb.Append(ch);
        }

        string? file = null, server = null, reference = null;
        foreach (var part in sb.ToString().Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (value.Length == 0)
                continue;
            if (key.Equals("File", StringComparison.OrdinalIgnoreCase))
                file = value;
            else if (key.Equals("Srvr", StringComparison.OrdinalIgnoreCase))
                server = value;
            else if (key.Equals("Ref", StringComparison.OrdinalIgnoreCase))
                reference = value;
        }
        return file is null && (server is null || reference is null) ? null : (file, server, reference);
    }

    /// <summary>
    /// Родительский PID из содержимого <c>/proc/&lt;pid&gt;/stat</c> (поле 4). Имя процесса
    /// в скобках может содержать пробелы и скобки, поэтому разбор — после последней «)».
    /// 0 — разобрать не удалось.
    /// </summary>
    public static int ParseParentPidFromStat(string? stat)
    {
        if (string.IsNullOrEmpty(stat))
            return 0;
        var close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 >= stat.Length)
            return 0;
        var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // fields[0] — состояние, fields[1] — ppid.
        return fields.Length > 1 && int.TryParse(fields[1], out var ppid) ? ppid : 0;
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
            var rest = line[index..].TrimStart().TrimStart('"');

            // Значение заканчивается перед следующим ключом «/...» или «-...».
            var end = rest.Length;
            for (var i = 1; i < rest.Length; i++)
            {
                if ((rest[i] == '/' || rest[i] == '-') && (rest[i - 1] == ' ' || rest[i - 1] == '"'))
                {
                    end = i;
                    break;
                }
            }

            var actual = rest[..end].Trim().TrimEnd('"').Trim();
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

    /// <summary>Снимает порт с адреса сервера («srv:1541» → «srv»); без порта — как есть.</summary>
    private static string NormalizeServerHost(string server)
    {
        var s = (server ?? "").Trim();
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
