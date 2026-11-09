using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #107 «Неисправленные огрехи в тёмной теме», окно добавления базы
/// (AddEditWindow): у выбранной карточки-радиокнопки не должно быть заливки
/// <c>AccentHoverBrush</c> — яркий акцент тёмной темы делал светлый текст
/// нечитаемым. Выбранный вариант обозначается только акцентной рамкой 2px.
/// Стражи по исходникам (source-scan) по образцу Issue107DarkButtonsTests.
/// </summary>
public sealed class Issue107AddWindowTests
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
    public void AddEditWindow_CheckedOption_DoesNotFillWithAccentHoverBrush()
    {
        // WPF: триггер IsChecked стиля OptionRadioButton — только акцентная рамка,
        // без заливки AccentHoverBrush.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "AddEditWindow.xaml"));

        var styleStart = xaml.IndexOf("x:Key=\"OptionRadioButton\"", StringComparison.Ordinal);
        Assert.True(styleStart > 0, "Стиль OptionRadioButton не найден в AddEditWindow.xaml");
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleEnd > styleStart, "Не найден конец стиля OptionRadioButton");
        var style = xaml[styleStart..styleEnd];

        var checkedTrigger = style.IndexOf("Property=\"IsChecked\"", StringComparison.Ordinal);
        Assert.True(checkedTrigger > 0, "Триггер IsChecked не найден в OptionRadioButton");
        var triggerBlock = style[checkedTrigger..];

        Assert.DoesNotContain("AccentHoverBrush", triggerBlock);
        // Рамка выбранной карточки сохранена (акцент, 2px).
        Assert.Contains("AccentBrush", triggerBlock);
        Assert.Contains("BorderThickness", triggerBlock);
    }

    [Fact]
    public void Avalonia_AddOptionCard_Checked_DoesNotFillWithAccentHoverBrush()
    {
        // Avalonia: селектор ^:checked темы AddOptionCard — только рамка, без заливки.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));

        var themeStart = axaml.IndexOf("x:Key=\"AddOptionCard\"", StringComparison.Ordinal);
        Assert.True(themeStart > 0, "ControlTheme AddOptionCard не найден в Controls.axaml");
        var themeEnd = axaml.IndexOf("</ControlTheme>", themeStart, StringComparison.Ordinal);
        Assert.True(themeEnd > themeStart, "Не найден конец ControlTheme AddOptionCard");
        var theme = axaml[themeStart..themeEnd];

        var checkedSelector = theme.IndexOf("^:checked", StringComparison.Ordinal);
        Assert.True(checkedSelector > 0, "Селектор ^:checked не найден в AddOptionCard");
        var selectorBlock = theme[checkedSelector..];

        Assert.DoesNotContain("AccentHoverBrush", selectorBlock);
        // Рамка выбранной карточки сохранена.
        Assert.Contains("AccentBrush", selectorBlock);
        Assert.Contains("BorderThickness", selectorBlock);
    }
}
