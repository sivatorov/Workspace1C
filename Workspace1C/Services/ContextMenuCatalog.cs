using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Вид команды пункта контекстного меню базы (issue #56): каталог хранит только
/// метаданные пункта, а фактическую команду/действие резолвит построитель меню
/// платформы (WPF/Avalonia), у которого есть доступ к <c>MainViewModel</c> и
/// обработчикам окна.
/// </summary>
public enum ContextMenuCommandKind
{
    /// <summary>Запуск 1С:Предприятия для выбранной базы.</summary>
    LaunchEnterprise,

    /// <summary>Запуск 1С:Конфигуратора для выбранной базы.</summary>
    LaunchConfigurator,

    /// <summary>Выполнить сценарий запуска скрипта для базы (issue #308).</summary>
    RunScript,

    /// <summary>Подменю «Пользовательские действия…» (функция 7; динамическое наполнение).</summary>
    CustomActions,

    /// <summary>Добавить/убрать базу из избранного.</summary>
    ToFavorites,

    /// <summary>Закрепить базу вверху списка.</summary>
    Pin,

    /// <summary>Добавить новую базу.</summary>
    AddBase,

    /// <summary>Блок пакетных операций «Для выделенных (N)…» (мультивыделение).</summary>
    BatchOperations,

    /// <summary>Обновить информацию о конфигурации базы.</summary>
    RefreshConfigInfo,

    /// <summary>Проверить обновления конфигурации (F9).</summary>
    CheckUpdate,

    /// <summary>Привязка базы к конфигурации («Актуальные релизы»).</summary>
    ConfigLink,

    /// <summary>Блокировка сеансов информационной базы.</summary>
    SessionLock,

    /// <summary>Регистрация COM-коннектора (только Windows/WPF).</summary>
    RegisterComConnector,

    /// <summary>Диагностика сети до сервера 1С (функция 12).</summary>
    NetworkDiagnostics,

    /// <summary>Открыть каталог файловой базы.</summary>
    OpenInfobaseFolder,

    /// <summary>Дублирование базы (копия каталога / клон на сервере).</summary>
    CloneBase,

    /// <summary>Проверка целостности файловой ИБ (chdbfl).</summary>
    CheckIntegrity,

    /// <summary>Публикация на веб-сервере (перенесена из «Утилиты», issue #63).</summary>
    WebPublish,

    /// <summary>История запусков базы.</summary>
    LaunchHistory,

    /// <summary>Выгрузка базы в файл .dt.</summary>
    DumpToDt,

    /// <summary>Выгрузка конфигурации в файл .cf.</summary>
    DumpConfigToCf,

    /// <summary>Список выгрузок (Ctrl+Shift+F7).</summary>
    ShowExportsList,

    /// <summary>Очистка программного кэша.</summary>
    ClearProgramCache,

    /// <summary>Очистка пользовательского кэша.</summary>
    ClearUserCache,

    /// <summary>Очистка программного и пользовательского кэша (Ctrl+Shift+C).</summary>
    ClearCacheBoth,

    /// <summary>Открыть папку программного кеша выбранной базы (issue #110).</summary>
    OpenProgramCacheFolder,

    /// <summary>Открыть папку пользовательского кеша выбранной базы (issue #110).</summary>
    OpenUserCacheFolder,

    /// <summary>Удалить базу из списка.</summary>
    Delete,

    /// <summary>Найти базу в общем списке «Все базы» (issue #285).</summary>
    FindInList,

    /// <summary>Создать ярлык на рабочем столе.</summary>
    CreateDesktopShortcut,

    /// <summary>Копировать строку подключения базы.</summary>
    CopyConnectionString,

    /// <summary>Изменить настройки базы.</summary>
    EditSettings,

    /// <summary>Заметки базы (п.8, 1.0.0.90): markdown-карточка и секреты внешних сервисов.</summary>
    Notes,

    /// <summary>
    /// «Развернуть тестовую копию» (п.10, 1.0.0.92): мастер параметров тестовой базы
    /// (имя, сервер/файл, движок выгрузки, регламенты, заголовок/цвет, группа «Тест»,
    /// расписание) с выполнением через <c>TestDeployService</c>.
    /// </summary>
    TestDeploy,

    /// <summary>
    /// «Массовая загрузка cf через Конфигуратор» (issue #106, автор aepkugesi):
    /// окно-чеклист баз (группа/мультивыделение), файл .cf и опции перед загрузкой
    /// (выгрузка .dt, завершение сеансов, блокировка начала сеансов, блокировка
    /// регламентных заданий). Загрузка — Конфигуратор /LoadCfg + /UpdateDBCfg.
    /// </summary>
    MassLoadCf
}

/// <summary>
/// Пункт каталога контекстного меню базы (issue #56): стабильные метаданные,
/// не зависящие от платформы. Иконки задаются парами «ключ для WPF (PackIcon Kind)
/// / ключ для Avalonia (Icon*)», потому что наборы иконок двух UI-фреймворков
/// не совпадают.
/// </summary>
public sealed class ContextMenuItem
{
    /// <param name="id">Стабильный идентификатор пункта (сохраняется в файле компоновки).</param>
    /// <param name="textKey">Ключ локализации заголовка.</param>
    /// <param name="defaultGroup">Стандартная группа: <see cref="ContextMenuCatalog.GroupTopLevel"/> и др.</param>
    /// <param name="wpfIconKind">Ключ иконки materialDesign PackIcon (WPF).</param>
    /// <param name="avaloniaIconKey">Ключ иконки Icon* из Themes/Icons.axaml (Avalonia).</param>
    /// <param name="iconColor">Цвет иконки #RRGGBB.</param>
    /// <param name="commandKind">Вид команды для построителя меню.</param>
    /// <param name="hotkeyKey">Имя свойства MainViewModel с хоткеем (или null).</param>
    /// <param name="linuxOnly">Пункт есть только в меню Avalonia/Linux.</param>
    /// <param name="isNew">Признак «новинка»: при сохранённой компоновке попадает в «Новинки программы».</param>
    /// <param name="essential">Критичный пункт: не может быть потерян из компоновки
    /// («Пользовательские действия», «Для выделенных» — от них зависит функция 7
    /// и пакетные операции). Резолвер/редактор гарантируют его наличие.</param>
    public ContextMenuItem(string id, string textKey, string defaultGroup,
        string wpfIconKind, string avaloniaIconKey, string iconColor,
        ContextMenuCommandKind commandKind, string? hotkeyKey = null,
        bool linuxOnly = false, bool isNew = false, bool essential = false)
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
        Essential = essential;
    }

    /// <summary>Стабильный идентификатор пункта.</summary>
    public string Id { get; }

    /// <summary>Ключ локализации заголовка.</summary>
    public string TextKey { get; }

    /// <summary>Стандартная группа (TopLevel / UpdateAndLink / Administration / Backup / ClearCache).</summary>
    public string DefaultGroup { get; }

    /// <summary>Ключ иконки materialDesign PackIcon (WPF).</summary>
    public string WpfIconKind { get; }

    /// <summary>Ключ иконки Icon* из Themes/Icons.axaml (Avalonia).</summary>
    public string AvaloniaIconKey { get; }

    /// <summary>Цвет иконки #RRGGBB.</summary>
    public string IconColor { get; }

    /// <summary>Вид команды для построителя меню.</summary>
    public ContextMenuCommandKind CommandKind { get; }

    /// <summary>Имя свойства MainViewModel с хоткеем (или null).</summary>
    public string? HotkeyKey { get; }

    /// <summary>Пункт есть только в меню Avalonia/Linux.</summary>
    public bool LinuxOnly { get; }

    /// <summary>Признак «новинка» (для подменю «Новинки программы»).</summary>
    public bool IsNew { get; }

    /// <summary>Критичный пункт: Essential-пункты нельзя потерять из компоновки
    /// (см. <see cref="ContextMenuResolver"/>).</summary>
    public bool Essential { get; }
}

/// <summary>
/// Каталог всех известных пунктов контекстного меню базы (issue #56) — единый
/// источник истины о составе меню. Новые пункты, добавляемые в будущих версиях,
/// помечаются <see cref="ContextMenuItem.IsNew"/> и при сохранённой пользовательской
/// компоновке автоматически попадают в подменю «Новинки программы».
/// </summary>
public static class ContextMenuCatalog
{
    /// <summary>Стандартная группа: верхний уровень меню.</summary>
    public const string GroupTopLevel = "TopLevel";

    /// <summary>Стандартная группа: подменю «Обновление и связь».</summary>
    public const string GroupUpdateAndLink = "UpdateAndLink";

    /// <summary>Стандартная группа: подменю «Администрирование».</summary>
    public const string GroupAdministration = "Administration";

    /// <summary>Стандартная группа: подменю «Резервирование».</summary>
    public const string GroupBackup = "Backup";

    /// <summary>Стандартная группа: подменю «Очистить кэш».</summary>
    public const string GroupClearCache = "ClearCache";

    /// <summary>Служебная группа: «Новинки программы» (авто-добавление новых пунктов).</summary>
    public const string GroupNewItems = "NewItems";

    /// <summary>Ключ локализации заголовка стандартной группы.</summary>
    public static string GroupTitleKey(string groupId) => groupId switch
    {
        GroupUpdateAndLink => "Menu.UpdateAndLink",
        GroupAdministration => "Menu.Administration",
        GroupBackup => "Menu.Backup",
        GroupClearCache => "Main.ClearCache",
        GroupNewItems => "ContextMenu.Submenu.NewItems",
        _ => string.Empty
    };

    /// <summary>Ключ иконки стандартной группы для WPF (PackIcon Kind); пусто — без иконки.</summary>
    public static string GroupWpfIconKind(string groupId) => groupId switch
    {
        GroupUpdateAndLink => "Refresh",
        GroupAdministration => "Server",
        GroupBackup => "DatabaseExport",
        GroupClearCache => "Broom",
        GroupNewItems => "NewBox",
        _ => string.Empty
    };

    /// <summary>Ключ иконки стандартной группы для Avalonia (Icon*); пусто — без иконки.</summary>
    public static string GroupAvaloniaIconKey(string groupId) => groupId switch
    {
        GroupUpdateAndLink => "IconRefresh",
        GroupAdministration => "IconServer",
        GroupBackup => "IconDatabaseExport",
        GroupClearCache => "IconBroom",
        GroupNewItems => "IconNewBox",
        _ => string.Empty
    };

    /// <summary>Цвет иконки стандартной группы (#RRGGBB).</summary>
    public static string GroupIconColor(string groupId) => groupId switch
    {
        GroupUpdateAndLink => "#14B8A6",
        GroupAdministration => "#EF4444",
        GroupBackup => "#0EA5E9",
        GroupClearCache => "#14B8A6",
        GroupNewItems => "#22C55E",
        _ => string.Empty
    };

    /// <summary>Все известные пункты контекстного меню в порядке стандартной компоновки.</summary>
    public static IReadOnlyList<ContextMenuItem> All { get; } = BuildAll();

    /// <summary>Возвращает пункт каталога по идентификатору (или null).</summary>
    public static ContextMenuItem? Find(string? id)
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

    /// <summary>Возвращает пункт каталога по виду команды (или null).</summary>
    public static ContextMenuItem? FindByCommand(ContextMenuCommandKind kind)
    {
        foreach (var item in All)
        {
            if (item.CommandKind == kind)
                return item;
        }
        return null;
    }

    /// <summary>
    /// Применим ли пункт к ВЫБРАННОЙ ГРУППЕ дерева (issue #105, запрос 7OH): для группы
    /// показываются только команды, которые реально работают с группой — «Для выделенных
    /// (N)…» (пакетные операции, включая резервные копии и скрипты по базам группы),
    /// «Пользовательские действия…» (контекст «Группа»), «Добавить базу» (в выбранную
    /// группу), «Очистить кэш» (с предвыбором баз группы), «Удалить» (группу) и
    /// «Изменить настройки» (группы). Остальные команды работают только с выделенной
    /// базой и для группы скрываются (не блокируются).
    /// </summary>
    public static bool IsGroupApplicable(ContextMenuCommandKind kind) => kind switch
    {
        ContextMenuCommandKind.BatchOperations or
        ContextMenuCommandKind.CustomActions or
        ContextMenuCommandKind.AddBase or
        ContextMenuCommandKind.ClearProgramCache or
        ContextMenuCommandKind.ClearUserCache or
        ContextMenuCommandKind.ClearCacheBoth or
        // Открытие папок кеша (issue #110): работает и для баз группы.
        ContextMenuCommandKind.OpenProgramCacheFolder or
        ContextMenuCommandKind.OpenUserCacheFolder or
        ContextMenuCommandKind.Delete or
        ContextMenuCommandKind.EditSettings or
        // «Массовая загрузка cf» (issue #106): работает и с группой — цели
        // собираются как в пакетных операциях («Для группы (N)…»).
        ContextMenuCommandKind.MassLoadCf => true,
        _ => false
    };

    private static IReadOnlyList<ContextMenuItem> BuildAll()
    {
        return new List<ContextMenuItem>
        {
            // ---- Верхний уровень: запуск и действия с базой ----
            new("LaunchEnterprise", "Main.LaunchEnterprise", GroupTopLevel,
                "Play", "IconPlay", "#22C55E", ContextMenuCommandKind.LaunchEnterprise, "HotkeyEnterprise"),
            new("LaunchConfigurator", "Main.LaunchConfigurator", GroupTopLevel,
                "Cog", "IconSettings", "#3B82F6", ContextMenuCommandKind.LaunchConfigurator, "HotkeyConfigurator"),
            // «Выполнить скрипт» (issue #308): запуск выбранного сценария для базы.
            new("RunScript", "Script.RunTitle", GroupTopLevel,
                "CodeBraces", "IconScript", "#06B6D4", ContextMenuCommandKind.RunScript, "HotkeyRunScript"),
            // Пользовательские действия (функция 7): подменю динамическое (заполняется
            // при открытии меню), но сам пункт обязан оставаться в компоновке — Essential.
            new("CustomActions", "Main.CustomActionsMenu", GroupTopLevel,
                "CodeBraces", "IconScript", "#06B6D4", ContextMenuCommandKind.CustomActions, essential: true),
            new("ToFavorites", "Main.ToFavorites", GroupTopLevel,
                "Star", "IconStar", "#FBBF24", ContextMenuCommandKind.ToFavorites, "HotkeyFavorite"),
            new("Pin", "Main.Pin", GroupTopLevel,
                "Pin", "IconPin", "#8B5CF6", ContextMenuCommandKind.Pin, "HotkeyPin"),
            new("AddBase", "Main.AddBase", GroupTopLevel,
                "Plus", "IconAdd", "#22C55E", ContextMenuCommandKind.AddBase, "HotkeyAdd"),
            // Мультивыделение (0.3.9.90): блок «Для выделенных (N)…» — Essential:
            // без него недоступны пакетные операции.
            new("BatchOperations", "Main.BatchForSelected", GroupTopLevel,
                "SelectionMultiple", "IconTag", "#8B5CF6", ContextMenuCommandKind.BatchOperations, essential: true),

            // ---- Обновление и связь (issue #287) ----
            new("RefreshConfigInfo", "Main.RefreshConfigInfo", GroupUpdateAndLink,
                "CloudDownload", "IconCloudDownload", "#14B8A6", ContextMenuCommandKind.RefreshConfigInfo),
            // Проверка обновлений конфигураций 1С (функции №21/№22): F9.
            new("CheckUpdate", "Updates.CheckTitle", GroupUpdateAndLink,
                "CloudDownload", "IconCloudDownload", "#14B8A6", ContextMenuCommandKind.CheckUpdate, "HotkeyCheckUpdate"),
            // «Актуальные релизы» — привязка базы к конфигурации (в XAML без иконки).
            new("ConfigLink", "Updates.ConfigLink", GroupUpdateAndLink,
                string.Empty, string.Empty, string.Empty, ContextMenuCommandKind.ConfigLink),

            // ---- Администрирование (issue #287) ----
            // Блокировка сеансов файловой ИБ (функция №20, Ctrl+Alt+L).
            new("SessionLock", "SessionLock.Title", GroupAdministration,
                "ShieldLockOutline", "IconRights", "#EF4444", ContextMenuCommandKind.SessionLock, "HotkeySessionLock"),
            // Регистрация COM-коннектора — только Windows/WPF; на Linux пункт
            // не строится (платформенный фильтр построителя Avalonia).
            new("RegisterComConnector", "Main.RegisterComConnector", GroupAdministration,
                "Key", "IconAccountKey", "#14B8A6", ContextMenuCommandKind.RegisterComConnector),
            // Диагностика сети до сервера 1С (функция 12).
            new("NetworkDiagnostics", "Main.NetworkDiagnostics", GroupAdministration,
                "NetworkOutline", "IconNetwork", "#3B82F6", ContextMenuCommandKind.NetworkDiagnostics),
            new("OpenInfobaseFolder", "Main.OpenCatalog", GroupAdministration,
                "FolderOpen", "IconFolderOpen", "#0EA5E9", ContextMenuCommandKind.OpenInfobaseFolder),
            // Дублирование базы (0.3.9.100, функция №10).
            new("CloneBase", "Clone.Title", GroupAdministration,
                "ContentCopy", "IconCopy", "#14B8A6", ContextMenuCommandKind.CloneBase),
            // «Развернуть тестовую копию» (п.10, 1.0.0.92): мастер параметров тестовой
            // базы; новый пункт — при сохранённой компоновке попадёт в «Новинки приложения».
            new("TestDeploy", "TestDeploy.MenuItem", GroupAdministration,
                "CloudUpload", "IconCloudUpload", "#22C55E", ContextMenuCommandKind.TestDeploy,
                isNew: true),
            // Администрирование ИБ (Этап 6, функция №29): проверка целостности (chdbfl).
            new("CheckIntegrity", "Admin.CheckIntegrity", GroupAdministration,
                "DatabaseCheck", "IconDatabase", "#10B981", ContextMenuCommandKind.CheckIntegrity, "HotkeyCheckIntegrity"),
            // Публикация на веб-сервере (перенесена из «Утилиты» в контекстное меню базы — issue #63).
            new("WebPublish", "WebPublish.MenuItem", GroupAdministration,
                "IconWeb", "IconWeb", "#22C55E", ContextMenuCommandKind.WebPublish,
                isNew: true),
            new("LaunchHistory", "Main.LaunchHistory", GroupAdministration,
                "History", "IconHistory", "#8B5CF6", ContextMenuCommandKind.LaunchHistory),

            // ---- Резервирование (issue #287) ----
            new("DumpToDt", "Main.DumpToDt", GroupBackup,
                "DatabaseExport", "IconDatabaseExport", "#0EA5E9", ContextMenuCommandKind.DumpToDt),
            new("DumpConfigToCf", "Main.DumpConfigToCf", GroupBackup,
                "FileExport", "IconFileExport", "#3B82F6", ContextMenuCommandKind.DumpConfigToCf),
            // «Список выгрузок» (функция №18, Ctrl+Shift+F7).
            new("ShowExportsList", "Restore.Title", GroupBackup,
                "DatabaseImport", "IconDatabaseImport", "#3B82F6", ContextMenuCommandKind.ShowExportsList, "HotkeyExportsList"),
            // «Массовая загрузка cf через Конфигуратор» (issue #106): новый пункт —
            // при сохранённой компоновке попадёт в «Новинки приложения».
            new("MassLoadCf", "Main.BatchLoadCfCfg", GroupBackup,
                "DatabaseImport", "IconDatabaseImport", "#EF4444", ContextMenuCommandKind.MassLoadCf,
                isNew: true),

            // ---- Очистить кэш ----
            new("ClearProgramCache", "Main.ClearProgramCache", GroupClearCache,
                "Broom", "IconBroom", "#14B8A6", ContextMenuCommandKind.ClearProgramCache),
            new("ClearUserCache", "Main.ClearUserCache", GroupClearCache,
                "Broom", "IconBroom", "#14B8A6", ContextMenuCommandKind.ClearUserCache),
            // Хоткей Ctrl+Shift+C показан на пункте «Программный и пользовательский».
            new("ClearCacheBoth", "Main.ClearCacheBoth", GroupClearCache,
                "Broom", "IconBroom", "#14B8A6", ContextMenuCommandKind.ClearCacheBoth, "HotkeyClearCache"),
            // «Открыть папку кеша» (issue #110): все найденные каталоги кеша базы
            // открываются в проводнике/файловом менеджере; новые пункты — при
            // сохранённой компоновке попадают в «Новинки приложения».
            new("OpenProgramCacheFolder", "Main.OpenProgramCacheFolder", GroupClearCache,
                "FolderOpen", "IconFolderOpen", "#0EA5E9", ContextMenuCommandKind.OpenProgramCacheFolder,
                isNew: true),
            new("OpenUserCacheFolder", "Main.OpenUserCacheFolder", GroupClearCache,
                "FolderOpen", "IconFolderOpen", "#0EA5E9", ContextMenuCommandKind.OpenUserCacheFolder,
                isNew: true),

            // ---- Низ меню ----
            // «Удалить» отделён разделителями сверху и снизу (issue #242).
            new("Delete", "Main.Delete", GroupTopLevel,
                "Delete", "IconDelete", "#EF4444", ContextMenuCommandKind.Delete, "HotkeyDelete"),
            // «Найти в списке»: переход к базе в общем списке (issue #285).
            new("FindInList", "Main.FindInList", GroupTopLevel,
                "Magnify", "IconSearch", "#3B82F6", ContextMenuCommandKind.FindInList, "HotkeyFindInList"),
            // «Создать ярлык» — недавняя функция; в дефолтной компоновке уже размещён,
            // а в пользовательской (до явного размещения) попадает в «Новинки программы».
            new("CreateDesktopShortcut", "Main.DesktopShortcut", GroupTopLevel,
                "DesktopClassic", "IconDesktopClassic", "#6366F1", ContextMenuCommandKind.CreateDesktopShortcut,
                isNew: true),
            new("CopyConnectionString", "Main.CopyConnectionString", GroupTopLevel,
                "ContentCopy", "IconCopy", "#06B6D4", ContextMenuCommandKind.CopyConnectionString),
            // «Изменить настройки» (аналог «Свойств» в ОС) размещён внизу меню (issue #242).
            new("EditSettings", "Main.EditSettings", GroupTopLevel,
                "Pencil", "IconEdit", "#3B82F6", ContextMenuCommandKind.EditSettings, "HotkeyEdit"),
            // «Заметки базы» (п.8, 1.0.0.90): markdown-карточка + секреты внешних сервисов.
            new("Notes", "Main.Notes", GroupTopLevel,
                "Note", "IconNote", "#8B5CF6", ContextMenuCommandKind.Notes,
                isNew: true),
        };
    }
}

/// <summary>
/// Построитель стандартной компоновки контекстного меню базы: воспроизводит текущую
/// статическую структуру (порядок, группы, разделители) обеих платформ, чтобы при
/// выключенной правке меню выглядело ровно как до появления настройки (обратная
/// совместимость).
/// </summary>
public static class ContextMenuLayouts
{
    /// <summary>
    /// Стандартная компоновка контекстного меню базы (текущая структура).
    /// Разделители — самостоятельные узлы <see cref="Separator"/> (issue #374):
    /// редактор показывает всю структуру «как есть», без скрытых флагов SeparatorBefore.
    /// Пункты «Пользовательские действия» и «Для выделенных (N)…» помечены Essential —
    /// резолвер/редактор гарантируют их наличие в компоновке.
    /// </summary>
    public static ContextMenuLayout Default()
    {
        var layout = new ContextMenuLayout();
        layout.Root = new List<ContextMenuNode>
        {
            Item("LaunchEnterprise"),
            Item("LaunchConfigurator"),
            Item("RunScript"),
            // Разделитель перед «Пользовательскими действиями» управляется вместе
            // с подменю (скрывается, когда действий нет) — как в XAML
            // (CustomActionsSeparator). Разделитель после подменю — обычный.
            Separator(),
            Item("CustomActions"),
            Separator(),
            Item("ToFavorites"),
            Item("Pin"),
            Separator(),
            Item("AddBase"),
            Item("BatchOperations"),
            Separator(),
            Group(ContextMenuCatalog.GroupUpdateAndLink, new List<ContextMenuNode>
            {
                Item("RefreshConfigInfo"),
                Item("CheckUpdate"),
                Separator(),
                Item("ConfigLink")
            }),
            Group(ContextMenuCatalog.GroupAdministration, new List<ContextMenuNode>
            {
                Item("SessionLock"),
                Separator(),
                Item("RegisterComConnector"),
                Item("NetworkDiagnostics"),
                Separator(),
                Item("OpenInfobaseFolder"),
                Item("CloneBase"),
                Item("TestDeploy"),
                Item("CheckIntegrity"),
                Separator(),
                Item("WebPublish"),
                Item("LaunchHistory")
            }),
            Group(ContextMenuCatalog.GroupBackup, new List<ContextMenuNode>
            {
                Item("DumpToDt"),
                Item("DumpConfigToCf"),
                Separator(),
                Item("ShowExportsList"),
                Item("MassLoadCf")
            }),
            Group(ContextMenuCatalog.GroupClearCache, new List<ContextMenuNode>
            {
                Item("ClearProgramCache"),
                Item("ClearUserCache"),
                Separator(),
                Item("ClearCacheBoth"),
                // Открытие папок кеша (issue #110) — после пунктов очистки.
                Separator(),
                Item("OpenProgramCacheFolder"),
                Item("OpenUserCacheFolder")
            }),
            Separator(),
            Item("Delete"),
            Separator(),
            Item("FindInList"),
            Item("CreateDesktopShortcut"),
            Item("CopyConnectionString"),
            Separator(),
            Item("EditSettings"),
            // Заметки базы (п.8, 1.0.0.90) — внизу меню, рядом с настройками базы.
            Item("Notes")
        };
        return layout;
    }

    /// <summary>Узел-пункт каталога.</summary>
    public static ContextMenuNode Item(string itemId, bool separatorBefore = false)
    {
        var node = new ContextMenuNode
        {
            Kind = "item",
            ItemId = itemId,
            SeparatorBefore = separatorBefore
        };
        return node;
    }

    /// <summary>Узел-группа (подменю).</summary>
    public static ContextMenuNode Group(string groupId, List<ContextMenuNode> children,
        bool separatorBefore = false)
    {
        var node = new ContextMenuNode
        {
            Kind = "group",
            GroupId = groupId,
            Children = children,
            SeparatorBefore = separatorBefore
        };
        return node;
    }

    /// <summary>Узел-разделитель (issue #374): самостоятельный элемент дерева компоновки.</summary>
    public static ContextMenuNode Separator() => new() { Kind = "separator" };
}