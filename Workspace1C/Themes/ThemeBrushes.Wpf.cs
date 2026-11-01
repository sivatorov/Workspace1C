#if !LINUX
using System.Windows;

namespace Workspace1C.Themes
{
    /// <summary>
    /// WPF-двойник <see cref="ThemeBrushes"/> (Avalonia): чтение кистей темы по ключу.
    /// Ресурсы кладёт ThemeManager.ApplyScheme в Application.Resources (v100112,
    /// тематизация остатков хардкод-цветов в коде окон).
    /// </summary>
    public static class WpfBrushes
    {
        /// <summary>Кисть темы по ключу; при отсутствии — null.</summary>
        public static System.Windows.Media.Brush? Get(string brushKey)
            => Application.Current?.TryFindResource(brushKey) as System.Windows.Media.Brush;

        /// <summary>
        /// Кэш сплошных кистей по HEX-спецификации (v100120, оптимизация дерева):
        /// переиспользование кисти вместо повторного <c>ColorConverter.ConvertFromString</c>.
        /// HEX-литералы от темы не зависят, поэтому кисть можно переиспользовать;
        /// кисти заморожены (<see cref="System.Windows.Freezable.Freeze"/>) — безопасно
        /// из любого потока и дешевле в рендере.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.Brush> SpecBrushCache = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Кисть из строковой спецификации: строка с префиксом «@» — кисть ресурса
        /// темы, иначе — HEX вида «#RRGGBB». Симметрично Avalonia-<c>ThemeBrushes.FromSpec</c>.
        /// </summary>
        /// <param name="spec">Спецификация цвета: "@SuccessBrush" или "#16A34A".</param>
        public static System.Windows.Media.Brush FromSpec(string spec)
        {
            // Issue #89: «@»-спецификация — ключ кисти, а не цвет; при отсутствии
            // ресурса она не должна уходить в ColorConverter (FormatException),
            // как это чинится в Avalonia-двойнике — вместо краха прозрачная кисть.
            if (!string.IsNullOrWhiteSpace(spec) && spec[0] == '@')
                return Get(spec[1..]) ?? System.Windows.Media.Brushes.Transparent;
            return SpecBrushCache.GetOrAdd(spec ?? string.Empty, static s =>
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s));
                if (brush.CanFreeze)
                    brush.Freeze();
                return brush;
            });
        }
    }
}
#endif
