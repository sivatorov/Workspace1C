using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты строки состояния Avalonia (issue #114 «Линукс - статус панель»: tooltip
/// показывается не там и усечён — нужен перенос перед ИД; контекстное меню показывается
/// не там). Стражи-сканы исходников: код <c>MainViewModel.Avalonia.Display.cs</c> и
/// <c>MainWindow.Avalonia.cs</c> собирается только в Linux-конфигурации (#if LINUX),
/// поэтому юнит-тесты — source-scan по образцу <see cref="Issue79ScheduledJobsResilienceTests"/>.
/// <list type="bullet">
/// <item><c>StatusBarInfoTip</c> — отдельный текст подсказки с переносом строки перед
/// «ID»; в самой панели строка остаётся однострочной;</item>
/// <item>tooltip — собственный ToolTip с TextWrapping=Wrap и Placement=Top
/// (открывается над элементом панели, а не «где-то» у курсора);</item>
/// <item>контекстное меню — якорное размещение (PlacementTarget + Placement=Top).</item>
/// </list>
/// </summary>
public sealed class Issue114StatusBarTooltipTests
{
    [Fact]
    public void ViewModel_StatusBarInfoTip_InsertsLineBreakBeforeId()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Display.cs"));
        // Отдельное свойство подсказки; строка панели не меняется.
        Assert.Contains("public string StatusBarInfoTip", code, StringComparison.Ordinal);
        // Единый источник строки (ComposeStatusInfo): пост-обработка StatusBarInfo
        // с переносом перед частью ИД.
        Assert.Contains("Replace(\"  ·  ID \", \"\\nID \")", code, StringComparison.Ordinal);
        // Setter StatusBarInfo уведомляет и о подсказке — иначе tooltip не обновится.
        Assert.Contains("OnPropertyChanged(nameof(StatusBarInfoTip))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_TooltipWrappedAndAnchoredAboveStatusPanel()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        // Tooltip привязан к StatusBarInfoTip (перенос перед ИД), длинный текст переносится
        // и не усекается экраном.
        Assert.Contains("new Binding(\"StatusBarInfoTip\")", code, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", code, StringComparison.Ordinal);
        // Якорное размещение подсказки: над элементом панели, а не у курсора
        // (pointer-based placement на X11 позиционируется неровно).
        Assert.Contains("ToolTip.SetTip(_statusInfo, statusTip)", code, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetPlacement(_statusInfo, PlacementMode.Top)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_StatusContextMenu_PlacementTargetSet()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        // Страж против регрессии «меню совсем не там»: меню привязано к элементу
        // строки состояния и открывается над ним.
        Assert.Contains("statusMenu.PlacementTarget = _statusInfo", code, StringComparison.Ordinal);
        Assert.Contains("statusMenu.Placement = PlacementMode.Top", code, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
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
