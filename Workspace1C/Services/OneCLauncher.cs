using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Тип клиента 1С:Предприятие.
/// </summary>
public enum OneCClientType
{
    /// <summary>Тонкий клиент (управляемое приложение).</summary>
    Thin,

    /// <summary>Толстый клиент (обычное приложение).</summary>
    Thick
}

/// <summary>
/// Режим форм приложения 1С:Предприятие (независим от типа клиента,
/// как в стандартном списке баз 1С — «Толстый клиент (управляемое приложение)»
/// и «Толстый клиент (обычное приложение)»).
/// </summary>
public enum OneCRunMode
{
    /// <summary>Управляемые формы (/RunModeManagedApplication).</summary>
    Managed,

    /// <summary>Обычные формы (/RunModeOrdinaryApplication).</summary>
    Ordinary
}

/// <summary>
/// Разрядность исполняемого файла платформы 1С.
/// </summary>
public enum OneCArchitecture
{
    /// <summary>32-битная версия.</summary>
    x86,

    /// <summary>64-битная версия.</summary>
    x64
}

/// <summary>
/// Сервис запуска платформы 1С:Предприятие.
/// </summary>
public static partial class OneCLauncher
{
    /// <summary>Логгер из DI (без создания жёсткой зависимости).</summary>
    private static IAppLogger? GetLogger()
    {
        try { return AppServices.GetRequiredService<IAppLogger>(); }
        catch { return null; }
    }

    /// <summary>
    /// Режим глобальной «Разрядности по умолчанию» («Настройки → Платформы»):
    /// "X64" — всегда 64-бит, "X86" — всегда 32-бит, либо "Priority"
    /// («Использовать приоритет базы») — брать явную настройку разрядности
    /// информационной базы (вкладка «Разрядность»). По умолчанию — X64.
    /// </summary>
    public static string DefaultArchitectureMode { get; set; } = "X64";

    /// <summary>
    /// Разрядность для «текущей сессии» (блок «Текущая сессия» в окне списка баз).
    /// Наивысший (первый) шаг приоритета выбора разрядности в <see cref="ResolveArchitecture"/>:
    /// если здесь задан конкретный режим (не Auto), он побеждает и суффикс версии,
    /// и глобальную настройку, и настройку базы. Auto — выбираем по шагам 2–4.
    /// Значение передаётся из <c>MainViewModel</c> (изменяется вместе с выбором в UI).
    /// </summary>
    public static SessionArchitectureMode SessionArchitecture { get; set; } = SessionArchitectureMode.Auto;

    /// <summary>
    /// Активные пакетные операции DESIGNER (выгрузка .dt/.cf, тест), запущенные приложением.
    /// Ключ — токен подключения базы. Используется для блокировки параллельных выгрузок
    /// и обнаружения уже запущенного конфигуратора этой же базы.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Process> _activeBatchProcesses =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Возникает при запуске пакетной операции DESIGNER (выгрузка .dt/.cf или тест).
    /// Используется главным окном для показа анимированного индикатора выгрузки.
    /// </summary>
    public static event EventHandler<DesignerBatchInfo>? DesignerBatchStarted;

    /// <summary>
    /// Возникает при завершении пакетной операции DESIGNER (в т.ч. по ошибке).
    /// Используется главным окном для скрытия индикатора выгрузки.
    /// </summary>
    public static event EventHandler<DesignerBatchInfo>? DesignerBatchCompleted;

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы в заданном режиме.
    /// Тип клиента определяется из режима запуска базы (LaunchMode):
    /// «Автоматический», «Тонкий клиент», «Толстый клиент» или «Веб-клиент».
    /// Разрядность берётся из настройки базы (Architecture), версия — из PlatformVersion.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="monopol">Монопольный запуск Предприятия (/M, issue #85, v1.0.1.43).
    /// Для «Конфигуратора» и веб-клиента игнорируется.</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false, bool monopol = false)
    {
        // В режиме «Конфигуратор» тип клиента не применяется.
        if (mode == OneCLaunchMode.Configurator)
            return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase), runAsAdmin);

        // Веб-клиент запускается через браузер (монопольный режим неприменим).
        if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
            return LaunchWebClient(infobase);

        // Автоматический режим — платформа сама выбирает клиент (без /RunMode).
        if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, null, GetArchitecture(infobase), runAsAdmin, monopol);

        // Толстый клиент в обычных формах.
        if (string.Equals(infobase.LaunchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Ordinary, GetArchitecture(infobase), runAsAdmin, monopol);

        // Толстый клиент (управляемые формы) — по умолчанию «Толстый клиент».
        if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin, monopol);

        // По умолчанию — тонкий клиент (управляемые формы).
        return Launch(infobase, mode, OneCClientType.Thin, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin, monopol);
    }

    /// <summary>
    /// Определяет режим форм по режиму запуска базы (LaunchMode).
    /// Только толстый клиент может работать и в управляемых, и в обычных формах:
    /// «Толстый клиент» → управляемые, «Толстый клиент (обычные формы)» → обычные,
    /// «Тонкий клиент» → управляемые; иначе — null (Авто).
    /// </summary>
    public static OneCRunMode? GetRunModeFromLaunchMode(string? launchMode)
    {
        if (string.Equals(launchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Ordinary;
        if (string.Equals(launchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Managed;
        if (string.Equals(launchMode, "Тонкий клиент", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Managed;
        return null;
    }

    /// <summary>
    /// Определяет фактическую разрядность клиента по настройке базы и установленным версиям.
    /// Режимы (как в 1С): 32, 64, 32-priority (по умолчанию), 64-priority.
    /// </summary>
    private static OneCArchitecture GetArchitecture(Infobase infobase)
        => ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);

    /// <summary>
    /// Выбор разрядности по правилам 1С:Предприятие.
    /// Порядок приоритетов (issue #146, комментарий 7OH; issue #73):
    /// 1. «Текущая сессия» — выбранное значение в группе «Текущая сессия»
    ///    (свойство <see cref="SessionArchitecture"/>, передаётся из MainViewModel).
    /// 2. Суффикс разрядности в выбранной версии платформы («8.3.27.1688 (64)»).
    /// 3. Явная настройка разрядности базы (вкладка «Разрядность»): «32»/«x86» → x86,
    ///    «64»/«x64» → x64. Явная разрядность базы перебивает глобальную настройку
    ///    «Разрядность по умолчанию» (issue #73).
    /// 4. Глобальная настройка «Разрядность по умолчанию»: X64 / X86 либо
    ///    режим «Использовать приоритет базы» (Priority). Применяется, когда
    ///    у базы разрядность не указана.
    /// 5. Приоритетные режимы базы 32-priority / 64-priority по стилю 1С.
    /// </summary>
    public static OneCArchitecture ResolveArchitecture(string? architectureSetting, string? platformVersion)
    {
        // 1. «Текущая сессия» — первый (наивысший) шаг приоритета (issue #146).
        //    Явный выбор разрядности в блоке «Текущая сессия» побеждает суффикс
        //    версии, глобальную настройку и настройку базы.
        if (SessionArchitecture == SessionArchitectureMode.X86)
            return OneCArchitecture.x86;
        if (SessionArchitecture == SessionArchitectureMode.X64)
            return OneCArchitecture.x64;

        // 2. Если в версии платформы явно указан суффикс разрядности («8.3.27.1688 (64)») —
        //    пользователь выбрал конкретную сборку. Это следующий по приоритету шаг
        //    после «текущей сессии» и перебивает глобальную настройку.
        PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out var versionArch);
        var hasSuffix = !string.IsNullOrWhiteSpace(platformVersion)
            && platformVersion.Contains("(") && platformVersion.Contains(")");
        if (hasSuffix && !string.IsNullOrWhiteSpace(cleanVersion) && (versionArch == "32" || versionArch == "64"))
            return versionArch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86;

        // 3. Явная настройка разрядности базы (вкладка «Разрядность»).
        //    Явно указанная разрядность базы перебивает глобальную настройку
        //    «Разрядность по умолчанию» (issue #73).
        var mode = (architectureSetting ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "64" or "x64" or "x86-64" or "x86_64")
            return OneCArchitecture.x64;
        if (mode is "32" or "x86")
            return OneCArchitecture.x86;

        // 4. Глобальная настройка «Разрядность по умолчанию»
        //    (Настройки → Платформы → «Разрядность по умолчанию»).
        //    Применяется, когда у базы разрядность не указана.
        var defaultMode = string.IsNullOrWhiteSpace(DefaultArchitectureMode) ? "X64" : DefaultArchitectureMode.Trim();
        if (string.Equals(defaultMode, "X86", StringComparison.OrdinalIgnoreCase))
            return OneCArchitecture.x86;
        if (string.Equals(defaultMode, "X64", StringComparison.OrdinalIgnoreCase))
            return OneCArchitecture.x64;
        // defaultMode == "Priority" («Использовать приоритет базы») → шаг 5.

        // 5. Приоритетные режимы базы: сравниваем лучшие доступные версии 32 и 64.
        var prefer64 = mode is "64-priority" or "priority64" or "x86-64-priority";
        if (string.IsNullOrWhiteSpace(cleanVersion))
            cleanVersion = string.Empty;

        var v32 = FindBestVersionDir("32", cleanVersion);
        var v64 = FindBestVersionDir("64", cleanVersion);

        if (v32 is null && v64 is null)
            return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
        if (v32 is null)
            return OneCArchitecture.x64;
        if (v64 is null)
            return OneCArchitecture.x86;

        var cmp = CompareVersionDirs(v32, v64);
        // Более старшая версия побеждает; при равенстве — предпочитаемая разрядность.
        if (cmp > 0)
            return OneCArchitecture.x86; // 32 новее
        if (cmp < 0)
            return OneCArchitecture.x64; // 64 новее
        return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
    }

    /// <summary>
    /// Лучший каталог версии для указанной разрядности (или null).
    /// Использует гибкий поиск, покрывающий и нестандартные корни из дополнительных папок.
    /// Полная версия ограничивает поиск только ею, частичная (префикс) — новейшей
    /// установленной версией с таким префиксом (issue #142).
    /// </summary>
    private static string? FindBestVersionDir(string archKey, string preferredVersion)
    {
        var entries = PlatformVersionService.FindPlatformVersionDirs(archKey);
        string? best = null;
        string bestVersion = string.Empty;

        foreach (var (version, _) in entries)
        {
            // Полная версия — только точные совпадения; частичная — по префиксу (issue #142).
            if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                !VersionMatches(preferredVersion, version))
                continue;

            if (best is null || CompareVersionDirs(version, bestVersion) > 0)
            {
                best = version;
                bestVersion = version;
            }
        }

        return best;
    }

    /// <summary>Сравнение номеров версий 1С (8.3.24.1000). >0 если a новее b.</summary>
    private static int CompareVersionDirs(string a, string b)
    {
        static int[] Parts(string v)
        {
            var s = v.Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>();
            foreach (var p in s)
            {
                if (int.TryParse(p, out var n))
                    list.Add(n);
                else
                    break;
            }
            return list.ToArray();
        }

        var pa = Parts(a);
        var pb = Parts(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
    }

    /// <summary>
    /// Проверяет, соответствует ли фактическая версия запрошенной.
    /// Полная версия (4 сегмента) — точное совпадение; частичная («8.5», «8.3.27») —
    /// по числовому префиксу (issue #142).
    /// </summary>
    private static bool VersionMatches(string requested, string actual)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return true;

        var reqParts = requested.Split('.');
        // Полная версия — только точное совпадение (как раньше).
        if (reqParts.Length >= 4)
            return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);

        // Частичная версия — префиксное сопоставление сегментов.
        var actParts = actual.Split('.');
        if (actParts.Length < reqParts.Length)
            return false;
        for (var i = 0; i < reqParts.Length; i++)
        {
            if (!string.Equals(actParts[i].Trim(), reqParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы с заданным
    /// типом клиента и разрядностью. Режим форм выводится из типа клиента
    /// (тонкий → управляемые, толстый → обычные).
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="clientType">Тип клиента (тонкий или толстый). null — автоматический выбор платформой.</param>
    /// <param name="architecture">Разрядность (32 или 64 бита).</param>
    /// <param name="monopol">Монопольный запуск Предприятия (/M, issue #85, v1.0.1.43).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType,
        OneCArchitecture architecture, bool runAsAdmin = false, bool monopol = false)
        => Launch(infobase, mode, clientType, null, architecture, runAsAdmin, monopol);

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы с заданным
    /// типом клиента, режимом форм и разрядностью. Если <paramref name="runMode"/>
    /// задан, он имеет приоритет над режимом, выводимым из типа клиента,
    /// что позволяет запускать управляемые формы толстым клиентом и наоборот.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="clientType">Тип клиента (тонкий или толстый). null — автоматический выбор платформой.</param>
    /// <param name="runMode">Режим форм (управляемые/обычные). null — из типа клиента.</param>
    /// <param name="architecture">Разрядность (32 или 64 бита).</param>
    /// <param name="monopol">Монопольный запуск Предприятия (/M, issue #85, v1.0.1.43).
    /// Применяется только в режиме «Предприятие» (см. <see cref="BuildArguments"/>).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType,
        OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false, bool monopol = false)
    {
        // База, расположенная на веб-сервере, подключается только тонким клиентом (/WS).
        // Толстый клиент 1cv8.exe не понимает /WS и при запуске открывает стандартное
        // окно со списком информационных баз вместо подключения к базе.
        if (infobase.Connection.Type == ConnectionType.WebServer)
        {
            clientType = OneCClientType.Thin;
            runMode ??= OneCRunMode.Managed;
        }

        var exePath = FindExecutable(infobase.PlatformVersion, architecture, clientType, mode);
        if (string.IsNullOrEmpty(exePath))
        {
            var archLabel = architecture == OneCArchitecture.x64
                ? LocalizationManager.T("Launcher.Bit64")
                : LocalizationManager.T("Launcher.Bit32");
            var versionHint = string.IsNullOrWhiteSpace(infobase.PlatformVersion)
                ? LocalizationManager.T("Launcher.PlatformVersionHint")
                : string.Format(LocalizationManager.T("Launcher.RequestedVersionFormat"), infobase.PlatformVersion);
            GetLogger()?.Warn(
                string.Format(LocalizationManager.T("Launcher.PlatformNotFoundFormat"), archLabel, versionHint));
            return false;
        }

        var arguments = BuildArguments(infobase, mode, clientType, runMode, monopol);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = runAsAdmin, // runas требует ShellExecute
                Verb = runAsAdmin ? "runas" : string.Empty
            };
            Process.Start(psi);

            // Обновляем дату последнего запуска базы.
            infobase.LastLaunchDate = DateTime.Now;

            return true;
        }
        catch (Exception ex)
        {
            GetLogger()?.Error(
                string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message), ex);
            return false;
        }
    }


    /// <summary>
    /// Запускает веб-клиент 1С в браузере по умолчанию.
    /// Для клиент-серверной базы формируется адрес http://сервер/имя_базы,
    /// для файловой базы веб-клиент недоступен — выводится предупреждение.
    /// </summary>
    private static bool LaunchWebClient(Infobase infobase)
    {
        var conn = infobase.Connection;
        string url;

        if (conn.Type == ConnectionType.WebServer)
        {
            if (string.IsNullOrWhiteSpace(conn.WebUrl))
            {
                GetLogger()?.Warn(LocalizationManager.T("Launcher.WebUrlNotSpecified"));
                return false;
            }
            url = conn.WebUrl;
        }
        else if (conn.Type == ConnectionType.ClientServer)
        {
            url = $"http://{conn.Server}/{conn.DatabaseName}";
        }
        else
        {
            GetLogger()?.Warn(LocalizationManager.T("Launcher.WebClientOnlyClientServer"));
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            infobase.LastLaunchDate = DateTime.Now;
            return true;
        }
        catch (Exception ex)
        {
            GetLogger()?.Error(
                string.Format(LocalizationManager.T("Launcher.WebClientOpenFailedFormat"), ex.Message), ex);
            return false;
        }
    }

    /// <summary>Открывает URL в приложении по умолчанию (браузер).
    /// Зеркало Linux-версии (xdg-open) — единая точка открытия ссылок в окнах
    /// (например, каталога релизов в «Проверке обновлений», issue #323).</summary>
    public static bool OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ищет исполняемый файл платформы 1С нужной разрядности и типа клиента.
    /// <list type="bullet">
    /// <item>64-бит: %ProgramFiles%\1cv8\<версия>\bin\ — современные версии: 1cv8.exe / 1cv8c.exe; старые: 1cv8x64.exe</item>
    /// <item>32-бит: %ProgramFiles(x86)%\1cv8\<версия>\bin\ — 1cv8.exe / 1cv8c.exe</item>
    /// </list>
    /// Версия вида «8.3.25.1234 (64)» очищается от суффикса разрядности.
    /// </summary>
    private static string? FindExecutable(
        string version,
        OneCArchitecture architecture,
        OneCClientType? clientType = null,
        OneCLaunchMode mode = OneCLaunchMode.Enterprise)
    {
        // Очищаем версию от суффикса «(32)» / «(64)», если он попал в поле PlatformVersion.
        PlatformVersionService.ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
        if (string.IsNullOrWhiteSpace(cleanVersion))
            cleanVersion = string.Empty;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";

        // Имена exe в порядке приоритета.
        // Конфигуратор всегда требует 1cv8.exe (толстый).
        // Тонкий клиент — предпочтительно 1cv8c.exe, иначе 1cv8.exe с /RunModeManagedApplication.
        // 64-бит старых версий — 1cv8x64.exe.
        string[] exeNames;
        if (mode == OneCLaunchMode.Configurator)
        {
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8.exe" };
        }
        else if (clientType == OneCClientType.Thin)
        {
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8c.exe", "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8c.exe", "1cv8.exe" };
        }
        else if (clientType is null)
        {
            // Автоматический выбор клиента (issue #245): приоритет тонкому клиенту
            // 1cv8c.exe, если он доступен; толстый — только как запасной.
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8c.exe", "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8c.exe", "1cv8.exe" };
        }
        else
        {
            // Толстый клиент: 1cv8.exe (современный 64) / 1cv8x64.exe (старый 64).
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8.exe" };
        }

        // 1. Конкретная версия в bin\. Используем гибкое разрешение каталога версии,
        //    покрывающее и нестандартные корни из дополнительных папок в настройках.
        //    Для частичной версии (префикс, issue #142) ResolveVersionBinDirectory
        //    возвращает каталог новейшей установленной версии с таким префиксом.
        if (!string.IsNullOrWhiteSpace(cleanVersion))
        {
            var versionBinDir = PlatformVersionService.ResolveVersionBinDirectory(cleanVersion, archKey);
            if (versionBinDir != null)
            {
                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(versionBinDir, exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        // 2. Установленные версии нужной разрядности (новейшая по имени каталога).
        //    Гибкий поиск учитывает стандартные и дополнительные корни.
        //    Если запрошена конкретная версия, запасной поиск ограничивается ТОЛЬКО
        //    соответствующими ей вариантами (полная — точным совпадением, частичная —
        //    префиксом с выбором новейшей) и не выбирает произвольную новейшую —
        //    иначе запускалась бы совсем не та версия (issues #29, #142, #28).
        string? best = null;
        string bestDir = string.Empty;
        foreach (var (verName, binDir) in PlatformVersionService.FindPlatformVersionDirs(archKey))
        {
            if (!string.IsNullOrWhiteSpace(cleanVersion) &&
                !VersionMatches(cleanVersion, verName))
            {
                continue;
            }

            string? chosen = null;
            foreach (var exeName in exeNames)
            {
                var candidate = Path.Combine(binDir, exeName);
                if (File.Exists(candidate))
                {
                    chosen = candidate;
                    break; // один exe на каталог версии (первый по приоритету)
                }
            }

            if (chosen is null)
                continue;

            // Берём «наибольшую» версию по числовому сравнению сегментов
            // (8.3.10 > 8.3.9 — строковое сравнение давало бы неверный результат).
            if (best is null || CompareVersionDirs(verName, bestDir) > 0)
            {
                best = chosen;
                bestDir = verName;
            }
        }

        if (best != null)
            return best;

        // 3. Общий лаунчер 1CEStart.exe (разрядность и версию выбирает сам). Применяется
        //    ТОЛЬКО при автоматическом выборе клиента (clientType == null): он открывает
        //    стартер со списком баз, а не подключается к конкретной базе. Для явного
        //    тонкого/толстого клиента такой откат запустил бы «обычное приложение»
        //    вместо запрошенного режима (issue #28), поэтому возвращаем null и
        //    показываем понятное предупреждение «платформа не найдена».
        if (clientType is null && mode == OneCLaunchMode.Enterprise)
        {
            foreach (var root in new[] { programFiles, programFilesX86 }.Where(r => !string.IsNullOrEmpty(r)).Distinct())
            {
                var launcherPath = Path.Combine(root!, "1cv8", "common", "1CEStart.exe");
                if (File.Exists(launcherPath))
                    return launcherPath;
            }
        }

        return null;
    }
}