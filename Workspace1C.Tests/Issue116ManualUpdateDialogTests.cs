using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 (1.0.1.82, aleksandr-ermakov): диалог ручного обновления.
/// <list type="bullet">
/// <item>кнопка «Скачать» заменена на «Обновить»;</item>
/// <item>в режиме без предварительной загрузки заголовок —
/// «Доступна новая версия приложения для загрузки»;</item>
/// <item>добавлена ссылка «Что нового», открывающая стандартный диалог изменений
/// новой версии;</item>
/// <item>окно подгоняется под содержимое (обе оси, без минимальной высоты 320).</item>
/// </list>
/// WPF-окно собирается только на Windows, Avalonia-диалог — только на Linux,
/// поэтому проверки платформенных оболочек сделаны стражами исходников.
/// </summary>
public sealed class Issue116ManualUpdateDialogTests
{
    // ===================== WPF: XAML =====================

    [Fact]
    public void Wpf_Xaml_DownloadButton_SaysUpdate()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        Assert.Contains("Text=\"{loc:Loc Update.Update}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{loc:Loc Update.Download}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Xaml_WindowFitsContent()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        // Issue #116 (1.0.1.82): окно подстраивается по обеим осям, минимальная
        // высота 320 больше не держит пустое место.
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"440\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"320\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent=\"Height\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Xaml_HasWhatsNewLink()
    {
        var xaml = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml"));

        Assert.Contains("x:Name=\"WhatsNewLink\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WhatsNewLinkText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnWhatsNewLinkClick\"", xaml, StringComparison.Ordinal);
    }

    // ===================== WPF: код-бихайнд =====================

    [Fact]
    public void Wpf_Heading_DependsOnReadyToInstall()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        // Без предварительной загрузки — уточняющий заголовок; готовый файл — прежний.
        Assert.Contains("Update.NewVersionAvailableForDownload", source, StringComparison.Ordinal);
        Assert.Contains("Update.NewVersionAvailable", source, StringComparison.Ordinal);
        Assert.Contains("readyToInstall", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_WhatsNewLink_OpensStandardWhatsNewDialog()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        Assert.Contains("OnWhatsNewLinkClick", source, StringComparison.Ordinal);
        Assert.Contains("Update.WhatsNewLink", source, StringComparison.Ordinal);
        Assert.Contains("new WhatsNewWindow(", source, StringComparison.Ordinal);
        // Показываются изменения НОВОЙ версии: версия и тело берутся из релиза.
        Assert.Contains("_release.Body", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeTag(_release.TagName)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_WindowSize_ReliesOnSizeToContent_WithoutManualMeasurement()
    {
        // Issue #116 (комментарий 7OH от 17.11.2026 14:07): окно «поплыло» из-за
        // ручного измерения размера. Размер обязан считаться только SizeToContent
        // из XAML — без WndProc, сброса в Manual и зафиксированных габаритов.
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));

        Assert.DoesNotContain("SizeToContent = SizeToContent.Manual", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureFixedSize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_lockedWidthDips", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_lockedHeightDips", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WndProc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WmGetMinMaxInfo", source, StringComparison.Ordinal);

        // Пересчёт разметки после смены этапа остаётся безопасным (без смены габаритов).
        Assert.Contains("private void RefreshWindowSize()", source, StringComparison.Ordinal);
        Assert.Contains("UpdateLayout();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsNewWindow_SupportsVersionAndBodyOverride()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.xaml.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.Avalonia.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            Assert.Contains("string? versionOverride = null", source, StringComparison.Ordinal);
            Assert.Contains("string? markdownBodyOverride = null", source, StringComparison.Ordinal);
        }
    }

    // ===================== Avalonia =====================

    [Fact]
    public void Avalonia_Dialog_SupportsUpdateButtonAndNotesLink()
    {
        var window = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        var service = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "AvaloniaDialogService.cs"));
        var update = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));

        Assert.Contains("string? okButtonText = null", window, StringComparison.Ordinal);
        Assert.Contains("string? notesLinkText = null", window, StringComparison.Ordinal);
        Assert.Contains("Action? onNotesLink = null", window, StringComparison.Ordinal);
        Assert.Contains("notesLink.Click +=", window, StringComparison.Ordinal);

        Assert.Contains("string? okButtonText = null", service, StringComparison.Ordinal);
        Assert.Contains("string? notesLinkText = null", service, StringComparison.Ordinal);
        Assert.Contains("Action? showNotes = null", service, StringComparison.Ordinal);

        Assert.Contains("Update.Update", update, StringComparison.Ordinal);
        Assert.Contains("Update.NewVersionAvailableForDownload", update, StringComparison.Ordinal);
        Assert.Contains("ShowNewVersionNotes(", update, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json", "Обновить", "Доступна новая версия приложения для загрузки", "Что нового")]
    [InlineData("en.json", "Update", "A new version of the application is available for download", "What's new")]
    public void Localization_HasManualUpdateDialogKeys(
        string file, string updateButton, string heading, string whatsNewLink)
    {
        var strings = LoadStrings(file);

        Assert.Equal(updateButton, strings["Update.Update"]);
        Assert.Equal(heading, strings["Update.NewVersionAvailableForDownload"]);
        Assert.Equal(whatsNewLink, strings["Update.WhatsNewLink"]);
        // Прежний ключ «Скачать» остаётся для других сценариев (ручная загрузка).
        Assert.True(strings.ContainsKey("Update.Download"));
    }

    [Fact]
    public void Localization_RuAndEn_HaveSameUpdateKeySet()
    {
        var ru = LoadStrings("ru.json").Keys
            .Where(key => key.StartsWith("Update.", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var en = LoadStrings("en.json").Keys
            .Where(key => key.StartsWith("Update.", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ru, en);
    }

    // ===================== Вспомогательное =====================

    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    private static System.Collections.Generic.Dictionary<string, string> LoadStrings(string fileName)
    {
        using var doc = JsonDocument.Parse(ReadProjectFile(
            Path.Combine("Workspace1C", "Localization", "Languages", fileName)));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }
}
