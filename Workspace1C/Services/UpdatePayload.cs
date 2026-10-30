using System.IO;
using System.IO.Compression;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Защита загрузчика автообновления от «не того» файла (issue #358). Релиз может
/// отдать ZIP-архив с exe внутри (пользователь выложил архив вместо/вместе с bare-exe)
/// или вовсе HTML-страницу (редирект на страницу скачивания, ошибка сервиса). Прежний
/// помощник делал Move-Item любого скачанного файла на место exe — «не запускается».
/// Теперь скачанный файл проходит проверку сигнатуры:
///  - <c>MZ</c> (PE) — штатный путь, файл возвращается как есть;
///  - <c>PK\x03\x04</c> (ZIP) — из архива извлекается <c>Workspace1C.exe</c>
///    и устанавливается он;
///  - иное (HTML и пр.) — файл удаляется, возвращается null (ошибка «не удалось
///    скачать обновление» вместо установки битого файла).
/// Логика кроссплатформенная и тестируется без сети (см. UpdatePayloadTests).
/// </summary>
public static class UpdatePayload
{
    /// <summary>Сигнатура начала скачанного файла.</summary>
    public enum PayloadSignature
    {
        /// <summary>PE-образ (самораспаковывающийся single-file exe, «MZ»).</summary>
        PortableExecutable,
        /// <summary>ZIP-архив («PK\x03\x04») — вероятно, exe упакован внутрь.</summary>
        ZipArchive,
        /// <summary>Ни PE, ни ZIP — HTML/обрезок/что-то ещё: устанавливать нельзя.</summary>
        Unknown,
    }

    /// <summary>Имя исполняемого файла, который ищется внутри ZIP-архива.</summary>
    internal const string PayloadEntryName = "Workspace1C.exe";

    /// <summary>Суффикс распакованного из архива exe (рядом со скачанным файлом).</summary>
    private const string ExtractedSuffix = ".extracted.exe";

    /// <summary>Имя журнала проверки сигнатуры (рядом с журналами обновления).</summary>
    private const string LogFileName = "update-payload.log";

    /// <summary>
    /// Определяет сигнатуру существующего файла по первым четырём байтам.
    /// При любой ошибке чтения возвращает <see cref="PayloadSignature.Unknown"/>.
    /// </summary>
    public static PayloadSignature DetectSignature(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[4];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer[read..]);
                if (n == 0)
                    break;
                read += n;
            }

            return Classify(buffer[..read]);
        }
        catch
        {
            return PayloadSignature.Unknown;
        }
    }

    /// <summary>
    /// Классифицирует первые байты файла: «MZ» — PE, «PK\x03\x04» — ZIP, иное — Unknown.
    /// </summary>
    public static PayloadSignature Classify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z')
            return PayloadSignature.PortableExecutable;

        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            return PayloadSignature.ZipArchive;

        return PayloadSignature.Unknown;
    }

    /// <summary>
    /// Гарантирует, что по пути <paramref name="downloadedFile"/> лежит устанавливаемый
    /// exe. Возвращает путь к exe: исходный файл (PE) либо распакованный из ZIP
    /// <c>Workspace1C.exe</c> (сохраняется рядом с исходным файлом с суффиксом
    /// <c>.extracted.exe</c>). Возвращает null и удаляет исходный файл, если это ни PE,
    /// ни ZIP (HTML-страница и т.п.) либо ZIP без exe внутри. Каждое решение пишется
    /// в журнал <paramref name="logDir"/>/update-payload.log (если каталог задан).
    /// </summary>
    public static string? EnsureExecutablePayload(string downloadedFile, string? logDir = null)
    {
        var signature = DetectSignature(downloadedFile);
        switch (signature)
        {
            case PayloadSignature.PortableExecutable:
                LogLine(logDir, $"Сигнатура MZ (PE): {Path.GetFileName(downloadedFile)} — штатный путь.");
                return downloadedFile;

            case PayloadSignature.ZipArchive:
                return TryExtractExecutable(downloadedFile, logDir);

            default:
                LogLine(logDir, $"Сигнатура не распознана (не exe/zip): {Path.GetFileName(downloadedFile)} — файл удалён, обновление отклонено.");
                TryDelete(downloadedFile);
                return null;
        }
    }

    // ===== Валидация платформы скачанного файла (issue #89) =====

    /// <summary>Результат проверки скачанного файла обновления на соответствие платформе.</summary>
    public enum UpdatePlatformValidation
    {
        /// <summary>Магические байты соответствуют платформе: файл можно применять.</summary>
        Valid,
        /// <summary>Скачан файл для другой операционной системы (например, EXE на Linux).
        /// Применять нельзя — платформа исполняемого формата не совпадает.</summary>
        WrongPlatform,
        /// <summary>Ни ELF, ни MZ — HTML-страница, обрезок или иной формат. Применять нельзя.</summary>
        Unknown,
    }

    /// <summary>Сигнатура Linux-исполняемого файла (ELF): 7F 45 4C 46.</summary>
    public static bool HasElfMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && bytes[0] == 0x7F && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F';

    /// <summary>Сигнатура Windows-исполняемого файла (PE): 4D 5A («MZ»).</summary>
    public static bool HasPeMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';

    /// <summary>
    /// Чистая проверка первых байт файла на соответствие платформе (issue #89).
    /// На Linux ожидается ELF (7F 45 4C 46), на Windows — PE (MZ). Попадание
    /// «чужой» сигнатуры означает, что был скачан файл другой платформы (например,
    /// EXE на Linux — старыми версиями приложения, где выбор ассета был иным):
    /// применять такой бинарник нельзя.
    /// </summary>
    public static UpdatePlatformValidation ValidatePlatformMagic(ReadOnlySpan<byte> bytes, bool linux)
    {
        if (linux)
        {
            if (HasElfMagic(bytes))
                return UpdatePlatformValidation.Valid;
            return HasPeMagic(bytes)
                ? UpdatePlatformValidation.WrongPlatform
                : UpdatePlatformValidation.Unknown;
        }

        if (HasPeMagic(bytes))
            return UpdatePlatformValidation.Valid;
        return HasElfMagic(bytes)
            ? UpdatePlatformValidation.WrongPlatform
            : UpdatePlatformValidation.Unknown;
    }

    /// <summary>
    /// Файловая обёртка над <see cref="ValidatePlatformMagic(ReadOnlySpan{byte}, bool)"/>:
    /// читает первые байты файла и дополнительно сверяет его размер с
    /// <paramref name="expectedSize"/> (если > 0). При любой ошибке чтения возвращает
    /// <see cref="UpdatePlatformValidation.Unknown"/>. Размер файла сверяется с заявленным
    /// размером ассета: расхождение означает недокачанный/посторонний файл.
    /// </summary>
    public static UpdatePlatformValidation ValidatePlatformMagic(string filePath, bool linux, long expectedSize = 0)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (expectedSize > 0 && info.Exists && info.Length != expectedSize)
                return UpdatePlatformValidation.Unknown;

            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[4];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer[read..]);
                if (n == 0)
                    break;
                read += n;
            }

            return ValidatePlatformMagic(buffer[..read], linux);
        }
        catch
        {
            return UpdatePlatformValidation.Unknown;
        }
    }

    /// <summary>
    /// Имя журнала валидации платформы (рядом с журналами обновления).
    /// </summary>
    private const string PlatformValidationLogFileName = "update-platform-validation.log";

    /// <summary>
    /// Валидирует скачанный файл обновления по магическим байтам платформы (issue #89)
    /// и «карантинит» неподходящий. При <see cref="UpdatePlatformValidation.Valid"/> возвращает
    /// <c>null</c> (файл пригоден). Иначе файл переименовывается в <c>*.wrong-platform</c>
    /// (сохраняется для диагностики), решение пишется в журнал <paramref name="logDir"/>,
    /// и возвращается текст понятной ошибки с подсказкой установить обновление вручную.
    /// </summary>
    public static string? RejectWrongPlatformFile(
        string downloadedFile, bool linux, string? htmlUrl, string? logDir = null)
    {
        var validation = ValidatePlatformMagic(downloadedFile, linux);
        if (validation == UpdatePlatformValidation.Valid)
            return null;

        // Карантин: переименовываем, чтобы файл не был принят повторно и остался
        // доступным для диагностики (лог URL и первых байт — ниже и в журнале).
        var quarantined = TryQuarantine(downloadedFile);
        var firstBytes = TryReadFirstBytesHex(quarantined);

        var kind = validation == UpdatePlatformValidation.WrongPlatform
            ? "файл для другой ОС"
            : "файл нераспознанного формата";
        LogLine(
            logDir,
            PlatformValidationLogFileName,
            $"Обновление отклонено ({kind}): {Path.GetFileName(quarantined)}, первые байты: {firstBytes}.");

        // Понятная ошибка пользователю с подсказкой ручной установки (issue #89).
        // Текущая версия приложения и подсказка про «застрявшие» старые версии: карантин
        // wrong-platform появился в 1.0.1.4 — если у пользователя версия старее, её
        // собственный код обновления качает EXE без проверки, и самолечения нет
        // (issue #89, репорт 1.0.1.7). Версия в тексте — чтобы в скриншоте репорта
        // сразу было видно, о какой сборке идёт речь.
        var message = linux
            ? "Скачан файл для другой операционной системы (Windows EXE). " +
              "Обновление отменено: неподходящий бинарник не будет применён. " +
              $"Текущая версия приложения: {VersionInfo.Display()}. " +
              "Если ваша версия старее 1.0.1.4 — карантин неподходящих файлов в ней " +
              "отсутствует, обновитесь вручную (один раз). " +
              "Скачайте сборку «Workspace1C-linux-x64» со страницы выпуска и установите вручную."
            : "Скачан файл для другой операционной системы. " +
              "Обновление отменено: неподходящий бинарник не будет применён. " +
              $"Текущая версия приложения: {VersionInfo.Display()}. " +
              "Скачайте сборку «Workspace1C.exe» со страницы выпуска и установите вручную.";

        if (!string.IsNullOrWhiteSpace(htmlUrl))
            message += Environment.NewLine + htmlUrl;

        return message;
    }

    /// <summary>Переименовывает файл в <c>*.wrong-platform</c>; возвращает путь после переименования.</summary>
    private static string TryQuarantine(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return filePath;
            var target = filePath + ".wrong-platform";
            File.Move(filePath, target, overwrite: true);
            return target;
        }
        catch
        {
            return filePath;
        }
    }

    /// <summary>Первые 8 байт файла в hex — для журнала; при ошибке чтения — пустая строка.</summary>
    private static string TryReadFirstBytesHex(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[8];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer[read..]);
                if (n == 0)
                    break;
                read += n;
            }

            var sb = new StringBuilder(read * 3);
            for (var i = 0; i < read; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(buffer[i].ToString("X2"));
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Пишет строку в журнал валидации (каталог рядом с логами обновления).</summary>
    private static void LogLine(string? logDir, string fileName, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logDir))
                return;
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, fileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch
        {
            // Журнал не критичен: решение уже принято.
        }
    }

    /// <summary>
    /// Ищет в ZIP-архиве запись <c>Workspace1C.exe</c> (точное имя файла,
    /// возможно в подкаталоге) и извлекает её рядом с архивом. Если точной записи нет,
    /// но архив содержит ровно один exe — берётся он. При отсутствии подходящей записи
    /// архив удаляется и возвращается null.
    /// </summary>
    private static string? TryExtractExecutable(string zipPath, string? logDir)
    {
        try
        {
            string? entryName = null;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // Точный приоритет: запись с именем Workspace1C.exe.
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/" + PayloadEntryName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, PayloadEntryName, StringComparison.OrdinalIgnoreCase))
                    {
                        entryName = entry.FullName;
                        break;
                    }
                }

                // Запасной вариант: ровно один exe в архиве.
                if (entryName is null)
                {
                    var exeEntries = archive.Entries
                        .Where(e => e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (exeEntries.Count == 1)
                        entryName = exeEntries[0].FullName;
                }
            }

            if (entryName is null)
            {
                LogLine(logDir, $"ZIP {Path.GetFileName(zipPath)} не содержит {PayloadEntryName} — файл удалён, обновление отклонено.");
                TryDelete(zipPath);
                return null;
            }

            var dir = Path.GetDirectoryName(zipPath);
            if (string.IsNullOrEmpty(dir))
                dir = ".";
            var destPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(zipPath) + ExtractedSuffix);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry(entryName.Replace('\\', '/'))
                    ?? archive.Entries.First(e => string.Equals(
                        e.FullName.Replace('\\', '/'), entryName.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                entry.ExtractToFile(destPath, overwrite: true);
            }

            LogLine(logDir, $"ZIP {Path.GetFileName(zipPath)}: извлечён {entryName} → {Path.GetFileName(destPath)} ({new FileInfo(destPath).Length} байт).");
            return destPath;
        }
        catch (Exception ex)
        {
            LogLine(logDir, $"Ошибка распаковки ZIP {Path.GetFileName(zipPath)}: {ex.Message} — файл удалён, обновление отклонено.");
            TryDelete(zipPath);
            return null;
        }
    }

    /// <summary>Пишет строку в журнал проверки сигнатуры (каталог рядом с логами обновления).</summary>
    private static void LogLine(string? logDir, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logDir))
                return;
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, LogFileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch
        {
            // Журнал не критичен: решение уже принято.
        }
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
            // Не критично — временный файл останется в %TEMP%.
        }
    }
}
