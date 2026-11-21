using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// issue #134, последний комментарий 7OH (21.11.2026): при открытии инспектора
/// процессов список должен получать фокус (Ctrl+A без мыши), а Enter и двойной
/// клик — завершать выбранные процессы, а не активировать базу в главном списке,
/// недоступном из модального окна.
/// </summary>
public sealed class Issue134ProcessInspectorFollowupTests
{
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

    private static string ReadProjectFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void Wpf_Inspector_FocusesGridOnOpen()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");
        Assert.Contains("Loaded += (_, _) => ProcessesGrid.Focus();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Inspector_EnterAndDoubleClickKillSelected()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml");
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");

        Assert.Contains("PreviewKeyDown=\"OnGrid_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnGrid_PreviewKeyDown", code, StringComparison.Ordinal);
        Assert.Contains("OnGrid_MouseDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("_vm.KillSelectedMany();", code, StringComparison.Ordinal);
        // Двойной клик больше не активирует базу в главном окне.
        Assert.DoesNotContain("_vm.OpenBase(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Inspector_FocusesGridOnOpen()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");
        Assert.Contains("Opened += (_, _) => _grid.Focus();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Inspector_EnterAndDoubleTapKillSelected()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("_grid.KeyDown += OnGrid_KeyDown;", code, StringComparison.Ordinal);
        Assert.Contains("OnGrid_KeyDown", code, StringComparison.Ordinal);
        Assert.Contains("OnGrid_DoubleTapped", code, StringComparison.Ordinal);
        Assert.Contains("_vm.KillSelectedMany();", code, StringComparison.Ordinal);
        // Двойной клик больше не активирует базу в главном окне.
        Assert.DoesNotContain("_vm.OpenBase(", code, StringComparison.Ordinal);
    }
}
