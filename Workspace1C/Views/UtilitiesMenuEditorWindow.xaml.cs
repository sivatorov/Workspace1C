#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Microsoft.Win32;

namespace Workspace1C
{
    /// <summary>
    /// Редактор структуры меню «Утилиты» (issue #374): дерево компоновки с перемещением
    /// пунктов, отключением, группировкой, назначением иконок (из каталога/внешнего файла),
    /// сбросом к стандартной компоновке и сохранением/загрузкой в файл
    /// <c>utilities_menu_layout.json</c>.
    /// </summary>
    public partial class UtilitiesMenuEditorWindow : Window
    {
        private readonly UtilitiesMenuEditorViewModel _vm;

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public UtilitiesMenuEditorWindow(IUtilitiesMenuLayoutStore? store)
        {
            InitializeComponent();
            _vm = new UtilitiesMenuEditorViewModel(store);
            DataContext = _vm;
            Owner = Application.Current?.MainWindow;
            // issue #146 (1.0.1.106): высота окна не должна выходить за рабочую область.
            ApplyWorkAreaLimits();
            // Сразу выбираем первую группу-назначение (если она есть), чтобы «В группу»
            // работал без лишнего клика.
            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
            Loaded += (_, _) => ExpandAll(MenuTree.Items);
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public Workspace1C.Models.UtilitiesMenuLayout Layout => _vm.Layout;

        /// <summary>Синхронизирует выбранный узел дерева с моделью представления.</summary>
        private void OnTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedItem = e.NewValue as UtilitiesMenuNodeItem;
        }

        /// <summary>Выбор иконки из внешнего файла: диалог → команда модели представления.</summary>
        private void OnExternalIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("Utilities.Editor.ExternalIconDialogTitle"),
                Filter = LocalizationManager.T("Utilities.Editor.ExternalIconFilter"),
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                _vm.SetExternalIconCommand.Execute(dialog.FileName);
            }
        }

        /// <summary>«Новая группа»: диалог имени/иконки → команда создания группы.</summary>
        private void OnAddGroupClick(object sender, RoutedEventArgs e)
        {
            // issue #146: поле «Отображение» показываем только для группы верхнего
            // уровня; новая группа внутри выбранной всегда будет с подписью.
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons,
                showDisplayMode: !_vm.NewGroupWillBeNested) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.AddGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Правка группы»: диалог с текущим именем/иконкой → команда переименования.</summary>
        private void OnEditGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            // issue #146: в диалог передаём эффективную иконку (как в дереве), а не
            // сохранённый ключ узла — у новой верхнеуровневой группы он пуст.
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons,
                item.Title,
                item.EffectiveIconKey,
                item.EffectiveIconColor,
                item.Node.DisplayMode,
                showDisplayMode: item.Parent is null) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.EditGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Удалить группу»: подтверждение; при непустой группе — предложение
        /// переместить элементы с подгруппами в корень (issue #374). Разделитель удаляется
        /// сразу, без вопроса — у него нет настроек, и его легко добавить заново.</summary>
        private void OnDeleteGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            // Разделитель: удаляем без подтверждения (issue #374).
            if (string.Equals(item.Node.Kind, "separator", StringComparison.Ordinal))
            {
                _vm.DeleteGroupCommand.Execute(false);
                return;
            }
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var title = LocalizationManager.T("Utilities.Editor.DeleteGroupTitle");
            if (item.Children.Count > 0)
            {
                var message = string.Format(
                    LocalizationManager.T("Utilities.Editor.DeleteGroupWithItems"),
                    item.Title);
                if (dialogs.Confirm(message, title))
                    _vm.DeleteGroupCommand.Execute(true);
            }
            else
            {
                var message = string.Format(
                    LocalizationManager.T("Utilities.Editor.DeleteGroupEmpty"),
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
        /// <see cref="DialogWorkAreaPolicy.UtilitiesEditor"/> — высота 660 DIP помещает
        /// пояснение без прокрутки, минимальная высота 340 DIP позволяет заметно
        /// уменьшать окно (issue #159, 1.0.1.128), а на маленьком экране размер
        /// клампится по рабочей области (прокрутка — аварийный вариант).
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var workArea = SystemParameters.WorkArea;
            var size = DialogWorkAreaPolicy.Recommend(
                DialogWorkAreaPolicy.UtilitiesEditor,
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