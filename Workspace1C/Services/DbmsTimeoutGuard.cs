using System;
using System.Threading.Tasks;

namespace Workspace1C.Services;

/// <summary>
/// Сторожевой таймаут операции, не зависящий от пула потоков (issue #143).
/// <para>
/// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> и
/// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> используют таймеры,
/// продолжения которых ставятся в пул потоков. Если пул исчерпан (sync-over-async,
/// зависший SMB-вызов на потоке пула), таймаут <b>не срабатывает</b> — именно поэтому
/// в окне «Резервные копии СУБД» не наступали 15/30 секунд.
/// </para>
/// <para>
/// Здесь решение о таймауте принимает отдельный выделенный поток
/// (<see cref="System.Threading.Thread"/>), который ждёт на
/// <see cref="System.Threading.ManualResetEventSlim"/> и завершает ожидание через
/// <see cref="TaskCompletionSource{TResult}"/> без
/// <c>RunContinuationsAsynchronously</c> — продолжение выполняется на потоке,
/// захватившем <see cref="SynchronizationContext"/> (UI-поток WPF/Avalonia), а не в пуле.
/// </para>
/// </summary>
public static class DbmsTimeoutGuard
{
    /// <summary>
    /// Выполняет <paramref name="operation"/> и возвращает управление не позже
    /// <paramref name="timeout"/>. По истечении — <see cref="TimeoutException"/>
    /// (токен операции при этом отменяется, а сама операция может завершиться позже).
    /// </summary>
    /// <param name="operation">Асинхронная операция, принимающая токен отмены.</param>
    /// <param name="timeout">Предельное время ожидания.</param>
    /// <param name="ct">Отмена пользователем (кнопка «Отмена», закрытие окна).</param>
    /// <param name="stage">Название этапа для журнала диагностики.</param>
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout, CancellationToken ct = default, string stage = "operation")
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signal = new TaskCompletionSource<GuardOutcome>();
        var stop = new ManualResetEventSlim(false);
        using var registration = ct.Register(() => stop.Set());

        var timer = new Thread(() =>
        {
            try
            {
                // WaitOne с предельным временем: пробуждение по отмене пользователем
                // (stop.Set) или по истечении таймаута. Пул потоков не участвует.
                if (stop.Wait(timeout))
                {
                    signal.TrySetResult(GuardOutcome.Canceled);
                    return;
                }

                DbmsBackupTrace.Warn("timeout-fired",
                    $"{stage}: превышено {timeout.TotalSeconds:0.###} с");
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
                signal.TrySetResult(GuardOutcome.TimedOut);
            }
            catch (ObjectDisposedException)
            {
                signal.TrySetResult(GuardOutcome.Canceled);
            }
            catch (Exception ex)
            {
                signal.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "dbms-timeout-" + stage
        };

        DbmsBackupTrace.Log("timeout-armed", $"{stage}: {timeout.TotalSeconds:0.###} с");
        timer.Start();

        Task<T> operationTask;
        try
        {
            operationTask = operation(cts.Token);
        }
        catch
        {
            stop.Set();
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            throw;
        }

        try
        {
            var finished = await Task.WhenAny(operationTask, signal.Task).ConfigureAwait(true);
            if (ReferenceEquals(finished, operationTask))
                return await operationTask.ConfigureAwait(true);

            var outcome = await signal.Task.ConfigureAwait(true);
            if (outcome == GuardOutcome.TimedOut)
            {
                Observe(operationTask);
                throw new TimeoutException(
                    $"{stage}: операция не завершилась за {timeout.TotalSeconds:0.###} с");
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException(cts.Token);
        }
        finally
        {
            // Будим сторожевой поток и отменяем операцию — выделенный поток не
            // должен ждать полный таймаут после успешного завершения.
            stop.Set();
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    /// <summary>Асинхронная операция без результата.</summary>
    public static Task RunAsync(Func<CancellationToken, Task> operation, TimeSpan timeout,
        CancellationToken ct = default, string stage = "operation")
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, timeout, ct, stage);
    }

    /// <summary>Наблюдает «осиротевшую» операцию, чтобы её исключение не стало необработанным.</summary>
    private static void Observe(Task task)
    {
        _ = task.ContinueWith(static t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private enum GuardOutcome
    {
        Canceled,
        TimedOut
    }
}
