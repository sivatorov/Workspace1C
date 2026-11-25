using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 «Обновление вручную» (комментарий 7OH от 16.11.2026): блок «Что нового»
/// лишний там, где пользователь сам нажал «Проверить обновления», а обрезанное описание
/// в системном уведомлении бессмысленно — оно не содержит полного списка изменений.
/// <list type="bullet">
/// <item>чистые тесты <see cref="AppUpdateModeHelper.ShouldShowReleaseNotes"/>: описание
/// релиза в диалоге скрывается только в <see cref="AppUpdateMode.Manual"/> и
/// <see cref="AppUpdateMode.NotifyOnly"/>;</item>
/// <item>текст уведомления «Доступна новая версия X» содержит номер версии, но НЕ
/// содержит описания выпуска, подписи «Что нового» и ссылки на релиз (issue #169);</item>
/// <item>source-гварды: обе оболочки больше не добавляют <c>Notify.ReleaseNotes</c> в
/// уведомление о доступной версии, окно WPF скрывает блок по флагу, Avalonia не передаёт
/// описание в диалог в ручных режимах;</item>
/// <item>регресс-гвард: уведомление об УСТАНОВЛЕННОМ обновлении
/// (<see cref="UpdateNotificationText"/>) не изменилось.</item>
/// </list>
/// </summary>
public sealed class Issue116UpdateNotesHiddenTests
{
    // ===================== Признак «показывать описание релиза» =====================

    [Theory]
    [InlineData(AppUpdateMode.Manual, false)]
    [InlineData(AppUpdateMode.NotifyOnly, false)]
    [InlineData(AppUpdateMode.InstallOnStartup, true)]
    [InlineData(AppUpdateMode.DownloadAskInstall, true)]
    public void ShouldShowReleaseNotes_DependsOnMode(AppUpdateMode mode, bool expected)
    {
        Assert.Equal(expected, AppUpdateModeHelper.ShouldShowReleaseNotes(mode));
    }

    [Fact]
    public void ShouldShowReleaseNotes_HidesNotesOnlyInManualModes()
    {
        // Ручные режимы — пользователь сам инициировал проверку, описание в диалоге лишнее.
        Assert.False(AppUpdateModeHelper.ShouldShowReleaseNotes(AppUpdateMode.Manual));
        Assert.False(AppUpdateModeHelper.ShouldShowReleaseNotes(AppUpdateMode.NotifyOnly));
        // Автоматические режимы — обновление «приходит само», описание остаётся.
        Assert.True(AppUpdateModeHelper.ShouldShowReleaseNotes(AppUpdateMode.InstallOnStartup));
        Assert.True(AppUpdateModeHelper.ShouldShowReleaseNotes(AppUpdateMode.DownloadAskInstall));
    }

    // ===================== Текст уведомления о доступной версии =====================

    private const string ReleaseBody =
        "### Исправлено\n- Скрыт блок «Что нового» в ручных режимах\n- Убрано обрезанное описание";

    private const string ReleaseUrl =
        "https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.78";

    private static ReleaseInfo SampleRelease() => new()
    {
        TagName = "v1.0.1.78",
        Body = ReleaseBody,
        HtmlUrl = ReleaseUrl
    };

    /// <summary>
    /// Ожидаемый заголовок уведомления: локализованный шаблон с тегом релиза.
    /// В юнит-тестах словари локализации могут быть не загружены, поэтому шаблон
    /// берётся тем же вызовом, что и в сервисе, — сравнение остаётся корректным.
    /// </summary>
    private static string ExpectedHeader(ReleaseInfo release)
        => string.Format(LocalizationManager.T("Notify.UpdateAvailable"), release.TagName);

    [Fact]
    public void UpdateAvailableMessage_IsHeaderOnly_WithoutReleaseNotes()
    {
        var release = SampleRelease();
        var message = UpdateService.BuildUpdateAvailableMessage(release);

        // Остался ровно заголовок с номером версии (issue #116, issue #169):
        // ссылка на релиз в системном тосте не кликабельна и больше не добавляется.
        Assert.Equal(ExpectedHeader(release), message);
        Assert.DoesNotContain(ReleaseUrl, message, StringComparison.Ordinal);
        Assert.DoesNotContain("http", message, StringComparison.Ordinal);
        // Описание выпуска и подпись «Что нового» в уведомление не попадают.
        Assert.DoesNotContain("Что нового", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Скрыт блок", message, StringComparison.Ordinal);
        Assert.DoesNotContain(ReleaseBody, message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateAvailableMessage_WithoutUrl_IsHeaderOnly()
    {
        var release = SampleRelease();
        release.HtmlUrl = null;

        var message = UpdateService.BuildUpdateAvailableMessage(release);

        Assert.Equal(ExpectedHeader(release), message);
        Assert.DoesNotContain("http", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Скрыт блок", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void UpdateAvailableHeader_ContainsVersionPlaceholder(string languageFile)
    {
        // Номер версии попадает в уведомление из тега релиза: локализованный заголовок
        // обязан содержать плейсхолдер {0} (иначе версия из текста исчезнет).
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", languageFile);
        using var doc = JsonDocument.Parse(json);
        var template = doc.RootElement
            .GetProperty("strings")
            .GetProperty("Notify.UpdateAvailable")
            .GetString();

        Assert.NotNull(template);
        Assert.Contains("{0}", template, StringComparison.Ordinal);
    }

    // ===================== Source-гварды =====================

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

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var fullPath = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Theory]
    [InlineData("UpdateService.cs")]
    [InlineData("UpdateService.Avalonia.cs")]
    public void UpdateService_Notification_NoLongerAddsReleaseNotesOrUrl(string fileName)
    {
        // Issue #116, issue #169: в уведомлении о доступной версии в обеих оболочках
        // остался только заголовок — ни «Что нового», ни ссылки на страницу релиза.
        var source = ReadProjectFile("Workspace1C", "Services", fileName);

        Assert.DoesNotContain("Notify.ReleaseNotes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TruncateReleaseNotes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxUpdateNotesLength", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "message += Environment.NewLine + release.HtmlUrl", source, StringComparison.Ordinal);

        Assert.Contains("BuildUpdateAvailableMessage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfDialog_HidesReleaseNotesByFlag()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs");

        // Флаг конструктора и скрытие обоих элементов блока «Что нового».
        Assert.Contains("showReleaseNotes", code, StringComparison.Ordinal);
        Assert.Contains("WhatsNewLabel.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("BodyViewer.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfShowUpdateDialog_PassesModeFlag()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "UpdateService.cs");

        // Значение флага берётся из единого признака по режиму обновления.
        Assert.Contains(
            "AppUpdateModeHelper.ShouldShowReleaseNotes(Mode)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaDialog_DoesNotGetReleaseNotesInManualModes()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "UpdateService.Avalonia.cs");

        // Описание релиза передаётся в диалог подтверждения только через признак режима.
        Assert.Contains("ReleaseNotesForDialog", code, StringComparison.Ordinal);
        Assert.Contains(
            "AppUpdateModeHelper.ShouldShowReleaseNotes(Mode)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledUpdateNotification_IsUnchanged()
    {
        // Уведомление об УСТАНОВЛЕННОМ обновлении — другой сценарий (issue #44):
        // описание изменений в нём остаётся.
        var message = UpdateNotificationText.BuildInstalledMessage(
            "v1.0.1.78", "- пункт изменений", "https://example.org/r");

        Assert.Contains("пункт изменений", message, StringComparison.Ordinal);
        Assert.Contains("https://example.org/r", message, StringComparison.Ordinal);

        var code = ReadProjectFile("Workspace1C", "Services", "UpdateNotificationText.cs");
        Assert.Contains("Notify.ReleaseNotes", code, StringComparison.Ordinal);
        Assert.Contains("BuildInstalledMessage", code, StringComparison.Ordinal);
    }
}
