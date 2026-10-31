using System.Diagnostics;
using System.IO;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Результат запуска утилиты ibcmd.</summary>
public sealed class IbcmdResult
{
    public bool Success { get; init; }

    public int ExitCode { get; init; } = -1;

    /// <summary>Вывод утилиты (stdout + stderr), пароли замаскированы.</summary>
    public string Output { get; init; } = string.Empty;

    public string? Error { get; init; }

    public static IbcmdResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Утилита автономного сервера <c>ibcmd</c> как второй «движок» операций с базой
/// (выпуск 1.0.0.84): выгрузка и загрузка <c>.dt</c>, сохранение и загрузка
/// конфигурации <c>.cf</c> с применением, установка расширений <c>.cfe</c> —
/// без запуска «Конфигуратора». Работает быстрее, не открывает окон, не требует
/// лицензии конфигуратора и одинаково ведёт себя в Windows и Linux.
/// <para>
/// Подключение. Файловая база — <c>--db-path</c>; клиент-серверная — напрямую к СУБД
/// (<c>--dbms --db-server --db-name --db-user --db-pwd</c>), параметры СУБД берутся из
/// профиля окна «Резервные копии СУБД» этой базы. Пользователь ИБ — <c>--user/--password</c>
/// (учётные данные «Конфигуратора» базы или сценария). Аргументы передаются списком
/// (<see cref="ProcessStartInfo.ArgumentList"/>), поэтому кавычки и пробелы в путях и
/// паролях экранировать не нужно.
/// </para>
/// </summary>
public static class IbcmdService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(3);

    /// <summary>Имя исполняемого файла ibcmd для текущей ОС.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? "ibcmd.exe" : "ibcmd";

    /// <summary>ibcmd в каталоге исполняемого файла платформы (bin) или null.</summary>
    public static string? FindNextTo(string? platformExecutable)
    {
        if (string.IsNullOrWhiteSpace(platformExecutable))
            return null;
        var dir = Path.GetDirectoryName(platformExecutable);
        if (string.IsNullOrEmpty(dir))
            return null;
        var candidate = Path.Combine(dir, ExecutableName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>ibcmd платформы базы (рядом с 1cv8 той же версии и разрядности).</summary>
    public static string? ResolveExecutable(Infobase infobase)
    {
        if (infobase is null)
            return null;
        try
        {
            return FindNextTo(OneCLauncher.ResolveConfiguratorExecutable(infobase));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Значение <c>--dbms</c> для вида СУБД профиля.</summary>
    public static string DbmsName(DbmsKind kind) => kind == DbmsKind.MsSql ? "MSSQLServer" : "PostgreSQL";

    /// <summary>Сервер СУБД с портом в принятом у 1С виде: PostgreSQL «host port=N», MS SQL «host,N».</summary>
    public static string DbServer(DbmsBackupProfile profile)
    {
        var host = string.IsNullOrWhiteSpace(profile.Host) ? "localhost" : profile.Host.Trim();
        if (profile.Port <= 0)
            return host;
        return profile.Dbms == DbmsKind.MsSql ? $"{host},{profile.Port}" : $"{host} port={profile.Port}";
    }

    /// <summary>
    /// Аргументы подключения к базе. Для клиент-серверной базы нужен профиль СУБД
    /// (иначе <paramref name="error"/>); веб-базы ibcmd не поддерживает.
    /// </summary>
    public static List<string>? BuildConnectionArguments(Infobase infobase, DbmsBackupProfile? dbms, out string? error)
    {
        error = null;
        var c = infobase?.Connection;
        if (c is null)
        {
            error = LocalizationManager.T("Ibcmd.NoConnection");
            return null;
        }
        switch (c.Type)
        {
            case ConnectionType.File:
                if (string.IsNullOrWhiteSpace(c.FilePath))
                {
                    error = LocalizationManager.T("Ibcmd.NoConnection");
                    return null;
                }
                return new List<string> { "--db-path=" + c.FilePath.Trim().TrimEnd('\\', '/') };

            case ConnectionType.ClientServer:
                if (dbms is null || string.IsNullOrWhiteSpace(dbms.Database))
                {
                    error = LocalizationManager.T("Ibcmd.NeedDbmsProfile");
                    return null;
                }
                var args = new List<string>
                {
                    "--dbms=" + DbmsName(dbms.Dbms),
                    "--db-server=" + DbServer(dbms),
                    "--db-name=" + dbms.Database.Trim()
                };
                if (!string.IsNullOrWhiteSpace(dbms.User))
                    args.Add("--db-user=" + dbms.User.Trim());
                if (!string.IsNullOrEmpty(dbms.Password))
                    args.Add("--db-pwd=" + dbms.Password);
                return args;

            default:
                error = LocalizationManager.T("Ibcmd.WebNotSupported");
                return null;
        }
    }

    /// <summary>Аргументы пользователя ИБ (пусто, если вход без пароля/без имени).</summary>
    public static List<string> BuildUserArguments(string? user, string? password)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(user))
            return args;
        args.Add("--user=" + user.Trim());
        if (!string.IsNullOrEmpty(password))
            args.Add("--password=" + password);
        return args;
    }

    /// <summary>
    /// Учётные данные пользователя ИБ: явные из сценария (если заданы) или
    /// «Конфигуратора» базы с учётом общей авторизации.
    /// </summary>
    public static (string User, string Password) ResolveUser(Infobase infobase, BackupCredential? credential = null)
    {
        if (credential is { UseInfobaseAuth: false } && !string.IsNullOrWhiteSpace(credential.User))
            return (credential.User, credential.Password ?? string.Empty);
        InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Configurator, out var mode, out var user, out var password,
            OneCLauncher.CommonEnterpriseAuthorization,
            OneCLauncher.CommonConfiguratorAuthorization,
            OneCLauncher.CommonAuthorization);
        return mode == AuthenticationMode.Credentials ? (user ?? string.Empty, password ?? string.Empty) : (string.Empty, string.Empty);
    }

    /// <summary>Полный список аргументов команды: «infobase …» + подключение + пользователь + хвост.</summary>
    public static List<string> BuildCommand(IEnumerable<string> command, IEnumerable<string> connection,
        IEnumerable<string> user, IEnumerable<string> tail)
    {
        var args = new List<string>(command);
        args.AddRange(connection);
        args.AddRange(user);
        args.AddRange(tail);
        return args;
    }

    public static readonly string[] DumpCommand = { "infobase", "dump" };
    public static readonly string[] RestoreCommand = { "infobase", "restore" };
    public static readonly string[] ConfigSaveCommand = { "infobase", "config", "save" };
    public static readonly string[] ConfigLoadCommand = { "infobase", "config", "load" };
    public static readonly string[] ConfigApplyCommand = { "infobase", "config", "apply" };

    /// <summary>Хвост загрузки/применения расширения: <c>--extension=Имя</c>.</summary>
    public static string ExtensionArgument(string name) => "--extension=" + name.Trim();

    /// <summary>Маскирует значения паролей в тексте (вывод утилиты, журнал команды).</summary>
    public static string Mask(string? text, IEnumerable<string?> secrets) =>
        ComScriptRunner.MaskSecrets(text ?? string.Empty, secrets) ?? string.Empty;

    /// <summary>Командная строка для журнала: пароли заменены на «***».</summary>
    public static string DescribeCommand(IEnumerable<string> args) =>
        string.Join(" ", args.Select(a =>
        {
            foreach (var key in new[] { "--db-pwd=", "--password=" })
                if (a.StartsWith(key, StringComparison.Ordinal))
                    return key + "***";
            return a.Contains(' ') ? "\"" + a + "\"" : a;
        }));

    // ---------------- Операции ----------------

    /// <summary>
    /// Выгрузка .dt. <paramref name="dbmsProfileOverride"/> — переопределение профиля СУБД
    /// (issue #79, 1.0.1.9: мастер «Развернуть тестовую копию» передаёт значения полей
    /// формы — «либо из профиля, либо вручную»; null — сохранённый профиль базы, как раньше).
    /// </summary>
    public static Task<IbcmdResult> DumpAsync(Infobase infobase, string dtPath, BackupCredential? credential = null,
        CancellationToken ct = default, DbmsBackupProfile? dbmsProfileOverride = null) =>
        RunForInfobaseAsync(infobase, DumpCommand, new[] { dtPath }, credential, ct, ensureDirOf: dtPath,
            dbmsProfileOverride: dbmsProfileOverride);

    public static Task<IbcmdResult> RestoreAsync(Infobase infobase, string dtPath, BackupCredential? credential = null,
        CancellationToken ct = default) =>
        RunForInfobaseAsync(infobase, RestoreCommand, new[] { dtPath }, credential, ct);

    public static Task<IbcmdResult> SaveConfigAsync(Infobase infobase, string cfPath, BackupCredential? credential = null,
        CancellationToken ct = default) =>
        RunForInfobaseAsync(infobase, ConfigSaveCommand, new[] { cfPath }, credential, ct, ensureDirOf: cfPath);

    /// <summary>Загружает конфигурацию из .cf и применяет её к базе данных.</summary>
    public static async Task<IbcmdResult> LoadConfigAsync(Infobase infobase, string cfPath, CancellationToken ct = default)
    {
        var load = await RunForInfobaseAsync(infobase, ConfigLoadCommand, new[] { cfPath }, null, ct).ConfigureAwait(false);
        if (!load.Success)
            return load;
        var apply = await RunForInfobaseAsync(infobase, ConfigApplyCommand, new[] { "--force" }, null, ct).ConfigureAwait(false);
        return Merge(load, apply);
    }

    /// <summary>Загружает расширение из .cfe и применяет его.</summary>
    public static async Task<IbcmdResult> InstallExtensionAsync(Infobase infobase, string cfePath, string extensionName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(extensionName))
            return IbcmdResult.Fail(LocalizationManager.T("Extensions.BadName"));
        var ext = ExtensionArgument(extensionName);
        var load = await RunForInfobaseAsync(infobase, ConfigLoadCommand, new[] { ext, cfePath }, null, ct).ConfigureAwait(false);
        if (!load.Success)
            return load;
        var apply = await RunForInfobaseAsync(infobase, ConfigApplyCommand, new[] { ext, "--force" }, null, ct).ConfigureAwait(false);
        return Merge(load, apply);
    }

    private static IbcmdResult Merge(IbcmdResult first, IbcmdResult second) => new()
    {
        Success = second.Success,
        ExitCode = second.ExitCode,
        Output = (first.Output + "\n" + second.Output).Trim(),
        Error = second.Error
    };

    private static async Task<IbcmdResult> RunForInfobaseAsync(Infobase infobase, string[] command, string[] tail,
        BackupCredential? credential, CancellationToken ct, string? ensureDirOf = null,
        DbmsBackupProfile? dbmsProfileOverride = null)
    {
        if (infobase is null)
            return IbcmdResult.Fail(LocalizationManager.T("Designer.NoInfobase"));
        var exe = ResolveExecutable(infobase);
        if (exe is null)
            return IbcmdResult.Fail(LocalizationManager.T("Ibcmd.NotFound"));

        // issue #79 (1.0.1.9): явное переопределение профиля СУБД имеет приоритет над
        // сохранённым профилем базы (мастер «Развернуть тестовую копию» выполняет операцию
        // по значениям на форме).
        DbmsBackupProfile? dbms = dbmsProfileOverride;
        if (infobase.Connection?.Type == ConnectionType.ClientServer && dbms is null)
        {
            try { dbms = DbmsBackupService.Load().Find(infobase.Id); }
            catch { /* профиль недоступен — ниже понятная ошибка */ }
        }
        var connection = BuildConnectionArguments(infobase, dbms, out var error);
        if (connection is null)
            return IbcmdResult.Fail(error ?? LocalizationManager.T("Ibcmd.NoConnection"));

        var (user, password) = ResolveUser(infobase, credential);
        if (!string.IsNullOrEmpty(ensureDirOf))
        {
            var dir = Path.GetDirectoryName(ensureDirOf);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var args = BuildCommand(command, connection, BuildUserArguments(user, password), tail);
        return await RunAsync(exe, args, new[] { password, dbms?.Password }, DefaultTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Запускает ibcmd с аргументами, ждёт завершения; успех — код 0. Вывод читается
    /// в кодировке <see cref="ProcessRunner.ConsoleOutputEncoding"/> (issue #79,
    /// «каракули» в окне учётных данных ibcmd: на русской Windows ibcmd пишет в
    /// консольной OEM-кодировке cp866, чтение как UTF-8 давало нечитаемый текст).
    /// </summary>
    public static async Task<IbcmdResult> RunAsync(string exe, IReadOnlyList<string> args, IEnumerable<string?> secrets,
        TimeSpan timeout, CancellationToken ct = default)
    {
        var outputEncoding = ProcessRunner.ConsoleOutputEncoding() ?? Encoding.UTF8;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        var secretList = secrets.ToList();

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return IbcmdResult.Fail(ex.Message);
        }
        if (process is null)
            return IbcmdResult.Fail(LocalizationManager.T("Designer.StartFailed"));

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
                return IbcmdResult.Fail(ct.IsCancellationRequested
                    ? LocalizationManager.T("Designer.Cancelled")
                    : LocalizationManager.T("Designer.Timeout"));
            }

            var output = Mask(((await stdout.ConfigureAwait(false)) + "\n" + (await stderr.ConfigureAwait(false))).Trim(), secretList);
            var code = process.ExitCode;
            return new IbcmdResult
            {
                Success = code == 0,
                ExitCode = code,
                Output = output,
                Error = code == 0 ? null : string.Format(LocalizationManager.T("Ibcmd.ExitCodeFormat"), code,
                    string.IsNullOrWhiteSpace(output) ? "—" : output)
            };
        }
    }
}
