using System.Collections.ObjectModel;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Расширения конфигурации» (выпуск 1.0.0.69): список расширений
/// выбранной базы, выгрузка и удаление расширения, пакетная установка одного
/// файла .cfe в отмеченные базы. Чистый .NET — обе платформы.
/// </summary>
public sealed class ExtensionsViewModel : ViewModelBase
{
    private readonly List<ExtensionTargetRow> _allTargets;
    private CancellationTokenSource? _cts;
    private string _searchText = string.Empty;
    private string _cfePath = string.Empty;
    private string _extensionName = string.Empty;
    // Issue #117: настоящее имя расширения, прочитанное из выбранного .cfe (null — не прочитано).
    private string? _cfeRealName;
    // Issue #117 (диагностика): все найденные в контейнере кандидаты имени с путями.
    private IReadOnlyList<ConfigExtensionService.ExtensionNameCandidate> _cfeNameCandidates =
        Array.Empty<ConfigExtensionService.ExtensionNameCandidate>();
    // Issue #117 п.2: пользователь вручную правил имя расширения — автоподстановка не перезаписывает его.
    private bool _extensionNameEdited;
    private string? _selectedExtension;
    private ExtensionTargetRow? _selectedTarget;
    private bool _isBusy;
    private bool _useIbcmd;
    private readonly StringBuilder _log = new();

    public ExtensionsViewModel(IEnumerable<Infobase> infobases, Infobase? selected)
    {
        _allTargets = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ib => new ExtensionTargetRow(ib))
            .ToList();
        foreach (var row in _allTargets)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExtensionTargetRow.IsChecked))
                    OnPropertyChanged(nameof(CheckedSummary));
            };

        if (selected is not null)
        {
            var row = _allTargets.FirstOrDefault(r => ReferenceEquals(r.Infobase, selected))
                      ?? _allTargets.FirstOrDefault(r => r.Infobase.Id == selected.Id);
            if (row is not null)
            {
                row.IsChecked = true;
                _selectedTarget = row;
            }
        }
        ApplyFilter();
    }

    public ObservableCollection<ExtensionTargetRow> Targets { get; } = new();

    public ObservableCollection<string> Extensions { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    public ExtensionTargetRow? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                Extensions.Clear();
                SelectedExtension = null;
                OnPropertyChanged(nameof(SelectedBaseText));
                // Issue #117 п.3: список расширений получается заранее при смене базы
                // (тихо, без «Обновить»-лога; IsBusy защищает от гонок и двойного запуска).
                _ = PrefetchExtensionsAsync();
            }
        }
    }

    public string SelectedBaseText => _selectedTarget is null
        ? LocalizationManager.T("Extensions.NoBaseSelected")
        : string.Format(LocalizationManager.T("Extensions.SelectedBase"), _selectedTarget.Name);

    public string CfePath
    {
        get => _cfePath;
        set
        {
            // Issue #117 п.2: имя автоподставляется при КАЖДОМ выборе файла (перезаписывает
            // предыдущее), если пользователь не вводил имя вручную после последнего выбора.
            // Повторный выбор того же файла после очистки имени тоже перезаполняет его,
            // поэтому при неизменившемся пути проверяем только флаг ручного ввода.
            if (!SetProperty(ref _cfePath, value ?? string.Empty) && _extensionNameEdited)
                return;

            // Issue #117: запоминаем настоящее имя из .cfe — оно нужно и для автоподстановки,
            // и для решения «спросить пользователя, чтобы не создать дубль». Заодно получаем
            // ВСЕХ кандидатов имени: если их несколько, они выводятся в журнал с путями
            // в контейнере (видно, почему выбрано это имя и что отвергнуто).
            _cfeRealName = ConfigExtensionService.TryReadExtensionName(_cfePath, out var candidates);
            _cfeNameCandidates = candidates;
            LogNameCandidates();
            if (!_extensionNameEdited)
            {
                // Приоритет — НАСТОЯЩЕЕ имя расширения из .cfe, чтобы в поле было имя,
                // под которым расширение установлено в базе, а не имя файла.
                SetAutoName(_cfeRealName ?? ConfigExtensionService.SuggestName(_cfePath));
            }
        }
    }

    /// <summary>
    /// Issue #117: настоящее имя расширения, прочитанное из выбранного .cfe
    /// (<c>null</c> — прочитать не удалось, используется имя файла).
    /// </summary>
    public string? CfeExtensionName => _cfeRealName;

    /// <summary>Issue #117: имя расширения удалось прочитать из .cfe.</summary>
    public bool HasCfeExtensionName => !string.IsNullOrWhiteSpace(_cfeRealName);

    /// <summary>
    /// Issue #117 (требование 7OH): нужно ли подтверждение перед установкой, чтобы
    /// не создать дубль. Истина, если имя из .cfe не прочитано, а в базе уже есть
    /// расширения с другими именами. Используется окном для предупреждения ДО установки.
    /// </summary>
    public bool RequiresNameConfirmation(IReadOnlyCollection<string> installedNames) =>
        ConfigExtensionService.RequiresUnknownNameConfirmation(_cfeRealName, ExtensionName, installedNames);

    public string ExtensionName
    {
        get => _extensionName;
        set
        {
            var v = value ?? string.Empty;
            if (!SetProperty(ref _extensionName, v))
                return;
            // Ручным вводом считаем любое непустое имя, отличное от автоподстановки
            // для текущего файла. Очистка поля пользователем снимает признак ручного
            // редактирования.
            _extensionNameEdited = v.Length > 0 && !IsAutoNameValue(v);
        }
    }

    /// <summary>
    /// Issue #117 (7OH): значение совпадает с автоподстановкой для текущего файла —
    /// имя файла или НАСТОЯЩЕЕ имя расширения из .cfe. Такое значение не считается
    /// ручным вводом: после автоподстановки UI может вернуть его же в сеттер (двусторонняя
    /// привязка), и раньше это ложно помечало имя как «изменённое вручную» — тогда при
    /// следующем выборе файла настоящее имя уже не подставлялось.
    /// </summary>
    private bool IsAutoNameValue(string value) =>
        string.Equals(value, ConfigExtensionService.SuggestName(_cfePath), StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(_cfeRealName) &&
         string.Equals(value, _cfeRealName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Issue #117 (диагностика): выводит в журнал окна всех кандидатов имени расширения
    /// с путями в дереве контейнеров — если кандидатов несколько.
    /// </summary>
    private void LogNameCandidates()
    {
        if (_cfeNameCandidates.Count <= 1)
            return;
        AppendLog(string.Format(LocalizationManager.T("Extensions.LogNameCandidates"),
            string.Join("; ", _cfeNameCandidates.Select(c => c.Describe()))));
    }

    /// <summary>
    /// Issue #117: программная автоподстановка имени (настоящее имя из .cfe или имя
    /// файла) — не считается ручным вводом и снимает признак редактирования.
    /// </summary>
    private void SetAutoName(string name)
    {
        _extensionNameEdited = false;
        SetProperty(ref _extensionName, name, nameof(ExtensionName));
    }

    public string? SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            if (SetProperty(ref _selectedExtension, value) && !string.IsNullOrWhiteSpace(value))
                ExtensionName = value!;
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    /// <summary>Устанавливать расширение утилитой ibcmd вместо «Конфигуратора» (1.0.0.84).</summary>
    public bool UseIbcmd
    {
        get => _useIbcmd;
        set => SetProperty(ref _useIbcmd, value);
    }

    /// <summary>
    /// Issue #117 (требование 7OH): подтверждение установки, когда настоящее имя
    /// расширения не прочитано из .cfe, а в базе уже есть расширения с другими именами.
    /// Аргумент — установленные имена; <c>true</c> — устанавливать под именем файла.
    /// Обработчик задаётся окном (WPF/Avalonia). Если его нет (headless/тесты),
    /// установка не выполняется — дубль не создаётся молча.
    /// </summary>
    public Func<IReadOnlyCollection<string>, Task<bool>>? ConfirmUnknownNameAsync { get; set; }

    public string LogText => _log.ToString();

    public string CheckedSummary => string.Format(LocalizationManager.T("Extensions.CheckedSummary"),
        _allTargets.Count(r => r.IsChecked), _allTargets.Count);

    public IReadOnlyList<ExtensionTargetRow> CheckedTargets => _allTargets.Where(r => r.IsChecked).ToList();

    public void SetAllChecked(bool value)
    {
        foreach (var row in Targets)
            row.IsChecked = value;
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void AppendLog(string message)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    private void ApplyFilter()
    {
        Targets.Clear();
        var needle = _searchText.Trim();
        foreach (var row in _allTargets)
        {
            if (needle.Length == 0 || row.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0)
                Targets.Add(row);
        }
    }

    private CancellationToken BeginOperation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        return _cts.Token;
    }

    /// <summary>Читает список расширений выбранной базы (кнопка «Обновить»).</summary>
    public Task RefreshExtensionsAsync()
    {
        if (IsBusy || _selectedTarget is null)
            return Task.CompletedTask;
        return ListCoreAsync(quiet: false);
    }

    /// <summary>
    /// Issue #117 п.3: тихое получение списка расширений выбранной базы заранее
    /// (при открытии окна и при смене базы). Ошибки не прерывают работу окна —
    /// пользователь может нажать «Обновить» и увидеть полный журнал.
    /// </summary>
    public async Task PrefetchExtensionsAsync()
    {
        if (IsBusy || _selectedTarget is null)
            return;
        try
        {
            await ListCoreAsync(quiet: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogPrefetchFailed"), ex.Message));
        }
    }

    private async Task ListCoreAsync(bool quiet)
    {
        var row = _selectedTarget!;
        var token = BeginOperation();
        try
        {
            if (!quiet)
                AppendLog(string.Format(LocalizationManager.T("Extensions.LogListing"), row.Name));
            var result = await ConfigExtensionService.ListAsync(row.Infobase, token);
            Extensions.Clear();
            if (!result.Success)
            {
                AppendLog(result.Error ?? LocalizationManager.T("Extensions.Failed"));
                return;
            }
            foreach (var name in ConfigExtensionService.ParseList(result.Log))
                Extensions.Add(name);
            if (!quiet)
                AppendLog(Extensions.Count == 0
                    ? LocalizationManager.T("Extensions.LogNoExtensions")
                    : string.Format(LocalizationManager.T("Extensions.LogFound"), Extensions.Count, string.Join(", ", Extensions)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Устанавливает (обновляет) расширение из файла во все отмеченные базы по очереди.
    /// Возвращает число успешных установок.
    /// </summary>
    public async Task<int> InstallToCheckedAsync()
    {
        if (IsBusy)
            return 0;
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            AppendLog(LocalizationManager.T("Extensions.NoChecked"));
            return 0;
        }
        if (string.IsNullOrWhiteSpace(ExtensionName))
        {
            AppendLog(LocalizationManager.T("Extensions.BadName"));
            return 0;
        }

        var token = BeginOperation();
        var ok = 0;
        try
        {
            foreach (var row in _allTargets)
                if (row.IsChecked) row.Status = LocalizationManager.T("Extensions.StatusQueued");

            // Issue #117: настоящее имя расширения из .cfe имеет приоритет над именем
            // файла — если пользователь не вводил имя вручную, установка выполняется под
            // именем из файла, чтобы обновить существующее расширение, а не создать дубль.
            ApplyCfeName();

            // Issue #117 п.4: перед установкой (только ветка конфигуратора — у ibcmd список
            // расширений дёшево не получить) тихо читаем установленные расширения каждой
            // отмеченной базы и показываем план: «установка» или «уже установлено — обновление».
            var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!UseIbcmd)
                installedNames = await MarkAlreadyInstalledAsync(targets, token);

            // Issue #117 (требование 7OH): если настоящее имя из .cfe не прочитано, а в
            // базе уже есть расширения с другими именами — предупреждаем и уточняем ДО
            // установки. Без подтверждения установка не выполняется, чтобы не создать
            // дубль под именем файла (платформа затем ругается «такое уже есть»).
            // Ветка ibcmd список расширений дёшево не получает — при неизвестном имени
            // спрашиваем всегда (безопаснее, чем молча создать дубль).
            var needsNameConfirmation = UseIbcmd
                ? string.IsNullOrWhiteSpace(_cfeRealName) && !string.IsNullOrWhiteSpace(CfePath)
                : ConfigExtensionService.RequiresUnknownNameConfirmation(_cfeRealName, ExtensionName, installedNames);
            if (needsNameConfirmation)
            {
                var confirmed = await ConfirmUnknownExtensionNameAsync(installedNames, token);
                if (!confirmed)
                {
                    foreach (var row in targets)
                        row.Status = LocalizationManager.T("Extensions.StatusCancelled");
                    AppendLog(string.Format(LocalizationManager.T("Extensions.LogUnknownNameCancelled"),
                        ExtensionName));
                    return 0;
                }
            }

            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusCancelled");
                    continue;
                }
                row.Status = LocalizationManager.T("Extensions.StatusRunning");
                AppendLog(string.Format(LocalizationManager.T("Extensions.LogInstalling"), ExtensionName, row.Name));
                var result = UseIbcmd
                    ? await InstallViaIbcmdAsync(row.Infobase, token)
                    : await ConfigExtensionService.InstallAsync(row.Infobase, CfePath, ExtensionName, token);

                // Issue #87 («Загрузка расширения конфигурации в клиент-серверную базу»):
                // если конфигуратор не принял параметры («Error in command-line options»,
                // код 1) или не смог аутентифицировать пользователя («The infobase user is
                // not authenticated»), а ibcmd-ветка не выбрана — автоматически повторяем
                // установку утилитой ibcmd (config load --extension + config apply --force).
                if (!UseIbcmd && !result.Success && ConfigExtensionService.ShouldFallbackToIbcmd(result))
                {
                    AppendLog(LocalizationManager.T("Extensions.LogDesignerFailedTryIbcmd"));
                    result = await InstallViaIbcmdAsync(row.Infobase, token);
                }
                if (result.Success)
                {
                    // Issue #117: конфигуратор возвращает код 0 даже при отказе применять
                    // расширение («Загрузка конфигурации успешно завершена» при неуспешном
                    // /UpdateDBCfg) — сообщаем фактический результат, а не «успешно».
                    var refusal = ConfigExtensionService.FindRefusalMessage(result.Log);
                    if (refusal is not null)
                    {
                        row.Status = LocalizationManager.T("Extensions.StatusError");
                        AppendLog(string.Format(LocalizationManager.T("Extensions.LogInstallRefused"), row.Name, refusal));
                    }
                    else
                    {
                        ok++;
                        row.Status = LocalizationManager.T("Extensions.StatusDone");
                        AppendLog(string.Format(LocalizationManager.T("Extensions.LogInstalled"), row.Name));
                    }
                }
                else
                {
                    row.Status = LocalizationManager.T("Extensions.StatusError");
                    AppendLog($"{row.Name}: {result.Error}");
                }
            }
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogSummary"), ok, targets.Count));
        }
        finally
        {
            IsBusy = false;
        }
        return ok;
    }

    private async Task<DesignerCommandResult> InstallViaIbcmdAsync(Infobase infobase, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(CfePath) || !System.IO.File.Exists(CfePath))
            return DesignerCommandResult.Fail(LocalizationManager.T("Extensions.FileNotFound"));
        var r = await IbcmdService.InstallExtensionAsync(infobase, CfePath, ExtensionName, token);
        return new DesignerCommandResult { Success = r.Success, ExitCode = r.ExitCode, Log = r.Output, Error = r.Error };
    }

    /// <summary>
    /// Issue #117: имя из .cfe имеет приоритет над именем файла. Если настоящее имя
    /// файла удалось прочитать и пользователь не вводил имя вручную — установка
    /// выполняется под именем из файла (обновление существующего расширения вместо
    /// дубля). При ручном вводе остаётся введённое имя, но выводится предупреждение
    /// о создании дубля. Если имя не прочитано — в журнале появляется предупреждение,
    /// а перед установкой запрашивается подтверждение (см. <see cref="ConfirmUnknownNameAsync"/>).
    /// Решение о имени — чистая функция <see cref="ConfigExtensionService.ResolveInstallName"/>.
    /// </summary>
    internal void ApplyCfeName()
    {
        if (string.IsNullOrWhiteSpace(_cfeRealName) && !string.IsNullOrWhiteSpace(CfePath))
        {
            // Имя ещё не читали (файл выбран до появления разбора) — читаем вместе
            // с кандидатами, чтобы в журнале была диагностика выбора.
            _cfeRealName = ConfigExtensionService.TryReadExtensionName(CfePath, out var candidates);
            if (candidates.Count > 0)
            {
                _cfeNameCandidates = candidates;
                LogNameCandidates();
            }
        }
        var cfeName = _cfeRealName;
        if (string.IsNullOrWhiteSpace(cfeName))
        {
            // Issue #117 (7OH): имя не прочитано — установка под именем файла может
            // создать дубль; предупреждаем в журнале (подтверждение — в InstallToCheckedAsync).
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogNameUnknown"),
                ConfigExtensionService.SuggestName(CfePath)));
            return;
        }
        _cfeRealName = cfeName;
        if (ConfigExtensionService.NameEquals(cfeName, ExtensionName))
            return;
        var installName = ConfigExtensionService.ResolveInstallName(cfeName, ExtensionName, _extensionNameEdited);
        if (!_extensionNameEdited)
        {
            SetAutoName(installName);
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogNameFromCfe"), installName));
        }
        else
        {
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogNameConflict"), cfeName, ExtensionName));
        }
    }

    /// <summary>
    /// Issue #117 (7OH): предупреждение и уточнение перед установкой, когда настоящее
    /// имя расширения не прочитано из .cfe. Обработчик задаётся окном (WPF/Avalonia);
    /// если его нет (headless/тесты) — установка отменяется, дубль не создаётся молча.
    /// </summary>
    internal async Task<bool> ConfirmUnknownExtensionNameAsync(IReadOnlyCollection<string> installedNames,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return false;
        var names = installedNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        AppendLog(names.Count == 0
            ? string.Format(LocalizationManager.T("Extensions.LogNameUnknown"),
                ConfigExtensionService.SuggestName(CfePath))
            : string.Format(LocalizationManager.T("Extensions.LogNameUnknownInstalled"),
                ConfigExtensionService.SuggestName(CfePath), string.Join(", ", names)));
        if (ConfirmUnknownNameAsync is null)
            return false;
        try
        {
            return await ConfirmUnknownNameAsync(installedNames);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Issue #117 п.4: для каждой отмеченной базы тихо получает список установленных
    /// расширений и помечает строки/журнал для имён, которые уже установлены
    /// (сравнение OrdinalIgnoreCase с обрезкой пробелов). Возвращает объединённый
    /// список установленных имён — по нему окно решает, нужно ли подтверждение.
    /// Сбой чтения не блокирует установку — в журнале появляется предупреждение.
    /// </summary>
    private async Task<HashSet<string>> MarkAlreadyInstalledAsync(IReadOnlyList<ExtensionTargetRow> targets,
        CancellationToken token)
    {
        var allInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in targets)
        {
            if (token.IsCancellationRequested)
                break;
            IReadOnlyList<string> installed;
            try
            {
                var listResult = await ConfigExtensionService.ListAsync(row.Infobase, token);
                installed = listResult.Success
                    ? ConfigExtensionService.ParseList(listResult.Log)
                    : new List<string>();
                if (!listResult.Success)
                    AppendLog(string.Format(LocalizationManager.T("Extensions.LogPreflightFailed"),
                        row.Name, listResult.Error ?? LocalizationManager.T("Extensions.Failed")));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppendLog(string.Format(LocalizationManager.T("Extensions.LogPreflightFailed"), row.Name, ex.Message));
                continue;
            }
            foreach (var name in installed)
                if (!string.IsNullOrWhiteSpace(name))
                    allInstalled.Add(name.Trim());
            if (ConfigExtensionService.FindInstalledMatch(ExtensionName, installed) is not null)
            {
                row.Status = LocalizationManager.T("Extensions.StatusAlreadyInstalled");
                AppendLog(string.Format(LocalizationManager.T("Extensions.LogAlreadyInstalled"), ExtensionName, row.Name));
            }
        }
        return allInstalled;
    }

    /// <summary>Выгружает выбранное расширение выбранной базы в файл.</summary>
    public async Task<bool> DumpSelectedAsync(string path)
    {
        if (IsBusy || _selectedTarget is null || string.IsNullOrWhiteSpace(ExtensionName))
            return false;
        var row = _selectedTarget;
        var token = BeginOperation();
        try
        {
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogDumping"), ExtensionName, row.Name, path));
            var result = await ConfigExtensionService.DumpAsync(row.Infobase, path, ExtensionName, token);
            AppendLog(result.Success
                ? string.Format(LocalizationManager.T("Extensions.LogDumped"), path)
                : $"{row.Name}: {result.Error}");
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Удаляет выбранное расширение из выбранной базы.</summary>
    public async Task<bool> DeleteSelectedAsync()
    {
        if (IsBusy || _selectedTarget is null || string.IsNullOrWhiteSpace(ExtensionName))
            return false;
        var row = _selectedTarget;
        var name = ExtensionName;
        var token = BeginOperation();
        try
        {
            AppendLog(string.Format(LocalizationManager.T("Extensions.LogDeleting"), name, row.Name));
            var result = await ConfigExtensionService.DeleteAsync(row.Infobase, name, token);
            AppendLog(result.Success
                ? string.Format(LocalizationManager.T("Extensions.LogDeleted"), name)
                : $"{row.Name}: {result.Error}");
            if (result.Success)
                Extensions.Remove(name);
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Строка списка баз окна «Расширения»: флажок, имя и статус операции.</summary>
public sealed class ExtensionTargetRow : ViewModelBase
{
    private bool _isChecked;
    private string _status = string.Empty;

    public ExtensionTargetRow(Infobase infobase)
    {
        Infobase = infobase;
    }

    public Infobase Infobase { get; }

    public string Name => Infobase.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? string.Empty);
    }
}
