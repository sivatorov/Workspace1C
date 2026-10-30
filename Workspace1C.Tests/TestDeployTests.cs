using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сценария «Развернуть тестовую копию» (п.10, 1.0.0.92):
/// построение имени тестовой базы, поиск существующей копии, создание группы «Тест»,
/// последовательность шагов/план операций, повторное развёртывание (обновить/создать),
/// валидация и обработка ошибок шага на уровне плана. Без реальной платформы 1С.
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class TestDeployTests
{
    // ---------- Имя тестовой базы ----------

    [Theory]
    [InlineData("Управление торговлей", "Тест: Управление торговлей")]
    [InlineData("Тест: Управление торговлей", "Тест: Управление торговлей")]
    [InlineData("  ", "Тест:")]
    public void ProposeTargetName_AddsOrKeepsPrefix(string source, string expected)
    {
        Assert.Equal(expected, TestDeployPlanner.ProposeTargetName(source));
    }

    [Fact]
    public void ResolveTargetName_ExplicitTargetWins()
    {
        var scenario = new TestDeployScenario { TargetName = "  УТ-песочница  " };
        Assert.Equal("УТ-песочница", TestDeployPlanner.ResolveTargetName(scenario, "Управление торговлей"));
    }

    [Fact]
    public void ResolveTargetName_NoTitleChange_KeepsSourceName()
    {
        var scenario = new TestDeployScenario { ChangeTitle = false };
        Assert.Equal("Управление торговлей", TestDeployPlanner.ResolveTargetName(scenario, "Управление торговлей"));
    }

    // ---------- Поиск существующей тестовой копии ----------

    private static Infobase Base(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    [Fact]
    public void FindExistingTarget_ExactName_ReturnsBase()
    {
        var scenario = new TestDeployScenario { TargetName = "Тест: УТ", AddToTestGroup = true, ChangeTitle = true };
        var list = new List<Infobase> { Base("Рабочая УТ"), Base("Тест: УТ") };

        var found = TestDeployPlanner.FindExistingTarget(list, scenario, "Тест: УТ");

        Assert.NotNull(found);
        Assert.Equal("Тест: УТ", found!.Name);
    }

    [Fact]
    public void FindExistingTarget_PrefixRenamedCopy_ReturnsBase()
    {
        // Переименованная копия «Тест: УТ 2» считается той же тестовой копией.
        var scenario = new TestDeployScenario { TargetName = "Тест: УТ", AddToTestGroup = true, ChangeTitle = true };
        var list = new List<Infobase> { Base("Тест: УТ 2") };

        var found = TestDeployPlanner.FindExistingTarget(list, scenario, "Тест: УТ");

        Assert.NotNull(found);
        Assert.Equal("Тест: УТ 2", found!.Name);
    }

    [Fact]
    public void FindExistingTarget_DifferentTestCopy_NotMatched()
    {
        var scenario = new TestDeployScenario { TargetName = "Тест: УТ", AddToTestGroup = true, ChangeTitle = true };
        var list = new List<Infobase> { Base("Тест: ЗУП") };

        Assert.Null(TestDeployPlanner.FindExistingTarget(list, scenario, "Тест: УТ"));
    }

    // ---------- Группа «Тест» ----------

    [Fact]
    public void EnsureGroup_ExistingRootGroup_ReturnsPathAndNoNewGroup()
    {
        var groups = new List<Group>
        {
            new() { Id = "g1", Name = "Тест", Color = "#111111" }
        };
        var scenario = new TestDeployScenario { AddToTestGroup = true, TestGroupName = "Тест" };

        var plan = TestDeployPlanner.EnsureGroup(groups, scenario);

        Assert.True(plan.Exists);
        Assert.Equal("Тест", plan.GroupPath);
        Assert.Null(plan.NewGroup);
    }

    [Fact]
    public void EnsureGroup_MissingGroup_CreatesWithColorAndPath()
    {
        var scenario = new TestDeployScenario
        {
            AddToTestGroup = true,
            TestGroupName = "Тест",
            TestGroupColor = "#D97706"
        };

        var plan = TestDeployPlanner.EnsureGroup(Array.Empty<Group>(), scenario);

        Assert.False(plan.Exists);
        Assert.Equal("Тест", plan.GroupPath);
        Assert.NotNull(plan.NewGroup);
        Assert.Equal("Тест", plan.NewGroup!.Name);
        Assert.Equal("#D97706", plan.NewGroup.Color);
    }

    [Fact]
    public void EnsureGroup_Disabled_ReturnsEmptyPath()
    {
        var scenario = new TestDeployScenario { AddToTestGroup = false };
        var plan = TestDeployPlanner.EnsureGroup(Array.Empty<Group>(), scenario);
        Assert.Equal(string.Empty, plan.GroupPath);
        Assert.Null(plan.NewGroup);
    }

    // ---------- План и последовательность шагов ----------

    [Fact]
    public void BuildPlan_NoExistingCopy_HasCreateAndRestoreSteps()
    {
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            AddToTestGroup = true,
            ChangeTitle = true,
            ChangeColor = true,
            DisableScheduledJobs = true
        };
        var groups = Array.Empty<Group>();

        var plan = TestDeployPlanner.BuildPlan(scenario, "Управление торговлей",
            Array.Empty<Infobase>(), groups, @"C:\Temp\testcopy");

        Assert.True(plan.CreateTarget);
        Assert.Equal("Тест: УТ", plan.TargetName);
        Assert.Equal(@"C:\Temp\testcopy\testcopy.dt", plan.DumpFilePath);
        var kinds = plan.Steps.Select(s => s.Kind).ToList();
        Assert.Contains(TestDeployStepKind.Dump, kinds);
        Assert.Contains(TestDeployStepKind.EnsureGroup, kinds);
        Assert.Contains(TestDeployStepKind.Create, kinds);
        Assert.Contains(TestDeployStepKind.Restore, kinds);
        Assert.Contains(TestDeployStepKind.DisableJobs, kinds);
        Assert.Contains(TestDeployStepKind.Markers, kinds);
        Assert.Contains(TestDeployStepKind.AddToGroup, kinds);
        Assert.Equal(7, kinds.Count);
    }

    [Fact]
    public void BuildPlan_ExistingCopy_UpdatesInsteadOfCreate()
    {
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            AddToTestGroup = true,
            ChangeTitle = true,
            DisableScheduledJobs = false
        };
        var existing = Base("Тест: УТ");
        var infobases = new List<Infobase> { Base("Рабочая"), existing };

        var plan = TestDeployPlanner.BuildPlan(scenario, "Рабочая", infobases,
            Array.Empty<Group>(), @"C:\Temp\testcopy");

        Assert.False(plan.CreateTarget);
        Assert.Same(existing, plan.ExistingTarget);
        var kinds = plan.Steps.Select(s => s.Kind).ToList();
        // «Создание» заменяется «Обновлением», шаг отключения регламентов отключён.
        Assert.Contains(TestDeployStepKind.Update, kinds);
        Assert.DoesNotContain(TestDeployStepKind.Create, kinds);
        Assert.DoesNotContain(TestDeployStepKind.DisableJobs, kinds);
    }

    // ---------- Валидация ----------

    [Fact]
    public void Validate_NullScenario_ReturnsInvalidScenario()
    {
        var errors = TestDeployPlanner.Validate(null, Base("УТ"), null);
        Assert.Contains("TestDeploy.InvalidScenario", errors);
    }

    [Fact]
    public void Validate_NoSource_ReturnsNoSource()
    {
        var scenario = new TestDeployScenario();
        var errors = TestDeployPlanner.Validate(scenario, null, null);
        Assert.Contains("TestDeploy.NoSource", errors);
    }

    [Fact]
    public void Validate_ServerMode_MissingServerOrRef_ReturnsError()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.Server,
            ServerAddress = "",
            DatabaseName = ""
        };
        var errors = TestDeployPlanner.Validate(scenario, source, null);
        Assert.Contains("CreateInfobase.EnterServerAndDb", errors);
    }

    [Fact]
    public void Validate_CreateSqlDatabase_MissingDbmsDetails_ReturnsError()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.Server,
            ServerAddress = "srv-1c:1541",
            DatabaseName = "УТ_t",
            CreateSqlDatabase = true,
            Dbms = "",
            DbServer = ""
        };
        var errors = TestDeployPlanner.Validate(scenario, source, null);
        Assert.Contains("TestDeploy.EnterDbDetails", errors);
    }

    // ---------- Пред-валидация профиля СУБД (issue #79, 1.0.1.7) ----------

    private static Infobase ServerBase(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        PlatformVersion = "8.3.27",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv-1c",
            DatabaseName = name + "_db"
        }
    };

    [Fact]
    public void Validate_IbcmdEngine_ServerSourceWithoutDbmsProfile_ReturnsErrorBeforeStart()
    {
        // Репорт 7OH «заполните профиль»: клиент-серверная база + движок ibcmd без
        // профиля СУБД — ошибка должна приходить ДО старта операций (валидация),
        // а не на шаге «Выгрузка».
        var source = ServerBase("УТ");
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\bases\Тест",
            DumpEngine = BackupEngine.Ibcmd
        };

        var errors = TestDeployPlanner.Validate(scenario, source, null, _ => null);

        Assert.Contains("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_IbcmdEngine_ServerSourceWithDbmsProfile_Passes()
    {
        var source = ServerBase("УТ");
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\bases\Тест",
            DumpEngine = BackupEngine.Ibcmd
        };
        var profile = new DbmsBackupProfile { Host = "pg", Database = "ut_db" };

        var errors = TestDeployPlanner.Validate(scenario, source, null, _ => profile);

        Assert.DoesNotContain("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_IbcmdEngine_ProfileWithoutDatabase_TreatedAsMissing()
    {
        // Профиль есть, но база данных СУБД не заполнена — ibcmd подключиться не сможет.
        var source = ServerBase("УТ");
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\bases\Тест",
            DumpEngine = BackupEngine.Ibcmd
        };
        var profile = new DbmsBackupProfile { Host = "pg", Database = " " };

        var errors = TestDeployPlanner.Validate(scenario, source, null, _ => profile);

        Assert.Contains("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_IbcmdEngine_FileSource_DoesNotRequireDbmsProfile()
    {
        // Файловая база выгружается через --db-path — профиль СУБД не нужен.
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\bases\Тест",
            DumpEngine = BackupEngine.Ibcmd
        };

        var errors = TestDeployPlanner.Validate(scenario, source, null, _ => null);

        Assert.DoesNotContain("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_DesignerEngine_ServerSource_DoesNotRequireDbmsProfile()
    {
        // Движок «Конфигуратор» (DesignerBatch) выгружает без профиля СУБД — требование
        // применяется только к ibcmd.
        var source = ServerBase("УТ");
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\bases\Тест",
            DumpEngine = BackupEngine.Designer
        };

        var errors = TestDeployPlanner.Validate(scenario, source, null, _ => null);

        Assert.DoesNotContain("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_ExistingCopyWithoutUpdateEnabled_BlocksRun()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\test\Тест_УТ",
            UpdateExisting = false
        };
        var existing = Base("Тест: УТ");

        var errors = TestDeployPlanner.Validate(scenario, source, existing);
        Assert.Contains("TestDeploy.ExistingTargetBlocked", errors);
    }

    [Fact]
    public void Validate_ValidFileScenario_NoErrors()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario
        {
            TargetName = "Тест: УТ",
            TargetMode = TestDeployTargetMode.File,
            FilePath = @"C:\test\Тест_УТ",
            AddToTestGroup = true,
            TestGroupName = "Тест"
        };

        var errors = TestDeployPlanner.Validate(scenario, source, null);
        Assert.Empty(errors);
    }

    // ---------- Валидация: ошибка шага выглядит для пользователя понятной ----------

    [Fact]
    public void Validate_ErrorKeys_AreLocalizationKeys_NotRawText()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var scenario = new TestDeployScenario { TargetName = " ", TargetMode = TestDeployTargetMode.File };
        var errors = TestDeployPlanner.Validate(scenario, source, null);
        Assert.All(errors, key =>
            Assert.True(key.StartsWith("TestDeploy.", StringComparison.Ordinal)
                || key.StartsWith("CreateInfobase.", StringComparison.Ordinal)));
    }

    // ---------- Хранилище сценариев ----------

    [Fact]
    public void Store_RoundTrip_UpsertGetDelete()
    {
        var tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cm_testdeploy_store_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        var oldOverride = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var scenario = new TestDeployScenario
            {
                Name = "Тест: УТ",
                SourceInfobaseId = "src-1",
                TargetMode = TestDeployTargetMode.Server,
                ServerAddress = "srv:1541"
            };
            TestDeployStore.Upsert(scenario);

            var loaded = TestDeployStore.Get(scenario.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Тест: УТ", loaded!.Name);
            Assert.Equal(TestDeployTargetMode.Server, loaded.TargetMode);
            Assert.Equal("srv:1541", loaded.ServerAddress);

            TestDeployStore.Delete(scenario.Id);
            Assert.Null(TestDeployStore.Get(scenario.Id));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = oldOverride;
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Store_RoundTrip_WithIbcmdCredential()
    {
        // issue #79: явные учётные данные для выгрузки через ibcmd переживают
        // сериализацию сценария (как и пароли сценариев резервирования).
        var tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cm_testdeploy_cred_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        var oldOverride = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = tempDir;
        try
        {
            var scenario = new TestDeployScenario
            {
                Name = "Тест: УТ",
                SourceInfobaseId = "src-1",
                DumpEngine = BackupEngine.Ibcmd,
                Credential = new BackupCredential
                {
                    UseInfobaseAuth = false,
                    User = "ibUser",
                    Password = "ibPass"
                }
            };
            TestDeployStore.Upsert(scenario);

            var loaded = TestDeployStore.Get(scenario.Id);
            Assert.NotNull(loaded);
            Assert.Equal(BackupEngine.Ibcmd, loaded!.DumpEngine);
            Assert.NotNull(loaded.Credential);
            Assert.False(loaded.Credential!.UseInfobaseAuth);
            Assert.Equal("ibUser", loaded.Credential.User);
            Assert.Equal("ibPass", loaded.Credential.Password);

            TestDeployStore.Delete(scenario.Id);
            Assert.Null(TestDeployStore.Get(scenario.Id));
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = oldOverride;
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ---------- ViewModel: сборка сценария ----------

    [Fact]
    public void ViewModel_BuildScenario_MapsFieldsAndScheduleDays()
    {
        var source = Base("Управление торговлей");
        source.PlatformVersion = "8.3.27";
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: Управление торговлей",
            "srv-1c:1541",
            "Управление_торговлей_t",
            string.Empty);
        vm.IsFileMode = true; // переключаем в файловый режим
        vm.FilePath = @"C:\test\cons";
        vm.EngineIndex = 1; // ibcmd
        vm.DisableScheduledJobs = false;
        vm.AddToTestGroup = true;
        vm.Monday = true;
        vm.Wednesday = true;

        var scenario = vm.BuildScenario();

        Assert.Equal(TestDeployTargetMode.File, scenario.TargetMode);
        Assert.Equal(@"C:\test\cons", scenario.FilePath);
        Assert.Equal(BackupEngine.Ibcmd, scenario.DumpEngine);
        Assert.False(scenario.DisableScheduledJobs);
        Assert.True(scenario.AddToTestGroup);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
            scenario.ScheduleDaysOfWeek.OrderBy(d => d).ToArray());
    }

    [Fact]
    public void ViewModel_Validate_ReturnsPublishableErrorKeys()
    {
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: УТ",
            string.Empty, // пустой сервер → ошибка валидации
            string.Empty,
            string.Empty);
        vm.IsServerMode = true;

        var errors = vm.Validate();
        Assert.NotEmpty(errors);
    }

    // ---------- Движок выгрузки (issue #79) ----------

    [Theory]
    [InlineData("Designer", "Конфигуратор")]
    [InlineData("Ibcmd", "ibcmd (без окна)")]
    public void EngineOption_ToString_ReturnsTextNotTypeName(string value, string text)
    {
        // issue #79: раньше ComboBox «Движок выгрузки» показывал CLR-имя типа
        // (например, «Workspace1C.TestDeployWindow+ScheduleOptionValue»), потому что
        // пара «ключ/текст» не переопределяла ToString(). Теперь общий класс
        // TestDeployEngineOption отображает человекочитаемый текст.
        var option = new TestDeployEngineOption(value, text);

        Assert.Equal(value, option.Value);
        Assert.Equal(text, option.Text);
        Assert.Equal(text, option.ToString());
        Assert.DoesNotContain("TestDeploy", option.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, BackupEngine.Designer)]
    [InlineData(1, BackupEngine.Ibcmd)]
    public void ViewModel_EngineIndex_MapsToDumpEngine(int index, BackupEngine expected)
    {
        // Выбор движка в списке мастера сохраняется в сценарий (используется при выгрузке .dt).
        var source = Base("УТ");
        source.PlatformVersion = "8.3.27";
        var vm = new TestDeployViewModel(
            source,
            Array.Empty<Infobase>(),
            Array.Empty<Group>(),
            "Тест: УТ",
            "srv:1541",
            "УТ_t",
            string.Empty);
        vm.EngineIndex = index;

        var scenario = vm.BuildScenario();
        Assert.Equal(expected, scenario.DumpEngine);
    }
}