using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.Audit;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Публичные точки расширения для модулей ПРО (2.0): журнал аудита (AuditHub),
/// внешние пункты меню «Утилиты», строки модулей в локализации.
/// </summary>
[Collection("ProLicensing")]
public sealed class AuditHubTests : IDisposable
{
    public void Dispose()
    {
        AuditHub.Sink = null;
        AuditHub.DefaultSource = AuditSources.Ui;
    }

    [Fact]
    public void Write_WithoutSink_DoesNothing()
    {
        AuditHub.Sink = null;
        AuditHub.Write(AuditActions.ConfigUpdate, "База", true, "x");
    }

    [Fact]
    public void Write_DeliversEvent_WithDefaultSource_AndMasksPasswords()
    {
        var events = new List<AuditEvent>();
        AuditHub.Sink = events.Add;
        AuditHub.DefaultSource = AuditSources.Cli;

        AuditHub.Write(AuditActions.ServerInfobaseDrop, "srv:1545", false, "rac --cluster-pwd=Secret123 infobase drop");

        var e = Assert.Single(events);
        Assert.Equal(AuditSources.Cli, e.Source);
        Assert.Equal(AuditActions.ServerInfobaseDrop, e.Action);
        Assert.False(e.Success);
        Assert.False(string.IsNullOrEmpty(e.Actor));
        Assert.DoesNotContain("Secret123", e.Details);
    }

    [Fact]
    public void Write_SinkThrows_IsSwallowed()
    {
        AuditHub.Sink = _ => throw new InvalidOperationException("диск полон");
        AuditHub.Write(AuditActions.TestDeploy, "Тест", true);
    }

    [Fact]
    public void ExternalMenuItem_RegisteredAndExecuted()
    {
        var executed = 0;
        var item = new UtilitiesMenuItem("Test.External." + Guid.NewGuid().ToString("N"), "Pro.MenuItem",
            UtilitiesMenuCatalog.GroupInformation, "History", "IconHistory", "#8B5CF6",
            UtilitiesMenuCommandKind.External, isNew: true);
        UtilitiesMenuCatalog.RegisterExternal(item, () => executed++);

        Assert.Same(item, UtilitiesMenuCatalog.Find(item.Id));
        Assert.Contains(item, UtilitiesMenuCatalog.All);
        Assert.True(UtilitiesMenuCatalog.ExecuteExternal(item.Id));
        Assert.Equal(1, executed);
        Assert.False(UtilitiesMenuCatalog.ExecuteExternal("нет-такого"));
    }

    [Fact]
    public void ModuleStrings_UsedOnlyForMissingKeys()
    {
        var key = "ProModule.Test." + Guid.NewGuid().ToString("N");
        Assert.Equal(key, LocalizationManager.T(key));
        LocalizationManager.Instance.RegisterModuleStrings("ru", new Dictionary<string, string> { [key] = "Строка модуля" });
        LocalizationManager.Instance.RegisterModuleStrings("en", new Dictionary<string, string> { [key] = "Module string" });
        var text = LocalizationManager.T(key);
        Assert.True(text is "Строка модуля" or "Module string");
    }
}
