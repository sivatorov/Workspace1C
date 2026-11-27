using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #180: кнопка «Конфигуратор» должна быть недоступна для базы,
/// опубликованной на веб-сервере (как в штатном стартере 1С). Для файловых
/// и клиент-серверных баз поведение запуска Конфигуратора не меняется.
/// </summary>
public sealed class Issue180ConfiguratorAvailabilityTests
{
    private static Infobase MakeInfobase(ConnectionType type) => new()
    {
        Name = "Тестовая база",
        Connection = new ConnectionSettings { Type = type }
    };

    [Fact]
    public void WebServerInfobase_ConfiguratorUnavailable()
    {
        var infobase = MakeInfobase(ConnectionType.WebServer);

        Assert.False(ConfiguratorAvailability.IsConfiguratorAvailable(infobase));
    }

    [Fact]
    public void FileInfobase_ConfiguratorAvailable()
    {
        var infobase = MakeInfobase(ConnectionType.File);

        Assert.True(ConfiguratorAvailability.IsConfiguratorAvailable(infobase));
    }

    [Fact]
    public void ClientServerInfobase_ConfiguratorAvailable()
    {
        var infobase = MakeInfobase(ConnectionType.ClientServer);

        Assert.True(ConfiguratorAvailability.IsConfiguratorAvailable(infobase));
    }

    [Fact]
    public void NullInfobase_ConfiguratorUnavailable()
    {
        Assert.False(ConfiguratorAvailability.IsConfiguratorAvailable((Infobase?)null));
    }

    [Fact]
    public void NullConnectionSettings_ConfiguratorUnavailable()
    {
        Assert.False(ConfiguratorAvailability.IsConfiguratorAvailable((ConnectionSettings?)null));
    }
}
