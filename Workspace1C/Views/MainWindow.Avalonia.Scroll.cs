#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Прокрутка дерева и восстановление выделения главного окна (Avalonia/Linux).
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Позиция прокрутки списка, снятая перед пересборкой дерева.</summary>
        private Avalonia.Vector? _treeScrollOffset;

        /// <summary>
        /// Идёт целевая навигация «переход к строке» (issue #31): цель уже захвачена, и окну
        /// нужно придти к ней, а не восстанавливать прежнюю позицию прокрутки. Устанавливается
        /// обработчиком RevealFindInListRequested и сбрасывается после применения/исчерпания
        /// попыток. Используется в TreeRebuilt: восстановление выполняется и при чистой смене
        /// вкладки, если запрошен переход к строке (паритет с WPF).
        /// </summary>
        private bool _revealFindInListPending;

        /// <summary>Максимум отложенных попыток довести строку до видимой области (issue #285).</summary>
        private const int MaxRevealAttempts = 8;

        /// <summary>Внутренняя прокрутка дерева: вертикаль ведёт сам TreeView.</summary>
        private ScrollViewer? TreeScroll =>
            _tree?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        /// <summary>
        /// Связывает внешнюю полосу прокрутки с деревом. Полоса стоит отдельным
        /// столбцом, вне горизонтальной прокрутки, поэтому остаётся у правого
        /// края области даже когда колонки шире окна.
        /// </summary>
        private void AttachVerticalScrollBar()
        {
            // Компактный режим пересобирает окно целиком, и дерево с полосой
            // становятся другими объектами. Поэтому сверяемся с самой прокруткой,
            // а не с признаком «уже привязывались»: иначе после переключения
            // полоса остаётся подписанной на выброшенный ScrollViewer.
            if (_listVerticalBar is not { } bar || TreeScroll is not { } scroll)
                return;
            if (ReferenceEquals(_boundTreeScroll, scroll) && ReferenceEquals(_boundScrollBar, bar))
                return;

            // Собственные полосы дерева скрываем не только присоединённым свойством
            // на самом дереве (оно может не дойти до внутреннего ScrollViewer шаблона),
            // но и напрямую на найденной прокрутке. Иначе её вертикальная полоса
            // рисуется у правого края содержимого дерева, а когда колонки шире окна
            // и включается горизонтальная прокрутка, оказывается поверх строк списка,
            // а не у правого края области. Полоса прячется, прокрутка остаётся.
            // Горизонталь тоже скрываем, но НЕ отключаем (Hidden, не Disabled):
            // контенту нужно разрешение быть шире области, чтобы внешний общий
            // ScrollViewer (listArea) получил горизонтальный extent и показал полосу
            // (issue #309). Сам внутренний скроллер по X не прокручивает — его
            // содержимое двигает внешний контейнер, поэтому Offset.X держится на нуле.
            ScrollViewer.SetVerticalScrollBarVisibility(scroll, ScrollBarVisibility.Hidden);
            ScrollViewer.SetHorizontalScrollBarVisibility(scroll, ScrollBarVisibility.Hidden);

            foreach (var link in _scrollBarLinks)
                link.Dispose();
            _scrollBarLinks.Clear();
            _boundTreeScroll = scroll;
            _boundScrollBar = bar;

            void Sync()
            {
                if (_syncingScrollBar)
                    return;
                _syncingScrollBar = true;
                try
                {
                    // Горизонтальная прокрутка дерева не используется (её ведёт внешний
                    // заголовок), но при пересборке/старте внутренняя прокрутка может получить
                    // ненулевую горизонталь — из-за неё появляется «необоснованный»
                    // горизонтальный скролл (issue #255). Держим её на нуле.
                    if (scroll.Offset.X != 0)
                        scroll.Offset = scroll.Offset.WithX(0);

                    var hidden = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                    bar.Maximum = hidden;
                    bar.ViewportSize = scroll.Viewport.Height;
                    // Шаги берёт сама прокрутка по своему содержимому. Без этого
                    // у отдельной полосы остаются значения RangeBase по умолчанию,
                    // и щелчок по дорожке двигает список на десять точек вместо
                    // страницы, а стрелка на одну точку вместо строки.
                    bar.SmallChange = scroll.SmallChange.Height;
                    bar.LargeChange = scroll.LargeChange.Height;
                    bar.Value = Math.Min(scroll.Offset.Y, hidden);
                }
                finally { _syncingScrollBar = false; }
            }

            var lastOffsetY = scroll.Offset.Y;
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(new PropertyObserver<Vector>(offset =>
                {
                    Sync();
                    // Issue #130: при вертикальной прокрутке меняется набор видимых строк —
                    // высота линий (низ последней реализованной строки) пересчитывается
                    // после раскладки, когда геометрия строк уже обновлена.
                    if (Math.Abs(offset.Y - lastOffsetY) > 0.01)
                    {
                        lastOffsetY = offset.Y;
                        Dispatcher.UIThread.Post(RefreshGridLinesOverlay, DispatcherPriority.Loaded);
                    }
                })));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ExtentProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.SmallChangeProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.LargeChangeProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));

            // Ширина контента внешней полосы считается по сумме видимых колонок
            // и вьюпорту (SyncListWidthToViewport, issue #309): изменения Extent/Viewport
            // внутренней прокрутки дерева пересчитывают её; сравнение внутри
            // SyncListWidthToViewport не даёт лишних присвоений при прокрутке.
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ExtentProperty)
                .Subscribe(new PropertyObserver<Size>(_ => SyncListWidthToViewport(scroll))));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(new PropertyObserver<Size>(_ => SyncListWidthToViewport(scroll))));

            Sync();
            // При (пере)привязке прокрутки сразу выставляем ширину контента списка по её
            // фактическому extent/вьюпорту: при пересборке окна (компактный режим) вьюпорт
            // уже ненулевой, и отложенная до первого ScrollChanged синхронизация оставила бы
            // полосу без последней колонки до следующего изменения размеров (issue #309).
            SyncListWidthToViewport(scroll);
        }

        /// <summary>
        /// Запоминает позицию прокрутки до пересборки: список опустеет, и после
        /// неё прежнюю позицию узнать уже неоткуда.
        /// </summary>
        private void RememberTreeScroll()
            // Значение перезаписывается каждой пересборкой и не обнуляется после
            // применения: две пересборки подряд тогда восстановят одну и ту же
            // позицию, а не потеряют её из-за уже отработавшего вызова.
            => _treeScrollOffset = TreeScroll?.Offset ?? _treeScrollOffset;

        /// <summary>
        /// Возвращает выделение строки после пересборки дерева: узлы групп
        /// пересоздаются, и дерево теряет подсветку вместе с ними. Объекты баз
        /// при этом те же самые, поэтому выбранная база ищется по ссылке.
        /// Установка откладывается ниже компоновки, чтобы попасть после
        /// перестроения строк, а цель читается в момент выполнения: вызывающие
        /// меняют выбор уже после возврата из пересборки (создание, регистрация
        /// и удаление базы), и снятая заранее цель подсветила бы чужую строку.
        /// </summary>
        private void RestoreTreeSelection()
        {
            if (_vm is null)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null)
                    return;

                var target = (object?)_vm.SelectedInfobase ?? _vm.SelectedGroupNode;
                if (target is not null && !ReferenceEquals(_tree.SelectedItem, target))
                {
                    // Выбор ставится напрямую на конкретный контейнер, а не через
                    // SelectedItem: закреплённая база присутствует в дереве дважды
                    // («Закреплённые» и своя группа), и разрешение по данным всегда
                    // находит первую копию — подсветка «перепрыгивала» бы в узел
                    // «Закреплённые» в начало списка (issue #301). Для базы строка
                    // ищется внутри её настоящей группы, как в «Найти в списке».
                    // Обработчик отключаем: выбор уже согласован с вьюмоделью, и
                    // повторный проход только сбросил бы парное поле.
                    _tree.SelectionChanged -= OnTreeSelectionChanged;
                    try
                    {
                        if (FindFindInListRow(target) is { } row)
                        {
                            _tree.SelectRow(row);
                            // Показываем восстановленную строку: AutoScrollToSelectedItem
                            // отключено, чтобы разметка данных не прокручивала список
                            // к ПЕРВОЙ копии базы (issue #301).
                            row.BringIntoView();
                        }
                    }
                    finally { _tree.SelectionChanged += OnTreeSelectionChanged; }
                }

                // Прокрутка возвращается последней: показываем прежнее место списка.
                // Горизонталь восстанавливается только вертикалью: смена набора колонок
                // (удаление/добавление) меняет ширину контента, и восстановление прежнего
                // Offset.X уводило бы позицию вправо (регресс issue #309) — внутренняя
                // горизонталь дерева всё равно не используется (её ведёт внешний контейнер).
                if (_treeScrollOffset is { } offset && TreeScroll is { } scroll)
                    scroll.Offset = offset.WithX(0);

                // Вернуть клавиатурный фокус строке после закрытия модального
                // диалога (например, сохранения настроек базы): контейнер прежней
                // строки уничтожен пересборкой, и фокус осел на окне. Если сейчас
                // идёт ввод в текстовом поле (поиск, теги), фокус не трогаем,
                // чтобы не выбивать курсор из поля во время набора. Строка та же,
                // что выделена выше (для базы — не первая копия в «Закреплённых»).
                if (target is not null
                    && FocusManager?.GetFocusedElement() is not TextBox
                    && FindFindInListRow(target) is { } focusRow)
                {
                    focusRow.Focus();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// «Найти в списке» (issue #285): команда переключила вкладку на «Все базы», раскрыла
        /// группы-предки и пересобрала дерево; цель уже выставлена во вьюмодели. Возврат
        /// прежней позиции прокрутки здесь отменяется (обнуляем offset, запомненный
        /// RememberTreeScroll), а строка цели выделяется и доводится до видимой области.
        /// </summary>
        private void RevealFindInList()
        {
            // Возврат прежней позиции прокрутки здесь отменяется (обнуляем offset, запомненный
            // RememberTreeScroll) — цель «Найти в списке» показать строку базы, а не старое место
            // списка (issue #285; кейс #252 «возврат после „Нет"» не затрагивается — у него свой
            // путь через RestoreTreeScrollAfterCancel).
            _treeScrollOffset = null;
            // Липкая цель (issue #31): захватываем базу в момент запроса. Последующая
            // пересборка (сброс поиска/тегов внутри Ctrl+T) может очистить SelectedInfobase
            // раньше, чем отработает отложенный reveal, — reveal продолжит искать захваченную цель.
            var target = (object?)_vm?.SelectedInfobase ?? _vm?.SelectedGroupNode;
            if (target is null)
                return;
            _revealFindInListPending = true;
            RevealTrace.Log($"Avalonia RevealFindInList: start, target={DescribeAvaloniaTarget(target)}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null || _tree is null)
                    return;

                // Ищем строку во «Все базы»: для базы — внутри её настоящей группы (или узла
                // «Без группы»), а не первую копию в «Закреплённых». Если контейнер ещё не
                // создан (не прошёл проход компоновки) — дожимаем попытками ниже.
                if (FindFindInListRow(target) is { } row)
                {
                    SelectAndReveal(row);
                    _revealFindInListPending = false;
                    RevealTrace.Log($"Avalonia Reveal: attempt=first, SELECTED target={DescribeAvaloniaTarget(target)}");
                    return;
                }

                RevealFindInListAttempt(target, 0);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Строка-цель команды «Найти в списке»: для базы — контейнер внутри домашнего узла
        /// во «Все базы» (настоящая группа или «Без группы»), для группы — первый найденный.
        /// Общий поиск по данным для закреплённой базы вернул бы строку «Закреплённых»
        /// (она первая в дереве), поэтому он используется только как запасной вариант.
        /// Домашний узел считается ЧИСТОЙ логикой планирования (<see cref="RevealPlanner"/>),
        /// а у найденного контейнера дополнительно проверяются предки — путь обязан дойти
        /// до домашнего узла без узла «Закреплённые» (issue #31).
        /// </summary>
        private TreeViewItem? FindFindInListRow(object target)
        {
            if (_vm is not null
                && target is Infobase ib
                && RevealPlanner.PlanLeaf(_vm.GroupNodes, ib, revealToHome: true) is { } home)
            {
                var row = _tree.ContainerForItemWithin(home, ib);
                var ok = row is not null && _tree.IsRowWithinHome(row, home);
                RevealTrace.Log($"Avalonia FindFindInListRow: target={ib.Id}, home={DescribeAvaloniaLeaf(home)}, " +
                                $"row={row is not null}, withinHome={ok}");
                return ok ? row : null;
            }
            return _tree.ContainerForItem(target);
        }

        /// <summary>
        /// Выделяет конкретный контейнер строки и доводит его до видимой области. Выделение
        /// ставится на контейнер, а не через SelectedItem: закреплённая база есть в дереве
        /// дважды, и SelectedItem всегда резолвился бы в первое вхождение вверху
        /// («Закреплённые»). Из контейнера выбор поднимается штатно (тот же приём,
        /// что в навигации клавишами LeveledTreeView).
        /// </summary>
        private void SelectAndReveal(TreeViewItem row)
        {
            _tree.SelectRow(row);
            row.BringIntoView();
            row.Focus();
        }

        /// <summary>
        /// Доводит контейнер строки цели до видимой области. Если контейнер ещё не создан
        /// (не прошёл проход компоновки), повторяет отложенно с ограничением числа попыток.
        /// </summary>
        private void RevealFindInListAttempt(object target, int attempt)
        {
            if (_vm is null || _tree is null)
                return;
            try
            {
                if (FindFindInListRow(target) is { } row)
                {
                    SelectAndReveal(row);
                    _revealFindInListPending = false;
                    RevealTrace.Log($"Avalonia Reveal: attempt={attempt}, SELECTED target={DescribeAvaloniaTarget(target)}");
                    return;
                }
            }
            catch
            {
                // Контейнер мог отсоединиться во время пересборки — выходим.
                _revealFindInListPending = false;
                return;
            }

            // Контейнер цели ещё не создан (issue #285): строка далеко вниз внутри длинной
            // раскрытой группы, а контейнеры реализуются только для видимой области. Поэтому
            // двигаемся к цели прокруткой — сначала к домашнему узлу во «Все базы» (его строки
            // материализуются), затем итеративно к самой строке через EnsureDataVisible.
            // Каждый Dispatcher-цикл делает шаг прокрутки; когда контейнер появится, выделяем.
            // Домашний узел считается чистой логикой (RevealPlanner), а EnsureDataVisible
            // ищет строку ТОЛЬКО в его поддереве — дубль «Закреплённые» исключён (issue #31).
            if (target is Infobase ib
                && RevealPlanner.PlanLeaf(_vm.GroupNodes, ib, revealToHome: true) is { } home
                && _tree.EnsureDataVisible(ib, home)
                && FindFindInListRow(target) is { } revealed)
            {
                SelectAndReveal(revealed);
                RevealTrace.Log($"Avalonia Reveal: attempt={attempt}, SELECTED after scroll, " +
                                $"target={DescribeAvaloniaTarget(target)}");
                return;
            }

            if (attempt < MaxRevealAttempts)
            {
                RevealTrace.Log($"Avalonia Reveal: attempt={attempt}, not realized — " +
                                $"scheduling attempt={attempt + 1}, target={DescribeAvaloniaTarget(target)}");
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => RevealFindInListAttempt(target, attempt + 1),
                    Avalonia.Threading.DispatcherPriority.Background);
                return;
            }

            // Попытки исчерпаны (issue #31): строка так и не стала видимой — снимаем запрос,
            // чтобы он не перехватывал последующие обычные пересборки.
            _revealFindInListPending = false;
            RevealTrace.Log($"Avalonia Reveal: attempt={attempt} >= MaxRevealAttempts={MaxRevealAttempts}, " +
                            "EXHAUSTED — контейнер домашней строки не найден");
        }

        /// <summary>Описание цели reveal для журнала CM_REVEAL (issue #31).</summary>
        private static string DescribeAvaloniaTarget(object? target) => target switch
        {
            Infobase ib => $"base={ib.Id}",
            GroupNodeViewModel gn => $"group={gn.NodeKey}, marker={gn.Marker ?? "-"}, path={gn.FullPath}",
            _ => target?.ToString() ?? "null"
        };

        /// <summary>Описание домашнего узла для журнала CM_REVEAL (issue #31).</summary>
        private static string DescribeAvaloniaLeaf(GroupNodeViewModel leaf)
            => $"node={leaf.NodeKey}, marker={leaf.Marker ?? "-"}, path={leaf.FullPath}";

        /// <summary>
        /// Восстанавливает прежнюю позицию прокрутки после закрытия окна свойств базы без
        /// сохранения («Нет»). Пересборки дерева не было, поэтому события TreeRebuilding/
        /// TreeRebuilt не сработали и <see cref="RestoreTreeSelection"/> не вызвался, а при
        /// закрытии модального окна Avalonia сама подтягивает выбранную строку в видимую
        /// область — список «уезжает» вверх/вниз (issue #252). Возвращаем точный offset,
        /// запомненный <see cref="RememberTreeScroll"/> перед открытием окна.
        /// </summary>
        private void RestoreTreeScrollAfterCancel()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_treeScrollOffset is { } offset && TreeScroll is { } scroll)
                    scroll.Offset = offset;
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
#endif