#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Редактор структуры меню системного трея (issue #62): дерево блоков верхнего
    /// уровня с перемещением, включением/отключением (видимость), сменой стиля
    /// блока («Блок меню»/«Подменю»), созданием пользовательских групп с иконками,
    /// разделителями, сбросом к стандартной компоновке и сохранением в файл
    /// <c>tray_menu_layout.json</c>. Фиксированные пункты меню трея
    /// («Открыть программу», «Настройки», «Выход») в дереве не отображаются —
    /// их всегда добавляют адаптеры трея.
    /// </summary>
    public partial class TrayMenuEditorWindow : Window
    {
        private readonly TrayMenuEditorViewModel _vm;

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public TrayMenuEditorWindow(ITrayMenuLayoutStore? store)
        {
            InitializeComponent();
            _vm = new TrayMenuEditorViewModel(store);
            DataContext = _vm;
            Owner = Application.Current?.MainWindow;
            // issue #146 (1.0.1.106): высота окна не должна выходить за рабочую область.
            ApplyWorkAreaLimits();
            Loaded += (_, _) => ExpandAll(TrayMenuTree.Items);
        }

        /// <summary>Компоновка после редактирования (актуальна после «Сохранить»).</summary>
        public Workspace1C.Models.TrayMenuLayout Layout => _vm.Layout;

        /// <summary>Синхронизирует выбранный узел дерева с моделью представления.</summary>
        private void OnTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedItem = e.NewValue as TrayMenuNodeItem;
        }

        /// <summary>«Добавить группу»: диалог имени/иконки → команда создания группы.</summary>
        private void OnAddGroupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.AddGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Переименовать»: диалог с текущим именем/иконкой → команда переименования
        /// пользовательской группы (issue #62). Для стандартных блоков кнопка неактивна.</summary>
        private void OnEditGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null || !_vm.CanEditGroup)
                return;
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons,
                item.Title,
                item.Node.IconKey) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.EditGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Удалить»: удаляет пользовательскую группу или разделитель (без
        /// подтверждения — узлы верхнего уровня и легко добавляются заново; issue #62).</summary>
        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            _vm.DeleteCommand.Execute(null);
        }

        /// <summary>«В группу…»: выбор пользовательской группы → перемещение блока/разделителя
        /// в неё как подменю (issue #62, замечание 7OH).</summary>
        private void OnMoveToGroupClick(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanMoveToGroup)
                return;

            var pick = new TrayMenuGroupPickWindow(_vm.UserGroups) { Owner = this };
            if (pick.ShowDialog() == true && pick.SelectedGroup is not null)
                _vm.MoveSelectedToGroup(pick.SelectedGroup);
        }

        /// <summary>«В корень»: возврат выбранного узла из группы в корень (issue #62).</summary>
        private void OnUnGroupClick(object sender, RoutedEventArgs e)
        {
            _vm.UnGroupSelected();
        }

        /// <summary>«Сохранить»: сохраняет компоновку и закрывает окно (как «Готово» других редакторов).
        /// Сразу после сохранения пересобираем меню трея главного окна — выбранная иконка группы
        /// применяется без перезапуска и без ожидания закрытия всего окна настроек (issue #62).</summary>
        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveCommand.Execute(null);
            if (Application.Current?.MainWindow is MainWindow main)
                main.RebuildTrayMenuNow();
            DialogResult = true;
        }

        private static TrayMenuGroupEditParams ToTrayGroupParams(UtilitiesGroupEditParams source) =>
            new(source.Title, source.IconKey);

        /// <summary>
        /// Issue #146 (1.0.1.106): клампит высоту окна по рабочей области монитора,
        /// чтобы пояснение и кнопки помещались на экранах 768 px с масштабированием.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var workArea = SystemParameters.WorkArea;
            MaxHeight = DialogWorkAreaPolicy.MaxHeight(workArea.Height);
            Height = DialogWorkAreaPolicy.ClampHeight(Height, MinHeight, workArea.Height);
        }

        private void ExpandAll(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = true;
                    ExpandAll(item.Items);
                }
            }
        }
    }
}
#endif