using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Параметры создания/правки группы в редакторе контекстного меню (issue #56):
/// только имя группы (иконки пунктов контекстного меню фиксированы каталогом,
/// поэтому блок «Иконки» в редакторе отсутствует).</summary>
public sealed class ContextMenuGroupEditParams
{
    public ContextMenuGroupEditParams(string title)
    {
        Title = title;
    }

    /// <summary>Имя группы.</summary>
    public string Title { get; }
}

/// <summary>
/// Вариант иконки каталога (issue #56). В упрощённом редакторе контекстного меню
/// блок «Иконки» отсутствует (иконки пунктов и стандартных групп фиксированы
/// каталогом), тип сохранён для симметрии с редактором меню «Утилиты» и будущих
/// расширений.
/// </summary>
public sealed class ContextMenuIconOption
{
    public ContextMenuIconOption(string key, string color)
    {
        Key = key;
        Color = color;
    }

    /// <summary>Ключ иконки PackIcon (WPF).</summary>
    public string Key { get; }

    /// <summary>Цвет иконки #RRGGBB.</summary>
    public string Color { get; }

    /// <summary>Подпись для выпадающего списка: «Ключ (цвет)».</summary>
    public string Label => $"{Key} ({Color})";
}

/// <summary>
/// Редактор структуры контекстного меню базы (issue #56): дерево компоновки
/// с перемещением пунктов (вверх/вниз/в группу/в корень), отключением (видимость),
/// созданием/правкой/удалением групп, разделителями, сбросом к стандартной
/// компоновке и сохранением/загрузкой компоновки в файл <c>context_menu_layout.json</c>.
/// Упрощённый вариант редактора меню «Утилиты» (issue #374): без блока «Иконки».
/// Чистый .NET — используется обеими платформами (WPF и Avalonia).
/// </summary>
public class ContextMenuEditorViewModel : ViewModelBase
{
    private readonly IContextMenuLayoutStore? _store;
    private ContextMenuLayout _layout = ContextMenuLayouts.Default();
    private ContextMenuNodeItem? _selectedItem;
    private ContextMenuNodeItem? _selectedGroup;

    /// <param name="store">Хранилище компоновки (для загрузки/сохранения); может быть null.</param>
    public ContextMenuEditorViewModel(IContextMenuLayoutStore? store)
    {
        _store = store;
        var loaded = store?.Load();
        // «Новинки программы» (issue #4): группа автоматически добавляется в структуру,
        // даже если её нет в файле; в неё попадают пункты, отсутствующие в файле.
        // Essential-пункты («Пользовательские действия», «Для выделенных») также
        // гарантируются резолвером (issue #56).
        _layout = ContextMenuResolver.EnsureNewItemsGroup(
            ContextMenuResolver.EnsureEssentialItems(loaded ?? ContextMenuLayouts.Default()))!;
        Roots = BuildTree(_layout.Root, null);

        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
        MoveToGroupCommand = new RelayCommand(_ => MoveSelectedToGroup(), _ => CanMoveToGroup);
        MoveToRootCommand = new RelayCommand(_ => MoveSelectedToRoot(), _ => CanMoveToRoot);
        AddGroupCommand = new RelayCommand(p => AddGroup(p as ContextMenuGroupEditParams));
        AddSeparatorCommand = new RelayCommand(_ => AddSeparator());
        EditGroupCommand = new RelayCommand(p => EditGroup(p as ContextMenuGroupEditParams), _ => CanEditGroup);
        DeleteGroupCommand = new RelayCommand(p => DeleteGroup(p is bool b && b), _ => CanDeleteGroup);
        ResetDefaultCommand = new RelayCommand(_ => ResetToDefault());
        SaveCommand = new RelayCommand(_ => Save());
        LoadCommand = new RelayCommand(_ => Load());
        CloseCommand = new RelayCommand(_ => Accepted = true);
        ToggleVisibleCommand = new RelayCommand(_ => { /* видимость переключается чекбоксом напрямую */ });
    }

    /// <summary>Корневые узлы дерева компоновки.</summary>
    public ObservableCollection<ContextMenuNodeItem> Roots { get; }

    /// <summary>Выбранный узел дерева (объект операций перемещения).</summary>
    public ContextMenuNodeItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetPropertyWithRelated(ref _selectedItem, value, nameof(SelectedItem), nameof(HasSelection), nameof(CanMoveSelected), nameof(CanMoveToGroup), nameof(CanMoveToRoot), nameof(CanEditGroup), nameof(CanDeleteGroup)))
            {
                RaiseCommandsCanExecuteChanged();
            }
        }
    }

    /// <summary>Выбранная группа-назначение (для «В группу»).</summary>
    public ContextMenuNodeItem? SelectedGroup
    {
        get => _selectedGroup;
        set => SetPropertyWithRelated(ref _selectedGroup, value, nameof(SelectedGroup), nameof(CanMoveToGroup));
    }

    /// <summary>Выбран ли узел (для UI-включения кнопок).</summary>
    public bool HasSelection => SelectedItem is not null;

    /// <summary>Можно ли править выбранный узел как группу (кнопка «Правка группы»).</summary>
    public bool CanEditGroup => IsGroup(SelectedItem);

    /// <summary>Можно ли удалить выбранный узел (группа или разделитель) — единая кнопка удаления.</summary>
    public bool CanDeleteGroup => IsGroup(SelectedItem) || IsSeparator(SelectedItem);

    /// <summary>Можно ли перенести выбранный узел в корень дерева (issue #56).</summary>
    public bool CanMoveToRoot => SelectedItem is not null && SelectedItem.Parent is not null;

    /// <summary>Диалог завершён нажатием «Готово».</summary>
    public bool Accepted { get; private set; }

    /// <summary>Все группы дерева (для выпадающего списка «Поместить в группу»).</summary>
    public ObservableCollection<ContextMenuNodeItem> Groups { get; } = new();

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveToGroupCommand { get; }
    public RelayCommand MoveToRootCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand AddSeparatorCommand { get; }
    public RelayCommand EditGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand ResetDefaultCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }

    /// <summary>Сохранённая компоновка (для построителя меню после закрытия редактора).</summary>
    public ContextMenuLayout Layout => _layout;

    /// <summary>Восстанавливает дерево из компоновки.</summary>
    private ObservableCollection<ContextMenuNodeItem> BuildTree(IEnumerable<ContextMenuNode> nodes, ContextMenuNodeItem? parent)
    {
        var result = new ObservableCollection<ContextMenuNodeItem>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            var item = new ContextMenuNodeItem(node, parent);
            foreach (var child in BuildTree(node.Children, item))
                item.Children.Add(child);
            result.Add(item);
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
                Groups.Add(item);
            CollectGroups(item);
        }
        return result;
    }

    private void CollectGroups(ContextMenuNodeItem item)
    {
        foreach (var child in item.Children)
        {
            if (string.Equals(child.Node.Kind, "group", StringComparison.Ordinal))
                Groups.Add(child);
            CollectGroups(child);
        }
    }

    private bool CanMoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return false;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        var index = siblings.IndexOf(item);
        var target = index + delta;
        return target >= 0 && target < siblings.Count;
    }

    private void MoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        var index = siblings.IndexOf(item);
        var target = index + delta;
        if (target < 0 || target >= siblings.Count)
            return;
        siblings.Move(index, target);
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Можно ли поместить выбранный узел в выбранную группу (issue #56).</summary>
    public bool CanMoveToGroup
    {
        get
        {
            var item = SelectedItem;
            var group = SelectedGroup;
            if (item is null || group is null || ReferenceEquals(item, group))
                return false;
            // Группу можно вложить в другую группу, но нельзя — в саму себя или в своего
            // потомка (иначе получится цикл) — issue #56.
            for (var p = group; p is not null; p = p.Parent)
            {
                if (ReferenceEquals(p, item))
                    return false;
            }
            return true;
        }
    }

    private void MoveSelectedToGroup()
    {
        var item = SelectedItem;
        var group = SelectedGroup;
        if (item is null || group is null || !CanMoveToGroup)
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        siblings.Remove(item);
        item.Parent = group;
        group.Children.Add(item);
        SelectedItem = item;
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Переносит выбранный узел в корень дерева (issue #56).</summary>
    private void MoveSelectedToRoot()
    {
        var item = SelectedItem;
        if (item is null || item.Parent is null)
            return;
        item.Parent.Children.Remove(item);
        item.Parent = null;
        Roots.Add(item);
        SelectedItem = item;
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Добавляет узел-разделитель после выбранного узла (или в конец корня) — issue #56.</summary>
    private void AddSeparator()
    {
        var node = new ContextMenuNode { Kind = "separator" };
        var selected = SelectedItem;
        if (selected is not null)
        {
            var siblings = selected.Parent is null ? Roots : selected.Parent.Children;
            var index = siblings.IndexOf(selected);
            var item = new ContextMenuNodeItem(node, selected.Parent);
            siblings.Insert(index + 1, item);
            SelectedItem = item;
        }
        else
        {
            var item = new ContextMenuNodeItem(node, null);
            Roots.Add(item);
            SelectedItem = item;
        }
    }

    /// <summary>Создаёт группу в корне или внутри выбранной группы (issue #56).</summary>
    private void AddGroup(ContextMenuGroupEditParams? editParams)
    {
        var title = editParams is not null && !string.IsNullOrWhiteSpace(editParams.Title)
            ? editParams.Title!.Trim()
            : LocalizationManager.T("ContextMenu.Editor.NewGroup");
        var group = new ContextMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = title,
        };
        var parent = IsGroup(SelectedItem) ? SelectedItem : null;
        var item = new ContextMenuNodeItem(group, parent);
        if (parent is null)
            Roots.Add(item);
        else
            parent.Children.Add(item);
        Groups.Add(item);
        SelectedItem = item;
    }

    /// <summary>Переименование выбранной группы (диалог «Правка группы»).</summary>
    private void EditGroup(ContextMenuGroupEditParams? editParams)
    {
        var item = SelectedItem;
        if (item is null || !IsGroup(item) || editParams is null)
            return;
        if (!string.IsNullOrWhiteSpace(editParams.Title))
            item.Node.CustomTitle = editParams.Title!.Trim();
        item.NotifyChanged();
    }

    /// <summary>Удаление выбранного узла: разделитель удаляется сразу; группа — при
    /// <paramref name="moveToRoot"/> дети переносятся в корень на место группы (с сохранением
    /// порядка), иначе группа удаляется вместе с детьми. После удаления выбирается узел,
    /// занявший место удалённого (тот же индекс в списке сиблингов; если индекс за концом —
    /// предыдущий; при переносе детей — первый перенесённый ребёнок) — issue #56.</summary>
    private void DeleteGroup(bool moveToRoot)
    {
        var item = SelectedItem;
        if (item is null)
            return;
        if (IsSeparator(item))
        {
            var sepSiblings = item.Parent is null ? Roots : item.Parent.Children;
            var sepIndex = sepSiblings.IndexOf(item);
            sepSiblings.Remove(item);
            SelectNodeAt(sepSiblings, sepIndex);
            RaiseCommandsCanExecuteChanged();
            return;
        }
        if (!IsGroup(item))
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (moveToRoot && item.Children.Count > 0)
        {
            var children = item.Children.ToList();
            foreach (var child in children)
            {
                child.Parent = null;
                siblings.Insert(index++, child);
            }
            siblings.Remove(item);
            Groups.Remove(item);
            SelectedItem = children[0]; // первый перенесённый ребёнок
        }
        else
        {
            siblings.Remove(item);
            Groups.Remove(item);
            SelectNodeAt(siblings, index);
        }
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Выбирает узел на месте удалённого: тот же индекс в списке сиблингов,
    /// если индекс за концом — предыдущий (issue #56).</summary>
    private void SelectNodeAt(ObservableCollection<ContextMenuNodeItem> siblings, int index)
    {
        if (siblings.Count == 0)
        {
            SelectedItem = null;
            return;
        }
        SelectedItem = siblings[Math.Min(index, siblings.Count - 1)];
    }

    /// <summary>True — узел является группой (подменю).</summary>
    private static bool IsGroup(ContextMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "group", StringComparison.Ordinal);

    /// <summary>True — узел является разделителем (issue #56).</summary>
    private static bool IsSeparator(ContextMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "separator", StringComparison.Ordinal);

    private void ResetToDefault()
    {
        // «Новинки программы» (issue #4): после сброса группа тоже добавляется
        // автоматически, если в каталоге есть неразмещённые пункты-новинки;
        // Essential-пункты гарантируются резолвером (issue #56).
        _layout = ContextMenuResolver.EnsureNewItemsGroup(
            ContextMenuResolver.EnsureEssentialItems(ContextMenuLayouts.Default()))!;
        Roots.Clear();
        Groups.Clear();
        foreach (var item in BuildTree(_layout.Root, null))
            Roots.Add(item);
        SelectedItem = null;
        SelectedGroup = null;
    }

    private void Save()
    {
        // Собираем дерево обратно в компоновку.
        var layout = new ContextMenuLayout { SchemaVersion = 1 };
        foreach (var item in Roots)
            layout.Root.Add(ToNode(item));
        _layout = layout;
        try
        {
            _store?.Save(layout);
        }
        catch
        {
            // Не удалось сохранить — редактор остаётся открытым; ошибку покажет вызывающий код.
        }
    }

    private static ContextMenuNode ToNode(ContextMenuNodeItem item)
    {
        var node = new ContextMenuNode
        {
            Kind = item.Node.Kind,
            ItemId = item.Node.ItemId,
            GroupId = item.Node.GroupId,
            CustomTitle = item.Node.CustomTitle,
            IconKey = item.Node.IconKey,
            IconColor = item.Node.IconColor,
            ExternalIconPath = item.Node.ExternalIconPath,
            Visible = item.Node.Visible,
            SeparatorBefore = item.Node.SeparatorBefore,
        };
        foreach (var child in item.Children)
            node.Children.Add(ToNode(child));
        return node;
    }

    private void Load()
    {
        var loaded = _store?.Load();
        if (loaded is null)
            return;
        // «Новинки программы» (issue #4): группа добавляется в структуру автоматически,
        // даже если её нет в файле; Essential-пункты гарантируются резолвером (issue #56).
        _layout = ContextMenuResolver.EnsureNewItemsGroup(
            ContextMenuResolver.EnsureEssentialItems(loaded))!;
        Roots.Clear();
        Groups.Clear();
        foreach (var item in BuildTree(_layout.Root, null))
            Roots.Add(item);
        SelectedItem = null;
        SelectedGroup = null;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        MoveToGroupCommand.RaiseCanExecuteChanged();
        EditGroupCommand.RaiseCanExecuteChanged();
        DeleteGroupCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Узел дерева редактора: обёртка над <see cref="ContextMenuNode"/> с коллекцией
/// дочерних узлов и родителем (для операций перемещения). Изменения свойств узла
/// транслируются в <see cref="ContextMenuNode"/> компоновки.
/// </summary>
public sealed class ContextMenuNodeItem : ViewModelBase
{
    /// <param name="node">Узел компоновки, который оборачиваем.</param>
    /// <param name="parent">Родительский узел дерева (null — корень).</param>
    public ContextMenuNodeItem(ContextMenuNode node, ContextMenuNodeItem? parent)
    {
        Node = node;
        Parent = parent;
    }

    /// <summary>Оборачиваемый узел компоновки.</summary>
    public ContextMenuNode Node { get; }

    /// <summary>Родительский узел дерева (null — корневой список).</summary>
    public ContextMenuNodeItem? Parent { get; set; }

    /// <summary>Дочерние узлы (для групп).</summary>
    public ObservableCollection<ContextMenuNodeItem> Children { get; } = new();

    /// <summary>Заголовок узла: разделитель / пользовательский или локализованный заголовок
    /// группы / словарь для пунктов.</summary>
    public string Title
    {
        get
        {
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return LocalizationManager.T("ContextMenu.Editor.SeparatorLabel");
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(Node.CustomTitle))
                    return Node.CustomTitle!;
                var key = ContextMenuCatalog.GroupTitleKey(Node.GroupId ?? string.Empty);
                return string.IsNullOrEmpty(key) ? LocalizationManager.T("ContextMenu.Editor.CustomGroup") : LocalizationManager.T(key);
            }
            var catalog = ContextMenuCatalog.Find(Node.ItemId);
            return catalog is null
                ? (Node.ItemId ?? "?")
                : LocalizationManager.T(catalog.TextKey);
        }
    }

    /// <summary>Пометка вида: «группа» / «пункт» / «разделитель».</summary>
    public string KindLabel =>
        string.Equals(Node.Kind, "separator", StringComparison.Ordinal)
            ? LocalizationManager.T("ContextMenu.Editor.KindSeparator")
            : string.Equals(Node.Kind, "group", StringComparison.Ordinal)
                ? LocalizationManager.T("ContextMenu.Editor.KindGroup")
                : LocalizationManager.T("ContextMenu.Editor.KindItem");

    /// <summary>Видимость узла (чекбокс в дереве).</summary>
    public bool IsVisible
    {
        get => Node.Visible;
        set
        {
            // «Новинки программы» (issue #4): видимость группы зафиксирована — скрытие
            // в настройках не влияет на отображение при работе, менять можно только
            // местоположение.
            if (!CanToggleVisibility)
                return;
            Node.Visible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Можно ли менять видимость узла: false — группа «Новинки программы»
    /// (её видимость зафиксирована, изменить можно только местоположение) — issue #4.</summary>
    public bool CanToggleVisibility =>
        !(string.Equals(Node.Kind, "group", StringComparison.Ordinal) &&
          string.Equals(Node.GroupId, ContextMenuCatalog.GroupNewItems, StringComparison.Ordinal));

    /// <summary>Строка для списка групп: заголовок.</summary>
    public string GroupLabel => Title;

    /// <summary>Эффективный ключ иконки для отображения: из каталога (для пунктов меню
    /// и стандартных групп), для разделителя — служебная иконка-линия (issue #56).
    /// Пусто — без иконки. Пользовательская компоновка может переопределить иконку.</summary>
    public string EffectiveIconKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Node.IconKey))
                return Node.IconKey!;
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return "Minus";
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
                return ContextMenuCatalog.GroupWpfIconKind(Node.GroupId ?? string.Empty);
            return ContextMenuCatalog.Find(Node.ItemId)?.WpfIconKind ?? string.Empty;
        }
    }

    /// <summary>Эффективный цвет иконки (#RRGGBB): пользовательский из компоновки, иначе — из каталога.</summary>
    public string EffectiveIconColor
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Node.IconColor))
                return Node.IconColor!;
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return "#9CA3AF";
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
                return ContextMenuCatalog.GroupIconColor(Node.GroupId ?? string.Empty);
            return ContextMenuCatalog.Find(Node.ItemId)?.IconColor ?? string.Empty;
        }
    }

    /// <summary>Ключ иконки для отображения в дереве (пусто — без иконки).</summary>
    public string DisplayIconKey => EffectiveIconKey;

    /// <summary>
    /// Issue #159 (1.0.1.123): ключ геометрии <c>Icon*</c> для превью в дереве
    /// редактора. PackIcon-имя каталога («Refresh», «Broom») в Avalonia не рисуется:
    /// <c>IconHelper</c> резолвил его в запасную «папку», поэтому все узлы выглядели
    /// одинаково. Пункт берёт готовый <see cref="ContextMenuItem.AvaloniaIconKey"/>,
    /// группа — стандартную иконку, неизвестный ключ — детерминированную «папку».
    /// </summary>
    public string DisplayIconGeometryKey
    {
        get
        {
            // Пользовательская иконка компоновки может хранить и PackIcon-имя,
            // и готовый ключ Icon*; приводим к геометрии.
            var custom = UtilitiesMenuCatalog.WpfIconGeometryKey(Node.IconKey);
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return "IconMinus";
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
            {
                var standard = ContextMenuCatalog.GroupAvaloniaIconKey(Node.GroupId ?? string.Empty);
                return string.IsNullOrWhiteSpace(standard) ? "IconFolder" : standard;
            }
            var catalog = ContextMenuCatalog.Find(Node.ItemId);
            if (catalog is not null && !string.IsNullOrWhiteSpace(catalog.AvaloniaIconKey))
                return catalog.AvaloniaIconKey;
            // Осмысленный фолбэк по типу узла — как в остальных редакторах.
            return "IconFolder";
        }
    }

    /// <summary>Цвет иконки (#RRGGBB) для отображения в дереве.</summary>
    public string DisplayIconColor => EffectiveIconColor;

    /// <summary>Есть ли иконка для отображения в дереве (видимость превью).</summary>
    public bool HasDisplayIcon => !string.IsNullOrWhiteSpace(DisplayIconGeometryKey);

    /// <summary>Оповещает UI об изменении внешних свойств узла (заголовок, иконка).</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(EffectiveIconKey));
        OnPropertyChanged(nameof(EffectiveIconColor));
        OnPropertyChanged(nameof(DisplayIconKey));
        OnPropertyChanged(nameof(DisplayIconGeometryKey));
        OnPropertyChanged(nameof(DisplayIconColor));
        OnPropertyChanged(nameof(HasDisplayIcon));
    }
}