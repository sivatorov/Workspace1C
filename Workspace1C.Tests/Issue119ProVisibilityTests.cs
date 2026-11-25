using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Workspace1C;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #119 (T4): гейтинг новых 2.x-функций при сохранении бесплатности public core
/// в ветке 1.x. Проверяются четыре сценария из плана 1.0.1.119 (раздел 2.5):
/// public core без ключа доступен, новые 2.x без ключа закрыты, с ключом
/// <c>major=1</c> и модулями ПРО доступны, с ключом без модулей — закрыты.
/// </summary>
[Collection("ProLicensing")]
public sealed class Issue119ProVisibilityTests : IDisposable
{
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "w1c_issue119_" + Guid.NewGuid().ToString("N"));

    public Issue119ProVisibilityTests()
    {
        _publicKey = Convert.ToBase64String(_signer.ExportSubjectPublicKeyInfo());
        Directory.CreateDirectory(_dir);
        EditionService.DirectoryOverride = _dir;
        LicenseVerifier.PublicKeyOverride = _publicKey;
        MachineIdProvider.RawIdOverride = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        VersionInfo.MajorOverride = 1; // 1.0.1.119 — ветка 1.x
        EditionService.Reset();
    }

    public void Dispose()
    {
        VersionInfo.MajorOverride = null;
        EditionService.HasProModulesOverride = null;
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
            Id = "test-119",
            Edition = edition,
            Major = major,
            Machine = machine,
            Owner = "ООО Ромашка",
            Email = "test@example.com",
            Issued = "2026-11-25"
        };
        var signed = LicenseKey.BuildSignedPart(payload);
        var signature = _signer.SignData(Encoding.UTF8.GetBytes(signed), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenseKey.Compose(signed, signature);
    }

    private static ProFeature[] PublicCore() =>
        ProFeatures.All.Where(ProFeatures.IsInPublicCore).ToArray();

    private static ProFeature[] New2x() =>
        ProFeatures.All.Where(feature => !ProFeatures.IsInPublicCore(feature)).ToArray();

    [Fact]
    public void PublicCore_WithoutKey_RemainsAvailableIn1x()
    {
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();

        Assert.False(EditionService.IsPro);
        var publicCore = PublicCore();
        Assert.NotEmpty(publicCore);
        foreach (var feature in publicCore)
            Assert.True(EditionService.Has(feature), feature + " — public core должен быть бесплатен в 1.x");

        // Public core без ключа не показывает окно «Лицензия».
        ProFeature? shown = null;
        var previous = ProGate.ShowLicenseDialog;
        ProGate.ShowLicenseDialog = f => shown = f;
        try
        {
            Assert.True(ProGate.Ensure(ProFeature.ServerAlerts));
            Assert.True(ProGate.Ensure(ProFeature.MassLoadCf));
            Assert.Null(shown);
        }
        finally
        {
            ProGate.ShowLicenseDialog = previous;
        }
    }

    [Fact]
    public void New2x_WithoutKey_Unavailable()
    {
        // Даже если модули ПРО встроены, без ключа новые 2.x-функции закрыты.
        EditionService.HasProModulesOverride = true;
        EditionService.Reset();

        Assert.False(EditionService.IsPro);
        var new2x = New2x();
        Assert.NotEmpty(new2x);
        foreach (var feature in new2x)
            Assert.False(EditionService.Has(feature), feature + " не должен быть доступен без ключа");
    }

    [Fact]
    public void New2x_WithKeyAndModules_Available()
    {
        EditionService.HasProModulesOverride = true;
        EditionService.Reset();

        var result = EditionService.Activate(Issue(1, MachineIdProvider.Current));
        Assert.True(result.IsValid);
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal(1, result.Key!.Payload.Major); // ключ major=1 подходит для 1.0.1.119
        Assert.True(EditionService.IsPro);

        var new2x = New2x();
        Assert.NotEmpty(new2x);
        foreach (var feature in new2x)
            Assert.True(EditionService.Has(feature), feature + " должен открываться ключом major=1 с модулями ПРО");

        // Public core остаётся доступным.
        foreach (var feature in PublicCore())
            Assert.True(EditionService.Has(feature), feature + " — public core должен оставаться доступным");
    }

    [Fact]
    public void New2x_WithKeyButNoModules_Unavailable()
    {
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();

        var result = EditionService.Activate(Issue(1, MachineIdProvider.Current));
        Assert.True(result.IsValid);
        Assert.True(EditionService.IsPro);

        var new2x = New2x();
        Assert.NotEmpty(new2x);
        foreach (var feature in new2x)
            Assert.False(EditionService.Has(feature), feature + " без модулей ПРО должен быть закрыт даже с ключом");

        // Public core доступен и с ключом без модулей.
        foreach (var feature in PublicCore())
            Assert.True(EditionService.Has(feature), feature + " — public core не зависит от модулей ПРО");
    }

    // ------------------------------------------------ T5: витрина 2.x-функций в меню

    /// <summary>
    /// T5-5: без ключа новая 2.x-функция в официальной сборке (модули есть) закрыта,
    /// а <see cref="ProGate.Ensure"/> показывает окно «Лицензия» с подсветкой функции.
    /// </summary>
    [Fact]
    public void ProGate_New2xWithoutKey_ShowsLicenseDialog()
    {
        EditionService.HasProModulesOverride = true;
        EditionService.Reset();
        Assert.False(EditionService.IsPro);

        var previous = ProGate.ShowLicenseDialog;
        var shown = new System.Collections.Generic.List<ProFeature?>();
        ProGate.ShowLicenseDialog = f => shown.Add(f);
        try
        {
            Assert.False(ProGate.Ensure(ProFeature.Pipelines));
            Assert.False(ProGate.Ensure(ProFeature.CloudBackup));
            Assert.Equal(new ProFeature?[] { ProFeature.Pipelines, ProFeature.CloudBackup }, shown);
        }
        finally
        {
            ProGate.ShowLicenseDialog = previous;
        }
    }

    /// <summary>
    /// T5-6: без ключа 2.x-пункт виден, получает суффикс « · ПРО» и подсказку
    /// «требуется ключ»; группа «ПРО» присутствует в официальной сборке.
    /// </summary>
    [Fact]
    public void Menu_New2xWithoutKey_ShowsProSuffixAndTooltip()
    {
        EditionService.HasProModulesOverride = true;
        EditionService.Reset();
        Assert.False(EditionService.IsPro);

        const string itemId = "Issue119Pipelines";
        var item = new UtilitiesMenuItem(itemId, "Pro.Feature.Pipelines",
            UtilitiesMenuCatalog.GroupProServices, "Pipe", "IconPipe", "#06B6D4",
            UtilitiesMenuCommandKind.External, isNew: true,
            requiredFeature: ProFeature.Pipelines);
        UtilitiesMenuCatalog.RegisterExternal(item, () => { });
        try
        {
            var found = UtilitiesMenuCatalog.Find(itemId);
            Assert.NotNull(found);
            Assert.True(found!.RequiresProKey);

            var suffix = Localization.LocalizationManager.T("Pro.MenuSuffix");
            Assert.Contains(suffix, found.DisplayText(), StringComparison.Ordinal);
            Assert.Equal(
                string.Format(Localization.LocalizationManager.T("Pro.RequiredFormat"),
                    Localization.LocalizationManager.T(ProFeatures.TitleKey(ProFeature.Pipelines))),
                found.DisplayToolTip());

            // Пункт не исчезает без ключа: группа «ПРО» видна и содержит зарегистрированный пункт.
            var layout = UtilitiesMenuResolver.ResolveForDisplay(null, null);
            var tools = UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupTools);
            Assert.NotNull(tools);
            var pro = Assert.Single(tools!.Root.Where(node => node.GroupId == UtilitiesMenuCatalog.GroupPro));
            Assert.Contains(pro.Children, node => node.ItemId == itemId);
        }
        finally
        {
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>
    /// T5-7: клик по External-пункту без ключа проходит через единый гейт меню:
    /// окно «Лицензия» показывается ровно один раз, действие не выполняется.
    /// </summary>
    [Fact]
    public void Menu_New2xWithoutKey_DoesNotExecuteExternal()
    {
        EditionService.HasProModulesOverride = true;
        EditionService.Reset();
        Assert.False(EditionService.IsPro);

        const string itemId = "Issue119AutoTests";
        var executed = 0;
        var item = new UtilitiesMenuItem(itemId, "Pro.Feature.AutoTests",
            UtilitiesMenuCatalog.GroupProServices, "PlayCircle", "IconPlayCircle", "#22C55E",
            UtilitiesMenuCommandKind.External, isNew: true,
            requiredFeature: ProFeature.AutoTests);
        UtilitiesMenuCatalog.RegisterExternal(item, () => executed++);

        var previous = ProGate.ShowLicenseDialog;
        var shown = 0;
        ProGate.ShowLicenseDialog = _ => shown++;
        try
        {
            Assert.False(UtilitiesMenuCatalog.ExecuteExternalGated(itemId));
            Assert.Equal(0, executed);
            Assert.Equal(1, shown); // ровно одно окно, без дубля из команды модуля
        }
        finally
        {
            ProGate.ShowLicenseDialog = previous;
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>
    /// T5-8: пункты public core в 1.x без ключа не получают суффикс « · ПРО»
    /// и подсказку «требуется ключ».
    /// </summary>
    [Fact]
    public void Menu_PublicCoreWithoutKey_HasNoProSuffix()
    {
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();
        Assert.False(EditionService.IsPro);

        var suffix = Localization.LocalizationManager.T("Pro.MenuSuffix");
        foreach (var id in new[] { "ServerAlerts", "TelegramBot", "BatchLoadCf", "ClusterMigration" })
        {
            var item = UtilitiesMenuCatalog.Find(id);
            Assert.NotNull(item);
            Assert.NotNull(item!.RequiredFeature);
            Assert.True(ProFeatures.IsInPublicCore(item.RequiredFeature!.Value));
            Assert.True(EditionService.Has(item.RequiredFeature.Value));
            Assert.False(item.RequiresProKey);
            Assert.DoesNotContain(suffix, item.DisplayText(), StringComparison.Ordinal);
            Assert.Null(item.DisplayToolTip());
        }
    }

    /// <summary>
    /// T5-2 (видимость): в публичной сборке группа «ПРО» видна всегда, но содержит
    /// только placeholder-пункты «только в официальной сборке», а не External-экшены.
    /// </summary>
    [Fact]
    public void Menu_PublicBuild_ShowsOfficialBuildOnlyPlaceholders()
    {
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();

        var layout = UtilitiesMenuResolver.ResolveForDisplay(null, null);
        var tools = UtilitiesMenuLayouts.ExtractSection(layout, UtilitiesMenuCatalog.GroupTools);
        Assert.NotNull(tools);
        var pro = Assert.Single(tools!.Root.Where(node => node.GroupId == UtilitiesMenuCatalog.GroupPro));
        Assert.NotEmpty(pro.Children);
        Assert.All(pro.Children, node => Assert.Equal("placeholder", node.Kind));
        Assert.DoesNotContain(pro.Children, node => node.Kind == "item");
    }

    /// <summary>
    /// T5-9: честные статусы строк окна «Лицензия» согласованы с доступностью:
    /// публичная сборка 2.x — «только в официальной сборке»; официальная без ключа —
    /// «требуется ключ»; с ключом — «доступна».
    /// </summary>
    [Fact]
    public void LicenseRows_Statuses_AreConsistent()
    {
        // Публичная сборка: новых 2.x-модулей нет — статус «только в официальной сборке».
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();
        var publicVm = new ViewModels.LicenseViewModel();
        foreach (var row in publicVm.Features.Where(r => !ProFeatures.IsInPublicCore(r.Feature)))
        {
            Assert.Equal(ViewModels.ProFeatureStatusKind.OfficialBuildOnly, row.StatusKind);
            Assert.Equal(Localization.LocalizationManager.T("Pro.FeatureStatus.OfficialBuildOnly"), row.HonestStatus);
            Assert.NotEqual(Localization.LocalizationManager.T("Pro.FeatureStatus.InDevelopment"), row.HonestStatus);
        }
        foreach (var row in publicVm.Features.Where(r => ProFeatures.IsInPublicCore(r.Feature)))
        {
            Assert.Equal(ViewModels.ProFeatureStatusKind.Available, row.StatusKind);
            Assert.Equal(Localization.LocalizationManager.T("Pro.FeatureStatus.Available"), row.HonestStatus);
        }

        try
        {
            // Официальная сборка без ключа: функции реализованы, но нужен ключ.
            EditionService.HasProModulesOverride = true;
            ProFeatures.ResetImplemented();
            foreach (var feature in New2x())
                ProFeatures.MarkImplemented(feature);
            EditionService.Reset();
            var officialVm = new ViewModels.LicenseViewModel();
            foreach (var row in officialVm.Features.Where(r => !ProFeatures.IsInPublicCore(r.Feature)))
            {
                Assert.Equal(ViewModels.ProFeatureStatusKind.RequiresKey, row.StatusKind);
                Assert.Equal(Localization.LocalizationManager.T("Pro.FeatureStatus.RequiresKey"), row.HonestStatus);
            }

            // С ключом major=1 и модулями все новые 2.x-функции доступны.
            var result = EditionService.Activate(Issue(1, MachineIdProvider.Current));
            Assert.True(result.IsValid);
            var proVm = new ViewModels.LicenseViewModel();
            foreach (var row in proVm.Features.Where(r => !ProFeatures.IsInPublicCore(r.Feature)))
            {
                Assert.Equal(ViewModels.ProFeatureStatusKind.Available, row.StatusKind);
                Assert.Equal(Localization.LocalizationManager.T("Pro.FeatureStatus.Available"), row.HonestStatus);
            }
        }
        finally
        {
            ProFeatures.ResetImplemented();
        }
    }

    /// <summary>
    /// T5-10: ключи витрины ПРО присутствуют одновременно в ru.json и en.json.
    /// </summary>
    [Fact]
    public void Localization_HasProVisibilityKeys()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var strings = ReadStrings(language);
            foreach (var key in new[]
                     {
                         "Pro.MenuSuffix",
                         "Pro.RequiredFormat",
                         "Pro.OfficialBuildOnlyFormat",
                         "Pro.FeatureStatus.Available",
                         "Pro.FeatureStatus.RequiresKey",
                         "Pro.FeatureStatus.OfficialBuildOnly",
                         "Pro.FeatureStatus.InDevelopment"
                     })
            {
                Assert.True(strings.ContainsKey(key), language + ": нет ключа " + key);
                Assert.False(string.IsNullOrWhiteSpace(strings[key]), language + ": пустой ключ " + key);
            }
        }
    }

    private static string FindSourceFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), "Не найден файл: " + path);
        return path;
    }

    private static System.Collections.Generic.Dictionary<string, string> ReadStrings(string language)
    {
        var path = FindSourceFile("Workspace1C", "Localization", "Languages", language + ".json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var strings = doc.RootElement.GetProperty("strings");
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return result;
    }
}
