using System;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Источник, из которого определён тип СУБД базы (issue #106, v1.0.1.73).
/// Порядок приоритетов: сохранённый профиль базы → данные кластера rac →
/// общая настройка «Тип СУБД по умолчанию» → тип не определён.
/// </summary>
public enum DbmsKindSource
{
    /// <summary>Тип не определён ни одним источником.</summary>
    Unknown,

    /// <summary>Тип взят из сохранённого профиля резервного копирования базы.</summary>
    SavedProfile,

    /// <summary>Тип получен из кластера 1С (rac, <see cref="ClusterInfobaseCredentialsResolver"/>).</summary>
    Cluster,

    /// <summary>Тип взят из общей настройки «Тип СУБД по умолчанию».</summary>
    Setting
}

/// <summary>
/// Результат выбора типа СУБД: сам тип (null — не определён) и источник, из которого
/// он получен. Чистая структура — удобна для тестов и журнала.
/// </summary>
public readonly record struct DbmsKindResolution(DbmsKind? Kind, DbmsKindSource Source)
{
    /// <summary>Тип СУБД известен.</summary>
    public bool IsKnown => Kind is not null;
}

/// <summary>
/// Чистые функции работы с типом СУБД (issue #106, v1.0.1.73): разбор/запись общей
/// настройки «Тип СУБД по умолчанию», разбор значения rac и выбор типа по приоритету
/// источников. Раньше профиль резервных копий молча подставлял PostgreSQL
/// (<c>DbmsIndex == 0</c>), даже если база работала на MS SQL — теперь тип берётся
/// из сохранённого профиля, кластера или общей настройки, а при полной неизвестности
/// остаётся явная подсказка и пустой выбор.
/// </summary>
public static class DbmsDefaults
{
    /// <summary>Отображаемое/настроечное имя PostgreSQL.</summary>
    public const string PostgreSqlName = "PostgreSQL";

    /// <summary>Отображаемое имя Microsoft SQL Server.</summary>
    public const string MsSqlName = "Microsoft SQL Server";

    /// <summary>Каноническое значение общей настройки для PostgreSQL.</summary>
    public const string PostgreSqlSetting = "PostgreSQL";

    /// <summary>Каноническое значение общей настройки для MS SQL Server.</summary>
    public const string MsSqlSetting = "MsSql";

    /// <summary>
    /// Разбирает значение общей настройки <c>DefaultDbmsKind</c>. Поддерживаются
    /// канонические значения (<see cref="PostgreSqlSetting"/>/<see cref="MsSqlSetting"/>),
    /// отображаемые имена и значения rac («MSSQLServer»). Пусто/«не задан»/неизвестное
    /// значение — null (тип не подставляется).
    /// </summary>
    public static DbmsKind? ParseSetting(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or "notset" or "none" or "не задан" or "не задано" => null,
            "postgresql" or "postgres" or "postgre" or "pg" => DbmsKind.PostgreSql,
            "mssql" or "mssqlserver" or "ms" or "sqlserver" or "microsoft sql server"
                or "microsoftsqlserver" => DbmsKind.MsSql,
            _ => null
        };
    }

    /// <summary>
    /// Каноническое значение настройки для типа СУБД (пустая строка — «не задан»).
    /// </summary>
    public static string ToSetting(DbmsKind? kind) => kind switch
    {
        DbmsKind.PostgreSql => PostgreSqlSetting,
        DbmsKind.MsSql => MsSqlSetting,
        _ => string.Empty
    };

    /// <summary>
    /// Нормализует значение настройки к каноническому виду (обратная совместимость
    /// со старыми файлами: пустое/неизвестное значение остаётся «не задан»).
    /// </summary>
    public static string NormalizeSetting(string? value) => ToSetting(ParseSetting(value));

    /// <summary>Отображаемое имя типа СУБД (для подсказок); пусто — тип не задан.</summary>
    public static string DisplayName(DbmsKind? kind) => kind switch
    {
        DbmsKind.PostgreSql => PostgreSqlName,
        DbmsKind.MsSql => MsSqlName,
        _ => string.Empty
    };

    /// <summary>
    /// Разбирает значение типа СУБД из вывода rac («PostgreSQL», «MSSQLServer», «MS»).
    /// Делегирует <see cref="ClusterInfobaseCredentialsResolver.ParseDbms"/>, чтобы
    /// профиль и кластер понимали тип одинаково.
    /// </summary>
    public static DbmsKind? ParseRacDbms(string? value) =>
        ClusterInfobaseCredentialsResolver.ParseDbms(value);

    /// <summary>
    /// Выбирает тип СУБД по приоритету источников (issue #106, v1.0.1.73):
    /// <list type="number">
    /// <item>сохранённый профиль базы — даже если кластер сообщает другой тип
    /// (профиль настроен пользователем вручную и важнее автоматики);</item>
    /// <item>данные кластера rac;</item>
    /// <item>общая настройка «Тип СУБД по умолчанию»;</item>
    /// <item>ничего не известно — <see cref="DbmsKindSource.Unknown"/> и null
    /// (НЕ PostgreSQL: профиль покажет явную подсказку).</item>
    /// </list>
    /// </summary>
    public static DbmsKindResolution Resolve(
        DbmsKind? savedProfileKind, DbmsKind? clusterKind, string? settingValue)
    {
        if (savedProfileKind is { } saved)
            return new DbmsKindResolution(saved, DbmsKindSource.SavedProfile);
        if (clusterKind is { } cluster)
            return new DbmsKindResolution(cluster, DbmsKindSource.Cluster);
        var fromSetting = ParseSetting(settingValue);
        if (fromSetting is { } setting)
            return new DbmsKindResolution(setting, DbmsKindSource.Setting);
        return new DbmsKindResolution(null, DbmsKindSource.Unknown);
    }

    /// <summary>
    /// Индекс выпадающего списка «СУБД» профиля: 0 — PostgreSQL, 1 — MS SQL,
    /// −1 — тип не определён (пустой выбор с подсказкой).
    /// </summary>
    public static int ToIndex(DbmsKind? kind) => kind switch
    {
        DbmsKind.PostgreSql => 0,
        DbmsKind.MsSql => 1,
        _ => -1
    };
}
