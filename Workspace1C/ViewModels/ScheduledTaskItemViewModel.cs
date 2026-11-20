using System;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка списка заданий по расписанию: отображаемые поля и действия
/// (выполнить/изменить/удалить + переключение активности в строке, issue #372).
/// Чистый .NET, используется обеими платформами.
/// </summary>
public class ScheduledTaskItemViewModel : ViewModelBase
{
    private readonly Action<ScheduledTaskItemViewModel> _runAction;
    private readonly Action<ScheduledTaskItemViewModel> _editAction;
    private readonly Action<ScheduledTaskItemViewModel> _deleteAction;
    private readonly Action<ScheduledTaskItemViewModel> _activityAction;

    public ScheduledTaskItemViewModel(ScheduledTask task,
        Action<ScheduledTaskItemViewModel>? run = null,
        Action<ScheduledTaskItemViewModel>? edit = null,
        Action<ScheduledTaskItemViewModel>? delete = null,
        Action<ScheduledTaskItemViewModel>? activity = null)
    {
        Task = task;
        _runAction = run ?? (_ => { });
        _editAction = edit ?? (_ => { });
        _deleteAction = delete ?? (_ => { });
        _activityAction = activity ?? (_ => { });
    }

    /// <summary>Исходная модель задания.</summary>
    public ScheduledTask Task { get; }

    public string Id => Task.Id;
    public string Name => Task.Name;
    public string KindText => KindName(Task.Kind);

    /// <summary>
    /// Включено ли задание (issue #372, п. 6): переключается прямо из списка,
    /// изменение сразу сохраняется в хранилище и влияет на планировщик.
    /// </summary>
    public bool IsActive
    {
        get => Task.Enabled;
        set
        {
            if (Task.Enabled == value)
                return;
            Task.Enabled = value;
            OnPropertyChanged();
            _activityAction(this);
        }
    }

    /// <summary>
    /// Устанавливает активность модели без записи в хранилище (для инициализации
    /// интерактивных элементов строки до их создания).
    /// </summary>
    public void SetActive(bool value)
    {
        if (Task.Enabled == value)
            return;
        Task.Enabled = value;
        OnPropertyChanged(nameof(IsActive));
    }

    /// <summary>Краткое описание расписания: время + дни недели (+ повтор, issue #366/#372).</summary>
    public string ScheduleSummary => BuildScheduleSummary(Task);

    /// <summary>Статус последнего запуска (или «не выполнялось»).</summary>
    public string LastRunSummary
    {
        get
        {
            if (Task.LastRunAt is null)
                return LocalizationManager.T("Schedule.NeverRun");
            var status = Task.LastRunSuccess
                ? LocalizationManager.T("Schedule.LastRunOk")
                : LocalizationManager.T("Schedule.LastRunFailed");
            var message = string.IsNullOrWhiteSpace(Task.LastRunMessage) ? "" : " — " + Task.LastRunMessage;
            return $"{Task.LastRunAt:yyyy-MM-dd HH:mm} {status}{message}";
        }
    }

    public void Run() => _runAction(this);
    public void Edit() => _editAction(this);
    public void Delete() => _deleteAction(this);

    /// <summary>Человекочитаемое имя типа задания.</summary>
    public static string KindName(ScheduledTaskKind kind) => kind switch
    {
        ScheduledTaskKind.Backup => LocalizationManager.T("Schedule.Kind.Backup"),
        ScheduledTaskKind.UpdateConfig => LocalizationManager.T("Schedule.Kind.UpdateConfig"),
        ScheduledTaskKind.BackupThenUpdateConfig => LocalizationManager.T("Schedule.Kind.BackupThenUpdateConfig"),
        ScheduledTaskKind.CheckIntegrity => LocalizationManager.T("Schedule.Kind.CheckIntegrity"),
        ScheduledTaskKind.UpdateApp => LocalizationManager.T("Schedule.Kind.UpdateApp"),
        ScheduledTaskKind.PlatformUpdateCheck => LocalizationManager.T("Schedule.Kind.PlatformUpdateCheck"),
        ScheduledTaskKind.ConfigurationUpdateCheck => LocalizationManager.T("Schedule.Kind.ConfigurationUpdateCheck"),
        ScheduledTaskKind.InstallExtension => LocalizationManager.T("Schedule.Kind.InstallExtension"),
        ScheduledTaskKind.GitCommitConfig => LocalizationManager.T("Schedule.Kind.GitCommitConfig"),
        ScheduledTaskKind.DbmsBackup => LocalizationManager.T("Schedule.Kind.DbmsBackup"),
        ScheduledTaskKind.SettingsSync => LocalizationManager.T("Schedule.Kind.SettingsSync"),
        ScheduledTaskKind.TestDeploy => LocalizationManager.T("Schedule.Kind.TestDeploy"),
        ScheduledTaskKind.Pipeline => LocalizationManager.T("Schedule.Kind.Pipeline"),
        ScheduledTaskKind.AutoTests => LocalizationManager.T("Schedule.Kind.AutoTests"),
        ScheduledTaskKind.PerformanceSnapshot => LocalizationManager.T("Schedule.Kind.PerformanceSnapshot"),
        _ => LocalizationManager.T("Schedule.Kind.Backup")
    };

    /// <summary>Собирает «HH:mm, Пн, Ср, Пт» или «HH:mm, ежедневно» (+ «повтор каждые N …»).</summary>
    private static string BuildScheduleSummary(ScheduledTask task)
    {
        var time = string.IsNullOrWhiteSpace(task.Time) ? "00:00" : task.Time.Trim();
        var baseSummary = task.DaysOfWeek is not { Count: > 0 }
            ? time + ", " + LocalizationManager.T("Schedule.EveryDay")
            : time + ", " + string.Join(", ", task.DaysOfWeek.Select(DayName));
        return baseSummary + BuildRepeatSuffix(task);
    }

    /// <summary>
    /// Суффикс «повтор каждые N единиц» (issue #372, п. 7): показывается, когда
    /// для задания включён повтор с интервалом и значение интервала положительно.
    /// </summary>
    private static string BuildRepeatSuffix(ScheduledTask task)
    {
        if (!task.RepeatEnabled || task.RepeatIntervalValue <= 0)
            return "";

        var unitKey = task.RepeatIntervalUnit switch
        {
            ScheduledTaskRepeatUnit.Seconds => "Schedule.Repeat.Unit.Seconds.Forms",
            ScheduledTaskRepeatUnit.Hours => "Schedule.Repeat.Unit.Hours.Forms",
            ScheduledTaskRepeatUnit.Days => "Schedule.Repeat.Unit.Days.Forms",
            _ => "Schedule.Repeat.Unit.Minutes.Forms"
        };
        var forms = (LocalizationManager.T(unitKey) ?? "").Split(',');
        var unit = Pluralize(task.RepeatIntervalValue, forms, LocalizationManager.Instance.CurrentLanguage);
        return ", " + string.Format(LocalizationManager.T("Schedule.Repeat.Summary"), task.RepeatIntervalValue, unit);
    }

    /// <summary>
    /// Склонение единицы измерения: формы «один, несколько, много»
    /// (например «секунду,секунды,секунд»). Для русского — правила 1/2-4/5-20,
    /// для остальных языков — «1 → форма один, иначе → форма несколько».
    /// </summary>
    internal static string Pluralize(int value, string[] forms, string language)
    {
        if (forms.Length == 0)
            return "";
        var one = forms[0];
        var few = forms.Length > 1 ? forms[1] : one;
        var many = forms.Length > 2 ? forms[2] : few;

        if (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            var mod10 = value % 10;
            var mod100 = value % 100;
            if (mod10 == 1 && mod100 != 11)
                return one;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
                return few;
            return many;
        }

        return value == 1 ? one : few;
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => LocalizationManager.T("Schedule.Monday"),
        DayOfWeek.Tuesday => LocalizationManager.T("Schedule.Tuesday"),
        DayOfWeek.Wednesday => LocalizationManager.T("Schedule.Wednesday"),
        DayOfWeek.Thursday => LocalizationManager.T("Schedule.Thursday"),
        DayOfWeek.Friday => LocalizationManager.T("Schedule.Friday"),
        DayOfWeek.Saturday => LocalizationManager.T("Schedule.Saturday"),
        _ => LocalizationManager.T("Schedule.Sunday")
    };
}