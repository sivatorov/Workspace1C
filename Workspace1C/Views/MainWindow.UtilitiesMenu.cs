#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// WPF-построитель меню «Утилиты» (issue #374): динамическое построение ContextMenu
/// по каталогу <see cref="UtilitiesMenuCatalog"/> + компоновке <see cref="UtilitiesMenuLayout"/>,
/// полученной из <see cref="UtilitiesMenuResolver.Resolve"/>. Заменяет статическое
/// XAML-меню в <c>MainWindow.xaml</c> (обратная совместимость: при выключенной правке
/// структуры меню компоновка — стандартная, и меню выглядит ровно как раньше).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Строит меню «Утилиты» верхней панели по каталогу+компоновке (issue #374).
    /// Правка структуры выключена (по умолчанию) — стандартная компоновка;
    /// включена — пользовательская из <c>utilities_menu_layout.json</c>. Новые пункты
    /// программы (признак «новинка»), не размещённые в компоновке, автоматически
    /// попадают в подменю «Новинки программы» в конце меню.
    /// </summary>
    private ContextMenu BuildUtilitiesMenu()
    {
        var menu = new ContextMenu
        {
            Style = TryFindResource("ModernContextMenu") as Style,
            DataContext = DataContext,
        };
        if (_viewModel is null)
            return menu;

        var store = AppServices.TryGetService<IUtilitiesMenuLayoutStore>();
        var settings = new AppSettings();
        settings.UtilitiesMenuEditingEnabled = _viewModel.UtilitiesMenuEditingEnabled;
        // «Новинки программы» (issue #4): группа автоматически добавляется в компоновку
        // (даже если её нет в файле), в неё попадают пункты, отсутствующие в файле.
        // Видимость группы зафиксирована — скрытие в редакторе не влияет на отображение.
        var layout = UtilitiesMenuResolver.EnsureNewItemsGroup(UtilitiesMenuResolver.Resolve(settings, store));
        if (layout is null)
            return menu;

        AppendNodes(menu.Items, layout.Root);

        return menu;
    }

    /// <summary>Рекурсивно добавляет узлы компоновки в целевой список пунктов меню.</summary>
    private void AppendNodes(ItemCollection target, List<UtilitiesMenuNode> nodes)
    {
        if (nodes is null || target is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null || !node.Visible)
                continue;
            if (node.SeparatorBefore && target.Count > 0)
                target.Add(new Separator());
            if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
            {
                // Узел-разделитель (issue #374): отдельный элемент дерева компоновки.
                if (target.Count > 0)
                    target.Add(new Separator());
                continue;
            }
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                var sub = new MenuItem
                {
                    Header = GroupTitle(node),
                    Style = TryFindResource("ModernMenuItem") as Style,
                };
                sub.Icon = GroupIcon(node);
                AppendNodes(sub.Items, node.Children);
                target.Add(sub);
            }
            else
            {
                var item = CreateItem(node);
                if (item is not null)
                    target.Add(item);
            }
        }
    }

    /// <summary>Заголовок группы: пользовательский или локализованный стандартный.</summary>
    private string GroupTitle(UtilitiesMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.CustomTitle))
            return node.CustomTitle!;
        var key = UtilitiesMenuCatalog.GroupTitleKey(node.GroupId ?? string.Empty);
        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationManager.T(key);
    }

    /// <summary>Иконка группы: внешний файл → пользовательская → стандартная каталога.</summary>
    private object? GroupIcon(UtilitiesMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.ExternalIconPath))
            return CreateExternalIcon(node.ExternalIconPath!);
        var isNewItems = string.Equals(node.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal);
        // issue #81 (финальный фикс): у группы «Новинки программы» меню «Утилиты» — ТА ЖЕ
        // иконка, что в контекстном меню (materialDesign PackIcon NewBox, #22C55E), всегда,
        // независимо от сохранённых в старых layout легаси-иконок («папка», «звезда») —
        // иначе реальное меню показывает звезду вместо NEW (комментарий 7OH). Рисуем её
        // тем же PackIcon, что и контекстное меню (MainWindow.ContextMenu.cs → CreateRowMenuIcon),
        // чтобы иконка была идентичной во всех наборах иконок. Внешний файл иконки сохраняется.
        if (isNewItems)
        {
            return CreateRowMenuIcon(
                UtilitiesMenuCatalog.GroupWpfIconKind(UtilitiesMenuCatalog.GroupNewItems),
                UtilitiesMenuCatalog.GroupIconColor(UtilitiesMenuCatalog.GroupNewItems));
        }
        var iconKey = !string.IsNullOrWhiteSpace(node.IconKey)
            ? node.IconKey!
            : UtilitiesMenuCatalog.GroupWpfIconKind(node.GroupId ?? string.Empty);
        var color = !string.IsNullOrWhiteSpace(node.IconColor)
            ? node.IconColor!
            : UtilitiesMenuCatalog.ResolveGroupIconColor(node.GroupId ?? string.Empty, ActiveScheme, IsDarkTheme);
        return CreateMenuIcon(iconKey, color);
    }

    /// <summary>Создаёт пункт меню по узлу-ссылке на пункт каталога (null — неизвестный пункт).</summary>
    private MenuItem? CreateItem(UtilitiesMenuNode node)
    {
        var catalogItem = UtilitiesMenuCatalog.Find(node.ItemId);
        if (catalogItem is null)
            return null;
        // Пункты только для Avalonia/Linux в WPF-меню не показываются.
        if (catalogItem.LinuxOnly)
            return null;

        var item = new MenuItem
        {
            Header = LocalizationManager.T(catalogItem.TextKey),
            Style = TryFindResource("ModernMenuItem") as Style,
        };
        item.Icon = ItemIcon(node, catalogItem);
        var hotkey = HotkeyFor(catalogItem.HotkeyKey);
        if (!string.IsNullOrWhiteSpace(hotkey))
            item.InputGestureText = hotkey!;
        ApplyCommand(item, catalogItem);
        return item;
    }

    /// <summary>Иконка пункта: внешний файл → пользовательская → из каталога.</summary>
    private object? ItemIcon(UtilitiesMenuNode node, UtilitiesMenuItem catalogItem)
    {
        if (!string.IsNullOrWhiteSpace(node.ExternalIconPath))
            return CreateExternalIcon(node.ExternalIconPath!);
        var iconKey = !string.IsNullOrWhiteSpace(node.IconKey)
            ? node.IconKey!
            : catalogItem.WpfIconKind;
        var color = !string.IsNullOrWhiteSpace(node.IconColor)
            ? node.IconColor!
            : UtilitiesMenuCatalog.ResolveItemIconColor(catalogItem, ActiveScheme, IsDarkTheme);
        return CreateMenuIcon(iconKey, color);
    }

    /// <summary>
    /// Иконка меню 16×16 по ключу (PackIcon-имя каталога или уже ключ геометрии <c>Icon*</c>).
    /// Геометрия резолвится через <see cref="Converters.IconKeyToGeometryConverter.ResolveGeometry"/>
    /// с учётом активного набора иконок (Задача 5): для Outline — контурный вариант, при его
    /// отсутствии — Fluent-геометрия. Цвет — из активной схемы (см. ParseMenuIconColor).
    /// </summary>
    private Path? CreateMenuIcon(string iconKey, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return null;
        var geometryKey = UtilitiesMenuCatalog.WpfIconGeometryKey(iconKey);
        if (string.IsNullOrWhiteSpace(geometryKey))
            return null;
        var geometry = Converters.IconKeyToGeometryConverter.ResolveGeometry(geometryKey);
        if (geometry is null || geometry.IsEmpty())
            return null;
        return new Path
        {
            Data = geometry,
            Fill = ParseMenuIconColor(colorHex),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>Изображение из внешнего файла (иконка «извне», назначается в редакторе структуры).</summary>
    private object? CreateExternalIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var uri = new Uri("file:///" + path.Replace("\\", "/"));
            var source = new BitmapImage(uri);
            return new Image { Source = source, Width = 16, Height = 16 };
        }
        catch
        {
            // Нечитаемый/отсутствующий файл иконки не должен ронять построение меню.
            return null;
        }
    }

    /// <summary>Цвет иконки: разбор #RRGGBB; при сбое — серый (как у «Без группы»).</summary>
    private Brush ParseMenuIconColor(string colorHex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(colorHex))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }
        catch { /* невалидный цвет — серый */ }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
    }

    /// <summary>Хоткей пункта по имени свойства MainViewModel (из каталога); null — нет хоткея.</summary>
    private string? HotkeyFor(string? hotkeyKey)
    {
        if (string.IsNullOrWhiteSpace(hotkeyKey))
            return null;
        switch (hotkeyKey)
        {
            case "HotkeyCommandPalette": return _viewModel.HotkeyCommandPalette;
            case "HotkeyActualReleases": return _viewModel.HotkeyActualReleases;
            case "HotkeyPlatformUpdate": return _viewModel.HotkeyPlatformUpdate;
            case "HotkeyServerConsole": return _viewModel.HotkeyServerConsole;
            case "HotkeyServices": return _viewModel.HotkeyServices;
            case "HotkeyLockApp": return _viewModel.HotkeyLockApp;
            case "HotkeyRunBackup": return _viewModel.HotkeyRunBackup;
            default: return null;
        }
    }

    /// <summary>Привязывает команду/действие к пункту меню по виду команды каталога.</summary>
    private void ApplyCommand(MenuItem item, UtilitiesMenuItem catalogItem)
    {
        switch (catalogItem.CommandKind)
        {
            case UtilitiesMenuCommandKind.CommandPalette:
                item.Command = _viewModel.CommandPaletteCommand; break;
            case UtilitiesMenuCommandKind.ShowActualReleases:
                item.Command = _viewModel.ShowActualReleasesCommand; break;
            case UtilitiesMenuCommandKind.ShowPlatformUpdate:
                item.Command = _viewModel.ShowPlatformUpdateCommand; break;
            case UtilitiesMenuCommandKind.ShowPlatformDownload:
                item.Command = _viewModel.ShowPlatformDownloadCommand; break;
            case UtilitiesMenuCommandKind.ServerMonitor:
                item.Command = _viewModel.ServerMonitorCommand; break;
            case UtilitiesMenuCommandKind.Services:
                item.Command = _viewModel.ShowServicesCommand; break;
            case UtilitiesMenuCommandKind.OpenServerConsole:
                item.Command = _viewModel.OpenServerConsoleCommand; break;
            case UtilitiesMenuCommandKind.LockApp:
                item.Command = _viewModel.LockAppCommand; break;
            case UtilitiesMenuCommandKind.UnlockPrivateBases:
                item.Command = _viewModel.UnlockPrivateBasesCommand; break;
            case UtilitiesMenuCommandKind.ChangeAppLock:
                item.Command = _viewModel.ChangeAppLockCommand; break;
            case UtilitiesMenuCommandKind.RunBackupScenario:
                item.Command = _viewModel.RunBackupScenarioCommand; break;
            case UtilitiesMenuCommandKind.MaintenanceCenter:
                item.Command = _viewModel.MaintenanceCenterCommand; break;
            case UtilitiesMenuCommandKind.OpenConfigTypesEdit:
                item.Click += (_, _) => _viewModel.OpenConfigTypesEdit(); break;
            case UtilitiesMenuCommandKind.OpenItsAccounts:
                item.Click += (_, _) => _viewModel.OpenItsAccounts(); break;
            case UtilitiesMenuCommandKind.OpenNotificationChannels:
                item.Click += (_, _) => _viewModel.OpenNotificationChannels(); break;
            case UtilitiesMenuCommandKind.OpenScheduledTasks:
                item.Click += (_, _) => OnScheduledTasksMenuClick(item, null); break;
            case UtilitiesMenuCommandKind.ShowBackupScenarios:
                item.Command = _viewModel.ShowBackupScenariosCommand; break;
            case UtilitiesMenuCommandKind.ShowScriptsSettings:
                item.Command = _viewModel.ShowScriptsSettingsCommand; break;
            case UtilitiesMenuCommandKind.UsageStatistics:
                item.Command = _viewModel.UsageStatisticsCommand; break;
            case UtilitiesMenuCommandKind.ConfigDiff:
                item.Command = _viewModel.ConfigDiffCommand; break;
            case UtilitiesMenuCommandKind.MetadataExplorer:
                item.Command = _viewModel.MetadataExplorerCommand; break;
            case UtilitiesMenuCommandKind.RepositoryBatchUpdate:
                item.Command = _viewModel.RepositoryBatchUpdateCommand; break;
            case UtilitiesMenuCommandKind.RepositoryBrowser:
                item.Command = _viewModel.RepositoryBrowserCommand; break;
            // Массовые операции по списку баз (issue #91).
            case UtilitiesMenuCommandKind.BatchDumpDt:
                item.Command = _viewModel.BatchDumpDtCommand; break;
            case UtilitiesMenuCommandKind.BatchLoadCf:
                item.Command = _viewModel.BatchLoadCfCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesCsv:
                item.Command = _viewModel.ExportBasesCsvCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesHtml:
                item.Command = _viewModel.ExportBasesHtmlCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesJson:
                item.Command = _viewModel.ExportBasesJsonCommand; break;
            case UtilitiesMenuCommandKind.ImportBasesJson:
                item.Command = _viewModel.ImportBasesJsonCommand; break;
            // Экспорт/импорт в произвольный файл *.v8i (issue #29): те же команды, что
            // в настройках (вкладка «Базы» → «Список баз»), с теми же диалогами.
            case UtilitiesMenuCommandKind.ExportBasesV8iFile:
                item.Command = _viewModel.ExportToIbasesFileCommand; break;
            case UtilitiesMenuCommandKind.ImportBasesV8iFile:
                item.Command = _viewModel.ImportFromIbasesFileCommand; break;
            case UtilitiesMenuCommandKind.ImportClusterInfobases:
                item.Command = _viewModel.ImportClusterInfobasesCommand; break;
            case UtilitiesMenuCommandKind.MigrateClusterInfobases:
                item.Command = _viewModel.MigrateClusterInfobasesCommand; break;
            case UtilitiesMenuCommandKind.ProcessInspector:
                item.Command = _viewModel.ProcessInspectorCommand; break;
            case UtilitiesMenuCommandKind.KillOneCProcesses:
                item.Command = _viewModel.KillOneCProcessesCommand; break;
            case UtilitiesMenuCommandKind.ConnectionReplace:
                item.Click += (_, _) => OnUtilitiesConnectionReplace_Click(item, null); break;
            case UtilitiesMenuCommandKind.UndoConnectionReplace:
                item.IsEnabled = _viewModel.CanUndoConnectionReplace;
                item.Click += (_, _) => OnUtilitiesUndoConnectionReplace_Click(item, null); break;
            case UtilitiesMenuCommandKind.RemoveMissingFileBases:
                item.Command = _viewModel.RemoveMissingFileBasesCommand; break;
            case UtilitiesMenuCommandKind.CheckForUpdates:
                item.Click += (_, _) => OnCheckForUpdatesMenuClick(item, null); break;
            case UtilitiesMenuCommandKind.EventLog:
                item.Command = _viewModel.EventLogCommand; break;
            case UtilitiesMenuCommandKind.SharedLists:
                item.Command = _viewModel.SharedListsCommand; break;
            case UtilitiesMenuCommandKind.TelegramBot:
                item.Command = _viewModel.TelegramBotCommand; break;
            case UtilitiesMenuCommandKind.DbmsBackup:
                item.Command = _viewModel.DbmsBackupCommand; break;
            case UtilitiesMenuCommandKind.ServerAlerts:
                item.Command = _viewModel.ServerAlertsCommand; break;
            case UtilitiesMenuCommandKind.TechLog:
                item.Command = _viewModel.TechLogCommand; break;
            case UtilitiesMenuCommandKind.GitVersioning:
                item.Command = _viewModel.GitVersioningCommand; break;
            case UtilitiesMenuCommandKind.ConfigExtensions:
                item.Command = _viewModel.ExtensionsCommand; break;
            case UtilitiesMenuCommandKind.InfobaseUsers:
                item.Command = _viewModel.InfobaseUsersCommand; break;
            case UtilitiesMenuCommandKind.InfobaseScheduledJobs:
                item.Command = _viewModel.ScheduledJobsCommand; break;
        }
    }

    /// <summary>Активная цветовая схема для построения меню «Утилиты» (Задача 6).</summary>
    private static Models.ColorScheme ActiveScheme => ThemeManager.CurrentScheme;

    /// <summary>Активен ли тёмный вариант темы (для выбора палитры активной схемы).</summary>
    private static bool IsDarkTheme => ThemeManager.CurrentTheme == ThemeManager.DarkThemeName;

    /// <summary>
    /// Смена полноценной темы (Задача 6): пересобираем контекстное меню кнопки «Утилиты»
    /// и обновляем его цвета/иконки на лету. Меню всё равно пересоздаётся при каждом
    /// открытии (см. <see cref="BuildUtilitiesMenu"/>), поэтому подписка лишь гарантирует
    /// мгновенное обновление уже построенного меню при переключении темы.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Глифы кнопок управления окном следуют набору иконок активной темы (Задача 2).
        UpdateWindowControlGlyphs();

        // Контекстное меню строки базы (issue #56) строится кодом — пересобираем
        // его при смене темы, чтобы цвета/иконки обновились на лету (независимо
        // от наличия кнопки «Утилиты»).
        RebuildRowContextMenu();

        if (UtilitiesButton is null)
            return;
        var menu = BuildUtilitiesMenu();
        menu.PlacementTarget = UtilitiesButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = DataContext;
        UtilitiesButton.ContextMenu = menu;
    }
}
#endif
