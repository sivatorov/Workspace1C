using System;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты подстановки/автосбора профиля СУБД в мастере «Развернуть тестовую копию»
/// (issue #79, v100113): <see cref="TestDeployViewModel.AutofillDbmsFromProfile"/> —
/// кнопка «Из профиля», <see cref="TestDeployViewModel.EnsureIbcmdProfileFromForm"/> —
/// автоматический сбор профиля из полей формы при запуске развёртывания.
/// </summary>
public sealed class TestDeployViewModelProfileTests
{
    private static Infobase MakeClientServerSource()
    {
        var ib = new Infobase { Name = "Прод" };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = "srv01";
        ib.Connection.DatabaseName = "proddb";
        return ib;
    }

    private static DbmsBackupProfile MakeProfile() => new()
    {
        InfobaseId = "id-1",
        Dbms = DbmsKind.PostgreSql,
        Host = "pg01",
        Database = "proddb",
        User = "admin",
        Password = "secret"
    };

    [Fact]
    public void AutofillDbmsFromProfile_FillsFormFields_FromResolver()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            proposedTargetName: "Прод_Тест", proposedServerAddress: "srv01",
            proposedDatabaseName: "proddb_test", proposedFilePath: string.Empty);

        Assert.True(vm.AutofillDbmsFromProfile(_ => MakeProfile()));

        Assert.Equal("PostgreSQL", vm.Dbms);
        Assert.False(string.IsNullOrWhiteSpace(vm.DbServer));
        Assert.Equal("proddb", vm.DbName);
        Assert.Equal("admin", vm.DbUser);
        Assert.Equal("secret", vm.DbPassword);
    }

    [Fact]
    public void AutofillDbmsFromProfile_ReturnsFalse_WhenNoProfile()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            "Прод_Тест", "srv01", "proddb_test", string.Empty);

        Assert.False(vm.AutofillDbmsFromProfile(_ => null));
        Assert.Equal(string.Empty, vm.DbName);
    }

    [Fact]
    public void EnsureIbcmdProfileFromForm_IbcmdWithEmptyFields_FillsFromProfile()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            "Прод_Тест", "srv01", "proddb_test", string.Empty);
        vm.EngineIndex = 1; // ibcmd

        Assert.True(vm.EnsureIbcmdProfileFromForm(_ => MakeProfile()));
        // Профиль собран в поля формы — повторное ручное заполнение не требуется.
        Assert.Equal("proddb", vm.DbName);
        Assert.False(string.IsNullOrWhiteSpace(vm.DbServer));
    }

    [Fact]
    public void EnsureIbcmdProfileFromForm_FormAlreadyFilled_KeepsFormValues()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            "Прод_Тест", "srv01", "proddb_test", string.Empty);
        vm.EngineIndex = 1;
        vm.DbServer = "manual-host";
        vm.DbName = "manual-db";

        Assert.True(vm.EnsureIbcmdProfileFromForm(_ => MakeProfile()));
        // Работаем по значениям на форме — профиль их не перетирает.
        Assert.Equal("manual-host", vm.DbServer);
        Assert.Equal("manual-db", vm.DbName);
    }

    [Fact]
    public void EnsureIbcmdProfileFromForm_NoProfile_ReturnsFalse()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            "Прод_Тест", "srv01", "proddb_test", string.Empty);
        vm.EngineIndex = 1;

        Assert.False(vm.EnsureIbcmdProfileFromForm(_ => null));
    }

    [Fact]
    public void EnsureIbcmdProfileFromForm_DesignerEngine_ReturnsTrueWithoutProfile()
    {
        var vm = new TestDeployViewModel(
            MakeClientServerSource(), Array.Empty<Infobase>(), Array.Empty<Group>(),
            "Прод_Тест", "srv01", "proddb_test", string.Empty);
        vm.EngineIndex = 0; // Конфигуратор — ibcmd-профиль не нужен.

        Assert.True(vm.EnsureIbcmdProfileFromForm(_ => null));
        Assert.Equal(string.Empty, vm.DbName);
    }
}
