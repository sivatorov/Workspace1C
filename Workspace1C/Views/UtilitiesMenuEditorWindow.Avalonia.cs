#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
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
    /// Редактор структуры меню «Утилиты» (issue #374): Avalonia/Linux-версия
    /// окна <see cref="UtilitiesMenuEditorWindow"/> (WPF). Дерево компоновки с
    /// перемещением пунктов, отключением, группировкой, назначением иконок
    /// (из каталога/внешнего файла), сбросом к стандартной компоновке и
    /// сохранением/загрузкой в файл <c>utilities_menu_layout.json</c>.
    /// </summary>
    public partial class UtilitiesMenuEditorWindow : ModalWindowBase
    {
        private readonly UtilitiesMenuEditorViewModel _vm;
        private readonly TreeView _tree = new();
        private readonly ComboBox _groupCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
        private readonly ComboBox _iconCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public UtilitiesMenuEditorWindow(IUtilitiesMenuLayoutStore? store)
        {
            Title = LocalizationManager.T("Utilities.Editor.Title");
            Width = 760;
            Height = 560;
            // Минимальные размеры (issue #55): при уменьшении окна дерево и панель
            // операций не сжимаются до обрезки.
            MinWidth = 640;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _vm = new UtilitiesMenuEditorViewModel(store);
            DataContext = _vm;
            Content = BuildLayout();

            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public UtilitiesMenuLayout Layout => _vm.Layout;

        private Control BuildLayout()
        {
            var dock = new DockPanel { Margin = new Thickness(16) };

            // Кнопки «Готово»/«Отмена» внизу (issue #374): «Готово» слева, «Отмена»
            // справа — как в остальных окнах программы (отмена всегда в конце).
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            buttons.Children.Add(BuildConfirmButton(
                LocalizationManager.T("Utilities.Editor.Done"), 130, OnOkClick));
            buttons.Children.Add(BuildCancelButton(110));
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Margin = new Thickness(0, 12, 0, 0);
            dock.Children.Add(buttons);

            // Дерево слева.
            // Issue #61: заголовок дерева — кисть темы (FormLabelBrush), не фиксированный серый.
            var treeHeader = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.TreeHeader"),
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
            var panel = BuildOperationsPanel();
            dock.Children.Add(panel);

            return dock;
        }

        private void BuildTree()
        {
            _tree.ItemsSource = _vm.Roots;
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(UtilitiesMenuNodeItem),
                (item, _) => BuildTreeRow(item as UtilitiesMenuNodeItem),
                item => item is UtilitiesMenuNodeItem node
                    ? node.Children
                    : Array.Empty<UtilitiesMenuNodeItem>());
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var theme) == true
                && theme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }
            _tree.SelectionChanged += (_, _) =>
                _vm.SelectedItem = _tree.SelectedItem as UtilitiesMenuNodeItem;
        }

        /// <summary>Строка узла: чекбокс видимости + иконка + заголовок + пометка вида.</summary>
        private Control BuildTreeRow(UtilitiesMenuNodeItem? node)
        {
            if (node is null)
                return new TextBlock { Text = string.Empty };
            var check = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
            check.Bind(ToggleButton.IsCheckedProperty, new Binding("IsVisible"));
            // issue #4: у группы «Новинки программы» видимость зафиксирована
            // (CanToggleVisibility=false) — чекбокс отключён, менять можно только
            // местоположение.
            check.Bind(CheckBox.IsEnabledProperty, new Binding("CanToggleVisibility"));
            var icon = IconHelper.MakeIcon(node.DisplayIconKey, 16, "TextPrimaryBrush");
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

            // Перемещение.
            panel.Children.Add(SectionHeader("Utilities.Editor.MoveHeader"));
            var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            moveRow.Children.Add(ActionButton("IconArrowUp", "Utilities.Editor.MoveUpTooltip", _vm.MoveUpCommand));
            moveRow.Children.Add(ActionButton("IconArrowDown", "Utilities.Editor.MoveDownTooltip", _vm.MoveDownCommand));
            moveRow.Children.Add(IconClickButton("IconFolderPlus", "Utilities.Editor.AddGroupTooltip", OnAddGroupClick));
            moveRow.Children.Add(IconClickButton("IconMinus", "Utilities.Editor.AddSeparatorTooltip", () => _vm.AddSeparatorCommand.Execute(null)));
            moveRow.Children.Add(IconClickButton("IconPencil", "Utilities.Editor.EditGroupTooltip", OnEditGroupClick, "CanEditGroup"));
            moveRow.Children.Add(IconClickButton("IconDelete", "Utilities.Editor.DeleteGroupTooltip", OnDeleteGroupClick, "CanDeleteGroup"));
            panel.Children.Add(moveRow);

            // Поместить в группу.
            panel.Children.Add(SectionHeader("Utilities.Editor.MoveToGroupHeader", top: 14));
            _groupCombo.ItemsSource = _vm.Groups;
            _groupCombo.ItemTemplate = new FuncDataTemplate<UtilitiesMenuNodeItem>(
                (g, _) => new TextBlock { Text = g.GroupLabel });
            _groupCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedGroup"));
            var groupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            groupRow.Children.Add(_groupCombo);
            groupRow.Children.Add(ActionButton("IconFolderMove", null, _vm.MoveToGroupCommand, caption: "Utilities.Editor.MoveToGroup"));
            panel.Children.Add(groupRow);
            var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            rootRow.Children.Add(ActionButton("IconArrowUp", "Utilities.Editor.MoveToRootTooltip", _vm.MoveToRootCommand, caption: "Utilities.Editor.MoveToRoot"));
            panel.Children.Add(rootRow);

            // Иконки.
            panel.Children.Add(SectionHeader("Utilities.Editor.IconHeader", top: 14));
            _iconCombo.ItemsSource = _vm.CatalogIcons;
            _iconCombo.ItemTemplate = new FuncDataTemplate<UtilitiesIconOption>(
                (o, _) => BuildIconRow(o));
            // В Avalonia нет SelectedValuePath: ключ выбранной иконки пишем в VM вручную.
            _iconCombo.SelectionChanged += (_, _) =>
            {
                if (_iconCombo.SelectedItem is UtilitiesIconOption option)
                    _vm.SelectedIconKey = option.Key;
            };
            var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            iconRow.Children.Add(_iconCombo);
            var applyIconButton = ActionButton("IconOk", null, _vm.SetCatalogIconCommand, caption: "Utilities.Editor.ApplyIcon");
            applyIconButton.Bind(Button.CommandParameterProperty, new Binding("SelectedIconKey"));
            iconRow.Children.Add(applyIconButton);
            panel.Children.Add(iconRow);

            var iconRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            iconRow2.Children.Add(TextButton("Utilities.Editor.ExternalIcon", OnExternalIconClick));
            iconRow2.Children.Add(ActionButton(null, null, _vm.ClearIconCommand, caption: "Utilities.Editor.ClearIcon"));
            panel.Children.Add(iconRow2);

            // Сохранение/загрузка/сброс.
            panel.Children.Add(SectionHeader("Utilities.Editor.FileHeader", top: 14));
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            fileRow.Children.Add(ActionButton("IconSave", null, _vm.SaveCommand, caption: "Utilities.Editor.Save"));
            fileRow.Children.Add(ActionButton("IconFolderDownload", null, _vm.LoadCommand, caption: "Utilities.Editor.Load"));
            panel.Children.Add(fileRow);
            var fileRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            fileRow2.Children.Add(ActionButton("IconRestore", null, _vm.ResetDefaultCommand, caption: "Utilities.Editor.Reset"));
            panel.Children.Add(fileRow2);

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
                Text = LocalizationManager.T("Utilities.Editor.Hint"),
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

        /// <summary>Кнопка-иконка с обработчиком клика (диалоги групп, issue #374).
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

        /// <summary>Строка иконки каталога: превью слева + подпись (issue #374).</summary>
        private Control BuildIconRow(UtilitiesIconOption option)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var icon = IconHelper.MakeIcon(option.Key, 16, "TextPrimaryBrush");
            if (icon is not null)
                row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = option.Label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        /// <summary>Иконка из внешнего файла: системный диалог выбора файла.</summary>
        private void OnExternalIconClick()
        {
            try
            {
                var dialogs = AppServices.GetRequiredService<IDialogService>();
                var path = dialogs.OpenFileDialog(
                    LocalizationManager.T("Utilities.Editor.ExternalIconDialogTitle"),
                    LocalizationManager.T("Utilities.Editor.ExternalIconFilter"));
                if (!string.IsNullOrWhiteSpace(path))
                    _vm.SetExternalIconCommand.Execute(path);
            }
            catch
            {
                // Диалог недоступен — просто не назначаем иконку.
            }
        }

        /// <summary>«Новая группа»: диалог имени/иконки → команда создания группы.</summary>
        private void OnAddGroupClick()
        {
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons);
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.AddGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Правка группы»: диалог с текущим именем/иконкой → команда переименования.</summary>
        private void OnEditGroupClick()
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons, item.Title, item.Node.IconKey);
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.EditGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Удалить группу»: подтверждение; при непустой группе — предложение
        /// переместить элементы с подгруппами в корень (issue #374). Разделитель удаляется
        /// сразу, без вопроса — у него нет настроек, и его легко добавить заново.</summary>
        private void OnDeleteGroupClick()
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

        private void OnOkClick()
        {
            _vm.SaveCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        private IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }
    }
}
#endif