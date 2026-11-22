using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #150 «Веб-база: теряются параметры запуска /WSN и /WSP».
/// <para>
/// Ключи учётных данных веб-сервера должны быть в справочнике
/// (<see cref="OneCLaunchArgumentParser.KnownKeys"/>), а значение в кавычках,
/// следующее за ключом отдельным токеном («/WSN "user"»), должно привязываться
/// к этому ключу. Иначе при пересборке строки запуска ключ теряется, а голое
/// значение остаётся позиционным аргументом, и платформа запрашивает
/// аутентификацию на веб-сервере.
/// </para>
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class Issue150WebServerCredentialsTests
{
    private const string WebUrl = "https://1c.example.com/base";

    private static Infobase WebBase(string launchParameters) => new()
    {
        Id = "web-150",
        Name = "Веб-база",
        LaunchParameters = launchParameters,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.WebServer,
            WebUrl = WebUrl,
            AuthenticationMode = AuthenticationMode.Prompt
        }
    };

    private static Infobase FileBase(string launchParameters) => new()
    {
        Id = "file-150",
        Name = "Файловая",
        LaunchParameters = launchParameters,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = @"C:\bases\demo",
            AuthenticationMode = AuthenticationMode.Prompt
        }
    };

    // ---------- Справочник ключей ----------

    [Fact]
    public void KnownKeys_ContainWebServerCredentials()
    {
        Assert.Contains("/WSN", OneCLaunchArgumentParser.KnownKeys);
        Assert.Contains("/WSP", OneCLaunchArgumentParser.KnownKeys);
    }

    [Fact]
    public void Autocomplete_ByPrefix_FindsWebServerCredentials()
    {
        var keys = OneCLaunchArgumentParser.AutocompleteKnownKeys("/WS").ToList();

        Assert.Contains("/WSN", keys);
        Assert.Contains("/WSP", keys);
    }

    // ---------- Разбор параметров ----------

    [Fact]
    public void Parse_SeparateQuotedValues_BindsThemToKeys()
    {
        var parsed = OneCLaunchArgumentParser.ParseLaunchParameters("/WSN \"webuser\" /WSP \"****\"");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("/WSN", parsed[0].Key);
        Assert.Equal("webuser", parsed[0].Value);
        Assert.Equal("/WSP", parsed[1].Key);
        Assert.Equal("****", parsed[1].Value);
    }

    [Fact]
    public void Parse_GluedValues_BindsThemToKeys()
    {
        var parsed = OneCLaunchArgumentParser.ParseLaunchParameters("/WSN\"webuser\" /WSP\"****\"");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("/WSN", parsed[0].Key);
        Assert.Equal("webuser", parsed[0].Value);
        Assert.Equal("/WSP", parsed[1].Key);
        Assert.Equal("****", parsed[1].Value);
    }

    [Fact]
    public void Format_RoundTrip_KeepsWebServerCredentials()
    {
        var formatted = OneCLaunchArgumentParser.FormatLaunchParameters(
            OneCLaunchArgumentParser.ParseLaunchParameters("/WSN \"webuser\" /WSP \"****\""));

        Assert.Equal("/WSN \"webuser\" /WSP \"****\"", formatted);
    }

    // ---------- Сборка аргументов веб-базы ----------

    [Fact]
    public void BuildArguments_WebBase_ContainsConnectionAndCredentials()
    {
        var args = OneCLauncher.BuildArguments(
            WebBase("/WSN \"webuser\" /WSP \"****\""),
            OneCLaunchMode.Enterprise,
            OneCClientType.Thin,
            OneCRunMode.Managed);

        Assert.Contains($"/WS \"{WebUrl}\"", args);
        Assert.Contains("/WSN \"webuser\"", args);
        Assert.Contains("/WSP \"****\"", args);
    }

    [Fact]
    public void BuildArguments_WebBase_GluedCredentials_AreNormalizedToKeyValueForm()
    {
        var args = OneCLauncher.BuildArguments(
            WebBase("/WSN\"webuser\" /WSP\"****\""),
            OneCLaunchMode.Enterprise,
            OneCClientType.Thin,
            OneCRunMode.Managed);

        Assert.Contains("/WSN \"webuser\"", args);
        Assert.Contains("/WSP \"****\"", args);
    }

    [Fact]
    public void BuildArguments_WebBase_CredentialsAreNotPositionalArguments()
    {
        var args = OneCLauncher.BuildArguments(
            WebBase("/WSN \"webuser\" /WSP \"****\""),
            OneCLaunchMode.Enterprise,
            OneCClientType.Thin,
            OneCRunMode.Managed);

        var parsed = OneCLaunchArgumentParser.ParseLaunchParameters(args);

        // Ключи присутствуют и несут значения.
        var user = parsed.Single(a => a.Key == "/WSN");
        var password = parsed.Single(a => a.Key == "/WSP");
        Assert.Equal("webuser", user.Value);
        Assert.Equal("****", password.Value);

        // Ни одно значение не осталось «позиционным» токеном в кавычках.
        Assert.DoesNotContain(parsed, a => a.Key.StartsWith("\""));
    }

    [Fact]
    public void BuildArguments_NonWebBase_KeepsUnknownUserParameters()
    {
        var args = OneCLauncher.BuildArguments(
            FileBase("/CustomKey \"custom value\""),
            OneCLaunchMode.Enterprise,
            OneCClientType.Thick,
            OneCRunMode.Ordinary);

        Assert.Contains("/CustomKey \"custom value\"", args);
    }
}
