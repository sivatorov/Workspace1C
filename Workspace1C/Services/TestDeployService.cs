using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Оркестратор сценария «Развернуть тестовую копию» (п.10, 1.0.0.92): связывает уже
/// готовые куски — выгрузку .dt (движок конфигуратора или ibcmd), создание новой ИБ
/// (клиент-серверной через CREATEINFOBASE или файловой), загрузку копии в целевую базу
/// (RestoreIB), выключение регламентных заданий в копии (<see cref="ScheduledJobsService"/>)
/// и применение отличительных признаков (заголовок «Тест: …» и группа «Тест» с цветом).
/// <para>
/// «Разумное поведение» повторного запуска: если тестовая база уже есть в списке —
/// она обновляется данными (без пересоздания), если отсутствует — создаётся. Временный
/// каталог <c>%TEMP%\cm_testdeploy_<guid></c> гарантированно удаляется в <c>finally</c>.
/// Секреты (пароли СУБД/пользователя ИБ) в журнал не выводятся.
/// </para>
/// </summary>
public sealed class TestDeployService
{
    /// <summary>Таймаут ожидания одного запуска 1cv8 DESIGNER (DumpIB / RestoreIB).</summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Таймаут создания ИБ на сервере (CREATEINFOBASE, большие конфигурации).</summary>
    private const int CreateInfoBaseTimeoutMs = 30 * 60 * 1000;

    private readonly IAppLogger? _logger;

    public TestDeployService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Выполняет развёртывание тестовой копии. Возвращает <see cref="TestDeployResult"/>:
    /// при ошибке шага результат содержит понятное сообщение (<see cref="TestDeployResult.ErrorMessage"/>),
    /// а цепочка уже выполненных шагов видна в журнале. Целевая запись списка и группа
    /// «Тест» возвращаются в результате — их сохраняет вызывающий (окно или планировщик).
    /// </summary>
    public Task<TestDeployResult> DeployAsync(
        TestDeployScenario scenario,
        Infobase source,
        IReadOnlyList<Infobase> allInfobases,
        IReadOnlyList<Group>? groups,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (scenario is null)
            throw new ArgumentNullException(nameof(scenario));
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return Task.Run(async () =>
        {
            var result = new TestDeployResult();

            // Валидация до любых действий (понятные ошибки шага «до старта»).
            var existing = TestDeployPlanner.FindExistingTarget(
                allInfobases, scenario, TestDeployPlanner.ResolveTargetName(scenario, source.Name));

            // issue #79 (v1.0.1.23): pre-валидация асинхронная, с rac-веткой — если
            // профиля СУБД нет и поля формы не описывают источник, перед ошибкой
            // выполняется лёгкая rac-проба (как позже на шаге резолва); шаги пробы
            // журналируются в окно мастера и в журнал приложения.
            void PreValidateLog(string message)
            {
                log?.Invoke(message);
                _logger?.Info(message);
            }

            var validation = await TestDeployPlanner.ValidateAsync(
                scenario, source, existing,
                // Issue #79 (v1.0.1.26, регресс v1.0.1.25): pre-валидация должна получать
                // креды ИБ с формы и тип СУБД формы — так же, как резолв перед «Выгрузкой»
                // (ResolveSourceDumpProfileAsync); иначе rac-проба шла без учётных данных
                // и падала с «Недостаточно прав пользователя» при верных кредах.
                racProfileResolverAsync: (ib, ct) =>
                    ResolveRacProfileForValidationAsync(ib, scenario, ct, PreValidateLog),
                log: PreValidateLog,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (validation.Count > 0)
            {
                result.ErrorMessage = string.Join(
                    Environment.NewLine,
                    validation.Select(TestDeployPlannerKey));
                return result;
            }

            var groupPlan = TestDeployPlanner.EnsureGroup(groups, scenario);
            var targetName = TestDeployPlanner.ResolveTargetName(scenario, source.Name);
            result.Steps = TestDeployPlanner.DescribeSteps(scenario, existing, groupPlan);
            result.Log.Add(string.Format(T("TestDeploy.StartedFrom"), source.Name, targetName));

            // Issue #79 (v1.0.1.21): параметры СУБД для выгрузки ИСХОДНОЙ базы разрешаются
            // ДО старта операций: профиль → rac → совпадающие с источником поля формы.
            // Если данных нет — понятная ошибка валидации до шага «Выгрузка», целевая
            // база не создаётся наполовину.
            DbmsBackupProfile? dumpProfile = null;
            if (scenario.DumpEngine == BackupEngine.Ibcmd
                && source.Connection?.Type == ConnectionType.ClientServer)
            {
                // Issue #79 (репорт 7OH «после ОК ничего не происходит, в логах тихо»):
                // шаги резолва СУБД дублируются в result.Log (окно показывает журнал
                // после завершения) и в журнал приложения (_logger) — чтобы «тихий»
                // ранний выход до шага «Выгрузка» был виден и в ZIP-диагностике.
                void ResolveLog(string message)
                {
                    log?.Invoke(message);
                    result.Log.Add(message);
                    _logger?.Info(message);
                }

                var (profile, profileError) = await ResolveSourceDumpProfileAsync(
                    source, scenario, ResolveLog, cancellationToken).ConfigureAwait(false);
                if (profileError is not null)
                {
                    result.ErrorMessage = profileError + "\n" + T("TestDeploy.IbcmdCredentialsHint");
                    ResolveLog(result.ErrorMessage);
                    return result;
                }
                dumpProfile = profile;
            }

            var tmpRoot = Path.Combine(Path.GetTempPath(), "cm_testdeploy_" + Guid.NewGuid().ToString("N"));
            try
            {
                // ---- Шаг 1: выгрузка исходной базы в .dt (движок из сценария). ----
                log?.Invoke(T("TestDeploy.Step.Dump"));
                result.Log.Add(T("TestDeploy.Step.Dump"));
                cancellationToken.ThrowIfCancellationRequested();

                var dtPath = TestDeployPlanner.BuildDumpPath(tmpRoot);
                var dumpError = await DumpAsync(source, scenario, dtPath, dumpProfile, cancellationToken).ConfigureAwait(false);
                if (dumpError is not null)
                {
                    result.ErrorMessage = Fail(T("TestDeploy.Step.Dump"), dumpError);
                    // issue #79 (последний комментарий 7OH «То есть я должен закрыть окно
                    // со всеми введёнными данными и пойти найти где это вводится?»): текст
                    // подсказки, где задать учётные данные для выгрузки через ibcmd.
                    result.ErrorMessage += "\n" + T("TestDeploy.IbcmdCredentialsHint");
                    return result;
                }

                // ---- Шаг 2 (только создание): группа «Тест» до создания базы. ----
                if (existing is null && groupPlan.NewGroup is not null)
                {
                    log?.Invoke(T("TestDeploy.Step.EnsureGroup"));
                    result.Log.Add(T("TestDeploy.Step.EnsureGroup"));
                    result.GroupCreated = true;
                    result.GroupPath = groupPlan.GroupPath;
                }

                // ---- Шаг 3: создание новой тестовой ИБ или обновление существующей. ----
                Infobase target;
                if (existing is null)
                {
                    log?.Invoke(T("TestDeploy.Step.Create"));
                    result.Log.Add(T("TestDeploy.Step.Create"));
                    cancellationToken.ThrowIfCancellationRequested();

                    var createError = CreateTarget(scenario, source, targetName, groupPlan.GroupPath, out target);
                    if (createError is not null)
                    {
                        result.ErrorMessage = Fail(T("TestDeploy.Step.Create"), createError);
                        return result;
                    }
                    result.TargetCreated = true;
                }
                else
                {
                    log?.Invoke(T("TestDeploy.Step.Update"));
                    result.Log.Add(T("TestDeploy.Step.Update"));
                    target = existing;
                    result.TargetUpdated = true;
                }

                // ---- Шаг 4: загрузка .dt в целевую базу (RestoreIB). ----
                log?.Invoke(T("TestDeploy.Step.Restore"));
                result.Log.Add(T("TestDeploy.Step.Restore"));
                cancellationToken.ThrowIfCancellationRequested();

                var restoreError = await RestoreAsync(target, dtPath, cancellationToken).ConfigureAwait(false);
                if (restoreError is not null)
                {
                    // База могла быть создана до сбоя загрузки — предупреждение без автоудаления.
                    result.ErrorMessage = Fail(T("TestDeploy.Step.Restore"), restoreError);
                    if (result.TargetCreated)
                        result.Warnings.Add(T("TestDeploy.PartialCreatedWarning"));
                    return result;
                }

                // ---- Шаг 5: выключение регламентных заданий в копии (1.0.0.85). ----
                if (scenario.DisableScheduledJobs)
                {
                    log?.Invoke(T("TestDeploy.Step.DisableJobs"));
                    result.Log.Add(T("TestDeploy.Step.DisableJobs"));
                    await DisableScheduledJobsAsync(target, result).ConfigureAwait(false);
                }

                // ---- Шаг 6: отличительные признаки (заголовок/группа/цвет). ----
                if (scenario.ChangeTitle || scenario.ChangeColor)
                {
                    log?.Invoke(T("TestDeploy.Step.Markers"));
                    result.Log.Add(T("TestDeploy.Step.Markers"));
                }

                target.Name = targetName;
                if (scenario.AddToTestGroup && !string.IsNullOrWhiteSpace(groupPlan.GroupPath))
                {
                    target.Group = groupPlan.GroupPath;
                    result.GroupPath = groupPlan.GroupPath;
                }

                log?.Invoke(T("TestDeploy.Step.AddToGroup"));
                result.Log.Add(
                    scenario.AddToTestGroup && !string.IsNullOrWhiteSpace(groupPlan.GroupPath)
                        ? string.Format(T("TestDeploy.AddedToGroup"), groupPlan.GroupPath)
                        : T("TestDeploy.GroupDisabled"));

                result.Success = true;
                result.Target = target;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = T("Designer.Cancelled");
                return result;
            }
            catch (Exception ex)
            {
                _logger?.Error("Ошибка развёртывания тестовой копии", ex);
                result.ErrorMessage = Fail(T("TestDeploy.Step.Unknown"), ex.Message);
                return result;
            }
            finally
            {
                TryDeleteDirectory(tmpRoot);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Создаёт целевую (тестовую) ИБ: клиент-серверную через CREATEINFOBASE (шаблон
    /// <see cref="ServerCloneService"/>) или файловую через CREATEINFOBASE файлового режима
    /// (механизм «Дублировать базу» / <see cref="CreateInfobaseService"/>). Возвращает
    /// локализованную ошибку или запись <paramref name="target"/>.
    /// </summary>
    private string? CreateTarget(
        TestDeployScenario scenario,
        Infobase source,
        string targetName,
        string groupPath,
        out Infobase target)
    {
        target = new Infobase();

        var platform = (source.PlatformVersion ?? string.Empty).Trim();
        if (scenario.TargetMode == TestDeployTargetMode.Server)
        {
            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: false,
                filePath: null,
                server: scenario.ServerAddress,
                databaseName: scenario.DatabaseName,
                templatePath: null,
                dbms: scenario.Dbms,
                dbServer: scenario.DbServer,
                dbName: scenario.DbName,
                dbUser: scenario.DbUser,
                dbPassword: scenario.DbPassword,
                createSqlDatabase: scenario.CreateSqlDatabase,
                blockScheduledJobs: scenario.DisableScheduledJobs,
                timeoutMs: CreateInfoBaseTimeoutMs);
            if (!ok)
                return error ?? T("TestDeploy.CreateFailed");

            var connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                DatabaseName = scenario.DatabaseName,
                BlockScheduledJobs = scenario.DisableScheduledJobs
            };
            // «host:port» разбирается на Server и Port (как при импорте из ibases.v8i).
            ConnectionSettings.ParseServerAndPort(scenario.ServerAddress, connection);
            target.Connection = connection;
        }
        else
        {
            var filePath = (scenario.FilePath ?? string.Empty).Trim();
            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: true,
                filePath: filePath,
                server: null,
                databaseName: null,
                timeoutMs: CreateInfoBaseTimeoutMs);
            if (!ok)
                return error ?? T("TestDeploy.CreateFailed");

            target.Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = filePath
            };
        }

        target.Id = Guid.NewGuid().ToString("N");
        target.Name = targetName;
        target.Group = groupPath;
        target.PlatformVersion = platform;
        target.Architecture = source.Architecture;
        // Авторизации записи наследуются от источника: единый вход для пользователя
        // (база данных после RestoreIB пуста — стандартное поведение платформы).
        target.Connection.User = source.Connection?.User;
        target.Connection.Password = source.Connection?.Password;
        target.Connection.AuthenticationMode = source.Connection?.AuthenticationMode ?? target.Connection.AuthenticationMode;
        return null;
    }

    /// <summary>Выгрузка .dt движком из сценария (конфигуратор или ibcmd). Для ibcmd
    /// передаётся профиль СУБД ИСТОЧНИКА, разрешённый заранее (issue #79, v1.0.1.21).</summary>
    private async Task<string?> DumpAsync(
        Infobase source, TestDeployScenario scenario, string dtPath,
        DbmsBackupProfile? sourceDbmsProfile, CancellationToken ct)
    {
        if (scenario.DumpEngine == BackupEngine.Ibcmd)
        {
            var exe = IbcmdService.ResolveExecutable(source);
            if (exe is null)
                return T("Ibcmd.NotFound");
            // issue #79: явные учётные данные из мастера (блок «Данные для ibcmd») или
            // null — резолвер (авторизация базы + общая авторизация приложения).
            // issue #79 (1.0.1.21): выгрузка источника выполняется по профилю СУБД
            // ИСТОЧНИКА (ResolveSourceDumpProfileAsync); поля формы мастера описывают
            // новую (целевую) базу и в выгрузку не попадают.
            var res = await IbcmdService.DumpAsync(source, dtPath, scenario.Credential, ct, sourceDbmsProfile).ConfigureAwait(false);
            if (!res.Success)
                return res.Error ?? T("Backup.Failed");
            if (!File.Exists(dtPath) || new FileInfo(dtPath).Length == 0)
                return T("Ibcmd.EmptyResult");
            _logger?.Info($"Тестовая копия: выгрузка ibcmd «{source.Name}» → {dtPath}");
            return null;
        }

        var started = OneCLauncher.RunDesignerBatch(
            source, OneCLauncher.DesignerBatchOperation.DumpIB, dtPath, credential: null);
        if (!started)
            return T("Backup.StartFailed");
        var info = await WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.DumpIB, dtPath, BatchTimeout).ConfigureAwait(false);
        if (info is null)
            return T("Backup.Timeout");
        if (!info.Success)
            return info.ErrorMessage ?? T("Backup.Failed");
        if (!File.Exists(dtPath) || new FileInfo(dtPath).Length == 0)
            return T("Ibcmd.EmptyResult");
        return null;
    }

    /// <summary>Загрузка .dt в целевую тестовую базу через RestoreIB (паттерн ServerCloneService).</summary>
    private async Task<string?> RestoreAsync(Infobase target, string dtPath, CancellationToken ct)
    {
        var started = OneCLauncher.RunDesignerBatch(
            target, OneCLauncher.DesignerBatchOperation.RestoreIB, dtPath, credential: null);
        if (!started)
            return T("Restore.StartFailed");
        var info = await WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.RestoreIB, dtPath, BatchTimeout).ConfigureAwait(false);
        if (info is null)
            return T("Restore.Timeout");
        if (!info.Success)
            return info.ErrorMessage ?? T("Restore.Failed");
        return null;
    }

    /// <summary>
    /// Выключение всех регламентных заданий в копии (<see cref="ScheduledJobsService"/>, 1.0.0.85).
    /// На Linux COM-механизм недоступен — фиксируется в журнале, развёртывание не прерывается.
    /// Секреты не логируются; текст ошибки замаскирован службой скриптов.
    /// </summary>
    private async Task DisableScheduledJobsAsync(Infobase target, TestDeployResult result)
    {
        if (!OperatingSystem.IsWindows())
        {
            result.Log.Add(T("TestDeploy.JobsLinuxSkipped"));
            return;
        }

        try
        {
            var jobs = await ScheduledJobsService.SetAllAsync(target, use: false).ConfigureAwait(false);
            if (jobs.Success)
            {
                result.Log.Add(string.Format(T("TestDeploy.JobsDisabled"), target.Name, jobs.Changed));
            }
            else
            {
                var warning = string.Format(
                    T("TestDeploy.JobsDisableWarning"), target.Name, jobs.Error ?? string.Empty);
                result.Log.Add(warning);
                result.Warnings.Add(warning);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Не удалось выключить регламентные задания в тестовой копии «{target.Name}»: {ex.Message}");
            var warning = string.Format(T("TestDeploy.JobsDisableWarning"), target.Name, ex.Message);
            result.Log.Add(warning);
            result.Warnings.Add(warning);
        }
    }

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом (копия паттерна
    /// <c>ServerCloneService.WaitForBatchAsync</c>).
    /// </summary>
    private static async Task<OneCLauncher.DesignerBatchInfo?> WaitForBatchAsync(
        OneCLauncher.DesignerBatchOperation operation,
        string outputPath,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(info);
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token)).ConfigureAwait(false);
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }

    /// <summary>
    /// Разрешает параметры СУБД для выгрузки ИСХОДНОЙ базы через ibcmd (issue #79,
    /// v1.0.1.21): 1) сохранённый профиль источника (окно «Резервные копии СУБД»);
    /// 2) автоподстановка из rac по исходной базе — данные из кластера, где реально
    /// живёт источник; 3) поля формы мастера — ТОЛЬКО если они заведомо описывают
    /// источник (совпадают с Ref исходной базы или её профилем; см.
    /// <see cref="TestDeployPlanner.FormFieldsDescribeSource"/>). Прежнее поведение
    /// (1.0.1.9–1.0.1.20) брало для выгрузки поля СУБД ЦЕЛЕВОЙ базы — из-за этого ibcmd
    /// искал SQL-БД целевой базы («…_t»), которой ещё нет, и падал с «База отсутствует
    /// в сервере».
    /// <para>Возвращает (профиль, null) при успехе или (null, понятная ошибка), если
    /// данных источника нет — вызывающий код прерывает сценарий до шага «Выгрузка».</para>
    /// </summary>
    internal async Task<(DbmsBackupProfile? Profile, string? Error)> ResolveSourceDumpProfileAsync(
        Infobase source, TestDeployScenario scenario, Action<string>? log, CancellationToken ct,
        Func<Infobase, DbmsBackupProfile?>? savedProfileLoader = null,
        Func<ClusterInfobaseCredentialsResolver?>? resolverFactory = null)
    {
        // 1) Сохранённый профиль исходной базы.
        DbmsBackupProfile? saved = null;
        try
        {
            saved = savedProfileLoader is not null
                ? savedProfileLoader(source)
                : DbmsBackupService.Load().Find(source.Id);
        }
        catch { /* хранилище недоступно — пробуем rac */ }
        if (saved is not null && !string.IsNullOrWhiteSpace(saved.Database))
        {
            log?.Invoke(T("TestDeploy.SourceDbmsProfileUsed"));
            return (saved, null);
        }

        // 1.5) Явный ручной override формы (issue #79, v1.0.1.29): пользователь включил
        // «Указать данные СУБД источника вручную» и заполнил поля — профиль строится
        // НАПРЯМУЮ, без эвристики FormFieldsDescribeSource и rac-проб: rac 8.5.4 отдаёт
        // пустые свойства СУБД, «infobase info» — «Недостаточно прав», и собрать профиль
        // автоматически не из чего.
        if (HasSourceDbmsOverride(scenario))
        {
            var overrideProfile = BuildSourceOverrideProfile(scenario);
            log?.Invoke(T("TestDeploy.SourceDbmsOverrideUsed"));
            return (overrideProfile, null);
        }

        // 2) Автоподстановка из rac по исходной базе (как в массовых операциях, issue #92).
        var resolver = resolverFactory is not null
            ? resolverFactory()
            : ClusterInfobaseCredentialsResolver.CreateDefault(message => log?.Invoke(message));
        if (resolver is not null)
        {
            log?.Invoke(T("TestDeploy.SourceDbmsResolving"));
            // Issue #79 (v1.0.1.25): учётные данные формы и тип СУБД формы — для
            // «infobase info» (права администратора ИБ) и пустого dbms rac 8.5.
            resolver.CredentialOverride = scenario.Credential;
            resolver.FallbackDbms = scenario.Dbms;
            // Issue #79 (v1.0.1.34): логин/пароль СУБД из ручных полей мастера дополняют
            // ПУСТЫЕ поля rac-профиля (rac пароль СУБД не возвращает никогда — профиль
            // уходил в ibcmd с пустым паролем, и подключение к MS SQL падало).
            // Issue #79 (v1.0.1.35, ZIP diagnostics_20261105_082423: профиль из rac ушёл
            // в ibcmd без пароля): если ручной блок «Данные СУБД источника» не заполнен,
            // fallback берётся из ВИДИМЫХ полей формы «Пользователь СУБД»/«Пароль СУБД» —
            // у одного SQL-сервера они, как правило, совпадают (унификация с окном
            // массовой выгрузки issue #92, где хватает одного пароля с формы).
            var (fallbackUser, fallbackPassword) = ResolveDbmsCredentialFallback(scenario);
            resolver.DbmsUserFallback = fallbackUser;
            resolver.DbmsPasswordFallback = fallbackPassword;
            // Issue #79 (v1.0.1.29): при неудаче резолва журнал/окно мастера прямо
            // советуют ручные данные СУБД источника ИЛИ движок «Конфигуратор».
            resolver.AdviseManualSourceDbms = true;
            try
            {
                var fromRac = await resolver.ResolveAsync(source, ct).ConfigureAwait(false);
                if (fromRac is not null && !string.IsNullOrWhiteSpace(fromRac.Database))
                {
                    // Issue #79 (v1.0.1.34): страховочный merge и для профилей, собранных
                    // кастомным резолвером — пустые поля дополняются из полей мастера.
                    // Признак применения fallback учитывает и резолвер (LastCredentialFallbackApplied).
                    if (MergeSourceDbmsCredentials(fromRac, scenario) || resolver.LastCredentialFallbackApplied)
                        log?.Invoke(T("TestDeploy.SourceDbmsCredentialsFromMaster"));
                    log?.Invoke(string.Format(T("TestDeploy.SourceDbmsResolvedFormat"),
                        $"{fromRac.Dbms}/{fromRac.Host}/{fromRac.Database}" +
                        (string.IsNullOrEmpty(fromRac.User) ? "" : $"/{fromRac.User}")));
                    return (fromRac, null);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke(string.Format(
                    LocalizationManager.T("BatchOps.DbmsResolveClustersFailed"),
                    source.Connection?.Server, ex.Message));
            }
        }

        // 3) Поля формы — только если они описывают ИСТОЧНИК (целевое имя базы вида
        // «источник_t» сюда не попадает, issue #79). Проверка журналируется (issue #79,
        // v1.0.1.22): «поля формы: считаем/не считаем данными источника и почему».
        log?.Invoke(T("TestDeploy.SourceDbmsFormCheck"));
        var fromForm = BuildFormProfileIfSource(scenario, source, saved);
        if (fromForm is not null)
        {
            log?.Invoke(T("TestDeploy.SourceDbmsFormUsed"));
            return (fromForm, null);
        }

        return (null, T("TestDeploy.DbmsProfileNotFound") + "\n" + T("TestDeploy.DbmsResolveAdviseManual"));
    }

    /// <summary>
    /// Активен ли явный ручной override данных СУБД источника (issue #79, v1.0.1.29):
    /// галочка <see cref="TestDeployScenario.UseSourceDbmsOverride"/> включена и заполнены
    /// сервер и имя SQL-БД источника. Internal — юнит-тесты и валидация планировщика.
    /// </summary>
    internal static bool HasSourceDbmsOverride(TestDeployScenario? scenario)
    {
        return scenario is { UseSourceDbmsOverride: true } &&
               !string.IsNullOrWhiteSpace(scenario.SourceDbServer) &&
               !string.IsNullOrWhiteSpace(scenario.SourceDbName);
    }

    /// <summary>
    /// Fallback логина/пароля СУБД для профиля из rac (issue #79, v1.0.1.35): приоритет —
    /// ручные поля «Данные СУБД источника» (<see cref="TestDeployScenario.SourceDbUser"/>/
    /// <see cref="TestDeployScenario.SourceDbPassword"/>); при пустых — видимые поля формы
    /// «Пользователь СУБД»/«Пароль СУБД» (целевые поля, <see cref="TestDeployScenario.DbUser"/>/
    /// <see cref="TestDeployScenario.DbPassword"/>): у одного SQL-сервера креды совпадают,
    /// а rac пароль СУБД не возвращает никогда. Internal — чистый helper для юнит-теста.
    /// </summary>
    internal static (string? User, string? Password) ResolveDbmsCredentialFallback(TestDeployScenario? scenario)
    {
        if (scenario is null)
            return (null, null);
        var user = !string.IsNullOrWhiteSpace(scenario.SourceDbUser)
            ? scenario.SourceDbUser!.Trim()
            : string.IsNullOrWhiteSpace(scenario.DbUser) ? null : scenario.DbUser!.Trim();
        var password = !string.IsNullOrWhiteSpace(scenario.SourceDbPassword)
            ? scenario.SourceDbPassword
            : string.IsNullOrWhiteSpace(scenario.DbPassword) ? null : scenario.DbPassword;
        return (user, password);
    }

    /// <summary>
    /// Дополняет профиль СУБД из rac пустыми логином/паролем из полей мастера
    /// (issue #79, v1.0.1.34): rac-значения приоритетны, поля мастера заполняют ТОЛЬКО
    /// пустые (пароль rac не возвращает никогда — ibcmd падал на авторизации даже при
    /// правильно распознанных данных). Источник кредов — <see cref="ResolveDbmsCredentialFallback"/>
    /// (с v1.0.1.35 — и видимые поля «Пользователь/Пароль СУБД» формы при пустом ручном блоке).
    /// Возвращает true, если профиль изменён. Internal — чистый helper для юнит-теста.
    /// </summary>
    internal static bool MergeSourceDbmsCredentials(DbmsBackupProfile? profile, TestDeployScenario? scenario)
    {
        if (profile is null || scenario is null)
            return false;
        var changed = false;
        var (fallbackUser, fallbackPassword) = ResolveDbmsCredentialFallback(scenario);
        var user = fallbackUser?.Trim();
        if (string.IsNullOrWhiteSpace(profile.User) && !string.IsNullOrWhiteSpace(user))
        {
            profile.User = user;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(profile.Password) && !string.IsNullOrWhiteSpace(fallbackPassword))
        {
            profile.Password = fallbackPassword;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Профиль СУБД ИСТОЧНИКА из ручных полей мастера (issue #79, v1.0.1.29). Вызывается
    /// только когда <see cref="HasSourceDbmsOverride"/> — проверка валидности не дублируется.
    /// Тип СУБД ограничен поддерживаемыми ibcmd (PostgreSQL/MSSQLServer).
    /// </summary>
    internal static DbmsBackupProfile BuildSourceOverrideProfile(TestDeployScenario scenario)
    {
        return new DbmsBackupProfile
        {
            InfobaseId = scenario.SourceInfobaseId,
            Dbms = string.Equals(scenario.SourceDbms?.Trim(), "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? DbmsKind.PostgreSql
                : DbmsKind.MsSql,
            Host = scenario.SourceDbServer!.Trim(),
            Database = scenario.SourceDbName!.Trim(),
            User = scenario.SourceDbUser?.Trim() ?? string.Empty,
            Password = scenario.SourceDbPassword ?? string.Empty
        };
    }

    /// <summary>
    /// Профиль СУБД из полей формы мастера, но ТОЛЬКО когда поля заведомо описывают
    /// исходную базу (имя SQL-БД совпадает с Ref исходной базы или её сохранённым
    /// профилем; сервер заполнен). Иначе (поля описывают целевую базу) — null:
    /// выгрузка по ним не выполняется (issue #79, v1.0.1.21).
    /// </summary>
    internal static DbmsBackupProfile? BuildFormProfileIfSource(
        TestDeployScenario scenario, Infobase source, DbmsBackupProfile? savedProfile)
    {
        if (!TestDeployPlanner.FormFieldsDescribeSource(scenario, source, savedProfile))
            return null;
        return new DbmsBackupProfile
        {
            InfobaseId = scenario.SourceInfobaseId,
            Dbms = string.Equals(scenario.Dbms?.Trim(), "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? DbmsKind.PostgreSql
                : DbmsKind.MsSql,
            Host = scenario.DbServer!.Trim(),
            Database = scenario.DbName!.Trim(),
            User = scenario.DbUser?.Trim() ?? string.Empty,
            Password = scenario.DbPassword ?? string.Empty
        };
    }

    /// <summary>
    /// Лёгкая rac-проба для pre-валидации (issue #79, v1.0.1.23): по исходной базе
    /// резолвит профиль СУБД через <see cref="ClusterInfobaseCredentialsResolver"/>
    /// (тот же механизм, что на шаге резолва перед «Выгрузкой»). Ошибки не бросаются —
    /// возвращается null (валидация покажет понятную причину).
    /// </summary>
    private static async Task<DbmsBackupProfile?> ResolveRacProfileForValidationAsync(
        Infobase source, TestDeployScenario scenario, CancellationToken ct, Action<string>? log)
    {
        var resolver = ClusterInfobaseCredentialsResolver.CreateDefault(message => log?.Invoke(message));
        if (resolver is null)
            return null;
        // Issue #79 (v1.0.1.26, регресс v1.0.1.25): креды ИБ с формы и тип СУБД формы
        // передаются и на шаге pre-валидации (ранее — только перед «Выгрузкой»,
        // из-за чего rac-проба шла без учётных данных и сообщала «Недостаточно прав»).
        resolver.CredentialOverride = scenario.Credential;
        resolver.FallbackDbms = scenario.Dbms;
        // Issue #79 (v1.0.1.35): унификация с боевым резолвом — fallback логина/пароля
        // СУБД (ручные поля «Данные СУБД источника», при пустых — поля СУБД формы)
        // действует и на шаге pre-валидации, чтобы валидация и выгрузка видели один
        // и тот же профиль.
        var (fallbackUser, fallbackPassword) = ResolveDbmsCredentialFallback(scenario);
        resolver.DbmsUserFallback = fallbackUser;
        resolver.DbmsPasswordFallback = fallbackPassword;
        // Issue #79 (v1.0.1.29): подсказка о ручных данных СУБД источника при неудаче.
        resolver.AdviseManualSourceDbms = true;
        return await resolver.ResolveAsync(source, ct).ConfigureAwait(false);
    }

    /// <summary>Текст ошибки шага: «Шаг: …» + понятная причина.</summary>
    private static string Fail(string stepTitle, string reason) =>
        string.Format(LocalizationManager.T("TestDeploy.StepFailedFormat"), stepTitle, reason);

    /// <summary>Локализованный текст по ключу (пароли не участвуют).</summary>
    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Ключ ошибки валидации может быть составным («CreateInfobase.…») — возвращает текст.</summary>
    private static string TestDeployPlannerKey(string key) => LocalizationManager.T(key);

    /// <summary>Рекурсивно удаляет временный каталог; ошибки игнорируются.</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Занят процессом/антивирусом — попытка при следующем запуске очистится сама.
        }
    }
}