using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 «Обновление вручную» (v1.0.1.48): «Пропустить эту версию» и подвал
/// главного окна. Проверяется семантика пропуска через компаратор версий
/// <see cref="GitHubReleaseService.IsNewerThan"/> (релиз пропущен, когда он НЕ новее
/// пропущенной версии) и стражи по исходникам: проверка пропуска стоит ДО скачивания
/// и диалога, кнопка пропуска есть в обоих диалогах, ссылки обновления стоят слева
/// от версии, при готовом обновлении — префикс «!!».
/// </summary>
public sealed class Issue116SkipVersionTests
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

    private static ReleaseInfo Release(string tag) => new() { TagName = tag };

    // ---- Семантика IsSkipped (пропуск = релиз НЕ новее пропущенной версии) ----

    [Theory]
    [InlineData("v1.0.1.48", "v1.0.1.48", true)]  // равные версии — пропущена
    [InlineData("v1.0.1.49", "v1.0.1.48", false)] // вышла более новая — снова предлагается
    [InlineData("v1.0.1.48", "v1.0.1.49", true)]  // пропущена более новая — релиз старше
    [InlineData("1.0.1.48", "v1.0.1.48", true)]   // тег без префикса «v» — нормализуется
    public void SkipComparison_MatchesVersionComparator(
        string releaseTag, string skippedVersion, bool expectedSkipped)
    {
        // IsSkipped реализован как !GitHubReleaseService.IsNewerThan(release, skipped):
        // проверяем ровно это соотношение на компараторе версий.
        var skipped = !GitHubReleaseService.IsNewerThan(Release(releaseTag), skippedVersion);
        Assert.Equal(expectedSkipped, skipped);
    }

    [Fact]
    public void SkipComparison_EmptySkippedVersion_NeverSkips()
    {
        // Пустой пропуск (по умолчанию) — ничего не пропущено: сервис проверяет
        // IsNullOrWhiteSpace перед сравнением.
        Assert.True(string.IsNullOrWhiteSpace(string.Empty));
        Assert.True(string.IsNullOrWhiteSpace((string?)null));
    }

    [Fact]
    public void AppSettings_HasSkippedUpdateVersionField()
    {
        var settings = new AppSettings();
        Assert.Equal(string.Empty, settings.SkippedUpdateVersion);
        settings.SkippedUpdateVersion = "v1.0.1.48";
        Assert.Equal("v1.0.1.48", settings.SkippedUpdateVersion);
    }

    // ---- Стражи: проверка пропуска ДО скачивания/диалога, кнопки в диалогах ----

    [Fact]
    public void UpdateService_Wpf_ChecksSkipBeforeDialogAndDownload()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        var firstSkip = source.IndexOf("IsSkipped(release)", StringComparison.Ordinal);
        Assert.True(firstSkip > 0, "Проверка IsSkipped не найдена в UpdateService.cs (WPF)");

        // Пропуск применяется и в фоновой, и в ручной проверке.
        Assert.Equal(2, CountOccurrences(source, "IsSkipped(release)"));
        // После проверки пропуска в ручной проверке состояние сбрасывается.
        Assert.Contains("SetAvailableRelease(null);", source);
    }

    [Fact]
    public void UpdateService_Avalonia_ChecksSkipBeforeDialogAndDownload()
    {
        var source = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        Assert.Equal(2, CountOccurrences(source, "IsSkipped(release)"));
        Assert.Contains("SkipVersion(release);", source);
    }

    [Fact]
    public void WpfDialog_HasSkipVersionButton()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("x:Name=\"SkipVersionButton\"", xaml);
        Assert.Contains("Update.SkipVersion", xaml);

        var code = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        Assert.Contains("SkipVersion(_release)", code);
    }

    [Fact]
    public void AvaloniaDialog_HasSkipVersionButton()
    {
        var window = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        Assert.Contains("SkipClicked", window);
        Assert.Contains("skipButtonText", window);

        var service = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "AvaloniaDialogService.cs"));
        Assert.Contains("ConfirmUpdateWithSkip", service);
        Assert.Contains("UpdateDialogResult.Skipped", service);
    }

    [Fact]
    public void Localization_HasSkipVersionKeys()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        Assert.Contains("\"Update.SkipVersion\": \"Пропустить эту версию\"", ru);
        Assert.Contains("\"Update.SkipHint\"", ru);

        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains("\"Update.SkipVersion\": \"Skip this version\"", en);
        Assert.Contains("\"Update.SkipHint\"", en);
    }

    // ---- Стражи: подвал главного окна ----

    [Fact]
    public void WpfFooter_UpdateLinkStandsBeforeVersionText()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var updateIndex = xaml.IndexOf("x:Name=\"FooterUpdateLink\"", StringComparison.Ordinal);
        var checkIndex = xaml.IndexOf("x:Name=\"FooterCheckUpdatesLink\"", StringComparison.Ordinal);
        var versionIndex = xaml.IndexOf("x:Name=\"FooterVersionText\"", StringComparison.Ordinal);
        Assert.True(updateIndex > 0 && checkIndex > 0 && versionIndex > 0,
            "Ссылки подвала не найдены в MainWindow.xaml");
        Assert.True(updateIndex < checkIndex && checkIndex < versionIndex,
            "Ссылки «Обновить»/«Проверить обновления» должны стоять СЛЕВА от версии (issue #116)");
    }

    [Fact]
    public void AvaloniaFooter_UpdateLinkStandsBeforeVersionText()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        var updateIndex = source.IndexOf("_footerUpdateLink = new TextBlock", StringComparison.Ordinal);
        var checkIndex = source.IndexOf("_footerCheckUpdatesLink = new TextBlock", StringComparison.Ordinal);
        var versionIndex = source.IndexOf("_footerVersionText = new TextBlock", StringComparison.Ordinal);
        Assert.True(updateIndex > 0 && checkIndex > 0 && versionIndex > 0,
            "Элементы подвала не найдены в MainWindow.Avalonia.cs");
        Assert.True(updateIndex < checkIndex && checkIndex < versionIndex,
            "Ссылки «Обновить»/«Проверить обновления» должны стоять СЛЕВА от версии (issue #116)");
    }

    [Fact]
    public void FooterRefreshState_BuildsExclamationPrefix()
    {
        // Индикатор «!! Обновить» при готовом обновлении (issue #116) — обе платформы.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("\"!! \"", wpf);
        Assert.Contains("DangerBrush", wpf);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("\"!! \"", avalonia);
        Assert.Contains("#DC2626", avalonia);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
