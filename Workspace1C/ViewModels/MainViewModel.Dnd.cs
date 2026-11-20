using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Drag & drop добавление файловых ИБ из проводника (0.3.9.92): общая логика
/// для обеих платформ. Платформенные шаги (добавление в коллекцию, сохранение,
/// перестройка дерева, уведомление) реализованы partial-методами в
/// MainViewModel.Dnd.Windows.cs / MainViewModel.Dnd.Avalonia.cs.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Статистика добавления баз из перетащенных путей.</summary>
    public sealed record DroppedBasesAddResult(
        int Added,
        int DuplicatesSkipped,
        int NotRecognized,
        string Message);

    /// <summary>
    /// Добавляет файловые информационные базы по путям, перетащенным в главное
    /// окно из проводника/файлового менеджера. Принимаются каталоги с файлом
    /// <c>1Cv8.1CD</c> и сам файл <c>1Cv8.1CD</c> (путь базы — его родительский
    /// каталог). Базы с уже существующим путём (сравнение без учёта регистра и
    /// хвостовых разделителей) пропускаются. Итог выводится через платформенный
    /// диалоговый сервис.
    /// </summary>
    public DroppedBasesAddResult AddInfobasesFromDroppedPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            var empty = LocalizationManager.T("Dnd.NotBaseFolder");
            NotifyDroppedInfobasesResult(empty, isWarning: true);
            return new DroppedBasesAddResult(0, 0, 0, empty);
        }

        var basePaths = DroppedBaseDetector.ResolveBasePaths(paths);
        var notRecognized = paths.Count - basePaths.Count;
        if (basePaths.Count == 0)
        {
            var none = LocalizationManager.T("Dnd.NotBaseFolder");
            NotifyDroppedInfobasesResult(none, isWarning: true);
            return new DroppedBasesAddResult(0, 0, notRecognized, none);
        }

        // Пути уже зарегистрированных файловых баз — для отсечения дубликатов.
        var knownPaths = new HashSet<string>(
            Infobases
                .Where(ib => ib.Connection?.Type == ConnectionType.File)
                .Select(ib => DroppedBaseDetector.NormalizePathForCompare(ib.Connection!.FilePath)),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var duplicates = 0;
        var addedBases = new List<Infobase>();
        foreach (var basePath in basePaths)
        {
            var normalized = DroppedBaseDetector.NormalizePathForCompare(basePath);
            if (knownPaths.Contains(normalized))
            {
                duplicates++;
                continue;
            }

            knownPaths.Add(normalized);

            var infobase = new Infobase
            {
                Id = Guid.NewGuid().ToString(),
                Name = Path.GetFileName(basePath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Group = string.Empty,
                Connection = new ConnectionSettings
                {
                    Type = ConnectionType.File,
                    FilePath = basePath
                },
                // Новая база — в конец списка (максимум текущих порядков + шаг).
                SortOrder = NextDroppedSortOrder()
            };

            AppendDroppedInfobase(infobase);
            addedBases.Add(infobase);
            added++;
        }

        try
        {
            FinalizeDroppedInfobases();
        }
        catch (Exception ex)
        {
            foreach (var infobase in addedBases)
                Services.Audit.AuditHub.Write(Services.Audit.AuditActions.InfobaseAdd, infobase.Name, false);
            _logger.Error("Ошибка сохранения списка после drag&drop добавления баз", ex);
            var error = string.Format(LocalizationManager.T("Dnd.AddError"), ex.Message);
            NotifyDroppedInfobasesError(error);
            return new DroppedBasesAddResult(added, duplicates, notRecognized, error);
        }

        foreach (var infobase in addedBases)
            Services.Audit.AuditHub.Write(Services.Audit.AuditActions.InfobaseAdd, infobase.Name, true);

        if (addedBases.Count > 0)
            SelectedInfobase = addedBases[^1];

        var message = string.Format(LocalizationManager.T("Dnd.AddSuccess"), added, duplicates);
        if (notRecognized > 0)
            message += "\n" + LocalizationManager.T("Dnd.NotBaseFolder");

        NotifyDroppedInfobasesResult(message, isWarning: false);
        return new DroppedBasesAddResult(added, duplicates, notRecognized, message);
    }

    /// <summary>Порядок новой базы: последний существующий + шаг 10 (как у остальных).</summary>
    private int NextDroppedSortOrder()
    {
        var max = Infobases.Count == 0 ? 0 : Infobases.Max(i => i.SortOrder);
        return max + 10;
    }

    /// <summary>Добавляет созданную базу в коллекцию списка (платформенная реализация).</summary>
    partial void AppendDroppedInfobase(Infobase infobase);

    /// <summary>Сохраняет список, перестраивает дерево и экспортирует в ibases.v8i.</summary>
    partial void FinalizeDroppedInfobases();

    /// <summary>Показывает итог операции (инфо или предупреждение).</summary>
    partial void NotifyDroppedInfobasesResult(string message, bool isWarning);

    /// <summary>Показывает ошибку операции.</summary>
    partial void NotifyDroppedInfobasesError(string message);
}
