using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты формы задания по расписанию (issue #372): поиск пункта списка по значению
/// для установки <c>SelectedItem</c> (закрытый ModernComboBox показывал ключ вместо названия).
/// </summary>
public sealed class ScheduledTaskEditViewModelTests
{
    private static IReadOnlyList<ScheduledTaskOption> Options() => new[]
    {
        new ScheduledTaskOption("Backup", "Резервная копия"),
        new ScheduledTaskOption("UpdateConfig", "Обновление конфигурации"),
        new ScheduledTaskOption("CheckIntegrity", "Проверка целостности"),
    };

    [Fact]
    public void FindOption_KnownValue_ReturnsOption()
    {
        var option = ScheduledTaskEditViewModel.FindOption(Options(), "UpdateConfig");

        Assert.NotNull(option);
        Assert.Equal("UpdateConfig", option!.Value);
        Assert.Equal("Обновление конфигурации", option.Text);
    }

    [Fact]
    public void FindOption_UnknownValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "Unknown"));
    }

    [Fact]
    public void FindOption_NullOrEmptyValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), null));
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), string.Empty));
    }

    [Fact]
    public void FindOption_NullOptions_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(null, "Backup"));
    }

    [Fact]
    public void FindOption_IsCaseSensitiveByValue()
    {
        // Значения — стабильные ключи (Backup), регистр учитывается.
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "backup"));
    }

    // ---- Кэширование списков опций (issue #372) ----

    [Fact]
    public void KindOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        // Списки опций должны возвращать одни и те же экземпляры при каждом обращении:
        // иначе SelectedItem (из FindOption) не совпадает с ItemsSource, и закрытый
        // комбобокс показывает ключ вместо названия.
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.KindOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.KindOptions, "Backup");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void NotifyOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.NotifyOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.NotifyOptions, "MainWindow");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void RepeatUnitOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.RepeatUnitOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.RepeatUnitOptions, "Minutes");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void ScenarioOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var scenarios = new List<BackupScenario> { new() { Id = "s1", Name = "Сценарий 1" } };
        var vm = new ScheduledTaskEditViewModel(null, scenarios, new List<Infobase>());

        var options = vm.ScenarioOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.ScenarioOptions, "s1");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void InfobaseOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var bases = new List<Infobase> { new() { Id = "b1", Name = "База 1" } };
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), bases);

        var options = vm.InfobaseOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.InfobaseOptions, "b1");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    // ---- Панель повтора с интервалом (issue #5) ----

    [Theory]
    [InlineData(ScheduledTaskKind.UpdateApp)]
    [InlineData(ScheduledTaskKind.PlatformUpdateCheck)]
    [InlineData(ScheduledTaskKind.ConfigurationUpdateCheck)]
    public void NeedsRepeat_True_ForRepeatableKinds(ScheduledTaskKind kind)
    {
        // issue #5: «Обновить приложение» получает повтор, как задания-проверки (#366/#367).
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());
        vm.Kind = kind;

        Assert.True(vm.NeedsRepeat);
    }

    [Theory]
    [InlineData(ScheduledTaskKind.Backup)]
    [InlineData(ScheduledTaskKind.UpdateConfig)]
    [InlineData(ScheduledTaskKind.BackupThenUpdateConfig)]
    [InlineData(ScheduledTaskKind.CheckIntegrity)]
    public void NeedsRepeat_False_ForNonRepeatableKinds(ScheduledTaskKind kind)
    {
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());
        vm.Kind = kind;

        Assert.False(vm.NeedsRepeat);
    }

    // ---- Гейт ПРО-типов заданий (дефект №5 аудита активации) ----

    /// <summary>
    /// Дефект №5 (аудит активации): из типов заданий планировщика только TestDeploy —
    /// функция редакции ПРО (выполняется при лицензии, гейт в SchedulerService.cs).
    /// Гейт выбора/сохранения в окне задания строится на этом маппинге.
    /// </summary>
    [Fact]
    public void RequiredProFeature_TestDeployKind_ReturnsTestDeployFeature()
    {
        Assert.Equal(ProFeature.TestDeploy,
            ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy));
    }

    [Theory]
    [InlineData(ScheduledTaskKind.Backup)]
    [InlineData(ScheduledTaskKind.UpdateConfig)]
    [InlineData(ScheduledTaskKind.BackupThenUpdateConfig)]
    [InlineData(ScheduledTaskKind.CheckIntegrity)]
    [InlineData(ScheduledTaskKind.UpdateApp)]
    [InlineData(ScheduledTaskKind.PlatformUpdateCheck)]
    [InlineData(ScheduledTaskKind.ConfigurationUpdateCheck)]
    [InlineData(ScheduledTaskKind.InstallExtension)]
    [InlineData(ScheduledTaskKind.GitCommitConfig)]
    [InlineData(ScheduledTaskKind.DbmsBackup)]
    [InlineData(ScheduledTaskKind.SettingsSync)]
    public void RequiredProFeature_CoreKinds_ReturnNull(ScheduledTaskKind kind)
    {
        // MassLoadCf — ПРО-функция, но это команда контекстного меню/утилит,
        // а не тип задания планировщика: ПРО-тип задания один — TestDeploy.
        Assert.Null(ScheduledTaskEditViewModel.RequiredProFeature(kind));
    }
}
