using System.Collections.Generic;
using System.Diagnostics;
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
    /// Запускает внешнюю программу с захватом вывода (выпуск 1.0.0.69). Необязательный
    /// <paramref name="outputEncoding"/> задаёт кодировку чтения stdout/stderr
    /// (issue #63: консольные утилиты вроде webinst на русской Windows пишут вывод
    /// в OEM-кодировке cp866 — чтение как UTF-8 даёт «каракули»); null — UTF-8.
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
        var encoding = outputEncoding ?? Encoding.UTF8;
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };
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
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
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
                    StdOut = await SafeRead(stdout).ConfigureAwait(false),
                    StdErr = await SafeRead(stderr).ConfigureAwait(false)
                };
            }

            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                StdOut = await SafeRead(stdout).ConfigureAwait(false),
                StdErr = await SafeRead(stderr).ConfigureAwait(false)
            };
        }
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return string.Empty; }
    }
}
