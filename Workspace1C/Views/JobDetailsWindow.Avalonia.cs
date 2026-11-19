#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Детали регламентного задания» (0.3.9.178, Linux/Avalonia): просмотр полного
    /// текста деталей задания (все поля из вывода rac «job list», включая расписание,
    /// результат и описание ошибки). Тонкая обёртка: принимает готовый текст
    /// (<see cref="ViewModels.RacJobRow.DetailsText"/>), вся логика — в ViewModel.
    /// </summary>
    public sealed class JobDetailsWindow : ModalWindowBase
    {
        public JobDetailsWindow(string detailsText)
        {
            Title = LocalizationManager.T("ServerMonitor.JobDetails.Title");
            Width = 680;
            Height = 520;
            MinWidth = 480;
            MinHeight = 320;
            FontSize = 13;
            CanResize = true;

            // Текст деталей: «ключ: значение» построчно, моноширинный шрифт.
            var text = new TextBlock
            {
                FontFamily = new FontFamily(ThemeManager.DefaultMonospaceFontFamily),
                FontSize = ThemeManager.FontSizeSecondary,
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                Text = detailsText ?? string.Empty
            };
            ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

            var closeButton = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), 14, "TextPrimaryBrush"),
                IsCancel = true,
                Padding = new Thickness(16, 6),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(closeButton, Button.BackgroundProperty, "ItemHoverBrush");
            ThemeBrushes.Bind(closeButton, Button.ForegroundProperty, "TextPrimaryBrush");
            closeButton.Click += (_, _) => Close();

            var border = new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(4),
                Child = new ScrollViewer
                {
                    Content = text,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            ThemeBrushes.Bind(border, Border.BackgroundProperty, "CardBackgroundColorBrush");

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(closeButton, 1);
            closeButton.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(border);
            root.Children.Add(closeButton);

            Content = root;
        }
    }
}
#endif