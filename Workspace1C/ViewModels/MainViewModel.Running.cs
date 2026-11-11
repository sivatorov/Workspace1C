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

            // Issue #103 (v1.0.1.35): двухпроходное сопоставление — сначала прямые ключи
            // /F//S, затем /IBName по имени базы. Запуск отладки из Конфигуратора
            // порождает клиентский процесс «ENTERPRISE /IBName <имя>» без /F//S —
            // значок «Предприятие» раньше не появлялся.
            var matchedByBase = RunningInfobaseMatcher.MatchProcesses(Infobases, processes);
            foreach (var ib in Infobases)
            {
                // База считается запущенной, если хотя бы один процесс 1С подключён к ней.
                var matching = matchedByBase.TryGetValue(ib.Id, out var list)
                    ? list
                    : (System.Collections.Generic.IReadOnlyList<RunningOneCProcess>)
                        System.Array.Empty<RunningOneCProcess>();
                var running = matching.Count > 0;

                // issue #310: если среди процессов базы есть «не отвечающий» —
                // индикаторы становятся оранжевыми/красными вместо зелёных.
                var notResponding = running && matching.Any(p => !p.IsResponding);

                // Индикатор режимов: значок Play — 1С:Предприятие (толстый клиент без
                // DESIGNER и тонкий клиент 1cv8c), ключ — Конфигуратор (DESIGNER/CONFIG).
                var enterpriseProcesses = matching
                    .Where(p => RunningKindClassifier.Classify(p.ProcessName, p.CommandLine) == RunningKind.Enterprise)
                    .ToList();
                var configuratorProcesses = matching
                    .Where(p => RunningKindClassifier.Classify(p.ProcessName, p.CommandLine) == RunningKind.Configurator)
                    .ToList();
                ib.IsEnterpriseRunning = running && enterpriseProcesses.Count > 0;
                ib.IsConfiguratorRunning = running && configuratorProcesses.Count > 0;

                // Issue #103 (v1.0.1.49): маркер отладки — хотя бы один Enterprise-процесс
                // базы запущен с ключом /DEBUG (свой сеанс; см. ограничение в issue).
                ib.IsEnterpriseDebugRunning = ib.IsEnterpriseRunning &&
                    enterpriseProcesses.Any(p => RunningInfobaseMatcher.HasDebugKey(p.CommandLine));

                // issue #101: зависание считается ПО РЕЖИМАМ раздельно — зависший
                // Конфигуратор красит только свой чип, Предприятие остаётся зелёным
                // (и наоборот). Общий флаг/счётчик (точка у имени базы) сохраняет
                // прежнее поведение — «любой процесс базы не отвечает».
                var enterpriseNotResponding = ib.IsEnterpriseRunning &&
                    enterpriseProcesses.Any(p => !p.IsResponding);
                var configuratorNotResponding = ib.IsConfiguratorRunning &&
                    configuratorProcesses.Any(p => !p.IsResponding);

                ib.IsEnterpriseNotResponding = enterpriseNotResponding;
                ib.EnterpriseNotRespondingStreak = enterpriseNotResponding
                    ? ib.EnterpriseNotRespondingStreak + 1 : 0;
                ib.IsConfiguratorNotResponding = configuratorNotResponding;
                ib.ConfiguratorNotRespondingStreak = configuratorNotResponding
                    ? ib.ConfiguratorNotRespondingStreak + 1 : 0;

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

    // ===================== Issue #120: переход к открытым окнам 1С =====================

    /// <summary>
    /// Issue #120: попытка активировать уже открытое окно Конфигуратора базы вместо
    /// запуска нового экземпляра (двойной щелчок, кнопка, трей, командная палитра).
    /// Возвращает <c>true</c>, если окно найдено и активировано. При устаревшем флаге
    /// монитора (окон фактически нет) возвращает <c>false</c> — вызывающий код
    /// выполняет обычный запуск. На Linux активация окон не поддержана — всегда
    /// <c>false</c> (graceful fallback: обычный запуск).
    /// Вызов может занять время (обход процессов) — из UI-потока вызывать через
    /// <see cref="Task.Run"/>.
    /// </summary>
    public bool TryActivateConfiguratorWindow(Infobase? infobase)
    {
        if (infobase is null || !infobase.IsConfiguratorRunning)
            return false;
        var windows = new List<RunningWindowInfo>();
        try
        {
            GetRunningWindows(infobase, RunningKind.Configurator, windows);
            if (windows.Count == 0)
            {
                LogRunningWindowMiss(infobase, RunningKind.Configurator);
                return false;
            }
            ActivateWindowByHandle(windows[0].Handle);
            return true;
        }
        catch
        {
            // Активация окна не должна ломать запуск: при ошибке — обычный запуск.
            return false;
        }
    }

    /// <summary>
    /// Issue #120: безопасное перечисление окон запущенного режима базы.
    /// Может занять время (WMI/обход процессов) — вызывать вне UI-потока.
    /// Ошибки тихо превращаются в пустой список (политика монитора запущенных баз).
    /// </summary>
    public IReadOnlyList<RunningWindowInfo> GetRunningWindowsSafe(Infobase? infobase, RunningKind kind)
    {
        var windows = new List<RunningWindowInfo>();
        if (infobase is null)
            return windows;
        try
        {
            GetRunningWindows(infobase, kind, windows);
        }
        catch
        {
            // Политика монитора: тихая деградация — пустой список.
        }
        return windows;
    }

    /// <summary>
    /// Issue #120 (v1.0.1.54): диагностика «индикатор показывает запущенный режим,
    /// но открытых окон не найдено» — именно так выглядела жалоба репортера «клик
    /// не работает, ничего не происходит». Причины: matcher процессов не распознал
    /// клиент (тонкий 1cv8c, /S"server/base"), WMI недоступен (права/политика),
    /// флаг монитора устарел. Запись в журнал делает «ничего не происходит»
    /// диагностируемым. На Linux перечисление окон не поддержано (список всегда
    /// пуст) — лог не пишется, чтобы не засорять журнал. Ошибки логирования
    /// игнорируются (не должны ломать вызывающий код).
    /// </summary>
    public void LogRunningWindowMiss(Infobase infobase, RunningKind kind)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            _logger.Info(
                $"Активация окна {kind}: у базы «{infobase.Name}» установлен флаг запущенного " +
                "режима, но открытые окна не найдены (matcher процессов / права WMI / устаревший " +
                "флаг монитора) — выполняется fallback на обычный запуск (issue #120)");
        }
        catch
        {
            // Логирование не должно ломать активацию/запуск.
        }
    }

    /// <summary>Issue #120: активация окна по хэндлу из списка выбора; ошибки игнорируются.</summary>
    public void ActivateWindowByHandle(long handle)
    {
        if (handle == 0)
            return;
        try
        {
            ActivateWindowHandle(handle);
        }
        catch
        {
            // Активация окна не должна ломать интерфейс.
        }
    }

    /// <summary>
    /// Issue #120: переход к первому окну запущенного режима базы (используется для
    /// клика по чипу «Конфигуратор» и как fallback для чипа «Предприятие»).
    /// Перечисление окон выполняется вне UI-потока.
    /// </summary>
    public void ActivateRunningWindow(Infobase? infobase, RunningKind kind)
    {
        if (infobase is null)
            return;
        Task.Run(() =>
        {
            var windows = GetRunningWindowsSafe(infobase, kind);
            if (windows.Count > 0)
                ActivateWindowByHandle(windows[0].Handle);
        });
    }

    /// <summary>
    /// Платформенное перечисление окон 1С режима kind, подключённых к базе:
    /// Windows — EnumWindows + фильтр <see cref="RunningKindClassifier"/> по PID
    /// процессов базы; Linux — реализация отсутствует, список остаётся пустым
    /// (активация окон не поддержана, graceful fallback issue #120).
    /// </summary>
    partial void GetRunningWindows(Infobase infobase, RunningKind kind, List<RunningWindowInfo> windows);

    /// <summary>Платформенная активация окна по хэндлу (Windows — Win32; Linux — no-op).</summary>
    partial void ActivateWindowHandle(long handle);
}
