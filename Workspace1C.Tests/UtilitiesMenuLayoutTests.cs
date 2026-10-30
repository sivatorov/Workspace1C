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

    /// <summary>Стандартная компоновка идентична текущему статическому меню «Утилиты» (обратная совместимость).
    /// Разделители — самостоятельные узлы <c>Kind == "separator"</c> (issue #374).</summary>
    [Fact]
    public void DefaultLayout_MatchesCurrentStaticMenu()
    {
        var layout = UtilitiesMenuLayouts.Default();

        // Корень: 17 узлов — 12 пунктов/групп + 5 узлов-разделителей (issue #374):
        // перед «Актуальные релизы», «Блокировка приложения», «Информация», «Операции», «Проверить обновления».
        // «Управление службами» (issue #11) стоит сразу после «Серверы 1С».
        Assert.Equal(17, layout.Root.Count);

        // ---- Верхний уровень ----
        AssertItem(layout.Root[0], "CommandPalette");
        AssertSeparator(layout.Root[1]);
        AssertItem(layout.Root[2], "ActualReleases");
        AssertItem(layout.Root[3], "PlatformUpdate");
        AssertItem(layout.Root[4], "PlatformDownload");
        AssertItem(layout.Root[5], "ServerMonitor");
        AssertItem(layout.Root[6], "Services");
        AssertItem(layout.Root[7], "ServerConsoleExternal");
        AssertSeparator(layout.Root[8]);

        // «Блокировка приложения» на верхнем уровне, перед подменю (issue #294).
        AssertItem(layout.Root[9], "AppLock");

        // ---- Подменю «Опции блокировки» ----
        AssertGroup(layout.Root[10], UtilitiesMenuCatalog.GroupLockOptions);
        var lockChildren = layout.Root[10].Children;
        Assert.Equal(4, lockChildren.Count);
        AssertItem(lockChildren[0], "UnlockPrivateBases");
        AssertItem(lockChildren[1], "ChangeAppLock");
        AssertSeparator(lockChildren[2]);
        AssertItem(lockChildren[3], "RunBackupScenario");

        // ---- Подменю «Информация» ----
        AssertSeparator(layout.Root[11]);
        AssertGroup(layout.Root[12], UtilitiesMenuCatalog.GroupInformation);
        var infoChildren = layout.Root[12].Children;
        Assert.Equal(11, infoChildren.Count);
        AssertItem(infoChildren[0], "MaintenanceCenter");
        AssertSeparator(infoChildren[1]);
        AssertItem(infoChildren[2], "ConfigTypesEdit");
        AssertItem(infoChildren[3], "ItsAccounts");
        // Уведомления в Telegram и по email (выпуск 0.3.12.69).
        AssertItem(infoChildren[4], "NotificationChannels");
        AssertSeparator(infoChildren[5]);
        AssertItem(infoChildren[6], "ScheduledTasks");
        AssertItem(infoChildren[7], "BackupScenarios");
        AssertSeparator(infoChildren[8]);
        AssertItem(infoChildren[9], "ScriptsSettings");
        AssertItem(infoChildren[10], "UsageStatistics");

        // ---- Подменю «Операции» ----
        AssertSeparator(layout.Root[13]);
        AssertGroup(layout.Root[14], UtilitiesMenuCatalog.GroupOperations);
        var opsChildren = layout.Root[14].Children;
        // Ожидаемый состав подменю «Операции» по порядку (null — разделитель).
        // Новые функции 1.0.0.69–1.0.0.71 — сразу после «Обозревателя метаданных»;
        // «Пользователи ИБ» (1.0.0.83) — после «Расширений конфигурации».
        // «Публикация на веб-сервере» перенесена в контекстное меню базы (issue #63);
        // «Блокировка сеансов ИБ» удалена из меню (issue #66); экспорт/импорт *.v8i
        // (issue #29); импорт и миграция кластера — отдельные пункты (issue #41).
        var expected = new string?[]
        {
            "ConfigDiff", "MetadataExplorer", "EventLog", "ConfigExtensions", "InfobaseUsers", "InfobaseScheduledJobs",
            "GitVersioning", "TechLog", "ServerAlerts", "DbmsBackup", "TelegramBot", "SharedLists",
            null,
            "RepositoryBatchUpdate", "RepositoryBrowser",
            null,
            // Массовые операции по списку баз (issue #91).
            "BatchDumpDt", "BatchLoadCf",
            null,
            "ExportCsv", "ExportHtml", "ExportJson", "ImportJson", "ExportV8iFile", "ImportV8iFile",
            "ClusterImport", "ClusterMigration",
            null,
            "ProcessInspector", "KillOneCProcesses",
            null,
            "ConnectionReplace", "UndoConnectionReplace", "RemoveMissingFileBases"
        };
        Assert.Equal(expected.Length, opsChildren.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] is null)
                AssertSeparator(opsChildren[i]);
            else
                AssertItem(opsChildren[i], expected[i]!);
        }

        // ---- «Проверить обновления» в конце, с разделителем ----
        AssertSeparator(layout.Root[15]);
        AssertItem(layout.Root[16], "CheckForUpdates");
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

        // В стандартной компоновке — сразу после «Импорта из кластера 1С».
        var layout = UtilitiesMenuLayouts.Default();
        var ops = layout.Root
            .First(n => string.Equals(n.Kind, "group", StringComparison.Ordinal)
                && string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupOperations, StringComparison.Ordinal));
        var ids = ops.Children.Select(n => n.ItemId).ToList();
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