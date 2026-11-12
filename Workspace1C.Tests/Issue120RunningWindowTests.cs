using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #120 «Переход к открытому окну Конфигуратора/Предприятия»:
/// чистая логика решения «запуск или активация» (<see cref="RunningWindowPlanner"/>)
/// и сторож-проверки точек интеграции на обеих платформах (запуск Конфигуратора —
/// активация открытого окна; двойной щелчок при запущенном Предприятии — сначала
/// активация открытого окна, запуск нового — только если окон нет; кнопка
/// «Предприятие» продолжает запускать новый экземпляр; чипы-индикаторы — НЕ
/// нажимные, по консенсусу обсуждения issue). Платформенная EnumWindows-часть —
/// ручная проверка.
/// </summary>
public sealed class Issue120RunningWindowTests
{
    // ===================== RunningWindowPlanner =====================

    [Theory]
    [InlineData(0, RunningWindowAction.LaunchNew)]
    [InlineData(1, RunningWindowAction.ActivateSingle)]
    [InlineData(3, RunningWindowAction.ActivateSingle)]
    public void PlanConfiguratorLaunch_ActivatesExistingWindow_InsteadOfNewLaunch(
        int openWindowCount, RunningWindowAction expected)
    {
        // Issue #120: Конфигуратор — «просто активировать окно запущенного, если есть»
        // (7OH): запуск нового экземпляра — редкость. Открыт хотя бы один портфель —
        // активация; окон нет — обычный запуск.
        Assert.Equal(expected, RunningWindowPlanner.PlanConfiguratorLaunch(openWindowCount));
    }

    [Theory]
    [InlineData(0, RunningWindowAction.None)]
    [InlineData(-1, RunningWindowAction.None)]
    [InlineData(1, RunningWindowAction.ActivateSingle)]
    [InlineData(2, RunningWindowAction.ShowPicker)]
    [InlineData(5, RunningWindowAction.ShowPicker)]
    public void PlanEnterpriseIndicator_MatchesAgreedIssueBehavior(
        int openWindowCount, RunningWindowAction expected)
    {
        // Issue #120: Предприятие по кнопке запускает новый экземпляр; историческая
        // точка входа «переход по значку-индикатору»: одно окно — активировать,
        // несколько — выбор (v1.0.1.54: чипы больше не нажимные, метод сохранён
        // как чистый API планирования).
        Assert.Equal(expected, RunningWindowPlanner.PlanEnterpriseIndicator(openWindowCount));
    }

    [Theory]
    [InlineData(0, RunningWindowAction.LaunchNew)]
    [InlineData(-1, RunningWindowAction.LaunchNew)]
    [InlineData(1, RunningWindowAction.ActivateSingle)]
    [InlineData(2, RunningWindowAction.ShowPicker)]
    [InlineData(5, RunningWindowAction.ShowPicker)]
    public void PlanEnterpriseDoubleClick_ActivatesRunningSession_BeforeLaunchingNew(
        int openWindowCount, RunningWindowAction expected)
    {
        // Issue #120 (v1.0.1.54, жалоба RafaIS): двойной щелчок по строке при
        // запущенном Предприятии должен АКТИВИРОВАТЬ открытый экземпляр, а не
        // запускать новый. Одно окно — активация; несколько — выбор окна;
        // окон нет (флаг монитора устарел / перечисление не удалось) — запуск.
        Assert.Equal(expected, RunningWindowPlanner.PlanEnterpriseDoubleClick(openWindowCount));
    }

    [Theory]
    [InlineData(0, true, RunningWindowAction.LaunchNew)]
    [InlineData(1, true, RunningWindowAction.ActivateSingle)]
    [InlineData(1, false, RunningWindowAction.ActivateSingle)]
    [InlineData(2, true, RunningWindowAction.ActivateSingle)]
    [InlineData(2, false, RunningWindowAction.ShowPicker)]
    [InlineData(5, false, RunningWindowAction.ShowPicker)]
    public void PlanEnterpriseDoubleClick_PickerOnly_WhenActionsColumnHidden(
        int openWindowCount, bool showActionsColumn, RunningWindowAction expected)
    {
        // Issue #120 (v1.0.1.58, RafaIS): меню выбора окна при двойном щелчке
        // появляется ТОЛЬКО когда колонка «Действия» отключена («пользователи с
        // колонкой запускают кнопками — меню только путает»). При включённой
        // колонке и нескольких окнах активируется первое окно; одно окно
        // активируется всегда напрямую; окон нет — обычный запуск.
        Assert.Equal(expected,
            RunningWindowPlanner.PlanEnterpriseDoubleClick(openWindowCount, showActionsColumn));
    }

    [Theory]
    [InlineData(0, true, false, RunningWindowAction.None)]
    [InlineData(1, true, false, RunningWindowAction.ActivateSingle)]
    [InlineData(1, true, true, RunningWindowAction.ActivateSingle)]
    [InlineData(3, true, false, RunningWindowAction.ActivateSingle)]
    [InlineData(3, true, true, RunningWindowAction.None)]
    [InlineData(3, false, false, RunningWindowAction.ShowPicker)]
    [InlineData(3, false, true, RunningWindowAction.ShowPicker)]
    [InlineData(5, false, true, RunningWindowAction.ShowPicker)]
    public void PlanAllWindowsDoubleClick_PickerOnly_WhenActionsColumnHidden(
        int openWindowCount, bool showActionsColumn, bool nothingAction, RunningWindowAction expected)
    {
        // Issue #120 (v1.0.1.58, RafaIS/7OH): итоговое правило для двойного щелчка
        // «Ничего»/«Активировать окно» и вкладки «Запущенные»:
        //  - колонка «Действия» ОТКЛЮЧЕНА: одно окно — напрямую, несколько — меню
        //    выбора, окон нет — ничего;
        //  - колонка ВКЛЮЧЕНА: «Ничего» ничего не делает (не путает),
        //    «Активировать окно»/вкладка «Запущенные» активируют первое окно.
        Assert.Equal(expected, RunningWindowPlanner.PlanAllWindowsDoubleClick(
            openWindowCount, showActionsColumn, nothingAction));
    }

    [Fact]
    public void FormatWindowTitle_AppendsPid_WhenTitlePresent()
    {
        var window = new RunningWindowInfo(123456, 42, "1С:Предприятие — База (турбо)");
        Assert.Equal("1С:Предприятие — База (турбо) (PID 42)",
            RunningWindowPlanner.FormatWindowTitle(window));
    }

    [Fact]
    public void FormatWindowTitle_FallsBackToPid_WhenTitleEmpty()
    {
        // Windows: служебные окна отфильтрованы по заголовку, но пустая строка —
        // на всякий случай элемент выбора всё равно различим по PID.
        Assert.Equal("PID 7", RunningWindowPlanner.FormatWindowTitle(new RunningWindowInfo(1, 7, "")));
        Assert.Equal("PID 7", RunningWindowPlanner.FormatWindowTitle(new RunningWindowInfo(1, 7, "   ")));
    }

    // ===================== Сторож: точки интеграции (WPF) =====================

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

    [Fact]
    public void Wpf_RunningChips_AreNotClickable()
    {
        // Issue #120 (v1.0.1.54, консенсус обсуждения): значки-индикаторы запущенных
        // режимов НЕ нажимные («нажимные значки вносят сумятицу») — курсор «рука»
        // и обработчики клика убраны из разметки. Переход к открытому Предприятию —
        // двойным щелчком по строке; повторный запуск Конфигуратора активирует окно.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.DoesNotContain("OnEnterpriseChip_MouseLeftButtonDown", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnConfiguratorChip_MouseLeftButtonDown", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_DoubleClick_TriesEnterpriseWindowActivation_BeforeLaunch()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        // Двойной щелчок при запущенном Предприятии: сначала активация открытого
        // окна (планирование через PlanEnterpriseDoubleClick), при пустом списке —
        // fallback на обычный запуск с записью в журнал (диагностика «не работает»).
        Assert.Contains("ActivateRunningEnterpriseOrLaunch", code, StringComparison.Ordinal);
        Assert.Contains("PlanEnterpriseDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("LogRunningWindowMiss", code, StringComparison.Ordinal);
        Assert.Contains("OpenRunningWindowPicker", code, StringComparison.Ordinal);
        Assert.Contains("FormatWindowTitle", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_LaunchCore_TriesConfiguratorWindowActivation()
    {
        // Issue #120: единая точка запуска (кнопка/двойной щелчок/хоткей) —
        // перед запуском Конфигуратора попытка активации открытого окна.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.Contains("TryActivateConfiguratorWindow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_TrayAndPalette_Launches_TryConfiguratorWindowActivation()
    {
        // Issue #120: запуск из трея и из командной палитры — та же активация.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Commands.cs"));
        Assert.Contains("TryActivateConfiguratorWindow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_Miss_IsLoggedForDiagnostics_WhenFlagSetButNoWindows()
    {
        // Issue #120 (v1.0.1.54): «клик по индикатору ничего не делает» стал
        // диагностируемым — при установленном флаге запущенного режима и пустом
        // списке окон пишется запись в журнал (MainViewModel.LogRunningWindowMiss).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Running.cs"));
        Assert.Contains("LogRunningWindowMiss", code, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Activation_DoesNotBreakMaximizedState()
    {
        // Issue #120 (v1.0.1.58, 7OH/RafaIS): безусловный ShowWindow(SW_RESTORE)
        // перед SetForegroundWindow сбивал максимизацию — maximized окно при каждой
        // активации разворачивалось в normal. Активация обязана смотреть состояние
        // окна (GetWindowPlacement → showCmd): restore — только для свернутых окон
        // (свернутое из максимизированного restore вернёт снова maximized,
        // WPF_RESTORETOMAXIMIZED), maximized/normal — только передний план.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "OneCWindowActivator.Windows.cs"));
        Assert.Contains("GetWindowPlacement", code, StringComparison.Ordinal);
        Assert.Contains("SwShowMinimized", code, StringComparison.Ordinal);
        // Порядок внутри Activate: проверка состояния (GetShowCmd) выполняется
        // ДО вызова восстановления — restore больше не безусловный.
        var activateIdx = code.IndexOf("public static bool Activate(long handle)", StringComparison.Ordinal);
        var placementIdx = code.IndexOf("GetShowCmd(hWnd);", StringComparison.Ordinal);
        var restoreIdx = code.IndexOf("ShowWindow(hWnd, SwRestore);", StringComparison.Ordinal);
        var foregroundIdx = code.IndexOf("return SetForegroundWindow(hWnd);", StringComparison.Ordinal);
        Assert.True(activateIdx >= 0, "метод Activate(long handle) не найден");
        Assert.True(placementIdx > activateIdx && restoreIdx > placementIdx && foregroundIdx > restoreIdx,
            "ShowWindow(SwRestore) обязан выполняться только после проверки состояния окна (GetShowCmd)");
    }

    [Fact]
    public void Wpf_DoubleClickPicker_ShownOnly_WhenActionsColumnHidden()
    {
        // Issue #120 (v1.0.1.58, RafaIS): меню выбора окна при двойном щелчке —
        // только при отключённой колонке «Действия». WPF-точки двойного щелчка
        // планируют решение через PlanAllWindowsDoubleClick/PlanEnterpriseDoubleClick
        // с видимостью колонки (ShowActionsColumn); «Ничего» при включённой колонке
        // не делает ничего (nothingAction).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        Assert.Contains("PlanAllWindowsDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.ShowActionsColumn", code, StringComparison.Ordinal);
        Assert.Contains("nothingAction", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_DoubleClickPicker_ShownOnly_WhenActionsColumnHidden()
    {
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("PlanAllWindowsDoubleClick", tree, StringComparison.Ordinal);
        Assert.Contains("ShowActionsColumn", tree, StringComparison.Ordinal);

        // Enter-активация при запущенном Предприятии — то же правило колонки.
        var enter = ReadProjectFile(Path.Combine("Workspace1C", "Controls", "LeveledTreeView.Avalonia.cs"));
        Assert.Contains("windows.Count, vm.ShowActionsColumn", enter, StringComparison.Ordinal);
        Assert.Contains("nothingAction: true", enter, StringComparison.Ordinal);
    }

    // ===================== Сторож: точки интеграции (Avalonia) =====================

    [Fact]
    public void Avalonia_RunningChips_AreNotClickable()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.DoesNotContain("enterpriseChip.Tapped", code, StringComparison.Ordinal);
        Assert.DoesNotContain("configuratorChip.Tapped", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_DoubleClick_TriesEnterpriseWindowActivation_BeforeLaunch()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Controls", "LeveledTreeView.Avalonia.cs"));
        // Двойной щелчок/Enter при запущенном Предприятии: сначала активация
        // открытого окна (PlanEnterpriseDoubleClick), fallback — обычный запуск.
        Assert.Contains("ActivateRunningEnterpriseOrLaunch", code, StringComparison.Ordinal);
        Assert.Contains("PlanEnterpriseDoubleClick", code, StringComparison.Ordinal);
        Assert.Contains("LogRunningWindowMiss", code, StringComparison.Ordinal);
        Assert.Contains("ShowRunningWindowPicker", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Launch_TriesConfiguratorWindowActivation()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Launch.cs"));
        Assert.Contains("TryActivateConfiguratorWindow", code, StringComparison.Ordinal);
    }

    // ===================== Graceful fallback (Linux) =====================

    [Fact]
    public void Linux_Activator_ReturnsEmptyWindowsList()
    {
        // На Linux перечисление/активация внешних окон не поддержаны (нужны
        // wmctrl/xdotool): пустой список — вызывающий код выполняет graceful
        // fallback (обычный запуск). В тестовой сборке скомпилирована Windows-
        // ветвь, поэтому проверяем исходник Linux-зеркала.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "OneCWindowActivator.Linux.cs"));
        Assert.Contains("Array.Empty<RunningWindowInfo>", code, StringComparison.Ordinal);
        Assert.Contains("public static bool Activate(long handle) => false", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_Decision_IsPure_AndConsistentForSameInput()
    {
        // Детерминированность: одинаковый вход — одинаковое решение.
        Assert.Equal(RunningWindowPlanner.PlanEnterpriseIndicator(2), RunningWindowPlanner.PlanEnterpriseIndicator(2));
        Assert.Equal(RunningWindowPlanner.PlanConfiguratorLaunch(0), RunningWindowPlanner.PlanConfiguratorLaunch(0));
        Assert.Equal(RunningWindowPlanner.PlanEnterpriseDoubleClick(1), RunningWindowPlanner.PlanEnterpriseDoubleClick(1));
    }
}
