#if WINDOWS
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        // ===================== Цветовое оформление =====================

        /// <summary>
        /// Инициализирует вкладку «Цветовое оформление»: активная схема загружается
        /// моделью представления (<see cref="SettingsViewModel"/>), здесь заполняется
        /// список тем и редактор цветов, а также режим редактируемой палитры.
        /// </summary>
        private void InitializeColorSchemes()
        {
            ColorItemsControl.ItemsSource = _colorItems;
            RefreshSchemeComboBox();
            // Начальный режим палитры — по текущему варианту темы, ДО построения строк
            // редактора (issue #26): RefreshColorItems читает GetEditableColors с флагом
            // EditingDarkPalette, и раньше строки строились по светлой палитре по умолчанию
            // (дефолтные #FDBF00/#E0A800) даже при активной тёмной теме с сохранёнными
            // правками — при повторном входе прямоугольники и диалог выбора цвета показывали
            // жёлтый вместо сохранённого акцента.
            var dark = Themes.ThemeManager.CurrentTheme == Themes.ThemeManager.DarkThemeName;
            _settings.SetPaletteMode(dark);
            UpdatePaletteButton();
            RefreshColorItems();
            // Режим «предпросмотр → Применить/Отмена» (issue #378): запоминаем тему
            // до входа в галерею, чтобы «Отмена» вернула её без сохранения.
            _viewModel.BeginThemePreview();
            InitializeFullThemes();
            RefreshSchemePreview();
        }

        // ---- Полноценные темы: галерея и тонкая настройка (0.3.12.62) ----

        /// <summary>Признак «идёт программная установка значений переопределений темы».</summary>
        private bool _suppressThemeOverrideEvent;

        /// <summary>Элемент списка переопределения: идентификатор (пусто — «по теме») и имя.</summary>
        private sealed class ThemeOverrideItem
        {
            public string Id { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public override string ToString() => DisplayName;
        }

        /// <summary>
        /// Заполняет блок «Темы»: галерею карточек встроенных тем и списки переопределения
        /// набора иконок и стиля кнопок окна.
        /// </summary>
        private void InitializeFullThemes()
        {
            BuildThemeGallery();
            InitializeThemeOverrides();
        }

        /// <summary>
        /// Строит галерею тем: карточка на каждую встроенную тему с палитрой, кнопками окна
        /// в стиле темы и иконками её набора; активная тема выделена рамкой акцентного цвета.
        /// </summary>
        private void BuildThemeGallery()
        {
            if (ThemeGalleryPanel is null || _settings is null)
                return;
            ThemeGalleryPanel.Children.Clear();
            var currentId = _settings.CurrentFullThemeId;
            foreach (var theme in SettingsViewModel.FullThemes)
            {
                var selected = string.Equals(theme.Id, currentId, StringComparison.OrdinalIgnoreCase);
                ThemeGalleryPanel.Children.Add(BuildThemeCard(theme, selected));
            }
        }

        internal static Brush HexBrush(string hex)
        {
            try
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        /// <summary>
        /// Геометрия иконки или глифа в заданном наборе — для предпросмотра чужой темы.
        /// Базовые ключи приложения переопределены активным набором, поэтому вариант
        /// Fluent берётся прямо из словаря Themes/Icons.xaml.
        /// </summary>
        internal static Geometry? PreviewGeometry(string key, IconSet set, bool back = false)
        {
            var app = Application.Current;
            if (app is null)
                return null;
            if (set != IconSet.Fluent)
            {
                var setKey = ThemeFeatureMap.IconKeyForSet(back ? ThemeFeatureMap.BackLayerKey(key) : key, set);
                if (app.TryFindResource(setKey) is Geometry g)
                    return g;
                if (back)
                    return null;
            }
            else if (back)
            {
                return null;
            }
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                var source = dict.Source?.OriginalString ?? string.Empty;
                if (source.EndsWith("Themes/Icons.xaml", StringComparison.OrdinalIgnoreCase) &&
                    dict.Contains(key) && dict[key] is Geometry baseGeometry)
                    return baseGeometry;
            }
            return null;
        }

        /// <summary>Карточка темы в галерее.</summary>
        private Button BuildThemeCard(ThemeDefinition theme, bool selected)
        {
            var colors = ThemeFeatureMap.PreviewColors(theme);
            var accent = HexBrush(colors.Accent);
            var text = HexBrush(colors.Text);
            var name = LocalizationManager.T(ThemeFeatureMap.ThemeNameKey(theme.Id));

            // Полоса заголовка «окна» темы с кнопками управления в её стиле: у macOS
            // «светофор» слева, у остальных — кнопки справа.
            var windowButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = theme.ButtonStyle == WindowButtonStyle.MacOS
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Right
            };
            foreach (var glyph in theme.ButtonStyle == WindowButtonStyle.MacOS
                         ? new[] { "GlyphWindowClose", "GlyphWindowMinimize", "GlyphWindowMaximize" }
                         : new[] { "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowClose" })
                windowButtons.Children.Add(BuildMiniWindowButton(theme, glyph, colors));
            var titleBar = new Border
            {
                Background = HexBrush(colors.Card),
                BorderBrush = HexBrush(colors.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Child = windowButtons
            };

            // Палитра: акцент, карточка, текст.
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
            foreach (var hex in new[] { colors.Accent, colors.Card, colors.Text })
            {
                swatches.Children.Add(new Border
                {
                    Width = 22,
                    Height = 10,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = HexBrush(hex),
                    BorderBrush = HexBrush(colors.Border),
                    BorderThickness = new Thickness(1)
                });
            }

            // Иконки набора темы.
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var key in ThemeFeatureMap.PreviewIconKeys)
                icons.Children.Add(BuildMiniIcon(key, theme.Icons, text));

            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            if (selected)
            {
                caption.Children.Add(new PackIcon
                {
                    Kind = PackIconKind.Check,
                    Width = 14,
                    Height = 14,
                    Foreground = accent,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            caption.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = text,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var body = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
            body.Children.Add(swatches);
            body.Children.Add(icons);
            body.Children.Add(caption);
            var content = new StackPanel();
            content.Children.Add(titleBar);
            content.Children.Add(body);

            var card = new Border
            {
                Width = 168,
                CornerRadius = new CornerRadius(8),
                Background = HexBrush(colors.Background),
                BorderBrush = selected ? accent : HexBrush(colors.Border),
                BorderThickness = new Thickness(selected ? 2.5 : 1),
                Child = content,
                SnapsToDevicePixels = true
            };

            var template = new ControlTemplate(typeof(Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
            };
            var button = new Button
            {
                Content = card,
                Template = template,
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Tag = theme.Id,
                FocusVisualStyle = null
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) => SelectFullTheme(theme.Id);
            return button;
        }

        /// <summary>Мини-кнопка управления окном в стиле темы (для карточки галереи).</summary>
        internal static FrameworkElement BuildMiniWindowButton(ThemeDefinition theme, string glyphKey,
            ThemeFeatureMap.ThemePreviewColors colors)
        {
            var close = glyphKey == "GlyphWindowClose";
            if (theme.ButtonStyle == WindowButtonStyle.MacOS)
            {
                var traffic = close ? "#FF5F57" : glyphKey == "GlyphWindowMinimize" ? "#FEBC2E" : "#28C840";
                return new System.Windows.Shapes.Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Margin = new Thickness(0, 0, 5, 0),
                    Fill = HexBrush(traffic)
                };
            }

            var metrics = ThemeFeatureMap.ButtonMetrics(theme.ButtonStyle);
            Brush background;
            Brush glyphBrush = HexBrush(colors.Text);
            if (theme.ButtonStyle == WindowButtonStyle.WindowsXp)
            {
                // Классические кнопки XP: синие объёмные, «закрыть» — красная.
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
                background = HexBrush(metrics.CloseHoverHex);
                glyphBrush = Brushes.White;
            }
            else if (theme.ButtonStyle is WindowButtonStyle.Material or WindowButtonStyle.Glass)
            {
                background = HexBrush(metrics.HoverHex);
            }
            else
            {
                background = Brushes.Transparent;
            }

            var size = theme.ButtonStyle is WindowButtonStyle.Classic or WindowButtonStyle.WindowsXp ? 14.0 : 15.0;
            var radius = theme.ButtonStyle switch
            {
                WindowButtonStyle.Classic => 0,
                WindowButtonStyle.WindowsXp => 3,
                WindowButtonStyle.Minimal => 3,
                _ => size / 2
            };
            return new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(radius),
                Background = background,
                BorderBrush = theme.ButtonStyle == WindowButtonStyle.WindowsXp ? Brushes.White : null,
                BorderThickness = new Thickness(theme.ButtonStyle == WindowButtonStyle.WindowsXp ? 1 : 0),
                Child = new System.Windows.Shapes.Path
                {
                    Width = 7,
                    Height = 7,
                    Stretch = Stretch.Uniform,
                    Stroke = glyphBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = PreviewGeometry(glyphKey, theme.Icons),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Мини-иконка набора темы: фоновый слой (Glass), основной и «блик» (Windows XP).</summary>
        internal static FrameworkElement BuildMiniIcon(string key, IconSet set, Brush fill)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            var back = PreviewGeometry(key, set, back: true);
            if (back is not null)
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = back,
                    Fill = fill,
                    Opacity = ThemeFeatureMap.BackLayerOpacity
                });
            var front = PreviewGeometry(key, set);
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = front, Fill = fill });
            if (ThemeFeatureMap.IconGloss(set) is { } gloss && front is not null)
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(gloss.Top), 0));
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(gloss.Bottom), gloss.Stop));
                brush.Freeze();
                canvas.Children.Add(new System.Windows.Shapes.Path { Data = front, Fill = brush });
            }
            return new Viewbox
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                Child = canvas
            };
        }

        /// <summary>
        /// Переводит выбранную в галерее тему в предпросмотр (issue #378): применяет её
        /// без сохранения в настройках и обновляет редактор палитры, предпросмотр и галерею.
        /// Сохранение — только по кнопке «Применить»/«ОК» окна настроек.
        /// </summary>
        private void SelectFullTheme(string id)
        {
            if (_settings is null)
                return;

            // Применяем полноценную тему БЕЗ записи в AppSettings (persist:false).
            _viewModel.PreviewFullTheme(id);

            // Редактор палитры и предпросмотр показывают цвета выбранной темы;
            // режим палитры — по базовому варианту темы (светлый/тёмный).
            var theme = ThemeManager.CurrentFullTheme;
            if (theme is not null)
                _settings.AdoptFullThemeScheme(theme);
            _settings.SetPaletteMode(ThemeManager.CurrentTheme == ThemeManager.DarkThemeName);
            RefreshSchemeComboBox();
            RefreshColorItems();
            UpdatePaletteButton();
            RefreshSchemePreview();
            BuildThemeGallery();
            InitializeThemeOverrides();
            UpdateThemePreviewButtons();
        }

        /// <summary>
        /// Обновляет доступность кнопок «Применить»/«Отмена» под галереей тем: они активны,
        /// пока есть тема в предпросмотре (не закоммичена и не отменена).
        /// </summary>
        private void UpdateThemePreviewButtons()
        {
            if (PreviewThemeButton is not null)
                PreviewThemeButton.IsEnabled = _viewModel.HasThemePreview;
            if (ApplyThemePreviewButton is not null)
                ApplyThemePreviewButton.IsEnabled = _viewModel.HasThemePreview;
            if (CancelThemePreviewButton is not null)
                CancelThemePreviewButton.IsEnabled = _viewModel.HasThemePreview;
        }

        /// <summary>Кнопка «Предпросмотр»: открывает большое окно предпросмотра темы (issue #378).</summary>
        private void OnPreviewTheme_Click(object sender, RoutedEventArgs e)
        {
            var theme = ThemeManager.CurrentFullTheme;
            if (theme is null)
                return;
            var win = new ThemePreviewWindow(_viewModel, theme) { Owner = this };
            win.ShowDialog();
            // После закрытия окна предпросмотра галерея и кнопки отражают результат
            // («Применить» сохранил тему, «Отмена»/крестик вернули прежнюю).
            BuildThemeGallery();
            InitializeThemeOverrides();
            UpdateThemePreviewButtons();
        }

        /// <summary>Кнопка «Применить» под галереей тем: сохраняет предпросмотренную тему.</summary>
        private void OnApplyThemePreview_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;
            _viewModel.CommitFullThemePreview();
            // После сохранения предпросмотра тема становится активной и сохранённой:
            // галерея и переопределения пересобираются, кнопки гаснут.
            BuildThemeGallery();
            InitializeThemeOverrides();
            UpdateThemePreviewButtons();
        }

        /// <summary>Кнопка «Отмена» под галереей тем: возвращает прежнюю тему без сохранения.</summary>
        private void OnCancelThemePreview_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;
            _viewModel.RevertFullThemePreview();
            // Редактор палитры и предпросмотр показывают цвета возвращённой темы.
            var theme = ThemeManager.CurrentFullTheme;
            if (theme is not null)
                _settings.AdoptFullThemeScheme(theme);
            _settings.SetPaletteMode(ThemeManager.CurrentTheme == ThemeManager.DarkThemeName);
            RefreshSchemeComboBox();
            RefreshColorItems();
            UpdatePaletteButton();
            RefreshSchemePreview();
            BuildThemeGallery();
            InitializeThemeOverrides();
            UpdateThemePreviewButtons();
        }

        /// <summary>
        /// Заполняет списки переопределения набора иконок и стиля кнопок окна. Первый пункт —
        /// «По теме (…)» с названием значения из активной темы.
        /// </summary>
        private void InitializeThemeOverrides()
        {
            if (IconSetOverrideComboBox is null || WindowButtonStyleOverrideComboBox is null)
                return;

            var theme = ThemeManager.CurrentFullTheme ?? ThemeFeatureMap.Resolve(null);
            var byTheme = LocalizationManager.T("Settings.ByTheme");
            _suppressThemeOverrideEvent = true;
            try
            {
                IconSetOverrideComboBox.Items.Clear();
                IconSetOverrideComboBox.Items.Add(new ThemeOverrideItem
                {
                    Id = string.Empty,
                    DisplayName = $"{byTheme} ({LocalizationManager.T(ThemeFeatureMap.IconSetDisplayKey(theme.Icons))})"
                });
                foreach (var set in ThemeFeatureMap.SelectableIconSets)
                {
                    IconSetOverrideComboBox.Items.Add(new ThemeOverrideItem
                    {
                        Id = ThemeFeatureMap.IconSetId(set),
                        DisplayName = LocalizationManager.T(ThemeFeatureMap.IconSetDisplayKey(set))
                    });
                }
                var iconId = ThemeManager.IconSetOverride is { } iconSet ? ThemeFeatureMap.IconSetId(iconSet) : string.Empty;
                IconSetOverrideComboBox.SelectedItem = IconSetOverrideComboBox.Items.Cast<ThemeOverrideItem>()
                    .FirstOrDefault(i => i.Id == iconId);

                WindowButtonStyleOverrideComboBox.Items.Clear();
                WindowButtonStyleOverrideComboBox.Items.Add(new ThemeOverrideItem
                {
                    Id = string.Empty,
                    DisplayName = $"{byTheme} ({LocalizationManager.T(ThemeFeatureMap.ButtonStyleNameKey(theme.ButtonStyle))})"
                });
                foreach (var style in ThemeFeatureMap.SelectableButtonStyles)
                {
                    WindowButtonStyleOverrideComboBox.Items.Add(new ThemeOverrideItem
                    {
                        Id = ThemeFeatureMap.ButtonStyleId(style),
                        DisplayName = LocalizationManager.T(ThemeFeatureMap.ButtonStyleNameKey(style))
                    });
                }
                var styleId = ThemeManager.WindowButtonStyleOverride is { } buttonStyle
                    ? ThemeFeatureMap.ButtonStyleId(buttonStyle)
                    : string.Empty;
                WindowButtonStyleOverrideComboBox.SelectedItem = WindowButtonStyleOverrideComboBox.Items
                    .Cast<ThemeOverrideItem>()
                    .FirstOrDefault(i => i.Id == styleId);
            }
            finally
            {
                _suppressThemeOverrideEvent = false;
            }
        }

        /// <summary>
        /// Смена переопределения набора иконок или стиля кнопок окна: применяется сразу (без
        /// изменения цветов) и сохраняется в настройках.
        /// </summary>
        private void OnThemeOverrideCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressThemeOverrideEvent)
                return;
            var iconSet = (IconSetOverrideComboBox.SelectedItem as ThemeOverrideItem)?.Id ?? string.Empty;
            var buttonStyle = (WindowButtonStyleOverrideComboBox.SelectedItem as ThemeOverrideItem)?.Id ?? string.Empty;
            _viewModel.ApplyThemeOverrides(iconSet, buttonStyle);
        }

        /// <summary>
        /// Обработчик кнопки-переключателя палитры «светлая/тёмная»: переключает
        /// редактируемую палитру на противоположную и обновляет список цветов,
        /// живой предпросмотр темы и состояние самой кнопки.
        /// </summary>
        private void OnPaletteSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;

            var dark = !_settings.EditingDarkPalette;
            _settings.SetPaletteMode(dark);
            RefreshColorItems();
            RefreshSchemePreview();
            UpdatePaletteButton();
        }

        /// <summary>
        /// Обновляет состояние кнопки-переключателя палитры: иконку (в тёмной палитре — солнце,
        /// в светлой — луна, как у кнопки смены темы главного окна), подсказку и подпись.
        /// </summary>
        private void UpdatePaletteButton()
        {
            if (PaletteToggleButton is null)
                return;

            // В тёмной палитре кнопка предлагает перейти на светлую (иконка солнца), и наоборот.
            var dark = _settings?.EditingDarkPalette == true;
            PaletteToggleButton.ToolTip = dark
                ? LocalizationManager.T("Theme.Light")
                : LocalizationManager.T("Theme.Dark");

            if (PaletteToggleIcon is not null)
            {
                PaletteToggleIcon.Data = dark
                    ? (System.Windows.Media.Geometry)FindResource("IconSun")
                    : (System.Windows.Media.Geometry)FindResource("IconMoon");
            }

            if (PaletteStateText is not null)
                PaletteStateText.Text = dark
                    ? LocalizationManager.T("Theme.Dark")
                    : LocalizationManager.T("Theme.Light");
        }

        /// <summary>Строит список доступных тем (встроенные + пользовательские) и выбирает текущую.</summary>
        private void RefreshSchemeComboBox()
        {
            _suppressSchemeEvent = true;
            try
            {
                SchemeComboBox.Items.Clear();
                var items = BuildSchemeItems();
                // Гарантируем, что рабочая схема присутствует в списке.
                if (!items.Any(i => string.Equals(i.Name, _settings.CurrentColorScheme.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    // Текущая схема может быть встроенной полноценной темой («Синяя»/«Зелёная»):
                    // её нельзя переименовывать/удалять, как и «Светлую»/«Тёмную».
                    items.Add(new SchemeComboItem
                    {
                        Name = _settings.CurrentColorScheme.Name,
                        IsBuiltIn = SettingsViewModel.IsBuiltInFullThemeName(_settings.CurrentColorScheme.Name)
                    });
                }
                foreach (var it in items)
                    SchemeComboBox.Items.Add(it);

                var selected = items.FirstOrDefault(i =>
                    string.Equals(i.Name, _settings.CurrentColorScheme.Name, StringComparison.OrdinalIgnoreCase));
                SchemeComboBox.SelectedItem = selected;
                UpdateSchemeButtons();
            }
            finally
            {
                _suppressSchemeEvent = false;
            }
        }

        private List<SchemeComboItem> BuildSchemeItems()
        {
            var items = new List<SchemeComboItem>();
            var all = _settings.AvailableColorSchemes();
            for (int i = 0; i < all.Count; i++)
            {
                // Встроенные полноценные темы («Синяя»/«Зелёная») тоже считаются встроенными.
                var builtIn = i < 2 || SettingsViewModel.IsBuiltInFullThemeName(all[i].Name);
                items.Add(new SchemeComboItem { Name = all[i].Name, IsBuiltIn = builtIn, DisplayName = SettingsViewModel.LocalizedBuiltInName(all[i].Name) });
            }
            return items;
        }

        /// <summary>Обновляет список редактируемых цветов из текущей схемы и подписывается на их live-изменение.</summary>
        private void RefreshColorItems()
        {
            _colorItems.Clear();
            // Кисть HEX-текста контрастна ФАКТИЧЕСКОМУ фону строки редактора (issue #26):
            // строка лежит на фоне окна настроек (карточка активной темы приложения),
            // а не на фоне редактируемой палитры. Иначе при редактировании светлой
            // палитры в тёмной теме текст HEX становился тёмным и сливался с тёмной
            // формой (и наоборот). Фон берём из активной темы приложения.
            var appDark = Themes.ThemeManager.CurrentTheme == Themes.ThemeManager.DarkThemeName;
            var backgroundHex = Themes.ThemeManager.CurrentScheme is { } appScheme
                ? appScheme.PaletteValue(appDark, "CardBackgroundColor")
                : (appDark ? "#1E293B" : "#FFFFFF"); // fallback при отсутствии схемы
            var hexForeground = Parse(Models.ColorScheme.ContrastTextHex(backgroundHex));
            foreach (var (key, label, hex) in _settings.GetEditableColors())
            {
                var item = new ColorItem { Key = key, Label = label, Hex = hex, HexForeground = hexForeground };
                item.PropertyChanged += OnColorItemPropertyChanged;
                _colorItems.Add(item);
            }
        }

        /// <summary>
        /// Подписка на изменение любого редактируемого цвета: при изменении <see cref="ColorItem.Hex"/>
        /// предпросмотр темы перерисовывается актуальными цветами (live-обновление без сохранения).
        /// </summary>
        private void OnColorItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ColorItem.Hex))
                RefreshSchemePreview();
        }

        /// <summary>Обновляет доступность кнопок «Переименовать»/«Удалить» для встроенных тем.</summary>
        private void UpdateSchemeButtons()
        {
            var selected = SchemeComboBox.SelectedItem as SchemeComboItem;
            var isBuiltIn = selected?.IsBuiltIn ?? false;
            if (RenameSchemeButton != null) RenameSchemeButton.IsEnabled = !isBuiltIn;
            if (DeleteSchemeButton != null) DeleteSchemeButton.IsEnabled = !isBuiltIn;
        }

        private void OnSchemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSchemeEvent)
                return;
            if (SchemeComboBox.SelectedItem is SchemeComboItem item)
            {
                // Берём рабочую копию темы (с уже внесёнными правками, если они есть),
                // чтобы переключение между темами не сбрасывало редактирование.
                // Встроенные «Светлая»/«Тёмная» используют слот своей базовой темы,
                // пользовательские — свой JSON-файл. Логика загрузки — в модели представления.
                _settings.SetCurrentScheme(item.Name);
                ThemeDebug($"SchemeCombo select '{item.Name}' -> '{_settings.CurrentColorScheme.Name}' (colors light={_settings.CurrentColorScheme.LightColors.Count}, dark={_settings.CurrentColorScheme.DarkColors.Count})");
                RefreshColorItems();
                UpdateSchemeButtons();
                RefreshSchemePreview();
            }
        }

        /// <summary>Диагностика переключения/применения темы (пишет во временный файл).</summary>
        private static void ThemeDebug(string message)
        {
#if DEBUG
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_theme_debug.log"),
                    "[settings] " + message + System.Environment.NewLine);
            }
            catch { /* не критично */ }
#endif
        }

        /// <summary>Применяет выбранную тему и цвета сразу (предпросмотр, без сохранения настроек).</summary>
        private void OnApplyColorScheme_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PreviewColorScheme(_settings.CurrentColorScheme);
        }

        /// <summary>
        /// Автогенерация цветовой схемы из одного базового цвета (issue #271): пользователь
        /// выбирает цвет (по умолчанию — текущий акцент), обе палитры (светлая и тёмная)
        /// строятся автоматически и подставляются в редактор для правки перед сохранением.
        /// </summary>
        private void OnGenerateSchemeFromColor_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;

            var baseColor = _settings.CurrentColorScheme.PaletteValue(
                _settings.EditingDarkPalette, "AccentColor");

            var picker = new ColorPickerWindow(baseColor) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                _settings.ApplyGeneratedScheme(picker.Result);
                RefreshColorItems();
                RefreshSchemePreview();
                UpdatePaletteButton();
            }
        }

        /// <summary>Открывает диалог выбора цвета для отдельного элемента схемы.</summary>
        private void OnColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string key })
                return;
            var item = _colorItems.FirstOrDefault(ci => string.Equals(ci.Key, key, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            var picker = new ColorPickerWindow(item.Hex) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                // Сначала обновляем схему, затем элемент (сеттер Hex поднимает PropertyChanged,
                // по которому предпросмотр перечитывает актуальное значение из схемы).
                _settings.SetColor(item.Key, picker.Result);
                item.Hex = picker.Result;
                RefreshSchemePreview();
            }
        }

        /// <summary>
        /// Перерисовывает единый живой предпросмотр темы цветами редактируемой схемы.
        /// Рисует ОДНУ палитру — ту, что сейчас редактируется (светлую или тёмную,
        /// в зависимости от переключателя палитры <c>_settings.EditingDarkPalette</c>) —
        /// читая значения через <see cref="ColorScheme.PaletteValue"/>. Поэтому отражает
        /// незаконченные правки редактора до сохранения.
        /// </summary>
        private void RefreshSchemePreview()
        {
            if (_settings is null || PreviewShell is null)
                return;

            PaintSchemePreview(_settings.CurrentColorScheme, _settings.EditingDarkPalette);
        }

        /// <summary>Рисует единый миниатюрный предпросмотр темы для заданной палитры.</summary>
        private void PaintSchemePreview(ColorScheme scheme, bool dark)
        {
            string V(string key) => scheme.PaletteValue(dark, key);

            // Окно: подложка-карточка с рамкой и акцентная шапка. Заголовок на шапке —
            // контрастный к акценту цвет (TextOnAccentBrush пересчитывается по WCAG
            // в ThemeManager, issue #26), иначе предпросмотр расходится с реальностью.
            PaintBorder(PreviewShell, V("CardBackgroundColor"), V("BorderColor"));
            Paint(PreviewTitleBar, V("AccentColor"));
            PaintText(PreviewTitleText, Contrast(ParseColor(V("AccentColor"))));

            // Боковая панель: тёмный фон, контрастный текст, акцентная/фоновая подсветка пунктов.
            var sidebar = ParseColor(V("SidebarColor"));
            var sidebarText = Contrast(sidebar);
            Paint(PreviewSidebar, V("SidebarColor"));
            Paint(PreviewNavSelected, V("SidebarSelectedColor"));
            Paint(PreviewNavItem1, V("SidebarHoverColor"));
            Paint(PreviewNavItem2, V("SidebarHoverColor"));
            PaintText(PreviewNavSelectedText, sidebarText);
            PaintText(PreviewNavItem1Text, sidebarText);
            PaintText(PreviewNavItem2Text, sidebarText);

            // Контент.
            Paint(PreviewMain, V("ContentBackgroundColor"));
            PaintText(PreviewContentTitle, V("TextPrimaryColor"));
            PaintText(PreviewContentSubtitle, V("TextSecondaryColor"));

            // Карточка.
            PaintBorder(PreviewCard, V("CardBackgroundColor"), V("BorderColor"));
            PaintText(PreviewCardTitle, V("TextPrimaryColor"));
            PaintText(PreviewCardText, V("TextSecondaryColor"));

            // Поле ввода.
            PaintTextBox(PreviewTextField,
                V("CardBackgroundColor"), V("BorderColor"), V("TextPrimaryColor"));

            // Кнопки: акцентная и вторичная.
            Paint(PreviewPrimaryButton, V("AccentColor"));
            PaintText(PreviewPrimaryButtonText, V("ButtonTextColor"));
            Paint(PreviewSecondaryButton, V("SecondaryButtonBackgroundColor"));
            PaintText(PreviewSecondaryButtonText, V("ButtonTextColor"));

            // Список.
            PaintBorder(PreviewListBox, V("CardBackgroundColor"), V("BorderColor"));
            Paint(PreviewListSelected, V("ItemSelectedColor"));
            Paint(PreviewListItem1, V("ItemHoverColor"));
            Paint(PreviewListItem2, V("ItemHoverColor"));
            PaintText(PreviewListSelectedText, V("TextPrimaryColor"));
            PaintText(PreviewListItem1Text, V("TextPrimaryColor"));
            PaintText(PreviewListItem2Text, V("TextPrimaryColor"));
        }

        // ---- Вспомогательные методы для миниатюрного предпросмотра ----

        private static SolidColorBrush Parse(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Transparent; }
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Transparent; }
        }

        /// <summary>Кисть с максимальным контрастом к заданному цвету (чёрный/белый).</summary>
        private static SolidColorBrush Contrast(Color c)
        {
            var lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return new SolidColorBrush(lum > 0.5 ? Colors.Black : Colors.White);
        }

        private static void Paint(Border? b, string hex)
        {
            if (b is not null) b.Background = Parse(hex);
        }

        private static void PaintBorder(Border? b, string bgHex, string borderHex)
        {
            if (b is null) return;
            b.Background = Parse(bgHex);
            b.BorderBrush = Parse(borderHex);
        }

        private static void PaintText(TextBlock? t, string hex)
        {
            if (t is not null) t.Foreground = Parse(hex);
        }

        private static void PaintText(TextBlock? t, SolidColorBrush? brush)
        {
            if (t is not null && brush is not null) t.Foreground = brush;
        }

        private static void PaintText(Control? c, string hex)
        {
            if (c is not null) c.Foreground = Parse(hex);
        }

        private static void PaintTextBox(TextBox? t, string bgHex, string borderHex, string fgHex)
        {
            if (t is null) return;
            t.Background = Parse(bgHex);
            t.BorderBrush = Parse(borderHex);
            t.Foreground = Parse(fgHex);
        }

        /// <summary>Создаёт собственную тему на основе текущих цветов.</summary>
        private void OnCreateScheme_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptForName(LocalizationManager.T("Settings.CreateTheme"), string.Format(LocalizationManager.T("Settings.CopyOf"), _settings.CurrentColorScheme.Name));
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            if (SettingsViewModel.IsReservedName(name))
            {
                _dialogs.ShowWarning(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.CreateTheme"));
                return;
            }

            if (ThemeManager.FindCustomScheme(name) is not null
                && !_dialogs.Confirm(string.Format(LocalizationManager.T("Settings.CreateSchemeReplace"), name),
                    LocalizationManager.T("Settings.CreateTheme")))
                return;

            _settings.CreateCustomScheme(name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Переименовывает выбранную пользовательскую тему.</summary>
        private void OnRenameScheme_Click(object sender, RoutedEventArgs e)
        {
            if (SchemeComboBox.SelectedItem is not SchemeComboItem item || item.IsBuiltIn)
            {
                _dialogs.ShowInfo(LocalizationManager.T("Settings.CannotRenameBuiltIn"),
                    LocalizationManager.T("Settings.RenameThemeTitle"));
                return;
            }

            var name = PromptForName(LocalizationManager.T("Settings.Rename"), item.Name);
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), item.Name, StringComparison.OrdinalIgnoreCase))
                return;
            name = name.Trim();
            if (SettingsViewModel.IsReservedName(name))
            {
                _dialogs.ShowWarning(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.RenameThemeTitle"));
                return;
            }

            _settings.RenameCustomScheme(item.Name, name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Удаляет выбранную пользовательскую тему.</summary>
        private void OnDeleteScheme_Click(object sender, RoutedEventArgs e)
        {
            if (SchemeComboBox.SelectedItem is not SchemeComboItem item || item.IsBuiltIn)
            {
                _dialogs.ShowInfo(LocalizationManager.T("Settings.CannotDeleteBuiltIn"),
                    LocalizationManager.T("Settings.DeleteThemeTitle"));
                return;
            }

            if (!_dialogs.Confirm(string.Format(LocalizationManager.T("Settings.DeleteThemeConfirm"), item.Name),
                    LocalizationManager.T("Settings.DeleteThemeTitle")))
                return;

            _settings.DeleteCustomScheme(item.Name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Сбрасывает цвета выбранной темы на значения по умолчанию.</summary>
        private void OnResetSchemeColors_Click(object sender, RoutedEventArgs e)
        {
            _settings.ResetCurrentSchemeColors();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Выгружает текущую тему в JSON-файл.</summary>
        private void OnExportScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.T("Settings.ExportSchemeTitle"),
                Filter = $"{LocalizationManager.T("Settings.ColorSchemeFilter")}|*.json|{LocalizationManager.T("Common.AllFiles")}|*.*",
                FileName = _settings.CurrentColorScheme.Name + ".json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                _viewModel.ExportColorScheme(_settings.CurrentColorScheme, dialog.FileName);
                _dialogs.ShowInfo(string.Format(LocalizationManager.T("Settings.ExportedOk"), dialog.FileName),
                    LocalizationManager.T("Settings.ExportDoneTitle"));
            }
            catch (Exception ex)
            {
                _dialogs.ShowError(string.Format(LocalizationManager.T("Settings.ExportFailed"), ex.Message),
                    LocalizationManager.T("Settings.ExportDoneTitle"));
            }
        }

        /// <summary>Загружает тему из JSON-файла и добавляет её в список пользовательских тем.</summary>
        private void OnImportScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("Settings.ImportSchemeTitle"),
                Filter = $"{LocalizationManager.T("Settings.ColorSchemeFilter")}|*.json|{LocalizationManager.T("Common.AllFiles")}|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
                return;

            var scheme = _viewModel.ImportColorScheme(dialog.FileName);
            if (scheme is null || (scheme.LightColors.Count == 0 && scheme.DarkColors.Count == 0))
            {
                _dialogs.ShowError(LocalizationManager.T("Settings.ImportFailed"),
                    LocalizationManager.T("Settings.ImportDoneTitle"));
                return;
            }

            if (ThemeManager.FindCustomScheme(scheme.Name) is not null
                && !_dialogs.Confirm(string.Format(LocalizationManager.T("Settings.ImportReplaceLinux"), scheme.Name),
                    LocalizationManager.T("Settings.ImportDoneTitle")))
                return;

            _settings.AdoptImportedScheme(scheme);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
            _dialogs.ShowInfo(string.Format(LocalizationManager.T("Settings.ImportedOk"), scheme.Name),
                LocalizationManager.T("Settings.ImportDoneTitle"));
        }

        /// <summary>
        /// Показывает модальное окно ввода названия темы (стиль Material Design,
        /// как остальные окна приложения). Возвращает null при отмене.
        /// </summary>
        private string? PromptForName(string title, string initial)
        {
            var dialog = new NameInputWindow(title, LocalizationManager.T("NameInput.Prompt"), LocalizationManager.T("Common.Ok"), initial) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        }
    }
}
#endif