using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>Быстрый доступ к серверу по RDP/SSH (2.0): построение команд без запуска процессов.</summary>
public sealed class RemoteAccessLauncherTests
{
    private static ManagementServer Server(string address = "srv1c:1545") => new() { Description = "Прод", Address = address };

    [Theory]
    [InlineData("srv1c:1545", "srv1c")]
    [InlineData("srv1c", "srv1c")]
    [InlineData("10.0.0.5:1540", "10.0.0.5")]
    [InlineData("fe80::1", "fe80::1")]
    public void HostOf_StripsOneCPort(string address, string expected)
    {
        Assert.Equal(expected, RemoteAccessLauncher.HostOf(Server(address)));
    }

    [Fact]
    public void Rdp_Windows_DefaultsToServerHost()
    {
        var cmd = RemoteAccessLauncher.BuildRdpCommands(Server(), windows: true).Single();
        Assert.Equal("mstsc.exe", cmd.FileName);
        Assert.Equal(new[] { "/v:srv1c" }, cmd.Arguments);
    }

    [Fact]
    public void Rdp_Windows_WithUser_UsesRdpFile_WithoutPassword()
    {
        var server = Server();
        server.RdpHost = "rdp.example.local:3390";
        server.RdpUser = @"CORP\admin";
        server.Password = "секрет-кластера";

        var cmd = RemoteAccessLauncher.BuildRdpCommands(server, windows: true, rdpFilePath: @"C:\tmp\Прод.rdp").Single();
        Assert.Equal(new[] { @"C:\tmp\Прод.rdp" }, cmd.Arguments);

        var file = RemoteAccessLauncher.BuildRdpFile(server);
        Assert.Contains("full address:s:rdp.example.local:3390", file);
        Assert.Contains(@"username:s:CORP\admin", file);
        Assert.DoesNotContain("секрет", file);
    }

    [Fact]
    public void Rdp_Linux_TriesFreeRdpThenRemmina()
    {
        var server = Server();
        server.RdpUser = "admin";
        var cmds = RemoteAccessLauncher.BuildRdpCommands(server, windows: false);
        Assert.Equal(new[] { "xfreerdp3", "xfreerdp", "remmina" }, cmds.Select(c => c.FileName));
        Assert.Contains("/u:admin", cmds[0].Arguments);
        Assert.Contains("/v:srv1c", cmds[0].Arguments);
        Assert.Equal("rdp://admin@srv1c", cmds[2].Arguments[1]);
    }

    [Fact]
    public void Ssh_TargetAndPort()
    {
        var server = Server();
        RemoteAccessLauncher.ApplySshTarget(server, "usr1cv8@app01:2222");
        Assert.Equal("usr1cv8@app01", server.SshTarget);
        Assert.Equal(2222, server.SshPort);
        Assert.Equal(new[] { "-p", "2222", "usr1cv8@app01" }, RemoteAccessLauncher.BuildSshArguments(server));
        Assert.Equal("usr1cv8@app01:2222", RemoteAccessLauncher.FormatSshTarget(server));
    }

    [Fact]
    public void Ssh_EmptyTarget_UsesServerHost_DefaultPort()
    {
        var server = Server();
        RemoteAccessLauncher.ApplySshTarget(server, "  ");
        Assert.Equal(new[] { "srv1c" }, RemoteAccessLauncher.BuildSshArguments(server));
    }

    [Fact]
    public void Ssh_Windows_PrefersWindowsTerminal_ThenOpenSsh()
    {
        var cmds = RemoteAccessLauncher.BuildSshCommands(Server(), windows: true);
        Assert.Equal("wt.exe", cmds[0].FileName);
        Assert.Equal(new[] { "ssh", "srv1c" }, cmds[0].Arguments);
        Assert.Equal("ssh.exe", cmds[1].FileName);
        Assert.True(cmds[1].UseShellExecute);
    }

    [Fact]
    public void Ssh_Linux_TerminalCandidates()
    {
        var cmds = RemoteAccessLauncher.BuildSshCommands(Server(), windows: false);
        Assert.Equal("x-terminal-emulator", cmds[0].FileName);
        Assert.Equal(new[] { "-e", "ssh", "srv1c" }, cmds[0].Arguments);
        Assert.Contains(cmds, c => c.FileName == "gnome-terminal" && c.Arguments.SequenceEqual(new[] { "--", "ssh", "srv1c" }));
    }

    [Fact]
    public void NoAddress_NoCommands()
    {
        var server = Server("");
        Assert.Empty(RemoteAccessLauncher.BuildRdpCommands(server, windows: true));
        Assert.Empty(RemoteAccessLauncher.BuildSshCommands(server, windows: false));
    }
}
