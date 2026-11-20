using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #48 (комментарий 7OH 23.10): «Запустить с аутентификацией» обязательно
/// спрашивает имя и пароль — сохранённые учётные данные (в том числе отдельные
/// учётные данные Предприятия/Конфигуратора) не подставляются, автовход по учётной
/// записи ОС отключается ключом /WA-.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class LaunchWithAuthPromptTests : IDisposable
{
    private readonly InfobaseAuthSettings? _enterprise = OneCLauncher.CommonEnterpriseAuthorization;
    private readonly InfobaseAuthSettings? _configurator = OneCLauncher.CommonConfiguratorAuthorization;
    private readonly InfobaseAuthSettings? _legacy = OneCLauncher.CommonAuthorization;

    public LaunchWithAuthPromptTests()
    {
        // Коллекция сериализует тесты, но статические общие авторизации могут остаться
        // от предыдущего теста и перекрыть авторизацию ОС базы (шаг 2 резолвера).
        OneCLauncher.CommonEnterpriseAuthorization = null;
        OneCLauncher.CommonConfiguratorAuthorization = null;
        OneCLauncher.CommonAuthorization = null;
    }

    public void Dispose()
    {
        OneCLauncher.CommonEnterpriseAuthorization = _enterprise;
        OneCLauncher.CommonConfiguratorAuthorization = _configurator;
        OneCLauncher.CommonAuthorization = _legacy;
    }

    private static Infobase FileBase(AuthenticationMode mode, string user = "Админ", string password = "123")
    {
        var ib = new Infobase { Name = "Тест" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = "/tmp/base";
        ib.Connection.AuthenticationMode = mode;
        ib.Connection.User = user;
        ib.Connection.Password = password;
        return ib;
    }

    [Theory]
    [InlineData(OneCLaunchMode.Enterprise)]
    [InlineData(OneCLaunchMode.Configurator)]
    public void SavedCredentials_NotPassed_AndWaMinusAdded(OneCLaunchMode mode)
    {
        var ib = FileBase(AuthenticationMode.Credentials);
        Assert.Contains("/N\"Админ\"", OneCLauncher.BuildArguments(ib, mode, null, null));

        OneCLauncher.RequestAuthPrompt(ib);
        var args = OneCLauncher.BuildArguments(ib, mode, null, null);

        Assert.DoesNotContain("/N\"", args);
        Assert.DoesNotContain("/P\"", args);
        Assert.Contains(" /WA-", args);
        // Учётные данные базы не изменились.
        Assert.Equal("Админ", ib.Connection.User);
        Assert.Equal("123", ib.Connection.Password);
    }

    [Fact]
    public void EnterpriseAuth_SeparateCredentials_NotPassed()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        ib.EnterpriseAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "Предприятие",
            Password = "pwd"
        };
        Assert.Contains("/N\"Предприятие\"", OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null));

        OneCLauncher.RequestAuthPrompt(ib);
        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

        Assert.DoesNotContain("Предприятие", args);
        Assert.Contains(" /WA-", args);
    }

    [Fact]
    public void OsAuthentication_ReplacedWithWaMinus()
    {
        var ib = FileBase(AuthenticationMode.Windows);
        Assert.Contains(" /WA+", OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null));

        OneCLauncher.RequestAuthPrompt(ib);
        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

        Assert.DoesNotContain("/WA+", args);
        Assert.Contains(" /WA-", args);
    }

    [Fact]
    public void LaunchParameters_AuthKeysStripped_OtherKeysKept()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        ib.LaunchParameters = "/N\"Кто\" /P\"Что\" /WA+ /DisableStartupMessages";

        OneCLauncher.RequestAuthPrompt(ib);
        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);

        Assert.DoesNotContain("Кто", args);
        Assert.DoesNotContain("Что", args);
        Assert.DoesNotContain("/WA+", args);
        Assert.Contains("/DisableStartupMessages", args);
        Assert.Contains(" /WA-", args);
    }

    [Fact]
    public void Request_IsOneShot()
    {
        var ib = FileBase(AuthenticationMode.Credentials);
        OneCLauncher.RequestAuthPrompt(ib);
        Assert.Contains(" /WA-", OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null));

        var next = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);
        Assert.DoesNotContain("/WA-", next);
        Assert.Contains("/N\"Админ\"", next);
    }

    [Fact]
    public void CancelledRequest_DoesNotAffectLaunch()
    {
        var ib = FileBase(AuthenticationMode.Credentials);
        OneCLauncher.RequestAuthPrompt(ib);
        OneCLauncher.CancelAuthPrompt(ib);

        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);
        Assert.DoesNotContain("/WA-", args);
        Assert.Contains("/N\"Админ\"", args);
    }
}
