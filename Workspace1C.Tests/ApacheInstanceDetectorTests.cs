using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (часть B): обнаружение инстансов Apache. Чистые функции разбора
/// командной строки службы (Windows) и вывода apache2ctl -V/-S (Linux)
/// тестируются без реальных служб/процессов.
/// </summary>
public sealed class ApacheInstanceDetectorTests
{
    [Fact]
    public void ApacheInstanceDetector_Windows_ParsesServiceImagePath_DAndF_Switches()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache24\\bin\\httpd.exe\" -d \"C:\\Apache24\" -f \"C:\\Apache24\\conf\\httpd.conf\"");

        Assert.NotNull(instance);
        Assert.Equal("Apache2.4", instance!.Id);
        Assert.Equal(@"C:\Apache24", instance.ServerRoot);
        Assert.Equal(@"C:\Apache24\conf\httpd.conf", instance.ConfPath);
        Assert.Equal(@"C:\Apache24\htdocs", instance.Htdocs);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_ParsesInlineConfigSwitch_Issue63()
    {
        // Некоторые обёртки записывают ImagePath слитно («-f=conf\httpd27.conf», «/f:…»):
        // конфиг, указанный в службе, всё равно должен стать ConfPath инстанса (issue #63,
        // «значения по умолчанию из файла, указанного в службе, а не из обычного httpd.conf»).
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f=conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance!.ConfPath, ignoreCase: true);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_ConfigFromService_PreferredOverDefaultHttpdConf()
    {
        // Если в службе указан -f conf\httpd27.conf, дефолтный ServerRoot\conf\httpd.conf
        // НЕ подставляется — основной конфиг ровно тот, что указан в службе (issue #63).
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d C:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.False(string.Equals(@"C:\Apache\Apache24\conf\httpd.conf", instance!.ConfPath,
            System.StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("httpd27.conf", instance.ConfPath, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_WithoutDOrF_UsesExeDirAndDefaultConf()
    {
        // Нет ключей -d/-f: ServerRoot выводится из каталога бинарника, conf — по умолчанию.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "httpd", "Apache", "Apache 2.2",
            "\"C:\\Apache22\\bin\\httpd.exe\" -k runservice");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache22", instance!.ServerRoot);
        Assert.Equal(@"C:\Apache22\conf\httpd.conf", instance.ConfPath);
        Assert.Equal(WebServerKind.Apache22, instance.Kind);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_NoApacheSwitches_ReturnsNull()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "some-service", "Some Service", "Not Apache", null);
        Assert.Null(instance);
    }

    [Fact]
    public void ApacheInstanceDetector_Linux_ParsesApacheCtl_V_ServerRootAndConfig()
    {
        const string apacheCtl = @"Server version: Apache/2.4.62 (Ubuntu)
Server built:   2024-08-07T13:24:45
Server's Module Magic Number: 20120211:129
Server loaded:  APR 1.5.2, APR-UTIL 1.5.4
Compiled using: APR 1.5.2, APR-UTIL 1.5.4
 -D HTTPD_ROOT=""/etc/apache2""
 -D SERVER_CONFIG_FILE=""conf/httpd.conf""
";
        const string apacheCtlS = @"VirtualHost configuration:
wildcard NameVirtualHosts and _default_ servers:
*:80                   is a NameVirtualHost
         default server localhost (/etc/apache2/sites-enabled/000-default.conf:1)
Main DocumentRoot: ""/var/www/html""
";

        var instance = ApacheInstanceDetector.ParseApacheCtl(
            apacheCtl, apacheCtlS, "/etc/apache2/apache2.conf", "/var/www");

        Assert.NotNull(instance);
        // Относительный SERVER_CONFIG_FILE резолвится против HTTPD_ROOT.
        Assert.Equal("/etc/apache2/conf/httpd.conf", instance!.ConfPath);
        Assert.Equal("/etc/apache2", instance.ServerRoot);
        Assert.Equal("/var/www/html", instance.Htdocs);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
        Assert.Contains("Apache/2.4.62", instance.Name);
    }

    [Fact]
    public void ApacheInstanceDetector_Linux_ParsesAbsoluteServerConfigFileAndFallsBackHtdocs()
    {
        const string apacheCtl = @"Server version: Apache/2.4.6 (CentOS)
 -D HTTPD_ROOT=""/etc/httpd""
 -D SERVER_CONFIG_FILE=""/etc/httpd/conf/httpd.conf""
";
        var instance = ApacheInstanceDetector.ParseApacheCtl(
            apacheCtl, null, "/etc/httpd/conf/httpd.conf", "/var/www");

        Assert.NotNull(instance);
        Assert.Equal("/etc/httpd/conf/httpd.conf", instance!.ConfPath);
        Assert.Equal("/etc/httpd", instance.ServerRoot);
        // Нет вывода -S: подставляется каталог htdocs по умолчанию.
        Assert.Equal("/var/www", instance.Htdocs);
    }

    [Fact]
    public void ApacheInstanceDetector_LooksLikeApache_MatchesHttpdAndApacheCaseInsensitive()
    {
        Assert.True(ApacheInstanceDetector.LooksLikeApache("Apache HTTP Server"));
        Assert.True(ApacheInstanceDetector.LooksLikeApache("httpd"));
        Assert.True(ApacheInstanceDetector.LooksLikeApache("Apache/2.4.62 (Ubuntu)"));
        Assert.False(ApacheInstanceDetector.LooksLikeApache("nginx"));
        Assert.False(ApacheInstanceDetector.LooksLikeApache(null));
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_SplitsFilesAndWildcardDirs()
    {
        var instance = new ApacheInstance { ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include extra/httpd-vhosts.conf\nIncludeOptional \"extra/sites/*.conf\"\n",
            @"C:\apache\conf");

        Assert.Single(instance.IncludeFiles);
        Assert.Equal(@"C:\apache\conf\extra\httpd-vhosts.conf", instance.IncludeFiles[0]);
        Assert.Contains(@"C:\apache\conf\extra\sites\*.conf", instance.IncludeDirs);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_ExpandsDefineVariables_Issue63()
    {
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort "8027"
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 80
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance, conf, @"C:\apache\conf");

        // Переменные из файла раскрыты («а переменные из файла кто подставлять будет сейчас?»).
        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Contains(8027, instance.ListenPorts);
        Assert.Contains(80, instance.ListenPorts);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_WithoutDefines_KeepsDocumentRootAndPort80()
    {
        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "DocumentRoot \"C:/apache/htdocs\"\nListen 80\n",
            @"C:\apache\conf");

        Assert.Equal(Path.GetFullPath(@"C:\apache\htdocs"), instance.DocumentRoot);
        Assert.Equal(new[] { 80 }, instance.ListenPorts);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_TwoPass_DefinesBeforeInclude_Issue63()
    {
        // issue #63, комментарий 7OH: «Переменные могут быть объявлены до include» — Define
        // живут в главном httpd.conf, а DocumentRoot/Listen — во включаемом файле. Двухпроходный
        // разбор: Define главного конфига видны внутри include-файла → путь и порт раскрыты.
        const string mainConf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 8027
        Include conf/extra/publish.conf
        """;
        const string includeConf = """
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance, mainConf, @"C:\apache\conf",
            _ => includeConf);

        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Contains(8027, instance.ListenPorts);

        // Include-файл попал в IncludeFiles. Относительный Include резолвится от ServerRoot
        // (семантика Apache), а не от каталога включающего файла:
        // «Include conf/extra/publish.conf» + ServerRoot C:\apache → C:\apache\conf\extra\publish.conf.
        Assert.Contains(@"C:\apache\conf\extra\publish.conf", instance.IncludeFiles);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_MissingIncludeFile_DoesNotFail()
    {
        // Включаемый файл отсутствует (нет на диске / readFileText вернул null) — разбор
        // главного конфига не падает, DocumentRoot/порты главного файла заполняются.
        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include extra/httpd-vhosts.conf\nDocumentRoot \"C:/apache/htdocs\"\nListen 80\n",
            @"C:\apache\conf",
            _ => null);

        Assert.Equal(Path.GetFullPath(@"C:\apache\htdocs"), instance.DocumentRoot);
        Assert.Equal(new[] { 80 }, instance.ListenPorts);
        Assert.Single(instance.IncludeFiles);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_UnresolvedVariables_DoNotLeakRawValues_Issue63()
    {
        // (г) Неразрешённая переменная (${...}) ни в одном файле графа не определена:
        // «сырое» значение в IncludeFiles/DocumentRoot не появляется — каталог остаётся
        // пустым (в окне fallback на Htdocs), include-цель с ${...} в список не попадает.
        var instance = new ApacheInstance
        {
            ServerRoot = @"C:\apache",
            ConfPath = @"C:\apache\httpd.conf",
            Htdocs = @"C:\apache\htdocs"
        };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include ${UNKNOWN}/extra/sites.conf\nDocumentRoot \"${UNKNOWN}/htdocs\"\nListen 80\n",
            @"C:\apache\conf",
            _ => null);

        Assert.Empty(instance.IncludeFiles);
        Assert.Empty(instance.DocumentRoot);
        Assert.Equal(new[] { 80 }, instance.ListenPorts);
    }

    [Fact]
    public void PopulateIncludes_UnresolvedAliasInclude_CollectsPatternForFallback_Issue63()
    {
        // Include-строка алиасов с переменной, которая НЕ определена ни в одном файле
        // графа («${SRVROOT}/conf/1c8Aliases${OneCPort}.conf»): цели в IncludeFiles нет
        // (сырые ${...} в UI не показываем), но шаблон сохраняется — VM-фаллбэк находит
        // используемый файл алиасов по include-строке (issue #63, комментарий 7OH
        // 2026-10-26: «Его можно же найти в строках с include?»).
        var instance = new ApacheInstance
        {
            ServerRoot = @"C:\apache",
            ConfPath = @"C:\apache\httpd.conf",
            Htdocs = @"C:\apache\htdocs"
        };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include \"${SRVROOT}/conf/1c8Aliases${OneCPort}.conf\"\n",
            @"C:\apache\conf",
            _ => null);

        Assert.Empty(instance.IncludeFiles);
        Assert.Single(instance.AliasTargetPatterns);
        Assert.Contains(instance.AliasTargetPatterns, p =>
            p.Contains("1c8Aliases", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(instance.AliasTargetPatterns, p => p.Contains("${", System.StringComparison.Ordinal));
    }

    [Fact]
    public void PopulateIncludes_ResolvedAliasInclude_StoredAsFullTargetInPatterns_Issue63()
    {
        // «Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"» при полном раскрытии
        // переменных (SRVROOT из включаемого файла) даёт реальную цель
        // «…\conf\1c8Aliases27.conf» — она же фиксируется в AliasTargetPatterns.
        const string mainConf = """
        Define OneCPort 27
        Include conf/site.conf
        """;
        const string siteConf = """
        Define SRVROOT "c:/Apache/Apache24"
        Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"
        """;
        const string aliasesConf = "Alias \"/Demo\" \"${SRVROOT}/htdocs/${OneCPort}/Demo/\"\n";

        var instance = new ApacheInstance
        {
            ServerRoot = @"C:\apache",
            ConfPath = @"C:\apache\httpd.conf",
            Htdocs = @"C:\apache\htdocs"
        };
        ApacheInstanceDetector.PopulateIncludes(instance, mainConf, @"C:\apache\conf",
            path => path.EndsWith("site.conf", System.StringComparison.OrdinalIgnoreCase) ? siteConf
                : path.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase) ? aliasesConf
                : null);

        Assert.Contains(instance.IncludeFiles, p =>
            p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(instance.AliasTargetPatterns, p =>
            p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(instance.AliasTargetPatterns, p =>
            p.Contains("${", System.StringComparison.Ordinal));
    }

    // ---------- Issue #63 (последний комментарий 7OH от 2026-10-29) ----------
    // У пользователя несколько служб Apache (Apache2.4, Apache2.4_51, Apache2.4_54,
    // Apache2.4_55) определялись с ОДНИМ конфигом httpd.conf — строка параметров
    // службы «-d c:\Apache\Apache24 -f conf\httpd27.conf» игнорировалась. Ниже —
    // устойчивое определение конфига из ImagePath для всех форм записи.

    [Fact]
    public void Windows_ServicesWithNumberSuffixes_EachGetsOwnConf_Issue63()
    {
        // Службы с номерами версий Apache в имени: каждая служба должна получить
        // СВОЙ конфиг из -f, а не общий дефолтный httpd.conf (issue #63).
        var c51 = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server 2.4.51", "Apache/2.4.51",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");
        var c54 = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_54", "Apache HTTP Server 2.4.54", "Apache/2.4.54",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd54.conf");
        var c55 = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_55", "Apache HTTP Server 2.4.55", "Apache/2.4.55",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd55.conf");

        Assert.NotNull(c51);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", c51!.ConfPath, ignoreCase: true);
        Assert.NotNull(c54);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd54.conf", c54!.ConfPath, ignoreCase: true);
        Assert.NotNull(c55);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd55.conf", c55!.ConfPath, ignoreCase: true);

        // Не все службы вырождаются в один «конфиг по умолчанию».
        var confs = new[] { c51.ConfPath, c54.ConfPath, c55.ConfPath }
            .Select(p => p.ToUpperInvariant()).Distinct().ToList();
        Assert.Equal(3, confs.Count);
    }

    [Fact]
    public void Windows_ImagePath_WithLineBreaks_ParsesDAndF_Issue63()
    {
        // Строка параметров службы из комментария 7OH записана с переносами строк
        // («-d\nc:\Apache\Apache24\n-f\nconf\httpd27.conf») — переносы являются
        // разделителями токенов, конфиг обязан извлечься.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d\r\nc:\\Apache\\Apache24\r\n-f\r\nconf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24", instance!.ServerRoot, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance.ConfPath, ignoreCase: true);
    }

    [Fact]
    public void Windows_ImagePath_UnbalancedQuote_StillParsesDAndF_Issue63()
    {
        // Непарная (оборванная) кавычка в ImagePath ломала токенизацию — вся строка
        // сливалась в один токен, -d/-f не извлекались и все службы получали дефолтный
        // httpd.conf (ровно это видно в логах 7OH от 2026-10-29). Запасная разбивка
        // по пробелам обязана восстановить ключи.
        // Строка начинается с открывающей кавычки, которую обёртка службы не закрыла.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24", instance!.ServerRoot, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance.ConfPath, ignoreCase: true);
    }

    [Fact]
    public void Windows_ImagePath_ConcatenatedF_ResolvesConf_Issue63()
    {
        // Сливная форма без разделителя «-fconf\httpd27.conf» (сторонние обёртки служб):
        // конфиг из строки запуска обязан стать ConfPath, а не дефолтный httpd.conf.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -fconf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance!.ConfPath, ignoreCase: true);

        // Контроль «-debug» не принимается за значение -d: «ebug» без пути/точки.
        var guard = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -debug -k runservice");
        Assert.NotNull(guard);
        Assert.Equal(@"C:\Apache\Apache24", guard!.ServerRoot, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd.conf", guard.ConfPath, ignoreCase: true);
    }

    [Fact]
    public void Windows_ServiceImagePath_KeepsRawCommandLine_Issue63()
    {
        // «Добавляем в отладку исходные данные службы» (7OH): исходная строка ImagePath
        // сохраняется в инстансе для лога CM_PUBLISH и fallback-поиска файла алиасов.
        const string imagePath =
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf";
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51", imagePath);

        Assert.NotNull(instance);
        Assert.Equal(imagePath, instance!.RawCommandLine);
    }

    [Fact]
    public void ExtractConfFileNameFromCommandLine_ConfAfterF_Issue63()
    {
        // Имя конфига из строки запуска службы: «-d c:\Apache\Apache24 -f conf\httpd27.conf»
        // → «httpd27.conf» — используется fallback-поиском файла алиасов (1c8Aliases27.conf),
        // когда ConfPath по какой-то причине не извлёкся (ситуация из логов 7OH).
        const string imagePath =
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf";
        Assert.Equal("httpd27.conf",
            ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(imagePath));

        // Сливная форма тоже распознаётся.
        Assert.Equal("httpd27.conf",
            ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(
                "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -fconf\\httpd27.conf"));
    }

    [Fact]
    public void ExtractConfFileNameFromCommandLine_NoConf_ReturnsNull()
    {
        Assert.Null(ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(null));
        Assert.Null(ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(string.Empty));
        Assert.Null(ApacheInstanceDetector.ExtractConfFileNameFromCommandLine(
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -k runservice"));
    }
}