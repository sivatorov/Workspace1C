using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #90 (репорт 7OH «RAC-команды продолжают сыпаться в лог после закрытия окна
/// „Серверы 1С"»): (а) выключение автообновления отменяет выполняющийся цикл загрузки
/// вкладок (rac-команды получают отмену); (б) после Dispose() ни один rac-метод не
/// вызывается (Refresh/LoadClusterDataAsync — no-op); (в) повторное включение
/// автообновления после Dispose — no-op (таймер не возрождается).
/// </summary>
public sealed class Issue90RacCancelTests
{
    /// <summary>
    /// Fake-клиент rac (образец FakeRacClient из ServerMonitorViewModelTests): данные
    /// возвращаются быстро, но при включённом <see cref="HangOnListCalls"/> list-команды
    /// «зависают» на токене — цикл загрузки можно держать выполняющимся и убедиться,
    /// что отмена доходит до rac-методов.
    /// </summary>
    private sealed class HangingRacClient : IRacClient
    {
        public static readonly Guid ClusterId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private readonly RacCluster _cluster = new()
        {
            Id = ClusterId,
            Name = "Кластер",
            Port = 1540
        };

        /// <summary>Включает «зависание» list-команд до отмены токена.</summary>
        public volatile bool HangOnListCalls;

        /// <summary>Сколько list-команд стартовало.</summary>
        private int _listCallsStarted;

        /// <summary>Сколько list-команд завершилось отменой токена ( rac «убит»).</summary>
        private int _listCallsCanceled;

        /// <summary>Сколько list-команд вернуло данные (не было отмены).</summary>
        private int _listCallsCompleted;

        public int ListCallsStarted => _listCallsStarted;
        public int ListCallsCanceled => _listCallsCanceled;
        public int ListCallsCompleted => _listCallsCompleted;
        public string LastActionError => string.Empty;

        private async Task<IReadOnlyList<T>> ListAsync<T>(CancellationToken ct) where T : class
        {
            Interlocked.Increment(ref _listCallsStarted);
            if (HangOnListCalls)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _listCallsCanceled);
                    throw;
                }
            }
            Interlocked.Increment(ref _listCallsCompleted);
            return Array.Empty<T>();
        }

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(new[] { _cluster });

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            // «Зависающая» команда цикла: cluster info тоже ждёт токен.
            return Task.FromResult<RacClusterInfo?>(null);
        }

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => ListAsync<RacProcessInfo>(cancellationToken);

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => ListAsync<RacSessionInfo>(cancellationToken);

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => ListAsync<RacConnectionInfo>(cancellationToken);

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => ListAsync<RacLockInfo>(cancellationToken);

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => ListAsync<RacInfobaseSummary>(cancellationToken);

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId, Guid? processId = null,
            string? infobaseUser = null, string? infobasePassword = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId, Guid? processId = null,
            string? infobaseUser = null, string? infobasePassword = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        Assert.Fail("Условие не выполнено за отведённое время.");
    }

    [Fact]
    public async Task SetAutoRefreshEnabledFalse_CancelsRunningRefreshCycle()
    {
        var client = new HangingRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs90());
        try
        {
            await vm.ConnectAsync();
            Assert.True(vm.HasConnected);

            // Следующий цикл «зависает» на rac-командах — как в репорте: цикл ещё идёт.
            client.HangOnListCalls = true;
            vm.Refresh();

            await WaitForAsync(() => client.ListCallsStarted > 0);
            Assert.Equal(0, client.ListCallsCanceled);

            // Репорт: «выключена галка автообновления → окно закрыто». Выключение
            // переключателя обязано отменить выполняющийся цикл rac-команд.
            vm.IsAutoRefreshEnabled = false;

            await WaitForAsync(() => client.ListCallsCanceled > 0);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public async Task Dispose_CancelsRunningRefreshCycle_AndBlocksNewOnes()
    {
        var client = new HangingRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs90());
        try
        {
            await vm.ConnectAsync();
            Assert.True(vm.HasConnected);

            client.HangOnListCalls = true;
            vm.Refresh();
            await WaitForAsync(() => client.ListCallsStarted > 0);

            vm.Dispose();

            // Выполняющийся цикл отменён.
            await WaitForAsync(() => client.ListCallsCanceled > 0);

            // После Dispose новые циклы не стартуют: Refresh и LoadClusterDataAsync — no-op.
            var startedBefore = client.ListCallsStarted;
            client.HangOnListCalls = false;
            vm.Refresh();
            await vm.LoadClusterDataAsync(HangingRacClient.ClusterId);
            Assert.Equal(startedBefore, client.ListCallsStarted);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public async Task StartAutoRefreshAfterDispose_IsNoOp()
    {
        var client = new HangingRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs90());
        try
        {
            await vm.ConnectAsync();
            Assert.True(vm.HasConnected);
            vm.IsAutoRefreshEnabled = true;
            Assert.True(vm.AutoRefreshActive);

            vm.Dispose();
            Assert.False(vm.AutoRefreshActive);

            // Гонка из репорта: «Refresh завершился после закрытия окна» вызвал
            // StartAutoRefresh — после Dispose таймер возрождаться не должен.
            vm.IsAutoRefreshEnabled = true;
            Assert.False(vm.AutoRefreshActive);
        }
        finally
        {
            vm.Dispose();
        }
    }
}

/// <summary>Диалоги-заглушка для тестов issue #90 (как RecordingDialogs в ServerMonitorViewModelTests).</summary>
internal sealed class RecordingDialogs90 : IDialogService
{
    public void ShowInfo(string message, string title = "") { }
    public void ShowWarning(string message, string title = "") { }
    public void ShowError(string message, string title = "") { }
    public bool Confirm(string message, string title = "") => false;
    public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
    public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
    public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
}
