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

        /// <summary>
        /// Обработчик клика по заголовку колонки для смены сортировки.
        /// </summary>
        private void OnColumnHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string field)
                return;
            _viewModel.SetSortField(field);
            e.Handled = true;
        }

        /// <summary>
        /// Скрывает колонку списка баз по её ключу (пункт «Скрыть колонку»
        /// контекстного меню заголовка, issue #173). Ключ колонки лежит в Tag пункта меню.
        /// </summary>
        private void OnColumnHeaderContextMenu_Hide(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string key } && !string.IsNullOrEmpty(key))
                _viewModel?.SetColumnVisible(key, false);
        }

        /// <summary>
        /// Открывает настройки сразу на подвкладке «Колонки» (пункт контекстного меню
        /// заголовка, issue #173).
        /// </summary>
        private void OnColumnHeaderContextMenu_OpenSettings(object sender, RoutedEventArgs e)
        {
            OpenSettingsOnColumnsTab();
        }

        /// <summary>Открывает окно настроек сразу на подвкладке «Колонки» (issue #173).</summary>
        private void OpenSettingsOnColumnsTab()
        {
            var dialog = new SettingsWindow(_viewModel) { Owner = this };
            dialog.SelectColumnsTab();
            dialog.ShowDialog();
        }

        /// <summary>
        /// Ограничение «системного меню» для режима «Пользователь» (Этап 10 StartManager).
        /// При открытии контекстного меню базы скрывает все системные пункты, кроме
        /// помеченных <c>Tag="User"</c> (запуск Предприятия/Конфигуратора, избранное, закрепление).
        /// В остальных режимах («Специалист»/«Разработчик») меню показывается полностью.
        /// </summary>
        private void OnBaseContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // issue #96: правый клик по строке входит в сценарий «Ошибка интерфейса» —
            // построение меню по выделению защищено guard'ом (исключение не роняет UI,
            // полный стек + контекст — в журнал, пользователю показываются детали).
            UiExceptionGuard.Guard(
                nameof(OnBaseContextMenu_Opened),
                () => OnBaseContextMenu_OpenedCore(sender, e));
        }

        private void OnBaseContextMenu_OpenedCore(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu || _viewModel == null)
                return;

            // Контекстное меню построено кодом (issue #56): при каждом открытии перечитываем
            // резолвер компоновки (как меню «Утилиты») — если компоновка/настройка правки
            // изменились с последней сборки, содержимое обновляется в месте, до batch- и
            // custom-логики ниже. Issue #105: меню также перестраивается при смене целевого
            // контекста (группа ↔ база) — для группы показываются только применимые пункты.
            var contextStore = AppServices.TryGetService<IContextMenuLayoutStore>();
            var contextSettings = new AppSettings
            {
                ContextMenuEditingEnabled = _viewModel.ContextMenuEditingEnabled
            };
            var contextLayout = ContextMenuResolver.PrepareForMenu(ContextMenuResolver.Resolve(contextSettings, contextStore));
            if (!string.Equals(_rowContextMenuLayoutKey, LayoutFingerprint(contextLayout), StringComparison.Ordinal) ||
                _rowContextMenuBuiltForGroup != IsRowMenuGroupContext())
                PopulateRowContextMenu(menu.Items);

            var restricted = _viewModel.IsSystemMenuRestricted;
            var items = menu.Items.Cast<object>().ToList();

            // Мультивыделение (0.3.9.90): блок «Для выделенных (N)…» — заголовок
            // показывает текущее число целевых баз. Счёт цели включает и одну
            // выделенную базу без Ctrl (issue #53): пункт виден при N >= 1.
            // В режиме «Пользователь» пакетные операции — системные, скрываются
            // общим правилом ниже (пункт помечен Tag="Batch", не "User").
            var batchMenu = items.OfType<MenuItem>()
                .FirstOrDefault(m => string.Equals(m.Tag as string, "Batch", StringComparison.Ordinal));
            if (batchMenu is not null)
            {
                // Issue #85 (v1.0.1.32): при выделенном узле группы (без мультивыделения)
                // цели — базы группы (включая вложенные), заголовок — «Для группы (N)…».
                var batchCount = _viewModel.BatchOperationTargetCountIncludingGroup;
                var batchTitleKey = _viewModel.IsGroupBatchTarget
                    ? "Main.BatchForGroup"
                    : "Main.BatchForSelected";
                batchMenu.Header = string.Format(
                    LocalizationManager.T(batchTitleKey), batchCount);
                batchMenu.Visibility = batchCount >= 1 && !restricted
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            foreach (var item in items.OfType<MenuItem>())
            {
                var allowed = string.Equals(item.Tag as string, "User", StringComparison.Ordinal);
                item.Visibility = restricted && !allowed ? Visibility.Collapsed : Visibility.Visible;
            }
            // Пакетный блок уже получил свою видимость; возвращаем её после общего
            // прохода (в не-restricted режиме общий проход делает все пункты видимыми).
            if (batchMenu is not null && !restricted)
            {
                batchMenu.Visibility = _viewModel.BatchOperationTargetCountIncludingGroup >= 1
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            // Пользовательские действия (0.3.9.197): наполняем подменю в конце — после
            // batch-логики и общего прохода видимости. Финальную видимость подменю и его
            // разделителя метод устанавливает явно (в т.ч. в restricted-режиме, где общий
            // проход оставляет Tag="User" видимым).
            PopulateCustomActionsMenu(menu);

            // Чистка «висячих» разделителей (issue #105, доработка 7OH): разделитель
            // показывается только между двумя видимыми пунктами — в начале/в конце меню,
            // рядом со скрытыми (batch-блок, пользовательские действия) и подряд идущие
            // скрываются/схлопываются. Выполняется ПОСЛЕ PopulateCustomActionsMenu —
            // видимость всех пунктов уже финальная.
            CollapseDanglingSeparators(items);
        }

        /// <summary>
        /// Скрывает «висячие» и подряд идущие разделители меню (issue #105, доработка 7OH):
        /// разделитель отображается, только если сверху и снизу от него есть видимый пункт
        /// (сквозь любые скрытые); из нескольких показанных подряд остаётся первый.
        /// <paramref name="items"/> — снимок пунктов корневого уровня меню (не рекурсивно).
        /// </summary>
        private static void CollapseDanglingSeparators(List<object> items)
        {
            var count = items.Count;
            if (count == 0)
                return;

            var visibleItem = new bool[count];
            for (var i = 0; i < count; i++)
                visibleItem[i] = items[i] is MenuItem m && m.Visibility == Visibility.Visible;

            var show = new bool[count];
            // Шаг 1: у разделителя есть видимый пункт сверху.
            var seenVisible = false;
            for (var i = 0; i < count; i++)
            {
                if (items[i] is Separator)
                    show[i] = seenVisible;
                else if (visibleItem[i])
                    seenVisible = true;
            }
            // Шаг 2: у разделителя есть видимый пункт снизу.
            seenVisible = false;
            for (var i = count - 1; i >= 0; i--)
            {
                if (items[i] is Separator)
                    show[i] &= seenVisible;
                else if (visibleItem[i])
                    seenVisible = true;
            }
            // Шаг 3: из нескольких показанных подряд (без видимого пункта между) — первый.
            var lastShownIndex = -1;
            for (var i = 0; i < count; i++)
            {
                if (items[i] is not Separator)
                {
                    if (visibleItem[i])
                        lastShownIndex = -1; // между разделителями появился видимый пункт
                    continue;
                }
                if (!show[i])
                    continue;
                if (lastShownIndex >= 0)
                    show[i] = false; // подряд — скрываем второй и далее
                else
                    lastShownIndex = i;
            }
            // Применяем видимость.
            for (var i = 0; i < count; i++)
            {
                if (items[i] is Separator sep)
                    sep.Visibility = show[i] ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Строит целевую последовательность колонок (логические ключи) по выбранному
        /// пользователем порядку. Первая итерация идёт по пользовательскому порядку
        /// (<see cref="_viewModel.ColumnOrderKeys"/>), отбрасывая незнакомые ключи, — поэтому
        /// фактически применяется порядок, заданный пользователем в настройках, в т.ч. перенос
        /// колонки «Действия». Второй проход лишь дополняет недостающие известные колонки в
        /// порядке по умолчанию, гарантируя их наличие.
        /// </summary>
        private List<string> BuildColumnLayout()
        {
            var known = new[] { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Modified", "LastBackup", "Configuration", "ConfigurationVersion" };
            var keys = new List<string>();
            // Идём по ПОЛЬЗОВАТЕЛЬСКОМУ порядку, отбрасывая незнакомые ключи,
            // чтобы фактически применять выбранный порядок (в т.ч. перенос «Действий»).
            var source = _viewModel?.ColumnOrderKeys ?? Array.Empty<string>();
            foreach (var k in source)
                if (Array.IndexOf(known, k) >= 0 && !keys.Contains(k))
                    keys.Add(k);
            // Гарантируем, что все известные колонки присутствуют (незнакомые ключи
            // из сохранённого порядка пропускаются).
            foreach (var k in known)
                if (!keys.Contains(k))
                    keys.Add(k);

            return keys;
        }

        /// <summary>
        /// Перестраивает колонки данных сетки <paramref name="grid"/> (заголовка, строки базы
        /// или заголовка группы) под выбранный порядок: передвигает определения колонок и
        /// обновляет позиции размещённых в них элементов. Фиксированные колонки слева и
        /// «Название» не трогаются.
        /// </summary>
        private void ReorderGridColumns(Grid grid, int firstDataCol)
        {
            if (grid is null || _viewModel is null)
                return;

            var layout = BuildColumnLayout();
            var leading = firstDataCol;
            var dataCount = grid.ColumnDefinitions.Count - leading;
            if (dataCount <= 0)
                return;

            // Первый проход: определяем логический ключ для каждого перемещаемого элемента
            // региона данных по статической раскладке. Ключ сохраняется в attached-свойстве
            // ColumnKey (Tag занят — сортировка/двойной клик), поэтому повторные вызовы
            // корректно работают и после перестановок, и для уже перестроенных сеток.
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue; // объединённые ячейки (название/теги) двигаются отдельно
                var c = Grid.GetColumn(fe);
                if (c < leading || c >= leading + dataCount)
                    continue;
                if (string.IsNullOrEmpty(GetColumnKey(fe)))
                    SetColumnKey(fe, StaticDataColumnKeys[c - leading]);
            }

            // Сопоставление «позиция колонки данных -> логический ключ» по ключам детей.
            var defKey = new string[dataCount];
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (string.IsNullOrEmpty(s))
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue;
                var c = Grid.GetColumn(fe);
                if (c >= leading && c < leading + dataCount)
                    defKey[c - leading] = s;
            }

            // Для сеток без дочерних элементов в части колонок данных (например, строка группы,
            // где заполнена только колонка «Действия») ключ незаполненных колонок берём по
            // статической раскладке: такие сетки всегда создаются в статическом порядке.
            for (var i = 0; i < dataCount; i++)
                if (string.IsNullOrEmpty(defKey[i]))
                    defKey[i] = StaticDataColumnKeys[i];

            // Новый порядок определений колонок под нужную раскладку.
            var defs = grid.ColumnDefinitions;
            var newOrder = new List<ColumnDefinition>(dataCount);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in layout)
            {
                for (var i = 0; i < dataCount; i++)
                {
                    if (defKey[i] == token && placed.Add(token))
                    {
                        newOrder.Add(defs[leading + i]);
                        break;
                    }
                }
            }
            for (var i = 0; i < dataCount; i++)
                if (!placed.Contains(defKey[i]))
                {
                    newOrder.Add(defs[leading + i]);
                    placed.Add(defKey[i]);
                }

            // Ранний выход (issue #255): если фактический порядок определений колонок данных
            // уже совпадает с целевым, ничего не пересобираем. Строки реализуются виртуализацией
            // на каждую прокрутку/материализацию, и до правки здесь каждый раз выполнялся
            // defs.Clear() с повторным добавлением всех ColumnDefinition — сброс внутреннего кэша
            // колонок сетки и инвалидация measure/arrange даже когда переставлять нечего.
            var orderUnchanged = true;
            for (var i = 0; i < dataCount; i++)
                if (!ReferenceEquals(defs[leading + i], newOrder[i]))
                {
                    orderUnchanged = false;
                    break;
                }
            if (orderUnchanged)
                return;

            // Пересобираем коллекцию определений: фиксированные слева + новый порядок данных.
            var leadingDefs = new List<ColumnDefinition>(leading);
            for (var i = 0; i < leading; i++)
                leadingDefs.Add(defs[i]);
            defs.Clear();
            foreach (var d in leadingDefs)
                defs.Add(d);
            foreach (var d in newOrder)
                defs.Add(d);

            // Обновляем позиции детей и span широких ячеек (до «Действий»).
            // Заголовок группы: имя/счётчик занимают область названия и тянутся до «Действий».
            var actionsColumn = leading + layout.IndexOf("Actions");
            var isGroup = ReferenceEquals(grid.Tag, GroupGridMarker);
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (!string.IsNullOrEmpty(s))
                {
                    var ti = layout.IndexOf(s);
                    if (ti >= 0)
                        Grid.SetColumn(fe, leading + ti);
                }
                else if (isGroup && Grid.GetRow(fe) == 0 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
                else if (Grid.GetRow(fe) == 1 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
            }
        }

        /// <summary>Рекурсивно собирает уже созданные сетки строк баз/заголовков групп по маркеру.</summary>
        private static List<Grid> FindRowGrids(DependencyObject? root, object marker)
        {
            var result = new List<Grid>();
            FindRowGridsCore(root, marker, result);
            return result;
        }

        private static void FindRowGridsCore(DependencyObject? parent, object marker, List<Grid> acc)
        {
            if (parent is null)
                return;
            if (parent is Grid g && ReferenceEquals(g.Tag, marker))
                acc.Add(g);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                FindRowGridsCore(VisualTreeHelper.GetChild(parent, i), marker, acc);
        }

        /// <summary>
        /// Находит первую сетку с указанным маркером внутри <paramref name="root"/>
        /// (используется для выравнивания заголовка по строке базы).
        /// </summary>
        private static Grid? FindGridByMarker(DependencyObject? root, object marker)
        {
            if (root is Grid g && ReferenceEquals(g.Tag, marker))
                return g;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindGridByMarker(VisualTreeHelper.GetChild(root, i), marker);
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Применяет выбранный порядок колонок к заголовку и всем созданным строкам баз
        /// и заголовкам групп. Вызывается при старте (после компоновки) и при изменении
        /// порядка в настройках.
        /// </summary>
        private void ApplyColumnOrder()
        {
            // issue #255: изменение порядка колонок сбрасывает флаг «порядок применён» на всех
            // найденных сетках, после чего существующие строки перестраиваются один раз (повторно
            // применять в On*RowGrid_Loaded не придётся, т.к. флаг снова выставляется здесь же).
            if (HeaderGrid is not null)
            {
                SetColumnOrderApplied(HeaderGrid, false);
                ReorderGridColumns(HeaderGrid, HeaderFirstDataColumn);
                SetColumnOrderApplied(HeaderGrid, true);
            }
            foreach (var grid in FindRowGrids(MainTree, RowGridMarker))
            {
                SetColumnOrderApplied(grid, false);
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }
            foreach (var grid in FindRowGrids(MainTree, GroupGridMarker))
            {
                SetColumnOrderApplied(grid, false);
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }

            // Порядок колонок влияет на состав суммы минимальной ширины (перенос «Действий»,
            // смена мест): после перестановки определений пересчитываем ширину заголовка и
            // минимум контента, иначе полоса оставалась бы по прежнему порядку и могла не
            // дотягивать до последней (новой) колонки (issue #309). Замер фактической
            // ширины строк обновляется вместе с суммой — строки уже перестроены под
            // новый порядок колонок.
            // Изменился состав колонок — кэш замера контента (issue #21, Этап B1) должен
            // выполнить ПОЛНЫЙ пересчёт, а не вернуться по совпадению верхней строки.
            InvalidateTreeMinWidthContent();
            UpdateTreeMinWidthContent();
            SyncHeaderWidthWithList();

            // Fallback-минимум (issue #309, девятая попытка): после применения настроек
            // колонок/порядка список принудительно прокручивается до конца горизонтальной
            // полосы — последние колонки гарантированно достижимы и видны, даже если
            // расчётная минимальная ширина оказалась меньше фактической ширины контента.
            EnsureHorizontalReach();
        }

        /// <summary>
        /// Fallback-минимум (issue #309): после применения настроек колонок/порядка
        /// проверяет достижимость последней колонки. Ширина контента равна сумме видимых
        /// колонок (см. <see cref="UpdateTreeMinWidth"/>), поэтому последняя колонка
        /// достижима по определению — безусловная докрутка в самый конец больше НЕ
        /// выполняется, иначе позиция «прыгала» бы вправо при старте и смене набора
        /// колонок (регресс issue #309). Докручиваем только аварийный случай: фактический
        /// extent всё ещё меньше расчётной суммы колонок (колонки без докрутки недостижимы).
        /// Выполняется отложенно — после завершения раскладки строк под новый порядок колонок.
        /// </summary>
        private void EnsureHorizontalReach()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Горизонтальную прокрутку ведёт внешний общий ScrollViewer (issue #309) —
                    // докручиваем именно его, а не внутреннюю прокрутку дерева.
                    var listScroll = DbListScroll;
                    if (listScroll is null || listScroll.ScrollableWidth <= 1)
                        return;
                    if (listScroll.ExtentWidth < _treeMinWidthTotal - 0.5)
                        listScroll.ScrollToHorizontalOffset(listScroll.ScrollableWidth);
                }
                catch
                {
                    // Fallback не должен ломать раскладку.
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Обработчик Loaded сетки строки базы в шаблоне: применяет выбранный порядок
        /// колонок к каждой вновь созданной строке (включая строки, появляющиеся при
        /// виртуализации/прокрутке дерева) и пересчитывает выравнивание заголовка.
        /// Пересчёт здесь обязателен: строки реализуются виртуализацией в проходе
        /// разметки ПОСЛЕ события Loaded дерева и пересборки (поиск, крестик поиска,
        /// сохранение свойств базы), поэтому пересчёт на ApplicationIdle, стартовавший
        /// сразу после пересборки, может выполниться до появления первой строки и
        /// оставить компенсатор заголовка в устаревшем значении (issue #214) — так же,
        /// как в Linux/Avalonia выравнивание пересчитывается на событии подготовки
        /// контейнера строки.
        /// </summary>
        private void OnInfobaseRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Диагностика переключения вкладок (issue #21, CM_TREESWITCH, v2): строка базы
            // реализована/подключена к визуальному дереву — счётчик realized (аналог
            // Avalonia-события ContainerPrepared). В Recycling-виртуализации тот же экземпляр
            // сетки может «загрузиться» заново под новую строку — каждая реализация считается.
            _treeRowRealizeCount++;
            // issue #96: подписки при реализации строки (Recycling) защищены guard'ом —
            // исключение на полуразобранном DataContext не должно ронять UI.
            UiExceptionGuard.Guard(nameof(OnInfobaseRowGrid_Loaded), () =>
            {
                if (sender is not Grid grid)
                    return;
            // issue #255: при Recycling-виртуализации экземпляр сетки переиспользуется под новые
            // строки, поэтому порядок колонок, однажды применённый, сохраняется до сброса флага
            // в ApplyColumnOrder. Пропускаем ReorderGridColumns целиком для уже приведённых сеток,
            // чтобы на каждую материализацию строки не строились layout/массивы/List/HashSet и не
            // выполнялся обход детей (главный источник CPU и мусора gen2 при прокрутке списка).
            if (!GetColumnOrderApplied(grid))
            {
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }
            grid.Tag = RowGridMarker;
            ApplyRowCompact(grid);
            // Проба утечки строк (issue #21, 1.0.0.62): при включённом CM_TREESWITCH
            // запоминаем строку слабой ссылкой; после GC в логе — сколько строк живо.
            if (TraceFlags.IsEnabled(TraceFlags.TreeSwitchFlag))
                TrackRowForLeakProbe(grid);
            // Намеренно НЕ вызываем QueueHeaderAlign здесь: обработчик Loaded срабатывает на
            // каждую реализацию строки при прокрутке/виртуализации, и перезапуск цикла
            // стабилизации на каждую строку держал компенсатор заголовка «в работе» всё время
            // скролла и усиливал рывки (issue #255). Стабилизатор HeaderAlignStabilizeStep
            // сам повторяется, пока первая строка ещё не появилась (!hadRows), поэтому
            // выравнивание после пересборки восстанавливается и без перезапуска здесь.
            });
        }

        /// <summary>
        /// Обработчик Loaded сетки заголовка группы в шаблоне: применяет выбранный порядок
        /// колонок, чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        /// </summary>
        private void OnGroupRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Диагностика переключения вкладок (issue #21, CM_TREESWITCH, v2): счётчик
            // реализованных строк — см. OnInfobaseRowGrid_Loaded.
            _treeRowRealizeCount++;
            // issue #96: та же защита, что и у строки базы (см. OnInfobaseRowGrid_Loaded).
            UiExceptionGuard.Guard(nameof(OnGroupRowGrid_Loaded), () =>
            {
                if (sender is not Grid grid)
                    return;
                // То же, что в OnInfobaseRowGrid_Loaded (issue #255): пропускаем пересборку колонок
                // для сеток, к которым порядок уже применён (Recycling переиспользует экземпляр).
                if (!GetColumnOrderApplied(grid))
                {
                    ReorderGridColumns(grid, RowFirstDataColumn);
                    SetColumnOrderApplied(grid, true);
                }
                grid.Tag = GroupGridMarker;
                ApplyRowCompact(grid);
            });
        }

        /// <summary>
        /// Диагностика переключения вкладок (issue #21, CM_TREESWITCH, v2): строка базы
        /// отсоединена от визуального дерева (виртуализация/пересборка/рециклинг) —
        /// счётчик cleared (аналог Avalonia-события ContainerClearing). Совместно с
        /// <see cref="OnInfobaseRowGrid_Loaded"/> даёт пару realized/cleared за смену:
        /// устойчивый разрыв (realized ≫ cleared между сменами) указывает на удержание
        /// контейнеров (Recycling-пул ItemContainerGenerator или подписки).
        /// </summary>
        private void OnInfobaseRowGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            _treeRowUnrealizeCount++;
        }

        /// <summary>
        /// Диагностика переключения вкладок (issue #21, CM_TREESWITCH, v2): отсоединение
        /// строки заголовка группы — тот же счётчик cleared, см. <see cref="OnInfobaseRowGrid_Unloaded"/>.
        /// </summary>
        private void OnGroupRowGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            _treeRowUnrealizeCount++;
        }

        /// <summary>
        /// Приводит вновь созданную строку (базы или группы) к текущей плотности. Строки
        /// дерева появляются позже применения режима к окну — при фоновой инициализации,
        /// виртуализации, прокрутке и пересборке дерева (поиск, сохранение свойств базы) —
        /// и без этого вызова оставались бы прежней плотности, «разъезжаясь» с заголовком
        /// (issue #214). Метод и сжимает, и разжимает: строку могло сжать, пока режим был
        /// включён, а потом её отсоединила виртуализация, и вернуться она могла уже после
        /// выключения. Повторные вызовы для той же строки безопасны: масштабирование идёт
        /// от сохранённых исходных значений, а не от текущих.
        /// </summary>
        private void ApplyRowCompact(Grid grid)
        {
            if (_viewModel is null)
                return;
            // В обычном режиме, пока ничего не сжималось, возвращать нечего — не ходим
            // по строке вовсе, чтобы не платить обходом на каждой прокрутке списка.
            if (!_viewModel.CompactMode && !ThemeManager.HasCompactMetrics)
                return;
            // Проходим по строке и в обычном режиме тоже. Строку могло сжать, пока режим был
            // включён, а потом её отсоединила виртуализация или пересборка дерева: обход окна
            // при выключении компактности такую строку не видит, и, вернувшись в видимую
            // область, она приходила сжатой (issue #214).
            // Сжимать надо строку целиком, от корня её шаблона, а не только внутреннюю
            // сетку: вертикальный отступ строки базы (Border Margin="0,1" в MainWindow.xaml)
            // задан на элементе, который сетке предок. Глобальный проход ApplyCompact его
            // сжимает, а обход от сетки не доставал, и строки, созданные позже (запуск с уже
            // включённым компактным режимом, пересборка дерева поиском), отличались
            // от строк после переключения тумблера (issue #214).
            ThemeManager.ApplyCompactTree(RowTemplateRoot(grid), _viewModel.CompactMode);
        }

        /// <summary>
        /// Возвращает корень шаблона строки: поднимается от сетки строки до элемента,
        /// который лежит непосредственно в <see cref="ContentPresenter"/> заголовка узла.
        /// Выше подниматься нельзя: там начинается шаблон контейнера с кнопкой разворота
        /// и <c>ItemsPresenter</c> дочерних строк, и обход захватывал бы соседние строки.
        /// Остановка на <see cref="TreeViewItem"/> — только страховка от бесконечного
        /// подъёма, если шаблон контейнера когда-нибудь останется без
        /// <see cref="ContentPresenter"/>; в нынешней разметке она недостижима.
        ///
        /// Метрики самого шаблона контейнера (кнопка разворота, обёртки строки) достаются
        /// только обходу всего окна. Сегодня это безразлично: отступы там либо нулевые,
        /// либо привязанные. Если в шаблон контейнера добавят ненулевой отступ константой,
        /// расхождение между путями вернётся, и его придётся учесть здесь.
        /// </summary>
        private static DependencyObject RowTemplateRoot(Grid grid)
        {
            DependencyObject current = grid;
            while (true)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent is null or ContentPresenter or TreeViewItem)
                    return current;
                current = parent;
            }
        }

        /// <summary>
        /// Обработчик раскрытия/сворачивания узла дерева (issue #119). Раскрытие/сворачивание
        /// группы меняет глубину первой видимой базы, поэтому компенсатор сдвига заголовка
        /// (HeaderOffsetColumn) устаревает и колонки «уезжают» относительно содержимого.
        /// Пересчитывает выравнивание заголовка с данными после завершения компоновки.
        /// </summary>
        private void OnMainTree_GroupExpansionChanged(object sender, RoutedEventArgs e)
        {
            QueueHeaderAlign();
        }

        // Признак того, что в очереди диспетчера уже стоит пересчёт выравнивания заголовка.
        // Нужен, чтобы многие события за короткое время (материализация/рециклинг десятков
        // строк дерева при пересборке, прокрутке и поиске) склеивались в один пересчёт за
        // проход — как в Linux/Avalonia (QueueHeaderAlign).
        private bool _headerAlignQueued;
        // Счётчик итераций стабилизации текущего пересчёта (защита от бесконечного цикла).
        private int _headerAlignStabilizeCount;
        // Максимум итераций стабилизации, пока компенсатор не перестанет меняться.
        private const int HeaderAlignMaxStabilize = 8;
        // Однопроходный режим стабилизации (issue #21, Этап C): ставится при чистом
        // переключении вкладки списка — цикл «до сходимости» (до 8 итераций) на каждое
        // переключение давал «паузу» и лишние записи CM_COLUMNS. Финальное положение
        // добирает MainTree.SizeChanged (AlignHeaderToData) после материализации строк.
        private bool _headerAlignSinglePass;

        /// <summary>
        /// Ставит пересчёт выравнивания заголовка (<see cref="AlignHeaderToData"/>) в очередь
        /// диспетчера на приоритет ApplicationIdle и зацикливает его до стабилизации значения
        /// колонки-компенсатора (см. <see cref="HeaderAlignStabilizeStep"/>). Повторные вызовы
        /// до выполнения объединяются в один пересчёт, чтобы частые события (появление каждой
        /// строки дерева, изменение размера списка) не вызывали лишние полные пересчёты.
        /// Выполнение на ApplicationIdle гарантирует, что раскладка уже завершена и
        /// виртуализированные контейнеры строк материализованы, — в отличие от Loaded, на
        /// котором они достраиваются уже после (issue #214).
        /// </summary>
        private void QueueHeaderAlign()
        {
            if (_headerAlignQueued)
                return;
            _headerAlignQueued = true;
            _headerAlignStabilizeCount = 0;
            Dispatcher.BeginInvoke(new Action(HeaderAlignStabilizeStep),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Один шаг стабилизации выравнивания заголовка: выполняет <see cref="AlignHeaderToData"/>
        /// и, пока значение компенсатора продолжает меняться либо строки ещё не материализованы
        /// (виртуализация достраивает их в проходе разметки после события), повторяет проверку
        /// на ApplicationIdle. Так фиксируется итоговое положение, а не промежуточное, по которому
        /// иконки заголовка «разъезжаются» относительно строк и требуют повторного переключения
        /// тумблера (issue #214). Число итераций ограничено как защита от бесконечного цикла.
        /// </summary>
        private void HeaderAlignStabilizeStep()
        {
            _headerAlignQueued = false;
            // Счётчик итераций стабилизации — диагностика переключения вкладок
            // (issue #21, Этап E; CM_TREESWITCH).
            _headerAlignStabilizeTotal++;
            // Однопроходный режим (issue #21, Этап C): при чистом переключении вкладки
            // цикл не крутится до 8 итераций — финальное выравнивание добирает
            // MainTree.SizeChanged (AlignHeaderToData) после материализации строк.
            var singlePass = _headerAlignSinglePass;
            _headerAlignSinglePass = false;
            var before = HeaderOffsetColumn?.Width.Value ?? 0;
            var hadRows = FindFirstInfobaseItem(MainTree) is not null;
            AlignHeaderToData();
            var after = HeaderOffsetColumn?.Width.Value ?? 0;

            var changed = Math.Abs(after - before) > 0.5;
            if ((changed || !hadRows) && !singlePass && _headerAlignStabilizeCount++ < HeaderAlignMaxStabilize)
            {
                _headerAlignQueued = true;
                Dispatcher.BeginInvoke(new Action(HeaderAlignStabilizeStep),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы первая колонка данных заголовка точно совпадала по горизонтали с первой
        /// колонкой данных строки базы. Строка базы и заголовок имеют одинаковый набор
        /// ведущих колонок, поэтому колонки данных всех строк (которые не смещаются
        /// отступами вложенности) оказываются на одной линии с заголовками.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderGrid is null || HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var rowGrid = FindGridByMarker(item, RowGridMarker);
            if (rowGrid is null)
                return;

            // Фактическая ширина строк уже известна (первая строка материализована):
            // обновляем кэш минимума контента до синхронизации ширины заголовка, чтобы
            // полоса дотягивала до конца длинных названий, а не только до суммы колонок
            // (issue #309). Редкий путь — стабилизация выравнивания на ApplicationIdle.
            UpdateTreeMinWidthContent(rowGrid);

            // Позиция первой колонки данных строки базы (отсчитывается от левого края сетки).
            double rowStart = 0;
            for (var i = 0; i < RowFirstDataColumn; i++)
                rowStart += rowGrid.ColumnDefinitions[i].ActualWidth;
            var rowOrigin = rowGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            // Позиция первой колонки данных заголовка без учёта текущей ширины компенсатора Offset.
            double headerStart = 0;
            for (var i = 0; i < HeaderFirstDataColumn; i++)
            {
                if (!ReferenceEquals(HeaderGrid.ColumnDefinitions[i], HeaderOffsetColumn))
                    headerStart += HeaderGrid.ColumnDefinitions[i].ActualWidth;
            }
            var headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            var offset = Math.Max(0, (rowOrigin + rowStart) - (headerOrigin + headerStart));
            if (Math.Abs(offset - HeaderOffsetColumn.Width.Value) > 0.5)
            {
                // Компенсатор управляется только этим методом — исключаем его из компактизации,
                // чтобы повторное применение компакт-режима не масштабировало уже выставленную
                // ширину и не «разъезжало» строки по горизонтали (issue #214).
                Themes.ThemeManager.ForgetCompactWidth(HeaderOffsetColumn);
                HeaderOffsetColumn.Width = new GridLength(offset);
            }

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Обновляет минимальную ширину контента списка (issue #309). Заголовок колонок
        /// лежит в том же контейнере, что и дерево (ListContentGrid внутри внешнего общего
        /// ScrollViewer), поэтому его ширина автоматически равна ширине строк — отдельная
        /// синхронизация ширины заголовка больше не нужна. Пересчёт минимума держится здесь,
        /// потому что метод вызывается из всех точек, где менялись колонки или раскладка.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            // Жёсткий минимум области списка = сумма ширин всех колонок (как в Linux/Avalonia).
            // Благодаря точной границе горизонтальная полоса появляется только когда
            // колонки реально не помещаются, а не «на волосок» раньше (анти-регресс #255).
            UpdateTreeMinWidth();
        }

        /// <summary>
        /// Ищет первый реально созданный (видимый) элемент дерева с базой.
        /// </summary>
        private static TreeViewItem? FindFirstInfobaseItem(DependencyObject parent)
        {
            // Строка узла «Закреплённые» несёт обёртку PinnedInfobaseItem
            // (уникальные данные, issue #314) — распознаём и её как строку базы.
            if (parent is TreeViewItem tvi && UnwrapInfobase(tvi.DataContext) is not null)
                return tvi;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindFirstInfobaseItem(VisualTreeHelper.GetChild(parent, i));
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Находит текстовый элемент названия базы (x:Name=NameText) в строке.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && tb.Name == "NameText")
                    return tb;

                var result = FindNameCell(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Применяет сохранённые ширины колонок списка баз.
        /// Ширины уже загружены в модель (VersionColumnWidth и т.д.), а колонки заголовка
        /// и строки данных привязаны к ним через ColumnVisibilityConverter, поэтому ручная
        /// установка Width не требуется и лишь перебивала бы binding, рассинхронизируя
        /// заголовок с данными.
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            // Колонка «Название» — гибкая (*), фиксированную ширину не задаём.
            // Остальные колонки применяют сохранённые ширины автоматически через binding.
        }

        /// <summary>
        /// Минимальная ширина гибкой колонки «Название» (*): на неё нельзя схлопываться,
        /// чтобы сумма колонок оставалась осмысленной при расчёте минимальной ширины списка.
        /// </summary>
        private const double NameColumnMinWidth = 220;

        /// <summary>
        /// Кэш фактической желаемой ширины строк дерева (issue #309). Сумма ширин колонок —
        /// нижний предел минимума контента, но реальная строка может быть шире: блок имени
        /// (горизонтальный StackPanel, ColumnSpan=5 в MainWindow.xaml) меряет NameText без
        /// ограничения ширины, и длинное название делает строку фактически шире суммы колонок.
        /// Внутренний ScrollViewer дерева (CanContentScroll) меряет панель вьюпортной шириной,
        /// поэтому ExtentWidth сам не растёт под контент, и минимум приходится досчитывать
        /// замером первых строк с бесконечной шириной. Обновляется только в редких точках
        /// (стабилизация выравнивания, применение ширин, смена верхней строки при прокрутке),
        /// чтобы не нагружать горячий путь <see cref="UpdateTreeMinWidth"/> (анти-регресс #255).
        /// Ноль означает «строки ещё нет или контент уже покрывается суммой колонок».
        /// </summary>
        private double _treeMinWidthContent;

        /// <summary>
        /// Флаг очереди пересчёта минимальной ширины списка (дебаунс, issue #309):
        /// ручное перетаскивание разделителя колонок генерирует MouseMove десятки раз
        /// в секунду, и полный пересчёт на каждое движение нагружал бы раскладку.
        /// Один отложенный вызов на проход диспетчера собирает все движения кадра.
        /// </summary>
        private bool _treeMinWidthQueued;

        /// <summary>
        /// Последняя установленная минимальная ширина контента списка (issue #309).
        /// MinWidth контента внешнего ScrollViewer присваивается только при фактическом
        /// изменении значения, а не на каждый ScrollChanged (анти-регресс #343).
        /// </summary>
        private double _lastTreeMinWidth = -1;

        /// <summary>
        /// Отложенная запись MinWidth/MaxWidth контента (issue #21): во время перестроения
        /// дерева/смены вкладки строки ещё не материализованы, и effective, посчитанный по
        /// промежуточным headerSumNow/viewportNow, «залипал» бы (горизонтальная полоса
        /// появлялась при переключении на «Недавние»). Пока флаг истинен,
        /// <see cref="UpdateTreeMinWidth"/> только кэширует сумму колонок; финальный проход
        /// <see cref="TreeMinWidthFinalizeStep"/> фиксирует значение по стабильному кадру.
        /// </summary>
        private bool _treeMinWidthDeferred;

        /// <summary>Очередь финального прохода сходимости ширины после смены вкладки (issue #21).</summary>
        private bool _treeMinWidthFinalizeQueued;

        /// <summary>Счётчик итераций финального прохода (защита от бесконечного цикла).</summary>
        private int _treeMinWidthFinalizeCount;

        /// <summary>Максимум итераций финального прохода, пока строки не материализованы.</summary>
        private const int TreeMinWidthFinalizeMax = 8;

        /// <summary>
        /// Кэш расчётной суммы минимальной ширины списка без побочных эффектов (issue #343).
        /// Обновляется в <see cref="UpdateTreeMinWidth"/> и используется для решения
        /// о необходимости горизонтальной полосы и в диагностике CM_COLUMNS.
        /// </summary>
        private double _treeMinWidthTotal;

        /// <summary>Данные верхней видимой строки при последнем замере <see cref="_treeMinWidthContent"/>.</summary>
        private object? _treeMinWidthAnchorData;

        /// <summary>
        /// Постоянная диагностика колонок (issue #309/#343): по умолчанию выключена.
        /// Включается флагом <c>CM_COLUMNS</c> в конфиге trace.json (issue #347).
        /// Семантика гейта (0.3.9.316): явное <c>false</c> в trace.json выключает диагностику
        /// ВСЕГДА — даже при установленной env-переменной CM_COLUMNS_TRACE=1 (замечание
        /// пользователя #347: записи «CM_COLUMNS: total=…» не должны появляться при false);
        /// env — только запасной способ включения, если флаг отсутствует либо true.
        /// Свойство читает флаг динамически — правка trace.json применяется без перезапуска.
        /// </summary>
        private static bool ColumnsTraceEnabled => TraceFlags.IsEnabled(TraceFlags.ColumnsFlag);

        /// <summary>
        /// Минимальная ширина колонки «Действия» (issue #121, v1.0.1.54): под три круглые
        /// кнопки-чипа (22 px каждая + отступы). Общий минимум —
        /// <see cref="Models.AppSettings.ActionsColumnMinWidth"/>; тот же минимум держит
        /// обработчик перетаскивания и кламп сохранённой ширины при загрузке. Раньше
        /// 120 оставляли много пустого места и не давали колонке сузиться.
        /// </summary>
        private const double ActionsColumnMinWidth = Models.AppSettings.ActionsColumnMinWidth;

        /// <summary>
        /// Задаёт контенту списка точную минимальную ширину, равную сумме ширин всех колонок
        /// заголовка (гибкое «Название» — по своему минимуму) вместе с ведущими отступами
        /// (колонки кнопок групп, компенсатор сдвига дерева, избранное, закрепление).
        /// Аналог <c>UpdateListMinWidth</c> из Linux/Avalonia: благодаря жёсткой границе
        /// горизонтальная прокрутка появляется ровно тогда, когда колонки реально не
        /// помещаются по ширине, а не «на волосок» раньше из-за округления последней
        /// (пустой) колонки «Конфигурация».
        /// <para>
        /// Минимум задаётся КОНТЕНТУ внешнего общего ScrollViewer (ListContentGrid,
        /// issue #309): внешний ScrollViewer меряет контент с бесконечной шириной,
        /// и MinWidth определяет его DesiredSize, а значит и горизонтальный extent
        /// полосы. Внутренняя прокрутка дерева при этом НЕ растягивается (именно она
        /// в прежней раскладке уводила правый край полосы за край окна) — дерево
        /// просто заполняет контент, а строки тянутся по вьюпорту, равному ширине
        /// контента.
        /// </summary>
        private void UpdateTreeMinWidth()
        {
            if (MainTree is null || HeaderGrid is null || ListContentGrid is null)
                return;

            var defs = HeaderGrid.ColumnDefinitions;
            if (defs.Count <= HeaderFirstDataColumn)
                return;

            // «Название» стоит перед первой колонкой данных (HeaderFirstDataColumn):
            // 0–3 — ведущие, 4 — имя, 5+ — значения, включая «Действия». Тот же
            // порядок, что в Linux/Avalonia (MainWindow.Avalonia.Columns.cs).
            var nameIndex = HeaderFirstDataColumn - 1;

            var leading = new List<ListMinWidthCalculator.Column>(nameIndex);
            for (var i = 0; i < nameIndex; i++)
                leading.Add(HeaderColumnSpec(defs[i]));

            var nameDef = defs[nameIndex];
            var nameWidth = nameDef.Width.IsAbsolute ? nameDef.Width.Value : 0;

            var values = new List<ListMinWidthCalculator.Column>(defs.Count - HeaderFirstDataColumn);
            for (var i = HeaderFirstDataColumn; i < defs.Count; i++)
                values.Add(HeaderColumnSpec(defs[i]));

            // Сумма ВСЕХ видимых колонок заголовка: скрытая колонка получает нулевую
            // абсолютную ширину (конвертер ColumnVis) и места не занимает (issue #255).
            // Расчёт общий с Linux/Avalonia (ListMinWidthCalculator), чтобы сумма не
            // расходилась с фактическим набором колонок (issue #309).
            // ВАЖНО: считаем строго по сумме видимых колонок БЕЗ слагаемого «фактической
            // желаемой ширины строк» (_treeMinWidthContent) — именно оно давало «пустой
            // хвост» справа от колонок (extent > суммы колонок, регресс issue #309).
            // Фактическая ширина контента ограничивается сверху этой же суммой (MaxWidth
            // ниже), поэтому строки не растягивают область прокрутки за последнюю колонку.
            var total = ListMinWidthCalculator.Compute(
                nameWidth, NameColumnMinWidth, leading, 0, values);
            // Кэш расчётной суммы без побочных эффектов (issue #343): используется
            // для решения о необходимости полосы и в диагностике CM_COLUMNS.
            _treeMinWidthTotal = total;

            // Фактическая сумма колонок заголовка может отличаться от расчётной из-за
            // округления: когда заголовок реально не помещается (headerSumNow > viewport),
            // берём максимум из расчётной и фактической суммы, чтобы полоса гарантированно
            // дотягивала до последней колонки (issue #309). Целевая ширина контента —
            // максимум из этой суммы и вьюпорта: при помещающихся колонках контент
            // растягивается на весь вьюпорт, полоса не появляется (анти-регресс #255).
            var headerSumNow = defs.Sum(d => d.ActualWidth);
            var viewportNow = DbListScroll?.ViewportWidth ?? MainTree.ActualWidth;
            var scrollableNow = DbListScroll?.ScrollableWidth ?? 0;
            // Целевая ширина контента: максимум из суммы колонок и вьюпорта с учётом
            // фактической суммы заголовка (округление) и guard «полоса не нужна»
            // (issue #21). Расчёт общий с Linux/Avalonia (ListMinWidthCalculator).
            var effective = ListMinWidthCalculator.EffectiveContentWidth(
                total, headerSumNow, viewportNow, scrollableNow);

            // Явная видимость горизонтальной полосы (issue #21, 1.0.0.62): решение только
            // по сумме колонок и вьюпорту — до отложенной записи ширины, чтобы и во время
            // перестроения дерева (смена вкладки) полоса оставалась отключённой, пока колонки
            // помещаются. Режим Auto сам по себе показывал полосу на кадр-два, когда
            // промежуточная ширина контента временно превышала вьюпорт.
            if (DbListScroll is not null)
            {
                var wantedHBar = ListMinWidthCalculator.NeedsHorizontalScroll(total, viewportNow)
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
                if (DbListScroll.HorizontalScrollBarVisibility != wantedHBar)
                    DbListScroll.HorizontalScrollBarVisibility = wantedHBar;
            }

            // Во время перестроения дерева (смена вкладки) НЕ фиксируем промежуточные
            // значения: строки ещё не материализованы, headerSumNow/viewportNow могут быть
            // промежуточными, и записанный effective «залипал» бы (issue #21). Сумма колонок
            // кэширована выше, запись откладывается до финального прохода
            // TreeMinWidthFinalizeStep по стабильному кадру.
            if (_treeMinWidthDeferred)
                return;

            // Вертикальный скроллбар вынесен отдельным столбцом вне горизонтальной прокрутки
            // (ListVerticalBar) и ширину контента не съедает, поэтому прежняя компенсация
            // «+ширина полосы» (эксперимент B, issue #309) не нужна.
            //
            // Контенту задаётся ЖЁСТКАЯ граница (и Min, и Max = effective): внешний ScrollViewer
            // меряет контент с бесконечной шириной, и без MaxWidth строки (длинное имя в
            // горизонтальном StackPanel) раздвигали ListContentGrid сверх суммы колонок —
            // появлялся «пустой хвост» справа (регресс issue #309). MaxWidth убирает хвост,
            // при этом последняя колонка остаётся достижимой: extent == max(сумма, вьюпорт).
            //
            // Защита от повторной раскладки без изменения (issue #343): ExtentWidthChange
            // зовёт UpdateTreeMinWidth на каждый ScrollChanged, и безусловная запись
            // MinWidth инвалидировала measure даже при том же значении, продлевая цикл
            // listMin → extent → ScrollChanged → listMin.
            //
            // Guard «полоса не нужна» (issue #21): при схлопывании контента до вьюпорта
            // разница может быть меньше обычного порога 0.5 (например, 1000.3 → 1000),
            // и без принудительной записи «зависшая» полоса не исчезла бы.
            var collapseToViewport = scrollableNow < 1 && total <= viewportNow + 1
                && effective < _lastTreeMinWidth - 0.01;
            if (collapseToViewport || Math.Abs(effective - _lastTreeMinWidth) > 0.5)
            {
                _lastTreeMinWidth = effective;
                ListContentGrid.MinWidth = effective;
                ListContentGrid.MaxWidth = effective;

                // Инструментальный лог (issue #309): фактические значения в журнале приложения,
                // чтобы следующая итерация правки опиралась на данные, а не на предположения.
                LogColumnsDiagnostics(effective);
            }
        }


        /// <summary>
        /// Пишет в журнал приложения фактические значения расчёта минимальной ширины
        /// (issue #309): сумму колонок заголовка, замер контента строк, MinWidth контента
        /// внешнего общего ScrollViewer, его viewport/extent и DPI окна. Пишет ТОЛЬКО
        /// при включённом флаге <c>CM_COLUMNS</c> (issue #347): без безусловных веток —
        /// ни постоянный лог, ни однократный стартовый дамп не пишутся при выключенном
        /// флаге, даже если установлена env-переменная (замечание пользователя #347,
        /// issue #343 — циклический вызов раздувал журнал до сотен мегабайт за полминуты).
        /// </summary>
        private void LogColumnsDiagnostics(double total)
        {
            if (!ColumnsTraceEnabled)
                return;
            try
            {
                var listScroll = DbListScroll;
                var defs = HeaderGrid?.ColumnDefinitions;
                var sumActual = defs is null ? 0 : defs.Sum(d => d.ActualWidth);
                // Вьюпорт/экстент внешнего общего ScrollViewer (issue #309): именно его
                // полосу видит пользователь, и она никогда не уходит за край окна.
                var viewport = listScroll?.ViewportWidth ?? 0;
                var extent = listScroll?.ExtentWidth ?? 0;
                var scrollable = listScroll?.ScrollableWidth ?? 0;
                // Вертикальная полоса вынесена отдельным столбцом вне горизонтальной прокрутки.
                var vSb = ListVerticalBar?.Visibility ?? System.Windows.Visibility.Collapsed;
                var dpi = VisualTreeHelper.GetDpi(this);

                // Фактические ширины колонок заголовка: растянутая звёздная «Название»
                // может выталкивать последние колонки за правый край, и сумма колонок
                // (sumActualHeader) оказывается больше расчётной (total) — это прямое
                // объяснение обрезания при scrollable ~0.
                var headerCols = defs is null ? string.Empty
                    : string.Join(",", defs.Select((d, i) => $"{i}:{d.ActualWidth:F1}"));
                var headerOrigin = 0d;
                var headerRight = 0d;
                if (HeaderGrid is not null)
                {
                    headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;
                    headerRight = headerOrigin + sumActual;
                }

                // Сравнение фактической ширины строк с заголовком: разные сетки могут дать
                // разный остаток звёздной «Название», и правая граница строки уходит дальше
                // правой границы заголовка (колонки данных обрезаются заголовком или наоборот).
                var (rowSum, rowOrigin, rowRight, rowCols, rowCount) = MeasureFirstRowDiagnostics();

                var sbw = SystemParameters.VerticalScrollBarWidth;
                var winW = ActualWidth;
                var winH = ActualHeight;
                var treeW = MainTree?.ActualWidth ?? 0;
                var headerW = HeaderGrid?.ActualWidth ?? 0;

                AppServices.GetRequiredService<IAppLogger>().Info(
                    $"CM_COLUMNS: total={total:F1}, sumActualHeader={sumActual:F1}, " +
                    $"content={_treeMinWidthContent:F1}, listMin={ListContentGrid?.MinWidth ?? 0:F1}, " +
                    $"viewport={viewport:F1}, extent={extent:F1}, scrollable={scrollable:F1}, vSb={vSb}, " +
                    $"panel=VirtualizingStackPanel, dpiScale={dpi.DpiScaleX:F2}, win={winW:F1}x{winH:F1}, " +
                    $"treeW={treeW:F1}, headerW={headerW:F1}, sbw={sbw:F1}, viewportSbw={viewport + sbw:F1}, " +
                    $"hdrOrigin={headerOrigin:F1}, hdrRight={headerRight:F1}, " +
                    $"rows={rowCount}, rowSum={rowSum:F1}, rowOrigin={rowOrigin:F1}, rowRight={rowRight:F1}, " +
                    $"cols=[{headerCols}], rowCols=[{rowCols}]");
            }
            catch
            {
                // Диагностика не должна ломать раскладку.
            }
        }

        /// <summary>
        /// Замер фактической ширины первой материализованной строки базы (или заголовка
        /// группы, если базы ещё не созданы): сумма <c>ActualWidth</c> колонок сетки строки,
        /// её левая координата в окне и правый край последней колонки (issue #309,
        /// эксперимент A — сравнение строк с заголовком). Возвращает нули и пустую строку,
        /// если ни одна строка ещё не материализована либо обход визуального дерева не
        /// удался. Диагностика не должна падать в горячем пути.
        /// </summary>
        private (double Sum, double OriginX, double RightEdgeX, string Columns, int Count) MeasureFirstRowDiagnostics()
        {
            if (MainTree is null)
                return (0, 0, 0, string.Empty, 0);
            try
            {
                var item = FindFirstInfobaseItem(MainTree);
                var grid = item is null ? null : FindGridByMarker(item, RowGridMarker)
                           ?? FindGridByMarker(item, GroupGridMarker);
                if (grid is null || grid.ColumnDefinitions.Count == 0)
                    return (0, 0, 0, string.Empty, 0);

                var sum = grid.ColumnDefinitions.Sum(d => d.ActualWidth);
                var origin = grid.TransformToAncestor(this).Transform(new Point(0, 0)).X;
                var cols = string.Join(",", grid.ColumnDefinitions
                    .Select((d, i) => $"{i}:{d.ActualWidth:F1}"));
                return (sum, origin, origin + sum, cols, grid.ColumnDefinitions.Count);
            }
            catch
            {
                return (0, 0, 0, string.Empty, 0);
            }
        }

        /// <summary>
        /// Пересчитывает <see cref="_treeMinWidthContent"/> замером первых видимых строк
        /// дерева с бесконечной шириной (issue #309). Горизонтальный StackPanel блока имени
        /// (MainWindow.xaml, ColumnSpan=5) меряет NameText без ограничения ширины, поэтому
        /// при бесконечном constraint Grid строки отдаёт звёздной колонке «Название»
        /// желание контента — DesiredSize совпадает с фактической шириной строки, а не
        /// с суммой колонок. Внутренний ScrollViewer дерева сам этого не учитывает
        /// (CanContentScroll меряет панель вьюпортной шириной), поэтому минимум контента
        /// досчитывается здесь. Вызов дороже обычного пересчёта суммы (замер до трёх
        /// строк), поэтому выполняется только в редких точках — стабилизация выравнивания
        /// (AlignHeaderToData), применение ширин/порядка колонок и смена верхней строки
        /// при прокрутке, а не в горячем пути <see cref="UpdateTreeMinWidth"/> (#255).
        /// </summary>
        private void UpdateTreeMinWidthContent(Grid? knownRowGrid = null)
        {
            // Кэш-хит (issue #21, Этап B1): верхняя видимая строка не менялась с прошлого
            // замера — дорогой замер до 12 строк (Measure с бесконечной шириной) на каждую
            // пересборку дерева/переключение вкладки (стабилизация выравнивания вызывала
            // его на каждой итерации) не повторяем. Значение инвалидируется сменой верхней
            // строки (прокрутка), сменой ширин/порядка колонок и компакт-режимом —
            // см. <see cref="InvalidateTreeMinWidthContent"/> и вход при новом top.
            var top = MainTree is null ? null : GetTopVisibleRowData();
            if (_treeMinWidthContent > 0 && ReferenceEquals(top, _treeMinWidthAnchorData))
                return;

            _treeMinWidthContent = 0;
            if (knownRowGrid is not null)
                MeasureRow(knownRowGrid);

            if (MainTree is null)
                return;

            var measured = knownRowGrid is null ? 0 : 1;
            foreach (var row in GetVisibleTreeViewItems())
            {
                // Лимит поднят с 3 до 12 (issue #309, седьмая попытка): широкая строка ниже
                // первых трёх не учитывалась, и полоса не дотягивала до конца контента.
                // Замер по 12 строкам покрывает видимую область при типичных высотах строк
                // и остаётся дешёвым (вызов не в горячем пути прокрутки).
                if (measured >= 12)
                    break;
                var grid = FindGridByMarker(row, RowGridMarker)
                           ?? FindGridByMarker(row, GroupGridMarker);
                if (grid is null || ReferenceEquals(grid, knownRowGrid))
                    continue;
                MeasureRow(grid);
                measured++;
            }

            // Запоминаем верхнюю строку, по которой выполнен замер (кэш-хит выше).
            _treeMinWidthAnchorData = top;

            void MeasureRow(Grid g)
            {
                // Замер «как хотела бы строка без ограничений»: ширина контента при
                // бесконечном constraint (текст названия не обрезается). Высота берётся
                // текущая, чтобы не ломать вертикальную раскладку строки.
                g.Measure(new Size(double.PositiveInfinity, g.ActualHeight));
                if (g.DesiredSize.Width > _treeMinWidthContent)
                    _treeMinWidthContent = g.DesiredSize.Width;
            }
        }

        /// <summary>
        /// Сбрасывает кэш фактической желаемой ширины строк (issue #21, Этап B1):
        /// применяется при изменении ширин/порядка колонок и применении компакт-режима,
        /// чтобы следующий <see cref="UpdateTreeMinWidthContent"/> выполнил полный замер.
        /// </summary>
        private void InvalidateTreeMinWidthContent()
        {
            _treeMinWidthContent = 0;
            _treeMinWidthAnchorData = null;
        }

        /// <summary>
        /// Ставит пересчёт минимальной ширины списка в очередь диспетчера (дебаунс,
        /// issue #309): вызывается из MouseMove ручного перетаскивания разделителя
        /// колонок и исполняется один раз на проход (Background), когда binding уже
        /// применил новую ширину колонок к заголовку и строкам. Так горизонтальная
        /// полоса прокрутки расширяется/сужается ЖИВЬЁМ во время перетаскивания,
        /// а не только после отпускания мыши (раньше полный пересчёт выполнялся лишь
        /// в MouseUp). Дорогой замер фактической ширины строк
        /// (<see cref="UpdateTreeMinWidthContent"/>) остаётся только на завершении
        /// перетаскивания — горячий путь не нагружается (анти-регресс #255).
        /// </summary>
        private void QueueTreeMinWidthRefresh()
        {
            if (_treeMinWidthQueued)
                return;
            _treeMinWidthQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _treeMinWidthQueued = false;
                UpdateTreeMinWidth();
                SyncHeaderWidthWithList();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Ставит финальный проход сходимости минимальной ширины списка после смены вкладки
        /// (issue #21): строки материализуются виртуализацией в проходе разметки ПОСЛЕ события
        /// TreeRebuilt, поэтому пересчёт на ApplicationIdle выполняется по стабильному кадру,
        /// а не по промежуточным headerSumNow/viewportNow. Повторные вызовы до выполнения
        /// объединяются в один проход (как <see cref="QueueHeaderAlign"/>).
        /// </summary>
        private void QueueTreeMinWidthFinalize()
        {
            if (_treeMinWidthFinalizeQueued)
                return;
            _treeMinWidthFinalizeQueued = true;
            _treeMinWidthFinalizeCount = 0;
            Dispatcher.BeginInvoke(new Action(TreeMinWidthFinalizeStep),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Один шаг финального прохода сходимости ширины (issue #21): снимает отложенную
        /// запись (<see cref="_treeMinWidthDeferred"/>) и пересчитывает минимум по стабильному
        /// кадру — строки уже материализованы, замер контента и сумма колонок актуальны.
        /// Пока первая строка не появилась (виртуализация достраивает её в следующем проходе),
        /// отложенная запись СОХРАНЯЕТСЯ (промежуточный effective не фиксируется), а шаг
        /// повторяется на ApplicationIdle — как <see cref="HeaderAlignStabilizeStep"/>.
        /// Пустой список (нет элементов вовсе) считается стабильным: строк материализовать
        /// нечему, и минимум фиксируется сразу.
        /// </summary>
        private void TreeMinWidthFinalizeStep()
        {
            _treeMinWidthFinalizeQueued = false;
            _treeMinWidthFinalizeCount++;
            // Ждём материализации строк: пока первая строка не появилась, раскладка ещё
            // промежуточная, и запись MinWidth «залипла» бы (issue #21). Пустой список
            // (нет элементов вовсе) считается стабильным — строк материализовать нечему.
            var rowsPending = MainTree is not null && MainTree.Items.Count > 0
                && FindFirstInfobaseItem(MainTree) is null;
            if (rowsPending && _treeMinWidthFinalizeCount < TreeMinWidthFinalizeMax)
            {
                _treeMinWidthFinalizeQueued = true;
                Dispatcher.BeginInvoke(new Action(TreeMinWidthFinalizeStep),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }
            // Стабильный кадр: снимаем отложенную запись и фиксируем минимум.
            _treeMinWidthDeferred = false;
            UpdateTreeMinWidthContent();
            UpdateTreeMinWidth();
        }

        /// <summary>Спецификация колонки заголовка для расчёта минимальной ширины.</summary>
        private static ListMinWidthCalculator.Column HeaderColumnSpec(ColumnDefinition d)
            => new(
                d.Width.IsAbsolute ? d.Width.Value : d.MinWidth,
                d.Width.IsAbsolute && d.Width.Value > 0);


        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель расположен на правом краю своей колонки (Grid.Column=N),
        /// поэтому он меняет ширину колонки с тем же индексом N.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, NameSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, VersionSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ConfigurationSplitter))
                return ConfigurationColumn;
            if (ReferenceEquals(sender, ConfigurationVersionSplitter))
                return ConfigurationVersionColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, ActionsSplitter))
                return ActionsColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return ServerColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return LastLaunchColumn;
            if (ReferenceEquals(sender, SizeSplitter))
                return SizeColumn;
            if (ReferenceEquals(sender, ModifiedSplitter))
                return ModifiedColumn;
            if (ReferenceEquals(sender, LastBackupSplitter))
                return LastBackupColumn;
            return null;
        }

        /// <summary>
        /// Начинает перетаскивание разделителя: захватывает мышь и запоминает стартовые значения.
        /// </summary>
        private void OnColumnResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // issue #96: ресайз колонок входит в сценарий «Ошибка интерфейса» — guard.
            UiExceptionGuard.Guard(
                nameof(OnColumnResize_MouseDown),
                () => OnColumnResize_MouseDownCore(sender, e));
        }

        private void OnColumnResize_MouseDownCore(object sender, MouseButtonEventArgs e)
        {
            var column = GetSplitterTargetColumn(sender);
            if (column is null)
                return;

            _resizeColumn = column;
            _resizeStartWidth = column.ActualWidth;
            _resizeStartMouse = e.GetPosition(this);

            if (sender is UIElement element)
                element.CaptureMouse();

            e.Handled = true;
        }

        /// <summary>
        /// Меняет ширину только целевой колонки при движении мыши.
        /// Ширина записывается в модель (VersionColumnWidth и т.д.), к которой привязаны
        /// и колонка заголовка, и колонки данных — поэтому заголовок и данные синхронно
        /// изменяются. Прямая установка Width перебивала бы binding и рассинхронизировала их.
        /// </summary>
        private void OnColumnResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeColumn is null || sender is not UIElement element || !element.IsMouseCaptured)
                return;

            var current = e.GetPosition(this);
            var delta = current.X - _resizeStartMouse.X;

            var newWidth = _resizeStartWidth + delta;
            if (ReferenceEquals(_resizeColumn, ActionsColumn) && newWidth < ActionsColumnMinWidth)
                newWidth = ActionsColumnMinWidth;
            else if (newWidth < 40)
                newWidth = 40;

            if (ReferenceEquals(_resizeColumn, SizeColumn))
            {
                // SizeColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.SizeColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }
            if (ReferenceEquals(_resizeColumn, ModifiedColumn))
            {
                // ModifiedColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.ModifiedColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }
            if (ReferenceEquals(_resizeColumn, LastBackupColumn))
            {
                // LastBackupColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.LastBackupColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }

            _viewModel.UpdateColumnWidths(
                ReferenceEquals(_resizeColumn, NameColumn) ? newWidth : NameColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, VersionColumn) ? newWidth : VersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationColumn) ? newWidth : ConfigurationColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationVersionColumn) ? newWidth : ConfigurationVersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LaunchModeColumn) ? newWidth : LaunchModeColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ServerColumn) ? newWidth : ServerColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LastLaunchColumn) ? newWidth : LastLaunchColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ActionsColumn) ? newWidth : ActionsColumn?.ActualWidth ?? 0);

            // Живой пересчёт минимума во время перетаскивания (дебаунс, issue #309):
            // полоса горизонтальной прокрутки расширяется сразу, пока колонку тянут
            // за край видимой области, а не только после отпускания мыши.
            QueueTreeMinWidthRefresh();
        }

        /// <summary>
        /// Завершает перетаскивание разделителя и сохраняет ширины колонок.
        /// </summary>
        private void OnColumnResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // issue #96: guard завершения ресайза — сбой сохранения ширин не должен
            // ронять UI (захват мыши при этом освобождается штатно).
            UiExceptionGuard.Guard(
                nameof(OnColumnResize_MouseUp),
                () => OnColumnResize_MouseUpCore(sender, e));
        }

        private void OnColumnResize_MouseUpCore(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.ReleaseMouseCapture();

            if (_resizeColumn is not null)
            {
                _viewModel.SaveColumnWidths(
                    NameColumn?.ActualWidth ?? 0,
                    VersionColumn?.ActualWidth ?? 0,
                    ConfigurationColumn?.ActualWidth ?? 0,
                    ConfigurationVersionColumn?.ActualWidth ?? 0,
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0,
                    ActionsColumn?.ActualWidth ?? 0);
                // Новая ширина колонки меняет желаемую ширину строки (звёздная «Название»
                // при бесконечном замере получает остаток) — пересчитываем фактическую
                // ширину контента до синхронизации минимума (issue #309).
                // Ширина колонок изменилась — кэш замера контента (issue #21, Этап B1)
                // должен выполнить ПОЛНЫЙ пересчёт.
                InvalidateTreeMinWidthContent();
                UpdateTreeMinWidthContent();
                SyncHeaderWidthWithList();
            }

            _resizeColumn = null;
            e.Handled = true;
        }


        // Слабые ссылки на созданные строки для пробы утечки (issue #21, только при трассировке).
        private readonly List<WeakReference> _leakProbeRows = new();

        private void TrackRowForLeakProbe(FrameworkElement row)
        {
            foreach (var w in _leakProbeRows)
                if (ReferenceEquals(w.Target, row))
                    return;
            _leakProbeRows.Add(new WeakReference(row));
        }

        /// <summary>
        /// Число живых строк из когда-либо созданных (после GC). Видимых строк ~20;
        /// если число растёт с каждым переключением — старые строки кто-то удерживает.
        /// </summary>
        internal int CountAliveProbeRows()
        {
            _leakProbeRows.RemoveAll(w => !w.IsAlive);
            return _leakProbeRows.Count;
        }
    }
}
#endif
