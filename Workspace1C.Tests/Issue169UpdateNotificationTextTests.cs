using System;
using System.IO;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #169 «Текст уведомления об обновлении»: в системном тосте «Доступна новая
/// версия X» выводилась ссылка на страницу релиза, но кликнуть по ней нельзя — она
/// просто занимает место. Достаточно сообщения о доступной версии.
/// <list type="bullet">
/// <item>текст уведомления для релиза с непустым <see cref="ReleaseInfo.HtmlUrl"/>
/// не содержит URL и содержит номер версии;</item>
/// <item>source-гварды: обе оболочки (WPF и Avalonia) больше не добавляют
/// <c>release.HtmlUrl</c> в текст уведомления.</item>
/// </list>
/// </summary>
public sealed class Issue169UpdateNotificationTextTests
{
    private const string ReleaseUrl =
        "https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.122";

    private static ReleaseInfo SampleRelease() => new()
    {
        TagName = "v1.0.1.122",
        Body = "### Исправлено\n- Текст уведомления об обновлении",
        HtmlUrl = ReleaseUrl
    };

    /// <summary>
    /// Ожидаемый текст уведомления: локализованный шаблон с тегом релиза.
    /// В юнит-тестах словари локализации могут быть не загружены, поэтому шаблон
    /// берётся тем же вызовом, что и в сервисе, — сравнение остаётся корректным.
    /// </summary>
    private static string ExpectedHeader(ReleaseInfo release)
        => string.Format(LocalizationManager.T("Notify.UpdateAvailable"), release.TagName);

    [Fact]
    public void UpdateAvailableMessage_WithUrl_IsHeaderOnly()
    {
        var release = SampleRelease();
        var message = UpdateService.BuildUpdateAvailableMessage(release);

        // Текст — ровно локализованный заголовок с номером версии, без ссылки.
        Assert.Equal(ExpectedHeader(release), message);
        Assert.DoesNotContain("http", message, StringComparison.Ordinal);
        Assert.DoesNotContain(ReleaseUrl, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void UpdateAvailableHeader_ContainsVersionPlaceholder(string languageFile)
    {
        // Номер версии попадает в уведомление из тега релиза: локализованный заголовок
        // обязан содержать плейсхолдер {0} (иначе версия из текста исчезнет).
        var template = ReadLanguageTemplate(languageFile);

        Assert.Contains("{0}", template, StringComparison.Ordinal);
        Assert.Contains(
            "v1.0.1.122", string.Format(template, "v1.0.1.122"), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateAvailableMessage_WithoutUrl_IsHeaderOnly()
    {
        var release = SampleRelease();
        release.HtmlUrl = null;

        var message = UpdateService.BuildUpdateAvailableMessage(release);

        Assert.Equal(ExpectedHeader(release), message);
        Assert.DoesNotContain("http", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateAvailableMessage_EmptyUrl_IsHeaderOnly()
    {
        var release = SampleRelease();
        release.HtmlUrl = "   ";

        var message = UpdateService.BuildUpdateAvailableMessage(release);

        Assert.Equal(ExpectedHeader(release), message);
        Assert.DoesNotContain("http", message, StringComparison.Ordinal);
    }

    // ===================== Source-гварды (паритет WPF/Avalonia) =====================

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

    private static string ReadLanguageTemplate(string languageFile)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", languageFile));
        var template = doc.RootElement
            .GetProperty("strings")
            .GetProperty("Notify.UpdateAvailable")
            .GetString();

        Assert.NotNull(template);
        return template!;
    }

    [Theory]
    [InlineData("UpdateService.cs")]
    [InlineData("UpdateService.Avalonia.cs")]
    public void UpdateService_Notification_DoesNotAppendReleaseUrl(string fileName)
    {
        // Issue #169: обе оболочки формируют только заголовок уведомления.
        var source = ReadProjectFile("Workspace1C", "Services", fileName);

        Assert.Contains("BuildUpdateAvailableMessage", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "message += Environment.NewLine + release.HtmlUrl", source, StringComparison.Ordinal);
        Assert.Contains("issue #169", source, StringComparison.Ordinal);
    }
}
