using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (комментарий RafaIS 2026-11-23): окно «Резервные копии СУБД» всё ещё
/// «наглухо» зависало на «Проверить подключение», «Проверить каталог», «Сохранить».
/// <para>
/// Раньше тесты проверяли только наличие строк в исходниках. Здесь — поведенческие
/// проверки: блокирующие заглушки, возврат по таймауту, сброс <c>IsBusy</c>,
/// доставка <see cref="CancellationToken"/> до заглушки, фоновая загрузка старого
/// профиля без исключения. Все тесты — в коллекции с отключённой параллельностью:
/// они подменяют статические <see cref="DbmsBackupService.RunToolOverride"/>,
/// <see cref="DbmsBackupService.SaveOverride"/> и
/// <see cref="FeatureDataStore.DirectoryOverride"/>.
/// </para>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue143ResponsivenessTests
{
    // ===================== Сохранение профиля =====================

    /// <summary>
    /// Блокирующий saver (игнорирует токен): «Сохранить» обязано вернуть управление
    /// не позже таймаута, сбросить IsBusy и показать Dbms.SaveTimeout. Без фикса
    /// синхронный Save на UI-потоке держал бы окно до конца записи.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ReturnsWithinTimeout_WhenSaverBlocks()
    {
        var previous = DbmsBackupService.SaveOverride;
        var invoked = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DbmsBackupService.SaveOverride = (_, ct) =>
        {
            invoked.TrySetResult(ct);
            // Игнорируем токен — имитируем зависшую сетевую запись.
            return Task.Delay(TimeSpan.FromSeconds(30));
        };

        try
        {
            var vm = CreateViewModel();
            var sw = Stopwatch.StartNew();
            var saved = await vm.SaveAsync(TimeSpan.FromMilliseconds(300));
            sw.Stop();

            Assert.False(saved);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"SaveAsync не вернулся по таймауту за {sw.Elapsed}.");
            Assert.False(vm.IsBusy);
            Assert.True(vm.IsIdle);
            Assert.Equal(LocalizationManager.T("Dbms.SaveTimeout"), vm.StatusText);

            var ct = await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(ct.IsCancellationRequested, "Токен записи не был отменён по таймауту.");
        }
        finally
        {
            DbmsBackupService.SaveOverride = previous;
        }
    }

    [Fact]
    public async Task SaveAsync_DefaultSaver_WritesProfile()
    {
        var dataDir = NewTempDirectory();
        var previous = FeatureDataStore.DirectoryOverride;
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            var vm = CreateViewModel();

            var saved = await vm.SaveAsync(TimeSpan.FromSeconds(5));

            Assert.True(saved);
            Assert.False(vm.IsBusy);
            var reloaded = await DbmsBackupService.LoadAsync();
            Assert.NotNull(reloaded.Find("base-1"));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            TryDeleteDirectory(dataDir);
        }
    }

    // ===================== Проверки подключения/каталога =====================

    /// <summary>
    /// Блокирующая утилизационная заглушка: проверка подключения обязана вернуться
    /// по таймауту, сбросить IsBusy и передать отменённый токен до ProcessRunner.
    /// </summary>
    [Fact]
    public async Task CheckConnection_ReturnsTimeout_AndCancelsTool()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var previous = DbmsBackupService.RunToolOverride;
        var invoked = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        DbmsBackupService.RunToolOverride = (_, _, _, _, ct) =>
        {
            invoked.TrySetResult(ct);
            // Никогда не завершается и игнорирует токен — проверка должна прерваться
            // по WaitAsync в ViewModel, а токен — быть отменён.
            return new TaskCompletionSource<ProcessRunResult>().Task;
        };

        try
        {
            var vm = CreateViewModel();
            vm.Host = "127.0.0.1";
            vm.Port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();

            var sw = Stopwatch.StartNew();
            await vm.CheckConnectionAsync(TimeSpan.FromMilliseconds(300));
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"Проверка подключения не вернулась по таймауту за {sw.Elapsed}.");
            Assert.False(vm.IsBusy);
            Assert.True(vm.IsIdle);
            Assert.Equal(LocalizationManager.T("Dbms.CheckTimeout"), vm.StatusText);

            var ct = await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(ct.IsCancellationRequested, "Токен проверки не дошёл отменённым до утилиты.");
        }
        finally
        {
            DbmsBackupService.RunToolOverride = previous;
            listener.Stop();
        }
    }

    [Fact]
    public async Task CheckDirectory_ReturnsSuccess_ForWritableTemp()
    {
        var dir = NewTempDirectory();
        try
        {
            var vm = CreateViewModel();
            vm.OutputDirectory = dir;

            await vm.CheckDirectoryAsync(TimeSpan.FromSeconds(5));

            Assert.False(vm.IsBusy);
            Assert.Contains(LocalizationManager.T("Dbms.CheckOk"), vm.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    /// <summary>
    /// Проверка каталога слушает токен: отменённый токен прерывает её до файловых
    /// операций (недоступный UNC не «висит»).
    /// </summary>
    [Fact]
    public async Task CheckOutputDirectory_HonorsCanceledToken()
    {
        var profile = Profile(Path.Combine(Path.GetTempPath(), "issue143_unc_" + Guid.NewGuid().ToString("N")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DbmsBackupService.CheckOutputDirectoryAsync(profile, cts.Token));
    }

    // ===================== Загрузка профиля вне UI-потока =====================

    [Fact]
    public void Constructor_DoesNotLoadProfileSynchronously()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");

        Assert.DoesNotContain("_data = data ?? DbmsBackupService.Load()", source, StringComparison.Ordinal);
        Assert.Contains("public async Task InitializeAsync", source, StringComparison.Ordinal);
        Assert.Contains("DbmsBackupService.LoadAsync", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Данные, не переданные в конструктор, НЕ читаются в нём: до InitializeAsync
    /// поля содержат подсказку, после — сохранённый профиль.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_LoadsProfile_ThatConstructorDidNotRead()
    {
        var dataDir = NewTempDirectory();
        var previous = FeatureDataStore.DirectoryOverride;
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            await File.WriteAllTextAsync(Path.Combine(dataDir, DbmsBackupService.FileName),
                OldProfileJson("saved-host", "saved_db", "\\\\192.0.2.1\\backup"));

            var vm = CreateViewModel(data: null, provideEmptyData: false);

            Assert.True(vm.NeedsLoad);
            Assert.NotEqual("saved-host", vm.Host); // конструктор не читал профиль

            await vm.InitializeAsync();

            Assert.Equal("saved-host", vm.Host);
            Assert.Equal("saved_db", vm.Database);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            TryDeleteDirectory(dataDir);
        }
    }

    /// <summary>
    /// Старый dbms_backups.json: TimeoutMinutes = 0, отрицательный KeepCount, путь в
    /// кавычках и недоступный сетевой OutputDirectory — загружается без исключения,
    /// нормализуется и не приводит к блокирующим операциям в конструкторе.
    /// </summary>
    [Fact]
    public async Task LoadAsync_NormalizesOldProfile_WithoutException()
    {
        var dataDir = NewTempDirectory();
        var previous = FeatureDataStore.DirectoryOverride;
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            await File.WriteAllTextAsync(Path.Combine(dataDir, DbmsBackupService.FileName),
                OldProfileJson("saved-host", "saved_db", "\"  \\\\192.0.2.1\\backup  \""));

            var data = await DbmsBackupService.LoadAsync();

            var profile = data.Find("base-1");
            Assert.NotNull(profile);
            Assert.Equal(360, profile!.TimeoutMinutes);
            Assert.Equal(7, profile.KeepCount);
            Assert.Equal("\\\\192.0.2.1\\backup", profile.OutputDirectory);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            TryDeleteDirectory(dataDir);
        }
    }

    [Fact]
    public async Task LoadAsync_MalformedFile_ReturnsEmpty_WithoutException()
    {
        var dataDir = NewTempDirectory();
        var previous = FeatureDataStore.DirectoryOverride;
        try
        {
            FeatureDataStore.DirectoryOverride = dataDir;
            await File.WriteAllTextAsync(Path.Combine(dataDir, DbmsBackupService.FileName), "{ not valid json");

            var data = await DbmsBackupService.LoadAsync();

            Assert.NotNull(data);
            Assert.Empty(data.Profiles);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            TryDeleteDirectory(dataDir);
        }
    }

    // ===================== Окна: async-обработчики =====================

    [Theory]
    [InlineData("DbmsBackupWindow.cs")]
    [InlineData("DbmsBackupWindow.Avalonia.cs")]
    public void Windows_UseAsyncHandlers_WithoutBlockingResult(string file)
    {
        var source = ReadProjectFile("Workspace1C", "Views", file);

        Assert.Contains("await _vm.SaveAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _vm.CheckConnectionAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _vm.CheckDirectoryAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await _vm.RunAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync().Result", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckConnectionAsync().Result", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", source, StringComparison.Ordinal);
        // Окно догружает профиль в фоне, а не блокируется синхронным Load.
        Assert.Contains("InitializeVmAsync", source, StringComparison.Ordinal);
    }

    // ===================== Процесс и локализация =====================

    [Fact]
    public void ProcessRunner_BoundsOutputDrainAfterExit()
    {
        var code = ReadProjectFile("Workspace1C", "Services", "ProcessRunner.cs");

        Assert.Contains("OutputDrainTimeout", code, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAny(readTask, Task.Delay(OutputDrainTimeout))", code, StringComparison.Ordinal);
        Assert.Contains("readCts?.Cancel()", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasTimeoutKeys(string file)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", file);

        Assert.Contains("\"Dbms.Saving\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.SaveTimeout\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Dbms.CheckTimeout\"", json, StringComparison.Ordinal);
    }

    // ===================== Вспомогательные методы =====================

    private static string OldProfileJson(string host, string database, string outputDirectory)
    {
        // Сериализатор экранирует пути/кавычки — получается валидный старый JSON.
        var payload = new
        {
            Profiles = new[]
            {
                new
                {
                    InfobaseId = "base-1",
                    Dbms = "PostgreSql",
                    Host = host,
                    Port = 5432,
                    Database = database,
                    User = "postgres",
                    Password = "secret",
                    OutputDirectory = outputDirectory,
                    KeepCount = -5,
                    TimeoutMinutes = 0
                }
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static DbmsBackupViewModel CreateViewModel(
        DbmsBackupData? data = null, bool provideEmptyData = true)
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
        var vm = new DbmsBackupViewModel(new[] { infobase }, infobase,
            data ?? (provideEmptyData ? new DbmsBackupData() : null));
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

    private static DbmsBackupProfile Profile(string dir) => new()
    {
        InfobaseId = "base-1",
        Dbms = DbmsKind.PostgreSql,
        Host = "127.0.0.1",
        Port = 5432,
        Database = "demo",
        User = "postgres",
        Password = "secret",
        OutputDirectory = dir,
        KeepCount = 7
    };

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "issue143_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Тестовый каталог мог остаться занятым — на результат не влияет.
        }
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }
}
