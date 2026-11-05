using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты применимости пунктов контекстного меню к ВЫБРАННОЙ ГРУППЕ дерева
/// (issue #105, запрос 7OH): неприменимые пункты скрываются (не блокируются),
/// пустые подменю не показываются, «висячие» и двойные разделители убираются,
/// компоновка при фильтрации не мутируется.
/// </summary>
public sealed class Issue105ContextMenuGroupTests
{
    // Пункты, явно названные лишними для группы в issue #105.
    private static readonly string[] ExplicitlyHidden =
    {
        "ShowExportsList",      // «Список выгрузок»
        "RegisterComConnector", // «Зарегистрировать COM-коннектор»
        "WebPublish",           // «Публикация на веб-сервере»
        "ConfigLink"            // «Связать с конфигурацией»
    };

    [Fact]
    public void IsGroupApplicable_KeepsGroupCommands()
    {
        // Работают с группой: пакетные операции («Для группы (N)…»), пользовательские
        // действия (контекст «Группа»), добавление базы в группу, очистка кэша
        // (с предвыбором баз группы), удаление группы, настройки группы.
        foreach (var kind in new[]
                 {
                     ContextMenuCommandKind.BatchOperations,
                     ContextMenuCommandKind.CustomActions,
                     ContextMenuCommandKind.AddBase,
                     ContextMenuCommandKind.ClearProgramCache,
                     ContextMenuCommandKind.ClearUserCache,
                     ContextMenuCommandKind.ClearCacheBoth,
                     ContextMenuCommandKind.Delete,
                     ContextMenuCommandKind.EditSettings
                 })
        {
            Assert.True(ContextMenuCatalog.IsGroupApplicable(kind), kind.ToString());
        }
    }

    [Fact]
    public void IsGroupApplicable_HidesSingleBaseCommands()
    {
        // Команды, требующие выделенную базу (SelectedInfobase), для группы не работают.
        foreach (var kind in new[]
                 {
                     ContextMenuCommandKind.LaunchEnterprise,
                     ContextMenuCommandKind.LaunchConfigurator,
                     ContextMenuCommandKind.RunScript,
                     ContextMenuCommandKind.ToFavorites,
                     ContextMenuCommandKind.Pin,
                     ContextMenuCommandKind.RefreshConfigInfo,
                     ContextMenuCommandKind.CheckUpdate,
                     ContextMenuCommandKind.ConfigLink,
                     ContextMenuCommandKind.SessionLock,
                     ContextMenuCommandKind.RegisterComConnector,
                     ContextMenuCommandKind.NetworkDiagnostics,
                     ContextMenuCommandKind.OpenInfobaseFolder,
                     ContextMenuCommandKind.CloneBase,
                     ContextMenuCommandKind.TestDeploy,
                     ContextMenuCommandKind.CheckIntegrity,
                     ContextMenuCommandKind.WebPublish,
                     ContextMenuCommandKind.LaunchHistory,
                     ContextMenuCommandKind.DumpToDt,
                     ContextMenuCommandKind.DumpConfigToCf,
                     ContextMenuCommandKind.ShowExportsList,
                     ContextMenuCommandKind.FindInList,
                     ContextMenuCommandKind.CreateDesktopShortcut,
                     ContextMenuCommandKind.CopyConnectionString,
                     ContextMenuCommandKind.Notes
                 })
        {
            Assert.False(ContextMenuCatalog.IsGroupApplicable(kind), kind.ToString());
        }
    }

    [Fact]
    public void FilterForGroup_DefaultLayout_HidesExplicitlyNamedItems()
    {
        var layout = ContextMenuLayouts.Default();
        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        var ids = CollectItemIds(filtered);
        foreach (var id in ExplicitlyHidden)
            Assert.False(ids.Contains(id), id);
    }

    [Fact]
    public void FilterForGroup_DefaultLayout_EmptySubmenusRemoved()
    {
        // «Обновление и связь» и «Администрирование» состоят только из
        // одиночно-базовых команд — для группы пустые, не показываются.
        // Issue #106 (v1.0.1.37): в «Резервирование» добавлена группо-применимая
        // команда «Массовая загрузка cf» — подменю для группы больше не пусто.
        var layout = ContextMenuLayouts.Default();
        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        var groupIds = filtered
            .Where(n => string.Equals(n.Kind, "group", StringComparison.Ordinal))
            .Select(n => n.GroupId)
            .ToList();
        Assert.DoesNotContain(ContextMenuCatalog.GroupUpdateAndLink, groupIds);
        Assert.DoesNotContain(ContextMenuCatalog.GroupAdministration, groupIds);
        Assert.Contains(ContextMenuCatalog.GroupBackup, groupIds);
        // «Очистить кэш» остаётся: очистка открывается с предвыбором баз группы.
        Assert.Contains(ContextMenuCatalog.GroupClearCache, groupIds);
    }

    [Fact]
    public void FilterForGroup_DefaultLayout_KeepsApplicableTopLevelItems()
    {
        var layout = ContextMenuLayouts.Default();
        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        var ids = CollectItemIds(filtered);
        Assert.Contains("BatchOperations", ids);
        Assert.Contains("CustomActions", ids);
        Assert.Contains("AddBase", ids);
        Assert.Contains("Delete", ids);
        Assert.Contains("EditSettings", ids);
        // «Очистить кэш» подменю с тремя пунктами.
        Assert.Contains("ClearProgramCache", ids);
        Assert.Contains("ClearUserCache", ids);
        Assert.Contains("ClearCacheBoth", ids);
    }

    [Fact]
    public void FilterForGroup_DefaultLayout_NoDanglingOrDoubleSeparators()
    {
        var layout = ContextMenuLayouts.Default();
        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        AssertNoEdgeOrDoubleSeparators(filtered);
        foreach (var group in filtered.Where(IsGroup))
            AssertNoEdgeOrDoubleSeparators(group.Children);
    }

    [Fact]
    public void FilterForGroup_DoesNotMutateSourceLayout()
    {
        var layout = ContextMenuLayouts.Default();
        var before = CollectItemIds(layout.Root);
        var beforeCount = layout.Root.Count;

        ContextMenuResolver.FilterForGroup(layout.Root);

        // Фильтрация не меняет исходную компоновку (она переиспользуется резолвером).
        Assert.Equal(beforeCount, layout.Root.Count);
        Assert.Equal(before, CollectItemIds(layout.Root));
    }

    [Fact]
    public void FilterForGroup_UnknownAndCustomItemsRemoved()
    {
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("BatchOperations"),
                ContextMenuLayouts.Item("NotInCatalog"),          // неизвестный пункт
                ContextMenuLayouts.Separator(),
                ContextMenuLayouts.Group("MyCustomGroup", new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("DumpToDt")           // неприменимый — группа опустеет
                }),
                ContextMenuLayouts.Group("MyCustomGroup2", new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("LaunchEnterprise"),
                    ContextMenuLayouts.Item("EditSettings")
                })
            }
        };

        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        var ids = CollectItemIds(filtered);
        Assert.DoesNotContain("NotInCatalog", ids);
        Assert.DoesNotContain("LaunchEnterprise", ids);
        Assert.Contains("BatchOperations", ids);
        Assert.Contains("EditSettings", ids);

        // Пустая пользовательская группа скрыта; непустая (с применимым пунктом) осталась.
        var groupIds = filtered.Where(IsGroup).Select(n => n.GroupId).ToList();
        Assert.DoesNotContain("MyCustomGroup", groupIds);
        Assert.Contains("MyCustomGroup2", groupIds);
    }

    [Fact]
    public void FilterForGroup_TrimsSeparatorsAroundRemovedItems()
    {
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("LaunchEnterprise"), // скроется
                ContextMenuLayouts.Separator(),              // «висячий» сверху — уберётся
                ContextMenuLayouts.Item("EditSettings"),
                ContextMenuLayouts.Separator(),
                ContextMenuLayouts.Separator(),              // двойной — один уберётся
                ContextMenuLayouts.Item("BatchOperations"),
                ContextMenuLayouts.Separator(),              // «висячий» снизу — уберётся
                ContextMenuLayouts.Item("Notes")             // скроется
            }
        };

        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        AssertNoEdgeOrDoubleSeparators(filtered);
        var separators = filtered.Count(IsSeparator);
        Assert.Equal(1, separators);
        var ids = CollectItemIds(filtered);
        Assert.DoesNotContain("LaunchEnterprise", ids);
        Assert.DoesNotContain("Notes", ids);
    }

    [Fact]
    public void FilterForGroup_ClearsSeparatorBeforeNextToSeparator()
    {
        var layout = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("EditSettings", separatorBefore: true),
                ContextMenuLayouts.Separator()
            }
        };

        var filtered = ContextMenuResolver.FilterForGroup(layout.Root);

        // Разделитель в конце списка убран, SeparatorBefore первого узла сброшен —
        // двойных линий при построении меню не будет.
        Assert.All(filtered, n => Assert.False(n.SeparatorBefore));
        Assert.Equal(0, filtered.Count(IsSeparator));
        Assert.Contains("EditSettings", CollectItemIds(filtered));
    }

    private static bool IsGroup(ContextMenuNode node) =>
        string.Equals(node.Kind, "group", StringComparison.Ordinal);

    private static bool IsSeparator(ContextMenuNode node) =>
        string.Equals(node.Kind, "separator", StringComparison.Ordinal);

    private static HashSet<string> CollectItemIds(IEnumerable<ContextMenuNode> nodes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        ContextMenuResolver.CollectItemIds(nodes, set);
        return set;
    }

    private static void AssertNoEdgeOrDoubleSeparators(List<ContextMenuNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!IsSeparator(nodes[i]))
                continue;
            Assert.NotEqual(0, i);                        // разделитель не в начале
            Assert.NotEqual(nodes.Count - 1, i);          // и не в конце
            Assert.False(IsSeparator(nodes[i - 1]));      // без подряд идущих
        }
    }
}
