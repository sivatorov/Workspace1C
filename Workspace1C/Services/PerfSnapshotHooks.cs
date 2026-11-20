using System;
using System.Collections.Generic;
using Workspace1C.Services.TechLogParser;

namespace Workspace1C.Services;

/// <summary>
/// Данные для снимка трендов производительности (ПРО 2.x): прочитанные события
/// технологического журнала за день и выбранный в окне анализа фильтр базы.
/// </summary>
/// <param name="InfobaseFilter">Фильтр базы из окна анализа ТЖ (пусто — все базы).</param>
/// <param name="Date">День, за который строится снимок.</param>
/// <param name="Events">События ТЖ, уже прочитанные окном анализа.</param>
public sealed record PerfSnapshotContext(string InfobaseFilter, DateTime Date,
    IReadOnlyCollection<TechLogEvent> Events);

/// <summary>
/// Точка расширения «анализ ТЖ завершён» (ПРО <c>PerformanceTrends</c>). Публичное
/// окно анализа вызывает <see cref="Notify"/> после чтения журнала; модуль ПРО
/// подписывается на <see cref="Sink"/> и сохраняет дневную сводку. Без модуля
/// вызов ничего не делает, ошибка приёмника не ломает анализ.
/// </summary>
public static class PerfSnapshotHooks
{
    /// <summary>Приёмник снимков (модуль трендов). null — модуль не подключён.</summary>
    public static Action<PerfSnapshotContext>? Sink { get; set; }

    /// <summary>Сообщает о завершённом анализе; исключения приёмника подавляются.</summary>
    public static void Notify(PerfSnapshotContext context)
    {
        var sink = Sink;
        if (sink is null)
            return;
        try
        {
            sink(context);
        }
        catch
        {
            // Снимок трендов не должен ломать анализ ТЖ.
        }
    }

    /// <summary>Снимает приёмник (тесты, выгрузка модуля).</summary>
    public static void Clear() => Sink = null;
}
