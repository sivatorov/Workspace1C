using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Кроссплатформенные пути к каталогам данных приложения.
/// Windows: %APPDATA%\Workspace1C (Environment.SpecialFolder.ApplicationData).
/// Linux:   ~/.config/Workspace1C (XDG_CONFIG_HOME или ~/.config).
/// Единая точка расчёта каталога данных и каталога логов, чтобы все сервисы
/// (InfobaseRepository, FileAppLogger и др.) писали в одно место.
/// <para>
/// issue #136: если предпочтительный каталог недоступен для записи (например,
/// программа установлена в <c>C:\Program Files</c>, а портативный каталог рядом
/// с exe защищён от записи), однократно выбирается резервный каталог — см.
/// <see cref="AppDataDirectory"/>, <see cref="IsFallbackActive"/> и
/// <see cref="FallbackReason"/>. Это избавляет от <c>UnauthorizedAccessException</c>
/// при каждом сохранении настроек интерфейса.
/// </para>
/// </summary>
public static class PlatformPaths
{
    private static readonly object ResolutionLock = new();
    private static bool _resolutionDone;
    private static string? _resolvedDataDirectory;
    private static bool _fallbackActive;
    private static string? _fallbackReason;

    /// <summary>
    /// Каталог данных приложения (infobases.json, groups.json, settings.json).
    /// В портативном режиме (<see cref="PortablePaths.IsPortable"/>) возвращается каталог
    /// рядом с исполняемым файлом (<see cref="PortablePaths.PortableAppDataDirectory"/>),
    /// что позволяет переносить все настройки вместе со сменным носителем (функция №1
    /// StartManager, Этап 10). Иначе — системный каталог профиля.
    /// <para>
    /// issue #136: каталог определяется однократно и кэшируется. Если запись в него
    /// невозможна, возвращается резервный каталог (Windows: <c>%LOCALAPPDATA%\Workspace1C</c>,
    /// Linux: <c>$XDG_DATA_HOME</c>/<c>~/.local/share/Workspace1C</c>, последний резерв —
    /// <c><temp>/Workspace1C</c>). Признак и причина перехода доступны через
    /// <see cref="IsFallbackActive"/> и <see cref="FallbackReason"/>.
    /// </para>
    /// </summary>
    public static string AppDataDirectory => ResolveDataDirectoryCached();

    /// <summary>
    /// Каталог данных, который приложение предпочитает использовать: портативный
    /// (рядом с exe) либо системный каталог профиля. Может оказаться недоступным
    /// для записи — фактический каталог см. <see cref="AppDataDirectory"/> (issue #136).
    /// </summary>
    public static string PreferredDataDirectory =>
        PortablePaths.TryResolveDataDirectory() ?? DefaultAppDataDirectory;

    /// <summary>
    /// Признак того, что предпочтительный каталог данных недоступен для записи
    /// и приложение использует резервный (issue #136).
    /// </summary>
    public static bool IsFallbackActive
    {
        get
        {
            _ = ResolveDataDirectoryCached();
            return _fallbackActive;
        }
    }

    /// <summary>
    /// Причина перехода на резервный каталог данных; <c>null</c>, если fallback
    /// не активен (issue #136).
    /// </summary>
    public static string? FallbackReason
    {
        get
        {
            _ = ResolveDataDirectoryCached();
            return _fallbackReason;
        }
    }

    /// <summary>
    /// Системный каталог данных приложения по умолчанию (без учёта портативного режима).
    /// Windows: %APPDATA%\Workspace1C; Linux: ~/.config/Workspace1C.
    /// При первом обращении переносит данные из каталога прежней версии (ConfigurationManagement).
    /// </summary>
    public static string DefaultAppDataDirectory
    {
        get
        {
            var dir = Path.Combine(GetConfigBaseDirectory(), AppFolderName);
            EnsureMigratedFromLegacy(dir);
            return dir;
        }
    }

    /// <summary>Имя каталога данных приложения Workspace 1C.</summary>
    public const string AppFolderName = "Workspace1C";

    /// <summary>
    /// Имя каталога данных прежнего названия программы («Управление конфигурациями 1С»).
    /// Используется только для однократного переноса настроек при первом запуске Workspace 1C.
    /// </summary>
    public const string LegacyAppFolderName = "ConfigurationManagement";

    private const string MigrationMarkerFile = ".migrated-from-ConfigurationManagement";
    private static readonly object MigrationLock = new();
    private static bool _migrationChecked;

    /// <summary>
    /// Однократный перенос данных из каталога прежней версии (ConfigurationManagement)
    /// в каталог Workspace1C. Файлы копируются без перезаписи, старый каталог не удаляется
    /// (к нему можно вернуться). Повторно перенос не выполняется (файл-маркер).
    /// Любые ошибки игнорируются: программа продолжит работу с новым каталогом.
    /// </summary>
    public static void EnsureMigratedFromLegacy(string? targetDirectory = null)
    {
        if (_migrationChecked)
            return;
        lock (MigrationLock)
        {
            if (_migrationChecked)
                return;
            _migrationChecked = true;
            try
            {
                var baseDir = GetConfigBaseDirectory();
                var target = targetDirectory ?? Path.Combine(baseDir, AppFolderName);
                var legacy = Path.Combine(baseDir, LegacyAppFolderName);
                if (!Directory.Exists(legacy))
                    return;
                if (File.Exists(Path.Combine(target, MigrationMarkerFile)))
                    return;
                // Если в новом каталоге уже есть собственные данные — не вмешиваемся.
                if (File.Exists(Path.Combine(target, "settings.json")) ||
                    File.Exists(Path.Combine(target, "infobases.json")))
                    return;

                CopyDirectory(legacy, target);
                File.WriteAllText(Path.Combine(target, MigrationMarkerFile),
                    "Данные перенесены из " + legacy + " " + DateTime.Now.ToString("s"));
            }
            catch
            {
                // Перенос — best effort: при сбое программа стартует с пустым каталогом.
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                try { File.Copy(file, dest, overwrite: false); } catch { /* занят/нет доступа */ }
            }
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>Базовый каталог конфигурации пользователя (%APPDATA% / ~/.config).</summary>
    private static string GetConfigBaseDirectory()
    {
        {
#if LINUX
            // На Linux SpecialFolder.ApplicationData уже указывает на ~/.config,
            // но задаём путь явно и корректно обрабатываем XDG_CONFIG_HOME.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(configHome)
                ? configHome
                : string.IsNullOrEmpty(home)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.Combine(home, ".config");
            return baseDir;
#else
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#endif
        }
    }

    /// <summary>
    /// Результат выбора каталога данных (issue #136).
    /// </summary>
    internal readonly record struct DataDirectoryResolution(
        string DirectoryPath, bool IsFallback, string? Reason);

    /// <summary>
    /// Однократное кэшированное определение фактического каталога данных (issue #136).
    /// </summary>
    private static string ResolveDataDirectoryCached()
    {
        if (_resolutionDone)
            return _resolvedDataDirectory!;

        lock (ResolutionLock)
        {
            if (_resolutionDone)
                return _resolvedDataDirectory!;

            var resolution = ResolveDataDirectory(PreferredDataDirectory, BuildFallbackDirectories());
            _resolvedDataDirectory = resolution.DirectoryPath;
            _fallbackActive = resolution.IsFallback;
            _fallbackReason = resolution.Reason;
            _resolutionDone = true;
            return _resolvedDataDirectory;
        }
    }

    /// <summary>
    /// Выбирает первый каталог, в который реально можно писать: сначала
    /// <paramref name="preferredDirectory"/>, затем <paramref name="fallbackDirectories"/>
    /// по порядку (issue #136). Если ни один не доступен, возвращает предпочтительный
    /// каталог с признаком fallback: дальнейшие ошибки записи обрабатываются вызывающим
    /// кодом, чтобы интерфейс не показывал диалог на каждое действие пользователя.
    /// </summary>
    internal static DataDirectoryResolution ResolveDataDirectory(
        string preferredDirectory,
        IEnumerable<string> fallbackDirectories)
    {
        if (IsWritableDirectory(preferredDirectory, out var preferredError))
            return new DataDirectoryResolution(preferredDirectory, false, null);

        var reason = $"Каталог данных «{preferredDirectory}» недоступен для записи: {preferredError}";
        var tried = new List<string> { preferredDirectory };

        foreach (var candidate in fallbackDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            // Один и тот же каталог не проверяем дважды (регистр в путях Windows не значим).
            if (tried.Any(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase)))
                continue;
            tried.Add(candidate);
            if (IsWritableDirectory(candidate, out _))
                return new DataDirectoryResolution(candidate, true, reason);
        }

        return new DataDirectoryResolution(preferredDirectory, true, reason);
    }

    /// <summary>
    /// Резервные каталоги данных по убыванию приоритета (issue #136):
    /// Windows — <c>%LOCALAPPDATA%\Workspace1C</c>;
    /// Linux — <c>$XDG_DATA_HOME</c> или <c>~/.local/share/Workspace1C</c>;
    /// последний резерв — <c><temp>/Workspace1C</c>.
    /// </summary>
    internal static IReadOnlyList<string> BuildFallbackDirectories()
    {
        var result = new List<string>();

#if LINUX
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var localData = !string.IsNullOrWhiteSpace(dataHome)
            ? dataHome
            : string.IsNullOrEmpty(home)
                ? null
                : Path.Combine(home, ".local", "share");
#else
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
        if (!string.IsNullOrWhiteSpace(localData))
            result.Add(Path.Combine(localData!, AppFolderName));

        try
        {
            result.Add(Path.Combine(Path.GetTempPath(), AppFolderName));
        }
        catch
        {
            // Temp недоступен — остаются только системные кандидаты.
        }

        return result;
    }

    /// <summary>
    /// Проверяет, что в каталог можно писать: создаёт его, записывает и удаляет
    /// пробный файл. Любая ошибка (нет прав, путь занят файлом, диск только для чтения)
    /// означает «недоступен» (issue #136).
    /// </summary>
    internal static bool IsWritableDirectory(string? directory, out string? error)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "каталог не задан";
            return false;
        }

        string? probePath = null;
        try
        {
            Directory.CreateDirectory(directory);
            probePath = Path.Combine(directory, ".w1c-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (probePath is not null)
            {
                try
                {
                    if (File.Exists(probePath))
                        File.Delete(probePath);
                }
                catch
                {
                    // Пробный файл — вспомогательный: не мешаем выбору каталога.
                }
            }
        }
    }

    /// <summary>
    /// Сбрасывает кэш выбора каталога данных (только для тестов, issue #136).
    /// </summary>
    internal static void ResetDataDirectoryCacheForTests()
    {
        lock (ResolutionLock)
        {
            _resolutionDone = false;
            _resolvedDataDirectory = null;
            _fallbackActive = false;
            _fallbackReason = null;
        }
    }

    /// <summary>
    /// Заполняет кэш результатом явного выбора каталога (только для тестов, issue #136):
    /// позволяет проверить <see cref="AppDataDirectory"/>, <see cref="LogDirectory"/>,
    /// <see cref="IsFallbackActive"/> и <see cref="FallbackReason"/> без записи
    /// в реальный <c>%APPDATA%</c>.
    /// </summary>
    internal static DataDirectoryResolution ResolveDataDirectoryForTests(
        string preferredDirectory,
        IEnumerable<string> fallbackDirectories)
    {
        var resolution = ResolveDataDirectory(preferredDirectory, fallbackDirectories);
        lock (ResolutionLock)
        {
            _resolvedDataDirectory = resolution.DirectoryPath;
            _fallbackActive = resolution.IsFallback;
            _fallbackReason = resolution.Reason;
            _resolutionDone = true;
        }
        return resolution;
    }

    /// <summary>
    /// Каталог логов приложения (файловый логгер FileAppLogger).
    /// </summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}