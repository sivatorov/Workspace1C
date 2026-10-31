#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (как в стартере): фильтр Все / x32 / x64,
    /// сортировка, дерево 8.3 → 8.3.27 → 8.3.27.2214 (x64). Avalonia/Linux-версия
    /// WPF-окна <see cref="PlatformVersionPickerWindow"/>.
    /// </summary>
    public class PlatformVersionPickerWindow : ModalWindowBase
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionInfo> _allInfos = new();
        private string _currentVersion = string.Empty;
        private bool _sortAscending; // по умолчанию — свежие версии сверху
        private string _archFilter = "all";
        private bool _isRestoringSelection; // перестроение дерева: гасим SelectionChanged(null) (#304)

        // Целевой узел для первичного выделения текущей версии (и его предки).
        private PlatformVersionGroup? _initialLeaf;
        private HashSet<PlatformVersionGroup>? _initialAncestors;

        private readonly TreeView _tree = new();
        private Button _selectButton = null!;
        private readonly RadioButton _filterAll = new() { Content = LocalizationManager.T("Common.All"), IsChecked = true, GroupName = "Arch" };
        private readonly RadioButton _filterX32 = new() { Content = LocalizationManager.T("PlatformVersionPicker.FilterX32"), GroupName = "Arch" };
        private readonly RadioButton _filterX64 = new() { Content = LocalizationManager.T("PlatformVersionPicker.FilterX64"), GroupName = "Arch" };
        private readonly ToggleButton _sortAsc = new();
        private readonly ToggleButton _sortDesc = new() { IsChecked = true };

        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            Title = LocalizationManager.T("PlatformVersionPicker.Title");
            Width = 520;
            Height = 560;
            MinWidth = 400;
            MinHeight = 400;

            _currentVersion = currentVersion ?? "";

            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _allInfos = PlatformVersionService.FindInstalledVersionInfos(extras);
            if (_allInfos.Count == 0 && installedPlatformVersions != null)
            {
                _allInfos = installedPlatformVersions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new PlatformVersionInfo { Display = s.Trim(), Path = "" })
                    .ToList();
            }
            else if (installedPlatformVersions != null)
            {
                var known = new HashSet<string>(_allInfos.Select(i => i.Display), StringComparer.OrdinalIgnoreCase);
                foreach (var s in installedPlatformVersions)
                {
                    if (string.IsNullOrWhiteSpace(s) || known.Contains(s.Trim())) continue;
                    _allInfos.Add(new PlatformVersionInfo { Display = s.Trim(), Path = "" });
                }
            }

            Content = BuildRoot();
            RefreshTree();
        }

        public string Result => _selectedVersion;

        private Control BuildRoot()
        {
            // Кнопка выбора создаётся до дерева: её доступность меняет обработчик
            // выделения, а выделение дерево умеет выставить само при подготовке
            // контейнеров.
            // Закрывает окно сам обработчик: у него есть проверка на пустой выбор,
            // и терять её нельзя (PlatformVersionPickerWindow.xaml:259).
            _selectButton = BuildConfirmActionButton("Common.Select", "IconCheck", 140,
                OnSelect_Click, closeOnClick: false);
            _selectButton.Classes.Add("dimmed");
            _selectButton.IsEnabled = false;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Фильтр разрядности и сортировка лежат в карточке с отступом 8,6
            // и скруглением 6 (PlatformVersionPickerWindow.xaml:145): фильтры
            // слева, кнопки сортировки прижаты вправо.
            foreach (var radio in new[] { _filterAll, _filterX32, _filterX64 })
                radio.Styled(ControlThemes.ArchRadio);

            _filterAll.IsCheckedChanged += (_, _) =>
            {
                if (_filterAll.IsChecked != true) return;
                _archFilter = "all"; RefreshTree();
            };
            _filterX32.IsCheckedChanged += (_, _) =>
            {
                if (_filterX32.IsChecked != true) return;
                _archFilter = "x32"; RefreshTree();
            };
            _filterX64.IsCheckedChanged += (_, _) =>
            {
                if (_filterX64.IsChecked != true) return;
                _archFilter = "x64"; RefreshTree();
            };

            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            filterPanel.Children.Add(_filterAll);
            filterPanel.Children.Add(_filterX32);
            filterPanel.Children.Add(_filterX64);

            _sortAsc.Content = IconHelper.MakeIcon("IconSortAscending", 16, out var sortAscIcon);
            _sortDesc.Content = IconHelper.MakeIcon("IconSortDescending", 16, out var sortDescIcon);
            foreach (var (toggle, icon) in new[] { (_sortAsc, sortAscIcon), (_sortDesc, sortDescIcon) })
            {
                toggle.Styled(ControlThemes.VersionSortToggle);
                // Значок красится подписью кнопки, как в разметке: у выбранной
                // она белая, у остальных обычный цвет текста.
                icon.Bind(Avalonia.Controls.Shapes.Path.FillProperty,
                    new Binding(nameof(ToggleButton.Foreground)) { Source = toggle });
            }
            // Щелчок по уже выбранной кнопке оставляет её выбранной: у автора
            // обработчик принудительно возвращает IsChecked = true
            // (PlatformVersionPickerWindow.xaml.cs:126), и состояния «сортировка
            // не выбрана ни одной кнопкой» в его версии не существует.
            _sortAsc.Click += (_, _) =>
            {
                _sortAsc.IsChecked = true;
                _sortDesc.IsChecked = false;
                if (_sortAscending) return;
                _sortAscending = true;
                RefreshTree();
            };
            _sortDesc.Click += (_, _) =>
            {
                _sortDesc.IsChecked = true;
                _sortAsc.IsChecked = false;
                if (!_sortAscending) return;
                _sortAscending = false;
                RefreshTree();
            };
            ToolTip.SetTip(_sortAsc, LocalizationManager.T("PlatformVersionPicker.SortAscTooltip"));
            ToolTip.SetTip(_sortDesc, LocalizationManager.T("PlatformVersionPicker.SortDescTooltip"));

            var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sortPanel.Children.Add(_sortAsc);
            sortPanel.Children.Add(_sortDesc);
            Grid.SetColumn(sortPanel, 1);

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.Children.Add(filterPanel);
            topGrid.Children.Add(sortPanel);

            var top = new Border
            {
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = topGrid
            };
            ThemeBrushes.Bind(top, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(top, Border.BorderBrushProperty, "BorderColorBrush");

            Grid.SetRow(top, 0);
            grid.Children.Add(top);

            // Дерево
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(object),
                (item, _) => BuildTreeRow(item),
                item => item is PlatformVersionGroup g && g.Children.Count > 0 ? g.Children : Array.Empty<PlatformVersionGroup>());
            // Разрешаем выбор и листьев (полная версия), и узлов линии/группы сборок
            // (частичная версия «8.3» / «8.3.27», issue #142). Разрядность подставляется
            // по активному фильтру или из однотипных листьев узла.
            _tree.SelectionChanged += (_, _) =>
            {
                if (_tree.SelectedItem is PlatformVersionGroup node)
                {
                    _selectedVersion = BuildResult(node);
                    // Запоминаем выбранную версию и как «текущую»: RefreshTree() (переключение
                    // фильтра разрядности, сортировка) повторно выделяет узел по _currentVersion,
                    // и без обновления выбор «перескакивал» на старую версию (issue #304).
                    _currentVersion = _selectedVersion;
                    _selectButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedVersion);
                }
                else if (!_isRestoringSelection)
                {
                    // Реальный сброс выбора пользователем — очищаем результат. Во время же
                    // перестроения дерева (RefreshTree) сюда приходит null из-за удаления
                    // старого узла из Items: выбор будет восстановлен отдельно (issue #304).
                    _selectedVersion = string.Empty;
                    _selectButton.IsEnabled = false;
                }
            };
            _tree.DoubleTapped += (_, _) =>
            {
                if (!string.IsNullOrEmpty(_selectedVersion))
                    OnSelect_Click();
            };
            // Раскрывает предков текущей версии и выбирает её лист по мере создания
            // контейнеров (см. SelectCurrent). Внутренние контейнеры готовит сам
            // TreeViewItem, поэтому событие дерева доходит и до них.
            _tree.ContainerPrepared += OnTreeContainerPrepared;

            // Дерево раскрыто целиком, как в версии для Windows
            // (PlatformVersionPickerWindow.xaml.cs:60, ExpandAll): линии, группы
            // сборок и сами сборки видны сразу. Значение стилем, а не локально,
            // чтобы свернуть узел вручную было можно.
            var expanded = new Style(x => x.OfType<TreeViewItem>());
            expanded.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, true));
            _tree.Styles.Add(expanded);

            // Элемент дерева по общему шаблону автора (ModernTreeViewItem):
            // раскрыватель «+»/«-», подсветка наведения и выбора, отступ вложенных 16.
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var treeItemTheme) == true
                && treeItemTheme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }

            var treeBorder = new Border
            {
                Child = new ScrollViewer
                {
                    Content = _tree,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(4)
                },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            // Фон и рамка карточки дерева из ресурсов темы
            // (PlatformVersionPickerWindow.xaml:183): без привязки рамка была
            // невидимой, а фон совпадал с фоном окна вместо карточного.
            ThemeBrushes.Bind(treeBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(treeBorder, Border.BorderBrushProperty, "BorderColorBrush");

            Grid.SetRow(treeBorder, 1);
            grid.Children.Add(treeBorder);

            // Кнопки
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            // Оформление и порядок по разметке (PlatformVersionPickerWindow.xaml:257):
            // зелёный «Выбрать» слева, красная «Отмена» справа, зазор 10.
            buttons.Children.Add(_selectButton);
            buttons.Children.Add(BuildCancelActionButton(140));
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        private Control BuildTreeRow(object? item)
        {
            if (item is PlatformVersionGroup node)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1) };
                // Значок и его цвет кодируют тип узла, как в разметке
                // (PlatformVersionPickerWindow.xaml:197): линия это жёлтая папка,
                // группа сборок открытая синяя папка, сборка x64 контурный
                // зелёный куб, x32 сплошной фиолетовый, без метки синее окно.
                var (iconKey, iconColor) = node.Kind switch
                {
                    PlatformNodeKind.Line => ("IconFolder", "@WarningBrush"),
                    PlatformNodeKind.BuildGroup => ("IconFolderOpen", "@InfoBrush"),
                    PlatformNodeKind.LeafX64 => ("IconCubeOutline", "@SuccessBrush"),
                    PlatformNodeKind.LeafX32 => ("IconCube", "@PurpleAccentBrush"),
                    _ => ("IconApplication", "@InfoBrush")
                };
                var icon = IconHelper.MakeIcon(iconKey, 16, Themes.ThemeBrushes.FromSpec(iconColor));
                icon.Margin = new Thickness(0, 0, 6, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(icon);
                // DisplayName — единый форматтер (issue #33): «8.3.27.2214 (x64)» +
                // локализованный суффикс «(тонкий клиент)» для тонкого клиента.
                var thin = node.IsThinClient;
                var text = new TextBlock
                {
                    Text = node.DisplayName,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = node.IsCurrent
                        ? FontWeight.Bold
                        : (node.IsLeaf ? FontWeight.Normal : FontWeight.SemiBold)
                };
                // Тонкий клиент (issue #33): оранжевая подсветка —
                // видно, что полные возможности (конфигуратор, сервер) недоступны.
                if (thin)
                    text.Foreground = Themes.ThemeBrushes.Get("WarningBrush", "#D97706");
                panel.Children.Add(text);
                return panel;
            }
            return new TextBlock { Text = item?.ToString() ?? string.Empty };
        }

        private void RefreshTree()
        {
            // Ключ восстановления — идентификатор выбранного узла (версия+разрядность):
            // сохраняем ДО перестроения дерева, восстановление идёт по нему, а не по
            // «текущей версии базы» (окончательный фикс issue #304).
            var restoreKey = !string.IsNullOrWhiteSpace(_selectedVersion)
                ? _selectedVersion
                : _currentVersion;

            // Дерево строится из полного списка версий, а фильтр разрядности скрывает
            // только листья: папки (линии/группы сборок) сохраняются, пока в линии есть
            // хоть один видимый вариант, — выбор «8.5.1» не перескакивает на линию 8.5
            // при переключении разрядности (issue #304).
            var tree = PlatformVersionService.BuildGroupedTree(_allInfos, _archFilter);
            if (_sortAscending)
                tree = ReverseTreeOrder(tree);

            // При замене ItemsSource SelectionChanged приходит с null (старый узел удалён):
            // гасим событие, чтобы не сбрасывать _selectedVersion до восстановления выбора (#304).
            _isRestoringSelection = true;
            try
            {
                // Сброс целей прошлого восстановления: узел уже мог быть выбран или фильтр
                // изменился, и «зависшие» предки не должны мешать новому выбору (issue #304).
                _initialLeaf = null;
                _initialAncestors = null;

                _tree.ItemsSource = tree;

                if (!string.IsNullOrWhiteSpace(restoreKey))
                    SelectCurrent(tree, restoreKey);
            }
            finally
            {
                _isRestoringSelection = false;
            }
        }

        private static List<PlatformVersionGroup> ReverseTreeOrder(List<PlatformVersionGroup> roots)
        {
            var list = roots.AsEnumerable().Reverse().ToList();
            foreach (var node in list)
                ReverseChildren(node);
            return list;
        }

        private static void ReverseChildren(PlatformVersionGroup node)
        {
            if (node.Children.Count == 0) return;
            node.Children = node.Children.AsEnumerable().Reverse().ToList();
            foreach (var c in node.Children)
                ReverseChildren(c);
        }

        private void SelectCurrent(IEnumerable<PlatformVersionGroup> roots, string restoreKey)
        {
            // Восстановление по ключу выбранного узла (версия+разрядность): FindBestNode
            // учитывает активный фильтр и находит либо тот же лист, либо осмысленный узел
            // того же семейства (папку/линию), — выбор не «уходит на самый верх» (issue #304).
            var node = PlatformVersionService.FindBestNode(roots, restoreKey);
            if (node is null)
            {
                // Узел не найден (например, вся линия отфильтрована) — сбрасываем «зависшую»
                // цель прошлого RefreshTree, иначе она помешает восстановлению выбора (issue #304).
                _initialLeaf = null;
                _initialAncestors = null;
                return;
            }
            node.IsCurrent = true;

            // Avalonia выделяет только через TreeView.SelectedItem, а контейнеры вложенных
            // узлов создаются лениво — пока не раскрыты предки. Поэтому запоминаем узел и
            // путь к нему, а раскрываем и выбираем по мере подготовки контейнеров; после
            // выделения узел прокручивается в видимую область (OnTreeContainerPrepared).
            _initialLeaf = node;
            _initialAncestors = new HashSet<PlatformVersionGroup>();
            CollectAncestors(roots, node, _initialAncestors);
        }

        private static bool CollectAncestors(
            IEnumerable<PlatformVersionGroup> nodes,
            PlatformVersionGroup target,
            HashSet<PlatformVersionGroup> result)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, target))
                    return true;

                var found = CollectAncestors(node.Children, target, result);
                if (found)
                {
                    result.Add(node);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Формирует строку выбора для узла дерева. Лист отдаёт полный вариант
        /// («8.3.27.1688 (64)»), узел линии/группы сборок — частичную версию
        /// с суффиксом разрядности, если он однозначен (issue #142). Суффикс
        /// подставляется только при реально видимых сборках нужной разрядности,
        /// иначе результат — чистая частичная версия («8.5.1», issue #304).
        /// </summary>
        private string BuildResult(PlatformVersionGroup node)
            => PlatformVersionService.BuildResultVariant(node, _archFilter);

        /// <summary>
        /// Раскрывает предков текущей версии и выбирает её лист по мере создания
        /// контейнеров. Начинается с корневых групп: раскрытие каждого предка готовит
        /// контейнеры его детей, пока не дойдём до листа, который и выделяем.
        /// </summary>
        private void OnTreeContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (e.Container?.DataContext is not PlatformVersionGroup node)
                return;

            if (_initialLeaf is null)
                return;

            if (_initialAncestors is not null && _initialAncestors.Contains(node))
                return;

            // Сам лист — выделяем его как единственный источник выбора.
            if (ReferenceEquals(node, _initialLeaf))
            {
                _initialLeaf = null;
                _initialAncestors = null;
                if (!ReferenceEquals(_tree.SelectedItem, node))
                    _tree.SelectedItem = node;
                // Скролл к восстановленному узлу (issue #304): после смены разрядности
                // выделение остаётся на той же версии и не уходит за видимую область.
                if (e.Container is Control control)
                    control.BringIntoView();
            }
        }

        private void OnSelect_Click()
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;
            DialogResult = true;
            Close();
        }
    }
}
#endif