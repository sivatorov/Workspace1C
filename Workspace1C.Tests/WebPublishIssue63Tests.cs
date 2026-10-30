using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 «Доработка публикации баз» (комментарий 7OH от 2026-10-25): пункт
/// «Публикация на веб-сервере» перенесён из «Утилиты» в контекстное меню базы
/// («Администрирование», выше истории запусков); парсер конфигов Apache понимает
/// DocumentRoot (с подкаталогом сайта) и Listen; URL публикации учитывает порт.
/// </summary>
public sealed class WebPublishIssue63Tests
{
    private const string ServerRootDir = @"C:\Apache\Apache24";

    // ---------- Перенос пункта в контекстное меню ----------

    [Fact]
    public void UtilitiesMenu_DoesNotContainWebPublish()
    {
        Assert.Null(UtilitiesMenuCatalog.Find("WebPublish"));

        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        Assert.DoesNotContain("WebPublish", ids);
    }

    [Fact]
    public void ContextMenu_CatalogContainsWebPublish_InAdministration()
    {
        var entry = ContextMenuCatalog.Find("WebPublish");

        Assert.NotNull(entry);
        Assert.Equal(ContextMenuCommandKind.WebPublish, entry!.CommandKind);
        Assert.Equal(ContextMenuCatalog.GroupAdministration, entry.DefaultGroup);
    }

    [Fact]
    public void ContextMenu_DefaultLayout_WebPublishAboveLaunchHistory()
    {
        var layout = ContextMenuLayouts.Default();
        var ids = CollectOrdered(layout.Root);

        Assert.Contains("WebPublish", ids);
        Assert.True(ids.IndexOf("WebPublish") < ids.IndexOf("LaunchHistory"),
            "Публикация должна идти выше «Истории запусков».");
    }

    /// <summary>Собирает ItemId узлов компоновки в порядке дерева (для проверки порядка).</summary>
    private static List<string> CollectOrdered(IEnumerable<ContextMenuNode> nodes)
    {
        var result = new List<string>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", System.StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(node.ItemId))
                result.Add(node.ItemId);
            result.AddRange(CollectOrdered(node.Children ?? new List<ContextMenuNode>()));
        }
        return result;
    }

    // ---------- Парсер DocumentRoot / Listen ----------

    [Fact]
    public void Parser_ParsesDocumentRoot_RelativeToConfDir()
    {
        const string conf = "# config\nServerRoot \"C:/Apache/Apache24\"\nDocumentRoot \"htdocs/27\"\nListen 8027\n";

        var documentRoot = ApacheConfIncludeParser.ParseDocumentRoot(conf, @"C:\Apache\Apache24");

        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), documentRoot);
    }

    [Fact]
    public void Parser_ParsesDocumentRoot_Absolute_KeepsPath()
    {
        const string conf = "DocumentRoot \"C:/Apache/Apache24/htdocs/27\"\n";

        var documentRoot = ApacheConfIncludeParser.ParseDocumentRoot(conf, @"C:\Apache\Apache24");

        Assert.EndsWith(@"htdocs\27", Path.GetFullPath(documentRoot!));
    }

    [Fact]
    public void Parser_ParsesListenPorts_WithAddresses()
    {
        const string conf = "Listen 80\nListen 0.0.0.0:8027\nListen [::]:8080\n";

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Equal(new[] { 80, 8027, 8080 }, ports);
    }

    [Fact]
    public void Parser_ListenPorts_NoDuplicates()
    {
        const string conf = "Listen 80\nListen 0.0.0.0:80\n";

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Single(ports);
        Assert.Equal(80, ports[0]);
    }

    // ---------- URL с портом ----------

    [Fact]
    public void LocalUrl_Port80_NoPortInUrl()
    {
        Assert.Equal("http://localhost/demo_27/", WebPublishService.LocalUrl("demo_27", 80));
    }

    [Fact]
    public void LocalUrl_CustomListenPort_IncludesPort()
    {
        Assert.Equal("http://localhost:8027/demo_27/", WebPublishService.LocalUrl("demo_27", 8027));
    }

    [Fact]
    public void LocalUrl_NoPortParameter_UsesNoPort()
    {
        Assert.Equal("http://localhost/demo_27/", WebPublishService.LocalUrl("demo_27"));
    }

    // ---------- WebPublishViewModel: DocumentRoot и порт из инстанса ----------

    [Fact]
    public void ViewModel_PhysicalDirectoryAndUrl_UseInstanceDocumentRootAndListenPort()
    {
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = @"C:\Apache\Apache24\conf\httpd.conf",
            ServerRoot = @"C:\Apache\Apache24",
            Htdocs = @"C:\Apache\Apache24\htdocs",
            DocumentRoot = @"C:\Apache\Apache24\htdocs\27",
            ListenPorts = new[] { 8027 },
            Kind = WebServerKind.Apache24
        };
        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"D:\1C\test"
            }
        };

        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        // Выбираем инстанс Apache и имя виртуального каталога.
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"), vm.PhysicalDirectory);
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }

    [Fact]
    public void ViewModel_PopulatedFromDefineConfig_ResolvesDocumentRootAndPort_Issue63()
    {
        // Конфиг с директивами Define: парсер детектора раскрывает ${SRVROOT}/${OneCPort};
        // VM получает инстанс уже с раскрытыми значениями (замечание 7OH: «а переменные
        // из файла кто подставлять будет сейчас?», «Порт … так и не подставлен»).
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 8027
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = @"C:\Apache\Apache24\conf\httpd.conf",
            ServerRoot = @"C:\Apache\Apache24",
            Htdocs = @"C:\Apache\Apache24\htdocs",
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, conf, @"C:\Apache\Apache24\conf");

        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Регистр диска в конфиге пользователя — строчный («c:/Apache/…»), а в исходном
        // sibling-тесте — прописной: сравниваем без учёта регистра пути.
        Assert.True(string.Equals(
                vm.PhysicalDirectory,
                Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file\demo_27"),
                System.StringComparison.OrdinalIgnoreCase),
            $"Физический каталог не совпал: {vm.PhysicalDirectory}");
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }

    // ---------- Issue #63 (последний комментарий 7OH от 2026-10-26):
    // конфиг службы как основной + файл алиасов в списке webinst ----------

    [Fact]
    public void ViewModel_DefaultsFromServiceConfig_NotFromDefaultHttpdConf_Issue63()
    {
        // Служба: -d c:\Apache\Apache24 -f conf\httpd27.conf (строка запуска из комментария).
        // Детектор обязан отдать конфиг СЛУЖБЫ (httpd27.conf) как основной — значения по
        // умолчанию (каталог публикации с подкаталогом сайта, порт Listen) берутся из его
        // Include-графа, а не из «обычного» httpd.conf (замечание 7OH).
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\conf\httpd27.conf"),
            instance!.ConfPath, ignoreCase: true);

        const string mainConf = """
        Define OneCPort 27
        Include conf/httpd.conf
        """;
        const string includeConf = """
        Define SRVROOT "c:/Apache/Apache24"
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}"
        Listen "80${OneCPort}"
        Include "${SRVROOT}/conf/1c8Aliases27.conf"
        """;
        const string aliasesConf = "Alias \"/Demo\" \"${SRVROOT}/htdocs/${OneCPort}/Demo/\"\n";

        string? ReadFixtureByFileName(string path) => path switch
        {
            _ when path.EndsWith("httpd27.conf", System.StringComparison.OrdinalIgnoreCase) => mainConf,
            _ when path.EndsWith("httpd.conf", System.StringComparison.OrdinalIgnoreCase) => includeConf,
            _ when path.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase) => aliasesConf,
            _ => null
        };

        ApacheInstanceDetector.PopulateIncludes(instance, mainConf,
            Path.GetDirectoryName(instance.ConfPath), ReadFixtureByFileName);

        // Значения по умолчанию из конфига службы: DocumentRoot «htdocs\27» и порт 8027.
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), instance.DocumentRoot, ignoreCase: true);
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);
        Assert.Contains(instance.IncludeFiles, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Каталог публикации по умолчанию и адрес — из конфига службы (не «обычного» httpd.conf).
        Assert.True(string.Equals(vm.PhysicalDirectory,
                Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
                System.StringComparison.OrdinalIgnoreCase),
            $"Физический каталог не совпал: {vm.PhysicalDirectory}");
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);

        // Второй ComboBox webinst содержит основной конфиг службы и файл алиасов.
        Assert.True(vm.HasConfTargets);
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("httpd27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файлы");
        // Issue #63 (комментарий 7OH 2026-10-30): при наличии файла алиасов 1c8Aliases* —
        // он выбирается в списке ПО УМОЛЧАНИЮ (webinst пишет блок публикации туда).
        var aliasIndex = IndexOfAlias(vm.ConfTargetPaths);
        Assert.True(aliasIndex >= 0, "файл алиасов должен быть в списке");
        Assert.Equal(aliasIndex, vm.ConfTargetIndex);
        Assert.True(string.Equals(vm.ApacheConfPath,
            vm.ConfTargetPaths[aliasIndex],
            System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ViewModel_FallbackFindsAliasesFileNextToServiceConfig_Issue63()
    {
        // Include-граф не содержит файла алиасов (IncludeOptional вне графа / маска каталога
        // не раскрылась), но 1c8Aliases*.conf лежит рядом с конфигом службы. VM обязан найти
        // его fallback-поиском и показать пункт «Include: 1c8Aliases27.conf» (issue #63,
        // «файл алиаса по-прежнему не вернулся»).
        var testData = Path.Combine(FindRepoRoot(), "Workspace1C.Tests", "TestData", "Apache27Conf");
        var mainConf = Path.Combine(testData, "httpd27.conf");
        Assert.True(File.Exists(mainConf), "Фикстура httpd27.conf должна существовать.");

        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = mainConf,
            ServerRoot = Path.GetDirectoryName(mainConf)!,
            Htdocs = Path.Combine(Path.GetDirectoryName(mainConf)!, "htdocs"),
            IncludeFiles = new List<string>(),
            Kind = WebServerKind.Apache24
        };

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);

        Assert.True(vm.HasConfTargets);
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        // Issue #63 (комментарий 7OH 2026-10-26): fallback добавляет РОВНО ОДИН файл
        // алиасов — используемый (по имени из конфига службы), а не все 1c8Aliases*.conf.
        Assert.Single(vm.ConfTargetPaths, p =>
            Path.GetFileName(p).StartsWith("1c8Aliases", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файл алиасов");
        // Issue #63 (комментарий 7OH 2026-10-30): файл алиасов 1c8Aliases* выбирается
        // по умолчанию (основной конфиг остаётся первым пунктом списка).
        Assert.Equal(0, IndexOfMain(vm.ConfTargetPaths));
        Assert.Equal(IndexOfAlias(vm.ConfTargetPaths), vm.ConfTargetIndex);
        Assert.EndsWith("1c8Aliases27.conf", vm.ApacheConfPath, System.StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Issue #63 (комментарий 7OH 2026-10-26): в списке ровно один файл
    // алиасов — используемый; каталог публикации «…\htdocs\27», URL с портом 8027 ----------

    [Fact]
    public void ViewModel_RealFixtures_ConfTargetsShowOnlyUsedAliasesFile_Issue63()
    {
        // Реальные фикстуры TestData/Apache27Conf: главный httpd27.conf → Include
        // conf/httpd.conf → Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf". В списке
        // webinst ровно один файл алиасов — используемый 1c8Aliases27.conf; каталог
        // публикации «…\htdocs\27» (DocumentRoot "${SRVROOT}/htdocs/${OneCPort}"), URL
        // с портом 8027 («Listen "80${OneCPort}"»).
        var testData = Path.Combine(FindRepoRoot(), "Workspace1C.Tests", "TestData", "Apache27Conf");
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance,
            File.ReadAllText(Path.Combine(testData, "httpd27.conf")),
            Path.Combine(ServerRootDir, "conf"),
            path =>
            {
                var fixture = Path.Combine(testData, Path.GetFileName(path));
                return File.Exists(fixture) ? File.ReadAllText(fixture) : null;
            });

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Ровно один файл алиасов — используемый 1c8Aliases27.conf из Include-графа.
        var aliasTargets = vm.ConfTargetPaths
            .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(aliasTargets);
        Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);

        // Каталог публикации из DocumentRoot «${SRVROOT}/htdocs/${OneCPort}» → …\htdocs\27.
        Assert.Equal(Path.Combine(ServerRootDir, "htdocs", "27", "demo_27"),
            vm.PhysicalDirectory, ignoreCase: true);
        // URL с портом из Listen "80${OneCPort}" → 8027.
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);

        // Смена файла webinst (например, на файл алиасов) не «сбрасывает» каталог и порт.
        var aliasIndex = vm.ConfTargetPaths
            .Select((p, i) => (p, i))
            .First(t => t.p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase)).i;
        vm.ConfTargetIndex = aliasIndex;
        Assert.Equal(Path.Combine(ServerRootDir, "htdocs", "27", "demo_27"),
            vm.PhysicalDirectory, ignoreCase: true);
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }

    [Fact]
    public void ViewModel_Fallback_AddsOnlyUsedAliasFile_NotAllNearby_Issue63()
    {
        // Include-граф НЕ содержит файла алиасов (IncludeOptional вне графа / маска
        // каталога не раскрылась), но в каталоге конфига лежат НЕСКОЛЬКО файлов
        // алиасов. Fallback добавляет РОВНО ОДИН — совпадающий с include-строкой
        // конфига службы («1c8Aliases${OneCPort}.conf» → 1c8Aliases27.conf), а не все
        // 1c8Aliases*.conf каталога (issue #63).
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_Fallback_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            var htdocsDir = Path.Combine(tempDir, "htdocs");
            Directory.CreateDirectory(confDir);
            Directory.CreateDirectory(htdocsDir);

            var serverRootFwd = tempDir.Replace('\\', '/');
            var mainPath = Path.Combine(confDir, "httpd27.conf");
            File.WriteAllText(mainPath,
                "Define OneCPort 27\r\n" +
                "Include conf/httpd.conf\r\n");
            File.WriteAllText(Path.Combine(confDir, "httpd.conf"),
                "Define SRVROOT \"" + serverRootFwd + "\"\r\n" +
                "DocumentRoot \"${SRVROOT}/htdocs/${OneCPort}\"\r\n" +
                "Listen \"80${OneCPort}\"\r\n");

            // В каталоге конфига несколько файлов алиасов — конфиг службы использует 27-й.
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases25.conf"), AliasContent(htdocsDir, 25));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"), AliasContent(htdocsDir, 27));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases29.conf"), AliasContent(htdocsDir, 29));

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = mainPath,
                ServerRoot = tempDir,
                Htdocs = htdocsDir,
                Kind = WebServerKind.Apache24
            };
            ApacheInstanceDetector.PopulateIncludes(instance, File.ReadAllText(mainPath), confDir);

            // Значения по умолчанию — только из конфига службы и его include-графа.
            Assert.Equal(Path.Combine(htdocsDir, "27"), instance.DocumentRoot, ignoreCase: true);
            Assert.Equal(new[] { 8027 }, instance.ListenPorts);
            // Алиасы в Include-графе отсутствуют — срабатывает fallback.
            Assert.DoesNotContain(instance.IncludeFiles, p =>
                Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            // В списке webinst РОВНО ОДИН файл алиасов — используемый (1c8Aliases27.conf),
            // а не все 1c8Aliases*.conf каталога.
            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);

            // Каталог публикации «…\htdocs\27» и порт 8027 подставились из конфига службы.
            Assert.Equal(Path.Combine(htdocsDir, "27", "demo_27"), vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);

            // Смена файла webinst на файл алиасов не теряет каталог/порт (issue #63).
            var aliasIndex = vm.ConfTargetPaths
                .Select((p, i) => (p, i))
                .First(t => t.p.EndsWith("1c8Aliases27.conf", StringComparison.OrdinalIgnoreCase)).i;
            vm.ConfTargetIndex = aliasIndex;
            Assert.Equal(Path.Combine(htdocsDir, "27", "demo_27"), vm.PhysicalDirectory, ignoreCase: true);
            Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    // ---------- Issue #63 (последний комментарий 7OH 2026-10-28 «файла в списке нет»):
    // fallback-ветка — инстансы не найдены, конфиг вводится/выбирается вручную ----------

    [Fact]
    public void ViewModel_FallbackBranch_ManualConfInput_BuildsConfTargetsAndUrlWithPort_Issue63()
    {
        // Сценарий (а) из разбора issue: на машине пользователя детектор инстансов
        // ничего не вернул (служба httpd нестандартно называется/реестр недоступен) —
        // в окне лишь fallback-пункт «Apache 2.4», ConfTargets при этом РАНЬШЕ был
        // пуст («файла в списке нет»). После фикса fallback-ветка строит список из
        // введённого вручную конфига: ровно один файл алиасов 1c8Aliases27.conf
        // (из реальных фикстур 27_conf.zip), каталог «…\htdocs\27», URL с портом 8027.
        var testData = Path.Combine(FindRepoRoot(), "Workspace1C.Tests", "TestData", "Apache27Conf");
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_FallbackManual_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            // Реальная раскладка пользователя: httpd27.conf → Include conf/httpd.conf →
            // Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf". SRVROOT переписываем
            // на тестовый каталог, чтобы include-цели существовали «как на машине».
            var srvRoot = tempDir.Replace('\\', '/');
            File.WriteAllText(Path.Combine(confDir, "httpd27.conf"),
                File.ReadAllText(Path.Combine(testData, "httpd27.conf")));
            File.WriteAllText(Path.Combine(confDir, "httpd.conf"),
                File.ReadAllText(Path.Combine(testData, "httpd.conf")).Replace("c:/Apache/Apache24", srvRoot));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"),
                File.ReadAllText(Path.Combine(testData, "1c8Aliases27.conf")).Replace("c:/Apache/Apache24", srvRoot));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            // Инстансы детектора НЕ передаём — воспроизводим fallback-ветку issue #63.
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, Array.Empty<ApacheInstance>());

            // Выбираем fallback-пункт «Apache 2.4» (не IIS, не обнаруженный инстанс).
            var fallbackIndex = -1;
            for (var i = 0; i < vm.ServerOptions.Count; i++)
            {
                vm.ServerIndex = i;
                if (vm.IsApache && !vm.IsInstanceSelected)
                {
                    fallbackIndex = i;
                    break;
                }
            }
            Assert.True(fallbackIndex >= 0, "Fallback-пункт «Apache по умолчанию» не найден в ServerOptions");
            vm.ServerIndex = fallbackIndex;
            vm.VirtualDirectory = "demo_27";

            // Ручная правка поля «Конфигурация»: пользователь указывает свой конфиг.
            vm.ApacheConfPath = Path.Combine(confDir, "httpd27.conf");

            Assert.True(vm.HasConfTargets,
                "ConfTargets должен строиться из вручную введённого конфига (fallback-ветка, issue #63)");
            var aliasTargets = vm.ConfTargetPaths
                .Where(p => Path.GetFileName(p).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(aliasTargets);
            Assert.EndsWith("1c8Aliases27.conf", aliasTargets[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains(vm.ConfTargetPaths,
                p => p.EndsWith("httpd27.conf", StringComparison.OrdinalIgnoreCase));

            // Значения по умолчанию из введённого конфига: DocumentRoot + порт Listen.
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

    [Fact]
    public void ViewModel_FallbackBranch_MissingConf_EmptyTargets_NoCrash_Issue63()
    {
        // Некорректный ручной путь не роняет окно: ConfTargets пуст, ApacheConfPath
        // сохраняет введённое значение (валидация webinst покажет ошибку при публикации).
        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, Array.Empty<ApacheInstance>());

        var fallbackIndex = -1;
        for (var i = 0; i < vm.ServerOptions.Count; i++)
        {
            vm.ServerIndex = i;
            if (vm.IsApache && !vm.IsInstanceSelected)
            {
                fallbackIndex = i;
                break;
            }
        }
        Assert.True(fallbackIndex >= 0);
        vm.ServerIndex = fallbackIndex;
        vm.ApacheConfPath = Path.Combine(Path.GetTempPath(), "no_such_dir_1c_w1c", "httpd-missing.conf");

        Assert.False(vm.HasConfTargets);
        Assert.Equal(-1, vm.ConfTargetIndex);
        Assert.EndsWith("httpd-missing.conf", vm.ApacheConfPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewModel_FallbackBranch_FirstEntryIsMainConf_Issue63()
    {
        // Основной конфиг службы — первый пункт ConfTargets, даже в fallback-ветке:
        // значения webinst по умолчанию берутся из него (webinst -confpath).
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_FallbackFirst_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var mainPath = Path.Combine(tempDir, "httpd27.conf");
            File.WriteAllText(mainPath,
                "Define SRVROOT \"" + tempDir.Replace("\\", "/") + "\"\r\n" +
                "Define OneCPort 27\r\n" +
                "Include \"${SRVROOT}/conf/1c8Aliases27.conf\"\r\n");
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"), "# 1c\nAlias \"/Demo\" \"/tmp/Demo/\"\n");

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, Array.Empty<ApacheInstance>());
            var fallbackIndex = -1;
            for (var i = 0; i < vm.ServerOptions.Count; i++)
            {
                vm.ServerIndex = i;
                if (vm.IsApache && !vm.IsInstanceSelected)
                {
                    fallbackIndex = i;
                    break;
                }
            }
            vm.ServerIndex = fallbackIndex;
            vm.ApacheConfPath = mainPath;

            Assert.True(vm.HasConfTargets);
            Assert.EndsWith("httpd27.conf", vm.ConfTargetPaths[0], StringComparison.OrdinalIgnoreCase);
            // Issue #63 (комментарий 7OH 2026-10-30): файл алиасов выбирается по умолчанию.
            Assert.Equal(1, vm.ConfTargetIndex);
            Assert.EndsWith("1c8Aliases27.conf", vm.ApacheConfPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    [Fact]
    public void ViewModel_FallbackFindsAliasFromIncludePatternWhenVarsUnresolved_Issue63()
    {
        // Include «${SRVROOT}/conf/1c8Aliases${OneCPort}.conf» с НЕРАСКРЫТОЙ переменной
        // (Define нигде нет): в Include-графе файла нет, но детектор запомнил шаблон
        // (AliasTargetPatterns), а VM-фаллбэк подставляет номер инстанса («27» из имени
        // конфига службы httpd27.conf) и находит ровно ОДИН файл алиасов — 1c8Aliases27.conf
        // (issue #63, 7OH: «Его можно же найти в строках с include?»).
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Issue63_Pattern_" + Guid.NewGuid().ToString("N"));
        try
        {
            var confDir = Path.Combine(tempDir, "conf");
            Directory.CreateDirectory(confDir);
            var mainPath = Path.Combine(confDir, "httpd27.conf");
            File.WriteAllText(mainPath,
                "Include \"${SRVROOT}/conf/1c8Aliases${OneCPort}.conf\"\r\n");
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases25.conf"), AliasContent(tempDir, 25));
            File.WriteAllText(Path.Combine(confDir, "1c8Aliases27.conf"), AliasContent(tempDir, 27));

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = mainPath,
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24
            };
            // Определений переменных нет — Include-цель не раскрылась, файл алиасов в
            // Include-граф не попал; шаблон включения сохранён детектором.
            ApacheInstanceDetector.PopulateIncludes(instance, File.ReadAllText(mainPath), confDir);
            Assert.Empty(instance.IncludeFiles);
            Assert.NotEmpty(instance.AliasTargetPatterns);

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";

            // Fallback по include-шаблону: РОВНО ОДИН файл алиасов — используемый 27-й.
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

    private static string AliasContent(string htdocs, int n)
        => "# 1c publication\nAlias \"/Demo\" \"" + htdocs.Replace('\\', '/') + "/" + n + "/Demo/\"\n";

    /// <summary>Индекс файла алиасов 1c8Aliases* в списке файлов webinst (-1 — нет).</summary>
    private static int IndexOfAlias(IReadOnlyList<string> paths)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            if (Path.GetFileName(paths[i]).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Индекс основного конфига httpd27.conf в списке файлов webinst (-1 — нет).</summary>
    private static int IndexOfMain(IReadOnlyList<string> paths)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            if (paths[i].EndsWith("httpd27.conf", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }
}