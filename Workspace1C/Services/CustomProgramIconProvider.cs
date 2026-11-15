using System;
using System.Collections.Concurrent;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Иконка программы группы «Мои программы» (issue #91). Если включена галочка
/// «Иконка из файла» — используется путь <see cref="CustomProgram.IconPath"/>; если
/// галочка снята — основная иконка исполняемого файла (как в проводнике): на Windows
/// она извлекается через <c>Icon.ExtractAssociatedIcon</c>, на Linux извлечение
/// не поддерживается, и панель показывает стандартную иконку группы.
/// Извлечённые иконки кэшируются по пути к файлу, чтобы не читать файл на каждую
/// перерисовку панели. Любая ошибка (файла нет, нет прав, не PE-файл) даёт
/// <c>null</c> — вызывающий код показывает стандартную иконку; исключения наружу
/// не выходят.
/// </summary>
public static partial class CustomProgramIconProvider
{
    /// <summary>Кэш извлечённых иконок: путь к файлу → PNG-байты (null — извлечь не удалось).</summary>
    private static readonly ConcurrentDictionary<string, byte[]?> ExecutableIconCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Путь к файлу иконки, если пользователь включил «Иконка из файла» и путь задан;
    /// иначе <c>null</c> (показывается иконка исполняемого файла либо стандартная).
    /// </summary>
    public static string? ResolveCustomIconPath(bool useCustomIcon, string? iconPath)
        => useCustomIcon && !string.IsNullOrWhiteSpace(iconPath) ? iconPath : null;

    /// <summary>То же для модели программы.</summary>
    public static string? ResolveCustomIconPath(CustomProgram? program)
        => program is null ? null : ResolveCustomIconPath(program.UseCustomIcon, program.IconPath);

    /// <summary>
    /// PNG-байты основной иконки исполняемого файла (Windows) либо <c>null</c>
    /// (Linux, отсутствующий/нечитаемый файл). Результат кэшируется по пути.
    /// </summary>
    public static byte[]? TryGetExecutableIcon(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        return ExecutableIconCache.GetOrAdd(filePath!, ExtractExecutableIcon);
    }

    /// <summary>Очищает кэш извлечённых иконок (тесты, замена файла по тому же пути).</summary>
    public static void ClearCache() => ExecutableIconCache.Clear();

    /// <summary>Извлечение иконки из файла средствами платформы (реализация по ОС).</summary>
    private static partial byte[]? ExtractExecutableIcon(string filePath);
}
