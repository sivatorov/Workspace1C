using Workspace1C.Services;
using Workspace1C.Services.EventLog;
using Workspace1C.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Workspace1C;

/// <summary>
/// Корневой контейнер зависимостей приложения.
/// </summary>
public static partial class AppServices
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static void Configure()
    {
        var services = new ServiceCollection();

        // Общие регистрации для обеих платформ (Windows/WPF и Linux/Avalonia):
        // реализации сервисов платформы 1С не зависят от UI-фреймворка.
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IInfobaseRepository, InfobaseRepository>();
        services.AddSingleton<IOneCLauncher, OneCLauncherService>();
        services.AddSingleton<IOneCComConnector, OneCComConnector>();
        services.AddSingleton<IPlatformVersionService, PlatformVersionServiceAdapter>();
        services.AddSingleton<IIbasesSyncService, IbasesSyncService>();
        // Пути удалённых пользователем пустых групп (issue #327): персистентный список
        // deleted_groups.json рядом с настройками, чтобы импорт из ibases.v8i не возвращал
        // удалённые пустые группы обратно. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IDeletedGroupPathsStore, DeletedGroupPathsStore>();
        services.AddSingleton<ICreateInfobaseService, CreateInfobaseService>();
        // Проверка обновлений конфигураций 1С по web-ресурсу обновлений (функции №21/№22).
        services.AddSingleton<IOneCUpdatesService, OneCUpdatesService>();
        // Автообновление платформы 1С (функция 9, цикл 0.3.9.208–0.3.9.216): каталог доступных
        // версий с портала, ленивая подгрузка файлов релиза и выбор дистрибутива под ОС.
        // Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IPlatformUpdateService, PlatformUpdateService>();
        // Пользовательские типовые конфигурации 1С (issue #321): отдельный читаемый JSON-файл
        // custom_config_types.json рядом с настройками; миграция из AppSettings.CustomConfigTypes.
        services.AddSingleton<ICustomConfigTypesStore, CustomConfigTypesStore>();
        // Учётные записи ИТС 1С (issue #333): отдельный читаемый JSON-файл its_accounts.json
        // рядом с настройками; миграция логина/пароля из AppSettings.UpdatesLogin/UpdatesPassword
        // в запись «Основная» при первом обращении. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IItsAccountsStore, ItsAccountsStore>();
        // Сценарии резервирования и восстановление (функции №16/№18): хранилище сценариев,
        // архивация ZIP/RAR и оркестратор выполнения. Чистые сервисы — без UI-зависимостей.
        services.AddSingleton<IBackupScenarioStore, BackupScenarioStore>();
        // Сценарии запуска скриптов (issue #308): JSON-хранилище и чистая подстановка
        // параметров. Чистые сервисы — без UI-зависимостей.
        services.AddSingleton<IScriptScenarioStore, ScriptScenarioStore>();
        // Пользовательские действия контекстного меню (функция 7, цикл 0.3.9.193–0.3.9.199):
        // единый читаемый JSON-файл custom_actions.json рядом с настройками. Чистый сервис —
        // без UI-зависимостей.
        services.AddSingleton<ICustomActionsStore, CustomActionsStore>();
        // Проверка резервных копий (функция 8, цикл 0.3.9.200–0.3.9.207): кэш результатов
        // проверки — единый читаемый JSON-файл backup_validation_cache.json рядом с настройками.
        // Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IBackupValidationCacheStore, BackupValidationCacheStore>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IBackupService, BackupService>();
        // Задания по расписанию (issue #286): хранилище заданий, обновление конфигурации ИБ
        // из .cf (/LoadCfg + /UpdateDBCfg) и фоновый планировщик. Чистые сервисы.
        services.AddSingleton<IScheduledTaskStore, ScheduledTaskStore>();
        services.AddSingleton<IConfigUpdateService, ConfigUpdateService>();
        services.AddSingleton<SchedulerService>();
        // Просмотр журнала регистрации ИБ (цикл 0.3.9.161–0.3.9.166): ридер
        // SQLite-формата журнала (.lgd). Ридер последовательного формата (.lgf/.lgp)
        // и фасад LgdReadSession добавляются следующими этапами; выбор ридера по
        // формату журнала будет выполнять LgdReadSession.
        services.AddSingleton<SqliteLgdReader>();
        // Блокировка сеансов файловой ИБ (функция №20): пакетный запуск конфигуратора
        // (/LockIB) без открытия «1С:Предприятия». Чистый сервис — без UI-зависимостей.
        services.AddSingleton<ISessionLockService, SessionLockService>();
        // Интеграция с проводником Windows (функция №12): ассоциация .1CD и команды
        // контекстного меню. На Windows/WPF — реализация на реестре HKCU; на Linux/Avalonia —
        // заглушка (IExplorerIntegrationService.IsAvailable == false). Тип один, реализация
        // выбирается символами условной компиляции (#if WINDOWS / #if LINUX).
        services.AddSingleton<IExplorerIntegrationService, ExplorerIntegrationService>();
        // Автозапуск при старте ОС (функция №31 StartManager): на Windows/WPF — ключ реестра
        // HKCU\...\Run; на Linux/Avalonia — автозапуск десктоп-окружения (~/.config/autostart).
        services.AddSingleton<IAutoStartService, AutoStartService>();
        // Сохранение копии экрана по хоткею (функция №30 StartManager): на Windows — захват
        // всего виртуального рабочего стола (CopyFromScreen); на Linux/Avalonia — снимок окна.
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        // Администрирование ИБ (Этап 6, функция №29 + консоль серверов): запуск chdbfl
        // для проверки целостности файловой ИБ и консоли администрирования серверов 1С
        // для клиент-серверных баз. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IInfobaseAdminService, InfobaseAdminService>();
        // Встроенный монитор серверов 1С (цикл 0.3.9.123–0.3.9.126): клиент rac
        // (Remote Administration Client) — чистый сервис без UI-зависимостей.
        services.AddSingleton<IRacClient, RacClient>();
        // Диагностика сети до сервера 1С (функция 12, цикл 0.3.9.229–0.3.9.233):
        // DNS-резолв, ICMP-пинг и TCP-проверка портов с RTT. Чистый сервис без
        // UI-зависимостей (низкоуровневые операции инжектируются делегатами).
        services.AddSingleton<INetworkDiagnosticsService, NetworkDiagnosticsService>();
        // Память портов серверов 1С для окна «Диагностика подключения» (issue #335):
        // server_ports.json рядом с настройками — порт на каждый сервис (кластер,
        // rac/монитор, хранилище) в разрезе сервера. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IServerPortsStore, ServerPortsStore>();
        // Справочник «Сервера управления» (issue #3): список серверов 1С для встроенного
        // монитора (rac) — server_list.json рядом с настройками (описание, адрес и порт,
        // логин и пароль). Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IServerListStore, ServerListStore>();
        // Управление службами (issue #11): список выбранных служб — services.json рядом
        // с настройками (имена служб), по образцу ServerListStore. Чистый сервис — без
        // UI-зависимостей; сам контроллер служб — один тип, реализация выбирается
        // символами условной компиляции (#if WINDOWS / #if LINUX).
        services.AddSingleton<IServicesStore, ServicesStore>();
        services.AddSingleton<IServiceControllerService, ServiceControllerService>();
        // Кэш статусов служб (issue #3, часть 2): единый экземпляр для трея, главного
        // окна и меню «СЛУЖБЫ» монитора серверов — мгновенное чтение статусов без
        // блокирующего опроса контроллера на UI-потоке. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<ServiceStatusCache>();
        // Компоновка меню «Утилиты» (issue #374): пользовательская структура меню —
        // utilities_menu_layout.json рядом с настройками, по образцу CustomActionsStore.
        // Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IUtilitiesMenuLayoutStore, UtilitiesMenuLayoutStore>();
        // Компоновка меню трея (issue #62): пользовательская структура меню трея —
        // tray_menu_layout.json рядом с настройками, по образцу UtilitiesMenuLayoutStore.
        // Чистый сервис — без UI-зависимостей.
        services.AddSingleton<ITrayMenuLayoutStore, TrayMenuLayoutStore>();
        // Компоновка контекстного меню базы (issue #56): пользовательская структура
        // контекстного меню — context_menu_layout.json рядом с настройками, по образцу
        // UtilitiesMenuLayoutStore. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IContextMenuLayoutStore, ContextMenuLayoutStore>();
        // Обозреватель хранилища конфигурации (цикл 0.3.9.127–0.3.9.130): пакетные операции
        // хранилища через DESIGNER (выгрузка версии .cf, отчёт по истории, захват/отмена
        // захвата) + состав версии. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IRepositoryStorageService, RepositoryStorageService>();
        // Обозреватель метаданных конфигурации (цикл 0.3.9.132–0.3.9.136): выгрузка
        // /DumpConfigToFiles из базы или .cf (переиспользование инфраструктуры сравнения)
        // без удаления каталога до закрытия окна. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IMetadataExplorerService, MetadataExplorerService>();
        // Индикатор «база сейчас запущена» и инспектор процессов: список процессов 1С
        // с командными строками и подробностями (PID, время старта, владелец).
        // Windows — WMI (Win32_Process), Linux — обход /proc; тип один, реализация
        // выбирается символами условной компиляции.
        services.AddSingleton<IRunningInfobasesService, RunningInfobasesService>();
        // Уведомления (функции №4/№5): системный канал ОС — balloon-tip трея на
        // Windows/WPF, notify-send на Linux/Avalonia (тип один, реализация выбирается
        // символами условной компиляции #if WINDOWS / #if LINUX); внешние каналы
        // Telegram/email добавляются этапами 0.3.9.183–0.3.9.184. Диспетчер
        // NotificationDispatcher рассылает сообщение всем включённым каналам.
        services.AddSingleton<INotificationChannel, SystemNotificationChannel>();
        // Внешние каналы (выпуск 0.3.12.69): Telegram-бот и email (SMTP). Настройки —
        // отдельный файл notifications.json профиля (NotificationSettingsStore).
        services.AddSingleton<INotificationSettingsStore>(sp => new NotificationSettingsStore(
            sp.GetService<IProfileService>(), null, sp.GetService<IAppLogger>()));
        services.AddSingleton<TelegramNotificationChannel>(sp => new TelegramNotificationChannel(
            sp.GetRequiredService<INotificationSettingsStore>(), sp.GetService<IAppLogger>()));
        services.AddSingleton<EmailNotificationChannel>(sp => new EmailNotificationChannel(
            sp.GetRequiredService<INotificationSettingsStore>(), sp.GetService<IAppLogger>()));
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<TelegramNotificationChannel>());
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<EmailNotificationChannel>());
        services.AddSingleton<INotificationService, NotificationDispatcher>();
        // п.5 «Мониторинг лицензий» (1.0.0.87): установленные программные лицензии
        // (ring license list / licensetool) и оповещения о нехватке лицензий.
        services.AddSingleton<IRingLicenseService>(sp => new RingLicenseService(sp.GetService<IAppLogger>()));
        services.AddSingleton<LicenseManager>(sp => new LicenseManager(
            sp.GetRequiredService<INotificationService>(), sp.GetService<IAppLogger>()));
        // Новые фоновые функции 1.0.0.69: оповещения мониторинга серверов/журналов
        // регистрации и команды Telegram-бота (запускаются BackgroundFeatures.Start).
        services.AddSingleton<ServerAlertsService>(sp => new ServerAlertsService(
            sp.GetRequiredService<IRacClient>(), sp.GetRequiredService<IServerListStore>(),
            sp.GetRequiredService<INotificationService>(), sp.GetService<IInfobaseRepository>(),
            sp.GetService<IAppLogger>(),
            sp.GetService<IRingLicenseService>(), sp.GetService<INotificationSettingsStore>(),
            sp.GetService<SizeHistoryStore>()));
        // п.7 «История размеров баз и прогноз диска» (1.0.0.89): дневные снимки
        // размеров в каталоге данных профиля (size_history.json).
        services.AddSingleton<SizeHistoryStore>(_ => new SizeHistoryStore());
        // п.8 «Заметки к базе» (1.0.0.90): markdown-карточки баз и зашифрованные
        // секреты внешних сервисов (infobase_notes.json в каталоге данных профиля).
        services.AddSingleton<NoteStore>(_ => new NoteStore());
        services.AddSingleton<NoteCrypto>(_ => NoteCrypto.CreateDefault());
        // п.9 «Синхронизация своих настроек между компьютерами» (1.0.0.91): ядро
        // (WebDAV GET/HEAD/PUT или сетевая папка, last-write-wins, конфликт-бэкапы)
        // и фоновый исполнитель по интервалу + задание планировщика.
        services.AddSingleton<Workspace1C.Services.Sync.SettingsSyncService>(sp =>
            new Workspace1C.Services.Sync.SettingsSyncService(sp.GetService<IAppLogger>()));
        services.AddSingleton<Workspace1C.Services.Sync.SettingsSyncRunner>(
            sp => new Workspace1C.Services.Sync.SettingsSyncRunner(
                sp.GetRequiredService<Workspace1C.Services.Sync.SettingsSyncService>(),
                sp.GetRequiredService<IScheduledTaskStore>(),
                sp.GetService<IAppLogger>()));
        // п.10 «Тестовый контур одной кнопкой» (1.0.0.92): оркестратор развёртывания
        // тестовой копии (выгрузка .dt → создание/обновление базы → загрузка копии →
        // регламенты → признаки отличия → группа «Тест»). Чистый сервис.
        services.AddSingleton<TestDeployService>(sp => new TestDeployService(sp.GetService<IAppLogger>()));
        services.AddSingleton<TelegramBotCommandService>(sp => new TelegramBotCommandService(
            sp.GetRequiredService<INotificationSettingsStore>(), sp.GetRequiredService<IScheduledTaskStore>(),
            sp.GetRequiredService<SchedulerService>(), sp.GetRequiredService<ServerAlertsService>(),
            sp.GetService<IAppLogger>()));
        // Интерактивные подтверждения Telegram (выпуск 1.0.0.36+): HTTP-клиент Bot API
        // с long polling (TelegramBotApiClient) и реестр обработанных update_id
        // (telegram.offset в каталоге профиля). Используются headless CLI-командами
        // --telegram-send / --telegram-confirm (рабочий процесс разработки, агент Roo Code).
        services.AddSingleton<TelegramBotApiClient>(sp => new TelegramBotApiClient(sp.GetService<IAppLogger>()));
        services.AddSingleton<TelegramOffsetStore>(sp => new TelegramOffsetStore(
            sp.GetService<IProfileService>(), null, sp.GetService<IAppLogger>()));
        services.AddSingleton<TelegramConfirmationService>(sp => new TelegramConfirmationService(
            sp.GetRequiredService<INotificationSettingsStore>(),
            sp.GetRequiredService<TelegramBotApiClient>(),
            sp.GetRequiredService<TelegramOffsetStore>(),
            null,
            sp.GetService<IAppLogger>()));
        // Маркер «запланировано уведомление об успешном обновлении» (issue #10):
        // pending_update.json в каталоге данных профиля. Пишется перед перезапуском
        // приложения для применения обновления, читается и очищается при следующем
        // старте новой версии. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<PendingUpdateStore>();
        // Сообщение в строке состояния главного окна из фоновых сервисов (issue #366):
        // канал уведомления задания «Проверить наличие обновления платформы» —
        // «В главном окне». Платформенная реализация (WPF/Avalonia) выбирается
        // символами условной компиляции.
        services.AddSingleton<IMainWindowNotifier, MainWindowNotifier>();
        // Завершение процесса 1С по PID (инспектор процессов): Windows — Process.Kill
        // вместе с деревом потомков, Linux — kill через /proc со сверкой времени старта.
        services.AddSingleton<IOneCProcessKiller, OneCProcessKiller>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

#if WINDOWS
        // Windows — приоритетная платформа (WPF). Здесь дополнительно регистрируются
        // WPF-диалоги, регистратор COM-коннектора и Windows-only модели представления окон.
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IOneCComConnectorRegistrar, OneCComConnectorRegistrar>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();
        // Проверка обновлений из GitHub Releases (Windows-only, автообновление).
        services.AddSingleton<GitHubReleaseService>();
        // Подсистема автообновления: фоновая проверка и диалог «Доступна новая версия».
        services.AddSingleton<UpdateService>();
        // Интерфейс автообновления для планировщика заданий (issue #8): тот же единственный
        // синглтон UpdateService, чтобы флаг AutoUpdateEnabled и события не дублировались.
        services.AddSingleton<IAppUpdateService>(sp => sp.GetRequiredService<UpdateService>());
#else
        // Linux (Avalonia): диалоги Avalonia. Регистратор COM-коннектора не подключается —
        // на Linux COM отсутствует (чтение конфигурации выполняется без COM: 1Cv8.1CD / DESIGNER).
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        // Проверка обновлений из GitHub Releases + подсистема автообновления (Linux/Avalonia).
        services.AddSingleton<GitHubReleaseService>();
        services.AddSingleton<UpdateService>();
        // Интерфейс автообновления для планировщика заданий (issue #8): см. комментарий выше.
        services.AddSingleton<IAppUpdateService>(sp => sp.GetRequiredService<UpdateService>());
#endif

        // Модули редакции ПРО (docs/pro/PRO-EDITION.md): реализация есть только в сборке
        // с приватным репозиторием Workspace1C.Pro; без неё вызов удаляется компилятором.
        RegisterProModules(services);

        Services = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>
    /// Служба, если контейнер уже настроен и она зарегистрирована, иначе null.
    /// Для вспомогательных фоновых задач (мониторы), которые обязаны тихо
    /// пропускать работу до инициализации контейнера.
    /// </summary>
    public static T? TryGetService<T>() where T : notnull
    {
        try
        {
            return Services is null ? default : Services.GetService<T>();
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Регистрация сервисов модулей ПРО. Реализуется в <c>ProModules.cs</c> приватного
    /// репозитория Workspace1C.Pro (partial-метод без реализации компилятор удаляет).
    /// </summary>
    static partial void RegisterProModules(IServiceCollection services);
}
