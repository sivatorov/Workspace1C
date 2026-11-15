using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        // Уточнение 7OH: будильник заменён колокольчиком (Pack.BellOutline).
        Assert.Contains("Pack.BellOutline", source);
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

    // ---------- Source-гварды: расположение и иконка (v1.0.1.64) ----------
    // Перенос переключателя из блока действий правой панели в подвал и замена
    // будильника на колокольчик должны оставаться зафиксированными source-тестами:
    // они не зависят от форматирования XAML (пробелы схлопываются) и не повторяют
    // проверки «ключ присутствует», уже сделанные выше.

    [Fact]
    public void Wpf_AlarmToggle_IsInStatusBarNextToSessionButton_NotInActionsBlock()
    {
        var xaml = CollapseWhitespace(ReadSource("Workspace1C", "Views", "MainWindow.xaml"));

        const string toggleName = "x:Name=\"LongOperationAlarmToggle\"";
        Assert.Equal(1, CountOccurrences(xaml, toggleName));

        var statusBar = xaml.IndexOf("StatusBarBorder", StringComparison.Ordinal);
        Assert.True(statusBar >= 0, "MainWindow.xaml: не найдена строка состояния (StatusBarBorder)");

        var toggle = xaml.IndexOf(toggleName, StringComparison.Ordinal);
        Assert.True(toggle > statusBar,
            "Переключатель уведомлений должен находиться в строке состояния (после StatusBarBorder), " +
            "а не в блоке действий правой панели");

        var session = xaml.IndexOf("ToggleSessionLaunchPanelCommand", statusBar, StringComparison.Ordinal);
        Assert.True(session > toggle,
            "Переключатель уведомлений должен стоять в подвале перед кнопкой «Текущая сессия» " +
            "(ToggleSessionLaunchPanelCommand)");
        Assert.True(session - toggle < 4000,
            "Переключатель уведомлений должен стоять рядом с кнопкой «Текущая сессия», " +
            "а не в другом конце строки состояния");

        // Блок «Действия» правой панели: от его заголовка до строки состояния —
        // переключателя там быть не должно.
        const string actionsHeader = "Text=\"{loc:Loc Main.Actions}\"";
        var actions = xaml.LastIndexOf(actionsHeader, statusBar, StringComparison.Ordinal);
        Assert.True(actions >= 0, "MainWindow.xaml: не найден блок действий правой панели");
        var actionsBlock = xaml.Substring(actions, statusBar - actions);
        Assert.DoesNotContain("LongOperationAlarmToggle", actionsBlock);

        // Непосредственный родитель — StackPanel строки состояния (тот же, где кнопка сессии).
        var beforeToggle = xaml.Substring(statusBar, toggle - statusBar);
        var parentTail = beforeToggle.Length > 400 ? beforeToggle[^400..] : beforeToggle;
        Assert.Contains("<StackPanel", parentTail);
    }

    [Fact]
    public void Wpf_AlarmToggle_UsesBellOutlineAndStrikeThroughWhenOff()
    {
        var xaml = CollapseWhitespace(ReadSource("Workspace1C", "Views", "MainWindow.xaml"));
        var toggleBlock = ExtractXamlElement(xaml,
            "x:Name=\"LongOperationAlarmToggle\"", "</ToggleButton>");

        // Иконка — контурный колокольчик из словаря темы.
        Assert.Contains("Data=\"{DynamicResource Pack.BellOutline}\"", toggleBlock);

        // Перечёркивание: линия поверх колокольчика.
        Assert.Contains("<Line ", toggleBlock);

        // Состояние переключателя — двусторонняя привязка к настройке.
        Assert.Contains("IsChecked=\"{Binding LongOperationNotificationsEnabled, Mode=TwoWay}\"", toggleBlock);

        // Выключено: линия видима по умолчанию; включено: DataTrigger её скрывает.
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Visible\"/>", toggleBlock);
        Assert.Contains(
            "<DataTrigger Binding=\"{Binding IsChecked, ElementName=LongOperationAlarmToggle}\" Value=\"True\">",
            toggleBlock);
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\"/>", toggleBlock);

        // Включённый колокольчик — акцентный, выключенный — цвет текста подвала.
        Assert.Contains("{DynamicResource AccentBrush}", toggleBlock);
        Assert.Contains("{DynamicResource SidebarTextBrush}", toggleBlock);

        // Тултип переключается вместе с состоянием: Off по умолчанию, On — в триггере.
        var tooltipOff = toggleBlock.IndexOf("LongOperation.Alarm.TooltipOff", StringComparison.Ordinal);
        var tooltipOn = toggleBlock.IndexOf("LongOperation.Alarm.TooltipOn", StringComparison.Ordinal);
        Assert.True(tooltipOff >= 0 && tooltipOn > tooltipOff,
            "Тултип переключателя: TooltipOff по умолчанию, TooltipOn — в DataTrigger включённого состояния");
    }

    [Fact]
    public void Avalonia_AlarmToggle_IsBuiltInStatusBar_NotInRightPanelActions()
    {
        var window = ReadSource("Workspace1C", "Views", "MainWindow.Avalonia.cs");
        var tree = ReadSource("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");

        var statusBar = ExtractMethod(window, "private Control BuildStatusBar()", "MainWindow.Avalonia.cs");
        Assert.Contains("BuildLongOperationAlarmToggle()", statusBar);
        Assert.Contains("ToggleSessionLaunchPanelCommand", statusBar);
        Assert.True(
            statusBar.IndexOf("BuildLongOperationAlarmToggle()", StringComparison.Ordinal) <
            statusBar.IndexOf("ToggleSessionLaunchPanelCommand", StringComparison.Ordinal),
            "В BuildStatusBar переключатель уведомлений должен добавляться перед кнопкой «Текущая сессия»");

        // Вызов ровно один: переключатель строится только в подвале.
        Assert.Equal(1, CountOccurrences(window, "BuildLongOperationAlarmToggle()"));

        var rightPanel = ExtractMethod(tree, "private Control BuildRightPanel()", "MainWindow.Avalonia.Tree.cs");
        Assert.DoesNotContain("BuildLongOperationAlarmToggle", rightPanel);
    }

    [Fact]
    public void Avalonia_AlarmToggle_UsesBellAndCrossLine()
    {
        var tree = ReadSource("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        var toggle = CollapseWhitespace(ExtractMethod(tree,
            "private static Control BuildLongOperationAlarmToggle()", "MainWindow.Avalonia.Tree.cs"));

        // Две копии колокольчика (включён/выключен) и линия перечёркивания.
        Assert.True(CountOccurrences(toggle, "Pack.BellOutline") >= 2,
            "BuildLongOperationAlarmToggle должен использовать Pack.BellOutline для обоих состояний");
        Assert.Contains("new Avalonia.Controls.Shapes.Line", toggle);
        Assert.Contains("cross.Bind(Visual.IsVisibleProperty", toggle);

        // Двусторонняя привязка к настройке и подсказки обоих состояний.
        Assert.Contains("BindingMode.TwoWay", toggle);
        Assert.Contains("LongOperationNotificationsEnabled", toggle);
        Assert.Contains("LongOperation.Alarm.TooltipOn", toggle);
        Assert.Contains("LongOperation.Alarm.TooltipOff", toggle);

        // Цвета состояний и управление видимостью всех трёх элементов через IsChecked.
        Assert.Contains("AccentBrush", toggle);
        Assert.Contains("SidebarTextBrush", toggle);
        Assert.True(CountOccurrences(toggle, "Visual.IsVisibleProperty") >= 3,
            "Видимость колокольчиков и перечёркивающей линии должна привязываться к IsChecked");
    }

    [Fact]
    public void BellOutlineIconKey_ExistsInBothThemeDictionaries()
    {
        // Ключ не начинается с Icon, поэтому IconSetCoverageTests его не покрывает —
        // проверяем непустую геометрию в обоих словарях базового набора.
        foreach (var file in new[] { "Icons.xaml", "Icons.axaml" })
        {
            var text = ReadSource("Workspace1C", "Themes", file);
            Assert.True(
                Regex.IsMatch(text, "<(?:Stream)?Geometry x:Key=\"Pack\\.BellOutline\">[^<]+"),
                $"{file}: нет непустой геометрии с ключом Pack.BellOutline");
        }
    }

    [Fact]
    public void Localization_AlarmTooltips_AreNonEmptyAndDifferInBothLanguages()
    {
        foreach (var language in new[] { "ru.json", "en.json" })
        {
            using var document = JsonDocument.Parse(
                ReadSource("Workspace1C", "Localization", "Languages", language));
            var strings = document.RootElement.GetProperty("strings");

            var on = strings.GetProperty("LongOperation.Alarm.TooltipOn").GetString();
            var off = strings.GetProperty("LongOperation.Alarm.TooltipOff").GetString();

            Assert.False(string.IsNullOrWhiteSpace(on), $"{language}: пустой LongOperation.Alarm.TooltipOn");
            Assert.False(string.IsNullOrWhiteSpace(off), $"{language}: пустой LongOperation.Alarm.TooltipOff");
            Assert.NotEqual(on, off);
        }
    }

    private static string CollapseWhitespace(string text) => Regex.Replace(text, @"\s+", " ");

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ExtractXamlElement(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"XAML: не найден маркер «{startMarker}»");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"XAML: не найден конец элемента «{endMarker}»");
        return text.Substring(start, end - start + endMarker.Length);
    }

    /// <summary>
    /// Вырезает тело метода по сигнатуре: окончание — первая закрывающая скобка
    /// на уровне членов класса (8 пробелов), вложенные блоки отступают глубже.
    /// </summary>
    private static string ExtractMethod(string source, string signature, string description)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{description}: не найдена сигнатура «{signature}»");
        const string endMarker = "\n        }";
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{description}: не найдено завершение метода");
        return source.Substring(start, end - start + endMarker.Length);
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
