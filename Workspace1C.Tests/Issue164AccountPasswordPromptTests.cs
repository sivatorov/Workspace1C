using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #164 «Не спрашивает пароль при старте» (план 1.0.1.120, T1):
/// <list type="bullet">
/// <item>окно входа показывается и для единственной защищённой паролем записи;</item>
/// <item>в окне видно «Пароль учётной записи <b>Имя</b>» (имя жирным) в WPF и Avalonia;</item>
/// <item>токен <c>%app_user%</c> подставляет имя активной учётной записи;</item>
/// <item>ключи локализации добавлены в ru/en.</item>
/// </list>
/// </summary>
public sealed class Issue164AccountPasswordPromptTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    private static string ReadLanguage(string language) =>
        ReadProjectFile("Workspace1C", "Localization", "Languages", language + ".json");

    /// <summary>
    /// Та же логика, что в App.xaml.cs / App.axaml.cs: вход нужен, если учётных
    /// записей несколько ИЛИ активная запись защищена паролем. Безопасно к null
    /// (повреждённый profiles.json).
    /// </summary>
    private static bool RequiresLogin(int profilesCount, bool? currentHasPassword) =>
        profilesCount > 1 || currentHasPassword == true;

    [Fact]
    public void SingleProtectedProfile_RequiresPasswordPrompt()
    {
        // Source-guard: условие показа окна входа в обеих оболочках учитывает пароль.
        foreach (var file in new[] { "App.xaml.cs", "App.axaml.cs" })
        {
            var source = ReadProjectFile("Workspace1C", file);
            Assert.Contains(
                "profileService.Profiles.Count > 1 || profileService.CurrentProfile?.HasPassword == true",
                source,
                StringComparison.Ordinal);
        }

        // Поведение: одна защищённая запись — вход с паролем обязателен.
        Assert.True(RequiresLogin(1, true));

        // Отмена входа по-прежнему завершает приложение: Shutdown() остаётся в обеих ветках.
        Assert.Contains("Shutdown();", ReadProjectFile("Workspace1C", "App.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("Shutdown();", ReadProjectFile("Workspace1C", "App.axaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SingleUnprotectedProfile_NoPrompt()
    {
        // Одна незащищённая запись — вход без запроса пароля.
        Assert.False(RequiresLogin(1, false));

        // Несколько записей — выбор профиля нужен (как раньше).
        Assert.True(RequiresLogin(2, false));

        // Повреждённый profiles.json: CurrentProfile == null — не падаем и не показываем вход.
        Assert.False(RequiresLogin(1, null));
        Assert.False(RequiresLogin(0, null));
    }

    [Fact]
    public void LoginWindow_ShowsAccountNameInPasswordLabel()
    {
        // WPF: подпись собирается из формата, имя — отдельный Run с полужирным начертанием.
        var wpfXaml = ReadProjectFile("Workspace1C", "Views", "LoginWindow.xaml");
        Assert.Contains("PasswordLabelAccount", wpfXaml, StringComparison.Ordinal);
        Assert.Contains("FontWeight=\"Bold\"", wpfXaml, StringComparison.Ordinal);

        var wpfCode = ReadProjectFile("Workspace1C", "Views", "LoginWindow.xaml.cs");
        Assert.Contains("Auth.PasswordForAccountFormat", wpfCode, StringComparison.Ordinal);
        Assert.Contains("profile?.Name", wpfCode, StringComparison.Ordinal);

        // Avalonia: подпись — TextBlock с Inlines, имя — Run с FontWeight.Bold.
        var avaloniaCode = ReadProjectFile("Workspace1C", "Views", "LoginWindow.Avalonia.cs");
        Assert.Contains("Auth.PasswordForAccountFormat", avaloniaCode, StringComparison.Ordinal);
        Assert.Contains("FontWeight = FontWeight.Bold", avaloniaCode, StringComparison.Ordinal);
        Assert.Contains("profile?.Name", avaloniaCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleSuffix_AppUserToken_ResolvesProfileName()
    {
        Assert.Equal("%app_user%", TitleSuffixResolver.AppUserToken);
        Assert.Contains(TitleSuffixResolver.AppUserToken, TitleSuffixResolver.Tokens);

        var resolved = TitleSuffixResolver.Resolve("%app_user%|%computer_name%", "Иван");
        Assert.Contains("Иван", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain(TitleSuffixResolver.AppUserToken, resolved, StringComparison.Ordinal);

        // Через Compose имя активной записи тоже подставляется в заголовок.
        var title = TitleSuffixResolver.Compose(
            "Workspace 1C", "1.0.1.119", enabled: true, "%app_user%", appUser: "Иван");
        Assert.Equal("Workspace 1C v1.0.1.119 — Иван", title);
    }

    [Fact]
    public void TitleSuffix_UnknownAppUser_Empty()
    {
        // Имя неизвестно (нет активного профиля) — токен заменяется пустой строкой.
        Assert.Equal(string.Empty, TitleSuffixResolver.Resolve("%app_user%", null));
        Assert.Equal(string.Empty, TitleSuffixResolver.Resolve("%app_user%", string.Empty));
        Assert.Equal("xy", TitleSuffixResolver.Resolve("x%app_user%y", null));

        // Неизвестные токены по-прежнему сохраняются как есть.
        Assert.Equal("%unknown%", TitleSuffixResolver.Resolve("%unknown%", null));
    }

    /// <summary>
    /// Issue #164 (1.0.1.123): повторный запуск ярлыком при работающем экземпляре.
    /// Второй экземпляр при запрете нескольких запусков активирует существующее окно
    /// и не показывает вход; при <c>AllowMultipleInstances=true</c> запускается полноценно;
    /// первичный экземпляр идёт обычным путём.
    /// </summary>
    [Fact]
    public void StartupInstancePolicy_SecondInstance_ActivatesExistingWithoutLogin()
    {
        const bool allowMultiple = false;
        const bool createdNew = false; // блокировка занята первым экземпляром

        Assert.True(StartupInstancePolicy.ShouldUseSingleInstanceGate(allowMultiple));
        Assert.True(StartupInstancePolicy.ShouldActivateExistingInstance(allowMultiple, createdNew));
        Assert.False(StartupInstancePolicy.IsPrimaryInstance(allowMultiple, createdNew));

        // Второй экземпляр завершается до окна входа: даже если профиль защищён паролем,
        // вход не показывается — окно активирует уже работающее приложение.
        Assert.False(StartupInstancePolicy.ShouldShowLogin(
            activateExistingInstance: true, requiresLogin: true));
    }

    [Fact]
    public void StartupInstancePolicy_AllowMultipleInstances_StartsFully()
    {
        const bool allowMultiple = true;
        const bool createdNew = false; // гейт не используется, блокировки нет

        Assert.False(StartupInstancePolicy.ShouldUseSingleInstanceGate(allowMultiple));
        Assert.False(StartupInstancePolicy.ShouldActivateExistingInstance(allowMultiple, createdNew));
        Assert.True(StartupInstancePolicy.IsPrimaryInstance(allowMultiple, createdNew));

        // Вход и обычный запуск работают как прежде (регрессии нет).
        Assert.True(StartupInstancePolicy.ShouldShowLogin(
            activateExistingInstance: false, requiresLogin: true));
        Assert.False(StartupInstancePolicy.ShouldShowLogin(
            activateExistingInstance: false, requiresLogin: false));
    }

    [Fact]
    public void StartupInstancePolicy_PrimaryInstance_NormalStartup()
    {
        const bool allowMultiple = false;
        const bool createdNew = true; // этот процесс создал блокировку

        Assert.True(StartupInstancePolicy.ShouldUseSingleInstanceGate(allowMultiple));
        Assert.False(StartupInstancePolicy.ShouldActivateExistingInstance(allowMultiple, createdNew));
        Assert.True(StartupInstancePolicy.IsPrimaryInstance(allowMultiple, createdNew));

        // Первичный экземпляр показывает вход, если это требуется профилем.
        Assert.True(StartupInstancePolicy.ShouldShowLogin(
            activateExistingInstance: false, requiresLogin: true));
    }

    /// <summary>
    /// Регрессионный source-guard: в обеих оболочках проверка одиночного экземпляра
    /// (и обработка CLI-команд в WPF) стоит ДО <c>LoginWindow.ShowLogin</c>, иначе
    /// второй процесс снова спросит пароль до активации существующего окна.
    /// </summary>
    [Fact]
    public void StartupGate_RunsBeforeLoginWindow_InBothShells()
    {
        foreach (var file in new[] { "App.xaml.cs", "App.axaml.cs" })
        {
            var source = ReadProjectFile("Workspace1C", file);

            var gateIndex = source.IndexOf(
                "StartupInstancePolicy.ShouldActivateExistingInstance", StringComparison.Ordinal);
            var loginIndex = source.IndexOf("LoginWindow.ShowLogin(", StringComparison.Ordinal);
            var listenerIndex = source.IndexOf("StartActivationListener();", StringComparison.Ordinal);

            Assert.True(gateIndex >= 0, $"{file}: нет вызова StartupInstancePolicy");
            Assert.True(loginIndex >= 0, $"{file}: нет вызова LoginWindow.ShowLogin");
            Assert.True(listenerIndex >= 0, $"{file}: нет запуска StartActivationListener");

            Assert.True(gateIndex < loginIndex,
                $"{file}: гейт одиночного экземпляра должен стоять до окна входа");
            Assert.True(gateIndex < listenerIndex,
                $"{file}: StartActivationListener запускается после решения гейта");
        }

        // WPF: команды --register/--launch/--designer обрабатываются до гейта (как раньше).
        var wpf = ReadProjectFile("Workspace1C", "App.xaml.cs");
        var cliIndex = wpf.IndexOf("ExplorerCommandLine.TryHandle(e.Args)", StringComparison.Ordinal);
        var wpfGateIndex = wpf.IndexOf(
            "StartupInstancePolicy.ShouldActivateExistingInstance", StringComparison.Ordinal);
        Assert.True(cliIndex >= 0, "App.xaml.cs: нет обработки CLI-команд");
        Assert.True(cliIndex < wpfGateIndex,
            "App.xaml.cs: CLI-команды должны обрабатываться до гейта одиночного экземпляра");
    }

    [Fact]
    public void Localization_HasAccountPasswordKeys()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            using var document = JsonDocument.Parse(ReadLanguage(language));
            // Строки локализации лежат во вложенном объекте "strings".
            var strings = document.RootElement.GetProperty("strings");

            Assert.True(strings.TryGetProperty("Auth.PasswordForAccountFormat", out var format),
                $"Нет ключа Auth.PasswordForAccountFormat в {language}.json");
            Assert.Contains("{0}", format.GetString(), StringComparison.Ordinal);

            Assert.True(strings.TryGetProperty("Auth.EnterPasswordHint", out var hint),
                $"Нет ключа Auth.EnterPasswordHint в {language}.json");
            Assert.False(string.IsNullOrWhiteSpace(hint.GetString()));

            // Существующий ключ сохраняется для обратной совместимости.
            Assert.True(strings.TryGetProperty("Auth.Password", out _),
                $"Ключ Auth.Password должен остаться в {language}.json");
        }
    }
}
