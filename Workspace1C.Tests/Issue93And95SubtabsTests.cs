using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты реорганизации окон (волна 3 плана 1.0.1.8):
/// <para>issue #95 — вкладка «Настройки» разбита на 4 под-вкладки и поднята первой,
/// «Платформы» — перед «ibases.v8i»;</para>
/// <para>issue #93 — вкладка «Запуск» окна Свойств разделена на «Режим запуска
/// клиента» и «События», высота окна уменьшена.</para>
/// Проверки — по исходникам разметки/кода и словарям локализации (ru/en).
/// </summary>
public sealed class Issue93And95SubtabsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(Directory.Exists(Path.Combine(dir.FullName, "Workspace1C"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests"))))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
        return dir.FullName;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var fullPath = Path.Combine(FindRepoRoot(), Path.Combine(parts));
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {Path.Combine(parts)}");
        return File.ReadAllText(fullPath);
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, string> LoadStrings(string languageFile)
    {
        using var doc = JsonDocument.Parse(ReadProjectFile("Workspace1C", "Localization", "Languages", languageFile));
        var root = doc.RootElement;
        if (!root.TryGetProperty("strings", out var strings))
            throw new InvalidOperationException($"В {languageFile} нет секции strings.");
        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return dict;
    }

    // ===================== Локализация (ru + en) =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasSubtabAndDumpKeys(string languageFile)
    {
        var strings = LoadStrings(languageFile);

        foreach (var key in new[]
                 {
                     // issue #95: под-вкладки «Настроек».
                     "Settings.General.Subtab.Behavior",
                     "Settings.General.Subtab.BehaviorTooltip",
                     "Settings.General.Subtab.Functionality",
                     "Settings.General.Subtab.FunctionalityTooltip",
                     "Settings.General.Subtab.ComConnector",
                     "Settings.General.Subtab.ComConnectorTooltip",
                     "Settings.General.Subtab.CommonAuth",
                     "Settings.General.Subtab.CommonAuthTooltip",
                     // issue #93: под-вкладки вкладки «Запуск».
                     "Connection.LaunchSubtab.Mode",
                     "Connection.LaunchSubtab.Events",
                     // issue #92: массовая выгрузка в DT/CF.
                     "Main.BatchDumpDtCf",
                     "BatchOps.TitleDumpCf",
                     "BatchOps.HintDumpCf",
                     "BatchOps.TemplateLabel",
                     "BatchOps.TemplateHint",
                     "BatchOps.KindDtRadio",
                     "BatchOps.KindCfRadio",
                     "BatchOps.KindDumpCf"
                 })
            Assert.True(strings.ContainsKey(key), $"{languageFile}: нет ключа {key}");
    }

    // ===================== issue #95: окно настроек =====================

    [Fact]
    public void SettingsWindowXaml_GeneralTabIsFirst_AndPlatformsBeforeIbases()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");

        // Ищем именно маркеры-разделители вкладок, а не любые упоминания в тексте.
        const string marker = "============ Вкладка ";
        var general = xaml.IndexOf(marker + "«Настройки»", StringComparison.Ordinal);
        var display = xaml.IndexOf(marker + "«Отображение»", StringComparison.Ordinal);
        var themes = xaml.IndexOf(marker + "«Темы»", StringComparison.Ordinal);
        var appearance = xaml.IndexOf(marker + "«Цветовое оформление»", StringComparison.Ordinal);
        var hotkeys = xaml.IndexOf(marker + "«Клавиши»", StringComparison.Ordinal);
        var utilities = xaml.IndexOf(marker + "«Меню „Утилиты“»", StringComparison.Ordinal);
        var platforms = xaml.IndexOf(marker + "«Платформы»", StringComparison.Ordinal);
        var ibases = xaml.IndexOf(marker + "«ibases.v8i»", StringComparison.Ordinal);

        Assert.True(general >= 0, "Не найдена вкладка «Настройки».");
        // Новый порядок: Настройки → Отображение → Темы → Цветовое → Клавиши →
        // Утилиты → Платформы → ibases.v8i.
        Assert.InRange(display, general + 1, themes);
        Assert.InRange(appearance, themes + 1, hotkeys);
        Assert.InRange(utilities, hotkeys + 1, platforms);
        Assert.InRange(platforms, utilities + 1, ibases);
    }

    [Fact]
    public void SettingsWindowXaml_GeneralTab_HasFourSubtabs()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");

        foreach (var key in new[]
                 {
                     "Settings.General.Subtab.Behavior",
                     "Settings.General.Subtab.Functionality",
                     "Settings.General.Subtab.ComConnector",
                     "Settings.General.Subtab.CommonAuth"
                 })
            Assert.Contains(key, xaml, StringComparison.Ordinal);

        // Контролы, перенесённые по под-вкладкам, остались на месте (x:Name не менялись).
        foreach (var name in new[]
                 {
                     "LanguageComboBox", "AllowMultipleInstancesCheck", "AfterLaunchActionCombo",
                     "FunctionalModeComboBox", "ItsAccountsCombo", "ComConnectorNameTemplateBox",
                     "ComDetectTimeoutMsBox", "AvailabilityTcpPrecheckCheckBox",
                     "CommonEnterpriseUserBox", "CommonConfiguratorUserBox"
                 })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindowAvalonia_GeneralTab_HasFourSubtabs_AndNewOrder()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        foreach (var key in new[]
                 {
                     "Settings.General.Subtab.Behavior",
                     "Settings.General.Subtab.Functionality",
                     "Settings.General.Subtab.ComConnector",
                     "Settings.General.Subtab.CommonAuth"
                 })
            Assert.Contains($"SubTab(\"{key}\"", code, StringComparison.Ordinal);

        // Порядок вкладок: tabGeneral — первый, tabPlatforms — после меню «Утилиты».
        var order = code.IndexOf("tabs.Items.Add(tabGeneral);", StringComparison.Ordinal);
        var display = code.IndexOf("tabs.Items.Add(tabDisplay);", StringComparison.Ordinal);
        var utilities = code.IndexOf("tabs.Items.Add(tabUtilitiesMenu);", StringComparison.Ordinal);
        var platforms = code.IndexOf("tabs.Items.Add(tabPlatforms);", StringComparison.Ordinal);
        var bases = code.IndexOf("tabs.Items.Add(tabBases);", StringComparison.Ordinal);
        Assert.True(order >= 0 && order < display, "tabGeneral должен быть первой вкладкой.");
        Assert.True(utilities >= 0 && utilities < platforms && platforms < bases,
            "tabPlatforms должен стоять перед «Базами» (внутри которых ibases.v8i).");
    }

    // ===================== issue #93: окно Свойств =====================

    [Fact]
    public void ConnectionSettingsXaml_LaunchTab_HasTwoSubtabs_AndCompactHeight()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ConnectionSettingsWindow.xaml");

        Assert.Contains("Connection.LaunchSubtab.Mode", xaml, StringComparison.Ordinal);
        Assert.Contains("Connection.LaunchSubtab.Events", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ConnSubTabControl\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ConnSubTabItem\"", xaml, StringComparison.Ordinal);
        // Высота окна уменьшена (issue #93).
        Assert.Contains("MinHeight=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"680\"", xaml, StringComparison.Ordinal);
        // Оба набора контролов на месте.
        Assert.Contains("x:Name=\"DoubleClickActionCombo\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsAutoMode", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSettingsAvalonia_LaunchTab_HasTwoSubtabs_AndCompactHeight()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs");

        Assert.Contains("LaunchSubTab(\"Connection.LaunchSubtab.Mode\"", code, StringComparison.Ordinal);
        Assert.Contains("LaunchSubTab(\"Connection.LaunchSubtab.Events\"", code, StringComparison.Ordinal);
        Assert.Contains("Height = 680;", code, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 640;", code, StringComparison.Ordinal);
        // Старые сохранённые высоты зажимаются к новой стартовой.
        Assert.Contains("if (height > 680) height = 680;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSettingsWpfCodeBehind_ClampsSavedHeights()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ConnectionSettingsWindow.xaml.cs");
        Assert.Contains("if (height > 680) height = 680;", code, StringComparison.Ordinal);
    }

    // ===================== issue #92: пункт меню и окно выгрузки =====================

    [Fact]
    public void BatchContextMenu_BothPlatforms_HaveDumpDtCfItem()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.ContextMenu.cs");
        Assert.Contains("Main.BatchDumpDtCf", wpf, StringComparison.Ordinal);
        Assert.Contains("OnBatchDumpDtCf_Click", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.Contains("Main.BatchDumpDtCf", avalonia, StringComparison.Ordinal);
        Assert.Contains("OnBatchDumpDtCfClick", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchOperationsWindow_HasTemplateAndKindSwitch()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "BatchOperationsWindow.xaml");
        Assert.Contains("x:Name=\"TemplateBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KindDtRadio\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KindCfRadio\"", xaml, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "BatchOperationsWindow.Avalonia.cs");
        Assert.Contains("BatchOps.TemplateLabel", avalonia, StringComparison.Ordinal);
        Assert.Contains("BatchOps.KindDtRadio", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_BothPlatforms_OpenBatchDumpForSelected()
    {
        Assert.Contains("ExecuteBatchDumpForSelected",
            ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Tools.cs"), StringComparison.Ordinal);
        Assert.Contains("ExecuteBatchDumpForSelected",
            ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Tools.cs"), StringComparison.Ordinal);
    }
}
