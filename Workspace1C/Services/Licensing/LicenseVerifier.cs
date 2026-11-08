using System;
using System.Security.Cryptography;
using System.Text;

namespace Workspace1C.Services.Licensing;

/// <summary>Результат проверки лицензионного ключа.</summary>
public enum LicenseStatus
{
    /// <summary>Ключ не введён.</summary>
    None,

    /// <summary>Ключ действителен.</summary>
    Valid,

    /// <summary>Текст не похож на ключ Workspace 1C.</summary>
    Malformed,

    /// <summary>Подпись не сходится (ключ изменён или поддельный).</summary>
    BadSignature,

    /// <summary>Ключ выпущен для другой редакции.</summary>
    WrongEdition,

    /// <summary>Ключ выпущен для другой старшей версии (например 2.x, а программа 3.x).</summary>
    WrongMajorVersion,

    /// <summary>Ключ выпущен для другого компьютера.</summary>
    WrongMachine,

    /// <summary>В сборку не встроен открытый ключ проверки.</summary>
    NotConfigured
}

/// <summary>Итог проверки: статус и, если ключ разобран, его содержимое.</summary>
public sealed record LicenseCheckResult(LicenseStatus Status, LicenseKey? Key)
{
    public bool IsValid => Status == LicenseStatus.Valid;
}

/// <summary>
/// Офлайн-проверка лицензионного ключа ПРО (docs/pro/PRO-EDITION.md, раздел 3):
/// подпись ECDSA P-256/SHA-256, редакция, старшая версия, код компьютера. Срока нет.
/// </summary>
public static class LicenseVerifier
{
    /// <summary>
    /// Открытый ключ проверки (SubjectPublicKeyInfo, base64). Пара ключей создаётся
    /// утилитой выпуска (<c>LicenseIssuer keygen</c>, приватный репозиторий Workspace1C.Pro);
    /// сюда вставляется только ОТКРЫТАЯ часть. Пусто — все ключи отклоняются
    /// со статусом <see cref="LicenseStatus.NotConfigured"/>.
    /// </summary>
    public const string PublicKey = "";

    /// <summary>Переопределение открытого ключа (тесты).</summary>
    public static string? PublicKeyOverride { get; set; }

    /// <summary>Проверяет ключ для текущей версии программы и текущего компьютера.</summary>
    public static LicenseCheckResult Verify(string? keyText) =>
        Verify(keyText, VersionInfo.Major(), MachineIdProvider.Current, PublicKeyOverride ?? PublicKey);

    /// <summary>Проверяет ключ (чистая функция для тестов и утилиты выпуска).</summary>
    public static LicenseCheckResult Verify(string? keyText, int programMajor, string machineCode, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyText))
            return new LicenseCheckResult(LicenseStatus.None, null);
        if (!LicenseKey.TryParse(keyText, out var key) || key is null)
            return new LicenseCheckResult(LicenseStatus.Malformed, null);
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            return new LicenseCheckResult(LicenseStatus.NotConfigured, key);

        if (!VerifySignature(key, publicKeyBase64))
            return new LicenseCheckResult(LicenseStatus.BadSignature, key);
        if (!string.Equals(key.Payload.Edition, "pro", StringComparison.OrdinalIgnoreCase))
            return new LicenseCheckResult(LicenseStatus.WrongEdition, key);
        if (key.Payload.Major != programMajor)
            return new LicenseCheckResult(LicenseStatus.WrongMajorVersion, key);
        if (!MachineIdProvider.AreEqual(key.Payload.Machine, machineCode))
            return new LicenseCheckResult(LicenseStatus.WrongMachine, key);

        return new LicenseCheckResult(LicenseStatus.Valid, key);
    }

    private static bool VerifySignature(LicenseKey key, string publicKeyBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(key.SignedPart),
                key.Signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Ключ локализации текста статуса: <c>Pro.Status.&lt;Статус&gt;</c>.</summary>
    public static string StatusTextKey(LicenseStatus status) => "Pro.Status." + status;
}
