using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регресс issue #173 «Закрепление из других вкладок»: закрепление базы, находящейся
/// во вкладке «Недавние», должно сразу (без перезапуска и без полной пересборки дерева)
/// появляться в узле «Закреплённые» дерева «Все базы»; снятие закрепления — так же сразу.
/// Проверяется общая логика <see cref="PinnedSectionSync"/>, которую используют и
/// WPF-, и Avalonia-версия главной модели (раньше WPF при активном фильтре вообще не
/// обновляла кэш дерева «Все», поэтому закрепление «терялось» до перезапуска).
/// </summary>
public sealed class Issue173PinFromRecentTests
{
    private static Infobase CreateBase(string name, string group = "Группа") => new()
    {
        Id = "id-" + name,
        Name = name,
        Group = group,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    /// <summary>Текущая сортировка строк (как ApplyCurrentSort: закреплённые первыми).</summary>
    private static IEnumerable<Infobase> Order(IEnumerable<Infobase> source) =>
        source.OrderBy(i => i.GroupSortOrder)
              .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);

    private static GroupNodeViewModel CreatePinnedNode() =>
        new(null, marker: GroupNodeViewModel.PinnedMarker) { IsExpanded = true };

    /// <summary>
    /// Строит дерево «Все базы» так же, как RebuildGroupTreeCore/BuildGroupedTree:
    /// «Закреплённые» первыми (только если непусты), затем «Без группы» и группы.
    /// </summary>
    private static (List<GroupNodeViewModel> Roots, GroupNodeViewModel GroupNode) BuildAllTree(
        IEnumerable<Infobase> bases, Group group)
    {
        var pinned = new GroupNodeViewModel(null, marker: GroupNodeViewModel.PinnedMarker);
        var groupNode = new GroupNodeViewModel(group);
        var noGroup = new GroupNodeViewModel(null, marker: GroupNodeViewModel.NoGroupMarker);

        foreach (var infobase in bases)
        {
            if (infobase.IsPinned)
                pinned.Infobases.Add(infobase);
            if (string.IsNullOrWhiteSpace(infobase.Group))
                noGroup.Infobases.Add(infobase);
            else
                groupNode.Infobases.Add(infobase);
        }

        pinned.PopulateItems();
        groupNode.PopulateItems();
        noGroup.PopulateItems();

        var roots = new List<GroupNodeViewModel>();
        if (pinned.ContainsInfobases)
            roots.Add(pinned);
        if (noGroup.ContainsInfobases)
            roots.Add(noGroup);
        roots.Add(groupNode);
        return (roots, groupNode);
    }

    [Fact]
    public void PinFromRecent_AddsBaseToPinnedNodeOfAllTree_WithoutRebuild()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var recent = CreateBase("Из недавних");
        recent.LastLaunchDate = DateTime.UtcNow;

        var (roots, groupNode) = BuildAllTree(new[] { recent }, group);
        Assert.Single(roots); // пока закреплений нет — только группа
        Assert.Null(PinnedSectionSync.FindPinnedNode(roots));

        // Пользователь на вкладке «Недавние» нажимает «Закрепить».
        recent.IsPinned = true;
        var changed = PinnedSectionSync.Apply(roots, recent, CreatePinnedNode, orderInfobases: Order);

        Assert.True(changed);
        var pinned = PinnedSectionSync.FindPinnedNode(roots);
        Assert.NotNull(pinned);
        Assert.Same(pinned, roots[0]); // «Закреплённые» идут первыми
        Assert.True(pinned!.IsVisible); // при возврате к группам виден сразу
        Assert.Contains(recent, pinned.Infobases);
        Assert.Single(pinned.Items);
        var row = Assert.IsType<PinnedInfobaseItem>(pinned.Items[0]);
        Assert.Same(recent, row.Base);

        // Секции «Закреплённые» и обычный список сосуществуют: база остаётся и в группе.
        Assert.Contains(recent, groupNode.Infobases);
    }

    [Fact]
    public void UnpinFromRecent_RemovesBaseAndEmptyPinnedNode_WithoutRebuild()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var pinnedBase = CreateBase("Закреплённая ранее");
        pinnedBase.IsPinned = true;

        var (roots, groupNode) = BuildAllTree(new[] { pinnedBase }, group);
        Assert.NotNull(PinnedSectionSync.FindPinnedNode(roots));

        // Пользователь на вкладке «Недавние» снимает закрепление.
        pinnedBase.IsPinned = false;
        var changed = PinnedSectionSync.Apply(roots, pinnedBase, CreatePinnedNode, orderInfobases: Order);

        Assert.True(changed);
        Assert.Null(PinnedSectionSync.FindPinnedNode(roots));
        // База не исчезает из обычного списка — только из «Закреплённых».
        Assert.Contains(pinnedBase, groupNode.Infobases);
    }

    [Fact]
    public void RepeatedPin_DoesNotCreateDuplicateRows()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var pinnedBase = CreateBase("Закреплённая");
        pinnedBase.IsPinned = true;

        var (roots, _) = BuildAllTree(new[] { pinnedBase }, group);
        var pinned = PinnedSectionSync.FindPinnedNode(roots);
        Assert.NotNull(pinned);

        // Повторное применение того же состояния (двойной клик по флажку).
        var changed = PinnedSectionSync.Apply(roots, pinnedBase, CreatePinnedNode, orderInfobases: Order);

        Assert.False(changed);
        Assert.Single(pinned!.Infobases);
        Assert.Single(pinned.Items);
        Assert.Single(roots.Where(PinnedSectionSync.IsPinnedNode));
    }

    [Fact]
    public void PinSecondBase_AddsToExistingPinnedNode_InSortedOrder_WithoutDuplicates()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var first = CreateBase("A");
        first.IsPinned = true;
        var second = CreateBase("B");

        var (roots, _) = BuildAllTree(new[] { first, second }, group);

        second.IsPinned = true;
        PinnedSectionSync.Apply(roots, second, CreatePinnedNode, orderInfobases: Order);

        var pinned = PinnedSectionSync.FindPinnedNode(roots);
        Assert.NotNull(pinned);
        Assert.Equal(new[] { "A", "B" }, pinned!.Infobases.Select(i => i.Name));
        Assert.Equal(2, pinned.Items.Count);
        Assert.Equal(2, pinned.Items.Cast<PinnedInfobaseItem>().Select(r => r.Base).Distinct().Count());
    }

    [Fact]
    public void PinFromRecent_WhenSwitchingBackToAll_PinnedNodeBecomesVisible()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var recent = CreateBase("Из недавних");
        recent.LastLaunchDate = DateTime.UtcNow;

        var (roots, _) = BuildAllTree(new[] { recent }, group);
        // На вкладке «Недавние» виден плоский узел, корни групп скрыты (ApplyStableRoots).
        foreach (var root in roots)
            root.IsVisible = false;

        recent.IsPinned = true;
        PinnedSectionSync.Apply(roots, recent, CreatePinnedNode, orderInfobases: Order);

        // Возврат на «Все базы»: видимость пересчитывает ApplyStableRoots без пересборки.
        foreach (var root in roots)
            root.IsVisible = root.ContainsInfobases;

        var pinned = PinnedSectionSync.FindPinnedNode(roots);
        Assert.NotNull(pinned);
        Assert.True(pinned!.IsVisible);
        Assert.Contains(recent, pinned.Infobases);
    }

    [Fact]
    public void SortOwningNode_MovesPinnedRowToTopOfItsGroup_AndBackOnUnpin()
    {
        var group = new Group { Id = "g1", Name = "Группа" };
        var a = CreateBase("A");
        var b = CreateBase("B");

        var (roots, groupNode) = BuildAllTree(new[] { a, b }, group);
        Assert.Equal(new[] { "A", "B" }, groupNode.Infobases.Select(i => i.Name));

        b.IsPinned = true;
        PinnedSectionSync.SortOwningNode(roots, b, Order);
        Assert.Equal(new[] { "B", "A" }, groupNode.Infobases.Select(i => i.Name));

        b.IsPinned = false;
        PinnedSectionSync.SortOwningNode(roots, b, Order);
        Assert.Equal(new[] { "A", "B" }, groupNode.Infobases.Select(i => i.Name));
    }
}
