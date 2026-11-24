using System;
using System.Collections.Concurrent;

namespace Workspace1C.Services;

/// <summary>
/// Выполнение блокирующего ввода-вывода на выделенном потоке (issue #143).
/// <para>
/// <see cref="System.IO.Directory.CreateDirectory(string)"/>, <see cref="System.IO.File"/>,
/// запуск процессов и подобные вызовы не принимают <see cref="CancellationToken"/> и на
/// недоступном SMB/UNC-ресурсе могут «висеть» минутами. На UI-потоке это даёт «наглухо»
/// зависшее окно, а на потоке пула — голодание пула, из-за которого перестают
/// срабатывать таймеры (<c>CancelAfter</c>, <c>WaitAsync</c>).
/// </para>
/// <para>
/// Здесь работа уходит на поток, созданный с <see cref="TaskCreationOptions.LongRunning"/>
/// (либо на собственный <see cref="Thread"/>), поэтому пул потоков не расходуется.
/// Асинхронный вариант использует собственный однопоточный
/// <see cref="SynchronizationContext"/>: продолжения операции не попадают в пул.
/// </para>
/// </summary>
public static class BlockingIo
{
    /// <summary>Синхронная операция на выделенном потоке.</summary>
    public static Task Run(Action action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        DbmsBackupTrace.Log("enqueue", "blocking-io");
        return Task.Factory.StartNew(() =>
        {
            ct.ThrowIfCancellationRequested();
            DbmsBackupTrace.Log("worker-start", "blocking-io");
            action();
        }, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    /// <summary>Синхронная операция с результатом на выделенном потоке.</summary>
    public static Task<T> Run<T>(Func<T> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        DbmsBackupTrace.Log("enqueue", "blocking-io");
        return Task.Factory.StartNew(() =>
        {
            ct.ThrowIfCancellationRequested();
            DbmsBackupTrace.Log("worker-start", "blocking-io");
            return func();
        }, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Асинхронная операция на выделенном потоке с собственным контекстом
    /// синхронизации: продолжения не используют пул потоков.
    /// </summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        var completion = new TaskCompletionSource<T>();
        var thread = new Thread(() => RunOnDedicatedThread(operation, ct, completion))
        {
            IsBackground = true,
            Name = "dbms-blocking-io"
        };
        DbmsBackupTrace.Log("enqueue", "blocking-io-async");
        thread.Start();
        return completion.Task;
    }

    /// <summary>Асинхронная операция без результата на выделенном потоке.</summary>
    public static Task RunAsync(Func<Task> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, ct);
    }

    private static void RunOnDedicatedThread<T>(Func<Task<T>> operation,
        CancellationToken ct, TaskCompletionSource<T> completion)
    {
        var context = new SingleThreadContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            DbmsBackupTrace.Log("worker-start", "blocking-io-async");
            var task = operation();
            _ = task.ContinueWith(t =>
            {
                if (t.IsCanceled)
                    completion.TrySetCanceled();
                else if (t.IsFaulted)
                    completion.TrySetException(t.Exception!.InnerExceptions);
                else
                    completion.TrySetResult(t.Result);
                context.Complete();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            context.Pump(ct, () => completion.TrySetCanceled(ct));
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(ct);
            context.Complete();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            context.Complete();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// Контекст синхронизации выделенного потока: выполняет продолжения операции
    /// на том же потоке и не задействует пул.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<Action> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add(() => d(state));
            }
            catch
            {
                // Очередь уже закрыта — операция отменена/завершена.
            }
        }

        public void Complete()
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch
            {
                // Уже закрыта.
            }
        }

        public void Pump(CancellationToken ct, Action onCanceled)
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    onCanceled();
                    return;
                }

                if (_queue.TryTake(out var work, 50))
                {
                    try
                    {
                        work();
                    }
                    catch
                    {
                        // Ошибка продолжения уже передана в Task операции.
                    }
                    continue;
                }

                if (_queue.IsCompleted)
                    return;
            }
        }
    }
}
