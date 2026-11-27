using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #152 (1.0.1.106): страница настроек правки меню перекомпонована.
/// Все галочки называются «Правка структуры», а длинные описания перенесены
/// в правую колонку: слева галочка и кнопка правки, справа текст. Это убирает
/// лишнюю вертикальную прокрутку вкладки.
/// </summary>
public sealed class Issue152SettingsLayoutTests
{
    [Fact]
    public void Wpf_AllMenuEditingCheckboxesUseCommonLabel()
    {
        var xaml = ReadView("SettingsWindow.xaml");

        Assert.Equal(3, Count(xaml, "Content=\"{loc:Loc Settings.MenuEditing.Enabled}\""));
        Assert.DoesNotContain(
            "Content=\"{loc:Loc Settings.UtilitiesMenu.EditingEnabled}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"{loc:Loc Settings.TrayMenu.EditingEnabled}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"{loc:Loc Settings.ContextMenu.EditingEnabled}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_FourGroupsUseTwoColumnLayout()
    {
        var xaml = ReadView("SettingsWindow.xaml");
        var menuTab = SliceMenuTab(xaml);

        // Четыре группы: «Утилиты», «Мои программы», «Меню трея», «Контекстное меню».
        Assert.Equal(4, Count(menuTab, "<ColumnDefinition Width=\"210\"/>"));
        Assert.Equal(4, Count(menuTab, "<ColumnDefinition Width=\"*\"/>"));
    }

    [Theory]
    [InlineData("Settings.UtilitiesMenu.EditingEnabledTooltip")]
    [InlineData("Launch.CustomPrograms.SettingsHint")]
    [InlineData("Settings.TrayMenu.Hint")]
    [InlineData("Settings.ContextMenu.Hint")]
    public void Wpf_DescriptionsAreInRightColumn(string hintKey)
    {
        var xaml = ReadView("SettingsWindow.xaml");
        var menuTab = SliceMenuTab(xaml);

        Assert.Contains(
            $"Grid.Column=\"1\" Text=\"{{loc:Loc {hintKey}}}\"",
            menuTab,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_UsesCommonLabelAndTwoColumnRows()
    {
        var source = ReadView("SettingsWindow.Avalonia.cs");

        Assert.Equal(3, Count(source, "\"Settings.MenuEditing.Enabled\""));
        Assert.Contains("MenuSettingsRow(", source, StringComparison.Ordinal);
        Assert.Contains("MenuEditButton(", source, StringComparison.Ordinal);
        // Issue #174 (1.0.1.129): фиксированная колонка 210 DIP убрана — переключатель
        // растягивается на всю строку (дорожка у правого края), а строка «кнопка + описание»
        // строится по шаблону «Auto,*».
        Assert.DoesNotContain("new ColumnDefinitions(\"210,*\")", source, StringComparison.Ordinal);
        Assert.Contains(
            "check.HorizontalAlignment = HorizontalAlignment.Stretch",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru", "Правка структуры")]
    [InlineData("en", "Edit structure")]
    public void Localization_DefinesCommonLabel(string language, string expected)
    {
        var path = Path.Combine(
            FindRepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
        Assert.True(File.Exists(path), $"Не найден файл локализации: {path}");

        var json = File.ReadAllText(path);
        Assert.True(
            json.Contains(
                $"\"Settings.MenuEditing.Enabled\": \"{expected}\"",
                StringComparison.Ordinal),
            $"{language}.json ({path}): нет ключа/значения " +
            $"Settings.MenuEditing.Enabled = «{expected}».");
    }

    /// <summary>Фрагмент разметки вкладки «Меню „Утилиты“» (issue #152).</summary>
    private static string SliceMenuTab(string xaml)
    {
        const string start = "<!-- Меню «Утилиты» (issue #374; компоновка — issue #152)";
        const string end = "<!-- ============ Вкладка «Платформы»";
        var from = xaml.IndexOf(start, StringComparison.Ordinal);
        var to = xaml.IndexOf(end, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "Не найден фрагмент вкладки правки меню в SettingsWindow.xaml");
        return xaml[from..to];
    }

    private static int Count(string text, string value)
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
