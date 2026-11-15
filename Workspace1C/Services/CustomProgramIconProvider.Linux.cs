#if LINUX
namespace Workspace1C.Services;

/// <summary>
/// Извлечение основной иконки исполняемого файла на Linux (issue #91): разумный
/// fallback — <c>null</c>, то есть стандартная иконка группы. Иконка из файла
/// (<see cref="Models.CustomProgram.IconPath"/>) при включённой галочке работает
/// и здесь; извлечение иконки из ELF-файла не поддерживается, чтобы не тянуть
/// внешние утилиты и не падать на отсутствующих зависимостях.
/// </summary>
public static partial class CustomProgramIconProvider
{
    private static partial byte[]? ExtractExecutableIcon(string filePath) => null;
}
#endif
