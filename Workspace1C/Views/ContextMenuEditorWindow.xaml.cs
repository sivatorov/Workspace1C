#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Редактор структуры контекстного меню базы (issue #56): дерево компоновки
    /// с перемещением пунктов (вверх/вниз/в группу/в корень), отключением, группировкой,
    /// разделителями, сбросом к стандартной компоновке и сохранением/загрузкой в файл
    /// <c>context_menu_layout.json</c>. Упрощённый вариант редактора меню «Утилиты»
    /// (issue #374): без блока «Иконки» (иконки пунктов фиксированы каталогом).
    /// </summary>
    public partial class ContextMenuEditorWindow : Window
    {
        private readonly ContextMenuEditorViewModel _vm;

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public ContextMenuEditorWindow(IContextMenuLayoutStore? store)
        {
            InitializeComponent();
            _vm = new ContextMenuEditorViewModel(store);
            DataContext = _vm;
            Owner = Application.Current?.MainWindow;
            // issue #146 (1.0.1.116): высота окна не должна выходить за рабочую область.
            ApplyWorkAreaLimits();
            // Сразу выбираем первую группу-назначение (если она есть), чтобы «В группу»
            // работал без лишнего клика.
            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
            Loaded += (_, _) => ExpandAll(MenuTree.Items);
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public Workspace1C.Models.ContextMenuLayout Layout => _vm.Layout;

        /// <summary>Синхронизирует выбранный узел дерева с моделью представления.</summary>
        private void OnTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedItem = e.NewValue as ContextMenuNodeItem;
        }

        /// <summary>«Новая группа»: диалог имени → команда создания группы.</summary>
        private void OnAddGroupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ContextMenuGroupEditWindow() { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.AddGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Правка группы»: диалог с текущим именем → команда переименования.</summary>
        private void OnEditGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            var dialog = new ContextMenuGroupEditWindow(item.Title) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.EditGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Удалить группу»: подтверждение; при непустой группе — предложение
        /// переместить элементы с подгруппами в корень (issue #56). Разделитель удаляется
        /// сразу, без вопроса — у него нет настроек, и его легко добавить заново.</summary>
        private void OnDeleteGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            // Разделитель: удаляем без подтверждения (issue #56).
            if (string.Equals(item.Node.Kind, "separator", StringComparison.Ordinal))
            {
                _vm.DeleteGroupCommand.Execute(false);
                return;
            }
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var title = LocalizationManager.T("ContextMenu.Editor.DeleteGroupTitle");
            if (item.Children.Count > 0)
            {
                var message = string.Format(
                    LocalizationManager.T("ContextMenu.Editor.DeleteGroupWithItems"),
                    item.Title);
                if (dialogs.Confirm(message, title))
                    _vm.DeleteGroupCommand.Execute(true);
            }
            else
            {
                var message = string.Format(
                    LocalizationManager.T("ContextMenu.Editor.DeleteGroupEmpty"),
                    item.Title);
                if (dialogs.Confirm(message, title))
                    _vm.DeleteGroupCommand.Execute(false);
            }
        }

        /// <summary>«Готово»: сохраняет компоновку и закрывает окно.</summary>
        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveCommand.Execute(null);
            DialogResult = true;
        }

        /// <summary>
        /// Issue #146 (1.0.1.118): рекомендованные размеры окна берутся из профиля
        /// <see cref="DialogWorkAreaPolicy.ContextMenuEditor"/> — высота 570 DIP
        /// помещает панель операций и пояснение без прокрутки, минимальная высота
        /// 320 DIP позволяет заметно уменьшать окно (issue #159, 1.0.1.128), а на
        /// маленьком экране размер клампится по рабочей области (прокрутка —
        /// аварийный вариант).
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var workArea = SystemParameters.WorkArea;
            var size = DialogWorkAreaPolicy.Recommend(
                DialogWorkAreaPolicy.ContextMenuEditor,
                workArea.Height);
            MaxHeight = size.MaxHeight;
            MinHeight = size.MinHeight;
            Height = size.Height;
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