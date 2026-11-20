using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #134 «Завершение процессов 1С» — итоговое решение (комментарий ArtyoZ
/// от 17.11.2026): прежние изменения по контекстному меню откатаны, доработан
/// «Инспектор процессов 1С»:
/// <list type="bullet">
/// <item>мультивыделение и массовое завершение со сводкой «завершено / не удалось»;</item>
/// <item>колонка «Пользователь» с fallback на имя из параметра <c>/N</c>;</item>
/// <item>скрытие/показ колонок с сохранением в настройках;</item>
/// <item>настраиваемая горячая клавиша и кнопка на панели рядом с «Утилитами».</item>
/// </list>
/// Source-гварды проверяют обе оболочки (WPF и Avalonia) и отсутствие подменю
/// «Завершить процессы» после отката.
/// </summary>
public sealed class Issue134ProcessInspectorTests
{
    // ===================== Разбор пользователя из /N =====================

    [Theory]
    [InlineData(@"1cv8c.exe ENTERPRISE /N""superadmin""", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE /N superadmin", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE /Nsuperadmin", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE -N superadmin", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE -N""superadmin""", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE -Nsuperadmin", "superadmin")]
    [InlineData(@"1cv8c.exe ENTERPRISE /n SuPeRaDmIn", "SuPeRaDmIn")]
    [InlineData(@"1cv8c.exe ENTERPRISE /N ""Иван Иванович"" /F C:\Bases\ib", "Иван Иванович")]
    [InlineData(@"1cv8c.exe ENTERPRISE /N Иван Иванович /DisableStartupMessages", "Иван Иванович")]
    public void Parser_ExtractsUserFromAllSlashNForms(string commandLine, string expected)
    {
        Assert.True(ProcessCommandLineUserParser.TryExtractUserName(commandLine, out var user));
        Assert.Equal(expected, user);
        Assert.Equal(expected, ProcessCommandLineUserParser.ExtractUserName(commandLine));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"1cv8c.exe ENTERPRISE /F C:\Bases\ib")]
    [InlineData(@"1cv8c.exe ENTERPRISE /N")]
    [InlineData(@"1cv8c.exe ENTERPRISE /N /F C:\Bases\ib")]
    [InlineData(@"1cv8c.exe ENTERPRISE /Out/Nfoo")]
    public void Parser_ReturnsNullWhenUserIsAbsent(string? commandLine)
    {
        Assert.False(ProcessCommandLineUserParser.TryExtractUserName(commandLine, out var user));
        Assert.Null(user);
    }

    [Fact]
    public void Parser_DoesNotTreatSlashInsidePathAsKey()
    {
        // «/N» внутри пути или другого ключа не должен считаться параметром пользователя.
        Assert.Null(ProcessCommandLineUserParser.ExtractUserName(
            @"""C:\Program Files\1cv8\bin\1cv8c.exe"" ENTERPRISE /F C:\Bases\NBase"));
    }

    [Fact]
    public void ProcessRow_UserFallsBackToCommandLine()
    {
        var details = Proc(1000, "1cv8c.exe", @"ENTERPRISE /N""superadmin"" /F C:\Bases\ib");

        var row = new ProcessRowViewModel(details, null);

        Assert.Equal("superadmin", row.UserName);
    }

    [Fact]
    public void ProcessRow_OwnerWinsOverCommandLine()
    {
        var details = new RunningOneCProcessDetails(
            1000, "1cv8c.exe", @"ENTERPRISE /N""cmd-user""", null, null, "owner");

        var row = new ProcessRowViewModel(details, null);

        Assert.Equal("owner", row.UserName);
    }

    // ===================== Мультивыделение и массовое завершение =====================

    [Fact]
    public void KillSelectedMany_KillsAllSelectedRows()
    {
        var service = new FakeRunningService(Proc(1, "Первый"), Proc(2, "Второй"), Proc(3, "Третий"));
        using var vm = CreateViewModel(service, out var killer, out var dialogs);

        WaitFor(() => vm.Processes.Count == 3);
        vm.SetSelectedRows(vm.Processes.Where(row => row.Pid != 3).ToList());

        vm.KillSelectedMany();

        Assert.Equal(new[] { 1, 2 }, killer.Calls.Select(call => call.Pid).OrderBy(pid => pid));
        Assert.Single(dialogs.Confirms);
        Assert.Single(dialogs.Infos);
        Assert.Empty(dialogs.Warnings);
    }

    [Fact]
    public void KillSelectedMany_ReportsFailedPids()
    {
        var service = new FakeRunningService(Proc(1, "Первый"), Proc(2, "Второй"));
        using var vm = CreateViewModel(service, out var killer, out var dialogs);
        killer.ResultByPid = pid => pid != 2;

        WaitFor(() => vm.Processes.Count == 2);
        vm.SetSelectedRows(vm.Processes.ToList());

        vm.KillSelectedMany();

        // Оба процесса попытались завершить; при ошибке одного — предупреждение,
        // а не «тихий» успех (сводка «завершено / не удалось»).
        Assert.Equal(new[] { 1, 2 }, killer.Calls.Select(call => call.Pid).OrderBy(pid => pid));
        Assert.Single(dialogs.Confirms);
        Assert.Single(dialogs.Warnings);
        Assert.Empty(dialogs.Infos);
    }

    [Fact]
    public void Refresh_RestoresAllSelectedRows()
    {
        var service = new FakeRunningService(Proc(1, "Первый"), Proc(2, "Второй"), Proc(3, "Третий"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 3);
        var original = vm.Processes.Where(row => row.Pid is 1 or 3).ToList();
        vm.SetSelectedRows(original);

        service.Set(Proc(1, "Первый"), Proc(2, "Второй"), Proc(3, "Третий"));
        vm.Refresh();

        WaitFor(() => vm.SelectedRows.Count == 2 && vm.SelectedRows.All(row => !original.Contains(row)));
        Assert.Equal(new[] { 1, 3 }, vm.SelectedRows.Select(row => row.Pid).OrderBy(pid => pid));
        Assert.Equal(1, vm.SelectedRow!.Pid);
    }

    [Fact]
    public void KillSelected_NoSelection_ShowsHint()
    {
        var service = new FakeRunningService(Proc(1, "Первый"));
        using var vm = CreateViewModel(service, out var killer, out var dialogs);

        WaitFor(() => vm.Processes.Count == 1);
        vm.SelectedRow = null;

        vm.KillSelected();

        Assert.Empty(killer.Calls);
        Assert.Single(dialogs.Warnings);
    }

    // ===================== Скрытие/показ колонок =====================

    [Fact]
    public void Columns_AllVisibleByDefault()
    {
        var settings = new AppSettings();
        var service = new FakeRunningService(Proc(1, "Первый"));
        using var vm = CreateViewModel(service, out _, out _, settings);

        Assert.Equal(7, ProcessInspectorViewModel.AllColumns.Count);
        Assert.All(
            ProcessInspectorViewModel.AllColumns,
            info => Assert.True(vm.IsColumnVisible(info.Column)));
        Assert.Empty(settings.ProcessInspectorHiddenColumns);
    }

    [Fact]
    public void Columns_HiddenRestoredFromSettings()
    {
        var settings = new AppSettings
        {
            ProcessInspectorHiddenColumns = new List<string>
            {
                nameof(ProcessInspectorColumn.User),
                nameof(ProcessInspectorColumn.Known)
            }
        };
        var service = new FakeRunningService(Proc(1, "Первый"));
        using var vm = CreateViewModel(service, out _, out _, settings);

        Assert.False(vm.IsColumnVisible(ProcessInspectorColumn.User));
        Assert.False(vm.IsColumnVisible(ProcessInspectorColumn.Known));
        Assert.True(vm.IsColumnVisible(ProcessInspectorColumn.Base));
    }

    [Fact]
    public void Columns_SetColumnVisible_PersistsSelection()
    {
        var settings = new AppSettings();
        var saved = 0;
        var service = new FakeRunningService(Proc(1, "Первый"));
        using var vm = CreateViewModel(service, out _, out _, settings, () => saved++);

        vm.SetColumnVisible(ProcessInspectorColumn.User, false);

        Assert.Contains(nameof(ProcessInspectorColumn.User), settings.ProcessInspectorHiddenColumns);
        Assert.Equal(1, saved);

        vm.SetColumnVisible(ProcessInspectorColumn.User, true);

        Assert.DoesNotContain(nameof(ProcessInspectorColumn.User), settings.ProcessInspectorHiddenColumns);
        Assert.Equal(2, saved);
    }

    [Fact]
    public void Columns_ToggleColumn_FlipsVisibility()
    {
        var service = new FakeRunningService(Proc(1, "Первый"));
        using var vm = CreateViewModel(service, out _, out _);

        vm.ToggleColumn(ProcessInspectorColumn.Connection);

        Assert.False(vm.IsColumnVisible(ProcessInspectorColumn.Connection));
    }

    // ===================== Горячая клавиша =====================

    [Fact]
    public void AppSettings_ProcessInspectorHotkey_HasDefault()
    {
        Assert.Equal("Ctrl+Alt+I", new AppSettings().HotkeyProcessInspector);
    }

    [Fact]
    public void Wpf_RegistersProcessInspectorHotkey()
    {
        var admin = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Admin.cs");
        var load = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.cs");
        var save = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Launch.cs");
        var hotkeys = ReadProjectFile("Workspace1C", "Views", "MainWindow.Hotkeys.cs");

        Assert.Contains("HotkeyProcessInspector", admin, StringComparison.Ordinal);
        Assert.Contains("settings.HotkeyProcessInspector", load, StringComparison.Ordinal);
        Assert.Contains("s.HotkeyProcessInspector", save, StringComparison.Ordinal);
        Assert.Contains(
            "Add(_viewModel.HotkeyProcessInspector, _viewModel.ProcessInspectorCommand);",
            hotkeys,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_RegistersProcessInspectorHotkey()
    {
        var admin = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Admin.cs");
        var display = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Display.cs");
        var hotkeys = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Hotkeys.cs");

        Assert.Contains("HotkeyProcessInspector", admin, StringComparison.Ordinal);
        Assert.Contains("_settings.HotkeyProcessInspector", display, StringComparison.Ordinal);
        Assert.Contains(
            "AddHotkey(_vm.HotkeyProcessInspector, _vm.ProcessInspectorCommand);",
            hotkeys,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_SettingsWindow_HasProcessInspectorHotkeyRow()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml");
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml.cs");

        Assert.Contains("HotkeyProcessInspectorBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Hotkeys.ProcessInspector", xaml, StringComparison.Ordinal);
        Assert.Contains("HotkeyProcessInspector", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_SettingsWindow_HasProcessInspectorHotkeyRow()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");

        Assert.Contains("Settings.Hotkeys.ProcessInspector", code, StringComparison.Ordinal);
        Assert.Contains("hotkeyProcessInspector", code, StringComparison.Ordinal);
        Assert.Contains("processInspector:", code, StringComparison.Ordinal);
    }

    // ===================== Кнопка на панели рядом с «Утилитами» =====================

    [Fact]
    public void Wpf_HasProcessInspectorToolbarButton()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");

        Assert.Contains("ProcessInspectorButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ProcessInspectorCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Main.ProcessInspectorTooltip", xaml, StringComparison.Ordinal);
        Assert.Contains("IconApplicationCog", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_HasProcessInspectorToolbarButton()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");

        Assert.Contains("ProcessInspectorCommand", code, StringComparison.Ordinal);
        Assert.Contains("Main.ProcessInspectorTooltip", code, StringComparison.Ordinal);
        Assert.Contains("IconApplicationCog", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.84): иконка инспектора стоит на панели сразу после
    /// «Проверить доступность всех баз» и до вертикального разделителя.
    /// </summary>
    [Fact]
    public void Wpf_ProcessInspectorButton_IsAfterCheckAvailability_BeforeSeparator()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");

        var checkAvailability = xaml.IndexOf("CheckAvailabilityCommand", StringComparison.Ordinal);
        var inspector = xaml.IndexOf("x:Name=\"ProcessInspectorButton\"", StringComparison.Ordinal);
        var separator = xaml.IndexOf(
            "Вертикальный разделитель: отделяет группу «Управление списком»",
            StringComparison.Ordinal);

        Assert.True(checkAvailability >= 0, "Не найдена кнопка «Проверить доступность».");
        Assert.True(inspector >= 0, "Не найдена кнопка инспектора процессов.");
        Assert.True(separator >= 0, "Не найден разделитель после группы «Управление списком».");
        Assert.True(checkAvailability < inspector,
            "Инспектор должен идти после «Проверить доступность всех баз».");
        Assert.True(inspector < separator, "Инспектор должен стоять до разделителя.");
    }

    /// <summary>
    /// issue #134 (1.0.1.84): в Avalonia кнопка инспектора добавляется после
    /// <c>checkAvailBtn</c> и до следующего <c>CommandPanelSeparator()</c>.
    /// </summary>
    [Fact]
    public void Avalonia_ProcessInspectorButton_IsAfterCheckAvailability_BeforeSeparator()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");

        var checkAvailability = code.IndexOf(
            "panel.Children.Add(checkAvailBtn);", StringComparison.Ordinal);
        var inspector = code.IndexOf(
            "panel.Children.Add(processInspectorBtn);", StringComparison.Ordinal);
        Assert.True(checkAvailability >= 0, "Не найдено добавление кнопки «Проверить доступность».");
        Assert.True(inspector >= 0, "Не найдено добавление кнопки инспектора процессов.");

        var separator = code.IndexOf(
            "panel.Children.Add(CommandPanelSeparator());", checkAvailability, StringComparison.Ordinal);
        Assert.True(separator > checkAvailability, "После «Проверить доступность» нет разделителя.");

        Assert.True(checkAvailability < inspector,
            "Инспектор должен идти после «Проверить доступность всех баз».");
        Assert.True(inspector < separator, "Инспектор должен стоять до разделителя.");
    }

    // ===================== Инспектор убран из меню «Утилиты» =====================

    /// <summary>
    /// issue #134 (1.0.1.84): пункта «Инспектор процессов 1С» больше нет ни в каталоге,
    /// ни в стандартной компоновке «Утилит», ни в обработчиках меню обеих оболочек.
    /// </summary>
    [Fact]
    public void ProcessInspector_IsRemovedFromUtilitiesCatalogAndLayout()
    {
        Assert.Null(UtilitiesMenuCatalog.Find("ProcessInspector"));
        Assert.DoesNotContain(
            UtilitiesMenuCatalog.All,
            item => string.Equals(item.Id, "ProcessInspector", StringComparison.Ordinal));

        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        Assert.DoesNotContain("ProcessInspector", ids);
        // Соседний пункт «Завершить процессы 1С» остаётся на месте.
        Assert.Contains("KillOneCProcesses", ids);

        // Значение перечисления удалено вместе с пунктом.
        Assert.False(Enum.TryParse<UtilitiesMenuCommandKind>("ProcessInspector", out _));

        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.DoesNotContain("UtilitiesMenuCommandKind.ProcessInspector", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("UtilitiesMenuCommandKind.ProcessInspector", avalonia, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.84): старая пользовательская компоновка могла содержать
    /// удалённый пункт <c>ProcessInspector</c>. <c>NormalizeNode</c> обязан молча
    /// отбросить неизвестный <c>ItemId</c>, не роняя загрузку меню.
    /// </summary>
    [Fact]
    public void OldCustomLayout_WithRemovedProcessInspectorItem_IsDroppedSafely()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "ws1c-issue134-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string json = """
                {
                  "SchemaVersion": 1,
                  "Root": [
                    { "Kind": "item", "ItemId": "CommandPalette", "Visible": true },
                    { "Kind": "item", "ItemId": "ProcessInspector", "Visible": true },
                    { "Kind": "item", "ItemId": "KillOneCProcesses", "Visible": true }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(dir, UtilitiesMenuLayoutStore.FileName), json);

            var store = new UtilitiesMenuLayoutStore(directoryOverride: dir);
            var layout = store.Load();

            Assert.NotNull(layout);
            var ids = layout!.Root.Select(node => node.ItemId).ToList();
            Assert.DoesNotContain("ProcessInspector", ids);
            Assert.Contains("CommandPalette", ids);
            Assert.Contains("KillOneCProcesses", ids);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ===================== Меню «Колонки»: настоящие чекбоксы =====================

    /// <summary>
    /// issue #134 (1.0.1.96): WPF-индикатор — часть <c>MenuItem.Header</c>
    /// (рамка 16×16 + векторная галочка), видимость задаётся ЯВНО из
    /// <c>IsColumnVisible</c>. Шаблон, <c>IsChecked</c> и привязки не участвуют.
    /// </summary>
    [Fact]
    public void Wpf_ColumnsMenu_HeaderIndicatorIsExplicit()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");

        Assert.Contains("_vm.GetColumnMenuItems()", code, StringComparison.Ordinal);
        Assert.Contains("IsCheckable = false", code, StringComparison.Ordinal);
        Assert.Contains("StaysOpenOnClick = true", code, StringComparison.Ordinal);
        Assert.Contains("_vm.ToggleColumn(column)", code, StringComparison.Ordinal);
        Assert.Contains("check.Visibility = visible ? Visibility.Visible : Visibility.Hidden",
            code, StringComparison.Ordinal);

        var header = ExtractMethod(code, "BuildColumnHeader");
        Assert.Contains("Geometry.Parse(CheckGeometryData)", header, StringComparison.Ordinal);
        Assert.Contains("Border", header, StringComparison.Ordinal);
        Assert.Contains("Width = 16", header, StringComparison.Ordinal);
        Assert.Contains("Height = 16", header, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(0, 0, 8, 0)", header, StringComparison.Ordinal);
        Assert.Contains("Visibility.Visible", header, StringComparison.Ordinal);
        Assert.Contains("Visibility.Hidden", header, StringComparison.Ordinal);

        // Никакой зависимости от шаблона меню, IsChecked, набора иконок и конвертеров.
        Assert.DoesNotContain("BooleanToVisibilityConverter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(MenuItem.IsChecked)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildColumnCheckIndicator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveGeometry(\"IconCheck\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.96): Avalonia-индикатор — часть <c>MenuItem.Header</c>,
    /// видимость задаётся ЯВНО из <c>IsColumnVisible</c>. Шаблон Fluent, ToggleType,
    /// IsChecked и FindAncestor-привязки не участвуют.
    /// </summary>
    [Fact]
    public void Avalonia_ColumnsMenu_HeaderIndicatorIsExplicit()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("_vm.GetColumnMenuItems()", code, StringComparison.Ordinal);
        Assert.Contains("ToggleType = MenuItemToggleType.None", code, StringComparison.Ordinal);
        Assert.Contains("StaysOpenOnClick = true", code, StringComparison.Ordinal);
        Assert.Contains("_vm.ToggleColumn(column)", code, StringComparison.Ordinal);
        Assert.Contains("check.IsVisible = visible", code, StringComparison.Ordinal);

        var header = ExtractMethod(code, "BuildColumnHeader");
        Assert.Contains("Geometry.Parse(CheckGeometryData)", header, StringComparison.Ordinal);
        Assert.Contains("Border", header, StringComparison.Ordinal);
        Assert.Contains("Width = 16", header, StringComparison.Ordinal);
        Assert.Contains("Height = 16", header, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(0, 0, 8, 0)", header, StringComparison.Ordinal);
        Assert.Contains("IsVisible = info.IsVisible", header, StringComparison.Ordinal);

        // Никакой зависимости от шаблона Fluent, IsChecked, набора иконок и конвертеров.
        Assert.DoesNotContain("FuncValueConverter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuItemToggleType.CheckBox", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildColumnCheckIndicator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Geometry(\"IconCheck\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.95): тема больше не опирается на внутреннюю часть шаблона
    /// Fluent <c>PART_ToggleIconPresenter</c> — в используемой версии Avalonia она
    /// не отображалась. Индикатор задаётся в <c>MenuItem.Icon</c> самим окном
    /// инспектора, поэтому подменю и прочие пункты меню не затрагиваются.
    /// </summary>
    [Fact]
    public void Avalonia_ModernMenuItem_DoesNotRelyOnFluentTogglePresenter()
    {
        var axaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var start = axaml.IndexOf("x:Key=\"ModernMenuItem\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "В Controls.axaml нет ControlTheme ModernMenuItem.");
        var end = axaml.IndexOf("</ControlTheme>", start, StringComparison.Ordinal);
        Assert.True(end > start, "ControlTheme ModernMenuItem не закрыт.");

        var theme = axaml[start..end];
        // Имя части шаблона может упоминаться в пояснении; зависимости нет, если
        // тема не объявляет селектор по ней и не переопределяет её видимость.
        Assert.DoesNotContain(
            "ContentControl#PART_ToggleIconPresenter", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("Selector=\"^:toggle", theme, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.95): WPF-шаблон <c>ModernMenuItem</c> выводит индикатор из
    /// <c>MenuItem.Icon</c> (<c>ContentSource="Icon"</c>) и сохраняет обязательную
    /// часть <c>PART_Popup</c>, чтобы подменю остальных меню не сломались.
    /// </summary>
    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void Wpf_ModernMenuItem_RendersIconAndKeepsPopup(string themeFile)
    {
        var xaml = ReadProjectFile("Workspace1C", "Themes", themeFile);
        var start = xaml.IndexOf("x:Key=\"ModernMenuItem\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"В {themeFile} нет стиля ModernMenuItem.");
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Стиль ModernMenuItem в {themeFile} не закрыт.");

        var style = xaml[start..end];
        Assert.Contains("ContentSource=\"Icon\"", style, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", style, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_ToggleIconPresenter", style, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.96): единая модель пунктов меню отдаёт фактическую
    /// видимость колонки — окна не хранят состояние индикатора отдельно.
    /// </summary>
    [Fact]
    public void ColumnsMenu_ViewModel_ExposesActualVisibility()
    {
        using var vm = CreateViewModel(new FakeRunningService(), out _, out _);

        var items = vm.GetColumnMenuItems();
        Assert.Equal(ProcessInspectorViewModel.AllColumns.Count, items.Count);
        Assert.All(items, item => Assert.True(item.IsVisible));
        Assert.Equal(
            ProcessInspectorViewModel.AllColumns.Select(c => c.Column),
            items.Select(i => i.Column));

        vm.SetColumnVisible(ProcessInspectorColumn.Pid, false);
        var pid = vm.GetColumnMenuItems().Single(i => i.Column == ProcessInspectorColumn.Pid);
        Assert.False(pid.IsVisible);

        vm.ToggleColumn(ProcessInspectorColumn.Pid);
        Assert.True(vm.IsColumnVisible(ProcessInspectorColumn.Pid));
        Assert.True(vm.GetColumnMenuItems().Single(i => i.Column == ProcessInspectorColumn.Pid).IsVisible);
    }

    /// <summary>
    /// issue #134 (1.0.1.96): оба окна берут геометрию галочки из константы в коде —
    /// набор иконок и ресурсы приложения на индикатор не влияют.
    /// </summary>
    [Theory]
    [InlineData("ProcessInspectorWindow.xaml.cs")]
    [InlineData("ProcessInspectorWindow.Avalonia.cs")]
    public void ColumnsMenu_IndicatorUsesConstantGeometry(string file)
    {
        var code = ReadProjectFile("Workspace1C", "Views", file);
        var header = ExtractMethod(code, "BuildColumnHeader");

        Assert.Contains("private const string CheckGeometryData", code, StringComparison.Ordinal);
        Assert.Contains("Geometry.Parse(CheckGeometryData)", header, StringComparison.Ordinal);
        Assert.Contains("Border", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.96): подписи пунктов не сдвигаются — у скрытой колонки
    /// остаётся пустая рамка того же размера 16×16.
    /// </summary>
    [Theory]
    [InlineData("ProcessInspectorWindow.xaml.cs")]
    [InlineData("ProcessInspectorWindow.Avalonia.cs")]
    public void ColumnsMenu_IndicatorKeepsFixedSlot(string file)
    {
        var code = ReadProjectFile("Workspace1C", "Views", file);
        var header = ExtractMethod(code, "BuildColumnHeader");

        Assert.Contains("Width = 16", header, StringComparison.Ordinal);
        Assert.Contains("Height = 16", header, StringComparison.Ordinal);
        Assert.Contains("Width = 12", header, StringComparison.Ordinal);
        Assert.Contains("Height = 12", header, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasColumnVisibilityKeys(string file)
    {
        var strings = LoadStrings(file);

        Assert.True(strings.ContainsKey("ProcessInspector.ColumnVisible"));
        Assert.True(strings.ContainsKey("ProcessInspector.ColumnHidden"));
        Assert.False(string.IsNullOrWhiteSpace(strings["ProcessInspector.ColumnVisible"]));
        Assert.False(string.IsNullOrWhiteSpace(strings["ProcessInspector.ColumnHidden"]));
    }

    // ===================== Мультивыбор, выделение и зебра =====================

    /// <summary>
    /// issue #134 (1.0.1.84) / #140: триггер IsSelected идёт после зебры и перекрывает её,
    /// а локальный AlternatingRowBackground обнулён.
    /// </summary>
    [Fact]
    public void Wpf_Inspector_SelectionTriggerOverridesZebra()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml");
        var start = xaml.IndexOf("<DataGrid.RowStyle>", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataGrid.RowStyle>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "В ProcessInspectorWindow.xaml нет DataGrid.RowStyle.");

        var rowStyle = xaml[start..end];
        var zebra = rowStyle.IndexOf("ItemsControl.AlternationIndex", StringComparison.Ordinal);
        var selected = rowStyle.IndexOf("Property=\"IsSelected\"", StringComparison.Ordinal);
        Assert.True(zebra >= 0, "В RowStyle нет триггера зебры.");
        Assert.True(selected > zebra, "Триггер IsSelected должен идти после AlternationIndex.");

        Assert.Contains(
            "<Setter Property=\"AlternatingRowBackground\" Value=\"{x:Null}\"/>",
            xaml,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.84) / #140: в Avalonia стиль <c>^:selected</c> объявлен после
    /// <c>^.zebra</c>, поэтому выделение видно и на чётных строках.
    /// </summary>
    [Fact]
    public void Avalonia_Inspector_SelectedStyleOverridesZebra()
    {
        var axaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var start = axaml.IndexOf("x:Key=\"ModernListBoxItem\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "В Controls.axaml нет ControlTheme ModernListBoxItem.");
        var end = axaml.IndexOf("</ControlTheme>", start, StringComparison.Ordinal);
        Assert.True(end > start, "ControlTheme ModernListBoxItem не закрыт.");

        var theme = axaml[start..end];
        var zebra = theme.IndexOf("Selector=\"^.zebra\"", StringComparison.Ordinal);
        var selected = theme.IndexOf("Selector=\"^:selected\"", StringComparison.Ordinal);
        Assert.True(zebra >= 0, "В ModernListBoxItem нет стиля ^.zebra.");
        Assert.True(selected > zebra, "Стиль ^:selected должен идти после ^.zebra.");
        Assert.Contains("ItemSelectedBrush", theme, StringComparison.Ordinal);
    }

    /// <summary>
    /// issue #134 (1.0.1.84): после автообновления восстанавливаются ВСЕ выбранные
    /// строки, включая чётные, — а не только первая.
    /// </summary>
    [Fact]
    public void RestoreSelectedRows_RestoresAllSelectedRows()
    {
        var service = new FakeRunningService(
            Proc(1, "Первый"), Proc(2, "Второй"), Proc(3, "Третий"), Proc(4, "Четвёртый"));
        using var vm = CreateViewModel(service, out _, out _);

        WaitFor(() => vm.Processes.Count == 4);
        var original = vm.Processes.Where(row => row.Pid is 2 or 4).ToList();
        vm.SetSelectedRows(original);

        service.Set(
            Proc(1, "Первый"), Proc(2, "Второй"), Proc(3, "Третий"), Proc(4, "Четвёртый"));
        vm.Refresh();

        WaitFor(() => vm.SelectedRows.Count == 2 && vm.SelectedRows.All(row => !original.Contains(row)));
        Assert.Equal(new[] { 2, 4 }, vm.SelectedRows.Select(row => row.Pid).OrderBy(pid => pid));
    }

    // ===================== Инспектор: мультивыбор и колонки в оболочках =====================

    [Fact]
    public void Wpf_Inspector_UsesExtendedSelectionAndColumnsMenu()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml");
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml.cs");

        Assert.Contains("SelectionMode=\"Extended\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnGrid_SelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("ProcessInspector.Columns", xaml, StringComparison.Ordinal);
        Assert.Contains("OnColumns_Click", code, StringComparison.Ordinal);
        Assert.Contains("KillSelectedMany", code, StringComparison.Ordinal);
        Assert.Contains("ApplyColumnVisibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Inspector_UsesMultipleSelectionAndColumnsMenu()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("SelectionMode.Multiple", code, StringComparison.Ordinal);
        Assert.Contains("ShowColumnsMenu", code, StringComparison.Ordinal);
        Assert.Contains("ProcessInspector.Columns", code, StringComparison.Ordinal);
        Assert.Contains("KillSelectedMany", code, StringComparison.Ordinal);
        Assert.Contains("RebuildTable", code, StringComparison.Ordinal);
    }

    // ===================== Откат: подменю «Завершить процессы» =====================

    [Fact]
    public void Wpf_ContextMenu_HasNoKillProcessesSubmenu()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.ContextMenu.cs");
        var columns = ReadProjectFile("Workspace1C", "Views", "MainWindow.Columns.cs");

        Assert.DoesNotContain("KillProcesses", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateKillProcessesMenu", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateKillProcessesMenu", columns, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ContextMenu_HasNoKillProcessesSubmenu()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");

        Assert.DoesNotContain("KillProcesses", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateKillProcessesMenu", code, StringComparison.Ordinal);
    }

    [Fact]
    public void KillProcessesMenuPlanner_IsRemoved()
    {
        var path = Path.Combine(FindRepoRoot(), "Workspace1C", "Services", "KillProcessesMenuPlanner.cs");
        Assert.False(File.Exists(path), "Планировщик подменю должен быть удалён при откате #134.");
    }

    [Fact]
    public void UtilityKillProcesses_ReportIsRevertedToCounters()
    {
        var wpf = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Tools.cs");
        var avalonia = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Tools.cs");
        var service = ReadProjectFile("Workspace1C", "Services", "InfobaseMaintenanceService.Processes.cs");

        Assert.Contains("Main.ProcessesKilled", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildBaseNamesByPid", wpf, StringComparison.Ordinal);
        Assert.Contains("Main.ProcessesKillFailed", avalonia, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatKillAllSummary", avalonia, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtendSnapshotForKillReport", service, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAliveOneCProcessIds", service, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasNewProcessInspectorKeys(string file)
    {
        var strings = LoadStrings(file);

        Assert.True(strings.ContainsKey("ProcessInspector.Columns"));
        Assert.True(strings.ContainsKey("ProcessInspector.Pid"));
        Assert.True(strings.ContainsKey("ProcessInspector.KillManyConfirm"));
        Assert.True(strings.ContainsKey("ProcessInspector.KillSummaryFormat"));
        Assert.True(strings.ContainsKey("ProcessInspector.KillFailedPidsFormat"));
        Assert.True(strings.ContainsKey("Settings.Hotkeys.ProcessInspector"));
        Assert.True(strings.ContainsKey("Main.ProcessInspectorTooltip"));
        Assert.Contains("{0}", strings["ProcessInspector.KillManyConfirm"], StringComparison.Ordinal);
        Assert.Contains("{0}", strings["ProcessInspector.KillSummaryFormat"], StringComparison.Ordinal);
        Assert.Contains("{1}", strings["ProcessInspector.KillSummaryFormat"], StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RuAndEn_HaveSameNewKeySet()
    {
        var keys = new[]
        {
            "ProcessInspector.Columns",
            "ProcessInspector.Pid",
            "ProcessInspector.KillManyConfirm",
            "ProcessInspector.KillSummaryFormat",
            "ProcessInspector.KillFailedPidsFormat",
            "Settings.Hotkeys.ProcessInspector",
            "Main.ProcessInspectorTooltip"
        };

        var ru = LoadStrings("ru.json");
        var en = LoadStrings("en.json");

        foreach (var key in keys)
        {
            Assert.True(ru.ContainsKey(key), $"ru.json: нет ключа {key}");
            Assert.True(en.ContainsKey(key), $"en.json: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(ru[key]));
            Assert.False(string.IsNullOrWhiteSpace(en[key]));
        }
    }

    // ===================== Вспомогательные методы =====================

    private static ProcessInspectorViewModel CreateViewModel(
        FakeRunningService service,
        out FakeKiller killer,
        out RecordingDialogs dialogs,
        AppSettings? settings = null,
        Action? saveSettings = null)
    {
        killer = new FakeKiller();
        dialogs = new RecordingDialogs();
        return new ProcessInspectorViewModel(
            service,
            killer,
            dialogs,
            Enumerable.Empty<Infobase>(),
            _ => { },
            null,
            settings,
            saveSettings);
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException("Состояние ViewModel не достигнуто за отведённое время.");
            Thread.Sleep(10);
        }
    }

    private static RunningOneCProcessDetails Proc(int pid, string processName, string commandLine = "") =>
        new(pid, processName, commandLine, null, null, null);

    /// <summary>
    /// Вырезает объявление метода вместе с телом: source-гвардам нужен именно
    /// метод индикатора, а не весь файл, иначе проверки ловят посторонний код
    /// (например, вызов <c>BuildColumnCheckIndicator()</c> в обработчике меню).
    /// </summary>
    private static string ExtractMethod(string code, string methodName)
    {
        const string declaration = "private static ";
        var searchFrom = 0;
        while (true)
        {
            var decl = code.IndexOf(declaration, searchFrom, StringComparison.Ordinal);
            Assert.True(decl >= 0, $"В файле нет объявления метода {methodName}.");
            var nameStart = code.IndexOf(methodName + "(", decl, StringComparison.Ordinal);
            var lineEnd = code.IndexOf('\n', decl);
            if (nameStart >= 0 && (lineEnd < 0 || nameStart < lineEnd))
            {
                var end = code.IndexOf("\n        }", nameStart, StringComparison.Ordinal);
                Assert.True(end > nameStart, $"Не найден конец метода {methodName}.");
                return code[decl..end];
            }

            searchFrom = decl + declaration.Length;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден.");
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
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

    private static Dictionary<string, string> LoadStrings(string file)
    {
        var path = Path.Combine(
            FindRepoRoot(), "Workspace1C", "Localization", "Languages", file);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        // Формат файла локализации: { "code": "...", "name": "...", "strings": { ... } }.
        var root = document.RootElement;
        var stringsElement = root.TryGetProperty("strings", out var nested) ? nested : root;

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in stringsElement.EnumerateObject())
        {
            // В файле локализации есть и вложенные объекты (например, ElementFonts) —
            // для проверки ключей нужны только строковые значения.
            if (pair.Value.ValueKind == JsonValueKind.String)
                strings[pair.Name] = pair.Value.GetString() ?? string.Empty;
        }

        return strings;
    }

    /// <summary>Настраиваемый источник процессов.</summary>
    private sealed class FakeRunningService : IRunningInfobasesService
    {
        private IReadOnlyList<RunningOneCProcessDetails> _details;

        public FakeRunningService(params RunningOneCProcessDetails[] details) => _details = details;

        public void Set(params RunningOneCProcessDetails[] details) => _details = details;

        public IReadOnlyList<RunningOneCProcess> GetRunning() => Array.Empty<RunningOneCProcess>();

        public IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails() => _details;
    }

    /// <summary>Записывает вызовы Kill и результат (общий либо по PID).</summary>
    private sealed class FakeKiller : IOneCProcessKiller
    {
        public List<(int Pid, string? Token)> Calls { get; } = new();

        public bool Result { get; set; } = true;

        public Func<int, bool>? ResultByPid { get; set; }

        public string? LastError { get; set; }

        public bool Kill(int pid, string? startTimeToken)
        {
            Calls.Add((pid, startTimeToken));
            return ResultByPid?.Invoke(pid) ?? Result;
        }
    }

    /// <summary>Записывает подтверждения/предупреждения/сообщения.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public List<string> Confirms { get; } = new();

        public List<string> Warnings { get; } = new();

        public List<string> Infos { get; } = new();

        public bool ConfirmResult { get; set; } = true;

        public void ShowInfo(string message, string title = "") => Infos.Add(message);

        public void ShowWarning(string message, string title = "") => Warnings.Add(message);

        public void ShowError(string message, string title = "")
        {
        }

        public bool Confirm(string message, string title = "")
        {
            Confirms.Add(message);
            return ConfirmResult;
        }

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}
