using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #175 «Linux — разброс»: вкладки настроек Linux должны совпадать с
/// Windows по составу, порядку и оформлению групп. Тесты — стражи исходников
/// Avalonia-версии окна настроек (1.0.1.128).
/// </summary>
public sealed class Issue175LinuxSettingsParityTests
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

    private static string AvaloniaSettingsCode()
        => ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

    [Fact]
    public void AccountsTab_IsSeparateAndNotInsideFunctionality()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.1: «Учётные записи» — отдельная вкладка, как в Windows.
        Assert.Contains("MainTab(\"IconAccountMultiple\", \"Settings.TabAccounts\"", code);
        Assert.Contains("tabs.Items.Add(tabAccounts)", code);
        Assert.DoesNotContain("settingsFunctionality.Children.Add(manageProfilesButton)", code);
    }

    [Fact]
    public void IbasesTab_IsSeparateAndPlacedBeforeBases()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.3: ibases.v8i — отдельная вкладка перед «Базами».
        Assert.Contains("MainTab(\"IconFileDocument\", \"Settings.TabIbases\"", code);
        var ibases = code.IndexOf("tabs.Items.Add(tabIbases)", StringComparison.Ordinal);
        var bases = code.IndexOf("tabs.Items.Add(tabBases)", StringComparison.Ordinal);
        Assert.True(ibases > 0, "Вкладка ibases.v8i не добавляется в TabControl");
        Assert.True(bases > 0, "Вкладка «Базы» не добавляется в TabControl");
        Assert.True(ibases < bases, "«ibases.v8i» должна идти перед «Базами», как в Windows");
    }

    [Fact]
    public void AppearanceGroups_UseBorderedSettingsCards()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.2: у групп «Цветового оформления» видимые границы-карточки.
        Assert.Contains("Settings.Theme\"), themeGroupContent", code);
        Assert.Contains("Settings.GroupColorTintGroup\"), tintGroupContent", code);
        Assert.Contains("Settings.Colors\"), colorsGroupContent", code);
    }

    [Fact]
    public void MaintenanceGroups_UseBorderedSettingsCards()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.4: у групп «Обслуживания» видимые границы-карточки.
        Assert.Contains("Settings.Maintenance\"), maintenanceActionsContent", code);
        Assert.Contains("Settings.DefaultDbms\"), defaultDbmsContent", code);
        Assert.Contains("Settings.AutoStart\"), autoStartContent", code);
        Assert.Contains("Settings.DangerousOps\"), dangerousOpsContent", code);
    }

    [Fact]
    public void BasesListButtons_HaveIcons()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.5: кнопки «Списка баз» — со значками, как в Windows.
        Assert.Contains("IconTextContent(\"IconExport\", \"@TextPrimaryBrush\", \"Settings.Bases.ExportList\")", code);
        Assert.Contains("IconTextContent(\"IconImport\", \"@WarningBrush\", \"Settings.Bases.ImportList\")", code);
        Assert.Contains("IconTextContent(\"IconExport\", \"@InfoBrush\", \"ExportSelect.Title\")", code);
        Assert.Contains("IconTextContent(\"IconImport\", \"@InfoBrush\", \"ExportSelect.ImportTitle\")", code);
        Assert.Contains("IconTextContent(\"IconCloudDownload\", \"@InfoBrush\", \"Settings.Bases.ImportV8i\")", code);
        Assert.Contains("IconTextContent(\"IconFolderOpen\", \"@PurpleAccentBrush\", \"Settings.Bases.ImportStartManager\")", code);
        Assert.Contains("IconTextContent(\"IconSearch\", \"@PurpleAccentBrush\", \"Settings.Bases.DetectAllConfigs\")", code);
        Assert.Contains("IconTextContent(\"IconFolderMultiple\", \"@InfoBrush\", \"Settings.Bases.FindLostBases\")", code);
        Assert.Contains("IconTextContent(\"IconHistory\", \"@ErrorBrush\", \"Settings.Bases.ClearHistory\")", code);
    }

    [Fact]
    public void SettingsSyncButton_IsLeftAlignedAndNotStretched()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.8: кнопка под вкладками, по содержимому, не на всю ширину.
        var start = code.IndexOf("var syncButton = new Button", StringComparison.Ordinal);
        Assert.True(start > 0, "Кнопка «Синхронизация настроек» не найдена");
        var snippet = code.Substring(start, Math.Min(700, code.Length - start));
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", snippet);
        Assert.DoesNotContain("HorizontalAlignment = HorizontalAlignment.Stretch", snippet);
    }

    [Fact]
    public void LoggingTab_UsesCheckBoxesWithCheckOnLeftLikeWindows()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs");

        // Issue #175 п.7 (1.0.1.129): в Windows вкладка «Логирование» построена
        // на CheckBox (галка слева), а не на ToggleButton с дорожкой справа.
        Assert.Contains("new CheckBox", code);
        Assert.Contains("LoggingCheck(", code);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", code);
        Assert.DoesNotContain("SettingsSwitch(", code);
        Assert.DoesNotContain("toggle.Styled(ControlThemes.SettingsToggle)", code);
    }

    [Fact]
    public void CheckBoxStyle_KeepsBoxLeftOfContent()
    {
        var theme = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");

        // Issue #175 п.7: шаблон ModernMaterialCheckBox не переставляет галку
        // вправо — PART_Box идёт первым, содержимое — во второй колонке.
        var themeStart = theme.IndexOf("x:Key=\"ModernMaterialCheckBox\"", StringComparison.Ordinal);
        Assert.True(themeStart > 0, "Не найден стиль ModernMaterialCheckBox");
        var themeEnd = theme.IndexOf("</ControlTheme>", themeStart, StringComparison.Ordinal);
        Assert.True(themeEnd > themeStart, "Не найден конец стиля ModernMaterialCheckBox");
        var block = theme.Substring(themeStart, themeEnd - themeStart);

        var box = block.IndexOf("Name=\"PART_Box\"", StringComparison.Ordinal);
        var presenter = block.IndexOf("Name=\"PART_ContentPresenter\"", StringComparison.Ordinal);
        Assert.True(box > 0, "В стиле CheckBox не найден PART_Box");
        Assert.True(presenter > box, "PART_ContentPresenter должен идти после PART_Box (галка слева)");
        // У ContentPresenter Grid.Column="1" стоит в той же строке перед Name.
        var column = block.LastIndexOf("Grid.Column=\"1\"", presenter, StringComparison.Ordinal);
        Assert.True(column > box, "Содержимое CheckBox должно быть во второй колонке (Grid.Column=\"1\")");
    }

    [Fact]
    public void LoggingTab_ReservesRightInsetForScrollBar()
    {
        var code = AvaloniaSettingsCode();
        var metrics = ReadProjectFile("Workspace1C", "Controls", "UiMetrics.Avalonia.cs");

        // Issue #175 п.7 / issue #174 (1.0.1.129): правый запас содержимого
        // больше 12 DIP и учитывает ширину вертикального скроллбара, чтобы он
        // не наезжал на рамку групп. Вкладки используют единый помощник.
        Assert.Contains("UiMetrics.SettingsContentRightPadding", code);
        Assert.Contains("public const double ScrollBarWidth = 12", metrics);
        Assert.Contains("public const double SettingsContentRightInset = 12 + ScrollBarWidth", metrics);
        Assert.Contains(
            "public const double SettingsContentRightPadding = SettingsContentRightInset + SettingsSideInset",
            metrics);
        Assert.DoesNotContain("SettingsContentRightInset = 12;", metrics);
    }

    [Fact]
    public void TabHeaders_HaveWidthAndPaddingToAvoidClipping()
    {
        var code = AvaloniaSettingsCode();

        // Issue #175 п.8: заголовки вкладок не обрезаются — «Резервное копирование».
        Assert.Contains("tab.Width = UiMetrics.Scaled(260)", code);
        Assert.Contains("tab.MinWidth = UiMetrics.Scaled(240)", code);
        Assert.Contains("tab.Padding = new Thickness(12, 10)", code);
    }

    [Fact]
    public void TabOrder_MatchesWindows()
    {
        var code = AvaloniaSettingsCode();

        var expected = new[]
        {
            "tabs.Items.Add(tabGeneral)",
            "tabs.Items.Add(tabDisplay)",
            "tabs.Items.Add(tabThemes)",
            "tabs.Items.Add(tabAppearance)",
            "tabs.Items.Add(tabHotkeys)",
            "tabs.Items.Add(tabUtilitiesMenu)",
            "tabs.Items.Add(tabPlatforms)",
            "tabs.Items.Add(tabIbases)",
            "tabs.Items.Add(tabBases)",
            "tabs.Items.Add(tabLogging)",
            "tabs.Items.Add(tabProfile)",
            "tabs.Items.Add(tabAccounts)",
            "tabs.Items.Add(tabAbout)"
        };

        var previous = -1;
        foreach (var marker in expected)
        {
            var index = code.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > 0, $"Вкладка не добавляется: {marker}");
            Assert.True(index > previous, $"Нарушен порядок вкладок: {marker}");
            previous = index;
        }
    }

    [Fact]
    public void NewTabTitles_AreLocalizedInBothLanguages()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        foreach (var key in new[] { "Settings.TabAccounts", "Settings.TabIbases", "Settings.TabProfile", "Settings.TabLogging" })
        {
            Assert.Contains($"\"{key}\"", ru);
            Assert.Contains($"\"{key}\"", en);
        }
    }
}
