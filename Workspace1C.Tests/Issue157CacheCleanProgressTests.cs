using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #157 «Очистка кэша — индикация расчёта» (релиз 1.0.1.118):
/// <list type="bullet">
/// <item>приоритетный и стабильный порядок расчёта: отмеченные базы первыми;</item>
/// <item>прогресс расчёта (<see cref="CacheSizeProgress"/>) без деления на ноль;</item>
/// <item>ограничение параллелизма и приоритетная очередь ожидающих баз;</item>
/// <item>страж-тесты WPF/Avalonia: индикатор, статус, блокировка «Очистить», отмена
/// по закрытию окна;</item>
/// <item>ключи локализации в ru.json/en.json.</item>
/// </list>
/// </summary>
public sealed class Issue157CacheCleanProgressTests
{
    private static Infobase Base(string name) => new() { Id = name, Name = name };

    // ===================== Порядок очереди =====================

    [Fact]
    public void Order_SelectedFirst_Stable()
    {
        var a = Base("A");
        var b = Base("B");
        var c = Base("C");
        var d = Base("D");
        var all = new[] { a, b, c, d };

        var ordered = CacheSizeCalculation.Order(all, ib => ReferenceEquals(ib, c) || ReferenceEquals(ib, b));

        Assert.Equal(new[] { b, c, a, d }, ordered);
    }

    [Fact]
    public void Order_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(CacheSizeCalculation.Order(null, _ => true));
        Assert.Empty(CacheSizeCalculation.Order(Array.Empty<Infobase>(), _ => true));
    }

    [Fact]
    public void Order_EmptySelection_KeepsListOrder()
    {
        var a = Base("A");
        var b = Base("B");
        var c = Base("C");

        var ordered = CacheSizeCalculation.Order(new[] { a, b, c }, _ => false);

        Assert.Equal(new[] { a, b, c }, ordered);
    }

    [Fact]
    public void Order_AllSelected_KeepsOrder()
    {
        var a = Base("A");
        var b = Base("B");
        var c = Base("C");

        var ordered = CacheSizeCalculation.Order(new[] { a, b, c }, _ => true);

        Assert.Equal(new[] { a, b, c }, ordered);
    }

    // ===================== Прогресс =====================

    [Fact]
    public void Progress_ReportsCompletedTotalAndDone()
    {
        var progress = new CacheSizeProgress(3);
        Assert.Equal(3, progress.Total);
        Assert.Equal(0, progress.Completed);
        Assert.False(progress.IsComplete);
        Assert.Equal("CacheClean.Calculating", progress.StatusKey);

        progress.ReportCompleted();
        progress.ReportCompleted();
        Assert.Equal(2, progress.Completed);
        Assert.False(progress.IsComplete);

        progress.ReportCompleted();
        Assert.True(progress.IsComplete);
        Assert.Equal("CacheClean.CalculatingDone", progress.StatusKey);

        // Лишние вызовы не переполняют счётчик.
        progress.ReportCompleted();
        Assert.Equal(3, progress.Completed);
    }

    [Fact]
    public void Progress_Percent_Clamped()
    {
        var empty = new CacheSizeProgress(0);
        Assert.True(empty.IsComplete);
        Assert.Equal(100, empty.Percent);

        var progress = new CacheSizeProgress(4);
        Assert.Equal(0, progress.Percent);
        progress.ReportCompleted();
        Assert.Equal(25, progress.Percent);
        progress.ReportCompleted();
        progress.ReportCompleted();
        progress.ReportCompleted();
        Assert.Equal(100, progress.Percent);
        Assert.InRange(progress.Percent, 0, 100);
    }

    // ===================== Параллелизм и очередь =====================

    [Fact]
    public void MaxConcurrency_IsClampedBetweenTwoAndFour()
    {
        Assert.InRange(CacheSizeCalculation.MaxConcurrency,
            CacheSizeCalculation.MinDegreeOfParallelism,
            CacheSizeCalculation.MaxDegreeOfParallelism);
        Assert.True(CacheSizeCalculation.MaxConcurrency <= 4);
    }

    [Fact]
    public void Queue_Prioritize_MovesPendingBaseToFront()
    {
        var a = Base("A");
        var b = Base("B");
        var c = Base("C");
        var queue = new CacheSizeQueue(new[] { a, b, c });

        queue.Prioritize(c);

        Assert.True(queue.TryDequeue(out var first));
        Assert.Same(c, first);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Same(a, second);
        Assert.True(queue.TryDequeue(out var third));
        Assert.Same(b, third);
        Assert.False(queue.TryDequeue(out _));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Queue_Prioritize_AlreadyDequeued_IsNoOp()
    {
        var a = Base("A");
        var b = Base("B");
        var queue = new CacheSizeQueue(new[] { a, b });
        Assert.True(queue.TryDequeue(out var first));
        Assert.Same(a, first);

        // Уже посчитанная база не возвращается в очередь.
        queue.Prioritize(a);
        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Same(b, second);
    }

    // ===================== Source-guard WPF/Avalonia =====================

    [Fact]
    public void WpfWindow_HasCalculatingIndicator()
    {
        var xaml = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml");
        var code = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");

        Assert.Contains("SizesProgressBar", xaml, StringComparison.Ordinal);
        Assert.Contains("SizesStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("CacheClean.Calculating", xaml, StringComparison.Ordinal);
        Assert.Contains("_isCalculating", code, StringComparison.Ordinal);
        Assert.Contains("UpdateCalculatingState", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasCalculatingIndicator()
    {
        var code = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        Assert.Contains("_sizesProgressBar", code, StringComparison.Ordinal);
        Assert.Contains("_sizesStatusText", code, StringComparison.Ordinal);
        Assert.Contains("CacheClean.Calculating", code, StringComparison.Ordinal);
        Assert.Contains("_isCalculating", code, StringComparison.Ordinal);
        Assert.Contains("UpdateCalculatingState", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanButton_DisabledWhileCalculating()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        Assert.Contains("!_isCalculating", wpf, StringComparison.Ordinal);
        Assert.Contains("CacheClean.CleanBlockedTooltip", wpf, StringComparison.Ordinal);
        Assert.Contains("!_isCalculating", avalonia, StringComparison.Ordinal);
        Assert.Contains("CacheClean.CleanBlockedTooltip", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_UsePrioritizedOrder()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        Assert.Contains("CacheSizeCalculation.Order", wpf, StringComparison.Ordinal);
        Assert.Contains("CacheSizeCalculation.Order", avalonia, StringComparison.Ordinal);
        Assert.Contains("CacheSizeCalculation.MaxConcurrency", wpf, StringComparison.Ordinal);
        Assert.Contains("CacheSizeCalculation.MaxConcurrency", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_CancelCalculationOnClosing_WithoutUnhandledExceptions()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            Assert.Contains("CancelCalculation();", source, StringComparison.Ordinal);
            Assert.Contains("_calculationCts?.Cancel()", source, StringComparison.Ordinal);
            Assert.Contains("catch (OperationCanceledException)", source, StringComparison.Ordinal);
            Assert.Contains("CacheClean.NotCalculated", source, StringComparison.Ordinal);
        }
    }

    // ===================== Текст статуса по состояниям (issue #157, 7OH) =====================

    [Fact]
    public void StatusText_WhileCalculating_IsUpdating()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            // Пока идёт расчёт, показывается «обновляются» — и стартовый текст,
            // и текст с прогрессом.
            var state = ExtractMethod(source, "private void UpdateCalculatingState");
            Assert.Contains("CacheClean.Calculating", state, StringComparison.Ordinal);

            // Прогресс не переключает строку на финальный текст: «рассчитаны» —
            // только после исчезновения полосы (issue #157, комментарий 7OH).
            var progress = ExtractMethod(source, "private void UpdateProgressStatus");
            Assert.DoesNotContain("CacheClean.CalculatingDone", progress, StringComparison.Ordinal);
            Assert.Contains("CacheClean.Calculating", progress, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StatusText_AfterComplete_IsCalculated()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            var state = ExtractMethod(source, "private void UpdateCalculatingState");
            // Ветка calculating == false — «Размеры кэша рассчитаны».
            Assert.Contains("CacheClean.CalculatingDone", state, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProgressBar_Hidden_WhenComplete()
    {
        var wpf = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C/Views/CacheCleanWindow.Avalonia.cs");

        // Порядок в UpdateCalculatingState: сначала скрываем полосу, затем меняем текст.
        var wpfState = ExtractMethod(wpf, "private void UpdateCalculatingState");
        Assert.True(
            wpfState.IndexOf("SizesProgressBar.Visibility", StringComparison.Ordinal) <
            wpfState.IndexOf("SizesStatusText.Text", StringComparison.Ordinal),
            "WPF: полоса прогресса должна скрываться до смены текста статуса.");

        var avaloniaState = ExtractMethod(avalonia, "private void UpdateCalculatingState");
        Assert.True(
            avaloniaState.IndexOf("_sizesProgressBar.IsVisible", StringComparison.Ordinal) <
            avaloniaState.IndexOf("_sizesStatusText.Text", StringComparison.Ordinal),
            "Avalonia: полоса прогресса должна скрываться до смены текста статуса.");
    }

    [Fact]
    public void Localization_UsesNewWording()
    {
        var ru = LoadStrings("ru.json");
        var en = LoadStrings("en.json");

        Assert.Equal("Размеры кэша обновляются", ru["CacheClean.Calculating"]);
        Assert.Equal("Размеры кэша обновляются… {0} из {1}", ru["CacheClean.CalculatingProgress"]);
        Assert.Equal("Размеры кэша рассчитаны", ru["CacheClean.CalculatingDone"]);

        Assert.Equal("Cache sizes are being updated", en["CacheClean.Calculating"]);
        Assert.Equal("Cache sizes are being updated… {0} of {1}", en["CacheClean.CalculatingProgress"]);
    }

    // ===================== Локализация =====================

    [Fact]
    public void Localization_HasCacheCleanProgressKeys()
    {
        var ru = LoadStrings("ru.json");
        var en = LoadStrings("en.json");

        foreach (var key in new[]
                 {
                     "CacheClean.Calculating",
                     "CacheClean.CalculatingProgress",
                     "CacheClean.CalculatingDone",
                     "CacheClean.CleanBlockedTooltip",
                     "CacheClean.NotCalculated"
                 })
        {
            Assert.True(ru.ContainsKey(key), $"ru.json: нет ключа {key}");
            Assert.True(en.ContainsKey(key), $"en.json: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(ru[key]), $"ru.json: пустой ключ {key}");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"en.json: пустой ключ {key}");
        }

        // Терминология — «кэш» в UI (issue #157).
        Assert.Contains("кэш", ru["CacheClean.Calculating"], StringComparison.OrdinalIgnoreCase);
    }

    // ===================== Вспомогательное =====================

    /// <summary>
    /// Вырезает исходный текст метода по сигнатуре (до следующего метода того же
    /// уровня) — для проверки порядка операторов и используемых ключей локализации.
    /// </summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден метод: {signature}");
        var end = source.IndexOf("private void ", start + signature.Length, StringComparison.Ordinal);
        if (end < 0)
            end = source.Length;
        return source[start..end];
    }

    private static Dictionary<string, string> LoadStrings(string languageFile)
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Localization", "Languages", languageFile);
        Assert.True(File.Exists(path), $"Файл локализации не найден: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
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
}
