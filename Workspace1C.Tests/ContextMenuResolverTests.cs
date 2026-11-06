using System;
using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты резолвера компоновки контекстного меню базы (issue #56): выбор стандартной
/// компоновки при выключенной правке и пользовательской при включённой; сбор «Новинок
/// программы»; гарантия наличия Essential-пунктов.
/// </summary>
public sealed class ContextMenuResolverTests
{
    [Fact]
    public void Resolve_EditingDisabled_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { ContextMenuEditingEnabled = false };
        var layout = ContextMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        Assert.Equal("LaunchEnterprise", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingDisabled_IgnoresStore()
    {
        var settings = new AppSettings { ContextMenuEditingEnabled = false };
        var store = new MemoryLayoutStore(new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("EditSettings")
            }
        });
        var layout = ContextMenuResolver.Resolve(settings, store);
        // Правка выключена — файл компоновки игнорируется.
        Assert.Equal("LaunchEnterprise", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingEnabled_NoStore_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { ContextMenuEditingEnabled = true };
        var layout = ContextMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        // 20 пунктов/групп + 5 узлов-разделителей.
        Assert.Equal(25, layout!.Root.Count);
    }

    [Fact]
    public void Resolve_EditingEnabled_WithStore_ReturnsCustomLayout()
    {
        var settings = new AppSettings { ContextMenuEditingEnabled = true };
        var custom = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("EditSettings"),
                ContextMenuLayouts.Item("Delete", separatorBefore: true)
            }
        };
        var store = new MemoryLayoutStore(custom);
        var layout = ContextMenuResolver.Resolve(settings, store);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Root.Count);
        Assert.Equal("EditSettings", layout.Root[0].ItemId);
        Assert.Equal("Delete", layout.Root[1].ItemId);
        Assert.True(layout.Root[1].SeparatorBefore);
    }

    [Fact]
    public void Resolve_NullSettings_ReturnsDefaultLayout()
    {
        var layout = ContextMenuResolver.Resolve(null, null);
        Assert.NotNull(layout);
        Assert.Equal("LaunchEnterprise", layout!.Root[0].ItemId);
    }

    // ---- «Новинки программы» (issue #4) ----

    [Fact]
    public void CollectNewItems_DefaultLayout_HasNoUnplacedNewItems()
    {
        // Стандартная компоновка содержит все пункты каталога (включая пункт-«новинку»),
        // поэтому «Новинки программы» пустые.
        var layout = ContextMenuLayouts.Default();
        var newItems = ContextMenuResolver.CollectNewItems(layout);
        Assert.Empty(newItems);
    }

    [Fact]
    public void CollectNewItems_LayoutWithoutNewItem_ReturnsIt()
    {
        // Пользовательская компоновка без пунктов-«новинок»: они попадают в «Новинки».
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode> { ContextMenuLayouts.Item("LaunchEnterprise") }
        };
        var newItems = ContextMenuResolver.CollectNewItems(layout);
        var ids = newItems.Select(i => i.Id).ToHashSet();
        Assert.Contains("CreateDesktopShortcut", ids);
        // «Публикация на веб-сервере» — новая в контекстном меню (перенос из «Утилиты», #63).
        Assert.Contains("WebPublish", ids);
    }

    [Fact]
    public void CollectItemIds_CollectsItemsAcrossGroups()
    {
        var layout = ContextMenuLayouts.Default();
        var ids = new HashSet<string>();
        ContextMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.Contains("LaunchEnterprise", ids);
        Assert.Contains("EditSettings", ids);
        // Пункты внутри групп тоже собираются.
        Assert.Contains("ConfigLink", ids);
        Assert.Contains("SessionLock", ids);
        Assert.Contains("LaunchHistory", ids);
        Assert.Equal(ContextMenuCatalog.All.Count, ids.Count);
    }

    [Fact]
    public void EnsureNewItemsGroup_NoNewItems_ReturnsSameLayout()
    {
        // Стандартная компоновка содержит все пункты каталога — «Новинки» не добавляются.
        var layout = ContextMenuLayouts.Default();
        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.Same(layout, result);
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Root,
            n => string.Equals(n.GroupId, ContextMenuCatalog.GroupNewItems, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureNewItemsGroup_AddsGroupWithNewItemsAtEnd()
    {
        // Пользовательская компоновка без пункта-«новинки»: группа создаётся в конце корня
        // с разделителем перед ней и принудительно видимой.
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode> { ContextMenuLayouts.Item("LaunchEnterprise") }
        };

        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[^1];
        Assert.Equal("group", group.Kind);
        Assert.Equal(ContextMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        Assert.True(group.SeparatorBefore);
        Assert.Contains(group.Children, c => c.ItemId == "CreateDesktopShortcut");
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingGroup_AddsMissingItemsAndKeepsLocation()
    {
        // Группа уже есть в начале корня (пользователь её переместил) — пункты
        // добавляются в неё, а местоположение группы сохраняется.
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Group(ContextMenuCatalog.GroupNewItems, new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("LaunchEnterprise")
                })
            }
        };

        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[0];
        Assert.Equal(ContextMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        Assert.Contains(group.Children, c => c.ItemId == "CreateDesktopShortcut");
    }

    [Fact]
    public void EnsureNewItemsGroup_GroupWithPlacedNewItem_StaysVisible()
    {
        // Issue #65: неразмещённых новинок больше нет (CreateDesktopShortcut уже
        // размещён пользователем вручную), но в самой группе остались пункты —
        // группа показывается, а не скрывается (старое поведение прятало её).
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Group(ContextMenuCatalog.GroupNewItems, new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("LaunchEnterprise"),
                    ContextMenuLayouts.Item("CreateDesktopShortcut") // новинка уже размещена вручную
                })
            }
        };

        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[0];
        Assert.Equal(ContextMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        // «WebPublish» и «Заметки» — неразмещённые новинки (перенесены из «Утилиты»/новые,
        // #63 / п.8 1.0.0.90); «Развернуть тестовую копию» — п.10 (1.0.0.92);
        // «Массовая загрузка cf» — issue #106 (v1.0.1.37);
        // «Открыть папку программного/пользовательского кеша» — issue #110 (v1.0.1.40).
        // Все добавляются в существующую группу «Новинки».
        Assert.Equal(8, group.Children.Count);
    }

    [Fact]
    public void EnsureNewItemsGroup_EmptyGroup_BecomesHidden()
    {
        // Стандартная компоновка содержит все пункты каталога, включая новинку
        // CreateDesktopShortcut, поэтому добавлять в группу нечего: пустая группа
        // «Новинки» скрывается (видимость определяется наличием пунктов, issue #65).
        var layout = ContextMenuLayouts.Default();
        layout.Root.Add(ContextMenuLayouts.Group(ContextMenuCatalog.GroupNewItems, new List<ContextMenuNode>()));

        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[^1];
        Assert.Equal(ContextMenuCatalog.GroupNewItems, group.GroupId);
        Assert.False(group.Visible);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void EnsureNewItemsGroup_MovedNewItem_NotAddedBackToGroup()
    {
        // Новинка, перенесённая пользователем в свою папку, не дублируется
        // в «Новинках» (issue #65).
        var folder = ContextMenuLayouts.Group(null, new List<ContextMenuNode>
        {
            ContextMenuLayouts.Item("CreateDesktopShortcut")
        });
        folder.CustomTitle = "Папка";
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                folder,
                ContextMenuLayouts.Group(ContextMenuCatalog.GroupNewItems, new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("LaunchEnterprise")
                })
            }
        };

        var result = ContextMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[1];
        Assert.True(group.Visible);
        Assert.DoesNotContain(group.Children, c => string.Equals(c.ItemId, "CreateDesktopShortcut", StringComparison.Ordinal));
        Assert.Contains(folder.Children, c => string.Equals(c.ItemId, "CreateDesktopShortcut", StringComparison.Ordinal));
    }

    // ---- Essential-пункты (issue #56) ----

    [Fact]
    public void EnsureEssentialItems_AddsMissingEssentialItems()
    {
        // Пользователь удалил «Пользовательские действия» и «Для выделенных» —
        // резолвер возвращает их в компоновку (функция 7 и пакетные операции критичны).
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("LaunchEnterprise"),
                ContextMenuLayouts.Item("EditSettings")
            }
        };

        var result = ContextMenuResolver.EnsureEssentialItems(layout);

        Assert.NotNull(result);
        var ids = new HashSet<string>();
        ContextMenuResolver.CollectItemIds(result.Root, ids);
        Assert.Contains("CustomActions", ids);
        Assert.Contains("BatchOperations", ids);
    }

    [Fact]
    public void EnsureEssentialItems_KeepsExistingLocation()
    {
        // Пункты уже размещены пользователем — их местоположение не меняется,
        // компоновка не дополняется.
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("CustomActions"),
                ContextMenuLayouts.Item("BatchOperations")
            }
        };

        var result = ContextMenuResolver.EnsureEssentialItems(layout);

        Assert.Same(layout, result);
        Assert.Equal(2, result.Root.Count);
        Assert.Equal("CustomActions", result.Root[0].ItemId);
        Assert.Equal("BatchOperations", result.Root[1].ItemId);
    }

    [Fact]
    public void PrepareForMenu_GuaranteesEssentialsAndNewsGroup()
    {
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode> { ContextMenuLayouts.Item("LaunchEnterprise") }
        };

        var result = ContextMenuResolver.PrepareForMenu(layout);

        var ids = new HashSet<string>();
        ContextMenuResolver.CollectItemIds(result.Root, ids);
        Assert.Contains("CustomActions", ids);
        Assert.Contains("BatchOperations", ids);
        // «Новинки» добавлены (CreateDesktopShortcut отсутствует в компоновке).
        Assert.Contains(result.Root, n =>
            string.Equals(n.GroupId, ContextMenuCatalog.GroupNewItems, StringComparison.Ordinal));
    }

    /// <summary>Хранилище-заглушка для тестов (без файла на диске).</summary>
    private sealed class MemoryLayoutStore : IContextMenuLayoutStore
    {
        private readonly ContextMenuLayout? _layout;

        public MemoryLayoutStore(ContextMenuLayout? layout)
        {
            _layout = layout;
        }

        public string FilePath => "memory://context_menu_layout.json";

        public ContextMenuLayout? Load() => _layout;

        public void Save(ContextMenuLayout layout) { /* в памяти ничего не пишем */ }
    }
}