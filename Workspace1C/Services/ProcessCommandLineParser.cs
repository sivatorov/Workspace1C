using System;

namespace Workspace1C.Services;

/// <summary>Режим запуска процесса платформы 1С (инспектор процессов).</summary>
public enum OneCProcessLaunchMode
{
    /// <summary>1С:Предприятие (обычный запуск клиента).</summary>
    Enterprise,

    /// <summary>Конфигуратор (ключи DESIGNER/CONFIG).</summary>
    Configurator,

    /// <summary>Служебный запуск: внешняя обработка (/Execute), команда (/C) и т.п.</summary>
    Service
}

/// <summary>
/// Чистый разбор командной строки процесса 1С для инспектора процессов: режим
/// запуска, пользователь (ключ /N) и краткая строка подключения (/F или /S).
/// Не зависит от платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class ProcessCommandLineParser
{
    /// <summary>
    /// Режим запуска по ключам командной строки. Приоритет: Конфигуратор
    /// (DESIGNER/CONFIG) → Служебный (/Execute, /C и прочие запуски с обработкой)
    /// → 1С:Предприятие. Ключи ищутся отдельными токенами, чтобы не поймать
    /// «CONFIG» внутри пути или «/C» внутри «/ClientConnection».
    /// </summary>
    public static OneCProcessLaunchMode DetectMode(string? commandLine)
    {
        var line = commandLine ?? string.Empty;

        if (ContainsToken(line, "DESIGNER") || ContainsToken(line, "CONFIG"))
            return OneCProcessLaunchMode.Configurator;

        if (HasKey(line, "Execute") || HasKey(line, "C"))
            return OneCProcessLaunchMode.Service;

        return OneCProcessLaunchMode.Enterprise;
    }

    /// <summary>
    /// Пользователь из ключа /N (или /N"Имя"). Возвращает null, если пользователь
    /// не задан. Разбор выполняет общий <see cref="ProcessCommandLineUserParser"/>
    /// (issue #134): поддерживаются формы <c>/N"Имя"</c>, <c>/N Имя</c>,
    /// <c>/NИмя</c> и <c>-N</c>.
    /// </summary>
    public static string? ExtractUser(string? commandLine) =>
        ProcessCommandLineUserParser.ExtractUserName(commandLine);

    /// <summary>
    /// Краткая строка подключения из командной строки: для файловой базы — «/F <путь>»,
    /// для клиент-серверной — «/S <сервер>\<база>», для веб-базы — «/WS <URL>»
    /// (issue #140, 1.0.1.84). Если подключение передано только строкой
    /// <c>/IBConnectionString</c>, показывается её фактическое значение (для веб-базы
    /// из неё извлекается <c>WS=</c>). Версия ключа сохраняется как в командной
    /// строке (со слешем). Возвращает null, если ключа подключения нет.
    /// </summary>
    public static string? ExtractConnectionString(string? commandLine)
    {
        var line = commandLine ?? string.Empty;

        var filePath = ExtractValue(line, "F");
        if (!string.IsNullOrWhiteSpace(filePath))
            return "/F " + filePath;

        var serverBase = ExtractValue(line, "S");
        if (!string.IsNullOrWhiteSpace(serverBase))
            return "/S " + serverBase;

        // issue #140 (1.0.1.84): веб-база запускается ключом /WS <адрес публикации>.
        var webUrl = ExtractValue(line, "WS");
        if (!string.IsNullOrWhiteSpace(webUrl))
            return "/WS " + webUrl;

        // Отладка из Конфигуратора передаёт полную строку соединения. Для веб-базы
        // из неё берётся WS=, иначе показывается сама строка — это фактическое
        // значение из командной строки (колонка «Строка подключения»).
        var connectionString = ExtractValue(line, "IBConnectionString");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var parts = RunningInfobaseMatcher.ParseConnectionString(connectionString);
            if (parts.TryGetValue("WS", out var ws) && !string.IsNullOrWhiteSpace(ws))
                return "/WS " + ws;
            return connectionString;
        }

        return null;
    }

    /// <summary>
    /// Значение после ключа «/буква» (или «-буква»): остаток строки до следующего
    /// ключа «/» или «-», без обрамляющих кавычек. Тот же алгоритм, что в
    /// <see cref="RunningInfobaseMatcher"/>, но возвращает само значение.
    /// </summary>
    internal static string? ExtractValue(string? commandLine, string key)
    {
        var line = commandLine ?? string.Empty;
        var search = "/" + key;

        var index = 0;
        while ((index = line.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var tokenStart = index == 0 || line[index - 1] == ' ' || line[index - 1] == '\t';
            index += search.Length;

            if (!tokenStart)
                continue;

            var rest = line[index..].TrimStart().TrimStart('"');

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
                return actual;
        }

        return null;
    }

    /// <summary>Токен (слово) присутствует в командной строке как отдельный элемент.</summary>
    private static bool ContainsToken(string line, string token)
    {
        var index = 0;
        while ((index = line.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeOk = index == 0 || char.IsWhiteSpace(line[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= line.Length || char.IsWhiteSpace(line[afterIndex])
                || line[afterIndex] == '"' || line[afterIndex] == '/';
            if (beforeOk && afterOk)
                return true;
            index += token.Length;
        }

        return false;
    }

    /// <summary>Ключ «/имя» присутствует отдельным токеном (например «/Execute», «/C»).</summary>
    private static bool HasKey(string line, string key)
    {
        var search = "/" + key;
        var index = 0;
        while ((index = line.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var tokenStart = index == 0 || line[index - 1] == ' ' || line[index - 1] == '\t';
            if (tokenStart)
                return true;
            index += search.Length;
        }

        return false;
    }
}