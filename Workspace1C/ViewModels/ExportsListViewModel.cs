using System.Collections.ObjectModel;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Модель окна «Список выгрузок»: сканирование каталогов резервирования на файлы
/// .dt/.cf/.zip/.rar и коллекция найденных строк. Чистый .NET, обе платформы.
/// </summary>
public class ExportsListViewModel : ViewModelBase
{
    private static readonly string[] SupportedExtensions = { ".dt", ".cf", ".zip", ".rar" };

    private readonly IBackupScenarioStore _store;
    private readonly AppSettings _settings;
    private readonly IAppLogger? _logger;

    public ExportsListViewModel(IBackupScenarioStore store, IInfobaseRepository repository, IAppLogger? logger = null)
    {
        _store = store;
        _logger = logger;
        try
        {
            _settings = repository.LoadSettings();
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public ObservableCollection<BackupExportItem> Items { get; } = new();

    public BackupExportItem? SelectedItem { get; set; }

    /// <summary>
    /// Заново сканирует каталоги назначения сценариев и настраиваемые каталоги
    /// (<see cref="AppSettings.BackupTargetDirectories"/>), сортирует по дате убывания.
    /// </summary>
    public void Refresh()
    {
        var items = ScanItems();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
    }

    /// <summary>Сканирует каталоги (без обращения к <see cref="Items"/>) — синхронно.</summary>
    private List<BackupExportItem> ScanItems()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in _settings.BackupTargetDirectories ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(dir))
                dirs.Add(dir.Trim());
        }

        foreach (var scenario in _store.LoadAll())
        {
            foreach (var dir in scenario.TargetDirectories ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(dir))
                    dirs.Add(dir.Trim());
            }
        }

        var items = new List<BackupExportItem>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!SupportedExtensions.Contains(ext))
                        continue;
                    var fi = new FileInfo(file);
                    items.Add(new BackupExportItem
                    {
                        FilePath = file,
                        Format = MapFormat(ext),
                        SizeBytes = fi.Length,
                        LastWriteTime = fi.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Не удалось просканировать каталог выгрузок «{dir}»: {ex.Message}");
            }
        }

        items.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return items;
    }

    /// <summary>
    /// Асинхронное обновление (аудит производительности): сканирование каталогов
    /// (в т.ч. сетевых) выполняется в фоновой задаче, а применение результата к
    /// <see cref="Items"/> — в исходном (UI) контексте. Продолжение после await
    /// возвращается в контекст вызывающего (SyncContext не подавляется намеренно).
    /// </summary>
    public async Task RefreshAsync()
    {
        var items = await Task.Run(ScanItems).ConfigureAwait(false);
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
    }

    private static BackupFormat MapFormat(string ext) => ext switch
    {
        ".cf" => BackupFormat.Cf,
        ".zip" => BackupFormat.Zip,
        ".rar" => BackupFormat.Rar,
        _ => BackupFormat.Dt
    };
}