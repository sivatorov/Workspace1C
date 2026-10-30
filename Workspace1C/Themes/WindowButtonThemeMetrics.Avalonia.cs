#if LINUX
using Avalonia;
using Avalonia.Media;

namespace Workspace1C.Themes
{
    /// <summary>
    /// Текущие параметры кнопок управления окном (Задача 4): кисть подложки наведения,
    /// кисть кнопки «закрыть», радиус скругления подложки и размер кнопки. Значения
    /// кладёт <see cref="ThemeManager.ApplyWindowButtonStyle"/> в Application.Resources
    /// по ключам <see cref="ThemeFeatureMap"/>; здесь они читаются кодом, потому что
    /// шаблоны кнопок строятся в C# (без XAML) и подписаться на ресурсы числовых
    /// типов (радиус/размер) DynamicResource-привязкой нельзя.
    /// </summary>
    internal sealed class WindowButtonThemeMetrics
    {
        /// <summary>Высота кнопок окна в обычном масштабе (совпадает с высотой полосы заголовка).</summary>
        public const double ButtonHeight = 34;

        public IBrush Hover { get; private set; } = Brushes.Transparent;
        public IBrush CloseHover { get; private set; } = Brushes.Transparent;

        /// <summary>
        /// Кисть подложки наведения при НЕАКТИВНОМ окне (issue #26, комментарий 7OH
        /// от 2026-10-29 «подсветка кнопок управления основным окном при неактивности —
        /// белая»): шапка неактивного окна тёмная, поэтому в тёмной теме подложка —
        /// приглушённый тёмный slate, а не полупрозрачный белый (Hover).
        /// </summary>
        public IBrush InactiveHover { get; private set; } = Brushes.Transparent;

        /// <summary>Кисть подложки кнопки «свернуть» (Задача 3): у macOS-«светофора» жёлтый круг.</summary>
        public IBrush MinimizeHover { get; private set; } = Brushes.Transparent;

        /// <summary>Кисть подложки кнопки «развернуть» (Задача 3): у macOS-«светофора» зелёный круг.</summary>
        public IBrush MaximizeHover { get; private set; } = Brushes.Transparent;

        public double CornerRadius { get; private set; }
        public double Size { get; private set; }

        /// <summary>
        /// Читает метрики из ресурсов темы. Если ресурсы ещё не выставлены (до первого
        /// <see cref="ThemeManager.ApplyFullTheme"/>) или значение имеет неожиданный тип —
        /// используется fallback на метрики текущего стиля кнопок
        /// (<see cref="ThemeManager.CurrentWindowButtonStyle"/>).
        /// </summary>
        public static WindowButtonThemeMetrics Current()
        {
            var fallback = ThemeFeatureMap.ButtonMetrics(ThemeManager.CurrentWindowButtonStyle);
            // issue #26 (1.0.1.6): fallback-подложка НЕАКТИВНОГО окна обязана считаться
            // с фактическим вариантом темы. Ранее «тёмность» была захардкожена в false —
            // Avalonia-кнопки, читающие метрики кодом, всегда получали светлую «дневную»
            // подложку, пока ресурс WindowButtonInactiveHoverBrushKey не выставлен.
            var dark = ThemeManager.CurrentTheme == ThemeManager.DarkThemeName;
            var result = new WindowButtonThemeMetrics
            {
                Hover = Parse(fallback.HoverHex),
                CloseHover = Parse(fallback.CloseHoverHex),
                InactiveHover = Parse(ThemeFeatureMap.InactiveHoverHex(
                    ThemeManager.CurrentWindowButtonStyle, dark)),
                MinimizeHover = Parse(fallback.MinimizeHoverHex ?? fallback.HoverHex),
                MaximizeHover = Parse(fallback.MaximizeHoverHex ?? fallback.HoverHex),
                CornerRadius = fallback.CornerRadius,
                Size = fallback.Size
            };

            if (Avalonia.Application.Current?.Resources is not { } resources)
                return result;

            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonHoverBrushKey, null, out var hover)
                && hover is IBrush hoverBrush)
                result.Hover = hoverBrush;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonCloseHoverBrushKey, null, out var closeHover)
                && closeHover is IBrush closeHoverBrush)
                result.CloseHover = closeHoverBrush;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonInactiveHoverBrushKey, null, out var inactiveHover)
                && inactiveHover is IBrush inactiveHoverBrush)
                result.InactiveHover = inactiveHoverBrush;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonMinimizeHoverBrushKey, null, out var minHover)
                && minHover is IBrush minHoverBrush)
                result.MinimizeHover = minHoverBrush;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonMaximizeHoverBrushKey, null, out var maxHover)
                && maxHover is IBrush maxHoverBrush)
                result.MaximizeHover = maxHoverBrush;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonCornerRadiusKey, null, out var radius)
                && radius is double cornerRadius)
                result.CornerRadius = cornerRadius;
            if (resources.TryGetResource(ThemeFeatureMap.WindowButtonSizeKey, null, out var size)
                && size is double buttonSize)
                result.Size = buttonSize;

            return result;
        }

        /// <summary>Создаёт кисть из шестнадцатеричного цвета (#RRGGBB), прозрачную при ошибке.</summary>
        private static IBrush Parse(string hex)
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Brushes.Transparent; }
        }
    }
}
#endif