using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>Решение о судьбе уже скачанного файла обновления (issue #129).</summary>
public enum UpdateCacheDecision
{
    /// <summary>Файла нет (или он пуст) — нужна обычная загрузка.</summary>
    Download,

    /// <summary>Файл есть, но не совпал по размеру/ETag/дате — перекачать с нуля.</summary>
    Redownload,

    /// <summary>Файл полный и валиден — переиспользовать без сети.</summary>
    Reuse,
}

/// <summary>
/// Маркер готового скачанного файла обновления (issue #129): размер, ETag и дата
/// последнего изменения ассета на момент загрузки. Хранится рядом с файлом
/// (<c>*.ready</c>), позволяет переиспользовать загрузку без повторного скачивания
/// и перекачивать файл только при явном расхождении с сервером.
/// </summary>
/// <param name="Size">Размер файла в байтах на момент успешной загрузки.</param>
/// <param name="ETag">ETag ассета (может отсутствовать).</param>
/// <param name="LastModifiedUtc">Last-Modified ассета в UTC (может отсутствовать).</param>
public readonly record struct UpdateCacheMarker(
    long Size,
    string? ETag,
    DateTime? LastModifiedUtc)
{
    private const int FormatVersion = 1;

    /// <summary>Сериализует маркер в простой текст «ключ=значение».</summary>
    public string Serialize()
    {
        var builder = new StringBuilder();
        builder.Append("version=").Append(FormatVersion).Append('\n');
        builder.Append("size=").Append(Size.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrEmpty(ETag))
            builder.Append("etag=").Append(ETag).Append('\n');
        if (LastModifiedUtc is not null)
        {
            builder.Append("lastModified=")
                .Append(LastModifiedUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Разбирает маркер. <c>null</c> — текст пуст/повреждён или в нём нет размера
    /// (без размера переиспользовать файл нельзя).
    /// </summary>
    public static UpdateCacheMarker? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        long? size = null;
        string? etag = null;
        DateTime? lastModified = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "size":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                        size = parsedSize;
                    break;
                case "etag":
                    etag = value.Length == 0 ? null : value;
                    break;
                case "lastmodified":
                    if (DateTime.TryParse(
                            value,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                            out var parsedDate))
                    {
                        lastModified = parsedDate.ToUniversalTime();
                    }
                    break;
            }
        }

        return size is null ? null : new UpdateCacheMarker(size.Value, etag, lastModified);
    }
}

/// <summary>
/// Кэш скачанных файлов обновления (issue #129): чистые функции без сети и UI.
/// <para>
/// Проблема: файл обновления удалялся при закрытии диалога и скачивался заново
/// при каждом запуске, хотя уже лежал в <c>%TEMP%</c>. Теперь полный файл
/// сохраняется, а перед загрузкой проверяется точный размер (из GitHub API) и
/// маркер <c>*.ready</c>; при совпадении размера/ETag/даты загрузка не выполняется.
/// </para>
/// <para>
/// Безопасность: файл принимается только при ТОЧНОМ совпадении ожидаемого размера;
/// при неизвестном размере (<c>expectedSize <= 0</c>) кэш не используется —
/// лучше скачать заново, чем установить обрезок. При явном несовпадении ETag или
/// даты файл перекачивается; пустой ETag ответа (сбой зонда) файл не «наказывает».
/// </para>
/// </summary>
public static class UpdateDownloadCache
{
    /// <summary>Расширение маркера готового файла.</summary>
    public const string ReadyMarkerExtension = ".ready";

    /// <summary>Путь маркера <c>.ready</c> для скачанного файла.</summary>
    public static string BuildReadyMarkerPath(string filePath) => filePath + ReadyMarkerExtension;

    /// <summary>
    /// True — файл существует и его размер ТОЧНО равен ожидаемому
    /// (<paramref name="expectedSize"/> > 0). При неизвестном ожидаемом размере
    /// возвращает <c>false</c>: подтвердить целостность нечем.
    /// </summary>
    public static bool IsComplete(string? filePath, long expectedSize)
    {
        if (expectedSize <= 0 || string.IsNullOrWhiteSpace(filePath))
            return false;
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists && info.Length == expectedSize;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Возвращает путь к уже скачанному полному файлу обновления или <c>null</c>.
    /// Проверяется только точный размер: сеть и маркер не нужны — это и убирает
    /// повторную загрузку при каждом запуске (issue #129).
    /// </summary>
    public static string? ResolveCachedFile(string? directory, string? fileName, long expectedSize)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return null;
        try
        {
            var path = Path.Combine(directory, fileName);
            return IsComplete(path, expectedSize) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Решает, можно ли переиспользовать файл (issue #129). Учитывает размер,
    /// ETag и дату последнего изменения ассета: явное расхождение — перекачка,
    /// пустой ETag/дата ответа — файл считается валидным (сеть могла сбоить).
    /// </summary>
    public static UpdateCacheDecision Decide(
        string? filePath,
        long expectedSize,
        UpdateCacheMarker? marker,
        string? remoteETag,
        DateTime? remoteLastModifiedUtc)
    {
        if (expectedSize <= 0 || string.IsNullOrWhiteSpace(filePath))
            return UpdateCacheDecision.Download;

        long actualSize;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length <= 0)
                return UpdateCacheDecision.Download;
            actualSize = info.Length;
        }
        catch
        {
            return UpdateCacheDecision.Download;
        }

        if (actualSize != expectedSize)
            return UpdateCacheDecision.Redownload;

        // Точный размер совпал — файл пригоден. Маркер уточняет идентичность ассета.
        if (marker is null)
            return UpdateCacheDecision.Reuse;

        var cached = marker.Value;
        if (cached.Size > 0 && cached.Size != actualSize)
            return UpdateCacheDecision.Redownload;

        if (!string.IsNullOrEmpty(remoteETag)
            && !string.IsNullOrEmpty(cached.ETag)
            && !string.Equals(cached.ETag, remoteETag, StringComparison.Ordinal))
        {
            return UpdateCacheDecision.Redownload;
        }

        if (remoteLastModifiedUtc is not null && cached.LastModifiedUtc is not null)
        {
            // Last-Modified имеет секундную точность: расхождение меньше секунды
            // не считается изменением ассета.
            var delta = (cached.LastModifiedUtc.Value - remoteLastModifiedUtc.Value).Duration();
            if (delta >= TimeSpan.FromSeconds(1))
                return UpdateCacheDecision.Redownload;
        }

        return UpdateCacheDecision.Reuse;
    }

    /// <summary>Читает маркер рядом с файлом; повреждённый/отсутствующий — <c>null</c>.</summary>
    public static UpdateCacheMarker? TryReadMarker(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        try
        {
            var markerPath = BuildReadyMarkerPath(filePath);
            return File.Exists(markerPath)
                ? UpdateCacheMarker.TryParse(File.ReadAllText(markerPath, Encoding.UTF8))
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Записывает маркер готового файла (размер + ETag + дата). Ошибки записи
    /// не критичны: без маркера файл всё равно переиспользуется по точному размеру.
    /// </summary>
    public static void WriteReadyMarker(
        string? filePath,
        long size,
        string? etag,
        DateTime? lastModifiedUtc)
    {
        if (string.IsNullOrWhiteSpace(filePath) || size <= 0)
            return;
        try
        {
            File.WriteAllText(
                BuildReadyMarkerPath(filePath),
                new UpdateCacheMarker(size, etag, lastModifiedUtc).Serialize(),
                Encoding.UTF8);
        }
        catch
        {
            // Маркер — оптимизация, а не обязательный файл.
        }
    }

    /// <summary>
    /// Удаляет скачанный файл и его маркер (неполный/чужой файл, «Пропустить версию»,
    /// ошибка установки). Ошибки игнорируются — это уборка временного каталога.
    /// </summary>
    public static void DeleteCachedFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;
        TryDelete(filePath);
        TryDelete(BuildReadyMarkerPath(filePath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Файл занят/нет прав — уборка не критична.
        }
    }
}
