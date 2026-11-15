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

        /// <summary>Кэш внутреннего <see cref="ScrollViewer"/> дерева (issue #252).</summary>
        private ScrollViewer? _treeScrollViewer;
        private bool _treeScrollHookAttached;

        /// <summary>Позиция вертикальной прокрутки, запомненная до пересборки дерева (issue #252).</summary>
        private double _treeScrollOffset;

        /// <summary>Ссылка на верхнюю видимую строку до пересборки (восстановление по строке, а не по пикселям).</summary>
        private object? _treeScrollAnchorData;

        // Ключ группы выбранной базы до пересборки (issue #252): если после пересборки база
        // осталась в той же группе и видимой, позицию восстанавливаем точным offset, а не
        // BringIntoView — иначе список «съезжает» после правки свойств без смены группы.
        private string? _treeScrollAnchorGroup;

        /// <summary>
        /// Защита от рекурсии между вынесенной вертикальной полосой (<see cref="ListVerticalBar"/>)
        /// и внутренней прокруткой дерева (issue #309): полоса пишет в ScrollViewer, ScrollViewer
        /// отвечает синхронизацией полосы — флаг разрывает цикл (аналог _syncingScrollBar
        /// в Linux/Avalonia, MainWindow.Avalonia.Scroll.cs).
        /// </summary>
        private bool _syncingVerticalBar;

        /// <summary>Обработчики внешней прокрутки списка и вынесенной полосы уже подписаны (issue #309).</summary>
        private bool _listScrollHooked;

        /// <summary>
        /// Возвращает данные верхней видимой строки дерева. Виртуализация реализует только
        /// видимые контейнеры, поэтому первый собранный контейнер и есть верхняя видимая строка.
        /// </summary>
        private object? GetTopVisibleRowData()
        {
            var rows = GetVisibleTreeViewItems();
            return rows.Count > 0 ? rows[0].DataContext : null;
        }

        /// <summary>
        /// Запоминает позицию прокрутки до пересборки: список опустеет, и после неё
        /// прежнюю позицию узнать уже неоткуда (паттерн из Linux/Avalonia-версии).
        /// Помимо «сырого» offset сохраняем ссылку на верхнюю видимую строку: после
        /// пересборки виртуализированное дерево имеет другой контент/высоты, и восстановление
        /// по строке точнее, чем по пикселям (issue #252). Значения не обнуляются после
        /// применения, чтобы две пересборки подряд восстановили одну и ту же позицию.
        /// </summary>
        private void RememberTreeScroll()
        {
            // Перед открытием модального окна свойств снимаем отложенную прокрутку (issue #252):
            // в очереди мог стоять DeferredScrollSelectedIntoView, поставленный кликом, которым
            // открыли свойства. Если его не снять, он исполнится позже (priority Loaded) и перетрёт
            // восстановленную при закрытии окна позицию — список «скачет» после «Нет».
            _scrollQueued = false;
            _scrollTargetData = null;

            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            _treeScrollOffset = treeScroll.VerticalOffset;
            _treeScrollAnchorData = GetTopVisibleRowData();
            _treeScrollAnchorGroup = _viewModel?.SelectedInfobase is { } ib
                ? FindGroupNodeByInfobase(ib)?.NodeKey
                : null;
        }

        /// <summary>
        /// Восстанавливает позицию прокрутки после пересборки дерева, чтобы список
        /// не «прыгал» в сторону (например, после сохранения свойств базы, issue #252).
        /// Вызывается после восстановления выделения: BringIntoView мог сдвинуть список
        /// к строке — возвращаем позицию, где был пользователь.
        /// </summary>
        private void RestoreTreeScrollAfterRebuild()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            try
            {
                // Layout уже устоялся в вызывающем RevealAndSelectAfterRebuild (он вызывает
                // MainTree.UpdateLayout перед поиском контейнера цели), поэтому повторная принудительная
                // раскладка здесь не нужна и только добавляла работу в горячем пути (issue #252).

                // Если выбранная база осталась в той же группе, ключевое состояние не изменилось —
                // восстанавливаем точный offset, а не «якорную» строку: так после правки свойств
                // без смены группы список не сдвигается (issue #252).
                var currentGroup = _viewModel?.SelectedInfobase is { } ib
                    ? FindGroupNodeByInfobase(ib)?.NodeKey
                    : null;
                var groupUnchanged = !string.IsNullOrEmpty(_treeScrollAnchorGroup)
                    && string.Equals(_treeScrollAnchorGroup, currentGroup, StringComparison.Ordinal);

                // Guard «ничего ключевого не изменилось» (issue #252): если группа не сменилась И
                // запомненная верхняя видимая строка осталась верхней после пересборки — позиция
                // уже корректна, не позиционируем вовсе (требование пользователя: «если ничего
                // ключевого не изменилось, просто не надо позиции пересчитывать»). Касается только
                // прокрутки; выделение и фокус восстанавливаются отдельно в RevealAndSelectAfterRebuild.
                if (groupUnchanged &&
                    _treeScrollAnchorData is not null &&
                    ReferenceEquals(GetTopVisibleRowData(), _treeScrollAnchorData))
                {
                    return;
                }

                if (groupUnchanged)
                {
                    treeScroll.ScrollToVerticalOffset(_treeScrollOffset);
                }
                else if (_treeScrollAnchorData is { } anchor)
                {
                    // Восстанавливаем по запомненной верхней видимой строке (не по «сырым» пикселям):
                    // виртуализированное дерево после пересборки имеет другой контент/высоты (issue #252).
                    if (FindTreeViewItemForData(anchor) is { } anchorItem)
                        anchorItem.BringIntoView();
                }
                else
                {
                    treeScroll.ScrollToVerticalOffset(_treeScrollOffset);
                }

                // Горизонтальная прокрутка дерева не используется — её ведёт внешний общий
                // ScrollViewer (DbListScroll, issue #309): сбрасываем горизонталь внутренней
                // прокрутки, чтобы восстановление не создавало лишний горизонтальный скрол
                // (анти-регресс #255).
                if (treeScroll.HorizontalOffset != 0)
                    treeScroll.ScrollToHorizontalOffset(0);

                // Clamp вертикальной позиции к допустимому диапазону (issue #252).
                var maxOffset = Math.Max(0, treeScroll.ScrollableHeight);
                if (treeScroll.VerticalOffset > maxOffset + 0.01)
                    treeScroll.ScrollToVerticalOffset(maxOffset);
            }
            catch
            {
                // Дерево могло отсоединиться во время пересборки — игнорируем.
            }
        }

        /// <summary>
        /// Восстанавливает прежнюю позицию прокрутки после закрытия окна свойств базы без
        /// сохранения («Нет»). Пересборки дерева не было, поэтому события TreeRebuilding/
        /// TreeRebuilt не сработали и <see cref="RestoreTreeScrollAfterRebuild"/> не вызвался,
        /// а при закрытии модального окна WPF сам подтягивает выбранную строку в видимую
        /// область — список «уезжает» вверх/вниз (issue #252). Возвращаем точный offset,
        /// запомненный <see cref="RememberTreeScroll"/> перед открытием окна.
        /// </summary>
        private void RestoreTreeScrollAfterCancel()
        {
            // Восстанавливаем позицию на приоритете Render (issue #252) — до отрисовки следующего
            // кадра. При закрытии модального окна WPF сам подтягивает выбранную строку в видимую
            // область (автоскролл): если откладывать восстановление до ApplicationIdle, сначала
            // рисуется этот «прыжок», потом «возврат» — два видимых перемещения. На Render offset
            // возвращается раньше, чем WPF успеет отрисовать скачок автоскролла. Зависших отложенных
            // прокруток к этому моменту нет: RememberTreeScroll снимает их при открытии окна свойств.
            Dispatcher.BeginInvoke(new Action(RestoreTreeScrollAfterCancelCore),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>Непосредственное восстановление позиции после «Нет» (см. <see cref="RestoreTreeScrollAfterCancel"/>).</summary>
        private void RestoreTreeScrollAfterCancelCore()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            try
            {
                // Чистый restore-путь: принудительная раскладка не нужна, позиция возвращается
                // точным offset без пересчёта ширины колонок (issue #252).
                treeScroll.ScrollToVerticalOffset(_treeScrollOffset);

                // Горизонталь дерева не используется — сбрасываем (как после пересборки).
                if (treeScroll.HorizontalOffset != 0)
                    treeScroll.ScrollToHorizontalOffset(0);

                var maxOffset = Math.Max(0, treeScroll.ScrollableHeight);
                if (treeScroll.VerticalOffset > maxOffset + 0.01)
                    treeScroll.ScrollToVerticalOffset(maxOffset);
            }
            catch
            {
                // Дерево могло быть отсоединено — игнорируем.
            }
        }

        /// <summary>
        /// Внутренний ScrollViewer шаблона TreeView (отвечает за вертикальную прокрутку
        /// и виртуализацию). Найденный экземпляр кэшируется: без кэша каждый вызов делает
        /// <c>ApplyTemplate()</c> и полный обход визуального дерева, что при частой прокрутке
        /// или материализации строк добавляет нагрузку и «прыжки» списка. Кэш сбрасывается
        /// при отсоединении дерева (Unloaded), чтобы не оставаться с устаревшей ссылкой
        /// после пересборки/пересоздания контейнера.
        /// </summary>
        private ScrollViewer? GetTreeScrollViewer()
        {
            if (MainTree is null)
                return null;

            if (!_treeScrollHookAttached)
            {
                _treeScrollHookAttached = true;
                MainTree.Unloaded += (_, _) =>
                {
                    _treeScrollViewer = null;
                };
            }
            if (_treeScrollViewer is not null)
                return _treeScrollViewer;

            // Шаблон может быть ещё не применён.
            MainTree.ApplyTemplate();
            _treeScrollViewer = FindVisualChild<ScrollViewer>(MainTree);
            return _treeScrollViewer;
        }

        /// <summary>
        /// Подписывается на ScrollChanged внутреннего ScrollViewer дерева (синхронизация
        /// вынесенной вертикальной полосы и обновление минимальной ширины контента).
        /// </summary>
        private void AttachTreeScrollHandler()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            treeScroll.ScrollChanged -= OnTreeScroll_ScrollChanged;
            treeScroll.ScrollChanged += OnTreeScroll_ScrollChanged;
            // Вертикальная полоса вынесена отдельным столбцом — сразу приводим её геометрию.
            SyncVerticalScrollBar(treeScroll);
        }

        /// <summary>
        /// Подписывает обработчики внешнего общего ScrollViewer списка (DbListScroll) и вынесенной
        /// вертикальной полосы (issue #309): изменение вьюпорта пересматривает минимальную ширину
        /// контента, полоса прокручивает дерево, а колесо над ней обрабатывается штатно
        /// (Shift+колесо — горизонталь). Вызывается из OnWindowLoaded.
        /// </summary>
        private void AttachListScrollHandler()
        {
            if (_listScrollHooked)
                return;
            _listScrollHooked = true;
            if (DbListScroll is not null)
            {
                DbListScroll.ScrollChanged -= OnListScroll_ScrollChanged;
                DbListScroll.ScrollChanged += OnListScroll_ScrollChanged;
            }
            if (ListVerticalBar is not null)
            {
                ListVerticalBar.ValueChanged -= OnListVerticalBar_ValueChanged;
                ListVerticalBar.ValueChanged += OnListVerticalBar_ValueChanged;
                ListVerticalBar.PreviewMouseWheel -= OnListVerticalBar_PreviewMouseWheel;
                ListVerticalBar.PreviewMouseWheel += OnListVerticalBar_PreviewMouseWheel;
            }
            // Полоса начинается под заголовком колонок; высота заголовка меняется компактным
            // режимом и темой — обновляем отступ при каждом изменении размера.
            if (DbHeaderBorder is not null)
            {
                DbHeaderBorder.SizeChanged -= OnDbHeader_SizeChanged;
                DbHeaderBorder.SizeChanged += OnDbHeader_SizeChanged;
            }
            UpdateVerticalBarMargin();
        }

        private void OnListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Ширина вьюпорта внешней прокрутки влияет на решение о горизонтальной полосе
            // (анти-регресс #255): при помещающихся колонках минимум контента не должен
            // превышать вьюпорт. Пересчёт на каждый ScrollChanged заменён дебаунсом
            // (QueueTreeMinWidthRefresh, один раз на проход Background) — при пересборке
            // дерева/переключении вкладки ScrollChanged сыплет серией событий, и синхронный
            // пересчёт на каждом усугублял каскад пересчётов колонок (issue #21, Этап B4).
            if (e.ViewportWidthChange != 0)
                QueueTreeMinWidthRefresh();
        }

        private void OnTreeScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer treeScroll)
                return;

            // Горизонтальная прокрутка ведётся внешним общим ScrollViewer (DbListScroll,
            // issue #309): внутреннюю горизонталь дерева держим на нуле, иначе её ненулевой
            // offset при старте или пересборке давал бы необоснованную горизонтальную полосу
            // (анти-регресс #255).
            if (treeScroll.HorizontalOffset != 0)
                treeScroll.ScrollToHorizontalOffset(0);

            // Issue #130: вертикальная прокрутка меняет набор реализованных строк, а высота
            // линий теперь равна низу последней реализованной строки. Пересчитываем после
            // раскладки (Loaded), когда контейнеры новой области уже созданы.
            if (e.VerticalChange != 0 || e.ExtentHeightChange != 0)
            {
                Dispatcher.BeginInvoke(new Action(RefreshGridLinesOverlay),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // Вертикальная полоса вынесена отдельным столбцом — синхронизируем её геометрию.
            SyncVerticalScrollBar(treeScroll);

            // Фактическая ширина контента зависит от верхней видимой строки: при вертикальной
            // прокрутке наверх приходит строка с другим названием, и замер _treeMinWidthContent,
            // выполненный по прежней строке, может не дотягивать до реальной ширины (issue #309).
            // Пересчитываем замер только когда полоса реально есть (минимум больше вьюпорта)
            // и верхняя строка сменилась — при помещающихся колонках замер не нужен вовсе.
            // Во время перестроения дерева (смена вкладки) замер пропускаем: строки ещё не
            // материализованы, и финальный проход TreeMinWidthFinalizeStep перемерит их по
            // стабильному кадру (issue #21) — дорогой Measure(Infinity) здесь был бы впустую.
            if (e.ExtentWidthChange != 0
                && _treeMinWidthTotal > (DbListScroll?.ViewportWidth ?? 0)
                && !_treeMinWidthDeferred)
            {
                var top = GetTopVisibleRowData();
                if (!ReferenceEquals(top, _treeMinWidthAnchorData))
                {
                    // Установку _treeMinWidthAnchorData больше НЕ делаем здесь:
                    // это ответственность UpdateTreeMinWidthContent (кэш-хит, issue #21,
                    // Этап B1) — сброс до вызова отключил бы кэш-хит на повторных вызовах.
                    UpdateTreeMinWidthContent();
                    UpdateTreeMinWidth();
                }
            }
        }

        /// <summary>
        /// Обновляет геометрию вынесенной вертикальной полосы по внутренней прокрутке дерева
        /// (issue #309): максимум, размер вьюпорта, шаги и значение. Полоса стоит отдельным
        /// столбцом вне горизонтальной прокрутки, поэтому остаётся у правого края видимой
        /// области и не перекрывает последнюю колонку.
        /// </summary>
        private void SyncVerticalScrollBar(ScrollViewer treeScroll)
        {
            if (ListVerticalBar is null)
                return;
            try
            {
                var scrollable = Math.Max(0, treeScroll.ScrollableHeight);
                var visible = scrollable > 0.5 && treeScroll.ViewportHeight > 0.5;
                // Hidden, а не Collapsed (issue #21, 1.0.0.62): место под полосу резервируется
                // всегда, поэтому ширина вьюпорта списка не прыгает на ширину полосы при
                // переключении вкладок с разным числом строк («Недавние» ↔ «Все») — именно
                // этот скачок давал мигающую горизонтальную полосу.
                var wantedVisibility = visible ? Visibility.Visible : Visibility.Hidden;
                if (ListVerticalBar.Visibility != wantedVisibility)
                    ListVerticalBar.Visibility = wantedVisibility;

                if (Math.Abs(ListVerticalBar.Maximum - scrollable) > 0.01)
                    ListVerticalBar.Maximum = scrollable;
                if (Math.Abs(ListVerticalBar.ViewportSize - treeScroll.ViewportHeight) > 0.01)
                    ListVerticalBar.ViewportSize = treeScroll.ViewportHeight;

                // Шаги кликов по дорожке/стрелкам: страница ≈ вьюпорт, строка ≈ вьюпорт/30
                // (примерно высота видимой строки; точные шаги сама прокрутка дерева ведёт
                // по своему содержимому).
                var large = Math.Max(10, treeScroll.ViewportHeight - 1);
                if (Math.Abs(ListVerticalBar.LargeChange - large) > 0.01)
                    ListVerticalBar.LargeChange = large;
                var small = Math.Max(1, treeScroll.ViewportHeight / 30.0);
                if (Math.Abs(ListVerticalBar.SmallChange - small) > 0.01)
                    ListVerticalBar.SmallChange = small;

                if (_syncingVerticalBar)
                    return;
                var offset = Math.Min(treeScroll.VerticalOffset, scrollable);
                if (Math.Abs(ListVerticalBar.Value - offset) > 0.01)
                {
                    _syncingVerticalBar = true;
                    try { ListVerticalBar.Value = offset; }
                    finally { _syncingVerticalBar = false; }
                }
            }
            catch
            {
                // Синхронизация полосы не должна ломать раскладку.
            }
        }

        /// <summary>Пользователь потянул вынесенную вертикальную полосу — прокручиваем дерево.</summary>
        private void OnListVerticalBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingVerticalBar || sender is not ScrollBar bar)
                return;
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            var maxOffset = Math.Max(0, treeScroll.ScrollableHeight);
            treeScroll.ScrollToVerticalOffset(Math.Min(bar.Value, maxOffset));
        }

        private void OnListVerticalBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        private void OnDbHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVerticalBarMargin();
        }

        /// <summary>
        /// Вынесенная вертикальная полоса начинается под заголовком колонок: высота заголовка
        /// меняется компактным режимом и темой, поэтому отступ пересчитывается при изменении
        /// размера (OnDbHeader_SizeChanged) и при инициализации.
        /// </summary>
        private void UpdateVerticalBarMargin()
        {
            if (ListVerticalBar is null)
                return;
            var top = Math.Max(0, DbHeaderBorder?.ActualHeight ?? 0);
            var margin = new Thickness(0, top, 0, 0);
            if (ListVerticalBar.Margin != margin)
                ListVerticalBar.Margin = margin;
        }

        private void OnMainTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        private void OnDbHeader_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        /// <summary>
        /// Прокрутка списка: колесо — вертикаль, Shift+колесо — горизонталь.
        /// Всегда помечает событие обработанным, чтобы вложенные элементы не «съедали» колесо.
        /// </summary>
        private void ScrollListByWheel(MouseWheelEventArgs e)
        {
            // Ctrl+колесо — масштаб строк списка (issue #303): вверх — крупнее,
            // вниз — мельче; список при этом не прокручивается.
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    _viewModel.ZoomListBy(+0.1);
                else if (e.Delta < 0)
                    _viewModel.ZoomListBy(-0.1);
                e.Handled = true;
                return;
            }

            // e.Delta обычно ±120; делим для плавности.
            var offset = -e.Delta / 3.0;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Горизонтальная прокрутка — внешний общий ScrollViewer (заголовок + дерево,
                // issue #309); внутренняя горизонталь дерева не используется (держится на нуле).
                if (DbListScroll is { } listScroll)
                    listScroll.ScrollToHorizontalOffset(listScroll.HorizontalOffset + offset);
            }
            else
            {
                var treeScroll = GetTreeScrollViewer();
                if (treeScroll is null)
                {
                    // Повторная попытка после загрузки шаблона.
                    AttachTreeScrollHandler();
                    treeScroll = GetTreeScrollViewer();
                }
                if (treeScroll is null)
                    return;
                treeScroll.ScrollToVerticalOffset(treeScroll.VerticalOffset + offset);
            }

            e.Handled = true;
        }

    }
}
#endif
