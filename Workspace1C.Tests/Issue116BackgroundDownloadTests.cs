using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 (v1.0.1.58): режим «Загружать с предложением установки» должен
/// действительно скачивать файл обновления в фоне при обнаружении новой версии
/// (и удалять старый скачанный файл), показывать в статус-баре отдельное состояние
/// «готов к установке», а в диалоге при готовом файле — кнопку «Установить» вместо
/// «Скачать». Остальные режимы («Обновлять при запуске», «Только уведомлять»,
/// «Проверять вручную») не меняются.
/// <para>
/// Служба обновления платформенно-условная (#if WINDOWS / #if LINUX), поэтому
/// проверка ведётся source-гвардами по исходникам (как в Issue116UpdateIntervalTests)
/// плюс функциональные тесты общего помощника очистки
/// <see cref="UpdateDownloadCleanup"/>.
/// </para>
/// </summary>
public sealed class Issue116BackgroundDownloadTests
{
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

    /// <summary>Вырезает текст ветки <c>case</c> из switch службы обновления.</summary>
    private static string ExtractSwitchCase(string source, string caseMarker, string? nextMarker)
    {
        var start = source.IndexOf(caseMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Ветка не найдена: {caseMarker}");
        var end = nextMarker is null
            ? source.Length
            : source.IndexOf(nextMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Конец ветки не найден: {nextMarker}");
        return source[start..end];
    }

    // ===================== Фоновая загрузка: WPF =====================

    [Fact]
    public void Wpf_DownloadAskInstall_TriggersBackgroundDownload()
    {
        // Ветвление фоновой проверки: режим «Загружать с предложением установки»
        // запоминает доступный релиз и сразу запускает фоновую загрузку файла.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        var caseBody = ExtractSwitchCase(
            source,
            "case AppUpdateMode.DownloadAskInstall:",
            "case AppUpdateMode.NotifyOnly:");
        Assert.Contains("SetAvailableRelease(release);", caseBody);
        Assert.Contains("StartBackgroundDownload(release);", caseBody);
        // Диалог больше не открывается сам из фоновой проверки этого режима.
        Assert.DoesNotContain("ShowUpdateDialog(release)", caseBody);
    }

    [Fact]
    public void Wpf_NotifyOnly_DoesNotDownloadInBackground()
    {
        // «Только уведомлять»: файл НЕ скачивается (и не в диалоге, и не в фоне).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        var caseBody = ExtractSwitchCase(
            source,
            "case AppUpdateMode.NotifyOnly:",
            "case AppUpdateMode.Manual:");
        Assert.DoesNotContain("StartBackgroundDownload", caseBody);
        Assert.DoesNotContain("DownloadNewExeCoreAsync", caseBody);
    }

    [Fact]
    public void Wpf_BackgroundDownload_DeletesStaleDownloadOnSuccess()
    {
        // После успешной фоновой загрузки предыдущий скачанный файл обновления
        // удаляется (issue #116: «если удалось — удалять старый файл обновления»).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        Assert.Contains("UpdateDownloadCleanup.RemoveStaleDownload(previousReadyPath, path);", source);
        // Готовый файл публикуется через состояние «готов к установке».
        Assert.Contains("SetReadyState(release, path);", source);
    }

    [Fact]
    public void Wpf_UpdateService_HasReadyStateMembers()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateService.cs"));
        Assert.Contains("public ReleaseInfo? ReadyRelease", source);
        Assert.Contains("public string? ReadyFilePath", source);
        Assert.Contains("public event Action? ReadyStateChanged", source);
        Assert.Contains("public bool IsReadyFor(ReleaseInfo? release)", source);
        // «Установить» применяет готовый файл по логике автообновления.
        Assert.Contains("public bool ApplyReadyInstall(ReleaseInfo release)", source);
        Assert.Contains("ApplyRestartNow(targetExe, newExe)", source);
        // Клик по значку готовности — сразу диалог установки, без сетевой проверки.
        Assert.Contains("public void ShowReadyInstallDialog()", source);
    }

    // ===================== Фоновая загрузка: Avalonia =====================

    [Fact]
    public void Avalonia_DownloadAskInstall_TriggersBackgroundDownload()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        var caseBody = ExtractSwitchCase(
            source,
            "case AppUpdateMode.DownloadAskInstall:",
            "case AppUpdateMode.NotifyOnly:");
        Assert.Contains("SetAvailableRelease(release);", caseBody);
        Assert.Contains("StartBackgroundDownload(release);", caseBody);
        // Диалог больше не открывается сам из фоновой проверки этого режима.
        Assert.DoesNotContain("ShowUpdateDialogAsync(release)", caseBody);
    }

    [Fact]
    public void Avalonia_NotifyOnly_DoesNotDownloadInBackground()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        var caseBody = ExtractSwitchCase(
            source,
            "case AppUpdateMode.NotifyOnly:",
            "case AppUpdateMode.Manual:");
        Assert.DoesNotContain("StartBackgroundDownload", caseBody);
        Assert.DoesNotContain("DownloadNewBinaryAsync", caseBody);
    }

    [Fact]
    public void Avalonia_BackgroundDownload_SkipsAppImageAndCleansStale()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        // Graceful-ограничение Linux: из AppImage самообновление невозможно —
        // файл не скачивается.
        Assert.Contains("IsRunningFromAppImage(target)", source);
        // После успешной загрузки предыдущий скачанный файл удаляется.
        Assert.Contains("UpdateDownloadCleanup.RemoveStaleDownload(previousReadyPath, path);", source);
        Assert.Contains("SetReadyState(release, path);", source);
    }

    [Fact]
    public void Avalonia_ReadyInstall_FlowsExist()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateService.Avalonia.cs"));
        Assert.Contains("public ReleaseInfo? ReadyRelease", source);
        Assert.Contains("public bool IsReadyFor(ReleaseInfo? release)", source);
        // Диалог установки готового файла: без подтверждения скачивания.
        Assert.Contains("private async Task ShowReadyInstallAsync(ReleaseInfo release, string target)", source);
        Assert.Contains("public Task OpenReadyInstallAsync()", source);
        // Повышение прав (pkexec) — только после явного согласия пользователя.
        Assert.Contains("Update.ReadyInstallPrompt", source);
    }

    // ===================== Диалог обновления: кнопка «Установить» =====================

    [Fact]
    public void Wpf_UpdateAvailableWindow_HasInstallButtonWhenReady()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("x:Name=\"InstallButton\"", xaml);
        Assert.Contains("{loc:Loc Update.Install}", xaml);

        var code = ReadProjectFile(Path.Combine(
            "Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        // Готовый файл: «Скачать» прячется, показывается «Установить».
        Assert.Contains("_service.IsReadyFor(release)", code);
        Assert.Contains("DownloadButton.Visibility = readyToInstall ? Visibility.Collapsed : Visibility.Visible;", code);
        Assert.Contains("private void OnInstallClick(object sender, RoutedEventArgs e)", code);
        // «Установить» идёт по существующей логике автообновления.
        Assert.Contains("_service.ApplyReadyInstall(_release)", code);
        // Скачанный в фоне файл не должен удаляться при закрытии окна без применения:
        // в готовом состоянии диалог ничего не скачивал (_newExe is null) — гвард не трогаем.
        Assert.Contains("if (!_applyScheduled && _newExe is not null)", code);
    }

    // ===================== Значок готовности в статус-баре =====================

    [Fact]
    public void Wpf_Footer_HasReadyStateIcon()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Зелёная галочка: состояние «готов к установке» различимо от «доступно
        // обновление» (акцентный значок «внимание») и от «проверить обновления»
        // (синие стрелки синхронизации).
        Assert.Contains("x:Name=\"FooterUpdateReadyIcon\"", xaml);
        Assert.Contains("Kind=\"Check\"", xaml);
        Assert.Contains("SuccessBrush", xaml);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("FooterUpdateReadyIcon.Visibility", code);
        Assert.Contains("updateService.ReadyStateChanged", code);
        Assert.Contains("Main.Footer.UpdateReadyTooltip", code);
        Assert.Contains("updateService.ShowReadyInstallDialog()", code);
    }

    [Fact]
    public void Avalonia_Footer_HasReadyStateIcon()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("_footerUpdateReadyIcon = IconHelper.MakeIcon(\"IconCheck\"", code);
        // v1.0.1.61: состояние считает общий хелпер, видимость не зависит от режима.
        Assert.Contains("_footerUpdateReadyIcon.IsVisible = state.ShowReadyIcon", code);
        Assert.Contains("FooterUpdateIndicatorState.Resolve", code);
        Assert.Contains("updateService.ReadyStateChanged", code);
        Assert.Contains("Main.Footer.UpdateReadyTooltip", code);
        Assert.Contains("updateService.OpenReadyInstallAsync()", code);
    }

    // ===================== Локализация =====================

    [Fact]
    public void Localization_HasInstallAndReadyKeys()
    {
        var repoRoot = FindRepoRoot();
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                repoRoot, "Workspace1C", "Localization", "Languages", lang + ".json"));
            Assert.Contains("\"Update.Install\"", json);
            Assert.Contains("\"Update.ReadyInstallPrompt\"", json);
            Assert.Contains("\"Main.Footer.UpdateReadyTooltip\"", json);
        }
    }

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

    // ===================== Очистка устаревших скачанных файлов =====================

    [Fact]
    public void RemoveStaleDownload_DeletesPreviousFile_KeepsNew()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cm-issue116-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var oldFile = Path.Combine(temp, "Workspace1C.1.0.1.57.new.exe");
            var newFile = Path.Combine(temp, "Workspace1C.1.0.1.58.new.exe");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(newFile, "new");

            UpdateDownloadCleanup.RemoveStaleDownload(oldFile, newFile);

            Assert.False(File.Exists(oldFile), "Старый скачанный файл должен быть удалён.");
            Assert.True(File.Exists(newFile), "Новый скачанный файл должен остаться.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void RemoveStaleDownload_SamePath_DoesNotDelete()
    {
        // Повторная загрузка той же версии: путь совпадает — файл не удаляется.
        var temp = Path.Combine(Path.GetTempPath(), "cm-issue116-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var file = Path.Combine(temp, "Workspace1C.1.0.1.58.new.exe");
            File.WriteAllText(file, "same");

            UpdateDownloadCleanup.RemoveStaleDownload(file, file);

            Assert.True(File.Exists(file));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void RemoveStaleDownload_NullOrEmptyPrevious_NoThrow()
    {
        // Готового файла ещё не было — очистка ничего не делает и не бросает.
        UpdateDownloadCleanup.RemoveStaleDownload(null, "any");
        UpdateDownloadCleanup.RemoveStaleDownload(string.Empty, "any");
        UpdateDownloadCleanup.RemoveStaleDownload("   ", "any");
    }

    [Fact]
    public void RemoveStaleDownload_MissingPreviousFile_NoThrow()
    {
        // Файл уже удалён (например, уборщиком temp) — отсутствие не ошибка.
        var missing = Path.Combine(
            Path.GetTempPath(), "cm-issue116-tests", Guid.NewGuid().ToString("N") + ".missing");
        UpdateDownloadCleanup.RemoveStaleDownload(missing, "any");
        Assert.False(File.Exists(missing));
    }
}
