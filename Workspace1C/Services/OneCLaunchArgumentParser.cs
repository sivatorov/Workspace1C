using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистый платформенно-нейтральный сервис разбора командной строки запуска 1С.
/// Разбирает строку параметров запуска базы на структурированные аргументы
/// (<see cref="OneCLaunchArgument"/>), собирает их обратно в строку, предоставляет
/// каталог известных ключей командной строки 1С для автодополнения (справочник
/// окна «Параметры») и автодополнение ключей по префиксу. Не зависит от UI
/// и от платформы — используется и WPF, и Avalonia-сборками (ПЗ-5).
/// </summary>
public static class OneCLaunchArgumentParser
{
    /// <summary>
    /// Известные ключи командной строки 1С в порядке справочника. Порядок важен —
    /// он сохраняется и в каталоге автодополнения.
    /// </summary>
    public static IReadOnlyList<string> KnownKeys { get; } = new[]
    {
        // Параметры-флаги.
        "/DisableStartupMessages",
        "/DisableStartupDialogs",
        "/DisableSplash",
        "/WA-",
        "/Debug",
        "/AllowExecuteScheduledJobs",
        "/RunModeManagedApplication",
        "/RunModeOrdinaryApplication",
        "/UpdateCfg",
        "/TestServer",
        "/RestoreIB",
        "/DumpIB",
        "/DumpCfg",
        "/LoadCfg",
        "/CheckConfig",
        "/UpdateConfigDumpCfg",
        "/DumpConfigToFiles",
        "/CreateInfobase",
        "/Command",
        "/ManagedClient",
        "/ThickClient",
        "/UpdateConfiguration",

        // Параметры с аргументами.
        "/UC",
        "/L",
        "/Out",
        "/C",
        "/Execute",
        "/DumpResult",
        "/N",
        "/P",
        "/S",
        "/F",
        "/Ref",
        "/Server",
        "/Srvr",
        "/IBName",
        "/DBMS",
        "/DBSrvr",
        "/DBUID",
        "/DBPwd",
        "/App",
        "/ConfigurationRepository",
        "/ConfigurationRepositoryUser",
        "/ConfigurationRepositoryPwd",
        "/DisplayAllFunctions",
        "/WSNamespace",
        // Учётные данные веб-сервера (issue #150): ключи должны быть в справочнике,
        // иначе автодополнение их не предлагает, а пересборка строки запуска
        // может принять их за неизвестные и оставить только позиционные значения.
        "/WSN",
        "/WSP",
        "/IBSecurity",
        "/CPUSecurity",
        "/SaveAgent",
        "/ConfigurationName",
        "/RegisterExternalDataSource",
        "/UnregisterExternalDataSource",
        "/SqlDump"
    };

    /// <summary>
    /// Разбирает строку параметров запуска 1С на структурированные аргументы.
    /// Разделение происходит по пробелам вне двойных кавычек; значение внутри
    /// кавычек снимает внешние кавычки. Пустая строка даёт пустой список.
    /// </summary>
    /// <param name="parameters">Строка параметров запуска (например «/UC "123" /Debug»).</param>
    /// <returns>Список разобранных аргументов в порядке следования.</returns>
    public static IReadOnlyList<OneCLaunchArgument> ParseLaunchParameters(string? parameters)
    {
        var result = new List<OneCLaunchArgument>();
        if (string.IsNullOrWhiteSpace(parameters))
            return result;

        foreach (var token in Tokenize(parameters))
        {
            var key = token;
            var value = string.Empty;

            // Значение отдельным токеном в кавычках («/WSN "webuser"», «/C "текст"»)
            // принадлежит предыдущему ключу (issue #150). Без этой привязки значение
            // становилось самостоятельным «позиционным» аргументом: при пересборке
            // строки запуска ключ мог потеряться, а голое значение оставалось —
            // платформа игнорировала его и запрашивала аутентификацию веб-сервера.
            if (IsFullyQuotedToken(token) && result.Count > 0 && !result[^1].HasValue)
            {
                var previous = result[^1];
                result[^1] = new OneCLaunchArgument(previous.Key, Unquote(token));
                continue;
            }

            // Ключ может следовать со значением без пробела («/UC"код"») — отделяем его.
            if (key.StartsWith('/'))
            {
                var quote = key.IndexOf('"');
                if (quote > 0)
                {
                    value = key[(quote + 1)..].TrimEnd('"');
                    key = key[..quote];
                }
            }

            result.Add(new OneCLaunchArgument(key, value));
        }

        return result;
    }

    /// <summary>
    /// Собирает структурированные аргументы обратно в каноническую строку параметров
    /// запуска 1С: ключи без значения выводятся как есть, аргументы со значением —
    /// как «/Ключ "значение"»; элементы разделяются одним пробелом.
    /// </summary>
    /// <param name="arguments">Разобранные аргументы.</param>
    /// <returns>Строка параметров запуска.</returns>
    public static string FormatLaunchParameters(IEnumerable<OneCLaunchArgument>? arguments)
    {
        if (arguments is null)
            return string.Empty;

        return string.Join(" ", arguments.Select(a =>
            a.HasValue ? $"{a.Key} \"{a.Value}\"" : a.Key));
    }

    /// <summary>
    /// Возвращает ключи командной строки 1С, начинающиеся с заданного префикса
    /// (регистронезависимо), дополненные пользовательскими ключами. Используется
    /// для автодополнения параметров запуска.
    /// </summary>
    /// <param name="prefix">Набираемый префикс ключа (например «/Di»). Пусто — все ключи.</param>
    /// <param name="extraKeys">Дополнительные пользовательские ключи (issue #141), необязательно.</param>
    public static IEnumerable<string> AutocompleteKnownKeys(string? prefix, IReadOnlyList<string>? extraKeys = null)
    {
        var keys = new List<string>(KnownKeys);
        if (extraKeys != null)
        {
            foreach (var k in extraKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                var key = (k ?? string.Empty).Split('\t')[0].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    keys.Add(key);
            }
        }

        var p = (prefix ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(p))
            return keys;
        return keys.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Добавляет параметр в строку «Параметры», разделяя пробелом, как делало окно
    /// «Параметры» при подстановке ключа из справочника. Поведение идентично прежнему
    /// inline-коду: вставка обрезается, пустая вставка ничего не меняет.
    /// </summary>
    /// <param name="currentText">Текущая строка параметров (может быть пустой или null).</param>
    /// <param name="key">Ключ, который нужно добавить.</param>
    /// <returns>Обновлённая строка параметров.</returns>
    public static string AppendParameter(string? currentText, string key)
    {
        var insert = (key ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(insert))
            return currentText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentText))
            return insert;
        return currentText.TrimEnd() + " " + insert;
    }

    /// <summary>
    /// Строит каталог известных ключей командной строки 1С с локализованными
    /// описаниями для справочника автодополнения окна «Параметры», дополняя его
    /// пользовательскими параметрами (issue #141), если они переданы.
    /// </summary>
    /// <param name="customParameters">
    /// Пользовательские параметры справочника; формат элемента «ключ» либо
    /// «ключ<TAB>комментарий» (необязательно).
    /// </param>
    public static IReadOnlyList<OneCLaunchParameterReference> BuildReferenceCatalog(
        IReadOnlyList<string>? customParameters = null)
    {
        var list = new List<OneCLaunchParameterReference>(KnownKeys.Count);

        // Ключ перевода описания параметра по его ключу командной строки:
        // «/DisableStartupMessages» → «LaunchParams.Ref.DisableStartupMessages».
        foreach (var key in KnownKeys)
        {
            var locKey = "LaunchParams.Ref." + key.Trim('/').Replace("-", "");
            list.Add(new OneCLaunchParameterReference(key, LocalizationManager.T(locKey)));
        }

        // Пользовательские параметры (issue #141): добавляются в конец списка,
        // помечаются, чтобы их можно было отличить от встроенных и удалить.
        if (customParameters != null)
        {
            foreach (var custom in customParameters)
            {
                var parts = (custom ?? string.Empty).Split('\t');
                var key = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var comment = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var description = string.IsNullOrWhiteSpace(comment)
                    ? LocalizationManager.T("LaunchParams.CustomMarker")
                    : comment;
                list.Add(new OneCLaunchParameterReference(key, description, isCustom: true));
            }
        }

        return list;
    }

    /// <summary>
    /// Разбирает запись пользовательского параметра справочника (issue #141): формат
    /// «ключ» либо «ключ<TAB>комментарий». Возвращает нормализованную пару
    /// (ключ без табуляций, комментарий без табуляций; могут быть пустыми).
    /// </summary>
    public static (string Key, string Comment) SplitCustomEntry(string? entry)
    {
        var parts = (entry ?? string.Empty).Split('\t');
        var key = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var comment = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return (key, comment);
    }

    /// <summary>
    /// Фильтрует каталог параметров по подстроке (issue #68): совпадает ключ или
    /// описание; регистр не учитывается. Пустой/пробельный запрос возвращает исходный
    /// список без изменений. Чистая функция — используется для поиска над справочником
    /// окна «Запустить с выбором параметров» (WPF и Avalonia).
    /// </summary>
    public static IReadOnlyList<OneCLaunchParameterReference> FilterReferenceCatalog(
        IReadOnlyList<OneCLaunchParameterReference>? entries, string? query)
    {
        if (entries is null)
            return Array.Empty<OneCLaunchParameterReference>();

        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
            return entries;

        return entries
            .Where(e => e is not null &&
                (e.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                 || (e.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
    }

    /// <summary>
    /// Сдвигает пользовательский параметр в списке справочника (issue #68) на
    /// <paramref name="delta"/> позиций (1 — ниже, -1 — выше) и возвращает true,
    /// если запись с ключом <paramref name="key"/> (без учёта регистра) найдена
    /// и сдвиг выполнен. Края списка не пересекаются (сдвиг «по месту» не меняет
    /// порядок). Встроенные ключи каталога не затрагиваются — работает только
    /// со списком пользовательских записей.
    /// </summary>
    public static bool MoveCustomParameter(IList<string> customParameters, string key, int delta)
    {
        if (customParameters is null || string.IsNullOrWhiteSpace(key) || delta == 0)
            return false;

        var targetKey = key.Trim();
        for (var i = 0; i < customParameters.Count; i++)
        {
            var (candidateKey, _) = SplitCustomEntry(customParameters[i]);
            if (!string.Equals(candidateKey, targetKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var j = i + delta;
            if (j < 0 || j >= customParameters.Count)
                return false;

            (customParameters[i], customParameters[j]) = (customParameters[j], customParameters[i]);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Компонует строку отображения варианта платформы для окна выбора: к чистой версии
    /// добавляет разрядность «(32)/(64)», если она явно задана и ещё не присутствует
    /// в строке. Соответствует прежней inline-логике окна настройки подключения.
    /// </summary>
    /// <param name="version">Версия платформы (например «8.3.25.1234»).</param>
    /// <param name="architecture">Разрядность («32»/«64») либо пусто.</param>
    /// <returns>Строка вида «8.3.25.1234 (64)» для отображения в списке версий.</returns>
    public static string ComposePlatformVersionDisplay(string? version, string? architecture)
    {
        var current = version ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(architecture)
            && architecture is "32" or "64"
            && !current.Contains('('))
        {
            current = $"{current} ({architecture})".Trim();
        }
        return current;
    }

    /// <summary>
    /// Признак токена, целиком заключённого в двойные кавычки («"webuser"»).
    /// Такой токен — значение предыдущего ключа, а не самостоятельный аргумент.
    /// </summary>
    private static bool IsFullyQuotedToken(string token)
        => token.Length >= 2 && token[0] == '"' && token[^1] == '"';

    /// <summary>Снимает внешние двойные кавычки с токена.</summary>
    private static string Unquote(string token)
        => token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1]
            : token;

    /// <summary>
    /// Разделяет строку на токены по пробелам, сохраняя содержимое двойных кавычек
    /// целиком (значения с пробелами не разбиваются).
    /// </summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }
}