using System;
using System.Collections.Generic;
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

    // ---------------- (7) Регрессия v1.0.1.33: дублирование _refBox и состав СУБД ----------------

    [Fact]
    public void WpfWindow_RefBoxIsNotAddedToLayoutTwice()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        // issue #79 (v1.0.1.33, diagnostics ZIP 7OH: InvalidOperationException «Specified
        // element is already the logical child of another element» в TestDeployWindow..ctor
        // на строке 244): в v1.0.1.32 _refBox добавлялся в дерево дважды — в строку
        // MakeFieldsRow (строка 225) и повторно напрямую в _serverPanel (строка 244).
        // Повторное Children.Add(_refBox) в _serverPanel запрещено: поле входит в раскладку
        // только внутри MakeFieldsRow («Сервер 1С» + «Имя базы на сервере»).
        Assert.Contains("_serverPanel.Children.Add(MakeFieldsRow(", code, StringComparison.Ordinal);
        Assert.Contains("(\"TestDeploy.Ref\", _refBox)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_serverPanel.Children.Add(_refBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_RefBoxIsAddedToLayoutOnlyOnce()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        // Avalonia-версия не была задвоена (падала только Windows) — гвард, чтобы дубль
        // не появился при будущих перекомпоновках: _refBox только внутри FieldsRow.
        Assert.Contains("_serverPanel.Children.Add(FieldsRow(", code, StringComparison.Ordinal);
        Assert.Contains("(T(\"TestDeploy.Ref\"), _refBox)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_serverPanel.Children.Add(_refBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DbmsLists_SourceOverrideComboHasFourSupportedTypes_OnBothPlatforms()
    {
        // issue #79 (v1.0.1.33): ручной блок «Данные СУБД источника» выровнен по тем же
        // 4 типам, что и ComboBox «СУБД» (в v1.0.1.32 было только 2 значения).
        var expected = "\"MSSQLServer\", \"PostgreSQL\", \"IBMDB2\", \"OracleDatabase\"";
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        Assert.Contains(expected, wpf, StringComparison.Ordinal);
        Assert.Contains(expected, avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_MySqlAndMariaDbKeysRemovedFromRuAndEn()
    {
        // issue #79 (v1.0.1.33): MySQL/MariaDB убраны из списка СУБД — ключи подписей
        // вычищены из локализации (неизвестные значения отображаются «как есть»).
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.DoesNotContain("TestDeploy.DbmsMySql", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("TestDeploy.DbmsMariaDb", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("TestDeploy.DbmsMySql", en, StringComparison.Ordinal);
        Assert.DoesNotContain("TestDeploy.DbmsMariaDb", en, StringComparison.Ordinal);
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

    // ---------------- (9) v1.0.1.36: RestoreIB в новую пустую базу без кредов ----------------

    [Fact]
    public void RestoreCredential_NewBase_SuppressesAuth()
    {
        // issue #79 (7OH 2026-11-05 10:01): при загрузке .dt в только что созданную
        // пустую базу «Пользователь ИБ не идентифицирован» — в ней нет пользователей,
        // поэтому /N и /P в командную строку не должны попадать вовсе.
        var credential = TestDeployService.BuildRestoreCredential(isNewBase: true);

        Assert.NotNull(credential);
        Assert.False(credential!.UseInfobaseAuth);
        Assert.Equal(string.Empty, credential.User);
        Assert.Equal(string.Empty, credential.Password);
    }

    [Fact]
    public void RestoreCredential_ExistingBase_KeepsBaseAuth()
    {
        // Обновление существующей копии: пользователи в ней уже есть (пришли из .dt
        // прошлой загрузки) — авторизация берётся из записи базы/общей, как раньше.
        Assert.Null(TestDeployService.BuildRestoreCredential(isNewBase: false));
    }

    [Fact]
    public void RestoreCredential_SuppressedCredential_ProducesNoAuthArgs()
    {
        // Сквозная проверка цепочки, по которой пустой user в подавляющем credential
        // даёт пустую строку авторизации в RunDesignerBatch (обе платформы):
        // BuildCredentialsArg отклоняет пустое имя (IsSafeCliValue("") == false).
        var credential = TestDeployService.BuildRestoreCredential(isNewBase: true)!;

        Assert.True(string.IsNullOrWhiteSpace(credential.User));
        Assert.False(credential.UseInfobaseAuth); // ветка `credential is { UseInfobaseAuth: false }`
    }

    // ---------------- (10) v1.0.1.37: перенос раздельных авторизаций в копию ----------------

    [Fact]
    public void ApplySourceAuthSettings_CopiesEnterpriseAndConfiguratorAuth()
    {
        // issue #79 (7OH 2026-11-05T11:22: «Авторизацию в новую базу перенесло только для
        // Предприятия, для Конфигуратора настройки входа не перенесло»; 14:03: «блоки
        // EnterpriseAuth и ConfiguratorAuth в файле списка баз отсутствуют вообще»):
        // в целевую запись переносятся обе раздельные авторизации и флаг «как для
        // Предприятия» (в CreateTarget раньше копировались только Connection.User/Password).
        var source = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "УТ",
            ConfiguratorUseEnterpriseAuth = true,
            EnterpriseAuth = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "admin",
                Password = "ent-pass"
            },
            ConfiguratorAuth = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "cfg-admin",
                Password = "cfg-pass"
            }
        };
        var target = new Infobase();

        TestDeployService.ApplySourceAuthSettings(source, target);

        Assert.True(target.ConfiguratorUseEnterpriseAuth);
        Assert.NotNull(target.EnterpriseAuth);
        Assert.Equal(AuthenticationMode.Credentials, target.EnterpriseAuth!.AuthenticationMode);
        Assert.Equal("admin", target.EnterpriseAuth.User);
        Assert.Equal("ent-pass", target.EnterpriseAuth.Password);
        Assert.NotNull(target.ConfiguratorAuth);
        Assert.Equal(AuthenticationMode.Credentials, target.ConfiguratorAuth!.AuthenticationMode);
        Assert.Equal("cfg-admin", target.ConfiguratorAuth.User);
        Assert.Equal("cfg-pass", target.ConfiguratorAuth.Password);
    }

    [Fact]
    public void ApplySourceAuthSettings_CopiesAreIndependent()
    {
        // Глубокая копия: изменение авторизации копии не затрагивает источник
        // (и наоборот) — записи списка баз независимы.
        var source = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            EnterpriseAuth = new InfobaseAuthSettings { User = "ent" },
            ConfiguratorAuth = new InfobaseAuthSettings { User = "cfg" }
        };
        var target = new Infobase();

        TestDeployService.ApplySourceAuthSettings(source, target);
        target.EnterpriseAuth!.User = "changed";
        target.ConfiguratorAuth!.AuthenticationMode = AuthenticationMode.Windows;

        Assert.Equal("ent", source.EnterpriseAuth!.User);
        Assert.Equal(AuthenticationMode.Prompt, source.ConfiguratorAuth!.AuthenticationMode);
    }

    [Fact]
    public void ApplySourceAuthSettings_NullSourceAuths_TargetGetsNull()
    {
        // У источника раздельные авторизации не заданы — в копии тоже null
        // (не создаются дефолтные «Запрос»-блоки, которых у оригинала не было).
        var source = new Infobase { Id = Guid.NewGuid().ToString("N") };
        var target = new Infobase();

        TestDeployService.ApplySourceAuthSettings(source, target);

        Assert.Null(target.EnterpriseAuth);
        Assert.Null(target.ConfiguratorAuth);
        Assert.False(target.ConfiguratorUseEnterpriseAuth);
    }

    // ---------------- (11) v1.0.1.37: имя тестовой базы в итоговом сообщении ----------------

    [Fact]
    public void DoneMessage_NoLeftoverPlaceholder_CreatedAndUpdated()
    {
        // issue #79 (7OH 2026-11-05: «не подставило имя в сообщение»): ключи
        // DoneCreated/DoneUpdated содержат {0}, но вызывались без string.Format —
        // в диалоге оставался не заполненный плейсхолдер. Единая точка форматирования
        // (TestDeployService.DoneMessage) никогда не возвращает «сырой» {0}.
        var created = TestDeployService.DoneMessage(new TestDeployResult
        {
            Success = true,
            TargetCreated = true,
            Target = new Infobase { Name = "Тест: Demo 27" }
        });
        var updated = TestDeployService.DoneMessage(new TestDeployResult
        {
            Success = true,
            TargetUpdated = true,
            Target = new Infobase { Name = "Тест: УТ" }
        });

        Assert.DoesNotContain("{0}", created, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void DoneMessage_KeysHaveNamePlaceholder_CallSitesUseFormatter()
    {
        // Ключи локализации содержат {0} (имя тестовой базы), а все четыре места
        // показа итогового сообщения (обе платформы окон и ViewModel) используют
        // DoneMessage — прямой вызов T(DoneCreated/DoneUpdated) без string.Format
        // и есть регрессия «не подставило имя».
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        Assert.Contains("\"TestDeploy.DoneCreated\": \"Тестовая копия «{0}» создана.\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"TestDeploy.DoneUpdated\": \"Тестовая копия «{0}» обновлена.\"", ru, StringComparison.Ordinal);

        foreach (var relativePath in new[]
                 {
                     Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"),
                     Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"),
                     Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Commands.cs"),
                     Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Commands.cs")
                 })
        {
            var code = ReadProjectFile(relativePath);
            Assert.True(
                code.Contains("TestDeployService.DoneMessage(result)", StringComparison.Ordinal),
                $"Ожидается TestDeployService.DoneMessage(result) в {relativePath}");
        }
    }

    // ---------------- (12) v1.0.1.37: восстановление значений окна ----------------

    private static TestDeployScenario MakeScenario(string sourceId, BackupEngine engine, string dbServer, string dbUser)
        => new()
        {
            SourceInfobaseId = sourceId,
            DumpEngine = engine,
            DbServer = dbServer,
            DbUser = dbUser
        };

    [Fact]
    public void ApplyRememberedWindowValues_RestoresEngineDbServerDbUser()
    {
        // issue #79 (7OH 2026-11-05T11:22: «Просьба сохранять значения окна: Движок
        // выгрузки, Сервер СУБД, Пользователь СУБД») — значения последнего сценария
        // исходной базы восстанавливаются при следующем открытии мастера.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        var last = MakeScenario(vm.Source.Id, BackupEngine.Ibcmd, "sqlsrv,1433", "sa");

        vm.ApplyRememberedWindowValues(last);

        Assert.Equal(1, vm.EngineIndex);                       // «Движок выгрузки» — ibcmd
        Assert.Equal("sqlsrv,1433", vm.DbServer);              // «Сервер СУБД»
        Assert.Equal("sa", vm.DbUser);                         // «Пользователь СУБД»
    }

    [Fact]
    public void ApplyRememberedWindowValues_DesignerEngine_KeepsDefaultIndex()
    {
        // Движок «Конфигуратор» в сценарии — индекс формы остаётся дефолтным (0):
        // возвращать его явно незачем, восстанавливается только ibcmd.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        var last = MakeScenario(vm.Source.Id, BackupEngine.Designer, "sqlsrv", "sa");

        vm.ApplyRememberedWindowValues(last);

        Assert.Equal(0, vm.EngineIndex);
        Assert.Equal("sqlsrv", vm.DbServer);
        Assert.Equal("sa", vm.DbUser);
    }

    [Fact]
    public void ApplyRememberedWindowValues_EmptyScenarioValues_DoNotOverwriteForm()
    {
        // Пустые значения сценария (старый JSON без новых полей) не затирают подсказки.
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");
        vm.DbServer = "form-server";
        vm.DbUser = "form-user";
        var last = MakeScenario(vm.Source.Id, BackupEngine.Designer, string.Empty, string.Empty);

        vm.ApplyRememberedWindowValues(last);

        Assert.Equal("form-server", vm.DbServer);
        Assert.Equal("form-user", vm.DbUser);
    }

    [Fact]
    public void ApplyRememberedWindowValues_NullScenario_NoChanges()
    {
        var vm = MakeVm(databaseName: "ut_base", dbName: "ut_base");

        vm.ApplyRememberedWindowValues(null); // не бросает

        Assert.Equal(0, vm.EngineIndex);
        Assert.Equal(string.Empty, vm.DbServer);
        Assert.Equal(string.Empty, vm.DbUser);
    }

    [Fact]
    public void FindLastForSource_ReturnsLastScenarioOfSource()
    {
        // Сценарии накапливаются по одному на успешное развёртывание — берётся
        // последний по списку для нужной исходной базы.
        var sourceId = Guid.NewGuid().ToString("N");
        var otherId = Guid.NewGuid().ToString("N");
        var scenarios = new List<TestDeployScenario>
        {
            MakeScenario(sourceId, BackupEngine.Designer, "old-server", "old-user"),
            MakeScenario(otherId, BackupEngine.Ibcmd, "other-server", "other-user"),
            MakeScenario(sourceId, BackupEngine.Ibcmd, "new-server", "new-user")
        };

        var last = TestDeployStore.FindLastForSource(sourceId, scenarios);

        Assert.NotNull(last);
        Assert.Equal("new-server", last!.DbServer);
        Assert.Equal("new-user", last.DbUser);
    }

    [Fact]
    public void FindLastForSource_NoScenariosOrEmptyId_ReturnsNull()
    {
        Assert.Null(TestDeployStore.FindLastForSource(Guid.NewGuid().ToString("N"), new List<TestDeployScenario>()));
        Assert.Null(TestDeployStore.FindLastForSource(null, new List<TestDeployScenario>
        {
            MakeScenario(Guid.NewGuid().ToString("N"), BackupEngine.Designer, "s", "u")
        }));
    }
}