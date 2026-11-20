using System;
using System.IO;
using System.Threading.Tasks;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// CLI-хук ПРО-команд: разбор <c>--pipeline</c>/<c>--decrypt</c>, реестр обработчиков,
/// код 6 без лицензии и вызов зарегистрированного обработчика.
/// </summary>
[Collection("ProLicensing")]
public sealed class ProCliCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c-cli-" + Guid.NewGuid().ToString("N"));

    public ProCliCommandsTests()
    {
        Directory.CreateDirectory(_dir);
        EditionService.DirectoryOverride = _dir;
        EditionService.Reset();
    }

    public void Dispose()
    {
        EditionService.DirectoryOverride = null;
        EditionService.Reset();
        ProCliCommands.Unregister(CliCommandKind.Pipeline);
        ProCliCommands.Unregister(CliCommandKind.Decrypt);
        try { Directory.Delete(_dir, true); } catch { /* временный каталог */ }
    }

    [Fact]
    public void Parse_Pipeline_WithValue()
    {
        var parsed = CliArgs.Parse(new[] { "--pipeline", "nightly" });

        Assert.NotNull(parsed);
        Assert.Equal(CliCommandKind.Pipeline, parsed!.Kind);
        Assert.Equal("nightly", parsed.Target);
    }

    [Fact]
    public void Parse_Decrypt_EqualsForm()
    {
        var parsed = CliArgs.Parse(new[] { @"--decrypt=C:\backup\1Cv8.dt.w1cenc" });

        Assert.NotNull(parsed);
        Assert.Equal(CliCommandKind.Decrypt, parsed!.Kind);
        Assert.Equal(@"C:\backup\1Cv8.dt.w1cenc", parsed.Target);
    }

    [Theory]
    [InlineData("--pipeline")]
    [InlineData("--decrypt")]
    public void Parse_ProCommand_RequiresTarget(string arg)
    {
        var parsed = CliArgs.Parse(new[] { arg });

        Assert.NotNull(parsed);
        Assert.False(string.IsNullOrWhiteSpace(parsed!.Error));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutLicense_ReturnsProRequired()
    {
        var parsed = new CliParseResult { Kind = CliCommandKind.Pipeline, Target = "nightly" };

        var code = await ProCliCommands.ExecuteAsync(parsed, ProFeature.Pipelines);

        Assert.Equal(CliExitCodes.ProRequired, code);
        Assert.Equal(6, code);
    }

    [Fact]
    public async Task InvokeAsync_UsesRegisteredHandler()
    {
        ProCliCommands.Register(CliCommandKind.Pipeline, (_, _) => Task.FromResult(42));

        var code = await ProCliCommands.InvokeAsync(
            new CliParseResult { Kind = CliCommandKind.Pipeline });

        Assert.Equal(42, code);
    }

    [Fact]
    public async Task InvokeAsync_WithoutHandler_ReturnsProRequired()
    {
        ProCliCommands.Unregister(CliCommandKind.Decrypt);

        var code = await ProCliCommands.InvokeAsync(
            new CliParseResult { Kind = CliCommandKind.Decrypt });

        Assert.Equal(CliExitCodes.ProRequired, code);
    }
}
