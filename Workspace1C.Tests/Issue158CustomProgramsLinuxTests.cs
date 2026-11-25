using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #158 «Linux — мои программы» (v1.0.1.120):
/// <list type="bullet">
/// <item>пустая группа «Мои программы» видна всегда — с подсказкой и кнопкой настройки
/// (WPF и Avalonia), даже без установленной 1С/стартера;</item>
/// <item>шестерёнка настроек использует существующий ключ <c>IconSettings</c>, а не
/// отсутствующий <c>IconCog</c> (резолвер откатывался на «папку»);</item>
/// <item>добавление программы с бинарным путём <c>/usr/bin/mc</c> не даёт ошибку,
/// сохраняется/удаляется и не бросает исключений при извлечении иконки;</item>
/// <item>редактор меню показывает добавленную программу (общий источник
/// <c>Programs</c> / <see cref="ICustomProgramsStore"/>);</item>
/// <item>защита от <c>null</c> в шаблоне иконок редактора «Утилит» — причина
/// NullReferenceException из скриншота issue;</item>
/// <item>локализация ru/en для новых текстов.</item>
/// </list>
/// </summary>
public sealed class Issue158CustomProgramsLinuxTests : IDisposable
{
    private readonly string _tempDir;

    public Issue158CustomProgramsLinuxTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_issue158_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временный каталог — не критично для результата теста.
        }
    }

    private CustomProgramsStore NewStore() => new(directoryOverride: _tempDir);

    // ===================== 1. Пустая группа видна всегда =====================

    [Fact]
    public void EmptyGroup_IsVisible_WithSettingsButton()
    {
        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        // Корень группы больше не скрывается при отсутствии программ.
        Assert.DoesNotContain(
            "root.Bind(Control.IsVisibleProperty, new Binding(\"HasCustomPrograms\"))",
            avalonia, StringComparison.Ordinal);
        Assert.Contains("ShowCustomProgramsEmptyHint", avalonia, StringComparison.Ordinal);
        Assert.Contains("Launch.CustomPrograms.EmptyHint", avalonia, StringComparison.Ordinal);
        Assert.Contains("Launch.CustomPrograms.SettingsButton", avalonia, StringComparison.Ordinal);
        Assert.Contains("ShowCustomProgramsSettingsCommand", avalonia, StringComparison.Ordinal);

        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");
        Assert.DoesNotContain(
            "Visibility=\"{Binding HasCustomPrograms",
            wpf, StringComparison.Ordinal);
        Assert.Contains("ShowCustomProgramsEmptyHint", wpf, StringComparison.Ordinal);
        Assert.Contains("Launch.CustomPrograms.EmptyHint", wpf, StringComparison.Ordinal);
        Assert.Contains("Launch.CustomPrograms.SettingsButton", wpf, StringComparison.Ordinal);

        // Вычисляемые признаки группы объявлены и поднимаются при перезагрузке.
        var vm = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.cs");
        Assert.Contains("ShowCustomProgramsEmptyHint", vm, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowCustomProgramsEmptyHint));", vm, StringComparison.Ordinal);
        // Списки/иконки показываются только при наличии программ (issue #158).
        Assert.Contains("HasCustomPrograms &&", vm, StringComparison.Ordinal);
    }

    // ===================== 2. Шестерёнка вместо папки =====================

    [Fact]
    public void GearIcon_UsesExistingIconKey()
    {
        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs");
        Assert.DoesNotContain("\"IconCog\"", avalonia, StringComparison.Ordinal);
        Assert.Contains(
            "IconHelper.MakeIcon(\"IconSettings\"", avalonia, StringComparison.Ordinal);

        var settings = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.DoesNotContain("\"IconCog\"", settings, StringComparison.Ordinal);
        Assert.Contains(
            "\"IconSettings\", \"Launch.CustomPrograms.SettingsButton\"",
            settings, StringComparison.Ordinal);

        // Ключ есть в базовом наборе иконок (Icons.axaml).
        var icons = ReadProjectFile("Workspace1C", "Themes", "Icons.axaml");
        Assert.Contains("x:Key=\"IconSettings\"", icons, StringComparison.Ordinal);

        // WPF-паритет: там используется существующий materialDesign Kind="Cog".
        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.xaml");
        Assert.Contains("Kind=\"Cog\"", wpf, StringComparison.Ordinal);
    }

    // ===================== 3. Ошибка при /usr/bin/mc =====================

    [Fact]
    public void AddProgram_WithBinaryPath_DoesNotThrow()
    {
        var store = NewStore();
        var vm = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);

        vm.AddProgram();
        var program = vm.Programs.Single();
        program.Name = "mc";
        program.FilePath = "/usr/bin/mc";

        Assert.True(vm.TrySave(out var saveError), saveError);
        var stored = store.LoadAll();
        Assert.Single(stored);
        Assert.Equal("/usr/bin/mc", stored[0].FilePath);
        Assert.True(stored[0].Enabled);

        // Извлечение иконки бинарника не бросает (на Linux — null, Windows — безопасный null).
        Assert.Null(CustomProgramIconProvider.TryGetExecutableIcon("/usr/bin/mc"));

        // Удаление из редактора сохраняется.
        var editor = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);
        Assert.Single(editor.Programs);
        editor.DeleteSelected();
        Assert.True(editor.TrySave(out var deleteError), deleteError);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void UtilitiesMenuEditor_IconTemplate_HandlesNullOption()
    {
        // Причина NullReferenceException со скриншота #158: шаблон ComboBox иконок
        // получал null (ничего не выбрано). Теперь метод принимает null и выходит.
        var source = ReadProjectFile("Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        Assert.Contains("BuildIconRow(UtilitiesIconOption? option)", source, StringComparison.Ordinal);
        Assert.Contains("if (option is null)", source, StringComparison.Ordinal);
        Assert.Contains("g?.GroupLabel ?? string.Empty", source, StringComparison.Ordinal);
    }

    // ===================== 4. Строка программы в редакторе =====================

    [Fact]
    public void Editor_LoadsPrograms_FromSameStore()
    {
        var store = NewStore();
        var program = new CustomProgram
        {
            Name = "mc",
            FilePath = "/usr/bin/mc",
            Order = 0
        };
        store.SaveAll(new[] { program });

        // Редактор читает тот же store (каталог активного профиля) — строка видна.
        var editor = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);
        Assert.Single(editor.Programs);
        Assert.Equal("mc", editor.Programs[0].Name);
        Assert.Equal("/usr/bin/mc", editor.Programs[0].FilePath);
        Assert.Same(editor.Programs[0], editor.SelectedProgram);

        // Avalonia-окно привязано к свойству Programs (как WPF), а не к несуществующему.
        var window = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("new Binding(\"Programs\")", window, StringComparison.Ordinal);
        Assert.DoesNotContain("new Binding(\"CustomPrograms\")", window, StringComparison.Ordinal);

        var wpf = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml");
        Assert.Contains("ItemsSource=\"{Binding Programs}\"", wpf, StringComparison.Ordinal);
    }

    [Fact]
    public void ReloadAfterSave_UpdatesPanel()
    {
        // После сохранения редактора панель перечитывает store (ReloadCustomPrograms)
        // и обновляет все производные признаки группы.
        var shared = ReadProjectFile("Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.cs");
        var reloadStart = shared.IndexOf("public void ReloadCustomPrograms()", StringComparison.Ordinal);
        Assert.True(reloadStart >= 0, "Не найден ReloadCustomPrograms");
        var reloadBlock = shared.Substring(reloadStart, Math.Min(1400, shared.Length - reloadStart));
        Assert.Contains("OnPropertyChanged(nameof(HasCustomPrograms));", reloadBlock, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ShowCustomProgramsEmptyHint));", reloadBlock, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(CustomProgramsListView));", reloadBlock, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(CustomProgramsIconsView));", reloadBlock, StringComparison.Ordinal);

        var avaloniaHook = ReadProjectFile(
            "Workspace1C", "ViewModels", "MainViewModel.CustomPrograms.Avalonia.cs");
        Assert.Contains("ReloadCustomPrograms();", avaloniaHook, StringComparison.Ordinal);

        // Повторная загрузка из хранилища отражает сохранение.
        var store = NewStore();
        store.SaveAll(new[]
        {
            new CustomProgram { Name = "Первая", FilePath = "/usr/bin/mc", Order = 0 }
        });
        var vm = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);
        vm.AddProgram();
        vm.Programs[^1].Name = "Вторая";
        vm.Programs[^1].FilePath = "/usr/bin/htop";
        Assert.True(vm.TrySave(out var error), error);

        var reloaded = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);
        Assert.Equal(2, reloaded.Programs.Count);
        Assert.Equal(new[] { "Первая", "Вторая" }, reloaded.Programs.Select(p => p.Name).ToArray());
    }

    // ===================== Локализация =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_ContainsEmptyHint(string fileName)
    {
        var strings = LoadLanguageStrings(fileName);
        Assert.True(strings.TryGetValue("Launch.CustomPrograms.EmptyHint", out var hint));
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.NotEqual("Launch.CustomPrograms.EmptyHint", hint);

        // Подсказка в настройках больше не утверждает, что группа скрыта без программ.
        Assert.True(strings.TryGetValue("Launch.CustomPrograms.SettingsHint", out var settingsHint));
        Assert.False(string.IsNullOrWhiteSpace(settingsHint));
    }

    [Fact]
    public void Localization_CustomProgramsKeysMatch()
    {
        var ru = LoadLanguageStrings("ru.json").Keys
            .Where(k => k.StartsWith("Launch.CustomPrograms.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var en = LoadLanguageStrings("en.json").Keys
            .Where(k => k.StartsWith("Launch.CustomPrograms.", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("Launch.CustomPrograms.EmptyHint", ru);
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
