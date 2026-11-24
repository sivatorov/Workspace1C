using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища компоновки меню «Утилиты» (issue #374): сохранение в файл
/// utilities_menu_layout.json (атомарная запись), чтение с нормализацией и
/// устойчивость к битому файлу.
/// </summary>
public sealed class UtilitiesMenuLayoutStoreTests : IDisposable
{
    private readonly string _tempDir;

    public UtilitiesMenuLayoutStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_utilities_menu_tests_" + Guid.NewGuid().ToString("N"));
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
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private UtilitiesMenuLayoutStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsLayout()
    {
        var store = CreateStore();
        var layout = UtilitiesMenuLayouts.Default();
        store.Save(layout);

        Assert.True(File.Exists(store.FilePath));
        var restored = store.Load();
        Assert.NotNull(restored);
        // Пункты модулей ПРО регистрируются глобально (UtilitiesMenuCatalog.RegisterExternal)
        // и могут быть активны в момент прогона из-за параллельных тестов. Тогда миграция
        // законно добавляет служебную группу «Новинки программы» в корень, поэтому
        // сравниваем пользовательскую часть компоновки, исключая служебную группу.
        static bool IsServiceGroup(UtilitiesMenuNode node) =>
            string.Equals(node.Kind, "group", StringComparison.Ordinal)
            && string.Equals(node.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal);
        Assert.Equal(
            layout.Root.Count(n => !IsServiceGroup(n)),
            restored!.Root.Count(n => !IsServiceGroup(n)));
        Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, restored.SchemaVersion);
        // Issue #146 (A4): первый узел корня — верхнеуровневый «Инспектор процессов»,
        // затем разделитель; разделы ищутся по GroupId, а не по индексу.
        AssertItem(restored.Root[0], "ProcessInspector");
        Assert.Equal("separator", restored.Root[1].Kind);
        // Первый раздел — «Серверы»; первый пункт — «Серверы 1С».
        var servers = restored.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        Assert.Equal("group", servers.Kind);
        AssertItem(servers.Children[0], "ServerMonitor");
        // Разделители сохраняются как самостоятельные узлы (issue #374).
        Assert.Equal("separator", servers.Children[3].Kind);
    }

    [Fact]
    public void Load_V1Layout_MigratesToSections_KeepsVisibilityAndIcons()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "CommandPalette", "Visible": false },
                { "Kind": "group", "GroupId": "Operations", "Visible": true, "Children": [
                    { "Kind": "item", "ItemId": "ExportCsv", "Visible": true, "IconColor": "#123456" }
                ] }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, result!.SchemaVersion);

        // Issue #146 (1.0.1.104): пользовательское дерево НЕ заменяется стандартными
        // разделами, но его корневые узлы переносятся в группу «Утилиты» с сохранением
        // порядка; недостающие функции каталога добавляются отдельно.
        var utilities = UtilitiesGroup(result);
        Assert.True(utilities.Children.Count >= 2);
        Assert.Equal("item", utilities.Children[0].Kind);
        Assert.Equal("CommandPalette", utilities.Children[0].ItemId);
        Assert.Equal("group", utilities.Children[1].Kind);
        Assert.Equal("Operations", utilities.Children[1].GroupId);

        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(result.Root, ids);
        Assert.Contains("CommandPalette", ids);
        Assert.Contains("ExportCsv", ids);
        // Отсутствовавшие функции каталога не теряются при миграции (issue #146).
        Assert.Contains("MetadataExplorer", ids);
        // Новинки каталога остаются доступными.
        Assert.Contains("TelegramBot", ids);

        var palette = FindItem(result.Root, "CommandPalette");
        Assert.NotNull(palette);
        Assert.False(palette!.Visible);
        var exportCsv = FindItem(result.Root, "ExportCsv");
        Assert.NotNull(exportCsv);
        Assert.Equal("#123456", exportCsv!.IconColor);
    }

    private static UtilitiesMenuNode? FindItem(
        System.Collections.Generic.IEnumerable<UtilitiesMenuNode>? nodes, string itemId)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal)
                && string.Equals(node.ItemId, itemId, StringComparison.Ordinal))
            {
                return node;
            }
            if (node.Children is { Count: > 0 } && FindItem(node.Children, itemId) is { } nested)
                return nested;
        }
        return null;
    }

    private static void AssertItem(UtilitiesMenuNode node, string itemId)
    {
        Assert.NotNull(node);
        Assert.Equal("item", node.Kind);
        Assert.Equal(itemId, node.ItemId);
    }

    /// <summary>
    /// Группа «Утилиты», в которую миграция 1.0.1.104 переносит пользовательские
    /// корневые узлы (issue #146).
    /// </summary>
    private static UtilitiesMenuNode UtilitiesGroup(UtilitiesMenuLayout layout)
    {
        var node = layout.Root.FirstOrDefault(n => n is not null
            && string.Equals(n.Kind, "group", StringComparison.Ordinal)
            && string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupUtilities, StringComparison.Ordinal));
        Assert.NotNull(node);
        return node!;
    }

    [Fact]
    public void Save_CreatesReadableJson_WithUtf8Cyrillic()
    {
        var store = CreateStore();
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                UtilitiesMenuLayouts.Group(UtilitiesMenuCatalog.GroupLockOptions, new System.Collections.Generic.List<UtilitiesMenuNode>
                {
                    UtilitiesMenuLayouts.Item("UnlockPrivateBases")
                })
            }
        };
        store.Save(layout);

        var text = File.ReadAllText(store.FilePath);
        // Кириллица записывается читаемыми символами, а не \uXXXX.
        Assert.DoesNotContain("\\u", text);
        // Ключи свойств в JSON — имена свойств модели.
        Assert.Contains("ItemId", text);
        Assert.Contains("Root", text);
    }

    [Fact]
    public void Load_BrokenFile_ReturnsNull_AndDoesNotThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ this is not valid json");

        var result = store.Load();
        Assert.Null(result);
        // Битый файл на диске не трогаем.
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_FileWithUnknownItem_DropsUnknownNode()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 2,
              "Root": [
                { "Kind": "item", "ItemId": "CommandPalette", "Visible": true },
                { "Kind": "item", "ItemId": "UnknownItemId", "Visible": true },
                { "Kind": "group", "GroupId": "LockOptions", "Visible": true, "Children": [] }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        // Неизвестный пункт отброшен, пользовательские узлы перенесены в «Утилиты».
        var utilities = UtilitiesGroup(result!);
        Assert.Equal(2, utilities.Children.Count);
        Assert.Equal("CommandPalette", utilities.Children[0].ItemId);
        Assert.Equal("group", utilities.Children[1].Kind);
    }

    [Fact]
    public void Load_FileWithNullCollections_Normalizes()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 2,
              "Root": [
                { "Kind": "group", "GroupId": "Operations", "Children": null }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        var utilities = UtilitiesGroup(result!);
        var operations = utilities.Children.FirstOrDefault(n =>
            string.Equals(n.GroupId, "Operations", StringComparison.Ordinal));
        Assert.NotNull(operations);
        Assert.NotNull(operations!.Children);
        Assert.Empty(operations.Children!);
    }

    [Fact]
    public void Load_FileWithSeparatorNodes_KeepsThem()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 2,
              "Root": [
                { "Kind": "item", "ItemId": "CommandPalette", "Visible": true },
                { "Kind": "separator", "Visible": true },
                { "Kind": "item", "ItemId": "ActualReleases", "Visible": true }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        var utilities = UtilitiesGroup(result!);
        Assert.True(utilities.Children.Count >= 3);
        Assert.Equal("separator", utilities.Children[1].Kind);
        Assert.Equal("ActualReleases", utilities.Children[2].ItemId);
    }

    [Fact]
    public void Load_FileWithSeparatorBefore_MigratesToSeparatorNode()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 2,
              "Root": [
                { "Kind": "item", "ItemId": "CommandPalette", "Visible": true },
                { "Kind": "item", "ItemId": "ActualReleases", "Visible": true, "SeparatorBefore": true }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        var utilities = UtilitiesGroup(result!);
        Assert.True(utilities.Children.Count >= 3);
        Assert.Equal("separator", utilities.Children[1].Kind);
        Assert.Equal("ActualReleases", utilities.Children[2].ItemId);
        Assert.False(utilities.Children[2].SeparatorBefore);
    }

    [Fact]
    public void Save_ThenLoad_WithSeparator_RoundTrips()
    {
        var store = CreateStore();
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                UtilitiesMenuLayouts.Separator(),
                UtilitiesMenuLayouts.Item("ActualReleases")
            }
        };
        store.Save(layout);

        var restored = store.Load();
        Assert.NotNull(restored);
        // Issue #146 (схема 5): сохранённые в корне пункты не переносятся в «Утилиты»;
        // миграция может лишь добавить служебную группу «Новинки программы» в конец.
        Assert.True(restored!.Root.Count >= 3);
        Assert.Equal("CommandPalette", restored.Root[0].ItemId);
        Assert.Equal("separator", restored.Root[1].Kind);
        Assert.Equal("ActualReleases", restored.Root[2].ItemId);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = CreateStore();
        var first = new UtilitiesMenuLayout();
        first.Root.Add(UtilitiesMenuLayouts.Item("CommandPalette"));
        store.Save(first);

        var second = new UtilitiesMenuLayout();
        second.Root.Add(UtilitiesMenuLayouts.Item("ActualReleases"));
        store.Save(second);

        // Файл перезаписан: старый пункт исчез из JSON. При чтении миграция может
        // снова добавить недостающие пункты каталога, поэтому проверяем сырой файл.
        var text = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("CommandPalette", text);
        Assert.Contains("ActualReleases", text);

        var restored = store.Load();
        Assert.NotNull(restored);
        // Issue #146 (схема 5): второй сохранённый корневой пункт — на первом месте.
        Assert.Equal("ActualReleases", restored!.Root[0].ItemId);
    }
}