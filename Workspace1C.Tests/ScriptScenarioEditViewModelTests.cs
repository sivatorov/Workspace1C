using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты формы редактирования сценария запуска скрипта
/// (<see cref="ScriptScenarioEditViewModel"/>, issue #308): перенос полей через
/// <see cref="ScriptScenarioEditViewModel.ApplyTo"/> с сохранением Id (редактирование
/// не плодит копию), генерация Id у нового сценария и перенос свойства
/// <see cref="ScriptScenario.HideWindow"/>.
/// </summary>
[Collection("ScriptLaunchContextState")]
public sealed class ScriptScenarioEditViewModelTests
{
    [Fact]
    public void ApplyTo_ExistingScenario_PreservesIdAndUpdatesFields()
    {
        var scenario = new ScriptScenario
        {
            Id = "existing-id",
            Name = "Старое имя",
            FilePath = "old.bat",
            Parameters = new List<string> { "%name%" },
            HideWindow = true
        };
        var vm = new ScriptScenarioEditViewModel(scenario);

        // Пользователь правит поля формы.
        vm.Name = "  Новое имя  ";
        vm.FilePath = "new.bat";
        vm.ParametersText = "/S:%connection.server%\n%connection.password%";
        vm.HideWindow = false;

        vm.ApplyTo(scenario);

        // Id НЕ меняется (issue #308: перезапись без дублей).
        Assert.Equal("existing-id", scenario.Id);
        Assert.Equal("Новое имя", scenario.Name);
        Assert.Equal("new.bat", scenario.FilePath);
        Assert.Equal(new[] { "/S:%connection.server%", "%connection.password%" }, scenario.Parameters);
        Assert.False(scenario.HideWindow);
    }

    [Fact]
    public void ApplyTo_TransfersWorkingDirectory()
    {
        // Issue #308, п.7: «Папка запуска» переносится из формы в сценарий.
        var scenario = new ScriptScenario { Id = "wd-id", Name = "Имя", FilePath = "tool.bat" };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.WorkingDirectory = @"C:\reports\output";

        vm.ApplyTo(scenario);

        Assert.Equal(@"C:\reports\output", scenario.WorkingDirectory);
    }

    [Fact]
    public void Constructor_FromScenario_FillsWorkingDirectory()
    {
        var scenario = new ScriptScenario
        {
            Name = "С папкой",
            FilePath = "x.bat",
            WorkingDirectory = @"/opt/scripts"
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.Equal(@"/opt/scripts", vm.WorkingDirectory);
    }

    [Fact]
    public void Constructor_NullScenario_WorkingDirectoryDefaultsToEmpty()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.Equal("", vm.WorkingDirectory);
    }

    [Fact]
    public void ApplyTo_NewScenario_AssignsId()
    {
        var created = new ScriptScenario();
        var vm = new ScriptScenarioEditViewModel(null);
        vm.Name = "Новый";
        vm.FilePath = "tool.bat";
        vm.ParametersText = "%name%";
        vm.HideWindow = false;

        vm.ApplyTo(created);

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Новый", created.Name);
        Assert.Equal("tool.bat", created.FilePath);
        Assert.Equal(new[] { "%name%" }, created.Parameters);
        Assert.False(created.HideWindow);
    }

    [Fact]
    public void Constructor_FromScenario_FillsHideWindow()
    {
        var scenario = new ScriptScenario
        {
            Name = "Скрытый",
            FilePath = "x.bat",
            HideWindow = false
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.False(vm.HideWindow);
        Assert.Equal("Скрытый", vm.Name);
    }

    [Fact]
    public void Constructor_NullScenario_HideWindowDefaultsToTrue()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.True(vm.HideWindow);
    }

    [Fact]
    public void ApplyTo_EmptyParametersText_ClearsParameters()
    {
        var scenario = new ScriptScenario
        {
            Name = "Имя",
            FilePath = "tool.bat",
            Parameters = new List<string> { "%name%", "old" }
        };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.ParametersText = "";

        vm.ApplyTo(scenario);

        Assert.Empty(scenario.Parameters);
    }

    [Fact]
    public void Validate_RequiresNameAndFilePath()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.Equal("Script.NameRequired", vm.Validate());

        vm.Name = "Имя";
        Assert.Equal("Script.FilePathRequired", vm.Validate());

        vm.FilePath = "tool.bat";
        Assert.Null(vm.Validate());
    }

    [Fact]
    public void AvailableTokens_ContainPasswordTokens()
    {
        // Локализация новых токенов пароля (issue #308).
        var tokens = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => t.Token)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("%connection.password%", tokens);
        Assert.Contains("%password%", tokens);
    }

    [Fact]
    public void ApplyTo_TransfersShell()
    {
        // Issue #308, п.9: «Интерпретатор» переносится из формы в сценарий.
        var scenario = new ScriptScenario { Id = "shell-id", Name = "Имя", FilePath = "x.ps1" };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.Shell = ScriptShell.PowerShell;

        vm.ApplyTo(scenario);

        Assert.Equal(ScriptShell.PowerShell, scenario.Shell);
    }

    [Fact]
    public void Constructor_FromScenario_FillsShell()
    {
        var scenario = new ScriptScenario
        {
            Name = "С шеллом",
            FilePath = "x.sh",
            Shell = ScriptShell.Sh
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.Equal(ScriptShell.Sh, vm.Shell);
    }

    [Fact]
    public void Constructor_NullScenario_ShellDefaultsToAuto()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.Equal(ScriptShell.Auto, vm.Shell);
    }

    [Fact]
    public void ShellOptions_ContainAllFourValues()
    {
        // Варианты выпадающего списка «Интерпретатор» (issue #308, п.9).
        Assert.Equal(
            new[] { ScriptShell.Auto, ScriptShell.Cmd, ScriptShell.PowerShell, ScriptShell.Sh },
            ScriptScenarioEditViewModel.ShellOptions);
    }

    [Fact]
    public void ApplyTo_TransfersNotifyOnStartAndKeepOpen()
    {
        // Issue #308: галочки «Уведомлять о запуске» и «Не закрывать» переносятся
        // из формы в сценарий; флаг уведомления управляет показом окна «скрипт
        // запущен» и системных уведомлений при выполнении (MainViewModel.RunScriptAsync).
        var scenario = new ScriptScenario { Id = "flags-id", Name = "Имя", FilePath = "x.bat" };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.NotifyOnStart = true;
        vm.KeepOpen = true;

        vm.ApplyTo(scenario);

        Assert.True(scenario.NotifyOnStart);
        Assert.True(scenario.KeepOpen);
    }

    [Fact]
    public void Constructor_FromScenario_FillsNotifyOnStartAndKeepOpen()
    {
        var scenario = new ScriptScenario
        {
            Name = "С флагами",
            FilePath = "x.bat",
            NotifyOnStart = true,
            KeepOpen = true
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.True(vm.NotifyOnStart);
        Assert.True(vm.KeepOpen);
    }

    [Fact]
    public void Constructor_NullScenario_FlagsDefaultToOff()
    {
        // «Без опций — просто не надо» (issue #308): новый сценарий не уведомляет
        // о запуске и не удерживает окно, пока пользователь не включит галочки.
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.False(vm.NotifyOnStart);
        Assert.False(vm.KeepOpen);
    }

    [Fact]
    public void AvailableTokens_ContainsAdditionalPlaceholderExample()
    {
        // Пример подстановки произвольного поля базы (issue #6): %Additional_1% —
        // значение первой строки таблицы «Произвольные поля» вкладки «Идентификатор».
        Assert.Contains(ScriptScenarioEditViewModel.AvailableTokens,
            t => t.Token == "%Additional_1%");
    }

    [Fact]
    public void BuildExampleCommandLine_PassedLaunchContext_DoesNotCallResolver()
    {
        // Issue #67 (производительность): окно редактора вычисляет контекст платформы один
        // раз на выбранную «Базу для примера» и передаёт его в каждый пересчёт превью —
        // резолвер НЕ вызывается на каждое нажатие клавиши (здесь он запрещён тестом).
        var scenario = new ScriptScenario
        {
            Name = "Имя",
            FilePath = "tool.bat",
            Parameters = new List<string> { "%full_exe_name%" }
        };
        var infobase = new Infobase { Name = "База", Id = "id" };
        var context = new ScriptLaunchContext(
            ExePath: @"C:\1cv8\8.3.3.1\bin\1cv8.exe",
            PlatformVersion: "8.3.3.1",
            EnterpriseCommandLine: "ENTERPRISE",
            ConfiguratorCommandLine: "DESIGNER");

        var previous = ScriptLaunchContextResolver.ResolverOverride;
        ScriptLaunchContextResolver.ResolverOverride = _ =>
            throw new InvalidOperationException("Резолвер не должен вызываться при переданном контексте (кэш окна, issue #67).");
        try
        {
            var result = ScriptScenarioEditViewModel.BuildExampleCommandLine(scenario, infobase, launchContext: context);

            Assert.Contains(@"C:\1cv8\8.3.3.1\bin\1cv8.exe", result);
            Assert.DoesNotContain("%full_exe_name%", result);
        }
        finally
        {
            ScriptLaunchContextResolver.ResolverOverride = previous;
        }
    }
}