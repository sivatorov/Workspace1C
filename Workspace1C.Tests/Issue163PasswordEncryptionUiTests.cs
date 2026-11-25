using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Secrets;
#if !LINUX
using Workspace1C.ViewModels;
#endif
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #163 «Хранение паролей от баз» (план 1.0.1.120, T3 — UI и сценарии):
/// <list type="bullet">
/// <item>смена пароля учётной записи перешифровывает все хранилища;</item>
/// <item>снятие пароля выключает шифрование и возвращает открытый вид;</item>
/// <item>без ключа в сеансе смена пароля не выполняется и данные не теряются;</item>
/// <item>галочка «Шифровать этим все пароли» доступна только при пароле (WPF-вьюмодель);</item>
/// <item>разметка/код WPF и Avalonia содержат галочку и запрос пароля;</item>
/// <item>ключи локализации есть в ru/en.</item>
/// </list>
/// </summary>
[Collection("PasswordEncryption")]
public sealed class Issue163PasswordEncryptionUiTests : IDisposable
{
    private readonly string _root;

    public Issue163PasswordEncryptionUiTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w1c-issue163-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        PasswordSession.Clear();
        FeatureDataStore.SecretJsonProtectorOverride = null;
        FeatureDataStore.DirectoryOverride = null;
    }

    public void Dispose()
    {
        PasswordSession.Clear();
        FeatureDataStore.SecretJsonProtectorOverride = null;
        FeatureDataStore.DirectoryOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ------------------------------------------------------------------ смена пароля

    [Fact]
    public void ChangePassword_ReEncryptsAllStores()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();
        var profile = service.CurrentProfile!;
        service.SetCurrentProfile(profile.Id);

        service.SetPassword(profile.Id, "старый-пароль");
        Assert.True(service.TryUnlockPasswordSession(profile.Id, "старый-пароль"));

        var protector = new SecretJsonProtector(
            new PasswordProtector(), () => service.IsPasswordEncryptionEnabled);
        var repository = new InfobaseRepository(service, secretProtector: protector);
        repository.Save(new List<Infobase>
        {
            new()
            {
                Id = "id",
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "секрет-базы" }
            }
        });

        service.SetPasswordEncryptionEnabled(true);
        var path = Path.Combine(service.CurrentProfileDataDirectory, "infobases.json");
        Assert.DoesNotContain("секрет-базы", File.ReadAllText(path), StringComparison.Ordinal);

        // Смена пароля: старый ключ в сеансе, все файлы перешифровываются новым.
        service.SetPassword(profile.Id, "новый-пароль");

        Assert.True(service.VerifyPassword(profile.Id, "новый-пароль"));
        Assert.False(service.VerifyPassword(profile.Id, "старый-пароль"));
        Assert.True(service.IsPasswordEncryptionEnabled);
        Assert.True(PasswordSession.HasKey);

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("секрет-базы", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);
        // Новый сеансовый ключ читает значения прозрачно.
        Assert.Equal("секрет-базы", repository.Load()[0].EnterpriseAuth!.Password);
    }

    [Fact]
    public void RemovePassword_DisablesEncryption_AndRestoresPlaintext()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();
        var profile = service.CurrentProfile!;
        service.SetCurrentProfile(profile.Id);

        service.SetPassword(profile.Id, "пароль-учётки");
        service.TryUnlockPasswordSession(profile.Id, "пароль-учётки");

        var protector = new SecretJsonProtector(
            new PasswordProtector(), () => service.IsPasswordEncryptionEnabled);
        var repository = new InfobaseRepository(service, secretProtector: protector);
        repository.Save(new List<Infobase>
        {
            new()
            {
                Id = "id",
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "открытый-секрет" }
            }
        });

        service.SetPasswordEncryptionEnabled(true);
        var path = Path.Combine(service.CurrentProfileDataDirectory, "infobases.json");
        Assert.DoesNotContain("открытый-секрет", File.ReadAllText(path), StringComparison.Ordinal);

        // Снятие пароля: расшифровка старым ключом, флаг выключен, открытый вид.
        service.SetPassword(profile.Id, null);

        Assert.False(profile.HasPassword);
        Assert.False(service.IsPasswordEncryptionEnabled);
        Assert.False(PasswordSession.HasKey);

        var raw = File.ReadAllText(path);
        Assert.Contains("открытый-секрет", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(PasswordProtector.Prefix, raw, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePassword_WithoutSessionKey_Throws_AndKeepsData()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();
        var profile = service.CurrentProfile!;
        service.SetCurrentProfile(profile.Id);

        service.SetPassword(profile.Id, "старый-пароль");
        service.TryUnlockPasswordSession(profile.Id, "старый-пароль");

        var protector = new SecretJsonProtector(
            new PasswordProtector(), () => service.IsPasswordEncryptionEnabled);
        var repository = new InfobaseRepository(service, secretProtector: protector);
        repository.Save(new List<Infobase>
        {
            new()
            {
                Id = "id",
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "секрет" }
            }
        });
        service.SetPasswordEncryptionEnabled(true);
        var path = Path.Combine(service.CurrentProfileDataDirectory, "infobases.json");

        // Ключа нет (перезапуск/фон): операция невозможна, данные остаются зашифрованными.
        PasswordSession.Clear();

        Assert.Throws<SecretProtectionException>(() => service.SetPassword(profile.Id, "новый-пароль"));

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("\"секрет\"", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);
        // Прежний пароль по-прежнему действителен — откат без потери данных.
        Assert.True(service.VerifyPassword(profile.Id, "старый-пароль"));
        Assert.True(service.IsPasswordEncryptionEnabled);
    }

    // ------------------------------------------------------------------ WPF-вьюмодель

#if !LINUX
    [Fact]
    public void ProfilesViewModel_EncryptCheckbox_RequiresPassword()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();
        var dialogs = new FakeDialogService();
        var vm = new ProfilesViewModel(service, dialogs);

        // Профиль без пароля: галочка недоступна, включить шифрование нельзя.
        Assert.False(vm.CanEncryptPasswords);
        vm.ProtectWithPassword = true;
        vm.EncryptPasswords = true;
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.False(service.IsPasswordEncryptionEnabled);
    }

    [Fact]
    public void ProfilesViewModel_EncryptCheckbox_EnablesEncryption_WithSessionKey()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();
        var profile = service.CurrentProfile!;
        service.SetCurrentProfile(profile.Id);
        service.SetPassword(profile.Id, "пароль-учётки");
        Assert.True(service.TryUnlockPasswordSession(profile.Id, "пароль-учётки"));

        var vm = new ProfilesViewModel(service, new FakeDialogService());
        vm.RefreshList(profile.Id);

        Assert.True(vm.CanEncryptPasswords);
        vm.EncryptPasswords = true;
        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasError);
        Assert.True(service.IsPasswordEncryptionEnabled);
    }
#endif

    // ------------------------------------------------------------------ разметка/код

    [Fact]
    public void WpfPanel_HasEncryptionCheckbox_AndPasswordPrompt()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Workspace1C", "Views", "ProfilesPanel.xaml"));
        Assert.Contains("Profiles.EncryptPasswords", xaml, StringComparison.Ordinal);
        Assert.Contains("CanEncryptPasswords", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowEncryptionHint", xaml, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(root, "Workspace1C", "Views", "ProfilesPanel.xaml.cs"));
        Assert.Contains("SecretUnlockWindow", code, StringComparison.Ordinal);
        Assert.Contains("PasswordPrompt", code, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(root, "Workspace1C", "Views", "SecretUnlockWindow.cs"));
        Assert.Contains("TryUnlockPasswordSession", window, StringComparison.Ordinal);
        Assert.Contains("Secrets.WrongPassword", window, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaProfilesWindow_HasEncryptionCheckbox_AndPasswordPrompt()
    {
        var root = FindRepoRoot();
        var code = File.ReadAllText(Path.Combine(root, "Workspace1C", "Views", "ProfilesWindow.Avalonia.cs"));
        Assert.Contains("Profiles.EncryptPasswords", code, StringComparison.Ordinal);
        Assert.Contains("_encryptCheck", code, StringComparison.Ordinal);
        Assert.Contains("Secrets.EncryptionEnabledHint", code, StringComparison.Ordinal);
        Assert.Contains("EnsureSessionKey", code, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(root, "Workspace1C", "Views", "SecretUnlockWindow.Avalonia.cs"));
        Assert.Contains("TryUnlockPasswordSession", window, StringComparison.Ordinal);
        Assert.Contains("Secrets.WrongPassword", window, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ локализация

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Localization_HasEncryptionUiKeys(string language)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var strings = document.RootElement.GetProperty("strings");

        foreach (var key in new[]
                 {
                     "Profiles.EncryptPasswords",
                     "Secrets.EncryptionEnabledHint",
                     "Secrets.EncryptionEnableConfirm",
                     "Secrets.EncryptionDisableConfirm",
                     "Secrets.UnlockTitle",
                     "Secrets.UnlockPrompt",
                     "Secrets.Unlock",
                     "Secrets.WrongPassword",
                     "Secrets.NotChangedNoKey"
                 })
        {
            Assert.True(strings.TryGetProperty(key, out var value), $"Нет ключа {key} в {language}.json");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"Пустое значение {key} в {language}.json");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public bool Confirm(string message, string title = "") => true;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}
