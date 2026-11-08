using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты резолвера компоновки меню «Утилиты» (issue #374): выбор стандартной компоновки
/// при выключенной правке и пользовательской при включённой; сбор «Новинок программы».
/// </summary>
public sealed class UtilitiesMenuResolverTests
{
    [Fact]
    public void Resolve_EditingDisabled_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = false };
        var layout = UtilitiesMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingDisabled_IgnoresStore()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = false };
        var store = new MemoryLayoutStore(new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("ActualReleases")
            }
        });
        var layout = UtilitiesMenuResolver.Resolve(settings, store);
        // Правка выключена — файл компоновки игнорируется.
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingEnabled_NoStore_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = true };
        var layout = UtilitiesMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        // 13 пунктов/групп («Лицензия…» — 2.0) + 5 узлов-разделителей (issue #374; «Управление службами»
        // issue #11 добавлено после «Серверы 1С»).
        Assert.Equal(18, layout!.Root.Count);
    }

    [Fact]
    public void Resolve_EditingEnabled_WithStore_ReturnsCustomLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = true };
        var custom = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("ActualReleases"),
                UtilitiesMenuLayouts.Item("ServerMonitor", separatorBefore: true)
            }
        };
        var store = new MemoryLayoutStore(custom);
        var layout = UtilitiesMenuResolver.Resolve(settings, store);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Root.Count);
        Assert.Equal("ActualReleases", layout.Root[0].ItemId);
        Assert.Equal("ServerMonitor", layout.Root[1].ItemId);
        Assert.True(layout.Root[1].SeparatorBefore);
    }

    [Fact]
    public void Resolve_NullSettings_ReturnsDefaultLayout()
    {
        var layout = UtilitiesMenuResolver.Resolve(null, null);
        Assert.NotNull(layout);
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void CollectNewItems_CurrentCatalog_HasNoUnplacedNewItems()
    {
        // В каталоге сейчас нет пунктов с признаком «новинка» (все пункты уже размещены
        // в стандартной компоновке), поэтому «Новинки программы» пустые.
        var layout = UtilitiesMenuLayouts.Default();
        var newItems = UtilitiesMenuResolver.CollectNewItems(layout);
        Assert.Empty(newItems);
    }

    [Fact]
    public void CollectNewItems_NullLayout_ReturnsAllNewItems()
    {
        // null-компоновка = «ничего не размещено»: возвращаются все пункты каталога
        // с признаком «новинка» (сейчас это «Уведомления: Telegram и email», 0.3.12.69).
        var newItems = UtilitiesMenuResolver.CollectNewItems(null);

        Assert.Contains(newItems, i => i.Id == "NotificationChannels");
    }

    [Fact]
    public void CollectItemIds_CollectsItemsAcrossGroups()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.Contains("CommandPalette", ids);
        Assert.Contains("CheckForUpdates", ids);
        // Пункты внутри групп тоже собираются.
        Assert.Contains("UnlockPrivateBases", ids);
        Assert.Contains("ConfigDiff", ids);
        Assert.Contains("RemoveMissingFileBases", ids);
        // Идентификаторы не дублируются: HashSet.
        Assert.True(ids.Count > 10);
    }

    // ---- «Новинки программы» в структуре (issue #4) ----

    [Fact]
    public void EnsureNewItemsGroup_NoNewItems_ReturnsSameLayout()
    {
        // Стандартная компоновка содержит все пункты каталога — «Новинки» не добавляются.
        var layout = UtilitiesMenuLayouts.Default();
        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.Same(layout, result);
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Root,
            n => string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureNewItemsGroup_AddsGroupWithNewItemsAtEnd()
    {
        // Пользовательская компоновка без пункта-новинки: группа создаётся в конце корня
        // с разделителем перед ней и принудительно видимой.
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette")
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[^1];
        Assert.Equal("group", group.Kind);
        Assert.Equal(UtilitiesMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        Assert.True(group.SeparatorBefore);
        Assert.Contains(group.Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingGroup_AddsMissingItems()
    {
        // Группа уже есть в компоновке (пользователь мог её переместить) — пункты
        // добавляются в неё, а не в новую группу в конце корня.
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                group
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.Same(group, result.Root[1]);
        Assert.Contains(group.Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingHiddenGroup_ForcesVisible()
    {
        // Скрытие группы в файле не влияет на отображение при работе: видимость
        // принудительно включается (issue #4).
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = false,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode> { group }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.True(result.Root[0].Visible);
        Assert.Contains(result.Root[0].Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_NullLayout_ReturnsNull()
    {
        Assert.Null(UtilitiesMenuResolver.EnsureNewItemsGroup(null));
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingEmptyGroup_BecomesHidden()
    {
        // Все пункты каталога размещены в компоновке — новых нет. Группа «Новинки
        // программы» в рабочем меню скрывается, даже если в файле она помечена
        // видимой: видимость определяется наличием пунктов, а не галочкой (issue #4).
        var layout = UtilitiesMenuLayouts.Default();
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        layout.Root.Add(group);

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.False(result.Root[^1].Visible);
        Assert.Empty(result.Root[^1].Children);
    }

    [Fact]
    public void EnsureNewItemsGroup_NoNewItems_NoGroup_ReturnsSameLayout()
    {
        // Новых пунктов нет и группы нет — компоновка не меняется (issue #4).
        var layout = UtilitiesMenuLayouts.Default();

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.Same(layout, result);
    }

    // ---- Issue #65: видимость «Новинок» определяется содержимым группы ----

    [Fact]
    public void EnsureNewItemsGroup_UserMovedOneItemToFolder_GroupStaysVisibleWithRemainingItems()
    {
        // Сценарий 7OH: 9 новинок, пользователь перенёс 1 в свою папку → все 9
        // присутствуют в компоновке (8 в группе NewItems + 1 в папке). Группа
        // обязана остаться видимой, её содержимое не меняется (issue #65).
        var newItems = UtilitiesMenuCatalog.All.Where(i => i.IsNew).ToList();
        Assert.True(newItems.Count >= 3);
        var movedId = newItems[0].Id;
        var remainingIds = newItems.Skip(1).Select(i => i.Id).ToList();

        var layout = UtilitiesMenuLayouts.Default();
        RemoveItemRecursively(layout.Root, newItems.Select(i => i.Id));

        // Папка пользователя с перенесённым пунктом.
        var folder = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = "Папка",
            Children = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = movedId }
            }
        };
        layout.Root.Add(folder);

        // Группа «Новинки» с оставшимися пунктами (в файле помечена скрытой).
        var newGroup = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = false,
            Children = remainingIds.Select(id => new UtilitiesMenuNode { Kind = "item", ItemId = id }).ToList(),
        };
        layout.Root.Add(newGroup);

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = FindGroupRecursively(result!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(group);
        // Группа видима: в ней остались пункты.
        Assert.True(group!.Visible);
        // Содержимое не изменено — только «оставшиеся» новинки, перенесённая не продублирована.
        Assert.Equal(remainingIds.Count, group.Children.Count);
        Assert.DoesNotContain(group.Children, c => string.Equals(c.ItemId, movedId, StringComparison.Ordinal));
        // Перенесённый пункт остался в папке.
        Assert.Contains(folder.Children, c => string.Equals(c.ItemId, movedId, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureNewItemsGroup_GroupHidden_OnlyWhenNoItemsInGroup()
    {
        // Пустая группа «Новинки» скрывается (все новинки каталога размещены
        // в стандартной компоновке).
        var layoutEmpty = UtilitiesMenuLayouts.Default();
        layoutEmpty.Root.Add(new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new List<UtilitiesMenuNode>()
        });

        var resultEmpty = UtilitiesMenuResolver.EnsureNewItemsGroup(layoutEmpty);

        var empty = FindGroupRecursively(resultEmpty!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(empty);
        Assert.False(empty!.Visible);

        // Группа с детьми видима, даже когда ВСЕ новинки каталога уже размещены
        // где-либо в компоновке (часть — в группе, часть — в папке пользователя).
        var newItems = UtilitiesMenuCatalog.All.Where(i => i.IsNew).ToList();
        Assert.True(newItems.Count >= 3);
        var movedId = newItems[0].Id;
        var groupIds = newItems.Skip(1).Select(i => i.Id).ToList();

        var layoutWithItems = UtilitiesMenuLayouts.Default();
        RemoveItemRecursively(layoutWithItems.Root, newItems.Select(i => i.Id));
        layoutWithItems.Root.Add(new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = "Папка",
            Children = new List<UtilitiesMenuNode> { new() { Kind = "item", ItemId = movedId } }
        });
        var groupWithItems = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = false,
            Children = groupIds.Select(id => new UtilitiesMenuNode { Kind = "item", ItemId = id }).ToList(),
        };
        layoutWithItems.Root.Add(groupWithItems);

        var resultWithItems = UtilitiesMenuResolver.EnsureNewItemsGroup(layoutWithItems);

        var nonEmpty = FindGroupRecursively(resultWithItems!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(nonEmpty);
        Assert.True(nonEmpty!.Visible);
    }

    [Fact]
    public void EnsureNewItemsGroup_MovedItem_IsNotAddedBackToGroup()
    {
        // Перенесённый в папку пункт-новинка НЕ возвращается обратно в «Новинки»,
        // даже если группа существует: неразмещённых новинок нет — группа не растёт.
        var newItems = UtilitiesMenuCatalog.All.Where(i => i.IsNew).ToList();
        Assert.True(newItems.Count >= 2);
        var movedId = newItems[0].Id;

        var layout = new UtilitiesMenuLayout { Root = new List<UtilitiesMenuNode>() };
        // Пользователь разместил все новинки сам: одну — в своей папке...
        layout.Root.Add(new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = "Папка",
            Children = new List<UtilitiesMenuNode> { new() { Kind = "item", ItemId = movedId } }
        });
        // ...остальные — в корне компоновки.
        foreach (var item in newItems.Skip(1))
            layout.Root.Add(new UtilitiesMenuNode { Kind = "item", ItemId = item.Id });
        // Группа «Новинки» существует, но пустая (все пункты уже размещены).
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new List<UtilitiesMenuNode>()
        };
        layout.Root.Add(group);

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var resolved = FindGroupRecursively(result!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(resolved);
        // Перенесённый пункт не продублирован в группу, группа пуста и скрыта.
        Assert.DoesNotContain(resolved!.Children, c => string.Equals(c.ItemId, movedId, StringComparison.Ordinal));
        Assert.Empty(resolved.Children);
        Assert.False(resolved.Visible);
        // В папке пункт остался.
        Assert.Contains(layout.Root[0].Children, c => string.Equals(c.ItemId, movedId, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureNewItemsGroup_NewCatalogItems_AddedToExistingGroup()
    {
        // Новинки из новых версий, отсутствующие в компоновке, добавляются
        // в существующую группу «Новинки» (issue #65).
        var existingNew = UtilitiesMenuCatalog.All.First(i => i.IsNew).Id;
        var layout = new UtilitiesMenuLayout
        {
            Root = new List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                new UtilitiesMenuNode
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupNewItems,
                    Visible = false,
                    Children = new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = existingNew }
                    }
                }
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Root.Count);
        var group = result.Root[1];
        Assert.Equal(UtilitiesMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        // В группе — все новинки каталога (существующая + неразмещённые).
        var catalogIds = UtilitiesMenuCatalog.All.Where(i => i.IsNew).Select(i => i.Id).ToList();
        foreach (var id in catalogIds)
            Assert.Contains(group.Children, c => string.Equals(c.ItemId, id, StringComparison.Ordinal));
    }

    /// <summary>Рекурсивно удаляет из дерева все узлы-пункты с указанными ItemId.</summary>
    private static void RemoveItemRecursively(List<UtilitiesMenuNode> nodes, IEnumerable<string> itemIds)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].ItemId is not null && itemIds.Contains(nodes[i].ItemId))
            {
                nodes.RemoveAt(i);
                continue;
            }
            if (nodes[i].Children.Count > 0)
                RemoveItemRecursively(nodes[i].Children, itemIds);
        }
    }

    /// <summary>Ищет группу по GroupId по всему дереву (рекурсивно).</summary>
    private static UtilitiesMenuNode? FindGroupRecursively(IEnumerable<UtilitiesMenuNode> nodes, string groupId)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal) &&
                string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            {
                return node;
            }
            if (node.Children.Count > 0 &&
                FindGroupRecursively(node.Children, groupId) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    /// <summary>Хранилище-заглушка для тестов (без файла на диске).</summary>
    private sealed class MemoryLayoutStore : IUtilitiesMenuLayoutStore
    {
        private readonly UtilitiesMenuLayout? _layout;

        public MemoryLayoutStore(UtilitiesMenuLayout? layout)
        {
            _layout = layout;
        }

        public string FilePath => Path.Combine(Path.GetTempPath(), "memory_utilities_menu_layout.json");

        public UtilitiesMenuLayout? Load() => _layout;

        public void Save(UtilitiesMenuLayout layout)
        {
            // Не сохраняем: тестовая заглушка.
        }
    }
}