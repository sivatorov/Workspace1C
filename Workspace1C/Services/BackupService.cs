using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IBackupService"/>. Выполнение сценария:
/// формирует имя файла по шаблону, запускает пакетную выгрузку конфигуратора
/// (<see cref="OneCLauncher.RunDesignerBatch"/>), ожидает её завершения через событие
/// <see cref="OneCLauncher.DesignerBatchCompleted"/> (TaskCompletionSource с таймаутом),
/// при формате ZIP/RAR пакует результат и копирует итог во все каталоги назначения.
/// </summary>
public class BackupService : IBackupService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(60);

    private readonly IArchiveService _archive;
    private readonly IAppLogger? _logger;

    public BackupService(IArchiveService archive, IAppLogger? logger = null)
    {
        _archive = archive;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(Infobase infobase, BackupScenario scenario)
    {
        var result = new BackupRunResult { ScenarioName = scenario?.Name ?? "" };
        if (scenario is null || infobase is null)
        {
            result.ErrorMessage = LocalizationManager.T("Backup.Failed");
            return result;
        }

        try
        {
            var targets = scenario.TargetDirectories?
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .ToList() ?? new List<string>();
            if (targets.Count == 0)
            {
                result.ErrorMessage = LocalizationManager.T("Backup.NoTargetDirectory");
                return result;
            }

            var primaryDir = targets[0];
            try
            {
                Directory.CreateDirectory(primaryDir);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format(LocalizationManager.T("Backup.TargetUnavailableFormat"), primaryDir, ex.Message);
                return result;
            }

            var now = DateTime.Now;
            var baseName = (scenario.BasePrefix ?? "") + (infobase.Name ?? "");
            var fileName = scenario.BuildFileName(baseName, now) + scenario.GetExtension();
            var primaryPath = Path.Combine(primaryDir, fileName);

            var operation = scenario.Format == BackupFormat.Cf
                ? OneCLauncher.DesignerBatchOperation.DumpCfg
                : OneCLauncher.DesignerBatchOperation.DumpIB;

            if (scenario.Engine == BackupEngine.Ibcmd)
            {
                // Движок ibcmd (1.0.0.84): выгрузка без «Конфигуратора» — без окна и лицензии.
                var ibcmd = scenario.Format == BackupFormat.Cf
                    ? await IbcmdService.SaveConfigAsync(infobase, primaryPath, scenario.Credential)
                    : await IbcmdService.DumpAsync(infobase, primaryPath, scenario.Credential);
                if (!ibcmd.Success)
                {
                    result.ErrorMessage = ibcmd.Error ?? LocalizationManager.T("Backup.Failed");
                    return result;
                }
                if (!File.Exists(primaryPath) || new FileInfo(primaryPath).Length == 0)
                {
                    result.ErrorMessage = LocalizationManager.T("Ibcmd.EmptyResult");
                    return result;
                }
                // issue #80 «Настройка логирования»: выгрузка резервной копии — направление
                // CM_BACKUP (INFO пишется при включённых категории и общем CM_ON).
                Log.Info(LogCategory.Backup, $"Выгрузка ibcmd «{infobase.Name}» → {primaryPath}");
            }
            else
            {
                var started = OneCLauncher.RunDesignerBatch(infobase, operation, primaryPath, scenario.Credential);
                if (!started)
                {
                    result.ErrorMessage = LocalizationManager.T("Backup.StartFailed");
                    return result;
                }

                var dumpInfo = await WaitForCompletionAsync(operation, primaryPath);
                if (dumpInfo is null || !dumpInfo.Success)
                {
                    result.ErrorMessage = dumpInfo?.ErrorMessage ?? LocalizationManager.T("Backup.Timeout");
                    return result;
                }
            }

            var finalPath = primaryPath;
            if (scenario.Format is BackupFormat.Zip or BackupFormat.Rar)
            {
                if (!_archive.CanProduce(scenario.Format))
                {
                    result.ErrorMessage = LocalizationManager.T("Backup.RarNotFound");
                    return result;
                }

                var archivePath = Path.Combine(primaryDir,
                    Path.GetFileNameWithoutExtension(fileName) + scenario.GetExtension());
                if (!_archive.CreateArchive(archivePath, new[] { primaryPath }))
                {
                    result.ErrorMessage = LocalizationManager.T("Backup.ArchiveFailed");
                    return result;
                }

                try { File.Delete(primaryPath); } catch { }
                finalPath = archivePath;
            }

            var created = new List<string> { finalPath };
            // Копируем итоговый файл в остальные каталоги назначения (перезапись).
            for (var i = 1; i < targets.Count; i++)
            {
                var targetDir = targets[i];
                try
                {
                    Directory.CreateDirectory(targetDir);
                    var copyPath = Path.Combine(targetDir, Path.GetFileName(finalPath));
                    File.Copy(finalPath, copyPath, true);
                    created.Add(copyPath);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Не удалось скопировать резервную копию в каталог «{targetDir}»: {ex.Message}");
                }
            }

            // Ротация (0.3.9.86): по КАЖДОМУ каталогу назначения удаляем копии
            // за пределами лимита количества и/или старше заданного возраста.
            // Префикс имени — безопасная часть шаблона (префикс базы), не зависящая
            // от имени ИБ; пустой префикс соответствует любым файлам расширения.
            var purged = new List<string>();
            if (scenario.KeepCount > 0 || scenario.DeleteOlderThanDays > 0)
            {
                foreach (var targetDir in targets)
                {
                    try
                    {
                        purged.AddRange(BackupRotation.Apply(
                            targetDir,
                            scenario.BasePrefix ?? "",
                            scenario.GetExtension(),
                            scenario.KeepCount,
                            scenario.DeleteOlderThanDays));
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Не удалось выполнить ротацию резервных копий в каталоге «{targetDir}»: {ex.Message}");
                    }
                }
            }

            result.Success = true;
            result.CreatedFiles = created;
            result.PurgedFiles = purged;
            // Метка последней успешной копии для колонки «Последняя копия» (0.3.9.86).
            if (infobase is not null)
                infobase.LastBackupUtc = DateTime.UtcNow;
            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка выполнения сценария резервирования", ex);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<BackupRunResult> RestoreAsync(Infobase infobase, string filePath, BackupCredential? credential = null)
    {
        var result = await RestoreCoreAsync(infobase, filePath, credential).ConfigureAwait(false);
        Audit.AuditHub.Write(Audit.AuditActions.DtRestore, infobase?.Name ?? string.Empty, result.Success,
            filePath + (result.Success ? string.Empty : "; " + result.ErrorMessage));
        return result;
    }

    private async Task<BackupRunResult> RestoreCoreAsync(Infobase infobase, string filePath, BackupCredential? credential)
    {
        var result = new BackupRunResult();
        if (infobase is null)
        {
            result.ErrorMessage = LocalizationManager.T("Restore.Failed");
            return result;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.ErrorMessage = LocalizationManager.T("Restore.FileNotFound");
                return result;
            }

            var started = OneCLauncher.RunDesignerBatch(infobase, OneCLauncher.DesignerBatchOperation.RestoreIB, filePath, credential);
            if (!started)
            {
                result.ErrorMessage = LocalizationManager.T("Restore.StartFailed");
                return result;
            }

            var info = await WaitForCompletionAsync(OneCLauncher.DesignerBatchOperation.RestoreIB, filePath);
            if (info is null || !info.Success)
            {
                result.ErrorMessage = info?.ErrorMessage ?? LocalizationManager.T("Restore.Timeout");
                return result;
            }

            result.Success = true;
            result.CreatedFiles = new[] { filePath };
            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка восстановления информационной базы", ex);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER (по операции и пути вывода)
    /// через событие <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом.
    /// </summary>
    private async Task<OneCLauncher.DesignerBatchInfo?> WaitForCompletionAsync(
        OneCLauncher.DesignerBatchOperation operation, string outputPath)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }
}