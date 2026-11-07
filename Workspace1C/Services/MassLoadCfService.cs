using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Результат массовой загрузки конфигурации по одной базе (issue #106).</summary>
public sealed class MassLoadCfBaseResult
{
    /// <summary>Загрузка конфигурации выполнена успешно.</summary>
    public bool Success { get; init; }

    /// <summary>Текст ошибки шага (причина отказа/ошибка конфигуратора); null при успехе.</summary>
    public string? Error { get; init; }

    /// <summary>Предупреждение (например, ошибка снятия блокировок после успеха).</summary>
    public string? Warning { get; init; }

    /// <summary>Путь созданного .dt-файла (если включена выгрузка перед загрузкой).</summary>
    public string? DtPath { get; init; }

    public static MassLoadCfBaseResult Ok(string? dtPath = null, string? warning = null)
        => new() { Success = true, DtPath = dtPath, Warning = warning };

    public static MassLoadCfBaseResult Fail(string error, string? dtPath = null, string? warning = null)
        => new() { Success = false, Error = error, DtPath = dtPath, Warning = warning };
}

/// <summary>
/// Результат одного пакетного запуска Конфигуратора (/DumpIB либо /LoadCfg):
/// обёртка над <see cref="OneCLauncher.DesignerBatchInfo"/> для тестов и журнала
/// (issue #106).
/// </summary>
public sealed class MassLoadCfRunnerResult
{
    /// <summary>Операция конфигуратора завершилась успешно.</summary>
    public bool Success { get; init; }

    /// <summary>Текст ошибки (причина отказа запуска либо ошибка конфигуратора).</summary>
    public string? Error { get; init; }

    public static MassLoadCfRunnerResult Ok() => new() { Success = true };

    public static MassLoadCfRunnerResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Оркестратор массовой загрузки конфигурации из .cf через Конфигуратор по списку баз
/// (issue #106, автор aepkugesi): по каждой базе выполняется план
/// <see cref="MassLoadCfPlanner.PlanSteps"/> —
/// <list type="number">
/// <item>подготовка «Перед загрузкой» (завершение сеансов, блокировка начала сеансов,
/// блокировка регламентных заданий — переиспользован <see cref="BatchOpsPreparationService"/>);</item>
/// <item>опциональная выгрузка базы в .dt (Конфигуратор /DumpIB, «как в массовой
/// выгрузке в dt» — шаблон имени тот же);</item>
/// <item>загрузка конфигурации (Конфигуратор /LoadCfg"path.cf" /UpdateDBCfg,
/// <see cref="OneCLauncher.DesignerBatchOperation.LoadCfg"/>);</item>
/// <item>снятие блокировок, установленных подготовкой (всегда, при любом исходе).</item>
/// </list>
/// Конфигуратор входит в заблокированную базу с кодом разрешения «/UC» (ключ
/// <c>permissionCode</c> пакетного запуска), поэтому блокировка начала сеансов
/// совместима с операцией — в отличие от движка «Конфигуратор» массовой выгрузки
/// (issue #92, v1.0.1.30), где /DumpIB требовал вход без блокировки.
/// <para>
/// Подмена запуска конфигуратора — <see cref="RunnerOverride"/> (юнит-тесты без
/// реальной 1С); подмена сервиса подготовки — через конструктор. Ошибки одной базы
/// не прерывают остальные (прогон продолжает список — решает вызывающий код VM).
/// </para>
/// </summary>
public sealed class MassLoadCfService
{
    private readonly BatchOpsPreparationService? _preparation;
    private readonly Action<string>? _log;

    /// <param name="preparation">Сервис подготовки «Перед загрузкой»; null — опции
    /// подготовки недоступны (rac-клиент не найден), базы с включёнными опциями
    /// пропускаются с понятной ошибкой.</param>
    /// <param name="log">Приёмник строк журнала; null — без журнала.</param>
    public MassLoadCfService(BatchOpsPreparationService? preparation = null, Action<string>? log = null)
    {
        _preparation = preparation;
        _log = log;
    }

    /// <summary>
    /// Подмена запуска Конфигуратора для юнит-тестов (без реальной 1С):
    /// (база, операция, путь файла, код разрешения, токен отмены) → результат
    /// ЗАВЕРШЁННОЙ операции (фейк имитирует и запуск, и завершение). null —
    /// реальный запуск <see cref="OneCLauncher.RunDesignerBatch"/> с ожиданием
    /// по событию <see cref="OneCLauncher.DesignerBatchCompleted"/>.
    /// </summary>
    internal Func<Infobase, OneCLauncher.DesignerBatchOperation, string, string?, CancellationToken,
        Task<MassLoadCfRunnerResult>>? RunnerOverride { get; set; }

    /// <summary>
    /// Выполняет загрузку конфигурации в одну базу по плану
    /// <see cref="MassLoadCfPlanner.PlanSteps"/>. Снятие блокировок выполняется
    /// при ЛЮБОМ исходе (успех, ошибка, отмена) — как в массовых операциях
    /// (issue #92, v1.0.1.30). Исключения не пробрасываются, кроме отмены
    /// операции (перед пробросом блокировки тоже снимаются).
    /// </summary>
    public async Task<MassLoadCfBaseResult> RunAsync(
        Infobase infobase, MassLoadCfOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(infobase);
        ArgumentNullException.ThrowIfNull(options);

        var plan = MassLoadCfPlanner.PlanSteps(options);
        BatchOpsPreparationResult? undo = null;

        // ---- Шаг «Подготовка»: завершение сеансов и/или блокировки. ----
        if (plan.Contains(MassLoadCfStep.Prepare))
        {
            var preparation = _preparation;
            if (preparation is null)
                return MassLoadCfBaseResult.Fail(LocalizationManager.T("BatchOps.Prepare.Unavailable"));
            _log?.Invoke(string.Format(
                LocalizationManager.T("MassLoadCf.LogPrepareFormat"), infobase.Name));
            try
            {
                undo = await preparation.ApplyBeforeAsync(
                    infobase, MassLoadCfPlanner.ToPreparationOptions(options), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return MassLoadCfBaseResult.Fail(ex.Message);
            }

            // null — подготовка не требовалась (например, опции не применимы к базе):
            // операция продолжается штатно (как в массовых операциях, issue #92).
            if (undo is not null && !undo.Success)
                return MassLoadCfBaseResult.Fail(
                    undo.Error ?? LocalizationManager.T("BatchOps.Prepare.ResolveFailed"));
        }

        // ---- Шаги «Выгрузка .dt» и «Загрузка конфигурации»; снятие блокировок —
        // при любом исходе (в том числе при отмене — перед пробросом исключения). ----
        try
        {
            var result = await RunOperationStepsAsync(infobase, options, plan, ct).ConfigureAwait(false);
            // Снятие блокировок при успехе/ошибке: ошибка восстановления —
            // предупреждение к результату (не перекрывает ошибку операции).
            var restoreWarning = RestoreUndo(infobase, undo);
            if (restoreWarning is not null)
                return result.Success
                    ? MassLoadCfBaseResult.Ok(result.DtPath, restoreWarning)
                    : MassLoadCfBaseResult.Fail(result.Error ?? "?", result.DtPath, restoreWarning);
            return result;
        }
        catch (OperationCanceledException)
        {
            // Отмена («Остановить»): блокировки снимаются перед пробросом —
            // база не должна остаться заблокированной приложением.
            RestoreUndo(infobase, undo);
            throw;
        }
    }

    /// <summary>Шаги с запуском Конфигуратора: опциональная выгрузка .dt и загрузка .cf.</summary>
    private async Task<MassLoadCfBaseResult> RunOperationStepsAsync(
        Infobase infobase, MassLoadCfOptions options, IReadOnlyList<MassLoadCfStep> plan, CancellationToken ct)
    {
        string? dtPath = null;

        if (plan.Contains(MassLoadCfStep.DumpDt))
        {
            dtPath = MassLoadCfPlanner.BuildDtPath(options, infobase.Name);
            _log?.Invoke(string.Format(
                LocalizationManager.T("MassLoadCf.LogDumpFormat"), infobase.Name, dtPath));
            ct.ThrowIfCancellationRequested();
            var dumpError = await RunDesignerStepAsync(
                infobase, OneCLauncher.DesignerBatchOperation.DumpIB, dtPath, options, ct).ConfigureAwait(false);
            if (dumpError is not null)
                return MassLoadCfBaseResult.Fail(dumpError, dtPath);
        }

        ct.ThrowIfCancellationRequested();

        // Источник обновления (issue #85, v1.0.1.43): файл поставки .cf (/LoadCfg)
        // или хранилище конфигурации (/ConfigurationRepositoryUpdateCfg + /UpdateDBCfg,
        // DesignerBatchOperation.RepositoryUpdate) — адрес/логин/пароль хранилища
        // собираются в RunDesignerBatch из свойств базы, путь-заглушка не нужен.
        if (plan.Contains(MassLoadCfStep.RepositoryUpdate))
        {
            _log?.Invoke(string.Format(
                LocalizationManager.T("MassLoadCf.LogRepoUpdateFormat"), infobase.Name));
            var updateError = await RunDesignerStepAsync(
                infobase, OneCLauncher.DesignerBatchOperation.RepositoryUpdate, null, options, ct)
                .ConfigureAwait(false);
            return updateError is null
                ? MassLoadCfBaseResult.Ok(dtPath)
                : MassLoadCfBaseResult.Fail(updateError, dtPath);
        }

        var cfPath = options.CfFilePath.Trim();
        // Защита (issue #106): файл .cf мог исчезнуть или стать недоступным в середине
        // прогона (переименован, антивирус, сеть) — даём понятную ошибку по базе вместо
        // малопонимаемого сбоя самого Конфигуратора.
        if (!File.Exists(cfPath))
            return MassLoadCfBaseResult.Fail(LocalizationManager.T("BatchOps.CfNotFound"));
        _log?.Invoke(string.Format(
            LocalizationManager.T("MassLoadCf.LogLoadFormat"), infobase.Name, cfPath));
        var loadError = await RunDesignerStepAsync(
            infobase, OneCLauncher.DesignerBatchOperation.LoadCfg, cfPath, options, ct).ConfigureAwait(false);
        return loadError is null
            ? MassLoadCfBaseResult.Ok(dtPath)
            : MassLoadCfBaseResult.Fail(loadError, dtPath);
    }

    /// <summary>
    /// Один запуск Конфигуратора (/DumpIB либо /LoadCfg) с ожиданием завершения по
    /// событию <see cref="OneCLauncher.DesignerBatchCompleted"/> (образец —
    /// BatchOperationsViewModel.WaitForDesignerBatchAsync): при отмене процесс
    /// конфигуратора гасится через <see cref="OneCLauncher.TerminateBatchProcess"/>.
    /// null — успех; иначе текст ошибки.
    /// </summary>
    private async Task<string?> RunDesignerStepAsync(
        Infobase infobase, OneCLauncher.DesignerBatchOperation operation, string path,
        MassLoadCfOptions options, CancellationToken ct)
    {
        var permissionCode = string.IsNullOrWhiteSpace(options.PermissionCode)
            ? null
            : options.PermissionCode!.Trim();
        var runner = RunnerOverride;
        if (runner is not null)
        {
            // Тестовый режим: результат фейка — ЗАВЕРШЁННАЯ операция (успех/ошибка),
            // ожидание по событию DesignerBatchCompleted не выполняется.
            var completed = await runner(infobase, operation, path, permissionCode, ct).ConfigureAwait(false);
            if (completed.Success)
                return null;
            // Пустое сообщение ошибки подменяем понятным текстом (issue #106):
            // без этого сбой выглядел как пустая причина в статусе базы.
            return string.IsNullOrWhiteSpace(completed.Error)
                ? LocalizationManager.T("Backup.StartFailed")
                : completed.Error!;
        }

        var started = LaunchDesigner(infobase, operation, path, permissionCode);
        if (!started.Success)
            return started.Error ?? LocalizationManager.T("Backup.StartFailed");

        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath, path, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(info);
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(
                tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, ct)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                // Отмена («Остановить»): процесс конфигуратора гасится вместе с
                // деревом потомков; путь сужает поиск порождённых процессов 1cv8
                // до НАШЕЙ операции (issue #92, v1.0.1.30).
                OneCLauncher.TerminateBatchProcess(infobase, path);
                throw new OperationCanceledException(ct);
            }

            var info = tcs.Task.Result;
            if (info is { Success: true })
                return null;

            // Информативность ошибок (issue #106): если лог Конфигуратора не дал текста,
            // добавляем код возврата процесса — без него все сбои выглядели одинаково.
            var error = (info?.ErrorMessage ?? string.Empty).Trim();
            if (error.Length == 0)
                error = string.Format(
                    LocalizationManager.T("MassLoadCf.DesignerFailedWithExitCode"),
                    info?.ExitCode ?? -1);
            return error;
        }
        catch (OperationCanceledException)
        {
            // Страховка на случай броска отмены из ожидания — гашение idempotent.
            OneCLauncher.TerminateBatchProcess(infobase, path);
            throw;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }

    /// <summary>Реальный запуск конфигуратора (не в тестовом режиме с RunnerOverride).</summary>
    private static MassLoadCfRunnerResult LaunchDesigner(
        Infobase infobase, OneCLauncher.DesignerBatchOperation operation, string path, string? permissionCode)
    {
        var started = OneCLauncher.RunDesignerBatch(
            infobase, operation, path,
            credential: null, repositoryVersion: null, repositoryReportBegin: null,
            repositoryReportEnd: null, permissionCode: permissionCode, out var startError);
        return started
            ? MassLoadCfRunnerResult.Ok()
            : MassLoadCfRunnerResult.Fail(startError ?? LocalizationManager.T("Backup.StartFailed"));
    }

    /// <summary>Снятие блокировок подготовки; возвращает текст ошибки/предупреждения или null.</summary>
    private string? RestoreUndo(Infobase infobase, BatchOpsPreparationResult? undo)
    {
        if (undo is null)
            return null;
        try
        {
            var restoreError = _preparation!.RestoreAfterAsync(infobase, undo, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            if (restoreError is not null)
                _log?.Invoke(string.Format(
                    LocalizationManager.T("MassLoadCf.LogRestoreFailedFormat"),
                    infobase.Name, restoreError));
            return restoreError;
        }
        catch (OperationCanceledException)
        {
            // Отмена при восстановлении: блокировка могла остаться — фиксируем.
            return LocalizationManager.T("BatchOps.Prepare.RestoreCancelled");
        }
        catch (Exception ex)
        {
            return string.Format(
                LocalizationManager.T("BatchOps.Prepare.RestoreFailedFormat"), ex.Message);
        }
    }
}
