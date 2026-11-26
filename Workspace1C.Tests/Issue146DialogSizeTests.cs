using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (1.0.1.106/1.0.1.115): окна правки меню («Утилиты», «Меню трея»,
/// «Мои программы») не помещались на экранах 768 px с масштабированием — поясняющий
/// текст уходил за нижнюю границу. Политика <see cref="DialogWorkAreaPolicy"/> клампит
/// высоту окна по рабочей области монитора; обе оболочки применяют её одинаково.
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
    [InlineData("ContextMenuEditorWindow.xaml.cs")]
    [InlineData("CustomProgramsWindow.xaml.cs")]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    [InlineData("CustomProgramsWindow.Avalonia.cs")]
    public void Editors_ApplyWorkAreaPolicy(string fileName)
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

        // Issue #146 (1.0.1.115): размеры окна считает общая политика; в коде окна
        // не должно быть «своих» клампов и зашитых высот.
        Assert.Contains("DialogWorkAreaPolicy.Recommend", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = size.MinHeight;", source, StringComparison.Ordinal);

        if (fileName.EndsWith(".Avalonia.cs", StringComparison.Ordinal))
        {
            // Issue #159 (1.0.1.123): Avalonia-окна открываются по содержимому —
            // высота из политики не присваивается, только границы.
            Assert.Contains("SizeToContent = Avalonia.Controls.SizeToContent.Height",
                source, StringComparison.Ordinal);
            Assert.DoesNotContain("Height = size.Height;", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Height = size.Height;", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml")]
    [InlineData("TrayMenuEditorWindow.xaml")]
    public void WpfEditors_OperationsPanelScrolls(string fileName)
    {
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

        // Issue #146 (1.0.1.118): правая колонка — ScrollViewer (DockPanel убран),
        // прокручивается панель операций вместе с пояснением (аварийный режим).
        Assert.Contains("<ScrollViewer Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomPrograms_NoNestedScrollViewer()
    {
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "CustomProgramsWindow.xaml"));

        // Issue #146 (1.0.1.115): двойная вложенность (окно + внутренний ScrollViewer)
        // убрана — прокручивается только правая форма, список прокручивает ListBox.
        Assert.DoesNotContain("CentralScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomProgramsAvalonia_NoNestedScrollViewer()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs"));

        // Issue #146: в Avalonia тоже нет внешнего центрального ScrollViewer —
        // прокручивается только правая форма (вместе с пояснением в её содержимом).
        Assert.DoesNotContain("centralScroll", source, StringComparison.Ordinal);
        Assert.Contains("dock.Children.Add(BuildEditorGrid());", source, StringComparison.Ordinal);
        Assert.Contains("var right = new ScrollViewer", source, StringComparison.Ordinal);
        Assert.Contains("Content = scrollContent", source, StringComparison.Ordinal);
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
