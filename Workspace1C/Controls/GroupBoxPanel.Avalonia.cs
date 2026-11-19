#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Рамка с заголовком: повторяет шаблон <c>GroupBox</c> из словарей WPF
    /// (Themes/LightTheme.xaml:928). Заголовок это отдельная полоса во всю ширину
    /// со скруглением 6, содержимое ниже в такой же рамке без верхней стороны.
    /// В Avalonia контрола GroupBox нет, поэтому здесь построитель.
    /// </summary>
    public static class GroupBoxPanel
    {
        /// <summary>
        /// Строит рамку с заголовком.
        /// </summary>
        /// <param name="headerKey">Ключ локализации заголовка.</param>
        /// <param name="content">Содержимое рамки.</param>
        /// <param name="margin">Внешний отступ рамки.</param>
        /// <param name="padding">Внутренний отступ содержимого; по умолчанию 8, как в стиле.</param>
        /// <param name="headerExtra">
        /// Довесок к подписи рамки, например кружок справки: в разметке автора
        /// заголовок GroupBox это панель из подписи и HelpLink рядом
        /// (LaunchParametersWindow.xaml:41-46).
        /// </param>
        public static Control Build(string headerKey, Control content,
            Thickness? margin = null, Thickness? padding = null, Control? headerExtra = null)
        {
            var grid = new Grid { Margin = margin ?? default };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            // Цвет подписи задан явно, как в шаблоне разметки
            // (TextElement.Foreground у ContentPresenter, LightTheme.xaml:946).
            var headerText = new TextBlock
            {
                Text = LocalizationManager.T(headerKey),
                FontWeight = FontWeight.SemiBold
            };
            Themes.ThemeBrushes.Bind(headerText, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            Control headerContent = headerText;
            if (headerExtra is not null)
            {
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                headerText.VerticalAlignment = VerticalAlignment.Center;
                headerPanel.Children.Add(headerText);
                headerPanel.Children.Add(headerExtra);
                headerContent = headerPanel;
            }

            var header = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4),
                Child = headerContent
            };
            Themes.ThemeBrushes.Bind(header, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(header, Border.BorderBrushProperty, "BorderBrushColor");
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var body = new Border
            {
                BorderThickness = new Thickness(1, 0, 1, 1),
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Padding = padding ?? new Thickness(8),
                Child = content
            };
            Themes.ThemeBrushes.Bind(body, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(body, Border.BorderBrushProperty, "BorderBrushColor");
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            return grid;
        }
    }
}
#endif
