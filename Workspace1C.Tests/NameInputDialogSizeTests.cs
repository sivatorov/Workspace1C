using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Диалог ввода имени (<c>NameInputWindow</c>, используется и для «Дублировать базу»
/// файловой ИБ) не должен растягиваться по ширине: раньше ширина подстраивалась под
/// содержимое (в т.ч. под длинную подпись), и окно занимало почти весь экран.
/// Теперь ширина фиксирована (440 DIP), подпись переносится по словам, а размеры
/// клампятся по рабочей области монитора (<see cref="Workspace1C.Services.DialogWorkAreaPolicy"/>).
/// </summary>
public sealed class NameInputDialogSizeTests
{
    [Fact]
    public void WpfWindow_HasFixedCompactWidthAndWrappingPrompt()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "NameInputWindow.xaml"));

        Assert.Contains("Width=\"440\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PromptLabel\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_ClampsSizeToWorkArea()
    {
        var cs = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "NameInputWindow.xaml.cs"));

        Assert.Contains("ApplyWorkAreaLimits", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.MaxHeight", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampMinHeight", cs, StringComparison.Ordinal);
        Assert.Contains("Width = Math.Min(Width,", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasFixedCompactWidthAndWrappingPrompt()
    {
        var axaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "NameInputWindow.axaml"));

        Assert.Contains("Width=\"440\"", axaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_ClampsSizeToWorkArea()
    {
        var cs = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "NameInputWindow.Avalonia.cs"));

        Assert.Contains("ApplyWorkAreaLimits", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.MaxHeight", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampMinHeight", cs, StringComparison.Ordinal);
        Assert.Contains("Screens.ScreenFromWindow", cs, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Не найден корень репозитория (Workspace1C.slnx).");
    }
}
