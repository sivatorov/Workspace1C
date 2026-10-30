#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Мастер «Развернуть тестовую копию» (п.10, 1.0.0.92, WPF): параметры тестовой базы
/// (имя, режим сервер/файл, сервер и СУБД, движок выгрузки, отключение регламентов,
/// заголовок/цвет, группа «Тест», расписание) + предпросмотр плана и журнал прогресса.
/// Выполнение — через <see cref="TestDeployService"/> прямо в окне; по успеху заполняет
/// <see cref="Scenario"/>/<see cref="DeployResult"/>/<see cref="CreatedGroup"/>, а запись
/// списка и группа «Тест» применяются вызывающим (командой главного окна).
/// </summary>
public class TestDeployWindow : Window
{
    private readonly TestDeployViewModel _vm;
    private readonly IReadOnlyList<Group> _groups;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    // Поля формы
    private readonly TextBox _targetNameBox;
    private readonly RadioButton _serverRadio;
    private readonly RadioButton _fileRadio;
    private readonly StackPanel _serverPanel;
    private readonly StackPanel _filePanel;
    private readonly TextBox _serverBox;
    private readonly TextBox _refBox;
    private readonly TextBox _dbmsBox;
    private readonly TextBox _dbServerBox;
    private readonly TextBox _dbNameBox;
    private readonly TextBox _dbUserBox;
    private readonly PasswordBox _dbPwdBox;
    private readonly CheckBox _createDbCheck;
    private readonly TextBox _filePathBox;
    private readonly ComboBox _engineCombo;
    private readonly CheckBox _disableJobsCheck;
    private readonly CheckBox _changeTitleCheck;
    private readonly CheckBox _changeColorCheck;
    private readonly CheckBox _addToGroupCheck;
    private readonly TextBox _groupNameBox;
    private readonly CheckBox _updateExistingCheck;
    private readonly CheckBox _useScheduleCheck;
    private readonly TextBox _scheduleTimeBox;
    private readonly CheckBox[] _dayChecks = new CheckBox[7];
    private readonly ListBox _logList;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    // Панель «Данные для выгрузки через ibcmd» (issue #79).
    private readonly StackPanel _ibcmdPanel;
    private readonly CheckBox _useInfobaseAuthCheck;
    private readonly TextBox _ibcmdUserBox;
    private readonly PasswordBox _ibcmdPwdBox;
    private readonly Button _fillCredentialsButton;

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

        // Подсказки по умолчанию: имя «Тест: <источник>», сервер с портом источника,
        // Ref «<источник>_t», файловый каталог — рядом с каталогом файловой базы.
        var proposedName = TestDeployPlanner.ProposeTargetName(source.Name);
        var proposedServer = source.Connection?.GetServerWithPort() ?? string.Empty;
        var proposedDb = source.Connection?.DatabaseName ?? string.Empty;
        var proposedDbRef = string.IsNullOrWhiteSpace(proposedDb)
            ? string.Empty
            : suggestedRef(proposedDb, allInfobases);
        var proposedFile = ProposedFileDirectory(source, proposedName);

        _vm = new TestDeployViewModel(
            source, allInfobases, _groups, proposedName, proposedServer, proposedDbRef, proposedFile);

        Title = LocalizationManager.T("TestDeploy.Title");
        Width = 760;
        Height = 720;
        MinWidth = 680;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "ContentBackgroundBrush");

        // --- Разметка ---
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // источник
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // форма
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // журнал
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // кнопки

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        // --- Источник ---
        var sourceBlock = new TextBlock
        {
            Text = string.Format(LocalizationManager.T("TestDeploy.SourceFormat"), _vm.SourceDisplay),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            FontWeight = FontWeights.SemiBold
        };
        panel.Children.Add(sourceBlock);

        // Действия по исходной базе (issue #79, 7OH: «Логично сразу эту кнопку видеть —
        // чтобы тут же нажать и открыть свойства», «иметь кнопки в окошке, чтобы админ
        // мог сам это сделать не выходя из этого окна»): свойства базы и Конфигуратор.
        var sourceActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var propsButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.OpenBaseProperties"),
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = LocalizationManager.T("TestDeploy.OpenBasePropertiesHint")
        };
        propsButton.SetResourceReference(StyleProperty, "SecondaryButton");
        propsButton.Click += (_, _) => OpenBaseProperties();
        sourceActions.Children.Add(propsButton);
        var configButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.LaunchConfigurator"),
            Height = 34,
            ToolTip = LocalizationManager.T("TestDeploy.LaunchConfiguratorHint")
        };
        configButton.SetResourceReference(StyleProperty, "SecondaryButton");
        configButton.Click += (_, _) => LaunchConfigurator();
        sourceActions.Children.Add(configButton);
        panel.Children.Add(sourceActions);

        // --- Имя тестовой базы ---
        panel.Children.Add(MakeLabel("TestDeploy.TargetName"));
        _targetNameBox = new TextBox { Text = _vm.TargetName, Margin = new Thickness(0, 2, 0, 10) };
        _targetNameBox.TextChanged += (_, _) => { _vm.TargetName = _targetNameBox.Text; RefreshPreview(); };
        panel.Children.Add(_targetNameBox);

        // --- Режим ---
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _serverRadio = new RadioButton
        {
            Content = LocalizationManager.T("TestDeploy.ModeServer"),
            IsChecked = _vm.IsServerMode,
            Margin = new Thickness(0, 0, 16, 0)
        };
        _fileRadio = new RadioButton
        {
            Content = LocalizationManager.T("TestDeploy.ModeFile"),
            IsChecked = _vm.IsFileMode
        };
        _serverRadio.Checked += (_, _) => { _vm.IsServerMode = true; UpdateModePanels(); RefreshPreview(); };
        _fileRadio.Checked += (_, _) => { _vm.IsFileMode = true; UpdateModePanels(); RefreshPreview(); };
        modeRow.Children.Add(_serverRadio);
        modeRow.Children.Add(_fileRadio);
        panel.Children.Add(modeRow);

        // --- Серверная секция ---
        _serverPanel = new StackPanel();
        _serverPanel.Children.Add(MakeLabel("TestDeploy.Server"));
        _serverBox = new TextBox { Text = _vm.ServerAddress, Margin = new Thickness(0, 2, 0, 8) };
        _serverBox.TextChanged += (_, _) => _vm.ServerAddress = _serverBox.Text;
        _serverPanel.Children.Add(_serverBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.Ref"));
        _refBox = new TextBox { Text = _vm.DatabaseName, Margin = new Thickness(0, 2, 0, 8) };
        _refBox.TextChanged += (_, _) =>
        {
            _vm.DatabaseName = _refBox.Text;
            // issue #79: «В поле "Имя базы данных СУБД" можно подставлять такое же имя,
            // как "Имя базы на сервере"» — пока СУБД-имя не заполнено, синхронизируем его
            // с именем базы на сервере (пользователь может потом поправить вручную).
            if (_dbNameBox is not null && string.IsNullOrWhiteSpace(_vm.DbName)
                && !string.IsNullOrWhiteSpace(_refBox.Text))
            {
                _vm.AutofillDbNameFromServerDbName();
                _dbNameBox.Text = _vm.DbName;
            }
        };
        _serverPanel.Children.Add(_refBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.Dbms"));
        _dbmsBox = new TextBox { Text = _vm.Dbms, Margin = new Thickness(0, 2, 0, 8) };
        _dbmsBox.TextChanged += (_, _) => _vm.Dbms = _dbmsBox.Text;
        _serverPanel.Children.Add(_dbmsBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.DbServer"));
        _dbServerBox = new TextBox { Text = _vm.DbServer, Margin = new Thickness(0, 2, 0, 8) };
        _dbServerBox.TextChanged += (_, _) => _vm.DbServer = _dbServerBox.Text;
        _serverPanel.Children.Add(_dbServerBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.DbName"));
        _dbNameBox = new TextBox { Text = _vm.DbName, Margin = new Thickness(0, 2, 0, 8) };
        _dbNameBox.TextChanged += (_, _) => _vm.DbName = _dbNameBox.Text;
        _serverPanel.Children.Add(_dbNameBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.DbUser"));
        _dbUserBox = new TextBox { Text = _vm.DbUser, Margin = new Thickness(0, 2, 0, 8) };
        _dbUserBox.TextChanged += (_, _) => _vm.DbUser = _dbUserBox.Text;
        _serverPanel.Children.Add(_dbUserBox);

        _serverPanel.Children.Add(MakeLabel("TestDeploy.DbPassword"));
        _dbPwdBox = new PasswordBox { Margin = new Thickness(0, 2, 0, 8) };
        _dbPwdBox.PasswordChanged += (_, _) => _vm.DbPassword = _dbPwdBox.Password;
        _serverPanel.Children.Add(_dbPwdBox);

        // issue #79 (1.0.1.9, 7OH: «Логичнее иметь кнопку "Подставить из профиля" … либо
        // данные из профиля, либо вручную. А выполнять работу по тому, что на форме»):
        // поля СУБД остаются редактируемыми; кнопка подставляет их из профиля исходной
        // базы (окно «Резервные копии СУБД») — по аналогии с «Подставить из базы»
        // для логина/пароля выгрузки. Операция выполняется по значениям на форме.
        var fillDbmsProfileButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.FillFromDbmsProfile"),
            Height = 32,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = LocalizationManager.T("TestDeploy.FillFromDbmsProfileHint")
        };
        fillDbmsProfileButton.SetResourceReference(StyleProperty, "SecondaryButton");
        fillDbmsProfileButton.Click += (_, _) =>
        {
            if (_vm.AutofillDbmsFromProfile())
            {
                _dbmsBox.Text = _vm.Dbms;
                _dbServerBox.Text = _vm.DbServer;
                _dbNameBox.Text = _vm.DbName;
                _dbUserBox.Text = _vm.DbUser;
                _dbPwdBox.Password = _vm.DbPassword;
            }
            else
            {
                _dialogs.ShowInfo(LocalizationManager.T("TestDeploy.NoDbmsProfile"),
                    LocalizationManager.T("TestDeploy.Title"));
            }
        };
        _serverPanel.Children.Add(fillDbmsProfileButton);

        _createDbCheck = new CheckBox
        {
            Content = LocalizationManager.T("TestDeploy.CreateDb"),
            IsChecked = _vm.CreateSqlDatabase,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _createDbCheck.Checked += (_, _) => _vm.CreateSqlDatabase = true;
        _createDbCheck.Unchecked += (_, _) => _vm.CreateSqlDatabase = false;
        _serverPanel.Children.Add(_createDbCheck);
        panel.Children.Add(_serverPanel);

        // --- Файловая секция ---
        _filePanel = new StackPanel();
        _filePanel.Children.Add(MakeLabel("TestDeploy.FilePath"));
        _filePathBox = new TextBox { Text = _vm.FilePath, Margin = new Thickness(0, 2, 0, 10) };
        _filePathBox.TextChanged += (_, _) => _vm.FilePath = _filePathBox.Text;
        var fileRow = new DockPanel { LastChildFill = true };
        var browseButton = new Button
        {
            Content = LocalizationManager.T("Common.Browse"),
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0)
        };
        browseButton.Click += (_, _) => BrowseFileDirectory();
        DockPanel.SetDock(browseButton, Dock.Right);
        fileRow.Children.Add(browseButton);
        fileRow.Children.Add(_filePathBox);
        _filePanel.Children.Add(fileRow);
        panel.Children.Add(_filePanel);

        // --- Движок выгрузки ---
        panel.Children.Add(MakeLabel("TestDeploy.Engine"));
        _engineCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                new TestDeployEngineOption("Designer", LocalizationManager.T("TestDeploy.EngineDesigner")),
                new TestDeployEngineOption("Ibcmd", LocalizationManager.T("TestDeploy.EngineIbcmd"))
            },
            DisplayMemberPath = "Text",
            SelectedIndex = _vm.EngineIndex,
            Margin = new Thickness(0, 2, 0, 10)
        };
        _engineCombo.SelectionChanged += (_, _) =>
        {
            _vm.EngineIndex = Math.Max(0, _engineCombo.SelectedIndex);
            UpdateIbcmdPanel();
        };
        panel.Children.Add(_engineCombo);

        // --- Данные для выгрузки через ibcmd (issue #79, 7OH: «То есть я должен закрыть
        // окно со всеми введёнными данными и пойти найти где это вводится?», «А зачем мне
        // отдельно вводить, если все данные вот они — уже введены выше?») ---
        _ibcmdPanel = new StackPanel();
        _ibcmdPanel.Children.Add(MakeLabel("TestDeploy.IbcmdCredentials"));
        _useInfobaseAuthCheck = MakeCheckBox("TestDeploy.UseInfobaseAuth", _vm.UseInfobaseAuth);
        _useInfobaseAuthCheck.Checked += (_, _) => { _vm.UseInfobaseAuth = true; UpdateIbcmdPanel(); };
        _useInfobaseAuthCheck.Unchecked += (_, _) => { _vm.UseInfobaseAuth = false; UpdateIbcmdPanel(); };
        _ibcmdPanel.Children.Add(_useInfobaseAuthCheck);

        _ibcmdPanel.Children.Add(MakeLabel("TestDeploy.CredentialsUser"));
        _ibcmdUserBox = new TextBox { Text = _vm.User, Margin = new Thickness(0, 2, 0, 8) };
        _ibcmdUserBox.TextChanged += (_, _) => _vm.User = _ibcmdUserBox.Text;
        _ibcmdPanel.Children.Add(_ibcmdUserBox);

        _ibcmdPanel.Children.Add(MakeLabel("TestDeploy.CredentialsPassword"));
        _ibcmdPwdBox = new PasswordBox { Margin = new Thickness(0, 2, 0, 8) };
        _ibcmdPwdBox.PasswordChanged += (_, _) => _vm.Password = _ibcmdPwdBox.Password;
        _ibcmdPanel.Children.Add(_ibcmdPwdBox);

        _fillCredentialsButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.FillFromBase"),
            Height = 32,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _fillCredentialsButton.SetResourceReference(StyleProperty, "SecondaryButton");
        _fillCredentialsButton.Click += (_, _) =>
        {
            _vm.AutofillCredentialFromSource();
            _ibcmdUserBox.Text = _vm.User;
            _ibcmdPwdBox.Password = _vm.Password;
        };
        _ibcmdPanel.Children.Add(_fillCredentialsButton);
        panel.Children.Add(_ibcmdPanel);

        // --- Отключаемые регламенты ---
        _disableJobsCheck = MakeCheckBox("TestDeploy.DisableJobs", _vm.DisableScheduledJobs);
        _disableJobsCheck.Checked += (_, _) => _vm.DisableScheduledJobs = true;
        _disableJobsCheck.Unchecked += (_, _) => _vm.DisableScheduledJobs = false;
        panel.Children.Add(_disableJobsCheck);

        // --- Заголовок и цвет ---
        _changeTitleCheck = MakeCheckBox("TestDeploy.ChangeTitle", _vm.ChangeTitle);
        _changeTitleCheck.Checked += (_, _) => _vm.ChangeTitle = true;
        _changeTitleCheck.Unchecked += (_, _) => _vm.ChangeTitle = false;
        panel.Children.Add(_changeTitleCheck);

        _changeColorCheck = MakeCheckBox("TestDeploy.ChangeColor", _vm.ChangeColor);
        _changeColorCheck.Checked += (_, _) => _vm.ChangeColor = true;
        _changeColorCheck.Unchecked += (_, _) => _vm.ChangeColor = false;
        panel.Children.Add(_changeColorCheck);

        // --- Группа «Тест» ---
        _addToGroupCheck = MakeCheckBox("TestDeploy.AddToGroup", _vm.AddToTestGroup);
        _addToGroupCheck.Checked += (_, _) => { _vm.AddToTestGroup = true; RefreshPreview(); };
        _addToGroupCheck.Unchecked += (_, _) => { _vm.AddToTestGroup = false; RefreshPreview(); };
        panel.Children.Add(_addToGroupCheck);

        panel.Children.Add(MakeLabel("TestDeploy.GroupName"));
        _groupNameBox = new TextBox { Text = _vm.TestGroupName, Margin = new Thickness(0, 2, 0, 10) };
        _groupNameBox.TextChanged += (_, _) => _vm.TestGroupName = _groupNameBox.Text;
        panel.Children.Add(_groupNameBox);

        // --- Обновление существующей ---
        _updateExistingCheck = MakeCheckBox("TestDeploy.UpdateExisting", _vm.UpdateExisting);
        _updateExistingCheck.Checked += (_, _) => _vm.UpdateExisting = true;
        _updateExistingCheck.Unchecked += (_, _) => _vm.UpdateExisting = false;
        panel.Children.Add(_updateExistingCheck);

        // --- Расписание ---
        _useScheduleCheck = MakeCheckBox("TestDeploy.UseSchedule", _vm.UseSchedule);
        _useScheduleCheck.Checked += (_, _) => { _vm.UseSchedule = true; UpdateScheduleVisibility(); };
        _useScheduleCheck.Unchecked += (_, _) => { _vm.UseSchedule = false; UpdateScheduleVisibility(); };
        panel.Children.Add(_useScheduleCheck);

        panel.Children.Add(MakeLabel("TestDeploy.ScheduleTime"));
        _scheduleTimeBox = new TextBox { Text = _vm.ScheduleTime, Margin = new Thickness(0, 2, 0, 8) };
        _scheduleTimeBox.TextChanged += (_, _) => _vm.ScheduleTime = _scheduleTimeBox.Text;
        panel.Children.Add(_scheduleTimeBox);

        panel.Children.Add(MakeLabel("TestDeploy.ScheduleDays"));
        var daysRow = new StackPanel { Orientation = Orientation.Horizontal };
        var dayNames = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var dayValues = new[] { _vm.Monday, _vm.Tuesday, _vm.Wednesday, _vm.Thursday, _vm.Friday, _vm.Saturday, _vm.Sunday };
        for (var i = 0; i < 7; i++)
        {
            var index = i;
            var check = new CheckBox { Content = dayNames[i], IsChecked = dayValues[i], Margin = new Thickness(0, 0, 10, 0) };
            check.Checked += (_, _) => SetScheduleDay(index, true);
            check.Unchecked += (_, _) => SetScheduleDay(index, false);
            _dayChecks[i] = check;
            daysRow.Children.Add(check);
        }
        panel.Children.Add(daysRow);
        var scheduleHint = new TextBlock
        {
            Text = LocalizationManager.T("TestDeploy.ScheduleHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 10)
        };
        scheduleHint.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(scheduleHint);

        // --- Предпросмотр ---
        _previewText = new TextBlock
        {
            Text = _vm.PreviewSummary,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 2)
        };
        panel.Children.Add(_previewText);
        _previewSteps = new ItemsControl { Margin = new Thickness(0, 0, 0, 10) };
        RefreshPreview();
        panel.Children.Add(_previewSteps);

        scroll.Content = panel;
        grid.Children.Add(scroll);
        Grid.SetRow(scroll, 1);

        // --- Журнал ---
        var logLabel = MakeLabel("TestDeploy.Log");
        grid.Children.Add(logLabel);
        Grid.SetRow(logLabel, 2);
        _logList = new ListBox { Height = 150, Margin = new Thickness(0, 2, 0, 10) };
        grid.Children.Add(_logList);
        Grid.SetRow(_logList, 2);

        // --- Кнопки ---
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _runButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.Run"),
            Width = 170,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton = new Button
        {
            Content = LocalizationManager.T("Common.Cancel"),
            Width = 100,
            IsCancel = true
        };
        _cancelButton.Click += (_, _) => DialogResult = false;
        // issue #79 (7OH: «Можно внизу кнопку добавить, которая откроет окно нужное
        // прямо тут?»): общая авторизация — без выхода из мастера. Issue #79 (1.0.1.9,
        // 7OH: кнопка «Учётные данные ИБ для ibcmd…» — дубль и не нужна): кнопка
        // удалена — учётные данные ИБ задаются в блоке «Данные для выгрузки через
        // ibcmd» (кнопка «Подставить из базы») и в свойствах исходной базы (кнопка
        // вверху окна). Оставлены «Профиль СУБД для ibcmd…» (окно «Резервные копии
        // СУБД») и «Общая авторизация…».
        var dbmsProfileButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.OpenDbmsProfileWindow"),
            Width = 200,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = LocalizationManager.T("TestDeploy.OpenDbmsProfileWindowHint")
        };
        dbmsProfileButton.SetResourceReference(StyleProperty, "SecondaryButton");
        dbmsProfileButton.Click += (_, _) => OpenDbmsProfile();
        buttons.Children.Add(dbmsProfileButton);
        var commonAuthButton = new Button
        {
            Content = LocalizationManager.T("TestDeploy.OpenCommonAuth"),
            Width = 175,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = LocalizationManager.T("TestDeploy.OpenCommonAuthHint")
        };
        commonAuthButton.SetResourceReference(StyleProperty, "SecondaryButton");
        commonAuthButton.Click += (_, _) => OpenCommonAuthSettings();
        buttons.Children.Add(commonAuthButton);
        buttons.Children.Add(_runButton);
        buttons.Children.Add(_cancelButton);
        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 3);

        // Автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере» (issue #79):
        // окно уже открыто с предложенным Ref — то же имя сразу подставляется в СУБД-поле.
        _vm.AutofillDbNameFromServerDbName();
        if (!string.IsNullOrEmpty(_vm.DbName))
            _dbNameBox.Text = _vm.DbName;

        // Единое оформление полей ввода (issue #79, 7OH: «одни поля серые, вторые
        // нормальные и с окантовкой»): все редактируемые поля получают тематический
        // ModernTextBox / ModernComboBox.
        ApplyModernStyles(grid);

        Content = grid;
        UpdateModePanels();
        UpdateScheduleVisibility();
        UpdateIbcmdPanel();
    }

    /// <summary>
    /// Показывает панель «Данные для выгрузки через ibcmd» только для движка ibcmd
    /// (issue #79); поля логина/пароля активны только при выключенной авторизации ИБ.
    /// </summary>
    private void UpdateIbcmdPanel()
    {
        var visible = _engineCombo.SelectedIndex == 1;
        _ibcmdPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var explicitAuth = !_vm.UseInfobaseAuth;
        _ibcmdUserBox.IsEnabled = explicitAuth;
        _ibcmdPwdBox.IsEnabled = explicitAuth;
        _fillCredentialsButton.IsEnabled = explicitAuth;
    }

    private readonly TextBlock _previewText;
    private readonly ItemsControl _previewSteps;

    private void RefreshPreview()
    {
        if (_previewSteps is null)
            return;
        _vm.RefreshPreview();
        if (_previewText is not null)
            _previewText.Text = _vm.PreviewSummary;
        _previewSteps.ItemsSource = _vm.PreviewSteps.ToList();
    }

    private void UpdateModePanels()
    {
        _serverPanel.Visibility = _vm.IsServerMode ? Visibility.Visible : Visibility.Collapsed;
        _filePanel.Visibility = _vm.IsFileMode ? Visibility.Visible : Visibility.Collapsed;
        RefreshPreview();
    }

    private void UpdateScheduleVisibility()
    {
        _scheduleTimeBox.IsEnabled = _vm.UseSchedule;
        foreach (var check in _dayChecks)
            check.IsEnabled = _vm.UseSchedule;
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

        // Валидация формы (понятная ошибка до старта).
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
            var progress = new Progress<string>(line =>
            {
                _logList.Items.Add(line);
                _logList.ScrollIntoView(_logList.Items[_logList.Items.Count - 1]);
            });
            var result = await service.DeployAsync(
                scenario, _vm.Source, _vm.AllInfobases, _groups, line => { }, default);

            foreach (var line in result.Log)
            {
                _logList.Items.Add(line);
                _logList.ScrollIntoView(_logList.Items[_logList.Items.Count - 1]);
            }

            if (!result.Success)
            {
                _logList.Items.Add(result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed"));
                _dialogs.ShowError(result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed"),
                    LocalizationManager.T("TestDeploy.Title"));
                return;
            }

            foreach (var warning in result.Warnings)
                _logList.Items.Add(warning);

            // Фиксируем сценарий (для задания по расписанию) и результат.
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
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, LocalizationManager.T("TestDeploy.Title"));
        }
        finally
        {
            _busy = false;
            _runButton.IsEnabled = true;
            _cancelButton.IsEnabled = true;
        }
    }

    private void BrowseFileDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationManager.T("TestDeploy.FilePath")
        };
        if (dialog.ShowDialog(this) == true)
            _filePathBox.Text = dialog.FolderName;
    }

    /// <summary>
    /// Предлагает каталог файловой тестовой базы: рядом с каталогом файловой базы-источника
    /// (папка <c>Имя тестовой базы</c>). Для серверного источника — пусто (пользователь вводит).
    /// </summary>
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

    private static string suggestedRef(string sourceDb, IEnumerable<Infobase> allInfobases)
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

    private static Label MakeLabel(string key) => new()
    {
        Content = LocalizationManager.T(key),
        Margin = new Thickness(0, 6, 0, 2)
    };

    private static CheckBox MakeCheckBox(string key, bool isChecked) => new()
    {
        Content = LocalizationManager.T(key),
        IsChecked = isChecked,
        Margin = new Thickness(0, 0, 0, 6)
    };

    /// <summary>
    /// Открывает окно свойств исходной базы (issue #79): «А нельзя брать из свойств базы
    /// разве? …иметь кнопки в окошке, чтобы админ сам мог это сделать не выходя из окна».
    /// </summary>
    private void OpenBaseProperties()
    {
        if (_vm.Source is not { } ib)
            return;
        var dialog = new ConnectionSettingsWindow(ib, _groups) { Owner = this };
        dialog.ShowDialog();
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
        var dialog = new DbmsBackupWindow(vm) { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Открывает настройки приложения — раздел «Общая авторизация» (issue #79,
    /// 7OH: «пойти найти где это вводится?»), не закрывая мастер.
    /// </summary>
    private void OpenCommonAuthSettings()
    {
        if (Owner is MainWindow main && main.DataContext is MainViewModel mainVm)
        {
            var dialog = new SettingsWindow(mainVm) { Owner = this };
            dialog.ShowDialog();
        }
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

    /// <summary>
    /// Единое оформление полей ввода (issue #79): обходит логическое дерево окна и
    /// назначает тематические стили полям (TextBox — ModernTextBox, ComboBox —
    /// ModernComboBox). PasswordBox не стилизуется здесь: он получает темированный
    /// implicit-стиль TargetType="PasswordBox" из LightTheme/DarkTheme (назначать
    /// ему ModernTextBox с TargetType="TextBox" нельзя — см. ветку обхода ниже).
    /// Без явного стиля часть полей рисуется «серыми» и без окантовки — «одни поля
    /// серые, вторые нормальные и с окантовкой».
    /// </summary>
    private void ApplyModernStyles(System.Windows.DependencyObject root)
    {
        var styleBox = TryFindResource("ModernTextBox") as Style;
        var styleCombo = TryFindResource("ModernComboBox") as Style;
        if (styleBox is null && styleCombo is null)
            return;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is TextBox tb && styleBox is not null)
                tb.Style = styleBox;
            // PasswordBox стиль НЕ назначается (issue #79, 7OH 2026-10-29): ModernTextBox
            // имеет TargetType="TextBox", а присваивание его PasswordBox бросает
            // InvalidOperationException — «TargetType "TextBox" не соответствует типу
            // элемента "PasswordBox"». Пароль получает темированный implicit-стиль
            // TargetType="PasswordBox" из LightTheme/DarkTheme (см. ProfilesPanel.xaml).
            else if (child is ComboBox cb && styleCombo is not null)
                cb.Style = styleCombo;
            if (child is System.Windows.DependencyObject d)
                ApplyModernStyles(d);
        }
    }
}
#endif