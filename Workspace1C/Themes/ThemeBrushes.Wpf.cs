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
        /// Кисть из строковой спецификации: строка с префиксом «@» — кисть ресурса
        /// темы, иначе — HEX вида «#RRGGBB». Симметрично Avalonia-<c>ThemeBrushes.FromSpec</c>.
        /// </summary>
        /// <param name="spec">Спецификация цвета: "@SuccessBrush" или "#16A34A".</param>
        public static System.Windows.Media.Brush FromSpec(string spec)
            => (!string.IsNullOrWhiteSpace(spec) && spec[0] == '@' ? Get(spec[1..]) : null)
               ?? new System.Windows.Media.SolidColorBrush(
                   (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(spec));
    }
}
#endif
