using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #91 «Группа „Мои программы“ под кнопками запуска конфигуратора» (v1.0.1.65):
/// пользовательские программы с именем, файлом, аргументами, рабочей папкой и иконкой
/// из файла; отображение группы «списком» (как родной стартер 1С) либо «иконками»
/// (в строку с переносом); переключатель хранится в настройках.
/// <list type="bullet">
/// <item>хранилище <see cref="CustomProgramsStore"/> — roundtrip JSON, порядок, битый
/// файл, пустой список, уникальные Id;</item>
/// <item><see cref="CustomProgramLauncher"/> — сборка командной строки/параметров запуска,
/// рабочая папка, отсутствующий файл → понятная ошибка;</item>
/// <item>режим отображения <see cref="CustomProgramsViewModes"/> и roundtrip
/// <see cref="AppSettings.CustomProgramsViewMode"/> (старые профили → «Списком»);</item>
/// <item>редактор <see cref="CustomProgramsViewModel"/> — добавление/удаление/порядок и
/// сохранение;</item>
/// <item>source-гварды WPF/Avalonia (группа на панели запуска, WrapPanel в режиме
/// «иконками», выбор иконки из файла) и локализация ru/en.</item>
/// </list>
/// </summary>
public sealed class Issue91CustomProgramsTests
{
    private readonly string _tempDir;

    public Issue91CustomProgramsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_custom_programs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private CustomProgramsStore NewStore() => new CustomProgramsStore(directoryOverride: _tempDir);

    private static CustomProgram Program(string name, int order, string? file = null, bool enabled = true) => new()
    {
        Name = name,
        FilePath = file ?? Path.Combine(Path.GetTempPath(), name + ".exe"),
        Arguments = "/arg " + order,
        WorkingDirectory = Path.GetTempPath(),
        IconPath = string.Empty,
        Enabled = enabled,
        Order = order
    };

    // ===================== Хранилище =====================

    [Fact]
    public void Store_Roundtrip_PreservesFieldsAndOrder()
    {
        var store = NewStore();
        var first = Program("Первая", 0);
        var second = Program("Вторая", 1);
        second.Arguments = "--flag value";
        second.IconPath = @"C:\icons\second.ico";

        store.SaveAll(new[] { first, second });
        var loaded = store.LoadAll();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Первая", loaded[0].Name);
        Assert.Equal("Вторая", loaded[1].Name);
        Assert.Equal("--flag value", loaded[1].Arguments);
        Assert.Equal(@"C:\icons\second.ico", loaded[1].IconPath);
        Assert.Equal(0, loaded[0].Order);
        Assert.Equal(1, loaded[1].Order);
    }

    [Fact]
    public void Store_SaveAll_KeepsNewOrder()
    {
        var store = NewStore();
        var first = Program("Первая", 0);
        var second = Program("Вторая", 1);
        store.SaveAll(new[] { first, second });

        // Перемещение «вверх» в редакторе: порядок пересчитывается по позиции.
        second.Order = 0;
        first.Order = 1;
        store.SaveAll(new[] { second, first });

        var loaded = store.LoadAll();
        Assert.Equal(new[] { "Вторая", "Первая" }, loaded.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Store_MissingFile_ReturnsEmpty()
    {
        var store = NewStore();
        Assert.Empty(store.LoadAll());
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Store_BrokenJson_ReturnsEmptyAndKeepsFile()
    {
        var store = NewStore();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(store.FilePath, "{ это не JSON ");

        Assert.Empty(store.LoadAll());
        // Битый файл не перезаписывается — пользователь может поправить его вручную.
        Assert.True(File.Exists(store.FilePath));
        Assert.Contains("не JSON", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Store_DuplicateAndEmptyIds_GetUniqueIds()
    {
        var store = NewStore();
        var json = JsonSerializer.Serialize(new[]
        {
            new CustomProgram { Id = "", Name = "Первая", FilePath = "a.exe", Order = 0 },
            new CustomProgram { Id = "same", Name = "Вторая", FilePath = "b.exe", Order = 1 },
            new CustomProgram { Id = "same", Name = "Третья", FilePath = "c.exe", Order = 2 }
        });
        File.WriteAllText(store.FilePath, json);

        var loaded = store.LoadAll();
        Assert.Equal(3, loaded.Count);
        Assert.Equal(3, loaded.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(loaded, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
    }

    [Fact]
    public void Store_EmptyNames_AreDropped()
    {
        var store = NewStore();
        var json = JsonSerializer.Serialize(new[]
        {
            new CustomProgram { Name = "  ", FilePath = "a.exe" },
            new CustomProgram { Name = "Рабочая", FilePath = "b.exe" }
        });
        File.WriteAllText(store.FilePath, json);

        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Рабочая", loaded[0].Name);
    }

    [Fact]
    public void Store_Delete_RemovesOnlyRequested()
    {
        var store = NewStore();
        var first = Program("Первая", 0);
        var second = Program("Вторая", 1);
        store.SaveAll(new[] { first, second });

        store.Delete(first.Id);
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(second.Id, loaded[0].Id);
        Assert.Null(store.Get(first.Id));
        Assert.NotNull(store.Get(second.Id));
    }

    // ===================== Лаунчер =====================

    [Fact]
    public void Launcher_BuildStartInfo_UsesFileArgumentsAndWorkingDirectory()
    {
        var program = new CustomProgram
        {
            Name = "EDT",
            FilePath = Path.Combine(_tempDir, "1cedt.exe"),
            Arguments = "-data \"C:\\ws\"",
            WorkingDirectory = _tempDir
        };

        var info = CustomProgramLauncher.BuildStartInfo(program);

        Assert.Equal(program.FilePath, info.FileName);
        Assert.Equal("-data \"C:\\ws\"", info.Arguments);
        Assert.Equal(_tempDir, info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
        Assert.Equal(program.FilePath + " -data \"C:\\ws\"", CustomProgramLauncher.BuildCommandLine(program));
    }

    [Fact]
    public void Launcher_WorkingDirectory_FallsBackToExecutableFolder()
    {
        var exeDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(exeDir);
        var program = new CustomProgram { Name = "App", FilePath = Path.Combine(exeDir, "app.exe") };

        Assert.Equal(exeDir, CustomProgramLauncher.ResolveWorkingDirectory(program));
        Assert.Equal(exeDir, CustomProgramLauncher.BuildStartInfo(program).WorkingDirectory);
    }

    [Fact]
    public void Launcher_IsRunnable_RequiresExistingFile()
    {
        var missing = new CustomProgram { Name = "Нет", FilePath = Path.Combine(_tempDir, "missing.exe") };
        Assert.False(CustomProgramLauncher.IsRunnable(missing));
        Assert.False(CustomProgramLauncher.IsRunnable(new CustomProgram { Name = "Пусто" }));
        Assert.False(CustomProgramLauncher.IsRunnable(null));

        var existing = Path.Combine(_tempDir, "present.exe");
        File.WriteAllText(existing, "stub");
        Assert.True(CustomProgramLauncher.IsRunnable(
            new CustomProgram { Name = "Есть", FilePath = existing }));
    }

    [Fact]
    public void Launcher_TryLaunch_MissingFile_ReturnsReadableError()
    {
        var missing = new CustomProgram
        {
            Name = "Пропавшая",
            FilePath = Path.Combine(_tempDir, "no-such-file.exe")
        };

        var result = CustomProgramLauncher.TryLaunch(missing, logger: null, out var error);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
        // Сообщение содержит путь — пользователь понимает, что именно не найдено.
        Assert.Contains("no-such-file.exe", error);
    }

    [Fact]
    public void Launcher_TryLaunch_EmptyFile_ReturnsRequiredError()
    {
        var result = CustomProgramLauncher.TryLaunch(
            new CustomProgram { Name = "Без файла" }, logger: null, out var error);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ===================== Режим отображения и настройки =====================

    [Theory]
    [InlineData(null, CustomProgramsViewModes.List)]
    [InlineData("", CustomProgramsViewModes.List)]
    [InlineData("  ", CustomProgramsViewModes.List)]
    [InlineData("unknown", CustomProgramsViewModes.List)]
    [InlineData("List", CustomProgramsViewModes.List)]
    [InlineData("list", CustomProgramsViewModes.List)]
    [InlineData("Icons", CustomProgramsViewModes.Icons)]
    [InlineData("icons", CustomProgramsViewModes.Icons)]
    public void ViewModes_Normalize(string? value, string expected)
        => Assert.Equal(expected, CustomProgramsViewModes.Normalize(value));

    [Fact]
    public void ViewModes_ParseAndToSettingString()
    {
        Assert.Equal(CustomProgramsViewMode.Icons, CustomProgramsViewModes.Parse("Icons"));
        Assert.Equal(CustomProgramsViewMode.List, CustomProgramsViewModes.Parse("что-то"));
        Assert.Equal("Icons", CustomProgramsViewModes.ToSettingString(CustomProgramsViewMode.Icons));
        Assert.Equal("List", CustomProgramsViewModes.ToSettingString(CustomProgramsViewMode.List));
    }

    [Fact]
    public void AppSettings_Roundtrip_PreservesViewMode()
    {
        var settings = new AppSettings { CustomProgramsViewMode = CustomProgramsViewModes.Icons };
        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(CustomProgramsViewModes.Icons, restored.CustomProgramsViewMode);
    }

    [Fact]
    public void AppSettings_OldProfileWithoutField_DefaultsToList()
    {
        // Старый settings.json без поля CustomProgramsViewMode.
        var restored = JsonSerializer.Deserialize<AppSettings>("{\"ShowFavoritesOnly\":false}")!;
        restored.NormalizeForLoad();

        Assert.Equal(CustomProgramsViewModes.List, restored.CustomProgramsViewMode);
    }

    [Fact]
    public void AppSettings_UnknownViewMode_NormalizesToList()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("{\"CustomProgramsViewMode\":\"какой-то\"}")!;
        restored.NormalizeForLoad();

        Assert.Equal(CustomProgramsViewModes.List, restored.CustomProgramsViewMode);
    }

    // ===================== Редактор =====================

    [Fact]
    public void ViewModel_AddAndSave_PersistsProgram()
    {
        var store = NewStore();
        var vm = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);

        vm.AddProgram();
        Assert.Single(vm.Programs);
        Assert.NotNull(vm.SelectedProgram);
        Assert.True(vm.HasSelection);

        // Без исполняемого файла сохранение отклоняется понятной ошибкой.
        Assert.False(vm.TrySave(out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));

        vm.SelectedProgram!.FilePath = Path.Combine(_tempDir, "app.exe");
        Assert.True(vm.TrySave(out _));

        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(vm.SelectedProgram.Name, loaded[0].Name);
        Assert.Equal(0, loaded[0].Order);
    }

    [Fact]
    public void ViewModel_MoveAndDelete_UpdatesOrderAndSelection()
    {
        var store = NewStore();
        var vm = new CustomProgramsViewModel(store, CustomProgramsViewModes.Icons);
        vm.AddProgram();
        vm.Programs[0].Name = "Первая";
        vm.Programs[0].FilePath = "a.exe";
        vm.AddProgram();
        vm.SelectedProgram!.Name = "Вторая";
        vm.SelectedProgram!.FilePath = "b.exe";

        Assert.True(vm.CanMoveSelected(-1));
        vm.MoveSelected(-1);
        Assert.Equal("Вторая", vm.Programs[0].Name);
        Assert.Equal(CustomProgramsViewModes.Icons, vm.ViewMode);

        Assert.True(vm.TrySave(out _));
        Assert.Equal(new[] { "Вторая", "Первая" }, store.LoadAll().Select(p => p.Name).ToArray());

        vm.SelectedProgram = vm.Programs[0];
        vm.DeleteSelected();
        Assert.Single(vm.Programs);
        Assert.True(vm.HasSelection);
    }

    // ===================== Source-гварды =====================

    [Fact]
    public void WpfLaunchPanel_ContainsCustomProgramsGroup()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");

        Assert.Contains("Launch.CustomPrograms.Header", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowCustomProgramsSettingsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LaunchCustomProgramCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CustomProgramsListView", xaml, StringComparison.Ordinal);
        Assert.Contains("CustomProgramsIconsView", xaml, StringComparison.Ordinal);
        // Режим «иконками» — в строку с переносом.
        Assert.Contains("<WrapPanel", xaml, StringComparison.Ordinal);
        // Иконка из файла.
        Assert.Contains("FileIconPathToImageSourceConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("IconPath", xaml, StringComparison.Ordinal);
        // Группа идёт после кнопки родного стартера (под кнопками запуска).
        Assert.True(
            xaml.IndexOf("Launch.CustomPrograms.Header", StringComparison.Ordinal) >
            xaml.IndexOf("Main.NativeStarter", StringComparison.Ordinal),
            "Группа «Мои программы» должна идти под кнопками запуска (после родного стартера).");
    }

    [Fact]
    public void AvaloniaLaunchPanel_ContainsCustomProgramsGroup()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");

        Assert.Contains("BuildCustomProgramsBlock", code, StringComparison.Ordinal);
        Assert.Contains("Launch.CustomPrograms.Header", code, StringComparison.Ordinal);
        Assert.Contains("ShowCustomProgramsSettingsCommand", code, StringComparison.Ordinal);
        Assert.Contains("CustomProgramsListView", code, StringComparison.Ordinal);
        Assert.Contains("CustomProgramsIconsView", code, StringComparison.Ordinal);
        // Режим «иконками» — WrapPanel (строка с переносом).
        Assert.Contains("new WrapPanel()", code, StringComparison.Ordinal);
        Assert.Contains("CreateCustomProgramIcon", code, StringComparison.Ordinal);
        // Группа добавляется после блока кнопок запуска.
        Assert.True(
            code.IndexOf("BuildCustomProgramsBlock()", StringComparison.Ordinal) >
            code.IndexOf("panel.Children.Add(starterBlock)", StringComparison.Ordinal),
            "Группа «Мои программы» должна идти под кнопками запуска (после стартера).");
    }

    [Fact]
    public void EditorWindows_SupportIconFileAndViewMode()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml");
        Assert.Contains("Launch.CustomPrograms.PickIcon", wpf, StringComparison.Ordinal);
        Assert.Contains("IconPath", wpf, StringComparison.Ordinal);
        Assert.Contains("IsListViewMode", wpf, StringComparison.Ordinal);
        Assert.Contains("IsIconsViewMode", wpf, StringComparison.Ordinal);
        Assert.Contains("SelectedProgram.FilePath", wpf, StringComparison.Ordinal);
        Assert.Contains("SelectedProgram.Arguments", wpf, StringComparison.Ordinal);
        Assert.Contains("SelectedProgram.WorkingDirectory", wpf, StringComparison.Ordinal);

        var wpfCode = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        Assert.Contains("OnBrowseIcon_Click", wpfCode, StringComparison.Ordinal);
        Assert.Contains("IconDialogFilter", wpfCode, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("BrowseIcon", avalonia, StringComparison.Ordinal);
        Assert.Contains("IconPath", avalonia, StringComparison.Ordinal);
        Assert.Contains("IsListViewMode", avalonia, StringComparison.Ordinal);
        Assert.Contains("IsIconsViewMode", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindows_OpenCustomProgramsEditor()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.xaml.cs");
        Assert.Contains("OnCustomProgramsEdit_Click", wpf, StringComparison.Ordinal);
        Assert.Contains("ReloadCustomPrograms", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.Contains("OpenCustomProgramsEditor", avalonia, StringComparison.Ordinal);
        Assert.Contains("ReloadCustomPrograms", avalonia, StringComparison.Ordinal);
    }

    // ===================== Локализация =====================

    private static readonly string[] RequiredKeys =
    {
        "Launch.CustomPrograms.Header",
        "Launch.CustomPrograms.SettingsTooltip",
        "Launch.CustomPrograms.SettingsHint",
        "Launch.CustomPrograms.SettingsButton",
        "Launch.CustomPrograms.Title",
        "Launch.CustomPrograms.Name",
        "Launch.CustomPrograms.File",
        "Launch.CustomPrograms.Arguments",
        "Launch.CustomPrograms.WorkingDirectory",
        "Launch.CustomPrograms.Icon",
        "Launch.CustomPrograms.PickIcon",
        "Launch.CustomPrograms.ClearIcon",
        "Launch.CustomPrograms.Enabled",
        "Launch.CustomPrograms.Hint",
        "Launch.CustomPrograms.ViewModeHeader",
        "Launch.CustomPrograms.ViewModeList",
        "Launch.CustomPrograms.ViewModeIcons",
        "Launch.CustomPrograms.MoveUp",
        "Launch.CustomPrograms.MoveDown",
        "Launch.CustomPrograms.NewProgram",
        "Launch.CustomPrograms.ErrorTitle",
        "Launch.CustomPrograms.ErrorNameRequired",
        "Launch.CustomPrograms.ErrorFileRequired",
        "Launch.CustomPrograms.ErrorFileRequiredFor",
        "Launch.CustomPrograms.ErrorNotFound",
        "Launch.CustomPrograms.ErrorStart",
        "Launch.CustomPrograms.ErrorSave",
        "Launch.CustomPrograms.FileDialogTitle",
        "Launch.CustomPrograms.FileDialogFilter",
        "Launch.CustomPrograms.WorkingDirectoryDialogTitle",
        "Launch.CustomPrograms.IconDialogTitle",
        "Launch.CustomPrograms.IconDialogFilter"
    };

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_ContainsAllCustomProgramsKeys(string fileName)
    {
        var strings = LoadLanguageStrings(fileName);

        foreach (var key in RequiredKeys)
        {
            Assert.True(strings.TryGetValue(key, out var value), $"Нет ключа {key} в {fileName}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Пустой перевод «{key}» в {fileName}");
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void Localization_CustomProgramsKeySetsMatch()
    {
        var ru = LoadLanguageStrings("ru.json").Keys
            .Where(k => k.StartsWith("Launch.CustomPrograms.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var en = LoadLanguageStrings("en.json").Keys
            .Where(k => k.StartsWith("Launch.CustomPrograms.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(ru);
        Assert.Equal(ru, en);
    }

    // ===================== Вспомогательные методы =====================

    private static Dictionary<string, string> LoadLanguageStrings(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        return result;
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
}
