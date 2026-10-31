using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #51 «Цвет подчеркивания»: подчёркивание полей/ссылок берёт цвет из темы,
/// а не из жёстких цветов (Brushes.Blue, Color.Parse("#0B6BCB") и т.п.).
/// Файловые проверки исходников (по образцу ButtonThemeBindingTests):
/// жёсткие цвета рядом с TextDecorations.Underline заменены на кисти темы
/// (AccentBrush в WPF через TryFindResource, ThemeBrushes.Bind в Avalonia).
/// </summary>
public sealed class UnderlineThemeBindingTests
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
    public void UpdateCheckWindow_NoHardcodedBlueUnderline()
    {
        // Issue #51: fallback Brushes.Blue у ссылки с подчёркиванием заменён на кисть
        // темы; v100113 (аудит UI): остатки фолбэков Brushes.Gray/Green тоже убраны —
        // все цвета статуса/ссылки берутся из семантических кистей темы.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "UpdateCheckWindow.xaml.cs"));
        Assert.DoesNotContain("Brushes.Blue", code);
        Assert.DoesNotContain("?? Brushes.Gray", code);
        Assert.DoesNotContain("?? Brushes.Green", code);
        Assert.Contains("FindResource(\"AccentBrush\") as Brush", code);
        Assert.Contains("FindResource(\"TextPrimaryBrush\") as Brush", code);
        Assert.Contains("FindResource(\"TextSecondaryBrush\") as Brush", code);
    }

    [Fact]
    public void MarkdownRenderer_NoHardcodedLinkBrush()
    {
        // Issue #51: жёсткий синий LinkBrush (#0B6BCB) убран из Avalonia-рендера.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MarkdownRenderer.cs"));
        Assert.DoesNotContain("LinkBrush", code);
        Assert.DoesNotContain("#0B6BCB", code);
    }

    [Fact]
    public void MarkdownRenderer_AvaloniaLinks_AreThemed()
    {
        // Оба места создания ссылок (Span) в Avalonia-рендере привязаны к AccentBrush темы.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MarkdownRenderer.cs"));
        var count = System.Text.RegularExpressions.Regex.Matches(
            code, "ThemeBrushes\\.Bind\\(link, Span\\.ForegroundProperty, \"AccentBrush\"\\)").Count;
        Assert.True(count >= 2, $"Ожидалось не менее 2 привязок ссылок к теме, найдено: {count}");
    }

    [Fact]
    public void MarkdownRenderer_WpfLinks_AreThemed()
    {
        // WPF-рендер: Hyperlink получает цвет из темы, а не системный синий.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MarkdownRenderer.cs"));
        Assert.Contains("TryFindResource(\"AccentBrush\") is Brush accent", code);
        Assert.Contains("link.Foreground = accent", code);
    }

    [Fact]
    public void AvaloniaUnderlinePlaces_UseThemeBrushes()
    {
        // Все Avalonia-места с TextDecorations.Underline привязаны к кистям темы.
        var files = new[]
        {
            Path.Combine("Workspace1C", "Views", "UpdateCheckWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Schemes.cs"),
            Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Services", "ManualUpdateWindow.Avalonia.cs")
        };
        foreach (var file in files)
        {
            var code = ReadProjectFile(file);
            Assert.Contains("TextDecorations.Underline", code);
            Assert.Contains("ThemeBrushes.Bind", code);
            Assert.DoesNotContain("new SolidColorBrush(Color.Parse(\"#0B6BCB\"))", code);
        }
    }

    [Fact]
    public void WpfThemes_OverrideMaterialDesignPrimaryWithAccent()
    {
        // Issue #51 (продолжение): поля Material Design без стиля ModernTextBox
        // (ConnectionSettingsWindow, SettingsWindow и др.) при фокусе рисуют полосу
        // кистью MaterialDesign.Brush.Primary (BundledTheme Primary=Amber — «оранжевая»).
        // Переопределяем ключ в темах на акцент схемы, чтобы подчёркивание следовало
        // выбранному пользователем акценту, а не жёсткому янтарю (issue #51).
        foreach (var name in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", name));
            Assert.Contains("x:Key=\"MaterialDesign.Brush.Primary\"", xaml);
            Assert.Contains("Color=\"{DynamicResource AccentColor}\"", xaml);
        }
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_FieldsUseThemeBrushes()
    {
        // Поля окна «Настройка информационной базы» (Avalonia) уже стилизованы
        // ModernTextBox/ModernPasswordBox — при фокусе акцентный контур темы (issue #51).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));
        Assert.Contains(".Styled(ControlThemes.ModernTextBox)", code);
        Assert.Contains(".Styled(ControlThemes.ModernPasswordBox)", code);
    }

    [Fact]
    public void AvaloniaFieldEditors_UseModernTextBox()
    {
        // Редакторы записей (сервер, учётная запись ITS, база rac, репозиторий):
        // поля ввода получают тему ModernTextBox — при фокусе контур из AccentBrush (issue #51).
        var files = new[]
        {
            Path.Combine("Workspace1C", "Views", "ServerEditWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "ItsAccountEditWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "RacInfobaseCreateWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "RepositoryBrowserWindow.Avalonia.cs")
        };
        foreach (var file in files)
        {
            var code = ReadProjectFile(file);
            Assert.Contains("ControlThemes.ModernTextBox", code);
        }
    }
}