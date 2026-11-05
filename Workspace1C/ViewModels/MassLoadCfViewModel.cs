using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Массовая загрузка cf через Конфигуратор» (issue #106, автор
/// aepkugesi): выбор списка баз (группа/мультивыделение — как в пакетных операциях),
/// файл .cf и опции перед загрузкой (выгрузка базы в .dt, завершение сеансов,
/// блокировка начала сеансов, блокировка регламентных заданий). Выполнение —
/// <see cref="MassLoadCfService"/> (Конфигуратор /LoadCfg + /UpdateDBCfg, по одной
/// базе последовательно; ошибки базы не прерывают остальные). Чистый .NET без
/// платформенных зависимостей — обе платформы (WPF и Avalonia); окна только
/// привязываются к коллекциям и вызывают методы.
/// </summary>
public sealed class MassLoadCfViewModel : ViewModelBase
{
    private readonly SynchronizationContext? _syncContext;
    private bool _isRunning;
    private string _summary = string.Empty;
    private string _cfFilePath = string.Empty;
    private string _dumpDirectory = string.Empty;
    private string _dumpTemplate = BatchOperationsService.DefaultDumpTemplate;
    private string? _timestampFormat;
    private bool _dumpDtBefore;
    private bool _terminateSessions;
    private bool _denyNewSessions;
    private string _permissionCode = string.Empty;
    private bool _denyScheduledJobs;

    /// <summary>
    /// Подмена запуска Конфигуратора для юнит-тестов (без реальной 1С) —
    /// прокидывается в <see cref="MassLoadCfService.RunnerOverride"/>.
    /// </summary>
    internal Func<Infobase, OneCLauncher.DesignerBatchOperation, string, string?, CancellationToken,
        Task<MassLoadCfRunnerResult>>? RunnerOverride { get; set; }

    public MassLoadCfViewModel(IEnumerable<Infobase> infobases, BatchOpsPreparationService? preparation = null)
    {
        _syncContext = SynchronizationContext.Current;
        Service = new MassLoadCfService(preparation, AppendLog);
        foreach (var ib in infobases ?? Enumerable.Empty<Infobase>())
        {
            if (ib is not null)
                Items.Add(new BatchOperationsItem(ib));
        }
        ApplyFilter();
    }

    /// <summary>Сервис выполнения (создаётся в конструкторе; журнал — общий с окном).
    /// Окно подменяет экземпляр после создания, чтобы передать сервис подготовки
    /// «Перед загрузкой» (<see cref="BatchOpsPreparationService.CreateDefault"/>).</summary>
    internal MassLoadCfService Service { get; set; }

    /// <summary>Строки списка (все базы).</summary>
    public ObservableCollection<BatchOperationsItem> Items { get; } = new();

    /// <summary>Строки списка под фильтром поиска (как в окне массовых операций).</summary>
    public ObservableCollection<BatchOperationsItem> FilteredItems { get; } = new();

    private string _searchText = string.Empty;

    /// <summary>Текст поиска по списку баз. Пусто — показываются все строки.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>Пересобирает <see cref="FilteredItems"/> по <see cref="SearchText"/>
    /// (функции фильтра общие с окном массовых операций).</summary>
    private void ApplyFilter()
    {
        var query = _searchText?.Trim() ?? string.Empty;
        FilteredItems.Clear();
        foreach (var item in Items)
        {
            if (BatchOperationsViewModel.MatchesFilter(
                    BatchOperationsViewModel.BuildSearchText(item), query))
                FilteredItems.Add(item);
        }
    }

    /// <summary>Публичная запись строки в журнал окна (для сообщений сервиса подготовки).</summary>
    public void WriteLog(string line) => AppendLog(line);

    /// <summary>Отметить/снять все видимые строки (запущенные базы не отмечаются).</summary>
    public void SetAll(bool value)
    {
        foreach (var item in FilteredItems)
        {
            if (!item.Infobase.IsRunning)
                item.IsChecked = value;
        }
    }

    /// <summary>Строки лога прогона (по одной строке на шаг).</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Идёт ли прогон в данный момент (блокирует кнопки окна).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>Итоговая строка по завершении прогона.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Файл .cf, загружаемый в каждую выбранную базу.</summary>
    public string CfFilePath
    {
        get => _cfFilePath;
        set => SetProperty(ref _cfFilePath, value ?? string.Empty);
    }

    /// <summary>Выгрузить базу в .dt перед загрузкой (страховочная копия данных).</summary>
    public bool DumpDtBefore
    {
        get => _dumpDtBefore;
        set => SetProperty(ref _dumpDtBefore, value);
    }

    /// <summary>Каталог для .dt-выгрузки (как в массовой выгрузке в dt).</summary>
    public string DumpDirectory
    {
        get => _dumpDirectory;
        set => SetProperty(ref _dumpDirectory, value ?? string.Empty);
    }

    /// <summary>Шаблон имени .dt-файла с токенами {Имя}/{Дата}.</summary>
    public string DumpTemplate
    {
        get => _dumpTemplate;
        set => SetProperty(ref _dumpTemplate, value ?? string.Empty);
    }

    /// <summary>Формат метки времени токена {Дата} (общая настройка выгрузок).</summary>
    public string? TimestampFormat
    {
        get => _timestampFormat;
        set => SetProperty(ref _timestampFormat, value);
    }

    /// <summary>Завершить сеансы пользователей перед загрузкой.</summary>
    public bool TerminateSessions
    {
        get => _terminateSessions;
        set => SetProperty(ref _terminateSessions, value);
    }

    /// <summary>Установить блокировку начала сеансов (снимается после загрузки).</summary>
    public bool DenyNewSessions
    {
        get => _denyNewSessions;
        set => SetProperty(ref _denyNewSessions, value);
    }

    /// <summary>Код разрешения для входа при блокировке сеансов (Конфигуратор «/UC»).
    /// Секретоподобное значение: в настройках приложения не сохраняется.</summary>
    public string PermissionCode
    {
        get => _permissionCode;
        set => SetProperty(ref _permissionCode, value ?? string.Empty);
    }

    /// <summary>Блокировать регламентные задания (возвращаются после загрузки).</summary>
    public bool DenyScheduledJobs
    {
        get => _denyScheduledJobs;
        set => SetProperty(ref _denyScheduledJobs, value);
    }

    /// <summary>Собирает опции операции из состояния чекбоксов окна.</summary>
    internal MassLoadCfOptions BuildOptions() => new()
    {
        CfFilePath = CfFilePath?.Trim() ?? string.Empty,
        DumpDtBefore = DumpDtBefore,
        DumpDirectory = DumpDirectory?.Trim() ?? string.Empty,
        DumpTemplate = string.IsNullOrWhiteSpace(DumpTemplate)
            ? BatchOperationsService.DefaultDumpTemplate
            : DumpTemplate!.Trim(),
        TerminateSessions = TerminateSessions,
        DenyNewSessions = DenyNewSessions,
        PermissionCode = string.IsNullOrWhiteSpace(PermissionCode) ? null : PermissionCode!.Trim(),
        DenyScheduledJobs = DenyScheduledJobs
    };

    /// <summary>
    /// Последовательно загружает конфигурацию во все отмеченные базы (issue #106):
    /// пред-валидация параметров, затем по каждой базе план
    /// <see cref="MassLoadCfPlanner.PlanSteps"/> через <see cref="MassLoadCfService"/>.
    /// Ошибка базы не прерывает остальные; итоговая строка — <see cref="Summary"/>.
    /// Отмена — через <paramref name="ct"/> (кнопка «Остановить»).
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Summary = string.Empty;
        LogLines.Clear();
        foreach (var item in Items)
        {
            item.IsBusy = false;
            item.DurationText = string.Empty;
            item.SetStatusText(string.Empty);
            item.Status = BatchOperationsStatus.Pending;
        }

        int ok = 0, failed = 0;
        try
        {
            var selected = Items.Where(i => i.IsChecked).ToList();
            if (selected.Count == 0)
            {
                AppendLog(LocalizationManager.T("BatchOps.NothingSelected"));
                return;
            }

            var options = BuildOptions();
            var errors = MassLoadCfPlanner.Validate(options, selected.Select(i => i.Infobase));
            if (errors.Count > 0)
            {
                foreach (var error in errors)
                    AppendLog("✖ " + error);
                Summary = LocalizationManager.T("BatchOps.ValidationFailed");
                return;
            }

            AppendLog(string.Format(
                LocalizationManager.T("BatchOps.StartedFormat"), selected.Count,
                LocalizationManager.T("MassLoadCf.KindName")));
            if (options.DenyNewSessions && string.IsNullOrWhiteSpace(options.PermissionCode))
                AppendLog($"[{DateTime.Now:HH:mm:ss}] " +
                          LocalizationManager.T("MassLoadCf.PermissionCodeHintLog"));
            if (options.DumpDtBefore)
                AppendLog($"[{DateTime.Now:HH:mm:ss}] " +
                          LocalizationManager.T("MassLoadCf.DumpDtEnabledLog"));

            // Подмена запуска конфигуратора (юнит-тесты) — прокидывается в сервис.
            Service.RunnerOverride = RunnerOverride;

            foreach (var item in selected)
            {
                ct.ThrowIfCancellationRequested();
                var ib = item.Infobase;
                item.IsBusy = true;
                item.DurationText = string.Empty;
                item.Status = BatchOperationsStatus.Running;

                // Запущенная база пропускается: конфигуратор требует монопольный доступ.
                if (ib.IsRunning)
                {
                    failed++;
                    item.Status = BatchOperationsStatus.Skipped;
                    item.SetStatusText(LocalizationManager.T("BatchOps.SkipRunning"));
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.SkipRunning")}");
                    item.IsBusy = false;
                    continue;
                }

                var startedAt = DateTime.Now;
                MassLoadCfBaseResult result;
                try
                {
                    result = await Service.RunAsync(ib, options, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    item.Status = BatchOperationsStatus.Skipped;
                    item.SetStatusText(LocalizationManager.T("BatchOps.Cancelled"));
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.Cancelled")}");
                    item.IsBusy = false;
                    throw;
                }
                catch (Exception ex)
                {
                    result = MassLoadCfBaseResult.Fail(ex.Message);
                }

                var elapsed = DateTime.Now - startedAt;
                if (result.Success)
                {
                    ok++;
                    item.Status = BatchOperationsStatus.Success;
                    // Успешная база снимается с выбора — повторный «Выполнить»
                    // пройдёт только по оставшимся (как в массовых операциях).
                    item.IsChecked = false;
                    item.SetStatusText(
                        result.Warning ?? result.DtPath ?? LocalizationManager.T("BatchOps.OperationOk"));
                    item.DurationText = FormatElapsed(elapsed);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("BatchOps.OperationOk")} ({FormatElapsed(elapsed)})");
                }
                else
                {
                    failed++;
                    item.Status = BatchOperationsStatus.Failed;
                    var reason = (result.Error ?? string.Empty).Trim();
                    if (result.Warning is not null)
                        reason = reason.Length == 0 ? result.Warning : reason + " " + result.Warning;
                    item.SetStatusText(reason);
                    item.DurationText = FormatElapsed(elapsed);
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — " +
                              $"{LocalizationManager.T("BatchOps.OperationFailed")} ({FormatElapsed(elapsed)})"
                              + (reason.Length > 0 ? $": {reason}" : string.Empty));
                }

                item.IsBusy = false;
            }

            Summary = string.Format(LocalizationManager.T("BatchOps.SummaryFormat"), ok, failed, selected.Count);
            AppendLog("— " + Summary + " —");
        }
        catch (OperationCanceledException)
        {
            // Итог и сводка выставляются и при остановке (как в массовых операциях,
            // issue #92, v1.0.1.29); IsRunning сбрасывается в finally.
            Summary = string.Format(
                LocalizationManager.T("BatchOps.SummaryCancelledFormat"), ok, failed, ok + failed);
            AppendLog("— " + Summary + " —");
            throw;
        }
        finally
        {
            // IsRunning сбрасывается на захваченном контексте UI-потока (issue #92,
            // v1.0.1.31: PropertyChanged с фонового потока «залипал» кнопками).
            if (_syncContext is not null && !ReferenceEquals(SynchronizationContext.Current, _syncContext))
                _syncContext.Post(_ => IsRunning = false, null);
            else
                IsRunning = false;
        }
    }

    /// <summary>Добавляет строку в лог потокобезопасно (через захваченный контекст синхронизации).</summary>
    private void AppendLog(string line)
    {
        if (_syncContext is null)
        {
            LogLines.Add(line);
            return;
        }
        _syncContext.Post(_ =>
        {
            LogLines.Add(line);
            OnPropertyChanged(nameof(LogLines));
        }, null);
    }

    /// <summary>Форматирует интервал времени «м:сс» (как в массовых операциях).</summary>
    internal static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
        return $"{Math.Max(1, (int)elapsed.TotalSeconds)} {LocalizationManager.T("BatchOps.SecondsShort")}";
    }
}
