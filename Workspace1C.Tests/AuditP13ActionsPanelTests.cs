using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI, пункт P1-3 (v1.0.1.90): иерархия действий в правой панели главного окна.
/// <list type="bullet">
/// <item>ровно один акцент на экран — акцентная заливка только у «1С:Предприятие»;</item>
/// <item>«Выход» нейтральный: без <c>DangerBrush</c>/<c>ErrorBrush</c> и без #DC2626;</item>
/// <item>при отсутствии выбранной базы кнопки запуска и «Перейти по ссылке» получают
/// <c>IsEnabled = false</c> с явным disabled-видом и подсказкой;</item>
/// <item>высоты кнопок панели одинаковы (34) в WPF и Avalonia.</item>
/// </list>
/// <para>Проверки — source-гварды по WPF-разметке и Avalonia-коду, как в
/// <see cref="Issue107StartPageButtonsTests"/>, <see cref="Issue121ActionsColumnTests"/>
/// и <see cref="Issue114StatusBarTooltipTests"/>.</para>
/// </summary>
public sealed class AuditP13ActionsPanelTests
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

    private static string Slice(string source, string startMarker, string endMarker, string description)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {description}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {description}");
        return source[start..end];
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

    private static string WpfRightPanel() =>
        Slice(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml")),
            "<!-- ============ Правая панель",
            "<!-- ===================== Строка состояния",
            "правая панель WPF");

    private static string AvaloniaRightPanel() =>
        Slice(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs")),
            "private Control BuildRightPanel()",
            "private static Control BuildLongOperationAlarmToggle(",
            "BuildRightPanel в Avalonia");

    // ===================== (а) Один акцент на экран =====================

    [Fact]
    public void Wpf_RightPanel_HasExactlyOneAccentFilledAction()
    {
        var panel = WpfRightPanel();

        // Акцентная заливка (не Foreground/Fill) — только у главного действия
        // «1С:Предприятие». Конфигуратор и остальные действия — вторичные.
        Assert.Equal(1, CountOccurrences(panel, "Background=\"{DynamicResource AccentBrush}\""));
        Assert.Contains("Command=\"{Binding LaunchEnterpriseCommand}\"", panel, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource CardBackgroundBrush}\"", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_RightPanel_HasExactlyOnePrimaryLaunch()
    {
        var panel = AvaloniaRightPanel();

        // primary: true — единственная акцентная кнопка запуска.
        Assert.Equal(1, CountOccurrences(panel, "primary: true"));
        Assert.Equal(1, CountOccurrences(panel, "primary: false"));
        Assert.Contains("\"LaunchEnterpriseCommand\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"LaunchConfiguratorCommand\"", panel, StringComparison.Ordinal);
    }

    // ===================== (б) «Выход» без красного =====================

    [Fact]
    public void Wpf_ExitButton_IsNeutral()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var exitBlock = Slice(xaml, "Click=\"OnExitApplicationClick\"", "</Button>", "кнопка «Выход» WPF");

        Assert.DoesNotContain("ErrorBrush", exitBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("DangerBrush", exitBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("#DC2626", exitBlock, StringComparison.OrdinalIgnoreCase);
        // Нейтральный цвет текста/иконки из темы.
        Assert.Contains("TextPrimaryBrush", exitBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ExitButton_IsNeutral()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        var exitBlock = Slice(source, "var exitBtn = CompactActionButton(", ");", "кнопка «Выход» Avalonia");

        Assert.DoesNotContain("#DC2626", exitBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ErrorBrush", exitBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("DangerBrush", exitBlock, StringComparison.Ordinal);
        Assert.Contains("colorHex: null", exitBlock, StringComparison.Ordinal);
    }

    // ===================== (в) Disabled без выбранной базы =====================

    [Fact]
    public void Wpf_LaunchButtons_BindIsEnabledToSelection()
    {
        var panel = WpfRightPanel();

        // «1С:Предприятие», «Конфигуратор» и «Перейти по ссылке» доступны только
        // при выбранной базе; у недоступных — opacity 0.5 и подсказка.
        Assert.True(
            CountOccurrences(panel, "IsEnabled=\"{Binding IsInfobaseSelected}\"") >= 3,
            "ожидались привязки IsEnabled к IsInfobaseSelected у кнопок запуска и перехода по ссылке");
        Assert.Contains("Main.SelectInfobaseHint", panel, StringComparison.Ordinal);
        Assert.Contains("Opacity\" Value=\"0.5\"", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_LaunchButtons_BindIsEnabledToSelection()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        // Общий помощник: IsEnabled ← IsInfobaseSelected, подсказка для disabled.
        Assert.Contains("InputElement.IsEnabledProperty", source, StringComparison.Ordinal);
        Assert.Contains("new Binding(\"IsInfobaseSelected\")", source, StringComparison.Ordinal);
        Assert.Contains("Main.SelectInfobaseHint", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetShowOnDisabled", source, StringComparison.Ordinal);
        // Вызовы: главная кнопка запуска, стрелка меню и «Перейти по ссылке».
        Assert.True(
            CountOccurrences(source, "BindActionAvailability(") >= 4,
            "ожидались вызовы BindActionAvailability для кнопок запуска и перехода по ссылке");
    }

    [Fact]
    public void ViewModel_ExposesIsInfobaseSelectedAndNotifies()
    {
        var display = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Display.cs"));
        Assert.Contains("public bool IsInfobaseSelected => SelectedInfobase is not null;", display, StringComparison.Ordinal);

        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("OnPropertyChanged(nameof(IsInfobaseSelected))", vm, StringComparison.Ordinal);
    }

    // ===================== (г) Равные высоты кнопок панели =====================

    [Fact]
    public void Wpf_ActionButtons_HaveEqualHeight()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Основные части split-кнопок «Предприятие»/«Конфигуратор».
        Assert.True(
            CountOccurrences(xaml, "Padding=\"8,0,4,0\" Height=\"34\" MinHeight=\"34\"") >= 2,
            "кнопки «1С:Предприятие» и «Конфигуратор» должны иметь одинаковую высоту 34");

        // «Перейти по ссылке» и «Выход» — та же высота через сеттер стиля.
        var byLink = Slice(xaml, "Command=\"{Binding OpenInfobaseByLinkCommand}\"", "</Button>", "кнопка перехода по ссылке");
        var exit = Slice(xaml, "Click=\"OnExitApplicationClick\"", "</Button>", "кнопка выхода");
        foreach (var block in new[] { byLink, exit })
        {
            Assert.Contains("<Setter Property=\"Height\" Value=\"34\"/>", block, StringComparison.Ordinal);
            Assert.Contains("Padding\" Value=\"14,0\"", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Avalonia_ActionButtons_UseSharedHeight()
    {
        var metrics = ReadProjectFile(Path.Combine("Workspace1C", "Controls", "UiMetrics.Avalonia.cs"));
        Assert.Contains("public static double ActionPanelButtonHeight => Scaled(34);", metrics, StringComparison.Ordinal);

        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Главная кнопка и стрелка split-кнопки, компактные действия, карточка программы.
        Assert.True(
            CountOccurrences(source, "UiMetrics.ActionPanelButtonHeight") >= 5,
            "ожидалось единое использование ActionPanelButtonHeight для кнопок правой панели");
    }
}
