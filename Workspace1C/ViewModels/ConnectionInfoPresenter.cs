using System.Collections.Generic;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Поле блока «Информация о подключении» (issue #138, 1.0.1.84). Набор строк
/// зависит от вида информационной базы, поэтому заголовки и значения строятся
/// не разметкой, а этим перечислением.
/// </summary>
public enum ConnectionInfoField
{
    Type,
    Directory,
    Address,
    Server,
    DatabaseName,
    ConnectionString,
    Platform,
    LaunchMode,
    Client,
    Bitness,
    Parameters,
    LastLaunch,
    CacheSize,
    Configuration
}

/// <summary>
/// Готовая строка блока «Информация о подключении» (issue #138, 1.0.1.84):
/// ключ и текст подписи, значение, признак копирования по клику и признак
/// «это каталог файловой базы» (для кнопки «Открыть каталог»).
/// </summary>
public sealed record ConnectionInfoFieldInfo(
    ConnectionInfoField Field,
    string LabelKey,
    string Label,
    string Value,
    bool Copyable,
    bool IsFileDirectory);

/// <summary>
/// Чистая логика состава блока «Информация о подключении» (issue #138, 1.0.1.84).
/// Для файловых и веб-баз не выводит «Сервер / путь», «Сервер/База» и строку
/// подключения — раньше они дублировали один и тот же адрес и раздували блок.
/// Пустые значения не добавляются (кроме «Тип»).
/// </summary>
public static class ConnectionInfoPresenter
{
    /// <summary>
    /// Строит строки блока для выбранной базы. Для <c>null</c> возвращает пустой список.
    /// </summary>
    public static IReadOnlyList<ConnectionInfoFieldInfo> Build(Infobase? infobase)
    {
        var rows = new List<ConnectionInfoFieldInfo>();
        if (infobase is null)
            return rows;

        // «Тип» выводится всегда — это единственная строка без проверки на пустоту.
        Add(rows, ConnectionInfoField.Type, "Main.Type", infobase.ConnectionTypeDisplay,
            copyable: false, isFileDirectory: false, omitEmpty: false);

        var connection = infobase.Connection;
        switch (connection.Type)
        {
            case ConnectionType.File:
                // Для файловой базы достаточно одного «Каталога»: путь к базе
                // больше не повторяется в «Сервер / путь» и строке подключения.
                Add(rows, ConnectionInfoField.Directory, "Main.ConnectionInfo.Directory",
                    connection.FilePath, copyable: true, isFileDirectory: true);
                break;

            case ConnectionType.WebServer:
                // Адрес публикации; при пустом WebUrl показываем имя базы (как в колонке).
                var address = string.IsNullOrWhiteSpace(connection.WebUrl)
                    ? connection.DatabaseName
                    : connection.WebUrl;
                Add(rows, ConnectionInfoField.Address, "Main.ConnectionInfo.Address",
                    address, copyable: true, isFileDirectory: false);
                break;

            default:
                // Клиент-серверная база: сервер, имя базы и строка подключения.
                Add(rows, ConnectionInfoField.Server, "Main.ConnectionInfo.Server",
                    connection.Server, copyable: true, isFileDirectory: false);
                Add(rows, ConnectionInfoField.DatabaseName, "Main.ConnectionInfo.DatabaseName",
                    connection.DatabaseName, copyable: true, isFileDirectory: false);
                Add(rows, ConnectionInfoField.ConnectionString, "Main.ConnectionString",
                    infobase.ConnectionStringDisplay, copyable: true, isFileDirectory: false);
                break;
        }

        Add(rows, ConnectionInfoField.Platform, "Main.Platform", infobase.PlatformVersion);
        Add(rows, ConnectionInfoField.LaunchMode, "Main.LaunchMode", infobase.ParsedLaunchMode);
        Add(rows, ConnectionInfoField.Client, "Main.Client", infobase.ClientTypeDisplay);
        Add(rows, ConnectionInfoField.Bitness, "Main.Bitness", infobase.ArchitectureDisplay);
        Add(rows, ConnectionInfoField.Parameters, "Main.Parameters", infobase.LaunchParameters);
        Add(rows, ConnectionInfoField.LastLaunch, "Main.LastLaunch", infobase.LastLaunchDisplay);
        Add(rows, ConnectionInfoField.CacheSize, "Main.CacheSize", infobase.CacheSizeDisplay);
        Add(rows, ConnectionInfoField.Configuration, "Column.Configuration", infobase.ConfigurationDisplay);

        return rows;
    }

    /// <summary>
    /// Можно ли открыть каталог базы из панели (issue #138, 1.0.1.84): только для
    /// файловой базы с непустым путём.
    /// </summary>
    public static bool CanOpenFolder(Infobase? infobase) =>
        infobase is { Connection.Type: ConnectionType.File } &&
        !string.IsNullOrWhiteSpace(infobase.Connection.FilePath);

    private static void Add(
        ICollection<ConnectionInfoFieldInfo> rows,
        ConnectionInfoField field,
        string labelKey,
        string? value,
        bool copyable = false,
        bool isFileDirectory = false,
        bool omitEmpty = true)
    {
        if (omitEmpty && string.IsNullOrWhiteSpace(value))
            return;

        rows.Add(new ConnectionInfoFieldInfo(
            field,
            labelKey,
            LocalizationManager.T(labelKey),
            value ?? string.Empty,
            copyable,
            isFileDirectory));
    }
}
