using Workspace1C.Models;

namespace Workspace1C.Themes;

/// <summary>
/// Чистая (без UI) логика полноценных тем (Задача 2): выбор определения по
/// идентификатору с возвратом к светлой теме, копирование темы с другим базовым
/// вариантом и маппинг стиля кнопок окна / набора иконок в параметры ресурсов
/// приложения. Именно эти части <see cref="ThemeManager"/> (обеих платформ)
/// тестируются без Application.Current.
/// </summary>
public static class ThemeFeatureMap
{
    /// <summary>
    /// Ключи ресурсов приложения, выставляемые по стилю кнопок окна и набору иконок.
    /// Стили кнопок управления окном (WindowControlButton/WindowControlCloseButton)
    /// начнут читать эти ключи в Задаче 4, переключение наборов иконок — в Задаче 5.
    /// </summary>
    public const string WindowButtonHoverBrushKey = "WindowButtonHoverBrush";
    public const string WindowButtonCloseHoverBrushKey = "WindowButtonCloseHoverBrush";
    public const string WindowButtonCornerRadiusKey = "WindowButtonCornerRadius";
    public const string WindowButtonSizeKey = "WindowButtonSize";
    public const string IconSetNameKey = "IconSetName";

    /// <summary>
    /// Ключ кисти подложки кнопки «свернуть» (Задача 3): у macOS-«светофора» это жёлтый
    /// круг #F5BF4F, у остальных стилей — null (используется общая
    /// <see cref="WindowButtonHoverBrushKey"/>).
    /// </summary>
    public const string WindowButtonMinimizeHoverBrushKey = "WindowButtonMinimizeHoverBrush";

    /// <summary>
    /// Ключ кисти подложки кнопки «развернуть» (Задача 3): у macOS-«светофора» это зелёный
    /// круг #59CB59, у остальных стилей — null (используется общая
    /// <see cref="WindowButtonHoverBrushKey"/>).
    /// </summary>
    public const string WindowButtonMaximizeHoverBrushKey = "WindowButtonMaximizeHoverBrush";

    /// <summary>
    /// Ключ кисти подложки кнопок управления окном при НЕАКТИВНОМ окне (issue #26,
    /// комментарий 7OH от 2026-10-29 «Подсветка кнопок управления основным окном
    /// при неактивности - белая подсветка»): шапка неактивного окна залита тёмной
    /// карточкой, и полупрозрачная белая <see cref="WindowButtonHoverBrushKey"/> на ней
    /// выглядит «белой подсветкой с белыми элементами». В тёмной теме неактивная
    /// подложка наведения — приглушённый тёмный slate (#334155), в светлой — прежние
    /// «дневные» подложки стиля.
    /// </summary>
    public const string WindowButtonInactiveHoverBrushKey = "WindowButtonInactiveHoverBrush";

    /// <summary>Все ключи ресурсов полноценной темы (в порядке выдачи).</summary>
    public static readonly string[] AllResourceKeys =
    {
        WindowButtonHoverBrushKey, WindowButtonCloseHoverBrushKey,
        WindowButtonMinimizeHoverBrushKey, WindowButtonMaximizeHoverBrushKey,
        WindowButtonInactiveHoverBrushKey,
        WindowButtonCornerRadiusKey, WindowButtonSizeKey, IconSetNameKey
    };

    /// <summary>
    /// Параметры стиля кнопок управления окном: цвет подложки наведения, цвет подложки
    /// кнопки «закрыть» (в формате #RRGGBB или #AARRGGBB для Glass), радиус скругления
    /// подложки и размер кнопки. Необязательные <see cref="MinimizeHoverHex"/> и
    /// <see cref="MaximizeHoverHex"/> задают цвета «светофора» macOS (жёлтый/зелёный);
    /// у остальных стилей они null — кнопки «свернуть»/«развернуть» используют
    /// <see cref="HoverHex"/>.
    /// </summary>
    public readonly record struct WindowButtonMetrics(
        string HoverHex, string CloseHoverHex, double CornerRadius, double Size,
        string? MinimizeHoverHex = null, string? MaximizeHoverHex = null);

    /// <summary>
    /// Находит определение полноценной темы по идентификатору. Неизвестный или пустой
    /// идентификатор ошибкой не является: применяется светлая тема по умолчанию.
    /// </summary>
    public static ThemeDefinition Resolve(string? id)
        => ThemeCatalog.Find(id) ?? ThemeCatalog.Find("light")!;

    /// <summary>
    /// Копия темы с другим базовым вариантом: тот же Id, имя, стиль кнопок, набор иконок
    /// и акцент, изменённый <see cref="ThemeDefinition.IsDark"/> и клонированная схема
    /// (чтобы применение палитры не мутировало объекты <see cref="ThemeCatalog.BuiltIn"/>).
    /// </summary>
    public static ThemeDefinition WithBaseVariant(ThemeDefinition? theme, bool isDark)
    {
        var source = theme ?? Resolve(null);
        return new ThemeDefinition
        {
            Id = source.Id,
            Name = source.Name,
            IsDark = isDark,
            Scheme = source.Scheme?.Clone()
                     ?? (isDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight()),
            ButtonStyle = source.ButtonStyle,
            Icons = source.Icons,
            AccentColor = source.AccentColor
        };
    }

    /// <summary>
    /// Параметры стиля кнопок управления окном: <see cref="WindowButtonStyle.Fluent"/> —
    /// круглая подложка (нынешний вид), <see cref="WindowButtonStyle.Minimal"/> — тонкая
    /// плоская кнопка почти без подложки, <see cref="WindowButtonStyle.Classic"/> —
    /// прямоугольная кнопка классического вида. Новые стили (Задача 3):
    /// <see cref="WindowButtonStyle.Material"/> — круглые кнопки с Material-подложкой,
    /// <see cref="WindowButtonStyle.MacOS"/> — «светофор» (красная/жёлтая/зелёная кнопки,
    /// цвета жёлтого/зелёного кругов в <see cref="WindowButtonMetrics.MinimizeHoverHex"/>/
    /// <see cref="WindowButtonMetrics.MaximizeHoverHex"/>), <see cref="WindowButtonStyle.WindowsXp"/> —
    /// классические квадратные кнопки, <see cref="WindowButtonStyle.Glass"/> — полупрозрачные
    /// кнопки (#AARRGGBB).
    /// </summary>
    /// <summary>
    /// Подложка наведения кнопок окна в тёмной теме (issue #26, комментарий 7OH
    /// 2026-10-28): дневные непрозрачные подложки (#E2E8F0/#FFF3CD/#EDE7F6/#D6E4F0)
    /// в тёмной теме смотрятся «белой» подсветкой кнопок окна. В тёмной теме кнопки
    /// «свернуть/развернуть/закрыть» получают полупрозрачную белую подложку, как в
    /// Windows 11 / VS Code (цветной круг «светофора» macOS и полупрозрачный Glass
    /// не трогаются).
    /// </summary>
    public const string DarkThemeWindowButtonHoverHex = "#33FFFFFF";

    public static WindowButtonMetrics ButtonMetrics(WindowButtonStyle style)
        => ButtonMetrics(style, dark: false);

    /// <param name="dark">Активна ли тёмная тема (подложка наведения затемняется).</param>
    public static WindowButtonMetrics ButtonMetrics(WindowButtonStyle style, bool dark)
    {
        var metrics = style switch
        {
            WindowButtonStyle.Minimal => new WindowButtonMetrics("#E2E8F0", "#E81123", 4, 40),
            WindowButtonStyle.Classic => new WindowButtonMetrics("#E2E8F0", "#E81123", 0, 34),
            WindowButtonStyle.Material => new WindowButtonMetrics("#EDE7F6", "#E81123", 15, 46),
            WindowButtonStyle.MacOS => new WindowButtonMetrics("#FF5F57", "#FF5F57", 15, 46, "#F5BF4F", "#59CB59"),
            WindowButtonStyle.WindowsXp => new WindowButtonMetrics("#D6E4F0", "#E81123", 0, 34),
            WindowButtonStyle.Glass => new WindowButtonMetrics("#40FFFFFF", "#66E81123", 15, 46),
            _ => new WindowButtonMetrics("#FFF3CD", "#E81123", 15, 46)
        };
        if (dark && style is not WindowButtonStyle.MacOS and not WindowButtonStyle.Glass)
            metrics = metrics with { HoverHex = DarkThemeWindowButtonHoverHex };
        return metrics;
    }

    /// <summary>
    /// Подложка наведения кнопок управления окном при НЕАКТИВНОМ окне (issue #26,
    /// комментарий 7OH от 2026-10-29): шапка неактивного окна залита тёмной карточкой,
    /// и полупрозрачная белая <see cref="DarkThemeWindowButtonHoverHex"/> на ней смотрится
    /// «белой подсветкой с белыми элементами». В тёмной теме неактивная подложка —
    /// приглушённый тёмный slate (как «зебра»/ItemHover), в светлой теме — прежние
    /// «дневные» подложки стиля. <para>
    /// Исправление issue #26 (1.0.1.6): исключение для Glass убрано — полупрозрачно-белая
    /// подложка Glass-кнопок на тёмной неактивной шапке выглядела именно той «белой
    /// подсветкой», на которую жаловался 7OH; тёмный slate применяется и к Glass.
    /// «Светофор» macOS сохраняет собственные цветные подложки (круги всегда цветные —
    /// это не «белая подсветка»).</para>
    /// </summary>
    public const string DarkThemeInactiveWindowButtonHoverHex = "#334155";

    public static string InactiveHoverHex(WindowButtonStyle style, bool dark)
    {
        if (!dark || style is WindowButtonStyle.MacOS)
            return ButtonMetrics(style, dark: false).HoverHex;
        return DarkThemeInactiveWindowButtonHoverHex;
    }

    /// <summary>
    /// Имя набора иконок для ресурса <see cref="IconSetNameKey"/>.
    /// Используется словарями иконок (Задача 2) для выбора активного набора.
    /// Имя равно суффиксу в ключах словаря набора (IconAdd.Material и т.п.).
    /// </summary>
    public static string IconSetId(IconSet icons) => icons switch
    {
        IconSet.Outline => "Outline",
        IconSet.Material => "Material",
        IconSet.MacOS => "MacOS",
        IconSet.WindowsXp => "WindowsXp",
        IconSet.Glass => "Glass",
        _ => "Fluent"
    };

    /// <summary>
    /// Разбирает имя набора из ресурса <see cref="IconSetNameKey"/>; неизвестное или
    /// пустое значение даёт <see cref="IconSet.Fluent"/> (набор по умолчанию).
    /// </summary>
    public static IconSet ParseIconSet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return IconSet.Fluent;
        return name.Trim().ToLowerInvariant() switch
        {
            "outline" => IconSet.Outline,
            "material" => IconSet.Material,
            "macos" => IconSet.MacOS,
            "windowsxp" => IconSet.WindowsXp,
            "glass" => IconSet.Glass,
            _ => IconSet.Fluent
        };
    }

    /// <summary>
    /// Ключ ресурса геометрии иконки для активного набора (Задача 2): для
    /// <see cref="IconSet.Fluent"/> — исходный ключ (Icons.xaml / Icons.axaml),
    /// для остальных — ключ с суффиксом ".Имя" (словарь IconsOutline/Material/
    /// MacOS/WindowsXp/Glass.xaml / .axaml). Все словари подключены в ресурсы
    /// приложения одновременно, поэтому суффикс исключает конфликт ключей.
    /// </summary>
    public static string IconKeyForSet(string key, IconSet set)
        => set == IconSet.Fluent ? key : key + "." + IconSetId(set);

    /// <summary>
    /// Ключи ресурсов для резолва иконки в порядке приоритета: ключ активного набора,
    /// затем исходный ключ (fallback на Fluent — когда варианта набора нет), затем
    /// запасная «папка». Используется конвертером иконок и <c>IconHelper</c> обеих
    /// платформ: отсутствующие в наборе иконки бесшовно фолбэчатся на Fluent.
    /// </summary>
    public static IReadOnlyList<string> IconResolutionCandidates(string key, IconSet set)
    {
        var effective = IconKeyForSet(key, set);
        if (set != IconSet.Fluent && !string.Equals(effective, key, StringComparison.Ordinal))
            return new[] { effective, key, "IconFolder" };
        return new[] { effective, "IconFolder" };
    }

    /// <summary>
    /// Суффикс ключа фонового (полупрозрачного) слоя иконки: у Duotone-набора Glass
    /// рядом с IconAdd.Glass лежит IconAdd.Back.Glass (0.3.12.59). Базовый ключ слоя —
    /// IconAdd.Back; в базовом наборе его нет, поэтому слой рисуется только у Glass.
    /// </summary>
    public const string BackLayerSuffix = ".Back";

    /// <summary>Непрозрачность фонового слоя иконки (как у Phosphor Duotone — 0.2–0.3).</summary>
    public const double BackLayerOpacity = 0.25;

    /// <summary>
    /// Ключ ресурса кисти «блика» поверх иконки (0.3.12.60): у набора Windows XP —
    /// вертикальный градиент от полупрозрачного белого к прозрачному (объёмный «Luna»-вид
    /// любой иконки независимо от её цвета), у остальных наборов — прозрачная кисть.
    /// </summary>
    public const string IconGlossBrushKey = "IconGlossBrush";

    /// <summary>
    /// Цвета «блика» иконки для набора: (верх, низ, доля высоты, где блик гаснет) в
    /// формате #AARRGGBB, либо null — блика нет. Сейчас блик есть только у Windows XP.
    /// </summary>
    public static (string Top, string Bottom, double Stop)? IconGloss(IconSet set) => set switch
    {
        IconSet.WindowsXp => ("#99FFFFFF", "#00FFFFFF", 0.55),
        _ => null
    };

    /// <summary>Ключ фонового слоя иконки: IconAdd → IconAdd.Back.</summary>
    public static string BackLayerKey(string key) => key + BackLayerSuffix;

    /// <summary>
    /// Ключ варианта значка materialDesign:PackIcon в наборе: Pack.&lt;Kind&gt;.&lt;Набор&gt;
    /// (фоновый слой — Pack.&lt;Kind&gt;.Back.&lt;Набор&gt;). Для Fluent возвращает null:
    /// базовый набор рисует собственную геометрию PackIcon.
    /// </summary>
    public static string? PackIconKey(string kind, IconSet set, bool back = false)
    {
        if (set == IconSet.Fluent || string.IsNullOrWhiteSpace(kind))
            return null;
        return "Pack." + kind + (back ? BackLayerSuffix : string.Empty) + "." + IconSetId(set);
    }

    /// <summary>
    /// Переопределения базовых ключей иконок для живого переключения набора (0.3.12.60):
    /// пары (базовый ключ → ключ ресурса набора), которые менеджер тем кладёт в
    /// собственные ресурсы приложения поверх подключённых словарей. Тогда все
    /// потребители DynamicResource (IconAdd, GlyphWindowClose, …) получают геометрию
    /// активного набора сразу, без перезапуска. Для каждого базового ключа берётся
    /// вариант набора (если есть) и его фоновый слой (если есть). Для Fluent список пуст —
    /// прежние переопределения просто снимаются.
    /// </summary>
    /// <param name="set">Активный набор иконок.</param>
    /// <param name="baseKeys">Ключи базового словаря (Icons.xaml / Icons.axaml).</param>
    /// <param name="exists">Проверка наличия ресурса по ключу.</param>
    public static IReadOnlyList<KeyValuePair<string, string>> IconOverrides(
        IconSet set, IEnumerable<string> baseKeys, Func<string, bool> exists)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (set == IconSet.Fluent || baseKeys is null || exists is null)
            return result;
        foreach (var key in baseKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var variant = IconKeyForSet(key, set);
            if (exists(variant))
                result.Add(new KeyValuePair<string, string>(key, variant));
            var back = IconKeyForSet(BackLayerKey(key), set);
            if (exists(back))
                result.Add(new KeyValuePair<string, string>(BackLayerKey(key), back));
        }
        return result;
    }

    /// <summary>
    /// Стили кнопок окна, доступные для переопределения в блоке «Темы» (0.3.12.62),
    /// в стабильном порядке отображения.
    /// </summary>
    public static readonly WindowButtonStyle[] SelectableButtonStyles =
    {
        WindowButtonStyle.Fluent, WindowButtonStyle.Minimal, WindowButtonStyle.Classic,
        WindowButtonStyle.Material, WindowButtonStyle.MacOS, WindowButtonStyle.WindowsXp,
        WindowButtonStyle.Glass
    };

    /// <summary>Наборы иконок, доступные для переопределения в блоке «Темы» (0.3.12.62).</summary>
    public static readonly IconSet[] SelectableIconSets =
    {
        IconSet.Fluent, IconSet.Outline, IconSet.Material, IconSet.MacOS, IconSet.WindowsXp, IconSet.Glass
    };

    /// <summary>Строковый идентификатор стиля кнопок окна для настроек ("Fluent", "MacOS", …).</summary>
    public static string ButtonStyleId(WindowButtonStyle style) => style.ToString();

    /// <summary>
    /// Переопределение набора иконок из настроек (AppSettings.IconSetOverride): пусто или
    /// неизвестное значение — null («по теме»).
    /// </summary>
    public static IconSet? ParseIconSetOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        foreach (var set in SelectableIconSets)
        {
            if (string.Equals(IconSetId(set), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return set;
        }
        return null;
    }

    /// <summary>
    /// Переопределение стиля кнопок окна из настроек (AppSettings.WindowButtonStyleOverride):
    /// пусто или неизвестное значение — null («по теме»).
    /// </summary>
    public static WindowButtonStyle? ParseButtonStyleOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        foreach (var style in SelectableButtonStyles)
        {
            if (string.Equals(ButtonStyleId(style), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return style;
        }
        return null;
    }

    /// <summary>Действующий набор иконок: переопределение пользователя или набор темы.</summary>
    public static IconSet EffectiveIconSet(ThemeDefinition? theme, IconSet? overrideSet)
        => overrideSet ?? theme?.Icons ?? IconSet.Fluent;

    /// <summary>Действующий стиль кнопок окна: переопределение пользователя или стиль темы.</summary>
    public static WindowButtonStyle EffectiveButtonStyle(ThemeDefinition? theme, WindowButtonStyle? overrideStyle)
        => overrideStyle ?? theme?.ButtonStyle ?? WindowButtonStyle.Fluent;

    /// <summary>Ключ локализации имени набора иконок (IconSet.Material → "IconSet.Material").</summary>
    public static string IconSetDisplayKey(IconSet set) => "IconSet." + IconSetId(set);

    /// <summary>Ключ локализации имени стиля кнопок окна (MacOS → "ButtonStyle.MacOS").</summary>
    public static string ButtonStyleNameKey(WindowButtonStyle style) => "ButtonStyle." + ButtonStyleId(style);

    /// <summary>
    /// Цвета карточки темы в галерее блока «Темы» (0.3.12.62): фон, карточка, текст,
    /// второстепенный текст, рамка и акцент — из палитры темы для её базового варианта.
    /// </summary>
    public readonly record struct ThemePreviewColors(
        string Background, string Card, string Text, string SecondaryText, string Border, string Accent);

    /// <summary>Цвета карточки темы для галереи (см. <see cref="ThemePreviewColors"/>).</summary>
    public static ThemePreviewColors PreviewColors(ThemeDefinition theme)
    {
        var scheme = theme.Scheme ?? (theme.IsDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight());
        string V(string key, string fallback)
        {
            var value = scheme.PaletteValue(theme.IsDark, key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        return new ThemePreviewColors(
            V("ContentBackgroundColor", theme.IsDark ? "#1E1E1E" : "#F5F5F5"),
            V("CardBackgroundColor", theme.IsDark ? "#2A2A2A" : "#FFFFFF"),
            V("TextPrimaryColor", theme.IsDark ? "#F5F5F5" : "#1F2937"),
            V("TextSecondaryColor", theme.IsDark ? "#A3A3A3" : "#6B7280"),
            V("BorderColor", theme.IsDark ? "#3F3F46" : "#E5E7EB"),
            string.IsNullOrWhiteSpace(theme.AccentColor) ? V("AccentColor", "#FDBF00") : theme.AccentColor);
    }

    /// <summary>Иконки, показываемые в карточке темы (по одной из разных групп интерфейса).</summary>
    public static readonly string[] PreviewIconKeys = { "IconSettings", "IconFolder", "IconDatabase", "IconSearch", "IconPlay" };

    /// <summary>
    /// Ключ локализации отображаемого имени полноценной темы по её идентификатору
    /// (используется в настройках обеих платформ, Задача 3). Неизвестный или пустой
    /// идентификатор даёт ключ светлой темы — как и <see cref="Resolve"/>.
    /// </summary>
    public static string ThemeNameKey(string? id) => id?.ToLowerInvariant() switch
    {
        "dark" => "Theme.Dark",
        "blue" => "Theme.Blue",
        "green" => "Theme.Green",
        "material" => "Theme.Material",
        "macos" => "Theme.MacOS",
        "windowsxp" => "Theme.WindowsXp",
        "glass" => "Theme.Glass",
        _ => "Theme.Light"
    };

    /// <summary>
    /// Суффикс, добавляемый к имени пользовательской схемы, если оно совпадает с именем
    /// встроенной полноценной темы (issue #376): так встроенная тема не затирает
    /// пользовательскую и наоборот.
    /// </summary>
    public const string CustomSchemeSuffix = " (пользовательская)";

    /// <summary>
    /// true, если имя совпадает с именем встроенной полноценной темы
    /// (Светлая/Тёмная/Синяя/Зелёная) без учёта регистра и крайних пробелов.
    /// </summary>
    public static bool IsBuiltInThemeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var trimmed = name.Trim();
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            if (string.Equals(theme.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Определяет сохранённый пользователем базовый вариант темы (устаревшее поле
    /// <see cref="AppSettings.Theme"/> со значениями "Light"/"Dark"): true — тёмный,
    /// false — светлый, null — вариант не задан (пустое или неизвестное значение),
    /// тогда применяется вариант, заданный полноценной темой.
    /// </summary>
    public static bool? ResolveDarkVariant(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
            return null;
        var trimmed = themeName.Trim();
        if (string.Equals(trimmed, "Dark", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(trimmed, "Light", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    /// <summary>
    /// Возвращает безопасное имя пользовательской схемы: если имя совпадает с именем
    /// встроенной полноценной темы, к нему добавляется <see cref="CustomSchemeSuffix"/>
    /// (issue #376). Иначе имя возвращается без изменений (с обрезкой крайних пробелов).
    /// </summary>
    public static string ResolveCustomSchemeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name ?? string.Empty;
        var trimmed = name.Trim();
        return IsBuiltInThemeName(trimmed) ? trimmed + CustomSchemeSuffix : trimmed;
    }
}