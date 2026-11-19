using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI, пункт P2-1 (v1.0.1.91): единый стиль опасных действий.
/// <list type="bullet">
/// <item>во всех окнах со списками (сценарии, действия, программы, параметры,
/// расписания, настройки) удаление оформлено <c>DangerButton</c> в WPF и
/// <c>ControlThemes.DangerButton</c> в Avalonia;</item>
/// <item>красная иконка внутри вторичной кнопки (<c>ErrorBrush</c> у
/// <c>SecondaryButton</c>) для удаления не допускается — цвет задаёт сам стиль;</item>
/// <item>значок удаления в <c>LaunchParametersWindow</c> — 16 px.</item>
/// </list>
/// <para>Проверки — source-гварды по WPF-разметке и Avalonia-коду, как в
/// <see cref="Issue26ButtonUniformityTests"/>, <see cref="AuditP13ActionsPanelTests"/>
/// и <see cref="Release100127Tests"/>.</para>
/// </summary>
public sealed class AuditP21DangerButtonTests
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

    private static string ReadView(string fileName) =>
        ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

    /// <summary>WPF-окна, в которых удаление должно быть опасной кнопкой.</summary>
    private static readonly string[] WpfWindows =
    {
        "BackupScenarioEditWindow.xaml",
        "BackupScenariosWindow.xaml",
        "CustomActionsWindow.xaml",
        "CustomProgramsWindow.xaml",
        "LaunchParametersWindow.xaml",
        "ScheduledTasksWindow.xaml",
        "ScriptScenariosWindow.xaml",
        "SettingsWindow.xaml"
    };

    /// <summary>Avalonia-реализации тех же окон (собираются кодом).</summary>
    private static readonly string[] AvaloniaWindows =
    {
        "BackupScenarioEditWindow.Avalonia.cs",
        "BackupScenariosWindow.Avalonia.cs",
        "CustomActionsWindow.Avalonia.cs",
        "CustomProgramsWindow.Avalonia.cs",
        "LaunchParametersWindow.Avalonia.cs",
        "ScheduledTasksWindow.Avalonia.cs",
        "ScriptScenariosWindow.Avalonia.cs",
        "SettingsWindow.Avalonia.cs"
    };

    /// <summary>Маркеры кнопки, ведущей к удалению (имя, команда или обработчик).</summary>
    private static readonly string[] DeleteMarkers =
    {
        "Common.Delete",
        "DeleteButton",
        "DeleteSchemeButton",
        "DeleteCommand",
        "OnDelete",
        "RemoveDir",
        "OnRemoveParam",
        "RemoveMissing",
        "RemovePlatformPath",
        "RemoveTemplate",
        "ClearAllInfobases"
    };

    private static string[] ExtractButtons(string xaml)
    {
        // Удаляем комментарии, чтобы примеры разметки в них не влияли на гвард.
        var text = Regex.Replace(xaml, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        return Regex.Matches(text, @"<Button\b[^>]*?/>|<Button\b[^>]*?>[\s\S]*?</Button>")
            .Cast<Match>()
            .Select(m => m.Value)
            .ToArray();
    }

    private static bool IsDeleteButton(string block) =>
        Array.Exists(DeleteMarkers, marker => block.Contains(marker, StringComparison.Ordinal));

    // ===================== WPF: DangerButton у удаления =====================

    [Fact]
    public void WpfWindows_DeleteActions_UseDangerButton()
    {
        foreach (var fileName in WpfWindows)
        {
            var xaml = ReadView(fileName);
            Assert.Contains("Style=\"{DynamicResource DangerButton}\"", xaml);
        }
    }

    [Fact]
    public void WpfWindows_DeleteButtons_AreNotSecondary()
    {
        foreach (var fileName in WpfWindows)
        {
            foreach (var block in ExtractButtons(ReadView(fileName)))
            {
                if (!IsDeleteButton(block))
                    continue;

                Assert.DoesNotContain("Style=\"{DynamicResource SecondaryButton}\"", block);
                Assert.DoesNotContain("Foreground=\"{DynamicResource ErrorBrush}\"", block);
            }
        }
    }

    // ===================== Avalonia: DangerButton у удаления =====================

    [Fact]
    public void AvaloniaWindows_DeleteActions_UseDangerButton()
    {
        foreach (var fileName in AvaloniaWindows)
        {
            var code = ReadView(fileName);
            Assert.Contains("ControlThemes.DangerButton", code);
        }
    }

    // ===================== LaunchParameters: значок удаления 16 px =====================

    [Fact]
    public void LaunchParameters_DeleteIcon_Is16Px()
    {
        var xaml = ReadView("LaunchParametersWindow.xaml");
        var block = Array.Find(ExtractButtons(xaml), b => b.Contains("BtnRemoveParam", StringComparison.Ordinal));
        Assert.NotNull(block);
        Assert.Contains("Style=\"{DynamicResource DangerButton}\"", block);
        Assert.Contains("Width=\"16\"", block);
        Assert.Contains("Height=\"16\"", block);
        Assert.DoesNotContain("Width=\"15\"", block);

        var avalonia = ReadView("LaunchParametersWindow.Avalonia.cs");
        Assert.Contains("IconHelper.MakeIcon(iconKey, UiMetrics.IconControl, \"DangerTextBrush\")", avalonia);
    }
}
