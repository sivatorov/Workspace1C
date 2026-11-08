using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты строки состояния Avalonia (issue #114 «Линукс - статус панель»). История:
/// v1.0.1.43 — tooltip показывался «не там» и усекался, меню — «совсем не там»;
/// v1.0.1.44 — собственный ToolTip с переносом и якорное размещение (Top): «по
/// вертикали нормально»; уточнение 7OH (2026-11-08): «по оси Х оба пункта там же,
/// где мышка… всегда по середине окна» — v1.0.1.45 ставит центр popup по X под
/// курсор. Стражи-сканы исходников: код <c>MainViewModel.Avalonia.Display.cs</c> и
/// <c>MainWindow.Avalonia.cs</c> собирается только в Linux-конфигурации (#if LINUX),
/// поэтому юнит-тесты — source-scan по образцу <see cref="Issue79ScheduledJobsResilienceTests"/>.
/// <list type="bullet">
/// <item><c>StatusBarInfoTip</c> — отдельный текст подсказки с переносом строки перед
/// «ID»; в самой панели строка остаётся однострочной;</item>
/// <item>tooltip — собственный ToolTip с TextWrapping=Wrap, Placement=Custom и
/// CustomPopupPlacementCallback (attached на элементе-владельце);</item>
/// <item>контекстное меню — Placement=Custom + тот же callback;</item>
/// <item>координата курсора по X запоминается в PointerEntered/PointerMoved/
/// PointerPressed на <c>_statusInfo</c>;</item>
/// <item>X-центрирование под курсором с зажимом в границы окна — чистая функция
/// <see cref="PopupBoundsMath.ClampCenteredX"/>.</item>
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
    public void MainWindow_TooltipWrappedAndCenteredAtCursor()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        // Tooltip привязан к StatusBarInfoTip (перенос перед ИД), длинный текст переносится
        // и не усекается экраном.
        Assert.Contains("new Binding(\"StatusBarInfoTip\")", code, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", code, StringComparison.Ordinal);
        // X-центрирование под курсором: Placement=Custom + CustomPopupPlacementCallback
        // (attached-свойство ToolTip на элементе-владельце, Avalonia 11.3).
        Assert.Contains("ToolTip.SetTip(_statusInfo, statusTip)", code, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetPlacement(_statusInfo, PlacementMode.Custom)", code, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetCustomPopupPlacementCallback(_statusInfo, StatusPopupPlacementCallback)", code, StringComparison.Ordinal);
        // Страж против регрессии v1.0.1.44 «всегда по середине окна по X»: Top-центрирование
        // по цели не должно вернуться.
        Assert.DoesNotContain("ToolTip.SetPlacement(_statusInfo, PlacementMode.Top)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_StatusContextMenu_CustomPlacementAtCursor()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        // Страж против регрессии «меню совсем не там» / «меню по середине окна по X»:
        // меню привязано к элементу строки состояния и открывается через тот же
        // Custom-callback, что и tooltip.
        Assert.Contains("statusMenu.PlacementTarget = _statusInfo", code, StringComparison.Ordinal);
        Assert.Contains("statusMenu.Placement = PlacementMode.Custom", code, StringComparison.Ordinal);
        Assert.Contains("statusMenu.CustomPopupPlacementCallback = StatusPopupPlacementCallback", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_StatusPointerX_TrackedOnPointerEvents()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        // Поле координаты курсора и его обновление на всех трёх pointer-событиях:
        // tooltip открывается по наведению (PointerEntered/Moved), меню — по правой
        // кнопке (PointerPressed до ContextRequested).
        Assert.Contains("private double _statusPointerX;", code, StringComparison.Ordinal);
        Assert.Contains("_statusInfo.PointerEntered += (_, e) => _statusPointerX = e.GetPosition(this).X", code, StringComparison.Ordinal);
        Assert.Contains("_statusInfo.PointerMoved += (_, e) => _statusPointerX = e.GetPosition(this).X", code, StringComparison.Ordinal);
        Assert.Contains("_statusInfo.PointerPressed += (_, e) => _statusPointerX = e.GetPosition(this).X", code, StringComparison.Ordinal);
        // Callback использует запомненную координату курсора и зажим в границы окна.
        Assert.Contains("_statusPointerX,", code, StringComparison.Ordinal);
        Assert.Contains("PopupBoundsMath.PlaceAboveCenteredAtCursorClamped(", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(600, 280, 1200, 460)]   // середина окна: центр панели = курсор.
    [InlineData(8, 280, 1200, 8)]       // левый край: left <= margin → прижата к 8.
    [InlineData(1192, 280, 1200, 912)]  // правый край: зажим к 1200-8-280=912.
    public void PopupBoundsMath_ClampCenteredX_EdgesAndMiddle(
        double cursorX, double popupWidth, double windowWidth, double expectedLeft)
    {
        Assert.Equal(expectedLeft, PopupBoundsMath.ClampCenteredX(cursorX, popupWidth, windowWidth), 6);
    }

    [Fact]
    public void PopupBoundsMath_ClampCenteredX_PopupWiderThanWindow_SticksToMargin()
    {
        Assert.Equal(8, PopupBoundsMath.ClampCenteredX(600, 1300, 1200));
    }

    [Fact]
    public void PopupBoundsMath_ClampCenteredX_InvalidBounds_NoClamp()
    {
        // Границы не заданы (≤ 0) — левый край панели просто центрируется под курсором.
        Assert.Equal(600 - 140, PopupBoundsMath.ClampCenteredX(600, 280, 0), 6);
    }

    [Fact]
    public void PopupBoundsMath_PlaceAboveCenteredAtCursorClamped_TopAboveTarget()
    {
        // Вертикаль — как у PlaceAboveClamped: над целью с зазором 4; горизонталь —
        // центр панели под курсором.
        var (left, top) = PopupBoundsMath.PlaceAboveCenteredAtCursorClamped(
            targetLeft: 0, targetTop: 730, cursorX: 600,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760);
        Assert.Equal(600 - 140, left, 6);  // 530
        Assert.Equal(730 - 4 - 120, top, 6); // 606
    }

    [Fact]
    public void PopupBoundsMath_PlaceAboveCenteredAtCursorClamped_NearTop_ClampsToMargin()
    {
        var (_, top) = PopupBoundsMath.PlaceAboveCenteredAtCursorClamped(
            targetLeft: 0, targetTop: 70, cursorX: 600,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760);
        Assert.Equal(8, top, 6);
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
