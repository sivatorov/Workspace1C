using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Форма мастера «Развернуть тестовую копию» (п.10, 1.0.0.92): поля параметров
/// (имя тестовой базы, режим сервер/файл, сервер и СУБД, движок выгрузки, отключение
/// регламентов, заголовок/цвет, группа «Тест», расписание) + предпросмотр плана шагов.
/// Чистый .NET без UI-зависимостей — используется WPF- и Avalonia-окнами.
/// </summary>
public sealed class TestDeployViewModel : ViewModelBase
{
    private readonly Infobase _source;
    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly IReadOnlyList<Group> _groups;

    public TestDeployViewModel(
        Infobase source,
        IReadOnlyList<Infobase> infobases,
        IReadOnlyList<Group> groups,
        string proposedTargetName,
        string proposedServerAddress,
        string proposedDatabaseName,
        string proposedFilePath)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _infobases = infobases ?? Array.Empty<Infobase>();
        _groups = groups ?? Array.Empty<Group>();

        TargetName = proposedTargetName;
        ServerAddress = proposedServerAddress;
        DatabaseName = proposedDatabaseName;
        FilePath = proposedFilePath;
    }

    /// <summary>Исходная база, из которой разворачивается тестовая копия.</summary>
    public Infobase Source => _source;

    /// <summary>Отображаемый заголовок блока источника.</summary>
    public string SourceDisplay =>
        string.IsNullOrWhiteSpace(_source.Name) ? "(—)" : _source.Name;

    /// <summary>Список баз приложения (для поиска существующей тестовой копии).</summary>
    public IReadOnlyList<Infobase> AllInfobases => _infobases;

    /// <summary>Список групп приложения (для решения по группе «Тест»).</summary>
    public IReadOnlyList<Group> Groups => _groups;

    // ---- Имя ----

    public string TargetName { get; set; } = string.Empty;

    // ---- Режим ----

    private bool _isServerMode = true;

    /// <summary>Клиент-серверная тестовая ИБ.</summary>
    public bool IsServerMode
    {
        get => _isServerMode;
        set => SetPropertyV2(ref _isServerMode, value);
    }

    /// <summary>Файловая тестовая ИБ.</summary>
    public bool IsFileMode
    {
        get => !_isServerMode;
        set
        {
            if (SetPropertyV2(ref _isServerMode, !value))
                OnPropertyChanged(nameof(IsServerMode));
        }
    }

    // ---- Сервер (клиент-серверный режим) ----

    public string ServerAddress { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере» (issue #79,
    /// 7OH: «В поле "Имя базы данных СУБД" можно подставлять как минимум такое же имя,
    /// как "Имя базы на сервере"»). Синхронизирует только пока СУБД-имя не заполнено —
    /// пользователь может потом изменить его вручную.
    /// </summary>
    public void AutofillDbNameFromServerDbName()
    {
        if (string.IsNullOrWhiteSpace(DbName) && !string.IsNullOrWhiteSpace(DatabaseName))
            DbName = DatabaseName;
    }

    /// <summary>Тип СУБД (MSSQLServer / PostgreSQL / …).</summary>
    public string Dbms { get; set; } = "MSSQLServer";

    public string DbServer { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Пароль СУБД (только в памяти окна; в сценарий сохраняется как есть).</summary>
    public string DbPassword { get; set; } = string.Empty;

    public bool CreateSqlDatabase { get; set; } = true;

    // ---- Файловый режим ----

    public string FilePath { get; set; } = string.Empty;

    // ---- Поведение ----

    private int _engineIndex;

    /// <summary>Движок выгрузки: 0 — Конфигуратор, 1 — ibcmd.</summary>
    public int EngineIndex
    {
        get => _engineIndex;
        set => SetPropertyV2(ref _engineIndex, value);
    }

    // ---- Учётные данные для выгрузки .dt (issue #79) ----

    private bool _useInfobaseAuth = true;

    /// <summary>
    /// Использовать авторизацию информационной базы при выгрузке (по умолчанию да):
    /// резолвер берёт авторизацию базы/общую авторизацию приложения. Иначе применяются
    /// явные <see cref="User"/>/<see cref="Password"/> из мастера (issue #79).
    /// </summary>
    public bool UseInfobaseAuth
    {
        get => _useInfobaseAuth;
        set => SetPropertyV2(ref _useInfobaseAuth, value);
    }

    /// <summary>Логин для выгрузки, когда <see cref="UseInfobaseAuth"/> выключен.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Пароль для выгрузки, когда <see cref="UseInfobaseAuth"/> выключен (только в памяти/JSON сценария).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Автоподстановка логина/пароля выгрузки из источника (issue #79, 7OH: «А зачем мне
    /// отдельно вводить, если все данные вот они — уже введены выше?»): единый резолвинг
    /// учётных данных «Конфигуратора» (авторизация базы → раздельная общая авторизация
    /// Конфигуратора → legacy общая, см. <see cref="InfobaseAuthResolver"/>). Заполняет
    /// поля только когда <see cref="UseInfobaseAuth"/> выключен (при включённом авторизация
    /// и так берётся резолвером).
    /// </summary>
    public void AutofillCredentialFromSource()
    {
        if (_useInfobaseAuth)
            return;
        InfobaseAuthResolver.Resolve(_source, OneCLaunchMode.Configurator,
            out var mode, out var user, out var password,
            OneCLauncher.CommonEnterpriseAuthorization,
            OneCLauncher.CommonConfiguratorAuthorization,
            OneCLauncher.CommonAuthorization);
        if (mode == AuthenticationMode.Credentials)
        {
            User = user ?? string.Empty;
            Password = password ?? string.Empty;
        }
    }

    public bool DisableScheduledJobs { get; set; } = true;
    public bool ChangeTitle { get; set; } = true;
    public bool ChangeColor { get; set; } = true;
    public bool AddToTestGroup { get; set; } = true;
    public string TestGroupName { get; set; } = TestDeployPlanner.DefaultTestGroupName;
    public bool UpdateExisting { get; set; } = true;

    // ---- Расписание ----

    public bool UseSchedule { get; set; }
    public string ScheduleTime { get; set; } = "06:00";
    public bool Monday { get; set; } = true;
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }

    /// <summary>Собирает сценарий из текущих полей формы.</summary>
    public TestDeployScenario BuildScenario()
    {
        var scenario = new TestDeployScenario
        {
            SourceInfobaseId = _source.Id,
            SourceInfobaseName = _source.Name ?? string.Empty,
            TargetName = (TargetName ?? string.Empty).Trim(),
            TargetMode = IsServerMode ? TestDeployTargetMode.Server : TestDeployTargetMode.File,
            ServerAddress = (ServerAddress ?? string.Empty).Trim(),
            DatabaseName = (DatabaseName ?? string.Empty).Trim(),
            Dbms = (Dbms ?? string.Empty).Trim(),
            DbServer = (DbServer ?? string.Empty).Trim(),
            DbName = (DbName ?? string.Empty).Trim(),
            DbUser = (DbUser ?? string.Empty).Trim(),
            DbPassword = DbPassword ?? string.Empty,
            CreateSqlDatabase = CreateSqlDatabase,
            // issue #79: учётные данные для выгрузки .dt (явные или «авторизация базы»).
            Credential = new BackupCredential
            {
                UseInfobaseAuth = UseInfobaseAuth,
                User = (User ?? string.Empty).Trim(),
                Password = Password ?? string.Empty
            },
            FilePath = (FilePath ?? string.Empty).Trim(),
            DumpEngine = EngineIndex == 1 ? BackupEngine.Ibcmd : BackupEngine.Designer,
            DisableScheduledJobs = DisableScheduledJobs,
            ChangeTitle = ChangeTitle,
            ChangeColor = ChangeColor,
            AddToTestGroup = AddToTestGroup,
            TestGroupName = string.IsNullOrWhiteSpace(TestGroupName)
                ? TestDeployPlanner.DefaultTestGroupName
                : (TestGroupName ?? string.Empty).Trim(),
            TestGroupColor = TestDeployPlanner.DefaultGroupColor,
            UpdateExisting = UpdateExisting,
            UseSchedule = UseSchedule,
            ScheduleTime = string.IsNullOrWhiteSpace(ScheduleTime) ? "06:00" : (ScheduleTime ?? string.Empty).Trim(),
            ScheduleDaysOfWeek = CollectDays()
        };
        scenario.Name = string.IsNullOrWhiteSpace(scenario.TargetName)
            ? TestDeployPlanner.ProposeTargetName(_source.Name)
            : scenario.TargetName;
        return scenario;
    }

    /// <summary>Дни недели расписания (пусто — ежедневно).</summary>
    public List<DayOfWeek> CollectDays()
    {
        var days = new List<DayOfWeek>();
        if (Monday) days.Add(DayOfWeek.Monday);
        if (Tuesday) days.Add(DayOfWeek.Tuesday);
        if (Wednesday) days.Add(DayOfWeek.Wednesday);
        if (Thursday) days.Add(DayOfWeek.Thursday);
        if (Friday) days.Add(DayOfWeek.Friday);
        if (Saturday) days.Add(DayOfWeek.Saturday);
        if (Sunday) days.Add(DayOfWeek.Sunday);
        return days;
    }

    /// <summary>
    /// Ключи ошибок валидации формы (пустой список — можно разворачивать).
    /// Использует ту же логику, что <see cref="Services.TestDeployPlanner.Validate"/>:
    /// при найденной тестовой копии и включённом обновлении часть требований снимается.
    /// </summary>
    public List<string> Validate()
    {
        var scenario = BuildScenario();
        var existing = TestDeployPlanner.FindExistingTarget(
            _infobases, scenario, TestDeployPlanner.ResolveTargetName(scenario, _source.Name));
        return TestDeployPlanner.Validate(scenario, _source, existing);
    }

    /// <summary>
    /// Предпросмотр шагов плана (заголовки уже локализованы: ключи → текст).
    /// Issue #79 (комментарий 7OH от 2026-10-29 «ошибка при открытии»): построение
    /// предпросмотра не должно ронять открытие мастера при некорректных данных базы
    /// (пустое соединение, странные имена и т.п.) — при исключении возвращается
    /// пустой список, а сбой фиксируется в журнале ERROR.
    /// </summary>
    public IReadOnlyList<string> PreviewSteps
    {
        get
        {
            try
            {
                var scenario = BuildScenario();
                var existing = TestDeployPlanner.FindExistingTarget(
                    _infobases, scenario, TestDeployPlanner.ResolveTargetName(scenario, _source.Name));
                var groupPlan = TestDeployPlanner.EnsureGroup(_groups, scenario);
                return TestDeployPlanner.DescribeSteps(scenario, existing, groupPlan)
                    .Select(s =>
                    {
                        var text = LocalizationManager.T(s.TitleKey);
                        return s.Kind == TestDeployStepKind.Update && existing is not null
                            ? string.Format(LocalizationManager.T("TestDeploy.PreviewUpdateFormat"), existing.Name, text)
                            : text;
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Info(LogCategory.MainWindow, $"Предпросмотр шагов «Развернуть тестовую копию» недоступен: {ex.Message}");
                return new List<string>();
            }
        }
    }

    /// <summary>
    /// Строка сводки для предпросмотра (целевая база, группа, признак создания/обновления).
    /// Issue #79: при исключении возвращается нейтральный текст, а не падение окна.
    /// </summary>
    public string PreviewSummary
    {
        get
        {
            try
            {
                var scenario = BuildScenario();
                var targetName = TestDeployPlanner.ResolveTargetName(scenario, _source.Name);
                var existing = TestDeployPlanner.FindExistingTarget(_infobases, scenario, targetName);
                var modeText = LocalizationManager.T(
                    IsServerMode ? "TestDeploy.ModeServer" : "TestDeploy.ModeFile");
                var actionText = existing is null
                    ? LocalizationManager.T("TestDeploy.ActionCreate")
                    : string.Format(LocalizationManager.T("TestDeploy.ActionUpdate"), existing.Name);
                return string.Format(
                    LocalizationManager.T("TestDeploy.PreviewSummaryFormat"),
                    targetName,
                    modeText,
                    actionText);
            }
            catch (Exception ex)
            {
                Log.Info(LogCategory.MainWindow,
                    $"Сводка предпросмотра «Развернуть тестовую копию» недоступна: {ex.Message}");
                return LocalizationManager.T("TestDeploy.PreviewUnavailable");
            }
        }
    }

    /// <summary>Обновляет предпросмотр шагов при изменении любого поля формы.</summary>
    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewSteps));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(IsFileMode));
    }

    /// <summary>Вариант helper смены значения (событие через CallerMemberName).</summary>
    private bool SetPropertyV2<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}