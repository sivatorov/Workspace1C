using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Монитор запущенных баз (частичный класс <see cref="MainViewModel"/>): раз в 10 секунд
/// запрашивает список процессов платформы 1С и сопоставляет их командные строки с базами
/// списка (<see cref="RunningInfobaseMatcher"/>). Результат — флаг <c>Infobase.IsRunning</c>,
/// который рисует зелёную точку у имени базы. Платформенная часть — только доставка
/// результата в UI-поток (<c>DispatchOnUi</c>: реализация в Windows/Avalonia-партиалах).
/// </summary>
public partial class MainViewModel
{
    private Timer? _runningBasesTimer;
    private int _runningRefreshBusy;

    /// <summary>Период опроса процессов 1С (миллисекунды).</summary>
    private const int RunningBasesIntervalMs = 10_000;

    /// <summary>
    /// Запускает монитор запущенных баз (повторный вызов игнорируется).
    /// Вызывается из окна после построения списка — первый опрос сразу, далее по таймеру.
    /// </summary>
    public void StartRunningBasesMonitor()
    {
        if (_runningBasesTimer is not null)
            return;

        RefreshRunningFlags();
        _runningBasesTimer = new Timer(_ => RefreshRunningFlags(), null,
            RunningBasesIntervalMs, RunningBasesIntervalMs);
    }

    /// <summary>Внеплановый опрос (после запуска базы — точка появится, не дожидаясь таймера).</summary>
    public void RefreshRunningFlags()
    {
        // Один опрос за раз: длительный WMI не должен копить очередь задач.
        if (Interlocked.Exchange(ref _runningRefreshBusy, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                var processes = AppServices.TryGetService<IRunningInfobasesService>()
                    ?.GetRunning() ?? Array.Empty<RunningOneCProcess>();
                DispatchOnUi(() => ApplyRunningProcesses(processes));
            }
            catch
            {
                // Монитор не должен влиять на работу приложения.
            }
            finally
            {
                Interlocked.Exchange(ref _runningRefreshBusy, 0);
            }
        });
    }

    /// <summary>Применяет результаты опроса к базам списка (только в UI-потоке).</summary>
    private void ApplyRunningProcesses(System.Collections.Generic.IReadOnlyList<RunningOneCProcess> processes)
    {
        try
        {
            var before = new HashSet<string>(
                Infobases.Where(i => i.IsRunning).Select(i => i.Id),
                StringComparer.Ordinal);

            foreach (var ib in Infobases)
            {
                // База считается запущенной, если хотя бы один процесс 1С подключён к ней.
                var matching = processes
                    .Where(p => RunningInfobaseMatcher.MatchesCommandLine(ib, p.CommandLine))
                    .ToList();
                var running = matching.Count > 0;

                // issue #310: если среди процессов базы есть «не отвечающий» —
                // индикаторы становятся оранжевыми/красными вместо зелёных.
                var notResponding = running && matching.Any(p => !p.IsResponding);

                // Индикатор режимов: значок Play — 1С:Предприятие (толстый клиент без
                // DESIGNER и тонкий клиент 1cv8c), ключ — Конфигуратор (DESIGNER/CONFIG).
                ib.IsEnterpriseRunning = running && matching.Any(
                    p => RunningKindClassifier.Classify(p.ProcessName, p.CommandLine) == RunningKind.Enterprise);
                ib.IsConfiguratorRunning = running && matching.Any(
                    p => RunningKindClassifier.Classify(p.ProcessName, p.CommandLine) == RunningKind.Configurator);

                ib.IsNotResponding = notResponding;
                ib.NotRespondingStreak = notResponding ? ib.NotRespondingStreak + 1 : 0;
            }

            // issue #339: в режиме «Только запущенные» изменение состава запущенных баз
            // должно сразу отражаться в списке. Раньше флаги обновлялись, но список не
            // пересобирался: база, дозапустившаяся после включения отбора, не появлялась,
            // а закрытая/вылетевшая — не исчезала (до переключения режима).
            var after = new HashSet<string>(
                Infobases.Where(i => i.IsRunning).Select(i => i.Id),
                StringComparer.Ordinal);
            if (IsRunningOnlyModeActive() &&
                (before.Count != after.Count || !before.SetEquals(after)))
                RefreshListAfterRunningFlagsChanged();
        }
        catch
        {
            // Применение результатов не должно ломать интерфейс.
        }
    }

    /// <summary>
    /// Пересобирает видимый список после изменения состава запущенных баз в режиме
    /// «Только запущенные» (issue #339). Платформенная реализация: WPF — RebuildGroupTree,
    /// Avalonia — ApplyFilter.
    /// </summary>
    partial void RefreshListAfterRunningFlagsChanged();

    /// <summary>
    /// Активен ли отбор «Только запущенные» (issue #339). Платформенная реализация:
    /// WPF — флаг <c>_showRunningOnly</c>, Avalonia — режим списка <c>_listMode</c>.
    /// </summary>
    private partial bool IsRunningOnlyModeActive();

    /// <summary>
    /// Активирует окно уже запущенной базы 1С (issue #339): при включённом отборе
    /// «Только запущенные» двойной клик и Enter вместо повторного запуска поднимают
    /// окно процесса 1С этой базы. Платформенная реализация — в партиалах.
    /// </summary>
    public void ActivateRunningInfobase(Infobase infobase)
    {
        if (infobase is null || !infobase.IsRunning)
            return;
        try
        {
            ActivateRunningInfobaseCore(infobase);
        }
        catch
        {
            // Активация окна не должна ломать интерфейс: при любой ошибке тихо пропускаем.
        }
    }

    /// <summary>Платформенная активация окна процесса 1С базы.</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase);

    /// <summary>Выполняет действие в UI-потоке (платформенная реализация).</summary>
    partial void DispatchOnUi(Action action);
}
