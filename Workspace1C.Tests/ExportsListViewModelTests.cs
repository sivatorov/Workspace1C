using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты вью-модели окна «Список выгрузок» (issue #104): сканирование каталогов
/// сценариев и настроек, применение результата синхронного <see cref="ExportsListViewModel.Refresh"/>
/// и регрессия потокобезопасного применения коллекции в асинхронном
/// <see cref="ExportsListViewModel.RefreshAsync"/> — продолжение после await обязано
/// выполняться в контексте вызывающего (UI-потоке), иначе WPF-привязка бросает
/// NotSupportedException «CollectionView не поддерживает изменения из потока,
/// отличного от потока Dispatcher», и окно падало при открытии.
/// </summary>
public sealed class ExportsListViewModelTests
{
    [Fact]
    public void Refresh_FillsItemsFromTargetDirectories_SortedByDateDescending()
    {
        using var temp = new TempDirectory();
        var dir = temp.Path;

        var oldFile = Path.Combine(dir, "base_20260101.dt");
        File.WriteAllText(oldFile, "dt");
        // Дата старого файла — вчера, нового — сейчас (новый должен идти первым).
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-1));
        var newFile = Path.Combine(dir, "base_20260102.dt");
        File.WriteAllText(newFile, "dt");

        // Файл не поддерживаемого расширения не попадает в список.
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "txt");

        var vm = CreateViewModel(dir);

        vm.Refresh();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(newFile, vm.Items[0].FilePath);
        Assert.Equal(oldFile, vm.Items[1].FilePath);
        Assert.Equal(BackupFormat.Dt, vm.Items[0].Format);
    }

    [Fact]
    public async Task RefreshAsync_PostsContinuationToCallerSyncContext()
    {
        // Регрессия issue #104: ConfigureAwait(false) после Task.Run(ScanItems)
        // переводил изменение Items на поток thread pool, и WPF бросал
        // NotSupportedException при первом обновлении окна после отрисовки.
        // Контекст вызывающего ставит посты в очередь; если продолжение подавлено,
        // ни один Post не вызывается, и коллекция модифицируется вне контекста.
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "base.dt"), "dt");

        var vm = CreateViewModel(temp.Path);
        var context = new QueueSyncContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = vm.RefreshAsync();

            // Прокручиваем очередь продолжений, пока задача не завершится:
            // Task.Run может закончиться и позже вызова RunPending, поэтому
            // прокрутка повторяется до завершения RefreshAsync. Таймаут
            // защищает тест от зависания, если продолжение «уплыло» на thread
            // pool и контекст вообще не использовался.
            var completed = SpinWait.SpinUntil(
                () =>
                {
                    context.RunPending();
                    return task.IsCompleted;
                },
                TimeSpan.FromSeconds(10));

            Assert.True(completed, "RefreshAsync не завершился через контекст вызывающего");

            // Продолжение должно пройти через SynchronizationContext вызывающего.
            Assert.True(
                context.PostCount > 0,
                "Продолжение после await не постится в SynchronizationContext вызывающего — " +
                "ConfigureAwait(false) подавил контекст (регрессия issue #104)");

            Assert.Single(vm.Items);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static ExportsListViewModel CreateViewModel(string targetDirectory) => new(
        new FakeScenarioStore(new List<BackupScenario>
        {
            new() { TargetDirectories = { targetDirectory } }
        }),
        new FakeRepository(new AppSettings()),
        logger: null);

    /// <summary>Синхронизационный контекст, ставящий продолжения в очередь.</summary>
    private sealed class QueueSyncContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            _queue.Enqueue((d, state));
        }

        public void RunPending()
        {
            while (_queue.Count > 0)
            {
                var (callback, state) = _queue.Dequeue();
                callback(state);
            }
        }
    }

    /// <summary>Фейк хранилища сценариев резервирования.</summary>
    private sealed class FakeScenarioStore : IBackupScenarioStore
    {
        private readonly IReadOnlyList<BackupScenario> _scenarios;

        public FakeScenarioStore(IReadOnlyList<BackupScenario> scenarios) => _scenarios = scenarios;

        public string ScenariosDirectory => string.Empty;
        public IReadOnlyList<BackupScenario> LoadAll() => _scenarios;
        public BackupScenario? Get(string id) => null;
        public void Save(BackupScenario scenario) { }
        public void Delete(string id) { }
    }

    /// <summary>Фейк репозитория баз: настройки без настраиваемых каталогов.</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        private readonly AppSettings _settings;
        public FakeRepository(AppSettings settings) => _settings = settings;

        public List<Infobase> Load() => new();
        public void Save(List<Infobase> infobases) { }
        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public List<Group> LoadGroups() => new();
        public void SaveGroups(List<Group> groups) { }
        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AppSettings LoadSettings() => _settings;
        public void SaveSettings(AppSettings settings) { }
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Временный каталог, удаляемый при освобождении.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "w1c_exports_list_" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* тестовые файлы */ }
        }
    }
}
