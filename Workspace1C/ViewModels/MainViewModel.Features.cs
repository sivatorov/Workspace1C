#if WINDOWS
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.EventLog;

namespace Workspace1C.ViewModels;

/// <summary>
/// Main ViewModel (Windows/WPF): команды новых функций выпуска 1.0.0.69
/// (журнал регистрации, расширения, технологический журнал, веб-публикация и др.).
/// Парный файл для Linux — <c>MainViewModel.Avalonia.Features.cs</c>.
/// </summary>
public partial class MainViewModel
{
    // ======================= Журнал регистрации (1.0.0.69) =======================

    private ICommand? _eventLogCommand;

    /// <summary>
    /// Команда «Журнал регистрации…»: журнал выбранной базы подставляется
    /// автоматически, базу можно сменить селектором вверху формы (issue #74).
    /// </summary>
    public ICommand EventLogCommand =>
        _eventLogCommand ??= new RelayCommand(_ => ExecuteEventLog());

    private void ExecuteEventLog()
    {
        var vm = new EventLogViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new EventLogWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Расширения конфигурации (1.0.0.69) =======================

    private ICommand? _extensionsCommand;

    /// <summary>Команда «Расширения конфигурации…»: пакетная установка .cfe, список, выгрузка, удаление.</summary>
    public ICommand ExtensionsCommand =>
        _extensionsCommand ??= new RelayCommand(_ => ExecuteExtensions());

    private void ExecuteExtensions()
    {
        var vm = new ExtensionsViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new ExtensionsWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Внешние отчёты и обработки (issue #91, 2.0) =======================

    private ICommand? _externalProcessingsCommand;

    /// <summary>Команда «Внешние отчёты и обработки…»: загрузка .epf/.erf по списку баз.</summary>
    public ICommand ExternalProcessingsCommand =>
        _externalProcessingsCommand ??= new RelayCommand(_ => ExecuteExternalProcessings());

    private void ExecuteExternalProcessings()
    {
        var preselected = SelectedInfobase is null ? Array.Empty<Infobase>() : new[] { SelectedInfobase };
        var vm = new ExternalProcessingsViewModel(Infobases.ToList(), preselected);
        var window = new ExternalProcessingsWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Пользователи информационных баз (1.0.0.83) =======================

    private ICommand? _infobaseUsersCommand;

    /// <summary>Команда «Пользователи информационных баз…»: список и пакетные операции через COM.</summary>
    public ICommand InfobaseUsersCommand =>
        _infobaseUsersCommand ??= new RelayCommand(_ => ExecuteInfobaseUsers());

    private void ExecuteInfobaseUsers()
    {
        var vm = new InfobaseUsersViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new InfobaseUsersWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Регламентные задания ИБ (1.0.0.85) =======================

    private ICommand? _scheduledJobsCommand;

    /// <summary>Команда «Регламентные задания ИБ…».</summary>
    public ICommand ScheduledJobsCommand =>
        _scheduledJobsCommand ??= new RelayCommand(_ => ExecuteScheduledJobs());

    private void ExecuteScheduledJobs()
    {
        var vm = new ScheduledJobsViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new ScheduledJobsWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    /// <summary>
    /// После дублирования базы предлагает выключить в копии все регламентные задания
    /// (1.0.0.85): копия не должна выполнять обмены, рассылки и загрузку данных.
    /// </summary>
    private async Task OfferDisableScheduledJobsAsync(Infobase clone)
    {
        if (clone is null || !_dialogs.Confirm(string.Format(LocalizationManager.T("Jobs.CloneAsk"), clone.Name),
                LocalizationManager.T("Jobs.Title")))
            return;
        var result = await ScheduledJobsService.SetAllAsync(clone, false);
        if (result.Success)
        {
            _logger.Info($"[clone] в копии «{clone.Name}» выключено регламентных заданий: {result.Changed}");
            _dialogs.ShowInfo(string.Format(LocalizationManager.T("Jobs.CloneDone"), clone.Name, result.Changed),
                LocalizationManager.T("Jobs.Title"));
        }
        else
        {
            _dialogs.ShowWarning(string.Format(LocalizationManager.T("Jobs.CloneFailed"), clone.Name, result.Error),
                LocalizationManager.T("Jobs.Title"));
        }
    }

    // ======================= Версионирование конфигурации в Git (1.0.0.69) =======================

    private ICommand? _gitVersioningCommand;

    /// <summary>Команда «Версионирование конфигурации в Git (1.0.0.69)».</summary>
    public ICommand GitVersioningCommand =>
        _gitVersioningCommand ??= new RelayCommand(_ => ExecuteGitVersioning());

    private void ExecuteGitVersioning()
    {
        var vm = new GitVersioningViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new GitVersioningWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Технологический журнал (1.0.0.69) =======================

    private ICommand? _techLogCommand;

    /// <summary>Команда «Технологический журнал (1.0.0.69)».</summary>
    public ICommand TechLogCommand =>
        _techLogCommand ??= new RelayCommand(_ => ExecuteTechLog());

    private void ExecuteTechLog()
    {
        var vm = new TechLogViewModel();
        var window = new TechLogWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Публикация на веб-сервере (1.0.0.69) =======================

    private ICommand? _webPublishCommand;

    /// <summary>Команда «Публикация на веб-сервере (1.0.0.69)».</summary>
    public ICommand WebPublishCommand =>
        _webPublishCommand ??= new RelayCommand(_ => ExecuteWebPublish());

    private void ExecuteWebPublish()
    {
        var vm = new WebPublishViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new WebPublishWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Мониторинг и оповещения (1.0.0.69) =======================

    private ICommand? _serverAlertsCommand;

    /// <summary>Команда «Мониторинг и оповещения (1.0.0.69)».</summary>
    public ICommand ServerAlertsCommand =>
        _serverAlertsCommand ??= new RelayCommand(_ => ExecuteServerAlerts());

    private void ExecuteServerAlerts()
    {
        var vm = new ServerAlertsViewModel(AppServices.GetRequiredService<IServerListStore>().Load(), Infobases.ToList(), ServerAlertsService.Load(), AppServices.TryGetService<ServerAlertsService>());
        var window = new ServerAlertsWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Резервные копии средствами СУБД (1.0.0.69) =======================

    private ICommand? _dbmsBackupCommand;

    /// <summary>Команда «Резервные копии средствами СУБД (1.0.0.69)».</summary>
    public ICommand DbmsBackupCommand =>
        _dbmsBackupCommand ??= new RelayCommand(_ => ExecuteDbmsBackup());

    private void ExecuteDbmsBackup()
    {
        var vm = new DbmsBackupViewModel(Infobases.ToList(), SelectedInfobase);
        var window = new DbmsBackupWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Команды Telegram-бота (1.0.0.69) =======================

    private ICommand? _telegramBotCommand;

    /// <summary>Команда «Команды Telegram-бота (1.0.0.69)».</summary>
    public ICommand TelegramBotCommand =>
        _telegramBotCommand ??= new RelayCommand(_ => ExecuteTelegramBot());

    private void ExecuteTelegramBot()
    {
        var vm = new TelegramBotViewModel(TelegramBotCommandService.Load(), !string.IsNullOrWhiteSpace(AppServices.GetRequiredService<INotificationSettingsStore>().Load().TelegramBotToken), AppServices.GetRequiredService<INotificationSettingsStore>().Load().TelegramChatId);
        var window = new TelegramBotWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ======================= Общие списки баз отдела (1.0.0.69) =======================

    private ICommand? _sharedListsCommand;

    /// <summary>Команда «Общие списки баз отдела (1.0.0.69)».</summary>
    public ICommand SharedListsCommand =>
        _sharedListsCommand ??= new RelayCommand(_ => ExecuteSharedLists());

    private void ExecuteSharedLists()
    {
        var vm = new SharedListsViewModel(SharedListService.Load(), () => SyncSharedListsAsync(true));
        var window = new SharedListsWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
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

    private System.Windows.Threading.DispatcherTimer? _sharedListTimer;

    /// <summary>Первичная синхронизация общих списков и таймер повторов (запуск главного окна).</summary>
    public void StartSharedListsAutoSync()
    {
        _ = SyncSharedListsAsync(false);
        if (_sharedListTimer is not null)
            return;
        _sharedListTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sharedListTimer.Tick += OnSharedListTimerTick;
        _sharedListTimer.Start();
    }

    private List<Infobase> AllInfobasesForShared() => Infobases.ToList();

    private List<Group> AllGroupsForShared() => Groups.ToList();

    private IDialogService SharedListsDialogs() => _dialogs;

    /// <summary>Применяет итог слияния к коллекциям (экземпляры баз сохраняются).</summary>
    private void ApplySharedLists(List<Infobase> infobases, List<Group> groups)
    {
        var keep = new HashSet<Infobase>(infobases, ReferenceEqualityComparer.Instance);
        foreach (var removed in Infobases.Where(b => !keep.Contains(b)).ToList())
            Infobases.Remove(removed);
        var present = new HashSet<Infobase>(Infobases, ReferenceEqualityComparer.Instance);
        foreach (var added in infobases.Where(b => !present.Contains(b)))
            Infobases.Add(added);
        var presentGroups = new HashSet<Group>(Groups, ReferenceEqualityComparer.Instance);
        foreach (var group in groups.Where(g => !presentGroups.Contains(g)))
            Groups.Add(group);
        if (SelectedInfobase is { } selected && !keep.Contains(selected))
            SelectedInfobase = null;
        InfobasesView.Refresh();
        Save();
        SaveGroups();
        RebuildGroupTree();
    }
}
#endif
