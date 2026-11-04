#if LINUX
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    public static partial class OneCLauncher
    {
        // ====================================================================
        // Пакетные операции DESIGNER
        // ====================================================================

        /// <summary>Операции DESIGNER без интерактивного UI (выгрузка, тест).</summary>
        public enum DesignerBatchOperation
        {
            DumpIB,
            DumpCfg,
            TestAndRepair,
            /// <summary>Восстановление данных ИБ из выгрузки .dt (/RestoreIB"path").</summary>
            RestoreIB,
            /// <summary>
            /// Загрузка конфигурации из файла .cf и обновление конфигурации БД
            /// (/LoadCfg"path.cf" /UpdateDBCfg). Используется заданиями по расписанию (issue #286).
            /// </summary>
            LoadCfg,
            /// <summary>Установка блокировки сеансов ИБ (/LockIB"строка сеансов").</summary>
            LockIB,
            /// <summary>Снятие блокировки сеансов ИБ (/LockIB"").</summary>
            UnlockIB,
            /// <summary>
            /// Обновление конфигурации из хранилища конфигурации и обновление конфигурации БД
            /// (/ConfigurationRepositoryF … /ConfigurationRepositoryUpdateCfg /UpdateDBCfg).
            /// Пакетное обновление из хранилищ (0.3.9.88): последовательный прогон выбранных баз.
            /// </summary>
            RepositoryUpdate,
            /// <summary>
            /// Выгрузка конфигурации в каталог XML-файлов (/DumpConfigToFiles"dir") для сравнения
            /// конфигураций (0.3.9.99, функция №9): каноническое дерево, одинаковое для базы и .cf.
            /// </summary>
            DumpConfigToFiles,
            /// <summary>
            /// Выгрузка версии хранилища конфигурации в файл .cf (/ConfigurationRepositoryDumpCfg"файл" [-v N]).
            /// «Обозреватель хранилища конфигурации» (0.3.9.127): номер версии передаётся в
            /// <see cref="RunDesignerBatch"/> отдельным параметром repositoryVersion.
            /// </summary>
            RepositoryDumpCfg,
            /// <summary>
            /// Отчёт по истории хранилища конфигурации (/ConfigurationRepositoryReport"файл" [-NBegin N] [-NEnd N]).
            /// Формат файла отчёта — табличный документ (.mxl); текстовые форматы не документированы
            /// (разведка этапа 1).
            /// </summary>
            RepositoryReport,
            /// <summary>
            /// Захват объектов хранилища (/ConfigurationRepositoryLock [-objects"файл.xml"]); без
            /// -objects — захват всех объектов; формат XML-списка не документирован (эксперимент).
            /// </summary>
            RepositoryLock,
            /// <summary>
            /// Отмена захвата объектов хранилища (/ConfigurationRepositoryUnlock [-objects"файл.xml"]).
            /// </summary>
            RepositoryUnlock
        }

        /// <summary>Информация о запущенной пакетной операции DESIGNER.</summary>
        public sealed class DesignerBatchInfo
        {
            public DesignerBatchInfo(DesignerBatchOperation operation, string infobaseName, string? outputPath,
                string? logPath = null, string? commandLine = null)
            {
                Operation = operation;
                InfobaseName = infobaseName;
                OutputPath = outputPath;
                LogPath = logPath;
                CommandLine = commandLine;
            }

            public DesignerBatchOperation Operation { get; }
            public string InfobaseName { get; }
            public string? OutputPath { get; }
            public string? LogPath { get; }
            public string? CommandLine { get; }
            public int ExitCode { get; set; } = -1;
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }

            public string OperationLabel => Operation switch
            {
                DesignerBatchOperation.DumpIB => LocalizationManager.T("Launcher.OperationDumpIB"),
                DesignerBatchOperation.DumpCfg => LocalizationManager.T("Launcher.OperationDumpCfg"),
                DesignerBatchOperation.TestAndRepair => LocalizationManager.T("Launcher.OperationTestAndRepair"),
                DesignerBatchOperation.RestoreIB => LocalizationManager.T("Launcher.OperationRestoreIB"),
                DesignerBatchOperation.LoadCfg => LocalizationManager.T("Launcher.OperationLoadCfg"),
                DesignerBatchOperation.LockIB => LocalizationManager.T("Launcher.OperationLockIB"),
                DesignerBatchOperation.UnlockIB => LocalizationManager.T("Launcher.OperationUnlockIB"),
                DesignerBatchOperation.RepositoryUpdate => LocalizationManager.T("Launcher.OperationRepositoryUpdate"),
                DesignerBatchOperation.DumpConfigToFiles => LocalizationManager.T("Launcher.OperationDumpConfigToFiles"),
                DesignerBatchOperation.RepositoryDumpCfg => LocalizationManager.T("Launcher.OperationRepositoryDumpCfg"),
                DesignerBatchOperation.RepositoryReport => LocalizationManager.T("Launcher.OperationRepositoryReport"),
                DesignerBatchOperation.RepositoryLock => LocalizationManager.T("Launcher.OperationRepositoryLock"),
                DesignerBatchOperation.RepositoryUnlock => LocalizationManager.T("Launcher.OperationRepositoryUnlock"),
                _ => LocalizationManager.T("Launcher.OperationGeneric")
            };
        }

        /// <summary>Запускает конфигуратор в пакетном режиме (выгрузка .dt/.cf или тест).</summary>
        public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath = null,
            BackupCredential? credential = null, int? repositoryVersion = null, int? repositoryReportBegin = null,
            int? repositoryReportEnd = null)
            => RunDesignerBatch(infobase, operation, outputPath, credential, repositoryVersion,
                repositoryReportBegin, repositoryReportEnd, out _);

        /// <summary>То же, но при отказе запуска возвращает локализованную причину (issue #365).</summary>
        public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath,
            BackupCredential? credential, int? repositoryVersion, int? repositoryReportBegin,
            int? repositoryReportEnd, out string? startError)
        {
            startError = null;
            var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
            if (string.IsNullOrEmpty(exePath))
            {
                var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
                exePath = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            }
            if (string.IsNullOrEmpty(exePath))
            {
                startError = LocalizationManager.T("Launcher.ConfiguratorExeNotFound");
                return false;
            }

            if (IsDesignerBlocked(infobase, out var blockReason))
            {
                startError = string.Format(LocalizationManager.T("Launcher.ConfiguratorBlockedFormat"), blockReason);
                return false;
            }

            if (operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg
                or DesignerBatchOperation.RepositoryDumpCfg or DesignerBatchOperation.RepositoryReport)
            {
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
                    return false;
                }
                // Каталог назначения создаётся здесь: платформа сама его не создаёт.
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch (Exception ex)
                    {
                        startError = string.Format(LocalizationManager.T("Launcher.CreateDirFailedFormat"), dir, ex.Message);
                        return false;
                    }
                }
            }
            else if (operation is DesignerBatchOperation.RepositoryLock or DesignerBatchOperation.RepositoryUnlock)
            {
                // Выборочный захват (-objects) требует существующий XML-файл; без файла — все объекты.
                if (!string.IsNullOrWhiteSpace(outputPath) && !File.Exists(outputPath))
                {
                    startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
                    return false;
                }
            }
            else if (operation == DesignerBatchOperation.DumpConfigToFiles)
            {
                // Каталог выгрузки должен существовать заранее (создаётся здесь): платформа
                // сама каталог не создаёт, а без него /DumpConfigToFiles завершится ошибкой.
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
                    return false;
                }
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception ex)
                {
                    startError = string.Format(LocalizationManager.T("Launcher.CreateDirFailedFormat"), outputPath, ex.Message);
                    return false;
                }
            }
            else if (operation is DesignerBatchOperation.RestoreIB or DesignerBatchOperation.LoadCfg)
            {
                // Восстановление и загрузка конфигурации требуют существующий исходный файл.
                if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                {
                    startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
                    return false;
                }
            }

            var connectionArg = BuildConnectionArgument(infobase);
            var authArg = BuildAuthArgument(infobase);
            // Переопределённые учётные данные сценария резервирования: если они заданы явно
            // (UseInfobaseAuth == false), используем их вместо авторизации базы.
            if (credential is { UseInfobaseAuth: false })
                authArg = BuildCredentialsArg(credential.User, credential.Password);

            // Ключи вида /DumpIB"path" — по грамматике ключа, НЕ строки подключения: кавычку внутри
            // пути удвоением не экранируют, поэтому путь с «"» недопустим (см. IsSafeCliValue) —
            // безопасно выгрузить его невозможно, отказываемся.
            string opArg = operation switch
            {
                DesignerBatchOperation.DumpIB when IsSafeCliValue(outputPath) => $"/DumpIB\"{outputPath}\"",
                DesignerBatchOperation.DumpCfg when IsSafeCliValue(outputPath) => $"/DumpCfg\"{outputPath}\"",
                DesignerBatchOperation.TestAndRepair => "/IBCheckAndRepair -TestOnly",
                DesignerBatchOperation.RestoreIB when IsSafeCliValue(outputPath) => $"/RestoreIB\"{outputPath}\"",
                // Загрузка новой конфигурации из .cf и обновление конфигурации БД.
                DesignerBatchOperation.LoadCfg when IsSafeCliValue(outputPath) => $"/LoadCfg\"{outputPath}\" /UpdateDBCfg",
                // Блокировка сеансов файловой ИБ: /LockIB"строка сеансов". Строка строится
                // в SessionLockOptions.BuildSessionLockString(); выходной файл не создаётся,
                // поэтому в outputPath передаётся именно строка сеансов.
                DesignerBatchOperation.LockIB when IsSafeCliValue(outputPath) => $"/LockIB\"{outputPath}\"",
                // Снятие блокировки: /LockIB с пустой строкой сеансов.
                DesignerBatchOperation.UnlockIB => "/LockIB\"\"",
                // Обновление конфигурации из хранилища (0.3.9.88): адрес, логин/пароль
                // хранилища и флаги /ConfigurationRepositoryUpdateCfg /UpdateDBCfg
                // собираются общим методом (см. OneCLauncher.Arguments.Shared.cs).
                DesignerBatchOperation.RepositoryUpdate => BuildRepositoryUpdateArgument(infobase),
                // Выгрузка конфигурации в каталог XML-файлов (0.3.9.99): каталог создан выше.
                DesignerBatchOperation.DumpConfigToFiles when IsSafeCliValue(outputPath) =>
                    $"/DumpConfigToFiles\"{outputPath}\"",
                _ => ""
            };
            if (string.IsNullOrEmpty(opArg))
            {
                startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
                return false;
            }

            // /Out — путь к временному логу, всегда системный GUID-файл (без пользовательских
            // данных), поэтому экранирование не требуется (вектора инъекции нет).
            var outLog = Path.Combine(Path.GetTempPath(), $"1c_batch_{Guid.NewGuid():N}.log");
            var arguments = $"DESIGNER {connectionArg}{authArg} {opArg} /DisableStartupDialogs /DisableStartupMessages /Out\"{outLog}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };
                var process = LinuxProcessEnvironment.Start(psi);
                // Пароль хранилища (/ConfigurationRepositoryP "…") маскируется в командной строке
                // ДО попадания в DesignerBatchInfo.CommandLine: при ошибке CompleteDesignerBatch
                // выводит CommandLine в ErrorMessage — пароль не должен утекать в UI/журнал.
                var commandLine = SensitiveDataMasker.MaskRepositoryPassword($"{exePath} {arguments}");
                var info = new DesignerBatchInfo(operation, infobase.Name, outputPath, outLog, commandLine);
                RegisterBatchProcess(infobase, process, info);
                DesignerBatchStarted?.Invoke(null, info);
                return true;
            }
            catch (Exception ex)
            {
                // Логируем замаскированную командную строку (пароль хранилища не должен попадать в журнал).
                var maskedArguments = SensitiveDataMasker.MaskRepositoryPassword(arguments);
                startError = string.Format(
                    LocalizationManager.T("Launcher.OperationStartFailedFormat"), ex.Message, exePath, maskedArguments);
                GetLogger()?.Error(startError, ex);
                return false;
            }
        }

        /// <summary>Токен подключения базы для сопоставления с командной строкой процесса.</summary>
        public static string GetBaseConnectionToken(Infobase infobase)
        {
            var conn = infobase.Connection;
            return conn.Type switch
            {
                ConnectionType.File => (conn.FilePath ?? string.Empty).Trim().TrimEnd('\\', '/'),
                ConnectionType.WebServer => (conn.WebUrl ?? string.Empty).Trim(),
                _ => $"{conn.GetServerWithPort()}\\{conn.DatabaseName}".Trim()
            };
        }

        private static void RegisterBatchProcess(Infobase infobase, Process? process, DesignerBatchInfo info)
        {
            var token = GetBaseConnectionToken(infobase);
            if (process is null || string.IsNullOrWhiteSpace(token))
                return;

            _activeBatchProcesses[token] = process;
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    _activeBatchProcesses.TryRemove(token, out _);
                    try { CompleteDesignerBatch(process, info); }
                    catch { }
                    DesignerBatchCompleted?.Invoke(null, info);
                };
            }
            catch
            {
                // процесс мог уже завершиться
            }
        }

        private static void CompleteDesignerBatch(Process process, DesignerBatchInfo info)
        {
            try { info.ExitCode = process.HasExited ? process.ExitCode : -1; }
            catch { info.ExitCode = -1; }

            var logText = ReadLogFile(info.LogPath);

            bool ok = info.ExitCode == 0;
            if (ok && info.Operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg)
            {
                ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                     File.Exists(info.OutputPath) &&
                     new FileInfo(info.OutputPath).Length > 0;
            }

            // Успех выгрузки версии хранилища (.cf) и отчёта по истории: код возврата 0 +
            // создан и не пуст выходной файл (разведка этапа 1: платформа возвращает код 0
            // даже при ошибке, поэтому файл — главный признак успеха).
            else if (ok && info.Operation is DesignerBatchOperation.RepositoryDumpCfg or DesignerBatchOperation.RepositoryReport)
            {
                ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                     File.Exists(info.OutputPath) &&
                     new FileInfo(info.OutputPath).Length > 0;
            }

            // Lock/Unlock выходной файл не создают — успех по коду возврата 0.

        else if (ok && info.Operation == DesignerBatchOperation.DumpConfigToFiles)
        {
            // Успех выгрузки в каталог: каталог существует и содержит служебный файл
            // выгрузки ConfigDumpInfo.xml (признак корректно созданного дерева).
            ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                 Directory.Exists(info.OutputPath) &&
                 File.Exists(Path.Combine(info.OutputPath, "ConfigDumpInfo.xml"));
        }

            info.Success = ok;
            if (ok)
                return;

            var sb = new StringBuilder();
            sb.AppendLine(string.Format(LocalizationManager.T("Launcher.OperationFailedFormat"), info.OperationLabel));
            sb.AppendLine(string.Format(LocalizationManager.T("Launcher.ExitCodeFormat"), info.ExitCode));
            if (!string.IsNullOrWhiteSpace(info.OutputPath))
                sb.AppendLine(string.Format(LocalizationManager.T("Launcher.FileFormat"), info.OutputPath));
            if (!string.IsNullOrWhiteSpace(logText))
            {
                sb.AppendLine();
                sb.AppendLine(LocalizationManager.T("Launcher.MessageHeader1C"));
                sb.Append(TruncateLogTail(logText, 3000));
            }
            info.ErrorMessage = sb.ToString();
        }

        private static string ReadLogFile(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath))
                return string.Empty;
            for (var i = 0; i < 30; i++)
            {
                try
                {
                    if (!File.Exists(logPath))
                        break;
                    var f = new FileInfo(logPath);
                    if (f.Length > 0)
                    {
                        var len1 = f.Length;
                        Thread.Sleep(120);
                        var len2 = new FileInfo(logPath).Length;
                        if (len1 == len2)
                            break;
                    }
                }
                catch
                {
                    break;
                }
                Thread.Sleep(80);
            }
            try
            {
                if (File.Exists(logPath))
                    return File.ReadAllText(logPath);
            }
            catch
            {
                // занят
            }
            finally
            {
                try { File.Delete(logPath); } catch { }
            }
            return string.Empty;
        }

        private static string TruncateLogTail(string text, int maxChars)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length <= maxChars)
                return text;
            return "…" + text.Substring(text.Length - maxChars);
        }

        /// <summary>Проверяет блокировку запуска конфигуратора перед пакетной операцией.</summary>
        public static bool IsDesignerBlocked(Infobase infobase, out string? reason)
        {
            reason = null;
            PruneDeadBatchProcesses();

            if (_activeBatchProcesses.Count > 0)
            {
                var otherName = _activeBatchProcesses.First().Value?.ProcessName ?? "1cv8";
                reason = string.Format(LocalizationManager.T("Launcher.AnotherOperationRunningFormat"), otherName);
                return true;
            }

            var token = GetBaseConnectionToken(infobase);
            if (!string.IsNullOrWhiteSpace(token) && IsConfiguratorRunningForBase(token))
            {
                reason = LocalizationManager.T("Launcher.ConfiguratorForBaseRunning");
                return true;
            }

            return false;
        }

        private static void PruneDeadBatchProcesses()
        {
            foreach (var kvp in _activeBatchProcesses)
            {
                if (kvp.Value == null || kvp.Value.HasExited)
                    _activeBatchProcesses.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>
        /// Завершает активную пакетную операцию DESIGNER для базы (issue #92, v1.0.1.28):
        /// кнопка «Остановить» окна массовой выгрузки при движке «Конфигуратор» гасит
        /// процесс 1cv8, а не оставляет его висеть. Процесс ищется по токену подключения
        /// базы; возвращает true, если процесс был найден и завершён (или уже отсутствует).
        /// <para>
        /// Issue #92 (v1.0.1.30, репорт 7OH «остановить по-прежнему ничего не даёт»):
        /// процесс-лаунчер 1cv8 может уже завершиться, а работа идёт в порождённом
        /// процессе. Поэтому дополнительно гасятся процессы DESIGNER этой базы по
        /// командной строке из /proc (как в <see cref="IsConfiguratorRunningForBase"/>;
        /// <paramref name="outputPath"/>, если задан, сужает поиск до нашей операции).
        /// </para>
        /// </summary>
        public static bool TerminateBatchProcess(Infobase infobase, string? outputPath = null)
        {
            var token = GetBaseConnectionToken(infobase);
            var killed = false;
            if (!string.IsNullOrWhiteSpace(token) && _activeBatchProcesses.TryRemove(token, out var process) &&
                process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }
                    killed = true;
                }
                catch
                {
                    // Лучшее усилие: процесс мог завершиться между проверкой и Kill —
                    // порождённые процессы догоняются поиском по /proc ниже.
                }
            }

            if (string.IsNullOrWhiteSpace(token))
                return killed;

            try
            {
                var victims = LinuxProc.Enumerate1C()
                    .Where(p => (p.Name ?? string.Empty).StartsWith("1cv8", StringComparison.OrdinalIgnoreCase))
                    .Where(p =>
                    {
                        var cmd = p.CmdLine ?? string.Empty;
                        return cmd.Contains("DESIGNER", StringComparison.OrdinalIgnoreCase) &&
                               cmd.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                               (string.IsNullOrWhiteSpace(outputPath) ||
                                cmd.Contains(outputPath, StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(p => (p.Pid, p.StartTime))
                    .ToList();
                if (victims.Count == 0)
                    return killed;
                LinuxProc.KillOneC(victims);
                return true;
            }
            catch
            {
                // Чтение /proc недоступно — лучшее усилие.
                return killed;
            }
        }

        /// <summary>Ищет запущенный конфигуратор (1cv8) для базы по командной строке из /proc.</summary>
        private static bool IsConfiguratorRunningForBase(string baseToken)
        {
            try
            {
                foreach (var process in LinuxProc.Enumerate1C())
                {
                    var name = process.Name;
                    var cmd = process.CmdLine;
                    var n = name ?? string.Empty;
                    if (!n.StartsWith("1cv8", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var c = cmd ?? string.Empty;
                    if (c.Contains("DESIGNER", StringComparison.OrdinalIgnoreCase) &&
                        c.Contains(baseToken, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // /proc недоступен
            }
            return false;
        }
    }
}
#endif
