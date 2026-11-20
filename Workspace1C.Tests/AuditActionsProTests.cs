using System.Linq;
using Workspace1C.Services.Audit;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>Коды аудита для модулей ПРО 2.x: стабильны, уникальны и не пусты.</summary>
public sealed class AuditActionsProTests
{
    [Fact]
    public void ProActionCodes_AreUniqueAndNonEmpty()
    {
        var codes = new[]
        {
            AuditActions.PipelineRun,
            AuditActions.PipelineStep,
            AuditActions.QueryRun,
            AuditActions.EnvDiffRun,
            AuditActions.AutoTestRun,
            AuditActions.PerfSnapshot,
            AuditActions.CloudUpload,
            AuditActions.CloudDecrypt
        };

        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void ProActionCodes_HaveExpectedStableValues()
    {
        Assert.Equal("pipeline.run", AuditActions.PipelineRun);
        Assert.Equal("pipeline.step", AuditActions.PipelineStep);
        Assert.Equal("query.run", AuditActions.QueryRun);
        Assert.Equal("envdiff.run", AuditActions.EnvDiffRun);
        Assert.Equal("autotest.run", AuditActions.AutoTestRun);
        Assert.Equal("perf.snapshot", AuditActions.PerfSnapshot);
        Assert.Equal("cloud.upload", AuditActions.CloudUpload);
        Assert.Equal("cloud.decrypt", AuditActions.CloudDecrypt);
    }
}
