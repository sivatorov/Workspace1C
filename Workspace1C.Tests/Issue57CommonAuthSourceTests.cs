using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 (1.0.1.7, репорт 7OH «не подставляет ничего»): раздельная авторизация
/// режима в состоянии «Windows с пустым пользователем» («выключена насильственная
/// авторизация») не должна перекрывать явную общую авторизация Credentials — фикс
/// 1.0.1.6 покрыл только шаг 2 (Connection), а шаг 1 (EnterpriseAuth/ConfiguratorAuth)
/// возвращал Windows раньше правки. Также проверяется источник учётных данных
/// (<see cref="InfobaseAuthSource"/>) для диагностики CM_BASE_RUN.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class Issue57CommonAuthSourceTests
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

    // ---- Сценарий пользователя 7OH: раздельная Windows без пользователя + общая Credentials ----

    [Fact]
    public void ModeWindowsEmptyUser_WithCommonCredentials_UsesCommonAuth()
    {
        // Главный кейс: у базы раздельная EnterpriseAuth = Windows с пустым пользователем
        // («выключена насильственная авторизация»), общая Enterprise = Credentials.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void ConfiguratorWindowsEmptyUser_WithCommonCredentials_UsesCommonAuth()
    {
        // То же для «Конфигуратора»: ConfiguratorAuth = Windows без пользователя.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.ConfiguratorAuth = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass, null, Common("cfgU", "cfgP"), null);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("cfgU", user);
        Assert.Equal("cfgP", pass);
    }

    [Fact]
    public void ModeWindowsEmptyUser_WithLegacyCommonCredentials_UsesLegacyCommonAuth()
    {
        // Раздельная Windows без пользователя + только legacy CommonAuthorization.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, null, null, Common("legU", "legP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("legU", user);
        Assert.Equal("legP", pass);
    }

    [Fact]
    public void ModeWindowsEmptyUser_WithoutCommonCredentials_KeepsWindows()
    {
        // Регресс-защита: без общих кредов Windows раздельной авторизации остаётся
        // (платформа получает /WA+), поведение 1.0.1.6 не меняется.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Windows, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void ModeCredentials_StillOverridesCommon()
    {
        // Регресс-защита: явные Credentials раздельной авторизации выше общей.
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("entU", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("entU", user);
        Assert.Equal("entP", pass);
    }

    [Fact]
    public void ModeWindowsWithUser_StillOverridesCommon()
    {
        // Явно заданный пользователь в раздельной Windows-авторизации — не «пустая»
        // Windows: настройка пользователя осознанная и сохраняет приоритет.
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("entU", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Windows, mode);
        Assert.Equal("entU", user);
    }

    // ---- Источник учётных данных (диагностика CM_BASE_RUN, issue #57, 1.0.1.7) ----

    [Fact]
    public void Source_IsCommonMode_WhenModeWindowsEmptyUserOverridden()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, Common("comU", "comP"), null, null, out var source);

        Assert.Equal(InfobaseAuthSource.CommonMode, source);
    }

    [Fact]
    public void Source_IsModeAuth_ForExplicitModeCredentials()
    {
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("entU", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, Common("comU", "comP"), null, null, out var source);

        Assert.Equal(InfobaseAuthSource.ModeAuth, source);
    }

    [Fact]
    public void Source_IsInfobaseConnection_WhenConnectionCredentialsWin()
    {
        var ib = FileBase(AuthenticationMode.Credentials, "connU", "connP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, Common("comU", "comP"), null, null, out var source);

        Assert.Equal(InfobaseAuthSource.InfobaseConnection, source);
    }

    [Fact]
    public void Source_IsCommonLegacy_WhenOnlyLegacyCommonSet()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, null, null, Common("legU", "legP"), out var source);

        Assert.Equal(InfobaseAuthSource.CommonLegacy, source);
    }

    [Fact]
    public void Source_IsPrompt_WhenNothingSet()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, null, null, null, out var source);

        Assert.Equal(InfobaseAuthSource.Prompt, source);
    }

    // ---- Человекочитаемые метки источника для записи CM_BASE_RUN ----

    [Theory]
    [InlineData(InfobaseAuthSource.ModeAuth, "режим базы")]
    [InlineData(InfobaseAuthSource.InfobaseConnection, "база")]
    [InlineData(InfobaseAuthSource.CommonMode, "общая режима")]
    [InlineData(InfobaseAuthSource.CommonLegacy, "общая legacy")]
    [InlineData(InfobaseAuthSource.Prompt, "запрос")]
    public void DescribeSource_ReturnsHumanReadableLabel(InfobaseAuthSource source, string expected)
    {
        Assert.Equal(expected, InfobaseAuthResolver.DescribeSource(source));
    }
}
