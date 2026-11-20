using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Колонка «Инспектора процессов 1С» (issue #134, доработка ArtyoZ): набор колонок,
/// которые пользователь может скрывать в окне инспектора. Значения сохраняются в
/// настройках строками — имена членов перечисления.
/// </summary>
public enum ProcessInspectorColumn
{
    /// <summary>База (имя и пояснение сервер/база).</summary>
    Base = 0,

    /// <summary>Режим запуска (Предприятие/Конфигуратор/Служебный).</summary>
    Mode,

    /// <summary>Пользователь (владелец процесса либо имя из /N).</summary>
    User,

    /// <summary>Время запуска процесса.</summary>
    StartTime,

    /// <summary>PID процесса.</summary>
    Pid,

    /// <summary>Строка подключения (/F или /S).</summary>
    Connection,

    /// <summary>Значок «известная база».</summary>
    Known
}

/// <summary>
/// Описание колонки инспектора: значение перечисления и ключ локализации заголовка.
/// </summary>
/// <param name="Column">Колонка.</param>
/// <param name="TitleKey">Ключ локализации заголовка.</param>
public sealed record ProcessInspectorColumnInfo(ProcessInspectorColumn Column, string TitleKey);

/// <summary>
/// Пункт меню «Колонки» инспектора процессов (issue #134, 1.0.1.96): единая модель
/// для WPF и Avalonia. <see cref="IsVisible"/> — фактическая видимость колонки в
/// таблице, по ней окна строят явный индикатор (без привязок к шаблону меню).
/// </summary>
/// <param name="Column">Колонка.</param>
/// <param name="TitleKey">Ключ локализации заголовка.</param>
/// <param name="IsVisible">Колонка видима в таблице.</param>
public sealed record ProcessInspectorColumnMenuItem(
    ProcessInspectorColumn Column, string TitleKey, bool IsVisible);

/// <summary>
/// ViewModel окна «Инспектор процессов 1С» (0.3.9.93): таблица всех запущенных
/// процессов платформы 1С (база/режим/пользователь/время старта/PID/строка
/// подключения/значок сопоставления), обновление по кнопке и по таймеру,
/// завершение выбранных процессов с подтверждением и переход к базе в главном
/// окне по двойному клику (FindInList-механика). Чистый .NET без платформенных
/// зависимостей — обе платформы (WPF и Avalonia); окна только привязываются.
/// <para>
/// issue #134 (доработка ArtyoZ): поддерживается мультивыделение и массовое
/// завершение (одно подтверждение, сводка «завершено / не удалось»), скрытие
/// колонок с сохранением в настройках. Выделение после автообновления (issue #342)
/// восстанавливается для ВСЕХ выбранных строк, а не только для одной.
/// </para>
/// </summary>
public sealed class ProcessInspectorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Период автообновления списка процессов, миллисекунды (~5 секунд).</summary>
    public const int AutoRefreshIntervalMs = 5000;

    /// <summary>Все колонки инспектора в порядке отображения (issue #134).</summary>
    public static IReadOnlyList<ProcessInspectorColumnInfo> AllColumns { get; } = new[]
    {
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.Base, "ProcessInspector.Base"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.Mode, "ProcessInspector.Mode"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.User, "ProcessInspector.User"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.StartTime, "ProcessInspector.StartTime"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.Pid, "ProcessInspector.Pid"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.Connection, "ProcessInspector.Connection"),
        new ProcessInspectorColumnInfo(ProcessInspectorColumn.Known, "ProcessInspector.Known")
    };

    private readonly IRunningInfobasesService _service;
    private readonly IOneCProcessKiller _killer;
    private readonly IDialogService _dialogs;
    private readonly Action<Infobase> _openBase;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly AppSettings? _settings;
    private readonly Action? _saveSettings;
    private readonly HashSet<ProcessInspectorColumn> _hiddenColumns = new();
    private Timer? _timer;
    private int _refreshBusy;
    private ProcessRowViewModel? _selectedRow;

    /// <summary>
    /// Ключи строк, восстановленных последним обновлением (issue #342). Хранятся для
    /// двухфазного восстановления: если пользователь с момента восстановления ничего
    /// не выбрал, повторная установка тех же ключей страхует от «роняющего» выделение
    /// поведения виртуализации контрола (WPF DataGrid / Avalonia ListBox) на последующей
    /// разметке. Как только пользователь выбирает другие строки — набор перезаписывается
    /// и повторная фаза становится no-op.
    /// </summary>
    private HashSet<RowSelectionKey> _lastRestoredKeys = new();

    /// <summary>Идёт пересоздание строк: события выделения контрола игнорируются.</summary>
    private bool _applyingRows;

    /// <param name="service">Источник процессов 1С (WMI на Windows, /proc на Linux).</param>
    /// <param name="killer">Завершение процесса по PID.</param>
    /// <param name="dialogs">Диалоги (подтверждение завершения, сообщения об ошибках).</param>
    /// <param name="infobases">Все базы списка для сопоставления командных строк.</param>
    /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
    /// <param name="dispatchToUi">
    /// Доставка результата фонового опроса в UI-поток (передаёт окно); null —
    /// результаты применяются прямо из рабочего потока (тесты).
    /// </param>
    /// <param name="settings">
    /// Настройки приложения (issue #134): скрытые колонки инспектора. null — все
    /// колонки видимы, выбор не сохраняется (тесты).
    /// </param>
    /// <param name="saveSettings">
    /// Сохранение настроек после изменения набора скрытых колонок (передаёт окно).
    /// null — настройки не сохраняются.
    /// </param>
    public ProcessInspectorViewModel(
        IRunningInfobasesService service,
        IOneCProcessKiller killer,
        IDialogService dialogs,
        IEnumerable<Infobase> infobases,
        Action<Infobase> openBase,
        Action<Action>? dispatchToUi = null,
        AppSettings? settings = null,
        Action? saveSettings = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _killer = killer ?? throw new ArgumentNullException(nameof(killer));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _openBase = openBase ?? throw new ArgumentNullException(nameof(openBase));
        _dispatchToUi = dispatchToUi;
        _settings = settings;
        _saveSettings = saveSettings;
        _infobases = infobases?.Where(ib => ib is not null).ToList()
            ?? new List<Infobase>();

        LoadHiddenColumns();

        Refresh();
        _timer = new Timer(_ => Refresh(), null, AutoRefreshIntervalMs, AutoRefreshIntervalMs);
    }

    /// <summary>Строки таблицы (пересоздаются при каждом обновлении).</summary>
    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();

    /// <summary>
    /// Выбранные строки (issue #134): мультивыделение ставит форма (WPF
    /// <c>DataGrid.SelectedItems</c> / Avalonia <c>ListBox.SelectedItems</c>),
    /// кнопка «Завершить процесс» действует на весь набор.
    /// </summary>
    public ObservableCollection<ProcessRowViewModel> SelectedRows { get; } = new();

    /// <summary>
    /// Составной ключ строки для восстановления выделения (issue #342): PID + полная
    /// командная строка. PID может быть переиспользован ОС после перезапуска процесса —
    /// одна только проверка по PID перескочила бы выделение на другой процесс; командная
    /// строка (строка подключения) остаётся стабильной для одного и того же запуска.
    /// </summary>
    private readonly record struct RowSelectionKey(int Pid, string CommandLine);

    /// <summary>Ключ строки для восстановления выделения (issue #342).</summary>
    private static RowSelectionKey KeyOf(ProcessRowViewModel row) =>
        new(row.Pid, row.FullCommandLine ?? string.Empty);

    /// <summary>
    /// Основная выбранная строка (для привязки <c>SelectedItem</c> и двойного клика);
    /// при мультивыделении — первая из <see cref="SelectedRows"/>.
    /// </summary>
    public ProcessRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;
            if (_applyingRows)
                return;

            // Прямая установка основной строки (привязка SelectedItem) синхронизирует
            // набор: одиночный выбор — одна строка, снятие — пустой набор. Форма при
            // мультивыделении затем дополняет набор через SetSelectedRows.
            if (value is null)
            {
                if (SelectedRows.Count == 1)
                    SelectedRows.Clear();
            }
            else if (!SelectedRows.Contains(value))
            {
                SelectedRows.Clear();
                SelectedRows.Add(value);
            }
        }
    }

    /// <summary>Итог: «Всего процессов: N».</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Есть ли хотя бы один процесс.</summary>
    public bool HasProcesses => Processes.Count > 0;

    /// <summary>Скрытые колонки инспектора (issue #134).</summary>
    public IReadOnlyCollection<ProcessInspectorColumn> HiddenColumns => _hiddenColumns;

    /// <summary>Колонка видима (по умолчанию все колонки видимы).</summary>
    public bool IsColumnVisible(ProcessInspectorColumn column) => !_hiddenColumns.Contains(column);

    /// <summary>
    /// Пункты меню «Колонки» с фактической видимостью (issue #134, 1.0.1.96).
    /// Источник истины — <see cref="IsColumnVisible"/>; окна не хранят состояние
    /// индикатора отдельно и не зависят от шаблона меню.
    /// </summary>
    public IReadOnlyList<ProcessInspectorColumnMenuItem> GetColumnMenuItems() =>
        AllColumns
            .Select(info => new ProcessInspectorColumnMenuItem(
                info.Column, info.TitleKey, IsColumnVisible(info.Column)))
            .ToList();

    /// <summary>
    /// Показывает/скрывает колонку (issue #134) и сохраняет выбор в настройках.
    /// </summary>
    public void SetColumnVisible(ProcessInspectorColumn column, bool visible)
    {
        var changed = visible ? _hiddenColumns.Remove(column) : _hiddenColumns.Add(column);
        if (!changed)
            return;

        PersistHiddenColumns();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HiddenColumns));
    }

    /// <summary>Инвертирует видимость колонки (пункт-чекбокс меню «Колонки»).</summary>
    public void ToggleColumn(ProcessInspectorColumn column) =>
        SetColumnVisible(column, !IsColumnVisible(column));

    /// <summary>
    /// Синхронизирует набор выбранных строк из контрола (issue #134). Во время
    /// пересоздания строк (<see cref="ApplyRows"/>) вызовы игнорируются, иначе
    /// очистка коллекции контролом стёрла бы восстанавливаемый набор.
    /// </summary>
    public void SetSelectedRows(IEnumerable<ProcessRowViewModel>? rows)
    {
        if (_applyingRows)
            return;

        var list = (rows ?? Enumerable.Empty<ProcessRowViewModel>())
            .Where(row => row is not null)
            .Distinct()
            .ToList();

        SelectedRows.Clear();
        foreach (var row in list)
            SelectedRows.Add(row);

        SelectedRow = list.FirstOrDefault();
    }

    /// <summary>
    /// «Обновить» — внеплановый опрос (первый опрос выполняется в конструкторе).
    /// </summary>
    public void Refresh()
    {
        // Один опрос за раз: длительный WMI/обход /proc не должен копить очередь задач.
        if (Interlocked.Exchange(ref _refreshBusy, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                var processes = _service.GetRunningDetails();
                var rows = BuildRows(processes);
                if (_dispatchToUi is null)
                    ApplyRows(rows);
                else
                    _dispatchToUi(() => ApplyRows(rows));
            }
            catch
            {
                // Опрос не должен ронять окно: при ошибке список остаётся прежним.
            }
            finally
            {
                Interlocked.Exchange(ref _refreshBusy, 0);
            }
        });
    }

    /// <summary>
    /// «Завершить процесс»: подтверждение → kill → перезапуск списка. При
    /// мультивыделении завершает ВСЕ выбранные процессы (issue #134).
    /// </summary>
    public void KillSelected() => KillSelectedMany();

    /// <summary>
    /// Массовое завершение выбранных строк (issue #134): одно подтверждение с
    /// количеством процессов, последовательное завершение через
    /// <see cref="IOneCProcessKiller"/>, сводка «завершено / не удалось» с
    /// перечислением PID и обновление списка. При одной строке поведение прежнее
    /// (подробная причина отказа из <see cref="IOneCProcessKiller.LastError"/>).
    /// </summary>
    public void KillSelectedMany()
    {
        var rows = GetEffectiveSelection();
        if (rows.Count == 0)
        {
            // Подсказка вместо молчаливого возврата: без выделения завершать нечего,
            // а молчание выглядело как поломка кнопки (issue #342).
            _dialogs.ShowWarning(
                LocalizationManager.T("ProcessInspector.SelectProcessHint"),
                LocalizationManager.T("ProcessInspector.KillProcess"));
            return;
        }

        var title = LocalizationManager.T("ProcessInspector.KillProcess");

        if (rows.Count == 1)
        {
            KillSingle(rows[0], title);
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ProcessInspector.KillManyConfirm"), rows.Count),
                title))
        {
            return;
        }

        var killedPids = new List<int>(rows.Count);
        var failedPids = new List<int>();
        foreach (var row in rows)
        {
            try
            {
                if (_killer.Kill(
                        row.Pid,
                        string.IsNullOrEmpty(row.StartTimeToken) ? null : row.StartTimeToken))
                {
                    killedPids.Add(row.Pid);
                }
                else
                {
                    failedPids.Add(row.Pid);
                }
            }
            catch
            {
                // Нет прав/процесс уже завершён — считаем ошибкой, продолжаем остальные.
                failedPids.Add(row.Pid);
            }
        }

        var message = string.Format(
            LocalizationManager.T("ProcessInspector.KillSummaryFormat"),
            killedPids.Count,
            failedPids.Count);
        if (failedPids.Count > 0)
        {
            message += "\n" + string.Format(
                LocalizationManager.T("ProcessInspector.KillFailedPidsFormat"),
                string.Join(", ", failedPids));
            _dialogs.ShowWarning(message, title);
        }
        else
        {
            _dialogs.ShowInfo(message, title);
        }

        Refresh();
    }

    /// <summary>«Открыть базу» — переход к строке в главном окне (только для известной базы).</summary>
    public void OpenBase(ProcessRowViewModel row)
    {
        if (row?.Infobase is not null)
            _openBase(row.Infobase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Выделение для действия: весь набор, иначе одиночная основная строка.</summary>
    private List<ProcessRowViewModel> GetEffectiveSelection()
    {
        if (SelectedRows.Count > 0)
            return SelectedRows.Distinct().ToList();

        return SelectedRow is { } single
            ? new List<ProcessRowViewModel> { single }
            : new List<ProcessRowViewModel>();
    }

    /// <summary>Одиночное завершение с подробной причиной отказа (прежнее поведение).</summary>
    private void KillSingle(ProcessRowViewModel row, string title)
    {
        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ProcessInspector.KillConfirm"), row.Pid),
                title))
        {
            return;
        }

        var killed = false;
        string? reason = null;
        try
        {
            killed = _killer.Kill(
                row.Pid,
                string.IsNullOrEmpty(row.StartTimeToken) ? null : row.StartTimeToken);
            reason = _killer.LastError;
        }
        catch (Exception ex)
        {
            // Любая ошибка завершения (нет прав, процесс уже завершён, WMI/proc) —
            // сообщение пользователю, интерфейс не падает.
            reason = ex.Message;
        }

        if (!killed)
        {
            // Причина отказа берётся из киллера (Windows: текст исключения — «Отказано
            // в доступе» и т.п.); если причина недоступна — стандартное объяснение
            // (issue #342: кнопка «Завершить процесс» не работала без пояснения).
            var message = string.IsNullOrWhiteSpace(reason)
                ? string.Format(LocalizationManager.T("ProcessInspector.KillFailedFormat"), row.Pid)
                : string.Format(LocalizationManager.T("ProcessInspector.KillFailedDetailFormat"), row.Pid, reason);
            _dialogs.ShowWarning(message, title);
        }

        Refresh();
    }

    private List<ProcessRowViewModel> BuildRows(IReadOnlyList<RunningOneCProcessDetails> processes)
    {
        var rows = new List<ProcessRowViewModel>(processes.Count);
        var emptyCommandLine = 0;
        foreach (var p in processes)
        {
            // issue #140 (1.0.1.84): инспектор использует полный матчер — прямые ключи,
            // /IBName (при уникальном имени), /IBConnectionString и веб-базы. Раньше
            // здесь был только MatchesCommandLine, поэтому процессы, запущенные по
            // /IBName (отладка из Конфигуратора) и веб-публикации, показывались как
            // «Неизвестная база».
            var matched = RunningInfobaseMatcher.FindOwner(_infobases, p.CommandLine);

            if (RunningMatchDebug.IsEnabled)
            {
                RunningMatchDebug.Log(
                    $"inspector: pid={p.Pid} exe=\"{p.ProcessName}\" cmdline=\"{p.CommandLine}\"");
                RunningMatchDebug.Log($"inspector: matched={(matched is null ? "none" : matched.Name)}");
                if (string.IsNullOrWhiteSpace(p.CommandLine))
                    emptyCommandLine++;
            }

            rows.Add(new ProcessRowViewModel(p, matched));
        }

        // issue #140 (1.0.1.84): на чужой машине пустая командная строка (нет прав WMI)
        // — самая частая причина «Неизвестной базы». Пишем итог, чтобы это отличить
        // от неудачного сопоставления путей/URL.
        if (emptyCommandLine > 0 && RunningMatchDebug.IsEnabled)
            RunningMatchDebug.Log($"inspector: processes without command line={emptyCommandLine}");

        return rows;
    }

    private void ApplyRows(List<ProcessRowViewModel> rows)
    {
        // Строки пересоздаются при каждом опросе, поэтому выделение сохраняется
        // по составному ключу (PID + командная строка), а не по ссылке на старую
        // строку (issue #342). Командная строка в ключе защищает от переиспользования
        // PID ОС: если процесс перезапустился с другим аргументом, тот же PID — это
        // уже другой процесс, выделение на него не переносится.
        // issue #134: восстанавливаются ВСЕ выбранные ключи, а не один.
        var selectedKeys = SelectedRows
            .Select(KeyOf)
            .Concat(SelectedRow is { } primary ? new[] { KeyOf(primary) } : Array.Empty<RowSelectionKey>())
            .Distinct()
            .ToList();

        _applyingRows = true;
        try
        {
            Processes.Clear();
            foreach (var row in rows)
                Processes.Add(row);

            var restored = new List<ProcessRowViewModel>(selectedKeys.Count);
            foreach (var key in selectedKeys)
            {
                // Точное совпадение по составному ключу; при пустой командной строке у
                // старой строки (идентичность неизвестна) — fallback на единственный PID.
                var match = rows.FirstOrDefault(r => KeyOf(r) == key)
                    ?? (key.CommandLine.Length == 0 ? rows.FirstOrDefault(r => r.Pid == key.Pid) : null);
                if (match is not null && !restored.Contains(match))
                    restored.Add(match);
            }

            SelectedRows.Clear();
            foreach (var row in restored)
                SelectedRows.Add(row);
            SelectedRow = restored.FirstOrDefault();

            _lastRestoredKeys = new HashSet<RowSelectionKey>(restored.Select(KeyOf));
        }
        finally
        {
            _applyingRows = false;
        }

        // Двухфазное восстановление (issue #342): часть контролов (виртуализация,
        // пересоздание контейнеров) может сбросить только что восстановленное выделение
        // на последующем проходе разметки. Повторная фаза через диспетчер UI переустанавливает
        // выбор, но только если пользователь за это время не выбрал другие строки.
        if (_lastRestoredKeys.Count > 0 && _dispatchToUi is not null)
            _dispatchToUi(ReassertSelection);

        SummaryText = Processes.Count == 0
            ? LocalizationManager.T("ProcessInspector.Empty")
            : string.Format(LocalizationManager.T("ProcessInspector.SummaryFormat"), Processes.Count);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasProcesses));

        // Форма переставляет SelectedItems по восстановленному набору.
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Вторая фаза восстановления выделения (issue #342): повторно уведомляет о выбранных
    /// строках, если они всё ещё в списке и пользователь не изменил выбор с момента
    /// восстановления. Выполняется асинхронно в UI-потоке ПОСЛЕ первой фазы, когда
    /// контрол успел обработать пересоздание коллекции и разметку.
    /// </summary>
    private void ReassertSelection()
    {
        if (_lastRestoredKeys.Count == 0)
            return;

        var currentKeys = new HashSet<RowSelectionKey>(
            SelectedRows.Select(KeyOf));
        if (SelectedRow is { } current)
            currentKeys.Add(KeyOf(current));

        // Пользователь уже выбрал другие строки (или выбор снят) — не вмешиваемся.
        if (!currentKeys.SetEquals(_lastRestoredKeys))
            return;

        // Строки должны всё ещё присутствовать в коллекции (не удалены следующим опросом).
        if (SelectedRows.Any(row => !Processes.Contains(row)))
            return;

        // Повторное уведомление заставляет привязку SelectedItem перевыставить выбор,
        // а событие — переставить весь набор SelectedItems в контроле.
        OnPropertyChanged(nameof(SelectedRow));
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Восстанавливает скрытые колонки из настроек (issue #134).</summary>
    private void LoadHiddenColumns()
    {
        _hiddenColumns.Clear();
        var stored = _settings?.ProcessInspectorHiddenColumns;
        if (stored is null)
            return;

        foreach (var value in stored)
        {
            if (!Enum.TryParse<ProcessInspectorColumn>(value, ignoreCase: true, out var column))
                continue;
            if (Enum.IsDefined(column))
                _hiddenColumns.Add(column);
        }
    }

    /// <summary>Сохраняет скрытые колонки в настройках (issue #134).</summary>
    private void PersistHiddenColumns()
    {
        if (_settings is null)
            return;

        _settings.ProcessInspectorHiddenColumns = _hiddenColumns
            .OrderBy(column => (int)column)
            .Select(column => column.ToString())
            .ToList();
        _saveSettings?.Invoke();
    }

    /// <summary>Набор скрытых колонок изменён (окно перестраивает шапку/строки).</summary>
    public event EventHandler? ColumnsChanged;

    /// <summary>
    /// Выделение восстановлено после автообновления: окно переставляет
    /// <c>SelectedItems</c> по <see cref="SelectedRows"/> (issue #134, #342).
    /// </summary>
    public event EventHandler? SelectionRestored;
}
