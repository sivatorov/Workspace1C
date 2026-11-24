using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Правила «Текущей сессии» — разового переопределения клиента и разрядности
/// для очередного запуска «Предприятия» (issue #156).
/// <para>
/// Семантика (issue #156, S1–S6):
/// S1 — по умолчанию переопределение разовое: после успешного запуска «Предприятия»
/// оба переключателя возвращаются в «Авто»;
/// S2 — включённая скрепка (pin) сохраняет значения до отжатия, сброса нет;
/// S3/S4 — ни скрепка, ни значения не переживают перезапуск (только runtime-состояние);
/// S5 — при «Авто»/«Авто» запуск штатный, <c>Infobase.LaunchMode</c> не меняется никогда;
/// S6 — сброс только для успешного запуска «Предприятия» по умолчанию
/// (не «Конфигуратор» и не явные Thin32/Thick32/Thin64/Thick64).
/// </para>
/// <para>
/// Класс намеренно не зависит ни от UI, ни от <see cref="OneCLauncher"/>:
/// обе платформы (WPF и Avalonia) используют одну и ту же логику, а тесты
/// проверяют её без запуска 1С.
/// </para>
/// </summary>
public static class SessionLaunchPolicy
{
    /// <summary>
    /// Есть ли что переопределять: хотя бы один переключатель не «Авто».
    /// При <c>false</c> запуск идёт штатно по настройкам базы (S5).
    /// </summary>
    public static bool HasOverrides(SessionClientMode client, SessionArchitectureMode arch) =>
        client != SessionClientMode.Auto || arch != SessionArchitectureMode.Auto;

    /// <summary>
    /// Нужно ли сбросить значения «Текущей сессии» после запуска:
    /// запуск успешен, режим — «Предприятие», скрепка отжата и есть что сбрасывать.
    /// Неудачный запуск переопределение не расходует (S1/S6).
    /// </summary>
    /// <param name="pinned">Включена ли скрепка (runtime-состояние, S2).</param>
    /// <param name="client">Текущий режим клиента «Текущей сессии».</param>
    /// <param name="arch">Текущая разрядность «Текущей сессии».</param>
    /// <param name="mode">Режим запуска платформы.</param>
    /// <param name="launchSucceeded">Успешен ли фактический запуск.</param>
    public static bool ShouldResetAfterLaunch(
        bool pinned,
        SessionClientMode client,
        SessionArchitectureMode arch,
        OneCLaunchMode mode,
        bool launchSucceeded) =>
        !pinned &&
        launchSucceeded &&
        mode == OneCLaunchMode.Enterprise &&
        HasOverrides(client, arch);

    /// <summary>
    /// Нужно ли сохранять значения «Текущей сессии» в настройки.
    /// Всегда <c>false</c>: скрепка не переживает перезапуск (S3), поэтому и
    /// сохранённые значения были бы противоречием разовости (S4). Параметр
    /// оставлен, чтобы явно фиксировать отсутствие исключений для pin.
    /// </summary>
    public static bool ShouldPersistValues(bool pinned) => false;
}
