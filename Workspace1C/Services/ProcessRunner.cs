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

    /// <summary>
    /// Запуск прерван вызывающим кодом (issue #143): процесс убит, вывод может быть
    /// неполным. В отличие от <see cref="TimedOut"/>, это не ошибка утилиты.
    /// </summary>
    public bool Canceled { get; init; }

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
    /// <para>
    /// <paramref name="timeout"/> = <c>null</c> (1.0.1.125, issue #143) — ожидание
    /// без жёсткого лимита: длительная копия большой базы не обрывается по времени,
    /// а прерывается только отменой <paramref name="ct"/>.
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
            // issue #143: stdin обязательно перенаправляем и сразу закрываем. Иначе
            // утилита, запросившая пароль/подтверждение, ждёт консоль, которой у
            // GUI-приложения нет, и «Сделать копию сейчас» висит бесконечно.
            RedirectStandardInput = true,
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
            // issue #143: закрываем stdin сразу после старта — утилита не может
            // «зависнуть» на приглашении ввода.
            try { process.StandardInput.Close(); } catch { /* поток уже закрыт */ }

            // Чтение с отменой: при таймауте/отмене отмена токена гарантированно
            // освобождает чтение, а не оставляет его висеть на незакрытой трубе.
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Сырое чтение байтов (issue #83) или готовых строк (явная кодировка).
            Task<byte[]> stdoutBytesTask = rawReading
                ? ReadToEndBytesAsync(process.StandardOutput.BaseStream, readCts.Token)
                : Task.FromResult(Array.Empty<byte>());
            Task<byte[]> stderrBytesTask = rawReading
                ? ReadToEndBytesAsync(process.StandardError.BaseStream, readCts.Token)
                : Task.FromResult(Array.Empty<byte>());
            var stdoutText = rawReading ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync();
            var stderrText = rawReading ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync();

            // issue #143 (1.0.1.125): null — ожидание без жёсткого лимита
            // (длительный дамп большой базы). Прерывание — только отменой.
            using var timeoutCts = timeout is { } limit ? new CancellationTokenSource(limit) : null;
            using var linked = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // issue #143: убиваем процесс и прерываем чтение — иначе ожидание
                // вывода может не завершиться никогда (поток держит потомок).
                var canceled = ct.IsCancellationRequested;
                Kill(process);
                readCts.Cancel();

                var stdOut = await SafeReadOutput(stdoutBytesTask, stdoutText, rawReading, readCts).ConfigureAwait(false);
                var stdErr = await SafeReadOutput(stderrBytesTask, stderrText, rawReading, readCts).ConfigureAwait(false);
                if (canceled)
                    ct.ThrowIfCancellationRequested();

                return new ProcessRunResult
                {
                    TimedOut = true,
                    StdOut = stdOut,
                    StdErr = stdErr
                };
            }
            finally
            {
                // Процесс не должен переживать вызов ни при каком исходе.
                if (!process.HasExited)
                    Kill(process);
            }

            // issue #143 (повторное обращение): процесс уже завершился, но потомок мог
            // унаследовать stdout/stderr и держать трубу открытой. Раньше ожидание
            // ReadToEnd висело бесконечно — «Сделать копию сейчас» не завершалась,
            // и кнопка «Отмена» не помогала. Ждём вывод ограниченное время, затем
            // закрываем трубу и возвращаем то, что успели прочитать.
            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                StdOut = await SafeReadOutput(stdoutBytesTask, stdoutText, rawReading, readCts).ConfigureAwait(false),
                StdErr = await SafeReadOutput(stderrBytesTask, stderrText, rawReading, readCts).ConfigureAwait(false)
            };
        }
    }

    /// <summary>
    /// Короткая проверка утилиты окружения (issue #143): «--version», «pg_isready»,
    /// «sqlcmd -Q SELECT 1». Таймаут по умолчанию — 10 секунд.
    /// </summary>
    public static Task<ProcessRunResult> ProbeAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => RunAsync(fileName, arguments, null, timeout ?? TimeSpan.FromSeconds(10), null, null, ct);

    /// <summary>
    /// Убивает процесс вместе с потомками и коротко ждёт завершения. Ошибки
    /// игнорируются: процесс мог уже завершиться или быть чужим.
    /// </summary>
    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
        try { process.WaitForExit(2000); } catch { /* нет доступа/уже закрыт */ }
    }

    /// <summary>Читает поток до конца в массив байтов (без декодирования), с отменой.</summary>
    private static async Task<byte[]> ReadToEndBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Предельное ожидание вывода после завершения процесса (issue #143). Потомок,
    /// унаследовавший stdout/stderr, может держать трубу открытой неограниченно;
    /// без этого предела ReadToEnd/копирование потока висело бы вечно.
    /// </summary>
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Читает вывод: сырое чтение — декодирование <see cref="ConsoleOutputDecoder"/>;
    /// при явной кодировке — готовый текст StreamReader. Ожидание ограничено
    /// <see cref="OutputDrainTimeout"/>; по истечении труба закрывается, а задача
    /// чтения наблюдается, чтобы не оставить необработанное исключение.
    /// </summary>
    private static async Task<string> SafeReadOutput(
        Task<byte[]> bytesTask, Task<string> textTask, bool rawReading,
        CancellationTokenSource? readCts = null)
    {
        try
        {
            var readTask = rawReading
                ? DecodeAsync(bytesTask)
                : textTask;
            var completed = await Task.WhenAny(readTask, Task.Delay(OutputDrainTimeout)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, readTask))
            {
                // Потомок держит трубу — не ждём бесконечно.
                try { readCts?.Cancel(); } catch (ObjectDisposedException) { }
                _ = readTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                return string.Empty;
            }
            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> DecodeAsync(Task<byte[]> bytesTask) =>
        ConsoleOutputDecoder.Decode(await bytesTask.ConfigureAwait(false));
}
