using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services.Secrets;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IProfileService"/>: управление реестром профилей (<c>profiles.json</c>),
/// пер-профильными каталогами данных, хэширование/проверка паролей (PBKDF2) и миграция
/// существующих данных в профиль по умолчанию при первом запуске.
///
/// Структура каталога данных приложения после включения профилей:
/// <code>
///   ~/.config/Workspace1C (Linux) / %APPDATA%\Workspace1C (Windows)
///   ├── profiles.json                  — реестр учётных записей
///   └── profiles\
///       └── <Id>\
///           ├── settings.json
///           ├── infobases.json
///           └── groups.json
/// </code>
/// </summary>
public class ProfileService : IProfileService
{
    private const string RegistryFileName = "profiles.json";
    private const int RegistrySchemaVersion = 1;
    private const string DefaultProfileName = "Пользователь";

    // Имена «легаси»-файлов, которые до введения профилей лежали в корне каталога данных.
    private static readonly string[] LegacyDataFileNames =
    {
        "settings.json",
        "infobases.json",
        "groups.json"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Имена профилей могут содержать кириллицу — записываем их читаемыми UTF-8,
        // а не \uXXXX-последовательностями (issue #170). Влияет только на запись.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<UserProfile> _profiles = new();
    private readonly IAppLogger _logger;
    private readonly string? _dataDirectoryOverride;
    private string? _lastProfileId;
    private UserProfile? _currentProfile;
    private bool _initialized;

    // Session-флаг разблокировки приватных баз (0.3.9.85): взводится верным паролем
    // активного профиля (или входом с паролем через LoginWindow), сбрасывается при
    // смене профиля. При завершении приложения значение теряется само.
    private bool _isPrivateBasesUnlocked;

    /// <summary>
    /// Оповещает подписчиков об изменении реестра учётных записей (создание/переименование/
    /// удаление профиля, смена пароля или активной записи). Используется для актуализации
    /// видимости кнопки «Смена пользователя» без перезапуска (issue #200).
    /// </summary>
    public event EventHandler? ProfilesChanged;

    /// <summary>
    /// Корень каталога данных: явный (юнит-тесты) либо платформенный
    /// (<see cref="PlatformPaths.AppDataDirectory"/>). Вынесен в свойство, чтобы тесты
    /// не трогали реальный <c>%APPDATA%</c>/<c>~/.config</c>.
    /// </summary>
    private string DataRoot =>
        !string.IsNullOrWhiteSpace(_dataDirectoryOverride)
            ? _dataDirectoryOverride!
            : PlatformPaths.AppDataDirectory;

    private string RegistryPath => Path.Combine(DataRoot, RegistryFileName);

    /// <param name="logger">Журнал приложения.</param>
    /// <param name="dataDirectoryOverride">
    /// Необязательный явный корень данных (юнит-тесты). null — платформенный каталог.
    /// </param>
    public ProfileService(IAppLogger logger, string? dataDirectoryOverride = null)
    {
        _logger = logger;
        _dataDirectoryOverride = dataDirectoryOverride;
    }

    /// <summary>Каталог данных конкретного профиля.</summary>
    public string GetProfileDataDirectory(string id) =>
        Path.Combine(DataRoot, "profiles", id);

    public IReadOnlyList<UserProfile> Profiles
    {
        get
        {
            EnsureInitialized();
            return _profiles;
        }
    }

    public UserProfile? CurrentProfile
    {
        get
        {
            EnsureInitialized();
            return _currentProfile;
        }
    }

    public string CurrentProfileDataDirectory =>
        _currentProfile != null
            ? GetProfileDataDirectory(_currentProfile.Id)
            : DataRoot;

    /// <summary>
    /// Загружает реестр профилей. Если реестра ещё нет, а в корне каталога данных есть
    /// легаси-файлы (settings/infobases/groups.json) — выполняет миграцию в профиль по умолчанию,
    /// чтобы существующие данные не потерялись. Повторные вызовы безопасны.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        if (File.Exists(RegistryPath))
        {
            LoadRegistry();
        }
        else
        {
            MigrateLegacyData();
            SaveRegistry();
        }

        // Активным делаем профиль, использованный последним; иначе — единственный профиль.
        if (_lastProfileId != null)
        {
            _currentProfile = _profiles.FirstOrDefault(p => p.Id == _lastProfileId);
        }
        _currentProfile ??= _profiles.FirstOrDefault();

        _initialized = true;
    }

    public UserProfile CreateProfile(string name, string? password = null)
    {
        EnsureInitialized();

        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Имя профиля не может быть пустым.", nameof(name));
        if (_profiles.Any(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Профиль с именем «{trimmed}» уже существует.", nameof(name));

        var profile = new UserProfile { Name = trimmed };
        if (!string.IsNullOrEmpty(password))
            profile.PasswordHash = HashPassword(password);

        _profiles.Add(profile);
        SaveRegistry();
        NotifyProfilesChanged();
        return profile;
    }

    public void RenameProfile(string id, string newName)
    {
        EnsureInitialized();

        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Имя профиля не может быть пустым.", nameof(newName));

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        if (_profiles.Any(p => p.Id != id && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Профиль с именем «{trimmed}» уже существует.", nameof(newName));

        profile.Name = trimmed;
        SaveRegistry();
        NotifyProfilesChanged();
    }

    public bool DeleteProfile(string id)
    {
        EnsureInitialized();

        // Нельзя удалить последний профиль — иначе приложение останется без данных.
        if (_profiles.Count <= 1)
            return false;

        var profile = FindProfile(id);
        if (profile == null)
            return false;

        var index = _profiles.IndexOf(profile);
        var wasCurrent = _currentProfile?.Id == id;
        var wasLast = _lastProfileId == id;

        _profiles.RemoveAt(index);
        if (wasCurrent)
            _currentProfile = _profiles.FirstOrDefault();
        if (wasLast)
            _lastProfileId = null;

        // Сначала сохраняем реестр — только при успешной записи удаляем каталог данных.
        // Так запись, исключённая из списка, не «оживёт» на следующем запуске из старого
        // файла profiles.json, когда её каталог уже удалён (issue #209).
        if (!SaveRegistry())
        {
            // Откатываем удаление, чтобы состояние на диске и в памяти не разошлось молча:
            // возвращаем профиль в список и восстанавливаем ссылки на текущий/последний.
            _profiles.Insert(index, profile);
            if (wasCurrent)
                _currentProfile = profile;
            if (wasLast)
                _lastProfileId = id;

            var message = string.Format(
                LocalizationManager.T("Profiles.DeleteFailedSave"), RegistryPath);
            _logger.Error(message);
            throw new InvalidOperationException(message);
        }

        // Реестр сохранён успешно — теперь удаляем каталог данных профиля.
        // Сбой здесь не критичен: запись уже исчезла из реестра, а каталог будет
        // перезаписан при пересоздании профиля с тем же Id.
        try
        {
            var dir = GetProfileDataDirectory(profile.Id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Оставляем каталог — он будет перезаписан при пересоздании профиля с тем же Id.
        }

        NotifyProfilesChanged();
        return true;
    }

    public void SetPassword(string id, string? password)
    {
        EnsureInitialized();

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        var newHash = string.IsNullOrEmpty(password) ? string.Empty : HashPassword(password);

        // Issue #163 (T3): при включённом шифровании смена/снятие пароля учётной записи
        // обязана перешифровать все сохранённые пароли. Старый ключ для расшифровки
        // обязан быть в сеансе — иначе данные не пострадают, но операцию выполнить нельзя.
        if (profile.EncryptPasswordsWithProfilePassword)
        {
            ChangePasswordWithReEncryption(profile, newHash, password);
            return;
        }

        profile.PasswordHash = newHash;
        if (string.IsNullOrEmpty(password))
        {
            // Пароля нет — шифровать паролем учётной записи нечем.
            profile.EncryptPasswordsWithProfilePassword = false;
        }

        // Старый сеансовый ключ выведен из прежнего пароля и больше не действителен.
        PasswordSession.Clear();
        SaveRegistry();
        NotifyProfilesChanged();
    }

    /// <summary>
    /// Смена пароля учётной записи при включённом шифровании: расшифровать хранилища
    /// старым ключом, сменить хэш и зашифровать их новым ключом. При любом сбое
    /// возвращает прежний пароль/флаг и прежнее шифрование, данные не теряются.
    /// </summary>
    private void ChangePasswordWithReEncryption(UserProfile profile, string newHash, string? newPassword)
    {
        // Расшифровать чужим/неизвестным ключом нельзя: требуем ключ именно этой записи.
        if (!PasswordSession.HasKey
            || !string.Equals(PasswordSession.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase)
            || !PasswordSession.TryGetPassword(out var oldPassword))
        {
            throw new SecretProtectionException(
                ProtectStatus.NoKey,
                LocalizationManager.T("Secrets.KeyUnavailable"));
        }

        var oldHash = profile.PasswordHash;
        var oldFlag = profile.EncryptPasswordsWithProfilePassword;

        // Сеансовый ключ обязан соответствовать прежнему паролю, иначе расшифровка
        // оставит шифротекст и повторное шифрование новым ключом потеряет данные.
        if (!VerifyPasswordHash(oldPassword, oldHash))
        {
            throw new SecretProtectionException(
                ProtectStatus.WrongKey,
                LocalizationManager.T("Secrets.WrongKey"));
        }

        var directory = GetProfileDataDirectory(profile.Id);

        // 1) Вернуть файлы в открытый вид старым ключом (пока хэш ещё прежний).
        SecretStoreMigrator.DecryptAll(directory, CreateMigrationProtector());

        // 2) Зафиксировать новый хэш и (при снятии пароля) выключить флаг.
        profile.PasswordHash = newHash;
        if (string.IsNullOrEmpty(newPassword))
            profile.EncryptPasswordsWithProfilePassword = false;

        if (!SaveRegistry())
        {
            RestorePasswordState(profile, oldHash, oldFlag, oldPassword, directory);
            throw new InvalidOperationException(LocalizationManager.T("Secrets.FlagSaveFailed"));
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            // Пароль снят: файлы уже в открытом виде, ключ больше не нужен.
            PasswordSession.Clear();
            NotifyProfilesChanged();
            return;
        }

        // 3) Новый ключ и повторное шифрование всех хранилищ.
        PasswordSession.SetFromPassword(profile.Id, newPassword);
        try
        {
            SecretStoreMigrator.EncryptAll(directory, CreateMigrationProtector());
        }
        catch
        {
            // Часть файлов могла успеть зашифроваться новым ключом — возвращаем их
            // в открытый вид текущим (новым) ключом, затем откатываемся на прежний.
            try { SecretStoreMigrator.DecryptAll(directory, CreateMigrationProtector()); } catch { }
            RestorePasswordState(profile, oldHash, oldFlag, oldPassword, directory);
            throw;
        }

        NotifyProfilesChanged();
    }

    /// <summary>Откат смены пароля: прежний хэш/флаг/сеансовый ключ и шифрование файлов.</summary>
    private void RestorePasswordState(
        UserProfile profile, string oldHash, bool oldFlag, string oldPassword, string directory)
    {
        profile.PasswordHash = oldHash;
        profile.EncryptPasswordsWithProfilePassword = oldFlag;
        PasswordSession.SetFromPassword(profile.Id, oldPassword);
        try
        {
            // Файлы после неудачной попытки могли остаться расшифрованными — шифруем обратно.
            SecretStoreMigrator.EncryptAll(directory, CreateMigrationProtector());
        }
        catch
        {
            // Повторный сбой не должен скрывать исходную ошибку; данные остаются на диске.
        }
        SaveRegistry();
    }

    /// <summary>Протектор для миграции файлов: шифрование считается включённым.</summary>
    private static SecretJsonProtector CreateMigrationProtector() =>
        new(new PasswordProtector(), () => true);

    public bool VerifyPassword(string id, string password)
    {
        EnsureInitialized();

        var profile = FindProfile(id);
        if (profile == null || !profile.HasPassword)
            return true;

        if (string.IsNullOrEmpty(password))
            return false;

        var valid = VerifyPasswordHash(password, profile.PasswordHash);
        // Вход с паролем при включённом шифровании делает пароль сеансовым ключом
        // (issue #163). Пароль остаётся только в памяти процесса и очищается при
        // блокировке/выходе/смене записи.
        if (valid && profile.EncryptPasswordsWithProfilePassword)
            PasswordSession.SetFromPassword(profile.Id, password);
        return valid;
    }

    public void SetCurrentProfile(string id)
    {
        EnsureInitialized();

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        var previousId = _currentProfile?.Id;
        _currentProfile = profile;
        _lastProfileId = profile.Id;
        // Приватные базы нового профиля разблокируются только его собственным
        // паролем: флаг чужой сессии не переносится (0.3.9.85).
        ResetPrivateBasesUnlock();
        // Сеансовый ключ шифрования принадлежит конкретной записи. При входе пароль
        // уже проверен окном входа (PasswordSession.ProfileId == profile.Id), поэтому
        // такой ключ сохраняем; при переключении на другую запись — очищаем.
        if (!string.Equals(previousId, profile.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(PasswordSession.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            PasswordSession.Clear();
        }
        SaveRegistry();
        NotifyProfilesChanged();
    }

    // ------------------------------------------------------- приватные базы (0.3.9.85)

    /// <inheritdoc />
    public bool IsPrivateBasesUnlocked => _isPrivateBasesUnlocked;

    /// <inheritdoc />
    public bool CanShowPrivateBases
    {
        get
        {
            EnsureInitialized();
            // Профиль без пароля защищать нечем: приватность для него не действует,
            // базы показываются всегда. Для профиля с паролем нужна разблокировка.
            return _currentProfile?.HasPassword != true || _isPrivateBasesUnlocked;
        }
    }

    /// <inheritdoc />
    public bool UnlockPrivateBases(string password)
    {
        EnsureInitialized();

        if (_currentProfile?.HasPassword != true)
        {
            _isPrivateBasesUnlocked = true;
            return true;
        }

        if (VerifyPassword(_currentProfile.Id, password ?? string.Empty))
        {
            _isPrivateBasesUnlocked = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void MarkPrivateBasesUnlocked() => _isPrivateBasesUnlocked = true;

    /// <inheritdoc />
    public void ResetPrivateBasesUnlock() => _isPrivateBasesUnlocked = false;

    // ------------------------------------------------------- шифрование паролей (issue #163)

    /// <inheritdoc />
    public bool IsPasswordEncryptionEnabled
    {
        get
        {
            EnsureInitialized();
            return _currentProfile?.EncryptPasswordsWithProfilePassword == true;
        }
    }

    /// <inheritdoc />
    public void SetPasswordEncryptionEnabled(bool enabled)
    {
        EnsureInitialized();

        var profile = _currentProfile
            ?? throw new InvalidOperationException(LocalizationManager.T("Profiles.EmptySelection"));

        if (enabled && !profile.HasPassword)
        {
            throw new InvalidOperationException(
                LocalizationManager.T("Secrets.EncryptionRequiresPassword"));
        }

        if (profile.EncryptPasswordsWithProfilePassword == enabled)
            return;

        // Ключ (пароль учётной записи) обязан быть в сеансе: иначе нельзя ни зашифровать
        // существующие открытые пароли, ни расшифровать их при выключении.
        if (!PasswordSession.HasKey)
        {
            throw new SecretProtectionException(
                ProtectStatus.NoKey,
                LocalizationManager.T("Secrets.KeyUnavailable"));
        }

        var previous = profile.EncryptPasswordsWithProfilePassword;
        profile.EncryptPasswordsWithProfilePassword = enabled;
        if (!SaveRegistry())
        {
            profile.EncryptPasswordsWithProfilePassword = previous;
            throw new InvalidOperationException(LocalizationManager.T("Secrets.FlagSaveFailed"));
        }

        try
        {
            // Миграция существующих файлов профиля: открытые значения шифруются при
            // включении и возвращаются в открытый вид при выключении. Флаг уже сохранён,
            // поэтому протектор считает шифрование включённым в обе стороны.
            var protector = new SecretJsonProtector(new PasswordProtector(), () => true);
            var directory = CurrentProfileDataDirectory;
            if (enabled)
                SecretStoreMigrator.EncryptAll(directory, protector);
            else
                SecretStoreMigrator.DecryptAll(directory, protector);
        }
        catch
        {
            // Миграция не удалась — возвращаем прежнее состояние флага, данные не теряем
            // (мигратор перезаписывает файлы атомарно и только при реальном изменении).
            profile.EncryptPasswordsWithProfilePassword = previous;
            SaveRegistry();
            throw;
        }

        NotifyProfilesChanged();
    }

    /// <inheritdoc />
    public bool TryUnlockPasswordSession(string id, string password)
    {
        EnsureInitialized();

        var profile = FindProfile(id);
        if (profile == null)
            return false;

        if (!profile.HasPassword)
            return true; // защищать нечем — сеансовый ключ не нужен

        if (string.IsNullOrEmpty(password) || !VerifyPasswordHash(password, profile.PasswordHash))
            return false;

        PasswordSession.SetFromPassword(profile.Id, password);
        return true;
    }

    /// <inheritdoc />
    public void ClearPasswordSession()
    {
        PasswordSession.Clear();
        _isPrivateBasesUnlocked = false;
    }

    // ---------------------------------------------------------------- internals

    private UserProfile? FindProfile(string id) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Оповещает подписчиков об изменении реестра учётных записей.</summary>
    private void NotifyProfilesChanged() => ProfilesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Миграция легаси-данных: если реестра профилей нет, а в корне каталога данных есть
    /// файлы настроек/баз/групп — создаёт профиль по умолчанию и переносит эти файлы в его
    /// подкаталог. Легаси-файлы в корне при этом удаляются, чтобы не дублироваться.
    /// </summary>
    private void MigrateLegacyData()
    {
        var legacyDir = DataRoot;
        var hadLegacyData = LegacyDataFileNames.Any(f => File.Exists(Path.Combine(legacyDir, f)));

        var defaultProfile = new UserProfile { Name = DefaultProfileName };
        _profiles.Add(defaultProfile);
        _lastProfileId = defaultProfile.Id;

        if (!hadLegacyData)
            return;

        var profileDir = GetProfileDataDirectory(defaultProfile.Id);
        Directory.CreateDirectory(profileDir);

        foreach (var name in LegacyDataFileNames)
        {
            var source = Path.Combine(legacyDir, name);
            if (!File.Exists(source))
                continue;
            try
            {
                var target = Path.Combine(profileDir, name);
                if (File.Exists(target))
                    File.Delete(target);
                File.Move(source, target);
            }
            catch
            {
                // Сбой переноса не должен блокировать запуск: файл останется в корне.
            }
        }
    }

    private void LoadRegistry()
    {
        try
        {
            var json = File.ReadAllText(RegistryPath);
            var registry = JsonSerializer.Deserialize<ProfileRegistry>(json, JsonOptions);
            if (registry?.Profiles is { Count: > 0 })
            {
                _profiles.Clear();
                _profiles.AddRange(registry.Profiles);
                _lastProfileId = registry.LastProfileId;
            }
        }
        catch
        {
            // Повреждённый реестр: пересоздаём чистый список. Каталоги данных профилей не трогаем.
            _profiles.Clear();
            _lastProfileId = null;
        }
    }

    /// <summary>
    /// Сохраняет реестр профилей в <c>profiles.json</c>.
    /// </summary>
    /// <returns>True, если запись выполнена успешно; иначе false.</returns>
    private bool SaveRegistry()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            var registry = new ProfileRegistry
            {
                SchemaVersion = RegistrySchemaVersion,
                LastProfileId = _lastProfileId,
                Profiles = _profiles.ToList()
            };
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(registry, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            // Ошибку больше не подавляем молча: фиксируем её в журнале. Возвращаем false,
            // чтобы вызывающий код (например, удаление профиля) мог откатить изменения
            // и сообщить пользователю, — иначе расхождение между реестром и диском останется
            // незамеченным до следующего запуска.
            _logger.Error($"Не удалось сохранить реестр профилей ({RegistryPath}): {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Хэширует пароль алгоритмом PBKDF2-SHA256 с случайной солью. Формат результата:
    /// «<c>итерации.сольBase64.хэшBase64</c>». Соль и число итераций хранятся вместе с хэшем,
    /// поэтому для проверки достаточно самого хэша.
    /// </summary>
    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPasswordHash(string password, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 3)
                return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Модель файла-реестра <c>profiles.json</c>.</summary>
    private sealed class ProfileRegistry
    {
        public int SchemaVersion { get; set; }
        public string? LastProfileId { get; set; }
        public List<UserProfile> Profiles { get; set; } = new();
    }
}