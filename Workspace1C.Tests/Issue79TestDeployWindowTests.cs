using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #79 «Развернуть тестовую копию» (последний комментарий 7OH, 2026-10-28):
/// (1) единое оформление полей ввода — тематический ModernTextBox вместо дефолтных
/// «серых» полей без окантовки; (2) автоподстановка «Имя базы данных СУБД» из
/// «Имя базы на сервере»; (3) кнопки «Свойства исходной базы» и «Конфигуратор»,
/// чтобы админ мог открыть свойства базы или конфигуратор, не выходя из мастера.
/// Чистая логика (автоподстановка) проверяется на ViewModel; разметочные части —
/// чтением исходников обеих платформ (как Issue71SettingsWindowLayoutTests).
/// </summary>
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
}