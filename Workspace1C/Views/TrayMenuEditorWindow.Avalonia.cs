#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Редактор структуры меню системного трея (issue #62): Avalonia/Linux-версия
    /// окна <see cref="TrayMenuEditorWindow"/> (WPF). Дерево блоков верхнего уровня
    /// с перемещением, включением/отключением (видимость), сменой стиля блока
    /// («Блок меню»/«Подменю»), созданием пользовательских групп с иконками,
    /// разделителями, сбросом к стандартной компоновке и сохранением в файл
    /// <c>tray_menu_layout.json</c>.
    /// </summary>
    public partial class TrayMenuEditorWindow : ModalWindowBase
    {
        private readonly TrayMenuEditorViewModel _vm;
        private readonly TreeView _tree = new();
        private readonly ComboBox _styleCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public TrayMenuEditorWindow(ITrayMenuLayoutStore? store)
        {
            Title = LocalizationManager.T("TrayMenu.Title");
            Width = 760;
            Height = 560;
            // Минимальные размеры (issue #55): при уменьшении окна дерево и панель
            // операций не сжимаются до обрезки.
            MinWidth = 640;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _vm = new TrayMenuEditorViewModel(store);
            DataContext = _vm;
            Content = BuildLayout();
        }

        /// <summary>Компоновка после редактирования (актуальна после «Сохранить»).</summary>
        public TrayMenuLayout Layout => _vm.Layout;

        private Control BuildLayout()
        {
            var dock = new DockPanel { Margin = new Thickness(16) };

            // Кнопки «Сохранить»/«Отмена» внизу (issue #62): «Сохранить» слева,
            // «Отмена» справа — как в остальных окнах программы (отмена всегда в конце).
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            buttons.Children.Add(BuildConfirmButton(
                LocalizationManager.T("TrayMenu.Save"), 130, OnOkClick));
            buttons.Children.Add(BuildCancelButton(110));
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Margin = new Thickness(0, 12, 0, 0);
            dock.Children.Add(buttons);

            // Дерево слева.
            // Issue #61: заголовок дерева — кисть темы (FormLabelBrush), не фиксированный серый.
            var treeHeader = new TextBlock
            {
                Text = LocalizationManager.T("TrayMenu.TreeHeader"),
                Margin = new Thickness(4, 4, 4, 8)
            };
            ThemeBrushes.Bind(treeHeader, TextBlock.ForegroundProperty, "FormLabelBrush");
            DockPanel.SetDock(treeHeader, Dock.Top);
            dock.Children.Add(treeHeader);

            var treeBorder = new Border
            {
                Background = TryBrush("CardBackgroundBrush"),
                BorderBrush = TryBrush("BorderColorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 12, 0)
            };
            dock.Children.Add(treeBorder);

            BuildTree();
            treeBorder.Child = _tree;

            // Правая панель операций.
            dock.Children.Add(BuildOperationsPanel());

            return dock;
        }

        private void BuildTree()
        {
            _tree.ItemsSource = _vm.Roots;
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(TrayMenuNodeItem),
                (item, _) => BuildTreeRow(item as TrayMenuNodeItem),
                item => item is TrayMenuNodeItem node
                    ? node.Children
                    : Array.Empty<TrayMenuNodeItem>());
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var theme) == true
                && theme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }
            _tree.SelectionChanged += (_, _) =>
                _vm.SelectedItem = _tree.SelectedItem as TrayMenuNodeItem;
        }

        /// <summary>Строка узла: чекбокс видимости + иконка + заголовок + пометка вида.</summary>
        private Control BuildTreeRow(TrayMenuNodeItem? node)
        {
            if (node is null)
                return new TextBlock { Text = string.Empty };
            var check = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
            check.Bind(ToggleButton.IsCheckedProperty, new Binding("IsVisible"));
            // Превью = применённая геометрия (issue #62): PackIcon-ключ каталога («Magnify») —
            // через UtilitiesMenuCatalog.WpfIconGeometryKey на парный ключ Icon*, иначе
            // IconHelper резолвил бы его в запасную «папку», и редактор показывал бы
            // «совсем другую» иконку.
            var geometryKey = Workspace1C.Services.UtilitiesMenuCatalog.WpfIconGeometryKey(node.DisplayIconKey);
            if (string.IsNullOrEmpty(geometryKey))
                geometryKey = node.DisplayIconKey;
            var icon = IconHelper.MakeIcon(geometryKey, 16, "TextPrimaryBrush");
            if (icon is not null)
            {
                icon.Margin = new Thickness(4, 0, 0, 0);
                icon.Bind(Control.IsVisibleProperty, new Binding("HasDisplayIcon"));
            }
            var title = new TextBlock { Text = node.Title, Margin = new Thickness(6, 0, 0, 0) };
            title.Bind(TextBlock.TextProperty, new Binding("Title"));
            var kind = new TextBlock
            {
                Text = node.KindLabel,
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            };
            // Issue #61: вторичный текст строки — вторичная кисть темы.
            ThemeBrushes.Bind(kind, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            kind.Bind(TextBlock.TextProperty, new Binding("KindLabel"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            row.Children.Add(check);
            if (icon is not null)
                row.Children.Add(icon);
            row.Children.Add(title);
            row.Children.Add(kind);
            return row;
        }

        private Control BuildOperationsPanel()
        {
            var panel = new StackPanel { Spacing = 6, Width = 250 };

            // Порядок блоков.
            panel.Children.Add(SectionHeader("TrayMenu.MoveHeader"));
            var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            moveRow.Children.Add(ActionButton("IconArrowUp", "TrayMenu.MoveUpTooltip", _vm.MoveUpCommand));
            moveRow.Children.Add(ActionButton("IconArrowDown", "TrayMenu.MoveDownTooltip", _vm.MoveDownCommand));
            moveRow.Children.Add(IconClickButton("IconFolderPlus", "TrayMenu.AddGroupTooltip", OnAddGroupClick));
            moveRow.Children.Add(IconClickButton("IconMinus", "TrayMenu.AddSeparatorTooltip", () => _vm.AddSeparatorCommand.Execute(null)));
            moveRow.Children.Add(IconClickButton("IconPencil", "TrayMenu.EditGroupTooltip", OnEditGroupClick, "CanEditGroup"));
            moveRow.Children.Add(IconClickButton("IconDelete", "TrayMenu.DeleteTooltip", OnDeleteClick, "CanDelete"));
            panel.Children.Add(moveRow);

            // Стиль блока: «Блок меню» / «Подменю».
            panel.Children.Add(SectionHeader("TrayMenu.BlockStyleHeader", top: 14));
            _styleCombo.ItemsSource = _vm.StyleOptions;
            _styleCombo.ItemTemplate = new FuncDataTemplate<TrayMenuBlockStyle>(
                (s, _) => new TextBlock
                {
                    Text = s == TrayMenuBlockStyle.Block
                        ? LocalizationManager.T("TrayMenu.StyleBlock")
                        : LocalizationManager.T("TrayMenu.StyleSubmenu")
                });
            _styleCombo.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding("SelectedBlockStyle", BindingMode.TwoWay));
            panel.Children.Add(_styleCombo);
            var styleHint = new TextBlock
            {
                Text = LocalizationManager.T("TrayMenu.StyleHint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };
            ThemeBrushes.Bind(styleHint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(styleHint);

            // Перемещение блоков в пользовательскую группу (issue #62, замечание 7OH:
            // «Создал группу, но переместить туда 2 блока не смог»).
            var moveGroupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var moveToGroupButton = TextButton("TrayMenu.MoveToGroup", OnMoveToGroupClick);
            moveToGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveToGroup"));
            var unGroupButton = TextButton("TrayMenu.UnGroup", OnUnGroupClick);
            unGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanUnGroup"));
            moveGroupRow.Children.Add(moveToGroupButton);
            moveGroupRow.Children.Add(unGroupButton);
            panel.Children.Add(moveGroupRow);

            // Переименование / удаление.
            panel.Children.Add(SectionHeader("TrayMenu.GroupsHeader", top: 14));
            var editGroupButton = TextButton("TrayMenu.EditGroup", OnEditGroupClick);
            editGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanEditGroup"));
            panel.Children.Add(editGroupButton);
            var deleteButton = TextButton("TrayMenu.Delete", OnDeleteClick);
            deleteButton.Bind(Button.IsEnabledProperty, new Binding("CanDelete"));
            panel.Children.Add(deleteButton);

            // Сброс.
            panel.Children.Add(ActionButton("IconRestore", null, _vm.ResetDefaultCommand, caption: "TrayMenu.Reset"));

            // Пояснение.
            var hint = new Border
            {
                Background = TryBrush("CardBackgroundBrush"),
                BorderBrush = TryBrush("BorderColorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 14, 0, 0)
            };
            var hintBlock = new TextBlock
            {
                Text = LocalizationManager.T("TrayMenu.Hint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };
            ThemeBrushes.Bind(hintBlock, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            hint.Child = hintBlock;
            panel.Children.Add(hint);

            return panel;
        }

        private TextBlock SectionHeader(string key, double top = 0)
        {
            var text = new TextBlock
            {
                Text = LocalizationManager.T(key),
                Margin = new Thickness(0, top, 0, 4)
            };
            // Issue #61: заголовок секции — единая кисть FormLabelBrush.
            ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "FormLabelBrush");
            return text;
        }

        private Button ActionButton(string? iconKey, string? tooltipKey, System.Windows.Input.ICommand command, string? caption = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (iconKey is not null)
                content.Children.Add(IconHelper.MakeIconForButton(iconKey, 16));
            if (caption is not null)
                content.Children.Add(new TextBlock { Text = LocalizationManager.T(caption), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            if (tooltipKey is not null)
                ToolTip.SetTip(button, LocalizationManager.T(tooltipKey));
            button.Command = command;
            return button;
        }

        /// <summary>Кнопка-иконка с обработчиком клика (диалоги групп, issue #62).
        /// <paramref name="enabledBinding"/> — имя свойства ViewModel для IsEnabled (или null).</summary>
        private Button IconClickButton(string iconKey, string? tooltipKey, Action onClick, string? enabledBinding = null)
        {
            var button = new Button
            {
                Content = IconHelper.MakeIconForButton(iconKey, 16),
                Padding = new Thickness(10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            if (tooltipKey is not null)
                ToolTip.SetTip(button, LocalizationManager.T(tooltipKey));
            if (enabledBinding is not null)
                button.Bind(Button.IsEnabledProperty, new Binding(enabledBinding));
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button TextButton(string captionKey, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T(captionKey),
                    VerticalAlignment = VerticalAlignment.Center
                },
                Padding = new Thickness(10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildConfirmButton(string caption, double width, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center },
                Width = width,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildCancelButton(double width)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("Common.Cancel"),
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = width,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>«Добавить группу»: диалог имени/иконки → команда создания группы.</summary>
        private void OnAddGroupClick()
        {
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons);
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.AddGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Переименовать»: диалог с текущим именем/иконкой → команда
        /// переименования пользовательской группы (issue #62).</summary>
        private void OnEditGroupClick()
        {
            var item = _vm.SelectedItem;
            if (item is null || !_vm.CanEditGroup)
                return;
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons, item.Title, item.Node.IconKey);
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.EditGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Удалить»: удаляет пользовательскую группу или разделитель (issue #62).</summary>
        private void OnDeleteClick()
        {
            _vm.DeleteCommand.Execute(null);
        }

        /// <summary>«В группу…»: выбор пользовательской группы → перемещение блока/разделителя
        /// в неё как подменю (issue #62, замечание 7OH).</summary>
        private void OnMoveToGroupClick()
        {
            if (!_vm.CanMoveToGroup)
                return;

            var pick = new TrayMenuGroupPickWindow(_vm.UserGroups);
            if (pick.ShowDialogSync(this) && pick.SelectedGroup is not null)
                _vm.MoveSelectedToGroup(pick.SelectedGroup);
        }

        /// <summary>«В корень»: возврат выбранного узла из группы в корень (issue #62).</summary>
        private void OnUnGroupClick()
        {
            _vm.UnGroupSelected();
        }

        /// <summary>«Сохранить»: сохраняет компоновку и закрывает окно. Сразу после сохранения
        /// пересобираем меню трея главного окна — выбранная иконка группы доезжает до трея
        /// без перезапуска (issue #62). RefreshTrayMenuAfterSettings сбрасывает отпечаток
        /// состава, иначе RefreshTrayMenu отбросил бы пересборку: иконка не меняет структуру.</summary>
        private void OnOkClick()
        {
            _vm.SaveCommand.Execute(null);
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                { MainWindow: MainWindow main })
                main.RefreshTrayMenuAfterSettings();
            DialogResult = true;
            Close();
        }

        private static TrayMenuGroupEditParams ToTrayGroupParams(UtilitiesGroupEditParams source) =>
            new(source.Title, source.IconKey);

        private IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }
    }
}
#endif