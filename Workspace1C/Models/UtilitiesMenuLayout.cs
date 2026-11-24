using System.Text.Json.Serialization;

namespace Workspace1C.Models;

/// <summary>
/// Режим отображения группы меню «Утилиты» (issue #146): только иконка (компактно,
/// с подсказкой) или иконка вместе с подписью. Настройка задаётся в диалоге правки
/// группы справа от выбора «Иконка» и хранится в JSON компоновки.
/// </summary>
public enum UtilitiesMenuGroupDisplay
{
    /// <summary>Только иконка с подсказкой.</summary>
    Icon = 0,

    /// <summary>Иконка и подпись группы (значение по умолчанию).</summary>
    IconWithLabel = 1
}

/// <summary>
/// Компоновка меню «Утилиты» (issue #374): пользовательская структура — порядок,
/// видимость, группировка и иконки пунктов. Хранится в отдельном читаемом JSON-файле
/// <c>utilities_menu_layout.json</c> рядом с настройками (см.
/// <see cref="Workspace1C.Services.UtilitiesMenuLayoutStore"/>).
/// </summary>
public sealed class UtilitiesMenuLayout
{
    /// <summary>Версия схемы файла компоновки (для будущих миграций).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Корневые узлы меню в порядке отображения.</summary>
    public List<UtilitiesMenuNode> Root { get; set; } = new();
}

/// <summary>
/// Узел дерева компоновки: либо ссылка на пункт каталога (<c>Kind == "item"</c>),
/// либо группа-подменю (<c>Kind == "group"</c>). Единый класс вместо иерархии — так
/// JSON-файл проще править вручную и не нужен полиморфный разбор с дискриминатором.
/// </summary>
public sealed class UtilitiesMenuNode
{
    /// <summary>Тип узла: "item" — пункт каталога, "group" — подменю.</summary>
    public string Kind { get; set; } = "item";

    /// <summary>Идентификатор пункта каталога (для <c>Kind == "item"</c>).</summary>
    public string? ItemId { get; set; }

    /// <summary>Идентификатор стандартной группы (для <c>Kind == "group"</c>; null — пользовательская).</summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Стабильный идентификатор пользовательской группы (issue #146): по нему кнопка
    /// верхней панели открывает именно эту группу. Генерируется при создании группы
    /// верхнего уровня и сохраняется в JSON, поэтому не зависит от заголовка.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// True — узел находится в корне компоновки (issue #146) и отображается на верхней
    /// панели: группа — кнопкой раздела (иконка/иконка с подписью), пункт — отдельной
    /// кнопкой только с иконкой, разделитель — вертикальной чертой. Признак действителен
    /// для любого вида узла корня (group/item/separator); у вложенных узлов — false.
    /// Корневые узлы с этим признаком миграция НЕ переносит в группу «Утилиты».
    /// </summary>
    public bool IsTopLevel { get; set; }

    /// <summary>Пользовательский заголовок (переопределяет локализованный).</summary>
    public string? CustomTitle { get; set; }

    /// <summary>Ключ иконки из программы (PackIcon Kind для WPF / Icon* для Avalonia).</summary>
    public string? IconKey { get; set; }

    /// <summary>Цвет иконки (#RRGGBB).</summary>
    public string? IconColor { get; set; }

    /// <summary>Путь к внешнему файлу иконки (назначается «извне»).</summary>
    public string? ExternalIconPath { get; set; }

    /// <summary>
    /// Режим отображения группы (issue #146): «С надписью» (по умолчанию) или
    /// «Иконка». В JSON пишется строкой (<c>"Icon"</c>/<c>"IconWithLabel"</c>),
    /// чтобы файл оставался читаемым при ручной правке; отсутствующее свойство
    /// в старых файлах даёт значение по умолчанию — с подписью.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UtilitiesMenuGroupDisplay DisplayMode { get; set; } = UtilitiesMenuGroupDisplay.IconWithLabel;

    /// <summary>Видимость узла (скрытые пункты не показываются в меню).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Рисовать разделитель перед узлом.</summary>
    public bool SeparatorBefore { get; set; }

    /// <summary>Дочерние узлы (для <c>Kind == "group"</c>).</summary>
    public List<UtilitiesMenuNode> Children { get; set; } = new();
}