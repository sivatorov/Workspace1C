using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #107 «Неисправленные огрехи в тёмной теме»: стражи по исходникам.
/// (a) «Выбор родительской группы»: кнопки футера не перекрашивают текст/иконку
/// в <c>ButtonTextBrush</c> (чёрный в обеих темах — нечитаемо в тёмной);
/// (b) hover вторичных кнопок — только рамка: триггер IsMouseOver стиля
/// SecondaryButton (WPF, обе темы) и селектор :pointerover SecondaryButton
/// (Avalonia) не задают Background; «Назначить теги» — «Отмена» на
/// SecondaryButton, а не акцентная ModernButton с подменой Background.
/// </summary>
public sealed class Issue107DarkButtonsTests
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

    [Fact]
    public void GroupPickerWindow_FooterButtons_DontOverrideWithButtonTextBrush()
    {
        // (a) Кнопки «Выбрать»/«Отмена» наследуют Foreground стилей PrimaryButton/
        // SecondaryButton (TextPrimaryBrush — светлый в тёмной теме); жёсткий
        // ButtonTextBrush в окне запрещён.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "GroupPickerWindow.xaml"));
        Assert.DoesNotContain("ButtonTextBrush", xaml);
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void SecondaryButton_HoverTrigger_DoesNotSetBackground(string themeFile)
    {
        // (b) WPF: hover вторичной кнопки — только акцентная рамка, без заливки.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        var start = xaml.IndexOf("x:Key=\"SecondaryButton\"", StringComparison.Ordinal);
        Assert.True(start > 0, $"Стиль SecondaryButton не найден в {themeFile}");
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден конец стиля SecondaryButton в {themeFile}");
        var style = xaml[start..end];

        var hover = Regex.Match(
            style, "<Trigger Property=\"IsMouseOver\" Value=\"True\">.*?</Trigger>",
            RegexOptions.Singleline);
        Assert.True(hover.Success, $"Триггер IsMouseOver не найден в SecondaryButton ({themeFile})");
        Assert.DoesNotContain("Background", hover.Value);
        Assert.Contains("BorderBrush", hover.Value);

        // Нажатие сохраняет тактильный отклик (заливка при IsPressed остаётся).
        var pressed = Regex.Match(
            style, "<Trigger Property=\"IsPressed\" Value=\"True\">.*?</Trigger>",
            RegexOptions.Singleline);
        Assert.True(pressed.Success, $"Триггер IsPressed не найден в SecondaryButton ({themeFile})");
        Assert.Contains("Background", pressed.Value);
    }

    [Fact]
    public void Avalonia_SecondaryButton_PointerOver_DoesNotSetBackground()
    {
        // (b) Avalonia: hover вторичной кнопки — только рамка, без заливки.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var start = axaml.IndexOf("x:Key=\"SecondaryButton\"", StringComparison.Ordinal);
        Assert.True(start > 0, "ControlTheme SecondaryButton не найден в Controls.axaml");
        var end = axaml.IndexOf("</ControlTheme>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Не найден конец ControlTheme SecondaryButton");
        var theme = axaml[start..end];

        var pointerOver = Regex.Match(
            theme, "<Style Selector=\"\\^:pointerover /template/ Border#PART_Surface\">.*?</Style>",
            RegexOptions.Singleline);
        Assert.True(pointerOver.Success, "Селектор :pointerover SecondaryButton не найден");
        Assert.DoesNotContain("Background", pointerOver.Value);
        Assert.Contains("BorderBrush", pointerOver.Value);

        // Нажатие сохраняет отклик (заливку) — паритет с WPF IsPressed.
        var pressed = Regex.Match(
            theme, "<Style Selector=\"\\^:pressed /template/ Border#PART_Surface\">.*?</Style>",
            RegexOptions.Singleline);
        Assert.True(pressed.Success, "Селектор :pressed SecondaryButton не найден");
        Assert.Contains("Background", pressed.Value);
    }

    [Fact]
    public void TagPickWindow_CancelButton_UsesSecondaryButton()
    {
        // (b) «Назначить теги»: «Отмена» — вторичная SecondaryButton, а не акцентная
        // ModernButton с подменой Background=ItemHoverBrush (анти-паттерн заливки
        // при наведении из скриншота issue).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TagPickWindow.xaml"));
        var index = xaml.IndexOf("IsCancel=\"True\"", StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка отмены не найдена в TagPickWindow.xaml");
        var start = xaml.LastIndexOf("<Button", index, StringComparison.Ordinal);
        Assert.True(start >= 0, "Тег кнопки отмены не найден");
        var tag = xaml[start..(index + 200)];

        Assert.Contains("SecondaryButton", tag);
        Assert.DoesNotContain("ModernButton", tag);
        Assert.DoesNotContain("ItemHoverBrush", tag);
    }

    [Fact]
    public void AboutLink_PointsToNewInfostartArticle()
    {
        // Issue #115: «О программе» ссылается на новую статью Infostart
        // (WPF — Text и Tag в SettingsWindow.xaml, Avalonia — LinkBlock).
        const string newUrl = "https://infostart.ru/1c/articles/2825056/";

        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains(newUrl, xaml);
        Assert.DoesNotContain("2764888", xaml);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains(newUrl, avalonia);
        Assert.DoesNotContain("2764888", avalonia);
    }
}
