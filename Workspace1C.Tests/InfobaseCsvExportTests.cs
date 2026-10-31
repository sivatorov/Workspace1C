using System;
using System.Globalization;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты CSV-экспорта списка баз (<see cref="InfobaseCsvExportService"/>, v100113, п.3.1):
/// набор колонок по плану (имя, группа, тип, строка подключения без пароля, версия
/// платформы, размер ИБ, дата последней копии), пустые значения и экранирование
/// запятых/кавычек при записи файла (<see cref="CsvExporter"/>).
/// </summary>
public sealed class InfobaseCsvExportTests
{
    private static Infobase MakeServerInfobase()
    {
        // Имя содержит разделитель CSV и кавычку — проверка экранирования.
        var ib = new Infobase { Name = "База; «тест»" };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = "srv01";
        ib.Connection.DatabaseName = "db01";
        ib.PlatformVersion = "8.3.27.1644";
        ib.Group = "Группа 1; филиал";
        ib.LastBackupUtc = new DateTime(2026, 10, 31, 9, 30, 0, DateTimeKind.Utc);
        ib.ManualSizeBytes = 123456789;
        return ib;
    }

    [Fact]
    public void BuildRows_Header_ContainsPlannedColumns()
    {
        var rows = InfobaseCsvExportService.BuildRows(Array.Empty<Infobase>());

        var header = rows[0];
        // Заголовки строятся теми же ключами локализации, что использует сервис.
        Assert.Equal(Workspace1C.Localization.LocalizationManager.T("ExportCsv.ColName"), header[0]);
        Assert.Equal(Workspace1C.Localization.LocalizationManager.T("ExportCsv.ColPlatform"), header[4]);
        Assert.Equal(Workspace1C.Localization.LocalizationManager.T("ExportCsv.ColSize"), header[5]);
        Assert.Equal(Workspace1C.Localization.LocalizationManager.T("ExportCsv.ColLastCopy"), header[6]);
        Assert.Equal(11, header.Count);
    }

    [Fact]
    public void BuildRows_ServerInfobase_FillsValues_WithoutPassword()
    {
        var rows = InfobaseCsvExportService.BuildRows(new[] { MakeServerInfobase() });
        Assert.Equal(2, rows.Count);

        var row = rows[1];
        Assert.Equal("База; «тест»", row[0]);
        Assert.Equal("Группа 1; филиал", row[1]);
        // Строка подключения — Srvr/Ref без Usr/Pwd.
        Assert.Contains("Srvr=\"srv01\"", row[3]);
        Assert.Contains("Ref=\"db01\"", row[3]);
        Assert.DoesNotContain("Pwd", row[3]);
        Assert.DoesNotContain("Usr", row[3]);
        Assert.Equal("8.3.27.1644", row[4]);
        Assert.Equal(Infobase.FormatSize(123456789), row[5]);
        Assert.NotEqual(string.Empty, row[5]);
        // Дата последней копии заполнена (локальное время от 2026-10-31 09:30 UTC).
        Assert.False(string.IsNullOrWhiteSpace(row[6]));
        Assert.Contains("2026-10-31", row[6]);
    }

    [Fact]
    public void BuildRows_EmptyValues_ProduceEmptyCells()
    {
        var ib = new Infobase();
        var rows = InfobaseCsvExportService.BuildRows(new[] { ib });
        var row = rows[1];

        Assert.Equal(string.Empty, row[1]); // группа
        Assert.Equal(string.Empty, row[4]); // версия платформы
        Assert.Equal(string.Empty, row[5]); // размер
        Assert.Equal(string.Empty, row[6]); // дата последней копии
    }

    [Fact]
    public void BuildRows_FileInfobase_ConnectionIsFilePath()
    {
        var ib = new Infobase { Name = "Файловая" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = @"C:\Bases\doc";

        var rows = InfobaseCsvExportService.BuildRows(new[] { ib });
        Assert.Equal("File=\"C:\\Bases\\doc\"", rows[1][3]);
    }

    [Fact]
    public void WriteFile_EscapesSeparatorAndQuotes_Rfc4180()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w1c_csv_{Guid.NewGuid():N}.csv");
        try
        {
            InfobaseCsvExportService.WriteFile(path, new[] { MakeServerInfobase() });

            // Файл в UTF-8 с BOM (иначе Excel не распознаёт кириллицу).
            var raw = File.ReadAllBytes(path);
            Assert.True(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF,
                "CSV-файл должен начинаться с UTF-8 BOM.");

            var text = File.ReadAllText(path);
            // Значения с разделителем «;» и кавычками взяты в кавычки (RFC 4180) —
            // иначе Excel разнёс бы «База; «тест»» по двум колонкам.
            Assert.Contains("\"База; «тест»\"", text, StringComparison.Ordinal);
            Assert.Contains("\"Группа 1; филиал\"", text, StringComparison.Ordinal);
            // Заголовок — первая строка файла (после BOM).
            Assert.StartsWith(LocalizationHeader(), text.TrimStart(), StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(path); } catch { /* не критично */ }
        }
    }

    private static string LocalizationHeader() =>
        Workspace1C.Localization.LocalizationManager.T("ExportCsv.ColName");
}
