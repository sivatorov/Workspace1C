using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 (1.0.1.8, репорт «откуда пустой пользователь взялся»): раздельная
/// авторизация режима <see cref="AuthenticationMode.Credentials"/> с ПУСТЫМ
/// пользователем при наличии общих кредов режима считается незаданной — шаг 1
/// резолвера переходит к шагу 3 (общая), а <see cref="OneCLauncher.BuildArguments"/>
/// не подставляет пустой /N. Фикс 1.0.1.7 покрывал только Windows-режим раздельной
/// авторизации; Credentials с пустым пользователем возвращался как есть.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class Issue57EmptyModeUserTests
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

    // ---- Резолвер: шаг 1 пропускает Credentials с пустым пользователем ----

    [Fact]
    public void ModeCredentialsEmptyUser_WithCommonCredentials_UsesCommonAuth()
    {
        // Главный кейс: раздельная EnterpriseAuth = Credentials с пустым пользователем
        // («сломанный» кейс), общая Enterprise = Credentials → применяется ОБЩАЯ.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void ConfiguratorCredentialsEmptyUser_WithCommonCredentials_UsesCommonAuth()
    {
        // То же для «Конфигуратора»: ConfiguratorAuth = Credentials без пользователя.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.ConfiguratorAuth = Common("", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass, null, Common("cfgU", "cfgP"), null);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("cfgU", user);
        Assert.Equal("cfgP", pass);
    }

    [Fact]
    public void ModeCredentialsEmptyUser_WithLegacyCommonCredentials_UsesLegacyCommonAuth()
    {
        // Раздельная Credentials без пользователя + только legacy CommonAuthorization.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, null, null, Common("legU", "legP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("legU", user);
        Assert.Equal("legP", pass);
    }

    [Fact]
    public void ModeCredentialsEmptyUser_WithoutCommonCredentials_KeepsModeAuth()
    {
        // Регресс-защита: без общих кредов Credentials с пустым пользователем
        // возвращается как есть (платформа покажет окно логина), как раньше.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void ModeCredentialsWithUser_StillOverridesCommon()
    {
        // Регресс-защита: раздельная Credentials С пользователем — осознанная настройка,
        // приоритет сохраняется над общей.
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("entU", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("entU", user);
        Assert.Equal("entP", pass);
    }

    [Fact]
    public void ModeCredentialsPasswordOnly_WithCommonCredentials_UsesCommonAuth()
    {
        // Пароль без пользователя — тоже «пустой пользователь»: собственными
        // учётными данными это не считается, применяется общая (семантика фикса
        // 1.0.1.8 — фильтр по пустому пользователю не зависит от пароля).
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void ModeCredentialsPasswordOnly_WithoutCommon_KeepsModeAuth()
    {
        // Без общих кредов Credentials с паролем (но без пользователя) возвращается
        // как есть — платформа получит /N"" и покажет окно логина с паролем.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out var user, out var pass);

        Assert.Equal(string.Empty, user);
        Assert.Equal("entP", pass);
    }

    // ---- Источник учётных данных (диагностика CM_BASE_RUN) ----

    [Fact]
    public void Source_IsCommonMode_WhenModeCredentialsEmptyUserOverridden()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out _, out _, Common("comU", "comP"), null, null, out var source);

        Assert.Equal(InfobaseAuthSource.CommonMode, source);
    }

    // ---- Аргументы запуска: пустой пользователь не подставляется ----

    [Fact]
    public void LaunchArgs_ModeCredentialsEmptyUser_WithCommonCredentials_PassesCommonUser()
    {
        // Конец в конец: база с «сломанной» раздельной Credentials + общая раздельная
        // → в командную строку уходят общий логин/пароль, без пустого /N"" и без /WA+.
        var ib = FileBase(AuthenticationMode.Windows, "", "");
        ib.EnterpriseAuth = Common("", "");
        try
        {
            OneCLauncher.CommonEnterpriseAuthorization = Common("common", "secret");

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
            Assert.DoesNotContain("/WA+", args);
            // Пустой пользователь не подставляется.
            Assert.DoesNotContain("/N\"\"", args);
        }
        finally
        {
            OneCLauncher.CommonEnterpriseAuthorization = null;
        }
    }

    [Fact]
    public void LaunchArgs_ModeCredentialsEmptyUser_WithoutCommon_NoUserNoWaPlus()
    {
        // Без общих кредов: Credentials с пустым пользователем не даёт ни /N"", ни /WA+ —
        // платформа сама спросит имя и пароль (пустой /N не подставляется).
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("", "");

        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

        Assert.DoesNotContain("/N\"", args);
        Assert.DoesNotContain("/P\"", args);
        Assert.DoesNotContain("/WA", args);
    }

    [Fact]
    public void LaunchArgs_WindowsBase_WithoutCommon_KeepsWaPlus()
    {
        // Регресс по плану (issue #57): Windows-режим базы без общих кредов → /WA+.
        var ib = FileBase(AuthenticationMode.Windows, "", "");

        var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

        Assert.Contains("/WA+", args);
        Assert.DoesNotContain("/N\"", args);
        Assert.DoesNotContain("/P\"", args);
    }

    [Fact]
    public void LaunchArgs_PromptBase_WithCommonCredentials_PassesCommonUser()
    {
        // Регресс по плану (issue #57): база «Запрос» + общая Credentials → общий
        // логин/пароль в аргументах.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        try
        {
            OneCLauncher.CommonAuthorization = Common("common", "secret");

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }
}
