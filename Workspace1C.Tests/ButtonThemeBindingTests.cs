using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #46 «Цвета кнопок»: кнопки без привязки цвета к теме привязываются к теме.
/// Файловые проверки исходников (по образцу SettingsWindowXamlResourcesTests):
/// в перечисленных окнах целевые кнопки используют стили темы
/// (SecondaryButton/IconButton в WPF, ControlThemes.SecondaryButton в Avalonia).
/// </summary>
public sealed class ButtonThemeBindingTests
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
    public void SettingsWindow_ScreenshotBrowseButton_IsThemed()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        // Кнопка «Обзор» каталога сохранения копий экрана получила вторичный стиль темы.
        var index = xaml.IndexOf("OnScreenshotBrowse_Click", System.StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка «Обзор» каталога копий экрана не найдена");
        var around = xaml.Substring(index - 300, 600);
        Assert.Contains("DynamicResource SecondaryButton", around);
    }

    [Fact]
    public void SettingsWindowProfile_BackupButtons_AreThemed()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Profile.cs"));
        // «Обзор», «Создать копию сейчас», «Восстановить сейчас» — 3 кнопки со стилем темы.
        Assert.Contains("TryFindResource(\"SecondaryButton\") as Style", code);
        var count = System.Text.RegularExpressions.Regex.Matches(code, "TryFindResource\\(\"SecondaryButton\"\\) as Style").Count;
        Assert.True(count >= 3, $"Ожидалось не менее 3 кнопок резервного копирования со стилем темы, найдено: {count}");
    }

    [Fact]
    public void ConnectionSettingsWindow_RepositoryPasteButton_IsThemed()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));
        var index = xaml.IndexOf("OnPasteRepositorySplit_Click", System.StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка «Вставить» в Хранилище не найдена");
        var around = xaml.Substring(index - 300, 600);
        Assert.Contains("DynamicResource SecondaryButton", around);
    }

    [Fact]
    public void ExportsListWindow_AllButtons_AreThemed()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ExportsListWindow.xaml"));
        // «Обновить», «Восстановить», «Открыть папку», «Закрыть» — все 4 кнопки.
        var count = System.Text.RegularExpressions.Regex.Matches(xaml, "DynamicResource SecondaryButton").Count;
        Assert.True(count >= 4, $"Ожидалось не менее 4 кнопок со стилем темы, найдено: {count}");
    }

    [Fact]
    public void ServerEditWindow_ShowPasswordButton_IsThemed()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.xaml"));
        var index = xaml.IndexOf("ShowPasswordButton", System.StringComparison.Ordinal);
        Assert.True(index > 0, "Кнопка просмотра пароля не найдена");
        var around = xaml.Substring(index - 200, 600);
        Assert.Contains("DynamicResource IconButton", around);
    }

    [Fact]
    public void Avalonia_SettingsWindow_ProfileAndScreenshotButtons_AreThemed()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("screenshotBrowse.Styled(ControlThemes.SecondaryButton)", code);
        Assert.Contains("profileBrowse.Styled(ControlThemes.SecondaryButton)", code);
        Assert.Contains("backupNow.Styled(ControlThemes.SecondaryButton)", code);
        Assert.Contains("restoreNow.Styled(ControlThemes.SecondaryButton)", code);
    }

    [Fact]
    public void Avalonia_ExportsListWindow_AllButtons_AreThemed()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ExportsListWindow.Avalonia.cs"));
        var count = System.Text.RegularExpressions.Regex.Matches(code, "ControlThemes.SecondaryButton").Count;
        Assert.True(count >= 4, $"Ожидалось не менее 4 кнопок со стилем темы, найдено: {count}");
    }

    [Fact]
    public void LaunchParametersWindow_AddAndRemoveButtons_AreThemed()
    {
        // Issue #50 «Цвета кнопок 2»: в окне «Запустить с выбором параметров»
        // кнопки используют стили темы. Аудит UI P2-1 (v1.0.1.91): удаление —
        // опасное действие, поэтому «Удалить» переведена на DangerButton,
        // «Добавить» остаётся вторичной.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParametersWindow.xaml"));
        var expected = new[]
        {
            ("BtnAddParam", "DynamicResource SecondaryButton"),
            ("BtnRemoveParam", "DynamicResource DangerButton")
        };
        foreach (var (name, style) in expected)
        {
            var index = xaml.IndexOf("x:Name=\"" + name + "\"", System.StringComparison.Ordinal);
            Assert.True(index > 0, $"Кнопка {name} не найдена");
            var start = xaml.LastIndexOf("<Button", index, System.StringComparison.Ordinal);
            var tag = xaml.Substring(start, index - start + 200);
            Assert.Contains(style, tag);
        }
    }
}
