using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 (комментарии 7OH от 17.11.2026 14:04 и 14:07, aleksandr-ermakov от 10:03):
/// <list type="bullet">
/// <item>при любом действии пользователя (ручная проверка, значок в подвале, диалог
/// обновления) системное уведомление о доступной версии НЕ показывается — оно
/// допустимо только на фоновых путях;</item>
/// <item>обновление, инициированное пользователем, не показывает и уведомление
/// «Приложение обновлено» после перезапуска (маркер pending_update.json);</item>
/// <item>основная кнопка диалога обновления называется «Обновить» во всех режимах
/// и на всех путях, в том числе когда файл уже скачан;</item>
/// <item>окно диалога подгоняется под содержимое без ручного измерения размера
/// (WPF — SizeToContent, Avalonia — fitContent).</item>
/// </list>
/// Платформенные оболочки проверяются source-гвардами, как в остальных тестах Issue116.
/// </summary>
public sealed class Issue116ManualUpdateNoNotificationTests
{
    // ===================== Чистая политика уведомлений =====================

    [Theory]
    [InlineData(UpdateNotificationOrigin.Background, false, true)]
    [InlineData(UpdateNotificationOrigin.Background, true, false)]
    [InlineData(UpdateNotificationOrigin.UserAction, false, false)]
    [InlineData(UpdateNotificationOrigin.UserAction, true, false)]
    public void ShouldNotify_OnlyOnBackgroundWithoutUserAction(
        UpdateNotificationOrigin origin, bool userActionInProgress, bool expected)
    {
        Assert.Equal(expected, UpdateNotificationPolicy.ShouldNotify(origin, userActionInProgress));
    }

    // ===================== Маркер «Приложение обновлено» =====================

    [Fact]
    public void ManualUpdateMarker_SuppressesInstalledNotification()
    {
        var dir = TempDir();
        try
        {
            var store = new PendingUpdateStore(directoryOverride: dir);
            store.Write("v1.0.1.90", "notes", "https://example.org/r",
                notifyOnNextStart: false);

            var marker = store.ReadMarkerAndClear();

            Assert.NotNull(marker);
            Assert.False(marker!.NotifyOnNextStart);
            Assert.False(PendingUpdateStore.ShouldNotifyForMarker(marker, "1.0.1.90"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void BackgroundUpdateMarker_KeepsInstalledNotification()
    {
        var dir = TempDir();
        try
        {
            var store = new PendingUpdateStore(directoryOverride: dir);
            store.Write("v1.0.1.90", "notes", "https://example.org/r");

            var marker = store.ReadMarkerAndClear();

            Assert.NotNull(marker);
            Assert.True(marker!.NotifyOnNextStart);
            Assert.True(PendingUpdateStore.ShouldNotifyForMarker(marker, "1.0.1.90"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void OldMarkerWithoutFlag_ReadsAsNotify()
    {
        // Маркеры прежних версий не содержат NotifyOnNextStart — прежнее поведение
        // (уведомление показывается) должно сохраниться.
        var dir = TempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, PendingUpdateStore.FileName),
                "{\"Version\":\"v1.0.1.90\"}");

            var marker = new PendingUpdateStore(directoryOverride: dir).ReadMarkerAndClear();

            Assert.NotNull(marker);
            Assert.True(marker!.NotifyOnNextStart);
            Assert.True(PendingUpdateStore.ShouldNotifyForMarker(marker, "1.0.1.90"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ===================== Source-гварды: ручные пути =====================

    [Theory]
    [InlineData("UpdateService.cs")]
    [InlineData("UpdateService.Avalonia.cs")]
    public void ManualCheck_SuppressesBackgroundNotifications(string fileName)
    {
        var source = ReadProjectFile("Workspace1C", "Services", fileName);

        // Ручная проверка оборачивается в счётчик действия пользователя.
        var manual = ExtractMethod(source, "public async Task CheckForUpdatesManualAsync()");
        Assert.Contains("BeginUserAction();", manual, StringComparison.Ordinal);
        Assert.Contains("EndUserAction();", manual, StringComparison.Ordinal);
        Assert.Contains("RememberUserActionRelease(release);", manual, StringComparison.Ordinal);
        // На ручном пути уведомления нет вообще — только диалог/результат проверки.
        Assert.DoesNotContain("NotifyUpdateAvailable", manual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UpdateService.cs", "private void ShowUpdateDialog(ReleaseInfo release)")]
    [InlineData("UpdateService.Avalonia.cs", "private async Task ShowUpdateDialogAsync(ReleaseInfo release)")]
    public void UpdateDialog_IsUserAction(string fileName, string signature)
    {
        var source = ReadProjectFile("Workspace1C", "Services", fileName);
        var method = ExtractMethod(source, signature);

        Assert.Contains("BeginUserAction();", method, StringComparison.Ordinal);
        Assert.Contains("EndUserAction();", method, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UpdateService.cs")]
    [InlineData("UpdateService.Avalonia.cs")]
    public void NotifyUpdateAvailable_UsesPolicyAndDeduplicates(string fileName)
    {
        var source = ReadProjectFile("Workspace1C", "Services", fileName);

        Assert.Contains(
            "UpdateNotificationPolicy.ShouldNotify(origin, IsUserActionInProgress)",
            source, StringComparison.Ordinal);
        Assert.Contains("IsUserActionRelease(release)", source, StringComparison.Ordinal);
        Assert.Contains("IsLastNotifiedRelease(release)", source, StringComparison.Ordinal);
        // Фоновые вызовы остались прежними — уведомление допустимо только там.
        Assert.Contains("NotifyUpdateAvailable(release);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ApplyReadyInstall_DoesNotNotifyAfterRestart()
    {
        var source = ReadProjectFile("Workspace1C", "Services", "UpdateService.cs");

        // Пользователь нажал «Обновить»/«Установить» — уведомление не нужно.
        Assert.Contains(
            "_pendingUpdate.Write(release.TagName, release.Body, release.HtmlUrl, notifyOnNextStart: false);",
            source, StringComparison.Ordinal);
        // Ручная проверка в режиме «Обновлять при запуске» — тот же ручной путь.
        Assert.Contains(
            "DownloadAndInstallAutoAsync(release, notifyOnNextStart: false)",
            source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("App.xaml.cs")]
    [InlineData("App.axaml.cs")]
    public void Startup_ChecksMarkerNotificationFlag(string appFile)
    {
        var source = ReadProjectFile("Workspace1C", appFile);

        Assert.Contains(
            "PendingUpdateStore.ShouldNotifyForMarker(marker, VersionInfo.Display())",
            source, StringComparison.Ordinal);
    }

    // ===================== Кнопка «Обновить» =====================

    [Fact]
    public void Wpf_PrimaryButtons_SayUpdateInEveryState()
    {
        var xaml = ReadProjectFile("Workspace1C", "Services", "UpdateAvailableWindow.xaml");

        // Не скачано — «Обновить»; уже скачано — тоже «Обновить» (issue #116).
        Assert.Contains("x:Name=\"DownloadText\" Text=\"{loc:Loc Update.Update}\"", xaml);
        Assert.Contains("x:Name=\"InstallText\" Text=\"{loc:Loc Update.Update}\"", xaml);
        Assert.DoesNotContain("Text=\"{loc:Loc Update.Download}\"", xaml);
        Assert.DoesNotContain("Text=\"{loc:Loc Update.Install}\"", xaml);
    }

    [Fact]
    public void Avalonia_PrimaryButton_SaysUpdate()
    {
        var service = ReadProjectFile("Workspace1C", "Services", "UpdateService.Avalonia.cs");

        Assert.Contains(
            "var okText = LocalizationManager.T(\"Update.Update\");",
            service, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LocalizationManager.T(\"Update.Download\")", service, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json", "Обновить")]
    [InlineData("en.json", "Update")]
    public void Localization_UpdateKeyIsPrimaryButtonLabel(string file, string expected)
    {
        var strings = LoadStrings(file);
        Assert.Equal(expected, strings["Update.Update"]);
        // Ключ прежней кнопки остаётся для ручной загрузки в других сценариях.
        Assert.True(strings.ContainsKey("Update.Download"));
    }

    // ===================== Стабильный размер окна =====================

    [Fact]
    public void Wpf_DialogSize_IsCalculatedBySizeToContentOnly()
    {
        var xaml = ReadProjectFile("Workspace1C", "Services", "UpdateAvailableWindow.xaml");
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"640\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs");
        Assert.DoesNotContain("SizeToContent = SizeToContent.Manual", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WndProc", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureFixedSize", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_DialogSize_FitsContentWithoutManualMeasurement()
    {
        var window = ReadProjectFile("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs");
        Assert.Contains("bool fitContent = false", window, StringComparison.Ordinal);
        Assert.Contains("SizeToContent.WidthAndHeight", window, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 440;", window, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 760;", window, StringComparison.Ordinal);
        Assert.Contains("CanResize = false;", window, StringComparison.Ordinal);

        var service = ReadProjectFile("Workspace1C", "Services", "AvaloniaDialogService.cs");
        Assert.Contains("fitContent: true", service, StringComparison.Ordinal);
    }

    // ===================== Вспомогательное =====================

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        Assert.Fail("Корень репозитория не найден (CHANGELOG.md)");
        return string.Empty;
    }

    private static string ReadProjectFile(params string[] relativeParts)
    {
        var fullPath = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Вырезает тело метода от сигнатуры до парной закрывающей скобки.</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Метод не найден: {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"Тело метода не найдено: {signature}");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        Assert.Fail($"Конец метода не найден: {signature}");
        return string.Empty;
    }

    private static System.Collections.Generic.Dictionary<string, string> LoadStrings(string fileName)
    {
        using var doc = JsonDocument.Parse(ReadProjectFile(
            "Workspace1C", "Localization", "Languages", fileName));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in strings.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm-issue116-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* temp */ }
    }
}
