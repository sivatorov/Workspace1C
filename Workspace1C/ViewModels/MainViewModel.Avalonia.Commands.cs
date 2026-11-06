#if LINUX
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): команды (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    // ======================= Команды =======================

    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand SearchByTagCommand { get; private set; } = null!;
    public ICommand AddTagInlineCommand { get; private set; } = null!;
    public ICommand RemoveTagCommand { get; private set; } = null!;
    public ICommand ClearTagFiltersCommand { get; private set; } = null!;
    public ICommand FindInListCommand { get; private set; } = null!;
    public ICommand ShowAllCommand { get; private set; } = null!;
    public ICommand ShowFavoritesCommand { get; private set; } = null!;
    public ICommand ShowRecentCommand { get; private set; } = null!;
    /// <summary>Команда отбора «Только запущенные» (горячая клавиша, issue #339).</summary>
    public ICommand ShowRunningCommand { get; private set; } = null!;
    public ICommand LaunchEnterpriseCommand { get; private set; } = null!;
    public ICommand LaunchConfiguratorCommand { get; private set; } = null!;

    /// <summary>Запуск Предприятия с разовыми параметрами командной строки.</summary>
    public ICommand LaunchEnterpriseWithParamsCommand { get; private set; } = null!;

    /// <summary>Запуск Предприятия с запросом имени и пароля вместо сохранённых.</summary>
    public ICommand LaunchEnterpriseWithAuthCommand { get; private set; } = null!;

    /// <summary>Запуск Конфигуратора с разовыми параметрами командной строки.</summary>
    public ICommand LaunchConfiguratorWithParamsCommand { get; private set; } = null!;

    /// <summary>Запуск Конфигуратора с запросом имени и пароля вместо сохранённых (issue #48).</summary>
    public ICommand LaunchConfiguratorWithAuthCommand { get; private set; } = null!;

    public ICommand EditInfobaseCommand { get; private set; } = null!;
    public ICommand AddInfobaseCommand { get; private set; } = null!;
    public ICommand DeleteInfobaseCommand { get; private set; } = null!;
    public ICommand EditGroupCommand { get; private set; } = null!;
    public ICommand DeleteGroupCommand { get; private set; } = null!;
    public ICommand OpenInfobaseByLinkCommand { get; private set; } = null!;
    public ICommand ToggleFavoriteCommand { get; private set; } = null!;
    public ICommand TogglePinCommand { get; private set; } = null!;
    public ICommand ToggleFavoriteForCommand { get; private set; } = null!;
    public ICommand TogglePinForCommand { get; private set; } = null!;
    public ICommand OpenSettingsCommand { get; private set; } = null!;
    public ICommand ExpandAllGroupsCommand { get; private set; } = null!;
    public ICommand CollapseAllGroupsCommand { get; private set; } = null!;
    public ICommand ExpandBranchCommand { get; private set; } = null!;
    public ICommand CollapseBranchCommand { get; private set; } = null!;
    public ICommand ToggleGroupBranchCommand { get; private set; } = null!;
    public ICommand SortGroupsAscendingCommand { get; private set; } = null!;
    public ICommand SortGroupsDescendingCommand { get; private set; } = null!;
    public ICommand SynchronizeWithIbasesCommand { get; private set; } = null!;
    public ICommand ToggleThemeCommand { get; private set; } = null!;
    public ICommand ToggleRightPanelDetailsCommand { get; private set; } = null!;
    public ICommand ToggleSessionLaunchPanelCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand CopyConnectionStringCommand { get; private set; } = null!;

    /// <summary>Команда «Экспорт списка баз в CSV…»: видимые сейчас базы → CSV для Excel (0.3.9.91).</summary>
    public ICommand ExportBasesCsvCommand { get; private set; } = null!;

    /// <summary>Команда «Экспорт отчёта по базам (HTML)…»: видимые базы → самодостаточный HTML-отчёт (0.3.9.131).</summary>
    public ICommand ExportBasesHtmlCommand { get; private set; } = null!;

    /// <summary>Команда «Экспорт списка баз (JSON)…»: полное состояние списка → JSON (0.3.9.122).</summary>
    public ICommand ExportBasesJsonCommand { get; private set; } = null!;

    /// <summary>Команда «Импорт списка баз (JSON)…»: добавляющий импорт из JSON (0.3.9.122).</summary>
    public ICommand ImportBasesJsonCommand { get; private set; } = null!;

    public ICommand CheckAvailabilityCommand { get; private set; } = null!;
    public ICommand OpenInfobaseFolderCommand { get; private set; } = null!;
    public ICommand CreateDesktopShortcutCommand { get; private set; } = null!;
    public ICommand OpenNativeStarterCommand { get; private set; } = null!;
    public ICommand QuickClearCacheCommand { get; private set; } = null!;
    public ICommand ClearCacheCommand { get; private set; } = null!;
    public ICommand ClearProgramCacheCommand { get; private set; } = null!;
    public ICommand DumpInfobaseDtCommand { get; private set; } = null!;
    public ICommand DumpConfigurationCfCommand { get; private set; } = null!;
    public ICommand RefreshConfigurationInfoCommand { get; private set; } = null!;
    public ICommand ClearUserCacheCommand { get; private set; } = null!;
    public ICommand ClearCacheBothCommand { get; private set; } = null!;
    public ICommand SwitchUserCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SearchByTagCommand = new RelayCommand(SearchByTag);
        AddTagInlineCommand = new RelayCommand(AddTagInline);
        RemoveTagCommand = new RelayCommand(RemoveTag);
        ClearTagFiltersCommand = new RelayCommand(ClearTagFilters);
        // «Найти в списке»: переход к базе в общем списке «Все базы» с раскрытием группы (issue #285).
        FindInListCommand = new RelayCommand(p => ExecuteFindInList(p as Infobase),
            p => SelectedInfobase is not null || p is Infobase);
        // Режимы списка вынесены в команды, чтобы их можно было повесить
        // на горячую клавишу: привязка принимает команду, а не свойство.
        ShowAllCommand = new RelayCommand(() => IsListModeAll = true);
        ShowFavoritesCommand = new RelayCommand(() => IsListModeFavorites = true);
        ShowRecentCommand = new RelayCommand(() => IsListModeRecent = true);
        ShowRunningCommand = new RelayCommand(() => IsListModeRunning = true);
        LaunchEnterpriseCommand = new RelayCommand(_ => Launch(_launchVm.LaunchCommand, LaunchKind.Enterprise), _ => SelectedInfobase is not null);
        LaunchConfiguratorCommand = new RelayCommand(_ => Launch(_launchVm.LaunchCommand, LaunchKind.Configurator), _ => SelectedInfobase is not null);
        LaunchEnterpriseWithParamsCommand = new RelayCommand(_ => LaunchWithParams(LaunchKind.Enterprise), _ => SelectedInfobase is not null);
        LaunchEnterpriseWithAuthCommand = new RelayCommand(_ => LaunchWithAuth(LaunchKind.Enterprise), _ => SelectedInfobase is not null);
        LaunchConfiguratorWithParamsCommand = new RelayCommand(_ => LaunchWithParams(LaunchKind.Configurator), _ => SelectedInfobase is not null);
        // Запуск Конфигуратора с запросом авторизации (issue #48): SHIFT+хоткей Конфигуратора.
        LaunchConfiguratorWithAuthCommand = new RelayCommand(_ => LaunchWithAuth(LaunchKind.Configurator), _ => SelectedInfobase is not null);
        EditInfobaseCommand = new RelayCommand(p => EditInfobase(p as Infobase ?? SelectedInfobase), _ => SelectedInfobase is not null);
        AddInfobaseCommand = new RelayCommand(AddInfobase);
        DeleteInfobaseCommand = new RelayCommand(_ => DeleteInfobase(),
            _ => SelectedInfobase is not null || SelectedGroupNode?.Group is not null);
        // Команды группы: параметр — узел группы или сама группа из строки дерева.
        EditGroupCommand = new RelayCommand(p =>
        {
            var group = ResolveGroup(p);
            if (group is not null)
            {
                EditGroup(group);
                return;
            }

            // Служебные узлы «Без группы» / «Закреплённые» (без модели Group)
            // редактируются тем же окном, но только по оформлению (цвет и иконка),
            // как в Windows-версии (issue #240).
            if (p is GroupNodeViewModel node && node.Marker is { } marker)
            {
                if (string.Equals(marker, GroupNodeViewModel.NoGroupMarker, StringComparison.Ordinal))
                    EditNoGroupNode();
                else if (string.Equals(marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                    EditPinnedNode();
            }
        });
        DeleteGroupCommand = new RelayCommand(p =>
        {
            var group = ResolveGroup(p);
            if (group is not null)
                DeleteGroup(group);
        }, p => ResolveGroup(p) != null);
        OpenInfobaseByLinkCommand = new RelayCommand(OpenInfobaseByLink);
        ToggleFavoriteCommand = new RelayCommand(_ => ToggleFavorite(), _ => SelectedInfobase is not null);
        TogglePinCommand = new RelayCommand(_ => TogglePin(), _ => SelectedInfobase is not null);
        ToggleFavoriteForCommand = new RelayCommand(p => ToggleFavoriteFor(p as Infobase), p => p is Infobase);
        TogglePinForCommand = new RelayCommand(p => TogglePinFor(p as Infobase), p => p is Infobase);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        // Копия экрана по хоткею (функция №30, Этап 8).
        TakeScreenshotCommand = new RelayCommand(TakeScreenshot);
        ExpandAllGroupsCommand = new RelayCommand(ExpandAllGroups);
        CollapseAllGroupsCommand = new RelayCommand(CollapseAllGroups);
        // Ветки дерева (issue #341): развернуть/свернуть только текущую группу с подгруппами.
        ExpandBranchCommand = new RelayCommand(_ => ExpandBranchOf(CurrentBranchNode()));
        CollapseBranchCommand = new RelayCommand(_ => CollapseBranchOf(CurrentBranchNode()));
        ToggleGroupBranchCommand = new RelayCommand(p => ToggleGroupBranch(p as GroupNodeViewModel));
        SortGroupsAscendingCommand = new RelayCommand(() => SortGroups(true));
        SortGroupsDescendingCommand = new RelayCommand(() => SortGroups(false));
        SynchronizeWithIbasesCommand = new RelayCommand(SynchronizeWithIbases);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleRightPanelDetailsCommand = new RelayCommand(() => ShowRightPanelDetails = !ShowRightPanelDetails);
        ToggleSessionLaunchPanelCommand = new RelayCommand(() => ShowSessionLaunchPanel = !ShowSessionLaunchPanel);
        ExitCommand = new RelayCommand(ExitApplication);
        CopyConnectionStringCommand = new RelayCommand(_ => CopyConnectionString(), _ => SelectedInfobase is not null);
        // Экспорт видимого списка баз в CSV (0.3.9.91): команда не требует выделенной базы.
        ExportBasesCsvCommand = new RelayCommand(ExportBasesCsv);
        // Экспорт отчёта по базам в HTML (0.3.9.131): видимые базы + сводка по данным
        // Центра обслуживания; команда не требует выделенной базы.
        ExportBasesHtmlCommand = new RelayCommand(ExportBasesHtml);
        // Экспорт/импорт полного состояния списка баз в JSON (0.3.9.122): команды
        // не требуют выделенной базы; импорт — добавляющий, дубликаты пропускаются.
        ExportBasesJsonCommand = new RelayCommand(ExportBasesJson);
        ImportBasesJsonCommand = new RelayCommand(ImportBasesJson);
        CheckAvailabilityCommand = new RelayCommand(CheckAvailability);
        OpenInfobaseFolderCommand = new RelayCommand(_ => OpenInfobaseFolder(),
            _ => SelectedInfobase?.Connection.Type == ConnectionType.File);
        CreateDesktopShortcutCommand = new RelayCommand(_ => CreateDesktopShortcut(), _ => SelectedInfobase is not null);
        OpenNativeStarterCommand = new RelayCommand(OpenNativeStarter);
        QuickClearCacheCommand = new RelayCommand(QuickClearCache, _ => SelectedInfobase is not null);
        // Кнопка «Очистить кеш» верхней панели действует на выбранную базу: если база не
        // выделена (например, под курсором папка) — окно открывается без предзаполненных
        // галок, и пользователь сам отмечает нужные базы (issue #196). В колонке
        // «Действия» строка передаёт свою базу параметром, поэтому там кнопка включена
        // независимо от глобального выбора и стартовая галка ставится на базу строки.
        ClearCacheCommand = new RelayCommand(p => OpenCacheClean(OneCCacheKind.All, p as Infobase),
            p => p is Infobase ? true : Infobases.Count > 0);
        ClearProgramCacheCommand = new RelayCommand(_ => OpenCacheClean(OneCCacheKind.Program));
        DumpInfobaseDtCommand = new RelayCommand(DumpInfobaseDt);
        DumpConfigurationCfCommand = new RelayCommand(DumpConfigurationCf);
        RefreshConfigurationInfoCommand = new RelayCommand(RefreshConfigurationInfo);
        ClearUserCacheCommand = new RelayCommand(_ => OpenCacheClean(OneCCacheKind.User));
        ClearCacheBothCommand = new RelayCommand(_ => OpenCacheClean(OneCCacheKind.All));
        // Смена пользователя (issue #200): диалог входа без перезапуска приложения.
        SwitchUserCommand = new RelayCommand(SwitchUser);
    }

    /// <summary>
    /// «Найти в списке» (issue #285): переходит к базе в общем списке «Все базы».
    /// Сбрасывает фильтры (вкладка/поиск/теги), принудительно раскрывает цепочку групп
    /// от корня до группы базы и выделяет базу; UI после пересборки прокрутит список к строке.
    /// </summary>
    private void ExecuteFindInList(Infobase? target)
    {
        var ib = target ?? SelectedInfobase;
        if (ib is null)
            return;

        // Целевая навигация (issue #31): окно не планирует промежуточное восстановление
        // выделения, пока идёт серия пересборок (смена вкладки + сброс поиска/тегов +
        // повторная пересборка) — единственный reveal выполнит запрос в конце операции
        // (паритет с WPF).
        IsRevealNavigationInProgress = true;
        try
        {
            // Переход на вкладку «Все базы» и сброс фильтров, скрывающих базу из списка.
            IsListModeAll = true;
            if (!string.IsNullOrWhiteSpace(SearchText))
                SearchText = string.Empty;
            ClearTagFilters();

            ExpandPathTo(ib);

            // Цель выставляем ПОСЛЕ пересборки: RebuildTree пересоздаёт узлы, и отложенное
            // восстановление выделения (TreeRebuilt → RestoreTreeSelection) читает цель
            // в момент выполнения. Окно по RevealFindInListRequested не вернёт прежнюю
            // позицию прокрутки, а доведёт строку до видимой области (issue #285).
            RebuildTree();
            SelectedInfobase = ib;
            RevealFindInListRequested?.Invoke();
        }
        finally
        {
            IsRevealNavigationInProgress = false;
        }
    }

    /// <summary>Раскрывает цепочку групп от корня до родителя базы (принудительно).</summary>
    private void ExpandPathTo(Infobase infobase)
    {
        if (string.IsNullOrWhiteSpace(infobase.Group))
            return;
        foreach (var root in AllGroupNodes)
        {
            var owner = FindNode(root, infobase.Group);
            if (owner is null)
                continue;

            var chain = new List<GroupNodeViewModel>();
            for (var n = owner; n is not null; n = n.Parent)
                chain.Add(n);
            chain.Reverse();
            foreach (var n in chain)
            {
                n.SetExpandedSilent(true);
                n.NotifyIsExpanded();
                // Убираем ключ из списка свёрнутых: RebuildTree создаёт узлы заново по
                // _collapsedGroups, иначе свёрнутая группа-предок осталась бы свёрнутой
                // и база осталась бы скрытой (тот же приём, что в NavigateToBookmark).
                if (!string.IsNullOrEmpty(n.NodeKey))
                    _collapsedGroups.Remove(n.NodeKey);
            }
            return;
        }
    }

    // ======================= Командная палитра (Ctrl+K) =======================

    private ICommand? _commandPaletteCommand;

    /// <summary>Команда открытия командной палитры (Ctrl+K).</summary>
    public ICommand CommandPaletteCommand =>
        _commandPaletteCommand ??= new RelayCommand(ExecuteShowCommandPalette);

    /// <summary>Открывает палитру и исполняет выбранный элемент.</summary>
    private void ExecuteShowCommandPalette()
    {
        try
        {
            var (bases, commands) = BuildPaletteSource();
            var win = new Workspace1C.CommandPaletteWindow(bases, commands);
            win.ShowDialogSync(OwnerWindow());

            var item = win.Result;
            if (item is null)
                return;

            if (item.Kind == CommandPaletteItemKind.Base)
            {
                var ib = Infobases.FirstOrDefault(b => b.Id == item.Id);
                if (ib is null)
                    return;

                // Запуск с записью истории и сохранением — общий путь запуска из трея.
                LaunchFromTray(ib, win.UseConfigurator);
                return;
            }

            ExecutePaletteCommand(item.Id);
        }
        catch (Exception ex)
        {
            // Палитра не должна ронять приложение (issue #312): любая ошибка
            // построения источника или открытия окна логируется и показывается.
            _logger.Error("[palette] Ошибка открытия командной палитры", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Palette.OpenError"), ex.Message),
                LocalizationManager.T("Palette.Title"));
        }
    }

    /// <summary>Источник элементов палитры: базы (избранные — выше) и команды интерфейса.</summary>
    private (System.Collections.Generic.List<CommandPaletteItem> Bases,
             System.Collections.Generic.List<CommandPaletteItem> Commands) BuildPaletteSource()
    {
        // Приватные базы заблокированного профиля в палитру не попадают (0.3.9.85).
        var bases = Infobases
            .Where(IsVisibleForPrivateFilter)
            .OrderByDescending(b => b.FavoriteHotkeyNumber > 0)
            .ThenBy(b => b.FavoriteHotkeyNumber > 0 ? b.FavoriteHotkeyNumber : int.MaxValue)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CommandPaletteItem
            {
                Kind = CommandPaletteItemKind.Base,
                Id = b.Id,
                Title = b.Name,
                Subtitle = string.Join(" · ",
                    new[] { b.GroupDisplay, b.ConnectionTypeDisplay, b.ServerDatabaseDisplay }
                        .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")),
                // Заметка базы (п.8, 1.0.0.90): текст карточки участвует в поиске.
                SearchText = NoteSearchText(b)
            })
            .ToList();

        // Сама «Командная палитра» в список команд НЕ включается — иначе выбор её
        // из палитры открывал бы палитру заново (рекурсия, issue #312).
        var commands = new (string Id, string TitleKey, ICommand Command)[]
        {
            ("palette.settings", "Main.Settings", OpenSettingsCommand),
            ("palette.sync", "Main.SyncWithIbases", SynchronizeWithIbasesCommand),
            ("palette.add", "Main.AddBase", AddInfobaseCommand),
            ("palette.availability", "Main.CheckAvailabilityLabel", CheckAvailabilityCommand),
            ("palette.tab-all", "Main.AllBases", ShowAllCommand),
            ("palette.tab-favorites", "Main.Favorites", ShowFavoritesCommand),
            ("palette.tab-recent", "Main.Recent", ShowRecentCommand),
            ("palette.tab-running", "Main.Running", ShowRunningCommand),
            ("palette.clear-search", "Main.ClearSearch", ClearSearchCommand),
            ("palette.clear-tags", "Main.ClearTagFilters", ClearTagFiltersCommand),
            ("palette.backup-scenarios", "Backup.ScenariosTitle", ShowBackupScenariosCommand),
            ("palette.exports", "Restore.Title", ShowExportsListCommand),
            ("palette.actual-releases", "Updates.ActualReleasesTitle", ShowActualReleasesCommand),
            // Массовая загрузка cf через Конфигуратор (issue #106).
            ("palette.mass-load-cf", "Main.BatchLoadCfCfg", MassLoadCfCommand),
        }
        .Select(c => new CommandPaletteItem
        {
            Kind = CommandPaletteItemKind.Command,
            Id = c.Id,
            Title = LocalizationManager.T(c.TitleKey)
        })
        .ToList();

        return (bases, commands);
    }

    /// <summary>Исполняет команду палитры по идентификатору.</summary>
    private void ExecutePaletteCommand(string id)
    {
        ICommand? command = id switch
        {
            "palette.settings" => OpenSettingsCommand,
            "palette.sync" => SynchronizeWithIbasesCommand,
            "palette.add" => AddInfobaseCommand,
            "palette.availability" => CheckAvailabilityCommand,
            "palette.tab-all" => ShowAllCommand,
            "palette.tab-favorites" => ShowFavoritesCommand,
            "palette.tab-recent" => ShowRecentCommand,
            "palette.tab-running" => ShowRunningCommand,
            "palette.clear-search" => ClearSearchCommand,
            "palette.clear-tags" => ClearTagFiltersCommand,
            "palette.backup-scenarios" => ShowBackupScenariosCommand,
            "palette.exports" => ShowExportsListCommand,
            "palette.actual-releases" => ShowActualReleasesCommand,
            "palette.mass-load-cf" => MassLoadCfCommand,
            _ => null
        };

        if (command is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
    }

    // ====================== Заметки базы (п.8, 1.0.0.90) ======================

    private ICommand? _showBaseNotesCommand;

    /// <summary>
    /// Команда «Заметки базы»: markdown-карточка (для кого база, контакты, особенности
    /// обновления) и зашифрованные секреты внешних сервисов (п.8, 1.0.0.90).
    /// </summary>
    public ICommand ShowBaseNotesCommand =>
        _showBaseNotesCommand ??= new RelayCommand(
            _ => ShowBaseNotes(),
            _ => SelectedInfobase != null);

    /// <summary>Открывает окно «Заметки базы» для выбранной базы (Avalonia/Linux).</summary>
    private void ShowBaseNotes()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        try
        {
            var win = new BaseNotesWindow(ib);
            win.ShowDialogSync(OwnerWindow());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка открытия окна «Заметки базы»", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Notes.OpenError"), ex.Message),
                LocalizationManager.T("Notes.Title"));
        }
    }

    // ====================== Дублирование ИБ (файловой и серверной) ======================

    private ICommand? _cloneInfobaseCommand;

    /// <summary>
    /// Команда «Дублировать базу»: файловая ИБ — копия каталога (0.3.9.83);
    /// клиент-серверная ИБ — клон на сервере через DumpIB/DumpCfg → CREATEINFOBASE
    /// → RestoreIB (0.3.9.100, функция №10). Для запущенной базы запрещено.
    /// </summary>
    public ICommand CloneInfobaseCommand =>
        _cloneInfobaseCommand ??= new RelayCommand(
            _ => ExecuteCloneInfobase(),
            _ => SelectedInfobase?.Connection?.Type is ConnectionType.File or ConnectionType.ClientServer);

    /// <summary>Клонирует выбранную базу: файловая — копия каталога, серверная — клон на сервере.</summary>
    private async void ExecuteCloneInfobase()
    {
        // v100114: прямой async void (вне обёртки RelayCommand) — исключение не должно
        // ронять приложение: журнал + диалог ошибки.
        try
        {
            await ExecuteCloneInfobaseCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка дублирования базы", ex);
            _dialog.ShowError(ex.Message, LocalizationManager.T("Clone.Title"));
        }
    }

    /// <summary>Тело дублирования базы (см. <see cref="ExecuteCloneInfobase"/>).</summary>
    private async System.Threading.Tasks.Task ExecuteCloneInfobaseCoreAsync()
    {
        var source = SelectedInfobase;
        if (source?.Connection is null)
            return;

        if (source.Connection.Type == ConnectionType.ClientServer)
        {
            await ExecuteCloneServerInfobaseAsync(source);
            return;
        }

        if (source.Connection.Type != ConnectionType.File)
            return;

        var sourceDir = source.Connection.FilePath ?? "";
        if (string.IsNullOrWhiteSpace(sourceDir) || !System.IO.Directory.Exists(sourceDir))
        {
            _dialog.ShowWarning(string.Format(LocalizationManager.T("Clone.SourceMissing"), source.Name), LocalizationManager.T("Clone.Title"));
            return;
        }

        if (source.IsRunning)
        {
            _dialog.ShowWarning(string.Format(LocalizationManager.T("Clone.SourceRunning"), source.Name), LocalizationManager.T("Clone.Title"));
            RefreshRunningFlags();
            return;
        }

        var proposed = InfobaseCloneHelper.ProposeCloneName(source.Name);
        var nameWindow = new Workspace1C.NameInputWindow(
            LocalizationManager.T("Clone.NameTitle"),
            LocalizationManager.T("Clone.NamePrompt"),
            LocalizationManager.T("Common.Ok"),
            proposed);
        if (!nameWindow.ShowDialogSync(OwnerWindow()))
            return;
        var newName = nameWindow.Result?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        var targetDir = InfobaseCloneHelper.BuildTargetDirectory(sourceDir, newName);
        if (string.IsNullOrEmpty(targetDir))
        {
            _dialog.ShowWarning(LocalizationManager.T("Clone.SourceMissing"), LocalizationManager.T("Clone.Title"));
            return;
        }

        var sizeBytes = InfobaseCloneHelper.GetDirectorySize(sourceDir);
        var sizeText = sizeBytes >= 0 ? Infobase.FormatSize(sizeBytes) : "?";
        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("Clone.Confirm"), source.Name, newName, sizeText, targetDir),
                LocalizationManager.T("Clone.Title")))
            return;

        var copied = await System.Threading.Tasks.Task.Run(() => InfobaseCloneHelper.CopyDirectory(sourceDir, targetDir));
        if (!copied)
        {
            _dialog.ShowError(LocalizationManager.T("Clone.Failed"), LocalizationManager.T("Clone.Title"));
            return;
        }

        var clone = System.Text.Json.JsonSerializer.Deserialize<Infobase>(
            System.Text.Json.JsonSerializer.Serialize(source))!;
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = newName;
        clone.Connection.FilePath = targetDir;
        clone.IsFavorite = false;
        clone.IsPinned = false;
        clone.IsSelected = false;
        clone.FavoriteHotkeyNumber = 0;
        clone.LastLaunchDate = null;
        clone.LaunchHistory = new List<LaunchHistoryEntry>();
        clone.FileSizeBytes = null;
        clone.FileLastWriteTimeUtc = null;

        var index = _allInfobases.IndexOf(source);
        if (index >= 0)
            _allInfobases.Insert(index + 1, clone);
        else
            _allInfobases.Add(clone);

        OnPropertyChanged(nameof(Infobases));
        SaveSilently();
        RebuildTree();
        SelectedInfobase = clone;
        RefreshRunningFlags();
        _logger.Info($"[clone] «{source.Name}» → «{newName}» ({targetDir})");
        _dialog.ShowInfo(string.Format(LocalizationManager.T("Clone.Done"), newName, targetDir), LocalizationManager.T("Clone.Title"));
    }

    /// <summary>
    /// Клонирует клиент-серверную базу (0.3.9.100, функция №10): диалог параметров
    /// (имя, сервер/Ref, СУБД, режим) → окно прогресса → <see cref="ServerCloneService"/>
    /// → новая запись списка рядом с источником + экспорт в ibases.v8i.
    /// </summary>
    private async System.Threading.Tasks.Task ExecuteCloneServerInfobaseAsync(Infobase source)
    {
        // Запрет для запущенной базы (как у файлового клона).
        if (source.IsRunning)
        {
            _dialog.ShowWarning(
                string.Format(LocalizationManager.T("Clone.SourceRunning"), source.Name),
                LocalizationManager.T("Clone.Title"));
            RefreshRunningFlags();
            return;
        }

        // Локально известные Ref на том же сервере — для уникализации имени базы клона.
        var existingRefNames = _allInfobases
            .Where(ib => ib.Connection?.Type == ConnectionType.ClientServer &&
                         string.Equals(ib.Connection.Server, source.Connection.Server, StringComparison.OrdinalIgnoreCase))
            .Select(ib => ib.Connection.DatabaseName)
            .ToList();

        var dialog = new Workspace1C.CloneServerInfobaseWindow(
            source,
            _groups,
            existingRefNames);
        if (!dialog.ShowDialogSync(OwnerWindow()) || dialog.Request is null || dialog.ResultConnection is null)
            return;

        var request = dialog.Request;
        var clone = System.Text.Json.JsonSerializer.Deserialize<Infobase>(
            System.Text.Json.JsonSerializer.Serialize(source))!;
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = request.CloneName;
        clone.Group = request.GroupPath;
        clone.Connection = dialog.ResultConnection;
        // Авторизации записи наследуются от источника (удобство запуска; пользователи
        // 1С в самой ИБ после RestoreIB пусты — стандартное поведение платформы).
        clone.Connection.User = source.Connection.User;
        clone.Connection.Password = source.Connection.Password;
        clone.Connection.AuthenticationMode = source.Connection.AuthenticationMode;
        clone.IsFavorite = false;
        clone.IsPinned = false;
        clone.IsSelected = false;
        clone.FavoriteHotkeyNumber = 0;
        clone.LastLaunchDate = null;
        clone.LaunchHistory = new List<LaunchHistoryEntry>();
        clone.FileSizeBytes = null;
        clone.FileLastWriteTimeUtc = null;

        var index = _allInfobases.IndexOf(source);
        if (index >= 0)
            _allInfobases.Insert(index + 1, clone);
        else
            _allInfobases.Add(clone);

        OnPropertyChanged(nameof(Infobases));
        SaveSilently();
        RebuildTree();
        ExportToIbasesAfterLocalChange();
        SelectedInfobase = clone;
        RefreshRunningFlags();
        _logger.Info($"[clone] серверная ИБ «{source.Name}» → «{request.CloneName}» ({request.Server}\\{request.DatabaseName})");
        _dialog.ShowInfo(
            string.Format(
                LocalizationManager.T("CloneServer.DoneFormat"),
                request.CloneName, request.Server, request.DatabaseName),
            LocalizationManager.T("CloneServer.Title"));
    }

    // ====================== Тестовая копия (п.10, 1.0.0.92) ======================

    private ICommand? _testDeployCommand;

    /// <summary>
    /// Команда «Развернуть тестовую копию» (п.10, 1.0.0.92): мастер параметров тестовой базы
    /// → <see cref="TestDeployService"/> (выгрузка .dt → создание/обновление → загрузка →
    /// регламенты → признаки отличия → группа «Тест») → новая/обновлённая запись списка.
    /// </summary>
    public ICommand TestDeployCommand =>
        _testDeployCommand ??= new RelayCommand(
            async _ => await ExecuteShowTestDeployAsync(SelectedInfobase),
            _ => SelectedInfobase?.Connection?.Type is ConnectionType.File or ConnectionType.ClientServer);

    private System.Threading.Tasks.Task ExecuteShowTestDeployAsync(Infobase source)
    {
        if (source?.Connection is null)
            return System.Threading.Tasks.Task.CompletedTask;

        // Issue #79: окно развёртывания показывается НЕМОДАЛЬНО (по аналогии с
        // «Серверы 1С») — пока копия создаётся, можно открыть базу и работать.
        // Одно окно на базу-источник: повторный вызов активирует существующее.
        var desktopLifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var existingWindow = desktopLifetime?.Windows
            .OfType<Workspace1C.TestDeployWindow>()
            .FirstOrDefault(w => ReferenceEquals(w.Source, source));
        if (existingWindow is not null)
        {
            existingWindow.Activate();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        var window = new Workspace1C.TestDeployWindow(source, _allInfobases.ToList(), _groups.ToList());
        window.DeployCompleted += ApplyTestDeployResult;
        window.Show();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Применяет результат успешного развёртывания из немодального окна (issue #79):
    /// добавляет/обновляет тестовую базу в списке, группу «Тест», сохраняет и
    /// показывает итоговое сообщение. Пост-обработка идентична прежней модальной.
    /// </summary>
    private void ApplyTestDeployResult(Workspace1C.TestDeployWindow window)
    {
        var source = window.Source;
        var result = window.DeployResult;
        if (result is null)
            return;
        var target = result.Target;
        if (target is null)
            return;

        try
        {
            if (result.TargetCreated)
            {
                // Убираем запись-тёзку (переименованную тестовую копию) и добавляем новую.
                foreach (var twin in _allInfobases
                             .Where(b => b is not null && TestDeployPlanner.MatchesTargetName(
                                 target.Name, b.Name, allowPrefixed: true))
                             .ToList())
                    _allInfobases.Remove(twin);

                var index = _allInfobases.IndexOf(source);
                if (index >= 0)
                    _allInfobases.Insert(index + 1, target);
                else
                    _allInfobases.Add(target);
            }
            else if (result.TargetUpdated)
            {
                var index = _allInfobases.IndexOf(target);
                if (index >= 0)
                    _allInfobases[index] = target;
                else
                    _allInfobases.Add(target);
            }

            if (window.CreatedGroup is not null &&
                !_groups.Any(g => string.Equals(g.Name, window.CreatedGroup.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _groups.Add(window.CreatedGroup);
            }

            OnPropertyChanged(nameof(Infobases));
            SaveSilently();
            SaveGroupsSilently();
            RebuildTree();
            SelectedInfobase = target;
            RefreshRunningFlags();
            _logger.Info($"[testdeploy] «{source.Name}» → «{target.Name}» ({(result.TargetCreated ? "создана" : "обновлена")})");

            // Issue #79 (комментарий 7OH 2026-11-05): DoneCreated/DoneUpdated содержат {0} —
            // имя тестовой базы; раньше показывалось без string.Format («не подставило имя
            // в сообщение»). Единая точка форматирования — TestDeployService.DoneMessage.
            var doneMessage = TestDeployService.DoneMessage(result);
            if (result.Warnings.Count > 0)
            {
                _dialog.ShowWarning(
                    doneMessage + "\n\n" + string.Join("\n", result.Warnings),
                    LocalizationManager.T("TestDeploy.Title"));
            }
            else
            {
                _dialog.ShowInfo(doneMessage, LocalizationManager.T("TestDeploy.Title"));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка применения результата развёртывания тестовой копии", ex);
            _dialog.ShowError(ex.Message, LocalizationManager.T("TestDeploy.Title"));
        }
    }
}
#endif