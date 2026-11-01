#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Workspace1C
{
    public partial class MainWindow
    {

        /// <summary>Данные строки, к которой запланирована прокрутка после выбора (issue #255).</summary>
        private object? _scrollTargetData;
        private bool _scrollQueued;

        /// <summary>
        /// Идёт «Найти в списке» (issue #285): цель уже выставлена во вьюмодели, и при
        /// восстановлении строки прокрутку нужно вести К ЦЕЛИ, а не возвращать прежнюю
        /// позицию. Устанавливается обработчиком RevealFindInListRequested и сбрасывается
        /// в RevealAndSelectAfterRebuild после применения.
        /// </summary>
        private bool _revealFindInListPending;

        /// <summary>
        /// Липкая цель «перехода к строке» (issue #31): база, к которой идёт навигация,
        /// запоминается в момент RevealFindInListRequested и используется reveal'ом вместо
        /// SelectedInfobase. Поздняя пересборка (сброс поиска/тегов внутри Ctrl+T) сбрасывает
        /// выбранный элемент дерева и очищает SelectedInfobase раньше, чем отработает
        /// отложенный RevealAndSelectAfterRebuild, — цель из override сохраняется.
        /// </summary>
        private Infobase? _revealTargetOverride;

        /// <summary>Максимум «добирающих» проходов при поиске контейнера цели после пересборки (issue #285).</summary>
        private const int MaxRevealAttempts = 6;

        /// <summary>
        /// Собирает контейнеры видимых строк дерева в порядке их отображения
        /// (сверху вниз), включая строки развёрнутых подгрупп. Навигация ведётся
        /// по контейнерам, а не по объектам данных: закреплённая база присутствует
        /// в дереве дважды (узел «Закреплённые» и собственная группа), и работа
        /// с данными всякий раз возвращала бы первое (верхнее) вхождение.
        /// </summary>
        private List<TreeViewItem> GetVisibleTreeViewItems()
        {
            if (MainTree is null)
                return new List<TreeViewItem>();
            var rows = new List<TreeViewItem>();
            Collect(MainTree);
            return rows;

            void Collect(ItemsControl parent)
            {
                for (var i = 0; i < parent.Items.Count; i++)
                {
                    if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    rows.Add(item);
                    if (item.IsExpanded)
                        Collect(item);
                }
            }
        }

        /// <summary>
        /// Базы в видимом порядке строк дерева (сверху вниз, включая строки
        /// развёрнутых подгрупп) ТОЛЬКО в пределах одной секции. Используется
        /// Shift-диапазоном мультивыделения (0.3.9.90): правило секций (issue #326)
        /// не даёт смешать «Закреплённые» с обычным списком. Порядок секции
        /// «Закреплённые» строится по ДАННЫМ узла (BuildPinnedSectionVisibleOrder),
        /// а не по контейнерам: при виртуализации контейнеры вне видимой области
        /// не реализованы, и обход вернул бы пустой/неполный порядок, из-за чего
        /// Shift-диапазон уходил в общий список (issue #326). Порядок обычной
        /// секции строится по контейнерам, как в навигации (GetVisibleTreeViewItems).
        /// </summary>
        /// <param name="pinnedSection">
        /// true — только строки узла «Закреплённые» (обёртки PinnedInfobaseItem,
        /// разворачиваются до реальной базы); false — только обычные строки
        /// (Infobase), закреплённые копии исключаются (issue #314).
        /// </param>
        private List<Infobase> VisibleInfobasesInOrder(bool pinnedSection)
        {
            if (pinnedSection)
                return _viewModel.BuildPinnedSectionVisibleOrder().ToList();

            var result = new List<Infobase>();
            foreach (var item in GetVisibleTreeViewItems())
            {
                // Строки узла «Закреплённые» несут обёртку PinnedInfobaseItem
                // (уникальные данные строки, issue #314), обычные — саму модель.
                // Берём только строки запрошенной секции (#326): диапазон в одной
                // секции никогда не захватывает строки другой.
                var isPinnedRow = Services.BatchSelectionHelper.IsPinnedSection(item.DataContext);
                if (isPinnedRow != pinnedSection)
                    continue;
                if (UnwrapInfobase(item.DataContext) is { } ib && !result.Contains(ib))
                    result.Add(ib);
            }
            return result;
        }

        /// <summary>
        /// Индекс текущей строки навигации. Определяется по контейнеру под
        /// фокусом либо под выделением, а не по объекту данных: закреплённая
        /// база присутствует в дереве дважды (узел «Закреплённые» и собственная
        /// группа), и поиск по ссылке данных вернул бы первое вхождение,
        /// «перепрыгивая» выделение в начало списка.
        /// </summary>
        private int FindCurrentRowIndex(List<TreeViewItem> rows)
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
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

            return rows.FindIndex(item =>
                (UnwrapInfobase(item.DataContext) is { } ib && ReferenceEquals(ib, _viewModel.SelectedInfobase)) ||
                (item.DataContext is GroupNodeViewModel gn && ReferenceEquals(gn, _viewModel.SelectedGroupNode)));
        }

        /// <summary>
        /// Выделяет указанный узел дерева (группу или базу), синхронизирует модель
        /// и переводит фокус на соответствующий TreeViewItem, чтобы дальнейшая
        /// навигация стрелками была стабильной и не «прыгала» на кнопки.
        /// </summary>
        private void SelectTreeNode(object node)
        {
            var item = FindTreeViewItemForData(node);
            switch (node)
            {
                case Infobase infobase:
                    if (item is not null)
                        ApplySelection(item, infobase);
                    else
                        _viewModel.SelectedInfobase = infobase;
                    break;
                case GroupNodeViewModel group when group.Group is not null:
                    if (item is not null)
                        ApplyGroupSelection(item, group);
                    else
                        _viewModel.SelectedGroupNode = group;
                    break;
            }

            if (item is not null)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
            else
            {
                Keyboard.Focus(MainTree);
            }

            // Прокручиваем список к выбранной строке (отложенно, чтобы контейнер
            // виртуализированного узла успел создаться после установки выделения).
            QueueScrollSelectedIntoView(item);
        }

        /// <summary>
        /// Выделяет конкретную строку дерева и синхронизирует модель, не ища
        /// контейнер заново по данным: у закреплённой базы данные встречаются
        /// дважды (узел «Закреплённые» и собственная группа), и повторный поиск
        /// вернул бы первую (верхнюю) копию, «перепрыгивая» выделение в начало.
        /// </summary>
        private void SelectRowItem(TreeViewItem item)
        {
            switch (item.DataContext)
            {
                // Закреплённая база в узле «Закреплённые» приходит обёрткой
                // PinnedInfobaseItem — разворачиваем до реальной базы (issue #314).
                case Infobase:
                case PinnedInfobaseItem:
                    if (UnwrapInfobase(item.DataContext) is { } infobase)
                        ApplySelection(item, infobase);
                    break;
                case GroupNodeViewModel group when group.Group is not null:
                    ApplyGroupSelection(item, group);
                    break;
            }

            // Фокус переносится и на служебные узлы («Закреплённые», «Без группы»):
            // у них модель не выделяется, но навигация должна продолжиться оттуда.
            item.Focus();
            System.Windows.Input.Keyboard.Focus(item);

            // Прокрутка к строке, как у SelectTreeNode.
            QueueScrollSelectedIntoView(item);
        }

        /// <summary>
        /// Восстанавливает последнюю выбранную строку списка (базу или группу) после запуска.
        /// Строка определяется по сохранённым значениям
        /// <see cref="ViewModels.MainViewModel.LastSelectedInfobaseId"/> и
        /// <see cref="ViewModels.MainViewModel.LastSelectedGroupPath"/>.
        /// </summary>
        private void RestoreLastSelection()
        {
            if (MainTree is null || _viewModel is null)
                return;

            object? target = null;

            var infobaseId = _viewModel.LastSelectedInfobaseId;
            if (!string.IsNullOrEmpty(infobaseId))
            {
                var ib = _viewModel.Infobases.FirstOrDefault(
                    i => string.Equals(i.Id, infobaseId, StringComparison.Ordinal));
                if (ib is not null)
                    target = ib;
            }
            else
            {
                var groupPath = _viewModel.LastSelectedGroupPath;
                if (!string.IsNullOrEmpty(groupPath))
                {
                    var groupNode = _viewModel.FindGroupNodeByPath(groupPath);
                    if (groupNode is not null)
                        target = groupNode;
                }
            }

            if (target is null)
                return;

            // Отложенно, чтобы виртуализированное дерево успело сгенерировать
            // контейнер строки после первой отрисовки.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (target is Infobase infobase)
                    SelectTreeNode(infobase);
                else if (target is GroupNodeViewModel group)
                    SelectTreeNode(group);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Восстанавливает выделение и клавиатурный фокус выбранной строки дерева после
        /// пересборки списка (например, после сохранения настроек базы). Прежний контейнер
        /// строки уничтожен заменой коллекции <see cref="ViewModels.MainViewModel.GroupNodes"/>,
        /// поэтому подсветка и фокус пропадают вместе с ним. Выделение восстанавливается всегда;
        /// клавиатурный фокус возвращается только если сейчас не идёт ввод в текстовом поле
        /// (поиск, теги) — чтобы не выбивать курсор при наборе.
        /// </summary>
        private void RestoreTreeKeyboardFocus()
        {
            if (MainTree is null || _viewModel is null)
                return;

            // Один атомарный проход на приоритете Render (до отрисовки следующего кадра):
            // выделение, раскрытие предков и восстановление позиции прокрутки применяются
            // в одном синхронном вызове, без промежуточной отрисовки между ними. Раньше это
            // были два прохода (Loaded → ApplicationIdle): между «сдвигом к строке» (BringIntoView)
            // и «возвратом позиции» успевал отрисоваться кадр, из-за чего список «пролистывался
            // повыше», а потом возвращался к активной строке (issue #252).
            Dispatcher.BeginInvoke(new Action(() => RevealAndSelectAfterRebuild()),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>
        /// Окно узнало, что выполняется целевая навигация «переход к строке» (issue #31):
        /// цель уже выставлена во вьюмодели, и при восстановлении строки прокрутку нужно
        /// вести К ЦЕЛИ, а не возвращать прежнюю позицию. Флаг и липкая цель сбрасываются
        /// в RevealAndSelectAfterRebuild после применения.
        /// </summary>
        private void OnFindInListRequested()
        {
            // Липкая цель: запоминаем базу в момент запроса (см. _revealTargetOverride).
            _revealTargetOverride = _viewModel?.SelectedInfobase;
            _revealFindInListPending = true;
            RevealTrace.Log($"WPF OnFindInListRequested: target={DescribeRevealTarget(_revealTargetOverride)}, pending=true");
            // Reveal планируется здесь явно, а не только из TreeRebuilt через
            // RestoreTreeKeyboardFocus: во время целевой навигации
            // (IsRevealNavigationInProgress) промежуточное восстановление в TreeRebuilt
            // пропускается, и этот отложенный вызов остаётся единственным.
            Dispatcher.BeginInvoke(new Action(() => RevealAndSelectAfterRebuild()),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>
        /// Восстанавливает выделение и клавиатурный фокус выбранной строки после пересборки.
        /// С учётом виртуализации: при виртуализации контейнер дочерней строки существует только
        /// внутри раскрытой группы, поэтому сначала раскрывается цепочка групп-предков цели,
        /// затем контейнер выбирается, доводится до видимой области и (вне текстового поля) получает
        /// фокус. Цель читается в момент выполнения, поэтому порядок установки SelectedInfobase
        /// относительно пересборки не важен.
        /// </summary>
        private void RevealAndSelectAfterRebuild(int attempt = 0)
        {
            if (MainTree is null || _viewModel is null)
                return;

            try
            {
                // Пока reveal в работе, цель берём из липкого запроса (issue #31): поздняя
                // пересборка могла очистить SelectedInfobase (SelectedItemChanged(null)).
                var target = _revealFindInListPending && _revealTargetOverride is not null
                    ? (object?)_revealTargetOverride
                    : (object?)_viewModel.SelectedInfobase ?? _viewModel.SelectedGroupNode;
                if (target is null)
                    return;

                // «Найти в списке» (issue #285): строка должна быть во «Все базы» — в настоящей
                // группе базы или узле «Без группы», а НЕ первой копией в «Закреплённых».
                // Закреплённая база присутствует в дереве дважды, и общий поиск всегда отдавал
                // закрепления (они идут первыми), из-за чего команда «переходила» туда, а не
                // к строке в общем списке. Обычные пересборки дерева не трогаем.
                var revealToHome = _revealFindInListPending;

                // Планирование reveal (issue #31): домашний узел (leaf) и достижимость цели
                // считаются ЧИСТОЙ логикой по данным дерева (RevealPlanner) без визуального
                // дерева; при revealToHome узел «Закреплённые» из кандидатов исключён — leaf
                // это настоящая группа во «Все базы», «Без группы» либо плоский узел.
                var plan = RevealPlanner.Plan(_viewModel.GroupNodes, target, revealToHome);

                // Ранний выход (issue #21 + #31): если цель не присутствует в текущем дереве
                // данных (например, выбранная база не входит в режим вкладки «Избранное», либо
                // при revealToHome база представлена только копией в «Закреплённых»), её контейнер
                // никогда не материализуется — не гоняем повторные проходы с принудительными
                // полными раскладками (UpdateLayout на каждой попытке до MaxRevealAttempts).
                // Проверка только по ViewModel-данным, визуальное дерево не трогаем.
                if (!plan.Reachable)
                {
                    // Цель недостижима (issue #31): reveal не сможет найти строку — снимаем
                    // липкий запрос, чтобы он не перехватывал последующие обычные пересборки.
                    if (_revealFindInListPending)
                    {
                        _revealFindInListPending = false;
                        _revealTargetOverride = null;
                    }
                    RevealTrace.Log($"WPF Reveal: target={DescribeRevealTarget(target)}, " +
                                    $"revealToHome={revealToHome}, leaf=null, plan=rejected");
                    return;
                }
                var leaf = plan.Leaf;

                // Плоский режим (issue #31): «Все базы» без группировки/отбор — дерево из одного
                // постоянного _flatNode, дублей нет, reveal обязан вести внутрь него.
                // (issue #21, Этап F: стабильный набор корней держит группы в GroupNodes
                // скрытыми, поэтому плоский режим определяется по флагу, а не по Count==1.)
                if (revealToHome && _viewModel.IsFlatModeActive)
                {
                    RevealTrace.Log($"WPF Reveal: flat mode (GroupNodes==[_flatNode]), " +
                                    $"leaf={DescribeLeaf(leaf)}, target={DescribeRevealTarget(target)}");
                }

                RevealTrace.Log($"WPF Reveal: attempt={attempt}, target={DescribeRevealTarget(target)}, " +
                                $"revealToHome={revealToHome}, leaf={DescribeLeaf(leaf)}, reachable=true");
                var stack = new Stack<GroupNodeViewModel>();
                for (var g = leaf; g is not null && g.Group is not null; g = g.Parent)
                    stack.Push(g);
                // Раскрываем цепочку групп от корня к цели: BringIntoView гарантирует, что группа
                // попадает в видимую область и материализуется (иначе при виртуализации её контейнер
                // может отсутствовать), после чего раскрытие реализует дочерние строки.
                // Группы, которые пользователь свернул, принудительно не раскрываем: иначе свёрнутая
                // группа, внутри которой выбрана база, после любой пересборки дерева (таймер
                // синхронизации, редактирование, сортировка) разворачивалась бы обратно, и она
                // «не сворачивалась» бы. Скрытый элемент просто останется не выделенным.
                foreach (var group in stack)
                {
                    // Группы, которые пользователь свернул, принудительно не раскрываем.
                    // Опора только на group.IsExpanded недостаточна: к моменту пересборки
                    // IsExpanded у свёрнутой группы может быть true (авторазворачивание при
                    // поиске/фильтре либо значение ещё не применено), а ключ свёрнутой
                    // группы уже сохранён в _collapsedGroups. Иначе свёрнутый родитель,
                    // внутри которого выбрана база во вложенной группе «домашнее», после
                    // любой пересборки раскрывался бы обратно и «не сворачивался».
                    if (!group.IsExpanded || _viewModel.IsGroupCollapsed(group.NodeKey))
                        continue;
                    if (FindTreeViewItemForData(group) is { } gItem)
                    {
                        // Прокрутка к контейнеру предка. Сам контейнер уже реализован:
                        // FindTreeViewItemForData отдаёт только созданные. Прокрутка заставляет
                        // виртуализацию достроить соседние строки, контейнеры потомков
                        // появляются в проходе разметки ниже.
                        // Раскрытие приходит из модели через OneWay-привязку (Setter внутри
                        // DataTrigger в ItemContainerStyle). Прямая установка gItem.IsExpanded
                        // оставляла бы на контейнере local value, у которого приоритет выше,
                        // и группа переставала сворачиваться и кнопкой в строке, и командой
                        // «Свернуть всё». По той же причине такие установки ранее убраны
                        // из ApplyGroupExpandedState.
                        gItem.BringIntoView();
                    }
                }

                // Один проход разметки доводит каскад раскрытия до конца в пределах
                // реализованного диапазона: виртуализация достраивает контейнеры раскрытых
                // веток, и контейнер цели ниже уже существует. Строку далеко за вьюпортом
                // это не создаёт — тогда поиск ниже вернёт null, и «добирающий» проход по
                // ApplicationIdle повторяет раскладку и поиск (issue #285).
                MainTree.UpdateLayout();

                var item = FindTargetContainer(target, revealToHome ? leaf : null);
                RevealTrace.Log($"WPF Reveal: attempt={attempt}, containerSearch: found={item is not null}, " +
                                $"homeLeaf={DescribeLeaf(leaf)}");
                if (item is null && revealToHome)
                {
                    // Домашний узел не реализован при виртуализации (issue #31): контейнер
                    // группы далеко за вьюпортом не создаётся, и FindTreeViewItemForData(leaf)
                    // вернёт null на ЛЮБОМ повторном проходе без сдвига прокрутки — материализуем
                    // дерево целиком сразу, не полагаясь на «добирающие» попытки с пустым результатом.
                    if (leaf is not null && FindTreeViewItemForData(leaf) is null)
                    {
                        RevealTrace.Log($"WPF Reveal: attempt={attempt}, home leaf container NOT realized " +
                                        $"({DescribeLeaf(leaf)}) — materializing immediately");
                        item = MaterializeRevealTarget(target, leaf);
                    }
                    else
                    {
                        // «Найти в списке» (issue #285): строка цели далеко вниз внутри длинной
                        // раскрытой группы. При Recycling-виртуализации (VirtualizingStackPanel,
                        // ScrollUnit=Pixel) её контейнер ниже вьюпорта не создаётся никогда, и
                        // повторные проходы БЕЗ сдвига прокрутки бесполезны. В повторных проходах
                        // первым делом прокручиваем к контейнеру домашней группы (BringIntoView):
                        // его строки материализуются, и цель может попасть в реализованный диапазон.
                        if (attempt > 0 && leaf is { } leafNode && FindTreeViewItemForData(leafNode) is { } homeItem)
                        {
                            homeItem.BringIntoView();
                            MainTree.UpdateLayout();
                            item = FindTargetContainer(target, leaf);
                        }

                        // Если цель всё ещё не создана — материализуем дерево целиком: временно
                        // отключаем виртуализацию, находим контейнер цели, прокручиваем к нему,
                        // возвращаем виртуализацию (цель остаётся в видимой области, а свежий
                        // контейнер после включения ищется по данным заново).
                        if (item is null)
                            item = MaterializeRevealTarget(target, leaf);
                    }
                }

                if (item is null)
                {
                    // Контейнер цели ещё не создан: строка ниже реализованного диапазона
                    // виртуализации либо ветка раскрылась после первого прохода разметки.
                    // Запланированный ниже повтор (ApplicationIdle) ещё раз вызывает
                    // UpdateLayout и поиск; при неудаче за отведённое число попыток выходим.
                    if (attempt < MaxRevealAttempts)
                    {
                        RevealTrace.Log($"WPF Reveal: attempt={attempt}, container not realized — " +
                                        $"scheduling attempt={attempt + 1}");
                        Dispatcher.BeginInvoke(new Action(() => RevealAndSelectAfterRebuild(attempt + 1)),
                            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                    else
                    {
                        // Попытки исчерпаны (issue #31): снимаем липкий запрос, чтобы он не
                        // перехватывал последующие обычные пересборки (паритет с Avalonia).
                        RevealTrace.Log($"WPF Reveal: attempt={attempt} >= MaxRevealAttempts={MaxRevealAttempts}, " +
                                        "EXHAUSTED: контейнер домашней строки не найден");
                        if (_revealFindInListPending)
                        {
                            _revealFindInListPending = false;
                            _revealTargetOverride = null;
                        }
                    }
                    return;
                }

                switch (target)
                {
                    case Infobase infobase:
                        // Запрет выбора копии «Закреплённые» при revealToHome (issue #31):
                        // проверка ПЕРЕД ApplySelection — контейнер обязан лежать в поддереве
                        // домашнего узла и не иметь предка GroupNodeViewModel с PinnedMarker.
                        // Если FindTargetContainer вернул чужой контейнер (сбой планирования/
                        // устаревшее дерево), пробуем материализацию домашней строки.
                        if (revealToHome && IsPinnedSectionContainer(item))
                        {
                            RevealTrace.Log($"WPF Reveal: pinned-copy guard hit for target={infobase.Id} " +
                                            $"(leaf={DescribeLeaf(leaf)}) — materializing home row");
                            item = MaterializeRevealTarget(infobase, leaf);
                            if (item is null || IsPinnedSectionContainer(item))
                            {
                                RevealTrace.Log($"WPF Reveal: home row still not found, selection NOT set " +
                                                $"on pinned copy (target={infobase.Id})");
                                return;
                            }
                        }
                        ApplySelection(item, infobase);
                        RevealTrace.Log($"WPF Reveal: attempt={attempt}, SELECTED target={infobase.Id}, " +
                                        $"pinnedSection={IsPinnedSectionContainer(item)}, revealToHome={revealToHome}");
                        break;
                    case GroupNodeViewModel group when group.Group is not null:
                        ApplyGroupSelection(item, group);
                        break;
                    default:
                        return;
                }

                if (_revealFindInListPending)
                {
                    // «Найти в списке» (issue #285): доводим строку цели до видимой области.
                    // Возврат прежней позиции здесь отменяется: RestoreTreeScrollAfterRebuild
                    // вернул бы позицию предыдущей вкладки («Избранное»/«Закреплённые») и
                    // «спрятал» целевую базу. Липкий запрос (issue #31) снимается вместе
                    // с флагом: следующая обычная пересборка восстановит выделение штатно.
                    _revealFindInListPending = false;
                    _revealTargetOverride = null;
                    ScrollSelectedIntoView(item);
                }
                else
                {
                    // Позицию прокрутки возвращаем в том же синхронном проходе, до отрисовки
                    // следующего кадра (issue #252). Промежуточный BringIntoView к цели не делаем:
                    // восстановление позиции (RestoreTreeScrollAfterRebuild) само приводит вьюпорт
                    // к сохранённому offset/якорю, а лишний сдвиг к строке давал двухфазный «скачок»
                    // списка «повыше» → к активной строке. Если группа и верхняя видимая строка
                    // не изменились — RestoreTreeScrollAfterRebuild вернёт управление, не тронув
                    // позицию вовсе.
                    RestoreTreeScrollAfterRebuild();
                }

                // Клавиатурный фокус возвращаем строке. Защищаем только поле поиска: после закрытия
                // модального окна настроек WPF может временно держать фокус на каком-либо контроле,
                // и строгая проверка «не TextBox» оставила бы базу без фокуса. Во время набора в поиске
                // курсор из поля не выбиваем.
                if (SearchTextBox is null || !ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, SearchTextBox))
                {
                    item.Focus();
                    System.Windows.Input.Keyboard.Focus(item);
                }
            }
            catch { /* элемент мог отсоединиться во время пересборки */ }
        }

        /// <summary>
        /// «Найти в списке» (issue #285): принудительно материализует контейнер строки цели.
        /// Строка внизу внутри длинной раскрытой группы не создаётся при Recycling-
        /// виртуализации (VirtualizingStackPanel): временно отключаем виртуализацию дерева,
        /// выполняем раскладку — контейнеры создаются для всех строк, — прокручиваем к цели
        /// и возвращаем виртуализацию. После включения цель остаётся в видимой области,
        /// и свежий контейнер ищется заново по данным. Возвращает контейнер цели либо null,
        /// если строка так и не найдена.
        /// </summary>
        private TreeViewItem? MaterializeRevealTarget(object target, GroupNodeViewModel? homeNode)
        {
            if (MainTree is null)
                return null;
            var wasVirtualizing = VirtualizingPanel.GetIsVirtualizing(MainTree);
            if (!wasVirtualizing)
                return FindTargetContainer(target, homeNode);

            VirtualizingPanel.SetIsVirtualizing(MainTree, false);
            try
            {
                MainTree.UpdateLayout();
                var item = FindTargetContainer(target, homeNode);
                if (item is null)
                    return null;
                // Прокрутка при полной материализации: позиция цели вычисляется точно
                // (контейнеры всех строк существуют, высоты актуальны).
                ScrollSelectedIntoView(item);
            }
            finally
            {
                VirtualizingPanel.SetIsVirtualizing(MainTree, wasVirtualizing);
                MainTree.UpdateLayout();
            }

            // После возврата виртуализации контейнеры пересозданы для видимой области:
            // цель в ней, ищем свежий контейнер по данным (прежний мог быть переиспользован).
            return FindTargetContainer(target, homeNode);
        }

        /// <summary>
        /// Планирует прокрутку к строке по её данным, а не по контейнеру. В режиме
        /// Recycling-виртуализации контейнер переиспользуется под другие строки, поэтому
        /// захват ссылки на контейнер в отложенном вызове к моменту исполнения мог указывать
        /// уже на другую базу — список «прыгал» туда-сюда, а при автоповторе клавиши «вниз»
        /// в очереди копились десятки таких вызовов, которые доигрывали и после отпускания
        /// клавиши (issue #255). Вызовы склеиваются по флагу: за проход исполняется только
        /// последняя цель.
        /// </summary>
        private void QueueScrollSelectedIntoView(TreeViewItem? item)
        {
            if (item is null)
                return;
            _scrollTargetData = item.DataContext;
            if (_scrollQueued)
                return;
            _scrollQueued = true;
            Dispatcher.BeginInvoke(new Action(DeferredScrollSelectedIntoView),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void DeferredScrollSelectedIntoView()
        {
            _scrollQueued = false;
            var data = _scrollTargetData;
            _scrollTargetData = null;
            if (data is null)
                return;
            var item = FindTreeViewItemForData(data);
            if (item is null)
                return;
            ScrollSelectedIntoView(item);
        }

        /// <summary>
        /// Прокручивает список так, чтобы указанный элемент дерева был в зоне видимости.
        /// Использует внутренний ScrollViewer дерева (вертикальная прокрутка списка).
        /// </summary>
        private void ScrollSelectedIntoView(TreeViewItem? item)
        {
            if (item is null)
                return;

            var scrollViewer = GetTreeScrollViewer();
            if (scrollViewer is null)
                return;

            try
            {
                // При прокрутке ВНИЗ ниже края вьюпорта из-за Recycling-виртуализации
                // реализуются НОВЫЕ контейнеры строк, раскладка которых к этому моменту
                // ещё не выполнена: позиция (TransformToAncestor) и ActualHeight окажутся
                // неактуальными, и величина прокрутки получится больше одной строки.
                // Доводим раскладку ТОЛЬКО для только что реализованного/ещё не измеренного
                // контейнера: синхронная UpdateLayout на автоповторе клавиши «вниз» по уже
                // разложенным строкам давала десятки раскладок за проход, «рывки» и
                // «доигрывание» после отпускания клавиши (issue #255).
                if (!item.IsMeasureValid || !item.IsArrangeValid)
                    item.UpdateLayout();

                // TransformToAncestor(scrollViewer) даёт позицию элемента ОТНОСИТЕЛЬНО
                // вьюпорта (уже с учётом прокрутки): top/bottom лежат в диапазоне видимой
                // области (0..ViewportHeight), отрицательные — выше верха вьюпорта.
                // Их нельзя сравнивать с VerticalOffset — это смещение в координатах
                // контента (растёт при прокрутке вниз). Смешивание систем координат
                // давало «прыжки» списка к началу и «прятало» выбранную базу внизу.
                var point = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                var top = point.Y;                        // относительно вьюпорта
                var viewportBottom = scrollViewer.ViewportHeight;

                // Ограничиваем шаг размером вьюпорта. У контейнера ГРУППЫ ActualHeight включает
                // высоту всех дочерних строк, поэтому «выступ» за нижний край огромен и при
                // листании клавишами по папке список «перепрыгивал» вниз на несколько экранов
                // (issue #255). Цель прокрутки для группы — показать её ЗАГОЛОВОК (одну строку),
                // а не всё поддерево: поэтому нижняя граница считается по высоте типовой строки,
                // а не по ActualHeight контейнера группы (иначе список «уводило» на высоту всех
                // детей сразу, а ограничение шага вьюпортом заставляло его «прыгать» в самый низ
                // и обратно при автоповторе клавиши). Шаг по-прежнему ограничен вьюпортом.
                var bottom = item.DataContext is GroupNodeViewModel
                    ? top + ReferenceRowHeight()
                    : top + item.ActualHeight;

                if (top < 0)
                {
                    // Элемент выше верха вьюпорта: поднимаем, но не более чем на вьюпорт.
                    var step = Math.Min(-top, viewportBottom);
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - step);
                }
                else if (bottom > viewportBottom)
                {
                    // Элемент ниже низа вьюпорта: опускаем, но не более чем на вьюпорт.
                    var step = Math.Min(bottom - viewportBottom, viewportBottom);
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + step);
                }
            }
            catch
            {
                // Элемент мог отсоединиться от визуального дерева — игнорируем.
            }
        }

        /// <summary>
        /// Высота типовой строки списка (заголовок группы или строка базы). Используется как
        /// «нижняя граница» цели прокрутки для узла-ГРУППЫ: у контейнера группы ActualHeight
        /// включает высоту всех дочерних строк, и прокрутка по ней уводила список на высоту
        /// всего поддерева (issue #255). Для группы достаточно показать её заголовок — одну
        /// строку, поэтому берём высоту первой реализованной строки базы как эталонную.
        /// </summary>
        private double ReferenceRowHeight()
        {
            if (FindFirstInfobaseItem(MainTree) is { } first && first.ActualHeight > 0)
                return first.ActualHeight;
            // Резерв: стабильная высота ОДНОЙ строки, а не высота вьюпорта (issue #255). Возврат
            // ViewportHeight делал шаг прокрутки у нижней папки равным целому экрану — «перескок
            // вниз» при долистывании до последних групп/папок. Типовая высота строки (заголовок
            // группы/базы) не зависит от размера окна и даёт шаг ровно в одну строку.
            return 32;
        }

        /// <summary>
        /// Возвращает контейнер TreeViewItem для указанного DataContext
        /// (поиск по всем раскрытым уровням дерева).
        /// </summary>
        private TreeViewItem? FindTreeViewItemForData(object data)
        {
            if (MainTree is null)
                return null;
            return FindTreeViewItemIn(MainTree, data);
        }

        private static TreeViewItem? FindTreeViewItemIn(ItemsControl parent, object data)
        {
            var dataGroup = data as GroupNodeViewModel;
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;

                if (dataGroup is not null)
                {
                    // Узел-ГРУППА ищется строго по ссылке (issue #31): прежнее сравнение через
                    // UnwrapInfobase сводило DataContext ЛЮБОГО не-базового узла к null и
                    // «подходило» под ПЕРВЫЙ реализованный групповой заголовок (обычно
                    // «Закреплённые», идущий первым в дереве). Из-за этого FindTargetContainer
                    // искал цель ВНУТРИ «Закреплённых» и reveal «останавливался» на копии базы.
                    if (ReferenceEquals(tvi.DataContext, dataGroup))
                        return tvi;
                }
                else
                {
                    // Данные строки узла «Закреплённые» — обёртка PinnedInfobaseItem
                    // (issue #314): сравниваем по реальной базе, иначе контейнер
                    // закреплённой строки не находился бы по данным обычной базы.
                    if (ReferenceEquals(UnwrapInfobase(tvi.DataContext), UnwrapInfobase(data)))
                        return tvi;
                }

                if (tvi.Items.Count > 0)
                {
                    var found = FindTreeViewItemIn(tvi, data);
                    if (found is not null)
                        return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Разворачивает данные строки дерева до реальной базы: строка узла
        /// «Закреплённые» несёт обёртку <see cref="PinnedInfobaseItem"/> (уникальные
        /// данные каждой строки, issue #314), а выбор, команды и поиск работают
        /// с моделью <see cref="Infobase"/>, как раньше.
        /// </summary>
        private static Infobase? UnwrapInfobase(object? data) => Services.BatchSelectionHelper.Unwrap(data);

        /// <summary>
        /// Находит узел группы, в котором размещена указанная база.
        /// </summary>
        private GroupNodeViewModel? FindGroupNodeByInfobase(Infobase infobase)
        {
            // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
            // ищем только среди видимых узлов.
            foreach (var root in _viewModel.GroupNodes)
            {
                if (!root.IsVisible)
                    continue;
                var found = FindInNode(root, infobase);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private static GroupNodeViewModel? FindInNode(GroupNodeViewModel node, Infobase infobase)
        {
            foreach (var child in node.Children)
            {
                var found = FindInNode(child, infobase);
                if (found is not null)
                    return found;
            }
            if (node.Infobases.Any(ib => ReferenceEquals(UnwrapInfobase(ib), infobase)))
                return node;
            return null;
        }

        /// <summary>
        /// Возвращает контейнер строки цели. При «Найти в списке» (issue #285) строка ищется
        /// ТОЛЬКО внутри домашнего узла во «Все базы» (настоящая группа или «Без группы»):
        /// закреплённая база присутствует в дереве дважды, и общий поиск по данным вернул бы
        /// первую копию в «Закреплённых» (они идут первыми), из-за чего переход «уезжал» туда.
        /// Для обычных пересборок (homeNode == null) поведение прежнее — первый найденный.
        /// </summary>
        private TreeViewItem? FindTargetContainer(object target, GroupNodeViewModel? homeNode)
        {
            if (homeNode is not null)
            {
                // Контейнер домашнего узла уникален (объект узла один на дерево; поиск строго
                // по ссылке, см. FindTreeViewItemIn); затем ищем строку цели только в его
                // поддереве, не выходя за пределы «Все базы».
                if (FindTreeViewItemForData(homeNode) is { } groupItem)
                {
                    var item = FindTreeViewItemIn(groupItem, target);
                    // Гарантия «домашней» строки (issue #31): найденный контейнер обязан лежать
                    // в поддереве homeNode и НЕ иметь предка-«Закреплённые» (GroupNodeViewModel
                    // с PinnedMarker). При нарушении считаем «не найдено» — вызывающий пойдёт
                    // в материализацию домашней строки, а не в копию закреплений.
                    return item is not null && IsContainerWithinHomeTree(item, homeNode)
                        ? item
                        : null;
                }
                return null;
            }
            return FindTreeViewItemForData(target);
        }

        /// <summary>
        /// Синхронизирует выделение в дереве с выбранной базой.
        /// При выборе группы снимает выделение базы.
        /// </summary>
        private void OnMainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Выбором базы управляет code-behind через обработчики кликов
            // (OnInfobaseTree_PreviewMouseLeftButtonDown), которые явно устанавливают
            // TreeViewItem.IsSelected и SelectedInfobase. Здесь лишь фиксируем результат
            // изменения выбранного элемента, не трогая свойство Infobase.IsSelected.
            // Ранее двухсторонняя привязка IsSelected к модели порождала каскад событий
            // SelectedItemChanged (база дублируется в «Закреплённых» и в своей группе),
            // что приводило к бесконечной рекурсии и StackOverflowException.
            if (UnwrapInfobase(e.NewValue) is { } infobase)
            {
                _viewModel.SelectedInfobase = infobase;
                // Выбор базы снимает выбор группы.
                _viewModel.SelectedGroupNode = null;
            }
            else if (e.NewValue is GroupNodeViewModel groupNode)
            {
                // Выбор группы снимает выбор базы и фиксирует выбранную группу.
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = groupNode;
            }
            else if (e.NewValue is null)
            {
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = null;
            }

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // чтобы они активировались при программной установке выделения в дереве.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Завершает синхронизацию IsExpanded после «Развернуть всё» / «Свернуть всё».
        /// Модель уже приведена к нужному состоянию через <see cref="GroupNodeViewModel.IsExpanded"/>
        /// (PropertyChanged), поэтому контейнеры обновляются сами из OneWay-привязки
        /// (MainWindow.xaml:1723). Здесь остаётся только заставить виртуализацию
        /// (VirtualizingStackPanel) догенерировать контейнеры вновь развёрнутых веток.
        ///
        /// Прямые установки <see cref="TreeViewItem.IsExpanded"/> (local value) намеренно
        /// удалены (issue #160): у local value выше приоритет, чем у OneWay-привязки из
        /// DataTrigger, из-за чего после команды мышь перестаёт сворачивать/разворачивать
        /// отдельные папки. Обход по <c>ItemContainerGenerator.ContainerFromIndex</c> также
        /// ненадёжен под виртуализацией — для нереализованных строк он возвращает null,
        /// поэтому первое нажатие действовало лишь на видимые ветки, а остальные — со второго.
        /// Каскадная генерация здесь не нужна: разворачивание родителя порождает контейнеры
        /// детей, а те читают уже выставленный IsExpanded из модели.
        /// </summary>
        internal void ApplyGroupExpandedState(bool expand)
        {
            if (MainTree is null)
                return;

            // Один проход разметки доводит каскад разворачивания до конца.
            MainTree.UpdateLayout();
        }

        internal void RefreshTagRowVisibility()
        {
            if (MainTree is null)
                return;

            // issue #373: при изменении ShowTags привязка видимости строки тегов
            // обновляется, но виртуализация кэширует высоту уже материализованных
            // контейнеров — строки не перемеряются («где-то совсем нет, где-то еле
            // видно»). Сброс виртуализации только на самом TreeView (0.3.12.25) не
            // доводил перерисовку до конца: вложенные панели TreeViewItem имеют
            // собственный IsVirtualizing=true из стиля контейнера. Полная пересборка
            // дерева (0.3.12.31, RebuildGroupTree) решает проблему, но на больших
            // списках занимает ~1 секунду. Вместо неё сбрасываем виртуализацию на
            // ВСЕХ вложенных панелях — контейнеры перемеряются с актуальной высотой
            // строки тегов без пересоздания дерева, прокрутка и выделение не теряются.
            //
            // 0.3.12.47: одного сброса IsVirtualizing недостаточно. Панель при
            // перемеривании вызывает Measure() на уже материализованных TreeViewItem,
            // но у них IsMeasureValid=true и доступный размер не изменился — Measure()
            // является no-op, поэтому высота строки тегов не пересчитывается (теги
            // появляются не во всех строках, кое-где частично). Поэтому после
            // материализации всех контейнеров принудительно инвалидируем измерение
            // контейнеров и всего их содержимого (TreeViewItem → Bd → ContentPresenter
            // → InfobaseRowGrid → панель тегов) и делаем ещё один проход разметки.
            var panels = new List<VirtualizingStackPanel>();
            CollectVirtualizingPanels(MainTree, panels);
            if (panels.Count == 0)
            {
                MainTree.UpdateLayout();
                return;
            }

            foreach (var panel in panels)
                VirtualizingPanel.SetIsVirtualizing(panel, false);
            try
            {
                MainTree.UpdateLayout();
                // Принудительное перемеривание всех контейнеров строк с актуальной
                // видимостью панели тегов (см. комментарий выше).
                InvalidateMeasureRecursive(MainTree);
                MainTree.UpdateLayout();
            }
            finally
            {
                foreach (var panel in panels)
                    VirtualizingPanel.SetIsVirtualizing(panel, true);
                MainTree.UpdateLayout();
            }
        }

        /// <summary>Собирает все виртуализирующие панели в визуальном дереве (рекурсивно).</summary>
        private static void CollectVirtualizingPanels(DependencyObject root, List<VirtualizingStackPanel> result)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is VirtualizingStackPanel panel)
                    result.Add(panel);
                CollectVirtualizingPanels(child, result);
            }
        }

        /// <summary>
        /// Рекурсивно инвалидирует измерение элемента и всех его потомков в визуальном
        /// дереве. Нужно, чтобы при изменении ShowTags перемерилась вся цепочка строки
        /// (TreeViewItem → Bd → ContentPresenter → InfobaseRowGrid → панель тегов):
        /// инвалидация только контейнера не доходит до панели тегов, т.к. промежуточные
        /// элементы с IsMeasureValid=true пропускают Measure() как no-op.
        /// </summary>
        private static void InvalidateMeasureRecursive(DependencyObject root)
        {
            if (root is UIElement element)
                element.InvalidateMeasure();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                InvalidateMeasureRecursive(VisualTreeHelper.GetChild(root, i));
        }

        private void OnMainTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// Устанавливает выделение указанного узла группы и синхронизирует
        /// выбранную группу в модели представления (снимая выбор базы).
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplyGroupSelection(TreeViewItem item, GroupNodeViewModel groupNode)
        {
            UiExceptionGuard.Guard(
                nameof(ApplyGroupSelection),
                () => ApplyGroupSelectionCore(item, groupNode),
                () => $"group={groupNode.Group?.Name ?? "null"}, " +
                      $"containerDataContextMatches={ReferenceEquals(item.DataContext, groupNode)}");
        }

        private void ApplyGroupSelectionCore(TreeViewItem item, GroupNodeViewModel groupNode)
        {
            // issue #96: контейнер мог быть переиспущен виртуализацией (Recycling) и
            // показывать другую строку — подсветка чужой строки недопустима. Несовпадение
            // не бросает исключение: подсветка пропускается, выбор ставится только на
            // модель (штатное поведение нереализованной строки), расхождение — в trace.
            if (!ReferenceEquals(item.DataContext, groupNode))
            {
                MenuCloseTrace.Log($"ApplyGroupSelection: containerRecycled=true, " +
                                   $"expected={groupNode.Group?.Name ?? "null"}, " +
                                   $"actual={item.DataContext?.GetType().Name ?? "null"}");
            }
            else
            {
                item.IsSelected = true;
            }

            _viewModel.SelectedInfobase = null;
            _viewModel.SelectedGroupNode = groupNode;

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // т.к. программная установка выделения не всегда гарантирует автоматический
            // пересчёт CanExecute команд через CommandManager.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Устанавливает выделение указанного элемента дерева и синхронизирует
        /// выбранную базу в модели представления.
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplySelection(TreeViewItem item, Infobase infobase)
        {
            UiExceptionGuard.Guard(
                nameof(ApplySelection),
                () => ApplySelectionCore(item, infobase),
                () => $"target={infobase.Id}, " +
                      $"containerMatches={Services.BatchSelectionHelper.IsContainerForBase(item.DataContext, infobase)}");
        }

        private void ApplySelectionCore(TreeViewItem item, Infobase infobase)
        {
            // issue #96: контейнер, захваченный в момент клика, мог быть переиспущен
            // виртуализацией (Recycling) и к моменту применения показывать ДРУГУЮ строку
            // (DataContext уже заменён) — установка IsSelected подсветила бы чужую строку,
            // а при осиротевшем контейнере бросила бы NRE, видимое как «Ошибка интерфейса».
            // Несовпадение не бросает: подсветка пропускается, выбор ставится только на
            // модель (штатное поведение нереализованной строки), расхождение — в trace.
            // В штатных путях (контейнер под кликом или найденный по данным) контекст
            // всегда совпадает — поведение не меняется (регресс #340/#97 обязателен).
            if (!Services.BatchSelectionHelper.IsContainerForBase(item.DataContext, infobase))
            {
                MenuCloseTrace.Log($"ApplySelection: containerRecycled=true, " +
                                   $"target={infobase.Id}, " +
                                   $"actualDataContext={item.DataContext?.GetType().Name ?? "null"}");
            }
            else
            {
                item.IsSelected = true;
            }

            _viewModel.SelectedInfobase = infobase;
        }

        /// <summary>
        /// Выбирает строку дерева по ДАННЫМ базы (issue #340). Контейнер, зафиксированный
        /// в момент клика, мог быть переиспользован виртуализацией (Recycling) и к моменту
        /// применения показывать другую строку, поэтому используется только если всё ещё
        /// показывает ту же базу; иначе контейнер ищется заново по данным В ТОЙ ЖЕ СЕКЦИИ
        /// (закреплённая база дублируется в узле «Закреплённые» и в своей группе — поиск
        /// без учёта секции мог бы подсветить копию, а не строку под кликом). Резерв:
        /// строка не реализована (вне видимой области) — выбор ставится на модели,
        /// подсветка синхронизируется при появлении строки в видимой области.
        /// </summary>
        /// <param name="target">База, которую нужно выбрать.</param>
        /// <param name="clickedContainer">Контейнер под кликом (может быть переиспользован).</param>
        /// <param name="isPinnedSection">Секция строки под кликом: true — «Закреплённые» (issue #326).</param>
        private void SelectTreeRowByData(Infobase target, TreeViewItem? clickedContainer, bool isPinnedSection)
        {
            // Захваченный контейнер корректен только пока показывает ту же базу.
            if (clickedContainer is not null &&
                ReferenceEquals(UnwrapInfobase(clickedContainer.DataContext), target))
            {
                ApplySelection(clickedContainer, target);
                return;
            }

            var item = isPinnedSection
                ? FindPinnedTreeViewItemForData(target)
                : FindRegularTreeViewItemForData(target);
            if (item is not null)
            {
                ApplySelection(item, target);
                return;
            }

            _viewModel.SelectedInfobase = target;
        }

        /// <summary>
        /// Кратковременная «конвергентная» стабилизация выделения после клика, которым закрыли
        /// контекстное меню (issue #340, седьмая попытка). Выбор применяется ШТАТНЫМ путём
        /// (повторная доставка клика по живому контейнеру), но после закрытия попапа идёт
        /// дополнительная переработка контейнеров (VirtualizingStackPanel, VirtualizationMode=
        /// Recycling): у переиспользуемых контейнеров IsSelected может «уехать», а двусторонней
        /// привязки IsSelected к модели нет (см. OnMainTree_SelectedItemChanged). Здесь
        /// одноразовая подписка на LayoutUpdated держится ДО СХОДИМОСТИ (до 10 срабатываний
        /// или ~1000 мс, F2): проверяет соответствие модели и контейнера и восстанавливает
        /// выбор по данным. Подписка дольше прежних 3 проходов/~200 мс, потому что отложенная
        /// переработка контейнеров (Recycling) после закрытия попапа может произойти позже.
        /// <para>
        /// Метод НИКОГДА не вызывает ClearBatchSelection/ToggleBatchSelection — не вмешивается
        /// в мультивыделение; вызывается только для безусловного левого клика без модификаторов.
        /// Защита от рекурсии: восстановление выполняется только при фактическом расхождении
        /// (SelectionMatchesTarget), счётчик проходов ограничивает работу. С новым кликом
        /// пользователя проверка ReferenceEquals(SelectedInfobase, target) прекращает работу.
        /// </para>
        /// </summary>
        /// <param name="target">База, выбранная кликом, которым закрыли меню.</param>
        /// <param name="isPinnedSection">Секция целевой строки: true — «Закреплённые» (issue #326).</param>
        /// <param name="reason">Причина запуска стабилизации для стартовой записи
        /// (issue #340, 0.3.9.311, B-5): "snapshot" — клик, закрывший меню, зафиксирован
        /// снимком (путь A/C или fallback); "recentMenuClose" — меню закрылось недавно
        /// без снимка (ESC/клик мимо строки), стабилизация по расширенному признаку.</param>
        private void EnsureSelectionStable(Infobase? target, bool isPinnedSection, string reason = "unknown")
        {
            if (MainTree is null || target is null || _viewModel is null)
                return;

            // B-5 (0.3.9.311): стартовая запись стабилизации — причина, целевая база,
            // выбранная база модели и реализация/подсветка контейнера на момент старта.
            // По последовательности EnsureStableStart → проходы → Dump500ms видно, какое
            // звено рвётся (выбор модели vs подсветка контейнера).
            var startContainer = isPinnedSection
                ? FindPinnedTreeViewItemForData(target)
                : FindRegularTreeViewItemForData(target);
            MenuCloseTrace.Log($"EnsureStableStart: target={target.Id}, reason={reason}, " +
                               $"SelectedInfobase={(_viewModel.SelectedInfobase?.Id ?? "null")}, " +
                               $"containerIsSelected={startContainer?.IsSelected}, " +
                               $"containerRealized={startContainer is not null}");

            var passes = 0;
            const int maxPasses = 15;   // F2: расширено с 10 (план 0.3.9.306, 2.4)
            var startTick = Environment.TickCount;
            const int timeoutMs = 1500; // F2: расширено с 1000 (план 0.3.9.306, 2.4)
            const int chaseDelayMs = 800; // F1: одноразовый «догоняющий» таймер

            // Диагностика (issue #340): актуальный SelectedItem дерева и время с начала
            // стабилизации — чтобы по логу видеть, «уезжал» ли SelectedItem к моменту
            // завершения подписки.
            string SelectedItemId() => UnwrapInfobase(MainTree?.SelectedItem)?.Id ?? "null";

            EventHandler onLayoutUpdated = null!;
            onLayoutUpdated = (_, _) =>
            {
                // issue #96: проход стабилизации обёрнут в try/catch (catch — в конце тела).
                // Исключение здесь раньше уходило в DispatcherUnhandledException и показывало
                // «Ошибка интерфейса»; теперь проход молча завершается с записью в журнал.
                try
                {
                    if (MainTree is null || _viewModel is null)
                    {
                        // Окно/дерево разобрано до сходимости стабилизации — просто отписываемся.
                        MainTree?.LayoutUpdated -= onLayoutUpdated;
                        return;
                    }

                passes++;
                var timeSinceStartMs = Environment.TickCount - startTick;
                if (passes > maxPasses || timeSinceStartMs >= timeoutMs)
                {
                    MainTree.LayoutUpdated -= onLayoutUpdated;
                    MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, done=true, " +
                                       $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    return;
                }

                // issue #340 (0.3.9.322): «перевыбрал ДРУГУЮ строку» — только когда выбор
                // ЕСТЬ и не совпадает с целью. При SelectedInfobase == null клик по целевой
                // строке был «проглочен» попапом меню (выбор не применён вовсе — трасса
                // 0.3.9.319: clickBeforeMenuClose, selectedItemId=null) — стабилизация НЕ
                // выходит, а восстанавливает цель ниже по данным (первый проход:
                // matches=false → SelectTreeRowByData применяет выбор).
                if (!BatchSelectionHelper.ShouldContinueRestore(_viewModel.SelectedInfobase, target))
                {
                    MainTree.LayoutUpdated -= onLayoutUpdated;
                    MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, userReselected=true, " +
                                       $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    return;
                }

                var matches = SelectionMatchesTarget(target, isPinnedSection);
                var containerRealized = isPinnedSection
                    ? FindPinnedTreeViewItemForData(target) is not null
                    : FindRegularTreeViewItemForData(target) is not null;
                // Восстановление только при фактическом расхождении (защита от рекурсии):
                // контейнер строки потерял IsSelected или SelectedItem дерева «уехал», либо
                // контейнер ещё не реализован виртуализацией (F2 — видимая-но-нереализованная
                // строка НЕ считается согласованной; повторный SelectTreeRowByData идемпотентен
                // и «догонит» выбор, когда контейнер появится).
                var restoreApplied = false;
                if (!matches)
                {
                    SelectTreeRowByData(target, null, isPinnedSection);
                    restoreApplied = true;
                }
                // B-5 (0.3.9.311): в каждый проход добавляются SelectedInfobase модели,
                // подсветка контейнера (containerIsSelected) и факт применения выбора
                // по данным (selectedByData) с результатом поиска контейнера (containerFound).
                var passContainer = isPinnedSection
                    ? FindPinnedTreeViewItemForData(target)
                    : FindRegularTreeViewItemForData(target);
                MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, matches={matches}, " +
                                   $"containerRealized={containerRealized}, action={(matches ? "skip" : "restored")}, " +
                                   $"selectedByData={restoreApplied}, containerFound={passContainer is not null}, " +
                                   $"selectedItemId={SelectedItemId()}, " +
                                   $"SelectedInfobase={(_viewModel.SelectedInfobase?.Id ?? "null")}, " +
                                   $"containerIsSelected={passContainer?.IsSelected}, timeSinceStartMs={timeSinceStartMs}");
                }
                catch (Exception passEx)
                {
                    // issue #96: сбой в проходе стабилизации не должен уходить в
                    // DispatcherUnhandledException («Ошибка интерфейса»): отписываемся,
                    // полный стек + контекст пишем через UiExceptionGuard; выделение
                    // доедет штатно (модель уже выбрана).
                    try { MainTree?.LayoutUpdated -= onLayoutUpdated; } catch { }
                    UiExceptionGuard.Report("EnsureSelectionStable.LayoutUpdated", passEx,
                        $"target={target.Id}, pass={passes}, pinned={isPinnedSection}");
                }
            };

            MainTree.LayoutUpdated += onLayoutUpdated;

            // F1 (план 0.3.9.306, 2.4): «догоняющая» стабилизация для нереализованного
            // контейнера. Если в момент старта контейнер целевой строки ещё не реализован
            // (виртуализация Recycling после закрытия попапа), подписка на LayoutUpdated
            // может закончиться раньше, чем контейнер появится, а WPF TreeView не
            // подсвечивает строку без контейнера (модель уже выбрана — SelectTreeRowByData
            // при отсутствии контейнера только ставит SelectedInfobase). Одноразовый
            // DispatcherTimer (~800 мс) ПОСЛЕ завершения подписки проверяет реализацию
            // контейнера и применяет выбор (ApplySelection), если подсветка так и не
            // встала. Идемпотентно; при перевыборе пользователем не вмешивается.
            var containerRealizedAtStart = isPinnedSection
                ? FindPinnedTreeViewItemForData(target) is not null
                : FindRegularTreeViewItemForData(target) is not null;
            if (BatchSelectionHelper.ShouldRetryRestoreForUnrealizedContainer(
                    containerRealizedAtStart, userReselected: false, elapsedMs: 0, maxChaseMs: chaseDelayMs))
            {
                var chaseTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(chaseDelayMs)
                };
                chaseTimer.Tick += (_, _) =>
                {
                    chaseTimer.Stop();
                    var timeSinceStartMs = Environment.TickCount - startTick;
                    if (MainTree is null || _viewModel is null || target is null)
                        return;
                    // Пользователь перевыбрал другую строку — не вмешиваемся
                    // (при ПУСТОМ выборе цель ещё не восстановлена — продолжаем, issue #340).
                    if (!BatchSelectionHelper.ShouldContinueRestore(_viewModel.SelectedInfobase, target))
                        return;

                    var item = isPinnedSection
                        ? FindPinnedTreeViewItemForData(target)
                        : FindRegularTreeViewItemForData(target);
                    if (item is null)
                    {
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=notRealized, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                        return;
                    }
                    if (!item.IsSelected)
                    {
                        ApplySelection(item, target);
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=applied, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    }
                    else
                    {
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=ok, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    }
                };
                chaseTimer.Start();
            }
        }

        /// <summary>
        /// Соответствует ли фактическое выделение дерева целевой базе (issue #340, F2):
        /// SelectedItem дерева разворачивается до той же базы И контейнер строки РЕАЛИЗОВАН
        /// и подсвечен. Видимая-но-нереализованная строка (контейнер ещё перерабатывается
        /// виртуализацией Recycling после закрытия попапа) согласованной НЕ считается —
        /// стабилизация восстановит выбор по данным (SelectTreeRowByData идемпотентен) и
        /// продолжит подписку до сходимости (см. <see cref="EnsureSelectionStable"/>).
        /// </summary>
        private bool SelectionMatchesTarget(Infobase target, bool isPinnedSection)
        {
            if (!ReferenceEquals(UnwrapInfobase(MainTree?.SelectedItem), target))
                return false;

            var item = isPinnedSection
                ? FindPinnedTreeViewItemForData(target)
                : FindRegularTreeViewItemForData(target);
            return item is not null && item.IsSelected;
        }

        /// <summary>
        /// Ищет контейнер базы в узле «Закреплённые» (issue #340): узел идентифицируется
        /// маркером <see cref="GroupNodeViewModel.PinnedMarker"/>, дальше рекурсивный поиск
        /// по данным (закреплённые строки несут обёртку <see cref="PinnedInfobaseItem"/>).
        /// </summary>
        private TreeViewItem? FindPinnedTreeViewItemForData(object data)
        {
            if (MainTree is null)
                return null;
            for (var i = 0; i < MainTree.Items.Count; i++)
            {
                if (MainTree.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is GroupNodeViewModel { Group: null } groupNode &&
                    string.Equals(groupNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                {
                    return FindTreeViewItemIn(tvi, data);
                }
            }
            return null;
        }

        /// <summary>
        /// Ищет контейнер базы в обычном списке (вне узла «Закреплённые», issue #340):
        /// дубль строки в «Закреплённых» пропускается, чтобы выбор не «перепрыгивал»
        /// на копию из закреплений.
        /// </summary>
        private TreeViewItem? FindRegularTreeViewItemForData(object data)
        {
            if (MainTree is null)
                return null;
            for (var i = 0; i < MainTree.Items.Count; i++)
            {
                if (MainTree.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is GroupNodeViewModel { Group: null } groupNode &&
                    string.Equals(groupNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                {
                    // Узел «Закреплённые» — дубль строк общего списка: пропускаем ветку.
                    continue;
                }
                var found = FindTreeViewItemIn(tvi, data);
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>Возвращает строку-родитель указанной строки дерева (WPF: ItemsControl,
        /// породивший контейнер, — TreeView для корневых строк либо родительский
        /// TreeViewItem для вложенных).</summary>
        private static TreeViewItem? ParentTreeViewItem(TreeViewItem item)
            => ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;

        /// <summary>
        /// Проверяет, что контейнер строки лежит в поддереве домашнего узла <paramref name="homeNode"/>
        /// (issue #31): путь от строки вверх должен дойти до <c>homeNode</c> ДО того, как встретится
        /// узел «Закреплённые» (GroupNodeViewModel с <see cref="GroupNodeViewModel.PinnedMarker"/>),
        /// иначе строка — копия из закреплений и для reveal недопустима.
        /// </summary>
        private static bool IsContainerWithinHomeTree(TreeViewItem item, GroupNodeViewModel homeNode)
        {
            for (var current = item; current is not null; current = ParentTreeViewItem(current))
            {
                if (current.DataContext is GroupNodeViewModel gn)
                {
                    if (GroupNodeViewModel.IsPinnedNode(gn))
                        return false;
                    if (ReferenceEquals(gn, homeNode))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Лежит ли строка в узле «Закреплённые» (сама строка или любой предок
        /// с <see cref="GroupNodeViewModel.PinnedMarker"/>). Используется как последний
        /// рубеж перед <see cref="ApplySelection"/> при revealToHome (issue #31).</summary>
        private static bool IsPinnedSectionContainer(TreeViewItem? item)
        {
            for (var current = item; current is not null; current = ParentTreeViewItem(current))
            {
                if (current.DataContext is GroupNodeViewModel gn && GroupNodeViewModel.IsPinnedNode(gn))
                    return true;
            }
            return false;
        }

        /// <summary>Развёрнутое описание цели reveal для журнала CM_REVEAL (issue #31):
        /// id базы (+признак «есть копия в „Закреплённых“») либо ключ/маркер/путь группы.</summary>
        private string DescribeRevealTarget(object? target) => target switch
        {
            Infobase ib => $"base={ib.Id}, pinned={IsInfobaseInPinnedSection(ib)}",
            GroupNodeViewModel gn => $"group={gn.NodeKey}, marker={gn.Marker ?? "-"}, path={gn.FullPath}",
            _ => target?.ToString() ?? "null"
        };

        /// <summary>Описание домашнего узла (leaf) для журнала CM_REVEAL.</summary>
        private static string DescribeLeaf(GroupNodeViewModel? leaf) => leaf is null
            ? "null"
            : $"node={leaf.NodeKey}, marker={leaf.Marker ?? "-"}, path={leaf.FullPath}";

        /// <summary>Присутствует ли копия базы в узле «Закреплённые» текущего дерева.</summary>
        private bool IsInfobaseInPinnedSection(Infobase ib)
        {
            // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
            // в плоском режиме секция «Закреплённые» не видна.
            foreach (var root in _viewModel.GroupNodes)
            {
                if (!root.IsVisible)
                    continue;
                if (FindPinnedNodeData(root) is { } pinned
                    && pinned.Infobases.Any(x => ReferenceEquals(x, ib)))
                {
                    return true;
                }
            }
            return false;

            static GroupNodeViewModel? FindPinnedNodeData(GroupNodeViewModel node)
            {
                if (GroupNodeViewModel.IsPinnedNode(node))
                    return node;
                foreach (var child in node.Children)
                {
                    var found = FindPinnedNodeData(child);
                    if (found is not null)
                        return found;
                }
                return null;
            }
        }
    }
}
#endif
