#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно большого предпросмотра полноценной темы (issue #378): крупные кнопки
    /// управления окном, кнопки заголовков окон и диалогов, мини-галерея иконок набора
    /// и палитра темы. Открывается из галереи тем; «Применить» фиксирует выбор
    /// (CommitFullThemePreview), «Отмена»/ESC возвращает прежнюю тему без сохранения
    /// (RevertFullThemePreview). Переиспользует общие билдеры галереи
    /// (<see cref="SettingsWindow.BuildMiniWindowButton"/>, <see cref="SettingsWindow.BuildMiniIcon"/>,
    /// <see cref="SettingsWindow.HexBrush"/>, <see cref="ThemeFeatureMap.PreviewColors"/>).
    /// </summary>
    public partial class ThemePreviewWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ThemeDefinition _theme;

        /// <summary>Создаёт окно предпросмотра темы и переводит её в предпросмотр (без сохранения).</summary>
        public ThemePreviewWindow(MainViewModel viewModel, ThemeDefinition theme)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _theme = theme;
            Title = LocalizationManager.T("Settings.ThemePreview.Title");
            // Применяем тему в предпросмотре (persist:false) — окно показывает её оформление.
            _viewModel.PreviewFullTheme(theme.Id);
            BuildContent();
        }

        /// <summary>Собирает содержимое окна: кнопки окна, кнопки заголовков, иконки, палитру.</summary>
        private void BuildContent()
        {
            var colors = ThemeFeatureMap.PreviewColors(_theme);
            var accent = SettingsWindow.HexBrush(colors.Accent);
            var text = SettingsWindow.HexBrush(colors.Text);
            var name = LocalizationManager.T(ThemeFeatureMap.ThemeNameKey(_theme.Id));

            // Заголовок окна предпросмотра.
            RootPanel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = Themes.ThemeManager.FontSizeTitle,
                FontWeight = FontWeights.SemiBold,
                Foreground = text,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Кнопки управления окном в стиле темы — крупно.
            RootPanel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.WindowButtons")));
            var windowButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            foreach (var glyph in _theme.ButtonStyle == WindowButtonStyle.MacOS
                         ? new[] { "GlyphWindowClose", "GlyphWindowMinimize", "GlyphWindowMaximize" }
                         : new[] { "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowClose" })
                windowButtons.Children.Add(BuildLargeWindowButton(glyph, colors));
            RootPanel.Children.Add(windowButtons);

            // Кнопки заголовков окон и диалогов: акцентная и вторичная.
            RootPanel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.TitleButtons")));
            var titleButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            titleButtons.Children.Add(BuildTitleButton(LocalizationManager.T("Common.Ok"), accent, text));
            titleButtons.Children.Add(BuildTitleButton(LocalizationManager.T("Common.Cancel"), null, text));
            RootPanel.Children.Add(titleButtons);

            // Мини-галерея иконок набора темы.
            RootPanel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.IconSet")));
            var icons = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            foreach (var key in ThemeFeatureMap.PreviewIconKeys)
                icons.Children.Add(SettingsWindow.BuildMiniIcon(key, _theme.Icons, text));
            RootPanel.Children.Add(icons);

            // Палитра темы: акцент, карточка, текст, границы.
            RootPanel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.Palette")));
            var swatches = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            foreach (var hex in new[] { colors.Accent, colors.Card, colors.Text, colors.Border })
            {
                swatches.Children.Add(new Border
                {
                    Width = 56,
                    Height = 36,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = SettingsWindow.HexBrush(hex),
                    BorderBrush = SettingsWindow.HexBrush(colors.Border),
                    BorderThickness = new Thickness(1)
                });
            }
            RootPanel.Children.Add(swatches);

            // Кнопки «Применить»/«Отмена».
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var apply = new Button
            {
                Content = LocalizationManager.T("Settings.ThemePreview.Apply"),
                MinWidth = 120,
                Style = (Style)FindResource("ModernButton"),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            apply.Click += (_, _) =>
            {
                _viewModel.CommitFullThemePreview();
                DialogResult = true;
                Close();
            };
            var cancel = new Button
            {
                Content = LocalizationManager.T("Settings.ThemePreview.Cancel"),
                MinWidth = 120,
                Style = (Style)FindResource("SecondaryButton"),
                IsCancel = true
            };
            cancel.Click += (_, _) =>
            {
                _viewModel.RevertFullThemePreview();
                DialogResult = false;
                Close();
            };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            RootPanel.Children.Add(buttons);
        }

        /// <summary>Подпись секции окна предпросмотра. Issue #61: вместо фиксированного
        /// Gray — кисть темы FormLabelBrush (читаема в обеих темах).</summary>
        private static TextBlock GroupLabel(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            if (Application.Current?.TryFindResource("FormLabelBrush") is Brush brush)
                block.Foreground = brush;
            return block;
        }

        /// <summary>Крупная кнопка управления окном в стиле темы (увеличенный BuildMiniWindowButton).</summary>
        private FrameworkElement BuildLargeWindowButton(string glyphKey, ThemeFeatureMap.ThemePreviewColors colors)
        {
            var close = glyphKey == "GlyphWindowClose";
            if (_theme.ButtonStyle == WindowButtonStyle.MacOS)
            {
                var traffic = close ? "#FF5F57" : glyphKey == "GlyphWindowMinimize" ? "#FEBC2E" : "#28C840";
                return new System.Windows.Shapes.Ellipse
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 10, 0),
                    Fill = SettingsWindow.HexBrush(traffic)
                };
            }

            var metrics = ThemeFeatureMap.ButtonMetrics(_theme.ButtonStyle);
            Brush background;
            Brush glyphBrush = SettingsWindow.HexBrush(colors.Text);
            if (_theme.ButtonStyle == WindowButtonStyle.WindowsXp)
            {
                var top = close ? "#E87A5D" : "#5A8EF0";
                var bottom = close ? "#C2361A" : "#1F55C9";
                var gradient = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(top),
                    (Color)ColorConverter.ConvertFromString(bottom), 90);
                gradient.Freeze();
                background = gradient;
                glyphBrush = Brushes.White;
            }
            else if (close)
            {
                background = SettingsWindow.HexBrush(metrics.CloseHoverHex);
                glyphBrush = Brushes.White;
            }
            else if (_theme.ButtonStyle is WindowButtonStyle.Material or WindowButtonStyle.Glass)
            {
                background = SettingsWindow.HexBrush(metrics.HoverHex);
            }
            else
            {
                background = Brushes.Transparent;
            }

            var size = _theme.ButtonStyle is WindowButtonStyle.Classic or WindowButtonStyle.WindowsXp ? 26.0 : 28.0;
            var radius = _theme.ButtonStyle switch
            {
                WindowButtonStyle.Classic => 0,
                WindowButtonStyle.WindowsXp => 5,
                WindowButtonStyle.Minimal => 5,
                _ => size / 2
            };
            return new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = new CornerRadius(radius),
                Background = background,
                BorderBrush = _theme.ButtonStyle == WindowButtonStyle.WindowsXp ? Brushes.White : null,
                BorderThickness = new Thickness(_theme.ButtonStyle == WindowButtonStyle.WindowsXp ? 1 : 0),
                Child = new System.Windows.Shapes.Path
                {
                    Width = 13,
                    Height = 13,
                    Stretch = Stretch.Uniform,
                    Stroke = glyphBrush,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = SettingsWindow.PreviewGeometry(glyphKey, _theme.Icons),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Кнопка заголовка окна/диалога: акцентная (основная) или вторичная.</summary>
        private static FrameworkElement BuildTitleButton(string text, Brush? accent, Brush textBrush)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 110,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Style = accent is not null
                    ? (Style)Application.Current.FindResource("ModernButton")
                    : (Style)Application.Current.FindResource("SecondaryButton")
            };
            return button;
        }

        /// <summary>При закрытии окна (в т.ч. крестиком) отменяет предпросмотр без сохранения.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Если окно закрывается не через «Применить» (DialogResult не установлен),
            // возвращаем прежнюю тему без сохранения.
            if (DialogResult != true)
                _viewModel.RevertFullThemePreview();
            base.OnClosing(e);
        }
    }
}
#endif