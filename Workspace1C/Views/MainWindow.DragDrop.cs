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

        // ===================== Приём файлов из проводника (0.3.9.92) =====================
        //
        // Окно целиком принимает drop (AllowDrop=True в XAML). Туннельные
        // PreviewDragOver/PreviewDrop перехватывают ВНЕШНИЕ файлы раньше
        // обработчиков дерева; внутреннее перетаскивание строк/групп
        // (форматы DragFormatInfobase/DragFormatGroup, DataFormats.FileDrop
        // отсутствует) обработчики не трогают — им занимается OnMainTree_*.

        /// <summary>
        /// Курсор «можно бросить» только когда среди перетащенных путей есть
        /// файловая ИБ (каталог с 1Cv8.1CD или сам файл 1Cv8.1CD).
        /// </summary>
        private void OnWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Effects = DroppedBaseDetector.HasAnyBasePath(files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Drop файлов из проводника: пути передаются во вьюмодель, которая
        /// распознаёт базы, отсеивает дубликаты и показывает итог.
        /// </summary>
        private void OnWindow_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            e.Handled = true;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is null || files.Length == 0)
                return;

            _viewModel.AddInfobasesFromDroppedPaths(files);
        }

        private void OnMainTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging || _draggedData is null)
                return;

            // Issue #112: при активном поиске/фильтре дерево пересобрано в плоский
            // список — перетаскивание не запускаем. Ghost-payload (запомненный
            // ранее в MouseDown и не очищенный при ПКМ-меню/пересборке) после
            // снятия фильтра мог «выстрелить» drop-ом в «Без группы» и увезти
            // базу из её группы.
            if (!string.IsNullOrEmpty(_viewModel.SearchText))
            {
                _draggedData = null;
                return;
            }

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Не переопределяем payload по позиции — он зафиксирован в MouseDown.
            var data = new DataObject();
            if (_draggedData is Infobase ib)
                data.SetData(DragFormatInfobase, ib);
            else if (_draggedData is GroupNodeViewModel gn)
                data.SetData(DragFormatGroup, gn);
            else
                return;

            // Дублируем по Type — совместимость с GetData(typeof(...)).
            data.SetData(_draggedData.GetType(), _draggedData);

            _isDragging = true;
            try
            {
                DragDrop.DoDragDrop(MainTree, data, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
                _draggedData = null;
            }
        }

        /// <summary>
        /// Считает количество РОДИТЕЛЬСКИХ (не считая собственного) TreeViewItem
        /// от строки базы до корня TreeView — это и есть число уровней вложенности
        /// групп, чьи ItemsPresenter.Margin реально сдвигают строку вправо.
        /// Собственный TreeViewItem строки базы (лист без детей) в счёт не идёт:
        /// его ItemsPresenter ничего не сдвигает, так как у листа нет дочерних строк.
        /// </summary>
        private static int CountAncestorTreeViewItems(DependencyObject node)
        {
            var depth = 0;
            var skippedOwnContainer = false;
            var parent = VisualTreeHelper.GetParent(node);
            while (parent is not null)
            {
                if (parent is TreeViewItem)
                {
                    if (!skippedOwnContainer)
                        skippedOwnContainer = true;
                    else
                        depth++;
                }
                else if (parent is TreeView)
                {
                    break;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
            return depth;
        }

        private void OnMainTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void OnMainTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            var payload = ResolveDragPayload(e);
            ResolveDropTarget(e.OriginalSource as DependencyObject, out var targetGroup, out _);
            if (payload is null || targetGroup is null)
            {
                e.Handled = true;
                return;
            }

            if (payload is Infobase)
            {
                e.Effects = DragDropEffects.Move;
            }
            else if (payload is GroupNodeViewModel sourceNode && sourceNode.Group is not null)
            {
                var targetId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceNode.Group.Id, targetId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(targetId)
                        || !GroupHierarchyHelper.IsAncestorOrSelf(targetId, sourceNode.Group.Id, _viewModel.Groups)))
                {
                    e.Effects = DragDropEffects.Move;
                }
            }

            e.Handled = true;
        }

        private void OnMainTree_Drop(object sender, DragEventArgs e)
        {
            var payload = ResolveDragPayload(e);
            if (payload is null)
            {
                e.Handled = true;
                return;
            }

            ResolveDropTarget(e.OriginalSource as DependencyObject,
                out var targetGroup, out var insertBefore);

            if (payload is GroupNodeViewModel sourceGroupNode
                && sourceGroupNode.Group is not null
                && targetGroup is not null)
            {
                var newParentId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceGroupNode.Group.Id, newParentId, StringComparison.OrdinalIgnoreCase))
                    _viewModel.MoveGroupUnder(sourceGroupNode.Group, newParentId);

                e.Handled = true;
                return;
            }

            if (payload is Infobase infobase && targetGroup is not null)
            {
                if (string.Equals(targetGroup.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                {
                    _viewModel.MoveInfobaseToGroup(infobase, infobase.Group ?? string.Empty, insertBefore);
                }
                else
                {
                    var path = targetGroup.Group is null
                        ? string.Empty
                        : GroupHierarchyHelper.GetFullPath(targetGroup.Group, _viewModel.Groups);
                    if (insertBefore is not null && ReferenceEquals(insertBefore, infobase))
                        insertBefore = null;
                    _viewModel.MoveInfobaseToGroup(infobase, path, insertBefore);
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// Полезная нагрузка DnD: сначала поле (валидно во время DoDragDrop), затем DataObject.
        /// </summary>
        private object? ResolveDragPayload(DragEventArgs e)
        {
            if (_draggedData is Infobase or GroupNodeViewModel)
                return _draggedData;

            if (e.Data.GetDataPresent(DragFormatGroup))
                return e.Data.GetData(DragFormatGroup);
            if (e.Data.GetDataPresent(DragFormatInfobase))
                return e.Data.GetData(DragFormatInfobase);
            if (e.Data.GetDataPresent(typeof(GroupNodeViewModel)))
                return e.Data.GetData(typeof(GroupNodeViewModel));
            if (e.Data.GetDataPresent(typeof(Infobase)))
                return e.Data.GetData(typeof(Infobase));
            return null;
        }

        /// <summary>
        /// Запоминает точку начала потенциального перетаскивания (из обработчика LButtonDown).
        /// </summary>
        private void CaptureDragStart(MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>
        /// Цель drop: группа + база, перед которой вставить (если курсор над строкой базы).
        /// </summary>
        private static void ResolveDropTarget(
            DependencyObject? source,
            out GroupNodeViewModel? group,
            out Infobase? insertBefore)
        {
            group = null;
            insertBefore = null;
            if (source is null)
                return;

            var item = FindAncestor<TreeViewItem>(source);
            if (item is null)
                return;

            // Строка узла «Закреплённые» несёт обёртку PinnedInfobaseItem
            // (уникальные данные, issue #314) — разворачиваем до реальной базы.
            if (UnwrapInfobase(item.DataContext) is { } ib)
            {
                insertBefore = ib;
                var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(item));
                while (parentItem is not null)
                {
                    if (parentItem.DataContext is GroupNodeViewModel gn)
                    {
                        group = gn;
                        return;
                    }
                    parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(parentItem));
                }
                return;
            }

            if (item.DataContext is GroupNodeViewModel g)
                group = g;
        }

    }
}
#endif
