using System.Text;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Пользователи информационных баз (выпуск 1.0.0.83): протокол COM-скрипта
/// (<see cref="ComScriptRunner"/>), разбор ответа, параметры операций, проверки формы
/// и логика окна. На Windows дополнительно проверяется реальный запуск PowerShell:
/// несуществующий ProgID должен дать код <c>noprogid</c> (значит, скрипт синтаксически
/// корректен и протокол stdin/stdout работает).
/// </summary>
public sealed class InfobaseUsersServiceTests
{
    private static string Response(string json) =>
        ComScriptRunner.ResultPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ParseOutput_Success_ReturnsData()
    {
        var stdout = "noise\r\n" + Response("{\"ok\":true,\"progId\":\"V83.COMConnector\",\"users\":[]}") + "\r\n";
        var result = ComScriptRunner.ParseOutput(stdout);
        Assert.True(result.Success);
        Assert.Equal("V83.COMConnector", result.ProgId);
        Assert.Equal(JsonValueKind.Array, result.Data.GetProperty("users").ValueKind);
    }

    [Fact]
    public void ParseOutput_Failure_MasksSecrets()
    {
        var stdout = Response("{\"ok\":false,\"code\":\"connect\",\"error\":\"Bad Srvr=x;Pwd=S3cr3t!;\"}");
        var result = ComScriptRunner.ParseOutput(stdout, new[] { "S3cr3t!", null, "" });
        Assert.False(result.Success);
        Assert.Equal("connect", result.Code);
        Assert.DoesNotContain("S3cr3t!", result.Error);
        Assert.Contains("***", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no marker here")]
    public void ParseOutput_NoMarker_IsProtocolError(string? stdout)
    {
        var result = ComScriptRunner.ParseOutput(stdout);
        Assert.False(result.Success);
        Assert.Equal("protocol", result.Code);
    }

    [Fact]
    public void ParseOutput_BadBase64_IsProtocolError()
    {
        var result = ComScriptRunner.ParseOutput(ComScriptRunner.ResultPrefix + "%%%");
        Assert.Equal("protocol", result.Code);
    }

    [Fact]
    public void ParseOutput_TakesLastMarker()
    {
        var stdout = Response("{\"ok\":false,\"code\":\"op\"}") + "\n" + Response("{\"ok\":true}");
        Assert.True(ComScriptRunner.ParseOutput(stdout).Success);
    }

    [Fact]
    public void BuildRequest_RoundTrips_UnicodeAndSecrets()
    {
        var b64 = ComScriptRunner.BuildRequest(new[] { "V83.COMConnector" }, "File=\"C:\\Базы\\Бух\";Usr=\"Админ\";Pwd=\"p;\"\"\";",
            new Dictionary<string, object?> { ["op"] = "list", ["name"] = "Иванов" });
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("V83.COMConnector", doc.RootElement.GetProperty("progIds")[0].GetString());
        Assert.Equal("list", doc.RootElement.GetProperty("op").GetString());
        Assert.Equal("Иванов", doc.RootElement.GetProperty("name").GetString());
        Assert.Contains("Pwd=", doc.RootElement.GetProperty("connect").GetString());
    }

    [Fact]
    public void Script_FitsCommandLineLimit_AndContainsAllOperations()
    {
        var script = ComScriptRunner.BuildScript(InfobaseUsersService.OperationBody);
        var encoded = ComScriptRunner.EncodeCommand(script);
        Assert.True(encoded.Length < ComScriptRunner.MaxEncodedCommandLength, $"encoded length {encoded.Length}");
        foreach (var op in new[] { "'list'", "'roles'", "'upsert'", "'password'", "'disable'", "'delete'" })
            Assert.Contains(op, script);
        Assert.Contains("function Invoke-Op", script);
        // Пароли не должны попадать в текст скрипта (он виден в командной строке процесса).
        Assert.DoesNotContain("Pwd=", script);
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void ParseList_SortsAndMapsFlags()
    {
        var data = JsonDocument.Parse("""
            {"ok":true,"users":[
              {"name":"Петров","fullName":"Петров П.","std":true,"os":false,"osUser":"","show":true,"pwd":true,"noChange":false},
              {"name":"Администратор","fullName":"","std":false,"os":true,"osUser":"\\\\DOM\\admin","show":false,"pwd":false,"noChange":true}
            ],"roles":["ПолныеПрава","Бухгалтер"]}
            """).RootElement;
        var (users, roles) = InfobaseUsersService.ParseList(data);
        Assert.Equal(new[] { "Администратор", "Петров" }, users.Select(u => u.Name));
        Assert.True(users[0].OSAuthentication);
        Assert.Equal(@"\\DOM\admin", users[0].OSUser);
        Assert.True(users[0].CannotChangePassword);
        Assert.True(users[1].PasswordIsSet);
        Assert.Equal(new[] { "Бухгалтер", "ПолныеПрава" }, roles);
    }

    [Fact]
    public void ParseList_SingleObjectInsteadOfArray_IsAccepted()
    {
        // ConvertTo-Json в PowerShell 5.1 может развернуть массив из одного элемента.
        var data = JsonDocument.Parse("""{"users":{"name":"Один","std":true},"roles":"ПолныеПрава"}""").RootElement;
        var (users, roles) = InfobaseUsersService.ParseList(data);
        Assert.Single(users);
        Assert.Equal("Один", users[0].Name);
        Assert.Equal(new[] { "ПолныеПрава" }, roles);
    }

    [Fact]
    public void ParseRoles_SplitsTrimsAndDeduplicates()
    {
        Assert.Equal(new[] { "ПолныеПрава", "Бухгалтер", "Кассир" },
            InfobaseUsersService.ParseRoles(" ПолныеПрава, Бухгалтер;\nКассир, полныеправа ,"));
        Assert.Empty(InfobaseUsersService.ParseRoles("  "));
        Assert.Empty(InfobaseUsersService.ParseRoles(null));
    }

    [Fact]
    public void Validate_RequiresNameAndAuth()
    {
        Assert.NotNull(InfobaseUsersService.Validate(new InfobaseUserSpec { Name = " " }));
        Assert.NotNull(InfobaseUsersService.Validate(new InfobaseUserSpec { Name = "a", StandardAuthentication = false, OSAuthentication = false }));
        Assert.NotNull(InfobaseUsersService.Validate(new InfobaseUserSpec { Name = "a", OSAuthentication = true, OSUser = "DOM\\x" }));
        Assert.Null(InfobaseUsersService.Validate(new InfobaseUserSpec { Name = "a", OSAuthentication = true, OSUser = "\\\\DOM\\x" }));
        Assert.Null(InfobaseUsersService.Validate(new InfobaseUserSpec { Name = "a" }));
    }

    [Fact]
    public void UpsertParameters_PasswordOnlyWhenRequested()
    {
        var spec = new InfobaseUserSpec { Name = " Иванов ", Password = "x", SetPassword = false, Roles = { "Р1" }, ReplaceRoles = true };
        var p = InfobaseUsersService.BuildUpsertParameters(spec);
        Assert.Equal("upsert", p["op"]);
        Assert.Equal("Иванов", p["name"]);
        Assert.Equal(string.Empty, p["password"]);
        Assert.Equal(false, p["setPassword"]);
        Assert.Equal(true, p["replaceRoles"]);
        spec.SetPassword = true;
        Assert.Equal("x", InfobaseUsersService.BuildUpsertParameters(spec)["password"]);
    }

    [Fact]
    public void DescribeFailure_UsesCode()
    {
        Assert.Contains("boom", InfobaseUsersService.DescribeFailure(ComScriptResult.Fail("connect", "boom")));
        Assert.Contains("boom", InfobaseUsersService.DescribeFailure(ComScriptResult.Fail("op", "boom")));
        Assert.Equal("boom", InfobaseUsersService.DescribeFailure(ComScriptResult.Fail("timeout", "boom")));
    }

    private static Infobase FileBase(string name) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = "/tmp/" + name }
    };

    [Fact]
    public void ViewModel_PreselectsAndChecksSelectedBase_SkipsWebBases()
    {
        var a = FileBase("Альфа");
        var b = FileBase("Бета");
        var web = new Infobase { Id = "w", Name = "Веб", Connection = new ConnectionSettings { Type = ConnectionType.WebServer } };
        var vm = new InfobaseUsersViewModel(new[] { b, a, web }, b);
        Assert.Equal(new[] { "Альфа", "Бета" }, vm.Targets.Select(t => t.Name));
        Assert.Same(b, vm.SelectedTarget!.Infobase);
        Assert.Single(vm.CheckedTargets);
        vm.SearchText = "аль";
        Assert.Single(vm.Targets);
    }

    [Fact]
    public void ViewModel_SelectingUserFillsForm_NewUserClears()
    {
        var vm = new InfobaseUsersViewModel(new[] { FileBase("A") }, null);
        vm.SelectedUser = new InfobaseUserRow(new InfobaseUserInfo
        {
            Name = "Петров", FullName = "Петров П.", StandardAuthentication = false, OSAuthentication = true, OSUser = "\\\\D\\p"
        });
        Assert.Equal("Петров", vm.UserName);
        Assert.False(vm.SetPasswordFlag);
        Assert.True(vm.OSAuthentication);
        vm.NewUser();
        Assert.Equal(string.Empty, vm.UserName);
        Assert.True(vm.SetPasswordFlag);
        Assert.True(vm.StandardAuthentication);
    }

    [Fact]
    public async Task ViewModel_Batch_WithoutChecked_LogsAndReturnsZero()
    {
        var vm = new InfobaseUsersViewModel(new[] { FileBase("A") }, null) { UserName = "x" };
        Assert.Equal(0, await vm.RunBatchAsync(InfobaseUserBatchOperation.Delete));
        Assert.False(string.IsNullOrEmpty(vm.LogText));
    }

    [Fact]
    public void Row_AuthText_CoversAllCombinations()
    {
        string Auth(bool std, bool os) => new InfobaseUserRow(new InfobaseUserInfo { StandardAuthentication = std, OSAuthentication = os }).AuthText;
        var texts = new[] { Auth(true, true), Auth(true, false), Auth(false, true), Auth(false, false) };
        Assert.Equal(4, texts.Distinct().Count());
    }

    [Fact]
    public void ConfirmText_ContainsUserAndCount()
    {
        foreach (var op in Enum.GetValues<InfobaseUserBatchOperation>())
        {
            var text = InfobaseUsersDialogs.ConfirmText(op, " Иванов ", 7);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void MenuCatalog_HasInfobaseUsersItem_InOperations()
    {
        var item = UtilitiesMenuCatalog.All.Single(i => i.Id == "InfobaseUsers");
        Assert.Equal(UtilitiesMenuCommandKind.InfobaseUsers, item.CommandKind);
        Assert.Equal(UtilitiesMenuCatalog.GroupDevelopment, item.DefaultGroup);
    }

    [Fact]
    public async Task RunAsync_RealPowerShell_UnknownProgId_ReportsNoProgId()
    {
        if (!OperatingSystem.IsWindows())
        {
            var linux = await ComScriptRunner.RunAsync(InfobaseUsersService.OperationBody, new[] { "X" }, "File=\"x\";",
                new Dictionary<string, object?> { ["op"] = "list" }, null, TimeSpan.FromSeconds(5));
            Assert.Equal("platform", linux.Code);
            return;
        }
        var result = await ComScriptRunner.RunAsync(InfobaseUsersService.OperationBody,
            new[] { "Workspace1C.Nonexistent.ProgId" }, "File=\"C:\\nonexistent\";Pwd=\"secret\";",
            new Dictionary<string, object?> { ["op"] = "list" }, new[] { "secret" }, TimeSpan.FromSeconds(90));
        Assert.False(result.Success);
        Assert.Equal("noprogid", result.Code);
        Assert.DoesNotContain("secret", result.Error ?? string.Empty);
    }
}
