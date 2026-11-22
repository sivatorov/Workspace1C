using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services.Licensing;

namespace Workspace1C.Services;

/// <summary>
/// Резолвер компоновки меню «Утилиты» (issue #374): выбирает эффективную компоновку
/// по настройкам (правка включена/выключена) и собирает «новинки программы» — пункты
/// каталога, добавленные в новых версиях и отсутствующие в сохранённой компоновке.
/// Платформенно-нейтральная логика, общая для построителей WPF и Avalonia.
/// </summary>
public static class UtilitiesMenuResolver
{
    /// <summary>
    /// Возвращает компоновку для построения меню:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// </summary>
    public static UtilitiesMenuLayout Resolve(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        if (settings is null || !settings.UtilitiesMenuEditingEnabled)
            return UtilitiesMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? UtilitiesMenuLayouts.Default();
    }

    /// <summary>Готовит рабочее меню, не сохраняя служебную группу в компоновке пользователя.</summary>
    public static UtilitiesMenuLayout ResolveForDisplay(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        var layout = Resolve(settings, store);
        if (EditionService.IsPro && EditionService.HasProModules)
        {
            var items = UtilitiesMenuCatalog.All
                .Where(item => item.RequiredFeature is { } feature && !ProFeatures.IsInPublicCore(feature))
                .Select(item => new UtilitiesMenuNode { ItemId = item.Id })
                .ToList();

            if (items.Count > 0)
            {
                var itemIds = items.Select(item => item.ItemId!).ToHashSet(StringComparer.Ordinal);
                RemoveProItems(layout.Root, itemIds);

                var tools = layout.Root.FirstOrDefault(node =>
                    node.Kind == "group" && node.GroupId == UtilitiesMenuCatalog.GroupTools);
                if (tools is null)
                {
                    tools = new UtilitiesMenuNode { Kind = "group", GroupId = UtilitiesMenuCatalog.GroupTools };
                    layout.Root.Add(tools);
                }
                if (!tools.Visible)
                {
                    tools.Children.Clear();
                    tools.Visible = true;
                }
                tools.Children.Add(new UtilitiesMenuNode
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupPro,
                    Children = items
                });
            }
        }
        EnsureNewItemsGroup(layout);
        return layout;
    }

    private static void RemoveProItems(List<UtilitiesMenuNode> nodes, HashSet<string> itemIds)
    {
        nodes.RemoveAll(node => node.Kind == "item" && node.ItemId is { } id && itemIds.Contains(id));
        foreach (var node in nodes)
            RemoveProItems(node.Children, itemIds);
    }

    /// <summary>
    /// Собирает «новинки программы»: пункты каталога с <see cref="UtilitiesMenuItem.IsNew"/>,
    /// отсутствующие в компоновке (по <c>ItemId</c>). Такие пункты автоматически попадают
    /// в подменю «Новинки программы» в конце меню, пока пользователь не разместит их
    /// в редакторе структуры.
    /// </summary>
    public static IReadOnlyList<UtilitiesMenuItem> CollectNewItems(UtilitiesMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout?.Root, present);
        var result = new List<UtilitiesMenuItem>();
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// Гарантирует наличие группы «Новинки программы» в компоновке (issue #4): если
    /// в каталоге есть пункты с признаком «новинка», отсутствующие в компоновке, они
    /// добавляются в группу <see cref="UtilitiesMenuCatalog.GroupNewItems"/>. Группа
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
    public static UtilitiesMenuLayout? EnsureNewItemsGroup(UtilitiesMenuLayout? layout)
    {
        if (layout is null)
            return null;

        var existing = FindGroup(layout.Root, UtilitiesMenuCatalog.GroupNewItems);
        if (existing is not null)
        {
            // Группа уже есть (пользователь мог её переместить). Добавляем в неё только
            // неразмещённые НИГДЕ новинки каталога: пункт, перенесённый пользователем
            // в свою папку, НЕ дублируется в «Новинках» (issue #65).
            var toAdd = CollectNewItemsNotInGroup(layout);
            foreach (var item in toAdd)
                existing.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = item.Id });
            // Видимость группы определяется наличием пунктов В САМОЙ ГРУППЕ (issue #65):
            // даже если все новинки каталога уже размещены где-либо в компоновке, но
            // в группе остались пункты — группа показывается; пустая группа скрывается.
            existing.Visible = existing.Children.Count > 0;
            // issue #81: старые layout (до 1.0.0.103) могли сохранить в группу
            // «Новинки программы» запасную «папку» (или пустую иконку) — реальное меню
            // показывало не ту иконку, хотя каталог и редактор уже отдавали NewBox.
            // Сбрасываем легаси-значения к стандартной иконке каталога (звезда NEW),
            // чтобы иконка была везде одинаковой.
            FixLegacyNewItemsIcon(existing);
            return layout;
        }

        // Группы нет — создаём в конце корня с разделителем перед ней, только если
        // есть неразмещённые новинки (как в меню).
        var newItems = CollectNewItems(layout);
        if (newItems.Count == 0)
            return layout;

        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            SeparatorBefore = true,
        };
        foreach (var item in newItems)
            group.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = item.Id });
        layout.Root.Add(group);
        return layout;
    }

    /// <summary>
    /// Собирает «новинки программы» для добавления в существующую группу: пункты
    /// каталога с <see cref="UtilitiesMenuItem.IsNew"/>, отсутствующие ВО ВСЕЙ компоновке
    /// (включая детей самой группы). Пункт, размещённый пользователем где-либо (в т.ч.
    /// перенесённый из «Новинок» в свою папку), не возвращается и не дублируется.
    /// </summary>
    private static IReadOnlyList<UtilitiesMenuItem> CollectNewItemsNotInGroup(UtilitiesMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout.Root, present);
        var result = new List<UtilitiesMenuItem>();
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>Ищет группу с указанным <c>GroupId</c> в дереве (рекурсивно, включая вложенные).</summary>
    private static UtilitiesMenuNode? FindGroup(IEnumerable<UtilitiesMenuNode>? nodes, string groupId)
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
    public static void CollectItemIds(IEnumerable<UtilitiesMenuNode>? nodes, HashSet<string> target)
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
    /// issue #81: сбрасывает легаси-иконку (запасная «папка»/пустая) в группе
    /// «Новинки программы», сохранённую старыми layout-файлами до 1.0.0.103, к
    /// стандартной иконке каталога (NewBox / #22C55E) — чтобы реальное меню,
    /// редактор структуры и контекстное меню показывали одну и ту же иконку.
    /// Пользовательская (не-легаси) иконка, назначенная в редакторе, сохраняется.
    /// </summary>
    private static void FixLegacyNewItemsIcon(UtilitiesMenuNode group)
    {
        if (!string.Equals(group.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal))
            return;
        if (!UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon(group.IconKey))
            return;
        group.IconKey = null;
        group.IconColor = null;
        // Внешняя иконка тоже относится к «пользовательским» — оставляем её, только
        // если она задана вместе с не-легаси ключом; легаси-путь сбрасываем.
        if (string.IsNullOrWhiteSpace(group.IconKey))
            group.ExternalIconPath = null;
    }
}
