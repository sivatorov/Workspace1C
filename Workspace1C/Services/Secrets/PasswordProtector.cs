using System.Security.Cryptography;
using System.Text;

namespace Workspace1C.Services.Secrets;

/// <summary>Статус операции расшифровки секрета (issue #163).</summary>
public enum ProtectStatus
{
    /// <summary>Значение получено (в том числе если оно хранилось открытым — обратная совместимость).</summary>
    Ok,

    /// <summary>Пароль учётной записи недоступен в текущем сеансе — расшифровать нечем.</summary>
    NoKey,

    /// <summary>Пароль не подходит: проверка подлинности AES-GCM не прошла.</summary>
    WrongKey,

    /// <summary>Конверт повреждён (не Base64, слишком короткий и т.п.).</summary>
    Corrupted
}

/// <summary>Результат расшифровки: статус и значение (для <see cref="ProtectStatus.Ok"/>).</summary>
public readonly record struct ProtectResult(ProtectStatus Status, string? Value)
{
    /// <summary>true, если значение успешно получено.</summary>
    public bool Succeeded => Status == ProtectStatus.Ok;

    public static ProtectResult Ok(string? value) => new(ProtectStatus.Ok, value);

    public static ProtectResult Fail(ProtectStatus status) => new(status, null);
}

/// <summary>
/// Шифрование секретов (паролей) паролем учётной записи (issue #163).
///
/// Формат значения: <c>enc:v1:<base64(salt|nonce|tag|ciphertext)></c>.
/// <list type="bullet">
/// <item>ключ — <c>PBKDF2-SHA256(password, salt, 200_000, 32)</c>;</item>
/// <item>AES-256-GCM, nonce 12 байт, tag 16 байт;</item>
/// <item>соль (16 байт) и nonce хранятся в самом значении, поэтому значения
/// переносимы вместе с профилем и не требуют отдельного файла ключей;</item>
/// <item>открытые (не начинающиеся с <c>enc:v1:</c>) значения читаются как есть —
/// обратная совместимость со старыми файлами.</item>
/// </list>
/// Пароль учётной записи нигде не пишется на диск и не журналируется: он живёт
/// только в памяти процесса (<see cref="PasswordSession"/>).
/// </summary>
public interface IPasswordProtector
{
    /// <summary>true, если в текущем сеансе доступен пароль учётной записи.</summary>
    bool HasKey { get; }

    /// <summary>true, если значение уже зашифровано (начинается с <c>enc:v1:</c>).</summary>
    bool IsProtected(string? value);

    /// <summary>
    /// Шифрует открытое значение. Пустое значение возвращается как есть.
    /// Если ключа нет — возвращает null (вызывающий код обязан сообщить понятную ошибку,
    /// а не записывать пароль открытым текстом).
    /// </summary>
    string? Protect(string? value);

    /// <summary>
    /// Расшифровывает значение. Открытое значение возвращается как есть
    /// (<see cref="ProtectStatus.Ok"/>) — обратная совместимость.
    /// </summary>
    ProtectResult Unprotect(string? value);
}

/// <inheritdoc />
public sealed class PasswordProtector : IPasswordProtector
{
    /// <summary>Префикс зашифрованного значения.</summary>
    public const string Prefix = "enc:v1:";

    /// <summary>Число итераций PBKDF2-SHA256 (по плану 1.0.1.120).</summary>
    public const int Iterations = 200_000;

    /// <summary>Размер соли (байт).</summary>
    public const int SaltSize = 16;

    /// <summary>Размер nonce AES-GCM (байт).</summary>
    public const int NonceSize = 12;

    /// <summary>Размер тега аутентификации AES-GCM (байт).</summary>
    public const int TagSize = 16;

    /// <summary>Размер ключа AES-256 (байт).</summary>
    public const int KeySize = 32;

    private readonly Func<string?> _passwordProvider;

    /// <param name="passwordProvider">
    /// Источник пароля учётной записи. null — <see cref="PasswordSession"/>
    /// (пароль текущего сеанса). В тестах можно передать фиксированный пароль.
    /// </param>
    public PasswordProtector(Func<string?>? passwordProvider = null)
    {
        _passwordProvider = passwordProvider ?? GetSessionPassword;
    }

    private static string? GetSessionPassword() =>
        PasswordSession.TryGetPassword(out var password) ? password : null;

    /// <inheritdoc />
    public bool HasKey => !string.IsNullOrEmpty(_passwordProvider());

    /// <inheritdoc />
    public bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <inheritdoc />
    public string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (IsProtected(value))
            return value;

        var password = _passwordProvider();
        if (string.IsNullOrEmpty(password))
            return null;

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, payload, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, SaltSize + NonceSize + TagSize, ciphertext.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    /// <inheritdoc />
    public ProtectResult Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return ProtectResult.Ok(value);

        // Обратная совместимость: старые открытые значения читаются как есть.
        if (!IsProtected(value))
            return ProtectResult.Ok(value);

        var password = _passwordProvider();
        if (string.IsNullOrEmpty(password))
            return ProtectResult.Fail(ProtectStatus.NoKey);

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(value!.Substring(Prefix.Length));
        }
        catch (FormatException)
        {
            return ProtectResult.Fail(ProtectStatus.Corrupted);
        }

        if (payload.Length < SaltSize + NonceSize + TagSize)
            return ProtectResult.Fail(ProtectStatus.Corrupted);

        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = payload.Length - SaltSize - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        Buffer.BlockCopy(payload, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(payload, SaltSize, nonce, 0, NonceSize);
        Buffer.BlockCopy(payload, SaltSize + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(payload, SaltSize + NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        try
        {
            var key = DeriveKey(password, salt);
            var plaintext = new byte[ciphertextLength];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return ProtectResult.Ok(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException)
        {
            // Неверный пароль либо повреждённый шифротекст — данные не теряем.
            return ProtectResult.Fail(ProtectStatus.WrongKey);
        }
        catch
        {
            return ProtectResult.Fail(ProtectStatus.Corrupted);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
}
