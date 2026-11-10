using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #120 «Переход к открытому окну Конфигуратора/Предприятия»:
/// чистая логика решения «запуск или активация» (<see cref="RunningWindowPlanner"/>)
/// и сторож-проверки точек интеграции на обеих платформах (запуск Конфигуратора —
/// активация открытого окна; клик по значку-индикатору Предприятия — переход
/// к запущенному окну, при нескольких — выбор; кнопка «Предприятие» продолжает
/// запускать новый экземпляр). Платформенная EnumWindows-часть — ручная проверка.
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
        // Issue #120: Предприятие по кнопке запускает новый экземпляр; переход —
        // по клику на значке-индикаторе: одно окно — активировать, несколько — выбор.
        Assert.Equal(expected, RunningWindowPlanner.PlanEnterpriseIndicator(openWindowCount));
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
    public void Wpf_RunningChips_HaveClickHandlersInMarkup()
    {
        // Issue #120: клик по чипам «Предприятие»/«Конфигуратор» в строке базы
        // переходит к окну уже запущенного приложения.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("OnEnterpriseChip_MouseLeftButtonDown", xaml, StringComparison.Ordinal);
        Assert.Contains("OnConfiguratorChip_MouseLeftButtonDown", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Events_EnterpriseChipClick_UsesPickerDecision()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Events.cs"));
        // Один запущенный экземпляр — активировать; несколько — выбор окна.
        Assert.Contains("PlanEnterpriseIndicator", code, StringComparison.Ordinal);
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

    // ===================== Сторож: точки интеграции (Avalonia) =====================

    [Fact]
    public void Avalonia_RunningChips_TappedActivatesEnterpriseAndConfigurator()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("OpenRunningWindowPicker", code, StringComparison.Ordinal);
        Assert.Contains("RunningKind.Configurator", code, StringComparison.Ordinal);
        Assert.Contains("enterpriseChip.Tapped", code, StringComparison.Ordinal);
        Assert.Contains("configuratorChip.Tapped", code, StringComparison.Ordinal);
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
    }
}
