using System;
using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Очистка устаревших скачанных файлов обновления приложения (issue #116, v1.0.1.58).
/// Класс общий для WPF- и Avalonia-версий <see cref="UpdateService"/> и вынесен
/// без платформенных условий, чтобы логику можно было покрыть юнит-тестами.
/// </summary>
public static class UpdateDownloadCleanup
{
    /// <summary>
    /// Удаляет предыдущий скачанный файл обновления после успешной загрузки нового
    /// (issue #116): «по логике должно было скачивать файл обновления, причём если
    /// удалось — удалять старый файл обновления». Устаревшие загруженные сборки
    /// не должны занимать десятки МБ в temp-каталоге.
    /// </summary>
    /// <param name="previousPath">Путь к прежнему скачанному файлу; null/пустой — нечего удалять.</param>
    /// <param name="newPath">Путь к только что скачанному файлу — никогда не удаляется,
    /// даже если совпадает с прежним (докачка той же версии).</param>
    public static void RemoveStaleDownload(string? previousPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(previousPath))
            return;
        if (string.IsNullOrWhiteSpace(newPath))
            return;
        // Один и тот же файл (повторная загрузка той же версии) не удаляем: это и есть готовый файл.
        if (string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(previousPath))
                File.Delete(previousPath);
        }
        catch
        {
            // Очистка не критична: устаревший файл будет удалён при следующей
            // успешной загрузке либо уборщиком temp-каталога.
        }
    }
}
