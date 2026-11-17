using System.IO;
using System.Text;

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
    private static bool _sharedGuardActive;
    private static string? _sharedGuardReason;

    /// <summary>
    /// Переопределение сегмента пользователя (тесты, issue #136, 1.0.1.81): позволяет
    /// проверить разделение данных двух учётных записей без входа под ними.
    /// </summary>
    internal static Func<string>? UserSegmentOverride { get; set; }

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
    /// Признак того, что каталог данных разделён по учётным записям внутри общего
    /// каталога (issue #136, 1.0.1.81): портативная установка в машинном каталоге
    /// (<c>C:\Program Files</c>, <c>%ProgramData%</c>) или резервный <c>%TEMP%</c>,
    /// не входящий в профиль пользователя.
    /// </summary>
    public static bool IsSharedDirectoryGuardActive
    {
        get
        {
            _ = ResolveDataDirectoryCached();
            return _sharedGuardActive;
        }
    }

    /// <summary>
    /// Причина включения персонального сегмента; <c>null</c>, если разделение
    /// по пользователям не потребовалось (issue #136, 1.0.1.81).
    /// </summary>
    public static string? SharedDirectoryGuardReason
    {
        get
        {
            _ = ResolveDataDirectoryCached();
            return _sharedGuardReason;
        }
    }

    /// <summary>
    /// Сегмент каталога для текущей учётной записи (issue #136, 1.0.1.81): безопасное
    /// имя пользователя плюс стабильный короткий хэш профиля, чтобы одинаковые имена
    /// в разных доменах/машинах не смешивали данные.
    /// </summary>
    public static string UserDataSegment => UserSegmentOverride?.Invoke() ?? BuildUserDataSegment();

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

            // issue #136 (1.0.1.81): если выбранный каталог общий для нескольких
            // пользователей (портативная установка в Program Files/ProgramData или
            // %TEMP% вне профиля), данные разделяются по учётной записи.
            var guarded = ApplyPerUserGuard(
                resolution, PortablePaths.IsPortable, IsMachineWideDirectory, UserDataSegment);

            _resolvedDataDirectory = guarded.DirectoryPath;
            _fallbackActive = guarded.IsFallback;
            _fallbackReason = guarded.Reason;
            _sharedGuardActive = !string.Equals(
                guarded.DirectoryPath, resolution.DirectoryPath, StringComparison.OrdinalIgnoreCase);
            _sharedGuardReason = _sharedGuardActive ? guarded.Reason : null;
            _resolutionDone = true;
            return _resolvedDataDirectory;
        }
    }

    /// <summary>
    /// Добавляет персональный сегмент <c>users/<сегмент></c> к общему каталогу
    /// данных (issue #136, 1.0.1.81). Для портативного режима сегмент добавляется только
    /// в машинных каталогах (сменный носитель сохраняет прежний путь); для обычной
    /// установки — когда каталог не входит в профиль пользователя. Перед возвратом
    /// данные прежнего общего каталога best-effort переносятся в персональный.
    /// </summary>
    internal static DataDirectoryResolution ApplyPerUserGuard(
        DataDirectoryResolution resolution,
        bool portableMode,
        Func<string, bool> isMachineWide,
        string userSegment)
    {
        if (!RequiresPerUserSegment(resolution.DirectoryPath, portableMode, isMachineWide))
            return resolution;

        var segment = string.IsNullOrWhiteSpace(userSegment) ? "user" : userSegment.Trim();
        var guardedDirectory = Path.Combine(resolution.DirectoryPath, "users", segment);
        EnsureMigratedToPerUserDirectory(resolution.DirectoryPath, guardedDirectory);

        var reason = resolution.Reason is null
            ? $"Каталог данных «{resolution.DirectoryPath}» общий для нескольких пользователей — " +
              $"данные разделены по учётной записи: «{guardedDirectory}»"
            : resolution.Reason + $" Данные разделены по учётной записи: «{guardedDirectory}».";

        return new DataDirectoryResolution(guardedDirectory, resolution.IsFallback, reason);
    }

    /// <summary>
    /// Нужен ли персональный сегмент для каталога (issue #136, 1.0.1.81):
    /// портативный режим — только для машинных каталогов; обычный — для любого
    /// каталога вне профиля пользователя (общий <c>%TEMP%</c> и т.п.).
    /// </summary>
    internal static bool RequiresPerUserSegment(
        string? directory, bool portableMode, Func<string, bool> isMachineWide)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        if (isMachineWide is null)
            throw new ArgumentNullException(nameof(isMachineWide));

        return portableMode
            ? isMachineWide(directory!)
            : !IsUserPrivateDirectory(directory!);
    }

    /// <summary>
    /// True — каталог находится внутри профиля пользователя (<c>%USERPROFILE%</c>,
    /// <c>%APPDATA%</c>, <c>%LOCALAPPDATA%</c>), то есть уже персональный.
    /// </summary>
    internal static bool IsUserPrivateDirectory(string? directory)
    {
        var full = NormalizeDirectoryPath(directory);
        if (full.Length == 0)
            return false;

        foreach (var root in GetUserPrivateRoots())
        {
            if (root.Length == 0)
                continue;
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True — каталог относится к машинным (<c>Program Files</c>, <c>ProgramData</c>,
    /// <c>%SystemRoot%</c>): в нём могут писать разные пользователи.
    /// </summary>
    internal static bool IsMachineWideDirectory(string? directory)
    {
        var full = NormalizeDirectoryPath(directory);
        if (full.Length == 0)
            return false;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        foreach (var root in roots)
        {
            var normalized = NormalizeDirectoryPath(root);
            if (normalized.Length == 0)
                continue;
            if (full.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetUserPrivateRoots()
    {
        yield return NormalizeDirectoryPath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        yield return NormalizeDirectoryPath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        yield return NormalizeDirectoryPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    /// <summary>Полный путь без хвостовых разделителей; пустая строка при пустом входе.</summary>
    internal static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return Path.GetFullPath(path!)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path!.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>Сегмент текущего пользователя: безопасное имя + стабильный хэш профиля.</summary>
    private static string BuildUserDataSegment()
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
            user = Environment.GetEnvironmentVariable("USERNAME") ?? "user";

        var seed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(seed))
            seed = user;

        return SanitizeUserSegment(user, seed);
    }

    /// <summary>
    /// Безопасный сегмент каталога: недопустимые символы заменяются «_», длина
    /// ограничена, в конец добавляется стабильный короткий хэш уникального профиля.
    /// </summary>
    internal static string SanitizeUserSegment(string? user, string? uniqueSeed)
    {
        var builder = new StringBuilder();
        foreach (var ch in (user ?? string.Empty).Trim())
            builder.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');

        var safe = builder.ToString().Trim('_');
        if (safe.Length == 0)
            safe = "user";
        if (safe.Length > 32)
            safe = safe[..32];

        var hash = 2166136261u;
        foreach (var ch in uniqueSeed ?? string.Empty)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return safe + "-" + hash.ToString("x8");
    }

    /// <summary>
    /// Best-effort перенос данных из общего каталога в персональный (issue #136,
    /// 1.0.1.81): файлы копируются без перезаписи, старый каталог не удаляется.
    /// </summary>
    private static void EnsureMigratedToPerUserDirectory(string legacyDirectory, string targetDirectory)
    {
        try
        {
            Directory.CreateDirectory(targetDirectory);
            CopyDirectory(legacyDirectory, targetDirectory);
        }
        catch
        {
            // Перенос — вспомогательная операция; при сбое работаем с пустым каталогом.
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
            _sharedGuardActive = false;
            _sharedGuardReason = null;
            UserSegmentOverride = null;
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
        IEnumerable<string> fallbackDirectories,
        bool applyPerUserGuard = false,
        bool portableMode = false,
        Func<string, bool>? isMachineWide = null,
        string? userSegment = null)
    {
        var resolution = ResolveDataDirectory(preferredDirectory, fallbackDirectories);
        var guarded = applyPerUserGuard
            ? ApplyPerUserGuard(
                resolution,
                portableMode,
                isMachineWide ?? IsMachineWideDirectory,
                userSegment ?? UserDataSegment)
            : resolution;

        lock (ResolutionLock)
        {
            _resolvedDataDirectory = guarded.DirectoryPath;
            _fallbackActive = guarded.IsFallback;
            _fallbackReason = guarded.Reason;
            _sharedGuardActive = !string.Equals(
                guarded.DirectoryPath, resolution.DirectoryPath, StringComparison.OrdinalIgnoreCase);
            _sharedGuardReason = _sharedGuardActive ? guarded.Reason : null;
            _resolutionDone = true;
        }
        return guarded;
    }

    /// <summary>
    /// Каталог логов приложения (файловый логгер FileAppLogger).
    /// </summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}