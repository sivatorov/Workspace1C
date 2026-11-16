namespace Workspace1C.Models;

/// <summary>Тип узла в дереве выбора платформы.</summary>
public enum PlatformNodeKind
{
    /// <summary>Линия платформы, например «8.3».</summary>
    Line,
    /// <summary>Группа сборок, например «8.3.27».</summary>
    BuildGroup,
    /// <summary>Конкретная сборка 64-бит.</summary>
    LeafX64,
    /// <summary>Конкретная сборка 32-бит.</summary>
    LeafX32,
    /// <summary>Сборка без явной разрядности.</summary>
    Leaf
}

/// <summary>
/// Узел дерева платформ: линия (8.3) → группа сборок (8.3.27) → полная версия «8.3.27.2214 (x64)».
/// </summary>
public class PlatformVersionGroup
{
    /// <summary>Заголовок узла (линия, группа сборок или Display сборки).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Путь к папке версии (только у листьев).</summary>
    public string? Path { get; set; }

    /// <summary>Строка варианта для выбора, например «8.3.27.1688 (64)» (только у листьев).</summary>
    public string? Variant { get; set; }

    /// <summary>Тип узла — для иконки в UI.</summary>
    public PlatformNodeKind Kind { get; set; } = PlatformNodeKind.Line;

    /// <summary>Вложенные узлы (для групп).</summary>
    public List<PlatformVersionGroup> Children { get; set; } = new();

    /// <summary>Листья (сборки) — для обратной совместимости с шаблонами, где ItemsSource=Versions.</summary>
    public List<PlatformVersionInfo> Versions { get; set; } = new();

    public bool IsLeaf => !string.IsNullOrEmpty(Variant);

    /// <summary>
    /// Синхронизация с TreeViewItem.IsSelected (стиль ModernTreeViewItem биндит IsSelected).
    /// У групп обычно false; у выбранного листа — true.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Признак того, что этот узел соответствует текущей версии базы — выделяется жирным.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// True — лист является тонким клиентом (в bin только 1cv8c, полного 1cv8 нет):
    /// в дереве выбора помечается суффиксом и подсветкой (issue #33).
    /// </summary>
    public bool IsThinClient { get; set; }

    /// <summary>
    /// Статус узла, показываемый справа от названия (issue #135): для диалога выбора
    /// версии внешней консоли администрирования серверов — найдена ли оснастка
    /// («оснастка найдена: 1CV8Servers64.msc» / «оснастка не найдена»).
    /// Пустая строка — статус не показывается.
    /// </summary>
    public string StatusHint { get; set; } = string.Empty;

    /// <summary>
    /// Полное отображаемое имя для списков: «8.3.27.2214 (x64)» или с локализованным
    /// суффиксом «(тонкий клиент)» для листьев-тонких клиентов (issue #33). Используется
    /// во всех списках платформ (диалог выбора версии и настройки, WPF и Avalonia)
    /// через <see cref="Services.PlatformDisplayFormatter"/>, чтобы суффикс и оранжевая
    /// подсветка гарантированно попадали в UI.
    /// </summary>
    public string DisplayName => Services.PlatformDisplayFormatter.Format(this);

    public override string ToString() => Name;
}
