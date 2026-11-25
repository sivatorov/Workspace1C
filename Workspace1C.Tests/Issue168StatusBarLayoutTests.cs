using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #168 «Порядок в панели статусов»: иконки управления и надпись версии с её
/// кнопкой должны стоять <b>справа</b>, а все появляющиеся длинные информационные
/// надписи (итог синхронизации, индикатор фоновых загрузок, в том числе сводка при
/// старте) — <b>слева от всех кнопок</b>, не сдвигая версию и кнопки управления.
/// </summary>
/// <remarks>
/// <para>Avalonia-код собирается только под <c>#if LINUX</c>, поэтому проверки —
/// source-guard'ы по исходникам (по образцу <see cref="Issue114StatusBarTooltipTests"/>
/// и <see cref="Issue116SkipVersionTests"/>).</para>
/// <para>Существующие стражи подвала (<c>Issue116*</c>, <c>Issue129*</c>,
/// <c>Issue131*</c>) не ослабляются: значок обновления остаётся слева от версии,
/// колокольчик — перед кнопкой «Текущая сессия».</para>
/// </remarks>
public sealed class Issue168StatusBarLayoutTests
{
    private const string WpfWindowPath = "Workspace1C/Views/MainWindow.xaml";
    private const string AvaloniaWindowPath = "Workspace1C/Views/MainWindow.Avalonia.cs";

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

    /// <summary>Фрагмент WPF-разметки строки состояния (от StatusBarBorder до оверлея загрузки).</summary>
    private static string WpfStatusBar()
    {
        var xaml = ReadProjectFile(WpfWindowPath);
        var start = xaml.IndexOf("x:Name=\"StatusBarBorder\"", StringComparison.Ordinal);
        Assert.True(start > 0, "MainWindow.xaml: не найдена строка состояния (StatusBarBorder)");
        var end = xaml.IndexOf("Индикатор фоновой загрузки при запуске", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.xaml: не найден конец строки состояния");
        return xaml.Substring(start, end - start);
    }

    /// <summary>Метод BuildStatusBar из Avalonia-версии главного окна.</summary>
    private static string AvaloniaStatusBar()
    {
        // Нормализуем переводы строк: файл под Windows — CRLF.
        var code = ReadProjectFile(AvaloniaWindowPath).Replace("\r\n", "\n");
        const string signature = "private Control BuildStatusBar()";
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.Avalonia.cs: не найден BuildStatusBar");

        // Конец метода — первая закрывающая скобка метода на уровне отступа 8 пробелов.
        var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.Avalonia.cs: не найден конец BuildStatusBar");
        return code.Substring(start, end - start);
    }

    // ===================== WPF =====================

    [Fact]
    public void WpfStatusBar_InfoLeftOfControls()
    {
        var bar = WpfStatusBar();

        // Информационная область — вложенный Grid в колонке 0 (*).
        Assert.Contains("<Grid Grid.Column=\"0\" Margin=\"0,0,8,0\" ClipToBounds=\"True\">", bar);

        // Правый кластер — только Auto-колонки 1–4: отмена загрузок, значок обновления
        // с версией, колокольчик/сессия, правая панель.
        Assert.Contains("<Button Grid.Column=\"1\" Command=\"{Binding CancelDownloadsCommand}\"", bar);
        Assert.Contains(
            "<StackPanel Grid.Column=\"2\" Orientation=\"Horizontal\" VerticalAlignment=\"Center\" Margin=\"16,0,0,0\">",
            bar);
        Assert.Contains("<StackPanel Grid.Column=\"3\" Orientation=\"Horizontal\" VerticalAlignment=\"Center\">", bar);
        Assert.Contains("<Button Grid.Column=\"4\" Command=\"{Binding ToggleRightPanelDetailsCommand}\"", bar);

        // Шестой колонки больше нет — информационные надписи не занимают Auto-колонку
        // правее версии.
        Assert.DoesNotContain("Grid.Column=\"5\"", bar);
    }

    [Fact]
    public void WpfStatusBar_InfoColumnIsStar_AndTrims()
    {
        var bar = WpfStatusBar();

        // Первая (внешняя) колонка — растягивающаяся.
        var firstColumn = bar.IndexOf("<ColumnDefinition Width=\"*\"", StringComparison.Ordinal);
        var firstAuto = bar.IndexOf("<ColumnDefinition Width=\"Auto\"", StringComparison.Ordinal);
        Assert.True(firstColumn >= 0 && firstAuto > firstColumn,
            "Первая колонка строки состояния должна быть * (информационная)");

        // У информационных текстов — обрезка многоточием, ограничение ширины и
        // полный текст в подсказке.
        Assert.Contains("Text=\"{Binding StatusBarInfo}\"", bar);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\" MinWidth=\"0\"", bar);
        Assert.Contains("MaxWidth=\"260\" TextTrimming=\"CharacterEllipsis\"", bar);
        Assert.Contains("ToolTip=\"{Binding SyncMessage}\"", bar);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\" MaxWidth=\"360\"", bar);
    }

    [Fact]
    public void WpfStatusBar_InfoTextsBeforeVersionAndButtons()
    {
        var bar = WpfStatusBar();

        var statusInfo = bar.IndexOf("Text=\"{Binding StatusBarInfo}\"", StringComparison.Ordinal);
        var sync = bar.IndexOf("Text=\"{Binding SyncMessage}\"", StringComparison.Ordinal);
        var downloads = bar.IndexOf("Text=\"{Binding DownloadsStatusText}\"", StringComparison.Ordinal);
        var cancel = bar.IndexOf("Command=\"{Binding CancelDownloadsCommand}\"", StringComparison.Ordinal);
        var version = bar.IndexOf("x:Name=\"FooterVersionText\"", StringComparison.Ordinal);
        var session = bar.IndexOf("ToggleSessionLaunchPanelCommand", StringComparison.Ordinal);

        Assert.True(statusInfo > 0 && sync > 0 && downloads > 0 && cancel > 0 && version > 0 && session > 0,
            "Не все элементы строки состояния найдены");
        Assert.True(statusInfo < sync && sync < downloads,
            "Информационные надписи должны идти в порядке: ИД базы → синхронизация → загрузки");
        Assert.True(downloads < cancel, "Отмена загрузок должна стоять сразу после индикатора");
        Assert.True(cancel < version, "Кнопка отмены загрузок должна быть левее версии");
        Assert.True(version < session, "Версия и её кнопка должны быть левее кнопки «Текущая сессия»");
    }

    // ===================== Avalonia =====================

    [Fact]
    public void AvaloniaStatusBar_HasNoPhantomColumn()
    {
        var bar = AvaloniaStatusBar();

        // Раньше панель загрузок ставилась в несуществующую колонку 5.
        Assert.DoesNotContain("Grid.SetColumn(downloadsPanel, 5)", bar);

        var columns = Regex.Matches(bar, @"grid\.ColumnDefinitions\.Add\(new ColumnDefinition");
        Assert.Equal(5, columns.Count);

        // Все индексы Grid.SetColumn укладываются в созданные колонки.
        foreach (Match match in Regex.Matches(bar, @"Grid\.SetColumn\(\w+, (\d+)\)"))
        {
            var index = int.Parse(match.Groups[1].Value);
            Assert.True(index < columns.Count, $"Grid.SetColumn с индексом {index} вне созданных колонок");
        }
    }

    [Fact]
    public void AvaloniaStatusBar_FooterIsRightOfInfo()
    {
        var bar = AvaloniaStatusBar();

        Assert.Contains("Grid.SetColumn(infoPanel, 0)", bar);
        Assert.Contains("Grid.SetColumn(cancelDownloadsBtn, 1)", bar);
        Assert.Contains("Grid.SetColumn(footerPanel, 2)", bar);
        Assert.Contains("Grid.SetColumn(sessionPanel, 3)", bar);
        Assert.Contains("Grid.SetColumn(toggleBtn, 4)", bar);

        // Порядок задаётся индексами колонок, а не порядком строк кода.
        Assert.True(ColumnOf(bar, "infoPanel") < ColumnOf(bar, "cancelDownloadsBtn"));
        Assert.True(ColumnOf(bar, "cancelDownloadsBtn") < ColumnOf(bar, "footerPanel"));
        Assert.True(ColumnOf(bar, "footerPanel") < ColumnOf(bar, "sessionPanel"));
        Assert.True(ColumnOf(bar, "sessionPanel") < ColumnOf(bar, "toggleBtn"));
    }

    [Fact]
    public void AvaloniaStatusBar_InfoTextsTrim()
    {
        var bar = AvaloniaStatusBar();

        // Информационная область — одна *-колонка, тексты обрезаются и имеют подсказку.
        Assert.Contains("var infoPanel = new Grid { Margin = new Thickness(0, 0, 8, 0), ClipToBounds = true };", bar);
        Assert.Contains("MaxWidth = 260", bar);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", bar);
        Assert.Contains("MaxWidth = 360", bar);
        Assert.Contains("_syncMessage.Bind(ToolTip.TipProperty, new Binding(\"SyncMessage\"))", bar);
        Assert.Contains("downloadsText.Bind(ToolTip.TipProperty, new Binding(\"DownloadsStatusText\"))", bar);

        // Текст/прогресс/ETA — в информационной области, кнопка отмены — отдельно.
        Assert.Contains("infoPanel.Children.Add(downloadsPanel);", bar);
        Assert.Contains("Grid.SetColumn(downloadsPanel, 2)", bar);
        Assert.Contains("grid.Children.Add(cancelDownloadsBtn);", bar);
    }

    /// <summary>Индекс колонки из ближайшего <c>Grid.SetColumn(element, N)</c>.</summary>
    private static int ColumnOf(string bar, string element)
    {
        var match = Regex.Match(bar, @"Grid\.SetColumn\(" + Regex.Escape(element) + @", (\d+)\)");
        Assert.True(match.Success, $"Не найден Grid.SetColumn для {element}");
        return int.Parse(match.Groups[1].Value);
    }

    // ===================== Паритет и сохранение привязок =====================

    [Fact]
    public void StatusBar_KeepsBindings()
    {
        foreach (var path in new[] { WpfWindowPath, AvaloniaWindowPath })
        {
            var text = ReadProjectFile(path);
            Assert.Contains("HasActiveDownloads", text);
            Assert.Contains("DownloadsStatusText", text);
            Assert.Contains("ActiveDownloadsProgress", text);
            Assert.Contains("DownloadsEtaText", text);
            Assert.Contains("CancelDownloadsCommand", text);
            Assert.Contains("SyncMessage", text);
        }
    }

    [Fact]
    public void StatusBar_Parity_WpfAndAvalonia()
    {
        var wpf = WpfStatusBar();
        var avalonia = AvaloniaStatusBar();

        // Обе оболочки: информация слева (колонка 0), правый кластер — отмена (1),
        // версия (2), сессия (3), правая панель (4).
        Assert.Contains("<Grid Grid.Column=\"0\" Margin=\"0,0,8,0\" ClipToBounds=\"True\">", wpf);
        Assert.Contains("Grid.SetColumn(infoPanel, 0)", avalonia);

        Assert.Contains("<Button Grid.Column=\"1\" Command=\"{Binding CancelDownloadsCommand}\"", wpf);
        Assert.Contains("Grid.SetColumn(cancelDownloadsBtn, 1)", avalonia);

        Assert.Contains("x:Name=\"FooterVersionText\"", wpf);
        Assert.Contains("_footerVersionText", avalonia);

        Assert.Contains("ToggleSessionLaunchPanelCommand", wpf);
        Assert.Contains("ToggleSessionLaunchPanelCommand", avalonia);

        Assert.Contains("ToggleRightPanelDetailsCommand", wpf);
        Assert.Contains("ToggleRightPanelDetailsCommand", avalonia);
    }
}
