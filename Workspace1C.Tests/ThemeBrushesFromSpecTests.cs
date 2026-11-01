using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #89 (критический крах старта на Linux): спецификации вида «@WarningBrush»
/// («@InfoBrush», «@PurpleAccentBrush») — это ключи кистей темы, и они не должны
/// уходить в <c>Color.Parse</c>: прямая передача ключа кидала
/// <c>FormatException: Invalid color string: '@WarningBrush'</c> в конструкторе
/// MainWindow, и приложение на Linux вообще не запускалось.
/// Чистые вызовы <see cref="Workspace1C.Themes.ThemeBrushes.FromSpec"/> — только
/// в Linux-сборке (#if LINUX, класс Avalonia; сборка тестов с -p:BuildLinux=true).
/// На Windows проверяются WPF-двойник WpfBrushes.FromSpec и файловые проверки
/// исходников (по образцу ButtonThemeBindingTests).
/// </summary>
public sealed class ThemeBrushesFromSpecTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

#if LINUX
    [Theory]
    [InlineData("@WarningBrush")]
    [InlineData("@InfoBrush")]
    [InlineData("@PurpleAccentBrush")]
    [InlineData("@NoSuchBrushResource")]
    public void FromSpec_AtBrushSpecs_DoesNotThrowAndReturnsBrush(string spec)
    {
        // В тестовом процессе Avalonia Application не создан: ресурс недоступен,
        // и FromSpec обязан вернуть нейтральную кисть, а не кидать FormatException.
        var brush = Workspace1C.Themes.ThemeBrushes.FromSpec(spec);
        Assert.NotNull(brush);
    }

    [Fact]
    public void FromSpec_HexSpec_ReturnsSolidColorBrushWithColor()
    {
        var brush = Assert.IsType<Avalonia.Media.SolidColorBrush>(
            Workspace1C.Themes.ThemeBrushes.FromSpec("#DC2626"));
        Assert.Equal(0xDC, brush.Color.R);
        Assert.Equal(0x26, brush.Color.G);
        Assert.Equal(0x26, brush.Color.B);
        Assert.Equal(0xFF, brush.Color.A);
    }
#else
    [Theory]
    [InlineData("@WarningBrush")]
    [InlineData("@InfoBrush")]
    [InlineData("@PurpleAccentBrush")]
    [InlineData("@NoSuchBrushResource")]
    public void WpfFromSpec_AtBrushSpecs_DoesNotThrowAndReturnsBrush(string spec)
    {
        // Симметричная защита WPF-двойника (Application.Current в тестах нет).
        var brush = Workspace1C.Themes.WpfBrushes.FromSpec(spec);
        Assert.NotNull(brush);
    }

    [Fact]
    public void WpfFromSpec_HexSpec_ReturnsSolidColorBrushWithColor()
    {
        var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            Workspace1C.Themes.WpfBrushes.FromSpec("#DC2626"));
        Assert.Equal(0xDC, brush.Color.R);
        Assert.Equal(0x26, brush.Color.G);
        Assert.Equal(0x26, brush.Color.B);
        Assert.Equal((byte)0xFF, brush.Color.A);
    }
#endif

    [Fact]
    public void MainWindowTree_ColorSpecsGoThroughSpecBrush()
    {
        // Файловая проверка: в MainWindow.Avalonia.Tree.cs явные цвета значков и
        // подписей идут через SpecBrush (ThemeBrushes.FromSpec), а не через прямой
        // Color.Parse — туда приходят и ключи кистей «@...» из вызовов правой панели.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("private static IBrush SpecBrush(string spec)", source, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.FromSpec(spec)", source, StringComparison.Ordinal);
        // Ни один обработчик не должен строить кисть из colorHex напрямую.
        Assert.DoesNotContain("Color.Parse(colorHex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(iconHex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(textHex)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_BothLanguagesHaveClusterContentsKey()
    {
        // issue #92: новый ключ диагностики содержимого кластера присутствует
        // в обеих локализациях.
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains("BatchOps.DbmsResolveClusterContentsFormat", ru, StringComparison.Ordinal);
        Assert.Contains("BatchOps.DbmsResolveClusterContentsFormat", en, StringComparison.Ordinal);
    }
}
