using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services.Sync;

/// <summary>
/// Фоновый исполнитель синхронизации настроек (п.9, 1.0.0.91): таймер с интервалом
/// <see cref="SettingsSyncSettings.IntervalMinutes"/> (0 — вручную) и запуск по требованию
/// (кнопка окна / задание планировщика). Также умеет регистрировать задание
/// «Синхронизация настроек» в <see cref="IScheduledTaskStore"/>/SchedulerService
/// (тип <see cref="ScheduledTaskKind.SettingsSync"/>) — точка интеграции с расписанием.
/// Потокобезопасность: фоновый тик и ручной запуск не пересекаются (SemaphoreSlim).
/// </summary>
public sealed class SettingsSyncRunner : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly SettingsSyncService _service;
    private readonly IScheduledTaskStore? _taskStore;
    private readonly IAppLogger? _logger;

    private Timer? _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="service">Ядро синхронизации.</param>
    /// <param name="taskStore">Хранилище заданий (nullable — регистрация задания отключена).</param>
    public SettingsSyncRunner(SettingsSyncService service, IScheduledTaskStore? taskStore = null, IAppLogger? logger = null)
    {
        _service = service;
        _taskStore = taskStore;
        _logger = logger;
    }

    /// <summary>Запускает фоновый таймер (если синхронизация включена и интервал > 0).</summary>
    public void Start()
    {
        Stop();
        try
        {
            var settings = SettingsSyncStore.Load();
            if (!settings.Enabled || settings.IntervalMinutes <= 0)
                return;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(15), TickInterval);
            _logger?.Info("Синхронизация настроек: фоновый таймер запущен.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Синхронизация настроек: не удалось запустить таймер", ex);
        }
    }

    /// <summary>Останавливает таймер.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Пора ли запускать синхронизацию по расписанию (чистая логика, тестируема).</summary>
    public static bool IsDue(SettingsSyncSettings settings, DateTime nowUtc)
    {
        if (settings is null || !settings.Enabled || settings.IntervalMinutes <= 0)
            return false;
        var last = settings.LastSyncAt ?? DateTime.MinValue;
        return nowUtc >= last.AddMinutes(settings.IntervalMinutes);
    }

    /// <summary>Выполняет синхронизацию сейчас (кнопка/задание). Журнал — через локаль, без секретов.</summary>
    public async Task<SettingsSyncResult> RunNowAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        var settings = SettingsSyncStore.Load();

        var errors = SettingsSyncService.Validate(settings);
        if (errors.Count > 0)
        {
            var result = new SettingsSyncResult { Success = false, Message = LocalizationManager.T("SettingsSync.ValidationFailed") };
            foreach (var key in errors)
                result.AddError(key);
            result.LastSyncUtc = DateTime.UtcNow;
            return result;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var syncResult = await _service.SyncAsync(settings, line =>
            {
                log?.Invoke(line);
                _logger?.Info(line);
            }, ct).ConfigureAwait(false);

            settings.LastSyncAt = syncResult.LastSyncUtc ?? DateTime.UtcNow;
            if (!string.IsNullOrEmpty(syncResult.Message))
                settings.LastResult = syncResult.Message;
            try { SettingsSyncStore.Save(settings); }
            catch (Exception ex) { _logger?.Error("Синхронизация настроек: не удалось сохранить настройки", ex); }

            return syncResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Фоновый тик: пропускаем, если интервал ещё не наступил или выполнение идёт.</summary>
    private void Tick()
    {
        // Асинхронный запуск без блокировки потока таймера (аудит sync-over-async):
        // RunNowAsync сам защищён гейтом _gate — фоновый тик не пересечётся с ручным
        // запуском. Прежний вариант держал гейт и блокирующе ждал RunNowAsync
        // (RunNowAsync внутренне ждёт ТОТ ЖЕ гейт — до дедлока не доходило только
        // потому, что тик редко совпадал с ручной синхронизацией).
        _ = RunScheduledTickAsync();
    }

    /// <summary>Фоновое выполнение тика: все исключения фиксируются в журнале.</summary>
    private async Task RunScheduledTickAsync()
    {
        try
        {
            var settings = SettingsSyncStore.Load();
            if (!IsDue(settings, DateTime.UtcNow))
                return;
            await RunNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Error("Синхронизация настроек: ошибка фонового запуска", ex);
        }
    }

    /// <summary>
    /// Регистрирует задание «Синхронизация настроек» в планировщике (если его ещё нет).
    /// Возвращает существующее/созданное задание. Задание создаётся ВЫКЛЮЧЕННЫМ
    /// (пользователь сам включает и настраивает время/повтор в окне заданий); основной
    /// интервальный режим — таймер <see cref="SettingsSyncRunner"/> по IntervalMinutes.
    /// </summary>
    public ScheduledTask? EnsureTaskRegistered()
    {
        if (_taskStore is null)
            return null;
        try
        {
            var existing = _taskStore.LoadAll().FirstOrDefault(t => t.Kind == ScheduledTaskKind.SettingsSync);
            if (existing is not null)
                return existing;

            var task = new ScheduledTask
            {
                Name = LocalizationManager.T("Schedule.Kind.SettingsSync"),
                Kind = ScheduledTaskKind.SettingsSync,
                Enabled = false, // включается в окне заданий вручную
                Time = "03:00"
            };
            _taskStore.Save(task);
            _logger?.Info("Синхронизация настроек: создано задание планировщика.");
            return task;
        }
        catch (Exception ex)
        {
            _logger?.Error("Синхронизация настроек: не удалось зарегистрировать задание", ex);
            return null;
        }
    }

    public void Dispose() => Stop();
}