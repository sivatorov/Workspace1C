using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты настроек логирования по направлениям (issue #80 «Настройка логирования»):
/// список категорий и значения по умолчанию (все выключены; ERROR/WARN не зависят
/// от категорий), логика общего выключателя «CM_ON», фильтрация INFO по категории,
/// сохранение/чтение настроек через <c>trace.json</c> (<see cref="TraceFlags"/>),
/// разметка вкладки «Логирование» (WPF и Avalonia) и локализация ru/en.
/// Разметочные тесты читают исходники репозитория (как Issue71SettingsWindowLayoutTests)
/// и поэтому работают на обеих платформах без запуска UI. Тесты меняют глобальное
/// состояние <see cref="TraceFlags"/>, поэтому изолированы коллекцией TraceFlagsState
/// (как и TraceFlagsTests, чтобы не гоняться параллельно друг с другом).
/// </summary>
[Collection("TraceFlagsState")]
public sealed class LoggingCategoriesTests : IDisposable
{
    private readonly string _dir;

    public LoggingCategoriesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "w1c_logcat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // Изолируем TraceFlags от реального конфига пользователя и от env.
        TraceFlags.ConfigDirectoryOverride = _dir;
        TraceFlags.EnvReader = _ => null;
        TraceFlags.ResetCacheForTesting();
    }

    public void Dispose()
    {
        TraceFlags.ConfigDirectoryOverride = null;
        TraceFlags.ResetCacheForTesting();
        try { Directory.Delete(_dir, true); }
        catch { /* временный каталог теста */ }
    }

    // --------------------------------------------------------------
    // Список категорий и значения по умолчанию
    // --------------------------------------------------------------

    [Fact]
    public void DefaultCategories_AllOff_AndMasterOff()
    {
        // По умолчанию (свежий trace.json) ВСЕ направления выключены.
        Assert.False(TraceFlags.IsLoggingMasterOn(), "Общий выключатель CM_ON должен быть выключен по умолчанию.");
        foreach (var category in LogCategory.AllCategories)
            Assert.False(TraceFlags.IsEnabled(category), $"Категория {category} должна быть выключена по умолчанию.");
    }

    [Fact]
    public void AllCategories_ContainsAllDirectionsAndDiagnostics_NoDuplicates()
    {
        Assert.Equal(
            new[]
            {
                "CM_MAIN_WINDOW", "CM_RAC", "CM_BASE_RUN", "CM_BACKUP",
                "CM_IBASES", "CM_JOBS", "CM_THEMES",
                // Issue #63 (комментарий 7OH 2026-10-28 «давайте отладку и тут
                // включать подробную»): направление «Публикация на веб-сервере».
                "CM_PUBLISH"
            },
            LogCategory.DirectionCategories);

        Assert.Contains("CM_COLUMNS", LogCategory.DiagnosticCategories);
        Assert.Contains("CM_UPDATES", LogCategory.DiagnosticCategories);

        var all = LogCategory.AllCategories;
        Assert.Equal(
            LogCategory.DirectionCategories.Length + LogCategory.DiagnosticCategories.Length,
            all.Length);
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());

        // Все категории должны быть известны формату trace.json (сериализация и разбор).
        Assert.All(all, c => Assert.Contains(c, TraceFlagsFormat.KnownFlags));
        Assert.Contains(LogCategory.MasterOn, TraceFlagsFormat.KnownFlags);

        // Все ключи локализации генерируются без пробелов и спецсимволов.
        Assert.All(all, c => Assert.Matches("^Settings\\.Logging\\.Cat[A-Za-z]+$", LogCategory.TitleKey(c)));
    }

    [Fact]
    public void SerializeDefaults_ContainsAllDirections_AsFalse()
    {
        var json = TraceFlagsFormat.SerializeDefaults();
        Assert.Contains("\"CM_ON\": false", json, StringComparison.Ordinal);
        foreach (var category in LogCategory.AllCategories)
            Assert.Contains($"\"{category}\": false", json, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------
    // Фильтрация записей по категории и общему выключателю
    // --------------------------------------------------------------

    [Fact]
    public void CategoryRecord_EnabledOnlyWhenMasterAndCategoryOn()
    {
        // 1) Включена только категория, CM_ON выключен — INFO не пишется.
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool> { [LogCategory.Backup] = true });
        TraceFlags.ResetCacheForTesting();
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Backup));

        // 2) Включён только CM_ON, все категории выключены (явно) — INFO не пишется.
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [LogCategory.MasterOn] = true,
            [LogCategory.Rac] = false,
            [LogCategory.Backup] = false
        });
        TraceFlags.ResetCacheForTesting();
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Rac));
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Backup));

        // 3) CM_ON + категория — INFO пишется (Backup остаётся явно выключен).
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [LogCategory.MasterOn] = true,
            [LogCategory.Rac] = true,
            [LogCategory.Backup] = false
        });
        TraceFlags.ResetCacheForTesting();
        Assert.True(TraceFlags.IsLogCategoryEnabled(LogCategory.Rac), "RAC включён (CM_ON + категория).");
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Backup), "Backup не включён.");
    }

    [Fact]
    public void MasterOn_NeverEnablesItself()
    {
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool> { [LogCategory.MasterOn] = true });
        TraceFlags.ResetCacheForTesting();
        Assert.True(TraceFlags.IsLoggingMasterOn());
        // Сам «CM_ON» категорией не является: передача его имени в гейт даёт false.
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.MasterOn));
    }

    [Fact]
    public void ExplicitFalse_InConfig_NotOverriddenByEnvironment()
    {
        // Семантика гейта issue #347 сохранена (issue #80): явный false в trace.json
        // выключает флаг всегда, env его НЕ перекрывает.
        TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [LogCategory.MasterOn] = true,
            [LogCategory.Jobs] = false
        });
        TraceFlags.EnvReader = name => name == LogCategory.Jobs ? "1" : null;
        TraceFlags.ResetCacheForTesting();
        Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Jobs));
    }

    // --------------------------------------------------------------
    // Сохранение/чтение настроек
    // --------------------------------------------------------------

    [Fact]
    public void SaveAndReload_Categories_RoundTrip()
    {
        var path = Path.Combine(_dir, TraceFlagsFormat.ConfigFileName);

        var ok = TraceFlags.SaveCategorySettings(new Dictionary<string, bool>
        {
            [LogCategory.MasterOn] = true,
            [LogCategory.Rac] = true,
            [LogCategory.Backup] = false,
            [LogCategory.Ibases] = true,
            // Неизвестный флаг игнорируется при записи.
            ["CM_UNKNOWN_TEST"] = true
        });
        Assert.True(ok);
        Assert.True(File.Exists(path));

        // Файл — валидный JSON с новыми категориями.
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("CM_UNKNOWN_TEST", json, StringComparison.Ordinal);
        var parsed = TraceFlagsFormat.Parse(json);
        Assert.True(parsed.IsValid);
        Assert.True(parsed.Flags[LogCategory.MasterOn]);
        Assert.True(parsed.Flags[LogCategory.Rac]);
        Assert.Contains(LogCategory.Backup, parsed.Flags.Keys); // явный false сохраняется
        Assert.False(parsed.Flags[LogCategory.Backup]);
    }

    [Fact]
    public void Save_EmptyValues_IsNoOp()
    {
        Assert.False(TraceFlags.SaveCategorySettings(new Dictionary<string, bool>()));
        Assert.False(TraceFlags.SaveCategorySettings(null!));
    }

    // --------------------------------------------------------------
    // Разметка вкладки «Логирование» (WPF + Avalonia)
    // --------------------------------------------------------------

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

    [Fact]
    public void WpfSettingsWindow_HasLoggingTabBeforeAbout()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("Settings.TabLogging", xaml, StringComparison.Ordinal);
        Assert.Contains("LoggingMasterOnCheck", xaml, StringComparison.Ordinal);
        Assert.Contains("LoggingDirectionsPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("LoggingDiagnosticsPanel", xaml, StringComparison.Ordinal);
        // Вкладка «Логирование» идёт ближе к концу — ПЕРЕД «О программе».
        Assert.True(
            xaml.IndexOf("Settings.TabLogging", StringComparison.Ordinal) <
            xaml.IndexOf("Settings.TabAbout", StringComparison.Ordinal));

        var codeBehind = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Logging.cs"));
        Assert.Contains("TraceFlags.SaveCategorySettings", codeBehind, StringComparison.Ordinal);
        var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("InitializeLoggingTab();", main, StringComparison.Ordinal);
        Assert.Contains("SaveLoggingSettings();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaSettingsWindow_HasLoggingTabBeforeAbout()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("Settings.TabLogging", code, StringComparison.Ordinal);
        Assert.Contains("BuildLoggingTab()", code, StringComparison.Ordinal);
        var loggingIndex = code.IndexOf("tabs.Items.Add(tabLogging);", StringComparison.Ordinal);
        var aboutIndex = code.IndexOf("tabs.Items.Add(tabAbout);", StringComparison.Ordinal);
        Assert.True(loggingIndex > 0 && aboutIndex > 0, "Avalonia: вкладки должны добавляться в список.");
        Assert.True(loggingIndex < aboutIndex, "Avalonia: «Логирование» должно идти ПЕРЕД «О программе».");

        var logging = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs"));
        Assert.Contains("BuildLoggingTab", logging, StringComparison.Ordinal);
        Assert.Contains("SaveLoggingSettingsAvalonia", logging, StringComparison.Ordinal);
        Assert.Contains("TraceFlags.SaveCategorySettings", logging, StringComparison.Ordinal);
        var main = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("SaveLoggingSettingsAvalonia();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_BothLanguages_HaveAllLoggingKeys()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        var keys = new[]
        {
            "Settings.TabLogging",
            "Settings.Logging.Description",
            "Settings.Logging.MasterOn",
            "Settings.Logging.MasterOnDesc",
            "Settings.Logging.Directions",
            "Settings.Logging.Diagnostics"
        }.Concat(LogCategory.AllCategories.SelectMany(c => new[]
        {
            LogCategory.TitleKey(c),
            LogCategory.DescriptionKey(c)
        })).ToList();

        foreach (var key in keys)
        {
            Assert.Contains($"\"{key}\":", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MarkedLogSites_UseLogCategoryHelper()
    {
        // Ключевые места логирования направлений помечены категориями через
        // Services.Log.Info (а не голым _logger.Info) — иначе отключение не работало бы.
        var rac = ReadProjectFile(Path.Combine("Workspace1C", "Services", "RacClient.cs"));
        Assert.Contains("Log.Info(LogCategory.Rac", rac, StringComparison.Ordinal);

        var scheduler = ReadProjectFile(Path.Combine("Workspace1C", "Services", "SchedulerService.cs"));
        Assert.Contains("Log.Info(LogCategory.Jobs", scheduler, StringComparison.Ordinal);

        var backup = ReadProjectFile(Path.Combine("Workspace1C", "Services", "BackupService.cs"));
        Assert.Contains("Log.Info(LogCategory.Backup", backup, StringComparison.Ordinal);

        var theme = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Theme.cs"));
        Assert.Contains("Log.Info(Workspace1C.Services.LogCategory.Themes", theme, StringComparison.Ordinal);

        var app = ReadProjectFile(Path.Combine("Workspace1C", "App.axaml.cs"));
        Assert.Contains("LogCategory.MainWindow", app, StringComparison.Ordinal);
    }

    [Fact]
    public void InfoLogSites_UseCategoryGate_NotRawLogger()
    {
        // issue #80 (комментарии 7OH от 2026-10-29): INFO-записи направлений обязаны
        // идти через категорийный гейт Log.Info(LogCategory.*, …), а не голый
        // _logger.Info — иначе они «капают» в журнал при выключенном CM_ON/категории.
        var launchWpf = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.Contains("Log.Info(LogCategory.BaseRun", launchWpf, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger.Info($\"Запущена база", launchWpf, StringComparison.Ordinal);

        var launchVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "LaunchViewModel.cs"));
        Assert.Contains("Log.Info(LogCategory.BaseRun", launchVm, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger.Info($\"Запущена база", launchVm, StringComparison.Ordinal);

        var launchAvalonia = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "LaunchViewModel.Avalonia.cs"));
        Assert.Contains("Log.Info(LogCategory.BaseRun", launchAvalonia, StringComparison.Ordinal);

        var main = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("Log.Info(LogCategory.Ibases", main, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger.Info($\"Создана резервная копия ibases.v8i", main, StringComparison.Ordinal);

        // «Сессионная cookie» — INFO (а не WARN) и через диагностический флаг CM_UPDATES.
        var updates = ReadProjectFile(Path.Combine("Workspace1C", "Services", "OneCUpdatesService.cs"));
        Assert.DoesNotContain("_logger.Warn(\"[Updates] Вход: сессионная cookie", updates, StringComparison.Ordinal);
        Assert.Contains("сессионная cookie", updates, StringComparison.Ordinal);
        Assert.Contains("Log.Info(TraceFlags.UpdatesFlag", updates, StringComparison.Ordinal);

        // RING-записи — через категорию CM_RAC (issue #80 + #83).
        var ring = ReadProjectFile(Path.Combine("Workspace1C", "Services", "RingLicenseService.cs"));
        Assert.Contains("Log.Info(LogCategory.Rac", ring, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger?.Info($\"RING:", ring, StringComparison.Ordinal);
    }
}
