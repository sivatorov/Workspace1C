using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #79 «Развернуть тестовую копию» (последний комментарий 7OH): (1) единое
/// оформление полей ввода — тематический ModernTextBox вместо дефолтных «серых» полей
/// без окантовки; (2) автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере»;
/// (3) кнопки «Свойства исходной базы» и «Конфигуратор», чтобы админ мог открыть
/// свойства базы или конфигуратор, не выходя из мастера.
/// 1.0.1.9 (последний комментарий 7OH, 2026-10-30): (4) кнопка-дубль «Учётные данные
/// ИБ для ibcmd…» внизу окна удалена; (5) добавлена кнопка «Подставить из профиля
/// СУБД» (поля остаются редактируемыми, операция — по значениям на форме);
/// (6) пред-валидация проходит без сохранённого профиля СУБД, если поля СУБД заполнены
/// на форме (разовый сценарий).
/// Чистая логика (автоподстановка) проверяется на ViewModel; разметочные части —
/// чтением исходников обеих платформ (как Issue71SettingsWindowLayoutTests).
/// </summary>
// Статики OneCLauncher.Common*Authorization (AutofillCredentialFromSource) сбрасываются
// в finally; коллекция сериализует класс с остальными тестами статиков (issue #57).
[Collection("CommonAuthStatics")]
public sealed class Issue79TestDeployWindowTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static TestDeployViewModel MakeVm(string databaseName, string dbName)
    {
        var source = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "УТ",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = databaseName
            }
        };
        return new TestDeployViewModel(
            source, Array.Empty<Infobase>(), Array.Empty<Group>(),
            proposedTargetName: "Тест: УТ",
            proposedServerAddress: "srv",
            proposedDatabaseName: databaseName,
            proposedFilePath: string.Empty)
        {
            DbName = dbName
        };
    }

    // ---------------- (2) Автоподстановка имени базы СУБД ----------------

    [Fact]
    public void AutofillDbName_EmptyDbName_TakesServerDatabaseName()
    {
        // «В поле "Имя базы данных СУБД" можно подставлять как минимум такое же имя,
        // как "Имя базы на сервере"».
        var vm = MakeVm(databaseName: "ut_base", dbName: string.Empty);

        vm.AutofillDbNameFromServerDbName();

        Assert.Equal("ut_base", vm.DbName);
    }

    [Fact]
    public void AutofillDbName_AlreadyFilled_DoesNotOverwrite()
    {
        // Пользователь ввёл своё СУБД-имя — автоподстановка не перезаписывает его.
        var vm = MakeVm(databaseName: "ut_base", dbName: "own_db");

        vm.AutofillDbNameFromServerDbName();

        Assert.Equal("own_db", vm.DbName);
    }

    [Fact]
    public void AutofillDbName_EmptyServerDbName_KeepsEmpty()
    {
        var vm = MakeVm(databaseName: string.Empty, dbName: string.Empty);

        vm.AutofillDbNameFromServerDbName();

        Assert.Equal(string.Empty, vm.DbName);
    }

    // ---------------- (4) Открытие мастера (регрессия «ошибка при открытии») ----------------

    [Theory]
    [InlineData(ConnectionType.ClientServer)]
    [InlineData(ConnectionType.File)]
    public void Preview_DoesNotThrow_ForAnyConnectionType(ConnectionType type)
    {
        // issue #79 (комментарий 7OH «Ну вот теперь просто ошибка при открытии»): построение
        // предпросмотра мастера не должно ронять открытие окна ни для одного типа базы.
        var source = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "УТ",
            PlatformVersion = "8.3.24",
            Connection = new ConnectionSettings
            {
                Type = type,
                DatabaseName = type == ConnectionType.File ? null : "ut_base",
                FilePath = type == ConnectionType.File ? @"C:\bases\УТ" : null,
                Server = type == ConnectionType.File ? null : "srv"
            }
        };
        var vm = new TestDeployViewModel(
            source, Array.Empty<Infobase>(), Array.Empty<Group>(),
            proposedTargetName: "Тест: УТ",
            proposedServerAddress: "srv",
            proposedDatabaseName: "ut_base",
            proposedFilePath: string.Empty);

        Assert.NotNull(vm.PreviewSummary);      // не бросает
        Assert.NotNull(vm.PreviewSteps);        // не бросает
    }

    [Fact]
    public void Preview_DoesNotThrow_WhenSourceHasNoConnection()
    {
        // issue #79: база без соединения (null) не должна ронять предпросмотр/открытие.
        var source = new Infobase { Id = Guid.NewGuid().ToString("N"), Name = "УТ", Connection = null };
        var vm = new TestDeployViewModel(
            source, Array.Empty<Infobase>(), Array.Empty<Group>(),
            proposedTargetName: string.Empty,
            proposedServerAddress: string.Empty,
            proposedDatabaseName: string.Empty,
            proposedFilePath: string.Empty);

        Assert.NotNull(vm.PreviewSummary);
        Assert.NotNull(vm.PreviewSteps);
    }

    [Fact]
    public void PreviewUnavailable_KeyExistsInLocalization()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains("TestDeploy.PreviewUnavailable", ru, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.PreviewUnavailable", en, StringComparison.Ordinal);
    }

    // ---------------- (1) Единое оформление полей ввода ----------------

    [Fact]
    public void WpfWindow_HasUnifiedModernTextBoxStyling()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        // «одни поля серые, вторые нормальные и с окантовкой»: обход дерева окна
        // назначает ModernTextBox/ModernComboBox всем редактируемым полям.
        Assert.Contains("ApplyModernStyles", code, StringComparison.Ordinal);
        Assert.Contains("\"ModernTextBox\"", code, StringComparison.Ordinal);
        Assert.Contains("\"ModernComboBox\"", code, StringComparison.Ordinal);
        // issue #79 (7OH, 2026-10-29 19:41): раньше ModernTextBox (TargetType="TextBox")
        // присваивался PasswordBox — падение «Развернуть тестовую копию». PasswordBox
        // не должен получать этот стиль.
        Assert.DoesNotContain("pb.Style", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_ApplyModernStyles_DoesNotAssignTextBoxStyleToPasswordBox()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        // issue #79, последний комментарий 7OH: «TargetType "TextBox" не соответствует
        // типу элемента "PasswordBox"» (System.InvalidOperationException в stack-трейсе
        // на строке ApplyModernStyles). Пароль оставлен на темированном implicit-стиле
        // TargetType="PasswordBox" из LightTheme/DarkTheme — явное присваивание
        // ModernTextBox этому контролу запрещено (см. ProfilesPanel.xaml).
        Assert.Contains("ApplyModernStyles", code, StringComparison.Ordinal);
        Assert.Contains("PasswordBox", code, StringComparison.Ordinal);
        Assert.Contains("is ComboBox cb", code, StringComparison.Ordinal);
        Assert.DoesNotContain("pb.Style = styleBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_EditableBoxesAreStyledWithModernTextBox()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        // Avalonia-версия уже использует ModernTextBox для полей — страж, чтобы
        // добавленные позже поля снова не остались без стиля.
        Assert.Contains("ControlThemes.ModernTextBox", code, StringComparison.Ordinal);
        Assert.Contains("ControlThemes.ModernPasswordBox", code, StringComparison.Ordinal);
    }

    // ---------------- (3) Кнопки действий по исходной базе ----------------

    [Fact]
    public void WpfWindow_HasSourceBaseActionButtons()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        Assert.Contains("TestDeploy.OpenBaseProperties", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.LaunchConfigurator", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenBaseProperties()", code, StringComparison.Ordinal);
        Assert.Contains("private void LaunchConfigurator()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasSourceBaseActionButtons()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        Assert.Contains("TestDeploy.OpenBaseProperties", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.LaunchConfigurator", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenBaseProperties()", code, StringComparison.Ordinal);
        Assert.Contains("private void LaunchConfigurator()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasButtonKeysInRuAndEn()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        foreach (var key in new[]
        {
            "TestDeploy.OpenBaseProperties",
            "TestDeploy.OpenBasePropertiesHint",
            "TestDeploy.LaunchConfigurator",
            "TestDeploy.LaunchConfiguratorHint"
        })
        {
            Assert.Contains(key, ru, StringComparison.Ordinal);
            Assert.Contains(key, en, StringComparison.Ordinal);
        }
    }

    // ---------------- (5) Учётные данные для выгрузки через ibcmd (issue #79, 2026-10-30) ----------------

    [Fact]
    public void BuildScenario_ContainsCredential_WithDefaultsAndExplicit()
    {
        // По умолчанию — «авторизация информационной базы» (поведение не меняется).
        var vm = MakeVm(databaseName: "ut_base", dbName: string.Empty);
        var s1 = vm.BuildScenario();
        Assert.NotNull(s1.Credential);
        Assert.True(s1.Credential!.UseInfobaseAuth);
        Assert.Equal(string.Empty, s1.Credential.User);
        Assert.Equal(string.Empty, s1.Credential.Password);

        // Явные логин/пароль из мастера попадают в сценарий.
        vm.UseInfobaseAuth = false;
        vm.User = " ivan ";
        vm.Password = "secret";
        var s2 = vm.BuildScenario();
        Assert.False(s2.Credential!.UseInfobaseAuth);
        Assert.Equal("ivan", s2.Credential.User);
        Assert.Equal("secret", s2.Credential.Password);
    }

    [Fact]
    public void AutofillCredentialFromSource_FillsFromResolver_AndSkipsWhenInfobaseAuthOn()
    {
        var source = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "УТ",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = "ut_base"
            }
        };
        var vm = new TestDeployViewModel(
            source, Array.Empty<Infobase>(), Array.Empty<Group>(),
            proposedTargetName: "Тест: УТ",
            proposedServerAddress: "srv",
            proposedDatabaseName: "ut_base",
            proposedFilePath: string.Empty);
        try
        {
            OneCLauncher.CommonConfiguratorAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "commonUser",
                Password = "commonPass"
            };

            // Авторизация ИБ включена (по умолчанию) — резолвер и так подставит общие:
            // автоподстановка не перезаписывает уже введённые пользователем данные.
            vm.UseInfobaseAuth = true;
            vm.User = "manual";
            vm.AutofillCredentialFromSource();
            Assert.Equal("manual", vm.User);

            // Авторизация ИБ выключена — автоподстановка из резолвера (общая «Конфигуратора»).
            vm.UseInfobaseAuth = false;
            vm.User = string.Empty;
            vm.Password = string.Empty;
            vm.AutofillCredentialFromSource();
            Assert.Equal("commonUser", vm.User);
            Assert.Equal("commonPass", vm.Password);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void WpfWindow_HasIbcmdCredentialsPanelAndButtons()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        Assert.Contains("TestDeploy.IbcmdCredentials", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.UseInfobaseAuth", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.CredentialsUser", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.CredentialsPassword", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.FillFromBase", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.OpenCommonAuth", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenCommonAuthSettings()", code, StringComparison.Ordinal);
        Assert.Contains("UpdateIbcmdPanel", code, StringComparison.Ordinal);
        // issue #79 (1.0.1.7): кнопка «Профиль СУБД для ibcmd…» открывает окно
        // «Резервные копии СУБД» (DbmsBackupWindow), а не свойства базы.
        Assert.Contains("TestDeploy.OpenDbmsProfileWindow", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenDbmsProfile()", code, StringComparison.Ordinal);
        Assert.Contains("new DbmsBackupWindow(", code, StringComparison.Ordinal);
        // issue #79 (1.0.1.9, 7OH: «Учётные данные ИБ для ibcmd» — дубль и не нужна):
        // кнопка удалена из окна; вместо дублирующих профиль полей — кнопка
        // «Подставить из профиля СУБД» (поля остаются редактируемыми).
        Assert.DoesNotContain("TestDeploy.OpenCredentialsWindow", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.FillFromDbmsProfile", code, StringComparison.Ordinal);
        Assert.Contains("AutofillDbmsFromProfile", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.NoDbmsProfile", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasIbcmdCredentialsPanelAndButtons()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        Assert.Contains("TestDeploy.IbcmdCredentials", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.UseInfobaseAuth", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.CredentialsUser", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.CredentialsPassword", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.FillFromBase", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.OpenCommonAuth", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenCommonAuthSettings()", code, StringComparison.Ordinal);
        Assert.Contains("ApplyIbcmdPanel", code, StringComparison.Ordinal);
        // issue #79 (1.0.1.7): Avalonia-паритет кнопки «Профиль СУБД для ibcmd…».
        Assert.Contains("TestDeploy.OpenDbmsProfileWindow", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenDbmsProfile()", code, StringComparison.Ordinal);
        Assert.Contains("new DbmsBackupWindow(", code, StringComparison.Ordinal);
        // issue #79 (1.0.1.9): Avalonia-паритет — дубль удалён, кнопка подстановки есть.
        Assert.DoesNotContain("TestDeploy.OpenCredentialsWindow", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.FillFromDbmsProfile", code, StringComparison.Ordinal);
        Assert.Contains("AutofillDbmsFromProfile", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.NoDbmsProfile", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasIbcmdKeysInRuAndEn()
    {
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        foreach (var key in new[]
        {
            "TestDeploy.IbcmdCredentials",
            "TestDeploy.UseInfobaseAuth",
            "TestDeploy.CredentialsUser",
            "TestDeploy.CredentialsPassword",
            "TestDeploy.FillFromBase",
            "TestDeploy.OpenCommonAuth",
            "TestDeploy.IbcmdCredentialsHint",
            // issue #79 (1.0.1.7): профиль СУБД для ibcmd.
            "TestDeploy.OpenDbmsProfileWindow",
            "TestDeploy.OpenDbmsProfileWindowHint",
            "TestDeploy.NeedDbmsProfile",
            // issue #79 (1.0.1.9): подстановка из профиля СУБД вместо дублирующих полей.
            "TestDeploy.FillFromDbmsProfile",
            "TestDeploy.FillFromDbmsProfileHint",
            "TestDeploy.NoDbmsProfile"
        })
        {
            Assert.Contains(key, ru, StringComparison.Ordinal);
            Assert.Contains(key, en, StringComparison.Ordinal);
        }

        // Ключи удалённой кнопки-дубля вычищены из локализации (1.0.1.9).
        Assert.DoesNotContain("TestDeploy.OpenCredentialsWindow", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("TestDeploy.OpenCredentialsWindow", en, StringComparison.Ordinal);
    }

    // ---------------- (6) Разовый сценарий без сохранённого профиля СУБД (1.0.1.9) ----------------

    private static TestDeployScenario BuildIbcmdScenario(TestDeployViewModel vm, string dbServer, string dbName)
    {
        vm.EngineIndex = 1; // ibcmd
        vm.DbServer = dbServer;
        vm.DbName = dbName;
        return vm.BuildScenario();
    }

    [Fact]
    public void Validate_IbcmdClientServer_NoSavedProfile_PassesWithFormDbmsValues()
    {
        // Разовый сценарий 7OH: «хочу просто копию без постоянного профиля» — заполненные
        // на форме поля СУБД удовлетворяют пред-валидацию без сохранённого профиля.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        vm.Dbms = "MSSQLServer";
        var scenario = BuildIbcmdScenario(vm, dbServer: "sqlsrv", dbName: "ut_base");
        var source = vm.Source;

        var errors = TestDeployPlanner.Validate(scenario, source, existingTarget: null,
            dbmsProfileResolver: _ => null);

        Assert.DoesNotContain("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void Validate_IbcmdClientServer_NoProfileAndEmptyFormDbms_Fails()
    {
        // Без профиля И без заполненных полей СУБД на форме — понятная ошибка до старта.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        var scenario = BuildIbcmdScenario(vm, dbServer: string.Empty, dbName: string.Empty);
        var source = vm.Source;

        var errors = TestDeployPlanner.Validate(scenario, source, existingTarget: null,
            dbmsProfileResolver: _ => null);

        Assert.Contains("TestDeploy.NeedDbmsProfile", errors);
    }

    [Fact]
    public void AutofillDbmsFromProfile_FillsFormFieldsFromProfile()
    {
        // «Логичнее иметь кнопку "Подставить из профиля" … либо данные из профиля,
        // либо вручную. А выполнять работу по тому, что на форме».
        var vm = MakeVm(databaseName: "ut_base", dbName: string.Empty);
        var profile = new DbmsBackupProfile
        {
            InfobaseId = vm.Source.Id,
            Dbms = DbmsKind.PostgreSql,
            Host = "pgsrv",
            Port = 5432,
            Database = "ut_base",
            User = "postgres",
            Password = "secret"
        };

        var filled = vm.AutofillDbmsFromProfile(_ => profile);

        Assert.True(filled);
        Assert.Equal("PostgreSQL", vm.Dbms);
        Assert.Equal("pgsrv port=5432", vm.DbServer);
        Assert.Equal("ut_base", vm.DbName);
        Assert.Equal("postgres", vm.DbUser);
        Assert.Equal("secret", vm.DbPassword);
    }

    [Fact]
    public void AutofillDbmsFromProfile_NoProfile_ReturnsFalse()
    {
        var vm = MakeVm(databaseName: "ut_base", dbName: string.Empty);
        vm.DbServer = "keep-me";

        var filled = vm.AutofillDbmsFromProfile(_ => null);

        Assert.False(filled);
        Assert.Equal("keep-me", vm.DbServer); // поля не тронуты
    }

    [Fact]
    public void BuildScenario_IbcmdKeepsFormDbmsValues_UsedAsOverride()
    {
        // Операция выполняется по значениям на форме: поля СУБД формы попадают
        // в сценарий и передаются в ibcmd как переопределение профиля.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        var scenario = BuildIbcmdScenario(vm, dbServer: "sqlsrv", dbName: "ut_base");

        Assert.Equal(BackupEngine.Ibcmd, scenario.DumpEngine);
        Assert.Equal("sqlsrv", scenario.DbServer);
        Assert.Equal("ut_base", scenario.DbName);
    }
}