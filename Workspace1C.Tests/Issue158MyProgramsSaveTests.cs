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
/// Issue #158 «Linux — мои программы», часть 2 (v1.0.1.121): «После ошибки — окно правки
/// не закрывается вообще».
/// <list type="bullet">
/// <item><see cref="CustomProgramsViewModel.TrySave"/> не пробрасывает исключение записи
/// (в том числе <see cref="IOException"/> на Linux), пишет его в
/// <see cref="IAppLogger"/> и заполняет <see cref="CustomProgramsViewModel.LastSaveError"/>;</item>
/// <item><c>ModalWindowBase.OnClosing</c> не делает <c>throw</c> — исключение в цепочке
/// закрытия больше не «залипает» окно на экране;</item>
/// <item>WPF: «Сохранить» при ошибке остаётся в окне, «Отмена» закрывает через
/// <c>Close()</c> (без <c>IsCancel</c>/<c>DialogResult</c>), Esc обрабатывается явно;</item>
/// <item>Avalonia: «Отмена» закрывает через <c>Close()</c>, сохранение закрывает окно
/// только при успехе;</item>
/// <item>локализация ru/en: ключи <c>Launch.CustomPrograms.SaveFailed</c> и
/// <c>Launch.CustomPrograms.SaveFailedTitle</c>.</item>
/// </list>
/// </summary>
public sealed class Issue158MyProgramsSaveTests : IDisposable
{
    private readonly string _tempDir;

    public Issue158MyProgramsSaveTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_issue158_save_" + Guid.NewGuid().ToString("N"));
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

    // ===================== 1. TrySave: исключение не выходит наружу =====================

    [Fact]
    public void TrySave_StoreThrows_ReturnsFalse_WithoutException_AndReportsError()
    {
        var logger = new CapturingLogger();
        var vm = new CustomProgramsViewModel(new ThrowingStore(), CustomProgramsViewModes.List, logger);
        vm.AddProgram();
        vm.Programs[0].Name = "mc";
        vm.Programs[0].FilePath = "/usr/bin/mc";

        var saved = true;
        string error = string.Empty;
        var exception = Record.Exception(() => saved = vm.TrySave(out error));

        Assert.Null(exception);              // исключение записи не выходит наружу
        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(error));
        // В тестовой среде словарь локализации может не загружаться (T вернёт ключ),
        // поэтому принимаем и подставленный текст исключения, и сам ключ.
        Assert.True(
            error.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Launch.CustomPrograms.SaveFailed", StringComparison.Ordinal),
            $"Неожиданный текст ошибки: {error}");
        Assert.Equal(error, vm.LastSaveError);
    }

    [Fact]
    public void TrySave_StoreThrows_LogsThroughIAppLogger()
    {
        var logger = new CapturingLogger();
        var vm = new CustomProgramsViewModel(new ThrowingStore(), CustomProgramsViewModes.List, logger);
        vm.AddProgram();
        vm.Programs[0].Name = "mc";
        vm.Programs[0].FilePath = "/usr/bin/mc";

        Assert.False(vm.TrySave(out _));

        var logged = Assert.Single(logger.Errors);
        Assert.Contains("custom-programs", logged, StringComparison.Ordinal);
        Assert.Contains("disk full", logged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrySave_LoggerThrows_DoesNotEscape()
    {
        // Журнал может быть недоступен — сохранение всё равно не бросает исключение.
        var logger = new CapturingLogger { ThrowOnError = true };
        var vm = new CustomProgramsViewModel(new ThrowingStore(), CustomProgramsViewModes.List, logger);
        vm.AddProgram();
        vm.Programs[0].Name = "mc";
        vm.Programs[0].FilePath = "/usr/bin/mc";

        var saved = true;
        string error = string.Empty;
        var exception = Record.Exception(() => saved = vm.TrySave(out error));
        Assert.Null(exception);
        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TrySave_ValidPrograms_ReturnsTrue_AndClearsLastSaveError()
    {
        var store = new CustomProgramsStore(directoryOverride: _tempDir);
        var vm = new CustomProgramsViewModel(store, CustomProgramsViewModes.List);
        vm.AddProgram();
        vm.Programs[0].Name = "mc";
        vm.Programs[0].FilePath = "/usr/bin/mc";

        Assert.True(vm.TrySave(out var error));
        Assert.Equal(string.Empty, error);
        Assert.Null(vm.LastSaveError);
        Assert.Single(new CustomProgramsViewModel(store, CustomProgramsViewModes.List).Programs);
    }

    // ===================== 2. OnClosing без throw =====================

    [Fact]
    public void ModalWindowBase_OnClosing_DoesNotRethrow()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "ModalWindowBase.cs");
        var onClosing = Slice(source,
            "protected override void OnClosing(WindowClosingEventArgs e)",
            "private void AddResizeZones(Grid root)");

        Assert.DoesNotContain("throw;", onClosing, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = false;", onClosing, StringComparison.Ordinal);
        Assert.Contains("LogWindowConstructionError", onClosing, StringComparison.Ordinal);
    }

    // ===================== 3. WPF: сохранение и «Отмена» =====================

    [Fact]
    public void WpfOk_DoesNotClose_OnSaveError()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        var onOk = Slice(source,
            "private void OnOk_Click(object sender, RoutedEventArgs e)",
            "private void OnCancel_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("_vm.TrySave(out var error)", onOk, StringComparison.Ordinal);
        Assert.Contains("ShowSaveError(error);", onOk, StringComparison.Ordinal);

        // При ошибке — выход до закрытия окна.
        var ret = onOk.IndexOf("return;", StringComparison.Ordinal);
        var close = onOk.IndexOf("CloseWithResult(true);", StringComparison.Ordinal);
        Assert.True(ret > 0 && close > ret, "Окно не должно закрываться при ошибке сохранения");
    }

    [Fact]
    public void WpfCloseWithResult_GuardsInvalidOperationException()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        var helper = Slice(source,
            "private void CloseWithResult(bool result)",
            "private void ShowSaveError(string error)");

        Assert.Contains("DialogResult = result;", helper, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", helper, StringComparison.Ordinal);
        Assert.Contains("Close();", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfCancel_ClosesWithoutDialogResult()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        var onCancel = Slice(source,
            "private void OnCancel_Click(object sender, RoutedEventArgs e)",
            "private void OnWindowKeyDown(object sender, KeyEventArgs e)");

        Assert.Contains("Close();", onCancel, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogResult", onCancel, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfXaml_CancelButton_IsNotIsCancel_AndEscHandled()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml");

        Assert.DoesNotContain("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCancel_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnWindowKeyDown\"", xaml, StringComparison.Ordinal);
    }

    // ===================== 4. Avalonia: сохранение и «Отмена» =====================

    [Fact]
    public void AvaloniaCancel_ClosesWindow()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("cancel.Click += (_, _) => Close();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaSaveAndClose_ReturnsOnSaveError()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        var save = Slice(source,
            "private void SaveAndClose()",
            "private void ShowSaveError(string error)");

        Assert.Contains("_vm.TrySave(out var error)", save, StringComparison.Ordinal);
        Assert.Contains("ShowSaveError(error);", save, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", save, StringComparison.Ordinal);

        var ret = save.IndexOf("return;", StringComparison.Ordinal);
        var close = save.IndexOf("Close();", StringComparison.Ordinal);
        Assert.True(ret > 0 && close > ret, "Окно не должно закрываться при ошибке сохранения");
    }

    // ===================== 5. Локализация ru/en =====================

    [Theory]
    [InlineData("ru.json")]
    [InlineData("en.json")]
    public void Localization_HasSaveFailedKeys(string fileName)
    {
        var strings = LoadLanguageStrings(fileName);

        Assert.True(strings.TryGetValue("Launch.CustomPrograms.SaveFailed", out var failed));
        Assert.False(string.IsNullOrWhiteSpace(failed));
        Assert.NotEqual("Launch.CustomPrograms.SaveFailed", failed);
        Assert.Contains("{0}", failed, StringComparison.Ordinal);
        Assert.Contains("\n", failed, StringComparison.Ordinal);

        Assert.True(strings.TryGetValue("Launch.CustomPrograms.SaveFailedTitle", out var title));
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.NotEqual("Launch.CustomPrograms.SaveFailedTitle", title);
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

        Assert.Contains("Launch.CustomPrograms.SaveFailed", ru);
        Assert.Contains("Launch.CustomPrograms.SaveFailedTitle", ru);
        Assert.Equal(ru, en);
    }

    [Fact]
    public void WpfAndAvalonia_UseSameSaveFailedKeys()
    {
        // Текст ошибки формирует общий VM, окна обеих платформ показывают один заголовок.
        var vm = ReadProjectFile("Workspace1C", "ViewModels", "CustomProgramsViewModel.cs");
        Assert.Contains("\"Launch.CustomPrograms.SaveFailed\"", vm, StringComparison.Ordinal);

        var wpf = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        Assert.Contains("\"Launch.CustomPrograms.SaveFailedTitle\"", wpf, StringComparison.Ordinal);
        Assert.Contains("\"Launch.CustomPrograms.SaveFailedTitle\"", avalonia, StringComparison.Ordinal);
    }

    // ===================== Заглушки =====================

    private sealed class ThrowingStore : ICustomProgramsStore
    {
        public string FilePath => Path.Combine(Path.GetTempPath(), "custom_programs.json");

        public IReadOnlyList<CustomProgram> LoadAll() => Array.Empty<CustomProgram>();

        public void Save(CustomProgram program) => throw new IOException("disk full");

        public void SaveAll(IEnumerable<CustomProgram> programs) => throw new IOException("disk full");

        public void Delete(string id) => throw new IOException("disk full");

        public CustomProgram? Get(string id) => null;
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Errors { get; } = new();

        public bool ThrowOnError { get; init; }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null)
        {
            if (ThrowOnError)
                throw new InvalidOperationException("logger failure");
            Errors.Add(exception is null ? message : $"{message} | {exception.Message}");
        }
    }

    // ===================== Вспомогательные методы =====================

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
