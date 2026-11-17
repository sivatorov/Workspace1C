using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #134 (1.0.1.80): отсутствующие и дублирующиеся <see cref="Infobase.Id"/>
/// восстанавливаются при загрузке списка баз и сразу сохраняются.
/// <para>
/// Причина: импорт из кластера 1С (<see cref="RacInfobaseMapper"/>) создаёт базы
/// с <c>Id = ""</c>. Матчер процессов и план подменю «Завершить процессы» требовали
/// непустой Id и такие базы молча пропускали — в отчёте оставались только счётчики.
/// </para>
/// </summary>
public sealed class Issue134InfobaseIdNormalizationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "w1c-issue134-" + Guid.NewGuid().ToString("N"));

    public Issue134InfobaseIdNormalizationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
            // Временный каталог — уборка best effort.
        }
    }

    private InfobaseRepository CreateRepository() => new(profileService: null, directory: _directory);

    private static Infobase FileBase(string name, string path, string id = "") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = path
        }
    };

    [Fact]
    public void Load_AssignsMissingId_AndPersistsIt()
    {
        // База, импортированная из кластера: Id пуст.
        var repository = CreateRepository();
        repository.Save(new List<Infobase> { FileBase("bgu", @"C:\Bases\BGU") });

        var loaded = repository.Load();

        var infobase = Assert.Single(loaded);
        Assert.False(string.IsNullOrWhiteSpace(infobase.Id));

        // Починка сохранена: повторная загрузка возвращает тот же идентификатор.
        var reloaded = CreateRepository().Load();
        Assert.Equal(infobase.Id, Assert.Single(reloaded).Id);
    }

    [Fact]
    public void Load_ReplacesDuplicateIds_KeepingFirst()
    {
        var repository = CreateRepository();
        repository.Save(new List<Infobase>
        {
            FileBase("bgu", @"C:\Bases\BGU", id: "duplicate"),
            FileBase("zkgu", @"C:\Bases\ZKGU", id: "duplicate")
        });

        var loaded = repository.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("duplicate", loaded[0].Id);
        Assert.NotEqual(loaded[0].Id, loaded[1].Id);
        Assert.False(string.IsNullOrWhiteSpace(loaded[1].Id));
        Assert.Equal(loaded.Select(b => b.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(), loaded.Count);
    }

    [Fact]
    public void Load_KeepsExistingIds()
    {
        var repository = CreateRepository();
        repository.Save(new List<Infobase>
        {
            FileBase("bgu", @"C:\Bases\BGU", id: "id-bgu"),
            FileBase("zkgu", @"C:\Bases\ZKGU", id: "id-zkgu")
        });

        var loaded = repository.Load();

        Assert.Equal(new[] { "id-bgu", "id-zkgu" }, loaded.Select(b => b.Id));
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyList()
    {
        var repository = CreateRepository();

        Assert.Empty(repository.Load());
    }
}
