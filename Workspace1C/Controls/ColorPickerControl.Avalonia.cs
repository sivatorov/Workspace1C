#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Переиспользуемый контрол выбора цвета (Avalonia/Linux) в HSV-модели:
    /// большая градиентная область «полной палитры» (оттенок × насыщенность)
    /// с перетаскиванием маркера, бегунок яркости, RGB-слайдеры, HEX-поле
    /// и расширенная палитра предустановленных цветов.
    /// Выбранный цвет доступен через свойство <see cref="SelectedColor"/>.
    /// Используется в <see cref="ColorPickerWindow"/> и вкладках окна настройки группы.
    /// </summary>
    public class ColorPickerControl : UserControl
    {
        // Расширенная палитра предустановленных цветов (73 шт., компактные плитки).
        private static readonly string[] PaletteColors =
        {
            "#000000", "#212121", "#3F3F3F", "#5C5C5C", "#7F7F7F", "#9E9E9E", "#BFBFBF", "#FFFFFF",
            "#7F1D1D", "#991B1B", "#DC2626", "#EF4444", "#F87171", "#FCA5A5", "#FFC2C2",
            "#7C2D12", "#C2410C", "#EA580C", "#F97316", "#FB923C", "#FDBA74", "#FED7AA",
            "#713F12", "#A16207", "#D97706", "#F59E0B", "#FBBF24", "#FDE047",
            "#14532D", "#166534", "#15803D", "#16A34A", "#22C55E", "#4ADE80", "#86EFAC", "#BBF7D0",
            "#134E4A", "#115E59", "#0F766E", "#0D9488", "#14B8A6", "#2DD4BF", "#5EEAD4",
            "#164E63", "#0E7490", "#0891B2", "#06B6D4", "#22D3EE", "#67E8F9",
            "#172554", "#1E3A8A", "#1D4ED8", "#2563EB", "#3B82F6", "#60A5FA", "#93C5FD",
            "#312E81", "#3730A3", "#4338CA", "#4F46E5", "#6366F1", "#A5B4FC",
            "#581C87", "#7E22CE", "#9333EA", "#A855F7", "#C084FC", "#D8B4FE",
            "#831843", "#BE185D", "#DB2777", "#EC4899", "#F472B6", "#F9A8D4"
        };

        // Высота градиентной области «полной палитры».
        private const double AreaHeight = 100;

        private bool _isUpdating;

        private IPointer? _capturedPointer;

        private double _hue;
        private double _saturation = 100;
        private double _brightness = 100;

        // Строка ползунка как в разметке WPF, где своей высоты у ползунка нет
        // и строка выходит около 22 пикселей.
        private const double SliderHeight = 22;
        private const double SliderThumbSize = 14;

        private readonly Slider _redSlider = new() { Minimum = 0, Maximum = 255 };
        private readonly Slider _greenSlider = new() { Minimum = 0, Maximum = 255 };
        private readonly Slider _blueSlider = new() { Minimum = 0, Maximum = 255 };
        private readonly Slider _brightnessSlider = new() { Minimum = 0, Maximum = 100 };

        private readonly TextBlock _redValue = new() { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _greenValue = new() { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _blueValue = new() { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _brightnessValue = new() { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        private readonly TextBox _hexBox;
        private readonly Border _colorPreview;

        // Элементы градиентной области палитры.
        // Слои палитры лежат в сетке и растягиваются на всю область, а канва
        // нужна только под маркер: у детей канвы собственный размер, и пустой
        // Border в ней вышел бы нулевым. Так же устроено в разметке WPF
        // (Controls/ColorPickerControl.xaml:51).
        private readonly Grid _paletteArea = new() { ClipToBounds = true };
        private readonly Canvas _paletteCanvas = new();
        private readonly Border _brightnessOverlay;
        private readonly Border _marker = new()
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.White
        };

        /// <summary>
        /// Выбранный цвет в формате #RRGGBB. Обновляется при любом изменении пикера.
        /// </summary>
        public static readonly StyledProperty<string> SelectedColorProperty =
            AvaloniaProperty.Register<ColorPickerControl, string>(nameof(SelectedColor), "#2D6CDF");

        public string SelectedColor
        {
            get => GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        /// <summary>
        /// Создаёт элемент выбора цвета.
        /// </summary>
        public ColorPickerControl()
        {
            // Размеры строки ползунка шаблон Fluent берёт из своих ресурсов:
            // поля 15 сверху и снизу и бегунок 20 на 20, строка занимает около
            // 50. Ключ SliderHorizontalHeight задаёт минимум сетки шаблона (32),
            // а не всю высоту. В разметке строка занимает 22, и высота окна
            // (ColorPickerWindow.xaml:11) рассчитана на такие строки, поэтому
            // размеры задаются ресурсами шаблона. Высота, заданная самому
            // ползунку, обрезает бегунок по вертикали.
            Resources["SliderHorizontalHeight"] = SliderHeight;
            Resources["SliderPreContentMargin"] = new GridLength((SliderHeight - SliderThumbSize) / 2);
            Resources["SliderPostContentMargin"] = new GridLength((SliderHeight - SliderThumbSize) / 2);
            Resources["SliderHorizontalThumbWidth"] = SliderThumbSize;
            Resources["SliderHorizontalThumbHeight"] = SliderThumbSize;
            Resources["SliderThumbCornerRadius"] = new CornerRadius(SliderThumbSize / 2);

            _hexBox = new TextBox { Width = 110, Padding = new Thickness(4, 3) };
            // Текст HEX следует за темой (issue #26): в тёмной теме без явной кисти
            // он оставался чёрным и сливался с тёмным полем ввода.
            ThemeBrushes.Bind(_hexBox, TemplatedControl.ForegroundProperty, "TextPrimaryColorBrush");
            _hexBox.TextChanged += OnHex_TextChanged;

            _colorPreview = new Border
            {
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1)
            };

            _redSlider.ValueChanged += OnRgb_ValueChanged;
            _greenSlider.ValueChanged += OnRgb_ValueChanged;
            _blueSlider.ValueChanged += OnRgb_ValueChanged;
            _brightnessSlider.ValueChanged += OnBrightness_ValueChanged;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 0: предпросмотр
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 1: подпись палитры
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 2: градиентная палитра
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 3: яркость
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 4: подпись предустановленных
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 5: предустановленные
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 6: R
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 7: G
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 8: B
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 9: HEX

            // Предпросмотр
            root.Children.Add(_colorPreview);

            // Полная палитра (оттенок × насыщенность)
            var paletteLabel = BuildSectionLabel(LocalizationManager.T("ColorPicker.FullPalette"), 6);
            Grid.SetRow(paletteLabel, 1);
            root.Children.Add(paletteLabel);

            _paletteArea.Cursor = new Cursor(StandardCursorType.Hand);
            _paletteArea.Height = AreaHeight;
            _paletteArea.Margin = new Thickness(0, 0, 0, 6);
            // Панель без фона в Avalonia не участвует в проверке попадания,
            // а все слои помечены непопадаемыми, поэтому щелчки по палитре
            // не доходили бы ни до кого.
            _paletteArea.Background = Brushes.Transparent;

            var rainbowBorder = new Border { IsHitTestVisible = false };
            rainbowBorder.Background = BuildRainbowBrush();
            _paletteArea.Children.Add(rainbowBorder);

            var satOverlay = new Border { IsHitTestVisible = false };
            satOverlay.Background = BuildSaturationOverlay();
            _paletteArea.Children.Add(satOverlay);

            _brightnessOverlay = new Border { IsHitTestVisible = false, Background = Brushes.Black };
            _paletteArea.Children.Add(_brightnessOverlay);

            _marker.IsHitTestVisible = false;
            _marker.BoxShadow = new BoxShadows(new BoxShadow { Blur = 2, OffsetY = 1, Color = new Color(160, 0, 0, 0) });
            _paletteCanvas.Children.Add(_marker);
            _paletteArea.Children.Add(_paletteCanvas);

            _paletteArea.PointerPressed += OnPalette_PointerPressed;
            _paletteArea.PointerMoved += OnPalette_PointerMoved;
            _paletteArea.PointerReleased += OnPalette_PointerReleased;
            _paletteArea.SizeChanged += (_, _) => UpdateMarker();

            Grid.SetRow(_paletteArea, 2);
            root.Children.Add(_paletteArea);

            // Яркость
            var brightnessRow = BuildBrightnessRow();
            Grid.SetRow(brightnessRow, 3);
            root.Children.Add(brightnessRow);

            // Предустановленные цвета
            var presetsLabel = BuildSectionLabel(LocalizationManager.T("ColorPicker.Palette"), 4);
            Grid.SetRow(presetsLabel, 4);
            root.Children.Add(presetsLabel);

            var paletteGrid = new UniformGrid { Columns = 16, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var hex in PaletteColors)
            {
                var button = new Button
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(1),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(ParseColor(hex))
                };
                button.Click += (_, _) => SetColor(ParseColor(hex));
                paletteGrid.Children.Add(button);
            }
            Grid.SetRow(paletteGrid, 5);
            root.Children.Add(paletteGrid);

            // RGB слайдеры
            var red = BuildRgbRow(6, LocalizationManager.T("ColorPicker.ChannelRed"), "#EF4444", _redSlider, _redValue);
            root.Children.Add(red);
            var green = BuildRgbRow(7, LocalizationManager.T("ColorPicker.ChannelGreen"), "#10B981", _greenSlider, _greenValue);
            root.Children.Add(green);
            var blue = BuildRgbRow(8, LocalizationManager.T("ColorPicker.ChannelBlue"), "#2D6CDF", _blueSlider, _blueValue);
            root.Children.Add(blue);

            // HEX
            var hexRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                // Поле сверху как в разметке (ColorPickerControl.xaml:120).
                Margin = new Thickness(0, 4, 0, 0),
                Spacing = 8
            };
            var hexLabel = new TextBlock
            {
                Text = LocalizationManager.T("ColorPicker.HexLabel"),
                VerticalAlignment = VerticalAlignment.Center
            };
            hexRow.Children.Add(hexLabel);
            hexRow.Children.Add(_hexBox);
            Grid.SetRow(hexRow, 9);
            root.Children.Add(hexRow);

            Content = root;

            SetColor(ParseColor(SelectedColor));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SelectedColorProperty && !_isUpdating)
                SetColor(ParseColor(change.GetNewValue<string>()));
        }

        // Единое RGB-HSV преобразование модели (issue #26): браузер и редактор
        // используют одинаковый расчёт, поэтому показанный в настройках HEX-цвет
        // совпадает с тем, что открывается в диалоге выбора цвета.
        private Color CurrentColor
        {
            get
            {
                var (r, g, b) = Models.ColorScheme.HsvToRgb(_hue, _saturation / 100.0, _brightness / 100.0);
                return Color.FromRgb(r, g, b);
            }
        }

        /// <summary>
        /// Подпись секции: второстепенный цвет и поле снизу как в разметке
        /// (ColorPickerControl.xaml:49-50 и 80-81), где у первой подписи 6, а
        /// у второй 4.
        /// </summary>
        private static TextBlock BuildSectionLabel(string text, double bottomMargin)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };
            ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            return label;
        }

        private Grid BuildBrightnessRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(70)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(38)));

            var label = new TextBlock
            {
                Text = LocalizationManager.T("ColorPicker.Brightness"),
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(label);

            Grid.SetColumn(_brightnessSlider, 1);
            _brightnessSlider.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(_brightnessSlider);

            Grid.SetColumn(_brightnessValue, 2);
            ThemeBrushes.Bind(_brightnessValue, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            grid.Children.Add(_brightnessValue);

            return grid;
        }

        private Grid BuildRgbRow(int row, string label, string accent, Slider slider, TextBlock value)
        {
            // Поле строки как в разметке (ColorPickerControl.xaml:85, 96, 107).
            var grid = new Grid { Margin = new Thickness(0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(20)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(ParseColor(accent)),
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(labelBlock);

            Grid.SetColumn(slider, 1);
            slider.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(slider);

            Grid.SetColumn(value, 2);
            ThemeBrushes.Bind(value, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            grid.Children.Add(value);

            Grid.SetRow(grid, row);
            return grid;
        }

        private static LinearGradientBrush BuildRainbowBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
            };
            for (var i = 0; i <= 6; i++)
            {
                var (r, g, b) = Models.ColorScheme.HsvToRgb(i * 60.0, 1.0, 1.0);
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 6.0));
            }
            return brush;
        }

        // Вертикальный градиент «белый снизу → прозрачный сверху» задаёт насыщенность.
        private static LinearGradientBrush BuildSaturationOverlay()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1));
            return brush;
        }

        private void SetColor(Color color)
        {
            // Единое RGB-HSV преобразование модели (issue #26): собственный обход
            // через HsvColor с усечением каналов «съедал» у выбранного в настройках
            // цвета до 1 на канал (например #13522C -> #12512B).
            var (h, s, v) = Models.ColorScheme.RgbToHsv(color.R, color.G, color.B);
            _hue = h;
            _saturation = s * 100.0;
            _brightness = v * 100.0;

            _isUpdating = true;
            try
            {
                _brightnessSlider.Value = _brightness;
            }
            finally
            {
                _isUpdating = false;
            }

            ApplyFromHsv();
        }

        private void ApplyFromHsv()
        {
            _isUpdating = true;
            try
            {
                var color = CurrentColor;

                _redSlider.Value = color.R;
                _greenSlider.Value = color.G;
                _blueSlider.Value = color.B;
                _redValue.Text = color.R.ToString();
                _greenValue.Text = color.G.ToString();
                _blueValue.Text = color.B.ToString();
                _brightnessValue.Text = ((int)Math.Round(_brightness)).ToString() + "%";
                _hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                _colorPreview.Background = new SolidColorBrush(color);

                // Затемнение всей палитры при снижении яркости.
                _brightnessOverlay.Opacity = 1.0 - _brightness / 100.0;

                // Публикуем текущий цвет (изменение внутри _isUpdating не вызовет повторного SetColor).
                SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            finally
            {
                _isUpdating = false;
            }

            UpdateMarker();
        }

        private void UpdateMarker()
        {
            var w = _paletteArea.Bounds.Width;
            var h = _paletteArea.Bounds.Height;
            if (w <= 0 || h <= 0)
                return;

            Canvas.SetLeft(_marker, _hue / 360.0 * w - _marker.Width / 2);
            Canvas.SetTop(_marker, (1.0 - _saturation / 100.0) * h - _marker.Height / 2);
        }

        private void OnPalette_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(_paletteArea);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            ApplyPointer(_paletteArea, point.Position);
            _capturedPointer = e.Pointer;
            e.Pointer.Capture(_paletteArea);
        }

        private void OnPalette_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_capturedPointer is not null)
            {
                if (ReferenceEquals(_capturedPointer.Captured, _paletteArea))
                    _capturedPointer.Capture(null);
                _capturedPointer = null;
            }
        }

        private void OnPalette_PointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(_paletteArea);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            ApplyPointer(_paletteArea, point.Position);
        }

        private void ApplyPointer(Control canvas, Point position)
        {
            var w = canvas.Bounds.Width;
            var h = canvas.Bounds.Height;
            if (w <= 0 || h <= 0)
                return;

            _hue = Math.Clamp(position.X / w * 360.0, 0, 359.99);
            _saturation = Math.Clamp((1.0 - position.Y / h) * 100.0, 0, 100);
            ApplyFromHsv();
        }

        private void OnBrightness_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            _brightness = _brightnessSlider.Value;
            ApplyFromHsv();
        }

        private void OnRgb_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            var color = Color.FromRgb(
                (byte)_redSlider.Value,
                (byte)_greenSlider.Value,
                (byte)_blueSlider.Value);
            SetColor(color);
        }

        private void OnHex_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            var text = _hexBox.Text?.Trim() ?? string.Empty;
            if (text.Length != 7 || !text.StartsWith("#"))
                return;

            try
            {
                SetColor(Color.Parse(text));
            }
            catch
            {
                // Игнорируем некорректный ввод HEX.
            }
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                return Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#2D6CDF" : hex);
            }
            catch
            {
                return Color.Parse("#2D6CDF");
            }
        }
    }
}
#endif