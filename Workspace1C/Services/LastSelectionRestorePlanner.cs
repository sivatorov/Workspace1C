using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Тип строки, которую нужно восстановить при запуске (issue #182).</summary>
public enum LastSelectionTargetKind
{
    /// <summary>Восстанавливать нечего (нет сохранённых значений или цель не найдена).</summary>
    None,

    /// <summary>Последняя активная строка — информационная база (ищется по Id).</summary>
    Infobase,

    /// <summary>Последняя активная строка — группа (ищется по полному пути).</summary>
    Group
}

/// <summary>
/// Цель восстановления последней активной строки списка баз (issue #182):
/// вид строки и сохранённое значение (Id базы либо полный путь группы).
/// </summary>
/// <param name="Kind">Вид восстанавливаемой строки.</param>
/// <param name="Value">Id базы или полный путь группы (пусто для <see cref="LastSelectionTargetKind.None"/>).</param>
public readonly record struct LastSelectionTarget(LastSelectionTargetKind Kind, string Value)
{
    /// <summary>Восстанавливать нечего.</summary>
    public static LastSelectionTarget None => new(LastSelectionTargetKind.None, string.Empty);

    /// <summary>true, если цель отсутствует.</summary>
    public bool IsNone => Kind == LastSelectionTargetKind.None;
}

/// <summary>
/// Чистая платформенно-независимая логика восстановления последней активной строки
/// списка баз (issue #182). Выделена из WPF-кода главного окна, чтобы одинаково
/// использоваться WPF и Avalonia-оболочкой и покрываться юнит-тестами без UI.
///
/// Правило выбора (приоритет как в прежнем WPF-коде): сохранённая база имеет
/// приоритет над сохранённой группой. Если Id базы задан, но такой базы в текущем
/// списке нет (удалена/импортирована заново), используется путь группы — иначе
/// после перезапуска не выделялось бы ничего (симптом issue #182).
/// </summary>
public static class LastSelectionRestorePlanner
{
    /// <summary>
    /// Определяет цель восстановления по сохранённым значениям настроек.
    /// </summary>
    /// <param name="lastSelectedInfobaseId">Id последней выбранной базы (может быть пустым).</param>
    /// <param name="lastSelectedGroupPath">Полный путь последней выбранной группы (может быть пустым).</param>
    /// <param name="infobaseExists">
    /// Проверка наличия базы с указанным Id в текущем списке. null — считать, что база есть
    /// (чистый выбор без обращения к данным).
    /// </param>
    /// <returns>Цель восстановления; <see cref="LastSelectionTargetKind.None"/>, если восстанавливать нечего.</returns>
    public static LastSelectionTarget Plan(
        string? lastSelectedInfobaseId,
        string? lastSelectedGroupPath,
        Func<string, bool>? infobaseExists = null)
    {
        var infobaseId = (lastSelectedInfobaseId ?? string.Empty).Trim();
        var groupPath = (lastSelectedGroupPath ?? string.Empty).Trim();

        // Приоритет базы: она восстанавливается, если Id задан и база ещё есть в списке.
        if (infobaseId.Length > 0 && (infobaseExists is null || infobaseExists(infobaseId)))
            return new LastSelectionTarget(LastSelectionTargetKind.Infobase, infobaseId);

        // База не найдена (или Id пуст) — пробуем группу: путь мог сохраниться
        // от предыдущего выбора и остаётся валидным.
        if (groupPath.Length > 0)
            return new LastSelectionTarget(LastSelectionTargetKind.Group, groupPath);

        return LastSelectionTarget.None;
    }

    /// <summary>
    /// Возвращает цепочку путей-предков для полного пути группы, включая сам путь:
    /// для «A / B / C» — [«A», «A / B», «A / B / C»]. Нужна, чтобы при восстановлении
    /// раскрыть свёрнутые группы-предки и материализовать строку цели (issue #182).
    /// </summary>
    /// <param name="fullPath">Полный путь группы (разделитель — <see cref="GroupHierarchyHelper.PathSeparator"/>).</param>
    public static IReadOnlyList<string> GetGroupAncestorPaths(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return Array.Empty<string>();

        var segments = fullPath.Split(
            new[] { GroupHierarchyHelper.PathSeparator },
            StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>(segments.Length);
        var current = string.Empty;
        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment.Length == 0)
                continue;
            current = current.Length == 0
                ? segment
                : current + GroupHierarchyHelper.PathSeparator + segment;
            result.Add(current);
        }
        return result;
    }
}
