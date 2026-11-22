using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (1.0.1.106): редактор меню «Утилиты» падал с
/// <c>XamlParseException: Не удается найти ресурс с именем "PackIconKindConverter"</c>
/// при выборе группы — конвертер использовался в <c>ComboBox.ItemTemplate</c>,
/// но не был объявлен в <c>Window.Resources</c>.
/// <para>
/// Страж-тесты проверяют, что каждый конвертер, на который ссылается разметка
/// редакторов меню, объявлен в том же окне и объявлен ДО первого использования
/// (StaticResource резолвится сверху вниз).
/// </para>
/// </summary>
public sealed class Issue146MenuEditorResourcesTests
{
    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml")]
    [InlineData("TrayMenuEditorWindow.xaml")]
    [InlineData("ContextMenuEditorWindow.xaml")]
    public void ConverterStaticResources_AreDeclaredInWindow(string fileName)
    {
        var xaml = ReadView(fileName);

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     xaml, @"Converter=\{StaticResource\s+([A-Za-z0-9_]+)\}"))
        {
            used.Add(match.Groups[1].Value);
        }

        Assert.NotEmpty(used);
        foreach (var key in used)
        {
            Assert.True(
                Regex.IsMatch(xaml, "x:Key=\"" + Regex.Escape(key) + "\""),
                $"{fileName}: конвертер '{key}' используется, но не объявлен в Window.Resources. " +
                "В рантайме это даёт XamlParseException и падение окна (issue #146).");
        }
    }

    [Fact]
    public void UtilitiesEditor_DeclaresPackIconKindConverter()
    {
        var xaml = ReadView("UtilitiesMenuEditorWindow.xaml");

        Assert.Contains(
            "Converter={StaticResource PackIconKindConverter}",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Key=\"PackIconKindConverter\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UtilitiesEditor_DeclaresConverterBeforeUse()
    {
        var xaml = ReadView("UtilitiesMenuEditorWindow.xaml");

        var declaration = xaml.IndexOf("x:Key=\"PackIconKindConverter\"", StringComparison.Ordinal);
        var usage = xaml.IndexOf(
            "Converter={StaticResource PackIconKindConverter}",
            StringComparison.Ordinal);

        Assert.True(declaration >= 0 && usage >= 0,
            "Объявление и/или использование PackIconKindConverter не найдены в UtilitiesMenuEditorWindow.xaml.");
        Assert.True(declaration < usage,
            "PackIconKindConverter должен быть объявлен ДО использования: " +
            "StaticResource в WPF резолвится сверху вниз.");
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
