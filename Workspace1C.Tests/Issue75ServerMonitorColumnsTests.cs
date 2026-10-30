using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты окна «Серверы 1С» (issue #75 «Серверы 1С — колонки»): колонки
/// вкладок «Сеансы/Соединения/Блокировки/Рабочие процессы» на обеих платформах
/// (WPF XAML + Avalonia-код) имеют MinWidth по содержимому заголовка (худший случай
/// ru/en + стрелка сортировки — расчёт <see cref="ServerMonitorColumnWidthsStore"/>),
/// стартовая ширина фиксированных колонок не меньше текста заголовка, а таблицы
/// допускают горизонтальную прокрутку (колонки уходят за границу области).
/// </summary>
public sealed class Issue75ServerMonitorColumnsTests
{
    // ===================== Помощники чтения репозитория/локали =====================

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

    private static IReadOnlyDictionary<string, string> LoadStrings(string languageFile)
    {
        using var doc = JsonDocument.Parse(ReadProjectFile(languageFile));
        var root = doc.RootElement;
        if (!root.TryGetProperty("strings", out var strings))
            return new Dictionary<string, string>();
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return dict;
    }

    /// <summary>Худший случай ru/en для ключа локализации колонки.</summary>
    private static double ExpectedMinWidth(IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en, string key)
    {
        ru.TryGetValue(key, out var ruText);
        en.TryGetValue(key, out var enText);
        return ServerMonitorColumnWidthsStore.HeaderMinWidth(ruText, enText);
    }

    // ===================== Состав колонок вкладок =====================

    /// <summary>Ключи локализации колонок вкладки (null — не локализованный заголовок: PID).</summary>
    private static readonly string?[] SessionsKeys =
    {
        "ServerMonitor.Columns.Session.User",
        "ServerMonitor.Columns.Session.Infobase",
        "ServerMonitor.Columns.Session.Host",
        "ServerMonitor.Columns.Session.App",
        "ServerMonitor.Columns.Session.StartedAt",
        "ServerMonitor.Columns.Session.LastActive",
        "ServerMonitor.Columns.Session.State",
        "ServerMonitor.Columns.Session.Blocked",
        "ServerMonitor.Columns.Session.Memory",
        "ServerMonitor.Columns.Session.Duration",
        "ServerMonitor.Columns.Session.DurationCurrent",
        "ServerMonitor.Columns.Session.Hibernate",
    };

    private static readonly string?[] ConnectionsKeys =
    {
        "ServerMonitor.Columns.Connection.Host",
        "ServerMonitor.Columns.Connection.Port",
        "ServerMonitor.Columns.Connection.Connector",
        "ServerMonitor.Columns.Connection.Established",
        "ServerMonitor.Columns.Connection.LastActive",
        "ServerMonitor.Columns.Connection.Duration",
        "ServerMonitor.Columns.Connection.Descr",
        "ServerMonitor.Columns.Connection.Blocked",
    };

    private static readonly string?[] LocksKeys =
    {
        "ServerMonitor.Columns.Lock.Object",
        "ServerMonitor.Columns.Lock.Waiting",
        "ServerMonitor.Columns.Lock.Blocking",
        "ServerMonitor.Columns.Lock.Session",
        "ServerMonitor.Columns.Lock.Connection",
        "ServerMonitor.Columns.Lock.Transaction",
    };

    private static readonly string?[] ProcessesKeys =
    {
        "ServerMonitor.Columns.Process.Type",
        "ServerMonitor.Columns.Process.Host",
        null, // PID — не локализованный заголовок, минимум абсолютный
        "ServerMonitor.Columns.Process.Port",
        "ServerMonitor.Columns.Process.StartedAt",
        "ServerMonitor.Columns.Process.Memory",
        "ServerMonitor.Columns.Process.Threads",
        "ServerMonitor.Columns.Process.Cpu",
        "ServerMonitor.Columns.Process.Running",
        "ServerMonitor.Columns.Process.Infobases",
    };

    // ===================== WPF XAML =====================

    [Fact]
    public void Wpf_AllFourGrids_AllowHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        foreach (var gridName in new[] { "SessionsGrid", "ConnectionsGrid", "LocksGrid", "ProcessesGrid" })
        {
            var region = GridRegion(xaml, gridName);
            Assert.True(
                region.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal),
                $"Грид {gridName} должен допускать горизонтальную прокрутку (issue #75).");
        }
    }

    [Fact]
    public void Wpf_Columns_HaveMinWidthForLongestHeaderRuEn()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));
        var ru = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        AssertColumnsMinWidth(xaml, "SessionsGrid", SessionsKeys, ru, en);
        AssertColumnsMinWidth(xaml, "ConnectionsGrid", ConnectionsKeys, ru, en);
        AssertColumnsMinWidth(xaml, "LocksGrid", LocksKeys, ru, en);
        AssertColumnsMinWidth(xaml, "ProcessesGrid", ProcessesKeys, ru, en);
    }

    [Fact]
    public void Wpf_Columns_StartWidth_NotBelowMinWidth()
    {
        // issue #75: «стартовая ширина» фиксированных колонок не меньше MinWidth —
        // заголовок помещается сразу после открытия окна.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        foreach (var gridName in new[] { "SessionsGrid", "ConnectionsGrid", "LocksGrid", "ProcessesGrid" })
        {
            var region = GridRegion(xaml, gridName);
            foreach (var columnStart in RegionColumns(region))
            {
                var widthText = ReadAttribute(columnStart, "Width");
                var minWidthText = ReadAttribute(columnStart, "MinWidth");
                if (widthText is null || minWidthText is null)
                    continue;
                if (widthText.EndsWith("*", StringComparison.Ordinal))
                    continue; // гибкие колонки ограничены MinWidth, стартовая задаётся звёздами
                Assert.True(ParseNumber(minWidthText, out var minWidth),
                    $"{gridName}: не удалось разобрать MinWidth колонки (issue #75).");
                Assert.True(ParseNumber(widthText, out var startWidth),
                    $"{gridName}: не удалось разобрать Width колонки (issue #75).");
                Assert.True(startWidth >= minWidth,
                    $"{gridName}: стартовая ширина {startWidth} меньше MinWidth={minWidth} (issue #75).");
            }
        }
    }

    private static void AssertColumnsMinWidth(
        string xaml, string gridName, IEnumerable<string?> keys,
        IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en)
    {
        var region = GridRegion(xaml, gridName);
        foreach (var key in keys)
        {
            var marker = key is null ? "Header=\"PID\"" : $"Header=\"{{loc:Loc {key}}}\"";
            Assert.True(region.Contains(marker, StringComparison.Ordinal),
                $"{gridName}: не найдена колонка {marker}.");

            var expected = key is null
                ? ServerMonitorColumnWidthsStore.AbsoluteMinWidth
                : ExpectedMinWidth(ru, en, key);
            var expectedAttribute = $"MinWidth=\"{FormatWidth(expected)}\"";
            var before = region.Substring(0, region.IndexOf(marker, StringComparison.Ordinal));
            var tail = before.Length <= 140 ? before : before.Substring(before.Length - 140);
            Assert.True(tail.Contains(expectedAttribute, StringComparison.Ordinal),
                $"{gridName}/{key}: ожидается MinWidth=\"{FormatWidth(expected)}\" «заголовок + стрелка» (issue #75).");
        }
    }

    private static string GridRegion(string xaml, string gridName)
    {
        var start = xaml.IndexOf($"<DataGrid x:Name=\"{gridName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Грид {gridName} не найден в разметке.");
        var end = xaml.IndexOf("</DataGrid>", start, StringComparison.Ordinal);
        return xaml.Substring(start, end - start);
    }

    private static IEnumerable<string> RegionColumns(string region)
    {
        var parts = region.Split("<DataGridTextColumn ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Select(p => p.Substring(0, Math.Min(220, p.Length)));
    }

    private static string? ReadAttribute(string columnText, string name)
    {
        var marker = $"{name}=\"";
        var idx = columnText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var valueStart = idx + marker.Length;
        var valueEnd = columnText.IndexOf('"', valueStart);
        return valueEnd < 0 ? null : columnText.Substring(valueStart, valueEnd - valueStart);
    }

    private static bool ParseNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static string FormatWidth(double value) => Math.Round(value, 0).ToString(CultureInfo.InvariantCulture);

    // ===================== Avalonia/Linux =====================

    [Fact]
    public void Avalonia_TableHasHorizontalScrollAndRowScrollsTogether()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        // Заголовок и строки — общий контент горизонтального ScrollViewer (issue #75).
        Assert.True(source.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal),
            "Avalonia: таблица вкладок должна иметь горизонтальную прокрутку (issue #75).");
        Assert.True(source.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Disabled", StringComparison.Ordinal),
            "Avalonia: горизонтальный ScrollViewer не должен перехватывать вертикаль у ListBox.");
        Assert.True(source.Contains("Content = dock", StringComparison.Ordinal),
            "Avalonia: заголовок и строки должны прокручиваться вместе.");
        Assert.True(source.Contains("UpdateTableMinWidth", StringComparison.Ordinal),
            "Avalonia: должна быть синхронизация минимальной ширины контента с вьюпортом.");
    }

    [Fact]
    public void Avalonia_ColumnsHaveMinWidthInHeaderGrid()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        // MinWidth задаётся колонкам и шапки, и строк; при перетаскивании колонка
        // не сжимается ниже минимума (issue #75).
        Assert.True(source.Contains("new ColumnDefinition(widths[i]) { MinWidth = columns[i].MinWidth }", StringComparison.Ordinal),
            "Avalonia: MinWidth должен задаваться колонкам шапки.");
        Assert.True(source.Contains("rowGrid.ColumnDefinitions[i].MinWidth = columns[i].MinWidth", StringComparison.Ordinal),
            "Avalonia: MinWidth должен задаваться колонкам строк.");
        Assert.True(source.Contains("Math.Max(30, c.MinWidth)", StringComparison.Ordinal),
            "Avalonia: перетаскивание не должно сжимать колонку ниже минимума.");
    }

    [Fact]
    public void Wpf_HeaderTracking_UsesSafeTyping_NoInvalidCastException()
    {
        // issue #75 (комментарии 7OH 27.10): при открытии окна «Серверы 1С» выбрасывался
        // InvalidCastException в AttachHeaderWidthTracking — presenter.Items в момент
        // ApplicationIdle содержит не только DataGridColumnHeader, а Cast<> падает.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml.cs"));

        // Сборка заголовков идёт через OfType<> (безопасно для не-заголовков)…
        Assert.True(source.Contains("presenter.Items.OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>()", StringComparison.Ordinal),
            "WPF: сбор заголовков должен использовать OfType<> (не Cast<>) — иначе InvalidCastException (issue #75).");
        // …и опасный Cast<> по заголовкам отсутствует.
        Assert.False(source.Contains("presenter.Items.Cast<", StringComparison.Ordinal),
            "WPF: Cast<> по коллекции presenter.Items должен быть удалён (issue #75).");
        // Перехват штатной «ручки» изменения размера заголовка для поведения как в 1С.
        Assert.True(source.Contains("PART_RightHeaderGripper", StringComparison.Ordinal),
            "WPF: перехват PART_RightHeaderGripper должен присутствовать (issue #75).");
        Assert.True(source.Contains("ItemContainerGenerator.StatusChanged", StringComparison.Ordinal),
            "WPF: должна быть дозагрузка заголовков по генератору контейнеров (issue #75).");
    }

    [Fact]
    public void BothPlatforms_UseOneCStyleColumnDragSemantics()
    {
        // issue #75 (1.0.0.96+): общая семантика перетаскивания как в 1С — обычное
        // перетаскивание сдвигает ВСЕ колонки справа блоком (ширины не меняются, при
        // переполнении появляется прокрутка), с Ctrl меняется ширина только двух смежных
        // колонок. Обе платформы применяют единую чистую математику
        // ServerMonitorColumnWidthsStore.ApplyDragMoveBlock / ApplyDragResizePair.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml.cs"));
        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        Assert.True(wpf.Contains("ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(", StringComparison.Ordinal),
            "WPF: обычный режим должен применять ApplyDragMoveBlock (сдвиг блока колонок справа, issue #75).");
        Assert.True(wpf.Contains("ServerMonitorColumnWidthsStore.ApplyDragResizePair(", StringComparison.Ordinal),
            "WPF: Ctrl-режим должен применять ApplyDragResizePair (только две смежные колонки, issue #75).");
        Assert.True(wpf.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Control)", StringComparison.Ordinal),
            "WPF: должен быть перехват Ctrl при перетаскивании (issue #75).");

        // issue #75 (репорт 7OH от 2026-10-30): индекс границы рассчитывается по
        // координате клика (dragIndex), а не «колонкой грипа».
        Assert.True(avalonia.Contains("ServerMonitorColumnWidthsStore.ApplyDragMoveBlock(startWidths, dragIndex, dx, minWidths)", StringComparison.Ordinal),
            "Avalonia: обычный режим должен применять ApplyDragMoveBlock (issue #75).");
        Assert.True(avalonia.Contains("ServerMonitorColumnWidthsStore.ApplyDragResizePair(startWidths, dragIndex, dx, minWidths)", StringComparison.Ordinal),
            "Avalonia: Ctrl-режим должен применять ApplyDragResizePair (issue #75).");
        Assert.True(avalonia.Contains("Avalonia.Input.KeyModifiers.Control", StringComparison.Ordinal),
            "Avalonia: должен быть перехват Ctrl при перетаскивании (issue #75).");
    }

    [Fact]
    public void Wpf_ColumnDrag_InterceptedAtGridLevel_NotOnFragileHeaderTracking()
    {
        // issue #75 (комментарий 7OH 2026-10-29: «вообще ничего не меняется… ужимает 2 справа
        // до определённого предела и всё»): прежние фиксы вешали перехват на Thumb «ручки»
        // (PART_RightHeaderGripper, 1.0.0.96) и на PreviewMouseLeftButtonDown самих заголовков
        // (1.0.1.1/1.0.1.3) — в реальном приложении срабатывал штатный ресайз DataGrid
        // («звёздные» колонки справа ужимались до минимума), потому что заголовки
        // материализуются/пересоздаются при переключении вкладок и перехват на них
        // не успевал навеситься либо внутренний обработчик заголовка срабатывал раньше.
        // Перехват должен висеть НА ГРИДЕ: PreviewMouseLeftButtonDown грида срабатывает ДО
        // любого обработчика заголовка (tunnel-фаза), не зависит от материализации заголовков,
        // заголовок под курсором находится подъёмом от источника события (FindAncestor),
        // мышь на время перетаскивания захватывается гридом.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml.cs"));

        Assert.True(source.Contains("grid.PreviewMouseLeftButtonDown += ", StringComparison.Ordinal),
            "WPF: перехват должен висеть на гриде (grid.PreviewMouseLeftButtonDown), а не на заголовке (issue #75).");
        Assert.True(source.Contains("AttachGridColumnDrag", StringComparison.Ordinal),
            "WPF: должна быть привязка общего перехвата на гриде AttachGridColumnDrag (issue #75).");
        Assert.True(source.Contains("FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>", StringComparison.Ordinal),
            "WPF: заголовок под курсором должен определяться подъёмом от источника клика (issue #75).");
        Assert.True(source.Contains("grid.CaptureMouse()", StringComparison.Ordinal),
            "WPF: мышь на время перетаскивания должна захватываться гридом (issue #75).");
        Assert.True(source.Contains("LostMouseCapture", StringComparison.Ordinal),
            "WPF: потеря захвата должна корректно завершать перетаскивание (issue #75).");
        Assert.True(source.Contains("HeaderResizeHotZone", StringComparison.Ordinal),
            "WPF: должна быть «зона границы» начала перетаскивания (issue #75).");
        // Перехват на отдельных заголовках больше НЕ используется — он и был причиной поломки.
        Assert.False(source.Contains("header.PreviewMouseLeftButtonDown += ", StringComparison.Ordinal),
            "WPF: перехват на заголовках должен быть удалён — только грид-уровневый перехват (issue #75).");
    }

    [Fact]
    public void Wpf_ColumnDrag_ReinforcedInterception_SecondBeltAndRobustStart()
    {
        // issue #75 (1.0.1.5, этап 1 — «гарантированное перехватывание штатного ресайза»):
        // у пользователя перехват на гриде (1.0.1.4) всё равно не срабатывал («поведение
        // всё то же»), поэтому перехват усилен: второй пояс на презентере заголовков,
        // форсированный сброс захвата штатного Thumb, гашение штатного ресайза при
        // движении по ручке, расширенная зона границы, координаты от грида и
        // диагностика под гейтом CM_RAC.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml.cs"));

        // Второй пояс перехвата — тот же обработчик на презентере заголовков.
        Assert.True(source.Contains("_dragInterceptedPresenters.Add(presenter)", StringComparison.Ordinal),
            "WPF: должен быть второй пояс перехвата на презентере заголовков (issue #75).");
        Assert.True(source.Contains("presenter.PreviewMouseLeftButtonDown += (_, e) => OnColumnPreviewMouseLeftButtonDown(grid, tabKey, e)", StringComparison.Ordinal),
            "WPF: презентер заголовков должен получать тот же обработчик клика (issue #75).");
        // Форсированный сброс возможного захвата штатного механизма перед захватом гридом.
        Assert.True(source.Contains("grid.ReleaseMouseCapture();", StringComparison.Ordinal),
            "WPF: перед захватом мыши должен быть сброс возможного захвата штатного Thumb (issue #75).");
        // Гашение штатного ресайза при движении по ручке (PART_RightHeaderGripper).
        Assert.True(source.Contains("FindAncestor<System.Windows.Controls.Primitives.Thumb>(d) is not null", StringComparison.Ordinal),
            "WPF: движение по штатной ручке должно гаситься (e.Handled) (issue #75).");
        // issue #75 (репорт 7OH от 2026-10-30): граница определяется ПО КООРДИНАТЕ X
        // клика относительно грида чистой функцией ResolveDragBoundary (ближайшая
        // граница в допуске ±HeaderResizeHotZone/2) — клик по выступу ручки/левой
        // кромке заголовка следующей колонки даёт ту же границу (без off-by-one),
        // клик в центре заголовка драг не стартует.
        Assert.True(source.Contains("ServerMonitorColumnWidthsStore.ResolveDragBoundary(widths, x, tolerance)", StringComparison.Ordinal),
            "WPF: граница должна определяться чистой функцией ResolveDragBoundary по координате X (issue #75).");
        Assert.True(source.Contains("const double tolerance = HeaderResizeHotZone / 2.0;", StringComparison.Ordinal),
            "WPF: допуск зоны границы — половина HeaderResizeHotZone (issue #75).");
        // Клик по штатной ручке (Thumb) — гарантированный жест границы; индекс
        // подтверждается координатой (с fallback на колонку заголовка).
        Assert.True(source.Contains("FindAncestor<System.Windows.Controls.Primitives.Thumb>(source) is not null", StringComparison.Ordinal),
            "WPF: клик по Thumb-ручке должен считаться границей (issue #75).");
        // Координаты движения — от грида (заголовок во время драга может пересоздаваться).
        Assert.True(source.Contains("UpdateColumnDrag(grid, e.GetPosition(grid).X)", StringComparison.Ordinal),
            "WPF: движение должно считать координаты от грида, а не заголовка (issue #75).");
        // Диагностика старта и итогов под гейтом CM_RAC (выключена по умолчанию).
        Assert.True(source.Contains("CM_COLUMNS: старт перетаскивания границы колонки", StringComparison.Ordinal),
            "WPF: должна быть диагностика старта перетаскивания (issue #75, этап 0).");
        Assert.True(source.Contains("CM_COLUMNS: итоговые ширины вкладки", StringComparison.Ordinal),
            "WPF: должна быть диагностика итоговых ширин (issue #75, этап 0).");
        // Оба пояса дедуплицируются наборами — повторная материализация заголовков
        // (переключение вкладок) не плодит обработчики.
        Assert.True(source.Contains("_dragInterceptedGrids.Add(grid)", StringComparison.Ordinal),
            "WPF: дедупликация подписки грида набором (issue #75).");
        Assert.True(source.Contains("_dragInterceptedPresenters.Add(presenter)", StringComparison.Ordinal),
            "WPF: дедупликация подписки презентера набором (issue #75).");
    }

    [Fact]
    public void Avalonia_DragHandlesCaptureLoss_AndUsesSharedMath()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        // Потеря захвата указателя (переключение окна/системная отмена) закрывает цикл
        // перетаскивания — иначе ширины «замирают» после PointerCaptureLost.
        Assert.True(source.Contains("PointerCaptureLost", StringComparison.Ordinal),
            "Avalonia: PointerCaptureLost должен закрывать цикл перетаскивания (issue #75).");
        Assert.True(source.Contains("ApplyDragMoveBlock(startWidths, dragIndex, dx, minWidths)", StringComparison.Ordinal),
            "Avalonia: обычный режим должен применять общую математику Store (issue #75).");
    }

    [Fact]
    public void Avalonia_ColumnGripHitZone_AlignedWithWpfHotZone()
    {
        // issue #75 (комментарий 7OH: «вообще ничего не меняется», целятся в границу и по
        // краю заголовка): прежняя «ручка» 7 px пропускала часть кликов в сортировку вместо
        // перетаскивания. Зона захвата расширена до 24 px (21 px + выступ 3 px за правый
        // край колонки) — та же зона, что в WPF-версии (HeaderResizeHotZone = 24).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        Assert.True(source.Contains("ColumnGripWidth", StringComparison.Ordinal),
            "Avalonia: ширина зоны захвата границы должна быть константой ColumnGripWidth (issue #75).");
        Assert.True(source.Contains("Width = ColumnGripWidth,", StringComparison.Ordinal),
            "Avalonia: «ручка» должна использовать константу ColumnGripWidth (issue #75).");
    }

    [Fact]
    public void Avalonia_ColumnMinWidths_MatchLongestHeaderRuEn()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));
        var ru = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        AssertColumnSpecs(source, SessionsKeys, ru, en);
        AssertColumnSpecs(source, ConnectionsKeys, ru, en);
        AssertColumnSpecs(source, LocksKeys, ru, en);
        AssertColumnSpecs(source, ProcessesKeys, ru, en);
    }

    private static void AssertColumnSpecs(
        string source, IEnumerable<string?> keys,
        IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en)
    {
        foreach (var key in keys)
        {
            var expected = key is null
                ? ServerMonitorColumnWidthsStore.AbsoluteMinWidth
                : ExpectedMinWidth(ru, en, key);

            // Ключ локализации в исходнике колонки.
            var marker = key is null ? "\"Pid\", \"Pid\", Px(90), 60)" : $"LocalizationManager.T(\"{key}\")";
            var idx = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Колонка {key ?? "PID"} не найдена в Avalonia-коде.");
            var lineStart = source.LastIndexOf('\n', idx) + 1;
            var lineEnd = source.IndexOf('\n', idx);
            var line = source.Substring(lineStart, lineEnd - lineStart).TrimEnd();

            var expectedSuffix = $", {FormatWidth(expected)})";
            Assert.True(line.Contains(expectedSuffix, StringComparison.Ordinal),
                $"{key ?? "PID"}: строка колонки должна содержать {expectedSuffix}, фактически: {line} (issue #75).");
        }
    }
}
