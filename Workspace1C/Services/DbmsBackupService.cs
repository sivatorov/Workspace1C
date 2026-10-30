using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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

    public static DbmsBackupData Load() => FeatureDataStore.Load<DbmsBackupData>(FileName);

    public static void Save(DbmsBackupData data) => FeatureDataStore.Save(FileName, data);

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

    /// <summary>Создаёт копию, применяет ротацию и при необходимости выгружает в WebDAV.</summary>
    public static async Task<BackupRunResult> RunAsync(DbmsBackupProfile profile, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var result = new BackupRunResult();
        var error = Validate(profile);
        if (error is not null)
        {
            result.ErrorMessage = LocalizationManager.T(error);
            return result;
        }

        var target = Path.Combine(profile.OutputDirectory.Trim(), BuildFileName(profile, DateTime.Now));
        ProcessRunResult run;
        if (profile.Dbms == DbmsKind.PostgreSql)
        {
            Directory.CreateDirectory(profile.OutputDirectory.Trim());
            var env = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(profile.Password))
                env["PGPASSWORD"] = profile.Password;
            log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDumping"), profile.Database, target));
            run = await ProcessRunner.RunAsync(ToolPath(profile), BuildPgDumpArguments(profile, target), null,
                TimeSpan.FromHours(6), env, ct: ct).ConfigureAwait(false);
        }
        else
        {
            // Каталог копии должен существовать на стороне сервера SQL; для локального
            // сервера создаём его сами, для удалённого — это забота администратора.
            try { Directory.CreateDirectory(profile.OutputDirectory.Trim()); } catch { /* путь сервера */ }
            log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDumping"), profile.Database, target));
            run = await ProcessRunner.RunAsync(ToolPath(profile), BuildSqlCmdArguments(profile, target), null,
                TimeSpan.FromHours(6), null, ct: ct).ConfigureAwait(false);
        }

        if (!run.Success)
        {
            result.ErrorMessage = string.IsNullOrWhiteSpace(run.CombinedOutput)
                ? LocalizationManager.T("Dbms.Failed")
                : run.CombinedOutput;
            return result;
        }

        result.Success = true;
        result.CreatedFiles = new[] { target };
        log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogDone"), target));

        if (profile.KeepCount > 0)
        {
            var removed = BackupRotation.Apply(profile.OutputDirectory.Trim(), BuildFilePrefix(profile),
                FileExtension(profile), profile.KeepCount, 0);
            if (removed.Count > 0)
                log?.Invoke(string.Format(LocalizationManager.T("Dbms.LogRotated"), removed.Count));
        }

        if (!string.IsNullOrWhiteSpace(profile.WebDavUrl) && File.Exists(target))
        {
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
        return result;
    }
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
