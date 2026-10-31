using System;
using System.Collections.Generic;
using System.Globalization;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Экспорт списка баз в CSV (v100113, п.3.1): чистая сборка строк полей без I/O и UI —
/// юнит-тестируется отдельно (<c>InfobaseCsvExportTests</c>). Используется командой
/// «Утилиты → Экспорт списка баз в CSV» обеих платформ (WPF и Avalonia). Колонки:
/// имя, группа, тип (файловая/серверная), строка подключения (без пароля —
/// <see cref="Infobase.ConnectionStringDisplay"/>), версия платформы, размер ИБ,
/// дата последней копии (<see cref="Infobase.LastBackupUtc"/>), дата изменений файла ИБ,
/// теги, избранное, закрепление. Экранирование запятых/кавычек и запись файла —
/// в <see cref="CsvExporter"/> (UTF-8 с BOM, RFC 4180).
/// </summary>
public static class InfobaseCsvExportService
{
    /// <summary>Собирает строки CSV-документа: локализованный заголовок + по строке на базу.</summary>
    public static IReadOnlyList<IReadOnlyList<string?>> BuildRows(IEnumerable<Infobase> bases)
    {
        var rows = new List<IReadOnlyList<string?>>
        {
            new[]
            {
                LocalizationManager.T("ExportCsv.ColName"),
                LocalizationManager.T("ExportCsv.ColGroup"),
                LocalizationManager.T("ExportCsv.ColType"),
                LocalizationManager.T("ExportCsv.ColConnection"),
                LocalizationManager.T("ExportCsv.ColPlatform"),
                LocalizationManager.T("ExportCsv.ColSize"),
                LocalizationManager.T("ExportCsv.ColLastCopy"),
                LocalizationManager.T("ExportCsv.ColModified"),
                LocalizationManager.T("ExportCsv.ColTags"),
                LocalizationManager.T("ExportCsv.ColFavorite"),
                LocalizationManager.T("ExportCsv.ColPinned")
            }
        };

        foreach (var ib in bases)
        {
            rows.Add(new[]
            {
                ib.Name ?? string.Empty,
                ib.Group ?? string.Empty,
                ib.ConnectionTypeDisplay ?? string.Empty,
                // Строка подключения без логина/пароля (Connection.ToConnectionString).
                ib.ConnectionStringDisplay ?? string.Empty,
                ib.PlatformVersion ?? string.Empty,
                FormatSize(ib),
                FormatLastBackup(ib),
                FormatModified(ib),
                ib.Tags is null ? string.Empty : string.Join(", ", ib.Tags),
                ib.FavoriteHotkeyNumber >= 1 && ib.FavoriteHotkeyNumber <= 9
                    ? ib.FavoriteHotkeyNumber.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                ib.IsPinned ? LocalizationManager.T("ExportCsv.Yes") : LocalizationManager.T("ExportCsv.No")
            });
        }
        return rows;
    }

    /// <summary>Записывает список баз в CSV-файл (экранирование — в <see cref="CsvExporter"/>).</summary>
    public static void WriteFile(string path, IEnumerable<Infobase> bases)
        => CsvExporter.WriteFile(path, BuildRows(bases));

    /// <summary>Размер ИБ в удобочитаемом виде, если известен; иначе пусто.</summary>
    private static string FormatSize(Infobase ib)
    {
        if (ib.ManualSizeBytes.HasValue)
            return Infobase.FormatSize(ib.ManualSizeBytes.Value);
        return ib.FileSizeBytes.HasValue ? Infobase.FormatSize(ib.FileSizeBytes.Value) : string.Empty;
    }

    /// <summary>Дата последней копии (локальное время) или пусто.</summary>
    private static string FormatLastBackup(Infobase ib)
        => ib.LastBackupUtc.HasValue
            ? ib.LastBackupUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>Дата изменений файла ИБ (локальное время) или пусто.</summary>
    private static string FormatModified(Infobase ib)
        => ib.FileLastWriteTimeUtc.HasValue
            ? ib.FileLastWriteTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
}
