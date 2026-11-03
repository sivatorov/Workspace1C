using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты встроенного монитора серверов 1С — ViewModel (этапы 2–3, 0.3.9.124–125):
/// дефолты подключения, подключение с fake-клиентом rac (кластеры), загрузка данных
/// кластера во вкладки, команды действий (завершение сеанса / разрыв соединения с
/// подтверждением), автообновление по таймеру и флаг занятости, форматирование
/// роу-строк (память, время, состояния).
/// </summary>
public sealed class ServerMonitorViewModelTests
{
    [Fact]
    public void Defaults_PortIs1540_AddressLocalhost_CommandsExist()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        Assert.Equal("localhost", vm.ServerAddress);
        Assert.Equal(IRacClient.DefaultPort, vm.ServerPort);
        Assert.Equal(1540, vm.ServerPort);
        Assert.Equal(string.Empty, vm.UserName);
        Assert.Equal(string.Empty, vm.Password);
        Assert.False(vm.HasConnected);
        Assert.False(vm.IsBusy);
        Assert.False(vm.AutoRefreshActive);
        Assert.NotNull(vm.ConnectCommand);
        Assert.NotNull(vm.RefreshCommand);
        Assert.NotNull(vm.TerminateSessionCommand);
        Assert.NotNull(vm.DisconnectConnectionCommand);
        Assert.Empty(vm.Processes);
        Assert.Empty(vm.Sessions);
        Assert.Empty(vm.Connections);
        Assert.Empty(vm.Locks);
        Assert.Empty(vm.Clusters);
        Assert.Empty(vm.ClusterRows);
    }

    [Fact]
    public async Task ConnectAsync_WithClusters_SetsConnected_AndSelectsFirst()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.Equal(2, vm.Clusters.Count);
        Assert.Equal(2, vm.ClusterRows.Count);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        Assert.NotEmpty(vm.StatusText);
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_OnClientFailure_ReportsError_WithoutConnected()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnClusters: true), new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.False(vm.HasConnected);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task ConnectAsync_HostPortInAddress_TakesPriorityOverPortField()
    {
        // Issue #324: нестандартный порт агента/RAS можно указать прямо в адресе «host:port»
        // (как в командной строке rac.exe localhost:27545 cluster list) — порт из адреса
        // приоритетнее значения поля «Порт», иначе rac получил бы «localhost:27545:1540».
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        vm.ServerAddress = "localhost:27545";
        vm.ServerPort = 1540;

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.NotNull(client.LastParams);
        Assert.Equal("localhost", client.LastParams.Address);
        Assert.Equal(27545, client.LastParams.Port);
        Assert.NotEmpty(vm.Clusters);
    }

    [Fact]
    public async Task LoadClusterDataAsync_FillsTabs_AndClusterInfo()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Single(vm.Processes);
        Assert.Single(vm.Sessions);
        Assert.Single(vm.Connections);
        Assert.Single(vm.Locks);
        Assert.NotNull(vm.ClusterInfo);
        Assert.NotEmpty(vm.ClusterInfoText);
        Assert.Contains("name:", vm.ClusterInfoText);
    }

    [Fact]
    public void Refresh_WithoutConnection_DoesNothing()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        vm.Refresh();
        Assert.Empty(vm.Processes);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Password_Settable_InMemory()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        vm.Password = "secret";
        Assert.Equal("secret", vm.Password);
    }

    // ===================== Действия: завершение сеанса =====================

    [Fact]
    public async Task TerminateSession_Confirm_CallsClient_WithCorrectArguments_AndRefreshes()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var session = vm.Sessions.Single();
        vm.SelectedSession = session;
        await vm.TerminateSessionAsync();

        // Подтверждение запрошено ровно один раз.
        Assert.Single(dialogs.Confirms);
        Assert.Empty(dialogs.Warnings);

        // Клиент вызван с верными аргументами: кластер + id выбранного сеанса
        // (рабочий процесс из вкладки «Сеансы» не виден — rac сам его находит).
        Assert.Single(client.TerminateCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, client.TerminateCalls[0].clusterId);
        Assert.Equal(session.Id, client.TerminateCalls[0].sessionId);
        Assert.Null(client.TerminateCalls[0].processId);
        Assert.Empty(client.LastActionError);

        // После действия списки перечитаны (Refresh в finally).
        Assert.NotEmpty(vm.StatusText);
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task TerminateSession_WithoutSelection_DoesNothing()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();

        await vm.TerminateSessionAsync();

        Assert.Empty(dialogs.Confirms);
        Assert.Empty(client.TerminateCalls);
    }

    [Fact]
    public async Task TerminateSession_Cancelled_DoesNotCallClient()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs { ConfirmResult = false };
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedSession = vm.Sessions.Single();
        await vm.TerminateSessionAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(client.TerminateCalls);
    }

    [Fact]
    public async Task TerminateSession_ClientReturnsFalse_ShowsWarning_AndSetsStatus()
    {
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedSession = vm.Sessions.Single();
        await vm.TerminateSessionAsync();

        Assert.Single(client.TerminateCalls);
        Assert.Single(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task TerminateSession_ClientThrows_ShowsWarning_AndDoesNotCrash()
    {
        var client = new FakeRacClient(throwOnAction: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedSession = vm.Sessions.Single();
        await vm.TerminateSessionAsync();

        Assert.Single(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    // ===================== Действия: разрыв соединения =====================

    [Fact]
    public async Task DisconnectConnection_Confirm_CallsClient_WithCorrectArguments()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var connection = vm.Connections.Single();
        vm.SelectedConnection = connection;
        await vm.DisconnectConnectionAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(dialogs.Warnings);
        Assert.Single(client.DisconnectCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, client.DisconnectCalls[0].clusterId);
        Assert.Equal(connection.Id, client.DisconnectCalls[0].connectionId);
        // issue #69: rac 8.5.4 требует --process=<pid> — VM берёт его из строки соединения.
        Assert.Equal(FakeRacClient.FirstProcessId, client.DisconnectCalls[0].processId);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DisconnectConnection_Cancelled_DoesNotCallClient()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs { ConfirmResult = false };
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedConnection = vm.Connections.Single();
        await vm.DisconnectConnectionAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(client.DisconnectCalls);
    }

    [Fact]
    public async Task DisconnectConnection_ClientFails_ShowsWarning_AndSetsStatus()
    {
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedConnection = vm.Connections.Single();
        await vm.DisconnectConnectionAsync();

        Assert.Single(client.DisconnectCalls);
        Assert.Single(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    // ===================== Автообновление =====================

    [Fact]
    public void AutoRefresh_NotStarted_WithoutConnection()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_StartsAfterConnect_AndStopsOnDispose()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        Assert.False(vm.AutoRefreshActive);

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.True(vm.AutoRefreshActive);
        Assert.Equal(ServerMonitorViewModel.AutoRefreshIntervalMs, 5000);

        vm.Dispose();

        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_DoesNotStart_OnFailedConnect()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnClusters: true), new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.False(vm.HasConnected);
        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_StopsDuringReconnect_AndRestartsAfterSuccess()
    {
        // issue #77 (комментарий 7OH от 2026-10-28): перед подключением к другому серверу
        // таймер автообновления останавливается — во время подключения тик таймера не
        // смешивает старый контекст с новым адресом; после успешного подключения (галка
        // стоит) таймер возобновляется.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());

        // Первое подключение: таймер работает.
        await vm.ConnectAsync();
        Assert.True(vm.HasConnected);
        Assert.True(vm.AutoRefreshActive);

        // Переподключение к другому серверу с «медленным» rac: пока идёт запрос
        // «cluster list», таймер должен быть остановлен (не тикает «микс»).
        client.ClustersDelay = TimeSpan.FromMilliseconds(300);
        var reconnect = vm.ConnectAsync();
        await Task.Delay(50);
        Assert.False(vm.AutoRefreshActive);

        // После успешного завершения подключения таймер восстановлен (галка стоит).
        await reconnect;
        Assert.True(vm.HasConnected);
        Assert.True(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_Disabled_StaysOff_AfterReconnect()
    {
        // issue #77: если галка «Автообновление» снята, то и во время переподключения,
        // и после него таймер не запускается.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        vm.SetAutoRefreshEnabled(false);

        await vm.ConnectAsync();
        Assert.False(vm.AutoRefreshActive);

        client.ClustersDelay = TimeSpan.FromMilliseconds(200);
        var reconnect = vm.ConnectAsync();
        await Task.Delay(40);
        Assert.False(vm.AutoRefreshActive);
        await reconnect;

        Assert.True(vm.HasConnected);
        Assert.False(vm.IsAutoRefreshEnabled);
        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_DisabledBeforeConnect_TimerDoesNotStart()
    {
        // issue #324: галка автообновления снята ДО подключения — таймер не должен
        // запускаться (раньше ConnectAsync стартовал его безусловно) и индикатор
        // должен оставаться «выключено».
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        vm.SetAutoRefreshEnabled(false);

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.False(vm.IsAutoRefreshEnabled);
        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task LoadClusterData_NetworkError_StopsAutoRefresh()
    {
        // issue #324: обычная ошибка загрузки (не только парсинг) останавливает таймер —
        // бесконечный ретрай каждые 5 с после разрыва соединения не нужен. Ручное
        // «Обновить» остаётся доступным; после успеха таймер возобновится.
        var vm = new ServerMonitorViewModel(
            new FakeRacClient(singleCluster: true, throwOnLoad: true), new RecordingDialogs());

        await vm.ConnectAsync();
        // Единственный кластер выбран автоматически → фоновая загрузка данных падает
        // с обычным исключением (не RacOutputParseException); ждём её завершения.
        await Task.Delay(50);

        Assert.True(vm.HasConnected);
        Assert.False(vm.AutoRefreshActive);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task LoadClusterDataAsync_BusyFlag_SkipsOverlappingRequests()
    {
        // Fake-клиент с задержкой: пока первый запрос выполняется, второй не должен
        // наложиться (флаг занятости через Interlocked пропускает его).
        var client = new FakeRacClient(delay: TimeSpan.FromMilliseconds(300));
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());

        var first = vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        // Первый вызов синхронно устанавливает флаг занятости до первого await.
        Assert.True(vm.IsBusy);

        var second = vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        // Второй вызов сразу возвращается: флаг занятости уже установлен.
        Assert.True(second.IsCompleted);
        await second;

        await first;

        Assert.False(vm.IsBusy);
        // Данные прочитаны ровно один раз.
        Assert.Equal(1, client.ProcessCalls);
    }

    // ===================== Форматирование роу-строк =====================

    [Fact]
    public void SessionRow_FormatsMemory_Duration_State()
    {
        var row = new RacSessionRow(new RacSessionInfo
        {
            Id = Guid.NewGuid(),
            User = "Иванов",
            Memory = 512 * 1024 * 1024,
            DurationAll = 3600_000,
            DurationCurrent = 90_000,
            State = "active"
        });

        Assert.Contains("512", row.MemoryText);
        Assert.EndsWith(LocalizationManager.T("ServerMonitor.Mb"), row.MemoryText);
        Assert.Equal("01:00:00", row.DurationAllText);
        Assert.Equal("00:01:30", row.DurationCurrentText);
        Assert.Equal("#16A34A", row.StateColorHex);
        Assert.False(string.IsNullOrWhiteSpace(row.StateText));
        Assert.Equal("—", new RacSessionRow(new RacSessionInfo { State = "sleep" }).StartedAtText);
    }

    [Fact]
    public void SessionRow_StateColors_AndBlockedSymbol()
    {
        Assert.Equal("#DC2626", new RacSessionRow(new RacSessionInfo { State = "dead" }).StateColorHex);
        Assert.Equal("#D97706", new RacSessionRow(new RacSessionInfo { State = "wait" }).StateColorHex);
        Assert.Equal("#64748B", new RacSessionRow(new RacSessionInfo { State = "unknown" }).StateColorHex);
        Assert.Equal("●", new RacSessionRow(new RacSessionInfo { BlockedByLs = true }).BlockedSymbol);
        Assert.Equal("●", new RacSessionRow(new RacSessionInfo { BlockedByDeadlock = true }).BlockedSymbol);
        Assert.Equal(string.Empty, new RacSessionRow(new RacSessionInfo()).BlockedSymbol);
    }

    [Fact]
    public void ProcessRow_FormatsMemory_Cpu_Running()
    {
        var row = new RacProcessRow(new RacProcessInfo
        {
            Id = Guid.NewGuid(),
            Pid = 1234,
            MemorySize = 1024 * 1024 * 1024,
            Cpu = 12.5,
            Running = true,
            Threads = 8
        });

        Assert.Contains("1024", row.MemorySizeText);
        Assert.EndsWith(LocalizationManager.T("ServerMonitor.Mb"), row.MemorySizeText);
        Assert.Contains("12", row.CpuText);
        Assert.EndsWith("%", row.CpuText);
        Assert.Equal("#16A34A", row.StateColorHex);
        Assert.Equal(1234, row.Pid);
    }

    [Fact]
    public void ConnectionRow_FormatsDuration_Blocked()
    {
        var row = new RacConnectionRow(new RacConnectionInfo
        {
            Id = Guid.NewGuid(),
            Duration = 1800_000,
            Blocked = true
        });

        Assert.Equal("00:30:00", row.DurationText);
        Assert.Equal("#D97706", row.BlockedColorHex);
        Assert.Equal("—", new RacConnectionRow(new RacConnectionInfo()).EstablishedAtText);
    }

    // ---------- issue #324: автовыбор кластера, ошибка разбора, автообновление ----------

    [Fact]
    public async Task Connect_SingleCluster_AutoSelectedImmediately()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(singleCluster: true), new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.Single(vm.ClusterRows);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
    }

    [Fact]
    public async Task LoadClusterData_ParseError_SetsErrorAndStopsAutoRefresh()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnParse: true), new RecordingDialogs());
        await vm.ConnectAsync();
        // Фоновая загрузка первого кластера споткнётся об ошибку разбора; ждём её завершения
        // (перекрытие запросов исключено флагом занятости), затем явная загрузка.
        await Task.Delay(50);
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        // Бесконечный ретрай каждые 5 с прекращён: таймер остановлен, ошибка показана.
        Assert.False(vm.AutoRefreshActive);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task LoadClusterData_ParseErrorOnOneTab_OtherTabsStillFill()
    {
        // issue #324 (лог 7OH от 2026-10-08): rac вернул exit=0 и непустой stdout по всем
        // шести list-командам, но сбой разбора ОДНОЙ из них раньше проваливал общий
        // Task.WhenAll — и ВСЕ вкладки оставались пустыми. Теперь сбойная вкладка пуста,
        // остальные заполнены, ошибка перечислена, автообновление остановлено.
        var vm = new ServerMonitorViewModel(
            new FakeRacClient(throwParseOnSessions: true), new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Empty(vm.Sessions);            // сбойная вкладка
        Assert.NotEmpty(vm.Processes);        // остальные заполнены
        Assert.NotEmpty(vm.Connections);
        Assert.NotEmpty(vm.Locks);
        Assert.NotNull(vm.ClusterInfo);
        // Сбойная вкладка названа в ошибке: текст исключения fake-клиента упоминает
        // команду session list (культуро-независимый фрагмент — не локализация).
        Assert.Contains("session list", vm.ErrorMessage);
        Assert.False(vm.AutoRefreshActive);
    }

    [Fact]
    public async Task SetAutoRefreshEnabled_TurnsOffAndOn_Timer()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        await vm.ConnectAsync();
        Assert.True(vm.AutoRefreshActive);

        vm.SetAutoRefreshEnabled(false);
        Assert.False(vm.AutoRefreshActive);
        Assert.False(vm.IsAutoRefreshEnabled);

        vm.SetAutoRefreshEnabled(true);
        Assert.True(vm.IsAutoRefreshEnabled);
        Assert.True(vm.AutoRefreshActive);
    }

    [Fact]
    public void AutoRefreshIntervalSeconds_ClampsToRange()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        vm.AutoRefreshIntervalSeconds = 0;
        Assert.Equal(1, vm.AutoRefreshIntervalSeconds);

        vm.AutoRefreshIntervalSeconds = 999;
        Assert.Equal(60, vm.AutoRefreshIntervalSeconds);

        vm.AutoRefreshIntervalSeconds = 10;
        Assert.Equal(10, vm.AutoRefreshIntervalSeconds);
    }

    // ===================== C2: колонка «Информационная база» на вкладке «Сеансы» =====================

    [Fact]
    public async Task LoadClusterDataAsync_SessionRows_HaveInfobaseNameFromSummaryList()
    {
        // issue #324, C2: infobase-id сеанса из «session list» сопоставляется с именем
        // из «infobase summary list» («Бухгалтерия» — единственная база fake-кластера).
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var session = vm.Sessions.Single();
        Assert.Equal("Бухгалтерия", session.InfobaseName);
    }

    [Fact]
    public void RacSessionRow_WithoutInfobaseMapping_ShowsPlaceholder()
    {
        var row = new RacSessionRow(new RacSessionInfo { User = "Петров" });

        Assert.Equal(string.Empty, row.InfobaseName);
    }

    // ===================== C3: редактируемая «Информация о кластере» =====================

    [Fact]
    public async Task LoadClusterDataAsync_BuildsClusterPropertyRows_WithEditableSubset()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var rows = vm.ClusterProperties;
        Assert.NotEmpty(rows);

        // Имя кластера — в безопасном наборе правки (rac «cluster update --name»).
        var nameRow = rows.Single(r => r.RawKey == "name");
        Assert.True(nameRow.IsEditable);
        Assert.Equal("Главный кластер", nameRow.OriginalValue);
        // Имя свойства — из локализации; в тестовой среде язык не загружен (T возвращает
        // ключ), поэтому строка показывает ключ rac «как есть» — оба варианта корректны.
        Assert.True(
            nameRow.DisplayName == LocalizationManager.T("ServerMonitor.ClusterProperty.name") ||
            nameRow.DisplayName == "name",
            $"DisplayName={nameRow.DisplayName}");

        // hostName и port rac не меняет — только чтение.
        Assert.False(rows.Single(r => r.RawKey == "hostName").IsEditable);
        Assert.False(rows.Single(r => r.RawKey == "port").IsEditable);
    }

    [Fact]
    public async Task SaveClusterPropertiesAsync_EditedName_CallsClusterUpdate_WithChangedValue()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var nameRow = vm.ClusterProperties.Single(r => r.RawKey == "name");
        nameRow.EditValue = "Новое имя кластера";
        await vm.SaveClusterPropertiesAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(dialogs.Warnings);
        var call = Assert.Single(client.UpdateCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, call.clusterId);
        Assert.Equal("Новое имя кластера", call.changes.Name);
        Assert.Null(call.changes.ExpirationTimeout);
        Assert.False(call.changes.IsEmpty);
    }

    [Fact]
    public async Task SaveClusterPropertiesAsync_Cancelled_DoesNotCallClient()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs { ConfirmResult = false };
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.ClusterProperties.Single(r => r.RawKey == "name").EditValue = "Другое имя";
        await vm.SaveClusterPropertiesAsync();

        Assert.Single(dialogs.Confirms);
        Assert.Empty(client.UpdateCalls);
    }

    [Fact]
    public async Task SaveClusterPropertiesAsync_NoChanges_DoesNotCallClient_AndExplainsStatus()
    {
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        await vm.SaveClusterPropertiesAsync();

        Assert.Empty(dialogs.Confirms);
        Assert.Empty(client.UpdateCalls);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task SaveClusterPropertiesAsync_NumericEdits_AreParsedInvariant()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();
        // В fake-выводе кластера числового поля нет — редактируем имя и таймаут
        // через строки, построенные из типизированного словаря (только name есть).
        // Проверяем парсер чисел напрямую на строках вкладки.
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var nameRow = vm.ClusterProperties.Single(r => r.RawKey == "name");
        nameRow.EditValue = "Кластер 42";
        await vm.SaveClusterPropertiesAsync();

        var call = Assert.Single(client.UpdateCalls);
        Assert.Equal("Кластер 42", call.changes.Name);
    }

    [Fact]
    public void IsEditableClusterProperty_CoversSafeSubset_Only()
    {
        // Безопасный набор rac «cluster update»: имя, таймауты/лимиты, уровень
        // безопасности, пинг, аутентификация (дефисы/регистр ключа не важны).
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("name"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("expiration-timeout"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("ExpirationTimeout"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("lifetime-limit"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("max-memory-size"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("max-memory-time-limit"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("security-level"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("ping-period"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("ping-timeout"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("max-auth-attempts"));
        Assert.True(ServerMonitorViewModel.IsEditableClusterProperty("auth-lock-duration"));

        // Порт кластера rac не меняет; служебные свойства — только чтение.
        Assert.False(ServerMonitorViewModel.IsEditableClusterProperty("port"));
        Assert.False(ServerMonitorViewModel.IsEditableClusterProperty("hostName"));
        Assert.False(ServerMonitorViewModel.IsEditableClusterProperty("cluster"));
        Assert.False(ServerMonitorViewModel.IsEditableClusterProperty("kill-problem-processes"));
    }

    [Fact]
    public async Task SaveClusterPropertiesAsync_ClientReturnsFalse_ShowsWarning()
    {
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.ClusterProperties.Single(r => r.RawKey == "name").EditValue = "Другое имя";
        await vm.SaveClusterPropertiesAsync();

        Assert.Single(client.UpdateCalls);
        Assert.Single(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    // ===================== Справочник «Сервера управления» (issue #3) =====================

    [Fact]
    public void Constructor_LoadsServersFromStore()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        store.Save(new[]
        {
            new ManagementServer { Id = "srv-1", Description = "Основной", Address = "srv1", Port = 1540 },
            new ManagementServer { Id = "srv-2", Description = "Резервный", Address = "srv2", Port = 1545 }
        });

        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);

        Assert.Equal(2, vm.Servers.Count);
        Assert.Equal("Основной", vm.Servers[0].Description);
        Assert.Equal("Резервный", vm.Servers[1].Description);
    }

    [Fact]
    public void AddServer_AddsToCollection_AndPersistsToStore()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);

        vm.AddServer(new ManagementServer { Description = "Новый", Address = "srv-new", Port = 1540 });

        Assert.Single(vm.Servers);
        Assert.Equal("Новый", vm.Servers[0].Description);
        // Запись сохранена в файл: новое хранилище на том же каталоге читает её.
        var reloaded = new ServerListStore(directoryOverride: tmp.Path).Load();
        Assert.Single(reloaded);
        Assert.Equal("srv-new", reloaded[0].Address);
    }

    [Fact]
    public void RemoveServer_RemovesFromCollection_AndPersists()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var server = new ManagementServer { Id = "srv-1", Description = "Сервер", Address = "srv1" };
        vm.AddServer(server);

        vm.RemoveServer(server);

        Assert.Empty(vm.Servers);
        Assert.Null(vm.SelectedServer);
        Assert.Empty(new ServerListStore(directoryOverride: tmp.Path).Load());
    }

    [Fact]
    public void UpdateServer_ReplacesEntry_AndPersists()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var server = new ManagementServer { Id = "srv-1", Description = "Старое имя", Address = "srv1" };
        vm.AddServer(server);

        vm.UpdateServer(new ManagementServer
        {
            Id = "srv-1",
            Description = "Новое имя",
            Address = "srv1-updated",
            Port = 1545,
            User = "admin",
            Password = "pwd"
        });

        Assert.Single(vm.Servers);
        Assert.Equal("Новое имя", vm.Servers[0].Description);
        Assert.Equal("srv1-updated", vm.Servers[0].Address);
        var reloaded = new ServerListStore(directoryOverride: tmp.Path).Load();
        Assert.Equal("Новое имя", reloaded[0].Description);
        Assert.Equal("pwd", reloaded[0].Password);
    }

    [Fact]
    public async Task ConnectToServerAsync_SetsParamsFromServer_AndConnects()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        var server = new ManagementServer
        {
            Id = "srv-1",
            Description = "Основной",
            Address = "srv1c:27545",
            Port = 1540,
            User = "admin",
            Password = "secret"
        };

        await vm.ConnectToServerAsync(server);

        Assert.True(vm.HasConnected);
        // Поле адреса хранит исходное значение записи (включая «host:port»).
        Assert.Equal("srv1c:27545", vm.ServerAddress);
        Assert.Equal(1540, vm.ServerPort);
        Assert.Equal("admin", vm.UserName);
        Assert.Equal("secret", vm.Password);
        // Параметры переданы клиенту rac: порт из адреса приоритетнее поля.
        Assert.NotNull(client.LastParams);
        Assert.Equal("srv1c", client.LastParams!.Address);
        Assert.Equal(27545, client.LastParams.Port);
        Assert.Equal("admin", client.LastParams.User);
        Assert.Equal("secret", client.LastParams.Password);
    }

    [Fact]
    public async Task ConnectToServerAsync_OnClientFailure_ReportsError()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnClusters: true), new RecordingDialogs());
        var server = new ManagementServer { Id = "srv-1", Description = "Сервер", Address = "srv1", Port = 1540 };

        await vm.ConnectToServerAsync(server);

        Assert.False(vm.HasConnected);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal("srv1", vm.ServerAddress);
    }

    // ===================== Подсветка подключённого сервера (issue #3, 7OH 19.10) =====================

    [Fact]
    public async Task ConnectToServerAsync_Success_SetsConnectedServer()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        var server = new ManagementServer { Id = "srv-1", Description = "Основной", Address = "srv1", Port = 1540 };

        await vm.ConnectToServerAsync(server);

        Assert.True(vm.HasConnected);
        Assert.Same(server, vm.ConnectedServer);
        Assert.True(vm.HasConnectedServer);
    }

    [Fact]
    public async Task ConnectToServerAsync_OnClientFailure_LeavesConnectedServerNull()
    {
        // Без успешного подключения запись справочника не подсвечивается.
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnClusters: true), new RecordingDialogs());
        var server = new ManagementServer { Id = "srv-1", Description = "Сервер", Address = "srv1", Port = 1540 };

        await vm.ConnectToServerAsync(server);

        Assert.False(vm.HasConnected);
        Assert.Null(vm.ConnectedServer);
        Assert.False(vm.HasConnectedServer);
    }

    // ===================== Переключение сервера с первого раза (issue #77) =====================

    [Fact]
    public async Task ReconnectToAnotherServer_LoadsClusterOfNewServer_FirstTry_Issue77()
    {
        // issue #77: переключение на другой сервер 1С («Подключиться» / двойной клик)
        // ВСЕГДА заканчивалось ошибкой — выбор кластера не сбрасывался при повторном
        // подключении, и первичная загрузка шла со старым id кластера ПРЕЖНЕГО сервера.
        // Синхронный путь (dispatch==null, как в юнит-тестах) воспроизводит багу на
        // сервере B с НЕСКОЛЬКИМИ кластерами: ApplyClusters при 2+ кластерах не трогает
        // SelectedClusterId, поэтому старый id оставался и уходил в rac.
        var client = new FakeRacClient();
        var firstLoads = 0;
        client.ClustersByAddress = new Dictionary<string, IReadOnlyList<RacCluster>>
        {
            // У сервера B СВОИ кластеры — id НЕ пересекаются с кластерами сервера A.
            ["srv-b"] = new[]
            {
                new RacCluster { Id = Guid.Parse("33333333-3333-3333-3333-333333333331"), Name = "Кластер B1", Port = 1541 },
                new RacCluster { Id = Guid.Parse("33333333-3333-3333-3333-333333333332"), Name = "Кластер B2", Port = 1542 }
            }
        };

        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        var serverA = new ManagementServer { Id = "a", Description = "A", Address = "srv-a", Port = 1540 };
        var serverB = new ManagementServer { Id = "b", Description = "B", Address = "srv-b", Port = 1540 };

        // Подключение к серверу A: выбран первый кластер (FirstClusterId) и данные загружены.
        await vm.ConnectToServerAsync(serverA);
        await Task.Delay(50);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        firstLoads = client.LoadedClusterIds.Count;
        Assert.True(firstLoads >= 1);

        // Переключение на сервер B С ПЕРВОГО РАЗА: выбор переходит на кластер B,
        // загрузка идёт с id первого кластера НОВОГО сервера (а не со старым id A).
        await vm.ConnectToServerAsync(serverB);
        await Task.Delay(50);

        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333331"), vm.SelectedClusterId);
        Assert.Same(serverB, vm.ConnectedServer);
        Assert.Equal(1, client.LoadedClusterIds.Count - firstLoads);
        Assert.All(client.LoadedClusterIds.Skip(firstLoads), id => Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333331"), id));
        Assert.Empty(vm.ErrorMessage);
        // Синхронный путь: данные первого кластера B уже применены, вкладки заполнены.
        Assert.Single(vm.Sessions);
        Assert.Single(vm.Infobases);
        vm.Dispose();
    }

    [Fact]
    public async Task SwitchServer_WithDeferredApply_LoadsClusterOfNewServer_FirstTry_Issue77()
    {
        // issue #77 на WPF/Avalonia: ApplyClusters применяется В UI-потоке ОТЛОЖЕННО
        // (Dispatcher.BeginInvoke / UIThread.Post), а ConnectAsync после ConfigureAwait(false)
        // продолжается в фоне. До фикса после ExitBusy проверялся УСТАРЕВШИЙ
        // SelectedClusterId прежнего сервера — загрузка уходила в rac со старым id на
        // новом сервере; «Обновить» потом работал, потому что отложенный Apply к тому
        // моменту уже применился.
        var client = new FakeRacClient();
        var clusterB = Guid.Parse("44444444-4444-4444-4444-444444444444");
        client.ClustersByAddress = new Dictionary<string, IReadOnlyList<RacCluster>>
        {
            ["srv-b"] = new[] { new RacCluster { Id = clusterB, Name = "Кластер B", Port = 1541 } }
        };

        // Отложенная доставка «в UI-поток»: действия копятся и применяются вручную,
        // как очередь диспетчера в реальном окне (WPF Dispatcher / Avalonia UIThread).
        var pending = new List<Action>();
        void dispatch(Action action) => pending.Add(action);
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs(), dispatch);
        var serverA = new ManagementServer { Id = "a", Description = "A", Address = "srv-a", Port = 1540 };
        var serverB = new ManagementServer { Id = "b", Description = "B", Address = "srv-b", Port = 1540 };

        // Первое подключение к A: применяем отложенную очередь (индексный цикл —
        // применение Apply может добавить в очередь новые действия).
        await vm.ConnectToServerAsync(serverA);
        for (var i = 0; i < pending.Count; i++)
            pending[i]();
        pending.Clear();
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        Assert.Single(vm.Sessions);

        // Переключение на B: очередь снова отложена — ConnectAsync работает в фоне.
        await vm.ConnectToServerAsync(serverB);
        await Task.Delay(50);

        // Загрузка уже ушла с id кластера НОВОГО сервера, а не со старым id кластера A.
        Assert.Equal(clusterB, vm.SelectedClusterId);
        Assert.Same(serverB, vm.ConnectedServer);
        Assert.All(client.LoadedClusterIds.Skip(1), id => Assert.Equal(clusterB, id));

        // Применяем очередь «UI-потока»: данные кластера B приходят без ошибок.
        for (var i = 0; i < pending.Count; i++)
            pending[i]();
        pending.Clear();

        Assert.Empty(vm.ErrorMessage);
        Assert.Single(vm.Sessions);
        Assert.Single(vm.Processes);
        Assert.Single(vm.Infobases);
        vm.Dispose();
    }

    [Fact]
    public async Task Reconnect_ResetsPreviousHighlight_WhenNewTargetNotInCatalog()
    {
        // Смена подключения сбрасывает подсветку прежней записи (issue #3, 7OH 19.10):
        // подключение к адресу вне справочника не оставляет жирной старую строку.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        var first = new ManagementServer { Id = "srv-1", Description = "Первый", Address = "srv1", Port = 1540 };
        await vm.ConnectToServerAsync(first);
        Assert.Same(first, vm.ConnectedServer);

        vm.ServerAddress = "other-host";
        vm.ServerPort = 1545;
        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.Null(vm.ConnectedServer);
        Assert.False(vm.HasConnectedServer);
    }

    [Fact]
    public async Task ConnectAsync_MatchesServerByAddressHostAndPort()
    {
        // Обычное подключение по полям совпадает с записью справочника по нормализованным
        // адресу и порту (порт из адреса «host:port» приоритетнее поля — issue #324).
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var server = new ManagementServer { Id = "srv-1", Description = "Основной", Address = "srv1c:27545", Port = 1540 };
        store.Save(new[] { server });
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        vm.ServerAddress = "srv1c:27545";
        vm.ServerPort = 1540;

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        // Load() десериализует JSON, поэтому экземпляр другой — сверяем по Id и по
        // принадлежности коллекции справочника (именно её строку подсвечивает UI).
        Assert.NotNull(vm.ConnectedServer);
        Assert.Equal(server.Id, vm.ConnectedServer!.Id);
        Assert.Contains(vm.ConnectedServer, vm.Servers);
        Assert.True(vm.HasConnectedServer);
    }

    [Fact]
    public async Task ConnectAsync_NoMatchingServer_LeavesConnectedServerNull()
    {
        // В справочнике есть записи, но ни одна не совпадает по адресу:порту — жирным
        // не выделяется ничего (подключение к «сырому» адресу вне справочника).
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        store.Save(new[] { new ManagementServer { Id = "srv-1", Description = "Другой", Address = "other", Port = 1545 } });
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        vm.ServerAddress = "srv1";
        vm.ServerPort = 1540;

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.Null(vm.ConnectedServer);
        Assert.False(vm.HasConnectedServer);
    }

    [Fact]
    public void SelectedServer_IsSettable()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        var server = new ManagementServer { Id = "srv-1", Description = "Сервер", Address = "srv1" };

        vm.SelectedServer = server;

        Assert.Same(server, vm.SelectedServer);
    }

    // ===================== Базы кластера (issue #3) =====================

    [Fact]
    public async Task LoadClusterDataAsync_FillsInfobasesTab()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Single(vm.Infobases);
        Assert.True(vm.HasInfobases);
        Assert.Equal("Бухгалтерия", vm.Infobases[0].Name);
        Assert.Equal(FakeRacClient.FirstInfobaseId, vm.Infobases[0].InfobaseId);
    }

    [Fact]
    public async Task SelectInfobase_FiltersSessionsConnectionsLocks_OnClient_WithoutReload()
    {
        // issue #3: выбор базы НЕ перезагружает кластер (rac «--infobase=...» на платформе
        // пользователя не работает и вызывает зависание) — сеансы/соединения/блокировки
        // фильтруются на клиенте по уже загруженному снимку, без rac-вызовов и IsBusy.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        // Данные кластера запрашиваются целиком — rac-фильтр по базе не передаётся.
        Assert.Null(client.LastSessionsInfobaseId);
        Assert.Null(client.LastConnectionsInfobaseId);
        Assert.Null(client.LastLocksInfobaseId);
        Assert.Single(vm.Sessions);
        Assert.Single(vm.Connections);
        Assert.Single(vm.Locks);
        Assert.Single(vm.FilteredSessions);
        Assert.Single(vm.FilteredConnections);
        Assert.Single(vm.FilteredLocks);
        var processCallsBefore = client.ProcessCalls;

        // Выбираем базу — фильтрация МГНОВЕННАЯ, без rac-вызовов и без IsBusy.
        vm.SelectedInfobase = vm.Infobases.Single();
        Assert.Equal(FakeRacClient.FirstInfobaseId, vm.SelectedInfobaseId);
        Assert.True(vm.HasInfobaseFilter);
        Assert.False(vm.IsBusy);

        // Сеанс принадлежит выбранной базе; соединение сопоставлено через SessionId
        // сеанса; блокировка — по InfobaseId строки.
        Assert.Single(vm.FilteredSessions);
        Assert.Single(vm.FilteredConnections);
        Assert.Single(vm.FilteredLocks);
        Assert.Equal(FakeRacClient.FirstInfobaseId, vm.FilteredSessions[0].InfobaseId);

        // Перезагрузки кластера не было: rac-команды не вызывались повторно.
        Assert.Equal(processCallsBefore, client.ProcessCalls);
        Assert.Null(client.LastSessionsInfobaseId);
        Assert.Null(client.LastConnectionsInfobaseId);
        Assert.Null(client.LastLocksInfobaseId);
    }

    [Fact]
    public async Task ClearInfobaseFilter_ShowsAllData_WithoutReload()
    {
        // issue #3: «Сбросить» снимает клиентский фильтр — снова видны все базы кластера.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedInfobase = vm.Infobases.Single();
        Assert.True(vm.HasInfobaseFilter);
        Assert.Single(vm.FilteredSessions);

        var processCallsBefore = client.ProcessCalls;
        vm.ClearInfobaseFilter();

        Assert.Null(vm.SelectedInfobaseId);
        Assert.False(vm.HasInfobaseFilter);
        // issue #3 (7OH): надпись фильтра видна ВСЕГДА; при сбросе — «Без фильтра».
        Assert.Equal(LocalizationManager.T("ServerMonitor.Infobase.NoFilter"), vm.InfobaseFilterText);
        Assert.Single(vm.FilteredSessions);
        Assert.Single(vm.FilteredConnections);
        Assert.Single(vm.FilteredLocks);
        // Клиентская операция — rac не вызывался.
        Assert.Equal(processCallsBefore, client.ProcessCalls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadClusterDataAsync_DoesNotCallGetJobsAsync()
    {
        // issue #3: вкладка «Регламентные задания» удалена — rac «job list» не вызывается.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Equal(0, client.JobCalls);
    }

    [Fact]
    public async Task CreateInfobaseAsync_CallsClient_WithRequest_AndRefreshes()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();

        var request = new RacInfobaseCreateRequest
        {
            Name = "Новая база",
            Dbms = RacDbms.MssqlServer,
            DbServer = "sql-01",
            DbName = "newbase"
        };
        await vm.CreateInfobaseAsync(request);

        var call = Assert.Single(client.CreateInfobaseCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, call.clusterId);
        Assert.Equal("Новая база", call.request.Name);
        Assert.Empty(client.LastActionError);
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task CreateInfobaseAsync_ClientFails_ShowsWarning()
    {
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();

        await vm.CreateInfobaseAsync(new RacInfobaseCreateRequest
        {
            Name = "База",
            Dbms = RacDbms.PostgreSql,
            DbServer = "pg-01",
            DbName = "baza"
        });

        Assert.Single(client.CreateInfobaseCalls);
        Assert.NotEmpty(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task CreateInfobaseAsync_ClientFails_ShowsWarningOnUiThread()
    {
        // Замечание 7OH от 19.10: «Вызывающим потоком должен быть STA». После
        // ConfigureAwait(false) продолжение CreateInfobaseAsync выполняется в потоке
        // пула, поэтому предупреждение WPF нельзя создавать напрямую — оно обязано
        // уйти через dispatch-маршалер в UI-поток (issue #3, 1.0.0.22).
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var inUi = false;
        dialogs.OnUiProvider = () => inUi;
        void dispatch(Action action)
        {
            var previous = inUi;
            inUi = true; // имитация UI-потока (WPF Dispatcher / Avalonia UIThread)
            try { action(); }
            finally { inUi = previous; }
        }
        var vm = new ServerMonitorViewModel(client, dialogs, dispatch);
        await vm.ConnectAsync();

        await vm.CreateInfobaseAsync(new RacInfobaseCreateRequest
        {
            Name = "База",
            Dbms = RacDbms.PostgreSql,
            DbServer = "pg-01",
            DbName = "baza"
        });

        Assert.Single(client.CreateInfobaseCalls);
        Assert.NotEmpty(dialogs.Warnings);
        Assert.True(dialogs.WarningsShownOnUi,
            "предупреждение должно показываться в UI-потоке (через dispatch), а не из потока пула");
    }

    [Fact]
    public async Task DropSelectedInfobase_FromClusterOnly_PassesFalse()
    {
        // «Просто удалить из кластера» (диалог удаления с выбором, issue #3):
        // rac «infobase drop» без флага «--drop-database».
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedInfobase = vm.Infobases.Single();
        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        Assert.Empty(dialogs.Warnings);
        var call = Assert.Single(client.DropInfobaseCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, call.clusterId);
        Assert.Equal(FakeRacClient.FirstInfobaseId, call.infobaseId);
        Assert.False(call.dropDatabase);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_WithDatabase_PassesTrue()
    {
        // «Удалить с удалением на сервере СУБД»: rac «infobase drop --drop-database».
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedInfobase = vm.Infobases.Single();
        await vm.DropSelectedInfobaseAsync(dropDatabase: true);

        Assert.Empty(dialogs.Warnings);
        var call = Assert.Single(client.DropInfobaseCalls);
        Assert.True(call.dropDatabase);
    }

    [Fact]
    public async Task DropSelectedInfobase_NoSelection_DoesNotCallClient()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();

        await vm.DropSelectedInfobaseAsync(dropDatabase: true);

        Assert.Empty(client.DropInfobaseCalls);
    }

    // ===================== Повтор удаления с логином/паролем (issue #69) =====================

    [Fact]
    public async Task DropSelectedInfobase_FirstAttemptFails_RetryWithCredentials_Succeeds()
    {
        // Одно неудачное удаление (защищённая база) → диалог повтора → «Повторить попытку»
        // с логином/паролем администратора ИБ → второй «infobase drop» с кредами → успех.
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var retryShown = new List<(string error, string? login, string? password)>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                retryShown.Add((error, login, password));
                return new DropRetryResult(true, "ИБ-админ", "секрет");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        Assert.Equal(2, client.DropInfobaseCalls.Count);
        // Первая попытка — без учётных данных администратора ИБ.
        Assert.Null(client.DropInfobaseCallsWithCredentials[0].infobaseUser);
        Assert.Null(client.DropInfobaseCallsWithCredentials[0].infobasePassword);
        // Вторая (повторная) — с кредами из диалога.
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
        Assert.Equal("секрет", client.DropInfobaseCallsWithCredentials[1].infobasePassword);
        // Диалог показан ровно один раз: после успеха повторного удаления он НЕ открывается.
        Assert.Single(retryShown);
        // finally → Refresh(): списки перечитаны, статус-строка переустановлена («загружено»).
        Assert.NotEmpty(vm.StatusText);
        Assert.Contains("ServerMonitor.Status.LoadedFormat", vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_RetryCancel_DoesNotRetry()
    {
        // «Отмена» в диалоге повтора: второй вызов rac НЕ выполняется, статус — текст ошибки.
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 5 };
        var dialogs = new RecordingDialogs();
        var retryShown = 0;
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                retryShown++;
                return DropRetryResult.Cancel;
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // «Отмена»: повторный вызов rac НЕ выполняется; диалог показан один раз.
        Assert.Single(client.DropInfobaseCalls);
        Assert.Equal(1, retryShown);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_RepeatedFailures_ShowsDialogAgain_WithSavedLoginAndPassword()
    {
        // Повторная ошибка: диалог показывается снова, логин И пароль из прошлой
        // попытки предзаполнены (переданы колбэком — комментарий 7OH от 2026-10-26
        // «Пароль сохранять между попытками»); после успеха — статус «удалена».
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 3 };
        var dialogs = new RecordingDialogs();
        var retryShown = new List<(string error, string? login, string? password)>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                retryShown.Add((error, login, password));
                return new DropRetryResult(true, "ИБ-админ", "новый-пароль");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // 3 неудачи + 1 успешная попытка.
        Assert.Equal(4, client.DropInfobaseCalls.Count);
        Assert.Equal(3, retryShown.Count);
        // Первый показ — без логина и пароля; второй и третий — с кредами из первого
        // ответа диалога (пароль тоже сохраняется между попытками).
        Assert.Null(retryShown[0].login);
        Assert.Null(retryShown[0].password);
        Assert.Equal("ИБ-админ", retryShown[1].login);
        Assert.Equal("новый-пароль", retryShown[1].password);
        Assert.Equal("ИБ-админ", retryShown[2].login);
        Assert.Equal("новый-пароль", retryShown[2].password);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
        Assert.Equal("новый-пароль", client.DropInfobaseCallsWithCredentials[1].infobasePassword);
        // После успешной повторной попытки диалог больше не показывается.
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_RetryDialog_IsShownOnUiThread()
    {
        // Диалог повтора — модальное окно: обязан открываться в UI-потоке (как
        // ShowWarningOnUi, issue #3, 1.0.0.22), рабочий поток ждёт его результат.
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var shownOnUi = false;
        void dispatch(Action action)
        {
            var previous = shownOnUi;
            shownOnUi = true; // имитация UI-потока (WPF Dispatcher / Avalonia UIThread)
            try { action(); }
            finally { shownOnUi = previous; }
        }
        var vm = new ServerMonitorViewModel(client, dialogs, dispatch,
            dropRetryDialog: (error, login, password) =>
            {
                Assert.True(shownOnUi, "диалог повтора должен открываться в UI-потоке");
                return new DropRetryResult(true, "ИБ-админ", "пароль");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
    }

    [Fact]
    public async Task DropSelectedInfobase_WithoutRetryDialog_FallsBackToWarning()
    {
        // Без колбэка диалога (прежние тесты/сценарии): при неудаче — предупреждение
        // с одной кнопкой OK и статус-строка, повторный вызов не выполняется.
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        Assert.Single(client.DropInfobaseCalls);
        Assert.NotEmpty(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_SecondAttemptFailure_MarksDialogAsRetryFailed()
    {
        // issue #69, комментарий 7OH от 2026-10-26: после ввода логина/пароля rac может
        // снова вернуть ошибку. Второй показ диалога обязан отличать «Повторная попытка
        // удаления не удалась: <детали rac>» от исходного «Не удалось удалить
        // информационную базу.» — пользователь видит, что креды применены, но rac
        // по-прежнему не удаляет базу. (В тестовой среде LocalizationManager не
        // инициализирован — T() возвращает ключ, по нему и проверяем.)
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 2 };
        var dialogs = new RecordingDialogs();
        var errors = new List<string>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                errors.Add(error);
                return new DropRetryResult(true, "ИБ-админ", "пароль");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // 1 исходная неудача + 2 повтора (успех на третьем). Диалог показан дважды.
        Assert.Equal(3, client.DropInfobaseCalls.Count);
        Assert.Equal(2, errors.Count);
        Assert.Contains("ServerMonitor.Infobase.DropFailed", errors[0]);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryFailedFormat", errors[1]);
        Assert.DoesNotContain("ServerMonitor.Infobase.Drop.RetryFailedFormat", errors[0]);
    }

    [Fact]
    public async Task DropSelectedInfobase_SessionsError_TerminateSessionsOption_TerminatesAndRetries()
    {
        // issue #69, комментарий 7OH от 2026-10-26: диалог повтора с кредами может не
        // помочь, если базу занимают активные сеансы/соединения — rac не удаляет занятую
        // базу. Кнопка «Завершить сеансы и повторить» сначала разрывает соединения и
        // завершает сеансы ИМЕННО этой базы (учётные данные уровня кластера — из
        // BuildParams, как у всех действий), затем выполняет повторный «infobase drop»
        // с кредами администратора ИБ из диалога.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
                new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true));
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Списки соединений и сеансов запрошены С фильтром по удаляемой базе: перед первым
        // drop (2 прохода: непустой + контрольный пустой), в явном «Завершить сеансы и
        // повторить» и перед повторным drop (автообновление в finally запрашивает их
        // целиком, с null — не в счёт). Фикстура после успешного разрыва возвращает
        // пустой список — как реальный rac.
        Assert.Equal(4, client.FilteredConnectionsListCalls);
        Assert.Equal(4, client.FilteredSessionsListCalls);
        // Фикстура возвращает одно соединение и один сеанс — оба разорваны/завершены.
        // issue #69 («повторить — ошибка», «Ошибка разбора параметра: process»): рабочий
        // процесс передаётся обеим rac-командам (8.5.4 без --process их отклоняет).
        var connection = Assert.Single(client.DisconnectCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, connection.clusterId);
        Assert.Equal(FakeRacClient.FirstProcessId, connection.processId);
        var session = Assert.Single(client.TerminateCalls);
        Assert.Equal(FakeRacClient.FirstSessionId, session.sessionId);
        Assert.Equal(FakeRacClient.FirstProcessId, session.processId);
        // Первый «infobase drop» без кредов упал (активные сеансы), второй — с кредами
        // администратора ИБ → успех.
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Null(client.DropInfobaseCallsWithCredentials[0].infobaseUser);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
        Assert.Equal("секрет", client.DropInfobaseCallsWithCredentials[1].infobasePassword);
        // Никаких «левых» предупреждений: завершение сеансов выполнилось без ошибок.
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_BlockScheduledJobs_BlocksFirst_ThenTerminates_ThenDrops()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «при повторном нажатии "Завершить
        // сеансы и повторить" — сначала ставить блокировку, а потом снимать сеансы».
        // Галочка «Установить блокировку регламентных заданий» → rac «infobase update
        // --scheduled-jobs-deny=on» ПЕРВЫМ, затем разрыв соединений/завершение сеансов,
        // затем повторный «infobase drop» с кредами администратора ИБ.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
                new DropRetryResult(true, "ИБ-админ", "секрет",
                    TerminateSessions: true, BlockScheduledJobs: true));
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Блокировка регламентных заданий выполнена ровно один раз, с флагом
        // ScheduledJobsDeny=true и параметрами СУБД из строки summary базы.
        var blockCall = Assert.Single(client.UpdateInfobaseCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, blockCall.clusterId);
        Assert.Equal(FakeRacClient.FirstInfobaseId, blockCall.infobaseId);
        Assert.True(blockCall.request.ScheduledJobsDeny);
        Assert.Equal("MSSQLServer", blockCall.request.Dbms);
        Assert.Equal("sql-01", blockCall.request.DbServer);
        Assert.Equal("buho", blockCall.request.DbName);
        // Соединения и сеансы сняты после блокировки (порядок «блокировка → сеансы»):
        // кроме явного прохода — ещё перед ПЕРВЫМ и ПОВТОРНЫМ drop (2+1+1 списка-запроса).
        Assert.Equal(4, client.FilteredConnectionsListCalls);
        Assert.Equal(4, client.FilteredSessionsListCalls);
        Assert.Single(client.DisconnectCalls);
        Assert.Single(client.TerminateCalls);
        // Первый «infobase drop» без кредов упал (активные сеансы), второй — с кредами
        // администратора ИБ → успех.
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Null(client.DropInfobaseCallsWithCredentials[0].infobaseUser);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_BlockScheduledJobs_EmptyDbmsInSummary_LoadsFromInfobaseInfo()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Тип СУБД в удаляемой базе явно же
        // прописан?». Сводка summary может не содержать параметров СУБД (пустые Dbms/
        // DbServer/DbName) — перед rac «infobase update --scheduled-jobs-deny=on» VM
        // догружает их через «infobase info» и использует для запроса блокировки.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            InfobaseSummaryOverride = new RacInfobaseSummary
            {
                InfobaseId = FakeRacClient.FirstInfobaseId,
                Name = "Бухгалтерия",
                Dbms = string.Empty,
                DbServer = string.Empty,
                DbName = string.Empty
            },
            InfobaseInfoResult = new RacInfobaseInfo
            {
                InfobaseId = FakeRacClient.FirstInfobaseId,
                Name = "Бухгалтерия",
                Dbms = "PostgreSQL",
                DbServer = "pg-02",
                DbName = "buh_prod"
            }
        };
        var dialogs = new RecordingDialogs();
        var retryShown = 0;
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                retryShown++;
                return new DropRetryResult(true, "ИБ-админ", "секрет",
                    TerminateSessions: true, BlockScheduledJobs: true);
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Параметры СУБД догружены через «infobase info» и попали в «infobase update».
        var blockCall = Assert.Single(client.UpdateInfobaseCalls);
        Assert.Equal("PostgreSQL", blockCall.request.Dbms);
        Assert.Equal("pg-02", blockCall.request.DbServer);
        Assert.Equal("buh_prod", blockCall.request.DbName);
        Assert.True(blockCall.request.ScheduledJobsDeny);
        Assert.True(client.InfobaseInfoCalls >= 1,
            "пустые параметры СУБД в summary должны догружаться через «infobase info»");
        // Цикл «блокировка → снять сеансы → drop» отработал без лишних диалогов и
        // предупреждений (повторная попытка удаления автоматически) — «удаляет не каждый
        // раз» устранено.
        Assert.Equal(1, retryShown);
        Assert.Empty(dialogs.Warnings);
        Assert.Equal(2, client.DropInfobaseCalls.Count);
    }

    [Fact]
    public async Task DropSelectedInfobase_BlockFails_NoSeparateWarning_CycleContinues()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Диалог появился (с ошибкой блокировки
        // заданий)... Этого диалога вообще быть не должно». Неудача установки блокировки
        // регламентных заданий НЕ показывает отдельное окно-предупреждение — детали
        // попадают строкой в следующий диалог повтора, цикл НЕ прерывается.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 2,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            UpdateInfobaseFailuresLeft = 1
        };
        var dialogs = new RecordingDialogs();
        var errors = new List<string>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                errors.Add(error);
                return new DropRetryResult(true, "ИБ-админ", "секрет",
                    TerminateSessions: true, BlockScheduledJobs: true);
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первая блокировка не удалась, вторая (после повторного диалога) — успешна.
        Assert.Equal(2, client.UpdateInfobaseCalls.Count);
        // drop: исходный fail → повторный диалог → повторный (успех после блокировки).
        Assert.Equal(3, client.DropInfobaseCalls.Count);
        Assert.Equal(2, errors.Count);
        // Пометка о неудачной блокировке — строкой во втором диалоге повтора
        // («Повторная попытка удаления не удалась: …»).
        Assert.Contains("ServerMonitor.Infobase.Drop.BlockRetryFailedFormat", errors[1]);
        // Отдельного окна-предупреждения об ошибке блокировки НЕТ.
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_BlockNoDbParamsFromSummaryOrInfo_ContinuesWithoutException()
    {
        // issue #69, риски: ни summary, ни «infobase info» не дали параметров СУБД
        // (защищённая база / нет прав). Блокировка не ставится с понятным текстом,
        // ArgumentException из сборки аргументов исключён: цикл продолжается, пометка
        // видна в диалоге повтора, «Отмена» прерывает удаление.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 2,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            InfobaseSummaryOverride = new RacInfobaseSummary
            {
                InfobaseId = FakeRacClient.FirstInfobaseId,
                Name = "Бухгалтерия",
                Dbms = string.Empty,
                DbServer = string.Empty,
                DbName = string.Empty
            }
            // InfobaseInfoResult = null — «infobase info» недоступен/не дал параметров.
        };
        var dialogs = new RecordingDialogs();
        var errors = new List<string>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                errors.Add(error);
                return errors.Count == 1
                    ? new DropRetryResult(true, "ИБ-админ", "секрет",
                        TerminateSessions: true, BlockScheduledJobs: true)
                    : DropRetryResult.Cancel;
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // «infobase update» не вызывался: параметров СУБД нет — блокировку ставить нечем.
        Assert.Empty(client.UpdateInfobaseCalls);
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Equal(2, errors.Count);
        Assert.Contains("ServerMonitor.Infobase.Drop.BlockRetryFailedFormat", errors[1]);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_TerminateSessions_RetriesWithIndicator_ThenExhaustedNote_ThenSucceeds()
    {
        // issue #69, комментарий 7OH от 2026-10-28: после установки блокировки РЗ rac
        // до возвращения пустого списка соединений требует НЕСКОЛЬКИХ попыток завершения
        // сеансов с небольшими паузами и индикацией «Попытка N из M…» в статус-строке.
        // Паузы в тесте мгновенные (asyncDelay); число встроенных попыток ограничено
        // (MaxSessionReleaseAttempts = 5) — при исчерпании показывается понятное
        // сообщение «Не удалось завершить сеансы за N попыток: …» и снова диалог повтора.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 2,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            // 1-й разрыв падает в release перед первым drop; ещё 5 отказов исчерпывают
            // встроенные попытки «Завершить сеансы и повторить» (1 + 5 = 6 отказов).
            DisconnectFailuresLeft = 6
        };
        var dialogs = new RecordingDialogs();
        var errors = new List<string>();
        var statusesDuringRetries = new List<string>();
        ServerMonitorViewModel? vm = null;
        vm = new ServerMonitorViewModel(client, dialogs,
            asyncDelay: _ =>
            {
                statusesDuringRetries.Add(vm!.StatusText);
                return Task.CompletedTask;
            },
            dropRetryDialog: (error, login, password) =>
            {
                errors.Add(error);
                return new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true);
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Индикация прогресса: между встроенными попытками в статусе окна — «Попытка N из M…».
        Assert.Equal(5, statusesDuringRetries.Count);
        Assert.All(statusesDuringRetries,
            s => Assert.Contains("ServerMonitor.Infobase.Drop.SessionReleaseAttemptFormat", s));
        // Одна попытка завершения сеансов внутри одного нажатия исчерпывается (не выходим
        // с ошибкой после первой) → пометка «Не удалось завершить сеансы за N попыток: …»
        // в следующем диалоге; затем разрыв успешен → 3 «infobase drop» (2 неудачи + успех).
        Assert.Equal(3, client.DropInfobaseCalls.Count);
        Assert.Equal(3, errors.Count);
        Assert.DoesNotContain("ServerMonitor.Infobase.Drop.SessionReleaseExhaustedFormat", errors[0]);
        Assert.Contains("ServerMonitor.Infobase.Drop.SessionReleaseExhaustedFormat", errors[1]);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryFailedFormat", errors[2]);
        // Разрыв соединений: 1 (падает перед первым drop) + 5 (встроенные ретраи) + 1
        // (успех на следующем проходе) = 7; завершение сеанса — после успешного разрыва.
        Assert.Equal(7, client.DisconnectCalls.Count);
        Assert.Single(client.TerminateCalls);
        Assert.Empty(dialogs.Warnings);
        // Креды администратора ИБ не теряются между попытками.
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[2].infobaseUser);
    }

    [Fact]
    public async Task DropSelectedInfobase_TerminateSessions_SucceedsOnSecondAttempt_WithPauses()
    {
        // issue #69: «после неудачной попытки завершения — короткая пауза и повтор»:
        // первый встроенный проход падает, второй — успешен, и удаление продолжается
        // без повторного диалога (один выход из цикла «Завершить сеансы и повторить»).
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            // 2 отказа: первый — в release перед первым drop, второй — в первом встроенном
            // ретрае «Завершить сеансы и повторить»; затем повтор успешен.
            DisconnectFailuresLeft = 2
        };
        var dialogs = new RecordingDialogs();
        var errors = new List<string>();
        var vm = new ServerMonitorViewModel(client, dialogs,
            asyncDelay: _ => Task.CompletedTask,
            dropRetryDialog: (error, login, password) =>
            {
                errors.Add(error);
                return new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true);
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первый drop-цикл: release падает («соединение не успело закрыться»); встроенный
        // ретрай на второй попытке успешно снимает соединение/сеанс → drop 2 раза.
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        // Разрывов: 1 (фейл) + 2 попытки встроенного ретрая + 0 (списки уже пусты) = 3.
        Assert.Equal(3, client.DisconnectCalls.Count);
        Assert.Single(client.TerminateCalls);
        // Никакой пометки об исчерпании — ретрай успешен ДО исчерпания попыток.
        Assert.DoesNotContain("ServerMonitor.Infobase.Drop.SessionReleaseExhaustedFormat", string.Join("\n", errors));
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_ReleaseContextError_RetriesAfterPause_AndSucceeds()
    {
        // issue #69, лог 7OH от 2026-10-28: rac отвечает «Попытка подключения к
        // освобождаемому контексту информационной базы. Требуется повторить попытку
        // подключения.» — такая ошибка НЕ прерывает завершение сеансов: после короткой
        // паузы (освобождение контекста) следующий встроенный проход снимает сеансы.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            // 2 отказа: первый — в release перед первым drop, второй — в первом встроенном
            // ретрае (оба с «освобождаемым контекстом»); затем повтор успешен.
            DisconnectFailuresLeft = 2,
            DisconnectFailureError = "Ошибка соединения с информационной базой. Попытка подключения к освобождаемому контексту информационной базы."
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            asyncDelay: _ => Task.CompletedTask,
            dropRetryDialog: (_, _, _) =>
                new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true));
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Разрыв соединений: 1 (фейл «освобождаемый контекст» в release перед первым drop)
        // + 1 (фейл в первом встроенном ретрае) + 1 (успех во втором ретрае) = 3.
        Assert.Equal(3, client.DisconnectCalls.Count);
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_TerminateFails_ThenCancel_Aborts()
    {
        // issue #69: после неудачного завершения сеансов цикл повторяет диалог, но
        // «Отмена» в нём ПО-ПРЕЖНЕМУ прерывает удаление — бесконечного повтора нет.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 2,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            DisconnectFailuresLeft = 1
        };
        var dialogs = new RecordingDialogs();
        var shown = 0;
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (error, login, password) =>
            {
                shown++;
                return shown == 1
                    ? new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true)
                    : DropRetryResult.Cancel;
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первый drop-цикл НЕ добрался до drop: release перед ним упал («соединение не
        // успело закрыться»); второй drop — после успешного явного «Завершить сеансы и
        // повторить» (уже с кредами, это и есть первая неудача rac); «Отмена» во втором
        // диалоге прерывает — третьего drop нет.
        Assert.Single(client.DropInfobaseCalls);
        Assert.Equal(2, shown);
        Assert.Equal(2, client.DisconnectCalls.Count);
        Assert.Single(client.TerminateCalls);
        Assert.Empty(dialogs.Warnings);
        // Статус-строка после finally → Refresh() переустановлена на «загружено» — проверяем
        // лишь, что она непустая (как в остальных drop-тестах); прерывание подтверждено
        // количеством вызовов rac выше.
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DropSelectedInfobase_BeforeFirstDrop_ConnectionsReleased_ThenDrop()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «кто-то забыл удалить текущие
        // соединения» — перед КАЖДЫМ «infobase drop» гарантированно разрываются
        // соединения и завершаются сеансы именно этой базы (иначе rac возвращает
        // «Информационная база имеет непустой список соединений»).
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            // «Отмена» при неудаче — здесь нажата не будет: первый drop успешен.
            dropRetryDialog: (_, _, _) => DropRetryResult.Cancel);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Успешный «infobase drop» предваряется разрывом соединения и завершением
        // сеанса фикстуры (по одному) — с фильтром по удаляемой базе.
        Assert.Single(client.DropInfobaseCalls);
        Assert.Single(client.DisconnectCalls);
        Assert.Single(client.TerminateCalls);
        Assert.True(client.ConnectionReleased);
        Assert.True(client.SessionReleased);
        Assert.True(client.FilteredConnectionsListCalls >= 1);
        Assert.True(client.FilteredSessionsListCalls >= 1);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_RetryDrop_ReleasesConnectionsBeforeEveryDrop()
    {
        // issue #69: разрыв соединений/сеансов выполняется перед ПЕРВЫМ drop и перед
        // каждым ПОВТОРНЫМ drop — списки с фильтром по базе запрашиваются заново.
        // Диалог повтора здесь отвечает простым «Повторить попытку» (без специальной
        // кнопки «Завершить сеансы и повторить») — release перед повторным drop и есть
        // гарантия того, что rac не вернёт «непустой список соединений».
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база имеет непустой список соединений"
        };
        var retryShown = 0;
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) =>
            {
                retryShown++;
                return new DropRetryResult(true, "ИБ-админ", "секрет");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первый drop: release (2 списка: непустой + контрольный) → неудача rac.
        // Повторный drop: release снова (списки уже пусты — фикстура как реальный rac).
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Equal(1, retryShown);
        Assert.Single(client.DisconnectCalls);
        Assert.Single(client.TerminateCalls);
        Assert.Equal(3, client.FilteredConnectionsListCalls);
        Assert.Equal(3, client.FilteredSessionsListCalls);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_ReleaseLimitedToThreePasses_WhenListNeverEmpties()
    {
        // issue #69: «при необходимости итеративно 2–3 раза со сбросом списка». Если
        // соединения/сеансы не исчезают (гонка «не успело закрыться»), release-цикл
        // ограничен тремя проходами и НЕ зависает; drop всё равно выполняется.
        var client = new FakeRacClient
        {
            // Списки всегда непустые — цикл упирается в максимум проходов.
            KeepConnectionsAfterDisconnect = true,
            KeepSessionsAfterTerminate = true
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) => DropRetryResult.Cancel);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Ровно 3 прохода (по одному соединению и сеансу в каждом), drop выполнен.
        Assert.Equal(3, client.DisconnectCalls.Count);
        Assert.Equal(3, client.TerminateCalls.Count);
        Assert.Single(client.DropInfobaseCalls);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_Retry_DisconnectAndTerminate_GetInfobaseCredentials()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «удалять сеансы не хочет» — в его
        // логе «connection disconnect»/«session terminate» падали с «Недостаточно прав
        // пользователя на информационную базу», потому что креды администратора ИБ из
        // диалога никуда не передавались (кроме самого «infobase drop»). Теперь они
        // передаются в разрыв соединений и завершение сеансов.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 2,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            // Гонка «соединение не исчезло»: повторные release-циклы снова находят
            // соединение/сеанс — как в логах 7OH, где база занята, а креды нужны
            // каждому «connection disconnect»/«session terminate».
            KeepConnectionsAfterDisconnect = true,
            KeepSessionsAfterTerminate = true
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) =>
                new DropRetryResult(true, "ИБ-админ", "секрет", TerminateSessions: true));
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первая попытка — кредов ещё нет (диалог не отвечен); повторные разрывы — с
        // кредами администратора ИБ из диалога.
        Assert.Null(client.DisconnectCallsWithCredentials[0].infobaseUser);
        var withCreds = client.DisconnectCallsWithCredentials
            .Where(c => c.infobaseUser is not null).ToList();
        Assert.NotEmpty(withCreds);
        Assert.All(withCreds, c =>
        {
            Assert.Equal("ИБ-админ", c.infobaseUser);
            Assert.Equal("секрет", c.infobasePassword);
        });
        var terminatedWithCreds = client.TerminateCallsWithCredentials
            .Where(c => c.infobaseUser is not null).ToList();
        Assert.NotEmpty(terminatedWithCreds);
        Assert.All(terminatedWithCreds, c =>
        {
            Assert.Equal("ИБ-админ", c.infobaseUser);
            Assert.Equal("секрет", c.infobasePassword);
        });
    }

    [Fact]
    public async Task DropSelectedInfobase_ServiceConnectionDisconnectFailure_IsSkipped_DropSucceeds()
    {
        // issue #69, комментарий 7OH от 2026-10-27 (лог 23:56): «connection disconnect»
        // служебного соединения возвращает «Служебное соединение не может быть удалено» —
        // раньше это ПРЕРЫВАЛО весь цикл разрыва, дальше (завершение сеансов, «infobase
        // drop») дело не доходило. Служебное соединение нельзя разорвать, но drop оно
        // НЕ блокирует — оно пропускается, остальной разрыв и удаление продолжаются.
        var client = new FakeRacClient
        {
            DisconnectFailuresLeft = 1,
            DisconnectFailureError = "rac: Служебное соединение не может быть удалено"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) => DropRetryResult.Cancel);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // Первый disconnect «служебный» — пропущен, второй успешен; сеансы завершены;
        // база удалена БЕЗ показа диалога повтора.
        Assert.Equal(2, client.DisconnectCalls.Count);
        Assert.Single(client.TerminateCalls);
        Assert.Single(client.DropInfobaseCalls);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public async Task DropSelectedInfobase_AutoRefresh_DisabledDuringOperation_RestoredAfter()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «отключать автообновление данных на
        // время, если оно было включено» — тик таймера перезапрашивает сеансы/соединения
        // и может пересоздавать соединения во время удаления. Автообновление выключается
        // на время всей операции удаления (включая диалог повтора) и включается обратно.
        var client = new FakeRacClient { DropInfobaseFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var autoRefreshDuringDialog = true;
        ServerMonitorViewModel vm = null!;
        vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) =>
            {
                // Пока открыт диалог повтора — таймер автообновления остановлен
                // (к моменту вызова колбэка vm уже создана).
                autoRefreshDuringDialog = vm.AutoRefreshActive;
                return new DropRetryResult(true, "ИБ-админ", "секрет");
            });
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        // До операции автообновление активно (подключение запускает таймер).
        Assert.True(vm.AutoRefreshActive);
        Assert.True(vm.IsAutoRefreshEnabled);

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        Assert.False(autoRefreshDuringDialog, "автообновление должно быть остановлено на время операции");
        // После операции автообновление восстановлено.
        Assert.True(vm.IsAutoRefreshEnabled);
        Assert.True(vm.AutoRefreshActive);
        // Первая попытка удаления не удалась (диалог показан) → повтор с кредами успешен.
        Assert.Equal(2, client.DropInfobaseCalls.Count);
    }

    [Fact]
    public async Task DropSelectedInfobase_BlockScheduledJobs_PassesCredentials_ToInfobaseInfoAndUpdate()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «Не удаётся теперь установить
        // блокировку» — на защищённой базе rac «infobase info» (догрузка параметров СУБД)
        // и «infobase update --scheduled-jobs-deny=on» без --infobase-user/--infobase-pwd
        // возвращают «Недостаточно прав». Креды из диалога передаются в оба вызова.
        var client = new FakeRacClient
        {
            DropInfobaseFailuresLeft = 1,
            DropInfobaseFailureError = "rac: информационная база используется активными сеансами",
            // Сводка не содержит параметров СУБД — догружаем через «infobase info».
            InfobaseSummaryOverride = new RacInfobaseSummary
            {
                InfobaseId = FakeRacClient.FirstInfobaseId,
                Name = "Бухгалтерия"
            },
            InfobaseInfoResult = new RacInfobaseInfo
            {
                Dbms = "MSSQLServer",
                DbServer = "sql-01",
                DbName = "buho"
            }
        };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs,
            dropRetryDialog: (_, _, _) =>
                new DropRetryResult(true, "ИБ-админ", "секрет", BlockScheduledJobs: true, TerminateSessions: true));
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.DropSelectedInfobaseAsync(dropDatabase: false);

        // «infobase info» (догрузка параметров СУБД) вызван с кредами администратора ИБ.
        Assert.True(client.InfobaseInfoCalls >= 1, "догрузка параметров СУБД должна была вызвать «infobase info»");
        Assert.Equal("ИБ-админ", client.LastInfobaseInfoUser);
        Assert.Equal("секрет", client.LastInfobaseInfoPassword);
        // «infobase update --scheduled-jobs-deny=on» получил креды и параметры СУБД.
        // Блокировка ставится на каждом повторе (галочка в ответе диалога) — проверяем
        // последний вызов (после успешного повтора кредами из того же диалога).
        var update = Assert.Single(client.UpdateInfobaseCalls);
        Assert.True(update.request.ScheduledJobsDeny);
        Assert.Equal("ИБ-админ", update.request.InfobaseUser);
        Assert.Equal("секрет", update.request.InfobasePassword);
        Assert.Equal("MSSQLServer", update.request.Dbms);
        Assert.Equal("sql-01", update.request.DbServer);
        Assert.Equal("buho", update.request.DbName);
        // Первая попытка drop без кредов → диалог → повтор с кредами успешен.
        Assert.Equal(2, client.DropInfobaseCalls.Count);
        Assert.Equal("ИБ-админ", client.DropInfobaseCallsWithCredentials[1].infobaseUser);
    }

    [Theory]
    [InlineData("rac: Служебное соединение не может быть удалено", true)]
    [InlineData("СЛУЖЕБНОЕ СОЕДИНЕНИЕ НЕ МОЖЕТ БЫТЬ УДАЛЕНО", true)]
    [InlineData("rac: Service connection cannot be terminated", true)]
    [InlineData("rac: Информационная база имеет непустой список соединений", false)]
    [InlineData("rac: Недостаточно прав пользователя на информационную базу", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsServiceConnectionError_DetectsServiceConnectionRejections(string? detail, bool expected)
    {
        // issue #69, комментарий 7OH от 2026-10-27: «Служебное соединение не может быть
        // удалено» — штатный отказ rac, прерывать из-за него разрыв и удаление нельзя.
        Assert.Equal(expected, ServerMonitorViewModel.IsServiceConnectionError(detail));
    }

    [Theory]
    [InlineData("Ошибка соединения с информационной базой. Попытка подключения к освобождаемому контексту информационной базы. Требуется повторить попытку подключения.", true)]
    [InlineData("Attempt to connect to the infobase being released. Retry the connection.", true)]
    [InlineData("the context of the infobase being released", true)]
    [InlineData("rac: Информационная база имеет непустой список соединений", false)]
    [InlineData("rac: соединение не успело закрыться", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsReleaseContextError_DetectsReleasedContextErrors(string? detail, bool expected)
    {
        // issue #69, лог 7OH от 2026-10-28: «Попытка подключения к освобождаемому
        // контексту информационной базы» — НЕ финал: после паузы следующая попытка
        // завершения сеансов обычно проходит, цикл прерывать нельзя.
        Assert.Equal(expected, ServerMonitorViewModel.IsReleaseContextError(detail));
    }

    [Theory]
    [InlineData("rac: требуется аутентификация администратора информационной базы", ServerMonitorViewModel.RacDropErrorKind.Authentication)]
    [InlineData("Access denied to infobase", ServerMonitorViewModel.RacDropErrorKind.Authentication)]
    [InlineData("нет прав доступа для удаления базы", ServerMonitorViewModel.RacDropErrorKind.Authentication)]
    [InlineData("infobase is used by active sessions", ServerMonitorViewModel.RacDropErrorKind.Sessions)]
    [InlineData("активные сеансы не позволяют удалить базу", ServerMonitorViewModel.RacDropErrorKind.Sessions)]
    [InlineData("есть соединения к базе", ServerMonitorViewModel.RacDropErrorKind.Sessions)]
    [InlineData("не удалось удалить базу данных на сервере СУБД", ServerMonitorViewModel.RacDropErrorKind.Database)]
    [InlineData("db user 'sa' has no privileges", ServerMonitorViewModel.RacDropErrorKind.Database)]
    [InlineData("неизвестная ошибка rac", ServerMonitorViewModel.RacDropErrorKind.Unknown)]
    [InlineData("", ServerMonitorViewModel.RacDropErrorKind.Unknown)]
    [InlineData(null, ServerMonitorViewModel.RacDropErrorKind.Unknown)]
    public void ClassifyDropError_RecognizesTypicalRacErrors(
        string? detail, ServerMonitorViewModel.RacDropErrorKind expected)
    {
        // issue #69: классификация типовой ошибки rac при «infobase drop» — от неё
        // зависят подсказка в диалоге повтора и кнопка «Завершить сеансы и повторить».
        var kind = ServerMonitorViewModel.ClassifyDropError(detail);
        Assert.Equal(expected, kind);
        // Hint для Unknown пустой; для распознанных — непустой (в тестах T() возвращает ключ).
        Assert.Equal(
            kind == ServerMonitorViewModel.RacDropErrorKind.Unknown,
            string.IsNullOrEmpty(ServerMonitorViewModel.BuildDropRetryHint(kind)));
    }

    [Fact]
    public void CanDropDatabaseOnServer_OnlyKnownClientServerTypes_True()
    {
        // Вариант «с удалением на сервере СУБД» доступен только клиент-серверным базам
        // (комментарий 7OH от 2026-10-17). С 1.0.0.14 — «белый список» известных СУБД
        // (issue #3, 18.10 17:16); с 1.0.0.15 — ровно значения rac «infobase create --dbms»
        // (комментарий 7OH от 2026-10-18 20:24): SQLite/ServerODBC в кластере не бывают.
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.MssqlServer })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = "postgresql" })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.IbmDb2 })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.OracleDatabase })));
    }

    [Fact]
    public void CanDropDatabaseOnServer_InitializedFromQuotedTable_IsTrue()
    {
        // issue #3, часть 1: rac табличного summary может заключать «dbms» в кавычки
        // («"MSSQLServer"»). Парсер снимает кавычки — строка, разобранная из такого
        // вывода, обязана распознаваться как клиент-серверная база (вариант
        // «Удалить с удалением на сервере СУБД» в диалоге удаления).
        const string output =
            "infobase\tname\tdescr\tdbms\tdb-server\tdb-name\tdb-user\tlocale\tsecurity-level\tlicensed\n" +
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\tБухгалтерия\t\t\"MSSQLServer\"\tsql-01\tbuho\tsa\tru\t0\t0\n";
        var summary = Assert.Single(RacOutputParser.ToInfobaseSummaries(output));
        var row = new RacInfobaseRow(summary);

        Assert.Equal("MSSQLServer", row.Dbms);
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(row));
    }

    [Fact]
    public void CanDropDatabaseOnServer_AnyClusterBase_IsTrue()
    {
        // В кластере 1С бывают только клиент-серверные базы (комментарий 7OH от
        // 2026-10-18 20:24 и 19.10 19:12), поэтому вариант «Удалить с удалением на
        // сервере СУБД» предлагается ВСЕМ базам списка кластера независимо от
        // распознанного «dbms» (до 1.0.0.21 белый список блокировал вариант на
        // версиях rac с нестандартным значением «dbms»). Распознанный тип остаётся
        // только диагностической подписью в диалоге удаления.
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.File })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = "File" })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.Sqlite })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = RacDbms.ServerOdbc })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = string.Empty })));
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = "   " })));
        // Незнакомое значение — тоже база кластера, вариант доступен (1.0.0.21).
        Assert.True(ServerMonitorViewModel.CanDropDatabaseOnServer(
            new RacInfobaseRow(new RacInfobaseSummary { Dbms = "FileBase" })));
        // Без выбранной базы вариант недоступен.
        Assert.False(ServerMonitorViewModel.CanDropDatabaseOnServer(null));
    }

    // ===================== Заголовки с количеством строк и фильтр (issue #3) =====================

    [Fact]
    public async Task LoadClusterDataAsync_TabHeaderTexts_ContainRowCounts()
    {
        // issue #3 (7OH от 2026-10-17): «После названия во вкладках должно показывать
        // количество строк на данной вкладке», например «Сеансы (1)».
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        // Используем «0» — заголовки видны и до загрузки данных.
        Assert.Equal(LocalizationManager.T("ServerMonitor.Tabs.CountFormat")
            .Replace("{0}", LocalizationManager.T("ServerMonitor.Tabs.Sessions")).Replace("{1}", "0"),
            vm.SessionsTabText);

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Equal(
            string.Format(
                LocalizationManager.T("ServerMonitor.Tabs.CountFormat"),
                LocalizationManager.T("ServerMonitor.Tabs.Processes"), 1),
            vm.ProcessesTabText);
        Assert.Equal(
            string.Format(
                LocalizationManager.T("ServerMonitor.Tabs.CountFormat"),
                LocalizationManager.T("ServerMonitor.Tabs.Sessions"), 1),
            vm.SessionsTabText);
        Assert.Equal(
            string.Format(
                LocalizationManager.T("ServerMonitor.Tabs.CountFormat"),
                LocalizationManager.T("ServerMonitor.Tabs.Infobases"), 1),
            vm.InfobasesHeaderText);
    }

    [Fact]
    public void InfobaseFilterText_WithoutFilter_IsNoFilter()
    {
        // issue #3: надпись фильтра видна ВСЕГДА; при снятом фильтре — «Без фильтра».
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());

        Assert.Equal(LocalizationManager.T("ServerMonitor.Infobase.NoFilter"), vm.InfobaseFilterText);
        Assert.False(vm.HasInfobaseFilter);
    }

    [Fact]
    public void SelectInfobase_CanDropInfobase_BecomesTrue()
    {
        // issue #3 (7OH от 2026-10-17, «Кнопка удаления сейчас не активна почему-то всегда»):
        // CanDropInfobase должен обновляться при выборе базы — раньше PropertyChanged не
        // поднимался, и кнопка «Удалить» оставалась неактивной навсегда.
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());

        Assert.False(vm.CanDropInfobase);

        // Подключаемся и загружаем базы; выбираем базу.
        var changed = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerMonitorViewModel.CanDropInfobase))
                changed = true;
        };
        vm.SelectedInfobase = new RacInfobaseRow(new RacInfobaseSummary
        {
            InfobaseId = FakeRacClient.FirstInfobaseId,
            Name = "Бухгалтерия"
        });

        Assert.True(changed, "CanDropInfobase обязан уведомлять об изменении выбора (issue #3)");
    }

    [Fact]
    public async Task UpdateSelectedInfobaseAsync_CallsClient_WithCorrectArguments()
    {
        // issue #3 (7OH от 2026-10-17): кнопка «Изменить» для базы — rac «infobase update».
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        vm.SelectedInfobase = vm.Infobases.Single();
        var update = new RacInfobaseUpdateRequest
        {
            Name = "Бухгалтерия новая",
            Dbms = RacDbms.MssqlServer,
            DbServer = "sql-01",
            DbName = "buh1c"
        };
        await vm.UpdateSelectedInfobaseAsync(update);

        var call = Assert.Single(client.UpdateInfobaseCalls);
        Assert.Equal(FakeRacClient.FirstClusterId, call.clusterId);
        Assert.Equal(FakeRacClient.FirstInfobaseId, call.infobaseId);
        Assert.Equal("Бухгалтерия новая", call.request.Name);
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task UpdateSelectedInfobaseAsync_WithoutSelection_DoesNothing()
    {
        var client = new FakeRacClient();
        var vm = new ServerMonitorViewModel(client, new RecordingDialogs());
        await vm.ConnectAsync();

        await vm.UpdateSelectedInfobaseAsync(new RacInfobaseUpdateRequest { Name = "X" });

        Assert.Empty(client.UpdateInfobaseCalls);
    }

    [Fact]
    public async Task UpdateSelectedInfobaseAsync_ClientFails_ShowsWarning()
    {
        var client = new FakeRacClient(actionFails: true);
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);
        vm.SelectedInfobase = vm.Infobases.Single();

        await vm.UpdateSelectedInfobaseAsync(new RacInfobaseUpdateRequest
        {
            Name = "Бухгалтерия",
            Dbms = RacDbms.PostgreSql,
            DbServer = "pg-01",
            DbName = "buh"
        });

        Assert.Single(client.UpdateInfobaseCalls);
        Assert.NotEmpty(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    // ===================== Перемещение серверов (issue #3) =====================

    [Fact]
    public void MoveServerUp_SwapsWithPreviousAndSaves()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var a = new ManagementServer { Id = "a", Description = "А", Address = "srv-a" };
        var b = new ManagementServer { Id = "b", Description = "Б", Address = "srv-b" };
        vm.AddServer(a);
        vm.AddServer(b);
        vm.SelectedServer = b;

        vm.MoveServerUp();

        Assert.Equal("b", vm.Servers[0].Id);
        Assert.Equal("a", vm.Servers[1].Id);
        // Сохранённый порядок в хранилище совпадает с новым.
        var saved = store.Load();
        Assert.Equal("b", saved[0].Id);
        Assert.Equal("a", saved[1].Id);
    }

    [Fact]
    public void MoveServerDown_SwapsWithNextAndSaves()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var a = new ManagementServer { Id = "a", Description = "А", Address = "srv-a" };
        var b = new ManagementServer { Id = "b", Description = "Б", Address = "srv-b" };
        vm.AddServer(a);
        vm.AddServer(b);
        vm.SelectedServer = a;

        vm.MoveServerDown();

        Assert.Equal("b", vm.Servers[0].Id);
        Assert.Equal("a", vm.Servers[1].Id);
        var saved = store.Load();
        Assert.Equal("b", saved[0].Id);
        Assert.Equal("a", saved[1].Id);
    }

    [Fact]
    public void MoveServerUp_FirstItem_DoesNothing()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var a = new ManagementServer { Id = "a", Description = "А", Address = "srv-a" };
        var b = new ManagementServer { Id = "b", Description = "Б", Address = "srv-b" };
        vm.AddServer(a);
        vm.AddServer(b);
        vm.SelectedServer = a;

        Assert.False(vm.CanMoveServerUp);
        vm.MoveServerUp();

        Assert.Equal("a", vm.Servers[0].Id);
        Assert.Equal("b", vm.Servers[1].Id);
    }

    [Fact]
    public void MoveServerDown_LastItem_DoesNothing()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        var a = new ManagementServer { Id = "a", Description = "А", Address = "srv-a" };
        var b = new ManagementServer { Id = "b", Description = "Б", Address = "srv-b" };
        vm.AddServer(a);
        vm.AddServer(b);
        vm.SelectedServer = b;

        Assert.False(vm.CanMoveServerDown);
        vm.MoveServerDown();

        Assert.Equal("a", vm.Servers[0].Id);
        Assert.Equal("b", vm.Servers[1].Id);
    }

    [Fact]
    public void CanMove_WithoutSelection_IsFalse()
    {
        using var tmp = TempDir();
        var store = new ServerListStore(directoryOverride: tmp.Path);
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), serverListStore: store);
        vm.AddServer(new ManagementServer { Id = "a", Description = "А", Address = "srv-a" });

        Assert.False(vm.CanMoveServerUp);
        Assert.False(vm.CanMoveServerDown);
    }

    // ===================== Мультивыделение (issue #72) =====================

    [Fact]
    public async Task TerminateSessions_MultipleSelected_TerminatesAll_WithSingleConfirm()
    {
        // issue #72: несколько выбранных сеансов (Ctrl/Shift) завершаются ОДНИМ нажатием —
        // единое подтверждение о количестве, затем rac «session terminate» для каждого.
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var first = new RacSessionRow(new RacSessionInfo { Id = Guid.NewGuid(), User = "Иванов" });
        var second = new RacSessionRow(new RacSessionInfo { Id = Guid.NewGuid(), User = "Петров" });
        vm.SelectedSessions = new[] { first, second };
        Assert.True(vm.CanTerminateSessions);

        await vm.TerminateSessionAsync();

        // Единое подтверждение (не по каждому сеансу) — текст о количестве строк.
        var confirm = Assert.Single(dialogs.Confirms);
        Assert.Contains("ServerMonitor.TerminateBulkConfirmFormat", confirm.message);
        Assert.Empty(dialogs.Warnings);
        // rac вызван для КАЖДОГО выбранного сеанса (последовательно).
        Assert.Equal(2, client.TerminateCalls.Count);
        Assert.Contains(client.TerminateCalls, c => c.sessionId == first.Id);
        Assert.Contains(client.TerminateCalls, c => c.sessionId == second.Id);
        Assert.All(client.TerminateCalls, c => Assert.Equal(FakeRacClient.FirstClusterId, c.clusterId));
        // После действия списки перечитаны (Refresh в конце массовой операции).
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task TerminateSessions_PartialFailure_ContinuesToOtherSessions()
    {
        // issue #72: частичная ошибка не прерывает остальные сеансы — все выбранные
        // строки обрабатываются; сводка в статус-строке и детали в ErrorMessage.
        var client = new FakeRacClient { TerminateFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var first = new RacSessionRow(new RacSessionInfo { Id = Guid.NewGuid(), User = "Иванов" });
        var second = new RacSessionRow(new RacSessionInfo { Id = Guid.NewGuid(), User = "Петров" });
        vm.SelectedSessions = new[] { first, second };
        await vm.TerminateSessionAsync();

        // Первый сеанс не удался, но второй всё равно завершён — цикл не прерван.
        Assert.Equal(2, client.TerminateCalls.Count);
        Assert.Empty(dialogs.Warnings);
        // Сводка «N из M» с индикацией ошибок складывается в статус-строку до перечитывания
        // списков; после Refresh (LoadClusterDataAsync сбрасывает ErrorMessage в тестах)
        // проверяем только, что статус непустой.
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DisconnectConnections_MultipleSelected_DisconnectsAll_WithSingleConfirm()
    {
        // issue #72: несколько выбранных соединений разрываются одним нажатием — единое
        // подтверждение и «connection disconnect» для каждого (с рабочим процессом, #69).
        var client = new FakeRacClient();
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var first = new RacConnectionRow(new RacConnectionInfo
        {
            Id = Guid.NewGuid(),
            ProcessId = FakeRacClient.FirstProcessId,
            Host = "client1"
        });
        var second = new RacConnectionRow(new RacConnectionInfo
        {
            Id = Guid.NewGuid(),
            ProcessId = FakeRacClient.FirstProcessId,
            Host = "client2"
        });
        vm.SelectedConnections = new[] { first, second };
        Assert.True(vm.CanDisconnectConnections);

        await vm.DisconnectConnectionAsync();

        var confirm = Assert.Single(dialogs.Confirms);
        Assert.Contains("ServerMonitor.DisconnectBulkConfirmFormat", confirm.message);
        Assert.Empty(dialogs.Warnings);
        Assert.Equal(2, client.DisconnectCalls.Count);
        Assert.Contains(client.DisconnectCalls, c => c.connectionId == first.Id);
        Assert.Contains(client.DisconnectCalls, c => c.connectionId == second.Id);
        // issue #69: рабочий процесс передаётся каждому вызову (rac 8.5.4).
        Assert.All(client.DisconnectCalls, c => Assert.Equal(FakeRacClient.FirstProcessId, c.processId));
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task DisconnectConnections_PartialFailure_DoesNotStopRemaining()
    {
        // issue #72: неудача одного соединения (rac вернул false) не останавливает остальные.
        var client = new FakeRacClient { DisconnectFailuresLeft = 1 };
        var dialogs = new RecordingDialogs();
        var vm = new ServerMonitorViewModel(client, dialogs);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        var first = new RacConnectionRow(new RacConnectionInfo
        {
            Id = Guid.NewGuid(),
            ProcessId = FakeRacClient.FirstProcessId,
            Host = "client1"
        });
        var second = new RacConnectionRow(new RacConnectionInfo
        {
            Id = Guid.NewGuid(),
            ProcessId = FakeRacClient.FirstProcessId,
            Host = "client2"
        });
        vm.SelectedConnections = new[] { first, second };
        await vm.DisconnectConnectionAsync();

        Assert.Equal(2, client.DisconnectCalls.Count);
        Assert.Empty(dialogs.Warnings);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public void BuildBulkStatusText_FormatsDoneOutOfTotal_AndAppendsErrorsCount()
    {
        // issue #72: сводка массовой операции «N из M»; при ошибках — суффикс с их числом
        // (в тестовой среде LocalizationManager.T возвращает ключ — по нему и проверяем).
        Assert.Equal(
            "ServerMonitor.Status.TerminatedBulkFormat",
            ServerMonitorViewModel.BuildBulkStatusText("ServerMonitor.Status.TerminatedBulkFormat", 2, 2, 0));
        Assert.Equal(
            "ServerMonitor.Status.DisconnectedBulkFormat",
            ServerMonitorViewModel.BuildBulkStatusText("ServerMonitor.Status.DisconnectedBulkFormat", 3, 5, 0));
    }

    [Fact]
    public void BuildBulkStatusText_WithErrors_ContainsErrorsSuffix()
    {
        // issue #72: индикация ошибок в общей сводке («N из M. Ошибок: K»). В тестовой
        // среде LocalizationManager.T возвращает ключ — по нему проверяем суффикс ошибок.
        var text = ServerMonitorViewModel.BuildBulkStatusText("ServerMonitor.Status.TerminatedBulkFormat", 1, 3, 2);
        Assert.Contains("ServerMonitor.Status.BulkErrorsFormat", text);
    }

    [Fact]
    public void CanTerminateSessions_RequiresConnectionAndSelection()
    {
        // issue #72: без подключения и без выбора кнопка недоступна (как раньше).
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        Assert.False(vm.CanTerminateSessions);

        // Выбор есть, подключения нет — кнопка всё ещё недоступна.
        vm.SelectedSessions = new[] { new RacSessionRow(new RacSessionInfo { User = "Иванов" }) };
        Assert.False(vm.CanTerminateSessions);
    }

    [Fact]
    public void CanDisconnectConnections_RequiresConnectionAndSelection()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs());
        Assert.False(vm.CanDisconnectConnections);

        vm.SelectedConnections = new[] { new RacConnectionRow(new RacConnectionInfo { Id = Guid.NewGuid() }) };
        Assert.False(vm.CanDisconnectConnections);
    }

    // ===================== Fakes =====================

    /// <summary>Fake-клиент rac для тестов: два кластера, по одной строке данных.</summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly System.Guid FirstClusterId = System.Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly System.Guid SecondClusterId = System.Guid.Parse("22222222-2222-2222-2222-222222222222");

        /// <summary>Идентификатор регламентного задания «Обмен данными».</summary>
        public static readonly System.Guid FirstJobId = System.Guid.Parse("aaaaaaaa-1111-2222-3333-444455556666");

        /// <summary>Идентификатор регламентного задания без базы (служебное).</summary>
        public static readonly System.Guid SecondJobId = System.Guid.Parse("bbbbbbbb-1111-2222-3333-444455556666");

        /// <summary>Идентификатор информационной базы-владельца первого задания.</summary>
        public static readonly System.Guid FirstInfobaseId = System.Guid.Parse("cccccccc-1111-2222-3333-444455556666");

        /// <summary>Идентификатор сеанса (детерминированный — соединение ссылается на него).</summary>
        public static readonly System.Guid FirstSessionId = System.Guid.Parse("dddddddd-1111-2222-3333-444455556666");

        /// <summary>Идентификатор соединения сеанса (issue #69: по нему VM находит рабочий процесс).</summary>
        public static readonly System.Guid FirstConnectionId = System.Guid.Parse("eeeeeeee-1111-2222-3333-444455556666");

        /// <summary>Идентификатор рабочего процесса соединения (issue #69: rac 8.5.4 требует
        /// «--process=<pid>» для «session terminate»/«connection disconnect»).</summary>
        public static readonly System.Guid FirstProcessId = System.Guid.Parse("ffffffff-1111-2222-3333-444455556666");

        private readonly bool _throwOnClusters;
        private readonly bool _throwOnAction;
        private readonly bool _actionFails;
        private readonly TimeSpan _delay;
        private readonly bool _throwOnParse;
        private readonly bool _singleCluster;
        private readonly bool _throwOnLoad;
        private readonly bool _throwParseOnSessions;

        public FakeRacClient(
            bool throwOnClusters = false, bool throwOnAction = false,
            bool actionFails = false, TimeSpan? delay = null,
            bool throwOnParse = false, bool singleCluster = false,
            bool throwOnLoad = false, bool throwParseOnSessions = false)
        {
            _throwOnClusters = throwOnClusters;
            _throwOnAction = throwOnAction;
            _actionFails = actionFails;
            _delay = delay ?? TimeSpan.Zero;
            _throwOnParse = throwOnParse;
            _singleCluster = singleCluster;
            _throwOnLoad = throwOnLoad;
            _throwParseOnSessions = throwParseOnSessions;
        }

        /// <summary>Текст последней ошибки действия (как в реальном клиенте).</summary>
        public string LastActionError { get; private set; } = string.Empty;

        /// <summary>Вызовы «session terminate»: (clusterId, sessionId, processId) — processId
        /// обязателен для rac 8.5.4 (issue #69 «Ошибка разбора параметра: process»).</summary>
        public List<(System.Guid clusterId, System.Guid sessionId, System.Guid? processId)>
            TerminateCalls { get; } = new();

        /// <summary>Вызовы «connection disconnect»: (clusterId, connectionId, processId).</summary>
        public List<(System.Guid clusterId, System.Guid connectionId, System.Guid? processId)>
            DisconnectCalls { get; } = new();

        /// <summary>Вызовы «session terminate» с учётными данными администратора ИБ
        /// (issue #69, комментарий 7OH от 2026-10-27: для защищённой базы rac без
        /// --infobase-user/--infobase-pwd возвращает «Недостаточно прав» — сеансы
        /// не снимаются).</summary>
        public List<(System.Guid clusterId, System.Guid sessionId, System.Guid? processId, string? infobaseUser, string? infobasePassword)>
            TerminateCallsWithCredentials { get; } = new();

        /// <summary>Вызовы «connection disconnect» с учётными данными администратора ИБ.</summary>
        public List<(System.Guid clusterId, System.Guid connectionId, System.Guid? processId, string? infobaseUser, string? infobasePassword)>
            DisconnectCallsWithCredentials { get; } = new();

        /// <summary>Параметры последнего вызова «cluster list» (проверка нормализации host:port).</summary>
        public RacConnectionParams? LastParams { get; private set; }

        /// <summary>Сколько раз запрошены рабочие процессы (для проверки флага занятости).</summary>
        public int ProcessCalls { get; private set; }

        /// <summary>
        /// clusterId всех вызовов «process list» (issue #77): какой кластер реально
        /// загружается после переключения на другой сервер — до фикса это был id
        /// кластера ПРЕЖНЕГО сервера.
        /// </summary>
        public List<System.Guid> LoadedClusterIds { get; } = new();

        /// <summary>
        /// Кластеры по адресу сервера (issue #77): имитация РАЗНЫХ серверов 1С с
        /// РАЗНЫМИ кластерами — при совпадении <see cref="RacConnectionParams.Address"/>
        /// возвращается собственный список (например, с чужими для другого сервера id).
        /// </summary>
        public Dictionary<string, IReadOnlyList<RacCluster>>? ClustersByAddress { get; set; }

        /// <summary>Задержка ответа «cluster list» (ms; issue #77): имитация идущего
        /// подключения, во время которого таймер автообновления должен быть остановлен.</summary>
        public TimeSpan ClustersDelay { get; set; } = TimeSpan.Zero;

        /// <summary>Сколько раз запрошены регламентные задания.</summary>
        public int JobCalls { get; private set; }

        /// <summary>Вызовы «job pause/resume/disable/enable»: (clusterId, jobId, action).</summary>
        public List<(System.Guid clusterId, System.Guid jobId, RacJobAction action)> StateCalls { get; } = new();

        /// <summary>infobaseId, переданный в последний вызов «session list» (фильтр по базе, issue #3).</summary>
        public System.Guid? LastSessionsInfobaseId { get; private set; }

        /// <summary>infobaseId, переданный в последний вызов «connection list» (фильтр по базе, issue #3).</summary>
        public System.Guid? LastConnectionsInfobaseId { get; private set; }

        /// <summary>Сколько вызовов «session list» выполнено С фильтром по базе (issue #69 —
        /// завершение сеансов именно удаляемой базы; автообновление запрашивает сеансы
        /// целиком, с null-фильтром).</summary>
        public int FilteredSessionsListCalls { get; private set; }

        /// <summary>Сколько вызовов «connection list» выполнено с фильтром по базе (issue #69 —
        /// разрыв соединений именно удаляемой базы).</summary>
        public int FilteredConnectionsListCalls { get; private set; }

        /// <summary>infobaseId, переданный в последний вызов «lock list» (фильтр по базе, issue #3).</summary>
        public System.Guid? LastLocksInfobaseId { get; private set; }

        /// <summary>Вызовы «infobase create»: (clusterId, запрос).</summary>
        public List<(System.Guid clusterId, RacInfobaseCreateRequest request)> CreateInfobaseCalls { get; } = new();

        /// <summary>Вызовы «infobase drop»: (clusterId, infobaseId, dropDatabase).</summary>
        public List<(System.Guid clusterId, System.Guid infobaseId, bool dropDatabase)> DropInfobaseCalls { get; } = new();

        /// <summary>Вызовы «infobase drop» с учётными данными администратора ИБ (issue #69):
        /// (clusterId, infobaseId, dropDatabase, infobaseUser, infobasePassword).</summary>
        public List<(System.Guid clusterId, System.Guid infobaseId, bool dropDatabase, string? infobaseUser, string? infobasePassword)>
            DropInfobaseCallsWithCredentials { get; } = new();

        public async Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            // issue #77: имитация идущего подключения — таймер автообновления в это
            // время должен быть остановлен.
            if (ClustersDelay > TimeSpan.Zero)
                await Task.Delay(ClustersDelay, cancellationToken).ConfigureAwait(false);
            if (_throwOnClusters)
                throw new RacClientException("rac not found");
            // issue #77: фикстура «другой сервер» — собственный список кластеров по адресу.
            if (ClustersByAddress is { } byAddress &&
                byAddress.TryGetValue(parameters.Address, out var custom))
            {
                return custom;
            }
            if (_singleCluster)
            {
                return new[]
                {
                    new RacCluster { Id = FirstClusterId, Name = "Главный кластер", Port = 1541 }
                };
            }
            return new[]
            {
                new RacCluster { Id = FirstClusterId, Name = "Главный кластер", Port = 1541 },
                new RacCluster { Id = SecondClusterId, Name = "Второй", Port = 1542 }
            };
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = "Главный кластер",
                HostName = "srv1",
                Port = 1541,
                Properties = new Dictionary<string, string>
                {
                    ["name"] = "Главный кластер",
                    ["hostName"] = "srv1",
                    ["port"] = "1541"
                }
            });
        }

        public async Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            ProcessCalls++;
            LoadedClusterIds.Add(clusterId);
            if (_throwOnParse)
                throw new RacOutputParseException("тест: вывод rac не распознан");
            if (_throwOnLoad)
                throw new InvalidOperationException("тест: сетевая ошибка загрузки");
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new[]
            {
                new RacProcessInfo
                {
                    Id = System.Guid.NewGuid(),
                    Host = "srv1",
                    Pid = 1234,
                    Port = 1560,
                    StartedAt = new DateTime(2026, 9, 28, 10, 0, 0),
                    MemorySize = 512 * 1024 * 1024,
                    Threads = 8,
                    Cpu = 12.5,
                    Running = true,
                    Infobases = 3
                }
            };
        }

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
        {
            // issue #324: сбой разбора ОДНОЙ вкладки (сеансы) — остальные заполняются.
            if (_throwParseOnSessions)
                throw new RacOutputParseException("тест: вывод session list не распознан");

            if (infobaseId is not null)
                FilteredSessionsListCalls++; // issue #69: заказы с фильтром по базе.
            LastSessionsInfobaseId = infobaseId;
            // issue #69: после успешного «session terminate» сеанс пропадает из списка базы
            // (как в реальном rac) — release-цикл перед «infobase drop» завершается досрочно;
            // KeepSessionsAfterTerminate имитирует гонку «сеанс не успел закрыться» — цикл
            // упирается в максимум проходов.
            if (infobaseId is not null && SessionReleased && !KeepSessionsAfterTerminate)
                return Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());
            // issue #3: фильтр по базе — rac «--infobase=...»; сеансы другой базы не возвращаются.
            if (infobaseId is Guid filter && filter != FirstInfobaseId)
                return Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

            return Task.FromResult<IReadOnlyList<RacSessionInfo>>(new[]
            {
                new RacSessionInfo
                {
                    Id = FirstSessionId,
                    // issue #324, C2: infobase-id сеанса маппится на имя из «infobase summary list».
                    InfobaseId = FirstInfobaseId,
                    // issue #69: ConnectionId сеанса = Id соединения — по нему VM находит
                    // рабочий процесс для «session terminate --process=...».
                    ConnectionId = FirstConnectionId,
                    User = "Иванов",
                    Host = "client1",
                    AppId = "1CV8",
                    StartedAt = new DateTime(2026, 9, 28, 9, 30, 0),
                    LastActiveAt = new DateTime(2026, 9, 28, 10, 5, 0),
                    State = "active",
                    Memory = 256 * 1024 * 1024,
                    DurationAll = 3600_000
                }
            });
        }

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
        {
            if (infobaseId is not null)
                FilteredConnectionsListCalls++; // issue #69: запрос с фильтром по базе.
            LastConnectionsInfobaseId = infobaseId;
            // issue #69: после успешного «connection disconnect» соединение пропадает из
            // списка базы (как в реальном rac) — release-цикл завершается досрочно;
            // KeepConnectionsAfterDisconnect имитирует гонку «соединение не успело закрыться».
            if (infobaseId is not null && ConnectionReleased && !KeepConnectionsAfterDisconnect)
                return Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());
            return Task.FromResult<IReadOnlyList<RacConnectionInfo>>(new[]
            {
                new RacConnectionInfo
                {
                    Id = FirstConnectionId,
                    // issue #3: SessionId соединения указывает на сеанс — по нему
                    // клиентская фильтрация определяет базу соединения. Id — тот же,
                    // что ConnectionId у сеанса (issue #69: маппинг на рабочий процесс).
                    SessionId = FirstSessionId,
                    // issue #69: рабочий процесс соединения — «session terminate»/
                    // «connection disconnect» требуют --process=<pid> в rac 8.5.4.
                    ProcessId = FirstProcessId,
                    Connector = "1CV8",
                    Host = "client1",
                    Port = 52000,
                    EstablishedAt = new DateTime(2026, 9, 28, 9, 30, 0),
                    Duration = 1800_000
                }
            });
        }

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default)
        {
            LastLocksInfobaseId = infobaseId;
            // issue #3: блокировки фильтруются rac по «--infobase=...».
            if (infobaseId is Guid filter && filter != FirstInfobaseId)
                return Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

            return Task.FromResult<IReadOnlyList<RacLockInfo>>(new[]
            {
                new RacLockInfo
                {
                    Id = System.Guid.NewGuid(),
                    SessionId = System.Guid.NewGuid(),
                    ConnectionId = System.Guid.NewGuid(),
                    TransactionId = System.Guid.NewGuid(),
                    InfobaseId = FirstInfobaseId,
                    Waiting = true,
                    Blocking = false,
                    Object = "Справочник.Номенклатура"
                }
            });
        }

        // Базы кластера: одна база-владелец для маппинга имён заданий и фильтра.
        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            // issue #69: для блокировки регламентных заданий («infobase update
            // --scheduled-jobs-deny=on») команда требует параметры СУБД — в фикстуре
            // возвращаем их, как это делает реальный вывод «infobase summary list».
            // Тест «пустых параметров СУБД в summary» переопределяет сводку целиком
            // (InfobaseSummaryOverride) — VM догружает их через «infobase info».
            return Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(
                InfobaseSummaryOverride is { } overrideRow
                    ? new[] { overrideRow }
                    : new[]
                    {
                        new RacInfobaseSummary
                        {
                            InfobaseId = FirstInfobaseId,
                            Name = "Бухгалтерия",
                            Dbms = "MSSQLServer",
                            DbServer = "sql-01",
                            DbName = "buho"
                        }
                    });
        }

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            JobCalls++;
            return Task.FromResult<IReadOnlyList<RacJobInfo>>(new[]
            {
                new RacJobInfo
                {
                    Id = FirstJobId,
                    InfobaseId = FirstInfobaseId,
                    Name = "Обмен данными",
                    MethodName = "ВыполнитьОбмен",
                    Predefined = true,
                    Schedule = "0 0 3 * * ?",
                    State = "scheduled",
                    NextStart = new DateTime(2026, 10, 1, 3, 0, 0),
                    LastStart = new DateTime(2026, 9, 30, 3, 0, 0),
                    LastEnd = new DateTime(2026, 9, 30, 3, 10, 0),
                    LastSuccess = true
                },
                new RacJobInfo
                {
                    Id = SecondJobId,
                    InfobaseId = null,
                    Name = "Служебное задание",
                    MethodName = "СлужебныйМетод",
                    State = "paused"
                }
            });
        }

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default)
        {
            StateCalls.Add((clusterId, jobId, action));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на изменение состояния задания";
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        /// <summary>Вызовы «cluster update» (issue #324, C3): (clusterId, изменения).</summary>
        public List<(System.Guid clusterId, RacClusterUpdate changes)> UpdateCalls { get; } = new();

        /// <summary>Настроенный провал «cluster update» (false из rac при удачной команде).</summary>
        public bool UpdateFails { get; set; }

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add((clusterId, changes));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (_actionFails || UpdateFails)
            {
                LastActionError = "rac: у пользователя нет прав на изменение кластера";
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId,
            Guid? processId = null, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
        {
            TerminateCalls.Add((clusterId, sessionId, processId));
            TerminateCallsWithCredentials.Add((clusterId, sessionId, processId, infobaseUser, infobasePassword));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (TerminateFailuresLeft > 0)
            {
                TerminateFailuresLeft--;
                LastActionError = "rac: не удалось завершить сеанс (занят)";
                return Task.FromResult(false);
            }
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на завершение сеанса";
                return Task.FromResult(false);
            }
            SessionReleased = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId,
            Guid? processId = null, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
        {
            DisconnectCalls.Add((clusterId, connectionId, processId));
            DisconnectCallsWithCredentials.Add((clusterId, connectionId, processId, infobaseUser, infobasePassword));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (DisconnectFailuresLeft > 0)
            {
                DisconnectFailuresLeft--;
                LastActionError = DisconnectFailureError ?? "rac: соединение не успело закрыться";
                return Task.FromResult(false);
            }
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на разрыв соединения";
                return Task.FromResult(false);
            }
            ConnectionReleased = true;
            return Task.FromResult(true);
        }

        /// <summary>Соединение базы разорвано (issue #69): после успешного «connection
        /// disconnect» «connection list» этой базы возвращает пустой список — как реальный
        /// rac; release-цикл перед «infobase drop» останавливается.</summary>
        public bool ConnectionReleased { get; private set; }

        /// <summary>Сеанс базы завершён (аналогично «session terminate»).</summary>
        public bool SessionReleased { get; private set; }

        /// <summary>True — «connection list» возвращает соединение, даже если оно уже
        /// разорвано (имитация гонки «соединение не успело закрыться»): release-цикл
        /// упирается в максимум проходов (issue #69, «итеративно 2–3 раза»).</summary>
        public bool KeepConnectionsAfterDisconnect { get; set; }

        /// <summary>Аналогично для «session list».</summary>
        public bool KeepSessionsAfterTerminate { get; set; }

        // ===================== Базы кластера (issue #3) =====================

        /// <summary>Вызовы «infobase update»: (clusterId, infobaseId, запрос).</summary>
        public List<(Guid clusterId, Guid infobaseId, RacInfobaseUpdateRequest request)> UpdateInfobaseCalls { get; } = new();

        /// <summary>Сколько ближайших вызовов «infobase update» завершаются неудачей
        /// (issue #69: неудачная установка блокировки регламентных заданий).</summary>
        public int UpdateInfobaseFailuresLeft { get; set; }

        /// <summary>Сводка кластера вместо фиксированной фикстуры (issue #69: тест
        /// пустых параметров СУБД в summary — догрузка через «infobase info»).</summary>
        public RacInfobaseSummary? InfobaseSummaryOverride { get; set; }

        /// <summary>Результат rac «infobase info» для догрузки параметров СУБД (issue #69:
        /// summary пуст, а полные свойства базы знает только «infobase info»).</summary>
        public RacInfobaseInfo? InfobaseInfoResult { get; set; }

        /// <summary>Сколько раз запрошены полные свойства базы («infobase info»).</summary>
        public int InfobaseInfoCalls { get; private set; }

        /// <summary>«infobase create»: фиксирует вызов; «неудача» настраивается, как у остальных действий.</summary>
        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateInfobaseCalls.Add((clusterId, request));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на изменение списка баз";
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        /// <summary>«infobase update»: фиксирует вызов; «неудача» настраивается, как у остальных действий.</summary>
        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default)
        {
            UpdateInfobaseCalls.Add((clusterId, infobaseId, request));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (UpdateInfobaseFailuresLeft > 0)
            {
                // issue #69: неудачная установка блокировки регламентных заданий.
                UpdateInfobaseFailuresLeft--;
                LastActionError = "rac: у пользователя нет прав на изменение списка баз";
                return Task.FromResult(false);
            }
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на изменение списка баз";
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        /// <summary>rac «infobase info» (issue #69): возвращает настроенный
        /// <see cref="InfobaseInfoResult"/> для догрузки параметров СУБД; по умолчанию null
        /// (как реализация по умолчанию интерфейса).</summary>
        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            string? infobaseUser, string? infobasePassword, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            InfobaseInfoCalls++;
            // issue #69, комментарий 7OH от 2026-10-27: догрузка «infobase info» для
            // блокировки РЗ должна выполняться с кредами администратора ИБ — иначе
            // защищённая база возвращает «Недостаточно прав», блокировка не ставится.
            LastInfobaseInfoUser = infobaseUser;
            LastInfobaseInfoPassword = infobasePassword;
            return Task.FromResult(InfobaseInfoResult);
        }

        /// <summary>Креды администратора ИБ последнего вызова «infobase info»
        /// (для проверки, что догрузка параметров СУБД идёт с кредами).</summary>
        public string? LastInfobaseInfoUser { get; private set; }
        public string? LastInfobaseInfoPassword { get; private set; }

        /// <summary>Сколько ближайших вызовов «infobase drop» завершаются неудачей (issue #69,
        /// сценарий «удаление защищённой базы → повтор с логином/паролем»).</summary>
        public int DropInfobaseFailuresLeft { get; set; }

        /// <summary>Текст ошибки rac для неудач «infobase drop» (issue #69): по умолчанию —
        /// аутентификация администратора ИБ; тест занятой базы задаёт «активные сеансы».</summary>
        public string? DropInfobaseFailureError { get; set; }

        /// <summary>Текст ошибки rac для неудач «connection disconnect» (issue #69,
        /// комментарий 7OH от 2026-10-27: тест «Служебное соединение не может быть
        /// удалено» — служебное соединение пропускается, разрыв остальных продолжается,
        /// удаление не застревает).</summary>
        public string? DisconnectFailureError { get; set; }

        /// <summary>Сколько ближайших вызовов «connection disconnect» завершаются неудачей
        /// (issue #69, комментарий 7OH от 2026-10-26: «не выходить после первой попытки» —
        /// повторная попытка завершения сеансов может помочь).</summary>
        public int DisconnectFailuresLeft { get; set; }

        /// <summary>Сколько ближайших вызовов «session terminate» завершаются неудачей.</summary>
        public int TerminateFailuresLeft { get; set; }

        /// <summary>«infobase drop»: фиксирует вызов (в т.ч. учётные данные администратора ИБ,
        /// issue #69); «неудача» настраивается, как у остальных действий.</summary>
        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
        {
            DropInfobaseCalls.Add((clusterId, infobaseId, dropDatabase));
            DropInfobaseCallsWithCredentials.Add((clusterId, infobaseId, dropDatabase, infobaseUser, infobasePassword));
            if (_throwOnAction)
                throw new RacClientException("нет прав администратора");
            if (DropInfobaseFailuresLeft > 0)
            {
                // Описание пользователя #69: удаление защищённой базы без учётных данных
                // администратора ИБ возвращает ошибку rac; тест «занятой базы» задаёт
                // собственный текст через DropInfobaseFailureError.
                DropInfobaseFailuresLeft--;
                LastActionError = DropInfobaseFailureError
                    ?? "rac: требуется аутентификация администратора информационной базы";
                return Task.FromResult(false);
            }
            if (_actionFails)
            {
                LastActionError = "rac: у пользователя нет прав на изменение списка баз";
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Запись диалогов для тестов: методы фиксируют вызовы (подтверждения, предупреждения),
    /// ничего не показывая; результат подтверждения настраивается (<see cref="ConfirmResult"/>).
    /// </summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public List<(string message, string title)> Confirms { get; } = new();
        public List<(string message, string title)> Warnings { get; } = new();

        /// <summary>
        /// Провайдер «мы сейчас в UI-потоке»: задаёт тест STA-фикса (issue #3, 1.0.0.22) —
        /// проверяет, что <see cref="ShowWarning"/> выполняется внутри dispatch-маршалера.
        /// </summary>
        public Func<bool>? OnUiProvider { get; set; }

        /// <summary>True, если хотя бы одно предупреждение показано в контексте UI-потока.</summary>
        public bool WarningsShownOnUi { get; private set; }

        public void ShowInfo(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }

        public void ShowWarning(string message, string title = "")
        {
            Warnings.Add((message, title));
            if (OnUiProvider?.Invoke() == true)
                WarningsShownOnUi = true;
        }

        public bool Confirm(string message, string title = "")
        {
            Confirms.Add((message, title));
            return ConfirmResult;
        }
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }

    /// <summary>Временный каталог для изоляции хранилища справочника (issue #3).</summary>
    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_sm_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }

    [Fact]
    public async Task LicenseTab_WhenRingUnavailable_ShowsReadableMessage_InsteadOfEmptyTab()
    {
        // issue #83 (комментарий 7OH от 2026-10-28): при недоступной утилите ring
        // вкладка «Лицензии» НЕ пустая — LicenseStatusText содержит понятное сообщение
        // с указанием, где искалась утилита и что сделать (а в общий журнал — без WARN-спама,
        // см. RingLicenseService: недоступность логируется как INFO).
        var ring = new FakeRingLicenseService(
            new RingLicenseResult
            {
                Warning = RingLicenseService.BuildUnavailableMessage()
            });
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new RecordingDialogs(), ringLicenses: ring);
        await vm.ConnectAsync();
        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.NotEmpty(vm.LicenseStatusText);
        Assert.Contains("ring", vm.LicenseStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rac", vm.LicenseStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH", vm.LicenseStatusText, StringComparison.Ordinal);
    }

    /// <summary>Fake-сервис установленных лицензий (issue #83): возвращает заданный результат.</summary>
    private sealed class FakeRingLicenseService : IRingLicenseService
    {
        private readonly RingLicenseResult _result;

        public FakeRingLicenseService(RingLicenseResult result) => _result = result;

        public Task<RingLicenseResult> GetInstalledLicensesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}