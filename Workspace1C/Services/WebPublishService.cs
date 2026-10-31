using System.IO;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Веб-сервер для публикации базы утилитой webinst.</summary>
public enum WebServerKind
{
    Iis,
    Apache24,
    Apache22
}

/// <summary>Параметры публикации информационной базы на веб-сервере.</summary>
public sealed class WebPublishRequest
{
    public WebServerKind Server { get; set; } = OperatingSystem.IsWindows() ? WebServerKind.Iis : WebServerKind.Apache24;

    /// <summary>Имя виртуального каталога (часть адреса: http://host/&lt;имя&gt;).</summary>
    public string VirtualDirectory { get; set; } = string.Empty;

    /// <summary>Физический каталог публикации (default.vrd).</summary>
    public string PhysicalDirectory { get; set; } = string.Empty;

    /// <summary>Файл конфигурации Apache (httpd.conf / apache2.conf); для IIS не нужен.</summary>
    public string ApacheConfPath { get; set; } = string.Empty;

    /// <summary>Строка соединения с базой.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Публикация базы на веб-сервере утилитой платформы <c>webinst</c> (выпуск 1.0.0.69):
/// построение аргументов публикации и удаления, поиск webinst рядом с конфигуратором
/// версии платформы базы, имя виртуального каталога по умолчанию (транслитерация).
/// Публикация требует прав администратора (IIS, конфигурация Apache).
/// </summary>
public static class WebPublishService
{
    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya"
    };

    /// <summary>Имя виртуального каталога из имени базы: латиница, цифры, «_» и «-».</summary>
    public static string SuggestVirtualDirectory(string? infobaseName)
    {
        var sb = new StringBuilder();
        foreach (var ch in (infobaseName ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var latin))
                sb.Append(latin);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '.')
                sb.Append('_');
        }
        var result = sb.ToString().Trim('_');
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        return string.IsNullOrEmpty(result) ? "base" : result;
    }

    /// <summary>Физический каталог по умолчанию.</summary>
    public static string SuggestPhysicalDirectory(string virtualDirectory, WebServerKind server)
    {
        if (OperatingSystem.IsWindows())
        {
            return server == WebServerKind.Iis
                ? Path.Combine(@"C:\inetpub\wwwroot", virtualDirectory)
                : Path.Combine(@"C:\Apache24\htdocs", virtualDirectory);
        }
        return "/var/www/" + virtualDirectory;
    }

    /// <summary>Файл конфигурации Apache по умолчанию.</summary>
    public static string SuggestApacheConf()
    {
        if (OperatingSystem.IsWindows())
            return @"C:\Apache24\conf\httpd.conf";
        foreach (var candidate in new[] { "/etc/apache2/apache2.conf", "/etc/httpd/conf/httpd.conf", "/etc/httpd/httpd.conf" })
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return "/etc/apache2/apache2.conf";
    }

    /// <summary>
    /// Строит «синтетический» инстанс Apache из произвольного conf-файла на диске
    /// (issue #63, последний комментарий 7OH 2026-10-28 «файла в списке нет»): когда
    /// детектор инстансов ничего не нашёл (служба нестандартно называется/реестр
    /// недоступен) и пользователь выбрал или ввёл конфиг ВРУЧНУЮ, список файлов
    /// webinst обязан строиться и без детектора. Через
    /// <see cref="ApacheInstanceDetector.PopulateIncludes"/> читается include-граф:
    /// возвращаемый инстанс содержит <c>IncludeFiles</c> (главный конфиг + включаемые
    /// файлы, в т.ч. <c>1c8Aliases*.conf</c>), <c>Defines</c>, <c>DocumentRoot</c> и
    /// <c>ListenPorts</c>. ServerRoot выводится как родитель каталога конфига
    /// («…\Apache24\conf\httpd.conf» → «…\Apache24»). Возвращает null, если файл
    /// отсутствует или не читается — окно остаётся без списка, но не падает.
    /// </summary>
    public static ApacheInstance? BuildInstanceFromConf(string? confPath, WebServerKind kind)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(confPath) || !File.Exists(confPath))
                return null;

            var fullPath = Path.GetFullPath(confPath);
            var confDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var serverRoot = string.Empty;
            if (!string.IsNullOrWhiteSpace(confDirectory))
            {
                var parent = Directory.GetParent(confDirectory);
                if (parent is not null)
                    serverRoot = parent.FullName;
            }

            var instance = new ApacheInstance
            {
                Id = "manual",
                Name = "Apache",
                ServerRoot = serverRoot,
                ConfPath = fullPath,
                Htdocs = string.IsNullOrWhiteSpace(serverRoot) ? string.Empty : Path.Combine(serverRoot, "htdocs"),
                Kind = kind
            };

            var text = File.ReadAllText(fullPath);
            ApacheInstanceDetector.PopulateIncludes(instance, text, confDirectory);
            return instance;
        }
        catch
        {
            // Файл недоступен/битый — fallback-ветка остаётся без списка (не роняем окно).
            return null;
        }
    }

    /// <summary>Строка соединения webinst для базы (с завершающей «;»).</summary>
    public static string ConnectionStringFor(Infobase infobase) =>
        infobase.Connection.ToConnectionString().TrimEnd(';') + ";";

    private static string ServerSwitch(WebServerKind server) => server switch
    {
        WebServerKind.Apache22 => "-apache22",
        WebServerKind.Apache24 => "-apache24",
        _ => "-iis"
    };

    /// <summary>Проверка запроса: ключ локализации ошибки или null.</summary>
    public static string? Validate(WebPublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VirtualDirectory)
            || request.VirtualDirectory.IndexOfAny(new[] { '/', '\\', ' ', '"', '?', '#' }) >= 0)
            return "WebPublish.BadName";
        if (string.IsNullOrWhiteSpace(request.PhysicalDirectory))
            return "WebPublish.DirRequired";
        if (request.Server != WebServerKind.Iis && string.IsNullOrWhiteSpace(request.ApacheConfPath))
            return "WebPublish.ConfRequired";
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return "WebPublish.ConnRequired";
        return null;
    }

    /// <summary>Аргументы webinst для публикации.</summary>
    public static List<string> BuildPublishArguments(WebPublishRequest request)
    {
        var args = new List<string>
        {
            "-publish", ServerSwitch(request.Server),
            "-wsdir", request.VirtualDirectory.Trim(),
            "-dir", request.PhysicalDirectory.Trim(),
            "-connstr", request.ConnectionString.Trim()
        };
        if (request.Server != WebServerKind.Iis)
        {
            args.Add("-confpath");
            args.Add(request.ApacheConfPath.Trim());
        }
        return args;
    }

    /// <summary>Аргументы webinst для удаления публикации.</summary>
    public static List<string> BuildDeleteArguments(WebPublishRequest request)
    {
        var args = new List<string>
        {
            "-delete", ServerSwitch(request.Server),
            "-wsdir", request.VirtualDirectory.Trim(),
            "-dir", request.PhysicalDirectory.Trim()
        };
        if (request.Server != WebServerKind.Iis)
        {
            args.Add("-confpath");
            args.Add(request.ApacheConfPath.Trim());
        }
        return args;
    }

    /// <summary>Путь к webinst рядом с конфигуратором платформы базы или null.</summary>
    public static string? FindWebinst(Infobase infobase)
    {
        var designer = OneCLauncher.ResolveConfiguratorExecutable(infobase);
        if (string.IsNullOrEmpty(designer))
            return null;
        var dir = Path.GetDirectoryName(designer);
        if (string.IsNullOrEmpty(dir))
            return null;
        var path = Path.Combine(dir, OperatingSystem.IsWindows() ? "webinst.exe" : "webinst");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Кодировка чтения вывода webinst (issue #63, «каракули»): на русской Windows
    /// webinst пишет в консольную OEM-кодировку (cp866) — читаем её; на Linux и при
    /// недоступной кодовой странице — null (UTF-8 по умолчанию). Поставщик кодовых
    /// страниц регистрируется однократно (идемпотентно).
    /// </summary>
    public static Encoding? WebinstOutputEncoding() => ProcessRunner.ConsoleOutputEncoding();

    public static async Task<ProcessRunResult> RunAsync(Infobase infobase, IEnumerable<string> arguments,
        CancellationToken ct = default)
    {
        var webinst = FindWebinst(infobase);
        if (webinst is null)
            return new ProcessRunResult { StartError = LocalizationManager.T("WebPublish.WebinstNotFound") };
        return await ProcessRunner.RunAsync(webinst, arguments, Path.GetDirectoryName(webinst),
            TimeSpan.FromMinutes(5), null, WebinstOutputEncoding(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Адрес опубликованной базы на локальном веб-сервере (issue #63): при явном порте
    /// <paramref name="listenPort"/>, отличном от 80, добавляется «:порт» (слушатель
    /// Apache в конфиге — LISTEN 8027 → http://localhost:8027/имя/).
    /// </summary>
    public static string LocalUrl(string virtualDirectory, int? listenPort = null)
    {
        var portPart = listenPort is > 0 and not 80 ? ":" + listenPort : string.Empty;
        return "http://localhost" + portPart + "/" + (virtualDirectory ?? string.Empty).Trim() + "/";
    }
}
