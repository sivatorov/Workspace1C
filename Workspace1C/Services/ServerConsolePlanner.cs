using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Результат открытия внешней консоли администрирования серверов 1С (issue #135).
/// Вместо «bool без причины» несёт ключ локализованной ошибки и подробности
/// (путь, текст исключения), чтобы интерфейс мог показать точную причину отказа.
/// </summary>
public sealed record ServerConsoleResult(bool Success, string? ErrorKey = null, string? Details = null)
{
    /// <summary>Успех (при необходимости — с подробностями, например путём к оснастке).</summary>
    public static ServerConsoleResult Ok(string? details = null) => new(true, null, details);

    /// <summary>Отказ с ключом локализации и необязательными подробностями.</summary>
    public static ServerConsoleResult Fail(string errorKey, string? details = null) => new(false, errorKey, details);
}

/// <summary>
/// Кандидат платформы для внешней консоли администрирования серверов: версия,
/// каталог <c>bin</c>, найденная оснастка <c>1CV8Servers.msc</c> и разрядность.
/// </summary>
public sealed record ServerConsoleCandidate(string Version, string BinDir, string SnapInPath, string Architecture)
{
    /// <summary>Вариант версии в формате приложения: «8.3.27.1688 (64)».</summary>
    public string Display => PlatformVersionService.FormatVariant(Version, Architecture);
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
/// отбор установленных платформ с оснасткой <c>1CV8Servers.msc</c>, выбор версии,
/// построение команд <c>mmc.exe</c> и <c>regsvr32</c>, список компонентов для
/// регистрации и причина отказа по каждой ветке. Без UI и без обращения к диску
/// (проверка существования файлов передаётся делегатом) — покрывается юнит-тестами.
/// </summary>
public static class ServerConsolePlanner
{
    /// <summary>Имя оснастки MMC консоли администрирования серверов 1С.</summary>
    public const string SnapInFileName = "1CV8Servers.msc";

    /// <summary>Компоненты консоли, регистрируемые через regsvr32 (порядок сообщений).</summary>
    public static readonly string[] RegistrationDllNames = { "radmin.dll", "comctrl.dll" };

    /// <summary>Платформа не установлена (не найден ни один каталог версии).</summary>
    public const string ErrorPlatformNotFound = "Admin.ServerConsolePlatformNotFound";

    /// <summary>Платформа есть, но в её каталоге bin нет 1CV8Servers.msc.</summary>
    public const string ErrorSnapInNotFound = "Admin.ServerConsoleSnapInNotFound";

    /// <summary>Не удалось запустить ни mmc.exe, ни оснастку через оболочку.</summary>
    public const string ErrorLaunchFailed = "Admin.ServerConsoleLaunchFailed";

    /// <summary>Внешняя консоль доступна только в Windows.</summary>
    public const string ErrorWindowsOnly = "Admin.ServerConsoleWindowsOnly";

    /// <summary>Нет платформы с оснасткой — регистрировать компоненты не для чего.</summary>
    public const string ErrorRegisterNoPlatform = "Admin.ServerConsoleRegisterNoPlatform";

    /// <summary>В каталоге платформы нет ни radmin.dll, ни comctrl.dll.</summary>
    public const string ErrorRegisterNoFiles = "Admin.ServerConsoleRegisterNoFiles";

    /// <summary>
    /// Создаёт кандидата, если в каталоге <paramref name="binDir"/> есть оснастка.
    /// <paramref name="fileExists"/> позволяет тестировать логику без файловой системы.
    /// </summary>
    public static ServerConsoleCandidate? CreateCandidate(
        string? version, string? binDir, string architecture, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(binDir))
            return null;

        var snapIn = Path.Combine(binDir, SnapInFileName);
        if (!fileExists(snapIn))
            return null;

        return new ServerConsoleCandidate(version.Trim(), binDir, snapIn, NormalizeArchitecture(architecture));
    }

    /// <summary>
    /// Отбирает платформы с оснасткой консоли, убирает дубли по каталогу bin и
    /// сортирует: новейшие версии первыми, при равной версии x64 раньше x86.
    /// </summary>
    public static IReadOnlyList<ServerConsoleCandidate> ListCandidates(
        IEnumerable<(string Version, string BinDir, string Architecture)>? sources,
        Func<string, bool> fileExists)
    {
        var result = new List<ServerConsoleCandidate>();
        if (sources is null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var candidate = CreateCandidate(source.Version, source.BinDir, source.Architecture, fileExists);
            if (candidate is null || !seen.Add(candidate.BinDir))
                continue;
            result.Add(candidate);
        }

        result.Sort((a, b) =>
        {
            // CompareVersions > 0, если a новее b; для сортировки «новее первым» знак инвертируем.
            var byVersion = CompareVersions(b.Version, a.Version);
            if (byVersion != 0)
                return byVersion;
            return string.Compare(b.Architecture, a.Architecture, StringComparison.Ordinal);
        });

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
    /// Нужно ли показывать диалог выбора версии: только если установлено
    /// больше одной платформы с оснасткой (единственная запускается сразу).
    /// </summary>
    public static bool ShouldShowPicker(IReadOnlyList<ServerConsoleCandidate>? candidates)
        => candidates is { Count: > 1 };

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
    /// Причина отказа по состоянию поиска: платформа не установлена, оснастка не
    /// найдена или всё в порядке. Используется сервисом и покрыта тестами.
    /// </summary>
    public static ServerConsoleResult EvaluateAvailability(
        IReadOnlyList<ServerConsoleCandidate>? candidates, string? resolvedBinDir, bool anyPlatformFound)
    {
        if (candidates is { Count: > 0 })
            return ServerConsoleResult.Ok();

        if (!anyPlatformFound && string.IsNullOrWhiteSpace(resolvedBinDir))
            return ServerConsoleResult.Fail(ErrorPlatformNotFound);

        return ServerConsoleResult.Fail(ErrorSnapInNotFound, resolvedBinDir);
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
/// общий формат причины отказа и отчёт о регистрации компонентов для обеих оболочек.
/// </summary>
public static class ServerConsoleMessages
{
    /// <summary>Текст причины отказа: локализованный ключ + подробности (путь/исключение).</summary>
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
        if (!string.IsNullOrWhiteSpace(result.Details))
            text += Environment.NewLine + result.Details;
        return text;
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
}
