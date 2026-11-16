using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Стратегии запуска внешней консоли администрирования серверов 1С (issue #135).
/// Каскад: <c>mmc.exe</c> с основным файлом оснастки → <c>mmc.exe</c> с явным/без
/// <c>/32</c> для альтернативного файла → запуск <c>.msc</c> через оболочку Windows.
/// </summary>
public static class ServerConsoleLaunchStrategies
{
    /// <summary>mmc.exe с основным файлом оснастки.</summary>
    public const string Mmc = "mmc";

    /// <summary>mmc.exe /32 с основным файлом оснастки (32-битная платформа).</summary>
    public const string Mmc32 = "mmc-32";

    /// <summary>mmc.exe с альтернативным файлом оснастки.</summary>
    public const string MmcAlternative = "mmc-alternative";

    /// <summary>mmc.exe /32 с альтернативным файлом оснастки.</summary>
    public const string MmcAlternative32 = "mmc-alternative-32";

    /// <summary>Запуск файла оснастки через оболочку Windows (ассоциация .msc).</summary>
    public const string Shell = "shell";

    /// <summary>Запуск не выполнялся (оснастка не найдена, все попытки исчерпаны).</summary>
    public const string None = "none";

    /// <summary>Внешняя консоль недоступна вне Windows.</summary>
    public const string WindowsOnly = "windows-only";
}

/// <summary>
/// Диагностика поиска оснастки консоли администрирования серверов (issue #135):
/// какие каталоги проверены, какие файлы <c>.msc</c> найдены, сколько платформ
/// найдено и какая стратегия запуска выбрана. Неизменяемый тип: попадает в
/// <see cref="ServerConsoleResult.Diagnostics"/> и выводится в диалоге ошибки.
/// </summary>
public sealed record ServerConsoleDiagnostics(
    IReadOnlyList<string> CheckedDirectories,
    IReadOnlyList<string> FoundSnapIns,
    int PlatformCount,
    string LaunchStrategy,
    string? PlatformVersion = null,
    string? SnapInPath = null)
{
    /// <summary>Пустая диагностика (ничего не проверялось).</summary>
    public static ServerConsoleDiagnostics Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        0,
        ServerConsoleLaunchStrategies.None);

    /// <summary>Признак того, что хоть один файл оснастки найден.</summary>
    public bool HasSnapIns => FoundSnapIns is { Count: > 0 };

    /// <summary>Копия диагностики с другой стратегией запуска (issue #135).</summary>
    public ServerConsoleDiagnostics WithStrategy(string? strategy) => this with
    {
        LaunchStrategy = string.IsNullOrWhiteSpace(strategy)
            ? ServerConsoleLaunchStrategies.None
            : strategy!.Trim()
    };
}

/// <summary>
/// Одна попытка запуска консоли администрирования серверов (issue #135):
/// стратегия, команда, аргументы и способ запуска (оболочка или прямой процесс).
/// </summary>
public sealed record ServerConsoleLaunchAttempt(
    string Strategy,
    string FileName,
    string? Arguments,
    bool UseShellExecute,
    string? SnapInPath = null);

/// <summary>
/// Результат поиска оснастки в каталоге платформы (issue #135): найденный файл
/// (основной), проверенные каталоги и все найденные файлы <c>.msc</c> без дублей.
/// </summary>
public sealed record ServerConsoleSnapInSearch(
    string? SnapInPath,
    IReadOnlyList<string> CheckedDirectories,
    IReadOnlyList<string> FoundSnapIns)
{
    /// <summary>Ничего не найдено.</summary>
    public static ServerConsoleSnapInSearch Empty { get; } = new(
        null, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Альтернативные файлы оснастки (без основного, без дублей).</summary>
    public IReadOnlyList<string> Alternatives => FoundSnapIns
        .Where(path => !string.Equals(path, SnapInPath, StringComparison.OrdinalIgnoreCase))
        .ToList();
}

/// <summary>
/// Установленная платформа 1С и статус оснастки консоли администрирования серверов
/// (issue #135). Нужна диалогу выбора версии: он показывает статус по каждой
/// версии, даже если оснастка не найдена ни в одной из них.
/// </summary>
public sealed record ServerConsolePlatformOption(
    string Version,
    string BinDir,
    string Architecture,
    string? SnapInPath,
    IReadOnlyList<string> SnapIns)
{
    /// <summary>Вариант версии в формате приложения: «8.3.27.1688 (64)».</summary>
    public string Display => PlatformVersionService.FormatVariant(Version, Architecture);

    /// <summary>Признак наличия оснастки в каталоге платформы.</summary>
    public bool HasSnapIn => !string.IsNullOrWhiteSpace(SnapInPath);

    /// <summary>Кандидат запуска или null, если оснастка не найдена.</summary>
    public ServerConsoleCandidate? ToCandidate() => HasSnapIn
        ? new ServerConsoleCandidate(Version, BinDir, SnapInPath!, Architecture, SnapIns)
        : null;
}

/// <summary>
/// Результат открытия внешней консоли администрирования серверов 1С (issue #135).
/// Вместо «bool без причины» несёт ключ локализованной ошибки, подробности
/// (путь, текст исключения) и структурированную диагностику поиска оснастки,
/// чтобы интерфейс мог показать точную причину отказа и предложить действия.
/// </summary>
public sealed record ServerConsoleResult(
    bool Success,
    string? ErrorKey = null,
    string? Details = null,
    ServerConsoleDiagnostics? Diagnostics = null)
{
    /// <summary>Успех (при необходимости — с подробностями, например путём к оснастке).</summary>
    public static ServerConsoleResult Ok(
        string? details = null, ServerConsoleDiagnostics? diagnostics = null)
        => new(true, null, details, diagnostics);

    /// <summary>Отказ с ключом локализации и необязательными подробностями.</summary>
    public static ServerConsoleResult Fail(
        string errorKey, string? details = null, ServerConsoleDiagnostics? diagnostics = null)
        => new(false, errorKey, details, diagnostics);

    /// <summary>
    /// Стоит ли предложить регистрацию компонентов консоли (radmin.dll, comctrl.dll):
    /// оснастка не найдена или запуск не удался (issue #135, просьба автора issue).
    /// </summary>
    public bool CanOfferRegistration => !Success
        && (ErrorKey == ServerConsolePlanner.ErrorLaunchFailed
            || ErrorKey == ServerConsolePlanner.ErrorSnapInNotFound
            || ErrorKey == ServerConsolePlanner.ErrorPlatformNotFound);

    /// <summary>
    /// Стоит ли предложить выбор версии платформы (issue #135): платформа есть,
    /// но оснастки в ней нет — возможно, подойдёт другая установленная версия.
    /// </summary>
    public bool CanOfferPlatformPicker => !Success
        && (ErrorKey == ServerConsolePlanner.ErrorSnapInNotFound
            || ErrorKey == ServerConsolePlanner.ErrorPlatformNotFound);
}

/// <summary>
/// Кандидат платформы для внешней консоли администрирования серверов: версия,
/// каталог <c>bin</c>, найденная оснастка и разрядность. <see cref="Alternatives"/>
/// содержит дополнительные файлы оснастки (<c>1CV8Servers64.msc</c> и подобные),
/// которые пробуются по каскаду стратегий запуска (issue #135).
/// </summary>
public sealed record ServerConsoleCandidate(
    string Version,
    string BinDir,
    string SnapInPath,
    string Architecture,
    IReadOnlyList<string>? Alternatives = null)
{
    /// <summary>Вариант версии в формате приложения: «8.3.27.1688 (64)».</summary>
    public string Display => PlatformVersionService.FormatVariant(Version, Architecture);

    /// <summary>
    /// Все найденные файлы оснастки: основной первым, альтернативы — без дублей
    /// (сравнение путей регистронезависимое, issue #135).
    /// </summary>
    public IReadOnlyList<string> AllSnapIns
    {
        get
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(SnapInPath))
            {
                result.Add(SnapInPath);
                seen.Add(SnapInPath);
            }

            if (Alternatives is not null)
            {
                foreach (var alternative in Alternatives)
                {
                    if (string.IsNullOrWhiteSpace(alternative) || !seen.Add(alternative))
                        continue;
                    result.Add(alternative);
                }
            }

            return result;
        }
    }
}

/// <summary>Компонент консоли (radmin.dll / comctrl.dll) и признак наличия файла.</summary>
public sealed record ServerConsoleDll(string DllPath, bool Exists);

/// <summary>Результат регистрации одного компонента консоли.</summary>
public sealed record ServerConsoleRegistrationItem(string DllPath, bool FileExists, bool Success, string? Error);

/// <summary>Результат регистрации компонентов консоли (radmin.dll, comctrl.dll).</summary>
public sealed record ServerConsoleRegistrationResult(
    bool Success,
    string? BinDir,
    string? ErrorKey,
    IReadOnlyList<ServerConsoleRegistrationItem> Items);

/// <summary>
/// Чистая логика внешней консоли администрирования серверов 1С (issue #135):
/// расширенный поиск оснастки (<c>1CV8Servers.msc</c> и альтернативные имена,
/// маски <c>*servers*.msc</c>/<c>*admin*.msc</c> в <c>bin</c>, <c>..</c> и
/// <c>..\common</c>), отбор установленных платформ, выбор версии, построение
/// каскада команд <c>mmc.exe</c>/оболочки, команды <c>regsvr32</c>, список
/// компонентов для регистрации и диагностика причины отказа по каждой ветке.
/// Без UI и без прямого обращения к диску (проверка существования и перечисление
/// файлов передаются делегатами) — покрывается юнит-тестами.
/// </summary>
public static class ServerConsolePlanner
{
    /// <summary>Каноническое имя оснастки MMC консоли администрирования серверов 1С.</summary>
    public const string SnapInFileName = "1CV8Servers.msc";

    /// <summary>
    /// Имена-кандидаты оснастки в порядке приоритета (issue #135): точное
    /// <see cref="SnapInFileName"/> важнее альтернативных вариантов.
    /// </summary>
    public static readonly string[] SnapInFileNames =
    {
        SnapInFileName,
        "1CV8Servers64.msc",
        "1CV8Servers32.msc",
        "1CV8Servers_x64.msc",
        "1CV8Servers_x86.msc",
        "1CV8ServersAdmin.msc",
        "1CV8Admin.msc",
        "1CV8Server.msc"
    };

    /// <summary>Маски файлов оснастки для регистронезависимого поиска (issue #135).</summary>
    public static readonly string[] SnapInSearchMasks = { "*servers*.msc", "*admin*.msc" };

    /// <summary>Максимум найденных файлов оснастки на одну платформу (диагностика и каскад).</summary>
    public const int MaxSnapIns = 8;

    /// <summary>Компоненты консоли, регистрируемые через regsvr32 (порядок сообщений).</summary>
    public static readonly string[] RegistrationDllNames = { "radmin.dll", "comctrl.dll" };

    /// <summary>Платформа не установлена (не найден ни один каталог версии).</summary>
    public const string ErrorPlatformNotFound = "Admin.ServerConsolePlatformNotFound";

    /// <summary>Платформа есть, но в её каталоге bin нет оснастки консоли.</summary>
    public const string ErrorSnapInNotFound = "Admin.ServerConsoleSnapInNotFound";

    /// <summary>Не удалось запустить ни mmc.exe, ни оснастку через оболочку.</summary>
    public const string ErrorLaunchFailed = "Admin.ServerConsoleLaunchFailed";

    /// <summary>Внешняя консоль доступна только в Windows.</summary>
    public const string ErrorWindowsOnly = "Admin.ServerConsoleWindowsOnly";

    /// <summary>Нет платформы — регистрировать компоненты не для чего.</summary>
    public const string ErrorRegisterNoPlatform = "Admin.ServerConsoleRegisterNoPlatform";

    /// <summary>В каталоге платформы нет ни radmin.dll, ни comctrl.dll.</summary>
    public const string ErrorRegisterNoFiles = "Admin.ServerConsoleRegisterNoFiles";

    /// <summary>
    /// Каталоги, в которых ищется оснастка (issue #135): <c>bin</c>, каталог версии
    /// (<c>..</c>) и общий каталог платформы (<c>..\common</c>). В каталоге версии
    /// некоторых сборок оснастка лежит не в <c>bin</c>.
    /// </summary>
    public static IReadOnlyList<string> GetSnapInSearchDirectories(string? binDir)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(binDir))
            return result;

        var trimmed = binDir.Trim().TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        result.Add(trimmed);

        var parent = Path.GetDirectoryName(trimmed);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            result.Add(parent!);
            result.Add(Path.Combine(parent!, "common"));
        }

        return result;
    }

    /// <summary>
    /// Ищет оснастку консоли администрирования серверов в каталогах платформы
    /// (issue #135): сначала точные имена в порядке приоритета, затем маски
    /// <c>*servers*.msc</c> и <c>*admin*.msc</c> (регистронезависимо).
    /// Результат дедуплицируется по полному пути. <paramref name="enumerateFiles"/>
    /// может быть null — тогда работает только проверка точных имён.
    /// </summary>
    public static ServerConsoleSnapInSearch FindSnapIn(
        string? binDir,
        Func<string, bool> fileExists,
        Func<string, IEnumerable<string>>? enumerateFiles = null)
    {
        var checkedDirectories = GetSnapInSearchDirectories(binDir);
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || found.Count >= MaxSnapIns)
                return;
            if (seen.Add(path))
                found.Add(path);
        }

        // 1. Точные имена: 1CV8Servers.msc важнее 1CV8Servers64.msc и т.п.,
        //    поэтому внешний цикл — по именам, внутренний — по каталогам.
        foreach (var name in SnapInFileNames)
        {
            foreach (var directory in checkedDirectories)
            {
                var path = Path.Combine(directory, name);
                if (fileExists(path))
                    Add(path);
            }
        }

        // 2. Поиск по маске: покрывает нестандартные имена и регистр файла.
        if (enumerateFiles is not null)
        {
            foreach (var directory in checkedDirectories)
            {
                IEnumerable<string> files;
                try
                {
                    files = enumerateFiles(directory) ?? Array.Empty<string>();
                }
                catch
                {
                    // Каталог недоступен — это не повод срывать поиск (issue #135).
                    continue;
                }

                foreach (var file in files)
                {
                    if (string.IsNullOrWhiteSpace(file))
                        continue;
                    var name = Path.GetFileName(file);
                    if (!MatchesSnapInMask(name))
                        continue;
                    Add(Path.IsPathRooted(file) ? file : Path.Combine(directory, name));
                }
            }
        }

        return new ServerConsoleSnapInSearch(found.FirstOrDefault(), checkedDirectories, found);
    }

    /// <summary>
    /// Создаёт кандидата, если в каталоге <paramref name="binDir"/> (или рядом с ним)
    /// найдена оснастка. <paramref name="fileExists"/> позволяет тестировать логику
    /// без файловой системы.
    /// </summary>
    public static ServerConsoleCandidate? CreateCandidate(
        string? version, string? binDir, string architecture, Func<string, bool> fileExists)
        => CreateCandidate(version, binDir, architecture, fileExists, null);

    /// <summary>
    /// Создаёт кандидата с расширенным поиском оснастки (issue #135): точные имена,
    /// маски, каталоги <c>bin</c>, <c>..</c> и <c>..\common</c>.
    /// </summary>
    public static ServerConsoleCandidate? CreateCandidate(
        string? version,
        string? binDir,
        string architecture,
        Func<string, bool> fileExists,
        Func<string, IEnumerable<string>>? enumerateFiles)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(binDir))
            return null;

        var search = FindSnapIn(binDir, fileExists, enumerateFiles);
        if (search.SnapInPath is null)
            return null;

        return new ServerConsoleCandidate(
            version.Trim(), binDir, search.SnapInPath, NormalizeArchitecture(architecture), search.FoundSnapIns);
    }

    /// <summary>
    /// Отбирает платформы с оснасткой консоли, убирает дубли по каталогу bin и
    /// сортирует: новейшие версии первыми, при равной версии x64 раньше x86.
    /// </summary>
    public static IReadOnlyList<ServerConsoleCandidate> ListCandidates(
        IEnumerable<(string Version, string BinDir, string Architecture)>? sources,
        Func<string, bool> fileExists)
        => ListCandidates(sources, fileExists, null);

    /// <summary>
    /// Отбирает платформы с оснасткой консоли с расширенным поиском (issue #135):
    /// альтернативные имена, маски и каталог <c>common</c>.
    /// </summary>
    public static IReadOnlyList<ServerConsoleCandidate> ListCandidates(
        IEnumerable<(string Version, string BinDir, string Architecture)>? sources,
        Func<string, bool> fileExists,
        Func<string, IEnumerable<string>>? enumerateFiles)
    {
        var result = new List<ServerConsoleCandidate>();
        foreach (var option in ListPlatformOptions(sources, fileExists, enumerateFiles))
        {
            var candidate = option.ToCandidate();
            if (candidate is not null)
                result.Add(candidate);
        }

        return result;
    }

    /// <summary>
    /// Все установленные платформы со статусом оснастки (issue #135): нужны диалогу
    /// выбора версии, который показывается при нескольких платформах и отображает
    /// статус по каждой версии, даже если оснастка не найдена ни в одной.
    /// </summary>
    public static IReadOnlyList<ServerConsolePlatformOption> ListPlatformOptions(
        IEnumerable<(string Version, string BinDir, string Architecture)>? sources,
        Func<string, bool> fileExists,
        Func<string, IEnumerable<string>>? enumerateFiles = null)
    {
        var result = new List<ServerConsolePlatformOption>();
        if (sources is null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Version) || string.IsNullOrWhiteSpace(source.BinDir))
                continue;
            if (!seen.Add(source.BinDir))
                continue;

            var search = FindSnapIn(source.BinDir, fileExists, enumerateFiles);
            result.Add(new ServerConsolePlatformOption(
                source.Version.Trim(),
                source.BinDir,
                NormalizeArchitecture(source.Architecture),
                search.SnapInPath,
                search.FoundSnapIns));
        }

        result.Sort(CompareOptions);
        return result;
    }

    /// <summary>
    /// Выбирает кандидата по сохранённой/выбранной версии. Понимает полный вариант
    /// «8.3.27.1688 (64)», чистую версию «8.3.27.1688» и частичную «8.3.27».
    /// Если версия не установлена (или не задана) — возвращает новейшую.
    /// </summary>
    public static ServerConsoleCandidate? ChoosePreferred(
        IReadOnlyList<ServerConsoleCandidate>? candidates, string? preferredVersion)
    {
        if (candidates is null || candidates.Count == 0)
            return null;
        if (string.IsNullOrWhiteSpace(preferredVersion))
            return candidates[0];

        var preferred = preferredVersion.Trim();

        // 1. Точное совпадение варианта (включая суффикс разрядности).
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Display, preferred, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        PlatformVersionService.ParseVariantOptionalArch(preferred, out var cleanVersion, out var architecture);
        cleanVersion = (cleanVersion ?? string.Empty).Trim();
        if (cleanVersion.Length == 0)
            return candidates[0];

        // 2. Та же версия без суффикса; при явной разрядности — сначала она.
        var sameVersion = candidates
            .Where(c => string.Equals(c.Version, cleanVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameVersion.Count > 0)
        {
            if (architecture is not null)
            {
                var exactArchitecture = sameVersion
                    .FirstOrDefault(c => string.Equals(c.Architecture, architecture, StringComparison.Ordinal));
                if (exactArchitecture is not null)
                    return exactArchitecture;
            }
            return sameVersion[0];
        }

        // 3. Частичная версия («8.3.27») — новейшая подходящая сборка.
        var prefixMatch = candidates.FirstOrDefault(c => MatchesVersionPrefix(c.Version, cleanVersion));
        return prefixMatch ?? candidates[0];
    }

    /// <summary>
    /// Выбирает установленную платформу (в том числе без оснастки) по сохранённой
    /// или выбранной версии — для диалога выбора версии и регистрации компонентов
    /// (issue #135). Логика та же, что у <see cref="ChoosePreferred"/>.
    /// </summary>
    public static ServerConsolePlatformOption? ChoosePreferredPlatform(
        IReadOnlyList<ServerConsolePlatformOption>? options, string? preferredVersion)
    {
        if (options is null || options.Count == 0)
            return null;
        if (string.IsNullOrWhiteSpace(preferredVersion))
            return options[0];

        var preferred = preferredVersion.Trim();
        foreach (var option in options)
        {
            if (string.Equals(option.Display, preferred, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        PlatformVersionService.ParseVariantOptionalArch(preferred, out var cleanVersion, out var architecture);
        cleanVersion = (cleanVersion ?? string.Empty).Trim();
        if (cleanVersion.Length == 0)
            return options[0];

        var sameVersion = options
            .Where(o => string.Equals(o.Version, cleanVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameVersion.Count > 0)
        {
            if (architecture is not null)
            {
                var exactArchitecture = sameVersion
                    .FirstOrDefault(o => string.Equals(o.Architecture, architecture, StringComparison.Ordinal));
                if (exactArchitecture is not null)
                    return exactArchitecture;
            }
            return sameVersion[0];
        }

        return options.FirstOrDefault(o => MatchesVersionPrefix(o.Version, cleanVersion)) ?? options[0];
    }

    /// <summary>
    /// Нужно ли показывать диалог выбора версии: только если установлено
    /// больше одной платформы с оснасткой (единственная запускается сразу).
    /// </summary>
    public static bool ShouldShowPicker(IReadOnlyList<ServerConsoleCandidate>? candidates)
        => candidates is { Count: > 1 };

    /// <summary>
    /// Нужно ли показывать диалог выбора версии по списку установленных платформ
    /// (issue #135): да, если платформ больше одной — даже когда оснастка не найдена
    /// ни в одной (в диалоге виден статус по каждой версии).
    /// </summary>
    public static bool ShouldShowPlatformPicker(IReadOnlyList<ServerConsolePlatformOption>? options)
        => options is { Count: > 1 };

    /// <summary>
    /// Аргументы <c>mmc.exe</c>: путь к оснастке всегда в кавычках; для 32-битной
    /// платформы добавляется <c>/32</c> (32-битный radmin.dll не загрузится
    /// 64-битной оснасткой MMC).
    /// </summary>
    public static string BuildMmcArguments(string? snapInPath, bool use32Bit)
    {
        var quoted = "\"" + (snapInPath ?? string.Empty) + "\"";
        return use32Bit ? quoted + " /32" : quoted;
    }

    /// <summary>Аргументы regsvr32: тихая регистрация DLL с путём в кавычках.</summary>
    public static string BuildRegSvr32Arguments(string? dllPath)
        => "/s \"" + (dllPath ?? string.Empty) + "\"";

    /// <summary>
    /// Каскад стратегий запуска консоли (issue #135), по порядку:
    /// <list type="number">
    /// <item><c>mmc.exe "<основной .msc>"</c> (+ <c>/32</c> для 32-битной платформы);</item>
    /// <item><c>mmc.exe</c> с явным <c>/32</c> или без него для альтернативного файла оснастки;</item>
    /// <item>запуск <c>.msc</c> через оболочку Windows (<c>UseShellExecute=true</c>).</item>
    /// </list>
    /// Чистая функция: сервис исполняет попытки по очереди и логирует каждую.
    /// </summary>
    public static IReadOnlyList<ServerConsoleLaunchAttempt> BuildLaunchPlan(
        ServerConsoleCandidate? candidate, string? mmcExecutable)
    {
        var attempts = new List<ServerConsoleLaunchAttempt>();
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.SnapInPath))
            return attempts;

        var mmc = string.IsNullOrWhiteSpace(mmcExecutable) ? "mmc.exe" : mmcExecutable!.Trim();
        var primary32 = ShouldUse32Bit(candidate.Architecture);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddMmc(string snapIn, bool use32Bit, bool alternative)
        {
            var arguments = BuildMmcArguments(snapIn, use32Bit);
            if (!seen.Add(mmc + "|" + arguments))
                return;

            var strategy = alternative
                ? (use32Bit ? ServerConsoleLaunchStrategies.MmcAlternative32 : ServerConsoleLaunchStrategies.MmcAlternative)
                : (use32Bit ? ServerConsoleLaunchStrategies.Mmc32 : ServerConsoleLaunchStrategies.Mmc);
            attempts.Add(new ServerConsoleLaunchAttempt(strategy, mmc, arguments, false, snapIn));
        }

        // a) mmc.exe с основным файлом оснастки (с /32 для 32-битной платформы).
        AddMmc(candidate.SnapInPath, primary32, alternative: false);

        // b) альтернативные файлы оснастки — с явным /32 и без него.
        foreach (var alternative in candidate.AllSnapIns)
        {
            if (string.Equals(alternative, candidate.SnapInPath, StringComparison.OrdinalIgnoreCase))
                continue;
            AddMmc(alternative, primary32, alternative: true);
            AddMmc(alternative, !primary32, alternative: true);
        }

        // c) последняя стратегия — запуск .msc через оболочку (ассоциация файлов).
        attempts.Add(new ServerConsoleLaunchAttempt(
            ServerConsoleLaunchStrategies.Shell, candidate.SnapInPath, null, true, candidate.SnapInPath));

        return attempts;
    }

    /// <summary>
    /// Путь к <c>mmc.exe</c>: из системного каталога, если файл существует, иначе
    /// bare-имя (разрешение через PATH). Чистая функция — для тестов.
    /// </summary>
    public static string ResolveMmcExecutable(string? systemDirectory, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(systemDirectory))
        {
            var candidate = Path.Combine(systemDirectory, "mmc.exe");
            if (fileExists(candidate))
                return candidate;
        }
        return "mmc.exe";
    }

    /// <summary>
    /// Путь к <c>regsvr32.exe</c> нужной разрядности: 64-бит — System32,
    /// 32-бит — SysWOW64 (иначе 32-битная DLL не зарегистрируется).
    /// </summary>
    public static string ResolveRegSvr32Executable(
        string? windowsDirectory, bool is64Bit, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            return "regsvr32.exe";

        var candidate = is64Bit
            ? Path.Combine(windowsDirectory, "System32", "regsvr32.exe")
            : Path.Combine(windowsDirectory, "SysWOW64", "regsvr32.exe");

        return fileExists(candidate) ? candidate : "regsvr32.exe";
    }

    /// <summary>
    /// Список компонентов консоли для регистрации — всегда оба имени, с признаком
    /// наличия файла: отсутствующий файл даёт понятное сообщение, а не исключение.
    /// </summary>
    public static IReadOnlyList<ServerConsoleDll> ListRegistrationDlls(
        string? binDir, Func<string, bool> fileExists)
    {
        var result = new List<ServerConsoleDll>();
        if (string.IsNullOrWhiteSpace(binDir))
            return result;

        foreach (var name in RegistrationDllNames)
        {
            var path = Path.Combine(binDir, name);
            result.Add(new ServerConsoleDll(path, fileExists(path)));
        }
        return result;
    }

    /// <summary>
    /// Диагностика по списку установленных платформ (issue #135): проверенные
    /// каталоги оснастки, найденные файлы <c>.msc</c>, число платформ и стратегия.
    /// </summary>
    public static ServerConsoleDiagnostics BuildDiagnostics(
        IReadOnlyList<ServerConsolePlatformOption>? options,
        string? launchStrategy = null,
        string? platformVersion = null,
        string? snapInPath = null)
    {
        var checkedDirectories = new List<string>();
        var foundSnapIns = new List<string>();
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options is not null)
        {
            foreach (var option in options)
            {
                foreach (var directory in GetSnapInSearchDirectories(option.BinDir))
                {
                    if (seenDirectories.Add(directory))
                        checkedDirectories.Add(directory);
                }

                foreach (var snapIn in option.SnapIns)
                {
                    if (seenFiles.Add(snapIn))
                        foundSnapIns.Add(snapIn);
                }
            }
        }

        return new ServerConsoleDiagnostics(
            checkedDirectories,
            foundSnapIns,
            options?.Count ?? 0,
            string.IsNullOrWhiteSpace(launchStrategy) ? ServerConsoleLaunchStrategies.None : launchStrategy!.Trim(),
            platformVersion,
            snapInPath);
    }

    /// <summary>
    /// Причина отказа по состоянию поиска: платформа не установлена, оснастка не
    /// найдена или всё в порядке. Используется сервисом и покрыта тестами.
    /// </summary>
    public static ServerConsoleResult EvaluateAvailability(
        IReadOnlyList<ServerConsoleCandidate>? candidates, string? resolvedBinDir, bool anyPlatformFound)
        => EvaluateAvailability(candidates, resolvedBinDir, anyPlatformFound, null);

    /// <summary>
    /// То же, но с диагностикой (issue #135): при отказе <see cref="ServerConsoleResult.Details"/>
    /// содержит человекочитаемый перечень проверенных каталогов и найденных файлов
    /// оснастки, а <see cref="ServerConsoleResult.Diagnostics"/> — структурированные данные.
    /// </summary>
    public static ServerConsoleResult EvaluateAvailability(
        IReadOnlyList<ServerConsoleCandidate>? candidates,
        string? resolvedBinDir,
        bool anyPlatformFound,
        ServerConsoleDiagnostics? diagnostics)
    {
        if (candidates is { Count: > 0 })
            return ServerConsoleResult.Ok(null, diagnostics);

        if (!anyPlatformFound && string.IsNullOrWhiteSpace(resolvedBinDir))
            return ServerConsoleResult.Fail(ErrorPlatformNotFound, null, diagnostics);

        var details = diagnostics is null
            ? resolvedBinDir
            : ServerConsoleMessages.FormatDiagnostics(diagnostics);
        return ServerConsoleResult.Fail(ErrorSnapInNotFound, details, diagnostics);
    }

    /// <summary>Для 32-битной платформы оснастку нужно запускать как <c>mmc /32</c>.</summary>
    public static bool ShouldUse32Bit(string? architecture)
        => NormalizeArchitecture(architecture) == "32";

    private static string NormalizeArchitecture(string? architecture)
        => string.Equals(architecture?.Trim(), "64", StringComparison.Ordinal) ? "64" : "32";

    private static bool MatchesVersionPrefix(string version, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return false;
        if (string.Equals(version, prefix, StringComparison.OrdinalIgnoreCase))
            return true;
        return version.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSnapInMask(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (!fileName!.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
            return false;
        return fileName.Contains("servers", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("admin", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareOptions(ServerConsolePlatformOption a, ServerConsolePlatformOption b)
    {
        // CompareVersions > 0, если a новее b; для сортировки «новее первым» знак инвертируем.
        var byVersion = CompareVersions(b.Version, a.Version);
        if (byVersion != 0)
            return byVersion;
        return string.Compare(b.Architecture, a.Architecture, StringComparison.Ordinal);
    }

    private static int CompareVersions(string a, string b)
    {
        static int[] Parts(string value) =>
            (value ?? string.Empty)
                .Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => int.TryParse(p, out _))
                .Select(int.Parse)
                .ToArray();

        var pa = Parts(a);
        var pb = Parts(b);
        var length = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < length; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
    }
}

/// <summary>
/// Локализованные сообщения консоли администрирования серверов (issue #135):
/// общий формат причины отказа, человекочитаемая диагностика поиска оснастки,
/// статусы версий для диалога выбора и отчёт о регистрации компонентов.
/// </summary>
public static class ServerConsoleMessages
{
    /// <summary>
    /// Текст причины отказа: локализованный ключ + подробности (путь/исключение)
    /// + диагностика поиска оснастки (проверенные каталоги и найденные файлы).
    /// </summary>
    public static string Format(ServerConsoleResult? result)
    {
        if (result is null)
            return string.Empty;
        if (result.Success)
            return result.Details ?? string.Empty;

        var key = string.IsNullOrWhiteSpace(result.ErrorKey)
            ? "Admin.ServerConsoleFailed"
            : result.ErrorKey!;
        var text = LocalizationManager.T(key);

        var details = BuildDetails(result);
        if (!string.IsNullOrWhiteSpace(details))
            text += Environment.NewLine + details;
        return text;
    }

    /// <summary>
    /// Диагностика поиска оснастки в человекочитаемом виде: заголовок, число
    /// найденных платформ, проверенные каталоги, найденные файлы <c>.msc</c> и
    /// стратегия запуска (issue #135).
    /// </summary>
    public static string FormatDiagnostics(ServerConsoleDiagnostics? diagnostics)
    {
        if (diagnostics is null)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine(LocalizationManager.T("Admin.ServerConsoleDiagHeader"));
        builder.AppendLine(FormatTemplate("Admin.ServerConsoleDiagPlatforms", diagnostics.PlatformCount));

        if (!string.IsNullOrWhiteSpace(diagnostics.PlatformVersion))
            builder.AppendLine(FormatTemplate("Admin.ServerConsoleDiagPlatformLine", diagnostics.PlatformVersion));

        builder.AppendLine(LocalizationManager.T("Admin.ServerConsoleDiagCheckedDirs"));
        AppendList(builder, diagnostics.CheckedDirectories);

        builder.AppendLine(LocalizationManager.T("Admin.ServerConsoleDiagFoundSnapIns"));
        AppendList(builder, diagnostics.FoundSnapIns);

        if (diagnostics.FoundSnapIns is { Count: > 1 })
        {
            builder.AppendLine(FormatTemplate(
                "Admin.ServerConsoleSnapInAlternatives", diagnostics.FoundSnapIns.Count));
        }

        builder.AppendLine(FormatTemplate(
            "Admin.ServerConsoleDiagStrategy", FormatStrategy(diagnostics.LaunchStrategy)));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Статус оснастки по каждой установленной платформе (issue #135) — подписи
    /// для диалога выбора версии: «оснастка найдена: 1CV8Servers64.msc» или
    /// «оснастка не найдена». Ключ — вариант версии («8.3.27.1688 (64)»).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildPlatformStatuses(
        IReadOnlyList<ServerConsolePlatformOption>? options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options is null)
            return result;

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Display) || result.ContainsKey(option.Display))
                continue;

            result[option.Display] = option.HasSnapIn
                ? FormatTemplate("Admin.ServerConsolePickAnySnapIn", Path.GetFileName(option.SnapInPath))
                : LocalizationManager.T("Admin.ServerConsolePickAnyNoSnapIn");
        }

        return result;
    }

    /// <summary>
    /// Отчёт по каждой DLL: успех, ошибка или «файл не найден». Отсутствующий файл
    /// и отмена UAC не бросают исключение — попадают в отчёт строкой.
    /// </summary>
    public static string BuildRegistrationReport(ServerConsoleRegistrationResult? result)
    {
        if (result is null)
            return string.Empty;

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.BinDir))
        {
            builder.AppendLine(string.Format(
                LocalizationManager.T("Admin.ServerConsoleRegisterReport"), result.BinDir));
        }

        if (result.Items is null || result.Items.Count == 0)
        {
            var key = string.IsNullOrWhiteSpace(result.ErrorKey)
                ? ServerConsolePlanner.ErrorRegisterNoPlatform
                : result.ErrorKey!;
            builder.AppendLine(LocalizationManager.T(key));
            return builder.ToString().TrimEnd();
        }

        foreach (var item in result.Items)
        {
            var name = Path.GetFileName(item.DllPath);
            if (!item.FileExists)
            {
                builder.AppendLine($"✗ {name}{LocalizationManager.T("Admin.ServerConsoleRegisterMissing")}");
            }
            else if (item.Success)
            {
                builder.AppendLine($"✓ {name}{LocalizationManager.T("Admin.ServerConsoleRegisterOk")}");
            }
            else
            {
                builder.AppendLine($"✗ {name}{string.Format(
                    LocalizationManager.T("Admin.ServerConsoleRegisterFailed"), item.Error ?? string.Empty)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Подробности отказа: текст исключения/путь и диагностика без дублей.</summary>
    private static string BuildDetails(ServerConsoleResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Details))
            parts.Add(result.Details!.Trim());

        var diagnostics = FormatDiagnostics(result.Diagnostics);
        if (!string.IsNullOrWhiteSpace(diagnostics)
            && !parts.Any(part => part.Contains(diagnostics, StringComparison.Ordinal)))
        {
            parts.Add(diagnostics);
        }

        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Подстановка значений в локализованный шаблон (issue #135): если во внешнем
    /// файле языка шаблон без плейсхолдеров (устаревший перевод), значения
    /// дописываются через пробел — данные диагностики не теряются.
    /// </summary>
    private static string FormatTemplate(string key, params object?[] args)
    {
        var template = LocalizationManager.T(key);
        if (args.Length == 0 || template.Contains("{0}", StringComparison.Ordinal))
            return string.Format(template, args);

        var suffix = string.Join(" ", args.Select(arg => arg?.ToString() ?? string.Empty));
        return (template + " " + suffix).TrimEnd();
    }

    private static void AppendList(StringBuilder builder, IReadOnlyList<string>? items)
    {
        if (items is null || items.Count == 0)
        {
            builder.AppendLine("  - " + LocalizationManager.T("Admin.ServerConsoleDiagNothing"));
            return;
        }

        foreach (var item in items)
            builder.AppendLine("  - " + item);
    }

    private static string FormatStrategy(string? strategy)
    {
        var key = strategy switch
        {
            ServerConsoleLaunchStrategies.Mmc => "Admin.ServerConsoleStrategyMmc",
            ServerConsoleLaunchStrategies.Mmc32 => "Admin.ServerConsoleStrategyMmc32",
            ServerConsoleLaunchStrategies.MmcAlternative => "Admin.ServerConsoleStrategyMmcAlternative",
            ServerConsoleLaunchStrategies.MmcAlternative32 => "Admin.ServerConsoleStrategyMmcAlternative32",
            ServerConsoleLaunchStrategies.Shell => "Admin.ServerConsoleStrategyShell",
            ServerConsoleLaunchStrategies.WindowsOnly => "Admin.ServerConsoleStrategyWindowsOnly",
            _ => "Admin.ServerConsoleStrategyNone"
        };

        return LocalizationManager.T(key);
    }
}
