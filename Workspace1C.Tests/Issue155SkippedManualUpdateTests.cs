using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #155 «Проверка обновлений при Пропустить версию»: после «Пропустить эту
/// версию» кнопка/значок «Обновить» в панели статуса должна снова открывать диалог
/// обновления (с возможностью скачать или пропустить повторно). Пропуск влияет
/// только на фоновые проверки.
/// <para>
/// Проверяется чистая логика <see cref="UpdateManualCheckLogic"/> и стражи по
/// исходникам WPF/Avalonia: ручная проверка не содержит раннего выхода по
/// <c>IsSkipped(release)</c>, показывает диалог с признаком «версия пропущена»,
/// а явное обновление очищает сохранённый пропуск.
/// </para>
/// </summary>
public sealed class Issue155SkippedManualUpdateTests
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

    private static string ReadServiceSource(string fileName) =>
        ReadProjectFile(Path.Combine("Workspace1C", "Services", fileName));

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static ReleaseInfo Release(string tag) => new() { TagName = tag };

    // ---- Сравнение версий (issue #155 п.5) ----

    [Theory]
    [InlineData("v1.0.1.115", "1.0.1.114")] // обычный инкремент
    [InlineData("v1.0.1.10", "1.0.1.9")]    // числовое, а не лексикографическое сравнение
    [InlineData("v1.0.1.115", "1.0.1")]     // 3-частная текущая версия нормализуется
    public void IsNewerThan_UsesNumericVersionComparison(string releaseTag, string current)
    {
        Assert.True(GitHubReleaseService.IsNewerThan(Release(releaseTag), current));
        Assert.False(GitHubReleaseService.IsNewerThan(Release(current), releaseTag));
    }

    [Fact]
    public void VersionComparison_ThreePartEqualsFourPartWithZeros()
    {
        // Issue #155: «1.0.1» и «1.0.1.0» — одна и та же версия, а не «прошлая».
        Assert.True(GitHubReleaseService.IsSameVersion(Release("v1.0.1"), "1.0.1.0"));
        Assert.False(GitHubReleaseService.IsNewerThan(Release("v1.0.1"), "1.0.1.0"));
    }

    [Fact]
    public void DowngradedCurrentVersion_ReleaseIsStillNewer()
    {
        // Issue #155 п.5: эффективная (понижённая) версия не должна делать
        // актуальный релиз «прошлым» — сравнение идёт по числовым частям.
        var release = Release("v1.0.1.115");
        const string downgradedCurrent = "1.0.1.114";

        Assert.True(GitHubReleaseService.IsNewerThan(release, downgradedCurrent));

        var evaluation = UpdateAvailabilityLogic.Reevaluate(
            release, downgradedCurrent, skipped: false);
        Assert.Same(release, evaluation.Release);
        Assert.False(evaluation.NeedsNetworkCheck);
    }

    // ---- Ручная проверка при пропущенной версии ----

    [Theory]
    [InlineData(AppUpdateMode.InstallOnStartup)]
    [InlineData(AppUpdateMode.DownloadAskInstall)]
    [InlineData(AppUpdateMode.NotifyOnly)]
    [InlineData(AppUpdateMode.Manual)]
    public void ManualCheck_SkippedRelease_AlwaysShowsDialog(AppUpdateMode mode)
    {
        var plan = UpdateManualCheckLogic.Plan(
            Release("v1.0.1.115"),
            currentVersion: "1.0.1.114",
            skippedVersion: "v1.0.1.115",
            mode);

        Assert.True(plan.HasNewerRelease);
        Assert.True(plan.WasSkipped);
        Assert.True(plan.ShowDialog);
        Assert.False(plan.InstallAutomatically);
    }

    [Fact]
    public void ManualCheck_SkippedRelease_InstallOnStartup_DoesNotInstallSilently()
    {
        var plan = UpdateManualCheckLogic.Plan(
            Release("v1.0.1.115"), "1.0.1.114", "v1.0.1.115",
            AppUpdateMode.InstallOnStartup);

        Assert.True(plan.ShowDialog);
        Assert.False(plan.InstallAutomatically);
    }

    [Fact]
    public void ManualCheck_NotSkipped_InstallOnStartup_StillAutoInstalls()
    {
        var plan = UpdateManualCheckLogic.Plan(
            Release("v1.0.1.115"), "1.0.1.114", skippedVersion: string.Empty,
            AppUpdateMode.InstallOnStartup);

        Assert.True(plan.HasNewerRelease);
        Assert.False(plan.WasSkipped);
        Assert.False(plan.ShowDialog);
        Assert.True(plan.InstallAutomatically);
    }

    [Fact]
    public void ManualCheck_NoNewerRelease_DoesNothing()
    {
        var plan = UpdateManualCheckLogic.Plan(
            Release("v1.0.1.114"), "1.0.1.114", "v1.0.1.115",
            AppUpdateMode.DownloadAskInstall);

        Assert.False(plan.HasNewerRelease);
        Assert.False(plan.ShowDialog);
        Assert.False(plan.InstallAutomatically);
    }

    [Fact]
    public void ManualCheck_NullRelease_DoesNothing()
    {
        var plan = UpdateManualCheckLogic.Plan(
            release: null, "1.0.1.114", "v1.0.1.115", AppUpdateMode.Manual);

        Assert.False(plan.HasNewerRelease);
        Assert.False(plan.ShowDialog);
    }

    [Fact]
    public void RepeatedSkip_SameVersion_RemainsSkippedWithoutException()
    {
        // Повторный пропуск той же версии: сохранённое значение перезаписывается
        // тем же тегом, семантика не меняется и исключений нет.
        var settings = new AppSettings();
        var release = Release("v1.0.1.115");

        settings.SkippedUpdateVersion = release.TagName!;
        Assert.True(UpdateManualCheckLogic.IsSkipped(release, settings.SkippedUpdateVersion));

        settings.SkippedUpdateVersion = release.TagName!;
        Assert.True(UpdateManualCheckLogic.IsSkipped(release, settings.SkippedUpdateVersion));
    }

    [Fact]
    public void MalformedSkippedVersion_DoesNotBlockUpdates()
    {
        // Issue #155: раньше !IsNewerThan для нераспознанной версии давал true и
        // скрывал ЛЮБОЕ обновление. Теперь битое значение ничего не блокирует.
        Assert.False(UpdateManualCheckLogic.IsSkipped(Release("v1.0.1.115"), "not-a-version"));
        Assert.False(UpdateManualCheckLogic.IsSkipped(Release("v1.0.1.115"), "   "));
    }

    [Fact]
    public void BackgroundCheck_StillHidesSkippedRelease()
    {
        // Автоматическая проверка не должна навязывать пропущенную версию.
        var release = Release("v1.0.1.115");
        var evaluation = UpdateAvailabilityLogic.Reevaluate(
            release, "1.0.1.114", skipped: true);

        Assert.Null(evaluation.Release);
        Assert.False(evaluation.NeedsNetworkCheck);
    }

    // ---- Стражи исходников: WPF ----

    [Fact]
    public void Wpf_ManualCheck_HasNoEarlyReturnOnSkipped()
    {
        var source = ReadServiceSource("UpdateService.cs");
        var manual = SliceMethod(
            source,
            "public async Task CheckForUpdatesManualAsync()",
            "private void ShowUpdateDialog(");

        Assert.DoesNotContain("IsSkipped(release)", manual);
        Assert.Contains("UpdateManualCheckLogic.Plan(", manual);
        Assert.Contains("ShowUpdateDialog(release, plan.WasSkipped)", manual);
    }

    [Fact]
    public void Wpf_Dialog_ShowsSkippedHintAndClearsSkipOnUpdate()
    {
        var window = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        Assert.Contains("bool wasSkipped = false", window);
        Assert.Contains("Update.SkippedHint", window);

        var xaml = ReadProjectFile(
            Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("x:Name=\"SkippedHintText\"", xaml);

        // Явное обновление (скачивание/установка готового файла) снимает пропуск.
        var download = SliceMethod(window, "private async void OnDownloadClick", "/// <summary>Обновляет индикатор");
        Assert.Contains("ClearSkippedVersion()", download);
        var install = SliceMethod(window, "private void OnInstallClick", "/// <summary>«Обновить после закрытия»");
        Assert.Contains("ClearSkippedVersion()", install);
    }

    [Fact]
    public void Wpf_Service_HasClearSkippedVersion()
    {
        var source = ReadServiceSource("UpdateService.cs");
        Assert.Contains("public void ClearSkippedVersion()", source);
        Assert.Contains("settings.SkippedUpdateVersion = string.Empty;", source);
    }

    // ---- Стражи исходников: Avalonia ----

    [Fact]
    public void Avalonia_ManualCheck_HasNoEarlyReturnOnSkipped()
    {
        var source = ReadServiceSource("UpdateService.Avalonia.cs");
        var manual = SliceMethod(
            source,
            "public async Task CheckForUpdatesManualAsync()",
            "private async Task ShowUpdateDialogAsync(");

        Assert.DoesNotContain("IsSkipped(release)", manual);
        Assert.Contains("UpdateManualCheckLogic.Plan(", manual);
        Assert.Contains("ShowUpdateDialogAsync(release, plan.WasSkipped)", manual);
    }

    [Fact]
    public void Avalonia_Dialog_ShowsSkippedHintAndClearsSkipOnUpdate()
    {
        var source = ReadServiceSource("UpdateService.Avalonia.cs");
        Assert.Contains("Update.SkippedHint", source);
        Assert.Contains("ClearSkippedVersion()", source);

        var selfUpdate = SliceMethod(
            source,
            "private async Task ShowSelfUpdateDialogAsync(",
            "private async Task ShowPrivilegedUpdateDialogAsync(");
        Assert.Contains("bool wasSkipped = false", selfUpdate);
        Assert.Contains("Update.SkippedHint", selfUpdate);
        Assert.Contains("ClearSkippedVersion()", selfUpdate);
    }

    [Fact]
    public void Avalonia_Service_HasClearSkippedVersion()
    {
        var source = ReadServiceSource("UpdateService.Avalonia.cs");
        Assert.Contains("public void ClearSkippedVersion()", source);
        Assert.Contains("settings.SkippedUpdateVersion = string.Empty;", source);
    }

    // ---- Локализация ----

    [Fact]
    public void Localization_HasSkippedHint()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        Assert.Contains("\"Update.SkippedHint\"", ru);

        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains("\"Update.SkippedHint\"", en);
    }
}
