using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workspace1C.Converters;

/// <summary>
/// Путь к файлу иконки (<c>.ico</c>/<c>.png</c>) → <see cref="ImageSource"/> для WPF
/// (issue #91, группа «Мои программы»). Нечитаемый/отсутствующий файл возвращает
/// <c>null</c> — кнопка показывает стандартную иконку, построение панели не падает.
/// </summary>
public sealed class FileIconPathToImageSourceConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;
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

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
