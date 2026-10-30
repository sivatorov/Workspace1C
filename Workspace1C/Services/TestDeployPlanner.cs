using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая логика сценария «Развернуть тестовую копию» (п.10, 1.0.0.92): предложение
/// имени тестовой базы, поиск существующей тестовой копии в списке, решение по группе
/// «Тест» (автосоздание с отличительным цветом), последовательность шагов плана и
/// валидация параметров. Не зависит от платформы и UI — покрыта юнит-тестами
/// (см. <c>TestDeployTests</c> в Workspace1C.Tests).
/// </summary>
public static class TestDeployPlanner
{
    /// <summary>Стандартное имя тестовой группы.</summary>
    public const string DefaultTestGroupName = "Тест";

    /// <summary>Стандартный префикс заголовка тестовой базы.</summary>
    public const string DefaultTitlePrefix = "Тест: ";

    /// <summary>Цвет группы «Тест» по умолчанию (янтарно-оранжевый, #RRGGBB).</summary>
    public const string DefaultGroupColor = "#D97706";

    /// <summary>Префикс имени временного файла выгрузки.</summary>
    public const string DumpFileName = "testcopy.dt";

    /// <summary>
    /// Предлагает имя тестовой базы: «Тест: <имя источника>». Уже префиксованные
    /// имена не дублируют префикс (повторное открытие мастера для той же базы).
    /// </summary>
    public static string ProposeTargetName(string? sourceName, string? prefix = null)
    {
        var p = string.IsNullOrWhiteSpace(prefix) ? DefaultTitlePrefix : prefix;
        var name = (sourceName ?? string.Empty).Trim();
        if (name.Length == 0)
            return p.Trim();
        return name.StartsWith(p, StringComparison.OrdinalIgnoreCase) ? name : p + name;
    }

    /// <summary>
    /// Итоговое имя тестовой базы: явное <see cref="TestDeployScenario.TargetName"/>,
    /// иначе — префикс заголовка + имя источника (когда <see cref="TestDeployScenario.ChangeTitle"/>),
    /// иначе имя источника как есть.
    /// </summary>
    public static string ResolveTargetName(TestDeployScenario scenario, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var explicitName = (scenario.TargetName ?? string.Empty).Trim();
        if (explicitName.Length > 0)
            return explicitName;
        if (!scenario.ChangeTitle)
            return (sourceName ?? string.Empty).Trim();
        return ProposeTargetName(sourceName);
    }

    /// <summary>Путь к временному файлу выгрузки в указанном каталоге.</summary>
    public static string BuildDumpPath(string tempDirectory) =>
        string.IsNullOrWhiteSpace(tempDirectory)
            ? DumpFileName
            : Path.Combine(tempDirectory.TrimEnd('\\', '/'), DumpFileName);

    /// <summary>
    /// Подходит ли имя кандидата под имя тестовой базы: точное совпадение
    /// (регистронезависимое) либо, при включённом добавлении в группу «Тест», имя,
    /// начинающееся с «Тест: …» + пробел/конец строки (переименованные копии вроде
    /// «Тест: УТ 2» не «уезжают» от повторного развёртывания).
    /// </summary>
    public static bool MatchesTargetName(string targetName, string? candidateName, bool allowPrefixed = true)
    {
        var t = (targetName ?? string.Empty).Trim();
        var c = (candidateName ?? string.Empty).Trim();
        if (t.Length == 0)
            return false;
        if (string.Equals(t, c, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!allowPrefixed)
            return false;
        // «Тест: УТ» → «Тест: УТ 2», «Тест: УТ (копия)» — это та же тестовая копия.
        return c.StartsWith(t, StringComparison.OrdinalIgnoreCase) &&
               (c.Length == t.Length || char.IsWhiteSpace(c[t.Length]));
    }

    /// <summary>
    /// Ищет существующую тестовую копию в списке баз: имя равно имени тестовой базы
    /// или начинается с него (см. <see cref="MatchesTargetName"/>). Только базы списка
    /// приложения — реальное наличие ИБ на сервере/диске не проверяется.
    /// </summary>
    public static Infobase? FindExistingTarget(
        IEnumerable<Infobase>? infobases,
        TestDeployScenario scenario,
        string targetName)
    {
        if (infobases is null || scenario is null)
            return null;
        var allowPrefixed = scenario.AddToTestGroup && scenario.ChangeTitle;
        foreach (var ib in infobases)
        {
            if (ib is null || ib.Connection is null)
                continue;
            if (MatchesTargetName(targetName, ib.Name, allowPrefixed))
                return ib;
        }
        return null;
    }

    /// <summary>
    /// Решение по группе «Тест»: если <see cref="TestDeployScenario.AddToTestGroup"/> —
    /// находит существующую группу с именем <see cref="TestDeployScenario.TestGroupName"/>
    /// (корневую или по полному пути) и возвращает её путь; иначе создаёт новую корневую
    /// группу с отличительным цветом. Если группы существуют как вложенные — берётся
    /// первая корневая (приоритет простому сценарию).
    /// </summary>
    public static TestDeployGroupPlan EnsureGroup(IReadOnlyList<Group>? groups, TestDeployScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (!scenario.AddToTestGroup)
            return new TestDeployGroupPlan { GroupPath = string.Empty, Exists = true };

        var groupName = string.IsNullOrWhiteSpace(scenario.TestGroupName)
            ? DefaultTestGroupName
            : scenario.TestGroupName.Trim();
        var all = groups?.Where(g => g is not null).ToList() ?? new List<Group>();

        // Существующая группа: имя совпадает точно (по полному пути или корне).
        var existing = all.FirstOrDefault(g =>
            string.Equals((g.Name ?? string.Empty).Trim(), groupName, StringComparison.OrdinalIgnoreCase)) ??
            all.FirstOrDefault(g =>
                string.Equals(GroupHierarchyHelper.GetFullPath(g, all), groupName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new TestDeployGroupPlan
            {
                GroupPath = GroupHierarchyHelper.GetFullPath(existing, all),
                Exists = true
            };

        // Новой группе задаём отличительный цвет иконки, чтобы она читалась
        // на цветном фоне заголовка группы.
        var newGroup = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = groupName,
            ParentId = string.Empty,
            Color = string.IsNullOrWhiteSpace(scenario.TestGroupColor)
                ? DefaultGroupColor
                : scenario.TestGroupColor.Trim(),
            IconColor = "#FFFFFF"
        };
        return new TestDeployGroupPlan { NewGroup = newGroup, GroupPath = groupName, Exists = false };
    }

    /// <summary>
    /// Последовательность шагов плана (для предпросмотра в мастере и журнала прогресса).
    /// Отражение «разумного поведения»: существующая тестовая база — обновление данных,
    /// отсутствующая — создание.
    /// </summary>
    public static IReadOnlyList<TestDeployStep> DescribeSteps(
        TestDeployScenario scenario,
        Infobase? existingTarget,
        TestDeployGroupPlan groupPlan)
    {
        var steps = new List<TestDeployStep>
        {
            new(TestDeployStepKind.Dump, "TestDeploy.Step.Dump")
        };

        // Группу создаём до создания базы (база сразу попадает в нужную группу).
        if (scenario.AddToTestGroup && groupPlan is { NewGroup: not null })
            steps.Add(new TestDeployStep(TestDeployStepKind.EnsureGroup, "TestDeploy.Step.EnsureGroup"));

        if (existingTarget is null)
        {
            steps.Add(new TestDeployStep(TestDeployStepKind.Create, "TestDeploy.Step.Create"));
            steps.Add(new TestDeployStep(TestDeployStepKind.Restore, "TestDeploy.Step.Restore"));
        }
        else
        {
            steps.Add(new TestDeployStep(TestDeployStepKind.Update, "TestDeploy.Step.Update"));
        }

        if (scenario.DisableScheduledJobs)
            steps.Add(new TestDeployStep(TestDeployStepKind.DisableJobs, "TestDeploy.Step.DisableJobs"));

        if (scenario.ChangeTitle || scenario.ChangeColor)
            steps.Add(new TestDeployStep(TestDeployStepKind.Markers, "TestDeploy.Step.Markers"));

        if (scenario.AddToTestGroup)
            steps.Add(new TestDeployStep(TestDeployStepKind.AddToGroup, "TestDeploy.Step.AddToGroup"));

        return steps;
    }

    /// <summary>
    /// Строит полный план развёртывания: итоговое имя, существующая/новая база,
    /// путь файла выгрузки, решение по группе «Тест» и шаги.
    /// </summary>
    public static TestDeployPlan BuildPlan(
        TestDeployScenario scenario,
        string? sourceName,
        IEnumerable<Infobase>? allInfobases,
        IReadOnlyList<Group>? groups,
        string tempDirectory)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var targetName = ResolveTargetName(scenario, sourceName);
        var existing = FindExistingTarget(allInfobases, scenario, targetName);
        var groupPlan = EnsureGroup(groups, scenario);
        return new TestDeployPlan
        {
            TargetName = targetName,
            ExistingTarget = existing,
            DumpFilePath = BuildDumpPath(tempDirectory),
            GroupPlan = groupPlan,
            Steps = DescribeSteps(scenario, existing, groupPlan)
        };
    }

    /// <summary>
    /// Валидация параметров сценария. Возвращает список ключей локализации ошибок
    /// (пустой — параметры корректны). <paramref name="existingTarget"/> — найденная
    /// тестовая база списка: при обновлении данные версии платформы и адреса сервера
    /// берутся из неё, поэтому часть требований снимается.
    /// </summary>
    public static List<string> Validate(
        TestDeployScenario? scenario,
        Infobase? source,
        Infobase? existingTarget,
        Func<Infobase, DbmsBackupProfile?>? dbmsProfileResolver = null)
    {
        var errors = new List<string>();
        if (scenario is null)
        {
            errors.Add("TestDeploy.InvalidScenario");
            return errors;
        }

        if (source is null)
        {
            errors.Add("TestDeploy.NoSource");
            return errors;
        }

        var targetName = ResolveTargetName(scenario, source.Name);
        if (string.IsNullOrWhiteSpace(targetName))
            errors.Add("TestDeploy.EnterTargetName");

        var updating = existingTarget is not null && scenario.UpdateExisting;
        if (!updating && string.IsNullOrWhiteSpace(source.PlatformVersion))
            errors.Add("TestDeploy.NoPlatform");

        switch (scenario.TargetMode)
        {
            case TestDeployTargetMode.Server:
                if (string.IsNullOrWhiteSpace(scenario.ServerAddress) ||
                    string.IsNullOrWhiteSpace(scenario.DatabaseName))
                    errors.Add("CreateInfobase.EnterServerAndDb");
                if (scenario.CreateSqlDatabase &&
                    (string.IsNullOrWhiteSpace(scenario.Dbms) ||
                     string.IsNullOrWhiteSpace(scenario.DbServer)))
                    errors.Add("TestDeploy.EnterDbDetails");
                break;

            case TestDeployTargetMode.File:
                if (string.IsNullOrWhiteSpace(scenario.FilePath))
                    errors.Add("CreateInfobase.EnterFilePath");
                break;
        }

        if (existingTarget is not null && !scenario.UpdateExisting)
            errors.Add("TestDeploy.ExistingTargetBlocked");

        if (scenario.AddToTestGroup && string.IsNullOrWhiteSpace(scenario.TestGroupName))
            errors.Add("TestDeploy.EnterTestGroup");

        // issue #79 (1.0.1.7, репорт 7OH «заполните профиль»): выгрузка клиент-серверной
        // базы движком ibcmd требует профиль СУБД — ibcmd подключается к СУБД напрямую
        // (IbcmdService.BuildConnectionArguments), поэтому галки «авторизация ИБ» и
        // «Подставить из базы» не помогают. Ошибка показывается ДО старта операций,
        // а не на шаге «Выгрузка».
        if (scenario.DumpEngine == BackupEngine.Ibcmd
            && source.Connection?.Type == ConnectionType.ClientServer)
        {
            var dbmsProfile = dbmsProfileResolver is not null
                ? dbmsProfileResolver(source)
                : TryLoadDbmsProfile(source);
            if (dbmsProfile is null || string.IsNullOrWhiteSpace(dbmsProfile.Database))
                errors.Add("TestDeploy.NeedDbmsProfile");
        }

        return errors;
    }

    /// <summary>
    /// Профиль СУБД исходной базы из хранилища профилей (issue #79). Ошибка чтения
    /// не роняет валидацию — считается, что профиля нет (ниже понятная ошибка).
    /// </summary>
    private static DbmsBackupProfile? TryLoadDbmsProfile(Infobase source)
    {
        try
        {
            return DbmsBackupService.Load().Find(source.Id);
        }
        catch
        {
            return null;
        }
    }
}