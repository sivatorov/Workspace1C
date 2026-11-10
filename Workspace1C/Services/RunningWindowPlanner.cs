using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Окно запущенного процесса платформы 1С (issue #120): хэндл для активации,
/// PID владельца и заголовок. Хэндл платформенно-нейтральный (Windows — HWND
/// в <see cref="long"/>; на Linux перечисление окон не поддержано и списки пусты).
/// </summary>
public sealed record RunningWindowInfo(long Handle, int Pid, string Title);

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
    /// Строка элемента выбора окна: «Заголовок (PID 1234)»; без заголовка — «PID 1234».
    /// </summary>
    public static string FormatWindowTitle(RunningWindowInfo window)
    {
        var title = (window.Title ?? string.Empty).Trim();
        return title.Length > 0
            ? $"{title} (PID {window.Pid})"
            : $"PID {window.Pid}";
    }
}
