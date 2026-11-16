#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (как в стартере):
    /// фильтр Все / x32 / x64, сортировка A→Z / Z→A,
    /// дерево 8.3 → 8.3.27 → 8.3.27.2214 (x64).
    /// </summary>
    public partial class PlatformVersionPickerWindow : Window
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionInfo> _allInfos = new();
        private string _currentVersion = string.Empty;
        private bool _sortAscending = false; // по умолчанию — свежие версии сверху
        private string _archFilter = "all"; // all | x32 | x64
        private bool _isRestoringSelection; // перестроение дерева: гасим SelectionChanged(null) (#304)
        // Статус по каждой версии (issue #135): «оснастка найдена/не найдена».
        private readonly IReadOnlyDictionary<string, string>? _statusByVariant;

        /// <param name="installedPlatformVersions">Варианты версий для выбора.</param>
        /// <param name="currentVersion">Текущая (сохранённая) версия — подсвечивается в дереве.</param>
        /// <param name="useProvidedVersionsOnly">
        /// true — показывать только переданный список (issue #135: внешняя консоль предлагает
        /// лишь переданные версии), не подмешивая все установленные.
        /// </param>
        /// <param name="statusByVariant">
        /// Статус по каждой версии (issue #135): «оснастка найдена: 1CV8Servers64.msc» или
        /// «оснастка не найдена» — показывается справа от названия сборки. null — без статусов.
        /// </param>
        public PlatformVersionPickerWindow(
            IEnumerable<string> installedPlatformVersions,
            string currentVersion,
            bool useProvidedVersionsOnly = false,
            IReadOnlyDictionary<string, string>? statusByVariant = null)
        {
            InitializeComponent();
            _currentVersion = currentVersion ?? "";
            _statusByVariant = statusByVariant;

            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            if (!useProvidedVersionsOnly)
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

            RefreshTree();
        }

        public string Result => _selectedVersion;

        private void RefreshTree()
        {
            // Ключ восстановления — идентификатор выбранного узла (версия+разрядность):
            // сохраняем ДО перестроения дерева, восстановление идёт по нему, а не по
            // «текущей версии базы», которая могла не совпадать с выбором пользователя
            // (окончательный фикс issue #304). После сброса выбора пользователем ключ пуст.
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

            // Статус оснастки по каждой версии (issue #135): видно, где консоль доступна.
            ApplyStatusHints(tree);

            // Во время перестроения SelectedItemChanged приходит с null (старый узел удалён
            // из Items): гасим его, чтобы не сбрасывать _selectedVersion до восстановления
            // выбора (issue #304). Команды не триггерятся и в отложенной части — до конца
            // восстановления.
            _isRestoringSelection = true;
            try
            {
                PlatformsTree.ItemsSource = tree;
            }
            finally
            {
                _isRestoringSelection = false;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _isRestoringSelection = true;
                    // Полностью разворачиваем дерево (линии → группы сборок → сборки), как в стартере
                    ExpandAll(PlatformsTree);
                    if (!string.IsNullOrWhiteSpace(restoreKey))
                        SelectCurrent(restoreKey);
                }
                finally
                {
                    _isRestoringSelection = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Проставляет статус оснастки листьям дерева (issue #135): пользователь видит,
        /// в каких версиях платформы внешняя консоль администрирования серверов доступна.
        /// </summary>
        private void ApplyStatusHints(IEnumerable<PlatformVersionGroup> nodes)
        {
            if (_statusByVariant is null)
                return;

            foreach (var node in nodes)
            {
                if (node.IsLeaf && !string.IsNullOrWhiteSpace(node.Variant)
                    && _statusByVariant.TryGetValue(node.Variant!, out var status))
                {
                    node.StatusHint = status;
                }

                if (node.Children.Count > 0)
                    ApplyStatusHints(node.Children);
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

        private static void ExpandAll(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                {
                    tvi.IsExpanded = true;
                    tvi.UpdateLayout();
                    ExpandAll(tvi);
                }
            }
        }

        private void OnArchFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (FilterX32.IsChecked == true) _archFilter = "x32";
            else if (FilterX64.IsChecked == true) _archFilter = "x64";
            else _archFilter = "all";
            RefreshTree();
        }

        private void OnSortAsc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = true;
            SortAsc.IsChecked = true;
            SortDesc.IsChecked = false;
            RefreshTree();
        }

        private void OnSortDesc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = false;
            SortDesc.IsChecked = true;
            SortAsc.IsChecked = false;
            RefreshTree();
        }

        private void OnPlatformsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Разрешаем выбор и листьев (полная версия), и узлов линии/группы сборок
            // (частичная версия «8.3» / «8.3.27», issue #142). Разрядность подставляется
            // по активному фильтру или из однотипных листьев узла.
            if (e.NewValue is PlatformVersionGroup node)
            {
                _selectedVersion = BuildResult(node);
                // Запоминаем выбранную версию и как «текущую»: RefreshTree() (переключение
                // фильтра разрядности, сортировка) повторно выделяет узел по _currentVersion,
                // и без обновления выбор «перескакивал» на старую версию (issue #304).
                _currentVersion = _selectedVersion;
                SelectButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedVersion);
            }
            else if (!_isRestoringSelection)
            {
                // Реальный сброс выбора пользователем — очищаем результат. Во время же
                // перестроения дерева (RefreshTree) сюда приходит null из-за удаления
                // старого узла из Items: выбор уже будет восстановлен отдельно, поэтому
                // _selectedVersion не трогаем (issue #304).
                _selectedVersion = string.Empty;
                SelectButton.IsEnabled = false;
            }
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

        private void OnPlatformsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedVersion))
                OnSelect_Click(sender, e);
        }

        private void OnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;
            DialogResult = true;
        }

        private void SelectCurrent(string restoreKey)
        {
            if (PlatformsTree.ItemsSource is not IEnumerable<PlatformVersionGroup> roots)
                return;
            // Восстановление по ключу выбранного узла (версия+разрядность): FindBestNode
            // учитывает активный фильтр и находит либо тот же лист, либо осмысленный узел
            // того же семейства (папку/линию), — выбор не «уходит на самый верх» (issue #304).
            var node = PlatformVersionService.FindBestNode(roots, restoreKey);
            if (node is null) return;
            node.IsCurrent = true; // подсветка жирным
            SelectNodeInTree(PlatformsTree, node);
        }

        private static bool SelectNodeInTree(ItemsControl parent, PlatformVersionGroup target)
        {
            foreach (var item in parent.Items)
            {
                if (item is not PlatformVersionGroup node) continue;
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container is null)
                {
                    // Контейнер ещё не создан (вложенный узел): раскрываем предка и
                    // принудительно обновляем компоновку, чтобы контейнер появился, —
                    // иначе восстановление выделения молча теряется (issue #304).
                    parent.UpdateLayout();
                    container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                }
                if (ReferenceEquals(node, target))
                {
                    if (container is not null)
                    {
                        node.IsSelected = true;
                        container.IsSelected = true;
                        // Скролл к восстановленному узлу (issue #304).
                        container.BringIntoView();
                    }
                    return true;
                }
                if (container is not null)
                {
                    container.IsExpanded = true;
                    container.UpdateLayout();
                    if (SelectNodeInTree(container, target))
                        return true;
                }
            }
            return false;
        }
    }
}
#endif
