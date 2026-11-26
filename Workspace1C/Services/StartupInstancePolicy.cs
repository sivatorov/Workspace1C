namespace Workspace1C.Services;

/// <summary>
/// Issue #164: чистое решение о том, кто «владеет» запуском приложения.
/// <para>
/// Проверка одиночного экземпляра должна выполняться <b>до</b> окна входа:
/// иначе повторный запуск ярлыком (в том числе когда окно свёрнуто в трей)
/// сначала спрашивает пароль учётной записи, и только потом обнаруживает уже
/// работающий экземпляр и завершается. Политика вынесена из WPF/Avalonia-точек
/// входа, чтобы порядок и результат проверялись тестами без запуска UI.
/// </para>
/// <para>
/// <c>createdNew</c> — признак первичного экземпляра: для WPF это результат
/// <c>new Mutex(true, name, out createdNew)</c>, для Avalonia — успешный захват
/// файлового lock. При <c>allowMultipleInstances == true</c> гейт не используется
/// вовсе: каждый запуск полноценный (вход и главное окно работают как обычно).
/// </para>
/// </summary>
public static class StartupInstancePolicy
{
    /// <summary>
    /// Нужно ли вообще брать блокировку одиночного экземпляра.
    /// При разрешённых нескольких экземплярах — нет.
    /// </summary>
    public static bool ShouldUseSingleInstanceGate(bool allowMultipleInstances) =>
        !allowMultipleInstances;

    /// <summary>
    /// Второй экземпляр при запрете нескольких запусков: нужно активировать уже
    /// работающее окно и завершиться, не показывая окно входа.
    /// </summary>
    public static bool ShouldActivateExistingInstance(bool allowMultipleInstances, bool createdNew) =>
        !allowMultipleInstances && !createdNew;

    /// <summary>
    /// Текущий процесс — первичный (или одиночный режим отключён): он продолжает
    /// обычный запуск и только он слушает сигнал активации от повторных запусков.
    /// </summary>
    public static bool IsPrimaryInstance(bool allowMultipleInstances, bool createdNew) =>
        allowMultipleInstances || createdNew;

    /// <summary>
    /// Нужно ли показывать окно входа. Второй экземпляр, активирующий уже
    /// работающее окно, окно входа не показывает.
    /// </summary>
    public static bool ShouldShowLogin(bool activateExistingInstance, bool requiresLogin) =>
        !activateExistingInstance && requiresLogin;
}
