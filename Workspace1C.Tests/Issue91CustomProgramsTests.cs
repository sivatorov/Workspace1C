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
/// Issue #91 «Группа „Мои программы“ под кнопками запуска конфигуратора»
/// (v1.0.1.65, доработка — v1.0.1.69): пользовательские программы с именем, файлом,
/// аргументами, рабочей папкой и иконкой; отображение группы «списком» (как родной
/// стартер 1С) либо «иконками» (в строку с переносом); переключатель хранится
/// в настройках.
/// <list type="bullet">
/// <item>хранилище <see cref="CustomProgramsStore"/> — roundtrip JSON, порядок, битый
/// файл, пустой список, уникальные Id;</item>
/// <item><see cref="CustomProgramLauncher"/> — сборка командной строки/параметров запуска,
/// рабочая папка, отсутствующий файл → понятная ошибка;</item>
/// <item>режим отображения <see cref="CustomProgramsViewModes"/> и roundtrip
/// <see cref="AppSettings.CustomProgramsViewMode"/> (старые профили → «Списком»);</item>
/// <item>флаг «Иконка из файла» <see cref="CustomProgram.UseCustomIcon"/> и обратная
/// совместимость JSON (старые записи с заполненным <c>IconPath</c> считаются
/// включёнными), кэш извлечённых иконок исполняемого файла;</item>
/// <item>автоперенос «1С:Стартер» в группу при первом запуске
/// (<see cref="CustomProgramsStarterMigration"/>, флаг
/// <see cref="AppSettings.CustomProgramsStarterMigrated"/>);</item>
/// <item>редактор <see cref="CustomProgramsViewModel"/> — добавление/удаление/порядок и
/// сохранение;</item>
/// <item>source-гварды WPF/Avalonia (группа на панели запуска, WrapPanel в режиме
/// «иконками», галочка «Иконка из файла», отсутствие отдельной кнопки стартера)
/// и локализация ru/en.</item>
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

    // ===================== Флаг «Иконка из файла» (1.0.1.69) =====================

    [Fact]
    public void UseCustomIcon_DefaultsFromIconPathForOldRecords()
    {
        // Старая запись custom_programs.json: поля UseCustomIcon нет, иконка задана —
        // считается, что галочка «Иконка из файла» включена (обратная совместимость).
        var old = JsonSerializer.Deserialize<CustomProgram>(
            "{\"Name\":\"Старая\",\"FilePath\":\"a.exe\",\"IconPath\":\"C:\\\\icons\\\\a.ico\"}")!;
        Assert.True(old.UseCustomIcon);
        Assert.Equal(@"C:\icons\a.ico", CustomProgramIconProvider.ResolveCustomIconPath(old));

        // Старая запись без иконки: иконка берётся из исполняемого файла.
        var withoutIcon = JsonSerializer.Deserialize<CustomProgram>(
            "{\"Name\":\"Без иконки\",\"FilePath\":\"b.exe\"}")!;
        Assert.False(withoutIcon.UseCustomIcon);
        Assert.Null(CustomProgramIconProvider.ResolveCustomIconPath(withoutIcon));
    }

    [Fact]
    public void UseCustomIcon_ExplicitValueWinsOverIconPath()
    {
        var program = JsonSerializer.Deserialize<CustomProgram>(
            "{\"Name\":\"Своя\",\"FilePath\":\"a.exe\",\"IconPath\":\"C:\\\\icons\\\\a.ico\",\"UseCustomIcon\":false}")!;
        Assert.False(program.UseCustomIcon);
        Assert.Null(CustomProgramIconProvider.ResolveCustomIconPath(program));

        program.UseCustomIcon = true;
        Assert.Equal(@"C:\icons\a.ico", CustomProgramIconProvider.ResolveCustomIconPath(program));

        // После явной установки флага очистка пути не включает иконку из файла.
        program.UseCustomIcon = false;
        program.IconPath = "";
        Assert.False(program.UseCustomIcon);
    }

    [Fact]
    public void UseCustomIcon_StoreRoundtrip_PreservesFlag()
    {
        var store = NewStore();
        var withIcon = Program("Своя", 0);
        withIcon.IconPath = @"C:\icons\a.ico";
        withIcon.UseCustomIcon = true;
        var withoutIcon = Program("Из файла программы", 1);
        withoutIcon.IconPath = @"C:\icons\b.ico";
        withoutIcon.UseCustomIcon = false;

        store.SaveAll(new[] { withIcon, withoutIcon });
        var loaded = store.LoadAll();

        Assert.True(loaded[0].UseCustomIcon);
        Assert.False(loaded[1].UseCustomIcon);
        // Путь сохранён в обоих случаях — снятая галочка его не стирает.
        Assert.Equal(@"C:\icons\b.ico", loaded[1].IconPath);
    }

    [Fact]
    public void UseCustomIcon_ChangeNotifications_ForEditor()
    {
        var program = new CustomProgram();
        var changed = new List<string?>();
        program.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        program.IconPath = "icon.ico";
        Assert.Contains(nameof(CustomProgram.UseCustomIcon), changed);

        changed.Clear();
        program.IconPath = "";
        Assert.Contains(nameof(CustomProgram.UseCustomIcon), changed);

        changed.Clear();
        program.UseCustomIcon = true;
        Assert.Contains(nameof(CustomProgram.UseCustomIcon), changed);
    }

    [Fact]
    public void ExecutableIcon_IsCachedAndNeverThrows()
    {
        CustomProgramIconProvider.ClearCache();
        Assert.Null(CustomProgramIconProvider.TryGetExecutableIcon(null));
        Assert.Null(CustomProgramIconProvider.TryGetExecutableIcon(""));
        Assert.Null(CustomProgramIconProvider.TryGetExecutableIcon(
            Path.Combine(_tempDir, "missing.exe")));

        var existing = Path.Combine(_tempDir, "present.exe");
        File.WriteAllText(existing, "stub");
        var first = CustomProgramIconProvider.TryGetExecutableIcon(existing);
        var second = CustomProgramIconProvider.TryGetExecutableIcon(existing);

#if !LINUX
        // Windows: извлекается связанная иконка файла и кэшируется (один массив).
        Assert.NotNull(first);
        Assert.Same(first, second);
#else
        // Linux: извлечение не поддерживается — стандартная иконка группы.
        Assert.Null(first);
#endif
    }

    // ===================== Автоперенос «1С:Стартер» (1.0.1.69) =====================

    [Fact]
    public void StarterMigration_EmptyList_AddsStarterAndPersistsFlag()
    {
        var store = NewStore();
        var starter = Path.Combine(_tempDir, "1CEStart.exe");

        var result = CustomProgramsStarterMigration.TryMigrate(
            store, alreadyMigrated: false, () => starter, "1С:Стартер");

        Assert.True(result.ProgramAdded);
        Assert.True(result.ShouldPersistFlag);
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("1С:Стартер", loaded[0].Name);
        Assert.Equal(starter, loaded[0].FilePath);
        Assert.True(loaded[0].Enabled);
        Assert.False(loaded[0].UseCustomIcon);
    }

    [Fact]
    public void StarterMigration_NonEmptyList_KeepsProgramsAndPersistsFlag()
    {
        var store = NewStore();
        store.SaveAll(new[] { Program("Своя", 0) });

        var result = CustomProgramsStarterMigration.TryMigrate(
            store, alreadyMigrated: false, () => Path.Combine(_tempDir, "1CEStart.exe"));

        Assert.False(result.ProgramAdded);
        // Флаг ставим, чтобы стартер не появился позже после удаления всех программ.
        Assert.True(result.ShouldPersistFlag);
        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Своя", loaded[0].Name);
    }

    [Fact]
    public void StarterMigration_AlreadyMigrated_DoesNothing()
    {
        var store = NewStore();
        var searchCalled = false;

        var result = CustomProgramsStarterMigration.TryMigrate(
            store, alreadyMigrated: true,
            () => { searchCalled = true; return Path.Combine(_tempDir, "1CEStart.exe"); });

        Assert.False(result.ProgramAdded);
        Assert.False(result.ShouldPersistFlag);
        Assert.False(searchCalled);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void StarterMigration_StarterNotFound_DoesNotPersistFlag()
    {
        var store = NewStore();

        var notFound = CustomProgramsStarterMigration.TryMigrate(
            store, alreadyMigrated: false, () => null);
        Assert.False(notFound.ProgramAdded);
        // Флаг не ставим: при следующем запуске попытка повторится.
        Assert.False(notFound.ShouldPersistFlag);
        Assert.Empty(store.LoadAll());

        var throwing = CustomProgramsStarterMigration.TryMigrate(
            store, alreadyMigrated: false, () => throw new InvalidOperationException("boom"));
        Assert.False(throwing.ProgramAdded);
        Assert.False(throwing.ShouldPersistFlag);
        Assert.Empty(store.LoadAll());

        // Без хранилища миграция не выполняется и не падает.
        var noStore = CustomProgramsStarterMigration.TryMigrate(
            null, alreadyMigrated: false, () => "x");
        Assert.False(noStore.ProgramAdded);
        Assert.False(noStore.ShouldPersistFlag);
    }

    [Fact]
    public void AppSettings_StarterMigrationFlag_RoundtripsAndDefaultsFalse()
    {
        Assert.False(new AppSettings().CustomProgramsStarterMigrated);
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{}")!.CustomProgramsStarterMigrated);

        var json = JsonSerializer.Serialize(new AppSettings { CustomProgramsStarterMigrated = true });
        Assert.True(JsonSerializer.Deserialize<AppSettings>(json)!.CustomProgramsStarterMigrated);
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
        // Иконка программы: галочка «Иконка из файла» либо основная иконка exe.
        Assert.Contains("CustomProgramIconConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("CustomProgramIconVisibilityConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("UseCustomIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("FilePath", xaml, StringComparison.Ordinal);
        // Отдельной кнопки «1С:Стартер» на панели больше нет (issue #91): стартер
        // переносится в группу «Мои программы».
        Assert.DoesNotContain("LaunchNativeStarterCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Main.NativeStarter", xaml, StringComparison.Ordinal);
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
        // Иконка: своя из файла либо основная иконка исполняемого файла.
        Assert.Contains("CustomProgramIconSource.Load", code, StringComparison.Ordinal);
        // Отдельной кнопки «1С:Стартер» на панели больше нет (issue #91).
        Assert.DoesNotContain("starterBlock", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenNativeStarterCommand", code, StringComparison.Ordinal);
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
        // Галочка «Иконка из файла» (issue #91): выбор/сброс файла доступны только
        // при включённой галочке.
        Assert.Contains("Launch.CustomPrograms.UseCustomIcon", wpf, StringComparison.Ordinal);
        Assert.Contains("SelectedProgram.UseCustomIcon", wpf, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding SelectedProgram.UseCustomIcon}\"", wpf, StringComparison.Ordinal);
        Assert.Contains("CustomProgramIconConverter", wpf, StringComparison.Ordinal);

        var wpfCode = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        Assert.Contains("OnBrowseIcon_Click", wpfCode, StringComparison.Ordinal);
        Assert.Contains("IconDialogFilter", wpfCode, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("BrowseIcon", avalonia, StringComparison.Ordinal);
        Assert.Contains("IconPath", avalonia, StringComparison.Ordinal);
        Assert.Contains("IsListViewMode", avalonia, StringComparison.Ordinal);
        Assert.Contains("IsIconsViewMode", avalonia, StringComparison.Ordinal);
        // Галочка «Иконка из файла» и блокировка выбора файла без неё (issue #91).
        Assert.Contains("Launch.CustomPrograms.UseCustomIcon", avalonia, StringComparison.Ordinal);
        Assert.Contains("SelectedProgram.UseCustomIcon", avalonia, StringComparison.Ordinal);
        Assert.Contains("CustomProgramIconConverter", avalonia, StringComparison.Ordinal);
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

    [Fact]
    public void MainViewModels_RunStarterMigrationOnStartup()
    {
        // Общий хук объявлен в общем файле, реализации — в платформенных.
        var shared = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.cs");
        Assert.Contains("EnsureCustomProgramsStarterMigration", shared, StringComparison.Ordinal);

        var wpfHooks = ReadProjectFile(
            "Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.Windows.cs");
        Assert.Contains("CustomProgramsStarterMigration.TryMigrate", wpfHooks, StringComparison.Ordinal);
        Assert.Contains("NativeStarterLocator.FindStarterPath", wpfHooks, StringComparison.Ordinal);

        var avaloniaHooks = ReadProjectFile(
            "Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.Avalonia.cs");
        Assert.Contains("CustomProgramsStarterMigration.TryMigrate", avaloniaHooks, StringComparison.Ordinal);
        Assert.Contains("NativeStarterLocator.FindStarterPath", avaloniaHooks, StringComparison.Ordinal);

        // Миграция вызывается при старте до загрузки списка программ.
        var wpfStart = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.cs");
        Assert.Contains("EnsureCustomProgramsStarterMigration();", wpfStart, StringComparison.Ordinal);

        var avaloniaStart = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs");
        Assert.Contains("EnsureCustomProgramsStarterMigration();", avaloniaStart, StringComparison.Ordinal);
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
        "Launch.CustomPrograms.UseCustomIcon",
        "Launch.CustomPrograms.UseCustomIconTooltip",
        "Launch.CustomPrograms.NativeStarter",
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
