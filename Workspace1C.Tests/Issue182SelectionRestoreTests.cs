using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #182 «Активная при закрытии строка»: после перезапуска должна восстанавливаться
/// последняя активная строка (база или группа) и прокрутка к ней.
/// <para>
/// Здесь покрыта платформенно-независимая часть исправления:
/// <see cref="LastSelectionRestorePlanner"/> (приоритет базы над группой, пустые
/// значения, отсутствующая база, путь группы), разбор цепочки предков для раскрытия
/// свёрнутых групп и сохранение/восстановление значений через
/// <see cref="AppSettings"/> / <see cref="InfobaseRepository"/>.
/// </para>
/// </summary>
public sealed class Issue182SelectionRestoreTests : IDisposable
{
    private readonly string _tempDir;

    public Issue182SelectionRestoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_issue182_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Каталог мог остаться занятым — тест уже завершён.
        }
    }

    // ===================== Планировщик цели =====================

    [Fact]
    public void Plan_InfobaseHasPriorityOverGroup()
    {
        var plan = LastSelectionRestorePlanner.Plan(
            lastSelectedInfobaseId: "base-1",
            lastSelectedGroupPath: "A / B",
            infobaseExists: _ => true);

        Assert.Equal(LastSelectionTargetKind.Infobase, plan.Kind);
        Assert.Equal("base-1", plan.Value);
        Assert.False(plan.IsNone);
    }

    [Fact]
    public void Plan_EmptyValues_ReturnsNone()
    {
        var plan = LastSelectionRestorePlanner.Plan(string.Empty, string.Empty, _ => true);

        Assert.Equal(LastSelectionTargetKind.None, plan.Kind);
        Assert.True(plan.IsNone);
        Assert.Equal(string.Empty, plan.Value);
    }

    [Fact]
    public void Plan_NullValues_ReturnsNone()
    {
        var plan = LastSelectionRestorePlanner.Plan(null, null);

        Assert.Equal(LastSelectionTargetKind.None, plan.Kind);
        Assert.True(plan.IsNone);
    }

    [Fact]
    public void Plan_MissingInfobase_FallsBackToGroupPath()
    {
        // Базу удалили/пересоздали — Id больше не существует, но группа осталась.
        var plan = LastSelectionRestorePlanner.Plan(
            lastSelectedInfobaseId: "deleted-base",
            lastSelectedGroupPath: "A / B",
            infobaseExists: id => string.Equals(id, "another-base", StringComparison.Ordinal));

        Assert.Equal(LastSelectionTargetKind.Group, plan.Kind);
        Assert.Equal("A / B", plan.Value);
    }

    [Fact]
    public void Plan_MissingInfobaseWithoutGroup_ReturnsNone()
    {
        var plan = LastSelectionRestorePlanner.Plan(
            lastSelectedInfobaseId: "deleted-base",
            lastSelectedGroupPath: string.Empty,
            infobaseExists: _ => false);

        Assert.Equal(LastSelectionTargetKind.None, plan.Kind);
        Assert.True(plan.IsNone);
    }

    [Fact]
    public void Plan_GroupPathOnly_ReturnsGroup()
    {
        var plan = LastSelectionRestorePlanner.Plan(
            lastSelectedInfobaseId: string.Empty,
            lastSelectedGroupPath: "Отдел / Бухгалтерия",
            infobaseExists: _ => true);

        Assert.Equal(LastSelectionTargetKind.Group, plan.Kind);
        Assert.Equal("Отдел / Бухгалтерия", plan.Value);
    }

    [Fact]
    public void Plan_TrimsWhitespaceAroundSavedValues()
    {
        var plan = LastSelectionRestorePlanner.Plan("  base-7  ", "  A / B  ", _ => true);

        Assert.Equal(LastSelectionTargetKind.Infobase, plan.Kind);
        Assert.Equal("base-7", plan.Value);

        var groupPlan = LastSelectionRestorePlanner.Plan("   ", "  A / B  ", _ => true);
        Assert.Equal(LastSelectionTargetKind.Group, groupPlan.Kind);
        Assert.Equal("A / B", groupPlan.Value);
    }

    // ===================== Раскрытие предков =====================

    [Fact]
    public void GetGroupAncestorPaths_ReturnsCumulativeChain()
    {
        var paths = LastSelectionRestorePlanner.GetGroupAncestorPaths("A / B / C");

        Assert.Equal(new[] { "A", "A / B", "A / B / C" }, paths);
    }

    [Fact]
    public void GetGroupAncestorPaths_HandlesSingleLevelAndEmpty()
    {
        Assert.Equal(new[] { "A" }, LastSelectionRestorePlanner.GetGroupAncestorPaths("A"));
        Assert.Empty(LastSelectionRestorePlanner.GetGroupAncestorPaths("   "));
        Assert.Empty(LastSelectionRestorePlanner.GetGroupAncestorPaths(null));
    }

    [Fact]
    public void GetGroupAncestorPaths_IgnoresEmptySegments()
    {
        var paths = LastSelectionRestorePlanner.GetGroupAncestorPaths("A /  / B");

        Assert.Equal(new[] { "A", "A / B" }, paths);
    }

    // ===================== AppSettings: сохранение/восстановление =====================

    [Fact]
    public void AppSettings_StoresLastSelectionFields()
    {
        var settings = new AppSettings
        {
            LastSelectedInfobaseId = "base-42",
            LastSelectedGroupPath = string.Empty
        };

        var plan = LastSelectionRestorePlanner.Plan(
            settings.LastSelectedInfobaseId,
            settings.LastSelectedGroupPath,
            _ => true);

        Assert.Equal(LastSelectionTargetKind.Infobase, plan.Kind);
        Assert.Equal("base-42", plan.Value);
    }

    [Fact]
    public void Repository_RoundTripsLastSelectedInfobaseId()
    {
        var repository = new InfobaseRepository(directory: _tempDir);
        repository.SaveSettings(new AppSettings
        {
            LastSelectedInfobaseId = "base-42",
            LastSelectedGroupPath = string.Empty
        });

        var loaded = repository.LoadSettings();

        Assert.Equal("base-42", loaded.LastSelectedInfobaseId);
        Assert.Equal(string.Empty, loaded.LastSelectedGroupPath);

        var plan = LastSelectionRestorePlanner.Plan(
            loaded.LastSelectedInfobaseId,
            loaded.LastSelectedGroupPath,
            id => string.Equals(id, "base-42", StringComparison.Ordinal));
        Assert.Equal(LastSelectionTargetKind.Infobase, plan.Kind);
        Assert.Equal("base-42", plan.Value);
    }

    [Fact]
    public void Repository_RoundTripsLastSelectedGroupPath()
    {
        var repository = new InfobaseRepository(directory: _tempDir);
        repository.SaveSettings(new AppSettings
        {
            LastSelectedInfobaseId = string.Empty,
            LastSelectedGroupPath = "Отдел / Бухгалтерия"
        });

        var loaded = repository.LoadSettings();

        Assert.Equal("Отдел / Бухгалтерия", loaded.LastSelectedGroupPath);

        var plan = LastSelectionRestorePlanner.Plan(
            loaded.LastSelectedInfobaseId,
            loaded.LastSelectedGroupPath,
            _ => true);
        Assert.Equal(LastSelectionTargetKind.Group, plan.Kind);
        Assert.Equal("Отдел / Бухгалтерия", plan.Value);
    }

    // ===================== Flush отложенного сохранения =====================

    [Fact]
    public void Debouncer_CancelPending_CancelsScheduledToken()
    {
        var debouncer = new SettingsSaveDebouncer();
        var cts = debouncer.Schedule();

        Assert.True(debouncer.CancelPending());
        Assert.True(cts.IsCancellationRequested);

        // Повторный сброс без ожидающего сохранения безопасен.
        Assert.False(debouncer.CancelPending());
    }

    [Fact]
    public void Debouncer_CancelPending_KeepsOnlyLatestScheduledToken()
    {
        var debouncer = new SettingsSaveDebouncer();
        var first = debouncer.Schedule();
        var second = debouncer.Schedule();

        Assert.True(first.IsCancellationRequested);

        Assert.True(debouncer.CancelPending());
        Assert.True(second.IsCancellationRequested);
    }
}
