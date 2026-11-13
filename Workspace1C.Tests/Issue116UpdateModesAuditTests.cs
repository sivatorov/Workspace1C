using System;
using System.IO;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 «Обновление вручную» — аудит соответствия четырёх режимов обновления
/// требованиям ветки комментариев (последнее подтверждение @7OH «Теперь нормально»,
/// v1.0.1.61). Задача — <b>верификация</b>: тесты фиксируют текущее поведение,
/// чтобы дальнейшие правки не развели WPF и Avalonia и не сломали подтверждённые
/// сценарии.
/// <list type="bullet">
/// <item>все четыре режима <see cref="AppUpdateMode"/> обрабатываются в фоновой
/// проверке обеих платформ;</item>
/// <item><c>InstallOnStartup</c> скачивает и устанавливает при запуске,
/// <c>DownloadAskInstall</c> качает в фоне и ставит готовый файл кнопкой
/// «Установить», <c>NotifyOnly</c> не скачивает, <c>Manual</c> не запускает
/// фоновую проверку при старте;</item>
/// <item>значок в подвале виден во всех режимах и считается общим хелпером
/// <see cref="FooterUpdateIndicatorState"/>;</item>
/// <item>клик по версии — «Что нового» (840×630, прокрутка, «Открыть на GitHub»),
/// клик по значку — ручная проверка/установка готового файла;</item>
/// <item>периодичность проверки — отдельной строкой, без повторных уведомлений
/// о том же релизе; «Пропустить эту версию» и значок слева от версии на месте.</item>
/// </list>
/// </summary>
public sealed class Issue116UpdateModesAuditTests
{
    private const string WpfService = "Workspace1C/Services/UpdateService.cs";
    private const string AvaloniaService = "Workspace1C/Services/UpdateService.Avalonia.cs";

    // ===================== Инфраструктура =====================

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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Вырезает ветку <c>case</c> из switch службы обновления.</summary>
    private static string ExtractCase(string source, string caseMarker, string? nextMarker)
    {
        var start = source.IndexOf(caseMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Ветка не найдена: {caseMarker}");
        var end = nextMarker is null
            ? source.Length
            : source.IndexOf(nextMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Конец ветки не найден: {nextMarker}");
        return source[start..end];
    }

    /// <summary>Возвращает фрагмент исходника сразу после маркера (для проверки условий).</summary>
    private static string TextAfter(string source, string marker, int length)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Маркер не найден: {marker}");
        var start = index + marker.Length;
        var take = Math.Min(length, source.Length - start);
        return source.Substring(start, take);
    }

    // ===================== 1. Четыре режима =====================

    [Fact]
    public void AppUpdateMode_HasExactlyFourModes()
    {
        var values = (AppUpdateMode[])Enum.GetValues(typeof(AppUpdateMode));
        Assert.Equal(4, values.Length);
        Assert.Contains(AppUpdateMode.InstallOnStartup, values);
        Assert.Contains(AppUpdateMode.DownloadAskInstall, values);
        Assert.Contains(AppUpdateMode.NotifyOnly, values);
        Assert.Contains(AppUpdateMode.Manual, values);
    }

    [Theory]
    [InlineData(WpfService, "case AppUpdateMode.InstallOnStartup:", "case AppUpdateMode.DownloadAskInstall:")]
    [InlineData(WpfService, "case AppUpdateMode.DownloadAskInstall:", "case AppUpdateMode.NotifyOnly:")]
    [InlineData(WpfService, "case AppUpdateMode.NotifyOnly:", "case AppUpdateMode.Manual:")]
    [InlineData(WpfService, "case AppUpdateMode.Manual:", null)]
    [InlineData(AvaloniaService, "case AppUpdateMode.InstallOnStartup:", "case AppUpdateMode.DownloadAskInstall:")]
    [InlineData(AvaloniaService, "case AppUpdateMode.DownloadAskInstall:", "case AppUpdateMode.NotifyOnly:")]
    [InlineData(AvaloniaService, "case AppUpdateMode.NotifyOnly:", "case AppUpdateMode.Manual:")]
    [InlineData(AvaloniaService, "case AppUpdateMode.Manual:", null)]
    public void BackgroundCheck_HandlesEveryMode_OnBothPlatforms(
        string file, string caseMarker, string? nextMarker)
    {
        // Все четыре режима должны иметь собственную ветку в CheckForUpdatesAsync.
        var body = ExtractCase(ReadProjectFile(file), caseMarker, nextMarker);
        Assert.Contains("SetAvailableRelease", body);
    }

    // ===================== 2. Поведение режимов =====================

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void InstallOnStartup_DownloadsAndInstallsAtStartup(string file)
    {
        var body = ExtractCase(
            ReadProjectFile(file),
            "case AppUpdateMode.InstallOnStartup:",
            "case AppUpdateMode.DownloadAskInstall:");

        // Молчаливая установка тем же путём, что и раньше, + уведомление о версии.
        Assert.Contains("DownloadAndInstallAutoAsync(release)", body);
        Assert.Contains("NotifyUpdateAvailable(release)", body);
        // Фоновая загрузка «в стол» здесь не нужна — установка идёт сразу.
        Assert.DoesNotContain("StartBackgroundDownload", body);
    }

    [Theory]
    [InlineData(WpfService, "ShowUpdateDialog(release)")]
    [InlineData(AvaloniaService, "ShowUpdateDialogAsync(release)")]
    public void DownloadAskInstall_DownloadsInBackgroundWithoutAutoInstall(string file, string dialogCall)
    {
        var body = ExtractCase(
            ReadProjectFile(file),
            "case AppUpdateMode.DownloadAskInstall:",
            "case AppUpdateMode.NotifyOnly:");

        // Файл скачивается сразу при обнаружении версии, установка не применяется молча.
        Assert.Contains("SetAvailableRelease(release);", body);
        Assert.Contains("StartBackgroundDownload(release);", body);
        Assert.DoesNotContain("DownloadAndInstallAutoAsync", body);
        // Диалог из фоновой проверки не открывается — его открывает клик по значку.
        Assert.DoesNotContain(dialogCall, body);
    }

    [Theory]
    [InlineData(WpfService, "DownloadNewExeCoreAsync")]
    [InlineData(AvaloniaService, "DownloadNewBinaryAsync")]
    public void NotifyOnly_DoesNotDownloadAnything(string file, string downloadMethod)
    {
        var body = ExtractCase(
            ReadProjectFile(file),
            "case AppUpdateMode.NotifyOnly:",
            "case AppUpdateMode.Manual:");

        Assert.Contains("NotifyUpdateAvailable(release)", body);
        Assert.DoesNotContain("StartBackgroundDownload", body);
        Assert.DoesNotContain(downloadMethod, body);
        Assert.DoesNotContain("DownloadAndInstallAutoAsync", body);
    }

    [Theory]
    [InlineData(WpfService, "ShowUpdateDialog(release)")]
    [InlineData(AvaloniaService, "ShowUpdateDialogAsync(release)")]
    public void Manual_DoesNotDownloadOrShowDialog(string file, string dialogCall)
    {
        // Ветка Manual короткая; ограничиваем фрагмент, чтобы не захватить
        // последующий код ручной проверки (там диалог как раз вызывается).
        var body = TextAfter(ReadProjectFile(file), "case AppUpdateMode.Manual:", 700);

        // На случай внешнего вызова: только фиксируем доступный релиз.
        Assert.Contains("SetAvailableRelease(release);", body);
        Assert.DoesNotContain("StartBackgroundDownload", body);
        Assert.DoesNotContain("DownloadAndInstallAutoAsync", body);
        Assert.DoesNotContain(dialogCall, body);
    }

    // ===================== 3. Manual: нет фоновой проверки при старте =====================

    [Theory]
    [InlineData("Workspace1C/App.xaml.cs")]
    [InlineData("Workspace1C/App.axaml.cs")]
    public void Manual_Startup_SkipsBackgroundCheck(string appFile)
    {
        // При старте режим применяется всегда, а фоновая проверка — только не в Manual.
        var source = ReadProjectFile(appFile);
        Assert.Contains("ApplyUpdateMode(updateMode)", source);

        var guard = TextAfter(source, "if (updateMode != AppUpdateMode.Manual)", 240);
        Assert.Contains("CheckForUpdatesInBackground(updateService);", guard);
    }

    [Theory]
    [InlineData(WpfService)]
    [InlineData(AvaloniaService)]
    public void Manual_DisablesPeriodicTimer_AndRepeatsAreSuppressed(string file)
    {
        var source = ReadProjectFile(file);

        // Таймер периодической проверки не создаётся в Manual и при нулевом интервале.
        Assert.Contains("Mode == AppUpdateMode.Manual", source);
        Assert.Contains("_intervalMinutes <= 0", source);

        // Повторное уведомление о том же релизе подавляется: пока AvailableRelease
        // не обработан, периодическая проверка не запускается.
        var periodic = TextAfter(source, "private async void RunPeriodicCheck()", 200);
        Assert.Contains("Mode == AppUpdateMode.Manual", periodic);
        Assert.Contains("AvailableRelease is not null", periodic);
    }

    // ===================== 4. Значок в подвале =====================

    [Theory]
    [InlineData(AppUpdateMode.InstallOnStartup)]
    [InlineData(AppUpdateMode.DownloadAskInstall)]
    [InlineData(AppUpdateMode.NotifyOnly)]
    [InlineData(AppUpdateMode.Manual)]
    public void FooterIndicator_IsVisibleInEveryMode(AppUpdateMode mode)
    {
        foreach (var (hasRelease, isReady) in new[] { (false, false), (true, false), (false, true) })
        {
            var state = FooterUpdateIndicatorState.Resolve(mode, hasRelease, isReady);
            // v1.0.1.61: значок доступен во всех режимах — ручная проверка всегда возможна.
            Assert.True(state.IsVisible, $"Значок скрыт в режиме {mode}");
            // Одновременно показывается ровно один глиф.
            var glyphs = (state.ShowSyncIcon ? 1 : 0)
                       + (state.ShowWarningIcon ? 1 : 0)
                       + (state.ShowReadyIcon ? 1 : 0);
            Assert.Equal(1, glyphs);
        }
    }

    [Fact]
    public void FooterIndicator_StatesMatchRequirements()
    {
        // Синие стрелки — релиза нет и файл не готов (ручная проверка).
        var sync = FooterUpdateIndicatorState.Resolve(AppUpdateMode.Manual, false, false);
        Assert.True(sync.ShowSyncIcon);

        // Акцентный «внимание» — есть доступный релиз.
        var warn = FooterUpdateIndicatorState.Resolve(AppUpdateMode.NotifyOnly, true, false);
        Assert.True(warn.ShowWarningIcon);

        // Зелёная галочка — файл скачан и готов к установке.
        var ready = FooterUpdateIndicatorState.Resolve(AppUpdateMode.DownloadAskInstall, true, true);
        Assert.True(ready.ShowReadyIcon);
        Assert.False(ready.ShowWarningIcon);
    }

    [Fact]
    public void FooterIndicator_IsUsedByBothPlatforms()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/MainWindow.Updates.cs");
        Assert.Contains("FooterUpdateIndicatorState.Resolve(updateService.Mode, hasRelease, isReady)", wpf);
        Assert.Contains("FooterUpdateIcon.Visibility", wpf);
        Assert.Contains("FooterUpdateSyncIcon.Visibility", wpf);
        Assert.Contains("FooterUpdateWarnIcon.Visibility", wpf);
        Assert.Contains("FooterUpdateReadyIcon.Visibility", wpf);
        Assert.Contains("AvailableReleaseChanged", wpf);
        Assert.Contains("ReadyStateChanged", wpf);

        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.cs");
        Assert.Contains("FooterUpdateIndicatorState.Resolve(updateService.Mode, hasRelease, isReady)", avalonia);
        Assert.Contains("_footerUpdateIcon.IsVisible = state.IsVisible", avalonia);
        Assert.Contains("_footerUpdateSyncIcon.IsVisible = state.ShowSyncIcon", avalonia);
        Assert.Contains("_footerUpdateWarningIcon.IsVisible = state.ShowWarningIcon", avalonia);
        Assert.Contains("_footerUpdateReadyIcon.IsVisible = state.ShowReadyIcon", avalonia);
        Assert.Contains("AvailableReleaseChanged", avalonia);
        Assert.Contains("ReadyStateChanged", avalonia);
    }

    [Fact]
    public void FooterIcon_IsBlueForManualCheck_AndAccentForAvailableUpdate()
    {
        // WPF: стрелки и «внимание» — AccentBrush, готовность — SuccessBrush.
        var xaml = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        Assert.Contains("FooterUpdateSyncIcon", xaml);
        Assert.Contains("Foreground=\"{DynamicResource AccentBrush}\"", xaml);
        Assert.Contains("FooterUpdateReadyIcon", xaml);
        Assert.Contains("Foreground=\"{DynamicResource SuccessBrush}\"", xaml);

        // Avalonia: те же цвета через спецификации темы.
        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.cs");
        Assert.Contains("IconHelper.MakeIcon(\"IconSync\", UiMetrics.Scaled(15), \"@AccentBrush\")", avalonia);
        Assert.Contains("IconHelper.MakeIcon(\"IconWarning\", UiMetrics.Scaled(16), \"@AccentBrush\")", avalonia);
        Assert.Contains("IconHelper.MakeIcon(\"IconCheck\", UiMetrics.Scaled(16), \"@SuccessBrush\")", avalonia);
    }

    // ===================== 5. Клики: версия и значок =====================

    [Fact]
    public void Footer_VersionClickOpensWhatsNew_IconClickChecksOrInstalls_Wpf()
    {
        var code = ReadProjectFile("Workspace1C/Views/MainWindow.Updates.cs");

        var whatsNew = TextAfter(code, "private void OnFooterVersionClick", 400);
        Assert.Contains("new WhatsNewWindow(settings)", whatsNew);

        var iconClick = TextAfter(code, "private async void OnFooterUpdateIconClick", 700);
        Assert.Contains("updateService.IsReadyFor(updateService.ReadyRelease)", iconClick);
        Assert.Contains("updateService.ShowReadyInstallDialog()", iconClick);
        Assert.Contains("await updateService.CheckForUpdatesManualAsync()", iconClick);

        // Значок и версия подключены в разметке.
        var xaml = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        Assert.Contains("MouseLeftButtonUp=\"OnFooterUpdateIconClick\"", xaml);
        Assert.Contains("FooterVersionText", xaml);
    }

    [Fact]
    public void Footer_VersionClickOpensWhatsNew_IconClickChecksOrInstalls_Avalonia()
    {
        var code = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.cs");

        var whatsNew = TextAfter(code, "private void ShowWhatsNewFromFooter()", 400);
        Assert.Contains("new WhatsNewWindow(settings)", whatsNew);

        var check = TextAfter(code, "private async System.Threading.Tasks.Task RunFooterCheckUpdatesAsync()", 700);
        Assert.Contains("updateService.OpenReadyInstallAsync()", check);
        Assert.Contains("CheckForUpdatesManualAsync()", check);

        Assert.Contains("_footerVersionText.PointerReleased += (_, _) => ShowWhatsNewFromFooter()", code);
        Assert.Contains("_footerUpdateIcon.PointerReleased += (_, _) => _ = RunFooterCheckUpdatesAsync()", code);
    }

    // ===================== 6. «Что нового» =====================

    [Fact]
    public void WhatsNew_HasRequiredSizeWheelScrollAndGithubButton_Wpf()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/WhatsNewWindow.xaml");
        Assert.Contains("Height=\"630\" Width=\"840\"", xaml);
        Assert.Contains("WhatsNew.OpenChangelog", xaml);

        // Один скролл-контейнер без вложенных перехватчиков колеса.
        var code = ReadProjectFile("Workspace1C/Views/WhatsNewWindow.xaml.cs");
        Assert.Contains("new ScrollViewer", code);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", code);
        Assert.Contains("FlowDocumentScrollViewer", code);
        // Непустое содержимое: GitHub-релиз → локальный CHANGELOG → заглушка.
        Assert.Contains("GetGithubReleaseNotesAsync", code);
        Assert.Contains("TryGetChangelogSection", code);
        Assert.Contains("WhatsNew.NotesUnavailable", code);
    }

    [Fact]
    public void WhatsNew_HasRequiredSizeWheelScrollAndGithubButton_Avalonia()
    {
        var code = ReadProjectFile("Workspace1C/Views/WhatsNewWindow.Avalonia.cs");
        Assert.Contains("Width = 840;", code);
        Assert.Contains("Height = 630;", code);
        Assert.Contains("new ScrollViewer", code);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", code);
        Assert.Contains("WhatsNew.OpenChangelog", code);
        Assert.Contains("GetGithubReleaseNotesAsync", code);
        Assert.Contains("TryGetChangelogSection", code);
        Assert.Contains("WhatsNew.NotesUnavailable", code);
    }

    [Theory]
    [InlineData("ru", "Открыть на GitHub")]
    [InlineData("en", "Open on GitHub")]
    public void WhatsNew_ButtonTextIsShortGithubLabel(string lang, string expected)
    {
        var json = ReadProjectFile($"Workspace1C/Localization/Languages/{lang}.json");
        Assert.Contains($"\"WhatsNew.OpenChangelog\": \"{expected}\"", json);
    }

    // ===================== 7. Периодичность и пропуск версии =====================

    [Fact]
    public void UpdateInterval_IsSeparateSettingsRow_OnBothPlatforms()
    {
        // WPF: отдельный StackPanel под строкой режима обновления.
        var xaml = ReadProjectFile("Workspace1C/Views/SettingsWindow.xaml");
        var modeRow = xaml.IndexOf("x:Name=\"UpdateModeComboBox\"", StringComparison.Ordinal);
        var intervalRow = xaml.IndexOf("x:Name=\"UpdateIntervalValueBox\"", StringComparison.Ordinal);
        Assert.True(modeRow > 0 && intervalRow > 0, "Элементы интервала/режима не найдены в SettingsWindow.xaml");
        Assert.True(intervalRow > modeRow, "Интервал должен идти отдельной строкой после режима обновления");
        Assert.Contains("UpdateIntervalUnitCombo", xaml);

        // Avalonia: отдельный ряд вертикальной компоновки.
        var avalonia = ReadProjectFile("Workspace1C/Views/SettingsWindow.Avalonia.cs");
        Assert.Contains("var updateIntervalRow = new StackPanel", avalonia);
        Assert.Contains("settingsBehavior.Children.Add(updateModeRow);", avalonia);
        Assert.Contains("settingsBehavior.Children.Add(updateIntervalRow);", avalonia);
        Assert.Contains("updateIntervalValueBox", avalonia);
        Assert.Contains("updateIntervalUnitCombo", avalonia);
    }

    [Fact]
    public void SkipVersion_ButtonAndIconPlacementAreIntact()
    {
        // WPF: кнопка пропуска в диалоге.
        var dialog = ReadProjectFile("Workspace1C/Services/UpdateAvailableWindow.xaml");
        Assert.Contains("x:Name=\"SkipVersionButton\"", dialog);
        Assert.Contains("Update.SkipVersion", dialog);

        // Значок слева от версии — на обеих платформах.
        var xaml = ReadProjectFile("Workspace1C/Views/MainWindow.xaml");
        Assert.True(
            xaml.IndexOf("x:Name=\"FooterUpdateIcon\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"FooterVersionText\"", StringComparison.Ordinal),
            "WPF: значок обновления должен стоять слева от версии");

        var avalonia = ReadProjectFile("Workspace1C/Views/MainWindow.Avalonia.cs");
        Assert.True(
            avalonia.IndexOf("_footerUpdateIcon = new StackPanel", StringComparison.Ordinal)
            < avalonia.IndexOf("_footerVersionText = new TextBlock", StringComparison.Ordinal),
            "Avalonia: значок обновления должен стоять слева от версии");
    }
}
