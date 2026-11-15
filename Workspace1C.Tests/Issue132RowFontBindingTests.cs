using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #132 «Шрифт строк списка слетает после сохранения настроек».
/// Причина: проход «По умолчанию» в <see cref="ThemeManager.ApplyElementFonts"/>
/// обходил всё дерево и вызывал <c>SetValue</c> на уже привязанных свойствах
/// строк списка. <c>SetValue</c> заменяет Binding локальным значением — привязки
/// RowNameFontSize / RowSecondaryFontSize / RowBadgeFontSize / RowHotkeyFontSize
/// снимались, а локальная запись затирала SemiBold у уже созданных строк.
/// Исправление:
/// <list type="bullet">
/// <item>обход «По умолчанию» исключает поддерево MainTree (skipSubtree);</item>
/// <item>шрифт области «Список баз» пишется только корню дерева, строки наследуют его;</item>
/// <item><c>ApplyFontProps</c> пишет значения только через <c>SetFontValueUnlessBound</c>,
/// который не трогает свойства с BindingExpressionBase;</item>
/// <item>в XAML-шаблоне строки размеры шрифта заданы только привязками к Row*FontSize;</item>
/// <item>Avalonia: <c>SaveElementFonts</c> пересчитывает масштаб списка (ApplyListZoom)
/// до сохранения настроек, а <c>ApplyDisplaySettings</c> — нет.</item>
/// </list>
/// </summary>
public sealed class Issue132RowFontBindingTests
{
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

    private static string ExtractFragment(string source, string signature, string nextSignature, string description)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{description}: не найден фрагмент «{signature}».");
        var end = source.IndexOf(nextSignature, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{description}: не найден маркер конца «{nextSignature}».");
        return source.Substring(start, end - start);
    }

    private static int CountOccurrences(string text, string value)
        => Regex.Matches(text, Regex.Escape(value)).Count;

    // ===================== WPF: ThemeManager =====================

    [Fact]
    public void Wpf_ApplyElementFonts_DefaultPass_SkipsMainTreeAndWritesListFontToRoot()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var method = ExtractFragment(
            source,
            "public static void ApplyElementFonts(",
            "private static void ApplyFontToNamed(",
            "ThemeManager.ApplyElementFonts");

        // Корень списка берётся по имени, но обход всего окна «По умолчанию»
        // больше не должен доходить до MainTree отдельным вызовом.
        Assert.Contains("var listRoot = window.FindName(\"MainTree\") as FrameworkElement;", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFontToNamed(window, \"MainTree\"", method, StringComparison.Ordinal);

        // Проход «По умолчанию» вызывается с skipSubtree = listRoot.
        Assert.Contains(
            "ApplyFontToTree(content, Scope(FontDefault) ?? new ElementFontSettings(), listRoot);",
            method,
            StringComparison.Ordinal);

        // Шрифт области «Список баз» применяется только к корню дерева.
        Assert.Contains(
            "ApplyFontProps(listRoot, Scope(FontList) ?? Scope(FontDefault) ?? new ElementFontSettings());",
            method,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ApplyFontToTree_SkipSubtreeGuard_ExcludesWholeSubtree()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var overload = ExtractFragment(
            source,
            "public static void ApplyFontToTree(DependencyObject root, ElementFontSettings? fs, DependencyObject? skipSubtree)",
            "private static void ApplyFontProps(",
            "ThemeManager.ApplyFontToTree(skipSubtree)");

        // Исключённое поддерево не получает значений вовсе — ни на корне, ни на потомках.
        Assert.Contains("ReferenceEquals(root, skipSubtree)", overload, StringComparison.Ordinal);
        Assert.Contains(
            "ApplyFontToTree(VisualTreeHelper.GetChild(root, i), fs, skipSubtree);",
            overload,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ApplyFontProps_WritesOnlyThroughSetFontValueUnlessBound()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var method = ExtractFragment(
            source,
            "private static void ApplyFontProps(",
            "private static void SetFontValueUnlessBound(",
            "ThemeManager.ApplyFontProps");

        // Прямых fe.SetValue(TextElement.Font*Property, ...) быть не должно:
        // они снимают привязки Row*FontSize у уже созданных строк.
        Assert.DoesNotContain("fe.SetValue(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(TextElement.Font", method, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(method, "SetFontValueUnlessBound(fe, TextElement.Font"));
        Assert.Contains("SetFontValueUnlessBound(fe, TextElement.FontFamilyProperty", method, StringComparison.Ordinal);
        Assert.Contains("SetFontValueUnlessBound(fe, TextElement.FontSizeProperty", method, StringComparison.Ordinal);
        Assert.Contains("SetFontValueUnlessBound(fe, TextElement.FontWeightProperty", method, StringComparison.Ordinal);
        Assert.Contains("SetFontValueUnlessBound(fe, TextElement.FontStyleProperty", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_SetFontValueUnlessBound_DoesNotOverwriteExistingBinding()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "ThemeManager.cs"));
        var method = ExtractFragment(
            source,
            "private static void SetFontValueUnlessBound(",
            "public static void ApplyElementFonts(",
            "ThemeManager.SetFontValueUnlessBound");

        Assert.Contains("BindingOperations.GetBindingExpressionBase(d, property) is not null", method, StringComparison.Ordinal);
        Assert.Contains("d.SetValue(property, value);", method, StringComparison.Ordinal);

        // Порядок охраны: проверка привязки → return → только потом запись значения.
        var bindingCheck = method.IndexOf("BindingOperations.GetBindingExpressionBase", StringComparison.Ordinal);
        var earlyReturn = method.IndexOf("return;", StringComparison.Ordinal);
        var write = method.IndexOf("d.SetValue(property, value);", StringComparison.Ordinal);
        Assert.True(bindingCheck >= 0 && earlyReturn > bindingCheck && write > earlyReturn,
            "SetFontValueUnlessBound должен возвращаться до SetValue, если свойство привязано.");
    }

    // ===================== WPF: XAML шаблона строки =====================

    [Fact]
    public void Wpf_InfobaseRowTemplate_FontSizesComeOnlyFromRowBindings()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        var keyIndex = xaml.IndexOf("x:Key=\"InfobaseRowTemplate\"", StringComparison.Ordinal);
        Assert.True(keyIndex > 0, "MainWindow.xaml: шаблон InfobaseRowTemplate не найден.");
        var templateStart = xaml.LastIndexOf("<DataTemplate", keyIndex, StringComparison.Ordinal);
        Assert.True(templateStart >= 0, "MainWindow.xaml: не найдено открытие DataTemplate InfobaseRowTemplate.");
        var templateEnd = xaml.IndexOf("</DataTemplate>", keyIndex, StringComparison.Ordinal);
        Assert.True(templateEnd > templateStart, "MainWindow.xaml: не найден конец InfobaseRowTemplate.");
        var fragment = xaml.Substring(templateStart, templateEnd - templateStart);

        // Никаких литеральных размеров внутри шаблона строки.
        Assert.False(Regex.IsMatch(fragment, "FontSize\\s*=\\s*\"\\s*[0-9]"),
            "В InfobaseRowTemplate найден литеральный FontSize — размеры строк должны приходить только привязками.");

        // Все FontSize шаблона — привязки к Row*FontSize (единый источник размеров).
        var sizes = Regex.Matches(fragment, "FontSize\\s*=\\s*\"([^\"]+)\"");
        Assert.Equal(13, sizes.Count);
        foreach (Match match in sizes)
        {
            var value = match.Groups[1].Value;
            Assert.True(
                value.Contains("RowNameFontSize", StringComparison.Ordinal) ||
                value.Contains("RowSecondaryFontSize", StringComparison.Ordinal) ||
                value.Contains("RowBadgeFontSize", StringComparison.Ordinal) ||
                value.Contains("RowHotkeyFontSize", StringComparison.Ordinal),
                $"В InfobaseRowTemplate найден FontSize не из Row*FontSize: {value}");
            Assert.Contains("DataContext.", value, StringComparison.Ordinal);
        }

        // Начертание названия базы остаётся локальным SemiBold, а размер — привязкой.
        var nameTextStart = fragment.IndexOf("x:Name=\"NameText\"", StringComparison.Ordinal);
        Assert.True(nameTextStart > 0, "InfobaseRowTemplate: NameText не найден.");
        var nameTagEnd = fragment.IndexOf('>', nameTextStart);
        Assert.True(nameTagEnd > nameTextStart, "InfobaseRowTemplate: не найден конец тега NameText.");
        var nameTag = fragment.Substring(nameTextStart, nameTagEnd - nameTextStart);
        Assert.Contains("FontWeight=\"SemiBold\"", nameTag, StringComparison.Ordinal);
        Assert.Contains("RowNameFontSize", nameTag, StringComparison.Ordinal);
    }

    // ===================== Avalonia: порядок SaveElementFonts =====================

    [Fact]
    public void Avalonia_SaveElementFonts_AppliesListZoom_BeforeSaving()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Display.cs"));

        var save = ExtractFragment(
            source,
            "public void SaveElementFonts(",
            "private static MainWindow? MainWindowOrNull()",
            "MainViewModel.SaveElementFonts (Avalonia)");
        var apply = ExtractFragment(
            source,
            "public void ApplyDisplaySettings(",
            "public void SetColumnVisible(",
            "MainViewModel.ApplyDisplaySettings (Avalonia)");

        Assert.Contains("ApplyListZoom();", save, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyListZoom();", apply, StringComparison.Ordinal);

        // Порядок: применить шрифты → пересчитать масштаб/пересобрать дерево → сохранить.
        var preview = save.IndexOf("PreviewElementFonts(_settings.ElementFonts);", StringComparison.Ordinal);
        var zoom = save.IndexOf("ApplyListZoom();", StringComparison.Ordinal);
        var persist = save.IndexOf("SaveSettingsSilently();", StringComparison.Ordinal);
        Assert.True(preview >= 0 && zoom > preview && persist > zoom,
            "SaveElementFonts должен вызывать ApplyListZoom после PreviewElementFonts и до SaveSettingsSilently.");
    }

    // ===================== Функциональная проверка вычисления размеров =====================
#if !LINUX
    /// <summary>
    /// Повторное применение одинаковых настроек шрифтов не меняет вычисленные
    /// RowNameFontSize/RowSecondaryFontSize. Конструктор MainViewModel создаёт
    /// репозиторий, WPF-диалоги и подписки на сервисы, поэтому берём неинициализированный
    /// экземпляр и заполняем только поля, от которых зависят свойства размеров
    /// (тем же способом, каким это делает SaveElementFonts: новая копия словаря).
    /// </summary>
    [Theory]
    [InlineData(false, 1.0)]
    [InlineData(true, 0.9)]
    public void RepeatedSameFontSettings_KeepComputedRowSizes(bool compactMode, double compactFactor)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));

        var fontsField = typeof(MainViewModel).GetField("_elementFonts", BindingFlags.Instance | BindingFlags.NonPublic);
        var zoomField = typeof(MainViewModel).GetField("_listZoomFactor", BindingFlags.Instance | BindingFlags.NonPublic);
        var compactField = typeof(MainViewModel).GetField("_compactMode", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(fontsField);
        Assert.NotNull(zoomField);
        Assert.NotNull(compactField);

        static Dictionary<string, ElementFontSettings> CreateFonts() => new()
        {
            [ThemeManager.FontList] = new ElementFontSettings { FontFamily = "Segoe UI", FontSize = 15 },
            [ThemeManager.FontDefault] = new ElementFontSettings { FontFamily = "Segoe UI", FontSize = 13 }
        };

        zoomField!.SetValue(vm, 1.0);
        compactField!.SetValue(vm, compactMode);
        fontsField!.SetValue(vm, CreateFonts());

        var nameBefore = vm.RowNameFontSize;
        var secondaryBefore = vm.RowSecondaryFontSize;

        // Повторное применение тех же настроек: SaveElementFonts кладёт новую копию словаря.
        fontsField.SetValue(vm, CreateFonts());

        Assert.Equal(nameBefore, vm.RowNameFontSize);
        Assert.Equal(secondaryBefore, vm.RowSecondaryFontSize);

        // Значения по-прежнему вычисляются из области «Список баз» (15 / 13) и компактности.
        Assert.Equal(15.0 * compactFactor, nameBefore, 6);
        Assert.Equal(12.0 * 15.0 / 13.0 * compactFactor, secondaryBefore, 6);
    }
#endif
}
