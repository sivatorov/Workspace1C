using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Workspace1C;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Отметка «ПРО» в контекстном меню базы (аудит активации, Дефект №3): пункты,
/// выполняющие ПРО-функции («Развернуть тестовую копию», «Массовая загрузка cf…»),
/// имеют <see cref="ContextMenuItem.RequiredFeature"/>, а
/// <see cref="ContextMenuItem.DisplayText"/> добавляет суффикс «· ПРО» для
/// недоступной функции — тот же механизм, что в меню «Утилиты»
/// (<see cref="UtilitiesMenuItem"/>).
/// </summary>
[Collection("ProLicensing")]
public sealed class ContextMenuProBadgeTests : IDisposable
{
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_ctxbadge_" + Guid.NewGuid().ToString("N"));

    public ContextMenuProBadgeTests()
    {
        _publicKey = Convert.ToBase64String(_signer.ExportSubjectPublicKeyInfo());
        Directory.CreateDirectory(_dir);
        EditionService.DirectoryOverride = _dir;
        LicenseVerifier.PublicKeyOverride = _publicKey;
        MachineIdProvider.RawIdOverride = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        EditionService.Reset();
    }

    public void Dispose()
    {
        EditionService.DirectoryOverride = null;
        LicenseVerifier.PublicKeyOverride = null;
        MachineIdProvider.RawIdOverride = null;
        EditionService.Reset();
        _signer.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* временный каталог */ }
    }

    private string Issue(int major, string machine, string edition = "pro")
    {
        var payload = new LicensePayload
        {
            Id = "test-1",
            Edition = edition,
            Major = major,
            Machine = machine,
            Owner = "ООО Ромашка",
            Email = "test@example.com",
            Issued = "2026-11-08"
        };
        var signed = LicenseKey.BuildSignedPart(payload);
        var signature = _signer.SignData(Encoding.UTF8.GetBytes(signed), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenseKey.Compose(signed, signature);
    }

    [Fact]
    public void Catalog_ProItems_HaveRequiredFeature()
    {
        var testDeploy = ContextMenuCatalog.Find("TestDeploy");
        var massLoadCf = ContextMenuCatalog.Find("MassLoadCf");

        Assert.NotNull(testDeploy);
        Assert.NotNull(massLoadCf);
        Assert.Equal(ProFeature.TestDeploy, testDeploy!.RequiredFeature);
        Assert.Equal(ProFeature.MassLoadCf, massLoadCf!.RequiredFeature);
    }

    [Fact]
    public void Catalog_OnlyProItems_HaveRequiredFeature()
    {
        // Из контекстного меню ПРО-функции — только TestDeploy и MassLoadCf
        // (ProGate-проверки в командах: MainViewModel.Commands/Tools и Avalonia-аналоги).
        var proIds = new[] { "TestDeploy", "MassLoadCf" };
        foreach (var item in ContextMenuCatalog.All)
        {
            if (Array.IndexOf(proIds, item.Id) >= 0)
                continue;
            Assert.True(item.RequiredFeature is null, $"Пункт {item.Id} неожиданно требует функцию ПРО");
        }
    }

    [Fact]
    public void DisplayText_WithoutLicense_ShowsProBadge()
    {
        Assert.False(EditionService.IsPro);

        var badge = Localization.LocalizationManager.T("Pro.Badge");
        var testDeploy = ContextMenuCatalog.Find("TestDeploy")!;
        var massLoadCf = ContextMenuCatalog.Find("MassLoadCf")!;

        // Тот же формат суффикса, что в UtilitiesMenuItem.DisplayText: «  · ПРО».
        Assert.EndsWith("  · " + badge, testDeploy.DisplayText(), StringComparison.Ordinal);
        Assert.EndsWith("  · " + badge, massLoadCf.DisplayText(), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayText_WithLicense_HidesProBadge()
    {
        var result = EditionService.Activate(Issue(VersionInfo.Major(), MachineIdProvider.Current));
        Assert.True(result.IsValid);
        Assert.True(EditionService.Has(ProFeature.TestDeploy));
        Assert.True(EditionService.Has(ProFeature.MassLoadCf));

        var badge = Localization.LocalizationManager.T("Pro.Badge");
        var testDeploy = ContextMenuCatalog.Find("TestDeploy")!;
        var massLoadCf = ContextMenuCatalog.Find("MassLoadCf")!;

        Assert.DoesNotContain("· " + badge, testDeploy.DisplayText(), StringComparison.Ordinal);
        Assert.DoesNotContain("· " + badge, massLoadCf.DisplayText(), StringComparison.Ordinal);
    }
}
