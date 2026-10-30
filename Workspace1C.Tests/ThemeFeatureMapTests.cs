using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики полноценных тем (Задача 2): выбор определения по идентификатору
/// с возвратом на светлую тему, копирование темы с другим базовым вариантом и маппинг
/// стиля кнопок окна / набора иконок в ресурсные ключи. Эти части
/// <see cref="ThemeFeatureMap"/> не требуют Application.Current и потому тестируются
/// без UI (сами менеджеры тем применяют их к ресурсам приложения).
/// </summary>
public sealed class ThemeFeatureMapTests
{
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("blue")]
    [InlineData("green")]
    [InlineData("material")]
    [InlineData("macos")]
    [InlineData("windowsxp")]
    [InlineData("glass")]
    public void Resolve_ReturnsBuiltInThemeById(string id)
    {
        var theme = ThemeFeatureMap.Resolve(id);

        Assert.NotNull(theme);
        Assert.Equal(id, theme.Id);
        Assert.False(string.IsNullOrWhiteSpace(theme.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("DarkTheme")]
    public void Resolve_UnknownOrEmptyId_FallsBackToLightTheme(string? id)
    {
        // ApplyFullTheme с неизвестным id применяет светлую тему по умолчанию.
        var theme = ThemeFeatureMap.Resolve(id);

        Assert.NotNull(theme);
        Assert.Equal("light", theme.Id);
        Assert.False(theme.IsDark);
        Assert.False(ThemeCatalog.Find(theme.Id)?.IsDark ?? true);
    }

    [Fact]
    public void WithBaseVariant_KeepsIdentityAndSwitchesBaseVariant()
    {
        var source = ThemeCatalog.Find("blue")!;

        var dark = ThemeFeatureMap.WithBaseVariant(source, isDark: true);

        // Идентичность темы сохраняется, меняется только базовый вариант.
        Assert.Equal(source.Id, dark.Id);
        Assert.Equal(source.Name, dark.Name);
        Assert.Equal(source.ButtonStyle, dark.ButtonStyle);
        Assert.Equal(source.Icons, dark.Icons);
        Assert.Equal(source.AccentColor, dark.AccentColor);
        Assert.True(dark.IsDark);

        // Схема клонируется: применение палитры не мутирует каталог ThemeCatalog.
        Assert.NotNull(dark.Scheme);
        Assert.NotSame(source.Scheme, dark.Scheme);
        Assert.NotEmpty(dark.Scheme.LightColors);
        Assert.NotEmpty(dark.Scheme.DarkColors);
    }

    [Fact]
    public void WithBaseVariant_NullThemeFallsBackToLight()
    {
        var theme = ThemeFeatureMap.WithBaseVariant(null, isDark: true);

        Assert.Equal("light", theme.Id);
        Assert.True(theme.IsDark);
        Assert.NotNull(theme.Scheme);
    }

    [Fact]
    public void ButtonMetrics_HaveExpectedStableValues()
    {
        var fluent = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Fluent);
        var minimal = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Minimal);
        var classic = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Classic);

        // Fluent — круглая подложка текущего вида (радиус круга 15, ширина кнопки 46).
        Assert.Equal(15, fluent.CornerRadius);
        Assert.Equal(46, fluent.Size);
        Assert.Equal("#FFF3CD", fluent.HoverHex);
        Assert.Equal("#E81123", fluent.CloseHoverHex);

        // Стили различаются геометрией, чтобы по ключам ресурсов можно было
        // отличить применённый стиль кнопок окна.
        Assert.NotEqual(fluent.CornerRadius, minimal.CornerRadius);
        Assert.NotEqual(fluent.Size, minimal.Size);
        Assert.NotEqual(fluent.CornerRadius, classic.CornerRadius);
        Assert.NotEqual(minimal.CornerRadius, classic.CornerRadius);
    }

    [Fact]
    public void ButtonMetrics_ThreeStylesDifferInGeometry()
    {
        // Задача 4: визуальное отличие трёх стилей кнопок окна обеспечивают метрики —
        // размер убывает (46 → 40 → 34), а скругление — от круга (15) через мягкий
        // квадрат (4) к прямоугольнику без скругления (0). Эти значения кладутся
        // в ресурсы WindowButtonCornerRadius/WindowButtonSize, которые читают кнопки.
        var fluent = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Fluent);
        var minimal = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Minimal);
        var classic = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Classic);

        Assert.True(fluent.Size > minimal.Size && minimal.Size > classic.Size);
        Assert.True(fluent.CornerRadius > minimal.CornerRadius && minimal.CornerRadius > classic.CornerRadius);
        Assert.Equal(0, classic.CornerRadius);
    }

    [Fact]
    public void ButtonMetrics_ColorsAreValidHex()
    {
        foreach (var style in new[] { WindowButtonStyle.Fluent, WindowButtonStyle.Minimal, WindowButtonStyle.Classic })
        {
            var metrics = ThemeFeatureMap.ButtonMetrics(style);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", metrics.HoverHex);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", metrics.CloseHoverHex);
        }
    }

    [Fact]
    public void ButtonMetrics_MacOS_HasTrafficLightColors()
    {
        // «Светофор» macOS (Задача 3): красный «закрыть», жёлтый «свернуть», зелёный «развернуть».
        var macos = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.MacOS);

        Assert.Equal("#FF5F57", macos.CloseHoverHex);
        Assert.Equal("#F5BF4F", macos.MinimizeHoverHex);
        Assert.Equal("#59CB59", macos.MaximizeHoverHex);
        Assert.Equal(15, macos.CornerRadius);
        Assert.Equal(46, macos.Size);
    }

    [Fact]
    public void ButtonMetrics_NewStyles_HaveExpectedGeometry()
    {
        // Material — круглые кнопки, Windows XP — квадратные, Glass — полупрозрачные (Задача 3).
        var material = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Material);
        var windowsXp = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.WindowsXp);
        var glass = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Glass);

        Assert.Equal(15, material.CornerRadius);
        Assert.Equal(46, material.Size);
        Assert.Equal(0, windowsXp.CornerRadius);
        Assert.Equal(34, windowsXp.Size);
        Assert.Equal(15, glass.CornerRadius);
        Assert.Equal(46, glass.Size);

        // У остальных новых стилей нет цветов «светофора» — поля null.
        Assert.Null(material.MinimizeHoverHex);
        Assert.Null(material.MaximizeHoverHex);
        Assert.Null(windowsXp.MinimizeHoverHex);
        Assert.Null(windowsXp.MaximizeHoverHex);
        Assert.Null(glass.MinimizeHoverHex);
        Assert.Null(glass.MaximizeHoverHex);
    }

    [Fact]
    public void ButtonMetrics_Glass_UsesSemiTransparentColors()
    {
        // Glass — полупрозрачные цвета #AARRGGBB (Задача 3): альфу поддерживают
        // ColorConverter WPF и Color.Parse Avalonia.
        var glass = ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Glass);

        Assert.Matches("^#[0-9A-Fa-f]{8}$", glass.HoverHex);
        Assert.Matches("^#[0-9A-Fa-f]{8}$", glass.CloseHoverHex);
    }

    [Fact]
    public void ResourceKeys_HaveExpectedNames()
    {
        // Ключи зафиксированы планом (Задача 4): стили кнопок окна будут читать их
        // из ресурсов приложения, поэтому имена нельзя менять без синхронизации.
        Assert.Equal("WindowButtonHoverBrush", ThemeFeatureMap.WindowButtonHoverBrushKey);
        Assert.Equal("WindowButtonCloseHoverBrush", ThemeFeatureMap.WindowButtonCloseHoverBrushKey);
        Assert.Equal("WindowButtonMinimizeHoverBrush", ThemeFeatureMap.WindowButtonMinimizeHoverBrushKey);
        Assert.Equal("WindowButtonMaximizeHoverBrush", ThemeFeatureMap.WindowButtonMaximizeHoverBrushKey);
        Assert.Equal("WindowButtonInactiveHoverBrush", ThemeFeatureMap.WindowButtonInactiveHoverBrushKey);
        Assert.Equal("WindowButtonCornerRadius", ThemeFeatureMap.WindowButtonCornerRadiusKey);
        Assert.Equal("WindowButtonSize", ThemeFeatureMap.WindowButtonSizeKey);
        Assert.Equal("IconSetName", ThemeFeatureMap.IconSetNameKey);
    }

    [Fact]
    public void AllResourceKeys_ContainsEachExpectedKeyOnce()
    {
        var expected = new[]
        {
            "WindowButtonHoverBrush",
            "WindowButtonCloseHoverBrush",
            "WindowButtonMinimizeHoverBrush",
            "WindowButtonMaximizeHoverBrush",
            "WindowButtonInactiveHoverBrush",
            "WindowButtonCornerRadius",
            "WindowButtonSize",
            "IconSetName"
        };

        Assert.Equal(expected, ThemeFeatureMap.AllResourceKeys);
    }

    [Theory]
    [InlineData(IconSet.Fluent, "Fluent")]
    [InlineData(IconSet.Outline, "Outline")]
    public void IconSetId_MapsToResourceName(IconSet icons, string expected)
    {
        // Значение кладётся в ресурс IconSetName и используется словарями иконок (Задача 5).
        Assert.Equal(expected, ThemeFeatureMap.IconSetId(icons));
    }
}