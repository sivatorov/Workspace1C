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
            var validation = TestDeployPlanner.Validate(scenario, source, existing);
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

            var tmpRoot = Path.Combine(Path.GetTempPath(), "cm_testdeploy_" + Guid.NewGuid().ToString("N"));
            try
            {
                // ---- Шаг 1: выгрузка исходной базы в .dt (движок из сценария). ----
                log?.Invoke(T("TestDeploy.Step.Dump"));
                result.Log.Add(T("TestDeploy.Step.Dump"));
                cancellationToken.ThrowIfCancellationRequested();

                var dtPath = TestDeployPlanner.BuildDumpPath(tmpRoot);
                var dumpError = await DumpAsync(source, scenario, dtPath, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Выгрузка .dt движком из сценария (конфигуратор или ibcmd).</summary>
    private async Task<string?> DumpAsync(
        Infobase source, TestDeployScenario scenario, string dtPath, CancellationToken ct)
    {
        if (scenario.DumpEngine == BackupEngine.Ibcmd)
        {
            var exe = IbcmdService.ResolveExecutable(source);
            if (exe is null)
                return T("Ibcmd.NotFound");
            // issue #79: явные учётные данные из мастера (блок «Данные для ibcmd») или
            // null — резолвер (авторизация базы + общая авторизация приложения).
            var res = await IbcmdService.DumpAsync(source, dtPath, scenario.Credential, ct).ConfigureAwait(false);
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