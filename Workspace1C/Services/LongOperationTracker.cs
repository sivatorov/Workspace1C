using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Наблюдение за одним режимом базы на одном опросе монитора (issue #131).
/// Несколько процессов одной базы/режима агрегируются вызывающим кодом:
/// <see cref="IsResponding"/> = все процессы отвечают, <see cref="WindowTitle"/> —
/// заголовок не отвечающего (или первого найденного) окна.
/// </summary>
/// <param name="InfobaseId">Идентификатор базы списка.</param>
/// <param name="InfobaseName">Отображаемое имя базы (для текста уведомления).</param>
/// <param name="Mode">Режим: Предприятие или Конфигуратор.</param>
/// <param name="IsRunning">Процесс режима запущен.</param>
/// <param name="IsResponding">Процесс отвечает на сообщения.</param>
/// <param name="WindowTitle">Полный заголовок главного окна (может быть пустым).</param>
public readonly record struct LongOperationObservation(
    string InfobaseId,
    string InfobaseName,
    RunningKind Mode,
    bool IsRunning,
    bool IsResponding,
    string? WindowTitle);

/// <summary>
/// Событие «длительная операция завершилась» (issue #131): окно было занято
/// дольше порога и снова отвечает. Отправляется один раз на цикл занятости.
/// </summary>
/// <param name="InfobaseId">Идентификатор базы.</param>
/// <param name="InfobaseName">Имя базы.</param>
/// <param name="Mode">Режим (Предприятие/Конфигуратор).</param>
/// <param name="WindowTitle">Заголовок окна на момент занятости.</param>
/// <param name="Duration">Сколько окно было недоступно.</param>
public sealed record LongOperationRelease(
    string InfobaseId,
    string InfobaseName,
    RunningKind Mode,
    string WindowTitle,
    TimeSpan Duration);

/// <summary>
/// Машина состояний «занятости» окон 1С (issue #131). Чистая логика без
/// зависимости от UI и таймеров: монитор передаёт наблюдения, трекер возвращает
/// события освобождения.
/// <para>
/// Ключ агрегации — пара (база, режим), поэтому несколько окон одной базы не
/// дают спам: событие формируется один раз на цикл «занято → освободилось».
/// Гистерезис: освобождение подтверждается <c>releaseConfirmations</c> опросами
/// подряд (по умолчанию 2), чтобы кратковременный отклик не считался
/// завершением. Если процесс закрылся, не освободившись, событие не
/// отправляется.
/// </para>
/// </summary>
public sealed class LongOperationTracker
{
    private readonly int _releaseConfirmations;
    private readonly Dictionary<(string InfobaseId, RunningKind Mode), BusyState> _states = new();

    /// <param name="releaseConfirmations">
    /// Сколько подряд опросов «отвечает» нужно для подтверждения освобождения
    /// (минимум 1; по умолчанию 2).
    /// </param>
    public LongOperationTracker(int releaseConfirmations = 2)
    {
        _releaseConfirmations = Math.Max(1, releaseConfirmations);
    }

    /// <summary>Текущее число отслеживаемых занятых окон (для тестов/диагностики).</summary>
    public int TrackedCount => _states.Count;

    /// <summary>
    /// Применяет результаты опроса и возвращает события освобождения, у которых
    /// длительность занятости достигла <paramref name="threshold"/>.
    /// </summary>
    /// <param name="observations">Наблюдения текущего опроса.</param>
    /// <param name="threshold">Порог уведомления (см. <see cref="LongOperationThreshold"/>).</param>
    /// <param name="now">Момент опроса (UTC).</param>
    public IReadOnlyList<LongOperationRelease> Observe(
        IEnumerable<LongOperationObservation> observations,
        TimeSpan threshold,
        DateTimeOffset now)
    {
        var releases = new List<LongOperationRelease>();
        var seen = new HashSet<(string InfobaseId, RunningKind Mode)>();

        foreach (var observation in observations)
        {
            if (!observation.IsRunning || string.IsNullOrWhiteSpace(observation.InfobaseId))
                continue;

            var key = (observation.InfobaseId, observation.Mode);
            seen.Add(key);

            if (!_states.TryGetValue(key, out var state))
            {
                state = new BusyState { Name = observation.InfobaseName ?? "" };
                _states[key] = state;
            }
            else if (!string.IsNullOrWhiteSpace(observation.InfobaseName))
            {
                state.Name = observation.InfobaseName;
            }

            if (!string.IsNullOrWhiteSpace(observation.WindowTitle))
                state.WindowTitle = observation.WindowTitle!.Trim();

            if (!observation.IsResponding)
            {
                // Начало (или продолжение) занятости: фиксируем первый момент.
                state.BusySince ??= now;
                state.RespondingStreak = 0;
                continue;
            }

            if (state.BusySince is not { } busySince)
                continue; // окно и не было занято — событие не нужно

            state.RespondingStreak++;
            if (state.RespondingStreak < _releaseConfirmations)
                continue;

            var duration = now - busySince;
            if (duration >= threshold && duration > TimeSpan.Zero)
            {
                releases.Add(new LongOperationRelease(
                    observation.InfobaseId,
                    state.Name,
                    observation.Mode,
                    state.WindowTitle,
                    duration));
            }

            // Цикл занятости закрыт (даже если порог не достигнут) — повторное
            // уведомление возможно только после новой занятости.
            _states.Remove(key);
        }

        // Процессы, которых больше нет в опросе, закрылись без освобождения —
        // уведомление не отправляем, состояние сбрасываем.
        foreach (var key in _states.Keys.Where(k => !seen.Contains(k)).ToList())
            _states.Remove(key);

        return releases;
    }

    /// <summary>Сбрасывает всё состояние (выключение мониторинга/смена настроек).</summary>
    public void Reset() => _states.Clear();

    private sealed class BusyState
    {
        public DateTimeOffset? BusySince;
        public string Name = "";
        public string WindowTitle = "";
        public int RespondingStreak;
    }
}
