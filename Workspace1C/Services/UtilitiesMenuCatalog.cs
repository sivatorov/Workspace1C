using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Вид команды пункта меню «Утилиты»: каталог хранит только метаданные пункта,
/// а фактическую команду/действие резолвит построитель меню платформы (WPF/Avalonia),
/// у которого есть доступ к <c>MainViewModel</c> и обработчикам окна.
/// </summary>
public enum UtilitiesMenuCommandKind
{
    /// <summary>Командная палитра (Ctrl+K).</summary>
    CommandPalette,

    /// <summary>Окно «Лицензия» редакции ПРО (2.0, docs/pro/PRO-EDITION.md).</summary>
    ShowLicense,

    /// <summary>Пункт модуля ПРО: действие зарегистрировано через <see cref="UtilitiesMenuCatalog.RegisterExternal"/>.</summary>
    External,

    /// <summary>Окно «Актуальные релизы».</summary>
    ShowActualReleases,

    /// <summary>Окно «Обновление платформы 1С».</summary>
    ShowPlatformUpdate,

    /// <summary>Окно «Скачивание платформы 1С».</summary>
    ShowPlatformDownload,

    /// <summary>Встроенный монитор серверов 1С.</summary>
    ServerMonitor,

    /// <summary>Управление службами (Windows/Linux, issue #11).</summary>
    Services,

    /// <summary>Внешняя консоль администрирования серверов.</summary>
    OpenServerConsole,

    /// <summary>Регистрация компонентов внешней консоли (radmin.dll, comctrl.dll), issue #135.</summary>
    RegisterServerConsoleComponents,

    /// <summary>Временная блокировка приложения паролем.</summary>
    LockApp,

    /// <summary>Разблокировка приватных баз.</summary>
    UnlockPrivateBases,

    /// <summary>Смена пароля блокировки приложения.</summary>
    ChangeAppLock,

    /// <summary>Выполнить сценарий резервирования.</summary>
    RunBackupScenario,

    /// <summary>Центр обслуживания.</summary>
    MaintenanceCenter,

    /// <summary>Список типовых конфигураций (правка).</summary>
    OpenConfigTypesEdit,

    /// <summary>Справочник учётных записей ИТС.</summary>
    OpenItsAccounts,

    /// <summary>Уведомления в Telegram и по email (выпуск 0.3.12.69).</summary>
    OpenNotificationChannels,

    /// <summary>Задания по расписанию.</summary>
    OpenScheduledTasks,

    /// <summary>Сценарии резервирования.</summary>
    ShowBackupScenarios,

    /// <summary>Настройка сценариев запуска скриптов.</summary>
    ShowScriptsSettings,

    /// <summary>Статистика использования баз.</summary>
    UsageStatistics,

    /// <summary>Сравнение конфигураций.</summary>
    ConfigDiff,

    /// <summary>Обозреватель метаданных.</summary>
    MetadataExplorer,

    /// <summary>Пакетное обновление конфигураций из хранилищ.</summary>
    RepositoryBatchUpdate,

    /// <summary>Массовая выгрузка списка баз в файлы .dt (issue #91).</summary>
    BatchDumpDt,

    /// <summary>Массовая загрузка конфигурации из .cf по списку баз (issue #91).</summary>
    BatchLoadCf,

    /// <summary>Обозреватель хранилища конфигурации.</summary>
    RepositoryBrowser,

    /// <summary>Экспорт списка баз в CSV.</summary>
    ExportBasesCsv,

    /// <summary>Экспорт отчёта по базам в HTML.</summary>
    ExportBasesHtml,

    /// <summary>Экспорт списка баз в JSON.</summary>
    ExportBasesJson,

    /// <summary>Импорт списка баз из JSON.</summary>
    ImportBasesJson,
 
    /// <summary>Экспорт списка баз в произвольный файл *.v8i (issue #29).</summary>
    ExportBasesV8iFile,
 
    /// <summary>Импорт списка баз из произвольного файла *.v8i (issue #29).</summary>
    ImportBasesV8iFile,
 
    /// <summary>Импорт баз из кластера сервера 1С.</summary>
    ImportClusterInfobases,

    /// <summary>Миграция баз между кластерами серверов 1С (issue #41).</summary>
    MigrateClusterInfobases,

    /// <summary>Завершение процессов платформы 1С.</summary>
    KillOneCProcesses,

    /// <summary>Массовая замена строк подключения.</summary>
    ConnectionReplace,

    /// <summary>Отмена последней замены строк подключения (динамическая доступность).</summary>
    UndoConnectionReplace,

    /// <summary>Удаление отсутствующих файловых баз.</summary>
    RemoveMissingFileBases,

    /// <summary>Проверка обновлений приложения.</summary>
    CheckForUpdates,

    /// <summary>Журнал регистрации информационной базы (1.0.0.69).</summary>
    EventLog,

    /// <summary>Расширения конфигурации (.cfe) (1.0.0.69).</summary>
    ConfigExtensions,

    /// <summary>Версионирование конфигурации в Git (1.0.0.69).</summary>
    GitVersioning,

    /// <summary>Технологический журнал (1.0.0.69).</summary>
    TechLog,


    /// <summary>Мониторинг и оповещения (1.0.0.69).</summary>
    ServerAlerts,

    /// <summary>Резервные копии средствами СУБД (1.0.0.69).</summary>
    DbmsBackup,

    /// <summary>Команды Telegram-бота (1.0.0.69).</summary>
    TelegramBot,

    /// <summary>Общие списки баз отдела (1.0.0.69).</summary>
    SharedLists,

    /// <summary>Пользователи информационных баз (1.0.0.83).</summary>
    InfobaseUsers,

    /// <summary>Регламентные задания информационных баз (1.0.0.85).</summary>
    InfobaseScheduledJobs,

    /// <summary>Сбор архива диагностики (ZIP с журналами) — issue #99: быстрый доступ
    /// из «Утилиты» → «Информация», без открытия окна настроек.</summary>
    CollectDiagnostics,

    // ---- Команды панели, перенесённые в разделы (навигация 1.0.1.99) ----

    /// <summary>Очистить локальный кеш 1С выбранной базы.</summary>
    ClearCache,

    /// <summary>Синхронизировать список баз с ibases.v8i.</summary>
    SyncIbases,

    /// <summary>Проверить доступность всех баз 1С.</summary>
    CheckAvailability,

    /// <summary>Переключить тему оформления (светлая/тёмная).</summary>
    ToggleTheme,

    /// <summary>Переключить компактный режим списка.</summary>
    ToggleCompact,

    /// <summary>Сменить пользователя профиля.</summary>
    SwitchUser
}

/// <summary>
/// Пункт каталога меню «Утилиты»: стабильные метаданные, не зависящие от платформы.
/// Иконки задаются парами «ключ для WPF (PackIcon Kind) / ключ для Avalonia (Icon*)»,
/// потому что наборы иконок двух UI-фреймворков не совпадают.
/// </summary>
public sealed class UtilitiesMenuItem
{
    /// <param name="id">Стабильный идентификатор пункта (сохраняется в файле компоновки).</param>
    /// <param name="textKey">Ключ локализации заголовка.</param>
    /// <param name="defaultGroup">Стандартная группа: <see cref="UtilitiesMenuCatalog.GroupTopLevel"/> и др.</param>
    /// <param name="wpfIconKind">Ключ иконки materialDesign PackIcon (WPF).</param>
    /// <param name="avaloniaIconKey">Ключ иконки Icon* из Themes/Icons.axaml (Avalonia).</param>
    /// <param name="iconColor">Цвет иконки #RRGGBB.</param>
    /// <param name="commandKind">Вид команды для построителя меню.</param>
    /// <param name="hotkeyKey">Имя свойства MainViewModel с хоткеем (или null).</param>
    /// <param name="linuxOnly">Пункт есть только в меню Avalonia/Linux.</param>
    /// <param name="isNew">Признак «новинка»: при сохранённой компоновке попадает в «Новинки программы».</param>
    public UtilitiesMenuItem(string id, string textKey, string defaultGroup,
        string wpfIconKind, string avaloniaIconKey, string iconColor,
        UtilitiesMenuCommandKind commandKind, string? hotkeyKey = null,
        bool linuxOnly = false, bool isNew = false,
        Licensing.ProFeature? requiredFeature = null)
    {
        RequiredFeature = requiredFeature;
        Id = id;
        TextKey = textKey;
        DefaultGroup = defaultGroup;
        WpfIconKind = wpfIconKind;
        AvaloniaIconKey = avaloniaIconKey;
        IconColor = iconColor;
        CommandKind = commandKind;
        HotkeyKey = hotkeyKey;
        LinuxOnly = linuxOnly;
        IsNew = isNew;
    }

    /// <summary>Стабильный идентификатор пункта.</summary>
    public string Id { get; }

    /// <summary>Ключ локализации заголовка.</summary>
    public string TextKey { get; }

    /// <summary>Стандартная группа (TopLevel / LockOptions / Information / Operations).</summary>
    public string DefaultGroup { get; }

    /// <summary>Ключ иконки materialDesign PackIcon (WPF).</summary>
    public string WpfIconKind { get; }

    /// <summary>Ключ иконки Icon* из Themes/Icons.axaml (Avalonia).</summary>
    public string AvaloniaIconKey { get; }

    /// <summary>Цвет иконки #RRGGBB.</summary>
    public string IconColor { get; }

    /// <summary>Вид команды для построителя меню.</summary>
    public UtilitiesMenuCommandKind CommandKind { get; }

    /// <summary>Имя свойства MainViewModel с хоткеем (или null).</summary>
    public string? HotkeyKey { get; }

    /// <summary>Пункт есть только в меню Avalonia/Linux.</summary>
    public bool LinuxOnly { get; }

    /// <summary>Признак «новинка» (для подменю «Новинки программы»).</summary>
    public bool IsNew { get; }

    /// <summary>
    /// Функция редакции ПРО (2.0): без лицензии пункт помечается «ПРО», а команда
    /// открывает окно «Лицензия» (проверка — в самой команде через ProGate).
    /// </summary>
    public Licensing.ProFeature? RequiredFeature { get; }

    /// <summary>Заголовок пункта с отметкой «*», если функция закрыта лицензией.</summary>
    public string DisplayText()
    {
        var text = Localization.LocalizationManager.T(TextKey);
        if (RequiredFeature is { } feature && !Licensing.EditionService.Has(feature))
            text += " " + Localization.LocalizationManager.T("Pro.Badge");
        return text;
    }
}

/// <summary>
/// Каталог всех известных пунктов меню «Утилиты» (issue #374) — единый источник истины
/// о составе меню. Новые пункты, добавляемые в будущих версиях, помечаются
/// <see cref="UtilitiesMenuItem.IsNew"/> и при сохранённой пользовательской компоновке
/// автоматически попадают в подменю «Новинки программы».
/// </summary>
public static class UtilitiesMenuCatalog
{
    // ======================= Разделы первого уровня (навигация 1.0.1.99) =======================
    //
    // Плоское меню из ~50 пунктов заменено четырьмя сценарными разделами:
    // «Серверы», «Обслуживание», «Инструменты», «Ещё». Пятый пункт основной
    // навигации — «Базы» — это сам список баз (главный экран), а не меню.

    /// <summary>Раздел первого уровня: серверы, кластеры, службы, консоль.</summary>
    public const string GroupServers = "Servers";

    /// <summary>Раздел первого уровня: копии, планировщик, диагностика, процессы.</summary>
    public const string GroupMaintenance = "Maintenance";

    /// <summary>Раздел первого уровня: сравнение, журналы, списки баз, разработка, сервисы.</summary>
    public const string GroupTools = "Tools";

    /// <summary>Раздел первого уровня: обновления, приложение, справка.</summary>
    public const string GroupMore = "More";

    /// <summary>Подгруппа «Обслуживание» → резервные копии.</summary>
    public const string GroupBackups = "Backups";

    /// <summary>Подгруппа «Обслуживание» → планировщик и уведомления.</summary>
    public const string GroupSchedule = "Schedule";

    /// <summary>Подгруппа «Обслуживание» → диагностика и обслуживание.</summary>
    public const string GroupDiagnostics = "Diagnostics";

    /// <summary>Подгруппа «Обслуживание» → процессы 1С.</summary>
    public const string GroupProcesses = "Processes";

    /// <summary>Подгруппа «Инструменты» → сравнение конфигураций и метаданных.</summary>
    public const string GroupCompare = "Compare";

    /// <summary>Подгруппа «Инструменты» → журналы.</summary>
    public const string GroupJournals = "Journals";

    /// <summary>Подгруппа «Инструменты» → экспорт, импорт и массовые операции по списку баз.</summary>
    public const string GroupBaseLists = "BaseLists";

    /// <summary>Подгруппа «Инструменты» → расширения, пользователи, Git, хранилище.</summary>
    public const string GroupDevelopment = "Development";

    /// <summary>Подгруппа «Инструменты» → сервисы редакции ПРО.</summary>
    public const string GroupProServices = "ProServices";

    /// <summary>Подгруппа «Ещё» → обновления приложения и платформы.</summary>
    public const string GroupUpdates = "Updates";

    /// <summary>Подгруппа «Ещё» → блокировка и приватные базы.</summary>
    public const string GroupApp = "App";

    /// <summary>Подгруппа «Ещё» → командная палитра, лицензия.</summary>
    public const string GroupHelp = "Help";

    /// <summary>
    /// Легаси-группа версии 1.0.1.98 и ранее: плоский верхний уровень меню.
    /// Оставлена для чтения сохранённых компоновок (миграция в
    /// <see cref="UtilitiesMenuLayouts.Migrate"/>).
    /// </summary>
    public const string GroupTopLevel = "TopLevel";

    /// <summary>Легаси-группа «Опции блокировки» (до 1.0.1.99).</summary>
    public const string GroupLockOptions = "LockOptions";

    /// <summary>Легаси-группа «Информация» (до 1.0.1.99).</summary>
    public const string GroupInformation = "Information";

    /// <summary>Легаси-группа «Операции» (до 1.0.1.99).</summary>
    public const string GroupOperations = "Operations";

    /// <summary>Служебная группа: «Новинки программы» (авто-добавление новых пунктов).</summary>
    public const string GroupNewItems = "NewItems";

    /// <summary>
    /// Запасная «папка» — фолбэк резолва неизвестной иконки (1.0.0.103 и ранее, когда
    /// ключа геометрии IconNewBox ещё не было в словарях, меню «Утилиты» и оба меню
    /// в Avalonia/Linux рисовали папку вместо звезды NEW). Сохранённые до 1.0.0.103
    /// layout-файлы (<c>utilities_menu_layout.json</c>) могли записать именно её (или
    /// пустую иконку) в группу «Новинки программы» — реальное меню потом показывало
    /// «не ту» иконку, хотя каталог и редактор уже отдавали NewBox (issue #81).
    /// </summary>
    public const string LegacyFallbackIconKey = "IconFolder";

    /// <summary>
    /// Является ли сохранённая иконка узла легаси-фолбэком (пустая, запасная
    /// «папка» IconFolder/Folder ИЛИ старая «звезда» Star/IconStar — дефолт «Новинок»
    /// до появления NewBox), который записывался старыми layout-файлами. Такие значения
    /// сбрасываются к стандартной иконке каталога NewBox, чтобы иконка «Новинок
    /// программы» была везде одинакова (issue #81, комментарий 7OH «при показе
    /// Утилиты — звезда»: layout с сохранённой «звездой» продолжал показывать её
    /// в реальном меню, тогда как редактор и контекстное меню уже отдавали NewBox).
    /// </summary>
    public static bool IsLegacyFallbackGroupIcon(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return true;
        var key = iconKey.Trim();
        return string.Equals(key, LegacyFallbackIconKey, StringComparison.Ordinal)
            || string.Equals(key, "Folder", StringComparison.Ordinal)
            // «Звезда» — старый дефолт «Новинок» до появления NewBox; учитываем и варианты
            // наборов/обводки, которые могли попасть в layout пользователя (issue #81).
            || string.Equals(key, "Star", StringComparison.Ordinal)
            || string.Equals(key, "IconStar", StringComparison.Ordinal)
            || string.Equals(key, "StarOutline", StringComparison.Ordinal)
            || string.Equals(key, "IconStarOutline", StringComparison.Ordinal)
            || string.Equals(key, "StarBorder", StringComparison.Ordinal)
            || string.Equals(key, "IconStarBorder", StringComparison.Ordinal);
    }

    /// <summary>Ключ локализации заголовка стандартной группы.</summary>
    public static string GroupTitleKey(string groupId) => groupId switch
    {
        GroupServers => "Utilities.Submenu.Servers",
        GroupMaintenance => "Utilities.Submenu.Maintenance",
        GroupTools => "Utilities.Submenu.Tools",
        GroupMore => "Utilities.Submenu.More",
        GroupBackups => "Utilities.Submenu.Backups",
        GroupSchedule => "Utilities.Submenu.Schedule",
        GroupDiagnostics => "Utilities.Submenu.Diagnostics",
        GroupProcesses => "Utilities.Submenu.Processes",
        GroupCompare => "Utilities.Submenu.Compare",
        GroupJournals => "Utilities.Submenu.Journals",
        GroupBaseLists => "Utilities.Submenu.BaseLists",
        GroupDevelopment => "Utilities.Submenu.Development",
        GroupProServices => "Utilities.Submenu.ProServices",
        GroupUpdates => "Utilities.Submenu.Updates",
        GroupApp => "Utilities.Submenu.App",
        GroupHelp => "Utilities.Submenu.Help",
        // Легаси-заголовки: нужны, чтобы старые компоновки отображались осмысленно
        // до миграции, и чтобы редактор меню не показывал пустые группы.
        GroupLockOptions => "Utilities.Submenu.LockOptions",
        GroupInformation => "Utilities.Submenu.Information",
        GroupOperations => "Utilities.Submenu.Operations",
        GroupNewItems => "Utilities.Submenu.NewItems",
        _ => string.Empty
    };

    /// <summary>Разделы первого уровня в порядке отображения (для панели навигации).</summary>
    public static IReadOnlyList<string> RootSections { get; } = new[]
    {
        GroupServers, GroupMaintenance, GroupTools, GroupMore
    };

    /// <summary>True — идентификатор является разделом первого уровня.</summary>
    public static bool IsSectionGroupId(string? groupId) =>
        groupId is not null && RootSections.Contains(groupId, StringComparer.Ordinal);

    /// <summary>
    /// True — идентификатор относится к легаси-группам версии 1.0.1.98 и ранее.
    /// Такие группы встречаются только в сохранённых компоновках и подлежат миграции.
    /// </summary>
    public static bool IsLegacyGroupId(string? groupId) =>
        string.Equals(groupId, GroupTopLevel, StringComparison.Ordinal)
        || string.Equals(groupId, GroupLockOptions, StringComparison.Ordinal)
        || string.Equals(groupId, GroupInformation, StringComparison.Ordinal)
        || string.Equals(groupId, GroupOperations, StringComparison.Ordinal);

    /// <summary>Ключ иконки стандартной группы для WPF (PackIcon Kind); пусто — без иконки.</summary>
    public static string GroupWpfIconKind(string groupId) => groupId switch
    {
        GroupServers => "ServerNetwork",
        GroupMaintenance => "Cog",
        GroupTools => "Tune",
        GroupMore => "Apps",
        GroupBackups => "BackupRestore",
        GroupSchedule => "CalendarClock",
        GroupDiagnostics => "MonitorDashboard",
        GroupProcesses => "ApplicationCog",
        GroupCompare => "Compare",
        GroupJournals => "History",
        GroupBaseLists => "Database",
        GroupDevelopment => "SourceBranch",
        GroupProServices => "Star",
        GroupUpdates => "Update",
        GroupApp => "Lock",
        GroupHelp => "HelpCircle",
        GroupLockOptions => "Lock",
        GroupInformation => "Information",
        GroupOperations => "Tune",
        GroupNewItems => "NewBox",
        _ => string.Empty
    };

    /// <summary>Ключ иконки стандартной группы для Avalonia (Icon*); пусто — без иконки.</summary>
    public static string GroupAvaloniaIconKey(string groupId) => groupId switch
    {
        GroupServers => "IconServer",
        GroupMaintenance => "IconSettings",
        GroupTools => "IconTune",
        GroupMore => "IconMenu",
        GroupBackups => "IconBackup",
        GroupSchedule => "IconScheduler",
        GroupDiagnostics => "IconMonitoring",
        GroupProcesses => "IconApplicationCog",
        GroupCompare => "IconCompare",
        GroupJournals => "IconHistory",
        GroupBaseLists => "IconDatabase",
        GroupDevelopment => "IconCloudDownload",
        GroupProServices => "IconStar",
        GroupUpdates => "IconUpdate",
        GroupApp => "IconExitToApp",
        GroupHelp => "IconInfo",
        GroupLockOptions => "IconExitToApp",
        GroupInformation => "IconInfo",
        GroupOperations => "IconTune",
        GroupNewItems => "IconNewBox",
        _ => string.Empty
    };

    /// <summary>Цвет иконки стандартной группы (#RRGGBB).</summary>
    public static string GroupIconColor(string groupId) => groupId switch
    {
        GroupServers => "#14B8A6",
        GroupMaintenance => "#0EA5E9",
        GroupTools => "#06B6D4",
        GroupMore => "#64748B",
        GroupBackups => "#16A34A",
        GroupSchedule => "#F59E0B",
        GroupDiagnostics => "#0EA5E9",
        GroupProcesses => "#F59E0B",
        GroupCompare => "#06B6D4",
        GroupJournals => "#EF4444",
        GroupBaseLists => "#3B82F6",
        GroupDevelopment => "#8B5CF6",
        GroupProServices => "#F59E0B",
        GroupUpdates => "#3B82F6",
        GroupApp => "#8B5CF6",
        GroupHelp => "#64748B",
        GroupLockOptions => "#8B5CF6",
        GroupInformation => "#0EA5E9",
        GroupOperations => "#06B6D4",
        GroupNewItems => "#22C55E",
        _ => string.Empty
    };

    /// <summary>Все известные пункты меню «Утилиты» в порядке стандартной компоновки.</summary>
    private static readonly IReadOnlyList<UtilitiesMenuItem> BuiltIn = BuildAll();
    private static readonly List<UtilitiesMenuItem> ExternalItems = new();
    private static readonly Dictionary<string, Action> ExternalActions = new(StringComparer.Ordinal);

    /// <summary>Все пункты: встроенные и зарегистрированные модулями ПРО.</summary>
    public static IReadOnlyList<UtilitiesMenuItem> All =>
        ExternalItems.Count == 0 ? BuiltIn : BuiltIn.Concat(ExternalItems).ToList();

    /// <summary>
    /// Регистрирует пункт меню модуля ПРО (2.0): <paramref name="item"/> с
    /// <see cref="UtilitiesMenuCommandKind.External"/>, действие вызывается построителем
    /// меню WPF/Avalonia. Пункт помечается новинкой и попадает в «Новинки программы»,
    /// пока пользователь не разместит его сам. Повторная регистрация id заменяет действие.
    /// </summary>
    public static void RegisterExternal(UtilitiesMenuItem item, Action execute)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(execute);
        ExternalItems.RemoveAll(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal));
        ExternalItems.Add(item);
        ExternalActions[item.Id] = execute;
    }

    /// <summary>Убирает внешний пункт (тесты, выгрузка модуля).</summary>
    public static void UnregisterExternal(string itemId)
    {
        ExternalItems.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        ExternalActions.Remove(itemId);
    }

    /// <summary>Выполняет действие внешнего пункта; false — пункт не зарегистрирован.</summary>
    public static bool ExecuteExternal(string itemId)
    {
        if (!ExternalActions.TryGetValue(itemId, out var action))
            return false;
        action();
        return true;
    }

    /// <summary>Возвращает пункт каталога по идентификатору (или null).</summary>
    public static UtilitiesMenuItem? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        foreach (var item in All)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
        }
        return null;
    }

    // ======================= Темизация меню (Задача 6) =======================
    //
    // Статичные цвета (#RRGGBB) каталога остаются «базовой палитрой» и fallback-значением,
    // а при построении меню цвета резолвятся из активной схемы (ThemeManager.CurrentScheme)
    // для активного варианта Light/Dark: те цвета каталога, которые совпадают с ролью
    // палитры (синий — FolderColor, фиолетовый — FavoriteFolderColor, оранжевый —
    // FavoriteColor, серый — TextSecondaryColor), берутся из схемы и перекрашиваются
    // на лету при переключении темы. Остальные (бирюзовый/циан/красный и т.п.) в палитре
    // схемы отсутствуют и остаются как есть — каталог не привязан к конкретной схеме.

    /// <summary>Ключи палитры схемы по совпадающим статичным цветам каталога.</summary>
    private static readonly Dictionary<string, string> _paletteKeyByColor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#3B82F6"] = "FolderColor",      // синий (в тёмной палитре FolderColor = #3B82F6)
        ["#2D6CDF"] = "FolderColor",      // синий (в светлой палитре FolderColor = #2D6CDF)
        ["#8B5CF6"] = "FavoriteFolderColor", // фиолетовый (та же роль в светлой и тёмной палитрах)
        ["#F59E0B"] = "FavoriteColor",    // оранжевый (в тёмной палитре ярче — #FBBF24)
        ["#64748B"] = "TextSecondaryColor"  // серый (в тёмной палитре светлее — #CBD5E1)
    };

    /// <summary>
    /// Ключ палитры активной схемы для статичного цвета каталога; null — цвет не имеет
    /// соответствия в схеме и остаётся неизменным при переключении темы.
    /// </summary>
    public static string? PaletteKeyForIconColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        return _paletteKeyByColor.TryGetValue(hex.Trim(), out var key) ? key : null;
    }

    /// <summary>
    /// Резолвит цвет иконки меню «Утилиты» из активной схемы: если статичный цвет
    /// <paramref name="fallbackHex"/> имеет роль в палитре (см. <see cref="PaletteKeyForIconColor"/>),
    /// возвращается значение схемы <paramref name="scheme"/> для варианта <paramref name="dark"/>
    /// (с дефолтами палитры, если ключ отсутствует), иначе — сам статичный цвет.
    /// </summary>
    public static string ResolveIconColor(string? fallbackHex, ColorScheme? scheme, bool dark)
    {
        var paletteKey = PaletteKeyForIconColor(fallbackHex);
        if (paletteKey is null)
            return string.IsNullOrWhiteSpace(fallbackHex) ? string.Empty : fallbackHex!;
        if (scheme is not null)
            return scheme.PaletteValue(dark, paletteKey);
        return fallbackHex!;
    }

    /// <summary>Цвет иконки стандартной группы из активной схемы (fallback — статичный каталога).</summary>
    public static string ResolveGroupIconColor(string groupId, ColorScheme? scheme, bool dark)
        => ResolveIconColor(GroupIconColor(groupId), scheme, dark);

    /// <summary>Цвет иконки пункта меню из активной схемы (fallback — статичный каталога).</summary>
    public static string ResolveItemIconColor(UtilitiesMenuItem item, ColorScheme? scheme, bool dark)
        => ResolveIconColor(item.IconColor, scheme, dark);

    /// <summary>Старые ключи иконок групп трея (до единого каталога «Утилит», 1.0.0.45–1.0.0.73) →
    /// ключи геометрии Icon* базового словаря (Icons.xaml / Icons.axaml). Объявлены ДО
    /// <see cref="_wpfIconToGeometryMap"/>: статические поля инициализируются в порядке
    /// объявления, и маппинг обязан видеть массив непустым.</summary>
    private static readonly (string PackIcon, string GeometryKey)[] LegacyTrayIconGeometryKeys =
    {
        ("Folder", "IconFolder"),
        ("Database", "IconDatabase"),
        ("Star", "IconStar"),
        ("Settings", "IconSettings"),
        ("Services", "IconServices"),
        ("ExitToApp", "IconExitToApp"),
        ("Open", "IconOpen"),
        ("Sync", "IconSync"),
    };

    /// <summary>Маппинг «ключ PickIcon каталога (WPF) → ключ геометрии Icon* (Icons.xaml/Icons.axaml)».</summary>
    private static readonly IReadOnlyDictionary<string, string> _wpfIconToGeometryMap = BuildWpfIconToGeometryMap();

    private static Dictionary<string, string> BuildWpfIconToGeometryMap()
    {
        // Единый источник пар каталога: каждый пункт уже несёт пару
        // (WpfIconKind — PackIcon для WPF, AvaloniaIconKey — Icon* для обеих платформ);
        // дубликаты (несколько PackIcon-имён указывают на одну геометрию) складываются
        // в словарь без конфликтов. Стандартные группы — тем же способом.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in All)
        {
            if (string.IsNullOrWhiteSpace(item.WpfIconKind) || string.IsNullOrWhiteSpace(item.AvaloniaIconKey))
                continue;
            map[item.WpfIconKind] = item.AvaloniaIconKey;
        }
        foreach (var groupId in new[]
                 {
                     GroupServers, GroupMaintenance, GroupTools, GroupMore,
                     GroupBackups, GroupSchedule, GroupDiagnostics, GroupProcesses,
                     GroupCompare, GroupJournals, GroupBaseLists, GroupDevelopment,
                     GroupProServices, GroupUpdates, GroupApp, GroupHelp,
                     GroupLockOptions, GroupInformation, GroupOperations, GroupNewItems
                 })
        {
            var wpfKind = GroupWpfIconKind(groupId);
            var geometryKey = GroupAvaloniaIconKey(groupId);
            if (string.IsNullOrWhiteSpace(wpfKind) || string.IsNullOrWhiteSpace(geometryKey))
                continue;
            map[wpfKind] = geometryKey;
        }
        // Совместимость со старыми ключами иконок меню трея (1.0.0.45–1.0.0.73,
        // «Несколько иконок для трея» — issue #40): эти PackIcon-имена не входят
        // в каталог «Утилит», но сохранились в tray_menu_layout.json у пользователей.
        // Без алиасов старая иконка группы резолвилась бы в запасную «папку».
        foreach (var (legacy, geometryKey) in LegacyTrayIconGeometryKeys)
            map[legacy] = geometryKey;
        return map;
    }

    /// <summary>
    /// Ключ геометрии (Icon*) для WPF-ключа иконки: ключ <c>Icon*</c> возвращается как есть
    /// (пользовательские компоновки и редактор могут хранить и такие), PackIcon-имя каталога
    /// (например «Magnify») отображается на парный ключ геометрии («IconSearch»). Неизвестный
    /// ключ даёт пустую строку — меню останется без иконки, как и при неизвестном PackIconKind.
    /// </summary>
    public static string WpfIconGeometryKey(string? wpfIconKey)
    {
        if (string.IsNullOrWhiteSpace(wpfIconKey))
            return string.Empty;
        if (wpfIconKey.StartsWith("Icon", StringComparison.Ordinal))
            return wpfIconKey;
        if (_wpfIconToGeometryMap.TryGetValue(wpfIconKey, out var geometryKey))
            return geometryKey;

        // Пункты, зарегистрированные модулями ПРО (RegisterExternal) уже после
        // построения статического словаря, должны резолвиться так же: ищем пару
        // в текущем каталоге, иначе иконка внешнего пункта молча пропадала.
        foreach (var item in All)
        {
            if (string.Equals(item.WpfIconKind, wpfIconKey, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.AvaloniaIconKey))
                return item.AvaloniaIconKey;
        }
        return string.Empty;
    }

    private static IReadOnlyList<UtilitiesMenuItem> BuildAll()
    {
        return new List<UtilitiesMenuItem>
        {
            // ---- Верхний уровень ----
            new("CommandPalette", "Main.CommandPalette", GroupHelp,
                "Magnify", "IconSearch", "#3B82F6", UtilitiesMenuCommandKind.CommandPalette, "HotkeyCommandPalette"),
            new("ActualReleases", "Updates.ActualReleasesTitle", GroupUpdates,
                "CloudDownload", "IconCloudDownload", "#14B8A6", UtilitiesMenuCommandKind.ShowActualReleases, "HotkeyActualReleases"),
            new("PlatformUpdate", "PlatformUpdate.WindowTitle", GroupUpdates,
                "PackageVariant", "IconCloudDownload", "#14B8A6", UtilitiesMenuCommandKind.ShowPlatformUpdate, "HotkeyPlatformUpdate"),
            new("PlatformDownload", "PlatformDownload.WindowTitle", GroupUpdates,
                "Download", "IconCloudDownload", "#22C55E", UtilitiesMenuCommandKind.ShowPlatformDownload),
            new("ServerMonitor", "ServerMonitor.Title", GroupServers,
                "ServerNetwork", "IconServer", "#14B8A6", UtilitiesMenuCommandKind.ServerMonitor, "HotkeyServerConsole"),
            // Управление службами (issue #11): сразу после «Серверы 1С», до внешней консоли.
            new("Services", "Services.MenuItem", GroupServers,
                "Cog", "IconSettings", "#06B6D4", UtilitiesMenuCommandKind.Services, "HotkeyServices", isNew: true),
            new("ServerConsoleExternal", "Admin.ServerConsoleExternal", GroupServers,
                "Server", "IconServer", "#64748B", UtilitiesMenuCommandKind.OpenServerConsole),
            // Регистрация компонентов консоли (issue #135): radmin.dll и comctrl.dll нужны,
            // если оснастка не запускается. Windows-only — в Avalonia/Linux пункт недоступен.
            new("ServerConsoleRegister", "Admin.ServerConsoleRegister", GroupServers,
                "Cog", "IconSettings", "#64748B",
                UtilitiesMenuCommandKind.RegisterServerConsoleComponents, isNew: true),

            // ---- Опции блокировки ----
            new("AppLock", "AppLock.LockTitle", GroupApp,
                "Lock", "IconExitToApp", "#8B5CF6", UtilitiesMenuCommandKind.LockApp, "HotkeyLockApp"),
            new("UnlockPrivateBases", "Private.OpenCommand", GroupApp,
                "LockOpenVariant", "IconAccountKey", "#22C55E", UtilitiesMenuCommandKind.UnlockPrivateBases),
            new("ChangeAppLock", "AppLock.ChangeMenu", GroupApp,
                "Key", "IconAccountKey", "#8B5CF6", UtilitiesMenuCommandKind.ChangeAppLock),
            new("RunBackupScenario", "Backup.RunTitle", GroupBackups,
                "DatabaseExport", "IconDatabaseExport", "#22C55E", UtilitiesMenuCommandKind.RunBackupScenario, "HotkeyRunBackup"),

            // ---- Информация ----
            new("MaintenanceCenter", "Maintenance.Title", GroupDiagnostics,
                "MonitorDashboard", "IconMonitoring", "#0EA5E9", UtilitiesMenuCommandKind.MaintenanceCenter),
            new("ConfigTypesEdit", "Updates.ManageList", GroupDiagnostics,
                string.Empty, string.Empty, string.Empty, UtilitiesMenuCommandKind.OpenConfigTypesEdit),
            new("ItsAccounts", "ItsAccounts.MenuItem", GroupSchedule,
                "AccountKey", "IconAccountKey", "#14B8A6", UtilitiesMenuCommandKind.OpenItsAccounts),
            new("NotificationChannels", "Notifications.MenuItem", GroupSchedule,
                "BellOutline", "IconPublish", "#0EA5E9", UtilitiesMenuCommandKind.OpenNotificationChannels,
                isNew: true),
            new("ScheduledTasks", "Schedule.Title", GroupSchedule,
                "CalendarClock", "IconScheduler", "#14B8A6", UtilitiesMenuCommandKind.OpenScheduledTasks),
            new("BackupScenarios", "Backup.ScenariosTitle", GroupBackups,
                "Cog", "IconSettings", "#F59E0B", UtilitiesMenuCommandKind.ShowBackupScenarios),
            new("ScriptsSettings", "Script.SettingsTitle", GroupDiagnostics,
                "CodeBraces", "IconScript", "#06B6D4", UtilitiesMenuCommandKind.ShowScriptsSettings),
            new("UsageStatistics", "Stats.Title", GroupDiagnostics,
                "History", "IconHistory", "#14B8A6", UtilitiesMenuCommandKind.UsageStatistics),
            // «Собрать диагностику» (issue #99): ZIP с журналами приложения в один клик.
            // v1.0.1.23: настраиваемый хоткей (AppSettings.HotkeyCollectDiagnostics,
            // по умолчанию пусто — решение 7OH); подпись хоткея на пункте меню берётся
            // из этого ключа, как у остальных пунктов.
            // Пара иконок («IconLogs»/«IconLogs») существует в ОБОИХ базовых словарях
            // (Icons.xaml/Icons.axaml) и во всех наборах — страж TrayMenuIconCatalogTests.
            new("CollectDiagnostics", "Utilities.CollectDiagnostics", GroupDiagnostics,
                "IconLogs", "IconLogs", "#16A34A", UtilitiesMenuCommandKind.CollectDiagnostics,
                "HotkeyCollectDiagnostics", isNew: true),

            // ---- Операции ----
            new("ConfigDiff", "ConfigDiff.Title", GroupCompare,
                "Compare", "IconCompare", "#06B6D4", UtilitiesMenuCommandKind.ConfigDiff),
            new("MetadataExplorer", "MetadataExplorer.Title", GroupCompare,
                "Sitemap", "IconCompare", "#06B6D4", UtilitiesMenuCommandKind.MetadataExplorer),
            // «Журнал регистрации» (1.0.0.69): просмотр журнала файловой базы или каталога 1Cv8Log.
            new("EventLog", "EventLog.MenuItem", GroupJournals,
                "IconLogs", "IconLogs", "#EF4444", UtilitiesMenuCommandKind.EventLog,
                isNew: true),
            // «Расширения конфигурации» (1.0.0.69): пакетная установка .cfe в отмеченные базы.
            new("ConfigExtensions", "Extensions.MenuItem", GroupDevelopment,
                "IconExtension", "IconExtension", "#8B5CF6", UtilitiesMenuCommandKind.ConfigExtensions,
                isNew: true),
            // «Пользователи информационных баз» (1.0.0.83): список и пакетные операции через COM.
            new("InfobaseUsers", "IbUsers.MenuItem", GroupDevelopment,
                "IconUsers", "IconUsers", "#6366F1", UtilitiesMenuCommandKind.InfobaseUsers,
                isNew: true),
            // «Регламентные задания ИБ» (1.0.0.85): список, включение/выключение через COM.
            new("InfobaseScheduledJobs", "Jobs.MenuItem", GroupDevelopment,
                "CalendarClock", "IconScheduler", "#F59E0B", UtilitiesMenuCommandKind.InfobaseScheduledJobs,
                isNew: true),
            // «Версионирование конфигурации в Git (1.0.0.69)».
            new("GitVersioning", "Git.MenuItem", GroupDevelopment,
                "IconMerge", "IconMerge", "#F97316", UtilitiesMenuCommandKind.GitVersioning,
                isNew: true),
            // «Технологический журнал (1.0.0.69)».
            new("TechLog", "TechLog.MenuItem", GroupJournals,
                "IconMonitoring", "IconMonitoring", "#0EA5E9", UtilitiesMenuCommandKind.TechLog,
                isNew: true),
            // «Мониторинг и оповещения (1.0.0.69)».
            new("ServerAlerts", "Alerts.MenuItem", GroupProServices,
                "IconMonitoring", "IconMonitoring", "#EF4444", UtilitiesMenuCommandKind.ServerAlerts,
                isNew: true, requiredFeature: Licensing.ProFeature.ServerAlerts),
            // «Резервные копии средствами СУБД (1.0.0.69)».
            new("DbmsBackup", "Dbms.MenuItem", GroupBackups,
                "IconBackup", "IconBackup", "#16A34A", UtilitiesMenuCommandKind.DbmsBackup,
                isNew: true),
            // «Команды Telegram-бота (1.0.0.69)».
            new("TelegramBot", "Bot.MenuItem", GroupProServices,
                "IconPublish", "IconPublish", "#0EA5E9", UtilitiesMenuCommandKind.TelegramBot,
                isNew: true, requiredFeature: Licensing.ProFeature.TelegramBot),
            // «Общие списки баз отдела (1.0.0.69)».
            new("SharedLists", "Shared.MenuItem", GroupBaseLists,
                "IconUsers", "IconUsers", "#3B82F6", UtilitiesMenuCommandKind.SharedLists,
                isNew: true),
            new("RepositoryBatchUpdate", "RepoUpdate.Title", GroupDevelopment,
                "SourceBranch", "IconCloudDownload", "#06B6D4", UtilitiesMenuCommandKind.RepositoryBatchUpdate),
            new("RepositoryBrowser", "RepositoryBrowser.Title", GroupDevelopment,
                "SourceBranch", "IconCloudDownload", "#8B5CF6", UtilitiesMenuCommandKind.RepositoryBrowser),
            // Массовые операции по списку баз (issue #91): выгрузка .dt и загрузка .cf.
            new("BatchDumpDt", "BatchOps.DumpDtTitle", GroupBaseLists,
                "DatabaseExport", "IconDatabaseExport", "#16A34A", UtilitiesMenuCommandKind.BatchDumpDt,
                isNew: true),
            new("BatchLoadCf", "BatchOps.LoadCfTitle", GroupBaseLists,
                "DatabaseImport", "IconImport", "#F59E0B", UtilitiesMenuCommandKind.BatchLoadCf,
                isNew: true, requiredFeature: Licensing.ProFeature.MassLoadCf),
            new("ExportCsv", "Utilities.ExportCsv", GroupBaseLists,
                "FileDelimited", "IconFileExport", "#22C55E", UtilitiesMenuCommandKind.ExportBasesCsv),
            new("ExportHtml", "Utilities.ExportHtml", GroupBaseLists,
                "FileDocument", "IconFileDocument", "#F59E0B", UtilitiesMenuCommandKind.ExportBasesHtml),
            new("ExportJson", "Utilities.ExportJson", GroupBaseLists,
                "FileExport", "IconFileExport", "#06B6D4", UtilitiesMenuCommandKind.ExportBasesJson),
            new("ImportJson", "Utilities.ImportJson", GroupBaseLists,
                "DatabaseImport", "IconImport", "#06B6D4", UtilitiesMenuCommandKind.ImportBasesJson),
            // Экспорт/импорт в произвольный файл *.v8i (issue #29): те же операции, что
            // в настройках (вкладка «Базы» → «Список баз»), с теми же названиями и сервисами.
            new("ExportV8iFile", "Settings.Bases.ExportV8iFile", GroupBaseLists,
                "FileExport", "IconFileExport", "#22C55E", UtilitiesMenuCommandKind.ExportBasesV8iFile),
            new("ImportV8iFile", "Settings.Bases.ImportV8iFile", GroupBaseLists,
                "DatabaseImport", "IconImport", "#22C55E", UtilitiesMenuCommandKind.ImportBasesV8iFile),
            // «Импорт из кластера 1С» (issue #27) и «Миграция между кластерами 1С»
            // (issue #41, переработка): отдельные пункты — импорт остаётся импортом,
            // миграция вынесена в самостоятельное окно. Хоткея у миграции нет — это
            // не частая операция (комментарий 7OH).
            new("ClusterImport", "ClusterImport.MenuItem", GroupServers,
                "DatabaseImport", "IconImport", "#06B6D4", UtilitiesMenuCommandKind.ImportClusterInfobases),
            // «Миграция между кластерами 1С» (issue #41, 1.0.0.66): флаг новинки,
            // чтобы при сохранённой пользовательской компоновке пункт попал в
            // «Новинки программы», пока пользователь не разместит его явно (issue #58).
            new("ClusterMigration", "ClusterMigration.MenuItem", GroupServers,
                "DatabaseExport", "IconDatabaseExport", "#16A34A", UtilitiesMenuCommandKind.MigrateClusterInfobases,
                isNew: true, requiredFeature: Licensing.ProFeature.ClusterMigration),
            // issue #134 (1.0.1.84): «Инспектор процессов 1С» убран из меню «Утилиты» —
            // он доступен кнопкой на панели инструментов и горячей клавишей Ctrl+Alt+I.
            new("KillOneCProcesses", "Settings.Bases.KillProcesses", GroupProcesses,
                "CloseNetwork", "IconClose", "#F59E0B", UtilitiesMenuCommandKind.KillOneCProcesses),
            new("ConnectionReplace", "Main.UtilitiesConnectionReplace", GroupBaseLists,
                "FindReplace", "IconFindReplace", "#F59E0B", UtilitiesMenuCommandKind.ConnectionReplace),
            new("UndoConnectionReplace", "Main.UtilitiesUndoConnectionReplace", GroupBaseLists,
                "UndoVariant", "IconUndo", "#F59E0B", UtilitiesMenuCommandKind.UndoConnectionReplace),
            new("RemoveMissingFileBases", "Settings.Bases.RemoveMissing", GroupBaseLists,
                "FolderRemove", "IconFolderRemove", "#EF4444", UtilitiesMenuCommandKind.RemoveMissingFileBases),

            // ---- Команды панели, перенесённые в разделы (навигация 1.0.1.99) ----
            // Сами команды не удалены: они доступны в сценарных разделах, а на панели
            // остаются только ежедневные действия и четыре раздела.
            new("ClearCache", "Main.ClearCacheTooltip", GroupDiagnostics,
                "Broom", "IconBroom", "#F59E0B", UtilitiesMenuCommandKind.ClearCache),
            new("SyncIbases", "Main.SyncDetailedTooltip", GroupBaseLists,
                "Sync", "IconSync", "#06B6D4", UtilitiesMenuCommandKind.SyncIbases),
            new("CheckAvailability", "Main.CheckAvailabilityTooltip", GroupProcesses,
                "Sonar", "IconSonar", "#22C55E", UtilitiesMenuCommandKind.CheckAvailability),
            new("ToggleTheme", "Main.Theme", GroupApp,
                "WeatherNight", "IconMoon", "#64748B", UtilitiesMenuCommandKind.ToggleTheme),
            new("ToggleCompact", "Main.CompactModeTooltip", GroupApp,
                "Compress", "IconCompress", "#64748B", UtilitiesMenuCommandKind.ToggleCompact),
            new("SwitchUser", "Main.SwitchUserTooltip", GroupApp,
                "AccountMultiple", "IconAccountMultiple", "#64748B", UtilitiesMenuCommandKind.SwitchUser),

            // ---- Низ меню ----
            new("CheckForUpdates", "Settings.About.CheckForUpdates", GroupUpdates,
                "Update", "IconUpdate", "#3B82F6", UtilitiesMenuCommandKind.CheckForUpdates),
            // «Лицензия…» (2.0, docs/pro/PRO-EDITION.md): редакция, код компьютера, активация ключа.
            new("License", "Pro.MenuItem", GroupHelp,
                "Key", "IconAccountKey", "#F59E0B", UtilitiesMenuCommandKind.ShowLicense,
                isNew: true)
        };
    }
}

/// <summary>
/// Построитель стандартной компоновки меню «Утилиты»: воспроизводит текущую статическую
/// структуру (порядок, группы, разделители), чтобы при выключенной правке меню выглядело
/// ровно как до появления настройки (обратная совместимость).
/// </summary>
public static class UtilitiesMenuLayouts
{
    /// <summary>Актуальная версия схемы файла компоновки (навигация 1.0.1.99 — разделы).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Стандартная компоновка меню: четыре сценарных раздела вместо плоского списка.
    /// Внутри разделов — подгруппы, каждая не длиннее семи пунктов. Все пункты каталога
    /// присутствуют: ни одна функция не удалена.
    /// Разделители — самостоятельные узлы <see cref="Separator"/> (issue #374):
    /// редактор показывает всю структуру «как есть», без скрытых флагов SeparatorBefore.
    /// </summary>
    public static UtilitiesMenuLayout Default()
    {
        var layout = new UtilitiesMenuLayout { SchemaVersion = CurrentSchemaVersion };
        layout.Root = new List<UtilitiesMenuNode>
        {
            // ---- 1. Серверы ----
            Group(UtilitiesMenuCatalog.GroupServers, new List<UtilitiesMenuNode>
            {
                Item("ServerMonitor"),
                Item("Services"),
                Item("ServerConsoleExternal"),
                Separator(),
                Item("ServerConsoleRegister"),
                Separator(),
                Item("ClusterImport"),
                Item("ClusterMigration")
            }),

            // ---- 2. Обслуживание ----
            Group(UtilitiesMenuCatalog.GroupMaintenance, new List<UtilitiesMenuNode>
            {
                Group(UtilitiesMenuCatalog.GroupBackups, new List<UtilitiesMenuNode>
                {
                    Item("BackupScenarios"),
                    Item("RunBackupScenario"),
                    Separator(),
                    Item("DbmsBackup")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupSchedule, new List<UtilitiesMenuNode>
                {
                    Item("ScheduledTasks"),
                    Item("NotificationChannels"),
                    Item("ItsAccounts")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupDiagnostics, new List<UtilitiesMenuNode>
                {
                    Item("MaintenanceCenter"),
                    Item("CollectDiagnostics"),
                    Item("ClearCache"),
                    Item("UsageStatistics"),
                    Item("ScriptsSettings"),
                    Item("ConfigTypesEdit")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupProcesses, new List<UtilitiesMenuNode>
                {
                    Item("CheckAvailability"),
                    Item("KillOneCProcesses")
                })
            }),

            // ---- 3. Инструменты ----
            Group(UtilitiesMenuCatalog.GroupTools, new List<UtilitiesMenuNode>
            {
                Group(UtilitiesMenuCatalog.GroupCompare, new List<UtilitiesMenuNode>
                {
                    Item("ConfigDiff"),
                    Item("MetadataExplorer")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupJournals, new List<UtilitiesMenuNode>
                {
                    Item("EventLog"),
                    Item("TechLog")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupBaseLists, new List<UtilitiesMenuNode>
                {
                    Item("ExportCsv"),
                    Item("ExportHtml"),
                    Item("ExportJson"),
                    Item("ImportJson"),
                    Separator(),
                    Item("ExportV8iFile"),
                    Item("ImportV8iFile"),
                    Separator(),
                    Item("SharedLists"),
                    Separator(),
                    Item("BatchDumpDt"),
                    Item("BatchLoadCf"),
                    Separator(),
                    Item("ConnectionReplace"),
                    Item("UndoConnectionReplace"),
                    Item("RemoveMissingFileBases"),
                    Separator(),
                    Item("SyncIbases")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupDevelopment, new List<UtilitiesMenuNode>
                {
                    Item("ConfigExtensions"),
                    Item("InfobaseUsers"),
                    Item("InfobaseScheduledJobs"),
                    Separator(),
                    Item("GitVersioning"),
                    Item("RepositoryBrowser"),
                    Item("RepositoryBatchUpdate")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupProServices, new List<UtilitiesMenuNode>
                {
                    Item("ServerAlerts"),
                    Item("TelegramBot")
                })
            }),

            // ---- 4. Ещё ----
            Group(UtilitiesMenuCatalog.GroupMore, new List<UtilitiesMenuNode>
            {
                Group(UtilitiesMenuCatalog.GroupUpdates, new List<UtilitiesMenuNode>
                {
                    Item("CheckForUpdates"),
                    Separator(),
                    Item("ActualReleases"),
                    Item("PlatformUpdate"),
                    Item("PlatformDownload")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupApp, new List<UtilitiesMenuNode>
                {
                    Item("AppLock"),
                    Item("UnlockPrivateBases"),
                    Item("ChangeAppLock"),
                    Separator(),
                    Item("ToggleTheme"),
                    Item("ToggleCompact"),
                    Item("SwitchUser")
                }),
                Separator(),
                Group(UtilitiesMenuCatalog.GroupHelp, new List<UtilitiesMenuNode>
                {
                    Item("CommandPalette"),
                    Item("License")
                })
            })
        };
        return layout;
    }

    /// <summary>
    /// Мигрирует сохранённую компоновку версии 1 (плоское меню 1.0.1.98 и ранее)
    /// в актуальную структуру разделов. Пользовательские настройки пунктов сохраняются:
    /// видимость, иконка, цвет и внешний файл иконки переносятся по <c>ItemId</c>.
    /// Пункты, удалённые пользователем из меню, не возвращаются; новые пункты каталога
    /// остаются на своих местах. Пользовательские группы не переносятся — структура
    /// навигации изменилась, а группы были лишь оформлением.
    /// </summary>
    public static UtilitiesMenuLayout Migrate(UtilitiesMenuLayout? layout)
    {
        if (layout is null)
            return Default();

        var isCurrent = layout.SchemaVersion >= CurrentSchemaVersion
            || (layout.Root?.Any(n => string.Equals(n.Kind, "group", StringComparison.Ordinal)
                && UtilitiesMenuCatalog.IsSectionGroupId(n.GroupId)) ?? false);
        if (isCurrent)
        {
            layout.SchemaVersion = CurrentSchemaVersion;
            return layout;
        }

        var saved = new Dictionary<string, UtilitiesMenuNode>(StringComparer.Ordinal);
        CollectItemSettings(layout.Root, saved);

        // Пустой/нечитаемый файл — безопаснее вернуть стандартную компоновку.
        if (saved.Count == 0)
            return Default();

        var migrated = Default();
        ApplyItemSettings(migrated.Root, saved);
        RemoveAbsentItems(migrated.Root, saved.Keys);
        RemoveEmptyGroups(migrated.Root);
        migrated.SchemaVersion = CurrentSchemaVersion;
        return migrated;
    }

    /// <summary>
    /// Выделяет СОДЕРЖИМОЕ раздела первого уровня для отдельного меню на панели навигации
    /// (навигация 1.0.1.99). В результат попадают дочерние узлы раздела, а не сам узел:
    /// иначе кнопка раздела («Серверы», «Обслуживание», «Инструменты», «Ещё») открывала бы
    /// меню с единственным подменю того же названия — дублирование уровня раздела.
    /// Возвращается новая копия списка детей, исходная компоновка не изменяется.
    /// null — раздела нет в компоновке; пустой <c>Root</c> — раздел есть, но без пунктов
    /// (меню открывается пустым, а не падает).
    /// </summary>
    public static UtilitiesMenuLayout? ExtractSection(UtilitiesMenuLayout? layout, string sectionGroupId)
    {
        if (layout?.Root is null || string.IsNullOrWhiteSpace(sectionGroupId))
            return null;
        var node = layout.Root.FirstOrDefault(n =>
            string.Equals(n.Kind, "group", StringComparison.Ordinal)
            && string.Equals(n.GroupId, sectionGroupId, StringComparison.Ordinal));
        if (node is null)
            return null;
        // Разворачиваем раздел на уровень меню: копия детей без дублирующего узла-раздела.
        var children = node.Children is null
            ? new List<UtilitiesMenuNode>()
            : new List<UtilitiesMenuNode>(node.Children);
        return new UtilitiesMenuLayout
        {
            SchemaVersion = CurrentSchemaVersion,
            Root = children
        };
    }

    /// <summary>Собирает настройки пунктов старой компоновки по <c>ItemId</c> (рекурсивно).</summary>
    private static void CollectItemSettings(IEnumerable<UtilitiesMenuNode>? nodes,
        Dictionary<string, UtilitiesMenuNode> target)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(node.ItemId))
            {
                target[node.ItemId!] = node;
            }
            else if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                CollectItemSettings(node.Children, target);
            }
        }
    }

    /// <summary>Переносит настройки видимости/иконок на пункты новой компоновки.</summary>
    private static void ApplyItemSettings(IEnumerable<UtilitiesMenuNode>? nodes,
        Dictionary<string, UtilitiesMenuNode> saved)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal)
                && node.ItemId is not null && saved.TryGetValue(node.ItemId, out var old))
            {
                node.Visible = old.Visible;
                if (!string.IsNullOrWhiteSpace(old.IconKey))
                    node.IconKey = old.IconKey;
                if (!string.IsNullOrWhiteSpace(old.IconColor))
                    node.IconColor = old.IconColor;
                if (!string.IsNullOrWhiteSpace(old.ExternalIconPath))
                    node.ExternalIconPath = old.ExternalIconPath;
            }
            else if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                ApplyItemSettings(node.Children, saved);
            }
        }
    }

    /// <summary>Удаляет пункты, которых не было в сохранённой компоновке (пользователь их убрал).
    /// Новинки каталога не удаляются — они должны появиться в своих разделах.</summary>
    private static void RemoveAbsentItems(List<UtilitiesMenuNode>? nodes, IEnumerable<string> savedIds)
    {
        if (nodes is null)
            return;
        var present = new HashSet<string>(savedIds, StringComparer.Ordinal);
        nodes.RemoveAll(n => string.Equals(n.Kind, "item", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(n.ItemId)
            && !present.Contains(n.ItemId!)
            && UtilitiesMenuCatalog.Find(n.ItemId)?.IsNew != true);
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
                RemoveAbsentItems(node.Children, present);
        }
    }

    /// <summary>Удаляет опустевшие подгруппы (разделы первого уровня остаются).</summary>
    private static void RemoveEmptyGroups(List<UtilitiesMenuNode>? nodes)
    {
        if (nodes is null)
            return;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
                RemoveEmptyGroups(node.Children);
        }
        nodes.RemoveAll(n => string.Equals(n.Kind, "group", StringComparison.Ordinal)
            && !UtilitiesMenuCatalog.IsSectionGroupId(n.GroupId)
            && (n.Children?.Count ?? 0) == 0);
    }

    /// <summary>Узел-пункт каталога.</summary>
    public static UtilitiesMenuNode Item(string itemId, bool separatorBefore = false)
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "item",
            ItemId = itemId,
            SeparatorBefore = separatorBefore
        };
        return node;
    }

    /// <summary>Узел-группа (подменю).</summary>
    public static UtilitiesMenuNode Group(string groupId, List<UtilitiesMenuNode> children,
        bool separatorBefore = false)
    {
        var node = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = groupId,
            Children = children,
            SeparatorBefore = separatorBefore
        };
        return node;
    }

    /// <summary>Узел-разделитель (issue #374): самостоятельный элемент дерева компоновки.</summary>
    public static UtilitiesMenuNode Separator() => new() { Kind = "separator" };
}