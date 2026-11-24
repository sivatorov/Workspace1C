using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (1.0.1.115): поясняющий текст в окнах «Настройка меню трея»,
/// «Мои программы» и «Настройка меню утилиты» должен быть виден без прокрутки
/// при рабочей области от 720 DIP (масштаб Windows 100–150 %). Прокрутка остаётся
/// только аварийным вариантом для действительно маленьких экранов.
/// <para>
/// Расчётная логика покрыта через <see cref="DialogWorkAreaPolicy.Recommend"/>,
/// структура окон — source-guard проверками XAML/кода (WPF и Avalonia).
/// </para>
/// </summary>
public sealed class Issue146WindowHeightTests
{
    [Fact]
    public void Policy_Constants_MatchPlan()
    {
        // План 1.0.1.115, раздел 3.3: высота 760 (редакторы) / 780 («Мои программы»),
        // минимальная — 600.
        Assert.Equal(760, DialogWorkAreaPolicy.EditorDefaultHeight);
        Assert.Equal(600, DialogWorkAreaPolicy.EditorMinHeight);
        Assert.Equal(780, DialogWorkAreaPolicy.CustomProgramsDefaultHeight);
        Assert.Equal(600, DialogWorkAreaPolicy.CustomProgramsMinHeight);
        // Высота содержимого должна помещаться в рабочую область 720 DIP минус отступ.
        Assert.True(DialogWorkAreaPolicy.EditorContentHeight <= 720 - DialogWorkAreaPolicy.DefaultMargin);
        Assert.True(DialogWorkAreaPolicy.CustomProgramsContentHeight <= 720 - DialogWorkAreaPolicy.DefaultMargin);
    }

    [Theory]
    [InlineData(720.0, 672.0)]
    [InlineData(768.0, 720.0)]
    [InlineData(900.0, 760.0)]
    [InlineData(1080.0, 760.0)]
    public void Policy_Recommend_Editor_FitsHintWithoutScrolling(double workArea, double expectedHeight)
    {
        var size = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.EditorDefaultHeight,
            DialogWorkAreaPolicy.EditorMinHeight,
            DialogWorkAreaPolicy.EditorContentHeight,
            workArea);

        Assert.Equal(expectedHeight, size.Height, 3);
        Assert.False(size.RequiresScrolling, $"При рабочей области {workArea} DIP прокрутка не нужна.");
        Assert.True(size.Height >= DialogWorkAreaPolicy.EditorContentHeight,
            "Высота окна должна вмещать пояснение без прокрутки.");
        Assert.True(size.MinHeight <= workArea);
        Assert.True(size.Height <= workArea);
        Assert.True(size.Height >= size.MinHeight);
    }

    [Theory]
    [InlineData(720.0, 672.0)]
    [InlineData(768.0, 720.0)]
    [InlineData(900.0, 780.0)]
    [InlineData(1080.0, 780.0)]
    public void Policy_Recommend_CustomPrograms_FitsHintWithoutScrolling(double workArea, double expectedHeight)
    {
        var size = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.CustomProgramsDefaultHeight,
            DialogWorkAreaPolicy.CustomProgramsMinHeight,
            DialogWorkAreaPolicy.CustomProgramsContentHeight,
            workArea);

        Assert.Equal(expectedHeight, size.Height, 3);
        Assert.False(size.RequiresScrolling, $"При рабочей области {workArea} DIP прокрутка не нужна.");
        Assert.True(size.Height >= DialogWorkAreaPolicy.CustomProgramsContentHeight);
        Assert.True(size.MinHeight <= workArea);
    }

    [Theory]
    [InlineData(600.0)]
    [InlineData(500.0)]
    public void Policy_Recommend_TinyWorkArea_RequiresScrolling(double workArea)
    {
        var editor = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.EditorDefaultHeight,
            DialogWorkAreaPolicy.EditorMinHeight,
            DialogWorkAreaPolicy.EditorContentHeight,
            workArea);
        var programs = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.CustomProgramsDefaultHeight,
            DialogWorkAreaPolicy.CustomProgramsMinHeight,
            DialogWorkAreaPolicy.CustomProgramsContentHeight,
            workArea);

        // На маленьком экране окно урезается по рабочей области — прокрутка включается.
        Assert.True(editor.RequiresScrolling);
        Assert.True(programs.RequiresScrolling);
        // MinHeight не выходит за рабочую область (нижняя граница не побеждает верхнюю).
        Assert.True(editor.MinHeight <= workArea);
        Assert.True(programs.MinHeight <= workArea);
        Assert.Equal(workArea - DialogWorkAreaPolicy.DefaultMargin, editor.Height, 3);
    }

    [Fact]
    public void Policy_Recommend_UnknownWorkArea_KeepsRequestedHeight()
    {
        var size = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.EditorDefaultHeight,
            DialogWorkAreaPolicy.EditorMinHeight,
            DialogWorkAreaPolicy.EditorContentHeight,
            0);

        Assert.Equal(DialogWorkAreaPolicy.EditorDefaultHeight, size.Height, 3);
        Assert.False(size.RequiresScrolling);
        Assert.True(double.IsPositiveInfinity(size.MaxHeight));
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml", "760", "600")]
    [InlineData("TrayMenuEditorWindow.xaml", "760", "600")]
    public void WpfEditors_DefaultHeightAtLeast720(string fileName, string height, string minHeight)
    {
        var xaml = ReadView(fileName);

        Assert.Contains($"Height=\"{height}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinHeight=\"{minHeight}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    public void AvaloniaEditors_HeightComesFromPolicy(string fileName)
    {
        var source = ReadView(fileName);

        Assert.Contains("Height = DialogWorkAreaPolicy.EditorDefaultHeight;", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.EditorMinHeight;", source, StringComparison.Ordinal);
        // Зашитых 560/480 больше нет — высота считается политикой.
        Assert.DoesNotContain("Height = 560;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = 480;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomPrograms_DefaultHeightAtLeast760()
    {
        var wpf = ReadView("CustomProgramsWindow.xaml");
        Assert.Contains("Height=\"780\"", wpf, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"600\"", wpf, StringComparison.Ordinal);

        var avalonia = ReadView("CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("Height = DialogWorkAreaPolicy.CustomProgramsDefaultHeight;", avalonia, StringComparison.Ordinal);
        Assert.Contains("MinHeight = DialogWorkAreaPolicy.CustomProgramsMinHeight;", avalonia, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 580;", avalonia, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = 500;", avalonia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml", "Utilities.Editor.Hint")]
    [InlineData("TrayMenuEditorWindow.xaml", "TrayMenu.Hint")]
    public void WpfEditors_HintOutsideScrollViewer(string fileName, string hintKey)
    {
        var xaml = ReadView(fileName);

        var dock = xaml.IndexOf("<DockPanel Grid.Column=\"2\">", StringComparison.Ordinal);
        Assert.True(dock >= 0, "Правая колонка должна быть DockPanel.");

        var bottom = xaml.IndexOf("DockPanel.Dock=\"Bottom\"", dock, StringComparison.Ordinal);
        var hint = xaml.IndexOf(hintKey, dock, StringComparison.Ordinal);
        var scroll = xaml.IndexOf("<ScrollViewer", dock, StringComparison.Ordinal);

        Assert.True(bottom >= 0 && bottom < hint,
            "Пояснение должно быть закреплено внизу правой колонки (DockPanel.Dock=\"Bottom\").");
        Assert.True(hint >= 0 && hint < scroll,
            "Пояснение должно находиться вне прокручиваемой панели (до ScrollViewer).");
    }

    [Fact]
    public void CustomPrograms_HintOutsideScrollViewer()
    {
        var xaml = ReadView("CustomProgramsWindow.xaml");

        var dock = xaml.IndexOf("<DockPanel Grid.Column=\"2\">", StringComparison.Ordinal);
        Assert.True(dock >= 0, "Правая колонка должна быть DockPanel.");

        var bottom = xaml.IndexOf("DockPanel.Dock=\"Bottom\"", dock, StringComparison.Ordinal);
        var hint = xaml.IndexOf("Launch.CustomPrograms.Hint", dock, StringComparison.Ordinal);
        var scroll = xaml.IndexOf("<ScrollViewer", dock, StringComparison.Ordinal);

        Assert.True(bottom >= 0 && bottom < hint,
            "Пояснение «Моих программ» должно быть закреплено внизу правой колонки.");
        Assert.True(hint >= 0 && hint < scroll,
            "Пояснение «Моих программ» должно быть вне прокручиваемой формы.");
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs", "Utilities.Editor.Hint")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs", "TrayMenu.Hint")]
    public void AvaloniaEditors_HintOutsideScrollViewer(string fileName, string hintKey)
    {
        var source = ReadView(fileName);

        Assert.Contains("DockPanel.SetDock(hint, Dock.Bottom)", source, StringComparison.Ordinal);
        Assert.Contains($"BuildHint(\"{hintKey}\")", source, StringComparison.Ordinal);

        // BuildOperationsPanel больше не добавляет пояснение — оно вне ScrollViewer.
        var panelStart = source.IndexOf("private Control BuildOperationsPanel()", StringComparison.Ordinal);
        var hintMethod = source.IndexOf("private Border BuildHint(", StringComparison.Ordinal);
        Assert.True(panelStart >= 0 && hintMethod > panelStart,
            "BuildOperationsPanel и BuildHint должны быть объявлены.");
        var panel = source.Substring(panelStart, hintMethod - panelStart);
        Assert.DoesNotContain(hintKey, panel, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomProgramsAvalonia_HintOutsideScrollViewer()
    {
        var source = ReadView("CustomProgramsWindow.Avalonia.cs");

        var hint = source.IndexOf("Launch.CustomPrograms.Hint", StringComparison.Ordinal);
        var scroll = source.IndexOf("right.Children.Add(new ScrollViewer", StringComparison.Ordinal);

        Assert.True(hint >= 0, "Пояснение «Моих программ» не найдено в Avalonia-версии.");
        Assert.True(scroll > hint, "Пояснение должно добавляться в DockPanel до ScrollViewer.");
        Assert.Contains("DockPanel.SetDock(hint, Dock.Bottom)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml")]
    [InlineData("TrayMenuEditorWindow.xaml")]
    [InlineData("CustomProgramsWindow.xaml")]
    public void WpfWindows_RemainResizable(string fileName)
    {
        var xaml = ReadView(fileName);

        // Окна с деревом/списком не должны терять возможность растягиваться:
        // SizeToContent ломает прокрутку и кламп по рабочей области.
        Assert.DoesNotContain("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs")]
    [InlineData("CustomProgramsWindow.Avalonia.cs")]
    public void AvaloniaWindows_RemainResizable(string fileName)
    {
        var source = ReadView(fileName);

        Assert.DoesNotContain("CanResize = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent = SizeToContent.WidthAndHeight", source, StringComparison.Ordinal);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", fileName));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
