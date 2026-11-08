using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Workspace1C.Services.Licensing;

/// <summary>
/// Код компьютера для привязки лицензии ПРО (docs/pro/PRO-EDITION.md, раздел 3).
/// Идентификатор ОС (Windows — MachineGuid, Linux — /etc/machine-id) хешируется и
/// показывается как 16 символов Base32 Крокфорда группами по 4: «7K2M-QX9P-4RTA-0HCW».
/// Сам идентификатор ОС за пределы компьютера не передаётся.
/// </summary>
public static class MachineIdProvider
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Base32 Крокфорда (без I, L, O, U)
    private const string Salt = "Workspace1C|";

    /// <summary>Переопределение сырого идентификатора ОС (тесты).</summary>
    public static string? RawIdOverride { get; set; }

    private static string? _cached;

    /// <summary>Код компьютера этого ПК; пустая строка — идентификатор ОС не получен.</summary>
    public static string Current
    {
        get
        {
            if (RawIdOverride is not null)
                return FromRawId(RawIdOverride);
            return _cached ??= FromRawId(ReadRawId());
        }
    }

    /// <summary>Код компьютера из сырого идентификатора ОС (чистая функция).</summary>
    public static string FromRawId(string? rawId)
    {
        var id = (rawId ?? string.Empty).Trim().ToLowerInvariant();
        if (id.Length == 0)
            return string.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + id));
        var code = ToBase32(hash, 10); // 80 бит → 16 символов
        return $"{code.Substring(0, 4)}-{code.Substring(4, 4)}-{code.Substring(8, 4)}-{code.Substring(12, 4)}";
    }

    /// <summary>
    /// Приводит введённый код к каноническому виду: верхний регистр, без пробелов и дефисов,
    /// с заменой похожих символов (O→0, I/L→1) — как принято в Base32 Крокфорда.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;
        var sb = new StringBuilder(code.Length);
        foreach (var ch in code.ToUpperInvariant())
        {
            switch (ch)
            {
                case '-':
                case ' ':
                    continue;
                case 'O':
                    sb.Append('0');
                    break;
                case 'I':
                case 'L':
                    sb.Append('1');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Совпадают ли два кода компьютера с точностью до форматирования.</summary>
    public static bool AreEqual(string? a, string? b)
    {
        var na = Normalize(a);
        return na.Length > 0 && string.Equals(na, Normalize(b), StringComparison.Ordinal);
    }

    private static string ToBase32(byte[] data, int byteCount)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        for (var i = 0; i < byteCount; i++)
        {
            buffer = (buffer << 8) | data[i];
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static string? ReadRawId()
    {
        try
        {
#if WINDOWS
            using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var crypto = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return crypto?.GetValue("MachineGuid") as string;
#else
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path).Trim();
                    if (text.Length > 0)
                        return text;
                }
            }
            return null;
#endif
        }
        catch
        {
            return null;
        }
    }
}
