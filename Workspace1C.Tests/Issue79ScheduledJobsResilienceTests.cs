using System;
using System.IO;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты устойчивости шага «Выключение регламентных заданий» (issue #79, v1.0.1.42,
/// диагностика 7OH 2026-11-06: «Внутренняя ошибка» на месте, хотя блокировка
/// регламентов фактически установлена):
/// <list type="bullet">
/// <item>COM-скрипт операции <c>setall</c> обрабатывает каждое задание в собственном
/// try/catch — сбой чтения одного задания не валит операцию целиком (раньше любой
/// сбой чтения <c>Metadata</c>/<c>Name</c> приводил к общему провалу с «Внутренней
/// ошибкой», при этом задания до сбойного уже были выключены);</item>
/// <item>новая операция <c>verify</c> читает фактическое состояние без записи;</item>
/// <item><see cref="TestDeployService.EvaluateJobsStep"/> классифицирует исход:
/// при сбое операции и подтверждённой блокировке — успех с предупреждением,
/// а не ложная ошибка;</item>
/// <item>журнал окна развёртывания пишется на диск (<see cref="WindowJournalService"/>)
/// и попадает в ZIP-диагностику (<see cref="DiagnosticsBundleService"/>) — в ZIP от
/// 7OH журнала окна не было вообще.</item>
/// </list>
/// </summary>
public sealed class Issue79ScheduledJobsResilienceTests
{
    // ------------------------------------------------------------------
    // Стражи COM-скрипта (ScheduledJobsService.OperationBody)
    // ------------------------------------------------------------------

    [Fact]
    public void OperationBody_SetAll_HasPerJobTryCatchAndErrorsKey()
    {
        var body = ScheduledJobsService.OperationBody;
        // Сегмент операции setall: per-job try/catch, сбор ошибок и ключ errors в ответе.
        var start = body.IndexOf("'setall'{", StringComparison.Ordinal);
        Assert.True(start >= 0, "В OperationBody нет операции setall.");
        var end = body.IndexOf("'verify'{", start, StringComparison.Ordinal);
        Assert.True(end > start, "В OperationBody нет операции verify после setall.");
        var segment = body.Substring(start, end - start);

        Assert.Contains("try{", segment, StringComparison.Ordinal);
        Assert.Contains("catch{[void]$errs.Add(@{index=$i;error=(Err $_)})}", segment, StringComparison.Ordinal);
        Assert.Contains("errors=$errs", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationBody_VerifyOperation_CountsEnabledWithoutWriting()
    {
        var body = ScheduledJobsService.OperationBody;
        var start = body.IndexOf("'verify'{", StringComparison.Ordinal);
        Assert.True(start >= 0, "В OperationBody нет операции verify (issue #79, v1.0.1.42).");
        var segment = body.Substring(start);

        // verify только читает: нет S (запись свойства) и Write.
        Assert.DoesNotContain("S $j", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("'Write'", segment, StringComparison.Ordinal);
        Assert.Contains("enabled=$en", segment, StringComparison.Ordinal);
        Assert.Contains("total=$n", segment, StringComparison.Ordinal);
        Assert.Contains("errors=$errs", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationBody_JInfo_MetadataReadIsSoft()
    {
        var body = ScheduledJobsService.OperationBody;
        // Чтение Metadata в JInfo мягкое: сбой чтения даёт пустое имя, а не провал list.
        Assert.Contains("$md=Try-G $j 'Metadata'", body, StringComparison.Ordinal);
        Assert.Contains("try{$name=[string](G $md 'Name')}catch{$name=''}", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Разбор ответа скрипта (ScheduledJobsService.ParseErrors)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseErrors_ParsesPerJobErrorsFromScriptResponse()
    {
        using var doc = JsonDocument.Parse(
            """{"changed":3,"total":5,"errors":[{"index":1,"error":"Внутренняя ошибка"},{"index":3,"error":"boom"}]}""");
        var errors = ScheduledJobsService.ParseErrors(doc.RootElement);

        Assert.Equal(2, errors.Count);
        Assert.Contains("задание 1: Внутренняя ошибка", errors);
        Assert.Contains("задание 3: boom", errors);
    }

    [Fact]
    public void ParseErrors_EmptyOrMissingErrors_ReturnsEmptyList()
    {
        using var doc = JsonDocument.Parse("""{"changed":5,"total":5}""");
        Assert.Empty(ScheduledJobsService.ParseErrors(doc.RootElement));

        using var doc2 = JsonDocument.Parse("""{"changed":5,"total":5,"errors":[]}""");
        Assert.Empty(ScheduledJobsService.ParseErrors(doc2.RootElement));
    }

    // ------------------------------------------------------------------
    // Классификация исхода шага (TestDeployService.EvaluateJobsStep)
    // ------------------------------------------------------------------

    [Fact]
    public void EvaluateJobsStep_AllDisabled_NoWarning()
    {
        var jobs = new ScheduledJobsResult { Success = true, Changed = 4 };
        var outcome = TestDeployService.EvaluateJobsStep("Тест: Demo", jobs);

        Assert.True(outcome.Disabled);
        Assert.Null(outcome.Warning);
        Assert.Single(outcome.LogLines);
    }

    [Fact]
    public void EvaluateJobsStep_PartialSkips_SuccessWithDetailsInLog()
    {
        var jobs = new ScheduledJobsResult
        {
            Success = true,
            Changed = 3,
            Errors = new[] { "задание 1: Внутренняя ошибка", "задание 4: boom" }
        };
        var outcome = TestDeployService.EvaluateJobsStep("Тест: Demo", jobs);

        // Частичный успех — НЕ ошибка шага: блокировка применена, сбойные в журнале.
        Assert.True(outcome.Disabled);
        Assert.Null(outcome.Warning);
        Assert.Equal(3, outcome.LogLines.Count); // итоговая строка + 2 перечня сбоев
    }

    [Fact]
    public void EvaluateJobsStep_OperationFailed_WarningWithoutVerifyCounting()
    {
        // Issue #79 (v1.0.1.44): verify-подсчёт заданий удалён (вопрос 7OH «что мы
        // считаем?») — при сбое операции фиксируется warning (не ошибка, Success
        // развёртывания не роняется), без повторного COM-чтения и подсчёта.
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep("Тест: Demo", jobs);

        Assert.False(outcome.Disabled);
        Assert.NotNull(outcome.Warning);
        Assert.Empty(outcome.LogLines);
    }

    [Fact]
    public void Service_DenyJobsStep_BeforeRestore_AndComStepSimplified()
    {
        // Страж (issue #79, v1.0.1.44): rac-блокировка регламентных заданий выполняется
        // ДО загрузки (шаг DenyJobs перед Restore), а COM-шаг после загрузки упрощён:
        // EvaluateJobsStep без verify, при успешной rac-блокировке COM пропускается.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TestDeployService.cs"));
        Assert.Contains("TestDeploy.Step.DenyJobs", code, StringComparison.Ordinal);
        Assert.Contains("TestDeploy.JobsSkippedDenyApplied", code, StringComparison.Ordinal);
        Assert.Contains("EvaluateJobsStep(target.Name, jobs)", code, StringComparison.Ordinal);
        Assert.Contains("TryDenyScheduledJobsAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduledJobsService.VerifyAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduledJobsService.VerifyOverride", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Журнал окна в ZIP-диагностике (стражи исходников окон)
    // ------------------------------------------------------------------

    [Fact]
    public void TestDeployWindows_RouteAllLogLinesThroughLogLineHelper()
    {
        // Журнал окна должен дублироваться в WindowJournalService: единственное
        // _logList.Items.Add — внутри хелпера LogLine, рядом Append в файл журнала.
        foreach (var file in new[]
                 {
                     Path.Combine("Workspace1C", "Views", "TestDeployWindow.cs"),
                     Path.Combine("Workspace1C", "Views", "TestDeployWindow.Avalonia.cs")
                 })
        {
            var code = ReadProjectFile(file);
            Assert.Contains("WindowJournalService.Append(\"testdeploy\", line);", code, StringComparison.Ordinal);

            var occurrences = 0;
            var index = 0;
            while ((index = code.IndexOf("_logList.Items.Add(", index, StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                index++;
            }

            Assert.True(occurrences == 1,
                $"{file}: ожидается ровно одно _logList.Items.Add (в хелпере LogLine), найдено {occurrences}.");
        }
    }

    // ------------------------------------------------------------------
    // Локализация
    // ------------------------------------------------------------------

    [Fact]
    public void Localization_NewJobsStepKeys_ExistInRuAndEn()
    {
        foreach (var key in new[]
                 {
                     "TestDeploy.Step.DenyJobs",
                     "TestDeploy.JobsDisabledWithSkips",
                     "TestDeploy.JobsDenySet",
                     "TestDeploy.JobsDenyFailed",
                     "TestDeploy.JobsDenyUnavailable",
                     "TestDeploy.JobsDenyFileSkipped",
                     "TestDeploy.JobsSkippedDenyApplied"
                 })
        {
            var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
            var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
            Assert.Contains($"\"{key}\":", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Localization_ObsoleteVerifyKeys_Removed()
    {
        // Issue #79 (v1.0.1.44): verify-подсчёт заданий удалён вместе с его ключами.
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        foreach (var key in new[]
                 {
                     "TestDeploy.JobsDisabledVerified",
                     "TestDeploy.JobsVerifyHint",
                     "TestDeploy.JobsVerifyRemaining"
                 })
        {
            Assert.DoesNotContain($"\"{key}\":", ru, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"{key}\":", en, StringComparison.Ordinal);
        }
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
