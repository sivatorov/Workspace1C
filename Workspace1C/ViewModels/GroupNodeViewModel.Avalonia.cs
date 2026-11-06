#if LINUX
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading;
using Avalonia.Media;
using Workspace1C.Collections;
using Workspace1C.Converters;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Представляет узел дерева групп информационных баз (Avalonia/Linux).
/// Содержит модель группы, коллекцию подгрупп и коллекцию баз, размещённых в этой группе.
/// Также содержит единую коллекцию <see cref="Items"/>, объединяющую подгруппы и базы.
/// </summary>
public class GroupNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private string? _fullPathCache;
    private bool? _containsInfobasesCache;
    private bool _suppressNotifications;
    // Видимость корня в дереве (issue #21, Этап F): стабильный набор корней держит
    // плоский узел и все группы в GroupNodes всегда, а переключение вкладки меняет
    // только этот флаг — контейнеры корней не пересоздаются (нет Reset GroupNodes).
    private bool _isVisible = true;
    // Делегаты-обработчики коллекций: поля, чтобы метод Detach() мог отписаться
    // (issue #21, эксперимент A 1.0.0.34) — иначе анонимные лямбды не отписываемы.
    private readonly NotifyCollectionChangedEventHandler _onInfobasesChanged;
    private readonly NotifyCollectionChangedEventHandler _onChildrenChanged;
    private bool _detached;

    // Совокупный счётчик вызовов Detach по всем узлам (диагностика issue #21, 1.0.0.41):
    // лог CM_TREESWITCH печатает прирост за смену вкладки (detachCount) — контроль, что
    // старые узлы дерева реально отписываются от коллекций при перестройке, а не
    // накапливаются обработчиками. Заведён и в Linux-варианте (1.0.0.44): фича полезна
    // на обеих платформах, а тест GroupNodeViewModelTests компилируется под Linux.
    private static long s_totalDetachCount;

    /// <summary>Совокупное число вызовов <see cref="Detach"/> по всем созданным узлам.</summary>
    public static long TotalDetachCount => Volatile.Read(ref s_totalDetachCount);

    /// <summary>Сбрасывает счётчик <see cref="TotalDetachCount"/> (юнит-тесты). Возвращает старое значение.</summary>
    internal static long ResetDetachCounterForTesting() =>
        Interlocked.Exchange(ref s_totalDetachCount, 0);

    private readonly string _color;
    private readonly string _iconColor;
    private readonly string _icon;
    // Внутренний маркер специального узла (null для обычных групп).
    // Не readonly: постоянный плоский узел обновляет маркер при смене режима (issue #21, Этап C).
    private string? _marker;
    // Отображаемое имя (для обычных узлов) или ключ локализации (для специальных узлов с маркером).
    // Не readonly: постоянный плоский узел обновляет имя при смене режима (issue #21, Этап C).
    private string _displayName;

    /// <summary>Внутренний маркер специального узла «Закреплённые».</summary>
    public const string PinnedMarker = "Pinned";

    /// <summary>Внутренний маркер специального узла «Все базы» (плоский список без группировки).</summary>
    public const string AllBasesMarker = "AllBases";

    /// <summary>Внутренний маркер специального узла «Без группы».</summary>
    public const string NoGroupMarker = "NoGroup";

    public GroupNodeViewModel(
        Group? group,
        GroupNodeViewModel? parent = null,
        string? displayName = null,
        string? defaultColor = null,
        string? defaultIconColor = null,
        string? defaultIcon = null,
        string? marker = null)
    {
        Group = group;
        Parent = parent;
        if (marker is not null)
        {
            // Специальный узел: храним маркер и ключ локализации, а отображаемое имя
            // вычисляем на лету (LocalizationManager.T) — текст обновится при смене языка.
            _marker = marker;
            _displayName = MarkerToKey(marker);
        }
        else
        {
            _marker = null;
            // Группа может не иметь имени (например, импортирована из ibases.v8i) —
            // тогда в дереве показываем плейсхолдер, чтобы группу можно было увидеть и отредактировать.
            _displayName = displayName
                ?? (group is null ? LocalizationManager.T("Group.NoGroup") : (string.IsNullOrWhiteSpace(group.Name) ? LocalizationManager.T("Group.NoName") : group.Name));
        }
        Children = new ObservableCollection<GroupNodeViewModel>();
        Infobases = new ObservableCollection<Infobase>();
        // ObservableRangeCollection: массовое заполнение одним Reset вместо тысяч
        // CollectionChanged на каждую базу (issue #21).
        Items = new ObservableRangeCollection<object>();
        _color = ResolveColor(group, defaultColor, marker);
        _iconColor = group is not null
            ? (!string.IsNullOrWhiteSpace(group.IconColor) ? group.IconColor : "#FFFFFF")
            : (defaultIconColor ?? "#FFFFFF");
        _icon = group?.Icon ?? defaultIcon ?? string.Empty;
        // Issue #107: плашка фона адаптируется под тёмную тему (светлая заливка
        // группы становится полупрозрачной, тёмной); цвет иконки — не адаптируется.
        HeaderBrush = GroupColorConverter.GetBrush(_color, adaptForDark: true);
        HeaderTextBrush = GroupTextColorConverter.GetBrush(_color);
        IconBrush = GroupColorConverter.GetBrush(_iconColor);
        // Обработчики вынесены в поля-делегаты — Detach() (эксперимент A, 1.0.0.34)
        // должен иметь возможность отписаться при удалении узла из дерева.
        _onInfobasesChanged = (_, _) =>
        {
            _containsInfobasesCache = null;
            NotifyCountChanged();
        };
        _onChildrenChanged = (_, _) =>
        {
            _containsInfobasesCache = null;
            NotifyCountChanged();
        };
        Infobases.CollectionChanged += _onInfobasesChanged;
        Children.CollectionChanged += _onChildrenChanged;
    }

    /// <summary>
    /// Отписывает узел от коллекций <see cref="Infobases"/> и <see cref="Children"/>
    /// (issue #21, эксперимент A 1.0.0.34): вызывается перед удалением узла из дерева
    /// (заменой коллекции GroupNodes), чтобы узел не удерживался собственными
    /// подписками и при пересборках дерева не накапливались обработчики. Повторный
    /// вызов безопасен (идемпотентен). Постоянные специальные узлы («Закреплённые»,
    /// «Все базы», «Без группы», плоский узел) не детачатся — они переживают смены вкладок.
    /// </summary>
    public void Detach()
    {
        if (_detached)
            return;
        _detached = true;
        Interlocked.Increment(ref s_totalDetachCount);
        // Поддерево узла не переиспользуется (дерево строгое, без общих поддеревьев) —
        // отписываем и дочерние узлы, чтобы коллекции не удерживали ни одного
        // обработчика узлов, ставших мусором после удаления корня.
        foreach (var child in Children)
            child.Detach();
        Infobases.CollectionChanged -= _onInfobasesChanged;
        Children.CollectionChanged -= _onChildrenChanged;
    }

    // Цвет обычной папки по умолчанию (равен цвету группы в модели и fallback в конвертерах).
    private const string DefaultFolderColor = "#2D6CDF";
    // Цвет узла «Закреплённые» (избранная папка) по умолчанию.
    private const string DefaultPinnedColor = "#8B5CF6";

    /// <summary>
    /// Разрешает цвет фона заголовка узла: индивидуальный цвет группы (или специального
    /// узла) имеет приоритет; иначе берётся общий цвет папки из активной цветовой схемы
    /// (<see cref="Models.ColorScheme"/>) — обычной или избранной. Схема читается из
    /// <c>Themes.ThemeManager.CurrentScheme</c> под текущий вариант темы, поэтому цвет
    /// папки пересчитывается при каждой перестройке дерева после применения схемы.
    /// </summary>
    private static string ResolveColor(Group? group, string? defaultColor, string? marker)
    {
        var scheme = Themes.ThemeManager.CurrentScheme;
        var dark = Themes.ThemeManager.CurrentTheme == Themes.ThemeManager.DarkThemeName;

        if (group is not null)
        {
            // Индивидуальный цвет группы; значение по умолчанию считаем ненастроенным,
            // чтобы общий цвет папки из схемы реально влиял на обычные папки.
            if (!string.IsNullOrWhiteSpace(group.Color)
                && !string.Equals(group.Color, DefaultFolderColor, StringComparison.OrdinalIgnoreCase))
                return group.Color;
            return scheme.PaletteValue(dark, "FolderColor");
        }

        // Узел «Закреплённые» — избранная папка: индивидуальный цвет или общий из схемы.
        if (string.Equals(marker, PinnedMarker, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(defaultColor)
                && !string.Equals(defaultColor, DefaultPinnedColor, StringComparison.OrdinalIgnoreCase))
                return defaultColor;
            return scheme.PaletteValue(dark, "FavoriteFolderColor");
        }

        // Прочие специальные узлы («Без группы», «Все базы»): свой цвет или обычная папка.
        if (!string.IsNullOrWhiteSpace(defaultColor))
            return defaultColor;
        return scheme.PaletteValue(dark, "FolderColor");
    }

    /// <summary>Кэшированная кисть фона заголовка группы.</summary>
    public IBrush HeaderBrush { get; }

    /// <summary>Кэшированная кисть текста заголовка (контраст к фону).</summary>
    public IBrush HeaderTextBrush { get; }

    /// <summary>Кэшированная кисть иконки группы.</summary>
    public IBrush IconBrush { get; }

    /// <summary>Модель группы. Null для специальных узлов («Закреплённые», «Без группы»).</summary>
    public Group? Group { get; }

    /// <summary>Родительский узел. Null для корневого узла.</summary>
    public GroupNodeViewModel? Parent { get; internal set; }

    /// <summary>
    /// Имя группы для отображения (без пути). Для специальных узлов («Закреплённые»,
    /// «Все базы», «Без группы») возвращает локализованный текст, который обновляется
    /// при смене языка интерфейса.
    /// </summary>
    public string DisplayName => _marker is null ? _displayName : LocalizationManager.T(_displayName);

    /// <summary>Внутренний маркер специального узла. Null для обычных групп.</summary>
    public string? Marker => _marker;

    /// <summary>
    /// Видимость узла в дереве (issue #21, Этап F): скрытые корни остаются в
    /// <c>GroupNodes</c>, но не отображаются (TreeViewItem.IsVisible привязан к этому
    /// свойству). Стабильный набор корней — переключение вкладки меняет только видимость,
    /// а не состав коллекции, поэтому контейнеры корней не пересоздаются (нет Reset).
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Стабильный ключ узла для сохранения состояния развёрнутости (не зависит от языка).
    /// Для реальных групп — полный путь; для специальных узлов — внутренний маркер.
    /// </summary>
    public string NodeKey => string.IsNullOrEmpty(FullPath) ? (Marker ?? DisplayName) : FullPath;

    /// <summary>Сопоставляет внутренний маркер специального узла с ключом локализации.</summary>
    private static string MarkerToKey(string marker) => marker switch
    {
        PinnedMarker => "Main.Pinned",
        AllBasesMarker => "Main.FlatAllBases",
        NoGroupMarker => "Group.NoGroup",
        _ => marker
    };

    /// <summary>Полный путь группы в иерархии (кэшируется после первого обращения).</summary>
    public string FullPath
    {
        get
        {
            if (_fullPathCache is not null)
                return _fullPathCache;

            if (Group is null)
                return _fullPathCache = string.Empty;

            var parts = new List<string>();
            for (var node = this; node is not null && node.Group is not null; node = node.Parent)
                parts.Add(node.Group.Name);
            parts.Reverse();
            return _fullPathCache = string.Join(GroupHierarchyHelper.PathSeparator, parts);
        }
    }

    /// <summary>Цвет фона заголовка группы.</summary>
    public string Color => Group?.Color ?? _color;

    /// <summary>Цвет иконки (по умолчанию белый, если не задан отдельно).</summary>
    public string IconColor => Group is not null
        ? (!string.IsNullOrWhiteSpace(Group.IconColor) ? Group.IconColor : "#FFFFFF")
        : _iconColor;

    /// <summary>Ключ иконки группы.</summary>
    public string Icon
    {
        get
        {
            if (Group is not null)
                return string.IsNullOrWhiteSpace(Group.Icon) ? "IconFolder" : Group.Icon;

            if (!string.IsNullOrWhiteSpace(_icon))
                return _icon;

            // Иконка специальных узлов выбирается по внутреннему маркеру (не по DisplayName),
            // поэтому не зависит от языка интерфейса.
            return Marker switch
            {
                PinnedMarker => "IconPin",
                AllBasesMarker => "IconDatabase",
                _ => "IconFolder"
            };
        }
    }

    /// <summary>Подгруппы текущего узла.</summary>
    public ObservableCollection<GroupNodeViewModel> Children { get; }

    /// <summary>Базы, размещённые непосредственно в этой группе.</summary>
    public ObservableCollection<Infobase> Infobases { get; }

    /// <summary>Единая коллекция для отображения в дереве.</summary>
    public ObservableRangeCollection<object> Items { get; }

    /// <summary>Признак наличия подгрупп.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Признак наличия баз в группе.</summary>
    public bool HasInfobases => Infobases.Count > 0;

    /// <summary>Признак наличия баз в группе или её подгруппах.</summary>
    public bool ContainsInfobases
    {
        get
        {
            if (_containsInfobasesCache.HasValue)
                return _containsInfobasesCache.Value;
            var value = Infobases.Count > 0 || Children.Any(c => c.ContainsInfobases);
            _containsInfobasesCache = value;
            return value;
        }
    }

    /// <summary>Общее количество баз в группе и всех её подгруппах.</summary>
    public int TotalInfobaseCount => Infobases.Count + Children.Sum(c => c.TotalInfobaseCount);

    /// <summary>Общее количество подгрупп в группе и всех её вложенных подгруппах (рекурсивно).</summary>
    public int TotalSubgroupCount => Children.Count + Children.Sum(c => c.TotalSubgroupCount);

    /// <summary>
    /// Суффикс счётчика заголовка группы: «(N / M)», где N — подгруппы, M — базы (оба
    /// рекурсивно); при отсутствии подгрупп — прежний вид «(M)». Скобки входят в суффикс,
    /// чтобы разметка привязывалась напрямую без StringFormat. Числа подставляются через
    /// локализуемый шаблон <c>Main.GroupCountWithSubgroups</c>.
    /// </summary>
    public string GroupCountSuffix
    {
        get
        {
            var totalBases = TotalInfobaseCount;
            if (TotalSubgroupCount > 0)
                return "(" + string.Format(LocalizationManager.T("Main.GroupCountWithSubgroups"), TotalSubgroupCount, totalBases) + ")";
            return "(" + totalBases + ")";
        }
    }

    /// <summary>Сообщает привязкам об изменении счётчика (и поднимает уведомление по родителям).</summary>
    public void NotifyCountChanged()
    {
        if (_suppressNotifications)
            return;
        OnPropertyChanged(nameof(TotalInfobaseCount));
        OnPropertyChanged(nameof(TotalSubgroupCount));
        OnPropertyChanged(nameof(GroupCountSuffix));
        OnPropertyChanged(nameof(HasInfobases));
        OnPropertyChanged(nameof(ContainsInfobases));
        Parent?.NotifyCountChanged();
    }

    /// <summary>Включить/выключить уведомления при массовом заполнении.</summary>
    public void SetNotificationsSuppressed(bool suppress)
    {
        _suppressNotifications = suppress;
        foreach (var child in Children)
            child.SetNotificationsSuppressed(suppress);
    }

    /// <summary>Состояние развёрнутости узла в дереве.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Устанавливает развёрнутость без PropertyChanged (массовые expand/collapse).</summary>
    public void SetExpandedSilent(bool expanded) => _isExpanded = expanded;

    /// <summary>Сообщить UI о текущем IsExpanded.</summary>
    public void NotifyIsExpanded() => OnPropertyChanged(nameof(IsExpanded));

    /// <summary>Состояние выделенности узла в дереве.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Заполняет коллекцию <see cref="Items"/>: сначала подгруппы, затем базы текущей группы.
    /// </summary>
    public void PopulateItems(bool includeEmptyGroups = false)
    {
        foreach (var child in Children)
            child.PopulateItems(includeEmptyGroups);

        _containsInfobasesCache = null;
        _suppressNotifications = true;
        try
        {
            var items = new List<object>(Children.Count + Infobases.Count);
            foreach (var child in Children)
            {
                if (includeEmptyGroups || child.ContainsInfobases)
                    items.Add(child);
            }
            foreach (var infobase in Infobases)
            {
                // Узел «Закреплённые» кладёт в дерево ОБЁРТКУ базы, а не сам экземпляр
                // (issue #301): экземпляр уже лежит в своей группе, и дубль данных в дереве
                // заставлял штатное выделение Avalonia подсвечивать первую копию
                // (узел «Закреплённые» стоит первым) вместо строки под курсором.
                items.Add(Marker == PinnedMarker ? new PinnedInfobaseItem(infobase) : infobase);
            }
            // Массовая замена одним событием Reset вместо тысяч CollectionChanged (issue #21).
            Items.ReplaceAll(items);
            _containsInfobasesCache = Infobases.Count > 0 || Children.Any(c => c.ContainsInfobases);
        }
        finally
        {
            _suppressNotifications = false;
        }
        OnPropertyChanged(nameof(TotalInfobaseCount));
        OnPropertyChanged(nameof(TotalSubgroupCount));
        OnPropertyChanged(nameof(GroupCountSuffix));
        OnPropertyChanged(nameof(HasInfobases));
        OnPropertyChanged(nameof(ContainsInfobases));
    }

    /// <summary>
    /// Переиспользует узел для плоского режима (issue #21, Этап C): обновляет маркер и
    /// отображаемое имя (например, «Найдено» при поиске vs «Все базы» при отключённой
    /// группировке) и массово заменяет базы одним событием Reset. Объект узла не
    /// пересоздаётся — контейнер корневого узла в дереве не пересоздаётся.
    /// </summary>
    /// <param name="infobases">Базы для плоского списка.</param>
    /// <param name="displayName">Отображаемое имя (для режима фильтра, marker == null).</param>
    /// <param name="marker">Внутренний маркер специального узла (например, <see cref="AllBasesMarker"/>).</param>
    public void ResetFlatContent(
        IReadOnlyList<Infobase> infobases,
        string? displayName = null,
        string? marker = null)
    {
        // Обновляем маркер и имя: для специальных узлов DisplayName вычисляется на лету
        // из ключа локализации, для фильтра — из переданного текста.
        _marker = marker;
        _displayName = marker is not null ? MarkerToKey(marker) : (displayName ?? string.Empty);
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(NodeKey));

        _suppressNotifications = true;
        try
        {
            Infobases.Clear();
            foreach (var infobase in infobases)
                Infobases.Add(infobase);
        }
        finally
        {
            _suppressNotifications = false;
        }
        PopulateItems();
    }

    /// <summary>Рекурсивно сортирует подгруппы текущего узла по имени.</summary>
    public void SortChildrenRecursive(bool ascending)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        List<GroupNodeViewModel> sorted;
        if (ascending)
            sorted = Children.OrderBy(c => c.DisplayName, comparer).ToList();
        else
            sorted = Children.OrderByDescending(c => c.DisplayName, comparer).ToList();

        Children.Clear();
        foreach (var child in sorted)
            Children.Add(child);

        foreach (var child in Children)
            child.SortChildrenRecursive(ascending);
    }

    public override string ToString() => string.IsNullOrEmpty(FullPath) ? DisplayName : FullPath;

    /// <summary>Строит дерево групп из плоского списка с учётом свойства <see cref="Group.ParentId"/>.</summary>
    public static List<GroupNodeViewModel> BuildTree(IEnumerable<Group> groups)
    {
        var list = groups.ToList();
        var nodes = new Dictionary<string, GroupNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in list)
            nodes[group.Id] = new GroupNodeViewModel(group);

        var inCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in list)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = group;
            while (current is not null && !string.IsNullOrEmpty(current.ParentId))
            {
                if (!visited.Add(current.Id))
                {
                    inCycle.Add(current.Id);
                    foreach (var id in visited)
                        inCycle.Add(id);
                    break;
                }

                if (!nodes.TryGetValue(current.ParentId, out var parentNode))
                    break;

                current = parentNode.Group;
            }
        }

        var roots = new List<GroupNodeViewModel>();
        foreach (var group in list)
        {
            if (inCycle.Contains(group.Id) ||
                string.IsNullOrEmpty(group.ParentId) ||
                !nodes.ContainsKey(group.ParentId))
            {
                roots.Add(nodes[group.Id]);
                continue;
            }

            var parentNode = nodes[group.ParentId];
            var childNode = nodes[group.Id];
            parentNode.Children.Add(childNode);
            childNode.Parent = parentNode;
        }

        return roots;
    }

    /// <summary>
    /// Является ли узел специальным узлом «Закреплённые» (issue #31): узел без реальной
    /// группы с маркером <see cref="PinnedMarker"/>. Закреплённая база присутствует в
    /// дереве дважды (копия в «Закреплённых» и строка в своей группе), и reveal перехода
    /// к строке обязан игнорировать такие узлы — иначе выделение «останавливается» на
    /// копии из закреплений.
    /// </summary>
    public static bool IsPinnedNode(GroupNodeViewModel? node)
        => node is not null
           && string.Equals(node.Marker, PinnedMarker, StringComparison.Ordinal);

    /// <summary>
    /// Находит узел, в котором база показана в разделе «Все базы»: настоящая группа
    /// (в т.ч. вложенная) или узел «Без группы». Дубль в «Закреплённых» пропускается —
    /// команда «Найти в списке» (issue #285) должна выделять строку именно в общем
    /// списке, а не первую попавшуюся копию в закреплениях.
    /// </summary>
    public static GroupNodeViewModel? FindInfobaseHomeNode(
        IEnumerable<GroupNodeViewModel> roots, Infobase infobase)
    {
        // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
        // reveal обязан искать цель только среди видимых узлов.
        foreach (var root in roots)
        {
            if (!root.IsVisible)
                continue;
            var found = FindInNode(root, infobase);
            if (found is not null)
                return found;
        }
        return null;

        static GroupNodeViewModel? FindInNode(GroupNodeViewModel node, Infobase ib)
        {
            // Узел «Закреплённые» — дубль строк из общего списка: во «Все базы» база
            // показана в своей группе (или «Без группы»), поэтому закрепления пропускаем.
            if (string.Equals(node.Marker, PinnedMarker, StringComparison.Ordinal))
                return null;
            foreach (var child in node.Children)
            {
                var found = FindInNode(child, ib);
                if (found is not null)
                    return found;
            }
            if (node.Infobases.Any(ibRef => ReferenceEquals(ibRef, ib)))
                return node;
            return null;
        }
    }
}
#endif