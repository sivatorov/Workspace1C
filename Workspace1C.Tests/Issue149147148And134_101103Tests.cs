using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регрессионные стражи для исправлений 1.0.1.103:
/// #149 (подсветка первой вкладки настроек), #148 (короткий текст пункта меню),
/// #147 (базовые геометрии иконок и отказ от шрифтовых PackIcon в кнопках),
/// #146 (сноска PRO и кнопки разделов без текста), #139 (увеличенное окно
/// обновления и рамка описания), #134 (возврат фокуса после завершения процесса).
/// </summary>
public sealed class Issue149147148And134_101103Tests
{
    // ===================== #149: подсветка раздела «Настройки» =====================

    [Fact]
    public void Issue149_Wpf_SettingsGroupTabHasSelectedState()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var group = Slice(xaml, "x:Key=\"SettingsGroupTabItem\"", "x:Key=\"SettingsSubTabControl\"");

        Assert.Contains("Property=\"IsSelected\" Value=\"True\"", group, StringComparison.Ordinal);
        // Доработка 1.0.1.104: полоса 3 px зарезервирована в шаблоне в любом
        // состоянии, а выделение только красит рамку — контент не смещается.
        Assert.Contains("BorderThickness=\"3,0,0,0\"", group, StringComparison.Ordinal);
        Assert.Contains("BorderBrush\" Value=\"{DynamicResource AccentBrush}\"", group, StringComparison.Ordinal);
        Assert.Contains("ItemHoverBrush", group, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue149_Avalonia_SettingsGroupTabHasSelectedState()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = Slice(xaml, "x:Key=\"SettingsGroupTabItem\"", "Переключатель настроек");

        Assert.Contains("^:selected /template/ Border#PART_GroupSurface", theme, StringComparison.Ordinal);
        Assert.Contains("AccentBrush", theme, StringComparison.Ordinal);
    }

    // ===================== #148: короткий текст пункта меню =====================

    [Fact]
    public void Issue148_CheckAvailabilityMenuUsesShortTitle()
    {
        var catalog = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UtilitiesMenuCatalog.cs"));
        Assert.Contains("\"Main.CheckAvailabilityMenu\"", catalog, StringComparison.Ordinal);

        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains("\"Main.CheckAvailabilityMenu\": \"Проверить доступность всех баз 1С\"", ru,
            StringComparison.Ordinal);
        Assert.Contains("\"Main.CheckAvailabilityMenu\": \"Check availability of all 1C infobases\"", en,
            StringComparison.Ordinal);
    }

    // ===================== #147: иконки кнопок =====================

    [Theory]
    [InlineData("IconCheck")]
    [InlineData("IconClose")]
    [InlineData("IconSave")]
    [InlineData("IconAlertCircleOutline")]
    [InlineData("IconArrowUp")]
    [InlineData("IconArrowDown")]
    [InlineData("IconArrowRight")]
    [InlineData("IconBullet")]
    [InlineData("IconFolderOpen")]
    [InlineData("IconLink")]
    [InlineData("IconPackage")]
    [InlineData("IconSortAscending")]
    [InlineData("IconSortDescending")]
    public void Issue147_BaseIconSetDefinesGeometry(string key)
    {
        var icons = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Icons.xaml"));
        Assert.Contains($"x:Key=\"{key}\"", icons, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue147_DialogButtonsDoNotUseFontPackIcons()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        foreach (var file in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("PackIcon Kind=\"ContentSave\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PackIcon Kind=\"Check\"", text, StringComparison.Ordinal);
        }
    }

    // ===================== #146: меню без сноски PRO и без текста на кнопках =====================

    [Fact]
    public void Issue146_ProFootnoteAndBadgeRemoved()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.DoesNotContain("Pro.Footnote", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("Pro.Footnote", avalonia, StringComparison.Ordinal);

        var catalog = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UtilitiesMenuCatalog.cs"));
        var context = ReadProjectFile(Path.Combine("Workspace1C", "Services", "ContextMenuCatalog.cs"));
        Assert.DoesNotContain("Pro.Badge", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Pro.Badge", context, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue146_SectionButtonsAreIconOnlyInBothShells()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var servers = Slice(xaml, "x:Name=\"ServersSectionButton\"", "</Button>");
        Assert.DoesNotContain("<TextBlock", servers, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("TopBarIconButton(sectionIcon, sectionTitle", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue146_SeparatorBeforeQuickButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var tail = Slice(xaml, "x:Name=\"UtilitiesFallbackButton\"", "OnQuickThemeButton_Click");
        Assert.Contains("<Border Width=\"1\"", tail, StringComparison.Ordinal);
    }

    // ===================== #139: окно обновления =====================

    [Fact]
    public void Issue139_UpdateWindowIsBiggerAndBodyIsFramed()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("MinWidth=\"680\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"160\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"420\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource BorderBrushColor}\"", xaml, StringComparison.Ordinal);
    }

    // ===================== #134: фокус после завершения процесса =====================

    [Fact]
    public void Issue134_FocusIsRestoredAfterKillInBothShells()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs"));

        Assert.Contains("_restoreFocusAfterKill", wpf, StringComparison.Ordinal);
        Assert.Contains("KillSelectedAndRestoreFocus", wpf, StringComparison.Ordinal);
        Assert.Contains("QueueGridFocusAfterKill", wpf, StringComparison.Ordinal);

        Assert.Contains("_restoreFocusAfterKill", avalonia, StringComparison.Ordinal);
        Assert.Contains("KillSelectedAndRestoreFocus", avalonia, StringComparison.Ordinal);
        Assert.Contains("RestoreFocusAfterKill", avalonia, StringComparison.Ordinal);
    }

    // ===================== helpers =====================

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Не найден фрагмент начала: {start}");
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"Не найден фрагмент конца: {end}");
        return text.Substring(from, to - from);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var full = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(full), $"Файл не найден: {full}");
        return File.ReadAllText(full);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
