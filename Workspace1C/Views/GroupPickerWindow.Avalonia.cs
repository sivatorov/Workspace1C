#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Вид объекта, для которого выбирается группа: от него зависят заголовок,
    /// подзаголовок и текст справки (GroupPickerWindow.xaml.cs:11).
    /// В Windows-версии это перечисление объявлено в code-behind, который
    /// в Linux-сборку не входит, поэтому здесь свой такой же.
    /// </summary>
    public enum GroupPickerObjectKind
    {
        Group,
        Infobase
    }

    /// <summary>
    /// Диалог выбора группы в виде дерева (или «Без группы» / корень) в стиле Material Design:
    /// шапка с иконкой и подзаголовком, поле поиска, переключатель сортировки, карточка-дерево
    /// с цветными «чипами» иконок и панель действий. Avalonia/Linux-версия WPF-окна
    /// <see cref="GroupPickerWindow"/>.
    /// </summary>
    public class GroupPickerWindow : ModalWindowBase
    {
        private readonly IReadOnlyList<Group> _groups;
        private readonly List<Group> _allowed;
        private readonly string _currentGroupId;
        private readonly bool _allowNone;
        private readonly string _noneLabel;
        private bool _sortAscending = true;
        private string _filterText = string.Empty;
        private GroupNodeViewModel? _selectedNode;

        private readonly TreeView _tree = new() { SelectionMode = SelectionMode.Single };
        private Button? _selectButton;
        private ToggleButton? _sortAsc;
        private ToggleButton? _sortDesc;
        private TextBox? _searchBox;
        private Button? _clearSearch;
        private TextBlock? _emptyLabel;
        private TextBlock? _pathLabel;

        /// <param name="groups">Список групп.</param>
        /// <param name="currentGroupId">Текущая выбранная группа (для подсветки).</param>
        /// <param name="excludeGroupId">Группа, которую нельзя выбрать (сама редактируемая + потомки отфильтруются).</param>
        /// <param name="allowNone">Разрешить выбор «Без группы» / корень.</param>
        /// <param name="noneLabel">Подпись корневого пункта.</param>
        /// <summary>Вид объекта, для которого выбирают группу: от него зависят формулировки.</summary>
        private readonly GroupPickerObjectKind _objectKind;

        private string TitleKey => _objectKind == GroupPickerObjectKind.Infobase
            ? "GroupPicker.TitleBase"
            : "GroupPicker.TitleGroup";

        private string SubtitleKey => _objectKind == GroupPickerObjectKind.Infobase
            ? "GroupPicker.SubtitleBase"
            : "GroupPicker.SubtitleGroup";

        private string HelpKey => _objectKind == GroupPickerObjectKind.Infobase
            ? "GroupPicker.HelpBase"
            : "GroupPicker.HelpGroup";

        public GroupPickerWindow(
            IEnumerable<Group> groups,
            string? currentGroupId = null,
            string? excludeGroupId = null,
            bool allowNone = true,
            string noneLabel = "",
            GroupPickerObjectKind kind = GroupPickerObjectKind.Group)
        {
            // Формулировки зависят от того, для чего выбирают группу: для базы
            // или для другой группы (GroupPickerWindow.xaml.cs:70).
            _objectKind = kind;
            Title = LocalizationManager.T(TitleKey);
            Width = 560;
            Height = 620;
            MinWidth = 460;
            MinHeight = 480;

            _groups = groups.ToList();
            _currentGroupId = currentGroupId ?? string.Empty;
            _allowNone = allowNone;
            _noneLabel = string.IsNullOrEmpty(noneLabel) ? LocalizationManager.T("Connection.NoGroup") : noneLabel;

            _allowed = string.IsNullOrEmpty(excludeGroupId)
                ? _groups.ToList()
                : _groups.Where(g =>
                        !string.Equals(g.Id, excludeGroupId, StringComparison.OrdinalIgnoreCase)
                        && !GroupHierarchyHelper.IsAncestorOrSelf(g.Id, excludeGroupId, _groups))
                    .ToList();

            Content = BuildRoot();
            RefreshTree();
        }

        /// <summary>Выбранная группа; null — без группы / корень.</summary>
        public Group? ResultGroup => _selectedNode?.Group;

        /// <summary>Id выбранной группы; пустая строка — корень / без группы.</summary>
        public string ResultGroupId => _selectedNode?.Group?.Id ?? string.Empty;

        /// <summary>Полный путь выбранной группы; пустая строка — без группы.</summary>
        public string ResultFullPath =>
            _selectedNode?.Group is null
                ? string.Empty
                : GroupHierarchyHelper.GetFullPath(_selectedNode.Group, _groups);

        // ------------------------------------------------------------------
        // Построение окна (Material Design)
        // ------------------------------------------------------------------

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));                      // 0 — шапка
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));                      // 1 — поиск + сортировка
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));                      // 2 — подсказка
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // 3 — дерево
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));                      // 4 — действия

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var toolbar = BuildToolbar();
            Grid.SetRow(toolbar, 1);
            grid.Children.Add(toolbar);

            var hint = ThemedText(LocalizationManager.T("GroupPicker.Hint"), 12, secondary: true, FontWeight.Normal);
            // ThemedText не задаёт перенос, а TextBlock без него держит одну строку:
            // пояснение и подзаголовок обрезались по краю окна. В разметке WPF
            // на этих же строках TextWrapping="Wrap" стоит.
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(hint, 2);
            grid.Children.Add(hint);

            var treeCard = BuildTreeCard();
            Grid.SetRow(treeCard, 3);
            grid.Children.Add(treeCard);

            var footer = BuildFooter();
            Grid.SetRow(footer, 4);
            grid.Children.Add(footer);

            return grid;
        }

        private Control BuildHeader()
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

            var headerIcon = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(UiMetrics.RadiusMd),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = IconHelper.MakeIcon("IconFolder", 20, "TextOnAccentBrush")
            };
            ThemeBrushes.Bind(headerIcon, Border.BackgroundProperty, "AccentBrush");
            DockPanel.SetDock(headerIcon, Dock.Left);
            header.Children.Add(headerIcon);

            // Справка добавляется до заголовка: в DockPanel последний ребёнок
            // занимает остаток, и после titleStack кружок «?» встал бы не к краю.
            var help = new HelpLink
            {
                HelpText = LocalizationManager.T(HelpKey),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(help, Dock.Right);
            header.Children.Add(help);

            var titleStack = new StackPanel { Spacing = 2 };
            // Перенос заголовка/подзаголовка (issue #55): в DockPanel при уменьшении
            // ширины окна текст переносится, а не обрезается правым краем.
            var title = ThemedText(LocalizationManager.T(TitleKey), 17, secondary: false, FontWeight.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            titleStack.Children.Add(title);
            var subtitle = ThemedText(LocalizationManager.T(SubtitleKey), 12.5, secondary: true, FontWeight.Normal);
            subtitle.TextWrapping = TextWrapping.Wrap;
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);

            return header;
        }

        private Control BuildToolbar()
        {
            var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            toolbar.Children.Add(BuildSearchField());

            // Кнопки сортировки: ширина 34 и поле 2,0 из разметки
            // (GroupPickerWindow.xaml:86), зазор задаётся полем кнопок.
            var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            _sortAsc = BuildSortToggle("IconSortAscending", LocalizationManager.T("Main.SortGroupsAscending"), isAscending: true);
            _sortDesc = BuildSortToggle("IconSortDescending", LocalizationManager.T("Main.SortGroupsDescending"), isAscending: false);
            sortPanel.Children.Add(_sortAsc);
            sortPanel.Children.Add(_sortDesc);
            Grid.SetColumn(sortPanel, 1);
            toolbar.Children.Add(sortPanel);

            return toolbar;
        }

        /// <summary>Поле поиска с иконкой и кнопкой очистки (стиль Material «outlined text field»).</summary>
        private Control BuildSearchField()
        {
            var field = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusLg),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 2)
            };
            ThemeBrushes.Bind(field, Border.BorderBrushProperty, "BorderColorBrush");
            ThemeBrushes.Bind(field, Border.BackgroundProperty, "CardBackgroundColorBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchIcon = IconHelper.MakeIcon("IconSearch", 16, "TextSecondaryBrush");
            searchIcon.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(searchIcon);
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 7),
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = LocalizationManager.T("GroupPicker.SearchPlaceholder")
            };
            _searchBox.GetObservable(TextBox.TextProperty).Subscribe(new ValueObserver<string?>(OnFilterChanged));
            grid.Children.Add(_searchBox);
            Grid.SetColumn(_searchBox, 1);

            _clearSearch = new Button
            {
                Content = IconHelper.MakeIcon("IconClose", UiMetrics.IconInline, "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsVisible = false
            };
            ToolTip.SetTip(_clearSearch, LocalizationManager.T("Main.ClearSearch"));
            _clearSearch.Click += (_, _) =>
            {
                if (_searchBox is not null) _searchBox.Text = string.Empty;
            };
            grid.Children.Add(_clearSearch);
            Grid.SetColumn(_clearSearch, 2);

            field.Child = grid;
            return field;
        }

        private void OnFilterChanged(string? value)
        {
            _filterText = value ?? string.Empty;
            if (_clearSearch is not null)
                _clearSearch.IsVisible = _filterText.Length > 0;
            RefreshTree();
        }

        /// <summary>Карточка-дерево групп с пустым состоянием поиска.</summary>
        private Control BuildTreeCard()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusLg),
                BorderThickness = new Thickness(1)
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");

            var root = new Grid();

            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(object),
                (item, _) => BuildTreeRow(item),
                item => item is GroupNodeViewModel g && g.HasChildren ? g.Children : Array.Empty<GroupNodeViewModel>());
            if (Application.Current?.TryFindResource(ControlThemes.GroupPickerTreeItem, out var treeItemTheme) == true
                && treeItemTheme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }
            _tree.SelectionChanged += (_, _) =>
            {
                _selectedNode = _tree.SelectedItem as GroupNodeViewModel;
                UpdateSelection();
            };
            // Двойной щелчок доходит до дерева только с handledEventsToo: у элемента
            // с детьми Avalonia помечает событие обработанным (раскрытие узла), а
            // разметка WPF подтверждает выбор при щелчке по любому узлу.
            _tree.AddHandler(InputElement.DoubleTappedEvent, (object? _, TappedEventArgs _) =>
            {
                if (_selectedNode is not null)
                    OnSelect_Click();
            }, RoutingStrategies.Bubble, handledEventsToo: true);

            var treeHost = new ScrollViewer
            {
                Content = _tree,
                Padding = new Thickness(6),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(treeHost, 0);
            root.Children.Add(treeHost);

            _emptyLabel = ThemedText(LocalizationManager.T("Main.EmptyNoResults"), 13, secondary: true, FontWeight.Normal);
            _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyLabel.VerticalAlignment = VerticalAlignment.Center;
            _emptyLabel.IsVisible = false;
            Grid.SetRow(_emptyLabel, 0);
            root.Children.Add(_emptyLabel);

            card.Child = root;
            return card;
        }

        private Control BuildTreeRow(object? item)
        {
            if (item is not GroupNodeViewModel node)
                return new TextBlock { Text = item?.ToString() ?? string.Empty };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1)
            };

            // Цветной чип со значком группы: размер задаётся отступом, как
            // в разметке (GroupPickerWindow.xaml:271), а не фиксированной
            // шириной с высотой. Аудит UI P1-2: чип остаётся исходным цветом
            // группы (HeaderStripeBrush), а не пастелью строки дерева.
            var chip = new Border
            {
                Padding = new Thickness(6, 3),
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Background = node.HeaderStripeBrush,
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Width = 18,
                    Height = 18,
                    Data = IconHelper.Geometry(node.Icon),
                    Stretch = Stretch.Uniform,
                    Fill = node.IconBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
            row.Children.Add(chip);

            var text = ThemedText(node.DisplayName, 14, secondary: false, FontWeight.Normal);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);

            return row;
        }

        private Control BuildFooter()
        {
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Сводка выбора слева: иконка + полный путь выбранной группы.
            var pathPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            pathPanel.Children.Add(IconHelper.MakeIcon("IconChevronRight", UiMetrics.IconControl, "TextSecondaryBrush"));
            _pathLabel = ThemedText(string.Empty, 12.5, secondary: true, FontWeight.Normal);
            _pathLabel.VerticalAlignment = VerticalAlignment.Center;
            _pathLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            _pathLabel.MaxWidth = 260;
            pathPanel.Children.Add(_pathLabel);
            Grid.SetColumn(pathPanel, 0);
            footer.Children.Add(pathPanel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            // Главное действие «Выбрать» — слева, «Отмена» — справа.
            _selectButton = BuildActionButton(
                "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush",
                ActionContent("IconCheck", LocalizationManager.T("Common.Select"), "TextOnAccentBrush"),
                minWidth: 116, isCancel: false, isDefault: true, onClick: OnSelect_Click);
            buttons.Children.Add(_selectButton);

            // Отмена — вторичная: фон карточки, контур и текст темы (issue #26).
            buttons.Children.Add(BuildActionButton(
                "CardBackgroundBrush", "ItemHoverBrush", "SidebarHoverBrush", "BorderColorBrush",
                ActionContent("IconClose", LocalizationManager.T("Common.Cancel"), "SecondaryButtonTextBrush", iconSize: 15),
                minWidth: 116, isCancel: true, isDefault: false, onClick: () => Close()));
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            return footer;
        }

        // ------------------------------------------------------------------
        // Служебные построители (Material-кнопки, тексты)
        // ------------------------------------------------------------------

        /// <summary>Кнопка в стиле Material: скруглённая, три состояния фона из темы.</summary>
        private Button BuildActionButton(
            string baseKey, string hoverKey, string pressedKey, string borderKey,
            Control content, double minWidth, bool isCancel, bool isDefault, Action onClick)
        {
            // Высота 38, отступ 14 на 6 и контур 1.5 из стилей разметки
            // (GroupPickerWindow.xaml:117): у акцентной кнопки контура нет.
            var btn = new Button
            {
                Content = content,
                MinWidth = minWidth,
                Height = 38,
                Padding = new Thickness(14, 6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(isDefault ? 0 : 1.5),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsCancel = isCancel,
                IsDefault = isDefault
            };

            btn.Theme = new ControlTheme(typeof(Button))
            {
                Setters =
                {
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((_, _) =>
                    {
                        var border = new Border { CornerRadius = new CornerRadius(UiMetrics.RadiusLg), BorderThickness = new Thickness(1) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        border[!Border.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                        UiMetrics.AddBrushTransition(border);
                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                }
            };

            WireState(btn, baseKey, hoverKey, pressedKey, borderKey, isActive: null);
            btn.Click += (_, _) => onClick();
            return btn;
        }

        /// <summary>Переключатель сортировки (А→Я / Я→А): активный заливается акцентом, значок белеет.</summary>
        private ToggleButton BuildSortToggle(string iconKey, string tooltip, bool isAscending)
        {
            var toggle = new ToggleButton
            {
                Width = 34,
                Height = 32,
                Margin = new Thickness(2, 0),
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsChecked = isAscending == _sortAscending,
                Content = IconHelper.MakeIcon(iconKey, 16, out var iconPath)
            };
            ToolTip.SetTip(toggle, tooltip);

            toggle.Theme = new ControlTheme(typeof(ToggleButton))
            {
                Setters =
                {
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToggleButton>((_, _) =>
                    {
                        var border = new Border { CornerRadius = new CornerRadius(UiMetrics.RadiusMd), BorderThickness = new Thickness(1) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        border[!Border.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                        UiMetrics.AddBrushTransition(border);
                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                }
            };

            var state = new ToggleState { Icon = iconPath };
            ThemeBrushes.Observe(toggle, "ItemHoverBrush", b => { state.Hover = b; state.Apply(toggle); });
            ThemeBrushes.Observe(toggle, "ButtonTextBrush", b => { state.IconBase = b; state.Apply(toggle); });
            ThemeBrushes.Observe(toggle, "TextOnAccentBrush", b => { state.IconOnAccent = b; state.Apply(toggle); });
            ThemeBrushes.Observe(toggle, "SecondaryButtonPressedBrush", b => { state.Pressed = b; state.Apply(toggle); });
            ThemeBrushes.Observe(toggle, "BorderColorBrush", b => { state.Border = b; state.Apply(toggle); });
            ThemeBrushes.Observe(toggle, "AccentBrush", b => { state.Accent = b; state.Apply(toggle); });

            toggle.PointerEntered += (_, _) => { state.Hovered = true; state.Apply(toggle); };
            toggle.PointerExited += (_, _) => { state.Hovered = false; state.IsPressed = false; state.Apply(toggle); };
            toggle.PointerPressed += (_, _) => { state.IsPressed = true; state.Apply(toggle); };
            toggle.PointerReleased += (_, _) => { state.IsPressed = false; state.Apply(toggle); };
            toggle.PointerCaptureLost += (_, _) => { state.IsPressed = false; state.Apply(toggle); };
            toggle.GetObservable(ToggleButton.IsCheckedProperty).Subscribe(new ValueObserver<bool?>(_ => state.Apply(toggle)));
            toggle.GetObservable(ToggleButton.IsEnabledProperty).Subscribe(new ValueObserver<bool>(_ => state.Apply(toggle)));

            var self = toggle;
            toggle.Click += (_, _) =>
            {
                if (self.IsChecked != true) return;
                if (isAscending)
                {
                    _sortAscending = true;
                    if (_sortDesc is not null) _sortDesc.IsChecked = false;
                }
                else
                {
                    _sortAscending = false;
                    if (_sortAsc is not null) _sortAsc.IsChecked = false;
                }
                RefreshTree();
            };

            state.Apply(toggle);
            return toggle;
        }

        /// <summary>Подключает состояния фона/границы/фокуса к обычной кнопке.</summary>
        private static void WireState(Button btn,
            string baseKey, string hoverKey, string pressedKey, string borderKey, bool? isActive)
        {
            var state = new ToggleState();
            if (baseKey.Length > 0)
                ThemeBrushes.Observe(btn, baseKey, b => { state.Base = b; state.Apply(btn); });
            ThemeBrushes.Observe(btn, hoverKey, b => { state.Hover = b; state.Apply(btn); });
            ThemeBrushes.Observe(btn, pressedKey, b => { state.Pressed = b; state.Apply(btn); });
            ThemeBrushes.Observe(btn, borderKey, b => { state.Border = b; state.Apply(btn); });
            ThemeBrushes.Observe(btn, "AccentBrush", b => { state.Accent = b; state.Apply(btn); });

            btn.PointerEntered += (_, _) => { state.Hovered = true; state.Apply(btn); };
            btn.PointerExited += (_, _) => { state.Hovered = false; state.IsPressed = false; state.Apply(btn); };
            btn.PointerPressed += (_, _) => { state.IsPressed = true; state.Apply(btn); };
            btn.PointerReleased += (_, _) => { state.IsPressed = false; state.Apply(btn); };
            btn.PointerCaptureLost += (_, _) => { state.IsPressed = false; state.Apply(btn); };
            btn.GetObservable(Button.IsEnabledProperty).Subscribe(new ValueObserver<bool>(_ => state.Apply(btn)));
            btn.GetObservable(Button.IsKeyboardFocusWithinProperty).Subscribe(new ValueObserver<bool>(_ => state.Apply(btn)));
            state.Apply(btn);
        }

        /// <summary>Текстовый блок, окрашенный кистью темы (основной или вторичный текст).</summary>
        private static TextBlock ThemedText(string text, double fontSize, bool secondary, FontWeight weight)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, secondary ? "TextSecondaryBrush" : "TextPrimaryBrush");
            return tb;
        }

        /// <summary>Содержимое кнопки: иконка + подпись цветом из темы.</summary>
        private static Control ActionContent(string iconKey, string text, string brushKey, double iconSize = 16)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    IconHelper.MakeIcon(iconKey, iconSize, brushKey),
                    tb
                }
            };
        }

        // ------------------------------------------------------------------
        // Логика дерева, поиска и сортировки
        // ------------------------------------------------------------------

        /// <summary>Перестраивает дерево с учётом сортировки и текста поиска.</summary>
        private void RefreshTree()
        {
            var roots = GroupNodeViewModel.BuildTree(_allowed);

            var groupComparer = StringComparer.OrdinalIgnoreCase;
            roots.Sort(_sortAscending
                ? (a, b) => groupComparer.Compare(a.DisplayName, b.DisplayName)
                : (a, b) => groupComparer.Compare(b.DisplayName, a.DisplayName));
            foreach (var root in roots)
                root.SortChildrenRecursive(_sortAscending);

            var items = new List<GroupNodeViewModel>();
            if (_allowNone)
                items.Add(new GroupNodeViewModel(null, displayName: _noneLabel));
            items.AddRange(roots);

            // Поиск фильтрует дерево, сохраняя иерархию (остаются узлы, где совпал
            // сам узел или хотя бы один потомок).
            if (!string.IsNullOrWhiteSpace(_filterText))
                items = FilterRoots(items, _filterText.Trim());

            _tree.ItemsSource = items;

            if (_emptyLabel is not null)
            {
                var hasMatch = items.Count > 0;
                _emptyLabel.IsVisible = !hasMatch;
                if (_tree.Parent is ScrollViewer sv)
                    sv.IsVisible = hasMatch;
            }

            if (items.Count == 0)
            {
                _selectedNode = null;
                UpdateSelection();
                return;
            }

            if (!string.IsNullOrEmpty(_currentGroupId))
                SelectById(items, _currentGroupId);
            else if (_allowNone && items.Count > 0 && _selectedNode is null)
            {
                _selectedNode = items[0];
                UpdateSelection();
            }
            else
            {
                UpdateSelection();
            }
        }

        /// <summary>Фильтрует корневые узлы по подстроке имени, сохраняя ветви иерархии.</summary>
        private static List<GroupNodeViewModel> FilterRoots(IEnumerable<GroupNodeViewModel> roots, string filter)
        {
            var result = new List<GroupNodeViewModel>();
            foreach (var root in roots)
            {
                var copy = FilterNode(root, filter);
                if (copy is not null)
                    result.Add(copy);
            }
            return result;
        }

        private static GroupNodeViewModel? FilterNode(GroupNodeViewModel node, string filter)
        {
            var selfMatches = node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var keptChildren = new List<GroupNodeViewModel>();
            foreach (var child in node.Children)
            {
                var childCopy = FilterNode(child, filter);
                if (childCopy is not null)
                    keptChildren.Add(childCopy);
            }

            if (!selfMatches && keptChildren.Count == 0)
                return null;

            var copy = new GroupNodeViewModel(node.Group, displayName: node.DisplayName);
            foreach (var child in keptChildren)
                copy.Children.Add(child);
            return copy;
        }

        private void UpdateSelection()
        {
            var enabled = _selectedNode is not null;
            if (_selectButton is not null)
                _selectButton.IsEnabled = enabled;

            if (_pathLabel is not null)
            {
                _pathLabel.Text = _selectedNode?.Group is null
                    ? (_allowNone ? _noneLabel : string.Empty)
                    : GroupHierarchyHelper.GetFullPath(_selectedNode.Group, _groups);
                _pathLabel.IsVisible = _selectedNode is not null;
            }
        }

        private void SelectById(IEnumerable<GroupNodeViewModel> roots, string groupId)
        {
            foreach (var root in roots)
            {
                if (TrySelect(root, groupId))
                    return;
            }
        }

        private bool TrySelect(GroupNodeViewModel node, string groupId)
        {
            if (node.Group is not null &&
                string.Equals(node.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNode = node;
                node.IsExpanded = true;
                // Единственный источник выделения — TreeView.SelectedItem (одиночный режим):
                // подсвечивается ровно один узел, родители не выделяются.
                _tree.SelectedItem = node;
                UpdateSelection();
                return true;
            }

            foreach (var child in node.Children)
            {
                if (TrySelect(child, groupId))
                    return true;
            }
            return false;
        }

        private void OnSelect_Click()
        {
            if (_selectedNode is null)
                return;
            DialogResult = true;
            Close();
        }

        /// <summary>Состояние кнопки/переключателя для ручного пересчёта фона из темы.</summary>
        private sealed class ToggleState
        {
            public IBrush Base = Brushes.Transparent;
            public IBrush Hover = Brushes.Transparent;
            public IBrush Pressed = Brushes.Transparent;
            public IBrush Border = Brushes.Transparent;
            public IBrush Accent = Brushes.Transparent;
            public IBrush IconBase = Brushes.Transparent;
            public IBrush IconOnAccent = Brushes.Transparent;
            public Avalonia.Controls.Shapes.Path? Icon;
            public bool Hovered;
            public bool IsPressed;

            public void Apply(Button btn)
            {
                var isActive = btn is ToggleButton t && t.IsChecked == true;
                var fill = isActive ? Accent : (IsPressed ? Pressed : (Hovered ? Hover : Base));
                if (Icon is not null)
                    Icon.Fill = isActive ? IconOnAccent : IconBase;

                if (!btn.IsEnabled)
                {
                    btn.Opacity = 0.5;
                    btn.Background = fill;
                    btn.BorderBrush = Border;
                    return;
                }

                btn.Opacity = 1.0;
                btn.Background = fill;
                btn.BorderBrush = Border;
            }
        }

        /// <summary>Простой наблюдатель значения (для TextBox.Text, IsChecked и пр.).</summary>
        private sealed class ValueObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;
            public ValueObserver(Action<T> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _onNext(value);
        }
    }
}
#endif