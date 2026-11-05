using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Резолвер компоновки контекстного меню базы (issue #56): выбирает эффективную
/// компоновку по настройкам (правка включена/выключена), собирает «новинки
/// программы» и гарантирует наличие Essential-пунктов. Платформенно-нейтральная
/// логика, общая для построителей WPF и Avalonia.
/// </summary>
public static class ContextMenuResolver
{
    /// <summary>
    /// Возвращает компоновку для построения меню:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// </summary>
    public static ContextMenuLayout Resolve(AppSettings? settings, IContextMenuLayoutStore? store)
    {
        if (settings is null || !settings.ContextMenuEditingEnabled)
            return ContextMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? ContextMenuLayouts.Default();
    }

    /// <summary>
    /// Готовит компоновку к построению: гарантирует наличие Essential-пунктов
    /// (см. <see cref="EnsureEssentialItems"/>) и группы «Новинки программы»
    /// (см. <see cref="EnsureNewItemsGroup"/>). Вызывается построителями меню
    /// WPF и Avalonia после <see cref="Resolve"/>.
    /// </summary>
    public static ContextMenuLayout PrepareForMenu(ContextMenuLayout layout)
    {
        if (layout is null)
            return ContextMenuLayouts.Default();
        return EnsureNewItemsGroup(EnsureEssentialItems(layout)) ?? layout;
    }

    /// <summary>
    /// Гарантирует наличие в компоновке Essential-пунктов каталога
    /// («Пользовательские действия» и «Для выделенных (N)…» — от них зависят
    /// функция 7 и пакетные операции; issue #56). Если пункт отсутствует,
    /// он добавляется в конец корневого списка (видимость по умолчанию true);
    /// при переносе/перемещении пользователем пункт сохраняет своё местоположение.
    /// </summary>
    public static ContextMenuLayout? EnsureEssentialItems(ContextMenuLayout? layout)
    {
        if (layout is null)
            return layout;
        layout.Root ??= new List<ContextMenuNode>();
        var present = new HashSet<string>();
        CollectItemIds(layout.Root, present);
        foreach (var item in ContextMenuCatalog.All)
        {
            if (item.Essential && !present.Contains(item.Id))
                layout.Root.Add(new ContextMenuNode { Kind = "item", ItemId = item.Id });
        }
        return layout;
    }

    /// <summary>
    /// Собирает «новинки программы»: пункты каталога с <see cref="ContextMenuItem.IsNew"/>,
    /// отсутствующие в компоновке (по <c>ItemId</c>). Такие пункты автоматически попадают
    /// в подменю «Новинки программы» в конце меню, пока пользователь не разместит их
    /// в редакторе структуры.
    /// </summary>
    public static IReadOnlyList<ContextMenuItem> CollectNewItems(ContextMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout?.Root, present);
        var result = new List<ContextMenuItem>();
        foreach (var item in ContextMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Гарантирует наличие группы «Новинки программы» в компоновке (issue #4): если
    /// в каталоге есть пункты с признаком «новинка», отсутствующие в компоновке, они
    /// добавляются в группу <see cref="ContextMenuCatalog.GroupNewItems"/>. Группа
    /// создаётся в конце корня, если её нет в компоновке; если она уже есть — используется
    /// её текущее местоположение (пользователь может менять только его). В существующую
    /// группу добавляются только новинки, которых нет НИГДЕ в компоновке: пункт,
    /// перенесённый пользователем в свою папку, не дублируется (issue #65).
    /// <para>
    /// Видимость группы в рабочем меню определяется НАЛИЧИЕМ пунктов в самой группе
    /// (после добавления), а не галочкой в редакторе структуры и не размещением всех
    /// новинок каталога где-либо: непустая группа показывается безусловно, скрывается
    /// только группа без единого пункта (issue #65).
    /// </para>
    /// </summary>
    public static ContextMenuLayout? EnsureNewItemsGroup(ContextMenuLayout? layout)
    {
        if (layout is null)
            return null;

        var existing = FindGroup(layout.Root, ContextMenuCatalog.GroupNewItems);
        if (existing is not null)
        {
            // Группа уже есть (пользователь мог её переместить). Добавляем в неё только
            // неразмещённые НИГДЕ новинки каталога: пункт, перенесённый пользователем
            // в свою папку, НЕ дублируется в «Новинках» (issue #65).
            var toAdd = CollectNewItemsNotInGroup(layout);
            foreach (var item in toAdd)
                existing.Children.Add(new ContextMenuNode { Kind = "item", ItemId = item.Id });
            // Видимость группы определяется наличием пунктов В САМОЙ ГРУППЕ (issue #65):
            // даже если все новинки каталога уже размещены где-либо в компоновке, но
            // в группе остались пункты — группа показывается; пустая группа скрывается.
            existing.Visible = existing.Children.Count > 0;
            return layout;
        }

        // Группы нет — создаём в конце корня с разделителем перед ней, только если
        // есть неразмещённые новинки (как в меню).
        var newItems = CollectNewItems(layout);
        if (newItems.Count == 0)
            return layout;

        var group = new ContextMenuNode
        {
            Kind = "group",
            GroupId = ContextMenuCatalog.GroupNewItems,
            Visible = true,
            SeparatorBefore = true,
        };
        foreach (var item in newItems)
            group.Children.Add(new ContextMenuNode { Kind = "item", ItemId = item.Id });
        layout.Root.Add(group);
        return layout;
    }

    /// <summary>
    /// Собирает «новинки программы» для добавления в существующую группу: пункты
    /// каталога с <see cref="ContextMenuItem.IsNew"/>, отсутствующие ВО ВСЕЙ компоновке
    /// (включая детей самой группы). Пункт, размещённый пользователем где-либо (в т.ч.
    /// перенесённый из «Новинок» в свою папку), не возвращается и не дублируется.
    /// </summary>
    private static IReadOnlyList<ContextMenuItem> CollectNewItemsNotInGroup(ContextMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout.Root, present);
        var result = new List<ContextMenuItem>();
        foreach (var item in ContextMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>Ищет группу с указанным <c>GroupId</c> в дереве (рекурсивно, включая вложенные).</summary>
    private static ContextMenuNode? FindGroup(IEnumerable<ContextMenuNode>? nodes, string groupId)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal) &&
                string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            {
                return node;
            }
            var found = FindGroup(node.Children, groupId);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Собирает все ItemId узлов дерева (включая вложенные группы).</summary>
    public static void CollectItemIds(IEnumerable<ContextMenuNode>? nodes, HashSet<string> target)
    {
        if (nodes is null || target is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(node.ItemId))
            {
                target.Add(node.ItemId!);
            }
            CollectItemIds(node.Children, target);
        }
    }

    /// <summary>
    /// Фильтрует компоновку для контекстного меню ВЫБРАННОЙ ГРУППЫ (issue #105, запрос 7OH):
    /// удаляет пункты, неприменимые к группе (<see cref="ContextMenuCatalog.IsGroupApplicable"/>),
    /// скрывает опустевшие подменю и убирает «висячие»/двойные разделители. Компоновка
    /// НЕ мутируется: возвращается новый список; исходные узлы-пункты и разделители
    /// переиспользуются, узлы-группы копируются с отфильтрованными детьми.
    /// </summary>
    public static List<ContextMenuNode> FilterForGroup(IEnumerable<ContextMenuNode>? nodes)
    {
        var filtered = FilterNodesForGroup(nodes);
        TrimSeparators(filtered);
        return filtered;
    }

    /// <summary>Рекурсивная часть <see cref="FilterForGroup"/>: отбор применимых пунктов и непустых групп.</summary>
    private static List<ContextMenuNode> FilterNodesForGroup(IEnumerable<ContextMenuNode>? nodes)
    {
        var result = new List<ContextMenuNode>();
        if (nodes is null)
            return result;
        foreach (var node in nodes)
        {
            if (node is null || !node.Visible)
                continue;
            if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
            {
                result.Add(node);
                continue;
            }
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                // Дети чистятся через FilterForGroup (с удалением «висячих» разделителей)
                // ДО проверки на пустоту: группа только из разделителей — пустая (issue #105).
                var children = FilterForGroup(node.Children);
                if (children.Count == 0)
                    continue; // пустое подменю не показываем (issue #105)
                result.Add(new ContextMenuNode
                {
                    Kind = node.Kind,
                    ItemId = node.ItemId,
                    GroupId = node.GroupId,
                    CustomTitle = node.CustomTitle,
                    IconKey = node.IconKey,
                    IconColor = node.IconColor,
                    ExternalIconPath = node.ExternalIconPath,
                    Visible = node.Visible,
                    SeparatorBefore = node.SeparatorBefore,
                    Children = children
                });
                continue;
            }

            // Пункт каталога: для группы оставляем только применимые команды; неизвестные
            // (пользовательские) пункты тоже скрываем — для группы они не определены.
            var catalogItem = ContextMenuCatalog.Find(node.ItemId);
            if (catalogItem is null ||
                !ContextMenuCatalog.IsGroupApplicable(catalogItem.CommandKind))
                continue;
            result.Add(node);
        }
        return result;
    }

    /// <summary>Убирает разделители в начале/конце списка и подряд идущие, а также
    /// дубли флага <c>SeparatorBefore</c> рядом с узлами-разделителями.</summary>
    private static void TrimSeparators(List<ContextMenuNode> nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            if (!string.Equals(node.Kind, "separator", StringComparison.Ordinal))
                continue;
            var prevIsSeparator = i > 0 && IsSeparator(nodes[i - 1]);
            var atEdge = i == 0 || i == nodes.Count - 1;
            if (prevIsSeparator || atEdge)
                nodes.RemoveAt(i);
        }
        // После чистки подряд-разделителей: узел сразу после разделителя не должен
        // дополнительно рисовать SeparatorBefore (двойной разделитель).
        for (var i = 1; i < nodes.Count; i++)
        {
            if (IsSeparator(nodes[i - 1]))
                nodes[i].SeparatorBefore = false;
        }
        if (nodes.Count > 0)
            nodes[0].SeparatorBefore = false;
    }

    private static bool IsSeparator(ContextMenuNode node) =>
        string.Equals(node.Kind, "separator", StringComparison.Ordinal);
}