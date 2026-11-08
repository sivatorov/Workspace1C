using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace1C.Services.Licensing;

/// <summary>
/// Содержимое лицензионного ключа ПРО (docs/pro/PRO-EDITION.md, раздел 3).
/// Ключ — строка <c>W1C2.&lt;payload base64url&gt;.&lt;signature base64url&gt;</c>.
/// </summary>
public sealed class LicensePayload
{
    /// <summary>Версия формата полезной нагрузки.</summary>
    [JsonPropertyName("v")] public int FormatVersion { get; set; } = 1;

    /// <summary>Идентификатор ключа (для перевыпуска и учёта продаж).</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>Редакция: «pro».</summary>
    [JsonPropertyName("edition")] public string Edition { get; set; } = "pro";

    /// <summary>Старшее число версии, для которой выпущен ключ (2 → 2.x.x.x).</summary>
    [JsonPropertyName("major")] public int Major { get; set; }

    /// <summary>Код компьютера (<see cref="MachineIdProvider"/>).</summary>
    [JsonPropertyName("machine")] public string Machine { get; set; } = string.Empty;

    /// <summary>Владелец лицензии (организация или ФИО).</summary>
    [JsonPropertyName("owner")] public string Owner { get; set; } = string.Empty;

    /// <summary>Email владельца.</summary>
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;

    /// <summary>Дата выпуска, yyyy-MM-dd.</summary>
    [JsonPropertyName("issued")] public string Issued { get; set; } = string.Empty;
}

/// <summary>Разобранный (но ещё не проверенный) лицензионный ключ.</summary>
public sealed class LicenseKey
{
    /// <summary>Префикс ключа: формат 2.x.</summary>
    public const string Prefix = "W1C2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private LicenseKey(string signedPart, byte[] signature, LicensePayload payload)
    {
        SignedPart = signedPart;
        Signature = signature;
        Payload = payload;
    }

    /// <summary>Подписанная часть: <c>W1C2.&lt;payload&gt;</c> (байты UTF-8 подписываются целиком).</summary>
    public string SignedPart { get; }

    /// <summary>Подпись ECDSA P-256 / SHA-256 в формате IEEE P1363 (64 байта).</summary>
    public byte[] Signature { get; }

    /// <summary>Полезная нагрузка.</summary>
    public LicensePayload Payload { get; }

    /// <summary>Текст ключа целиком.</summary>
    public string Text => SignedPart + "." + Base64Url.Encode(Signature);

    /// <summary>
    /// Разбирает ключ. Пробелы и переводы строк внутри (ключ, вставленный из письма) игнорируются.
    /// Возвращает false при неверной структуре; подпись здесь НЕ проверяется.
    /// </summary>
    public static bool TryParse(string? text, out LicenseKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (!char.IsWhiteSpace(ch))
                sb.Append(ch);
        var compact = sb.ToString();

        var parts = compact.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            var payloadBytes = Base64Url.Decode(parts[1]);
            var signature = Base64Url.Decode(parts[2]);
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null || signature.Length == 0)
                return false;
            key = new LicenseKey(parts[0] + "." + parts[1], signature, payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Подписанная часть для полезной нагрузки (используется утилитой выпуска ключей и тестами).</summary>
    public static string BuildSignedPart(LicensePayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Prefix + "." + Base64Url.Encode(json);
    }

    /// <summary>Собирает текст ключа из подписанной части и подписи.</summary>
    public static string Compose(string signedPart, byte[] signature) =>
        signedPart + "." + Base64Url.Encode(signature);
}

/// <summary>Base64url без выравнивания (RFC 4648 §5).</summary>
public static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Неверная длина base64url.");
        }
        return Convert.FromBase64String(s);
    }
}
