using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// issue #145 «Что случилось с цветом папок»: в 1.0.1.98 фон строки-папки был
/// слишком бледным (14 % цвета группы), а вертикальная полоса выходила за
/// скруглённые углы рамки. Проверяются: повышенный вес пастели, читаемость и
/// вертикальный отступ полосы в WPF-конвертере и Avalonia-разметке.
/// </summary>
public sealed class Issue145FolderColorTests
{
    private const string GroupColor = "#2D6CDF";
    private const string LightSurface = "#F1F5F9";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Themes")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }

    private static string ReadProjectFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static (int R, int G, int B) Rgb(string hex)
    {
        var raw = hex.TrimStart('#');
        return (Convert.ToInt32(raw.Substring(0, 2), 16),
                Convert.ToInt32(raw.Substring(2, 2), 16),
                Convert.ToInt32(raw.Substring(4, 2), 16));
    }

    private static double Distance(string a, string b)
    {
        var (ar, ag, ab) = Rgb(a);
        var (br, bg, bb) = Rgb(b);
        return Math.Sqrt(Math.Pow(ar - br, 2) + Math.Pow(ag - bg, 2) + Math.Pow(ab - bb, 2));
    }

    [Fact]
    public void TintWeight_IsNoticeablyStrongerThanAuditBaseline()
    {
        // 14 % из аудита P1-2 выглядели «оооочень бледно» (issue #145).
        Assert.True(Workspace1C.Models.ColorScheme.GroupRowTintWeight >= 0.28,
            "Фон строки группы должен быть заметнее прежних 14 %.");
        Assert.True(Workspace1C.Models.ColorScheme.GroupRowHoverTintWeight
                    > Workspace1C.Models.ColorScheme.GroupRowTintWeight,
            "Hover обязан быть насыщеннее покоя.");
        Assert.True(Workspace1C.Models.ColorScheme.GroupRowHoverTintWeight < 0.5,
            "Hover не должен превращаться в сплошную заливку.");
    }

    [Fact]
    public void Background_AtNewWeight_IsFurtherFromSurfaceThanBefore()
    {
        var oldBackground = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(
            GroupColor, LightSurface, 0.14);
        var newBackground = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(
            GroupColor, LightSurface);

        Assert.True(Distance(newBackground, LightSurface) > Distance(oldBackground, LightSurface),
            "Новый фон должен быть заметнее (дальше от фона темы), чем прежние 14 %.");
    }

    [Fact]
    public void Wpf_GroupStripeConverter_UsesSharedSymmetricLayout()
    {
        var converter = ReadProjectFile("Workspace1C", "Converters", "LevelToThicknessConverter.cs");
        // Issue #145: полоса и содержимое считаются от общего helper, поэтому
        // зазоры слева (от кнопки свёртки) и справа (до иконки папки) одинаковы.
        Assert.Contains("GroupRowStripeLayout.Gap", converter, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.ContentOffset", converter, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.VerticalInset", converter, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_GroupStripe_UsesSharedSymmetricLayout()
    {
        var tree = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.Contains("GroupRowStripeLayout.Gap", tree, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.ContentOffset", tree, StringComparison.Ordinal);
        Assert.Contains("GroupRowStripeLayout.VerticalInset", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupRowText_RemainsReadableAtNewWeight()
    {
        foreach (var surface in new[] { LightSurface, "#0F172A" })
        {
            var text = Workspace1C.Models.ColorScheme.GroupRowTextHex(GroupColor, surface, "#000000");
            var background = Workspace1C.Models.ColorScheme.GroupRowBackgroundHex(GroupColor, surface);
            Assert.True(Workspace1C.Models.ColorScheme.ContrastRatio(text, background) >= 4.5,
                $"Текст {text} на фоне {background} должен иметь контраст ≥ 4.5:1.");
        }
    }
}
