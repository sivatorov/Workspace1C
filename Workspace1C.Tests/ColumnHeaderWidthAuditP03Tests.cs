using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI, пункт P0-3: заголовок последней колонки списка баз («Последняя копия»)
/// обрезался до «Послед…», а под таблицей появлялась горизонтальная прокрутка.
/// <list type="bullet">
/// <item>каждая колонка получает <c>MinWidth</c> не меньше ширины самого длинного слова
/// подписи с иконкой и отступами (<see cref="ColumnHeaderMetrics"/>);</item>
/// <item>заголовок переносится на две строки при фиксированной высоте шапки, а не
/// обрезается многоточием;</item>
/// <item>правки симметричны для WPF (разметка) и Avalonia (построитель колонок);</item>
/// <item>ширины по умолчанию не меньше минимума — исправление не расширяет таблицу и
/// не добавляет горизонтальную прокрутку само по себе.</item>
/// </list>
/// </summary>
public sealed class ColumnHeaderWidthAuditP03Tests
{
    /// <summary>Ширины колонок по умолчанию (WPF ConverterParameter / Avalonia fallback).</summary>
    private static readonly Dictionary<string, double> DefaultWidths = new(StringComparer.Ordinal)
    {
        ["Version"] = 120,
        ["Configuration"] = 160,
        ["ConfigurationVersion"] = 100,
        ["LaunchMode"] = 120,
        ["ServerBase"] = 200,
        ["LastLaunch"] = 140,
        ["Size"] = 90,
        ["Modified"] = 130,
        ["LastBackup"] = 130,
        ["Actions"] = 120,
    };

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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    // ===================== Метрики (общая логика) =====================

    [Fact]
    public void MinWidthFor_LastBackup_CoversFullHeaderWord()
    {
        // «Последняя» — самое длинное слово подписи «Последняя копия»: 70.4 px
        // (Segoe UI/Inter 12 px SemiBold) + иконка 13 + зазор 5 + отступы 12 + запас 2.
        // Ширина 130 при этом оставляет под текст только 101 px, поэтому подпись
        // обрезалась. Минимум обязан быть не меньше 103.
        var min = ColumnHeaderMetrics.MinWidthFor("LastBackup");

        Assert.Equal(103, min);
        Assert.True(min > 130 - ColumnHeaderMetrics.HeaderChrome,
            "минимум должен вмещать самое длинное слово подписи без обрезки");
    }

    [Theory]
    [InlineData("Version", 107)]
    [InlineData("Configuration", 126)]
    [InlineData("ConfigurationVersion", 80)]
    [InlineData("LaunchMode", 84)]
    [InlineData("ServerBase", 114)]
    [InlineData("LastLaunch", 104)]
    [InlineData("Size", 82)]
    [InlineData("Modified", 104)]
    [InlineData("LastBackup", 103)]
    [InlineData("Actions", 94)]
    public void MinWidthFor_MatchesMeasuredHeaderMetrics(string key, double expected)
    {
        Assert.Equal(expected, ColumnHeaderMetrics.MinWidthFor(key));
    }

    [Fact]
    public void MinWidthFor_EveryDataColumn_CoversLongestWordAndFloor()
    {
        foreach (var (key, wordWidth) in ColumnHeaderMetrics.LongestWordWidthTable)
        {
            if (key == "Name")
                continue;

            var min = ColumnHeaderMetrics.MinWidthFor(key);

            Assert.True(min >= wordWidth + ColumnHeaderMetrics.HeaderChrome + ColumnHeaderMetrics.TextSafety,
                $"{key}: MinWidth {min} меньше ширины слова {wordWidth} с иконкой и отступами");
            Assert.True(min >= ColumnHeaderMetrics.MinWidthFloor,
                $"{key}: MinWidth {min} ниже абсолютного минимума");
        }
    }

    [Fact]
    public void MinWidthFor_Name_UsesNameMinimum()
    {
        Assert.Equal(ColumnHeaderMetrics.NameColumnMinWidth, ColumnHeaderMetrics.MinWidthFor("Name"));
        Assert.Equal(220, ColumnHeaderMetrics.NameColumnMinWidth);
    }

    [Fact]
    public void MinWidthFor_UnknownKey_FallsBackToFloor()
    {
        Assert.Equal(ColumnHeaderMetrics.MinWidthFloor, ColumnHeaderMetrics.MinWidthFor("Unknown"));
        Assert.Equal(ColumnHeaderMetrics.MinWidthFloor, ColumnHeaderMetrics.MinWidthFor(null));
        Assert.Equal(ColumnHeaderMetrics.MinWidthFloor, ColumnHeaderMetrics.MinWidthFor(string.Empty));
    }

    [Fact]
    public void DefaultWidths_AreNotSmallerThanMinimums()
    {
        // Исправление не должно расширять таблицу: минимум каждой колонки обязан
        // помещаться в её ширину по умолчанию, иначе горизонтальная прокрутка
        // появилась бы даже при широком окне.
        foreach (var (key, width) in DefaultWidths)
        {
            var min = ColumnHeaderMetrics.MinWidthFor(key);
            Assert.True(min <= width,
                $"{key}: MinWidth {min} больше ширины по умолчанию {width} — таблица станет шире");
        }
    }

    [Fact]
    public void HeaderHeight_FitsTwoTextLines()
    {
        // Две строки 12 px SemiBold (≈ 16 px каждая) плюс нижний отступ 4 px.
        Assert.True(ColumnHeaderMetrics.HeaderHeight >= 2 * 16 + 4,
            "фиксированная высота шапки должна вмещать две строки подписи");
        Assert.Equal(
            ColumnHeaderMetrics.IconSize + ColumnHeaderMetrics.IconTextGap + ColumnHeaderMetrics.HorizontalMargins,
            ColumnHeaderMetrics.HeaderChrome);
    }

    // ===================== WPF (разметка) =====================

    [Fact]
    public void Wpf_HeaderAndRows_DeclareMinWidthForEveryColumn()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        Assert.Contains("<conv:ColumnMinWidthConverter x:Key=\"ColumnMinWidth\"/>", xaml, StringComparison.Ordinal);

        // Заголовок + строка группы + строка базы: по одному MinWidth на колонку данных.
        var count = CountOccurrences(xaml, "Converter={StaticResource ColumnMinWidth}");
        Assert.True(count >= 30,
            $"ожидались MinWidth у всех колонок шапки и обеих строк, найдено {count}");

        foreach (var key in DefaultWidths.Keys)
            Assert.Contains($"ConverterParameter={key}", xaml, StringComparison.Ordinal);

        // «Название» держит минимум имени базы.
        Assert.Contains("MinWidth=\"{x:Static local:ColumnHeaderMetrics.NameColumnMinWidth}\"",
            xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Header_WrapsInsteadOfTrimming()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // 11 подписей колонок — у каждой перенос строк и фиксированная высота шапки.
        var wrapCount = CountOccurrences(xaml, "TextWrapping=\"Wrap\"");
        Assert.True(wrapCount >= 11, $"ожидался перенос у всех подписей шапки, найдено {wrapCount}");
        Assert.Contains("Height=\"{x:Static local:ColumnHeaderMetrics.HeaderHeight}\"",
            xaml, StringComparison.Ordinal);
        // Горизонтальный StackPanel не давал тексту ширину колонки — у последней
        // колонки шапки («Последняя копия») его больше нет, там сетка «иконка + текст».
        Assert.DoesNotContain("<StackPanel Grid.Column=\"12\"",
            xaml, StringComparison.Ordinal);
    }

    // ===================== Avalonia (код) =====================

    [Fact]
    public void Avalonia_HeaderAndRows_UseSharedMetrics()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));

        Assert.Contains("MinWidth = ColumnHeaderMetrics.MinWidthFor(columns[i].Key)", source, StringComparison.Ordinal);
        Assert.Contains("MinWidth = ColumnHeaderMetrics.NameColumnMinWidth", source, StringComparison.Ordinal);
        Assert.Contains("Height = ColumnHeaderMetrics.HeaderHeight", source, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", source, StringComparison.Ordinal);
        // Перенос работает только в сетке: горизонтальный StackPanel меряет текст
        // без ограничения ширины, поэтому подпись строится сеткой «иконка + текст».
        Assert.Contains("grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });",
            source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(block, 1);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ResizeAndWidthSum_RespectMinWidth()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));

        Assert.Contains("var headerMinWidth = ColumnHeaderMetrics.MinWidthFor(_resizeKey);", source, StringComparison.Ordinal);
        Assert.Contains("d.Width.IsAbsolute ? Math.Max(d.Width.Value, d.MinWidth) : 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ResizeAndWidthSum_RespectMinWidth()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        Assert.Contains("private double MinWidthForColumn(ColumnDefinition? column)", source, StringComparison.Ordinal);
        Assert.Contains("var headerMinWidth = MinWidthForColumn(_resizeColumn);", source, StringComparison.Ordinal);
        Assert.Contains("d.Width.IsAbsolute ? Math.Max(d.Width.Value, d.MinWidth) : d.MinWidth", source, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Fact]
    public void ColumnHeaders_AreShortEnoughForTwoLines()
    {
        // Заголовки берутся из локализации: длинная подпись не должна появиться
        // без пересмотра метрик (иначе снова обрезка или третья строка).
        foreach (var language in new[] { "ru.json", "en.json" })
        {
            var path = Path.Combine("Workspace1C", "Localization", "Languages", language);
            using var document = JsonDocument.Parse(ReadProjectFile(path));
            // Словарь локализации лежит во вложенном объекте "strings".
            var root = document.RootElement.GetProperty("strings");

            foreach (var key in DefaultWidths.Keys.Append("Name"))
            {
                var localizationKey = "Column." + key;
                Assert.True(root.TryGetProperty(localizationKey, out var element),
                    $"{language}: нет ключа {localizationKey}");

                var text = element.GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: пустой заголовок {localizationKey}");
                Assert.True(text.Length <= (int)ColumnHeaderMetrics.MaxHeaderChars,
                    $"{language}: «{text}» ({localizationKey}) длиннее {ColumnHeaderMetrics.MaxHeaderChars} символов");
                Assert.DoesNotContain("…", text, StringComparison.Ordinal);
            }
        }
    }
}
