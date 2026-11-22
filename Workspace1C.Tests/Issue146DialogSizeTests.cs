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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
