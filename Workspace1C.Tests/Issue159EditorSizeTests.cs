using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #159 (1.0.1.128), четвёртая итерация: после 1.0.1.127 «Утилиты» в Windows
/// получили прокрутку, «Контекстное меню» стало слишком большим, а на Linux при
/// раскрытии папки по-прежнему «прыгала» ширина списка. Профили
/// <see cref="DialogWorkAreaPolicy"/> выровнены по реальному содержимому (без
/// прокрутки, но с заметно меньшим <c>MinHeight</c>), а в Avalonia-редакторах
/// дерево/список растягиваются по родительской колонке и не имеют горизонтальной
/// прокрутки — измеренная ширина больше не зависит от раскрытых узлов.
/// </summary>
public sealed class Issue159EditorSizeTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void EditorHeights_AreBalanced()
    {
        // 1.0.1.128: «Утилиты» 640 → 660 (640 давало прокрутку), «Контекстное меню»
        // 600 → 570 (600 — лишний запас). MinHeight снижены 440/400 → 340/320.
        Assert.Equal(660, DialogWorkAreaPolicy.UtilitiesEditor.DefaultHeight);
        Assert.Equal(340, DialogWorkAreaPolicy.UtilitiesEditor.MinHeight);
        Assert.Equal(570, DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight);
        Assert.Equal(320, DialogWorkAreaPolicy.ContextMenuEditor.MinHeight);

        // Контекстное меню по содержимому ниже «Утилит» (в нём нет блока иконок).
        Assert.True(DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight
                    < DialogWorkAreaPolicy.UtilitiesEditor.DefaultHeight);
    }

    [Fact]
    public void EditorProfiles_MinHeightIsWellBelowContentHeight()
    {
        // Инвариант профиля: MinHeight < DefaultHeight = ContentHeight.
        foreach (var (name, profile) in new (string Name, DialogWorkAreaPolicy.DialogProfile Profile)[]
                 {
                     ("Утилиты", DialogWorkAreaPolicy.UtilitiesEditor),
                     ("Контекстное меню", DialogWorkAreaPolicy.ContextMenuEditor),
                     ("Меню трея", DialogWorkAreaPolicy.TrayEditor),
                     ("Мои программы", DialogWorkAreaPolicy.CustomPrograms),
                 })
        {
            Assert.True(profile.MinHeight < profile.DefaultHeight,
                $"{name}: MinHeight ({profile.MinHeight}) должен быть ниже DefaultHeight ({profile.DefaultHeight}).");
            Assert.Equal(profile.DefaultHeight, profile.ContentHeight, 3);
        }

        // Issue #159 (1.0.1.128): у «Утилит» и «Контекстного меню» запас сжатия по
        // высоте должен быть заметным (окно свободно уменьшается), а MinHeight —
        // в ориентире плана 320–360 DIP.
        Assert.True(DialogWorkAreaPolicy.UtilitiesEditor.DefaultHeight
                    - DialogWorkAreaPolicy.UtilitiesEditor.MinHeight >= 240);
        Assert.True(DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight
                    - DialogWorkAreaPolicy.ContextMenuEditor.MinHeight >= 240);
        Assert.InRange(DialogWorkAreaPolicy.UtilitiesEditor.MinHeight, 320, 360);
        Assert.InRange(DialogWorkAreaPolicy.ContextMenuEditor.MinHeight, 320, 360);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    public void LinuxEditors_StableTreeWidth(string fileName)
    {
        var code = ReadProjectFile("Workspace1C", "Views", fileName);

        // Issue #159 (1.0.1.128): у дерева нет горизонтальной прокрутки...
        Assert.Contains(
            "ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled)",
            code);
        // ...дерево растягивается по родительской колонке...
        Assert.Contains("_tree.HorizontalAlignment = HorizontalAlignment.Stretch", code);
        // ...и находится в звёздной колонке Grid, а не в Dock.Left (иначе ширина
        // определялась бы содержимым и «прыгала» при раскрытии групп).
        Assert.Contains("Grid.SetColumn(treeBorder, 0)", code);
        Assert.Contains("ColumnDefinitions(\"*,12,260\")", code);
        Assert.DoesNotContain("dock.Children.Add(treeBorder)", code);
        // Длинные подписи обрезаются, а не увеличивают измеренную ширину строки.
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", code);
    }

    [Fact]
    public void LinuxCustomPrograms_StableListWidth()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");

        // Список программ растягивается по фиксированной колонке, без горизонтальной
        // прокрутки — ширина не зависит от длины названий программ.
        Assert.Contains("_list.HorizontalAlignment = HorizontalAlignment.Stretch", code);
        Assert.Contains(
            "ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled)",
            code);
        Assert.Contains("Grid.SetColumn(left, 0)", code);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml", 660, 340)]
    [InlineData("ContextMenuEditorWindow.xaml", 570, 320)]
    public void WpfEditors_XamlMatchesProfiles(string fileName, double height, double minHeight)
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", fileName);

        // WPF-части не должны задавать высоту, конфликтующую с профилем политики.
        Assert.Contains($"Height=\"{height}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinHeight=\"{minHeight}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent", xaml, StringComparison.Ordinal);

        var profile = fileName.StartsWith("Utilities", StringComparison.Ordinal)
            ? DialogWorkAreaPolicy.UtilitiesEditor
            : DialogWorkAreaPolicy.ContextMenuEditor;
        Assert.Equal(profile.DefaultHeight, height, 3);
        Assert.Equal(profile.MinHeight, minHeight, 3);
    }
}
