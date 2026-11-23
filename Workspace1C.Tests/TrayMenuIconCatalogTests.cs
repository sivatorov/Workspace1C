using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #62 («Иконок конечно маловато, намного меньше, чем в правке структуры Утилиты…
/// Выбираем из того, что есть - сохраняем. Иконка совсем другая»): набор иконок меню трея
/// должен совпадать с каталогом редактора «Утилиты», а выбранная иконка — применяться
/// к группе в трее (не заменяться стандартной). Стражи на каталог, построитель и адаптер.
/// </summary>
public sealed class TrayMenuIconCatalogTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TrayEditor_ReusesUtilitiesIconCatalog()
    {
        // issue #62: редактор меню трея берёт каталог иконок «Утилит» (а не 5 захардкоженных).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "TrayMenuEditorViewModel.cs"));

        Assert.Contains("UtilitiesMenuEditorViewModel.BuildCatalogIcons()", source);
    }

    [Fact]
    public void UtilitiesCatalog_HasMoreThanLegacyFive_AndEveryKeyResolvesToGeometryInBothDictionaries()
    {
        // Каталог «Утилит» больше прежних 5 иконок трея; каждый ключ отображается на парный
        // ключ геометрии Icon* (UtilitiesMenuCatalog.WpfIconGeometryKey), который присутствует
        // в ОБОИХ базовых словарях — Icons.xaml (WPF) и Icons.axaml (Avalonia). Если бы ключа
        // не было, резолв уходил бы в запасную «папку» — в трее и превью рисовалась бы
        // «совсем другая» иконка (issue #62, последний комментарий 7OH).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Icons.xaml"));
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Icons.axaml"));
        var catalog = UtilitiesMenuEditorViewModel.BuildCatalogIcons();

        Assert.True(catalog.Count > 5, $"Каталог иконок должен быть больше прежних 5 (реально: {catalog.Count})");

        // Issue #146: глобальная регистрация модулей ПРО (AppServices.Configure в
        // Issue79TestDeployWindowConstructionTests) добавляет внешние пункты с иконками,
        // которых может не быть в публичных словарях. Проверяем ТОЛЬКО встроенный
        // каталог — это и есть контракт публичных словарей; иконки внешних пунктов
        // поставляет соответствующий модуль ПРО. Так тест не зависит от порядка
        // выполнения тестов в полном прогоне.
        foreach (var item in UtilitiesMenuCatalog.BuiltInItems)
        {
            if (string.IsNullOrWhiteSpace(item.WpfIconKind) || string.IsNullOrWhiteSpace(item.IconColor))
                continue;
            var geometryKey = UtilitiesMenuCatalog.WpfIconGeometryKey(item.WpfIconKind);
            Assert.False(string.IsNullOrEmpty(geometryKey),
                $"Ключ каталога {item.WpfIconKind} не отображается на ключ геометрии Icon*");
            Assert.Contains("x:Key=\"" + geometryKey + "\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Key=\"" + geometryKey + "\"", axaml, StringComparison.Ordinal);
            Assert.StartsWith("#", item.IconColor);
        }
    }

    [Fact]
    public void LegacyTrayGroupIconKeys_StillMapToGeometry()
    {
        // Совместимость с tray_menu_layout.json, сохранёнными до единого каталога «Утилит»
        // (1.0.0.45–1.0.0.73): старые PackIcon-ключи групп продолжают давать реальную
        // геометрию, а не запасную «папку».
        Assert.Equal("IconFolder", UtilitiesMenuCatalog.WpfIconGeometryKey("Folder"));
        Assert.Equal("IconDatabase", UtilitiesMenuCatalog.WpfIconGeometryKey("Database"));
        Assert.Equal("IconStar", UtilitiesMenuCatalog.WpfIconGeometryKey("Star"));
        Assert.Equal("IconSettings", UtilitiesMenuCatalog.WpfIconGeometryKey("Settings"));
        Assert.Equal("IconServices", UtilitiesMenuCatalog.WpfIconGeometryKey("Services"));
        Assert.Equal("IconExitToApp", UtilitiesMenuCatalog.WpfIconGeometryKey("ExitToApp"));
    }

    [Fact]
    public void EditorPreviews_UseApplicationGeometry_SharedWithTray()
    {
        // Превью редактора и диалога иконок обязано рисовать ТУ ЖЕ геометрию, что применяет
        // адаптер трея (IconKeyToGeometryConverter), а не MaterialDesign PackIcon — иначе
        // выбранная иконка «менялась бы по дороге» (issue #62).
        var trayEditorXaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.xaml"));
        Assert.Contains("conv:IconKeyToGeometryConverter x:Key=\"IconKeyToGeometry\"", trayEditorXaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"{Binding DisplayIconKey, Converter={StaticResource IconKeyToGeometry}}\"", trayEditorXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PackIconKindConverter", trayEditorXaml);

        var groupDialogXaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "UtilitiesGroupEditWindow.xaml"));
        Assert.Contains("conv:IconKeyToGeometryConverter x:Key=\"IconKeyToGeometry\"", groupDialogXaml, StringComparison.Ordinal);
        // Issue #146: превью привязано к единому ключу геометрии (GeometryKey), а не к
        // PackIcon-ключу — иначе в настройке была бы «вторая» иконка.
        Assert.Contains("Data=\"{Binding GeometryKey, Converter={StaticResource IconKeyToGeometry}}\"", groupDialogXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PackIconKindConverter", groupDialogXaml);

        // Avalonia: ключи превью проходят тот же маппинг каталога (иначе IconHelper
        // резолвил бы PackIcon-имя в запасную «папку»).
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.Avalonia.cs"));
        Assert.Contains("UtilitiesMenuCatalog.WpfIconGeometryKey", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_PropagatesIconKeyAndColor_ForUserGroups()
    {
        // Построитель обязан донести ключ и цвет иконки пользовательской группы до адаптеров
        // (WPF рисует выбранную иконку; без IconColor группа теряла бы цвет каталога).
        var layout = new TrayMenuLayout
        {
            Root = new System.Collections.Generic.List<TrayMenuNode>
            {
                new()
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.UserGroup,
                    CustomTitle = "Группа",
                    IconKey = "Folder",
                    IconColor = "#3B82F6",
                    Style = TrayMenuBlockStyle.Submenu,
                    Visible = true
                }
            }
        };

        var items = new TrayMenuBuilder(k => k).Build(layout);

        var group = items.FirstOrDefault(i => i.Kind == TrayItemKind.Submenu || i.Kind == TrayItemKind.Header);
        Assert.NotNull(group);
        Assert.Equal("Folder", group!.IconKey);
        Assert.Equal("#3B82F6", group.IconColor);
    }

    [Fact]
    public void WpfTrayAdapter_PassesGroupIconKeyAndColor()
    {
        // issue #62 («сохраняем — иконка совсем другая»): WPF-адаптер передаёт выбранную
        // иконку в CreateTrayItem (ранее пункт группы создавался всегда со стандартной).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Tray.cs"));

        Assert.Contains("TrayIconKind.Group, null, item.IconKey, item.IconColor", code);
        Assert.Contains("DrawCustomTrayIcon(iconKey, iconColor, TrayMenuIconSize)", code);
    }
}