using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #83 «Информация о лицензиях в Серверы 1С» (репорт 7OH от 2026-11-02:
/// «каракули» в выводе ring + фикс сделать правилом для всех мест работы с выводом
/// в консоли): центральный декодер <see cref="ConsoleOutputDecoder"/> — сырые байты
/// + эвристика «UTF-8 → cp866 → cp1251» по числу символов замены U+FFFD (по образцу
/// DecodeRacOutput), а также построение таблицы лицензий
/// <see cref="RingLicenseParser.Parse"/> из корректно декодированного вывода ring.
/// </summary>
public sealed class Issue83ConsoleOutputDecoderTests
{
    static Issue83ConsoleOutputDecoderTests()
    {
        // Кодовые страницы 866/1251 нужны и в тестах (построение ожидаемых байтов);
        // ConsoleOutputDecoder регистрирует провайдер в своём статическом конструкторе.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Decode_Cp866RussianText_IsReadable()
    {
        // «Сервер 1С:Предприятия» в cp866 (OEM-кодировка rac/ibcmd на русской Windows).
        var bytes = Encoding.GetEncoding(866).GetBytes("Сервер 1С:Предприятия");

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal("Сервер 1С:Предприятия", decoded);
    }

    [Fact]
    public void Decode_Utf8RussianText_PassesThrough()
    {
        var bytes = new UTF8Encoding(false).GetBytes("Локальный кластер");

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal("Локальный кластер", decoded);
    }

    [Fact]
    public void Decode_Cp1251RussianText_IsReadableWithoutReplacementChars()
    {
        // ANSI-вывод (утилиты ring/Java на Windows): не валидный UTF-8 → fallback
        // cp866/cp1251. ОДНОБАЙТОВЫЕ кодировки принципиально неразличимы по U+FFFD
        // (они не дают символов замены) — декодер выбирает cp866 (первая в списке);
        // гарантируется лишь отсутствие символов замены, конкретная страница
        // доподлинно знает только утилита/пользователь.
        var bytes = Encoding.GetEncoding(1251).GetBytes("Серийный номер лицензии");

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.False(string.IsNullOrEmpty(decoded));
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    [Fact]
    public void Decode_EmptyAndNull_ReturnEmpty()
    {
        Assert.Equal(string.Empty, ConsoleOutputDecoder.Decode(Array.Empty<byte>()));
        Assert.Equal(string.Empty, ConsoleOutputDecoder.Decode(null));
    }

    [Fact]
    public void Decode_AsciiText_PassesThrough()
    {
        var bytes = Encoding.ASCII.GetBytes("ring license list: OK");

        Assert.Equal("ring license list: OK", ConsoleOutputDecoder.Decode(bytes));
    }

    // ===================== UTF-16-вывод rac (issue #79, v1.0.1.31) =====================

    /// <summary>Реалистичный вывод «infobase info» rac 8.5.4 (поля СУБД источника).</summary>
    private const string RacInfoOutput = """
        infobase        : 0f6d2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e
        name            : erp
        dbms            : MSSQLServer
        db-server       : sql01
        db-name         : erp_db
        db-user         : sa
        locale          : ru_RU
        """;

    [Fact]
    public void Decode_Utf16LeWithBom_IsReadable()
    {
        // Перенаправление «rac … > file» на Windows даёт файл UTF-16LE с BOM «FF FE».
        var payload = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(RacInfoOutput)).ToArray();

        var decoded = ConsoleOutputDecoder.Decode(payload);

        Assert.Equal(RacInfoOutput, decoded);
    }

    [Fact]
    public void Decode_Utf16LeWithoutBom_IsReadable()
    {
        // БЕЗ BOM: у преимущественно латинско-кириллического UTF-16LE каждый второй
        // байт — NUL; однобайтовые кодировки NUL не дают → распознаём UTF-16LE.
        var bytes = Encoding.Unicode.GetBytes(RacInfoOutput);

        var decoded = ConsoleOutputDecoder.Decode(bytes);

        Assert.Equal(RacInfoOutput, decoded);
    }

    [Fact]
    public void Decode_Utf16LeOutput_InfobaseInfo_ParsesDbmsFields()
    {
        // Главный сценарий issue #79: rac отдал валидные поля в stdout (UTF-16LE), но
        // прежняя эвристика (UTF-8 → cp866 → cp1251) превращала вывод в mojibake с
        // NUL-символами — ключи «dbms»/«db-server» не совпадали и свойства базы
        // «не виделись». Теперь вывод декодируется и разбирается до полей СУБД.
        var bytes = Encoding.Unicode.GetBytes(RacInfoOutput);

        var info = RacOutputParser.ToInfobaseInfo(ConsoleOutputDecoder.Decode(bytes));

        Assert.NotNull(info);
        Assert.Equal("MSSQLServer", info!.Dbms);
        Assert.Equal("sql01", info.DbServer);
        Assert.Equal("erp_db", info.DbName);
        Assert.Equal("sa", info.DbUser);
        Assert.Equal(Guid.Parse("0f6d2b55-5f7d-4d9f-9a0e-1b8a0b1c2d3e"), info.InfobaseId);
    }

    // ===================== Таблица лицензий из вывода ring =====================

    /// <summary>Реалистичный вывод «ring license list» (ring 0.20, ru-локаль),
    /// полученный ПОСЛЕ корректного декодирования cp866/UTF-8 (до фикса #83 заголовки
    /// приходили «каракулями» и таблица не распознавалась).</summary>
    private const string RingTableOutput =
        "Тип          Серийный номер                        Версия   Модуль           Пользователь        Действует до\r\n" +
        "Клиентская   6f5a9c8e-1d2b-3c4d-5e6f-7a8b9c0d1e2f  8.5.4    КОРП             Иванов И. И.        01.12.2026\r\n" +
        "Серверная    0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d  8.5.4    СЕРВЕР           ООО «Ромашка»       31.12.2026\r\n";

    [Fact]
    public void RingLicenseParser_DecodedRingOutput_BuildsTableWithColumns()
    {
        var licenses = RingLicenseParser.Parse(RingTableOutput);

        Assert.Equal(2, licenses.Count);

        var first = licenses[0];
        Assert.Equal("6f5a9c8e-1d2b-3c4d-5e6f-7a8b9c0d1e2f", first.SerialNumber);
        Assert.Equal("Клиентская", first.Type);
        Assert.Equal("8.5.4", first.Version);
        Assert.Equal("КОРП", first.Module);
        Assert.Equal("Иванов И. И.", first.User);
        Assert.Equal(new DateTime(2026, 12, 1), first.ExpiryDate);

        var second = licenses[1];
        Assert.Equal("0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d", second.SerialNumber);
        Assert.Equal("Серверная", second.Type);
        Assert.Equal("ООО «Ромашка»", second.User);
    }

    [Fact]
    public void RingLicenseParser_MojibakeHeader_FallsBackToFreeFormWithoutCrash()
    {
        // Регресс: если заголовок всё же пришёл «каракулями» (до фикса кодировки),
        // парсер не должен падать и не должен выдумывать пустую таблицу — уходит
        // в запасной free-form разбор строк с серийниками (или пустой список).
        const string mojibake = "\uFFFD\uFFFD\uFFFD  \uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\r\n" +
            "6f5a9c8e-1d2b-3c4d-5e6f-7a8b9c0d1e2f  Текст лицензии 8 мест";

        var licenses = RingLicenseParser.Parse(mojibake);

        Assert.Single(licenses);
        Assert.Equal("6f5a9c8e-1d2b-3c4d-5e6f-7a8b9c0d1e2f", licenses[0].SerialNumber);
    }

    /// <summary>Проверка «правила для всех мест»: ProcessRunner без явной кодировки
    /// объявляет сырое чтение байтов и центральный декодер в своей документации,
    /// а RacClient и IbcmdService делегируют декодирование общему
    /// <see cref="ConsoleOutputDecoder"/> (файловые стражи по образцу
    /// Issue54EscapeCloseTests).</summary>
    [Fact]
    public void CentralDecoderRule_IsWiredIntoProcessRunnerRacAndIbcmd()
    {
        var processRunner = File.ReadAllText(
            FindRepoRoot(Path.Combine("Workspace1C", "Services", "ProcessRunner.cs")));
        Assert.Contains("ConsoleOutputDecoder.Decode", processRunner, StringComparison.Ordinal);

        var racClient = File.ReadAllText(
            FindRepoRoot(Path.Combine("Workspace1C", "Services", "RacClient.cs")));
        Assert.Contains("ConsoleOutputDecoder.Decode(bytes)", racClient, StringComparison.Ordinal);

        var ibcmd = File.ReadAllText(
            FindRepoRoot(Path.Combine("Workspace1C", "Services", "IbcmdService.cs")));
        Assert.Contains("ConsoleOutputDecoder.Decode", ibcmd, StringComparison.Ordinal);
    }

    private static string FindRepoRoot(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var fullPath = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return fullPath;
    }
}
