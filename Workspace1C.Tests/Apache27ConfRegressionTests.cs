using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 «Доработка публикации баз» (последний комментарий 7OH от 2026-10-25,
/// источник требований): реальные конфиги пользователя из архива 27_conf.zip
/// (httpd27.conf — главный файл запуска через <c>-f conf\httpd27.conf</c>, Include
/// conf/httpd.conf, Define SRVROOT/OneCPort, DocumentRoot/Listen в httpd.conf,
/// второй уровень Include — 1c8Aliases27.conf) зафиксированы как фикстуры в
/// TestData/Apache27Conf и воспроизводят сценарий «Пока не удалось»: относительный
/// конфиг из ImagePath резолвится от ServerRoot, Include-граф раскрывается по всему
/// дереву, каталог публикации и порт Listen подставляются в окно публикации.
/// </summary>
public sealed class Apache27ConfRegressionTests
{
    private const string ServerRootDir = @"C:\Apache\Apache24";

    private static string TestDataDir => Path.Combine(FindRepoRoot(), "Workspace1C.Tests", "TestData", "Apache27Conf");

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestDataDir, fileName));

    /// <summary>
    /// Чтение включаемых файлов по имени (пути в конфигах пользователя — от
    /// <c>C:\Apache\Apache24</c>): файлы, отсутствующие в фикстурах
    /// (conf/extra/*.conf), не роняют разбор.
    /// </summary>
    private static string? ReadFixtureByPath(string path)
    {
        var fixture = Path.Combine(TestDataDir, Path.GetFileName(path));
        return File.Exists(fixture) ? File.ReadAllText(fixture) : null;
    }

    [Fact]
    public void Windows_ImagePath_RelativeConf_IsResolvedAgainstServerRoot()
    {
        // Строка запуска из комментария: -d c:\Apache\Apache24 -f conf\httpd27.conf.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24", instance!.ServerRoot, ignoreCase: true);
        // Относительный -f резолвится от ServerRoot: conf\httpd27.conf → C:\Apache\Apache24\conf\httpd27.conf.
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance.ConfPath, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\htdocs", instance.Htdocs, ignoreCase: true);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
    }

    [Fact]
    public void PopulateIncludes_RealUserConfigs_ResolvesDocumentRootAndListenAcrossGraph()
    {
        // Главный файл запуска — httpd27.conf (передаётся через -f conf\httpd27.conf),
        // а не httpd.conf. ServerRoot из -d c:\Apache\Apache24.
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };

        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        // DocumentRoot "${SRVROOT}/htdocs/${OneCPort}" = c:/Apache/Apache24/htdocs/27:
        // Define OneCPort — в главном httpd27.conf, Define SRVROOT — во включаемом
        // httpd.conf; общий словарь по всему графу раскрыл путь.
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), instance.DocumentRoot, ignoreCase: true);

        // Listen "80${OneCPort}" = 8027 и Listen "192.168.2.11:80${OneCPort}" = 192.168.2.11:8027.
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);

        // Include-граф раскрыт рекурсивно по обоим уровням: conf/httpd.conf (уровень 1)
        // и ${SRVROOT}/conf/1c8Aliases27.conf (уровень 2, из httpd.conf).
        Assert.Contains(instance.IncludeFiles, p => p.EndsWith("httpd.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(instance.IncludeFiles, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PopulateIncludes_RelativeInclude_IsResolvedFromServerRoot()
    {
        // «Include conf/httpd.conf» из conf\httpd27.conf Apache трактует от ServerRoot
        // (C:\Apache\Apache24), а не от каталога включающего файла (C:\Apache\Apache24\conf).
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Kind = WebServerKind.Apache24
        };

        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        Assert.Contains(instance.IncludeFiles, p => string.Equals(
            Path.GetFullPath(@"C:\Apache\Apache24\conf\httpd.conf"), p, System.StringComparison.OrdinalIgnoreCase));
        // Ошибочного «conf\conf\httpd.conf» (от каталога включающего файла) быть не должно.
        Assert.DoesNotContain(instance.IncludeFiles, p =>
            p.Contains("conf" + Path.DirectorySeparatorChar + "conf" + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ViewModel_RealUserConfigs_FillsPhysicalDirAndUrlWithPort()
    {
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Каталог публикации = DocumentRoot из конфига + имя виртуального каталога.
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
            vm.PhysicalDirectory, ignoreCase: true);
        // URL учитывает порт Listen (8027), а не «сырое» значение без порта.
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
        Assert.Equal(WebServerKind.Apache24, vm.BuildRequest().Server);
    }

    [Fact]
    public void ViewModel_RealUserConfigs_ConfTargetsContainAliasesFile()
    {
        // Issue #63, последний комментарий: «пропала опция … файла алиасов выборе нет,
        // а значения по умолчанию из обычного файла, а не который указан в службе».
        // Основной конфиг — httpd27.conf из службы (-f conf\httpd27.conf), а не «обычный»
        // httpd.conf; второй ComboBox webinst показывает include-файлы графа, включая
        // 1c8Aliases27.conf.
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);

        Assert.True(vm.HasConfTargets);
        // Второй ComboBox webinst: основной конфиг службы и включаемые файлы графа
        // (в т.ч. файл алиасов 1c8Aliases27.conf).
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("httpd27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файлы");
        // Issue #63 (последний комментарий 7OH 2026-10-26: «Зачем мне все файлы алиасов?»):
        // в списке ровно ОДИН файл алиасов — используемый из Include-графа (включён строкой
        // «Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"»), а не все 1c8Aliases*.conf
        // из каталога конфига.
        var aliasTargets = vm.ConfTargetPaths
            .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(aliasTargets);
        Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], System.StringComparison.OrdinalIgnoreCase);
        // Issue #63 (комментарий 7OH 2026-10-30): при наличии файла алиасов 1c8Aliases* —
        // он выбирается в списке webinst ПО УМОЛЧАНИЮ.
        var aliasIndex = -1;
        for (var i = 0; i < vm.ConfTargetPaths.Count; i++)
        {
            if (Path.GetFileName(vm.ConfTargetPaths[i]).StartsWith("1c8Aliases", System.StringComparison.OrdinalIgnoreCase))
            {
                aliasIndex = i;
                break;
            }
        }
        Assert.True(aliasIndex >= 0, "файл алиасов должен быть в списке");
        Assert.Equal(aliasIndex, vm.ConfTargetIndex);
        Assert.Equal(Path.GetFullPath(Path.Combine(ServerRootDir, "conf", "1c8Aliases27.conf")),
            vm.ApacheConfPath, ignoreCase: true);
    }

    [Fact]
    public void PopulateIncludes_WildcardAliasesInclude_ResolvesAliasFileAndConfTargets()
    {
        // Issue #63 (последний комментарий 7OH от 2026-10-26: «файл алиаса по-прежнему не
        // вернулся»): include алиасов может идти через IncludeOptional с маской каталога
        // («IncludeOptional aliases/*.conf» — типовой способ подключения 1c8Aliases*.conf).
        // Детектор обязан раскрыть маску в реальные файлы (IncludeFiles), а VM — показать
        // пункт «Include: 1c8Aliases27.conf» в списке webinst.
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Apache27Aliases_" + Guid.NewGuid().ToString("N"));
        try
        {
            var aliasesDir = Path.Combine(tempDir, "aliases");
            Directory.CreateDirectory(aliasesDir);
            File.WriteAllText(Path.Combine(aliasesDir, "1c8Aliases27.conf"),
                "# 1c publication\nAlias \"/Demo\" \"${SRVROOT}/htdocs/27/Demo/\"\n");
            File.WriteAllText(Path.Combine(aliasesDir, "ignored.txt"), "not a conf\n");
            var mainPath = Path.Combine(tempDir, "httpd27.conf");
            File.WriteAllText(mainPath,
                "Define SRVROOT \"" + tempDir.Replace("\\", "/") + "\"\r\n" +
                "Define OneCPort 27\r\n" +
                "IncludeOptional aliases/*.conf\r\n");

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = mainPath,
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24
            };
            ApacheInstanceDetector.PopulateIncludes(instance, File.ReadAllText(mainPath), tempDir);

            // Маска раскрыта в реальный файл алиасов (aliases\1c8Aliases27.conf) —
            // файл в IncludeFiles, а не только «шаблон» каталога в IncludeDirs.
            Assert.Contains(instance.IncludeFiles, p =>
                p.EndsWith(Path.Combine("aliases", "1c8Aliases27.conf"), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(instance.IncludeFiles, p => p.EndsWith("ignored.txt", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);

            // Список webinst: основной конфиг службы + include-файл алиасов.
            Assert.True(vm.HasConfTargets);
            Assert.Contains(vm.ConfTargetPaths, p =>
                p.EndsWith(Path.Combine("aliases", "1c8Aliases27.conf"), StringComparison.OrdinalIgnoreCase));
            Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файл алиасов");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    [Fact]
    public void PopulateIncludes_RealFixturesOnDisk_EndToEnd_ConfTargetsAndDefaults_Issue63()
    {
        // Воспроизведение сценария 7OH (комментарий #issuecomment-6419272270) «как на
        // диске»: фикстуры TestData/Apache27Conf копируются во временный каталог, детектор
        // читает файлы ПО-НАСТОЯЩЕМУ (File.ReadAllText), Include-граф раскрывается через
        // conf\httpd.conf, файл алиасов 1c8Aliases27.conf попадает в ConfTargets ровно один,
        // каталог публикации «…\htdocs\27» и порт 8027 подставляются в окно.
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_E2E_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            foreach (var fileName in new[] { "httpd27.conf", "httpd.conf", "1c8Aliases27.conf" })
                File.Copy(Path.Combine(TestDataDir, fileName), Path.Combine(confDir, fileName), overwrite: true);

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = Path.Combine(confDir, "httpd27.conf"),
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24
            };
            ApacheInstanceDetector.PopulateIncludes(instance,
                File.ReadAllText(instance.ConfPath), confDir);

            // DocumentRoot/Listen из include-графа (httpd.conf), переменные из обоих файлов.
            Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), instance.DocumentRoot, ignoreCase: true);
            Assert.Equal(new[] { 8027 }, instance.ListenPorts);
            Assert.Contains(instance.IncludeFiles, p =>
                p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            // В ConfTargets ровно ОДИН файл алиасов — используемый 1c8Aliases27.conf.
            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);

            // Каталог публикации и URL по умолчанию из конфига службы.
            Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
                vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);

            // Смена файла webinst (на файл алиасов) пересчитывает каталог/порт корректно.
            var aliasIndex = vm.ConfTargetPaths
                .Select((p, i) => (p, i))
                .First(t => t.p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase)).i;
            vm.ConfTargetIndex = aliasIndex;
            Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
                vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    [Fact]
    public void PopulateIncludes_DefineAfterInclude_StillResolvesAliasAndDefaults_Issue63()
    {
        // «Переменные могут быть объявлены до include» — а если Define стоит ПОСЛЕ
        // Include-строки (реальный вариант раскладки), двухпроходный разбор всё равно
        // должен раскрыть ${SRVROOT}/${OneCPort} для DocumentRoot/Listen/Include алиасов.
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };

        const string mainConf = """
        Include conf/site.conf
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 27
        """;
        const string siteConf = """
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}"
        Listen "80${OneCPort}"
        Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"
        """;
        const string aliasesConf = "Alias \"/Demo\" \"${SRVROOT}/htdocs/${OneCPort}/Demo/\"\n";

        ApacheInstanceDetector.PopulateIncludes(instance, mainConf,
            Path.Combine(ServerRootDir, "conf"),
            path => path.EndsWith("site.conf", StringComparison.OrdinalIgnoreCase) ? siteConf
                : path.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase) ? aliasesConf
                : null);

        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), instance.DocumentRoot, ignoreCase: true);
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);
        Assert.Contains(instance.IncludeFiles, p =>
            p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        var aliasTargets = vm.ConfTargetPaths
            .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(aliasTargets);
        Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
            vm.PhysicalDirectory, ignoreCase: true);
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }

    [Fact]
    public void PopulateIncludes_RealConfigsOnDisk_ExactPaths_ReadsAliasFileAndConfTargets()
    {
        // Воспроизведение сценария 7OH «как на реальной машине» (последний комментарий
        // 2026-10-27: «Файл алиасов в списке не появился»): конфиги лежат на диске по
        // точным путям, SRVROOT указывает на тот же каталог (в отличие от E2E-теста,
        // где SRVROOT указывает на несуществующий c:/Apache/Apache24 и файл алиасов
        // добавляется в IncludeFiles, но НЕ читается). Include-граф обязан ПРОЧИТАТЬ
        // 1c8Aliases27.conf, а VM — показать его в ConfTargets ровно один раз.
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_Exact_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            // Переписываем фикстуры: SRVROOT → tempDir (пути с /), чтобы include-цели
            // резолвились в реальные файлы на диске (как на машине пользователя).
            var srvRoot = tempDir.Replace("\\", "/");
            File.WriteAllText(Path.Combine(confDir, "httpd27.conf"), ReadFixture("httpd27.conf"));
            File.WriteAllText(Path.Combine(confDir, "httpd.conf"),
                ReadFixture("httpd.conf").Replace("c:/Apache/Apache24", srvRoot));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"),
                ReadFixture("1c8Aliases27.conf").Replace("c:/Apache/Apache24", srvRoot));

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = Path.Combine(confDir, "httpd27.conf"),
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24
            };
            ApacheInstanceDetector.PopulateIncludes(instance,
                File.ReadAllText(instance.ConfPath), confDir);

            // Файл алиасов ПРОЧИТАН из графа (путь существует на диске), DocumentRoot
            // и порт раскрыты по общему Define-графу.
            Assert.Contains(instance.IncludeFiles, p =>
                p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "htdocs", "27")),
                instance.DocumentRoot, ignoreCase: true);
            Assert.Equal(new[] { 8027 }, instance.ListenPorts);

            // Финальный словарь Define сохранён в инстансе; include-шаблоны алиасов
            // раскрыты по нему (в путях fallback-кандидатов нет сырых ${...}).
            Assert.True(instance.Defines.ContainsKey("OneCPort"));
            Assert.All(instance.AliasTargetPatterns, p => Assert.DoesNotContain("${", p));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            // В ConfTargets ровно ОДИН файл алиасов — используемый 1c8Aliases27.conf.
            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "htdocs", "27", "demo_27")),
                vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
            // В UI нет сырых ${...}.
            Assert.DoesNotContain("${", vm.PhysicalDirectory);
            Assert.DoesNotContain("${", vm.Url);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    /// <summary>
    /// Fallback (issue #63, «файл алиасов в списке не появился»): если Include-граф по
    /// какой-то причине НЕ прочитал файл алиасов, но include-строка
    /// («Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"») раскрыта в полный путь
    /// ВНЕ стандартных каталогов поиска (другой диск, conf\vhosts\...) — VM берёт файл
    /// по ТОЧНОМУ пути Include-строки (в 1.0.0.97 искала только имя файла рядом с конфигом).
    /// </summary>
    [Fact]
    public void Fallback_ExpandedAliasIncludePathOutsideStandardDirs_AddsExactlyOneAliasFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_Vhosts_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            var vhostsDir = Path.Combine(confDir, "vhosts");
            Directory.CreateDirectory(vhostsDir);
            var aliasPath = Path.Combine(vhostsDir, "1c8Aliases27.conf");
            File.WriteAllText(aliasPath, "define myDocumentRoot \"C:/Apache/Apache24/htdocs/27\"\n");

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = Path.Combine(confDir, "httpd27.conf"),
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24,
                // Include-граф не прочитал файл (IncludeFiles пуст), но include-строка
                // раскрыта в полный путь (так делает PopulateIncludes после стабилизации).
                IncludeFiles = Array.Empty<string>(),
                AliasTargetPatterns = new[] { aliasPath },
                Defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OneCPort"] = "27",
                    ["SRVROOT"] = "C:/Apache/Apache24"
                }
            };

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.Equal(Path.GetFullPath(aliasPath), aliasTargets[0], ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    /// <summary>
    /// Fallback (issue #63): номер инстанса берётся из значения Define
    /// («Define OneCPort 27» → суффикс «27»), когда ни DocumentRoot, ни Listen,
    /// ни Include-граф суффикс не дали, — VM находит «1c8Aliases27.conf» рядом
    /// с конфигом службы по словарю Define инстанса (1.0.0.98).
    /// </summary>
    [Fact]
    public void Fallback_DefinePortValue_AddsAliasFileNearConf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_Define_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"),
                "define myDocumentRoot \"C:/Apache/Apache24/htdocs/27\"\n");

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = Path.Combine(confDir, "httpd27.conf"),
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24,
                IncludeFiles = Array.Empty<string>(),
                AliasTargetPatterns = Array.Empty<string>(),
                ListenPorts = Array.Empty<int>(),
                DocumentRoot = string.Empty,
                Defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OneCPort"] = "27",
                    ["SRVROOT"] = "C:/Apache/Apache24"
                }
            };

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    // ---------- Issue #63, последний комментарий 7OH от 2026-10-29 (собственные конфиги служб) ----------

    /// <summary>
    /// Сценарий из логов 7OH: на машине службы Apache2.4, Apache2.4_51, Apache2.4_54,
    /// Apache2.4_55 определялись с ОДНИМ конфигом httpd.conf. Ожидание: конфиг БЕРЁТСЯ
    /// из строки параметров службы (-f conf\httpd27.conf) для каждого инстанса, а не
    /// подставляется дефолтный httpd.conf; у служб с разными -f — разные конфиги.
    /// </summary>
    [Fact]
    public void Windows_MultipleServices_WithNumbers_ResolveOwnConfigs_Issue63()
    {
        var plain = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24");
        var v51 = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");
        var v54 = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4_54", "Apache HTTP Server", "Apache/2.4.54",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(plain);
        Assert.NotNull(v51);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd.conf", plain!.ConfPath, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", v51!.ConfPath, ignoreCase: true);

        // Одна и та же строка запуска («-f conf\httpd27.conf») у РАЗНЫХ служб даёт
        // один конфиг httpd27.conf (а не общий дефолтный httpd.conf), при этом
        // служба без -f остаётся на обычном httpd.conf.
        Assert.Equal(v51.ConfPath, v54!.ConfPath, ignoreCase: true);
        Assert.NotEqual(plain.ConfPath, v51.ConfPath, StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith("httpd27.conf", v51.ConfPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// End-to-end по реальным конфигам (фикстуры TestData/Apache27Conf — файлы из
    /// 27_conf.zip / _27conf_extracted): служба Apache2.4_51 со строкой параметров
    /// «-d <temp> -f conf\httpd27.conf» → конфиг httpd27.conf → include-граф прочитан
    /// (включая 1c8Aliases27.conf) → в окне каталог «…\htdocs\27\demo_27» и URL 8027.
    /// </summary>
    [Fact]
    public void Windows_ServiceWithF_Httpd27Conf_EndToEnd_Issue63()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_ServiceF_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            // SRVROOT в фикстурах → tempDir, чтобы include-цели резолвились в реальные
            // файлы на диске (как на машине пользователя), иначе as-включаемый
            // 1c8Aliases27.conf не прочитается.
            var srvRoot = tempDir.Replace("\\", "/");
            File.WriteAllText(Path.Combine(confDir, "httpd27.conf"), ReadFixture("httpd27.conf"));
            File.WriteAllText(Path.Combine(confDir, "httpd.conf"),
                ReadFixture("httpd.conf").Replace("c:/Apache/Apache24", srvRoot));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"),
                ReadFixture("1c8Aliases27.conf").Replace("c:/Apache/Apache24", srvRoot));

            // Строка параметров службы из комментария 7OH: -d <temp> -f conf\httpd27.conf.
            var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
                "Apache2.4_51", "Apache HTTP Server", "Apache/2.4.51",
                "\"" + Path.Combine(tempDir, "bin", "httpd.exe") + "\" -d " + tempDir + " -f conf\\httpd27.conf");

            Assert.NotNull(instance);
            Assert.Equal(Path.Combine(tempDir, "conf", "httpd27.conf"), instance!.ConfPath, ignoreCase: true);
            Assert.Equal(tempDir, instance.ServerRoot, ignoreCase: true);

            ApacheInstanceDetector.PopulateIncludes(instance,
                File.ReadAllText(instance.ConfPath), confDir);

            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "htdocs", "27")),
                instance.DocumentRoot, ignoreCase: true);
            Assert.Equal(new[] { 8027 }, instance.ListenPorts);
            Assert.Contains(instance.IncludeFiles, p =>
                p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            Assert.Contains(vm.ConfTargetPaths, p =>
                p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "htdocs", "27", "demo_27")),
                vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    /// <summary>
    /// Сценарий из логов 7OH (2026-10-29): даже когда детектор по какой-то причине
    /// выдал дефолтный ConfPath=httpd.conf (а реальная строка параметров службы
    /// содержит «-f conf\httpd27.conf»), fallback-поиск файла алиасов обязан найти
    /// 1c8Aliases27.conf — номер инстанса берётся из исходной строки ImagePath
    /// (суффикс «27» из «httpd27.conf»).
    /// </summary>
    [Fact]
    public void Fallback_AliasSuffixFromServiceImagePath_FindsAliasNearConf_Issue63()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_ImagePath_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);

            // Из фиксируемого httpd.conf: Define SRVROOT есть, Define OneCPort — НЕТ
            // (он в httpd27.conf, который детектор не прочитал) → include-строка
            // «Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"» не раскрывается,
            // файл алиасов в IncludeFiles не попадает.
            File.WriteAllText(Path.Combine(confDir, "httpd.conf"), ReadFixture("httpd.conf"));
            // Сам файл алиасов лежит рядом с конфигом (как на машине пользователя).
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"),
                ReadFixture("1c8Aliases27.conf"));

            var instance = new ApacheInstance
            {
                Id = "Apache2.4_51",
                Name = "Apache 2.4_51",
                ConfPath = Path.Combine(confDir, "httpd.conf"),
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24,
                // Строка параметров службы (как в комментарии 7OH) — конфиг обязан
                // быть httpd27.conf, но моделируем не извлечённый -f: ConfPath остался
                // дефолтным httpd.conf, а ImagePath реальный.
                RawCommandLine = "\"" + Path.Combine(tempDir, "bin", "httpd.exe") + "\" -d " + tempDir + " -f conf\\httpd27.conf"
            };
            ApacheInstanceDetector.PopulateIncludes(instance,
                File.ReadAllText(instance.ConfPath), confDir);

            // Include-граф httpd.conf НЕ дал файл алиасов (OneCPort не определён)...
            Assert.DoesNotContain(instance.IncludeFiles, p =>
                p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);

            // ...но fallback-поиск по суффиксу из ImagePath («-f conf\httpd27.conf» →
            // «27») находит 1c8Aliases27.conf рядом с конфигом (ровно один файл).
            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }
}
