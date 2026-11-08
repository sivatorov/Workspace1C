using System.Reflection;

namespace Workspace1C;

/// <summary>
/// Чтение версии приложения для отображения пользователю.
/// Возвращает только номер версии (например «0.3.5.34») без суффикса «+<sha>»,
/// который .NET может добавлять к <see cref="AssemblyInformationalVersionAttribute"/>
/// при сборке из git-репозитория.
/// </summary>
public static class VersionInfo
{
    /// <summary>
    /// Информационная версия сборки без хеша коммита, например «0.3.5.34».
    /// Если информационная версия не задана — возвращает 4-частную AssemblyVersion.
    /// </summary>
    public static string Display()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return StripHash(info) ?? asm.GetName().Version?.ToString() ?? "";
    }

    /// <summary>
    /// Старшее число версии программы (2 для 2.x.x.x) — по нему проверяется лицензия ПРО
    /// (docs/pro/PRO-EDITION.md). Берётся из AssemblyVersion, не зависит от суффиксов.
    /// </summary>
    public static int Major() =>
        Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0;

    private static string? StripHash(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var plus = version.IndexOf('+');
        return plus >= 0 ? version.Substring(0, plus) : version;
    }
}