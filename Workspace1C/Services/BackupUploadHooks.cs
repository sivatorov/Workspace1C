using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services.Audit;

namespace Workspace1C.Services;

/// <summary>Готовая резервная копия, которую можно отправить во внешнее хранилище.</summary>
/// <param name="LocalFilePath">Путь к созданному файлу копии.</param>
/// <param name="InfobaseName">Имя информационной базы (или её идентификатор для профиля СУБД).</param>
/// <param name="ScenarioName">Имя сценария резервирования или базы данных СУБД.</param>
/// <param name="Kind">Вид копии: <c>scenario</c> — сценарий, <c>dbms</c> — средствами СУБД.</param>
public sealed record BackupUploadContext(string LocalFilePath, string InfobaseName, string ScenarioName, string Kind);

/// <summary>Результат загрузки копии во внешнее хранилище.</summary>
public sealed record BackupUploadResult(bool Success, string? Error = null)
{
    public static BackupUploadResult Ok { get; } = new(true);

    public static BackupUploadResult Fail(string error) => new(false, error);
}

/// <summary>
/// Загрузчик готовой копии во внешнее хранилище (модуль ПРО <c>CloudBackup</c>: S3,
/// шифрование). Публичный WebDAV-загрузчик остаётся как есть.
/// </summary>
public interface IBackupUploader
{
    /// <summary>Уникальное имя загрузчика (повторная регистрация заменяет прежний).</summary>
    string Name { get; }

    /// <summary>Загружает копию; исключение не должно ломать основное действие.</summary>
    Task<BackupUploadResult> UploadAsync(BackupUploadContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Точка расширения «копия готова» (ПРО 2.x): после успешного создания файла
/// <see cref="BackupService"/> и <see cref="DbmsBackupService"/> передают его всем
/// зарегистрированным загрузчикам. Ошибка загрузчика не делает копию неуспешной:
/// результат возвращается вызывающему и пишется в журнал.
/// </summary>
public static class BackupUploadHooks
{
    private static readonly object Sync = new();
    private static readonly List<IBackupUploader> Registered = new();

    /// <summary>Текущие загрузчики (копия списка).</summary>
    public static IReadOnlyList<IBackupUploader> Uploaders
    {
        get
        {
            lock (Sync)
                return Registered.ToArray();
        }
    }

    /// <summary>Регистрирует загрузчик; одноимённый заменяется.</summary>
    public static void Register(IBackupUploader uploader)
    {
        ArgumentNullException.ThrowIfNull(uploader);
        if (string.IsNullOrWhiteSpace(uploader.Name))
            throw new ArgumentException("Uploader name is required.", nameof(uploader));

        lock (Sync)
        {
            Registered.RemoveAll(u => string.Equals(u.Name, uploader.Name, StringComparison.Ordinal));
            Registered.Add(uploader);
        }
    }

    /// <summary>Снимает загрузчик по имени.</summary>
    public static void Unregister(string name)
    {
        lock (Sync)
            Registered.RemoveAll(u => string.Equals(u.Name, name, StringComparison.Ordinal));
    }

    /// <summary>Снимает все загрузчики (тесты, выгрузка модуля).</summary>
    public static void Clear()
    {
        lock (Sync)
            Registered.Clear();
    }

    /// <summary>
    /// Запускает все загрузчики последовательно. Исключение загрузчика превращается в
    /// <see cref="BackupUploadResult"/> с ошибкой; наружу не пробрасывается.
    /// </summary>
    public static async Task<IReadOnlyList<BackupUploadResult>> RunAsync(
        BackupUploadContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<BackupUploadResult>();
        foreach (var uploader in Uploaders)
        {
            BackupUploadResult result;
            try
            {
                result = await uploader.UploadAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = BackupUploadResult.Fail(ex.Message);
            }

            results.Add(result);
            AuditHub.Write(AuditActions.CloudUpload, context.InfobaseName, result.Success,
                uploader.Name + "; " + context.Kind + "; " + context.LocalFilePath
                + (result.Success ? string.Empty : "; " + result.Error));
        }
        return results;
    }
}
