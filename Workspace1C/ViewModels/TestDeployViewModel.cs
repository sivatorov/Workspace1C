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
    /// <param name="force">
    /// Принудительная подстановка независимо от <see cref="UseInfobaseAuth"/> (issue #79,
    /// хвост 1.0.1.12): кнопка «Подставить из базы» активна и при включённой галке —
    /// явный клик пользователя есть согласие заполнить поля, а автоподстановка на
    /// смене галки по-прежнему не трогает введённое вручную.
    /// </param>
    public void AutofillCredentialFromSource(bool force = false)
    {
        if (_useInfobaseAuth && !force)
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

    /// <summary>
    /// Подстановка параметров СУБД из профиля исходной базы (issue #79, 1.0.1.9, 7OH:
    /// «Логичнее иметь кнопку "Подставить из профиля" … либо данные из профиля, либо
    /// вручную. А выполнять работу по тому, что на форме») — по аналогии с
    /// <see cref="AutofillCredentialFromSource"/>. Заполняет поля СУБД формы (тип,
    /// сервер, база, пользователь, пароль) из профиля окна «Резервные копии СУБД»;
    /// поля остаются редактируемыми вручную, операция выполняется по значениям на форме.
    /// Возвращает true, если профиль найден и подставлен.
    /// </summary>
    public bool AutofillDbmsFromProfile(Func<Infobase, DbmsBackupProfile?>? profileResolver = null)
    {
        DbmsBackupProfile? profile;
        try
        {
            profile = profileResolver is not null
                ? profileResolver(_source)
                : DbmsBackupService.Load().Find(_source.Id);
        }
        catch
        {
            return false;
        }
        if (profile is null || string.IsNullOrWhiteSpace(profile.Database))
            return false;

        Dbms = profile.Dbms == DbmsKind.PostgreSql ? "PostgreSQL" : "MSSQLServer";
        // Формат 1С: MS SQL «host,port», PostgreSQL «host port=N» (как в --db-server).
        DbServer = IbcmdService.DbServer(profile);
        DbName = profile.Database ?? string.Empty;
        DbUser = profile.User ?? string.Empty;
        DbPassword = profile.Password ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Автоподстановка профиля СУБД ИСХОДНОЙ базы при движке ibcmd (issue #79):
    /// если поля СУБД формы пусты, они заполняются из сохранённого профиля окна
    /// «Резервные копии СУБД» (v1.0.1.21 — только когда поля пусты: поля формы
    /// описывают НОВУЮ (целевую) базу и используются для её создания; выгрузка
    /// источника выполняется по профилю источника / данным rac, а автоподстановка
    /// <see cref="AutofillDbNameFromServerDbName"/> имени целевой базы на выгрузку
    /// больше не влияет). Наличие данных СУБД источника проверяет
    /// <see cref="TestDeployService"/> до старта операций.
    /// </summary>
    public bool EnsureIbcmdProfileFromForm(Func<Infobase, DbmsBackupProfile?>? profileResolver = null)
    {
        if (EngineIndex != 1 || _source.Connection?.Type != ConnectionType.ClientServer)
            return true; // ibcmd не задействован — собирать нечего.

        if (!string.IsNullOrWhiteSpace(DbServer) && !string.IsNullOrWhiteSpace(DbName))
            return true; // параметры целевой базы уже на форме — не трогаем их.

        // Поля пусты — автосбор из сохранённого профиля ИСХОДНОЙ базы в поля формы
        // (пользователю не требуется заполнять их повторно для создания SQL-БД).
        AutofillDbmsFromProfile(profileResolver);
        return true; // выгрузка источника больше не зависит от полей формы (issue #79).
    }

    public bool DisableScheduledJobs { get; set; } = true;
    public bool ChangeTitle { get; set; } = true;
    public bool ChangeColor { get; set; } = true;
    public bool AddToTestGroup { get; set; } = true;
    public string TestGroupName { get; set; } = TestDeployPlanner.DefaultTestGroupName;
    public bool UpdateExisting { get; set; } = true;

    // ---- Ручные данные СУБД ИСТОЧНИКА (issue #79, v1.0.1.29) ----

    /// <summary>
    /// «Указать данные СУБД источника вручную» (issue #79): явные данные СУБД исходной
    /// базы для выгрузки через ibcmd — когда rac отдаёт пустые свойства СУБД или отвечает
    /// «Недостаточно прав». При включении поля автозаполняются (см.
    /// <see cref="FillSourceDbmsOverrideDefaults"/>), профиль строится напрямую,
    /// без rac-проб и эвристики.
    /// </summary>
    public bool UseSourceDbmsOverride { get; set; }

    /// <summary>Тип СУБД источника (PostgreSQL / MSSQLServer).</summary>
    public string SourceDbms { get; set; } = "MSSQLServer";

    /// <summary>Сервер СУБД источника.</summary>
    public string SourceDbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных на сервере СУБД источника.</summary>
    public string SourceDbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД источника.</summary>
    public string SourceDbUser { get; set; } = string.Empty;

    /// <summary>Пароль пользователя СУБД источника (в журнал не выводится).</summary>
    public string SourceDbPassword { get; set; } = string.Empty;

    /// <summary>
    /// Автозаполнение ручных полей СУБД источника при включении галочки (issue #79):
    /// тип/сервер/пользователь — из целевых полей формы (там обычно те же значения),
    /// имя БД — Ref исходной базы. Вызывается при включении галочки; пустые поля формы
    /// не затирают уже введённые ручные значения.
    /// </summary>
    public void FillSourceDbmsOverrideDefaults()
    {
        if (!string.IsNullOrWhiteSpace(Dbms))
            SourceDbms = Dbms;
        if (!string.IsNullOrWhiteSpace(DbServer))
            SourceDbServer = DbServer;
        if (!string.IsNullOrWhiteSpace(DbUser))
            SourceDbUser = DbUser;
        if (string.IsNullOrWhiteSpace(SourceDbName))
            SourceDbName = _source.Connection?.DatabaseName ?? string.Empty;
    }

    // ---- Восстановление значений окна (issue #79, комментарий 7OH 2026-11-05T11:22) ----

    /// <summary>
    /// Восстанавливает значения окна из последнего сценария этой исходной базы (issue #79,
    /// комментарий 7OH 2026-11-05T11:22: «Просьба сохранять значения окна: Движок
    /// выгрузки, Сервер СУБД, Пользователь СУБД»). Сценарий сохраняется в test_deploy.json
    /// после каждого успешного развёртывания (<see cref="Services.TestDeployStore"/>);
    /// пустые значения сценария не затирают подсказки формы. Вызывается окном мастера
    /// при открытии (обе платформы) до привязки полей.
    /// </summary>
    public void ApplyRememberedWindowValues(TestDeployScenario? last)
    {
        if (last is null)
            return;
        if (last.DumpEngine == BackupEngine.Ibcmd)
            EngineIndex = 1;
        if (!string.IsNullOrWhiteSpace(last.DbServer))
            DbServer = last.DbServer;
        if (!string.IsNullOrWhiteSpace(last.DbUser))
            DbUser = last.DbUser;
    }

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
            // Ручные данные СУБД источника (issue #79, v1.0.1.29); пароль — как
            // DbPassword: хранится в JSON сценария, в журнал не выводится.
            UseSourceDbmsOverride = UseSourceDbmsOverride,
            SourceDbms = (SourceDbms ?? string.Empty).Trim(),
            SourceDbServer = (SourceDbServer ?? string.Empty).Trim(),
            SourceDbName = (SourceDbName ?? string.Empty).Trim(),
            SourceDbUser = (SourceDbUser ?? string.Empty).Trim(),
            SourceDbPassword = SourceDbPassword ?? string.Empty,
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
    /// Асинхронная валидация формы с rac-веткой (issue #79, v1.0.1.23): если профиля
    /// СУБД исходной базы нет и поля формы не описывают источник, перед ошибкой
    /// выполняется лёгкая rac-проба (<see cref="ClusterInfobaseCredentialsResolver"/>;
    /// точки подмены — <paramref name="resolverFactory"/> для юнит-тестов). Шаги пробы
    /// журналируются через <paramref name="log"/> (окно мастера; коллбэк может
    /// приходить из фоновых потоков — marshal на UI-поток на стороне окна).
    /// </summary>
    public async Task<List<string>> ValidateAsync(
        Func<ClusterInfobaseCredentialsResolver?>? resolverFactory = null,
        Action<string>? log = null)
    {
        var scenario = BuildScenario();
        var existing = TestDeployPlanner.FindExistingTarget(
            _infobases, scenario, TestDeployPlanner.ResolveTargetName(scenario, _source.Name));

        Func<Infobase, CancellationToken, Task<DbmsBackupProfile?>>? racProbe = null;
        if (scenario.DumpEngine == BackupEngine.Ibcmd
            && _source.Connection?.Type == ConnectionType.ClientServer)
        {
            racProbe = async (infobase, ct) =>
            {
                var resolver = resolverFactory is not null
                    ? resolverFactory()
                    : ClusterInfobaseCredentialsResolver.CreateDefault(message => log?.Invoke(message));
                if (resolver is null)
                    return null;
                // Issue #79 (v1.0.1.25): логин/пароль формы (если «авторизация базы»
                // выключена) и тип СУБД формы — для «infobase info» и пустого dbms rac 8.5.
                resolver.CredentialOverride = scenario.Credential;
                resolver.FallbackDbms = scenario.Dbms;
                // Issue #79 (v1.0.1.29): при неудаче rac-пробы в журнал подсказка —
                // ручные данные СУБД источника ИЛИ движок «Конфигуратор».
                resolver.AdviseManualSourceDbms = true;
                var profile = await resolver.ResolveAsync(infobase, ct).ConfigureAwait(false);
                if (profile is null && !string.IsNullOrWhiteSpace(resolver.LastFailureReason))
                    log?.Invoke(resolver.LastFailureReason!);
                return profile;
            };
        }

        return await TestDeployPlanner.ValidateAsync(
            scenario, _source, existing,
            racProfileResolverAsync: racProbe,
            log: log).ConfigureAwait(false);
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