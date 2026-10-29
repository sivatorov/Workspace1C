using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #81 («Иконка для Новинки»): у «Новинки программы» в меню «Утилиты» должна быть
/// ТА ЖЕ иконка (materialDesign NewBox), что у этого же пункта в контекстном меню базы, а
/// ключ NEW обязан быть доступен для выбора при правке структуры (каталог иконок редактора).
/// Пока геометрия IconNewBox отсутствовала в Icons.xaml / Icons.axaml, меню «Утилиты» и всё
/// Avalonia-приложение рисовали запасную «папку» вместо звезды NEW.
/// </summary>
public sealed class Issue81NewBoxIconTests
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
    public void NewItemsGroup_HasSameIconInUtilitiesAndContextMenu()
    {
        // Обе платформы: WPF — PackIcon Kind NewBox, Avalonia — ключ геометрии IconNewBox,
        // цвет — зелёный каталога. Меню «Утилиты» и контекстное меню ссылаются на одну иконку.
        Assert.Equal("NewBox", UtilitiesMenuCatalog.GroupWpfIconKind(UtilitiesMenuCatalog.GroupNewItems));
        Assert.Equal("NewBox", ContextMenuCatalog.GroupWpfIconKind(ContextMenuCatalog.GroupNewItems));
        Assert.Equal("IconNewBox", UtilitiesMenuCatalog.GroupAvaloniaIconKey(UtilitiesMenuCatalog.GroupNewItems));
        Assert.Equal("IconNewBox", ContextMenuCatalog.GroupAvaloniaIconKey(ContextMenuCatalog.GroupNewItems));
        Assert.Equal("#22C55E", UtilitiesMenuCatalog.GroupIconColor(UtilitiesMenuCatalog.GroupNewItems));
        Assert.Equal(UtilitiesMenuCatalog.GroupIconColor(UtilitiesMenuCatalog.GroupNewItems),
            ContextMenuCatalog.GroupIconColor(ContextMenuCatalog.GroupNewItems));
    }

    [Fact]
    public void NewBoxKey_IsInEditorIconCatalog_AndResolvesToIconNewBox()
    {
        // issue #81: «Для выбора при правке иконки NEW не видно» — ключ NewBox обязан быть
        // в каталоге иконок редактора структуры (Утилиты / контекстное меню / трей).
        var catalog = UtilitiesMenuEditorViewModel.BuildCatalogIcons();
        var newBox = catalog.FirstOrDefault(o => string.Equals(o.Key, "NewBox", StringComparison.Ordinal));
        Assert.NotNull(newBox);
        Assert.Equal("#22C55E", newBox!.Color);

        // PackIcon-имя каталога отображается на парный ключ геометрии Icon* (та же иконка).
        Assert.Equal("IconNewBox", UtilitiesMenuCatalog.WpfIconGeometryKey("NewBox"));
    }

    [Fact]
    public void IconNewBox_GeometryExistsInBaseAndSetDictionaries()
    {
        // Резолв конвертера ищет ключ IconNewBox в базовых словарях (Icons.xaml / Icons.axaml)
        // и вариант набора (IconNewBox.<Набор>) в словарях тем; иначе — запасная «папка».
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Icons.xaml"));
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Icons.axaml"));
        Assert.Contains("x:Key=\"IconNewBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconNewBox\"", axaml, StringComparison.Ordinal);

        foreach (var set in new[] { "Material", "MacOS", "WindowsXp", "Glass" })
        {
            var setXaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", $"Icons{set}.xaml"));
            var setAxaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", $"Icons{set}.axaml"));
            Assert.Contains($"x:Key=\"IconNewBox.{set}\"", setXaml, StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"IconNewBox.{set}\"", setAxaml, StringComparison.Ordinal);
        }

        // Duotone-набор Glass: полупрозрачный фоновый слой, как у остальных базовых иконок.
        Assert.Contains("x:Key=\"IconNewBox.Back.Glass\"",
            ReadProjectFile(Path.Combine("Workspace1C", "Themes", "IconsGlass.xaml")), StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconNewBox.Back.Glass\"",
            ReadProjectFile(Path.Combine("Workspace1C", "Themes", "IconsGlass.axaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void NewItemsGroup_IsCreatedByResolver_WithCatalogNewBoxIcon()
    {
        // В пользовательской компоновке без новых пунктов резолвер создаёт группу
        // «Новинки программы» (issue #4); у группы — стандартная иконка каталога
        // NewBox (issue #81, регрессия — та же иконка, что в контекстном меню).
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette")
            }
        };
        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);
        Assert.NotNull(result);
        var group = FindGroupRecursively(result!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(group);
        Assert.True(group!.Visible);
        Assert.Equal("NewBox", UtilitiesMenuCatalog.GroupWpfIconKind(group.GroupId!));
    }

    [Fact]
    public void LegacyFolderIcon_IsRecognized_AndNewBoxIsNotLegacy()
    {
        // Легаси-значения «папки» (пустое/IconFolder/Folder из старых layout) распознаются
        // как фолбэк, а стандартный ключ NewBox — нет: иначе нормализация сломала бы
        // правильную иконку.
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon(null));
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon(string.Empty));
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon("IconFolder"));
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon("Folder"));
        // issue #81 (комментарий 7OH «при показе Утилиты — звезда»): сохранённая в старых
        // layout «звезда» (Star/IconStar — старый дефолт «Новинок» до появления NewBox)
        // тоже легаси — реальное меню должно показывать NewBox, а не звезду.
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon("Star"));
        Assert.True(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon("IconStar"));
        Assert.False(UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon("NewBox"));
    }

    [Fact]
    public void ExistingNewItemsGroup_WithLegacyFolderIcon_IsResetToCatalogNewBox()
    {
        // «Реальное» меню при включённой правке структуры строится из сохранённого layout.
        // Старый файл (созданный до 1.0.0.103) мог сохранить в группу «Новинки программы»
        // запасную «папку» — реальное меню показывало не ту иконку, тогда как каталог,
        // редактор и контекстное меню уже отдавали NewBox (issue #81, комментарий 7OH:
        // «Какая иконка будет у реального меню Утилиты? Сюрприиииииз»).
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                new()
                {
                    Kind = "group",
                    GroupId = UtilitiesMenuCatalog.GroupNewItems,
                    IconKey = "IconFolder",
                    IconColor = "#64748B",
                    Visible = true,
                    Children =
                    {
                        new UtilitiesMenuNode { Kind = "item", ItemId = "Services" }
                    }
                }
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);
        Assert.NotNull(result);
        var group = FindGroupRecursively(result!.Root, UtilitiesMenuCatalog.GroupNewItems);
        Assert.NotNull(group);

        // Легаси-иконка сброшена — построитель резолвит стандартную каталога.
        Assert.Null(group!.IconKey);
        Assert.Null(group.IconColor);

        // И по обоим платформенным ключам это та же звезда NEW, что в контекстном меню.
        Assert.Equal("NewBox", UtilitiesMenuCatalog.GroupWpfIconKind(group.GroupId!));
        Assert.Equal("IconNewBox", UtilitiesMenuCatalog.GroupAvaloniaIconKey(group.GroupId!));
        Assert.Equal("#22C55E", UtilitiesMenuCatalog.GroupIconColor(group.GroupId!));
        Assert.Equal(
            ContextMenuCatalog.GroupWpfIconKind(Workspace1C.Services.ContextMenuCatalog.GroupNewItems),
            UtilitiesMenuCatalog.GroupWpfIconKind(group.GroupId!));
    }

    private static UtilitiesMenuNode? FindGroupRecursively(IEnumerable<UtilitiesMenuNode> nodes, string groupId)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal)
                && string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
                return node;
            if (node.Children is { Count: > 0 }
                && FindGroupRecursively(node.Children, groupId) is { } nested)
                return nested;
        }
        return null;
    }
}