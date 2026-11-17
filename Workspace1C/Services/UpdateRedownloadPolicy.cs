using System;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Правило фонового перекачивания обновления в режиме «Загружать с предложением
/// установки» (issue #116, комментарий 7OH от 17.11.2026 14:30): если вышла версия
/// новее уже скачанной — файл скачивается заново, а старый удаляется только после
/// успешной загрузки новой. Более старая или та же версия не перекачивается:
/// иначе при отставании GitHub API можно было бы откатить готовый файл назад.
/// </summary>
public static class UpdateRedownloadPolicy
{
    /// <summary>
    /// Нужно ли начинать фоновую загрузку кандидата при уже скачанном релизе.
    /// </summary>
    /// <param name="candidate">Релиз, который предлагает фоновая проверка.</param>
    /// <param name="ready">Уже скачанный релиз (может быть null).</param>
    /// <param name="readyFileExists">Существует ли файл скачанного релиза на диске.
    /// Если файл исчез (уборка temp), ту же версию разрешено скачать заново.</param>
    public static bool ShouldDownload(ReleaseInfo? candidate, ReleaseInfo? ready, bool readyFileExists)
    {
        if (candidate is null)
            return false;
        if (ready is null || !readyFileExists)
            return true;
        return GitHubReleaseService.IsNewerThan(candidate, ready.TagName);
    }

    /// <summary>
    /// Нужно ли удалять прежний скачанный файл после загрузки. Вызывается только
    /// после УСПЕШНОЙ загрузки (путь к новому файлу не пуст); при неудаче старый
    /// файл остаётся нетронутым.
    /// </summary>
    public static bool ShouldCleanup(string? previousPath, string? newPath)
    {
        if (string.IsNullOrWhiteSpace(previousPath) || string.IsNullOrWhiteSpace(newPath))
            return false;
        return !string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase);
    }
}
