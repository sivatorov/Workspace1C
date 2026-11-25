using System.Text.Json;
using System.Text.Json.Nodes;
using Workspace1C.Localization;

namespace Workspace1C.Services.Secrets;

/// <summary>
/// Понятная ошибка шифрования секретов: ключ недоступен, пароль неверен или значение
/// повреждено. Наружу (в UI) выводится текст из локализации, а не техническая деталь.
/// </summary>
public sealed class SecretProtectionException : Exception
{
    public SecretProtectionException(ProtectStatus status, string message, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
    }

    /// <summary>Причина: нет ключа / неверный пароль / повреждённое значение.</summary>
    public ProtectStatus Status { get; }
}

/// <summary>
/// Прозрачное шифрование/расшифровка секретных полей в JSON-хранилищах профиля
/// (issue #163, план 1.0.1.120, T2).
///
/// Шифруются ТОЛЬКО значения свойств из белого списка (пароли баз, ИТС, СУБД, SMTP,
/// обновлений, сценариев). Хэши паролей, флаги и прочие поля не трогаются, поэтому
/// <c>profiles.json</c> и вход в приложение остаются работоспособными.
///
/// Файл остаётся валидным JSON: зашифрованное значение — обычная строка
/// <c>enc:v1:...</c> в том же поле (обратная совместимость и ручное редактирование).
/// </summary>
public sealed class SecretJsonProtector
{
    /// <summary>
    /// Белый список имён свойств, значения которых являются секретами.
    /// Имена сравниваются без учёта регистра.
    /// </summary>
    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "DbPassword",
        "SourceDbPassword",
        "InfobasePassword",
        "SmtpPassword",
        "UpdatesPassword"
    };

    /// <summary>
    /// Страховочный список исключений: даже если поле случайно попадёт в белый список,
    /// хэши/флаги шифровать нельзя — иначе станет невозможен вход.
    /// </summary>
    private static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "AppLockPasswordHash",
        "PasswordEncrypted",
        "HasPassword",
        "ProtectWithPassword",
        "PasswordIsSet",
        "CannotChangePassword"
    };

    private readonly IPasswordProtector _protector;
    private readonly Func<bool> _isEnabled;

    /// <param name="protector">Шифратор значений.</param>
    /// <param name="isEnabled">
    /// Признак включённого шифрования (флаг профиля). null — шифрование считается
    /// включённым (используется миграцией и тестами).
    /// </param>
    public SecretJsonProtector(IPasswordProtector protector, Func<bool>? isEnabled = null)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _isEnabled = isEnabled ?? (() => true);
    }

    /// <summary>true, если шифрование включено флагом активного профиля.</summary>
    public bool IsEnabled => _isEnabled();

    /// <summary>
    /// Шифрует секретные поля JSON. Если шифрование выключено — возвращает JSON без изменений.
    /// Если включено, а ключа в сеансе нет — бросает <see cref="SecretProtectionException"/>:
    /// записывать пароли открытым текстом нельзя, потеря данных недопустима.
    /// </summary>
    public string Protect(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !IsEnabled)
            return json;

        if (!_protector.HasKey)
        {
            throw new SecretProtectionException(
                ProtectStatus.NoKey,
                LocalizationManager.T("Secrets.KeyUnavailable"));
        }

        return Transform(json, encrypt: true) ?? json;
    }

    /// <summary>
    /// Расшифровывает секретные поля JSON. Никогда не бросает исключений и не теряет данные:
    /// если ключа нет, пароль неверен или конверт повреждён — значение остаётся как есть
    /// (зашифрованная строка), а вызывающий код может показать «ключ недоступен» (T3).
    /// Открытые значения читаются как есть — обратная совместимость.
    /// </summary>
    public string Unprotect(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        return Transform(json, encrypt: false) ?? json;
    }

    private string? Transform(string json, bool encrypt)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // Повреждённый JSON обрабатывает сам репозиторий (карантин/значения по умолчанию).
            return null;
        }

        if (root is null)
            return null;

        var changed = false;
        Visit(root, encrypt, ref changed);
        if (!changed)
            return json; // ничего не изменилось — не переформатируем файл зря

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private void Visit(JsonNode? node, bool encrypt, ref bool changed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (IsSecretProperty(property.Key) && property.Value is JsonValue value
                        && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                    {
                        var transformed = encrypt
                            ? _protector.Protect(text)
                            : UnprotectValue(text);

                        if (transformed is not null && !string.Equals(transformed, text, StringComparison.Ordinal))
                        {
                            obj[property.Key] = transformed;
                            changed = true;
                        }
                    }
                    else
                    {
                        Visit(property.Value, encrypt, ref changed);
                    }
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                    Visit(item, encrypt, ref changed);
                break;
        }
    }

    private string? UnprotectValue(string text)
    {
        if (!_protector.IsProtected(text))
            return text; // открытое значение — обратная совместимость

        var result = _protector.Unprotect(text);
        // Нет ключа/неверный пароль/повреждение — оставляем шифротекст, данные не теряем.
        return result.Succeeded ? result.Value : text;
    }

    private static bool IsSecretProperty(string name) =>
        SecretPropertyNames.Contains(name) && !ExcludedPropertyNames.Contains(name);
}
