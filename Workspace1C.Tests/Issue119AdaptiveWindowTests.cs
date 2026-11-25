using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #119 (1.0.1.119): единая политика адаптивных размеров модальных окон.
/// Окно не больше рабочей области минус отступ, не больше разумного потолка
/// (1200×800 DIP), длинное содержимое прокручивается. Проверяется чистая
/// <see cref="WindowBoundsPolicy"/> и точки подключения WPF/Avalonia
/// (source-guard, как в тестах issue #146).
/// </summary>
public sealed class Issue119AdaptiveWindowTests
{
    [Fact]
    public void Policy_ClampsWidthAndHeightToWorkArea()
    {
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 1400,
            designHeight: 840,
            minWidth: 0,
            minHeight: 0,
            workWidth: 1366,
            workHeight: 728);

        // Рабочая область минус отступ 48 DIP.
        Assert.Equal(1318, bounds.MaxWidth, 3);
        Assert.Equal(680, bounds.MaxHeight, 3);
        Assert.True(bounds.Width <= bounds.MaxWidth);
        Assert.True(bounds.Height <= bounds.MaxHeight);
        Assert.Equal(1200, bounds.Width, 3); // потолок ширины
        Assert.Equal(680, bounds.Height, 3); // высота упирается в рабочую область
        Assert.True(bounds.RequiresScrolling); // контент 840 > 680
    }

    [Fact]
    public void Policy_NeverBelowMin()
    {
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 400,
            designHeight: 300,
            minWidth: 600,
            minHeight: 500,
            workWidth: 1366,
            workHeight: 728);

        Assert.Equal(600, bounds.Width, 3);
        Assert.Equal(500, bounds.Height, 3);
        Assert.Equal(600, bounds.MinWidth, 3);
        Assert.Equal(500, bounds.MinHeight, 3);
        Assert.False(bounds.RequiresScrolling);
    }

    [Fact]
    public void Policy_RequiresScrolling_WhenContentHigher()
    {
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 800,
            designHeight: 840,
            minWidth: 0,
            minHeight: 0,
            workWidth: 1366,
            workHeight: 728);

        Assert.Equal(680, bounds.Height, 3);
        Assert.True(bounds.RequiresScrolling);
    }

    [Fact]
    public void Policy_DefaultCeiling_1200x800()
    {
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 1600,
            designHeight: 1000,
            minWidth: 0,
            minHeight: 0,
            workWidth: 4000,
            workHeight: 2000);

        Assert.Equal(WindowBoundsPolicy.DefaultMaxWidth, bounds.Width, 3);
        Assert.Equal(WindowBoundsPolicy.DefaultMaxHeight, bounds.Height, 3);
        Assert.Equal(1200, bounds.Width, 3);
        Assert.Equal(800, bounds.Height, 3);
        Assert.True(bounds.RequiresScrolling);
    }

    [Fact]
    public void Policy_UnknownWorkArea_KeepsDefaultCeiling()
    {
        var bounds = WindowBoundsPolicy.Recommend(
            designWidth: 1600,
            designHeight: 1000,
            minWidth: 0,
            minHeight: 0,
            workWidth: 0,
            workHeight: 0);

        Assert.Equal(1200, bounds.Width, 3);
        Assert.Equal(800, bounds.Height, 3);
        Assert.True(double.IsPositiveInfinity(bounds.MaxWidth));
        Assert.True(double.IsPositiveInfinity(bounds.MaxHeight));
    }

    [Fact]
    public void WpfUi_SetupWindow_AppliesBounds()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "WpfUi.cs");

        Assert.Contains("WindowBoundsPolicy.Recommend(", source, StringComparison.Ordinal);
        Assert.Contains("WindowWorkAreaLimiter.Attach(window);", source, StringComparison.Ordinal);
        Assert.Contains("window.MaxWidth = bounds.MaxWidth;", source, StringComparison.Ordinal);
        Assert.Contains("window.MaxHeight = bounds.MaxHeight;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_GlobalLimiter_Registered()
    {
        var app = ReadProjectFile("Workspace1C", "App.xaml.cs");
        var limiter = ReadProjectFile("Workspace1C", "Views", "WindowWorkAreaLimiter.cs");

        Assert.Contains("WindowWorkAreaLimiter.RegisterGlobal();", app, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterClassHandler(", limiter, StringComparison.Ordinal);
        Assert.Contains("typeof(Window)", limiter, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.LoadedEvent", limiter, StringComparison.Ordinal);
        Assert.Contains("WindowBoundsPolicy.Recommend(", limiter, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ModalWindowBase_ClampsWorkArea()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");

        Assert.Contains("ClampWindowToWorkArea(this);", source, StringComparison.Ordinal);
        Assert.Contains("public static void ClampWindowToWorkArea(Window window)", source, StringComparison.Ordinal);
        Assert.Contains("WindowBoundsPolicy.Recommend(", source, StringComparison.Ordinal);
        Assert.Contains("ScreenFromWindow(window)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Issue #119, T2: ни одно WPF-окно из таблицы 1.4 не открывается выше 800 DIP
    /// без <c>MaxHeight</c>, а окна с <c>SizeToContent="Height"</c> ограничены только
    /// <c>MaxHeight</c> (не фиксированной высотой).
    /// </summary>
    [Fact]
    public void NoFixedHeightAbove800_WithoutMaxHeight()
    {
        var xamlWindows = new[]
        {
            "MaintenanceCenterWindow.xaml",
            "CustomActionEditWindow.xaml",
            "ScriptScenarioEditWindow.xaml",
            "ClusterMigrationWindow.xaml",
            "ServerMonitorWindow.xaml",
            "PlatformDownloadWindow.xaml",
            "ConnectionSettingsWindow.xaml",
            "NotificationChannelsWindow.xaml",
            "ConfigTypeEditWindow.xaml",
            "RepositoryBrowserWindow.xaml",
            "BatchOperationsWindow.xaml",
            "MassLoadCfWindow.xaml",
            "ConnectionReplaceWindow.xaml",
            "UsageStatisticsWindow.xaml",
            "ProcessInspectorWindow.xaml",
            "DeleteInfobaseWindow.xaml",
            "AddEditWindow.xaml",
            "SettingsWindow.xaml"
        };

        foreach (var file in xamlWindows)
        {
            var xaml = ReadProjectFile("Workspace1C", "Views", file);
            var windowTag = ExtractWindowTag(xaml);

            var heightMatch = Regex.Match(windowTag, "(?<![A-Za-z])Height=\"([^\"]+)\"");
            var maxMatch = Regex.Match(windowTag, "(?<![A-Za-z])MaxHeight=\"([^\"]+)\"");
            var sizeToContent = Regex.IsMatch(windowTag, "SizeToContent=\"Height\"");

            if (heightMatch.Success &&
                double.TryParse(heightMatch.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var height) &&
                height > 800)
            {
                Assert.True(maxMatch.Success,
                    $"{file}: фиксированная Height={height} > 800 DIP без MaxHeight.");
            }

            if (sizeToContent)
            {
                Assert.True(maxMatch.Success,
                    $"{file}: SizeToContent=\"Height\" требует MaxHeight, иначе окно «гигантское».");
            }
        }

        // Окна, собранные кодом, задают стартовый размер через WpfUi.SetupWindow.
        var codeWindows = new[]
        {
            "EventLogWindow.cs",
            "ExtensionsWindow.cs",
            "InfobaseUsersWindow.cs",
            "ScheduledJobsWindow.cs",
            "ServerAlertsWindow.cs",
            "DbmsBackupWindow.cs",
            "TechLogWindow.cs",
            "TechLogAnalysisWindow.cs",
            "WebPublishWindow.cs",
            "LicenseWindow.cs",
            "HowToBuyWindow.cs"
        };

        foreach (var file in codeWindows)
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            var match = Regex.Match(source,
                @"WpfUi\.SetupWindow\(this,\s*""[^""]+"",\s*[\d.]+,\s*(?<height>[\d.]+)\)");
            Assert.True(match.Success, $"{file}: не найдена настройка размера через WpfUi.SetupWindow.");

            var height = double.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
            Assert.True(height <= WindowBoundsPolicy.DefaultMaxHeight,
                $"{file}: стартовая высота {height} DIP выше потолка " +
                $"{WindowBoundsPolicy.DefaultMaxHeight} DIP.");
        }
    }

    /// <summary>
    /// Issue #119, T2: у окон с длинным содержимым есть вертикальная прокрутка
    /// (<c>ScrollViewer</c>), а кнопки/футер остаются вне её.
    /// </summary>
    [Fact]
    public void LongWindows_HaveScrollViewer()
    {
        var longWindows = new[]
        {
            "CustomActionEditWindow.xaml",
            "ScriptScenarioEditWindow.xaml",
            "NotificationChannelsWindow.xaml",
            "ConfigTypeEditWindow.xaml",
            "ConnectionSettingsWindow.xaml",
            "PlatformDownloadWindow.xaml",
            "SettingsWindow.xaml"
        };

        foreach (var file in longWindows)
        {
            var xaml = ReadProjectFile("Workspace1C", "Views", file);
            Assert.Contains("<ScrollViewer", xaml, StringComparison.Ordinal);
            Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Issue #119, T3: стартовые размеры Avalonia-окон совпадают с целями WPF
    /// из таблицы 1.4 плана (паритет оболочек).
    /// </summary>
    [Fact]
    public void Avalonia_WindowSizes_MatchWpfTargets()
    {
        var expected = new (string File, double Width, double Height)[]
        {
            ("MaintenanceCenterWindow.Avalonia.cs", 1080, 760),
            ("InfobaseUsersWindow.Avalonia.cs", 1100, 720),
            ("ScheduledJobsWindow.Avalonia.cs", 1100, 720),
            ("EventLogWindow.Avalonia.cs", 1100, 720),
            ("ExtensionsWindow.Avalonia.cs", 1100, 720),
            ("ServerAlertsWindow.Avalonia.cs", 960, 700),
            ("DbmsBackupWindow.Avalonia.cs", 960, 700),
            ("TechLogWindow.Avalonia.cs", 980, 680),
            ("TechLogAnalysisWindow.Avalonia.cs", 1000, 700),
            ("SettingsWindow.Avalonia.cs", 1000, 720),
            ("CustomActionEditWindow.Avalonia.cs", 680, 680),
            ("ScriptScenarioEditWindow.Avalonia.cs", 660, 680),
            ("ServerMonitorWindow.Avalonia.cs", 1200, 680),
            ("PlatformDownloadWindow.Avalonia.cs", 980, 680),
            ("ConnectionSettingsWindow.Avalonia.cs", 900, 660),
            ("NotificationChannelsWindow.Avalonia.cs", 620, 660),
            ("ConfigTypeEditWindow.Avalonia.cs", 700, 680),
            ("RepositoryBrowserWindow.Avalonia.cs", 1100, 640),
            ("BatchOperationsWindow.Avalonia.cs", 980, 660),
            ("MassLoadCfWindow.Avalonia.cs", 980, 660),
            ("ConnectionReplaceWindow.Avalonia.cs", 1000, 620),
            ("UsageStatisticsWindow.Avalonia.cs", 1000, 620),
            ("ProcessInspectorWindow.Avalonia.cs", 1000, 560),
            ("WebPublishWindow.Avalonia.cs", 920, 660),
            ("TestDeployWindow.Avalonia.cs", 720, 660),
            ("LicenseWindow.Avalonia.cs", 640, 620),
            ("HowToBuyWindow.Avalonia.cs", 620, 600)
        };

        foreach (var (file, width, height) in expected)
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            var widthMatch = Regex.Match(source, @"(?m)^[ \t]+Width\s*=\s*(?<v>[0-9]+);");
            var heightMatch = Regex.Match(source, @"(?m)^[ \t]+Height\s*=\s*(?<v>[0-9]+);");
            Assert.True(widthMatch.Success, $"{file}: не найден стартовый Width.");
            Assert.True(heightMatch.Success, $"{file}: не найден стартовый Height.");

            Assert.Equal(width, double.Parse(widthMatch.Groups["v"].Value, CultureInfo.InvariantCulture), 3);
            Assert.Equal(height, double.Parse(heightMatch.Groups["v"].Value, CultureInfo.InvariantCulture), 3);
        }

        // ClusterMigrationWindow в Avalonia задаёт размер в разметке (*.axaml).
        var cluster = ReadProjectFile("Workspace1C", "Views", "ClusterMigrationWindow.axaml");
        var clusterWidth = Regex.Match(cluster, "Width=\"(?<v>[0-9]+)\"");
        var clusterHeight = Regex.Match(cluster, "Height=\"(?<v>[0-9]+)\"");
        Assert.True(clusterWidth.Success && clusterHeight.Success,
            "ClusterMigrationWindow.axaml: не найдены Width/Height.");
        Assert.Equal(1100, double.Parse(clusterWidth.Groups["v"].Value, CultureInfo.InvariantCulture), 3);
        Assert.Equal(680, double.Parse(clusterHeight.Groups["v"].Value, CultureInfo.InvariantCulture), 3);

        // DeleteInfobaseWindow: SizeToContent="Height", только MaxHeight (как в WPF T2).
        var delete = ReadProjectFile("Workspace1C", "Views", "DeleteInfobaseWindow.axaml");
        Assert.Contains("SizeToContent=\"Height\"", delete, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"620\"", delete, StringComparison.Ordinal);
    }

    /// <summary>
    /// Issue #119, T3: локальные «магические» <c>MaxHeight</c> не противоречат единой
    /// политике — потолок 800 DIP; для <c>SizeToContent="Height"</c> MaxHeight обязателен.
    /// </summary>
    [Fact]
    public void Avalonia_NoMagicMaxHeightAbovePolicy()
    {
        var avaloniaWindows = new[]
        {
            "MaintenanceCenterWindow.Avalonia.cs",
            "InfobaseUsersWindow.Avalonia.cs",
            "ScheduledJobsWindow.Avalonia.cs",
            "EventLogWindow.Avalonia.cs",
            "ExtensionsWindow.Avalonia.cs",
            "ServerAlertsWindow.Avalonia.cs",
            "DbmsBackupWindow.Avalonia.cs",
            "TechLogWindow.Avalonia.cs",
            "TechLogAnalysisWindow.Avalonia.cs",
            "SettingsWindow.Avalonia.cs",
            "CustomActionEditWindow.Avalonia.cs",
            "ScriptScenarioEditWindow.Avalonia.cs",
            "ServerMonitorWindow.Avalonia.cs",
            "PlatformDownloadWindow.Avalonia.cs",
            "ConnectionSettingsWindow.Avalonia.cs",
            "NotificationChannelsWindow.Avalonia.cs",
            "ConfigTypeEditWindow.Avalonia.cs",
            "RepositoryBrowserWindow.Avalonia.cs",
            "BatchOperationsWindow.Avalonia.cs",
            "MassLoadCfWindow.Avalonia.cs",
            "ConnectionReplaceWindow.Avalonia.cs",
            "UsageStatisticsWindow.Avalonia.cs",
            "ProcessInspectorWindow.Avalonia.cs",
            "WebPublishWindow.Avalonia.cs",
            "TestDeployWindow.Avalonia.cs",
            "GroupEditWindow.Avalonia.cs",
            "LicenseWindow.Avalonia.cs",
            "HowToBuyWindow.Avalonia.cs"
        };

        foreach (var file in avaloniaWindows)
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            foreach (Match match in Regex.Matches(source, @"(?<!\.)\bMaxHeight\s*=\s*(?<v>[0-9]+);"))
            {
                var maxHeight = double.Parse(match.Groups["v"].Value, CultureInfo.InvariantCulture);
                Assert.True(maxHeight <= WindowBoundsPolicy.DefaultMaxHeight,
                    $"{file}: локальный MaxHeight={maxHeight} выше потолка " +
                    $"{WindowBoundsPolicy.DefaultMaxHeight} DIP — противоречит WindowBoundsPolicy.");
            }
        }
    }

    /// <summary>
    /// Issue #119, T3: у Avalonia-окон с длинным содержимым есть вертикальная прокрутка,
    /// а нижние кнопки вынесены из прокручиваемой области.
    /// </summary>
    [Fact]
    public void Avalonia_LongWindows_HaveScrollViewer()
    {
        var longWindows = new[]
        {
            "CustomActionEditWindow.Avalonia.cs",
            "ScriptScenarioEditWindow.Avalonia.cs",
            "NotificationChannelsWindow.Avalonia.cs",
            "ConfigTypeEditWindow.Avalonia.cs",
            "ConnectionSettingsWindow.Avalonia.cs",
            "PlatformDownloadWindow.Avalonia.cs",
            "SettingsWindow.Avalonia.cs"
        };

        foreach (var file in longWindows)
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            Assert.Contains("new ScrollViewer", source, StringComparison.Ordinal);
            Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", source,
                StringComparison.Ordinal);
        }

        // В формах, где футер вынесен явно, кнопки закреплены Dock.Bottom вне прокрутки.
        foreach (var file in new[]
                 {
                     "CustomActionEditWindow.Avalonia.cs",
                     "ScriptScenarioEditWindow.Avalonia.cs",
                     "ConfigTypeEditWindow.Avalonia.cs"
                 })
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            Assert.Contains("DockPanel.SetDock(", source, StringComparison.Ordinal);
            Assert.Contains("Dock.Bottom", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Issue #119, T3: все целевые Avalonia-окна наследуют <c>ModalWindowBase</c>,
    /// поэтому <c>ModalWindowBase.ClampWindowToWorkArea</c> применяется
    /// в конструкторе базы без дублирования клампа в каждом окне.
    /// </summary>
    [Fact]
    public void Avalonia_TargetWindows_DeriveFromModalWindowBase()
    {
        var windows = new[]
        {
            "MaintenanceCenterWindow.Avalonia.cs",
            "InfobaseUsersWindow.Avalonia.cs",
            "ScheduledJobsWindow.Avalonia.cs",
            "EventLogWindow.Avalonia.cs",
            "ExtensionsWindow.Avalonia.cs",
            "ServerAlertsWindow.Avalonia.cs",
            "DbmsBackupWindow.Avalonia.cs",
            "TechLogWindow.Avalonia.cs",
            "TechLogAnalysisWindow.Avalonia.cs",
            "SettingsWindow.Avalonia.cs",
            "CustomActionEditWindow.Avalonia.cs",
            "ScriptScenarioEditWindow.Avalonia.cs",
            "ClusterMigrationWindow.Avalonia.cs",
            "ServerMonitorWindow.Avalonia.cs",
            "PlatformDownloadWindow.Avalonia.cs",
            "ConnectionSettingsWindow.Avalonia.cs",
            "NotificationChannelsWindow.Avalonia.cs",
            "ConfigTypeEditWindow.Avalonia.cs",
            "RepositoryBrowserWindow.Avalonia.cs",
            "BatchOperationsWindow.Avalonia.cs",
            "MassLoadCfWindow.Avalonia.cs",
            "ConnectionReplaceWindow.Avalonia.cs",
            "UsageStatisticsWindow.Avalonia.cs",
            "ProcessInspectorWindow.Avalonia.cs",
            "WebPublishWindow.Avalonia.cs",
            "TestDeployWindow.Avalonia.cs",
            "GroupEditWindow.Avalonia.cs",
            "LicenseWindow.Avalonia.cs",
            "HowToBuyWindow.Avalonia.cs",
            "DeleteInfobaseWindow.Avalonia.cs"
        };

        foreach (var file in windows)
        {
            var source = ReadProjectFile("Workspace1C", "Views", file);
            Assert.Matches(@"class\s+\w+\s*:\s*ModalWindowBase", source);
        }

        // Базовый класс вызывает кламп один раз — в конструкторе; окна его не дублируют.
        var modalBase = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");
        Assert.Contains("ClampWindowToWorkArea(this);", modalBase, StringComparison.Ordinal);
    }

    private static string ExtractWindowTag(string xaml)
    {
        var start = xaml.IndexOf("<Window", StringComparison.Ordinal);
        Assert.True(start >= 0, "В XAML не найден корневой элемент <Window>.");
        var end = xaml.IndexOf('>', start);
        Assert.True(end > start, "Не найден конец открывающего тега <Window>.");
        return xaml[start..(end + 1)];
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
