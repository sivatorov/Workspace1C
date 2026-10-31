using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// на метку времени. Формат метки времени задаётся настройкой приложения
    /// «Формат даты в имени файла выгрузки» (<paramref name="timestampFormat"/>);
    /// токен может задавать и собственный формат — <c>{Дата:формат}</c>/<c>{Date:формат}</c>
    /// (issue #92, комментарий 7OH от 2026-10-31: «формат даты задать можно»).
    /// Пустой шаблон эквивалентен <c>{Имя}</c>. Результат очищается от недопустимых
    /// символов (кроме расширения) и не перезаписывает существующий файл —
    /// при коллизии добавляется «_2», «_3» и т.д.
    /// </summary>
    public static string BuildDumpFilePath(string directory, string infobaseName,
        BatchOperationKind kind, string? template, DateTime? timestamp = null,
        string? timestampFormat = null)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? "." : directory.Trim();
        var extension = kind == BatchOperationKind.DumpCf ? ".cf" : ".dt";
        var at = timestamp ?? DateTime.Now;
        // Формат по умолчанию для токена {Дата} без собственного формата:
        // из настроек приложения (ExportTimestampFormat) либо встроенный.
        var defaultFormat = string.IsNullOrWhiteSpace(timestampFormat)
            ? DumpTimestampFormat
            : timestampFormat!.Trim();
        var body = string.IsNullOrWhiteSpace(template)
            ? SanitizeFileName(infobaseName)
            : ExpandDumpTemplate(template!.Trim(), infobaseName, at, defaultFormat);
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

    /// <summary>
    /// Разворачивает токены шаблона имени файла массовой выгрузки (issue #92):
    /// <c>{Имя}</c>/<c>{Name}</c> — имя базы; <c>{Дата}</c>/<c>{Date}</c> — отметка
    /// времени в формате <paramref name="defaultFormat"/>; <c>{Дата:формат}</c>/
    /// <c>{Date:формат}</c> — отметка в собственном формате токена (пробелы вокруг
    /// «:» допустимы). Некорректный формат токена (FormatException — например
    /// «{Дата:QQQ}») не ломает операцию: метка разворачивается в defaultFormat.
    /// </summary>
    internal static string ExpandDumpTemplate(string template, string infobaseName,
        DateTime timestamp, string defaultFormat)
        => DumpTemplateTokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            if (token.Equals("Имя", StringComparison.Ordinal) ||
                token.Equals("Name", StringComparison.OrdinalIgnoreCase))
                return SanitizeFileName(infobaseName);
            var tokenFormat = match.Groups[2].Success && match.Groups[2].Value.Trim().Length > 0
                ? match.Groups[2].Value.Trim()
                : defaultFormat;
            string expanded;
            try
            {
                expanded = timestamp.ToString(tokenFormat);
            }
            catch (FormatException)
            {
                return timestamp.ToString(defaultFormat);
            }
            // Не-спецификаторы формата (например «QQQ») DateTime.ToString разворачивает
            // буквально, без FormatException: распознаём невалидный формат по тому,
            // что ничего не подставилось — разворот совпал с самим форматом.
            if (string.Equals(expanded, tokenFormat, StringComparison.Ordinal))
                return timestamp.ToString(defaultFormat);
            return expanded;
        });

    /// <summary>Шаблон токенов имени файла массовой выгрузки (issue #92):
    /// {Имя}/{Name} без формата или {Дата}/{Date} с необязательным «:формат».</summary>
    private static readonly Regex DumpTemplateTokenRegex =
        new(@"\{(Имя|Name|Дата|Date)(?:\s*:\s*([^{}]*))?\}", RegexOptions.Compiled);

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
