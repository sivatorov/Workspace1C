using System;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services;

/// <summary>Результат расшифровки резервной копии.</summary>
public sealed record CloudDecryptResult(bool Success, string? OutputPath = null, string? Error = null)
{
    public static CloudDecryptResult Ok(string outputPath) => new(true, outputPath);

    public static CloudDecryptResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Точка расширения «расшифровать копию» (ПРО <c>CloudBackup</c>). Публичное окно
/// «Список выгрузок» вызывает <see cref="DecryptAsync"/>, если модуль подключён;
/// без модуля кнопка недоступна. Ошибка обработчика превращается в результат,
/// наружу не пробрасывается.
/// </summary>
public static class CloudDecryptHooks
{
    /// <summary>Обработчик расшифровки (модуль ПРО). null — модуль не подключён.</summary>
    public static Func<string, CancellationToken, Task<CloudDecryptResult>>? Handler { get; set; }

    /// <summary>Доступна ли расшифровка в этой сборке.</summary>
    public static bool IsAvailable => Handler is not null;

    /// <summary>Расшифровывает файл; исключения обработчика подавляются.</summary>
    public static async Task<CloudDecryptResult> DecryptAsync(string path,
        CancellationToken cancellationToken = default)
    {
        var handler = Handler;
        if (handler is null)
            return CloudDecryptResult.Fail("Расшифровка недоступна: модуль ПРО не подключён.");

        try
        {
            return await handler(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CloudDecryptResult.Fail(ex.Message);
        }
    }

    /// <summary>Снимает обработчик (тесты, выгрузка модуля).</summary>
    public static void Clear() => Handler = null;
}
