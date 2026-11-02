using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Релиз 1.0.1.25 (issues #79, #83, #92, #98, #101):
/// <list type="bullet">
/// <item>#79/#92 — rac-резолв данных СУБД: учётные данные администратора ИБ из свойств
/// базы, выбор кластера по порту менеджера из строки подключения, тип СУБД «по
/// умолчанию» при пустом dbms rac 8.5;</item>
/// <item>#83 — разбор реального вывода «session list --licenses» (license-type,
/// max-users, series, short-presentation) и раскладка вкладки «Лицензии»;</item>
/// <item>#92 — поиск по данным баз, снятие галочки при успехе, выгрузка запущенных баз;</item>
/// <item>#98 — «Отмена/Закрыть» последней кнопкой во всех WPF-окнах;</item>
/// <item>#101 — Вариант 4: красный чип при зависании, крупный значок «Предприятие».</item>
/// </list>
/// </summary>
public sealed class Release100125Tests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static RacCluster Cluster(int port) => new() { Id = Guid.NewGuid(), Port = port, Host = "localhost" };

    // ===================== #92/#79: выбор кластера по порту =====================

    [Theory]
    [InlineData("localhost", 1541, 1541)]
    [InlineData("localhost:27541", 1541, 27541)]
    [InlineData("localhost", 27541, 27541)]
    [InlineData("srv1:3541,srv2", 1541, 3541)]
    public void ResolveManagerPort_FromServerOrPortField(string server, int port, int expected)
    {
        var connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = server, Port = port };
        Assert.Equal(expected, ClusterInfobaseCredentialsResolver.ResolveManagerPort(connection));
    }

    [Fact]
    public void OrderClustersByManagerPort_KeepsOnlyMatchingCluster()
    {
        var p27 = new RacConnectionParams { Address = "localhost", Port = 27545 };
        var p51 = new RacConnectionParams { Address = "localhost", Port = 1545 };
        var targets = new List<(RacConnectionParams, RacCluster)>
        {
            (p27, Cluster(27541)),
            (p51, Cluster(1541))
        };

        var ordered = ClusterInfobaseCredentialsResolver.OrderClustersByManagerPort(targets, 1541);

        var single = Assert.Single(ordered);
        Assert.Equal(1545, single.Parameters.Port);
    }

    [Fact]
    public void OrderClustersByManagerPort_NoMatch_KeepsAll()
    {
        var p = new RacConnectionParams { Address = "localhost", Port = 1545 };
        var targets = new List<(RacConnectionParams, RacCluster)> { (p, Cluster(2541)), (p, Cluster(3541)) };
        Assert.Equal(2, ClusterInfobaseCredentialsResolver.OrderClustersByManagerPort(targets, 1541).Count);
    }

    [Fact]
    public void BuildConnectionCandidates_ReturnsAllServersOfSameHost()
    {
        var servers = new List<ManagementServer>
        {
            new() { Address = "localhost", Port = 27545 },
            new() { Address = "localhost", Port = 1545 },
            new() { Address = "otherhost", Port = 1545 },
            new() { Address = "localhost", Port = 1545 } // дубль — не повторяется
        };

        var candidates = ClusterInfobaseCredentialsResolver.BuildConnectionCandidates("localhost", servers);

        Assert.Equal(new[] { 27545, 1545 }, candidates.Select(c => c.Port).ToArray());
    }

    [Fact]
    public void BuildConnectionCandidates_NoServers_DefaultAgentPort()
    {
        var candidate = Assert.Single(ClusterInfobaseCredentialsResolver.BuildConnectionCandidates("srv:1541", null));
        Assert.Equal("srv", candidate.Address);
        Assert.Equal(IRacClient.DefaultPort, candidate.Port);
    }

    // ===================== #79: тип СУБД «по умолчанию» и учётные данные =====================

    [Fact]
    public void ToProfileWithFallbackDbms_UsesFormDbmsWhenRacTypeEmpty()
    {
        var summary = new RacInfobaseSummary { Name = "Demo_8.5.1", Dbms = "", DbServer = "ALF", DbName = "Demo_8.5.1", DbUser = "sa" };

        var profile = ClusterInfobaseCredentialsResolver.ToProfileWithFallbackDbms(summary, null, "MSSQLServer", "id1", "Demo_8.5.1");

        Assert.NotNull(profile);
        Assert.Equal(DbmsKind.MsSql, profile!.Dbms);
        Assert.Equal("Demo_8.5.1", profile.Database);
        Assert.Equal("sa", profile.User);
        Assert.Null(ClusterInfobaseCredentialsResolver.ToProfileWithFallbackDbms(summary, null, null, "id1", "x"));
        Assert.Null(ClusterInfobaseCredentialsResolver.ToProfileWithFallbackDbms(
            new RacInfobaseSummary { DbServer = "" }, null, "MSSQLServer", "id1", "x"));
    }

    [Fact]
    public void InfobaseAdminCredentials_ExplicitCredentialWins()
    {
        var ib = new Infobase { Name = "Demo 27" };
        ib.Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "localhost", DatabaseName = "Demo_8.3.27" };

        var (user, password) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentials(ib,
            new BackupCredential { UseInfobaseAuth = false, User = "Админ", Password = "1" });

        Assert.Equal("Админ", user);
        Assert.Equal("1", password);
    }

    [Fact]
    public void InfobaseAdminCredentials_FromConfiguratorAuth()
    {
        var ib = new Infobase { Name = "Demo 27" };
        ib.Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "localhost", DatabaseName = "Demo_8.3.27" };
        ib.ConfiguratorAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "Администратор",
            Password = "pwd"
        };

        var (user, password) = ClusterInfobaseCredentialsResolver.InfobaseAdminCredentials(ib);

        Assert.Equal("Администратор", user);
        Assert.Equal("pwd", password);
    }

    [Fact]
    public void InfobaseInfoCalls_PassInfobaseCredentials()
    {
        var resolver = ReadProjectFile("Workspace1C/Services/ClusterInfobaseCredentialsResolver.cs");
        Assert.Contains("infobaseUser: ibUser, infobasePassword: ibPassword", resolver);
        var preparation = ReadProjectFile("Workspace1C/Services/BatchOpsPreparationService.cs");
        Assert.DoesNotContain("infobaseUser: null, infobasePassword: null", preparation);
    }

    // ===================== #83: лицензии =====================

    [Fact]
    public void ToSessions_ParsesRealRacLicenseBlocks()
    {
        const string output =
            "session            : 4444aaaa-0000-0000-0000-000000000001\n" +
            "user-name          : Администратор\n" +
            "host               : PC-DEV\n" +
            "app-id             : 1CV8C\n" +
            "full-name          : \"file://C:/ProgramData/1C/licenses/8100000001.lic\"\n" +
            "series             : \"8100000001\"\n" +
            "issued-by-server   : no\n" +
            "license-type       : soft\n" +
            "net                : no\n" +
            "max-users          : 1\n" +
            "max-cur-users      : 1\n" +
            "rmngr-address      : \"\"\n" +
            "rmngr-port         : 0\n" +
            "rmngr-pid          : 0\n" +
            "short-presentation : \"Клиент 8100000001 1 польз.\"\n" +
            "full-presentation  : \"Клиент, 8100000001, 1 пользователей\"\n";

        var session = Assert.Single(RacOutputParser.ToSessions(output));

        Assert.Equal("soft", session.LicType);
        Assert.Equal(1, session.MaxLicenses);
        Assert.Equal("8100000001", session.LicenseSeries);
        Assert.Equal("Клиент 8100000001 1 польз.", session.LicensePresentation);
        Assert.False(session.LicenseIssuedByServer);

        var row = new RacLicensedSessionRow(session);
        Assert.Contains("soft", row.LicenseDescription);
        Assert.Contains("8100000001", row.LicenseDescription);
    }

    [Fact]
    public void LicensesTab_Wpf_UsesGridRows_NotDockPanel()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/ServerMonitorWindow.xaml");
        var tab = xaml.IndexOf("<TabItem Header=\"{Binding LicensesTabText}\">", StringComparison.Ordinal);
        Assert.True(tab > 0);
        var installed = xaml.IndexOf("x:Name=\"InstalledLicensesGrid\"", tab, StringComparison.Ordinal);
        var segment = xaml[tab..installed];
        Assert.DoesNotContain("<DockPanel>", segment);
        Assert.Contains("Grid.Row=\"4\"", xaml.Substring(installed, 80));
        Assert.Contains("LicenseDescription", segment);
    }

    // ===================== #92: массовые операции =====================

    private static Infobase Base(string name, string group = "", string server = "", string db = "")
    {
        var ib = new Infobase { Name = name, Group = group };
        ib.Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = server, DatabaseName = db };
        return ib;
    }

    [Fact]
    public void SearchText_MatchesBaseData_NotOnlyName()
    {
        var vm = new BatchOperationsViewModel(new[]
        {
            Base("Бухгалтерия", "ДэмоБазы", "srv-prod", "acc_main"),
            Base("Зарплата", "Кадры", "srv-test", "zup")
        }, BatchOperationKind.DumpDt);

        vm.SearchText = "SRV-PROD";
        Assert.Equal("Бухгалтерия", Assert.Single(vm.FilteredItems).Title);

        vm.SearchText = "кадры";
        Assert.Equal("Зарплата", Assert.Single(vm.FilteredItems).Title);

        vm.SearchText = "zup";
        Assert.Single(vm.FilteredItems);

        vm.SearchText = string.Empty;
        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void ShouldSkipRunning_AllowedOnlyForDumpWithOption()
    {
        var running = Base("Demo 27");
        running.IsEnterpriseRunning = true;
        var vm = new BatchOperationsViewModel(new[] { running }, BatchOperationKind.DumpDt);

        Assert.True(vm.ShouldSkipRunning(running));
        vm.AllowRunningBases = true;
        Assert.False(vm.ShouldSkipRunning(running));

        vm.Kind = BatchOperationKind.LoadConfig;
        Assert.True(vm.ShouldSkipRunning(running));
    }

    [Fact]
    public void SuccessfulItem_IsUnchecked()
    {
        var vm = ReadProjectFile("Workspace1C/ViewModels/BatchOperationsViewModel.cs");
        var success = vm.IndexOf("item.Status = BatchOperationsStatus.Success;", StringComparison.Ordinal);
        Assert.True(success > 0);
        Assert.Contains("item.IsChecked = false;", vm.Substring(success, 600));
    }

    [Fact]
    public void BatchWindow_ListOnLeftFullHeight_NarrowerColumn()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.xaml");
        Assert.Contains("<ColumnDefinition Width=\"1*\" MinWidth=\"240\"/>", wpf);
        Assert.Contains("<ColumnDefinition Width=\"2*\"/>", wpf);
        Assert.Contains("SearchClearButton", wpf);
        Assert.Contains("AllowRunningCheck", wpf);

        var avalonia = ReadProjectFile("Workspace1C/Views/BatchOperationsWindow.Avalonia.cs");
        Assert.Contains("InnerLeftContent = searchIcon", avalonia);
        Assert.Contains("InnerRightContent = searchClear", avalonia);
        Assert.Contains("new ColumnDefinition(new GridLength(1, GridUnitType.Star))", avalonia);
    }

    // ===================== #98: порядок кнопок во всех WPF-окнах =====================

    [Fact]
    public void AllWpfWindows_CancelButtonIsLastInItsPanel()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Workspace1C", "Views"), "*.xaml"))
        {
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch { continue; }
            foreach (var parent in doc.Descendants())
            {
                var buttons = parent.Elements().Where(e => e.Name.LocalName == "Button").ToList();
                if (buttons.Count < 2)
                    continue;
                var cancelIndex = buttons.FindIndex(b => (string?)b.Attribute("IsCancel") == "True");
                if (cancelIndex >= 0 && cancelIndex != buttons.Count - 1)
                    offenders.Add($"{Path.GetFileName(file)}: {(string?)buttons[cancelIndex].Attribute(x + "Name") ?? "Cancel"}");
            }
        }
        Assert.True(offenders.Count == 0, "«Отмена/Закрыть» не последней: " + string.Join("; ", offenders));
    }

    [Fact]
    public void TagPickWindow_OkBeforeCancel_BothPlatforms()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/TagPickWindow.xaml");
        Assert.True(wpf.IndexOf("x:Name=\"OkButton\"", StringComparison.Ordinal)
                    < wpf.IndexOf("IsCancel=\"True\"", StringComparison.Ordinal));
        // Avalonia-окно строит кнопки через общий BuildButtons (ОК слева, «Отмена» последней).
        var avaloniaBase = ReadProjectFile("Workspace1C/Views/ModalWindowBase.cs");
        Assert.True(avaloniaBase.IndexOf("panel.Children.Add(BuildConfirmButton", StringComparison.Ordinal)
                    < avaloniaBase.IndexOf("panel.Children.Add(BuildCancelButton", StringComparison.Ordinal));
    }

    // ===================== #101: Вариант 4 =====================

    [Fact]
    public void RunningChips_Variant4_RedOnHang_BiggerEnterpriseIcon()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        var play = wpf.IndexOf("Kind=\"Play\" Width=\"16\" Height=\"16\"", StringComparison.Ordinal);
        Assert.True(play > 0);
        var chip = wpf.LastIndexOf("<Border Width=\"18\" Height=\"18\"", play, StringComparison.Ordinal);
        Assert.True(chip > 0, "значок Play без чипа 18 px");
        var chipMarkup = wpf[chip..play];
        Assert.Contains("{DynamicResource ErrorBrush}", chipMarkup);
        Assert.Contains("Value=\"Hung\"", chipMarkup);
        Assert.Contains("Kind=\"Play\" Width=\"16\" Height=\"16\"", wpf);

        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.Tree.cs");
        Assert.Contains("BuildRunningChip", avalonia);
        Assert.Contains("RunningChipBrushConverter", avalonia);
        Assert.Contains("RunningEnterpriseGlyph", avalonia);
    }

    // ===================== Версия релиза =====================

    [Fact]
    public void Csproj_VersionBumpedTo100125_AllFourProperties()
    {
        var csproj = ReadProjectFile("Workspace1C/Workspace1C.csproj");
        Assert.Contains("<Version>1.0.1.25</Version>", csproj);
        Assert.Contains("<AssemblyVersion>1.0.1.25</AssemblyVersion>", csproj);
        Assert.Contains("<FileVersion>1.0.1.25</FileVersion>", csproj);
        Assert.Contains("<InformationalVersion>1.0.1.25</InformationalVersion>", csproj);
    }

    [Fact]
    public void Changelog_HasSection100125()
    {
        var changelog = ReadProjectFile("CHANGELOG.md");
        var start = changelog.IndexOf("## [1.0.1.25]", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = changelog.IndexOf("## [1.0.1.24]", start, StringComparison.Ordinal);
        var section = changelog[start..end];
        foreach (var issue in new[] { "#79", "#83", "#92", "#98", "#101" })
            Assert.Contains(issue, section);
    }
}
