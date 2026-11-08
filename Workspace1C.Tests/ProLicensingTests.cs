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
/// Лицензирование редакции ПРО 2.x (docs/pro/PRO-EDITION.md): код компьютера, формат
/// ключа, проверка подписи/версии/компьютера, активация и шлюз функций.
/// </summary>
[Collection("ProLicensing")]
public sealed class ProLicensingTests : IDisposable
{
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_lic_" + Guid.NewGuid().ToString("N"));

    public ProLicensingTests()
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

    // ---------------------------------------------------------------- код компьютера

    [Fact]
    public void MachineCode_IsStable_GroupedAndCrockfordBase32()
    {
        var a = MachineIdProvider.FromRawId("ABC-123");
        var b = MachineIdProvider.FromRawId("  abc-123 ");
        Assert.Equal(a, b);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", a);
        Assert.NotEqual(a, MachineIdProvider.FromRawId("ABC-124"));
    }

    [Fact]
    public void MachineCode_EmptyRawId_IsEmpty()
    {
        Assert.Equal(string.Empty, MachineIdProvider.FromRawId(null));
        Assert.Equal(string.Empty, MachineIdProvider.FromRawId("   "));
    }

    [Fact]
    public void MachineCode_Comparison_IgnoresFormattingAndLookalikes()
    {
        Assert.True(MachineIdProvider.AreEqual("7K2M-QX9P-4RTA-0HCW", "7k2mqx9p4rtaohcw"));
        Assert.False(MachineIdProvider.AreEqual("", ""));
        Assert.False(MachineIdProvider.AreEqual("7K2M-QX9P-4RTA-0HCW", "7K2M-QX9P-4RTA-0HCX"));
    }

    // ---------------------------------------------------------------- формат ключа

    [Fact]
    public void Key_RoundTrip_IgnoresWhitespaceInside()
    {
        var text = Issue(2, MachineIdProvider.Current);
        var broken = text.Substring(0, 20) + "\r\n  " + text.Substring(20);
        Assert.True(LicenseKey.TryParse(broken, out var key));
        Assert.Equal(text, key!.Text);
        Assert.Equal("ООО Ромашка", key.Payload.Owner);
        Assert.Equal(2, key.Payload.Major);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("W1C1.abc.def")]
    [InlineData("W1C2.!!!.???")]
    public void Key_Malformed_IsRejected(string? text)
    {
        Assert.False(LicenseKey.TryParse(text, out _));
    }

    // ---------------------------------------------------------------- проверка

    [Fact]
    public void Verify_ValidKey()
    {
        var machine = MachineIdProvider.Current;
        var result = LicenseVerifier.Verify(Issue(2, machine), 2, machine, _publicKey);
        Assert.Equal(LicenseStatus.Valid, result.Status);
    }

    [Fact]
    public void Verify_Statuses()
    {
        var machine = MachineIdProvider.Current;
        Assert.Equal(LicenseStatus.None, LicenseVerifier.Verify("  ", 2, machine, _publicKey).Status);
        Assert.Equal(LicenseStatus.Malformed, LicenseVerifier.Verify("abc", 2, machine, _publicKey).Status);
        Assert.Equal(LicenseStatus.NotConfigured, LicenseVerifier.Verify(Issue(2, machine), 2, machine, "").Status);
        Assert.Equal(LicenseStatus.WrongMajorVersion, LicenseVerifier.Verify(Issue(2, machine), 3, machine, _publicKey).Status);
        Assert.Equal(LicenseStatus.WrongMachine, LicenseVerifier.Verify(Issue(2, "AAAA-BBBB-CCCC-DDDD"), 2, machine, _publicKey).Status);
        Assert.Equal(LicenseStatus.WrongEdition, LicenseVerifier.Verify(Issue(2, machine, "enterprise"), 2, machine, _publicKey).Status);
    }

    [Fact]
    public void Verify_TamperedPayload_BadSignature()
    {
        var machine = MachineIdProvider.Current;
        var text = Issue(2, machine);
        Assert.True(LicenseKey.TryParse(text, out var key));
        key!.Payload.Owner = "Взломщик";
        var forged = LicenseKey.Compose(LicenseKey.BuildSignedPart(key.Payload), key.Signature);
        Assert.Equal(LicenseStatus.BadSignature, LicenseVerifier.Verify(forged, 2, machine, _publicKey).Status);
    }

    [Fact]
    public void Verify_KeyFromAnotherIssuer_BadSignature()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherPublic = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        var machine = MachineIdProvider.Current;
        Assert.Equal(LicenseStatus.BadSignature, LicenseVerifier.Verify(Issue(2, machine), 2, machine, otherPublic).Status);
    }

    // ---------------------------------------------------------------- редакция

    [Fact]
    public void Edition_WithoutKey_IsFree_AndGateDenies()
    {
        Assert.False(EditionService.IsPro);
        Assert.False(EditionService.Has(ProFeature.ServerAlerts));
        ProFeature? shown = null;
        var previous = ProGate.ShowLicenseDialog;
        ProGate.ShowLicenseDialog = f => shown = f;
        try
        {
            Assert.False(ProGate.Ensure(ProFeature.TelegramBot));
            Assert.Equal(ProFeature.TelegramBot, shown);
        }
        finally
        {
            ProGate.ShowLicenseDialog = previous;
        }
    }

    [Fact]
    public void Edition_Activate_SavesKey_AndUnlocksPublicCoreFeatures()
    {
        var key = Issue(VersionInfo.Major(), MachineIdProvider.Current);
        var result = EditionService.Activate(key);

        Assert.True(result.IsValid);
        Assert.True(File.Exists(EditionService.LicenseFilePath));
        EditionService.Reset(); // перечитать с диска
        Assert.True(EditionService.IsPro);
        Assert.True(EditionService.Has(ProFeature.ServerAlerts));
        Assert.Equal(EditionService.HasProModules, EditionService.Has(ProFeature.Pipelines));
        Assert.True(ProGate.Ensure(ProFeature.MassLoadCf));

        EditionService.Deactivate();
        Assert.False(EditionService.IsPro);
        Assert.False(File.Exists(EditionService.LicenseFilePath));
    }

    [Fact]
    public void Edition_InvalidKey_IsNotSaved()
    {
        var result = EditionService.Activate(Issue(VersionInfo.Major(), "AAAA-BBBB-CCCC-DDDD"));
        Assert.Equal(LicenseStatus.WrongMachine, result.Status);
        Assert.False(File.Exists(EditionService.LicenseFilePath));
        Assert.False(EditionService.IsPro);
    }

    [Fact]
    public void ProFeatures_PublicCoreSplit()
    {
        Assert.True(ProFeatures.IsInPublicCore(ProFeature.ServerAlerts));
        Assert.True(ProFeatures.IsInPublicCore(ProFeature.MassLoadCf));
        Assert.False(ProFeatures.IsInPublicCore(ProFeature.Pipelines));
        Assert.False(ProFeatures.IsInPublicCore(ProFeature.CloudBackup));
    }

    [Fact]
    public void Cli_LicenseCommands_AreParsed()
    {
        var info = CliArgs.Parse(new[] { "--license-info", "--json" });
        Assert.Equal(CliCommandKind.LicenseInfo, info!.Kind);
        Assert.True(info.Options.Json);

        var activate = CliArgs.Parse(new[] { "--activate", "W1C2.abc.def" });
        Assert.Equal(CliCommandKind.Activate, activate!.Kind);
        Assert.Equal("W1C2.abc.def", activate.Options.Text);
    }
}

/// <summary>Тесты лицензирования меняют статическое состояние — выполняются последовательно.</summary>
[CollectionDefinition("ProLicensing", DisableParallelization = true)]
public sealed class ProLicensingCollection
{
}

/// <summary>Встроенный открытый ключ проверки — корректный ключ ECDSA P-256.</summary>
public sealed class ProLicensingPublicKeyTests
{
    [Fact]
    public void EmbeddedPublicKey_IsValidP256()
    {
        Assert.False(string.IsNullOrWhiteSpace(LicenseVerifier.PublicKey));
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(LicenseVerifier.PublicKey), out _);
        Assert.Equal(256, ecdsa.KeySize);
    }
}
