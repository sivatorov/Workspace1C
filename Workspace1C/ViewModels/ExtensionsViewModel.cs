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
            if (!_extensionNameEdited)
                ExtensionName = ConfigExtensionService.SuggestName(_cfePath);
        }
    }

    public string ExtensionName
    {
        get => _extensionName;
        set
        {
            var v = value ?? string.Empty;
            if (!SetProperty(ref _extensionName, v))
                return;
            // Ручным вводом считаем любое непустое имя, отличное от автоподстановки
            // для текущего файла (и выбор расширения из списка). Очистка поля
            // пользователем снимает признак ручного редактирования.
            _extensionNameEdited = v.Length > 0 &&
                !string.Equals(v, ConfigExtensionService.SuggestName(_cfePath), StringComparison.OrdinalIgnoreCase);
        }
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

            // Issue #117 п.4: перед установкой (только ветка конфигуратора — у ibcmd список
            // расширений дёшево не получить) тихо читаем установленные расширения каждой
            // отмеченной базы и показываем план: «установка» или «уже установлено — обновление».
            if (!UseIbcmd)
                await MarkAlreadyInstalledAsync(targets, token);

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
                    ok++;
                    row.Status = LocalizationManager.T("Extensions.StatusDone");
                    AppendLog(string.Format(LocalizationManager.T("Extensions.LogInstalled"), row.Name));
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
    /// Issue #117 п.4: для каждой отмеченной базы тихо получает список установленных
    /// расширений и помечает строки/журнал для имён, которые уже установлены
    /// (сравнение OrdinalIgnoreCase). Сбой чтения не блокирует установку —
    /// в журнале появляется предупреждение.
    /// </summary>
    private async Task MarkAlreadyInstalledAsync(IReadOnlyList<ExtensionTargetRow> targets, CancellationToken token)
    {
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
            if (installed.Contains(ExtensionName, StringComparer.OrdinalIgnoreCase))
            {
                row.Status = LocalizationManager.T("Extensions.StatusAlreadyInstalled");
                AppendLog(string.Format(LocalizationManager.T("Extensions.LogAlreadyInstalled"), ExtensionName, row.Name));
            }
        }
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
