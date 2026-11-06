using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #110 «ИД базы»: два новых пункта «Открыть папку программного/пользовательского
/// кеша» в подменю «Очистить кэш» (с разделителем), команды не считаются применимыми к группе
/// как одиночно-базовые… но открытие папок кеша должно работать и для группы
/// (<see cref="ContextMenuCatalog.IsGroupApplicable"/>), и привязка команд в построителях меню
/// WPF/Avalonia (стражи по исходникам). Локализация: ключи заголовков и сообщения
/// «папки кеша не найдены», «Копировать ИД базы».
/// </summary>
public sealed class Issue110CacheFolderMenuTests
{
    [Fact]
    public void Catalog_ContainsOpenCacheFolderItems()
    {
        var program = ContextMenuCatalog.Find("OpenProgramCacheFolder");
        var user = ContextMenuCatalog.Find("OpenUserCacheFolder");

        Assert.NotNull(program);
        Assert.NotNull(user);
        Assert.Equal(ContextMenuCommandKind.OpenProgramCacheFolder, program!.CommandKind);
        Assert.Equal(ContextMenuCommandKind.OpenUserCacheFolder, user!.CommandKind);
        // Оба пункта живут в подменю «Очистить кэш».
        Assert.Equal(ContextMenuCatalog.GroupClearCache, program.DefaultGroup);
        Assert.Equal(ContextMenuCatalog.GroupClearCache, user.DefaultGroup);
    }

    [Fact]
    public void DefaultLayout_OpenCacheFolderItems_AfterClearItemsWithSeparator()
    {
        var layout = ContextMenuLayouts.Default();

        var clearGroup = layout.Root.FirstOrDefault(n =>
            string.Equals(n.Kind, "group", StringComparison.Ordinal) &&
            string.Equals(n.GroupId, ContextMenuCatalog.GroupClearCache, StringComparison.Ordinal));
        Assert.NotNull(clearGroup);

        var children = clearGroup!.Children;
        var ids = children.Select(c => c.ItemId).ToList();

        var bothIndex = ids.IndexOf("ClearCacheBoth");
        Assert.True(bothIndex >= 0, "В компоновке отсутствует ClearCacheBoth");

        var programIndex = ids.IndexOf("OpenProgramCacheFolder");
        var userIndex = ids.IndexOf("OpenUserCacheFolder");
        Assert.True(programIndex > bothIndex, "«Открыть папку программного кеша» должен идти после пунктов очистки");
        Assert.True(userIndex > programIndex, "«Открыть папку пользовательского кеша» — после программного");

        // Перед первым пунктом «Открыть папку…» — разделитель.
        Assert.Equal("separator", children[programIndex - 1].Kind);
    }

    [Fact]
    public void IsGroupApplicable_OpenCacheFolder_True()
    {
        // Открытие папок кеша работает и для группы (базы группы открываются так же,
        // как очистка кеша с предвыбором).
        Assert.True(ContextMenuCatalog.IsGroupApplicable(ContextMenuCommandKind.OpenProgramCacheFolder));
        Assert.True(ContextMenuCatalog.IsGroupApplicable(ContextMenuCommandKind.OpenUserCacheFolder));
    }

    [Fact]
    public void Localization_OpenCacheFolderAndStatusId_ExistInRuAndEn()
    {
        foreach (var key in new[]
                 {
                     "Main.OpenProgramCacheFolder",
                     "Main.OpenUserCacheFolder",
                     "Main.CacheFolderNotFound",
                     "Main.StatusIdCopy"
                 })
        {
            AssertLocalizationKey(key);
        }
    }

    [Fact]
    public void CommandBindings_PresentInBothBuilders()
    {
        // WPF-построитель меню строки.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.ContextMenu.cs"));
        Assert.Contains("case ContextMenuCommandKind.OpenProgramCacheFolder:", wpf, StringComparison.Ordinal);
        Assert.Contains("case ContextMenuCommandKind.OpenUserCacheFolder:", wpf, StringComparison.Ordinal);

        // Avalonia-построитель.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("case ContextMenuCommandKind.OpenProgramCacheFolder:", avalonia, StringComparison.Ordinal);
        Assert.Contains("case ContextMenuCommandKind.OpenUserCacheFolder:", avalonia, StringComparison.Ordinal);

        // Статус-бар: WPF XAML — команда копирования ИД; Avalonia — пункт меню + DoubleTapped.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("CopyInfobaseIdCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Main.StatusIdCopy", xaml, StringComparison.Ordinal);

        var avaloniaMain = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("CopyInfobaseIdCommand", avaloniaMain, StringComparison.Ordinal);
        Assert.Contains("DoubleTapped", avaloniaMain, StringComparison.Ordinal);
    }

    private static void AssertLocalizationKey(string key)
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains($"\"{key}\":", ru, StringComparison.Ordinal);
        Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
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
