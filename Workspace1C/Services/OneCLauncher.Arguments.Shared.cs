using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Платформенно-нейтральное построение аргументов командной строки 1С.
/// Используется обеими реализациями <see cref="OneCLauncher"/> — Windows (WPF)
/// и Linux (Avalonia) — чтобы не дублировать идентичную логику сборки строки
/// запуска. Поведение и порядок ключей НЕ должны отличаться от прежних
/// платформенных версий.
/// </summary>
public static partial class OneCLauncher
{
    /// <summary>
    /// Общая авторизация для всех ИБ (issue #57): fallback при незаданной
    /// авторизации ИБ. Устанавливается из настроек при старте и при сохранении.
    /// Поле остаётся legacy — применяется для обоих режимов при незаданной
    /// раздельной общей авторизации (<see cref="CommonEnterpriseAuthorization"/> /
    /// <see cref="CommonConfiguratorAuthorization"/>).
    /// </summary>
    public static InfobaseAuthSettings? CommonAuthorization { get; set; }

    /// <summary>
    /// Раздельная общая авторизация для «1С:Предприятия» (issue #57): fallback для
    /// запусков в режиме Enterprise при незаданной авторизации ИБ. При незаданной
    /// (null или IsDefault) резолвер использует прежнюю общую
    /// <see cref="CommonAuthorization"/>. Устанавливается из настроек при старте
    /// и при сохранении.
    /// </summary>
    public static InfobaseAuthSettings? CommonEnterpriseAuthorization { get; set; }

    /// <summary>
    /// Раздельная общая авторизация для «Конфигуратора» (issue #57): fallback для
    /// запусков в режиме Configurator при незаданной авторизации ИБ. При незаданной
    /// (null или IsDefault) резолвер использует прежнюю общую
    /// <see cref="CommonAuthorization"/>. Устанавливается из настроек при старте
    /// и при сохранении.
    /// </summary>
    public static InfobaseAuthSettings? CommonConfiguratorAuthorization { get; set; }

    // Синхронизация статиков с настройками (issue #57, 1.0.1.7 — гипотеза Г2 «статик
    // не заполнен на момент запуска»): путь запуска в обход главного окна (CLI --run,
    // проводник, скрипты в отдельном контексте) может резолвить учётные данные до того,
    // как MainViewModel загрузит настройки и заполнит статики.
    private static bool s_commonAuthLoaded;

    /// <summary>
    /// Помечает общие авторизации как установленные (MainViewModel загрузил настройки
    /// либо пользователь изменил общие реквизиты в UI). Повторный вызов
    /// <see cref="EnsureCommonAuthLoaded"/> после этого — no-op.
    /// </summary>
    public static void MarkCommonAuthLoaded() => s_commonAuthLoaded = true;

    /// <summary>
    /// Гарантирует заполнение статических Common*Authorization из настроек (issue #57):
    /// при ещё не загруженных настройках читает их из хранилища и устанавливает статики.
    /// При недоступных сервисах (юнит-тесты, прямой вызов) — no-op: используются текущие
    /// значения статиков.
    /// </summary>
    public static void EnsureCommonAuthLoaded()
    {
        if (s_commonAuthLoaded)
            return;
        try
        {
            var repository = AppServices.TryGetService<IInfobaseRepository>();
            if (repository is null)
                return;
            var settings = repository.LoadSettings();
            CommonAuthorization = settings.CommonAuthorization;
            CommonEnterpriseAuthorization = settings.CommonEnterpriseAuth;
            CommonConfiguratorAuthorization = settings.CommonConfiguratorAuth;
            s_commonAuthLoaded = true;
        }
        catch
        {
            // Настройки недоступны — используем текущие значения статиков.
        }
    }

    /// <summary>
    /// Проверяет, можно ли безопасно подставить значение внутрь кавычек ключа командной строки
    /// 1С вида /Key"value".
    /// ВАЖНО: грамматика таких ключей — НЕ грамматика строки подключения. Внутри значения кавычку
    /// экранировать удвоением («""») НЕЛЬЗЯ: для ключа командной строки это неверно, и 1С получит
    /// искажённое значение. Поэтому «"» внутри значения — единственный реальный вектор инъекции
    /// дополнительного /ключа 1cv8 (можно «вырваться» из кавычек). Пробелы внутри значения
    /// безопасны (остаются внутри кавычек и не создают новых аргументов). Также отклоняются
    /// управляющие символы (CR/LF/…), способные нарушить разбор командной строки.
    /// Если метод вернул false, корректно представить значение в этой грамматике невозможно —
    /// такой аргумент нужно отбросить/отказаться, а НЕ «экранировать».
    /// </summary>
    private static bool IsSafeCliValue(string? value)
        => !string.IsNullOrEmpty(value) &&
           value!.IndexOf('"') < 0 &&
           !value.Any(c => char.IsControl(c));

    /// <summary>
    /// Собирает /N"user" /P"password". Небезопасное значение (содержит «"» или управляющий символ)
    /// опускается, чтобы не допустить инъекции аргумента — см. <see cref="IsSafeCliValue"/>.
    /// </summary>
    private static string BuildCredentialsArg(string user, string password)
    {
        if (!IsSafeCliValue(user))
            return "";
        var auth = $" /N\"{user}\"";
        if (!string.IsNullOrEmpty(password) && IsSafeCliValue(password))
            auth += $" /P\"{password}\"";
        return auth;
    }

    // ---------------------------------------------------------------------------
    // «Запуск с аутентификацией» (issue #48, комментарий 7OH 23.10): разовый запрос
    // имени и пароля. Флаг ставится на конкретную базу и «съедается» первой же
    // сборкой аргументов для неё. Флаг, а не временная очистка учётных данных базы:
    // pre-команда может выполняться асинхронно до 30 секунд, и прежнее восстановление
    // в finally успевало вернуть сохранённые данные раньше фактического запуска; к тому
    // же очищенные данные успевали сохраниться на диск. Срок жизни ограничен, чтобы
    // несостоявшийся запуск (не найдена платформа и т.п.) не повлиял на следующий.
    // ---------------------------------------------------------------------------

    private static readonly ConditionalWeakTable<Infobase, StrongBox<DateTime>> s_authPromptRequests = new();

    /// <summary>Сколько живёт неиспользованный запрос авторизации (pre-команда ждёт до 30 с).</summary>
    internal static readonly TimeSpan AuthPromptRequestLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Ключи авторизации, которые при запуске с аутентификацией не передаются ни из
    /// параметров базы, ни из 1CLaunch.cfg: имя/пароль и аутентификация ОС.
    /// </summary>
    private static readonly string[] AuthPromptSuppressedKeys = { "/N", "/P", "/WA", "/WA+", "/WA-" };

    /// <summary>
    /// Запрашивает для ближайшего запуска базы ввод имени и пароля: сохранённые
    /// учётные данные не подставляются, автовход по учётной записи ОС отключается
    /// ключом <c>/WA-</c> — платформа обязательно спросит пользователя и пароль.
    /// </summary>
    public static void RequestAuthPrompt(Infobase infobase)
    {
        if (infobase is null)
            return;
        s_authPromptRequests.AddOrUpdate(infobase, new StrongBox<DateTime>(DateTime.UtcNow));
    }

    /// <summary>Снимает неиспользованный запрос авторизации базы.</summary>
    public static void CancelAuthPrompt(Infobase infobase)
    {
        if (infobase is not null)
            s_authPromptRequests.Remove(infobase);
    }

    /// <summary>Забирает запрос авторизации базы (true — запрос был и ещё действителен).</summary>
    internal static bool ConsumeAuthPrompt(Infobase infobase)
    {
        if (!s_authPromptRequests.TryGetValue(infobase, out var box))
            return false;
        s_authPromptRequests.Remove(infobase);
        return DateTime.UtcNow - box.Value <= AuthPromptRequestLifetime;
    }

    private static bool IsAuthPromptSuppressedKey(string key)
        => AuthPromptSuppressedKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Убирает из строки параметров ключи /N, /P и /WA± (для запуска с аутентификацией).</summary>
    internal static string StripAuthKeys(string parameters)
    {
        var parsed = OneCLaunchArgumentParser.ParseLaunchParameters(parameters);
        if (!parsed.Any(a => IsAuthPromptSuppressedKey(a.Key)))
            return parameters;
        return OneCLaunchArgumentParser.FormatLaunchParameters(
            parsed.Where(a => !IsAuthPromptSuppressedKey(a.Key)));
    }

    /// <summary>Формирует аргументы командной строки для запуска 1С.</summary>
    internal static string BuildArguments(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode)
    {
        var modeArg = mode switch
        {
            OneCLaunchMode.Enterprise => "ENTERPRISE",
            _ => "DESIGNER"
        };

        // Параметр режима форм применяется только в режиме «Предприятие».
        // Явно заданный runMode имеет приоритет; иначе режим выводится из типа клиента
        // (тонкий → управляемые, толстый → обычные). Если задано и runMode, и clientType —
        // они независимы, что соответствует 1С («толстый клиент в управляемом приложении»).
        // null (автоматический выбор платформы) — параметр /RunMode не передаётся.
        var clientArg = mode == OneCLaunchMode.Enterprise && (runMode.HasValue || clientType.HasValue)
            ? (runMode ?? (clientType == OneCClientType.Thin ? OneCRunMode.Managed : OneCRunMode.Ordinary)) switch
            {
                OneCRunMode.Managed => " /RunModeManagedApplication",
                _ => " /RunModeOrdinaryApplication"
            }
            : "";

        var conn = infobase.Connection;
        // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
        // кавычку внутри значения удвоением не экранируют — поэтому небезопасное значение
        // (с «"») не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
        // /S "server\base" — server может быть host:port при нестандартном порте.
        string connectionArg = conn.Type switch
        {
            ConnectionType.File => IsSafeCliValue(conn.FilePath) ? $" /F \"{conn.FilePath}\"" : "",
            ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl) ? $" /WS \"{conn.WebUrl}\"" : "",
            _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                ? $" /S \"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                : ""
        };

        // Учётные данные выбираются единым резолвингом (issue #236, #57): раздельная авторизация
        // Конфигуратора/Предприятия (EnterpriseAuth/ConfiguratorAuth), иначе авторизация базы,
        // а при незаданной — общая авторизация по режиму запуска (issue #57): раздельная
        // CommonEnterpriseAuthorization/CommonConfiguratorAuthorization, при незаданной —
        // legacy CommonAuthorization.
        // Синхронизация статиков общих авторизаций (issue #57, 1.0.1.7): путь запуска
        // в обход главного окна (CLI --run, проводник) не заполняет их через MainViewModel.
        EnsureCommonAuthLoaded();

        // Источник учётных данных (issue #57, 1.0.1.7): какой шаг резолвера вернул
        // авторизацию — видно по журналу без пароля.
        InfobaseAuthResolver.Resolve(infobase, mode, out var authMode, out var authUser, out var authPassword,
            CommonEnterpriseAuthorization, CommonConfiguratorAuthorization, CommonAuthorization,
            out var authSource);

        // Диагностика (issue #57, 1.0.1.6/1.0.1.7): режим авторизации базы, источник
        // и наличие пользователя пишутся в журнал без пароля — чтобы следующий репорт
        // «не подставляется общая авторизация» был диагностируемым с первого сообщения
        // (направление CM_BASE_RUN).
        Log.Info(LogCategory.BaseRun,
            $"[{infobase.Name}] Авторизация запуска ({mode}): режим={authMode}, " +
            $"источник={InfobaseAuthResolver.DescribeSource(authSource)}, " +
            $"режим базы={infobase.Connection?.AuthenticationMode.ToString() ?? "—"}, " +
            $"пользователь={(string.IsNullOrWhiteSpace(authUser) ? "не задан" : "задан")}.");

        // Запуск с аутентификацией (issue #48): ни сохранённых имени/пароля, ни входа
        // по учётной записи ОС — /WA- заставляет платформу спросить имя и пароль, даже
        // если у пользователя ИБ включена аутентификация операционной системы.
        var forceAuthPrompt = ConsumeAuthPrompt(infobase);

        string authArg = forceAuthPrompt ? " /WA-" : authMode switch
        {
            AuthenticationMode.Credentials when !string.IsNullOrWhiteSpace(authUser)
                => BuildCredentialsArg(authUser, authPassword),
            AuthenticationMode.Windows
                => " /WA+",
            _ => ""
        };

        // Подключение к хранилищу конфигурации (только в режиме «Конфигуратор»):
        // /ConfigurationRepositoryF "<путь>" — путь к хранилищу. Для серверного хранилища
        // путь имеет вид tcp://сервер:порт/имяХранилища (из Repository.Server + RepositoryName);
        // /ConfigurationRepositoryN — пользователь хранилища; /ConfigurationRepositoryP — пароль.
        // Аргументы добавляются, только если задан адрес сервера хранилища.
        string repositoryArg = "";
        var repo = infobase.Repository;
        if (mode == OneCLaunchMode.Configurator && repo.HasServer)
        {
            var server = repo.Server.Trim().TrimEnd('/');
            var name = (repo.RepositoryName ?? string.Empty).Trim();
            var repoPath = string.IsNullOrWhiteSpace(name) ? server : $"{server}/{name}";
            // Учётные данные Хранилища конфигурации выбираются единым резолвингом (Этап 12,
            // функция №7 StartManager): отдельные логин/пароль хранилища (RepositorySettings),
            // передаваемые ключами /ConfigurationRepositoryN и /ConfigurationRepositoryP.
            InfobaseAuthResolver.ResolveRepository(infobase, out var repoUser, out var repoPassword);
            // Значения /ConfigurationRepository* тоже идут по грамматике ключа (не строки
            // подключения): небезопасное значение (с «"») не подставляется (см. IsSafeCliValue).
            if (IsSafeCliValue(repoPath))
                repositoryArg = $" /ConfigurationRepositoryF \"{repoPath}\"";
            if (IsSafeCliValue(repoUser))
            {
                repositoryArg += $" /ConfigurationRepositoryN \"{repoUser}\"";
                if (IsSafeCliValue(repoPassword))
                    repositoryArg += $" /ConfigurationRepositoryP \"{repoPassword}\"";
            }
        }

        // Внешняя обработка, запускаемая при открытии ИБ в режиме «1С:Предприятие»
        // (функция №25 StartManager). Передаётся ключом /Execute "<путь>" и при необходимости
        // /C "<данные>" (данные обработки). Работает только в режиме «Предприятие» — в
        // «Конфигураторе» эти ключи неприменимы. Значение идёт по грамматике ключа командной
        // строки: небезопасное значение (с «"») опускается (см. IsSafeCliValue).
        string externalArg = "";
        if (mode == OneCLaunchMode.Enterprise &&
            !string.IsNullOrWhiteSpace(infobase.ExternalProcessingPath))
        {
            var path = infobase.ExternalProcessingPath.Trim();
            if (IsSafeCliValue(path))
            {
                externalArg = $" /Execute \"{path}\"";
                if (!string.IsNullOrWhiteSpace(infobase.ExternalProcessingData) &&
                    IsSafeCliValue(infobase.ExternalProcessingData))
                {
                    externalArg += $" /C \"{infobase.ExternalProcessingData.Trim()}\"";
                }
            }
        }

        var extraParameters = infobase.LaunchParameters?.Trim() ?? string.Empty;
        if (forceAuthPrompt && extraParameters.Length > 0)
            extraParameters = StripAuthKeys(extraParameters).Trim();
        var extraArg = extraParameters.Length == 0 ? "" : " " + extraParameters;

        var arguments = $"{modeArg}{clientArg}{connectionArg}{authArg}{repositoryArg}{externalArg}{extraArg}";

        // Параметры по умолчанию из 1CLaunch.cfg (Этап 10 StartManager) подставляются
        // лаунчером, если соответствующие ключи уже не заданы параметрами самой базы
        // / командной строкой (приоритет командной строки над файлом). Для этого разбираем
        // уже собранную строку и добавляем только те ключи, которых в ней ещё нет.
        var defaults = OneCLaunchConfigReader.ToLaunchArguments(
            OneCLaunchConfigReader.ReadDefaults());
        if (defaults.Count > 0)
        {
            var existingKeys = new System.Collections.Generic.HashSet<string>(
                OneCLaunchArgumentParser.ParseLaunchParameters(arguments)
                    .Select(a => a.Key),
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var d in defaults)
            {
                if (existingKeys.Contains(d.Key))
                    continue;
                if (forceAuthPrompt && IsAuthPromptSuppressedKey(d.Key))
                    continue;
                arguments += d.HasValue ? $" {d.Key} \"{d.Value}\"" : $" {d.Key}";
            }
        }

        return arguments;
    }

    /// <summary>
    /// Собирает общий блок аргументов подключения к хранилищу конфигурации:
    /// /ConfigurationRepositoryF "путь" /ConfigurationRepositoryN "user" /ConfigurationRepositoryP "pwd".
    /// Путь строится из <see cref="Infobase.Repository"/> (Server + RepositoryName); логин/пароль
    /// хранилища — через единый резолвинг <see cref="InfobaseAuthResolver.ResolveRepository"/>
    /// и возвращаются через <paramref name="repositoryUser"/>/<paramref name="repositoryPassword"/>
    /// (нужны сервису для маскирования пароля в командной строке операции).
    /// Значения идут по грамматике ключа (не строки подключения): небезопасное значение (с «"»)
    /// не подставляется (см. <see cref="IsSafeCliValue"/>). Возвращает пустую строку, если адрес
    /// хранилища не задан или безопасно представить его невозможно.
    /// </summary>
    private static string BuildRepositoryArguments(Infobase infobase,
        out string repositoryUser, out string repositoryPassword)
    {
        repositoryUser = string.Empty;
        repositoryPassword = string.Empty;

        var repo = infobase.Repository;
        if (repo is null || !repo.HasServer)
            return "";

        var server = repo.Server.Trim().TrimEnd('/');
        var name = (repo.RepositoryName ?? string.Empty).Trim();
        var repoPath = string.IsNullOrWhiteSpace(name) ? server : $"{server}/{name}";
        if (!IsSafeCliValue(repoPath))
            return "";

        // Учётные данные Хранилища конфигурации выбираются единым резолвингом (Этап 12,
        // функция №7 StartManager): отдельные логин/пароль хранилища (RepositorySettings).
        InfobaseAuthResolver.ResolveRepository(infobase, out repositoryUser, out repositoryPassword);

        var arg = $" /ConfigurationRepositoryF \"{repoPath}\"";
        if (IsSafeCliValue(repositoryUser))
        {
            arg += $" /ConfigurationRepositoryN \"{repositoryUser}\"";
            if (IsSafeCliValue(repositoryPassword))
                arg += $" /ConfigurationRepositoryP \"{repositoryPassword}\"";
        }
        return arg;
    }

    /// <summary>
    /// Собирает аргументы пакетного обновления конфигурации из хранилища конфигурации:
    /// общий блок F/N/P (см. <see cref="BuildRepositoryArguments"/>) +
    /// /ConfigurationRepositoryUpdateCfg /UpdateDBCfg.
    /// Поведение идентично прежней реализации (0.3.9.88): загрузка конфигурации из хранилища
    /// и обновление конфигурации БД; /ConfigurationRepositoryDumpCfg НЕ используется.
    /// Возвращает пустую строку, если адрес хранилища не задан или безопасно представить его
    /// невозможно.
    /// </summary>
    public static string BuildRepositoryUpdateArgument(Infobase? infobase)
    {
        if (infobase is null)
            return "";

        var arg = BuildRepositoryArguments(infobase, out _, out _);
        if (string.IsNullOrEmpty(arg))
            return "";

        arg += " /ConfigurationRepositoryUpdateCfg /UpdateDBCfg";
        return arg;
    }

    /// <summary>
    /// Собирает аргументы выгрузки версии хранилища конфигурации в файл .cf (0.3.9.127,
    /// «Обозреватель хранилища конфигурации»): общий блок F/N/P +
    /// /ConfigurationRepositoryDumpCfg"файл.cf" [-v N].
    /// ВАЖНО (разведка этапа 1, платформа 8.3.27.2325): параметры -v/-objects/-NBegin/-NEnd
    /// передаются с дефисом и пробелом перед значением (« -v N») — уникальная грамматика
    /// repository-команд, в отличие от обычных ключей вида /DumpIB"path" (значение сразу
    /// в кавычках). Без -v (или с -v -1) выгружается актуальная версия хранилища.
    /// Значения — через <see cref="IsSafeCliValue"/>.
    /// </summary>
    public static string BuildRepositoryDumpCfgArgument(Infobase infobase, string cfPath, int? version)
    {
        var arg = BuildRepositoryArguments(infobase, out _, out _);
        if (string.IsNullOrEmpty(arg) || !IsSafeCliValue(cfPath))
            return "";
        arg += $" /ConfigurationRepositoryDumpCfg\"{cfPath}\"";
        if (version.HasValue)
            arg += $" -v {version.Value}";
        return arg;
    }

    /// <summary>
    /// Собирает аргументы отчёта по истории хранилища (/ConfigurationRepositoryReport "файл"):
    /// общий блок F/N/P + /ConfigurationRepositoryReport"файл" [-NBegin N] [-NEnd N].
    /// Параметры -NBegin/-NEnd — с дефисом и пробелом перед значением (грамматика repository).
    /// Отчёт формируется платформой в виде табличного документа (.mxl); текстовые форматы
    /// (.txt/.html) не документированы (см. разведку этапа 1), поэтому парсер
    /// <see cref="RepositoryHistoryParser"/> носит запасной характер, а при недоступности
    /// читаемого текста история ограничивается актуальной версией.
    /// </summary>
    public static string BuildRepositoryReportArgument(Infobase infobase, string reportPath, int? nBegin, int? nEnd)
    {
        var arg = BuildRepositoryArguments(infobase, out _, out _);
        if (string.IsNullOrEmpty(arg) || !IsSafeCliValue(reportPath))
            return "";
        arg += $" /ConfigurationRepositoryReport\"{reportPath}\"";
        if (nBegin.HasValue)
            arg += $" -NBegin {nBegin.Value}";
        if (nEnd.HasValue)
            arg += $" -NEnd {nEnd.Value}";
        return arg;
    }

    /// <summary>
    /// Собирает аргументы захвата объектов хранилища (/ConfigurationRepositoryLock):
    /// общий блок F/N/P + /ConfigurationRepositoryLock [-objects"файл.xml"].
    /// Без -objects захватываются все объекты конфигурации; с -objects — только перечисленные
    /// в XML-файле (формат файла НЕ документирован платформой — экспериментальный, см.
    /// комментарий разведки в <see cref="OneCLauncher.DesignerBatch"/>).
    /// Параметр -objects — с дефисом и пробелом перед значением (грамматика repository);
    /// значение — через <see cref="IsSafeCliValue"/>.
    /// </summary>
    public static string BuildRepositoryLockArgument(Infobase infobase, string? objectsXmlPath)
    {
        var arg = BuildRepositoryArguments(infobase, out _, out _);
        if (string.IsNullOrEmpty(arg))
            return "";
        arg += " /ConfigurationRepositoryLock";
        if (!string.IsNullOrEmpty(objectsXmlPath) && IsSafeCliValue(objectsXmlPath))
            arg += $" -objects\"{objectsXmlPath}\"";
        return arg;
    }

    /// <summary>
    /// Собирает аргументы отмены захвата объектов хранилища (/ConfigurationRepositoryUnlock):
    /// общий блок F/N/P + /ConfigurationRepositoryUnlock [-objects"файл.xml"].
    /// Без -objects отменяется захват всех объектов; с -objects — только перечисленных
    /// в XML-файле (формат не документирован — экспериментальный). Грамматика и безопасность
    /// значений — как в <see cref="BuildRepositoryLockArgument"/>.
    /// </summary>
    public static string BuildRepositoryUnlockArgument(Infobase infobase, string? objectsXmlPath)
    {
        var arg = BuildRepositoryArguments(infobase, out _, out _);
        if (string.IsNullOrEmpty(arg))
            return "";
        arg += " /ConfigurationRepositoryUnlock";
        if (!string.IsNullOrEmpty(objectsXmlPath) && IsSafeCliValue(objectsXmlPath))
            arg += $" -objects\"{objectsXmlPath}\"";
        return arg;
    }

    /// <summary>
    /// Аргументы /N /P при режиме Credentials.
    /// Пакетные операции конфигуратора (выгрузка .dt/.cf, установка расширений) выполняются
    /// в режиме «Конфигуратор»: единый резолвинг учётных данных (issue #236, #57) сам возьмёт
    /// ConfiguratorAuth, если она задана, иначе авторизацию информационной базы, а при
    /// незаданной — общую авторизацию «Конфигуратора» (CommonConfiguratorAuthorization),
    /// затем legacy <see cref="CommonAuthorization"/> (issue #87: «The infobase user is not
    /// authenticated», когда логин/пароль заданы только в общей авторизации приложения).
    /// </summary>
    public static string BuildAuthArgument(Infobase infobase)
    {
        // Синхронизация статиков общих авторизаций (issue #57, 1.0.1.7) — пакетные
        // операции конфигуратора идут в обход главного окна.
        EnsureCommonAuthLoaded();
        InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Configurator,
            out var authMode, out var authUser, out var authPassword,
            CommonEnterpriseAuthorization, CommonConfiguratorAuthorization, CommonAuthorization);
        if (authMode != AuthenticationMode.Credentials || string.IsNullOrWhiteSpace(authUser))
            return "";
        return BuildCredentialsArg(authUser, authPassword);
    }

    /// <summary>Аргументы командной строки для ярлыка «как у стандартного стартера 1С».</summary>
    public static string BuildEnterpriseShortcutArguments(Infobase infobase)
    {
        var args = $"ENTERPRISE {BuildConnectionArgument(infobase)}{BuildAuthArgument(infobase)}";
        if (!string.IsNullOrWhiteSpace(infobase.LaunchParameters))
            args += " " + infobase.LaunchParameters.Trim();
        return args;
    }

    /// <summary>
    /// Экранирует значение для строки подключения 1С: кавычка внутри значения удваивается.
    /// </summary>
    private static string EscapeConnectValue(string value) => value.Replace("\"", "\"\"");

    /// <summary>
    /// Разворачивает экранирование строки подключения 1С: удвоенная кавычка «""» снова
    /// становится одной. Обратная операция к <see cref="EscapeConnectValue"/>.
    /// </summary>
    private static string UnescapeConnectValue(string value) => value.Replace("\"\"", "\"");

    /// <summary>
    /// Удаляет только что созданный пустой каталог файловой базы, если CREATEINFOBASE не удался.
    /// Затрагивает лишь каталог, созданный в этой попытке, и только если он остался пустым.
    /// </summary>
    private static void CleanupCreatedDir(string? dirPath)
    {
        if (string.IsNullOrEmpty(dirPath))
            return;
        try
        {
            if (Directory.Exists(dirPath) &&
                !Directory.EnumerateFileSystemEntries(dirPath).Any())
            {
                Directory.Delete(dirPath);
            }
        }
        catch
        {
            /* Не критично: каталог мог быть занят или уже удалён. */
        }
    }

    /// <summary>
    /// Отбрасывает завершающий сегмент локали из пути web-ссылки
    /// (issue #332): «https://host/base/ru_RU/» → «https://host/base/».
    /// Шаблон <c>/([a-z]{2,3})_[A-Z]{2}</c> применяется ТОЛЬКО к конечному сегменту
    /// пути (часть до '?'/'#'), поэтому сегмент вида ru_RU в середине пути,
    /// query/fragment и trailing-slash без локали не затрагиваются.
    /// Возвращает исходную строку, если сегмент локали не найден.
    /// </summary>
    internal static string? StripWebLocaleSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var url = value!.Trim();

        // Query/fragment к пути не относятся: регекс применяется только к части пути.
        var pathEnd = url.Length;
        for (var i = 0; i < url.Length; i++)
        {
            if (url[i] == '?' || url[i] == '#')
            {
                pathEnd = i;
                break;
            }
        }

        var pathPart = url.Substring(0, pathEnd);
        var suffix = url.Substring(pathEnd);

        // Конечный сегмент вида /ru_RU[/], /en_US — регистр значим (шаблон из issue):
        // /([a-z]{2,3})_[A-Z]{2} сразу после слеша, далее конец пути или завершающий слеш.
        var match = Regex.Match(pathPart, @"/([a-z]{2,3})_[A-Z]{2}/?$", RegexOptions.CultureInvariant);
        if (!match.Success)
            return value;

        // Удаляем сегмент вместе со слешем перед ним; завершающий слеш сохраняем.
        var trimmed = pathPart.Substring(0, match.Index);
        if (!trimmed.EndsWith("/", StringComparison.Ordinal))
            trimmed += "/";

        return trimmed + suffix;
    }
}