using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Workspace1C.Services;

/// <summary>Результат запуска внешней программы.</summary>
public sealed class ProcessRunResult
{
    public int ExitCode { get; init; } = -1;

    public string StdOut { get; init; } = string.Empty;

    public string StdErr { get; init; } = string.Empty;

    /// <summary>Программа не запустилась (нет файла и т.п.).</summary>
    public string? StartError { get; init; }

    public bool TimedOut { get; init; }

    public bool Success => StartError is null && !TimedOut && ExitCode == 0;

    /// <summary>Текст для журнала: stdout + stderr или причина отказа.</summary>
    public string CombinedOutput
    {
        get
        {
            if (StartError is not null)
                return StartError;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(StdOut))
                sb.AppendLine(StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(StdErr))
                sb.AppendLine(StdErr.TrimEnd());
            return sb.ToString().TrimEnd();
        }
    }
}

/// <summary>
/// Запуск внешних утилит (git, pg_dump, sqlcmd, webinst, …) с захватом вывода,
/// таймаутом и отменой (выпуск 1.0.0.69). Аргументы передаются списком
/// (<see cref="ProcessStartInfo.ArgumentList"/>), поэтому кавычки и пробелы в путях
/// экранирует сама платформа .NET.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Кодировка чтения вывода консольных утилит платформы 1С (webinst, ibcmd и др.)
    /// (issue #63 «каракули» webinst; issue #79 «каракули» вывода ibcmd в окне
    /// учётных данных): на русской Windows эти утилиты пишут в консольной
    /// OEM-кодировке (cp866) — читаем её; на Linux и при недоступной кодовой
    /// странице — null (UTF-8 по умолчанию). Поставщик кодовых страниц
    /// регистрируется однократно (идемпотентно).
    /// <para>
    /// issue #83: явная кодировка больше НЕ обязательна — при <c>null</c>
    /// <see cref="RunAsync"/> читает сырые байты и декодирует их центральным правилом
    /// <see cref="ConsoleOutputDecoder"/> (UTF-8 → cp866 → cp1251). Метод оставлен
    /// для утилит, которым нужна фиксированная кодировка чтения на уровне платформы
    /// (например, чтение через <see cref="ProcessStartInfo"/> вне
    /// <see cref="RunAsync"/>).
    /// </para>
    /// </summary>
    public static Encoding? ConsoleOutputEncoding()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(866);
        }
        catch
        {
            // Провайдер/кодовая страница недоступны — читаем как UTF-8.
            return null;
        }
    }

    /// <summary>
    /// Запускает внешнюю программу с захватом вывода (выпуск 1.0.0.69).
    /// <para>
    /// ЦЕНТРАЛЬНОЕ правило декодирования консольного вывода (issue #83, просьба 7OH
    /// «фикс сделать правилом для всех мест работы с выводом в консоли»): при
    /// <paramref name="outputEncoding"/> = null (по умолчанию) stdout/stderr читаются
    /// СЫРЫМИ байтами и декодируются <see cref="ConsoleOutputDecoder"/> — строгий UTF-8,
    /// затем cp866/cp1251 по наименьшему числу символов замены U+FFFD (по образцу
    /// DecodeRacOutput). Кодировка вывода консольных утилит заранее неизвестна
    /// (rac/ibcmd на русской Windows — cp866, ring — cp1251/UTF-8, Linux — UTF-8),
    /// поэтому фиксированное чтение как UTF-8 давало «каракули» (issue #63 webinst,
    /// #79 ibcmd, #83 ring/таблица лицензий).
    /// </para>
    /// <para>
    /// Явно заданная <paramref name="outputEncoding"/> имеет приоритет — для утилит
    /// с известной фиксированной кодировкой чтения.
    /// </para>
    /// </summary>
    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IDictionary<string, string>? environment = null,
        Encoding? outputEncoding = null,
        CancellationToken ct = default)
    {
        // null → сырое чтение байтов + эвристика ConsoleOutputDecoder (issue #83).
        var rawReading = outputEncoding is null;
        var encoding = outputEncoding ?? ConsoleOutputEncoding() ?? Encoding.UTF8;
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!rawReading)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new ProcessRunResult { StartError = $"{fileName}: {ex.Message}" };
        }
        if (process is null)
            return new ProcessRunResult { StartError = $"{fileName}: не удалось запустить процесс" };

        using (process)
        {
            // Сырое чтение байтов (issue #83) или готовых строк (явная кодировка).
            Task<byte[]> stdoutBytesTask = rawReading
                ? ReadToEndBytesAsync(process.StandardOutput.BaseStream)
                : Task.FromResult(Array.Empty<byte>());
            Task<byte[]> stderrBytesTask = rawReading
                ? ReadToEndBytesAsync(process.StandardError.BaseStream)
                : Task.FromResult(Array.Empty<byte>());
            var stdoutText = rawReading ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync();
            var stderrText = rawReading ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
                ct.ThrowIfCancellationRequested();
                return new ProcessRunResult
                {
                    TimedOut = true,
                    StdOut = await SafeReadOutput(stdoutBytesTask, stdoutText, rawReading).ConfigureAwait(false),
                    StdErr = await SafeReadOutput(stderrBytesTask, stderrText, rawReading).ConfigureAwait(false)
                };
            }

            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                StdOut = await SafeReadOutput(stdoutBytesTask, stdoutText, rawReading).ConfigureAwait(false),
                StdErr = await SafeReadOutput(stderrBytesTask, stderrText, rawReading).ConfigureAwait(false)
            };
        }
    }

    /// <summary>Читает поток до конца в массив байтов (без декодирования).</summary>
    private static async Task<byte[]> ReadToEndBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Читает вывод: сырое чтение — декодирование <see cref="ConsoleOutputDecoder"/>;
    /// при явной кодировке — готовый текст StreamReader.
    /// </summary>
    private static async Task<string> SafeReadOutput(Task<byte[]> bytesTask, Task<string> textTask, bool rawReading)
    {
        try
        {
            if (rawReading)
                return ConsoleOutputDecoder.Decode(await bytesTask.ConfigureAwait(false));
            return await textTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
