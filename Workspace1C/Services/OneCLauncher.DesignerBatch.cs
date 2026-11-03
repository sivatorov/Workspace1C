using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Сервис запуска платформы 1С:Предприятие.
/// </summary>
public static partial class OneCLauncher
{
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
        /// Формат файла отчёта — табличный документ (.mxl); текстовые форматы (.txt/.html)
        /// не документированы (разведка этапа 1: грамматика ключей подтверждена на 8.3.27.2325,
        /// реальный формат на этой машине не проверяем из-за недоступности хранилища).
        /// </summary>
        RepositoryReport,
        /// <summary>
        /// Захват объектов хранилища конфигурации (/ConfigurationRepositoryLock [-objects"файл.xml"]).
        /// Без -objects захватываются все объекты; формат XML-файла списка не документирован
        /// платформой — выборочный захват экспериментальный.
        /// </summary>
        RepositoryLock,
        /// <summary>
        /// Отмена захвата объектов хранилища (/ConfigurationRepositoryUnlock [-objects"файл.xml"]).
        /// </summary>
        RepositoryUnlock
    }

    /// <summary>
    /// Информация о запущенной пакетной операции DESIGNER (выгрузка .dt/.cf или тест),
    /// передаваемая через события <see cref="OneCLauncher.DesignerBatchStarted"/> /
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/>.
    /// </summary>
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

        /// <summary>Тип выполняемой операции.</summary>
        public DesignerBatchOperation Operation { get; }

        /// <summary>Имя информационной базы, для которой выполняется операция.</summary>
        public string InfobaseName { get; }

        /// <summary>Путь к файлу выгрузки (.dt/.cf); может быть пустым для тестирования.</summary>
        public string? OutputPath { get; }

        /// <summary>Путь к временному файлу лога операции (/Out), заполняется по завершении.</summary>
        public string? LogPath { get; }

        /// <summary>Командная строка запуска 1cv8 (для диагностики).</summary>
        public string? CommandLine { get; }

        /// <summary>Код возврата процесса 1cv8 (заполняется по завершении).</summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>Успешно ли завершилась операция (код 0 и файл создан).</summary>
        public bool Success { get; set; }

        /// <summary>Сообщение об ошибке с текстом лога 1С (при неуспехе).</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Человекочитаемое название операции для индикатора и подсказки.</summary>
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

    /// <summary>
    /// Запускает конфигуратор в пакетном режиме: выгрузка .dt / .cf или тестирование ИБ.
    /// Формат аргументов как у командной строки 1С (без пробела между ключом и значением в кавычках).
    /// </summary>
    public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath = null,
        BackupCredential? credential = null, int? repositoryVersion = null, int? repositoryReportBegin = null,
        int? repositoryReportEnd = null)
        => RunDesignerBatch(infobase, operation, outputPath, credential, repositoryVersion,
            repositoryReportBegin, repositoryReportEnd, out _);

    /// <summary>
    /// То же, что и основная перегрузка, но при отказе запуска в <paramref name="startError"/>
    /// возвращается локализованная причина (нет 1cv8.exe, блокировка, небезопасный путь,
    /// не создался каталог, не запустился процесс) — вместо generic-фразы у вызывающего
    /// кода (issue #365: «Не удалось загрузить данные обозревателя» без деталей).
    /// </summary>
    public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath,
        BackupCredential? credential, int? repositoryVersion, int? repositoryReportBegin,
        int? repositoryReportEnd, out string? startError)
    {
        startError = null;
        var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
        var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);

        // Платформа может быть установлена только в одной разрядности (например,
        // 32-бит в Program Files (x86) при глобальной настройке по умолчанию «64»).
        // Если для выбранной разрядности 1cv8.exe не найден — пробуем противоположную.
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
            var fallback = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            if (!string.IsNullOrEmpty(fallback) &&
                !fallback.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
                exePath = fallback;
        }

        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            startError = LocalizationManager.T("Launcher.ConfiguratorExeNotFound");
            GetLogger()?.Warn(startError);
            return false;
        }

        // Проверка блокировки запуска конфигуратора: уже запущен конфигуратор этой базы
        // (в т.ч. открытый вручную вне приложения) или идёт другая выгрузка/операция DESIGNER.
        if (IsDesignerBlocked(infobase, out var blockReason))
        {
            startError = string.Format(LocalizationManager.T("Launcher.ConfiguratorBlockedFormat"), blockReason);
            GetLogger()?.Warn(startError);
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
            // Каталог назначения должен существовать (создаётся здесь): платформа сама его не создаёт.
            // Для RepositoryDumpCfg outputPath — файл .cf, для RepositoryReport — файл отчёта по истории.
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    startError = string.Format(LocalizationManager.T("Launcher.CreateDirFailedFormat"), dir, ex.Message);
                    GetLogger()?.Error(startError, ex);
                    return false;
                }
            }
        }
        else if (operation is DesignerBatchOperation.RepositoryLock or DesignerBatchOperation.RepositoryUnlock)
        {
            // Выборочный захват/отмена (-objects) требует существующий XML-файл списка объектов;
            // без файла (outputPath == null) операция выполняется для всех объектов хранилища.
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
                GetLogger()?.Error(startError, ex);
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

        // Важно: у 1С ключи вида /DumpIB"C:\path\file.dt" (значение сразу в кавычках). Это НЕ строка
        // подключения: кавычку внутри пути экранировать удвоением нельзя, поэтому путь с «"»
        // недопустим (см. IsSafeCliValue) — безопасно выгрузить его невозможно, отказываемся.
        string opArg = operation switch
        {
            DesignerBatchOperation.DumpIB when IsSafeCliValue(outputPath) => $"/DumpIB\"{outputPath}\"",
            DesignerBatchOperation.DumpCfg when IsSafeCliValue(outputPath) => $"/DumpCfg\"{outputPath}\"",
            DesignerBatchOperation.TestAndRepair => "/IBCheckAndRepair -TestOnly",
            DesignerBatchOperation.RestoreIB when IsSafeCliValue(outputPath) => $"/RestoreIB\"{outputPath}\"",
            // Загрузка новой конфигурации из .cf и обновление конфигурации БД.
            DesignerBatchOperation.LoadCfg when IsSafeCliValue(outputPath) => $"/LoadCfg\"{outputPath}\" /UpdateDBCfg",
            // Выгрузка конфигурации в каталог XML-файлов (/DumpConfigToFiles"dir", issue #365):
            // ветки НЕ было — операция «Обозревателя метаданных» (DumpConfigToFiles) всегда
            // падала с «Операция отклонена: пустой или недопустимый путь» (opArg был пустым).
            DesignerBatchOperation.DumpConfigToFiles => DumpConfigToFilesArgument(outputPath),
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
            // Операции хранилища «Обозревателя хранилища конфигурации» (0.3.9.127): общий блок
            // F/N/P + ключ операции; параметры -v/-NBegin/-NEnd/-objects — с дефисом и пробелом
            // перед значением (уникальная грамматика repository-команд, см. Arguments.Shared.cs).
            DesignerBatchOperation.RepositoryDumpCfg =>
                BuildRepositoryDumpCfgArgument(infobase, outputPath!, repositoryVersion),
            DesignerBatchOperation.RepositoryReport =>
                BuildRepositoryReportArgument(infobase, outputPath!, repositoryReportBegin, repositoryReportEnd),
            DesignerBatchOperation.RepositoryLock => BuildRepositoryLockArgument(infobase, outputPath),
            DesignerBatchOperation.RepositoryUnlock => BuildRepositoryUnlockArgument(infobase, outputPath),
            _ => ""
        };
        if (string.IsNullOrEmpty(opArg))
        {
            // Здесь операция отсутствует в switch либо значение параметра небезопасно.
            // Сообщение одно — «Операция отклонена» (см. Launcher.OperationRefusedFormat).
            startError = LocalizationManager.T("Launcher.OperationRefusedFormat");
            return false;
        }

        // /Out — путь к временному логу, всегда системный GUID-файл, без пользовательских данных,
        // поэтому экранирование не требуется (вектора инъекции нет).
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
            var process = Process.Start(psi);
            // Пароль хранилища (/ConfigurationRepositoryP "…") маскируется в командной строке
            // ДО попадания в DesignerBatchInfo.CommandLine: при ошибке CompleteDesignerBatch
            // выводит CommandLine в ErrorMessage — пароль не должен утекать в UI/журнал.
            var commandLine = SensitiveDataMasker.MaskRepositoryPassword($"{exePath} {arguments}");
            var info = new DesignerBatchInfo(operation, infobase.Name, outputPath, outLog, commandLine);
            if (!RegisterBatchProcess(infobase, process, info))
            {
                // Процесс запущен, но отследить его невозможно (null или пустой token) —
                // Exited никогда не придёт, ожидание завершилось бы таймаутом часами
                // (issue #365). Убиваем СВОЙ процесс и сразу завершаем с причиной.
                try { process?.Kill(); } catch { /* уже завершился */ }
                startError = LocalizationManager.T("Launcher.TrackProcessFailed");
                info.ErrorMessage = startError;
                GetLogger()?.Warn(startError);
                DesignerBatchCompleted?.Invoke(null, info);
                return false;
            }
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

    /// <summary>
    /// Аргумент выгрузки конфигурации в каталог XML-файлов (/DumpConfigToFiles"dir"),
    /// ключ по грамматике 1С (значение сразу в кавычках, кавычку внутри пути удвоением
    /// экранировать нельзя). Пустой результат — небезопасный путь (issue #365: ветки не
    /// было, и операция «Обозревателя метаданных» всегда отклонялась «пустой или
    /// недопустимый путь»). Internal — для юнит-тестов.
    /// </summary>
    internal static string DumpConfigToFilesArgument(string? outputPath)
        => IsSafeCliValue(outputPath) ? $"/DumpConfigToFiles\"{outputPath}\"" : "";

    /// <summary>Токен подключения базы для сопоставления с командной строкой процесса конфигуратора.</summary>
    public static string GetBaseConnectionToken(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            ConnectionType.File => (conn.FilePath ?? string.Empty).Trim().TrimEnd('\\'),
            ConnectionType.WebServer => (conn.WebUrl ?? string.Empty).Trim(),
            _ => $"{conn.GetServerWithPort()}\\{conn.DatabaseName}".Trim()
        };
    }

    /// <summary>
    /// Регистрирует запущенный процесс пакетной операции и удаляет его по завершении.
    /// Возвращает false, если процесс невозможно отследить (null или пустой token):
    /// событие <c>Exited</c> никогда не придёт, и ожидание завершилось бы таймаутом
    /// (issue #365). При сбое подписки процесс останавливается здесь — вызывающий
    /// получит false и не будет ждать 60 минут.
    /// </summary>
    private static bool RegisterBatchProcess(Infobase infobase, Process? process, DesignerBatchInfo info)
    {
        var token = GetBaseConnectionToken(infobase);
        if (process is null || string.IsNullOrWhiteSpace(token))
            return false;

        _activeBatchProcesses[token] = process;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                _activeBatchProcesses.TryRemove(token, out _);
                // Читаем лог, определяем успех операции и формируем сообщение об ошибке.
                try { CompleteDesignerBatch(process, info); }
                catch { /* не должны ронять поток обработчика */ }
                // Оповещаем об окончании операции (индикатор выгрузки в главном окне).
                DesignerBatchCompleted?.Invoke(null, info);
            };
            return true;
        }
        catch
        {
            // EnableRaisingEvents/подписка Exited недоступны (процесс мог завершиться
            // мгновенно) — убиваем свой процесс: отслеживать Exited мы не сможем.
            try { process.Kill(); } catch { /* уже завершился */ }
            return false;
        }
    }

    /// <summary>
    /// По завершении процесса 1cv8 читает лог /Out, определяет успех операции
    /// (код возврата 0 и наличие файла выгрузки) и заполняет <paramref name="info"/>.
    /// При неуспехе формирует человекочитаемое сообщение с текстом лога 1С.
    /// </summary>
    private static void CompleteDesignerBatch(Process process, DesignerBatchInfo info)
    {
        try { info.ExitCode = process.HasExited ? process.ExitCode : -1; }
        catch { info.ExitCode = -1; }

        // Читаем лог операции (файл мог ещё дописываться — ждём стабилизации размера).
        var logText = ReadLogFile(info.LogPath);

        // Успех: код возврата 0 и (для выгрузки) создан и не пуст файл назначения.
        bool ok = info.ExitCode == 0;
        if (ok && info.Operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg)
        {
            ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                 File.Exists(info.OutputPath) &&
                 new FileInfo(info.OutputPath).Length > 0;
        }

        // Успех выгрузки версии хранилища (.cf) и отчёта по истории: код возврата 0 +
        // создан и не пуст выходной файл (разведка этапа 1: платформа возвращает код 0
        // даже при ошибке — «Хранилище … не обнаружено», поэтому файл — главный признак).
        else if (ok && info.Operation is DesignerBatchOperation.RepositoryDumpCfg or DesignerBatchOperation.RepositoryReport)
        {
            ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                 File.Exists(info.OutputPath) &&
                 new FileInfo(info.OutputPath).Length > 0;
        }

        // Lock/Unlock выходной файл не создают — успех по коду возврата 0.

        else if (ok && info.Operation == DesignerBatchOperation.DumpConfigToFiles)
        {
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
        if (!string.IsNullOrWhiteSpace(info.CommandLine))
        {
            sb.AppendLine();
            sb.AppendLine(LocalizationManager.T("Launcher.CommandLineHeader"));
            sb.AppendLine(info.CommandLine);
        }
        info.ErrorMessage = sb.ToString();
    }

    /// <summary>Читает содержимое временного лога 1С и удаляет файл.</summary>
    private static string ReadLogFile(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
            return string.Empty;

        // Ждём, пока файл перестанет расти (1С дописывает лог даже после выхода процесса).
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
                        break; // размер стабилен — можно читать
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
            /* занят другим процессом — пропускаем */
        }
        finally
        {
            try { File.Delete(logPath); } catch { /* ignore */ }
        }
        return string.Empty;
    }

    /// <summary>Возвращает хвост текста (последние <paramref name="maxChars"/> символов).</summary>
    private static string TruncateLogTail(string text, int maxChars)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length <= maxChars)
            return text;
        return "…" + text.Substring(text.Length - maxChars);
    }

    /// <summary>Удаляет завершившиеся процессы из реестра активных операций.</summary>
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
    /// </summary>
    public static bool TerminateBatchProcess(Infobase infobase)
    {
        var token = GetBaseConnectionToken(infobase);
        if (string.IsNullOrWhiteSpace(token) || !_activeBatchProcesses.TryRemove(token, out var process))
            return false;
        if (process is null)
            return false;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            return true;
        }
        catch
        {
            // Лучшее усилие: процесс мог завершиться между проверкой и Kill.
            return false;
        }
    }

    /// <summary>
    /// Проверяет блокировку запуска конфигуратора перед выгрузкой .dt/.cf или тестированием.
    /// Возвращает true, если запуск нужно заблокировать; <paramref name="reason"/> описывает причину.
    /// </summary>
    public static bool IsDesignerBlocked(Infobase infobase, out string? reason)
    {
        reason = null;
        PruneDeadBatchProcesses();

        // 1. Уже идёт другая выгрузка / пакетная операция DESIGNER, запущенная приложением.
        if (_activeBatchProcesses.Count > 0)
        {
            var otherName = _activeBatchProcesses.First().Value?.ProcessName ?? "1cv8.exe";
            reason = string.Format(LocalizationManager.T("Launcher.AnotherOperationRunningFormat"), otherName);
            return true;
        }

        // 2. Конфигуратор этой базы уже запущен (в т.ч. открыт вручную вне приложения).
        var token = GetBaseConnectionToken(infobase);
        if (!string.IsNullOrWhiteSpace(token) && IsConfiguratorRunningForBase(token))
        {
            reason = LocalizationManager.T("Launcher.ConfiguratorForBaseRunning");
            return true;
        }

        return false;
    }

    /// <summary>Ищет запущенный процесс конфигуратора (1cv8.exe) для указанной базы по командной строке.</summary>
    private static bool IsConfiguratorRunningForBase(string baseToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process " +
                "WHERE Name='1cv8.exe' OR Name='1cv8x64.exe'");
            foreach (var obj in searcher.Get())
            {
                var cmd = obj["CommandLine"] as string ?? string.Empty;
                if (cmd.Contains("DESIGNER", StringComparison.OrdinalIgnoreCase) &&
                    cmd.Contains(baseToken, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Нет прав на чтение командной строки процессов других пользователей или WMI недоступен.
        }
        return false;
    }
}
