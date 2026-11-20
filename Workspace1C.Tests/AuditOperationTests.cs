using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Audit;
using Xunit;

namespace Workspace1C.Tests;

[Collection("ProLicensing")]
public sealed class AuditOperationTests
{
    [Fact]
    public async Task Unlock_RecordsOutcomeAndUsesTheOperationSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "w1c-unlock-audit-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        var previousSink = AuditHub.Sink;
        var previousSource = AuditHub.DefaultSource;
        try
        {
            var events = new List<AuditEvent>();
            AuditHub.Sink = events.Add;
            AuditHub.DefaultSource = AuditSources.Ui;
            var service = new FileBaseSessionLock();
            var lockFile = Path.Combine(path, FileBaseSessionLock.CdnFileName);
            File.WriteAllText(lockFile, "");

            var success = await AuditHub.WithSourceAsync(AuditSources.Scheduler, async () =>
            {
                await Task.Yield();
                return service.Unlock(new Infobase
                {
                    Name = "Test database",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
                });
            });
            var failure = service.Unlock(new Infobase
            {
                Name = "Invalid database",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = "" }
            });

            Assert.True(success.Success);
            Assert.False(File.Exists(lockFile));
            Assert.False(failure.Success);
            Assert.Collection(events,
                entry =>
                {
                    Assert.Equal(AuditActions.SessionUnlock, entry.Action);
                    Assert.Equal(AuditSources.Scheduler, entry.Source);
                    Assert.Equal("Test database", entry.Target);
                    Assert.True(entry.Success);
                },
                entry =>
                {
                    Assert.Equal(AuditActions.SessionUnlock, entry.Action);
                    Assert.Equal(AuditSources.Ui, entry.Source);
                    Assert.Equal("Invalid database", entry.Target);
                    Assert.False(entry.Success);
                });
        }
        finally
        {
            AuditHub.Sink = previousSink;
            AuditHub.DefaultSource = previousSource;
            Directory.Delete(path, recursive: true);
        }
    }
}
