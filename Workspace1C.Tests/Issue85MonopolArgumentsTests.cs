using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #85 «Групповое применение» (v1.0.1.43, этап C): монопольный запуск
/// Предприятия — ключ <c>/M</c> добавляется в конец строки запуска «Предприятия»
/// (<see cref="OneCLauncher.BuildArguments"/>, Windows + Linux) и НЕ добавляется
/// для «Конфигуратора» (ключ неприменим) и при выключенной опции.
/// </summary>
public sealed class Issue85MonopolArgumentsTests
{
    private static Infobase FileBase() => new()
    {
        Id = "base-m",
        Name = "Монопольно",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = @"C:\bases\mono"
        }
    };

    [Fact]
    public void Enterprise_WithMonopol_AppendsMKey()
    {
        var args = OneCLauncher.BuildArguments(
            FileBase(), OneCLaunchMode.Enterprise, null, null, monopol: true);

        // /M добавляется в конец строки запуска, после ключей из 1CLaunch.cfg.
        Assert.EndsWith(" /M", args);
    }

    [Fact]
    public void Enterprise_WithoutMonopol_HasNoMKey()
    {
        var args = OneCLauncher.BuildArguments(
            FileBase(), OneCLaunchMode.Enterprise, null, null, monopol: false);

        Assert.DoesNotContain("/M", args);
    }

    [Fact]
    public void Configurator_MonopolIsIgnored()
    {
        var args = OneCLauncher.BuildArguments(
            FileBase(), OneCLaunchMode.Configurator, null, null, monopol: true);

        // Ключ /M применим только к «Предприятию» — Конфигуратору он не передаётся.
        Assert.DoesNotContain("/M", args);
    }

    [Fact]
    public void Enterprise_WithMonopol_KeepsConnectionAndRunModeKeys()
    {
        var ib = FileBase();
        var args = OneCLauncher.BuildArguments(
            ib, OneCLaunchMode.Enterprise, null, OneCRunMode.Managed, monopol: true);

        Assert.Contains("/F", args);
        Assert.Contains("/RunModeManagedApplication", args);
        Assert.EndsWith(" /M", args);
    }
}
