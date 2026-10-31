using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты экспорта сводки сервера в CSV (v100112, F2): сборка строк сводки
/// (<see cref="ServerMonitorViewModel.BuildSummaryCsvRows"/>) — секции, заголовки,
/// данные строк; запись файла через <see cref="CsvExporter"/> (UTF-8 с BOM, RFC 4180)
/// по пути из диалога сохранения; отмена диалога — файл не создаётся.
/// </summary>
public sealed class ServerMonitorCsvSummaryTests : IDisposable
{
    private readonly string _tempDir;

    public ServerMonitorCsvSummaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "w1c_csv_summary_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* очистка вспомогательная */ }
    }

    private static RacSessionRow MakeSession(string user) =>
        new(new RacSessionInfo { Id = Guid.NewGuid(), User = user, Host = "pc1", State = "active" });

    private static RacProcessRow MakeProcess(int pid) =>
        new(new RacProcessInfo { Id = Guid.NewGuid(), Pid = pid, Type = "rphost" });

    private static RacInfobaseRow MakeInfobase(string name) =>
        new(new RacInfobaseSummary { InfobaseId = Guid.NewGuid(), Name = name });

    /// <summary>Локализация по ключу (как в проектных тестах: словарь сравнивается сам с собой).</summary>
    private static string T(string key) => LocalizationManager.T(key);

    [Fact]
    public void BuildSummaryCsvRows_ContainsSections_Headers_AndData()
    {
        var rows = ServerMonitorViewModel.BuildSummaryCsvRows(
            "Сервер 1С", "localhost", 1540,
            new RacClusterInfo { Name = "Локальный кластер" }, "Подключено",
            new[] { MakeProcess(1234) },
            new[] { MakeSession("Иванов") },
            Array.Empty<RacConnectionRow>(),
            Array.Empty<RacLockRow>(),
            new[] { MakeInfobase("Рабочая") },
            Array.Empty<RacLicensedSessionRow>(),
            Array.Empty<ProgramLicense>(),
            "Всего: 10, занято: 7, свободно: 3", string.Empty,
            new DateTime(2026, 10, 31, 12, 0, 0));

        // Секции сводки присутствуют с количеством строк (сверка с локализацией —
        // как в RacJobRowTests: словарь в тестах может быть пуст, ключ сравнивается
        // сам с собой).
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == T("ServerMonitor.Export.Section.General"));
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == $"{T("ServerMonitor.Tabs.Sessions")} (1)");
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == $"{T("ServerMonitor.Tabs.Processes")} (1)");
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == $"{T("ServerMonitor.Tabs.Infobases")} (1)");
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty).StartsWith(T("ServerMonitor.Tabs.Licenses"), StringComparison.Ordinal));

        // Общая информация: сервер, кластер, время экспорта.
        Assert.Contains(rows, r => r.Count >= 2 && (r[1] ?? string.Empty) == "Сервер 1С");
        Assert.Contains(rows, r => r.Count >= 2 && (r[1] ?? string.Empty) == "localhost:1540");
        Assert.Contains(rows, r => r.Count >= 2 && (r[1] ?? string.Empty) == "Локальный кластер");
        Assert.Contains(rows, r => r.Count >= 2 && (r[1] ?? string.Empty) == "31.10.2026 12:00:00");

        // Строка данных сеанса: пользователь из вкладки «Сеансы».
        Assert.Contains(rows, r => r.Count >= 2 && (r[0] ?? string.Empty) == "Иванов");

        // Строка данных базы.
        Assert.Contains(rows, r => r.Count >= 1 && (r[0] ?? string.Empty) == "Рабочая");
    }

    [Fact]
    public void BuildSummaryCsvRows_EmptyData_StillContainsSections()
    {
        var rows = ServerMonitorViewModel.BuildSummaryCsvRows(
            string.Empty, "srv", 0, null, string.Empty,
            Array.Empty<RacProcessRow>(), Array.Empty<RacSessionRow>(),
            Array.Empty<RacConnectionRow>(), Array.Empty<RacLockRow>(),
            Array.Empty<RacInfobaseRow>(), Array.Empty<RacLicensedSessionRow>(),
            Array.Empty<ProgramLicense>(), string.Empty, string.Empty,
            DateTime.Now);

        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == $"{T("ServerMonitor.Tabs.Sessions")} (0)");
        Assert.Contains(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == $"{T("ServerMonitor.Tabs.Infobases")} (0)");
        // Пустая сводка лицензий — без заголовков таблиц лицензий.
        Assert.DoesNotContain(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == T("ServerMonitor.Licenses.Columns.User"));
        Assert.DoesNotContain(rows, r => r.Count > 0 && (r[0] ?? string.Empty) == T("ServerMonitor.Licenses.Columns.Serial"));
    }

    [Fact]
    public void ExportSummary_WritesCsvFile_WithUtf8Bom_AndEscaping()
    {
        var client = new FakeRacClient();
        var path = Path.Combine(_tempDir, "summary.csv");
        var dialogs = new RecordingDialogs { SavedPath = path };
        var vm = new ServerMonitorViewModel(client, dialogs);
        // Экспорт требует подключения (кнопка неактивна без него).
        vm.ConnectAsync().Wait();

        vm.ExportSummary();

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 3);
        // UTF-8 BOM — иначе Excel не распознаёт кириллицу.
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var content = File.ReadAllText(path);
        Assert.Contains(T("ServerMonitor.Tabs.Sessions"), content);
        Assert.NotEqual(0, dialogs.SaveDialogs);
    }

    [Fact]
    public void ExportSummary_CancelledDialog_DoesNotWriteFile()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs { SavedPath = null };
        var vm = new ServerMonitorViewModel(client, dialogs);
        vm.ConnectAsync().Wait();

        var path = Path.Combine(_tempDir, "cancelled.csv");
        vm.ExportSummary();

        Assert.False(File.Exists(path));
    }

    // ===================== Вспомогательное =====================

    /// <summary>Fake-клиент rac: один кластер, одна база, один сеанс, один процесс.</summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly Guid ClusterId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(new[]
            {
                new RacCluster { Id = ClusterId, Name = "Кластер 1", Port = 1540 }
            });

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<RacClusterInfo?>(new RacClusterInfo { Name = "Кластер 1" });

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacProcessInfo>>(new[]
            {
                new RacProcessInfo { Id = Guid.NewGuid(), Pid = 1234, Type = "rphost" }
            });

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(new[]
            {
                new RacSessionInfo { Id = Guid.NewGuid(), User = "Иванов", Host = "pc1", State = "active" }
            });

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(new[]
            {
                new RacInfobaseSummary { InfobaseId = Guid.NewGuid(), Name = "Рабочая" }
            });

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId, Guid? processId = null,
            string? infobaseUser = null, string? infobasePassword = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId, Guid? processId = null,
            string? infobaseUser = null, string? infobasePassword = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    /// <summary>Диалоги-рекордер с диалогом сохранения, возвращающим заданный путь.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public string? SavedPath { get; set; }
        public int SaveDialogs { get; private set; }

        public void ShowInfo(string message, string title = "")
        {
        }

        public void ShowWarning(string message, string title = "")
        {
        }

        public void ShowError(string message, string title = "")
        {
        }

        public bool Confirm(string message, string title = "") => true;

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null)
        {
            SaveDialogs++;
            return SavedPath;
        }

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}
