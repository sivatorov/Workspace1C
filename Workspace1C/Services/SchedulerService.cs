using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Планировщик заданий по расписанию (issue #286). Работает, пока приложение запущено:
/// периодически проверяет наступление времени заданий (время «HH:mm» + дни недели) и
/// выполняет их последовательно (одна операция 1С за раз). Момент запуска вычисляется
/// чистым классом <see cref="ScheduleCalculator"/>. Пропущенные запуски (приложение было
/// выключено) «догоняются» при старте — см. <see cref="RunCatchUpAsync"/> и
/// <see cref="ScheduleCatchUpCalculator"/>. Результаты записываются в задание и в журнал.
/// </summary>
public class SchedulerService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);

    private readonly IScheduledTaskStore _tasks;
    private readonly IBackupScenarioStore _scenarios;
    private readonly IInfobaseRepository _repository;
    private readonly IBackupService _backup;
    private readonly IConfigUpdateService _configUpdate;
    private readonly IInfobaseAdminService _admin;
    private readonly GitHubReleaseService _gitHub;
    private readonly IAppUpdateService _updateService;
    private readonly INotificationService _notifications;
    private readonly IPlatformUpdateService _platformUpdate;
    private readonly IPlatformVersionService _platformVersions;
    private readonly IOneCUpdatesService _updates;
    private readonly ICustomConfigTypesStore _configTypes;
    private readonly IMainWindowNotifier _mainWindowNotifier;
    private readonly Workspace1C.Services.Sync.SettingsSyncRunner? _settingsSync;
    private readonly TestDeployService? _testDeploy;
    private readonly IAppLogger? _logger;

    private Timer? _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTime?> _nextRuns = new(StringComparer.Ordinal);

    public SchedulerService(
        IScheduledTaskStore tasks,
        IBackupScenarioStore scenarios,
        IInfobaseRepository repository,
        IBackupService backup,
        IConfigUpdateService configUpdate,
        IInfobaseAdminService admin,
        GitHubReleaseService gitHub,
        IAppUpdateService updateService,
        INotificationService notifications,
        IPlatformUpdateService platformUpdate,
        IPlatformVersionService platformVersions,
        IOneCUpdatesService updates,
        ICustomConfigTypesStore configTypes,
        IMainWindowNotifier mainWindowNotifier,
        Workspace1C.Services.Sync.SettingsSyncRunner? settingsSync = null,
        TestDeployService? testDeploy = null,
        IAppLogger? logger = null)
    {
        _tasks = tasks;
        _scenarios = scenarios;
        _repository = repository;
        _backup = backup;
        _configUpdate = configUpdate;
        _admin = admin;
        _gitHub = gitHub;
        _updateService = updateService;
        _notifications = notifications;
        _platformUpdate = platformUpdate;
        _platformVersions = platformVersions;
        _updates = updates;
        _configTypes = configTypes;
        _mainWindowNotifier = mainWindowNotifier;
        _settingsSync = settingsSync;
        _testDeploy = testDeploy;
        _logger = logger;
    }

    /// <summary>Запускает планировщик (после входа в профиль). Повторный вызов перезапускает.</summary>
    public void Start()
    {
        Stop();
        Refresh();
        _timer = new Timer(_ => Tick(), null, InitialDelay, TickInterval);
    }

    /// <summary>Останавливает планировщик (при выходе из приложения).</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Выполняет задание немедленно (кнопка «Выполнить сейчас» в окне заданий).
    /// Записывает результат и сохраняет задание. Возвращает null при неожиданной ошибке.
    /// </summary>
    public async Task<BackupRunResult?> RunNowAsync(ScheduledTask task)
    {
        if (task is null)
            return null;
        var result = await ExecuteAsync(task).ConfigureAwait(false);
        MarkRun(task);
        SaveResult(task, result);
        NotifyJobFinished(task, result);
        return result;
    }

    /// <summary>
    /// Догоняющее выполнение пропущенных заданий (функция №7): вызывается один раз при
    /// старте приложения после запуска планировщика. Для каждого задания, чьё плановое
    /// время наступило, пока приложение было выключено (см. <see cref="ScheduleCatchUpCalculator.IsMissed"/>),
    /// выполняет его сразу — один раз, переиспользуя общий путь выполнения
    /// (<see cref="ExecuteAsync"/>), с записью результата и отметкой времени запуска.
    /// Возвращает число выполненных заданий; любая ошибка не роняет старт приложения.
    /// </summary>
    /// <param name="catchUpEnabled">Глобальная настройка «догонять пропущенные задания».</param>
    public async Task<int> RunCatchUpAsync(bool catchUpEnabled)
    {
        if (!catchUpEnabled)
            return 0;

        // Не конфликтуем с фоновым тиком: одна операция 1С за раз.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = DateTime.Now;
            var executed = 0;
            foreach (var task in _tasks.LoadAll())
            {
                if (!ScheduleCatchUpCalculator.IsMissed(task, now))
                    continue;

                // issue #80 «Настройка логирования»: задания по расписанию — направление
                // CM_JOBS (INFO пишется при включённых категории и общем CM_ON).
                Log.Info(LogCategory.Jobs, $"Догоняющее выполнение пропущенного задания «{task.Name}» ({task.Kind}).");
                var result = await ExecuteAsync(task).ConfigureAwait(false);
                MarkRun(task);
                SaveResult(task, result);
                NotifyJobFinished(task, result);
                // Следующий момент — с запасом в минуту, чтобы тик не выполнил задание дважды.
                _nextRuns[task.Id] = ScheduleCalculator.ComputeNextRun(task, now.AddMinutes(1));
                executed++;
            }

            if (executed > 0)
            {
                // issue #80: задания по расписанию — направление CM_JOBS.
                Log.Info(LogCategory.Jobs, $"Догоняющее выполнение завершено: выполнено пропущенных заданий — {executed}.");
                NotifyCatchUpDone(executed);
            }

            return executed;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка догоняющего выполнения пропущенных заданий", ex);
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Фиксирует момент фактического запуска задания: локальное время — для отображения
    /// в списке заданий, UTC — для сравнения с плановым временем при догонянии (функция №7).
    /// </summary>
    private static void MarkRun(ScheduledTask task)
    {
        task.LastRunAt = DateTime.Now;
        task.LastRunUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Системное уведомление о выполненном догонянии (функция №7): только когда число
    /// выполненных заданий больше нуля. Ошибки уведомления не влияют на планировщик.
    /// </summary>
    private void NotifyCatchUpDone(int count)
    {
        try
        {
            _notifications.Show(
                LocalizationManager.T("App.Title"),
                string.Format(LocalizationManager.T("Schedule.CatchUpDone"), count),
                NotificationKind.Info,
                NotificationEvent.ScheduledTask);
        }
        catch (Exception ex)
        {
            _logger?.Error("Не удалось показать системное уведомление о догоняющем выполнении", ex);
        }
    }

    /// <summary>
    /// Колбэк таймера планировщика (v100112): полностью асинхронный путь —
    /// <see cref="ProcessDueAsync"/> ожидается через await, а не через
    /// <c>GetAwaiter().GetResult()</c> (sync-over-async: блокировка потока пула и
    /// риск дедлока при синхронном контексте). Семафорная защита от параллельного
    /// выполнения сохранена: <c>WaitAsync(0)</c> — «занято — тихо выходим», как
    /// раньше <c>Wait(0)</c>. <c>async void</c> здесь безопасен: весь путь (включая
    /// ожидание семафора) обёрнут в try/catch, исключение не уйдёт в ThreadPool.
    /// </summary>
    private async void Tick()
    {
        try
        {
            // Не выполняем параллельную обработку: операции 1С конфликтуют между собой.
            if (!await _gate.WaitAsync(0).ConfigureAwait(false))
                return;
            try
            {
                await ProcessDueAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка фонового планировщика заданий", ex);
        }
    }

    private async Task ProcessDueAsync()
    {
        var now = DateTime.Now;
        foreach (var task in _tasks.LoadAll())
        {
            if (!task.Enabled)
                continue;

            var next = _nextRuns.TryGetValue(task.Id, out var cached) ? cached : null;
            if (next is null || now < next)
                continue;

            // issue #80: задания по расписанию — направление CM_JOBS.
            Log.Info(LogCategory.Jobs, $"Расписание: наступило время задания «{task.Name}» ({task.Kind}).");
            var result = await ExecuteAsync(task).ConfigureAwait(false);
            MarkRun(task);
            SaveResult(task, result);
            NotifyJobFinished(task, result);
            // Следующий момент — с запасом в минуту, чтобы не выполнить задание дважды подряд.
            _nextRuns[task.Id] = ScheduleCalculator.ComputeNextRun(task, now.AddMinutes(1));
        }
    }

    private void SaveResult(ScheduledTask task, BackupRunResult? result)
    {
        if (result is not null)
        {
            var files = result.CreatedFiles ?? Array.Empty<string>();
            task.LastRunSuccess = result.Success;
            task.LastRunMessage = result.Success
                ? (files.Count > 0 ? string.Join("; ", files) : result.ScenarioName)
                : result.ErrorMessage;
        }
        else
        {
            task.LastRunSuccess = false;
            task.LastRunMessage = LocalizationManager.T("Schedule.RunFailed");
        }

        try { _tasks.Save(task); }
        catch (Exception ex) { _logger?.Error("Не удалось сохранить состояние задания", ex); }
    }

    /// <summary>
    /// Системное уведомление о завершении задания по расписанию (функция №4): успех или
    /// ошибка, имя задания и тип. Вызывается только когда задание реально выполнилось
    /// (не на каждое тиканье таймера). Ошибки уведомления не влияют на планировщик.
    /// </summary>
    private void NotifyJobFinished(ScheduledTask task, BackupRunResult? result)
    {
        // Задание «Проверить наличие обновления платформы» (issue #366) уведомляет
        // само себя по выбранному каналу (главное окно / системные уведомления) —
        // здесь оно пропускается, чтобы не дублировать сообщение. То же для проверки
        // обновления конфигурации (issue #367).
        if (task.Kind is ScheduledTaskKind.PlatformUpdateCheck or ScheduledTaskKind.ConfigurationUpdateCheck)
            return;

        // Тихий результат (issue #8): задание «Обновить приложение» при актуальной версии
        // завершается успешно, но уведомлять о каждом прогоне по расписанию нельзя — любое
        // повторное уведомление («задание выполнено успешно») было бы спамом. Запись в
        // журнал задания при этом сохраняется как обычно.
        if (result is { SuppressNotification: true })
            return;

        try
        {
            var success = result is { Success: true };
            _notifications.Show(
                LocalizationManager.T("App.Title"),
                string.Format(
                    LocalizationManager.T(success ? "Notify.JobDone" : "Notify.JobError"),
                    task.Name, KindName(task.Kind)),
                success ? NotificationKind.Success : NotificationKind.Error,
                NotificationEvent.ScheduledTask);
        }
        catch (Exception ex)
        {
            _logger?.Error("Не удалось показать системное уведомление о задании", ex);
        }
    }

    /// <summary>Локализованное имя типа задания (как в окне заданий).</summary>
    private static string KindName(ScheduledTaskKind kind) => kind switch
    {
        ScheduledTaskKind.Backup => LocalizationManager.T("Schedule.Kind.Backup"),
        ScheduledTaskKind.UpdateConfig => LocalizationManager.T("Schedule.Kind.UpdateConfig"),
        ScheduledTaskKind.BackupThenUpdateConfig => LocalizationManager.T("Schedule.Kind.BackupThenUpdateConfig"),
        ScheduledTaskKind.CheckIntegrity => LocalizationManager.T("Schedule.Kind.CheckIntegrity"),
        ScheduledTaskKind.UpdateApp => LocalizationManager.T("Schedule.Kind.UpdateApp"),
        ScheduledTaskKind.PlatformUpdateCheck => LocalizationManager.T("Schedule.Kind.PlatformUpdateCheck"),
        ScheduledTaskKind.ConfigurationUpdateCheck => LocalizationManager.T("Schedule.Kind.ConfigurationUpdateCheck"),
        ScheduledTaskKind.InstallExtension => LocalizationManager.T("Schedule.Kind.InstallExtension"),
        ScheduledTaskKind.GitCommitConfig => LocalizationManager.T("Schedule.Kind.GitCommitConfig"),
        ScheduledTaskKind.DbmsBackup => LocalizationManager.T("Schedule.Kind.DbmsBackup"),
        ScheduledTaskKind.SettingsSync => LocalizationManager.T("Schedule.Kind.SettingsSync"),
        ScheduledTaskKind.TestDeploy => LocalizationManager.T("Schedule.Kind.TestDeploy"),
        _ => LocalizationManager.T("Schedule.Kind.Backup")
    };

    private async Task<BackupRunResult?> ExecuteAsync(ScheduledTask task)
    {
        try
        {
            return task.Kind switch
            {
                ScheduledTaskKind.Backup => await RunBackupAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.UpdateConfig => await RunUpdateConfigAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.BackupThenUpdateConfig => await RunBackupThenUpdateAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.CheckIntegrity => await RunCheckIntegrityAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.UpdateApp => await RunUpdateAppAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.PlatformUpdateCheck => await RunPlatformUpdateCheckAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.ConfigurationUpdateCheck => await RunConfigurationUpdateCheckAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.InstallExtension => await RunInstallExtensionAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.GitCommitConfig => await RunGitCommitAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.DbmsBackup => await RunDbmsBackupAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.SettingsSync => await RunSettingsSyncAsync(task).ConfigureAwait(false),
                ScheduledTaskKind.TestDeploy => await RunTestDeployAsync(task).ConfigureAwait(false),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger?.Error($"Ошибка выполнения задания «{task.Name}» ({task.Kind})", ex);
            return new BackupRunResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<BackupRunResult?> RunBackupAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));

        var scenario = ResolveScenario(task.ScenarioId);
        if (scenario is null)
            return Fail(LocalizationManager.T("Schedule.ScenarioNotFound"));

        return await _backup.RunAsync(infobase, scenario).ConfigureAwait(false);
    }

    private async Task<BackupRunResult?> RunUpdateConfigAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));

        if (string.IsNullOrWhiteSpace(task.ConfigFilePath))
            return Fail(LocalizationManager.T("Schedule.CfgFileRequired"));

        return await _configUpdate.UpdateConfigAsync(infobase, task.ConfigFilePath!).ConfigureAwait(false);
    }

    /// <summary>Установка расширения из .cfe (выпуск 1.0.0.69): имя — имя файла без расширения.</summary>
    private async Task<BackupRunResult?> RunInstallExtensionAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));
        if (string.IsNullOrWhiteSpace(task.ConfigFilePath))
            return Fail(LocalizationManager.T("Schedule.CfgFileRequired"));

        var name = ConfigExtensionService.SuggestName(task.ConfigFilePath);
        var run = await ConfigExtensionService.InstallAsync(infobase, task.ConfigFilePath!, name).ConfigureAwait(false);
        return run.Success
            ? new BackupRunResult { Success = true, ScenarioName = name }
            : Fail(run.Error ?? LocalizationManager.T("Extensions.Failed"));
    }

    /// <summary>Выгрузка конфигурации в Git (выпуск 1.0.0.69) по сохранённым настройкам базы.</summary>
    private async Task<BackupRunResult?> RunGitCommitAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));
        var settings = GitVersioningService.Load().Find(infobase.Id);
        if (settings is null)
            return Fail(LocalizationManager.T("Git.NotConfigured"));

        var result = await GitVersioningService.RunAsync(infobase, settings,
            // issue #80: задания по расписанию — направление CM_JOBS.
            message => Log.Info(LogCategory.Jobs, $"Git «{infobase.Name}»: {message}")).ConfigureAwait(false);
        return result.Success
            ? new BackupRunResult { Success = true, ScenarioName = result.Message, SuppressNotification = !result.Committed }
            : Fail(result.Message);
    }

    /// <summary>Копия средствами СУБД (выпуск 1.0.0.69) по профилю базы.</summary>
    private async Task<BackupRunResult?> RunDbmsBackupAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));
        var profile = DbmsBackupService.Load().Find(infobase.Id);
        if (profile is null)
            return Fail(LocalizationManager.T("Dbms.NotConfigured"));
        var result = await DbmsBackupService.RunAsync(profile,
            // issue #80: задания по расписанию — направление CM_JOBS.
            message => Log.Info(LogCategory.Jobs, $"СУБД «{infobase.Name}»: {message}")).ConfigureAwait(false);
        result.ScenarioName = string.IsNullOrEmpty(result.ScenarioName) ? profile.Database : result.ScenarioName;
        return result;
    }

    private async Task<BackupRunResult?> RunCheckIntegrityAsync(ScheduledTask task)
    {
        var infobase = ResolveInfobase(task.InfobaseId);
        if (infobase is null)
            return Fail(LocalizationManager.T("Schedule.BaseNotFound"));

        // Тихий запуск chdbfl с ожиданием завершения (0.3.9.87): процесс без окна,
        // результат (успех/код выхода) возвращается в BackupRunResult.
        return await _admin.CheckIntegrityQuiet(infobase).ConfigureAwait(false);
    }

    private async Task<BackupRunResult?> RunBackupThenUpdateAsync(ScheduledTask task)
    {
        var backupResult = await RunBackupAsync(task).ConfigureAwait(false);
        if (backupResult is null)
            return null;
        if (!backupResult.Success)
        {
            // Копия не удалась — обновлять конфигурацию не начинаем.
            _logger?.Warn($"Задание «{task.Name}»: резервная копия не создана, обновление пропущено.");
            return backupResult;
        }

        return await RunUpdateConfigAsync(task).ConfigureAwait(false);
    }

    private async Task<BackupRunResult?> RunUpdateAppAsync(ScheduledTask task)
    {
        var result = new BackupRunResult();
        var release = await _gitHub.GetLatestReleaseAsync().ConfigureAwait(false);
        if (release is null)
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T("Update.CheckFailed");
            return result;
        }

        // Релиз получен, но он не новее установленной версии — программа актуальна (issue #8).
        // Это НЕ ошибка: задание завершается успешно, в LastRunMessage попадает текст
        // «версия актуальна», а уведомление подавляется, чтобы не спамить при повторе
        // проверки по расписанию.
        if (!GitHubReleaseService.IsNewerThan(release, VersionInfo.Display()))
        {
            result.Success = true;
            result.ScenarioName = LocalizationManager.T("Update.UpToDate");
            result.SuppressNotification = true;
            return result;
        }

        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T("Update.NoDownloadUrl");
            return result;
        }

        result.Success = true;
        result.ScenarioName = release.TagName ?? "";
        // Метод скачивает новый исполняемый файл, применяет обновление и перезапускает
        // приложение (в том числе без диалогов). Ошибки он обрабатывает сам.
        //
        // issue #10: при успешном применении (метод вернул true — помощник запущен,
        // маркер pending_update записан) отдельное уведомление «Задание выполнено»
        // подавляется: после перезапуска сама новая версия отправит единственное
        // сообщение «Приложение обновлено до версии X» (см. NotifyPendingUpdate).
        // Если обновление не применилось — подавление не включается, и уведомление
        // о завершении задания уходит как обычно.
        result.SuppressNotification = await _updateService.DownloadAndInstallAutoAsync(release).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Проверка наличия обновления технологической платформы 1С (issue #366): установленные
    /// версии по выражению проверки сравниваются с каталогом <c>releases.1c.ru</c>
    /// (все поддерживаемые каталоги Platform83/Platform85). Результат — сводка о доступных
    /// версиях (уведомление по выбранному каналу + LastRunMessage); ошибка авторизации
    /// ИТС или сети честно попадает в сообщение об ошибке.
    /// </summary>
    private async Task<BackupRunResult?> RunPlatformUpdateCheckAsync(ScheduledTask task)
    {
        var result = new BackupRunResult();
        var expression = task.PlatformCheckExpression?.Trim() ?? "";
        if (!PlatformUpdateCheckLogic.IsValidExpression(expression))
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T("Schedule.PlatformCheck.InvalidExpression");
            NotifyPlatformUpdateCheck(task, NotificationKind.Error, result.ErrorMessage);
            return result;
        }

        // Установленные версии: Display с суффиксом разрядности «(64)» → чистые номера.
        List<string> installed;
        try
        {
            installed = _platformVersions.FindInstalledVersions()
                .Select(display =>
                {
                    PlatformVersionService.ParseVariant(display, out var clean, out _);
                    return clean;
                })
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.Error("Задание проверки платформы: не удалось получить установленные версии", ex);
            installed = new List<string>();
        }

        var catalog = await _platformUpdate.GetAllAvailableReleasesAsync().ConfigureAwait(false);
        if (catalog.Status != PortalFetchStatus.Ok)
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T(
                string.IsNullOrEmpty(catalog.ErrorKey) ? "PlatformUpdate.Error.NetworkError" : catalog.ErrorKey);
            NotifyPlatformUpdateCheck(task, NotificationKind.Error, result.ErrorMessage);
            return result;
        }

        var check = PlatformUpdateCheckLogic.Check(
            installed, catalog.Releases, expression, task.PlatformCheckIncludeTestVersions);

        var summary = check.HasUpdate
            ? string.Format(
                LocalizationManager.T("Schedule.PlatformUpdateCheck.Found"),
                PlatformUpdateCheckLogic.FormatVersions(check.AvailableVersions))
            : check.MaxInstalledVersion is null
                ? LocalizationManager.T("Schedule.PlatformUpdateCheck.NoInstalledNone")
                : string.Format(
                    LocalizationManager.T("Schedule.PlatformUpdateCheck.UpToDate"),
                    check.MaxInstalledVersion);

        result.Success = true;
        result.ScenarioName = summary;
        // issue #366 (7OH от 2026-10-12): при отсутствии обновлений уведомление можно
        // отключить опцией «Уведомлять об отсутствии» — пустое окно/balloon не выводится,
        // но результат всё равно записывается в LastRunMessage.
        if (check.HasUpdate || task.PlatformCheckNotifyWhenNoUpdates)
        {
            NotifyPlatformUpdateCheck(
                task,
                check.HasUpdate ? NotificationKind.Info : NotificationKind.Success,
                summary);
        }
        return result;
    }

    /// <summary>
    /// Проверка наличия обновления конфигурации 1С (issue #367): максимальная
    /// установленная версия выбранной конфигурации по списку баз сравнивается с каталогом
    /// релизов <c>releases.1c.ru</c> — через тот же сервис, что использует окно «Проверка
    /// обновлений» (F9, <see cref="IOneCUpdatesService"/>). Результат — уведомление о
    /// версиях выше установленной по выбранному каналу + LastRunMessage; ошибки
    /// авторизации ИТС/сети честно попадают в сообщение об ошибке (паттерн issue #366).
    /// </summary>
    private async Task<BackupRunResult?> RunConfigurationUpdateCheckAsync(ScheduledTask task)
    {
        var result = new BackupRunResult();
        var configurationName = task.ConfigCheckConfiguration?.Trim() ?? "";
        if (configurationName.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T("Schedule.ConfigCheck.ConfigurationRequired");
            NotifyConfigurationUpdateCheck(task, NotificationKind.Error, result.ErrorMessage);
            return result;
        }

        List<Infobase> bases;
        try
        {
            bases = _repository.Load();
        }
        catch (Exception ex)
        {
            _logger?.Error("Задание проверки конфигурации: не удалось загрузить список баз", ex);
            bases = new List<Infobase>();
        }

        // Максимальная установленная версия по базам с выбранной конфигурацией
        // (чистая функция, покрыта юнит-тестами).
        var installed = ConfigurationUpdateCheckLogic.InstalledVersions(bases, configurationName);

        // Адрес каталога релизов строится как в окне F9: связка базы с типовой
        // конфигурацией (или её имя), редакция по версии и переопределения базы.
        var configTypes = _configTypes.LoadAll();
        var reference = ConfigurationUpdateCheckLogic.PickReferenceBase(bases, configurationName);
        var config = ConfigTypeMatcher.FindByCode(configTypes, reference?.UpdateConfigCode)
            ?? ConfigTypeMatcher.FindByInfobaseName(configTypes, configurationName);
        var edition = ConfigTypeMatcher.FindEditionByVersion(config, reference?.ConfigurationVersion)
            ?? config?.DefaultEdition;
        var url = _updates.BuildUpdateUrl(
            config, edition, reference?.UpdateUrlOverride, reference?.UpdateUrlSegment);
        if (string.IsNullOrWhiteSpace(url))
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T("Updates.NoUrl");
            NotifyConfigurationUpdateCheck(task, NotificationKind.Error, result.ErrorMessage);
            return result;
        }

        // Полный список версий: параметр allUpdates=true, как в окне F9 (issue #352).
        // Учётная запись ИТС, назначенная конфигурации (issue #13): раньше планировщик её не
        // передавал, и запрос всегда шёл под «Основной» записью.
        var catalog = await _updates.GetUpdateCatalogAsync(
            OneCUpdatesService.BuildAllUpdatesCatalogUrl(url), default,
            ConfigTypeMatcher.AccountIdFor(config)).ConfigureAwait(false);
        if (catalog.Status != PortalFetchStatus.Ok)
        {
            result.Success = false;
            result.ErrorMessage = LocalizationManager.T(
                string.IsNullOrEmpty(catalog.ErrorKey) ? "Updates.NetworkError" : catalog.ErrorKey);
            NotifyConfigurationUpdateCheck(task, NotificationKind.Error, result.ErrorMessage);
            return result;
        }

        var check = ConfigurationUpdateCheckLogic.Check(installed, catalog.Releases);

        // Текущей версии может не быть в каталоге (issue #367) — упоминаем в журнале.
        if (check.MaxInstalledVersion is not null && !check.InstalledVersionInCatalog)
        {
            _logger?.Warn(
                $"Задание «{task.Name}»: установленная версия {check.MaxInstalledVersion} " +
                $"конфигурации «{configurationName}» не найдена в каталоге релизов.");
        }

        var versions = ConfigurationUpdateCheckLogic.FormatVersions(check.AvailableVersions);
        var summary = check.HasUpdate
            ? check.MaxInstalledVersion is null
                ? string.Format(
                    LocalizationManager.T("Schedule.ConfigUpdateCheck.FoundNoInstalled"),
                    configurationName,
                    versions)
                : string.Format(
                    LocalizationManager.T("Schedule.ConfigUpdateCheck.Found"),
                    configurationName,
                    versions,
                    check.MaxInstalledVersion)
            : string.Format(
                LocalizationManager.T("Schedule.ConfigUpdateCheck.UpToDate"),
                configurationName,
                check.MaxInstalledVersion ?? string.Empty);

        result.Success = true;
        result.ScenarioName = summary;
        // issue #367 (7OH): при отсутствии доступных версий уведомление можно отключить
        // опцией «Уведомлять об отсутствии» — сводка остаётся в LastRunMessage.
        if (check.HasUpdate || task.ConfigCheckNotifyWhenNoUpdates)
        {
            NotifyConfigurationUpdateCheck(
                task,
                check.HasUpdate ? NotificationKind.Info : NotificationKind.Success,
                summary);
        }
        return result;
    }

    /// <summary>
    /// Уведомление о результате проверки платформы (issue #366) по каналу задания:
    /// «В главном окне» — строка состояния главного окна, «Системные уведомления» —
    /// диспетчер уведомлений (balloon трея / notify-send). Ошибки не влияют на планировщик.
    /// </summary>
    private void NotifyPlatformUpdateCheck(ScheduledTask task, NotificationKind kind, string message)
        => NotifyCheckResult(task, task.PlatformNotifyChannel, kind, message);

    /// <summary>
    /// Уведомление о результате проверки конфигурации (issue #367): тот же общий канал,
    /// что у проверки платформы (issue #366) — «В главном окне» / «Системные уведомления».
    /// </summary>
    private void NotifyConfigurationUpdateCheck(ScheduledTask task, NotificationKind kind, string message)
        => NotifyCheckResult(task, task.ConfigCheckNotifyChannel, kind, message);

    /// <summary>
    /// Общий показ результата заданий-проверок (issues #366/#367) по выбранному каналу:
    /// «В главном окне» — строка состояния главного окна через
    /// <see cref="IMainWindowNotifier"/>, «Системные уведомления» — диспетчер
    /// уведомлений (balloon трея Windows / notify-send Linux). Ошибки уведомления
    /// не влияют на планировщик.
    /// </summary>
    private void NotifyCheckResult(
        ScheduledTask task,
        PlatformUpdateNotifyChannel channel,
        NotificationKind kind,
        string message)
    {
        try
        {
            var text = $"{task.Name}: {message}";
            switch (channel)
            {
                case PlatformUpdateNotifyChannel.MainWindow:
                    _mainWindowNotifier.ShowMessage(text);
                    break;
                case PlatformUpdateNotifyChannel.SystemNotifications:
                    _notifications.Show(
                        LocalizationManager.T("App.Title"),
                        text,
                        kind,
                        NotificationEvent.ScheduledTask);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Не удалось уведомить о проверке обновления", ex);
        }
    }

    /// <summary>
    /// Задание «Синхронизация настроек» (п.9, 1.0.0.91): выполняется ядром
    /// <see cref="Workspace1C.Services.Sync.SettingsSyncRunner"/> по сохранённым настройкам
    /// окна (settings_sync.json). Результат — сводка изменённых файлов и ошибок (без секретов).
    /// </summary>
    private async Task<BackupRunResult?> RunSettingsSyncAsync(ScheduledTask task)
    {
        if (_settingsSync is null)
            return Fail(LocalizationManager.T("SettingsSync.NotConfigured"));

        var errors = Workspace1C.Services.Sync.SettingsSyncService.Validate(
            Workspace1C.Services.Sync.SettingsSyncStore.Load());
        if (errors.Count > 0)
            return Fail(LocalizationManager.T("SettingsSync.ValidationFailed"));

        var result = await _settingsSync.RunNowAsync().ConfigureAwait(false);
        return new BackupRunResult
        {
            Success = result.Success,
            ErrorMessage = result.Errors.Count == 0 ? null : string.Join("; ", result.Errors),
            ScenarioName = result.Message
        };
    }

    /// <summary>
    /// Задание «Развернуть тестовую копию» (п.10, 1.0.0.92): выполняет сценарий
    /// <see cref="TestDeployScenario"/> (test_deploy.json) через <see cref="TestDeployService"/>
    /// и применяет результат к списку баз — тестовая база добавляется/обновляется,
    /// группа «Тест» создаётся при необходимости. Например, «каждый понедельник
    /// обновлять тест из прода». Ошибки шагов возвращаются в <see cref="BackupRunResult"/>
    /// с понятным сообщением; секреты не логируются.
    /// </summary>
    private async Task<BackupRunResult?> RunTestDeployAsync(ScheduledTask task)
    {
        var scenario = TestDeployStore.Get(task.ScenarioId);
        if (scenario is null)
            return Fail(LocalizationManager.T("Schedule.ScenarioNotFound"));

        var source = ResolveInfobase(scenario.SourceInfobaseId);
        if (source is null)
            return Fail(LocalizationManager.T("TestDeploy.SourceNotFound"));

        List<Infobase> infobases;
        try
        {
            infobases = _repository.Load();
        }
        catch (Exception ex)
        {
            _logger?.Error("Задание тест-копии: не удалось загрузить список баз", ex);
            infobases = new List<Infobase>();
        }
        var groups = LoadSchedulerGroupsSafe();

        var service = _testDeploy ?? new TestDeployService(_logger);
        var result = await service.DeployAsync(scenario, source, infobases, groups,
                // issue #80: задания по расписанию — направление CM_JOBS.
                message => Log.Info(LogCategory.Jobs, $"Тестовая копия «{scenario.Name}»: {message}"))
            .ConfigureAwait(false);

        if (result.Success && result.Target is not null)
        {
            try
            {
                var (basesChanged, groupsChanged) = ApplyTestDeployToRepository(infobases, groups, scenario, result);
                if (basesChanged)
                    _repository.Save(infobases);
                if (groupsChanged)
                    _repository.SaveGroups(groups);
                // issue #80: задания по расписанию — направление CM_JOBS.
                if (result.GroupCreated)
                    Log.Info(LogCategory.Jobs, $"Задание тест-копии: создана группа «{result.GroupPath}».");
            }
            catch (Exception ex)
            {
                _logger?.Error("Задание тест-копии: не удалось сохранить список баз/групп", ex);
            }
        }

        // Фиксируем результат последнего запуска в сценарии (LastRunMessage без секретов).
        scenario.LastRunAt = DateTime.Now;
        scenario.LastRunSuccess = result.Success;
        scenario.LastRunMessage = result.Success
            ? string.Format(
                LocalizationManager.T(result.TargetCreated ? "TestDeploy.DoneCreated" : "TestDeploy.DoneUpdated"),
                result.Target?.Name ?? string.Empty)
            : result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed");
        try
        {
            TestDeployStore.Upsert(scenario);
        }
        catch (Exception ex)
        {
            _logger?.Error("Задание тест-копии: не удалось сохранить сценарий", ex);
        }

        if (!result.Success)
            return Fail(result.ErrorMessage ?? LocalizationManager.T("Schedule.RunFailed"));

        return new BackupRunResult
        {
            Success = true,
            ScenarioName = scenario.LastRunMessage
        };
    }

    /// <summary>Группы списка баз; при ошибке загрузки — пустой список.</summary>
    private List<Group> LoadSchedulerGroupsSafe()
    {
        try
        {
            return _repository.LoadGroups() ?? new List<Group>();
        }
        catch (Exception ex)
        {
            _logger?.Error("Задание тест-копии: не удалось загрузить группы", ex);
            return new List<Group>();
        }
    }

    /// <summary>
    /// Применяет результат развёртывания к списку баз и групп (задание по расписанию):
    /// новая тестовая запись добавляется (перед этим убирается запись-тёзка), существующая
    /// заменяется; новая группа «Тест» добавляется, если сценарий её создал. Возвращает
    /// признаки изменений для сохранения.
    /// </summary>
    private static (bool BasesChanged, bool GroupsChanged) ApplyTestDeployToRepository(
        List<Infobase> infobases,
        List<Group> groups,
        TestDeployScenario scenario,
        TestDeployResult result)
    {
        var basesChanged = false;
        if (result.Target is not null)
        {
            if (result.TargetCreated)
            {
                infobases.RemoveAll(b => TestDeployPlanner.MatchesTargetName(
                    result.Target.Name, b.Name, allowPrefixed: true));
                infobases.Add(result.Target);
                basesChanged = true;
            }
            else if (result.TargetUpdated)
            {
                var index = infobases.FindIndex(b =>
                    string.Equals(b.Id, result.Target.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    infobases[index] = result.Target;
                }
                else
                {
                    infobases.Add(result.Target);
                    basesChanged = true;
                }
            }
        }

        var groupsChanged = false;
        if (result.GroupCreated)
        {
            var plan = TestDeployPlanner.EnsureGroup(groups, scenario);
            if (plan.NewGroup is not null)
            {
                groups.Add(plan.NewGroup);
                groupsChanged = true;
            }
        }
        return (basesChanged, groupsChanged);
    }

    private Infobase? ResolveInfobase(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            return _repository.Load().FirstOrDefault(
                b => string.Equals(b.Id, id, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    private BackupScenario? ResolveScenario(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            return _scenarios.Get(id!);
        }
        catch
        {
            return null;
        }
    }

    private static BackupRunResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };

    /// <summary>
    /// Пересобирает кэш ближайших моментов запуска (issue #7): очищает <see cref="_nextRuns"/>
    /// и наполняет заново из <see cref="IScheduledTaskStore.LoadAll"/> через
    /// <see cref="ScheduleCalculator.ComputeNextRun"/>. Вызывается из окна «Задания по
    /// расписанию» после сохранения/удаления задания, чтобы планировщик учёл добавленные,
    /// изменённые и удалённые задания без перезапуска приложения. У выключенного задания
    /// ComputeNextRun вернёт null — такой ключ остаётся в кэше, и <see cref="ProcessDueAsync"/>
    /// пропускает задание, пока оно снова не будет включено. Вызов безопасен в любой момент:
    /// не влияет на таймер, а только заменяет содержимое кэша.
    /// </summary>
    public void Refresh()
    {
        _nextRuns.Clear();
        var now = DateTime.Now;
        foreach (var task in _tasks.LoadAll())
            _nextRuns[task.Id] = ScheduleCalculator.ComputeNextRun(task, now);
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}