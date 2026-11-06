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
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs, verify: null, CancellationToken.None);

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
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs, verify: null, CancellationToken.None);

        // Частичный успех — НЕ ошибка шага: блокировка применена, сбойные в журнале.
        Assert.True(outcome.Disabled);
        Assert.Null(outcome.Warning);
        Assert.Equal(3, outcome.LogLines.Count); // итоговая строка + 2 перечня сбоев
    }

    [Fact]
    public void EvaluateJobsStep_OperationFailed_ButVerificationShowsAllDisabled_TreatedAsSuccessWithWarning()
    {
        // Главный сценарий 7OH: операция упала «Внутренней ошибкой», но блокировка
        // фактически установлена — шаг считается выполненным (warning, не ошибка).
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs,
            verify: _ => new ScheduledJobsResult { Success = true, Enabled = 0 },
            CancellationToken.None);

        Assert.True(outcome.Disabled);
        Assert.Null(outcome.Warning);
        Assert.Single(outcome.LogLines);
    }

    [Fact]
    public void EvaluateJobsStep_OperationFailed_VerificationShowsEnabledJobs_RealFailure()
    {
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs,
            verify: _ => new ScheduledJobsResult { Success = true, Enabled = 2 },
            CancellationToken.None);

        Assert.False(outcome.Disabled);
        Assert.NotNull(outcome.Warning);
        // В журнале одна поясняющая строка от проверки (число оставшихся включёнными;
        // текст локализован, содержимое не проверяем — T() в тестах без Initialize
        // возвращает ключ).
        Assert.Single(outcome.LogLines);
    }

    [Fact]
    public void EvaluateJobsStep_OperationFailed_VerificationAlsoFails_RealFailureWithHint()
    {
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs,
            verify: _ => ScheduledJobsResult.Fail("Внутренняя ошибка COM-соединения"),
            CancellationToken.None);

        Assert.False(outcome.Disabled);
        Assert.NotNull(outcome.Warning);
        // Пояснение от неудавшейся проверки добавлено в журнал (одна строка).
        Assert.Single(outcome.LogLines);
    }

    [Fact]
    public void EvaluateJobsStep_OperationFailed_NoVerifier_RealFailure()
    {
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs, verify: null, CancellationToken.None);

        Assert.False(outcome.Disabled);
        Assert.NotNull(outcome.Warning);
        Assert.Empty(outcome.LogLines);
    }

    [Fact]
    public void EvaluateJobsStep_VerifierThrows_RealFailureWithoutCrash()
    {
        var jobs = ScheduledJobsResult.Fail("Внутренняя ошибка");
        var outcome = TestDeployService.EvaluateJobsStep(
            "Тест: Demo", jobs,
            verify: _ => throw new InvalidOperationException("verify crashed"),
            CancellationToken.None);

        Assert.False(outcome.Disabled);
        Assert.NotNull(outcome.Warning);
    }

    [Fact]
    public void Service_DisableJobsStep_UsesEvaluationWithVerification()
    {
        // Страж: DisableScheduledJobsAsync идёт через EvaluateJobsStep и verify.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TestDeployService.cs"));
        Assert.Contains("EvaluateJobsStep(target.Name, jobs,", code, StringComparison.Ordinal);
        Assert.Contains("ScheduledJobsService.VerifyAsync", code, StringComparison.Ordinal);
        Assert.Contains("ScheduledJobsService.VerifyOverride", code, StringComparison.Ordinal);
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
                     "TestDeploy.JobsDisabledWithSkips",
                     "TestDeploy.JobsDisabledVerified",
                     "TestDeploy.JobsVerifyHint",
                     "TestDeploy.JobsVerifyRemaining"
                 })
        {
            var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
            var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
            Assert.Contains($"\"{key}\":", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
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
