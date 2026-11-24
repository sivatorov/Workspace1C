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
/// Issue #146, задача A3 (релиз 1.0.1.116): вынос пунктов и разделителей меню «Утилиты»
/// на верхний уровень. Проверяются модель <see cref="UtilitiesMenuNode.IsTopLevel"/>,
/// планировщик кнопок панели (<see cref="UtilitiesSectionMenuPlanner"/>), каталог
/// (<see cref="UtilitiesMenuLayouts.AddMissingCatalogItems"/>), сохранение/загрузка
/// компоновки схемы 5 и рендер панелей WPF/Avalonia (source-guard).
/// </summary>
public sealed class Issue146TopLevelItemsTests
{
    // ===================== 1. Планировщик кнопок =====================

    /// <summary>Верхнеуровневый пункт даёт запись кнопки-иконки с идентификатором пункта.</summary>
    [Fact]
    public void SectionMenuPlanner_RootItem_ProducesIconButtonEntry()
    {
        var layout = Layout(
            Item("ServerMonitor"),
            Section(UtilitiesMenuCatalog.GroupServers));

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        var entry = Assert.Single(plan, b => b.Kind == UtilitiesSectionButtonKind.Item);
        Assert.Equal("ServerMonitor", entry.ItemId);
        Assert.Equal("ServerMonitor", entry.SectionId);
        Assert.Equal(UtilitiesMenuCatalog.Find("ServerMonitor")!.TextKey, entry.TitleKey);
        Assert.False(entry.IsCustom);
        Assert.Null(entry.NodeId);
    }

    /// <summary>Верхнеуровневый разделитель даёт запись-черту между кнопками.</summary>
    [Fact]
    public void SectionMenuPlanner_RootSeparator_ProducesSeparatorEntry()
    {
        var layout = Layout(
            Section(UtilitiesMenuCatalog.GroupServers),
            Separator(),
            Section(UtilitiesMenuCatalog.GroupTools));

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(
            new[]
            {
                UtilitiesSectionButtonKind.Group,
                UtilitiesSectionButtonKind.Separator,
                UtilitiesSectionButtonKind.Group
            },
            plan.Select(b => b.Kind).ToArray());
    }

    /// <summary>Порядок группы/пункта/разделителя на панели совпадает с порядком в дереве.</summary>
    [Fact]
    public void SectionMenuPlanner_RootOrder_GroupItemSeparatorPreserved()
    {
        var layout = Layout(
            Section(UtilitiesMenuCatalog.GroupServers),
            Item("Services"),
            Separator(),
            Section(UtilitiesMenuCatalog.GroupTools));

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(
            new[]
            {
                UtilitiesSectionButtonKind.Group,
                UtilitiesSectionButtonKind.Item,
                UtilitiesSectionButtonKind.Separator,
                UtilitiesSectionButtonKind.Group
            },
            plan.Select(b => b.Kind).ToArray());
        Assert.Equal(UtilitiesMenuCatalog.GroupServers, plan[0].SectionId);
        Assert.Equal("Services", plan[1].ItemId);
        Assert.Equal(UtilitiesMenuCatalog.GroupTools, plan[3].SectionId);
    }

    /// <summary>
    /// Ведущие, двойные и замыкающие разделители отбрасываются: черта имеет смысл
    /// только между двумя элементами панели.
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_LeadingAndTrailingSeparators_Dropped()
    {
        var layout = Layout(
            Separator(),
            Section(UtilitiesMenuCatalog.GroupServers),
            Separator(),
            Separator(),
            Item("Services"),
            Separator());

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(
            new[]
            {
                UtilitiesSectionButtonKind.Group,
                UtilitiesSectionButtonKind.Separator,
                UtilitiesSectionButtonKind.Item
            },
            plan.Select(b => b.Kind).ToArray());
    }

    /// <summary>
    /// Плоский корень из пунктов даёт кнопки-иконки и запасную кнопку «Утилиты»:
    /// верхнеуровневые пункты не отнимают доступ к полному меню (5.5).
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_FlatRootItems_StillReturnsUtilitiesFallback()
    {
        var layout = Layout(Item("ServerMonitor"), Item("Services"));

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.Equal(2, plan.Count(b => b.Kind == UtilitiesSectionButtonKind.Item));
        var fallback = Assert.Single(plan, b => b.Kind == UtilitiesSectionButtonKind.Group);
        Assert.True(fallback.IsCustom);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesSectionId, fallback.SectionId);
        Assert.Equal(UtilitiesSectionMenuPlanner.UtilitiesTitleKey, fallback.TitleKey);
    }

    /// <summary>Скрытый верхнеуровневый пункт не попадает на панель.</summary>
    [Fact]
    public void SectionMenuPlanner_HiddenRootItem_NotInPlan()
    {
        var hidden = Item("ServerMonitor");
        hidden.Visible = false;
        var layout = Layout(hidden, Section(UtilitiesMenuCatalog.GroupServers));

        var plan = UtilitiesSectionMenuPlanner.Build(layout);

        Assert.DoesNotContain(plan, b => b.Kind == UtilitiesSectionButtonKind.Item);
        Assert.Single(plan, b => b.Kind == UtilitiesSectionButtonKind.Group);
    }

    /// <summary>
    /// Планировщик платформонезависим: <c>LinuxOnly</c>-пункт остаётся в плане,
    /// фильтрация — обязанность рендерера платформы (R10).
    /// </summary>
    [Fact]
    public void SectionMenuPlanner_LinuxOnlyItem_LeftToPlatformRenderer()
    {
        const string itemId = "TestLinuxOnlyTopLevel";
        var item = new UtilitiesMenuItem(
            itemId, "Test.LinuxOnly", UtilitiesMenuCatalog.GroupTopLevel,
            "Cog", "IconSettings", "#22C55E",
            UtilitiesMenuCommandKind.CommandPalette, linuxOnly: true);
        UtilitiesMenuCatalog.RegisterExternal(item, () => { });
        try
        {
            var layout = Layout(
                new UtilitiesMenuNode { Kind = "item", ItemId = itemId },
                Section(UtilitiesMenuCatalog.GroupServers));

            var plan = UtilitiesSectionMenuPlanner.Build(layout);

            var entry = Assert.Single(plan, b => b.Kind == UtilitiesSectionButtonKind.Item);
            Assert.Equal(itemId, entry.ItemId);
        }
        finally
        {
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>Поиск корневого узла находит верхнеуровневый пункт по ItemId.</summary>
    [Fact]
    public void FindRootNode_FindsTopLevelItemById()
    {
        var layout = Layout(
            Item("ServerMonitor"),
            Section(UtilitiesMenuCatalog.GroupServers));

        var node = UtilitiesMenuLayouts.FindRootNode(layout, "ServerMonitor");

        Assert.NotNull(node);
        Assert.Equal("item", node!.Kind);
        Assert.Equal("ServerMonitor", node.ItemId);
        // Группа по-прежнему находится по GroupId.
        Assert.Equal(UtilitiesMenuCatalog.GroupServers,
            UtilitiesMenuLayouts.FindRootNode(layout, UtilitiesMenuCatalog.GroupServers)!.GroupId);
    }

    // ===================== 2. Каталог: верхнеуровневый пункт =====================

    /// <summary>
    /// Пункт с <c>DefaultGroup == TopLevel</c> добавляется в корень (а не в «Новинки»):
    /// перед первой группой, с признаком IsTopLevel и разделителем после пункта.
    /// </summary>
    [Fact]
    public void AddMissingCatalogItems_TopLevelDefaultGroup_AddsToRoot()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var item = new UtilitiesMenuItem(
            "TestTopLevelMissing", "Test.TopLevel", UtilitiesMenuCatalog.GroupTopLevel,
            "Cog", "IconSettings", "#22C55E", UtilitiesMenuCommandKind.CommandPalette);

        UtilitiesMenuLayouts.AddMissingCatalogItems(layout, new[] { item });

        // Issue #146 (A4): в стандартной компоновке уже есть верхнеуровневый
        // «Инспектор процессов» и разделитель, поэтому новый пункт вставляется
        // перед первой группой — после существующего блока верхнего уровня.
        Assert.Equal("ProcessInspector", layout.Root[0].ItemId);
        Assert.Equal("separator", layout.Root[1].Kind);

        var addedIndex = layout.Root.FindIndex(
            n => n.Kind == "item" && n.ItemId == "TestTopLevelMissing");
        Assert.True(addedIndex > 1, "Пункт верхнего уровня должен попасть в корень.");
        Assert.True(layout.Root[addedIndex].IsTopLevel);
        Assert.Equal("separator", layout.Root[addedIndex + 1].Kind);
        Assert.True(layout.Root[addedIndex + 1].IsTopLevel);
        // Разделы не сдвинуты: первый раздел — сразу после пункта и разделителя.
        Assert.Equal(UtilitiesMenuCatalog.GroupServers, layout.Root[addedIndex + 2].GroupId);

        // Повторный вызов не дублирует пункт.
        UtilitiesMenuLayouts.AddMissingCatalogItems(layout, new[] { item });
        Assert.Single(layout.Root, n => n.Kind == "item" && n.ItemId == "TestTopLevelMissing");
    }

    /// <summary>
    /// Пункт, уже размещённый пользователем в группе, повторно в корень не добавляется
    /// (проверка <c>present</c> по всему дереву) — R9.
    /// </summary>
    [Fact]
    public void AddMissingCatalogItems_ItemAlreadyInGroup_NoDuplicate()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var servers = layout.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
        servers.Children.Add(new UtilitiesMenuNode { Kind = "item", ItemId = "TestTopLevelExisting" });
        var item = new UtilitiesMenuItem(
            "TestTopLevelExisting", "Test.TopLevel", UtilitiesMenuCatalog.GroupTopLevel,
            "Cog", "IconSettings", "#22C55E", UtilitiesMenuCommandKind.CommandPalette);

        UtilitiesMenuLayouts.AddMissingCatalogItems(layout, new[] { item });

        Assert.DoesNotContain(layout.Root,
            n => n.Kind == "item" && n.ItemId == "TestTopLevelExisting");
        Assert.Single(servers.Children, n => n.ItemId == "TestTopLevelExisting");
    }

    // ===================== 2b. «Инспектор процессов» (A4) =====================

    /// <summary>
    /// Стандартная компоновка (issue #146, A4): «Инспектор процессов 1С» — первый
    /// узел корня (верхний уровень), за ним разделитель, затем четыре раздела.
    /// Всего в корне 6 узлов.
    /// </summary>
    [Fact]
    public void DefaultLayout_ProcessInspectorAtRoot_BeforeSections_WithSeparator()
    {
        var layout = UtilitiesMenuLayouts.Default();

        Assert.Equal(6, layout.Root.Count);
        Assert.Equal("item", layout.Root[0].Kind);
        Assert.Equal("ProcessInspector", layout.Root[0].ItemId);
        Assert.True(layout.Root[0].IsTopLevel);
        Assert.Equal("separator", layout.Root[1].Kind);
        Assert.True(layout.Root[1].IsTopLevel);
        Assert.Equal(
            UtilitiesMenuCatalog.RootSections,
            layout.Root.Skip(2).Select(n => n.GroupId!).ToArray());
        Assert.All(layout.Root.Skip(2), n => Assert.True(n.IsTopLevel));

        var catalogItem = UtilitiesMenuCatalog.Find("ProcessInspector");
        Assert.NotNull(catalogItem);
        Assert.Equal(UtilitiesMenuCatalog.GroupTopLevel, catalogItem!.DefaultGroup);
        Assert.Equal(UtilitiesMenuCommandKind.ProcessInspector, catalogItem.CommandKind);
    }

    /// <summary>
    /// Старая пользовательская компоновка, где <c>ProcessInspector</c> уже лежит в группе,
    /// сохраняет его на месте: пункт валиден, повторно в корень не добавляется (R9).
    /// </summary>
    [Fact]
    public void OldLayout_WithProcessInspectorInGroup_IsPreserved_NoDuplicate()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Section(UtilitiesMenuCatalog.GroupProcesses, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ProcessInspector" },
                    new() { Kind = "item", ItemId = "KillOneCProcesses" },
                }),
                UtilitiesMenuLayouts.Section(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ServerMonitor" },
                }),
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        var occurrences = Flatten(migrated.Root)
            .Where(n => n.Kind == "item" && n.ItemId == "ProcessInspector")
            .ToList();
        Assert.Single(occurrences);
        // Пункт остался в группе пользователя, а не продублирован в корне.
        Assert.DoesNotContain(migrated.Root,
            n => n.Kind == "item" && n.ItemId == "ProcessInspector");
        var processes = migrated.Root.Single(
            n => n.GroupId == UtilitiesMenuCatalog.GroupProcesses);
        Assert.Contains(processes.Children, n => n.ItemId == "ProcessInspector");
    }

    /// <summary>
    /// Обе оболочки привязывают <c>ProcessInspectorCommand</c> к пункту меню по
    /// <see cref="UtilitiesMenuCommandKind.ProcessInspector"/>.
    /// </summary>
    [Fact]
    public void WpfAndAvalonia_ProcessInspectorCommandWired()
    {
        var wpf = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("case UtilitiesMenuCommandKind.ProcessInspector:", wpf, StringComparison.Ordinal);
        Assert.Contains("item.Command = _viewModel.ProcessInspectorCommand;", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("case UtilitiesMenuCommandKind.ProcessInspector:", avalonia, StringComparison.Ordinal);
        Assert.Contains("item.Command = _vm!.ProcessInspectorCommand;", avalonia, StringComparison.Ordinal);
    }

    /// <summary>
    /// Жёстко зашитой кнопки инспектора на панели инструментов больше нет ни в WPF,
    /// ни в Avalonia; хоткей Ctrl+Alt+I сохранён (его проверяют тесты #134).
    /// </summary>
    [Fact]
    public void WpfAndAvalonia_ProcessInspectorNotOnToolbar()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.DoesNotContain("ProcessInspectorButton", xaml, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.DoesNotContain("processInspectorBtn", avalonia, StringComparison.Ordinal);

        var wpfHotkeys = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.Hotkeys.cs"));
        Assert.Contains("_viewModel.ProcessInspectorCommand", wpfHotkeys, StringComparison.Ordinal);
        var avaloniaHotkeys = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.Avalonia.Hotkeys.cs"));
        Assert.Contains("_vm.ProcessInspectorCommand", avaloniaHotkeys, StringComparison.Ordinal);
    }

    /// <summary>Ключ локализации пункта «Инспектор процессов 1С» есть в ru/en.</summary>
    [Fact]
    public void Localization_HasProcessInspectorMenuItemKey()
    {
        var ru = ReadProjectFile(Path.Combine(
            "Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine(
            "Workspace1C", "Localization", "Languages", "en.json"));

        Assert.Contains("\"ProcessInspector.MenuItem\"", ru, StringComparison.Ordinal);
        Assert.Contains("Инспектор процессов 1С", ru, StringComparison.Ordinal);
        Assert.Contains("\"ProcessInspector.MenuItem\"", en, StringComparison.Ordinal);
        Assert.Contains("1C process inspector", en, StringComparison.Ordinal);
    }

    // ===================== 3. Модель, сохранение и миграция =====================

    /// <summary>
    /// Схема 5: при загрузке/миграции признак IsTopLevel выставляется всем узлам корня —
    /// группе, пункту и разделителю; у вложенных узлов он остаётся false.
    /// </summary>
    [Fact]
    public void Schema5Layout_Load_SetsIsTopLevelForRootItemAndSeparator()
    {
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                new() { Kind = "item", ItemId = "ServerMonitor" },
                new() { Kind = "separator" },
                UtilitiesMenuLayouts.Section(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "Services" },
                    new() { Kind = "separator" },
                }),
            }
        };

        var migrated = UtilitiesMenuLayouts.Migrate(layout);

        Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.True(migrated.Root[0].IsTopLevel);
        Assert.True(migrated.Root[1].IsTopLevel);
        Assert.True(migrated.Root[2].IsTopLevel);
        var servers = migrated.Root[2];
        Assert.All(servers.Children, child => Assert.False(child.IsTopLevel));
    }

    /// <summary>
    /// Перенос пункта и разделителя в корень через редактор выставляет IsTopLevel и
    /// сохраняется в компоновку (<see cref="UtilitiesMenuEditorViewModel.SaveCommand"/>).
    /// </summary>
    [Fact]
    public void MoveToRoot_ItemAndSeparator_SetsIsTopLevel_AndPersists()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        var servers = vm.Roots.Single(r => r.Node.GroupId == UtilitiesMenuCatalog.GroupServers);

        var item = servers.Children.First(c => c.Node.Kind == "item");
        vm.SelectedItem = item;
        vm.MoveToRootCommand.Execute(null);

        Assert.True(item.Node.IsTopLevel);
        Assert.Null(item.Parent);

        vm.SaveCommand.Execute(null);
        var savedItem = vm.Layout.Root.Single(n => n.Kind == "item" && n.ItemId == item.Node.ItemId);
        Assert.True(savedItem.IsTopLevel);

        var separator = servers.Children.First(c => c.Node.Kind == "separator");
        vm.SelectedItem = separator;
        vm.MoveToRootCommand.Execute(null);

        Assert.True(separator.Node.IsTopLevel);
        Assert.Null(separator.Parent);

        vm.SaveCommand.Execute(null);
        // Issue #146 (A4): в корне стандартной компоновки уже есть верхнеуровневый
        // разделитель «Инспектора процессов», поэтому после переноса их два —
        // оба должны быть верхнего уровня (ищем не по Single, а по признаку).
        var savedSeparators = vm.Layout.Root
            .Where(n => n.Kind == "separator" && n.ItemId is null && n.GroupId is null)
            .ToList();
        Assert.NotEmpty(savedSeparators);
        Assert.All(savedSeparators, n => Assert.True(n.IsTopLevel));
    }

    /// <summary>
    /// Сохранение и загрузка компоновки схемы 5 сохраняют IsTopLevel у корневых
    /// пункта и разделителя (round-trip через файл).
    /// </summary>
    [Fact]
    public void SaveLoad_RootItemAndSeparator_RoundTrip()
    {
        var dir = CreateTempDirectory();
        try
        {
            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var layout = new UtilitiesMenuLayout
            {
                SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
                Root = new List<UtilitiesMenuNode>
                {
                    new() { Kind = "item", ItemId = "ServerMonitor", IsTopLevel = true },
                    new() { Kind = "separator", IsTopLevel = true },
                    UtilitiesMenuLayouts.Section(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
                    {
                        new() { Kind = "item", ItemId = "Services" },
                    }),
                }
            };

            store.Save(layout);
            var restored = store.Load();

            Assert.NotNull(restored);
            Assert.Equal(UtilitiesMenuLayouts.CurrentSchemaVersion, restored!.SchemaVersion);
            Assert.True(restored.Root[0].IsTopLevel);
            Assert.Equal("ServerMonitor", restored.Root[0].ItemId);
            Assert.True(restored.Root[1].IsTopLevel);
            Assert.Equal("separator", restored.Root[1].Kind);
            // Issue #146 (A4): миграция может добавить верхнеуровневый «Инспектор
            // процессов» перед первой группой, поэтому раздел ищем по GroupId.
            var servers = restored.Root.Single(
                n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
            Assert.True(servers.IsTopLevel);
            Assert.False(servers.Children[0].IsTopLevel);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Схема файла остаётся 5: новых persisted-полей нет, миграция лишь нормализует
    /// признак верхнего уровня (риск R11 — схему не поднимаем).
    /// </summary>
    [Fact]
    public void SchemaVersion_RemainsFive()
    {
        Assert.Equal(5, UtilitiesMenuLayouts.CurrentSchemaVersion);
    }

    // ===================== 4. Рендер панелей (source-guard) =====================

    /// <summary>
    /// WPF-панель умеет рисовать верхнеуровневый пункт (кнопка IconButton с иконкой,
    /// подсказкой и командой из CreateItem) и разделитель (Border шириной 1).
    /// </summary>
    [Fact]
    public void WpfPanel_BuildsItemAndSeparatorButtons()
    {
        var code = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));

        Assert.Contains("UtilitiesSectionButtonKind.Separator", code, StringComparison.Ordinal);
        Assert.Contains("UtilitiesSectionButtonKind.Item", code, StringComparison.Ordinal);
        Assert.Contains("BuildSectionSeparator", code, StringComparison.Ordinal);
        Assert.Contains("CreateItem(itemNode)", code, StringComparison.Ordinal);
        Assert.Contains("Tag = catalogItem.Id", code, StringComparison.Ordinal);
        Assert.Contains("FindRootNode", code, StringComparison.Ordinal);
        Assert.Contains("Width = 1", code, StringComparison.Ordinal);

        // Панель разделов — WrapPanel: при переполнении кнопки переносятся (R8).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("<WrapPanel x:Name=\"SectionButtonsPanel\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Avalonia-панель рисует пункт кнопкой-иконкой с командой из CreateAvaloniaItem,
    /// разделитель — <c>CommandPanelSeparator()</c>; блок разделов — WrapPanel.
    /// </summary>
    [Fact]
    public void AvaloniaPanel_BuildsItemAndSeparatorButtons()
    {
        var code = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        Assert.Contains("UtilitiesSectionButtonKind.Separator", code, StringComparison.Ordinal);
        Assert.Contains("UtilitiesSectionButtonKind.Item", code, StringComparison.Ordinal);
        Assert.Contains("CommandPanelSeparator()", code, StringComparison.Ordinal);
        Assert.Contains("CreateAvaloniaItem(itemNode)", code, StringComparison.Ordinal);
        Assert.Contains("FindRootNode", code, StringComparison.Ordinal);
        Assert.Contains("new WrapPanel", code, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static UtilitiesMenuLayout Layout(params UtilitiesMenuNode[] nodes) => new()
    {
        SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
        Root = nodes.ToList()
    };

    private static UtilitiesMenuNode Item(string itemId) =>
        UtilitiesMenuLayouts.Item(itemId);

    private static UtilitiesMenuNode Separator() =>
        UtilitiesMenuLayouts.Separator();

    /// <summary>Рекурсивный обход дерева компоновки (для проверки уникальности пункта).</summary>
    private static IEnumerable<UtilitiesMenuNode> Flatten(IEnumerable<UtilitiesMenuNode>? nodes)
    {
        if (nodes is null)
            yield break;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    private static UtilitiesMenuNode Section(string groupId) =>
        UtilitiesMenuLayouts.Section(groupId, new List<UtilitiesMenuNode>());

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue146-a3-" + Guid.NewGuid().ToString("N"));
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
}
