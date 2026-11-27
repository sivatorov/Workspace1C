using System;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #159 (1.0.1.127), третья итерация: высоты окон правки «Утилит» и
/// контекстного меню «поплыли» — «Утилиты» стали чуть выше нужного, контекстное
/// меню чуть ниже и появился скролл. Высоты выровнены профилями
/// <see cref="DialogWorkAreaPolicy"/>; на Linux ширина списка больше не скачет
/// при раскрытии групп (горизонтальная прокрутка дерева отключена).
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
        // «Утилиты» — 640 (было 680), контекстное меню — 600 (было 540).
        Assert.Equal(640, DialogWorkAreaPolicy.UtilitiesEditor.DefaultHeight);
        Assert.Equal(600, DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight);
        // Контекстное меню не ниже минимальной высоты и не выше «Утилит».
        Assert.True(DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight
                    >= DialogWorkAreaPolicy.ContextMenuEditor.MinHeight);
        Assert.True(DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight
                    < DialogWorkAreaPolicy.UtilitiesEditor.DefaultHeight);
        // По содержимому — без прокрутки: ContentHeight совпадает с DefaultHeight.
        Assert.Equal(DialogWorkAreaPolicy.ContextMenuEditor.DefaultHeight,
            DialogWorkAreaPolicy.ContextMenuEditor.ContentHeight);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    public void LinuxEditors_DisableHorizontalScrollOfTree(string fileName)
    {
        var code = ReadProjectFile("Workspace1C", "Views", fileName);

        // Issue #159: ширина списка не должна скакать при раскрытии групп.
        Assert.Contains("ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled)", code);
    }
}
