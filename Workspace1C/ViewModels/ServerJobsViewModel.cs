using System.Collections.ObjectModel;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel диалога «Регламентные задания сервера» (v100112, F1): список заданий
/// кластера через rac «job list» (переиспользует <see cref="IRacClient.GetJobsAsync"/>),
/// фильтр по информационной базе, действия над выбранным заданием — rac «job
/// pause/resume/disable/enable» (<see cref="IRacClient.SetJobStateAsync"/>): «Запустить
/// сейчас» (активация снятого с расписания/приостановленного задания; rac не умеет
/// немедленный запуск — задание выполнит сервер по ближайшему слоту расписания),
/// «Перезапустить» (pause → resume). Вся работа с rac — асинхронно, UI-поток не
/// блокируется. Чистый .NET — без платформенных зависимостей; окна только привязываются.
/// </summary>
public sealed class ServerJobsViewModel : ViewModelBase
{
    private readonly IRacClient _rac;
    private readonly IDialogService _dialogs;
    private readonly RacConnectionParams _connection;
    private readonly Guid _clusterId;

    /// <summary>Имена информационных баз кластера (guid из «job list» → имя из «infobase summary list»).</summary>
    private readonly Dictionary<Guid, string> _infobaseNames;

    private RacJobFilterRow _selectedFilter;
    private RacJobRow? _selectedJob;
    private string _statusText = string.Empty;
    private int _busy;

    private ICommand? _refreshCommand;
    private ICommand? _runNowCommand;
    private ICommand? _restartCommand;
    private ICommand? _pauseCommand;
    private ICommand? _resumeCommand;
    private ICommand? _detailsCommand;

    /// <param name="rac">Клиент rac (список заданий, изменение состояния).</param>
    /// <param name="dialogs">Диалоги (подтверждения, предупреждения).</param>
    /// <param name="connection">Точка подключения (адрес:порт, логин/пароль администратора кластера).</param>
    /// <param name="clusterId">Идентификатор кластера.</param>
    /// <param name="infobases">Базы кластера для колонки «База» и фильтра (null — без фильтра).</param>
    public ServerJobsViewModel(
        IRacClient rac,
        IDialogService dialogs,
        RacConnectionParams connection,
        Guid clusterId,
        IReadOnlyList<RacInfobaseRow>? infobases = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clusterId = clusterId;

        _infobaseNames = new Dictionary<Guid, string>();
        Filters.Add(RacJobFilterRow.All);
        if (infobases is not null)
        {
            foreach (var ib in infobases)
            {
                if (ib.InfobaseId == Guid.Empty || _infobaseNames.ContainsKey(ib.InfobaseId))
                    continue;
                _infobaseNames[ib.InfobaseId] = ib.Name;
                Filters.Add(new RacJobFilterRow(ib.InfobaseId, ib.Name));
            }
        }
        _selectedFilter = Filters[0];
    }

    // ===================== Списки =====================

    /// <summary>Фильтр по информационной базе: «Все базы» + базы кластера.</summary>
    public ObservableCollection<RacJobFilterRow> Filters { get; } = new();

    /// <summary>Выбранный фильтр по базе (null-id — все базы).</summary>
    public RacJobFilterRow SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (!SetProperty(ref _selectedFilter, value))
                return;
            ApplyFilter();
        }
    }

    /// <summary>Все задания кластера (последняя загрузка).</summary>
    public ObservableCollection<RacJobRow> Jobs { get; } = new();

    /// <summary>Задания с учётом фильтра по базе (таблица биндится сюда).</summary>
    public ObservableCollection<RacJobRow> VisibleJobs { get; } = new();

    /// <summary>Выбранное задание (объект действий).</summary>
    public RacJobRow? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (!SetProperty(ref _selectedJob, value))
                return;
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanRunNow));
            OnPropertyChanged(nameof(CanRestart));
        }
    }

    // ===================== Состояние =====================

    /// <summary>Строка состояния («Заданий: N», ошибки rac).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Выполняется ли сейчас rac-запрос (флаг занятости исключает наложение).</summary>
    public bool IsBusy => _busy == 1;

    /// <summary>Подсказка при пустом списке (права администратора кластера).</summary>
    public string EmptyHint => LocalizationManager.T("ServerMonitor.Jobs.EmptyHint");

    /// <summary>Есть ли строки после фильтрации (для подсказки пустого списка).</summary>
    public bool HasVisibleJobs => VisibleJobs.Count > 0;

    // ===================== Доступность действий =====================

    /// <summary>Разрешено ли «Приостановить» (есть выбор, не busy, состояние задания позволяет).</summary>
    public bool CanPause => CanActOnSelected && (SelectedJob?.CanPause ?? false);

    /// <summary>Разрешено ли «Возобновить» (есть выбор, задание приостановлено).</summary>
    public bool CanResume => CanActOnSelected && (SelectedJob?.CanResume ?? false);

    /// <summary>
    /// Разрешено ли «Запустить сейчас»: есть выбор и задание снято с расписания либо
    /// приостановлено (активное задание rac выполняет по расписанию — немедленного
    /// запуска через rac не существует, см. класс-комментарий).
    /// </summary>
    public bool CanRunNow
    {
        get
        {
            var job = SelectedJob;
            if (!CanActOnSelected || job is null)
                return false;
            var state = job.State?.ToLowerInvariant();
            return state is "disabled" or "paused";
        }
    }

    /// <summary>
    /// Разрешено ли «Перезапустить» (pause → resume): есть выбор и задание не снято
    /// с расписания (для снятого перезапуск бессмысленен — сначала «Запустить сейчас»).
    /// </summary>
    public bool CanRestart
    {
        get
        {
            var job = SelectedJob;
            if (!CanActOnSelected || job is null)
                return false;
            var state = job.State?.ToLowerInvariant();
            return state is not ("disabled" or "");
        }
    }

    private bool CanActOnSelected => SelectedJob is not null && !IsBusy;

    // ===================== Команды =====================

    /// <summary>«Обновить»: перечитать список заданий.</summary>
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(async () => await LoadJobsAsync());

    /// <summary>«Запустить сейчас»: активировать задание (enable/resume по состоянию).</summary>
    public ICommand RunNowCommand =>
        _runNowCommand ??= new RelayCommand(async () => await RunNowAsync());

    /// <summary>«Перезапустить»: пауза → возобновление выбранного задания.</summary>
    public ICommand RestartCommand =>
        _restartCommand ??= new RelayCommand(async () => await RestartAsync());

    /// <summary>«Приостановить»: rac «job pause».</summary>
    public ICommand PauseCommand =>
        _pauseCommand ??= new RelayCommand(async () => await ChangeStateAsync(RacJobAction.Pause));

    /// <summary>«Возобновить»: rac «job resume».</summary>
    public ICommand ResumeCommand =>
        _resumeCommand ??= new RelayCommand(async () => await ChangeStateAsync(RacJobAction.Resume));

    /// <summary>«Детали»: окно деталей выбранного задания (окно задаёт <see cref="ShowDetails"/>).</summary>
    public ICommand DetailsCommand =>
        _detailsCommand ??= new RelayCommand(() =>
        {
            if (SelectedJob is not { } job)
                return;
            ShowDetails?.Invoke(job);
        });

    /// <summary>Окно-владелец (для модального моста Avalonia); в окнах переопределяется делегатом.</summary>
    internal Action<Action>? UiDispatcher { get; set; }

    /// <summary>
    /// Показ окна «Детали задания» (задаёт платформенное окно — VM не знает о фреймворке).
    /// null — команда «Детали» не выполняет действий (тесты).
    /// </summary>
    public Action<RacJobRow>? ShowDetails { get; set; }

    // ===================== Загрузка =====================

    /// <summary>
    /// Загружает список заданий кластера (rac «job list», асинхронно). Ошибки не роняют
    /// окно — пишутся в <see cref="StatusText"/>; исключение rac (не найден, таймаут)
    /// показывается предупреждением.
    /// </summary>
    public async Task LoadJobsAsync()
    {
        if (!TryEnterBusy())
            return;
        try
        {
            var jobs = await _rac.GetJobsAsync(_connection, _clusterId).ConfigureAwait(false);
            ReplaceRows(Jobs, jobs.Select(j => new RacJobRow(j, InfobaseName(j))));
            ApplyFilter();
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Jobs.Status.LoadedFormat"), VisibleJobs.Count);
        }
        catch (Exception ex)
        {
            ReplaceRows(Jobs, Enumerable.Empty<RacJobRow>());
            ApplyFilter();
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
                : ex.Message;
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Jobs.Status.LoadFailedFormat"), message);
            ShowWarningOnUi(StatusText, LocalizationManager.T("ServerMonitor.Jobs.Title"));
        }
        finally
        {
            ExitBusy();
        }
    }

    // ===================== Действия =====================

    /// <summary>
    /// «Запустить сейчас» (F1): rac не поддерживает немедленный запуск регламентного
    /// задания (только «job pause/resume/disable/enable»), поэтому действие АКТИВИРУЕТ
    /// задание: снятое с расписания — «job enable», приостановленное — «job resume»;
    /// сервер 1С выполнит его по ближайшему слоту расписания. Активное (scheduled/running)
    /// задание не трогается — пользователь информируется. Подтверждение — диалогом.
    /// </summary>
    public async Task RunNowAsync()
    {
        var job = SelectedJob;
        if (job is null || IsBusy)
            return;

        var state = job.State?.ToLowerInvariant();
        if (state is not ("disabled" or "paused"))
        {
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Jobs.AlreadyActiveFormat"),
                job.Name, job.StateText);
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.Jobs.ConfirmRunNowFormat"), job.Name),
                LocalizationManager.T("ServerMonitor.Jobs.Title")))
            return;

        var action = state == "disabled" ? RacJobAction.Enable : RacJobAction.Resume;
        if (await ExecuteActionAsync(job, action).ConfigureAwait(false))
        {
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Jobs.Done.RunNowFormat"), job.Name);
            await LoadJobsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// «Перезапустить» (F1): rac «job pause» → «job resume» (последовательные вызовы
    /// <see cref="IRacClient.SetJobStateAsync"/>). Для приостановленного задания — только
    /// resume; для снятого с расписания — enable + resume. Подтверждение — диалогом.
    /// </summary>
    public async Task RestartAsync()
    {
        var job = SelectedJob;
        if (job is null || IsBusy)
            return;

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.Jobs.ConfirmRestartFormat"), job.Name),
                LocalizationManager.T("ServerMonitor.Jobs.Title")))
            return;

        var state = job.State?.ToLowerInvariant();
        if (!TryEnterBusy())
            return;
        try
        {
            // Снятое с расписания: вернуть на расписание, затем снять паузу.
            if (state == "disabled")
            {
                if (!await SetStateAsync(job, RacJobAction.Enable).ConfigureAwait(false))
                    return;
                state = null;
            }

            // Приостановленное задание: resume; активное — pause, затем resume.
            if (state == "paused")
            {
                if (!await SetStateAsync(job, RacJobAction.Resume).ConfigureAwait(false))
                    return;
            }
            else if (state is not null)
            {
                if (!await SetStateAsync(job, RacJobAction.Pause).ConfigureAwait(false))
                    return;
                if (!await SetStateAsync(job, RacJobAction.Resume).ConfigureAwait(false))
                    return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Jobs.Done.RestartFormat"), job.Name);
        }
        finally
        {
            ExitBusy();
            await LoadJobsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>«Приостановить»/«Возобновить» (команды <see cref="PauseCommand"/>/<see cref="ResumeCommand"/>).</summary>
    public async Task ChangeStateAsync(RacJobAction action)
    {
        var job = SelectedJob;
        if (job is null || IsBusy)
            return;
        if (await ExecuteActionAsync(job, action).ConfigureAwait(false))
        {
            StatusText = string.Format(
                LocalizationManager.T(action == RacJobAction.Pause
                    ? "ServerMonitor.Jobs.Done.PauseFormat"
                    : "ServerMonitor.Jobs.Done.ResumeFormat"),
                job.Name);
            await LoadJobsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Одно действие над заданием: подтверждение уже сделано вызывающим.</summary>
    private async Task<bool> ExecuteActionAsync(RacJobRow job, RacJobAction action)
    {
        if (!TryEnterBusy())
            return false;
        try
        {
            return await SetStateAsync(job, action).ConfigureAwait(false);
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Выполняет rac-команду изменения состояния задания; при неудаче показывает
    /// предупреждение с текстом <see cref="IRacClient.LastActionError"/>.
    /// </summary>
    private async Task<bool> SetStateAsync(RacJobRow job, RacJobAction action)
    {
        bool ok;
        try
        {
            ok = await _rac.SetJobStateAsync(_connection, _clusterId, job.Id, action)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                string.Format(LocalizationManager.T("ServerMonitor.Jobs.ActionFailedFormat"),
                    job.Name, BuildErrorMessage(ex)),
                LocalizationManager.T("ServerMonitor.Jobs.Title"));
            return false;
        }

        if (ok)
            return true;

        ShowWarningOnUi(
            string.Format(LocalizationManager.T("ServerMonitor.Jobs.ActionFailedFormat"),
                job.Name, BuildActionError(_rac.LastActionError)),
            LocalizationManager.T("ServerMonitor.Jobs.Title"));
        return false;
    }

    // ===================== Внутреннее =====================

    /// <summary>Применяет клиентский фильтр по базе (без rac-вызовов).</summary>
    private void ApplyFilter()
    {
        var filterId = _selectedFilter?.Id;
        IEnumerable<RacJobRow> visible = filterId is Guid id
            ? Jobs.Where(j => j.InfobaseId == id)
            : Jobs;
        ReplaceRows(VisibleJobs, visible);
        // Выбранная строка могла скрыться фильтром.
        if (SelectedJob is { } selected && !VisibleJobs.Contains(selected))
            SelectedJob = null;
        OnPropertyChanged(nameof(VisibleJobs));
        OnPropertyChanged(nameof(HasVisibleJobs));
    }

    private string InfobaseName(RacJobInfo job) =>
        job.InfobaseId is Guid id && _infobaseNames.TryGetValue(id, out var name) &&
        !string.IsNullOrWhiteSpace(name)
            ? name
            : LocalizationManager.T("ServerMonitor.Session.UnknownBase");

    private static void ReplaceRows<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
            target.Add(row);
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var message = ex.Message;
        return string.IsNullOrWhiteSpace(message)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : message;
    }

    private static string BuildActionError(string? lastActionError) =>
        string.IsNullOrWhiteSpace(lastActionError)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : lastActionError;

    private bool TryEnterBusy()
    {
        if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanRunNow));
        OnPropertyChanged(nameof(CanRestart));
        return true;
    }

    private void ExitBusy()
    {
        System.Threading.Interlocked.Exchange(ref _busy, 0);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanRunNow));
        OnPropertyChanged(nameof(CanRestart));
    }

    private void ShowWarningOnUi(string message, string title)
    {
        // LoadJobsAsync и действия идут с ConfigureAwait(false) — предупреждение должно
        // создаваться в UI-потоке (WPF-окно требует STA). Диспетчер задаёт окно.
        var dispatch = UiDispatcher;
        if (dispatch is null)
            _dialogs.ShowWarning(message, title);
        else
            dispatch(() => _dialogs.ShowWarning(message, title));
    }
}
