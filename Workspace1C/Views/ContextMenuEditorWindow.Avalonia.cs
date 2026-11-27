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
    /// Редактор структуры контекстного меню базы (issue #56): Avalonia/Linux-версия
    /// окна <see cref="ContextMenuEditorWindow"/> (WPF). Дерево компоновки с
    /// перемещением пунктов (вверх/вниз/в группу/в корень), отключением, группировкой,
    /// разделителями, сбросом к стандартной компоновке и сохранением/загрузкой в файл
    /// <c>context_menu_layout.json</c>. Упрощённый вариант редактора меню «Утилиты»
    /// (issue #374): без блока «Иконки» (иконки пунктов фиксированы каталогом).
    /// </summary>
    public partial class ContextMenuEditorWindow : ModalWindowBase
    {
        private readonly ContextMenuEditorViewModel _vm;
        private readonly TreeView _tree = new();
        private readonly ComboBox _groupCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public ContextMenuEditorWindow(IContextMenuLayoutStore? store)
        {
            Title = LocalizationManager.T("ContextMenu.Editor.Title");
            Width = 760;
            // Issue #159 (1.0.1.123): окно растягивается по содержимому
            // (SizeToContent.Height) — постоянная внешняя прокрутка на Linux исчезает.
            // MaxHeight/MinHeight уточняются в ApplyWorkAreaLimits по рабочей области.
            SizeToContent = Avalonia.Controls.SizeToContent.Height;
            // Минимальные размеры (issue #55): при уменьшении окна дерево и панель
            // операций не сжимаются до обрезки.
            MinWidth = 640;
            MinHeight = DialogWorkAreaPolicy.ContextMenuEditor.MinHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _vm = new ContextMenuEditorViewModel(store);
            DataContext = _vm;
            Content = BuildLayout();
            // issue #146 (1.0.1.116): высота окна не должна выходить за рабочую область.
            ApplyWorkAreaLimits();

            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public ContextMenuLayout Layout => _vm.Layout;

        /// <summary>
        /// Issue #146 (1.0.1.118): рекомендованные размеры окна берутся из профиля
        /// <see cref="DialogWorkAreaPolicy.ContextMenuEditor"/> — высота 540 DIP
        /// помещает панель операций и пояснение без прокрутки, а на маленьком экране
        /// клампится по рабочей области (прокрутка остаётся аварийным вариантом).
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
                return;
            var dipHeight = screen.WorkingArea.Height / screen.Scaling;
            var size = DialogWorkAreaPolicy.Recommend(
                DialogWorkAreaPolicy.ContextMenuEditor,
                dipHeight);
            // Issue #159: высота — по содержимому (SizeToContent.Height), Height не
            // задаём; MaxHeight клампится по рабочей области, дерево прокручивается
            // внутри и не растягивает окно за экран.
            MaxHeight = size.MaxHeight;
            MinHeight = size.MinHeight;
            _tree.MaxHeight = Math.Max(200, size.MaxHeight - 190);
        }

        private Control BuildLayout()
        {
            var dock = new DockPanel { Margin = new Thickness(16) };

            // Кнопки «Готово»/«Отмена» внизу (issue #56): «Готово» слева, «Отмена»
            // справа — как в остальных окнах программы (отмена всегда в конце).
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            buttons.Children.Add(BuildConfirmButton(
                LocalizationManager.T("ContextMenu.Editor.Done"), 130, OnOkClick));
            buttons.Children.Add(BuildCancelButton(110));
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Margin = new Thickness(0, 12, 0, 0);
            dock.Children.Add(buttons);

            // Дерево слева.
            // Issue #61: заголовок дерева — кисть темы (FormLabelBrush), не фиксированный серый.
            var treeHeader = new TextBlock
            {
                Text = LocalizationManager.T("ContextMenu.Editor.TreeHeader"),
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

            // Правая колонка (issue #146, 1.0.1.118): пояснение — последний элемент
            // прокручиваемого содержимого, сразу за панелью операций (см. BuildOperationsPanel).
            // Пустого места перед пояснением нет; прокрутка — только аварийный режим.
            var right = new ScrollViewer
            {
                Content = BuildOperationsPanel(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            dock.Children.Add(right);

            return dock;
        }

        private void BuildTree()
        {
            _tree.ItemsSource = _vm.Roots;
            // Issue #159 (1.0.1.127): без горизонтальной прокрутки — при раскрытии
            // групп ширина списка больше не скачет из-за появившейся полосы.
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(ContextMenuNodeItem),
                (item, _) => BuildTreeRow(item as ContextMenuNodeItem),
                item => item is ContextMenuNodeItem node
                    ? node.Children
                    : Array.Empty<ContextMenuNodeItem>());
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var theme) == true
                && theme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }
            _tree.SelectionChanged += (_, _) =>
                _vm.SelectedItem = _tree.SelectedItem as ContextMenuNodeItem;
        }

        /// <summary>Строка узла: чекбокс видимости + иконка + заголовок + пометка вида.</summary>
        private Control BuildTreeRow(ContextMenuNodeItem? node)
        {
            if (node is null)
                return new TextBlock { Text = string.Empty };
            var check = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
            check.Bind(ToggleButton.IsCheckedProperty, new Binding("IsVisible"));
            // issue #4: у группы «Новинки программы» видимость зафиксирована
            // (CanToggleVisibility=false) — чекбокс отключён, менять можно только
            // местоположение.
            check.Bind(CheckBox.IsEnabledProperty, new Binding("CanToggleVisibility"));
            // Issue #159 (1.0.1.123): иконка — по ключу геометрии Icon*
            // (DisplayIconGeometryKey). Раньше PackIcon-имя каталога («Refresh»,
            // «Broom») передавалось в IconHelper напрямую и резолвилось в запасную
            // «папку», поэтому у всех узлов была одна и та же иконка.
            var geometryKey = node.DisplayIconGeometryKey;
            if (string.IsNullOrWhiteSpace(geometryKey))
                geometryKey = "IconFolder";
            var colorSpec = node.DisplayIconColor;
            var icon = string.IsNullOrWhiteSpace(colorSpec)
                ? IconHelper.MakeIcon(geometryKey, 16, "TextPrimaryBrush")
                : IconHelper.MakeIcon(geometryKey, 16, ThemeBrushes.FromSpec(colorSpec));
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
            panel.Children.Add(SectionHeader("ContextMenu.Editor.MoveHeader"));
            var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            moveRow.Children.Add(ActionButton("IconArrowUp", "ContextMenu.Editor.MoveUpTooltip", _vm.MoveUpCommand));
            moveRow.Children.Add(ActionButton("IconArrowDown", "ContextMenu.Editor.MoveDownTooltip", _vm.MoveDownCommand));
            moveRow.Children.Add(IconClickButton("IconFolderPlus", "ContextMenu.Editor.AddGroupTooltip", OnAddGroupClick));
            moveRow.Children.Add(IconClickButton("IconMinus", "ContextMenu.Editor.AddSeparatorTooltip", () => _vm.AddSeparatorCommand.Execute(null)));
            moveRow.Children.Add(IconClickButton("IconPencil", "ContextMenu.Editor.EditGroupTooltip", OnEditGroupClick, "CanEditGroup"));
            moveRow.Children.Add(IconClickButton("IconDelete", "ContextMenu.Editor.DeleteGroupTooltip", OnDeleteGroupClick, "CanDeleteGroup"));
            panel.Children.Add(moveRow);

            // Поместить в группу.
            panel.Children.Add(SectionHeader("ContextMenu.Editor.MoveToGroupHeader", top: 14));
            _groupCombo.ItemsSource = _vm.Groups;
            _groupCombo.ItemTemplate = new FuncDataTemplate<ContextMenuNodeItem>(
                (g, _) => new TextBlock { Text = g.GroupLabel });
            _groupCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedGroup"));
            var groupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            groupRow.Children.Add(_groupCombo);
            groupRow.Children.Add(ActionButton("IconFolderMove", null, _vm.MoveToGroupCommand, caption: "ContextMenu.Editor.MoveToGroup"));
            panel.Children.Add(groupRow);
            var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            rootRow.Children.Add(ActionButton("IconArrowUp", "ContextMenu.Editor.MoveToRootTooltip", _vm.MoveToRootCommand, caption: "ContextMenu.Editor.MoveToRoot"));
            panel.Children.Add(rootRow);

            // Сохранение/загрузка/сброс.
            panel.Children.Add(SectionHeader("ContextMenu.Editor.FileHeader", top: 14));
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            fileRow.Children.Add(ActionButton("IconSave", null, _vm.SaveCommand, caption: "ContextMenu.Editor.Save"));
            fileRow.Children.Add(ActionButton("IconFolderDownload", null, _vm.LoadCommand, caption: "ContextMenu.Editor.Load"));
            panel.Children.Add(fileRow);
            var fileRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            fileRow2.Children.Add(ActionButton("IconRestore", null, _vm.ResetDefaultCommand, caption: "ContextMenu.Editor.Reset"));
            panel.Children.Add(fileRow2);

            // Issue #146 (1.0.1.118): пояснение — последний элемент прокручиваемого
            // содержимого, вплотную за панелью операций (Margin 14 DIP).
            panel.Children.Add(BuildHint("ContextMenu.Editor.Hint"));

            return panel;
        }

        /// <summary>
        /// Issue #146 (1.0.1.118): блок пояснения добавляется в конец панели операций
        /// внутри <c>ScrollViewer</c> (без <c>Dock.Bottom</c>) — пустого места перед
        /// пояснением нет.
        /// </summary>
        private Border BuildHint(string key)
        {
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
                Text = LocalizationManager.T(key),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };
            ThemeBrushes.Bind(hintBlock, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            hint.Child = hintBlock;
            return hint;
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

        /// <summary>Кнопка-иконка с обработчиком клика (диалоги групп, issue #56).
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

        /// <summary>«Новая группа»: диалог имени → команда создания группы.</summary>
        private void OnAddGroupClick()
        {
            var dialog = new ContextMenuGroupEditWindow();
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.AddGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Правка группы»: диалог с текущим именем → команда переименования.</summary>
        private void OnEditGroupClick()
        {
            var item = _vm.SelectedItem;
            if (item is null)
                return;
            var dialog = new ContextMenuGroupEditWindow(item.Title);
            dialog.ShowSync(this);
            if (dialog.Result is not null)
                _vm.EditGroupCommand.Execute(dialog.Result);
        }

        /// <summary>«Удалить группу»: подтверждение; при непустой группе — предложение
        /// переместить элементы с подгруппами в корень (issue #56). Разделитель удаляется
        /// сразу, без вопроса — у него нет настроек, и его легко добавить заново.</summary>
        private void OnDeleteGroupClick()
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