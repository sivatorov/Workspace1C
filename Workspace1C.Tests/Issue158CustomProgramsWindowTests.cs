using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #158 «Linux — мои программы», доработка v1.0.1.123:
/// <list type="bullet">
/// <item><b>Причина «окно не закрывается»</b>: шаблон строки списка
/// (<c>BuildListItem</c>) обращался к <c>program.Name</c> без проверки на <c>null</c>.
/// <c>ContentPresenter</c> строит строку и для пустого содержимого (удаление записи,
/// пересоздание контейнера, закрытие окна) — <c>NullReferenceException</c> прерывал
/// закрытие, и редактор «залипал» на экране. Теперь шаблон null-безопасен.</item>
/// <item><b>Список обновляется сразу</b>: имя в строке привязано к модели
/// (<c>CustomProgram</c> реализует <see cref="INotifyPropertyChanged"/>), а не
/// скопировано в <c>Text</c>.</item>
/// <item><b>Одно окно редактора</b>: <see cref="CustomProgramsEditorGuard"/> не даёт
/// открыть второй экземпляр; слот освобождается в <c>finally</c> и <c>Closed</c>.</item>
/// <item><b>Удаление без ошибок</b>: <see cref="CustomProgramsViewModel.DeleteSelected"/>
/// не пробрасывает исключение наружу, пишет его в журнал и оставляет окно рабочим.</item>
/// </list>
/// </summary>
public sealed class Issue158CustomProgramsWindowTests
{
    // ===================== 1. INotifyPropertyChanged модели =====================

    [Fact]
    public void CustomProgram_RaisesPropertyChanged_ForEditableFields()
    {
        var program = new CustomProgram();
        var names = new List<string?>();
        program.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        program.Name = "mc";
        program.FilePath = "/usr/bin/mc";
        program.Arguments = "--help";
        program.WorkingDirectory = "/tmp";
        program.IconPath = "/tmp/icon.png";
        program.UseCustomIcon = false;
        program.Order = 3;

        Assert.Contains(nameof(CustomProgram.Name), names);
        Assert.Contains(nameof(CustomProgram.FilePath), names);
        Assert.Contains(nameof(CustomProgram.Arguments), names);
        Assert.Contains(nameof(CustomProgram.WorkingDirectory), names);
        Assert.Contains(nameof(CustomProgram.IconPath), names);
        Assert.Contains(nameof(CustomProgram.UseCustomIcon), names);
        Assert.Contains(nameof(CustomProgram.Order), names);
    }

    [Fact]
    public void CustomProgram_NameChange_RaisesOnlyOnActualChange()
    {
        var program = new CustomProgram { Name = "mc" };
        var count = 0;
        program.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CustomProgram.Name))
                count++;
        };

        program.Name = "mc";
        Assert.Equal(0, count);

        program.Name = "htop";
        Assert.Equal(1, count);
    }

    // ===================== 2. Guard одного окна =====================

    [Fact]
    public void EditorGuard_AllowsOnlyOneOpenWindow()
    {
        try
        {
            CustomProgramsEditorGuard.ResetForTests();
            Assert.False(CustomProgramsEditorGuard.IsOpen);

            Assert.True(CustomProgramsEditorGuard.TryEnter());
            Assert.True(CustomProgramsEditorGuard.IsOpen);

            // Повторное открытие отклоняется — второе окно не создаётся.
            Assert.False(CustomProgramsEditorGuard.TryEnter());
            Assert.True(CustomProgramsEditorGuard.IsOpen);

            CustomProgramsEditorGuard.Exit();
            Assert.False(CustomProgramsEditorGuard.IsOpen);

            // После закрытия (в т.ч. после ошибки) редактор снова открывается.
            Assert.True(CustomProgramsEditorGuard.TryEnter());
        }
        finally
        {
            CustomProgramsEditorGuard.ResetForTests();
        }
    }

    [Fact]
    public void EditorGuard_ReleasedInFinally_DoesNotStickAfterError()
    {
        try
        {
            CustomProgramsEditorGuard.ResetForTests();

            Assert.True(CustomProgramsEditorGuard.TryEnter());
            try
            {
                // Имитация ошибки внутри показа окна.
                throw new InvalidOperationException("save failed");
            }
            catch (InvalidOperationException)
            {
                // Окно закрывается штатно, слот освобождается в finally.
            }
            finally
            {
                CustomProgramsEditorGuard.Exit();
            }

            Assert.False(CustomProgramsEditorGuard.IsOpen);
            Assert.True(CustomProgramsEditorGuard.TryEnter());
        }
        finally
        {
            CustomProgramsEditorGuard.ResetForTests();
        }
    }

    // ===================== 3. Удаление без исключений =====================

    [Fact]
    public void DeleteSelected_ListFailure_DoesNotThrow_Logs_AndKeepsEditorUsable()
    {
        var logger = new CapturingLogger();
        var vm = new ThrowingDeleteViewModel(logger);
        vm.AddProgram();
        vm.Programs[0].Name = "mc";
        vm.Programs[0].FilePath = "/usr/bin/mc";

        var deleted = true;
        var exception = Record.Exception(() => deleted = vm.DeleteSelected());

        Assert.Null(exception);          // исключение списка не выходит наружу
        Assert.False(deleted);
        Assert.Single(vm.Programs);      // запись осталась, окно работоспособно
        Assert.False(string.IsNullOrWhiteSpace(vm.LastDeleteError));

        var logged = Assert.Single(logger.Errors);
        Assert.Contains("custom-programs", logged, StringComparison.Ordinal);
        Assert.Contains("collection locked", logged, StringComparison.Ordinal);

        // Редактор продолжает работать: можно добавить и выбрать другую программу.
        vm.AddProgram();
        Assert.Equal(2, vm.Programs.Count);
        Assert.NotNull(vm.SelectedProgram);
    }

    [Fact]
    public void DeleteSelected_RemovesProgram_AndClearsIconReference()
    {
        var vm = new CustomProgramsViewModel(null, CustomProgramsViewModes.List);
        vm.AddProgram();
        var program = vm.Programs[0];
        program.Name = "mc";
        program.FilePath = "/usr/bin/mc";
        program.IconPath = "/tmp/icon.png";
        Assert.True(program.UseCustomIcon);

        Assert.True(vm.DeleteSelected());

        Assert.Empty(vm.Programs);
        Assert.Null(vm.SelectedProgram);
        Assert.Null(vm.LastDeleteError);
        // Ссылка на иконку снята до удаления — файл пользователя не удаляется.
        Assert.Equal(string.Empty, program.IconPath);
        Assert.False(program.UseCustomIcon);
    }

    // ===================== 4. Null-безопасный шаблон и привязка имени =====================

    [Fact]
    public void AvaloniaItemTemplate_IsNullSafe_AndBindsName()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");

        // Причина NRE из скриншота issue: program.Name без проверки на null.
        Assert.Contains("private Control BuildListItem(CustomProgram? program)", source,
            StringComparison.Ordinal);
        Assert.Contains("if (program is null)", source, StringComparison.Ordinal);
        Assert.Contains("return new Panel();", source, StringComparison.Ordinal);

        // Имя привязано к модели — переименование сразу видно в списке.
        Assert.Contains(
            "name.Bind(TextBlock.TextProperty, new Binding(nameof(CustomProgram.Name)));",
            source, StringComparison.Ordinal);
    }

    // ===================== 5. Одно окно: обе точки открытия =====================

    [Theory]
    [InlineData("Workspace1C/Views/CustomProgramsWindow.Avalonia.cs")]
    [InlineData("Workspace1C/Views/CustomProgramsWindow.xaml.cs")]
    public void EditorWindows_UseSingleWindowGuard(string relativePath)
    {
        var source = ReadProjectFile(relativePath.Split('/'));

        Assert.Contains("CustomProgramsEditorGuard.TryEnter()", source, StringComparison.Ordinal);
        Assert.Contains("CustomProgramsEditorGuard.Exit()", source, StringComparison.Ordinal);
        Assert.Contains("public static CustomProgramsWindow ShowEditor(", source,
            StringComparison.Ordinal);
        Assert.Contains("public static bool IsOpen", source, StringComparison.Ordinal);
        Assert.Contains("protected override void OnClosed(EventArgs e)", source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Workspace1C/Views/SettingsWindow.Avalonia.cs")]
    [InlineData("Workspace1C/Views/SettingsWindow.xaml.cs")]
    [InlineData("Workspace1C/ViewModels/MainViewModel.CustomPrograms.Avalonia.cs")]
    [InlineData("Workspace1C/ViewModels/MainViewModel.CustomPrograms.Windows.cs")]
    public void OpenPoints_CallShowEditor_AndDoNotCreateWindowDirectly(string relativePath)
    {
        var source = ReadProjectFile(relativePath.Split('/'));

        Assert.Contains("CustomProgramsWindow.ShowEditor(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new CustomProgramsWindow(", source, StringComparison.Ordinal);
    }

    // ===================== 6. Безопасное закрытие =====================

    [Theory]
    [InlineData("Workspace1C/Views/CustomProgramsWindow.Avalonia.cs")]
    [InlineData("Workspace1C/Views/CustomProgramsWindow.xaml.cs")]
    public void ClosePaths_UseSafeClose(string relativePath)
    {
        var source = ReadProjectFile(relativePath.Split('/'));

        Assert.Contains("private void CloseSafely()", source, StringComparison.Ordinal);
        Assert.Contains("LogWindowError(", source, StringComparison.Ordinal);

        // «Отмена», Esc и закрытие после сохранения не бросают исключение наружу.
        Assert.Contains("CloseSafely();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaSaveAndCancel_CloseSafely_AndDoNotBlockAfterError()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "CustomProgramsWindow.Avalonia.cs");
        var save = Slice(source, "private void SaveAndClose()", "private void ShowSaveError(string error)");

        Assert.Contains("_vm.TrySave(out var error)", save, StringComparison.Ordinal);
        Assert.Contains("ShowSaveError(error);", save, StringComparison.Ordinal);
        Assert.Contains("CloseSafely();", save, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", save, StringComparison.Ordinal);
    }

    // ===================== Заглушки и вспомогательные методы =====================

    private sealed class ThrowingDeleteViewModel : CustomProgramsViewModel
    {
        public ThrowingDeleteViewModel(IAppLogger logger)
            : base(null, CustomProgramsViewModes.List, logger)
        {
        }

        protected override void RemoveProgramAt(int index)
            => throw new InvalidOperationException("collection locked");
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Errors { get; } = new();

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null)
            => Errors.Add(exception is null ? message : $"{message} | {exception.Message}");
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return text.Substring(start, end - start);
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
