using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики окна выбора версии платформы (<see cref="PlatformVersionService"/>,
/// issue #304): фильтрация по разрядности и поиск узла дерева с учётом разрядности
/// и fallback на доступного предка при отфильтрованной версии.
/// </summary>
public sealed class PlatformVersionPickerTests
{
    // ======================= Фильтрация по разрядности =======================

    [Fact]
    public void FilterByArchitecture_WithoutSuffix_KeptInBothFilters()
    {
        var infos = Infos("8.5.1", "8.5.1 (64)", "8.5.1 (32)");

        var x64 = PlatformVersionService.FilterByArchitecture(infos, "x64");
        var x32 = PlatformVersionService.FilterByArchitecture(infos, "x32");

        // Вариант без явной разрядности показывается в обоих фильтрах (#304/#251):
        // ParseVariant считает его x32, но это лишь значение по умолчанию.
        Assert.Contains(x64, i => i.Display == "8.5.1");
        Assert.Contains(x64, i => i.Display == "8.5.1 (64)");
        Assert.DoesNotContain(x64, i => i.Display == "8.5.1 (32)");

        Assert.Contains(x32, i => i.Display == "8.5.1");
        Assert.Contains(x32, i => i.Display == "8.5.1 (32)");
        Assert.DoesNotContain(x32, i => i.Display == "8.5.1 (64)");
    }

    [Fact]
    public void FilterByArchitecture_All_ReturnsEverything()
    {
        var infos = Infos("8.5.1", "8.5.1 (64)", "8.5.1 (32)");
        var all = PlatformVersionService.FilterByArchitecture(infos, "all");
        Assert.Equal(3, all.Count);
    }

    // ======================= Поиск узла (FindBestNode) =======================

    [Fact]
    public void FindBestNode_PartialVersion_ReturnsBuildGroupFolder()
    {
        // «8.5.4» и «8.5.1» — частичные версии (по 3 сегмента): им соответствует
        // папка группы сборок, а не максимальная сборка в ней (#251).
        var tree = BuildTree("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (64)", "8.5.1 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void FindBestNode_BuildGroupFiltered_FallsBackToLine()
    {
        // 8.5.1 существует только как x32, а фильтр x64 её исключил — осталась линия «8.5»
        // с группой «8.5.4». Выделение должно осмысленно перейти на линию (#304).
        var filtered = PlatformVersionService.FilterByArchitecture(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");
        var tree = PlatformVersionService.BuildGroupedTree(filtered);

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5", node.Name);
        Assert.Equal(PlatformNodeKind.Line, node.Kind);
    }

    [Fact]
    public void FindBestNode_EmptyTree_ReturnsNull()
    {
        // Все версии 8.5.1 — x32; фильтр x64 исключает их полностью → дерево пусто.
        var empty = PlatformVersionService.BuildGroupedTree(new List<PlatformVersionInfo>());

        var node = PlatformVersionService.FindBestNode(empty, "8.5.1 (32)");

        Assert.Null(node);
    }

    [Fact]
    public void FindBestNode_FullVersionWithArch_ReturnsExactLeaf()
    {
        // Полная версия с явной разрядностью должна выбрать именно x64-лист,
        // а не первый попавшийся лист той же версии (x32 идёт вторым) (#304).
        var tree = BuildTree("8.5.1.1000 (64)", "8.5.1.1000 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (64)");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.Equal("8.5.1.1000 (64)", node.Variant);
    }

    [Fact]
    public void FindBestNode_FullVersionWithoutArch_ReturnsAnyLeafOfThatVersion()
    {
        // Версия без суффикса — любой лист той же версии (#251).
        var tree = BuildTree("8.5.1.1000 (64)", "8.5.1.1000 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.StartsWith("8.5.1.1000", node.Variant);
    }

    [Fact]
    public void FindBestNode_FullVersionArchFiltered_FallsBackToSameVersionOtherArch()
    {
        // В дереве x64 есть только 8.5.1.1000 (64), ищем 8.5.1.1000 (32) —
        // осмысленный fallback на доступную сборку той же версии (#304).
        var tree = BuildTree("8.5.1.1000 (64)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (32)");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.Equal("8.5.1.1000 (64)", node.Variant);
    }

    // ======================= Совпадение варианта (MatchesCurrent) =======================

    [Fact]
    public void MatchesCurrent_ExplicitArch_MustMatchSameArch()
    {
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1 (64)"));
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1 (32)"));
        // Разрядность теперь учитывается: лист x64 не совпадает с искомой x32 (#304).
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1 (32)"));
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1 (64)"));
    }

    [Fact]
    public void MatchesCurrent_NoArchInRequest_MatchesAnyLeaf()
    {
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1"));
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1"));
    }

    [Fact]
    public void MatchesCurrent_DifferentVersion_ReturnsFalse()
    {
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.4 (64)", "8.5.1 (64)"));
    }

    // ============ Сценарий #304: 8.5.4 → 8.5.1 → фильтр x64 ============

    [Fact]
    public void BuildGroupedTree_WithArchFilter_KeepsFolderWhenLineHasVisibleVariant()
    {
        // У 8.5.1 только x32-сборка, у 8.5.4 — x32 и x64. При фильтре x64 папка «8.5.1»
        // должна остаться в дереве (линия 8.5 видима), хотя её листья скрыты (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");

        var line = Assert.Single(tree);
        Assert.Equal("8.5", line.Name);
        Assert.Equal(PlatformNodeKind.Line, line.Kind);

        var group51 = Assert.Single(line.Children, g => g.Name == "8.5.1");
        Assert.Equal(PlatformNodeKind.BuildGroup, group51.Kind);
        Assert.Empty(group51.Children); // все листья 8.5.1 скрыты фильтром

        var group54 = Assert.Single(line.Children, g => g.Name == "8.5.4");
        Assert.Single(group54.Children); // остался только лист «8.5.4 (64)»
        Assert.Equal("8.5.4 (64)", group54.Children[0].Variant);
    }

    [Fact]
    public void BuildGroupedTree_WithArchFilter_HidesOtherArchLeaves()
    {
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1 (64)", "8.5.1 (32)"), "x64");

        var line = Assert.Single(tree);
        var group = Assert.Single(line.Children, g => g.Name == "8.5.1");
        var leaf = Assert.Single(group.Children);
        Assert.Equal("8.5.1 (64)", leaf.Variant);
    }

    [Fact]
    public void BuildGroupedTree_WithArchFilter_OmitsFullyFilteredLine()
    {
        // Вся линия 8.5 — только x32: при фильтре x64 её не показываем вовсе,
        // fallback бессмыслен (issue #304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1 (32)", "8.5.4 (32)"), "x64");

        Assert.Empty(tree);
    }

    [Fact]
    public void FindBestNode_PartialVersionInFilteredTree_ReturnsBuildGroupFolder()
    {
        // Главный сценарий: текущая 8.5.4, выбрана папка «8.5.1», переключаем фильтр
        // на x64 — выделение должно остаться на папке 8.5.1, а НЕ уходить на линию 8.5.
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void FindBestNode_LeafFullVersionFiltered_FallsBackToBuildGroupFolder()
    {
        // Выбран лист «8.5.1.1000 (32)»; у 8.5.1 нет x64-сборок — при фильтре x64
        // выделение переходит на папку группы сборок 8.5.1, а не на линию 8.5 (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1.1000 (32)", "8.5.4 (64)"), "x64");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (32)");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void Scenario_854To851SwitchX64_KeepsSelectionOn851()
    {
        // Полный сценарий из комментария @7OH: «Была 8.5.4, выбираю 8.5.1,
        // переключаю на х64 и выделяет 8.5». После исправления выделение остаётся
        // на папке 8.5.1.
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)");

        // Окно открыто с текущей версией 8.5.4 (64); пользователь выбрал папку 8.5.1,
        // затем переключил фильтр разрядности на x64 — RefreshTree строит дерево
        // из ПОЛНОГО списка версий с активным фильтром и восстанавливает выбор.
        var tree = PlatformVersionService.BuildGroupedTree(all, "x64");
        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
        Assert.DoesNotContain(tree, n => n.Name == "8.5" && n.Kind == PlatformNodeKind.BuildGroup);
    }

    // =================== Сценарий #304: папка/лист 8.5.1 при фильтре x64 ===================

    [Fact]
    public void Scenario_854Select851SwitchX64_NoX64Builds_SelectionAndResultStayOn851()
    {
        // (а) У 8.5.1 НЕТ x64-сборок: при фильтре x64 папка 8.5.1 остаётся в дереве
        // (пустой), FindBestNode возвращает её, а не линию 8.5; результат диалога —
        // чистая «8.5.1», а не несуществующая «8.5.1 (64)» (#304).
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)");

        // Пользователь выбрал лист «8.5.1 (x32)» или папку 8.5.1 → _currentVersion «8.5.1 (32)»/«8.5.1».
        foreach (var current in new[] { "8.5.1", "8.5.1 (32)" })
        {
            var tree = PlatformVersionService.BuildGroupedTree(all, "x64");
            var node = PlatformVersionService.FindBestNode(tree, current);

            Assert.NotNull(node);
            Assert.False(node!.IsLeaf);
            Assert.Equal("8.5.1", node.Name);
            Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);

            Assert.Equal("8.5.1", PlatformVersionService.BuildResultVariant(node, "x64"));
        }
    }

    [Fact]
    public void Scenario_854Select851SwitchX64_HasX64Builds_SelectionStaysOn851Leaf()
    {
        // (б) У 8.5.1 ЕСТЬ x64-сборка: лист «8.5.1 (64)» виден при фильтре x64;
        // восстановление по выбранному листу возвращает сам лист, а не папку/линию;
        // результат диалога — полный вариант «8.5.1 (64)» (#304, #142).
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (64)", "8.5.1 (32)");

        var tree = PlatformVersionService.BuildGroupedTree(all, "x64");
        var leaf = PlatformVersionService.FindBestNode(tree, "8.5.1 (64)");

        Assert.NotNull(leaf);
        Assert.True(leaf!.IsLeaf);
        Assert.Equal("8.5.1 (64)", leaf.Variant);
        Assert.Equal("8.5.1 (64)", PlatformVersionService.BuildResultVariant(leaf, "x64"));

        // Папка 8.5.1 тоже остаётся доступной и даёт суффикс x64 (в папке есть x64-лист).
        var folder = PlatformVersionService.FindBestNode(tree, "8.5.1");
        Assert.NotNull(folder);
        Assert.Equal("8.5.1", folder!.Name);
        Assert.Equal("8.5.1 (64)", PlatformVersionService.BuildResultVariant(folder, "x64"));
    }

    [Fact]
    public void FindBestNode_PartialWithExplicitArch_PrefersExactLeaf()
    {
        // «8.5.1 (64)» при наличии точного листа выбирает лист, а не папку группы (#304).
        var tree = BuildTree("8.5.1 (64)", "8.5.1 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1 (64)");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.Equal("8.5.1 (64)", node.Variant);
    }

    [Fact]
    public void FindBestNode_PartialWithExplicitArch_Filtered_FallsBackToFolderNotLine()
    {
        // Лист «8.5.1 (32)» скрыт фильтром x64 (у 8.5.1 нет x64-сборок): восстановление
        // должно идти на папку 8.5.1, а НЕ на линию 8.5 (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.1 (32)"), "x64");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1 (32)");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void FindBestNode_LineSelection_SwitchFilters_StaysOnLine()
    {
        // Выбор линии «8.5» не скачет: и в режиме «Все», и при фильтре x64/x32
        // восстановление возвращает саму линию 8.5 (#304).
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)");

        foreach (var filter in new[] { "all", "x64", "x32" })
        {
            var tree = PlatformVersionService.BuildGroupedTree(all, filter);
            var node = PlatformVersionService.FindBestNode(tree, "8.5");

            Assert.NotNull(node);
            Assert.False(node!.IsLeaf);
            Assert.Equal("8.5", node.Name);
            Assert.Equal(PlatformNodeKind.Line, node.Kind);
        }
    }

    // ==================== Результат диалога (BuildResultVariant) ====================

    [Fact]
    public void BuildResultVariant_EmptyFolderUnderFilter_ReturnsCleanVersion()
    {
        // Папка 8.5.1 пустая при фильтре x64 (только x32-сборки): результат «8.5.1»,
        // а не несуществующая «8.5.1 (64)» — иначе при создании базы лаунчер уходил
        // на другую x64-версию линии (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.1 (32)"), "x64");
        var folder = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.Equal("8.5.1", PlatformVersionService.BuildResultVariant(folder!, "x64"));
    }

    [Fact]
    public void BuildResultVariant_FolderWithMatchingArch_AddsSuffix()
    {
        // Папка 8.5.4 при фильтре x64 содержит видимый x64-лист: результат «8.5.4 (64)»
        // (регресс #142/#251 не ломаем — суффикс разрядности сохраняется).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.4 (32)"), "x64");
        var folder = PlatformVersionService.FindBestNode(tree, "8.5.4");

        Assert.Equal("8.5.4 (64)", PlatformVersionService.BuildResultVariant(folder!, "x64"));
    }

    [Fact]
    public void BuildResultVariant_LineWithVisibleX64_AddsSuffix()
    {
        // Линия 8.5 при фильтре x64 имеет видимые x64-листья в поддереве:
        // результат «8.5 (64)» (#142).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.1 (32)"), "x64");
        var line = PlatformVersionService.FindBestNode(tree, "8.5");

        Assert.Equal("8.5 (64)", PlatformVersionService.BuildResultVariant(line!, "x64"));
    }

    [Fact]
    public void BuildResultVariant_AllFilter_ReturnsCleanVersionForFolder()
    {
        // Режим «Все» (авто): папка даёт чистую частичную версию без суффикса (#251).
        var tree = BuildTree("8.5.4 (64)", "8.5.1 (32)");
        var folder = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.Equal("8.5.1", PlatformVersionService.BuildResultVariant(folder!, "all"));
    }

    [Fact]
    public void BuildResultVariant_Leaf_ReturnsFullVariant()
    {
        // Лист всегда отдаёт полный вариант с разрядностью (#142).
        var tree = BuildTree("8.5.1.1000 (64)", "8.5.1.1000 (32)");
        var leaf = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (64)");

        Assert.Equal("8.5.1.1000 (64)", PlatformVersionService.BuildResultVariant(leaf!, "all"));
    }

    // ============ Окончательный фикс #304: сохранение/восстановление по ключу ============

    [Fact]
    public void RestoreKey_ExactLeafAvailable_ReturnsSameLeafAfterRebuild()
    {
        // Выбран лист «8.5.1.1000 (32)» — ключ восстановления «версия+разрядность».
        // После перестроения дерева с тем же фильтром FindBestNode возвращает ТОТ ЖЕ
        // лист (не папку и не линию) — выделение не уходит наверх (#304).
        var all = Infos("8.5.1.1000 (64)", "8.5.1.1000 (32)");

        var treeAll = PlatformVersionService.BuildGroupedTree(all, "all");
        var selected = PlatformVersionService.FindBestNode(treeAll, "8.5.1.1000 (32)");
        Assert.NotNull(selected);
        Assert.True(selected!.IsLeaf);
        Assert.Equal("8.5.1.1000 (32)", selected.Variant);

        // Пользователь переключил фильтр на x32 — RefreshTree строит дерево заново.
        var treeX32 = PlatformVersionService.BuildGroupedTree(all, "x32");
        var restored = PlatformVersionService.FindBestNode(treeX32, selected.Variant!);

        Assert.NotNull(restored);
        Assert.True(restored!.IsLeaf);
        Assert.Equal("8.5.1.1000 (32)", restored.Variant);
    }

    [Fact]
    public void RestoreKey_SwitchArch_FindsSameVersionLeafNotTopLevel()
    {
        // Выбран лист «8.5.1.1000 (64)», пользователь переключил фильтр на x32 —
        // у версии есть x32-лист: восстановление должно найти лист 8.5.1.1000 (32)
        // (та же полная версия), а НЕ уйти на папку/линию (#304).
        var all = Infos("8.5.1.1000 (64)", "8.5.1.1000 (32)", "8.3.20.1000 (64)");

        var treeX32 = PlatformVersionService.BuildGroupedTree(all, "x32");
        var restored = PlatformVersionService.FindBestNode(treeX32, "8.5.1.1000 (64)");

        Assert.NotNull(restored);
        Assert.True(restored!.IsLeaf);
        Assert.Equal("8.5.1.1000 (32)", restored.Variant);
    }

    [Fact]
    public void RestoreKey_NoSameVersionUnderFilter_FallsBackToFolderOfSameFamily()
    {
        // У 8.5.1 только x32-сборки; фильтр x64 скрывает её листья, но папка 8.5.1
        // сохраняется (линия 8.5 видима): восстановление по ключу «8.5.1 (32)»
        // возвращает папку 8.5.1, а не первую линию дерева (#304).
        var all = Infos("8.3.20.1000 (64)", "8.5.4 (64)", "8.5.1 (32)");

        var treeX64 = PlatformVersionService.BuildGroupedTree(all, "x64");
        var restored = PlatformVersionService.FindBestNode(treeX64, "8.5.1 (32)");

        Assert.NotNull(restored);
        Assert.False(restored!.IsLeaf);
        Assert.Equal("8.5.1", restored.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, restored.Kind);
        Assert.Equal("8.5.1", PlatformVersionService.BuildResultVariant(restored, "x64"));
    }

    [Fact]
    public void RestoreKey_LineSelected_StaysOnSameLineUnderAnyFilter()
    {
        // Ключ восстановления для линии «8.5» во всех фильтрах возвращает саму линию
        // (не первый узел дерева) — «самый верх» не теряется (#304).
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)", "8.3.20.1000 (64)");

        foreach (var filter in new[] { "all", "x64", "x32" })
        {
            var tree = PlatformVersionService.BuildGroupedTree(all, filter);
            var restored = PlatformVersionService.FindBestNode(tree, "8.5");

            Assert.NotNull(restored);
            Assert.False(restored!.IsLeaf);
            Assert.Equal("8.5", restored.Name);
            Assert.Equal(PlatformNodeKind.Line, restored.Kind);
        }
    }

    // ======================= Тонкий клиент (issue #33) =======================

    [Fact]
    public void BuildGroupedTree_ThinClientLeaf_MarkedAndKept()
    {
#if LINUX
        return; // Linux-ветка BuildGroupedTree определяет тонкий клиент иначе
#endif
        // Версия, установленная как тонкий клиент (в bin только 1cv8c), должна
        // попадать в дерево выбора и быть помечена IsThinClient — UI подсветит её.
        var infos = new List<PlatformVersionInfo>
        {
            new() { Display = "8.3.27.2343 (32)", Path = "", IsThinClient = true },
            new() { Display = "8.3.26.1540 (64)", Path = "", IsThinClient = false }
        };

        var tree = PlatformVersionService.BuildGroupedTree(infos);

        var thinLeaf = FindLeaf(tree, "8.3.27.2343 (32)");
        Assert.NotNull(thinLeaf);
        Assert.True(thinLeaf!.IsThinClient);

        var fullLeaf = FindLeaf(tree, "8.3.26.1540 (64)");
        Assert.NotNull(fullLeaf);
        Assert.False(fullLeaf!.IsThinClient);
    }

    [Fact]
    public void BuildGroupedTree_ThinClientOnly_LineStillVisible()
    {
#if LINUX
        return; // Linux-ветка BuildGroupedTree определяет тонкий клиент иначе
#endif
        // Тонкий клиент — единственная версия линии: линия и группа сборок
        // сохраняются, лист помечен (не «пропадает» из дерева).
        var infos = new List<PlatformVersionInfo>
        {
            new() { Display = "8.3.27.2343 (32)", Path = "", IsThinClient = true }
        };

        var tree = PlatformVersionService.BuildGroupedTree(infos);

        Assert.Single(tree);
        Assert.Equal("8.3", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("8.3.27", tree[0].Children[0].Name);
        Assert.True(tree[0].Children[0].Children[0].IsThinClient);
    }

#if !LINUX
    [Fact]
    public void FindInstalledVersionInfos_ThinClientDir_Detected()
    {
        // Сканер Windows: каталог версии с одним 1cv8c.exe (без 1cv8.exe) —
        // тонкий клиент должен попадать в список и быть помечен (issue #33).
        using var temp = new TempDirectory();
        var binDir = Path.Combine(temp.Path, "1cv8", "8.3.27.2343", "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "1cv8c.exe"), string.Empty);

        var found = PlatformVersionService.FindInstalledVersionInfos(new[] { temp.Path });

        var thin = found.FirstOrDefault(v => v.Display.StartsWith("8.3.27.2343", StringComparison.Ordinal));
        Assert.NotNull(thin);
        Assert.True(thin!.IsThinClient);
    }

    [Fact]
    public void FindInstalledVersionInfos_FullClientDir_NotThin()
    {
        // Полный клиент (1cv8.exe есть) — НЕ тонкий клиент.
        using var temp = new TempDirectory();
        var binDir = Path.Combine(temp.Path, "1cv8", "8.3.26.1540", "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "1cv8.exe"), string.Empty);
        File.WriteAllText(Path.Combine(binDir, "1cv8c.exe"), string.Empty);

        var found = PlatformVersionService.FindInstalledVersionInfos(new[] { temp.Path });

        var full = found.FirstOrDefault(v => v.Display.StartsWith("8.3.26.1540", StringComparison.Ordinal));
        Assert.NotNull(full);
        Assert.False(full!.IsThinClient);
    }
#endif

    // ======================= Хелперы =======================

    private static List<PlatformVersionInfo> Infos(params string[] displays)
        => displays.Select(d => new PlatformVersionInfo { Display = d, Path = "" }).ToList();

    private static PlatformVersionGroup? FindLeaf(IReadOnlyList<PlatformVersionGroup> roots, string variant)
    {
        foreach (var root in roots)
        {
            foreach (var group in root.Children)
            {
                foreach (var leaf in group.Children)
                {
                    if (leaf.Variant == variant)
                        return leaf;
                }
            }
        }
        return null;
    }

    /// <summary>Временный каталог, удаляемый при освобождении.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cm-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Тестовые каталоги не критичны — оставляем как есть при сбое удаления.
            }
        }
    }

    private static List<PlatformVersionGroup> BuildTree(params string[] displays)
        => PlatformVersionService.BuildGroupedTree(Infos(displays));
}