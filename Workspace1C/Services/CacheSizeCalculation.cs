using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая (без UI) логика расчёта размеров кэша для окна «Очистка кэша» (issue #157):
/// <list type="bullet">
/// <item><see cref="Order"/> — детерминированный приоритетный порядок баз: отмеченные
/// первыми, внутри групп сохраняется исходный порядок основного списка;</item>
/// <item><see cref="MaxConcurrency"/> — разумный предел одновременных расчётов,
/// чтобы при 115 базах не запускать 230 задач сразу;</item>
/// <item><see cref="CacheSizeProgress"/> — счётчик готовых баз для строки состояния;</item>
/// <item><see cref="CacheSizeQueue"/> — очередь ожидающих баз с возможностью поднять
/// приоритет ещё не посчитанной базы при смене отметки.</item>
/// </list>
/// Класс общий для WPF- и Avalonia-версий окна, поэтому не содержит платформенных
/// зависимостей и полностью тестируем.
/// </summary>
public static class CacheSizeCalculation
{
    /// <summary>Нижняя граница одновременных расчётов баз.</summary>
    public const int MinDegreeOfParallelism = 2;

    /// <summary>Верхняя граница одновременных расчётов баз.</summary>
    public const int MaxDegreeOfParallelism = 4;

    /// <summary>
    /// Предел одновременных расчётов баз: <see cref="Environment.ProcessorCount"/>,
    /// зажатый в диапазон 2…4. Меньше двух — заметное замедление полного расчёта,
    /// больше четырёх — конкуренция за диск без выигрыша.
    /// </summary>
    public static int MaxConcurrency =>
        Math.Clamp(Environment.ProcessorCount, MinDegreeOfParallelism, MaxDegreeOfParallelism);

    /// <summary>
    /// Стабильно упорядочивает базы: сначала отмеченные (в порядке исходного списка),
    /// затем остальные (тоже в исходном порядке). Устойчив к <c>null</c>-списку,
    /// <c>null</c>-предикату и <c>null</c>-элементам.
    /// </summary>
    public static IReadOnlyList<Infobase> Order(
        IEnumerable<Infobase>? all, Func<Infobase, bool>? isSelected)
    {
        if (all is null)
            return Array.Empty<Infobase>();

        var selected = new List<Infobase>();
        var rest = new List<Infobase>();
        foreach (var infobase in all)
        {
            if (infobase is null)
                continue;
            if (isSelected is not null && isSelected(infobase))
                selected.Add(infobase);
            else
                rest.Add(infobase);
        }

        selected.AddRange(rest);
        return selected;
    }
}

/// <summary>
/// Прогресс расчёта размеров кэша (issue #157): сколько баз уже посчитано из скольких.
/// Изменяется только из потока интерфейса — окна вызывают <see cref="ReportCompleted"/>
/// внутри UI-диспетчера, поэтому блокировка не нужна.
/// </summary>
public sealed class CacheSizeProgress
{
    public CacheSizeProgress(int total)
    {
        Total = Math.Max(0, total);
    }

    /// <summary>Всего баз в очереди расчёта.</summary>
    public int Total { get; }

    /// <summary>Сколько баз уже обработано (успешно или с ошибкой).</summary>
    public int Completed { get; private set; }

    /// <summary>Все базы обработаны (для пустой очереди — сразу <c>true</c>).</summary>
    public bool IsComplete => Completed >= Total;

    /// <summary>Процент готовности 0…100; при <see cref="Total"/> = 0 — 100 без деления на ноль.</summary>
    public int Percent => Total <= 0
        ? 100
        : Math.Clamp((int)Math.Round(Completed * 100.0 / Total), 0, 100);

    /// <summary>Ключ локализации текущего состояния строки расчёта.</summary>
    public string StatusKey => IsComplete
        ? "CacheClean.CalculatingDone"
        : "CacheClean.Calculating";

    /// <summary>Отмечает одну базу как обработанную; после <see cref="Total"/> вызовов — готово.</summary>
    public void ReportCompleted()
    {
        if (Completed < Total)
            Completed++;
    }
}

/// <summary>
/// Очередь ожидающих расчёта баз (issue #157). Обходится несколькими воркерами,
/// поэтому операции защищены блокировкой. <see cref="Prioritize"/> переносит ещё
/// не посчитанную базу в начало очереди: смена отметки во время расчёта поднимает
/// её приоритет, но не перезапускает уже идущие расчёты.
/// </summary>
public sealed class CacheSizeQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<Infobase> _items = new();
    private readonly HashSet<Infobase> _pending = new();

    public CacheSizeQueue(IEnumerable<Infobase> ordered)
    {
        foreach (var infobase in ordered)
        {
            if (infobase is null || !_pending.Add(infobase))
                continue;
            _items.AddLast(infobase);
        }
    }

    /// <summary>Сколько баз ещё ожидает расчёта.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _items.Count;
        }
    }

    /// <summary>Берёт следующую базу из начала очереди; <c>false</c> — очередь пуста.</summary>
    public bool TryDequeue(out Infobase? infobase)
    {
        lock (_gate)
        {
            if (_items.First is null)
            {
                infobase = null;
                return false;
            }

            infobase = _items.First.Value;
            _items.RemoveFirst();
            _pending.Remove(infobase);
            return true;
        }
    }

    /// <summary>
    /// Поднимает приоритет ещё не посчитанной базы: переносит её в начало очереди.
    /// Если база уже посчитана или её нет в очереди — ничего не делает.
    /// </summary>
    public void Prioritize(Infobase? infobase)
    {
        if (infobase is null)
            return;

        lock (_gate)
        {
            if (!_pending.Remove(infobase))
                return;
            _items.Remove(infobase);
            _items.AddFirst(infobase);
            _pending.Add(infobase);
        }
    }
}
