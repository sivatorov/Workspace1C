namespace Workspace1C.Models;

/// <summary>
/// Действие по двойному щелчку на информационной базе (функция №28 StartManager).
/// Канонические строковые значения хранятся на диске и не локализуются.
/// </summary>
public static class DoubleClickAction
{
    /// <summary>Запустить «1С:Предприятие».</summary>
    public const string Enterprise = "Enterprise";

    /// <summary>Запустить «Конфигуратор».</summary>
    public const string Configurator = "Configurator";

    /// <summary>Ничего не делать.</summary>
    public const string None = "None";

    /// <summary>
    /// Активировать окно уже запущенного экземпляра (issue #120 v1.0.1.55):
    /// одно окно — активируется напрямую, несколько — выбор окна
    /// («Предприятие/Конфигуратор + время запуска»); окон нет — ничего.
    /// </summary>
    public const string ActivateWindow = "ActivateWindow";

    /// <summary>Пустая строка — использовать глобальную настройку.</summary>
    public const string Default = "";

    /// <summary>
    /// Значение по умолчанию для глобальной настройки: запуск «1С:Предприятие».
    /// </summary>
    public const string GlobalDefault = Enterprise;

    /// <summary>
    /// Приводит произвольное значение к каноническому действию. Неизвестные/пустые
    /// значения возвращают <see cref="GlobalDefault"/> (запуск «1С:Предприятие»).
    /// </summary>
    public static string Normalize(string? value)
        => value switch
        {
            Enterprise or Configurator or None or ActivateWindow => value,
            _ => GlobalDefault
        };
}