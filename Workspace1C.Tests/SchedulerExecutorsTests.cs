using System.Text.Json;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Хук планировщика для ПРО-заданий (2.x): реестр исполнителей, диспетчеризация,
/// привязка видов заданий к функциям ПРО и локализация.
/// </summary>
[Collection("ProLicensing")]
public sealed class SchedulerExecutorsTests
{
    [Fact]
    public void Register_TryGet_Unregister()
    {
        SchedulerExecutors.Unregister(ScheduledTaskKind.Pipeline);
        Assert.False(SchedulerExecutors.IsRegistered(ScheduledTaskKind.Pipeline));
        Assert.False(SchedulerExecutors.TryGet(ScheduledTaskKind.Pipeline, out _));

        SchedulerExecutors.Register(ScheduledTaskKind.Pipeline,
            (_, _) => Task.FromResult<BackupRunResult?>(new BackupRunResult { Success = true }));
        Assert.True(SchedulerExecutors.TryGet(ScheduledTaskKind.Pipeline, out var executor));
        Assert.NotNull(executor);

        SchedulerExecutors.Unregister(ScheduledTaskKind.Pipeline);
        Assert.False(SchedulerExecutors.TryGet(ScheduledTaskKind.Pipeline, out _));
    }

    [Fact]
    public async Task ExecuteRegisteredAsync_UsesRegisteredExecutor()
    {
        var calls = 0;
        SchedulerExecutors.Register(ScheduledTaskKind.Pipeline, (task, _) =>
        {
            calls++;
            return Task.FromResult<BackupRunResult?>(
                new BackupRunResult { Success = true, ScenarioName = task.Name });
        });
        try
        {
            var task = new ScheduledTask { Kind = ScheduledTaskKind.Pipeline, Name = "nightly" };
            var result = await SchedulerService.ExecuteRegisteredAsync(task);

            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.Equal("nightly", result.ScenarioName);
            Assert.Equal(1, calls);
        }
        finally
        {
            SchedulerExecutors.Unregister(ScheduledTaskKind.Pipeline);
        }
    }

    [Fact]
    public async Task ExecuteRegisteredAsync_ReturnsNullWithoutHandler()
    {
        SchedulerExecutors.Unregister(ScheduledTaskKind.AutoTests);

        var result = await SchedulerService.ExecuteRegisteredAsync(
            new ScheduledTask { Kind = ScheduledTaskKind.AutoTests });

        Assert.Null(result);
    }

    [Theory]
    [InlineData(ScheduledTaskKind.Pipeline, ProFeature.Pipelines)]
    [InlineData(ScheduledTaskKind.AutoTests, ProFeature.AutoTests)]
    [InlineData(ScheduledTaskKind.TestDeploy, ProFeature.TestDeploy)]
    [InlineData(ScheduledTaskKind.Backup, null)]
    public void RequiredProFeature_MapsProKinds(ScheduledTaskKind kind, ProFeature? expected) =>
        Assert.Equal(expected, ScheduledTaskEditViewModel.RequiredProFeature(kind));

    [Theory]
    [InlineData("cm_lang_ru.json", "Конвейер обслуживания")]
    [InlineData("cm_lang_en.json", "Maintenance pipeline")]
    public void NewKindTitles_ExistInLanguageResources(string resource, string expectedPipeline)
    {
        var assembly = typeof(LocalizationManager).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);

        using var document = JsonDocument.Parse(stream!);
        var strings = document.RootElement.GetProperty("strings");
        Assert.Equal(expectedPipeline, strings.GetProperty("Schedule.Kind.Pipeline").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            strings.GetProperty("Schedule.Kind.AutoTests").GetString()));
    }

    [Fact]
    public void KindName_UsesNewLocalizationKeys()
    {
        // KindName обращается к своим ключам, а не к fallback «Backup»; без
        // инициализации LocalizationManager.T возвращает сам ключ — значения всё равно
        // должны различаться.
        Assert.NotEqual(ScheduledTaskItemViewModel.KindName(ScheduledTaskKind.Backup),
            ScheduledTaskItemViewModel.KindName(ScheduledTaskKind.Pipeline));
        Assert.NotEqual(ScheduledTaskItemViewModel.KindName(ScheduledTaskKind.Backup),
            ScheduledTaskItemViewModel.KindName(ScheduledTaskKind.AutoTests));
    }
}
