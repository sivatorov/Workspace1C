using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #138 (1.0.1.84): блок «Информация о подключении» — заголовки по типу базы,
/// копирование значений по клику и открытие каталога файловой базы из панели.
/// Проверяем чистую логику состава строк, разметку WPF и оболочку Avalonia,
/// а также наличие ключей локализации.
/// </summary>
public sealed class Issue138ConnectionInfoPanelTests
{
    private static readonly string[] ConnectionInfoKeys =
    {
        "Main.ConnectionInfo.Directory",
        "Main.ConnectionInfo.Address",
        "Main.ConnectionInfo.Server",
        "Main.ConnectionInfo.DatabaseName",
        "Main.ConnectionInfo.CopyTooltip",
        "Main.ConnectionInfo.OpenFolder",
        "Main.ConnectionInfo.Copied"
    };

    // ===================== Чистая логика =====================

    [Fact]
    public void FileBase_HasDirectoryRow_AndNoDuplicatePathRows()
    {
        const string path = @"C:\bases\demo";
        var infobase = CreateFileBase(path);

        var rows = ConnectionInfoPresenter.Build(infobase);

        // Путь выводится ровно одной строкой — «Каталог».
        var pathRows = rows.Where(r => r.Value.Contains(path, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(pathRows);
        Assert.Equal(ConnectionInfoField.Directory, pathRows[0].Field);

        // Дублирующих строк из старой фиксированной сетки больше нет.
        Assert.DoesNotContain(rows, r => r.LabelKey is "Main.ServerPath" or "Column.ServerBase" or "Main.ConnectionString");
    }

    [Fact]
    public void FileBase_DirectoryRow_IsCopyableAndOpenFolder()
    {
        const string path = @"C:\bases\demo";
        var infobase = CreateFileBase(path);

        var directory = rows(infobase).Single(r => r.Field == ConnectionInfoField.Directory);

        Assert.Equal("Main.ConnectionInfo.Directory", directory.LabelKey);
        Assert.Equal(path, directory.Value);
        Assert.True(directory.Copyable);
        Assert.True(directory.IsFileDirectory);
        Assert.True(ConnectionInfoPresenter.CanOpenFolder(infobase));
    }

    [Fact]
    public void WebBase_HasAddressRow()
    {
        var infobase = new Infobase
        {
            Name = "Веб-база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "http://host/name",
                DatabaseName = "ignored"
            }
        };

        var rows = ConnectionInfoPresenter.Build(infobase);
        var address = rows.Single(r => r.Field == ConnectionInfoField.Address);

        Assert.Equal("Main.ConnectionInfo.Address", address.LabelKey);
        Assert.Equal("http://host/name", address.Value);
        Assert.True(address.Copyable);
        Assert.DoesNotContain(rows, r => r.Field is ConnectionInfoField.Directory
            or ConnectionInfoField.Server
            or ConnectionInfoField.ConnectionString);
    }

    [Fact]
    public void WebBase_AddressFallsBackToDatabaseName()
    {
        var infobase = new Infobase
        {
            Name = "Веб-база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "   ",
                DatabaseName = "web"
            }
        };

        var address = rows(infobase).Single(r => r.Field == ConnectionInfoField.Address);

        Assert.Equal("web", address.Value);
        Assert.True(address.Copyable);
    }

    [Fact]
    public void ServerBase_HasServerAndDatabaseRows()
    {
        var infobase = CreateServerBase("srv-1c", "Accounting");

        var rows = ConnectionInfoPresenter.Build(infobase);
        var server = rows.Single(r => r.Field == ConnectionInfoField.Server);
        var database = rows.Single(r => r.Field == ConnectionInfoField.DatabaseName);

        Assert.Equal("Main.ConnectionInfo.Server", server.LabelKey);
        Assert.Equal("srv-1c", server.Value);
        Assert.Equal("Main.ConnectionInfo.DatabaseName", database.LabelKey);
        Assert.Equal("Accounting", database.Value);
        Assert.DoesNotContain(rows, r => r.Field is ConnectionInfoField.Directory or ConnectionInfoField.Address);
    }

    [Fact]
    public void ServerBase_ConnectionStringRow_IsCopyable()
    {
        var infobase = CreateServerBase("srv-1c", "Accounting");

        var connectionString = rows(infobase).Single(r => r.Field == ConnectionInfoField.ConnectionString);

        Assert.Equal("Main.ConnectionString", connectionString.LabelKey);
        Assert.Equal(infobase.ConnectionStringDisplay, connectionString.Value);
        Assert.True(connectionString.Copyable);
    }

    [Fact]
    public void EmptyValues_AreOmitted()
    {
        // Серверная база без сервера и имени базы: этих строк быть не должно,
        // но «Тип» остаётся всегда.
        var server = CreateServerBase(string.Empty, string.Empty);
        var serverRows = rows(server);
        Assert.DoesNotContain(serverRows, r => r.Field == ConnectionInfoField.Server);
        Assert.DoesNotContain(serverRows, r => r.Field == ConnectionInfoField.DatabaseName);
        Assert.Contains(serverRows, r => r.Field == ConnectionInfoField.Type);

        // Файловая база без пути: «Каталог» не создаётся, открывать нечего.
        var file = CreateFileBase(string.Empty);
        Assert.DoesNotContain(rows(file), r => r.Field == ConnectionInfoField.Directory);
        Assert.False(ConnectionInfoPresenter.CanOpenFolder(file));

        // Пустой список — для отсутствующей базы.
        Assert.Empty(ConnectionInfoPresenter.Build(null));
        Assert.False(ConnectionInfoPresenter.CanOpenFolder(null));
    }

    // ===================== Разметка WPF =====================

    [Fact]
    public void Wpf_Panel_UsesItemsControlAndCopyHandler()
    {
        var xaml = File.ReadAllText(FindSourceFile("Workspace1C", "Views", "MainWindow.xaml"));

        Assert.Contains("ItemsSource=\"{Binding ConnectionInfoFields}\"", xaml);
        Assert.Contains("OnConnectionInfoValueClick", xaml);
        Assert.Contains("OpenInfobaseFolderCommand", xaml);
        Assert.Contains("ShowOpenFolderLink", xaml);
    }

    [Fact]
    public void Wpf_ConnectionInfoValue_UsesPrimaryThemeBrush()
    {
        // Issue #141/#138 (1.0.1.95): значение строки — основная кисть темы,
        // подпись — вторичная. Раньше у TextBlock значения не было Foreground,
        // и в тёмной теме он наследовал чёрный (значения «сливались» с фоном).
        var xaml = File.ReadAllText(FindSourceFile("Workspace1C", "Views", "MainWindow.xaml"));
        var panel = ExtractBetween(
            xaml,
            "<ItemsControl ItemsSource=\"{Binding ConnectionInfoFields}\">",
            "</ItemsControl>");

        Assert.Contains("Text=\"{Binding Value}\"", panel);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimaryBrush}\"", panel);
        Assert.Contains("Foreground=\"{DynamicResource TextSecondaryBrush}\"", panel);

        // Кликабельность копируемых значений сохранена.
        Assert.Contains("Value=\"Hand\"", panel);
        Assert.Contains("Main.ConnectionInfo.CopyTooltip", panel);
        Assert.Contains("OnConnectionInfoValueClick", panel);

        AssertNoHardcodedBlack(panel);
    }

    // ===================== Оболочка Avalonia =====================

    [Fact]
    public void Avalonia_Panel_UsesConnectionInfoFieldsAndClipboard()
    {
        var code = File.ReadAllText(FindSourceFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        Assert.Contains("ConnectionInfoFields", code);
        Assert.Contains("Clipboard", code);
        Assert.Contains("SetTextAsync", code);
        Assert.Contains("OpenInfobaseFolderCommand", code);
        Assert.Contains("ShowOpenFolderLink", code);
    }

    [Fact]
    public void Avalonia_ConnectionInfoValue_UsesPrimaryThemeBrush()
    {
        // Issue #141/#138 (1.0.1.95): значение строки привязано к основной кисти
        // темы через ThemeBrushes.Bind (как desc панели), подпись — вторичная.
        var code = File.ReadAllText(FindSourceFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Маркер конца — закрывающая скобка метода на отступе 8 пробелов;
        // без завершающего \n, чтобы не зависеть от CRLF/LF в исходнике.
        var method = ExtractBetween(
            code,
            "private Control BuildConnectionInfoRow(",
            "\n        }");

        Assert.Contains("Text = row.Value", method);
        Assert.Contains("TextBlock.ForegroundProperty, \"TextPrimaryBrush\"", method);
        Assert.Contains("TextBlock.ForegroundProperty, \"TextSecondaryColorBrush\"", method);

        // Кликабельность копируемых значений сохранена.
        Assert.Contains("StandardCursorType.Hand", method);
        Assert.Contains("Main.ConnectionInfo.CopyTooltip", method);
        Assert.Contains("CopyConnectionInfoValue", method);

        AssertNoHardcodedBlack(method);
    }

    // ===================== Локализация =====================

    [Fact]
    public void Localization_HasConnectionInfoKeys()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var path = FindSourceFile("Workspace1C", "Localization", "Languages", language + ".json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            // Ключи локализации лежат во вложенном объекте "strings".
            var strings = document.RootElement.GetProperty("strings");

            foreach (var key in ConnectionInfoKeys)
            {
                Assert.True(strings.TryGetProperty(key, out var value), $"{language}.json: нет ключа {key}");
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{language}.json: пустое значение {key}");
            }
        }
    }

    // ===================== Вспомогательные методы =====================

    private static IReadOnlyList<ConnectionInfoFieldInfo> rows(Infobase infobase) =>
        ConnectionInfoPresenter.Build(infobase);

    /// <summary>Вырезает фрагмент между маркерами (сами маркеры не включаются).</summary>
    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"не найден маркер начала: {startMarker}");
        start += startMarker.Length;

        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"не найден маркер конца: {endMarker}");
        return text[start..end];
    }

    /// <summary>
    /// Жёсткий чёрный цвет в блоке недопустим: цвет должен приходить из кистей
    /// темы (DynamicResource в WPF / ThemeBrushes в Avalonia), иначе переключение
    /// темы на лету не работает (issue #141/#138).
    /// </summary>
    private static void AssertNoHardcodedBlack(string snippet)
    {
        Assert.False(snippet.Contains("#000000", StringComparison.OrdinalIgnoreCase),
            "Найден жёсткий цвет #000000 — нужна кисть темы.");
        Assert.False(snippet.Contains("Black", StringComparison.Ordinal),
            "Найден жёсткий цвет Black — нужна кисть темы.");
        Assert.False(snippet.Contains("SystemColors.ControlTextBrush", StringComparison.Ordinal),
            "Найден SystemColors.ControlTextBrush — нужна кисть темы.");
    }

    private static Infobase CreateFileBase(string path) => new()
    {
        Name = "Файловая",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = path
        }
    };

    private static Infobase CreateServerBase(string server, string database) => new()
    {
        Name = "Серверная",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = database
        }
    };

    private static string FindSourceFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }
}
