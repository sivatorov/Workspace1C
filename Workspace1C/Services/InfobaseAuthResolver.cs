using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Единый выбор учётных данных информационной базы по режиму (issue #236, #57).
/// «1С:Предприятие» использует отдельную авторизацию (<see cref="Infobase.EnterpriseAuth"/>),
/// если она задана; «Конфигуратор» — отдельную авторизацию (<see cref="Infobase.ConfiguratorAuth"/>),
/// если она задана; иначе — авторизацию информационной базы (<see cref="Infobase.Connection"/>,
/// обратная совместимость). При незаданной авторизации базы (режима и базы) подставляется
/// общая авторизация (issue #57): раздельная по режиму запуска —
/// <see cref="AppSettings.CommonEnterpriseAuth"/> для «1С:Предприятия» /
/// <see cref="AppSettings.CommonConfiguratorAuth"/> для «Конфигуратора», а при её
/// незаданности — прежняя общая <see cref="AppSettings.CommonAuthorization"/> (legacy).
/// Учётные данные Хранилища конфигурации разрешаются отдельно
/// (<see cref="ResolveRepository"/>) из <see cref="Infobase.Repository"/>.
/// Резолвинг применяется и при запуске 1С (<see cref="OneCLauncher"/>), и при чтении сведений
/// о конфигурации через COM-коннектор (<see cref="OneCComConnector"/>), чтобы учётные данные
/// выбирались одинаково: раздельная авторизация Конфигуратора/Предприятия не терялась при
/// построении строки подключения COM.
/// </summary>
public static class InfobaseAuthResolver
{
    // СКВОЗНОЙ АУДИТ ИСПОЛЬЗОВАНИЯ ПАРОЛЯ (issue #57, требование 7OH, 1.0.1.6).
    // Все пути входа в информационную базу проходят через этот резолвер:
    //  - OneCLauncher.Arguments.Shared.BuildArguments / BuildAuthArgument — запуск
    //    Предприятия/Конфигуратора и пакетные операции Конфигуратора;
    //  - OneCComConnector — COM-подключение (единственный источник ComConnectionInfo,
    //    из которого пароль получают InfobaseMaintenanceService, ScheduledJobsService
    //    и ComScriptRunner);
    //  - IbcmdService — ibcmd;
    //  - InfobaseUsersService — пользователи ИБ через COM;
    //  - MainViewModel.Scripts / MainViewModel.Avalonia.Scripts / ScriptTokenPickerViewModel /
    //    ScriptScenarioEditViewModel / TestDeployViewModel — токены и развёртывание
    //    (resolvedCredentials приходят из InfobaseAuthResolver.Resolve, поэтому
    //    новая семантика перекрытия Windows-авторизации общей Credentials
    //    применяется автоматически);
    //  - DesignerCommandRunner / ConfigurationUpdateService / ConfigExtensionService —
    //    пакетные команды дизайнера через BuildAuthArgument/BuildUserArguments.
    // НЕ учётные данные входа в ИБ (общий резолвер к ним не применяется):
    // IbasesV8iExporter/IbasesV8iImporter/StartManagerImporter (импорт/экспорт списка),
    // ConnectionReplacementPlanner, TestDeployService (копирование свойств),
    // ServerClonePlanner/OneCLauncher.Create (пароль СУБД DBPwd), Cluster*
    // (кластеры), CustomActionRunner (маскирование пароля в логе).

    /// <summary>
    /// Разрешает режим аутентификации, логин и пароль для заданного режима.
    /// Порядок приоритета:
    /// 1) раздельная авторизация режима — <see cref="Infobase.EnterpriseAuth"/> для
    ///    «1С:Предприятия» / <see cref="Infobase.ConfiguratorAuth"/> для «Конфигуратора»,
    ///    если она задана (не <see cref="InfobaseAuthSettings.IsDefault"/>);
    /// 2) авторизация информационной базы (<see cref="Infobase.Connection"/>), если она
    ///    задана явно (режим не «Запрос» или заполнены пользователь/пароль);
    /// 3) общая авторизация <paramref name="commonAuth"/> (issue #57), если задана —
    ///    legacy для обоих режимов;
    /// 4) иначе — «Запрос имени и пароля» с пустыми реквизитами (платформа спросит
    ///    имя и пароль при запуске).
    /// <para>
    /// ПОВЕДЕНЧЕСКИЙ НЮАНС (issue #57): ранее <c>EnterpriseAuth != null</c> использовался
    /// даже если значения дефолтные («Запрос» с пустыми реквизитами, <see cref="InfobaseAuthSettings.IsDefault"/>).
    /// Теперь «дефолтная» раздельная авторизация считается незаданной и даёт fallback на
    /// авторизацию базы, затем на общую. Для баз, где раздельная авторизация задана
    /// логином/паролем, поведение не меняется.
    /// </para>
    /// </summary>
    public static void Resolve(Infobase infobase, OneCLaunchMode mode,
        out AuthenticationMode authMode, out string authUser, out string authPassword,
        InfobaseAuthSettings? commonAuth = null)
    {
        // Обратная совместимость: одиночный параметр общей авторизации — это legacy
        // («общая для обоих режимов»), раздельные общие не заданы.
        Resolve(infobase, mode, out authMode, out authUser, out authPassword,
            commonAuth, commonAuth, commonAuth);
    }

    /// <summary>
    /// Разрешает режим аутентификации, логин и пароль для заданного режима
    /// (раздельная общая авторизация по режимам запуска, issue #57). Порядок приоритета:
    /// 1) раздельная авторизация режима — <see cref="Infobase.EnterpriseAuth"/> для
    ///    «1С:Предприятия» / <see cref="Infobase.ConfiguratorAuth"/> для «Конфигуратора»,
    ///    если она задана (не <see cref="InfobaseAuthSettings.IsDefault"/>);
    /// 2) авторизация информационной базы (<see cref="Infobase.Connection"/>) — только если
    ///    она реально содержит учётные данные (см. ниже);
    /// 3) общая авторизация по режиму запуска (issue #57): для «1С:Предприятия» —
    ///    <paramref name="commonEnterpriseAuth"/>, для «Конфигуратора» —
    ///    <paramref name="commonConfiguratorAuth"/>; при незаданной (null или
    ///    <see cref="InfobaseAuthSettings.IsDefault"/>) — прежняя общая
    ///    <paramref name="legacyCommonAuth"/> (обратная совместимость);
    /// 4) иначе — «Запрос имени и пароля» с пустыми реквизитами.
    /// <para>
    /// ПОВЕДЕНЧЕСКИЙ НЮАНС (issue #57): «дефолтная» общая авторизация режима
    /// (IsDefault — «Запрос» с пустыми реквизитами) считается незаданной и даёт
    /// fallback на legacy, затем на авторизацию базы — порядок шагов см. выше.
    /// </para>
    /// <para>
    /// ИСПРАВЛЕНИЕ issue #57 (1.0.1.6, комментарий 7OH «общая авторизация»):
    /// авторизация ОС базы (<c>Connection.AuthenticationMode == Windows</c>, фактически
    /// «/WA+» без /N /P) больше не перекрывает явные общие учётные данные режима.
    /// Ранее база с «выключенной насильной авторизацией» возвращалась на шаге 2,
    /// и общая авторизация Credentials никогда не подставлялась. Теперь Connection
    /// перекрывает общую только если:
    /// - она задана режимом <see cref="AuthenticationMode.Credentials"/> с непустым
    ///   пользователем (собственные учётные данные базы — высший приоритет);
    /// - либо задана режимом <see cref="AuthenticationMode.Windows"/>, а общая авторизация
    ///   режима не является <see cref="AuthenticationMode.Credentials"/> (явных общих
    ///   учётных данных нет — общая Windows/запрос не спорит с авторизацией ОС базы).
    /// Во всех остальных случаях применяется общая авторизация режима (шаг 3).
    /// </para>
    /// </summary>
    public static void Resolve(Infobase infobase, OneCLaunchMode mode,
        out AuthenticationMode authMode, out string authUser, out string authPassword,
        InfobaseAuthSettings? commonEnterpriseAuth,
        InfobaseAuthSettings? commonConfiguratorAuth,
        InfobaseAuthSettings? legacyCommonAuth)
    {
        // 1) Раздельная авторизация режима. «Дефолтные» настройки (IsDefault — «Запрос»
        // с пустыми реквизитами) считаются незаданными и переходят к следующим шагам.
        if (mode == OneCLaunchMode.Enterprise
            && infobase.EnterpriseAuth is { } entAuth && !entAuth.IsDefault)
        {
            authMode = entAuth.AuthenticationMode;
            authUser = entAuth.User;
            authPassword = entAuth.Password;
            return;
        }

        if (mode == OneCLaunchMode.Configurator
            && infobase.ConfiguratorAuth is { } cfgAuth && !cfgAuth.IsDefault)
        {
            authMode = cfgAuth.AuthenticationMode;
            authUser = cfgAuth.User;
            authPassword = cfgAuth.Password;
            return;
        }

        // Эффективная общая авторизация режима (шаг 3) вычисляется заранее: она участвует
        // и в решении шага 2 (исправление issue #57), и в самом шаге 3. Состав правила
        // не менялся: раздельная общая режима → legacy → незадана.
        InfobaseAuthSettings? modeCommon =
            mode == OneCLaunchMode.Enterprise
                ? commonEnterpriseAuth is { } ent && !ent.IsDefault ? ent : legacyCommonAuth
                : commonConfiguratorAuth is { } cfg && !cfg.IsDefault ? cfg : legacyCommonAuth;

        // Общая авторизация режима содержит явные учётные данные (Credentials).
        var commonCredentials = modeCommon is { } mc && !mc.IsDefault
            && mc.AuthenticationMode == AuthenticationMode.Credentials;

        // 2) Авторизация информационной базы (Connection). Дефолтная Connection («Запрос»
        // с пустыми реквизитами) считается незаданной. Исправление issue #57 (1.0.1.6):
        // Connection перекрывает общую только если она реально содержит учётные данные —
        // Credentials с пользователем, — либо это режим ОС (Windows) без заданных общих
        // кредов (общая Windows/«Запрос» не спорит с авторизацией ОС базы). Случай
        // «база Windows + общая Credentials» больше не съедает общую авторизацию.
        var conn = infobase.Connection;
        if (conn is not null && (
                (conn.AuthenticationMode == AuthenticationMode.Credentials
                    && !string.IsNullOrWhiteSpace(conn.User))
                || (conn.AuthenticationMode == AuthenticationMode.Windows
                    && !commonCredentials)))
        {
            authMode = conn.AuthenticationMode;
            authUser = conn.User ?? string.Empty;
            authPassword = conn.Password ?? string.Empty;
            return;
        }

        // 3) Общая авторизация по режиму запуска (issue #57): раздельная для режима
        // (Enterprise — commonEnterpriseAuth, Configurator — commonConfiguratorAuth),
        // при незаданной (null или IsDefault — «Запрос» с пустыми реквизитами) —
        // прежняя общая legacyCommonAuth (обратная совместимость), затем «Запрос».
        if (modeCommon is { } a && !a.IsDefault)
        {
            authMode = a.AuthenticationMode;
            authUser = a.User;
            authPassword = a.Password;
            return;
        }

        // 4) Прежнее поведение: платформа запросит имя и пароль.
        authMode = AuthenticationMode.Prompt;
        authUser = string.Empty;
        authPassword = string.Empty;
    }

    /// <summary>
    /// Разрешает отдельные учётные данные Хранилища конфигурации (логин и пароль).
    /// В отличие от Предприятия/Конфигуратора у хранилища нет «базовой» авторизации:
    /// используются собственные реквизиты <see cref="Infobase.Repository"/> (поле ввода
    /// логина/пароля в свойствах базы), поэтому метод возвращает их напрямую. Применяется
    /// при запуске Конфигуратора для передачи ключей /ConfigurationRepositoryN и
    /// /ConfigurationRepositoryP.
    /// </summary>
    public static void ResolveRepository(Infobase infobase,
        out string repositoryUser, out string repositoryPassword)
    {
        var repo = infobase.Repository;
        repositoryUser = repo?.User ?? string.Empty;
        repositoryPassword = repo?.Password ?? string.Empty;
    }
}