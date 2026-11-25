using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #160 «Linux — лишний ESC» (v1.0.1.120): один Esc должен закрывать только
/// самый верхний (активный) диалог.
/// <list type="bullet">
/// <item>причина каскада: на Linux/X11 фокус после показа вложенного окна иногда
/// остаётся в родителе, Esc приходит в родительский <c>OnKeyDown</c>, родитель
/// закрывался безусловно — а вместе с владельцем закрывались и дочерние окна
/// (настройки → структура → меню);</item>
/// <item>исправление: <c>ModalWindowBase.CloseOnEscape</c> перед закрытием ищет
/// верхний вложенный диалог по цепочке <c>Window.Owner</c> и закрывает только его;</item>
/// <item>главное окно по Esc закрывает ровно один верхний диалог
/// (<c>TopmostModalDialog</c> + <c>CloseOnEscape</c>);</item>
/// <item>WPF-паритет: там Esc обрабатывает сам модальный диалог
/// (<c>EscClosableWindow</c>/<c>IsCancel</c>), владелец на время показа отключён,
/// поведение не менялось.</item>
/// </list>
/// </summary>
public sealed class Issue160EscapeRoutingTests
{
    private const string BaseWindowPath = "ModalWindowBase.cs";
    private const string HotkeysPath = "MainWindow.Avalonia.Hotkeys.cs";

    [Fact]
    public void Escape_WithChildDialog_DoesNotCloseParent()
    {
        var baseWindow = ReadView(BaseWindowPath);

        // Инвариант живёт в базе модальных окон: единая точка для всех диалогов.
        Assert.Contains("internal void CloseOnEscape()", baseWindow, StringComparison.Ordinal);
        Assert.Contains("FindTopmostOwnedDialog(this)", baseWindow, StringComparison.Ordinal);
        Assert.Contains("IsOwnedBy(", baseWindow, StringComparison.Ordinal);
        Assert.Contains("window.Owner", baseWindow, StringComparison.Ordinal);

        // В ветке Esc обработчик вызывает CloseOnEscape, а не закрывает окно напрямую.
        var onKeyDown = Slice(
            baseWindow,
            "protected override void OnKeyDown(KeyEventArgs e)",
            "internal void CloseOnEscape()");
        Assert.Contains("CloseOnEscape();", onKeyDown, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAsCancel();", onKeyDown, StringComparison.Ordinal);

        // Родитель остаётся открытым: закрывается только найденный дочерний диалог.
        var closeOnEscape = Slice(
            baseWindow,
            "internal void CloseOnEscape()",
            "internal static Window? FindTopmostOwnedDialog");
        Assert.Contains("modalChild.CloseOnEscape();", closeOnEscape, StringComparison.Ordinal);
        Assert.Contains("return;", closeOnEscape, StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_ClosesOnlyTopmost()
    {
        var baseWindow = ReadView(BaseWindowPath);

        var findTopmost = Slice(
            baseWindow,
            "internal static Window? FindTopmostOwnedDialog",
            "private static bool IsOwnedBy");
        // Порядок — по Z-порядку платформы, а не по порядку открытия окон.
        Assert.Contains("Window.SortWindowsByZOrder", findTopmost, StringComparison.Ordinal);
        Assert.Contains("desktop.Windows", findTopmost, StringComparison.Ordinal);
        Assert.Contains("!window.IsVisible", findTopmost, StringComparison.Ordinal);
        Assert.Contains("ordered[ordered.Length - 1]", findTopmost, StringComparison.Ordinal);
    }

    [Fact]
    public void ModalWindowBase_ActivatesChildOnOpen()
    {
        var baseWindow = ReadView(BaseWindowPath);

        // При каждом открытии окно запрашивает активацию (Linux/X11 не всегда
        // передаёт фокус сразу), поэтому Esc с первого нажатия обрабатывает диалог.
        Assert.Contains(
            "Dispatcher.UIThread.Post(Activate, DispatcherPriority.Input);",
            baseWindow,
            StringComparison.Ordinal);

        var onOpened = Slice(
            baseWindow,
            "protected override void OnOpened(EventArgs e)",
            "protected override void OnKeyDown(KeyEventArgs e)");
        Assert.Contains("Post(Activate", onOpened, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Escape_DoesNotCloseAllDialogs()
    {
        var hotkeys = ReadView(HotkeysPath);

        var escapeBranch = Slice(
            hotkeys,
            "&& TopmostModalDialog() is { } dialog)",
            "// Сначала закрываем открытую подсказку");
        Assert.Contains("dialog.CloseOnEscape();", escapeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAsCancel();", escapeBranch, StringComparison.Ordinal);
        // Никакого обхода и закрытия всех окон: ровно один верхний диалог.
        Assert.DoesNotContain("foreach", escapeBranch, StringComparison.Ordinal);

        // Верхнее окно определяется Z-порядком платформы.
        Assert.Contains("Window.SortWindowsByZOrder(ordered);", hotkeys, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaEditors_UseModalWindowBase()
    {
        // Все редакторы меню — наследники ModalWindowBase, поэтому Esc у них идёт
        // через общий CloseOnEscape (настройки → редактор не закрывает родителя).
        Assert.Contains(
            "class UtilitiesMenuEditorWindow : ModalWindowBase",
            ReadView("UtilitiesMenuEditorWindow.Avalonia.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "class ContextMenuEditorWindow : ModalWindowBase",
            ReadView("ContextMenuEditorWindow.Avalonia.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "class TrayMenuEditorWindow : ModalWindowBase",
            ReadView("TrayMenuEditorWindow.Avalonia.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Parity_EscapeClosesOnlyOwnDialog()
    {
        // WPF: Esc обрабатывает сам диалог, владелец на время показа отключён —
        // событие не доходит до родительского окна, каскада нет.
        var escClosable = ReadView("EscClosableWindow.cs");
        Assert.Contains("PreviewKeyDown +=", escClosable, StringComparison.Ordinal);
        Assert.Contains("DialogResult = false;", escClosable, StringComparison.Ordinal);
        Assert.Contains("Close();", escClosable, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", escClosable, StringComparison.Ordinal);

        // Редакторы открываются модально с владельцем «настройки»: Esc закрывает
        // только сам редактор (кнопка «Отмена» с IsCancel).
        var settings = ReadView("SettingsWindow.xaml.cs");
        foreach (var editor in new[]
                 {
                     "UtilitiesMenuEditorWindow", "ContextMenuEditorWindow", "TrayMenuEditorWindow"
                 })
        {
            Assert.Contains($"new {editor}(", settings, StringComparison.Ordinal);
        }
        Assert.Contains("Owner = this,", settings, StringComparison.Ordinal);
        Assert.Contains("win.ShowDialog();", settings, StringComparison.Ordinal);

        var utilitiesXaml = ReadView("UtilitiesMenuEditorWindow.xaml");
        Assert.Contains("IsCancel=\"True\"", utilitiesXaml, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static string ReadView(string fileName)
        => ReadProjectFile("Workspace1C", "Views", fileName);

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
