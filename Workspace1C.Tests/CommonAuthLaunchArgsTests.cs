using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 «добавить общую авторизацию»: общая авторизация подставляется при
/// запуске 1С (<see cref="OneCLauncher.BuildArguments"/>) для ИБ без собственной
/// авторизации и не перекрывает заданную. «Запуск с аутентификацией» (issue #48)
/// по-прежнему подавляет любые учётные данные ключом /WA-.
/// Статик <see cref="OneCLauncher.CommonAuthorization"/> всегда сбрасывается
/// в finally, чтобы не задеть параллельные тесты.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class CommonAuthLaunchArgsTests
{
    private static Infobase FileBase(AuthenticationMode mode, string user = "", string password = "")
    {
        var ib = new Infobase { Name = "Тест" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = "/tmp/base";
        ib.Connection.AuthenticationMode = mode;
        ib.Connection.User = user;
        ib.Connection.Password = password;
        return ib;
    }

    [Fact]
    public void DefaultBase_WithCommonAuth_AddsCredentials()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void OwnConnection_NotOverriddenByCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Credentials, "own", "ownPass");
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"own\"", args);
            Assert.Contains("/P\"ownPass\"", args);
            Assert.DoesNotContain("common", args);
            Assert.DoesNotContain("secret", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void RequestAuthPrompt_SuppressesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            OneCLauncher.RequestAuthPrompt(ib);
            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            // «Запуск с аутентификацией» (issue #48): общая авторизация не подставляется.
            Assert.DoesNotContain("/N\"", args);
            Assert.DoesNotContain("/P\"", args);
            Assert.Contains(" /WA-", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void CommonAuth_WindowsMode_AddsWaPlus()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Windows
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains(" /WA+", args);
            Assert.DoesNotContain("/N\"", args);
            Assert.DoesNotContain("/P\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void ConfiguratorMode_WithCommonAuth_AddsCredentials()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    // ---- Раздельная общая авторизация по режимам запуска (issue #57) ----

    [Fact]
    public void EnterpriseCommonAuth_UsedForEnterpriseMode()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonEnterpriseAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "entCommon",
                Password = "entSecret"
            };

            var entArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);
            Assert.Contains("/N\"entCommon\"", entArgs);
            Assert.Contains("/P\"entSecret\"", entArgs);

            // В режиме «Конфигуратора» раздельная для «1С:Предприятия» не применяется.
            var cfgArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);
            Assert.DoesNotContain("/N\"entCommon\"", cfgArgs);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void ConfiguratorCommonAuth_UsedForConfiguratorMode()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonConfiguratorAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "cfgCommon",
                Password = "cfgSecret"
            };

            var cfgArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);
            Assert.Contains("/N\"cfgCommon\"", cfgArgs);
            Assert.Contains("/P\"cfgSecret\"", cfgArgs);

            // В режиме «1С:Предприятия» раздельная для «Конфигуратора» не применяется.
            var entArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);
            Assert.DoesNotContain("/N\"cfgCommon\"", entArgs);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void ModeSpecificCommonAuth_OverridesLegacy()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            // Раздельная общая для «1С:Предприятия» приоритетнее legacy: в Enterprise
            // идут раздельные учётные данные, и legacy не попадает в аргументы.
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "legacy",
                Password = "legacySecret"
            };
            OneCLauncher.CommonEnterpriseAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "entCommon",
                Password = "entSecret"
            };

            var entArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);
            Assert.Contains("/N\"entCommon\"", entArgs);
            Assert.DoesNotContain("legacy", entArgs);

            // Для «Конфигуратора» раздельная не задана — применяется legacy.
            var cfgArgs = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);
            Assert.Contains("/N\"legacy\"", cfgArgs);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    // ---- BuildAuthArgument (пакетные операции конфигуратора, issue #87) ----

    [Fact]
    public void BuildAuthArgument_WithCommonConfiguratorAuth_ReturnsCredentialArgs()
    {
        // issue #87: база без собственной авторизации + заданная раздельная общая
        // авторизация «Конфигуратора» → пакетный запуск получает /N /P.
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonConfiguratorAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "cfgUser",
                Password = "cfgPass"
            };

            var auth = OneCLauncher.BuildAuthArgument(ib);

            Assert.Equal(" /N\"cfgUser\" /P\"cfgPass\"", auth);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void BuildAuthArgument_WithOwnConfiguratorAuth_PrefersBaseAuth()
    {
        // База с собственной авторизацией Конфигуратора → её креды, общая не перекрывает.
        var ib = FileBase(AuthenticationMode.Prompt);
        ib.ConfiguratorAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "own",
            Password = "ownPass"
        };
        try
        {
            OneCLauncher.CommonConfiguratorAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "commonPass"
            };

            var auth = OneCLauncher.BuildAuthArgument(ib);

            Assert.Equal(" /N\"own\" /P\"ownPass\"", auth);
            Assert.DoesNotContain("common", auth);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void BuildAuthArgument_WithLegacyCommonAuth_ReturnsCredentialArgs()
    {
        // issue #87: при незаданной раздельной общей авторизации «Конфигуратора»
        // подставляется legacy CommonAuthorization.
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "legacyUser",
                Password = "legacyPass"
            };

            var auth = OneCLauncher.BuildAuthArgument(ib);

            Assert.Equal(" /N\"legacyUser\" /P\"legacyPass\"", auth);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void BuildAuthArgument_NoAuth_ReturnsEmpty()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            Assert.Equal(string.Empty, OneCLauncher.BuildAuthArgument(ib));
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }
}