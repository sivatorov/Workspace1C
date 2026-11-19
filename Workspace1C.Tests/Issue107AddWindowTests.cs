using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #107 «Неисправленные огрехи в тёмной теме», окно добавления базы
/// (AddEditWindow). С v1.0.1.49 (п.1 #107) контракт выбранной карточки-радиокнопки
/// вернулся к акцентной заливке: Background = AccentBrush, Foreground =
/// TextOnAccentBrush, рамка акцентом 2px сохранена. Наличие текста/значка на
/// акценте делает читаемым ThemeManager: пользовательский «Текст на акцентном
/// фоне» при контрасте к акценту ≥ 3:1, иначе WCAG-фолбэк (issue #26 + п.2 #107).
/// Заливка AccentHoverBrush по-прежнему запрещена — она не читаема на любом
/// акценте. Стражи по исходникам (source-scan) по образцу Issue107DarkButtonsTests.
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
    public void AddEditWindow_CheckedOption_AccentFillWithTextOnAccentBrush()
    {
        // WPF: триггер IsChecked стиля OptionRadioButton — акцентная заливка +
        // рамка 2px + TextOnAccentBrush, без заливки AccentHoverBrush.
        // P2-2 (1.0.1.92): стиль перенесён из AddEditWindow в словарь темы.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "LightTheme.xaml"));

        var styleStart = xaml.IndexOf("x:Key=\"OptionRadioButton\"", StringComparison.Ordinal);
        Assert.True(styleStart > 0, "Стиль OptionRadioButton не найден в LightTheme.xaml");
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleEnd > styleStart, "Не найден конец стиля OptionRadioButton");
        var style = xaml[styleStart..styleEnd];

        var checkedTrigger = style.IndexOf("Property=\"IsChecked\"", StringComparison.Ordinal);
        Assert.True(checkedTrigger > 0, "Триггер IsChecked не найден в OptionRadioButton");
        var triggerBlock = style[checkedTrigger..];

        Assert.DoesNotContain("AccentHoverBrush", triggerBlock);
        // Акцентная заливка выбранной карточки и контрастный текст (issue #107 п.1).
        Assert.Contains("<Setter TargetName=\"OptionBorder\" Property=\"Background\" Value=\"{DynamicResource AccentBrush}\"/>", triggerBlock);
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentBrush}\"", triggerBlock);
        // Рамка выбранной карточки сохранена (акцент, 2px).
        Assert.Contains("BorderThickness", triggerBlock);
    }

    [Fact]
    public void Avalonia_OptionCards_UseUnifiedMarkerPattern()
    {
        // Аудит UI P1c: все радиокарточки используют единый паттерн OptionCard —
        // тонированный фон, акцентная рамка и маркер-точка. AddOptionCard больше
        // не отдельный вид со сплошной акцентной заливкой, а псевдоним OptionCard.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));

        var optionStart = axaml.IndexOf("x:Key=\"OptionCard\"", StringComparison.Ordinal);
        Assert.True(optionStart > 0, "ControlTheme OptionCard не найден в Controls.axaml");
        var optionEnd = axaml.IndexOf("</ControlTheme>", optionStart, StringComparison.Ordinal);
        Assert.True(optionEnd > optionStart, "Не найден конец ControlTheme OptionCard");
        var option = axaml[optionStart..optionEnd];

        // Маркер и акцентная рамка выбранного варианта.
        Assert.Contains("Name=\"PART_Dot\"", option);
        Assert.Contains("^:checked", option);
        Assert.Contains("Property=\"BorderBrush\" Value=\"{DynamicResource AccentBrush}\"", option);
        Assert.DoesNotContain("AccentHoverBrush", option);

        // Псевдоним окна добавления не переопределяет шаблон и наследует паттерн.
        var addStart = axaml.IndexOf("x:Key=\"AddOptionCard\"", StringComparison.Ordinal);
        Assert.True(addStart > 0, "ControlTheme AddOptionCard не найден в Controls.axaml");
        var addEnd = axaml.IndexOf("</ControlTheme>", addStart, StringComparison.Ordinal);
        Assert.True(addEnd > addStart, "Не найден конец ControlTheme AddOptionCard");
        var add = axaml[addStart..addEnd];

        Assert.Contains("BasedOn=\"{StaticResource OptionCard}\"", add);
        Assert.DoesNotContain("<ControlTemplate", add);
        Assert.DoesNotContain("Property=\"Background\" Value=\"{DynamicResource AccentBrush}\"", add);
    }

    [Fact]
    public void ColorScheme_ResolveTextOnAccent_CustomColorWinsAtSufficientContrast()
    {
        // П.2 #107: пользовательский цвет с контрастом ≥ 3:1 к акценту берётся как есть.
        // Тёмно-зелёный #13522C на акценте #16A34A — контраст ниже 3:1? Нет:
        // тёмный на среднем зелёном даёт < 3:1 → WCAG. Проверяем пары надёжно.
        // Чёрный на светлом акценте #FFD54F — контраст > 3:1 → берётся чёрный.
        Assert.Equal("#000000", Models.ColorScheme.ResolveTextOnAccentHex("#000000", "#FFD54F"));
        // Белый на тёмном акценте #0B3D91 — контраст > 3:1 → берётся белый.
        Assert.Equal("#FFFFFF", Models.ColorScheme.ResolveTextOnAccentHex("#FFFFFF", "#0B3D91"));
        // Слабый контраст (белый на почти белом #F5F5F5 < 3:1) → WCAG-фолбэк (чёрный).
        Assert.Equal("#000000", Models.ColorScheme.ResolveTextOnAccentHex("#FFFFFF", "#F5F5F5"));
        // Не задано — WCAG-фолбэк.
        Assert.Equal("#000000", Models.ColorScheme.ResolveTextOnAccentHex(null, "#F5F5F5"));
        Assert.Equal("#FFFFFF", Models.ColorScheme.ResolveTextOnAccentHex("", "#13522C"));
    }
}
