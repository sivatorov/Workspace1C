using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #113 «Группа "Без группы"»: служебный узел «Без группы» считается
/// групповым контекстом меню — пункты, применимые к группе, показываются, но
/// требующие реальной группы (удаление группы) скрываются. ПКМ по узлу «Без группы»
/// выделяет его (стражи по исходникам WPF/Avalonia).
/// </summary>
public sealed class Issue113NoGroupMenuTests
{
    [Fact]
    public void FilterForGroup_NoGroupContext_HidesDeleteKeepsGroupOps()
    {
        var layout = ContextMenuLayouts.Default();

        var noGroupIds = CollectItemIds(ContextMenuResolver.FilterForGroup(layout.Root, isNoGroup: true));
        var groupIds = CollectItemIds(ContextMenuResolver.FilterForGroup(layout.Root, isNoGroup: false));

        // «Удалить» (группы) для «Без группы» скрыт — модели группы нет.
        Assert.False(noGroupIds.Contains("Delete"), "«Удалить» должен быть скрыт для узла «Без группы»");
        // Для обычной группы «Удалить» остаётся.
        Assert.True(groupIds.Contains("Delete"), "«Удалить» должен остаться для обычной группы");

        // Групповые операции в «Без группы» доступны (очистка кэша, пакетные, настройки).
        foreach (var id in new[] { "ClearProgramCache", "ClearUserCache", "ClearCacheBoth", "BatchOperations", "EditSettings" })
            Assert.True(noGroupIds.Contains(id), id);
    }

    [Fact]
    public void FilterForGroup_NoGroupContext_DoesNotMutateLayout()
    {
        var layout = ContextMenuLayouts.Default();
        var before = CollectItemIds(layout.Root);

        ContextMenuResolver.FilterForGroup(layout.Root, isNoGroup: true);

        Assert.Equal(before, CollectItemIds(layout.Root));
    }

    [Fact]
    public void BatchBlockTargetsNoGroupBases_BothStacks()
    {
        // Issue #113 (v1.0.1.41): пакетный блок «Для группы (N)…» работает и для
        // узла «Без группы» — цели считаются по IsNoGroupBatchTarget, заголовок
        // берётся из IsGroupBatchTarget (VM), поэтому достаточно стражей по VM.
        var batch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Batch.cs"));
        Assert.Contains("IsNoGroupBatchTarget", batch, StringComparison.Ordinal);
        Assert.Contains("GroupNodeViewModel.NoGroupMarker", batch, StringComparison.Ordinal);
        Assert.Contains("GetNoGroupInfobases", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void RightClickSelectsNoGroupNode_BothStacks()
    {
        // WPF: ветка NoGroupMarker в OnInfobaseTree_PreviewMouseRightButtonDown.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        Assert.Contains("noGroupNode.Group is null", wpf, StringComparison.Ordinal);
        Assert.Contains("GroupNodeViewModel.NoGroupMarker", wpf, StringComparison.Ordinal);

        // Avalonia: IsRowMenuNoGroupContext в построителе меню дерева.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("IsRowMenuNoGroupContext", avalonia, StringComparison.Ordinal);

        // WPF: IsRowMenuGroupContext учитывает «Без группы».
        var contextMenu = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));
        Assert.Contains("IsRowMenuNoGroupContext", contextMenu, StringComparison.Ordinal);
    }

    private static HashSet<string> CollectItemIds(IEnumerable<ContextMenuNode> nodes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Collect(nodes, set);
        return set;

        static void Collect(IEnumerable<ContextMenuNode> list, HashSet<string> acc)
        {
            foreach (var node in list)
            {
                if (node is null)
                    continue;
                if (!string.IsNullOrEmpty(node.ItemId))
                    acc.Add(node.ItemId);
                if (node.Children is { Count: > 0 })
                    Collect(node.Children, acc);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }
}
