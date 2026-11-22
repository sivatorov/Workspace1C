using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты единого форматтера имени платформы (<see cref="PlatformDisplayFormatter"/>,
/// issue #33): суффикс «(тонкий клиент)» добавляется только листьям-тонким клиентам,
/// а флаг <see cref="PlatformVersionGroup.IsThinClient"/> проносится через
/// <see cref="PlatformVersionService.BuildGroupedTree"/> (в диалоге выбора и в
/// настройках дерево строится именно этим методом).
/// </summary>
public sealed class PlatformDisplayFormatterTests
{
    // ======================= Чистый форматтер =======================

    [Theory]
    [InlineData(true, "8.3.27.2214 (x64) (тонкий клиент)")]
    [InlineData(false, "8.3.27.2214 (x64)")]
    public void AppendThinClientSuffix_AddsSuffixOnlyForThinClients(bool isThinClient, string expected)
    {
        // Суффикс передаётся явно — тест не зависит от текущего языка локализации.
        Assert.Equal(expected,
            PlatformDisplayFormatter.AppendThinClientSuffix("8.3.27.2214 (x64)", isThinClient, "(тонкий клиент)"));
    }

    [Fact]
    public void AppendThinClientSuffix_ThinClient_KeepsBaseNameAndSuffix()
    {
        Assert.Equal("8.3.27.1688 (32) (тонкий клиент)",
            PlatformDisplayFormatter.AppendThinClientSuffix("8.3.27.1688 (32)", true, "(тонкий клиент)"));
    }

    // ======================= Пронос флага через BuildGroupedTree =======================

    [Fact]
    public void BuildGroupedTree_MixedSet_ThinClientReachesLeafOnly()
    {
#if LINUX
        return; // Linux-ветка BuildGroupedTree определяет тонкий клиент иначе
#endif
        // Display содержит суффикс разрядности «(32)/(64)» — так версии возвращает
        // FindInstalledVersionInfos, и именно этот формат понимает VersionComparer
        // (отображаемое имя листа в дереве уже с меткой «(x64)/(x32)»).
        var infos = new List<PlatformVersionInfo>
        {
            new() { Display = "8.3.27.2214 (64)", Path = @"C:\1cv8\8.3.27.2214", IsThinClient = true },
            new() { Display = "8.3.27.2214 (32)", Path = @"C:\1cv8\8.3.27.2214\1c", IsThinClient = false },
            new() { Display = "8.3.27.1688 (64)", Path = @"C:\1cv8\8.3.27.1688", IsThinClient = false },
            new() { Display = "8.5.1.42 (64)", Path = @"C:\1cv8\8.5.1.42", IsThinClient = true },
        };

        var tree = PlatformVersionService.BuildGroupedTree(infos);

        // Группы и линии не являются тонкими клиентами ни в одном наборе.
        Assert.All(tree, n => Assert.False(n.IsThinClient));

        var leaves = tree.SelectMany(AllLeaves).ToList();

        var thinX64 = leaves.Single(l => l.Variant == "8.3.27.2214 (64)");
        Assert.True(thinX64.IsThinClient);

        var x32 = leaves.Single(l => l.Variant == "8.3.27.2214 (32)");
        Assert.False(x32.IsThinClient);

        var fullX64 = leaves.Single(l => l.Variant == "8.3.27.1688 (64)");
        Assert.False(fullX64.IsThinClient);

        var thin85 = leaves.Single(l => l.Variant == "8.5.1.42 (64)");
        Assert.True(thin85.IsThinClient);

        // У тонкого листа форматтер добавляет суффикс (без зависимости от языка);
        // отображаемое имя листа в дереве содержит метку разрядности «(x64)».
        Assert.Equal("8.3.27.2214 (x64) (тонкий клиент)",
            PlatformDisplayFormatter.AppendThinClientSuffix(thinX64.Name, thinX64.IsThinClient, "(тонкий клиент)"));
    }

    private static IEnumerable<PlatformVersionGroup> AllLeaves(PlatformVersionGroup node)
    {
        if (node.IsLeaf)
        {
            yield return node;
            yield break;
        }

        foreach (var child in node.Children)
            foreach (var leaf in AllLeaves(child))
                yield return leaf;
    }
}