using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Внешние отчёты и обработки» (issue #91, п.3): список файлов
/// .epf/.erf, отмеченные базы, режим «обновлять существующие», журнал.
/// Строки баз — <see cref="ExtensionTargetRow"/> (как в окне «Расширения»). Обе платформы.
/// </summary>
public sealed class ExternalProcessingsViewModel : ViewModelBase
{
    private readonly List<ExtensionTargetRow> _allTargets;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private string _searchText = string.Empty;
    private string? _selectedFile;
    private bool _updateExisting = true;
    private bool _isBusy;

    public ExternalProcessingsViewModel(IEnumerable<Infobase> infobases, IEnumerable<Infobase>? preselected)
    {
        _allTargets = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ib => new ExtensionTargetRow(ib))
            .ToList();
        var ids = new HashSet<string>((preselected ?? Array.Empty<Infobase>()).Where(i => i is not null).Select(i => i.Id));
        foreach (var row in _allTargets)
        {
            row.IsChecked = ids.Contains(row.Infobase.Id);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExtensionTargetRow.IsChecked))
                    OnPropertyChanged(nameof(CheckedSummary));
            };
        }
        Files.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilesSummary));
        ApplyFilter();
    }

    public ObservableCollection<ExtensionTargetRow> Targets { get; } = new();

    /// <summary>Файлы .epf/.erf для загрузки (полные пути).</summary>
    public ObservableCollection<string> Files { get; } = new();

    public string? SelectedFile { get => _selectedFile; set => SetProperty(ref _selectedFile, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>Обновлять уже загруженные обработки (иначе — только добавлять новые).</summary>
    public bool UpdateExisting { get => _updateExisting; set => SetProperty(ref _updateExisting, value); }

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

    public string LogText => _log.ToString();

    public string CheckedSummary => string.Format(LocalizationManager.T("Extensions.CheckedSummary"),
        _allTargets.Count(r => r.IsChecked), _allTargets.Count);

    public string FilesSummary => string.Format(LocalizationManager.T("ExtProc.FilesSummary"), Files.Count);

    public IReadOnlyList<ExtensionTargetRow> CheckedTargets => _allTargets.Where(r => r.IsChecked).ToList();

    /// <summary>Добавляет файлы (неподдерживаемые и повторы пропускаются). Возвращает число добавленных.</summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (!ExternalProcessingsService.IsSupported(path))
                continue;
            var full = Path.GetFullPath(path);
            if (Files.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase)))
                continue;
            Files.Add(full);
            added++;
        }
        return added;
    }

    /// <summary>Добавляет все .epf/.erf из каталога. Возвращает число добавленных.</summary>
    public int AddFolder(string folder)
    {
        var added = AddFiles(ExternalProcessingsService.FindInFolder(folder));
        AppendLog(string.Format(LocalizationManager.T("ExtProc.LogFolderAdded"), added, folder));
        return added;
    }

    public void RemoveSelectedFile()
    {
        if (_selectedFile is not null)
            Files.Remove(_selectedFile);
    }

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

    /// <summary>
    /// Загружает файлы во все отмеченные базы по очереди. Возвращает число баз,
    /// в которые все файлы загружены без ошибок.
    /// </summary>
    public async Task<int> LoadToCheckedAsync(
        Func<Infobase, IReadOnlyList<string>, bool, CancellationToken, Task<ExternalProcessingsBaseResult>>? loader = null)
    {
        if (IsBusy)
            return 0;
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            AppendLog(LocalizationManager.T("Extensions.NoChecked"));
            return 0;
        }
        var files = Files.ToList();
        var invalid = ExternalProcessingsService.Validate(files);
        if (invalid is not null)
        {
            AppendLog(invalid);
            return 0;
        }

        loader ??= ExternalProcessingsService.LoadAsync;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        var okBases = 0;
        try
        {
            foreach (var row in targets)
                row.Status = LocalizationManager.T("Extensions.StatusQueued");

            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusCancelled");
                    continue;
                }
                row.Status = LocalizationManager.T("Extensions.StatusRunning");
                AppendLog(string.Format(LocalizationManager.T("ExtProc.LogLoading"), files.Count, row.Name));

                ExternalProcessingsBaseResult result;
                try
                {
                    result = await loader(row.Infobase, files, UpdateExisting, token);
                }
                catch (Exception ex)
                {
                    result = ExternalProcessingsBaseResult.Fail(ex.Message);
                }

                if (!result.Success)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusError");
                    AppendLog($"{row.Name}: {result.Error}");
                    continue;
                }

                var errors = 0;
                foreach (var file in result.Files)
                {
                    var name = Path.GetFileName(file.File);
                    if (!file.Success)
                    {
                        errors++;
                        AppendLog($"{row.Name}: {name} — {file.Error}");
                        continue;
                    }
                    var key = file.Action switch
                    {
                        "created" => "ExtProc.LogCreated",
                        "updated" => "ExtProc.LogUpdated",
                        _ => "ExtProc.LogSkipped"
                    };
                    AppendLog(string.Format(LocalizationManager.T(key), row.Name, name, file.Title, file.Version));
                    foreach (var warning in file.Warnings)
                        AppendLog("    ⚠ " + ExternalProcessingsService.DescribeWarning(warning));
                }

                if (errors == 0)
                {
                    okBases++;
                    row.Status = LocalizationManager.T("Extensions.StatusDone");
                }
                else
                {
                    row.Status = string.Format(LocalizationManager.T("ExtProc.StatusErrorsFormat"), errors);
                }
            }
            AppendLog(string.Format(LocalizationManager.T("ExtProc.LogSummary"), okBases, targets.Count));
        }
        finally
        {
            IsBusy = false;
        }
        return okBases;
    }
}
