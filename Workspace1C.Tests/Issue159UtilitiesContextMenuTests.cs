using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Services;
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

    // ===================== 4. Issue #159 (1.0.1.123): раскрыватель и иконки =====================

    [Fact]
    public void AvaloniaTreeTheme_ExpanderUsesTwoExplicitGlyphs_NoRenderTransform()
    {
        var xaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var theme = Slice(xaml, "<ControlTheme x:Key=\"ModernTreeItem\"", "</ControlTheme>");

        // Два явных глифа по состоянию: свёрнуто — вправо, раскрыто — вниз.
        Assert.Contains("IconChevronRight", theme, StringComparison.Ordinal);
        Assert.Contains("IconChevronDown", theme, StringComparison.Ordinal);
        Assert.Contains(
            "Selector=\"ToggleButton:checked /template/ Path#ExpanderGlyph\"",
            theme, StringComparison.Ordinal);

        // Поворот больше не используется — именно он «схлопывал» глиф в точку.
        Assert.DoesNotContain("RenderTransform", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("RotateTransform", theme, StringComparison.Ordinal);

        // Размер и растяжение глифа сохранены.
        Assert.Contains("Width=\"16\" Height=\"16\" Stretch=\"Uniform\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenuEditor_UsesGeometryKey_NotRawPackIconName()
    {
        var editor = ReadProjectFile("Workspace1C", "Views", "ContextMenuEditorWindow.Avalonia.cs");
        var row = Slice(editor, "private Control BuildTreeRow(", "private Control BuildOperationsPanel(");

        // PackIcon-имя каталога в Avalonia не рисуется — только ключ геометрии Icon*.
        Assert.Contains("node.DisplayIconGeometryKey", row, StringComparison.Ordinal);
        Assert.DoesNotContain("MakeIcon(node.DisplayIconKey", row, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.FromSpec(colorSpec)", row, StringComparison.Ordinal);
        // Единый осмысленный фолбэк.
        Assert.Contains("\"IconFolder\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayEditor_UsesUnifiedFolderFallback()
    {
        var editor = ReadProjectFile("Workspace1C", "Views", "TrayMenuEditorWindow.Avalonia.cs");
        var row = Slice(editor, "private Control BuildTreeRow(", "private Control BuildOperationsPanel(");

        Assert.Contains("WpfIconGeometryKey(node.DisplayIconKey)", row, StringComparison.Ordinal);
        Assert.Contains("geometryKey = \"IconFolder\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorIcons_AreDistinctPerNodeType()
    {
        // Утилиты: разные типы узлов дают разные геометрические ключи.
        var utilities = new UtilitiesMenuEditorViewModel(null);
        var utilityKeys = FlattenUtilities(utilities.Roots)
            .Select(n => n.DisplayIconGeometryKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(utilityKeys.Count >= 3,
            $"Ожидались различимые иконки «Утилит», получено {utilityKeys.Count}: {string.Join(", ", utilityKeys)}");

        // Контекстное меню: раньше все пункты получали одну запасную «папку».
        var context = new ContextMenuEditorViewModel(null);
        var contextNodes = FlattenContext(context.Roots).ToList();
        Assert.NotEmpty(contextNodes);
        Assert.All(contextNodes, n => Assert.False(
            string.IsNullOrWhiteSpace(n.DisplayIconGeometryKey),
            "У каждого узла должен быть осмысленный ключ иконки или фолбэк."));

        var contextKeys = contextNodes
            .Select(n => n.DisplayIconGeometryKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(contextKeys.Count >= 3,
            $"Ожидались различимые иконки контекстного меню, получено {contextKeys.Count}: {string.Join(", ", contextKeys)}");

        // Группа и пункт не выглядят одинаково.
        var group = contextNodes.First(n => n.Node.Kind == "group");
        var differentItem = contextNodes.FirstOrDefault(n =>
            n.Node.Kind != "group" && n.Node.Kind != "separator" &&
            !string.Equals(n.DisplayIconGeometryKey, group.DisplayIconGeometryKey, StringComparison.Ordinal));
        Assert.NotNull(differentItem);

        // Разделитель — служебная иконка-линия, а не «папка».
        var separator = contextNodes.FirstOrDefault(n => n.Node.Kind == "separator");
        Assert.NotNull(separator);
        Assert.Equal("IconMinus", separator!.DisplayIconGeometryKey);
    }

    // ===================== 5. Issue #159 (1.0.1.123): размеры окон =====================

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    [InlineData("CustomProgramsWindow.Avalonia.cs")]
    public void EditorWindows_SizeToContentHeight_WithWorkAreaClamp(string fileName)
    {
        var source = ReadProjectFile("Workspace1C", "Views", fileName);

        // Высота — по содержимому, верхняя/нижняя границы — из политики.
        Assert.Contains("SizeToContent = Avalonia.Controls.SizeToContent.Height",
            source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = size.MinHeight", source, StringComparison.Ordinal);
        // Фиксированная высота больше не задаётся — иначе снова появился бы скролл.
        Assert.DoesNotContain("Height = size.Height", source, StringComparison.Ordinal);
        // Дерево/список ограничен и прокручивается сам.
        Assert.Contains("MaxHeight = Math.Max(200, size.MaxHeight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorProfiles_ClampContentHeightToWorkArea()
    {
        var profiles = new[]
        {
            DialogWorkAreaPolicy.UtilitiesEditor,
            DialogWorkAreaPolicy.TrayEditor,
            DialogWorkAreaPolicy.ContextMenuEditor,
            DialogWorkAreaPolicy.CustomPrograms,
        };

        foreach (var profile in profiles)
        {
            // Нормальный экран: высота по содержимому помещается, прокрутка не нужна.
            var normal = DialogWorkAreaPolicy.Recommend(profile, 1080);
            Assert.True(normal.Height <= 1080 - DialogWorkAreaPolicy.DefaultMargin + 0.5,
                $"Высота {normal.Height} выходит за рабочую область.");
            Assert.True(normal.MaxHeight <= 1080 - DialogWorkAreaPolicy.DefaultMargin + 0.5);
            Assert.True(normal.Height <= profile.ContentHeight + 0.5,
                "Высота не должна превышать высоту содержимого.");
            Assert.True(normal.MinHeight <= normal.MaxHeight,
                "MinHeight не должен побеждать MaxHeight на маленьком экране.");
            Assert.False(normal.RequiresScrolling,
                "На нормальном экране окно должно помещаться по содержимому без прокрутки.");

            // Маленький экран: окно клампится, прокрутка — аварийный режим.
            var small = DialogWorkAreaPolicy.Recommend(profile, 400);
            Assert.True(small.MaxHeight <= 400 - DialogWorkAreaPolicy.DefaultMargin + 0.5);
            Assert.True(small.Height <= small.MaxHeight + 0.5);
            Assert.True(small.MinHeight <= small.MaxHeight + 0.5);
            Assert.True(small.RequiresScrolling,
                "На маленьком экране содержимое не помещается — прокрутка обязательна.");
        }
    }

    [Fact]
    public void EditorProfile_MaxHeightCap_IsRespected()
    {
        // Профили с авторазмером имеют верхнюю границу, чтобы длинный список
        // не растягивал окно на весь экран.
        foreach (var profile in new[]
                 {
                     DialogWorkAreaPolicy.UtilitiesEditor,
                     DialogWorkAreaPolicy.TrayEditor,
                     DialogWorkAreaPolicy.ContextMenuEditor,
                     DialogWorkAreaPolicy.CustomPrograms,
                 })
        {
            Assert.False(double.IsPositiveInfinity(profile.MaxHeight),
                "У профилей редакторов должна быть верхняя граница MaxHeight.");
            var size = DialogWorkAreaPolicy.Recommend(profile, 4000);
            Assert.True(size.MaxHeight <= profile.MaxHeight + 0.5);
        }
    }

    // ===================== Вспомогательные методы =====================

    private static IEnumerable<UtilitiesMenuNodeItem> FlattenUtilities(
        IEnumerable<UtilitiesMenuNodeItem> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenUtilities(node.Children))
                yield return child;
        }
    }

    private static IEnumerable<ContextMenuNodeItem> FlattenContext(
        IEnumerable<ContextMenuNodeItem> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenContext(node.Children))
                yield return child;
        }
    }

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
