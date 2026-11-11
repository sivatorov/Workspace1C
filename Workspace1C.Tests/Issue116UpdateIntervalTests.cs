using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #116 (v1.0.1.54): доработка подсистемы обновлений.
/// <list type="bullet">
/// <item>периодичность проверки «как в заданиях»: интервал (число) + единица
/// (минуты/часы/дни), хранение в минутах — юнит-тесты
/// <see cref="AppSettings.DecomposeUpdateInterval"/>/<see cref="AppSettings.ComposeUpdateInterval"/>,
/// кламп и миграция старых настроек (0/60/360/1440 остаются валидными);</item>
/// <item>значок «внимание» в подвале вместо красных «!!!» — source-гварды WPF
/// разметки и Avalonia кода;</item>
/// <item>fallback «Что нового»: локальная секция CHANGELOG.md (парсер) и
/// гарантия непустого содержимого — source-гварды окон;</item>
/// <item>запрет вертикального растягивания окна обновления — source-гварды
/// WndProc (WPF) и CanResize=false (Avalonia).</item>
/// </list>
/// </summary>
public sealed class Issue116UpdateIntervalTests
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

    // ===================== Интервал: разложение значение+единица =====================

    [Fact]
    public void Decompose_Zero_IsZeroMinutes()
    {
        // 0 — проверка только при запуске и вручную.
        AppSettings.DecomposeUpdateInterval(0, out var value, out var unit);
        Assert.Equal(0, value);
        Assert.Equal(UpdateCheckIntervalUnit.Minutes, unit);
    }

    [Theory]
    [InlineData(60, 1, UpdateCheckIntervalUnit.Hours)]     // час
    [InlineData(360, 6, UpdateCheckIntervalUnit.Hours)]    // 6 часов (старая настройка)
    [InlineData(1440, 1, UpdateCheckIntervalUnit.Days)]    // сутки (старая настройка)
    [InlineData(2880, 2, UpdateCheckIntervalUnit.Days)]    // двое суток
    [InlineData(90, 90, UpdateCheckIntervalUnit.Minutes)]  // полтора часа — не кратен часу
    [InlineData(43200, 30, UpdateCheckIntervalUnit.Days)]  // максимум — 30 суток
    public void Decompose_SplitsByBestUnit(int minutes, int expectedValue, UpdateCheckIntervalUnit expectedUnit)
    {
        AppSettings.DecomposeUpdateInterval(minutes, out var value, out var unit);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedUnit, unit);
    }

    [Fact]
    public void Compose_ConvertsValueAndUnitToMinutes()
    {
        Assert.Equal(0, AppSettings.ComposeUpdateInterval(0, UpdateCheckIntervalUnit.Minutes));
        Assert.Equal(0, AppSettings.ComposeUpdateInterval(-1, UpdateCheckIntervalUnit.Hours));
        Assert.Equal(90, AppSettings.ComposeUpdateInterval(90, UpdateCheckIntervalUnit.Minutes));
        Assert.Equal(360, AppSettings.ComposeUpdateInterval(6, UpdateCheckIntervalUnit.Hours));
        Assert.Equal(1440, AppSettings.ComposeUpdateInterval(1, UpdateCheckIntervalUnit.Days));
    }

    [Fact]
    public void Compose_ClampsToMaxInterval()
    {
        // Значения выше 30 суток клампятся к пределу.
        Assert.Equal(AppSettings.MaxUpdateCheckIntervalMinutes,
            AppSettings.ComposeUpdateInterval(100, UpdateCheckIntervalUnit.Days));
        Assert.Equal(AppSettings.MaxUpdateCheckIntervalMinutes,
            AppSettings.ComposeUpdateInterval(10_000, UpdateCheckIntervalUnit.Hours));
    }

    [Fact]
    public void ComposeDecompose_RoundTrip_PreservesSettings()
    {
        // Настройка → UI → настройка: старые значения из фиксированного списка
        // и произвольные значения пользователя проходят без потерь.
        foreach (var minutes in new[] { 0, 15, 60, 90, 360, 1440, 2880, 43200 })
        {
            AppSettings.DecomposeUpdateInterval(minutes, out var value, out var unit);
            Assert.Equal(minutes, AppSettings.ComposeUpdateInterval(value, unit));
        }
    }

    [Fact]
    public void NormalizeUpdateInterval_ClampsAndPasses()
    {
        Assert.Equal(0, AppSettings.NormalizeUpdateInterval(-5));
        Assert.Equal(777, AppSettings.NormalizeUpdateInterval(777));
        Assert.Equal(AppSettings.MaxUpdateCheckIntervalMinutes,
            AppSettings.NormalizeUpdateInterval(AppSettings.MaxUpdateCheckIntervalMinutes + 1));
    }

    // ===================== Миграция старых настроек =====================

    [Fact]
    public void LoadSettings_OldFixedIntervals_MigrateUnchanged()
    {
        // Старые настройки (v1.0.1.49–v1.0.1.53: 0/60/360/1440) остаются валидными
        // после загрузки — миграции значений не требуется (хранение в минутах).
        foreach (var minutes in AppSettings.UpdateCheckIntervals)
        {
            var settings = new AppSettings { UpdateCheckIntervalMinutes = minutes };
            settings.NormalizeForLoad();
            Assert.Equal(minutes, settings.UpdateCheckIntervalMinutes);
        }
    }

    [Fact]
    public void OldDecomposition_CoversAllLegacyIntervals()
    {
        // Каждое старое значение раскладывается в читаемую пару «значение + единица».
        AppSettings.DecomposeUpdateInterval(60, out var v1, out var u1);
        Assert.Equal((1, UpdateCheckIntervalUnit.Hours), (v1, u1));
        AppSettings.DecomposeUpdateInterval(360, out var v2, out var u2);
        Assert.Equal((6, UpdateCheckIntervalUnit.Hours), (v2, u2));
        AppSettings.DecomposeUpdateInterval(1440, out var v3, out var u3);
        Assert.Equal((1, UpdateCheckIntervalUnit.Days), (v3, u3));
    }

    // ===================== UI настроек: source-гварды WPF =====================

    [Fact]
    public void Wpf_SettingsWindow_HasIntervalValueAndUnitControls()
    {
        // Вместо фиксированного ComboBox «час / 6 часов / сутки» — поле «Интервал»
        // (число) + выбор периодичности (минуты/часы/дни), как в планировщике заданий.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("UpdateIntervalValueBox", xaml);
        Assert.Contains("UpdateIntervalUnitCombo", xaml);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Minutes", xaml);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Hours", xaml);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Days", xaml);
        Assert.DoesNotContain("UpdateIntervalComboBox", xaml);
        Assert.DoesNotContain("Settings.General.UpdateInterval.OnStartup", xaml);
    }

    [Fact]
    public void Wpf_SettingsWindow_CodeBehind_ComposesAndDecomposes()
    {
        // Загрузка: минуты раскладываются DecomposeUpdateInterval; сохранение:
        // пара «значение + единица» собирается ComposeUpdateInterval.
        var display = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Display.cs"));
        Assert.Contains("DecomposeUpdateInterval", display);
        Assert.Contains("UpdateIntervalValueBox", display);
        Assert.DoesNotContain("UpdateCheckIntervals", display);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("ComposeUpdateInterval", code);
        Assert.Contains("UpdateIntervalValueBox", code);
        Assert.DoesNotContain("UpdateCheckIntervals", code);
    }

    // ===================== UI настроек: source-гварды Avalonia =====================

    [Fact]
    public void Avalonia_SettingsWindow_HasIntervalValueAndUnitControls()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("updateIntervalValueBox", code);
        Assert.Contains("updateIntervalUnitCombo", code);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Minutes", code);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Hours", code);
        Assert.Contains("Settings.General.UpdateIntervalUnit.Days", code);
        Assert.Contains("DecomposeUpdateInterval", code);
        Assert.Contains("ComposeUpdateInterval", code);
        Assert.DoesNotContain("Settings.General.UpdateInterval.OnStartup", code);
    }

    [Fact]
    public void Localization_HasIntervalUnitKeys()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                FindRepoRoot(), "Workspace1C", "Localization", "Languages", lang + ".json"));
            foreach (var key in new[]
                     {
                         "Settings.General.UpdateInterval",
                         "Settings.General.UpdateIntervalTooltip",
                         "Settings.General.UpdateIntervalUnit.Minutes",
                         "Settings.General.UpdateIntervalUnit.Hours",
                         "Settings.General.UpdateIntervalUnit.Days"
                     })
            {
                Assert.Contains($"\"{key}\"", json);
            }
            // Устаревшие ключи фиксированного списка удалены.
            Assert.DoesNotContain("Settings.General.UpdateInterval.OnStartup", json);
            Assert.DoesNotContain("Settings.General.UpdateInterval.Hourly", json);
            Assert.DoesNotContain("Settings.General.UpdateInterval.SixHours", json);
            Assert.DoesNotContain("Settings.General.UpdateInterval.Daily\"", json);
        }
    }

    // ===================== Значок «внимание» вместо «!!!» =====================

    [Fact]
    public void Wpf_Footer_UsesWarningGlyphInsteadOfExclaims()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Глиф «внимание» (восклицательный знак в треугольнике) в акцентном цвете.
        Assert.Contains("x:Name=\"FooterUpdateWarnIcon\"", xaml);
        Assert.Contains("Kind=\"Alert\"", xaml);
        Assert.Contains("AccentBrush", xaml);
        // Красные «!!!» убраны (красный «!!» в логах = серьёзные ошибки).
        Assert.DoesNotContain("Text=\"!!!\"", xaml);
        Assert.DoesNotContain("FooterUpdateExclaim", xaml);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Updates.cs"));
        Assert.Contains("FooterUpdateWarnIcon.Visibility", code);
        Assert.DoesNotContain("FooterUpdateExclaim", code);
    }

    [Fact]
    public void Avalonia_Footer_UsesWarningGlyphInsteadOfExclaims()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("_footerUpdateWarningIcon = IconHelper.MakeIcon(\"IconWarning\"", code);
        Assert.DoesNotContain("Text = \"!!!\"", code);
        Assert.DoesNotContain("_footerUpdateExclaim", code);
    }

    // ===================== Fallback «Что нового» =====================

    [Fact]
    public void ChangelogSectionParser_ExtractsVersionSection()
    {
        const string changelog = """
            # Changelog

            ## [9.9.9.1] — 2026-01-01

            ### Исправлено

            - Пункт старой версии.

            ## [9.9.9.2] — 2026-01-02

            - Сводка новой версии.

            ## [9.9.9.3] — 2026-01-03
            """;

        var section = WhatsNewService.ParseChangelogSection(changelog, "9.9.9.2");
        Assert.NotNull(section);
        Assert.Contains("Сводка новой версии", section);
        Assert.DoesNotContain("Пункт старой версии", section);
        Assert.DoesNotContain("9.9.9.3", section);

        // Пустая секция (нет тела до следующего заголовка) — null.
        Assert.Null(WhatsNewService.ParseChangelogSection(changelog, "9.9.9.3"));
        // Версии нет в файле — null.
        Assert.Null(WhatsNewService.ParseChangelogSection(changelog, "1.2.3.4"));
        Assert.Null(WhatsNewService.ParseChangelogSection(null, "1.2.3.4"));
        Assert.Null(WhatsNewService.ParseChangelogSection(changelog, ""));
    }

    [Fact]
    public void ChangelogSectionParser_ReadsRepoChangelog()
    {
        // Реальный CHANGELOG репозитория парсится: секция текущей базовой версии
        // находится, тело непустое (fallback «Что нового» работает рядом с exe).
        var path = Path.Combine(FindRepoRoot(), "CHANGELOG.md");
        var markdown = File.ReadAllText(path);
        var section = WhatsNewService.ParseChangelogSection(markdown, "1.0.1.53");
        Assert.NotNull(section);
        Assert.NotEmpty(section.Trim());
    }

    [Fact]
    public void ShouldShow_UsesChangelogFallback()
    {
        // ShouldShow показывает диалог, когда для версии есть захардкоженная
        // сводка ИЛИ локальная секция CHANGELOG.md (issue #116 v1.0.1.54).
        var settings = new AppSettings { ShowWhatsNewOnStart = true, LastWhatsNewVersion = string.Empty };
        var expected = WhatsNewService.HasEntry(WhatsNewService.CurrentVersion)
            || WhatsNewService.HasChangelogSection(WhatsNewService.CurrentVersion);
        Assert.Equal(expected, WhatsNewService.ShouldShow(settings));
    }

    [Fact]
    public void WhatsNewWindows_NeverStayEmpty()
    {
        // Fallback-порядок: body GitHub-релиза → локальная секция CHANGELOG.md →
        // заглушка; окно никогда не остаётся с пустым содержимым (обе платформы).
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.xaml.cs"));
        Assert.Contains("TryGetChangelogSection", wpf);
        Assert.Contains("WhatsNew.NotesUnavailable", wpf);
        // В блоке-перехватчике исключений тоже добавляется содержимое.
        Assert.Contains("Сбой загрузки сводки не роняет диалог", wpf);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.Avalonia.cs"));
        Assert.Contains("TryGetChangelogSection", avalonia);
        Assert.Contains("WhatsNew.NotesUnavailable", avalonia);
    }

    [Fact]
    public void Localization_HasNotesUnavailableKey()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = File.ReadAllText(Path.Combine(
                FindRepoRoot(), "Workspace1C", "Localization", "Languages", lang + ".json"));
            Assert.Contains("\"WhatsNew.NotesUnavailable\"", json);
        }
    }

    // ===================== Растягивание окон обновления =====================

    [Fact]
    public void Wpf_UpdateAvailableWindow_LocksWidthAndHeight()
    {
        // WndProc фиксирует и ширину (issue #162/#157), и высоту
        // (issue #116 v1.0.1.54 — запрет вертикального растягивания).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml.cs"));
        Assert.Contains("FixedHeightDips", code);
        Assert.Contains("FixedHeightPx", code);
        Assert.Contains("DpiScaleY", code);
        Assert.Contains("mmi.MinTrackSize.Y = h;", code);
        Assert.Contains("mmi.MaxTrackSize.Y = h;", code);
        Assert.Contains("rc.Top = rc.Bottom - h;", code);
        Assert.Contains("rc.Bottom = rc.Top + h;", code);
        // Вертикальная прокрутка «Что нового» внутри окна сохранена (разметка окна).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
    }

    [Fact]
    public void Avalonia_MessageWindow_KeepsFixedHeight()
    {
        // Avalonia-диалог обновления: CanResize=false фиксирует размер по контенту.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        Assert.Contains("CanResize = false", code);
        Assert.Contains("SizeToContent.Height", code);
    }

    [Fact]
    public void Avalonia_WhatsNewWindow_TextAreaFollowsWindowSize()
    {
        // Окно «Что нового» можно растягивать, и область текста следует за окном:
        // корневая компоновка — DockPanel с LastChildFill (issue #124, v1.0.1.56:
        // нижняя панель докится к низу и не обрезается, карточка сводки заполняет
        // остаток), контент — единственный ScrollViewer с Auto-прокруткой (issue #116).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WhatsNewWindow.Avalonia.cs"));
        Assert.Contains("CanResize = true", code);
        Assert.Contains("new DockPanel { LastChildFill = true", code);
        Assert.Contains("DockPanel.SetDock(bottom, Dock.Bottom)", code);
        Assert.Contains("ScrollBarVisibility.Auto", code);
    }
}
