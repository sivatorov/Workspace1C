using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #131 «Уведомления о долгих операциях»: порог и единицы, машина состояний
/// занятости окон 1С, текст уведомления, настройки (persist) и source-гварды
/// парных правок WPF/Avalonia.
/// </summary>
public sealed class Issue131LongOperationNotificationTests
{
    // ---------- Порог и единицы ----------

    [Theory]
    [InlineData(1, LongOperationThresholdUnit.Seconds, 1)]
    [InlineData(30, LongOperationThresholdUnit.Seconds, 30)]
    [InlineData(2, LongOperationThresholdUnit.Minutes, 120)]
    [InlineData(3, LongOperationThresholdUnit.Hours, 10800)]
    public void Threshold_ConvertsUnits(int value, LongOperationThresholdUnit unit, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), LongOperationThreshold.ToTimeSpan(value, unit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Threshold_ZeroOrNegative_MinimumOneSecond(int value)
    {
        Assert.Equal(TimeSpan.FromSeconds(1),
            LongOperationThreshold.ToTimeSpan(value, LongOperationThresholdUnit.Minutes));
    }

    [Fact]
    public void Threshold_StringUnits_AreParsedAndNormalized()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), LongOperationThreshold.ToTimeSpan(2, "Minutes"));
        Assert.Equal(TimeSpan.FromHours(1), LongOperationThreshold.ToTimeSpan(1, "hours"));
        Assert.Equal(TimeSpan.FromSeconds(5), LongOperationThreshold.ToTimeSpan(5, "unknown"));
        Assert.Equal(LongOperationThresholdUnits.Hours,
            LongOperationThreshold.ToSettingString(LongOperationThresholdUnit.Hours));
    }

    [Fact]
    public void Threshold_TooLarge_IsClamped()
    {
        var span = LongOperationThreshold.ToTimeSpan(int.MaxValue, LongOperationThresholdUnit.Hours);
        Assert.Equal(TimeSpan.FromSeconds(LongOperationThreshold.MaximumSeconds), span);
    }

    // ---------- Трекер занятости ----------

    private static LongOperationObservation Obs(
        bool responding, string title = "1С:Предприятие", RunningKind mode = RunningKind.Enterprise,
        string id = "base-1", string name = "Бухгалтерия")
        => new(id, name, mode, true, responding, title);

    [Fact]
    public void Tracker_BusyShorterThanThreshold_NoRelease()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        Assert.Empty(tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(10), start));
        Assert.Empty(tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(10), start.AddSeconds(5)));
        Assert.Empty(tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(10), start.AddSeconds(6)));
    }

    [Fact]
    public void Tracker_BusyLongerThanThreshold_ReleaseWithDuration()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(10), start);
        Assert.Empty(tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(10), start.AddSeconds(15)));
        var releases = tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(10), start.AddSeconds(20));

        var release = Assert.Single(releases);
        Assert.Equal("base-1", release.InfobaseId);
        Assert.Equal("Бухгалтерия", release.InfobaseName);
        Assert.Equal(RunningKind.Enterprise, release.Mode);
        Assert.Equal("1С:Предприятие", release.WindowTitle);
        Assert.Equal(TimeSpan.FromSeconds(20), release.Duration);
        Assert.Equal(0, tracker.TrackedCount);
    }

    [Fact]
    public void Tracker_SecondCycle_EmitsAgain()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start);
        tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(6));
        var first = tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(7));
        Assert.Single(first);

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start.AddSeconds(20));
        tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(30));
        var second = tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(31));
        Assert.Single(second);
        Assert.Equal(TimeSpan.FromSeconds(11), second[0].Duration);
    }

    [Fact]
    public void Tracker_RepeatedBusyPoll_DoesNotRestartTimer()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start);
        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start.AddSeconds(10));
        tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(12));
        var releases = tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(13));

        Assert.Single(releases);
        Assert.Equal(TimeSpan.FromSeconds(13), releases[0].Duration);
    }

    [Fact]
    public void Tracker_Modes_AreSeparateKeys()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[]
        {
            Obs(false, "Конфигуратор", RunningKind.Configurator),
            Obs(false, "Предприятие", RunningKind.Enterprise)
        }, TimeSpan.FromSeconds(5), start);

        tracker.Observe(new[]
        {
            Obs(true, "Конфигуратор", RunningKind.Configurator),
            Obs(false, "Предприятие", RunningKind.Enterprise)
        }, TimeSpan.FromSeconds(5), start.AddSeconds(10));
        var releases = tracker.Observe(new[]
        {
            Obs(true, "Конфигуратор", RunningKind.Configurator),
            Obs(false, "Предприятие", RunningKind.Enterprise)
        }, TimeSpan.FromSeconds(5), start.AddSeconds(11));

        var release = Assert.Single(releases);
        Assert.Equal(RunningKind.Configurator, release.Mode);
        Assert.Equal(1, tracker.TrackedCount); // Предприятие всё ещё занято
    }

    [Fact]
    public void Tracker_ProcessClosedWithoutRelease_NoEventAndStateReset()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start);
        // Процесс закрылся: наблюдения больше нет — событие не отправляется.
        Assert.Empty(tracker.Observe(Array.Empty<LongOperationObservation>(),
            TimeSpan.FromSeconds(5), start.AddSeconds(30)));
        Assert.Equal(0, tracker.TrackedCount);
    }

    [Fact]
    public void Tracker_NotRunningObservation_IsIgnored()
    {
        var tracker = new LongOperationTracker();
        var start = DateTimeOffset.UtcNow;

        var idle = new LongOperationObservation("base-1", "База", RunningKind.Enterprise, false, true, "");
        Assert.Empty(tracker.Observe(new[] { idle }, TimeSpan.FromSeconds(5), start));
        Assert.Equal(0, tracker.TrackedCount);
    }

    [Fact]
    public void Tracker_ConfigurableHysteresis_OnePollReleasesImmediately()
    {
        var tracker = new LongOperationTracker(releaseConfirmations: 1);
        var start = DateTimeOffset.UtcNow;

        tracker.Observe(new[] { Obs(false) }, TimeSpan.FromSeconds(5), start);
        var releases = tracker.Observe(new[] { Obs(true) }, TimeSpan.FromSeconds(5), start.AddSeconds(6));

        Assert.Single(releases);
    }

    // ---------- Текст уведомления ----------

    [Fact]
    public void Text_ContainsComputerModeTitleAndDuration()
    {
        var duration = TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5);
        var text = LongOperationNotificationText.Build("PC-01", RunningKind.Configurator,
            "Конфигуратор — Бухгалтерия", duration);

        Assert.Contains("PC-01", text);
        Assert.True(text.Contains("Конфигуратор") || text.Contains("Configurator"),
            "В тексте нет режима «Конфигуратор»: " + text);
        Assert.Contains("Конфигуратор — Бухгалтерия", text);
        Assert.Contains("2", text);
        Assert.Contains(LongOperationNotificationText.FormatDuration(duration), text);
    }

    [Fact]
    public void Text_EmptyTitleAndMachine_HasFallbacks()
    {
        var text = LongOperationNotificationText.Build(null, RunningKind.Enterprise, null, TimeSpan.FromSeconds(45));

        Assert.Contains(Environment.MachineName, text);
        Assert.True(text.Contains("Предприятие") || text.Contains("Enterprise"),
            "В тексте нет режима «Предприятие»: " + text);
        Assert.True(text.Contains("окно без заголовка") || text.Contains("window without a title"),
            "В тексте нет замены пустого заголовка: " + text);
        Assert.Equal(4, text.Split(Environment.NewLine).Length);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 45)]
    [InlineData(0, 2, 5)]
    [InlineData(1, 5, 3)]
    public void Text_FormatDuration_IsReadable(int hours, int minutes, int seconds)
    {
        var formatted = LongOperationNotificationText.FormatDuration(
            new TimeSpan(hours, minutes, seconds));
        Assert.False(string.IsNullOrWhiteSpace(formatted));
        if (hours > 0)
            Assert.Contains(hours.ToString(), formatted);
        else if (minutes > 0)
            Assert.Contains(minutes.ToString(), formatted);
        else
            Assert.Contains(seconds.ToString(), formatted);
    }

    // ---------- Настройки (persist) ----------

    [Fact]
    public void AppSettings_RoundTrip_LongOperationSection()
    {
        var settings = new AppSettings
        {
            LongOperationNotifications = new LongOperationNotificationsSettings
            {
                Enabled = true,
                Channel = LongOperationNotificationChannels.Telegram,
                TelegramTarget = "t.me/c/1234567890/42",
                ThresholdValue = 15,
                ThresholdUnit = LongOperationThresholdUnits.Minutes,
                MonitorEnterprise = true,
                MonitorConfigurator = false
            }
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        var section = restored.LongOperationNotifications;
        Assert.True(section.Enabled);
        Assert.Equal(LongOperationNotificationChannels.Telegram, section.Channel);
        Assert.Equal("t.me/c/1234567890/42", section.TelegramTarget);
        Assert.Equal(15, section.ThresholdValue);
        Assert.Equal(LongOperationThresholdUnits.Minutes, section.ThresholdUnit);
        Assert.True(section.MonitorEnterprise);
        Assert.False(section.MonitorConfigurator);
    }

    [Fact]
    public void AppSettings_LegacyWithoutSection_GetsDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"ru\"}")!;
        settings.NormalizeForLoad();

        Assert.NotNull(settings.LongOperationNotifications);
        Assert.False(settings.LongOperationNotifications.Enabled);
        Assert.Equal(LongOperationNotificationChannels.System, settings.LongOperationNotifications.Channel);
        Assert.Equal(LongOperationThresholdUnits.Seconds, settings.LongOperationNotifications.ThresholdUnit);
    }

    [Fact]
    public void Settings_Normalize_UnknownValuesToCanonical()
    {
        var section = new LongOperationNotificationsSettings
        {
            Channel = "telegram",
            ThresholdUnit = "HOURS",
            TelegramTarget = null!,
            ThresholdValue = 0
        };

        section.Normalize();

        Assert.Equal(LongOperationNotificationChannels.Telegram, section.Channel);
        Assert.Equal(LongOperationThresholdUnits.Hours, section.ThresholdUnit);
        Assert.Equal("", section.TelegramTarget);
        Assert.Equal(30, section.ThresholdValue);
    }

    // ---------- Source-гварды ----------

    [Fact]
    public void WpfMainWindow_HasAlarmToggleAndTooltips()
    {
        var xaml = ReadSource("Workspace1C", "Views", "MainWindow.xaml");
        Assert.Contains("LongOperationAlarmToggle", xaml);
        Assert.Contains("LongOperationNotificationsEnabled", xaml);
        Assert.Contains("LongOperation.Alarm.TooltipOn", xaml);
        Assert.Contains("LongOperation.Alarm.TooltipOff", xaml);
    }

    [Fact]
    public void AvaloniaMainWindow_HasAlarmToggle()
    {
        var source = ReadSource("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.Contains("BuildLongOperationAlarmToggle", source);
        Assert.Contains("LongOperationNotificationsEnabled", source);
        Assert.Contains("IconScheduler", source);
    }

    [Fact]
    public void Monitor_ReadsSettingsOnEveryPoll()
    {
        var source = ReadSource("Workspace1C", "ViewModels", "MainViewModel.Running.cs");
        Assert.Contains("LoadLongOperationNotificationsSafe()", source);
        Assert.Contains("LongOperationTracker", source);
        Assert.Contains("NotificationEvent.LongOperation", source);
        Assert.Contains("LongOperationThreshold.ToTimeSpan", source);
    }

    [Fact]
    public void BothPlatforms_HaveSettingsGroup()
    {
        var wpf = ReadSource("Workspace1C", "Views", "SettingsWindow.xaml");
        Assert.Contains("LongOperationNotificationsCheck", wpf);
        Assert.Contains("LongOperationChannelCombo", wpf);
        Assert.Contains("LongOperationTelegramTargetBox", wpf);
        Assert.Contains("LongOperationThresholdBox", wpf);
        Assert.Contains("LongOperationThresholdUnitCombo", wpf);

        var avalonia = ReadSource("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.Contains("LongOperation.Settings.GroupTitle", avalonia);
        Assert.Contains("ApplyLongOperationNotificationSettings", avalonia);
    }

    [Fact]
    public void Localization_HasAllNewKeysInBothLanguages()
    {
        var keys = new[]
        {
            "LongOperation.Settings.GroupTitle", "LongOperation.Settings.Enabled",
            "LongOperation.Settings.ChannelLabel", "LongOperation.Settings.Channel.System",
            "LongOperation.Settings.Channel.Telegram", "LongOperation.Settings.TelegramTargetHint",
            "LongOperation.Settings.TelegramTargetTooltip", "LongOperation.Settings.ThresholdLabel",
            "LongOperation.Settings.Unit.Seconds", "LongOperation.Settings.Unit.Minutes",
            "LongOperation.Settings.Unit.Hours", "LongOperation.Settings.Hint",
            "LongOperation.Alarm.TooltipOn", "LongOperation.Alarm.TooltipOff",
            "LongOperation.NotificationTitle", "LongOperation.Mode.Enterprise",
            "LongOperation.Mode.Configurator", "LongOperation.Label.Computer",
            "LongOperation.Label.Mode", "LongOperation.Label.Window",
            "LongOperation.Label.Duration", "LongOperation.NoWindowTitle",
            "LongOperation.Duration.HoursShort", "LongOperation.Duration.MinutesShort",
            "LongOperation.Duration.SecondsShort"
        };

        foreach (var language in new[] { "ru.json", "en.json" })
        {
            using var document = JsonDocument.Parse(ReadSource("Workspace1C", "Localization", "Languages", language));
            var strings = document.RootElement.GetProperty("strings");
            foreach (var key in keys)
                Assert.True(strings.TryGetProperty(key, out _), $"{language}: нет ключа {key}");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
