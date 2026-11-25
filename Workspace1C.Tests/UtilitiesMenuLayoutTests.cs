using System.Text.Json;
using System.Text.Json.Serialization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты модели компоновки меню «Утилиты» (issue #374): стандартная компоновка
/// воспроизводит текущее статическое меню (состав, порядок, группы, разделители),
/// а JSON-сериализация/десериализация сохраняет структуру.
/// </summary>
public sealed class UtilitiesMenuLayoutTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Стандартная компоновка — четыре сценарных раздела первого уровня
    /// («Серверы», «Обслуживание», «Инструменты», «Ещё»), внутри — подгруппы.
    /// Разделители — самостоятельные узлы <c>Kind == "separator"</c> (issue #374).
    /// Issue #146 (A3): в корне могут быть и верхнеуровневые пункты/разделители,
    /// поэтому разделы ищутся по <c>GroupId</c>, а не по индексам Root[0..3].
    /// </summary>
    [Fact]
    public void DefaultLayout_HasFourScenarioSections()
    {
        var layout = UtilitiesMenuLayouts.Default();

        Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, layout.SchemaVersion);
        var serversNode = layout.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        var maintenanceNode = layout.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupMaintenance);
        var toolsNode = layout.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupTools);
        var moreNode = layout.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupMore);
        AssertGroup(serversNode, UtilitiesMenuCatalog.GroupServers);
        AssertGroup(maintenanceNode, UtilitiesMenuCatalog.GroupMaintenance);
        AssertGroup(toolsNode, UtilitiesMenuCatalog.GroupTools);
        AssertGroup(moreNode, UtilitiesMenuCatalog.GroupMore);

        // ---- Серверы: кластеры и консоль ----
        var servers = serversNode.Children;
        AssertItem(servers[0], "ServerMonitor");
        AssertItem(servers[1], "Services");
        AssertItem(servers[2], "ServerConsoleExternal");
        AssertSeparator(servers[3]);
        AssertItem(servers[4], "ServerConsoleRegister");
        AssertSeparator(servers[5]);
        AssertItem(servers[6], "ClusterImport");
        AssertItem(servers[7], "ClusterMigration");

        // ---- Обслуживание: копии, планировщик, диагностика, процессы ----
        var maintenance = maintenanceNode.Children;
        Assert.Equal(7, maintenance.Count);
        AssertGroup(maintenance[0], UtilitiesMenuCatalog.GroupBackups);
        AssertSeparator(maintenance[1]);
        AssertGroup(maintenance[2], UtilitiesMenuCatalog.GroupSchedule);
        AssertSeparator(maintenance[3]);
        AssertGroup(maintenance[4], UtilitiesMenuCatalog.GroupDiagnostics);
        AssertSeparator(maintenance[5]);
        AssertGroup(maintenance[6], UtilitiesMenuCatalog.GroupProcesses);
        Assert.Contains(maintenance[0].Children, n => n.ItemId == "DbmsBackup");
        Assert.Contains(maintenance[4].Children, n => n.ItemId == "CollectDiagnostics");
        // Команды панели, перенесённые в разделы (навигация 1.0.1.99).
        Assert.Contains(maintenance[4].Children, n => n.ItemId == "ClearCache");
        Assert.Contains(maintenance[6].Children, n => n.ItemId == "CheckAvailability");

        // ---- Инструменты: сравнение, журналы, списки баз, разработка, сервисы, ПРО ----
        var tools = toolsNode.Children;
        // Issue #146 (T8): группа «ПРО» — обычный узел компоновки в конце раздела.
        Assert.Equal(11, tools.Count);
        AssertGroup(tools[0], UtilitiesMenuCatalog.GroupCompare);
        AssertGroup(tools[2], UtilitiesMenuCatalog.GroupJournals);
        AssertGroup(tools[4], UtilitiesMenuCatalog.GroupBaseLists);
        AssertGroup(tools[6], UtilitiesMenuCatalog.GroupDevelopment);
        AssertGroup(tools[8], UtilitiesMenuCatalog.GroupProServices);
        Assert.Contains(tools[8].Children, n => n.ItemId == "TelegramBot");
        AssertGroup(tools[10], UtilitiesMenuCatalog.GroupPro);
        Assert.Contains(tools[4].Children, n => n.ItemId == "ExportV8iFile");
        Assert.Contains(tools[4].Children, n => n.ItemId == "SyncIbases");

        // ---- Ещё: обновления, блокировка, справка ----
        var more = moreNode.Children;
        Assert.Equal(5, more.Count);
        AssertGroup(more[0], UtilitiesMenuCatalog.GroupUpdates);
        AssertGroup(more[2], UtilitiesMenuCatalog.GroupApp);
        AssertGroup(more[4], UtilitiesMenuCatalog.GroupHelp);
        Assert.Contains(more[4].Children, n => n.ItemId == "License");
        Assert.Contains(more[2].Children, n => n.ItemId == "AppLock");
        Assert.Contains(more[2].Children, n => n.ItemId == "ToggleTheme");
        Assert.Contains(more[2].Children, n => n.ItemId == "ToggleCompact");
        Assert.Contains(more[2].Children, n => n.ItemId == "SwitchUser");
    }

    /// <summary>
    /// Ни одна функция не потеряна: все пункты плоского меню версии 1.0.1.98
    /// присутствуют в новой компоновке (проверка по явному списку идентификаторов).
    /// </summary>
    [Fact]
    public void DefaultLayout_ContainsEveryV1Item()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        string[] v1Items =
        {
            "CommandPalette", "ActualReleases", "PlatformUpdate", "PlatformDownload",
            "ServerMonitor", "Services", "ServerConsoleExternal", "ServerConsoleRegister",
            "AppLock", "UnlockPrivateBases", "ChangeAppLock", "RunBackupScenario",
            "MaintenanceCenter", "ConfigTypesEdit", "ItsAccounts", "NotificationChannels",
            "ScheduledTasks", "BackupScenarios", "ScriptsSettings", "UsageStatistics",
            "CollectDiagnostics", "ConfigDiff", "MetadataExplorer", "EventLog",
            "ConfigExtensions", "InfobaseUsers", "InfobaseScheduledJobs", "GitVersioning",
            "TechLog", "ServerAlerts", "DbmsBackup", "TelegramBot", "SharedLists",
            "RepositoryBatchUpdate", "RepositoryBrowser", "BatchDumpDt", "BatchLoadCf",
            "ExportCsv", "ExportHtml", "ExportJson", "ImportJson", "ExportV8iFile",
            "ImportV8iFile", "ClusterImport", "ClusterMigration", "KillOneCProcesses",
            "ConnectionReplace", "UndoConnectionReplace", "RemoveMissingFileBases",
            "CheckForUpdates", "License"
        };
        foreach (var id in v1Items)
            Assert.Contains(id, ids);
        Assert.Equal(v1Items.Length, v1Items.Distinct().Count());
    }

    /// <summary>Каталог содержит все пункты стандартной компоновки (ни один ItemId не теряется).</summary>
    [Fact]
    public void Catalog_ContainsAllDefaultLayoutItems()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        foreach (var id in ids)
        {
            Assert.NotNull(UtilitiesMenuCatalog.Find(id));
        }
        // В каталоге больше пунктов, чем в стандартной компоновке? Нет: компоновка
        // включает все пункты каталога, кроме пометок «новинка» (сейчас их нет).
        Assert.True(ids.Count <= UtilitiesMenuCatalog.All.Count);
    }

    /// <summary>
    /// «Миграция между кластерами 1С» (issue #41, переработка): отдельный пункт каталога
    /// рядом с «Импортом из кластера 1С», БЕЗ хоткея (не частая операция — комментарий 7OH).
    /// </summary>
    [Fact]
    public void ClusterMigration_IsSeparateItem_WithoutHotkey()
    {
        var item = UtilitiesMenuCatalog.Find("ClusterMigration");
        Assert.NotNull(item);
        Assert.Equal(UtilitiesMenuCommandKind.MigrateClusterInfobases, item!.CommandKind);
        Assert.Null(item.HotkeyKey); // без хоткея
        Assert.Equal("ClusterMigration.MenuItem", item.TextKey);

        // В стандартной компоновке — в разделе «Серверы», сразу после «Импорта из кластера 1С».
        var layout = UtilitiesMenuLayouts.Default();
        var servers = FindGroupById(layout.Root, UtilitiesMenuCatalog.GroupServers);
        Assert.NotNull(servers);
        var ids = servers!.Children.Select(n => n.ItemId).ToList();
        var importIndex = ids.IndexOf("ClusterImport");
        var migrationIndex = ids.IndexOf("ClusterMigration");
        Assert.True(importIndex >= 0);
        Assert.Equal(importIndex + 1, migrationIndex);
    }

    /// <summary>
    /// «Миграция между кластерами 1С» помечена «новинкой» (issue #58): иначе
    /// при сохранённой пользовательской компоновке пункт не попадает в подменю
    /// «Новинки программы», а сам пункт теряется из виду.
    /// </summary>
    [Fact]
    public void ClusterMigration_IsMarkedAsNew()
    {
        var item = UtilitiesMenuCatalog.Find("ClusterMigration");
        Assert.NotNull(item);
        Assert.True(item!.IsNew);
    }

    /// <summary>
    /// «Новинки программы» (issue #4): пункт-новинка, отсутствующий в пользовательской
    /// компоновке, собирается <see cref="UtilitiesMenuResolver.CollectNewItems"/> — из него
    /// построитель меню добавляет пункт в группу «Новинки» (issue #58).
    /// </summary>
    [Fact]
    public void CollectNewItems_ReturnsClusterMigration_WhenAbsentFromCustomLayout()
    {
        var layout = UtilitiesMenuLayouts.Default();
        RemoveItem(layout, "ClusterMigration"); // пользовательская компоновка короче каталога

        var newItems = UtilitiesMenuResolver.CollectNewItems(layout);

        Assert.Contains(newItems, i => string.Equals(i.Id, "ClusterMigration", StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="UtilitiesMenuResolver.EnsureNewItemsGroup"/> создаёт/включает видимую группу
    /// «Новинки программы» с неразмещённым пунктом миграции — видимость группы определяется
    /// наличием новинок, а не галочкой в редакторе (issue #4, регресс #58).
    /// </summary>
    [Fact]
    public void EnsureNewItemsGroup_AddsClusterMigrationIntoVisibleGroup()
    {
        var layout = UtilitiesMenuLayouts.Default();
        RemoveItem(layout, "ClusterMigration");

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);
        Assert.NotNull(result);

        var newGroup = FindGroupById(result!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(newGroup);
        Assert.True(newGroup!.Visible);
        Assert.Contains(newGroup.Children, c => string.Equals(c.ItemId, "ClusterMigration", StringComparison.Ordinal));
    }

    /// <summary>
    /// Регресс-инвариант: каждый пункт каталога, который НЕ помечен «новинкой», обязан
    /// присутствовать в стандартной компоновке — иначе пункт может «потеряться» и из
    /// рабочего меню, и из «Новинок» (issue #58).
    /// </summary>
    [Fact]
    public void DefaultLayout_ContainsAllNonNewCatalogItems()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (item.IsNew)
                continue;
            Assert.Contains(item.Id, ids);
        }
    }

    /// <summary>
    /// Issue #146: миграция схемы 1 → 2 сохраняет пользовательские группы и порядок,
    /// а не заменяет дерево стандартной компоновкой. С 1.0.1.104 пользовательские
    /// корневые узлы переносятся в группу «Утилиты» (комментарий 7OH).
    /// </summary>
    [Fact]
    public void Migrate_V1_PreservesCustomGroupsAndOrder()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" },
                new()
                {
                    Kind = "group",
                    GroupId = null,
                    CustomTitle = "Моя папка",
                    Children = new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = "Services" }
                    }
                }
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        var utilities = FindGroupById(migrated.Root, UtilitiesMenuCatalog.GroupUtilities);
        Assert.NotNull(utilities);
        Assert.Equal("ServerMonitor", utilities!.Children[0].ItemId);
        Assert.Equal("Моя папка", utilities.Children[1].CustomTitle);
        Assert.Equal("Services", utilities.Children[1].Children[0].ItemId);
    }

    /// <summary>Issue #146: скрытый раздел не открывается кнопкой на панели.</summary>
    [Fact]
    public void ExtractSection_HiddenSection_ReturnsNull()
    {
        var layout = UtilitiesMenuLayouts.Default();
        layout.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupTools).Visible = false;

        Assert.Null(UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupTools));
        Assert.NotNull(UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupServers));
    }

    private static void RemoveItem(UtilitiesMenuLayout layout, string itemId)
    {
        static bool RemoveFrom(List<UtilitiesMenuNode> nodes, string id)
        {
            for (var i = nodes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(nodes[i].ItemId, id, StringComparison.Ordinal))
                {
                    nodes.RemoveAt(i);
                    return true;
                }
                if (nodes[i].Children?.Count > 0 && RemoveFrom(nodes[i].Children, id))
                    return true;
            }
            return false;
        }

        RemoveFrom(layout.Root, itemId);
    }

    private static UtilitiesMenuNode? FindGroupById(IEnumerable<UtilitiesMenuNode>? nodes, string groupId)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal) &&
                string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            {
                return node;
            }
            if (node.Children?.Count > 0 &&
                FindGroupById(node.Children, groupId) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    /// <summary>JSON-сериализация и обратная десериализация сохраняют структуру компоновки.</summary>
    [Fact]
    public void Json_RoundTrip_PreservesStructure()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        var restored = JsonSerializer.Deserialize<UtilitiesMenuLayout>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(layout.SchemaVersion, restored!.SchemaVersion);
        Assert.Equal(layout.Root.Count, restored.Root.Count);
        for (var i = 0; i < layout.Root.Count; i++)
        {
            var original = layout.Root[i];
            var copy = restored.Root[i];
            Assert.Equal(original.Kind, copy.Kind);
            Assert.Equal(original.ItemId, copy.ItemId);
            Assert.Equal(original.GroupId, copy.GroupId);
            Assert.Equal(original.SeparatorBefore, copy.SeparatorBefore);
            Assert.Equal(original.Visible, copy.Visible);
            Assert.Equal(original.Children.Count, copy.Children.Count);
        }
    }

    /// <summary>JSON читается с учётом регистра имён свойств (ручная правка файла).</summary>
    [Fact]
    public void Json_Deserialize_IsCaseInsensitive()
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "Root": [
                {
                  "Kind": "item",
                  "ItemId": "CommandPalette",
                  "Visible": true
                },
                {
                  "Kind": "group",
                  "GroupId": "LockOptions",
                  "Visible": true,
                  "Children": [
                    { "Kind": "item", "ItemId": "UnlockPrivateBases", "Visible": true }
                  ]
                }
              ]
            }
            """;
        var layout = JsonSerializer.Deserialize<UtilitiesMenuLayout>(json, JsonOptions);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Root.Count);
        Assert.Equal("CommandPalette", layout.Root[0].ItemId);
        Assert.Equal("group", layout.Root[1].Kind);
        Assert.Single(layout.Root[1].Children);
        Assert.Equal("UnlockPrivateBases", layout.Root[1].Children[0].ItemId);
    }

    private static void AssertItem(UtilitiesMenuNode node, string itemId)
    {
        Assert.NotNull(node);
        Assert.Equal("item", node.Kind);
        Assert.Equal(itemId, node.ItemId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }

    private static void AssertGroup(UtilitiesMenuNode node, string groupId)
    {
        Assert.NotNull(node);
        Assert.Equal("group", node.Kind);
        Assert.Equal(groupId, node.GroupId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }

    private static void AssertSeparator(UtilitiesMenuNode node)
    {
        Assert.NotNull(node);
        Assert.Equal("separator", node.Kind);
        Assert.Null(node.ItemId);
        Assert.Null(node.GroupId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }
}