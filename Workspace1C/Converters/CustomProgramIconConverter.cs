using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Workspace1C.Services;

namespace Workspace1C.Converters;

/// <summary>
/// Иконка программы группы «Мои программы» (issue #91) для WPF: MultiBinding
/// (<c>UseCustomIcon</c>, <c>IconPath</c>, <c>FilePath</c>) → <see cref="ImageSource"/>.
/// Галочка «Иконка из файла» включена — картинка из файла; выключена — основная иконка
/// исполняемого файла (извлекается один раз и кэшируется). Нет ни того, ни другого —
/// <c>null</c>, шаблон показывает стандартную иконку группы.
/// </summary>
public sealed class CustomProgramIconConverter : IMultiValueConverter
{
    /// <summary>Кэш декодированных картинок: «file:путь» / «exe:путь» → ImageSource.</summary>
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 3)
            return null;
        var useCustomIcon = values[0] is bool flag && flag;
        return Load(useCustomIcon, values[1] as string, values[2] as string);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Возвращает картинку иконки программы либо <c>null</c>. Путь к файлу иконки
    /// учитывается только при включённой галочке; иначе берётся основная иконка
    /// исполняемого файла (Windows) или <c>null</c> (Linux/ошибка чтения).
    /// </summary>
    internal static ImageSource? Load(bool useCustomIcon, string? iconPath, string? filePath)
    {
        if (useCustomIcon)
            return LoadFromFile(iconPath);

        var bytes = CustomProgramIconProvider.TryGetExecutableIcon(filePath);
        if (bytes is null || string.IsNullOrWhiteSpace(filePath))
            return null;
        return Cache.GetOrAdd("exe:" + filePath, _ => DecodeBytes(bytes));
    }

    private static ImageSource? LoadFromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return Cache.GetOrAdd("file:" + path, _ => DecodeFile(path!));
    }

    private static ImageSource? DecodeFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? DecodeBytes(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Видимость элемента иконки программы (issue #91) для WPF: MultiBinding
/// (<c>UseCustomIcon</c>, <c>IconPath</c>, <c>FilePath</c>) → <see cref="Visibility"/>.
/// Параметр <c>Invert</c> переворачивает результат — так стандартная иконка группы
/// показывается только тогда, когда иконка программы недоступна.
/// </summary>
public sealed class CustomProgramIconVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var hasIcon = values is { Length: >= 3 } &&
                      CustomProgramIconConverter.Load(
                          values[0] is bool flag && flag, values[1] as string, values[2] as string) is not null;
        var invert = parameter is string text &&
                     text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return hasIcon ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
