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

    /// <summary>Инспектор процессов 1С.</summary>
    ProcessInspector,

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
    InfobaseScheduledJobs
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
        bool linuxOnly = false, bool isNew = false)
    {
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
}

/// <summary>
/// Каталог всех известных пунктов меню «Утилиты» (issue #374) — единый источник истины
/// о составе меню. Новые пункты, добавляемые в будущих версиях, помечаются
/// <see cref="UtilitiesMenuItem.IsNew"/> и при сохранённой пользовательской компоновке
/// автоматически попадают в подменю «Новинки программы».
/// </summary>
public static class UtilitiesMenuCatalog
{
    /// <summary>Стандартная группа: верхний уровень меню.</summary>
    public const string GroupTopLevel = "TopLevel";

    /// <summary>Стандартная группа: подменю «Опции блокировки».</summary>
    public const string GroupLockOptions = "LockOptions";

    /// <summary>Стандартная группа: подменю «Информация».</summary>
    public const string GroupInformation = "Information";

    /// <summary>Стандартная группа: подменю «Операции».</summary>
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
            || string.Equals(key, "Star", StringComparison.Ordinal)
            || string.Equals(key, "IconStar", StringComparison.Ordinal);
    }

    /// <summary>Ключ локализации заголовка стандартной группы.</summary>
    public static string GroupTitleKey(string groupId) => groupId switch
    {
        GroupLockOptions => "Utilities.Submenu.LockOptions",
        GroupInformation => "Utilities.Submenu.Information",
        GroupOperations => "Utilities.Submenu.Operations",
        GroupNewItems => "Utilities.Submenu.NewItems",
        _ => string.Empty
    };

    /// <summary>Ключ иконки стандартной группы для WPF (PackIcon Kind); пусто — без иконки.</summary>
    public static string GroupWpfIconKind(string groupId) => groupId switch
    {
        GroupLockOptions => "Lock",
        GroupInformation => "Information",
        GroupOperations => "Tune",
        GroupNewItems => "NewBox",
        _ => string.Empty
    };

    /// <summary>Ключ иконки стандартной группы для Avalonia (Icon*); пусто — без иконки.</summary>
    public static string GroupAvaloniaIconKey(string groupId) => groupId switch
    {
        GroupLockOptions => "IconExitToApp",
        GroupInformation => "IconInfo",
        GroupOperations => "IconTune",
        GroupNewItems => "IconNewBox",
        _ => string.Empty
    };

    /// <summary>Цвет иконки стандартной группы (#RRGGBB).</summary>
    public static string GroupIconColor(string groupId) => groupId switch
    {
        GroupLockOptions => "#8B5CF6",
        GroupInformation => "#0EA5E9",
        GroupOperations => "#06B6D4",
        GroupNewItems => "#22C55E",
        _ => string.Empty
    };

    /// <summary>Все известные пункты меню «Утилиты» в порядке стандартной компоновки.</summary>
    public static IReadOnlyList<UtilitiesMenuItem> All { get; } = BuildAll();

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
        foreach (var groupId in new[] { GroupLockOptions, GroupInformation, GroupOperations, GroupNewItems })
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
        return _wpfIconToGeometryMap.TryGetValue(wpfIconKey, out var geometryKey) ? geometryKey : string.Empty;
    }

    private static IReadOnlyList<UtilitiesMenuItem> BuildAll()
    {
        return new List<UtilitiesMenuItem>
        {
            // ---- Верхний уровень ----
            new("CommandPalette", "Main.CommandPalette", GroupTopLevel,
                "Magnify", "IconSearch", "#3B82F6", UtilitiesMenuCommandKind.CommandPalette, "HotkeyCommandPalette"),
            new("ActualReleases", "Updates.ActualReleasesTitle", GroupTopLevel,
                "CloudDownload", "IconCloudDownload", "#14B8A6", UtilitiesMenuCommandKind.ShowActualReleases, "HotkeyActualReleases"),
            new("PlatformUpdate", "PlatformUpdate.WindowTitle", GroupTopLevel,
                "PackageVariant", "IconCloudDownload", "#14B8A6", UtilitiesMenuCommandKind.ShowPlatformUpdate, "HotkeyPlatformUpdate"),
            new("PlatformDownload", "PlatformDownload.WindowTitle", GroupTopLevel,
                "Download", "IconCloudDownload", "#22C55E", UtilitiesMenuCommandKind.ShowPlatformDownload),
            new("ServerMonitor", "ServerMonitor.Title", GroupTopLevel,
                "ServerNetwork", "IconServer", "#14B8A6", UtilitiesMenuCommandKind.ServerMonitor, "HotkeyServerConsole"),
            // Управление службами (issue #11): сразу после «Серверы 1С», до внешней консоли.
            new("Services", "Services.MenuItem", GroupTopLevel,
                "Cog", "IconSettings", "#06B6D4", UtilitiesMenuCommandKind.Services, "HotkeyServices", isNew: true),
            new("ServerConsoleExternal", "Admin.ServerConsoleExternal", GroupTopLevel,
                "Server", "IconServer", "#64748B", UtilitiesMenuCommandKind.OpenServerConsole),

            // ---- Опции блокировки ----
            new("AppLock", "AppLock.LockTitle", GroupTopLevel,
                "Lock", "IconExitToApp", "#8B5CF6", UtilitiesMenuCommandKind.LockApp, "HotkeyLockApp"),
            new("UnlockPrivateBases", "Private.OpenCommand", GroupLockOptions,
                "LockOpenVariant", "IconAccountKey", "#22C55E", UtilitiesMenuCommandKind.UnlockPrivateBases),
            new("ChangeAppLock", "AppLock.ChangeMenu", GroupLockOptions,
                "Key", "IconAccountKey", "#8B5CF6", UtilitiesMenuCommandKind.ChangeAppLock),
            new("RunBackupScenario", "Backup.RunTitle", GroupLockOptions,
                "DatabaseExport", "IconDatabaseExport", "#22C55E", UtilitiesMenuCommandKind.RunBackupScenario, "HotkeyRunBackup"),

            // ---- Информация ----
            new("MaintenanceCenter", "Maintenance.Title", GroupInformation,
                "MonitorDashboard", "IconMonitoring", "#0EA5E9", UtilitiesMenuCommandKind.MaintenanceCenter),
            new("ConfigTypesEdit", "Updates.ManageList", GroupInformation,
                string.Empty, string.Empty, string.Empty, UtilitiesMenuCommandKind.OpenConfigTypesEdit),
            new("ItsAccounts", "ItsAccounts.MenuItem", GroupInformation,
                "AccountKey", "IconAccountKey", "#14B8A6", UtilitiesMenuCommandKind.OpenItsAccounts),
            new("NotificationChannels", "Notifications.MenuItem", GroupInformation,
                "BellOutline", "IconPublish", "#0EA5E9", UtilitiesMenuCommandKind.OpenNotificationChannels,
                isNew: true),
            new("ScheduledTasks", "Schedule.Title", GroupInformation,
                "CalendarClock", "IconScheduler", "#14B8A6", UtilitiesMenuCommandKind.OpenScheduledTasks),
            new("BackupScenarios", "Backup.ScenariosTitle", GroupInformation,
                "Cog", "IconSettings", "#F59E0B", UtilitiesMenuCommandKind.ShowBackupScenarios),
            new("ScriptsSettings", "Script.SettingsTitle", GroupInformation,
                "CodeBraces", "IconScript", "#06B6D4", UtilitiesMenuCommandKind.ShowScriptsSettings),
            new("UsageStatistics", "Stats.Title", GroupInformation,
                "History", "IconHistory", "#14B8A6", UtilitiesMenuCommandKind.UsageStatistics),

            // ---- Операции ----
            new("ConfigDiff", "ConfigDiff.Title", GroupOperations,
                "Compare", "IconCompare", "#06B6D4", UtilitiesMenuCommandKind.ConfigDiff),
            new("MetadataExplorer", "MetadataExplorer.Title", GroupOperations,
                "Sitemap", "IconCompare", "#06B6D4", UtilitiesMenuCommandKind.MetadataExplorer),
            // «Журнал регистрации» (1.0.0.69): просмотр журнала файловой базы или каталога 1Cv8Log.
            new("EventLog", "EventLog.MenuItem", GroupOperations,
                "IconLogs", "IconLogs", "#EF4444", UtilitiesMenuCommandKind.EventLog,
                isNew: true),
            // «Расширения конфигурации» (1.0.0.69): пакетная установка .cfe в отмеченные базы.
            new("ConfigExtensions", "Extensions.MenuItem", GroupOperations,
                "IconExtension", "IconExtension", "#8B5CF6", UtilitiesMenuCommandKind.ConfigExtensions,
                isNew: true),
            // «Пользователи информационных баз» (1.0.0.83): список и пакетные операции через COM.
            new("InfobaseUsers", "IbUsers.MenuItem", GroupOperations,
                "IconUsers", "IconUsers", "#6366F1", UtilitiesMenuCommandKind.InfobaseUsers,
                isNew: true),
            // «Регламентные задания ИБ» (1.0.0.85): список, включение/выключение через COM.
            new("InfobaseScheduledJobs", "Jobs.MenuItem", GroupOperations,
                "CalendarClock", "IconScheduler", "#F59E0B", UtilitiesMenuCommandKind.InfobaseScheduledJobs,
                isNew: true),
            // «Версионирование конфигурации в Git (1.0.0.69)».
            new("GitVersioning", "Git.MenuItem", GroupOperations,
                "IconMerge", "IconMerge", "#F97316", UtilitiesMenuCommandKind.GitVersioning,
                isNew: true),
            // «Технологический журнал (1.0.0.69)».
            new("TechLog", "TechLog.MenuItem", GroupOperations,
                "IconMonitoring", "IconMonitoring", "#0EA5E9", UtilitiesMenuCommandKind.TechLog,
                isNew: true),
            // «Мониторинг и оповещения (1.0.0.69)».
            new("ServerAlerts", "Alerts.MenuItem", GroupOperations,
                "IconMonitoring", "IconMonitoring", "#EF4444", UtilitiesMenuCommandKind.ServerAlerts,
                isNew: true),
            // «Резервные копии средствами СУБД (1.0.0.69)».
            new("DbmsBackup", "Dbms.MenuItem", GroupOperations,
                "IconBackup", "IconBackup", "#16A34A", UtilitiesMenuCommandKind.DbmsBackup,
                isNew: true),
            // «Команды Telegram-бота (1.0.0.69)».
            new("TelegramBot", "Bot.MenuItem", GroupOperations,
                "IconPublish", "IconPublish", "#0EA5E9", UtilitiesMenuCommandKind.TelegramBot,
                isNew: true),
            // «Общие списки баз отдела (1.0.0.69)».
            new("SharedLists", "Shared.MenuItem", GroupOperations,
                "IconUsers", "IconUsers", "#3B82F6", UtilitiesMenuCommandKind.SharedLists,
                isNew: true),
            new("RepositoryBatchUpdate", "RepoUpdate.Title", GroupOperations,
                "SourceBranch", "IconCloudDownload", "#06B6D4", UtilitiesMenuCommandKind.RepositoryBatchUpdate),
            new("RepositoryBrowser", "RepositoryBrowser.Title", GroupOperations,
                "SourceBranch", "IconCloudDownload", "#8B5CF6", UtilitiesMenuCommandKind.RepositoryBrowser),
            new("ExportCsv", "Utilities.ExportCsv", GroupOperations,
                "FileDelimited", "IconFileExport", "#22C55E", UtilitiesMenuCommandKind.ExportBasesCsv),
            new("ExportHtml", "Utilities.ExportHtml", GroupOperations,
                "FileDocument", "IconFileDocument", "#F59E0B", UtilitiesMenuCommandKind.ExportBasesHtml),
            new("ExportJson", "Utilities.ExportJson", GroupOperations,
                "FileExport", "IconFileExport", "#06B6D4", UtilitiesMenuCommandKind.ExportBasesJson),
            new("ImportJson", "Utilities.ImportJson", GroupOperations,
                "DatabaseImport", "IconImport", "#06B6D4", UtilitiesMenuCommandKind.ImportBasesJson),
            // Экспорт/импорт в произвольный файл *.v8i (issue #29): те же операции, что
            // в настройках (вкладка «Базы» → «Список баз»), с теми же названиями и сервисами.
            new("ExportV8iFile", "Settings.Bases.ExportV8iFile", GroupOperations,
                "FileExport", "IconFileExport", "#22C55E", UtilitiesMenuCommandKind.ExportBasesV8iFile),
            new("ImportV8iFile", "Settings.Bases.ImportV8iFile", GroupOperations,
                "DatabaseImport", "IconImport", "#22C55E", UtilitiesMenuCommandKind.ImportBasesV8iFile),
            // «Импорт из кластера 1С» (issue #27) и «Миграция между кластерами 1С»
            // (issue #41, переработка): отдельные пункты — импорт остаётся импортом,
            // миграция вынесена в самостоятельное окно. Хоткея у миграции нет — это
            // не частая операция (комментарий 7OH).
            new("ClusterImport", "ClusterImport.MenuItem", GroupOperations,
                "DatabaseImport", "IconImport", "#06B6D4", UtilitiesMenuCommandKind.ImportClusterInfobases),
            // «Миграция между кластерами 1С» (issue #41, 1.0.0.66): флаг новинки,
            // чтобы при сохранённой пользовательской компоновке пункт попал в
            // «Новинки программы», пока пользователь не разместит его явно (issue #58).
            new("ClusterMigration", "ClusterMigration.MenuItem", GroupOperations,
                "DatabaseExport", "IconDatabaseExport", "#16A34A", UtilitiesMenuCommandKind.MigrateClusterInfobases,
                isNew: true),
            new("ProcessInspector", "ProcessInspector.Title", GroupOperations,
                "ApplicationCog", "IconApplicationCog", "#0EA5E9", UtilitiesMenuCommandKind.ProcessInspector),
            new("KillOneCProcesses", "Settings.Bases.KillProcesses", GroupOperations,
                "CloseNetwork", "IconClose", "#F59E0B", UtilitiesMenuCommandKind.KillOneCProcesses),
            new("ConnectionReplace", "Main.UtilitiesConnectionReplace", GroupOperations,
                "FindReplace", "IconFindReplace", "#F59E0B", UtilitiesMenuCommandKind.ConnectionReplace),
            new("UndoConnectionReplace", "Main.UtilitiesUndoConnectionReplace", GroupOperations,
                "UndoVariant", "IconUndo", "#F59E0B", UtilitiesMenuCommandKind.UndoConnectionReplace),
            new("RemoveMissingFileBases", "Settings.Bases.RemoveMissing", GroupOperations,
                "FolderRemove", "IconFolderRemove", "#EF4444", UtilitiesMenuCommandKind.RemoveMissingFileBases),

            // ---- Низ меню ----
            new("CheckForUpdates", "Settings.About.CheckForUpdates", GroupTopLevel,
                "Update", "IconUpdate", "#3B82F6", UtilitiesMenuCommandKind.CheckForUpdates)
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
    /// <summary>Стандартная компоновка меню «Утилиты» (текущая структура).
    /// Разделители — самостоятельные узлы <see cref="Separator"/> (issue #374):
    /// редактор показывает всю структуру «как есть», без скрытых флагов SeparatorBefore.</summary>
    public static UtilitiesMenuLayout Default()
    {
        var layout = new UtilitiesMenuLayout();
        layout.Root = new List<UtilitiesMenuNode>
        {
            Item("CommandPalette"),
            Separator(),
            Item("ActualReleases"),
            Item("PlatformUpdate"),
            Item("PlatformDownload"),
            Item("ServerMonitor"),
            // Управление службами (issue #11): под «Серверы 1С», как в каталоге.
            Item("Services"),
            Item("ServerConsoleExternal"),
            Separator(),
            Item("AppLock"),
            Group(UtilitiesMenuCatalog.GroupLockOptions, new List<UtilitiesMenuNode>
            {
                Item("UnlockPrivateBases"),
                Item("ChangeAppLock"),
                Separator(),
                Item("RunBackupScenario")
            }),
            Separator(),
            Group(UtilitiesMenuCatalog.GroupInformation, new List<UtilitiesMenuNode>
            {
                Item("MaintenanceCenter"),
                Separator(),
                Item("ConfigTypesEdit"),
                Item("ItsAccounts"),
                Item("NotificationChannels"),
                Separator(),
                Item("ScheduledTasks"),
                Item("BackupScenarios"),
                Separator(),
                Item("ScriptsSettings"),
                Item("UsageStatistics")
            }),
            Separator(),
            Group(UtilitiesMenuCatalog.GroupOperations, new List<UtilitiesMenuNode>
            {
                Item("ConfigDiff"),
                Item("MetadataExplorer"),
                Item("EventLog"),
                Item("ConfigExtensions"),
                Item("InfobaseUsers"),
                Item("InfobaseScheduledJobs"),
                Item("GitVersioning"),
                Item("TechLog"),
                Item("ServerAlerts"),
                Item("DbmsBackup"),
                Item("TelegramBot"),
                Item("SharedLists"),
                Separator(),
                Item("RepositoryBatchUpdate"),
                Item("RepositoryBrowser"),
                Separator(),
                Item("ExportCsv"),
                Item("ExportHtml"),
                Item("ExportJson"),
                Item("ImportJson"),
                // Экспорт/импорт в произвольный файл *.v8i (issue #29): те же операции,
                // что в настройках (вкладка «Базы» → «Список баз»).
                Item("ExportV8iFile"),
                Item("ImportV8iFile"),
                Item("ClusterImport"),
                Item("ClusterMigration"),
                Separator(),
                Item("ProcessInspector"),
                Item("KillOneCProcesses"),
                Separator(),
                Item("ConnectionReplace"),
                Item("UndoConnectionReplace"),
                Item("RemoveMissingFileBases")
            }),
            Separator(),
            Item("CheckForUpdates")
        };
        return layout;
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