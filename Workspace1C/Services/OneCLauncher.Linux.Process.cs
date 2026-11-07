#if LINUX
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    public static partial class OneCLauncher
    {
        // ====================================================================
        // Запуск базы
        // ====================================================================

        public static bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false, bool monopol = false)
        {
            if (mode == OneCLaunchMode.Configurator)
                return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase), runAsAdmin);

            if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
                return LaunchWebClient(infobase);

            if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, null, GetArchitecture(infobase), runAsAdmin, monopol);

            if (string.Equals(infobase.LaunchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Ordinary, GetArchitecture(infobase), runAsAdmin, monopol);

            if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin, monopol);

            return Launch(infobase, mode, OneCClientType.Thin, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin, monopol);
        }

        public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType,
            OneCArchitecture architecture, bool runAsAdmin = false, bool monopol = false)
            => Launch(infobase, mode, clientType, null, architecture, runAsAdmin, monopol);

        /// <summary>
        /// Запускает платформу 1С; <paramref name="monopol"/> — монопольный запуск
        /// Предприятия (/M, issue #85, v1.0.1.43), применяется только в режиме
        /// «Предприятие» (см. <see cref="BuildArguments"/>).
        /// </summary>
        public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType,
            OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false, bool monopol = false)
        {
            // База, расположенная на веб-сервере, подключается только тонким клиентом (/WS).
            // 1cv8 (толстый клиент) не понимает /WS и при запуске открывает стандартное
            // окно со списком информационных баз вместо подключения к базе.
            if (infobase.Connection.Type == ConnectionType.WebServer)
            {
                clientType = OneCClientType.Thin;
                runMode ??= OneCRunMode.Managed;
            }

            var exePath = FindExecutable(infobase.PlatformVersion, architecture, clientType, mode);
            if (string.IsNullOrEmpty(exePath))
            {
                var archLabel = architecture == OneCArchitecture.x64
                    ? LocalizationManager.T("Launcher.Bit64")
                    : LocalizationManager.T("Launcher.Bit32");
                var versionHint = string.IsNullOrWhiteSpace(infobase.PlatformVersion)
                    ? LocalizationManager.T("Launcher.PlatformVersionHint")
                    : string.Format(LocalizationManager.T("Launcher.RequestedVersionFormat"), infobase.PlatformVersion);
                var logger = GetLogger();
                logger?.Warn(string.Format("{0} ({1}). {2}",
                    LocalizationManager.T("Launcher.PlatformNotFoundTitle"),
                    archLabel,
                    versionHint));
                return false;
            }

            // Запуск идёт через ProcessStartInfo.Arguments (строку), а не ArgumentList: .NET сам
            // разбивает строку по правилам CommandLineToArgvW, снимая внешние кавычки у /N"user"
            // и /F"path". Для 1С это и есть рабочий, проверенный формат. Перевод на ArgumentList
            // передавал бы кавычки платформе дословно — поведение 1С при этом не гарантировано,
            // поэтому осознанно не переводим (защита от инъекции реализована на уровне значений,
            // см. IsSafeCliValue).
            var arguments = BuildArguments(infobase, mode, clientType, runMode, monopol);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };
                LinuxProcessEnvironment.Start(psi);
                infobase.LastLaunchDate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                GetLogger()?.Error(string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message), ex);
                return false;
            }
        }

        private static bool LaunchWebClient(Infobase infobase)
        {
            var conn = infobase.Connection;
            string url;
            if (conn.Type == ConnectionType.WebServer)
            {
                if (string.IsNullOrWhiteSpace(conn.WebUrl))
                    return false;
                url = conn.WebUrl;
            }
            else if (conn.Type == ConnectionType.ClientServer)
            {
                url = $"http://{conn.Server}/{conn.DatabaseName}";
            }
            else
            {
                return false;
            }

            try
            {
                LinuxProcessEnvironment.Start(new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false, ArgumentList = { url } });
                infobase.LastLaunchDate = DateTime.Now;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ====================================================================
        // Поиск исполняемого файла
        // ====================================================================

        /// <summary>Имена бинарников 1С на Linux (без .exe).</summary>
        private static string[] GetBinaryNames(OneCArchitecture architecture, OneCClientType? clientType, OneCLaunchMode mode)
        {
            if (mode == OneCLaunchMode.Configurator)
                return new[] { "1cv8" };
            // Тонкий клиент или автоматический выбор (issue #245): приоритет 1cv8c,
            // если он доступен; толстый 1cv8 — только как запасной.
            if (clientType is null || clientType == OneCClientType.Thin)
                return new[] { "1cv8c", "1cv8" };
            // Толстый клиент.
            return new[] { "1cv8" };
        }

        /// <summary>Ищет исполняемый файл 1cv8/1cv8c нужной версии и разрядности.</summary>
        private static string? FindExecutable(
            string version,
            OneCArchitecture architecture,
            OneCClientType? clientType = null,
            OneCLaunchMode mode = OneCLaunchMode.Enterprise)
        {
            PlatformVersionService.ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
            if (string.IsNullOrWhiteSpace(cleanVersion))
                cleanVersion = string.Empty;

            var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";
            var exeNames = GetBinaryNames(architecture, clientType, mode);

            // 1. Конкретная версия в bin\.
            if (!string.IsNullOrWhiteSpace(cleanVersion))
            {
                var versionBinDir = PlatformVersionService.ResolveVersionBinDirectory(cleanVersion, archKey);
                if (versionBinDir != null)
                {
                    foreach (var exeName in exeNames)
                    {
                        var candidate = Path.Combine(versionBinDir, exeName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            // 2. Любая установленная версия нужной разрядности (новейшая).
            //    Если запрошена конкретная версия, запасной поиск ограничивается ТОЛЬКО
            //    соответствующими ей вариантами (полная — точным совпадением, частичная —
            //    префиксом с выбором новейшей) и не выбирает произвольную новейшую —
            //    иначе запускалась бы совсем не та версия (issues #29, #142, #28).
            string? best = null;
            string bestDir = string.Empty;
            foreach (var (verName, binDir) in PlatformVersionService.FindPlatformVersionDirs(archKey))
            {
                if (!string.IsNullOrWhiteSpace(cleanVersion) &&
                    !VersionMatches(cleanVersion, verName))
                {
                    continue;
                }

                string? chosen = null;
                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(binDir, exeName);
                    if (File.Exists(candidate))
                    {
                        chosen = candidate;
                        break;
                    }
                }
                if (chosen is null)
                    continue;
                if (best is null || PlatformVersionService.CompareVersionStrings(verName, bestDir) > 0)
                {
                    best = chosen;
                    bestDir = verName;
                }
            }

            if (best != null)
                return best;

            // 3. Симлинк /usr/bin/1cv8 или ~/.1cv8/1cv8.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var dir in new[] { "/usr/bin", string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".1cv8") })
            {
                if (dir is null)
                    continue;
                foreach (var exeName in exeNames)
                {
                    var path = Path.Combine(dir, exeName);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        /// <summary>Путь к 1cv8 (толстый клиент) для ярлыка / пакетных операций.</summary>
        public static string? ResolveThickClientExe(Infobase infobase)
        {
            var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            return FindExecutable(infobase.PlatformVersion, arch, OneCClientType.Thick, OneCLaunchMode.Enterprise)
                ?? FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
        }

        // ====================================================================
        // Запуск по ссылке / открытие URL
        // ====================================================================

        /// <summary>
        /// Запускает 1С по ссылке на информационную базу (аналог «Перейти по ссылке»).
        /// Поддерживаемые форматы:
        /// <list type="bullet">
        /// <item>Ссылка-протокол «e1c://…» — открывается системным обработчиком (xdg-open);</item>
        /// <item>Веб-клиент «http(s)://…» — в браузере по умолчанию (xdg-open);</item>
        /// <item>Файловая база «/path» или «File="/path"» — через платформу 1cv8;</item>
        /// <item>Клиент-сервер «server\База» или «Srvr="...";Ref="..."» — через 1cv8.</item>
        /// </list>
        /// </summary>
        /// <param name="link">Ссылка на информационную базу.</param>
        /// <returns>true, если запуск успешно инициирован.</returns>
        public static bool LaunchByLink(string link)
        {
            var value = (link ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Веб-ссылки и ссылки-протоколы обрабатывает системный обработчик (xdg-open).
            // Для web-ссылок отбрасываем завершающий сегмент локали вида /ru_RU/
            // (issue #332), чтобы ссылка открывалась как веб-база, а не страница локали.
            if (value.StartsWith("e1c:", StringComparison.OrdinalIgnoreCase))
            {
                return OpenUrl(value);
            }
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return OpenUrl(StripWebLocaleSegment(value));
            }

            // Файловая / клиент-серверная база — запускаем через платформу 1С.
            var args = ParseLinkArguments(value);
            if (args is null)
                return false;

            var exe = FindExecutable(string.Empty, OneCArchitecture.x64, OneCClientType.Thick, OneCLaunchMode.Enterprise)
                      ?? FindExecutable(string.Empty, OneCArchitecture.x86, OneCClientType.Thick, OneCLaunchMode.Enterprise);
            if (string.IsNullOrEmpty(exe))
                return false;

            try
            {
                LinuxProcessEnvironment.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"ENTERPRISE {args}",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Открывает URL в приложении по умолчанию (xdg-open).</summary>
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            try
            {
                LinuxProcessEnvironment.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { url }
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Разбирает ссылку на файловую/клиент-серверную базу в аргументы командной
        /// строки 1С (/F /S). Возвращает null, если формат не распознан.
        /// </summary>
        private static string? ParseLinkArguments(string value)
        {
            // 1. Строка подключения: Srvr="...";Ref="..."
            //    Кавычка внутри значения экранируется удвоением, поэтому шаблон допускает «""»
            //    внутри и разворачивает его обратно (см. UnescapeConnectValue).
            var srvr = Regex.Match(value, @"Srvr\s*=\s*""(?<s>(?:[^""]|"""")*)""", RegexOptions.IgnoreCase);
            if (srvr.Success)
            {
                var re = Regex.Match(value, @"Ref\s*=\s*""(?<r>(?:[^""]|"""")*)""", RegexOptions.IgnoreCase);
                var server = UnescapeConnectValue(srvr.Groups["s"].Value).Trim();
                var db = re.Success ? UnescapeConnectValue(re.Groups["r"].Value).Trim() : string.Empty;
                // Значения идут в /S"…" по грамматике ключа (не строки подключения): значение с «"»
                // недопустимо (см. IsSafeCliValue) — отказываемся от запуска вместо инъекции /ключа.
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(db) ||
                    !IsSafeCliValue(server) || !IsSafeCliValue(db))
                    return null;
                return $"/S \"{server}\\{db}\"";
            }

            // 2. Файловая база: File="..." или File=...
            //    Кавычка внутри пути экранируется удвоением (симметрично записи), поэтому шаблон
            //    допускает «""» внутри и разворачивает его обратно (см. UnescapeConnectValue).
            var file = Regex.Match(value,
                @"File\s*=\s*""(?<f>(?:[^""]|"""")*)""|File\s*=\s*(?<f>[^;]+)", RegexOptions.IgnoreCase);
            if (file.Success)
            {
                var path = UnescapeConnectValue(file.Groups["f"].Value).Trim();
                if (string.IsNullOrWhiteSpace(path) || !IsSafeCliValue(path))
                    return null;
                return $"/F \"{path}\"";
            }

            // 3. Клиент-серверная: server\База (обратный слэш, но не существующий каталог).
            if (value.Contains('\\'))
            {
                if (Directory.Exists(value))
                    return IsSafeCliValue(value) ? $"/F \"{value}\"" : null;
                var sep = value.IndexOf('\\');
                var server = value.Substring(0, sep).Trim();
                var db = value.Substring(sep + 1).Trim();
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(db) ||
                    !IsSafeCliValue(server) || !IsSafeCliValue(db))
                    return null;
                return $"/S \"{server}\\{db}\"";
            }

            // 4. Простой путь к существующему каталогу файловой базы.
            if (Directory.Exists(value))
                return IsSafeCliValue(value) ? $"/F \"{value}\"" : null;

            return null;
        }

        // ====================================================================
        // Создание информационной базы
        // ====================================================================

        public static (bool Ok, string? Error) CreateInfoBase(
            string platformVersion,
            bool isFile,
            string? filePath,
            string? server,
            string? databaseName,
            string? templatePath = null,
            string? dbms = null,
            string? dbServer = null,
            string? dbName = null,
            string? dbUser = null,
            string? dbPassword = null,
            bool createSqlDatabase = false,
            bool blockScheduledJobs = false,
            bool forbidSpeechRecognition = false,
            int serverPort = 0,
            int timeoutMs = 5 * 60 * 1000)
        {
            PlatformVersionService.ParseVariant(platformVersion, out var version, out var arch);
            var exe = FindExecutable(version, arch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86,
                OneCClientType.Thick, OneCLaunchMode.Configurator);
            if (string.IsNullOrEmpty(exe))
                return (false, LocalizationManager.T("Launcher.CreateExeNotFoundLinux"));

            string connectionString;
            // Каталог, созданный только что под файловую базу. Запоминаем его, чтобы удалить
            // при неудачной попытке создания ИБ (issue #77): иначе пустой каталог остаётся на диске.
            string? createdDirPath = null;
            if (isFile)
            {
                var path = (filePath ?? "").Trim().TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(path))
                    return (false, LocalizationManager.T("Launcher.CreateFileDirNotSpecified"));
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                        createdDirPath = path;
                    }
                }
                catch (Exception ex)
                {
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateDirCreateFailedFormat"), path, ex.Message));
                }
                connectionString = $"File=\"{EscapeConnectValue(path)}\"";
            }
            else
            {
                var srv = (server ?? "").Trim();
                var db = (databaseName ?? "").Trim();
                if (string.IsNullOrEmpty(srv) || string.IsNullOrEmpty(db))
                    return (false, LocalizationManager.T("Launcher.CreateServerOrDbNotSpecified"));

                // Строка подключения собирается общим чистым helper'ом (OneCLauncher.Create.cs):
                // одна реализация для Windows и Linux + юнит-тесты (см. issue #307).
                connectionString = BuildClientServerCreateConnectionString(
                    srv,
                    db,
                    serverPort: serverPort,
                    dbms: dbms,
                    dbServer: dbServer,
                    dbName: dbName,
                    dbUser: dbUser,
                    dbPassword: dbPassword,
                    createSqlDatabase: createSqlDatabase,
                    blockScheduledJobs: blockScheduledJobs,
                    forbidSpeechRecognition: forbidSpeechRecognition);
            }

            var args = new List<string> { "CREATEINFOBASE", connectionString };
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                if (!File.Exists(templatePath))
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateTemplateNotFoundFormat"), templatePath));
                }
                // /UseTemplate"…" — это ключ командной строки, а не строка подключения: кавычку внутри
                // пути здесь экранировать удвоением нельзя (см. IsSafeCliValue), поэтому при «"» в пути
                // отказываемся от создания, а не пытаемся «экранировать».
                if (!IsSafeCliValue(templatePath))
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateTemplateInvalidPathFormat"), templatePath));
                }
                args.Add($"/UseTemplate\"{templatePath}\"");
            }
            args.Add("/DisableStartupDialogs");
            args.Add("/DisableStartupMessages");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                };
                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                using var proc = LinuxProcessEnvironment.Start(psi);
                if (proc is null)
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, LocalizationManager.T("Launcher.CreateProcessFailed"));
                }

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    CleanupCreatedDir(createdDirPath);
                    return (false, LocalizationManager.T("Launcher.CreateTimeout"));
                }

                if (proc.ExitCode != 0)
                {
                    var err = "";
                    try { err = proc.StandardError.ReadToEnd(); } catch { }
                    CleanupCreatedDir(createdDirPath);
                    var message = string.Format(
                        LocalizationManager.T("Launcher.CreateExitCodeFormat"),
                        proc.ExitCode,
                        err,
                        exe,
                        string.Join(" ", args));
                    return (false, SensitiveDataMasker.MaskDbPassword(message));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                CleanupCreatedDir(createdDirPath);
                var message = string.Format(
                    LocalizationManager.T("Launcher.CreateCommandErrorFormat"),
                    ex.Message,
                    exe,
                    string.Join(" ", args));
                return (false, SensitiveDataMasker.MaskDbPassword(message));
            }
        }
    }
}
#endif