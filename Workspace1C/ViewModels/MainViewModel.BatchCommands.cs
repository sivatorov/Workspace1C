#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Пакетные операции над мультивыделением (0.3.9.90, Windows/WPF): назначение
/// тега, перемещение в группу, избранное, резервирование, доступность, удаление.
/// Диалоги — платформенные (WPF); сами изменения данных — через существующие
/// методы VM, чтобы сохранить синхронизацию (теги, дерево, настройки).
/// </summary>
public partial class MainViewModel
{
    /// <summary>Число целевых баз блока «Для выделенных» (для заголовка меню, issue #53).</summary>
    public string BatchMenuTitle => string.Format(
        LocalizationManager.T("Main.BatchForSelected"),
        BatchOperationTargetCount);

    /// <summary>Назначает введённый тег всем базам мультивыделения.</summary>
    public void AssignTagToBatch(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;
        var bases = BatchOperationTargets.ToList();
        if (bases.Count == 0)
            return;

        var trimmed = tag!.Trim();
        foreach (var ib in bases)
        {
            if (!ib.Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                ib.Tags.Add(trimmed);
                ib.NotifyTagsChanged();
            }
        }
        ScheduleSave();
        PruneActiveTagFilters();
        RefreshTagFilterItems();
    }

    /// <summary>
    /// Назначает несколько тегов всем базам мультивыделения (issue #315).
    /// Каждый тег добавляется каждой базе (регистронезависимо, без дублей);
    /// пустые/дублирующиеся входные значения игнорируются.
    /// </summary>
    public void AssignTagsToBatch(IEnumerable<string> tags)
    {
        var list = tags?
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list is null || list.Count == 0)
            return;
        var bases = BatchOperationTargets.ToList();
        if (bases.Count == 0)
            return;

        foreach (var ib in bases)
        {
            var changed = false;
            foreach (var tag in list)
            {
                if (!ib.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    ib.Tags.Add(tag);
                    changed = true;
                }
            }
            if (changed)
                ib.NotifyTagsChanged();
        }
        ScheduleSave();
        PruneActiveTagFilters();
        RefreshTagFilterItems();
    }

    /// <summary>Добавляет все базы мультивыделения в избранное (без снятия).</summary>
    public void AddBatchToFavorites()
    {
        foreach (var ib in BatchOperationTargets.ToList())
        {
            if (ib.IsFavorite)
                continue;
            ib.IsFavorite = true;
            if (!_favoriteHotkeyIds.Contains(FavoriteKey(ib)) && _favoriteHotkeyIds.Count < 9)
                _favoriteHotkeyIds.Add(FavoriteKey(ib));
        }
        SyncFavoriteHotkeys();
        // Кэш «Избранное» (issue #21): сбрасываем всегда — ниже пересборка идёт
        // только при активном фильтре, а без него кэш остался бы устаревшим.
        _listFilter.Invalidate();

        // Состав списка меняется только при активном временном фильтре.
        if (ShowFavoritesOnly
            || !string.IsNullOrWhiteSpace(SearchText)
            || HasActiveTagFilter)
        {
            InfobasesView.Refresh();
        }
        ScheduleSave();
    }

    /// <summary>Перемещает все базы мультивыделения в выбранную группу.</summary>
    public void MoveBatchToGroup(string groupFullPath)
    {
        foreach (var ib in BatchOperationTargets.ToList())
            MoveInfobaseToGroup(ib, groupFullPath);
    }

    /// <summary>
    /// Выполняет сценарий резервирования для всех баз мультивыделения.
    /// Сценарий выбирается в окне «Сценарии резервирования» (как одиночный
    /// запуск); выполнение идёт последовательно по выбранным базам.
    /// </summary>
    public async Task RunBatchBackupAsync()
    {
        var bases = BatchOperationTargets.ToList();
        if (bases.Count == 0)
            return;

        var store = AppServices.GetRequiredService<IBackupScenarioStore>();
        var scenarios = store.LoadAll();
        if (scenarios.Count == 0)
        {
            _dialogs.ShowInfo(LocalizationManager.T("Backup.NoneConfigured"), LocalizationManager.T("Backup.RunTitle"));
            return;
        }

        if (scenarios.Count == 1)
        {
            foreach (var ib in bases)
                await RunBackupAsync(ib, scenarios[0]);
            return;
        }

        var win = new Workspace1C.BackupScenariosWindow(batchBases: bases, vm: this)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    /// <summary>Проверяет доступность только баз мультивыделения.</summary>
    public void CheckBatchAvailability()
    {
        if (_availabilityCheckRunning)
            return;

        var targets = BatchOperationTargets.ToList();
        if (targets.Count == 0)
        {
            _dialogs.ShowInfo(LocalizationManager.T("Main.ConfigListEmpty"),
                LocalizationManager.T("Main.AvailabilityTitle"));
            return;
        }

        _availabilityCheckRunning = true;
        foreach (var ib in targets)
            ib.SetChecking(true);
        InfobasesView?.Refresh();
        SyncMessage = string.Format(
            LocalizationManager.T("Main.AvailabilityProgress"), 0, targets.Count);

        _ = RunAvailabilityCheckAsync(targets);
    }

    /// <summary>
    /// Удаляет все базы мультивыделения: для каждой открывается общее окно
    /// подтверждения (<see cref="Workspace1C.DeleteInfobaseWindow"/>).
    /// Физическое удаление каталога — по флагу окна.
    /// </summary>
    public void DeleteBatch()
    {
        var bases = BatchOperationTargets.ToList();
        if (bases.Count == 0)
            return;

        foreach (var ib in bases)
        {
            var dlg = new Workspace1C.DeleteInfobaseWindow(ib)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true || !dlg.Confirmed)
                continue;

            if (dlg.DeletePhysically)
            {
                var err = InfobaseMaintenanceService.TryDeleteFileBasePhysically(ib);
                if (err is not null)
                {
                    _dialogs.ShowError(err, LocalizationManager.T("DeleteInfobase.PhysicalDeleteTitle"));
                    if (!_dialogs.Confirm(
                            LocalizationManager.T("Main.ConfirmDeleteFromList"),
                            LocalizationManager.T("Main.DeleteFromListTitle")))
                        continue;
                }
            }

            // Issue #119: очистка кэша базы при удалении из списка (по флагу диалога,
            // включён по умолчанию). Junction-safe; ошибки кэша не блокируют удаление.
            if (dlg.DeleteCache)
            {
                var cacheRemoved = OneCCacheCleaner.Clear(ib, OneCCacheKind.All);
                Log.Info(LogCategory.Cache,
                    $"CACHE cleared on delete «{ib.Name}»: удалено каталогов={cacheRemoved}");
            }

            Infobases.Remove(ib);
            if (ReferenceEquals(SelectedInfobase, ib))
                SelectedInfobase = null;
        }

        Save();
        RebuildGroupTree();
        ExportToIbasesAfterLocalChange();
    }
}
#endif