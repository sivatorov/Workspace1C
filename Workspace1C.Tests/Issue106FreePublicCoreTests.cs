using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Workspace1C;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #106 «Массовая загрузка cf просит оплату»: по политике docs/pro/PRO-EDITION.md
/// версии 1.0.x остаются под MIT навсегда — функции публичного ядра
/// (<see cref="ProFeatures.IsInPublicCore"/>: ServerAlerts…MassLoadCf) доступны
/// без ключа в ветке 1.x (<see cref="EditionService.Has"/>), а закрытие лицензией
/// появляется только со сборок 2.x. Страж: в сценарии 2.x
/// (<see cref="VersionInfo.MajorOverride"/> = 2) поведение прежнее — public core
/// требует ключ, бейдж «· ПРО» возвращается в меню.
/// </summary>
[Collection("ProLicensing")]
public sealed class Issue106FreePublicCoreTests : IDisposable
{
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_issue106_" + Guid.NewGuid().ToString("N"));

    public Issue106FreePublicCoreTests()
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
        VersionInfo.MajorOverride = null;
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

    // ---------------------------------------------------------------- 1.x: public core бесплатен

    /// <summary>В 1.x (override = 1) все функции публичного ядра доступны без ключа.</summary>
    [Fact]
    public void Major1_PublicCore_AvailableWithoutKey()
    {
        VersionInfo.MajorOverride = 1;
        EditionService.Reset();
        try
        {
            Assert.False(EditionService.IsPro); // ключа нет
            foreach (var feature in ProFeatures.All.Where(ProFeatures.IsInPublicCore))
                Assert.True(EditionService.Has(feature), feature + " должен быть бесплатен в 1.x");
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>
    /// В 1.x <see cref="ProGate.Ensure(MassLoadCf)"/> пропускает без ключа и НЕ показывает
    /// окно «Лицензия» — целевой эффект issue #106.
    /// </summary>
    [Fact]
    public void Major1_EnsureMassLoadCf_PassesWithoutKey_NoLicenseDialog()
    {
        VersionInfo.MajorOverride = 1;
        EditionService.Reset();
        var previous = ProGate.ShowLicenseDialog;
        var dialogShown = 0;
        ProGate.ShowLicenseDialog = _ => dialogShown++;
        try
        {
            Assert.True(ProGate.Ensure(ProFeature.MassLoadCf));
            Assert.Equal(0, dialogShown);

            // Гейт для функции публичного ядра из планировщика (TestDeploy) тоже свободен.
            var feature = ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy);
            Assert.Equal(ProFeature.TestDeploy, feature);
            Assert.True(ProGate.Ensure(feature!.Value));
            Assert.Equal(0, dialogShown);
        }
        finally
        {
            ProGate.ShowLicenseDialog = previous;
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>В 1.x функции вне публичного ядра (новые 2.x) по-прежнему закрыты без ключа.</summary>
    [Fact]
    public void Major1_NonCoreFeatures_StillDeniedWithoutKey()
    {
        VersionInfo.MajorOverride = 1;
        EditionService.Reset();
        try
        {
            foreach (var feature in ProFeatures.All.Where(f => !ProFeatures.IsInPublicCore(f)))
                Assert.False(EditionService.Has(feature), feature + " не должен быть бесплатен без ключа");
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>
    /// В 1.x бейдж «· ПРО» не показывается на пунктах меню с функциями public core
    /// (контекстное меню и меню «Утилиты») — он производен от <see cref="EditionService.Has"/>.
    /// </summary>
    [Fact]
    public void Major1_Menus_HideProBadgeOnPublicCoreItems()
    {
        VersionInfo.MajorOverride = 1;
        EditionService.Reset();
        try
        {
            Assert.False(EditionService.IsPro);
            var badge = Localization.LocalizationManager.T("Pro.Badge");

            foreach (var id in new[] { "TestDeploy", "MassLoadCf" })
            {
                var item = ContextMenuCatalog.Find(id);
                Assert.NotNull(item);
                Assert.DoesNotContain("· " + badge, item!.DisplayText(), StringComparison.Ordinal);
            }

            // Public core в 1.x бесплатен — бейдж исчезает; функции вне ядра
            // (например AuditLog) остаются закрытыми и с бейджем.
            var utilityCoreItems = UtilitiesMenuCatalog.All
                .Where(i => i.RequiredFeature is { } f && ProFeatures.IsInPublicCore(f)).ToList();
            Assert.NotEmpty(utilityCoreItems); // в каталоге есть пункты public core
            foreach (var item in utilityCoreItems)
                Assert.DoesNotContain("· " + badge, item.DisplayText(), StringComparison.Ordinal);

            var auditLogItem = UtilitiesMenuCatalog.All
                .FirstOrDefault(i => i.RequiredFeature == ProFeature.AuditLog);
            if (auditLogItem is not null)
                Assert.Contains("· " + badge, auditLogItem.DisplayText(), StringComparison.Ordinal);
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    // ---------------------------------------------------------------- 2.x: поведение прежнее

    /// <summary>В сценарии 2.x (override = 2) без ключа public core снова закрыт.</summary>
    [Fact]
    public void Major2_PublicCore_RequiresKey()
    {
        VersionInfo.MajorOverride = 2;
        EditionService.Reset();
        try
        {
            Assert.False(EditionService.IsPro);
            foreach (var feature in ProFeatures.All.Where(ProFeatures.IsInPublicCore))
                Assert.False(EditionService.Has(feature), feature + " в 2.x требует ключ");

            // Гейт отказывает и показывает окно «Лицензия».
            ProFeature? shown = null;
            var previous = ProGate.ShowLicenseDialog;
            ProGate.ShowLicenseDialog = f => shown = f;
            try
            {
                Assert.False(ProGate.Ensure(ProFeature.MassLoadCf));
                Assert.Equal(ProFeature.MassLoadCf, shown);
            }
            finally
            {
                ProGate.ShowLicenseDialog = previous;
            }
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>В сценарии 2.x с действительным ключом public core снова доступен.</summary>
    [Fact]
    public void Major2_PublicCore_UnlockedWithValidKey()
    {
        VersionInfo.MajorOverride = 2;
        EditionService.Reset();
        try
        {
            var result = EditionService.Activate(Issue(2, MachineIdProvider.Current));
            Assert.True(result.IsValid);
            Assert.True(EditionService.IsPro);
            foreach (var feature in ProFeatures.All.Where(ProFeatures.IsInPublicCore))
                Assert.True(EditionService.Has(feature), feature + " должен быть доступен с ключом в 2.x");
            Assert.True(ProGate.Ensure(ProFeature.MassLoadCf));
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>В сценарии 2.x бейдж «· ПРО» возвращается на пункты public core.</summary>
    [Fact]
    public void Major2_Menus_ShowProBadgeOnPublicCoreItems()
    {
        VersionInfo.MajorOverride = 2;
        EditionService.Reset();
        try
        {
            Assert.False(EditionService.IsPro);
            var badge = Localization.LocalizationManager.T("Pro.Badge");

            foreach (var id in new[] { "TestDeploy", "MassLoadCf" })
            {
                var item = ContextMenuCatalog.Find(id);
                Assert.NotNull(item);
                Assert.Contains("· " + badge, item!.DisplayText(), StringComparison.Ordinal);
            }

            // В 2.x бейдж возвращается на все ПРО-пункты, включая функции вне ядра.
            var utilityProItems = UtilitiesMenuCatalog.All.Where(i => i.RequiredFeature is not null).ToList();
            Assert.NotEmpty(utilityProItems);
            foreach (var item in utilityProItems)
                Assert.Contains("· " + badge, item.DisplayText(), StringComparison.Ordinal);
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }
}
