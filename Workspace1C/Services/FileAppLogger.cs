using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>
/// Файловый логгер: %AppData%/Workspace1C/logs/app-YYYYMMDD.log
/// Ротация: удаляет файлы старше 14 дней; обрезает текущий файл свыше 5 МБ.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly object _sync = new();
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;

    /// <summary>
    /// Переопределение каталога журнала для тестового процесса
    /// (<c>Workspace1C.Tests/TestEnvironment.Init</c>). Тесты не должны писать
    /// «тестовые» ERROR в пользовательский app-YYYYMMDD.log: такие записи путают
    /// диагностику реальных сбоев. В приложении значение всегда <c>null</c>.
    /// </summary>
    public static string? DirectoryOverride { get; set; }

    public FileAppLogger(string? logDirectory = null)
    {
        // Windows: %APPDATA%\Workspace1C\logs
        // Linux:   ~/.config/Workspace1C/logs
        _logDirectory = logDirectory ?? DirectoryOverride ?? PlatformPaths.LogDirectory;
        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);
            if (exception is not null)
            {
                line.Append(" | ").Append(exception.GetType().Name)
                    .Append(": ").Append(exception.Message);
            }
            line.AppendLine();

            lock (_sync)
            {
                var path = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var archive = path + ".old";
                    if (File.Exists(archive)) File.Delete(archive);
                    File.Move(path, archive);
                }
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Логгер не должен ронять приложение.
            System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-KeepDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "app-*.log*"))
            {
                if (File.GetLastWriteTime(file) < threshold)
                    File.Delete(file);
            }
        }
        catch { /* ignore */ }
    }
}
