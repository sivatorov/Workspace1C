using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #67 «Подстановки для сценариев»: сценарий понимает ВСЕ реквизиты базы
/// (включая логин при общей авторизации) + диалог «Выбрать» переменную.
/// </summary>
[Collection("ScriptLaunchContextState")]
public sealed class ScriptTokenPickerViewModelTests
{
    private static Infobase BuildBase(string? user = null, string? password = null)
    {
        return new Infobase
        {
            Name = "Бухгалтерия",
            Group = "Бухгалтерия",
            Id = "ib-1",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-01",
                Port = 1541,
                DatabaseName = "buho",
                User = user ?? string.Empty,
                Password = password ?? string.Empty
            }
        };
    }

    [Fact]
    public void AvailableTokens_IncludesConnectionUser()
    {
        // «Подставить логин оно пока не может» — токен логина теперь в справочнике.
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "%connection.user%");
    }

    [Fact]
    public void AvailableTokens_IncludesGroupAndId()
    {
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "%id%");
        Assert.Contains(tokens, t => t.Token == "%имягруппы%");
    }

    [Fact]
    public void BuildValueMap_OwnCredentials_TakesPrecedenceOverResolved()
    {
        var infobase = BuildBase(user: "own-user", password: "own-pass");
        var resolved = ("common-user", "common-pass");

        var map = ScriptParameterResolver.BuildValueMap(infobase, resolved);

        Assert.Equal("own-user", map["connection.user"]);
        Assert.Equal("own-pass", map["connection.password"]);
    }

    [Fact]
    public void BuildValueMap_EmptyOwnCredentials_UsesResolvedLogin()
    {
        // База без собственных учётных данных (общая авторизация issue #57):
        // в карту подставляется логин, который реально подставит стартер.
        var infobase = BuildBase();
        var resolved = ("common-user", "common-pass");

        var map = ScriptParameterResolver.BuildValueMap(infobase, resolved);

        Assert.Equal("common-user", map["connection.user"]);
        Assert.Equal("common-pass", map["connection.password"]);
    }

    [Fact]
    public void TokenPicker_BuildOptions_IncludesStaticAndDynamicTokens()
    {
        var infobase = BuildBase();
        var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

        // Статический токен логина есть; динамический ключ карты (например id) тоже.
        Assert.Contains(options, o => o.Token == "%connection.user%");
        Assert.Contains(options, o => o.Token == "%id%");
    }

    [Fact]
    public void TokenPicker_FilterOptions_MatchesTokenAndValue_IgnoreCase()
    {
        var infobase = BuildBase();
        var all = ScriptTokenPickerViewModel.BuildOptions(infobase);

        var byToken = ScriptTokenPickerViewModel.FilterOptions(all, "connection.server");
        var byValue = ScriptTokenPickerViewModel.FilterOptions(all, "srv-01");

        Assert.Contains(byToken, o => o.Token == "%connection.server%");
        Assert.Contains(byValue, o => o.Value == "srv-01");
    }

    [Fact]
    public void TokenPicker_FilterOptions_EmptyQuery_ReturnsAll()
    {
        var infobase = BuildBase();
        var all = ScriptTokenPickerViewModel.BuildOptions(infobase);

        var filtered = ScriptTokenPickerViewModel.FilterOptions(all, "   ");

        Assert.Equal(all.Count, filtered.Count);
    }

    [Fact]
    public void TokenOption_PasswordTokens_MaskedInDisplay()
    {
        var option = new ScriptTokenOption("%connection.password%", "secret");

        Assert.True(option.IsSecret);
        Assert.Equal("••••••", option.DisplayValue);
    }

    [Fact]
    public void AvailableTokens_IncludesFullLaunchTokens_Issue67()
    {
        // Токены из первого сообщения issue #67: полные строки запуска, путь к exe и версия.
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "%full_launch_enterprise%");
        Assert.Contains(tokens, t => t.Token == "%full_launch_config%");
        Assert.Contains(tokens, t => t.Token == "%full_exe_name%");
        Assert.Contains(tokens, t => t.Token == "%platform_version%");
    }

    [Fact]
    public void TokenPicker_BuildOptions_IncludesFullLaunchTokens_WithValues()
    {
        var infobase = BuildBase();
        var ctx = new ScriptLaunchContext(
            ExePath: @"C:\1cv8\bin\1cv8.exe",
            PlatformVersion: "8.5.4.1878",
            EnterpriseCommandLine: @"""C:\1cv8\bin\1cv8.exe"" ENTERPRISE /S""srv-01:1541\buho""",
            ConfiguratorCommandLine: @"""C:\1cv8\bin\1cv8.exe"" DESIGNER /S""srv-01:1541\buho""");
        ScriptLaunchContextResolver.ResolverOverride = _ => ctx;
        try
        {
            var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

            var exe = Assert.Single(options, o => o.Token == "%full_exe_name%");
            var version = Assert.Single(options, o => o.Token == "%platform_version%");
            var enterprise = Assert.Single(options, o => o.Token == "%full_launch_enterprise%");
            var configurator = Assert.Single(options, o => o.Token == "%full_launch_config%");

            Assert.Equal(ctx.ExePath, exe.Value);
            Assert.Equal(ctx.PlatformVersion, version.Value);
            Assert.Equal(ctx.EnterpriseCommandLine, enterprise.Value);
            Assert.Equal(ctx.ConfiguratorCommandLine, configurator.Value);
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void TokenPicker_BuildOptions_CoversEveryMapKey_OneToOne()
    {
        // «Не должно быть отличия в списке доступных полей и в полях структуры базы»
        // (issue #67): для КАЖДОГО ключа карты подстановок в диалоге есть соответствующая
        // строка «%ключ%» — структура базы 1-в-1.
        var infobase = BuildBase();
        infobase.Tags = new List<string> { "оперативка", "бэкап" };
        infobase.AdditionalFields = new List<string> { "Поле1" };

        ScriptLaunchContextResolver.ResolverOverride = _ => null;
        try
        {
            var map = ScriptParameterResolver.BuildValueMap(infobase, null, null);
            var options = ScriptTokenPickerViewModel.BuildOptions(infobase);
            var tokens = options.Select(o => o.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in map.Keys)
                Assert.True(tokens.Contains("%" + key + "%"), $"Ключ структуры базы отсутствует в диалоге: %{key}%");
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void TokenPicker_BuildOptions_GroupedBySections_WithSingleStartPerSection()
    {
        // Issue #67: список сгруппирован по секциям (Основные/Подключение/Платформа/Дата)
        // — перед каждой секцией ровно одна строка-«заголовок» (SectionStart).
        var infobase = BuildBase();
        ScriptLaunchContextResolver.ResolverOverride = _ => null;
        try
        {
            var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

            Assert.All(options, o => Assert.False(string.IsNullOrEmpty(o.SectionKey)));
            Assert.True(options[0].SectionStart);
            Assert.Equal(
                options.Select(o => o.SectionKey).Distinct().Count(),
                options.Count(o => o.SectionStart));
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void TokenPicker_FilterOptions_RecomputesSectionStarts()
    {
        // После фильтрации флаги начала секций пересчитываются — секции не «висят» в воздухе.
        var infobase = BuildBase();
        ScriptLaunchContextResolver.ResolverOverride = _ => null;
        try
        {
            var all = ScriptTokenPickerViewModel.BuildOptions(infobase);
            var filtered = ScriptTokenPickerViewModel.FilterOptions(all, "connection");

            Assert.NotEmpty(filtered);
            Assert.Equal(
                filtered.Select(o => o.SectionKey).Distinct().Count(),
                filtered.Count(o => o.SectionStart));
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void BuildValueMap_IncludesDerivedStructureKeys()
    {
        // Производные ключи «структуры базы» (issue #67): группа, теги и произвольные поля.
        var infobase = BuildBase();
        infobase.Tags = new List<string> { "оперативка" };
        infobase.AdditionalFields = new List<string> { "Поле1", "Поле2" };

        var map = ScriptParameterResolver.BuildValueMap(infobase, null, null);

        Assert.Equal("Бухгалтерия", map["группа"]);
        Assert.Equal("оперативка", map["тэги"]);
        Assert.Equal("оперативка", map["tags"]);
        Assert.Equal("Поле1; Поле2", map["дополнительныеполя"]);
        Assert.Equal("Поле1; Поле2", map["extrafields"]);
    }

    // ---------------- Раздельная авторизация и пояснения (issue #67) ----------------

    [Fact]
    public void AvailableTokens_IncludesSeparateAuthTokens()
    {
        // «ConfiguratorAuth напрочь отсутствует»: токены раздельной авторизации теперь
        // в справочнике редактора (и, как следствие, в секции «Авторизация» пикера).
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;
        var set = tokens.Select(t => t.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("%enterpriseauth.user%", set);
        Assert.Contains("%enterpriseauth.password%", set);
        Assert.Contains("%enterpriseauth.mode%", set);
        Assert.Contains("%configuratorauth.user%", set);
        Assert.Contains("%configuratorauth.password%", set);
        Assert.Contains("%configuratorauth.mode%", set);
    }

    [Fact]
    public void TokenPicker_BuildOptions_IncludesAuthSection()
    {
        var infobase = BuildBase();
        infobase.EnterpriseAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "ent", Password = "ep"
        };
        infobase.ConfiguratorAuth = new InfobaseAuthSettings
        {
            AuthenticationMode = AuthenticationMode.Credentials,
            User = "cfg", Password = "cp"
        };
        ScriptLaunchContextResolver.ResolverOverride = _ => null;
        try
        {
            var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

            // Секция «Авторизация» присутствует и содержит все шесть токенов.
            var authOptions = options.Where(o => o.SectionKey == "Script.TokenSectionAuth").ToList();
            Assert.NotEmpty(authOptions);
            Assert.Contains(authOptions, o => o.Token == "%enterpriseauth.user%" && o.Value == "ent");
            Assert.Contains(authOptions, o => o.Token == "%enterpriseauth.password%" && o.Value == "ep");
            Assert.Contains(authOptions, o => o.Token == "%configuratorauth.user%" && o.Value == "cfg");
            Assert.Contains(authOptions, o => o.Token == "%configuratorauth.password%" && o.Value == "cp");
            Assert.Contains(authOptions, o => o.Token == "%configuratorauth.mode%" && o.Value == "Credentials");
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void TokenPicker_EveryOption_HasNonEmptyDisplayDescription()
    {
        // Пояснения (issue #67): у каждого ключа есть описание — статическое из
        // AvailableTokens, динамическое из каталога или универсальная подпись.
        var infobase = BuildBase();
        infobase.Tags = new List<string> { "т" };
        infobase.AdditionalFields = new List<string> { "a" };
        ScriptLaunchContextResolver.ResolverOverride = _ => null;
        try
        {
            var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

            Assert.NotEmpty(options);
            foreach (var option in options)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.DisplayDescription),
                    $"Токен {option.Token} не имеет пояснения (DisplayDescription).");
            }
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = null;
        }
    }

    [Fact]
    public void TokenPicker_FilterOptions_MatchesDescription()
    {
        // Поиск ищет и по пояснению («раздельная авторизация…»), не только по имени
        // и значению. Строки создаются напрямую — тест не зависит от состояния
        // локализации в тестовом окружении.
        var options = new System.Collections.Generic.List<ScriptTokenOption>
        {
            new ScriptTokenOption("%configuratorauth.user%", "cfg", description: "логин раздельной авторизации Конфигуратора"),
            new ScriptTokenOption("%connection.user%", "администратор", description: "логин из настроек подключения базы; если пусто — из общей/раздельной авторизации"),
            new ScriptTokenOption("%name%", "База", description: "имя базы")
        };

        // Запрос в родительном падеже — совпадает с пояснением «…раздельной авторизации…».
        var byDescription = ScriptTokenPickerViewModel.FilterOptions(options, "раздельной авторизации");

        Assert.Contains(byDescription, o => o.Token == "%configuratorauth.user%");
        Assert.Contains(byDescription, o => o.Token == "%connection.user%");
        Assert.DoesNotContain(byDescription, o => o.Token == "%name%");
    }

    [Fact]
    public void TokenOption_SeparateAuthPasswordTokens_MaskedInDisplay()
    {
        // Политика маскирования (как для connection.password): пароли раздельной
        // авторизации не светятся в списке значений.
        var enterprise = new ScriptTokenOption("%enterpriseauth.password%", "secret1");
        var configurator = new ScriptTokenOption("%configuratorauth.password%", "secret2");

        Assert.True(enterprise.IsSecret);
        Assert.Equal("••••••", enterprise.DisplayValue);
        Assert.True(configurator.IsSecret);
        Assert.Equal("••••••", configurator.DisplayValue);
    }
}