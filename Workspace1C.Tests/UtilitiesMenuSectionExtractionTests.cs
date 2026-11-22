using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи против дублирования меню и кнопок разделов (релиз 1.0.1.101, задача 3).
/// <para>
/// Навигация 1.0.1.99 заменила плоское меню «Утилиты» четырьмя сценарными разделами
/// («Серверы», «Обслуживание», «Инструменты», «Ещё»). Кнопка раздела должна открывать
/// СОДЕРЖИМОЕ раздела, а не сам раздел: иначе появляется дублирующий уровень
/// «кнопка Инструменты → подменю Инструменты». Эти тесты фиксируют:
/// </para>
/// <list type="bullet">
/// <item><see cref="UtilitiesMenuLayouts.ExtractSection"/> разворачивает раздел на уровень меню
/// без узла-раздела, сохраняя состав и порядок детей;</item>
/// <item>возвращённый <c>Root</c> — независимая копия списка, исходная компоновка не меняется;</item>
/// <item>в стандартной компоновке нет дублей пунктов и подгрупп;</item>
/// <item>UI-код содержит ровно одну кнопку на раздел и закрывает ранее открытое меню.</item>
/// </list>
/// UI-проверки читают исходники из корня репозитория (как AboutLicenseBlockTests/Issue71),
/// поэтому не требуют запуска WPF/Avalonia.
/// </summary>
public sealed class UtilitiesMenuSectionExtractionTests
{
    // ===================== 1. ExtractSection: содержимое раздела без дубля уровня =====================

    /// <summary>
    /// Для каждого раздела первого уровня <see cref="UtilitiesMenuCatalog.RootSections"/>:
    /// результат не null, <c>Root</c> не пуст, в нём НЕТ узла-группы с <c>GroupId</c> самого
    /// раздела (нет дублирующего уровня «кнопка → подменю того же названия»), а первый узел
    /// не является разделителем (меню не начинается с черты).
    /// </summary>
    [Fact]
    public void ExtractSection_EveryRootSection_HasNoDuplicateSectionLevel()
    {
        var layout = UtilitiesMenuLayouts.Default();

        foreach (var sectionId in UtilitiesMenuCatalog.RootSections)
        {
            var extracted = UtilitiesMenuLayouts.ExtractSection(layout, sectionId);

            Assert.NotNull(extracted);
            Assert.NotEmpty(extracted!.Root);
            Assert.DoesNotContain(extracted.Root, node =>
                string.Equals(node.Kind, "group", StringComparison.Ordinal)
                && string.Equals(node.GroupId, sectionId, StringComparison.Ordinal));
            Assert.NotEqual("separator", extracted.Root[0].Kind);
        }
    }

    /// <summary>
    /// Содержимое не потеряно и не переупорядочено: последовательность узлов результата
    /// совпадает с <c>Children</c> исходного узла раздела (сравнение по ключу
    /// <c>Kind + ":" + (GroupId ?? ItemId)</c>).
    /// </summary>
    [Fact]
    public void ExtractSection_EveryRootSection_PreservesChildrenOrder()
    {
        var layout = UtilitiesMenuLayouts.Default();

        foreach (var sectionId in UtilitiesMenuCatalog.RootSections)
        {
            var section = FindSection(layout, sectionId);
            Assert.NotNull(section);

            var extracted = UtilitiesMenuLayouts.ExtractSection(layout, sectionId);
            Assert.NotNull(extracted);

            var expected = section!.Children.Select(NodeKey).ToList();
            var actual = extracted!.Root.Select(NodeKey).ToList();
            Assert.Equal(expected, actual);
        }
    }

    // ===================== 2. Независимая копия списка =====================

    /// <summary>
    /// <see cref="UtilitiesMenuLayouts.ExtractSection"/> возвращает независимую копию списка:
    /// после <c>Add</c>/<c>Clear</c> в возвращённом <c>Root</c> исходная стандартная компоновка
    /// не меняется (количество детей раздела до и после совпадает).
    /// </summary>
    [Fact]
    public void ExtractSection_ReturnsIndependentCopyOfChildren()
    {
        var layout = UtilitiesMenuLayouts.Default();

        foreach (var sectionId in UtilitiesMenuCatalog.RootSections)
        {
            var section = FindSection(layout, sectionId);
            Assert.NotNull(section);
            var countBefore = section!.Children.Count;

            var extracted = UtilitiesMenuLayouts.ExtractSection(layout, sectionId);
            Assert.NotNull(extracted);

            extracted!.Root.Add(new UtilitiesMenuNode { Kind = "item", ItemId = "TempGuardItem" });
            Assert.Equal(countBefore, section.Children.Count);

            extracted.Root.Clear();
            Assert.Equal(countBefore, section.Children.Count);
            Assert.NotEmpty(section.Children);
        }
    }

    // ===================== 3. Краевые случаи =====================

    /// <summary>Несуществующий раздел (и null-компоновка) — <c>null</c>, а не исключение.</summary>
    [Fact]
    public void ExtractSection_UnknownSection_ReturnsNull()
    {
        var layout = UtilitiesMenuLayouts.Default();

        Assert.Null(UtilitiesMenuLayouts.ExtractSection(layout, "NoSuchSectionGuard"));
        Assert.Null(UtilitiesMenuLayouts.ExtractSection(layout, ""));
        Assert.Null(UtilitiesMenuLayouts.ExtractSection(null, UtilitiesMenuCatalog.GroupServers));
    }

    /// <summary>
    /// Раздел без детей — не <c>null</c>, но пустой <c>Root</c>: меню раздела открывается
    /// пустым, а не падает (построен временный <see cref="UtilitiesMenuLayout"/>).
    /// </summary>
    [Fact]
    public void ExtractSection_SectionWithoutChildren_ReturnsEmptyRoot()
    {
        const string sectionId = "EmptySectionGuard";
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion,
            Root = new List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Group(sectionId, new List<UtilitiesMenuNode>())
            }
        };

        var extracted = UtilitiesMenuLayouts.ExtractSection(layout, sectionId);

        Assert.NotNull(extracted);
        Assert.Empty(extracted!.Root);
    }

    // ===================== 4. Нет дублей пунктов в стандартной компоновке =====================

    /// <summary>
    /// В стандартной компоновке каждый <c>ItemId</c> встречается ровно один раз:
    /// дубли пунктов означают дубли меню (один и тот же пункт в двух местах).
    /// </summary>
    [Fact]
    public void DefaultLayout_EveryItemAppearsExactlyOnce()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new List<string>();
        CollectItemIds(layout.Root, ids);

        var duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ×{group.Count()}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "В стандартной компоновке есть дубли пунктов: " + string.Join(", ", duplicates));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Все пункты каталога, доступные не только в Linux (<c>LinuxOnly == false</c>),
    /// присутствуют в стандартной компоновке: ни одна функция не потеряна.
    /// </summary>
    [Fact]
    public void DefaultLayout_ContainsEveryNonLinuxCatalogItem()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectItemIds(layout.Root, ids);

        var missing = UtilitiesMenuCatalog.All
            .Where(item => !item.LinuxOnly)
            .Select(item => item.Id)
            .Where(id => !ids.Contains(id))
            .ToList();

        Assert.True(missing.Count == 0,
            "Пункты каталога отсутствуют в стандартной компоновке: " + string.Join(", ", missing));
    }

    // ===================== 5. Нет дублей подгрупп внутри раздела =====================

    /// <summary>
    /// В каждом разделе нет двух подгрупп с одинаковым <c>GroupId</c> (дубль подменю).
    /// Проверка рекурсивная — включая вложенные подгруппы.
    /// </summary>
    [Fact]
    public void DefaultLayout_Sections_HaveNoDuplicateSubgroupIds()
    {
        var layout = UtilitiesMenuLayouts.Default();

        foreach (var sectionId in UtilitiesMenuCatalog.RootSections)
        {
            var section = FindSection(layout, sectionId);
            Assert.NotNull(section);

            var groupIds = new List<string>();
            CollectGroupIds(section!.Children, groupIds);

            var duplicates = groupIds
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            Assert.True(duplicates.Count == 0,
                $"В разделе «{sectionId}» дублируются подгруппы: {string.Join(", ", duplicates)}");
        }
    }

    // ===================== 6. UI-стражи (исходники) =====================

    /// <summary>
    /// WPF: в разметке ровно по одной кнопке на каждый раздел
    /// (<c>Tag="Servers"</c>, <c>Tag="Maintenance"</c>, <c>Tag="Tools"</c>, <c>Tag="More"</c>).
    /// Дубли кнопок приводили бы к двум одинаковым меню на панели.
    /// </summary>
    [Fact]
    public void WpfMainWindow_HasExactlyOneButtonPerSection()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        foreach (var sectionId in UtilitiesMenuCatalog.RootSections)
        {
            var count = CountOccurrences(xaml, $"Tag=\"{sectionId}\"");
            Assert.True(count == 1,
                $"Кнопок раздела Tag=\"{sectionId}\" должно быть ровно 1, найдено: {count}");
        }
    }

    /// <summary>
    /// WPF-построитель меню раздела использует <see cref="UtilitiesMenuLayouts.ExtractSection"/>
    /// и НЕ оборачивает раздел обратно в узел-группу (старая конструкция
    /// <c>Root = new List<UtilitiesMenuNode> { node }</c> давала дублирующий уровень).
    /// </summary>
    [Fact]
    public void WpfMainWindow_BuildsSectionMenuFromExtractedContent()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));

        Assert.Contains("UtilitiesMenuLayouts.ExtractSection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Root = new List<UtilitiesMenuNode> { node }", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// WPF: при открытии меню раздела ранее открытое меню закрывается
    /// (поле <c>_openSectionMenu</c> + сброс <c>IsOpen = false</c>).
    /// </summary>
    [Fact]
    public void WpfMainWindow_ClosesPreviousSectionMenu()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));

        Assert.Contains("_openSectionMenu", source, StringComparison.Ordinal);
        Assert.Contains("IsOpen = false", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Avalonia: <c>OpenSectionMenu</c> закрывает предыдущее меню через <c>Close()</c>
    /// (в Avalonia сеттер <c>IsOpen</c> у <c>ContextMenu</c> недоступен).
    /// </summary>
    [Fact]
    public void AvaloniaMainWindow_ClosesPreviousSectionMenu()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        Assert.Contains("OpenSectionMenu", source, StringComparison.Ordinal);
        Assert.Contains("_openSectionMenu.Close()", source, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    /// <summary>Находит узел-раздел первого уровня по <c>GroupId</c>.</summary>
    private static UtilitiesMenuNode? FindSection(UtilitiesMenuLayout layout, string sectionId) =>
        layout.Root.FirstOrDefault(node =>
            string.Equals(node.Kind, "group", StringComparison.Ordinal)
            && string.Equals(node.GroupId, sectionId, StringComparison.Ordinal));

    /// <summary>Ключ узла для сравнения последовательностей: <c>Kind + ":" + (GroupId ?? ItemId)</c>.</summary>
    private static string NodeKey(UtilitiesMenuNode node) =>
        node.Kind + ":" + (node.GroupId ?? node.ItemId);

    /// <summary>Рекурсивно собирает все <c>ItemId</c> (включая вложенные подгруппы).</summary>
    private static void CollectItemIds(IEnumerable<UtilitiesMenuNode>? nodes, ICollection<string> target)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(node.ItemId))
            {
                target.Add(node.ItemId!);
            }
            if (node.Children is { Count: > 0 })
                CollectItemIds(node.Children, target);
        }
    }

    /// <summary>Рекурсивно собирает <c>GroupId</c> всех подгрупп (включая вложенные).</summary>
    private static void CollectGroupIds(IEnumerable<UtilitiesMenuNode>? nodes, ICollection<string> target)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(node.GroupId))
            {
                target.Add(node.GroupId!);
            }
            if (node.Children is { Count: > 0 })
                CollectGroupIds(node.Children, target);
        }
    }

    /// <summary>Корень репозитория — вверх от каталога тестов до файла <c>Workspace1C.slnx</c>.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Читает файл проекта из корня репозитория.</summary>
    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Количество неперекрывающихся вхождений подстроки.</summary>
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
