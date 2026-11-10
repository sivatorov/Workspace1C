#if WINDOWS
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Microsoft.Win32;

namespace Workspace1C;

/// <summary>
/// Форма задания по расписанию (Windows/WPF, issue #286): тип действия, время, дни недели,
/// сценарий резервирования, целевая база и файл конфигурации .cf.
/// </summary>
public partial class ScheduledTaskEditWindow : Window
{
    private readonly ScheduledTaskEditViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly ScheduledTask? _sourceTask;

    /// <summary>Готовое задание при подтверждении, иначе <c>null</c>.</summary>
    public ScheduledTask? Result { get; private set; }

    /// <param name="task">Редактируемое задание или <c>null</c> для нового.</param>
    public ScheduledTaskEditWindow(ScheduledTask? task = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        // Исходное задание сохраняется: при «Изменить» правится оно само (Id остаётся
        // прежним и файл перезаписывается), а не создаётся дубль с новым GUID (issue #370).
        _sourceTask = task;

        var scenarios = AppServices.GetRequiredService<IBackupScenarioStore>().LoadAll();
        var infobases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
        _vm = new ScheduledTaskEditViewModel(task, scenarios, infobases);

        Title = T(task is null ? "Schedule.AddTitle" : "Schedule.EditTitle");
        OkButton.Content = T("Common.Save");
        BrowseButton.Content = T("Schedule.Browse");
        ScenariosButton.Content = T("Schedule.Scenarios");

        NameBox.Text = _vm.Name;
        EnabledCheck.IsChecked = _vm.Enabled;
        TimeBox.Text = _vm.Time;
        CfgPathBox.Text = _vm.ConfigFilePath;

        // Отображение элементов списка задаёт ItemTemplate в XAML (ScheduleOptionTemplate),
        // а выбор ставится через SelectedItem (issue #372): SelectedValuePath/SelectedValue
        // в связке с кастомным шаблоном ModernComboBox показывали в закрытом поле ключ
        // («Backup») вместо названия («Резервная копия»).
        KindCombo.ItemsSource = _vm.KindOptions;
        KindCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.KindOptions, _vm.Kind.ToString());

        ScenarioCombo.ItemsSource = _vm.ScenarioOptions;
        ScenarioCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.ScenarioOptions, _vm.SelectedScenarioId);

        InfobaseCombo.ItemsSource = _vm.InfobaseOptions;
        InfobaseCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.InfobaseOptions, _vm.SelectedInfobaseId);

        MonCheck.IsChecked = _vm.IsMonday;
        TueCheck.IsChecked = _vm.IsTuesday;
        WedCheck.IsChecked = _vm.IsWednesday;
        ThuCheck.IsChecked = _vm.IsThursday;
        FriCheck.IsChecked = _vm.IsFriday;
        SatCheck.IsChecked = _vm.IsSaturday;
        SunCheck.IsChecked = _vm.IsSunday;

        MonCheck.Content = T("Schedule.Days.Mon");
        TueCheck.Content = T("Schedule.Days.Tue");
        WedCheck.Content = T("Schedule.Days.Wed");
        ThuCheck.Content = T("Schedule.Days.Thu");
        FriCheck.Content = T("Schedule.Days.Fri");
        SatCheck.Content = T("Schedule.Days.Sat");
        SunCheck.Content = T("Schedule.Days.Sun");

        // Опции проверки обновления платформы (issue #366).
        PlatformExpressionLabel.Text = T("Schedule.PlatformCheck.Expression");
        PlatformExpressionBox.Text = _vm.PlatformCheckExpression;
        PlatformIncludeTestCheck.Content = T("Schedule.PlatformCheck.IncludeTest");
        PlatformIncludeTestCheck.IsChecked = _vm.PlatformCheckIncludeTestVersions;
        PlatformNotifyLabel.Text = T("Schedule.PlatformCheck.Notify");
        PlatformNotifyCombo.ItemsSource = _vm.NotifyOptions;
        PlatformNotifyCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.NotifyOptions, _vm.PlatformNotifyChannel.ToString());

        // Опции проверки обновления конфигурации (issue #367): ComboBox редактируемый —
        // можно выбрать имя из списка баз либо ввести вручную.
        ConfigLabel.Text = T("Schedule.ConfigCheck.Configuration");
        ConfigCombo.ItemsSource = _vm.ConfigurationOptions;
        ConfigCombo.Text = _vm.ConfigCheckConfiguration;
        ConfigNotifyLabel.Text = T("Schedule.PlatformCheck.Notify");
        ConfigNotifyCombo.ItemsSource = _vm.NotifyOptions;
        ConfigNotifyCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.NotifyOptions, _vm.ConfigCheckNotifyChannel.ToString());

        // Повтор с интервалом и «Уведомлять об отсутствии» (issue #366/#367).
        // Значение флага обязательно грузится из VM: раньше галочка «Повторять с
        // интервалом» не выставлялась при открытии задания — значения интервала
        // сохранялись, а флаг «терялся» (issue #366, комментарий 7OH от 2026-10-12).
        RepeatCheck.IsChecked = _vm.RepeatEnabled;
        RepeatCheck.Content = T("Schedule.Repeat.Enabled");
        RepeatValueLabel.Text = T("Schedule.Repeat.ValueLabel");
        RepeatValueBox.Text = _vm.RepeatIntervalValueText;
        RepeatUnitCombo.ItemsSource = _vm.RepeatUnitOptions;
        RepeatUnitCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.RepeatUnitOptions, _vm.RepeatIntervalUnit.ToString());
        PlatformNotifyNoUpdatesCheck.Content = T("Schedule.PlatformCheck.NotifyWhenNoUpdates");
        PlatformNotifyNoUpdatesCheck.IsChecked = _vm.PlatformCheckNotifyWhenNoUpdates;
        ConfigNotifyNoUpdatesCheck.Content = T("Schedule.ConfigCheck.NotifyWhenNoUpdates");
        ConfigNotifyNoUpdatesCheck.IsChecked = _vm.ConfigCheckNotifyWhenNoUpdates;

#if DEBUG
        // Диагностика отображения закрытых комбобоксов (issue #372): язык, значения
        // локализации и состояние выбранных пунктов — чтобы зафиксировать, почему поле
        // показывает ключ вместо названия. Пишется в schedule-debug.log при CM_SCHEDULE_DEBUG=1.
        LogOptionSelections("ctor");
#endif

        ApplyKindVisibility();

        // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
        // иначе при ShowDialog() фокус ставится до активации окна и «съедается».
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                NameBox.Focus();
                if (task is null)
                    NameBox.SelectAll();
            }));

            // Переустановка выбора ПОСЛЕ показа окна (issue #372): закрытый комбобокс
            // рисует выбранный элемент до применения шаблона и показывал ToString() —
            // ключ вместо названия. Повторный SelectedValue пересоздаёт контейнер
            // уже с применённым ItemTemplate (схема ConfigDiffSetupWindow #316,
            // ScriptScenarioEditWindow #308).
            RestoreOptionSelections();
        };
    }

    /// <summary>
    /// Переустанавливает выбранные значения списков после показа окна (issue #372):
    /// контейнер пересоздаётся с уже применённым <c>ItemTemplate</c>, поэтому закрытые
    /// комбобоксы показывают название, а не ключ.
    /// </summary>
    private void RestoreOptionSelections()
    {
        KindCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.KindOptions, _vm.Kind.ToString());
        ScenarioCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.ScenarioOptions, _vm.SelectedScenarioId);
        InfobaseCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.InfobaseOptions, _vm.SelectedInfobaseId);
        PlatformNotifyCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.NotifyOptions, _vm.PlatformNotifyChannel.ToString());
        ConfigNotifyCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.NotifyOptions, _vm.ConfigCheckNotifyChannel.ToString());
        RepeatUnitCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(_vm.RepeatUnitOptions, _vm.RepeatIntervalUnit.ToString());
#if DEBUG
        LogOptionSelections("restore");
#endif
        ApplyKindVisibility();
    }

#if DEBUG
    /// <summary>
    /// Диагностика отображения закрытых комбобоксов (issue #372): язык, значения локализации
    /// и состояние выбранных пунктов — чтобы зафиксировать, почему поле показывает ключ
    /// вместо названия. Пишется в schedule-debug.log при CM_SCHEDULE_DEBUG=1.
    /// </summary>
    private void LogOptionSelections(string stage)
    {
        if (!ScheduleDebug.IsEnabled)
            return;
        ScheduleDebug.Log($"[{stage}] lang={LocalizationManager.Instance.CurrentLanguage}, " +
            $"T(Schedule.Kind.Backup)='{T("Schedule.Kind.Backup")}'");
        LogCombo("Kind", KindCombo);
        LogCombo("Scenario", ScenarioCombo);
        LogCombo("Infobase", InfobaseCombo);
        LogCombo("PlatformNotify", PlatformNotifyCombo);
        LogCombo("ConfigNotify", ConfigNotifyCombo);
        LogCombo("RepeatUnit", RepeatUnitCombo);
    }

    private static void LogCombo(string name, System.Windows.Controls.ComboBox combo)
    {
        var selected = combo.SelectedItem;
        var text = selected is ScheduledTaskOption o
            ? $"Value='{o.Value}', Text='{o.Text}'"
            : selected?.ToString() ?? "null";
        var box = combo.SelectionBoxItem;
        ScheduleDebug.Log($"[{name}] SelectedItem type={selected?.GetType().Name ?? "null"}, {text}; " +
            $"SelectionBoxItem={box?.ToString() ?? "null"} (type={box?.GetType().Name ?? "null"})");
    }
#endif

    private static string T(string key) => LocalizationManager.T(key);

    private ScheduledTaskKind SelectedKind
    {
        get
        {
            var value = (KindCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "Backup";
            return Enum.TryParse<ScheduledTaskKind>(value, out var kind) ? kind : ScheduledTaskKind.Backup;
        }
    }

    /// <summary>Показывает секции, нужные для выбранного типа действия.</summary>
    private void ApplyKindVisibility()
    {
        var kind = SelectedKind;
        var needsScenario = kind is ScheduledTaskKind.Backup
            or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.TestDeploy;
        var needsInfobase = kind is ScheduledTaskKind.Backup
            or ScheduledTaskKind.UpdateConfig
            or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.CheckIntegrity
            or ScheduledTaskKind.InstallExtension
            or ScheduledTaskKind.GitCommitConfig
            or ScheduledTaskKind.DbmsBackup;
        var needsCfg = kind is ScheduledTaskKind.UpdateConfig or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.InstallExtension;
        var needsPlatformCheck = kind is ScheduledTaskKind.PlatformUpdateCheck;
        var needsConfigCheck = kind is ScheduledTaskKind.ConfigurationUpdateCheck;

        ScenarioPanel.Visibility = needsScenario ? Visibility.Visible : Visibility.Collapsed;
        // Кнопка «Сценарии…» открывает сценарии резервирования — для типа TestDeploy
        // сценарии выбираются в списке (они создаются мастером «Развернуть тестовую копию»).
        ScenariosButton.Visibility = kind == ScheduledTaskKind.TestDeploy ? Visibility.Collapsed : Visibility.Visible;
        InfobasePanel.Visibility = needsInfobase ? Visibility.Visible : Visibility.Collapsed;
        CfgPanel.Visibility = needsCfg ? Visibility.Visible : Visibility.Collapsed;
        PlatformPanel.Visibility = needsPlatformCheck ? Visibility.Visible : Visibility.Collapsed;
        ConfigPanel.Visibility = needsConfigCheck ? Visibility.Visible : Visibility.Collapsed;
        // Повтор с интервалом (issue #366/#367/#5): для заданий-проверок и «Обновить
        // приложение» (issue #5) — единый источник истины в ViewModel.
        RepeatPanel.Visibility = _vm.NeedsRepeat ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KindCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Дефект №5 (аудит активации): тип «Тестовый контур» (TestDeploy) — функция редакции
        // ПРО (docs/pro/PRO-EDITION.md). Без лицензии выбор запрещён: задание создалось бы,
        // но SchedulerService при каждом запуске отказывал (гейт при выполнении). При отказе
        // выбор откатывается к прежнему типу; окно «Лицензия» показывает ProGate.Ensure.
        var previousOption = e.RemovedItems.OfType<ScheduledTaskOption>().FirstOrDefault();
        if (previousOption is not null &&
            previousOption.Value != nameof(ScheduledTaskKind.TestDeploy) &&
            SelectedKind == ScheduledTaskKind.TestDeploy &&
            !Workspace1C.Services.Licensing.ProGate.Ensure(
                ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy)!.Value))
        {
            KindCombo.SelectedItem = previousOption;
            return;
        }

        ApplyKindVisibility();
        // Список «Сценарий» зависит от типа: сценарии резервирования или сценарии
        // «Развернуть тестовую копию» (п.10, 1.0.0.92).
        var selected = (ScenarioCombo.SelectedItem as ScheduledTaskOption)?.Value;
        var options = _vm.ScenarioOptions;
        if (options.Any(o => o.Value == selected))
        {
            ScenarioCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(options, selected);
        }
        else
        {
            ScenarioCombo.ItemsSource = options;
            ScenarioCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(options, _vm.SelectedScenarioId);
        }
    }

    /// <summary>
    /// Открывает окно «Сценарии резервирования» (issue #292), чтобы посмотреть или создать
    /// сценарий, не закрывая окно задания. После возврата список сценариев в выборе
    /// перечитывается.
    /// </summary>
    private void Scenarios_Click(object sender, RoutedEventArgs e)
    {
        var scenarios = new BackupScenariosWindow();
        // Модальность относительно окна задания (issue #291/#292): владелец — это окно,
        // иначе сценарии можно было бы редактировать параллельно с текущим заданием.
        scenarios.Owner = this;
        scenarios.ShowDialog();

        ReloadScenarios();
    }

    /// <summary>Перечитывает сценарии после возврата из окна «Сценарии резервирования».</summary>
    private void ReloadScenarios()
    {
        var selected = (ScenarioCombo.SelectedItem as ScheduledTaskOption)?.Value;
        var scenarios = AppServices.GetRequiredService<IBackupScenarioStore>().LoadAll();
        _vm.UpdateScenarios(scenarios);

        ScenarioCombo.ItemsSource = _vm.ScenarioOptions;
        ScenarioCombo.SelectedItem = ScheduledTaskEditViewModel.FindOption(
            _vm.ScenarioOptions,
            string.IsNullOrEmpty(selected) || !_vm.ScenarioOptions.Any(o => o.Value == selected)
                ? _vm.SelectedScenarioId
                : selected);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = T("Schedule.CfgFileRequired"),
            Filter = "Конфигурация 1С (*.cf)|*.cf|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            CfgPathBox.Text = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Дефект №5 (аудит активации): сохранение задания типа TestDeploy без лицензии
        // запрещено — задание каждый запуск падало бы в SchedulerService. Окно остаётся
        // открытым, окно «Лицензии» показывает ProGate.Ensure.
        if (SelectedKind == ScheduledTaskKind.TestDeploy &&
            !Workspace1C.Services.Licensing.ProGate.Ensure(
                ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy)!.Value))
            return;

        _vm.Name = NameBox.Text ?? "";
        _vm.Enabled = EnabledCheck.IsChecked == true;
        _vm.Time = TimeBox.Text ?? "";
        _vm.Kind = SelectedKind;
        _vm.ConfigFilePath = CfgPathBox.Text ?? "";
        _vm.SelectedScenarioId = (ScenarioCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "";
        _vm.SelectedInfobaseId = (InfobaseCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "";
        _vm.PlatformCheckExpression = PlatformExpressionBox.Text ?? "";
        _vm.PlatformCheckIncludeTestVersions = PlatformIncludeTestCheck.IsChecked == true;
        if (Enum.TryParse<PlatformUpdateNotifyChannel>((PlatformNotifyCombo.SelectedItem as ScheduledTaskOption)?.Value, out var notifyChannel))
            _vm.PlatformNotifyChannel = notifyChannel;
        // Для редактируемого ComboBox значение — введённый/выбранный текст (issue #367).
        _vm.ConfigCheckConfiguration = ConfigCombo.Text ?? "";
        if (Enum.TryParse<PlatformUpdateNotifyChannel>((ConfigNotifyCombo.SelectedItem as ScheduledTaskOption)?.Value, out var configNotifyChannel))
            _vm.ConfigCheckNotifyChannel = configNotifyChannel;
        // Повтор с интервалом и опции «Уведомлять об отсутствии» (issue #366/#367).
        _vm.RepeatEnabled = RepeatCheck.IsChecked == true;
        _vm.RepeatIntervalValueText = RepeatValueBox.Text ?? "";
        if (Enum.TryParse<ScheduledTaskRepeatUnit>((RepeatUnitCombo.SelectedItem as ScheduledTaskOption)?.Value, out var repeatUnit))
            _vm.RepeatIntervalUnit = repeatUnit;
        _vm.PlatformCheckNotifyWhenNoUpdates = PlatformNotifyNoUpdatesCheck.IsChecked == true;
        _vm.ConfigCheckNotifyWhenNoUpdates = ConfigNotifyNoUpdatesCheck.IsChecked == true;
        _vm.IsMonday = MonCheck.IsChecked == true;
        _vm.IsTuesday = TueCheck.IsChecked == true;
        _vm.IsWednesday = WedCheck.IsChecked == true;
        _vm.IsThursday = ThuCheck.IsChecked == true;
        _vm.IsFriday = FriCheck.IsChecked == true;
        _vm.IsSaturday = SatCheck.IsChecked == true;
        _vm.IsSunday = SunCheck.IsChecked == true;

        var error = _vm.Validate();
        if (error is not null)
        {
            _dialogs.ShowWarning(T(error), Title);
            return;
        }

        // При редактировании правится исходное задание (Id сохраняется — Store.Save
        // перезаписывает тот же файл), новый объект создаётся только для нового задания
        // (issue #370: раньше «Изменить» плодил дубль с новым GUID).
        if (_sourceTask is not null)
        {
            _vm.ApplyTo(_sourceTask);
            Result = _sourceTask;
        }
        else
        {
            var created = new ScheduledTask();
            _vm.ApplyTo(created);
            Result = created;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
#endif