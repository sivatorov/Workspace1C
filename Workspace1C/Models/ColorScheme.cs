using System.Text.Json;
using System.Text.Json.Serialization;
using Workspace1C.Localization;

namespace Workspace1C.Models;

/// <summary>
/// Цветовая схема (тема оформления) приложения: именованный набор из двух палитр —
/// для светлого (<see cref="LightColors"/>) и тёмного (<see cref="DarkColors"/>) режима.
/// Накладывается поверх базовой темы. Поддерживает выгрузку/загрузку в JSON-файл,
/// а также создание собственных тем.
/// </summary>
public class ColorScheme
{
    /// <summary>Название схемы (темы).</summary>
    public string Name { get; set; } = "Light";

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDark { get; set; }

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>
    /// Палитра для светлого режима: ключ ресурса (имя Color-ресурса из темы, напр.
    /// <c>AccentColor</c>, либо имя кисти для ресурсов без отдельного цвета, напр.
    /// <c>ScrollThumbBrush</c>) → значение в формате #RRGGBB.
    /// </summary>
    public Dictionary<string, string> LightColors { get; set; } = new();

    /// <summary>Палитра для тёмного режима (тот же формат ключей, что у <see cref="LightColors"/>).</summary>
    public Dictionary<string, string> DarkColors { get; set; } = new();

    /// <summary>
    /// Акцентный цвет схемы в формате #RRGGBB (например "#2563EB" для синей темы).
    /// Необязательное поле: у пользовательских схем может отсутствовать — тогда
    /// используется акцент активной палитры (см. <see cref="Normalize"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccentColor { get; set; }

    /// <summary>
    /// Ключи ресурсов цветов и соответствующие им ключи локализации подписей
    /// для редактора в настройках. Технический ключ ресурса (первый элемент)
    /// хранится и сравнивается — его НЕ переводим; переводится только подпись.
    /// </summary>
    private static readonly (string Key, string LabelKey)[] _definitionKeys = new (string, string)[]
    {
        ("AccentColor", "Color.Accent"),
        ("AccentHoverColor", "Color.AccentHover"),
        ("AccentPressedColor", "Color.AccentPressed"),
        ("SidebarColor", "Color.Sidebar"),
        ("SidebarHoverColor", "Color.SidebarHover"),
        ("SidebarSelectedColor", "Color.SidebarSelected"),
        ("ContentBackgroundColor", "Color.ContentBackground"),
        ("CardBackgroundColor", "Color.CardBackground"),
        ("BorderColor", "Color.Border"),
        ("TextPrimaryColor", "Color.TextPrimary"),
        ("TextSecondaryColor", "Color.TextSecondary"),
        ("TextOnAccentColor", "Color.TextOnAccent"),
        ("ButtonTextColor", "Color.ButtonText"),
        ("FavoriteColor", "Color.Favorite"),
        ("FolderColor", "Color.Folder"),
        ("FavoriteFolderColor", "Color.FavoriteFolder"),
        ("ItemHoverColor", "Color.ItemHover"),
        ("ItemSelectedColor", "Color.ItemSelected"),
        ("AvatarBackgroundColor", "Color.AvatarBackground"),
        ("AvatarTextColor", "Color.AvatarText"),
        ("SecondaryButtonBackgroundColor", "Color.SecondaryButtonBackground"),
        ("SecondaryButtonHoverColor", "Color.SecondaryButtonHover"),
        ("SecondaryButtonPressedColor", "Color.SecondaryButtonPressed"),
        ("TreeHoverColor", "Color.TreeHover"),
        ("TreeSelectedColor", "Color.TreeSelected"),
        ("ScrollTrackBrush", "Color.ScrollTrack"),
        ("ScrollThumbBrush", "Color.ScrollThumb"),
        ("ScrollThumbHoverBrush", "Color.ScrollThumbHover"),
        ("ScrollThumbPressedBrush", "Color.ScrollThumbPressed")
    };

    /// <summary>
    /// Возвращает упорядоченное описание редактируемых цветов:
    /// ключ ресурса и локализованная человекочитаемая подпись для редактора в настройках.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> Definitions =>
        _definitionKeys.Select(d => (d.Key, LocalizationManager.T(d.LabelKey))).ToArray();

    /// <summary>Возвращает локализованную подпись для ключа цвета (если ключ неизвестен — сам ключ).</summary>
    public static string GetLabel(string key)
    {
        foreach (var (k, labelKey) in _definitionKeys)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.T(labelKey);
        }
        return key;
    }

    /// <summary>Возвращает активную палитру (светлую или тёмную) по варианту темы.</summary>
    public Dictionary<string, string> Palette(bool dark)
        => dark ? DarkColors : LightColors;

    /// <summary>
    /// Возвращает значение цвета указанной палитры по ключу. Если ключа нет — значение
    /// по умолчанию для этого варианта (для неузнаваемых ключей — нейтральный запасной).
    /// </summary>
    public string PaletteValue(bool dark, string key)
    {
        var dict = Palette(dark);
        if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        var defaults = dark ? DefaultDarkPalette() : DefaultLightPalette();
        if (defaults.TryGetValue(key, out var def) && !string.IsNullOrWhiteSpace(def))
            return def;

        return key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase)
            ? (dark ? "#3B4A5F" : "#B6C2D2")
            : (dark ? "#FFB300" : "#FDBF00");
    }

    /// <summary>
    /// Приводит схему к актуальному виду: переносит устаревшую одиночную палитру
    /// (<see cref="Colors"/>/<see cref="IsDark"/>) в соответствующее поле и заполняет
    /// отсутствующую палитру значениями по умолчанию. Вызывается при загрузке из JSON
    /// и перед применением схемы.
    /// </summary>
    public void Normalize()
    {
        if (Colors is { Count: > 0 })
        {
            var target = Palette(IsDark);
            var other = Palette(!IsDark);
            if (target.Count == 0)
            {
                foreach (var kvp in Colors)
                    target[kvp.Key] = kvp.Value;
            }
            if (other.Count == 0)
            {
                foreach (var kvp in (IsDark ? DefaultLightPalette() : DefaultDarkPalette()))
                    other[kvp.Key] = kvp.Value;
            }
            // Устаревшие поля больше не нужны.
            Colors = null;
        }

        FillMissing(LightColors, DefaultLightPalette());
        FillMissing(DarkColors, DefaultDarkPalette());

        // Подпись поля формы всегда равна первичному тексту своей палитры (issue #61):
        // у старых пользовательских схем ключ мог появиться из палитры по умолчанию,
        // и тогда FormLabelBrush отставал бы от собственного TextPrimaryColor схемы.
        if (LightColors.TryGetValue("TextPrimaryColor", out var lightText) && !string.IsNullOrWhiteSpace(lightText))
            LightColors["FormLabelBrush"] = lightText;
        if (DarkColors.TryGetValue("TextPrimaryColor", out var darkText) && !string.IsNullOrWhiteSpace(darkText))
            DarkColors["FormLabelBrush"] = darkText;

        // Акцентный цвет: если не задан (старые JSON-файлы) — берём акцент активной палитры.
        if (string.IsNullOrWhiteSpace(AccentColor))
            AccentColor = PaletteValue(IsDark, "AccentColor");
    }

    private static void FillMissing(Dictionary<string, string> target, Dictionary<string, string> defaults)
    {
        foreach (var kvp in defaults)
        {
            if (!target.ContainsKey(kvp.Key))
                target[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>Создаёт копию схемы (независимые наборы цветов обеих палитр).</summary>
    public ColorScheme Clone() => new()
    {
        Name = Name,
        IsDark = IsDark,
        Colors = Colors is null ? null : new Dictionary<string, string>(Colors, StringComparer.Ordinal),
        LightColors = new Dictionary<string, string>(LightColors, StringComparer.Ordinal),
        DarkColors = new Dictionary<string, string>(DarkColors, StringComparer.Ordinal),
        AccentColor = AccentColor
    };

    /// <summary>
    /// Собирает единую схему с двумя палитрами из устаревших полей настроек:
    /// старого одиночного <paramref name="active"/> и/или раздельных слотов
    /// <paramref name="light"/>/<paramref name="dark"/>. Возвращает нормализованную
    /// схему, готовую к применению и сохранению в новом формате.
    /// </summary>
    public static ColorScheme FromLegacy(ColorScheme? active, ColorScheme? light, ColorScheme? dark)
    {
        var hasSlots = light is not null || dark is not null;
        var source = hasSlots ? null : active;
        var merged = source?.Clone() ?? CreateLight();
        merged.Name = source?.Name ?? light?.Name ?? dark?.Name ?? "Светлая";

        if (hasSlots)
        {
            if (light is not null)
            {
                light.Normalize();
                merged.LightColors = new Dictionary<string, string>(light.Palette(false), StringComparer.Ordinal);
                if (light.DarkColors.Count > 0)
                    merged.DarkColors = new Dictionary<string, string>(light.DarkColors, StringComparer.Ordinal);
            }
            if (dark is not null)
            {
                dark.Normalize();
                merged.DarkColors = new Dictionary<string, string>(dark.Palette(true), StringComparer.Ordinal);
                if (dark.LightColors.Count > 0)
                    merged.LightColors = new Dictionary<string, string>(dark.LightColors, StringComparer.Ordinal);
            }
        }

        merged.Normalize();
        return merged;
    }

    /// <summary>Создаёт встроенную схему «Светлая» (несёт обе палитры).</summary>
    public static ColorScheme CreateLight() => Create("Светлая", false);

    /// <summary>Создаёт встроенную схему «Тёмная» (несёт обе палитры).</summary>
    public static ColorScheme CreateDark() => Create("Тёмная", true);

    /// <summary>Создаёт встроенную схему «Синяя» (несёт обе палитры, синий акцент).</summary>
    public static ColorScheme CreateBlue() => CreateAccent("Синяя", "#2563EB");

    /// <summary>Создаёт встроенную схему «Зелёная» (несёт обе палитры, зелёный акцент).</summary>
    public static ColorScheme CreateGreen() => CreateAccent("Зелёная", "#16A34A");

    /// <summary>
    /// Создаёт встроенную схему «Material Design» (Задача 3): фиолетовый акцент
    /// #7C4DFF, светлые поверхности #FAFAFA/#FFFFFF и тёмные #121212/#1E1E1E.
    /// </summary>
    public static ColorScheme CreateMaterial()
    {
        var scheme = CreateAccent("Material Design", "#7C4DFF");
        scheme.LightColors["ContentBackgroundColor"] = "#FAFAFA";
        scheme.LightColors["CardBackgroundColor"] = "#FFFFFF";
        scheme.LightColors["BorderColor"] = "#E0E0E0";
        scheme.DarkColors["ContentBackgroundColor"] = "#121212";
        scheme.DarkColors["CardBackgroundColor"] = "#1E1E1E";
        scheme.DarkColors["BorderColor"] = "#2C2C2C";
        return scheme;
    }

    /// <summary>
    /// Создаёт встроенную схему «macOS» (Задача 3): синий акцент #0A84FF,
    /// светлый фон #F5F5F7 и тёмный #1E1E1E.
    /// </summary>
    public static ColorScheme CreateMacOS()
    {
        var scheme = CreateAccent("macOS", "#0A84FF");
        scheme.LightColors["ContentBackgroundColor"] = "#F5F5F7";
        scheme.LightColors["CardBackgroundColor"] = "#FFFFFF";
        scheme.LightColors["BorderColor"] = "#D2D2D7";
        scheme.DarkColors["ContentBackgroundColor"] = "#1E1E1E";
        scheme.DarkColors["CardBackgroundColor"] = "#2C2C2E";
        scheme.DarkColors["BorderColor"] = "#3A3A3C";
        return scheme;
    }

    /// <summary>
    /// Создаёт встроенную схему «Windows XP» (Задача 3): синий акцент #3B6EA5,
    /// светло-голубой фон и классические границы.
    /// </summary>
    public static ColorScheme CreateWindowsXp()
    {
        var scheme = CreateAccent("Windows XP", "#3B6EA5");
        scheme.LightColors["ContentBackgroundColor"] = "#DCE9F7";
        scheme.LightColors["CardBackgroundColor"] = "#FFFFFF";
        scheme.LightColors["BorderColor"] = "#7F9DB9";
        scheme.DarkColors["ContentBackgroundColor"] = "#1E1E1E";
        scheme.DarkColors["CardBackgroundColor"] = "#2C2C2E";
        scheme.DarkColors["BorderColor"] = "#3B6EA5";
        return scheme;
    }

    /// <summary>
    /// Создаёт встроенную схему «Glass» (Задача 3): синий акцент #007AFF и светлая
    /// полупрозрачная палитра (#AARRGGBB — альфу поддерживают ColorConverter WPF
    /// и Color.Parse Avalonia).
    /// </summary>
    public static ColorScheme CreateGlass()
    {
        var scheme = CreateAccent("Glass", "#007AFF");
        scheme.LightColors["ContentBackgroundColor"] = "#E8EAF2FB";
        scheme.LightColors["CardBackgroundColor"] = "#F0F5F9FF";
        scheme.LightColors["BorderColor"] = "#B3C7E0";
        scheme.DarkColors["ContentBackgroundColor"] = "#E61A1A2E";
        scheme.DarkColors["CardBackgroundColor"] = "#E824243E";
        scheme.DarkColors["BorderColor"] = "#4A4A6A";
        return scheme;
    }

    /// <summary>
    /// Создаёт схему с палитрами по умолчанию для обоих режимов. Параметр <paramref name="isDark"/>
    /// сохраняется только в устаревшем поле <see cref="IsDark"/> (для совместимости); сама схема
    /// всегда содержит и светлую, и тёмную палитру. Акцентный цвет заполняется стандартным
    /// акцентом соответствующей палитры (см. <see cref="DefaultLightPalette"/>/<see cref="DefaultDarkPalette"/>).
    /// </summary>
    public static ColorScheme Create(string name, bool isDark)
    {
        var scheme = new ColorScheme { Name = name, IsDark = isDark };
        foreach (var (key, value) in DefaultLightPalette())
            scheme.LightColors[key] = value;
        foreach (var (key, value) in DefaultDarkPalette())
            scheme.DarkColors[key] = value;
        scheme.AccentColor = (isDark ? DefaultDarkPalette() : DefaultLightPalette())["AccentColor"];
        return scheme;
    }

    /// <summary>
    /// Создаёт схему с палитрами по умолчанию, но с заданным акцентным цветом:
    /// акцентные ключи обеих палитр заменяются на оттенки <paramref name="accentHex"/>.
    /// </summary>
    private static ColorScheme CreateAccent(string name, string accentHex)
    {
        var scheme = Create(name, false);
        scheme.AccentColor = accentHex;
        scheme.LightColors["AccentColor"] = accentHex;
        scheme.LightColors["AccentHoverColor"] = Shade(accentHex, 0.9);
        scheme.LightColors["AccentPressedColor"] = Shade(accentHex, 0.8);
        scheme.LightColors["SidebarSelectedColor"] = accentHex;
        scheme.DarkColors["AccentColor"] = accentHex;
        scheme.DarkColors["AccentHoverColor"] = Shade(accentHex, 1.1);
        scheme.DarkColors["AccentPressedColor"] = Shade(accentHex, 0.85);
        scheme.DarkColors["SidebarSelectedColor"] = accentHex;
        return scheme;
    }

    /// <summary>Палитра по умолчанию для светлого режима (соответствует LightTheme).</summary>
    private static Dictionary<string, string> DefaultLightPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FDBF00",
        ["AccentHoverColor"] = "#E0A800",
        ["AccentPressedColor"] = "#C49400",
        ["SidebarColor"] = "#1E293B",
        ["SidebarHoverColor"] = "#273549",
        ["SidebarSelectedColor"] = "#FDBF00",
        ["ContentBackgroundColor"] = "#F1F5F9",
        ["CardBackgroundColor"] = "#FFFFFF",
        ["InputBackgroundColor"] = "#E8EDF3",
        ["BorderColor"] = "#E2E8F0",
        ["TextPrimaryColor"] = "#000000",
        ["TextSecondaryColor"] = "#64748B",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        // Подпись поля формы (issue #61): равна первичному тексту темы — ключ в палитре
        // гарантирует, что ApplyColors пересоздаёт кисть для ВСЕХ схем (в т.ч. пользовательских
        // и генерируемых), а не только для тем по умолчанию.
        ["FormLabelBrush"] = "#000000",
        // Семантические статусы (аудит UI 1.0.1.10, п. 2.1): единые цвета
        // успеха/предупреждения/ошибки/информации для всех схем и генератора.
        ["SuccessColor"] = "#16A34A",
        ["WarningColor"] = "#D97706",
        ["ErrorColor"] = "#DC2626",
        ["InfoColor"] = "#0EA5E9",
        // Акцентные декоративные кисти (v100112): сиреневая и розовая для иконок/меток.
        ["PurpleAccentBrush"] = "#7C3AED",
        ["PinkAccentBrush"] = "#EC4899",
        // Мягкая красная плашка и плашка закрепления (аудит UI, пп. 2.4–2.5).
        ["DangerSoftBrush"] = "#FEF2F2",
        ["DangerSoftBorderBrush"] = "#FECACA",
        ["PinnedBadgeBrush"] = "#EDE9FE",
        ["PinnedBadgeTextBrush"] = "#5B21B6",
        ["FavoriteColor"] = "#F59E0B",
        // Текст на плашке избранного: тёмный (плашка всегда яркая). Реальный контраст
        // к палитре схемы добирает ThemeManager (SetContrastBrush FavoriteTextBrush).
        ["FavoriteTextColor"] = "#1C1917",
        ["FolderColor"] = "#2D6CDF",
        ["FavoriteFolderColor"] = "#8B5CF6",
        ["ItemHoverColor"] = "#FFF3CD",
        ["ItemSelectedColor"] = "#FFE69C",
        ["AvatarBackgroundColor"] = "#FFF3CD",
        ["AvatarTextColor"] = "#8A6D00",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#FFF9E6",
        ["TreeSelectedColor"] = "#FFD54D",
        // Дорожка переключателя в выключенном положении (issue #26): светлая #CBD5E1.
        ["SettingsToggleTrackColor"] = "#CBD5E1",
        // «Зебра» таблиц (issue #26): едва заметная slate-100 на белых карточках.
        ["ZebraRowColor"] = "#F1F5F9",
        ["ScrollTrackBrush"] = "#E8EDF3",
        ["ScrollThumbBrush"] = "#B6C2D2",
        ["ScrollThumbHoverBrush"] = "#94A3B8",
        ["ScrollThumbPressedBrush"] = "#7C8BA0"
    };

    /// <summary>Палитра по умолчанию для тёмного режима (соответствует DarkTheme).</summary>
    private static Dictionary<string, string> DefaultDarkPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FFB300",
        ["AccentHoverColor"] = "#FFCA28",
        ["AccentPressedColor"] = "#FF8F00",
        ["SidebarColor"] = "#111827",
        ["SidebarHoverColor"] = "#1F2937",
        ["SidebarSelectedColor"] = "#FFB300",
        ["ContentBackgroundColor"] = "#0F172A",
        ["CardBackgroundColor"] = "#1E293B",
        ["InputBackgroundColor"] = "#273548",
        ["BorderColor"] = "#334155",
        ["TextPrimaryColor"] = "#F1F5F9",
        ["TextSecondaryColor"] = "#CBD5E1",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        // Подпись поля формы (issue #61): равна первичному тексту тёмной темы.
        ["FormLabelBrush"] = "#F1F5F9",
        // Семантические статусы (аудит UI 1.0.1.10, п. 2.1): осветлённые варианты
        // для контраста на тёмном фоне.
        ["SuccessColor"] = "#22C55E",
        ["WarningColor"] = "#F59E0B",
        ["ErrorColor"] = "#EF4444",
        ["InfoColor"] = "#38BDF8",
        // Акцентные декоративные кисти (v100112): осветлённые варианты для тёмного фона.
        ["PurpleAccentBrush"] = "#A78BFA",
        ["PinkAccentBrush"] = "#F472B6",
        // Мягкая красная плашка и плашка закрепления (аудит UI, пп. 2.4–2.5).
        ["DangerSoftBrush"] = "#3F1D1D",
        ["DangerSoftBorderBrush"] = "#7F1D1D",
        ["PinnedBadgeBrush"] = "#4C1D95",
        ["PinnedBadgeTextBrush"] = "#DDD6FE",
        ["FavoriteColor"] = "#FBBF24",
        ["FavoriteTextColor"] = "#1C1917",
        ["FolderColor"] = "#3B82F6",
        ["FavoriteFolderColor"] = "#8B5CF6",
        ["ItemHoverColor"] = "#334155",
        ["ItemSelectedColor"] = "#1E3A5F",
        ["AvatarBackgroundColor"] = "#1E3A5F",
        ["AvatarTextColor"] = "#FFB300",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#334155",
        ["TreeSelectedColor"] = "#B45309",
        // Дорожка переключателя в выключенном положении (issue #26): тёмная #475569.
        ["SettingsToggleTrackColor"] = "#475569",
        // «Зебра» таблиц (issue #26): приглушённая slate-700 в тёмной теме.
        ["ZebraRowColor"] = "#334155",
        ["ScrollTrackBrush"] = "#16202E",
        ["ScrollThumbBrush"] = "#3B4A5F",
        ["ScrollThumbHoverBrush"] = "#52657F",
        ["ScrollThumbPressedBrush"] = "#6B80A0"
    };

    /// <summary>
    /// Строит цветовую схему (пару светлой и тёмной палитр) автоматически из одного
    /// базового цвета (issue #271). Базовый цвет становится акцентным; остальные ключи
    /// выводятся HSL-трансформациями (сдвиг светлости/насыщенности), сохраняя оттенок.
    /// Результат пригоден для ручной правки в редакторе перед сохранением.
    /// </summary>
    public static ColorScheme GenerateFromColor(string baseHex)
    {
        var (r, g, b) = ParseRgb(baseHex);
        RgbToHsl(r, g, b, out var h, out var s, out _);

        var scheme = new ColorScheme { Name = "Scheme.FromColor" };
        scheme.LightColors = BuildGeneratedPalette(h, s, dark: false);
        scheme.DarkColors = BuildGeneratedPalette(h, s, dark: true);
        return scheme;
    }

    private static Dictionary<string, string> BuildGeneratedPalette(double h, double s, bool dark)
    {
        string C(double lightness, double saturationFactor = 1.0)
            => HslToHex(h, Math.Clamp(s * saturationFactor, 0.08, 1.0), Math.Clamp(lightness, 0.0, 1.0));

        if (dark)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AccentColor"] = C(0.55, 0.9),
                ["AccentHoverColor"] = C(0.60, 0.9),
                ["AccentPressedColor"] = C(0.45, 0.9),
                ["SidebarColor"] = C(0.12, 0.35),
                ["SidebarHoverColor"] = C(0.18, 0.40),
                ["SidebarSelectedColor"] = C(0.55, 0.9),
                ["ContentBackgroundColor"] = C(0.12, 0.40),
                ["CardBackgroundColor"] = C(0.18, 0.40),
                ["InputBackgroundColor"] = C(0.22, 0.40),
                ["BorderColor"] = C(0.28, 0.35),
                ["TextPrimaryColor"] = "#F1F5F9",
                // Подпись поля формы (issue #61): в генерируемых схемах тоже присутствует
                // и повторяет первичный текст — контрастна и на карточке, и на поле ввода.
                ["FormLabelBrush"] = "#F1F5F9",
                // Семантические статусы (аудит UI, п. 2.1): фиксированные значения,
                // чтобы генератор не смешивал статусы с оттенком акцента схемы.
                ["SuccessColor"] = "#22C55E",
                ["WarningColor"] = "#F59E0B",
                ["ErrorColor"] = "#EF4444",
                ["InfoColor"] = "#38BDF8",
                ["PurpleAccentBrush"] = "#A78BFA",
                ["PinkAccentBrush"] = "#F472B6",
                ["DangerSoftBrush"] = "#3F1D1D",
                ["DangerSoftBorderBrush"] = "#7F1D1D",
                ["PinnedBadgeBrush"] = "#4C1D95",
                ["PinnedBadgeTextBrush"] = "#DDD6FE",
                ["TextSecondaryColor"] = C(0.80, 0.30),
                ["TextOnAccentColor"] = "#000000",
                ["ButtonTextColor"] = "#000000",
                ["FavoriteColor"] = C(0.60, 0.90),
                ["FavoriteTextColor"] = "#1C1917",
                ["FolderColor"] = C(0.60, 0.80),
                ["FavoriteFolderColor"] = C(0.60, 0.60),
                ["ItemHoverColor"] = C(0.25, 0.40),
                ["ItemSelectedColor"] = C(0.30, 0.50),
                ["AvatarBackgroundColor"] = C(0.30, 0.50),
                ["AvatarTextColor"] = C(0.55, 0.90),
                ["SecondaryButtonBackgroundColor"] = C(0.90, 0.50),
                ["SecondaryButtonHoverColor"] = C(0.85, 0.60),
                ["SecondaryButtonPressedColor"] = C(0.80, 0.65),
                ["TreeHoverColor"] = C(0.25, 0.40),
                ["TreeSelectedColor"] = C(0.40, 0.50),
                ["ScrollTrackBrush"] = C(0.14, 0.40),
                ["ScrollThumbBrush"] = C(0.32, 0.35),
                ["ScrollThumbHoverBrush"] = C(0.40, 0.35),
                ["ScrollThumbPressedBrush"] = C(0.48, 0.35)
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccentColor"] = C(0.50, 0.85),
            ["AccentHoverColor"] = C(0.44, 0.85),
            ["AccentPressedColor"] = C(0.38, 0.85),
            ["SidebarColor"] = C(0.16, 0.45),
            ["SidebarHoverColor"] = C(0.22, 0.45),
            ["SidebarSelectedColor"] = C(0.52, 0.90),
            ["ContentBackgroundColor"] = C(0.93, 0.35),
            ["CardBackgroundColor"] = C(0.99, 0.15),
            ["InputBackgroundColor"] = C(0.95, 0.25),
            ["BorderColor"] = C(0.88, 0.30),
            ["TextPrimaryColor"] = "#000000",
            // Подпись поля формы (issue #61): в генерируемых схемах повторяет первичный текст.
            ["FormLabelBrush"] = "#000000",
            // Семантические статусы (аудит UI, п. 2.1): фиксированные значения,
            // чтобы генератор не смешивал статусы с оттенком акцента схемы.
            ["SuccessColor"] = "#16A34A",
            ["WarningColor"] = "#D97706",
            ["ErrorColor"] = "#DC2626",
            ["InfoColor"] = "#0EA5E9",
            ["PurpleAccentBrush"] = "#7C3AED",
            ["PinkAccentBrush"] = "#EC4899",
            ["DangerSoftBrush"] = "#FEF2F2",
            ["DangerSoftBorderBrush"] = "#FECACA",
            ["PinnedBadgeBrush"] = "#EDE9FE",
            ["PinnedBadgeTextBrush"] = "#5B21B6",
            ["TextSecondaryColor"] = C(0.45, 0.40),
            ["TextOnAccentColor"] = "#FFFFFF",
            ["ButtonTextColor"] = "#000000",
            ["FavoriteColor"] = C(0.50, 0.90),
            ["FavoriteTextColor"] = "#1C1917",
            ["FolderColor"] = C(0.50, 0.80),
            ["FavoriteFolderColor"] = C(0.55, 0.60),
            ["ItemHoverColor"] = C(0.94, 0.50),
            ["ItemSelectedColor"] = C(0.88, 0.60),
            ["AvatarBackgroundColor"] = C(0.94, 0.50),
            ["AvatarTextColor"] = C(0.35, 0.70),
            ["SecondaryButtonBackgroundColor"] = C(0.94, 0.50),
            ["SecondaryButtonHoverColor"] = C(0.88, 0.60),
            ["SecondaryButtonPressedColor"] = C(0.82, 0.65),
            ["TreeHoverColor"] = C(0.96, 0.40),
            ["TreeSelectedColor"] = C(0.84, 0.65),
            ["ScrollTrackBrush"] = C(0.91, 0.30),
            ["ScrollThumbBrush"] = C(0.75, 0.30),
            ["ScrollThumbHoverBrush"] = C(0.68, 0.30),
            ["ScrollThumbPressedBrush"] = C(0.60, 0.30)
        };
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        hex = (hex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 6 && byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rr)
            && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var gg)
            && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var bb))
            return (rr, gg, bb);
        return (253, 191, 0); // запасной жёлтый акцент
    }

    /// <summary>Затемняет/осветляет HEX-цвет умножением каналов на коэффициент (0..1 — темнее, >1 — светлее).</summary>
    private static string Shade(string hex, double factor)
    {
        var (r, g, b) = ParseRgb(hex);
        byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
        return $"#{Clamp(r * factor):X2}{Clamp(g * factor):X2}{Clamp(b * factor):X2}";
    }

    // ---- Контраст текста (issue #26): чистые хелперы без UI-зависимостей ----

    /// <summary>
    /// Относительная яркость цвета sRGB по спецификации WCAG 2.x (0 — чёрный, 1 — белый).
    /// Используется для выбора контрастного цвета текста HEX-полей в обеих темах
    /// (Windows/WPF и Linux/Avalonia) и покрыта юнит-тестами.
    /// </summary>
    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        static double Linearize(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    /// <summary>
    /// Контрастное соотношение двух цветов (#RRGGBB) по WCAG 2.x (диапазон 1..21).
    /// Значение ≥ 4.5:1 обычно считается достаточным для текста обычного размера.
    /// </summary>
    public static double ContrastRatio(string fgHex, string bgHex)
    {
        var lighter = Math.Max(RelativeLuminance(fgHex), RelativeLuminance(bgHex));
        var darker = Math.Min(RelativeLuminance(fgHex), RelativeLuminance(bgHex));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Возвращает цвет текста (#FFFFFF или #000000) с максимальным контрастом на заданном
    /// фоне (#RRGGBB): для тёмного фона — белый, для светлого — чёрный. Оба варианта
    /// считаются по WCAG, берётся лучший: для средних оттенков (например зелёный акцент
    /// #16A34A) чёрный контрастнее белого, хотя его яркость и ниже порога 0.5 (issue #26).
    /// </summary>
    public static string ContrastTextHex(string backgroundHex)
    {
        var bg = RelativeLuminance(backgroundHex);
        var blackContrast = (bg + 0.05) / 0.05;
        var whiteContrast = 1.05 / (bg + 0.05);
        return whiteContrast > blackContrast ? "#FFFFFF" : "#000000";
    }

    // ---- Точный RGB-HSV (единый источник для пикеров обеих платформ) ----
    // Раньше WPF и Avalonia конвертировали цвет собственными преобразованиями,
    // а каналы «усекались» при приведении к байту: из-за потери точности при
    // масштабировании в HSV и усечении выбранный в настройках цвет (например
    // #13522C) открывался в диалоге выбора цвета слегка другим (#12512B)
    // (issue #26). Здесь единый путь с округлением до ближайшего байта:
    // RGB - HSV - RGB возвращает ровно исходный цвет.

    /// <summary>Преобразует каналы RGB в HSV: H в градусах (0..360), S и V в 0..1.</summary>
    public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double d = max - min;

        double h;
        if (d < 1e-9)
            h = 0;
        else if (Math.Abs(max - rd) < 1e-9)
            h = 60 * (((gd - bd) / d) % 6);
        else if (Math.Abs(max - gd) < 1e-9)
            h = 60 * ((bd - rd) / d + 2);
        else
            h = 60 * ((rd - gd) / d + 4);
        if (h < 0)
            h += 360;

        double s = max < 1e-9 ? 0 : d / max;
        return (h, s, max);
    }

    /// <summary>Преобразует HSV (H в градусах, S и V в 0..1) в каналы RGB.
    /// Каналы округляются до ближайшего байта (Math.Round), а не усекаются:
    /// округление делает обход RGB-HSV-RGB точным.</summary>
    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        var hi = (int)Math.Floor(h / 60.0) % 6;
        var f = h / 60.0 - Math.Floor(h / 60.0);
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);

        static byte Byte(double x) => (byte)Math.Clamp(Math.Round(x * 255.0), 0.0, 255.0);

        return hi switch
        {
            0 => (Byte(v), Byte(t), Byte(p)),
            1 => (Byte(q), Byte(v), Byte(p)),
            2 => (Byte(p), Byte(v), Byte(t)),
            3 => (Byte(p), Byte(q), Byte(v)),
            4 => (Byte(t), Byte(p), Byte(v)),
            _ => (Byte(v), Byte(p), Byte(q))
        };
    }

    /// <summary>Возвращает #RRGGBB «эталонным» обходом RGB-HSV-RGB с округлением
    /// каналов. Пикеры (Windows и Linux) построены на нём, поэтому показанный
    /// в настройках HEX совпадает с тем, что открывается в диалоге выбора цвета.</summary>
    public static string RoundTripHsvToHex(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        var (h, s, v) = RgbToHsv(r, g, b);
        var (rr, gg, bb) = HsvToRgb(h, s, v);
        return $"#{rr:X2}{gg:X2}{bb:X2}";
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9)
        {
            h = 0; s = 0;
            return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (Math.Abs(max - rd) < 1e-9) h = (gd - bd) / d + (gd < bd ? 6 : 0);
        else if (Math.Abs(max - gd) < 1e-9) h = (bd - rd) / d + 2;
        else h = (rd - gd) / d + 4;
        h /= 6.0;
    }

    private static string HslToHex(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;
        byte To(double t)
        {
            t = ((t % 1.0) + 1.0) % 1.0;
            double v;
            if (t < 1.0 / 6.0) v = p + (q - p) * 6.0 * t;
            else if (t < 0.5) v = q;
            else if (t < 2.0 / 3.0) v = p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            else v = p;
            return (byte)Math.Round(v * 255.0);
        }
        byte r = To(h + 1.0 / 3.0), g = To(h), b = To(h - 1.0 / 3.0);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    // ---- Сериализация схемы в JSON ----

    /// <summary>Сериализует схему в JSON-строку (пишутся только <see cref="Name"/> и обе палитры).</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Десериализует схему из JSON-строки. При ошибке возвращает null.</summary>
    public static ColorScheme? FromJson(string json)
    {
        try
        {
            var scheme = JsonSerializer.Deserialize<ColorScheme>(json, JsonOptions);
            scheme?.Normalize();
            return scheme;
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Кириллицу и прочие не-ASCII символы (например, имена схем) записываем читаемыми
        // UTF-8, а не \uXXXX-последовательностями (issue #170). Влияет только на запись.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}