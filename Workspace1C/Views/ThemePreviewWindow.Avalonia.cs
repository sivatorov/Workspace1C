#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно большого предпросмотра полноценной темы (issue #378, Avalonia/Linux):
    /// крупные кнопки управления окном, кнопки заголовков окон и диалогов, мини-галерея
    /// иконок набора и палитра темы. Открывается из галереи тем; «Применить» фиксирует
    /// выбор (CommitFullThemePreview), «Отмена»/ESC возвращает прежнюю тему без сохранения
    /// (RevertFullThemePreview). Переиспользует общие билдеры галереи
    /// (<see cref="SettingsWindow.BuildMiniWindowButton"/>, <see cref="SettingsWindow.BuildMiniIcon"/>,
    /// <see cref="SettingsWindow.HexBrush"/>, <see cref="ThemeFeatureMap.PreviewColors"/>).
    /// </summary>
    public class ThemePreviewWindow : ModalWindowBase
    {
        private readonly MainViewModel _viewModel;
        private readonly ThemeDefinition _theme;

        /// <summary>Создаёт окно предпросмотра темы и переводит её в предпросмотр (без сохранения).</summary>
        public ThemePreviewWindow(MainViewModel viewModel, ThemeDefinition theme)
        {
            _viewModel = viewModel;
            _theme = theme;
            Title = LocalizationManager.T("Settings.ThemePreview.Title");
            FontSize = 13;
            Width = 560;
            Height = 640;
            CanResize = false;

            // Применяем тему в предпросмотре (persist:false) — окно показывает её оформление.
            _viewModel.PreviewFullTheme(theme.Id);

            var root = new ScrollViewer
            {
                Content = BuildContent(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Content = root;
        }

        /// <summary>Собирает содержимое окна: кнопки окна, кнопки заголовков, иконки, палитру.</summary>
        private Control BuildContent()
        {
            var colors = ThemeFeatureMap.PreviewColors(_theme);
            var accent = SettingsWindow.HexBrush(colors.Accent);
            var text = SettingsWindow.HexBrush(colors.Text);
            var name = LocalizationManager.T(ThemeFeatureMap.ThemeNameKey(_theme.Id));

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 6 };

            // Заголовок окна предпросмотра.
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = ThemeManager.FontSizeTitle,
                FontWeight = FontWeight.SemiBold,
                Foreground = text,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Кнопки управления окном в стиле темы — крупно.
            panel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.WindowButtons")));
            var windowButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 10)
            };
            foreach (var glyph in _theme.ButtonStyle == WindowButtonStyle.MacOS
                         ? new[] { "GlyphWindowClose", "GlyphWindowMinimize", "GlyphWindowMaximize" }
                         : new[] { "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowClose" })
                windowButtons.Children.Add(BuildLargeWindowButton(glyph, colors));
            panel.Children.Add(windowButtons);

            // Кнопки заголовков окон и диалогов: акцентная и вторичная.
            panel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.TitleButtons")));
            var titleButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 10)
            };
            titleButtons.Children.Add(BuildTitleButton(LocalizationManager.T("Common.Ok"), accent));
            titleButtons.Children.Add(BuildTitleButton(LocalizationManager.T("Common.Cancel"), null));
            panel.Children.Add(titleButtons);

            // Мини-галерея иконок набора темы.
            panel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.IconSet")));
            var icons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var key in ThemeFeatureMap.PreviewIconKeys)
                icons.Children.Add(SettingsWindow.BuildMiniIcon(key, _theme.Icons, text));
            panel.Children.Add(icons);

            // Палитра темы: акцент, карточка, текст, границы.
            panel.Children.Add(GroupLabel(LocalizationManager.T("Settings.ThemePreview.Palette")));
            var swatches = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 10)
            };
            foreach (var hex in new[] { colors.Accent, colors.Card, colors.Text, colors.Border })
            {
                swatches.Children.Add(new Border
                {
                    Width = 56,
                    Height = 36,
                    CornerRadius = new CornerRadius(8),
                    Background = SettingsWindow.HexBrush(hex),
                    BorderBrush = SettingsWindow.HexBrush(colors.Border),
                    BorderThickness = new Thickness(1)
                });
            }
            panel.Children.Add(swatches);

            // Кнопки «Применить»/«Отмена».
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var apply = new Button
            {
                Content = LocalizationManager.T("Settings.ThemePreview.Apply"),
                MinWidth = 120,
                Padding = new Thickness(12, 6)
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
                Padding = new Thickness(12, 6),
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
            panel.Children.Add(buttons);

            return panel;
        }

        /// <summary>Подпись секции окна предпросмотра. Issue #61: вместо фиксированного
        /// серого — единая кисть FormLabelBrush (читаема в обеих темах).</summary>
        private static TextBlock GroupLabel(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Крупная кнопка управления окном в стиле темы (увеличенный BuildMiniWindowButton).</summary>
        private Control BuildLargeWindowButton(string glyphKey, ThemeFeatureMap.ThemePreviewColors colors)
        {
            var close = glyphKey == "GlyphWindowClose";
            if (_theme.ButtonStyle == WindowButtonStyle.MacOS)
            {
                var traffic = close ? "#FF5F57" : glyphKey == "GlyphWindowMinimize" ? "#FEBC2E" : "#28C840";
                return new Ellipse
                {
                    Width = 26,
                    Height = 26,
                    Fill = SettingsWindow.HexBrush(traffic)
                };
            }

            var metrics = ThemeFeatureMap.ButtonMetrics(_theme.ButtonStyle);
            IBrush background;
            IBrush glyphBrush = SettingsWindow.HexBrush(colors.Text);
            if (_theme.ButtonStyle == WindowButtonStyle.WindowsXp)
            {
                background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse(close ? "#E87A5D" : "#5A8EF0"), 0),
                        new GradientStop(Color.Parse(close ? "#C2361A" : "#1F55C9"), 1)
                    }
                };
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
                CornerRadius = new CornerRadius(radius),
                Background = background,
                BorderBrush = _theme.ButtonStyle == WindowButtonStyle.WindowsXp ? Brushes.White : null,
                BorderThickness = new Thickness(_theme.ButtonStyle == WindowButtonStyle.WindowsXp ? 1 : 0),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Width = 13,
                    Height = 13,
                    Stretch = Stretch.Uniform,
                    Stroke = glyphBrush,
                    StrokeThickness = 1.6,
                    StrokeLineCap = PenLineCap.Round,
                    Data = SettingsWindow.PreviewGeometry(glyphKey, _theme.Icons),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Кнопка заголовка окна/диалога: акцентная (основная) или вторичная.</summary>
        private static Button BuildTitleButton(string text, IBrush? accent)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 110,
                Height = 34,
                Padding = new Thickness(12, 6)
            };
            if (accent is not null)
                button.Background = accent;
            return button;
        }

        /// <summary>При закрытии окна (в т.ч. крестиком) отменяет предпросмотр без сохранения.</summary>
        protected override void OnClosed(EventArgs e)
        {
            // Если окно закрывается не через «Применить» (DialogResult не установлен),
            // возвращаем прежнюю тему без сохранения.
            if (DialogResult != true)
                _viewModel.RevertFullThemePreview();
            base.OnClosed(e);
        }
    }
}
#endif