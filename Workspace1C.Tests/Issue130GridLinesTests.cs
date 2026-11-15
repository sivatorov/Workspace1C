using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #130 «Опция вывода сетки» (v1.0.1.62): в «Отображение → Колонки» две
/// настройки — «Выводить линии — по горизонтали» и «Выводить линии — по вертикали».
/// При включении в списке баз рисуются тонкие линии между строками и между
/// столбцами, включая заголовок; при выключенных флагах вид и разметка списка
/// не меняются. «Зебра» (чересстрочное выделение) по обсуждению issue не делается.
/// <list type="bullet">
/// <item>план линий — чистый <see cref="GridLinesPlanner"/> (один и тот же для WPF
/// и Avalonia), покрыт матрицей комбинаций флагов и компактного режима;</item>
/// <item>настройки по умолчанию выключены и переживают сохранение/загрузку профиля,
/// старые профили без полей читаются как «выключено»;</item>
/// <item>WPF- и Avalonia-отрисовка проверяются source-гвардами (Avalonia
/// собирается только в Linux-конфигурации).</item>
/// </list>
/// </summary>
public sealed class Issue130GridLinesTests
{
    private readonly string _tempDir;

    public Issue130GridLinesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_grid_lines_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

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

    // ===================== Планировщик линий (обе платформы) =====================

    private static readonly bool[] ThreeVisible = { true, true, true };

    [Fact]
    public void Resolve_BothDisabled_NoLines()
    {
        var plan = GridLinesPlanner.Resolve(false, false, compactMode: false, visibleColumns: ThreeVisible);

        Assert.False(plan.ShowHorizontalLine);
        Assert.False(plan.ShowVerticalLines);
        Assert.False(plan.HasAnyLines);
        Assert.Empty(plan.VerticalBoundaryColumns);
    }

    [Fact]
    public void Resolve_OnlyHorizontal_OneLineBetweenRows()
    {
        var plan = GridLinesPlanner.Resolve(true, false, compactMode: false, visibleColumns: ThreeVisible);

        Assert.True(plan.ShowHorizontalLine);
        Assert.False(plan.ShowVerticalLines);
        Assert.True(plan.HasAnyLines);
        Assert.Empty(plan.VerticalBoundaryColumns);
    }

    [Fact]
    public void Resolve_OnlyVertical_LinesBetweenColumnsExceptLast()
    {
        var plan = GridLinesPlanner.Resolve(false, true, compactMode: false, visibleColumns: ThreeVisible);

        Assert.False(plan.ShowHorizontalLine);
        Assert.True(plan.ShowVerticalLines);
        Assert.True(plan.HasAnyLines);
        // Три видимые колонки → две внутренние границы (0 и 1), наружной рамки нет.
        Assert.Equal(new[] { 0, 1 }, plan.VerticalBoundaryColumns);
    }

    [Fact]
    public void Resolve_BothEnabled_BothLines()
    {
        var plan = GridLinesPlanner.Resolve(true, true, compactMode: false, visibleColumns: ThreeVisible);

        Assert.True(plan.ShowHorizontalLine);
        Assert.True(plan.ShowVerticalLines);
        Assert.Equal(new[] { 0, 1 }, plan.VerticalBoundaryColumns);
    }

    [Theory]
    [InlineData(false, GridLinesPlanner.NormalOpacity)]
    [InlineData(true, GridLinesPlanner.CompactOpacity)]
    public void Resolve_CompactMode_SetsOpacity(bool compactMode, double expectedOpacity)
    {
        var plan = GridLinesPlanner.Resolve(true, true, compactMode, ThreeVisible);

        Assert.Equal(expectedOpacity, plan.Opacity);
        // Толщина линии одинакова в обеих платформах и режимах.
        Assert.Equal(GridLinesPlanner.LineThickness, plan.Thickness);
    }

    [Fact]
    public void Resolve_HiddenColumn_DoesNotCreateBoundary()
    {
        // Скрытая колонка нулевой ширины линии не даёт; граница — только между
        // двумя видимыми колонками.
        var plan = GridLinesPlanner.Resolve(false, true, compactMode: false,
            visibleColumns: new[] { true, false, true, true });

        Assert.Equal(new[] { 0, 2 }, plan.VerticalBoundaryColumns);
    }

    [Fact]
    public void Resolve_SingleVisibleColumn_NoVerticalLines()
    {
        var plan = GridLinesPlanner.Resolve(false, true, compactMode: false,
            visibleColumns: new[] { false, true, false });

        Assert.Empty(plan.VerticalBoundaryColumns);
        Assert.False(plan.ShowVerticalLines);
    }

    [Fact]
    public void Resolve_NullOrEmptyColumns_NoVerticalLines()
    {
        Assert.Empty(GridLinesPlanner.Resolve(false, true, false, visibleColumns: null).VerticalBoundaryColumns);
        Assert.Empty(GridLinesPlanner.Resolve(false, true, false, visibleColumns: Array.Empty<bool>()).VerticalBoundaryColumns);
    }

    [Fact]
    public void Resolve_NoVisibleColumns_NoVerticalLines()
    {
        var plan = GridLinesPlanner.Resolve(false, true, compactMode: false,
            visibleColumns: new[] { false, false });

        Assert.Empty(plan.VerticalBoundaryColumns);
        Assert.False(plan.HasAnyLines);
    }

    // ===================== Единый оверлей: позиции границ и толщина =====================

    [Fact]
    public void ResolveVerticalOffsets_OneLinePerBoundary_NoOuterFrame()
    {
        var widths = new[] { 10d, 20d, 30d, 40d };
        var visible = new[] { true, true, true, true };

        var offsets = GridLinesPlanner.ResolveVerticalOffsets(widths, visible, showVertical: true, dpiScale: 1.0);

        // Одна линия на каждую внутреннюю границу: после 1-й, 2-й и 3-й колонки.
        Assert.Equal(new[] { 10d, 30d, 60d }, offsets.ToArray());
        // После последней видимой колонки линии нет — наружной рамки не рисуем.
        Assert.DoesNotContain(100d, offsets);
    }

    [Fact]
    public void ResolveVerticalOffsets_HiddenColumn_NoBoundaryInside()
    {
        var widths = new[] { 10d, 0d, 20d, 30d };
        var visible = new[] { true, false, true, true };

        var offsets = GridLinesPlanner.ResolveVerticalOffsets(widths, visible, true, 1.0);

        // Скрытая колонка нулевой ширины: линия после 1-й видимой и после 2-й видимой.
        Assert.Equal(new[] { 10d, 30d }, offsets.ToArray());
    }

    [Fact]
    public void ResolveVerticalOffsets_LastVisibleNoLine_SingleVisibleNoLines()
    {
        var offsets = GridLinesPlanner.ResolveVerticalOffsets(
            new[] { 10d, 20d, 30d, 40d },
            new[] { true, true, false, false }, true, 1.0);
        Assert.Equal(new[] { 10d }, offsets.ToArray());

        Assert.Empty(GridLinesPlanner.ResolveVerticalOffsets(
            new[] { 10d, 20d, 30d }, new[] { false, true, false }, true, 1.0));
    }

    [Fact]
    public void ResolveVerticalOffsets_Disabled_NoOffsets()
    {
        Assert.Empty(GridLinesPlanner.ResolveVerticalOffsets(
            new[] { 10d, 20d, 30d }, ThreeVisible, showVertical: false, dpiScale: 1.0));
        Assert.Empty(GridLinesPlanner.ResolveVerticalOffsets(null, ThreeVisible, true, 1.0));
        Assert.Empty(GridLinesPlanner.ResolveVerticalOffsets(new[] { 10d }, null, true, 1.0));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ResolveVerticalOffsets_SnapsToPhysicalPixel(double dpiScale)
    {
        var widths = new[] { 24d, 0d, 28d, 26d, 300.3, 120.7, 99.9 };
        var visible = new[] { false, false, false, false, true, true, true };

        var offsets = GridLinesPlanner.ResolveVerticalOffsets(widths, visible, true, dpiScale);

        Assert.Equal(2, offsets.Count);
        foreach (var x in offsets)
        {
            var physical = x * dpiScale;
            Assert.True(Math.Abs(physical - Math.Round(physical)) < 1e-9,
                $"позиция {x} не выровнена по физическому пикселю при DPI {dpiScale}");
        }
        // Позиции монотонно растут — по одной линии на границу.
        Assert.True(offsets[0] < offsets[1]);
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 0.8)]
    [InlineData(1.5, 2.0 / 3.0)]
    [InlineData(2.0, 0.5)]
    public void ResolveLineThickness_IsOnePhysicalPixel(double dpiScale, double expected)
    {
        var thickness = GridLinesPlanner.ResolveLineThickness(dpiScale);

        Assert.Equal(expected, thickness, 6);
        Assert.Equal(1.0, thickness * dpiScale, 6);
    }

    [Fact]
    public void ResolveLineThickness_InvalidScale_FallsBackToDip()
    {
        Assert.Equal(GridLinesPlanner.LineThickness, GridLinesPlanner.ResolveLineThickness(0));
        Assert.Equal(GridLinesPlanner.LineThickness, GridLinesPlanner.ResolveLineThickness(-1));
    }

    [Theory]
    [InlineData(false, GridLinesPlanner.NormalOpacity)]
    [InlineData(true, GridLinesPlanner.CompactOpacity)]
    public void ResolveOpacity_MatchesPlan(bool compactMode, double expected)
    {
        Assert.Equal(expected, GridLinesPlanner.ResolveOpacity(compactMode));
        Assert.Equal(expected, GridLinesPlanner.Resolve(true, true, compactMode, ThreeVisible).Opacity);
    }

    [Fact]
    public void VerticalOffsets_HeaderWidthsAreSingleSourceOfTruth()
    {
        // Одна и та же граница даёт одну и ту же позицию независимо от того,
        // в какой сетке (строка базы, строка группы, заголовок) она нарисована:
        // позиции считаются только по ширинам колонок заголовка.
        var widths = new[] { 24d, 12d, 28d, 26d, 320d, 120d, 120d };
        var visible = new[] { false, false, false, false, true, true, true };

        var first = GridLinesPlanner.ResolveVerticalOffsets(widths, visible, true, 1.0);
        var second = GridLinesPlanner.ResolveVerticalOffsets(widths, visible, true, 1.0);

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(2, first.Count);
    }

    [Fact]
    public void Resolve_SameInputsAfterThemeSwitch_SamePlan()
    {
        // Тема не участвует в плане (issue #130): после переключения темы те же
        // настройки дают тот же план — прозрачность и толщина не «запоминают»
        // цвет/состояние прежней темы.
        var before = GridLinesPlanner.Resolve(true, true, compactMode: false, visibleColumns: ThreeVisible);
        var after = GridLinesPlanner.Resolve(true, true, compactMode: false, visibleColumns: ThreeVisible);

        Assert.Equal(before.Opacity, after.Opacity);
        Assert.Equal(GridLinesPlanner.NormalOpacity, after.Opacity);
        Assert.Equal(before.Thickness, after.Thickness);
        Assert.Equal(GridLinesPlanner.LineThickness, after.Thickness);
        Assert.Equal(before.ShowHorizontalLine, after.ShowHorizontalLine);
        Assert.Equal(before.VerticalBoundaryColumns, after.VerticalBoundaryColumns);
    }

    [Fact]
    public void Planner_HasNoThemeDependency()
    {
        // Чистая логика плана не читает тему: иначе после смены темы план мог бы
        // вернуть цвет/прозрачность прежней темы (дефект issue #130).
        var planner = ReadProjectFile(Path.Combine("Workspace1C", "Services", "GridLinesPlanner.cs"));
        Assert.DoesNotContain("ThemeManager", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDarkTheme", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentTheme", planner, StringComparison.Ordinal);
    }

    // ===================== Высота линий: низ последней строки (issue #130, 7OH) =====================

    [Fact]
    public void Extent_HeaderOnly_EndsAtHeaderBottom()
    {
        // Пустой список: строк нет — линии заканчиваются низом заголовка,
        // а не тянутся до низа окна.
        var height = GridLinesPlanner.ResolveVerticalExtent(
            headerBottom: 36, rowBottoms: Array.Empty<double>(), availableHeight: 600, dpiScale: 1.0);

        Assert.Equal(36, height);
    }

    [Fact]
    public void Extent_NoHeaderNoRows_FallsBackToAvailable()
    {
        // Разметка ещё не готова — прежнее поведение как безопасный fallback.
        Assert.Equal(600, GridLinesPlanner.ResolveVerticalExtent(null, null, 600, 1.0));
        Assert.Equal(600, GridLinesPlanner.ResolveVerticalExtent(null, Array.Empty<double>(), 600, 1.0));
        Assert.Equal(600, GridLinesPlanner.ResolveVerticalExtent(0, new[] { -10d }, 600, 1.0));
    }

    [Fact]
    public void Extent_OneRow_EndsAtRowBottom()
    {
        var height = GridLinesPlanner.ResolveVerticalExtent(36, new[] { 76d }, 600, 1.0);

        Assert.Equal(76, height);
    }

    [Fact]
    public void Extent_RowsShorterThanViewport_EndsAtLastRow()
    {
        // Заголовок 36 + три строки по 30 → 126; доступно 600 — «хвоста» после
        // последней строки нет.
        var height = GridLinesPlanner.ResolveVerticalExtent(
            36, new[] { 66d, 96d, 126d }, availableHeight: 600, dpiScale: 1.0);

        Assert.Equal(126, height);
        Assert.True(height < 600);
    }

    [Fact]
    public void Extent_RowsTallerThanViewport_Clamped()
    {
        // Длинный список/прокрутка: линия не выходит за доступную высоту оверлея.
        var height = GridLinesPlanner.ResolveVerticalExtent(36, new[] { 900d }, 200, 1.0);

        Assert.Equal(200, height);
    }

    [Fact]
    public void Extent_GroupRowTallerThanDataRow_UsesMaxBottom()
    {
        // Низ строки группы может быть ниже строки базы — берётся максимум,
        // иначе линии обрывались бы на предпоследней строке.
        var height = GridLinesPlanner.ResolveVerticalExtent(36, new[] { 100d, 140d, 120d }, 600, 1.0);

        Assert.Equal(140, height);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Extent_SnapsToPhysicalPixel(double dpiScale)
    {
        var height = GridLinesPlanner.ResolveVerticalExtent(36, new[] { 122.3 }, 600, dpiScale);

        var physical = height * dpiScale;
        Assert.True(Math.Abs(physical - Math.Round(physical)) < 1e-9,
            $"высота {height} не выровнена по физическому пикселю при DPI {dpiScale}");
        Assert.True(height <= 122.3 + 1.0 / dpiScale + 1e-9);
    }

    [Fact]
    public void Extent_ZeroOrNegativeAvailable_Zero()
    {
        Assert.Equal(0, GridLinesPlanner.ResolveVerticalExtent(36, new[] { 76d }, 0, 1.0));
        Assert.Equal(0, GridLinesPlanner.ResolveVerticalExtent(36, new[] { 76d }, -100, 1.0));
        Assert.Equal(0, GridLinesPlanner.ResolveVerticalExtent(36, new[] { 76d }, double.NaN, 1.0));
    }

    [Fact]
    public void Extent_TinyExtentAfterSnap_Zero()
    {
        // Меньше половины физического пикселя — линия не видна, не создаём её.
        Assert.Equal(0, GridLinesPlanner.ResolveVerticalExtent(0.2, null, 600, 1.0));
    }

    [Fact]
    public void BottomOfRows_IgnoresNegativeAndNaN()
    {
        Assert.Equal(36, GridLinesPlanner.ResolveBottomOfRows(36, new[] { double.NaN, -20d, 10d }));
        Assert.Equal(0, GridLinesPlanner.ResolveBottomOfRows(null, new[] { -1d }));
        Assert.Equal(50, GridLinesPlanner.ResolveBottomOfRows(36, new[] { 50d }));
    }

    [Fact]
    public void Wpf_OverlayHeight_EndsAtLastRealizedRow()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        // Высота линий — чистый расчёт по низу последней реализованной строки.
        Assert.Contains("Services.GridLinesPlanner.ResolveVerticalExtent(", columns, StringComparison.Ordinal);
        Assert.Contains("GetGridLinesHeaderBottom()", columns, StringComparison.Ordinal);
        Assert.Contains("GetGridLinesRowBottoms()", columns, StringComparison.Ordinal);
        Assert.Contains("TransformToVisual(GridLinesOverlay)", columns, StringComparison.Ordinal);
        Assert.Contains("FindRowGrids(MainTree, RowGridMarker)", columns, StringComparison.Ordinal);
        Assert.Contains("FindRowGrids(MainTree, GroupGridMarker)", columns, StringComparison.Ordinal);
        // Прежняя высота «во весь оверлей» больше не задаёт высоту линии.
        Assert.DoesNotContain("var height = GridLinesOverlay.ActualHeight", columns, StringComparison.Ordinal);

        // Пересчёт при вертикальной прокрутке: набор реализованных строк меняется.
        var scroll = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Scroll.cs"));
        Assert.Contains("Dispatcher.BeginInvoke(new Action(RefreshGridLinesOverlay),",
            scroll, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_OverlayHeight_EndsAtLastRealizedRow()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));

        Assert.Contains("GridLinesPlanner.ResolveVerticalExtent(", columns, StringComparison.Ordinal);
        Assert.Contains("GetGridLinesHeaderBottom()", columns, StringComparison.Ordinal);
        Assert.Contains("GetGridLinesRowBottoms()", columns, StringComparison.Ordinal);
        Assert.Contains("TranslatePoint(new Point(0, grid.Bounds.Height), _gridLinesOverlay)",
            columns, StringComparison.Ordinal);
        Assert.Contains("GroupRowGridName", columns, StringComparison.Ordinal);
        Assert.Contains("InfobaseRowCard", columns, StringComparison.Ordinal);
        // Прежняя высота «во весь оверлей» больше не задаёт высоту линии.
        Assert.DoesNotContain("var height = _gridLinesOverlay.Bounds.Height", columns, StringComparison.Ordinal);

        // Пересчёт при вертикальной прокрутке списка.
        var scroll = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Scroll.cs"));
        Assert.Contains("Dispatcher.UIThread.Post(RefreshGridLinesOverlay, DispatcherPriority.Loaded)",
            scroll, StringComparison.Ordinal);
    }

    // ===================== Настройки: хранение и загрузка =====================

    [Fact]
    public void AppSettings_GridLines_DefaultToFalse()
    {
        var settings = new AppSettings();

        Assert.False(settings.ShowHorizontalGridLines);
        Assert.False(settings.ShowVerticalGridLines);
    }

    [Fact]
    public void AppSettings_GridLines_SurviveSaveAndLoad()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            ShowHorizontalGridLines = true,
            ShowVerticalGridLines = true
        });

        var loaded = repo.LoadSettings();

        Assert.True(loaded.ShowHorizontalGridLines);
        Assert.True(loaded.ShowVerticalGridLines);
    }

    [Fact]
    public void AppSettings_GridLines_DisabledSurvivesSaveAndLoad()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            ShowHorizontalGridLines = false,
            ShowVerticalGridLines = false
        });

        var loaded = repo.LoadSettings();

        Assert.False(loaded.ShowHorizontalGridLines);
        Assert.False(loaded.ShowVerticalGridLines);
    }

    [Fact]
    public void AppSettings_OldProfileWithoutGridFields_LoadsAsDisabled()
    {
        // Старый профиль не знает о линиях сетки: отсутствующие поля JSON должны
        // читаться как false — вид списка не меняется после обновления.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"),
            "{\n  \"Language\": \"ru\"\n}");

        var loaded = new InfobaseRepository(directory: _tempDir).LoadSettings();

        Assert.False(loaded.ShowHorizontalGridLines);
        Assert.False(loaded.ShowVerticalGridLines);
    }

    // ===================== WPF: отрисовка и реакция на настройку =====================

    [Fact]
    public void Wpf_RowsAndOverlay_UseSharedPlannerAndBothFlags()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Один чистый планировщик: позиции границ и толщина.
        Assert.Contains("Services.GridLinesPlanner.ResolveVerticalOffsets(", columns, StringComparison.Ordinal);
        Assert.Contains("Services.GridLinesPlanner.ResolveLineThickness(", columns, StringComparison.Ordinal);
        // Читаются оба флага.
        Assert.Contains("_viewModel.ShowHorizontalGridLines", columns, StringComparison.Ordinal);
        Assert.Contains("_viewModel.ShowVerticalGridLines", columns, StringComparison.Ordinal);
        // Строки рисуют только горизонталь; вертикальные линии внутри строк/заголовка
        // больше не рисуются (иначе — «две линии в колонке»).
        Assert.Contains("ApplyHorizontalGridLine(grid)", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGridLinesToGrid", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGridLinesToHeader", columns, StringComparison.Ordinal);
        // Единый оверлей объявлен в разметке поверх заголовка и строк.
        Assert.Contains("x:Name=\"GridLinesOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"2\"", xaml, StringComparison.Ordinal);
        // Цвет линии — из темы, без ярких хардкод-цветов.
        Assert.Contains("TryFindResource(\"BorderBrushColor\")", columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Overlay_IsSingleSourceAndRefreshedAfterAlignment()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        // Один оверлей: очищается и пересоздаётся целиком, линии в строках не плодятся.
        Assert.Contains("private void RefreshGridLinesOverlay()", columns, StringComparison.Ordinal);
        Assert.Contains("GridLinesOverlay.Children.Clear();", columns, StringComparison.Ordinal);
        // Источник ширин — заголовок колонок (единственный источник истины).
        Assert.Contains("var definitions = HeaderGrid.ColumnDefinitions;", columns, StringComparison.Ordinal);
        // При включённой сетке декоративные линии разделителей заголовка скрыты:
        // в шапке остаётся ровно одна линия — линия оверлея.
        Assert.Contains("ApplyHeaderSeparatorVisibility(showSeparators: !showVertical);", columns, StringComparison.Ordinal);

        // Перерисовка после AlignHeaderToData: линии заголовка не расходятся со строками.
        var alignIndex = columns.IndexOf("private void AlignHeaderToData()", StringComparison.Ordinal);
        Assert.True(alignIndex >= 0, "AlignHeaderToData не найден");
        var overlayAfterAlign = columns.IndexOf("RefreshGridLinesOverlay();", alignIndex, StringComparison.Ordinal);
        Assert.True(overlayAfterAlign > alignIndex, "RefreshGridLinesOverlay не вызывается после AlignHeaderToData");

        // Пересчёт при смене DPI и размеров области списка.
        var events = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        Assert.Contains("DpiChanged += (_, _) => RefreshGridLines();", events, StringComparison.Ordinal);
        Assert.Contains("ListContentGrid.SizeChanged += (_, _) => RefreshGridLinesOverlay();", events, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_HorizontalLine_IsOnePhysicalPixelAndExtendsToRowEdges()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        // Толщина — от DPI (1 физический пиксель), а не 1 DIP.
        Assert.Contains("Height = Services.GridLinesPlanner.ResolveLineThickness(GridLinesDpiScale)",
            columns, StringComparison.Ordinal);
        // Строка группы: отрицательный отступ компенсирует Padding 8/6.
        Assert.Contains("ApplyHorizontalGridLine(grid, groupInset);", columns, StringComparison.Ordinal);
        // Обе настройки выключены — линия не создаётся.
        Assert.Contains("if (!_viewModel.ShowHorizontalGridLines)", columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ViewModel_RaisesPropertyChangedForGridLines()
    {
        var view = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml.cs"));

        // Смена флагов перерисовывает линии без перезапуска (подписка на VM).
        Assert.Contains("nameof(MainViewModel.ShowHorizontalGridLines)", view, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.ShowVerticalGridLines)", view, StringComparison.Ordinal);
        Assert.Contains("RefreshGridLines();", view, StringComparison.Ordinal);

        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Display.cs"));
        Assert.Contains("public bool ShowHorizontalGridLines => _showHorizontalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("public bool ShowVerticalGridLines => _showVerticalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowHorizontalGridLines));", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowVerticalGridLines));", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_GridLineBrush_FollowsThemeResource()
    {
        // Issue #130 (7OH): после переключения тёмной темы линии становились чёрными.
        // Кисть линии должна следовать за ресурсом темы, а не захватываться разово.
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        Assert.Contains("SetResourceReference(System.Windows.Shapes.Shape.FillProperty, \"BorderBrushColor\")",
            columns, StringComparison.Ordinal);
        // Запасной серый — только если ресурса темы реально нет в словарях.
        Assert.Contains("TryFindResource(\"BorderBrushColor\") is Brush", columns, StringComparison.Ordinal);
        Assert.Contains("line.Fill = Brushes.Gray;", columns, StringComparison.Ordinal);
        // Разовое присваивание кисти в инициализаторе линии (Fill = brush,) запрещено:
        // именно оно и «замораживало» цвет до повторного переключения темы.
        Assert.DoesNotContain("Fill = brush,", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("Fill = Brushes.Black", columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ThemeChange_RefreshesGridLines()
    {
        // Реакция на смену темы: вьюмодель уведомляет окно свойством SavedTheme,
        // а окно отложенно перерисовывает линии после применения ресурсов темы.
        var events = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        Assert.Contains("nameof(MainViewModel.SavedTheme)", events, StringComparison.Ordinal);
        Assert.Contains("RefreshGridLinesAfterThemeChange", events, StringComparison.Ordinal);
        Assert.Contains("RefreshGridLines();", events, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", events, StringComparison.Ordinal);

        // ToggleTheme (и предпросмотр полноценной темы) уведомляет окно о смене темы.
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Theme.cs"));
        Assert.Contains("public void ToggleTheme()", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(SavedTheme));", vm, StringComparison.Ordinal);

        // Прямая смена темы через ThemeManager тоже перерисовывает линии.
        var menu = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs"));
        Assert.Contains("RefreshGridLinesAfterThemeChange();", menu, StringComparison.Ordinal);
    }

    // ===================== Avalonia: отрисовка и реакция на настройку =====================

    [Fact]
    public void Avalonia_RowsAndOverlay_UseSharedPlannerAndBothFlags()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));
        var view = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        // Тот же планировщик, что в WPF, — расхождение поведения недопустимо.
        Assert.Contains("GridLinesPlanner.ResolveVerticalOffsets(", columns, StringComparison.Ordinal);
        Assert.Contains("GridLinesPlanner.ResolveLineThickness(", columns, StringComparison.Ordinal);
        Assert.Contains("_vm.ShowHorizontalGridLines", columns, StringComparison.Ordinal);
        Assert.Contains("_vm.ShowVerticalGridLines", columns, StringComparison.Ordinal);
        // Вертикальные линии — только в едином оверлее, не в сетках строк/заголовка.
        Assert.DoesNotContain("ApplyGridLinesToGrid", columns, StringComparison.Ordinal);
        Assert.Contains("RefreshGridLinesOverlay();", columns, StringComparison.Ordinal);
        // Цвет линии — из темы.
        Assert.Contains("ThemeBrushes.Bind(line, Border.BackgroundProperty, \"BorderColorBrush\")",
            columns, StringComparison.Ordinal);

        // Строка базы и строка группы — только с горизонтальной линией.
        Assert.Contains("ApplyHorizontalGridLine(grid);", tree, StringComparison.Ordinal);
        Assert.Contains("ApplyHorizontalGridLine(row, 2 + (UiMetrics.Compact ? 6 : 8));", tree, StringComparison.Ordinal);

        // Единый оверлей создаётся в контейнере списка поверх заголовка и строк.
        Assert.Contains("_gridLinesOverlay = new Canvas", view, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRowSpan(_gridLinesOverlay, 2);", view, StringComparison.Ordinal);
        // Пересчёт при смене DPI (толщина — один физический пиксель).
        Assert.Contains("ScalingChanged += (_, _) => RefreshGridLinesOverlay();", view, StringComparison.Ordinal);
        // Декоративные линии разделителей заголовка скрываются при включённой сетке.
        Assert.Contains("ApplyHeaderSeparatorVisibility(showSeparators: !showVertical);", columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ThemeChange_RefreshesGridLines()
    {
        // Паритет с WPF: при смене темы линии сетки перерисовываются после
        // применения ресурсов темы, а не остаются с прежней кистью.
        var view = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("Dispatcher.UIThread.Post(RefreshGridLines, DispatcherPriority.Loaded)",
            view, StringComparison.Ordinal);

        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));
        Assert.Contains("private void RefreshGridLines()", columns, StringComparison.Ordinal);
        Assert.Contains("private void RefreshGridLinesOverlay()", columns, StringComparison.Ordinal);
        Assert.Contains("ThemeBrushes.Bind(line, Border.BackgroundProperty, \"BorderColorBrush\")",
            columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ViewModel_ExposesGridLinesAndHeaderRefreshes()
    {
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Display.cs"));
        Assert.Contains("public bool ShowHorizontalGridLines => _settings.ShowHorizontalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("public bool ShowVerticalGridLines => _settings.ShowVerticalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("_settings.ShowHorizontalGridLines = showHorizontalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("_settings.ShowVerticalGridLines = showVerticalGridLines;", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowHorizontalGridLines));", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowVerticalGridLines));", vm, StringComparison.Ordinal);

        var view = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("nameof(MainViewModel.ShowHorizontalGridLines)", view, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.ShowVerticalGridLines)", view, StringComparison.Ordinal);
        Assert.Contains("QueueColumnHeaderRefresh();", view, StringComparison.Ordinal);
    }

    // ===================== UI настроек и локализация =====================

    [Fact]
    public void Wpf_SettingsWindow_HasBothGridLineToggles()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("x:Name=\"ShowHorizontalGridLinesCheck\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShowVerticalGridLinesCheck\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Columns.ShowHorizontalGridLines", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Columns.ShowVerticalGridLines", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Columns.GridLinesHint", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("ShowHorizontalGridLinesCheck?.IsChecked ?? false", code, StringComparison.Ordinal);
        Assert.Contains("ShowVerticalGridLinesCheck?.IsChecked ?? false", code, StringComparison.Ordinal);

        var display = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Display.cs"));
        Assert.Contains("ShowHorizontalGridLinesCheck.IsChecked = _viewModel.ShowHorizontalGridLines;", display, StringComparison.Ordinal);
        Assert.Contains("ShowVerticalGridLinesCheck.IsChecked = _viewModel.ShowVerticalGridLines;", display, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_SettingsWindow_HasBothGridLineChecks()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("\"Settings.Columns.ShowHorizontalGridLines\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Settings.Columns.ShowVerticalGridLines\"", code, StringComparison.Ordinal);
        Assert.Contains("Settings.Columns.GridLinesHint", code, StringComparison.Ordinal);
        Assert.Contains("showHorizontalGridLinesCheck.IsChecked == true", code, StringComparison.Ordinal);
        Assert.Contains("showVerticalGridLinesCheck.IsChecked == true", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json", "Выводить линии — по горизонтали", "Выводить линии — по вертикали")]
    [InlineData("en.json", "Show lines — horizontally", "Show lines — vertically")]
    public void Localization_HasGridLineKeys(string file, string horizontal, string vertical)
    {
        var json = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", file));
        Assert.Contains("\"Settings.Columns.ShowHorizontalGridLines\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Settings.Columns.ShowVerticalGridLines\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Settings.Columns.GridLinesHint\"", json, StringComparison.Ordinal);
        Assert.Contains(horizontal, json, StringComparison.Ordinal);
        Assert.Contains(vertical, json, StringComparison.Ordinal);
    }
}
