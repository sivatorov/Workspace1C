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
    /// Возвращает компоновку для редактирования и служебных сценариев:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// Для фактически отображаемого меню и кнопок панели используйте
    /// <see cref="ResolveForDisplay"/>: он учитывает сохранённую компоновку независимо
    /// от флага правки (issue #146).
    /// </summary>
    public static UtilitiesMenuLayout Resolve(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        if (settings is null || !settings.UtilitiesMenuEditingEnabled)
            return UtilitiesMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? UtilitiesMenuLayouts.Default();
    }

    /// <summary>
    /// Готовит фактически отображаемое меню и кнопки панели. Issue #146: сохранённая пользовательская
    /// компоновка — источник истины для отображения, даже если режим правки выключен:
    /// флаг <see cref="AppSettings.UtilitiesMenuEditingEnabled"/> разрешает менять
    /// структуру, но не должен подменять уже настроенные группы стандартной
    /// компоновкой (иначе сохранённый <c>DisplayMode</c> и группы верхнего уровня
    /// игнорировались, и кнопки панели снова показывали только иконки).
    /// </summary>
    public static UtilitiesMenuLayout ResolveForDisplay(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        // Файла компоновки нет — показываем стандартную структуру (обратная совместимость).
        // Настройка правки здесь намеренно не участвует: она влияет на возможность
        // редактирования, а не на то, какую компоновку показывать пользователю.
        var layout = store?.Load() ?? UtilitiesMenuLayouts.Default();

        // Issue #146 (T8): группа «ПРО» — обычный узел компоновки. Резолвер больше не
        // внедряет её при каждом отображении и не форсирует видимость «Инструментов»:
        // группа скрывается/перемещается/редактируется как любая другая.
        EnsureProGroup(layout);

        EnsureNewItemsGroup(layout);
        return layout;
    }

    /// <summary>
    /// Issue #146 (T8): гарантирует группу «ПРО» в компоновке и синхронизирует её состав.
    /// <para>
    /// Группа — обычный узел: если она уже есть, сохраняются её видимость, позиция,
    /// режим отображения, пользовательский заголовок/иконка и <c>NodeId</c>. При
    /// отсутствии (компоновки до 1.0.1.120) группа добавляется один раз внутрь раздела
    /// «Инструменты»; видимость раздела при этом НЕ меняется — скрытый «Инструменты»
    /// остаётся скрытым. Состав синхронизируется: новые пункты ПРО добавляются,
    /// исчезнувшие (модуль не зарегистрирован) — удаляются. Пункт, перенесённый
    /// пользователем в другую группу, не дублируется.
    /// </para>
    /// </summary>
    public static UtilitiesMenuLayout? EnsureProGroup(UtilitiesMenuLayout? layout)
    {
        if (layout?.Root is null)
            return layout;

        var proGroup = FindGroup(layout.Root, UtilitiesMenuCatalog.GroupPro);
        var created = proGroup is null;
        if (proGroup is null)
        {
            // Группа живёт в разделе «Инструменты». Если раздела нет (пользовательская
            // компоновка без стандартных разделов), создаём его — это единственное
            // место, где группа ПРО показывается так же, как в стандартной компоновке.
            var tools = FindGroup(layout.Root, UtilitiesMenuCatalog.GroupTools);
            if (tools is null)
            {
                tools = new UtilitiesMenuNode
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupTools,
                    IsTopLevel = true,
                    Visible = true
                };
                layout.Root.Add(tools);
            }

            proGroup = new UtilitiesMenuNode
            {
                Kind = "group",
                GroupId = UtilitiesMenuCatalog.GroupPro,
                NodeId = UtilitiesMenuCatalog.ProGroupNodeId,
                Visible = true
            };
            tools.Children.Add(proGroup);
        }

        // Повреждённая/ручная компоновка могла содержать несколько групп «ПРО» —
        // оставляем первую (дублирующее подменю — причина issue #146).
        RemoveDuplicateGroups(layout.Root, UtilitiesMenuCatalog.GroupPro, proGroup);

        // Для компоновок, где группы ещё не было: переносим уже размещённые пункты ПРО
        // внутрь новой группы (это наследие прежнего авто-размещения, а не выбор
        // пользователя). Для существующей группы выбор пользователя сохраняется.
        if (created)
            MoveExistingProItems(layout.Root, proGroup);

        SyncProGroupChildren(layout, proGroup);
        return layout;
    }

    /// <summary>
    /// Синхронизирует состав группы «ПРО», не трогая свойства и порядок существующих
    /// узлов. Удаляются только служебные/устаревшие узлы: placeholder-заглушки, ставшие
    /// ненужными (сборка с модулями), и пункты, которых больше нет в каталоге.
    /// </summary>
    private static void SyncProGroupChildren(UtilitiesMenuLayout layout, UtilitiesMenuNode proGroup)
    {
        var expected = UtilitiesMenuCatalog.ProGroupChildren();
        var expectedItems = expected
            .Where(node => string.Equals(node.Kind, "item", StringComparison.Ordinal))
            .Select(node => node.ItemId!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedPlaceholders = expected
            .Where(node => string.Equals(node.Kind, "placeholder", StringComparison.Ordinal))
            .Select(node => node.ItemId!)
            .ToHashSet(StringComparer.Ordinal);

        proGroup.Children.RemoveAll(node => node is not null
            && string.Equals(node.Kind, "placeholder", StringComparison.Ordinal)
            && !expectedPlaceholders.Contains(node.ItemId ?? string.Empty));

        proGroup.Children.RemoveAll(node => node is not null
            && string.Equals(node.Kind, "item", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(node.ItemId)
            && UtilitiesMenuCatalog.Find(node.ItemId) is null);

        // Пункт, который пользователь перенёс в свою группу, не дублируем (issue #65).
        var present = new HashSet<string>(StringComparer.Ordinal);
        CollectItemIds(layout.Root, present);
        foreach (var itemId in expectedItems)
        {
            if (present.Add(itemId))
                proGroup.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = itemId });
        }

        foreach (var featureId in expectedPlaceholders)
        {
            var exists = proGroup.Children.Any(node => node is not null
                && string.Equals(node.Kind, "placeholder", StringComparison.Ordinal)
                && string.Equals(node.ItemId, featureId, StringComparison.Ordinal));
            if (!exists)
                proGroup.Children.Add(new UtilitiesMenuNode { Kind = "placeholder", ItemId = featureId });
        }
    }

    /// <summary>
    /// Переносит уже размещённые в компоновке пункты ПРО в только что созданную группу
    /// (наследие прежнего авто-размещения). Настройки узла (видимость, иконка) сохраняются.
    /// </summary>
    private static void MoveExistingProItems(List<UtilitiesMenuNode> nodes, UtilitiesMenuNode proGroup)
    {
        if (nodes is null)
            return;
        var proIds = UtilitiesMenuCatalog.ProItems()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (proIds.Count == 0)
            return;

        var moved = new List<UtilitiesMenuNode>();
        foreach (var node in nodes.ToList())
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal)
                && node.ItemId is { } id && proIds.Contains(id))
            {
                if (ReferenceEquals(nodes, proGroup.Children))
                    continue;
                nodes.Remove(node);
                moved.Add(node);
                continue;
            }
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal)
                && !ReferenceEquals(node, proGroup))
            {
                MoveExistingProItems(node.Children, proGroup);
            }
        }

        foreach (var node in moved)
        {
            // Не дублируем пункт, уже находящийся в группе ПРО.
            if (!proGroup.Children.Any(existing => existing is not null
                && string.Equals(existing.Kind, "item", StringComparison.Ordinal)
                && string.Equals(existing.ItemId, node.ItemId, StringComparison.Ordinal)))
            {
                proGroup.Children.Add(node);
            }
        }
    }

    /// <summary>Удаляет все группы с указанным <c>GroupId</c>, кроме <paramref name="keep"/>.</summary>
    private static void RemoveDuplicateGroups(
        IEnumerable<UtilitiesMenuNode> nodes, string groupId, UtilitiesMenuNode keep)
    {
        if (nodes is null)
            return;
        var list = nodes as List<UtilitiesMenuNode> ?? nodes.ToList();
        list.RemoveAll(node => node is not null
            && !ReferenceEquals(node, keep)
            && string.Equals(node.Kind, "group", StringComparison.Ordinal)
            && string.Equals(node.GroupId, groupId, StringComparison.Ordinal));
        foreach (var node in list)
        {
            if (node is not null && !ReferenceEquals(node, keep))
                RemoveDuplicateGroups(node.Children, groupId, keep);
        }
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
