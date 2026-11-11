using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Workspace1C;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;
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

    /// <summary>
    /// Без ключа в ветке 1.x функции публичного ядра бесплатны (docs/pro/PRO-EDITION.md,
    /// issue #106), а функции вне ядра закрыты: гейт отказывает и показывает окно «Лицензия».
    /// </summary>
    [Fact]
    public void Edition_WithoutKey_IsFree_AndGateDenies()
    {
        Assert.False(EditionService.IsPro);
        // Public core бесплатен в 1.x (VersionInfo.Major() = 1 для сборок 1.0.x).
        Assert.True(EditionService.Has(ProFeature.ServerAlerts));
        Assert.True(EditionService.Has(ProFeature.MassLoadCf));
        // Функции вне публичного ядра (новые 2.x) остаются закрытыми без ключа.
        Assert.False(EditionService.Has(ProFeature.Pipelines));
        ProFeature? shown = null;
        var previous = ProGate.ShowLicenseDialog;
        ProGate.ShowLicenseDialog = f => shown = f;
        try
        {
            Assert.False(ProGate.Ensure(ProFeature.Pipelines));
            Assert.Equal(ProFeature.Pipelines, shown);
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

    /// <summary>
    /// Дефект №4 (аудит активации): UI подписывается на <see cref="EditionService.Changed"/>,
    /// чтобы пересобрать меню «Утилиты» с актуальными бейджами «· ПРО». Событие обязано
    /// подниматься и при успешной активации, и при деактивации — иначе UI не узнает о смене.
    /// </summary>
    [Fact]
    public void Edition_Changed_IsRaisedOnActivateAndDeactivate()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        EditionService.Changed += handler;
        try
        {
            // Активация без события (невалидный ключ, ключ не сохраняется) и с событием (валидный).
            EditionService.Activate("hello");
            Assert.Equal(0, raised);
            EditionService.Activate(Issue(VersionInfo.Major(), MachineIdProvider.Current));
            Assert.Equal(1, raised);

            EditionService.Deactivate();
            Assert.Equal(2, raised);
        }
        finally
        {
            EditionService.Changed -= handler;
        }
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

    // ---------------------------------------------------------------- планировщик (дефект №5)

    /// <summary>
    /// Дефект №5 (аудит активации): создание задания планировщика типа TestDeploy
    /// гейтится лицензией при выборе/сохранении в редакторе задания (ScheduledTaskEditWindow,
    /// гейт по <see cref="ScheduledTaskEditViewModel.RequiredProFeature"/>), а не только
    /// при выполнении (SchedulerService.cs). TestDeploy — функция публичного ядра и в ветке
    /// 1.x бесплатна (issue #106), поэтому отказ без лицензии проверяем для сценария 2.x
    /// (<see cref="VersionInfo.MajorOverride"/> = 2): гейт возвращает false и показывает
    /// окно «Лицензия».
    /// </summary>
    [Fact]
    public void SchedulerTestDeploy_CreationGate_DeniedWithoutLicense()
    {
        Assert.False(EditionService.IsPro);
        VersionInfo.MajorOverride = 2; // поведение 2.x: public core снова требует ключ
        EditionService.Reset();
        try
        {
            ProFeature? shown = null;
            var previous = ProGate.ShowLicenseDialog;
            ProGate.ShowLicenseDialog = f => shown = f;
            try
            {
                var feature = ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy);
                Assert.NotNull(feature);
                Assert.False(ProGate.Ensure(feature!.Value));
                Assert.Equal(ProFeature.TestDeploy, shown);
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

    /// <summary>Дефект №5: при активной лицензии выбор/сохранение типа TestDeploy разрешены.</summary>
    [Fact]
    public void SchedulerTestDeploy_CreationGate_AllowedWithLicense()
    {
        var result = EditionService.Activate(Issue(VersionInfo.Major(), MachineIdProvider.Current));
        Assert.True(result.IsValid);

        var feature = ScheduledTaskEditViewModel.RequiredProFeature(ScheduledTaskKind.TestDeploy);
        Assert.NotNull(feature);
        Assert.True(EditionService.Has(feature!.Value));
        Assert.True(ProGate.Ensure(feature.Value));
    }

    // ------------------------------------------------- реализованность функций (аудит лицензирования, 2026-11)

    /// <summary>
    /// Аудит лицензирования: реализованы только публичное ядро (ServerAlerts…MassLoadCf)
    /// и модуль AuditLog; остальные функции перечня 2.x существуют только в enum и
    /// должны помечаться «в разработке».
    /// </summary>
    [Fact]
    public void ProFeatures_ImplementedSet_MatchesAudit()
    {
        // Порядок ключа статуса: реализованные — «доступна», нереализованные — «в разработке».
        Assert.Equal("Pro.FeatureStatus.Available", ProFeatures.StatusKey(ProFeature.ServerAlerts));
        Assert.Equal("Pro.FeatureStatus.Available", ProFeatures.StatusKey(ProFeature.AuditLog));
        Assert.Equal("Pro.FeatureStatus.InDevelopment", ProFeatures.StatusKey(ProFeature.Pipelines));
        Assert.Equal("Pro.FeatureStatus.InDevelopment", ProFeatures.StatusKey(ProFeature.CloudBackup));

        ProFeature[] implemented =
        {
            ProFeature.ServerAlerts, ProFeature.TelegramBot, ProFeature.Mcp,
            ProFeature.ClusterMigration, ProFeature.TestDeploy, ProFeature.MassLoadCf,
            ProFeature.AuditLog
        };
        ProFeature[] planned =
        {
            ProFeature.Pipelines, ProFeature.MultiBaseQuery, ProFeature.EnvironmentDiff,
            ProFeature.AutoTests, ProFeature.PerformanceTrends, ProFeature.CloudBackup
        };

        foreach (var f in implemented)
            Assert.True(ProFeatures.IsImplemented(f), f + " должен быть реализован");
        foreach (var f in planned)
            Assert.False(ProFeatures.IsImplemented(f), f + " должен быть помечен «в разработке»");

        // Перечень покрыт полностью: реализованные + планируемые = все функции.
        Assert.Equal(ProFeatures.All.Count, implemented.Length + planned.Length);
    }

    /// <summary>
    /// Аудит лицензирования: список функций окна «Лицензия» показывает реализованные
    /// функции как «доступна», нереализованные — «в разработке»; признак реализованности
    /// не меняет гейт <see cref="EditionService.Has"/>. Сценарий 2.x
    /// (<see cref="VersionInfo.MajorOverride"/> = 2): без ключа ни одна функция
    /// не включена — в 1.x функции публичного ядра бесплатны (issue #106).
    /// </summary>
    [Fact]
    public void LicenseViewModel_FeatureRows_MarkInDevelopment()
    {
        Assert.False(EditionService.IsPro);
        VersionInfo.MajorOverride = 2; // поведение 2.x: public core снова требует ключ
        EditionService.Reset();
        try
        {
            var available = LocalizationManager.T("Pro.FeatureStatus.Available");
            var inDevelopment = LocalizationManager.T("Pro.FeatureStatus.InDevelopment");

            var vm = new LicenseViewModel();
            Assert.Equal(ProFeatures.All.Count, vm.Features.Count);
            foreach (var row in vm.Features)
            {
                Assert.Equal(!ProFeatures.IsImplemented(row.Feature), row.InDevelopment);
                Assert.Equal(
                    LocalizationManager.T(ProFeatures.StatusKey(row.Feature)),
                    row.Status);
                if (ProFeatures.IsImplemented(row.Feature))
                    Assert.Equal(available, row.Status);
                else
                    Assert.Equal(inDevelopment, row.Status);
                Assert.Contains(row.Status, row.Display);
                Assert.Equal("—", row.Mark); // без лицензии функция не включена
            }
        }
        finally
        {
            VersionInfo.MajorOverride = null;
            EditionService.Reset();
        }
    }

    /// <summary>
    /// Аудит лицензирования: в <c>--license-info</c> реализованные функции показываются
    /// булевым значением доступности, нереализованные — строкой статуса «в разработке».
    /// </summary>
    [Fact]
    public void CliLicenseInfo_FeaturesMap_MarksInDevelopmentAsString()
    {
        var map = ProFeatures.All.ToDictionaryCompat();
        Assert.Equal(ProFeatures.All.Count, map.Count);
        foreach (var f in ProFeatures.All)
        {
            if (ProFeatures.IsImplemented(f))
            {
                Assert.IsType<bool>(map[f.ToString()]);
                Assert.Equal(EditionService.Has(f), (bool)map[f.ToString()]!);
            }
            else
            {
                Assert.Equal(LocalizationManager.T("Pro.FeatureStatus.InDevelopment"),
                    map[f.ToString()]);
            }
        }
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

/// <summary>
/// Аудит лицензирования (2026-11): кнопка «Купить» и статусы функций должны быть
/// локализованы в обоих встроенных языках; Pro.HowToBuy должен указывать цену.
/// Включён в последовательную коллекцию лицензирования, чтобы не менять параллельный
/// профиль остальных тестов (общая статика локализации).
/// </summary>
[Collection("ProLicensing")]
public sealed class ProLocalizationKeysTests
{
    private static string FindSourceFile(params string[] parts)
    {
        // Из bin-каталога тестов поднимаемся до корня репозитория (содержит каталог Workspace1C).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), "Не найден файл: " + path);
        return path;
    }

    private static Dictionary<string, string> ReadStrings(string language)
    {
        var path = FindSourceFile("Workspace1C", "Localization", "Languages", language + ".json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return result;
    }

    [Fact]
    public void Pro_Buy_And_FeatureStatus_Keys_ExistInRuAndEn()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var strings = ReadStrings(language);
            Assert.True(strings.ContainsKey("Pro.Buy"), language + ": нет ключа Pro.Buy");
            Assert.False(string.IsNullOrWhiteSpace(strings["Pro.Buy"]));
            Assert.True(strings.ContainsKey("Pro.FeatureStatus.Available"),
                language + ": нет ключа Pro.FeatureStatus.Available");
            Assert.True(strings.ContainsKey("Pro.FeatureStatus.InDevelopment"),
                language + ": нет ключа Pro.FeatureStatus.InDevelopment");
            Assert.NotEqual(strings["Pro.FeatureStatus.Available"], strings["Pro.FeatureStatus.InDevelopment"]);
        }
    }

    /// <summary>Pro.HowToBuy и раздел README должны указывать цену 2000 ₽.</summary>
    [Fact]
    public void ProHowToBuy_MentionsPrice()
    {
        Assert.Contains("2000", ReadStrings("ru")["Pro.HowToBuy"]);
        Assert.Contains("2,000", ReadStrings("en")["Pro.HowToBuy"]);
    }
}
