using System.Globalization;
using System.IO;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.TechLogParser;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Анализ технологического журнала» (выпуск 1.0.0.86, п.4):
/// чтение логов из каталога, топы долгих операций (CALL/DBMSSQL/SDBL), сводка
/// блокировок (TLOCK/TDEADLOCK) и группировка исключений EXCP, отбор по базе и
/// пользователю, экспорт в CSV. Чистый .NET — используется WPF и Avalonia.
/// </summary>
public sealed class TechLogAnalysisViewModel : ViewModelBase
{
    /// <summary>Максимум строк в каждом топе.</summary>
    public const int TopLimit = 100;

    private readonly DateTime _now;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<TechLogEvent> _events = Array.Empty<TechLogEvent>();

    private int _periodIndex = 3;
    private string _baseFilter = string.Empty;
    private string _userFilter = string.Empty;
    private string _statusText = string.Empty;
    private bool _isBusy;

    private IReadOnlyList<TopOperationRow> _topCalls = Array.Empty<TopOperationRow>();
    private IReadOnlyList<TopOperationRow> _topSql = Array.Empty<TopOperationRow>();
    private IReadOnlyList<TopOperationRow> _topSdbl = Array.Empty<TopOperationRow>();
    private IReadOnlyList<LockSummaryRow> _locks = Array.Empty<LockSummaryRow>();
    private IReadOnlyList<ExceptionGroupRow> _exceptions = Array.Empty<ExceptionGroupRow>();
    private string _summaryText = string.Empty;

    public TechLogAnalysisViewModel(string logDirectory, int thresholdMs, DateTime now)
    {
        LogDirectory = logDirectory ?? string.Empty;
        ThresholdMs = Math.Max(0, thresholdMs);
        _now = now;
        PeriodOptions = new[]
        {
            LocalizationManager.T("TechLog.Analysis.Period.Hour"),
            LocalizationManager.T("TechLog.Analysis.Period.Today"),
            LocalizationManager.T("TechLog.Analysis.Period.Week"),
            LocalizationManager.T("TechLog.Analysis.Period.All"),
        };
        StatusText = string.IsNullOrWhiteSpace(LogDirectory)
            ? LocalizationManager.T("TechLog.LogDirRequired")
            : LocalizationManager.T("TechLog.Analysis.Status.Ready");
    }

    public TechLogAnalysisViewModel(string logDirectory, int thresholdMs)
        : this(logDirectory, thresholdMs, DateTime.Now)
    {
    }

    /// <summary>Каталог логов (передаётся из окна «Технологический журнал»).</summary>
    public string LogDirectory { get; }

    /// <summary>Порог длительности из окна ТЖ, мс (применяется к «долгим» операциям).</summary>
    public int ThresholdMs { get; }

    public IReadOnlyList<string> PeriodOptions { get; }

    /// <summary>Пресет периода: 0 — последний час, 1 — сегодня, 2 — 7 дней, 3 — весь период.</summary>
    public int PeriodIndex
    {
        get => _periodIndex;
        set
        {
            if (SetProperty(ref _periodIndex, Math.Clamp(value, 0, PeriodOptions.Count - 1)))
                Recompute();
        }
    }

    /// <summary>Отбор по базе: подстрока для базы/процесса/контекста.</summary>
    public string BaseFilter
    {
        get => _baseFilter;
        set
        {
            if (SetProperty(ref _baseFilter, value ?? string.Empty))
                Recompute();
        }
    }

    /// <summary>Отбор по пользователю (<c>usr</c>).</summary>
    public string UserFilter
    {
        get => _userFilter;
        set
        {
            if (SetProperty(ref _userFilter, value ?? string.Empty))
                Recompute();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public IReadOnlyList<TopOperationRow> TopCalls
    {
        get => _topCalls;
        private set => SetProperty(ref _topCalls, value);
    }

    public IReadOnlyList<TopOperationRow> TopSql
    {
        get => _topSql;
        private set => SetProperty(ref _topSql, value);
    }

    public IReadOnlyList<TopOperationRow> TopSdbl
    {
        get => _topSdbl;
        private set => SetProperty(ref _topSdbl, value);
    }

    public IReadOnlyList<LockSummaryRow> Locks
    {
        get => _locks;
        private set => SetProperty(ref _locks, value);
    }

    public IReadOnlyList<ExceptionGroupRow> Exceptions
    {
        get => _exceptions;
        private set => SetProperty(ref _exceptions, value);
    }

    /// <summary>Сводная строка: количество событий по типам.</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>Период для пресета (0 — час, 1 — сегодня, 2 — 7 дней, 3 — весь).</summary>
    public static (DateTime? From, DateTime? To) ResolvePeriod(int index, DateTime now) => index switch
    {
        0 => (now.AddHours(-1), null),
        1 => (now.Date, null),
        2 => (now.Date.AddDays(-6), null),
        _ => (null, null),
    };

    /// <summary>Читает логи в фоне. Вызывать из UI-потока.</summary>
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var (from, to) = ResolvePeriod(PeriodIndex, _now);
        var readQuery = new TechLogQuery { From = from, To = to };

        IsBusy = true;
        StatusText = LocalizationManager.T("TechLog.Analysis.Status.Reading");
        try
        {
            var started = DateTime.Now;
            var events = await Task.Run(() => TechLogReader.ReadDirectory(LogDirectory, readQuery, token), token);
            _events = events;
            Recompute();

            // ПРО PerformanceTrends: модуль сохраняет дневную сводку по прочитанным событиям.
            PerfSnapshotHooks.Notify(new PerfSnapshotContext(BaseFilter, _now.Date, _events));

            var seconds = (DateTime.Now - started).TotalSeconds;
            StatusText = string.Format(
                LocalizationManager.T(_events.Count == 0
                    ? "TechLog.Analysis.Status.Empty"
                    : "TechLog.Analysis.Status.Done"),
                _events.Count, seconds.ToString("0.0", CultureInfo.CurrentCulture));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = LocalizationManager.T("TechLog.Analysis.Status.Error") + " " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Отменяет текущее чтение логов.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>Пересчитывает агрегаты по уже прочитанным событиям (синхронно, из памяти).</summary>
    private void Recompute()
    {
        var result = TechLogAnalyzer.Analyze(_events, BuildAnalysisQuery(), TopLimit);
        TopCalls = result.TopCalls;
        TopSql = result.TopSql;
        TopSdbl = result.TopSdbl;
        Locks = result.Locks;
        Exceptions = result.Exceptions;
        SummaryText = string.Format(
            LocalizationManager.T("TechLog.Analysis.Summary"),
            result.TotalEvents, result.CountCalls, result.CountSql, result.CountSdbl,
            result.CountLocks, result.CountDeadlocks, result.CountExceptions);
    }

    /// <summary>Фильтры текущего состояния для анализатора (период, база, пользователь, порог).</summary>
    private TechLogQuery BuildAnalysisQuery()
    {
        var (from, to) = ResolvePeriod(PeriodIndex, _now);
        return new TechLogQuery
        {
            From = from,
            To = to,
            BaseFilter = BaseFilter,
            UserFilter = UserFilter,
            MinDurationUs = (long)ThresholdMs * 1000L
        };
    }

    /// <summary>CSV-документ по текущему состоянию фильтров (для предпросмотра и тестов).</summary>
    public string BuildCsvDocument()
    {
        var result = TechLogAnalyzer.Analyze(_events, BuildAnalysisQuery(), TopLimit);
        return TechLogCsvBuilder.BuildDocument(result, LogDirectory);
    }

    /// <summary>Пишет CSV-файл (UTF-8 с BOM, как в <see cref="CsvExporter"/>). Возвращает число событий.</summary>
    public int ExportCsv(string path)
    {
        var document = BuildCsvDocument();
        File.WriteAllText(path, document, CsvExporter.Utf8WithBom);
        return _events.Count;
    }
}