using System;
using System.IO;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 «Обновление вручную»: режим обновления приложения.
/// <list type="bullet">
/// <item>чистые тесты <see cref="AppUpdateModeHelper"/>: разбор строк и миграция из
/// устаревших флагов CheckForUpdatesOnStartup/AutoUpdateEnabled (у выключивших
/// автообновление режим НЕ должен стать «обновлять при запуске»);</item>
/// <item>source-scan: все четыре режима обрабатываются в UpdateService (обе платформы),
/// старт вызывает ApplyUpdateMode, в подвале главного окна есть версия и ссылка
/// проверки обновлений.</item>
/// </list>
/// </summary>
public sealed class Issue116UpdateModeTests
{
    // ===================== AppUpdateModeHelper.Parse =====================

    [Theory]
    [InlineData("InstallOnStartup", AppUpdateMode.InstallOnStartup)]
    [InlineData("DownloadAskInstall", AppUpdateMode.DownloadAskInstall)]
    [InlineData("NotifyOnly", AppUpdateMode.NotifyOnly)]
    [InlineData("Manual", AppUpdateMode.Manual)]
    [InlineData("manual", AppUpdateMode.Manual)]                       // регистр не важен
    [InlineData(" NotifyOnly ", AppUpdateMode.NotifyOnly)]             // пробелы обрезаются
    [InlineData("", AppUpdateMode.InstallOnStartup)]                   // пусто — по умолчанию
    [InlineData(null, AppUpdateMode.InstallOnStartup)]
    [InlineData("garbage", AppUpdateMode.InstallOnStartup)]            // неизвестное — по умолчанию
    public void Parse_ReturnsExpectedMode(string? value, AppUpdateMode expected)
    {
        Assert.Equal(expected, AppUpdateModeHelper.Parse(value));
    }

    [Theory]
    [InlineData(AppUpdateMode.InstallOnStartup, "InstallOnStartup")]
    [InlineData(AppUpdateMode.DownloadAskInstall, "DownloadAskInstall")]
    [InlineData(AppUpdateMode.NotifyOnly, "NotifyOnly")]
    [InlineData(AppUpdateMode.Manual, "Manual")]
    public void ToStringValue_ProducesCanonicalStrings(AppUpdateMode mode, string expected)
    {
        Assert.Equal(expected, AppUpdateModeHelper.ToStringValue(mode));
    }

    // ===================== Миграция из старых флагов =====================

    [Fact]
    public void Migrate_AutoUpdateOn_CheckOn_Startup_BecomesInstallOnStartup()
    {
        // Оба флага включены (старое поведение по умолчанию) — режим «обновлять при запуске».
        Assert.Equal(AppUpdateMode.InstallOnStartup,
            AppUpdateModeHelper.MigrateFromLegacyFlags(checkForUpdatesOnStartup: true, autoUpdateEnabled: true));
    }

    [Fact]
    public void Migrate_AutoUpdateOff_BecomesDownloadAskInstall_NotInstallOnStartup()
    {
        // КРИТИЧНО (issue #116): у выключивших автообновление режим НЕ должен стать
        // «обновлять при запуске» — иначе молчаливая установка с перезапуском вернётся.
        Assert.Equal(AppUpdateMode.DownloadAskInstall,
            AppUpdateModeHelper.MigrateFromLegacyFlags(checkForUpdatesOnStartup: true, autoUpdateEnabled: false));
    }

    [Fact]
    public void Migrate_CheckOff_BecomesManual()
    {
        Assert.Equal(AppUpdateMode.Manual,
            AppUpdateModeHelper.MigrateFromLegacyFlags(checkForUpdatesOnStartup: false, autoUpdateEnabled: true));
        Assert.Equal(AppUpdateMode.Manual,
            AppUpdateModeHelper.MigrateFromLegacyFlags(checkForUpdatesOnStartup: false, autoUpdateEnabled: false));
    }

    [Fact]
    public void ToLegacyFlags_AreConsistentWithMode()
    {
        // Обратная синхронизация: AutoUpdateEnabled только для InstallOnStartup,
        // CheckForUpdatesOnStartup — для всех, кроме Manual.
        Assert.True(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.InstallOnStartup).CheckForUpdatesOnStartup);
        Assert.True(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.InstallOnStartup).AutoUpdateEnabled);
        Assert.True(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.DownloadAskInstall).CheckForUpdatesOnStartup);
        Assert.False(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.DownloadAskInstall).AutoUpdateEnabled);
        Assert.True(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.NotifyOnly).CheckForUpdatesOnStartup);
        Assert.False(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.NotifyOnly).AutoUpdateEnabled);
        Assert.False(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.Manual).CheckForUpdatesOnStartup);
        Assert.False(AppUpdateModeHelper.ToLegacyFlags(AppUpdateMode.Manual).AutoUpdateEnabled);
    }

    // ===================== AppSettings: миграция и синхронизация =====================

    [Fact]
    public void AppSettings_NormalizeUpdateMode_MigratesFromLegacyFlags()
    {
        var settings = new AppSettings { CheckForUpdatesOnStartup = true, AutoUpdateEnabled = false };
        settings.NormalizeUpdateMode();
        Assert.Equal(AppUpdateMode.DownloadAskInstall, settings.GetUpdateMode());
        Assert.Equal("DownloadAskInstall", settings.UpdateMode);
        // Устаревшие флаги синхронизированы с режимом.
        Assert.True(settings.CheckForUpdatesOnStartup);
        Assert.False(settings.AutoUpdateEnabled);
    }

    [Fact]
    public void AppSettings_NormalizeUpdateMode_ExistingValueIsNotOverwrittenByFlags()
    {
        // Уже заданный UpdateMode не перебивается старыми флагами.
        var settings = new AppSettings
        {
            UpdateMode = "Manual",
            CheckForUpdatesOnStartup = true,
            AutoUpdateEnabled = true
        };
        settings.NormalizeUpdateMode();
        Assert.Equal(AppUpdateMode.Manual, settings.GetUpdateMode());
        // Флаги приведены в соответствие режиму.
        Assert.False(settings.CheckForUpdatesOnStartup);
        Assert.False(settings.AutoUpdateEnabled);
    }

    [Fact]
    public void AppSettings_SetUpdateMode_SyncsLegacyFlags()
    {
        var settings = new AppSettings();
        settings.SetUpdateMode(AppUpdateMode.NotifyOnly);
        Assert.Equal("NotifyOnly", settings.UpdateMode);
        Assert.True(settings.CheckForUpdatesOnStartup);
        Assert.False(settings.AutoUpdateEnabled);

        settings.SetUpdateMode(AppUpdateMode.Manual);
        Assert.False(settings.CheckForUpdatesOnStartup);
        Assert.False(settings.AutoUpdateEnabled);
    }

    // ===================== Source-scan =====================

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

    [Theory]
    [InlineData("Workspace1C", "Services", "UpdateService.cs")]
    [InlineData("Workspace1C", "Services", "UpdateService.Avalonia.cs")]
    public void UpdateService_HandlesAllFourModes(string parts1, string parts2, string fileName)
    {
        // Все четыре режима обрабатываются в CheckForUpdatesAsync (source-scan).
        var source = ReadProjectFile(Path.Combine(parts1, parts2, fileName));
        Assert.Contains("AppUpdateMode.InstallOnStartup", source);
        Assert.Contains("AppUpdateMode.DownloadAskInstall", source);
        Assert.Contains("AppUpdateMode.NotifyOnly", source);
        Assert.Contains("AppUpdateMode.Manual", source);
        // Состояние «доступна новая версия» для ссылки «Обновить» в подвале.
        Assert.Contains("AvailableRelease", source);
        Assert.Contains("AvailableReleaseChanged", source);
    }

    [Fact]
    public void App_Startup_AppliesUpdateMode()
    {
        // Старт приложения задаёт режим через ApplyUpdateMode (обе платформы).
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "App.xaml.cs"));
        Assert.Contains("ApplyUpdateMode", wpf);
        Assert.DoesNotContain("AutoUpdateEnabled = settings.AutoUpdateEnabled", wpf);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "App.axaml.cs"));
        Assert.Contains("ApplyUpdateMode", avalonia);
    }

    [Fact]
    public void MainWindow_Footer_HasVersionAndCheckUpdatesLink()
    {
        // WPF: колонка 1 строки состояния — версия (VersionInfo) и ссылка проверки.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("FooterVersionText", xaml);
        Assert.Contains("FooterUpdateLink", xaml);
        Assert.Contains("FooterCheckUpdatesLink", xaml);
        Assert.Contains("Main.Footer.CheckUpdates", xaml);
        Assert.Contains("Main.Footer.WhatsNewTooltip", xaml);

        // Avalonia: те же элементы кодом в BuildStatusBar.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("_footerVersionText", avalonia);
        Assert.Contains("VersionInfo.Display()", avalonia);
        Assert.Contains("Main.Footer.CheckUpdates", avalonia);
        Assert.Contains("Main.Footer.WhatsNewTooltip", avalonia);
        Assert.Contains("RefreshFooterUpdateState", avalonia);
    }

    [Fact]
    public void SettingsWindow_HasUpdateModeComboBox()
    {
        // WPF: два чекбокса заменены комбобоксом режима обновления.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("UpdateModeComboBox", xaml);
        Assert.Contains("Settings.General.UpdateMode.InstallOnStartup", xaml);
        Assert.Contains("Settings.General.UpdateMode.NotifyOnly", xaml);
        Assert.Contains("Settings.General.UpdateMode.Manual", xaml);
        Assert.DoesNotContain("CheckForUpdatesOnStartupCheck", xaml);
        Assert.DoesNotContain("AutoUpdateEnabledCheck", xaml);

        // Avalonia: тот же комбобокс в коде.
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("updateModeCombo", avalonia);
        Assert.Contains("Settings.General.UpdateMode.NotifyOnly", avalonia);
    }

    [Fact]
    public void Localization_HasUpdateModeKeys()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                FindRepoRoot(), "Workspace1C", "Localization", "Languages", lang + ".json"));
            foreach (var key in new[]
                     {
                         "Settings.General.UpdateMode",
                         "Settings.General.UpdateMode.InstallOnStartup",
                         "Settings.General.UpdateMode.DownloadAskInstall",
                         "Settings.General.UpdateMode.NotifyOnly",
                         "Settings.General.UpdateMode.Manual",
                         "Main.Footer.WhatsNewTooltip",
                         "Main.Footer.CheckUpdates",
                         "Main.Footer.Update"
                     })
            {
                Assert.Contains($"\"{key}\"", json);
            }
        }
    }
}
