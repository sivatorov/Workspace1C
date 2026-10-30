using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты окна «Сменить платформу», «Выбор родительской группы»,
/// «Настройки» и «Настройка информационной базы» (issue #55, комментарий ArtyoZ):
/// элементы не должны обрезаться при уменьшении размеров окон — минимальные размеры,
/// прокрутка, перенос меток, звёздные колонки вместо фиксированных ширин.
/// Тесты читают исходные XAML/код из репозитория (как Issue41WindowLayoutTests),
/// поэтому работают на обеих платформах без запуска UI.
/// </summary>
public sealed class Issue55WindowLayoutTests
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

    private static string ViewsDir => Path.Combine(FindRepoRoot(), "Workspace1C", "Views");

    // ------------------------------------------------------------------
    // Окно «Сменить платформу» (issue #55): при уменьшении окна элементы
    // обрезались — комбобокс с фиксированной шириной в горизонтальном
    // StackPanel уезжал за правый край; окно было NoResize без прокрутки.
    // ------------------------------------------------------------------

    [Fact]
    public void PlatformChangeWindow_Wpf_ResizableMinSize_GridRow_AutoScroll_Wrap()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.xaml"));

        // Окно можно изменять и оно не сжимается меньше минимального размера.
        Assert.DoesNotContain("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"380\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"240\"", xaml, StringComparison.Ordinal);

        // У комбобокса нет фиксированной ширины: строка «подпись + поле» лежит
        // в Grid Auto+* — поле сжимается вместе со звёздной колонкой.
        Assert.DoesNotContain("Width=\"260\"", xaml, StringComparison.Ordinal);
        var labelIdx = xaml.IndexOf("PlatformChange.PlatformLabel", StringComparison.Ordinal);
        Assert.True(labelIdx >= 0, "Подпись выбора версии не найдена в разметке.");
        var region = xaml.Substring(Math.Max(0, labelIdx - 400), 1200);
        Assert.Contains("<Grid", region, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"Auto\"", region, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"*\"", region, StringComparison.Ordinal);

        // Перенос сводки и результата + прокрутка по вертикали.
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformChangeWindow_Avalonia_ResizableMinSize_GridRow_AutoScroll_Wrap()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.Avalonia.cs"));

        Assert.DoesNotContain("CanResize = false;", code, StringComparison.Ordinal);
        Assert.Contains("CanResize = true;", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 380;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 240;", code, StringComparison.Ordinal);

        // Комбобокс без фиксированной ширины в звёздной колонке Grid.
        Assert.DoesNotContain("Width = 260,", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 180", code, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinition(GridLength.Auto)", code, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinition(GridLength.Star)", code, StringComparison.Ordinal);

        // Прокрутка по вертикали и перенос сводки/результата уже включены.
        Assert.Contains("new ScrollViewer", code, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
        Assert.Contains("TextWrapping.Wrap", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Окно «Выбор родительской группы»: заголовок/подзаголовок без переноса
    // обрезались правым краем при уменьшении ширины окна.
    // ------------------------------------------------------------------

    [Fact]
    public void GroupPickerWindow_Wpf_HeaderTitlesWrapText()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "GroupPickerWindow.xaml"));

        Assert.Contains("MinWidth=\"460\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);

        var titlePos = xaml.IndexOf("x:Name=\"TitleText\"", StringComparison.Ordinal);
        var subtitlePos = xaml.IndexOf("x:Name=\"SubtitleText\"", StringComparison.Ordinal);
        Assert.True(titlePos >= 0 && subtitlePos >= 0, "Заголовок/подзаголовок не найдены в разметке.");
        var titleRegion = xaml.Substring(titlePos, Math.Min(400, xaml.Length - titlePos));
        var subtitleRegion = xaml.Substring(subtitlePos, Math.Min(400, xaml.Length - subtitlePos));
        Assert.Contains("TextWrapping=\"Wrap\"", titleRegion, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", subtitleRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupPickerWindow_Avalonia_HeaderTitleWraps()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "GroupPickerWindow.Avalonia.cs"));

        Assert.Contains("MinWidth = 460;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 480;", code, StringComparison.Ordinal);
        Assert.Contains("title.TextWrapping = TextWrapping.Wrap;", code, StringComparison.Ordinal);
        Assert.Contains("subtitle.TextWrapping = TextWrapping.Wrap;", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // «Настройка информационной базы»: колонка подписей Auto с минимумом,
    // перенос меток чекбоксов, прокрутка вкладок, перенос подзаголовка.
    // ------------------------------------------------------------------

    [Fact]
    public void ConnectionSettingsWindow_Wpf_LabelColumnAutoMinWidth_TabsScroll_HeaderWrap()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));

        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"760\"", xaml, StringComparison.Ordinal);
        // issue #93: минимум окна снижен с 780 до 640 — окно стало компактным.
        Assert.Contains("MinHeight=\"640\"", xaml, StringComparison.Ordinal);

        // Колонка подписей Auto с минимумом 120 (чекбоксы «Размер вручную» и
        // «Приватная база» в ней не обрезаются при узком окне).
        Assert.Contains("ColumnDefinition Width=\"Auto\" MinWidth=\"120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Connection.ManualSizeLabel}\" TextWrapping=\"Wrap", xaml, StringComparison.Ordinal);
        Assert.Contains("Connection.PrivateBase}\" TextWrapping=\"Wrap", xaml, StringComparison.Ordinal);

        // Каждая вкладка прокручивается по вертикали.
        var scrollCount = Regex.Matches(xaml, "<ScrollViewer").Count;
        Assert.True(scrollCount >= 8, $"Вкладок с прокруткой должно быть не менее 8, найдено {scrollCount}.");

        // Подзаголовок шапки переносится.
        var subtitleIdx = xaml.IndexOf("Connection.HeaderSubtitle", StringComparison.Ordinal);
        Assert.True(subtitleIdx >= 0, "Подзаголовок шапки не найден в разметке.");
        var headerRegion = xaml.Substring(subtitleIdx, Math.Min(300, xaml.Length - subtitleIdx));
        Assert.Contains("TextWrapping=\"Wrap\"", headerRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_LabelColumnAutoMinWidth_TabsScroll_HeaderWrap()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));

        Assert.Contains("MinWidth = 760;", code, StringComparison.Ordinal);
        // issue #93: минимум окна снижен с 780 до 640 — окно стало компактным.
        Assert.Contains("MinHeight = 640;", code, StringComparison.Ordinal);

        // FieldsGrid: колонка подписей Auto с минимумом LabelColumn.
        Assert.Contains("Width = GridLength.Auto, MinWidth = LabelColumn", code, StringComparison.Ordinal);
        // Чекбоксы «Размер вручную» и «Приватная база» — TextBlock с переносом.
        Assert.Contains("Connection.ManualSizeLabel", code, StringComparison.Ordinal);
        Assert.Contains("Connection.PrivateBase", code, StringComparison.Ordinal);
        Assert.Contains("TextWrapping.Wrap", code, StringComparison.Ordinal);

        // Вкладки обёрнуты в ScrollViewer.
        Assert.Contains("new ScrollViewer", code, StringComparison.Ordinal);
        Assert.Contains("ScrollBarVisibility.Auto", code, StringComparison.Ordinal);

        // Подзаголовок шапки переносится.
        var subtitleIdx = code.IndexOf("Connection.HeaderSubtitle", StringComparison.Ordinal);
        Assert.True(subtitleIdx >= 0, "Подзаголовок шапки не найден в коде.");
        var headerRegion = code.Substring(subtitleIdx, Math.Min(400, code.Length - subtitleIdx));
        Assert.Contains("TextWrapping = TextWrapping.Wrap", headerRegion, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // «Настройки»: минимальные размеры + прокрутка нижнего блока вкладки
    // «Платформы» (фикс 1.0.0.66, issue #55).
    // ------------------------------------------------------------------

    [Fact]
    public void SettingsWindow_Wpf_MinSize_And_PlatformsTabScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"960\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"720\"", xaml, StringComparison.Ordinal);

        // Вкладка «Платформы»: нижний блок (статус/пути/разрядность) в ScrollViewer.
        var treePos = xaml.IndexOf("PlatformsTree", StringComparison.Ordinal);
        var scrollPos = xaml.IndexOf("Grid.Row=\"2\" VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal);
        Assert.True(treePos >= 0, "Дерево версий платформ не найдено в разметке.");
        Assert.True(scrollPos > treePos, "Прокрутка нижнего блока вкладки «Платформы» должна быть ПОД деревом версий.");

        var scrollCount = Regex.Matches(xaml, "<ScrollViewer").Count;
        Assert.True(scrollCount >= 10, $"Прокруток в «Настройках» должно быть не менее 10, найдено {scrollCount}.");
    }

    [Fact]
    public void SettingsWindow_Avalonia_MinSize_And_PlatformsTabScroll()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        Assert.Contains("MinWidth = 960;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 720;", code, StringComparison.Ordinal);
        Assert.Contains("new ScrollViewer", code, StringComparison.Ordinal);
        // Нижняя строка вкладки «Платформы» (статус/пути/разрядность) живёт в ScrollViewer.
        Assert.Contains("ScrollViewer.SetVerticalScrollBarVisibility", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Глобальный аудит всех форм (issue #55, «перешерстить все формы»):
    // каждое изменяемое окно WPF обязано объявлять MinWidth и MinHeight —
    // иначе при уменьшении размеров контент гарантированно обрезается.
    // Исключение — окна ResizeMode="NoResize": их размер жёстко задан.
    // ------------------------------------------------------------------

    [Fact]
    public void AllResizableWpfWindows_HaveMinWidthAndMinHeight()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ViewsDir, "*.xaml").OrderBy(f => Path.GetFileName(f)))
        {
            var xaml = File.ReadAllText(file);
            if (!IsWindowXaml(xaml))
                continue;
            // Окна без изменения размера исключаются: их нельзя уменьшить.
            if (Regex.IsMatch(xaml, @"ResizeMode=""NoResize"""))
                continue;

            var name = Path.GetFileName(file);
            if (!Regex.IsMatch(xaml, @"MinWidth=""[0-9.]+"""))
                offenders.Add($"{name}: отсутствует MinWidth");
            if (!Regex.IsMatch(xaml, @"MinHeight=""[0-9.]+"""))
                offenders.Add($"{name}: отсутствует MinHeight");
        }

        Assert.True(
            offenders.Count == 0,
            "Окна, изменяющие размер, без минимальных размеров (обрезание при уменьшении, issue #55):\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void AllResizableAvaloniaWindows_HaveMinWidthAndMinHeight()
    {
        var offenders = new List<string>();
        // Avalonia-окна строятся кодом: ищем окна ModalWindowBase с Width,
        // у которых нет MinWidth/MinHeight и CanResize=false.
        foreach (var file in Directory.EnumerateFiles(ViewsDir, "*.Avalonia.cs").OrderBy(f => Path.GetFileName(f)))
        {
            var code = File.ReadAllText(file);
            if (!code.Contains("ModalWindowBase", StringComparison.Ordinal))
                continue;
            if (code.Contains("CanResize = false;", StringComparison.Ordinal))
                continue; // размер зафиксирован — окно нельзя уменьшить

            var name = Path.GetFileName(file);
            // Присваивание размера окна — оператор с начала строки ("Width = 760;"
            // или "MinHeight = changeMode ? 400 : 320;"), а не ширина дочернего
            // контрола ("Width = 300," / "_cancelButton.Width = 110;").
            var setsWidth = Regex.IsMatch(code, @"(?m)^\s*Width\s*=\s*[^;]*;");
            var hasMinWidth = Regex.IsMatch(code, @"(?m)^\s*MinWidth\s*=\s*[^;]*;");
            var hasMinHeight = Regex.IsMatch(code, @"(?m)^\s*MinHeight\s*=\s*[^;]*;");
            if (setsWidth && !hasMinWidth)
                offenders.Add($"{name}: отсутствует MinWidth");
            if (setsWidth && !hasMinHeight)
                offenders.Add($"{name}: отсутствует MinHeight");
        }

        Assert.True(
            offenders.Count == 0,
            "Avalonia-окна с изменяемым размером без минимальных размеров (issue #55):\n" +
            string.Join("\n", offenders));
    }

    // ------------------------------------------------------------------
    // Окна Avalonia, собираемые разметкой .axaml (issue #55): изменяемые
    // окна обязаны объявлять MinWidth/MinHeight в разметке, как XAML-аналоги.
    // ------------------------------------------------------------------

    [Fact]
    public void AllResizableAvaloniaAxamlWindows_HaveMinWidthAndMinHeight()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ViewsDir, "*.axaml").OrderBy(f => Path.GetFileName(f)))
        {
            var axaml = File.ReadAllText(file);
            if (!axaml.Contains("ModalWindowBase", StringComparison.Ordinal) &&
                !Regex.IsMatch(axaml, @"<(\w+:)?Window\b"))
                continue;
            // Окна без изменения размера исключаются: их нельзя сузить.
            if (Regex.IsMatch(axaml, @"CanResize=""False"""))
                continue;

            var name = Path.GetFileName(file);
            if (!Regex.IsMatch(axaml, @"MinWidth=""[0-9.]+"""))
                offenders.Add($"{name}: отсутствует MinWidth");
            if (!Regex.IsMatch(axaml, @"MinHeight=""[0-9.]+"""))
                offenders.Add($"{name}: отсутствует MinHeight");
        }

        Assert.True(
            offenders.Count == 0,
            "Avalonia-окна .axaml с изменяемым размером без минимальных размеров (issue #55):\n" +
            string.Join("\n", offenders));
    }

    // ------------------------------------------------------------------
    // Регрессии 1.0.0.82 (полный аудит issue #55): исправленные окна.
    // ------------------------------------------------------------------

    [Fact]
    public void BaseSelectionWindow_Avalonia_MinSizesPreventShrink()
    {
        // Окно выбора баз изменяемое (CanResize="True") — минимальные размеры
        // синхронны Windows-аналогу BaseSelectionWindow.xaml (420x360).
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "BaseSelectionWindow.axaml"));
        Assert.Contains("CanResize=\"True\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"420\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"360\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorWindow_MinSizesPreventShrink_WpfAndAvalonia()
    {
        // Монитор серверов: минимумы 1120x560 на обеих платформах (Avalonia задавал
        // 560 по высоте, WPF — 480; синхронизировано по верхней границе).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));
        Assert.Contains("MinWidth=\"1120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));
        Assert.Contains("MinWidth = 1120;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 560;", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Негативные сценарии (issue #55): минимумы не должны превышать стартовый
    // размер окна (иначе окно открывалось бы иным размером) и должны влезать
    // в рабочий размер маленького экрана (1280x720, ширина).
    // ------------------------------------------------------------------

    [Fact]
    public void ResizableWindowMinSizes_DoNotExceedStartupSize()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ViewsDir, "*.xaml").OrderBy(f => Path.GetFileName(f)))
        {
            var xaml = File.ReadAllText(file);
            if (!IsWindowXaml(xaml))
                continue;
            // Окна без изменения размера сузить нельзя — минимумы им не нужны.
            if (Regex.IsMatch(xaml, @"ResizeMode=""NoResize"""))
                continue;

            // Атрибуты корневого тега окна: ширины дочерних контролов не попадают.
            var rootTagEnd = xaml.IndexOf('>');
            var head = rootTagEnd > 0 ? xaml.Substring(0, rootTagEnd) : xaml;

            static double GetAttr(string headLocal, string attr)
            {
                var m = Regex.Match(headLocal, attr + "\"([0-9.]+)\"");
                return m.Success
                    ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                    : double.NaN;
            }

            var minWidth = GetAttr(head, "MinWidth=");
            var minHeight = GetAttr(head, "MinHeight=");
            if (double.IsNaN(minWidth) || double.IsNaN(minHeight))
                continue; // поймает отдельный тест-аудит существования минимумов

            var width = GetAttr(head, "Width=");
            var height = GetAttr(head, "Height=");
            var name = Path.GetFileName(file);

            if (!double.IsNaN(width) && minWidth > width)
                offenders.Add($"{name}: MinWidth={minWidth} > стартовой ширины {width}");
            if (!double.IsNaN(height) && minHeight > height)
                offenders.Add($"{name}: MinHeight={minHeight} > стартовой высоты {height}");
        }

        Assert.True(
            offenders.Count == 0,
            "Минимумы окон не должны превышать стартовые размеры (issue #55):\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void ResizableWindowMinWidth_FitsSmallScreenWidth()
    {
        // Рабочая ширина экрана 1280 (1366x768 и 1280x720): даже самый широкий
        // минимум обязан открыться целиком без горизонтальных «хвостов».
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ViewsDir, "*.xaml").OrderBy(f => Path.GetFileName(f)))
        {
            var xaml = File.ReadAllText(file);
            if (!IsWindowXaml(xaml))
                continue;
            if (Regex.IsMatch(xaml, @"ResizeMode=""NoResize"""))
                continue;

            var rootTagEnd = xaml.IndexOf('>');
            var head = rootTagEnd > 0 ? xaml.Substring(0, rootTagEnd) : xaml;
            var m = Regex.Match(head, "MinWidth=\"([0-9.]+)\"");
            if (!m.Success)
                continue;

            var minWidth = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (minWidth > 1280)
                offenders.Add($"{Path.GetFileName(file)}: MinWidth={minWidth} превышает рабочую ширину экрана 1280");
        }

        Assert.True(
            offenders.Count == 0,
            "Минимумы ширины обязаны влезать в рабочий размер маленького экрана (issue #55):\n" +
            string.Join("\n", offenders));
    }

    /// <summary>Признак, что XAML объявляет окно (а не панель/контрол).</summary>
    private static bool IsWindowXaml(string xaml)
    {
        return Regex.IsMatch(xaml, @"<(\w+:)?Window\b") ||
               Regex.IsMatch(xaml, "x:Class=\"Workspace1C\\.\\w*Window\"");
    }
}
