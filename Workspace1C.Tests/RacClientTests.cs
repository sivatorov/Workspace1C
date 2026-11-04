using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сборки аргументов командной строки rac (<see cref="RacClient.BuildArguments"/>)
/// и маскирования пароля (<see cref="SensitiveDataMasker.MaskRacPassword"/>) —
/// без запуска реального процесса rac.
/// Точка подключения собирается ЕДИНЫМ токеном «host:port» первым аргументом
/// (rac.exe host:port cluster list): раздельные --host=/--port= не разбираются
/// новыми версиями платформы (issue #324).
/// </summary>
public sealed class RacClientTests
{
    private static RacConnectionParams Params(
        string address = "localhost", int port = 1540,
        string user = "Admin", string password = "secret")
    {
        return new RacConnectionParams
        {
            Address = address,
            Port = port,
            User = user,
            Password = password
        };
    }

    [Fact]
    public void BuildArguments_IncludesConnectionParams_ThenCommand()
    {
        var args = RacClient.BuildArguments(Params(), "cluster", "list");

        Assert.Equal(new[]
        {
            "localhost:1540",
            "--user=Admin",
            "--password=secret",
            "cluster",
            "list"
        }, args);
    }

    [Fact]
    public void BuildArguments_HostAndPort_AreSingleToken()
    {
        // Ключевой формат issue #324: rac.exe localhost:1545 cluster list.
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1545 }, "cluster", "list");

        Assert.Equal("srv1:1545", args[0]);
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
        Assert.Equal(new[] { "srv1:1545", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_OmitsEmptyUserAndPassword()
    {
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1545 }, "cluster", "list");

        Assert.Equal(new[] { "srv1:1545", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_EmptyAddress_OmitsConnectionToken()
    {
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "   ", Port = 1540, User = "Admin" },
            "cluster", "list");

        Assert.Equal(new[] { "--user=Admin", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_ZeroPort_AddressIsPlainHost()
    {
        // Порт ≤ 0: rac.exe host cluster list — без «:port».
        var args = RacClient.BuildArguments(Params(port: 0), "cluster", "list");

        Assert.Equal("localhost", args[0]);
        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
        Assert.Equal(1540, IRacClient.DefaultPort);
    }

    [Fact]
    public void BuildArguments_ClusterList_HasNoClusterOption()
    {
        var args = RacClient.BuildArguments(Params(), "cluster", "list");

        Assert.DoesNotContain(args, a => a.StartsWith("--cluster="));
    }

    [Fact]
    public void BuildArguments_ProcessList_IncludesClusterOption()
    {
        var clusterId = Guid.NewGuid();
        var args = RacClient.BuildArguments(Params(), "process", "list", $"--cluster={clusterId}");

        Assert.Contains($"--cluster={clusterId}", args);
        Assert.Equal("--cluster=" + clusterId, args[^1]);
    }

    [Fact]
    public void SessionTerminateArgs_WithoutProcess_OmitsProcessOption()
    {
        // Вкладка «Сеансы»: rac самого находит рабочий процесс — «--process» не нужен.
        var clusterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var args = RacClient.SessionTerminateArgs(clusterId, sessionId, processId: null);

        Assert.Equal(new[]
        {
            "session", "terminate",
            $"--cluster={clusterId}",
            $"--session={sessionId}"
        }, args);
        Assert.DoesNotContain(args, a => a.StartsWith("--process="));
    }

    [Fact]
    public void SessionTerminateArgs_WithProcess_IncludesProcessOption()
    {
        // issue #69 («повторить — ошибка», «Ошибка разбора параметра: process»): рабочий
        // процесс сеанса известен из «connection list» — rac 8.5.4 требует «--process=<pid>».
        var clusterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var args = RacClient.SessionTerminateArgs(clusterId, sessionId, processId);

        Assert.Equal(new[]
        {
            "session", "terminate",
            $"--cluster={clusterId}",
            $"--process={processId}",
            $"--session={sessionId}"
        }, args);
    }

    [Fact]
    public void SessionTerminateArgs_WithEmptyProcess_OmitsProcessOption()
    {
        var clusterId = Guid.NewGuid();
        var args = RacClient.SessionTerminateArgs(clusterId, Guid.NewGuid(), Guid.Empty);

        Assert.DoesNotContain(args, a => a.StartsWith("--process="));
    }

    [Fact]
    public void ConnectionDisconnectArgs_WithProcess_IncludesProcessOption()
    {
        // issue #69: «connection disconnect» тоже требует «--process=<pid>» в rac 8.5.4 —
        // pid берётся из строки «connection list» (процесс, обслуживающий соединение).
        var clusterId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var args = RacClient.ConnectionDisconnectArgs(clusterId, connectionId, processId);

        Assert.Equal(new[]
        {
            "connection", "disconnect",
            $"--cluster={clusterId}",
            $"--process={processId}",
            $"--connection={connectionId}"
        }, args);
    }

    [Fact]
    public void ConnectionDisconnectArgs_WithoutProcess_OmitsProcessOption()
    {
        var clusterId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var args = RacClient.ConnectionDisconnectArgs(clusterId, connectionId);

        Assert.Equal(new[]
        {
            "connection", "disconnect",
            $"--cluster={clusterId}",
            $"--connection={connectionId}"
        }, args);
        Assert.DoesNotContain(args, a => a.StartsWith("--process="));
    }

    [Fact]
    public void SessionTerminateArgs_WithCredentials_AddsInfobaseUserAndPassword()
    {
        // issue #69, комментарий 7OH от 2026-10-27: rac отклоняет «session terminate»
        // защищённой базы с «Недостаточно прав пользователя на информационную базу»,
        // если не переданы --infobase-user/--infobase-pwd — сеансы не снимаются.
        var clusterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var args = RacClient.SessionTerminateArgs(clusterId, sessionId, processId: null,
            "ИБ-админ", "секрет");

        Assert.Equal(new[]
        {
            "session", "terminate",
            $"--cluster={clusterId}",
            $"--session={sessionId}",
            "--infobase-user=ИБ-админ",
            "--infobase-pwd=секрет"
        }, args);
        Assert.DoesNotContain(args, a => a.StartsWith("--process="));
    }

    [Fact]
    public void SessionTerminateArgs_WithoutCredentials_NoInfobaseUserOrPasswordOptions()
    {
        // Обычная вкладка «Сеансы»: креды администратора ИБ неизвестны — флагов нет.
        var clusterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var args = RacClient.SessionTerminateArgs(clusterId, sessionId);

        Assert.DoesNotContain(args, a => a.StartsWith("--infobase-user="));
        Assert.DoesNotContain(args, a => a.StartsWith("--infobase-pwd="));
    }

    [Fact]
    public void ConnectionDisconnectArgs_WithCredentials_AddsInfobaseUserAndPassword()
    {
        // issue #69, комментарий 7OH от 2026-10-27: то же, что и «session terminate», —
        // «connection disconnect» защищённой базы без кредов падает «Недостаточно прав».
        var clusterId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var args = RacClient.ConnectionDisconnectArgs(clusterId, connectionId, processId,
            "ИБ-админ", "секрет");

        Assert.Equal(new[]
        {
            "connection", "disconnect",
            $"--cluster={clusterId}",
            $"--process={processId}",
            $"--connection={connectionId}",
            "--infobase-user=ИБ-админ",
            "--infobase-pwd=секрет"
        }, args);
    }

    [Fact]
    public void ConnectionDisconnectArgs_WithoutCredentials_NoInfobaseUserOrPasswordOptions()
    {
        var clusterId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var args = RacClient.ConnectionDisconnectArgs(clusterId, connectionId);

        Assert.DoesNotContain(args, a => a.StartsWith("--infobase-user="));
        Assert.DoesNotContain(args, a => a.StartsWith("--infobase-pwd="));
    }

    [Fact]
    public void BuildArguments_ClusterInfo_IncludesHostPortToken_AndClusterOption()
    {
        // Команда «cluster info» (данные кластера на вкладке «Информация о кластере»,
        // issue #324): точка подключения одним токеном host:port + --cluster=<uuid>.
        var clusterId = Guid.Parse("8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b");
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1540, User = "Admin", Password = "secret" },
            "cluster", "info", $"--cluster={clusterId}");

        Assert.Equal(new[]
        {
            "srv1:1540",
            "--user=Admin",
            "--password=secret",
            "cluster",
            "info",
            "--cluster=" + clusterId
        }, args);
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
    }

    [Fact]
    public void BuildArguments_KeepsValuesWithSpacesAsSingleToken()
    {
        // Адрес, пароль и логин с пробелами передаются одним токеном (ArgumentList без shell).
        var args = RacClient.BuildArguments(
            new RacConnectionParams
            {
                Address = "srv 1",
                Port = 1540,
                User = "Иванов",
                Password = "пароль с пробелами"
            },
            "session", "list");

        Assert.Contains("srv 1:1540", args);
        Assert.Contains("--user=Иванов", args);
        Assert.Contains("--password=пароль с пробелами", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
    }

    [Fact]
    public void MaskRacPassword_HidesPasswordValue()
    {
        // Формат журнала после перехода на единый токен «host:port»: пароль по-прежнему
        // передаётся как --password=... и маскируется целиком.
        const string line = "localhost:1540 --user=Admin --password=secret cluster list";
        var masked = SensitiveDataMasker.MaskRacPassword(line);

        Assert.DoesNotContain("secret", masked);
        Assert.Contains("--password=***", masked);
        // Точка подключения host:port и остальные аргументы не затрагиваются.
        Assert.Contains("localhost:1540", masked);
        Assert.Contains("--user=Admin", masked);
    }

    [Fact]
    public void MaskRacPassword_LeavesOtherArguments_AndNull()
    {
        const string line = "srv1:1540 cluster list";

        Assert.Equal(line, SensitiveDataMasker.MaskRacPassword(line));
        Assert.Null(SensitiveDataMasker.MaskRacPassword(null!));
    }

    [Fact]
    public void BuildArguments_JobList_IncludesClusterOption()
    {
        var clusterId = Guid.NewGuid();
        var args = RacClient.BuildArguments(Params(), "job", "list", $"--cluster={clusterId}");

        Assert.Contains($"--cluster={clusterId}", args);
        Assert.Equal("localhost:1540", args[0]);
        Assert.Equal("--cluster=" + clusterId, args[^1]);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("disable")]
    [InlineData("enable")]
    public void BuildArguments_SetJobState_IncludesCommandAndOptions(string expectedSubcommand)
    {
        var clusterId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var args = RacClient.BuildArguments(
            Params(), "job", expectedSubcommand, $"--cluster={clusterId}", $"--job={jobId}");

        Assert.Equal(new[]
        {
            "localhost:1540",
            "--user=Admin",
            "--password=secret",
            "job",
            expectedSubcommand,
            "--cluster=" + clusterId,
            "--job=" + jobId
        }, args);
    }

    // ---------- DecodeRacOutput (кодировка вывода rac, issue #324) ----------

    [Fact]
    public void DecodeRacOutput_Utf8_Passthrough()
    {
        // Linux/Avalonia: rac выводит UTF-8 — строгий UTF-8 декодируется как есть.
        var bytes = Encoding.UTF8.GetBytes("cluster\tname\n111-222\tЛокальный кластер\n");

        Assert.Equal("cluster\tname\n111-222\tЛокальный кластер\n",
            RacClient.DecodeRacOutput(bytes));
    }

    [Fact]
    public void DecodeRacOutput_Cp866_RussianText_IsDecoded()
    {
        // Windows: rac пишет в OEM-кодовой странице (cp866) — байты невалидны как UTF-8,
        // fallback должен декодировать их без символов замены. Провайдер кодовых страниц
        // регистрируется здесь, чтобы тест не зависел от порядка выполнения.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(866).GetBytes(
            "cluster\tname\n111-222\tЛокальный кластер\n");

        var decoded = RacClient.DecodeRacOutput(bytes);

        Assert.Contains("Локальный кластер", decoded);
        Assert.DoesNotContain("\uFFFD", decoded);
    }

    [Fact]
    public void DecodeRacOutput_SingleByteWindowsText_DecodesWithoutReplacementChars()
    {
        // Однобайтовые кодовые страницы (cp866 приоритетно, cp1251 резерв) не дают
        // символов замены U+FFFD и сохраняют ASCII-часть (цифры, GUID, ключи колонок —
        // то, что парсеру нужно). Приоритет cp866 соответствует OEM-кодировке консоли
        // Windows; точное различие 866/1251 без образца вывода недостижимо, поэтому
        // тест фиксирует именно гарантию отсутствия потерь при fallback (issue #324).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1251).GetBytes("Имя: Тестовый кластер 1С");

        var decoded = RacClient.DecodeRacOutput(bytes);

        Assert.DoesNotContain("\uFFFD", decoded);
        // ASCII-фрагмент (« 1» перед «С») сохраняется в любой однобайтовой кодировке.
        Assert.Contains(" 1", decoded);
    }

    [Fact]
    public void DecodeRacOutput_EmptyAndNull_ReturnEmpty()
    {
        Assert.Equal(string.Empty, RacClient.DecodeRacOutput(Array.Empty<byte>()));
        Assert.Equal(string.Empty, RacClient.DecodeRacOutput(null!));
    }

    // ---------- EnsureParsedOrThrow (issue #324: непустой нераспознанный вывод = ошибка) ----------

    [Fact]
    public void EnsureParsedOrThrow_NonEmptyUnparsedOutput_ThrowsRacOutputParseException()
    {
        // rac завершился с кодом 0 и вернул НЕпустой вывод, но ни табличный разбор, ни блоки
        // «ключ : значение» не дали ни одной строки данных — новая версия формата. Монитор
        // должен показать понятную ошибку и ОСТАНОВИТЬ автообновление (без повторов каждые 5 с).
        var ex = Assert.Throws<RacOutputParseException>(
            () => RacClient.EnsureParsedOrThrow("some future format output\n", 0, "process list"));

        Assert.Contains("process list", ex.Message);
    }

    [Fact]
    public void EnsureParsedOrThrow_EmptyOutput_DoesNotThrow()
    {
        // Пустой вывод — легитимный случай «данных нет» (регресс): исключения быть не должно.
        RacClient.EnsureParsedOrThrow(string.Empty, 0, "process list");
        RacClient.EnsureParsedOrThrow(null!, 0, "process list");
        RacClient.EnsureParsedOrThrow("   \n\t ", 0, "process list");
    }

    [Fact]
    public void EnsureParsedOrThrow_ParsedRows_DoesNotThrow()
    {
        // Разбор дал строки данных — правило не срабатывает даже при непустом выводе.
        RacClient.EnsureParsedOrThrow("cluster\tname\n", 1, "process list");
    }

    // ---------- форматы передачи id кластера и ключ кэша (issue #324/#25) ----------

    [Fact]
    public void JobListArgs_Format0_UsesEqualsToken()
    {
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(new[] { "job", "list", $"--cluster={clusterId}" }, RacClient.JobListArgs(0, clusterId));
    }

    [Fact]
    public void JobListArgs_Format1_UsesTwoTokens()
    {
        // rac 8.5.4.1878 отклоняет формат 0 (код -1, «Ошибка разбора параметра»);
        // формат 1 — два токена «--cluster <uuid>» (issue #324).
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(new[] { "job", "list", "--cluster", clusterId.ToString() }, RacClient.JobListArgs(1, clusterId));
    }

    [Fact]
    public void JobListArgs_Format2_UsesPositionalUuid()
    {
        // По логам 2026-10-07/08 rac 8.5.4.1878 отклоняет ОБА варианта «--cluster»
        // («Ошибка разбора параметра: --cluster») — добавлен формат 2: позиционный
        // uuid без имени параметра (issue #324, 0.3.9.330).
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(new[] { "job", "list", clusterId.ToString() }, RacClient.JobListArgs(2, clusterId));
    }

    [Fact]
    public void ClusterArgs_Format0_UsesEqualsToken()
    {
        // Обобщённый helper (issue #25): префикс команды произвольный, формат 0 —
        // «--cluster=<uuid>» (стандарт, поведение rac 8.3/8.4 по умолчанию).
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(
            new[] { "infobase", "summary", "list", $"--cluster={clusterId}" },
            RacClient.ClusterArgs(new[] { "infobase", "summary", "list" }, 0, clusterId));
    }

    [Fact]
    public void ClusterArgs_Format1_UsesTwoTokens()
    {
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(
            new[] { "infobase", "summary", "list", "--cluster", clusterId.ToString() },
            RacClient.ClusterArgs(new[] { "infobase", "summary", "list" }, 1, clusterId));
    }

    [Fact]
    public void ClusterArgs_Format2_UsesPositionalUuid()
    {
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(
            new[] { "infobase", "summary", "list", clusterId.ToString() },
            RacClient.ClusterArgs(new[] { "infobase", "summary", "list" }, 2, clusterId));
    }

    [Fact]
    public void InfobaseSummaryListArgs_Formats()
    {
        // «infobase summary list» — команда окна «Импорт из кластера 1С» (issue #25):
        // rac 8.5.x может отклонять «--cluster=<uuid>» и в ней, как в «job list».
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(
            new[] { "infobase", "summary", "list", $"--cluster={clusterId}" },
            RacClient.InfobaseSummaryListArgs(0, clusterId));
        Assert.Equal(
            new[] { "infobase", "summary", "list", "--cluster", clusterId.ToString() },
            RacClient.InfobaseSummaryListArgs(1, clusterId));
        Assert.Equal(
            new[] { "infobase", "summary", "list", clusterId.ToString() },
            RacClient.InfobaseSummaryListArgs(2, clusterId));
    }

    [Fact]
    public void ClusterInfoArgs_Formats()
    {
        // «cluster info» — команда окна «Импорт из кластера 1С» (issue #25): rac 8.5.x
        // может отклонять «--cluster=<uuid>» и в ней, как в «job list»/«infobase summary
        // list» — перебор форматов нужен наравне с ними.
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");

        Assert.Equal(
            new[] { "cluster", "info", $"--cluster={clusterId}" },
            RacClient.ClusterInfoArgs(0, clusterId));
        Assert.Equal(
            new[] { "cluster", "info", "--cluster", clusterId.ToString() },
            RacClient.ClusterInfoArgs(1, clusterId));
        Assert.Equal(
            new[] { "cluster", "info", clusterId.ToString() },
            RacClient.ClusterInfoArgs(2, clusterId));
    }

    [Fact]
    public void ClusterFormatCount_CoversAllFormats()
    {
        Assert.Equal(3, RacClient.ClusterFormatCount);
        // Имена форматов — для журнала; должны отличаться между собой.
        Assert.Equal(
            new[] { "--cluster=<uuid>", "--cluster <uuid>", "<uuid>" },
            new[]
            {
                RacClient.ClusterFormatName(0),
                RacClient.ClusterFormatName(1),
                RacClient.ClusterFormatName(2)
            });
    }

    [Fact]
    public void ClusterFormatKey_IncludesConnectionClusterAndCommand_NotPassword()
    {
        // Ключ кэша формата: точка подключения + учётная запись + кластер + ИМЯ КОМАНДЫ
        // (issue #25: кэш «job list» и «infobase summary list» не конфликтует); пароль
        // не должен попадать в ключ (секреты не хранятся в словаре/логах).
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");
        var parameters = new RacConnectionParams
        {
            Address = "srv1",
            Port = 1545,
            User = "Admin",
            Password = "secret-password"
        };

        var jobKey = RacClient.ClusterFormatKey(parameters, clusterId, "job list");
        var infobaseKey = RacClient.ClusterFormatKey(parameters, clusterId, "infobase summary list");

        Assert.Contains("srv1:1545", jobKey);
        Assert.Contains("Admin", jobKey);
        Assert.Contains(clusterId.ToString(), jobKey);
        Assert.Contains("job list", jobKey);
        Assert.DoesNotContain("secret-password", jobKey);
        // Разные команды — разные ключи: кэш форматов не конфликтует (issue #25).
        Assert.NotEqual(jobKey, infobaseKey);
        Assert.Contains("infobase summary list", infobaseKey);
    }

    // ---------- перебор форматов list-команды (issue #25) ----------

    [Fact]
    public async Task RunListCommandWithFormatFallback_FirstFormatSuccess_IsCached()
    {
        // rac 8.3/8.4: формат 0 («--cluster=<uuid>») успешен с первой попытки —
        // rac запускается один раз, формат запоминается в кэше.
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<int>();
        var output = "infobase : 99999999-aaaa-bbbb-cccc-ddddeeeeffff\n";

        var result = await RacClient.RunListCommandWithFormatFallbackAsync(
            index =>
            {
                attempts.Add(index);
                return Task.FromResult(output);
            },
            RacClient.LooksLikeUsageHelp,
            formats, "key|job list", "job list", new CapturingLogger());

        Assert.Equal(output, result);
        Assert.Equal(new[] { 0 }, attempts);
        Assert.Equal(0, formats["key|job list"]);
    }

    [Fact]
    public async Task RunListCommandWithFormatFallback_SecondFormatSuccess_IsCached()
    {
        // rac 8.5.x: формат 0 отклонён (код -1) — пробуется формат 1, успех запоминается.
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<int>();
        var output = "infobase : 99999999-aaaa-bbbb-cccc-ddddeeeeffff\n";

        var result = await RacClient.RunListCommandWithFormatFallbackAsync(
            index =>
            {
                attempts.Add(index);
                if (index == 0)
                    throw new RacClientException("rac завершился с кодом -1: Ошибка разбора параметра: --cluster=…");
                return Task.FromResult(output);
            },
            RacClient.LooksLikeUsageHelp,
            formats, "key|infobase summary list", "infobase summary list", new CapturingLogger());

        Assert.Equal(output, result);
        Assert.Equal(new[] { 0, 1 }, attempts);
        Assert.Equal(1, formats["key|infobase summary list"]);
    }

    [Fact]
    public async Task RunListCommandWithFormatFallback_StartsFromCachedFormat()
    {
        // Повторный запрос: рабочий формат уже в кэше — rac запускается один раз
        // с запомненным форматом (без заведомо падающих попыток).
        var formats = new Dictionary<string, int>(StringComparer.Ordinal) { { "key|job list", 2 } };
        var attempts = new List<int>();

        await RacClient.RunListCommandWithFormatFallbackAsync(
            index =>
            {
                attempts.Add(index);
                return Task.FromResult("job : 44444444-5555-6666-7777-88889999aaaa\n");
            },
            RacClient.LooksLikeUsageHelp,
            formats, "key|job list", "job list", new CapturingLogger());

        Assert.Equal(new[] { 2 }, attempts);
    }

    [Fact]
    public async Task RunListCommandWithFormatFallback_UsageHelp_NotCached_Retries()
    {
        // rac может завершить команду с кодом 0, но вернуть справку вместо данных —
        // такой вывод НЕ считается успехом формата: пробуется следующий синтаксис.
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<int>();

        var result = await RacClient.RunListCommandWithFormatFallbackAsync(
            index =>
            {
                attempts.Add(index);
                return Task.FromResult(index == 0
                    ? "1C:Enterprise 8.5 Remote Administrative Client Utility\nИспользование: rac [режим] [команда] …\n"
                    : "infobase : 99999999-aaaa-bbbb-cccc-ddddeeeeffff\n");
            },
            RacClient.LooksLikeUsageHelp,
            formats, "key|infobase summary list", "infobase summary list", new CapturingLogger());

        Assert.Equal(new[] { 0, 1 }, attempts);
        Assert.Equal(1, formats["key|infobase summary list"]);
        Assert.Contains("99999999", result);
    }

    [Fact]
    public async Task RunListCommandWithFormatFallback_AllFormatsFail_ThrowsWithSummary_AndDropsCache()
    {
        // Все три формата отклонены — понятная ошибка со сводкой попыток; запись
        // формата удаляется из кэша (сменилась версия платформы — перепробовать заново).
        var formats = new Dictionary<string, int>(StringComparer.Ordinal) { { "key|job list", 0 } };
        var attempts = new List<int>();

        var ex = await Assert.ThrowsAsync<RacClientException>(() =>
            RacClient.RunListCommandWithFormatFallbackAsync(
                index =>
                {
                    attempts.Add(index);
                    throw new RacClientException($"rac завершился с кодом -1 (формат {index})");
                },
                RacClient.LooksLikeUsageHelp,
                formats, "key|job list", "job list", new CapturingLogger()));

        Assert.Equal(new[] { 0, 1, 2 }, attempts);
        Assert.Contains("job list", ex.Message);
        Assert.Contains("--cluster=<uuid>", ex.Message);
        Assert.Contains("--cluster <uuid>", ex.Message);
        Assert.Contains("<uuid>", ex.Message);
        Assert.DoesNotContain("key|job list", formats);
    }

    // ---------- кэш форматов только в памяти, без файла на диске (issue #39) ----------

    [Fact]
    public async Task FormatCache_StaysInMemory_NoLegacyFileCreated()
    {
        // issue #39: раньше перебор форматов читал (в конструкторе) и писал (после команд)
        // файл rac_joblist_formats.json в каталоге данных. С 1.0.0.40 кэш — ТОЛЬКО в памяти:
        // выполнение list-команд не касается диска. Тест использует отдельный временный
        // каталог «данных»: устаревший файл прежней версии удаляется при «первом старте»
        // (RacJobListFormatCleaner), после чего цикл перебора форматов НЕ создаёт его заново.
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);

        var legacyPath = Path.Combine(temp.Path, RacJobListFormatCleaner.LegacyFileName);
        File.WriteAllText(legacyPath, "{\"key|job list\": 2}");

        // «Запуск приложения»: одноразовая чистка исторического файла (аналог вызова
        // RacJobListFormatCleaner.Clean() при старте WPF/Avalonia).
        RacJobListFormatCleaner.Clean(temp.Path);
        Assert.False(File.Exists(legacyPath));

        // «Работа с кластером»: перебор форматов в памяти (0 отклонён → 1 успешен → запомнен).
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempts = new List<int>();
        const string output = "infobase : 99999999-aaaa-bbbb-cccc-ddddeeeeffff\n";

        Func<int, Task<string>> run = index =>
        {
            attempts.Add(index);
            if (index == 0)
                throw new RacClientException("rac завершился с кодом -1: Ошибка разбора параметра: --cluster=…");
            return Task.FromResult(output);
        };

        // Первый вызов: формат 0 отклонён — успешен формат 1, запомнен в памяти.
        await RacClient.RunListCommandWithFormatFallbackAsync(
            run, RacClient.LooksLikeUsageHelp, formats, "key|infobase summary list",
            "infobase summary list", new CapturingLogger());
        Assert.Equal(new[] { 0, 1 }, attempts);
        Assert.Equal(1, formats["key|infobase summary list"]);

        // Повторный вызов: rac стартует сразу с запомненного формата 1 (без дисковых обращений).
        attempts.Clear();
        await RacClient.RunListCommandWithFormatFallbackAsync(
            run, RacClient.LooksLikeUsageHelp, formats, "key|infobase summary list",
            "infobase summary list", new CapturingLogger());
        Assert.Equal(new[] { 1 }, attempts);

        // Файл так и не создан в каталоге данных после всех команд (дискового кэша нет).
        Assert.False(File.Exists(legacyPath));
        Assert.Empty(Directory.GetFiles(temp.Path, RacJobListFormatCleaner.LegacyFileName));
    }

    // ============ cluster info из кэша cluster list (issue #324) ============

    private const string TwoClustersKvOutput = """
        cluster : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9
        host : ALF
        port : 27541
        name : "Локальный кластер"
        expiration-timeout : 60

        cluster : 2a4b1e0c-1111-4b1a-909f-cff4c8de61d9
        host : SRV2
        port : 1741
        name : "Второй кластер"
        expiration-timeout : 120
        """;

    [Fact]
    public void ExtractClusterBlock_FirstCluster_ReturnsBlockLines()
    {
        var block = RacClient.ExtractClusterBlock(
            TwoClustersKvOutput, Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9"));

        Assert.NotNull(block);
        Assert.Contains("Локальный кластер", block);
        Assert.Contains("expiration-timeout : 60", block);
        Assert.DoesNotContain("Второй кластер", block);
    }

    [Fact]
    public void ExtractClusterBlock_SecondCluster_ReturnsOnlySecondBlock()
    {
        var block = RacClient.ExtractClusterBlock(
            TwoClustersKvOutput, Guid.Parse("2a4b1e0c-1111-4b1a-909f-cff4c8de61d9"));

        Assert.NotNull(block);
        Assert.Contains("Второй кластер", block);
        Assert.Contains("expiration-timeout : 120", block);
        Assert.DoesNotContain("Локальный кластер", block);
    }

    [Fact]
    public void ExtractClusterBlock_ClusterNotFound_ReturnsNull()
    {
        Assert.Null(RacClient.ExtractClusterBlock(
            TwoClustersKvOutput, Guid.Parse("00000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public void ExtractClusterBlock_EmptyOrInvalid_ReturnsNull()
    {
        var id = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");
        Assert.Null(RacClient.ExtractClusterBlock(null, id));
        Assert.Null(RacClient.ExtractClusterBlock(string.Empty, id));
        Assert.Null(RacClient.ExtractClusterBlock("port : 1541", id));
    }

    // ---------- Диагностика нераспознанного вывода (issue #324, лог 7OH от 2026-10-08):
    // при exit=0, stdout>0 и 0 распознанных записей в журнал пишется WARN с командой,
    // объёмом вывода и его началом — по журналу видно, какая из шести параллельных
    // list-команд не распознана и в каком виде пришёл вывод. ----------

    [Fact]
    public void EnsureParsedOrThrow_WithLogger_WarnsWithCommandAndOutputPreview()
    {
        var logger = new CapturingLogger();
        const string output = "some future format line 1\nsome future format line 2\n";

        Assert.Throws<RacOutputParseException>(
            () => RacClient.EnsureParsedOrThrow(output, 0, "session list", logger));

        var warn = Assert.Single(logger.Warnings);
        Assert.Contains("session list", warn);                       // какая команда
        Assert.Contains($"stdout={output.Length}", warn);            // объём вывода
        Assert.Contains("some future format line 1", warn);          // начало вывода
        Assert.DoesNotContain("\n", warn);                           // одна строка журнала
    }

    [Fact]
    public void EnsureParsedOrThrow_WithoutLogger_StillThrows()
    {
        // Логгер опционален: старые вызовы (3 аргумента) продолжают работать.
        Assert.Throws<RacOutputParseException>(
            () => RacClient.EnsureParsedOrThrow("future format", 0, "process list"));
    }

    [Fact]
    public void EnsureParsedOrThrow_IncludesMaskedOutputPrefix()
    {
        // issue #25: текст ошибки парсинга содержит префикс вывода rac (первые ~200
        // символов) с маскированным паролем — при новом формате причина видна сразу
        // в сообщении окна, без чтения журнала.
        const string output =
            "cluster-id : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9\n" +
            "name : \"Новый формат 8.5.1\"\n" +
            "--password=super-secret\n";

        var ex = Assert.Throws<RacOutputParseException>(
            () => RacClient.EnsureParsedOrThrow(output, 0, "cluster list"));

        Assert.Contains("cluster list", ex.Message);
        Assert.Contains("cluster-id : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9", ex.Message);
        Assert.Contains("Новый формат 8.5.1", ex.Message);
        // Пароль в префиксе вывода маскируется (SensitiveDataMasker).
        Assert.DoesNotContain("super-secret", ex.Message);
        Assert.Contains("--password=***", ex.Message);
    }

    [Fact]
    public void Preview_FlattensLines_AndTruncatesTo200Chars()
    {
        var longOutput = string.Concat(Enumerable.Repeat("абвгд ", 60)); // 360 символов

        var preview = RacClient.Preview(longOutput);

        Assert.True(preview.Length <= 201); // 200 символов + «…»
        Assert.EndsWith("…", preview);
        Assert.DoesNotContain("\n", RacClient.Preview("a\r\nb\tc"));
        Assert.Contains("a b c", RacClient.Preview("a\r\nb\tc"));
    }

    /// <summary>Логгер-заглушка: собирает сообщения для проверки диагностики.</summary>
    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) { }
    }

    [Fact]
    public void ToClusterInfo_FromExtractedBlock_FillsNameAndPort()
    {
        // Блок из cluster list должен разбираться так же, как вывод cluster info:
        // это основание для переиспользования кэша вместо запуска rac (issue #324).
        var block = RacClient.ExtractClusterBlock(
            TwoClustersKvOutput, Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9"))!;

        var info = RacOutputParser.ToClusterInfo(block);

        Assert.Equal("Локальный кластер", info.Name);
        Assert.Equal(27541, info.Port);
    }

    // ---------- LooksLikeUsageHelp (issue #324, 0.3.10.3: справка вместо списка заданий) ----------

    [Fact]
    public void LooksLikeUsageHelp_RussianHelp_IsDetected()
    {
        // Фактический случай 8.5.4.1878: «job list <uuid>» завершается с кодом 0,
        // но rac вернул справку об использовании (stdout=1833 симв.) — 0 записей.
        const string output =
            "Использование: rac [режим] [команда] [параметры]\n" +
            "Команды: cluster, infobase, session, connection, process, lock, job\n" +
            "Пример: rac localhost:1540 cluster list";
        Assert.True(RacClient.LooksLikeUsageHelp(output));
    }

    [Fact]
    public void LooksLikeUsageHelp_EnglishHelp_IsDetected()
    {
        Assert.True(RacClient.LooksLikeUsageHelp("Usage: rac [mode] [command] [options]\n"));
    }

    [Fact]
    public void LooksLikeUsageHelp_LeadingBlankLine_StillDetected()
    {
        Assert.True(RacClient.LooksLikeUsageHelp("\r\n\r\nИспользование: rac [режим] [команда]\n"));
    }

    [Fact]
    public void LooksLikeUsageHelp_DataOutput_IsNotHelp()
    {
        // Табличный и key-value вывод данных справкой не считаются.
        Assert.False(RacClient.LooksLikeUsageHelp("cluster\tjob\tinfobase\tname\n"));
        Assert.False(RacClient.LooksLikeUsageHelp(
            "job                                 : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9\n" +
            "name                                : \"Обновление информационной базы\"\n"));
        Assert.False(RacClient.LooksLikeUsageHelp(null));
        Assert.False(RacClient.LooksLikeUsageHelp(string.Empty));
        Assert.False(RacClient.LooksLikeUsageHelp("   \n  "));
    }

    [Fact]
    public void LooksLikeUsageHelp_HelpMentionedInsideData_IsNotHelp()
    {
        // Справка распознаётся только по ПЕРВОЙ непустой строке: упоминание слова
        // «Использование» в данных (например, в имени задания) не сигнализирует.
        Assert.False(RacClient.LooksLikeUsageHelp(
            "job\tinfobase\tname\n" +
            "guid\tguid\tИспользование: rac — так называется задание\n"));
    }

    // ---------- LooksLikeUsageHelp (issue #324, 0.3.11: справка 8.5.4.1878 с баннером) ----------

    [Fact]
    public void LooksLikeUsageHelp_RussianHelpWithBanner_8_5_4_1878_IsDetected()
    {
        // Реальная структура вывода rac 8.5.4.1878 (issue #324, 0.3.11): сначала
        // многострочный баннер, «Использование:» — дальше первой строки.
        const string output =
            "1C:Enterprise 8.5 Remote Administrative Client Utility © 1C-Soft LLC 1996-2026\n" +
            "Утилита административной консоли 1С:Предприятия\n" +
            "\n" +
            "Использование: rac [режим] [команда] [параметры]\n" +
            "Пример: rac localhost:1540 cluster list\n";
        Assert.True(RacClient.LooksLikeUsageHelp(output));
    }

    [Fact]
    public void LooksLikeUsageHelp_EnglishHelpWithBanner_IsDetected()
    {
        const string output =
            "1C:Enterprise 8.5 Remote Administrative Client Utility © 1C-Soft LLC 1996-2026\n" +
            "\n" +
            "Usage: rac [mode] [command] [options]\n" +
            "Example: rac localhost:1540 cluster list\n";
        Assert.True(RacClient.LooksLikeUsageHelp(output));
    }

    [Fact]
    public void LooksLikeUsageHelp_CommandTemplateMarkerWithoutUsagePrefix_IsDetected()
    {
        // Шаблон команды «rac [режим]» в первых строках — признак справки, даже если
        // строка не начинается с «Использование:»/«Usage:».
        Assert.True(RacClient.LooksLikeUsageHelp(
            "Утилита 1С:Предприятия\n rac [режим] [команда]\n"));
    }

    [Fact]
    public void LooksLikeUsageHelp_BannerBeyondScanWindow_IsNotHelp()
    {
        // «Использование:» за пределами окна 15 строк — data-вывод с поздним
        // упоминанием (в имени задания) справкой не считается.
        var lines = new System.Collections.Generic.List<string> { "job-id : 1" };
        for (var i = 0; i < RacClient.UsageHelpScanLines; i++)
            lines.Add("name : «Использование режима» — имя задания");
        lines.Add("name : Использование: rac — так называется задание");
        Assert.False(RacClient.LooksLikeUsageHelp(string.Join("\n", lines)));
    }

    [Fact]
    public void LooksLikeUsageHelp_RealJobDataOutput_IsNotHelp()
    {
        // Реальный data-вывод «job list» (блоки «ключ : значение») — не справка.
        const string output =
            "job                                 : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9\n" +
            "name                                : \"Обновление информационной базы\"\n" +
            "start                               : 2026-10-07T10:00:00\n" +
            "state                               : StartInfobase\n";
        Assert.False(RacClient.LooksLikeUsageHelp(output));
    }

    // ---------- ClusterUpdateArgs (issue #324, C3: rac «cluster update») ----------

    [Fact]
    public void ClusterUpdateArgs_EmptyChanges_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RacClient.ClusterUpdateArgs(Guid.NewGuid(), new RacClusterUpdate()));
        Assert.Throws<ArgumentException>(() => RacClient.ClusterUpdateArgs(Guid.NewGuid(), null));
    }

    [Fact]
    public void ClusterUpdateArgs_OnlyChangedFields_AreIncluded()
    {
        // В командную строку попадают ТОЛЬКО изменённые свойства (null — не трогаем),
        // числа — в инвариантной записи.
        var clusterId = Guid.Parse("cbc95ef0-99c9-4b1a-909f-cff4c8de61d9");
        var args = RacClient.ClusterUpdateArgs(clusterId, new RacClusterUpdate
        {
            Name = "Локальный кластер",
            ExpirationTimeout = 120,
            SecurityLevel = 1
        });

        Assert.Equal(new[]
        {
            "cluster", "update", $"--cluster={clusterId}",
            "--name=Локальный кластер",
            "--expiration-timeout=120",
            "--security-level=1"
        }, args);
    }

    [Fact]
    public void ClusterUpdateArgs_AllKnownFields_AreSupported()
    {
        var clusterId = Guid.NewGuid();
        var args = RacClient.ClusterUpdateArgs(clusterId, new RacClusterUpdate
        {
            Name = "кл",
            ExpirationTimeout = 60,
            LifetimeLimit = 0,
            MaxMemorySize = 900000,
            MaxMemoryTimeLimit = 120,
            SecurityLevel = 0,
            PingPeriod = 1000,
            PingTimeout = 5000,
            MaxAuthAttempts = 10,
            AuthLockDuration = 900
        });

        Assert.Equal(new[]
        {
            "cluster", "update", $"--cluster={clusterId}",
            "--name=кл",
            "--expiration-timeout=60",
            "--lifetime-limit=0",
            "--max-memory-size=900000",
            "--max-memory-time-limit=120",
            "--security-level=0",
            "--ping-period=1000",
            "--ping-timeout=5000",
            "--max-auth-attempts=10",
            "--auth-lock-duration=900"
        }, args);
    }

    [Fact]
    public void ClusterUpdateArgs_NameWithSpaces_IsSingleToken()
    {
        // Значение с пробелами остаётся одним токеном (аргументы передаются через
        // ArgumentList без shell — как у остальной сборки аргументов rac).
        var args = RacClient.ClusterUpdateArgs(
            Guid.NewGuid(), new RacClusterUpdate { Name = "Имя с пробелами" });

        Assert.Contains("--name=Имя с пробелами", args);
    }

    // ---------- Фильтр по базе «--infobase=...» (issue #3, вкладка «Базы») ----------

    [Fact]
    public void SessionListArgs_WithoutInfobase_HasOnlyCluster()
    {
        var clusterId = Guid.NewGuid();

        var args = RacClient.SessionListArgs(clusterId);

        Assert.Equal(new[] { "session", "list", $"--cluster={clusterId}" }, args);
    }

    [Fact]
    public void SessionListArgs_WithInfobase_AddsInfobaseToken()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.Parse("cccccccc-1111-2222-3333-444455556666");

        var args = RacClient.SessionListArgs(clusterId, infobaseId);

        Assert.Equal(
            new[] { "session", "list", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            args);
    }

    [Fact]
    public void ConnectionListArgs_WithInfobase_AddsInfobaseToken()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        var args = RacClient.ConnectionListArgs(clusterId, infobaseId);

        Assert.Equal(
            new[] { "connection", "list", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            args);
    }

    [Fact]
    public void LockListArgs_WithInfobase_AddsInfobaseToken()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        var args = RacClient.LockListArgs(clusterId, infobaseId);

        Assert.Equal(
            new[] { "lock", "list", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            args);
    }

    // ---------- «infobase create» (issue #3, вкладка «Базы») ----------

    [Fact]
    public void InfobaseCreateArgs_ServerBase_AllFieldsAreIncluded()
    {
        var clusterId = Guid.NewGuid();
        var request = new RacInfobaseCreateRequest
        {
            Name = "Новая база",
            Description = "Описание базы",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "newbase",
            DbUser = "sa",
            DbPassword = "p@ss",
            Locale = "ru_RU",
            SecurityLevel = 1,
            CreateDb = true,
            ScheduledJobsDeny = false
        };

        var args = RacClient.InfobaseCreateArgs(clusterId, request);

        Assert.Equal(new[]
        {
            "infobase", "create", $"--cluster={clusterId}",
            "--name=Новая база",
            "--descr=Описание базы",
            "--dbms=MSSQLServer",
            "--db-server=sql-01",
            "--db-name=newbase",
            "--db-user=sa",
            "--db-pwd=p@ss",
            "--locale=ru_RU",
            "--security-level=1",
            "--create-database"
        }, args);
    }

    [Fact]
    public void InfobaseCreateArgs_OptionalFields_AreOmitted()
    {
        var clusterId = Guid.NewGuid();

        var args = RacClient.InfobaseCreateArgs(clusterId, new RacInfobaseCreateRequest
        {
            Name = "Минимум",
            Dbms = "PostgreSQL",
            DbServer = "pg-01",
            DbName = "mini"
        });

        Assert.DoesNotContain(args, a => a.StartsWith("--descr="));
        Assert.DoesNotContain(args, a => a.StartsWith("--db-user="));
        Assert.DoesNotContain(args, a => a.StartsWith("--db-pwd="));
        Assert.DoesNotContain(args, a => a.StartsWith("--locale="));
        Assert.DoesNotContain(args, a => a.StartsWith("--security-level="));
        Assert.DoesNotContain(args, a => a == "--create-database");
        Assert.DoesNotContain(args, a => a.StartsWith("--scheduled-jobs-deny"));
        Assert.DoesNotContain(args, a => a.StartsWith("--sessions-deny"));
    }

    [Fact]
    public void InfobaseCreateArgs_SessionsDeny_OnlyWhenOn()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «при переносе/копировании — копировать
        // флаги блокировок» — rac «infobase create --sessions-deny=on» передаётся ТОЛЬКО
        // для фактически выставленной блокировки начала сеансов источника; false/null —
        // ключ не добавляется (новая база и так без блокировки).
        var clusterId = Guid.NewGuid();

        var on = RacClient.InfobaseCreateArgs(clusterId, new RacInfobaseCreateRequest
        {
            Name = "Блокированная",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "blocked",
            ScheduledJobsDeny = true,
            SessionsDeny = true
        });
        Assert.Contains(on, a => a == "--scheduled-jobs-deny=on");
        Assert.Contains(on, a => a == "--sessions-deny=on");

        var off = RacClient.InfobaseCreateArgs(clusterId, new RacInfobaseCreateRequest
        {
            Name = "Свободная",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "free",
            SessionsDeny = false
        });
        Assert.DoesNotContain(off, a => a.StartsWith("--sessions-deny"));

        var unknown = RacClient.InfobaseCreateArgs(clusterId, new RacInfobaseCreateRequest
        {
            Name = "По умолчанию",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "default"
        });
        Assert.DoesNotContain(unknown, a => a.StartsWith("--sessions-deny"));
    }

    [Fact]
    public void InfobaseCreateArgs_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RacClient.InfobaseCreateArgs(Guid.NewGuid(), new RacInfobaseCreateRequest
            {
                Dbms = "MSSQLServer",
                DbServer = "sql-01",
                DbName = "newbase"
            }));
        Assert.Throws<ArgumentNullException>(() => RacClient.InfobaseCreateArgs(Guid.NewGuid(), null));
    }

    // ---------- «infobase update» (issue #3, кнопка «Изменить») ----------

    [Fact]
    public void InfobaseUpdateArgs_SummaryOnly_UsesRacUpdateKeys()
    {
        // issue #3 (7OH 17.10): «infobase update» не принимает --name/--locale/--security-level;
        // описание — «--descr», признак регламентных — «on|off».
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();
        var request = new RacInfobaseUpdateRequest
        {
            Name = "База",
            Description = "Новое описание",
            Dbms = "MSSQLServer",
            DbServer = "sql-02",
            DbName = "buh2",
            DbUser = "sa",
            DbPassword = "secret",
            Locale = "ru_RU",
            SecurityLevel = 2,
            ScheduledJobsDeny = true
        };

        var args = RacClient.InfobaseUpdateArgs(clusterId, infobaseId, request);

        Assert.Equal(new[]
        {
            "infobase", "update",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}",
            "--dbms=MSSQLServer",
            "--db-server=sql-02",
            "--db-name=buh2",
            "--db-user=sa",
            "--db-pwd=secret",
            "--descr=Новое описание",
            "--scheduled-jobs-deny=on"
        }, args);
    }

    [Fact]
    public void InfobaseUpdateArgs_BlockScheduledJobs_SwitchesOnOff()
    {
        // issue #69, комментарий 7OH от 2026-10-26 («Установить блокировку регламентных
        // заданий»): для удаления занятой базы VM вызывает «infobase update» с
        // ScheduledJobsDeny=true → rac-аргумент «--scheduled-jobs-deny=on»; null-свойства
        // (полная блокировка сеансов и пр.) не добавляют лишних ключей.
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();
        var onArgs = RacClient.InfobaseUpdateArgs(clusterId, infobaseId, new RacInfobaseUpdateRequest
        {
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "buho",
            ScheduledJobsDeny = true
        });

        Assert.Equal(new[]
        {
            "infobase", "update",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}",
            "--dbms=MSSQLServer",
            "--db-server=sql-01",
            "--db-name=buho",
            "--descr=",
            "--scheduled-jobs-deny=on"
        }, onArgs);

        var offArgs = RacClient.InfobaseUpdateArgs(clusterId, infobaseId, new RacInfobaseUpdateRequest
        {
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "buho",
            ScheduledJobsDeny = false
        });
        Assert.Contains(offArgs, a => a == "--scheduled-jobs-deny=off");
        Assert.DoesNotContain(offArgs, a => a == "--scheduled-jobs-deny=on");
    }

    [Fact]
    public void InfobaseUpdateArgs_FullProperties_AndInfobaseCredentials()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();
        var request = new RacInfobaseUpdateRequest
        {
            Dbms = "PostgreSQL",
            DbServer = "pg",
            DbName = "erp",
            InfobaseUser = "Администратор",
            InfobasePassword = "123",
            ScheduledJobsDeny = false,
            SessionsDeny = true,
            DeniedFrom = new DateTime(2026, 10, 17, 22, 0, 0),
            DeniedTo = new DateTime(2026, 10, 18, 6, 30, 0),
            DeniedMessage = "Обновление",
            PermissionCode = "777",
            DeniedParameter = "",
            LicenseDistributionAllowed = false,
            ExternalSessionManagerConnectionString = "",
            ExternalSessionManagerRequired = false,
            SecurityProfileName = "",
            SafeModeSecurityProfileName = "safe"
        };

        var args = RacClient.InfobaseUpdateArgs(clusterId, infobaseId, request);

        Assert.Equal(new[]
        {
            "infobase", "update",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}",
            "--infobase-user=Администратор",
            "--infobase-pwd=123",
            "--dbms=PostgreSQL",
            "--db-server=pg",
            "--db-name=erp",
            "--descr=",
            "--scheduled-jobs-deny=off",
            "--sessions-deny=on",
            "--denied-from=2026-10-17T22:00:00",
            "--denied-to=2026-10-18T06:30:00",
            "--denied-message=Обновление",
            "--permission-code=777",
            "--denied-parameter=",
            "--license-distribution=deny",
            "--external-session-manager-connection-string=",
            "--external-session-manager-required=no",
            "--security-profile-name=",
            "--safe-mode-security-profile-name=safe"
        }, args);
    }

    [Fact]
    public void InfobaseUpdateArgs_MissingDbFields_AreOmittedInsteadOfThrowing()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «infobase update» с пустым «--dbms=»
        // раньше давал ArgumentException из сборщика аргументов → блокировка регламентных
        // заданий не ставилась («удаляет, но не каждый раз»). Пустые параметры СУБД теперь
        // НЕ попадают в аргументы: rac сам вернёт понятную ошибку, а VM догружает параметры
        // через «infobase info» перед запросом блокировки.
        var args = RacClient.InfobaseUpdateArgs(Guid.NewGuid(), Guid.NewGuid(), new RacInfobaseUpdateRequest
        {
            // Dbms модели по умолчанию равен "MSSQLServer" — явно обнуляем, как при
            // отсутствии параметров СУБД в summary удаляемой базы (issue #69).
            Dbms = string.Empty,
            DbServer = string.Empty,
            DbName = string.Empty,
            ScheduledJobsDeny = true
        });

        Assert.DoesNotContain(args, a => a.StartsWith("--dbms=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("--db-server=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("--db-name=", StringComparison.Ordinal));
        Assert.Contains(args, a => a == "--scheduled-jobs-deny=on");

        Assert.Throws<ArgumentNullException>(() =>
            RacClient.InfobaseUpdateArgs(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    [Fact]
    public void InfobaseInfoArgs_WithAndWithoutCredentials()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(new[] { "infobase", "info", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            RacClient.InfobaseInfoArgs(clusterId, infobaseId, null, null));
        Assert.Equal(new[]
            {
                "infobase", "info", $"--cluster={clusterId}", $"--infobase={infobaseId}",
                "--infobase-user=admin", "--infobase-pwd=p"
            },
            RacClient.InfobaseInfoArgs(clusterId, infobaseId, "admin", "p"));
    }

    [Fact]
    public void InfobaseInfoArgsByFormat_AllClusterFormats()
    {
        // Issue #79 (v1.0.1.30): «infobase info» выполняется с перебором форматов
        // передачи идентификатора кластера (как list-команды, issue #25) — форматы
        // 0/1/2 строят корректные аргументы, креды ИБ сохраняются.
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(
            new[] { "infobase", "info", $"--cluster={clusterId}", $"--infobase={infobaseId}", "--infobase-user=u", "--infobase-pwd=p" },
            RacClient.InfobaseInfoArgsByFormat(0, clusterId, infobaseId, "u", "p"));
        Assert.Equal(
            new[] { "infobase", "info", "--cluster", clusterId.ToString(), $"--infobase={infobaseId}" },
            RacClient.InfobaseInfoArgsByFormat(1, clusterId, infobaseId, null, null));
        Assert.Equal(
            new[] { "infobase", "info", clusterId.ToString(), $"--infobase={infobaseId}" },
            RacClient.InfobaseInfoArgsByFormat(2, clusterId, infobaseId, null, null));
    }

    [Fact]
    public void LooksLikeArgumentParseError_MatchesRacParseErrors()
    {
        // Issue #92 (v1.0.1.30): rac 8.5.4 на «session terminate --infobase-user=…»
        // отвечает «Ошибка разбора параметра» — по этому признаку выполняется ретрай
        // без кредов ИБ; ошибки прав/отмены признаком разбора НЕ являются.
        Assert.True(RacClient.LooksLikeArgumentParseError(
            "Ошибка разбора параметра: --infobase-user=Администратор"));
        Assert.True(RacClient.LooksLikeArgumentParseError("Invalid option: --infobase-user"));
        Assert.False(RacClient.LooksLikeArgumentParseError("Недостаточно прав пользователя на информационную базу"));
        Assert.False(RacClient.LooksLikeArgumentParseError(null));
        Assert.False(RacClient.LooksLikeArgumentParseError(string.Empty));
    }

    [Fact]
    public void InfobaseDropArgs_FromClusterOnly_NoDropDatabaseFlag()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(
            new[] { "infobase", "drop", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            RacClient.InfobaseDropArgs(clusterId, infobaseId, dropDatabase: false));
    }

    [Fact]
    public void InfobaseDropArgs_WithDatabase_IncludesDropDatabaseFlag()
    {
        // Диалог удаления с выбором (issue #3, 7OH от 2026-10-17): вариант
        // «Удалить с удалением на сервере СУБД» добавляет к команде «--drop-database».
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(
            new[]
            {
                "infobase", "drop", $"--cluster={clusterId}", $"--infobase={infobaseId}",
                "--drop-database"
            },
            RacClient.InfobaseDropArgs(clusterId, infobaseId, dropDatabase: true));
    }

    [Fact]
    public void InfobaseDropArgs_WithCredentials_AddsInfobaseUserAndPassword()
    {
        // Issue #69: повторное удаление защищённой базы с учётными данными администратора
        // ИБ — rac-команда получает «--infobase-user=...» и «--infobase-pwd=...»
        // (по образцу InfobaseInfoArgs), флаг «--drop-database» не теряется.
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(
            new[]
            {
                "infobase", "drop", $"--cluster={clusterId}", $"--infobase={infobaseId}",
                "--infobase-user=admin", "--infobase-pwd=secret", "--drop-database"
            },
            RacClient.InfobaseDropArgs(clusterId, infobaseId, dropDatabase: true, "admin", "secret"));
    }

    [Fact]
    public void InfobaseDropArgs_WithoutCredentials_NoUserOrPasswordArguments()
    {
        // Регресс (issue #69): пустые учётные данные не добавляют к команде удаления
        // аргументов администратора ИБ — первая попытка выполняется как раньше.
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();

        Assert.Equal(
            new[] { "infobase", "drop", $"--cluster={clusterId}", $"--infobase={infobaseId}" },
            RacClient.InfobaseDropArgs(clusterId, infobaseId, dropDatabase: false, null, null));
        Assert.Equal(
            new[] { "infobase", "drop", $"--cluster={clusterId}", $"--infobase={infobaseId}", "--drop-database" },
            RacClient.InfobaseDropArgs(clusterId, infobaseId, dropDatabase: true, string.Empty, string.Empty));
    }

    [Fact]
    public void MaskRacPassword_HidesDropInfobasePassword()
    {
        // Issue #69: пароль администратора ИБ не должен попадать в журнал rac-команды
        // «infobase drop» (маскируется, как пароли info/update).
        var masked = SensitiveDataMasker.MaskRacPassword(
            "infobase drop --cluster=1111 --infobase=2222 --infobase-user=adm --infobase-pwd=topsecret");

        Assert.DoesNotContain("topsecret", masked);
        Assert.Contains("--infobase-user=adm", masked);
        Assert.Contains("--infobase-pwd=***", masked);
    }

    [Fact]
    public void MaskRacPassword_HidesDbAndInfobasePasswords()
    {
        var masked = SensitiveDataMasker.MaskRacPassword(
            "infobase update --db-pwd=secret --infobase-user=adm --infobase-pwd=top --password=x");

        Assert.DoesNotContain("secret", masked);
        Assert.DoesNotContain("top", masked);
        Assert.Contains("--infobase-user=adm", masked);
    }

    /// <summary>Временный каталог для изоляции проверки «файл кэша не создаётся» (issue #39).</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_rac_tests_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}