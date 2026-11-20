using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты резолвера контекста запуска платформы для подстановок сценариев (issue #67):
/// токены %full_launch_enterprise%/%full_launch_config%/%full_exe_name%/%platform_version%.
/// Реальный поиск платформы на диске проверяется только через переопределяемый резолвер —
/// сам поиск не зависит от логики теста.
/// </summary>
[Collection("ScriptLaunchContextState")]
public sealed class ScriptLaunchContextResolverTests
{
    private static ScriptLaunchContext MakeContext() => new(
        ExePath: @"C:\Program Files\1cv8\8.5.4.1878\bin\1cv8.exe",
        PlatformVersion: "8.5.4.1878",
        EnterpriseCommandLine: @"""C:\Program Files\1cv8\8.5.4.1878\bin\1cv8.exe"" ENTERPRISE /S""srv-1c\acc_main""",
        ConfiguratorCommandLine: @"""C:\Program Files\1cv8\8.5.4.1878\bin\1cv8.exe"" DESIGNER /S""srv-1c\acc_main""");

    [Fact]
    public void Resolve_NullBase_ReturnsNull()
    {
        Assert.Null(ScriptLaunchContextResolver.Resolve(null));
    }

    [Fact]
    public void Resolve_Override_IsUsedForBase()
    {
        var ib = new Infobase { Name = "Бухгалтерия" };
        var expected = MakeContext();
        ScriptLaunchContextResolver.ResolverOverride = _ => expected;
        try
        {
            var resolved = ScriptLaunchContextResolver.Resolve(ib);
            Assert.Same(expected, resolved);
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void Resolve_WithoutOverride_NeverThrows()
    {
        // Платформа на тестовой машине может не быть установлена — резолвер должен
        // вернуть null, а не бросить исключение (токены остаются пустыми).
        var ib = new Infobase { Name = "База" };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = "srv";
        ib.Connection.DatabaseName = "db";
        ScriptLaunchContextResolver.ResolverOverride = null;
        var resolved = ScriptLaunchContextResolver.Resolve(ib);
        // Допустимо и null (нет платформы), и контекст (платформа найдена) —
        // главное: без исключений.
        Assert.True(resolved is null || resolved is ScriptLaunchContext);
    }

    [Fact]
    public void LaunchContext_Entries_AreStableStrings()
    {
        var ctx = MakeContext();
        Assert.Equal(@"C:\Program Files\1cv8\8.5.4.1878\bin\1cv8.exe", ctx.ExePath);
        Assert.Equal("8.5.4.1878", ctx.PlatformVersion);
        Assert.Contains("ENTERPRISE", ctx.EnterpriseCommandLine);
        Assert.Contains("DESIGNER", ctx.ConfiguratorCommandLine);
    }
}