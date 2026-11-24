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

    /// <summary>
    /// Пустая/отсутствующая компоновка — стандартная (рабочая точка входа).
    /// Issue #146 (A4): в корне ещё верхнеуровневый «Инспектор процессов» и разделитель.
    /// </summary>
    [Fact]
    public void Migrate_EmptyOrCorruptLayout_ReturnsDefault()
    {
        Assert.Equal(UtilitiesMenuCatalog.RootSections.Count + 2,
            UtilitiesMenuLayouts.Migrate(null).Root.Count);
        Assert.Equal(UtilitiesMenuCatalog.RootSections.Count + 2,
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
    public void Migrate_Schema3ExplicitIcon_RestoresNestedLabels()
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
                    Children = new List<UtilitiesMenuNode>
                    {
                        new()
                        {
                            Kind = "group",
                            GroupId = UtilitiesMenuCatalog.GroupBackups,
                            DisplayMode = UtilitiesMenuGroupDisplay.Icon,
                            Children = new List<UtilitiesMenuNode>
                            {
                                new() { Kind = "item", ItemId = "DbmsBackup" }
                            }
                        }
                    }
                }
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);
        var backups = migrated.Root
            .First(n => n.GroupId == UtilitiesMenuCatalog.GroupServers)
            .Children.First(n => n.GroupId == UtilitiesMenuCatalog.GroupBackups);
        // Вложенные группы из схем < 4 снова показываются с подписью.
        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, backups.DisplayMode);
        Assert.True(UtilitiesMenuGroupPresentation.Build(backups, "Копии", isTopLevel: false).ShowLabel);
        // Стандартный раздел панели остаётся компактным (только иконка).
        var servers = migrated.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        Assert.Equal(UtilitiesMenuGroupDisplay.Icon, servers.DisplayMode);
        Assert.False(UtilitiesMenuGroupPresentation.Build(servers, "Серверы", isTopLevel: true).ShowLabel);
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

    /// <summary>
    /// Стандартная компоновка даёт четыре кнопки разделов в порядке каталога.
    /// Issue #146 (A3): в плане могут быть и верхнеуровневые пункты/разделители,
    /// поэтому кнопки разделов фильтруются по <see cref="UtilitiesSectionButtonKind.Group"/>.
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_VisibleSections_ReturnsFourButtons()
    {
        var plan = UtilitiesSectionMenuPlanner.Build(UtilitiesMenuLayouts.Default());
        var groups = plan.Where(b => b.Kind == UtilitiesSectionButtonKind.Group).ToList();

        Assert.Equal(UtilitiesMenuCatalog.RootSections,
            groups.Select(b => b.SectionId).ToArray());
        Assert.All(groups, b => Assert.False(b.IsCustom));
    }

    /// <summary>
    /// Плоская компоновка без групп даёт кнопки верхнеуровневых пунктов и в конце —
    /// запасную кнопку «Утилиты» (issue #146, A3): пункты не отнимают доступ к меню.
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_FlatRoot_ReturnsUtilitiesButton()
    {
        var layout = new UtilitiesMenuLayout
        {
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" },
                new() { Kind = "item", ItemId = "Services" }
            }
        };

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(3, plan.Count);
        Assert.Equal(2, plan.Count(b => b.Kind == UtilitiesSectionButtonKind.Item));
        Assert.All(plan.Take(2), b => Assert.Equal(b.ItemId, b.SectionId));
        Assert.Equal(UtilitiesSectionButtonKind.Group, plan[^1].Kind);
        Assert.True(plan[^1].IsCustom);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesSectionId, plan[^1].SectionId);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesTitleKey, plan[^1].TitleKey);
    }

    /// <summary>
    /// Issue #146: видимая пользовательская группа верхнего уровня даёт собственную
    /// кнопку (по NodeId/заголовку), а не запасную «Утилиты».
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_UserTopLevelGroup_ReturnsOwnButton()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Section(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ServerMonitor" }
                }),
                new()
                {
                    Kind = "group",
                    GroupId = null,
                    NodeId = "node-146",
                    IsTopLevel = true,
                    CustomTitle = "Моя папка",
                    Children = new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = "Services" }
                    }
                }
            }
        };

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, b => b.SectionId == UtilitiesMenuCatalog.GroupServers && !b.IsCustom);
        var custom = Assert.Single(plan, b => b.IsCustom);
        Assert.Equal("node-146", custom.NodeId);
        Assert.Equal("Моя папка", custom.Title);
        Assert.Equal("node-146", custom.SectionId);
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
    public void WpfPanel_UsesDynamicSectionButtonsAndQuickAppearanceButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Issue #146: статический набор кнопок заменён динамической панелью.
        Assert.Contains("x:Name=\"SectionButtonsPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ServersSectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnQuickThemeButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnQuickCompactButton_Click\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("UtilitiesSectionMenuPlanner.Build", code, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuLayouts.ExtractSection", code, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuLayouts.ExtractSectionByNodeId", code, StringComparison.Ordinal);
        // Режим «С надписью» применяется к кнопке панели через общий презентер.
        Assert.Contains("UtilitiesMenuGroupPresentation.Build", code, StringComparison.Ordinal);

        // Обработчик ПКМ объявлен рядом с меню разделов (MainWindow.Updates.cs).
        var updates = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("OnUtilitiesButton_PreviewMouseRightButtonDown", updates, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfSectionButtons_AreBuiltDynamicallyWithRightClickEditor()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("button.PreviewMouseRightButtonDown += OnUtilitiesButton_PreviewMouseRightButtonDown", code, StringComparison.Ordinal);
        Assert.Contains("SectionButtonsPanel.Children.Clear()", code, StringComparison.Ordinal);
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

    // ===================== 6. Иконка группы в правке структуры (T3) =====================

    /// <summary>
    /// Выбор узла, чей эффективный ключ иконки отсутствует в каталоге (новая
    /// верхнеуровневая папка), добавляет его в <see cref="UtilitiesMenuEditorViewModel.IconOptions"/>
    /// и выставляет <see cref="UtilitiesMenuEditorViewModel.SelectedIconKey"/> — поле
    /// настройки показывает иконку из дерева, а не первый элемент каталога (issue #146).
    /// </summary>
    [Fact]
    public void Editor_SelectNodeWithIconNotInCatalog_AddsIconOptionAndSelectsIt()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        var baseline = vm.IconOptions.Count;
        var item = new UtilitiesMenuNodeItem(
            new UtilitiesMenuNode { Kind = "group", GroupId = null, CustomTitle = "Новая папка" }, null);

        vm.SelectedItem = item;

        var effectiveKey = item.EffectiveIconKey;
        Assert.False(string.IsNullOrWhiteSpace(effectiveKey));
        // Эффективного ключа нет в каталоге — это и было причиной «чужой» иконки в поле.
        Assert.DoesNotContain(vm.CatalogIcons,
            o => string.Equals(o.Key, effectiveKey, StringComparison.Ordinal));
        Assert.Contains(vm.IconOptions,
            o => string.Equals(o.Key, effectiveKey, StringComparison.Ordinal));
        Assert.Equal(effectiveKey, vm.SelectedIconKey);
        Assert.Equal(baseline + 1, vm.IconOptions.Count);
    }

    /// <summary>
    /// Повторный выбор разных узлов не раздувает <see cref="UtilitiesMenuEditorViewModel.IconOptions"/>
    /// синтетическими вариантами: слот «текущая иконка» один, при выборе узла с
    /// каталожной иконкой он удаляется (issue #146).
    /// </summary>
    [Fact]
    public void Editor_SelectingDifferentNodes_KeepsSingleSyntheticIconOption()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        var baseline = vm.IconOptions.Count;
        var itemA = new UtilitiesMenuNodeItem(
            new UtilitiesMenuNode { Kind = "item", ItemId = "ServerMonitor", IconKey = "IconCustomA" }, null);
        var itemB = new UtilitiesMenuNodeItem(
            new UtilitiesMenuNode { Kind = "item", ItemId = "Services", IconKey = "IconCustomB" }, null);

        vm.SelectedItem = itemA;
        Assert.Equal(baseline + 1, vm.IconOptions.Count);
        Assert.Equal("IconCustomA", vm.SelectedIconKey);

        vm.SelectedItem = itemB;
        Assert.Equal(baseline + 1, vm.IconOptions.Count);
        Assert.DoesNotContain(vm.IconOptions, o => string.Equals(o.Key, "IconCustomA", StringComparison.Ordinal));
        Assert.Contains(vm.IconOptions, o => string.Equals(o.Key, "IconCustomB", StringComparison.Ordinal));
        Assert.Equal("IconCustomB", vm.SelectedIconKey);

        // Узел с иконкой из каталога: синтетический слот удаляется, коллекция — как была.
        var catalogItem = new UtilitiesMenuNodeItem(
            new UtilitiesMenuNode { Kind = "item", ItemId = "ServerMonitor" }, null);
        vm.SelectedItem = catalogItem;
        Assert.Equal(baseline, vm.IconOptions.Count);
        Assert.Equal(catalogItem.EffectiveIconKey, vm.SelectedIconKey);
    }

    /// <summary>
    /// Страж: WPF-редактор «Утилиты» передаёт диалогу эффективную иконку/цвет и
    /// привязывает список иконок к <c>IconOptions</c>, а не к статическому каталогу.
    /// </summary>
    [Fact]
    public void WpfUtilitiesEditor_PassesEffectiveIconAndBindsIconOptions()
    {
        var code = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.xaml.cs"));
        Assert.Contains("item.EffectiveIconKey", code, StringComparison.Ordinal);
        Assert.Contains("item.EffectiveIconColor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Node.IconKey", code, StringComparison.Ordinal);

        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.xaml"));
        Assert.Contains("ItemsSource=\"{Binding IconOptions}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding CatalogIcons}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Страж: Avalonia-редактор «Утилиты» использует <c>IconOptions</c> и передаёт
    /// диалогу эффективную иконку/цвет (issue #146, паритет с WPF).
    /// </summary>
    [Fact]
    public void AvaloniaUtilitiesEditor_PassesEffectiveIconAndUsesIconOptions()
    {
        var code = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs"));
        Assert.Contains("_iconCombo.ItemsSource = _vm.IconOptions", code, StringComparison.Ordinal);
        Assert.Contains("item.EffectiveIconKey", code, StringComparison.Ordinal);
        Assert.Contains("item.EffectiveIconColor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Node.IconKey", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Страж: диалог правки группы умеет добавлять текущую иконку в список, если её
    /// ключа нет в каталоге, и выбирать именно её (WPF и Avalonia).
    /// </summary>
    [Theory]
    [InlineData("UtilitiesGroupEditWindow.xaml.cs")]
    [InlineData("UtilitiesGroupEditWindow.Avalonia.cs")]
    public void GroupDialog_AddsCurrentIconWhenMissingFromCatalog(string fileName)
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));
        Assert.Contains("initialIconColor", code, StringComparison.Ordinal);
        Assert.Contains("new UtilitiesIconOption(", code, StringComparison.Ordinal);
        Assert.Contains("options.Any(", code, StringComparison.Ordinal);
        Assert.Contains("options.FirstOrDefault(", code, StringComparison.Ordinal);
    }

    // ===================== 7. Группы верхнего уровня (issue #146, схема 5) =====================

    /// <summary>
    /// Новая группа в корне получает стабильный NodeId и IsTopLevel=true; сохранение
    /// переносит оба поля в компоновку без изменений.
    /// </summary>
    [Fact]
    public void AddGroup_TopLevel_SetsNodeIdAndIsTopLevel()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Верхняя", null));
        var group = vm.SelectedItem!;

        Assert.True(group.Node.IsTopLevel);
        Assert.False(string.IsNullOrWhiteSpace(group.Node.NodeId));

        vm.SaveCommand.Execute(null);
        var saved = Assert.Single(vm.Layout.Root, n => n.CustomTitle == "Верхняя");
        Assert.True(saved.IsTopLevel);
        Assert.Equal(group.Node.NodeId, saved.NodeId);
    }

    /// <summary>Вложенная группа не становится группой верхнего уровня и не получает NodeId.</summary>
    [Fact]
    public void AddGroup_Nested_IsNotTopLevel()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внешняя", null));

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внутренняя", null));
        var inner = vm.SelectedItem!;

        Assert.False(inner.Node.IsTopLevel);
        Assert.True(string.IsNullOrWhiteSpace(inner.Node.NodeId));
    }

    /// <summary>
    /// Группа верхнего уровня после Save → Load → Migrate остаётся в корне и не
    /// переносится в «Утилиты» (основная причина issue #146).
    /// </summary>
    [Fact]
    public void TopLevelGroup_SaveLoadMigrate_StaysInRoot()
    {
        var dir = CreateTempDirectory();
        try
        {
            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var vm = new UtilitiesMenuEditorViewModel(store);
            vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Моя верхняя", null));
            var nodeId = vm.SelectedItem!.Node.NodeId;
            vm.SaveCommand.Execute(null);

            var loaded = store.Load();
            Assert.NotNull(loaded);
            var top = Assert.Single(loaded!.Root,
                n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));
            Assert.True(top.IsTopLevel);
            Assert.Equal("Моя верхняя", top.CustomTitle);

            // Повторная миграция ничего не переносит.
            var migrated = UtilitiesMenuLayouts.Migrate(loaded);
            Assert.Contains(migrated.Root, n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));
            Assert.DoesNotContain(migrated.Root, n =>
                string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupUtilities, StringComparison.Ordinal)
                && n.Children.Any(c => string.Equals(c.NodeId, nodeId, StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Несколько групп верхнего уровня сохраняются и остаются отдельными.</summary>
    [Fact]
    public void MultipleTopLevelGroups_SurviveRoundTrip()
    {
        var dir = CreateTempDirectory();
        try
        {
            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var vm = new UtilitiesMenuEditorViewModel(store);
            vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Первая", null));
            var first = vm.SelectedItem!.Node.NodeId;
            vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Вторая", null));
            var second = vm.SelectedItem!.Node.NodeId;
            vm.SaveCommand.Execute(null);

            var loaded = store.Load();
            Assert.NotNull(loaded);
            var ids = loaded!.Root
                .Where(n => n.Kind == "group" && n.IsTopLevel)
                .Select(n => n.NodeId)
                .ToList();
            Assert.Contains(first, ids);
            Assert.Contains(second, ids);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Легаси-схема (до 5) по-прежнему переносит пользовательские корневые группы в
    /// «Утилиты», но только один раз — повторная миграция не дублирует и не двигает.
    /// </summary>
    [Fact]
    public void LegacyLayout_MigratesUserRootIntoUtilitiesOnce()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 4,
            Root = new List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Group(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ServerMonitor" }
                }),
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
        Assert.DoesNotContain(migrated.Root,
            n => n.Kind == "group" && n.CustomTitle == "Моя папка");
        var utilities = Assert.Single(migrated.Root,
            n => n.Kind == "group" && n.GroupId == UtilitiesMenuCatalog.GroupUtilities);
        Assert.Single(utilities.Children, n => n.CustomTitle == "Моя папка");

        var second = UtilitiesMenuLayouts.Migrate(migrated);
        var utilities2 = Assert.Single(second.Root,
            n => n.Kind == "group" && n.GroupId == UtilitiesMenuCatalog.GroupUtilities);
        Assert.Single(utilities2.Children, n => n.CustomTitle == "Моя папка");
    }

    /// <summary>
    /// <see cref="UtilitiesMenuLayouts.ExtractSectionByNodeId"/> открывает пользовательскую
    /// группу по NodeId; скрытая группа не открывается.
    /// </summary>
    [Fact]
    public void ExtractSectionByNodeId_OpensUserGroup()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                new()
                {
                    Kind = "group",
                    NodeId = "node-open",
                    IsTopLevel = true,
                    CustomTitle = "Моя папка",
                    Children = new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = "ServerMonitor" }
                    }
                }
            }
        };

        var extracted = UtilitiesMenuLayouts.ExtractSectionByNodeId(layout, "node-open");
        Assert.NotNull(extracted);
        Assert.Equal("ServerMonitor", Assert.Single(extracted!.Root).ItemId);

        layout.Root[0].Visible = false;
        Assert.Null(UtilitiesMenuLayouts.ExtractSectionByNodeId(layout, "node-open"));
        Assert.Null(UtilitiesMenuLayouts.ExtractSectionByNodeId(layout, "no-such-node"));
    }

    /// <summary>
    /// Source-страж: AddGroup выставляет IsTopLevel/NodeId, миграция не трогает
    /// IsTopLevel, а обе платформы применяют UtilitiesMenuGroupPresentation к кнопкам.
    /// </summary>
    [Fact]
    public void SourceGuards_TopLevelGroupsAndPresentation()
    {
        var editor = ReadProjectFile(Path.Combine(
            "Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs"));
        Assert.Contains("NodeId = isTopLevel ? UtilitiesMenuLayouts.NewNodeId() : null", editor, StringComparison.Ordinal);
        Assert.Contains("IsTopLevel = isTopLevel", editor, StringComparison.Ordinal);
        Assert.Contains("item.Parent is null", editor, StringComparison.Ordinal);

        var catalog = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UtilitiesMenuCatalog.cs"));
        Assert.Contains("if (node.IsTopLevel)", catalog, StringComparison.Ordinal);
        Assert.Contains("if (layout.SchemaVersion >= CurrentSchemaVersion)", catalog, StringComparison.Ordinal);

        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("UtilitiesMenuGroupPresentation.Build(node, title, isTopLevel: true)", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("BuildSectionButtons", avalonia, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuGroupPresentation.Build(node, title, isTopLevel: true)", avalonia, StringComparison.Ordinal);
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
