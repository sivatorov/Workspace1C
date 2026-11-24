using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #146 (1.0.1.116): окна правки меню открываются с <c>Height = MinHeight</c>
/// («Утилиты» — 825, «Мои программы» — 760, «Меню трея» — 855, «Контекстное меню» —
/// 685 DIP), поэтому поясняющий текст виден без прокрутки на обычном экране.
/// Прокрутка остаётся только аварийным вариантом после клампа по маленькой рабочей
/// области.
/// <para>
/// Расчётная логика покрыта через
/// <see cref="DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.DialogProfile, double, double)"/>,
/// структура окон — source-guard проверками XAML/кода (WPF и Avalonia).
/// </para>
/// </summary>
public sealed class Issue146WindowHeightTests
{
    [Fact]
    public void Policy_Profiles_MatchIssue146Values()
    {
        // План 1.0.1.116, раздел 3.5: Height = MinHeight = contentHeight для всех окон.
        AssertProfile(DialogWorkAreaPolicy.UtilitiesEditor, 825);
        AssertProfile(DialogWorkAreaPolicy.TrayEditor, 855);
        AssertProfile(DialogWorkAreaPolicy.ContextMenuEditor, 685);
        AssertProfile(DialogWorkAreaPolicy.CustomPrograms, 760);
    }

    [Theory]
    [InlineData(900.0)]
    [InlineData(1000.0)]
    [InlineData(1080.0)]
    public void Policy_Recommend_Utilities_FullHeightWithoutScrolling(double workArea)
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.UtilitiesEditor, workArea);

        Assert.Equal(825, size.Height, 3);
        Assert.Equal(825, size.MinHeight, 3);
        Assert.False(size.RequiresScrolling,
            $"При рабочей области {workArea} DIP «Утилиты» помещаются без прокрутки.");
        Assert.True(size.Height <= workArea);
    }

    [Theory]
    [InlineData(1000.0)]
    [InlineData(1080.0)]
    public void Policy_Recommend_Tray_FullHeightWithoutScrolling(double workArea)
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.TrayEditor, workArea);

        Assert.Equal(855, size.Height, 3);
        Assert.Equal(855, size.MinHeight, 3);
        Assert.False(size.RequiresScrolling,
            $"При рабочей области {workArea} DIP «Меню трея» помещается без прокрутки.");
        Assert.True(size.Height <= workArea);
    }

    [Theory]
    [InlineData(800.0)]
    [InlineData(900.0)]
    public void Policy_Recommend_ContextMenu_FullHeightWithoutScrolling(double workArea)
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.ContextMenuEditor, workArea);

        Assert.Equal(685, size.Height, 3);
        Assert.Equal(685, size.MinHeight, 3);
        Assert.False(size.RequiresScrolling,
            $"При рабочей области {workArea} DIP «Контекстное меню» помещается без прокрутки.");
        Assert.True(size.Height <= workArea);
    }

    [Theory]
    [InlineData(900.0)]
    [InlineData(1000.0)]
    public void Policy_Recommend_CustomPrograms_FullHeightWithoutScrolling(double workArea)
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.CustomPrograms, workArea);

        Assert.Equal(760, size.Height, 3);
        Assert.Equal(760, size.MinHeight, 3);
        Assert.False(size.RequiresScrolling,
            $"При рабочей области {workArea} DIP «Мои программы» помещаются без прокрутки.");
        Assert.True(size.Height <= workArea);
    }

    [Fact]
    public void Policy_Recommend_SmallWorkArea_ClampsAndRequiresScrolling()
    {
        // На экране 600 DIP верхняя граница (600 − 48 = 552) побеждает высоту профиля:
        // окно урезается, и только тогда включается прокрутка (аварийный режим).
        var profiles = new (string Name, DialogWorkAreaPolicy.DialogProfile Profile)[]
        {
            ("Утилиты", DialogWorkAreaPolicy.UtilitiesEditor),
            ("Меню трея", DialogWorkAreaPolicy.TrayEditor),
            ("Контекстное меню", DialogWorkAreaPolicy.ContextMenuEditor),
            ("Мои программы", DialogWorkAreaPolicy.CustomPrograms),
        };

        foreach (var (name, profile) in profiles)
        {
            var size = DialogWorkAreaPolicy.Recommend(profile, 600);

            Assert.Equal(600 - DialogWorkAreaPolicy.DefaultMargin, size.Height, 3);
            Assert.Equal(size.Height, size.MinHeight, 3);
            Assert.True(size.MinHeight <= 600, $"{name}: MinHeight не должен выходить за экран.");
            Assert.True(size.RequiresScrolling, $"{name}: после клампа нужна прокрутка.");
        }
    }

    [Fact]
    public void Policy_Recommend_UnknownWorkArea_KeepsProfileHeight()
    {
        var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.UtilitiesEditor, 0);

        Assert.Equal(825, size.Height, 3);
        Assert.False(size.RequiresScrolling);
        Assert.True(double.IsPositiveInfinity(size.MaxHeight));
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml", "825")]
    [InlineData("TrayMenuEditorWindow.xaml", "855")]
    [InlineData("ContextMenuEditorWindow.xaml", "685")]
    [InlineData("CustomProgramsWindow.xaml", "760")]
    public void WpfWindows_XamlHeightEqualsMinHeight(string fileName, string value)
    {
        // Issue #146: требование владельца «сделать Height = MinHeight» зафиксировано
        // в разметке, чтобы окно не открывалось с завышенным запасом.
        var xaml = ReadView(fileName);

        Assert.Contains($"Height=\"{value}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinHeight=\"{value}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.Avalonia.cs", "UtilitiesEditor")]
    [InlineData("TrayMenuEditorWindow.Avalonia.cs", "TrayEditor")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs", "ContextMenuEditor")]
    [InlineData("CustomProgramsWindow.Avalonia.cs", "CustomPrograms")]
    public void AvaloniaWindows_UseProfiles(string fileName, string profile)
    {
        var source = ReadView(fileName);

        Assert.Contains($"DialogWorkAreaPolicy.{profile}", source, StringComparison.Ordinal);
        Assert.Contains($"Height = DialogWorkAreaPolicy.{profile}.DefaultHeight;", source, StringComparison.Ordinal);
        Assert.Contains($"MinHeight = DialogWorkAreaPolicy.{profile}.MinHeight;", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend(", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ContextMenuEditorWindow.xaml.cs")]
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    public void ContextMenuEditor_AppliesWorkAreaPolicy(string fileName)
    {
        // Issue #146 (1.0.1.116): «Контекстное меню» раньше не использовало политику
        // вовсе — теперь кламп по рабочей области общий с остальными окнами.
        var source = ReadView(fileName);

        Assert.Contains("ApplyWorkAreaLimits();", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.Recommend", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ContextMenuEditor", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = size.MaxHeight;", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = size.MinHeight;", source, StringComparison.Ordinal);
        Assert.Contains("Height = size.Height;", source, StringComparison.Ordinal);
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

    [Fact]
    public void ContextMenuEditor_Wpf_HintOutsideScrollViewer()
    {
        // Issue #146 (1.0.1.116): пояснение «Контекстного меню» вынесено из прокручиваемой
        // панели в DockPanel.Dock="Bottom" — как в «Утилитах» и «Меню трея» (1.0.1.115).
        var xaml = ReadView("ContextMenuEditorWindow.xaml");

        var dock = xaml.IndexOf("<DockPanel Grid.Column=\"2\">", StringComparison.Ordinal);
        Assert.True(dock >= 0, "Правая колонка «Контекстного меню» должна быть DockPanel.");

        var bottom = xaml.IndexOf("DockPanel.Dock=\"Bottom\"", dock, StringComparison.Ordinal);
        var hint = xaml.IndexOf("ContextMenu.Editor.Hint", dock, StringComparison.Ordinal);
        var scroll = xaml.IndexOf("<ScrollViewer", dock, StringComparison.Ordinal);

        Assert.True(bottom >= 0 && bottom < hint,
            "Пояснение должно быть закреплено внизу правой колонки (DockPanel.Dock=\"Bottom\").");
        Assert.True(hint >= 0 && hint < scroll,
            "Пояснение должно находиться вне прокручиваемой панели (до ScrollViewer).");
    }

    [Fact]
    public void ContextMenuEditor_Avalonia_HintOutsideScrollViewer()
    {
        var source = ReadView("ContextMenuEditorWindow.Avalonia.cs");

        Assert.Contains("DockPanel.SetDock(hint, Dock.Bottom)", source, StringComparison.Ordinal);
        Assert.Contains("BuildHint(\"ContextMenu.Editor.Hint\")", source, StringComparison.Ordinal);

        // BuildOperationsPanel больше не добавляет пояснение — оно вне ScrollViewer.
        var panelStart = source.IndexOf("private Control BuildOperationsPanel()", StringComparison.Ordinal);
        var hintMethod = source.IndexOf("private Border BuildHint(", StringComparison.Ordinal);
        Assert.True(panelStart >= 0 && hintMethod > panelStart,
            "BuildOperationsPanel и BuildHint должны быть объявлены.");
        var panel = source.Substring(panelStart, hintMethod - panelStart);
        Assert.DoesNotContain("ContextMenu.Editor.Hint", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenuEditor_Wpf_OperationsPanelScrolls()
    {
        var xaml = ReadView("ContextMenuEditorWindow.xaml");

        // Прокручивается только панель операций (аварийный режим), пояснение — вне её.
        Assert.Contains("<DockPanel Grid.Column=\"2\">", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenuEditor_RemainsResizable()
    {
        var xaml = ReadView("ContextMenuEditorWindow.xaml");
        var avalonia = ReadView("ContextMenuEditorWindow.Avalonia.cs");

        Assert.DoesNotContain("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CanResize = false", avalonia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UtilitiesMenuEditorWindow.xaml")]
    [InlineData("TrayMenuEditorWindow.xaml")]
    [InlineData("ContextMenuEditorWindow.xaml")]
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
    [InlineData("ContextMenuEditorWindow.Avalonia.cs")]
    [InlineData("CustomProgramsWindow.Avalonia.cs")]
    public void AvaloniaWindows_RemainResizable(string fileName)
    {
        var source = ReadView(fileName);

        Assert.DoesNotContain("CanResize = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent = SizeToContent.WidthAndHeight", source, StringComparison.Ordinal);
    }

    private static void AssertProfile(DialogWorkAreaPolicy.DialogProfile profile, double expected)
    {
        Assert.Equal(expected, profile.DefaultHeight, 3);
        Assert.Equal(expected, profile.MinHeight, 3);
        Assert.Equal(expected, profile.ContentHeight, 3);
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
