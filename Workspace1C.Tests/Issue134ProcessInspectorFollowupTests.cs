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

    /// <summary>
    /// issue #134 (1.0.1.102, замечание 7OH): фокус ставится отложенно — после
    /// загрузки данных и первого прохода разметки, с повтором при неактивированном
    /// окне. Синхронного Focus() в Loaded больше нет: он не передавал фокус таблице.
    /// </summary>
    [Fact]
    public void Wpf_Inspector_FocusesGridOnOpen()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");
        Assert.Contains("ContentRendered += (_, _) => QueueGridFocus();", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(new Action(FocusProcessesGrid), DispatcherPriority.Input)", code, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Focus(ProcessesGrid)", code, StringComparison.Ordinal);
        Assert.Contains("NotifyCollectionChangedAction.Add", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded += (_, _) => ProcessesGrid.Focus();", code, StringComparison.Ordinal);
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

    /// <summary>
    /// issue #134 (1.0.1.102, замечание 7OH): в Avalonia фокус ставится через
    /// <c>Dispatcher.UIThread.Post</c> после загрузки данных и разметки, с повтором.
    /// </summary>
    [Fact]
    public void Avalonia_Inspector_FocusesGridOnOpen()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");
        Assert.Contains("FocusGridAttempt(0)", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(", code, StringComparison.Ordinal);
        Assert.Contains("_grid.Focus(NavigationMethod.Directional)", code, StringComparison.Ordinal);
        Assert.Contains("NotifyCollectionChangedAction.Add", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Opened += (_, _) => _grid.Focus();", code, StringComparison.Ordinal);
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

    /// <summary>
    /// issue #134 (1.0.1.104, замечание 7OH): если после открытия окна или завершения
    /// процесса фокус оказался на кнопке, «вниз»/«вверх» управляют таблицей, а не
    /// переводят фокус на «Колонки». Признак установленного фокуса не фиксируется
    /// на пустом списке — после загрузки данных фокус ставится повторно.
    /// </summary>
    [Fact]
    public void Wpf_Inspector_ArrowsWorkWhenFocusIsOnButton()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");

        Assert.Contains("PreviewKeyDown += OnWindow_PreviewKeyDown;", code, StringComparison.Ordinal);
        Assert.Contains("OnWindow_PreviewKeyDown", code, StringComparison.Ordinal);
        Assert.Contains("NavigateGridFromButton(navigation)", code, StringComparison.Ordinal);
        Assert.Contains("Key.Down", code, StringComparison.Ordinal);
        Assert.Contains("ProcessesGrid.IsKeyboardFocusWithin", code, StringComparison.Ordinal);
        Assert.Contains("if (ProcessesGrid.Items.Count > 0)", code, StringComparison.Ordinal);
        // После завершения процесса при остатке строк ставится текущая строка.
        Assert.Contains("ProcessesGrid.SelectedIndex < 0", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.104): паритет Avalonia — то же перенаправление стрелок
    /// в таблицу и повторная установка фокуса после заполнения списка.
    /// </summary>
    [Fact]
    public void Avalonia_Inspector_ArrowsWorkWhenFocusIsOnButton()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("AddHandler(KeyDownEvent, OnWindow_KeyDown, RoutingStrategies.Tunnel)", code, StringComparison.Ordinal);
        Assert.Contains("OnWindow_KeyDown", code, StringComparison.Ordinal);
        Assert.Contains("NavigateGridFromButton(navigation)", code, StringComparison.Ordinal);
        Assert.Contains("Key.Down", code, StringComparison.Ordinal);
        Assert.Contains("_grid.IsKeyboardFocusWithin", code, StringComparison.Ordinal);
        Assert.Contains("if (_grid.ItemCount > 0)", code, StringComparison.Ordinal);
        Assert.Contains("_grid.SelectedIndex < 0", code, StringComparison.Ordinal);
    }
}
