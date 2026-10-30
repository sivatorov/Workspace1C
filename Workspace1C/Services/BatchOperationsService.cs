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

        if (kind == BatchOperationKind.DumpDt)
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
