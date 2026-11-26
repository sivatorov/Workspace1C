using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>СУБД клиент-серверной базы для резервного копирования средствами СУБД.</summary>
public enum DbmsKind
{
    PostgreSql,
    MsSql
}

/// <summary>Профиль резервного копирования базы средствами СУБД (выпуск 1.0.0.69).</summary>
public sealed class DbmsBackupProfile
{
    /// <summary>Информационная база списка, к которой относится профиль.</summary>
    public string InfobaseId { get; set; } = string.Empty;

    public DbmsKind Dbms { get; set; } = DbmsKind.PostgreSql;

    /// <summary>Сервер СУБД (для MS SQL — имя экземпляра, например «srv\SQLEXPRESS»).</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Порт (0 — по умолчанию: 5432 для PostgreSQL).</summary>
    public int Port { get; set; }

    /// <summary>Имя базы данных в СУБД.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД (MS SQL: пусто — проверка подлинности Windows).</summary>
    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Каталог копий. Для MS SQL — путь, доступный самому серверу SQL.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Каталог утилит (pg_dump / sqlcmd); пусто — из PATH.</summary>
    public string ToolsDirectory { get; set; } = string.Empty;

    /// <summary>Сколько последних копий хранить (0 — все).</summary>
    public int KeepCount { get; set; } = 7;

    /// <summary>
    /// Таймаут операции дампа в минутах (issue #143). 0 — без ограничения:
    /// длительная копия большой базы не обрывается по времени и завершается
    /// только по факту (или кнопкой «Отмена»). Отрицательное значение
    /// нормализуется в 0. По умолчанию — без ограничения (1.0.1.125).
    /// </summary>
    public int TimeoutMinutes { get; set; }

    /// <summary>Адрес WebDAV-каталога для выгрузки копии (пусто — не выгружать).</summary>
    public string WebDavUrl { get; set; } = string.Empty;

    public string WebDavUser { get; set; } = string.Empty;

    public string WebDavPassword { get; set; } = string.Empty;
}

/// <summary>Файл профилей (<c>dbms_backups.json</c>).</summary>
public sealed class DbmsBackupData
{
    public List<DbmsBackupProfile> Profiles { get; set; } = new();

    public DbmsBackupProfile? Find(string? infobaseId) =>
        string.IsNullOrWhiteSpace(infobaseId)
            ? null
            : Profiles.FirstOrDefault(p => string.Equals(p.InfobaseId, infobaseId, StringComparison.Ordinal));

    public void Upsert(DbmsBackupProfile profile)
    {
        Profiles.RemoveAll(p => string.Equals(p.InfobaseId, profile.InfobaseId, StringComparison.Ordinal));
        Profiles.Add(profile);
    }
}

/// <summary>Этап операции резервного копирования средствами СУБД (issue #143).</summary>
public enum DbmsBackupStage
{
    Prepare,
    Check,
    Dump,
    Rotate,
    Upload,
    Done
}

/// <summary>Прогресс операции резервного копирования (issue #143).</summary>
/// <param name="Stage">Текущий этап.</param>
public sealed record DbmsBackupProgress(DbmsBackupStage Stage);

/// <summary>
/// Результат предварительной проверки (issue #143): утилита, каталог, подключение.
/// </summary>
public sealed class DbmsCheckResult
{
    /// <summary>Проверка пройдена.</summary>
    public bool Success { get; init; }

    /// <summary>Ключ локализации сообщения (успех или причина отказа).</summary>
    public string MessageKey { get; init; } = "Dbms.CheckFailed";

    /// <summary>Подробности: путь, версия утилиты, вывод сервера.</summary>
    public string? Detail { get; init; }

    public static DbmsCheckResult Ok(string? detail = null) =>
        new() { Success = true, MessageKey = "Dbms.CheckOk", Detail = detail };

    public static DbmsCheckResult Fail(string messageKey, string? detail = null) =>
        new() { Success = false, MessageKey = messageKey, Detail = detail };
}

/// <summary>
/// Резервное копирование клиент-серверных баз средствами СУБД (выпуск 1.0.0.69):
/// PostgreSQL — <c>pg_dump -Fc</c>, MS SQL Server — <c>BACKUP DATABASE … WITH COPY_ONLY</c>
/// через <c>sqlcmd</c>. На больших базах это в разы быстрее выгрузки .dt и не требует
/// монопольного режима. Ротация копий и выгрузка в WebDAV-хранилище (Яндекс.Диск,
/// Nextcloud и др.). Построение команд — чистые функции (тесты).
/// </summary>
public static class DbmsBackupService
{
    public const string FileName = "dbms_backups.json";

    /// <summary>
    /// Читает профили и нормализует их (issue #143): старые файлы с
    /// <c>TimeoutMinutes = 0</c>, путями в кавычках и лишними пробелами больше не
    /// приводят к зависанию/странностям. Сетевых операций при нормализации нет.
    /// </summary>
    public static DbmsBackupData Load()
    {
        var data = FeatureDataStore.Load<DbmsBackupData>(FileName);
        Normalize(data);
        return data;
    }

    /// <summary>Асинхронное чтение профилей (issue #143) — вне UI-потока, с отменой.</summary>
    public static async Task<DbmsBackupData> LoadAsync(CancellationToken ct = default)
    {
        var data = await FeatureDataStore.LoadAsync<DbmsBackupData>(FileName, ct).ConfigureAwait(false);
        Normalize(data);
        return data;
    }

    /// <summary>Синхронная запись профилей (оставлена для фоновых/сервисных вызовов).</summary>
    public static void Save(DbmsBackupData data) => FeatureDataStore.Save(FileName, data);

    /// <summary>
    /// Точка подмены асинхронной записи профилей (тесты). По умолчанию —
    /// <see cref="FeatureDataStore.SaveAsync{T}"/>; формат JSON не меняется.
    /// </summary>
    public static Func<DbmsBackupData, CancellationToken, Task> SaveOverride { get; set; } =
        (data, ct) => FeatureDataStore.SaveAsync(FileName, data, ct);

    /// <summary>Асинхронная запись профилей (issue #143) — вне UI-потока, с отменой.</summary>
    public static Task SaveAsync(DbmsBackupData data, CancellationToken ct = default) =>
        SaveOverride(data, ct);

    /// <summary>
    /// Нормализует старый профиль (issue #143), не выполняя сетевых операций:
    /// убирает пробелы/кавычки в путях, приводит отрицательный таймаут к 0
    /// («без ограничения», 1.0.1.125) и корректный тип СУБД, чтобы
    /// <see cref="Validate"/> не пропускал заведомо проблемный профиль в
    /// блокирующую операцию.
    /// </summary>
    public static void Normalize(DbmsBackupProfile profile)
    {
        if (profile is null)
            return;
        profile.Host = (profile.Host ?? string.Empty).Trim();
        profile.Database = (profile.Database ?? string.Empty).Trim();
        profile.User = (profile.User ?? string.Empty).Trim();
        profile.OutputDirectory = NormalizePath(profile.OutputDirectory);
        profile.ToolsDirectory = NormalizePath(profile.ToolsDirectory);
        profile.WebDavUrl = (profile.WebDavUrl ?? string.Empty).Trim();
        profile.WebDavUser = (profile.WebDavUser ?? string.Empty).Trim();
        // issue #143 (1.0.1.125): 0 — без ограничения, длинная копия не обрывается.
        if (profile.TimeoutMinutes < 0)
            profile.TimeoutMinutes = 0;
        if (profile.KeepCount < 0)
            profile.KeepCount = 7;
        if (!Enum.IsDefined(profile.Dbms))
            profile.Dbms = DbmsKind.PostgreSql;
    }

    /// <summary>Нормализует все профили файла (старые/частичные записи).</summary>
    public static void Normalize(DbmsBackupData data)
    {
        if (data?.Profiles is null)
            return;
        foreach (var profile in data.Profiles)
            Normalize(profile);
    }

    private static string NormalizePath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value.Substring(1, value.Length - 2).Trim();
        return value;
    }

    /// <summary>Профиль по умолчанию для клиент-серверной базы.</summary>
    public static DbmsBackupProfile SuggestProfile(Infobase infobase)
    {
        var server = infobase.Connection.Server ?? string.Empty;
        var colon = server.IndexOf(':');
        return new DbmsBackupProfile
        {
            InfobaseId = infobase.Id,
            Host = colon > 0 ? server.Substring(0, colon) : (string.IsNullOrWhiteSpace(server) ? "localhost" : server),
            Database = infobase.Connection.DatabaseName ?? string.Empty,
            OutputDirectory = OperatingSystem.IsWindows() ? @"C:\Backup\SQL" : "/var/backups/1c"
        };
    }

    /// <summary>Имя файла копии: «база_гггг-ММ-дд_ЧЧммсс.ext».</summary>
    public static string BuildFileName(DbmsBackupProfile profile, DateTime now) =>
        BuildFilePrefix(profile) + now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + FileExtension(profile);

    /// <summary>Префикс имени копий профиля (для ротации): «база_».</summary>
    public static string BuildFilePrefix(DbmsBackupProfile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.Database) ? "db" : profile.Database.Trim();
        return new string(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()) + "_";
    }

    public static string FileExtension(DbmsBackupProfile profile) =>
        profile.Dbms == DbmsKind.PostgreSql ? ".backup" : ".bak";

    public static string ToolPath(DbmsBackupProfile profile)
    {
        var exe = profile.Dbms == DbmsKind.PostgreSql ? "pg_dump" : "sqlcmd";
        if (OperatingSystem.IsWindows())
            exe += ".exe";
        return string.IsNullOrWhiteSpace(profile.ToolsDirectory) ? exe : Path.Combine(profile.ToolsDirectory.Trim(), exe);
    }

    /// <summary>Аргументы pg_dump (пароль передаётся переменной PGPASSWORD).</summary>
    public static List<string> BuildPgDumpArguments(DbmsBackupProfile profile, string targetFile)
    {
        var args = new List<string> { "-h", profile.Host.Trim() };
        if (profile.Port > 0)
        {
            args.Add("-p");
            args.Add(profile.Port.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(profile.User))
        {
            args.Add("-U");
            args.Add(profile.User.Trim());
        }
        args.AddRange(new[] { "-F", "c", "-b", "-w", "-f", targetFile, profile.Database.Trim() });
        return args;
    }

    /// <summary>SQL-команда резервного копирования MS SQL (имя базы и путь экранируются).</summary>
    public static string BuildMsSqlBackupQuery(DbmsBackupProfile profile, string targetFile)
    {
        var db = profile.Database.Trim().Replace("]", "]]");
        var path = targetFile.Replace("'", "''");
        return $"BACKUP DATABASE [{db}] TO DISK = N'{path}' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10";
    }

    /// <summary>Аргументы sqlcmd (без пользователя — проверка подлинности Windows, -E).</summary>
    public static List<string> BuildSqlCmdArguments(DbmsBackupProfile profile, string targetFile)
    {
        var server = profile.Host.Trim();
        if (profile.Port > 0)
            server += "," + profile.Port.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "-S", server, "-b" };
        if (string.IsNullOrWhiteSpace(profile.User))
            args.Add("-E");
        else
        {
            args.AddRange(new[] { "-U", profile.User.Trim(), "-P", profile.Password ?? string.Empty });
        }
        args.AddRange(new[] { "-Q", BuildMsSqlBackupQuery(profile, targetFile) });
        return args;
    }

    /// <summary>Проверка профиля: ключ локализации ошибки или null.</summary>
    public static string? Validate(DbmsBackupProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Host))
            return "Dbms.HostRequired";
        if (string.IsNullOrWhiteSpace(profile.Database))
            return "Dbms.DatabaseRequired";
        if (string.IsNullOrWhiteSpace(profile.OutputDirectory))
            return "Dbms.DirRequired";
        return null;
    }

    /// <summary>
    /// Точка подмены запуска утилит (тесты). По умолчанию — <see cref="ProcessRunner"/>.
    /// </summary>
    public static Func<string, IReadOnlyList<string>, TimeSpan?, IDictionary<string, string>?,
        CancellationToken, Task<ProcessRunResult>> RunToolOverride { get; set; } =
        (fileName, args, timeout, environment, ct) =>
            ProcessRunner.RunAsync(fileName, args, timeout: timeout, environment: environment, ct: ct);

    /// <summary>Порт СУБД по умолчанию (0 в профиле — «по умолчанию»).</summary>
    public static int EffectivePort(DbmsBackupProfile profile) => profile.Port > 0
        ? profile.Port
        : profile.Dbms == DbmsKind.PostgreSql ? 5432 : 1433;

    /// <summary>
    /// Проверка наличия и работоспособности утилиты резервного копирования
    /// (issue #143): <c>pg_dump --version</c> или <c>sqlcmd -?</c> с таймаутом 10 с.
    /// </summary>
    public static async Task<DbmsCheckResult> CheckToolsAsync(DbmsBackupProfile profile,
        CancellationToken ct = default)
    {
        var tool = ToolPath(profile);
        var args = profile.Dbms == DbmsKind.PostgreSql
            ? new[] { "--version" }
            : new[] { "-?" };
        var run = await RunToolAsync(tool, args, TimeSpan.FromSeconds(10), null, ct).ConfigureAwait(false);

        if (run.StartError is not null)
            return DbmsCheckResult.Fail("Dbms.ToolNotFound", $"{tool}: {run.StartError}");
        if (run.TimedOut)
            return DbmsCheckResult.Fail("Dbms.CheckTimeout", tool);
        if (!run.Success && string.IsNullOrWhiteSpace(run.CombinedOutput))
            return DbmsCheckResult.Fail("Dbms.CheckFailed", tool);

        var detail = run.CombinedOutput.Trim();
        return DbmsCheckResult.Ok(string.IsNullOrWhiteSpace(detail) ? tool : detail);
    }

    /// <summary>
    /// Проверка каталога копий (issue #143): каталог создаётся, в него пишется и
    /// удаляется пробный файл. Так пользователь видит отсутствие прав до запуска
    /// многоминутного дампа.
    /// <para>
    /// Вся работа выполняется на выделенном потоке (<see cref="BlockingIo"/>) — не на
    /// UI-потоке и не на потоке пула, — а <paramref name="ct"/> проверяется до/после
    /// каждой файловой операции: недоступный сетевой каталог не может «повесить» окно,
    /// отмена/таймаут прерывают проверку.
    /// </para>
    /// </summary>
    public static Task<DbmsCheckResult> CheckOutputDirectoryAsync(DbmsBackupProfile profile,
        CancellationToken ct = default) =>
        BlockingIo.Run(() => CheckOutputDirectoryCore(profile, ct), ct);

    private static DbmsCheckResult CheckOutputDirectoryCore(DbmsBackupProfile profile,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = profile.OutputDirectory?.Trim();
        if (string.IsNullOrWhiteSpace(dir))
            return DbmsCheckResult.Fail("Dbms.DirRequired");

        DbmsBackupTrace.Log("io-begin", $"check-directory {DbmsBackupTrace.DescribePath(dir)}");
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dir);
            ct.ThrowIfCancellationRequested();
            var probe = Path.Combine(dir, ".w1c_write_test");
            File.WriteAllText(probe, "ok");
            ct.ThrowIfCancellationRequested();
            File.Delete(probe);
            return DbmsCheckResult.Ok(dir);
        }
        catch (OperationCanceledException)
        {
            // Отмена/таймаут — не ошибка прав каталога, пробрасываем наверх.
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            return DbmsCheckResult.Fail("Dbms.NoWriteAccess", $"{dir}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return DbmsCheckResult.Fail("Dbms.DirUnavailable", $"{dir}: {ex.Message}");
        }
        finally
        {
            DbmsBackupTrace.Log("io-end", "check-directory");
        }
    }

    /// <summary>
    /// Проверка доступности СУБД (issue #143): TCP-подключение к host:port (5 с),
    /// затем, если утилита доступна, <c>pg_isready</c> или <c>sqlcmd -Q "SELECT 1"</c>.
    /// </summary>
    public static async Task<DbmsCheckResult> CheckConnectionAsync(DbmsBackupProfile profile,
        CancellationToken ct = default)
    {
        var host = profile.Host?.Trim();
        if (string.IsNullOrWhiteSpace(host))
            return DbmsCheckResult.Fail("Dbms.HostRequired");

        var port = EffectivePort(profile);
        var endpoint = $"{host}:{port}";
        if (!await CanConnectAsync(host, port, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false))
            return DbmsCheckResult.Fail("Dbms.ConnectionFailed", endpoint);

        if (profile.Dbms == DbmsKind.PostgreSql)
        {
            var pgIsReadyName = OperatingSystem.IsWindows() ? "pg_isready.exe" : "pg_isready";
            // Пусто в ToolsDirectory — ищем pg_isready в PATH (не "./pg_isready").
            var pgIsReady = string.IsNullOrWhiteSpace(profile.ToolsDirectory)
                ? pgIsReadyName
                : Path.Combine(profile.ToolsDirectory.Trim(), pgIsReadyName);
            var args = new List<string> { "-h", host, "-p", port.ToString(CultureInfo.InvariantCulture) };
            if (!string.IsNullOrWhiteSpace(profile.Database))
            {
                args.Add("-d");
                args.Add(profile.Database.Trim());
            }

            var run = await RunToolAsync(pgIsReady, args, TimeSpan.FromSeconds(10), null, ct)
                .ConfigureAwait(false);
            // pg_isready может отсутствовать — TCP-проверка уже подтвердила доступность.
            if (run.StartError is null && !run.TimedOut && !run.Success)
                return DbmsCheckResult.Fail("Dbms.ConnectionFailed", $"{endpoint}: {run.CombinedOutput.Trim()}");
            return DbmsCheckResult.Ok(endpoint);
        }

        var sqlArgs = BuildSqlCmdArguments(profile, Path.Combine(
            string.IsNullOrWhiteSpace(profile.OutputDirectory) ? "." : profile.OutputDirectory.Trim(),
            "probe.bak"));
        // Для проверки достаточно SELECT 1: подменяем команду резервного копирования.
        var queryIndex = sqlArgs.IndexOf("-Q");
        if (queryIndex >= 0 && queryIndex + 1 < sqlArgs.Count)
            sqlArgs[queryIndex + 1] = "SET NOCOUNT ON; SELECT 1";
        var sqlRun = await RunToolAsync(ToolPath(profile), sqlArgs, TimeSpan.FromSeconds(10), null, ct)
            .ConfigureAwait(false);
        if (sqlRun.StartError is not null)
            return DbmsCheckResult.Fail("Dbms.ToolNotFound", ToolPath(profile));
        if (sqlRun.TimedOut)
            return DbmsCheckResult.Fail("Dbms.CheckTimeout", endpoint);
        return sqlRun.Success
            ? DbmsCheckResult.Ok(endpoint)
            : DbmsCheckResult.Fail("Dbms.ConnectionFailed", $"{endpoint}: {sqlRun.CombinedOutput.Trim()}");
    }

    private static async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запуск утилиты на выделенном потоке (issue #143): <c>Process.Start</c> и ожидание
    /// не выполняются ни на UI-потоке, ни на потоке пула — зависший UNC-путь к pg_dump
    /// не «съедает» потоки пула и не мешает срабатыванию сторожевого таймаута.
    /// </summary>
    private static Task<ProcessRunResult> RunToolAsync(string fileName, IReadOnlyList<string> args,
        TimeSpan? timeout, IDictionary<string, string>? environment, CancellationToken ct) =>
        BlockingIo.RunAsync(() => RunToolOverride(fileName, args, timeout, environment, ct), ct);

    /// <summary>Удаляет недописанный файл копии после отмены/таймаута/ошибки.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Файл занят или уже удалён — это не ошибка сценария.
        }
    }

    /// <summary>Создаёт копию, применяет ротацию и при необходимости выгружает в WebDAV.</summary>
    public static Task<BackupRunResult> RunAsync(DbmsBackupProfile profile, Action<string>? log = null,
        CancellationToken ct = default)
        => RunAsync(profile, log, null, ct);

    /// <summary>
    /// Создаёт копию с отчётом о прогрессе (issue #143). Вся работа с файловой
    /// системой и процессами уходит с вызывающего потока, поэтому окно остаётся
    /// отзывчивым, а «Отмена» и таймаут действительно прерывают операцию.
    /// </summary>
    /// <param name="profile">Профиль копирования.</param>
    /// <param name="log">Журнал окна (строка текста).</param>
    /// <param name="progress">Этапы операции; null — без отчёта.</param>
    /// <param name="ct">Отмена (кнопка «Отмена», закрытие окна).</param>
    public static async Task<BackupRunResult> RunAsync(DbmsBackupProfile profile, Action<string>? log,
        IProgress<DbmsBackupProgress>? progress, CancellationToken ct = default)
    {
        var result = new BackupRunResult();
        var error = Validate(profile);
        if (error is not null)
        {
            result.ErrorMessage = LocalizationManager.T(error);
            return result;
        }

        // issue #143: один запуск на процесс. Окно теперь немодальное, его можно
        // закрыть и открыть снова, пока копия ещё выполняется, — повторный запуск
        // не должен создавать второй дамп параллельно.
        if (Interlocked.CompareExchange(ref _activeRun, 1, 0) == 1)
        {
            DbmsBackupTrace.Log("reentrancy", "service-run");
            result.ErrorMessage = LocalizationManager.T("Dbms.Reentrancy");
            return result;
        }

        // issue #143 (1.0.1.125): 0 — без жёсткого лимита. Копия большой базы
        // не должна обрываться по времени; прервать её может только «Отмена».
        TimeSpan? timeout = profile.TimeoutMinutes > 0
            ? TimeSpan.FromMinutes(profile.TimeoutMinutes)
            : null;
        var directory = profile.OutputDirectory.Trim();
        var target = Path.Combine(directory, BuildFileName(profile, DateTime.Now));

        try
        {
            progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Prepare));

            // issue #143: до запуска утилиты проверяем, что она действительно лежит
            // в указанном каталоге. Проверки подключения/каталога могут пройти
            // успешно, а pg_dump/sqlcmd при этом отсутствует (например, в поле
            // «Каталог pg_dump / sqlcmd» указана папка для копий) — раньше это
            // давало невнятную ошибку после «успешных» проверок.
            var missingTool = await BlockingIo.Run(() => CheckToolFile(profile), ct).ConfigureAwait(false);
            if (missingTool is not null)
            {
                DbmsBackupTrace.Error("error",
                    "run: tool-not-found " + DbmsBackupTrace.DescribePath(missingTool));
                result.ErrorMessage = LocalizationManager.T("Dbms.ToolNotFound") + ": " + missingTool;
                return result;
            }

            // Создание каталога — на выделенном потоке (issue #143): сетевой путь
            // может отвечать десятки секунд, и раньше это выглядело как «наглухо
            // зависло», а блокировка потока пула мешала срабатыванию таймаутов.
            DbmsBackupTrace.Log("io-begin", $"create-directory {DbmsBackupTrace.DescribePath(directory)}");
            try
            {
                await BlockingIo.Run(() => Directory.CreateDirectory(directory), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                // Конкретная причина вместо общего «копия не создана»: путь и текст
                // ошибки файловой системы (нет прав, диск/UNC недоступен).
                DbmsBackupTrace.Error("error",
                    "run: create-directory " + DbmsBackupTrace.DescribePath(directory) + ": " + ex.Message, ex);
                result.ErrorMessage = LocalizationManager.T("Dbms.DirUnavailable") + ": " + directory +
                    ": " + ex.Message;
                return result;
            }
            DbmsBackupTrace.Log("io-end", "create-directory");

            progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Check));

            var tool = ToolPath(profile);
            ProcessRunResult run;
            if (profile.Dbms == DbmsKind.PostgreSql)
            {
                var env = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(profile.Password))
                    env["PGPASSWORD"] = profile.Password;
                log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDumping"), profile.Database, target));
                progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Dump));
                DbmsBackupTrace.Log("tool-start",
                    $"{DbmsBackupTrace.DescribePath(tool)}; target={DbmsBackupTrace.DescribePath(target)}");
                run = await RunToolAsync(tool, BuildPgDumpArguments(profile, target),
                    timeout, env, ct).ConfigureAwait(false);
            }
            else
            {
                // Каталог копии должен существовать на стороне сервера SQL; для
                // локального сервера создаём его сами, для удалённого — забота
                // администратора (ошибку не показываем).
                try
                {
                    await BlockingIo.Run(() => Directory.CreateDirectory(directory), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Путь существует только на сервере SQL.
                }

                log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDumping"), profile.Database, target));
                progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Dump));
                DbmsBackupTrace.Log("tool-start",
                    $"{DbmsBackupTrace.DescribePath(tool)}; target={DbmsBackupTrace.DescribePath(target)}");
                run = await RunToolAsync(tool, BuildSqlCmdArguments(profile, target),
                    timeout, null, ct).ConfigureAwait(false);
            }

            // issue #143: в журнал диагностики — код возврата, длительность и хвост
            // stderr (без паролей). По этим данным видно, почему копия не создалась.
            DbmsBackupTrace.Log("tool-exit",
                $"exit={run.ExitCode}; timedout={run.TimedOut}; canceled={run.Canceled}; " +
                $"startError={(run.StartError is null ? "нет" : "да")}");
            LogOutputTail(run);

            if (run.Canceled)
            {
                await TryDeleteAsync(target, CancellationToken.None).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            if (run.TimedOut)
            {
                await TryDeleteAsync(target, CancellationToken.None).ConfigureAwait(false);
                result.ErrorMessage = LocalizationManager.T("Dbms.TimedOut");
                return result;
            }

            if (run.StartError is not null)
            {
                await TryDeleteAsync(target, CancellationToken.None).ConfigureAwait(false);
                result.ErrorMessage = LocalizationManager.T("Dbms.ToolNotFound") + ": " + run.StartError;
                return result;
            }

            if (!run.Success)
            {
                await TryDeleteAsync(target, CancellationToken.None).ConfigureAwait(false);
                var tail = OutputTail(run);
                DbmsBackupTrace.Error("error",
                    $"run: tool-failed exit={run.ExitCode}; tool={DbmsBackupTrace.DescribePath(tool)}; " +
                    $"target={DbmsBackupTrace.DescribePath(target)}; output={tail}");
                result.ErrorMessage = BuildToolFailureMessage(tool, target, run.ExitCode, tail);
                return result;
            }

            // issue #143: успешный код возврата ещё не значит, что файл создан.
            // Для PostgreSQL файл пишет сама утилита локально — проверяем наличие и
            // размер, чтобы не показывать «успех» без копии (и наоборот, не терять
            // понятную причину, если файл пуст). Для MS SQL путь может быть виден
            // только серверу SQL — там ограничиваемся предупреждением в журнале.
            var fileState = await BlockingIo.Run(() => GetFileState(target), ct).ConfigureAwait(false);
            DbmsBackupTrace.Log("verify-file",
                $"{DbmsBackupTrace.DescribePath(target)}; exists={fileState.Exists}; size={fileState.Length}");
            if (profile.Dbms == DbmsKind.PostgreSql && (!fileState.Exists || fileState.Length <= 0))
            {
                var tail = OutputTail(run);
                DbmsBackupTrace.Error("error",
                    $"run: file-not-created target={DbmsBackupTrace.DescribePath(target)}; " +
                    $"exists={fileState.Exists}; size={fileState.Length}; output={tail}");
                result.ErrorMessage = string.Format(CultureInfo.InvariantCulture,
                    LocalizationManager.T("Dbms.FileNotCreated"), target, run.ExitCode, tail);
                return result;
            }

            result.Success = true;
            result.CreatedFiles = new[] { target };
            log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDone"), target));

            if (profile.KeepCount > 0)
            {
                progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Rotate));
                // keepPath защищает только что созданный файл: при неточных отметках
                // времени на сетевом/FAT-каталоге ротация не должна его удалять.
                var removed = await BlockingIo.Run(() => BackupRotation.Apply(directory, BuildFilePrefix(profile),
                    FileExtension(profile), profile.KeepCount, 0, target), ct).ConfigureAwait(false);
                DbmsBackupTrace.Log("rotate", $"removed={removed.Count}; keep={profile.KeepCount}");
                if (removed.Count > 0)
                    log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogRotated"), removed.Count));
            }

            var targetExists = string.IsNullOrWhiteSpace(profile.WebDavUrl) || fileState.Exists;
            if (!string.IsNullOrWhiteSpace(profile.WebDavUrl) && targetExists)
            {
                progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Upload));
                log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogUploading"), profile.WebDavUrl));
                var upload = await WebDavUploader.UploadAsync(target, profile.WebDavUrl, profile.WebDavUser,
                    profile.WebDavPassword, ct).ConfigureAwait(false);
                if (upload is not null)
                {
                    result.Success = false;
                    result.ErrorMessage = string.Format(LocalizationManager.T("Dbms.UploadFailed"), upload);
                    return result;
                }
                log?.Invoke(LocalizationManager.T("Dbms.LogUploaded"));
            }

            // ПРО CloudBackup: готовый файл получают загрузчики (S3, шифрование).
            // Ошибка загрузчика не делает копию неуспешной.
            await BackupUploadHooks.RunAsync(
                new BackupUploadContext(target, profile.InfobaseId, profile.Database, "dbms"), ct)
                .ConfigureAwait(false);

            progress?.Report(new DbmsBackupProgress(DbmsBackupStage.Done));
            return result;
        }
        catch (OperationCanceledException)
        {
            // Недописанный файл не должен попасть в ротацию и выглядеть как копия.
            await TryDeleteAsync(target, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _activeRun, 0);
        }
    }

    /// <summary>Признак выполняющегося копирования (issue #143).</summary>
    private static int _activeRun;

    /// <summary>
    /// Возвращает полный путь отсутствующей утилиты или null (issue #143). Пустой
    /// <see cref="DbmsBackupProfile.ToolsDirectory"/> — поиск в PATH, наличие проверит
    /// сам запуск процесса.
    /// </summary>
    internal static string? CheckToolFile(DbmsBackupProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ToolsDirectory))
            return null;
        var tool = ToolPath(profile);
        try
        {
            return File.Exists(tool) ? null : tool;
        }
        catch
        {
            return tool;
        }
    }

    /// <summary>Состояние файла копии: существование и размер (issue #143).</summary>
    internal static (bool Exists, long Length) GetFileState(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (true, info.Length) : (false, 0);
        }
        catch
        {
            return (false, 0);
        }
    }

    /// <summary>Последние строки вывода утилиты (stderr/stdout) для причины ошибки.</summary>
    internal static string OutputTail(ProcessRunResult run, int maxLength = 600)
    {
        var text = run.CombinedOutput;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        text = text.Trim();
        return text.Length <= maxLength ? text : text.Substring(text.Length - maxLength);
    }

    /// <summary>Пишет хвост вывода утилиты в диагностику (issue #143).</summary>
    private static void LogOutputTail(ProcessRunResult run)
    {
        var tail = OutputTail(run, 400);
        if (tail.Length == 0)
            return;
        DbmsBackupTrace.Log("tool-output", tail);
    }

    /// <summary>
    /// Понятное сообщение об ошибке утилиты: программа, код возврата, путь копии и
    /// хвост вывода (issue #143). Без этого после «успешных» проверок пользователь
    /// видел общее «копия не создана» без причины.
    /// </summary>
    internal static string BuildToolFailureMessage(string tool, string target, int exitCode, string tail)
    {
        var template = LocalizationManager.T("Dbms.ToolFailed");
        if (template.Contains("{0}", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, template, tool, exitCode, target, tail);
        return LocalizationManager.T("Dbms.Failed") + $": {tool}; exit={exitCode}; {target}; {tail}";
    }

    /// <summary>Удаление недописанного файла на выделенном потоке (issue #143).</summary>
    private static Task TryDeleteAsync(string path, CancellationToken ct) =>
        BlockingIo.Run(() => TryDelete(path), ct);
}

/// <summary>
/// Выгрузка файла в WebDAV-каталог (Яндекс.Диск — https://webdav.yandex.ru/папка,
/// Nextcloud — https://host/remote.php/dav/files/user/папка): MKCOL каталога и PUT
/// файла потоком с базовой аутентификацией. Возвращает null при успехе или текст ошибки.
/// </summary>
public static class WebDavUploader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(6) };

    public static string CombineUrl(string folderUrl, string fileName) =>
        folderUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName);

    public static async Task<string?> UploadAsync(string filePath, string folderUrl, string? user, string? password,
        CancellationToken ct = default)
    {
        try
        {
            AuthenticationHeaderValue? auth = null;
            if (!string.IsNullOrEmpty(user))
            {
                var raw = Encoding.UTF8.GetBytes($"{user}:{password ?? string.Empty}");
                auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            }

            using (var mkcol = new HttpRequestMessage(new HttpMethod("MKCOL"), folderUrl.TrimEnd('/') + "/"))
            {
                mkcol.Headers.Authorization = auth;
                using var mkcolResponse = await Http.SendAsync(mkcol, ct).ConfigureAwait(false);
                // 201 — создан, 405 — уже существует: оба варианта подходят.
            }

            await using var stream = File.OpenRead(filePath);
            using var put = new HttpRequestMessage(HttpMethod.Put, CombineUrl(folderUrl, Path.GetFileName(filePath)))
            {
                Content = new StreamContent(stream)
            };
            put.Headers.Authorization = auth;
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await Http.SendAsync(put, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
