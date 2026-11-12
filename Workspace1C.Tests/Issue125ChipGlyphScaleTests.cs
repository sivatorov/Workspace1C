using System;
using System.IO;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #125 (предложение 7OH): значки в кружках запуска мелкие.
/// <list type="bullet">
/// <item>Material Design — «Предприятие» и «Конфигуратор», обе колонки
/// (наименование и «Действия»), укрупняются;</item>
/// <item>Fluent — только «Предприятие», обе колонки;</item>
/// <item>остальные наборы (Outline, macOS, Windows XP, Glass) и Конфигуратор
/// у Fluent — без изменений (Windows XP — эталон «как должно быть»).</item>
/// </list>
/// Кружок (18 px) не меняется — растёт только отрисовка значка внутри
/// (<see cref="ChipGlyphScale"/>). Правила применены в WPF-разметке
/// (MainWindow.xaml, конвертер <c>ChipGlyphSizeConverter</c>) и в Avalonia
/// (MainWindow.Avalonia.Tree.cs); Avalonia-код собирается только под
/// #if LINUX — покрывается source-гвардами (по образцу
/// <see cref="Issue103EnterpriseGlyphTests"/>).
/// </summary>
public sealed class Issue125ChipGlyphScaleTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    // ===================== Логика масштаба (ChipGlyphScale) =====================

    [Theory]
    [InlineData(IconSet.Fluent, ChipGlyphKind.Enterprise, 1.4)]
    [InlineData(IconSet.Material, ChipGlyphKind.Enterprise, 1.4)]
    [InlineData(IconSet.Material, ChipGlyphKind.Configurator, 1.3)]
    public void Factor_Request_ScalesUp(IconSet set, ChipGlyphKind kind, double expected)
    {
        Assert.Equal(expected, ChipGlyphScale.Factor(set, kind), 5);
    }

    [Theory]
    [InlineData(IconSet.Fluent, ChipGlyphKind.Configurator)]   // Конфигуратор у Fluent не меняется
    [InlineData(IconSet.Outline, ChipGlyphKind.Enterprise)]
    [InlineData(IconSet.Outline, ChipGlyphKind.Configurator)]
    [InlineData(IconSet.MacOS, ChipGlyphKind.Enterprise)]
    [InlineData(IconSet.MacOS, ChipGlyphKind.Configurator)]
    [InlineData(IconSet.WindowsXp, ChipGlyphKind.Enterprise)]  // XP — эталон, без изменений
    [InlineData(IconSet.WindowsXp, ChipGlyphKind.Configurator)]
    [InlineData(IconSet.Glass, ChipGlyphKind.Enterprise)]
    [InlineData(IconSet.Glass, ChipGlyphKind.Configurator)]
    public void Factor_OtherSetsAndGlyphs_Unchanged(IconSet set, ChipGlyphKind kind)
    {
        Assert.Equal(1.0, ChipGlyphScale.Factor(set, kind), 5);
    }

    [Fact]
    public void Factor_NoneKind_AlwaysUnchanged()
    {
        foreach (IconSet set in Enum.GetValues(typeof(IconSet)))
            Assert.Equal(1.0, ChipGlyphScale.Factor(set, ChipGlyphKind.None), 5);
    }

    [Fact]
    public void Scale_MultipliesBaseSize()
    {
        Assert.Equal(22.4, ChipGlyphScale.Scale(16, IconSet.Fluent, ChipGlyphKind.Enterprise), 5);
        Assert.Equal(22.4, ChipGlyphScale.Scale(16, IconSet.Material, ChipGlyphKind.Enterprise), 5);
        Assert.Equal(15.6, ChipGlyphScale.Scale(12, IconSet.Material, ChipGlyphKind.Configurator), 5);
        Assert.Equal(20.8, ChipGlyphScale.Scale(16, IconSet.Material, ChipGlyphKind.Configurator), 5);
        // Неизменные наборы возвращают базовый размер как есть.
        Assert.Equal(16, ChipGlyphScale.Scale(16, IconSet.WindowsXp, ChipGlyphKind.Enterprise), 5);
        Assert.Equal(13, ChipGlyphScale.Scale(13, IconSet.Fluent, ChipGlyphKind.Configurator), 5);
    }

    // ===================== WPF-конвертер (только Windows-сборка тестов) =====================

#if !LINUX
    [Fact]
    public void Converter_EnterpriseFluentAndMaterial_ScalesUp()
    {
        var converter = new Converters.ChipGlyphSizeConverter();

        var fluent = converter.Convert(
            new object?[] { IconSet.Fluent },
            typeof(double), "Enterprise:16", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(22.4, (double)fluent!, 5);

        var material = converter.Convert(
            new object?[] { IconSet.Material },
            typeof(double), "Enterprise:16", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(22.4, (double)material!, 5);
    }

    [Fact]
    public void Converter_Configurator_OnlyMaterialScalesUp()
    {
        var converter = new Converters.ChipGlyphSizeConverter();

        // Fluent — базовый размер (12 px в индикаторе наименования).
        var fluent = converter.Convert(
            new object?[] { IconSet.Fluent },
            typeof(double), "Configurator:12", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(12, (double)fluent!, 5);

        // Material — укрупнён (12 px в индикаторе наименования).
        var material = converter.Convert(
            new object?[] { IconSet.Material },
            typeof(double), "Configurator:12", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(15.6, (double)material!, 5);
    }

    [Fact]
    public void Converter_WindowsXpAndNone_Unchanged()
    {
        var converter = new Converters.ChipGlyphSizeConverter();

        Assert.Equal(16, (double)converter.Convert(
            new object?[] { IconSet.WindowsXp },
            typeof(double), "Enterprise:16", System.Globalization.CultureInfo.InvariantCulture)!, 5);

        // Прочие значки чипов (Pencil/Delete/Broom) — размер не зависит от набора.
        Assert.Equal(16, (double)converter.Convert(
            new object?[] { IconSet.Material },
            typeof(double), "None:16:13", System.Globalization.CultureInfo.InvariantCulture)!, 5);
    }

    [Fact]
    public void Converter_CompactMode_UsesCompactBase()
    {
        var converter = new Converters.ChipGlyphSizeConverter();

        // Компактный режим: базовый 13 px, у Material Предприятие — 13 × 1.4.
        Assert.Equal(13, (double)converter.Convert(
            new object?[] { IconSet.Fluent, true },
            typeof(double), "None:16:13", System.Globalization.CultureInfo.InvariantCulture)!, 5);
        Assert.Equal(18.2, (double)converter.Convert(
            new object?[] { IconSet.Material, true },
            typeof(double), "Enterprise:16:13", System.Globalization.CultureInfo.InvariantCulture)!, 5);
        // Конфигуратор у Fluent — 13 без изменений.
        Assert.Equal(13, (double)converter.Convert(
            new object?[] { IconSet.Fluent, true },
            typeof(double), "Configurator:16:13", System.Globalization.CultureInfo.InvariantCulture)!, 5);
    }

    [Fact]
    public void Converter_MissingSet_FallsBackToFluent()
    {
        var converter = new Converters.ChipGlyphSizeConverter();
        Assert.Equal(22.4, (double)converter.Convert(
            Array.Empty<object?>(),
            typeof(double), "Enterprise:16", System.Globalization.CultureInfo.InvariantCulture)!, 5);
    }
#endif

    // ===================== WPF-разметка (MainWindow.xaml) =====================

    [Fact]
    public void Wpf_NameIndicatorChips_UseChipGlyphSize()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Индикатор в наименовании: Предприятие — укрупнение у Fluent/Material.
        // Якорь с переносом строки — именно чип индикатора (другие значки Play
        // в разметке записаны атрибутами в одну строку).
        var play = xaml.IndexOf("Kind=\"Play\"\r\n", StringComparison.Ordinal);
        Assert.True(play > 0, "значок Play в индикаторе наименования не найден");
        Assert.Contains("ConverterParameter=\"Enterprise:16\"", xaml, StringComparison.Ordinal);

        // Индикатор в наименовании: Конфигуратор — только Material.
        Assert.Contains("ConverterParameter=\"Configurator:12\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ActionChipButtons_OverriddenGlyphKinds()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Кнопки запуска колонки «Действия»: Предприятие (Enterprise) и Конфигуратор
        // (только Material) — со своим видом глифа в компактном режиме 13 px.
        Assert.Contains("ConverterParameter=\"Enterprise:16:13\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=\"Configurator:16:13\"", xaml, StringComparison.Ordinal);
        // Прочие значки чипов — без масштабирования по набору (16/13 px).
        Assert.Contains("ConverterParameter=\"None:16:13\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ChipCircle_SizeUnchanged()
    {
        // Кружок 18 px (issue #121 v1.0.1.55) не менялся — растёт только значок.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var chip = xaml.IndexOf("<Style x:Key=\"RowActionChip\" TargetType=\"Border\">", StringComparison.Ordinal);
        Assert.True(chip > 0, "стиль RowActionChip не найден");
        var chipBlock = xaml[chip..xaml.IndexOf("</Style>", chip, StringComparison.Ordinal)];
        Assert.Contains("<Setter Property=\"Width\" Value=\"18\"/>", chipBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"18\"/>", chipBlock, StringComparison.Ordinal);
    }

    // ===================== Avalonia (source-гварды) =====================

    [Fact]
    public void Avalonia_NameIndicatorIcons_ScaleBySet()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        // Значок Предприятия — масштаб Enterprise (Fluent/Material крупнее).
        Assert.Contains("ChipGlyphScale.Factor(Themes.IconSetState.Instance.Current, Themes.ChipGlyphKind.Enterprise)", source, StringComparison.Ordinal);
        // Значок Конфигуратора — масштаб Configurator (только Material).
        Assert.Contains("ChipGlyphScale.Factor(Themes.IconSetState.Instance.Current, Themes.ChipGlyphKind.Configurator)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ActionChips_ScaleBySet()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        // Кнопки колонки «Действия»: вид глифа по ключу иконки, размер —
        // ChipGlyphScale.Scale от базовых 16 px.
        Assert.Contains("\"IconPlay\" => Themes.ChipGlyphKind.Enterprise", source, StringComparison.Ordinal);
        Assert.Contains("\"IconWrench\" => Themes.ChipGlyphKind.Configurator", source, StringComparison.Ordinal);
        Assert.Contains("Themes.ChipGlyphScale.Scale(UiMetrics.Scaled(16), Themes.IconSetState.Instance.Current, glyphKind)", source, StringComparison.Ordinal);
        // Кружок 18 px не менялся.
        Assert.Contains("UiMetrics.Scaled(18)", source, StringComparison.Ordinal);
    }
}
