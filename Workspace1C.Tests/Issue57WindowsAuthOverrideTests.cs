using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 (1.0.1.6, комментарий 7OH «общая авторизация не подставляется»):
/// авторизация ОС базы (<see cref="Infobase.Connection"/> с
/// <see cref="AuthenticationMode.Windows"/>) больше не перекрывает явную общую
/// авторизацию Credentials. Раньше база с «выключенной насильной авторизацией»
/// возвращалась на шаге 2 резолвера, и общая никогда не применялась — платформа
/// получала /WA+ без /N /P. Приоритет собственной авторизации базы/режима сохранён.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class Issue57WindowsAuthOverrideTests
{
    private static InfobaseAuthSettings Common(string user, string password,
        AuthenticationMode mode = AuthenticationMode.Credentials)
        => new() { AuthenticationMode = mode, User = user, Password = password };

    private static Infobase FileBase(AuthenticationMode mode, string user, string password)
    {
        var ib = new Infobase { Name = "Тест" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = "/tmp/base";
        ib.Connection.AuthenticationMode = mode;
        ib.Connection.User = user;
        ib.Connection.Password = password;
        return ib;
    }

    // ---- CommonAuthResolverTests: главный баг-кейс 7OH и регрессы ----

    [Fact]
    public void WindowsBase_WithCommonCredentials_UsesCommonAuth()
    {
        // Главный баг-кейс: у базы «выключена насильная авторизация» (режим ОС),
        // общая Enterprise = Credentials → подставляется ОБЩАЯ (issue #57).
        var ib = FileBase(AuthenticationMode.Windows, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void WindowsBase_WithoutCommon_KeepsWindowsMode()
    {
        // Общая не задана → остаётся авторизация ОС базы (платформа получит /WA+).
        var ib = FileBase(AuthenticationMode.Windows, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Windows, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void WindowsBase_WithCommonWindows_KeepsConnectionWindows()
    {
        // Общая Windows (не Credentials) не спорит с авторизацией ОС базы — база wins.
        var ib = FileBase(AuthenticationMode.Windows, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("", "", AuthenticationMode.Windows));

        Assert.Equal(AuthenticationMode.Windows, mode);
    }

    [Fact]
    public void CredentialsBase_WithCommonCredentials_KeepsBaseCredentials()
    {
        // Регресс-защита: база Credentials с пользователем побеждает общую.
        var ib = FileBase(AuthenticationMode.Credentials, "connU", "connP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void PromptBase_WithCommonCredentials_UsesCommonAuth()
    {
        // Регресс: пустая Prompt-база + общая задана → общая.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void PasswordOnlyBase_WithCommonCredentials_UsesCommonAuth()
    {
        // База только с паролем без пользователя — собственных учётных данных нет,
        // применяется общая (старое правило «заполнен пароль → база» убрано).
        var ib = FileBase(AuthenticationMode.Prompt, "", "oldPass");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void OwnEnterpriseAuth_StillOverridesCommon()
    {
        // Регресс-защита: собственная авторизация режима выше базы и общей.
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("entU", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("entU", user);
        Assert.Equal("entP", pass);
    }

    // ---- CommonAuthLaunchArgsTests: аргументы запуска ----

    [Fact]
    public void LaunchArgs_WindowsBase_WithCommonCredentials_HasUserPassword_NoWaPlus()
    {
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            // Общая авторизация перекрыла /WA+ базы: есть /N /P, нет /WA+ (issue #57).
            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
            Assert.DoesNotContain("/WA+", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void LaunchArgs_WindowsBase_WithoutCommon_KeepsWaPlus()
    {
        var ib = FileBase(AuthenticationMode.Windows, "", "");

        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

        // Общая не задана — авторизация ОС базы сохраняется.
        Assert.Contains("/WA+", args);
        Assert.DoesNotContain("/N\"", args);
        Assert.DoesNotContain("/P\"", args);
    }
}

#if !LINUX
/// <summary>
/// Issue #57 (1.0.1.6): COM-строка подключения при базе с авторизацией ОС + общей
/// Credentials — в строке Usr/Pwd из общей, а не пустая строка без Usr.
/// Класс недоступен в Linux-сборке (#if !LINUX, как в CommonAuthComTests).
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class Issue57WindowsAuthComTests
{
    [Fact]
    public void ComConnectString_WindowsBase_WithCommonCredentials_AddsUsrPwd()
    {
        var ib = new Infobase { Name = "Тест" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = "/tmp/base";
        ib.Connection.AuthenticationMode = AuthenticationMode.Windows;
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "comU",
                Password = "comP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Configurator, out _);

            Assert.Contains("Usr=\"comU\"", result);
            Assert.Contains("Pwd=\"comP\"", result);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }
}
#endif
