#if LINUX
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Avalonia-версия TreeView для дерева баз. Контейнеры строк — <see cref="LeveledTreeViewItem"/>,
    /// а вложенность и сдвиг уровней обеспечивает штатный механизм TreeView. Ручное вычисление
    /// уровня (присоединённое свойство Level), унаследованное от WPF, здесь не требуется и удалено.
    /// </summary>
    public class LeveledTreeView : TreeView
    {
        /// <summary>
        /// Тема оформления ищется по типу контрола, а для наследника её в Fluent нет:
        /// без этого шаблон не находится и контрол не отрисовывается вовсе.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TreeView);

        public LeveledTreeView()
        {
            AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel);
        }

        /// <summary>
        /// Строка, которой выделение поставлено напрямую через <see cref="SelectRow"/>
        /// (локальное значение IsSelected). Штатная разметка выделения Avalonia
        /// (SetCurrentValue) может перезаписывать локальные значения, поэтому при
        /// пересборке/переработке контейнеров прежняя ссылка гасится явно
        /// (см. ClearContainerForItemOverride), а дубль данных в дереве исключён
        /// обёрткой <see cref="PinnedInfobaseItem"/> (issue #301) — чужой строке
        /// подсветку поставить больше некому.
        /// </summary>
        private TreeViewItem? _selectedRow;

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();

        // Контейнеры переиспользуются, поэтому прежняя привязка освобождается:
        // иначе на одном контейнере копились бы выражения привязки.
        private readonly ConditionalWeakTable<Control, IDisposable> _expandedBindings = new();

        /// <summary>
        /// Связывает раскрытие контейнера с моделью узла. Делается здесь, а не по
        /// событию ContainerPrepared у дерева: вложенные контейнеры готовит
        /// родительский TreeViewItem, и его событие до дерева не доходит, поэтому
        /// подгруппы оставались свёрнутыми независимо от модели. Подготовку своих
        /// детей TreeViewItem перенаправляет сюда же.
        /// </summary>
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            base.PrepareContainerForItemOverride(container, item, index);
            ReleaseExpandedBinding(container);

            if (container is not TreeViewItem treeItem || item is not GroupNodeViewModel)
                return;

            // Источником указан сам узел, а не DataContext контейнера: привязка
            // тогда не зависит от того, когда и чем контекст будет установлен.
            _expandedBindings.Add(treeItem, treeItem.Bind(TreeViewItem.IsExpandedProperty,
                new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = item }));
        }

        /// <summary>
        /// Клик по строке выделяет именно её, а не другую копию тех же данных.
        /// Подсветка ставится на конкретный контейнер под курсором, а событие
        /// помечается обработанным: штатный пузырьковый обработчик TreeView
        /// (UpdateSelectionFromEventSource) красит ПЕРВЫЙ контейнер с данными базы
        /// по всему дереву — раньше он перебивал нашу подсветку копией в узле
        /// «Закреплённые» (issue #301). Дубль данных в дереве устранён обёрткой
        /// <see cref="PinnedInfobaseItem"/> (узел «Закреплённые»), поэтому и штатная
        /// разметка, и <see cref="SelectRow"/> теперь всегда указывают на одну строку.
        /// </summary>
        private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // issue #96: защитная обёртка клик-конвейера (Linux). При исключении — полный
            // стек, версия, ОС, имя обработчика и контекст в журнал + содержательный диалог;
            // приложение продолжает работу. Поведение без исключения не меняется
            // (регресс #340/#97 обязателен).
            UiExceptionGuard.Guard(
                nameof(OnRowPointerPressed),
                () => OnRowPointerPressedCore(sender, e),
                () => $"keyModifiers={e.KeyModifiers}, " +
                      $"selectedBase={(DataContext is MainViewModel vm ? vm.SelectedInfobase?.Id ?? "null" : "no-vm")}");
        }

        private void OnRowPointerPressedCore(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed)
                return;
            if (e.Source is not Visual source)
                return;

            var row = source.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
            if (row is null)
                return;

            // ПРАВАЯ кнопка — только основное выделение под курсором (SelectRow).
            // Набор «для выделенных» при этом НЕ меняется: ни ToggleBatchSelection,
            // ни SelectRange, ни ClearBatchSelection. Иначе при правом клике с
            // зажатым Ctrl строка набора снималась бы повторным toggle, а «бывшая
            // текущая» (выбранная без Ctrl) терялась из набора (issue #313).
            // Отдельная ветка с ранним выходом страхует от будущих правок веток
            // модификаторов: правый клик никогда не попадает в них.
            if (point.Properties.IsRightButtonPressed)
            {
                SelectRow(row);
                e.Handled = true;
                return;
            }

            // Ctrl+ЛЕВЫЙ щелчок по шеврону («плюсику») строки ГРУППЫ — то же, что и
            // по названию: переключается ВСЯ ветка, текущая строка и выделение не
            // меняются (issue #341). Шеврон — ToggleButton PART_ExpandCollapseChevron
            // из шаблона строки (LeveledTreeViewItem.RowTheme): его штатное
            // переключение через TwoWay-привязку IsChecked↔IsExpanded свернуло бы
            // только сам узел. Перехватываем клик в tunnel-фазе и гасим событие,
            // чтобы ToggleButton не переключился дополнительно, — команда ветки
            // применяется ровно один раз. Обычный (без Ctrl) клик по плюсику
            // работает как раньше.
            if (row.DataContext is GroupNodeViewModel chevronGroup && chevronGroup.Group is not null &&
                point.Properties.IsLeftButtonPressed &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Shift) != KeyModifiers.Shift &&
                source.FindAncestorOfType<ToggleButton>(includeSelf: true) is { Name: "PART_ExpandCollapseChevron" })
            {
                if (DataContext is MainViewModel chevronVm)
                    chevronVm.ToggleGroupBranch(chevronGroup);
                e.Handled = true;
                return;
            }

            // Ctrl+ЛЕВЫЙ щелчок по группе — развернуть/свернуть ветку (сама группа + все
            // подгруппы рекурсивно), НЕ меняя текущую строку и выделение (issue #341).
            // Зеркало WPF-ветки (MainWindow.Events.cs, case GroupNodeViewModel when
            // group.Group is not null): служебные узлы («Закреплённые», «Без группы»)
            // ветку не сворачивают.
            if (row.DataContext is GroupNodeViewModel groupNode && groupNode.Group is not null &&
                point.Properties.IsLeftButtonPressed &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Shift) != KeyModifiers.Shift)
            {
                if (DataContext is MainViewModel vm)
                    vm.ToggleGroupBranch(groupNode);
                e.Handled = true;
                return;
            }

            // Мультивыделение (0.3.9.90): Ctrl+ЛЕВЫЙ щелчок — точечное переключение,
            // Shift+левый — диапазон от «якоря» до цели по видимому порядку.
            // Обычный ЛЕВЫЙ клик снимает мультивыделение (как в WPF-версии).
            //
            // Секция строки определяется по данным контейнера (обёртка
            // PinnedInfobaseItem в узле «Закреплённые»): клик в другой секции
            // не смешивает наборы, а Shift-диапазон строится только по строкам
            // той же секции (issue #326).
            var rowBase = row.DataContext switch
            {
                Infobase ib => ib,
                PinnedInfobaseItem pinned => pinned.Base,
                _ => null
            };
            var isPinnedSection = BatchSelectionHelper.IsPinnedSection(row.DataContext);
            if (rowBase is not null &&
                point.Properties.IsLeftButtonPressed &&
                (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control &&
                (e.KeyModifiers & KeyModifiers.Shift) != KeyModifiers.Shift)
            {
                if (DataContext is MainViewModel vm)
                {
                    // Правило issue #313: первый Ctrl-клик по строке, отличной от
                    // «текущей», добавляет в набор и «текущую» — но только если она
                    // лежит в той же секции (#326). Секция текущей строки берётся из
                    // контейнера, выделенного через SelectRow (_selectedRow): текущая
                    // строка может быть закреплённой копией (обёртка PinnedInfobaseItem).
                    vm.ToggleBatchSelection(rowBase, "Ctrl", isPinnedSection: isPinnedSection,
                        currentRowSectionIsPinned: BatchSelectionHelper.IsPinnedSection(_selectedRow?.DataContext));
                }
                e.Handled = true;
                return;
            }

            if (rowBase is not null &&
                (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift &&
                point.Properties.IsLeftButtonPressed)
            {
                if (DataContext is MainViewModel vm)
                    vm.SelectRange(
                        vm.SelectedInfobase,
                        rowBase,
                        VisibleInfobasesInOrder(isPinnedSection),
                        isPinnedSection);
                e.Handled = true;
                return;
            }

            SelectRow(row);
            e.Handled = true;

            // Левый клик без модификаторов — единственный выбор: снимаем
            // мультивыделение, чтобы пакетный блок меню не «висел».
            if (point.Properties.IsLeftButtonPressed &&
                rowBase is not null &&
                DataContext is MainViewModel plainVm)
            {
                plainVm.ClearBatchSelection();
            }
        }

        /// <summary>
        /// Базы в видимом порядке строк дерева (сверху вниз, включая строки
        /// развёрнутых подгрупп) ТОЛЬКО в пределах одной секции. Используется
        /// Shift-диапазоном мультивыделения (0.3.9.90). Порядок секции
        /// «Закреплённые» строится по ДАННЫМ узла (BuildPinnedSectionVisibleOrder),
        /// а не по контейнерам: при виртуализации контейнеры вне видимой области
        /// могут отсутствовать, а строки узла несут обёртку PinnedInfobaseItem —
        /// прежняя проверка «row.DataContext is Infobase» пропускала их, порядок
        /// оставался пустым, и Shift-диапазон уходил в общий список (issue #326).
        /// Порядок обычной секции строится по контейнерам (VisibleRows).
        /// </summary>
        /// <param name="pinnedSection">
        /// true — только строки узла «Закреплённые» (обёртки PinnedInfobaseItem,
        /// разворачиваются до реальной базы); false — только обычные строки
        /// (Infobase), закреплённые копии исключаются (issue #314). Диапазон
        /// одной секции никогда не захватывает строки другой (issue #326).
        /// </param>
        private List<Infobase> VisibleInfobasesInOrder(bool pinnedSection)
        {
            if (pinnedSection && DataContext is MainViewModel pinnedVm)
                return pinnedVm.BuildPinnedSectionVisibleOrder().ToList();

            var result = new List<Infobase>();
            foreach (var row in VisibleRows())
            {
                // Строки узла «Закреплённые» несут обёртку PinnedInfobaseItem
                // (issue #301), обычные — саму модель. Берём только запрошенную
                // секцию: иначе любой диапазон ниже захватывал бы закреплённые
                // копии («Лишнее выделение», issue #314) или смешивал секции
                // (issue #326). Клик по закреплённой строке как якорю работает —
                // обработчик разворачивает обёртку до реальной базы.
                var isPinnedRow = BatchSelectionHelper.IsPinnedSection(row.DataContext);
                if (isPinnedRow != pinnedSection)
                    continue;
                if (BatchSelectionHelper.Unwrap(row.DataContext) is { } ib && !result.Contains(ib))
                    result.Add(ib);
            }
            return result;
        }

        /// <summary>
        /// Ставит подсветку ровно на указанную строку: гасит локальную подсветку
        /// предыдущей и включает её на переданной. Установка IsSelected поднимает
        /// IsSelectedChanged, синхронизирующий SelectedItems дерева; благодаря
        /// уникальным данным в дереве (обёртка <see cref="PinnedInfobaseItem"/> в узле
        /// «Закреплённые», issue #301) поиск контейнера по данным находит ровно
        /// эту строку — чужие копии не подсвечиваются.
        /// </summary>
        public void SelectRow(TreeViewItem row)
        {
            if (row is null)
                return;

            // issue #96: прежняя строка могла быть утилизирована/переподготовлена
            // (комментарий у ClearContainerForItemOverride) — обращение к IsSelected
            // по осиротевшему контейнеру защищаем: ссылка на DETACHED-контейнер
            // просто сбрасывается, чужая подсветка не гасится. Плюс общий guard:
            // сбой здесь не должен уходить в Dispatcher как «Ошибка интерфейса».
            UiExceptionGuard.Guard(
                nameof(SelectRow),
                () =>
                {
                    if (_selectedRow is { } previous && !ReferenceEquals(previous, row) &&
                        previous.IsAttachedToVisualTree() && previous.IsSelected)
                    {
                        previous.IsSelected = false;
                    }
                    else if (_selectedRow is { } stale && !stale.IsAttachedToVisualTree())
                    {
                        // Контейнер уже вне дерева — ссылка недействительна.
                        _selectedRow = null;
                    }
                    row.IsSelected = true;
                    _selectedRow = row;
                });
        }

        /// <summary>
        /// Навигация по видимым строкам: ↑/↓ — соседняя строка, Home/End —
        /// начало/конец, PageUp/PageDown — на страницу. Обработчик стоит на
        /// туннелировании: внутренняя прокрутка дерева лежит в маршруте ближе
        /// к строке и разбирает PageUp с PageDown сама, помечая их
        /// обработанными, так что до самого дерева они не доходят.
        /// Распределение взято у версии для Windows: голые клавиши переносят
        /// выделение, а с Ctrl прокручивают, не трогая его.
        ///
        /// ↑/↓ обрабатываются явно, а не отдаются штатной логике TreeView:
        /// стандартная навигация держится на фокусе, а в строках базы сидят
        /// фокусируемые кнопки (избранное, закрепление, действия, теги).
        /// Из-за этого при движении вниз выделение «перепрыгивало» на избранное,
        /// а дальше листалось от него. Здесь шаг считается по порядку видимых
        /// строк, а фокус ставится на контейнер, как делает версия для Windows.
        /// </summary>
        private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key is not (Key.Home or Key.End or Key.PageUp or Key.PageDown
                or Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter))
                return;

            // Стрелки не перехватываем, если фокус в поле ввода
            // (поиск, инлайн-теги): там они двигают курсор, а не выделение.
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && FocusedElementIsTextBox)
                return;

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                // Прокрутка с Ctrl работает только для перечисленных клавиш;
                // Ctrl+стрелки/Ctrl+Enter не назначены, оставляем их остальному маршруту.
                if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter)
                    return;
                ScrollBy(e.Key);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers != KeyModifiers.None)
                return;

            // Пустой список: у TreeView в Avalonia эти клавиши считаются
            // направленными, и при пустом выборе он берёт первый элемент
            // представления, которого нет. Гасим событие до него.
            if (ItemsView.Count == 0)
            {
                e.Handled = true;
                return;
            }

            var rows = VisibleRows();
            if (rows.Count == 0)
                return;

            var current = CurrentRowIndex(rows);

            // Enter = двойной клик (issue #328): база → запуск «1С:Предприятие»/
            // «Конфигуратор» (ResolveDoubleClickAction), группа → свернуть/развернуть.
            // Туннельный обработчик дерева срабатывает только когда фокус внутри
            // дерева; дополнительно Enter не перехватывается в текстовом вводе
            // (поле поиска, инлайн-редактор тега, палитра команд), на интерактивных
            // кнопках строки и при открытых модальных окнах — там он работает
            // как обычно. В HotkeyBox Enter намеренно не назначается (клавиша
            // ввода/навигации), поэтому конфликта с пользовательскими горячими нет.
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
                && !FocusedElementIsInteractiveControl
                && EnterActivationHelper.CanHandleEnter(FocusedElementIsTextInput, AnyOpenModalWindow())
                && HandleEnterActivation(rows, current))
            {
                e.Handled = true;
                return;
            }

            // ←/→ — по фактической вложенности (произвольная глубина, issue #331):
            // вправо раскрывает свёрнутую папку или переходит к первому потомку;
            // влево сворачивает ТОЛЬКО текущую развёрнутую папку (корневые секции
            // «Закреплённые» не затрагиваются — кейс 2) или переходит к родителю.
            // Раньше эти клавиши уходили штатной логике TreeView, которая считала
            // вложенность «двухуровневой» и сворачивала ближайшего развёрнутого
            // предка вплоть до «Закреплённых».
            if (e.Key is Key.Left or Key.Right)
            {
                if (current < 0)
                    return;
                var info = BuildRowInfoForNavigation(rows, current);
                var action = e.Key == Key.Right
                    ? TreeNavigationHelper.DecideRight(info)
                    : TreeNavigationHelper.DecideLeft(info);

                switch (action)
                {
                    case TreeNavigationHelper.LateralAction.Expand:
                    case TreeNavigationHelper.LateralAction.Collapse:
                        // Сворачиваем/раскрываем ТОЛЬКО эту группу; выделение и
                        // фокус остаются на ней (кейс 2 issue #331).
                        ToggleGroupExpanded(rows[current]);
                        SelectRow(rows[current]);
                        rows[current].Focus();
                        break;

                    case TreeNavigationHelper.LateralAction.GoToFirstChild:
                    case TreeNavigationHelper.LateralAction.GoToParent:
                    {
                        var lateralTarget = TreeNavigationHelper.TargetIndex(action, info, current);
                        if (lateralTarget >= 0 && lateralTarget < rows.Count && lateralTarget != current)
                            SelectRow(rows[lateralTarget]);
                        break;
                    }
                }

                e.Handled = true;
                return;
            }

            int target;
            if (e.Key is Key.Up or Key.Down)
            {
                target = e.Key == Key.Down
                    ? TreeNavigationHelper.NextVisible(rows.Count, current)
                    : TreeNavigationHelper.PreviousVisible(current);
                if (target < 0 || target == current)
                    return;
            }
            else
            {
                target = e.Key switch
                {
                    Key.Home => 0,
                    Key.End => rows.Count - 1,
                    Key.PageUp => PageStep(rows, current, back: true),
                    _ => PageStep(rows, current, back: false)
                };
                if (target < 0)
                    return;
            }

            e.Handled = true;

            // Выделение ставится напрямую на конкретный контейнер строки, а не
            // через SelectedItem: закреплённая база присутствует в дереве дважды
            // (узел «Закреплённые» и собственная группа), и SelectedItem всегда
            // резолвился бы в первое вхождение вверху, «перепрыгивая» подсветку
            // на закреплённую базу в начало списка. Карточка строки следит за
            // IsSelected собственного контейнера, поэтому фокус остаётся на нужной
            // копии; SelectedItem дерева поднимается из контейнера сам (штатный
            // путь выбора, как по клику), а с ним — правая панель и модель.
            // SelectRow дополнительно гасит локальную подсветку прежней строки:
            // без этого при движении по списку подсвеченными оставались бы обе.
            SelectRow(rows[target]);
            BringRowIntoView(rows[target]);
            rows[target].Focus();
        }

        /// <summary>
        /// Описание строки для навигации влево/вправо: принадлежность группе,
        /// развёрнутость, наличие потомков и индексы родителя/первого потомка в
        /// видимом порядке. Индексы считаются по контейнерам, чтобы закреплённая
        /// копия базы не «перепрыгивала» выделение в начало списка.
        /// </summary>
        private TreeNavigationHelper.RowInfo BuildRowInfoForNavigation(List<TreeViewItem> rows, int index)
        {
            var row = rows[index];
            var isGroup = row.DataContext is GroupNodeViewModel;
            var hasChildren = row.DataContext is GroupNodeViewModel groupNode && groupNode.Items.Count > 0;
            var isExpanded = row.IsExpanded;

            // Родительская строка — ItemsControl, которому принадлежит контейнер;
            // для вложенной строки это TreeViewItem-предок из того же обхода.
            int? parentIndex = null;
            if (ItemsControl.ItemsControlFromItemContainer(row) is TreeViewItem parentTvi)
            {
                var pi = rows.IndexOf(parentTvi);
                if (pi >= 0)
                    parentIndex = pi;
            }

            int? firstChildIndex = null;
            if (isGroup && isExpanded && hasChildren)
                firstChildIndex = index + 1; // первый потомок идёт сразу после группы

            return new TreeNavigationHelper.RowInfo(
                isGroup, isExpanded, hasChildren, parentIndex, firstChildIndex);
        }

        /// <summary>Раскрывает/сворачивает группу через модель (TwoWay-привязка контейнера).</summary>
        private void ToggleGroupExpanded(TreeViewItem row)
        {
            if (row.DataContext is GroupNodeViewModel groupNode)
                groupNode.IsExpanded = !groupNode.IsExpanded;
        }

        /// <summary>
        /// Выполняет «Enter = двойной клик» по текущей строке (issue #328):
        /// группа — свернуть/развернуть (через модель), база — запуск действием
        /// по настройке (та же логика, что у двойного клика карточки).
        /// Возвращает true, если строка есть и действие выполнено.
        /// </summary>
        private bool HandleEnterActivation(List<TreeViewItem> rows, int current)
        {
            if (DataContext is not MainViewModel vm)
                return false;
            if (current < 0 || current >= rows.Count)
                return false;

            switch (rows[current].DataContext)
            {
                case GroupNodeViewModel groupNode:
                    groupNode.IsExpanded = !groupNode.IsExpanded;
                    return true;
                case Infobase ib:
                    ActivateInfobaseLikeDoubleClick(vm, ib);
                    return true;
                case PinnedInfobaseItem pinned:
                    ActivateInfobaseLikeDoubleClick(vm, pinned.Base);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Запуск базы тем же действием, что и двойной клик (issue #328).</summary>
        private void ActivateInfobaseLikeDoubleClick(MainViewModel vm, Infobase ib)
        {
            vm.SelectedInfobase = ib;

            // Отбор «Только запущенные» / вкладка «Запущенные» (issue #339): двойной
            // клик/Enter активируют окно уже запущенной базы. Issue #120 (v1.0.1.55,
            // 7OH): при нескольких запущенных приложениях базы — выбор окна.
            if (vm.IsListModeRunning && ib.IsRunning)
            {
                Workspace1C.MainWindow.ActivateAllWindowsOrNothing(vm, ib, this, nothingAction: false);
                return;
            }

            var dblAction = vm.ResolveDoubleClickAction(ib);
            // Issue #120 (v1.0.1.55, 7OH): при действии «Ничего» у запущенной базы —
            // выбор окна (в списке и Предприятие, и Конфигуратор). Issue #120
            // (v1.0.1.58, RafaIS): меню выбора — только при отключённой колонке
            // «Действия»; при включённой двойной клик с «Ничего» ничего не делает.
            if (dblAction == Workspace1C.Models.DoubleClickAction.None)
            {
                if (ib.IsRunning)
                    Workspace1C.MainWindow.ActivateAllWindowsOrNothing(vm, ib, this, nothingAction: true);
                return;
            }
            // Issue #120 (v1.0.1.55): действие «Активировать окно» — переход к
            // открытому окну Предприятия/Конфигуратора. Меню выбора — только при
            // отключённой колонке «Действия» (issue #120 v1.0.1.58, RafaIS).
            if (dblAction == Workspace1C.Models.DoubleClickAction.ActivateWindow)
            {
                Workspace1C.MainWindow.ActivateAllWindowsOrNothing(vm, ib, this, nothingAction: false);
                return;
            }
            if (dblAction == Workspace1C.Models.DoubleClickAction.Configurator)
            {
                // Issue #120: запуск Конфигуратора сам активирует открытое окно
                // (MainViewModel.Avalonia.Launch → TryActivateConfiguratorWindow).
                vm.LaunchConfiguratorCommand.Execute(null);
            }
            else if (ib.IsEnterpriseRunning)
            {
                // Issue #120 (v1.0.1.54): при запущенном Предприятии двойной щелчок
                // сначала активирует уже открытое окно; новый экземпляр запускается
                // только если запущенного нет (окна не найдены).
                ActivateRunningEnterpriseOrLaunch(vm, ib);
            }
            else
            {
                vm.LaunchEnterpriseCommand.Execute(null);
            }
        }

        /// <summary>
        /// Issue #120 (v1.0.1.54): двойной щелчок при запущенном Предприятии —
        /// активация открытого окна вместо запуска нового экземпляра. Одно окно —
        /// активируется напрямую; несколько — меню выбора; окон нет (флаг монитора
        /// устарел, matcher процессов не распознал клиент, на Linux перечисление
        /// окон не поддержано и список всегда пуст) — fallback на обычный запуск.
        /// Перечисление окон идёт через обход процессов — вне UI-потока; применение
        /// решения (активация/меню/запуск) — через Dispatcher в UI-потоке.
        /// </summary>
        private void ActivateRunningEnterpriseOrLaunch(MainViewModel vm, Infobase ib)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var windows = vm.GetRunningWindowsSafe(ib, Workspace1C.Models.RunningKind.Enterprise);
                if (windows.Count == 0)
                    vm.LogRunningWindowMiss(ib, Workspace1C.Models.RunningKind.Enterprise);
                return windows;
            })
            .ContinueWith(t =>
            {
                var windows = t.Result;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Issue #120 (v1.0.1.58, RafaIS): меню выбора окна при двойном
                    // щелчке — только при отключённой колонке «Действия»;
                    // при включённой — первое окно.
                    switch (Services.RunningWindowPlanner.PlanEnterpriseDoubleClick(
                                windows.Count, vm.ShowActionsColumn))
                    {
                        case Services.RunningWindowAction.ActivateSingle:
                            vm.ActivateWindowByHandle(windows[0].Handle);
                            break;
                        case Services.RunningWindowAction.ShowPicker:
                            Workspace1C.MainWindow.ShowRunningWindowPicker(vm, windows, this);
                            break;
                        default:
                            vm.LaunchEnterpriseCommand.Execute(null);
                            break;
                    }
                });
            });
        }

        /// <summary>Фокус клавиатуры сейчас в поле ввода/редакторе (TextBox, ComboBox).</summary>
        private bool FocusedElementIsTextInput =>
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or ComboBox;

        /// <summary>
        /// Фокус на интерактивном элементе строки (кнопка, флажок, шеврон раскрытия):
        /// там Enter активирует сам элемент, а не «двойной клик» по строке.
        /// </summary>
        private bool FocusedElementIsInteractiveControl =>
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()
                is Button or ToggleButton or CheckBox;

        /// <summary>
        /// Открыт ли другой видимый модальный диалог/окно (свойства базы, настройки,
        /// командная палитра и т.п.). При открытом окне Enter не перехватывается —
        /// он работает в нём как обычно (issue #328).
        /// </summary>
        private bool AnyOpenModalWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return false;
            var top = TopLevel.GetTopLevel(this);
            foreach (var window in desktop.Windows)
                if (!ReferenceEquals(window, top) && window.IsVisible)
                    return true;
            return false;
        }

        /// <summary>
        /// Высота типовой строки списка (заголовок группы или строка базы). Используется как
        /// «нижняя граница» цели прокрутки для узла-ГРУППЫ: у контейнера группы Bounds.Height
        /// включает высоту всех дочерних строк, и прокрутка по ней уводила список на высоту
        /// всего поддерева (issue #255). Для группы достаточно показать её заголовок — одну
        /// строку, поэтому берём высоту первой видимой строки базы как эталонную.
        /// </summary>
        private double ReferenceRowHeight()
        {
            foreach (var row in VisibleRows())
            {
                if (row.DataContext is not GroupNodeViewModel && row.Bounds.Height > 0)
                    return row.Bounds.Height;
            }
            // Резерв: высота первого реализованного контейнера или вьюпорт прокрутки.
            return TreeScroll?.Viewport.Height > 0 ? TreeScroll.Viewport.Height : 32;
        }

        /// <summary>
        /// Прокручивает список к строке, не «перепрыгивая» вниз на высоту всей группы
        /// (issue #255). У контейнера группы высота включает все дочерние строки, поэтому
        /// штатный BringIntoView уводил список на несколько экранов при листании по папке.
        /// Шаг ограничен размером вьюпорта; следующее нажатие доводит выделение обычным шагом.
        /// </summary>
        private void BringRowIntoView(TreeViewItem item)
        {
            if (TreeScroll is not { } scroll)
            {
                item.BringIntoView();
                return;
            }

            var point = item.TranslatePoint(default, scroll);
            if (point is null)
            {
                item.BringIntoView();
                return;
            }

            var top = point.Value.Y;
            // У контейнера ГРУППЫ Bounds.Height включает высоту всех дочерних строк, поэтому
            // «выступ» за нижний край огромен и при листании клавишами по папке список
            // «перепрыгивал» вниз на высоту всего поддерева (issue #255). Цель прокрутки для
            // группы — показать её ЗАГОЛОВОК (одну строку): нижняя граница считается по высоте
            // типовой строки, а не по Bounds.Height контейнера. Для строки базы — её высота.
            var bottom = item.DataContext is GroupNodeViewModel
                ? top + ReferenceRowHeight()
                : top + item.Bounds.Height;
            var viewport = Math.Max(1, scroll.Viewport.Height);
            var current = scroll.Offset.Y;

            if (top < 0)
            {
                var step = Math.Min(-top, viewport);
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, current - step));
            }
            else if (bottom > viewport)
            {
                var step = Math.Min(bottom - viewport, viewport);
                scroll.Offset = scroll.Offset.WithY(current + step);
            }
        }

        /// <summary>Фокус клавиатуры сейчас в текстовом поле (поиск, инлайн-теги).</summary>
        private bool FocusedElementIsTextBox =>
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

        /// <summary>
        /// Индекс текущей строки навигации. Определяется по контейнеру под
        /// фокусом либо под выделением, а не по SelectedItem: закреплённая база
        /// присутствует в дереве дважды (узел «Закреплённые» и собственная группа),
        /// и поиск по ссылке данных вернул бы первое вхождение, «перепрыгивая»
        /// выделение в начало списка.
        /// </summary>
        private int CurrentRowIndex(List<TreeViewItem> rows)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            for (var node = focused; node is not null; node = node.GetVisualParent())
            {
                if (node is not TreeViewItem tvi)
                    continue;
                var idx = rows.IndexOf(tvi);
                if (idx >= 0)
                    return idx;
                break;
            }

            for (var i = 0; i < rows.Count; i++)
                if (rows[i].IsSelected)
                    return i;

            return rows.FindIndex(row => ReferenceEquals(row.DataContext, SelectedItem));
        }

        /// <summary>Прокрутка без переноса выделения, вариант с Ctrl.</summary>
        private void ScrollBy(Key key)
        {
            if (TreeScroll is not { } scroll)
                return;

            var hidden = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            var target = key switch
            {
                Key.Home => 0,
                Key.End => hidden,
                Key.PageUp => scroll.Offset.Y - scroll.Viewport.Height,
                _ => scroll.Offset.Y + scroll.Viewport.Height
            };

            var next = scroll.Offset.WithY(Math.Clamp(target, 0, hidden));
            if (next != scroll.Offset)
                scroll.Offset = next;
        }

        private ScrollViewer? TreeScroll =>
            this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        /// <summary>
        /// Строки в порядке показа: обход контейнеров сверху вниз, в развёрнутые
        /// узлы с заходом внутрь. По координатам порядок не строится, потому что
        /// у части контейнеров пересчёт координат к дереву не удаётся, и такие
        /// строки сваливаются в начало.
        /// </summary>
        private List<TreeViewItem> VisibleRows()
        {
            var rows = new List<TreeViewItem>();
            Collect(this);
            return rows;

            void Collect(ItemsControl parent)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    rows.Add(item);
                    if (item.IsExpanded)
                        Collect(item);
                }
            }
        }

        /// <summary>
        /// Строка через экран от текущей. Отсчёт идёт по координатам, а не по
        /// числу строк: высота у групп и баз разная.
        /// </summary>
        private int PageStep(List<TreeViewItem> rows, int current, bool back)
        {
            if (current < 0)
                return back ? 0 : rows.Count - 1;

            // Отсчёт по координатам строки, а не по сумме высот контейнеров:
            // высота контейнера группы включает всех её детей, и шаг ушёл бы
            // во всю группу разом.
            var page = TreeScroll?.Viewport.Height ?? Bounds.Height;
            var from = Top(rows[current]);

            if (back)
            {
                for (var i = current - 1; i >= 0; i--)
                    if (Top(rows[i]) <= from - page)
                        return i;
                return 0;
            }

            for (var i = current + 1; i < rows.Count; i++)
                if (Top(rows[i]) >= from + page)
                    return i;
            return rows.Count - 1;

            double Top(TreeViewItem item) => item.TranslatePoint(default, this)?.Y ?? 0;
        }

        /// <summary>
        /// Возвращает контейнер строки для указанных данных по всему дереву
        /// (включая вложенные уровни групп) или null, если контейнер ещё не создан.
        /// Используется, чтобы вернуть клавиатурный фокус на строку после
        /// пересборки дерева, когда прежний контейнер уничтожен.
        /// </summary>
        public TreeViewItem? ContainerForItem(object data)
        {
            return Find(this, data);

            static TreeViewItem? Find(ItemsControl parent, object data)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    if (ReferenceEquals(item.DataContext, data))
                        return item;
                    if (item.IsExpanded)
                    {
                        var found = Find(item, data);
                        if (found is not null)
                            return found;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Контейнер строки ВНУТРИ конкретного узла (а не первое вхождение по всему дереву):
        /// закреплённая база присутствует в дереве дважды, и общий <see cref="ContainerForItem"/>
        /// всегда отдавал бы строку «Закреплённых» (они идут первыми). Команде «Найти в списке»
        /// (issue #285) нужна строка во «Все базы» — внутри настоящей группы или «Без группы».
        /// </summary>
        public TreeViewItem? ContainerForItemWithin(GroupNodeViewModel node, object data)
        {
            var container = ContainerForItem(node);
            return container is null ? null : FindWithin(container, data);

            static TreeViewItem? FindWithin(ItemsControl parent, object data)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    if (ReferenceEquals(item.DataContext, data))
                        return item;
                    if (item.IsExpanded)
                    {
                        var found = FindWithin(item, data);
                        if (found is not null)
                            return found;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Проверяет, что строка лежит в поддереве домашнего узла «Все базы» (issue #31):
        /// путь от строки вверх должен дойти до <paramref name="home"/> ДО того, как встретится
        /// узел «Закреплённые» (GroupNodeViewModel с <see cref="GroupNodeViewModel.PinnedMarker"/>).
        /// Используется как последний рубеж перед <see cref="SelectRow"/> в reveal перехода
        /// к строке — даже при сбое поиска выбор не должен «остановиться» на копии закреплений.
        /// </summary>
        public bool IsRowWithinHome(TreeViewItem row, GroupNodeViewModel home)
        {
            foreach (var ancestor in row.GetSelfAndVisualAncestors().OfType<TreeViewItem>())
            {
                if (ancestor.DataContext is GroupNodeViewModel gn)
                {
                    if (GroupNodeViewModel.IsPinnedNode(gn))
                        return false;
                    if (ReferenceEquals(gn, home))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Контейнер строки базы в нужной секции дерева (issue #340): «Закреплённые» —
        /// строка с обёрткой <see cref="PinnedInfobaseItem"/>, обычный список — сама модель
        /// <see cref="Infobase"/>. Используется fallback-применением и стабилизацией
        /// выделения после закрытия контекстного меню. Контейнер вне видимой области не
        /// реализован (виртуализация) — возвращается null.
        /// </summary>
        public TreeViewItem? FindRowForData(Infobase target, bool pinnedSection)
        {
            return Find(this, target, pinnedSection);

            static TreeViewItem? Find(ItemsControl parent, Infobase target, bool pinnedSection)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    if (ReferenceEquals(BatchSelectionHelper.Unwrap(item.DataContext), target)
                        && BatchSelectionHelper.IsPinnedSection(item.DataContext) == pinnedSection)
                    {
                        return item;
                    }
                    if (item.IsExpanded)
                    {
                        var found = Find(item, target, pinnedSection);
                        if (found is not null)
                            return found;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Доводит строку до видимой области по её данным (issue #285). Контейнер строки
        /// далеко вниз внутри длинной раскрытой группы не реализован (контейнеры создаются
        /// только для видимой области), поэтому метод итеративно двигает внутренний
        /// ScrollViewer к цели: сначала к контейнеру домашнего узла — если он реализован,
        /// прокручивает к его заголовку, а если нет (заголовок выше вьюпорта) — вверх до его
        /// появления, — затем вниз к самой строке. Возвращает true, если контейнер строки
        /// найден и прокрутка к нему выполнена. При неудаче позиция прокрутки всё равно
        /// продвигается к цели: последующие попытки RevealFindInListAttempt продолжают с места.
        /// </summary>
        public bool EnsureDataVisible(object data, object? home)
        {
            // Строка ищется ТОЛЬКО в поддереве домашнего узла «Все базы» (issue #31):
            // общий поиск по данным для закреплённой базы находил бы ПЕРВУЮ копию
            // в «Закреплённых» (она идёт первой в дереве) и прокручивал бы к ней —
            // reveal «останавливался» бы на закреплении вместо домашней строки.
            var homeNode = home as GroupNodeViewModel;
            if (homeNode is not null)
            {
                if (ContainerForItemWithin(homeNode, data) is { } within)
                {
                    within.BringIntoView();
                    return true;
                }
            }
            else if (ContainerForItem(data) is { } item)
            {
                item.BringIntoView();
                return true;
            }
            if (TreeScroll is not { } scroll)
                return false;

            const int maxSteps = 8;
            var viewport = Math.Max(1, scroll.Viewport.Height);
            var hidden = Math.Max(0, scroll.Extent.Height - viewport);

            // 1) Добираемся до домашнего узла: вниз, если его заголовок уже реализован,
            //    либо вверх, если он выше текущего вьюпорта и контейнер ещё не создан.
            for (var step = 0; step < maxSteps; step++)
            {
                if (homeNode is not null && ContainerForItem(homeNode) is { } homeItem)
                {
                    homeItem.BringIntoView();
                    break;
                }
                if (scroll.Offset.Y <= 0)
                    return false;
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y - viewport));
            }

            // 2) От заголовка домашнего узла спускаемся к строке цели — снова строго
            //    внутри поддерева homeNode (не в «Закреплённые»).
            for (var step = 0; step < maxSteps; step++)
            {
                var found = homeNode is not null
                    ? ContainerForItemWithin(homeNode, data)
                    : ContainerForItem(data);
                if (found is { } target)
                {
                    target.BringIntoView();
                    return true;
                }
                if (scroll.Offset.Y >= hidden)
                    return false;
                scroll.Offset = scroll.Offset.WithY(Math.Min(hidden, scroll.Offset.Y + viewport));
            }
            return false;
        }

        /// <summary>
        /// Локальная «заглушка» выделения строк базы при подготовке контейнера.
        /// Штатная разметка выделения красит контейнер по принадлежности данных
        /// к SelectedItems (TreeContainerFromItem → SetCurrentValue); перекрашивание
        /// чужих строк исключено тем, что данные в дереве уникальны (обёртка
        /// <see cref="PinnedInfobaseItem"/> в узле «Закреплённые», issue #301), а
        /// заглушка страхует подсветку строк, чьи базы ещё числятся выбранными
        /// с прошлой пересборки. Строки групп не трогаем: их подсветка идёт
        /// из модели (GroupNodeViewModel.IsSelected).
        /// </summary>
        protected override void ContainerForItemPreparedOverride(Control container, object? item, int index)
        {
            base.ContainerForItemPreparedOverride(container, item, index);

            if (item is Infobase or PinnedInfobaseItem && container is TreeViewItem row && !row.IsSelected)
                row.IsSelected = false;
        }

        protected override void ClearContainerForItemOverride(Control container)
        {
            // Контейнер уходит из дерева (пересборка, сворачивание): прежняя ссылка
            // на подсвеченную строку больше недействительна, иначе следующая SelectRow
            // гасила бы локальное значение уже чужой строки.
            if (ReferenceEquals(_selectedRow, container))
                _selectedRow = null;
            ReleaseExpandedBinding(container);
            base.ClearContainerForItemOverride(container);
        }

        private void ReleaseExpandedBinding(Control container)
        {
            if (!_expandedBindings.TryGetValue(container, out var previous))
                return;
            previous.Dispose();
            _expandedBindings.Remove(container);
        }
    }
}
#endif