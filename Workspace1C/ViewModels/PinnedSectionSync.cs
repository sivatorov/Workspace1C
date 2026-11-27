using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Точечное обновление узла «Закреплённые» без полной пересборки дерева (issue #173).
/// Общая логика Windows (WPF) и Linux (Avalonia): обе версии правят один и тот же
/// стабильный набор корней дерева режима «Все», а не снимок текущей вкладки.
/// <para>
/// Раньше WPF-версия при активном фильтре («Недавние»/«Избранное»/поиск/тег) вообще
/// не трогала кэш дерева «Все». Закрепление из «Недавних» применялось к данным и
/// сохранялось, но кэш «Все» оставался прежним — при возврате на «Все базы» он
/// переиспользовался без пересборки, и базы в «Закреплённых» не было до перезапуска.
/// </para>
/// </summary>
internal static class PinnedSectionSync
{
    /// <summary>true, если узел — служебный узел «Закреплённые».</summary>
    public static bool IsPinnedNode(GroupNodeViewModel node) =>
        node.Group is null
        && string.Equals(node.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal);

    /// <summary>Находит служебный узел «Закреплённые» среди корней дерева (или null).</summary>
    public static GroupNodeViewModel? FindPinnedNode(IEnumerable<GroupNodeViewModel> roots) =>
        roots.FirstOrDefault(IsPinnedNode);

    /// <summary>
    /// Применяет текущее состояние <see cref="Infobase.IsPinned"/> к набору корней:
    /// добавляет базу в «Закреплённые» (создавая узел при необходимости) или убирает её
    /// оттуда, удаляя опустевший узел. Дубликатов не возникает — состав проверяется по
    /// экземпляру базы.
    /// </summary>
    /// <param name="roots">Стабильный набор корней дерева «Все» (кэш или постоянная коллекция).</param>
    /// <param name="infobase">База, у которой только что переключён <see cref="Infobase.IsPinned"/>.</param>
    /// <param name="createPinnedNode">Фабрика узла «Закреплённые» (оформление/развёрнутость — платформенные).</param>
    /// <param name="showEmptyGroups">Показывать ли пустые группы (влияет на <c>PopulateItems</c>).</param>
    /// <param name="orderInfobases">Текущая сортировка баз (для порядка строк внутри узла); может быть null.</param>
    /// <returns>true, если состав корней изменился (узел добавлен или удалён).</returns>
    public static bool Apply(
        IList<GroupNodeViewModel> roots,
        Infobase infobase,
        Func<GroupNodeViewModel> createPinnedNode,
        bool showEmptyGroups = false,
        Func<IEnumerable<Infobase>, IEnumerable<Infobase>>? orderInfobases = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(infobase);
        ArgumentNullException.ThrowIfNull(createPinnedNode);

        var pinned = FindPinnedNode(roots);
        var structureChanged = false;

        if (infobase.IsPinned)
        {
            if (pinned is null)
            {
                pinned = createPinnedNode();
                pinned.Infobases.Add(infobase);
                pinned.PopulateItems(showEmptyGroups);
                roots.Insert(0, pinned);
                structureChanged = true;
            }
            else if (!pinned.Infobases.Contains(infobase))
            {
                pinned.Infobases.Add(infobase);
                Reorder(pinned, orderInfobases);
                pinned.PopulateItems(showEmptyGroups);
            }
            else
            {
                // База уже в узле (повторный клик по флажку): не создаём вторую строку.
                pinned.NotifyCountChanged();
            }
        }
        else if (pinned is not null)
        {
            pinned.Infobases.Remove(infobase);
            if (pinned.Infobases.Count == 0)
            {
                // Опустевший узел уничтожается (issue #21, эксперимент A 1.0.0.34):
                // отписываем от коллекций перед удалением из корней.
                pinned.Detach();
                roots.Remove(pinned);
                structureChanged = true;
            }
            else
            {
                pinned.PopulateItems(showEmptyGroups);
            }
        }

        return structureChanged;
    }

    /// <summary>
    /// Переупорядочивает узел, в котором лежит база (кроме «Закреплённых»): после
    /// переключения закрепления строка должна встать на новое место в своей группе
    /// (<see cref="Infobase.GroupSortOrder"/>) без полной пересборки дерева.
    /// </summary>
    public static void SortOwningNode(
        IEnumerable<GroupNodeViewModel> roots,
        Infobase infobase,
        Func<IEnumerable<Infobase>, IEnumerable<Infobase>> orderInfobases,
        bool showEmptyGroups = false)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(infobase);
        ArgumentNullException.ThrowIfNull(orderInfobases);

        foreach (var root in roots)
        {
            var owner = FindNodeWith(root, infobase);
            if (owner is null || IsPinnedNode(owner))
                continue;

            Reorder(owner, orderInfobases);
            owner.PopulateItems(showEmptyGroups);
            return;
        }
    }

    /// <summary>Ищет узел дерева, в списке баз которого лежит указанная база.</summary>
    private static GroupNodeViewModel? FindNodeWith(GroupNodeViewModel node, Infobase infobase)
    {
        if (node.Infobases.Contains(infobase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeWith(child, infobase);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Переупорядочивает базы узла по текущему полю сортировки без лавины уведомлений:
    /// на время Clear/Add уведомления подавляются, после — одно обновление через
    /// <c>PopulateItems</c> вызывает вызывающая сторона.
    /// </summary>
    private static void Reorder(
        GroupNodeViewModel node,
        Func<IEnumerable<Infobase>, IEnumerable<Infobase>>? orderInfobases)
    {
        if (orderInfobases is null)
            return;

        var sorted = orderInfobases(node.Infobases).ToList();
        node.SetNotificationsSuppressed(true);
        try
        {
            node.Infobases.Clear();
            foreach (var infobase in sorted)
                node.Infobases.Add(infobase);
        }
        finally
        {
            node.SetNotificationsSuppressed(false);
        }
    }
}
