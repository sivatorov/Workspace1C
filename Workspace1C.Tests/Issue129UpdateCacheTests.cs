using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #129, вторая часть исправления: мгновенное применение «Считать версию прошлой»
/// и переиспользование уже скачанного файла обновления.
/// <list type="bullet">
/// <item>событие <see cref="TraceFlags.SettingsSaved"/> поднимается при сохранении
/// флагов — главное окно и <c>UpdateService</c> пересчитывают версию/значок/доступность
/// без перезапуска;</item>
/// <item><see cref="UpdateAvailabilityLogic"/> — чистая переоценка кэшированного релиза
/// относительно эффективной версии;</item>
/// <item><see cref="UpdateDownloadCache"/> — переиспользование полного файла и перекачка
/// при несовпадении размера/ETag/даты;</item>
/// <item>source-гварды: обе платформы подписаны на событие, диалог не удаляет полный
/// файл, загрузчики сначала проверяют кэш.</item>
/// </list>
/// Тесты меняют глобальное состояние <see cref="TraceFlags"/> и git-провайдер, поэтому
/// изолированы коллекцией TraceFlagsState (последовательный прогон). Сеть не используется.
/// </summary>
[Collection("TraceFlagsState")]
public sealed class Issue129UpdateCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly Func<string?>? _previousGitProvider;

    public Issue129UpdateCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "w1c_issue129cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        TraceFlags.ConfigDirectoryOverride = _dir;
        TraceFlags.EnvReader = _ => null;
        TraceFlags.ResetCacheForTesting();
        _previousGitProvider = EffectiveVersionService.GitTagsProvider;
    }

    public void Dispose()
    {
        EffectiveVersionService.GitTagsProvider = _previousGitProvider;
        EffectiveVersionService.ResetGitCacheForTesting();
        TraceFlags.ConfigDirectoryOverride = null;
        TraceFlags.ResetCacheForTesting();
        try { Directory.Delete(_dir, true); }
        catch { /* временный каталог теста */ }
    }

    // --------------------------------------------------------------
    // Событие сохранения флагов (мгновенное применение без перезапуска)
    // --------------------------------------------------------------

    [Fact]
    public void SaveCategorySettings_RaisesSettingsSaved_WithPrevVersionFlag()
    {
        var events = new List<TraceSettingsSavedEventArgs>();
        void Handler(TraceSettingsSavedEventArgs args) => events.Add(args);

        TraceFlags.SettingsSaved += Handler;
        try
        {
            Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
            {
                [TraceFlags.PrevVersionFlag] = true
            }));
        }
        finally
        {
            TraceFlags.SettingsSaved -= Handler;
        }

        var args = Assert.Single(events);
        Assert.True(args.Contains(TraceFlags.PrevVersionFlag));
        Assert.True(args.Contains("cm_prev_version"), "Сравнение имён флагов регистронезависимо.");
        Assert.False(args.Contains(TraceFlags.ColumnsFlag));
        Assert.False(args.Contains(null));
    }

    [Fact]
    public void SaveCategorySettings_UnknownFlagOnly_DoesNotRaiseSettingsSaved()
    {
        var raised = 0;
        void Handler(TraceSettingsSavedEventArgs args) => raised++;

        TraceFlags.SettingsSaved += Handler;
        try
        {
            // Неизвестный ключ игнорируется — подписчикам нечего пересчитывать.
            Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
            {
                ["CM_NOT_A_REAL_FLAG"] = true
            }));
        }
        finally
        {
            TraceFlags.SettingsSaved -= Handler;
        }

        Assert.Equal(0, raised);
    }

    [Fact]
    public void PrevVersionToggle_ChangesDisplayedVersionAndIndicator_WithoutRestart()
    {
        var real = VersionInfo.Display();
        // Релиз равен РЕАЛЬНОЙ сборке — типичный случай из issue: проверка обновлений
        // уже нашла (или отвергла) именно его по реальной версии.
        var cachedRelease = new ReleaseInfo { TagName = "v" + real, AssetSize = 1234 };

        // 1) Флаг выключен: релиз не новее — предложения нет, значок «синхронизация».
        Assert.False(EffectiveVersionService.IsPrevVersionModeEnabled());
        var before = UpdateAvailabilityLogic.Reevaluate(
            cachedRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Null(before.Release);
        Assert.False(before.NeedsNetworkCheck);
        var syncState = FooterUpdateIndicatorState.Resolve(
            AppUpdateMode.NotifyOnly, hasRelease: before.Release is not null, isReady: false);
        Assert.True(syncState.ShowSyncIcon);
        Assert.False(syncState.ShowWarningIcon);

        // 2) Включаем галку: событие сохранения меняет эффективную версию СРАЗУ.
        EffectiveVersionService.GitTagsProvider = () => "v1.0.0.1";
        EffectiveVersionService.ResetGitCacheForTesting();
        Assert.True(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = true
        }));

        Assert.True(EffectiveVersionService.IsPrevVersionModeEnabled());
        Assert.Equal("1.0.0.1", EffectiveVersionService.CurrentForUpdates);

        var after = UpdateAvailabilityLogic.Reevaluate(
            cachedRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Same(cachedRelease, after.Release);
        Assert.False(after.NeedsNetworkCheck);

        // Значок «внимание» — обновление доступно (без перезапуска).
        var warningState = FooterUpdateIndicatorState.Resolve(
            AppUpdateMode.NotifyOnly, hasRelease: after.Release is not null, isReady: false);
        Assert.True(warningState.ShowWarningIcon);
        Assert.False(warningState.ShowSyncIcon);

        // Подпись версии показывает реальную сборку и эффективную (понижённую).
        var caption = EffectiveVersionService.FormatVersionForDisplay(
            real, EffectiveVersionService.CurrentForUpdates, "{0} ({1})");
        Assert.Contains(real, caption, StringComparison.Ordinal);
        Assert.Contains("1.0.0.1", caption, StringComparison.Ordinal);

        // 3) Выключаем галку — состояние возвращается без перезапуска.
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [TraceFlags.PrevVersionFlag] = false
        });
        var restored = UpdateAvailabilityLogic.Reevaluate(
            cachedRelease, EffectiveVersionService.CurrentForUpdates, skipped: false);
        Assert.Null(restored.Release);
        Assert.Equal(real, EffectiveVersionService.CurrentForUpdates);
    }

    [Fact]
    public void Reevaluate_NoCachedRelease_RequestsBackgroundCheck()
    {
        var evaluation = UpdateAvailabilityLogic.Reevaluate(null, "1.0.1.0", skipped: false);
        Assert.Null(evaluation.Release);
        Assert.True(evaluation.NeedsNetworkCheck);
    }

    [Fact]
    public void Reevaluate_SkippedRelease_IsNotOffered()
    {
        var release = new ReleaseInfo { TagName = "v1.0.1.10" };
        var evaluation = UpdateAvailabilityLogic.Reevaluate(
            release, "1.0.1.0", skipped: true);
        Assert.Null(evaluation.Release);
        Assert.False(evaluation.NeedsNetworkCheck);
    }

    // --------------------------------------------------------------
    // Кэш скачанного файла: размер / ETag / дата
    // --------------------------------------------------------------

    [Fact]
    public void IsComplete_RequiresExactKnownSize()
    {
        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);
        Assert.True(UpdateDownloadCache.IsComplete(path, 100));
        Assert.False(UpdateDownloadCache.IsComplete(path, 99));
        Assert.False(UpdateDownloadCache.IsComplete(path, 0), "Без эталона целостность не подтвердить.");
        Assert.False(UpdateDownloadCache.IsComplete(Path.Combine(_dir, "missing.bin"), 100));
        Assert.False(UpdateDownloadCache.IsComplete(null, 100));
    }

    [Fact]
    public void ResolveCachedFile_ReusesOnlyExactSizeAndVersionName()
    {
        WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);

        Assert.NotNull(UpdateDownloadCache.ResolveCachedFile(
            _dir, "Workspace1C.v1.0.1.62.new.exe", 100));
        // Другая версия — другой файл, не переиспользуется.
        Assert.Null(UpdateDownloadCache.ResolveCachedFile(
            _dir, "Workspace1C.v1.0.1.61.new.exe", 100));
        // Размер не совпал — нужна загрузка.
        Assert.Null(UpdateDownloadCache.ResolveCachedFile(
            _dir, "Workspace1C.v1.0.1.62.new.exe", 101));
    }

    [Fact]
    public void Decide_FileMissingOrWrongSize_RequiresDownload()
    {
        var missing = Path.Combine(_dir, "Workspace1C.v1.0.1.62.new.exe");
        Assert.Equal(UpdateCacheDecision.Download,
            UpdateDownloadCache.Decide(missing, 100, null, null, null));

        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 50);
        Assert.Equal(UpdateCacheDecision.Redownload,
            UpdateDownloadCache.Decide(path, 100, null, null, null));
    }

    [Fact]
    public void Decide_ExactSizeWithoutMarker_ReusesFile()
    {
        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);
        Assert.Equal(UpdateCacheDecision.Reuse,
            UpdateDownloadCache.Decide(path, 100, null, null, null));
    }

    [Fact]
    public void Decide_ETagMismatch_Redownloads_EmptyRemoteETag_Reuses()
    {
        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);
        var marker = new UpdateCacheMarker(100, "\"etag-old\"", null);

        Assert.Equal(UpdateCacheDecision.Redownload,
            UpdateDownloadCache.Decide(path, 100, marker, "\"etag-new\"", null));
        // Пустой ETag ответа (сбой зонда) файл не «наказывает».
        Assert.Equal(UpdateCacheDecision.Reuse,
            UpdateDownloadCache.Decide(path, 100, marker, null, null));
        Assert.Equal(UpdateCacheDecision.Reuse,
            UpdateDownloadCache.Decide(path, 100, marker, "\"etag-old\"", null));
    }

    [Fact]
    public void Decide_LastModifiedMismatch_Redownloads()
    {
        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);
        var marker = new UpdateCacheMarker(
            100, "\"etag\"", new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(UpdateCacheDecision.Reuse,
            UpdateDownloadCache.Decide(
                path, 100, marker, "\"etag\"", new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(UpdateCacheDecision.Redownload,
            UpdateDownloadCache.Decide(
                path, 100, marker, "\"etag\"", new DateTime(2026, 11, 13, 10, 0, 5, DateTimeKind.Utc)));
    }

    [Fact]
    public void Marker_RoundTrip_AndDeleteCachedFile_RemovesBothFiles()
    {
        var path = WriteFile("Workspace1C.v1.0.1.62.new.exe", 100);
        var modified = new DateTime(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc);

        UpdateDownloadCache.WriteReadyMarker(path, 100, "\"etag-1\"", modified);
        Assert.True(File.Exists(UpdateDownloadCache.BuildReadyMarkerPath(path)));

        var marker = UpdateDownloadCache.TryReadMarker(path);
        Assert.NotNull(marker);
        Assert.Equal(100, marker!.Value.Size);
        Assert.Equal("\"etag-1\"", marker.Value.ETag);
        Assert.Equal(modified, marker.Value.LastModifiedUtc);

        UpdateDownloadCache.DeleteCachedFile(path);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(UpdateDownloadCache.BuildReadyMarkerPath(path)));
    }

    [Fact]
    public void Marker_TryParse_RejectsCorruptText()
    {
        Assert.Null(UpdateCacheMarker.TryParse(null));
        Assert.Null(UpdateCacheMarker.TryParse(""));
        Assert.Null(UpdateCacheMarker.TryParse("etag=\"x\"\nlastModified=2026-11-13T10:00:00Z"));
        Assert.NotNull(UpdateCacheMarker.TryParse("size=42\netag=\"x\""));
    }

    // --------------------------------------------------------------
    // Source-гварды: обе платформы и политика удаления temp-файла
    // --------------------------------------------------------------

    [Theory]
    [InlineData("Workspace1C/Services/UpdateService.cs", "TryGetCachedUpdateFile")]
    [InlineData("Workspace1C/Services/UpdateService.Avalonia.cs", "UpdateDownloadCache.ResolveCachedFile")]
    public void UpdateServices_ReuseCachedFile_AndReevaluateAvailability(string relativePath, string cacheCall)
    {
        var source = ReadProjectFile(relativePath);
        Assert.Contains(cacheCall, source, StringComparison.Ordinal);
        Assert.Contains("public bool ReevaluateAvailability()", source, StringComparison.Ordinal);
        Assert.Contains("TraceFlags.SettingsSaved += OnTraceSettingsSaved;", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAvailabilityLogic.Reevaluate", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Workspace1C/Views/MainWindow.Updates.cs")]
    [InlineData("Workspace1C/Views/MainWindow.Avalonia.cs")]
    public void MainWindows_SubscribeToSettingsSaved_AndRefreshVersionAndIndicator(string relativePath)
    {
        var source = ReadProjectFile(relativePath);
        Assert.Contains("TraceFlags.SettingsSaved += OnTraceSettingsSaved;", source, StringComparison.Ordinal);
        Assert.Contains("RefreshFooterVersionText", source, StringComparison.Ordinal);
        Assert.Contains("EffectiveVersionService.FormatVersionForDisplay", source, StringComparison.Ordinal);
        Assert.Contains("OnTraceSettingsSaved", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateDialog_KeepsCompleteFile_AndDeletesOnlyIncompleteOrSkipped()
    {
        var source = ReadProjectFile("Workspace1C/Services/UpdateAvailableWindow.xaml.cs");
        // Полный файл при закрытии диалога больше не удаляется безусловно (issue #129).
        Assert.Contains("UpdateDownloadCache.IsComplete(_newExe, _release.AssetSize)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("try { File.Delete(_newExe); }", source, StringComparison.Ordinal);
        // «Пропустить версию» — единственный сценарий явной уборки кэша.
        Assert.Contains("UpdateDownloadCache.DeleteCachedFile(_service.GetUpdateDownloadTargetPath(_release));",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceFlags_RaisesSettingsSavedOutsideLock()
    {
        var source = ReadProjectFile("Workspace1C/Services/TraceFlags.cs");
        Assert.Contains("public static event Action<TraceSettingsSavedEventArgs>? SettingsSaved;",
            source, StringComparison.Ordinal);
        Assert.Contains("RaiseSettingsSaved(savedFlags);", source, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------
    // Вспомогательные методы
    // --------------------------------------------------------------

    private string WriteFile(string name, int size)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

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
}
