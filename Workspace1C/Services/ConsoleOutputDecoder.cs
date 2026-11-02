using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// ЦЕНТРАЛЬНОЕ правило декодирования консольного вывода внешних утилит (issue #83,
/// просьба 7OH «фикс сделать правилом для последующих доработок при работе с выводом
/// в консоли»): кодировка вывода консольных утилит (ring, webinst, ibcmd, rac, git,
/// pg_dump, sqlcmd, where/which) заранее неизвестна — rac/ibcmd на русской Windows пишут
/// в OEM-кодовой странице (cp866), утилиты ring/Java — в ANSI (cp1251) или UTF-8,
/// на Linux — UTF-8. Поэтому читаем СЫРЫЕ байты и декодируем эвристикой:
/// <list type="number">
/// <item>строгий UTF-8 (невалидные байты — ошибка декодирования);</item>
/// <item>если байты не UTF-8 — cp866 и cp1251, выбирается вариант с наименьшим числом
/// символов замены U+FFFD.</item>
/// </list>
/// Единая точка правила: <see cref="ProcessRunner.RunAsync"/> (без явной кодировки),
/// <see cref="RacClient"/> и <see cref="IbcmdService"/> — все декодируют вывод через
/// этот хелпер. Явно заданная кодировка (<c>outputEncoding</c>) имеет приоритет —
/// для утилит с известной фиксированной кодировкой.
/// </summary>
public static class ConsoleOutputDecoder
{
    /// <summary>Строгий UTF-8 (невалидные байты — ошибка декодирования).</summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Кодовые страницы Windows для fallback-декодирования.</summary>
    private static readonly Encoding[] WindowsFallbackEncodings;

    static ConsoleOutputDecoder()
    {
        // Провайдер кодовых страниц регистрируется однократно (идемпотентно).
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        WindowsFallbackEncodings = new[] { Encoding.GetEncoding(866), Encoding.GetEncoding(1251) };
    }

    /// <summary>
    /// Декодирует сырые байты консольного вывода: строгий UTF-8 → cp866 → cp1251
    /// (по числу символов замены U+FFFD). Пустой/нулевой вход — пустая строка.
    /// Public — единое правило для всех мест чтения консольного вывода (issue #83).
    /// </summary>
    public static string Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Не UTF-8 — переходим к кодовым страницам Windows.
        }

        string? best = null;
        var bestBad = int.MaxValue;
        foreach (var encoding in WindowsFallbackEncodings)
        {
            var decoded = encoding.GetString(bytes);
            var bad = CountReplacementChars(decoded);
            if (bad < bestBad)
            {
                bestBad = bad;
                best = decoded;
            }
        }
        return best ?? WindowsFallbackEncodings[0].GetString(bytes);
    }

    /// <summary>Считает символы замены U+FFFD (признак неверной кодировки).</summary>
    private static int CountReplacementChars(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD')
                count++;
        }
        return count;
    }
}
