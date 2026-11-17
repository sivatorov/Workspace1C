using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Мост функции «Массовая замена в строке подключения баз» (0.3.9.189, функция 6):
/// кандидаты областей из видимых баз (приватные скрытые исключаются), применение с
/// сохранением одноуровневой undo-истории, JSON-бэкапом до мутации и уведомлением,
/// откат последней замены. Общий для обеих платформ файл: персистентность, пересборка
/// дерева и выгрузка ibases.v8i делегируются платформенным реализациям через
/// partial-хук <see cref="AfterConnectionReplaceCommitted"/>.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Записи отката последней выполненной замены (одноуровневая история — повторное применение замещает).</summary>
    private IReadOnlyList<ConnectionReplaceUndoEntry>? _lastConnectionReplaceUndo;

    /// <summary>Есть ли запись о последней замене (для пункта «Отменить последнюю замену строк подключения»).</summary>
    public bool CanUndoConnectionReplace => _lastConnectionReplaceUndo is { Count: > 0 };

    /// <summary>
    /// Кандидаты области из видимых баз списка: приватные базы скрытого профиля
    /// исключаются (единая фильтрация с остальным UI через
    /// <see cref="IProfileService.CanShowPrivateBases"/>); выделенные — по Id
    /// мультивыделения; текущая группа — по полному пути выбранного узла дерева
    /// (<see cref="GroupNodeViewModel.FullPath"/>; служебный узел — пустой список).
    /// </summary>
    public IReadOnlyList<Infobase> GetConnectionReplaceCandidates(ConnectionReplaceScope scope)
    {
        var canShowPrivate = CanShowPrivateBasesForConnectionReplace();
        var visible = ConnectionReplacementPlanner.FilterVisibleInfobases(Infobases, canShowPrivate);
        // Область «Выделенные» (issue #53): цели — мультивыделение, выбранная группа
        // (включая вложенные) либо одна выделенная база. Issue #137: раньше для
        // группы список кандидатов был пуст, и «Сменить платформу» молча выходила.
        var ids = scope != ConnectionReplaceScope.BatchSelected || _batchSelectedIds.Count > 0
            ? BatchSelectedIds
            : (IReadOnlyCollection<string>)BatchOperationTargetsIncludingGroup
                .Select(ib => ib.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToArray();
        return ConnectionReplacementPlanner.SelectCandidates(visible, ids, SelectedGroupNode?.FullPath, scope);
    }

    /// <summary>
    /// Применение замены (вызывается окном через колбэк VM): сохраняет записи отката
    /// (замещает предыдущие — одноуровневая история), создаёт JSON-бэкап списка ПЕРЕД
    /// применением (страховка вне сессии; ошибка записи не блокирует операцию), затем
    /// выполняет платформенную персистентность/пересборку дерева, пишет лог и шлёт
    /// уведомление (kind Success). Самово применение баз уже выполнено
    /// <see cref="ConnectionReplacementPlanner.Apply"/> до вызова этого метода.
    /// </summary>
    public void ApplyConnectionReplace(IReadOnlyList<ConnectionReplaceUndoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var list = entries.ToList();
        if (list.Count == 0)
            return;

        CreateConnectionReplaceBackup();

        _lastConnectionReplaceUndo = list;
        OnPropertyChanged(nameof(CanUndoConnectionReplace));

        AfterConnectionReplaceCommitted();

        _logger.Info($"Массовая замена строк подключения: изменено баз {list.Count}");
        TryNotifyConnectionReplaceApplied(list.Count);
    }

    /// <summary>
    /// Откат последней замены (из окна или пункта «Утилит»): восстанавливает прежние
    /// настройки через <see cref="ConnectionReplacementPlanner.Undo"/> (мутация объектов
    /// на месте) и выполняет ту же персистентность, что и при применении. Без записи — no-op.
    /// </summary>
    public void UndoLastConnectionReplace()
    {
        if (_lastConnectionReplaceUndo is not { Count: > 0 } entries)
            return;

        ConnectionReplacementPlanner.Undo(entries);
        var count = entries.Count;
        _lastConnectionReplaceUndo = null;
        OnPropertyChanged(nameof(CanUndoConnectionReplace));

        AfterConnectionReplaceCommitted();

        _logger.Info($"Отменена последняя замена строк подключения: восстановлено баз {count}");
    }

    // ---------- Внутреннее ----------

    /// <summary>
    /// Видимость приватных баз текущего профиля. Сбой получения сервиса (тестовый
    /// контекст) не скрывает базы — единообразно с <c>IsVisibleForPrivateFilter</c>
    /// остального UI.
    /// </summary>
    private bool CanShowPrivateBasesForConnectionReplace()
    {
        try
        {
            return AppServices.GetRequiredService<IProfileService>().CanShowPrivateBases;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// JSON-бэкап всего списка баз и групп в каталог данных приложения
    /// (<see cref="PlatformPaths.AppDataDirectory"/>, портативный режим учитывается)
    /// перед применением замены. Файл <c>connection_replace_backup_<yyyyMMdd_HHmmss>.json</c> —
    /// страховка вне сессии (undo живёт только в памяти). Ошибка записи не блокирует
    /// применение — логируется через Warn.
    /// </summary>
    private void CreateConnectionReplaceBackup()
    {
        try
        {
            var directory = PlatformPaths.AppDataDirectory;
            Directory.CreateDirectory(directory);
            // JSON-бэкап перед заменой строк подключения сохраняет и заметки баз (п.8, 1.0.0.90).
            var snapshot = InfobaseJsonTransfer.BuildSnapshot(Infobases, Groups, GetOrCreateNotesStore().GetAll());
            var fileName = $"connection_replace_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            File.WriteAllText(Path.Combine(directory, fileName), InfobaseJsonTransfer.Serialize(snapshot));
            _logger.Info($"Создан JSON-бэкап перед заменой строк подключения: {fileName}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось создать JSON-бэкап перед заменой строк подключения: {ex.Message}");
        }
    }

    /// <summary>
    /// Системное уведомление об успешном применении (fire-and-forget). Сбой уведомления
    /// не должен ронять операцию — гасится и логируется.
    /// </summary>
    private void TryNotifyConnectionReplaceApplied(int affectedCount)
    {
        try
        {
            AppServices.GetRequiredService<INotificationService>().Show(
                LocalizationManager.T("App.Title"),
                string.Format(LocalizationManager.T("ConnectionReplace.Result.AppliedFormat"), affectedCount),
                NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Системное уведомление о замене строк подключения не показано: {ex.Message}");
        }
    }

    /// <summary>
    /// Платформенный хук после применения/отката замены: Windows — отложенное сохранение
    /// списка (<c>ScheduleSave</c>), пересборка дерева групп (<c>RebuildGroupTree</c>) и
    /// выгрузка ibases.v8i (<c>ExportToIbasesAfterLocalChange</c>); Linux — те же шаги
    /// через <c>SaveSilently</c>/<c>RebuildTree</c>. Реализации в
    /// <c>MainViewModel.ConnectionReplace.Windows.cs</c> / <c>*.Avalonia.cs</c>.
    /// </summary>
    partial void AfterConnectionReplaceCommitted();
}