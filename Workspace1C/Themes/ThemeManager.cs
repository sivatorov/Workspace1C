#if WINDOWS
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Themes
{
    /// <summary>
    /// Управляет переключением светлой и тёмной темы приложения и цветовыми схемами.
    /// Поддерживает встроенные схемы («Светлая», «Тёмная»), пользовательские темы
    /// (сохраняются в каталог пользователя) и выгрузку/загрузку схем в JSON-файл.
    /// </summary>
    public static class ThemeManager
    {
        public const string LightThemeName = "Light";
        public const string DarkThemeName = "Dark";

        /// <summary>Название активной темы (Light/Dark) — базовая тема текущей схемы.</summary>
        public static string CurrentTheme { get; private set; } = LightThemeName;

        /// <summary>Активная цветовая схема (тема оформления).</summary>
        public static ColorScheme CurrentScheme { get; private set; } = ColorScheme.CreateLight();

        /// <summary>
        /// Активная полноценная тема (задаётся <see cref="ApplyFullTheme"/>). Хранит выбранный
        /// идентификатор, стиль кнопок окна, набор иконок и схему; null до первого применения.
        /// </summary>
        public static ThemeDefinition? CurrentFullTheme { get; private set; }

        /// <summary>Активный стиль кнопок управления окном (из <see cref="CurrentFullTheme"/>).</summary>
        public static WindowButtonStyle CurrentWindowButtonStyle { get; private set; } = WindowButtonStyle.Fluent;

        /// <summary>Активный набор иконок (из <see cref="CurrentFullTheme"/>).</summary>
        public static IconSet CurrentIconSet { get; private set; } = IconSet.Fluent;

        /// <summary>
        /// Событие смены полноценной темы: поднимается после применения базового варианта,
        /// схемы, стиля кнопок окна и набора иконок. Слушатели перерисовывают интерфейс
        /// (меню «Утилиты», кнопки окна, иконки — Задачи 4–6).
        /// </summary>
        public static event EventHandler? ThemeChanged;

        /// <summary>Каталог пользовательских цветовых схем (JSON-файлы).</summary>
        public static string CustomSchemesDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Workspace1C",
            "ColorSchemes");

        private static readonly Uri LightUri = new("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkUri = new("Themes/DarkTheme.xaml", UriKind.Relative);

        /// <summary>
        /// Применяет цветовую схему: загружает базовую тему (Light/Dark) и накладывает
        /// цвета схемы на ресурсы приложения.
        /// </summary>
        public static void ApplyScheme(ColorScheme? scheme)
        {
            var app = Application.Current;
            if (app is null)
                return;

            scheme ??= ColorScheme.CreateLight();
            scheme.Normalize();
            CurrentScheme = scheme;
            // Схема текущей полноценной темы следует за активной: пользовательская схема,
            // применённая поверх встроенной (ApplyScheme после ApplyFullTheme), остаётся
            // частью темы и учитывается при переключении базового варианта.
            if (CurrentFullTheme is not null)
                CurrentFullTheme.Scheme = scheme;

            // Загружаем базовую тему по текущему варианту и накладываем его палитру.
            var dark = CurrentTheme == DarkThemeName;
            var uri = dark ? DarkUri : LightUri;
            var dictionary = new ResourceDictionary { Source = uri };
            ApplyColors(dictionary, scheme, dark);

            // Ищем существующий словарь темы (Light/Dark), не трогаем MaterialDesign.
            var merged = app.Resources.MergedDictionaries;
            int index = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                var src = merged[i].Source?.OriginalString ?? "";
                if (src.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                merged[index] = dictionary;
            else
                merged.Add(dictionary);
        }

        /// <summary>
        /// Задаёт базовый вариант темы (светлый/тёмный) и применяет активную схему
        /// с палитрой этого варианта. Текущая полноценная тема сохраняет идентификатор
        /// и параметры оформления, но пересоздаётся с новым вариантом (обратная
        /// совместимость: вызовы из MainViewModel/LoginWindow продолжают работать).
        /// </summary>
        public static void ApplyTheme(bool dark)
        {
            var themeName = dark ? DarkThemeName : LightThemeName;
            var changed = !string.Equals(CurrentTheme, themeName, StringComparison.Ordinal);
            CurrentTheme = themeName;
            if (CurrentFullTheme is not null)
                CurrentFullTheme = ThemeFeatureMap.WithBaseVariant(CurrentFullTheme, dark);
            ApplyScheme(CurrentScheme);
            if (changed)
                ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Применяет вариант темы по имени («Light» / «Dark»).</summary>
        public static void ApplyTheme(string themeName)
            => ApplyTheme(themeName == DarkThemeName);

        /// <summary>Переключает между светлой и тёмной темой, возвращает новое имя темы.</summary>
        public static string ToggleTheme()
        {
            var next = CurrentTheme == DarkThemeName ? LightThemeName : DarkThemeName;
            ApplyTheme(next == DarkThemeName);
            return next;
        }

        /// <summary>
        /// Переопределение набора иконок пользователем (блок «Темы», 0.3.12.62): null — набор
        /// берётся из темы.
        /// </summary>
        public static IconSet? IconSetOverride { get; private set; }

        /// <summary>
        /// Переопределение стиля кнопок управления окном пользователем (блок «Темы», 0.3.12.62):
        /// null — стиль берётся из темы.
        /// </summary>
        public static WindowButtonStyle? WindowButtonStyleOverride { get; private set; }

        /// <summary>
        /// Задаёт переопределения набора иконок и стиля кнопок окна из настроек
        /// (AppSettings.IconSetOverride / WindowButtonStyleOverride; пусто — «по теме»).
        /// Вызывается до <see cref="ApplyFullTheme"/> при старте; во время работы — вместе с
        /// <see cref="ReapplyThemeFeatures"/>.
        /// </summary>
        public static void SetOverrides(string? iconSet, string? buttonStyle)
        {
            IconSetOverride = ThemeFeatureMap.ParseIconSetOverride(iconSet);
            WindowButtonStyleOverride = ThemeFeatureMap.ParseButtonStyleOverride(buttonStyle);
        }

        /// <summary>
        /// Повторно применяет набор иконок и стиль кнопок окна активной темы с учётом
        /// переопределений, не трогая цветовую схему (пользовательская схема поверх темы
        /// сохраняется), и рассылает <see cref="ThemeChanged"/>.
        /// </summary>
        public static void ReapplyThemeFeatures()
        {
            var theme = CurrentFullTheme ?? ThemeFeatureMap.Resolve(null);
            CurrentWindowButtonStyle = ThemeFeatureMap.EffectiveButtonStyle(theme, WindowButtonStyleOverride);
            CurrentIconSet = ThemeFeatureMap.EffectiveIconSet(theme, IconSetOverride);
            ApplyWindowButtonStyle();
            ApplyIconSet();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Применяет полноценную тему по идентификатору (см. <see cref="ThemeCatalog"/>):
        /// задаёт текущее определение, базовый вариант (Light/Dark), стиль кнопок окна
        /// и набор иконок, применяет схему темы и рассылает <see cref="ThemeChanged"/>.
        /// Неизвестный или пустой идентификатор ошибкой не является — применяется светлая
        /// тема по умолчанию. Пользовательская схема при необходимости накладывается
        /// поверх вызовом <see cref="ApplyScheme"/> (порядок вызовов: сначала
        /// ApplyFullTheme, затем ApplyScheme(пользовательская схема)).
        /// </summary>
        public static void ApplyFullTheme(string? id)
        {
            var theme = ThemeFeatureMap.Resolve(id);
            CurrentFullTheme = ThemeFeatureMap.WithBaseVariant(theme, theme.IsDark);
            // Набор иконок и стиль кнопок окна — параметры темы, но пользователь может
            // переопределить их в блоке «Темы» (0.3.12.62).
            CurrentWindowButtonStyle = ThemeFeatureMap.EffectiveButtonStyle(CurrentFullTheme, WindowButtonStyleOverride);
            CurrentIconSet = ThemeFeatureMap.EffectiveIconSet(CurrentFullTheme, IconSetOverride);
            CurrentTheme = CurrentFullTheme.IsDark ? DarkThemeName : LightThemeName;
            ApplyScheme(CurrentFullTheme.Scheme);
            ApplyWindowButtonStyle();
            ApplyIconSet();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Выставляет ресурсы стиля кнопок управления окном (подложка наведения, подложка
        /// кнопки «закрыть», радиус скругления, размер) согласно
        /// <see cref="CurrentWindowButtonStyle"/>. Ключи описаны в
        /// <see cref="ThemeFeatureMap"/>; стили кнопок (Задача 4) читают их
        /// DynamicResource'ом и перекрашиваются на лету.
        /// </summary>
        private static void ApplyWindowButtonStyle()
        {
            var app = Application.Current;
            if (app is null)
                return;
            // issue #26: в тёмной теме подложка наведения кнопок окна — полупрозрачный
            // белый, а не «дневная» яркая заливка (#E2E8F0/#FFF3CD — выглядела белой).
            var dark = CurrentTheme == DarkThemeName;
            var metrics = ThemeFeatureMap.ButtonMetrics(CurrentWindowButtonStyle, dark);
            app.Resources[ThemeFeatureMap.WindowButtonHoverBrushKey] = ParseBrush(metrics.HoverHex);
            // issue #26 (7OH, 2026-10-29): подложка наведения кнопок окна при НЕАКТИВНОМ
            // окне — приглушённая (тёмный slate в тёмной теме), чтобы на тёмной шапке
            // карточки кнопки не «светились белым». Активная шапка остаётся на
            // полупрозрачном белом (WindowButtonHoverBrush / OnAccent-варианты).
            app.Resources[ThemeFeatureMap.WindowButtonInactiveHoverBrushKey] =
                ParseBrush(ThemeFeatureMap.InactiveHoverHex(CurrentWindowButtonStyle, dark));
            app.Resources[ThemeFeatureMap.WindowButtonCloseHoverBrushKey] = ParseBrush(metrics.CloseHoverHex);
            // Цвета «светофора» macOS (Задача 3): жёлтый круг «свернуть» и зелёный
            // «развернуть». У остальных стилей поля null — кнопки используют общую
            // WindowButtonHoverBrush.
            app.Resources[ThemeFeatureMap.WindowButtonMinimizeHoverBrushKey] =
                ParseBrush(metrics.MinimizeHoverHex ?? metrics.HoverHex);
            app.Resources[ThemeFeatureMap.WindowButtonMaximizeHoverBrushKey] =
                ParseBrush(metrics.MaximizeHoverHex ?? metrics.HoverHex);
            // Радиус кладём типизированным CornerRadius: шаблон кнопки привязывает
            // свойство CornerRadius напрямую, а конвертации double → CornerRadius во
            // фреймворке нет. Avalonia-версия ThemeManager кладёт double — там кнопки
            // читают ресурс кодом.
            app.Resources[ThemeFeatureMap.WindowButtonCornerRadiusKey] = new CornerRadius(metrics.CornerRadius);
            app.Resources[ThemeFeatureMap.WindowButtonSizeKey] = metrics.Size;
        }

        /// <summary>
        /// Ключ стиля кнопки управления окном для текущего стиля кнопок (Задача 3):
        /// macOS использует отдельные шаблоны «светофора» (три цветных круга),
        /// остальные стили — общие шаблоны WindowControlButton/WindowControlCloseButton.
        /// Используется главным окном (MainWindow.xaml.cs) и WindowChromeHelper.
        /// </summary>
        public static string WindowButtonStyleKey(bool close, bool onAccent)
        {
            if (CurrentWindowButtonStyle == WindowButtonStyle.MacOS)
            {
                return close
                    ? (onAccent ? "WindowControlCloseButtonMacOSOnAccent" : "WindowControlCloseButtonMacOS")
                    : (onAccent ? "WindowControlButtonMacOSOnAccent" : "WindowControlButtonMacOS");
            }
            return close
                ? (onAccent ? "WindowControlCloseButtonOnAccent" : "WindowControlCloseButton")
                : (onAccent ? "WindowControlButtonOnAccent" : "WindowControlButton");
        }

        /// <summary>
        /// Выставляет ресурс активного набора иконок (<see cref="IconSetNameKey"/>) согласно
        /// <see cref="CurrentIconSet"/>. Оба словаря иконок (Icons.xaml — Fluent и
        /// IconsOutline.xaml — Outline) подключены в ресурсы приложения; резолв
        /// (IconKeyToGeometryConverter) читает этот ресурс и для Outline ищет ключ с
        /// суффиксом ".Outline", при отсутствии — бесшовный fallback на Fluent (Задача 5).
        /// </summary>
        private static void ApplyIconSet()
        {
            var app = Application.Current;
            if (app is null)
                return;
            app.Resources[ThemeFeatureMap.IconSetNameKey] = ThemeFeatureMap.IconSetId(CurrentIconSet);

            // Живое переключение (0.3.12.60): базовые ключи (IconAdd, GlyphWindowClose, …)
            // переопределяются в собственных ресурсах приложения геометрией активного набора —
            // все DynamicResource-потребители перерисовываются сразу. Прежние переопределения
            // снимаются, поэтому возврат к Fluent восстанавливает словарь Icons.xaml.
            foreach (var key in AppliedIconOverrides)
                app.Resources.Remove(key);
            AppliedIconOverrides.Clear();
            foreach (var pair in ThemeFeatureMap.IconOverrides(CurrentIconSet, BaseIconKeys(app),
                         key => app.TryFindResource(key) is not null))
            {
                if (app.TryFindResource(pair.Value) is { } resource)
                {
                    app.Resources[pair.Key] = resource;
                    AppliedIconOverrides.Add(pair.Key);
                }
            }

            // «Блик» поверх иконок (Windows XP) и уведомление привязок PackIcon/конвертеров.
            var gloss = ThemeFeatureMap.IconGloss(CurrentIconSet);
            app.Resources[ThemeFeatureMap.IconGlossBrushKey] = gloss is { } g
                ? CreateGlossBrush(g.Top, g.Bottom, g.Stop)
                : Brushes.Transparent;
            IconSetState.Instance.Current = CurrentIconSet;
        }

        /// <summary>Ключи, переопределённые последним <see cref="ApplyIconSet"/>.</summary>
        private static readonly List<string> AppliedIconOverrides = new();

        private static List<string>? _baseIconKeys;

        /// <summary>
        /// Ключи геометрий базового словаря Themes/Icons.xaml (Icon*, Glyph*), собранные один
        /// раз: именно их набор иконок переопределяет при переключении.
        /// </summary>
        private static IEnumerable<string> BaseIconKeys(Application app)
        {
            if (_baseIconKeys is not null)
                return _baseIconKeys;
            var keys = new List<string>();
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                var source = dict.Source?.OriginalString ?? string.Empty;
                if (!source.EndsWith("Themes/Icons.xaml", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var key in dict.Keys)
                {
                    if (key is string name && dict[name] is Geometry)
                        keys.Add(name);
                }
            }
            _baseIconKeys = keys;
            return keys;
        }

        /// <summary>Вертикальный градиент «блика» иконки (верх → низ, гаснет к доле stop).</summary>
        private static Brush CreateGlossBrush(string top, string bottom, double stop)
        {
            TryParseColor(top, out var topColor);
            TryParseColor(bottom, out var bottomColor);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop(topColor, 0));
            brush.GradientStops.Add(new GradientStop(bottomColor, stop));
            brush.GradientStops.Add(new GradientStop(bottomColor, 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>Создаёт кисть из шестнадцатеричного цвета (#RRGGBB), прозрачную при ошибке.</summary>
        private static Brush ParseBrush(string hex)
            => TryParseColor(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

        /// <summary>Шрифт интерфейса по умолчанию (если настройки не заданы).</summary>
        public const string DefaultFontFamily = "Segoe UI";
        public const double DefaultFontSize = 13;
        public const string DefaultFontWeight = "Normal";
        public const string DefaultFontStyle = "Normal";

        /// <summary>
        /// Применяет настройки шрифта интерфейса к указанному элементу (окну).
        /// Семейство, размер, начертание и стиль шрифта задаются через наследуемые
        /// свойства <see cref="TextElement"/>, поэтому распространяются на все дочерние
        /// текстовые элементы, не переопределяющие их явно.
        /// </summary>
        public static void ApplyFont(FrameworkElement target,
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (target is null)
                return;

            try
            {
                var family = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
                var size = fontSize > 0 ? fontSize : DefaultFontSize;

                TextElement.SetFontFamily(target, new FontFamily(family));
                TextElement.SetFontSize(target, size);
                TextElement.SetFontWeight(target,
                    string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                        ? FontWeights.Bold : FontWeights.Normal);
                TextElement.SetFontStyle(target,
                    string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                        ? FontStyles.Italic : FontStyles.Normal);
            }
            catch
            {
                // Игнорируем некорректные настройки шрифта (например, несуществующее семейство).
            }
        }

        /// <summary>
        /// Применяет настройки шрифта интерфейса ко всем открытым окнам приложения.
        /// Используется для мгновенного обновления интерфейса после применения/сохранения.
        /// </summary>
        public static void ApplyFontToAllWindows(
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (Application.Current is null)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                ApplyFont(window, fontFamily, fontSize, fontWeight, fontStyle);
            }
        }

        // ---- Настройки шрифта отдельных областей интерфейса ----

        public const string FontDefault = "Default";
        public const string FontList = "List";
        public const string FontListHeader = "ListHeader";
        public const string FontRightPanel = "RightPanel";
        public const string FontStatusBar = "StatusBar";
        public const string FontTabs = "Tabs";
        public const string FontButtons = "Buttons";
        public const string FontInputs = "Inputs";

        /// <summary>Все ключи областей (в порядке наложения).</summary>
        public static readonly string[] AllFontScopes =
        {
            FontDefault, FontButtons, FontInputs, FontTabs, FontListHeader, FontList, FontRightPanel, FontStatusBar
        };

        /// <summary>Читаемое название области для интерфейса настроек.</summary>
        public static string FontScopeDisplayName(string key) => key switch
        {
            FontDefault => LocalizationManager.T("Font.Default"),
            FontList => LocalizationManager.T("Font.List"),
            FontListHeader => LocalizationManager.T("Font.ListHeader"),
            FontRightPanel => LocalizationManager.T("Font.RightPanel"),
            FontStatusBar => LocalizationManager.T("Font.StatusBar"),
            FontTabs => LocalizationManager.T("Font.Tabs"),
            FontButtons => LocalizationManager.T("Font.Buttons"),
            FontInputs => LocalizationManager.T("Font.Inputs"),
            _ => key
        };

        /// <summary>
        /// Применяет настройки шрифта ко всем текстовым элементам поддерева принудительно
        /// (устанавливает локальные значения, перекрывающие фиксированные размеры в XAML).
        /// </summary>
        public static void ApplyFontToTree(DependencyObject root, ElementFontSettings? fs)
        {
            if (root is null)
                return;
            ApplyFontProps(root as FrameworkElement, fs);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                ApplyFontToTree(VisualTreeHelper.GetChild(root, i), fs);
        }

        private static void ApplyFontProps(FrameworkElement? fe, ElementFontSettings? fs)
        {
            if (fe is null)
                return;
            var family = string.IsNullOrWhiteSpace(fs?.FontFamily) ? DefaultFontFamily : fs.FontFamily;
            var size = fs is { FontSize: > 0 } ? fs.FontSize : DefaultFontSize;
            var weight = string.Equals(fs?.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                ? FontWeights.Bold : FontWeights.Normal;
            var style = string.Equals(fs?.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                ? FontStyles.Italic : FontStyles.Normal;

            fe.SetValue(TextElement.FontFamilyProperty, new FontFamily(family));
            fe.SetValue(TextElement.FontSizeProperty, size);
            fe.SetValue(TextElement.FontWeightProperty, weight);
            fe.SetValue(TextElement.FontStyleProperty, style);
        }

        /// <summary>
        /// Применяет индивидуальные настройки шрифта областей к главному окну.
        /// Сначала применяется «По умолчанию» ко всему окну, затем более конкретные
        /// области (кнопки, поля, вкладки, список, заголовки, панели) накладываются поверх.
        /// </summary>
        public static void ApplyElementFonts(MainWindow window, IReadOnlyDictionary<string, ElementFontSettings>? fonts)
        {
            if (window is null)
                return;
            fonts ??= new Dictionary<string, ElementFontSettings>();

            ElementFontSettings? Scope(string key)
            {
                if (fonts.TryGetValue(key, out var fs) && fs is not null && fs.FontSize > 0)
                    return fs;
                return null;
            }

            if (window.Content is FrameworkElement content)
                ApplyFontToTree(content, Scope(FontDefault) ?? new ElementFontSettings());

            ApplyFontToType(window, typeof(Button), Scope(FontButtons));
            ApplyFontToType(window, typeof(TextBoxBase), Scope(FontInputs));
            ApplyFontToType(window, typeof(PasswordBox), Scope(FontInputs));
            ApplyFontToType(window, typeof(ComboBox), Scope(FontInputs));

            ApplyFontToNamed(window, "TabsPanel", Scope(FontTabs));
            ApplyFontToNamed(window, "HeaderGrid", Scope(FontListHeader));
            ApplyFontToNamed(window, "MainTree", Scope(FontList));
            ApplyFontToNamed(window, "RightPanelBorder", Scope(FontRightPanel));
            ApplyFontToNamed(window, "StatusBarBorder", Scope(FontStatusBar));
        }

        private static void ApplyFontToNamed(MainWindow window, string name, ElementFontSettings? fs)
        {
            if (fs is null)
                return;
            var el = window.FindName(name) as FrameworkElement;
            if (el is not null)
                ApplyFontToTree(el, fs);
        }

        private static void ApplyFontToType(DependencyObject root, Type type, ElementFontSettings? fs)
        {
            if (root is null || fs is null)
                return;
            if (type.IsInstanceOfType(root) && root is FrameworkElement fe)
                ApplyFontProps(fe, fs);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                ApplyFontToType(VisualTreeHelper.GetChild(root, i), type, fs);
        }

        // ---- Компактный режим (уменьшенная плотность интерфейса) ----
        // Исходные значения метрик сохраняются при первом применении, чтобы
        // переключение компактного режима обратно восстанавливало обычные размеры.
        // Вместе со значением запоминаем, откуда оно взялось. Локальное значение при возврате
        // в обычный режим надо записать обратно, а значение, пришедшее из стиля, его триггера
        // или шаблона, — наоборот, снять (ClearValue): запись локального значения гасит стиль
        // навсегда, потому что локальное значение в WPF сильнее. В нынешней разметке отступы
        // кнопок панели команд заданы стилями простыми константами, поэтому разницы не видно;
        // она появится, как только у такого отступа окажется триггер или другая тема
        // (issue #214). Оговорка: значение из DynamicResource тоже считается локальным,
        // и запись обратно оборвала бы связь с ресурсом; в разметке приложения отступов
        // из ресурсов нет, но при их появлении правило надо уточнить.
        private readonly record struct CompactMetric(Thickness Value, bool WasLocal);

        // Issue #21: таблицы со СЛАБЫМИ ключами. Раньше это были Dictionary с сильными
        // ссылками: каждая строка списка баз, созданная в компактном режиме, оставалась
        // в них навсегда после ухода из визуального дерева (переключение вкладок
        // пересоздаёт строки) — утечка ~5 МБ на переключение и растущие задержки.
        private static readonly Workspace1C.Collections.WeakKeyTable<FrameworkElement, CompactMetric> _compactMargin = new();
        private static readonly Workspace1C.Collections.WeakKeyTable<Control, CompactMetric> _compactPadding = new();
        private static readonly Workspace1C.Collections.WeakKeyTable<DependencyObject, double> _compactFont = new();
        private static readonly Workspace1C.Collections.WeakKeyTable<ColumnDefinition, double> _compactColumn = new();

        /// <summary>Число живых записей метрик компактного режима (диагностика issue #21).</summary>
        public static int CompactMetricEntries
            => _compactMargin.Count + _compactPadding.Count + _compactFont.Count + _compactColumn.Count;

        // Колонки, навсегда исключённые из компактизации ширины (колонка-компенсатор
        // заголовка, ширина которой целиком управляется AlignHeaderToData). В отличие от
        // удаления из _compactColumn, добавление сюда переживает последующие проходы
        // ApplyCompact, поэтому повторное применение компакт-режима не масштабирует уже
        // выставленную ширину и заголовок не «уезжает» влево относительно строк (issue #214).
        private static readonly HashSet<ColumnDefinition> _excludedCompactColumns = new();

        /// <summary>
        /// Применяет компактный режим к главному окну: уменьшает отступы, внутренние поля
        /// и ширины фиксированных колонок на коэффициент 0.7, а шрифты (в т.ч. унаследованные —
        /// заголовки колонок, строки списка) — на более мягкий коэффициент 0.9, чтобы надписи
        /// оставались читаемыми (восстанавливает при выключении).
        /// Высоты и минимальные размеры не трогаются, чтобы не создавать артефакты-разделители.
        /// Вызывается из настроек при переключении и при запуске приложения.
        /// </summary>
        public static void ApplyCompact(bool compact)
        {
            var window = Application.Current?.MainWindow;
            if (window is null)
                return;
            // Геометрия (отступы, поля, ширины колонок) сжимается сильнее (0.7),
            // а шрифты — мягче (0.9), чтобы текст оставался читаемым.
            var factor = compact ? 0.7 : 1.0;
            var fontFactor = compact ? 0.9 : 1.0;
            ApplyCompactElement(window, factor, fontFactor);
        }

        /// <summary>
        /// Применяет компактный режим к поддереву визуального дерева — например, к строке
        /// списка баз или заголовку группы. WPF-вариант компактности применяется обходом
        /// визуального дерева (<see cref="ApplyCompact"/>), поэтому элементы, создаваемые
        /// уже после первичного применения (виртуализация/прокрутка дерева, пересборка при
        /// сохранении свойств базы), иначе оставались бы полной плотности и «разъезжались»
        /// относительно заголовка. Вызывается из обработчиков реализации строк дерева.
        /// </summary>
        public static void ApplyCompactTree(DependencyObject root, bool compact)
        {
            if (root is null)
                return;
            var factor = compact ? 0.7 : 1.0;
            var fontFactor = compact ? 0.9 : 1.0;
            ApplyCompactElement(root, factor, fontFactor);
        }

        /// <summary>
        /// Навсегда исключает колонку из компактизации ширины. Используется для колонки-компенсатора
        /// заголовка (<c>HeaderOffsetColumn</c>), ширина которой целиком управляется
        /// <c>AlignHeaderToData</c>: она не является колонкой данных и не должна масштабироваться
        /// коэффициентом компактности — иначе после первичного применения компакт-режима (когда
        /// компенсатор уже получил ненулевую ширину) строки «разъезжаются» по горизонтали и
        /// выравнивание восстанавливается лишь повторным переключением тумблера (issue #214).
        /// Пометка хранится в <see cref="_excludedCompactColumns"/> и потому действует на все
        /// последующие проходы <c>ApplyCompact</c>/<c>ApplyCompactTree</c>, а не только на текущий.
        /// </summary>
        public static void ForgetCompactWidth(ColumnDefinition column)
        {
            if (column is null)
                return;
            _excludedCompactColumns.Add(column);
            _compactColumn.Remove(column);
        }

        /// <summary>
        /// Запомнены ли метрики хоть одного элемента. Пока ничего не запомнено, возвращать
        /// в обычный режим нечего, и обход поддерева строки можно не делать вовсе.
        /// </summary>
        public static bool HasCompactMetrics
            => !_compactMargin.IsEmpty || !_compactPadding.IsEmpty
               || !_compactFont.IsEmpty || !_compactColumn.IsEmpty;

        /// <summary>
        /// Применяет метрики компактного режима к поддереву в два прохода: сначала по всему
        /// поддереву запоминаются исходные значения, и только потом они масштабируются.
        /// Один проход не годится: WPF (TextBoxBase) копирует Padding поля ввода на внутренний
        /// хост содержимого (PART_ContentHost) локальным значением, поэтому обход сверху вниз
        /// успевал сжать Padding самого поля, копия уходила на хост, и «исходным» у хоста
        /// запоминалось уже сжатое значение. При возврате в обычный режим оно писалось
        /// обратно, поле поиска оставалось ниже на 3,6 точки по высоте, а всё, что под ним,
        /// поднималось выше, чем при чистом запуске (замер: y шапки списка 336 против 342).
        /// Замерено на отдельном стенде; отступ хоста, заданный в шаблоне через
        /// TemplateBinding, возвращается сам — ломалась именно локальная копия Padding
        /// (issue #214).
        /// </summary>
        private static void ApplyCompactElement(DependencyObject d, double factor, double fontFactor)
        {
            if (d is null)
                return;
            // Захват нужен только при сжатии: возврат по построению касается лишь тех
            // элементов, которые уже запомнены. Лишний захват при возврате не только
            // делал бы двойную работу, но и был бы опасен — локальное значение-выражение
            // (DynamicResource) он записал бы константой, оборвав связь с ресурсом.
            if (factor < 1.0)
                CaptureCompactMetrics(d);
            WriteCompactMetrics(d, factor, fontFactor);
        }

        /// <summary>
        /// Первый проход: запоминает исходные метрики поддерева, ничего не меняя.
        /// </summary>
        private static void CaptureCompactMetrics(DependencyObject d)
        {
            if (d is FrameworkElement fe && IsCompactable(fe, FrameworkElement.MarginProperty)
                && !_compactMargin.ContainsKey(fe))
            {
                _compactMargin[fe] = new CompactMetric(fe.Margin, IsLocalValue(fe, FrameworkElement.MarginProperty));
            }

            if (d is Control c && IsCompactable(c, Control.PaddingProperty)
                && !_compactPadding.ContainsKey(c))
            {
                _compactPadding[c] = new CompactMetric(c.Padding, IsLocalValue(c, Control.PaddingProperty));
            }

            if (d.ReadLocalValue(TextElement.FontSizeProperty) is double fontVal && fontVal > 0
                && !_compactFont.ContainsKey(d))
            {
                _compactFont[d] = fontVal;
            }

            if (d is Grid grid)
            {
                foreach (var cd in grid.ColumnDefinitions)
                {
                    if (!IsCompactableColumn(cd) || _compactColumn.ContainsKey(cd))
                        continue;
                    // Нулевую колонку-компенсатор (сдвиг вложенности групп) не трогаем:
                    // она обязана оставаться 0 (в строке базы — всегда, см. MainWindow.xaml;
                    // в заголовке ширину выставляет AlignHeaderToData), иначе компактизация
                    // принудительно задавала бы ей минимум 32 точки и строки «уезжали» бы
                    // по горизонтали относительно заголовка (issue #214).
                    var width = cd.Width.Value;
                    if (width <= 0)
                        continue;
                    _compactColumn[cd] = width;
                }
            }

            int count = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < count; i++)
                CaptureCompactMetrics(VisualTreeHelper.GetChild(d, i));
        }

        /// <summary>
        /// Второй проход: масштабирует запомненные метрики (коэффициент меньше единицы)
        /// либо возвращает их в обычный режим (коэффициент равен единице).
        /// </summary>
        private static void WriteCompactMetrics(DependencyObject d, double factor, double fontFactor)
        {
            var compacting = factor < 1.0;

            if (d is FrameworkElement fe && _compactMargin.TryGetValue(fe, out var margin))
            {
                if (compacting)
                {
                    fe.Margin = ScaleThickness(margin.Value, factor);
                }
                else
                {
                    RestoreThickness(fe, FrameworkElement.MarginProperty, margin);
                    _compactMargin.Remove(fe);
                }
            }

            if (d is Control c && _compactPadding.TryGetValue(c, out var padding))
            {
                if (compacting)
                {
                    c.Padding = ScaleThickness(padding.Value, factor);
                }
                else
                {
                    RestoreThickness(c, Control.PaddingProperty, padding);
                    _compactPadding.Remove(c);
                }
            }

            // Шрифт: масштабируем только явно заданные значения (включая базовый шрифт
            // окна и заголовки). Унаследованные элементы автоматически следуют за предком,
            // поэтому после масштабирования базы все заголовки и строки уменьшаются.
            if (_compactFont.TryGetValue(d, out var origFont))
            {
                // Минимум 8 — ограничение сжатия, а не восстановления: при возврате размер
                // должен стать ровно исходным, даже если пользователь выставил меньше.
                d.SetValue(TextElement.FontSizeProperty,
                    compacting ? Math.Max(origFont * fontFactor, 8) : origFont);
                if (!compacting)
                    _compactFont.Remove(d);
            }

            if (d is Grid grid)
            {
                foreach (var cd in grid.ColumnDefinitions)
                {
                    if (!_compactColumn.TryGetValue(cd, out var origWidth))
                        continue;
                    // Минимум 32 точки — ограничение сжатия, а не восстановления: при возврате
                    // в обычный режим колонка должна получить ровно своё исходное значение,
                    // иначе колонка уже 32 точек возвращается шире, чем была (issue #214).
                    cd.Width = new GridLength(compacting ? Math.Max(origWidth * factor, 32) : origWidth);
                    if (!compacting)
                        _compactColumn.Remove(cd);
                }
            }

            int count = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < count; i++)
                WriteCompactMetrics(VisualTreeHelper.GetChild(d, i), factor, fontFactor);
        }

        /// <summary>
        /// Можно ли масштабировать отступ этого элемента. Значение, заданное привязкой
        /// (Binding, MultiBinding), не трогаем — то же правило, что и для ширины колонки.
        /// Причин две, обе проверены замером на живом окне (issue #214).
        ///
        /// Во-первых, запись отступа локальным значением снимает привязку: компенсаторы
        /// вложенности (GroupOffsetConverter у заголовка группы, LevelToThicknessConverter
        /// у подсветки, названия базы и кнопки разворота) замирают в том значении, которое
        /// было снято при первом проходе, и перестают отвечать на смену уровня, HasItems
        /// и самого компактного режима. Отсюда и зависимость от пути: строки, созданные
        /// до прохода, и строки, созданные после (виртуализация, пересборка дерева при
        /// поиске, запуск с уже включённым компактным режимом), получали разные отступы.
        ///
        /// Во-вторых, масштабировать эти компенсаторы и не нужно: они считаются от ширины
        /// кнопки разворота (Width="26" в MainWindow.xaml), которая коэффициентом
        /// компактности не масштабируется. Сжатый компенсатор переставал с ней совпадать,
        /// и заголовок группы уезжал относительно строки базы. Цена решения: шаг вложенности
        /// (18 точек на уровень) и зазор подсветки (8 точек) в компактном режиме тоже
        /// остаются обычного размера — плотность по горизонтали не меняется, зато все
        /// формулы согласованы между собой.
        ///
        /// Правило не покрывает TemplateBinding: у него не BindingExpressionBase, и
        /// GetBindingExpressionBase возвращает null. Такие отступы по-прежнему
        /// масштабируются, а возвращаются через ClearValue (источник значения —
        /// ParentTemplate), то есть от пути не зависят.
        /// </summary>
        private static bool IsCompactable(DependencyObject d, DependencyProperty property)
            => BindingOperations.GetBindingExpressionBase(d, property) is null;

        /// <summary>
        /// Можно ли масштабировать ширину колонки: звёздочку и Auto не трогаем, привязанную —
        /// тоже (иначе теряется живое обновление при перетаскивании разделителя), а колонка-
        /// компенсатор заголовка исключена навсегда через <see cref="ForgetCompactWidth"/>.
        /// </summary>
        private static bool IsCompactableColumn(ColumnDefinition cd)
        {
            if (_excludedCompactColumns.Contains(cd))
                return false;
            if (cd.Width.IsStar || cd.Width.IsAuto)
                return false;
            return BindingOperations.GetBindingExpressionBase(cd, ColumnDefinition.WidthProperty) is null;
        }

        private static Thickness ScaleThickness(Thickness t, double factor)
            => new(t.Left * factor, t.Top * factor, t.Right * factor, t.Bottom * factor);

        /// <summary>
        /// Было ли значение свойства задано локально (в разметке элемента), а не стилем,
        /// его триггером или шаблоном. От этого зависит, как возвращать обычный режим.
        /// </summary>
        private static bool IsLocalValue(DependencyObject d, DependencyProperty property)
            => DependencyPropertyHelper.GetValueSource(d, property).BaseValueSource
               == BaseValueSource.Local;

        /// <summary>
        /// Возвращает отступ в обычный режим: локальное значение пишем обратно, значение
        /// из стиля или триггера снимаем, чтобы стиль снова стал хозяином свойства.
        /// </summary>
        private static void RestoreThickness(DependencyObject d, DependencyProperty property, CompactMetric metric)
        {
            if (metric.WasLocal)
                d.SetValue(property, metric.Value);
            else
                d.ClearValue(property);
        }

        /// <summary>Возвращает встроенную схему по имени темы («Light»/«Dark») или null.</summary>
        public static ColorScheme? GetBuiltInScheme(string themeName)
        {
            return themeName == DarkThemeName ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        }

        /// <summary>
        /// Накладывает цвета схемы на загруженный словарь темы: для каждого цвета
        /// обновляется ресурс Color и (если есть) одноимённый SolidColorBrush; для
        /// ключей, оканчивающихся на «Brush», обновляется непосредственно кисть.
        /// </summary>
        private static void ApplyColors(ResourceDictionary dict, ColorScheme scheme, bool dark)
        {
            foreach (var kvp in scheme.Palette(dark))
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    continue;
                if (!TryParseColor(kvp.Value, out var color))
                    continue;

                if (kvp.Key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    if (dict.Contains(kvp.Key))
                        dict[kvp.Key] = new SolidColorBrush(color);
                }
                else
                {
                    if (dict.Contains(kvp.Key))
                        dict[kvp.Key] = color;

                    // Одноимённая кисть цвета: TextPrimaryColor -> TextPrimaryBrush.
                    var brushKey = kvp.Key + "Brush";
                    if (dict.Contains(brushKey))
                        dict[brushKey] = new SolidColorBrush(color);

                    // Акцентная кисть темы называется AccentBrush (без фрагмента «Color»),
                    // поэтому generic-правило «ключ + "Brush"» выше её не находит (искало бы
                    // AccentColorBrush). Задаём её напрямую конкретной кистью: иначе активная
                    // шапка главного окна, ссылающаяся на AccentBrush через DynamicResource,
                    // теряет акцентную заливку и остаётся бесцветной на стеклянном фоне DWM.
                    if (string.Equals(kvp.Key, "AccentColor", StringComparison.OrdinalIgnoreCase)
                        && dict.Contains("AccentBrush"))
                    {
                        dict["AccentBrush"] = new SolidColorBrush(color);
                    }
                }
            }

            // Контрастный текст на акцентной и на выбранной (TreeSelected) заливке
            // выбирается по WCAG-контрасту с фактическим фоном (issue #26): у светлого
            // акцента (например #FFB300) — чёрный, у тёмного пользовательского акцента
            // (например #13522C) — белый. Иначе шапки окон, кнопки «на акценте» и
            // выбранная вкладка левой панели настроек теряют читаемость в тёмной теме.
            SetContrastBrush(dict, scheme, dark, "AccentTextBrush", "AccentColor");
            SetContrastBrush(dict, scheme, dark, "TreeSelectedTextBrush", "TreeSelectedColor");
            // Текст на плашке избранного (issue #61): контраст по WCAG к фактическому
            // FavoriteColor, чтобы номера Alt+N и подписи бейджей были читаемыми
            // и в тёмной теме, и в пользовательских схемах с тёмным «янтарём».
            SetContrastBrush(dict, scheme, dark, "FavoriteTextBrush", "FavoriteColor");

            // Текст на акцентных кнопках и плитках (issue #26): из палитры TextOnAccentColor
            // приходит фиксированный белый и теряет читаемость на тёмных акцентах
            // пользовательских схем (например #AF6B1D — белый даёт лишь 4.25:1).
            // Пересчитываем по WCAG, как AccentTextBrush.
            SetContrastBrush(dict, scheme, dark, "TextOnAccentBrush", "AccentColor");
            // Текст на тёмных панелях (статус-бар, меню выбора окна): контраст по WCAG
            // к фактическому SidebarColor — SidebarBrush всегда тёмный, но пользовательская
            // схема может его осветлить (issue #26).
            SetContrastBrush(dict, scheme, dark, "SidebarTextBrush", "SidebarColor");
        }

        /// <summary>Выставляет кисть, контрастную заданному цвету палитры (WCAG 4.5:1).</summary>
        private static void SetContrastBrush(ResourceDictionary dict, ColorScheme scheme, bool dark,
            string brushKey, string colorKey)
        {
            var hex = scheme.PaletteValue(dark, colorKey);
            if (string.IsNullOrWhiteSpace(hex) || !TryParseColor(hex, out _))
                return;
            if (TryParseColor(ColorScheme.ContrastTextHex(hex), out var color))
                dict[brushKey] = new SolidColorBrush(color);
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
            }
        }

        // ---- Управление пользовательскими схемами ----

        /// <summary>Возвращает список всех доступных схем: встроенные + пользовательские.</summary>
        public static List<ColorScheme> EnumerateAllSchemes()
        {
            var result = new List<ColorScheme>
            {
                ColorScheme.CreateLight(),
                ColorScheme.CreateDark()
            };
            result.AddRange(LoadCustomSchemes());
            return result;
        }

        /// <summary>Загружает пользовательские схемы из каталога пользователя.</summary>
        public static List<ColorScheme> LoadCustomSchemes()
        {
            var result = new List<ColorScheme>();
            if (!Directory.Exists(CustomSchemesDirectory))
                return result;

            foreach (var file in Directory.GetFiles(CustomSchemesDirectory, "*.json"))
            {
                try
                {
                    var scheme = ColorScheme.FromJson(File.ReadAllText(file));
                    if (scheme is not null && !string.IsNullOrWhiteSpace(scheme.Name))
                    {
                        // Сохраняем название по имени файла, если имя в файле отсутствует.
                        if (string.IsNullOrWhiteSpace(scheme.Name) || scheme.Name == "Light" || scheme.Name == "Dark")
                            scheme.Name = Path.GetFileNameWithoutExtension(file);

                        // Миграция (issue #376): имя пользовательской схемы не должно совпадать
                        // с именем встроенной полноценной темы — иначе встроенная тема затирает
                        // пользовательскую. Переименовываем файл и внутреннее имя.
                        var resolved = ThemeFeatureMap.ResolveCustomSchemeName(scheme.Name);
                        if (!string.Equals(resolved, scheme.Name, StringComparison.Ordinal))
                        {
                            scheme.Name = resolved;
                            TryMigrateSchemeFile(file, scheme);
                        }

                        result.Add(scheme);
                    }
                }
                catch
                {
                    // Пропускаем повреждённые файлы схем.
                }
            }
            return result;
        }

        /// <summary>Ищет пользовательскую схему по имени (с учётом регистра).</summary>
        public static ColorScheme? FindCustomScheme(string name)
        {
            return LoadCustomSchemes().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Сохраняет пользовательскую схему в каталог пользователя. Если имя схемы
        /// совпадает с именем встроенной полноценной темы (issue #376), оно заменяется
        /// на безопасное (с суффиксом « (пользовательская)»), чтобы встроенная тема
        /// не затирала пользовательскую и наоборот.
        /// </summary>
        public static void SaveCustomScheme(ColorScheme scheme)
        {
            if (scheme is null || string.IsNullOrWhiteSpace(scheme.Name))
                return;

            scheme.Name = ThemeFeatureMap.ResolveCustomSchemeName(scheme.Name);

            Directory.CreateDirectory(CustomSchemesDirectory);
            var file = Path.Combine(CustomSchemesDirectory, SafeFileName(scheme.Name) + ".json");
            File.WriteAllText(file, scheme.ToJson());
        }

        /// <summary>
        /// Переписывает файл схемы под её текущим (безопасным) именем и удаляет старый
        /// файл, если имя изменилось. Ошибки записи/удаления не критичны: схема всё
        /// равно попадёт в список под новым именем.
        /// </summary>
        private static void TryMigrateSchemeFile(string oldFile, ColorScheme scheme)
        {
            try
            {
                var newFile = Path.Combine(CustomSchemesDirectory, SafeFileName(scheme.Name) + ".json");
                if (string.Equals(oldFile, newFile, StringComparison.Ordinal))
                    return;
                File.WriteAllText(newFile, scheme.ToJson());
                if (File.Exists(oldFile))
                    File.Delete(oldFile);
            }
            catch
            {
                // Миграция не критична: при ошибке схема останется под старым именем.
            }
        }

        /// <summary>Удаляет пользовательскую схему по имени. Возвращает true, если файл удалён.</summary>
        public static bool DeleteCustomScheme(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            var file = Path.Combine(CustomSchemesDirectory, SafeFileName(name) + ".json");
            if (File.Exists(file))
            {
                File.Delete(file);
                return true;
            }
            return false;
        }

        /// <summary>Выгружает схему в указанный файл JSON.</summary>
        public static void ExportScheme(ColorScheme scheme, string filePath)
        {
            if (scheme is null)
                throw new ArgumentNullException(nameof(scheme));
            File.WriteAllText(filePath, scheme.ToJson());
        }

        /// <summary>Загружает схему из файла JSON. Возвращает null при ошибке.</summary>
        public static ColorScheme? ImportScheme(string filePath)
        {
            if (!File.Exists(filePath))
                return null;
            return ColorScheme.FromJson(File.ReadAllText(filePath));
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var result = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(result) ? "Scheme" : result;
        }
    }
}
#endif
