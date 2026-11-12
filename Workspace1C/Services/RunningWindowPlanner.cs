using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Окно запущенного процесса платформы 1С (issue #120): хэндл для активации,
/// PID владельца и заголовок. Хэндл платформенно-нейтральный (Windows — HWND
/// в <see cref="long"/>; на Linux перечисление окон не поддержано и списки пусты).
/// <para>
/// Issue #120 (v1.0.1.55, 7OH): в пунктах выбора окна вместо заголовка окна
/// (GUID/длинная строка) показываются «Предприятие/Конфигуратор + время запуска» —
/// для этого окно несёт режим запуска (<see cref="Kind"/>) и время старта процесса
/// (<see cref="StartedAt"/>, из сведений монитора процессов).
/// </para>
/// </summary>
public sealed record RunningWindowInfo(
    long Handle,
    int Pid,
    string Title,
    RunningKind Kind = RunningKind.None,
    DateTime? StartedAt = null);

/// <summary>Решение «запуск нового экземпляра или переход к открытому окну» (issue #120).</summary>
public enum RunningWindowAction
{
    /// <summary>Действия нет (режим не запущен / окон нет).</summary>
    None,

    /// <summary>Запустить новый экземпляр.</summary>
    LaunchNew,

    /// <summary>Активировать единственное открытое окно.</summary>
    ActivateSingle,

    /// <summary>Открытых окон несколько — показать выбор.</summary>
    ShowPicker
}

/// <summary>
/// Чистая логика решения «запуск или активация» (issue #120 «Переход к открытому
/// окну Конфигуратора/Предприятия»): Конфигуратор — активировать открытое окно
/// вместо запуска нового экземпляра (запуск нового — редкость, обсуждение issue);
/// Предприятие — по клику на значке-индикаторе переход к единственному окну либо
/// выбор окна при нескольких запущенных (кнопка «Предприятие» продолжает
/// запускать новый экземпляр — несколько экземпляров легитимны). Не зависит
/// от платформы и UI — покрыта юнит-тестами; платформенная активация окон —
/// в <see cref="OneCWindowActivator"/>.
/// </summary>
public static class RunningWindowPlanner
{
    /// <summary>
    /// Решение для запуска Конфигуратора (двойной щелчок, кнопка, трей, палитра):
    /// если окно Конфигуратора этой базы открыто — активировать его вместо
    /// запуска нового экземпляра.
    /// </summary>
    public static RunningWindowAction PlanConfiguratorLaunch(int openWindowCount)
        => openWindowCount > 0 ? RunningWindowAction.ActivateSingle : RunningWindowAction.LaunchNew;

    /// <summary>
    /// Решение для клика по значку-индикатору «Предприятие»: не запущено — ничего;
    /// одно окно — активировать; несколько — показать выбор окна.
    /// </summary>
    public static RunningWindowAction PlanEnterpriseIndicator(int openWindowCount)
        => openWindowCount switch
        {
            <= 0 => RunningWindowAction.None,
            1 => RunningWindowAction.ActivateSingle,
            _ => RunningWindowAction.ShowPicker
        };

    /// <summary>
    /// Issue #120 (v1.0.1.54): решение для двойного щелчка по строке при действии
    /// «1С:Предприятие» и запущенном Предприятии (обсуждение issue): сначала
    /// активировать уже открытое окно — одно окно активируется напрямую, несколько —
    /// выбор окна; окон нет (флаг монитора устарел / перечисление не удалось,
    /// на Linux список всегда пуст) — обычный запуск нового экземпляра.
    /// </summary>
    public static RunningWindowAction PlanEnterpriseDoubleClick(int openWindowCount)
        => openWindowCount switch
        {
            <= 0 => RunningWindowAction.LaunchNew,
            1 => RunningWindowAction.ActivateSingle,
            _ => RunningWindowAction.ShowPicker
        };

    /// <summary>
    /// Issue #120 (v1.0.1.58, RafaIS): двойной щелчок при запущенном Предприятии
    /// с учётом видимости колонки «Действия». Меню выбора окна при двойном щелчке
    /// показывается ТОЛЬКО при отключённой колонке «Действия»: «пользователи с
    /// колонкой запускают кнопками» — внезапное меню только путает. При включённой
    /// колонке и нескольких окнах активируется первое (верхнее по Z-порядку
    /// перечисления окон). Одно окно активируется напрямую, окон нет — запуск.
    /// </summary>
    public static RunningWindowAction PlanEnterpriseDoubleClick(int openWindowCount, bool showActionsColumn)
    {
        var planned = PlanEnterpriseDoubleClick(openWindowCount);
        return planned == RunningWindowAction.ShowPicker && showActionsColumn
            ? RunningWindowAction.ActivateSingle
            : planned;
    }

    /// <summary>
    /// Решение для действия «Активировать окно» (issue #120 v1.0.1.55) и для
    /// двойного щелчка при действии «Ничего» при запущенной базе: окон нет —
    /// ничего; одно окно — активировать напрямую; несколько — выбор окна.
    /// </summary>
    public static RunningWindowAction PlanAllWindows(int openWindowCount)
        => openWindowCount switch
        {
            <= 0 => RunningWindowAction.None,
            1 => RunningWindowAction.ActivateSingle,
            _ => RunningWindowAction.ShowPicker
        };

    /// <summary>
    /// Issue #120 (v1.0.1.58, RafaIS/7OH): двойной щелчок «Ничего»/«Активировать
    /// окно» (и двойной клик на вкладке «Запущенные») с учётом видимости колонки
    /// «Действия». Меню выбора окна показывается ТОЛЬКО при отключённой колонке
    /// «Действия» (RafaIS: «если колонка Действия отключена, то только тогда
    /// появляется это меню» — кнопки действия и так перед глазами, меню путает).
    /// При включённой колонке:
    /// <list type="bullet">
    /// <item>действие «Ничего» — ничего не делает (7OH: «Выбор — когда не указано
    /// действие»; колонка включена — двойной клик не путает);</item>
    /// <item>«Активировать окно»/вкладка «Запущенные» — активируется первое окно
    /// (верхнее по Z-порядку перечисления), явное намерение пользователя сохранить.</item>
    /// </list>
    /// </summary>
    /// <param name="openWindowCount">Число открытых окон базы (все режимы).</param>
    /// <param name="showActionsColumn">Видима ли колонка «Действия» (ShowActionsColumn).</param>
    /// <param name="nothingAction">true — действие двойного щелчка «Ничего».</param>
    public static RunningWindowAction PlanAllWindowsDoubleClick(
        int openWindowCount, bool showActionsColumn, bool nothingAction)
    {
        var planned = PlanAllWindows(openWindowCount);
        if (planned != RunningWindowAction.ShowPicker || !showActionsColumn)
            return planned;
        return nothingAction ? RunningWindowAction.None : RunningWindowAction.ActivateSingle;
    }

    /// <summary>
    /// Строка элемента выбора окна (issue #120 v1.0.1.55, 7OH): «Предприятие
    /// (запущено в 12:34:05)» / «Конфигуратор (запущено в 12:34:05)» — вместо
    /// заголовка окна (GUID/длинная строка), время запуска полезнее. Без режима
    /// или времени — прежний формат «Заголовок (PID 1234)».
    /// </summary>
    public static string FormatWindowTitle(RunningWindowInfo window)
    {
        var label = window.Kind switch
        {
            RunningKind.Enterprise => Localization.LocalizationManager.T("Running.PickerEnterprise"),
            RunningKind.Configurator => Localization.LocalizationManager.T("Running.PickerConfigurator"),
            _ => null
        };

        if (label is null)
        {
            var title = (window.Title ?? string.Empty).Trim();
            return title.Length > 0
                ? $"{title} (PID {window.Pid})"
                : $"PID {window.Pid}";
        }

        return window.StartedAt is { } started
            ? $"{label} ({string.Format(Localization.LocalizationManager.T("Running.PickerStartedAt"), started.ToString("HH:mm:ss"))})"
            : $"{label} (PID {window.Pid})";
    }
}
