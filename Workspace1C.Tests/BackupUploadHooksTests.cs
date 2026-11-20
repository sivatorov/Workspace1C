using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services;
using Workspace1C.Services.Audit;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Хук «копия готова» для модулей ПРО: регистрация загрузчиков, изоляция ошибок,
/// замена по имени и снятие.
/// </summary>
public sealed class BackupUploadHooksTests
{
    private sealed class RecordingUploader : IBackupUploader
    {
        public RecordingUploader(string name, bool fail = false)
        {
            Name = name;
            Fail = fail;
        }

        public string Name { get; }
        public bool Fail { get; }
        public List<string> Files { get; } = new();

        public Task<BackupUploadResult> UploadAsync(BackupUploadContext context, CancellationToken cancellationToken)
        {
            Files.Add(context.LocalFilePath);
            if (Fail)
                throw new InvalidOperationException("boom");
            return Task.FromResult(BackupUploadResult.Ok);
        }
    }

    [Fact]
    public async Task RunAsync_InvokesAllUploaders()
    {
        BackupUploadHooks.Clear();
        var first = new RecordingUploader("one");
        var second = new RecordingUploader("two");
        BackupUploadHooks.Register(first);
        BackupUploadHooks.Register(second);
        try
        {
            var results = await BackupUploadHooks.RunAsync(
                new BackupUploadContext(@"C:\backup.dt", "Бухгалтерия", "Ночная", "scenario"));

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Single(first.Files);
            Assert.Single(second.Files);
        }
        finally
        {
            BackupUploadHooks.Clear();
        }
    }

    [Fact]
    public async Task RunAsync_IsolatesFailingUploader()
    {
        BackupUploadHooks.Clear();
        var failing = new RecordingUploader("bad", fail: true);
        var good = new RecordingUploader("good");
        BackupUploadHooks.Register(failing);
        BackupUploadHooks.Register(good);
        try
        {
            var results = await BackupUploadHooks.RunAsync(
                new BackupUploadContext(@"C:\backup.dt", "b", "s", "dbms"));

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => !r.Success && r.Error == "boom");
            Assert.Single(good.Files);
        }
        finally
        {
            BackupUploadHooks.Clear();
        }
    }

    [Fact]
    public void Register_ReplacesByName_AndUnregisterRemoves()
    {
        BackupUploadHooks.Clear();
        try
        {
            BackupUploadHooks.Register(new RecordingUploader("s3"));
            BackupUploadHooks.Register(new RecordingUploader("s3"));
            Assert.Single(BackupUploadHooks.Uploaders);

            BackupUploadHooks.Unregister("s3");
            Assert.Empty(BackupUploadHooks.Uploaders);
        }
        finally
        {
            BackupUploadHooks.Clear();
        }
    }

    [Fact]
    public async Task RunAsync_WithoutUploaders_ReturnsEmpty()
    {
        BackupUploadHooks.Clear();
        var results = await BackupUploadHooks.RunAsync(
            new BackupUploadContext(@"C:\backup.dt", "b", "s", "scenario"));
        Assert.Empty(results);
    }

    [Fact]
    public async Task RunAsync_WritesCloudUploadAuditEvent()
    {
        BackupUploadHooks.Clear();
        var events = new List<AuditEvent>();
        var previousSink = AuditHub.Sink;
        AuditHub.Sink = events.Add;
        try
        {
            BackupUploadHooks.Register(new RecordingUploader("s3"));
            await BackupUploadHooks.RunAsync(
                new BackupUploadContext(@"C:\backup.dt", "Бухгалтерия", "Ночная", "scenario"));

            var audit = Assert.Single(events.Where(e => e.Action == AuditActions.CloudUpload));
            Assert.True(audit.Success);
            Assert.Equal("Бухгалтерия", audit.Target);
            Assert.Contains("s3", audit.Details);
        }
        finally
        {
            AuditHub.Sink = previousSink;
            BackupUploadHooks.Clear();
        }
    }
}
