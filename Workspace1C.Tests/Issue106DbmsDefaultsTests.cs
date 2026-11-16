using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #106 «Массовая загрузка cf» — остаток по комментарию aepkugesi (2026-11-15):
/// 1) журнал окна массовой загрузки всегда содержит след операции (старт с параметрами,
///    строка по каждой базе, итоговая сводка), очистка — только в начале прогона;
/// 2) профиль «Резервные копии СУБД» больше не подставляет PostgreSQL молча: тип
///    берётся из сохранённого профиля → кластера rac → общей настройки → явная подсказка;
/// 3) общая настройка «Тип СУБД по умолчанию» в <see cref="AppSettings"/> и обеих оболочках.
/// Тесты чистых функций, ViewModel (без реальной 1С/rac), обратной совместимости JSON,
/// source-гвардов WPF/Avalonia и совпадения ключей локализации ru/en.
/// </summary>
public sealed class Issue106DbmsDefaultsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cfFile;

    public Issue106DbmsDefaultsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "issue106dbms_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cfFile = Path.Combine(_tempDir, "conf.cf");
        File.WriteAllText(_cfFile, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* не критично */ }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray()));

    private static Infobase ServerBase(string name = "Бухгалтерия", string id = "base-1") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv1c",
            DatabaseName = name
        }
    };

    private static Infobase FileBase(string name = "Файловая", string id = "file-1") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = Path.GetTempPath() }
    };

    // ======================= Приоритет источников типа СУБД =======================

    [Fact]
    public void Resolve_SavedProfile_WinsOverClusterAndSetting()
    {
        // Сохранённый профиль настроен пользователем вручную — он важнее автоматики
        // даже при конфликте «профиль MS SQL, кластер PostgreSQL» (issue #106).
        var result = DbmsDefaults.Resolve(DbmsKind.MsSql, DbmsKind.PostgreSql, "PostgreSQL");

        Assert.Equal(DbmsKind.MsSql, result.Kind);
        Assert.Equal(DbmsKindSource.SavedProfile, result.Source);
        Assert.True(result.IsKnown);
    }

    [Fact]
    public void Resolve_Cluster_WinsOverSetting()
    {
        var result = DbmsDefaults.Resolve(null, DbmsKind.MsSql, "PostgreSQL");

        Assert.Equal(DbmsKind.MsSql, result.Kind);
        Assert.Equal(DbmsKindSource.Cluster, result.Source);
    }

    [Fact]
    public void Resolve_Setting_UsedWhenNoProfileAndNoCluster()
    {
        var result = DbmsDefaults.Resolve(null, null, "MsSql");

        Assert.Equal(DbmsKind.MsSql, result.Kind);
        Assert.Equal(DbmsKindSource.Setting, result.Source);
    }

    [Fact]
    public void Resolve_NothingKnown_IsUnknownAndNotPostgreSql()
    {
        var result = DbmsDefaults.Resolve(null, null, null);

        Assert.Null(result.Kind);
        Assert.Equal(DbmsKindSource.Unknown, result.Source);
        Assert.False(result.IsKnown);
        Assert.Equal(-1, DbmsDefaults.ToIndex(result.Kind));
    }

    // ======================= Разбор/запись настройки =======================

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("notset", null)]
    [InlineData("не задан", null)]
    [InlineData("PostgreSQL", DbmsKind.PostgreSql)]
    [InlineData("postgresql", DbmsKind.PostgreSql)]
    [InlineData("postgres", DbmsKind.PostgreSql)]
    [InlineData("MsSql", DbmsKind.MsSql)]
    [InlineData("MSSQLServer", DbmsKind.MsSql)]
    [InlineData("Microsoft SQL Server", DbmsKind.MsSql)]
    [InlineData("microsoftsqlserver", DbmsKind.MsSql)]
    [InlineData("unknown-vendor", null)]
    public void ParseSetting_NormalizesKnownValues(string? value, DbmsKind? expected)
    {
        Assert.Equal(expected, DbmsDefaults.ParseSetting(value));
    }

    [Fact]
    public void ToSetting_RoundTripsCanonicalValues()
    {
        Assert.Equal("PostgreSQL", DbmsDefaults.ToSetting(DbmsKind.PostgreSql));
        Assert.Equal("MsSql", DbmsDefaults.ToSetting(DbmsKind.MsSql));
        Assert.Equal(string.Empty, DbmsDefaults.ToSetting(null));
        Assert.Equal("PostgreSQL", DbmsDefaults.NormalizeSetting("postgres"));
        Assert.Equal("MsSql", DbmsDefaults.NormalizeSetting("Microsoft SQL Server"));
        Assert.Equal(string.Empty, DbmsDefaults.NormalizeSetting("что-то"));
    }

    [Fact]
    public void ParseRacDbms_UnderstandsRacValues()
    {
        Assert.Equal(DbmsKind.PostgreSql, DbmsDefaults.ParseRacDbms("PostgreSQL"));
        Assert.Equal(DbmsKind.MsSql, DbmsDefaults.ParseRacDbms("MSSQLServer"));
        Assert.Null(DbmsDefaults.ParseRacDbms("OracleDatabase"));
        Assert.Null(DbmsDefaults.ParseRacDbms(""));
    }

    // ======================= Обратная совместимость AppSettings =======================

    [Fact]
    public void AppSettings_OldJsonWithoutField_ReadsAsNotSet()
    {
        const string json = "{\"ShowFavoritesOnly\":false,\"ListMode\":\"All\"}";

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(string.Empty, settings!.DefaultDbmsKind);
        Assert.Null(DbmsDefaults.ParseSetting(settings.DefaultDbmsKind));
    }

    [Fact]
    public void AppSettings_DefaultDbmsKind_RoundTrips()
    {
        var settings = new AppSettings { DefaultDbmsKind = DbmsDefaults.MsSqlSetting };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.Contains("\"DefaultDbmsKind\":\"MsSql\"", json, StringComparison.Ordinal);
        Assert.Equal(DbmsDefaults.MsSqlSetting, restored!.DefaultDbmsKind);
    }

    // ======================= ViewModel профиля резервных копий =======================

    [Fact]
    public void ViewModel_NoProfileNoClusterNoSetting_LeavesDbmsUnknown()
    {
        var vm = new DbmsBackupViewModel(new[] { ServerBase() }, null);

        Assert.Equal(-1, vm.DbmsIndex);
        Assert.Equal("Dbms.NotDetectedHint", vm.DbmsHint);
        // Профиль не сохраняется с молчаливым PostgreSQL.
        Assert.False(vm.Save());
        Assert.Contains("Dbms.DbmsRequired", vm.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_SavedProfile_WinsOverGlobalSetting()
    {
        var data = new DbmsBackupData();
        data.Upsert(new DbmsBackupProfile { InfobaseId = "base-1", Dbms = DbmsKind.MsSql });

        var vm = new DbmsBackupViewModel(
            new[] { ServerBase() }, null, data, defaultDbms: "PostgreSQL");

        Assert.Equal(1, vm.DbmsIndex);
        Assert.Equal(string.Empty, vm.DbmsHint);
    }

    [Fact]
    public void ViewModel_GlobalSetting_UsedAsFallback()
    {
        var vm = new DbmsBackupViewModel(
            new[] { ServerBase() }, null, new DbmsBackupData(), defaultDbms: "MsSql");

        Assert.Equal(1, vm.DbmsIndex);
        Assert.Equal(DbmsKind.MsSql, vm.CurrentProfile()!.Dbms);
        Assert.Equal(string.Empty, vm.DbmsHint);
    }

    [Fact]
    public void ViewModel_ExplicitChoice_OverridesUnknownHint()
    {
        var vm = new DbmsBackupViewModel(new[] { ServerBase() }, null);

        Assert.Equal(-1, vm.DbmsIndex);
        vm.DbmsIndex = 0;

        Assert.Equal(string.Empty, vm.DbmsHint);
        Assert.Equal(DbmsKind.PostgreSql, vm.CurrentProfile()!.Dbms);
    }

    /// <summary>
    /// Создаёт ViewModel профиля без контекста синхронизации: результат фонового
    /// резолва кластера применяется синхронно до возврата из await (детерминизм теста).
    /// </summary>
    private static DbmsBackupViewModel CreateDbmsVmNoContext(
        Infobase[] bases, DbmsBackupData data, string? defaultDbms,
        ClusterInfobaseCredentialsResolver? resolver)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new DbmsBackupViewModel(bases, null, data, defaultDbms, resolver);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static FakeRac MsSqlRac() => new()
    {
        Clusters = new[] { new RacCluster { Id = Guid.NewGuid(), Name = "Кластер" } },
        Summaries = new[]
        {
            new RacInfobaseSummary
            {
                InfobaseId = Guid.NewGuid(),
                Name = "Бухгалтерия",
                Dbms = "MSSQLServer",
                DbServer = "sql-host",
                DbName = "buh_db"
            }
        }
    };

    [Fact]
    public async Task ViewModel_ClusterResolve_AppliesWhenNoSavedProfile()
    {
        var resolver = new ClusterInfobaseCredentialsResolver(MsSqlRac());
        var vm = CreateDbmsVmNoContext(
            new[] { ServerBase() }, new DbmsBackupData(),
            defaultDbms: "PostgreSQL", resolver: resolver);

        Assert.Equal(0, vm.DbmsIndex); // пока — общая настройка
        await vm.ResolveDbmsFromClusterAsync();

        Assert.Equal(1, vm.DbmsIndex); // кластер приоритетнее настройки
        Assert.Equal(string.Empty, vm.DbmsHint);
    }

    [Fact]
    public async Task ViewModel_SavedProfile_PriorityOverCluster()
    {
        var data = new DbmsBackupData();
        data.Upsert(new DbmsBackupProfile { InfobaseId = "base-1", Dbms = DbmsKind.PostgreSql });
        var resolver = new ClusterInfobaseCredentialsResolver(MsSqlRac());
        var vm = CreateDbmsVmNoContext(
            new[] { ServerBase() }, data, defaultDbms: null, resolver: resolver);

        await vm.ResolveDbmsFromClusterAsync();

        Assert.Equal(0, vm.DbmsIndex); // профиль PostgreSQL важнее кластера
    }

    // ======================= Журнал массовой загрузки =======================

    private static MassLoadCfViewModel CreateMassLoadVm(params Infobase[] bases)
    {
        // Без контекста синхронизации журнал пишется напрямую — тест детерминирован.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new MassLoadCfViewModel(bases);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private MassLoadCfViewModel ConfiguredMassLoadVm(params Infobase[] bases)
    {
        var vm = CreateMassLoadVm(bases);
        vm.CfFilePath = _cfFile;
        vm.RunnerOverride = (_, _, _, _, _) => Task.FromResult(MassLoadCfRunnerResult.Ok());
        vm.SetAll(true);
        return vm;
    }

    [Fact]
    public async Task MassLoad_AllOptionsOff_LogsStartPerBaseAndSummary()
    {
        var vm = ConfiguredMassLoadVm(FileBase());

        await vm.RunAsync(CancellationToken.None);

        Assert.NotEmpty(vm.LogLines);
        Assert.Contains(vm.LogLines, l => l.Contains("MassLoadCf.LogStartFormat", StringComparison.Ordinal));
        Assert.Contains(vm.LogLines, l => l.Contains("MassLoadCf.LogBaseStartFormat", StringComparison.Ordinal));
        Assert.Contains(vm.LogLines, l => l.Contains("BatchOps.SummaryFormat", StringComparison.Ordinal));
        // Все опции подготовки выключены — в стартовой строке явно сказано об этом.
        Assert.Equal("MassLoadCf.OptionsNone",
            MassLoadCfViewModel.DescribeOptions(new MassLoadCfOptions()));
    }

    [Fact]
    public async Task MassLoad_LogIsClearedOnlyAtStartOfNextRun()
    {
        var vm = ConfiguredMassLoadVm(FileBase());

        await vm.RunAsync(CancellationToken.None);
        Assert.NotEmpty(vm.LogLines);
        // После завершения журнал не очищается.
        Assert.Contains(vm.LogLines, l => l.Contains("MassLoadCf.LogStartFormat", StringComparison.Ordinal));

        vm.SetAll(true);
        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(1, vm.LogLines.Count(l => l.Contains("MassLoadCf.LogStartFormat", StringComparison.Ordinal)));
        Assert.Equal(1, vm.LogLines.Count(l => l.Contains("MassLoadCf.LogBaseStartFormat", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MassLoad_NoSelection_StillLogsStartAndSummary()
    {
        var vm = CreateMassLoadVm(FileBase());
        vm.SetAll(false);

        await vm.RunAsync(CancellationToken.None);

        Assert.Contains(vm.LogLines, l => l.Contains("MassLoadCf.LogStartFormat", StringComparison.Ordinal));
        Assert.Contains(vm.LogLines, l => l.Contains("BatchOps.NothingSelected", StringComparison.Ordinal));
        Assert.Contains(vm.LogLines, l => l.Contains("BatchOps.SummaryFormat", StringComparison.Ordinal));
        Assert.Equal("BatchOps.SummaryFormat", vm.Summary);
    }

    [Fact]
    public async Task MassLoad_ValidationFailure_StillLogsStartAndSummary()
    {
        var vm = CreateMassLoadVm(FileBase());
        vm.CfFilePath = string.Empty; // нет файла .cf — валидация не пройдёт
        vm.SetAll(true);

        await vm.RunAsync(CancellationToken.None);

        Assert.Contains(vm.LogLines, l => l.Contains("MassLoadCf.LogStartFormat", StringComparison.Ordinal));
        Assert.Contains(vm.LogLines, l => l.Contains("BatchOps.ValidationFailed", StringComparison.Ordinal));
        Assert.Equal("BatchOps.ValidationFailed", vm.Summary);
    }

    // ======================= Source-гварды WPF/Avalonia =======================

    [Fact]
    public void Wpf_Settings_HasDefaultDbmsControl_AndSavesIt()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml.cs");

        Assert.Contains("DefaultDbmsCombo", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.DefaultDbms", xaml, StringComparison.Ordinal);
        Assert.Contains("DefaultDbmsCombo.SelectedIndex", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.DefaultDbmsKind", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Settings_HasDefaultDbmsControl_AndAppliesIt()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        Assert.Contains("Settings.DefaultDbms", source, StringComparison.Ordinal);
        Assert.Contains("_viewModel.DefaultDbmsKind", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DbmsProfile_UsesPureResolver_AndClusterFallback()
    {
        var vmSource = ReadProjectFile("Workspace1C", "ViewModels", "DbmsBackupViewModel.cs");
        var wpfSource = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Features.cs");
        var avaloniaSource = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Features.cs");

        Assert.Contains("DbmsDefaults.Resolve", vmSource, StringComparison.Ordinal);
        Assert.Contains("ResolveDbmsFromClusterAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("Dbms.NotDetectedHint", vmSource, StringComparison.Ordinal);
        Assert.Contains("defaultDbms: DefaultDbmsKind", wpfSource, StringComparison.Ordinal);
        Assert.Contains("defaultDbms: DefaultDbmsKind", avaloniaSource, StringComparison.Ordinal);
        Assert.Contains("ClusterInfobaseCredentialsResolver.CreateDefault()", wpfSource, StringComparison.Ordinal);
        Assert.Contains("ClusterInfobaseCredentialsResolver.CreateDefault()", avaloniaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MassLoadViewModel_LogsStartBaseSummary_AndClearsOnlyAtStart()
    {
        var source = ReadProjectFile("Workspace1C", "ViewModels", "MassLoadCfViewModel.cs");

        Assert.Contains("MassLoadCf.LogStartFormat", source, StringComparison.Ordinal);
        Assert.Contains("MassLoadCf.LogBaseStartFormat", source, StringComparison.Ordinal);
        Assert.Contains("MassLoadCf.OptionsNone", source, StringComparison.Ordinal);
        Assert.Contains("MassLoadCf.LogCancelled", source, StringComparison.Ordinal);
        Assert.Contains("AuditHub.Write", source, StringComparison.Ordinal);

        var clearIndex = source.IndexOf("LogLines.Clear()", StringComparison.Ordinal);
        var startLogIndex = source.IndexOf("MassLoadCf.LogStartFormat", StringComparison.Ordinal);
        Assert.True(clearIndex > 0, "MassLoadCfViewModel: очистка журнала не найдена.");
        Assert.True(startLogIndex > clearIndex, "Стартовая строка должна писаться после очистки в начале прогона.");
        // Других очисток журнала нет: по завершении журнал сохраняется.
        Assert.Equal(1, CountOccurrences(source, "LogLines.Clear()"));
    }

    [Fact]
    public void Localization_MassLoadAndDbmsKeys_MatchRuAndEn()
    {
        var ru = ReadJsonKeys("ru.json");
        var en = ReadJsonKeys("en.json");
        var prefixes = new[]
        {
            "MassLoadCf.Log",
            "MassLoadCf.Option",
            "MassLoadCf.OptionsNone",
            "Dbms.DbmsRequired",
            "Dbms.NotDetectedHint",
            "Settings.DefaultDbms"
        };

        var ruKeys = ru.Where(k => prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).OrderBy(k => k).ToArray();
        var enKeys = en.Where(k => prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).OrderBy(k => k).ToArray();

        Assert.NotEmpty(ruKeys);
        Assert.Equal(ruKeys, enKeys);
    }

    private static HashSet<string> ReadJsonKeys(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        // Файл локализации: { "code": "...", "strings": { "Ключ": "Значение", ... } }.
        var strings = document.RootElement.GetProperty("strings");
        return strings.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    // ======================= Фейковый rac-клиент =======================

    /// <summary>Минимальный фейк <see cref="IRacClient"/> (образец Issue106MassLoadCfTests).</summary>
    private sealed class FakeRac : IRacClient
    {
        public RacCluster[] Clusters { get; init; } = Array.Empty<RacCluster>();
        public RacInfobaseSummary[] Summaries { get; init; } = Array.Empty<RacInfobaseSummary>();
        public RacInfobaseInfo? Info { get; init; }
        public RacSessionInfo[] Sessions { get; init; } = Array.Empty<RacSessionInfo>();

        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(RacConnectionParams p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(Clusters);

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Summaries);

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(RacConnectionParams p, Guid c, Guid i, string? u, string? pw, CancellationToken ct = default, TimeSpan? timeout = null)
            => Task.FromResult(Info);

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Sessions);

        public Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid c, Guid s, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> UpdateInfobaseAsync(RacConnectionParams p, Guid c, Guid i, RacInfobaseUpdateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<RacClusterInfo?> GetClusterInfoAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(RacConnectionParams p, Guid c, Guid? i = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(RacConnectionParams p, Guid c, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> UpdateClusterAsync(RacConnectionParams p, Guid c, RacClusterUpdate u, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> SetJobStateAsync(RacConnectionParams p, Guid c, Guid j, RacJobAction a, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid c, Guid con, Guid? pr = null, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> CreateInfobaseAsync(RacConnectionParams p, Guid c, RacInfobaseCreateRequest r, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> DropInfobaseAsync(RacConnectionParams p, Guid c, Guid i, bool d = false, string? u = null, string? pw = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
