using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #128 «Действия при наведении» (v1.0.1.58): настройка
/// <c>AppSettings.ActionsHoverOnly</c> — при включении кнопки колонки «Действия»
/// в строке базы видны только когда курсор над строкой; кнопки-чипы запущенного
/// режима (Конфигуратор/Предприятие) остаются видимыми без наведения, пока
/// процесс работает; при выключенной настройке — как раньше.
/// <list type="bullet">
/// <item>правило видимости — чистый <see cref="ActionsHoverPlanner"/> (по образцу
/// RunningWindowPlanner), покрыт матрицей юнит-тестов;</item>
/// <item>настройка по умолчанию выключена и переживает сохранение/загрузку
/// профиля (InfobaseRepository);</item>
/// <item>WPF-разметка и Avalonia-код проверяются source-гвардами (Avalonia
/// собирается только в Linux-конфигурации).</item>
/// </list>
/// </summary>
public sealed class Issue128ActionsHoverTests
{
    private readonly string _tempDir;

    public Issue128ActionsHoverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_actions_hover_tests_" + Guid.NewGuid().ToString("N"));
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

    // ===================== Чистое правило видимости (обе платформы) =====================

    [Theory]
    [InlineData(false, false, false, true)]   // опция выключена — как раньше, всегда видно
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]     // hover + не запущено — видно кнопки
    [InlineData(true, true, true, true)]      // hover + запущено — видно
    [InlineData(true, false, true, true)]     // не hover + запущено — чип-индикатор виден без наведения
    [InlineData(true, false, false, false)]   // не hover + не запущено — пусто
    public void Planner_LaunchActionVisibility_Matrix(
        bool hoverOnlyEnabled, bool rowHovered, bool modeRunning, bool expectedVisible)
    {
        Assert.Equal(expectedVisible,
            ActionsHoverPlanner.IsLaunchActionVisible(hoverOnlyEnabled, rowHovered, modeRunning));
    }

    [Theory]
    [InlineData(false, false, true)]          // опция выключена — всегда видно
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]            // hover — видно
    [InlineData(true, false, false)]          // не hover — пусто
    public void Planner_UtilityActionVisibility_Matrix(
        bool hoverOnlyEnabled, bool rowHovered, bool expectedVisible)
    {
        Assert.Equal(expectedVisible,
            ActionsHoverPlanner.IsUtilityActionVisible(hoverOnlyEnabled, rowHovered));
    }

    [Fact]
    public void Planner_RunningChipStaysVisibleWithoutHover()
    {
        // Ключевое требование issue #128: пока база запущена (есть чип-индикатор),
        // кнопка видна БЕЗ наведения — «сразу видно, где запущено что-то».
        Assert.True(ActionsHoverPlanner.IsLaunchActionVisible(hoverOnlyEnabled: true, rowHovered: false, modeRunning: true));
        // И чип не «мигает»: наведение ничего не ломает у запущенной базы.
        Assert.True(ActionsHoverPlanner.IsLaunchActionVisible(hoverOnlyEnabled: true, rowHovered: true, modeRunning: true));
    }

    // ===================== Настройка: хранение и загрузка =====================

    [Fact]
    public void AppSettings_ActionsHoverOnly_DefaultsToFalse()
    {
        // По умолчанию выключено — поведение не меняется у существующих пользователей.
        var settings = new AppSettings();
        Assert.False(settings.ActionsHoverOnly);
    }

    [Fact]
    public void AppSettings_ActionsHoverOnly_SurvivesSaveAndLoad()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings { ActionsHoverOnly = true });

        var loaded = repo.LoadSettings();

        Assert.True(loaded.ActionsHoverOnly);
    }

    [Fact]
    public void AppSettings_ActionsHoverOnly_DisabledSurvivesSaveAndLoad()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings { ActionsHoverOnly = false });

        var loaded = repo.LoadSettings();

        Assert.False(loaded.ActionsHoverOnly);
    }

    // ===================== ViewModel (обе платформы) =====================

    [Fact]
    public void Wpf_ViewModel_ExposesAndPersistsActionsHoverOnly()
    {
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Display.cs"));
        // Свойство для привязок WPF-разметки (DataContext.ActionsHoverOnly).
        Assert.Contains("public bool ActionsHoverOnly => _actionsHoverOnly;", vm, StringComparison.Ordinal);
        // ApplyDisplaySettings принимает и применяет настройку (окно настроек).
        // Issue #130 добавила после actionsHoverOnly параметры линий сетки — проверяем
        // саму настройку, а не точный хвост сигнатуры.
        Assert.Contains("bool actionsHoverOnly = false, bool showHorizontalGridLines = false", vm, StringComparison.Ordinal);
        Assert.Contains("_actionsHoverOnly = actionsHoverOnly;", vm, StringComparison.Ordinal);
        // Смена настройки доходит до привязок без пересборки дерева.
        Assert.Contains("OnPropertyChanged(nameof(ActionsHoverOnly));", vm, StringComparison.Ordinal);
        // Смена видимости колонки из контекстного меню не сбрасывает настройку.
        Assert.Contains("actionsHoverOnly: _actionsHoverOnly", vm, StringComparison.Ordinal);

        var core = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("_actionsHoverOnly = settings.ActionsHoverOnly;", core, StringComparison.Ordinal);

        var launch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.Contains("s.ActionsHoverOnly = _actionsHoverOnly;", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ViewModel_ExposesAndPersistsActionsHoverOnly()
    {
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Display.cs"));
        Assert.Contains("public bool ActionsHoverOnly => _settings.ActionsHoverOnly;", vm, StringComparison.Ordinal);
        // Issue #130: сигнатура выросла параметрами линий сетки — проверяем настройку.
        Assert.Contains("bool actionsHoverOnly = false,", vm, StringComparison.Ordinal);
        Assert.Contains("bool showHorizontalGridLines = false,", vm, StringComparison.Ordinal);
        Assert.Contains("_settings.ActionsHoverOnly = actionsHoverOnly;", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ActionsHoverOnly));", vm, StringComparison.Ordinal);
        Assert.Contains("actionsHoverOnly: _settings.ActionsHoverOnly", vm, StringComparison.Ordinal);
    }

    // ===================== WPF-разметка (source-гварды) =====================

    [Fact]
    public void Wpf_RowBorder_HasNameForHoverBindings()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Корневой Border строки базы поименован — на его IsMouseOver смотрят
        // триггеры кнопок колонки «Действия».
        Assert.Contains("<Border x:Name=\"InfobaseRowBorder\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_RowActionButtons_HaveHoverOnlyTriggers()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Панель кнопок строки базы: триггер «опция включена + курсор не над строкой
        // + режим не запущен» — кнопка Предприятия (IsEnterpriseRunning).
        var rowActions = xaml.IndexOf("<StackPanel Grid.Column=\"7\" Grid.RowSpan=\"2\"", StringComparison.Ordinal);
        Assert.True(rowActions > 0, "панель кнопок строки не найдена");
        var blockEnd = xaml.IndexOf("<!-- Теги базы", rowActions, StringComparison.Ordinal);
        Assert.True(blockEnd > rowActions, "конец панели кнопок строки не найден");
        var rowBlock = xaml[rowActions..blockEnd];

        Assert.Contains("<MultiDataTrigger>", rowBlock, StringComparison.Ordinal);
        Assert.Contains("DataContext.ActionsHoverOnly", rowBlock, StringComparison.Ordinal);
        // Hover — по корневому Border строки, а не по самой кнопке.
        Assert.Contains("IsMouseOver, ElementName=InfobaseRowBorder", rowBlock, StringComparison.Ordinal);
        // Чип-индикатор запущенного Предприятия виден без наведения.
        Assert.Contains("<Condition Binding=\"{Binding IsEnterpriseRunning}\" Value=\"False\"/>", rowBlock, StringComparison.Ordinal);
        // Чип-индикатор запущенного Конфигуратора виден без наведения.
        Assert.Contains("<Condition Binding=\"{Binding IsConfiguratorRunning}\" Value=\"False\"/>", rowBlock, StringComparison.Ordinal);
        // Служебная кнопка (очистка кеша) скрывается только по hover.
        var hoverConditions = CountOccurrences(rowBlock,
            "<Condition Binding=\"{Binding IsMouseOver, ElementName=InfobaseRowBorder}\" Value=\"False\"/>");
        Assert.Equal(3, hoverConditions);
    }

    [Fact]
    public void Wpf_LaunchButtons_HideWithHiddenToKeepFixedSlots()
    {
        // Issue #128 (v1.0.1.59, 7OH): кнопки запуска при hover-only скрываются
        // через Hidden (слот резервируется, иконки не сдвигаются), а служебная
        // кнопка очистки кеша — по-прежнему Collapsed.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        var enterprise = xaml.IndexOf("<Condition Binding=\"{Binding IsEnterpriseRunning}\" Value=\"False\"/>", StringComparison.Ordinal);
        Assert.True(enterprise > 0, "триггер кнопки Предприятия не найден");
        var enterpriseBlock = xaml[enterprise..(enterprise + 900)];
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Hidden\"/>", enterpriseBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Visibility\" Value=\"Collapsed\"/>", enterpriseBlock, StringComparison.Ordinal);

        var configurator = xaml.IndexOf("<Condition Binding=\"{Binding IsConfiguratorRunning}\" Value=\"False\"/>", StringComparison.Ordinal);
        Assert.True(configurator > 0, "триггер кнопки Конфигуратора не найден");
        var configuratorBlock = xaml[configurator..(configurator + 900)];
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Hidden\"/>", configuratorBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Visibility\" Value=\"Collapsed\"/>", configuratorBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_SettingsWindow_HasActionsHoverOnlyOption()
    {
        // Опция под списком колонок во вкладке «Отображение» → «Колонки».
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var check = xaml.IndexOf("x:Name=\"ActionsHoverOnlyCheck\"", StringComparison.Ordinal);
        Assert.True(check > 0, "переключатель ActionsHoverOnlyCheck не найден");
        // Опция стоит в том же StackPanel вкладки «Колонки», что и список колонок.
        var columnsTab = xaml.IndexOf("Settings.Columns.OrderTitle", StringComparison.Ordinal);
        Assert.True(check > columnsTab, "опция должна быть ниже списка колонок");

        var codeBehind = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("ActionsHoverOnlyCheck?.IsChecked ?? false", codeBehind, StringComparison.Ordinal);

        var display = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Display.cs"));
        Assert.Contains("ActionsHoverOnlyCheck.IsChecked = _viewModel.ActionsHoverOnly;", display, StringComparison.Ordinal);
    }

    // ===================== Avalonia (source-гварды) =====================

    [Fact]
    public void Avalonia_RowButtons_BindHoverVisibility()
    {
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Привязка видимости через чистый планировщик (обёртка-конвертер).
        Assert.Contains("BindActionsHoverVisibility(launchBtn, card, ib, nameof(Infobase.IsEnterpriseRunning))", tree, StringComparison.Ordinal);
        Assert.Contains("BindActionsHoverVisibility(configuratorBtn, card, ib, nameof(Infobase.IsConfiguratorRunning))", tree, StringComparison.Ordinal);
        Assert.Contains("BindActionsHoverVisibility(clearCacheBtn, card, ib, runningPath: null)", tree, StringComparison.Ordinal);
        // Курсор над строкой — IsPointerOver карточки (hover строки целиком).
        Assert.Contains("nameof(InputElement.IsPointerOver)", tree, StringComparison.Ordinal);
        Assert.Contains("nameof(MainViewModel.ActionsHoverOnly)", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_LaunchButtons_LiveInFixedSlots()
    {
        // Issue #128 (v1.0.1.59, 7OH): кнопки запуска Предприятия и Конфигуратора
        // обёрнуты в фиксированные слоты — скрытая (Collapsed) кнопка не сдвигает
        // соседние иконки, каждая иконка стоит на своём месте.
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("actions.Children.Add(FixedLaunchSlot(launchBtn));", tree, StringComparison.Ordinal);
        Assert.Contains("actions.Children.Add(FixedLaunchSlot(configuratorBtn));", tree, StringComparison.Ordinal);
        Assert.Contains("private static Panel FixedLaunchSlot(Control button)", tree, StringComparison.Ordinal);
        // Служебная кнопка (очистка кеша) без слота — появляется последней.
        Assert.Contains("actions.Children.Add(clearCacheBtn);", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_HoverVisibilityConverter_WrapsPlanner()
    {
        var converter = ReadProjectFile(Path.Combine("Workspace1C", "Converters", "Avalonia", "RowActionHoverVisibilityConverter.cs"));
        Assert.Contains("ActionsHoverPlanner.IsLaunchActionVisible", converter, StringComparison.Ordinal);
        // values[2] — признак запущенного режима.
        Assert.Contains("values[2] is true", converter, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_SettingsWindow_HasActionsHoverOnlyOption()
    {
        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("Settings.Columns.ActionsHoverOnly", settings, StringComparison.Ordinal);
        Assert.Contains("actionsHoverOnlyCheck.IsChecked == true", settings, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("Workspace1C", "Localization", "Languages", "ru.json")]
    [InlineData("Workspace1C", "Localization", "Languages", "en.json")]
    public void Localization_HasActionsHoverOnlyKeys(params string[] parts)
    {
        var json = ReadProjectFile(Path.Combine(parts));
        Assert.Contains("\"Settings.Columns.ActionsHoverOnly\":", json, StringComparison.Ordinal);
        Assert.Contains("\"Settings.Columns.ActionsHoverOnlyHint\":", json, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
