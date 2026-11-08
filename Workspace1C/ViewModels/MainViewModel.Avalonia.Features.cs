#if LINUX
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.EventLog;

namespace Workspace1C.ViewModels;

/// <summary>
/// Main ViewModel (Avalonia/Linux): команды новых функций выпуска 1.0.0.69
/// (журнал регистрации, расширения, технологический журнал, веб-публикация и др.).
/// Парный файл для Windows — <c>MainViewModel.Features.cs</c>.
/// </summary>
public partial class MainViewModel
{
    // ======================= Журнал регистрации (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _eventLogCommand;

    /// <summary>
    /// Команда «Журнал регистрации…»: журнал выбранной базы подставляется
    /// автоматически, базу можно сменить селектором вверху формы (issue #74).
    /// </summary>
    public System.Windows.Input.ICommand EventLogCommand =>
        _eventLogCommand ??= new RelayCommand(_ => ExecuteEventLog());

    private void ExecuteEventLog()
    {
        var vm = new EventLogViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.EventLogWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Расширения конфигурации (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _extensionsCommand;

    /// <summary>Команда «Расширения конфигурации…»: пакетная установка .cfe, список, выгрузка, удаление.</summary>
    public System.Windows.Input.ICommand ExtensionsCommand =>
        _extensionsCommand ??= new RelayCommand(_ => ExecuteExtensions());

    private void ExecuteExtensions()
    {
        var vm = new ExtensionsViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.ExtensionsWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Пользователи информационных баз (1.0.0.83) =======================

    private System.Windows.Input.ICommand? _infobaseUsersCommand;

    /// <summary>Команда «Пользователи информационных баз…» (на Linux операции недоступны — нет COM).</summary>
    public System.Windows.Input.ICommand InfobaseUsersCommand =>
        _infobaseUsersCommand ??= new RelayCommand(_ => ExecuteInfobaseUsers());

    private void ExecuteInfobaseUsers()
    {
        var vm = new InfobaseUsersViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.InfobaseUsersWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Регламентные задания ИБ (1.0.0.85) =======================

    private System.Windows.Input.ICommand? _scheduledJobsCommand;

    /// <summary>Команда «Регламентные задания ИБ…» (на Linux операции недоступны — нет COM).</summary>
    public System.Windows.Input.ICommand ScheduledJobsCommand =>
        _scheduledJobsCommand ??= new RelayCommand(_ => ExecuteScheduledJobs());

    private void ExecuteScheduledJobs()
    {
        var vm = new ScheduledJobsViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.ScheduledJobsWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Версионирование конфигурации в Git (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _gitVersioningCommand;

    /// <summary>Команда «Версионирование конфигурации в Git (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand GitVersioningCommand =>
        _gitVersioningCommand ??= new RelayCommand(_ => ExecuteGitVersioning());

    private void ExecuteGitVersioning()
    {
        var vm = new GitVersioningViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.GitVersioningWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Технологический журнал (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _techLogCommand;

    /// <summary>Команда «Технологический журнал (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand TechLogCommand =>
        _techLogCommand ??= new RelayCommand(_ => ExecuteTechLog());

    private void ExecuteTechLog()
    {
        var vm = new TechLogViewModel();
        var window = new Workspace1C.TechLogWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Публикация на веб-сервере (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _webPublishCommand;

    /// <summary>Команда «Публикация на веб-сервере (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand WebPublishCommand =>
        _webPublishCommand ??= new RelayCommand(_ => ExecuteWebPublish());

    private void ExecuteWebPublish()
    {
        var vm = new WebPublishViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.WebPublishWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Мониторинг и оповещения (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _serverAlertsCommand;

    /// <summary>Команда «Мониторинг и оповещения (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand ServerAlertsCommand =>
        _serverAlertsCommand ??= new RelayCommand(_ => ExecuteServerAlerts());

    private void ExecuteServerAlerts()
    {
        // Функция редакции ПРО (docs/pro/PRO-EDITION.md): без лицензии — окно «Лицензия».
        if (!Workspace1C.Services.Licensing.ProGate.Ensure(Workspace1C.Services.Licensing.ProFeature.ServerAlerts))
            return;
        var vm = new ServerAlertsViewModel(AppServices.GetRequiredService<IServerListStore>().Load(), _allInfobases.ToList(), ServerAlertsService.Load(), AppServices.TryGetService<ServerAlertsService>());
        var window = new Workspace1C.ServerAlertsWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Резервные копии средствами СУБД (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _dbmsBackupCommand;

    /// <summary>Команда «Резервные копии средствами СУБД (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand DbmsBackupCommand =>
        _dbmsBackupCommand ??= new RelayCommand(_ => ExecuteDbmsBackup());

    private void ExecuteDbmsBackup()
    {
        var vm = new DbmsBackupViewModel(_allInfobases.ToList(), SelectedInfobase);
        var window = new Workspace1C.DbmsBackupWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Команды Telegram-бота (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _telegramBotCommand;

    /// <summary>Команда «Команды Telegram-бота (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand TelegramBotCommand =>
        _telegramBotCommand ??= new RelayCommand(_ => ExecuteTelegramBot());

    private void ExecuteTelegramBot()
    {
        // Функция редакции ПРО (docs/pro/PRO-EDITION.md): без лицензии — окно «Лицензия».
        if (!Workspace1C.Services.Licensing.ProGate.Ensure(Workspace1C.Services.Licensing.ProFeature.TelegramBot))
            return;
        var vm = new TelegramBotViewModel(TelegramBotCommandService.Load(), !string.IsNullOrWhiteSpace(AppServices.GetRequiredService<INotificationSettingsStore>().Load().TelegramBotToken), AppServices.GetRequiredService<INotificationSettingsStore>().Load().TelegramChatId);
        var window = new Workspace1C.TelegramBotWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Общие списки баз отдела (1.0.0.69) =======================

    private System.Windows.Input.ICommand? _sharedListsCommand;

    /// <summary>Команда «Общие списки баз отдела (1.0.0.69)».</summary>
    public System.Windows.Input.ICommand SharedListsCommand =>
        _sharedListsCommand ??= new RelayCommand(_ => ExecuteSharedLists());

    private void ExecuteSharedLists()
    {
        var vm = new SharedListsViewModel(SharedListService.Load(), () => SyncSharedListsAsync(true));
        var window = new Workspace1C.SharedListsWindow(vm);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Синхронизация общих списков (1.0.0.69) =======================

    private DateTime _sharedListNextRun = DateTime.MinValue;
    private bool _sharedListSyncing;

    private void OnSharedListTimerTick(object? sender, EventArgs e)
    {
        var settings = SharedListService.Load();
        if (settings.IntervalMinutes <= 0 || settings.Sources.Count == 0 || DateTime.Now < _sharedListNextRun)
            return;
        _ = SyncSharedListsAsync(false);
    }

    /// <summary>
    /// Синхронизирует общие списки баз. Возвращает текст итога (для окна настроек);
    /// при <paramref name="interactive"/> ошибки показываются пользователю.
    /// </summary>
    public async Task<string> SyncSharedListsAsync(bool interactive)
    {
        if (_sharedListSyncing)
            return LocalizationManager.T("Shared.Syncing");
        _sharedListSyncing = true;
        try
        {
            var settings = SharedListService.Load();
            _sharedListNextRun = DateTime.Now.AddMinutes(Math.Max(1, settings.IntervalMinutes));
            if (settings.Sources.Count(s => s.Enabled) == 0)
                return LocalizationManager.T("Shared.NoSources");

            var (infobases, groups, result, errors) =
                await SharedListService.SyncAllAsync(AllInfobasesForShared(), AllGroupsForShared(), settings);
            if (result.Changed)
                ApplySharedLists(infobases, groups);

            var summary = string.Format(LocalizationManager.T("Shared.Result"),
                result.Added, result.Updated, result.Removed, result.Skipped);
            if (errors.Count > 0)
                summary += "\n" + string.Join("\n", errors);
            _logger.Info("Общие списки баз: " + summary.Replace("\n", "; "));
            if (interactive && errors.Count > 0)
                SharedListsDialogs().ShowWarning(string.Join("\n", errors), LocalizationManager.T("Shared.Title"));
            return summary;
        }
        catch (Exception ex)
        {
            _logger.Warn("Ошибка синхронизации общих списков баз: " + ex.Message);
            return ex.Message;
        }
        finally
        {
            _sharedListSyncing = false;
        }
    }

    private Avalonia.Threading.DispatcherTimer? _sharedListTimer;

    /// <summary>Первичная синхронизация общих списков и таймер повторов (запуск главного окна).</summary>
    public void StartSharedListsAutoSync()
    {
        _ = SyncSharedListsAsync(false);
        if (_sharedListTimer is not null)
            return;
        _sharedListTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sharedListTimer.Tick += OnSharedListTimerTick;
        _sharedListTimer.Start();
    }

    private List<Infobase> AllInfobasesForShared() => _allInfobases.ToList();

    private List<Group> AllGroupsForShared() => _groups.ToList();

    private IDialogService SharedListsDialogs() => _dialog;

    /// <summary>Применяет итог слияния к спискам, сохраняет и перестраивает дерево.</summary>
    private void ApplySharedLists(List<Infobase> infobases, List<Group> groups)
    {
        _allInfobases.Clear();
        _allInfobases.AddRange(infobases);
        SyncFavoriteHotkeys();
        _groups.Clear();
        _groups.AddRange(groups);
        SaveSilently();
        SaveGroupsSilently();
        RebuildTree();
        if (SelectedInfobase is { } selected && !_allInfobases.Contains(selected))
            SelectedInfobase = null;
    }
}
#endif
