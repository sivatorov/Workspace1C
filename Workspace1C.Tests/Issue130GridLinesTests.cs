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
    public void Wpf_RowAndHeader_UseSharedPlannerAndBothFlags()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));

        // Один чистый планировщик для строк и заголовка.
        Assert.Contains("Services.GridLinesPlanner.Resolve(", columns, StringComparison.Ordinal);
        // Читаются оба флага.
        Assert.Contains("_viewModel.ShowHorizontalGridLines", columns, StringComparison.Ordinal);
        Assert.Contains("_viewModel.ShowVerticalGridLines", columns, StringComparison.Ordinal);
        // Строки рисуют горизонталь, заголовок — только вертикальные линии.
        Assert.Contains("ApplyGridLinesToGrid(grid, includeHorizontal: true)", columns, StringComparison.Ordinal);
        Assert.Contains("ApplyGridLinesToGrid(HeaderGrid, includeHorizontal: false)", columns, StringComparison.Ordinal);
        // Цвет линии — из темы, без ярких хардкод-цветов.
        Assert.Contains("TryFindResource(\"BorderBrushColor\")", columns, StringComparison.Ordinal);
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

    // ===================== Avalonia: отрисовка и реакция на настройку =====================

    [Fact]
    public void Avalonia_RowAndHeader_UseSharedPlannerAndBothFlags()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));

        // Тот же планировщик, что в WPF, — расхождение поведения недопустимо.
        Assert.Contains("GridLinesPlanner.Resolve(", columns, StringComparison.Ordinal);
        Assert.Contains("_vm.ShowHorizontalGridLines", columns, StringComparison.Ordinal);
        Assert.Contains("_vm.ShowVerticalGridLines", columns, StringComparison.Ordinal);
        Assert.Contains("ApplyGridLinesToGrid(_columnHeaderRow, includeHorizontal: false)", columns, StringComparison.Ordinal);
        // Цвет линии — из темы.
        Assert.Contains("ThemeBrushes.Bind(line, Border.BackgroundProperty, \"BorderColorBrush\")",
            columns, StringComparison.Ordinal);

        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Строка базы и строка группы — с горизонтальной линией.
        Assert.Contains("ApplyGridLinesToGrid(grid, includeHorizontal: true)", tree, StringComparison.Ordinal);
        Assert.Contains("ApplyGridLinesToGrid(row, includeHorizontal: true)", tree, StringComparison.Ordinal);
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
