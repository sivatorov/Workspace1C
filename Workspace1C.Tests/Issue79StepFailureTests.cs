using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #79 «Развернуть тестовую копию» (v1.0.1.40, diagnostics 7OH
/// 2026-11-06 — ошибка уже после создания базы и загрузки .dt):
/// <list type="bullet">
/// <item>шаги 5–7 (DisableJobs/Markers/AddToGroup) имеют собственный try/catch с
/// <c>RecordStepFailure</c> — имя шага видно в любом сообщении, тип/стек попадают
/// в журнал окна (страж по исходникам);</item>
/// <item>сбой финализации окна (TestDeployStore.Upsert / RegisterTask) защищён:
/// он не роняет успешное развёртывание и пишет тип/стек в журнал;</item>
/// <item>типовая COM-ошибка «Внутренняя ошибка» расшифровывается подсказкой.</item>
/// </list>
/// </summary>
public sealed class Issue79StepFailureTests
{
    [Theory]
    [InlineData("TestDeploy.Step.DisableJobs")]
    [InlineData("TestDeploy.Step.Markers")]
    [InlineData("TestDeploy.Step.AddToGroup")]
    public void Service_WrapsEachFinalStepWithRecordStepFailure(string stepKey)
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TestDeployService.cs"));
        Assert.Contains($"RecordStepFailure(result, T(\"{stepKey}\"), ex)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_GeneralCatchUsesRecordStepFailure()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TestDeployService.cs"));
        Assert.Contains("RecordStepFailure(result, T(\"TestDeploy.Step.Unknown\"), ex)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_FinalizationIsGuarded()
    {
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"));
        Assert.Contains("TestDeploy.FinalizeFailed", wpf, StringComparison.Ordinal);
        Assert.Contains("TestDeployStore.Upsert(scenario);", wpf, StringComparison.Ordinal);
        Assert.Contains("TestDeployScheduleHelper.RegisterTask(scenario);", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs"));
        Assert.Contains("TestDeploy.FinalizeFailed", avalonia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Внутренняя ошибка", true)]
    [InlineData("внутренняя ошибка COM-соединения", true)]
    [InlineData("Internal error: something broke", true)]
    [InlineData("База отсутствует в сервере", false)]
    [InlineData("Внутренняя ошибка сервера: детали здесь", true)]
    public void ExplainComInternalError_AppendsHintOnlyForComMessage(string message, bool expectHint)
    {
        var result = TestDeployService.ExplainComInternalError(message);

        if (expectHint)
        {
            Assert.NotEqual(message, result);
            // Подсказка добавлена в конец, исходный текст не потерян.
            Assert.StartsWith(message, result, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(message, result);
        }
    }

    [Fact]
    public void ExplainComInternalError_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Equal(string.Empty, TestDeployService.ExplainComInternalError(string.Empty));
        Assert.Null(TestDeployService.ExplainComInternalError(null));
    }

    [Fact]
    public void Localization_FinalizeFailedAndInternalErrorHint_ExistInRuAndEn()
    {
        foreach (var key in new[] { "TestDeploy.FinalizeFailed", "TestDeploy.InternalErrorHint" })
        {
            var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
            var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
            Assert.Contains($"\"{key}\":", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }
}
