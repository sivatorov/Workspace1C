using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #137 «Пункты подменю "Для группы" контекстного меню списка баз не работают»
/// (v1.0.1.79): при выбранном узле группы пункты «Назначить тег», «Переместить в
/// группу», «Добавить в Избранное», «Сменить платформу» молча ничего не делали.
/// <para>
/// Причина была двойной: обработчики обеих оболочек проверяли
/// <c>BatchOperationTargetCount</c> (счётчик только мультивыделения), а методы
/// ViewModel брали <c>BatchOperationTargets</c> (тоже без группы). Тесты фиксируют
/// чистую логику выбора целей (<see cref="BatchSelectionHelper.ResolveBatchTargets"/>)
/// и стражи по исходникам: обработчики и VM-методы обязаны использовать
/// group-aware вариант.
/// </para>
/// </summary>
public sealed class Issue137GroupBatchActionsTests
{
    // ===================== Чистая логика выбора целей =====================

    [Fact]
    public void ResolveBatchTargets_MultiSelection_WinsOverGroupContext()
    {
        var first = NewBase("Первая");
        var second = NewBase("Вторая");
        var groupBase = NewBase("Из группы");

        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 2,
            batchSelectedBases: new[] { first, second },
            hasGroupTarget: true,
            groupBases: new[] { groupBase },
            hasNoGroupTarget: false,
            noGroupBases: Array.Empty<Infobase>(),
            singleSelected: null);

        Assert.Equal(new[] { first, second }, targets);
    }

    [Fact]
    public void ResolveBatchTargets_GroupSelected_ReturnsGroupBases()
    {
        // Сценарий автора issue #137: выделен узел группы (без Ctrl/Shift),
        // мультивыделения нет — цели обязаны быть базами группы.
        var groupBase1 = NewBase("БГУ");
        var groupBase2 = NewBase("ЗКГУ");

        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 0,
            batchSelectedBases: Array.Empty<Infobase>(),
            hasGroupTarget: true,
            groupBases: new[] { groupBase1, groupBase2 },
            hasNoGroupTarget: false,
            noGroupBases: Array.Empty<Infobase>(),
            singleSelected: null);

        Assert.Equal(new[] { groupBase1, groupBase2 }, targets);
    }

    [Fact]
    public void ResolveBatchTargets_NoGroupNode_ReturnsBasesWithoutGroup()
    {
        // Issue #113: служебный узел «Без группы» — тоже групповой контекст.
        var noGroupBase = NewBase("Без группы");

        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 0,
            batchSelectedBases: Array.Empty<Infobase>(),
            hasGroupTarget: false,
            groupBases: Array.Empty<Infobase>(),
            hasNoGroupTarget: true,
            noGroupBases: new[] { noGroupBase },
            singleSelected: null);

        Assert.Equal(new[] { noGroupBase }, targets);
    }

    [Fact]
    public void ResolveBatchTargets_SingleSelected_ReturnsSingleBase()
    {
        var single = NewBase("Одна");

        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 0,
            batchSelectedBases: Array.Empty<Infobase>(),
            hasGroupTarget: false,
            groupBases: Array.Empty<Infobase>(),
            hasNoGroupTarget: false,
            noGroupBases: Array.Empty<Infobase>(),
            singleSelected: single);

        Assert.Equal(new[] { single }, targets);
    }

    [Fact]
    public void ResolveBatchTargets_NothingSelected_ReturnsEmpty()
    {
        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 0,
            batchSelectedBases: null,
            hasGroupTarget: false,
            groupBases: null,
            hasNoGroupTarget: false,
            noGroupBases: null,
            singleSelected: null);

        Assert.Empty(targets);
    }

    [Fact]
    public void ResolveBatchTargets_EmptyGroup_DoesNotFallBackToSingleBase()
    {
        // Группа без баз: цели пусты. Подставлять «случайно выделенную» базу нельзя —
        // операция «для группы» не должна задеть базу вне группы.
        var staleSingle = NewBase("Старое выделение");

        var targets = BatchSelectionHelper.ResolveBatchTargets(
            batchMarkedCount: 0,
            batchSelectedBases: Array.Empty<Infobase>(),
            hasGroupTarget: true,
            groupBases: Array.Empty<Infobase>(),
            hasNoGroupTarget: false,
            noGroupBases: Array.Empty<Infobase>(),
            singleSelected: staleSingle);

        Assert.Empty(targets);
    }

    // ===================== Стражи исходников =====================

    [Fact]
    public void WpfBatchHandlers_UseGroupAwareTargetCount()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));

        // Все 8 пакетных обработчиков блока «Для выделенных/Для группы» проверяют
        // цели с учётом группы (issue #137) — иначе пункт молча выходит.
        var groupAware = CountOccurrences(code, "BatchOperationTargetCountIncludingGroup");
        Assert.True(groupAware >= 8, $"Ожидалось не менее 8 group-aware guard'ов, найдено {groupAware}");

        // Прежний guard по мультивыделению в этих обработчиках не остался.
        Assert.DoesNotContain("_viewModel.BatchOperationTargetCount == 0", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaBatchHandlers_UseGroupAwareTargetCount()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        var groupAware = CountOccurrences(code, "BatchOperationTargetCountIncludingGroup");
        Assert.True(groupAware >= 8, $"Ожидалось не менее 8 group-aware guard'ов, найдено {groupAware}");

        Assert.DoesNotContain("_vm.BatchOperationTargetCount == 0", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchCommands_BothStacks_UseGroupAwareTargets()
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Workspace1C", "ViewModels", "MainViewModel.BatchCommands.cs"),
                     Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.BatchCommands.cs")
                 })
        {
            var code = ReadProjectFile(relativePath);

            // Пакетные методы работают по целям «мультивыделение либо группа».
            Assert.Contains("BatchOperationTargetsIncludingGroup", code, StringComparison.Ordinal);

            // Старый выбор целей без группы в этих методах не остался.
            Assert.DoesNotContain("BatchOperationTargets.ToList()", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BatchTargetsResolver_IsSharedWithViewModel()
    {
        var batch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Batch.cs"));
        Assert.Contains("BatchSelectionHelper.ResolveBatchTargets(", batch, StringComparison.Ordinal);

        // «Сменить платформу» и «Замена строк» для области «Выделенные» получают
        // кандидатов по тем же целям (issue #137) — иначе окно не открывается.
        var replace = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.ConnectionReplace.cs"));
        Assert.Contains("BatchOperationTargetsIncludingGroup", replace, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static Infobase NewBase(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings()
    };

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
