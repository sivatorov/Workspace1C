using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI, пункт P0-1 (контраст): вторичный текст и текст аватара в светлой
/// и тёмной темах обязаны давать WCAG AA ≥ 4.5:1 на всех поверхностях, где они
/// используются (фон контента, карточка, поле ввода, плашка аватара).
///
/// Значения читаются из тех же источников, что и продукт: палитры
/// <see cref="ColorScheme.CreateLight"/>/<see cref="ColorScheme.CreateDark"/> и
/// словари тем <c>LightTheme</c>/<c>DarkTheme</c> (.xaml — WPF, .axaml — Avalonia).
/// Поэтому тест ловит не только регресс контраста, но и рассинхронизацию трёх
/// источников (палитра ↔ WPF-тема ↔ Avalonia-тема).
/// </summary>
public sealed class ContrastAuditP01Tests
{
    /// <summary>Поверхности, на которых используется вторичный текст.</summary>
    private static readonly string[] SecondaryBackgroundKeys =
    {
        "ContentBackgroundColor",
        "CardBackgroundColor",
        "InputBackgroundColor"
    };

    private const double WcagAaMinimum = 4.5;

    /// <summary>
    /// Наборы «WPF-тема, Avalonia-тема, тёмная ли палитра». Пары файлов читаются
    /// из проекта, чтобы обе платформы проверялись одинаково.
    /// </summary>
    public static IEnumerable<object[]> Themes => new[]
    {
        new object[] { "LightTheme.xaml", "LightTheme.axaml", false },
        new object[] { "DarkTheme.xaml", "DarkTheme.axaml", true }
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Читает значение <c><Color x:Key="...">#RRGGBB</Color></c> из словаря темы.</summary>
    private static string ReadThemeColor(string themeFile, string key)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        var match = Regex.Match(
            xaml,
            $"<Color x:Key=\"{Regex.Escape(key)}\">(#[0-9A-Fa-f]{{6,8}})</Color>");
        Assert.True(match.Success,
            $"В {themeFile} не найден цвет {key} (палитра и словарь темы рассинхронизированы).");
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Один и тот же ключ обязан совпадать в палитре схемы, WPF-теме и Avalonia-теме.
    /// </summary>
    private static string ReadSynchronizedColor(ColorScheme scheme, bool dark, string key,
        string wpfTheme, string avaloniaTheme)
    {
        var palette = scheme.PaletteValue(dark, key);
        Assert.Equal(palette, ReadThemeColor(wpfTheme, key), ignoreCase: true);
        Assert.Equal(palette, ReadThemeColor(avaloniaTheme, key), ignoreCase: true);
        return palette;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TextSecondary_MeetsWcagAa_OnAllSurfaces(string wpfTheme, string avaloniaTheme, bool dark)
    {
        var scheme = dark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        var secondary = ReadSynchronizedColor(scheme, dark, "TextSecondaryColor", wpfTheme, avaloniaTheme);

        foreach (var backgroundKey in SecondaryBackgroundKeys)
        {
            var background = ReadSynchronizedColor(scheme, dark, backgroundKey, wpfTheme, avaloniaTheme);
            var ratio = ColorScheme.ContrastRatio(secondary, background);

            Assert.True(ratio >= WcagAaMinimum,
                $"{wpfTheme}/{avaloniaTheme}: TextSecondary {secondary} на {backgroundKey} " +
                $"({background}) даёт {ratio:F2}:1 — требуется ≥ {WcagAaMinimum}:1 (WCAG AA).");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void AvatarText_MeetsWcagAa_OnAvatarBackground(string wpfTheme, string avaloniaTheme, bool dark)
    {
        var scheme = dark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        var avatarText = ReadSynchronizedColor(scheme, dark, "AvatarTextColor", wpfTheme, avaloniaTheme);
        var avatarBackground = ReadSynchronizedColor(scheme, dark, "AvatarBackgroundColor",
            wpfTheme, avaloniaTheme);

        var ratio = ColorScheme.ContrastRatio(avatarText, avatarBackground);

        Assert.True(ratio >= WcagAaMinimum,
            $"{wpfTheme}/{avaloniaTheme}: AvatarText {avatarText} на AvatarBackground " +
            $"{avatarBackground} даёт {ratio:F2}:1 — требуется ≥ {WcagAaMinimum}:1 (WCAG AA).");
    }

    [Fact]
    public void LightTheme_PreviousSecondaryAndAvatarColors_WouldFailWcag()
    {
        // Контрольный замер аудита P0-1: прежние значения светлой темы не проходили AA.
        // Значения-регрессы фиксируются здесь, чтобы «улучшение» не откатилось молча.
        Assert.True(ColorScheme.ContrastRatio("#64748B", "#F1F5F9") < WcagAaMinimum);
        Assert.True(ColorScheme.ContrastRatio("#64748B", "#E8EDF3") < WcagAaMinimum);
        Assert.True(ColorScheme.ContrastRatio("#64748B", "#E2E8F0") < WcagAaMinimum);
        Assert.True(ColorScheme.ContrastRatio("#8A6D00", "#FFF3CD") < WcagAaMinimum);
    }
}
