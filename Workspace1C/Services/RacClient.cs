using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Клиент утилиты rac (Remote Administration Client) — сервисный слой встроенного монитора
/// серверов 1С (цикл 0.3.9.123–0.3.9.126). Чистый сервис — без UI-зависимостей.
/// Команды выполняются прямым запуском rac (без shell, через ArgumentList) с захватом
/// stdout/stderr, параллельным чтением и таймаутом. Кодировка вывода определяется
/// автоматически: UTF-8, а на Windows — также OEM (cp866) / ANSI (cp1251) кодовые страницы
/// (issue #324: «в терминале rac работает, монитор вывод не распознаёт»). Пароль
/// администратора кластера передаётся аргументом <c>--password</c>, но НЕ пишется
/// в журнал (маскируется <see cref="SensitiveDataMasker.MaskRacPassword"/>).
/// </summary>
public sealed class RacClient : IRacClient
{
    /// <summary>Строгий UTF-8 (невалидные байты — ошибка декодирования).</summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Кодовые страницы Windows для fallback-декодирования вывода rac.</summary>
    private static readonly Encoding[] WindowsFallbackEncodings;

    static RacClient()
    {
        // Кодовые страницы 866/1251 доступны и на Linux (.NET Core) только после
        // регистрации провайдера CodePagesEncodingProvider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WindowsFallbackEncodings = new[] { Encoding.GetEncoding(866), Encoding.GetEncoding(1251) };
    }

    /// <summary>Таймаут выполнения одной rac-команды (30 секунд).</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Источник rac-команд для диагностики CM_RAC-лога (issue #90): «MONITOR» — окно
    /// «Серверы 1С» (<see cref="ViewModels.ServerMonitorViewModel.MonitorLogSourceTag"/>),
    /// «ALERTS» — фоновые проверки <see cref="ServerAlertsService"/>. AsyncLocal — значение
    /// течёт по async-цепочке и не смешивается между параллельными циклами.
    /// </summary>
    public static readonly AsyncLocal<string?> LogSource = new();

    /// <summary>Метка источника для строк CM_RAC: «RAC [MONITOR]: …» / «RAC: …» (issue #90).</summary>
    private static string SourcePrefix =>
        LogSource.Value is { Length: > 0 } tag ? $" [{tag}]" : string.Empty;

    private readonly IAppLogger _logger;

    /// <summary>
    /// Запомненный рабочий формат передачи идентификатора кластера в list-командах rac
    /// по ключу подключения (адрес:порт|user|clusterId|команда, issue #324/#25):
    /// rac 8.5.x отклоняет <c>--cluster=<uuid></c> (код -1) и, по логам 2026-10-07/08,
    /// также <c>--cluster <uuid></c> двумя токенами — добавлен формат 2 (позиционный
    /// <c><uuid></c>). Без кэша каждая загрузка данных кластера запускала rac
    /// дважды (первая попытка всегда падала), что заметно удлиняло
    /// подключение/автообновление. Значение — индекс формата: 0 = "--cluster=<uuid>",
    /// 1 = "--cluster <uuid>", 2 = "<uuid>" (см. <see cref="ClusterArgs"/>).
    /// Ключ дополнен ИМЕНЕМ КОМАНДЫ (issue #25): у одного сервера форматы разных
    /// команд могут отличаться, поэтому кэш «job list» и «infobase summary list»
    /// не должен конфликтовать. Кэш живёт ТОЛЬКО в памяти процесса (issue #39):
    /// на диск он не пишется, файл rac_joblist_formats.json больше не создаётся.
    /// После перезапуска приложения формат перепробуется заново — для rac 8.3/8.4
    /// это успех с первой попытки, для rac 8.5.x одна падающая попытка при первой
    /// в сессии загрузке данных допустима (вкладка заданий rac удалена, issue #39).
    /// </summary>
    private readonly Dictionary<string, int> _clusterFormats;

    /// <summary>
    /// Кэш последнего вывода «cluster list» по ключу подключения (issue #324):
    /// вывод «cluster info --cluster=<uuid>» на новых rac ПОСТРОЧНО совпадает с блоком
    /// соответствующего кластера в «cluster list» (в логе 8.5.4.1878 — по 1060 символов),
    /// поэтому info переиспользуется из кэша в течение TTL вместо запуска rac.
    /// Каждый запуск rac на Windows стоит ~1.1 с (старт процесса + comcntr) — на цикл
    /// из 8 вызовов экономится один.
    /// </summary>
    private readonly Dictionary<string, (string Output, DateTime Timestamp)> _clusterListCache = new();

    /// <summary>Срок годности кэша вывода «cluster list» (issue #324).</summary>
    internal static readonly TimeSpan ClusterListCacheTtl = TimeSpan.FromSeconds(30);

    public RacClient(IAppLogger logger)
    {
        _logger = logger;
        // Кэш форматов — только в памяти (issue #39): с диска не загружается,
        // файл rac_joblist_formats.json не читается и не создаётся.
        _clusterFormats = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacCluster>> GetClustersAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "cluster", "list")
            .ConfigureAwait(false);

        // rac завершился успешно (exit=0), но вывода нет: это либо пустой список кластеров,
        // либо подключение ушло на RAS/неправильный порт и «рабочего» ответа нет. Предупреждение
        // в журнале позволяет отличить такой случай от ошибки подключения (issue #324).
        if (string.IsNullOrWhiteSpace(output))
        {
            _logger.Warn(
                "RAC: cluster list завершился с кодом 0, но вернул пустой вывод — " +
                "кластеры не найдены. Проверьте порт: агент ragent (1540) или RAS (1545), " +
                "а не порт кластера (1541).");
        }
        else
        {
            // Кэшируем вывод для переиспользования в GetClusterInfoAsync (issue #324):
            // экономит один запуск rac (~1.1 с) на каждый цикл обновления данных.
            _clusterListCache[ConnectionKey(parameters)] = (output, DateTime.UtcNow);
        }

        var clusters = RacOutputParser.ToClusters(output);
        // Диагностика «в списке ключ вместо имени» (issue #324): в журнал попадают
        // распознанные подписи кластеров — сразу видно, что вернул парсер.
        if (clusters.Count > 0)
        {
            // issue #80 «Настройка логирования»: события RAC — направление CM_RAC
            // (INFO пишется при включённых категории и общем CM_ON).
            Log.Info(LogCategory.Rac, "RAC: cluster list — распознано кластеров: " + string.Join("; ",
                clusters.Select(c =>
                    $"«{(string.IsNullOrWhiteSpace(c.Name) ? (string.IsNullOrWhiteSpace(c.Host) ? "?" : c.Host) : c.Name)}» (порт {c.Port})")));
        }

        // issue #324: непустой вывод без единого кластера — так же подозрителен, как
        // и для остальных list-команд (WARN + понятная ошибка вместо «кластеров: 0»).
        EnsureParsedOrThrow(output, clusters.Count, "cluster list", _logger);

        return clusters;
    }

    /// <inheritdoc />
    public async Task<RacClusterInfo?> GetClusterInfoAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        // Переиспользование кэша cluster list (issue #324): блок кластера из свежего
        // «cluster list» идентичен выводу «cluster info» — rac не запускается второй раз.
        if (_clusterListCache.TryGetValue(ConnectionKey(parameters), out var cached) &&
            DateTime.UtcNow - cached.Timestamp <= ClusterListCacheTtl)
        {
            var block = ExtractClusterBlock(cached.Output, clusterId);
            if (block is not null)
            {
                // issue #80: события RAC — направление CM_RAC (INFO при включённых категории и CM_ON).
                Log.Info(LogCategory.Rac, "RAC: cluster info — переиспользован кэш cluster list (экономия запуска rac, issue #324).");
                return RacOutputParser.ToClusterInfo(block);
            }
        }

        // issue #25: rac 8.5.x может отклонять «--cluster=<uuid>» (код -1) и в команде
        // «cluster info» (как в «job list»/«infobase summary list») — перебираем форматы
        // передачи идентификатора кластера (0/1/2) с запоминанием рабочего формата по
        // ключу подключения + имени команды (кэш в памяти, issue #39). Для rac 8.3/8.4
        // формат 0 (--cluster=<uuid>) успешен с первой попытки — поведение не меняется.
        var formatKey = ClusterFormatKey(parameters, clusterId, "cluster info");
        var output = await RunListCommandWithFormatFallbackAsync(
                parameters, cancellationToken, "cluster info", formatKey,
                index => ClusterInfoArgs(index, clusterId))
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(output) ? null : RacOutputParser.ToClusterInfo(output);
    }

    /// <summary>
    /// Извлекает из вывода «cluster list» (формат блоков «ключ : значение») текст блока
    /// кластера с идентификатором <paramref name="clusterId"/> — он построчно совпадает
    /// с выводом «cluster info --cluster=<uuid>» (issue #324). Возвращает null, если
    /// вывод не в формате блоков или кластер не найден. Internal — для юнит-тестов.
    /// </summary>
    internal static string? ExtractClusterBlock(string? clusterListOutput, Guid clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterListOutput) || clusterId == Guid.Empty)
            return null;

        var builder = new StringBuilder();
        var inTargetBlock = false;

        foreach (var rawLine in clusterListOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                // Пустая строка внутри целевого блока сохраняется; после блока — конец.
                if (inTargetBlock)
                    builder.AppendLine(line);
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon > 0 &&
                line.Substring(0, colon).Trim().Equals("cluster", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring(colon + 1).Trim();
                var isTarget = Guid.TryParse(value, out var parsed) && parsed == clusterId;
                if (inTargetBlock)
                    break;              // начался следующий кластер — блок завершён
                if (!isTarget)
                    continue;           // чужой блок — пропускаем строки до своего
                inTargetBlock = true;
            }

            if (inTargetBlock)
                builder.AppendLine(line);
        }

        return inTargetBlock && builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>Ключ подключения без кластера: точка подключения и учётная запись
    /// (без пароля — секреты не попадают в ключи словарей, issue #324).</summary>
    private static string ConnectionKey(RacConnectionParams parameters)
        => $"{parameters.Address}:{parameters.Port}|{parameters.User}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "process", "list",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        var processes = RacOutputParser.ToProcesses(output);
        EnsureParsedOrThrow(output, processes.Count, "process list", _logger);
        return processes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, SessionListArgs(clusterId, infobaseId))
            .ConfigureAwait(false);
        var sessions = RacOutputParser.ToSessions(output);
        EnsureParsedOrThrow(output, sessions.Count, "session list", _logger);
        return sessions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacSessionInfo>> GetLicensedSessionsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        // п.5 «Мониторинг лицензий» (1.0.0.87): команда «session list --licenses».
        // Данные кластера всегда запрашиваются целиком (параметр «--infobase=...» на
        // платформе пользователя не работает и вызывает зависание — см. комментарий
        // в LoadClusterDataAsync), поэтому фильтр по базе здесь не применяется.
        var output = await RunAsync(parameters, cancellationToken, SessionLicensesArgs(clusterId))
            .ConfigureAwait(false);
        var sessions = RacOutputParser.ToSessions(output);
        EnsureParsedOrThrow(output, sessions.Count, "session list --licenses", _logger);
        return sessions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, ConnectionListArgs(clusterId, infobaseId))
            .ConfigureAwait(false);
        var connections = RacOutputParser.ToConnections(output);
        EnsureParsedOrThrow(output, connections.Count, "connection list", _logger);
        return connections;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
        RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
        CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, LockListArgs(clusterId, infobaseId))
            .ConfigureAwait(false);
        var locks = RacOutputParser.ToLocks(output);
        EnsureParsedOrThrow(output, locks.Count, "lock list", _logger);
        return locks;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        // issue #25: rac 8.5.x может отклонять «--cluster=<uuid>» (код -1) не только
        // в «job list» (issue #324), но и в «infobase summary list» — перебираем
        // форматы передачи идентификатора кластера (0/1/2) с запоминанием рабочего
        // формата по ключу подключения + имени команды (кэш в памяти, issue #39).
        // Для rac 8.3/8.4 формат 0 (--cluster=<uuid>) успешен с первой попытки —
        // поведение не меняется.
        var formatKey = ClusterFormatKey(parameters, clusterId, "infobase summary list");
        var output = await RunListCommandWithFormatFallbackAsync(
                parameters, cancellationToken, "infobase summary list", formatKey,
                index => InfobaseSummaryListArgs(index, clusterId))
            .ConfigureAwait(false);
        var infobases = RacOutputParser.ToInfobaseSummaries(output);
        EnsureParsedOrThrow(output, infobases.Count, "infobase summary list", _logger);
        return infobases;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        // issue #324: rac 8.5.4.1878 отклоняет «job list --cluster=<uuid>» (код -1,
        // «Ошибка разбора параметра: --cluster=…») и «--cluster <uuid>» двумя токенами:
        // в логах от 2026-10-07/08 ОБЕ попытки падают (успех в логе — параллельный
        // «process list», а не job list). Перебор форматов вынесен в общий helper
        // (issue #25): 0 — --cluster=<uuid>, 1 — --cluster <uuid>, 2 — позиционный
        // <uuid> без имени параметра. После первого успеха формат запоминается по
        // ключу подключения + имени команды — повторные загрузки запускают rac один раз.
        var formatKey = ClusterFormatKey(parameters, clusterId, "job list");
        var output = await RunListCommandWithFormatFallbackAsync(
                parameters, cancellationToken, "job list", formatKey,
                index => JobListArgs(index, clusterId))
            .ConfigureAwait(false);

        var jobs = RacOutputParser.ToJobs(output);
        // Защита от новых форм справки (issue #324, 0.3.11): непустой stdout с 0 записей,
        // выглядящий как текст справки, — это НЕуспех: формат не должен оставаться
        // закэшированным (иначе WARN «вывод rac не распознан» повторяется на каждом опросе).
        if (jobs.Count == 0 && LooksLikeUsageHelp(output))
        {
            _logger.Warn(
                "RAC: job list — rac вернул справку об использовании вместо данных " +
                $"(exit=0, stdout={output.Length} симв., 0 записей); кэш формата сброшен.");
            _clusterFormats.Remove(formatKey);
        }
        EnsureParsedOrThrow(output, jobs.Count, "job list", _logger);
        return jobs;
    }

    /// <summary>Число первых строк вывода rac, сканируемых на признаки справки (issue
    /// #324, 0.3.11: у 8.5.4.1878 перед «Использование:» стоит многострочный баннер
    /// «1C:Enterprise 8.5 Remote Administrative Client Utility …», поэтому проверка
    /// только первой строки не распознавала справку — формат ошибочно кэшировался).</summary>
    internal const int UsageHelpScanLines = 15;

    /// <summary>
    /// Признак того, что rac вместо данных вернул СПРАВКУ об использовании команды
    /// (issue #324, комментарий 7OH от 2026-10-09: «job list <uuid>» на 8.5.4.1878
    /// завершается с кодом 0, но stdout — текст «1C:Enterprise 8.5 Remote Administrative
    /// Client Utility …» + «Использование: rac [режим] [команда] …», 0 записей).
    /// <para>
    /// С 0.3.11 сканируются ПЕРВЫЕ <see cref="UsageHelpScanLines"/> строк вывода (не
    /// только первая): справка распознаётся по префиксу строки «Использование:»
    /// (русская локаль) / «Usage:» (английская) или по подстроке «rac [» (шаблон
    /// команды в справке) в пределах окна строк. Data-вывод (таблица/блоки
    /// «ключ : значение») такими признаками в первых строках не обладает.
    /// Internal — для юнит-тестов без запуска процесса rac.
    /// </para>
    /// </summary>
    internal static bool LooksLikeUsageHelp(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        var scanned = 0;
        foreach (var rawLine in output.Split('\n'))
        {
            if (scanned >= UsageHelpScanLines)
                break;

            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;
            scanned++;

            if (line.StartsWith("Использование:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                return true;

            // Шаблон команды в справке: «rac [режим] [команда] …» — встречается
            // и в русской, и в английской локали; в data-выводе «rac [» не бывает.
            if (line.Contains("rac [", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Число известных форматов передачи идентификатора кластера в list-командах
    /// rac (issue #25): 0 — «--cluster=<uuid>», 1 — «--cluster <uuid>», 2 — позиционный
    /// «<uuid>».</summary>
    internal const int ClusterFormatCount = 3;

    /// <summary>
    /// Аргументы list-команды rac с идентификатором кластера по индексу формата
    /// (issue #25): 0 — <c>--cluster=<uuid></c> (стандарт), 1 — <c>--cluster <uuid></c>
    /// двумя токенами, 2 — позиционный <c><uuid></c> (без имени параметра).
    /// <paramref name="commandPrefix"/> — слова команды («job list», «infobase summary
    /// list» и т.п.). Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] ClusterArgs(
        IReadOnlyList<string> commandPrefix, int formatIndex, Guid clusterId)
    {
        var args = new List<string>(commandPrefix.Count + 2);
        args.AddRange(commandPrefix);
        switch (formatIndex)
        {
            case 1:
                args.Add("--cluster");
                args.Add(clusterId.ToString());
                break;
            case 2:
                args.Add(clusterId.ToString());
                break;
            default:
                args.Add($"--cluster={clusterId}");
                break;
        }
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «job list» по индексу формата (issue #324): обёртка над
    /// <see cref="ClusterArgs"/>. Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] JobListArgs(int formatIndex, Guid clusterId) =>
        ClusterArgs(new[] { "job", "list" }, formatIndex, clusterId);

    /// <summary>
    /// Аргументы команды «infobase summary list» по индексу формата передачи
    /// идентификатора кластера (issue #25): обёртка над <see cref="ClusterArgs"/>.
    /// Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] InfobaseSummaryListArgs(int formatIndex, Guid clusterId) =>
        ClusterArgs(new[] { "infobase", "summary", "list" }, formatIndex, clusterId);

    /// <summary>
    /// Аргументы команды «cluster info» по индексу формата передачи идентификатора
    /// кластера (issue #25): обёртка над <see cref="ClusterArgs"/>. rac 8.5.x может
    /// отклонять «--cluster=<uuid>» и в этой команде — перебор форматов нужен наравне
    /// с «job list»/«infobase summary list». Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] ClusterInfoArgs(int formatIndex, Guid clusterId) =>
        ClusterArgs(new[] { "cluster", "info" }, formatIndex, clusterId);

    /// <summary>Человекочитаемое имя формата передачи идентификатора кластера для
    /// журнала (issue #25).</summary>
    internal static string ClusterFormatName(int formatIndex) => formatIndex switch
    {
        1 => "--cluster <uuid>",
        2 => "<uuid>",
        _ => "--cluster=<uuid>"
    };

    /// <summary>
    /// Ключ запомненного формата list-команды (issue #25): точка подключения, учётная
    /// запись, кластер и ИМЯ КОМАНДЫ — кэш «job list» и «infobase summary list» не
    /// конфликтует (у одного сервера форматы разных команд могут отличаться). Без
    /// пароля — пароль в ключе не хранится (секреты не должны попадать в строковые
    /// ключи словаря и логи). Internal — для юнит-тестов.
    /// </summary>
    internal static string ClusterFormatKey(
        RacConnectionParams parameters, Guid clusterId, string commandName)
        => $"{ConnectionKey(parameters)}|{clusterId}|{commandName}";

    /// <summary>
    /// Выполняет list-команду rac с перебором форматов передачи идентификатора
    /// кластера (issue #25): rac 8.5.x может отклонять «--cluster=<uuid>» (код -1)
    /// или возвращать справку вместо данных. Форматы: 0 — «--cluster=<uuid>»,
    /// 1 — «--cluster <uuid>», 2 — позиционный «<uuid>». Первый успешный формат
    /// запоминается в <paramref name="formats"/> по <paramref name="formatKey"/>
    /// (кэш в памяти, на диск не пишется — issue #39); если все форматы
    /// отклонены — ключ удаляется и бросается <see cref="RacClientException"/>
    /// со сводкой попыток. Internal — для юнит-тестов без запуска процесса rac.
    /// </summary>
    internal static async Task<string> RunListCommandWithFormatFallbackAsync(
        Func<int, Task<string>> runAttempt,
        Func<string?, bool> isUsageHelp,
        Dictionary<string, int> formats,
        string formatKey,
        string commandName,
        IAppLogger logger)
    {
        var startIndex = formats.TryGetValue(formatKey, out var known) ? known : 0;

        string? output = null;
        var failures = new List<string>();
        var usedFallback = false;
        var successFormat = -1;

        // До трёх попыток: сначала известный/первый формат, при неуспехе — остальные.
        for (var attempt = 0; attempt < ClusterFormatCount && output is null; attempt++)
        {
            var index = (startIndex + attempt) % ClusterFormatCount;
            try
            {
                var attemptOutput = await runAttempt(index).ConfigureAwait(false);

                // issue #324 (комментарий 7OH от 2026-10-09): rac 8.5.4.1878 может
                // завершить команду с кодом 0, но вернуть СПРАВКУ об использовании
                // («Использование: rac [режим] [команда] …») — данных нет. Такой вывод
                // считается НЕуспехом формата: формат не кэшируется, запрос повторяется
                // с альтернативным синтаксисом.
                if (isUsageHelp(attemptOutput))
                {
                    failures.Add(
                        $"формат '{ClusterFormatName(index)}': rac вернул справку об использовании " +
                        $"({attemptOutput.Length} симв.) вместо данных");
                    continue;
                }

                output = attemptOutput;
                usedFallback = attempt > 0;
                successFormat = index;
                // Запоминаем рабочий формат (в памяти; на диск сохраняет вызывающий код).
                // При смене версии платформы неудачная попытка ниже удалит ключ, и на
                // следующем вызове форматы перепробуются заново.
                formats[formatKey] = index;
            }
            catch (RacClientException ex)
            {
                failures.Add($"формат '{ClusterFormatName(index)}': {ex.Message}");
            }
        }

        if (output is null)
        {
            // Все форматы неуспешны — один сводный [WARN] в журнал (issue #324: раньше
            // WARN писался и при успехе одной из попыток, сбивая с толку).
            logger.Warn($"RAC: {commandName} — все форматы отклонены rac: " + string.Join("; ", failures));
            // Запись формата устарела (сменилась версия платформы) — удаляем из кэша,
            // чтобы на следующем вызове форматы перепробовались заново.
            formats.Remove(formatKey);
            throw new RacClientException(
                $"{commandName}: rac отклонил все известные форматы команды (" +
                string.Join("; ", failures) +
                $"). Пришлите, пожалуйста, вывод «rac <адрес:порт> {commandName} --help» вашей платформы.");
        }

        if (usedFallback)
        {
            // issue #80: события RAC — направление CM_RAC (INFO при включённых категории и CM_ON).
            Log.Info(LogCategory.Rac,
                $"RAC: {commandName} — стандартный формат '--cluster=<uuid>' не поддержан rac, " +
                $"применён формат '{ClusterFormatName(successFormat)}' (успешно).");
        }

        return output;
    }

    /// <summary>
    /// Экземплярная обёртка над <see cref="RunListCommandWithFormatFallbackAsync"/>:
    /// запускает rac через <see cref="RunAsync"/> и ведёт карту форматов в памяти
    /// (на диск она не пишется, issue #39).
    /// </summary>
    private async Task<string> RunListCommandWithFormatFallbackAsync(
        RacConnectionParams parameters,
        CancellationToken cancellationToken,
        string commandName,
        string formatKey,
        Func<int, string[]> argsBuilder)
    {
        return await RunListCommandWithFormatFallbackAsync(
                index => RunAsync(parameters, cancellationToken, argsBuilder(index)),
                RacClient.LooksLikeUsageHelp,
                _clusterFormats, formatKey, commandName, _logger)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Аргументы команды «session list» (issue #3, вкладка «Базы»): при заданном
    /// идентификаторе базы добавляется параметр «--infobase=<uuid>» — rac возвращает
    /// сеансы только выбранной информационной базы. Internal — для юнит-тестов.
    /// </summary>
    internal static string[] SessionListArgs(Guid clusterId, Guid? infobaseId = null) =>
        BuildListArgs("session", "list", clusterId, infobaseId);

    /// <summary>
    /// Аргументы команды «session list --licenses» (п.5 «Мониторинг лицензий»,
    /// выпуск 1.0.0.87): та же команда «session list --cluster=...» + параметр
    /// «--licenses» — rac дополняет вывод колонками/свойствами лицензий
    /// (lic-type, max-licenses, licenses). Internal — для юнит-тестов.
    /// </summary>
    internal static string[] SessionLicensesArgs(Guid clusterId)
    {
        var args = new List<string>(5) { "session", "list", $"--cluster={clusterId}", "--licenses" };
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «connection list» (issue #3): фильтр по базе через
    /// «--infobase=<uuid>». Internal — для юнит-тестов.
    /// </summary>
    internal static string[] ConnectionListArgs(Guid clusterId, Guid? infobaseId = null) =>
        BuildListArgs("connection", "list", clusterId, infobaseId);

    /// <summary>
    /// Аргументы команды «lock list» (issue #3): фильтр по базе через
    /// «--infobase=<uuid>». Internal — для юнит-тестов.
    /// </summary>
    internal static string[] LockListArgs(Guid clusterId, Guid? infobaseId = null) =>
        BuildListArgs("lock", "list", clusterId, infobaseId);

    /// <summary>Общий построитель аргументов list-команды кластера с фильтром по базе.</summary>
    private static string[] BuildListArgs(string command, string subcommand, Guid clusterId, Guid? infobaseId)
    {
        var args = new List<string>(4) { command, subcommand, $"--cluster={clusterId}" };
        if (infobaseId is Guid id && id != Guid.Empty)
            args.Add($"--infobase={id}");
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «infobase create» (issue #3, вкладка «Базы»):
    /// <c>infobase create --cluster=<uuid> --name=... --dbms=... --db-server=...
    /// --db-name=...</c> + необязательные параметры. Пароль СУБД остаётся одним
    /// токеном (ArgumentList без shell); в журнале он маскируется. Internal —
    /// для юнит-тестов сборки аргументов без запуска rac.
    /// </summary>
    internal static string[] InfobaseCreateArgs(Guid clusterId, RacInfobaseCreateRequest? request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Не указано имя информационной базы.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Dbms))
            throw new ArgumentException("Не указан тип СУБД.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DbServer))
            throw new ArgumentException("Не указан сервер СУБД.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DbName))
            throw new ArgumentException("Не указано имя базы данных.", nameof(request));

        var args = new List<string>(16) { "infobase", "create", $"--cluster={clusterId}" };
        args.Add($"--name={request.Name}");
        // Ключи по справке rac «infobase create» (issue #3): описание — «--descr»,
        // создание БД — «--create-database», запрет регламентных — «--scheduled-jobs-deny=on».
        if (!string.IsNullOrWhiteSpace(request.Description))
            args.Add($"--descr={request.Description}");
        args.Add($"--dbms={request.Dbms}");
        args.Add($"--db-server={request.DbServer}");
        args.Add($"--db-name={request.DbName}");
        if (!string.IsNullOrWhiteSpace(request.DbUser))
            args.Add($"--db-user={request.DbUser}");
        if (!string.IsNullOrEmpty(request.DbPassword))
            args.Add($"--db-pwd={request.DbPassword}");
        if (!string.IsNullOrWhiteSpace(request.Locale))
            args.Add($"--locale={request.Locale}");
        if (request.SecurityLevel is { } level)
            args.Add($"--security-level={level.ToString(CultureInfo.InvariantCulture)}");
        if (request.CreateDb)
            args.Add("--create-database");
        if (request.ScheduledJobsDeny)
            args.Add("--scheduled-jobs-deny=on");
        // issue #69, комментарий 7OH от 2026-10-26: «при переносе/копировании — копировать
        // флаги блокировок». Только фактически выставленная блокировка начала сеансов
        // (== true) передаётся ключом «--sessions-deny=on»; off/null — ключ не добавляется
        // (rac и так создаёт базу без блокировки).
        if (request.SessionsDeny == true)
            args.Add("--sessions-deny=on");
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «infobase update» (issue #3, кнопка «Изменить»):
    /// <c>infobase update --cluster=<uuid> --infobase=<uuid> --name=... --dbms=...
    /// --db-server=... --db-name=...</c> + необязательные параметры. Пароль СУБД
    /// остаётся одним токеном (ArgumentList без shell); в журнале он маскируется.
    /// Internal — для юнит-тестов сборки аргументов без запуска rac.
    /// </summary>
    internal static string[] InfobaseUpdateArgs(
        Guid clusterId, Guid infobaseId, RacInfobaseUpdateRequest? request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        // Параметры СУБД «--dbms/--db-server/--db-name» добавляются ТОЛЬКО непустыми:
        // «infobase update» с пустым значением («--dbms=») rac отклоняет малопонятной
        // ошибкой, а отсутствующие ключи дают его же внятный текст. VM догружает
        // параметры через «infobase info» перед блокировкой регламентных заданий
        // (issue #69, комментарий 7OH от 2026-10-26: «Тип СУБД в удаляемой базе явно
        // же прописан?») — ArgumentException здесь исключён.

        // Ключи по справке rac «infobase update» (issue #3, 7OH 17.10): имя, локаль и уровень
        // безопасности командой update не меняются (раньше передавались «--name/--locale/
        // --security-level», и rac отвергал команду); описание — «--descr», признаки —
        // «on|off», «yes|no», «allow|deny». Учётные данные администратора ИБ — первыми.
        var args = new List<string>(32)
        {
            "infobase", "update",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}"
        };
        if (!string.IsNullOrEmpty(request.InfobaseUser))
        {
            args.Add($"--infobase-user={request.InfobaseUser}");
            if (!string.IsNullOrEmpty(request.InfobasePassword))
                args.Add($"--infobase-pwd={request.InfobasePassword}");
        }
        if (!string.IsNullOrWhiteSpace(request.Dbms))
            args.Add($"--dbms={request.Dbms}");
        if (!string.IsNullOrWhiteSpace(request.DbServer))
            args.Add($"--db-server={request.DbServer}");
        if (!string.IsNullOrWhiteSpace(request.DbName))
            args.Add($"--db-name={request.DbName}");
        if (!string.IsNullOrWhiteSpace(request.DbUser))
            args.Add($"--db-user={request.DbUser}");
        if (!string.IsNullOrEmpty(request.DbPassword))
            args.Add($"--db-pwd={request.DbPassword}");
        // Описание передаётся всегда — так его можно и очистить.
        args.Add($"--descr={request.Description ?? string.Empty}");
        args.Add($"--scheduled-jobs-deny={OnOff(request.ScheduledJobsDeny)}");

        if (request.SessionsDeny is { } sessionsDeny)
            args.Add($"--sessions-deny={OnOff(sessionsDeny)}");
        if (request.DeniedFrom is { } deniedFrom)
            args.Add($"--denied-from={RacInfobaseInfo.FormatRacDate(deniedFrom)}");
        if (request.DeniedTo is { } deniedTo)
            args.Add($"--denied-to={RacInfobaseInfo.FormatRacDate(deniedTo)}");
        if (request.DeniedMessage is not null)
            args.Add($"--denied-message={request.DeniedMessage}");
        if (request.PermissionCode is not null)
            args.Add($"--permission-code={request.PermissionCode}");
        if (request.DeniedParameter is not null)
            args.Add($"--denied-parameter={request.DeniedParameter}");
        if (request.LicenseDistributionAllowed is { } license)
            args.Add($"--license-distribution={(license ? "allow" : "deny")}");
        if (request.ExternalSessionManagerConnectionString is not null)
            args.Add($"--external-session-manager-connection-string={request.ExternalSessionManagerConnectionString}");
        if (request.ExternalSessionManagerRequired is { } esmRequired)
            args.Add($"--external-session-manager-required={(esmRequired ? "yes" : "no")}");
        if (request.SecurityProfileName is not null)
            args.Add($"--security-profile-name={request.SecurityProfileName}");
        if (request.SafeModeSecurityProfileName is not null)
            args.Add($"--safe-mode-security-profile-name={request.SafeModeSecurityProfileName}");
        return args.ToArray();
    }

    /// <summary>Значение признака rac «on»/«off».</summary>
    private static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>
    /// Аргументы команды «infobase info» (issue #3): <c>infobase info --cluster=<uuid>
    /// --infobase=<uuid> [--infobase-user=U [--infobase-pwd=P]]</c>. Internal — для тестов.
    /// </summary>
    internal static string[] InfobaseInfoArgs(Guid clusterId, Guid infobaseId, string? infobaseUser, string? infobasePassword)
    {
        var args = new List<string>(6)
        {
            "infobase", "info",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}"
        };
        if (!string.IsNullOrEmpty(infobaseUser))
        {
            args.Add($"--infobase-user={infobaseUser}");
            if (!string.IsNullOrEmpty(infobasePassword))
                args.Add($"--infobase-pwd={infobasePassword}");
        }
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «infobase drop» (issue #3, комментарий 7OH от 2026-10-17):
    /// <c>infobase drop --cluster=<uuid> --infobase=<uuid> [--infobase-user=U
    /// [--infobase-pwd=P]] [--drop-database]</c>. Учётные данные администратора ИБ
    /// (issue #69) собираются по образцу <see cref="InfobaseInfoArgs"/> — rac требует
    /// их при удалении защищённой базы; пароль маскируется при журналировании.
    /// <paramref name="dropDatabase"/> — удалить заодно базу данных на сервере СУБД
    /// (диалог удаления с выбором, как в консоли кластера). Internal — для юнит-тестов
    /// сборки аргументов без запуска rac.
    /// </summary>
    internal static string[] InfobaseDropArgs(
        Guid clusterId, Guid infobaseId, bool dropDatabase,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        var args = new List<string>(7)
        {
            "infobase", "drop",
            $"--cluster={clusterId}",
            $"--infobase={infobaseId}"
        };
        if (!string.IsNullOrEmpty(infobaseUser))
        {
            args.Add($"--infobase-user={infobaseUser}");
            if (!string.IsNullOrEmpty(infobasePassword))
                args.Add($"--infobase-pwd={infobasePassword}");
        }
        if (dropDatabase)
            args.Add("--drop-database");
        return args.ToArray();
    }

    /// <summary>
    /// Нераспознанный вывод rac (issue #324): rac завершился с кодом 0 и вернул НЕпустой вывод,
    /// но ни табличный разбор, ни формат блоков «ключ : значение» не дали ни одной строки
    /// данных — вероятно, новая версия формата. Бросаем <see cref="RacOutputParseException"/>,
    /// чтобы монитор показал понятную ошибку и ОСТАНОВИЛ автообновление вместо тихих повторов
    /// каждые 5 с. Пустой вывод — легитимный случай (данных нет) и ошибкой не считается.
    /// Internal — для юнит-тестов правила «непустой вывод + 0 строк» без запуска процесса rac.
    /// </summary>
    internal static void EnsureParsedOrThrow(
        string output, int rowCount, string commandName, IAppLogger? logger = null)
    {
        if (!string.IsNullOrWhiteSpace(output) && rowCount == 0)
        {
            // Диагностика «exit=0, stdout>0, а вкладки пустые» (issue #324, лог 7OH
            // от 2026-10-08): раньше сбой разбора молча уходил в статус-строку окна,
            // в журнале не оставалось НИЧЕГО — по логу невозможно понять, какая из
            // шести параллельных list-команд не распознана и в каком виде пришёл
            // вывод. Пишем WARN с командой, объёмом и началом вывода.
            logger?.Warn(
                $"RAC: {commandName} — вывод rac не распознан: exit=0, stdout={output.Length} симв., " +
                $"0 записей. Начало вывода: «{Preview(output)}»");
            // issue #25: в текст ошибки включаем префикс вывода rac (первые ~200 символов,
            // пароль маскируется) — при следующем новом формате причина видна сразу
            // в сообщении окна/issue, без чтения журнала.
            throw RacOutputParseException.WithOutputPrefix(commandName, output);
        }
    }

    /// <summary>
    /// Однострочный фрагмент вывода для журнала: переводы строк и табуляции заменены
    /// пробелами, берутся первые 200 символов. Internal — для юнит-тестов формата.
    /// </summary>
    internal static string Preview(string output)
    {
        var flat = string.Join(
            " ",
            output.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        flat = flat.Trim();
        return flat.Length <= 200 ? flat : flat.Substring(0, 200) + "…";
    }

    /// <inheritdoc />
    public Task<bool> SetJobStateAsync(
        RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
        CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken, "job", JobActionCommand(action),
            $"--cluster={clusterId}", $"--job={jobId}");

    /// <inheritdoc />
    public Task<bool> UpdateClusterAsync(
        RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
        CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken, ClusterUpdateArgs(clusterId, changes));

    /// <summary>
    /// Аргументы команды «cluster update» (issue #324, C3): <c>cluster update
    /// --cluster=<uuid></c> + параметры ТОЛЬКО изменённых свойств. Значение с
    /// пробелами (имя кластера) остаётся одним токеном — аргументы передаются через
    /// ArgumentList без shell; пароль в аргументах маскируется при журналировании
    /// (<see cref="SensitiveDataMasker.MaskRacPassword"/>). Internal — для юнит-тестов
    /// сборки аргументов без запуска rac.
    /// </summary>
    internal static string[] ClusterUpdateArgs(Guid clusterId, RacClusterUpdate? changes)
    {
        if (changes is null || changes.IsEmpty)
            throw new ArgumentException("Нет изменений параметров кластера для сохранения.", nameof(changes));

        var args = new List<string>(12) { "cluster", "update", $"--cluster={clusterId}" };
        if (changes.Name is not null)
            args.Add($"--name={changes.Name}");
        if (changes.ExpirationTimeout is { } expirationTimeout)
            args.Add($"--expiration-timeout={expirationTimeout.ToString(CultureInfo.InvariantCulture)}");
        if (changes.LifetimeLimit is { } lifetimeLimit)
            args.Add($"--lifetime-limit={lifetimeLimit.ToString(CultureInfo.InvariantCulture)}");
        if (changes.MaxMemorySize is { } maxMemorySize)
            args.Add($"--max-memory-size={maxMemorySize.ToString(CultureInfo.InvariantCulture)}");
        if (changes.MaxMemoryTimeLimit is { } maxMemoryTimeLimit)
            args.Add($"--max-memory-time-limit={maxMemoryTimeLimit.ToString(CultureInfo.InvariantCulture)}");
        if (changes.SecurityLevel is { } securityLevel)
            args.Add($"--security-level={securityLevel.ToString(CultureInfo.InvariantCulture)}");
        if (changes.PingPeriod is { } pingPeriod)
            args.Add($"--ping-period={pingPeriod.ToString(CultureInfo.InvariantCulture)}");
        if (changes.PingTimeout is { } pingTimeout)
            args.Add($"--ping-timeout={pingTimeout.ToString(CultureInfo.InvariantCulture)}");
        if (changes.MaxAuthAttempts is { } maxAuthAttempts)
            args.Add($"--max-auth-attempts={maxAuthAttempts.ToString(CultureInfo.InvariantCulture)}");
        if (changes.AuthLockDuration is { } authLockDuration)
            args.Add($"--auth-lock-duration={authLockDuration.ToString(CultureInfo.InvariantCulture)}");
        return args.ToArray();
    }

    /// <inheritdoc />
    public Task<bool> TerminateSessionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid sessionId,
        Guid? processId = null, string? infobaseUser = null,
        string? infobasePassword = null, CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken,
            SessionTerminateArgs(clusterId, sessionId, processId, infobaseUser, infobasePassword));

    /// <inheritdoc />
    public Task<bool> DisconnectConnectionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid connectionId,
        Guid? processId = null, string? infobaseUser = null,
        string? infobasePassword = null, CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken,
            ConnectionDisconnectArgs(clusterId, connectionId, processId, infobaseUser, infobasePassword));

    /// <summary>
    /// Аргументы команды «session terminate»: «--process=<pid>» включается, когда рабочий
    /// процесс сеанса известен (rac 8.5.4 без него: «Ошибка разбора параметра: process»,
    /// issue #69). Учётные данные администратора ИБ (<paramref name="infobaseUser"/>/
    /// <paramref name="infobasePassword"/>, issue #69, комментарий 7OH от 2026-10-27)
    /// передаются аргументами «--infobase-user=…»/«--infobase-pwd=…» для защищённых баз —
    /// без них rac возвращает «Недостаточно прав пользователя на информационную базу».
    /// Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] SessionTerminateArgs(
        Guid clusterId, Guid sessionId, Guid? processId = null,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        var args = new List<string>(7) { "session", "terminate", $"--cluster={clusterId}" };
        if (processId is Guid pid && pid != Guid.Empty)
            args.Add($"--process={pid}");
        args.Add($"--session={sessionId}");
        if (!string.IsNullOrEmpty(infobaseUser))
        {
            args.Add($"--infobase-user={infobaseUser}");
            if (!string.IsNullOrEmpty(infobasePassword))
                args.Add($"--infobase-pwd={infobasePassword}");
        }
        return args.ToArray();
    }

    /// <summary>
    /// Аргументы команды «connection disconnect»: «--process=<pid>» включается, когда рабочий
    /// процесс соединения известен (rac 8.5.4 без него: «Ошибка разбора параметра: process»,
    /// issue #69). Учётные данные администратора ИБ (<paramref name="infobaseUser"/>/
    /// <paramref name="infobasePassword"/>, issue #69, комментарий 7OH от 2026-10-27)
    /// передаются аргументами «--infobase-user=…»/«--infobase-pwd=…» для защищённых баз —
    /// без них rac возвращает «Недостаточно прав пользователя на информационную базу».
    /// Internal — для юнит-тестов без запуска rac.
    /// </summary>
    internal static string[] ConnectionDisconnectArgs(
        Guid clusterId, Guid connectionId, Guid? processId = null,
        string? infobaseUser = null, string? infobasePassword = null)
    {
        var args = new List<string>(7) { "connection", "disconnect", $"--cluster={clusterId}" };
        if (processId is Guid pid && pid != Guid.Empty)
            args.Add($"--process={pid}");
        args.Add($"--connection={connectionId}");
        if (!string.IsNullOrEmpty(infobaseUser))
        {
            args.Add($"--infobase-user={infobaseUser}");
            if (!string.IsNullOrEmpty(infobasePassword))
                args.Add($"--infobase-pwd={infobasePassword}");
        }
        return args.ToArray();
    }

    /// <inheritdoc />
    public Task<bool> CreateInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
        CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken, InfobaseCreateArgs(clusterId, request));

    /// <inheritdoc />
    public Task<bool> UpdateInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken,
            InfobaseUpdateArgs(clusterId, infobaseId, request));

    /// <inheritdoc />
    public async Task<RacInfobaseInfo?> GetInfobaseInfoAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        string? infobaseUser, string? infobasePassword, CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await RunAsync(parameters, cancellationToken,
                InfobaseInfoArgs(clusterId, infobaseId, infobaseUser, infobasePassword)).ConfigureAwait(false);
            var info = RacOutputParser.ToInfobaseInfo(output);
            LastActionError = info is null ? "Не удалось разобрать вывод rac «infobase info»." : string.Empty;
            return info;
        }
        catch (OperationCanceledException)
        {
            LastActionError = "Операция отменена.";
            return null;
        }
        catch (Exception ex)
        {
            LastActionError = string.IsNullOrWhiteSpace(ex.Message) ? "Неизвестная ошибка rac." : ex.Message;
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> DropInfobaseAsync(
        RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
        bool dropDatabase = false, string? infobaseUser = null,
        string? infobasePassword = null, CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken,
            InfobaseDropArgs(clusterId, infobaseId, dropDatabase, infobaseUser, infobasePassword));

    /// <inheritdoc />
    public string LastActionError { get; private set; } = string.Empty;

    /// <summary>
    /// Собирает аргументы командной строки rac:
    /// <c>[адрес[:порт] --user=U --password=P] <команда> [--cluster=uuid ...]</c>.
    /// Точка подключения передаётся ЕДИНЫМ токеном в формате <c>host:port</c> первым аргументом
    /// (rac.exe host:port cluster list) — раздельные <c>--host=</c>/<c>--port=</c> не разбираются
    /// новыми версиями платформы (issue #324). Если порт ≤ 0 — только <c>host</c>; если адрес
    /// пуст — токен подключения опускается. Логин/пароль передаются как <c>--user=</c>/<c>--password=</c>.
    /// Значение с пробелами (адрес, пароль) остаётся одним токеном — аргументы передаются через
    /// ArgumentList без shell. Internal — для юнит-тестов сборки аргументов (без запуска процесса).
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(
        RacConnectionParams parameters, params string[] commandAndArgs)
    {
        var args = new List<string>(commandAndArgs.Length + 5);
        if (!string.IsNullOrWhiteSpace(parameters.Address))
            args.Add(parameters.Port > 0
                ? $"{parameters.Address}:{parameters.Port}"
                : parameters.Address);
        if (!string.IsNullOrEmpty(parameters.User))
            args.Add($"--user={parameters.User}");
        if (!string.IsNullOrEmpty(parameters.Password))
            args.Add($"--password={parameters.Password}");
        args.AddRange(commandAndArgs);
        return args;
    }

    /// <summary>Подкоманда rac для операции управления регламентным заданием.</summary>
    private static string JobActionCommand(RacJobAction action) => action switch
    {
        RacJobAction.Pause => "pause",
        RacJobAction.Resume => "resume",
        RacJobAction.Disable => "disable",
        _ => "enable"
    };

    /// <summary>
    /// Выполняет rac-команду действия (завершение сеанса / разрыв соединения /
    /// изменение состояния задания).
    /// Возвращает true при ExitCode 0; при неудаче — false и текст ошибки
    /// (сообщение rac + stderr) в <see cref="LastActionError"/>. Исключения наружу
    /// не пробрасываются: ViewModel показывает пользователю LastActionError.
    /// </summary>
    private async Task<bool> RunActionAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken,
        params string[] commandAndArgs)
    {
        try
        {
            await RunAsync(parameters, cancellationToken, commandAndArgs).ConfigureAwait(false);
            LastActionError = string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            LastActionError = "Операция отменена.";
            return false;
        }
        catch (Exception ex)
        {
            LastActionError = string.IsNullOrWhiteSpace(ex.Message)
                ? "Неизвестная ошибка rac."
                : ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Выполняет rac-команду: поиск исполняемого файла, прямой запуск без shell
    /// (UseShellExecute=false, CreateNoWindow=true, UTF-8), параллельное чтение
    /// stdout/stderr, таймаут 30 с. Возвращает stdout при ExitCode 0; при ошибке —
    /// <see cref="RacClientException"/> с текстом stderr.
    /// </summary>
    private async Task<string> RunAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken,
        params string[] commandAndArgs)
    {
        var args = BuildArguments(parameters, commandAndArgs);
        // Метка команды для журнала: строка «выполнено» должна указывать, КАКАЯ именно
        // list-команда завершилась (issue #324: шесть параллельных команд — по строкам
        // «выполнено, exit=0, stdout=N» их невозможно сопоставить с вкладками).
        var commandLabel = SensitiveDataMasker.MaskRacPassword(string.Join(" ", commandAndArgs));
        // Диагностика «подключение стало дольше» (issue #324): время выполнения одной
        // rac-команды видно в журнале — где остаются секунды (старт rac или сервер).
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Поиск rac может сканировать каталоги установленных платформ и быть заметной
        // частью общего времени подключения (issue #324: «локально почти 5 секунд») —
        // выделяем его тайминг в журнал, чтобы знать вклад этого этапа.
        var locatorStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var rac = OneCPlatformLocator.FindRacExecutable();
        locatorStopwatch.Stop();
        if (locatorStopwatch.ElapsedMilliseconds > 200)
        {
            // issue #80: события RAC — направление CM_RAC (INFO при включённых категории и CM_ON).
            Log.Info(LogCategory.Rac, $"RAC: поиск rac занял {locatorStopwatch.ElapsedMilliseconds} мс");
        }

        if (rac is null)
        {
            const string message =
                "Не найден исполняемый файл rac в каталоге установленной платформы 1С.";
            _logger.Warn($"RAC: {message}");
            throw new RacClientException(message);
        }

        // Пароль маскируется: в журнал rac-команда попадает без --password=<значение>.
        // Путь к найденному исполняемому файлу и полная командная строка (без секретов)
        // позволяют сравнить команду приложения с рабочей командой из терминала, когда
        // «в терминале rac работает, а монитор данные кластера не видит» (issue #324).
        // issue #80: события RAC — направление CM_RAC (INFO при включённых категории и CM_ON).
        Log.Info(LogCategory.Rac,
            $"RAC{SourcePrefix}: rac={rac}, команда: {SensitiveDataMasker.MaskRacPassword(string.Join(" ", args))}");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rac,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                    // Кодировку НЕ задаём: байты читаются сырыми и декодируются
                    // DecodeRacOutput (UTF-8 / cp866 / cp1251) — rac на Windows пишет
                    // в OEM-кодовой странице консоли, а не в UTF-8 (issue #324).
                }
            };

            // ArgumentList — каждый аргумент отдельным токеном, без shell: корректная
            // передача значений с пробелами и спецсимволами (пароль, имена баз).
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            if (!process.Start())
                throw new RacClientException($"Не удалось запустить rac: {rac}.");

            // Читаем stdout/stderr параллельно с ожиданием выхода: иначе большой вывод
            // может переполнить буфер канала и заблокировать завершение процесса.
            // Кодировка заранее неизвестна (UTF-8 на Linux, cp866/cp1251 на Windows),
            // поэтому читаем СЫРЫЕ байты и декодируем их позже (issue #324).
            var stdoutTask = ReadToEndBytesAsync(process.StandardOutput.BaseStream);
            var stderrTask = ReadToEndBytesAsync(process.StandardError.BaseStream);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CommandTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                if (cancellationToken.IsCancellationRequested)
                    throw;

                const string message = "Превышен таймаут ожидания ответа rac (30 с).";
                _logger.Warn($"RAC: {message}");
                throw new RacClientException(message);
            }

            var stdout = DecodeRacOutput(await stdoutTask.ConfigureAwait(false));
            var stderr = DecodeRacOutput(await stderrTask.ConfigureAwait(false));

            if (process.ExitCode != 0)
            {
                // Ненулевой код выхода rac: ошибка подключения, отсутствие прав либо
                // неподдерживаемая команда (например lock list на старых платформах).
                // В сообщение включаем точку подключения (адрес:порт), к которой шла
                // команда: пользователь должен видеть, куда именно «ушёл» запрос —
                // при повторных нажатиях тексты ошибок rac могут отличаться (issue #324).
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var token = parameters.Port > 0
                    ? $"{parameters.Address}:{parameters.Port}"
                    : parameters.Address;
                var message = string.IsNullOrWhiteSpace(token)
                    ? $"rac завершился с кодом {process.ExitCode}."
                    : $"rac ({token}) завершился с кодом {process.ExitCode}.";
                if (!string.IsNullOrWhiteSpace(detail))
                    message += " " + detail.Trim();

                _logger.Warn($"RAC: {message}");
                throw new RacClientException(message);
            }

            // Итог выполнения в журнале: код выхода, объём вывода и длительность —
            // по ним видно, что rac вернул данные (или пустой список) и где задержка
            // при «монитор не видит кластер» / «подключение стало дольше» (issue #324).
            stopwatch.Stop();
            // issue #80: события RAC — направление CM_RAC (INFO при включённых категории и CM_ON).
            // Код выхода/объём вывода полезны и при выключенной категории, поэтому WARN
            // и ERROR (выше в методе) остаются без фильтра — CM_RAC отключает только INFO.
            Log.Info(LogCategory.Rac,
                $"RAC{SourcePrefix}: {commandLabel} — выполнено, exit={process.ExitCode}, stdout={stdout.Length} симв., " +
                $"stderr={stderr.Length} симв., за {stopwatch.ElapsedMilliseconds} мс");
            return stdout;
        }
        catch (RacClientException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Не удалось выполнить команду rac: {ex.Message}";
            _logger.Error($"RAC: {message}", ex);
            throw new RacClientException(message, ex);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Процесс мог завершиться сам — игнорируем.
        }
    }

    /// <summary>Читает поток до конца в массив байтов (без декодирования).</summary>
    private static async Task<byte[]> ReadToEndBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Декодирует байты вывода rac в текст. Сначала строгий UTF-8 (Linux/Avalonia);
    /// если байты невалидны как UTF-8 (rac на Windows пишет в OEM/ANSI кодовой странице,
    /// обычно cp866, issue #324) — пробуем cp866 и cp1251 и берём вариант с наименьшим
    /// числом символов замены U+FFFD. Internal — для юнит-тестов без запуска процесса.
    /// </summary>
    internal static string DecodeRacOutput(byte[] bytes)
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