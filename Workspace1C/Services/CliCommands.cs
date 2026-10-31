using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;
using Microsoft.Extensions.DependencyInjection;

namespace Workspace1C.Services;

/// <summary>
/// Выполнение команд CLI (функция 10). Команды получают сервисы из <see cref="AppServices"/>
/// и возвращают единый код возврата (см. <see cref="CliExitCodes"/>). Чистые части логики
/// выносятся в тестируемые методы по мере добавления команд на последующих этапах.
/// </summary>
public static class CliCommands
{
    /// <summary>Диспетчер команд: выполняет распознанную команду и возвращает код выхода.</summary>
    public static int Execute(CliParseResult parsed)
    {
        var command = CommandName(parsed.Kind);

        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
            CliOutput.WriteResult(CliResult.Failure("cli", "invalid_args", parsed.Error), parsed.Options.Json);
            return CliExitCodes.Error;
        }

        return parsed.Kind switch
        {
            CliCommandKind.List => ExecuteList(command, parsed.Options),
            CliCommandKind.Run => ExecuteRun(command, parsed.Target!, parsed.Options),
            CliCommandKind.TelegramSend => CliTelegramCommands.Send(command, parsed.Options),
            CliCommandKind.TelegramConfirm => CliTelegramCommands.Confirm(command, parsed.Options),
            // Новые команды п.6 (выпуск 1.0.0.88): MCP обрабатывается в CliEntryPoint до Execute.
            CliCommandKind.Mcp => Unsupported(command, parsed.Options),
            CliCommandKind.Update => ExecuteUpdate(command, parsed.Options),
            CliCommandKind.Eventlog => ExecuteEventlog(command, parsed.Target!, parsed.Options),
            CliCommandKind.Check => ExecuteCheck(command, parsed.Options),
            _ => Unsupported(command, parsed.Options)
        };
    }

    // --------------------------------------------------------------- список (--list)

    /// <summary>
    /// Вывод списка баз. Plain-режим сохраняет исторический формат <c>Name\tkind\tId</c>
    /// (совместимость со скриптами); при --json — детали в едином контракте.
    /// Приватные базы заблокированного профиля скрываются (0.3.9.85).
    /// </summary>
    internal static int ExecuteList(string command, CliOptions options)
    {
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var logger = AppServices.GetRequiredService<IAppLogger>();
        var profileService = AppServices.GetRequiredService<IProfileService>();
        bool IsPrivateVisible(Infobase ib) => !ib.IsPrivate || profileService.CanShowPrivateBases;

        var visible = repository.Load()
            .Where(IsPrivateVisible)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Json)
        {
            var items = visible.Select(ib => new
            {
                name = ib.Name,
                kind = KindOf(ib),
                id = ib.Id,
                group = ib.Group,
                connection = ib.Connection?.ToConnectionString() ?? string.Empty,
                tags = ib.Tags ?? new System.Collections.Generic.List<string>(),
                isFavorite = ib.IsFavorite,
                isPinned = ib.IsPinned,
                lastLaunch = ib.LastLaunchDate,
                lastBackup = ib.LastBackupUtc
            });

            CliOutput.WriteResult(CliResult.Success(command, new { bases = items }), json: true);
        }
        else
        {
            foreach (var ib in visible)
                Console.WriteLine($"{ib.Name}\t{KindOf(ib)}\t{ib.Id}");
        }

        logger.Info("[cli] Выведен список баз (--list)");
        return CliExitCodes.Success;
    }

    // ---------------------------------------------------------------- запуск (--run)

    /// <summary>
    /// Запуск базы (--run "Имя"/--base): поиск по Id, затем по имени без учёта регистра;
    /// pre/post-команды пользовательских скриптов (функция 8); история запуска.
    /// Коды возврата мигрированы на единый контракт (не найдено — 2, ошибка запуска — 1,
    /// приватная база — 1 с error.code=private_base).
    /// </summary>
    internal static int ExecuteRun(string command, string target, CliOptions options)
    {
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var logger = AppServices.GetRequiredService<IAppLogger>();
        var profileService = AppServices.GetRequiredService<IProfileService>();
        bool IsPrivateVisible(Infobase ib) => !ib.IsPrivate || profileService.CanShowPrivateBases;

        var bases = repository.Load();
        var infobase = bases.FirstOrDefault(b =>
            string.Equals(b.Id, target, StringComparison.OrdinalIgnoreCase))
            ?? bases.FirstOrDefault(b =>
                string.Equals(b.Name.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase));

        if (infobase is null)
        {
            var message = $"База не найдена: '{target}'";
            CliOutput.WriteResult(CliResult.Failure(command, "base_not_found", message), options.Json);
            logger.Warn($"[cli] База не найдена: '{target}'");
            return CliExitCodes.NotFound;
        }

        // Приватная база заблокированного профиля: запуск из CLI запрещён (0.3.9.85).
        // Разблокировать её можно только в работающем приложении паролем профиля.
        if (!IsPrivateVisible(infobase))
        {
            var message =
                $"База «{infobase.Name}» приватная: откройте приложение и разблокируйте " +
                "приватные базы паролем профиля (меню «Утилиты» → «Открыть приватные базы»).";
            CliOutput.WriteResult(CliResult.Failure(command, "private_base", message), options.Json);
            logger.Warn($"[cli] Отказ запуска приватной базы «{infobase.Name}» (профиль не разблокирован)");
            return CliExitCodes.Error;
        }

        var mode = options.Designer ? OneCLaunchMode.Configurator : OneCLaunchMode.Enterprise;
        var launcher = AppServices.GetRequiredService<IOneCLauncher>();

        // Пользовательские скрипты при запуске базы (0.3.9.98, функция №8):
        // pre-команда выполняется с ожиданием (до 30 секунд). При ошибке или
        // таймауте запуск НЕ блокируется — пишем предупреждение и продолжаем.
        if (!string.IsNullOrWhiteSpace(infobase.PreLaunchCommand))
        {
            var preOk = ExternalCommandRunner.RunAsync(
                infobase.PreLaunchCommand,
                ExternalCommandRunner.DefaultPreCommandTimeoutMs).GetAwaiter().GetResult();
            if (preOk)
            {
                // issue #80 «Настройка логирования»: запуск баз — направление CM_BASE_RUN.
                Log.Info(LogCategory.BaseRun, $"[cli] Pre-команда «{infobase.Name}» выполнена: {infobase.PreLaunchCommand}");
            }
            else
            {
                CliOutput.WriteDiagnostic(
                    $"Внимание: не удалось выполнить команду перед запуском: {infobase.PreLaunchCommand}");
                logger.Warn($"[cli] Pre-команда «{infobase.Name}» завершилась с ошибкой: {infobase.PreLaunchCommand}");
            }
        }

        var ok = launcher.Launch(infobase, mode);

        if (ok)
        {
            // Post-команда — fire-and-forget, без ожидания завершения.
            if (!string.IsNullOrWhiteSpace(infobase.PostLaunchCommand))
            {
                ExternalCommandRunner.RunDetached(infobase.PostLaunchCommand);
                // issue #80: запуск баз — направление CM_BASE_RUN.
                Log.Info(LogCategory.BaseRun, $"[cli] Запущена post-команда «{infobase.Name}»: {infobase.PostLaunchCommand}");
            }

            // История запуска: если приложение уже запущено, его сохранение
            // перезапишет файл последним — запись истории может потеряться,
            // это допустимо (полные JSON-файлы, «побеждает последний»).
            infobase.LastLaunchDate = DateTime.Now;
            infobase.AddLaunchHistory(options.Designer ? "Configurator" : "Enterprise", BuildCliLaunchDetails(infobase));
            try { repository.Save(bases); }
            catch { /* параллельная запись — не ошибка CLI-запуска */ }

            if (options.Json)
            {
                CliOutput.WriteResult(CliResult.Success(command, new
                {
                    name = infobase.Name,
                    mode = options.Designer ? "configurator" : "enterprise"
                }), json: true);
            }

            // issue #80: запуск баз — направление CM_BASE_RUN.
            Log.Info(LogCategory.BaseRun, $"[cli] Запущена «{infobase.Name}» ({mode})");
            return CliExitCodes.Success;
        }

        var failMessage = $"Не удалось запустить «{infobase.Name}»";
        CliOutput.WriteResult(CliResult.Failure(command, "launch_failed", failMessage), options.Json);
        logger.Warn($"[cli] Не удалось запустить «{infobase.Name}» ({mode})");
        return CliExitCodes.Error;
    }

    // ------------------------------------------------------------ обновление (--update)

    /// <summary>
    /// <c>--update</c> (выпуск 1.0.0.88, п.6): проверка наличия новой версии через
    /// <see cref="GitHubReleaseService"/>; при <c>--apply</c> — скачивание однофайловой
    /// сборки текущей платформы и запись маркера <see cref="PendingUpdateStore"/>,
    /// который применит новую версию при следующем запуске приложения.
    /// JSON-контракт: data.updateAvailable / latestVersion / notes / downloadUrl;
    /// при --apply дополнительно data.downloaded=true и data.path.
    /// </summary>
    internal static int ExecuteUpdate(string command, CliOptions options)
    {
        var logger = AppServices.GetRequiredService<IAppLogger>();
        var gitHub = AppServices.GetRequiredService<GitHubReleaseService>();

        ReleaseInfo? release = null;
        try { release = gitHub.GetLatestReleaseAsync().GetAwaiter().GetResult(); }
        catch { /* сообщение об ошибке ниже */ }

        if (release is null)
        {
            CliOutput.WriteResult(
                CliResult.Failure(command, "check_failed", "Не удалось проверить наличие обновления (сеть или GitHub недоступны)."),
                options.Json);
            logger.Warn("[cli] Не удалось проверить наличие обновления (--update)");
            return CliExitCodes.Error;
        }

        var currentVersion = VersionInfo.Display();
        if (!GitHubReleaseService.IsNewerThan(release, currentVersion))
        {
            CliOutput.WriteResult(CliResult.Success(command, new
            {
                updateAvailable = false,
                currentVersion,
                message = "Версия актуальна."
            }), options.Json);
            return CliExitCodes.Success;
        }

        // ---- Обновление доступно: без --apply только информируем. ----
        if (!options.Apply)
        {
            CliOutput.WriteResult(CliResult.Success(command, new
            {
                updateAvailable = true,
                currentVersion,
                latestVersion = ReleaseVersion(release),
                notes = release.Body ?? string.Empty,
                downloadUrl = release.DownloadUrl,
                htmlUrl = release.HtmlUrl
            }), options.Json);

            if (!options.Json)
            {
                Console.WriteLine($"Доступна новая версия: {ReleaseVersion(release)} (текущая {currentVersion}); " +
                                  "для загрузки выполните: --update --apply");
            }

            logger.Info($"[cli] Найдено обновление {ReleaseVersion(release)} (--update)");
            return CliExitCodes.Success;
        }

        // ---- --apply: скачиваем сборку текущей платформы + маркер применения. ----
        var (downloadOk, path, error) = DownloadUpdateFile(release);
        if (!downloadOk)
        {
            CliOutput.WriteResult(CliResult.Failure(
                command, "download_failed",
                string.IsNullOrWhiteSpace(error) ? "Не удалось скачать обновление." : "Не удалось скачать обновление: " + error),
                options.Json);
            logger.Warn("[cli] Не удалось скачать обновление (--update --apply): " + error);
            return CliExitCodes.Error;
        }

        // Маркер «планируется обновление»: новая версия отправит уведомление при старте,
        // а сам файл пользователь/планировщик подменит (или запустит при следующем старте).
        try
        {
            AppServices.GetRequiredService<PendingUpdateStore>()
                .Write(release.TagName, release.Body, release.HtmlUrl);
        }
        catch { /* маркер сервисный */ }

        CliOutput.WriteResult(CliResult.Success(command, new
        {
            updateAvailable = true,
            currentVersion,
            latestVersion = ReleaseVersion(release),
            downloaded = true,
            path,
            message = "Новая версия скачана: закройте приложение и замените исполняемый файл (или запустите скачанный)."
        }), options.Json);

        logger.Info($"[cli] Обновление {ReleaseVersion(release)} скачано: {path}");
        return CliExitCodes.Success;
    }

    /// <summary>Версия выпуска для контракта: тег без ведущего «v».</summary>
    private static string ReleaseVersion(ReleaseInfo release) =>
        (release.TagName ?? string.Empty).TrimStart('v', 'V');

    /// <summary>
    /// Скачивает однофайловую сборку текущей платформы во временный каталог
    /// <c>%TEMP%\Workspace1C\update\<версия>\</c>. Возвращает путь файла.
    /// </summary>
    private static (bool Ok, string? Path, string Error) DownloadUpdateFile(ReleaseInfo release)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(release.DownloadUrl))
                return (false, null, "no_download_url");

            var safeTag = string.Concat(
                (release.TagName ?? "unknown").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
            var dir = Path.Combine(Path.GetTempPath(), "Workspace1C", "update", safeTag);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, OperatingSystem.IsWindows() ? "Workspace1C.exe" : "Workspace1C");

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Workspace1C/1.0");
            http.Timeout = TimeSpan.FromMinutes(20);
            using var response = http.GetAsync(release.DownloadUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return (false, null, "http_" + (int)response.StatusCode);
            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var file = File.Create(dest);
            // Синхронное копирование с буфером 1 МБ (аудит производительности):
            // CopyToAsync().GetAwaiter().GetResult() давал оверхед async-машины без
            // пользы (метод и так блокирующий), а дефолтный буфер 8 КБ тормозил
            // копирование больших dt-файлов обновления.
            stream.CopyTo(file, bufferSize: 1024 * 1024);
            return (true, dest, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    // ------------------------------------------------------------ журнал (--eventlog)

    /// <summary>
    /// <c>--eventlog</c> (выпуск 1.0.0.88, п.6): выборка записей журнала регистрации
    /// файловой базы (--target <имя базы или каталог ИБ>) с фильтрами периода,
    /// пользователя, события и текста. Вывод — единый JSON-контракт (data.entries).
    /// </summary>
    internal static int ExecuteEventlog(string command, string target, CliOptions options)
    {
        var logger = AppServices.GetRequiredService<IAppLogger>();

        // Источник журнала: каталог ИБ (1Cv8.1CD рядом) / каталог журнала либо база по имени/id.
        JournalLocation location = ResolveJournalLocation(target);
        if (!location.IsValid)
        {
            CliOutput.WriteResult(CliResult.Failure(
                command, "eventlog_not_found", location.ErrorMessage ?? "Журнал регистрации не найден."),
                options.Json);
            logger.Warn($"[cli] Журнал регистрации недоступен: {location.ErrorMessage}");
            return CliExitCodes.Error;
        }

        var query = new EventLogQuery
        {
            From = options.From is null
                ? DateTime.Today
                : (DateTime.TryParse(options.From, out var from) ? from : DateTime.Today),
            To = options.To is null
                ? DateTime.MaxValue
                : (DateTime.TryParse(options.To, out var to) ? to : DateTime.MaxValue),
            User = options.User ?? string.Empty,
            Event = options.Event ?? string.Empty,
            Text = options.Text ?? string.Empty,
            MaxRows = options.Limit > 0 ? options.Limit : EventLogQuery.DefaultMaxRows,
            IncludeInfo = true,
            IncludeWarning = true,
            IncludeError = true,
            IncludeNote = true
        };

        var result = EventLogQueryService.Query(location, query);
        if (result.Error is not null)
        {
            CliOutput.WriteResult(CliResult.Failure(
                command, "read_failed", "Ошибка чтения журнала регистрации: " + result.Error), options.Json);
            return CliExitCodes.Error;
        }

        if (options.Json)
        {
            CliOutput.WriteResult(CliResult.Success(command, new
            {
                location = location.LogDir,
                format = location.FormatDisplay,
                truncated = result.Truncated,
                count = result.Entries.Count,
                entries = result.Entries.Select(e => new
                {
                    timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    severity = e.Severity.ToString().ToLowerInvariant(),
                    user = e.User,
                    computer = e.Computer,
                    application = e.Application,
                    @event = string.IsNullOrEmpty(e.EventName) ? e.EventCode.ToString() : e.EventName,
                    metadata = string.IsNullOrEmpty(e.MetadataName) ? null : e.MetadataName,
                    comment = e.Comment,
                    data = e.Data,
                    session = e.Session == 0 ? (long?)null : e.Session
                }).ToList()
            }), json: true);
        }
        else
        {
            foreach (var e in result.Entries)
            {
                Console.WriteLine(
                    $"{e.Timestamp:yyyy-MM-dd HH:mm:ss}\t{e.Severity}\t{e.User}\t{e.EventName}\t{e.Comment}");
            }
        }

        logger.Info($"[cli] Выборка журнала регистрации: {result.Entries.Count} записей (--eventlog)");
        return CliExitCodes.Success;
    }

    /// <summary>Определяет каталог журнала регистрации по цели --target (база/каталог ИБ).</summary>
    internal static JournalLocation ResolveJournalLocation(string target)
    {
        if (Directory.Exists(target))
        {
            // Цель — каталог файловой ИБ (рядом 1Cv8.1CD) или сам каталог журнала.
            if (File.Exists(System.IO.Path.Combine(target, "1Cv8.1CD")))
                return LgdJournalLocator.FromDirectory(
                    System.IO.Path.Combine(target, LgdJournalLocator.LogDirectoryName));
            if (Directory.Exists(System.IO.Path.Combine(target, LgdJournalLocator.LogDirectoryName)))
                return LgdJournalLocator.FromDirectory(
                    System.IO.Path.Combine(target, LgdJournalLocator.LogDirectoryName));
            return LgdJournalLocator.FromDirectory(target);
        }

        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var infobase = repository.Load().FirstOrDefault(b =>
                string.Equals(b.Id, target, StringComparison.OrdinalIgnoreCase))
            ?? repository.Load().FirstOrDefault(b =>
                string.Equals(b.Name?.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase));
        return infobase is null
            ? new JournalLocation { ErrorMessage = $"База не найдена: '{target}'" }
            : LgdJournalLocator.Resolve(infobase);
    }

    // -------------------------------------------------------------- проверка (--check)

    /// <summary>
    /// <c>--check</c> (выпуск 1.0.0.88, п.6): диагностика окружения и конфигурации —
    /// версия приложения, каталог данных, установленные платформы 1С, наличие rac/ring,
    /// запущенные процессы 1С, службы из справочника и права текущего пользователя.
    /// Вывод — единый JSON-контракт (data).
    /// </summary>
    internal static int ExecuteCheck(string command, CliOptions options)
    {
        var profileService = AppServices.GetRequiredService<IProfileService>();

        var platforms = new System.Collections.Generic.List<object>();
        foreach (var architecture in new[] { "64", "32" })
        {
            foreach (var (version, binDir) in PlatformVersionService.FindPlatformVersionDirs(architecture))
                platforms.Add(new { version, architecture = architecture + "-bit", bin = binDir });
        }

        var racPath = OneCPlatformLocator.FindRacExecutable();
        var ringPath = racPath is null
            ? null
            : OneCPlatformLocator.FindInBinDir(System.IO.Path.GetDirectoryName(racPath)!, "ring");

        var runningProcesses = 0;
        try
        {
            runningProcesses = AppServices.GetRequiredService<IRunningInfobasesService>().GetRunning().Count;
        }
        catch { /* инспектор может быть недоступен в окружении */ }

        var services = new System.Collections.Generic.List<object>();
        try
        {
            var names = AppServices.GetRequiredService<IServicesStore>().Load().Services ?? new System.Collections.Generic.List<string>();
            var controller = AppServices.GetRequiredService<IServiceControllerService>();
            foreach (var name in names)
            {
                try { services.Add(new { name, status = controller.GetStatus(name).Status.ToString() }); }
                catch { services.Add(new { name, status = "error" }); }
            }
        }
        catch { /* недоступен список служб */ }

        var data = new
        {
            version = VersionInfo.Display(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            appDataDirectory = string.IsNullOrWhiteSpace(profileService.CurrentProfileDataDirectory)
                ? PlatformPaths.AppDataDirectory
                : profileService.CurrentProfileDataDirectory,
            platforms,
            rac = racPath is null ? null : new { found = true, path = racPath },
            ring = ringPath is null ? null : new { found = true, path = ringPath },
            runningProcesses,
            services,
            isElevated = IsAdministrator(),
            rightsNote = string.Empty
        };

        CliOutput.WriteResult(CliResult.Success(command, data), options.Json);

        if (!options.Json)
        {
            var logger = AppServices.GetRequiredService<IAppLogger>();
            Console.WriteLine($"Версия: {VersionInfo.Display()}");
            Console.WriteLine($"Платформ 1С: {platforms.Count}, rac: {(racPath is null ? "нет" : racPath)}, ring: {(ringPath is null ? "нет" : ringPath)}");
            Console.WriteLine($"Процессов 1С: {runningProcesses}, служб в справочнике: {services.Count}, повышенные права: {(IsAdministrator() ? "да" : "нет")}");
        }

        return CliExitCodes.Success;
    }

    /// <summary>Проверка повышенных прав текущего процесса (Windows — роль администратора, Linux — root).</summary>
    internal static bool IsAdministrator()
    {
#if WINDOWS
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
#else
        try
        {
            // Linux: euid 0 — процесс под root (sudo/pkexec); обычный пользователь — false.
            return System.IO.File.Exists("/proc/self/status")
                && (System.IO.File.ReadAllText("/proc/self/status")
                    .Split('\n')
                    .FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal))
                    ?.Split('\t', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).FirstOrDefault() == "0");
        }
        catch { return false; }
#endif
    }

    // ------------------------------------------------------------ не реализовано

    /// <summary>Команды будущих этапов цикла (add/backup/status/export/help) — до реализации
    /// парсером не распознаются, поэтому сюда не попадают; ветка — страховка диспетчера.</summary>
    private static int Unsupported(string command, CliOptions options)
    {
        CliOutput.WriteResult(
            CliResult.Failure(command, "invalid_args", $"Команда не поддерживается: {command}"),
            options.Json);
        return CliExitCodes.Error;
    }

    /// <summary>Тип подключения базы для вывода: server/web/file.</summary>
    internal static string KindOf(Infobase ib) => ib.Connection?.Type switch
    {
        ConnectionType.ClientServer => "server",
        ConnectionType.WebServer => "web",
        _ => "file"
    };

    /// <summary>Имя команды для контракта вывода (нижний регистр; составные — с подчёркиванием).</summary>
    internal static string CommandName(CliCommandKind kind) => kind switch
    {
        CliCommandKind.TelegramSend => "telegram_send",
        CliCommandKind.TelegramConfirm => "telegram_confirm",
        // Составные имена новых команд п.6 (выпуск 1.0.0.88) — с подчёркиванием.
        _ => kind.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Детали истории запуска из CLI с маркером пользовательских команд (0.3.9.98):
    /// например «cli; pre: ras connect …; post: start …».
    /// </summary>
    private static string BuildCliLaunchDetails(Infobase infobase)
    {
        var parts = new System.Collections.Generic.List<string> { "cli" };
        if (!string.IsNullOrWhiteSpace(infobase.PreLaunchCommand))
            parts.Add("pre: " + infobase.PreLaunchCommand);
        if (!string.IsNullOrWhiteSpace(infobase.PostLaunchCommand))
            parts.Add("post: " + infobase.PostLaunchCommand);
        return string.Join("; ", parts);
    }
}