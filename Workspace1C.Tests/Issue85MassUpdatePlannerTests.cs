using System;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #85 «Групповое применение» (v1.0.1.43, этап B): источник обновления
/// «Хранилище конфигурации» в массовом обновлении через Конфигуратор — план шагов
/// <see cref="MassLoadCfPlanner.PlanSteps"/> (шаг RepositoryUpdate вместо LoadCf),
/// валидация <see cref="MassLoadCfPlanner.Validate"/> (у каждой цели настроено
/// хранилище; файл .cf не требуется), разбор сохранённого источника
/// (<see cref="MassLoadCfPlanner.ParseSource"/>/<see cref="MassLoadCfPlanner.SourceToString"/>)
/// и маппинг опций <see cref="MassLoadCfPlanner.ToPreparationOptions"/>.
/// </summary>
public sealed class Issue85MassUpdatePlannerTests
{
    private static Infobase ServerBaseWithRepository() => new()
    {
        Id = "base-repo",
        Name = "Бухгалтерия",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv1c",
            DatabaseName = "Бухгалтерия"
        },
        Repository = new RepositorySettings
        {
            Server = "tcp://srv-repo:1542",
            RepositoryName = "ХранилищеБух"
        }
    };

    private static Infobase ServerBaseWithoutRepository() => new()
    {
        Id = "base-norepo",
        Name = "БезХранилища",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv1c",
            DatabaseName = "БезХранилища"
        }
    };

    // ======================= План шагов =======================

    [Fact]
    public void PlanSteps_RepositorySource_Bare_OnlyRepositoryUpdate()
    {
        var steps = MassLoadCfPlanner.PlanSteps(new MassLoadCfOptions
        {
            Source = MassLoadCfSource.Repository
        });

        var plan = Assert.Single(steps);
        Assert.Equal(MassLoadCfStep.RepositoryUpdate, plan);
    }

    [Fact]
    public void PlanSteps_RepositorySource_AllOptions_PrepareDumpRepoUpdateRestore()
    {
        var steps = MassLoadCfPlanner.PlanSteps(new MassLoadCfOptions
        {
            Source = MassLoadCfSource.Repository,
            TerminateSessions = true,
            DenyNewSessions = true,
            DenyScheduledJobs = true,
            DumpDtBefore = true
        });

        Assert.Equal(
            new[]
            {
                MassLoadCfStep.Prepare, MassLoadCfStep.DumpDt,
                MassLoadCfStep.RepositoryUpdate, MassLoadCfStep.Restore
            },
            steps.ToArray());
    }

    [Fact]
    public void PlanSteps_CfFileSource_KeepsIssue106Plan()
    {
        // Источник по умолчанию — файл .cf: прежний план issue #106 не меняется.
        var steps = MassLoadCfPlanner.PlanSteps(new MassLoadCfOptions
        {
            DumpDtBefore = true
        });

        Assert.Equal(
            new[] { MassLoadCfStep.DumpDt, MassLoadCfStep.LoadCf },
            steps.ToArray());
    }

    [Fact]
    public void ToPreparationOptions_RepositorySource_MapsFlags()
    {
        var mapped = MassLoadCfPlanner.ToPreparationOptions(new MassLoadCfOptions
        {
            Source = MassLoadCfSource.Repository,
            TerminateSessions = true,
            DenyNewSessions = true,
            PermissionCode = " код "
        });

        Assert.True(mapped.TerminateSessions);
        Assert.True(mapped.DenyNewSessions);
        Assert.Equal("код", mapped.PermissionCode);
        // Конфигуратор входит в заблокированную базу с кодом разрешения (/UC) —
        // источник «хранилище» совместим с блокировкой, как и файл .cf (issue #106).
        Assert.False(mapped.SessionDenyIncompatible);
    }

    // ======================= Валидация =======================

    [Fact]
    public void Validate_RepositorySource_DoesNotRequireCfFile()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { Source = MassLoadCfSource.Repository },
            new[] { ServerBaseWithRepository() });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RepositorySource_TargetWithoutRepository_AddsNamedError()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { Source = MassLoadCfSource.Repository },
            new[] { ServerBaseWithRepository(), ServerBaseWithoutRepository() });

        // Валидация блокирует старт ДО запуска конфигураторов: ровно одна ошибка —
        // по базе без хранилища (в локализованном сообщении выводится её имя;
        // в тестах без загруженных локалей T() возвращает ключ).
        var error = Assert.Single(errors);
        Assert.Contains("RepoNotConfigured", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CfFileSource_StillRequiresCfFile()
    {
        var errors = MassLoadCfPlanner.Validate(
            new MassLoadCfOptions { Source = MassLoadCfSource.CfFile, CfFilePath = string.Empty },
            new[] { ServerBaseWithRepository() });

        Assert.Single(errors);
    }

    // ======================= Источник в настройках =======================

    [Theory]
    [InlineData("Repository", MassLoadCfSource.Repository)]
    [InlineData("repository", MassLoadCfSource.Repository)]
    [InlineData(" Repository ", MassLoadCfSource.Repository)]
    [InlineData("CfFile", MassLoadCfSource.CfFile)]
    [InlineData("", MassLoadCfSource.CfFile)]
    [InlineData(null, MassLoadCfSource.CfFile)]
    [InlineData("мусор", MassLoadCfSource.CfFile)]
    public void ParseSource_RoundTrip(string? saved, MassLoadCfSource expected)
    {
        Assert.Equal(expected, MassLoadCfPlanner.ParseSource(saved));
        Assert.Equal(MassLoadCfPlanner.ParseSource(saved), MassLoadCfPlanner.ParseSource(
            MassLoadCfPlanner.SourceToString(MassLoadCfPlanner.ParseSource(saved))));
    }
}
