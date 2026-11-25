using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #159 «Linux — правый клик на Утилиты» (v1.0.1.120):
/// <list type="bullet">
/// <item>оба входа (ПКМ по кнопке раздела на панели и кнопка «Настроить структуру…»
/// в настройках) открывают редактор через единую точку
/// <c>UtilitiesMenuEditorWindow.ShowEditor</c>;</item>
/// <item>ошибка открытия больше не теряется в пустом <c>catch</c> — пишется в журнал
/// (<c>IAppLogger</c>) и показывается пользователю (<c>IDialogService.ShowError</c>,
/// ключи <c>Utilities.Editor.OpenFailed*</c>);</item>
/// <item>ПКМ работает и по разделителям между разделами — как в WPF (паритет);</item>
/// <item>шаблоны Avalonia терпят <c>null</c>-элемент (причина NullReferenceException
/// при открытии редактора), а расчёт геометрии окна не может уронить конструктор;</item>
/// <item>модель редактора конструируется с хранилищем и без него, сохранение
/// компоновки читается обратно.</item>
/// </list>
/// </summary>
public sealed class Issue159UtilitiesEditorLinuxTests : IDisposable
{
    private readonly string _tempDir;

    public Issue159UtilitiesEditorLinuxTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_issue159_" + Guid.NewGuid().ToString("N"));
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

    private UtilitiesMenuLayoutStore NewStore()
        => new(directoryOverride: _tempDir);

    // ===================== 1. Модель редактора =====================

    [Fact]
    public void Editor_Constructs_WithNullStore_AndWithStore()
    {
        // Пустое хранилище (сервис ещё не поднят) — редактор всё равно строится.
        var withoutStore = new UtilitiesMenuEditorViewModel(null);
        Assert.NotNull(withoutStore.Layout);
        Assert.NotEmpty(withoutStore.Roots);
        Assert.NotEmpty(withoutStore.CatalogIcons);

        // С хранилищем без файла компоновки — стандартная структура, без исключений.
        var withStore = new UtilitiesMenuEditorViewModel(NewStore());
        Assert.NotNull(withStore.Layout);
        Assert.NotEmpty(withStore.Roots);
        Assert.NotNull(withStore.Layout.Root);
    }

    [Fact]
    public void Editor_Save_RoundTripsLayout()
    {
        var store = NewStore();
        var vm = new UtilitiesMenuEditorViewModel(store);
        var rootsBefore = vm.Roots.Count;
        Assert.True(rootsBefore > 0);

        vm.SaveCommand.Execute(null);

        Assert.True(File.Exists(store.FilePath), "Компоновка должна быть записана в файл.");
        var saved = store.Load();
        Assert.NotNull(saved);
        // Миграция может добавить недостающие пункты каталога, но не теряет структуру.
        Assert.True(saved!.Root.Count >= rootsBefore,
            "Сохранённая компоновка не должна терять корневые узлы.");

        // Повторное открытие редактора читает ту же структуру.
        var reloaded = new UtilitiesMenuEditorViewModel(store);
        Assert.Equal(saved.Root.Count, reloaded.Roots.Count);
    }

    // ===================== 2. ПКМ по кнопкам и разделителям =====================

    [Fact]
    public void RightClickHandler_IsAttached_ToSectionButtons()
    {
        var panel = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");

        // ПКМ по кнопке раздела и по кнопке верхнеуровневого пункта.
        Assert.True(CountOccurrences(panel, "IsRightButtonPressed") >= 3,
            "ПКМ должен обрабатываться у кнопок разделов, пунктов и разделителей.");
        Assert.True(CountOccurrences(panel, "OpenUtilitiesMenuEditorDirectly();") >= 3,
            "Каждый обработчик ПКМ должен открывать редактор структуры меню.");

        // Разделитель между разделами тоже открывает редактор (паритет с WPF).
        Assert.Contains("var separator = CommandPanelSeparator();", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void RightClick_ParityWithWpf()
    {
        var wpf = ReadProjectFile("Workspace1C", "Views", "MainWindow.UtilitiesMenu.cs");
        Assert.Contains(
            "button.PreviewMouseRightButtonDown += OnUtilitiesButton_PreviewMouseRightButtonDown",
            wpf, StringComparison.Ordinal);
        Assert.Contains(
            "line.PreviewMouseRightButtonDown += OnUtilitiesButton_PreviewMouseRightButtonDown",
            wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.cs");
        Assert.Contains("IsRightButtonPressed", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenUtilitiesMenuEditorDirectly", avalonia, StringComparison.Ordinal);

        // Оба входа Avalonia используют общий открыватель редактора.
        var columns = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs");
        Assert.Contains("UtilitiesMenuEditorWindow.ShowEditor(this)", columns, StringComparison.Ordinal);
    }

    // ===================== 3. Настройки → редактор, диагностика =====================

    [Fact]
    public void OpenFromSettings_LogsAndShowsError_OnFailure()
    {
        var settings = ReadProjectFile("Workspace1C", "Views", "SettingsWindow.Avalonia.cs");
        Assert.Contains("UtilitiesMenuEditorWindow.ShowEditor(this)", settings, StringComparison.Ordinal);
        // Немого catch с созданием окна напрямую больше нет.
        Assert.DoesNotContain("var win = new UtilitiesMenuEditorWindow(store);", settings,
            StringComparison.Ordinal);

        var columns = ReadProjectFile("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs");
        Assert.DoesNotContain("var win = new UtilitiesMenuEditorWindow(store);", columns,
            StringComparison.Ordinal);

        var editor = ReadProjectFile(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        // Единая точка открытия и видимая диагностика сбоя.
        Assert.Contains("public static void ShowEditor(Window? owner)", editor, StringComparison.Ordinal);
        Assert.Contains("IAppLogger", editor, StringComparison.Ordinal);
        Assert.Contains(".Error(", editor, StringComparison.Ordinal);
        Assert.Contains("IDialogService", editor, StringComparison.Ordinal);
        Assert.Contains(".ShowError(", editor, StringComparison.Ordinal);
        Assert.Contains("Utilities.Editor.OpenFailed", editor, StringComparison.Ordinal);
        Assert.Contains("Utilities.Editor.OpenFailedTitle", editor, StringComparison.Ordinal);
    }

    // ===================== 4. Устойчивость построения окна =====================

    [Fact]
    public void Editor_Templates_HandleNullItems()
    {
        var editor = ReadProjectFile(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");

        // Причина «редактор не открывается»: Avalonia вызывает шаблоны и с null
        // (пустой ComboBox) — шаблон иконки и групп обязан это пережить.
        Assert.Contains("BuildIconRow(UtilitiesIconOption? option)", editor, StringComparison.Ordinal);
        Assert.Contains("if (option is null)", editor, StringComparison.Ordinal);
        Assert.Contains("g?.GroupLabel ?? string.Empty", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_Construction_GeometryFailure_DoesNotBreakOpen()
    {
        var editor = ReadProjectFile(
            "Workspace1C", "Views", "UtilitiesMenuEditorWindow.Avalonia.cs");
        var method = Slice(editor, "private void ApplyWorkAreaLimits()", "private Control BuildLayout()");

        Assert.Contains("try", method, StringComparison.Ordinal);
        Assert.Contains("catch", method, StringComparison.Ordinal);
        Assert.Contains("Screens.ScreenFromWindow(this)", method, StringComparison.Ordinal);
    }

    // ===================== 5. Локализация =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasOpenFailedKeys(string fileName)
    {
        var strings = LoadLanguageStrings(fileName);

        Assert.True(strings.TryGetValue("Utilities.Editor.OpenFailed", out var message));
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("{0}", message, StringComparison.Ordinal);
        Assert.NotEqual("Utilities.Editor.OpenFailed", message);

        Assert.True(strings.TryGetValue("Utilities.Editor.OpenFailedTitle", out var title));
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.NotEqual("Utilities.Editor.OpenFailedTitle", title);
    }

    [Fact]
    public void Localization_OpenFailedKeysMatch()
    {
        static string[] Keys(string fileName) =>
            LoadLanguageStrings(fileName).Keys
                .Where(k => k.StartsWith("Utilities.Editor.OpenFailed", StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

        var ru = Keys("ru.json");
        var en = Keys("en.json");

        Assert.Contains("Utilities.Editor.OpenFailed", ru);
        Assert.Contains("Utilities.Editor.OpenFailedTitle", ru);
        Assert.Equal(ru, en);
    }

    // ===================== Вспомогательные методы =====================

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return text.Substring(start, end - start);
    }

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
