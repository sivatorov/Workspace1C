using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Параметры создания/правки группы в редакторе меню «Утилиты» (issue #374):
/// имя группы, ключ иконки каталога и режим отображения (issue #146).</summary>
public sealed class UtilitiesGroupEditParams
{
    public UtilitiesGroupEditParams(string title, string? iconKey,
        UtilitiesMenuGroupDisplay displayMode = UtilitiesMenuGroupDisplay.Icon)
    {
        Title = title;
        IconKey = iconKey;
        DisplayMode = displayMode;
    }

    /// <summary>Имя группы.</summary>
    public string Title { get; }

    /// <summary>Ключ иконки каталога (пустой — без иконки).</summary>
    public string? IconKey { get; }

    /// <summary>Режим отображения группы: «Иконка» или «С надписью» (issue #146).</summary>
    public UtilitiesMenuGroupDisplay DisplayMode { get; }
}

/// <summary>Вариант выпадающего списка «Отображение» в диалоге правки группы (issue #146).</summary>
public sealed class UtilitiesGroupDisplayOption
{
    public UtilitiesGroupDisplayOption(UtilitiesMenuGroupDisplay mode, string labelKey)
    {
        Mode = mode;
        LabelKey = labelKey;
    }

    /// <summary>Режим отображения.</summary>
    public UtilitiesMenuGroupDisplay Mode { get; }

    /// <summary>Ключ локализации подписи варианта.</summary>
    public string LabelKey { get; }

    /// <summary>Локализованная подпись.</summary>
    public string Label => LocalizationManager.T(LabelKey);
}

/// <summary>
/// Редактор структуры меню «Утилиты» (issue #374): дерево компоновки с перемещением
/// пунктов (вверх/вниз/в группу), отключением (видимость), созданием групп, назначением
/// иконок из каталога программы и из внешнего файла, сбросом к стандартной компоновке
/// и сохранением/загрузкой компоновки в файл <c>utilities_menu_layout.json</c>.
/// Чистый .NET — используется обеими платформами (WPF и Avalonia).
/// </summary>
public class UtilitiesMenuEditorViewModel : ViewModelBase
{
    private readonly IUtilitiesMenuLayoutStore? _store;
    private UtilitiesMenuLayout _layout = UtilitiesMenuLayouts.Default();
    private UtilitiesMenuNodeItem? _selectedItem;
    private UtilitiesMenuNodeItem? _selectedGroup;

    /// <param name="store">Хранилище компоновки (для загрузки/сохранения); может быть null.</param>
    public UtilitiesMenuEditorViewModel(IUtilitiesMenuLayoutStore? store)
    {
        _store = store;
        var loaded = store?.Load();
        // «Новинки программы» (issue #4): группа автоматически добавляется в структуру,
        // даже если её нет в файле; в неё попадают пункты, отсутствующие в файле.
        _layout = UtilitiesMenuResolver.EnsureNewItemsGroup(loaded ?? UtilitiesMenuLayouts.Default())!;
        Roots = BuildTree(_layout.Root, null);

        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
        MoveToGroupCommand = new RelayCommand(_ => MoveSelectedToGroup(), _ => CanMoveToGroup);
        MoveToRootCommand = new RelayCommand(_ => MoveSelectedToRoot(), _ => CanMoveToRoot);
        AddGroupCommand = new RelayCommand(p => AddGroup(p as UtilitiesGroupEditParams));
        AddSeparatorCommand = new RelayCommand(_ => AddSeparator());
        EditGroupCommand = new RelayCommand(p => EditGroup(p as UtilitiesGroupEditParams), _ => CanEditGroup);
        DeleteGroupCommand = new RelayCommand(p => DeleteGroup(p is bool b && b), _ => CanDeleteGroup);
        SetCatalogIconCommand = new RelayCommand(p => ApplyCatalogIcon(p as string));
        SetExternalIconCommand = new RelayCommand(p => ApplyExternalIcon(p as string));
        ClearIconCommand = new RelayCommand(_ => ClearSelectedIcon(), _ => CanClearIcon());
        ResetDefaultCommand = new RelayCommand(_ => ResetToDefault());
        SaveCommand = new RelayCommand(_ => Save());
        LoadCommand = new RelayCommand(_ => Load());
        CloseCommand = new RelayCommand(_ => Accepted = true);
        ToggleVisibleCommand = new RelayCommand(_ => { /* видимость переключается чекбоксом напрямую */ });
    }

    /// <summary>Корневые узлы дерева компоновки.</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Roots { get; }

    /// <summary>Выбранный узел дерева (объект операций перемещения/иконки).</summary>
    public UtilitiesMenuNodeItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetPropertyWithRelated(ref _selectedItem, value, nameof(SelectedItem), nameof(HasSelection), nameof(CanMoveSelected), nameof(CanMoveToGroup), nameof(CanMoveToRoot), nameof(CanClearIcon), nameof(CanEditGroup), nameof(CanDeleteGroup)))
            {
                // Показываем в поле выбора иконки текущую (эффективную) иконку узла —
                // в т.ч. для пунктов меню и типовых групп, у которых иконка берётся из
                // каталога, а не из компоновки (issue #374).
                SelectedIconKey = value?.EffectiveIconKey ?? string.Empty;
                RaiseCommandsCanExecuteChanged();
            }
        }
    }

    /// <summary>Выбранная группа-назначение (для «В группу»).</summary>
    public UtilitiesMenuNodeItem? SelectedGroup
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

    /// <summary>Можно ли перенести выбранный узел в корень дерева (issue #374).</summary>
    public bool CanMoveToRoot => SelectedItem is not null && SelectedItem.Parent is not null;

    /// <summary>Выбранный ключ иконки каталога (ComboBox редактора).</summary>
    public string SelectedIconKey
    {
        get => _selectedIconKey;
        set => SetProperty(ref _selectedIconKey, value);
    }

    private string _selectedIconKey = string.Empty;

    /// <summary>Диалог завершён нажатием «Готово».</summary>
    public bool Accepted { get; private set; }

    /// <summary>Доступные иконки из каталога для назначения (ключ → подпись).</summary>
    public IReadOnlyList<UtilitiesIconOption> CatalogIcons { get; } = BuildCatalogIcons();

    /// <summary>Варианты настройки «Отображение» в диалоге правки группы (issue #146).</summary>
    public static IReadOnlyList<UtilitiesGroupDisplayOption> GroupDisplayOptions { get; } = new[]
    {
        new UtilitiesGroupDisplayOption(UtilitiesMenuGroupDisplay.Icon, "Utilities.Editor.DisplayIcon"),
        new UtilitiesGroupDisplayOption(UtilitiesMenuGroupDisplay.IconWithLabel, "Utilities.Editor.DisplayIconWithLabel"),
    };

    /// <summary>Все группы дерева (для выпадающего списка «Поместить в группу»).</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Groups { get; } = new();

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveToGroupCommand { get; }
    public RelayCommand MoveToRootCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand AddSeparatorCommand { get; }
    public RelayCommand EditGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand SetCatalogIconCommand { get; }
    public RelayCommand SetExternalIconCommand { get; }
    public RelayCommand ClearIconCommand { get; }
    public RelayCommand ResetDefaultCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }

    /// <summary>Сохранённая компоновка (для построителя меню после закрытия редактора).</summary>
    public UtilitiesMenuLayout Layout => _layout;

    /// <summary>Восстанавливает дерево из компоновки.</summary>
    private ObservableCollection<UtilitiesMenuNodeItem> BuildTree(IEnumerable<UtilitiesMenuNode> nodes, UtilitiesMenuNodeItem? parent)
    {
        var result = new ObservableCollection<UtilitiesMenuNodeItem>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            var item = new UtilitiesMenuNodeItem(node, parent);
            foreach (var child in BuildTree(node.Children, item))
                item.Children.Add(child);
            result.Add(item);
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
                Groups.Add(item);
            CollectGroups(item);
        }
        return result;
    }

    private void CollectGroups(UtilitiesMenuNodeItem item)
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

    /// <summary>Можно ли поместить выбранный узел в выбранную группу (issue #374).</summary>
    public bool CanMoveToGroup
    {
        get
        {
            var item = SelectedItem;
            var group = SelectedGroup;
            if (item is null || group is null || ReferenceEquals(item, group))
                return false;
            // Группу можно вложить в другую группу, но нельзя — в саму себя или в своего
            // потомка (иначе получится цикл) — issue #374.
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

    /// <summary>Переносит выбранный узел в корень дерева (issue #374).</summary>
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

    /// <summary>Добавляет узел-разделитель после выбранного узла (или в конец корня) — issue #374.</summary>
    private void AddSeparator()
    {
        var node = new UtilitiesMenuNode { Kind = "separator" };
        var selected = SelectedItem;
        if (selected is not null)
        {
            var siblings = selected.Parent is null ? Roots : selected.Parent.Children;
            var index = siblings.IndexOf(selected);
            var item = new UtilitiesMenuNodeItem(node, selected.Parent);
            siblings.Insert(index + 1, item);
            SelectedItem = item;
        }
        else
        {
            var item = new UtilitiesMenuNodeItem(node, null);
            Roots.Add(item);
            SelectedItem = item;
        }
    }

    private void AddGroup(UtilitiesGroupEditParams? editParams)
    {
        var title = editParams is not null && !string.IsNullOrWhiteSpace(editParams.Title)
            ? editParams.Title!.Trim()
            : LocalizationManager.T("Utilities.Editor.NewGroup");
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = title,
            DisplayMode = editParams?.DisplayMode ?? UtilitiesMenuGroupDisplay.Icon,
        };
        ApplyIconToNode(group, editParams?.IconKey);
        var parent = IsGroup(SelectedItem) ? SelectedItem : null;
        var item = new UtilitiesMenuNodeItem(group, parent);
        if (parent is null)
            Roots.Add(item);
        else
            parent.Children.Add(item);
        Groups.Add(item);
        SelectedItem = item;
    }

    /// <summary>Переименование выбранной группы (диалог «Правка группы»).</summary>
    private void EditGroup(UtilitiesGroupEditParams? editParams)
    {
        var item = SelectedItem;
        if (item is null || !IsGroup(item) || editParams is null)
            return;
        if (!string.IsNullOrWhiteSpace(editParams.Title))
            item.Node.CustomTitle = editParams.Title!.Trim();
        item.Node.DisplayMode = editParams.DisplayMode;
        ApplyIconToNode(item.Node, editParams.IconKey);
        item.NotifyChanged();
    }

    /// <summary>Удаление выбранного узла: разделитель удаляется сразу; группа — при
    /// <paramref name="moveToRoot"/> дети переносятся в корень на место группы (с сохранением
    /// порядка), иначе группа удаляется вместе с детьми. После удаления выбирается узел,
    /// занявший место удалённого (тот же индекс в списке сиблингов; если индекс за концом —
    /// предыдущий; при переносе детей — первый перенесённый ребёнок) — issue #374.</summary>
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
    /// если индекс за концом — предыдущий (issue #374).</summary>
    private void SelectNodeAt(ObservableCollection<UtilitiesMenuNodeItem> siblings, int index)
    {
        if (siblings.Count == 0)
        {
            SelectedItem = null;
            return;
        }
        SelectedItem = siblings[Math.Min(index, siblings.Count - 1)];
    }

    /// <summary>Назначает иконку каталога узлу (ключ → ключ+цвет, сброс внешнего файла).</summary>
    private void ApplyIconToNode(UtilitiesMenuNode node, string? iconKey)
    {
        if (node is null || string.IsNullOrWhiteSpace(iconKey))
            return;
        var option = CatalogIcons.FirstOrDefault(o => string.Equals(o.Key, iconKey, StringComparison.Ordinal));
        if (option is null)
            return;
        node.IconKey = option.Key;
        node.IconColor = option.Color;
        node.ExternalIconPath = null;
    }

    /// <summary>True — узел является группой (подменю).</summary>
    private static bool IsGroup(UtilitiesMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "group", StringComparison.Ordinal);

    /// <summary>True — узел является разделителем (issue #374).</summary>
    private static bool IsSeparator(UtilitiesMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "separator", StringComparison.Ordinal);

    private void ApplyCatalogIcon(string? key)
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(key))
            return;
        var option = CatalogIcons.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
        if (option is null)
            return;
        item.Node.IconKey = option.Key;
        item.Node.IconColor = option.Color;
        item.Node.ExternalIconPath = null;
        item.NotifyChanged();
    }

    private void ApplyExternalIcon(string path)
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(path))
            return;
        item.Node.ExternalIconPath = path;
        item.NotifyChanged();
    }

    private bool CanClearIcon() => SelectedItem is not null &&
        (!string.IsNullOrWhiteSpace(SelectedItem!.Node.IconKey) ||
         !string.IsNullOrWhiteSpace(SelectedItem!.Node.IconColor) ||
         !string.IsNullOrWhiteSpace(SelectedItem!.Node.ExternalIconPath));

    private void ClearSelectedIcon()
    {
        var item = SelectedItem;
        if (item is null)
            return;
        item.Node.IconKey = null;
        item.Node.IconColor = null;
        item.Node.ExternalIconPath = null;
        item.NotifyChanged();
    }

    private void ResetToDefault()
    {
        // «Новинки программы» (issue #4): после сброса группа тоже добавляется
        // автоматически, если в каталоге есть неразмещённые пункты-новинки.
        _layout = UtilitiesMenuResolver.EnsureNewItemsGroup(UtilitiesMenuLayouts.Default())!;
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
        var layout = new UtilitiesMenuLayout { SchemaVersion = UtilitiesMenuLayouts.CurrentSchemaVersion };
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
        // Issue #146: главное окно перестраивает меню и кнопки разделов сразу,
        // без перезапуска (WPF — видимость кнопок, Avalonia — панель команд).
        UtilitiesMenuEvents.RaiseLayoutChanged();
    }

    private static UtilitiesMenuNode ToNode(UtilitiesMenuNodeItem item)
    {
        var node = new UtilitiesMenuNode
        {
            Kind = item.Node.Kind,
            ItemId = item.Node.ItemId,
            GroupId = item.Node.GroupId,
            CustomTitle = item.Node.CustomTitle,
            IconKey = item.Node.IconKey,
            IconColor = item.Node.IconColor,
            ExternalIconPath = item.Node.ExternalIconPath,
            DisplayMode = item.Node.DisplayMode,
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
        // даже если её нет в файле.
        _layout = UtilitiesMenuResolver.EnsureNewItemsGroup(loaded)!;
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
        ClearIconCommand.RaiseCanExecuteChanged();
        EditGroupCommand.RaiseCanExecuteChanged();
        DeleteGroupCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Единый каталог иконок пользовательских групп и пунктов меню (issue #62): строится
    /// из <see cref="UtilitiesMenuCatalog.All"/> (ключ PackIcon + цвет) и иконок стандартных
    /// групп (issue #81: без них иконку «Новинки программы» — NewBox — нельзя было выбрать
    /// при правке структуры, а у группы «Новинки» меню «Утилиты» не было своей иконки).
    /// Используется редакторами «Утилиты», «Контекстное меню» и «Меню в трее» — один набор
    /// на все редакторы.
    /// В каталог попадают только РЕЗОЛВИМЫЕ ключи: <see cref="UtilitiesMenuCatalog.WpfIconGeometryKey"/>
    /// отображает PackIcon-имя на ключ геометрии Icon*, которую реально рисуют адаптеры
    /// (меню «Утилиты» и трей) — нерезолвимый ключ показывал бы «совсем другую» иконку.
    /// </summary>
    internal static IReadOnlyList<UtilitiesIconOption> BuildCatalogIcons()
    {
        var result = new List<UtilitiesIconOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (string.IsNullOrWhiteSpace(item.WpfIconKind) || string.IsNullOrWhiteSpace(item.IconColor))
                continue;
            if (string.IsNullOrEmpty(UtilitiesMenuCatalog.WpfIconGeometryKey(item.WpfIconKind)))
                continue;
            if (seen.Add(item.WpfIconKind + "|" + item.IconColor))
                result.Add(new UtilitiesIconOption(item.WpfIconKind, item.IconColor));
        }
        // Иконки стандартных подменю каталога (issue #81): «Новинки программы» (NewBox),
        // «Опции блокировки» (Lock), «Информация» (Information), «Операции» (Tune).
        foreach (var groupId in new[]
                 {
                     UtilitiesMenuCatalog.GroupUtilities,
                     UtilitiesMenuCatalog.GroupNewItems,
                     UtilitiesMenuCatalog.GroupLockOptions,
                     UtilitiesMenuCatalog.GroupInformation,
                     UtilitiesMenuCatalog.GroupOperations,
                 })
        {
            var wpfKind = UtilitiesMenuCatalog.GroupWpfIconKind(groupId);
            var color = UtilitiesMenuCatalog.GroupIconColor(groupId);
            if (string.IsNullOrWhiteSpace(wpfKind) || string.IsNullOrWhiteSpace(color))
                continue;
            if (string.IsNullOrEmpty(UtilitiesMenuCatalog.WpfIconGeometryKey(wpfKind)))
                continue;
            if (seen.Add(wpfKind + "|" + color))
                result.Add(new UtilitiesIconOption(wpfKind, color));
        }
        // issue #146: детерминированная запасная «папка» должна быть выбираема в диалоге —
        // иначе группа без иконки показывала бы в настройке первый пункт каталога,
        // а в меню — папку.
        if (!result.Any(o => string.Equals(o.Key, UtilitiesMenuCatalog.LegacyFallbackIconKey.Replace("Icon", string.Empty), StringComparison.Ordinal)))
            result.Add(new UtilitiesIconOption("Folder", "#6B7280"));
        return result;
    }
}

/// <summary>Вариант иконки каталога для редактора (ключ PackIcon + цвет).</summary>
public sealed class UtilitiesIconOption
{
    public UtilitiesIconOption(string key, string color)
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
/// Узел дерева редактора: обёртка над <see cref="UtilitiesMenuNode"/> с коллекцией
/// дочерних узлов и родителем (для операций перемещения). Изменения свойств узла
/// транслируются в <see cref="UtilitiesMenuNode"/> компоновки.
/// </summary>
public sealed class UtilitiesMenuNodeItem : ViewModelBase
{
    /// <param name="node">Узел компоновки, который оборачиваем.</param>
    /// <param name="parent">Родительский узел дерева (null — корень).</param>
    public UtilitiesMenuNodeItem(UtilitiesMenuNode node, UtilitiesMenuNodeItem? parent)
    {
        Node = node;
        Parent = parent;
    }

    /// <summary>Оборачиваемый узел компоновки.</summary>
    public UtilitiesMenuNode Node { get; }

    /// <summary>Родительский узел дерева (null — корневой список).</summary>
    public UtilitiesMenuNodeItem? Parent { get; set; }

    /// <summary>Дочерние узлы (для групп).</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Children { get; } = new();

    /// <summary>Заголовок узла: разделитель / пользовательский или локализованный заголовок
    /// группы / словарь для пунктов.</summary>
    public string Title
    {
        get
        {
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return LocalizationManager.T("Utilities.Editor.SeparatorLabel");
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(Node.CustomTitle))
                    return Node.CustomTitle!;
                var key = UtilitiesMenuCatalog.GroupTitleKey(Node.GroupId ?? string.Empty);
                return string.IsNullOrEmpty(key) ? LocalizationManager.T("Utilities.Editor.CustomGroup") : LocalizationManager.T(key);
            }
            var catalog = UtilitiesMenuCatalog.Find(Node.ItemId);
            return catalog is null
                ? (Node.ItemId ?? "?")
                : LocalizationManager.T(catalog.TextKey);
        }
    }

    /// <summary>Пометка вида: «группа» / «пункт» / «разделитель».</summary>
    public string KindLabel =>
        string.Equals(Node.Kind, "separator", StringComparison.Ordinal)
            ? LocalizationManager.T("Utilities.Editor.KindSeparator")
            : string.Equals(Node.Kind, "group", StringComparison.Ordinal)
                ? LocalizationManager.T("Utilities.Editor.KindGroup")
                : LocalizationManager.T("Utilities.Editor.KindItem");

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
          string.Equals(Node.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal));

    /// <summary>Строка для списка групп: заголовок.</summary>
    public string GroupLabel => Title;

    /// <summary>
    /// Эффективный ключ иконки для отображения: пользовательская из компоновки, иначе —
    /// из каталога (для пунктов меню и типовых групп), для разделителя — служебная
    /// иконка-линия (issue #374). Пусто — без иконки.
    /// </summary>
    public string EffectiveIconKey
    {
        get
        {
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return "Minus";
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
            {
                // issue #146: тот же расчёт, что в меню — сохранённая иконка, иначе
                // стандартная иконка группы, иначе детерминированная «папка».
                return UtilitiesMenuCatalog.ResolveGroupIconKeyWpf(Node.GroupId, Node.IconKey);
            }
            // issue #146: неизвестный сохранённый ключ не должен давать «другую» иконку —
            // падаем на иконку каталога, как и построители меню.
            if (!string.IsNullOrWhiteSpace(Node.IconKey) &&
                !string.IsNullOrWhiteSpace(UtilitiesMenuCatalog.WpfIconGeometryKey(Node.IconKey)))
            {
                return Node.IconKey!;
            }
            return UtilitiesMenuCatalog.Find(Node.ItemId)?.WpfIconKind ?? string.Empty;
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
            {
                var groupColor = UtilitiesMenuCatalog.GroupIconColor(Node.GroupId ?? string.Empty);
                // issue #146: у пользовательской группы без цвета — тот же серый,
                // что применяет меню (ParseMenuIconColor), иначе иконки различались.
                return string.IsNullOrWhiteSpace(groupColor) ? "#6B7280" : groupColor;
            }
            return UtilitiesMenuCatalog.Find(Node.ItemId)?.IconColor ?? string.Empty;
        }
    }

    /// <summary>Ключ иконки для отображения в дереве (пусто — без иконки).</summary>
    public string DisplayIconKey => EffectiveIconKey;

    /// <summary>
    /// Ключ геометрии <c>Icon*</c> для превью в дереве редактора (issue #146): то же
    /// разрешение, что в меню, поэтому настройка и рабочее меню показывают одну иконку.
    /// </summary>
    public string DisplayIconGeometryKey
    {
        get
        {
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return "IconMinus";
            var key = EffectiveIconKey;
            if (string.IsNullOrWhiteSpace(key))
                return "IconFolder";
            var mapped = UtilitiesMenuCatalog.WpfIconGeometryKey(key);
            return string.IsNullOrWhiteSpace(mapped) ? "IconFolder" : mapped;
        }
    }

    /// <summary>Цвет иконки (#RRGGBB) для отображения в дереве.</summary>
    public string DisplayIconColor => EffectiveIconColor;

    /// <summary>Есть ли иконка для отображения в дереве (видимость превью).</summary>
    public bool HasDisplayIcon => !string.IsNullOrWhiteSpace(EffectiveIconKey);

    /// <summary>Оповещает UI об изменении внешних свойств узла (иконка, заголовок).</summary>
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