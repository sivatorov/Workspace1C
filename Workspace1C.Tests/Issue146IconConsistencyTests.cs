using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (комментарий 7OH 2026-11-23): «Иконка в правке группы и иконка в
/// отображаемой структуре — одинаковые. Справа в настройке иконки — уже вторая иконка.
/// В самом отображаемом меню — вообще третья». Все поверхности (дерево редактора,
/// диалог правки группы, поле выбора иконки, реальное меню WPF/Avalonia) обязаны
/// использовать единый резолвер <see cref="UtilitiesMenuCatalog.WpfIconGeometryKey"/>.
/// </summary>
public sealed class Issue146IconConsistencyTests
{
    /// <summary>У каждого варианта каталога иконок есть непустой ключ геометрии Icon*.</summary>
    [Fact]
    public void CatalogIcons_AllHaveResolvableGeometryKey()
    {
        var options = UtilitiesMenuEditorViewModel.BuildCatalogIcons();

        Assert.NotEmpty(options);
        foreach (var option in options)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.GeometryKey),
                $"Иконка каталога «{option.Key}» не резолвится в геометрию Icon*");
            Assert.Equal(UtilitiesMenuCatalog.WpfIconGeometryKey(option.Key), option.GeometryKey);
        }
    }

    /// <summary>
    /// Дерево редактора, диалог правки группы и реальное меню получают один и тот же
    /// ключ геометрии для стандартной группы «Новинки программы» (раньше меню рисовало
    /// MaterialDesign PackIcon, а дерево — геометрию IconNewBox).
    /// </summary>
    [Fact]
    public void GroupNewItems_SameGeometryKey_OnAllSurfaces()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems
        };
        var item = new UtilitiesMenuNodeItem(node, parent: null);

        var effective = item.EffectiveIconKey;
        var geometry = item.DisplayIconGeometryKey;

        Assert.False(string.IsNullOrWhiteSpace(effective));
        Assert.Equal("IconNewBox", geometry);
        Assert.Equal(UtilitiesMenuCatalog.WpfIconGeometryKey(effective), geometry);
        Assert.Equal(
            UtilitiesMenuCatalog.WpfIconGeometryKey(
                UtilitiesMenuCatalog.ResolveGroupIconKeyWpf(node.GroupId, node.IconKey)),
            geometry);
        // Та же пара, что использует реальное меню через ResolveGroupIconKeyWpf.
        Assert.Equal("NewBox", UtilitiesMenuCatalog.ResolveGroupIconKeyWpf(node.GroupId, node.IconKey));
    }

    /// <summary>
    /// Каталог иконок строится только из резолвимых ключей, поэтому поле выбора
    /// (WPF/Avalonia) не может показать «вторую» иконку.
    /// </summary>
    [Fact]
    public void CatalogIcons_ContainOnlyResolvableKeys()
    {
        foreach (var option in UtilitiesMenuEditorViewModel.BuildCatalogIcons())
        {
            Assert.False(string.IsNullOrWhiteSpace(
                UtilitiesMenuCatalog.WpfIconGeometryKey(option.Key)));
        }
    }

    /// <summary>Нерезолвимый сохранённый ключ группы нормализуется к стандартной иконке.</summary>
    [Fact]
    public void GroupWithUnknownSavedIcon_FallsBackToStandardGeometry()
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupServers,
            IconKey = "NoSuchPackIcon146"
        };
        var item = new UtilitiesMenuNodeItem(node, parent: null);

        Assert.Equal("ServerNetwork", item.EffectiveIconKey);
        Assert.Equal("IconServer", item.DisplayIconGeometryKey);
    }

    /// <summary>Пользовательская группа без иконки детерминированно рисует «папку».</summary>
    [Fact]
    public void UserGroupWithoutIcon_UsesFolderGeometry()
    {
        var node = new UtilitiesMenuNode { Kind = "group", GroupId = null, CustomTitle = "Папка" };
        var item = new UtilitiesMenuNodeItem(node, parent: null);

        Assert.Equal(UtilitiesMenuCatalog.LegacyFallbackIconKey, item.EffectiveIconKey);
        Assert.Equal("IconFolder", item.DisplayIconGeometryKey);
    }

    /// <summary>
    /// Source-страж: «Новинки программы» больше не рисуются отдельным PackIcon-путём
    /// в реальном меню (WPF и Avalonia), а превью редакторов привязаны к GeometryKey.
    /// </summary>
    [Fact]
    public void SourceGuards_NoSpecialPackIconPathAndGeometryPreviews()
    {
        var wpfMenu = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");
        Assert.DoesNotContain("CreateRowMenuIcon", wpfMenu, StringComparison.Ordinal);

        var avaloniaMenu = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.DoesNotContain("GroupAvaloniaIconKey(UtilitiesMenuCatalog.GroupNewItems)", avaloniaMenu,
            StringComparison.Ordinal);

        var groupEdit = ReadProjectFile("Workspace1C", "Views", "UtilitiesGroupEditWindow.xaml");
        Assert.Contains("{Binding GeometryKey", groupEdit, StringComparison.Ordinal);

        var editor = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.xaml");
        Assert.Contains("{Binding GeometryKey", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("PackIcon Kind=\"{Binding Key", editor, StringComparison.Ordinal);

        var avaloniaEditor = ReadProjectFile(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        Assert.Contains("option.GeometryKey", avaloniaEditor, StringComparison.Ordinal);
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
