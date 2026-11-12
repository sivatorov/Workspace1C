using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #123 «Одинаковые кнопки с разным действием» — регресс/недоисправленность.
/// Фикс v1.0.1.55 сделал шевроны групп (IconChevronRight/IconChevronDown), но в
/// наборах иконок, кроме Fluent, кнопка «Свернуть все группы» панели
/// (IconCollapseAllBox в Avalonia, PackIcon CollapseAll в WPF) рисовалась той же
/// геометрией, что кнопка компактного режима (IconCompress / PackIcon Compress):
/// icongen сопоставил CollapseAllBox и CollapseAll виду Compress. Пара
/// «Развернуть все группы» (ExpandAllBox / ExpandAll) была тоже почти неотличима —
/// стрелки от/к линии. Теперь в наборах эти значки — глифы ExpandAll/CollapseAll
/// (двойные шевроны), отличимые от Compress.
/// Тесты читают файлы словарей (как IconSetCoverageTests) и не требуют UI.
/// </summary>
public sealed class Issue123GroupIconTests
{
    /// <summary>Наборы, у которых есть собственный словарь геометрий.</summary>
    private static readonly string[] Sets = { "Outline", "Material", "MacOS", "WindowsXp", "Glass" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Themes")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }

    private static IReadOnlyDictionary<string, string> Geometries(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "Workspace1C", "Themes", file));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text,
                     "<(?:Stream)?Geometry x:Key=\"([^\"]+)\">([^<]+)</(?:Stream)?Geometry>"))
            result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return result;
    }

    /// <summary>Пара геометрий из словаря набора; WPF-словарь (xaml) содержит Pack-ключи.</summary>
    private static (string A, string B) Pair(
        IReadOnlyDictionary<string, string> map, string keyA, string keyB)
    {
        Assert.True(map.TryGetValue(keyA, out var a), $"Нет ключа {keyA}");
        Assert.True(map.TryGetValue(keyB, out var b), $"Нет ключа {keyB}");
        return (a, b);
    }

    /// <summary>
    /// Шевроны состояния группы: свёрнута — IconChevronRight, развёрнута —
    /// IconChevronDown (IconHelper разрешает ключи IconChevronRight/IconChevronDown
    /// через <see cref="Themes.ThemeFeatureMap.IconResolutionCandidates"/>: вариант
    /// набора, при отсутствии — базовый Fluent). В каждом наборе EFFECTIVE-геометрии
    /// состояний должны быть РАЗНЫМИ, иначе состояния неразличимы.
    /// </summary>
    [Theory]
    [InlineData("Icons.xaml")]
    [InlineData("Icons.axaml")]
    [InlineData("IconsOutline.xaml")]
    [InlineData("IconsOutline.axaml")]
    [InlineData("IconsMaterial.xaml")]
    [InlineData("IconsMaterial.axaml")]
    [InlineData("IconsMacOS.xaml")]
    [InlineData("IconsMacOS.axaml")]
    [InlineData("IconsWindowsXp.xaml")]
    [InlineData("IconsWindowsXp.axaml")]
    [InlineData("IconsGlass.xaml")]
    [InlineData("IconsGlass.axaml")]
    public void ChevronGroupStates_DifferInEverySet(string file)
    {
        // Вариант набора или (при отсутствии) базовый Fluent из словаря той же платформы.
        var baseFile = file.Contains(".axaml", StringComparison.Ordinal) ? "Icons.axaml" : "Icons.xaml";
        var suffix = file.StartsWith("Icons", StringComparison.Ordinal) && file != baseFile
            ? "." + file[5..file.IndexOf('.')].Replace(".axaml", string.Empty).Replace(".xaml", string.Empty)
            : string.Empty;

        var map = Geometries(file);
        var baseMap = Geometries(baseFile);

        var right = map.TryGetValue($"IconChevronRight{suffix}", out var r) ? r : baseMap["IconChevronRight"];
        var down = map.TryGetValue($"IconChevronDown{suffix}", out var d) ? d : baseMap["IconChevronDown"];

        Assert.NotEqual(right, down);
    }

    /// <summary>
    /// Кнопка «Свернуть все группы» панели (IconCollapseAllBox в Avalonia,
    /// Pack.CollapseAll в WPF через PackIconGeometryConverter) не должна совпадать
    /// с кнопкой компактного режима (IconCompress / Pack.Compress) — при этом
    /// совпадении действия было не различить во всех наборах, кроме Fluent.
    /// </summary>
    [Theory]
    [InlineData("Outline")]
    [InlineData("Material")]
    [InlineData("MacOS")]
    [InlineData("WindowsXp")]
    [InlineData("Glass")]
    public void CollapseAllButton_DiffersFromCompactModeButton(string set)
    {
        foreach (var ext in new[] { "xaml", "axaml" })
        {
            var map = Geometries($"Icons{set}.{ext}");

            var (collapseBox, compress) = Pair(map, $"IconCollapseAllBox.{set}", $"IconCompress.{set}");
            var expandBox = map[$"IconExpandAllBox.{set}"];
            // «Иконка разворота ≠ иконка сворота ≠ иконка компактного режима» (issue #123):
            // проверяются ОБЕ групповые кнопки панели против кнопки компактности.
            Assert.NotEqual(collapseBox, compress);
            Assert.NotEqual(expandBox, compress);
            Assert.NotEqual(collapseBox, expandBox);

            // WPF-словарь дополнительно содержит Pack-варианты значков.
            if (map.ContainsKey($"Pack.CollapseAll.{set}") && map.ContainsKey($"Pack.Compress.{set}"))
            {
                Assert.NotEqual(map[$"Pack.CollapseAll.{set}"], map[$"Pack.Compress.{set}"]);
                Assert.NotEqual(map[$"Pack.ExpandAll.{set}"], map[$"Pack.Compress.{set}"]);
            }
        }
    }

    /// <summary>
    /// Пара «Развернуть все / Свернуть все группы» на панели — разные глифы в
    /// каждом наборе (ExpandAll — шевроны наружу, CollapseAll — внутрь).
    /// </summary>
    [Theory]
    [InlineData("Outline")]
    [InlineData("Material")]
    [InlineData("MacOS")]
    [InlineData("WindowsXp")]
    [InlineData("Glass")]
    public void ExpandCollapseAllPair_DiffersInEverySet(string set)
    {
        foreach (var ext in new[] { "xaml", "axaml" })
        {
            var map = Geometries($"Icons{set}.{ext}");
            var (expand, collapse) = Pair(map, $"IconExpandAll.{set}", $"IconCollapseAll.{set}");
            Assert.NotEqual(expand, collapse);
        }
    }
}
