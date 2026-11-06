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
    // «СУБД» — список выбора вместо свободного ввода (issue #79, v1.0.1.32).
    private readonly ComboBox _dbmsCombo = new();
    private IReadOnlyList<TestDeployDbmsOption> _dbmsOptions = TestDeployDbmsOption.BuildOptions(null, LocalizationManager.T);
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
    // Блок «Данные СУБД источника (вручную)» (issue #79, v1.0.1.29).
    private readonly CheckBox _sourceDbmsOverrideCheck = new();
    private readonly StackPanel _sourceDbmsPanel = new() { Spacing = 4 };
    private readonly ComboBox _sourceDbmsCombo = new() { IsEditable = true };
    private readonly TextBox _sourceDbServerBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _sourceDbNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _sourceDbUserBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly PasswordBox _sourceDbPwdBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);

    private bool _busy;

    /// <summary>Сценарий (заполняется и сохраняется при успешном развёртывании).</summary>
    public TestDeployScenario? Scenario { get; private set; }

    /// <summary>Результат развёртывания (заполняется при успехе).</summary>
    public TestDeployResult? DeployResult { get; private set; }

    /// <summary>База-источник развёртывания (для поиска открытого окна, issue #79).</summary>
    public Infobase Source => _vm.Source;

    /// <summary>Идёт ли развёртывание прямо сейчас (немодальный показ, issue #79).</summary>
    public bool DeployRunning => _busy;

    /// <summary>
    /// Успешное завершение развёртывания (issue #79: окно показывается НЕМОДАЛЬНО,
    /// по аналогии с «Серверы 1С»). Подписчик (MainViewModel) применяет результат:
    /// добавляет/обновляет тестовую базу в списке. Вызывается после заполнения
    /// <see cref="Scenario"/>/<see cref="DeployResult"/>, до закрытия окна.
    /// </summary>
    public event Action<TestDeployWindow>? DeployCompleted;

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

        // Issue #79 (комментарий 7OH 2026-11-05T11:22): «Просьба сохранять значения окна:
        // Движок выгрузки, Сервер СУБД, Пользователь СУБД» — значения восстанавливаются
        // из последнего сценария этой исходной базы (test_deploy.json); сбой хранилища
        // окно не блокирует.
        try
        {
            _vm.ApplyRememberedWindowValues(TestDeployStore.FindLastForSource(source.Id));
        }
        catch
        {
            // Хранилище сценариев недоступно — окно открывается со значениями по умолчанию.
        }

        Title = LocalizationManager.T("TestDeploy.Title");
        Width = 720;
        Height = 700;
        MinWidth = 640;
        MinHeight = 620;
        FontSize = 13;
        Content = BuildRoot();
        // Автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере» (issue #79).
        // Issue #79 (v1.0.1.23): существующий ввод в поле (имя SQL-базы источника,
        // «понятное» pre-валидации) автоподстановка целевой базы не затирает.
        _vm.AutofillDbNameFromServerDbName();
        if (!string.IsNullOrEmpty(_vm.DbName) && string.IsNullOrWhiteSpace(_dbNameBox.Text))
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

        // Режим: сервер / файл + «Движок выгрузки» в той же строке (issue #79,
        // v1.0.1.32, раскладка 7OH п.1; подпись TestDeploy.Engine сохранена).
        _serverRadio.Content = T("TestDeploy.ModeServer");
        _fileRadio.Content = T("TestDeploy.ModeFile");
        _serverRadio.IsChecked = _vm.IsServerMode;
        _serverRadio.IsCheckedChanged += (_, _) => { _vm.IsServerMode = _serverRadio.IsChecked == true; ApplyModePanels(); };
        _fileRadio.IsCheckedChanged += (_, _) => { _vm.IsFileMode = _fileRadio.IsChecked == true; ApplyModePanels(); };
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        modeRow.Children.Add(_serverRadio);
        modeRow.Children.Add(_fileRadio);
        var engineLabel = new TextBlock { Text = T("TestDeploy.Engine"), VerticalAlignment = VerticalAlignment.Center };
        ThemeBrushes.Bind(engineLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
        modeRow.Children.Add(engineLabel);
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
        modeRow.Children.Add(_engineCombo);
        fields.Children.Add(Field(T("TestDeploy.Mode"), modeRow));

        // --- Серверная секция (раскладка 7OH, issue #79, v1.0.1.32) ---
        // П.2: «Сервер 1С» и «Имя базы на сервере» — в одной строке.
        _serverBox.Text = _vm.ServerAddress;
        _serverBox.TextChanged += (_, _) => _vm.ServerAddress = _serverBox.Text;
        _refBox.Text = _vm.DatabaseName;
        _serverPanel.Children.Add(FieldsRow(
            130, (T("TestDeploy.Server"), _serverBox), (T("TestDeploy.Ref"), _refBox)));
        _refBox.TextChanged += (_, _) =>
        {
            _vm.DatabaseName = _refBox.Text;
            // issue #79: «В поле "Имя базы данных СУБД" можно подставлять такое же имя,
            // как "Имя базы на сервере"» — пока СУБД-имя не заполнено, синхронизируем.
            // Issue #79 (v1.0.1.23): ручной ввод пользователя не затирается — если в
            // поле уже есть значение (например, имя SQL-базы ИСХОДНОЙ базы), автоподстановка
            // имени целевой базы не выполняется.
            if (string.IsNullOrWhiteSpace(_vm.DbName) && !string.IsNullOrWhiteSpace(_refBox.Text)
                && string.IsNullOrWhiteSpace(_dbNameBox.Text))
            {
                _vm.AutofillDbNameFromServerDbName();
                if (!string.IsNullOrEmpty(_vm.DbName))
                    _dbNameBox.Text = _vm.DbName;
            }
        };

        // П.3: «СУБД» (список выбора вместо свободного ввода — issue #79) и
        // «Сервер СУБД» — в одной строке.
        _dbmsOptions = TestDeployDbmsOption.BuildOptions(_vm.Dbms, T);
        _dbmsCombo.ItemsSource = _dbmsOptions;
        SelectDbmsComboItem(_vm.Dbms);
        _dbmsCombo.SelectionChanged += (_, _) =>
            _vm.Dbms = (_dbmsCombo.SelectedItem as TestDeployDbmsOption)?.Value ?? _vm.Dbms;
        _dbServerBox.TextChanged += (_, _) => _vm.DbServer = _dbServerBox.Text;
        _serverPanel.Children.Add(FieldsRow(
            100, (T("TestDeploy.Dbms"), _dbmsCombo), (T("TestDeploy.DbServer"), _dbServerBox)));

        // П.4: «Имя базы данных в СУБД», «Пользователь СУБД» и «Пароль СУБД» — в одной строке.
        _dbNameBox.TextChanged += (_, _) => _vm.DbName = _dbNameBox.Text;
        _dbUserBox.TextChanged += (_, _) => _vm.DbUser = _dbUserBox.Text;
        // Восстановленные значения окна «Сервер СУБД»/«Пользователь СУБД» (issue #79,
        // комментарий 7OH 2026-11-05T11:22) — в поля формы при открытии мастера.
        if (!string.IsNullOrEmpty(_vm.DbServer))
            _dbServerBox.Text = _vm.DbServer;
        if (!string.IsNullOrEmpty(_vm.DbUser))
            _dbUserBox.Text = _vm.DbUser;
        _dbPwdBox.PasswordChanged += (_, _) => _vm.DbPassword = _dbPwdBox.Password;
        _serverPanel.Children.Add(FieldsRow(
            90, (T("TestDeploy.DbName"), _dbNameBox), (T("TestDeploy.DbUser"), _dbUserBox),
            (T("TestDeploy.DbPassword"), _dbPwdBox)));

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
                SelectDbmsComboItem(_vm.Dbms);
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

        // «Движок выгрузки» перенесён в строку с переключателем типа источника
        // (issue #79, v1.0.1.32, раскладка 7OH п.1) — здесь блока больше нет.

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

        // --- Данные СУБД ИСТОЧНИКА (вручную) (issue #79, v1.0.1.29): rac 8.5.4 отдаёт
        // пустые свойства СУБД, «infobase info» — «Недостаточно прав» — пользователь
        // вводит данные сам, профиль строится напрямую, без rac-проб и эвристики. ---
        _sourceDbmsOverrideCheck.Content = T("TestDeploy.SourceDbmsOverride");
        ToolTip.SetTip(_sourceDbmsOverrideCheck, T("TestDeploy.SourceDbmsOverrideHint"));
        _sourceDbmsOverrideCheck.IsCheckedChanged += (_, _) =>
        {
            _vm.UseSourceDbmsOverride = _sourceDbmsOverrideCheck.IsChecked == true;
            if (_vm.UseSourceDbmsOverride)
            {
                _vm.FillSourceDbmsOverrideDefaults();
                _sourceDbmsCombo.SelectedItem = string.IsNullOrWhiteSpace(_vm.SourceDbms)
                    ? "MSSQLServer" : _vm.SourceDbms;
                _sourceDbServerBox.Text = _vm.SourceDbServer;
                _sourceDbNameBox.Text = _vm.SourceDbName;
                _sourceDbUserBox.Text = _vm.SourceDbUser;
            }
            ApplyIbcmdPanel();
        };
        _ibcmdPanel.Children.Add(_sourceDbmsOverrideCheck);

        // issue #79 (v1.0.1.33): состав списка выровнен по четырём типам, реально
        // поддерживаемым 1С (как и ComboBox «СУБД» выше) — на обеих платформах.
        _sourceDbmsCombo.ItemsSource = new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" };
        _sourceDbmsCombo.SelectedItem = string.IsNullOrWhiteSpace(_vm.SourceDbms)
            ? "MSSQLServer" : _vm.SourceDbms;
        _sourceDbmsCombo.SelectionChanged += (_, _) =>
            _vm.SourceDbms = _sourceDbmsCombo.SelectedItem?.ToString() ?? _vm.SourceDbms;
        _sourceDbmsPanel.Children.Add(Field(T("TestDeploy.SourceDbms"), _sourceDbmsCombo));

        _sourceDbServerBox.TextChanged += (_, _) => _vm.SourceDbServer = _sourceDbServerBox.Text ?? string.Empty;
        _sourceDbmsPanel.Children.Add(Field(T("TestDeploy.SourceDbServer"), _sourceDbServerBox));

        _sourceDbNameBox.TextChanged += (_, _) => _vm.SourceDbName = _sourceDbNameBox.Text ?? string.Empty;
        _sourceDbmsPanel.Children.Add(Field(T("TestDeploy.SourceDbName"), _sourceDbNameBox));

        _sourceDbUserBox.TextChanged += (_, _) => _vm.SourceDbUser = _sourceDbUserBox.Text ?? string.Empty;
        _sourceDbmsPanel.Children.Add(Field(T("TestDeploy.SourceDbUser"), _sourceDbUserBox));

        _sourceDbPwdBox.PasswordChanged += (_, _) => _vm.SourceDbPassword = _sourceDbPwdBox.Password ?? string.Empty;
        _sourceDbmsPanel.Children.Add(Field(T("TestDeploy.SourceDbPassword"), _sourceDbPwdBox));

        _ibcmdPanel.Children.Add(_sourceDbmsPanel);
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
        // Issue #79 (v1.0.1.29): поля «Данные СУБД источника» — только при включённой галочке.
        _sourceDbmsPanel.IsVisible = _vm.UseSourceDbmsOverride;
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

        // Issue #79 (v1.0.1.23): pre-валидация асинхронная, с rac-веткой — если профиля
        // СУБД нет и поля формы не описывают источник, выполняется лёгкая rac-проба,
        // и её шаги журналируются в окно мастера. Журнал очищается ДО валидации, чтобы
        // строки пробы не пропали после старта сценария.
        _logList.Items.Clear();
        _logList.Items.Add(LocalizationManager.T("TestDeploy.Started"));

        // Коллбэк журнала приходит и из фоновых потоков rac-пробы — marshal на UI-поток.
        void AddPreValidationLog(string line)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _logList.Items.Add(line);
                _logList.ScrollIntoView(line);
            });
        }

        // Валидация формы (понятная ошибка до старта; rac-проба перед ошибкой).
        var errors = await _vm.ValidateAsync(log: AddPreValidationLog);
        if (errors.Count > 0)
        {
            _dialogs.ShowWarning(LocalizationManager.T(errors[0]), LocalizationManager.T("TestDeploy.Title"));
            return;
        }

        _busy = true;
        _runButton.IsEnabled = false;
        try
        {
            var scenario = _vm.BuildScenario();
            var service = AppServices.TryGetService<TestDeployService>() ?? new TestDeployService();

            // Issue #79 (репорт 7OH «после ОК ничего не происходит, в логах тихо»):
            // ранее log был null — шаги резолва СУБД (до 60 с rac-проб) не отображались
            // и сценарий выглядел зависшим. Прогресс пишется в журнал окна в реальном
            // времени; добавление — строго в UI-потоке (коллбэк приходит из фоновых
            // потоков DeployAsync).
            void AddLogLine(string line)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _logList.Items.Add(line);
                    _logList.ScrollIntoView(line);
                });
            }

            var result = await service.DeployAsync(
                scenario, _vm.Source, _vm.AllInfobases, _groups, log: AddLogLine);

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

            // Issue #79 (v1.0.1.40, diagnostics 7OH 2026-11-06): сбой финализации
            // (файл сценариев/планировщик) происходил ПОСЛЕ успешного развёртывания
            // и показывался как ошибка всего процесса. Теперь он не роняет результат:
            // тип и стек пишутся в журнал окна, пользователю показывается
            // предупреждение, окно не закрывается — сценарий видно.
            try
            {
                TestDeployStore.Upsert(scenario);
                TestDeployScheduleHelper.RegisterTask(scenario);
            }
            catch (Exception finalizeEx)
            {
                var finalizeMessage = string.Format(
                    LocalizationManager.T("TestDeploy.FinalizeFailed"),
                    finalizeEx.GetType().FullName, finalizeEx.Message);
                _logList.Items.Add(finalizeMessage);
                if (!string.IsNullOrWhiteSpace(finalizeEx.StackTrace))
                    _logList.Items.Add(finalizeEx.StackTrace);
                _logList.ScrollIntoView(finalizeMessage);
                _dialogs.ShowWarning(finalizeMessage, LocalizationManager.T("TestDeploy.Title"));
                return;
            }

            Scenario = scenario;
            DeployResult = result;
            if (result.GroupCreated)
            {
                var groupPlan = TestDeployPlanner.EnsureGroup(_groups, scenario);
                CreatedGroup = groupPlan.NewGroup;
            }

            // Issue #79 (комментарий 7OH 2026-11-05): ключ DoneCreated/DoneUpdated содержит
            // {0} — имя тестовой базы; раньше подставлялось без string.Format, и в журнале
            // окна оставался не заполненный плейсхолдер.
            _logList.Items.Add(TestDeployService.DoneMessage(result));

            // Немодальное окно (issue #79): подписчик применяет результат до закрытия.
            DeployCompleted?.Invoke(this);

            Close();
        }
        catch (Exception ex)
        {
            // Issue #79 (v1.0.1.40): в журнал окна пишутся тип и стек — «голое»
            // сообщение («Внутренняя ошибка…») в ZIP-диагностике ничего не даёт.
            _logList.Items.Add(ex.GetType().FullName + ": " + ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                _logList.Items.Add(ex.StackTrace);
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
    /// Строка раскладки мастера (issue #79, v1.0.1.32, раскладка 7OH): несколько пар
    /// «подпись — поле» в одном ряду. Подпись — колонка фиксированной ширины
    /// (<paramref name="labelWidth"/>), поле — звездой. Подписи длинные («Имя базы
    /// данных в СУБД»), поэтому для строки из трёх пар ширина меньше — текст переносится.
    /// </summary>
    private static Grid FieldsRow(int labelWidth, params (string Label, Control Field)[] pairs)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < pairs.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(labelWidth)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var labelBlock = new TextBlock
            {
                Text = pairs[i].Label,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            ThemeBrushes.Bind(labelBlock, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(labelBlock, i * 2);
            grid.Children.Add(labelBlock);

            pairs[i].Field.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pairs[i].Field, i * 2 + 1);
            grid.Children.Add(pairs[i].Field);
        }
        return grid;
    }

    /// <summary>
    /// Выбирает в списке «СУБД» значение: известное — по значению (без учёта регистра);
    /// неизвестное (например, введённое ранее или подставленное rac-резолвом) —
    /// добавляется опцией, чтобы не терять ввод (issue #79, v1.0.1.32).
    /// </summary>
    private void SelectDbmsComboItem(string? value)
    {
        var options = _dbmsOptions;
        var index = TestDeployDbmsOption.IndexOf(options, value);
        if (index < 0)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                _dbmsCombo.SelectedIndex = 0; // MSSQLServer — значение по умолчанию
                return;
            }
            var extended = new List<TestDeployDbmsOption>(options)
                { new(normalized, normalized) };
            index = extended.Count - 1;
            options = extended;
            _dbmsOptions = extended;
            _dbmsCombo.ItemsSource = extended;
        }
        _dbmsCombo.SelectedIndex = index;
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