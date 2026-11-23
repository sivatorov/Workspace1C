using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (1.0.1.106): окна правки меню («Утилиты», «Меню трея», «Мои программы»)
/// не помещались на экранах 768 px с масштабированием — поясняющий текст уходил за
/// нижнюю границу. Политика <see cref="DialogWorkAreaPolicy"/> клампит высоту окна
/// по рабочей области монитора; обе оболочки применяют её одинаково.
/// </summary>
public sealed class Issue146DialogSizeTests
{
    [Fact]
    public void ClampHeight_FitsRequestedHeightIntoWorkArea()
    {
        // 800 DIP не помещается в 768 DIP с отступом 48 → 720.
        Assert.Equal(720, DialogWorkAreaPolicy.ClampHeight(800, 480, 768));
    }

    [Fact]
    public void ClampHeight_KeepsMinimumWhenThereIsRoom()
    {
        // Запрошено меньше минимума, но места достаточно — поднимаем до минимума.
        Assert.Equal(480, DialogWorkAreaPolicy.ClampHeight(300, 480, 1080));
    }

    [Fact]
    public void ClampHeight_TinyScreen_PrefersUpperBound()
    {
        // Экран 500 DIP: верхняя граница 452 меньше MinHeight 480 — окно должно
        // поместиться целиком, поэтому побеждает верхняя граница.
        Assert.Equal(452, DialogWorkAreaPolicy.ClampHeight(560, 480, 500));
    }

    [Fact]
    public void ClampHeight_UnknownWorkArea_KeepsRequested()
    {
        Assert.Equal(560, DialogWorkAreaPolicy.ClampHeight(560, 480, 0));
        Assert.Equal(560, DialogWorkAreaPolicy.ClampHeight(560, 480, double.NaN));
    }

    [Fact]
    public void ClampHeight_NaNRequested_FallsBackToMinimum()
    {
        Assert.Equal(480, DialogWorkAreaPolicy.ClampHeight(double.NaN, 480, 1080));
    }

    [Fact]
    public void ClampHeight_NegativeMargin_TreatedAsZero()
    {
        Assert.Equal(560, DialogWorkAreaPolicy.ClampHeight(560, 480, 560, margin: -100));
    }

    [Fact]
    public void ClampMinHeight_RegularScreen_KeepsMinimum()
    {
        // На обычном экране минимальная высота не меняется.
        Assert.Equal(500, DialogWorkAreaPolicy.ClampMinHeight(500, 1080));
    }

    [Fact]
    public void ClampMinHeight_SmallScreen_LowersToMaxHeight()
    {
        // 768 DIP минус отступ 48 = 720: MinHeight 800 понижается до границы экрана.
        Assert.Equal(720, DialogWorkAreaPolicy.ClampMinHeight(800, 768));
        // MinHeight меньше границы не меняется: окно и так помещается.
        Assert.Equal(500, DialogWorkAreaPolicy.ClampMinHeight(500, 768));
    }

    [Fact]
    public void ClampMinHeight_TinyScreen_MatchesMaxHeight()
    {
        // Экран 500 DIP: верхняя граница 452 меньше MinHeight 480.
        Assert.Equal(452, DialogWorkAreaPolicy.ClampMinHeight(480, 500));
    }

    [Fact]
    public void ClampMinHeight_UnknownWorkArea_KeepsMinimum()
    {
        Assert.Equal(480, DialogWorkAreaPolicy.ClampMinHeight(480, 0));
        Assert.Equal(480, DialogWorkAreaPolicy.ClampMinHeight(480, double.NaN));
    }

    [Fact]
    public void ClampMinHeight_NaNOrNegative_TreatedAsZero()
    {
        Assert.Equal(0, DialogWorkAreaPolicy.ClampMinHeight(double.NaN, 1080));
        Assert.Equal(0, DialogWorkAreaPolicy.ClampMinHeight(-100, 1080));
    }

    [Fact]
    public void ClampMinHeight_ThenClampHeight_FitsTinyScreen()
    {
        // Порядок из редакторов: сначала MinHeight, затем Height — окно помещается.
        var minHeight = DialogWorkAreaPolicy.ClampMinHeight(480, 500);
        Assert.Equal(452, minHeight);
        Assert.Equal(452, DialogWorkAreaPolicy.ClampHeight(560, minHeight, 500));
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml.cs")]
    [InlineData("TrayMenuEditorWindow.xaml.cs")]
    [InlineData("CustomProgramsWindow.xaml.cs")]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    [InlineData("CustomProgramsWindow.Avalonia.cs")]
    public void Editors_ApplyWorkAreaPolicy(string fileName)
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

        Assert.Contains("DialogWorkAreaPolicy.ClampHeight", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.MaxHeight", source, StringComparison.Ordinal);
        // Issue #146: MinHeight тоже клампится, иначе нижняя граница побеждает
        // верхнюю и окно снова выходит за рабочую область.
        Assert.Contains("DialogWorkAreaPolicy.ClampMinHeight", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.ClampMinHeight(MinHeight,", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml")]
    [InlineData("TrayMenuEditorWindow.xaml")]
    public void WpfEditors_OperationsPanelScrolls(string fileName)
    {
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

        // Панель операций в правой колонке обёрнута в ScrollViewer: длинное
        // пояснение не обрезается, а прокручивается.
        Assert.Contains("ScrollViewer Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomPrograms_CentralAreaScrolls()
    {
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "CustomProgramsWindow.xaml"));

        // Issue #146: центральная область (список программ и редактор) обёрнута
        // в ScrollViewer, поэтому при малой высоте окна содержимое доступно,
        // а нижние кнопки и переключатель режима остаются видимыми.
        var start = xaml.IndexOf("x:Name=\"CentralScrollViewer\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "В CustomProgramsWindow.xaml нет ScrollViewer центральной области.");
        var block = xaml.Substring(start, Math.Min(400, xaml.Length - start));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", block, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomProgramsAvalonia_CentralAreaScrolls()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs"));

        // Issue #146: в Avalonia центральная область тоже в ScrollViewer.
        Assert.Contains("var centralScroll = new ScrollViewer", source, StringComparison.Ordinal);
        Assert.Contains("Content = BuildEditorGrid()", source, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Disabled", source, StringComparison.Ordinal);
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
