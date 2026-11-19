using System;
using System.Collections.Generic;
using System.Linq;

namespace Workspace1C.ViewModels;

/// <summary>Колонка сортировки списка типовых конфигураций (issue #12).</summary>
public enum ConfigTypesSortColumn
{
    Name,
    ConfigName,
    Nick,
    Editions,
    Account,
}

/// <summary>
/// Сортировка списка типовых конфигураций (issue #12): по умолчанию — по наименованию по
/// возрастанию; щелчок по заголовку выбирает колонку, повторный — меняет направление.
/// Сравнение без учёта регистра по правилам текущего языка; при равенстве — по наименованию,
/// затем по исходному порядку (сортировка устойчивая). Пустые значения — в конце при любом
/// направлении. Без платформенных зависимостей — общая для окон WPF и Avalonia.
/// </summary>
public sealed class ConfigTypesSorter
{
    /// <summary>Текущая колонка сортировки.</summary>
    public ConfigTypesSortColumn Column { get; private set; } = ConfigTypesSortColumn.Name;

    /// <summary>Направление: <c>true</c> — по убыванию.</summary>
    public bool Descending { get; private set; }

    /// <summary>
    /// Щелчок по заголовку колонки: та же колонка — смена направления, другая — эта колонка
    /// по возрастанию.
    /// </summary>
    public void Toggle(ConfigTypesSortColumn column)
    {
        if (column == Column)
        {
            Descending = !Descending;
            return;
        }
        Column = column;
        Descending = false;
    }

    /// <summary>
    /// Ключ векторной иконки направления для заголовка колонки (P1b):
    /// <c>IconArrowDown</c>, <c>IconArrowUp</c> или пустая строка. Текстовые «▲/▼»
    /// в UI больше не используются — заголовок рисует стрелку из набора темы.
    /// </summary>
    public string IndicatorFor(ConfigTypesSortColumn column) =>
        column != Column ? string.Empty : (Descending ? "IconArrowDown" : "IconArrowUp");

    /// <summary>Возвращает строки в порядке текущей сортировки (исходный список не меняется).</summary>
    public IReadOnlyList<ConfigTypeItemViewModel> Apply(IEnumerable<ConfigTypeItemViewModel> rows) =>
        Sort(rows, Column, Descending);

    /// <summary>Чистая функция сортировки строк по колонке и направлению.</summary>
    public static IReadOnlyList<ConfigTypeItemViewModel> Sort(
        IEnumerable<ConfigTypeItemViewModel> rows, ConfigTypesSortColumn column, bool descending)
    {
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        var indexed = (rows ?? Array.Empty<ConfigTypeItemViewModel>())
            .Select((row, index) => (row, index, key: KeyOf(row, column)))
            .ToList();

        indexed.Sort((a, b) =>
        {
            // Пустые значения — всегда в конце.
            var aEmpty = string.IsNullOrWhiteSpace(a.key);
            var bEmpty = string.IsNullOrWhiteSpace(b.key);
            if (aEmpty != bEmpty)
                return aEmpty ? 1 : -1;

            var cmp = comparer.Compare(a.key.Trim(), b.key.Trim());
            if (descending)
                cmp = -cmp;
            if (cmp != 0)
                return cmp;

            if (column != ConfigTypesSortColumn.Name)
            {
                cmp = comparer.Compare(a.row.Name ?? string.Empty, b.row.Name ?? string.Empty);
                if (cmp != 0)
                    return cmp;
            }
            return a.index.CompareTo(b.index);
        });

        return indexed.Select(x => x.row).ToList();
    }

    private static string KeyOf(ConfigTypeItemViewModel row, ConfigTypesSortColumn column) => column switch
    {
        ConfigTypesSortColumn.ConfigName => row.ConfigName ?? string.Empty,
        ConfigTypesSortColumn.Nick => row.Nick ?? string.Empty,
        ConfigTypesSortColumn.Editions => row.EditionsSummary ?? string.Empty,
        ConfigTypesSortColumn.Account => row.AccountDisplay ?? string.Empty,
        _ => row.Name ?? string.Empty,
    };
}
