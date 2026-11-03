using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты вью-модели окна «Миграция между кластерами 1С» (issue #41, переработка по
/// комментарию 7OH): двухпанельная раскладка «Источник»/«Приёмник» (сервера не
/// пересекаются — проверка по выбору, из списков не удаляются), загрузка кластеров
/// с автовыбором единственного, «Получить базы» (ВСЕ галочки), «Получить статусы»
/// («Есть в кластере»/пусто по совпадению имени; галочки остаются только там, где
/// базы нет в приёмнике), доступность «Мигрировать» по галочкам и статусам, перенос
/// (create в приёмнике + drop из источника при снятой «Не удалять в старом»), добавление
/// баз в новую группу (имя кластера — приёмник) и формирование результата.
/// </summary>
public sealed class ClusterMigrationViewModelTests
{
    [Fact]
    public void Ctor_KeepsAllServersInBothPanels()
    {
        var source = BuildServer("src", "srv-old");
        var target = BuildServer("tgt", "srv-new");

        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(),
            new ManagementServer[] { source, target });

        // Сервера НЕ удаляются из списков (issue #41): оба сервера в обеих панелях.
        Assert.Equal(2, vm.AvailableSourceServers.Count);
        Assert.Equal(2, vm.AvailableTargetServers.Count);
        Assert.True(vm.HasAvailableServers);
        Assert.False(vm.IsBusy);
        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
        Assert.False(vm.ServersOverlap);
    }

    [Fact]
    public async Task SourceServerChange_LoadsClusters_SingleClusterAutoSelected()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });

        vm.SelectedSourceServer = vm.AvailableSourceServers[0];

        await AwaitSourceClusters(vm);

        Assert.Single(vm.SourceClusters);
        Assert.Equal(FakeRacClient.SourceClusterId, vm.SelectedSourceClusterId); // автовыбор единственного
        Assert.Equal("srv-old", client.LastParams!.Address);
        Assert.Empty(vm.SourceError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task TargetServerChange_LoadsClusters_SingleClusterAutoSelected()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("tgt", "srv-new") });

        vm.SelectedTargetServer = vm.AvailableTargetServers[0];

        await AwaitTargetClusters(vm);

        Assert.Single(vm.TargetClusters);
        Assert.Equal(FakeRacClient.TargetClusterId, vm.SelectedTargetClusterId);
        Assert.Equal("srv-new", client.LastParams!.Address);
        Assert.Empty(vm.TargetError);
    }

    [Fact]
    public async Task LoadSourceBases_BuildsRows_ChecksAll()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal(2, vm.SourceRows.Count);
        // После получения баз источника — ВСЕ галочки (issue #41).
        Assert.All(vm.SourceRows, r => Assert.True(r.IsChecked));
        Assert.Equal(2, vm.CheckedCount);
        Assert.False(vm.HasCheckedInTarget);
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedBasesFormat"), 2),
            vm.SourceStatusText);
    }

    [Fact]
    public async Task LoadSourceBases_MarksDuplicatesInSourceStatus()
    {
        var existing = new Infobase
        {
            Name = "База1",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-old",
                Port = 1542, // порт кластера-источника из FakeRacClient
                DatabaseName = "База1"
            }
        };
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, new[] { existing }, new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        var row = vm.SourceRows.First(r => r.Name == "База1");
        Assert.Equal(LocalizationManager.T("ClusterImport.AlreadyExists"), row.SourceStatus);
        // Статус источника не мешает переносу — галочка стоит.
        Assert.True(row.IsChecked);
    }

    [Fact]
    public async Task LoadSourceBases_SortsRowsByName_OrdinalIgnoreCase()
    {
        // Issue #41, комментарий 7OH от 2026-10-26 «сортировать по имени сразу»: список
        // баз источника ранжируется по имени при чтении. StringComparer.OrdinalIgnoreCase —
        // стабилен для кириллицы и не зависит от региональных настроек: латиница идёт
        // до кириллицы по кодовым точкам, регистр не влияет.
        var client = new FakeRacClient(sourceBaseNames: new[] { "бета", "Alpha", "Альфа", "beta", "Гамма" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal(5, vm.SourceRows.Count);
        Assert.Equal(
            new[] { "Alpha", "beta", "Альфа", "бета", "Гамма" },
            vm.SourceRows.Select(r => r.Name).ToArray());
        // После сортировки галочки — ВСЕ (как при несортированном списке).
        Assert.All(vm.SourceRows, r => Assert.True(r.IsChecked));
        Assert.Equal(5, vm.CheckedCount);
    }

    [Fact]
    public async Task GetTargetStatuses_SetsInTarget_UnchecksExisting()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "База1" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        Assert.Equal(2, vm.CheckedCount);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);

        await vm.GetTargetStatusesAsync();

        // «Есть в кластере» по совпадению имени; галочка снята у существующей в приёмнике.
        var inTarget = vm.SourceRows.First(r => r.Name == "База1");
        var free = vm.SourceRows.First(r => r.Name == "База2");
        Assert.Equal(LocalizationManager.T("ClusterMigration.Status.InTarget"), inTarget.TargetStatus);
        Assert.Equal(string.Empty, free.TargetStatus);
        Assert.False(inTarget.IsChecked);
        Assert.True(free.IsChecked);
        Assert.Equal(1, vm.CheckedCount);
        // Отмеченных строк со статусом «Есть в кластере» нет — «Мигрировать» доступна.
        Assert.False(vm.HasCheckedInTarget);
        Assert.True(vm.CanMigrate);

        // Пользователь вручную отмечает базу, которая есть в приёмнике — кнопка недоступна.
        inTarget.IsChecked = true;
        Assert.True(vm.HasCheckedInTarget);
        Assert.False(vm.CanMigrate);
        Assert.NotEqual(string.Empty, vm.MigrateHint);
    }

    [Fact]
    public async Task GetTargetStatuses_NoMatches_KeepsAllChecked()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "СовсемДругаяБаза" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        Assert.All(vm.SourceRows, r => Assert.Equal(string.Empty, r.TargetStatus));
        Assert.All(vm.SourceRows, r => Assert.True(r.IsChecked));
        Assert.False(vm.HasCheckedInTarget);
    }

    [Fact]
    public void ServersOverlap_WhenSameServerSelectedInBothPanels()
    {
        var server = BuildServer("srv", "srv-1");
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), new[] { server });

        vm.SelectedSourceServer = server;
        vm.SelectedTargetServer = server;

        Assert.True(vm.ServersOverlap);
        Assert.False(vm.CanMigrate);
        Assert.NotEqual(string.Empty, vm.MigrateHint);
    }

    [Fact]
    public async Task Migrate_CreatesInTarget_DropsFromSource_AddsToGroup()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.DbUser = "db-user";
        vm.DbPassword = "db-pass";
        vm.KeepInSource = false;
        vm.UseClusterGrouping = true;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Equal(2, vm.Result!.MigratedBaseNames.Count);
        Assert.Equal(new[] { "База1", "База2" }, vm.Result.MigratedBaseNames);
        Assert.Equal("srv-new-host", vm.Result.NewServer);   // hostName из «cluster info»
        Assert.Equal(1542, vm.Result.NewPort);               // порт кластера приёмника

        // Базы добавляются в новую группу с именем «хост - кластер (порт)», когда порт
        // приёмника НЕ стандартный (issue #41: 1542 ≠ 1541 → «(1542)»; иначе папки «Новый
        // кластер» разных серверов сливались бы в одну).
        Assert.Equal(2, vm.Result.AddedInfobases.Count);
        Assert.All(vm.Result.AddedInfobases, b => Assert.Equal("srv-new-host - Новый кластер (1542)", b.Group));
        Assert.All(vm.Result.AddedInfobases, b => Assert.Equal("srv-new-host", b.Connection!.Server));

        // Каждая база: create в приёмнике + drop из источника (dropDatabase=false).
        Assert.Equal(2, client.Created.Count);
        Assert.Equal(2, client.Dropped.Count);
        Assert.All(client.Created, c => Assert.Equal(FakeRacClient.TargetClusterId, c.ClusterId));
        Assert.All(client.Dropped, d => Assert.False(d.DropDatabase));
        Assert.Equal("db-user", client.Created[0].Request.DbUser);
        Assert.Equal("db-pass", client.Created[0].Request.DbPassword);
        Assert.Equal("MSSQLServer", client.Created[0].Request.Dbms);
    }

    [Fact]
    public async Task Migrate_CopiesSourceLockFlags_FromSummaryToCreatedRequest()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «при переносе/копировании — копировать
        // флаги блокировок». Блочный формат «infobase summary list» источника отдал
        // scheduled-jobs-deny/sessions-deny — они передаются в rac «infobase create»
        // приёмника; «infobase info» не требуется.
        var client = new FakeRacClient(sourceScheduledJobsDeny: true, sourceSessionsDeny: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.DbUser = "db-user";
        vm.DbPassword = "db-pass";
        vm.KeepInSource = false;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.False(vm.IsBusy);
        Assert.Equal(2, client.Created.Count);
        // Флаги блокировок копируются из базы-источника в запрос создания приёмника.
        Assert.All(client.Created, c => Assert.True(c.Request.ScheduledJobsDeny));
        Assert.All(client.Created, c => Assert.True(c.Request.SessionsDeny));
        // Сводка флаги отдала — догрузка через «infobase info» не потребовалась.
        Assert.Equal(0, client.InfobaseInfoCalls);
    }

    [Fact]
    public async Task Migrate_CopiesSourceLockFlags_FromInfobaseInfo_WhenSummaryLacks()
    {
        // issue #69: «Если флаги недоступны в summary — догружать через infobase info».
        // Табличный формат источника флагов не отдал (null) — VM запрашивает полные
        // свойства базы и передаёт фактические флаги в запрос создания приёмника.
        var client = new FakeRacClient
        {
            InfobaseInfoResult = new RacInfobaseInfo
            {
                Name = "База1",
                ScheduledJobsDeny = true,
                SessionsDeny = true
            }
        };
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.DbUser = "db-user";
        vm.DbPassword = "db-pass";
        vm.KeepInSource = false;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(2, client.Created.Count);
        Assert.All(client.Created, c => Assert.True(c.Request.ScheduledJobsDeny));
        Assert.All(client.Created, c => Assert.True(c.Request.SessionsDeny));
        Assert.True(client.InfobaseInfoCalls >= 1);
    }

    [Fact]
    public async Task Migrate_KeepInSource_CreateOnly_NoDrop()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.KeepInSource = true; // «Не удалять в старом» — копия без удаления

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(2, client.Created.Count); // обе базы источника копируются
        Assert.Empty(client.Dropped);
    }

    [Fact]
    public async Task Migrate_NoGrouping_AddsBasesWithoutGroup()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.UseClusterGrouping = false;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.All(vm.Result!.AddedInfobases, b => Assert.Equal(string.Empty, b.Group));
    }

    [Fact]
    public async Task Migrate_Progress_UpdatesIndexTotalCurrentBase()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.Equal(2, vm.ProgressTotal);
        Assert.Equal(2, vm.ProgressIndex);
        Assert.Equal("База2", vm.CurrentBaseName);
        Assert.NotEmpty(vm.ProgressText);
    }

    [Fact]
    public async Task Migrate_Progress_ShowsEachBaseNameInTurn_IncludingFailedSecond()
    {
        // Issue #41, последний комментарий 7OH от 2026-10-27: «Во время обработки 2-й,
        // которой нет в сервере СУБД — показывает данные первой базы. … Во время начала
        // обработки 3-й — была надпись 2-й, и в конце появилась 3-я». Причина «сдвига на
        // предыдущую базу»: CurrentBaseName/ProcessedCount НЕ оповещали о смене
        // ProgressText — окно (WPF и Avalonia подписаны на ProgressText → UpdateCommandStates
        // читает vm.ProgressText) пересобирало строку «N из M: <база>» при смене СЧЁТЧИКА
        // (ProgressIndex поднимает ProgressText), когда CurrentBaseName ещё содержал имя
        // ПРЕДЫДУЩЕЙ базы; смена имени события ProgressText не поднимала, и корректное имя
        // появлялось только на следующем оповещении (следующая итерация / сброс занятости).
        // Регресс: каждое изменение имени/счётчика/итога обязано поднимать ProgressText —
        // в последовательности оповещений присутствуют пары (счётчик, имя) для КАЖДОЙ
        // обрабатываемой базы, включая вторую с ошибкой создания.
        var client = new FakeRacClient(sourceBaseCount: 3, failCreateOnIndex: 2);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        var snapshots = new List<(int Index, int Total, string Name)>();
        vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ClusterMigrationViewModel.ProgressText), StringComparison.Ordinal))
                snapshots.Add((vm.ProgressIndex, vm.ProgressTotal, vm.CurrentBaseName));
        };

        await vm.MigrateAsync();

        Assert.Equal(3, vm.ProgressTotal);
        // Для каждой итерации есть оповещение ProgressText с УЖЕ НОВЫМ именем базы:
        // без фикса окно получало «N из M» со старым именем («2 из 3: База1», «3 из 3:
        // База2») и имени текущей базы не видело до конца прогона.
        Assert.Contains(snapshots, s => s.Index == 1 && s.Name == "База1");
        Assert.Contains(snapshots, s => s.Index == 2 && s.Name == "База2");
        Assert.Contains(snapshots, s => s.Index == 3 && s.Name == "База3");
        // Вторая база обработана с ошибкой (пример из комментария — «которой нет в
        // сервере СУБД»), но её имя в строке прогресса появилось в свой ход.
        Assert.NotEmpty(vm.SourceRows.First(r => r.Name == "База2").MigrationStatus);
        Assert.True(vm.MigrationCompleted);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_FirstBaseNameShown_WhileFirstOperationInProgress()
    {
        // Issue #41, комментарий 7OH от 2026-10-26 «Первая без имени — просто "1 из 3"»:
        // строка «1 из K: <Имя>» устанавливается НА СТАРТЕ обработки первой базы (до
        // rac-операции), а не после её завершения. Воспроизведение: «infobase create»
        // заморожена гейтом — пока операция первой базы ещё НЕ завершена, CurrentBaseName
        // и счётчик «1 из K» уже видны.
        var client = new FakeRacClient
        {
            CreateGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        var createStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnBeforeCreate = () => createStarted.TrySetResult(true);

        var migrate = vm.MigrateAsync();
        await createStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)); // первая операция началась

        // Прогресс первой строки стоит, хотя операция ещё НЕ завершена (гейт закрыт).
        Assert.Equal("База1", vm.CurrentBaseName);
        Assert.Equal(1, vm.ProgressIndex);
        Assert.Equal(2, vm.ProgressTotal);
        Assert.Equal(1, vm.ProcessedCount);
        Assert.False(vm.MigrationCompleted);

        client.CreateGate.SetResult(true); // отпускаем «infobase create»
        await migrate.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(2, vm.Result!.MigratedBaseNames.Count);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_CreateFailure_ContinuesWithNextBase_MarksErrorRow_KeepsCheckmark()
    {
        // Issue #41 (continue-on-error): ошибка создания ПЕРВОЙ базы не прерывает перенос —
        // вторая база обрабатывается; проблемная строка помечается ошибкой и сохраняет
        // галочку, успешная — попадает в результат (в список приложения).
        var client = new FakeRacClient(failCreate: true); // «infobase create» падает на 1-й базе
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        // Обе базы попробованы: первая — ошибка создания, вторая — перенесена.
        Assert.Equal(2, client.Created.Count);
        Assert.Single(client.Dropped); // drop только для успешно созданной второй
        Assert.NotEmpty(vm.ErrorMessage);

        var broken = vm.SourceRows.First(r => r.Name == "База1");
        Assert.NotEmpty(broken.MigrationStatus);   // маркер ошибки на строке
        Assert.True(broken.IsChecked);             // галочка сохранена (база не в приёмнике)
        Assert.Empty(broken.TargetStatus);         // автообновление статусов её не затирало

        var ok = vm.SourceRows.First(r => r.Name == "База2");
        Assert.Empty(ok.MigrationStatus);
        Assert.False(ok.IsChecked);                // перенесена — галочка снята

        // Результат — по фактически успешному списку («в кластере, но не в списке» не бывает).
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База2" }, vm.Result!.MigratedBaseNames);
        Assert.Single(vm.Result.AddedInfobases);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_ThreeBases_ErrorInMiddle_DoesNotStopOthers()
    {
        // Пример пользователя из issue #41: 3 базы — без ошибки, ошибка («в кластере есть,
        // но нет в сервере БД» — create), без ошибки. Ошибка ВТОРОЙ не прерывает: третья
        // обрабатывается; успешные попадают в результат, проблемная остаётся с галочкой.
        var client = new FakeRacClient(sourceBaseCount: 3, failCreateOnIndex: 2);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.Equal(3, vm.SourceRows.Count);
        Assert.Equal(3, client.Created.Count); // все три базы попробованы
        Assert.Equal(2, client.Dropped.Count); // drop для 1-й и 3-й (вторая не создана)
        Assert.NotEmpty(vm.ErrorMessage);

        var broken = vm.SourceRows.First(r => r.Name == "База2");
        Assert.NotEmpty(broken.MigrationStatus);
        Assert.True(broken.IsChecked);
        Assert.Empty(broken.TargetStatus);

        // Успешные добавлены в результат — «в кластере, но не в списке» больше не бывает.
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База1", "База3" }, vm.Result!.MigratedBaseNames);
        Assert.Equal(2, vm.Result.AddedInfobases.Count);

        // Сводка «Перенесено: 2 · Ошибок: 1».
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Summary.ResultFormat"), 2, 1),
            vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_ProgressIndex_IsRunOrdinal_NotFullListPosition()
    {
        // Issue #41 (счётчик, комментарий 7OH): ProgressIndex — порядковый номер
        // обрабатываемой базы в текущем прогоне (1-based), а НЕ позиция в полном списке.
        // Снята первая из трёх — обрабатываются «База2» и «База3»: счётчик «1 из 2»,
        // «2 из 2»; ProgressTotal — количество отмеченных (rows.Count), ProcessedCount
        // — число фактически обработанных (совпадает с ProgressIndex).
        var client = new FakeRacClient(sourceBaseCount: 3);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.SourceRows[0].IsChecked = false; // первая из трёх снята

        await vm.MigrateAsync();

        Assert.Equal(3, vm.SourceRows.Count);       // полный список — 3 строки
        Assert.Equal(2, vm.ProgressTotal);          // отмеченных — 2 (rows.Count)
        Assert.Equal(2, vm.ProgressIndex);          // последняя обработанная — «2 из 2»
        Assert.Equal("База3", vm.CurrentBaseName);
        Assert.Equal(2, vm.ProcessedCount);         // обработано две отмеченные базы
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База2", "База3" }, vm.Result!.MigratedBaseNames);
    }

    [Fact]
    public async Task Migrate_UncheckedFirstBase_FirstProcessedShowsOneOfChecked()
    {
        // Issue #41 (счётчик): при снятой галочке первой базы первая ОБРАБАТЫВАЕМАЯ база
        // показывает «1 из K» (ProgressIndex == 1), а не свою позицию в полном списке
        // («2 из 3»). Значения фиксируются в момент начала обработки первой базы.
        var client = new FakeRacClient(sourceBaseCount: 3);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.SourceRows[0].IsChecked = false; // первая из трёх снята

        var firstIndex = -1;
        var firstTotal = -1;
        var firstName = string.Empty;
        client.OnBeforeCreate = () =>
        {
            if (firstIndex >= 0)
                return; // фиксируем значения только ПЕРВОЙ обрабатываемой базы
            firstIndex = vm.ProgressIndex;
            firstTotal = vm.ProgressTotal;
            firstName = vm.CurrentBaseName;
        };

        await vm.MigrateAsync();

        // Первая обрабатываемая — «База2» (вторая строка списка): «1 из 2», не «2 из 3».
        Assert.Equal(1, firstIndex);
        Assert.Equal(2, firstTotal);
        Assert.Equal("База2", firstName);
        Assert.True(vm.MigrationCompleted);
    }

    [Fact]
    public async Task Migrate_ProgressTotal_IsCheckedRowsCount_NotFullListCount()
    {
        // Issue #41 (счётчик): ProgressTotal — количество ОТМЕЧЕННЫХ баз в текущем прогоне
        // (rows.Count), а не размер полного списка SourceRows.
        var client = new FakeRacClient(sourceBaseCount: 3);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.SourceRows[0].IsChecked = false; // отмечены только 2 из 3

        await vm.MigrateAsync();

        Assert.Equal(3, vm.SourceRows.Count);   // полный список — 3
        Assert.Equal(2, vm.ProgressTotal);      // отмеченных — 2 (rows.Count)
        Assert.Equal(2, vm.ProgressIndex);      // последняя обработанная — «2 из 2»
        Assert.True(vm.MigrationCompleted);
    }

    [Fact]
    public async Task Migrate_SecondRun_ProgressRestartsFromOne_WithCheckedCount()
    {
        // Issue #41 (счётчик): при повторном запуске счётчик начинается заново — первая
        // обрабатываемая база показывает «1 из K», где K — отмеченные на момент запуска
        // (не позиция в полном списке и не остаток предыдущего прогона).
        var client = new FakeRacClient(sourceBaseCount: 3, failCreateOnIndex: 2);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        // Первый прогон: «База1» и «База3» перенесены (галочки сняты автообновлением
        // статусов), «База2» — ошибка (галочка осталась).
        await vm.MigrateAsync();
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(1, vm.CheckedCount);

        // Второй прогон: обрабатывается только «База2» — счётчик «1 из 1», а не «2 из 3».
        var firstIndex = -1;
        var firstTotal = -1;
        client.OnBeforeCreate = () =>
        {
            if (firstIndex >= 0)
                return; // фиксируем значения только ПЕРВОЙ обрабатываемой базы
            firstIndex = vm.ProgressIndex;
            firstTotal = vm.ProgressTotal;
        };

        await vm.MigrateAsync();

        Assert.Equal(1, firstIndex);
        Assert.Equal(1, firstTotal);
        Assert.Equal("База2", vm.CurrentBaseName);
        Assert.True(vm.MigrationCompleted);
    }

    [Fact]
    public async Task Migrate_WithErrors_SecondRun_AccumulatesMigratedBasesInResult()
    {
        // Issue #41, комментарий 7OH от 2026-10-26 «закрывать окно не надо»: окно НЕ
        // закрывается по завершении (в т.ч. при ошибках), чтобы увидеть ошибки и можно
        // было перенести остаток на другой сервер/кластер повторным прогоном. Result
        // НАКАПЛИВАЕТ перенесённые базы за прогоны: после ручного закрытия окна в список
        // приложения попадут все перенесённые базы, а не только последнего прогона.
        var client = new FakeRacClient(sourceBaseCount: 3, failCreateOnIndex: 2);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        // Первый прогон: «База1» и «База3» перенесены, «База2» — ошибка создания.
        await vm.MigrateAsync();
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База1", "База3" }, vm.Result!.MigratedBaseNames);
        Assert.Equal(1, vm.CheckedCount); // ошибка — галочка осталась (переход к другому серверу)

        // Второй прогон: «База2» переносится — результат НАКАПЛИВАЕТСЯ.
        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База1", "База3", "База2" }, vm.Result.MigratedBaseNames);
        Assert.Equal(3, vm.Result.AddedInfobases.Count);
        Assert.Equal(0, vm.CheckedCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_AllRowsFailed_NotCompleted_ErrorsMarked()
    {
        // Issue #41: если не перенеслась НИ ОДНА база — результат не формируется, окно
        // остаётся открытым; строка помечена ошибкой и сохраняет галочку; сводка «0 · Ошибок».
        var client = new FakeRacClient(sourceBaseCount: 1, throwOnCreate: true); // сбой на всех create
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.NotEmpty(vm.SourceRows[0].MigrationStatus);
        Assert.True(vm.SourceRows[0].IsChecked);
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Summary.ResultFormat"), 0, 1),
            vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_CreateFailure_RefreshesTargetStatusesAutomatically()
    {
        // Issue #41: «Если произошла ошибка - стоит обновить статусы в приёмнике автоматически».
        var client = new FakeRacClient(failCreate: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();
        var callsBefore = client.GetInfobasesCalls;

        await vm.MigrateAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        // После ошибки переноса статусы приёмника запрашиваются повторно автоматически.
        Assert.True(client.GetInfobasesCalls > callsBefore,
            "После ошибки миграции статусы приёмника должны обновляться автоматически (issue #41)");
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_PartialFailure_RefreshesTargetStatuses_ShowsCreatedBaseInTarget()
    {
        // Issue #41: ошибка на ВТОРОЙ базе — первая уже создана в приёмнике. Автообновление
        // статусов после ошибки помечает «База1» как «Есть в кластере» и снимает галочку:
        // сразу видно, какие базы реально перенесены, а какие — нет.
        var client = new FakeRacClient(failCreateOnIndex: 2);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        // fire-and-forget обновление статусов в тестах (dispatch = null) выполняется синхронно.
        var created = vm.SourceRows.First(r => r.Name == "База1");
        Assert.Equal(LocalizationManager.T("ClusterMigration.Status.InTarget"), created.TargetStatus);
        Assert.False(created.IsChecked);
        var pending = vm.SourceRows.First(r => r.Name == "База2");
        Assert.Equal(string.Empty, pending.TargetStatus);
        Assert.True(pending.IsChecked);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_CreateThrows_RefreshesTargetStatusesAutomatically()
    {
        // Issue #41: общее исключение в середине переноса (сбой соединения с rac вместо
        // возврата «false») — частичный перенос, статусы приёмника обновляются автоматически
        // и в этом пути (failed выставляется в общем catch MigrateAsync).
        var client = new FakeRacClient(throwOnCreate: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();
        var callsBefore = client.GetInfobasesCalls;

        await vm.MigrateAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        Assert.True(client.GetInfobasesCalls > callsBefore,
            "После исключения миграции статусы приёмника должны обновляться автоматически (issue #41)");
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_DropFailure_Continues_MarksErrorRows_MigratedInResult()
    {
        // Issue #41 (continue-on-error): ошибка удаления из старого кластера НЕ прерывает
        // перенос — строки помечаются ошибкой, но базы УЖЕ созданы в приёмнике и попадают
        // в результат (в список приложения); галочки снимает автообновление статусов
        // («Есть в кластере»).
        var client = new FakeRacClient(failDrop: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.Equal(2, client.Created.Count); // обе базы добавлены в приёмник
        Assert.Equal(2, client.Dropped.Count); // обе попытки удаления из источника
        Assert.NotEmpty(vm.ErrorMessage);

        // Обе строки помечены ошибкой (drop), но базы перенесены — «Есть в кластере»
        // проставлено автообновлением статусов приёмника.
        Assert.All(vm.SourceRows, r => Assert.NotEmpty(r.MigrationStatus));
        Assert.All(vm.SourceRows, r => Assert.Equal(
            LocalizationManager.T("ClusterMigration.Status.InTarget"), r.TargetStatus));

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(new[] { "База1", "База2" }, vm.Result!.MigratedBaseNames);
        Assert.Equal(2, vm.Result.AddedInfobases.Count);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_SameServer_Validates()
    {
        var server = BuildServer("srv", "srv-1");
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), new[] { server });
        vm.SelectedSourceServer = server;
        vm.SelectedTargetServer = server;

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoSourceServer_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), Array.Empty<ManagementServer>());

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoTargetCluster_Validates()
    {
        var client = new FakeRacClient(singleCluster: false); // несколько кластеров — автовыбора нет
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        Assert.Null(vm.SelectedTargetClusterId);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Empty(client.Created);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoBases_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), Array.Empty<ManagementServer>());

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(0, vm.CheckedCount);
    }

    [Fact]
    public async Task SetReplaceConnectionStrings_UpdatesResultFlag()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.False(vm.Result!.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(true);
        Assert.True(vm.Result.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(false);
        Assert.False(vm.Result.ReplaceConnectionStrings);
    }

    [Fact]
    public async Task Migrate_KeepInSource_ResultNotMarkedForReplace_AndCopiesInResult()
    {
        // issue #41 (комментарий 7OH от 2026-10-28): при «Не удалять в старом» (копирование)
        // вопрос «Хотите заменить строку подключения?» не задаётся и результат НЕ помечается
        // заменой — новые базы созданы с корректным подключением, старые строки остались.
        // Сами копии при этом корректно попадают в результат/список (AddedInfobases).
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.KeepInSource = true; // копия без удаления из старого кластера

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(2, vm.Result!.AddedInfobases.Count); // копии добавлены в список
        Assert.Equal(2, vm.Result.MigratedBaseNames.Count);
        Assert.False(vm.Result.ReplaceConnectionStrings); // замену строк при копировании не предлагаем
        Assert.Empty(client.Dropped); // источник не трогаем
    }

    [Fact]
    public async Task Migrate_KeepInSource_AfterConfirmedReplaceOnCopyRun_ResetsReplaceFlag()
    {
        // issue #41: пользователь подтвердил замену строк при ПЕРВОМ прогоне (перенос),
        // затем включил «Не удалять в старом» и мигрировал ещё раз (копирование). Для
        // нового прогона-копии флаг замены строк НЕ сохраняется: старые строки остались
        // в источнике, менять их нечем и незачем.
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        // Первый прогон — перенос, пользователь подтвердил замену строк подключения.
        vm.KeepInSource = false;
        await vm.MigrateAsync();
        Assert.True(vm.MigrationCompleted);
        vm.SetReplaceConnectionStrings(true);
        Assert.True(vm.Result!.ReplaceConnectionStrings);

        // Второй прогон — копирование: флаг сбрасывается, вопрос не нужен (первый прогон
        // снял галочки — повторно отмечаем строки, как это делает пользователь в окне).
        vm.SelectAll();
        vm.KeepInSource = true;
        await vm.MigrateAsync();
        Assert.True(vm.MigrationCompleted);
        Assert.Equal(4, vm.Result.AddedInfobases.Count); // копии добавились к результату
        Assert.False(vm.Result.ReplaceConnectionStrings);
    }

    [Fact]
    public async Task SelectAll_SelectNone_ToggleChecks()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectNone();
        Assert.Equal(0, vm.CheckedCount);
        Assert.False(vm.CanMigrate);

        vm.SelectAll();
        Assert.Equal(2, vm.CheckedCount);
    }

    // ---------- Issue #41: сводка выделения ----------

    [Fact]
    public async Task SelectionSummary_AfterLoad_ShowsCheckedAndToMigrate()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(singleCluster: true), Array.Empty<Infobase>(),
            new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        // После получения баз — ВСЕ галочки: «Выделено: 2 из 2 · будет перенесено: 2».
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"), 2, 2, 2),
            vm.SelectionSummaryText);
    }

    [Fact]
    public async Task ManualUncheck_UpdatesSummary_AndFollowsCheckedCount()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(singleCluster: true), Array.Empty<Infobase>(),
            new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        // Пользователь снимает одну галочку вручную — сводка обновляется через подписку
        // VM на PropertyChanged строки (issue #41), без повторного нажатия «Получить базы».
        vm.SourceRows[0].IsChecked = false;

        Assert.Equal(1, vm.CheckedCount);
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"), 1, 2, 1),
            vm.SelectionSummaryText);

        vm.SourceRows[0].IsChecked = false;
        vm.SourceRows[1].IsChecked = false;
        Assert.Equal(0, vm.CheckedCount);
        // Сводка остаётся информативной («Выделено: 0 из 2 · будет перенесено: 0»),
        // а не пустой: строим ожидание теми же вызовами локализации, что и VM.
        Assert.Equal(
            string.Format(
                LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"), 0, 2, 0),
            vm.SelectionSummaryText);
    }

    [Fact]
    public async Task SelectionSummary_AfterStatuses_ExcludesInTargetBases()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "База1" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(),
            new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        // «База1» уже в приёмнике — галочка снята: «Выделено: 1 из 2 · будет перенесено: 1».
        Assert.Equal(1, vm.CheckedCount);
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"), 1, 2, 1),
            vm.SelectionSummaryText);
        Assert.DoesNotContain(
            LocalizationManager.T("ClusterMigration.Summary.InTargetPending"),
            vm.SelectionSummaryText);
    }

    [Fact]
    public async Task SelectionSummary_HintsPendingWhenCheckedRowIsInTarget()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "База1" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(),
            new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        // Пользователь вручную отмечает базу, которая есть в приёмнике: сводка считает
        // «будет перенесено» только свободные базы и добавляет подсказку (issue #41).
        vm.SourceRows.First(r => r.Name == "База1").IsChecked = true;

        Assert.Equal(2, vm.CheckedCount);
        // Ожидаемая строка собирается теми же вызовами локализации, что и VM, —
        // сравнение детерминировано и в тестовом процессе (словари не загружены,
        // T() возвращает ключ), и в приложении (загруженный перевод).
        var selected = string.Format(
            LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"), 2, 2, 1);
        var pendingHint = LocalizationManager.T("ClusterMigration.Summary.InTargetPending");
        Assert.Equal(selected + " " + pendingHint, vm.SelectionSummaryText);
    }

    // ---------- Issue #41: маршалинг миграции в UI-поток ----------

    [Fact]
    public async Task Migrate_WithUiDispatch_CompletesAndGoesThroughDispatcher()
    {
        // Регресс кросс-потоковой InvalidOperationException (issue #41): окно подписано
        // на PropertyChanged и трогает UI напрямую. VM обязан доставлять обновления
        // прогресса и финальный блок (Result/MigrationCompleted) через dispatch-колбэк.
        var dispatches = 0;
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(),
            new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") },
            action => { dispatches++; action(); }); // синхронный аналог Dispatcher.Post
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.NotNull(vm.Result);
        Assert.Equal(2, vm.Result!.MigratedBaseNames.Count);
        // Прогресс (общий + по итерациям) и финал уходят через диспетчер.
        Assert.True(dispatches >= 3, $"Ожидались dispatch-вызовы, получено {dispatches}");
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ErrorMessage);
    }

    // ===================== Issue #41: автоподстановка сервера =====================

    [Fact]
    public void SetInitialSource_WithServerIdInList_SelectsInstanceFromList()
    {
        // ManagementServer — class без переопределённого Equals: окно «Серверы 1С» передаёт
        // ДРУГОЙ экземпляр с тем же Id, и ComboBox не находил SelectedItem. Сопоставление
        // по Id должно выбрать запись ИЗ списка (как InitializeFromServer в импорте).
        var listed = BuildServer("srv-01", "srv-old");
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(singleCluster: false), Array.Empty<Infobase>(), new[] { listed });
        var fromServersWindow = BuildServer("srv-01", "srv-old"); // другой объект, тот же Id

        vm.SetInitialSource(fromServersWindow, null);

        Assert.Same(listed, vm.SelectedSourceServer);
        // Кластер не передан; на сервере кластеров несколько — автовыбор единственного
        // не выполняется (SetInitialSource не выбирает кластер сам).
        Assert.Null(vm.SelectedSourceClusterId);
    }

    [Fact]
    public void SetInitialSource_WithUnknownServer_KeepsInstanceAndSetsCluster()
    {
        // Сервер вне списка справочника (новый Guid) — берётся переданный экземпляр,
        // а переданный кластер выставляется сразу (issue #41: кластер в окне «Серверы 1С»
        // подставлялся, сервер — нет).
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), Array.Empty<ManagementServer>());
        var unknown = BuildServer("new-guid", "srv-unknown");

        vm.SetInitialSource(unknown, FakeRacClient.SourceClusterId);

        Assert.Same(unknown, vm.SelectedSourceServer);
        Assert.Equal(FakeRacClient.SourceClusterId, vm.SelectedSourceClusterId);
    }

    // ===================== Issue #41: Тип СУБД и Сервер СУБД =====================

    [Fact]
    public async Task LoadSourceBases_AutoFillsDbmsAndDbServer_FromFirstBase_Normalized()
    {
        // После «Получить базы» поля «Тип СУБД»/«Сервер СУБД» автозаполняются из первой
        // базы источника; тип нормализуется из вывода rac регистронезависимо
        // ("mssqlserver" → "MSSQLServer").
        var client = new FakeRacClient(singleCluster: true, sourceDbms: "mssqlserver");
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal("MSSQLServer", vm.Dbms);
        Assert.Equal("sql-01", vm.DbServer);
    }

    [Fact]
    public async Task LoadSourceBases_AutoFillsLocale_FromFirstBase_Normalized()
    {
        // issue #41, комментарий 7OH («Поле с локалью на поле не замечено»): локаль
        // автозаполняется из первой базы источника и нормализуется до двухбуквенного
        // кода («ru_RU» → «ru» — как принимает rac в «--locale»).
        var client = new FakeRacClient(singleCluster: true, sourceLocale: "ru_RU");
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal("ru", vm.Locale);
    }

    [Fact]
    public async Task LoadSourceBases_LocaleMissing_FallsBackToDefaultRu()
    {
        // rac 8.3/частичные 8.5 не отдают locale в summary list — остаётся «ru» по умолчанию
        // (пустое значение больше не уходит в rac «infobase create» как прежде).
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal("ru", vm.Locale);
    }

    [Fact]
    public async Task LoadSourceBases_DoesNotOverwriteDbmsDbServer_WhenUserEditedThem()
    {
        // Пользователь изменил «Тип СУБД»/«Сервер СУБД» вручную (приёмник — другой сервер
        // СУБД) — автозаполнение из первой базы источника не перезаписывает его выбор.
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        vm.Dbms = "PostgreSQL";
        vm.DbServer = "pg-02";

        await AwaitSourceRows(vm);

        Assert.Equal("PostgreSQL", vm.Dbms);
        Assert.Equal("pg-02", vm.DbServer);
    }

    [Fact]
    public async Task MigrateAsync_PassesVmDbmsAndDbServer_IntoCreateRequest()
    {
        // «Тип СУБД»/«Сервер СУБД» с формы миграции уходят в rac «infobase create»
        // (фейк IRacClient фиксирует полученные RacInfobaseCreateRequest).
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.Dbms = "PostgreSQL";
        vm.DbServer = "pg-02";

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.NotEmpty(client.Created);
        Assert.All(client.Created, c => Assert.Equal("PostgreSQL", c.Request.Dbms));
        Assert.All(client.Created, c => Assert.Equal("pg-02", c.Request.DbServer));
    }

    [Fact]
    public async Task MigrateAsync_UsesSourceDbNameOrInfobaseName_WhenDbNameMissingInSource()
    {
        // issue #41 (фикс 1.0.0.71): поле «Имя БД» удалено с формы миграции, а имя БД в СУБД
        // переносится автоматически внутри ClusterMigrationService.BuildCreateRequest. У базы
        // rac не вернул db-name (sourceDbName: "") — в rac «infobase create» уходит имя ИБ
        // (сервер 1С по умолчанию создаёт БД в СУБД с именем ИБ). VM не хранит DbName — перенос
        // работает без поля.
        var client = new FakeRacClient(singleCluster: true, sourceDbName: "");
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.NotEmpty(client.Created);
        Assert.Equal(new[] { "База1", "База2" },
            client.Created.Select(c => c.Request.DbName).ToArray());
    }

    [Fact]
    public async Task MigrateAsync_NoDbNameField_RequestHasNonEmptyDbName()
    {
        // Регресс фикса 1.0.0.71: запрос создания никогда не получает пустой DbName — у базы
        // с корректным db-name переносится собственное имя («база1»/«база2»), у базы без
        // db-name — имя ИБ (fallback). Никакое значение с формы не участвует.
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.NotEmpty(client.Created);
        Assert.All(client.Created, c => Assert.False(string.IsNullOrWhiteSpace(c.Request.DbName)));
        Assert.Equal(new[] { "база1", "база2" },
            client.Created.Select(c => c.Request.DbName).ToArray());
    }

    // ===================== Хелперы =====================

    private static async Task AwaitSourceClusters(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.SourceClusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static async Task AwaitSourceRows(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.SourceRows.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static async Task AwaitTargetClusters(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.TargetClusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static ManagementServer BuildServer(string id, string address) => new()
    {
        Id = id,
        Description = "Сервер " + address,
        Address = address,
        Port = 1545,
        User = "admin",
        Password = "secret"
    };

    // ===================== Fake =====================

    /// <summary>
    /// Fake-клиент rac для тестов миграции: кластеры источника/приёмника (один по умолчанию,
    /// несколько по запросу), «cluster info» с hostName, базы источника/приёмника, подсчёт
    /// и запись вызовов «infobase create»/«infobase drop» (с параметрами) и имитация сбоев.
    /// </summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly Guid SourceClusterId = Guid.Parse("99000000-0000-0000-0000-000000000001");
        public static readonly Guid TargetClusterId = Guid.Parse("88000000-0000-0000-0000-000000000002");
        public static readonly Guid TargetCluster2Id = Guid.Parse("77000000-0000-0000-0000-000000000003");

        private readonly bool _singleCluster;
        // 1-based индекс создаваемой базы, на котором имитируется отказ rac («infobase create»
        // возвращает false); null — без отказа. failCreate: true в конструкторе равнозначен
        // failCreateOnIndex: 1.
        private readonly int? _failCreateOnIndex;
        // true — «infobase create» бросает исключение (сбой соединения) вместо false:
        // частичный перенос идёт через общий catch MigrateAsync (issue #41).
        private readonly bool _throwOnCreate;
        private readonly bool _failDrop;
        private readonly string[] _targetBaseNames;
        private readonly string _sourceDbms;
        // null — прежнее поведение (DbName="база1"/"база2"); "" — пустой db-name у обеих баз
        // источника (кейс issue #41, фикс 1.0.0.71: rac не вернул db-name — переносится имя ИБ).
        private readonly string? _sourceDbName;
        // Локаль источника (issue #41): rac может вернуть «ru_RU» (нормализуется) или не
        // вернуть ничего (пусто → «ru» по умолчанию, как при добавлении базы).
        private readonly string? _sourceLocale;
        // Число баз источника («База1»…«БазаN»; по умолчанию 2 — совместимость с прежними
        // тестами; для сценария «3 базы» из комментария 7OH issue #41 передаётся 3).
        private readonly int _sourceBaseCount;
        // Флаги блокировок источника (issue #69, комментарий 7OH: копировать флаги
        // блокировок при переносе/копировании) — если заданы, «infobase summary list»
        // источника их возвращает (блочный формат 8.5.x).
        private readonly bool? _sourceScheduledJobsDeny;
        private readonly bool? _sourceSessionsDeny;

        // Имена баз источника в ЗАДАННОМ порядке (issue #41, сортировка по имени):
        // null — прежнее поведение («База1»…«БазаN» по возрастанию).
        private readonly string[]? _sourceBaseNames;

        public FakeRacClient(bool singleCluster = true, bool failCreate = false, bool failDrop = false,
            string[]? targetBaseNames = null, string? sourceDbms = null, string? sourceDbName = null,
            string? sourceLocale = null, int? failCreateOnIndex = null, bool throwOnCreate = false,
            int sourceBaseCount = 2, string[]? sourceBaseNames = null,
            bool? sourceScheduledJobsDeny = null, bool? sourceSessionsDeny = null)
        {
            _singleCluster = singleCluster;
            _failCreateOnIndex = failCreate ? 1 : failCreateOnIndex;
            _throwOnCreate = throwOnCreate;
            _failDrop = failDrop;
            _targetBaseNames = targetBaseNames ?? Array.Empty<string>();
            _sourceDbms = sourceDbms ?? "MSSQLServer";
            _sourceDbName = sourceDbName;
            _sourceLocale = sourceLocale;
            _sourceBaseCount = sourceBaseCount;
            _sourceBaseNames = sourceBaseNames;
            _sourceScheduledJobsDeny = sourceScheduledJobsDeny;
            _sourceSessionsDeny = sourceSessionsDeny;
        }

        public RacConnectionParams? LastParams { get; private set; }

        public List<(Guid ClusterId, RacInfobaseCreateRequest Request)> Created { get; } = new();
        // Имена ТОЛЬКО успешно созданных баз (в «infobase summary list» приёмника они и
        // попадают; неудачные попытки в Created есть, в статусах приёмника — нет).
        public List<string> CreatedNames { get; } = new();
        public List<(Guid ClusterId, Guid InfobaseId, bool DropDatabase)> Dropped { get; } = new();

        /// <summary>Число вызовов «infobase summary list» (счётчик автообновления статусов, issue #41).</summary>
        public int GetInfobasesCalls { get; private set; }

        /// <summary>Колбэк перед каждым «infobase create» (для проверки прогресса в момент обработки).</summary>
        public Action? OnBeforeCreate { get; set; }

        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            // Сервер-источник (srv-old) отдаёт кластер-источник, приёмник — кластер-приёмник.
            var isSource = string.Equals(parameters.Address, "srv-old", StringComparison.OrdinalIgnoreCase);
            var clusterId = isSource ? SourceClusterId : TargetClusterId;
            IReadOnlyList<RacCluster> clusters = _singleCluster
                ? new RacCluster[] { new() { Id = clusterId, Name = "Новый кластер", Port = 1542 } }
                : new RacCluster[]
                {
                    new() { Id = clusterId, Name = "Новый кластер", Port = 1542 },
                    new() { Id = TargetCluster2Id, Name = "Второй новый", Port = 2542 }
                };
            return Task.FromResult(clusters);
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = "Новый кластер",
                HostName = "srv-new-host",
                Port = clusterId == TargetClusterId ? 1542 : 2542
            });

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            GetInfobasesCalls++;
            // Источник: две базы; приёмник: заданные имена (по умолчанию — пусто).
            if (clusterId == SourceClusterId)
            {
                var names = _sourceBaseNames ??
                    Enumerable.Range(1, _sourceBaseCount).Select(n => "База" + n).ToArray();
                var source = new RacInfobaseSummary[names.Length];
                for (var i = 0; i < names.Length; i++)
                {
                    var n = i + 1;
                    source[i] = new RacInfobaseSummary
                    {
                        InfobaseId = Guid.Parse($"66000000-0000-0000-0000-{n:D12}"),
                        Name = names[i],
                        Descr = "Описание",
                        Dbms = _sourceDbms,
                        DbServer = "sql-01",
                        DbName = _sourceDbName ?? "база" + n,
                        Locale = _sourceLocale ?? string.Empty,
                        // Флаги блокировок источника (issue #69): null — rac их не отдал —
                        // догружаются через «infobase info».
                        ScheduledJobsDeny = _sourceScheduledJobsDeny,
                        SessionsDeny = _sourceSessionsDeny
                    };
                }
                return Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(source);
            }
            // Приёмник: заданные имена + базы, уже созданные в этом кластере миграцией
            // (по ним автообновление статусов при ошибке показывает «Есть в кластере»).
            var targetNames = new List<string>(_targetBaseNames);
            targetNames.AddRange(CreatedNames.Where(n => n.Length > 0));
            return Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(
                targetNames.Select(name => new RacInfobaseSummary { Name = name }).ToList());
        }

        /// <summary>
        /// Гейт «долгой» rac-операции создания базы (issue #41, имя первой базы видно
        /// сразу): если задан, «infobase create» не завершается, пока гейт не открыт —
        /// тест проверяет прогресс «в полёте», до завершения операции.
        /// </summary>
        public TaskCompletionSource<bool>? CreateGate { get; set; }

        public async Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            OnBeforeCreate?.Invoke();
            Created.Add((clusterId, request));
            if (CreateGate is not null)
                await CreateGate.Task.ConfigureAwait(false);
            if (_throwOnCreate)
                throw new InvalidOperationException("сбой соединения с RAS (имитация)");
            if (_failCreateOnIndex is int failIndex && Created.Count == failIndex)
            {
                LastActionError = "нет прав на создание базы (имитация)";
                return false;
            }
            LastActionError = string.Empty;
            CreatedNames.Add(request.Name ?? string.Empty);
            return true;
        }

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default)
        {
            Dropped.Add((clusterId, infobaseId, dropDatabase));
            if (_failDrop)
            {
                LastActionError = "нет прав на удаление базы (имитация)";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

        /// <summary>Результат rac «infobase info» для догрузки флагов блокировок источника
        /// (issue #69: summary флагов не отдал — VM запрашивает полные свойства базы).</summary>
        public RacInfobaseInfo? InfobaseInfoResult { get; set; }

        /// <summary>Сколько раз запрашивались полные свойства базы-источника.</summary>
        public int InfobaseInfoCalls { get; private set; }

        public Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            string? infobaseUser, string? infobasePassword, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            InfobaseInfoCalls++;
            return Task.FromResult(InfobaseInfoResult);
        }

        // ---- Неиспользуемые методы интерфейса ----

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacProcessInfo>>(Array.Empty<RacProcessInfo>());

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId,
            Guid? processId = null, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId,
            Guid? processId = null, string? infobaseUser = null,
            string? infobasePassword = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
