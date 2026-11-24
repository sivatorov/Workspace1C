using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (комментарий RafaIS 2026-11-24): окно «Резервные копии СУБД»
/// «наглухо» зависает на любой кнопке, а таймауты 15/30 с не наступают.
/// <para>
/// Проверяем исправление 1.0.1.115: отсутствие sync-over-async в UI-пути,
/// сторожевой таймаут, не зависящий от пула потоков, сброс <c>IsBusy</c> при
/// занятом пуле, отсечение повторного клика, этапную трассировку и сторож
/// UI-потока. Тесты — в коллекции с отключённой параллельностью: подменяют
/// статические <see cref="DbmsBackupService.SaveOverride"/> и настройки
/// <see cref="DbmsBackupTrace"/>.
/// </para>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143UiHangDiagnosticsTests
{
    // ===================== Source-guard: нет блокирующих ожиданий =====================

    [Theory]
    [InlineData("ViewModels", "DbmsBackupViewModel.cs")]
    [InlineData("Views", "DbmsBackupWindow.cs")]
    [InlineData("Views", "DbmsBackupWindow.Avalonia.cs")]
    public void UiPath_HasNoSyncOverAsync(string folder, string file)
    {
        var source = ReadProjectFile("Workspace1C", folder, file);

        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveCore", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_UsesGuardedTimeoutAndTrace()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");

        Assert.Contains("DbmsTimeoutGuard.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupTrace.Log", source, StringComparison.Ordinal);
        Assert.Contains("TryEnterOperation", source, StringComparison.Ordinal);
        Assert.Contains("Dbms.TimeoutDetails", source, StringComparison.Ordinal);
        Assert.Contains("Dbms.SendDebugLog", source, StringComparison.Ordinal);
    }

    // ===================== Таймаут не зависит от пула потоков =====================

    /// <summary>
    /// При заблокированном пуле (<see cref="ThreadPoolStarvation"/>) сторожевой
    /// таймаут обязан вернуть управление: он принимает решение на выделенном потоке,
    /// а не на таймере пула (<c>CancelAfter</c>/<c>WaitAsync</c>).
    /// </summary>
    [Fact]
    public void Timeout_DoesNotDependOnThreadPool()
    {
        using var starvation = new ThreadPoolStarvation(Environment.ProcessorCount + 4);

        var outcome = RunWithDedicatedContext(() =>
        {
            var never = new TaskCompletionSource<bool>();
            var sw = Stopwatch.StartNew();
            var error = Record.Exception(() =>
                DbmsTimeoutGuard.RunAsync(_ => never.Task, TimeSpan.FromMilliseconds(250), stage: "test")
                    .GetAwaiter().GetResult());
            sw.Stop();
            return (error, sw.Elapsed);
        });

        Assert.IsType<TimeoutException>(outcome.error);
        Assert.True(outcome.Elapsed < TimeSpan.FromSeconds(5),
            $"Таймаут не сработал за {outcome.Elapsed} при занятом пуле.");
    }

    /// <summary>
    /// Сохранение профиля при занятом пуле: окно получает управление, <c>IsBusy</c>
    /// сбрасывается, статус — сообщение о таймауте (кнопки снова активны).
    /// </summary>
    [Fact]
    public void SaveAsync_ReturnsWithinTimeout_AndResetsBusy_WhenThreadPoolStarved()
    {
        var previous = DbmsBackupService.SaveOverride;
        DbmsBackupService.SaveOverride = (_, _) => new TaskCompletionSource<bool>().Task;
        try
        {
            using var starvation = new ThreadPoolStarvation(Environment.ProcessorCount + 4);

            var outcome = RunWithDedicatedContext(() =>
            {
                var vm = CreateViewModel();
                var sw = Stopwatch.StartNew();
                var saved = vm.SaveAsync(TimeSpan.FromMilliseconds(300)).GetAwaiter().GetResult();
                sw.Stop();
                return (saved, sw.Elapsed, vm.IsBusy, vm.StatusText);
            });

            Assert.False(outcome.saved);
            Assert.True(outcome.Elapsed < TimeSpan.FromSeconds(5),
                $"SaveAsync не вернулся за {outcome.Elapsed} при занятом пуле.");
            Assert.False(outcome.IsBusy);
            Assert.Equal(LocalizationManager.T("Dbms.SaveTimeout"), outcome.StatusText);
        }
        finally
        {
            DbmsBackupService.SaveOverride = previous;
        }
    }

    // ===================== Повторный вход =====================

    /// <summary>
    /// Второй клик «Сохранить» игнорируется: <c>IsBusy</c> выставляется до
    /// <c>CurrentProfile/Validate</c>, запись выполняется один раз.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SecondClickIgnored_WhileBusy()
    {
        var previous = DbmsBackupService.SaveOverride;
        var calls = 0;
        DbmsBackupService.SaveOverride = (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            return Task.Delay(TimeSpan.FromSeconds(10), ct);
        };

        try
        {
            var vm = CreateViewModel();

            var first = vm.SaveAsync(TimeSpan.FromMilliseconds(300));
            Assert.True(vm.IsBusy, "IsBusy должен быть выставлен до Validate/записи.");

            var second = await vm.SaveAsync(TimeSpan.FromMilliseconds(300));

            Assert.False(second);
            Assert.Equal(1, calls);
            Assert.False(await first);
            Assert.False(vm.IsBusy);
            Assert.True(vm.IsIdle);
        }
        finally
        {
            DbmsBackupService.SaveOverride = previous;
        }
    }

    // ===================== Трассировка этапов =====================

    [Fact]
    public void Trace_RecordsAllStages()
    {
        var directory = NewTempDirectory();
        var previousEnabled = DbmsBackupTrace.EnabledOverride;
        var previousDirectory = DbmsBackupTrace.DirectoryOverride;
        try
        {
            DbmsBackupTrace.EnabledOverride = true;
            DbmsBackupTrace.DirectoryOverride = directory;

            DbmsBackupTrace.Log("click", "save");
            DbmsBackupTrace.Log("enter", "save");
            DbmsBackupTrace.Log("io-begin", @"C:\Backup\SQL (drive)");
            DbmsBackupTrace.Warn("timeout-fired", "save: 15 с");

            var path = Path.Combine(directory, DbmsBackupTrace.FileName);
            Assert.True(WaitFor(() => File.Exists(path), TimeSpan.FromSeconds(5)),
                "Файл диагностики не создан.");

            var text = WaitForText(path,
                value => value.Contains("timeout-fired", StringComparison.Ordinal), TimeSpan.FromSeconds(5));

            Assert.True(text.Contains("timeout-fired", StringComparison.Ordinal),
                "Этапы не записаны в журнал: " + text);
            Assert.Contains("| click |", text, StringComparison.Ordinal);
            Assert.Contains("| enter |", text, StringComparison.Ordinal);
            Assert.Contains("| io-begin |", text, StringComparison.Ordinal);
            Assert.Contains("| WARN |", text, StringComparison.Ordinal);
            Assert.Contains("thread=", text, StringComparison.Ordinal);
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}Z", text);
            Assert.Contains("timeout-fired", DbmsBackupTrace.LastStageDescription, StringComparison.Ordinal);
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previousEnabled;
            DbmsBackupTrace.DirectoryOverride = previousDirectory;
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Trace_Disabled_NoFiles()
    {
        var directory = NewTempDirectory();
        var previousEnabled = DbmsBackupTrace.EnabledOverride;
        var previousDirectory = DbmsBackupTrace.DirectoryOverride;
        try
        {
            DbmsBackupTrace.EnabledOverride = false;
            DbmsBackupTrace.DirectoryOverride = directory;

            DbmsBackupTrace.Log("click", "save");
            DbmsBackupTrace.Warn("timeout-fired", "save");

            Thread.Sleep(200);
            Assert.False(File.Exists(Path.Combine(directory, DbmsBackupTrace.FileName)));
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previousEnabled;
            DbmsBackupTrace.DirectoryOverride = previousDirectory;
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Trace_DoesNotThrow_WhenDirectoryUnavailable()
    {
        var file = Path.Combine(Path.GetTempPath(), "issue143_trace_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a directory");
        var previousEnabled = DbmsBackupTrace.EnabledOverride;
        var previousDirectory = DbmsBackupTrace.DirectoryOverride;
        try
        {
            DbmsBackupTrace.EnabledOverride = true;
            // Каталог журнала вложен в файл — Directory.CreateDirectory упадёт.
            DbmsBackupTrace.DirectoryOverride = Path.Combine(file, "logs");

            var error = Record.Exception(() => DbmsBackupTrace.Log("click", "save"));

            Assert.Null(error);
            Assert.Contains("click", DbmsBackupTrace.LastStageDescription, StringComparison.Ordinal);
        }
        finally
        {
            DbmsBackupTrace.EnabledOverride = previousEnabled;
            DbmsBackupTrace.DirectoryOverride = previousDirectory;
            try { File.Delete(file); } catch { /* тестовый файл мог остаться */ }
        }
    }

    [Fact]
    public void ProfilePath_NetworkPath_LoggedAsWarning()
    {
        Assert.Equal("unc", DbmsBackupTrace.ClassifyPath(@"\\server\share\backup"));
        Assert.True(DbmsBackupTrace.IsNetworkPath(@"\\server\share\backup"));
        Assert.False(DbmsBackupTrace.IsNetworkPath(@"C:\Backup"));
        Assert.Equal("empty", DbmsBackupTrace.ClassifyPath(null));

        var profile = Profile(@"\\server\share\backup");
        var details = DbmsBackupTrace.DescribeProfile(profile);

        Assert.Contains("unc", details, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\\server\share\backup", details, StringComparison.Ordinal);
    }

    /// <summary>
    /// По таймауту окно показывает этап, путь профиля (без пароля) и просит прислать
    /// <c>dbms-backup-debug.log</c>.
    /// </summary>
    [Fact]
    public async Task TimeoutMessage_ContainsStagePathAndDebugLogHint()
    {
        var previous = DbmsBackupService.SaveOverride;
        DbmsBackupService.SaveOverride = (_, _) => new TaskCompletionSource<bool>().Task;
        try
        {
            var vm = CreateViewModel();
            vm.OutputDirectory = @"\\server\share\backup";

            var saved = await vm.SaveAsync(TimeSpan.FromMilliseconds(250));

            Assert.False(saved);
            Assert.False(vm.IsBusy);
            // Журнал окна обновляется через SynchronizationContext — даём ему
            // доехать, затем проверяем этап, путь и подсказку о файле диагностики.
            Assert.True(WaitFor(() => vm.LogText.Contains("save", StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)), vm.LogText);
            Assert.Contains(@"\\server\share\backup", vm.LogText, StringComparison.Ordinal);
            Assert.Contains(DbmsBackupTrace.FileName, vm.LogText, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", vm.LogText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DbmsBackupService.SaveOverride = previous;
        }
    }

    // ===================== Сторож UI-потока =====================

    [Fact]
    public void Watchdog_ReportsStall_WithLastStage()
    {
        var messages = new List<string>();
        var core = new UiThreadWatchdogCore(
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(50),
            () => "stage=save/io-begin",
            message => { lock (messages) messages.Add(message); });

        core.Beat();
        Assert.False(core.CheckOnce());

        Thread.Sleep(300);
        Assert.True(core.CheckOnce());
        // В той же паузе повторно не пишет.
        Assert.False(core.CheckOnce());

        lock (messages)
        {
            var message = Assert.Single(messages);
            Assert.Contains("UI-поток не отвечает", message, StringComparison.Ordinal);
            Assert.Contains("stage=save/io-begin", message, StringComparison.Ordinal);
        }

        // Новый heartbeat закрывает паузу; следующая пауза снова фиксируется.
        core.Beat();
        Thread.Sleep(300);
        Assert.True(core.CheckOnce());
        lock (messages) Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void Watchdog_BackgroundThread_DetectsStall()
    {
        using var reported = new ManualResetEventSlim(false);
        var core = new UiThreadWatchdogCore(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(30),
            () => "stage=run",
            _ => reported.Set());

        core.Start();
        try
        {
            // Heartbeat не обновляется — сторож должен сработать сам.
            Assert.True(reported.Wait(TimeSpan.FromSeconds(5)), "Сторож не зафиксировал паузу.");
        }
        finally
        {
            core.Stop();
        }
    }

    // ===================== Вспомогательные методы =====================

    private static DbmsBackupViewModel CreateViewModel()
    {
        var infobase = new Infobase
        {
            Id = "base-1",
            Name = "Demo",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = "demo"
            }
        };
        var vm = new DbmsBackupViewModel(new[] { infobase }, infobase, new DbmsBackupData());
        vm.DbmsIndex = 0;
        vm.Host = "127.0.0.1";
        vm.Port = "5432";
        vm.Database = "demo";
        vm.User = "postgres";
        vm.Password = "secret";
        vm.OutputDirectory = Path.GetTempPath();
        vm.KeepCount = "7";
        return vm;
    }

    private static DbmsBackupProfile Profile(string outputDirectory) => new()
    {
        InfobaseId = "base-1",
        Dbms = DbmsKind.PostgreSql,
        Host = "127.0.0.1",
        Port = 5432,
        Database = "demo",
        User = "postgres",
        Password = "secret",
        OutputDirectory = outputDirectory,
        KeepCount = 7
    };

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "issue143_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Тестовый каталог мог остаться занятым — на результат не влияет.
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    private static string WaitForText(string path, Func<string, bool> predicate, TimeSpan timeout)
    {
        var text = string.Empty;
        _ = WaitFor(() =>
        {
            try
            {
                if (!File.Exists(path))
                    return false;
                // Чтение с FileShare.ReadWrite: писатель журнала не должен терять
                // строки из-за того, что тест держит файл открытым.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
                return predicate(text);
            }
            catch (IOException)
            {
                return false;
            }
        }, timeout);
        return text;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var fullPath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            directory = directory.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    /// <summary>
    /// Выполняет действие на отдельном потоке с собственным контекстом синхронизации:
    /// продолжения <c>await</c> идут на этот контекст, а не в (занятый) пул потоков.
    /// </summary>
    private static T RunWithDedicatedContext<T>(Func<T> action, int timeoutMs = 15000)
    {
        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);
        using var context = new DedicatedSyncContext();

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true,
            Name = "issue143-probe"
        };

        thread.Start();
        Assert.True(done.Wait(timeoutMs), "Операция не вернула управление за отведённое время.");
        if (error is not null)
            throw error;
        return result;
    }

    /// <summary>Однопоточный контекст: выполняет Post-колбэки на своём потоке.</summary>
    private sealed class DedicatedSyncContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _pump;

        public DedicatedSyncContext()
        {
            _pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "issue143-sync-context"
            };
            _pump.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch
            {
                // Очередь закрыта — тест уже завершился.
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Dispose()
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch
            {
                // Уже закрыта.
            }
        }

        private void Pump()
        {
            SynchronizationContext.SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    callback(state);
                }
                catch
                {
                    // Ошибка продолжения уже передана в Task операции.
                }
            }
        }
    }

    /// <summary>
    /// Занимает потоки пула блокирующими задачами: эмуляция голодания, из-за которого
    /// <c>CancelAfter</c>/<c>WaitAsync</c> не срабатывают.
    /// </summary>
    private sealed class ThreadPoolStarvation : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private readonly Task[] _blockers;

        public ThreadPoolStarvation(int workers)
        {
            var count = Math.Max(4, workers);
            _blockers = new Task[count];
            for (var i = 0; i < count; i++)
                _blockers[i] = Task.Run(() => _gate.Wait(TimeSpan.FromSeconds(30)));
            // Дать пулу реально занять потоки блокирующими задачами.
            Thread.Sleep(200);
        }

        public void Dispose()
        {
            _gate.Set();
            try
            {
                Task.WaitAll(_blockers, TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Блокирующие задачи уже отпущены.
            }
            _gate.Dispose();
        }
    }
}
