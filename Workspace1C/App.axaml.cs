#if LINUX
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Avalonia-версия точки входа приложения (Linux). Заменяет WPF App.xaml / App.xaml.cs.
    /// Собирается только под #if LINUX; Windows использует WPF-версию App.
    /// </summary>
    public partial class App : Application
    {
        private static FileStream? _instanceLock;
        private static string? _dataDir;
        private static CancellationTokenSource? _activateCts;
        private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;

        /// <summary>
        /// Хронометр запуска (issue #153): по нему пишутся метки этапов старта Linux/Avalonia,
        /// чтобы на машине пользователя (VirtualBox/KDE NEON X11) было видно, до какого этапа
        /// дошёл запуск и где именно он останавливается или начинает «молотить» CPU.
        /// </summary>
        private static readonly System.Diagnostics.Stopwatch _startupWatch =
            System.Diagnostics.Stopwatch.StartNew();

        /// <summary>
        /// Работает ли режим единственного экземпляра: блокировка взята и сигнал
        /// от повторного запуска слушается. Значение относится к текущему
        /// процессу и после старта не меняется, даже если настройку переключат:
        /// блокировка берётся один раз, и снятый в окне настроек флажок не
        /// делает окно возвращаемым повторным запуском.
        /// </summary>
        internal static bool SingleInstanceActive { get; private set; }

        private const string LockFileName = "workspace-1c.lock";
        private const string ActivateFileName = "activate";

        /// <summary>
        /// Каталог данных приложения (например ~/.config/Workspace1C).
        /// Считается тем же механизмом, что и у репозитория настроек: расчёт
        /// напрямую через SpecialFolder.ApplicationData на Linux даёт пустую
        /// строку, когда каталога из XDG_CONFIG_HOME нет, путь становится
        /// относительным, и приложение молча не запускается.
        /// </summary>
        private static string DataDirectory =>
            _dataDir ??= Services.PlatformPaths.AppDataDirectory;

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Показываем любые необработанные ошибки — иначе окно просто не появляется.
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    ShowFatalError(TOr("App.Fatal.Critical", "Критическая ошибка"), ex);
            };

            // Необработанные исключения на UI-потоке (команды, построение и показ модальных
            // окон) Avalonia маршрутизирует через Dispatcher. Без обработчика они поднимаются
            // наверх и завершают процесс аварийно (Signal 6 / SIGABRT), как в issue #168:
            // при правке свойств базы и при открытии настроек приложение падало без отчёта.
            // Перехватываем, логируем в errors.log и не даём abort-у убить процесс. Окно,
            // которое строится в момент сбоя, не откроется, но приложение продолжит работу.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                ShowFatalError(TOr("App.Fatal.Interface", "Ошибка интерфейса"), args.Exception);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                ShowFatalError(TOr("App.Fatal.BackgroundTask", "Ошибка фоновой задачи"), args.Exception);
                args.SetObserved();
            };

            // Освобождаем файловый lock при завершении процесса и фиксируем факт выхода.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                // Диагностика issue #153: запись о штатном (managed) выходе с кодом возврата.
                // Если окно закрылось «само», а этой строки в логе нет — процесс завершился
                // нативным сбоем (SIGSEGV/SIGABRT в рендере/вводе) до какого-либо управляемого
                // обработчика, и причина не в логике приложения, а в окружении (vmwgfx/GL).
                try { LogProcessExit(); } catch { /* ignore */ }
                try { _activateCts?.Cancel(); _activateCts?.Dispose(); } catch { /* ignore */ }
                try { _instanceLock?.Dispose(); } catch { /* ignore */ }
                // Останавливаем планировщик заданий по расписанию (issue #286).
                try { AppServices.GetRequiredService<SchedulerService>().Stop(); } catch { /* ignore */ }
                try { BackgroundFeatures.Stop(); } catch { /* ignore */ }
            };

            try
            {
                // Загружаем настройки до показа окна, чтобы проверить запрет второго экземпляра.
                AppServices.Configure();

                // Конфиг отладочных флагов trace.json (issue #347 «Сказ о trace.json»):
                // создаётся при каждом старте рядом с настройками (при первом старте новой
                // версии старый JSONL-журнал мигрирует в trace_menuclose_legacy.json).
                // Ошибки игнорируются — диагностика не должна блокировать запуск.
                try { Services.TraceFlags.EnsureExists(); }
                catch { /* диагностика — вспомогательная возможность */ }

                // Исторический файл кэша форматов rac (issue #39): с 1.0.0.40 кэш рабочих
                // форматов — только в памяти, файл rac_joblist_formats.json больше не
                // создаётся; созданный прежними версиями удаляем при первом старте.
                // Ошибки глушатся — «идиот-файл» не должен мешать запуску.
                try { Services.RacJobListFormatCleaner.Clean(); }
                catch { /* чистка — вспомогательная возможность */ }

                // Логгер доступен только после настройки контейнера. Метки этапов запуска
                // (issue #153) пишутся в журнал приложения и в консоль, чтобы на виртуальной
                // машине пользователя (VirtualBox/KDE NEON X11) было видно, до какого этапа
                // старт дошёл и где именно он останавливается или начинает «молотить» CPU.
                var startupLogger = AppServices.GetRequiredService<Services.IAppLogger>();
                LogStartupStage(startupLogger, "Контейнер настроен");

                // Диагностика окружения рендеринга (issue #153): флаги непрозрачности/анимаций
                // и переменные сессии пишутся в файловый лог один раз при старте, чтобы на
                // машине пользователя (VirtualBox/KDE NEON X11) было видно, какой режим выбран
                // и почему окно «висит» или рисуется чёрным (связано с #177).
                try
                {
                    Services.LinuxRendering.LogStartupDiagnostics(startupLogger);
                    LogStartupStage(startupLogger, "Диагностика окружения рендеринга выполнена");
                }
                catch
                {
                    // Диагностика не должна блокировать запуск.
                    LogStartupStage(startupLogger, "Диагностика окружения рендеринга пропущена");
                }

                // Портативный режим (функция №1 StartManager, Этап 10): если приложение
                // запущено со сменного носителя (portable.dat рядом с exe или env-флаг),
                // при первом запуске переносим данные из системного каталога в каталог
                // рядом с приложением. Выполняется ДО инициализации профилей, чтобы
                // репозиторий сразу писал в портативный каталог.
                try { Services.PortablePaths.EnsurePortableData(); }
                catch { /* портативный режим — вспомогательная возможность */ }

                // Инициализируем учётные записи (профили): загружаем реестр, при первом
                // запуске мигрируем легаси-данные в профиль по умолчанию. Репозиторий
                // читает/пишет файлы данных в каталог активного профиля.
                var profileService = AppServices.GetRequiredService<IProfileService>();
                profileService.EnsureInitialized();
                LogStartupStage(startupLogger, "Профили инициализированы");

                // Любое окно, закрытое до создания главного, гасит приложение: режим
                // завершения по умолчанию OnLastWindowClose считает его последним,
                // и запуск падает с «Dispatcher shut down». Поэтому до показа
                // главного окна завершение только явное, а прежний режим
                // возвращается после. Условие здесь не на число профилей: на этом
                // отрезке модальным может оказаться и другое окно.
                var shutdownModeBeforeStartup = ShutdownMode.OnLastWindowClose;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime startupLifetime)
                {
                    shutdownModeBeforeStartup = startupLifetime.ShutdownMode;
                    startupLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }

                // Если в приложении несколько учётных записей — показываем окно авторизации
                // по аналогии со списком пользователей 1С. При одной записи входим без запроса.
                if (profileService.Profiles.Count > 1)
                {
                    LogStartupStage(startupLogger, "Окно авторизации: профилей более одного");

                    // Локализацию поднимаем до показа окна: настройки выбранного профиля
                    // читаются ниже, а без словаря окно входа показывает ключи
                    // (Auth.Title, Auth.Login) вместо подписей. Язык берётся из профиля,
                    // активного с прошлого запуска, и уточняется после выбора.
                    try
                    {
                        var startupSettings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
                        LocalizationManager.Instance.Initialize(startupSettings.Language, DataDirectory);
                    }
                    catch
                    {
                        LocalizationManager.Instance.Initialize(null, DataDirectory);
                    }

                    var (selectedId, passwordEntered) = LoginWindow.ShowLogin(profileService);
                    if (selectedId == null)
                    {
                        // Вход отменён — завершаем приложение.
                        Shutdown();
                        return;
                    }
                    profileService.SetCurrentProfile(selectedId);
                    // Вход с паролем автоматически разблокирует приватные базы (0.3.9.85).
                    if (passwordEntered)
                        profileService.MarkPrivateBasesUnlocked();
                }
                else
                {
                    LogStartupStage(startupLogger, "Окно авторизации не требуется");
                }

                ProfileBackupService.DataDirectoryResolver = () => profileService.CurrentProfileDataDirectory;

                // Конфиг отладочных флагов trace.json живёт в каталоге АКТИВНОГО ПРОФИЛЯ
                // (рядом с settings.json, issue #347): до выбора профиля EnsureExists выше
                // создавал его в корне каталога данных — переносим при загрузке настроек
                // профиля (если в профильном каталоге файла ещё нет).
                try { Services.TraceFlags.SetProfileDataDirectory(profileService.CurrentProfileDataDirectory); }
                catch { /* диагностика — вспомогательная возможность */ }

                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                AppSettings settings;
                try { settings = repository.LoadSettings(); }
                catch { settings = new AppSettings(); }
                LogStartupStage(startupLogger, "Настройки загружены");

                // Восстановление профиля из указанного каталога резервной копии
                // (например, после переустановки системы): настройки, список баз
                // (с пользователями и паролями запуска), группы и ibases.v8i.
                // Файлы копируются до загрузки данных главным окном, поэтому приложение
                // сразу открывается с привычным состоянием. Настройки перечитываются,
                // чтобы последующие этапы запуска использовали восстановленные значения.
                if (settings.ProfileRestoreOnStartup
                    && !string.IsNullOrWhiteSpace(settings.ProfileBackupDirectory)
                    && ProfileBackupService.HasBackup(settings.ProfileBackupDirectory))
                {
                    try
                    {
                        ProfileBackupService.Restore(settings.ProfileBackupDirectory, settings.IbasesSyncFilePath);
                        try { settings = repository.LoadSettings(); }
                        catch { /* оставляем уже прочитанные настройки */ }
                    }
                    catch (Exception ex)
                    {
                        // Сбой восстановления не должен блокировать запуск.
                        Console.Error.WriteLine("[profile] Ошибка восстановления профиля: " + ex.Message);
                    }
                }

                // Инициализируем локализацию: выбранный или системный язык, а также
                // загружаем внешние языки (.json) из папок Languages (рядом с приложением
                // и в каталоге данных).
                try
                {
                    LocalizationManager.Instance.Initialize(settings.Language, DataDirectory);
                    // Если словарь уже поднят ради окна входа, Initialize выходит сразу,
                    // поэтому язык выбранного профиля применяется отдельно и по тем же
                    // правилам: пустое значение означает язык системы.
                    LocalizationManager.Instance.ApplyPreferredLanguage(settings.Language);
                }
                catch
                {
                    // Локализация не должна блокировать запуск приложения.
                }
                LogStartupStage(startupLogger, "Локализация инициализирована");

                if (!settings.AllowMultipleInstances)
                {
                    if (!TryAcquireSingleInstanceLock())
                    {
                        // Уже запущен другой экземпляр — просим его показать окно и выходим.
                        SignalExistingInstance();
                        Shutdown();
                        return;
                    }
                    // Слушаем сигнал от повторных запусков, чтобы поднять окно.
                    StartActivationListener();
                    SingleInstanceActive = true;
                }
                LogStartupStage(startupLogger, "Проверка единственного экземпляра завершена");

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    _desktopLifetime = desktop;

                    // Применяем сохранённую полноценную тему (FullTheme с миграцией из
                    // устаревшего Theme) и накладываем поверх пользовательскую цветовую схему
                    // (две палитры). Старые раздельные схемы (активная + слоты светлой/тёмной)
                    // мигрируются в единую схему. Порядок важен: сначала встроенная схема темы,
                    // затем пользовательская — чтобы выбор пользователя не затирался.
                    var mergedScheme = Workspace1C.Models.ColorScheme.FromLegacy(
                        settings.ActiveColorScheme, settings.LightColorScheme, settings.DarkColorScheme);
                    var fullTheme = settings.NormalizeThemeSelection();
                    ThemeManager.SetOverrides(settings.IconSetOverride, settings.WindowButtonStyleOverride);
                    ThemeManager.ApplyFullTheme(fullTheme);
                    // Сохранённый базовый вариант (Theme="Dark") применяем поверх полноценной
                    // темы: тумблер «Светлая/Тёмная» не меняет FullTheme, и без этого выбора
                    // вариант терялся при перезапуске (в т.ч. после автообновления). Вариант
                    // задаётся до ApplyScheme — тот выбирает палитру по CurrentTheme.
                    if (ThemeFeatureMap.ResolveDarkVariant(settings.Theme) is bool darkVariant)
                        ThemeManager.ApplyTheme(darkVariant);
                    ThemeManager.ApplyScheme(mergedScheme);

                    // Компактный режим интерфейса (влияет на метрики отступов/иконок,
                    // должен быть установлен до построения главного окна).
                    UiMetrics.Compact = settings.CompactMode;
                    LogStartupStage(startupLogger, "Тема применена");

                    var mainWindow = AppServices.GetRequiredService<MainWindow>();
                    LogStartupStage(startupLogger, "Главное окно построено");

                    // «Дополнение заголовка» (issue #28): заголовок собирает само окно из
                    // актуальных настроек профиля — RefreshWindowTitle вызывает
                    // ComposeWindowTitle (идемпотентно) и обновляет Title с суффиксом из
                    // шаблона. Тот же путь, что и после «ОК» настроек; дублирования
                    // логики с настройками из App здесь больше нет (issue #34).
                    mainWindow.RefreshWindowTitle();

                    // Применяем сохранённые настройки шрифта интерфейса и отдельных областей
                    // (дерево групп, кнопки, поля ввода, правая панель, статус-бар).
                    ThemeManager.ApplyFont(mainWindow,
                        settings.FontFamily, settings.FontSize, settings.FontWeight, settings.FontStyle);

                    // Ресурс UiFontFamily (аудит UI 1.0.1.10, п. 1.9): все окна следуют
                    // ему через стиль в App.axaml, поэтому диалоги открываются с
                    // пользовательским шрифтом без литералов в разметке.
                    var uiFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
                        ? ThemeManager.DefaultFontFamily
                        : settings.FontFamily;
                    Application.Current.Resources["UiFontFamily"] = new Avalonia.Media.FontFamily(uiFamily);
                    // Меню, подсказки и выпадающие списки живут в отдельных корнях
                    // и шрифт окна не наследуют, поэтому им он ставится стилем.
                    ThemeManager.ApplyFontToPopups(
                        settings.FontFamily, settings.FontSize, settings.FontWeight, settings.FontStyle);
                    ThemeManager.ApplyElementFonts(mainWindow, settings.ElementFonts);

                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    LogStartupStage(startupLogger, "Главное окно показано");

                    // Уведомление об успешном обновлении приложения (issue #10): маркер
                    // «pending_update.json» пишется перед перезапуском для применения новой
                    // версии, а сообщение в Telegram/email отправляется ТОЛЬКО отсюда — из
                    // новой версии, когда интерфейс готов и каналы уведомлений настроены.
                    NotifyPendingUpdate();

                    // Фоновая проверка обновлений (Linux/Avalonia): запускаем после показа
                    // главного окна, чтобы не задерживать старт. Если пользователь отключил
                    // проверку в настройках — пропускаем. При обнаружении новой версии всегда
                    // показывается единый диалог с вопросом о применении обновления.
                    var updateService = AppServices.GetRequiredService<UpdateService>();
                    // Флаг ставится всегда, а не только вместе с фоновой проверкой: тот же
                    // экземпляр службы обслуживает кнопку «Проверить обновления» во вкладке
                    // «О программе». Пока присваивание стояло внутри условия, у выключивших
                    // проверку при запуске оставалось значение по умолчанию (true), и ручная
                    // проверка молча скачивала и ставила новую версию с перезапуском, минуя
                    // вопрос о согласии и вопрос о моменте перезапуска.
                    updateService.ApplyAutoUpdatePolicy(settings.AutoUpdateEnabled);

                    if (settings.CheckForUpdatesOnStartup)
                        CheckForUpdatesInBackground(updateService);

                    // Задания по расписанию (issue #286): запускаем планировщик после
                    // входа в профиль и показа главного окна. Он работает, пока приложение
                    // запущено; остановка выполняется при завершении приложения.
                    try
                    {
                        var scheduler = AppServices.GetRequiredService<SchedulerService>();
                        scheduler.Start();
                        // Фоновые функции 1.0.0.69 (мониторинг, Telegram-бот, общий список баз).
                        BackgroundFeatures.Start();
                        // Догоняющее выполнение пропущенных заданий (функция №7): задания,
                        // чьё плановое время наступило, пока приложение было выключено,
                        // выполняются сразу — один раз (если включено в настройках).
                        _ = scheduler.RunCatchUpAsync(settings.CatchUpMissedTasks);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[schedule] Ошибка запуска планировщика: " + ex.Message);
                    }

                    // Прежний режим завершения возвращается: на время старта он
                    // переключался на явный, иначе закрытие окна входа гасило
                    // приложение до появления главного.
                    desktop.ShutdownMode = shutdownModeBeforeStartup;
                    LogStartupStage(startupLogger, "Запуск завершён");
                }
            }
            catch (Exception ex)
            {
                // issue #213: при раннем сбое локализация может быть ещё не загружена,
                // тогда T вернёт сам ключ — подставляем встроенный читаемый текст.
                ShowFatalError(TOr("App.Fatal.StartupFailed", "Не удалось запустить приложение"), ex);
                Shutdown(1);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Возвращает перевод ключа, а если ключ не найден (словари ещё пусты из-за
        /// сбоя до инициализации локализации), — встроенный запасной текст. Так
        /// фатальное сообщение остаётся читаемым при любом состоянии приложения (issue #213).
        /// </summary>
        private static string TOr(string key, string fallback)
        {
            var text = LocalizationManager.T(key);
            return string.Equals(text, key, StringComparison.Ordinal) ? fallback : text;
        }

        /// <summary>
        /// Отправляет уведомление «Приложение обновлено до версии X» (issue #10),
        /// если при предыдущем запуске было выполнено обновление и новая версия
        /// реально запустилась. Маркер пишется перед перезапуском (в
        /// <see cref="UpdateService.DownloadAndInstallAutoAsync"/>), читается и
        /// удаляется здесь. Уведомление уходит по включённым каналам
        /// (Telegram/email/system) при событии <see cref="NotificationEvent.Update"/>,
        /// поэтому уважает фильтр «Уведомлять о новых версиях». Ошибки не роняют старт.
        /// </summary>
        private static async void NotifyPendingUpdate()
        {
            try
            {
                var store = AppServices.GetRequiredService<PendingUpdateStore>();
                var marker = store.ReadMarkerAndClear();
                if (marker is null || !PendingUpdateStore.ShouldNotify(marker.Version, VersionInfo.Display()))
                    return;

                // Описание изменений (issue #44): из маркера, а если его записала прежняя
                // версия приложения без описания — из GitHub Releases по тегу (с таймаутом,
                // при недоступности сети уведомление уходит без описания).
                var notes = marker.ReleaseNotes;
                var url = marker.ReleaseUrl;
                if (string.IsNullOrWhiteSpace(notes))
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var fetched = await AppServices.GetRequiredService<GitHubReleaseService>()
                            .GetReleaseNotesByTagAsync(marker.Version, cts.Token).ConfigureAwait(true);
                        if (fetched is { } f)
                        {
                            notes = f.Body;
                            if (string.IsNullOrWhiteSpace(url))
                                url = f.HtmlUrl;
                        }
                    }
                    catch
                    {
                        // Без описания — как раньше.
                    }
                }

                var notifications = AppServices.GetRequiredService<INotificationService>();
                notifications.Show(
                    LocalizationManager.T("App.Title"),
                    UpdateNotificationText.BuildInstalledMessage(marker.Version, notes, url),
                    NotificationKind.Success,
                    NotificationEvent.Update);
            }
            catch
            {
                // Уведомление не должно блокировать запуск приложения.
            }
        }

        /// <summary>
        /// Запускает фоновую проверку обновлений и не ждёт её завершения.
        /// Внутренние ошибки ловятся в <see cref="UpdateService"/>, здесь лишь
        /// дополнительно страхуемся, чтобы исключение не уронило поток.
        /// </summary>
        private static async void CheckForUpdatesInBackground(UpdateService updateService)
        {
            try
            {
                await updateService.CheckForUpdatesAsync().ConfigureAwait(false);
            }
            catch
            {
                // Фоновая проверка не должна влиять на запуск и работу приложения.
            }
        }

        /// <summary>
        /// Завершает приложение на этапе запуска. У Avalonia Application нет метода Shutdown,
        /// а desktop.Shutdown здесь не годится: оба вызова происходят внутри
        /// OnFrameworkInitializationCompleted, то есть до входа в цикл сообщений, и гасят
        /// Dispatcher раньше времени. Тогда MainLoop падает с InvalidOperationException
        /// «Cannot perform requested operation because the Dispatcher shut down».
        /// </summary>
        private static void Shutdown(int exitCode = 0) => Environment.Exit(exitCode);

        /// <summary>
        /// Пишет метку этапа запуска в журнал приложения и в консоль (issue #153).
        /// Диагностическая сборка Linux: по времени нарастающим итогом видно, до какого
        /// этапа дошёл старт и где он остановился или начал циклически «молотить» CPU.
        /// Безопасен на любом этапе — при недоступном логгере просто молча пропускается.
        /// </summary>
        private static void LogStartupStage(IAppLogger? logger, string stage)
        {
            try
            {
                var elapsed = _startupWatch.ElapsedMilliseconds;
                var message = $"[CM_MAIN_WINDOW][startup {elapsed} мс] {stage}";
                // issue #80 «Настройка логирования»: этапы запуска — направление
                // CM_MAIN_WINDOW; INFO-записи пишутся в журнал только при включённых
                // категории и общем выключателе CM_ON (по умолчанию всё выключено).
                // В консоль (CLI-режим, ранний старт до логгера) пишем всегда.
                if (logger is not null &&
                    Workspace1C.Services.TraceFlags.IsLogCategoryEnabled(Workspace1C.Services.LogCategory.MainWindow))
                    logger.Info(message);
                else if (logger is null)
                    Console.WriteLine(message);
            }
            catch { /* диагностика не должна блокировать запуск */ }
        }

        /// <summary>
        /// Захватывает исключительный файловый lock (один экземпляр на Linux).
        /// Файл-блокировка в каталоге данных с FileShare.None.
        /// </summary>
        private static bool TryAcquireSingleInstanceLock()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                _instanceLock = new FileStream(
                    Path.Combine(DataDirectory, LockFileName),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }                 // другой экземпляр держит lock
            catch (UnauthorizedAccessException) { return false; } // нет прав — считаем занятым
        }

        /// <summary>
        /// Второй экземпляр: создаёт файл-сигнал, чтобы первый показал главное окно.
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(
                    Path.Combine(DataDirectory, ActivateFileName),
                    DateTime.UtcNow.Ticks.ToString());
            }
            catch { /* ignore — повторный запуск просто завершится */ }
        }

        /// <summary>
        /// Основной экземпляр: следит за файлом-сигналом и активирует главное окно.
        /// </summary>
        private static void StartActivationListener()
        {
            try
            {
                _activateCts = new CancellationTokenSource();
                var token = _activateCts.Token;
                var activatePath = Path.Combine(DataDirectory, ActivateFileName);

                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (File.Exists(activatePath))
                            {
                                try { File.Delete(activatePath); } catch { /* ignore */ }
                                Dispatcher.UIThread.Post(ActivateMainWindow);
                            }
                        }
                        catch { /* ignore transient errors */ }
                        Thread.Sleep(300);
                    }
                }, token);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Показывает и активирует главное окно (в том числе если оно было свёрнуто).
        /// </summary>
        private static void ActivateMainWindow()
        {
            try
            {
                if (_desktopLifetime?.MainWindow is not Window win)
                    return;
                if (!win.IsVisible)
                    win.Show();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Записывает фатальную ошибку в errors.log и на stderr, а также в журнал
        /// приложения через логгер, если контейнер уже настроен.
        /// (Полноценный диалог ошибок появится вместе с портом окон — Этап 3.)
        /// </summary>
        private static void ShowFatalError(string title, Exception ex)
        {
            try
            {
                var text = $"{title}{Environment.NewLine}{ex}{Environment.NewLine}";
                Console.Error.WriteLine(text);
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(
                        Path.Combine(DataDirectory, "errors.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}");
                }
                catch { /* ignore */ }

                // Дублируем в штатный журнал приложения (app.log), если логгер уже доступен:
                // в нём фатальная ошибка видна рядом с обычными событиями и облегчает разбор.
                try
                {
                    var logger = AppServices.Services?.GetService<IAppLogger>();
                    logger?.Error($"{title}: {ex.Message}", ex);
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Фиксирует факт штатного (managed) завершения процесса с кодом возврата
        /// в консоль и errors.log. Диагностика issue #153: если окно закрылось «само»,
        /// а этой записи в логе нет, значит процесс упал нативно (SIGSEGV/SIGABRT)
        /// до какого-либо управляемого обработчика — причина не в логике приложения,
        /// а в рендере/вводе окружения (например, GL на vmwgfx).
        /// </summary>
        private static void LogProcessExit()
        {
            try
            {
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Процесс завершается (managed exit), код возврата {Environment.ExitCode}.";
                Console.WriteLine(text);
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), text + Environment.NewLine);
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }
    }
}
#endif