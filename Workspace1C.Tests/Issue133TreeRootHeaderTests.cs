using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #133 «Заголовок корня дерева баз» (v1.0.1.70): при пустом поле поиска корень
/// дерева показывает имя раздела — «Все», «Избранное», «Недавние», «Запущенные»,
/// а «Найдено» пишется только при непустом поиске. Счётчик «(N)» сохраняется.
/// <list type="bullet">
/// <item>правило вынесено в чистый <see cref="TreeRootHeaderResolver"/> (один и тот же
/// для WPF и Avalonia), покрыт матрицей разделов, поиска и счётчика;</item>
/// <item>ключи локализации ru/en добавлены и совпадают по набору;</item>
/// <item>обе платформы проверяются source-гвардами (Avalonia собирается только
/// в Linux-конфигурации, поэтому исходники читаются как текст).</item>
/// </list>
/// </summary>
public sealed class Issue133TreeRootHeaderTests
{
    private static string ReadProjectFile(string relativePath)
    {
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

    private static Dictionary<string, string> LoadStrings(string file)
    {
        var json = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", file));
        using var document = JsonDocument.Parse(json);
        var strings = document.RootElement.GetProperty("strings");
        // В словарях встречаются исторические дубли ключей (JSON допускает повторы) —
        // для проверки набора берём последнее значение, как это делает загрузчик.
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    // ===================== Правило подписи: разделы и поиск =====================

    [Theory]
    [InlineData(TreeRootSection.All, TreeRootHeaderResolver.AllKey)]
    [InlineData(TreeRootSection.Favorites, TreeRootHeaderResolver.FavoritesKey)]
    [InlineData(TreeRootSection.Recent, TreeRootHeaderResolver.RecentKey)]
    [InlineData(TreeRootSection.Running, TreeRootHeaderResolver.RunningKey)]
    public void EmptySearch_ReturnsSectionNameKey(TreeRootSection section, string expectedKey)
    {
        Assert.Equal(expectedKey, TreeRootHeaderResolver.ResolveNameKey(section, searchActive: false));
        Assert.Equal(expectedKey, TreeRootHeaderResolver.Resolve(section, searchActive: false).NameKey);
    }

    [Theory]
    [InlineData(TreeRootSection.All)]
    [InlineData(TreeRootSection.Favorites)]
    [InlineData(TreeRootSection.Recent)]
    [InlineData(TreeRootSection.Running)]
    public void ActiveSearch_ReturnsFoundKeyForEverySection(TreeRootSection section)
    {
        Assert.Equal(TreeRootHeaderResolver.FoundKey,
            TreeRootHeaderResolver.ResolveNameKey(section, searchActive: true));
        Assert.Equal(TreeRootHeaderResolver.FoundKey,
            TreeRootHeaderResolver.Resolve(section, searchActive: true).NameKey);
    }

    [Fact]
    public void Resolve_SearchResultIsNotSectionName()
    {
        Assert.True(TreeRootHeaderResolver.Resolve(TreeRootSection.Favorites, true).IsSearchResult);
        Assert.False(TreeRootHeaderResolver.Resolve(TreeRootSection.Favorites, false).IsSearchResult);
    }

    [Fact]
    public void Resolve_AllSectionKeepsAllBasesMarkerOnlyWithoutSearch()
    {
        // Раздел «Все» без поиска — прежний узел «Все базы» (иконка базы);
        // остальные разделы и результат поиска — обычный узел без маркера.
        Assert.True(TreeRootHeaderResolver.Resolve(TreeRootSection.All, false).UseAllBasesMarker);

        foreach (var section in Enum.GetValues<TreeRootSection>())
        {
            Assert.False(TreeRootHeaderResolver.Resolve(section, true).UseAllBasesMarker);
            if (section != TreeRootSection.All)
                Assert.False(TreeRootHeaderResolver.Resolve(section, false).UseAllBasesMarker);
        }
    }

    [Theory]
    [InlineData(ListViewMode.All, TreeRootSection.All)]
    [InlineData(ListViewMode.Favorites, TreeRootSection.Favorites)]
    [InlineData(ListViewMode.Recent, TreeRootSection.Recent)]
    [InlineData(ListViewMode.Running, TreeRootSection.Running)]
    public void FromListViewMode_MapsSections(ListViewMode mode, TreeRootSection expected)
    {
        Assert.Equal(expected, TreeRootHeaderResolver.FromListViewMode(mode));
    }

    // ===================== Счётчик и текст подписи =====================

    [Fact]
    public void ResolveHeader_EmptySearch_AppendsCounterToSectionName()
    {
        var header = TreeRootHeaderResolver.ResolveHeader(
            TreeRootSection.Favorites, searchActive: false, count: 12, key => key == TreeRootHeaderResolver.FavoritesKey ? "Избранное" : key);

        Assert.Equal("Избранное (12)", header);
    }

    [Fact]
    public void ResolveHeader_ActiveSearch_AppendsCounterToFound()
    {
        var header = TreeRootHeaderResolver.ResolveHeader(
            TreeRootSection.Recent, searchActive: true, count: 3, key => key == TreeRootHeaderResolver.FoundKey ? "Найдено" : key);

        Assert.Equal("Найдено (3)", header);
    }

    [Fact]
    public void ResolveHeader_EmptyList_ShowsZeroCounter()
    {
        // Пустой список: имя раздела сохраняется, счётчик — «(0)».
        var header = TreeRootHeaderResolver.ResolveHeader(
            TreeRootSection.Running, searchActive: false, count: 0, key => key == TreeRootHeaderResolver.RunningKey ? "Запущенные" : key);

        Assert.Equal("Запущенные (0)", header);
    }

    [Fact]
    public void FormatHeader_KeepsCurrentCounterFormat()
    {
        Assert.Equal("Все (7)", TreeRootHeaderResolver.FormatHeader("Все", 7));
        Assert.Equal("(0)", TreeRootHeaderResolver.FormatHeader(string.Empty, 0));
        Assert.Equal("(5)", TreeRootHeaderResolver.FormatHeader(null, 5));
    }

    // ===================== Узел дерева: подпись и маркер =====================

    [Fact]
    public void GroupNode_FavoritesWithoutSearch_ShowsSectionNameWithoutMarker()
    {
        var plan = TreeRootHeaderResolver.Resolve(TreeRootSection.Favorites, searchActive: false);
        var node = new GroupNodeViewModel(null);

        node.ResetFlatContent(
            Array.Empty<Infobase>(),
            displayName: LocalizationManager.T(plan.NameKey),
            marker: plan.UseAllBasesMarker ? GroupNodeViewModel.AllBasesMarker : null);

        Assert.Null(node.Marker);
        // LocalizationManager в тестовой среде не инициализирован и T возвращает ключ.
        Assert.Equal(TreeRootHeaderResolver.FavoritesKey, node.DisplayName);
        Assert.NotEqual(LocalizationManager.T(TreeRootHeaderResolver.FoundKey), node.DisplayName);
        Assert.Equal("(0)", node.GroupCountSuffix);
    }

    [Fact]
    public void GroupNode_AllWithoutSearch_KeepsAllBasesMarkerWithSectionKey()
    {
        var plan = TreeRootHeaderResolver.Resolve(TreeRootSection.All, searchActive: false);
        var node = new GroupNodeViewModel(null);

        node.ResetFlatContent(
            Array.Empty<Infobase>(),
            displayName: LocalizationManager.T(plan.NameKey),
            marker: plan.UseAllBasesMarker ? GroupNodeViewModel.AllBasesMarker : null);

        Assert.Equal(GroupNodeViewModel.AllBasesMarker, node.Marker);
        Assert.Equal(LocalizationManager.T(TreeRootHeaderResolver.AllKey), node.DisplayName);
    }

    [Fact]
    public void GroupNode_ActiveSearch_ShowsFoundWithoutMarker()
    {
        var plan = TreeRootHeaderResolver.Resolve(TreeRootSection.Recent, searchActive: true);
        var node = new GroupNodeViewModel(null);

        node.ResetFlatContent(
            Array.Empty<Infobase>(),
            displayName: LocalizationManager.T(plan.NameKey),
            marker: plan.UseAllBasesMarker ? GroupNodeViewModel.AllBasesMarker : null);

        Assert.Null(node.Marker);
        Assert.Equal(LocalizationManager.T(TreeRootHeaderResolver.FoundKey), node.DisplayName);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json", "Все", "Избранное", "Недавние", "Запущенные", "Найдено")]
    [InlineData("en.json", "All", "Favorites", "Recent", "Running", "Found")]
    public void Localization_HasTreeRootKeys(
        string file, string all, string favorites, string recent, string running, string found)
    {
        var strings = LoadStrings(file);

        Assert.Equal(all, strings[TreeRootHeaderResolver.AllKey]);
        Assert.Equal(favorites, strings[TreeRootHeaderResolver.FavoritesKey]);
        Assert.Equal(recent, strings[TreeRootHeaderResolver.RecentKey]);
        Assert.Equal(running, strings[TreeRootHeaderResolver.RunningKey]);
        Assert.Equal(found, strings[TreeRootHeaderResolver.FoundKey]);
    }

    [Fact]
    public void Localization_RuAndEn_HaveSameTreeRootKeySet()
    {
        var ru = LoadStrings("ru.json");
        var en = LoadStrings("en.json");
        var keys = TreeRootHeaderResolver.SectionKeys
            .Append(TreeRootHeaderResolver.FoundKey)
            .ToArray();

        foreach (var key in keys)
        {
            Assert.True(ru.ContainsKey(key), $"ru.json: нет ключа {key}");
            Assert.True(en.ContainsKey(key), $"en.json: нет ключа {key}");
        }

        // Набор новых ключей в обоих словарях совпадает (без «лишних» и «пропущенных»).
        Assert.Equal(
            keys.OrderBy(k => k, StringComparer.Ordinal),
            ru.Keys.Where(k => k.StartsWith("Main.TreeRoot.", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(
            keys.OrderBy(k => k, StringComparer.Ordinal),
            en.Keys.Where(k => k.StartsWith("Main.TreeRoot.", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Localization_NoRussianTextHardcodedInResolver()
    {
        var resolver = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TreeRootHeaderResolver.cs"));

        // Русские подписи живут только в ru.json: в коде — ключи локализации
        // (упоминания в XML-комментариях допустимы, строковых литералов быть не должно).
        Assert.DoesNotContain("\"Найдено\"", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Все\"", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Избранное\"", resolver, StringComparison.Ordinal);
    }

    // ===================== Source-гварды: обе платформы =====================

    [Fact]
    public void Wpf_ViewModel_UsesSharedResolver()
    {
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Theme.cs"));

        Assert.Contains("TreeRootHeaderResolver.Resolve(", vm, StringComparison.Ordinal);
        Assert.Contains("TreeRootHeaderResolver.FromListViewMode(_listViewMode)", vm, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(SearchText)", vm, StringComparison.Ordinal);
        // Прежняя безусловная подпись «Найдено» убрана.
        Assert.DoesNotContain("Main.FlatFound", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ViewModel_UsesSharedResolver()
    {
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));

        Assert.Contains("TreeRootHeaderResolver.Resolve(", vm, StringComparison.Ordinal);
        Assert.Contains("TreeRootHeaderResolver.FromListViewMode(ListViewModes.Parse(_listMode))", vm, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(SearchText)", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Main.FlatFound", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPlatforms_MapAllBasesMarkerToSharedSectionKey()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "GroupNodeViewModel.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "GroupNodeViewModel.Avalonia.cs"));

        Assert.Contains("AllBasesMarker => TreeRootHeaderResolver.AllKey", wpf, StringComparison.Ordinal);
        Assert.Contains("AllBasesMarker => TreeRootHeaderResolver.AllKey", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPlatforms_Views_ShowDisplayNameAndCounter()
    {
        // Подпись корня = DisplayName + GroupCountSuffix в обеих оболочках.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("{Binding DisplayName}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding GroupCountSuffix}", xaml, StringComparison.Ordinal);

        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("new Binding(\"DisplayName\")", tree, StringComparison.Ordinal);
        Assert.Contains("new Binding(\"GroupCountSuffix\")", tree, StringComparison.Ordinal);
    }
}
