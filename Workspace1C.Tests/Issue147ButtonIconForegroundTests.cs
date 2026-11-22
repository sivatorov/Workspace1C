using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #147 (1.0.1.104): иконки кнопок «Сохранить/Готово/Отмена» должны иметь
/// тот же цвет, что и подпись, и оставаться контрастными на акцентной заливке
/// (<c>AccentBrush</c>) и при наведении (<c>AccentHoverBrush</c>).
///
/// В 1.0.1.103 значки красились жёстко (<c>SuccessBrush</c>/<c>TextPrimaryBrush</c>),
/// а акцентная кнопка — <c>TextOnAccentBrush</c>. На hover белая подпись оставалась
/// видимой, а значок «пропадал». Исправление: <c>Fill</c> значка привязан к
/// <c>Foreground</c> кнопки (WPF) / предку-кнопке (Avalonia).
/// </summary>
public sealed class Issue147ButtonIconForegroundTests
{
    private const string ForegroundBinding =
        "Fill=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}\"";

    // Жёсткие кисти, из-за которых значок расходился с цветом подписи на акцентной
    // кнопке (план #147: не задавать «жёстким TextPrimaryBrush/SuccessBrush»).
    // TextSecondaryBrush оставлен декоративным крестикам очистки поиска (#60).
    private static readonly string[] HardcodedIconBrushes =
    {
        "SuccessBrush", "TextPrimaryBrush"
    };

    // ===================== Контраст на акцентном фоне =====================

    [Theory]
    [InlineData(false)] // светлая тема
    [InlineData(true)]  // тёмная тема
    public void Issue147_IconOnAccentAndHoverHasContrastAtLeast45(bool dark)
    {
        var scheme = ColorScheme.Create("issue-147", dark);
        var accent = scheme.PaletteValue(dark, "AccentColor");
        var hover = scheme.PaletteValue(dark, "AccentHoverColor");
        var text = EffectiveTextOnAccent(scheme, dark);

        Assert.True(ColorScheme.ContrastRatio(text, accent) >= 4.5,
            $"Текст/значок {text} на AccentBrush {accent} даёт " +
            $"{ColorScheme.ContrastRatio(text, accent):0.00}:1 (< 4.5).");
        Assert.True(ColorScheme.ContrastRatio(text, hover) >= 4.5,
            $"Текст/значок {text} на AccentHoverBrush {hover} даёт " +
            $"{ColorScheme.ContrastRatio(text, hover):0.00}:1 (< 4.5). " +
            "Именно на наведении значок раньше пропадал.");
    }

    [Fact]
    public void Issue147_DefaultAmberAccentUsesDarkTextInBothThemes()
    {
        // Янтарный акцент по умолчанию — светлый, поэтому TextOnAccentBrush
        // обязан стать тёмным, иначе белый значок невидим на AccentHoverBrush.
        foreach (var dark in new[] { false, true })
        {
            var scheme = ColorScheme.Create("issue-147", dark);
            var text = EffectiveTextOnAccent(scheme, dark);
            Assert.Equal("#000000", text, ignoreCase: true);
        }
    }

    // ===================== WPF: Fill привязан к Foreground =====================

    [Fact]
    public void Issue147_Wpf_DialogIconPathsBindFillToButtonForeground()
    {
        var views = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        var pathPattern = new Regex("<Path\\b[^>]*?/>", RegexOptions.Singleline);
        var iconPattern = new Regex("Data=\"\\{DynamicResource Icon(Check|Save|Close)\\}\"");
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in pathPattern.Matches(text))
            {
                var element = match.Value;
                if (!iconPattern.IsMatch(element))
                    continue;

                if (element.Contains("Fill=\"{DynamicResource", StringComparison.Ordinal))
                {
                    var hard = HardcodedIconBrushes.FirstOrDefault(b =>
                        element.Contains($"Fill=\"{{DynamicResource {b}}}\"", StringComparison.Ordinal));
                    if (hard is not null)
                        failures.Add($"{Path.GetFileName(file)}: жёсткий Fill={hard} — {Collapse(element)}");
                }
                else if (!element.Contains(ForegroundBinding, StringComparison.Ordinal))
                {
                    failures.Add($"{Path.GetFileName(file)}: Fill не привязан к Foreground — {Collapse(element)}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Иконки Save/Done/Cancel должны крашиться Foreground кнопки:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void Issue147_Wpf_KeyDialogButtonsUseForegroundBinding()
    {
        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var save = Slice(settings, "Click=\"OnSave_Click\"", "Common.Save");
        Assert.Contains(ForegroundBinding, save, StringComparison.Ordinal);

        var connection = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));
        var connSave = Slice(connection, "Click=\"OnSave_Click\"", "Common.Save");
        Assert.Contains(ForegroundBinding, connSave, StringComparison.Ordinal);

        var utilities = ReadProjectFile(Path.Combine("Workspace1C", "Views", "UtilitiesMenuEditorWindow.xaml"));
        var done = Slice(utilities, "x:Name=\"OkButton\"", "Utilities.Editor.Done");
        Assert.Contains(ForegroundBinding, done, StringComparison.Ordinal);
    }

    // ===================== Avalonia: Fill привязан к Foreground кнопки =====================

    [Fact]
    public void Issue147_Avalonia_IconHelperBindsFillToButtonForeground()
    {
        var helper = ReadProjectFile(Path.Combine("Workspace1C", "Converters", "Avalonia", "IconHelper.Avalonia.cs"));
        Assert.Contains("MakeIconForButton", helper, StringComparison.Ordinal);
        Assert.Contains("IconAndTextForButton", helper, StringComparison.Ordinal);
        Assert.Contains("RelativeSourceMode.FindAncestor", helper, StringComparison.Ordinal);
        Assert.Contains("typeof(Button)", helper, StringComparison.Ordinal);
        Assert.Contains("nameof(Button.Foreground)", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue147_Avalonia_DialogAndEditorButtonsUseForegroundIcon()
    {
        var modal = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
        Assert.Contains("IconHelper.MakeIconForButton(okIconKey", modal, StringComparison.Ordinal);
        Assert.Contains("IconHelper.MakeIconForButton(iconKey", modal, StringComparison.Ordinal);
        Assert.Contains("IconHelper.MakeIconForButton(\"IconClose\"", modal, StringComparison.Ordinal);

        foreach (var file in new[]
                 {
                     "ContextMenuEditorWindow.Avalonia.cs",
                     "TrayMenuEditorWindow.Avalonia.cs",
                     "UtilitiesMenuEditorWindow.Avalonia.cs"
                 })
        {
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", file));
            Assert.Contains("IconHelper.MakeIconForButton(iconKey", code, StringComparison.Ordinal);
        }

        foreach (var file in new[]
                 {
                     "ServicesPickerWindow.Avalonia.cs",
                     "ServiceGroupEditWindow.Avalonia.cs"
                 })
        {
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", file));
            Assert.Contains("IconHelper.IconAndTextForButton(iconKey", code, StringComparison.Ordinal);
        }
    }

    // ===================== Темы и наборы иконок =====================

    [Fact]
    public void Issue147_Themes_AccentButtonForegroundMatchesTextOnAccent()
    {
        foreach (var theme in new[] { "DarkTheme.xaml", "LightTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            var modern = Slice(xaml, "x:Key=\"ModernButton\"", "x:Key=\"SecondaryButton\"");
            Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentBrush}\"", modern,
                StringComparison.Ordinal);

            var secondary = Slice(xaml, "x:Key=\"SecondaryButton\"", "x:Key=\"DangerButton\"");
            Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource TextPrimaryBrush}\"", secondary,
                StringComparison.Ordinal);
        }

        var controls = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var avaloniaModern = Slice(controls, "x:Key=\"ModernButton\"", "x:Key=\"SecondaryButton\"");
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentBrush}\"", avaloniaModern,
            StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SecondaryButtonTextBrush\"", controls, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IconsGlass.xaml", "Glass")]
    [InlineData("IconsMaterial.xaml", "Material")]
    [InlineData("IconsOutline.xaml", "Outline")]
    [InlineData("IconsMacOS.xaml", "MacOS")]
    [InlineData("IconsWindowsXp.xaml", "WindowsXp")]
    public void Issue147_EveryIconSetDefinesDialogGeometry(string file, string setSuffix)
    {
        // Привязка Fill не зависит от набора: у каждого набора есть свои контуры
        // для Save/Done/Cancel (с суффиксом набора), поэтому значок не пропадёт
        // при переключении набора иконок.
        var icons = ReadProjectFile(Path.Combine("Workspace1C", "Themes", file));
        Assert.Contains($"x:Key=\"IconCheck.{setSuffix}\"", icons, StringComparison.Ordinal);
        Assert.Contains($"x:Key=\"IconSave.{setSuffix}\"", icons, StringComparison.Ordinal);
        Assert.Contains($"x:Key=\"IconClose.{setSuffix}\"", icons, StringComparison.Ordinal);
    }

    // ===================== helpers =====================

    /// <summary>
    /// Цвет текста на акцентной кнопке — та же логика, что в ThemeManager:
    /// пользовательский TextOnAccentColor (если явно задан) при контрасте ≥ 3:1,
    /// иначе чёрный/белый по максимуму контраста к акценту.
    /// </summary>
    private static string EffectiveTextOnAccent(ColorScheme scheme, bool dark)
    {
        var accent = scheme.PaletteValue(dark, "AccentColor");
        var custom = scheme.PaletteValue(dark, "TextOnAccentColor");
        var isCustom = !string.IsNullOrWhiteSpace(custom)
            && !string.Equals(custom.Trim(), "#FFFFFF", StringComparison.OrdinalIgnoreCase);
        return ColorScheme.ResolveTextOnAccentHex(isCustom ? custom : null, accent);
    }

    private static string Collapse(string text) =>
        Regex.Replace(text, "\\s+", " ").Trim();

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Не найден фрагмент начала: {start}");
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"Не найден фрагмент конца: {end}");
        return text.Substring(from, to - from);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var full = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(full), $"Файл не найден: {full}");
        return File.ReadAllText(full);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
