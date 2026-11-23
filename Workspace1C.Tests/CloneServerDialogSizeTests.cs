using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Окно «Дублировать серверную базу» (<c>CloneServerInfobaseWindow</c>) содержит
/// 14 полей и раньше имело фиксированную высоту 640 DIP (WPF) и
/// <c>SizeToContent.Height</c> (Avalonia) — на экранах 768 px с масштабированием
/// оно почти не помещалось. Теперь высота клампится по рабочей области
/// (<see cref="Workspace1C.Services.DialogWorkAreaPolicy"/>), а поля прокручиваются;
/// кнопки остаются видимыми.
/// </summary>
public sealed class CloneServerDialogSizeTests
{
    [Fact]
    public void WpfWindow_UsesWorkAreaPolicy()
    {
        var cs = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "CloneServerInfobaseWindow.xaml.cs"));

        Assert.Contains("ApplyWorkAreaLimits", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.MaxHeight", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampMinHeight", cs, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampHeight", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_DefaultHeightIsCompactAndContentScrolls()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "CloneServerInfobaseWindow.xaml"));

        // Высота уменьшена с 640, жёсткий MaxHeight из разметки убран
        // (его задаёт политика по фактической рабочей области).
        Assert.Contains("Height=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"360\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"640\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"800\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_UsesWorkAreaPolicyAndScrolls()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Workspace1C", "Views", "CloneServerInfobaseWindow.Avalonia.cs"));

        Assert.Contains("ApplyWorkAreaLimits", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.MaxHeight", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampMinHeight", source, StringComparison.Ordinal);
        Assert.Contains("DialogWorkAreaPolicy.ClampHeight", source, StringComparison.Ordinal);
        // Высота больше не подстраивается под всё содержимое.
        Assert.DoesNotContain("SizeToContent.Height", source, StringComparison.Ordinal);
        Assert.Contains("Height = 560;", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 360;", source, StringComparison.Ordinal);
        Assert.Contains("new ScrollViewer", source, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
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
