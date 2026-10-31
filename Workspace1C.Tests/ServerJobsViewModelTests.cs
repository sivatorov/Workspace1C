using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты диалога «Регламентные задания сервера» (v100112, F1): загрузка списка заданий
/// (rac «job list» через fake-клиент), фильтр по информационной базе, действия
/// «Запустить сейчас» (enable/resume по состоянию), «Перезапустить» (pause → resume),
/// «Приостановить»/«Возобновить», обработки ошибок rac.
/// </summary>
public sealed class ServerJobsViewModelTests
{
    private static readonly Guid ClusterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobId = Guid.Parse("aaaaaaaa-1111-2222-3333-444455556666");
    private static readonly Guid InfobaseId = Guid.Parse("cccccccc-1111-2222-3333-444455556666");

    /// <summary>База без заданий — для проверки фильтра.</summary>
    private static readonly Guid EmptyInfobaseId = Guid.Parse("dddddddd-1111-2222-3333-444455556666");

    [Fact]
    public async Task LoadJobsAsync_FillsRows_WithInfobaseNames()
    {
        var client = new FakeRacClient();
        var vm = CreateVm(client);

        await vm.LoadJobsAsync();

        Assert.Single(vm.Jobs);
        Assert.Single(vm.VisibleJobs);
        Assert.Equal("Обмен данными", vm.Jobs[0].Name);
        Assert.Equal("Рабочая база", vm.Jobs[0].InfobaseName);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task LoadJobsAsync_ClientThrows_ShowsWarning_AndClearsList()
    {
        var client = new FakeRacClient { ThrowOnJobs = true };
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);

        await vm.LoadJobsAsync();

        Assert.Empty(vm.Jobs);
        Assert.Empty(vm.VisibleJobs);
        Assert.NotEmpty(vm.StatusText);
        Assert.Single(dialogs.Warnings);
    }

    [Fact]
    public async Task Filter_ByInfobase_HidesOtherJobs_ClientSide()
    {
        var client = new FakeRacClient();
        var vm = CreateVm(client);
        await vm.LoadJobsAsync();

        // Задание принадлежит Рабочей базе: фильтр по ней показывает его…
        vm.SelectedFilter = vm.Filters.First(f => f.Id == InfobaseId);
        Assert.Single(vm.VisibleJobs);

        // …а фильтр по базе без заданий — скрывает; «Все базы» возвращает всё.
        vm.SelectedFilter = vm.Filters.First(f => f.Id == EmptyInfobaseId);
        Assert.Empty(vm.VisibleJobs);
        Assert.False(vm.HasVisibleJobs);

        vm.SelectedFilter = RacJobFilterRow.All;
        Assert.Single(vm.VisibleJobs);
        Assert.True(vm.HasVisibleJobs);
    }

    [Fact]
    public async Task RunNow_PausedJob_CallsResume_WithCorrectArguments()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        var job = vm.VisibleJobs.Single();
        vm.SelectedJob = job;
        Assert.True(vm.CanRunNow);

        await vm.RunNowAsync();

        Assert.Single(dialogs.Confirms);
        var call = client.JobStateCalls.Single();
        Assert.Equal(RacJobAction.Resume, call.action);
        Assert.Equal(ClusterId, call.clusterId);
        Assert.Equal(JobId, call.jobId);
    }

    [Fact]
    public async Task RunNow_DisabledJob_CallsEnable()
    {
        var client = new FakeRacClient(jobState: "disabled");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        await vm.RunNowAsync();

        Assert.Equal(RacJobAction.Enable, client.JobStateCalls.Single().action);
    }

    [Fact]
    public async Task RunNow_ActiveJob_DoesNothing_AndInforms()
    {
        var client = new FakeRacClient(jobState: "scheduled");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        Assert.False(vm.CanRunNow);

        await vm.RunNowAsync();

        Assert.Empty(dialogs.Confirms);
        Assert.Empty(client.JobStateCalls);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task RunNow_Cancelled_DoesNotCallClient()
    {
        var client = new FakeRacClient(jobState: "paused");
        var dialogs = new RecordingDialogs { ConfirmResult = false };
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        await vm.RunNowAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(client.JobStateCalls);
    }

    [Fact]
    public async Task Restart_RunningJob_CallsPauseThenResume()
    {
        var client = new FakeRacClient(jobState: "running");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        Assert.True(vm.CanRestart);

        await vm.RestartAsync();

        Assert.Equal(
            new[] { RacJobAction.Pause, RacJobAction.Resume },
            client.JobStateCalls.Select(c => c.action).ToArray());
    }

    [Fact]
    public async Task Restart_DisabledJob_CallsEnableOnly()
    {
        // Снятое с расписания: «Перезапустить» возвращает его на расписание («job enable»);
        // resume не нужен — задание не приостановлено.
        var client = new FakeRacClient(jobState: "disabled");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        await vm.RestartAsync();

        Assert.Equal(
            new[] { RacJobAction.Enable },
            client.JobStateCalls.Select(c => c.action).ToArray());
    }

    [Fact]
    public async Task Restart_PausedJob_CallsResumeOnly()
    {
        var client = new FakeRacClient(jobState: "paused");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        await vm.RestartAsync();

        Assert.Equal(new[] { RacJobAction.Resume }, client.JobStateCalls.Select(c => c.action).ToArray());
    }

    [Fact]
    public async Task ChangeState_Pause_CallsClient_AndReloads()
    {
        var client = new FakeRacClient(jobState: "running");
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        var callsBefore = client.JobCalls;
        vm.SelectedJob = vm.VisibleJobs.Single();
        Assert.True(vm.CanPause);

        await vm.ChangeStateAsync(RacJobAction.Pause);

        Assert.Equal(RacJobAction.Pause, client.JobStateCalls.Single().action);
        // После действия список перечитан (rac «job list» повторно).
        Assert.Equal(callsBefore + 1, client.JobCalls);
    }

    [Fact]
    public async Task Action_ClientReturnsFalse_ShowsWarning()
    {
        var client = new FakeRacClient { ActionFails = true };
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        vm.SelectedJob = vm.VisibleJobs.Single();
        await vm.ChangeStateAsync(RacJobAction.Pause);

        Assert.Single(client.JobStateCalls);
        Assert.Single(dialogs.Warnings);
    }

    [Fact]
    public async Task Actions_WithoutSelection_DoNothing()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = CreateVm(client, dialogs);
        await vm.LoadJobsAsync();

        await vm.RunNowAsync();
        await vm.RestartAsync();
        await vm.ChangeStateAsync(RacJobAction.Pause);

        Assert.Empty(dialogs.Confirms);
        Assert.Empty(client.JobStateCalls);
    }

    [Fact]
    public void Defaults_FiltersContainAllEntry_AndNoSelection()
    {
        var vm = CreateVm(new FakeRacClient());

        Assert.NotNull(vm.RefreshCommand);
        Assert.NotNull(vm.RunNowCommand);
        Assert.NotNull(vm.RestartCommand);
        // RacJobFilterRow.All создаёт новый экземпляр — сравниваем по значению.
        Assert.Null(vm.Filters[0].Id);
        Assert.Equal(RacJobFilterRow.All.DisplayText, vm.Filters[0].DisplayText);
        Assert.Null(vm.SelectedJob);
        Assert.False(vm.IsBusy);
        Assert.False(vm.CanRunNow);
        Assert.False(vm.CanRestart);
        Assert.False(vm.CanPause);
        Assert.False(vm.CanResume);
        // До первой загрузки заданий список пуст.
        Assert.False(vm.HasVisibleJobs);
    }

    // ===================== Вспомогательное =====================

    private static ServerJobsViewModel CreateVm(FakeRacClient client, RecordingDialogs? dialogs = null)
    {
        var infobases = new List<RacInfobaseRow>
        {
            new(new RacInfobaseSummary { InfobaseId = InfobaseId, Name = "Рабочая база" }),
            new(new RacInfobaseSummary { InfobaseId = EmptyInfobaseId, Name = "Пустая база" })
        };
        return new ServerJobsViewModel(
            client, dialogs ?? new RecordingDialogs(),
            new RacConnectionParams { Address = "localhost", Port = IRacClient.DefaultPort },
            ClusterId, infobases);
    }

    /// <summary>Fake-клиент rac: одно задание («Обмен данными», состояние настраивается).</summary>
    private sealed class FakeRacClient : IRacClient
    {
        public bool ThrowOnJobs { get; set; }
        public bool ActionFails { get; set; }
        public string JobState { get; set; } = "paused";

        public FakeRacClient(string jobState = "paused")
        {
            JobState = jobState;
        }

        public string LastActionError { get; private set; } = string.Empty;

        public int JobCalls { get; private set; }

        public List<(Guid clusterId, Guid jobId, RacJobAction action)> JobStateCalls { get; } = new();

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacCluster>>(Array.Empty<RacCluster>());

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<RacClusterInfo?>(null);

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacProcessInfo>>(Array.Empty<RacProcessInfo>());

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

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
            => Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(Array.Empty<RacInfobaseSummary>());

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            JobCalls++;
            if (ThrowOnJobs)
                throw new RacClientException("rac не найден");
            RacJobInfo job = new()
            {
                Id = JobId,
                InfobaseId = InfobaseId,
                Name = "Обмен данными",
                MethodName = "Common.ExchangeJob",
                State = JobState,
                Schedule = "* * * * *"
            };
            return Task.FromResult<IReadOnlyList<RacJobInfo>>(new[] { job });
        }

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default)
        {
            JobStateCalls.Add((clusterId, jobId, action));
            if (ActionFails)
            {
                LastActionError = "Ошибка rac";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

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

    /// <summary>Диалоги-рекордер (как в ServerMonitorViewModelTests).</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public List<string> Confirms { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool ConfirmResult { get; set; } = true;

        public void ShowInfo(string message, string title = "")
        {
        }

        public void ShowWarning(string message, string title = "") => Warnings.Add(message);

        public void ShowError(string message, string title = "")
        {
        }

        public bool Confirm(string message, string title = "")
        {
            Confirms.Add(message);
            return ConfirmResult;
        }

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}
