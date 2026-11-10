#if LINUX
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Форма задания по расписанию (Avalonia/Linux, issue #286): тип действия, время, дни недели,
/// сценарий резервирования, целевая база и файл конфигурации .cf.
/// Возвращает результат через свойство <see cref="Result"/> при подтверждении.
/// </summary>
public sealed class ScheduledTaskEditWindow : ModalWindowBase
{
    private readonly ScheduledTaskEditViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _enabledCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly ComboBox _kindCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _timeBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly StackPanel _daysPanel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly CheckBox[] _dayChecks = new CheckBox[7];
    private readonly StackPanel _scenarioPanel = new() { Spacing = 6 };
    private readonly ComboBox _scenarioCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly Button _scenariosButton = new Button { Width = 110 }.Styled(ControlThemes.SecondaryButton);
    private readonly StackPanel _infobasePanel = new() { Spacing = 6 };
    private readonly ComboBox _infobaseCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly StackPanel _cfgPanel = new() { Spacing = 6 };
    private readonly TextBox _cfgPathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly StackPanel _platformPanel = new() { Spacing = 6 };
    private readonly TextBox _platformExpressionBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _platformIncludeTestCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly ComboBox _platformNotifyCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly StackPanel _configPanel = new() { Spacing = 6 };
    private readonly ComboBox _configCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly ComboBox _configNotifyCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    // issue #366/#367: «Уведомлять об отсутствии» и периодичность повтора.
    private readonly CheckBox _platformNotifyNoUpdatesCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _configNotifyNoUpdatesCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly StackPanel _repeatPanel = new() { Spacing = 6 };
    private readonly CheckBox _repeatCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _repeatValueBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly ComboBox _repeatUnitCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);

    /// <summary>Готовое задание при подтверждении, иначе <c>null</c>.</summary>
    public ScheduledTask? Result { get; private set; }

    private readonly ScheduledTask? _sourceTask;

    /// <param name="task">Редактируемое задание или <c>null</c> для нового.</param>
    public ScheduledTaskEditWindow(ScheduledTask? task = null)
    {
        var scenarios = AppServices.GetRequiredService<IBackupScenarioStore>().LoadAll();
        var infobases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
        _vm = new ScheduledTaskEditViewModel(task, scenarios, infobases);
        // Исходное задание сохраняется: при «Изменить» правится оно само (Id остаётся
        // прежним и файл перезаписывается), а не создаётся дубль с новым GUID (issue #370).
        _sourceTask = task;

        Title = T(task is null ? "Schedule.AddTitle" : "Schedule.EditTitle");
        Width = 560;
        Height = 640;
        MinWidth = 500;
        MinHeight = 560;
        FontSize = 13;
        Content = BuildRoot();
        ApplyKindVisibility();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (task is null)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private static Control Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        // Цвет подписи из темы (issue #291), как в остальных окнах Avalonia.
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return label;
    }

    private Control BuildRoot()
    {
        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(Label(T("Schedule.Name")));
        _nameBox.Text = _vm.Name;
        panel.Children.Add(_nameBox);

        _enabledCheck.Content = T("Schedule.Enabled");
        _enabledCheck.IsChecked = _vm.Enabled;
        panel.Children.Add(_enabledCheck);

        panel.Children.Add(Label(T("Schedule.Kind")));
        _kindCombo.ItemsSource = _vm.KindOptions;
        ApplyOptionTemplate(_kindCombo);
        _kindCombo.SelectedItem = _vm.KindOptions.FirstOrDefault(o => o.Value == _vm.Kind.ToString());
        _kindCombo.SelectionChanged += (_, e) => OnKindChanged(e);
        _kindCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _kindCombo.MinWidth = 280;
        panel.Children.Add(_kindCombo);

        panel.Children.Add(Label(T("Schedule.Time")));
        _timeBox.Text = _vm.Time;
        panel.Children.Add(_timeBox);

        panel.Children.Add(Label(T("Schedule.Days")));
        BuildDayChecks();
        panel.Children.Add(_daysPanel);

        // Сценарий резервирования. Кнопка «Сценарии…» (issue #292) открывает окно списка
        // сценариев, чтобы посмотреть/создать сценарий, не закрывая окно задания.
        _scenarioCombo.ItemsSource = _vm.ScenarioOptions;
        ApplyOptionTemplate(_scenarioCombo);
        _scenarioCombo.SelectedItem = _vm.ScenarioOptions.FirstOrDefault(o => o.Value == _vm.SelectedScenarioId);
        _scenarioCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scenariosButton.Content = T("Schedule.Scenarios");
        _scenariosButton.Click += (_, _) => OpenScenarios();
        var scenarioRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_scenariosButton, Dock.Right);
        scenarioRow.Children.Add(_scenariosButton);
        scenarioRow.Children.Add(_scenarioCombo);
        _scenarioPanel.Children.Add(Label(T("Schedule.Scenario")));
        _scenarioPanel.Children.Add(scenarioRow);
        panel.Children.Add(_scenarioPanel);

        // Информационная база.
        _infobaseCombo.ItemsSource = _vm.InfobaseOptions;
        ApplyOptionTemplate(_infobaseCombo);
        _infobaseCombo.SelectedItem = _vm.InfobaseOptions.FirstOrDefault(o => o.Value == _vm.SelectedInfobaseId);
        _infobaseCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _infobasePanel.Children.Add(Label(T("Schedule.Base")));
        _infobasePanel.Children.Add(_infobaseCombo);
        panel.Children.Add(_infobasePanel);

        // Файл конфигурации .cf.
        _cfgPathBox.Text = _vm.ConfigFilePath;
        var browse = new Button { Content = T("Schedule.Browse"), Width = 100 }.Styled(ControlThemes.SecondaryButton);
        browse.Click += (_, _) => BrowseCfgFile();
        var cfgRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browse, Dock.Right);
        cfgRow.Children.Add(browse);
        cfgRow.Children.Add(_cfgPathBox);
        _cfgPanel.Children.Add(Label(T("Schedule.CfgFile")));
        _cfgPanel.Children.Add(cfgRow);
        panel.Children.Add(_cfgPanel);

        // Опции проверки обновления платформы (issue #366): выражение проверки,
        // учёт тестовых сборок и канал уведомления.
        _platformExpressionBox.Text = _vm.PlatformCheckExpression;
        _platformIncludeTestCheck.Content = T("Schedule.PlatformCheck.IncludeTest");
        _platformIncludeTestCheck.IsChecked = _vm.PlatformCheckIncludeTestVersions;
        _platformNotifyCombo.ItemsSource = _vm.NotifyOptions;
        ApplyOptionTemplate(_platformNotifyCombo);
        _platformNotifyCombo.SelectedItem = _vm.NotifyOptions
            .FirstOrDefault(o => o.Value == _vm.PlatformNotifyChannel.ToString());
        _platformNotifyCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _platformNotifyNoUpdatesCheck.Content = T("Schedule.PlatformCheck.NotifyWhenNoUpdates");
        _platformNotifyNoUpdatesCheck.IsChecked = _vm.PlatformCheckNotifyWhenNoUpdates;
        _platformPanel.Children.Add(Label(T("Schedule.PlatformCheck.Expression")));
        _platformPanel.Children.Add(_platformExpressionBox);
        _platformPanel.Children.Add(_platformIncludeTestCheck);
        _platformPanel.Children.Add(Label(T("Schedule.PlatformCheck.Notify")));
        _platformPanel.Children.Add(_platformNotifyCombo);
        _platformPanel.Children.Add(_platformNotifyNoUpdatesCheck);
        panel.Children.Add(_platformPanel);

        // Опции проверки обновления конфигурации (issue #367): имя конфигурации (выбор
        // из списка баз + ручной ввод) и канал уведомления. ComboBox редактируемый —
        // IsEditable выставляется отдельным оператором (после Styled() недопустим
        // объектный инициализатор); введённое/выбранное имя читается из Text.
        _configCombo.IsEditable = true;
        _configCombo.ItemsSource = _vm.ConfigurationOptions;
        _configCombo.Text = _vm.ConfigCheckConfiguration;
        _configCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _configNotifyCombo.ItemsSource = _vm.NotifyOptions;
        ApplyOptionTemplate(_configNotifyCombo);
        _configNotifyCombo.SelectedItem = _vm.NotifyOptions
            .FirstOrDefault(o => o.Value == _vm.ConfigCheckNotifyChannel.ToString());
        _configNotifyCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _configNotifyNoUpdatesCheck.Content = T("Schedule.ConfigCheck.NotifyWhenNoUpdates");
        _configNotifyNoUpdatesCheck.IsChecked = _vm.ConfigCheckNotifyWhenNoUpdates;
        _configPanel.Children.Add(Label(T("Schedule.ConfigCheck.Configuration")));
        _configPanel.Children.Add(_configCombo);
        _configPanel.Children.Add(Label(T("Schedule.PlatformCheck.Notify")));
        _configPanel.Children.Add(_configNotifyCombo);
        _configPanel.Children.Add(_configNotifyNoUpdatesCheck);
        panel.Children.Add(_configPanel);

        // Повтор с интервалом (issue #366/#367, 7OH): «каждые N единиц» — общий
        // для обоих типов заданий-проверок.
        _repeatCheck.Content = T("Schedule.Repeat.Enabled");
        _repeatCheck.IsChecked = _vm.RepeatEnabled;
        _repeatValueBox.Text = _vm.RepeatIntervalValueText;
        _repeatValueBox.Width = 80;
        _repeatValueBox.HorizontalAlignment = HorizontalAlignment.Left;
        _repeatUnitCombo.ItemsSource = _vm.RepeatUnitOptions;
        ApplyOptionTemplate(_repeatUnitCombo);
        _repeatUnitCombo.SelectedItem = _vm.RepeatUnitOptions
            .FirstOrDefault(o => o.Value == _vm.RepeatIntervalUnit.ToString());
        _repeatUnitCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _repeatUnitCombo.MinWidth = 110;
        var repeatRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 6)
        };
        repeatRow.Children.Add(new TextBlock
        {
            Text = T("Schedule.Repeat.ValueLabel"),
            VerticalAlignment = VerticalAlignment.Center
        });
        repeatRow.Children.Add(_repeatValueBox);
        repeatRow.Children.Add(_repeatUnitCombo);
        _repeatPanel.Children.Add(_repeatCheck);
        _repeatPanel.Children.Add(repeatRow);
        panel.Children.Add(_repeatPanel);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 110 }.Styled(ControlThemes.DialogConfirmButton);
        ok.Click += (_, _) => OkClicked();
        var cancel = new Button { Content = T("Common.Cancel"), Width = 100 }.Styled(ControlThemes.DialogCancelButton);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);

        // Кнопки зафиксированы внизу, поля прокручиваются (issue #366, 7OH:
        // «снизу кнопочки немного не влазят»).
        var root = new DockPanel { Margin = new Avalonia.Thickness(14) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
#if DEBUG
        // Диагностика отображения закрытых комбобоксов (issue #372): язык, значения
        // локализации и состояние выбранных пунктов — чтобы зафиксировать, почему поле
        // показывает ключ вместо названия. Пишется в schedule-debug.log при CM_SCHEDULE_DEBUG=1.
        LogOptionSelections("build");
#endif
        return root;
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
        LogCombo("Kind", _kindCombo);
        LogCombo("Scenario", _scenarioCombo);
        LogCombo("Infobase", _infobaseCombo);
        LogCombo("PlatformNotify", _platformNotifyCombo);
        LogCombo("ConfigNotify", _configNotifyCombo);
        LogCombo("RepeatUnit", _repeatUnitCombo);
    }

    private static void LogCombo(string name, ComboBox combo)
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

    private void BuildDayChecks()
    {
        var days = new[]
        {
            ("Monday", "Schedule.Days.Mon", DayOfWeek.Monday),
            ("Tuesday", "Schedule.Days.Tue", DayOfWeek.Tuesday),
            ("Wednesday", "Schedule.Days.Wed", DayOfWeek.Wednesday),
            ("Thursday", "Schedule.Days.Thu", DayOfWeek.Thursday),
            ("Friday", "Schedule.Days.Fri", DayOfWeek.Friday),
            ("Saturday", "Schedule.Days.Sat", DayOfWeek.Saturday),
            ("Sunday", "Schedule.Days.Sun", DayOfWeek.Sunday)
        };
        for (var i = 0; i < days.Length; i++)
        {
            // Флажки дней недели темизируются (issue #291), как и остальные элементы окна.
            var check = new CheckBox { Content = T(days[i].Item2) }.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsChecked = days[i].Item3 switch
            {
                DayOfWeek.Monday => _vm.IsMonday,
                DayOfWeek.Tuesday => _vm.IsTuesday,
                DayOfWeek.Wednesday => _vm.IsWednesday,
                DayOfWeek.Thursday => _vm.IsThursday,
                DayOfWeek.Friday => _vm.IsFriday,
                DayOfWeek.Saturday => _vm.IsSaturday,
                _ => _vm.IsSunday
            };
            _dayChecks[i] = check;
            _daysPanel.Children.Add(check);
        }
    }

    private ScheduledTaskKind SelectedKind
    {
        get
        {
            var value = (_kindCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "Backup";
            return Enum.TryParse<ScheduledTaskKind>(value, out var kind) ? kind : ScheduledTaskKind.Backup;
        }
    }

    /// <summary>
    /// Задаёт шаблон отображения пунктов списка (issue #372): один и тот же шаблон идёт
    /// и в раскрытый список, и в закрытую часть (<see cref="ComboBox.SelectionBoxItemTemplate"/>).
    /// Без него закрытый комбобокс рендерит выбранное значение через <c>ToString()</c>
    /// элемента — то есть ключ («Backup») вместо названия.
    /// </summary>
    private static void ApplyOptionTemplate(ComboBox combo)
    {
        var template = new FuncDataTemplate<ScheduledTaskOption>((item, _) =>
            new TextBlock { Text = item?.Text ?? string.Empty });
        combo.ItemTemplate = template;
        combo.SelectionBoxItemTemplate = template;
    }

    private void ApplyKindVisibility()
    {
        var kind = SelectedKind;
        _scenarioPanel.IsVisible = kind is ScheduledTaskKind.Backup
            or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.TestDeploy;
        _infobasePanel.IsVisible = kind is ScheduledTaskKind.Backup
            or ScheduledTaskKind.UpdateConfig
            or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.CheckIntegrity
            or ScheduledTaskKind.InstallExtension
            or ScheduledTaskKind.GitCommitConfig
            or ScheduledTaskKind.DbmsBackup;
        _cfgPanel.IsVisible = kind is ScheduledTaskKind.UpdateConfig or ScheduledTaskKind.BackupThenUpdateConfig
            or ScheduledTaskKind.InstallExtension;
        _platformPanel.IsVisible = kind is ScheduledTaskKind.PlatformUpdateCheck;
        _configPanel.IsVisible = kind is ScheduledTaskKind.ConfigurationUpdateCheck;
        // Кнопка «Сценарии…» открывает сценарии резервирования — для типа TestDeploy
        // сценарии выбираются в списке (их создаёт мастер «Развернуть тестовую копию»).
        _scenariosButton.IsVisible = kind != ScheduledTaskKind.TestDeploy;
        // Повтор с интервалом (issue #366/#367/#5): для заданий-проверок и «Обновить
        // приложение» (issue #5) — единый источник истины в ViewModel.
        _repeatPanel.IsVisible = _vm.NeedsRepeat;
    }

    /// <summary>Смена типа задания: обновляет видимость секций и список «Сценарий».</summary>
    private void OnKindChanged(SelectionChangedEventArgs? e = null)
    {
        // Дефект №5 (аудит активации): тип «Тестовый контур» (TestDeploy) — функция редакции
        // ПРО (docs/pro/PRO-EDITION.md). Без лицензии выбор запрещён: задание создалось бы,
        // но SchedulerService при каждом запуске отказывал (гейт при выполнении). При отказе
        // выбор откатывается к прежнему типу; окно «Лицензия» показывает ProGate.Ensure.
        var previousOption = e?.RemovedItems.OfType<ScheduledTaskOption>().FirstOrDefault();
        if (previousOption is not null &&
            previousOption.Value != nameof(ScheduledTaskKind.TestDeploy) &&
            SelectedKind == ScheduledTaskKind.TestDeploy &&
            !Workspace1C.Services.Licensing.ProGate.Ensure(
                ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy)!.Value))
        {
            _kindCombo.SelectedItem = previousOption;
            return;
        }

        ApplyKindVisibility();
        var selected = (_scenarioCombo.SelectedItem as ScheduledTaskOption)?.Value;
        var options = _vm.ScenarioOptions;
        if (options.Any(o => o.Value == selected))
        {
            _scenarioCombo.SelectedItem = options.FirstOrDefault(o => o.Value == selected);
        }
        else
        {
            _scenarioCombo.ItemsSource = options;
            _scenarioCombo.SelectedItem = options.FirstOrDefault(o => o.Value == _vm.SelectedScenarioId);
        }
    }

    private void BrowseCfgFile()
    {
        var path = _dialogs.OpenFileDialog(T("Schedule.CfgFile"), "*.cf");
        if (!string.IsNullOrWhiteSpace(path))
            _cfgPathBox.Text = path;
    }

    /// <summary>
    /// Открывает окно «Сценарии резервирования» (issue #292) модально относительно окна задания.
    /// После возврата список сценариев в выборе перечитывается, чтобы новые/изменённые
    /// сценарии стали доступны без переоткрытия окна.
    /// </summary>
    private void OpenScenarios()
    {
        var scenarios = new BackupScenariosWindow();
        scenarios.ShowDialogSync(this);
        ReloadScenarios();
    }

    /// <summary>Перечитывает сценарии после возврата из окна «Сценарии резервирования».</summary>
    private void ReloadScenarios()
    {
        var selected = (_scenarioCombo.SelectedItem as ScheduledTaskOption)?.Value;
        var scenarios = AppServices.GetRequiredService<IBackupScenarioStore>().LoadAll();
        _vm.UpdateScenarios(scenarios);

        _scenarioCombo.ItemsSource = _vm.ScenarioOptions;
        _scenarioCombo.SelectedItem = string.IsNullOrEmpty(selected) || !_vm.ScenarioOptions.Any(o => o.Value == selected)
            ? _vm.ScenarioOptions.FirstOrDefault(o => o.Value == _vm.SelectedScenarioId)
            : _vm.ScenarioOptions.FirstOrDefault(o => o.Value == selected);
    }

    private void OkClicked()
    {
        // Дефект №5 (аудит активации): сохранение задания типа TestDeploy без лицензии
        // запрещено — задание каждый запуск падало бы в SchedulerService. Окно остаётся
        // открытым, окно «Лицензии» показывает ProGate.Ensure.
        if (SelectedKind == ScheduledTaskKind.TestDeploy &&
            !Workspace1C.Services.Licensing.ProGate.Ensure(
                ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy)!.Value))
            return;

        _vm.Name = _nameBox.Text ?? "";
        _vm.Enabled = _enabledCheck.IsChecked == true;
        _vm.Kind = SelectedKind;
        _vm.Time = _timeBox.Text ?? "";
        _vm.ConfigFilePath = _cfgPathBox.Text ?? "";
        _vm.SelectedScenarioId = (_scenarioCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "";
        _vm.SelectedInfobaseId = (_infobaseCombo.SelectedItem as ScheduledTaskOption)?.Value ?? "";
        _vm.PlatformCheckExpression = _platformExpressionBox.Text ?? "";
        _vm.PlatformCheckIncludeTestVersions = _platformIncludeTestCheck.IsChecked == true;
        if (Enum.TryParse<PlatformUpdateNotifyChannel>(
                (_platformNotifyCombo.SelectedItem as ScheduledTaskOption)?.Value, out var notifyChannel))
            _vm.PlatformNotifyChannel = notifyChannel;
        // Для редактируемого ComboBox имя конфигурации читается из Text (issue #367).
        _vm.ConfigCheckConfiguration = _configCombo.Text ?? "";
        if (Enum.TryParse<PlatformUpdateNotifyChannel>(
                (_configNotifyCombo.SelectedItem as ScheduledTaskOption)?.Value, out var configNotifyChannel))
            _vm.ConfigCheckNotifyChannel = configNotifyChannel;
        // Повтор с интервалом и «Уведомлять об отсутствии» (issue #366/#367).
        _vm.RepeatEnabled = _repeatCheck.IsChecked == true;
        _vm.RepeatIntervalValueText = _repeatValueBox.Text ?? "";
        if (Enum.TryParse<ScheduledTaskRepeatUnit>(
                (_repeatUnitCombo.SelectedItem as ScheduledTaskOption)?.Value, out var repeatUnit))
            _vm.RepeatIntervalUnit = repeatUnit;
        _vm.PlatformCheckNotifyWhenNoUpdates = _platformNotifyNoUpdatesCheck.IsChecked == true;
        _vm.ConfigCheckNotifyWhenNoUpdates = _configNotifyNoUpdatesCheck.IsChecked == true;
        _vm.IsMonday = _dayChecks[0].IsChecked == true;
        _vm.IsTuesday = _dayChecks[1].IsChecked == true;
        _vm.IsWednesday = _dayChecks[2].IsChecked == true;
        _vm.IsThursday = _dayChecks[3].IsChecked == true;
        _vm.IsFriday = _dayChecks[4].IsChecked == true;
        _vm.IsSaturday = _dayChecks[5].IsChecked == true;
        _vm.IsSunday = _dayChecks[6].IsChecked == true;

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title ?? "");
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
        Close();
    }
}
#endif