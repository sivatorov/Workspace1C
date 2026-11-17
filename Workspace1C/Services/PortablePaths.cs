using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Портативный режим приложения (функция №1 StartManager, Этап 10 дорожной карты).
/// Детектирует запуск со сменного носителя и переносит каталог данных настроек
/// (<c>settings.json</c>, <c>infobases.json</c>, <c>groups.json</c>) рядом с исполняемым файлом.
///
/// Включение портативного режима:
/// <list type="bullet">
/// <item>наличие файла <c>portable.dat</c> в каталоге исполняемого файла, либо</item>
/// <item>переменная окружения <c>CONFIG_MANAGEMENT_PORTABLE</c> со значением <c>1</c>/<c>true</c>.</item>
/// </list>
///
/// В портативном режиме <see cref="PlatformPaths.AppDataDirectory"/> возвращает каталог
/// <c><каталог exe>/Workspace1C-Data</c> вместо системного каталога
/// профиля, поэтому все настройки хранятся на самом носителе и переносятся вместе с ним.
/// При первом запуске существующие данные из системного каталога копируются в портативный
/// (см. <see cref="EnsurePortableData"/>), чтобы настройки не потерялись.
/// </summary>
public static class PortablePaths
{
    /// <summary>Имя файла-маркера портативного режима.</summary>
    public const string MarkerFileName = "portable.dat";

    /// <summary>Имя переменной окружения, включающей портативный режим.</summary>
    public const string PortableEnvVar = "CONFIG_MANAGEMENT_PORTABLE";

    /// <summary>Имя подкаталога данных в портативном режиме (рядом с exe).</summary>
    public const string PortableDataSubdir = "Workspace1C-Data";

    private static readonly bool _isPortable = Detect();

    /// <summary>Признак работы в портативном режиме (запуск со сменного носителя).</summary>
    public static bool IsPortable => _isPortable;

    /// <summary>Каталог исполняемого файла приложения.</summary>
    public static string ApplicationDirectory
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            return string.IsNullOrWhiteSpace(baseDir) ? Directory.GetCurrentDirectory() : baseDir;
        }
    }

    /// <summary>Каталог данных в портативном режиме (<каталог exe>/Workspace1C-Data).</summary>
    /// <remarks>
    /// Совместимость с прежним названием программы: если рядом с exe есть только каталог
    /// <c>ConfigurationManagement-Data</c> (портативная установка «Управления конфигурациями 1С»),
    /// используется он — настройки на носителе продолжают работать без переноса.
    /// </remarks>
    public static string PortableAppDataDirectory
    {
        get
        {
            var current = Path.Combine(ApplicationDirectory, PortableDataSubdir);
            if (Directory.Exists(current))
                return current;
            var legacy = Path.Combine(ApplicationDirectory, LegacyPortableDataSubdir);
            return Directory.Exists(legacy) ? legacy : current;
        }
    }

    /// <summary>Имя портативного подкаталога данных прежней версии программы.</summary>
    public const string LegacyPortableDataSubdir = "ConfigurationManagement-Data";

    /// <summary>
    /// Возвращает каталог данных приложения в портативном режиме, либо null,
    /// если портативный режим не активен (используется системный каталог профиля).
    /// </summary>
    public static string? TryResolveDataDirectory() => IsPortable ? PortableAppDataDirectory : null;

    /// <summary>
    /// При первом запуске в портативном режиме переносит данные из системного каталога
    /// профиля в портативный каталог, если портативный каталог ещё пуст. Ошибки глушатся —
    /// перенос не должен блокировать запуск.
    /// <para>
    /// issue #136 (1.0.1.81): перенос идёт в фактический каталог данных
    /// (<see cref="PlatformPaths.AppDataDirectory"/>), а не в сырой каталог рядом с exe:
    /// если портативная установка лежит в машинном каталоге (Program Files/ProgramData),
    /// фактические данные находятся в персональном подкаталоге <c>users/<сегмент></c>,
    /// и разные пользователи не смешивают настройки.
    /// </para>
    /// </summary>
    public static void EnsurePortableData()
    {
        if (!IsPortable)
            return;

        try
        {
            var target = PlatformPaths.AppDataDirectory;
            var source = PlatformPaths.DefaultAppDataDirectory;
            if (string.Equals(
                    PlatformPaths.NormalizeDirectoryPath(target),
                    PlatformPaths.NormalizeDirectoryPath(source),
                    StringComparison.OrdinalIgnoreCase))
            {
                return; // источник и цель совпадают — переносить нечего
            }

            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                return; // портативные данные уже есть — не затираем их системными

            if (!Directory.Exists(source))
                return;

            Directory.CreateDirectory(target);
            foreach (var fileName in new[] { "settings.json", "infobases.json", "groups.json", "profiles.json" })
            {
                var src = Path.Combine(source, fileName);
                if (!File.Exists(src))
                    continue;
                var dst = Path.Combine(target, fileName);
                if (!File.Exists(dst))
                    File.Copy(src, dst);
            }
        }
        catch
        {
            // Перенос — вспомогательная операция; при неудаче продолжаем без него.
        }
    }

    /// <summary>
    /// Детектирует портативный режим: файл <c>portable.dat</c> рядом с exe либо
    /// переменная окружения <see cref="PortableEnvVar"/>.
    /// </summary>
    private static bool Detect()
    {
        var env = Environment.GetEnvironmentVariable(PortableEnvVar);
        if (!string.IsNullOrWhiteSpace(env) &&
            (string.Equals(env.Trim(), "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(env.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        try
        {
            return File.Exists(Path.Combine(ApplicationDirectory, MarkerFileName));
        }
        catch
        {
            return false;
        }
    }
}