using System.Globalization;
using System.IO;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Настройки версионирования конфигурации одной базы в Git (выпуск 1.0.0.69).
/// </summary>
public sealed class GitRepoSettings
{
    public string InfobaseId { get; set; } = string.Empty;

    /// <summary>Каталог Git-репозитория (создаётся и инициализируется при первом запуске).</summary>
    public string RepoPath { get; set; } = string.Empty;

    /// <summary>Подкаталог для XML-файлов конфигурации внутри репозитория.</summary>
    public string SubFolder { get; set; } = "src";

    /// <summary>Шаблон сообщения коммита: {base}, {date}, {version}.</summary>
    public string MessageTemplate { get; set; } = "Конфигурация «{base}» от {date}";

    /// <summary>Выполнять git push после коммита.</summary>
    public bool Push { get; set; }
}

/// <summary>Файл настроек версионирования (<c>git_versioning.json</c>).</summary>
public sealed class GitVersioningData
{
    public List<GitRepoSettings> Repos { get; set; } = new();

    public GitRepoSettings? Find(string? infobaseId) =>
        string.IsNullOrWhiteSpace(infobaseId)
            ? null
            : Repos.FirstOrDefault(r => string.Equals(r.InfobaseId, infobaseId, StringComparison.Ordinal));

    public void Upsert(GitRepoSettings settings)
    {
        Repos.RemoveAll(r => string.Equals(r.InfobaseId, settings.InfobaseId, StringComparison.Ordinal));
        Repos.Add(settings);
    }
}

/// <summary>Итог выгрузки конфигурации в Git.</summary>
public sealed class GitVersioningResult
{
    public bool Success { get; init; }

    /// <summary>Создан ли коммит (false — изменений не было).</summary>
    public bool Committed { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Версионирование конфигурации в Git (выпуск 1.0.0.69): выгрузка конфигурации базы
/// в XML-файлы (<c>/DumpConfigToFiles</c>) в подкаталог репозитория, затем
/// <c>git add -A</c>, <c>git commit</c> и при необходимости <c>git push</c>.
/// Подкаталог очищается перед выгрузкой, поэтому удалённые объекты тоже
/// попадают в историю. Нужен установленный Git (в PATH).
/// </summary>
public static class GitVersioningService
{
    public const string FileName = "git_versioning.json";

    public static GitVersioningData Load() => FeatureDataStore.Load<GitVersioningData>(FileName);

    public static void Save(GitVersioningData data) => FeatureDataStore.Save(FileName, data);

    /// <summary>Сообщение коммита по шаблону.</summary>
    public static string FormatMessage(string? template, Infobase infobase, DateTime now)
    {
        var text = string.IsNullOrWhiteSpace(template) ? "{base} {date}" : template!;
        return text
            .Replace("{base}", infobase.Name ?? string.Empty)
            .Replace("{date}", now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture))
            .Replace("{version}", infobase.ConfigurationVersion ?? string.Empty)
            .Trim();
    }

    /// <summary>Подкаталог выгрузки внутри репозитория (защита от выхода за пределы).</summary>
    public static string ResolveDumpDirectory(GitRepoSettings settings)
    {
        var repo = Path.GetFullPath(settings.RepoPath).TrimEnd('/', '\\');
        var sub = string.IsNullOrWhiteSpace(settings.SubFolder) ? "src" : settings.SubFolder.Trim().Trim('/', '\\');
        var full = Path.GetFullPath(Path.Combine(repo, sub)).TrimEnd('/', '\\');
        // Подкаталог обязан лежать строго внутри репозитория: он очищается перед выгрузкой,
        // и «..» или пустое значение не должны удалить сам репозиторий или чужие файлы.
        if (!full.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || full.EndsWith(Path.DirectorySeparatorChar + ".git", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationManager.T("Git.BadSubFolder"));
        return full;
    }

    public static async Task<GitVersioningResult> RunAsync(Infobase infobase, GitRepoSettings settings,
        Action<string>? log = null, CancellationToken ct = default)
    {
        void Log(string message) => log?.Invoke(message);

        if (infobase is null || settings is null || string.IsNullOrWhiteSpace(settings.RepoPath))
            return new GitVersioningResult { Message = LocalizationManager.T("Git.NotConfigured") };

        string dumpDir;
        try
        {
            Directory.CreateDirectory(settings.RepoPath);
            dumpDir = ResolveDumpDirectory(settings);
        }
        catch (Exception ex)
        {
            return new GitVersioningResult { Message = ex.Message };
        }

        var version = await Git(settings.RepoPath, ct, "--version").ConfigureAwait(false);
        if (!version.Success)
            return new GitVersioningResult { Message = LocalizationManager.T("Git.NotInstalled") + " " + version.CombinedOutput };

        if (!Directory.Exists(Path.Combine(settings.RepoPath, ".git")))
        {
            Log(LocalizationManager.T("Git.LogInit"));
            var init = await Git(settings.RepoPath, ct, "init").ConfigureAwait(false);
            if (!init.Success)
                return new GitVersioningResult { Message = init.CombinedOutput };
        }

        Log(string.Format(LocalizationManager.T("Git.LogDumping"), infobase.Name, dumpDir));
        try
        {
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, recursive: true);
            Directory.CreateDirectory(dumpDir);
        }
        catch (Exception ex)
        {
            return new GitVersioningResult { Message = ex.Message };
        }

        if (!DesignerCommandRunner.IsSafeValue(dumpDir))
            return new GitVersioningResult { Message = LocalizationManager.T("Git.BadSubFolder") };
        var dump = await DesignerCommandRunner.RunAsync(infobase, $"/DumpConfigToFiles\"{dumpDir}\"", null, ct)
            .ConfigureAwait(false);
        if (!dump.Success)
            return new GitVersioningResult { Message = dump.Error ?? LocalizationManager.T("Git.DumpFailed") };
        if (!Directory.EnumerateFileSystemEntries(dumpDir).Any())
            return new GitVersioningResult { Message = LocalizationManager.T("Git.DumpFailed") };

        var add = await Git(settings.RepoPath, ct, "add", "-A").ConfigureAwait(false);
        if (!add.Success)
            return new GitVersioningResult { Message = add.CombinedOutput };

        var status = await Git(settings.RepoPath, ct, "status", "--porcelain").ConfigureAwait(false);
        if (status.Success && string.IsNullOrWhiteSpace(status.StdOut))
        {
            Log(LocalizationManager.T("Git.LogNoChanges"));
            return new GitVersioningResult { Success = true, Message = LocalizationManager.T("Git.LogNoChanges") };
        }
        var changed = status.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Log(string.Format(LocalizationManager.T("Git.LogChanged"), changed));

        var message = FormatMessage(settings.MessageTemplate, infobase, DateTime.Now);
        var args = new List<string>();
        var userName = await Git(settings.RepoPath, ct, "config", "user.name").ConfigureAwait(false);
        if (!userName.Success || string.IsNullOrWhiteSpace(userName.StdOut))
        {
            // Автор по умолчанию, если в Git не настроены user.name/user.email.
            args.AddRange(new[] { "-c", "user.name=Workspace 1C", "-c", "user.email=workspace1c@localhost" });
        }
        args.AddRange(new[] { "commit", "-m", message });
        var commit = await ProcessRunner.RunAsync("git", args, settings.RepoPath, TimeSpan.FromMinutes(10), null, ct: ct)
            .ConfigureAwait(false);
        if (!commit.Success)
            return new GitVersioningResult { Message = commit.CombinedOutput };
        Log(string.Format(LocalizationManager.T("Git.LogCommitted"), message));

        if (settings.Push)
        {
            Log(LocalizationManager.T("Git.LogPushing"));
            var push = await Git(settings.RepoPath, ct, "push").ConfigureAwait(false);
            if (!push.Success)
                return new GitVersioningResult
                {
                    Committed = true,
                    Message = LocalizationManager.T("Git.PushFailed") + " " + push.CombinedOutput
                };
        }

        return new GitVersioningResult
        {
            Success = true,
            Committed = true,
            Message = string.Format(LocalizationManager.T("Git.Done"), changed)
        };
    }

    private static Task<ProcessRunResult> Git(string repo, CancellationToken ct, params string[] args) =>
        ProcessRunner.RunAsync("git", args, repo, TimeSpan.FromMinutes(10), null, ct: ct);
}
