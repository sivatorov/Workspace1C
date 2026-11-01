using System.Text.Json;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты формирования строки подключения для пункта контекстного меню
/// «Копировать строку подключения» (план v1.0.1.14 п.3.3): файловая база —
/// <c>File="путь"</c>, клиент-серверная — <c>Srvr="сервер";Ref="база"</c>
/// (стандартный формат 1С), без учётных данных (Usr/Pwd в копируемую строку
/// не попадают). Строка формируется в модели (<see cref="ConnectionSettings.ToConnectionString"/>/
/// <see cref="Infobase.ConnectionStringDisplay"/>) — тестируемо без UI.
/// </summary>
public sealed class CopyConnectionStringTests
{
    [Fact]
    public void FileBase_FilePathOnly()
    {
        var ib = new Infobase
        {
            Name = "Файловая",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"C:\bases\alfa"
            }
        };

        Assert.Equal(@"File=""C:\bases\alfa""", ib.ConnectionStringDisplay);
    }

    [Fact]
    public void ServerBase_SrvrRefFormat()
    {
        var ib = new Infobase
        {
            Name = "Бухгалтерия",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "Бухгалтерия"
            }
        };

        Assert.Equal(@"Srvr=""srv1c"";Ref=""Бухгалтерия""", ib.ConnectionStringDisplay);
    }

    [Fact]
    public void ServerBase_NonStandardPort_IncludedInSrvr()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                Port = 2541,
                DatabaseName = "ЗУП"
            }
        };

        Assert.Contains(@"Srvr=""srv1c:2541""", ib.ConnectionStringDisplay);
    }

    [Fact]
    public void ServerBase_WithCredentials_NoUsrPwdInCopiedString()
    {
        // Безопасный вариант: учётные данные НЕ включаются в копируемую строку.
        var ib = new Infobase
        {
            Name = "Секретная",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "База",
                User = "admin",
                Password = "p@ssw0rd"
            }
        };

        var text = ib.ConnectionStringDisplay;

        Assert.DoesNotContain("Usr=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pwd=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ssw0rd", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FileBase_WithCredentials_NoUsrPwdInCopiedString()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"D:\1c\База",
                User = "Админ",
                Password = "123"
            }
        };

        var text = ib.ConnectionStringDisplay;

        Assert.Equal(@"File=""D:\1c\База""", text);
    }

    [Fact]
    public void ServerBase_QuoteInName_IsEscapedByDoubling()
    {
        // Значение в кавычках экранируется удвоением (правило строки подключения 1С).
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv\"1c",
                DatabaseName = "База"
            }
        };

        Assert.Equal(@"Srvr=""srv""""1c"";Ref=""База""", ib.ConnectionStringDisplay);
    }

    [Fact]
    public void ConnectionPathDisplay_FileBase_PathInQuotesWithoutPrefix()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"C:\bases\beta"
            }
        };

        Assert.Equal(@"""C:\bases\beta""", ib.ConnectionPathDisplay);
    }

    [Fact]
    public void ConnectionPathDisplay_ServerBase_SameAsConnectionString()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv1c",
                DatabaseName = "База"
            }
        };

        Assert.Equal(ib.ConnectionStringDisplay, ib.ConnectionPathDisplay);
    }

    // ======================= Локализация пункта меню =======================

    /// <summary>Значение ключа из встроенного JSON-файла локализации.</summary>
    private static string? GetLocalizationValue(string languageFile, string key)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !(Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                 Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests"))))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "Workspace1C", "Localization", "Languages", languageFile);
        Assert.True(File.Exists(path), $"Файл локализации не найден: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        if (strings.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    [Fact]
    public void CopyConnectionString_LocalizedInRuAndEn()
    {
        var ru = GetLocalizationValue("ru.json", "Main.CopyConnectionString");
        var en = GetLocalizationValue("en.json", "Main.CopyConnectionString");

        Assert.Equal("Копировать строку подключения", ru);
        Assert.Equal("Copy connection string", en);
    }
}
