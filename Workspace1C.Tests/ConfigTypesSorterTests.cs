using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #12: сортировка списка типовых конфигураций — по умолчанию по наименованию,
/// по любой колонке щелчком по заголовку, повторный щелчок меняет направление, индикатор ▲/▼.
/// </summary>
public sealed class ConfigTypesSorterTests
{
    private static ConfigTypeItemViewModel Row(string name, string nick = "", string configName = "",
        params string[] editions)
    {
        var model = new OneCConfigType
        {
            Code = name.ToUpperInvariant(),
            Name = name,
            Nick = nick,
            ConfigName = configName,
            Editions = editions.Select(e => new OneCConfigEdition { Name = e, Red = e }).ToList(),
        };
        return new ConfigTypeItemViewModel(model, _ => { }, _ => { });
    }

    private static string[] Names(IEnumerable<ConfigTypeItemViewModel> rows) => rows.Select(r => r.Name).ToArray();

    [Fact]
    public void Default_IsNameAscending()
    {
        var sorter = new ConfigTypesSorter();

        Assert.Equal(ConfigTypesSortColumn.Name, sorter.Column);
        Assert.False(sorter.Descending);

        var sorted = sorter.Apply(new[] { Row("Зарплата"), Row("бухгалтерия"), Row("Розница") });
        Assert.Equal(new[] { "бухгалтерия", "Зарплата", "Розница" }, Names(sorted));
    }

    [Fact]
    public void Toggle_SameColumn_ReversesDirection()
    {
        var sorter = new ConfigTypesSorter();
        sorter.Toggle(ConfigTypesSortColumn.Name);

        Assert.True(sorter.Descending);
        var sorted = sorter.Apply(new[] { Row("A"), Row("C"), Row("B") });
        Assert.Equal(new[] { "C", "B", "A" }, Names(sorted));
    }

    [Fact]
    public void Toggle_OtherColumn_StartsAscending()
    {
        var sorter = new ConfigTypesSorter();
        sorter.Toggle(ConfigTypesSortColumn.Name); // по убыванию
        sorter.Toggle(ConfigTypesSortColumn.Nick);

        Assert.Equal(ConfigTypesSortColumn.Nick, sorter.Column);
        Assert.False(sorter.Descending);
    }

    [Fact]
    public void SortByNick_EmptyValuesLast_InBothDirections()
    {
        var rows = new[] { Row("A", nick: ""), Row("B", nick: "Zeta"), Row("C", nick: "Alpha") };

        var asc = ConfigTypesSorter.Sort(rows, ConfigTypesSortColumn.Nick, descending: false);
        var desc = ConfigTypesSorter.Sort(rows, ConfigTypesSortColumn.Nick, descending: true);

        Assert.Equal(new[] { "C", "B", "A" }, Names(asc));
        Assert.Equal(new[] { "B", "C", "A" }, Names(desc));
    }

    [Fact]
    public void SortByColumn_TiesBrokenByName()
    {
        var rows = new[] { Row("Б", nick: "same"), Row("А", nick: "same") };

        var sorted = ConfigTypesSorter.Sort(rows, ConfigTypesSortColumn.Nick, descending: false);

        Assert.Equal(new[] { "А", "Б" }, Names(sorted));
    }

    [Fact]
    public void SortByEditionsAndConfigName()
    {
        var rows = new[]
        {
            Row("X", configName: "Retail", editions: "3.0"),
            Row("Y", configName: "Accounting", editions: "2.5"),
        };

        Assert.Equal(new[] { "Y", "X" }, Names(ConfigTypesSorter.Sort(rows, ConfigTypesSortColumn.Editions, false)));
        Assert.Equal(new[] { "Y", "X" }, Names(ConfigTypesSorter.Sort(rows, ConfigTypesSortColumn.ConfigName, false)));
    }

    [Fact]
    public void Indicator_OnlyForCurrentColumn()
    {
        var sorter = new ConfigTypesSorter();

        Assert.Equal("IconArrowUp", sorter.IndicatorFor(ConfigTypesSortColumn.Name));
        Assert.Equal(string.Empty, sorter.IndicatorFor(ConfigTypesSortColumn.Nick));

        sorter.Toggle(ConfigTypesSortColumn.Name);
        Assert.Equal("IconArrowDown", sorter.IndicatorFor(ConfigTypesSortColumn.Name));
    }

    [Fact]
    public void Apply_DoesNotModifySource()
    {
        var source = new List<ConfigTypeItemViewModel> { Row("B"), Row("A") };

        new ConfigTypesSorter().Apply(source);

        Assert.Equal(new[] { "B", "A" }, Names(source));
    }
}
