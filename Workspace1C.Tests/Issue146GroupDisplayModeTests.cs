using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (комментарий 7OH 2026-11-23): в правке структуры меню «Утилиты»
/// не было поля «Отображение» для папки верхнего уровня, а вложенные группы
/// при миграции показывались только иконками. Настройка «Иконка» / «С надписью»
/// должна действовать только на группы верхнего уровня; вложенные — всегда с подписью.
/// </summary>
public sealed class Issue146GroupDisplayModeTests
{
    [Fact]
    public void EditParams_DefaultDisplayMode_IsIconWithLabel()
    {
        var parameters = new UtilitiesGroupEditParams("Группа", null);

        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, parameters.DisplayMode);
    }

    [Fact]
    public void Presentation_NestedGroup_AlwaysShowsLabel()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.Icon
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Вложенная", isTopLevel: false);

        Assert.True(presentation.ShowLabel);
        Assert.Equal("Вложенная", presentation.Header);
    }

    [Fact]
    public void Presentation_TopLevelGroup_CanHideLabel()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.Icon
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Верхняя", isTopLevel: true);

        Assert.False(presentation.ShowLabel);
        Assert.Null(presentation.Header);
    }

    [Fact]
    public void Presentation_TopLevelGroup_WithLabel_ShowsHeader()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel
        };

        var presentation = UtilitiesMenuGroupPresentation.Build(node, "Верхняя", isTopLevel: true);

        Assert.True(presentation.ShowLabel);
        Assert.Equal("Верхняя", presentation.Header);
    }

    [Fact]
    public void Editor_ForcesLabelForNestedGroup()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs");

        Assert.Contains("item.Parent is null", source, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuGroupDisplay.IconWithLabel", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesGroupEditWindow.xaml.cs")]
    [InlineData("UtilitiesGroupEditWindow.Avalonia.cs")]
    public void GroupDialog_HasDisplayModeSelector(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains("GroupDisplayOptions", source, StringComparison.Ordinal);
        Assert.Contains("showDisplayMode", source, StringComparison.Ordinal);
        Assert.Contains("DisplayIconWithLabel", ReadProjectFile("Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Стандартные разделы панели по умолчанию компактны (только иконка) — вид панели
    /// не меняется после появления настройки «С надписью».
    /// </summary>
    [Fact]
    public void DefaultLayout_StandardSections_AreCompact()
    {
        var layout = UtilitiesMenuLayouts.Default();

        foreach (var section in UtilitiesMenuCatalog.RootSections)
        {
            var node = layout.Root.First(n =>
                string.Equals(n.GroupId, section, StringComparison.Ordinal));
            Assert.Equal(UtilitiesMenuGroupDisplay.Icon, node.DisplayMode);
            Assert.True(node.IsTopLevel);
            var presentation = UtilitiesMenuGroupPresentation.Build(node, section, isTopLevel: true);
            Assert.False(presentation.ShowLabel);
            Assert.Null(presentation.Header);
        }
    }

    /// <summary>Пользовательская группа верхнего уровня по умолчанию — с подписью.</summary>
    [Fact]
    public void UserTopLevelGroup_DefaultDisplay_IsIconWithLabel()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Моя группа", null));
        var node = vm.SelectedItem!.Node;

        Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, node.DisplayMode);
        Assert.True(UtilitiesMenuGroupPresentation.Build(node, "Моя группа", isTopLevel: true).ShowLabel);
    }

    /// <summary>
    /// Кнопка панели и подменю используют один презентер: «С надписью» показывает
    /// подпись и на кнопке, и в подменю; «Иконка» — только иконку на кнопке,
    /// но вложенное подменю всегда с подписью.
    /// </summary>
    [Fact]
    public void PanelButtonAndSubmenu_SharePresentationRules()
    {
        var labeled = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel
        };
        var iconOnly = new UtilitiesMenuNode
        {
            Kind = "group",
            DisplayMode = UtilitiesMenuGroupDisplay.Icon
        };

        Assert.True(UtilitiesMenuGroupPresentation.Build(labeled, "Группа", isTopLevel: true).ShowLabel);
        Assert.True(UtilitiesMenuGroupPresentation.Build(labeled, "Группа", isTopLevel: false).ShowLabel);
        Assert.False(UtilitiesMenuGroupPresentation.Build(iconOnly, "Группа", isTopLevel: true).ShowLabel);
        Assert.True(UtilitiesMenuGroupPresentation.Build(iconOnly, "Группа", isTopLevel: false).ShowLabel);
    }

    /// <summary>Явный выбор «Иконка» для группы верхнего уровня сохраняется в компоновке.</summary>
    [Fact]
    public void Editor_ExplicitIconChoice_IsSavedForTopLevelGroup()
    {
        var vm = new UtilitiesMenuEditorViewModel(store: null);
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Группа", null));
        vm.EditGroupCommand.Execute(new UtilitiesGroupEditParams(
            "Группа", null, UtilitiesMenuGroupDisplay.Icon));

        vm.SaveCommand.Execute(null);
        var saved = Assert.Single(vm.Layout.Root, n => n.CustomTitle == "Группа");
        Assert.Equal(UtilitiesMenuGroupDisplay.Icon, saved.DisplayMode);
        Assert.False(UtilitiesMenuGroupPresentation.Build(saved, "Группа", isTopLevel: true).ShowLabel);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasDisplayModeKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Utilities.Editor.DisplayHeader\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Utilities.Editor.DisplayIcon\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Utilities.Editor.DisplayIconWithLabel\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Issue #146 (комментарий 7OH 2026-11-23T17:31Z): при выключенном режиме правки
    /// панель должна строиться по фактической пользовательской компоновке, а не по
    /// стандартной. Раньше <see cref="UtilitiesMenuResolver.Resolve"/> отдавал
    /// <see cref="UtilitiesMenuLayouts.Default"/> (все разделы — <c>Icon</c>), поэтому
    /// сохранённый <c>DisplayMode = IconWithLabel</c> не читался и кнопки показывали
    /// только иконку. Проверяем полный путь store → Migrate → панель.
    /// </summary>
    [Fact]
    public void ResolveForDisplay_EditingDisabled_UsesSavedDisplayMode()
    {
        var dir = CreateTempDirectory();
        try
        {
            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var layout = UtilitiesMenuLayouts.Default();
            var servers = layout.Root.First(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
            servers.DisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel;
            layout.Root.Add(new UtilitiesMenuNode
            {
                Kind = "group",
                NodeId = "node-146-panel",
                IsTopLevel = true,
                CustomTitle = "Моя папка",
                DisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel
            });
            store.Save(layout);

            var settings = new AppSettings { UtilitiesMenuEditingEnabled = false };
            var display = UtilitiesMenuResolver.ResolveForDisplay(settings, store);

            var savedServers = display.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, savedServers.DisplayMode);
            Assert.True(UtilitiesMenuGroupPresentation.Build(savedServers, "Серверы", isTopLevel: true).ShowLabel);

            var savedUser = display.Root.Single(n => n.NodeId == "node-146-panel");
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, savedUser.DisplayMode);
            Assert.True(UtilitiesMenuGroupPresentation.Build(savedUser, "Моя папка", isTopLevel: true).ShowLabel);

            var plan = UtilitiesSectionMenuPlanner.Build(display);
            Assert.Contains(plan, b => b.SectionId == "node-146-panel" && b.Title == "Моя папка");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Схема 5: <c>DisplayMode = IconWithLabel</c> переживает JSON-сериализацию,
    /// загрузку и повторную миграцию; группы верхнего уровня не переносятся в «Утилиты».
    /// </summary>
    [Fact]
    public void Schema5_LoadPreservesIconWithLabel_AndMigrationIsIdempotent()
    {
        var dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, UtilitiesMenuLayoutStore.FileName), """
            {
              "SchemaVersion": 5,
              "Root": [
                { "Kind": "group", "GroupId": "Servers", "IsTopLevel": true,
                  "DisplayMode": "IconWithLabel", "Visible": true, "Children": [] },
                { "Kind": "group", "NodeId": "node-146-json", "IsTopLevel": true,
                  "CustomTitle": "Моя папка", "DisplayMode": "IconWithLabel",
                  "Visible": true, "Children": [] }
              ]
            }
            """);

            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var loaded = store.Load();
            Assert.NotNull(loaded);

            var servers = loaded!.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers);
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, servers.DisplayMode);
            Assert.True(servers.IsTopLevel);
            var user = loaded.Root.Single(n => n.NodeId == "node-146-json");
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel, user.DisplayMode);
            Assert.True(user.IsTopLevel);

            // Повторная миграция схемы 5 идемпотентна: режим и состав корня не меняются.
            var again = UtilitiesMenuLayouts.Migrate(loaded);
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel,
                again.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers).DisplayMode);
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel,
                again.Root.Single(n => n.NodeId == "node-146-json").DisplayMode);
            Assert.DoesNotContain(again.Root, n => n.GroupId == UtilitiesMenuCatalog.GroupUtilities);

            // Повторное сохранение/загрузка сохраняет значение (JsonStringEnumConverter).
            store.Save(again);
            var reloaded = store.Load();
            Assert.Equal(UtilitiesMenuGroupDisplay.IconWithLabel,
                reloaded!.Root.Single(n => n.GroupId == UtilitiesMenuCatalog.GroupServers).DisplayMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Source-гварды: обе платформы применяют общий презентер и отдельный стиль кнопки
    /// с подписью (без фиксированных 32×32, иначе подпись обрезается), а отображаемая
    /// компоновка берётся из сохранённого файла независимо от режима правки.
    /// </summary>
    [Fact]
    public void SourceGuards_LabelButtonStyle_AndDisplayUsesSavedLayout()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");
        Assert.Contains("SectionLabelButton", wpf, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuGroupPresentation.Build(node, title, isTopLevel: true)", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");
        Assert.Contains("ControlThemes.SectionLabelButton", avalonia, StringComparison.Ordinal);
        Assert.Contains("UtilitiesMenuGroupPresentation.Build(node, title, isTopLevel: true)", avalonia, StringComparison.Ordinal);

        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile("Workspace1C", "Themes", theme);
            Assert.Contains("x:Key=\"SectionLabelButton\"", xaml, StringComparison.Ordinal);
            Assert.Contains("BasedOn=\"{StaticResource IconButton}\"", xaml, StringComparison.Ordinal);
        }

        var controls = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        Assert.Contains("<ControlTheme x:Key=\"SectionLabelButton\"", controls, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource IconButton}\"", controls, StringComparison.Ordinal);

        var resolver = ReadProjectFile("Workspace1C", "Services", "UtilitiesMenuResolver.cs");
        Assert.Contains("store?.Load() ?? UtilitiesMenuLayouts.Default()", resolver, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue146-display-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
