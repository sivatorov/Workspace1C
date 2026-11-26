using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Этапная диагностика окна «Резервные копии СУБД» (issue #143). При переменной
/// окружения <c>CM_DEBUG_DBMS=1</c> подробные записи о каждом этапе операции
/// (клик, вход в метод, чтение профиля, проверка, постановка задачи, старт
/// выделенного потока, файловая операция, взвод/срабатывание таймаута,
/// завершение/исключение) пишутся в файл <c>dbms-backup-debug.log</c> рядом с
/// журналом приложения (<see cref="PlatformPaths.LogDirectory"/>).
/// <para>
/// Формат строки: <c>UTC-время | thread=id | уровень | этап | детали</c>.
/// Пароли и строки подключения маскируются (<see cref="SensitiveDataMasker"/>).
/// Запись выполняется отдельным фоновым потоком, поэтому диагностика не может
/// «повесить» UI-поток на медленном каталоге журналов; ошибки записи молча
/// пропускаются. WARN/ERROR дублируются в <see cref="IAppLogger"/>.
/// </para>
/// </summary>
public static class DbmsBackupTrace
{
    /// <summary>Имя файла диагностики в каталоге журналов.</summary>
    public const string FileName = "dbms-backup-debug.log";

    private static readonly bool EnabledByEnvironment = IsEnabledByEnvironment();
    private static readonly object Sync = new();
    private static readonly BlockingCollection<string> Pending = new();
    private static int _writerStarted;
    private static string _lastStage = "none";

    /// <summary>Переопределение включения (тесты); null — по переменной окружения.</summary>
    public static bool? EnabledOverride { get; set; }

    /// <summary>
    /// Применяет настройку «Диагностика резервного копирования СУБД» из
    /// <see cref="Workspace1C.Models.AppSettings.DbmsBackupDiagnosticsEnabled"/>
    /// (issue #143, 1.0.1.125): включённая настройка равносильна
    /// <c>CM_DEBUG_DBMS=1</c>. Используется существующий
    /// <see cref="EnabledOverride"/>; выключение возвращает решение переменной
    /// окружения, чтобы диагностика, включённая снаружи, продолжала работать.
    /// </summary>
    public static void ApplySettings(bool enabled)
    {
        EnabledOverride = enabled ? true : null;
    }

    /// <summary>Переопределение каталога журнала (тесты); null — <see cref="PlatformPaths.LogDirectory"/>.</summary>
    public static string? DirectoryOverride { get; set; }

    /// <summary>True — диагностика включена (<c>CM_DEBUG_DBMS=1</c> или переопределение).</summary>
    public static bool IsEnabled => EnabledOverride ?? EnabledByEnvironment;

    /// <summary>Каталог, в который пишется <see cref="FileName"/>.</summary>
    public static string TraceDirectory =>
        string.IsNullOrWhiteSpace(DirectoryOverride) ? PlatformPaths.LogDirectory : DirectoryOverride!;

    /// <summary>
    /// Описание последнего зафиксированного этапа — для сторожа UI-потока
    /// (по нему видно, на чём именно встало окно).
    /// </summary>
    public static string LastStageDescription => Volatile.Read(ref _lastStage);

    /// <summary>Пишет этап операции (INFO).</summary>
    public static void Log(string stage, string? details = null) =>
        Write("INFO", stage, details);

    /// <summary>Пишет предупреждение: файл диагностики + <see cref="IAppLogger.Warn"/>.</summary>
    public static void Warn(string stage, string? details = null)
    {
        Write("WARN", stage, details);
        TryWriteAppLogger(logger => logger.Warn(FormatMessage(stage, details)));
    }

    /// <summary>Пишет ошибку: файл диагностики + <see cref="IAppLogger.Error"/>.</summary>
    public static void Error(string stage, string? details = null, Exception? exception = null)
    {
        Write("ERROR", stage, details);
        TryWriteAppLogger(logger => logger.Error(FormatMessage(stage, details), exception));
    }

    /// <summary>
    /// Классификация пути без обращения к диску (не блокирует и не «висит» на
    /// недоступном сетевом ресурсе): <c>empty</c>, <c>unc</c>, <c>drive</c>,
    /// <c>rooted</c>, <c>relative</c>.
    /// </summary>
    public static string ClassifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "empty";
        var value = path!.Trim();
        if (value.StartsWith(@"\\", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal))
            return "unc";
        if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
            return "drive";
        if (Path.IsPathRooted(value))
            return "rooted";
        return "relative";
    }

    /// <summary>True — путь UNC (<c>\\server\share</c>), то есть сетевой.</summary>
    public static bool IsNetworkPath(string? path) =>
        string.Equals(ClassifyPath(path), "unc", StringComparison.Ordinal);

    /// <summary>Путь с признаком типа: <c>C:\Backup (drive)</c>, <c>\\srv\share (unc)</c>.</summary>
    public static string DescribePath(string? path) =>
        $"{(string.IsNullOrWhiteSpace(path) ? "<пусто>" : path!.Trim())} ({ClassifyPath(path)})";

    /// <summary>
    /// Краткое описание профиля для журнала: идентификатор базы, тип СУБД, каталоги
    /// с типом пути. Пароль и логин в журнал не попадают.
    /// </summary>
    public static string DescribeProfile(DbmsBackupProfile? profile)
    {
        if (profile is null)
            return "profile=<нет>";
        var webDav = string.IsNullOrWhiteSpace(profile.WebDavUrl) ? "нет" : "да";
        return $"infobase={profile.InfobaseId}; dbms={profile.Dbms}; " +
               $"output={DescribePath(profile.OutputDirectory)}; " +
               $"tools={DescribePath(profile.ToolsDirectory)}; webdav={webDav}";
    }

    private static void Write(string level, string stage, string? details)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(stage))
            return;

        var safeDetails = Mask(details);
        var description = string.IsNullOrWhiteSpace(safeDetails) ? stage : stage + " " + safeDetails;
        Volatile.Write(ref _lastStage, description);

        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z | " +
                   $"thread={Environment.CurrentManagedThreadId} | {level} | " +
                   $"{stage} | {safeDetails}{Environment.NewLine}";

        try
        {
            EnsureWriter();
            Pending.Add(line);
        }
        catch
        {
            // Диагностика не должна ронять приложение.
        }
    }

    private static string Mask(string? details)
    {
        if (string.IsNullOrEmpty(details))
            return string.Empty;
        try
        {
            return SensitiveDataMasker.MaskDbPassword(details!);
        }
        catch
        {
            return details!;
        }
    }

    private static string FormatMessage(string stage, string? details) =>
        string.IsNullOrWhiteSpace(details) ? $"DBMS: {stage}" : $"DBMS: {stage}: {details}";

    private static void TryWriteAppLogger(Action<IAppLogger> write)
    {
        try
        {
            var logger = AppServices.TryGetService<IAppLogger>();
            if (logger is not null)
                write(logger);
        }
        catch
        {
            // Журнал приложения может быть ещё не настроен.
        }
    }

    private static void EnsureWriter()
    {
        if (Interlocked.CompareExchange(ref _writerStarted, 1, 0) != 0)
            return;

        var thread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "dbms-trace-writer"
        };
        thread.Start();
    }

    /// <summary>Выделенный поток записи: файловые операции не выполняются на UI/пуле.</summary>
    private static void WriterLoop()
    {
        foreach (var line in Pending.GetConsumingEnumerable())
            AppendLine(line);
    }

    /// <summary>
    /// Дописывает строку. Открываем с <see cref="FileShare.ReadWrite"/>, чтобы
    /// параллельное чтение журнала (диагностика/тесты) не приводило к потере строк;
    /// кратковременная занятость файла повторяется, а не глотается молча.
    /// </summary>
    private static void AppendLine(string line)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                lock (Sync)
                {
                    var directory = TraceDirectory;
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, FileName);
                    var bytes = Encoding.UTF8.GetBytes(line);
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
            catch
            {
                // Недоступный каталог журнала — диагностика пропускается.
                return;
            }
        }
    }

    private static bool IsEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("CM_DEBUG_DBMS");
        return value is "1" or "true" or "True" or "TRUE";
    }
}
