namespace Workspace1C.Services;

/// <summary>
/// Поиск исполняемого файла родного стартера 1С (issue #91): на Windows —
/// <c>1CEStart.exe</c> (<see cref="InfobaseMaintenanceService.FindOneCStartExe"/>),
/// на Linux — <c>1cestart</c> (<see cref="InfobaseMaintenanceService.FindOneCStart"/>).
/// Используется автопереносом «1С:Стартер» в группу «Мои программы»; ошибки поиска
/// не выходят наружу — возвращается <c>null</c>.
/// </summary>
public static class NativeStarterLocator
{
    /// <summary>Полный путь к стартеру 1С либо <c>null</c>, если он не найден.</summary>
    public static string? FindStarterPath()
    {
#if WINDOWS
        try
        {
            return InfobaseMaintenanceService.FindOneCStartExe();
        }
        catch
        {
            return null;
        }
#elif LINUX
        try
        {
            return InfobaseMaintenanceService.FindOneCStart();
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }
}
