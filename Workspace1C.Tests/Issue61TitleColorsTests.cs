using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #61 «Цвета заголовков в тёмной версии» (автор 7OH): чёрный/тёмный текст
/// на тёмном фоне в тёмной теме, минимум в двух окнах. Файловые проверки исходников
/// (по образцу Issue60SearchIconColorTests / ButtonThemeBindingTests):
/// — бейджи избранного (Alt+N в строке базы и в «Горячих клавишах», подпись
///   «Избранное» на кремовой плашке) используют кисть FavoriteTextBrush — контрастный
///   текст к FavoriteColor (WCAG), а не фиксированные тёмные константы;
/// — во всех Views/*.xaml и *.axaml нет TextBlock с фиксированным тёмным Foreground;
/// — текстовые построители код-окон (AvaloniaUi.Text/Header/Cell) привязывают кисти темы
///   (раньше полагались на глобальный стиль — «чёрное на тёмном» в окнах вроде
///   публикации баз и TechLog).
/// Локализация не затронута — новых строк нет.
/// </summary>
public sealed class Issue61TitleColorsTests
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
    public void MainWindowAndSettings_Wpf_BadgesUseFavoriteTextBrush_NoDarkConstants()
    {
        var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        // Номера слотов Alt+N и подпись «Избранное»: текст на плашке избранного через
        // контрастную кисть FavoriteTextBrush, а не фиксированную константу #1C1917
        // (нечитаемо при тёмных пользовательских схемах) и не сам FavoriteBrush
        // (жёлтый по жёлтому в тёмной теме).
        Assert.Contains("Foreground=\"{DynamicResource FavoriteTextBrush}\"", main, System.StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource FavoriteTextBrush}\"", settings, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#1C1917\"", main, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#1C1917\"", settings, System.StringComparison.Ordinal);
        // Текст больше нигде не красится кистью плашки (только фон Border).
        // Исключение (аудит UI 1.0.1.10, п. 2.10): кисть разрешена значку-звезде
        // PackIcon Star в MainWindow — это иконка, а не текст на плашке.
        foreach (var line in main.Split('\n'))
        {
            if (line.Contains("Foreground=\"{DynamicResource FavoriteBrush}\"", System.StringComparison.Ordinal))
                Assert.True(line.Contains("PackIcon", System.StringComparison.Ordinal),
                    "Foreground=\"{DynamicResource FavoriteBrush}\" допустим только у иконки PackIcon, не у текста");
        }
        Assert.DoesNotContain("Foreground=\"{DynamicResource FavoriteBrush}\"", settings, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("SettingsWindow.xaml")]
    public void ConfiguredWindow_NoDarkFixedTextForeground(string fileName)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));
        Assert.DoesNotContain("Foreground=\"Black\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#000000\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#111827\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#0F172A\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#1E293B\"", xaml, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Views_XamlAndAxaml_NoFixedDarkTextForeground()
    {
        // Grep-страж на «почти чёрный» текст в разметке обеих платформ.
        // Исключения из чёрного списка не требуются: единственные намеренно тёмные
        // тексты сидят на постоянных светлых плашках (#EDE9FE / #FEF3C7) и потому
        // не входят в перечень (#5B21B6 «Закреплено», #1C1917 теперь через кисть).
        var root = FindRepoRoot();
        var views = Path.Combine(root, "Workspace1C", "Views");
        var forbidden = new[]
        {
            "Foreground=\"Black\"",
            "Foreground=\"#000000\"",
            "Foreground=\"#1C1917\"",
            "Foreground=\"#111827\"",
            "Foreground=\"#0F172A\"",
            "Foreground=\"#1E293B\""
        };

        foreach (var file in Directory.EnumerateFiles(views, "*.xaml").Concat(Directory.EnumerateFiles(views, "*.axaml")))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in forbidden)
            {
                Assert.False(text.Contains(pattern, System.StringComparison.Ordinal),
                    $"В {Path.GetFileName(file)} найден фиксированный тёмный текст: {pattern}");
            }
        }
    }

    [Fact]
    public void Themes_And_Palettes_ExposeFavoriteTextBrush()
    {
        // Кисть объявлена в обеих темах WPF и Avalonia и в обеих палитрах схемы
        // (константа-фолбэк для предзапуска; реальный контраст добирает ThemeManager).
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var text = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("x:Key=\"FavoriteTextBrush\"", text, System.StringComparison.Ordinal);
            Assert.Contains("x:Key=\"FavoriteTextColor\"", text, System.StringComparison.Ordinal);
        }

        foreach (var theme in new[] { "LightTheme.axaml", "DarkTheme.axaml" })
        {
            var text = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("x:Key=\"FavoriteTextBrush\"", text, System.StringComparison.Ordinal);
            Assert.Contains("x:Key=\"FavoriteTextColor\"", text, System.StringComparison.Ordinal);
        }

        var palette = ReadProjectFile(Path.Combine("Workspace1C", "Models", "ColorScheme.cs"));
        var counter = System.Text.RegularExpressions.Regex.Matches(palette, "FavoriteTextColor").Count;
        Assert.True(counter >= 4, $"В палитрах ColorScheme ожидалось ≥4 вхождений FavoriteTextColor (светлая/тёмная + генератор), найдено: {counter}");

        // ThemeManager (обе платформы) рассчитывает контраст к FavoriteColor по WCAG.
        var wpfManager = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var avaloniaManager = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.Avalonia.cs"));
        Assert.Contains("\"FavoriteTextBrush\", \"FavoriteColor\"", wpfManager, System.StringComparison.Ordinal);
        Assert.Contains("\"FavoriteTextBrush\", \"FavoriteColor\"", avaloniaManager, System.StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowAndSettings_Avalonia_BadgeTextBindsFavoriteTextBrush()
    {
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        Assert.Contains("\"FavoriteTextBrush\"", tree, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(\"#1C1917\")", tree, System.StringComparison.Ordinal);

        Assert.Contains("\"FavoriteTextBrush\"", settings, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(\"#1C1917\")", settings, System.StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaUi_TextHeaderCell_BindThemeBrushes()
    {
        // Построители окон в коде (окно публикации «пять полей», TechLog, DBMS, бот):
        // подписи, шапки и ячейки привязываются к кистям темы, а не остаются
        // чёрными по умолчанию TextBlock.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "AvaloniaUi.Avalonia.cs"));
        var binds = System.Text.RegularExpressions.Regex.Matches(code, "ThemeBrushes.Bind").Count;
        Assert.True(binds >= 4, $"Ожидалось ≥4 привязки ThemeBrushes.Bind в AvaloniaUi (Text/Header/Cell), найдено: {binds}");
        Assert.Contains("TextSecondaryBrush", code, System.StringComparison.Ordinal);
        Assert.Contains("TextPrimaryBrush", code, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ServerEditWindow_Avalonia_FieldCaptionsAndTitle_BindThemeBrushes()
    {
        // Issue #61 (комментарий 7OH от 2026-10-25): «Заходим в первое окно (Изменение
        // сервера) и заголовки полей по-прежнему чёрные» — подписи полей собраны кодом
        // и привязаны к единой кисти подписи формы FormLabelBrush (читаема в обеих
        // темах и пользовательских схемах), заголовок окна — к первичному тексту темы.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.Avalonia.cs"));
        Assert.Contains("ThemeBrushes.Bind(caption, TextBlock.ForegroundProperty, \"FormLabelBrush\");",
            code, System.StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(title, TextBlock.ForegroundProperty, \"TextPrimaryBrush\");",
            code, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ServerEditWindow_Wpf_PasswordRevealField_UsesThemeTextBoxStyle()
    {
        // Поле показа пароля окна «Изменение сервера» — стиль ModernTextBox (читаемый
        // текст/фон в обеих темах; issue #61 «текст в поле прочитать сложно»).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.xaml"));
        Assert.Contains("PasswordRevealTextBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ModernTextBox}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptWindows_Wpf_PreviewBoxThemed_AndLabelFormLabelBrush()
    {
        // Issue #61 (замечание 7OH): «Выбор скрипта — поле Командная строка» и итоговая
        // строка запуска — по-прежнему тёмный текст на тёмном фоне. Подпись переведена
        // на единую кисть FormLabelBrush, поле-предпросмотр — на темизированный
        // ModernTextBox (фон/текст читаемы в обеих темах).
        foreach (var fileName in new[] { "ScriptPickWindow.xaml", "ScriptScenarioEditWindow.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));
            Assert.Contains("Foreground=\"{DynamicResource FormLabelBrush}\"", xaml, System.StringComparison.Ordinal);
            Assert.Contains("Style=\"{StaticResource ModernTextBox}\"", xaml, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MenuEditorWindows_Wpf_TreeViewUsesModernTreeViewItem()
    {
        // Issue #61 (замечание 7OH): тёмные пиктограммы переключения папки в списках
        // редакторов структур меню. Третий редактор получил ItemContainerStyle
        // ModernTreeViewItem — темизированные раскрыватели и выравнивание вместо
        // дефолтных тёмных «+/-»; иконки каталога остаются пользовательскими цветами.
        foreach (var fileName in new[]
        {
            "ContextMenuEditorWindow.xaml",
            "TrayMenuEditorWindow.xaml",
            "UtilitiesMenuEditorWindow.xaml"
        })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));
            Assert.Contains("ItemContainerStyle=\"{StaticResource ModernTreeViewItem}\"",
                xaml, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LaunchParametersWindow_ListView_UsesThemedContainer()
    {
        // Issue #61 (комментарий 7OH от 2026-10-26): «Конфигуратор параметров запуска» —
        // строка списка выделялась системными цветами (тёмное на тёмном при наведении,
        // белый фон с белыми буквами при выборе без фокуса). ListView с GridView теперь
        // покрыт неявным стилем темы (ModernListViewItem) — в разметке окна нет
        // фиксированных белых/тёмных фонов выделения.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParametersWindow.xaml"));
        Assert.Contains("<ListView", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"White\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#FFFFFF\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Black\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#000000\"", xaml, System.StringComparison.Ordinal);
    }
}
