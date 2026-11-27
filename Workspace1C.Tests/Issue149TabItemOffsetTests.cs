using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #149, доработка 1.0.1.104: первая вкладка-раздел «Настройки» не должна
/// смещаться при получении выделения. Корневая причина — у группы
/// <c>SettingsGroupTabItem</c> рамка в покое была нулевой, а по <c>IsSelected</c>
/// появлялась акцентная полоса <c>BorderThickness="3,0,0,0"</c>: рамка «отъедала»
/// 3 px, и надпись с иконкой уезжала вправо. Исправление — полоса зарезервирована
/// в шаблоне всегда, а выделение меняет только цвет рамки. Тесты проверяют
/// инвариант «горизонтальный отступ контента одинаков до и после выделения»
/// для WPF и Avalonia, включая компактный режим.
/// </summary>
public sealed class Issue149TabItemOffsetTests
{
    // ===================== Расчёт отступа (TabItemLayout) =====================

    [Fact]
    public void AccentBarThickness_IsThreeDips()
    {
        Assert.Equal(3, TabItemLayout.AccentBarThickness);
    }

    [Fact]
    public void ContentLeftOffset_AddsReservedBorderAndPadding()
    {
        // Рамка 3 px + левый padding 14 = 17 px от левого края вкладки.
        Assert.Equal(17, TabItemLayout.ContentLeftOffset(TabItemLayout.AccentBarThickness, 14));
    }

    [Fact]
    public void SelectionShift_IsZeroWhenBorderReservedInBothStates()
    {
        Assert.Equal(0, TabItemLayout.SelectionShift(
            TabItemLayout.AccentBarThickness, TabItemLayout.AccentBarThickness));
    }

    [Fact]
    public void SelectionShift_DocumentsOldBugWhenBorderAppearsOnSelection()
    {
        // Старое поведение: в покое 0, при выделении 3 — сдвиг контента вправо.
        Assert.Equal(3, TabItemLayout.SelectionShift(0, TabItemLayout.AccentBarThickness));
    }

    // ===================== WPF =====================

    [Fact]
    public void Wpf_SettingsGroupTabReservesAccentBarInBothStates()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var itemStyle = Slice(xaml, "x:Key=\"SettingsTabItem\"", "x:Key=\"SettingsGroupTabItem\"");
        var groupStyle = Slice(xaml, "x:Key=\"SettingsGroupTabItem\"", "x:Key=\"SettingsSubTabControl\"");

        var paddingLeft = Left(SetterValue(itemStyle, "Padding"));
        var itemBorderLeft = Left(Attribute(itemStyle, "BorderThickness"));
        var groupBorderLeft = Left(Attribute(groupStyle, "BorderThickness"));

        // У обеих вкладок место под полосу зарезервировано одинаково.
        Assert.Equal(TabItemLayout.AccentBarThickness, itemBorderLeft);
        Assert.Equal(TabItemLayout.AccentBarThickness, groupBorderLeft);

        // Выделение не меняет геометрию: толщина рамки в триггере не задаётся.
        Assert.DoesNotContain("BorderThickness",
            Slice(itemStyle, "Property=\"IsSelected\" Value=\"True\"", "</Trigger>"), StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness",
            Slice(groupStyle, "Property=\"IsSelected\" Value=\"True\"", "</Trigger>"), StringComparison.Ordinal);

        // Отступ контента одинаков у обычной и у группы, до и после выделения.
        var itemOffset = TabItemLayout.ContentLeftOffset(itemBorderLeft, paddingLeft);
        var groupOffset = TabItemLayout.ContentLeftOffset(groupBorderLeft, paddingLeft);
        Assert.Equal(itemOffset, groupOffset);
        Assert.Equal(0, TabItemLayout.SelectionShift(groupBorderLeft, groupBorderLeft));
    }

    // ===================== Avalonia =====================

    [Fact]
    public void Avalonia_SettingsGroupTabReservesAccentBarInBothStates()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var connStyle = Slice(xaml, "x:Key=\"ConnTabItem\"", "x:Key=\"OptionCard\"");
        var groupStyle = Slice(xaml, "x:Key=\"SettingsGroupTabItem\"", "Переключатель настроек");

        var paddingLeft = Left(SetterValue(connStyle, "Padding"));
        var connBorderLeft = Left(Attribute(connStyle, "BorderThickness"));
        var groupBorderLeft = Left(Attribute(groupStyle, "BorderThickness"));

        // SettingsTabItem наследует ConnTabItem, SettingsGroupTabItem — тоже.
        Assert.Equal(TabItemLayout.AccentBarThickness, connBorderLeft);
        Assert.Equal(TabItemLayout.AccentBarThickness, groupBorderLeft);

        // Состояние :selected меняет фон и цвет рамки, но не её толщину.
        Assert.DoesNotContain("BorderThickness",
            Slice(connStyle, "^:selected /template/ Border#PART_Surface", "</Style>"), StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness",
            Slice(groupStyle, "^:selected /template/ Border#PART_GroupSurface", "</Style>"), StringComparison.Ordinal);

        var connOffset = TabItemLayout.ContentLeftOffset(connBorderLeft, paddingLeft);
        var groupOffset = TabItemLayout.ContentLeftOffset(groupBorderLeft, paddingLeft);
        Assert.Equal(connOffset, groupOffset);
        Assert.Equal(0, TabItemLayout.SelectionShift(groupBorderLeft, groupBorderLeft));
    }

    // ===================== Компактный режим =====================

    [Fact]
    public void Avalonia_CompactModeScalesWidthFontAndIconButNotTabPadding()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        var mainTab = Slice(code, "private static TabItem MainTab(", "private static ToggleButton SettingsSwitch(");
        // Компактный режим уменьшает ширину, кегль и значок вкладки...
        // Issue #175 (1.0.1.128): ширина вкладки увеличена до 260 DIP (MinWidth 240).
        Assert.Contains("UiMetrics.Scaled(260)", mainTab, StringComparison.Ordinal);
        Assert.Contains("UiMetrics.ScaledFont(13)", mainTab, StringComparison.Ordinal);
        Assert.Contains("UiMetrics.Scaled(UiMetrics.IconToolbar)", mainTab, StringComparison.Ordinal);
        // ...но не трогает рамку вкладки: смещения при выделении нет. Паддинг
        // заголовка задан фиксированным (не через UiMetrics.Scaled), поэтому
        // компактный режим его не масштабирует.
        Assert.Contains("tab.Padding = new Thickness(12, 10);", mainTab, StringComparison.Ordinal);
        Assert.DoesNotContain("UiMetrics.Scaled(12), 10", mainTab, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness", mainTab, StringComparison.Ordinal);

        var subTab = Slice(code, "private static TabItem SubTab(", "private static Control SettingsGroup(");
        Assert.DoesNotContain("BorderThickness", subTab, StringComparison.Ordinal);
        Assert.DoesNotContain("tab.Padding", subTab, StringComparison.Ordinal);
        Assert.Contains("UiMetrics.ScaledFont(12)", subTab, StringComparison.Ordinal);
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

    private static string Attribute(string text, string name)
    {
        var match = Regex.Match(text, name + "=\"([^\"]+)\"");
        Assert.True(match.Success, $"Не найден атрибут {name}");
        return match.Groups[1].Value;
    }

    private static string SetterValue(string text, string property)
    {
        var match = Regex.Match(text, "Property=\"" + Regex.Escape(property) + "\" Value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Не найден сеттер свойства {property}");
        return match.Groups[1].Value;
    }

    private static double Left(string thickness)
    {
        var first = thickness.Split(',')[0].Trim();
        return double.Parse(first, CultureInfo.InvariantCulture);
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
