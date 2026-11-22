using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 «Сломанное меню Утилиты» (релиз 1.0.1.102): миграция пользовательской
/// компоновки без потери групп/порядка/видимости/иконок, отсутствие дублирующего
/// подменю раздела, учёт видимости разделов в кнопках панели, рабочая точка входа
/// «Утилиты» для плоской компоновки, мгновенная перестройка по событию изменения
/// компоновки, возвращённые быстрые кнопки темы/компактности и паритет WPF/Avalonia.
/// </summary>
public sealed class Issue146UtilitiesMenuTests
{
    // ===================== 1. Миграция сохраняет пользовательское дерево =====================

    /// <summary>
    /// Пользовательские группы, их названия и порядок сохраняются: миграция больше
    /// не заменяет дерево на <see cref="UtilitiesMenuLayouts.Default"/>. С 1.0.1.104
    /// (issue #146, комментарий 7OH) пользовательские узлы не остаются «подвешенными»
    /// в корне, а переносятся в группу «Утилиты» в исходном порядке.
    /// </summary>
    [Fact]
    public void Migrate_PreservesCustomGroupsAndOrder()
    {
        var layout = BuildV1Layout();

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, migrated.SchemaVersion);
        // Пользовательские узлы перенесены в группу «Утилиты», порядок сохранён.
        var utilities = migrated.Root.FirstOrDefault(n =>
            n.Kind == "group" && n.GroupId == UtilitiesMenuCatalog.GroupUtilities);
        Assert.NotNull(utilities);
        Assert.Equal("item", utilities!.Children[0].Kind);
        Assert.Equal("ServerMonitor", utilities.Children[0].ItemId);
        Assert.Equal("group", utilities.Children[1].Kind);
        Assert.Equal("Моя папка", utilities.Children[1].CustomTitle);
        Assert.Equal("group", utilities.Children[2].Kind);
        Assert.Equal(UtilitiesMenuCatalog.GroupLockOptions, utilities.Children[2].GroupId);
        Assert.Equal("Старые блокировки", utilities.Children[2].CustomTitle);

        // Порядок детей пользовательской группы сохранён.
        Assert.Equal(new[] { "Services", "ActualReleases" },
            utilities.Children[1].Children.Where(n => n.Kind == "item").Select(n => n.ItemId).ToArray());
    }

    /// <summary>
    /// Видимость, иконка, цвет и внешний файл иконки пункта переносятся по <c>ItemId</c>.
    /// </summary>
    [Fact]
    public void Migrate_PreservesItemVisibilityAndIcons()
    {
        var migrated = UtilitiesMenuLayouts.Migrate(BuildV1Layout());

        var services = FindNode(migrated.Root, n => n.ItemId == "Services");
        Assert.NotNull(services);
        Assert.False(services!.Visible);
        Assert.Equal("Cog", services.IconKey);
        Assert.Equal("#ABCDEF", services.IconColor);
        Assert.Equal(@"C:\icons\services.png", services.ExternalIconPath);
    }

    /// <summary>Миграция не дублирует пункты: каждый <c>ItemId</c> встречается один раз.</summary>
    [Fact]
    public void Migrate_DoesNotDuplicateItems()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" },
                new() { Kind = "item", ItemId = "ServerMonitor" },
                UtilitiesMenuLayouts.Group(null!, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ServerMonitor" }
                })
            }
        };
        layout.Root[2].GroupId = null;

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        var ids = new List<string>();
        CollectItemIds(migrated.Root, ids);
        Assert.Single(ids.Where(id => id == "ServerMonitor"));
    }

    /// <summary>
    /// Отсутствующие пункты каталога не теряются: новинки попадают в «Новинки программы»,
    /// остальные — в «Новинки» либо в существующую подходящую группу.
    /// </summary>
    [Fact]
    public void Migrate_AddsMissingNewItemsToNewItemsGroup()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" }
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        var newItems = migrated.Root.FirstOrDefault(n =>
            n.Kind == "group" && n.GroupId == UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(newItems);
        Assert.Contains(newItems!.Children, n => n.ItemId == "NotificationChannels");
        Assert.Contains(newItems.Children, n => n.ItemId == "CollectDiagnostics");

        // Не-новинка, которой не было в дереве, тоже не потеряна (в «Новинках» или группе).
        var ids = new List<string>();
        CollectItemIds(migrated.Root, ids);
        Assert.Contains("CheckForUpdates", ids);
    }

    /// <summary>Пустая/отсутствующая компоновка — стандартная (рабочая точка входа).</summary>
    [Fact]
    public void Migrate_EmptyOrCorruptLayout_ReturnsDefault()
    {
        Assert.Equal(UtilitiesMenuCatalog.RootSections.Count,
            UtilitiesMenuLayouts.Migrate(null).Root.Count);
        Assert.Equal(UtilitiesMenuCatalog.RootSections.Count,
            UtilitiesMenuLayouts.Migrate(new UtilitiesMenuLayout { SchemaVersion = 1 }).Root.Count);
    }

    /// <summary>
    /// 1.0.1.105: группы по умолчанию показываются с подписью — исправление
    /// регресса 1.0.104, когда в меню оставались только иконки.
    /// </summary>
    [Fact]
    public void GroupDisplay_DefaultsToIconWithLabel()
    {
        var node = new UtilitiesMenuNode { Kind = "group", GroupId = null, CustomTitle = "Группа" };
        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, node.DisplayMode);

        var layout = UtilitiesMenuGroupPresentation.Build(node, "Группа");
        Assert.True(layout.ShowLabel);
        Assert.Equal("Группа", layout.Header);
    }

    /// <summary>
    /// 1.0.1.105: компоновка, сохранённая 1.0.104 с явным DisplayMode=Icon,
    /// однократно переводится на режим «С надписью».
    /// </summary>
    [Fact]
    public void Migrate_Schema3ExplicitIcon_RestoresLabels()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 3,
            Root = new List<UtilitiesMenuNode>
            {
                new()
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupServers,
                    DisplayMode = UtilitiesMenuGroupDisplay.Icon,
                    Children = new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = "ServerMonitor" }
                    }
                }
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);
        var servers = migrated.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, servers.DisplayMode);
        Assert.True(UtilitiesMenuGroupPresentation.Build(servers, "Серверы").ShowLabel);
    }

    /// <summary>
    /// 1.0.1.105: в актуальной схеме явный выбор «Иконка» сохраняется.
    /// </summary>
    [Fact]
    public void Migrate_CurrentSchema_KeepsExplicitIcon()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                new()
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupServers,
                    DisplayMode = UtilitiesMenuGroupDisplay.Icon,
                    Children = new List<UtilitiesMenuNode>()
                }
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);
        var servers = migrated.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        Assert.Equal(UtilitiesMenuGroupDisplay.Icon, servers.DisplayMode);
        Assert.False(UtilitiesMenuGroupPresentation.Build(servers, "Серверы").ShowLabel);
    }

    /// <summary>
    /// Хранилище делает резервную копию исходного файла перед миграцией схемы 1 → 2.
    /// </summary>
    [Fact]
    public void LayoutStore_Migration_WritesBackup()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, UtilitiesMenuLayoutStore.FileName);
            var original = """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "ServerMonitor", "Visible": true }
              ]
            }
            """;
            File.WriteAllText(path, original);

            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var loaded = store.Load();

            Assert.NotNull(loaded);
            var backup = Path.Combine(dir, UtilitiesMenuLayoutStore.BackupFileName);
            Assert.True(File.Exists(backup), "Резервная копия перед миграцией не создана");
            Assert.Contains("ServerMonitor", File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ===================== 2. Видимость разделов =====================

    /// <summary>Скрытый раздел не открывается (нет содержимого для меню кнопки).</summary>
    [Fact]
    public void ExtractSection_HiddenSection_ReturnsNull()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var tools = layout.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupTools);
        tools.Visible = false;

        Assert.Null(UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupTools));
        // Видимый раздел по-прежнему открывается.
        Assert.NotNull(UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupServers));
    }

    /// <summary>Скрытый «Инструменты» не даёт кнопку на панели; остальные — дают.</summary>
    [Fact]
    public void SectionMenuPlanner_HiddenTools_NotInPlan()
    {
        var layout = UtilitiesMenuLayouts.Default();
        layout.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupTools).Visible = false;

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.DoesNotContain(plan, b => b.SectionId == UtilitiesMenuCatalog.GroupTools);
        Assert.Contains(plan, b => b.SectionId == UtilitiesMenuCatalog.GroupServers);
        Assert.Contains(plan, b => b.SectionId == UtilitiesMenuCatalog.GroupMaintenance);
        Assert.Contains(plan, b => b.SectionId == UtilitiesMenuCatalog.GroupMore);
    }

    /// <summary>Стандартная компоновка даёт четыре кнопки разделов в порядке каталога.</summary>
    [Fact]
    public void SectionMenuPlanner_VisibleSections_ReturnsFourButtons()
    {
        var plan = UtilitiesSectionMenuPlanner.Build(UtilitiesMenuLayouts.Default());

        Assert.Equal(UtilitiesMenuCatalog.RootSections,
            plan.Select(b => b.SectionId).ToArray());
        Assert.All(plan, b => Assert.False(b.IsCustom));
    }

    /// <summary>Пользовательская компоновка без разделов даёт одну кнопку «Утилиты».</summary>
    [Fact]
    public void SectionMenuPlanner_CustomRoot_ReturnsUtilitiesButton()
    {
        var layout = new UtilitiesMenuLayout
        {
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" },
                UtilitiesMenuLayouts.Group(null!, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "Services" }
                })
            }
        };
        layout.Root[1].GroupId = null;
        layout.Root[1].CustomTitle = "Моя папка";

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Single(plan);
        Assert.True(plan[0].IsCustom);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesSectionId, plan[0].SectionId);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesTitleKey, plan[0].TitleKey);
    }

    /// <summary>Все разделы скрыты — остаётся рабочая кнопка «Утилиты».</summary>
    [Fact]
    public void SectionMenuPlanner_AllHidden_ReturnsUtilitiesButton()
    {
        var layout = UtilitiesMenuLayouts.Default();
        foreach (var node in layout.Root)
            node.Visible = false;

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Single(plan);
        Assert.True(plan[0].IsCustom);
    }

    // ===================== 3. Автовключение правки и настройки =====================

    /// <summary>
    /// При наличии файла компоновки и отсутствии явного флага правка включается
    /// при загрузке настроек и флаг сохраняется.
    /// </summary>
    [Fact]
    public void Resolve_ExistingLayout_EnablesEditingOnMigration()
    {
        var dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(dir, UtilitiesMenuLayoutStore.FileName), """
            { "SchemaVersion": 2, "Root": [ { "Kind": "item", "ItemId": "ServerMonitor" } ] }
            """);

            var settings = new InfobaseRepository(directory: dir).LoadSettings();

            Assert.True(settings.UtilitiesMenuEditingEnabled);
            Assert.True(settings.UtilitiesMenuEditingEnabledInitialized);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Явно сохранённое выключение правки уважается даже при наличии файла.</summary>
    [Fact]
    public void Resolve_ExplicitDisabled_IsRespected()
    {
        var dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), """
            { "UtilitiesMenuEditingEnabled": false, "UtilitiesMenuEditingEnabledInitialized": true }
            """);
            File.WriteAllText(Path.Combine(dir, UtilitiesMenuLayoutStore.FileName), """
            { "SchemaVersion": 2, "Root": [ { "Kind": "item", "ItemId": "ServerMonitor" } ] }
            """);

            var settings = new InfobaseRepository(directory: dir).LoadSettings();

            Assert.False(settings.UtilitiesMenuEditingEnabled);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Галочка правки меню сохраняется и читается обратно (round-trip).</summary>
    [Fact]
    public void Settings_ToggleUtilitiesMenuEditing_RoundTrip()
    {
        var settings = new AppSettings
        {
            UtilitiesMenuEditingEnabled = true,
            UtilitiesMenuEditingEnabledInitialized = true
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.True(restored!.UtilitiesMenuEditingEnabled);
        Assert.True(restored.UtilitiesMenuEditingEnabledInitialized);
    }

    // ===================== 4. Перестройка без перезапуска =====================

    /// <summary>Сохранение в редакторе поднимает событие перестройки меню/кнопок.</summary>
    [Fact]
    public void EditorSave_RaisesLayoutChanged()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        UtilitiesMenuEvents.LayoutChanged += handler;
        try
        {
            var vm = new UtilitiesMenuEditorViewModel(store: null);
            vm.SaveCommand.Execute(null);
            Assert.Equal(1, raised);
        }
        finally
        {
            UtilitiesMenuEvents.LayoutChanged -= handler;
        }
    }

    // ===================== 5. Source-гварды (паритет WPF/Avalonia) =====================

    [Fact]
    public void WpfPanel_UsesPlannerAndHasQuickAppearanceButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"ServersSectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MaintenanceSectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolsSectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MoreSectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UtilitiesFallbackButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnQuickThemeButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnQuickCompactButton_Click\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("UtilitiesSectionMenuPlanner.Build", code, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuLayouts.ExtractSection", code, StringComparison.Ordinal);

        // Обработчик ПКМ объявлен рядом с меню разделов (MainWindow.Updates.cs).
        var updates = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("OnUtilitiesButton_PreviewMouseRightButtonDown", updates, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfSectionButtons_AllHaveRightClickEditor()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // ПКМ-редактор привязан к каждой из четырёх кнопок разделов и к «Утилитам».
        var count = CountOccurrences(xaml,
            "PreviewMouseRightButtonDown=\"OnUtilitiesButton_PreviewMouseRightButtonDown\"");
        Assert.True(count >= 5, $"Ожидалось ≥5 кнопок с ПКМ-редактором, найдено: {count}");
    }

    [Fact]
    public void AvaloniaPanel_UsesPlannerAndHasQuickAppearanceButtons()
    {
        var panel = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("UtilitiesSectionMenuPlanner.Build", panel, StringComparison.Ordinal);
        Assert.Contains("ToggleThemeCommand", panel, StringComparison.Ordinal);
        Assert.Contains("ToggleCompactCommand", panel, StringComparison.Ordinal);
        Assert.Contains("IsRightButtonPressed", panel, StringComparison.Ordinal);
        Assert.Contains("OpenUtilitiesMenuEditorDirectly", panel, StringComparison.Ordinal);

        var menu = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("UtilitiesSectionMenuPlanner.IsUtilitiesSection", menu, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuLayouts.ExtractSection", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void RebuildSubscriptions_ExistOnBothPlatforms()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));
        Assert.Contains("UtilitiesMenuEvents.LayoutChanged", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("UtilitiesMenuEvents.LayoutChanged", avalonia, StringComparison.Ordinal);

        var editor = ReadProjectFile(Path.Combine(
            "Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs"));
        Assert.Contains("UtilitiesMenuEvents.RaiseLayoutChanged", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasUtilitiesFallbackTitle_InBothLanguages()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var json = ReadProjectFile(Path.Combine(
                "Workspace1C", "Localization", "Languages", language + ".json"));
            using var doc = JsonDocument.Parse(json);
            var strings = doc.RootElement.GetProperty("strings");
            Assert.True(strings.TryGetProperty(UtilitiesSectionMenuPlanner.UtilitiesTitleKey, out var value)
                        && value.GetString() is { Length: > 0 },
                $"{language}.json: нет ключа {UtilitiesSectionMenuPlanner.UtilitiesTitleKey}");
        }
    }

    // ===================== Вспомогательные методы =====================

    private static UtilitiesMenuLayout BuildV1Layout() => new()
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
                IconKey = "Folder",
                IconColor = "#123456",
                Children = new List<UtilitiesMenuNode>
                {
                    new()
                    {
                        Kind = "item",
                        ItemId = "Services",
                        Visible = false,
                        IconKey = "Cog",
                        IconColor = "#ABCDEF",
                        ExternalIconPath = @"C:\icons\services.png"
                    },
                    new() { Kind = "item", ItemId = "ActualReleases" }
                }
            },
            new()
            {
                Kind = "group",
                GroupId = UtilitiesMenuCatalog.GroupLockOptions,
                CustomTitle = "Старые блокировки",
                Children = new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "AppLock" }
                }
            }
        }
    };

    private static UtilitiesMenuNode? FindNode(
        IEnumerable<UtilitiesMenuNode>? nodes, Func<UtilitiesMenuNode, bool> predicate)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (predicate(node))
                return node;
            var found = FindNode(node.Children, predicate);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static void CollectItemIds(IEnumerable<UtilitiesMenuNode>? nodes, ICollection<string> target)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (node.Kind == "item" && !string.IsNullOrWhiteSpace(node.ItemId))
                target.Add(node.ItemId!);
            CollectItemIds(node.Children, target);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue146-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
}
