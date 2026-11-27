using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Доступность Конфигуратора для информационной базы (issue #180).
///
/// В стандартном стартере 1С для базы, опубликованной на веб-сервере
/// (<see cref="ConnectionType.WebServer"/>), Конфигуратор недоступен.
/// Для файловых (<see cref="ConnectionType.File"/>) и клиент-серверных
/// (<see cref="ConnectionType.ClientServer"/>) баз запуск Конфигуратора
/// остаётся доступным, как и раньше.
/// </summary>
public static class ConfiguratorAvailability
{
    /// <summary>
    /// Возвращает true, если для базы доступен запуск Конфигуратора.
    /// null (база не выбрана) считается недоступным.
    /// </summary>
    public static bool IsConfiguratorAvailable(Infobase? infobase)
        => IsConfiguratorAvailable(infobase?.Connection);

    /// <summary>
    /// Возвращает true, если для настроек подключения доступен запуск Конфигуратора.
    /// null считается недоступным.
    /// </summary>
    public static bool IsConfiguratorAvailable(ConnectionSettings? connection)
        => connection is not null && connection.Type != ConnectionType.WebServer;
}
