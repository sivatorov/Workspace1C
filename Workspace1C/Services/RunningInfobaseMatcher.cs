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

        // Второй проход: /IBName — запуск отладки из Конфигуратора (issue #103).
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
