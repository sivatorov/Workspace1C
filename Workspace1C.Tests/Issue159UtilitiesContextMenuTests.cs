using System;
using System.IO;
using System.Linq;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #159 «Linux — правый клик на Утилиты», вторая итерация (v1.0.1.121):
/// <list type="bullet">
/// <item>иконка узла в Avalonia-дереве редактора строится по ключу геометрии
/// <c>Icon*</c> (<c>DisplayIconGeometryKey</c>), а не по PackIcon-имени каталога —
/// иначе все узлы получали запасную «папку»;</item>
/// <item>цвет иконки разрешается через <c>ThemeBrushes.FromSpec</c> (HEX или
/// «@Кисть»), а не через перегрузку <c>MakeIcon(..., string)</c>, трактующую
/// «@Key» как имя ресурса без префикса;</item>
/// <item>вся строка узла — область клика: прозрачный фон, растяжение, MinHeight,
/// курсор-рука и обработчик ПКМ на контейнере строки;</item>
/// <item>раскрыватель дерева занимает всю колонку и высоту строки в Avalonia и
/// WPF-темах, содержимое строки растягивается — клик не требует попадания в
/// маленький шеврон;</item>
/// <item>паритет оболочек: контекстное меню «Утилиты» и оба входа в редактор
/// доступны и в WPF, и в Avalonia.</item>
/// </list>
/// </summary>
public sealed class Issue159UtilitiesContextMenuTests
{
    // ===================== 1. Иконка узла: геометрия и кисть =====================

    [Fact]
    public void EditorTreeIcon_UsesGeometryKey_NotCatalogPackIconKey()
    {
        var editor = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        var row = Slice(editor, "private Control BuildTreeRow(", "private Control BuildOperationsPanel(");

        // PackIcon-имя каталога («Folder», «Sync») — не геометрия Icon*: прежний
        // вызов MakeIcon(node.DisplayIconKey, …) давал запасную «папку» у всех узлов.
        Assert.Contains("node.DisplayIconGeometryKey", row, StringComparison.Ordinal);
        Assert.DoesNotContain("MakeIcon(node.DisplayIconKey", row, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorTreeIcon_BrushKeyIsResolvable_NoRawAtSpecInMakeIcon()
    {
        var editor = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        var row = Slice(editor, "private Control BuildTreeRow(", "private Control BuildOperationsPanel(");

        // Цвет — через FromSpec: строка «@AccentBrush» разрешается в кисть ресурса,
        // HEX — в SolidColorBrush. Перегрузка MakeIcon(..., string) префикс «@» не понимает.
        Assert.Contains("ThemeBrushes.FromSpec(colorSpec)", row, StringComparison.Ordinal);
        // Запасной ключ — существующий ресурс темы (без префикса «@»).
        Assert.Contains("\"TextPrimaryBrush\"", row, StringComparison.Ordinal);
        // Ни один вызов MakeIcon не получает «@…» как ключ ресурса.
        Assert.DoesNotContain("\"@", row, StringComparison.Ordinal);

        // Ключ TextPrimaryBrush есть в обеих палитрах (светлая и тёмная).
        foreach (var themeFile in new[] { "LightTheme.axaml", "DarkTheme.axaml" })
        {
            var theme = ReadProjectFile("Workspace1C", "Themes", themeFile);
            Assert.Contains("x:Key=\"TextPrimaryBrush\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\"AccentBrush\"", theme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeometryKey_DoesNotDependOnExpansion()
    {
        var vm = new UtilitiesMenuEditorViewModel(null);
        var group = vm.Roots.FirstOrDefault(n => n.Children.Count > 0);
        Assert.NotNull(group);

        // Раскрытие — состояние контейнера TreeViewItem, а не узла: ключ иконки
        // пересчитывается стабильно и не «превращается в точку» при раскрытии.
        var first = group!.DisplayIconGeometryKey;
        Assert.False(string.IsNullOrWhiteSpace(first),
            "У группы должен быть разрешаемый ключ геометрии иконки.");
        Assert.Equal(first, group.DisplayIconGeometryKey);
        Assert.Equal(group.HasDisplayIcon, !string.IsNullOrWhiteSpace(group.DisplayIconGeometryKey));

        var vmSource = ReadProjectFile("Workspace1C", "ViewModels", "UtilitiesMenuEditorViewModel.cs");
        var getter = Slice(vmSource, "public string DisplayIconGeometryKey", "/// <summary>Цвет иконки");
        Assert.DoesNotContain("IsExpanded", getter, StringComparison.Ordinal);
    }

    // ===================== 2. Область клика строки узла =====================

    [Fact]
    public void EditorTreeRow_IsClickableAcrossWholeRow()
    {
        var editor = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        var row = Slice(editor, "private Control BuildTreeRow(", "private Control BuildOperationsPanel(");

        // Прозрачный фон обязателен для hit-test прозрачных областей.
        Assert.Contains("Background = Brushes.Transparent", row, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", row, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 24", row, StringComparison.Ordinal);
        Assert.Contains("Cursor = new Cursor(StandardCursorType.Hand)", row, StringComparison.Ordinal);

        // ПКМ обрабатывается контейнером строки, а не только глифом/иконкой.
        Assert.Contains("row.PointerPressed +=", row, StringComparison.Ordinal);
        Assert.Contains("IsRightButtonPressed", row, StringComparison.Ordinal);
        Assert.Contains("_tree.SelectedItem = node", row, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", row, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaTreeTheme_ExpanderFillsRow_AndPresenterStretches()
    {
        var xaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var theme = Slice(xaml, "<ControlTheme x:Key=\"ModernTreeItem\"", "</ControlTheme>");

        // Раскрыватель — на всю колонку 24 и высоту строки, фон прозрачный.
        Assert.Contains("MinWidth=\"24\" MinHeight=\"24\"", theme, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"",
            theme, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"20\" Height=\"22\"", theme, StringComparison.Ordinal);

        // Содержимое строки растягивается на всю ширину колонки.
        Assert.Contains("HorizontalAlignment=\"Stretch\"", theme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void WpfTreeTheme_ExpanderFillsRow_AndPresenterStretches(string themeFile)
    {
        var xaml = ReadProjectFile("Workspace1C", "Themes", themeFile);
        var theme = Slice(xaml, "x:Key=\"ModernTreeViewItem\"", "</Style>");

        Assert.Contains("MinWidth=\"22\" MinHeight=\"22\"", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"22\" Height=\"22\"", theme, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", theme, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfEditorRow_IsStretchAndTransparent()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.xaml");
        var template = Slice(
            xaml,
            "<HierarchicalDataTemplate x:Key=\"MenuNodeTemplate\"",
            "</HierarchicalDataTemplate>");

        // WPF-паритет: та же строка-контейнер (прозрачный фон, растяжение, MinHeight),
        // иконка — по ключу геометрии (DisplayIconGeometryKey).
        Assert.Contains("HorizontalAlignment=\"Stretch\"", template, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", template, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"24\"", template, StringComparison.Ordinal);
        Assert.Contains("DisplayIconGeometryKey", template, StringComparison.Ordinal);
    }

    // ===================== 3. Паритет контекстного меню оболочек =====================

    [Fact]
    public void ContextMenu_AvailableInBothShells()
    {
        // WPF: меню «Утилиты» — ContextMenu, ПКМ по кнопкам/чертам — редактор.
        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");
        Assert.Contains("new ContextMenu", wpf, StringComparison.Ordinal);
        Assert.Contains(
            "button.PreviewMouseRightButtonDown += OnUtilitiesButton_PreviewMouseRightButtonDown",
            wpf, StringComparison.Ordinal);
        Assert.Contains(
            "line.PreviewMouseRightButtonDown += OnUtilitiesButton_PreviewMouseRightButtonDown",
            wpf, StringComparison.Ordinal);

        // Avalonia: меню раздела открывается у кнопки, ПКМ по кнопке/черте — редактор.
        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");
        Assert.Contains("new ContextMenu().Styled(Themes.ControlThemes.ModernContextMenu)",
            avalonia, StringComparison.Ordinal);
        Assert.Contains("IsRightButtonPressed", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenUtilitiesMenuEditorDirectly();", avalonia, StringComparison.Ordinal);
        Assert.Contains("menu.Open(anchor)", avalonia, StringComparison.Ordinal);

        // Оба входа Avalonia идут через единую точку открытия редактора.
        var columns = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs");
        Assert.Contains("UtilitiesMenuEditorWindow.ShowEditor(this)", columns, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return text.Substring(start, end - start);
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
