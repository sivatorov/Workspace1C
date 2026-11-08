using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты шага «Блокировка регламентных заданий ДО загрузки» (issue #79, v1.0.1.44,
/// вопрос 7OH 2026-11-07: «почему задания выключаются ПОСЛЕ загрузки — надо ставить
/// блокировку сразу после создания/обнаружения пустой базы, ДО загрузки данных»):
/// <list type="bullet">
/// <item>порядок шагов плана: <c>DenyJobs</c> между созданием/обновлением базы и
/// загрузкой (Restore), COM-шаг выключения — после загрузки;</item>
/// <item>файловая целевая база: rac-шаг пропускается с пояснением в журнале;</item>
/// <item>rac-успех: шаг возвращает true, журнал содержит <c>JobsDenySet</c>;</item>
/// <item>rac-сбой/исключение: warning в журнал и <c>Warnings</c>, шаг возвращает false,
/// исключений нет — развёртывание продолжается.</item>
/// </list>
/// rac-путь подменяется статическим override <see cref="TestDeployService.DenyJobsOverride"/>
/// (по образцу ScheduledJobsService.VerifyOverride) — без rac и COM.
/// </summary>
public sealed class TestDeployDenyJobsStepTests
{
    private static Infobase ServerTarget() => new()
    {
        Name = "Тест: Demo",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv",
            Port = 1541,
            DatabaseName = "demo_t"
        }
    };

    private static Infobase FileTarget() => new()
    {
        Name = "Тест: Demo",
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\demo_t" }
    };

    private static TestDeployGroupPlan GroupPlan(string path) =>
        new() { GroupPath = path, Exists = true };

    // ------------------------------------------------------------------
    // Порядок шагов (TestDeployPlanner.DescribeSteps)
    // ------------------------------------------------------------------

    [Fact]
    public void DescribeSteps_DenyJobs_BetweenCreateAndRestore()
    {
        var scenario = new TestDeployScenario
        {
            TargetMode = TestDeployTargetMode.Server,
            DisableScheduledJobs = true
        };
        var steps = TestDeployPlanner.DescribeSteps(scenario, null, GroupPlan("Тест")).ToList();

        var create = steps.FindIndex(s => s.Kind == TestDeployStepKind.Create);
        var deny = steps.FindIndex(s => s.Kind == TestDeployStepKind.DenyJobs);
        var restore = steps.FindIndex(s => s.Kind == TestDeployStepKind.Restore);
        var disableJobs = steps.FindIndex(s => s.Kind == TestDeployStepKind.DisableJobs);

        Assert.True(create >= 0, "Шаг «Создание» должен быть в плане.");
        Assert.True(deny > create, "Блокировка заданий — после создания базы.");
        Assert.True(restore > deny, "Блокировка заданий — ДО загрузки данных (Restore).");
        Assert.True(disableJobs > restore, "COM-шаг выключения остаётся после загрузки.");
    }

    [Fact]
    public void DescribeSteps_DenyJobs_AfterUpdateAndBeforeRestoreOrder_KeptForExistingTarget()
    {
        var scenario = new TestDeployScenario { DisableScheduledJobs = true };
        var existing = new Infobase
        {
            Name = "Тест: Demo",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv",
                DatabaseName = "demo_t"
            }
        };
        var steps = TestDeployPlanner.DescribeSteps(scenario, existing, GroupPlan("Тест")).ToList();

        var update = steps.FindIndex(s => s.Kind == TestDeployStepKind.Update);
        var deny = steps.FindIndex(s => s.Kind == TestDeployStepKind.DenyJobs);

        Assert.True(update >= 0, "Шаг «Обновление» должен быть в плане.");
        Assert.True(deny == update + 1, "Для существующей копии блокировка идёт сразу после «Обновление».");
        Assert.DoesNotContain(steps, s => s.Kind == TestDeployStepKind.Restore);
    }

    [Fact]
    public void DescribeSteps_FileTarget_NoDenyJobsStep()
    {
        var scenario = new TestDeployScenario
        {
            TargetMode = TestDeployTargetMode.File,
            DisableScheduledJobs = true
        };
        var steps = TestDeployPlanner.DescribeSteps(scenario, null, GroupPlan(string.Empty));

        Assert.DoesNotContain(steps, s => s.Kind == TestDeployStepKind.DenyJobs);
        Assert.Contains(steps, s => s.Kind == TestDeployStepKind.DisableJobs);
    }

    [Fact]
    public void DescribeSteps_JobsDisabledOptOut_NoDenyJobsStep()
    {
        var scenario = new TestDeployScenario
        {
            TargetMode = TestDeployTargetMode.Server,
            DisableScheduledJobs = false
        };
        var steps = TestDeployPlanner.DescribeSteps(scenario, null, GroupPlan("Тест"));

        Assert.DoesNotContain(steps, s => s.Kind == TestDeployStepKind.DenyJobs);
        Assert.DoesNotContain(steps, s => s.Kind == TestDeployStepKind.DisableJobs);
    }

    // ------------------------------------------------------------------
    // Шаг rac-блокировки (TestDeployService.TryDenyScheduledJobsAsync)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryDenyScheduledJobsAsync_FileBase_SkippedWithNote()
    {
        var result = new TestDeployResult();
        var applied = await TestDeployService.TryDenyScheduledJobsAsync(
            FileTarget(), result, CancellationToken.None);

        Assert.False(applied);
        Assert.Contains("TestDeploy.JobsDenyFileSkipped", result.Log);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task TryDenyScheduledJobsAsync_RacSuccess_JobsDenySetInLog()
    {
        TestDeployService.DenyJobsOverride = (_, _) => Task.FromResult((true, (string?)null));
        try
        {
            var result = new TestDeployResult();
            var applied = await TestDeployService.TryDenyScheduledJobsAsync(
                ServerTarget(), result, CancellationToken.None);

            Assert.True(applied);
            Assert.Contains("TestDeploy.JobsDenySet", result.Log);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            TestDeployService.DenyJobsOverride = null;
        }
    }

    [Fact]
    public async Task TryDenyScheduledJobsAsync_RacFailure_WarningNotError()
    {
        TestDeployService.DenyJobsOverride = (_, _) => Task.FromResult((false, (string?)"rac недоступен"));
        try
        {
            var result = new TestDeployResult();
            var applied = await TestDeployService.TryDenyScheduledJobsAsync(
                ServerTarget(), result, CancellationToken.None);

            Assert.False(applied);
            // T() в тестах без Initialize возвращает ключ; текст ошибки в реальном
            // прогоне подставляется в {0} локализованной строки.
            Assert.Contains("TestDeploy.JobsDenyFailed", result.Log);
            Assert.Single(result.Warnings);
            Assert.Contains("TestDeploy.JobsDenyFailed", result.Warnings);
        }
        finally
        {
            TestDeployService.DenyJobsOverride = null;
        }
    }

    [Fact]
    public async Task TryDenyScheduledJobsAsync_RacThrows_WarningNotException()
    {
        TestDeployService.DenyJobsOverride = (_, _) =>
            throw new InvalidOperationException("rac crashed");
        try
        {
            var result = new TestDeployResult();
            var applied = await TestDeployService.TryDenyScheduledJobsAsync(
                ServerTarget(), result, CancellationToken.None);

            Assert.False(applied);
            Assert.Single(result.Warnings);
            Assert.Contains("TestDeploy.JobsDenyFailed", result.Warnings);
        }
        finally
        {
            TestDeployService.DenyJobsOverride = null;
        }
    }
}
