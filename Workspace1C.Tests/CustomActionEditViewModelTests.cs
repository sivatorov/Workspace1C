using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты формы редактирования пользовательского действия (0.3.9.195, функция 7):
/// валидация полей, перенос полей через <see cref="CustomActionEditViewModel.ApplyTo"/>
/// с сохранением Id, список доступных токенов и построение примера командной строки.
/// </summary>
public sealed class CustomActionEditViewModelTests
{
    private static Infobase SampleBase() => new()
    {
        Id = "base-1",
        Name = "Бухгалтерия (тест)",
        Group = "Основные",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv-1c",
            DatabaseName = "бухгалтерия",
            Password = "pass"
        }
    };

    private static CustomAction Draft(string name = "Архивировать", string command = "echo {ИмяБазы} {Пароль}") => new()
    {
        Name = name,
        Command = command,
        Scope = CustomActionScope.Both,
        Shell = ScriptShell.Cmd,
        SupportsBatch = true,
        RunWithoutConfirm = true,
        EscapeValues = false,
        TimeoutMs = 60_000,
        Hotkey = "F8",
        WorkingDirectory = @"C:\tools"
    };

    [Fact]
    public void Validate_EmptyName_ReturnsNameRequiredKey()
    {
        var vm = new CustomActionEditViewModel(null) { Name = "  ", Command = "echo x", TimeoutSeconds = 30 };

        Assert.Equal("CustomAction.NameRequired", vm.Validate());
    }

    [Fact]
    public void Validate_EmptyCommand_ReturnsCommandRequiredKey()
    {
        var vm = new CustomActionEditViewModel(null) { Name = "Действие", Command = "", TimeoutSeconds = 30 };

        Assert.Equal("CustomAction.CommandRequired", vm.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(601)]
    [InlineData(700)]
    public void Validate_TimeoutOutOfRange_ReturnsTimeoutInvalidKey(int seconds)
    {
        var vm = new CustomActionEditViewModel(null) { Name = "Действие", Command = "echo x", TimeoutSeconds = seconds };

        Assert.Equal("CustomAction.TimeoutInvalid", vm.Validate());
    }

    [Fact]
    public void Validate_ValidFields_ReturnsNull()
    {
        var vm = new CustomActionEditViewModel(null)
        {
            Name = "Действие",
            Command = "echo {ИмяБазы}",
            TimeoutSeconds = 30
        };

        Assert.Null(vm.Validate());
    }

    [Fact]
    public void ApplyTo_TransfersAllFields_PreservesId()
    {
        var action = new CustomAction { Id = "existing-id", Name = "Старое", Command = "old" };
        var vm = new CustomActionEditViewModel(null)
        {
            Name = "  Новое действие  ",
            Command = "echo {ИмяБазы}",
            SelectedScope = CustomActionScope.Group,
            SelectedShell = ScriptShell.PowerShell,
            SupportsBatch = true,
            RunWithoutConfirm = true,
            EscapeValues = true,
            TimeoutSeconds = 120,
            Hotkey = " Ctrl+F9 ",
            WorkingDirectory = @"  C:\work  "
        };

        vm.ApplyTo(action);

        // Id НЕ меняется (перезапись по тому же Id в хранилище).
        Assert.Equal("existing-id", action.Id);
        Assert.Equal("Новое действие", action.Name);
        Assert.Equal("echo {ИмяБазы}", action.Command);
        Assert.Equal(CustomActionScope.Group, action.Scope);
        Assert.Equal(ScriptShell.PowerShell, action.Shell);
        Assert.True(action.SupportsBatch);
        Assert.True(action.RunWithoutConfirm);
        Assert.True(action.EscapeValues);
        Assert.Equal(120_000, action.TimeoutMs); // секунды → миллисекунды
        Assert.Equal("Ctrl+F9", action.Hotkey);
        Assert.Equal(@"C:\work", action.WorkingDirectory);
    }

    [Fact]
    public void Constructor_FromAction_FillsFieldsAndConvertsTimeout()
    {
        var vm = new CustomActionEditViewModel(Draft());

        Assert.Equal("Архивировать", vm.Name);
        Assert.Equal("echo {ИмяБазы} {Пароль}", vm.Command);
        Assert.Equal(CustomActionScope.Both, vm.SelectedScope);
        Assert.Equal(ScriptShell.Cmd, vm.SelectedShell);
        Assert.True(vm.SupportsBatch);
        Assert.True(vm.RunWithoutConfirm);
        Assert.False(vm.EscapeValues);
        Assert.Equal(60, vm.TimeoutSeconds); // 60000 мс → 60 с
        Assert.Equal("F8", vm.Hotkey);
        Assert.Equal(@"C:\tools", vm.WorkingDirectory);
    }

    [Fact]
    public void AvailableTokens_ContainsCurlyBraceTokens()
    {
        var tokens = CustomActionEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "{ИмяБазы}");
        Assert.Contains(tokens, t => t.Token == "{СтрокаПодключения}");
        Assert.Contains(tokens, t => t.Token == "{Пароль}");
        Assert.Contains(tokens, t => t.Token == "{Дата}");
        // У каждого токена есть ключ локализации описания.
        Assert.All(tokens, t => Assert.False(string.IsNullOrEmpty(t.LocalizationKey)));
    }

    [Fact]
    public void BuildExampleCommandLine_BuildsShellWrappedCommandForExampleBase()
    {
        var draft = Draft();
        draft.Shell = ScriptShell.Cmd;
        draft.EscapeValues = false;

        var commandLine = CustomActionEditViewModel.BuildExampleCommandLine(draft, SampleBase());

        Assert.StartsWith("cmd.exe /c ", commandLine);
        Assert.Contains("Бухгалтерия (тест)", commandLine);
        Assert.Contains("pass", commandLine);
    }

    // ---------- Валидация конфликтов горячих клавиш (0.3.9.198) ----------

    [Fact]
    public void Validate_OccupiedHotkey_ReturnsHotkeyConflictKey()
    {
        var others = new[]
        {
            new CustomAction { Name = "Чужое", Hotkey = "F8" },
            new CustomAction { Name = "Ещё одно", Hotkey = "Ctrl+Alt+F8" }
        };
        var vm = new CustomActionEditViewModel(null, others)
        {
            Name = "Действие",
            Command = "echo x",
            TimeoutSeconds = 30,
            Hotkey = "F8"
        };

        Assert.Equal("CustomAction.HotkeyConflict", vm.Validate());
    }

    [Fact]
    public void Validate_OccupiedHotkey_CaseInsensitiveAndTrimmed()
    {
        // « f8 » нормализуется (Trim) и сравнивается без учёта регистра с занятым «F8».
        var others = new[] { new CustomAction { Name = "Чужое", Hotkey = "F8" } };
        var vm = new CustomActionEditViewModel(null, others)
        {
            Name = "Действие",
            Command = "echo x",
            TimeoutSeconds = 30,
            Hotkey = " f8 "
        };

        Assert.Equal("CustomAction.HotkeyConflict", vm.Validate());
    }

    [Fact]
    public void Validate_OwnHotkeyWhenEditing_IsAllowed()
    {
        // Свой хоткей редактируемого действия (сравнение по Id) конфликтом не считается,
        // даже если он есть в списке существующих действий. Сочетание выбрано заведомо
        // не системным: при полном прогоне тестов другой тест инициализирует AppServices
        // реальными настройками профиля, где «F8» может быть занят системно.
        var existing = new CustomAction { Id = "self-id", Name = "Моё", Command = "echo a", Hotkey = "Ctrl+Shift+Alt+F12" };
        var other = new CustomAction { Id = "other-id", Name = "Чужое", Command = "echo b", Hotkey = "Ctrl+F9" };

        var vm = new CustomActionEditViewModel(existing, new[] { existing, other })
        {
            Name = "Моё",
            Command = "echo a",
            TimeoutSeconds = 30,
            Hotkey = "Ctrl+Shift+Alt+F12" // совпадает только с самим собой
        };

        Assert.Null(vm.Validate());
    }

    [Fact]
    public void Validate_HotkeyOfAnotherAction_StillConflictsWhenEditing()
    {
        var existing = new CustomAction { Id = "self-id", Name = "Моё", Command = "echo a", Hotkey = "F8" };
        var other = new CustomAction { Id = "other-id", Name = "Чужое", Command = "echo b", Hotkey = "F9" };

        // Меняем хоткей на сочетание, занятое ДРУГИМ действием (не своим).
        var vm = new CustomActionEditViewModel(existing, new[] { existing, other })
        {
            Name = "Моё",
            Command = "echo a",
            TimeoutSeconds = 30,
            Hotkey = "F9"
        };

        Assert.Equal("CustomAction.HotkeyConflict", vm.Validate());
    }

    [Fact]
    public void Validate_EmptyHotkey_IsAllowedEvenIfReserved()
    {
        var others = new[] { new CustomAction { Name = "Чужое", Hotkey = "F5" } };
        var vm = new CustomActionEditViewModel(null, others)
        {
            Name = "Действие",
            Command = "echo x",
            TimeoutSeconds = 30,
            Hotkey = ""
        };

        Assert.Null(vm.Validate());
    }

    [Fact]
    public void GetKnownSystemHotkeys_IncludesHardcodedRunScriptHotkey()
    {
        // Жёсткое системное сочетание «Выполнить скрипт» (F5) входит в список всегда,
        // даже если настройки профиля недоступны (в тестовом контексте AppServices пуст).
        Assert.Contains(
            CustomActionEditViewModel.GetKnownSystemHotkeys(),
            h => h.Equals("F5", StringComparison.OrdinalIgnoreCase));
    }
}