#if LINUX
using System.Windows.Input;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): запуск 1С, избранное и горячие клавиши запуска (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Слоты Alt+1…Alt+9 в порядке назначения: ключи баз.</summary>
    private readonly List<string> _favoriteHotkeyIds = new();

    // ---- Действие после запуска базы или конфигуратора ----
    private string _afterLaunchAction = "None";

    /// <summary>
    /// Что делать с окном после успешного запуска: "None", "MinimizeToTray" или "Close".
    /// Хранится строкой, как в WPF-версии и в файле настроек.
    /// </summary>
    public string AfterLaunchAction
    {
        get => _afterLaunchAction;
        set
        {
            if (SetProperty(ref _afterLaunchAction, value))
            {
                _settings.AfterLaunchAction = value;
                SaveSettingsSilently();
            }
        }
    }

    /// <summary>
    /// Глубина истории запусков одной базы (issue #246): максимальное количество записей
    /// истории запусков, которое запоминается для информационной базы. Минимум 1.
    /// </summary>
    public int MaxLaunchHistoryPerBase
    {
        get => Math.Max(1, _settings.MaxLaunchHistoryPerBase);
        set
        {
            var v = Math.Max(1, value);
            if (_settings.MaxLaunchHistoryPerBase == v)
                return;
            _settings.MaxLaunchHistoryPerBase = v;
            SaveSettingsSilently();
        }
    }

    /// <summary>Запрос к главному окну выполнить действие после успешного запуска.</summary>
    public event Action<Models.AfterLaunchAction>? AfterLaunchRequested;

    /// <summary>Оповещает главное окно, если настройка требует действия.</summary>
    public void NotifyAfterLaunch()
    {
        var action = Models.AfterLaunchActionHelper.Parse(_afterLaunchAction);
        if (action != Models.AfterLaunchAction.None)
            AfterLaunchRequested?.Invoke(action);
    }

    private void Launch(ICommand launchVmCommand, LaunchKind kind)
    {
        // Issue #120: активация открытого окна Конфигуратора вместо нового запуска.
        // На Linux перечисление/активация окон не поддержаны (нужны wmctrl/xdotool) —
        // TryActivateConfiguratorWindow всегда возвращает false и выполняется
        // обычный запуск (graceful fallback).
        if (kind == LaunchKind.Configurator && TryActivateConfiguratorWindow(SelectedInfobase))
        {
            _logger.Info($"Активировано окно Конфигуратора базы «{SelectedInfobase!.Name}» вместо запуска нового (issue #120)");
            return;
        }

        launchVmCommand.Execute(kind);
    }

    /// <summary>
    /// Запуск с разовыми параметрами: диалог правит параметры только на один
    /// запуск, сохранённое значение базы возвращается в любом случае.
    /// </summary>
    private void LaunchWithParams(LaunchKind kind)
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        var dialog = new Workspace1C.LaunchParametersWindow(
            infobase.LaunchParameters ?? string.Empty,
            CustomLaunchParameters,
            SetCustomLaunchParameters);
        if (!dialog.ShowDialogSync(OwnerWindow()))
            return;

        var saved = infobase.LaunchParameters ?? string.Empty;
        try
        {
            infobase.LaunchParameters = dialog.Result;
            Launch(_launchVm.LaunchCommand, kind);
        }
        finally
        {
            infobase.LaunchParameters = saved;
            // Успешный запуск сохраняет список баз изнутри, то есть подменённое
            // значение уже успело уйти на диск. Возвращаем файл к прежнему виду,
            // иначе разовые параметры остались бы у базы навсегда.
            SaveSilently();
        }
    }

    /// <summary>Пользовательские параметры запуска, добавленные в справочник параметров (issue #141).</summary>
    public IReadOnlyList<string> CustomLaunchParameters => _customLaunchParameters;

    /// <summary>
    /// Сохраняет список пользовательских параметров запуска (дополняет справочник ключей командной строки).
    /// </summary>
    public void SetCustomLaunchParameters(IEnumerable<string> values)
    {
        _customLaunchParameters = new List<string>(
            (values ?? Enumerable.Empty<string>())
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        _settings.CustomLaunchParameters = _customLaunchParameters.ToList();
        SaveSettingsSilently();
    }

    /// <summary>
    /// Запуск с авторизацией (issue #48): платформа обязательно спрашивает имя и пароль.
    /// Сохранённые учётные данные (общие и отдельные для режима) не подставляются, ключ
    /// <c>/WA-</c> отключает автовход по учётной записи ОС — см.
    /// <see cref="OneCLauncher.RequestAuthPrompt"/>. Учётные данные базы не меняются:
    /// флаг «съедается» сборкой аргументов, даже если запуск ушёл после асинхронной
    /// pre-команды (раньше восстановление в finally опережало такой запуск).
    /// </summary>
    private void LaunchWithAuth(LaunchKind kind)
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        OneCLauncher.RequestAuthPrompt(infobase);
        Launch(_launchVm.LaunchCommand, kind);
    }

    /// <summary>
    /// Запуск базы из меню трея: прямо по ссылке, не трогая выделение в списке.
    /// В Windows-версии это делает LaunchInfobaseById (MainViewModel.Commands.cs:544),
    /// и она тоже не меняет SelectedInfobase: иначе запуск из трея переставлял бы
    /// выделение, правую панель и строку состояния. Источник записи в истории
    /// тот же, что у автора. Пользовательские скрипты (функция №8, 0.3.9.98)
    /// выполняются и при запуске из трея.
    /// </summary>
    public void LaunchFromTray(Infobase ib, bool configurator)
    {
        if (!_allInfobases.Contains(ib))
            return;

        _ = LaunchFromTrayCoreAsync(ib, configurator);
    }

    /// <summary>Асинхронное ядро запуска из трея: pre → запуск → post.</summary>
    private async Task LaunchFromTrayCoreAsync(Infobase ib, bool configurator)
    {
        try
        {
            await RunPreLaunchScriptAsync(ib);
            // Issue #120: активация открытого окна Конфигуратора; на Linux активация
            // окон не поддержана — TryActivateConfiguratorWindow вернёт false и
            // выполнится обычный запуск (graceful fallback).
            if (configurator && TryActivateConfiguratorWindow(ib))
            {
                _logger.Info($"[tray] Активировано окно Конфигуратора базы «{ib.Name}» вместо запуска нового (issue #120)");
                return;
            }
            var ok = configurator
                ? _launcher.Launch(ib, Services.OneCLaunchMode.Configurator)
                : _launcher.Launch(ib, Services.OneCLaunchMode.Enterprise);

            if (ok)
            {
                RunPostLaunchScript(ib);
                ib.AddLaunchHistory(configurator ? "Configurator" : "Enterprise",
                    BuildLaunchDetails("tray", ib));
                SaveSilently();
                OnPropertyChanged(nameof(RecentInfobases));
                _logger.Info($"[tray] Запущена «{ib.Name}» ({(configurator ? "Конфигуратор" : "Предприятие")})");
                NotifyAfterLaunch();
            }
            else
            {
                _logger.Warn($"[tray] Не удалось запустить «{ib.Name}»");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[tray] Ошибка запуска «{ib.Name}»", ex);
        }
    }

    private void OnLaunched()
    {
        if (SelectedInfobase is not null)
        {
            SelectedInfobase.AddLaunchHistory(
                LocalizationManager.T("Main.LaunchAction"),
                BuildLaunchDetails(string.Empty, SelectedInfobase));
            SaveSilently();
        }

        // Список недавних изменился, и его показывает меню трея.
        OnPropertyChanged(nameof(RecentInfobases));

        // Одна точка на все пути запуска: команды окна, контекстное меню и трей
        // приходят сюда же, в отличие от WPF, где уведомление расставлено трижды.
        NotifyAfterLaunch();
    }

    /// <summary>
    /// Выполняет пользовательскую команду «перед запуском» (функция №8, 0.3.9.98):
    /// ожидание завершения с таймаутом 30 секунд. При ошибке/таймауте предупреждает
    /// пользователя, НО не блокирует запуск базы — он продолжается.
    /// </summary>
    private async Task RunPreLaunchScriptAsync(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PreLaunchCommand))
            return;

        var ok = await ExternalCommandRunner.RunAsync(
            ib.PreLaunchCommand, ExternalCommandRunner.DefaultPreCommandTimeoutMs);

        if (ok)
        {
            _logger.Info($"Pre-команда базы «{ib.Name}» выполнена: {ib.PreLaunchCommand}");
        }
        else
        {
            _logger.Warn($"Pre-команда базы «{ib.Name}» завершилась с ошибкой или таймаутом: {ib.PreLaunchCommand}");
            _dialog.ShowWarning(
                string.Format(LocalizationManager.T("Launch.PreCommandFailed"), ib.PreLaunchCommand),
                LocalizationManager.T("Launch.CommandsTitle"));
        }
    }

    /// <summary>
    /// Запускает пользовательскую команду «после запуска» (функция №8, 0.3.9.98)
    /// без ожидания завершения (fire-and-forget).
    /// </summary>
    private void RunPostLaunchScript(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PostLaunchCommand))
            return;

        ExternalCommandRunner.RunDetached(ib.PostLaunchCommand);
        _logger.Info($"Запущена post-команда базы «{ib.Name}»: {ib.PostLaunchCommand}");
    }

    /// <summary>
    /// Детали истории запуска с маркером пользовательских команд (0.3.9.98):
    /// например «pre: ras connect …; post: start …». Пустые части пропускаются.
    /// </summary>
    private static string BuildLaunchDetails(string baseDetails, Infobase ib)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDetails))
            parts.Add(baseDetails);
        if (!string.IsNullOrWhiteSpace(ib.PreLaunchCommand))
            parts.Add("pre: " + ib.PreLaunchCommand);
        if (!string.IsNullOrWhiteSpace(ib.PostLaunchCommand))
            parts.Add("post: " + ib.PostLaunchCommand);
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Открывает диалог ввода ссылки на информационную базу и запускает её,
    /// как «Перейти по ссылке» в стандартном загрузчике 1С.
    /// </summary>
    private void OpenInfobaseByLink()
    {
        var dialog = new Workspace1C.LinkInputWindow();
        if (!dialog.ShowDialogSync(OwnerWindow()) || string.IsNullOrWhiteSpace(dialog.Result))
            return;

        var link = dialog.Result;
        _logger.Info($"Запуск 1С по ссылке: {link}");
        if (!OneCLauncher.LaunchByLink(link))
        {
            _dialog.ShowError(string.Format(LocalizationManager.T("Main.ErrOpenLink"), link));
            return;
        }

        NotifyAfterLaunch();
    }

    private void ToggleFavorite() => ToggleFavoriteFor(SelectedInfobase);

    private void TogglePin() => TogglePinFor(SelectedInfobase);

    private void DumpInfobaseDt(object? parameter) =>
        DumpViaDesigner(parameter, OneCLauncher.DesignerBatchOperation.DumpIB, ".dt",
            "Main.DumpDtDialogTitle", "Main.DtFileFilter", "Main.DumpDtStarted", "Main.DumpDtTitle");

    private void DumpConfigurationCf(object? parameter) =>
        DumpViaDesigner(parameter, OneCLauncher.DesignerBatchOperation.DumpCfg, ".cf",
            "Main.DumpCfDialogTitle", "Main.CfFileFilter", "Main.DumpCfStarted", "Main.DumpCfTitle");

    /// <summary>
    /// Выгрузка базы или конфигурации пакетным запуском конфигуратора.
    /// Общая часть обеих команд: в версии для Windows это два почти одинаковых метода.
    /// </summary>
    private void DumpViaDesigner(
        object? parameter,
        OneCLauncher.DesignerBatchOperation operation,
        string extension,
        string dialogTitleKey,
        string filterKey,
        string startedKey,
        string titleKey)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null)
            return;

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T(dialogTitleKey),
            BuildExportFileName(SanitizeFileName(ib.Name), extension,
                _settings.AddTimestampToExportFileName, _settings.ExportTimestampFormat),
            LocalizationManager.T(filterKey));
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!OneCLauncher.RunDesignerBatch(ib, operation, path))
            return;

        ib.AddLaunchHistory(operation == OneCLauncher.DesignerBatchOperation.DumpIB ? "DumpDT" : "DumpCF", path);
        SaveSilently();
        _dialog.ShowInfo(LocalizationManager.T(startedKey), LocalizationManager.T(titleKey));
    }

    private void RefreshConfigurationInfo(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null)
            return;

        // Сброса вердиктов о недоступности COM здесь нет: он живёт
        // в OneCComConnector.cs, а тот в Linux-сборку не входит.
        // Временная индикация процесса (issue #244): надпись «(обновление информации)»
        // в колонке «Конфигурация», если она видима; иначе в «№ релиза»; иначе в «Название».
        var indicatorColumn = _settings.ShowConfigurationColumn ? "Configuration"
            : _settings.ShowConfigurationVersionColumn ? "ConfigurationVersion"
            : "Name";
        ib.SetConfigInfoIndicator(true, indicatorColumn);
        // Строки Avalonia не следят за PropertyChanged модели — пересобираем дерево,
        // чтобы надпись появилась в ячейке выбранной колонки.
        RebuildTree();

        var baseName = ib.Name;
        _ = Task.Run(() =>
        {
            OneCConfigInfo? info = null;
            try
            {
                // Режим чтения сведений — «Конфигуратор» (issue #236): учётные данные берутся
                // из ConfiguratorAuth при её наличии, иначе из авторизации базы.
                info = ConfigurationInfoService.ReadAndApply(ib, overwriteExisting: true,
                    mode: OneCLaunchMode.Configurator);
            }
            catch
            {
                // причина уходит в LastComError и показывается ниже
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Надпись очищается независимо от результата (успех или ошибка).
                ib.SetConfigInfoIndicator(false, indicatorColumn);
                RebuildTree();

                if (info is null)
                {
                    var comError = ConfigurationInfoService.LastComError;
                    var detail = string.IsNullOrWhiteSpace(comError)
                        ? LocalizationManager.T("Main.ConfigInfoCheckHint")
                        : string.Format(LocalizationManager.T("Main.ConfigInfoReason"), comError);
                    _logger.Warn($"Не удалось получить информацию о конфигурации базы «{baseName}». {detail}");
                    _dialog.ShowWarning(
                        string.Format(LocalizationManager.T("Main.ErrConfigInfo"), baseName, detail),
                        LocalizationManager.T("Main.ConfigInfoTitle"));
                    return;
                }

                SaveSilently();

                var name = info.Value.Name.Trim();
                var version = info.Value.Version.Trim();
                _logger.Info($"Обновлена информация о конфигурации базы «{baseName}»: {name} ({version})");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoBase"), baseName));
                if (name.Length > 0)
                    sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoName"), name));
                if (version.Length > 0)
                    sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoVersion"), version));
                _dialog.ShowInfo(sb.ToString().TrimEnd(), LocalizationManager.T("Main.ConfigInfoTitle"));
            });
        });
    }

    private static string SanitizeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "base").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "base" : s;
    }

    private void ToggleFavoriteFor(Infobase? infobase)
    {
        if (infobase is null)
            return;

        infobase.IsFavorite = !infobase.IsFavorite;
        // Кэш «Избранное»/«Недавние» (issue #21): сбрасываем всегда — ниже ApplyFilter
        // идёт только при активном фильтре, а без него кэш остался бы устаревшим.
        _listFilter.Invalidate();

        // Снятая звезда освобождает слот явно: пересчёт сам этого не сделает,
        // он выбрасывает только ключи баз, которых больше нет в списке.
        // Тот же порядок в версии для Windows (MainViewModel.Commands.cs:460).
        if (!infobase.IsFavorite)
            _favoriteHotkeyIds.Remove(FavoriteKey(infobase));

        SyncFavoriteHotkeys();
        SaveSilently();
        // Слоты живут в настройках, а не в списке баз, поэтому сохраняются
        // отдельно: SaveSilently пишет только список.
        SaveSettingsSilently();

        // Состав списка меняется только при активном временном фильтре: там база
        // может выпасть из выборки. Без фильтра строка перекрашивается сама
        // по уведомлению модели, и пересобирать дерево незачем.
        if (IsFilterModeActive())
            ApplyFilter();
    }

    // ======================= Горячие клавиши избранного =======================

    /// <summary>
    /// Ключ базы для слота. Идентификатор, а при его отсутствии имя: тот же
    /// ключ, что и в версии для Windows, поэтому список слотов переносится
    /// между платформами через общий файл настроек.
    /// </summary>
    private static string FavoriteKey(Infobase ib) =>
        !string.IsNullOrEmpty(ib.Id) ? ib.Id : "name:" + ib.Name;

    /// <summary>Упорядоченный список ключей слотов, для окна настроек.</summary>
    public IReadOnlyList<string> FavoriteHotkeyIds => _favoriteHotkeyIds;

    /// <summary>
    /// Возвращает базу по ключу слота. Приватная база заблокированного профиля
    /// не возвращается: она не запускается ни горячей клавишей, ни из трея (0.3.9.85).
    /// </summary>
    public Infobase? FindByFavoriteKey(string key) =>
        _allInfobases.FirstOrDefault(ib => FavoriteKey(ib) == key && IsVisibleForPrivateFilter(ib));

    /// <summary>
    /// Избранные базы в порядке слотов 1…9 (для меню трея и командной палитры):
    /// ключи слотов разворачиваются в экземпляры баз, пустые слоты пропускаются.
    /// </summary>
    public System.Collections.Generic.List<(Infobase Base, int Slot)> GetFavoriteInfobasesOrdered()
    {
        var result = new System.Collections.Generic.List<(Infobase, int)>();
        for (var i = 0; i < _favoriteHotkeyIds.Count; i++)
        {
            var ib = FindByFavoriteKey(_favoriteHotkeyIds[i]);
            if (ib is not null)
                result.Add((ib, i + 1));
        }
        return result;
    }

    /// <summary>
    /// Раздаёт слоты избранным базам и проставляет номера на самих базах.
    /// Список хранится в настройках, поэтому здесь же переписывается в них:
    /// любое последующее сохранение унесёт его на диск.
    /// </summary>
    private void SyncFavoriteHotkeys()
    {
        try
        {
            // Слот должен соответствовать только текущим избранным базам, иначе
            // вкладка, счётчик и список горячих клавиш разъезжаются (issue #194).
            _favoriteHotkeyIds.RemoveAll(key =>
                !_allInfobases.Any(ib => ib.IsFavorite && FavoriteKey(ib) == key));

            // Избранные без слота получают его в порядке имени, как в версии
            // для Windows. Слотов девять, лишние остаются без номера.
            foreach (var ib in _allInfobases
                         .Where(i => i.IsFavorite)
                         .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                var key = FavoriteKey(ib);
                if (!_favoriteHotkeyIds.Contains(key) && _favoriteHotkeyIds.Count < 9)
                    _favoriteHotkeyIds.Add(key);
            }

            foreach (var ib in _allInfobases)
            {
                var idx = _favoriteHotkeyIds.IndexOf(FavoriteKey(ib));
                ib.FavoriteHotkeyNumber = idx >= 0 ? idx + 1 : 0;
            }

            _settings.FavoriteHotkeyIds = _favoriteHotkeyIds.ToList();
        }
        catch (Exception ex)
        {
            // Избранное не повод ронять окно: без слотов список просто
            // остаётся без номеров.
            _logger.Error("Не удалось разложить слоты избранного", ex);
        }
    }

    /// <summary>Заменяет порядок слотов, заданный в окне настроек.</summary>
    public void SetFavoriteHotkeyOrder(IEnumerable<string> orderedKeys)
    {
        _favoriteHotkeyIds.Clear();
        foreach (var key in orderedKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Take(9))
            _favoriteHotkeyIds.Add(key);
        SyncFavoriteHotkeys();
        SaveSettingsSilently();
    }

    /// <summary>
    /// Запускает Предприятие для базы из слота. Запуск идёт обычным путём окна,
    /// а не прямым вызовом лаунчера, как в версии для Windows: так работают
    /// и разовые параметры, и действие после запуска.
    /// </summary>
    public void LaunchFavoriteByHotkey(int number)
    {
        if (number < 1 || number > _favoriteHotkeyIds.Count)
            return;

        var ib = FindByFavoriteKey(_favoriteHotkeyIds[number - 1]);
        if (ib is null)
            return;

        SelectedInfobase = ib;
        _logger.Info($"Запуск избранной базы «{ib.Name}» по Alt+{number}");
        Launch(_launchVm.LaunchCommand, LaunchKind.Enterprise);
    }

    // ======================= Закладки (горячие клавиши 1–9) =======================

    /// <summary>Ставит/снимает закладку для выбранной базы (Ctrl+Shift+P, Ctrl+щелчок).</summary>
    public void ToggleBookmarkForCurrent()
    {
        if (SelectedInfobase is { } ib)
            ToggleBookmark(ib);
    }

    /// <summary>Ставит/снимает закладку для указанной базы (Ctrl+щелчок по строке).</summary>
    public void ToggleBookmark(Infobase ib)
    {
        if (_favoriteHotkeyIds.Contains(FavoriteKey(ib)))
            RemoveBookmark(ib);
        else
            AssignNextBookmarkSlot(ib);
    }

    /// <summary>Назначает базе явный номер закладки 1..9 (Ctrl+Shift+N).</summary>
    public void AssignBookmarkSlot(Infobase? ib, int number)
    {
        if (ib is null)
            return;
        ib.IsFavorite = true;
        BookmarkSlotHelper.AssignSlot(_favoriteHotkeyIds, FavoriteKey(ib), number);
        FinishBookmarkChange();
    }

    /// <summary>Назначает базе первый свободный слот закладки (Ctrl+Shift+P, Ctrl+щелчок).</summary>
    private void AssignNextBookmarkSlot(Infobase ib)
    {
        ib.IsFavorite = true;
        if (!BookmarkSlotHelper.AssignNextFreeSlot(_favoriteHotkeyIds, FavoriteKey(ib)))
        {
            _dialog.ShowInfo(
                LocalizationManager.T("Main.BookmarksAllSlotsFull"),
                LocalizationManager.T("Main.Bookmarks"));
            return;
        }
        FinishBookmarkChange();
    }

    /// <summary>Снимает закладку с базы: номер уходит, избранность («звезда») сохраняется.</summary>
    public void RemoveBookmark(Infobase? ib)
    {
        if (ib is null)
            return;
        BookmarkSlotHelper.RemoveFromSlot(_favoriteHotkeyIds, FavoriteKey(ib));
        FinishBookmarkChange();
    }

    /// <summary>Очищает все закладки (Ctrl+Alt+X). Избранность баз сохраняется.</summary>
    public void ClearAllBookmarks()
    {
        BookmarkSlotHelper.ClearAllSlots(_favoriteHotkeyIds);
        FinishBookmarkChange();
    }

    /// <summary>
    /// Завершает изменение состава закладок: пересчитывает слоты, обновляет список
    /// при активном временном фильтре и сохраняет настройки вместе со списком баз.
    /// </summary>
    private void FinishBookmarkChange()
    {
        SyncFavoriteHotkeys();
        // Закладки меняют IsFavorite — кэш «Избранное» сбрасываем (issue #21).
        _listFilter.Invalidate();
        if (IsFilterModeActive())
            ApplyFilter();
        SaveSilently();
        SaveSettingsSilently();
    }

    /// <summary>
    /// Переходит к базе по номеру закладки (Ctrl+N), раскрывая свёрнутую ветку группы,
    /// после чего пересобирает дерево и выделяет базу.
    /// </summary>
    public void NavigateToBookmark(int number)
    {
        var key = BookmarkSlotHelper.FindKeyBySlot(_favoriteHotkeyIds, number);
        if (key is null)
            return;
        var ib = FindByFavoriteKey(key);
        if (ib is null)
            return;

        // Целевая навигация (issue #31): окно во время серии пересборок команды не
        // выполняет промежуточное восстановление выделения — единственный reveal выполнит
        // запрос RevealFindInListRequested по окончании операции (паритет с WPF).
        IsRevealNavigationInProgress = true;
        try
        {
            // 1-й проход: раскрываем цепочку групп-предков и снимаем их ключи из набора
            // свёрнутых (ExpandPathTo). Иначе RebuildTree по _collapsedGroups снова свернул
            // бы группу-предок и строка цели осталась бы скрытой (issue #31).
            ExpandPathTo(ib);

            // Пересборка дерева, чтобы контейнер строки создался (issue #31).
            RebuildTree();

            // 2-й проход после пересборки: узлы дерева созданы заново — повторно раскрываем
            // цепочку и снимаем ключи свёрнутых на новых узлах (параллель с WPF-версией).
            ExpandPathTo(ib);

            // Цель выставляем ПОСЛЕ пересборки (паритет с WPF): пересборка сбрасывает
            // выделение дерева, и цель, выставленная до неё, могла бы быть потеряна.
            SelectedInfobase = ib;
            // Переход к строке (issue #31): сообщаем окну, что нужно выделить строку
            // и прокрутить к ней. Без этого после оптимизации переключения вкладок
            // (1.0.0.26+) выделение не восстанавливалось и прокрутки не было.
            RevealFindInListRequested?.Invoke();
        }
        finally
        {
            IsRevealNavigationInProgress = false;
        }
    }

    /// <summary>
    /// Запускает базу по номеру закладки: Предприятие (Alt+N) или Конфигуратор (Ctrl+Alt+N).
    /// Запуск идёт обычным путём окна (как LaunchFavoriteByHotkey), а не прямым лаунчером.
    /// </summary>
    public void LaunchBookmark(int number, bool configurator)
    {
        if (number < 1 || number > _favoriteHotkeyIds.Count)
            return;

        var ib = FindByFavoriteKey(_favoriteHotkeyIds[number - 1]);
        if (ib is null)
            return;

        SelectedInfobase = ib;
        _logger.Info(configurator
            ? $"Запуск Конфигуратора избранной базы «{ib.Name}» по Ctrl+Alt+{number}"
            : $"Запуск избранной базы «{ib.Name}» по Alt+{number}");
        Launch(_launchVm.LaunchCommand, configurator ? LaunchKind.Configurator : LaunchKind.Enterprise);
    }

    /// <summary>
    /// Запускает Предприятие для всех баз с закладками (Alt+E). Массовый запуск идёт
    /// напрямую через лаунчер, как запуск из трея (LaunchFromTray), чтобы не таскать
    /// выделение по каждой базе.
    /// </summary>
    public void LaunchAllBookmarks()
    {
        _ = LaunchAllBookmarksCoreAsync();
    }

    /// <summary>Асинхронное ядро массового запуска: для каждой базы — pre → запуск → post.</summary>
    private async Task LaunchAllBookmarksCoreAsync()
    {
        try
        {
            foreach (var key in BookmarkSlotHelper.GetAllKeys(_favoriteHotkeyIds))
            {
                var ib = FindByFavoriteKey(key);
                if (ib is null)
                    continue;
                await RunPreLaunchScriptAsync(ib);
                var ok = _launcher.Launch(ib, Services.OneCLaunchMode.Enterprise);
                if (ok)
                {
                    RunPostLaunchScript(ib);
                    ib.AddLaunchHistory("Enterprise", BuildLaunchDetails("bookmarks", ib));
                    SaveSilently();
                    OnPropertyChanged(nameof(RecentInfobases));
                    _logger.Info($"Запущена избранная база «{ib.Name}» по Alt+E");
                }
                else
                {
                    _logger.Warn($"Не удалось запустить избранную базу «{ib.Name}» по Alt+E");
                }
            }
            NotifyAfterLaunch();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка массового запуска закладок (Alt+E)", ex);
        }
    }

    /// <summary>Занятые слоты с базами в порядке нумерации (для меню Ctrl+B).</summary>
    public IReadOnlyList<(int Number, Infobase Base)> GetBookmarks()
    {
        var result = new List<(int Number, Infobase Base)>();
        for (var i = 0; i < _favoriteHotkeyIds.Count && i < 9; i++)
        {
            var ib = FindByFavoriteKey(_favoriteHotkeyIds[i]);
            if (ib is not null)
                result.Add((i + 1, ib));
        }
        return result;
    }

    private void TogglePinFor(Infobase? infobase)
    {
        if (infobase is null)
            return;

        infobase.IsPinned = !infobase.IsPinned;
        SaveSilently();
        UpdatePinnedSection(infobase);
    }

    /// <summary>
    /// Точечно обновляет узел «Закреплённые», как это делает WPF-версия
    /// (ApplyPinToggle / UpdatePinnedSection): полная пересборка дерева здесь
    /// стоила бы пересоздания всех строк и потери выделения. Узел правится
    /// всегда, а в видимый список попадает только когда он там уместен:
    /// AllGroupNodes переживает и фильтр, и отключение группировки, и после
    /// возврата к группам список берётся именно оттуда.
    /// </summary>
    private void UpdatePinnedSection(Infobase infobase)
    {
        var pinned = AllGroupNodes.FirstOrDefault(node => node.Group is null
            && string.Equals(node.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal));

        // Закрепление меняет и порядок внутри своей группы: закреплённые идут
        // первыми (GroupSortOrder). Без этого строка осталась бы на прежнем
        // месте до следующей полной пересборки.
        SortOwningNode(infobase);

        if (infobase.IsPinned)
        {
            if (pinned is null)
            {
                pinned = new GroupNodeViewModel(null, marker: GroupNodeViewModel.PinnedMarker);
                pinned.Infobases.Add(infobase);
                pinned.PopulateItems(_showEmptyGroups);
                ApplyExpandedState(pinned);
                SubscribeExpandedTracking(pinned);
                AllGroupNodes.Insert(0, pinned);
                // Стабильный набор корней (issue #21, Этап F): узел всегда в GroupNodes,
                // видимость применяет ApplyStableRoots (в плоском режиме скрыт).
                ApplyStableRoots(IsFilterModeActive() || !_groupByGroup);
                return;
            }

            if (!pinned.Infobases.Contains(infobase))
            {
                pinned.Infobases.Add(infobase);
                SortNodeInfobases(pinned);
                pinned.PopulateItems(_showEmptyGroups);
            }
            else
            {
                pinned.NotifyCountChanged();
            }

            return;
        }

        if (pinned is null)
            return;

        pinned.Infobases.Remove(infobase);
        if (pinned.Infobases.Count > 0)
        {
            pinned.PopulateItems(_showEmptyGroups);
            return;
        }

        // Узел «Закреплённые» опустел — узел уничтожается: отписываем от коллекций
        // (issue #21, эксперимент A 1.0.0.34), затем удаляем из обоих списков.
        pinned.Detach();
        AllGroupNodes.Remove(pinned);
        ApplyStableRoots(IsFilterModeActive() || !_groupByGroup);
    }

    /// <summary>
    /// Переупорядочивает узел, в котором лежит база, кроме служебного узла
    /// «Закреплённые»: его состав меняется отдельно.
    /// </summary>
    private void SortOwningNode(Infobase infobase)
    {
        foreach (var root in AllGroupNodes)
        {
            var owner = FindNodeWith(root, infobase);
            if (owner is null
                || string.Equals(owner.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                continue;

            SortNodeInfobases(owner);
            owner.PopulateItems(_showEmptyGroups);
            return;
        }
    }

    /// <summary>Ищет узел дерева, в списке баз которого лежит указанная база.</summary>
    private static GroupNodeViewModel? FindNodeWith(GroupNodeViewModel node, Infobase infobase)
    {
        if (node.Infobases.Contains(infobase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeWith(child, infobase);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>Переупорядочивает базы узла по текущему полю сортировки.</summary>
    private void SortNodeInfobases(GroupNodeViewModel node)
    {
        var sorted = ApplyCurrentSort(node.Infobases).ToList();
        node.SetNotificationsSuppressed(true);
        try
        {
            node.Infobases.Clear();
            foreach (var infobase in sorted)
                node.Infobases.Add(infobase);
        }
        finally
        {
            node.SetNotificationsSuppressed(false);
        }
    }
}
#endif