using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;

namespace Workspace1C.ViewModels;

/// <summary>
/// Пункт выпадающего списка (тип задания / сценарий / база).
/// </summary>
public sealed class ScheduledTaskOption
{
    public ScheduledTaskOption(string value, string text)
    {
        Value = value;
        Text = text;
    }

    public string Value { get; }
    public string Text { get; }

    /// <summary>Отображение в выпадающих списках без явной привязки.</summary>
    public override string ToString() => Text;
}

/// <summary>
/// Форма редактирования задания по расписанию (issue #286): поля формы и валидация.
/// Чистый .NET, используется обеими платформами.
/// </summary>
public class ScheduledTaskEditViewModel : ViewModelBase
{
    private IReadOnlyList<BackupScenario> _scenarios;
    private readonly IReadOnlyList<Infobase> _infobases;

    // Списки опций кэшируются (issue #372): свойства KindOptions/NotifyOptions/… создавали
    // НОВЫЕ экземпляры при каждом обращении, из-за чего FindOption(...) возвращал пункт из
    // другого массива, чем ItemsSource, и SelectedItem не совпадал с элементами списка —
    // закрытый комбобокс показывал ключ вместо названия.
    private readonly IReadOnlyList<ScheduledTaskOption> _kindOptions = BuildKindOptions();
    private readonly IReadOnlyList<ScheduledTaskOption> _notifyOptions = BuildNotifyOptions();
    private readonly IReadOnlyList<ScheduledTaskOption> _repeatUnitOptions = BuildRepeatUnitOptions();
    private IReadOnlyList<ScheduledTaskOption> _scenarioOptions;
    private IReadOnlyList<ScheduledTaskOption> _testDeployOptions;
    private readonly IReadOnlyList<ScheduledTaskOption> _infobaseOptions;

    public ScheduledTaskEditViewModel(ScheduledTask? task,
        IReadOnlyList<BackupScenario> scenarios,
        IReadOnlyList<Infobase> infobases)
    {
        _scenarios = scenarios ?? Array.Empty<BackupScenario>();
        _infobases = infobases ?? Array.Empty<Infobase>();
        _scenarioOptions = _scenarios
            .Select(s => new ScheduledTaskOption(s.Id, s.Name))
            .ToList();
        // Сценарии «Развернуть тестовую копию» (п.10, 1.0.0.92): для типа TestDeploy
        // список «Сценарий» показывает их вместо сценариев резервирования.
        _testDeployOptions = TestDeployStore.Load()
            .Select(s => new ScheduledTaskOption(s.Id, ScenarioDisplayName(s)))
            .ToList();
        _infobaseOptions = _infobases
            .Select(b => new ScheduledTaskOption(b.Id, b.Name))
            .ToList();

        Name = task?.Name ?? "";
        Enabled = task?.Enabled ?? true;
        Kind = task?.Kind ?? ScheduledTaskKind.Backup;
        Time = string.IsNullOrWhiteSpace(task?.Time) ? "02:00" : task!.Time.Trim();
        ConfigFilePath = task?.ConfigFilePath ?? "";
        SelectedScenarioId = task?.ScenarioId ?? "";
        SelectedInfobaseId = task?.InfobaseId ?? "";
        PlatformCheckExpression = task?.PlatformCheckExpression ?? "";
        PlatformCheckIncludeTestVersions = task?.PlatformCheckIncludeTestVersions ?? false;
        PlatformNotifyChannel = task?.PlatformNotifyChannel ?? PlatformUpdateNotifyChannel.MainWindow;
        ConfigCheckConfiguration = task?.ConfigCheckConfiguration ?? "";
        ConfigCheckNotifyChannel = task?.ConfigCheckNotifyChannel ?? PlatformUpdateNotifyChannel.MainWindow;
        // issue #366/#367: периодичность повтора и «Уведомлять об отсутствии».
        RepeatEnabled = task?.RepeatEnabled ?? false;
        RepeatIntervalValueText = (task?.RepeatIntervalValue ?? 60).ToString();
        RepeatIntervalUnit = task?.RepeatIntervalUnit ?? ScheduledTaskRepeatUnit.Minutes;
        PlatformCheckNotifyWhenNoUpdates = task?.PlatformCheckNotifyWhenNoUpdates ?? false;
        ConfigCheckNotifyWhenNoUpdates = task?.ConfigCheckNotifyWhenNoUpdates ?? false;

        var days = task?.DaysOfWeek ?? new List<DayOfWeek>();
        IsMonday = days.Contains(DayOfWeek.Monday);
        IsTuesday = days.Contains(DayOfWeek.Tuesday);
        IsWednesday = days.Contains(DayOfWeek.Wednesday);
        IsThursday = days.Contains(DayOfWeek.Thursday);
        IsFriday = days.Contains(DayOfWeek.Friday);
        IsSaturday = days.Contains(DayOfWeek.Saturday);
        IsSunday = days.Contains(DayOfWeek.Sunday);
    }

    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ScheduledTaskKind Kind { get; set; } = ScheduledTaskKind.Backup;
    public string Time { get; set; } = "02:00";
    public string ConfigFilePath { get; set; } = "";
    public string SelectedScenarioId { get; set; } = "";
    public string SelectedInfobaseId { get; set; } = "";
    public string PlatformCheckExpression { get; set; } = "";
    public bool PlatformCheckIncludeTestVersions { get; set; }
    public PlatformUpdateNotifyChannel PlatformNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;
    public string ConfigCheckConfiguration { get; set; } = "";
    public PlatformUpdateNotifyChannel ConfigCheckNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;

    /// <summary>Включён ли повтор задания с интервалом (issue #366/#367).</summary>
    public bool RepeatEnabled { get; set; }

    /// <summary>Значение интервала повтора (текст поля; по умолчанию «60»).</summary>
    public string RepeatIntervalValueText { get; set; } = "60";

    /// <summary>Единица интервала повтора.</summary>
    public ScheduledTaskRepeatUnit RepeatIntervalUnit { get; set; } = ScheduledTaskRepeatUnit.Minutes;

    /// <summary>Уведомлять ли при отсутствии обновлений платформы (issue #366).</summary>
    public bool PlatformCheckNotifyWhenNoUpdates { get; set; }

    /// <summary>Уведомлять ли при отсутствии обновлений конфигурации (issue #367).</summary>
    public bool ConfigCheckNotifyWhenNoUpdates { get; set; }

    public bool IsMonday { get; set; }
    public bool IsTuesday { get; set; }
    public bool IsWednesday { get; set; }
    public bool IsThursday { get; set; }
    public bool IsFriday { get; set; }
    public bool IsSaturday { get; set; }
    public bool IsSunday { get; set; }

    /// <summary>Доступные типы задания для выпадающего списка (кэш — issue #372).</summary>
    public IReadOnlyList<ScheduledTaskOption> KindOptions => _kindOptions;

    /// <summary>Доступные каналы уведомления проверок платформы/конфигурации (issue #366/#367).</summary>
    public IReadOnlyList<ScheduledTaskOption> NotifyOptions => _notifyOptions;

    /// <summary>
    /// Имена конфигураций из списка баз для выбора в форме (issue #367): ComboBox
    /// позволяет выбрать из списка либо ввести имя вручную.
    /// </summary>
    public IReadOnlyList<string> ConfigurationOptions =>
        ConfigurationUpdateCheckLogic.ConfigurationNames(_infobases);

    /// <summary>
    /// Доступные сценарии (кэш — issue #372): сценарии резервирования для Backup/
    /// BackupThenUpdateConfig, сценарии «Развернуть тестовую копию» для TestDeploy
    /// (п.10, 1.0.0.92).
    /// </summary>
    public IReadOnlyList<ScheduledTaskOption> ScenarioOptions =>
        Kind == ScheduledTaskKind.TestDeploy ? _testDeployOptions : _scenarioOptions;

    /// <summary>Отображаемое имя сценария тест-копии (Name → TargetName → «Тест: <источник>»).</summary>
    private static string ScenarioDisplayName(TestDeployScenario s)
    {
        if (!string.IsNullOrWhiteSpace(s.Name))
            return s.Name.Trim();
        if (!string.IsNullOrWhiteSpace(s.TargetName))
            return s.TargetName.Trim();
        if (!string.IsNullOrWhiteSpace(s.SourceInfobaseName))
            return TestDeployPlanner.ProposeTargetName(s.SourceInfobaseName);
        return "(—)";
    }

    /// <summary>
    /// Обновляет список доступных сценариев после возврата из окна «Сценарии резервирования»
    /// (issue #292): вновь созданные/изменённые сценарии появляются в выборе без переоткрытия
    /// окна задания. Заодно перечитываются сценарии тест-копий (test_deploy.json).
    /// </summary>
    public void UpdateScenarios(IReadOnlyList<BackupScenario> scenarios)
    {
        _scenarios = scenarios ?? Array.Empty<BackupScenario>();
        _scenarioOptions = _scenarios
            .Select(s => new ScheduledTaskOption(s.Id, s.Name))
            .ToList();
        _testDeployOptions = TestDeployStore.Load()
            .Select(s => new ScheduledTaskOption(s.Id, ScenarioDisplayName(s)))
            .ToList();
    }

    /// <summary>Доступные информационные базы (кэш — issue #372).</summary>
    public IReadOnlyList<ScheduledTaskOption> InfobaseOptions => _infobaseOptions;

    /// <summary>
    /// Нужен ли выбор сценария: сценарий резервирования для Backup/BackupThenUpdateConfig
    /// или сценарий «Развернуть тестовую копию» (п.10, 1.0.0.92).
    /// </summary>
    public bool NeedsScenario => Kind is ScheduledTaskKind.Backup
        or ScheduledTaskKind.BackupThenUpdateConfig
        or ScheduledTaskKind.TestDeploy;

    /// <summary>
    /// Функция редакции ПРО, требуемая для типа задания (дефект №5 аудита активации,
    /// docs/pro/PRO-EDITION.md): тип TestDeploy — функция <see cref="ProFeature.TestDeploy"/>,
    /// выполняется только при активной лицензии (<see cref="SchedulerService"/> гейтит
    /// запуск, SchedulerService.cs). Остальные типы заданий входят в ядро 1.0.x — null.
    /// Гейт выбора/сохранения в окне задания (ScheduledTaskEditWindow, WPF и Avalonia)
    /// строится на этом маппинге + <see cref="ProGate.Ensure"/>, чтобы ПРО-задание
    /// нельзя было создать без лицензии (иначе оно падало бы при каждом запуске).
    /// </summary>
    public static ProFeature? RequiredProFeature(ScheduledTaskKind kind) =>
        kind == ScheduledTaskKind.TestDeploy ? ProFeature.TestDeploy : null;

    /// <summary>Нужен ли выбор базы (кроме обновления приложения).</summary>
    public bool NeedsInfobase => Kind is ScheduledTaskKind.Backup
        or ScheduledTaskKind.UpdateConfig
        or ScheduledTaskKind.BackupThenUpdateConfig
        or ScheduledTaskKind.CheckIntegrity
        or ScheduledTaskKind.InstallExtension
        or ScheduledTaskKind.GitCommitConfig
        or ScheduledTaskKind.DbmsBackup;

    /// <summary>Нужен ли путь к файлу .cf (для обновления конфигурации).</summary>
    public bool NeedsCfgFile => Kind is ScheduledTaskKind.UpdateConfig or ScheduledTaskKind.BackupThenUpdateConfig
        or ScheduledTaskKind.InstallExtension;

    /// <summary>Доступные единицы интервала повтора (issue #366/#367; кэш — issue #372).</summary>
    public IReadOnlyList<ScheduledTaskOption> RepeatUnitOptions => _repeatUnitOptions;

    /// <summary>
    /// Находит пункт списка по значению (issue #372). Окно использует результат для
    /// установки <c>SelectedItem</c> вместо <c>SelectedValuePath</c>/<c>SelectedValue</c>:
    /// закрытый ModernComboBox показывал ключ («Backup») вместо названия («Резервная копия»).
    /// null/пустое значение или отсутствие совпадения — null.
    /// </summary>
    public static ScheduledTaskOption? FindOption(IReadOnlyList<ScheduledTaskOption>? options, string? value)
        => options?.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal));

    private static IReadOnlyList<ScheduledTaskOption> BuildKindOptions()
    {
        var options = new[]
        {
            new ScheduledTaskOption("Backup", LocalizationManager.T("Schedule.Kind.Backup")),
            new ScheduledTaskOption("UpdateConfig", LocalizationManager.T("Schedule.Kind.UpdateConfig")),
            new ScheduledTaskOption("BackupThenUpdateConfig", LocalizationManager.T("Schedule.Kind.BackupThenUpdateConfig")),
            new ScheduledTaskOption("CheckIntegrity", LocalizationManager.T("Schedule.Kind.CheckIntegrity")),
            new ScheduledTaskOption("UpdateApp", LocalizationManager.T("Schedule.Kind.UpdateApp")),
            new ScheduledTaskOption("PlatformUpdateCheck", LocalizationManager.T("Schedule.Kind.PlatformUpdateCheck")),
            new ScheduledTaskOption("ConfigurationUpdateCheck", LocalizationManager.T("Schedule.Kind.ConfigurationUpdateCheck")),
            new ScheduledTaskOption("InstallExtension", LocalizationManager.T("Schedule.Kind.InstallExtension")),
            new ScheduledTaskOption("GitCommitConfig", LocalizationManager.T("Schedule.Kind.GitCommitConfig")),
            new ScheduledTaskOption("DbmsBackup", LocalizationManager.T("Schedule.Kind.DbmsBackup")),
            // Синхронизация своих настроек между компьютерами (п.9, 1.0.0.91).
            new ScheduledTaskOption("SettingsSync", LocalizationManager.T("Schedule.Kind.SettingsSync")),
            // Развёртывание тестовой копии (п.10, 1.0.0.92): «каждый понедельник
            // обновлять тест из прода» — сценарий TestDeployScenario + TestDeployService.
            new ScheduledTaskOption("TestDeploy", LocalizationManager.T("Schedule.Kind.TestDeploy"))
        };
#if DEBUG
        // Диагностика локализации опций (issue #372): язык и текст первой опции — чтобы
        // зафиксировать, что ключ («Backup») не подменяет название («Резервная копия»).
        if (ScheduleDebug.IsEnabled)
            ScheduleDebug.Log($"[BuildKindOptions] lang={LocalizationManager.Instance.CurrentLanguage}, " +
                $"T(Schedule.Kind.Backup)='{LocalizationManager.T("Schedule.Kind.Backup")}', " +
                $"first.Text='{options[0].Text}'");
#endif
        return options;
    }

    private static IReadOnlyList<ScheduledTaskOption> BuildNotifyOptions() => new[]
    {
        new ScheduledTaskOption("MainWindow", LocalizationManager.T("Schedule.PlatformCheck.Notify.MainWindow")),
        new ScheduledTaskOption("SystemNotifications", LocalizationManager.T("Schedule.PlatformCheck.Notify.System"))
    };

    private static IReadOnlyList<ScheduledTaskOption> BuildRepeatUnitOptions() => new[]
    {
        new ScheduledTaskOption("Seconds", LocalizationManager.T("Schedule.Repeat.Unit.Seconds")),
        new ScheduledTaskOption("Minutes", LocalizationManager.T("Schedule.Repeat.Unit.Minutes")),
        new ScheduledTaskOption("Hours", LocalizationManager.T("Schedule.Repeat.Unit.Hours")),
        new ScheduledTaskOption("Days", LocalizationManager.T("Schedule.Repeat.Unit.Days"))
    };

    /// <summary>Нужны ли опции проверки платформы (issue #366).</summary>
    public bool NeedsPlatformCheck => Kind is ScheduledTaskKind.PlatformUpdateCheck;

    /// <summary>Нужны ли опции проверки конфигурации (issue #367).</summary>
    public bool NeedsConfigCheck => Kind is ScheduledTaskKind.ConfigurationUpdateCheck;

    /// <summary>
    /// Нужна ли панель повтора с интервалом (issue #366/#367/#5): для заданий-проверок
    /// и «Обновить приложение» (issue #5) — повтор доступен, для остальных типов
    /// (резервная копия, обновление конфигурации, проверка целостности) — нет.
    /// Единый источник истины для окон WPF и Avalonia.
    /// </summary>
    public bool NeedsRepeat => Kind is ScheduledTaskKind.PlatformUpdateCheck
        or ScheduledTaskKind.ConfigurationUpdateCheck
        or ScheduledTaskKind.UpdateApp;

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Schedule.NameRequired";

        if (!TimeSpan.TryParse(Time, out _))
            return "Schedule.InvalidTime";

        switch (Kind)
        {
            case ScheduledTaskKind.Backup:
            case ScheduledTaskKind.BackupThenUpdateConfig:
            case ScheduledTaskKind.TestDeploy:
                if (string.IsNullOrWhiteSpace(SelectedScenarioId))
                    return "Schedule.ScenarioRequired";
                break;
            case ScheduledTaskKind.UpdateConfig:
                break;
        }

        if (NeedsInfobase && string.IsNullOrWhiteSpace(SelectedInfobaseId))
            return "Schedule.BaseRequired";

        if (NeedsCfgFile && string.IsNullOrWhiteSpace(ConfigFilePath))
            return "Schedule.CfgFileRequired";

        if (NeedsPlatformCheck && !PlatformUpdateCheckLogic.IsValidExpression(PlatformCheckExpression))
            return "Schedule.PlatformCheck.InvalidExpression";

        if (NeedsConfigCheck && string.IsNullOrWhiteSpace(ConfigCheckConfiguration))
            return "Schedule.ConfigCheck.ConfigurationRequired";

        // Периодичность (issue #366/#367): при включённом повторе интервал —
        // положительное целое число.
        if (RepeatEnabled &&
            (!int.TryParse(RepeatIntervalValueText, out var repeatValue) || repeatValue <= 0))
            return "Schedule.Repeat.InvalidValue";

        return null;
    }

    /// <summary>Переносит заполненные поля формы в задание.</summary>
    public void ApplyTo(ScheduledTask task)
    {
        task.Name = Name.Trim();
        task.Enabled = Enabled;
        task.Kind = Kind;
        task.Time = TimeSpan.TryParse(Time, out var t) ? t.ToString(@"hh\:mm") : "00:00";
        task.ScenarioId = string.IsNullOrWhiteSpace(SelectedScenarioId) ? null : SelectedScenarioId.Trim();
        task.InfobaseId = string.IsNullOrWhiteSpace(SelectedInfobaseId) ? null : SelectedInfobaseId.Trim();
        task.ConfigFilePath = string.IsNullOrWhiteSpace(ConfigFilePath) ? null : ConfigFilePath.Trim();
        task.PlatformCheckExpression = string.IsNullOrWhiteSpace(PlatformCheckExpression)
            ? null
            : PlatformCheckExpression.Trim();
        task.PlatformCheckIncludeTestVersions = PlatformCheckIncludeTestVersions;
        task.PlatformNotifyChannel = PlatformNotifyChannel;
        task.ConfigCheckConfiguration = string.IsNullOrWhiteSpace(ConfigCheckConfiguration)
            ? null
            : ConfigCheckConfiguration.Trim();
        task.ConfigCheckNotifyChannel = ConfigCheckNotifyChannel;
        task.DaysOfWeek = CollectDays();

        // Периодичность и «Уведомлять об отсутствии» (issue #366/#367).
        task.RepeatEnabled = RepeatEnabled;
        task.RepeatIntervalValue = int.TryParse(RepeatIntervalValueText, out var repeatValue) && repeatValue > 0
            ? repeatValue
            : task.RepeatIntervalValue;
        task.RepeatIntervalUnit = RepeatIntervalUnit;
        task.PlatformCheckNotifyWhenNoUpdates = PlatformCheckNotifyWhenNoUpdates;
        task.ConfigCheckNotifyWhenNoUpdates = ConfigCheckNotifyWhenNoUpdates;
    }

    private List<DayOfWeek> CollectDays()
    {
        var days = new List<DayOfWeek>();
        if (IsMonday) days.Add(DayOfWeek.Monday);
        if (IsTuesday) days.Add(DayOfWeek.Tuesday);
        if (IsWednesday) days.Add(DayOfWeek.Wednesday);
        if (IsThursday) days.Add(DayOfWeek.Thursday);
        if (IsFriday) days.Add(DayOfWeek.Friday);
        if (IsSaturday) days.Add(DayOfWeek.Saturday);
        if (IsSunday) days.Add(DayOfWeek.Sunday);
        return days;
    }
}