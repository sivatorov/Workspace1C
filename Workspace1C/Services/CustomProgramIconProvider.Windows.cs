#if WINDOWS
using System.IO;
using Drawing = System.Drawing;

namespace Workspace1C.Services;

/// <summary>
/// Извлечение основной иконки исполняемого файла на Windows (issue #91): связанная
/// иконка файла, как в проводнике, через <c>Icon.ExtractAssociatedIcon</c>.
/// </summary>
public static partial class CustomProgramIconProvider
{
    private static partial byte[]? ExtractExecutableIcon(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            using var icon = Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon is null)
                return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            // Нечитаемый файл, нет прав, не PE-образ — стандартная иконка группы.
            return null;
        }
    }
}
#endif
