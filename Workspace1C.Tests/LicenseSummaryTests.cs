using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Workspace1C;
using Workspace1C.Services.Licensing;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Сводка о лицензии для раздела «О программе» (<see cref="LicenseSummary"/>):
/// редакция, владелец, строка о ключе, код компьютера и текст статуса.
/// Тесты не зависят от машины и порядка запуска: ключ подписывается временной парой,
/// а код компьютера вычисляется из фиксированного сырого идентификатора ОС.
/// </summary>
[Collection("ProLicensing")]
public sealed class LicenseSummaryTests : IDisposable
{
    private const string MachineRawId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    /// <summary>Русские тексты, нужные сводке (детерминированный транслятор для тестов).</summary>
    private static readonly Dictionary<string, string> Ru = new(StringComparer.Ordinal)
    {
        ["Pro.EditionFree"] = "Редакция: бесплатная",
        ["Pro.EditionPro"] = "Редакция: ПРО",
        ["Pro.OwnerFormat"] = "Владелец: {0} · ключ для версий {1}.x · выпущен {2}",
        ["Settings.About.LicenseKeyInfo"] = "Ключ: для версий {0}.x · выпущен {1}",
        ["Pro.MachineCodeUnavailable"] = "Не удалось определить код компьютера",
        ["Pro.Status.WrongMachine"] = "Ключ выпущен для другого компьютера.",
        ["Pro.Status.WrongMajorVersion"] = "Ключ выпущен для другой версии программы.",
        ["Pro.Status.WrongMajorVersionFormat"] = "Ключ выпущен для версий {0}.x и не подходит к этой версии программы."
    };

    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _machine = MachineIdProvider.FromRawId(MachineRawId);

    public LicenseSummaryTests()
    {
        _publicKey = Convert.ToBase64String(_signer.ExportSubjectPublicKeyInfo());
    }

    public void Dispose() => _signer.Dispose();

    private static string Translate(string key) => Ru.TryGetValue(key, out var value) ? value : key;

    private string Issue(int major, string machine)
    {
        var payload = new LicensePayload
        {
            Id = "summary-test",
            Edition = "pro",
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

    private LicenseSummary Summary(LicenseCheckResult result, bool hasProModules = true) =>
        LicenseSummary.From(result, _machine, hasProModules, Translate);

    [Fact]
    public void WithoutKey_IsFree_AndHasNoOwnerKeyInfoOrStatus()
    {
        var result = LicenseVerifier.Verify(null, VersionInfo.Major(), _machine, _publicKey);
        var summary = Summary(result);

        Assert.Equal(LicenseStatus.None, result.Status);
        Assert.False(summary.IsPro);
        Assert.False(summary.HasKey);
        Assert.Equal(Ru["Pro.EditionFree"], summary.EditionText);
        Assert.Equal(string.Empty, summary.OwnerText);
        Assert.Equal(string.Empty, summary.KeyInfoText);
        Assert.Equal(string.Empty, summary.StatusText);
        Assert.Equal(_machine, summary.MachineCode);
        Assert.Equal(_machine, summary.MachineCodeText);
    }

    [Fact]
    public void ValidKey_IsPro_WithOwnerAndKeyInfo()
    {
        var major = VersionInfo.Major();
        var result = LicenseVerifier.Verify(Issue(major, _machine), major, _machine, _publicKey);
        var summary = Summary(result);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.True(summary.IsPro);
        Assert.True(summary.HasKey);
        Assert.Equal(Ru["Pro.EditionPro"], summary.EditionText);
        Assert.Contains("ООО Ромашка", summary.OwnerText);
        Assert.Contains($"{major}.x", summary.KeyInfoText);
        Assert.Contains("2026-11-08", summary.KeyInfoText);
        Assert.Equal(string.Empty, summary.StatusText);
    }

    [Fact]
    public void KeyForAnotherMachine_IsNotPro_WithWrongMachineStatus()
    {
        var major = VersionInfo.Major();
        var result = LicenseVerifier.Verify(
            Issue(major, "AAAA-BBBB-CCCC-DDDD"), major, _machine, _publicKey);
        var summary = Summary(result);

        Assert.Equal(LicenseStatus.WrongMachine, result.Status);
        Assert.False(summary.IsPro);
        Assert.True(summary.HasKey);
        Assert.Equal(Ru["Pro.Status.WrongMachine"], summary.StatusText);
        Assert.Equal(string.Empty, summary.OwnerText);
        Assert.Equal(string.Empty, summary.KeyInfoText);
    }

    [Fact]
    public void CommunityBuild_IsDetected()
    {
        var result = LicenseVerifier.Verify(null, VersionInfo.Major(), _machine, _publicKey);

        Assert.True(Summary(result, hasProModules: false).IsCommunityBuild);
        Assert.False(Summary(result, hasProModules: true).IsCommunityBuild);
    }

    [Fact]
    public void EmptyMachineCode_UsesUnavailableText()
    {
        var summary = LicenseSummary.From(
            new LicenseCheckResult(LicenseStatus.None, null), string.Empty, true, Translate);

        Assert.Equal(string.Empty, summary.MachineCode);
        Assert.Equal(Ru["Pro.MachineCodeUnavailable"], summary.MachineCodeText);
    }

    [Fact]
    public void WrongMajorVersion_UsesFormatWithKeyMajor()
    {
        var major = VersionInfo.Major();
        var result = LicenseVerifier.Verify(Issue(major, _machine), major + 1, _machine, _publicKey);
        var summary = Summary(result);

        Assert.Equal(LicenseStatus.WrongMajorVersion, result.Status);
        Assert.False(summary.IsPro);
        Assert.Contains($"{major}.x", summary.StatusText);
    }
}
