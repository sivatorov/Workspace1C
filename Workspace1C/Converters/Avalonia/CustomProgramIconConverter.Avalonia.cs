#if LINUX
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Workspace1C.Services;

namespace Workspace1C.Converters
{
    /// <summary>
    /// Иконка программы группы «Мои программы» (issue #91) для Avalonia: галочка
    /// «Иконка из файла» включена — картинка из <c>IconPath</c>; выключена — основная
    /// иконка исполняемого файла (на Linux не извлекается, поэтому <c>null</c>) либо
    /// стандартная иконка группы. Декодированные картинки кэшируются по пути, чтобы
    /// не читать файл на каждую перерисовку панели.
    /// </summary>
    public static class CustomProgramIconSource
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Картинка иконки программы либо <c>null</c> (показывается стандартная иконка
        /// группы). Ошибки чтения файла не выходят наружу.
        /// </summary>
        public static Bitmap? Load(bool useCustomIcon, string? iconPath, string? filePath)
        {
            if (useCustomIcon)
            {
                if (string.IsNullOrWhiteSpace(iconPath))
                    return null;
                return Cache.GetOrAdd("file:" + iconPath, _ => DecodeFile(iconPath!));
            }

            var bytes = CustomProgramIconProvider.TryGetExecutableIcon(filePath);
            if (bytes is null || string.IsNullOrWhiteSpace(filePath))
                return null;
            return Cache.GetOrAdd("exe:" + filePath, _ => DecodeBytes(bytes));
        }

        /// <summary>Картинка иконки для модели программы.</summary>
        public static Bitmap? Load(Models.CustomProgram? program)
            => program is null ? null : Load(program.UseCustomIcon, program.IconPath, program.FilePath);

        private static Bitmap? DecodeFile(string path)
        {
            try
            {
                return File.Exists(path) ? new Bitmap(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap? DecodeBytes(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Avalonia-версия WPF-конвертера иконки программы: MultiBinding
    /// (<c>UseCustomIcon</c>, <c>IconPath</c>, <c>FilePath</c>) → <see cref="Bitmap"/>.
    /// </summary>
    public sealed class CustomProgramIconConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is not { Count: >= 3 })
                return null;
            return CustomProgramIconSource.Load(
                values[0] is bool flag && flag, values[1] as string, values[2] as string);
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Avalonia-версия конвертера видимости: есть ли у программы иконка (своя или
    /// из исполняемого файла). Параметр <c>Invert</c> переворачивает результат для
    /// стандартной иконки группы.
    /// </summary>
    public sealed class CustomProgramIconVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var hasIcon = values is { Count: >= 3 } &&
                          CustomProgramIconSource.Load(
                              values[0] is bool flag && flag, values[1] as string, values[2] as string) is not null;
            var invert = parameter is string text &&
                         text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            return hasIcon ^ invert;
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif
