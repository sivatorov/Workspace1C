using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты унификации операций импорта/экспорта *.v8i между «Настройками» (вкладка
/// «Базы» → «Список баз») и меню «Утилиты» (issue #29): меню «Утилиты» содержит те же
/// пункты «Экспорт в файл *.v8i…» и «Импорт из файла *.v8i…», что и настройки, с теми же
/// названиями (одни ключи локализации) и теми же видами команд, которые резолвятся
/// в единые методы/команды MainViewModel.
/// </summary>
public sealed class UtilitiesMenuV8iUnificationTests
{
    /// <summary>Каталог меню «Утилиты» содержит пункты экспорта/импорта *.v8i (issue #29).</summary>
    [Fact]
    public void Catalog_ContainsV8iExportImportItems()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        Assert.Equal(UtilitiesMenuCommandKind.ExportBasesV8iFile, export!.CommandKind);
        Assert.Equal(UtilitiesMenuCommandKind.ImportBasesV8iFile, import!.CommandKind);
    }

    /// <summary>Пункты *.v8i используют те же ключи локализации, что и кнопки настроек, —
    /// названия в обоих местах идентичны (issue #29).</summary>
    [Fact]
    public void V8iItems_UseSameLocalizationKeysAsSettings()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        // Те же ключи, что у кнопок «Экспорт в файл *.v8i…» / «Импорт из файла *.v8i…»
        // в настройках (вкладка «Базы» → «Список баз»).
        Assert.Equal("Settings.Bases.ExportV8iFile", export!.TextKey);
        Assert.Equal("Settings.Bases.ImportV8iFile", import!.TextKey);
    }

    /// <summary>Стандартная компоновка включает пункты *.v8i в подгруппе «Списки баз».</summary>
    [Fact]
    public void DefaultLayout_IncludesV8iItemsInBaseListsGroup()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.Contains("ExportV8iFile", ids);
        Assert.Contains("ImportV8iFile", ids);

        // Пункты находятся в подгруппе «Списки баз» раздела «Инструменты»
        // (как и остальные операции экспорта/импорта).
        var baseLists = FindGroup(layout.Root, UtilitiesMenuCatalog.GroupBaseLists);
        Assert.NotNull(baseLists);
        var opsIds = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(baseLists!.Children, opsIds);
        Assert.Contains("ExportV8iFile", opsIds);
        Assert.Contains("ImportV8iFile", opsIds);
    }

    private static Workspace1C.Models.UtilitiesMenuNode? FindGroup(
        System.Collections.Generic.IEnumerable<Workspace1C.Models.UtilitiesMenuNode>? nodes, string groupId)
    {
        if (nodes is null)
            return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Kind, "group", System.StringComparison.Ordinal)
                && string.Equals(node.GroupId, groupId, System.StringComparison.Ordinal))
            {
                return node;
            }
            if (node.Children is { Count: > 0 } && FindGroup(node.Children, groupId) is { } nested)
                return nested;
        }
        return null;
    }

    /// <summary>Пункты *.v8i не помечены как «новинки» — они сразу в стандартной компоновке.</summary>
    [Fact]
    public void V8iItems_AreNotMarkedAsNew()
    {
        var export = UtilitiesMenuCatalog.Find("ExportV8iFile");
        var import = UtilitiesMenuCatalog.Find("ImportV8iFile");

        Assert.NotNull(export);
        Assert.NotNull(import);
        Assert.False(export!.IsNew);
        Assert.False(import!.IsNew);
    }
}
