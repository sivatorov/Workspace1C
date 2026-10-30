using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Вид массовой (пакетной) операции по списку баз (issue #91):
/// выгрузка каждой базы в файл <c>.dt</c> в выбранный каталог либо загрузка
/// конфигурации из файла <c>.cf</c> в каждую выбранную базу.
/// </summary>
public enum BatchOperationKind
{
    /// <summary>Выгрузка списка баз в файлы .dt (ibcmd infobase dump).</summary>
    DumpDt,

    /// <summary>Выгрузка конфигурации каждой базы в файл .cf (ibcmd config dump, issue #92).</summary>
    DumpCf,

    /// <summary>Загрузка конфигурации из .cf по списку баз (ibcmd config load + apply).</summary>
    LoadConfig
}

/// <summary>
/// Сервис массовых операций по списку баз (issue #91): чистая логика без запуска
/// процессов — построение целевого пути файла и пред-валидация до старта. Само
/// выполнение (запуск ibcmd) делает <see cref="IbcmdService"/>; для юнит-тестов
/// ViewModel подменяет исполнитель делегатом. Используется обеими платформами
/// (WPF и Avalonia) и покрыт юнит-тестами.
/// </summary>
public static class BatchOperationsService
{
    /// <summary>
    /// Целевое имя файла .dt для базы: имя базы очищается от недопустимых в имени
    /// файла символов (к всплывающим «базам» из списка это применяется редко,
    /// но импортированные имена бывают с «/», «:» и т.п.).
    /// </summary>
    public static string BuildDtFilePath(string directory, string infobaseName)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? "." : directory.Trim();
        return Path.Combine(dir, SanitizeFileName(infobaseName) + ".dt");
    }

    /// <summary>Шаблон имени файла массовой выгрузки по умолчанию (issue #92).</summary>
    public const string DefaultDumpTemplate = "{Имя}_{Дата}";

    /// <summary>Формат метки времени токена {Дата} в шаблоне имени файла (issue #92).</summary>
    public const string DumpTimestampFormat = "yyyy-MM-dd_HH-mm";

    /// <summary>
    /// Разбирает вид пакетной операции из сохранённой строки настроек (issue #92):
    /// «DT»/«dt» → DumpDt, «CF»/«cf» → DumpCf, остальное (в т.ч. пусто) → DumpDt.
    /// </summary>
    public static BatchOperationKind ParseDumpKind(string? value)
        => string.Equals(value?.Trim(), "CF", StringComparison.OrdinalIgnoreCase)
            ? BatchOperationKind.DumpCf
            : BatchOperationKind.DumpDt;

    /// <summary>Строка настроек для вида выгрузки: «DT» или «CF».</summary>
    public static string DumpKindToString(BatchOperationKind kind)
        => kind == BatchOperationKind.DumpCf ? "CF" : "DT";

    /// <summary>
    /// Строит целевое имя файла массовой выгрузки по шаблону (issue #92):
    /// токены <c>{Имя}</c>/<c>{Name}</c> заменяются на имя базы, <c>{Дата}</c>/<c>{Date}</c> —
    /// на метку времени. Пустой шаблон эквивалентен <c>{Имя}</c>. Результат
    /// очищается от недопустимых символов (кроме расширения) и не перезаписывает
    /// существующий файл — при коллизии добавляется «_2», «_3» и т.д.
    /// </summary>
    public static string BuildDumpFilePath(string directory, string infobaseName,
        BatchOperationKind kind, string? template, DateTime? timestamp = null)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? "." : directory.Trim();
        var extension = kind == BatchOperationKind.DumpCf ? ".cf" : ".dt";
        var stamp = (timestamp ?? DateTime.Now).ToString(DumpTimestampFormat);
        var body = string.IsNullOrWhiteSpace(template)
            ? SanitizeFileName(infobaseName)
            : template!.Trim()
                .Replace("{Имя}", SanitizeFileName(infobaseName))
                .Replace("{Name}", SanitizeFileName(infobaseName))
                .Replace("{Дата}", stamp)
                .Replace("{Date}", stamp);
        // Имя файла целиком (без расширения) чистим от недопустимых символов:
        // в шаблоне могут остаться «/», «:» и т.п.
        var fileName = SanitizeFileName(body) + extension;

        // Коллизии: две базы могут дать одинаковое имя (шаблон без {Имя} и/или
        // с одним {Дата}), существующий файл не перезаписываем.
        var candidate = Path.Combine(dir, fileName);
        if (!File.Exists(candidate))
            return candidate;
        var stem = fileName[..^extension.Length];
        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(dir, $"{stem}_{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return candidate;
    }

    /// <summary>Убирает из имени файла недопустимые символы и обрезает длину.</summary>
    public static string SanitizeFileName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(c, '_');
        cleaned = cleaned.Replace('_', '_');
        if (cleaned.Length == 0)
            cleaned = "base";
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }

    /// <summary>
    /// Пред-валидация массовой операции ДО старта (issue #91): ошибки параметров
    /// (не указан каталог/файл .cf) и заведомо неподдерживаемых баз (веб-базы ibcmd
    /// не поддерживает; клиент-серверные без профиля СУБД). Возвращает список
    /// сообщений об ошибках; пустой список — валидация пройдена.
    /// </summary>
    /// <param name="targets">Выбранные базы.</param>
    /// <param name="kind">Вид операции.</param>
    /// <param name="path">Каталог для .dt (DumpDt) или файл .cf (LoadConfig).</param>
    public static IReadOnlyList<string> Validate(
        IEnumerable<Infobase>? targets, BatchOperationKind kind, string? path)
    {
        var errors = new List<string>();
        var list = targets?.Where(b => b is not null).ToList() ?? new List<Infobase>();

        if (kind == BatchOperationKind.DumpDt || kind == BatchOperationKind.DumpCf)
        {
            if (string.IsNullOrWhiteSpace(path))
                errors.Add(LocalizationManager.T("BatchOps.NeedDirectory"));
            else if (!Directory.Exists(path!.Trim()))
                errors.Add(LocalizationManager.T("BatchOps.DirectoryNotFound"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(path))
                errors.Add(LocalizationManager.T("BatchOps.NeedCfFile"));
            else if (!File.Exists(path!.Trim()))
                errors.Add(LocalizationManager.T("BatchOps.CfNotFound"));
        }

        foreach (var ib in list)
        {
            if (ib.Connection?.Type == ConnectionType.WebServer)
                errors.Add(string.Format(
                    LocalizationManager.T("BatchOps.WebNotSupportedFormat"), ib.Name));
        }

        // Для клиент-серверных баз ibcmd требует профиль СУБД (как в окне «Развернуть
        // тестовую копию», issue #79): ошибка ДО старта, а не посреди прогона.
        foreach (var ib in list)
        {
            if (ib.Connection?.Type != ConnectionType.ClientServer)
                continue;
            try
            {
                if (DbmsBackupService.Load().Find(ib.Id) is null)
                    errors.Add(string.Format(
                        LocalizationManager.T("BatchOps.NeedDbmsProfileFormat"), ib.Name));
            }
            catch
            {
                // Профиль недоступен — конкретная ошибка придёт от ibcmd по базе.
            }
        }

        return errors;
    }
}
