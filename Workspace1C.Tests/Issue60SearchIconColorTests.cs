using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #60 «Цвет иконки поиска» (автор 7OH): иконка поиска должна быть того же цвета,
/// что и крестик очистки; в светлой теме оба тёмные, в тёмной — рассинхрон.
/// Файловые проверки исходников (по образцу Issue41WindowLayoutTests / ButtonThemeBindingTests):
/// WPF-иконка поиска Magnify получает ту же кисть TextSecondaryBrush, что и крестик Close,
/// и не имеет полупрозрачности (Opacity="0.7" убрана); в Avalonia обе иконки используют
/// одну кисть TextSecondaryBrush.
/// </summary>
public sealed class Issue60SearchIconColorTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Полный тег PackIcon по позиции атрибута Kind (тег многострочный, до />).</summary>
    private static string ExtractPackIconTag(string xaml, int kindIndex)
    {
        var start = xaml.LastIndexOf("<materialDesign:PackIcon", kindIndex, StringComparison.Ordinal);
        var end = xaml.IndexOf("/>", kindIndex, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Фрагмент PackIcon не найден в разметке.");
        return xaml.Substring(start, end - start + 2);
    }

    [Fact]
    public void MainWindow_Wpf_SearchIconHasSecondaryBrushAndNoOpacity()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Иконка поиска (первая строка поля поиска). Issue #60: цвет один-в-один с крестиком
        // (TextSecondaryBrush), полупрозрачность 0.7 убрана — в тёмной теме иконка не «белёсая».
        var magnifyIdx = xaml.IndexOf("Kind=\"Magnify\"", StringComparison.Ordinal);
        Assert.True(magnifyIdx >= 0, "Иконка поиска Magnify не найдена в разметке.");
        var tag = ExtractPackIconTag(xaml, magnifyIdx);

        Assert.Contains("Foreground=\"{DynamicResource TextSecondaryBrush}\"", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.7\"", tag, StringComparison.Ordinal);
        // Иконка не перехватывает клики (клики уходят в поле поиска под ней).
        Assert.Contains("IsHitTestVisible=\"False\"", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Wpf_SearchAndClearIconsUseSameBrush()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Область поля поиска: от начала тега иконки Magnify до следующего комментария
        // разметки («Вкладки Все / Избранное / Недавние + действия»). Содержит только
        // иконку поиска и крестик очистки.
        var magnifyIdx = xaml.IndexOf("Kind=\"Magnify\"", StringComparison.Ordinal);
        Assert.True(magnifyIdx >= 0, "Иконка поиска Magnify не найдена в разметке.");
        var tagStart = xaml.LastIndexOf("<materialDesign:PackIcon", magnifyIdx, StringComparison.Ordinal);
        Assert.True(tagStart >= 0, "Начало тега иконки поиска не найдено.");
        var regionEnd = xaml.IndexOf("<!--", magnifyIdx + 1, StringComparison.Ordinal);
        Assert.True(regionEnd > magnifyIdx, "Конец области поля поиска не найден.");
        var region = xaml.Substring(tagStart, regionEnd - tagStart);

        var searchTag = ExtractPackIconTag(region, region.IndexOf("Kind=\"Magnify\"", StringComparison.Ordinal));
        var closeIdx = region.IndexOf("Kind=\"Close\"", StringComparison.Ordinal);
        Assert.True(closeIdx >= 0, "Крестик очистки Close не найден в области поля поиска.");
        var closeTag = ExtractPackIconTag(region, closeIdx);

        // Инвариант issue #60: обе иконки используют одну и ту же кисть темы.
        Assert.Contains("TextSecondaryBrush", searchTag, StringComparison.Ordinal);
        Assert.Contains("TextSecondaryBrush", closeTag, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Avalonia_SearchAndClearIconsUseSameBrush()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));

        // Иконка поиска и крестик очистки построены с одной кистью TextSecondaryBrush
        // (в Avalonia код уже синхронен, правка не требовалась).
        Assert.Contains("IconHelper.MakeIcon(\"IconSearch\", UiMetrics.IconControl, \"TextSecondaryBrush\")", source, StringComparison.Ordinal);
        Assert.Contains("IconHelper.MakeIcon(\"IconClose\", UiMetrics.IconInline, \"TextSecondaryBrush\")", source, StringComparison.Ordinal);
    }
}
