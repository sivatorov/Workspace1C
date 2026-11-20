using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реестр исполнителей заданий планировщика, добавляемых модулями ПРО (2.x).
/// Публичное ядро знает только встроенные виды <see cref="ScheduledTaskKind"/>;
/// модуль регистрирует обработчик своего вида в <c>RegisterProModules</c>. Без модуля
/// задание такого вида не выполняется (планировщик получает <c>null</c>).
/// </summary>
public static class SchedulerExecutors
{
    /// <summary>Обработчик задания: результат в формате планировщика.</summary>
    public delegate Task<BackupRunResult?> Executor(ScheduledTask task, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<ScheduledTaskKind, Executor> Handlers = new();

    /// <summary>Регистрирует (или заменяет) обработчик для вида задания.</summary>
    public static void Register(ScheduledTaskKind kind, Executor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        Handlers[kind] = executor;
    }

    /// <summary>Снимает обработчик (тесты, выгрузка модуля).</summary>
    public static void Unregister(ScheduledTaskKind kind) => Handlers.TryRemove(kind, out _);

    /// <summary>true — для вида задания зарегистрирован внешний обработчик.</summary>
    public static bool TryGet(ScheduledTaskKind kind, out Executor executor) =>
        Handlers.TryGetValue(kind, out executor!);

    /// <summary>Есть ли обработчик для вида задания.</summary>
    public static bool IsRegistered(ScheduledTaskKind kind) => Handlers.ContainsKey(kind);
}
