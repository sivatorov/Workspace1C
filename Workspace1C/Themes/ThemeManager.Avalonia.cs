#if LINUX
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes;
using Avalonia.VisualTree;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Themes
{
    /// <summary>
    /// Avalonia-версия ThemeManager (Linux). Управляет переключением светлой/тёмной темы
    /// и цветовых схем: задаёт RequestedThemeVariant и накладывает цвета схемы как ресурсы
    /// приложения (Application.Resources). Пользовательские схемы хранятся в JSON-файлах.
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

        /// <summary>
        /// Применяет цветовую схему, не меняя базовый вариант темы: накладывает палитру,
        /// соответствующую текущему варианту (<see cref="CurrentTheme"/>), на ресурсы приложения.
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
            ApplyColors(app, scheme, CurrentTheme == DarkThemeName);
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
            if (Application.Current is { } app)
                app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplyScheme(CurrentScheme);
            // issue #26 (1.0.1.6): ресурсы кнопок окна должны обновляться при КАЖДОМ
            // применении темы, иначе после переключения Light/Dark остаётся подложка
            // от прошлой темы (ApplyWindowButtonStyle вызывался только из ApplyFullTheme
            // и ReapplyThemeFeatures).
            ApplyWindowButtonStyle();
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
        /// <see cref="ThemeFeatureMap"/>; сами кнопки окна начнут их читать в Задаче 4.
        /// </summary>
        private static void ApplyWindowButtonStyle()
        {
            if (Application.Current is not { } app)
                return;
            // issue #26: в тёмной теме подложка наведения кнопок окна — полупрозрачный
            // белый, а не «дневная» яркая заливка (#E2E8F0/#FFF3CD — выглядела белой).
            var dark = CurrentTheme == DarkThemeName;
            var metrics = ThemeFeatureMap.ButtonMetrics(CurrentWindowButtonStyle, dark);
            app.Resources[ThemeFeatureMap.WindowButtonHoverBrushKey] = ParseBrush(metrics.HoverHex);
            // issue #26 (7OH, 2026-10-29): подложка наведения кнопок окна при НЕАКТИВНОМ
            // окне — приглушённая (тёмный slate в тёмной теме), чтобы на тёмной шапке
            // карточки кнопки не «светились белым».
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
            app.Resources[ThemeFeatureMap.WindowButtonCornerRadiusKey] = metrics.CornerRadius;
            app.Resources[ThemeFeatureMap.WindowButtonSizeKey] = metrics.Size;
        }

        /// <summary>
        /// Выставляет ресурс активного набора иконок (<see cref="IconSetNameKey"/>) согласно
        /// <see cref="CurrentIconSet"/>. Оба словаря иконок (Icons.axaml — Fluent и
        /// IconsOutline.axaml — Outline) подключены в ресурсы приложения; резолв
        /// (IconHelper) читает этот ресурс и для Outline ищет ключ с суффиксом
        /// ".Outline", при отсутствии — бесшовный fallback на Fluent (Задача 5).
        /// </summary>
        private static void ApplyIconSet()
        {
            if (Application.Current is not { } app)
                return;
            app.Resources[ThemeFeatureMap.IconSetNameKey] = ThemeFeatureMap.IconSetId(CurrentIconSet);

            // Живое переключение (0.3.12.60): базовые ключи (IconAdd, GlyphWindowClose, …)
            // переопределяются в собственных ресурсах приложения геометрией активного набора —
            // DynamicResource-потребители (axaml) перерисовываются сразу; иконки, собранные
            // кодом (IconHelper), перечитывают ключ по событию ThemeChanged.
            foreach (var key in AppliedIconOverrides)
                app.Resources.Remove(key);
            AppliedIconOverrides.Clear();
            foreach (var pair in ThemeFeatureMap.IconOverrides(CurrentIconSet, BaseIconKeys(app),
                         key => app.TryGetResource(key, null, out var v) && v is not null))
            {
                if (app.TryGetResource(pair.Value, null, out var resource) && resource is not null)
                {
                    app.Resources[pair.Key] = resource;
                    AppliedIconOverrides.Add(pair.Key);
                }
            }

            // «Блик» поверх иконок (Windows XP) и уведомление подписчиков набора.
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
        /// Ключи геометрий базового словаря Themes/Icons.axaml (Icon*, Glyph*), собранные один
        /// раз: именно их набор иконок переопределяет при переключении.
        /// </summary>
        private static IEnumerable<string> BaseIconKeys(Application app)
        {
            if (_baseIconKeys is not null)
                return _baseIconKeys;
            var keys = new List<string>();
            foreach (var provider in app.Resources.MergedDictionaries)
            {
                if (provider is not Avalonia.Markup.Xaml.Styling.ResourceInclude include ||
                    include.Source is null ||
                    !include.Source.ToString().EndsWith("Themes/Icons.axaml", StringComparison.OrdinalIgnoreCase) ||
                    include.Loaded is not IResourceDictionary dict)
                    continue;
                foreach (var pair in dict)
                {
                    if (pair.Key is string name && pair.Value is Geometry)
                        keys.Add(name);
                }
            }
            _baseIconKeys = keys;
            return keys;
        }

        /// <summary>Вертикальный градиент «блика» иконки (верх → низ, гаснет к доле stop).</summary>
        private static IBrush CreateGlossBrush(string top, string bottom, double stop)
        {
            TryParseColor(top, out var topColor);
            TryParseColor(bottom, out var bottomColor);
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(topColor, 0),
                    new GradientStop(bottomColor, stop),
                    new GradientStop(bottomColor, 1)
                }
            };
        }

        /// <summary>Создаёт кисть из шестнадцатеричного цвета (#RRGGBB), прозрачную при ошибке.</summary>
        private static IBrush ParseBrush(string hex)
            => TryParseColor(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

        /// <summary>Возвращает встроенную схему по имени темы («Light»/«Dark») или null.</summary>
        public static ColorScheme? GetBuiltInScheme(string themeName)
            => themeName == DarkThemeName ? ColorScheme.CreateDark() : ColorScheme.CreateLight();

        /// <summary>Шрифт интерфейса по умолчанию.</summary>
        public const string DefaultFontFamily = "Segoe UI";
        public const double DefaultFontSize = 13;
        public const string DefaultFontWeight = "Normal";
        public const string DefaultFontStyle = "Normal";

        /// <summary>
        /// Применяет настройки шрифта к элементу управления. Свойства шрифта в Avalonia
        /// наследуются, поэтому распространяются на дочерние элементы.
        /// </summary>
        public static void ApplyFont(Control? target,
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
                TextElement.SetFontWeight(target, string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                    ? FontWeight.Bold : FontWeight.Normal);
                TextElement.SetFontStyle(target, string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                    ? FontStyle.Italic : FontStyle.Normal);
            }
            catch
            {
                // Игнорируем некорректные настройки шрифта (например, несуществующее семейство).
            }
        }

        /// <summary>
        /// Применяет настройки шрифта ко всем открытым окнам приложения.
        /// </summary>
        public static void ApplyFontToAllWindows(
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            // Ресурс UiFontFamily (аудит UI 1.0.1.10, п. 1.9): окна следуют ему через
            // стиль в App.axaml, поэтому новые диалоги, открытые после смены шрифта,
            // тоже наследуют выбор пользователя без перезапуска.
            if (Application.Current is { } app)
            {
                var family = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
                app.Resources["UiFontFamily"] = new FontFamily(family);
            }
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            foreach (Window window in desktop.Windows)
                ApplyFont(window, fontFamily, fontSize, fontWeight, fontStyle);
            ApplyFontToPopups(fontFamily, fontSize, fontWeight, fontStyle);
        }

        /// <summary>Стиль приложения, задающий шрифт всем корням, включая всплывающие.</summary>
        private static Style? _popupFontStyle;

        /// <summary>
        /// Содержимое меню, подсказок и выпадающих списков живёт в отдельном
        /// PopupRoot и потомком окна не является, поэтому наследуемый шрифт окна
        /// до него не доходит и обход визуальных детей его не посещает.
        /// В WPF наследование идёт по логическому дереву и такие корни
        /// охватывает само. Здесь тот же охват даёт стиль приложения на TopLevel.
        /// </summary>
        public static void ApplyFontToPopups(string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (Application.Current is not { } app)
                return;
            try
            {
                var family = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
                var size = fontSize > 0 ? fontSize : DefaultFontSize;
                // Селектор именно Is, а не OfType: OfType в Avalonia совпадает
                // только с точным типом, а всплывающий корень это наследник
                // TopLevel, и по OfType стиль до него не доходил.
                var style = new Style(x => x.Is<TopLevel>())
                {
                    Setters =
                    {
                        new Setter(TextElement.FontFamilyProperty, new FontFamily(family)),
                        new Setter(TextElement.FontSizeProperty, size),
                        new Setter(TextElement.FontWeightProperty,
                            string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                                ? FontWeight.Bold : FontWeight.Normal),
                        new Setter(TextElement.FontStyleProperty,
                            string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                                ? FontStyle.Italic : FontStyle.Normal)
                    }
                };
                if (_popupFontStyle is not null)
                    app.Styles.Remove(_popupFontStyle);
                app.Styles.Add(style);
                _popupFontStyle = style;
            }
            catch
            {
                // Настройка шрифта не должна ронять приложение.
            }
        }

        /// <summary>
        /// Применяет индивидуальные настройки шрифта отдельных областей интерфейса
        /// к главному окну (Avalonia): базовый шрифт всего окна + по типам/именам
        /// областей (кнопки, поля ввода, дерево групп, правая панель, статус-бар).
        /// Порождает те же области, что и WPF-версия <see cref="ThemeManager"/>.
        /// </summary>
        public static void ApplyElementFonts(MainWindow window, Dictionary<string, ElementFontSettings>? elementFonts)
        {
            if (window is null)
                return;
            elementFonts ??= new Dictionary<string, ElementFontSettings>();

            // «По умолчанию» распространяется на всё окно (наследуется дочерними элементами).
            var def = GetScope(elementFonts, FontDefault);
            if (def is not null && def.FontSize > 0)
            {
                ApplyFont(window, def.FontFamily, def.FontSize, def.FontWeight, def.FontStyle);
                // Кнопка «Применить» идёт этим путём, а не через ApplyFontToAllWindows,
                // поэтому всплывающие корни надо обновить и здесь: иначе меню
                // и подсказки остаются на прежнем шрифте до перезапуска.
                ApplyFontToPopups(def.FontFamily, def.FontSize, def.FontWeight, def.FontStyle);
            }

            ApplyFontToType(window, typeof(Avalonia.Controls.Button), GetScope(elementFonts, FontButtons));
            ApplyFontToType(window, typeof(Avalonia.Controls.TextBox), GetScope(elementFonts, FontInputs));
            ApplyFontToType(window, typeof(Workspace1C.Controls.LeveledTreeView), GetScope(elementFonts, FontList));
            // Списки ввода в разметке WPF получают шрифт области «Поля ввода»
            // наравне с текстовыми полями (ThemeManager.cs:234).
            ApplyFontToType(window, typeof(Avalonia.Controls.ComboBox), GetScope(elementFonts, FontInputs));
            // Область «Шапка списка» была в списке настроек, но ни к чему
            // не применялась: имя элемента появилось только сейчас.
            // Область «Вкладки» в Linux не применялась вовсе: она есть в списке
            // областей и в настройках, но здесь для неё не было ни одной строки,
            // тогда как Windows-версия применяет её к TabsPanel (ThemeManager.cs:236).
            ApplyFontToNamed(window, "TabsPanel", GetScope(elementFonts, FontTabs));
            ApplyFontToNamed(window, "HeaderGrid", GetScope(elementFonts, FontListHeader));
            ApplyFontToNamed(window, "RightPanelBorder", GetScope(elementFonts, FontRightPanel));
            ApplyFontToNamed(window, "StatusBarBorder", GetScope(elementFonts, FontStatusBar));
        }

        private static ElementFontSettings? GetScope(Dictionary<string, ElementFontSettings> dict, string key)
            => dict.TryGetValue(key, out var fs) ? fs : null;

        /// <summary>Применяет шрифт области ко всем элементам заданного типа в дереве визуальных элементов.</summary>
        private static void ApplyFontToType(Control root, Type type, ElementFontSettings? fs)
        {
            if (fs is null || fs.FontSize <= 0)
                return;
            if (type.IsInstanceOfType(root))
                ApplyFont(root, fs.FontFamily, fs.FontSize, fs.FontWeight, fs.FontStyle);
            foreach (var child in root.GetVisualChildren())
                if (child is Control c)
                    ApplyFontToType(c, type, fs);
        }

        /// <summary>Применяет шрифт области к элементу с заданным именем (и его потомкам).</summary>
        private static void ApplyFontToNamed(Control root, string name, ElementFontSettings? fs)
        {
            if (fs is null || fs.FontSize <= 0)
                return;
            if (string.Equals(root.Name, name, StringComparison.Ordinal))
            {
                ApplyFontToSubtree(root, fs);
                return;
            }
            foreach (var child in root.GetVisualChildren())
                if (child is Control c)
                    ApplyFontToNamed(c, name, fs);
        }

        /// <summary>
        /// Применяет шрифт области ко всему поддереву, а не только к его корню.
        /// Одного наследования мало: у кнопок и полей размер шрифта задан стилем
        /// темы, а стилевое значение сильнее унаследованного, поэтому настройка
        /// области до подписей внутри не доходила.
        /// </summary>
        private static void ApplyFontToSubtree(Control root, ElementFontSettings fs)
        {
            ApplyFont(root, fs.FontFamily, fs.FontSize, fs.FontWeight, fs.FontStyle);
            foreach (var child in root.GetVisualChildren())
                if (child is Control c)
                    ApplyFontToSubtree(c, fs);
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
        /// Накладывает цвета схемы на ресурсы приложения: для каждого цвета обновляется
        /// ресурс Color и (если есть) одноимённый SolidColorBrush; для ключей, оканчивающихся
        /// на «Brush», обновляется непосредственно кисть. Берётся палитра, соответствующая
        /// варианту темы (<paramref name="dark"/>).
        /// </summary>
        private static void ApplyColors(Application app, ColorScheme scheme, bool dark)
        {
            foreach (var kvp in scheme.Palette(dark))
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || !TryParseColor(kvp.Value, out var color))
                    continue;

                if (kvp.Key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    app.Resources[kvp.Key] = new SolidColorBrush(color);
                }
                else
                {
                    app.Resources[kvp.Key] = color;
                    app.Resources[kvp.Key + "Brush"] = new SolidColorBrush(color);
                }
            }

            // Контрастный текст на акцентной и на выбранной (TreeSelected) заливке
            // выбирается по WCAG-контрасту с фактическим фоном (issue #26): у светлого
            // акцента — чёрный, у тёмного пользовательского акцента — белый. Иначе
            // шапки окон, кнопки «на акценте» и выбранная вкладка левой панели
            // настроек теряют читаемость в тёмной теме.
            SetContrastBrush(app, scheme, dark, "AccentTextBrush", "AccentColor");
            SetContrastBrush(app, scheme, dark, "TreeSelectedTextBrush", "TreeSelectedColor");

            // Текст на плашке избранного (issue #61): контраст по WCAG к фактическому
            // FavoriteColor — номера Alt+N и подписи бейджей читаемы в обеих темах
            // и в пользовательских схемах с тёмным янтарём.
            SetContrastBrush(app, scheme, dark, "FavoriteTextBrush", "FavoriteColor");

            // Текст на акцентных кнопках и плитках (issue #26): из палитры TextOnAccentColor
            // приходит фиксированный белый и теряет читаемость на тёмных акцентах
            // пользовательских схем (например #AF6B1D — белый даёт лишь 4.25:1).
            // Пересчитываем по WCAG, как AccentTextBrush.
            SetContrastBrush(app, scheme, dark, "TextOnAccentBrush", "AccentColor");
            // Текст на тёмных панелях (статус-бар, меню выбора окна): контраст по WCAG
            // к фактическому SidebarColor — SidebarBrush всегда тёмный, но пользовательская
            // схема может его осветлить (issue #26).
            SetContrastBrush(app, scheme, dark, "SidebarTextBrush", "SidebarColor");

            ApplyFluentAccent(app, scheme, dark);
        }

        /// <summary>Выставляет кисть, контрастную заданному цвету палитры (WCAG 4.5:1).</summary>
        private static void SetContrastBrush(Application app, ColorScheme scheme, bool dark,
            string brushKey, string colorKey)
        {
            var hex = scheme.PaletteValue(dark, colorKey);
            if (string.IsNullOrWhiteSpace(hex) || !TryParseColor(hex, out _))
                return;
            if (TryParseColor(ColorScheme.ContrastTextHex(hex), out var color))
                app.Resources[brushKey] = new SolidColorBrush(color);
        }

        /// <summary>
        /// Отдаёт акцент схемы стандартным контролам Fluent (переключатели,
        /// флажки, ползунки): свой цвет они берут из SystemAccentColor, и без
        /// этого в янтарном интерфейсе они оставались синими. Светлые и тёмные
        /// оттенки Fluent использует для состояний, поэтому считаются, а не
        /// подменяются одним цветом.
        /// </summary>
        private static void ApplyFluentAccent(Application app, ColorScheme scheme, bool dark)
        {
            var accent = ResolveAccent(scheme, dark);

            app.Resources["SystemAccentColor"] = accent;
            app.Resources["SystemAccentColorLight1"] = Shade(accent, 0.10);
            app.Resources["SystemAccentColorLight2"] = Shade(accent, 0.20);
            app.Resources["SystemAccentColorLight3"] = Shade(accent, 0.30);
            app.Resources["SystemAccentColorDark1"] = Shade(accent, -0.10);
            app.Resources["SystemAccentColorDark2"] = Shade(accent, -0.20);
            app.Resources["SystemAccentColorDark3"] = Shade(accent, -0.30);
        }

        /// <summary>
        /// Акцент активной палитры схемы. Если в палитре его нет — акцент по умолчанию
        /// для этого варианта; иначе без запасного значения у стандартных контролов
        /// остался бы акцент предыдущей схемы.
        /// </summary>
        private static Color ResolveAccent(ColorScheme scheme, bool dark)
        {
            return TryParseColor(scheme.PaletteValue(dark, "AccentColor"), out var accent)
                ? accent
                : Colors.DodgerBlue;
        }

        /// <summary>Осветлённый или затемнённый оттенок цвета по светлоте HSL.</summary>
        private static Color Shade(Color color, double delta)
        {
            var hsl = color.ToHsl();
            var lightness = Math.Clamp(hsl.L + delta, 0, 1);
            return new HslColor(hsl.A, hsl.H, hsl.S, lightness).ToRgb();
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try { color = Color.Parse(hex); return true; }
            catch { color = Colors.Transparent; return false; }
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

            string[] files;
            try
            {
                files = Directory.GetFiles(CustomSchemesDirectory, "*.json");
            }
            catch
            {
                // Каталог схем может быть недоступен для чтения: список тем тогда пустой,
                // но окно настроек всё равно открывается.
                return result;
            }

            foreach (var file in files)
            {
                try
                {
                    var scheme = ColorScheme.FromJson(File.ReadAllText(file));
                    if (scheme is not null && !string.IsNullOrWhiteSpace(scheme.Name))
                    {
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
            => LoadCustomSchemes().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

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

        /// <summary>
        /// Переименовывает пользовательскую схему: сначала пишет файл под новым именем,
        /// затем удаляет старый и только если это другой файл. Порядок важен: при
        /// обратном схема пропадала бы при любой ошибке записи.
        /// </summary>
        public static void RenameCustomScheme(ColorScheme scheme, string oldName)
        {
            if (scheme is null || string.IsNullOrWhiteSpace(scheme.Name) || string.IsNullOrWhiteSpace(oldName))
                return;

            var oldFile = Path.Combine(CustomSchemesDirectory, SafeFileName(oldName) + ".json");
            var newFile = Path.Combine(CustomSchemesDirectory, SafeFileName(scheme.Name) + ".json");
            SaveCustomScheme(scheme);
            if (!string.Equals(oldFile, newFile, StringComparison.Ordinal) && File.Exists(oldFile))
                File.Delete(oldFile);
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