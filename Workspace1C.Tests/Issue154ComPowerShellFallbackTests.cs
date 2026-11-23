using System.Text;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #154: кнопка «Определить» падала с нативным fast-fail <c>0xC0000409</c>, потому что
/// COM-агент — дочерний экземпляр этого же CoreCLR-приложения. Проверяем безопасный путь через
/// Windows PowerShell 5.1: классификацию кода возврата, сборку запроса (Base64, ProgID,
/// отсутствие пароля в аргументах процесса), выбор 32-битного хоста и source-гварды на то,
/// что команда «Определить» действительно использует этот путь. Реальный COM/1С не запускается.
/// </summary>
public sealed class Issue154ComPowerShellFallbackTests
{
    private static string Response(string json) =>
        ComScriptRunner.ResultPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

#if !LINUX
    [Theory]
    [InlineData("0xC0000409", true)]  // STATUS_STACK_BUFFER_OVERRUN / __fastfail — дефект из issue
    [InlineData("0xc0000409", true)]  // регистр не важен
    [InlineData("0xC00000FD", true)]  // stack overflow
    [InlineData("0xC0000005", true)]  // access violation
    [InlineData("0x80000003", true)]  // breakpoint
    [InlineData("0x80004005", false)] // обычная COM-ошибка
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsNativeFastFail_ClassifiesAgentExitCodes(string? code, bool expected)
        => Assert.Equal(expected, ComConfigInfoPowerShell.IsNativeFastFail(code));

    [Fact]
    public void ShouldFallback_FastFailCrash_True()
    {
        var result = ComReadResult.Fail(ComFailureKind.AgentCrashed, "0xC0000409");
        Assert.True(ComConfigInfoPowerShell.ShouldFallback(result));
    }

    [Fact]
    public void ShouldFallback_NonNativeCrash_False()
    {
        var result = ComReadResult.Fail(ComFailureKind.AgentCrashed, "0x80004005");
        Assert.False(ComConfigInfoPowerShell.ShouldFallback(result));
    }

    [Fact]
    public void ShouldFallback_OrdinaryFailures_False()
    {
        Assert.False(ComConfigInfoPowerShell.ShouldFallback(ComReadResult.Fail(ComFailureKind.Timeout)));
        Assert.False(ComConfigInfoPowerShell.ShouldFallback(ComReadResult.Fail(ComFailureKind.DatabaseError)));
        Assert.False(ComConfigInfoPowerShell.ShouldFallback(ComReadResult.Fail(ComFailureKind.Transport)));
    }

    [Fact]
    public void ShouldFallback_NotRegisteredAndInstanceFailed_True()
    {
        Assert.True(ComConfigInfoPowerShell.ShouldFallback(
            ComReadResult.Fail(ComFailureKind.NotRegistered)));
        Assert.True(ComConfigInfoPowerShell.ShouldFallback(
            ComReadResult.Fail(ComFailureKind.InstanceFailed, "V83.COMConnector")));
    }

    [Fact]
    public void ShouldFallback_Disabled_OnlyAfterObservedFastFail()
    {
        ComConfigInfoPowerShell.ResetSessionState();
        try
        {
            Assert.False(ComConfigInfoPowerShell.ShouldFallback(
                ComReadResult.Fail(ComFailureKind.Disabled)));

            ComConfigInfoPowerShell.NoteAgentFailure(
                ComReadResult.Fail(ComFailureKind.AgentCrashed, "0xC0000409"));
            Assert.True(ComConfigInfoPowerShell.PreferPowerShell);
            Assert.True(ComConfigInfoPowerShell.ShouldFallback(
                ComReadResult.Fail(ComFailureKind.Disabled)));
        }
        finally
        {
            ComConfigInfoPowerShell.ResetSessionState();
        }
    }

    [Fact]
    public void MapResult_Success_ReturnsConfigurationInfoAndProgId()
    {
        var json = JsonDocument.Parse(
            "{\"ok\":true,\"progId\":\"V85.COMConnector\",\"name\":\"Бухгалтерия\",\"version\":\"3.0.1.2\"}")
            .RootElement.Clone();
        var script = new ComScriptResult
        {
            Success = true,
            ProgId = "V85.COMConnector",
            Data = json
        };

        var result = ComConfigInfoPowerShell.MapResult(script);

        Assert.Equal(ComFailureKind.None, result.Failure);
        Assert.NotNull(result.Info);
        Assert.Equal("Бухгалтерия", result.Info!.Value.Name);
        Assert.Equal("3.0.1.2", result.Info.Value.Version);
        Assert.Equal("V85.COMConnector", result.UsedProgId);
    }

    [Fact]
    public void MapResult_ConnectFailure_IsDatabaseErrorWithProgId()
    {
        var script = new ComScriptResult
        {
            Success = false,
            Code = "connect",
            Error = "bad password",
            ProgId = "V83.COMConnector"
        };

        var result = ComConfigInfoPowerShell.MapResult(script);

        Assert.Equal(ComFailureKind.DatabaseError, result.Failure);
        Assert.Equal("bad password", result.Detail);
        Assert.Equal("V83.COMConnector", result.UsedProgId);
    }

    [Fact]
    public void MapResult_NoProgId_IsNotRegistered()
    {
        var script = new ComScriptResult
        {
            Success = false,
            Code = "noprogid",
            Error = "not registered"
        };

        var result = ComConfigInfoPowerShell.MapResult(script);

        Assert.Equal(ComFailureKind.NotRegistered, result.Failure);
    }

    [Fact]
    public void MapResult_EmptyMetadata_IsMetadataRead()
    {
        var json = JsonDocument.Parse("{\"ok\":true,\"name\":\"\",\"version\":\"\"}").RootElement.Clone();
        var script = new ComScriptResult
        {
            Success = true,
            ProgId = "V85.COMConnector",
            Data = json
        };

        var result = ComConfigInfoPowerShell.MapResult(script);

        Assert.Equal(ComFailureKind.MetadataRead, result.Failure);
        Assert.Equal("V85.COMConnector", result.UsedProgId);
    }
#endif

    [Fact]
    public void BuildRequest_IsBase64Json_WithProgIdsAndConnect()
    {
        const string connect = "File=\"C:\\Базы\\Бух\";Usr=\"Админ\";Pwd=\"S3cr3t!\";";

        var request = ComScriptRunner.BuildRequest(
            new[] { "V85.COMConnector", "V83.COMConnector" },
            connect,
            new Dictionary<string, object?> { ["op"] = "config" });

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(request));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("progIds").GetArrayLength());
        Assert.Equal("V85.COMConnector", doc.RootElement.GetProperty("progIds")[0].GetString());
        Assert.Equal("config", doc.RootElement.GetProperty("op").GetString());
        Assert.Equal(connect, doc.RootElement.GetProperty("connect").GetString());
    }

    [Fact]
    public void PowerShellArguments_DoNotContainConnectionStringOrPassword()
    {
#if !LINUX
        var script = ComScriptRunner.BuildScript(ComConfigInfoPowerShell.OperationBody);
        var encoded = ComScriptRunner.EncodeCommand(script);
        Assert.True(encoded.Length < ComScriptRunner.MaxEncodedCommandLength,
            $"encoded length {encoded.Length}");

        var arguments = ComScriptRunner.BuildArguments(encoded);
        Assert.Contains("-EncodedCommand", arguments);
        Assert.DoesNotContain(arguments, a => a.Contains("Pwd=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.Contains("S3cr3t!", StringComparison.Ordinal));

        // Текст скрипта виден в командной строке процесса, поэтому пароля там быть не должно.
        var decodedScript = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.DoesNotContain("Pwd=", decodedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("S3cr3t!", decodedScript, StringComparison.Ordinal);
        Assert.Contains("function Invoke-Op", decodedScript, StringComparison.Ordinal);
        Assert.Contains("'Metadata'", decodedScript, StringComparison.Ordinal);
        Assert.Contains("'Version'", decodedScript, StringComparison.Ordinal);
#else
        // На Linux безопасный путь не компилируется (WINDOWS-only); проверяем, что протокол
        // запроса всё равно не кладёт пароль в аргументы.
        var arguments = ComScriptRunner.BuildArguments("encoded");
        Assert.DoesNotContain(arguments, a => a.Contains("Pwd=", StringComparison.Ordinal));
#endif
    }

    [Fact]
    public void PowerShellPath_UsesSystem32ForX64AndSysWow64ForX86()
    {
        var x64 = ComScriptRunner.PowerShellPath(x86: false);
        var x86 = ComScriptRunner.PowerShellPath(x86: true);

        Assert.Contains(Path.Combine("System32", "WindowsPowerShell"), x64);
        if (Environment.Is64BitOperatingSystem)
            Assert.Contains("SysWOW64", x86);
        Assert.Equal("PowerShell 5.1 x64", ComScriptRunner.HostDescription(x86: false));
        Assert.Equal("PowerShell 5.1 x86", ComScriptRunner.HostDescription(x86: true));
    }

    [Fact]
    public void DetectCommand_UsesSafePowerShellFallback()
    {
        var connector = ReadProjectFile("Workspace1C", "Services", "OneCComConnector.cs");
        Assert.Contains("ComConfigInfoPowerShell.ShouldFallback", connector, StringComparison.Ordinal);
        Assert.Contains("ComConfigInfoPowerShell.Read", connector, StringComparison.Ordinal);
        Assert.Contains("ComConfigInfoPowerShell.NoteAgentFailure", connector, StringComparison.Ordinal);

        var fallback = ReadProjectFile("Workspace1C", "Services", "ComConfigInfoPowerShell.cs");
        Assert.Contains("0xC0000409", fallback, StringComparison.Ordinal);
        Assert.Contains("ComScriptRunner.RunAsync", fallback, StringComparison.Ordinal);
        Assert.Contains("Invoke-Op", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndApply_DoesNotRunSecondComPass()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "ConfigurationInfoService.cs");
        var start = source.IndexOf("public static OneCConfigInfo? ReadAndApply", StringComparison.Ordinal);
        Assert.True(start >= 0, "ReadAndApply не найден");

        var body = source.Substring(start);
        var end = body.IndexOf("\n    }", StringComparison.Ordinal);
        if (end > 0)
            body = body[..end];

        // Один вызов TryRead на операцию: иначе падение агента и fallback в PowerShell
        // повторялись бы дважды за одно нажатие «Определить» (issue #154).
        Assert.Contains("ApplyReadInfo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryApply(ib", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectFailure_ShowsHostAndBitnessHint_InBothPlatforms()
    {
        foreach (var file in new[] { "ConnectionSettingsWindow.xaml.cs", "ConnectionSettingsWindow.Avalonia.cs" })
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            Assert.Contains("ConfigurationInfoService.LastUsedHost", source, StringComparison.Ordinal);
            Assert.Contains("Connection.DetectUsedHostFormat", source, StringComparison.Ordinal);
            Assert.Contains("Connection.DetectBitnessHint", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Localization_ContainsIssue154Keys()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var strings = document.RootElement.GetProperty("strings");

            foreach (var key in new[]
                     {
                         "Connection.DetectUsedHostFormat",
                         "Connection.DetectBitnessHint",
                         "Com.PowerShellFallbackFailed"
                     })
            {
                Assert.True(strings.TryGetProperty(key, out _), $"{language}: отсутствует ключ {key}");
            }
        }
    }

    [Fact]
    public void ComScriptRunner_ParseOutput_KeepsHostForDiagnostics()
    {
        var stdout = Response("{\"ok\":false,\"code\":\"noprogid\"}");
        var result = ComScriptRunner.ParseOutput(stdout).WithHost(ComScriptRunner.HostDescription(x86: true));

        Assert.Equal("PowerShell 5.1 x86", result.Host);
        Assert.Equal("noprogid", result.Code);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));
}
