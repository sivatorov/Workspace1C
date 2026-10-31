using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (волна 2): пост-обработка конфига Apache после успешной публикации
/// webinst — (1) дедупликация строк LoadModule _1cws_module, (2) подстановка путей
/// нового блока алиаса через ${…}-переменные «как тут заведено», (3) .bak-копия перед
/// записью, (4) файл алиасов 1c8Aliases*.conf выбирается по умолчанию в списке файлов
/// webinst. Фикстуры — фрагменты реальных конфигов 7OH из _27conf_extracted
/// (1c8Aliases27.conf, httpd27.conf), скопированные в тест как текст (тесты не ссылаются
/// на временную папку напрямую).
/// </summary>
public sealed class WebPublishConfPostProcessTests
{
    // ------------------- Фикстуры (фрагменты _27conf_extracted) -------------------

    /// <summary>Фрагмент реального файла алиасов 1С (1c8Aliases27.conf) + Define'ы из httpd27.conf.</summary>
    private const string Aliases27ConfFragment = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 27
        define myDocumentRoot "${SRVROOT}/htdocs/${OneCPort}"

        # 1c publication
        Alias "/erp" "${myDocumentRoot}/erp/"
        <Directory "${myDocumentRoot}/erp/">
            AllowOverride All
            Options None
            Require all granted
            SetHandler 1c-application
            ManagedApplicationDescriptor "${myDocumentRoot}/erp/default.vrd"
        </Directory>

        # 1c publication
        Alias "/Demo" "${myDocumentRoot}/Demo/"
        <Directory "${myDocumentRoot}/Demo/">
            AllowOverride All
            Options None
            Require all granted
            SetHandler 1c-application
            ManagedApplicationDescriptor "${myDocumentRoot}/Demo/default.vrd"
        </Directory>
        """;

    /// <summary>То же, но существующие записи — с ПОЛНЫМИ путями (переменная не «заведена»).</summary>
    private const string Aliases27ConfFullPaths = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 27
        define myDocumentRoot "${SRVROOT}/htdocs/${OneCPort}"

        # 1c publication
        Alias "/erp" "c:/Apache/Apache24/htdocs/27/erp/"
        <Directory "c:/Apache/Apache24/htdocs/27/erp/">
            AllowOverride All
            Options None
            Require all granted
            SetHandler 1c-application
            ManagedApplicationDescriptor "c:/Apache/Apache24/htdocs/27/erp/default.vrd"
        </Directory>
        """;

    /// <summary>Новый блок, который дописывает webinst (полные пути) + дубль LoadModule.</summary>
    private const string NewWebinstBlock = """

        LoadModule _1cws_module "C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll"
        # 1c publication
        Alias "/demo_27" "c:/Apache/Apache24/htdocs/27/demo_27/"
        <Directory "c:/Apache/Apache24/htdocs/27/demo_27/">
            AllowOverride All
            Options None
            Require all granted
            SetHandler 1c-application
            ManagedApplicationDescriptor "c:/Apache/Apache24/htdocs/27/demo_27/default.vrd"
        </Directory>
        """;

    private static string AppendNewBlock(string conf) => conf + NewWebinstBlock;

    // ----------------------- Дедупликация LoadModule -----------------------

    [Fact]
    public void DeduplicateLoadModule_RemovesDuplicates_KeepsFirst()
    {
        const string conf = """
            LoadModule _1cws_module "C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll"
            Alias "/demo_27" "c:/Apache/Apache24/htdocs/27/demo_27/"
            LoadModule _1cws_module "C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll"
            """;

        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(conf);

        Assert.Equal(1, removed);
        Assert.Equal(1, CountOccurrences(text, "LoadModule _1cws_module"));
        Assert.Contains("Alias \"/demo_27\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DeduplicateLoadModule_NoDuplicates_TextUnchanged()
    {
        const string conf = "LoadModule _1cws_module \"C:/x/wsap24.dll\"\r\nListen 80\r\n";
        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(conf);

        Assert.Equal(0, removed);
        Assert.Equal(conf, text);
    }

    [Fact]
    public void DeduplicateLoadModule_OtherLoadModules_AreNotTouched()
    {
        // Дедуплицируется ТОЛЬКО _1cws_module — прочие модули не трогаем.
        const string conf = """
            LoadModule ssl_module modules/mod_ssl.so
            LoadModule _1cws_module "C:/x/wsap24.dll"
            LoadModule ssl_module modules/mod_ssl.so
            LoadModule _1cws_module "C:/x/wsap24.dll"
            """;

        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(conf);

        Assert.Equal(1, removed);
        Assert.Equal(2, CountOccurrences(text, "LoadModule ssl_module"));
        Assert.Equal(1, CountOccurrences(text, "LoadModule _1cws_module"));
    }

    [Fact]
    public void DeduplicateLoadModule_CaseInsensitiveDirective_LineEndingsPreserved()
    {
        const string conf = "loadmodule _1cws_module \"C:/x/wsap24.dll\"\r\n" +
                            "LoadModule _1cws_module \"C:/x/wsap24.dll\"\r\n";

        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(conf);

        Assert.Equal(1, removed);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(text, "_1cws_module"));
    }

    [Fact]
    public void DeduplicateLoadModule_EmptyText_ReturnsUnchanged()
    {
        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(string.Empty);
        Assert.Equal(string.Empty, text);
        Assert.Equal(0, removed);
    }

    // ----------------------- Чистка LoadModule по include-цепочке (issue #63, 1.0.1.11) -----------------------

    [Fact]
    public void RemoveLoadModuleIfPresentInChain_ModuleInMainConf_RemovedFromAliasFile()
    {
        // Сценарий 7OH: «LoadModule _1cws_module» уже в httpd.conf (другой файл
        // цепочки), webinst дописал его в файл алиасов — подчистить полностью.
        const string httpdConf = """
            Define SRVROOT "c:/Apache/Apache24"
            LoadModule _1cws_module "C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll"
            Include "${SRVROOT}/conf/1c8Aliases27.conf"
            """;
        const string aliasConf = """
            Alias "/demo_27" "c:/Apache/Apache24/htdocs/27/demo_27/"
            <Directory "c:/Apache/Apache24/htdocs/27/demo_27/">
                SetHandler 1c-application
            </Directory>
            LoadModule _1cws_module "C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll"
            """;

        var (text, removed) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            aliasConf, new[] { httpdConf });

        Assert.Equal(1, removed);
        Assert.Equal(0, CountOccurrences(text, "LoadModule _1cws_module"));
        Assert.Contains("Alias \"/demo_27\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveLoadModuleIfPresentInChain_ModuleNotInChain_TextUnchanged()
    {
        const string aliasConf = """
            Alias "/demo_27" "c:/Apache/Apache24/htdocs/27/demo_27/"
            LoadModule _1cws_module "C:/x/wsap24.dll"
            """;

        // Модуля в цепочке нет (файл алиасов — единственное подключение): не трогаем.
        var (text, removed) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            aliasConf, new[] { "ServerRoot \"c:/Apache\"\nListen 80\n" });

        Assert.Equal(0, removed);
        Assert.Equal(aliasConf, text);
    }

    [Fact]
    public void RemoveLoadModuleIfPresentInChain_EmptyChainOrText_NoChanges()
    {
        const string aliasConf = "LoadModule _1cws_module \"C:/x/wsap24.dll\"\n";
        var (text, removed) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            aliasConf, Array.Empty<string?>());

        Assert.Equal(0, removed);
        Assert.Equal(aliasConf, text);

        var (text2, removed2) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            string.Empty, new[] { aliasConf });
        Assert.Equal(string.Empty, text2);
        Assert.Equal(0, removed2);
    }

    // ----------------------- Подстановка ${…}-переменных -----------------------

    [Fact]
    public void RewriteAliasBlock_VariableUsedInExistingEntries_PathsRewritten()
    {
        // Реальный кейс 7OH: существующие записи — через ${myDocumentRoot}, новый блок
        // webinst — с полными путями; пути нового блока переписываются через переменную.
        var conf = AppendNewBlock(Aliases27ConfFragment);

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo_27", null);

        Assert.True(changed, "пути нового блока должны быть переписаны через ${myDocumentRoot}");
        Assert.Contains("Alias \"/demo_27\" \"${myDocumentRoot}/demo_27/\"", text, StringComparison.Ordinal);
        Assert.Contains("<Directory \"${myDocumentRoot}/demo_27/\">", text, StringComparison.Ordinal);
        Assert.Contains("ManagedApplicationDescriptor \"${myDocumentRoot}/demo_27/default.vrd\"", text, StringComparison.Ordinal);
        // Полные пути в новом блоке больше не встречаются.
        Assert.DoesNotContain("c:/Apache/Apache24/htdocs/27/demo_27", text, StringComparison.OrdinalIgnoreCase);
        // Существующие записи не изменяются (они уже с переменной).
        Assert.Contains("Alias \"/erp\" \"${myDocumentRoot}/erp/\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteAliasBlock_NoVariableUsageInExistingEntries_Unchanged()
    {
        // «Если выше было тоже с переменной» НЕ выполнено: записи с полными путями —
        // конфиг не трогаем (защита от поломки Apache).
        var conf = AppendNewBlock(Aliases27ConfFullPaths);

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo_27", null);

        Assert.False(changed);
        Assert.Equal(AppendNewBlock(Aliases27ConfFullPaths), text);
    }

    [Fact]
    public void RewriteAliasBlock_AmbiguousPrefixVariable_Unchanged()
    {
        // ДВЕ переменные, используемые в существующих записях публикации, совпадают
        // с префиксом пути нового блока — подстановка неоднозначна, не делаем.
        const string conf = """
            Define SRVROOT "c:/Apache/Apache24"
            Define OneCPort 27
            Define myDocumentRoot "${SRVROOT}/htdocs/${OneCPort}"
            Define mySiteRoot "${SRVROOT}/htdocs/27"

            Alias "/erp" "${myDocumentRoot}/erp/"
            <Directory "${myDocumentRoot}/erp/">
                Require all granted
            </Directory>

            Alias "/site" "${mySiteRoot}/site/"
            <Directory "${mySiteRoot}/site/">
                Require all granted
            </Directory>

            Alias "/demo_27" "c:/Apache/Apache24/htdocs/27/demo_27/"
            <Directory "c:/Apache/Apache24/htdocs/27/demo_27/">
                Require all granted
            </Directory>
            """;

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo_27", null);

        Assert.False(changed, "две подходящие переменные — подстановка неоднозначна");
        Assert.Contains("c:/Apache/Apache24/htdocs/27/demo_27", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RewriteAliasBlock_UnusedRootVariable_DoesNotBlockSubstitution()
    {
        // SRVROOT префиксует любой путь, но в записях публикации не используется —
        // он НЕ делает подстановку «неоднозначной».
        const string conf = """
            Define SRVROOT "c:/Apache/Apache24"
            Define myDocumentRoot "${SRVROOT}/htdocs/27"

            Alias "/erp" "${myDocumentRoot}/erp/"
            <Directory "${myDocumentRoot}/erp/">
                Require all granted
            </Directory>

            Alias "/demo" "c:/Apache/Apache24/htdocs/27/demo/"
            <Directory "c:/Apache/Apache24/htdocs/27/demo/">
                Require all granted
            </Directory>
            """;

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo", null);

        Assert.True(changed);
        Assert.Contains("Alias \"/demo\" \"${myDocumentRoot}/demo/\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteAliasBlock_PrefixIsNotDirectoryBoundary_Unchanged()
    {
        // «htdocs/27» не должен совпасть как префикс пути «htdocs/270/base».
        const string conf = """
            Define SRVROOT "c:/Apache"
            Define myDocumentRoot "${SRVROOT}/htdocs/27"

            Alias "/erp" "${myDocumentRoot}/erp/"
            <Directory "${myDocumentRoot}/erp/">
                Require all granted
            </Directory>

            Alias "/demo" "c:/Apache/htdocs/270/demo/"
            <Directory "c:/Apache/htdocs/270/demo/">
                Require all granted
            </Directory>
            """;

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo", null);

        Assert.False(changed);
    }

    [Fact]
    public void RewriteAliasBlock_NoDefines_Unchanged()
    {
        var conf = AppendNewBlock("Alias \"/demo_27\" \"c:/Apache/Apache24/htdocs/27/demo_27/\"\r\n");

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo_27", null);

        Assert.False(changed);
        Assert.Equal(conf, text);
    }

    [Fact]
    public void RewriteAliasBlock_NoAliasForWsdir_Unchanged()
    {
        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            Aliases27ConfFragment, "absent_base", null);

        Assert.False(changed);
    }

    [Fact]
    public void RewriteAliasBlock_DefinesPassedExternally_StillWorks()
    {
        // Define'ы могут приходить из include-графа (EffectiveInstance.Defines).
        var conf = AppendNewBlock(Aliases27ConfFragment.Replace(
            "define myDocumentRoot \"${SRVROOT}/htdocs/${OneCPort}\"", string.Empty));

        var defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SRVROOT"] = "c:/Apache/Apache24",
            ["OneCPort"] = "27",
            ["myDocumentRoot"] = "${SRVROOT}/htdocs/${OneCPort}"
        };

        var (text, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            conf, "demo_27", defines);

        Assert.True(changed);
        Assert.Contains("Alias \"/demo_27\" \"${myDocumentRoot}/demo_27/\"", text, StringComparison.Ordinal);
    }

    // ----------------------- Полный сценарий (dedup + rewrite) -----------------------

    [Fact]
    public void FullPostProcess_DedupAndRewrite_OnRealConfFixture()
    {
        // Полный сценарий публикации в файл алиасов: дубль LoadModule + блок с полными путями.
        var conf = Aliases27ConfFragment + "\n" +
                   "LoadModule _1cws_module \"C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll\"\n" +
                   "# 1c publication\n" +
                   "Alias \"/demo_27\" \"c:/Apache/Apache24/htdocs/27/demo_27/\"\n" +
                   "<Directory \"c:/Apache/Apache24/htdocs/27/demo_27/\">\n" +
                   "    AllowOverride All\n" +
                   "    Options None\n" +
                   "    Require all granted\n" +
                   "    SetHandler 1c-application\n" +
                   "    ManagedApplicationDescriptor \"c:/Apache/Apache24/htdocs/27/demo_27/default.vrd\"\n" +
                   "</Directory>\n";

        var (deduped, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(conf);
        Assert.Equal(0, removed); // в файле алиасов LoadModule мог не быть вовсе — дубля нет.

        var (rewritten, changed) = WebPublishConfPostProcessor.RewriteAliasBlockWithVariables(
            deduped, "demo_27", null);
        Assert.True(changed);
        Assert.Contains("Alias \"/demo_27\" \"${myDocumentRoot}/demo_27/\"", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPostProcess_DuplicateLoadModuleAcrossMainConfAndAliasFile()
    {
        // Кейс 7OH: webinst дописал LoadModule в 1c8Aliases27.conf, хотя она уже есть
        // в основном httpd.conf — дедупликация на объединённом тексте.
        var merged = "LoadModule _1cws_module \"C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll\"\r\n" +
                     Aliases27ConfFragment.Replace("\n", "\r\n") + "\r\n" +
                     "LoadModule _1cws_module \"C:/Program Files/1cv8/8.3.27.2342/bin/wsap24.dll\"\r\n";

        var (text, removed) = WebPublishConfPostProcessor.DeduplicateLoadModule(merged);

        Assert.Equal(1, removed);
        Assert.Equal(1, CountOccurrences(text, "LoadModule _1cws_module"));
    }

    // ----------------------- Выбор 1c8Aliases* по умолчанию (VM) -----------------------

    private static Infobase Infobase() => new() { Id = "test-base-id", Name = "Бухгалтерия" };

    [Fact]
    public void WebPublishViewModel_AliasFileInGraph_IsSelectedByDefault()
    {
        // issue #63 (комментарий 7OH 2026-10-30): при наличии 1c8Aliases* — он в списке
        // файлов webinst по умолчанию (а не основной conf).
        var instance = new ApacheInstance
        {
            Id = "Apache2.4",
            Name = "Apache2.4_27",
            ServerRoot = @"C:\Apache\Apache24",
            ConfPath = @"C:\Apache\Apache24\conf\httpd27.conf",
            Htdocs = @"C:\Apache\Apache24\htdocs\27",
            Kind = WebServerKind.Apache24,
            IncludeFiles = new[] { @"C:\Apache\Apache24\conf\1c8Aliases27.conf" }
        };
        var vm = new WebPublishViewModel(new[] { Infobase() }, Infobase(), new[] { instance });

        vm.ServerIndex = vm.ServerIndexFor(instance);

        Assert.True(vm.HasConfTargets);
        Assert.Equal(2, vm.ConfTargets.Count);
        Assert.Equal(1, vm.ConfTargetIndex); // не 0: выбран именно файл алиасов.
        Assert.Equal(@"C:\Apache\Apache24\conf\1c8Aliases27.conf", vm.ApacheConfPath);
        Assert.Equal(@"C:\Apache\Apache24\conf\1c8Aliases27.conf", vm.BuildRequest().ApacheConfPath);
    }

    [Fact]
    public void WebPublishViewModel_NoAliasFile_MainConfSelectedByDefault()
    {
        var instance = new ApacheInstance
        {
            Id = "Apache2.4",
            Name = "Apache2.4",
            ServerRoot = @"C:\Apache24",
            ConfPath = @"C:\Apache24\conf\httpd.conf",
            Htdocs = @"C:\Apache24\htdocs",
            Kind = WebServerKind.Apache24,
            IncludeFiles = new[] { @"C:\Apache24\conf\extra\httpd-vhosts.conf" }
        };
        var vm = new WebPublishViewModel(new[] { Infobase() }, Infobase(), new[] { instance });

        vm.ServerIndex = vm.ServerIndexFor(instance);

        Assert.Equal(0, vm.ConfTargetIndex);
        Assert.Equal(@"C:\Apache24\conf\httpd.conf", vm.ApacheConfPath);
    }

    // ----------------------- Пост-обработка файла (VM, с .bak) -----------------------

    [Fact]
    public void PostProcessApacheConfAfterPublish_CreatesBak_RewritesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ws1c_postprocess_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var confPath = Path.Combine(tempDir, "1c8Aliases27.conf");
            File.WriteAllText(confPath, AppendNewBlock(Aliases27ConfFragment) + "\n");

            // Цепочка include намеренно ПУСТАЯ (основной конфиг не существует):
            // тест не должен зависеть от машины — на машине с реальным Apache
            // fallback-ветка подключила бы C:\Apache24\conf\httpd.conf, и если в нём
            // есть LoadModule _1cws_module, новая чистка по цепочке (issue #63,
            // 1.0.1.11) удалила бы строку и из файла алиасов.
            var instance = new ApacheInstance
            {
                Id = "Apache2.4",
                Name = "Apache2.4",
                ServerRoot = tempDir,
                ConfPath = Path.Combine(tempDir, "absent-httpd.conf"),
                Kind = WebServerKind.Apache24
            };
            var vm = new WebPublishViewModel(new[] { Infobase() }, Infobase(), new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);
            vm.VirtualDirectory = "demo_27";
            vm.PostProcessApacheConfAfterPublish(new WebPublishRequest
            {
                Server = WebServerKind.Apache24,
                VirtualDirectory = "demo_27",
                PhysicalDirectory = @"c:\Apache\Apache24\htdocs\27\demo_27",
                ApacheConfPath = confPath,
                ConnectionString = "Srvr=\"localhost\";Ref=\"demo\";"
            });

            // .bak-копия исходного файла создана с исходным содержимым.
            var bakPath = confPath + ".bak";
            Assert.True(File.Exists(bakPath), "должна быть создана .bak-копия конфига");
            Assert.Contains("c:/Apache/Apache24/htdocs/27/demo_27",
                File.ReadAllText(bakPath), StringComparison.OrdinalIgnoreCase);

            // Основной файл переписан: пути — через переменную, дублей LoadModule нет.
            var rewritten = File.ReadAllText(confPath);
            Assert.Contains("Alias \"/demo_27\" \"${myDocumentRoot}/demo_27/\"", rewritten, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(rewritten, "LoadModule _1cws_module"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PostProcessApacheConfAfterPublish_MissingFile_DoesNotThrow()
    {
        var vm = new WebPublishViewModel(new[] { Infobase() }, Infobase());
        vm.ServerIndex = 0;
        vm.PostProcessApacheConfAfterPublish(new WebPublishRequest
        {
            Server = WebServerKind.Apache24,
            VirtualDirectory = "demo",
            ApacheConfPath = Path.Combine(Path.GetTempPath(), "ws1c_absent_conf_" + Guid.NewGuid().ToString("N") + ".conf")
        });
        // Исключение не брошено — пост-обработка «не отменяет» публикацию.
    }

    [Fact]
    public void PostProcessApacheConfAfterPublish_Iis_IsSkipped()
    {
        var vm = new WebPublishViewModel(new[] { Infobase() }, Infobase());
        vm.PostProcessApacheConfAfterPublish(new WebPublishRequest
        {
            Server = WebServerKind.Iis,
            VirtualDirectory = "demo",
            ApacheConfPath = @"C:\nonexistent\httpd.conf"
        });
        // Для IIS пост-обработка не выполняется вовсе.
    }

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += marker.Length;
        }
        return count;
    }
}
