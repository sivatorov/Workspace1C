using System;
using System.Collections.Generic;
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
/// Issue #146 (T8): группа «ПРО» — обычный узел компоновки меню «Утилиты».
/// Проверяется, что её можно скрыть/переместить/отредактировать, что скрытый раздел
/// «Инструменты» не раскрывается, что старая компоновка мигрирует без потери настроек
/// и что гейтинг ПРО-пунктов сохранён.
/// </summary>
[Collection("ProLicensing")]
public sealed class Issue146ProGroupEditingTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "w1c_issue146pro_" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;

    public Issue146ProGroupEditingTests()
    {
        Directory.CreateDirectory(_dir);
        _publicKey = Convert.ToBase64String(_signer.ExportSubjectPublicKeyInfo());
        EditionService.DirectoryOverride = _dir;
        EditionService.HasProModulesOverride = true;
        LicenseVerifier.PublicKeyOverride = _publicKey;
        MachineIdProvider.RawIdOverride = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        EditionService.Reset();
    }

    public void Dispose()
    {
        EditionService.HasProModulesOverride = null;
        EditionService.DirectoryOverride = null;
        LicenseVerifier.PublicKeyOverride = null;
        MachineIdProvider.RawIdOverride = null;
        EditionService.Reset();
        _signer.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* временный каталог */ }
    }

    private UtilitiesMenuLayoutStore NewStore() => new(directoryOverride: _dir);

    /// <summary>Ключ ПРО с указанной major-версией для текущей машины.</summary>
    private string Issue(int major)
    {
        var payload = new LicensePayload
        {
            Id = "issue-146-t8",
            Edition = "pro",
            Major = major,
            Machine = MachineIdProvider.Current,
            Owner = "ООО Ромашка",
            Email = "test@example.com",
            Issued = "2026-11-25"
        };
        var signed = LicenseKey.BuildSignedPart(payload);
        var signature = _signer.SignData(Encoding.UTF8.GetBytes(signed), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenseKey.Compose(signed, signature);
    }

    private static UtilitiesMenuNode Tools(UtilitiesMenuLayout layout) =>
        layout.Root.Single(node => node is not null
            && string.Equals(node.Kind, "group", StringComparison.Ordinal)
            && string.Equals(node.GroupId, UtilitiesMenuCatalog.GroupTools, StringComparison.Ordinal));

    private static UtilitiesMenuNode Pro(UtilitiesMenuLayout layout) =>
        Tools(layout).Children.Single(node => node is not null
            && string.Equals(node.Kind, "group", StringComparison.Ordinal)
            && string.Equals(node.GroupId, UtilitiesMenuCatalog.GroupPro, StringComparison.Ordinal));

    private static UtilitiesMenuNodeItem? FindItem(
        IEnumerable<UtilitiesMenuNodeItem> nodes, Func<UtilitiesMenuNodeItem, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
                return node;
            var found = FindItem(node.Children, predicate);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Стандартная компоновка содержит группу «ПРО» внутри «Инструментов».</summary>
    [Fact]
    public void DefaultLayout_ContainsProGroupUnderTools()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var pro = Pro(layout);

        Assert.True(pro.Visible);
        Assert.Equal(UtilitiesMenuCatalog.ProGroupNodeId, pro.NodeId);
        Assert.False(pro.IsTopLevel);
        // Состав зависит от глобально зарегистрированных модулей ПРО (другие тесты
        // могут временно сбрасывать каталог) — здесь проверяется сама структура группы.
        Assert.NotNull(pro.Children);
    }

    /// <summary>Скрытие группы «ПРО» в компоновке реально скрывает её при отображении.</summary>
    [Fact]
    public void ResolveForDisplay_RespectsHiddenProGroup()
    {
        var store = NewStore();
        var layout = UtilitiesMenuLayouts.Default();
        Pro(layout).Visible = false;
        store.Save(layout);

        var resolved = UtilitiesMenuResolver.ResolveForDisplay(null, store);
        var pro = Pro(resolved);
        Assert.False(pro.Visible);
        Assert.NotNull(pro.Children); // скрытие не разрушает состав группы
    }

    /// <summary>Скрытый раздел «Инструменты» не раскрывается резолвером из-за группы ПРО.</summary>
    [Fact]
    public void ResolveForDisplay_HiddenTools_DoesNotForceVisible()
    {
        var store = NewStore();
        var layout = UtilitiesMenuLayouts.Default();
        Tools(layout).Visible = false;
        store.Save(layout);

        var resolved = UtilitiesMenuResolver.ResolveForDisplay(null, store);
        var tools = Tools(resolved);
        Assert.False(tools.Visible);
        // Группа ПРО остаётся внутри скрытого раздела и не всплывает в корень.
        Assert.Single(tools.Children.Where(node => node.GroupId == UtilitiesMenuCatalog.GroupPro));
        Assert.DoesNotContain(resolved.Root, node => node.GroupId == UtilitiesMenuCatalog.GroupPro);
    }

    /// <summary>Редактор видит группу ПРО (даже в старой компоновке) и может её скрыть.</summary>
    [Fact]
    public void Editor_SeesProGroup_AndCanHideIt()
    {
        var store = NewStore();
        // Компоновка до 1.0.1.120: группы ПРО ещё нет.
        var legacy = UtilitiesMenuLayouts.Default();
        Tools(legacy).Children.RemoveAll(node => node.GroupId == UtilitiesMenuCatalog.GroupPro);
        store.Save(legacy);

        var editor = new UtilitiesMenuEditorViewModel(store);
        var proItem = FindItem(editor.Roots, node => node.Node.GroupId == UtilitiesMenuCatalog.GroupPro);
        Assert.NotNull(proItem);
        Assert.True(proItem!.CanToggleVisibility);
        editor.SelectedItem = proItem;
        Assert.True(editor.CanEditGroup);

        proItem.IsVisible = false;
        editor.SaveCommand.Execute(null);

        var resolved = UtilitiesMenuResolver.ResolveForDisplay(null, store);
        Assert.False(Pro(resolved).Visible);
    }

    /// <summary>
    /// Редактор может скрыть отдельный ПРО-пункт и переместить всю группу (в корень);
    /// после сохранения резолвер не возвращает группу обратно в «Инструменты».
    /// </summary>
    [Fact]
    public void Editor_CanHideProItem_AndMoveProGroup()
    {
        const string itemId = "Issue146ProGroupEditableItem";
        UtilitiesMenuCatalog.RegisterExternal(
            new UtilitiesMenuItem(itemId, "Pro.Feature.Pipelines",
                UtilitiesMenuCatalog.GroupProServices, "Pipe", "IconPipe", "#06B6D4",
                UtilitiesMenuCommandKind.External, isNew: true,
                requiredFeature: ProFeature.Pipelines),
            () => { });
        try
        {
            var store = NewStore();
            store.Save(UtilitiesMenuLayouts.Default());
            var editor = new UtilitiesMenuEditorViewModel(store);

            var proItem = FindItem(editor.Roots, node => node.Node.GroupId == UtilitiesMenuCatalog.GroupPro);
            Assert.NotNull(proItem);
            var child = proItem!.Children.FirstOrDefault(node => node.Node.ItemId == itemId);
            Assert.NotNull(child);

            // Скрытие отдельного пункта ПРО — обычная операция редактора.
            child!.IsVisible = false;

            // Перемещение группы ПРО в корень (отдельная кнопка панели) — тоже.
            editor.SelectedItem = proItem;
            Assert.True(editor.CanMoveToRoot);
            editor.MoveToRootCommand.Execute(null);
            editor.SaveCommand.Execute(null);

            var resolved = UtilitiesMenuResolver.ResolveForDisplay(null, store);
            var moved = resolved.Root.Single(node => node.GroupId == UtilitiesMenuCatalog.GroupPro);
            Assert.True(moved.IsTopLevel);
            Assert.Equal(UtilitiesMenuCatalog.ProGroupNodeId, moved.NodeId);
            Assert.Contains(moved.Children, node => node.ItemId == itemId && node.Visible == false);
            Assert.DoesNotContain(Tools(resolved).Children,
                node => node.GroupId == UtilitiesMenuCatalog.GroupPro);
        }
        finally
        {
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>Пункты ПРО остаются гейтированными без ключа (суффикс и окно «Лицензия»).</summary>
    [Fact]
    public void ProItems_RemainGated_WithoutKey()
    {
        EditionService.Reset();
        Assert.False(EditionService.IsPro);

        const string itemId = "Issue146ProGroupTestItem";
        var item = new UtilitiesMenuItem(itemId, "Pro.Feature.Pipelines",
            UtilitiesMenuCatalog.GroupProServices, "Pipe", "IconPipe", "#06B6D4",
            UtilitiesMenuCommandKind.External, isNew: true,
            requiredFeature: ProFeature.Pipelines);
        UtilitiesMenuCatalog.RegisterExternal(item, () => { });
        try
        {
            var layout = UtilitiesMenuResolver.ResolveForDisplay(null, null);
            var pro = Pro(layout);
            Assert.Contains(pro.Children, node => node.ItemId == itemId);

            var found = UtilitiesMenuCatalog.Find(itemId)!;
            Assert.True(found.RequiresProKey);
            Assert.Contains(Localization.LocalizationManager.T("Pro.MenuSuffix"), found.DisplayText(),
                StringComparison.Ordinal);

            var previous = ProGate.ShowLicenseDialog;
            var shown = 0;
            ProGate.ShowLicenseDialog = _ => shown++;
            try
            {
                Assert.False(UtilitiesMenuCatalog.ExecuteExternalGated(itemId));
                Assert.Equal(1, shown);
            }
            finally
            {
                ProGate.ShowLicenseDialog = previous;
            }
        }
        finally
        {
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>
    /// С ключом major=1 и модулями ПРО пункты группы активны: без суффикса « · ПРО»,
    /// команда выполняется без окна «Лицензия».
    /// </summary>
    [Fact]
    public void ProItems_WithKeyAndModules_AreActive()
    {
        var result = EditionService.Activate(Issue(1));
        Assert.True(result.IsValid);
        Assert.True(EditionService.IsPro);
        Assert.True(EditionService.Has(ProFeature.Pipelines));

        const string itemId = "Issue146ProGroupActiveItem";
        var executed = 0;
        var item = new UtilitiesMenuItem(itemId, "Pro.Feature.Pipelines",
            UtilitiesMenuCatalog.GroupProServices, "Pipe", "IconPipe", "#06B6D4",
            UtilitiesMenuCommandKind.External, isNew: true,
            requiredFeature: ProFeature.Pipelines);
        UtilitiesMenuCatalog.RegisterExternal(item, () => executed++);
        try
        {
            var layout = UtilitiesMenuResolver.ResolveForDisplay(null, null);
            Assert.Contains(Pro(layout).Children, node => node.ItemId == itemId);

            var found = UtilitiesMenuCatalog.Find(itemId)!;
            Assert.False(found.RequiresProKey);
            Assert.DoesNotContain(Localization.LocalizationManager.T("Pro.MenuSuffix"),
                found.DisplayText(), StringComparison.Ordinal);

            var previous = ProGate.ShowLicenseDialog;
            var shown = 0;
            ProGate.ShowLicenseDialog = _ => shown++;
            try
            {
                Assert.True(UtilitiesMenuCatalog.ExecuteExternalGated(itemId));
                Assert.Equal(1, executed);
                Assert.Equal(0, shown);
            }
            finally
            {
                ProGate.ShowLicenseDialog = previous;
            }
        }
        finally
        {
            UtilitiesMenuCatalog.UnregisterExternal(itemId);
        }
    }

    /// <summary>Публичная сборка показывает в группе ПРО только заглушки.</summary>
    [Fact]
    public void PublicBuild_Placeholders_RemainInProGroup()
    {
        EditionService.HasProModulesOverride = false;
        EditionService.Reset();

        var layout = UtilitiesMenuResolver.ResolveForDisplay(null, null);
        var pro = Pro(layout);
        Assert.NotEmpty(pro.Children);
        Assert.All(pro.Children, node => Assert.Equal("placeholder", node.Kind));
    }

    /// <summary>Миграция старой компоновки добавляет группу ПРО, не теряя пользовательские группы.</summary>
    [Fact]
    public void Migration_AddsProGroup_PreservesUserLayout()
    {
        var layout = UtilitiesMenuLayouts.Default();
        Tools(layout).Children.RemoveAll(node => node.GroupId == UtilitiesMenuCatalog.GroupPro);
        var custom = new UtilitiesMenuNode
        {
            Kind = "group",
            NodeId = "user-group-1",
            CustomTitle = "Моя папка",
            IsTopLevel = true,
            Children = new List<UtilitiesMenuNode> { new() { Kind = "item", ItemId = "CommandPalette" } }
        };
        layout.Root.Add(custom);

        var migrated = UtilitiesMenuLayouts.Migrate(layout);
        Assert.NotNull(Pro(migrated));
        var preserved = migrated.Root.Single(node => node.NodeId == "user-group-1");
        Assert.Equal("Моя папка", preserved.CustomTitle);
        Assert.Contains(preserved.Children, node => node.ItemId == "CommandPalette");
    }
}
