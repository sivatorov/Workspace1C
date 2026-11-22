using System.IO;
using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты запуска платформы по версии, закреплённой за базой в ibases.v8i (DefaultVersion):
/// неполная версия в карточке базы («8.3») не должна подменяться последней установленной
/// сборкой линии, если стартер закрепил за базой другую — иначе клиент перезапускается
/// в закреплённую версию и теряет параметры запуска (/WSN, /WSP).
/// Все данные вымышленные.
/// </summary>
public sealed class OneCLauncherPinnedVersionTests
{
    private const string BaseId = "1b2c3d4e-0000-0000-0000-000000000001";
    private const string OtherId = "1b2c3d4e-0000-0000-0000-000000000002";
    private const string Pinned = "8.3.27.2214";
    private const string Newest = "8.3.27.2325";

    [Fact]
    public void Parse_ReadsDefaultVersionAndVersion()
    {
        var filePath = WriteIbases($"""
            [Example base]
            ID={BaseId}
            Connect=WS="https://example.test/base";Usr="webuser";Pwd="secret";
            Version={Newest}
            DefaultVersion={Pinned}

            """);

        try
        {
            var entry = Assert.Single(IbaseEntry.Parse(filePath));

            Assert.Equal(Pinned, entry.DefaultVersion);
            Assert.Equal(Newest, entry.Version);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FindPinnedVersion_UsesDefaultVersionOfMatchingId()
    {
        var filePath = WriteIbases($"""
            [Other base]
            ID={OtherId}
            Connect=WS="https://example.test/other";
            DefaultVersion={Newest}

            [Example base]
            ID={BaseId}
            Connect=WS="https://example.test/base";Usr="webuser";Pwd="secret";
            Version={Newest}
            DefaultVersion={Pinned}

            """);

        try
        {
            Assert.Equal(Pinned, OneCLauncher.FindPinnedVersion(BaseId, filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FindPinnedVersion_FallsBackToVersionWhenDefaultVersionMissing()
    {
        // DefaultVersion записи неполный/пустой — берётся Version записи.
        var filePath = WriteIbases($"""
            [Example base]
            ID={BaseId}
            Connect=WS="https://example.test/base";
            Version={Pinned}
            DefaultVersion=8.3

            """);

        try
        {
            Assert.Equal(Pinned, OneCLauncher.FindPinnedVersion(BaseId, filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FindPinnedVersion_MissingFileOrEntry_ReturnsNull()
    {
        // Файла нет — молча текущее поведение.
        Assert.Null(OneCLauncher.FindPinnedVersion(BaseId, Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.v8i")));

        // Записи с нужным ID нет.
        var filePath = WriteIbases($"""
            [Other base]
            ID={OtherId}
            Connect=WS="https://example.test/other";
            DefaultVersion={Pinned}

            """);

        try
        {
            Assert.Null(OneCLauncher.FindPinnedVersion(BaseId, filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void PickEffectiveVersion_PartialRequestedAndPinnedInstalled_ReturnsPinned()
    {
        var effective = OneCLauncher.PickEffectiveVersion("8.3", Pinned, new[] { Newest, Pinned });

        Assert.Equal(Pinned, effective);
    }

    [Fact]
    public void PickEffectiveVersion_PartialRequestedAndPinnedNotInstalled_ReturnsRequested()
    {
        // Закреплённой версии на машине нет — прежний поиск последней установленной.
        var effective = OneCLauncher.PickEffectiveVersion("8.3", Pinned, new[] { Newest });

        Assert.Equal("8.3", effective);
    }

    [Fact]
    public void PickEffectiveVersion_ExactRequested_IgnoresPinned()
    {
        // Точная версия в карточке — решение пользователя, закрепление не применяется.
        var effective = OneCLauncher.PickEffectiveVersion(Newest, Pinned, new[] { Newest, Pinned });

        Assert.Equal(Newest, effective);
    }

    [Fact]
    public void PickEffectiveVersion_PinnedOfAnotherLine_ReturnsRequested()
    {
        // Закреплена 8.3, а в карточке 8.5 — чужую линию не подставляем.
        var effective = OneCLauncher.PickEffectiveVersion("8.5", Pinned, new[] { Newest, Pinned });

        Assert.Equal("8.5", effective);
    }

    [Fact]
    public void PickEffectiveVersion_NoPinned_ReturnsRequested()
    {
        var effective = OneCLauncher.PickEffectiveVersion("8.3", null, new[] { Newest });

        Assert.Equal("8.3", effective);
    }

    [Fact]
    public void Export_KeepsDefaultVersionLine()
    {
        // Экспорт пишет Version из PlatformVersion базы, но закрепление (DefaultVersion)
        // в файле не должно теряться или подменяться.
        var filePath = WriteIbases($"""
            [Example base]
            ID={BaseId}
            Enable=1
            Connect=WS="https://example.test/base";Usr="webuser";Pwd="secret";
            App=Auto
            Version={Newest}
            DefaultVersion={Pinned}

            """);

        try
        {
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = BaseId,
                    Name = "Example base",
                    PlatformVersion = Newest,
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.WebServer,
                        WebUrl = "https://example.test/base",
                        User = "webuser",
                        Password = "secret"
                    }
                }
            };

            IbasesV8iExporter.AddInfobasesToFile(filePath, infobases, new List<Group>());

            var text = File.ReadAllText(filePath, IbaseEntry.DetectEncoding(filePath));
            Assert.Contains($"DefaultVersion={Pinned}", text);
            Assert.Contains($"Version={Newest}", text);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string WriteIbases(string content)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-pin-{Guid.NewGuid():N}.v8i");
        File.WriteAllText(filePath, content, Encoding.Default);
        return filePath;
    }
}
