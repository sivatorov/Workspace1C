using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Workspace1C.Tests.TestSupport;

/// <summary>
/// Ожидание применения отложенных записей журнала пакетных операций
/// (<see cref="ObservableCollection{T}"/> строк) и безопасное перечисление снимка.
/// <para>
/// <see cref="Workspace1C.ViewModels.BatchOperationsViewModel"/> для «чужого» потока
/// публикует строку через <see cref="SynchronizationContext.Post"/> (fire-and-forget),
/// поэтому сразу после <c>await RunAsync()</c> коллекция может быть ещё не заполнена,
/// а её перечисление в момент добавления элемента бросает
/// <see cref="InvalidOperationException"/> («Collection was modified»).
/// </para>
/// <para>
/// Helper опрашивает снимок <c>LogLines.ToArray()</c> с шагом
/// <see cref="PollStepMilliseconds"/> мс и таймаутом <see cref="DefaultTimeout"/>,
/// пока не выполнится условие, и возвращает последний снимок. Все проверки журнала
/// выполняются по снимку — гонка «добавление во время перечисления» исключена,
/// результат детерминирован.
/// </para>
/// </summary>
internal static class AsyncLogAssert
{
    /// <summary>Таймаут ожидания по умолчанию — 2 секунды.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Шаг опроса снимка журнала — 10 мс.</summary>
    internal const int PollStepMilliseconds = 10;

    /// <summary>
    /// Дожидается, пока снимок журнала удовлетворит <paramref name="condition"/>,
    /// и возвращает этот снимок. Если за <paramref name="timeout"/> условие не
    /// выполнилось — падает с сообщением <paramref name="description"/> и последним
    /// снимком журнала.
    /// </summary>
    internal static IReadOnlyList<string> WaitForSnapshot(
        ObservableCollection<string> logLines,
        Func<IReadOnlyList<string>, bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(logLines);
        ArgumentNullException.ThrowIfNull(condition);

        var limit = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var snapshot = TakeSnapshot(logLines);
            if (condition(snapshot))
                return snapshot;

            if (stopwatch.Elapsed >= limit)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{description}: условие не выполнено за {limit.TotalMilliseconds:F0} мс. " +
                    $"Журнал ({snapshot.Count} строк): {FormatLog(snapshot)}");
            }

            Thread.Sleep(PollStepMilliseconds);
        }
    }

    /// <summary>Дожидается появления хотя бы одной непустой строки журнала.</summary>
    internal static IReadOnlyList<string> WaitForNonEmpty(
        ObservableCollection<string> logLines,
        string description,
        TimeSpan? timeout = null)
        => WaitForSnapshot(logLines, s => s.Any(l => !string.IsNullOrWhiteSpace(l)), description, timeout);

    /// <summary>Дожидается появления строки, удовлетворяющей <paramref name="predicate"/>.</summary>
    internal static IReadOnlyList<string> WaitForLine(
        ObservableCollection<string> logLines,
        Func<string, bool> predicate,
        string description,
        TimeSpan? timeout = null)
        => WaitForSnapshot(logLines, s => s.Any(predicate), description, timeout);

    /// <summary>
    /// Дожидается, пока число строк, удовлетворяющих <paramref name="predicate"/>,
    /// станет равным <paramref name="expected"/>.
    /// </summary>
    internal static IReadOnlyList<string> WaitForCount(
        ObservableCollection<string> logLines,
        Func<string, bool> predicate,
        int expected,
        string description,
        TimeSpan? timeout = null)
        => WaitForSnapshot(logLines, s => s.Count(predicate) == expected, description, timeout);

    /// <summary>
    /// Снимок коллекции. <see cref="ObservableCollection{T}"/> не потокобезопасна:
    /// при добавлении элемента во время копирования получаем
    /// <see cref="InvalidOperationException"/> — это штатная ситуация гонки, опрос
    /// повторяется на следующем шаге.
    /// </summary>
    private static IReadOnlyList<string> TakeSnapshot(ObservableCollection<string> logLines)
    {
        try
        {
            return logLines.ToArray();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    private static string FormatLog(IReadOnlyList<string> snapshot)
    {
        if (snapshot.Count == 0)
            return "<пусто>";
        return string.Join(" | ", snapshot.Select(l => l.ReplaceLineEndings(" ")));
    }
}
