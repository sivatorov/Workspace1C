using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IInfobaseAdminService"/> (Этап 6 дорожной карты StartManager,
/// функция №29 + консоль администрирования серверов).
/// Исполняемые файлы платформы ищутся в каталоге установленной платформы 1С рядом
/// с <c>1cv8</c>/<c>1cv8.exe</c> тем же способом, что и лаунчер: через
/// <see cref="PlatformVersionService.ResolveVersionBinDirectory"/> и
/// <see cref="PlatformVersionService.FindPlatformVersionDirs"/>.
/// </summary>
public sealed class InfobaseAdminService : IInfobaseAdminService
{
    /// <summary>Лимит ожидания проверки целостности: большие файловые базы проверяются долго.</summary>
    private static readonly TimeSpan QuietTimeout = TimeSpan.FromHours(2);

    private readonly IAppLogger _logger;

    public InfobaseAdminService(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CheckIntegrity(Infobase infobase)
    {
        if (infobase?.Connection?.Type != ConnectionType.File)
            return false;

        var baseDir = infobase.Connection.FilePath;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            _logger.Warn($"Проверка целостности: не задан путь к файловой базе «{infobase.Name}».");
            return false;
        }

        var binDir = OneCPlatformLocator.ResolveBinDirectory(infobase);
        if (binDir is null)
        {
            _logger.Warn(
                $"Проверка целостности: не найден каталог платформы 1С для базы «{infobase.Name}».");
            return false;
        }

        var exe = OneCPlatformLocator.FindInBinDir(binDir, "chdbfl");
        if (exe is null)
        {
            _logger.Warn(
                $"Проверка целостности: не найден исполняемый файл chdbfl в каталоге платформы {binDir}.");
            return false;
        }

        // Проверка целостности выполняется над файлом базы: <путь>\1Cv8.1CD.
        var dbFile = Path.Combine(baseDir, "1Cv8.1CD");
        return Launch(exe, $"\"{dbFile}\"", $"Проверка целостности базы «{infobase.Name}» (chdbfl)");
    }

    /// <inheritdoc />
    public async Task<BackupRunResult> CheckIntegrityQuiet(Infobase infobase)
    {
        var result = new BackupRunResult();
        if (infobase?.Connection?.Type != ConnectionType.File)
        {
            _logger.Warn($"Проверка целостности (тихо): база «{infobase?.Name}» не файловая.");
            result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityNotFileBase");
            return result;
        }

        var baseDir = infobase.Connection.FilePath;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            _logger.Warn($"Проверка целостности (тихо): не задан путь к файловой базе «{infobase.Name}».");
            result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityFailed");
            return result;
        }

        var binDir = OneCPlatformLocator.ResolveBinDirectory(infobase);
        if (binDir is null)
        {
            _logger.Warn(
                $"Проверка целостности (тихо): не найден каталог платформы 1С для базы «{infobase.Name}».");
            result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityFailed");
            return result;
        }

        var exe = OneCPlatformLocator.FindInBinDir(binDir, "chdbfl");
        if (exe is null)
        {
            _logger.Warn(
                $"Проверка целостности (тихо): не найден исполняемый файл chdbfl в каталоге платформы {binDir}.");
            result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityFailed");
            return result;
        }

        // Проверка целостности выполняется над файлом базы: <путь>\1Cv8.1CD.
        var dbFile = Path.Combine(baseDir, "1Cv8.1CD");
        try
        {
            _logger.Info($"Проверка целостности (тихо): запущен {exe} \"{dbFile}\"");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"\"{dbFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (!process.Start())
            {
                _logger.Warn($"Проверка целостности (тихо): не удалось запустить {exe}.");
                result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityFailed");
                return result;
            }

            // Читаем вывод параллельно с ожиданием: иначе большой вывод может переполнить
            // буфер канала и заблокировать завершение процесса.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(QuietTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Проверка целостности базы «{infobase.Name}»: превышен таймаут ожидания.");
                result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityTimeout");
                return result;
            }

            var output = ((await stdoutTask.ConfigureAwait(false)) + "\n" +
                          (await stderrTask.ConfigureAwait(false))).Trim();

            if (process.ExitCode == 0)
            {
                _logger.Info($"Проверка целостности базы «{infobase.Name}»: завершена успешно.");
                result.Success = true;
                result.ScenarioName = LocalizationManager.T("Admin.CheckIntegrityDone");
                return result;
            }

            var message = string.Format(
                LocalizationManager.T("Admin.CheckIntegrityExitCode"), process.ExitCode);
            if (!string.IsNullOrWhiteSpace(output))
                message += "\n" + output;
            _logger.Warn($"Проверка целостности базы «{infobase.Name}»: {message}");
            result.ErrorMessage = message;
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"Проверка целостности (тихо): не удалось запустить {exe}. {ex.Message}", ex);
            result.ErrorMessage = LocalizationManager.T("Admin.CheckIntegrityFailed");
            return result;
        }
    }

    /// <inheritdoc />
    public ServerConsoleResult OpenServerAdminConsole(Infobase? infobase, string? platformVersion = null)
    {
        // Консоль администрирования серверов 1С не связана с конкретной базой (issue #295):
        // запускаем оснастку всегда, какая бы строка ни была выбрана в списке (или вообще
        // без выбора). База — только источник настроек платформы; если её нет или она файловая,
        // используется выбранная (или новейшая) установленная платформа с оснасткой.
        var baseLabel = infobase is null ? string.Empty : $" для базы «{infobase.Name}»";

#if WINDOWS
        var options = GetServerConsolePlatformOptions();
        var candidates = ToCandidates(options);

        // Версия из диалога/настроек имеет приоритет; затем — версия базы; затем новейшая.
        var preferred = platformVersion;
        if (string.IsNullOrWhiteSpace(preferred))
            preferred = infobase?.PlatformVersion;

        var candidate = ServerConsolePlanner.ChoosePreferred(candidates, preferred)
                        ?? ServerConsolePlanner.ChoosePreferred(candidates, null);

        // Диагностика (issue #135): проверенные каталоги, найденные .msc, число платформ
        // и выбранная стратегия запуска — попадает в отказ и в диалог ошибки.
        var diagnostics = ServerConsolePlanner.BuildDiagnostics(
            options,
            ServerConsoleLaunchStrategies.None,
            candidate?.Display,
            candidate?.SnapInPath);

        // Причина отказа по каждой ветке — наружу (issue #135), иначе пункт меню
        // «молча ничего не делает»: раньше bool терялся за общим текстом.
        if (candidate is null)
        {
            // Не тупик (issue #135, п. 3d): отказ с диагностикой и признаками
            // CanOfferPlatformPicker/CanOfferRegistration для предложения действий в UI.
            var resolvedBinDir = OneCPlatformLocator.ResolveBinDirectory(infobase);
            var failure = ServerConsolePlanner.EvaluateAvailability(
                candidates, resolvedBinDir, anyPlatformFound: options.Count > 0, diagnostics);
            _logger.Warn(
                $"Консоль администрирования серверов 1С{baseLabel}: {ServerConsoleMessages.Format(failure)}");
            return failure;
        }

        // Каскад стратегий запуска (issue #135), каждая попытка логируется:
        // a) mmc.exe "основной .msc" (+ /32 для 32-битной платформы);
        // b) mmc.exe с явным /32 или без него для альтернативного файла оснастки;
        // c) запуск .msc через оболочку (UseShellExecute=true).
        var mmc = ServerConsolePlanner.ResolveMmcExecutable(Environment.SystemDirectory, File.Exists);
        var plan = ServerConsolePlanner.BuildLaunchPlan(candidate, mmc);
        var errors = new List<string>();

        foreach (var attempt in plan)
        {
            var commandLine = $"{attempt.FileName} {attempt.Arguments}".TrimEnd();
            try
            {
                _logger.Info(
                    $"Консоль администрирования серверов 1С{baseLabel}: попытка запуска " +
                    $"({attempt.Strategy}) {commandLine}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = attempt.FileName,
                    Arguments = attempt.Arguments ?? string.Empty,
                    UseShellExecute = attempt.UseShellExecute
                });
                _logger.Info(
                    $"Консоль администрирования серверов 1С{baseLabel}: запущено " +
                    $"({attempt.Strategy}) {commandLine} (версия {candidate.Display}).");
                return ServerConsoleResult.Ok(
                    attempt.SnapInPath ?? candidate.SnapInPath,
                    diagnostics.WithStrategy(attempt.Strategy));
            }
            catch (Exception ex)
            {
                errors.Add($"{commandLine}: {ex.Message}");
                _logger.Warn(
                    $"Консоль администрирования серверов 1С{baseLabel}: попытка {attempt.Strategy} " +
                    $"не удалась: {ex.Message}");
            }
        }

        var failedDiagnostics = diagnostics.WithStrategy(ServerConsoleLaunchStrategies.None);
        var launchFailure = ServerConsoleResult.Fail(
            ServerConsolePlanner.ErrorLaunchFailed,
            string.Join(Environment.NewLine, errors),
            failedDiagnostics);
        _logger.Error(
            $"Консоль администрирования серверов 1С{baseLabel}: все стратегии запуска не удались. " +
            ServerConsoleMessages.Format(launchFailure));
        return launchFailure;
#else
        // На Linux внешней консоли (оснастки MMC) нет: сообщаем причину, а не молчим.
        _logger.Warn(
            $"Консоль администрирования серверов 1С{baseLabel}: внешняя оснастка доступна только в Windows.");
        return ServerConsoleResult.Fail(
            ServerConsolePlanner.ErrorWindowsOnly,
            null,
            new ServerConsoleDiagnostics(
                Array.Empty<string>(), Array.Empty<string>(), 0, ServerConsoleLaunchStrategies.WindowsOnly));
#endif
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerConsoleCandidate> GetServerConsoleCandidates()
        => ToCandidates(GetServerConsolePlatformOptions());

    /// <inheritdoc />
    public IReadOnlyList<ServerConsolePlatformOption> GetServerConsolePlatformOptions()
    {
#if WINDOWS
        var sources = new List<(string Version, string BinDir, string Architecture)>();
        foreach (var architecture in new[] { "64", "32" })
        {
            foreach (var (version, binDir) in PlatformVersionService.FindPlatformVersionDirs(architecture))
                sources.Add((version, binDir, architecture));
        }

        // Расширенный поиск оснастки (issue #135): точные имена + маски в bin, .. и ..\common.
        return ServerConsolePlanner.ListPlatformOptions(sources, File.Exists, EnumerateMscFiles);
#else
        return Array.Empty<ServerConsolePlatformOption>();
#endif
    }

    /// <summary>Кандидаты запуска из списка установленных платформ (только с оснасткой).</summary>
    private static IReadOnlyList<ServerConsoleCandidate> ToCandidates(
        IReadOnlyList<ServerConsolePlatformOption> options)
    {
        var result = new List<ServerConsoleCandidate>();
        foreach (var option in options)
        {
            var candidate = option.ToCandidate();
            if (candidate is not null)
                result.Add(candidate);
        }
        return result;
    }

#if WINDOWS
    /// <summary>
    /// Перечисление файлов <c>.msc</c> для поиска оснастки по маске (issue #135):
    /// недоступный каталог не должен срывать поиск.
    /// </summary>
    private static IEnumerable<string> EnumerateMscFiles(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(directory, "*.msc").ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
#endif

    /// <inheritdoc />
    public ServerConsoleRegistrationResult RegisterServerConsoleComponents(string? platformVersion)
    {
#if WINDOWS
        // Регистрация компонентов не требует самой оснастки (issue #135): автор issue
        // просил зарегистрировать radmin.dll и comctrl.dll в каталоге платформы.
        var (binDir, is64Bit) = ResolveRegistrationTarget(platformVersion);
        if (string.IsNullOrWhiteSpace(binDir))
        {
            _logger.Warn(
                "Регистрация компонентов консоли: не найдена установленная платформа 1С.");
            return new ServerConsoleRegistrationResult(
                false, null, ServerConsolePlanner.ErrorRegisterNoPlatform,
                Array.Empty<ServerConsoleRegistrationItem>());
        }

        var dlls = ServerConsolePlanner.ListRegistrationDlls(binDir, File.Exists);
        var items = new List<ServerConsoleRegistrationItem>();

        foreach (var dll in dlls)
        {
            if (!dll.Exists)
            {
                // Отсутствующий файл — понятное сообщение в отчёте, а не исключение.
                items.Add(new ServerConsoleRegistrationItem(dll.DllPath, false, false, null));
                _logger.Warn($"Регистрация компонентов консоли: файл не найден — {dll.DllPath}");
                continue;
            }

            var (success, error) = RegisterConsoleDll(dll.DllPath, is64Bit);
            items.Add(new ServerConsoleRegistrationItem(dll.DllPath, true, success, error));
            _logger.Info(
                $"Регистрация компонента консоли {(success ? "успешна" : "не удалась")}: {dll.DllPath}" +
                (string.IsNullOrWhiteSpace(error) ? string.Empty : $" — {error}"));
        }

        if (items.All(item => !item.FileExists))
        {
            _logger.Warn(
                $"Регистрация компонентов консоли: в каталоге {binDir} нет radmin.dll и comctrl.dll.");
            return new ServerConsoleRegistrationResult(
                false, binDir, ServerConsolePlanner.ErrorRegisterNoFiles, items);
        }

        // Успех — только если все НАЙДЕННЫЕ DLL зарегистрированы; отсутствие одной
        // из них не делает регистрацию второй неудачной, но отчёт покажет пропуск.
        var allRegistered = items.Where(item => item.FileExists).All(item => item.Success);
        return new ServerConsoleRegistrationResult(allRegistered, binDir, null, items);
#else
        _logger.Warn("Регистрация компонентов консоли: доступна только в Windows.");
        return new ServerConsoleRegistrationResult(
            false, null, ServerConsolePlanner.ErrorWindowsOnly,
            Array.Empty<ServerConsoleRegistrationItem>());
#endif
    }

#if WINDOWS
    /// <summary>
    /// Каталог <c>bin</c> и разрядность для регистрации компонентов консоли (issue #135):
    /// платформа выбирается по варианту версии, а при его отсутствии — новейшая
    /// установленная. Оснастка <c>.msc</c> для регистрации не требуется — radmin.dll
    /// и comctrl.dll лежат в том же каталоге платформы.
    /// </summary>
    private (string? BinDir, bool Is64Bit) ResolveRegistrationTarget(string? platformVersion)
    {
        var options = GetServerConsolePlatformOptions();
        var option = ServerConsolePlanner.ChoosePreferredPlatform(options, platformVersion);
        if (option is not null)
            return (option.BinDir, string.Equals(option.Architecture, "64", StringComparison.Ordinal));

        // Платформа могла быть не найдена штатным поиском — разрешаем каталог по версии.
        if (!string.IsNullOrWhiteSpace(platformVersion))
        {
            PlatformVersionService.ParseVariantOptionalArch(
                platformVersion!, out var version, out var architecture);
            if (!string.IsNullOrWhiteSpace(version))
            {
                var architectures = string.IsNullOrWhiteSpace(architecture)
                    ? new[] { "64", "32" }
                    : new[] { architecture!, architecture == "64" ? "32" : "64" };
                foreach (var arch in architectures)
                {
                    var binDir = PlatformVersionService.ResolveVersionBinDirectory(version!, arch);
                    if (!string.IsNullOrWhiteSpace(binDir))
                        return (binDir, arch == "64");
                }
            }
        }

        return (null, true);
    }

    /// <summary>
    /// Регистрирует один компонент консоли через regsvr32 нужной разрядности с
    /// повышением прав (UAC). Возвращает (успех, текст ошибки).
    /// </summary>
    private (bool Success, string? Error) RegisterConsoleDll(string dllPath, bool is64Bit)
    {
        try
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var regSvr32 = ServerConsolePlanner.ResolveRegSvr32Executable(windowsDir, is64Bit, File.Exists);

            var psi = new ProcessStartInfo
            {
                FileName = regSvr32,
                Arguments = ServerConsolePlanner.BuildRegSvr32Arguments(dllPath),
                UseShellExecute = true,
                Verb = "runas",          // повышение прав через UAC
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, LocalizationManager.T("Admin.ServerConsoleRegisterStartFailed"));

            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return (false, LocalizationManager.T("Admin.ServerConsoleRegisterTimeout"));
            }

            var exitCode = process.ExitCode;
            if (exitCode == 0)
                return (true, null);
            if (exitCode == 1223)
                return (false, LocalizationManager.T("Admin.ServerConsoleRegisterCanceled"));
            return (false, string.Format(
                LocalizationManager.T("Admin.ServerConsoleRegisterExitCode"), exitCode));
        }
        catch (System.ComponentModel.Win32Exception wex)
        {
            // 1223 — пользователь отменил повышение прав UAC.
            if (wex.NativeErrorCode == 1223)
                return (false, LocalizationManager.T("Admin.ServerConsoleRegisterCanceled"));
            return (false, wex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
#endif

    /// <summary>
    /// Поиск исполняемых файлов платформы (bin-каталог, rac, chdbfl) вынесен в
    /// <see cref="OneCPlatformLocator"/> (0.3.9.123) и используется совместно
    /// с клиентом rac встроенного монитора серверов 1С — без дублирования.
    /// </summary>
    private bool Launch(string fileName, string arguments, string what, bool shellExecute = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = shellExecute
            });
            _logger.Info($"{what}: запущен {fileName} {arguments}".Trim());
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"{what}: не удалось запустить {fileName}. {ex.Message}", ex);
            return false;
        }
    }
}