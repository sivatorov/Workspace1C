using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Secrets;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #163 «Хранение паролей от баз» (план 1.0.1.120, T2 — криптоядро и хранилища):
/// <list type="bullet">
/// <item>конверт <c>enc:v1:</c>, AES-256-GCM + PBKDF2, round-trip и проверка неверного пароля;</item>
/// <item>шифрование только белых полей JSON, обратная совместимость с открытыми файлами;</item>
/// <item>прозрачное чтение/запись <c>infobases.json</c>, <c>its_accounts.json</c>, FeatureDataStore;</item>
/// <item>отсутствие открытого пароля в файле после сохранения;</item>
/// <item>понятная ошибка при отсутствии ключа (без падения и без потери данных);</item>
/// <item>сеансовый пароль и его очистка; флаг профиля и миграция.</item>
/// </list>
/// </summary>
[Collection("PasswordEncryption")]
public sealed class Issue163PasswordEncryptionTests : IDisposable
{
    private readonly string _root;

    public Issue163PasswordEncryptionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "w1c-issue163-" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------ криптоядро

    [Fact]
    public void Protector_RoundTrip_And_Prefix()
    {
        var protector = new PasswordProtector(() => "p@ssw0rd");

        var first = protector.Protect("секрет");
        var second = protector.Protect("секрет");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.StartsWith(PasswordProtector.Prefix, first, StringComparison.Ordinal);
        // Соль и nonce случайны: одинаковые значения дают разные конверты.
        Assert.NotEqual(first, second);

        var result = protector.Unprotect(first);
        Assert.Equal(ProtectStatus.Ok, result.Status);
        Assert.Equal("секрет", result.Value);
    }

    [Fact]
    public void Protector_WrongPassword_ReturnsWrongKey()
    {
        var encrypted = new PasswordProtector(() => "правильный").Protect("секрет");

        var result = new PasswordProtector(() => "неправильный").Unprotect(encrypted);

        Assert.Equal(ProtectStatus.WrongKey, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Protector_NoKey_ReturnsNoKey_AndDoesNotEncrypt()
    {
        var encrypted = new PasswordProtector(() => "пароль").Protect("секрет");
        var withoutKey = new PasswordProtector(() => null);

        Assert.False(withoutKey.HasKey);
        Assert.Null(withoutKey.Protect("секрет"));
        Assert.Equal(ProtectStatus.NoKey, withoutKey.Unprotect(encrypted).Status);
    }

    [Fact]
    public void Protector_CorruptedEnvelope_ReturnsCorrupted()
    {
        var protector = new PasswordProtector(() => "пароль");

        Assert.Equal(ProtectStatus.Corrupted, protector.Unprotect("enc:v1:!!!").Status);
        Assert.Equal(ProtectStatus.Corrupted, protector.Unprotect("enc:v1:AAAA").Status);
    }

    [Fact]
    public void Protector_Plaintext_IsReadAsIs()
    {
        // Обратная совместимость: старые открытые значения не ломают чтение.
        var result = new PasswordProtector(() => "пароль").Unprotect("открытый-пароль");

        Assert.Equal(ProtectStatus.Ok, result.Status);
        Assert.Equal("открытый-пароль", result.Value);
    }

    [Fact]
    public void SecretJsonProtector_EncryptsOnlyWhitelistedFields()
    {
        const string json = """
        {
          "Password": "p1",
          "PasswordHash": "hash",
          "SmtpPassword": "smtp",
          "DbPassword": "db",
          "Name": "Имя",
          "Nested": { "UpdatesPassword": "upd" }
        }
        """;

        var protector = new SecretJsonProtector(new PasswordProtector(() => "пароль"));
        var encrypted = protector.Protect(json);

        using (var document = JsonDocument.Parse(encrypted))
        {
            var root = document.RootElement;
            Assert.StartsWith(PasswordProtector.Prefix, root.GetProperty("Password").GetString(), StringComparison.Ordinal);
            Assert.StartsWith(PasswordProtector.Prefix, root.GetProperty("SmtpPassword").GetString(), StringComparison.Ordinal);
            Assert.StartsWith(PasswordProtector.Prefix, root.GetProperty("DbPassword").GetString(), StringComparison.Ordinal);
            Assert.StartsWith(PasswordProtector.Prefix, root.GetProperty("Nested").GetProperty("UpdatesPassword").GetString(), StringComparison.Ordinal);
            // Хэш и обычные поля не шифруются — иначе станет невозможен вход.
            Assert.Equal("hash", root.GetProperty("PasswordHash").GetString());
            Assert.Equal("Имя", root.GetProperty("Name").GetString());
        }

        var decrypted = protector.Unprotect(encrypted);
        using var restored = JsonDocument.Parse(decrypted);
        Assert.Equal("p1", restored.RootElement.GetProperty("Password").GetString());
        Assert.Equal("smtp", restored.RootElement.GetProperty("SmtpPassword").GetString());
        Assert.Equal("db", restored.RootElement.GetProperty("DbPassword").GetString());
        Assert.Equal("upd", restored.RootElement.GetProperty("Nested").GetProperty("UpdatesPassword").GetString());
    }

    // ------------------------------------------------------------------ хранилища

    [Fact]
    public void InfobaseRepository_RoundTrip_Encrypted_NoPlaintextInFile()
    {
        var protector = EnabledProtector("пароль-учётки");
        var repository = new InfobaseRepository(directory: _root, secretProtector: protector);

        repository.Save(new List<Infobase>
        {
            new()
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "enterprise-secret" },
                Repository = new RepositorySettings { Password = "repository-secret" }
            }
        });

        var raw = File.ReadAllText(Path.Combine(_root, "infobases.json"));
        Assert.DoesNotContain("enterprise-secret", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("repository-secret", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);

        var loaded = repository.Load();
        Assert.Single(loaded);
        Assert.Equal("enterprise-secret", loaded[0].EnterpriseAuth!.Password);
        Assert.Equal("repository-secret", loaded[0].Repository.Password);
    }

    [Fact]
    public void ItsAccountsStore_RoundTrip_Encrypted_NoPlaintextInFile()
    {
        var protector = EnabledProtector("пароль-учётки");
        var store = new ItsAccountsStore(directoryOverride: _root, secretProtector: protector);

        store.Save(new List<ItsAccount>
        {
            new() { Name = "ИТС", Login = "user", Password = "its-secret", IsPrimary = true }
        });

        var raw = File.ReadAllText(Path.Combine(_root, ItsAccountsStore.FileName));
        Assert.DoesNotContain("its-secret", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("its-secret", loaded[0].Password);
    }

    [Fact]
    public void ServerListStore_RoundTrip_Encrypted_NoPlaintextInFile()
    {
        var protector = EnabledProtector("пароль-учётки");
        var store = new ServerListStore(directoryOverride: _root, secretProtector: protector);

        store.Save(new List<ManagementServer>
        {
            new()
            {
                Description = "Сервер",
                Address = "localhost",
                Port = ManagementServer.DefaultPort,
                User = "admin",
                Password = "server-secret"
            }
        });

        var raw = File.ReadAllText(Path.Combine(_root, ServerListStore.FileName));
        Assert.DoesNotContain("server-secret", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("server-secret", loaded[0].Password);
    }

    [Fact]
    public void FeatureDataStore_RoundTrip_Encrypted_NoPlaintextInFile()
    {
        FeatureDataStore.DirectoryOverride = _root;
        FeatureDataStore.SecretJsonProtectorOverride = EnabledProtector("пароль-учётки");

        FeatureDataStore.Save("feature.json", new FeatureSecretModel
        {
            Name = "СУБД",
            Password = "feature-secret",
            DbPassword = "feature-db-secret"
        });

        var raw = File.ReadAllText(Path.Combine(_root, "feature.json"));
        Assert.DoesNotContain("feature-secret", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("feature-db-secret", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);

        var loaded = FeatureDataStore.Load<FeatureSecretModel>("feature.json");
        Assert.Equal("feature-secret", loaded.Password);
        Assert.Equal("feature-db-secret", loaded.DbPassword);
        Assert.Equal("СУБД", loaded.Name);
    }

    [Fact]
    public void LegacyPlaintextFile_ReadsAsIs()
    {
        var json = JsonSerializer.Serialize(new List<Infobase>
        {
            new()
            {
                Id = "legacy-id",
                Name = "Старая база",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "legacy-secret" }
            }
        });
        File.WriteAllText(Path.Combine(_root, "infobases.json"), json);

        var repository = new InfobaseRepository(directory: _root, secretProtector: EnabledProtector("пароль"));
        var loaded = repository.Load();

        Assert.Single(loaded);
        Assert.Equal("legacy-secret", loaded[0].EnterpriseAuth!.Password);
    }

    [Fact]
    public void NoSessionKey_DoesNotCrash_LeavesCiphertext_AndSaveThrows()
    {
        // Сначала пишем зашифрованный файл с ключом.
        var withKey = EnabledProtector("пароль");
        var repositoryWithKey = new InfobaseRepository(directory: _root, secretProtector: withKey);
        repositoryWithKey.Save(new List<Infobase>
        {
            new()
            {
                Id = "id",
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "hidden" }
            }
        });
        // С ключом значение читается (round-trip), а в файле лежит конверт.
        Assert.Equal("hidden", repositoryWithKey.Load()[0].EnterpriseAuth!.Password);
        var raw = File.ReadAllText(Path.Combine(_root, "infobases.json"));
        Assert.DoesNotContain("hidden", raw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, raw, StringComparison.Ordinal);

        // Чтение без ключа: не падаем, значение остаётся зашифрованным («ключ недоступен»).
        var withoutKey = new SecretJsonProtector(new PasswordProtector(() => null));
        var repositoryWithoutKey = new InfobaseRepository(directory: _root, secretProtector: withoutKey);
        var loaded = repositoryWithoutKey.Load();
        var encryptedValue = loaded[0].EnterpriseAuth!.Password;
        Assert.StartsWith(PasswordProtector.Prefix, encryptedValue, StringComparison.Ordinal);
        Assert.Contains(encryptedValue, raw, StringComparison.Ordinal);

        // Сохранение без ключа при включённом шифровании: понятная ошибка, файл не портится.
        Assert.Throws<SecretProtectionException>(() => repositoryWithoutKey.Save(loaded));
        var rawAfter = File.ReadAllText(Path.Combine(_root, "infobases.json"));
        Assert.Contains(encryptedValue, rawAfter, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ сеансовый ключ

    [Fact]
    public void PasswordSession_SetAndClear()
    {
        PasswordSession.SetFromPassword("profile-1", "пароль");

        Assert.True(PasswordSession.HasKey);
        Assert.Equal("profile-1", PasswordSession.ProfileId);
        Assert.True(PasswordSession.TryGetPassword(out var password));
        Assert.Equal("пароль", password);

        PasswordSession.Clear();

        Assert.False(PasswordSession.HasKey);
        Assert.Null(PasswordSession.ProfileId);
        Assert.False(PasswordSession.TryGetPassword(out _));
    }

    // ------------------------------------------------------------------ флаг и миграция

    [Fact]
    public void ProfileService_EncryptionFlag_RequiresPassword_AndMigratesStores()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();

        var profile = service.CurrentProfile!;
        service.SetPassword(profile.Id, "пароль-учётки");
        service.SetCurrentProfile(profile.Id);

        // Пароль неверен — сеансового ключа нет.
        Assert.False(service.TryUnlockPasswordSession(profile.Id, "неверный"));
        Assert.False(PasswordSession.HasKey);

        // Верный пароль — ключ в памяти, шифрование пока выключено.
        Assert.True(service.TryUnlockPasswordSession(profile.Id, "пароль-учётки"));
        Assert.True(PasswordSession.HasKey);
        Assert.False(service.IsPasswordEncryptionEnabled);

        var protector = new SecretJsonProtector(
            new PasswordProtector(),
            () => service.IsPasswordEncryptionEnabled);
        var repository = new InfobaseRepository(service, secretProtector: protector);
        repository.Save(new List<Infobase>
        {
            new()
            {
                Id = "id",
                Name = "База",
                EnterpriseAuth = new InfobaseAuthSettings { Password = "plain-secret" }
            }
        });

        var path = Path.Combine(service.CurrentProfileDataDirectory, "infobases.json");
        Assert.Contains("plain-secret", File.ReadAllText(path), StringComparison.Ordinal);

        // Включение: миграция «открытый → зашифрованный», пароль читается.
        service.SetPasswordEncryptionEnabled(true);
        Assert.True(service.IsPasswordEncryptionEnabled);

        var encryptedRaw = File.ReadAllText(path);
        Assert.DoesNotContain("plain-secret", encryptedRaw, StringComparison.Ordinal);
        Assert.Contains(PasswordProtector.Prefix, encryptedRaw, StringComparison.Ordinal);
        Assert.Equal("plain-secret", repository.Load()[0].EnterpriseAuth!.Password);

        // Выключение: возврат к открытому виду.
        service.SetPasswordEncryptionEnabled(false);
        Assert.False(service.IsPasswordEncryptionEnabled);

        var plainRaw = File.ReadAllText(path);
        Assert.Contains("plain-secret", plainRaw, StringComparison.Ordinal);
        Assert.DoesNotContain(PasswordProtector.Prefix, plainRaw, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileService_EncryptionFlag_RequiresAccountPassword()
    {
        var service = new ProfileService(new NullLogger(), _root);
        service.EnsureInitialized();

        var profile = service.CurrentProfile!;
        service.SetCurrentProfile(profile.Id);
        service.TryUnlockPasswordSession(profile.Id, "любой");

        Assert.Throws<InvalidOperationException>(() => service.SetPasswordEncryptionEnabled(true));
        Assert.False(service.IsPasswordEncryptionEnabled);
    }

    // ------------------------------------------------------------------ локализация

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Localization_HasEncryptionKeys(string language)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", language + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var strings = document.RootElement.GetProperty("strings");

        foreach (var key in new[]
                 {
                     "Secrets.KeyUnavailable",
                     "Secrets.EncryptionRequiresPassword",
                     "Secrets.WrongKey",
                     "Secrets.Corrupted",
                     "Secrets.FlagSaveFailed",
                     "Secrets.SaveBlocked"
                 })
        {
            Assert.True(strings.TryGetProperty(key, out var value), $"Нет ключа {key} в {language}.json");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"Пустое значение {key} в {language}.json");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static SecretJsonProtector EnabledProtector(string password) =>
        new(new PasswordProtector(() => password));

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

    /// <summary>Модель для проверки FeatureDataStore (секретные и обычное поля).</summary>
    private sealed class FeatureSecretModel
    {
        public string Name { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DbPassword { get; set; } = string.Empty;
    }
}
