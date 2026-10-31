#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Мастер «Развернуть тестовую копию» (п.10, 1.0.0.92, Avalonia/Linux): параметры тестовой
/// базы (имя, режим сервер/файл, сервер и СУБД, движок выгрузки, отключение регламентов,
/// заголовок/цвет, группа «Тест», расписание), предпросмотр плана и журнал прогресса.
/// Avalonia-версия WPF-окна <see cref="TestDeployWindow"/>. По успеху заполняет
/// <see cref="Scenario"/>/<see cref="DeployResult"/>/<see cref="CreatedGroup"/> и закрывается —
/// запись списка и группу применяет вызывающий (команда главного окна).
/// </summary>
public class TestDeployWindow : ModalWindowBase
{
    private readonly TestDeployViewModel _vm;
    private readonly IReadOnlyList<Group> _groups;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly TextBox _targetNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly RadioButton _serverRadio = new();
    private readonly RadioButton _fileRadio = new();
    private readonly StackPanel _serverPanel = new() { Spacing = 4 };
    private readonly StackPanel _filePanel = new() { Spacing = 4 };
    private readonly TextBox _serverBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _refBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly ComboBox _dbmsBox = new() { IsEditable = true };
    private readonly TextBox _dbServerBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _dbNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _dbUserBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly PasswordBox _dbPwdBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);
    private readonly CheckBox _createDbCheck = new() { IsChecked = true };
    private readonly TextBox _filePathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly ComboBox _engineCombo = new();
    private readonly CheckBox _disableJobsCheck = new() { IsChecked = true };
    private readonly CheckBox _changeTitleCheck = new() { IsChecked = true };
    private readonly CheckBox _changeColorCheck = new() { IsChecked = true };
    private readonly CheckBox _addToGroupCheck = new() { IsChecked = true };
    private readonly TextBox _groupNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _updateExistingCheck = new() { IsChecked = true };
    private readonly CheckBox _useScheduleCheck = new();
    private readonly TextBox _scheduleTimeBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox[] _dayChecks = new CheckBox[7];
    private readonly TextBlock _previewSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ItemsControl _previewSteps = new();
    private readonly ListBox _logList = new() { Height = 140 };
    private readonly Button _runButton = new();
    private readonly Button _closeButton = new();
    // Панель «Данные для выгрузки через ibcmd» (issue #79).
    private readonly StackPanel _ibcmdPanel = new() { Spacing = 4 };
    private readonly CheckBox _useInfobaseAuthCheck = new() { IsChecked = true };
    private readonly TextBox _ibcmdUserBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly PasswordBox _ibcmdPwdBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);
    private readonly Button _fillCredentialsButton = new();

    private bool _busy;

    /// <summary>Сценарий (заполняется и сохраняется при успешном развёртывании).</summary>
    public TestDeployScenario? Scenario { get; private set; }

    /// <summary>Результат развёртывания (заполняется при успехе).</summary>
    public TestDeployResult? DeployResult { get; private set; }

    /// <summary>Созданная группа «Тест» (null — группа уже была или добавление выключено).</summary>
    public Group? CreatedGroup { get; private set; }

    public TestDeployWindow(
        Infobase source,
        IReadOnlyList<Infobase> allInfobases,
        IReadOnlyList<Group> groups)
    {
        _groups = groups?.ToList() ?? new List<Group>();

        var proposedName = TestDeployPlanner.ProposeTargetName(source.Name);
        var proposedServer = source.Connection?.GetServerWithPort() ?? string.Empty;
        var proposedDb = source.Connection?.DatabaseName ?? string.Empty;
        var proposedDbRef = string.IsNullOrWhiteSpace(proposedDb)
            ? string.Empty
            : SuggestRef(proposedDb, allInfobases);
        var proposedFile = ProposedFileDirectory(source, proposedName);

        _vm = new TestDeployViewModel(
            source, allInfobases, _groups, proposedName, proposedServer, proposedDbRef, proposedFile);

        Title = LocalizationManager.T("TestDeploy.Title");
        Width = 720;
        Height = 700;
        MinWidth = 640;
        MinHeight = 620;
        FontSize = 13;
        Content = BuildRoot();
        // Автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере» (issue #79).
        _vm.AutofillDbNameFromServerDbName();
        if (!string.IsNullOrEmpty(_vm.DbName))
            _dbNameBox.Text = _vm.DbName;
        ApplyModePanels();
        ApplyScheduleState();
        ApplyIbcmdPanel();
    }

    private Control BuildRoot()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var fields = new StackPanel { Spacing = 8 };

        var sourceBlock = new TextBlock
        {
            Text = string.Format(LocalizationManager.T("TestDeploy.SourceFormat"), _vm.SourceDisplay),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        };
        fields.Children.Add(sourceBlock);

        // Действия по исходной базе (issue #79, 7OH: «иметь кнопки в окошке, чтобы админ
        // мог сам это сделать не выходя из этого окна»): свойства базы и Конфигуратор.
        var sourceActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var propsButton = new Button { Content = T("TestDeploy.OpenBaseProperties"), Height = 32 };
        propsButton.Styled(ControlThemes.SecondaryButton);
        ToolTip.SetTip(propsButton, T("TestDeploy.OpenBasePropertiesHint"));
        propsButton.Click += (_, _) => OpenBaseProperties();
        sourceActions.Children.Add(propsButton);
        var configButton = new Button { Content = T("TestDeploy.LaunchConfigurator"), Height = 32 };
        configButton.Styled(ControlThemes.SecondaryButton);
        ToolTip.SetTip(configButton, T("TestDeploy.LaunchConfiguratorHint"));
        configButton.Click += (_, _) => LaunchConfigurator();
        sourceActions.Children.Add(configButton);
        fields.Children.Add(sourceActions);

        fields.Children.Add(Field(T("TestDeploy.TargetName"), _targetNameBox));
        _targetNameBox.Text = _vm.TargetName;
        _targetNameBox.TextChanged += (_, _) => { _vm.TargetName = _targetNameBox.Text; RefreshPreview(); };

        // Режим: сервер / файл.
        _serverRadio.Content = T("TestDeploy.ModeServer");
        _fileRadio.Content = T("TestDeploy.ModeFile");
        _serverRadio.IsChecked = _vm.IsServerMode;
        _serverRadio.IsCheckedChanged += (_, _) => { _vm.IsServerMode = _serverRadio.IsChecked == true; ApplyModePanels(); };
        _fileRadio.IsCheckedChanged += (_, _) => { _vm.IsFileMode = _fileRadio.IsChecked == true; ApplyModePanels(); };
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        modeRow.Children.Add(_serverRadio);
        modeRow.Children.Add(_fileRadio);
        fields.Children.Add(Field(T("TestDeploy.Mode"), modeRow));

        // --- Серверная секция ---
        _serverPanel.Children.Add(Field(T("TestDeploy.Server"), _serverBox));
        _serverBox.Text = _vm.ServerAddress;
        _serverBox.TextChanged += (_, _) => _vm.ServerAddress = _serverBox.Text;

        _serverPanel.Children.Add(Field(T("TestDeploy.Ref"), _refBox));
        _refBox.Text = _vm.DatabaseName;
        _refBox.TextChanged += (_, _) =>
        {
            _vm.DatabaseName = _refBox.Text;
            // issue #79: «В поле "Имя базы данных СУБД" можно подставлять такое же имя,
            // как "Имя базы на сервере"» — пока СУБД-имя не заполнено, синхронизируем.
            if (string.IsNullOrWhiteSpace(_vm.DbName) && !string.IsNullOrWhiteSpace(_refBox.Text))
            {
                _vm.AutofillDbNameFromServerDbName();
                if (!string.IsNullOrEmpty(_vm.DbName))
                    _dbNameBox.Text = _vm.DbName;
            }
        };

        _dbmsBox.ItemsSource = new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" };
        _dbmsBox.SelectedItem = _vm.Dbms;
        _dbmsBox.SelectionChanged += (_, _) => _vm.Dbms = _dbmsBox.SelectedItem?.ToString() ?? _vm.Dbms;
        _serverPanel.Children.Add(Field(T("TestDeploy.Dbms"), _dbmsBox));

        _serverPanel.Children.Add(Field(T("TestDeploy.DbServer"), _dbServerBox));
        _dbServerBox.TextChanged += (_, _) => _vm.DbServer = _dbServerBox.Text;

        _serverPanel.Children.Add(Field(T("TestDeploy.DbName"), _dbNameBox));
        _dbNameBox.TextChanged += (_, _) => _vm.DbName = _dbNameBox.Text;

        _serverPanel.Children.Add(Field(T("TestDeploy.DbUser"), _dbUserBox));
        _dbUserBox.TextChanged += (_, _) => _vm.DbUser = _dbUserBox.Text;

        _serverPanel.Children.Add(Field(T("TestDeploy.DbPassword"), _dbPwdBox));
        _dbPwdBox.PasswordChanged += (_, _) => _vm.DbPassword = _dbPwdBox.Password;

        // issue #79 (1.0.1.9, 7OH: «Логичнее иметь кнопку "Подставить из профиля" … либо
        // данные из профиля, либо вручную. А выполнять работу по тому, что на форме»):
        // поля СУБД остаются редактируемыми; кнопка подставляет их из профиля исходной
        // базы (окно «Резервные копии СУБД») — по аналогии с «Подставить из базы».
        var fillDbmsProfileButton = new Button { Content = T("TestDeploy.FillFromDbmsProfile"), Height = 32 };
        fillDbmsProfileButton.Styled(ControlThemes.SecondaryButton);
        ToolTip.SetTip(fillDbmsProfileButton, T("TestDeploy.FillFromDbmsProfileHint"));
        fillDbmsProfileButton.Click += (_, _) =>
        {
            if (_vm.AutofillDbmsFromProfile())
            {
                _dbmsBox.SelectedItem = _vm.Dbms;
                _dbServerBox.Text = _vm.DbServer;
                _dbNameBox.Text = _vm.DbName;
                _dbUserBox.Text = _vm.DbUser;
                _dbPwdBox.Password = _vm.DbPassword;
            }
            else
            {
                _dialogs.ShowInfo(T("TestDeploy.NoDbmsProfile"), T("TestDeploy.Title"));
            }
        };
        _serverPanel.Children.Add(fillDbmsProfileButton);

        _createDbCheck.Content = T("TestDeploy.CreateDb");
        _createDbCheck.IsCheckedChanged += (_, _) => _vm.CreateSqlDatabase = _createDbCheck.IsChecked == true;
        _serverPanel.Children.Add(_createDbCheck);
        fields.Children.Add(_serverPanel);

        // --- Файловая секция ---
        _filePanel.Children.Add(Field(T("TestDeploy.FilePath"), _filePathBox));
        _filePathBox.Text = _vm.FilePath;
        _filePathBox.TextChanged += (_, _) => _vm.FilePath = _filePathBox.Text;
        fields.Children.Add(_filePanel);

        // --- Движок выгрузки ---
        _engineCombo.ItemsSource = new[]
        {
            new TestDeployEngineOption("Designer", T("TestDeploy.EngineDesigner")),
            new TestDeployEngineOption("Ibcmd", T("TestDeploy.EngineIbcmd"))
        };
        _engineCombo.SelectedIndex = _vm.EngineIndex;
        _engineCombo.SelectionChanged += (_, _) =>
        {
            _vm.EngineIndex = Math.Max(0, _engineCombo.SelectedIndex);
            ApplyIbcmdPanel();
        };
        fields.Children.Add(Field(T("TestDeploy.Engine"), _engineCombo));

        // --- Данные для выгрузки через ibcmd (issue #79) ---
        var ibcmdTitle = new TextBlock { Text = T("TestDeploy.IbcmdCredentials"), FontWeight = FontWeight.SemiBold };
        _ibcmdPanel.Children.Add(ibcmdTitle);
        _useInfobaseAuthCheck.Content = T("TestDeploy.UseInfobaseAuth");
        _useInfobaseAuthCheck.IsChecked = _vm.UseInfobaseAuth;
        _useInfobaseAuthCheck.IsCheckedChanged += (_, _) =>
        {
            _vm.UseInfobaseAuth = _useInfobaseAuthCheck.IsChecked == true;
            ApplyIbcmdPanel();
        };
        _ibcmdPanel.Children.Add(_useInfobaseAuthCheck);

        _ibcmdUserBox.Text = _vm.User;
        _ibcmdUserBox.TextChanged += (_, _) => _vm.User = _ibcmdUserBox.Text ?? string.Empty;
        _ibcmdPanel.Children.Add(Field(T("TestDeploy.CredentialsUser"), _ibcmdUserBox));

        _ibcmdPwdBox.PasswordChanged += (_, _) => _vm.Password = _ibcmdPwdBox.Password ?? string.Empty;
        _ibcmdPanel.Children.Add(Field(T("TestDeploy.CredentialsPassword"), _ibcmdPwdBox));

        _fillCredentialsButton.Content = T("TestDeploy.FillFromBase");
        _fillCredentialsButton.Height = 32;
        _fillCredentialsButton.Styled(ControlThemes.SecondaryButton);
        _fillCredentialsButton.Click += (_, _) =>
        {
            _vm.AutofillCredentialFromSource();
            _ibcmdUserBox.Text = _vm.User;
            _ibcmdPwdBox.Password = _vm.Password;
        };
        _ibcmdPanel.Children.Add(Wrap(_fillCredentialsButton));
        fields.Children.Add(_ibcmdPanel);

        // --- Опции ---
        _disableJobsCheck.Content = T("TestDeploy.DisableJobs");
        _disableJobsCheck.IsCheckedChanged += (_, _) => _vm.DisableScheduledJobs = _disableJobsCheck.IsChecked == true;
        fields.Children.Add(Field(string.Empty, Wrap(_disableJobsCheck)));

        _changeTitleCheck.Content = T("TestDeploy.ChangeTitle");
        _changeTitleCheck.IsCheckedChanged += (_, _) => _vm.ChangeTitle = _changeTitleCheck.IsChecked == true;
        fields.Children.Add(Field(string.Empty, Wrap(_changeTitleCheck)));

        _changeColorCheck.Content = T("TestDeploy.ChangeColor");
        _changeColorCheck.IsCheckedChanged += (_, _) => _vm.ChangeColor = _changeColorCheck.IsChecked == true;
        fields.Children.Add(Field(string.Empty, Wrap(_changeColorCheck)));

        _addToGroupCheck.Content = T("TestDeploy.AddToGroup");
        _addToGroupCheck.IsCheckedChanged += (_, _) => { _vm.AddToTestGroup = _addToGroupCheck.IsChecked == true; RefreshPreview(); };
        fields.Children.Add(Field(string.Empty, Wrap(_addToGroupCheck)));

        fields.Children.Add(Field(T("TestDeploy.GroupName"), _groupNameBox));
        _groupNameBox.Text = _vm.TestGroupName;
        _groupNameBox.TextChanged += (_, _) => _vm.TestGroupName = _groupNameBox.Text;

        _updateExistingCheck.Content = T("TestDeploy.UpdateExisting");
        _updateExistingCheck.IsCheckedChanged += (_, _) => _vm.UpdateExisting = _updateExistingCheck.IsChecked == true;
        fields.Children.Add(Field(string.Empty, Wrap(_updateExistingCheck)));

        // --- Расписание ---
        _useScheduleCheck.Content = T("TestDeploy.UseSchedule");
        _useScheduleCheck.IsCheckedChanged += (_, _) => { _vm.UseSchedule = _useScheduleCheck.IsChecked == true; ApplyScheduleState(); };
        fields.Children.Add(Field(string.Empty, Wrap(_useScheduleCheck)));

        fields.Children.Add(Field(T("TestDeploy.ScheduleTime"), _scheduleTimeBox));
        _scheduleTimeBox.Text = _vm.ScheduleTime;
        _scheduleTimeBox.TextChanged += (_, _) => _vm.ScheduleTime = _scheduleTimeBox.Text;

        var daysRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var dayNames = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var dayValues = new[] { _vm.Monday, _vm.Tuesday, _vm.Wednesday, _vm.Thursday, _vm.Friday, _vm.Saturday, _vm.Sunday };
        for (var i = 0; i < 7; i++)
        {
            var index = i;
            var check = new CheckBox { Content = dayNames[i], IsChecked = dayValues[i] };
            check.IsCheckedChanged += (_, _) => SetScheduleDay(index, check.IsChecked == true);
            _dayChecks[i] = check;
            daysRow.Children.Add(check);
        }
        fields.Children.Add(Field(T("TestDeploy.ScheduleDays"), daysRow));
        fields.Children.Add(new TextBlock
        {
            Text = T("TestDeploy.ScheduleHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        // --- Предпросмотр ---
        _previewSummary.Text = _vm.PreviewSummary;
        fields.Children.Add(_previewSummary);
        fields.Children.Add(_previewSteps);
        RefreshPreview();

        scroll.Content = fields;
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        // --- Журнал ---
        var logLabel = new TextBlock { Text = T("TestDeploy.Log"), Margin = new Thickness(0, 6, 0, 2) };
        Grid.SetRow(logLabel, 1);
        grid.Children.Add(logLabel);
        Grid.SetRow(_logList, 2);
        grid.Children.Add(_logList);

        // --- Кнопки ---
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _runButton.Content = T("TestDeploy.Run");
        _runButton.Width = 170;
        _runButton.Height = 36;
        _runButton.IsDefault = true;
        _runButton.Styled(ControlThemes.DialogConfirmButton);
        _runButton.Click += async (_, _) => await RunAsync();
        _closeButton.Content = T("Common.Close");
        _closeButton.Width = 110;
        _closeButton.Styled(ControlThemes.SecondaryButton);
        _closeButton.Click += (_, _) => Close();
        // issue #79 (7OH: «Можно внизу кнопку добавить, которая откроет окно нужное
        // прямо тут?»): общая авторизация — без выхода из мастера. Issue #79 (1.0.1.9,
        // 7OH: кнопка «Учётные данные ИБ для ibcmd…» — дубль и не нужна): кнопка
        // удалена — учётные данные ИБ задаются в блоке «Данные для выгрузки через
        // ibcmd» (кнопка «Подставить из базы») и в свойствах исходной базы (кнопка
        // вверху окна). Оставлены «Профиль СУБД для ibcmd…» и «Общая авторизация…».
        var dbmsProfileButton = new Button { Content = T("TestDeploy.OpenDbmsProfileWindow"), Width = 200, Height = 32 };
        dbmsProfileButton.Styled(ControlThemes.SecondaryButton);
        ToolTip.SetTip(dbmsProfileButton, T("TestDeploy.OpenDbmsProfileWindowHint"));
        dbmsProfileButton.Click += (_, _) => OpenDbmsProfile();
        buttons.Children.Add(dbmsProfileButton);
        var commonAuthButton = new Button { Content = T("TestDeploy.OpenCommonAuth"), Width = 175, Height = 32 };
        commonAuthButton.Styled(ControlThemes.SecondaryButton);
        ToolTip.SetTip(commonAuthButton, T("TestDeploy.OpenCommonAuthHint"));
        commonAuthButton.Click += (_, _) => OpenCommonAuthSettings();
        buttons.Children.Add(commonAuthButton);
        buttons.Children.Add(_runButton);
        buttons.Children.Add(_closeButton);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        return grid;
    }

    private void RefreshPreview()
    {
        _vm.RefreshPreview();
        _previewSummary.Text = _vm.PreviewSummary;
        _previewSteps.ItemsSource = _vm.PreviewSteps.ToList();
    }

    private void ApplyModePanels()
    {
        _serverPanel.IsVisible = _vm.IsServerMode;
        _filePanel.IsVisible = _vm.IsFileMode;
        RefreshPreview();
    }

    private void ApplyScheduleState()
    {
        _scheduleTimeBox.IsEnabled = _vm.UseSchedule;
        foreach (var check in _dayChecks)
            check.IsEnabled = _vm.UseSchedule;
    }

    /// <summary>
    /// Показывает панель «Данные для выгрузки через ibcmd» только для движка ibcmd
    /// (issue #79); поля логина/пароля активны только при выключенной авторизации ИБ.
    /// </summary>
    private void ApplyIbcmdPanel()
    {
        var visible = _engineCombo.SelectedIndex == 1;
        _ibcmdPanel.IsVisible = visible;
        var explicitAuth = !_vm.UseInfobaseAuth;
        _ibcmdUserBox.IsEnabled = explicitAuth;
        _ibcmdPwdBox.IsEnabled = explicitAuth;
        _fillCredentialsButton.IsEnabled = explicitAuth;
    }

    /// <summary>
    /// Открывает настройки приложения — раздел «Общая авторизация» (issue #79,
    /// 7OH: «пойти найти где это вводится?»), не закрывая мастер.
    /// </summary>
    private void OpenCommonAuthSettings()
    {
        if (Owner is MainWindow main && main.DataContext is MainViewModel mainVm)
        {
            var dialog = new SettingsWindow(mainVm);
            dialog.ShowDialogSync(this);
        }
    }

    private void SetScheduleDay(int index, bool value)
    {
        switch (index)
        {
            case 0: _vm.Monday = value; break;
            case 1: _vm.Tuesday = value; break;
            case 2: _vm.Wednesday = value; break;
            case 3: _vm.Thursday = value; break;
            case 4: _vm.Friday = value; break;
            case 5: _vm.Saturday = value; break;
            case 6: _vm.Sunday = value; break;
        }
    }

    private async Task RunAsync()
    {
        if (_busy)
            return;

        // Автосбор профиля СУБД ibcmd из формы (issue #79, коммент 8): если профиль
        // обязателен, а поля пусты — подставляем из сохранённого профиля автоматически.
        _vm.EnsureIbcmdProfileFromForm();

        var errors = _vm.Validate();
        if (errors.Count > 0)
        {
            _dialogs.ShowWarning(LocalizationManager.T(errors[0]), LocalizationManager.T("TestDeploy.Title"));
            return;
        }

        _busy = true;
        _runButton.IsEnabled = false;
        _logList.Items.Clear();
        _logList.Items.Add(LocalizationManager.T("TestDeploy.Started"));
        try
        {
            var scenario = _vm.BuildScenario();
            var service = AppServices.TryGetService<TestDeployService>() ?? new TestDeployService();
            var result = await service.DeployAsync(
                scenario, _vm.Source, _vm.AllInfobases, _groups, log: null);

            foreach (var line in result.Log)
                _logList.Items.Add(line);

            if (!result.Success)
            {
                _logList.Items.Add(result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed"));
                _dialogs.ShowError(result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed"),
                    LocalizationManager.T("TestDeploy.Title"));
                return;
            }

            foreach (var warning in result.Warnings)
                _logList.Items.Add(warning);

            TestDeployStore.Upsert(scenario);
            TestDeployScheduleHelper.RegisterTask(scenario);

            Scenario = scenario;
            DeployResult = result;
            if (result.GroupCreated)
            {
                var groupPlan = TestDeployPlanner.EnsureGroup(_groups, scenario);
                CreatedGroup = groupPlan.NewGroup;
            }

            _logList.Items.Add(LocalizationManager.T(
                result.TargetCreated ? "TestDeploy.DoneCreated" : "TestDeploy.DoneUpdated"));
            Close();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, LocalizationManager.T("TestDeploy.Title"));
        }
        finally
        {
            _busy = false;
            _runButton.IsEnabled = true;
        }
    }

    private static string SuggestRef(string sourceDb, IEnumerable<Infobase> allInfobases)
    {
        var seed = $"{sourceDb}_t";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ib in allInfobases)
        {
            var name = ib.Connection?.DatabaseName;
            if (!string.IsNullOrWhiteSpace(name))
                existing.Add(name);
        }
        var candidate = seed;
        var suffix = 2;
        while (existing.Contains(candidate))
        {
            candidate = $"{seed}{suffix}";
            suffix++;
        }
        return candidate;
    }

    private static string ProposedFileDirectory(Infobase source, string targetName)
    {
        try
        {
            var sourceDir = source.Connection?.FilePath;
            if (string.IsNullOrWhiteSpace(sourceDir))
                return string.Empty;
            var parent = System.IO.Path.GetDirectoryName(sourceDir.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(parent))
                return string.Empty;
            var folderName = targetName;
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
                folderName = folderName.Replace(invalid, '_');
            return System.IO.Path.Combine(parent, folderName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Control Wrap(Control control)
    {
        var row = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(control);
        return row;
    }

    /// <summary>
    /// Открывает окно свойств исходной базы (issue #79): «А нельзя брать из свойств базы
    /// разве? …иметь кнопки в окошке, чтобы админ сам мог это сделать не выходя из окна».
    /// </summary>
    private void OpenBaseProperties()
    {
        if (_vm.Source is not { } ib)
            return;
        var dialog = new ConnectionSettingsWindow(ib, _groups);
        dialog.ShowSync(this);
    }

    /// <summary>
    /// Открывает окно «Резервные копии СУБД» для исходной базы (issue #79, 1.0.1.7,
    /// 7OH «То есть я должен закрыть окно со всеми введёнными данными и пойти найти где
    /// это вводится?»): профиль СУБД нужен ibcmd для выгрузки клиент-серверной базы.
    /// </summary>
    private void OpenDbmsProfile()
    {
        if (_vm.Source is not { } ib)
            return;
        var vm = new DbmsBackupViewModel(_vm.AllInfobases, ib);
        var dialog = new DbmsBackupWindow(vm);
        dialog.ShowDialogSync(this);
    }

    /// <summary>Запускает Конфигуратор для исходной базы (issue #79).</summary>
    private void LaunchConfigurator()
    {
        if (_vm.Source is not { } ib)
            return;
        // Команда главного окна умеет запускать конфигуратор для переданной базы —
        // таким образом в мастере открывается та же база, что и из списка.
        if (Owner is MainWindow main && main.DataContext is MainViewModel mainVm
            && mainVm.LaunchConfiguratorCommand.CanExecute(ib))
        {
            mainVm.LaunchConfiguratorCommand.Execute(ib);
        }
    }

    private static Grid Field(string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(170)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        if (label.Length > 0)
        {
            var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            ThemeBrushes.Bind(labelBlock, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static string T(string key) => LocalizationManager.T(key);

}
#endif