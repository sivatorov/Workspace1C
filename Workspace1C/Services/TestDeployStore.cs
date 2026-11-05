using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище сценариев «Развернуть тестовую копию» (п.10, 1.0.0.92): список
/// <see cref="TestDeployScenario"/> в одном JSON-файле <c>test_deploy.json</c> в каталоге
/// данных профиля (через <see cref="FeatureDataStore"/>). Идентификатор сценария
/// (<see cref="TestDeployScenario.Id"/>) ссылается из задания по расписанию
/// (<see cref="ScheduledTask.ScenarioId"/>, тип <see cref="ScheduledTaskKind.TestDeploy"/>).
/// </summary>
public static class TestDeployStore
{
    /// <summary>Имя файла сценариев в каталоге данных профиля.</summary>
    public const string FileName = "test_deploy.json";

    /// <summary>Обёртка для сериализации списка (FeatureDataStore требует новый экземпляр по умолчанию).</summary>
    private sealed class ScenarioList
    {
        public List<TestDeployScenario> Scenarios { get; set; } = new();
    }

    /// <summary>Загружает все сценарии (пустой список, если файла нет или он битый).</summary>
    public static List<TestDeployScenario> Load()
    {
        var data = FeatureDataStore.Load<ScenarioList>(FileName);
        return data?.Scenarios ?? new List<TestDeployScenario>();
    }

    /// <summary>Сохраняет список сценариев целиком.</summary>
    public static void Save(IEnumerable<TestDeployScenario> scenarios)
    {
        FeatureDataStore.Save(FileName, new ScenarioList
        {
            Scenarios = (scenarios ?? Array.Empty<TestDeployScenario>()).ToList()
        });
    }

    /// <summary>Добавляет или обновляет сценарий (по <see cref="TestDeployScenario.Id"/>).</summary>
    public static void Upsert(TestDeployScenario scenario)
    {
        if (scenario is null)
            return;
        var list = Load();
        list.RemoveAll(s => string.Equals(s.Id, scenario.Id, StringComparison.Ordinal));
        list.Add(scenario);
        Save(list);
    }

    /// <summary>Удаляет сценарий по идентификатору.</summary>
    public static void Delete(string id)
    {
        var list = Load();
        var count = list.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
        if (count > 0)
            Save(list);
    }

    /// <summary>Возвращает сценарий по идентификатору или null.</summary>
    public static TestDeployScenario? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return Load().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Последний сценарий исходной базы (issue #79, комментарий 7OH 2026-11-05T11:22:
    /// «Просьба сохранять значения окна: Движок выгрузки, Сервер СУБД, Пользователь СУБД»).
    /// Сценарии накапливаются по одному на успешное развёртывание — последним в списке
    /// хранится самый свежий; используется окном мастера для восстановления значений.
    /// <paramref name="scenarios"/> — точка подмены списка для юнит-тестов (null — читается
    /// хранилище). Возвращает null, если сценариев этой базы нет.
    /// </summary>
    public static TestDeployScenario? FindLastForSource(
        string? infobaseId, IReadOnlyList<TestDeployScenario>? scenarios = null)
    {
        if (string.IsNullOrWhiteSpace(infobaseId))
            return null;
        var list = (IReadOnlyList<TestDeployScenario>)(scenarios ?? (IReadOnlyList<TestDeployScenario>)Load());
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].MatchesSource(infobaseId))
                return list[i];
        }
        return null;
    }
}