using System;
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
/// <item>если байты не UTF-8 — cp866 и cp1251; выбор: (а) валидационная эвристика —
/// вариант, в котором распознаётся известный маркер вывода ring/1С («Имя файла»,
/// «Файл лицензии», «лиценз…»), побеждает даже при равной метрике U+FFFD (issue #83,
/// v1.0.1.23: на коротком выводе ring метрика «минимум U+FFFD» неоднозначна);</item>
/// <item>при неоднозначной метрике (число U+FFFD одинаково или в обоих вариантах нет
/// замен) предпочитается cp866 — фактическая кодировка консоли 1С в русской локали
/// Windows.</item>
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
    /// Декодирует сырые байты консольного вывода: строгий UTF-8 → эвристика маркеров
    /// → cp866/cp1251. Пустой/нулевой вход — пустая строка.
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

        var decoded866 = WindowsFallbackEncodings[0].GetString(bytes);
        var decoded1251 = WindowsFallbackEncodings[1].GetString(bytes);

        // Валидационная эвристика (issue #83, v1.0.1.23): вариант, в котором
        // распознаётся известный маркер вывода ring/1С, считается верным даже при
        // равной (или лучшей у конкурента) метрике U+FFFD — маркер несовместим с
        // неверной кодировкой, а U+FFFD на коротком выводе с латиницей/цифрами
        // неоднозначен.
        var marker866 = ContainsKnownOutputMarker(decoded866);
        var marker1251 = ContainsKnownOutputMarker(decoded1251);
        if (marker866 != marker1251)
            return marker866 ? decoded866 : decoded1251;

        // Неоднозначная метрика (нет замен ни в одном варианте или одинаково):
        // предпочитаем cp866 — фактическую кодировку консоли 1С в русской локали
        // Windows (до v1.0.1.23 здесь побеждал cp1251 при меньшем числе U+FFFD,
        // что давало mojibake «шь Їрщыр» вместо «Имя файла» в выводе ring).
        var bad866 = CountReplacementChars(decoded866);
        var bad1251 = CountReplacementChars(decoded1251);
        return bad866 <= bad1251 ? decoded866 : decoded1251;
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

    /// <summary>
    /// Известные кириллические маркеры вывода утилит ring/1С (issue #83, v1.0.1.23):
    /// фрагменты, которые встречаются в выводе «ring license list» и его mojibake-
    /// исправленных вариантах. Сравнение — по инвариантной к регистру подстроке.
    /// Internal — юнит-тесты и <see cref="FixCp1251ToCp866Mojibake"/>.
    /// </summary>
    internal static readonly string[] KnownOutputMarkers =
    {
        "имя файла",
        "файл лицензии",
        "лиценз",
        "серийный номер"
    };

    /// <summary>
    /// Распознаётся ли в тексте хотя бы один известный маркер вывода ring/1С
    /// (<see cref="KnownOutputMarkers"/>). Internal — юнит-тесты.
    /// </summary>
    internal static bool ContainsKnownOutputMarker(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var marker in KnownOutputMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Чинит mojibake «cp866-текст, декодированный как cp1251» (issue #83, v1.0.1.23,
    /// репорт 7OH: «шь Їрщыр» вместо «Имя файла» в ответе ring): текст
    /// перезакодируется cp1251 → байты → cp866; если после этого распознаётся
    /// известный маркер вывода ring/1С, а в исходном тексте не распознавался —
    /// возвращается исправленный текст. Иначе текст возвращается как есть
    /// (перезакодирование корректного текста без маркеров не применяется).
    /// Public — переиспользуется там, где сырые байты уже недоступны
    /// (<see cref="RingLicenseService.BuildNoLicensesMessage"/>).
    /// </summary>
    public static string FixCp1251ToCp866Mojibake(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;
        if (ContainsKnownOutputMarker(text))
            return text;

        string fixedText;
        try
        {
            fixedText = WindowsFallbackEncodings[0].GetString(
                WindowsFallbackEncodings[1].GetBytes(text));
        }
        catch
        {
            // Перезакодирование невозможно (символы вне cp1251) — текст не трогаем.
            return text;
        }

        return ContainsKnownOutputMarker(fixedText) ? fixedText : text;
    }
}
