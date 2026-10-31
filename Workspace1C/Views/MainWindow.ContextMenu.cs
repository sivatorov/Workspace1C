#if WINDOWS
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// WPF-построитель контекстного меню строки базы (issue #56): динамическое построение
/// ContextMenu по каталогу <see cref="ContextMenuCatalog"/> + компоновке
/// <see cref="ContextMenuLayout"/>, полученной из <see cref="ContextMenuResolver.Resolve"/>.
/// Заменяет статическое XAML-меню в <c>MainWindow.xaml</c> (обратная совместимость: при
/// выключенной правке структуры компоновка — стандартная, и меню выглядит ровно как
/// раньше — структура, хоткеи, batch-блок, пользовательские действия, стабилизация
/// выделения). Содержимое перечитывается при каждом открытии меню (как меню «Утилиты»).
/// </summary>
public partial class MainWindow
{
    /// <summary>Подменю «Пользовательские действия…» код-меню (наполняется в Opened).</summary>
    private MenuItem? _customActionsMenu;

    /// <summary>Разделитель перед подменю «Пользовательские действия…»: управляется вместе с ним.</summary>
    private Separator? _customActionsSeparator;

    /// <summary>Отпечаток компоновки, по которой построено текущее меню (для перечитывания при открытии).</summary>
    private string? _rowContextMenuLayoutKey;

    /// <summary>
    /// Строит контекстное меню строки базы по каталогу+компоновке (issue #56).
    /// Правка структуры выключена (по умолчанию) — стандартная компоновка; включена —
    /// пользовательская из <c>context_menu_layout.json</c>. Новые пункты программы
    /// (признак «новинка»), не размещённые в компоновке, автоматически попадают
    /// в подменю «Новинки программы» в конце меню.
    /// </summary>
    private ContextMenu BuildRowContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = TryFindResource("ModernContextMenu") as Style,
            DataContext = DataContext,
        };
        // Обработчик Opened — прежний (см. MainWindow.Columns.cs): batch-заголовок/видимость,
        // restricted-режим, пользовательские действия. Он же перечитывает компоновку
        // (резолвер) при каждом открытии меню.
        menu.Opened += OnBaseContextMenu_Opened;
        PopulateRowContextMenu(menu.Items);
        return menu;
    }

    /// <summary>Наполняет пункты меню по актуальной компоновке и запоминает её отпечаток.</summary>
    private void PopulateRowContextMenu(ItemCollection target)
    {
        if (target is null)
            return;
        target.Clear();
        _customActionsMenu = null;
        _customActionsSeparator = null;

        if (_viewModel is null)
        {
            _rowContextMenuLayoutKey = null;
            return;
        }
        var store = AppServices.TryGetService<IContextMenuLayoutStore>();
        var layout = ContextMenuResolver.PrepareForMenu(
            ContextMenuResolver.Resolve(new AppSettings { ContextMenuEditingEnabled = _viewModel.ContextMenuEditingEnabled }, store));
        AppendRowContextNodes(target, layout.Root);
        _rowContextMenuLayoutKey = LayoutFingerprint(layout);
    }

    /// <summary>Рекурсивно добавляет узлы компоновки в целевой список пунктов меню (issue #56).</summary>
    private void AppendRowContextNodes(ItemCollection target, List<ContextMenuNode> nodes)
    {
        if (nodes is null || target is null)
            return;
        Separator? lastSeparator = null;
        foreach (var node in nodes)
        {
            if (node is null || !node.Visible)
                continue;
            if (node.SeparatorBefore && target.Count > 0)
            {
                var s = new Separator();
                target.Add(s);
                lastSeparator = s;
            }
            if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
            {
                // Узел-разделитель (issue #374): отдельный элемент дерева компоновки.
                if (target.Count > 0)
                {
                    var s = new Separator();
                    target.Add(s);
                    lastSeparator = s;
                }
                continue;
            }
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                var sub = new MenuItem
                {
                    Header = RowGroupTitle(node),
                    Style = TryFindResource("ModernMenuItem") as Style,
                };
                sub.Icon = RowGroupIcon(node);
                AppendRowContextNodes(sub.Items, node.Children);
                target.Add(sub);
            }
            else
            {
                var item = CreateRowItem(node);
                if (item is null)
                    continue;
                var catalog = ContextMenuCatalog.Find(node.ItemId);
                if (catalog?.CommandKind == ContextMenuCommandKind.CustomActions)
                {
                    // Подменю «Пользовательские действия…» и его разделитель: ссылки
                    // сохраняются для PopulateCustomActionsMenu (MainWindow.CustomActions.cs).
                    _customActionsMenu = item;
                    _customActionsSeparator = lastSeparator;
                }
                target.Add(item);
            }
        }
    }

    /// <summary>Заголовок группы: пользовательский или локализованный стандартный.</summary>
    private string RowGroupTitle(ContextMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.CustomTitle))
            return node.CustomTitle!;
        var key = ContextMenuCatalog.GroupTitleKey(node.GroupId ?? string.Empty);
        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationManager.T(key);
    }

    /// <summary>Иконка группы: пользовательская из компоновки → стандартная каталога.</summary>
    private object? RowGroupIcon(ContextMenuNode node)
    {
        var iconKey = !string.IsNullOrWhiteSpace(node.IconKey)
            ? node.IconKey!
            : ContextMenuCatalog.GroupWpfIconKind(node.GroupId ?? string.Empty);
        var color = !string.IsNullOrWhiteSpace(node.IconColor)
            ? node.IconColor!
            : ContextMenuCatalog.GroupIconColor(node.GroupId ?? string.Empty);
        return CreateRowMenuIcon(iconKey, color);
    }

    /// <summary>Создаёт пункт меню по узлу-ссылке на пункт каталога (null — неизвестный пункт).</summary>
    private MenuItem? CreateRowItem(ContextMenuNode node)
    {
        var catalogItem = ContextMenuCatalog.Find(node.ItemId);
        if (catalogItem is null)
            return null;

        var item = new MenuItem
        {
            Header = LocalizationManager.T(catalogItem.TextKey),
            Style = TryFindResource("ModernMenuItem") as Style,
            Tag = RowItemTag(catalogItem.CommandKind),
        };
        item.Icon = CreateRowMenuIcon(
            !string.IsNullOrWhiteSpace(node.IconKey) ? node.IconKey! : catalogItem.WpfIconKind,
            !string.IsNullOrWhiteSpace(node.IconColor) ? node.IconColor! : catalogItem.IconColor);
        var hotkey = RowHotkeyFor(catalogItem.HotkeyKey);
        if (!string.IsNullOrWhiteSpace(hotkey))
            item.InputGestureText = hotkey!;

        // Специальные динамические узлы: подменю «Пользовательские действия» и блок
        // «Для выделенных (N)…» в XAML стартовали свёрнутыми (Visibility=Collapsed),
        // видимость устанавливается в OnBaseContextMenu_Opened.
        if (catalogItem.CommandKind == ContextMenuCommandKind.CustomActions)
            item.Visibility = Visibility.Collapsed;
        else if (catalogItem.CommandKind == ContextMenuCommandKind.BatchOperations)
            item.Visibility = Visibility.Collapsed;

        ApplyRowCommand(item, catalogItem);
        return item;
    }

    /// <summary>Метка Tag пункта (режим «Пользователь» / пакетный блок), как в прежней XAML-разметке.</summary>
    private static object? RowItemTag(ContextMenuCommandKind kind) => kind switch
    {
        ContextMenuCommandKind.LaunchEnterprise or
        ContextMenuCommandKind.LaunchConfigurator or
        ContextMenuCommandKind.RunScript or
        ContextMenuCommandKind.CustomActions or
        ContextMenuCommandKind.ToFavorites or
        ContextMenuCommandKind.Pin => "User",
        ContextMenuCommandKind.BatchOperations => "Batch",
        _ => null
    };

    /// <summary>Иконка пункта/группы 16×16 materialDesign PackIcon по ключу каталога.</summary>
    private static object? CreateRowMenuIcon(string iconKind, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(iconKind) || !Enum.TryParse<PackIconKind>(iconKind, out var kind))
            return null;
        return new PackIcon
        {
            Kind = kind,
            Width = 16,
            Height = 16,
            Foreground = ParseRowMenuIconColor(colorHex)
        };
    }

    /// <summary>Цвет иконки: разбор #RRGGBB; при сбое — серый (как в меню «Утилиты»).</summary>
    private static Brush ParseRowMenuIconColor(string colorHex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(colorHex))
                return Themes.WpfBrushes.FromSpec(colorHex);
        }
        catch { /* невалидный цвет — серый */ }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
    }

    /// <summary>Хоткей пункта по имени свойства MainViewModel (из каталога); null — нет хоткея.</summary>
    private string? RowHotkeyFor(string? hotkeyKey)
    {
        if (string.IsNullOrWhiteSpace(hotkeyKey) || _viewModel is null)
            return null;
        switch (hotkeyKey)
        {
            case "HotkeyEnterprise": return _viewModel.HotkeyEnterprise;
            case "HotkeyConfigurator": return _viewModel.HotkeyConfigurator;
            case "HotkeyRunScript": return _viewModel.HotkeyRunScript;
            case "HotkeyFavorite": return _viewModel.HotkeyFavorite;
            case "HotkeyPin": return _viewModel.HotkeyPin;
            case "HotkeyAdd": return _viewModel.HotkeyAdd;
            case "HotkeyCheckUpdate": return _viewModel.HotkeyCheckUpdate;
            case "HotkeySessionLock": return _viewModel.HotkeySessionLock;
            case "HotkeyCheckIntegrity": return _viewModel.HotkeyCheckIntegrity;
            case "HotkeyExportsList": return _viewModel.HotkeyExportsList;
            case "HotkeyClearCache": return _viewModel.HotkeyClearCache;
            case "HotkeyDelete": return _viewModel.HotkeyDelete;
            case "HotkeyFindInList": return _viewModel.HotkeyFindInList;
            case "HotkeyEdit": return _viewModel.HotkeyEdit;
            default: return null;
        }
    }

    /// <summary>Заголовок блока пакетных операций «Для выделенных (N)…».</summary>
    private string BatchMenuHeader(int count) => string.Format(
        LocalizationManager.T("Main.BatchForSelected"), count);

    /// <summary>Привязывает команду/действие к пункту меню по виду команды каталога.</summary>
    private void ApplyRowCommand(MenuItem item, ContextMenuItem catalogItem)
    {
        if (_viewModel is null)
            return;
        switch (catalogItem.CommandKind)
        {
            case ContextMenuCommandKind.LaunchEnterprise:
                item.Command = _viewModel.LaunchEnterpriseCommand; break;
            case ContextMenuCommandKind.LaunchConfigurator:
                item.Command = _viewModel.LaunchConfiguratorCommand; break;
            case ContextMenuCommandKind.RunScript:
                item.Command = _viewModel.RunScriptForSelectedCommand; break;
            case ContextMenuCommandKind.CustomActions:
                break; // динамическое подменю — наполняется в OnBaseContextMenu_Opened
            case ContextMenuCommandKind.ToFavorites:
                item.Command = _viewModel.ToggleFavoriteCommand; break;
            case ContextMenuCommandKind.Pin:
                item.Command = _viewModel.TogglePinCommand; break;
            case ContextMenuCommandKind.AddBase:
                item.Command = _viewModel.AddInfobaseCommand; break;
            case ContextMenuCommandKind.BatchOperations:
                PopulateBatchOperationsMenu(item); break; // блок «Для выделенных (N)…»
            case ContextMenuCommandKind.RefreshConfigInfo:
                item.Command = _viewModel.RefreshConfigurationInfoCommand; break;
            case ContextMenuCommandKind.CheckUpdate:
                item.Command = _viewModel.CheckUpdateCommand; break;
            case ContextMenuCommandKind.ConfigLink:
                item.Click += OnConfigUpdateLinkMenuClick; break;
            case ContextMenuCommandKind.SessionLock:
                item.Command = _viewModel.ShowSessionLockCommand; break;
            case ContextMenuCommandKind.RegisterComConnector:
                item.Command = _viewModel.RegisterComConnectorCommand; break;
            case ContextMenuCommandKind.NetworkDiagnostics:
                item.Command = _viewModel.NetworkDiagnosticsCommand; break;
            case ContextMenuCommandKind.OpenInfobaseFolder:
                item.Command = _viewModel.OpenInfobaseFolderCommand; break;
            case ContextMenuCommandKind.CloneBase:
                item.Command = _viewModel.CloneInfobaseCommand; break;
            case ContextMenuCommandKind.TestDeploy:
                item.Command = _viewModel.TestDeployCommand; break;
            case ContextMenuCommandKind.CheckIntegrity:
                item.Command = _viewModel.CheckIntegrityCommand; break;
            case ContextMenuCommandKind.WebPublish:
                item.Command = _viewModel.WebPublishCommand; break;
            case ContextMenuCommandKind.LaunchHistory:
                item.Command = _viewModel.ShowLaunchHistoryCommand; break;
            case ContextMenuCommandKind.DumpToDt:
                item.Command = _viewModel.DumpInfobaseDtCommand; break;
            case ContextMenuCommandKind.DumpConfigToCf:
                item.Command = _viewModel.DumpConfigurationCfCommand; break;
            case ContextMenuCommandKind.ShowExportsList:
                item.Command = _viewModel.ShowExportsListCommand; break;
            case ContextMenuCommandKind.ClearProgramCache:
                item.Command = _viewModel.ClearProgramCacheCommand; break;
            case ContextMenuCommandKind.ClearUserCache:
                item.Command = _viewModel.ClearUserCacheCommand; break;
            case ContextMenuCommandKind.ClearCacheBoth:
                item.Command = _viewModel.ClearCacheBothCommand; break;
            case ContextMenuCommandKind.Delete:
                item.Command = _viewModel.DeleteInfobaseCommand; break;
            case ContextMenuCommandKind.FindInList:
                item.Command = _viewModel.FindInListCommand; break;
            case ContextMenuCommandKind.CreateDesktopShortcut:
                item.Command = _viewModel.CreateDesktopShortcutCommand; break;
            case ContextMenuCommandKind.CopyConnectionString:
                item.Command = _viewModel.CopyConnectionStringCommand; break;
            case ContextMenuCommandKind.EditSettings:
                item.Command = _viewModel.EditInfobaseCommand; break;
            case ContextMenuCommandKind.Notes:
                item.Command = _viewModel.ShowBaseNotesCommand; break;
        }
    }

    /// <summary>Наполняет блок «Для выделенных (N)…» (мультивыделение, 0.3.9.90) —
    /// состав прежней XAML-разметки MainWindow.xaml:2376–2403.</summary>
    private void PopulateBatchOperationsMenu(MenuItem batchMenu)
    {
        batchMenu.Header = BatchMenuHeader(_viewModel?.BatchOperationTargetCount ?? 0);
        batchMenu.Icon = CreateRowMenuIcon("SelectionMultiple", "@PurpleAccentBrush");
        batchMenu.Items.Add(CreateBatchAction("Main.BatchAssignTag", "Tag", "@InfoBrush", OnBatchAssignTag_Click));
        batchMenu.Items.Add(CreateBatchAction("Main.BatchMoveToGroup", "FolderMove", "@InfoBrush", OnBatchMoveToGroup_Click));
        batchMenu.Items.Add(CreateBatchAction("Main.BatchAddFavorites", "Star", "@WarningBrush", OnBatchAddFavorites_Click));
        batchMenu.Items.Add(CreateBatchAction("Main.BatchRunBackup", "DatabaseExport", "@SuccessBrush", OnBatchRunBackup_Click));
        // Массовая выгрузка выделенных баз в DT/CF с шаблоном имени (issue #92).
        batchMenu.Items.Add(CreateBatchAction("Main.BatchDumpDtCf", "DatabaseExport", "@SuccessBrush", OnBatchDumpDtCf_Click));
        batchMenu.Items.Add(CreateBatchAction("Main.BatchCheckAvailability", "CloudCheck", "@InfoBrush", OnBatchCheckAvailability_Click));
        // Массовая замена строк подключения (0.3.9.191, функция 6).
        batchMenu.Items.Add(CreateBatchAction("Main.BatchConnectionReplace", "FindReplace", "@WarningBrush", OnBatchConnectionReplace_Click));
        // Смена платформы выделенных баз (issue #54, переработка по комментарию 7OH):
        // отдельное окно «Сменить платформу» вместо встроенной галочки в «Замене строк…».
        batchMenu.Items.Add(CreateBatchAction("Main.BatchChangePlatform", "PackageVariant", "@SuccessBrush", OnBatchChangePlatform_Click));
        batchMenu.Items.Add(CreateBatchAction("Main.BatchDelete", "Delete", "@ErrorBrush", OnBatchDelete_Click));
    }

    /// <summary>Пункт пакетной операции (0.3.9.90): подпись, значок, обработчик клика.</summary>
    private MenuItem CreateBatchAction(string textKey, string iconKind, string iconColor, RoutedEventHandler click)
    {
        var item = new MenuItem
        {
            Header = LocalizationManager.T(textKey),
            Style = TryFindResource("ModernMenuItem") as Style,
        };
        item.Icon = CreateRowMenuIcon(iconKind, iconColor);
        item.Click += click;
        return item;
    }

    /// <summary>
    /// Пересобирает контекстное меню дерева (назначение нового экземпляра). Вызывается
    /// при смене темы, языка и после закрытия окна настроек (изменился резолвер/хоткеи),
    /// как меню «Утилиты».
    /// </summary>
    public void RebuildRowContextMenu()
    {
        if (MainTree is null)
            return;
        MainTree.ContextMenu = BuildRowContextMenu();
    }

    /// <summary>Отпечаток компоновки для сравнения при открытии меню (перечитывание резолвера).</summary>
    private static string LayoutFingerprint(ContextMenuLayout? layout)
    {
        var sb = new StringBuilder();
        AppendFingerprint(sb, layout?.Root);
        return sb.ToString();
    }

    private static void AppendFingerprint(StringBuilder sb, List<ContextMenuNode>? nodes)
    {
        if (nodes is null)
            return;
        foreach (var n in nodes)
        {
            if (n is null)
                continue;
            sb.Append(n.Kind).Append('|').Append(n.ItemId).Append('|').Append(n.GroupId)
              .Append('|').Append(n.CustomTitle).Append('|').Append(n.Visible).Append(';');
            AppendFingerprint(sb, n.Children);
        }
    }
}
#endif