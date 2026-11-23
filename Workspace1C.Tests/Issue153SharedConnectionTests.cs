using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #153: одна и та же база заведена в списке несколько раз под разными
/// учётными записями (авторизация ОС и имя/пароль). Раньше процесс 1С привязывался
/// только к первой записи с таким подключением, поэтому у второй не появлялся
/// индикатор запуска, а двойной щелчок открывал новый экземпляр.
/// </summary>
public sealed class Issue153SharedConnectionTests
{
    private static Infobase ServerBase(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srvc-1c-doc",
            DatabaseName = "doc3"
        }
    };

    private static RunningOneCProcess Process(string user = "") =>
        new("1cv8.exe",
            @"""C:\Program Files\1cv8\8.3.27.2214\bin\1cv8.exe"" ENTERPRISE /S ""srvc-1c-doc\doc3"""
            + (string.IsNullOrEmpty(user) ? string.Empty : " /N " + user));

    [Fact]
    public void MatchProcesses_SameConnection_TwoEntries_BothReceiveProcess()
    {
        var osAuth = ServerBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = ServerBase("id-admin", "1С ДО 3.0 - РАБОЧАЯ БАЗА - АДМИН");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { osAuth, admin }, new[] { Process() });

        Assert.True(result.ContainsKey("id-os"));
        Assert.True(result.ContainsKey("id-admin"));
        Assert.Single(result["id-os"]);
        Assert.Single(result["id-admin"]);
    }

    [Fact]
    public void MatchProcesses_TwoProcesses_TwoEntries_AllAssignedToBoth()
    {
        var osAuth = ServerBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = ServerBase("id-admin", "1С ДО 3.0 - РАБОЧАЯ БАЗА - АДМИН");
        var processes = new[]
        {
            Process(),
            Process("Салихов Администратор")
        };

        var result = RunningInfobaseMatcher.MatchProcesses(new[] { osAuth, admin }, processes);

        Assert.Equal(2, result["id-os"].Count);
        Assert.Equal(2, result["id-admin"].Count);
    }

    [Fact]
    public void MatchProcesses_DifferentConnections_NotMixed()
    {
        var doc3 = ServerBase("id-doc3", "doc3");
        var other = new Infobase
        {
            Id = "id-other",
            Name = "other",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srvc-1c-doc",
                DatabaseName = "other"
            }
        };

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { doc3, other }, new[] { Process() });

        Assert.True(result.ContainsKey("id-doc3"));
        Assert.False(result.ContainsKey("id-other"));
    }

    [Fact]
    public void FindDirectOwners_ReturnsAllEntriesWithSameConnection()
    {
        var osAuth = ServerBase("id-os", "1С ДО 3.0 - РАБОЧАЯ БАЗА");
        var admin = ServerBase("id-admin", "1С ДО 3.0 - РАБОЧАЯ БАЗА - АДМИН");

        var owners = RunningInfobaseMatcher.FindDirectOwners(
            new[] { osAuth, admin }, Process().CommandLine);

        Assert.Equal(2, owners.Count);
        Assert.Contains(owners, ib => ib.Id == "id-os");
        Assert.Contains(owners, ib => ib.Id == "id-admin");
    }

    [Fact]
    public void MatchProcesses_DuplicateEntry_IsNotDuplicatedInResult()
    {
        // Одна и та же запись (тот же Id) в списке дважды не должна дать
        // два одинаковых совпадения в словаре.
        var first = ServerBase("id-same", "База");
        var second = ServerBase("id-same", "База (копия)");

        var result = RunningInfobaseMatcher.MatchProcesses(
            new[] { first, second }, new[] { Process() });

        Assert.Single(result["id-same"]);
    }
}
