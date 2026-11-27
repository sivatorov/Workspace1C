using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #174/#175 «Linux — отступы от края и разброс вкладок настроек»:
/// содержимое вкладок Linux-настроек должно иметь такие же боковые отступы, как
/// в Windows (12 DIP), группы — видимую рамку, кнопка «Синхронизация настроек» —
/// размер по содержимому, а порядок вкладок — совпадать с Windows.
/// Тесты — стражи исходников Avalonia-версии окна настроек.
/// </summary>
public sealed class Issue174LinuxSettingsLayoutTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Tabs_HaveTwelveDipSidePadding()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #174: галочки и поля не должны сливаться с краем окна.
        Assert.Contains("new Thickness(12, 12, 12, 0)", code);
        Assert.DoesNotContain("new Thickness(4, 12, 4, 0)", code);
    }

    [Fact]
    public void LoggingGroups_UseBorderedSettingsCards()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs");

        // Issue #175: группы вкладки «Логирование» — карточки с видимой рамкой.
        Assert.Contains("SettingsGroup(", code);
        Assert.Contains("Settings.Logging.Directions", code);
        Assert.Contains("Settings.Logging.Diagnostics", code);
    }

    [Fact]
    public void SettingsSyncButton_DoesNotStretchAcrossFooter()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        // Issue #175 п.8: кнопка синхронизации — по содержимому, у левого края.
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", code);
    }

    [Fact]
    public void TabOrder_LoggingBeforeBackupLikeWindows()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        var logging = code.IndexOf("tabs.Items.Add(tabLogging)", StringComparison.Ordinal);
        var profile = code.IndexOf("tabs.Items.Add(tabProfile)", StringComparison.Ordinal);
        Assert.True(logging > 0, "Вкладка логирования не добавляется в TabControl");
        Assert.True(profile > 0, "Вкладка резервного копирования не добавляется в TabControl");
        // Issue #175 п.6: «Логирование» идёт перед «Резервным копированием», как в Windows.
        Assert.True(logging < profile, "Порядок вкладок не совпадает с Windows");
    }
}
