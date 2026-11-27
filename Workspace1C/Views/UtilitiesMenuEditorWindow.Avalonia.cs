#if LINUX
using System;
using System.Linq;
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
            Width = 820;
            // Issue #159 (1.0.1.123): окно растягивается по содержимому
            // (SizeToContent.Height) — постоянная внешняя прокрутка на Linux исчезает.
            // MaxHeight/MinHeight уточняются в ApplyWorkAreaLimits по рабочей области;
            // дерево при длинном списке прокручивается само.
            SizeToContent = Avalonia.Controls.SizeToContent.Height;
            // Минимальные размеры (issue #55): при уменьшении окна дерево и панель
            // операций не сжимаются до обрезки.
            MinWidth = 640;
            MinHeight = DialogWorkAreaPolicy.UtilitiesEditor.MinHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _vm = new UtilitiesMenuEditorViewModel(store);
            DataContext = _vm;
            Content = BuildLayout();
            // issue #146 (1.0.1.106): высота окна не должна выходить за рабочую область.
            ApplyWorkAreaLimits();

            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public UtilitiesMenuLayout Layout => _vm.Layout;

        /// <summary>
        /// Issue #159: единая точка открытия редактора для обоих входов — ПКМ по кнопке
        /// раздела на панели и кнопка «Настроить структуру…» в настройках. Раньше оба
        /// места глушили исключение пустым <c>catch</c>: падение шаблона или конструктора
        /// выглядело как «редактор не открывается» без единого сообщения. Теперь ошибка
        /// пишется в журнал и показывается пользователю, а не пропадает молча.
        /// </summary>
        /// <param name="owner">Окно-владелец (может быть null).</param>
        public static void ShowEditor(Window? owner)
        {
            try
            {
                var store = AppServices.TryGetService<IUtilitiesMenuLayoutStore>();
                var window = new UtilitiesMenuEditorWindow(store);
                if (owner is not null)
                    window.ShowDialogSync(owner);
                else
                    window.ShowDialogSync();
            }
            catch (Exception ex)
            {
                ReportOpenFailure(ex);
            }
        }

        /// <summary>
        /// Issue #159: ошибка открытия редактора не теряется — пишется в журнал
        /// (<see cref="IAppLogger"/>) и показывается диалогом. В сообщении есть тип и
        /// текст исключения, поэтому причину видно без отладчика.
        /// </summary>
        internal static void ReportOpenFailure(Exception ex)
        {
            try
            {
                AppServices.TryGetService<IAppLogger>()?.Error(
                    "Не удалось открыть редактор меню «Утилиты»", ex);
            }
            catch
            {
                // Журнал — вспомогательная возможность; показ ошибки важнее.
            }

            try
            {
                var message = string.Format(
                    LocalizationManager.T("Utilities.Editor.OpenFailed"),
                    ex.GetType().Name + ": " + ex.Message);
                AppServices.TryGetService<IDialogService>()?.ShowError(
                    message, LocalizationManager.T("Utilities.Editor.OpenFailedTitle"));
            }
            catch
            {
                // Диалог недоступен (например, приложение уже закрывается) —
                // ошибка к этому моменту уже записана в журнал.
            }
        }

        /// <summary>
        /// Issue #146 (1.0.1.118): рекомендованные размеры окна берутся из профиля
        /// <see cref="DialogWorkAreaPolicy.UtilitiesEditor"/> — высота 660 DIP помещает
        /// пояснение без прокрутки, а на маленьком экране клампится по рабочей области
        /// (прокрутка остаётся аварийным вариантом).
        /// Issue #159: расчёт обёрнут в <c>try/catch</c> — у ещё не показанного окна
        /// <see cref="Window.Screens"/> может быть недоступен, и сбой геометрии не должен
        /// мешать открытию редактора (smoke-тест конструирования окна без показа).
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                if (screen is null)
                    return;
                var dipHeight = screen.WorkingArea.Height / screen.Scaling;
                var size = DialogWorkAreaPolicy.Recommend(
                    DialogWorkAreaPolicy.UtilitiesEditor,
                    dipHeight);
                // Issue #159: высота — по содержимому (SizeToContent.Height), поэтому
                // Height не задаём. MaxHeight клампится по рабочей области, MinHeight
                // не превышает её. Дерево получает собственный предел и прокручивается
                // внутри, а не растягивает окно за экран.
                MaxHeight = size.MaxHeight;
                MinHeight = size.MinHeight;
                _tree.MaxHeight = Math.Max(200, size.MaxHeight - 190);
            }
            catch
            {
                // Геометрия — вспомогательная возможность: окно откроется по
                // содержимому с MinHeight профиля.
            }
        }

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

            // Issue #159 (1.0.1.128): дерево и панель операций — в Grid с фиксированными
            // колонками. Раньше дерево было доковано влево (DockPanel по умолчанию
            // Dock.Left), и его ширина равнялась желаемой ширине содержимого: при
            // раскрытии группы список «прыгал» по ширине. В звёздной колонке ширина
            // дерева определяется окном, а не раскрытыми узлами.
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,12,260")
            };

            var treeBorder = new Border
            {
                Background = TryBrush("CardBackgroundBrush"),
                BorderBrush = TryBrush("BorderColorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            BuildTree();
            treeBorder.Child = _tree;
            Grid.SetColumn(treeBorder, 0);
            grid.Children.Add(treeBorder);

            // Правая колонка (issue #146, 1.0.1.118): пояснение — последний элемент
            // прокручиваемого содержимого, сразу за панелью операций (см. BuildOperationsPanel).
            // Пустого места перед пояснением нет; прокрутка — только аварийный режим.
            var right = new ScrollViewer
            {
                Content = BuildOperationsPanel(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            dock.Children.Add(grid);

            return dock;
        }

        private void BuildTree()
        {
            _tree.ItemsSource = _vm.Roots;
            // Issue #159 (1.0.1.127): горизонтальная прокрутка выключена — при
            // раскрытии групп список больше не «скачет» по ширине из-за появившейся
            // горизонтальной полосы; длинные подписи обрезаются внутри строки.
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            // Issue #159 (1.0.1.128): дерево растягивается по родительской колонке —
            // его ширина не зависит от раскрытых узлов.
            _tree.HorizontalAlignment = HorizontalAlignment.Stretch;
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
            // Issue #159 (1.0.1.121): DisplayIconKey — PackIcon-имя каталога («Folder»,
            // «Sync», …), а IconHelper ждёт ключ геометрии Icon*; неизвестный ключ
            // резолвился в запасную «папку», и у всех узлов была одна и та же иконка.
            // Берём то же разрешение, что меню и WPF (WpfIconGeometryKey), — см.
            // TrayMenuEditorWindow.Avalonia.cs.
            var geometryKey = node.DisplayIconGeometryKey;
            if (string.IsNullOrWhiteSpace(geometryKey))
                geometryKey = "IconFolder";
            // Issue #159: цвет — HEX каталога или «@Кисть»; FromSpec разрешает оба и не
            // падает на неизвестной кисти (в отличие от перегрузки MakeIcon(..., string),
            // которая трактует «@AccentBrush» как имя ресурса без префикса).
            var colorSpec = node.DisplayIconColor;
            var icon = string.IsNullOrWhiteSpace(colorSpec)
                ? IconHelper.MakeIcon(geometryKey, 16, "TextPrimaryBrush")
                : IconHelper.MakeIcon(geometryKey, 16, ThemeBrushes.FromSpec(colorSpec));
            if (icon is not null)
            {
                icon.Margin = new Thickness(4, 0, 0, 0);
                icon.Bind(Control.IsVisibleProperty, new Binding("HasDisplayIcon"));
            }
            var title = new TextBlock
            {
                Text = node.Title,
                Margin = new Thickness(6, 0, 0, 0),
                // Issue #159 (1.0.1.128): длинная подпись обрезается многоточием,
                // а не увеличивает измеренную ширину строки и дерева.
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.Bind(TextBlock.TextProperty, new Binding("Title"));
            var kind = new TextBlock
            {
                Text = node.KindLabel,
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Issue #61: вторичный текст строки — вторичная кисть темы.
            ThemeBrushes.Bind(kind, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            kind.Bind(TextBlock.TextProperty, new Binding("KindLabel"));
            // Issue #159 (1.0.1.128): колонка заголовка — звёздная, поэтому
            // TextTrimming работает, а ширина строки не зависит от длины подписи.
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 0);
            content.Children.Add(check);
            if (icon is not null)
            {
                Grid.SetColumn(icon, 1);
                content.Children.Add(icon);
            }
            Grid.SetColumn(title, 2);
            content.Children.Add(title);
            Grid.SetColumn(kind, 3);
            content.Children.Add(kind);

            // Issue #159 (1.0.1.121): кликабельна вся строка узла, а не только глиф.
            // Прозрачный фон обязателен для hit-test (прозрачные области StackPanel
            // событий указателя не получают), Stretch — на всю ширину дерева,
            // MinHeight — высота строки. Иначе после раскрытия группы приходилось
            // «попадать в точку» — маленький шеврон.
            var row = new Border
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 24,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = content
            };
            // Issue #159: ПКМ по любому месту строки выделяет узел — как в WPF-дереве,
            // где выделение по всей строке выполняет сам TreeViewItem.
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsRightButtonPressed)
                    return;
                _tree.SelectedItem = node;
                e.Handled = true;
            };
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
            // Issue #158: шаблон может быть вызван с null (пустой ComboBox) —
            // обращаемся к элементу только после проверки.
            _groupCombo.ItemTemplate = new FuncDataTemplate<UtilitiesMenuNodeItem>(
                (g, _) => new TextBlock { Text = g?.GroupLabel ?? string.Empty });
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
            // issue #146: список с синтетическим слотом текущей иконки узла,
            // если её ключа нет в каталоге, — поле не показывает чужую иконку.
            _iconCombo.ItemsSource = _vm.IconOptions;
            _iconCombo.ItemTemplate = new FuncDataTemplate<UtilitiesIconOption>(
                (o, _) => BuildIconRow(o));
            // В Avalonia нет SelectedValuePath: ключ выбранной иконки пишем в VM вручную.
            _iconCombo.SelectionChanged += (_, _) =>
            {
                if (_iconCombo.SelectedItem is UtilitiesIconOption option)
                    _vm.SelectedIconKey = option.Key;
            };
            // issue #146: выбранная в дереве иконка должна отображаться в поле настройки,
            // даже если её ключа нет в каталоге (тогда это синтетический вариант).
            _vm.PropertyChanged += (_, e) =>
            {
                if (!string.Equals(e.PropertyName,
                        nameof(UtilitiesMenuEditorViewModel.SelectedIconKey), StringComparison.Ordinal))
                    return;
                var selected = _vm.IconOptions.FirstOrDefault(
                    o => string.Equals(o.Key, _vm.SelectedIconKey, StringComparison.Ordinal));
                if (!ReferenceEquals(_iconCombo.SelectedItem, selected))
                    _iconCombo.SelectedItem = selected;
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

            // Issue #146 (1.0.1.118): пояснение — последний элемент прокручиваемого
            // содержимого, вплотную за панелью операций (Margin 14 DIP).
            panel.Children.Add(BuildHint("Utilities.Editor.Hint"));

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
        private Control BuildIconRow(UtilitiesIconOption? option)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            // Issue #158: Avalonia вызывает шаблон и с null (пока ничего не выбрано) —
            // раньше это давало NullReferenceException при открытии редактора.
            if (option is null)
                return row;
            // Issue #146: превью — та же геометрия Icon*, что рисуют меню, дерево и
            // правка группы (единый резолвер WpfIconGeometryKey). Раньше PackIcon-ключ
            // передавался в IconHelper напрямую, и в поле выбора была «другая» иконка.
            var geometryKey = option.GeometryKey;
            if (string.IsNullOrWhiteSpace(geometryKey))
                geometryKey = option.Key;
            var icon = IconHelper.MakeIcon(geometryKey, 16, "TextPrimaryBrush");
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
            // issue #146: поле «Отображение» показываем только для группы верхнего
            // уровня; новая группа внутри выбранной всегда будет с подписью.
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons,
                showDisplayMode: !_vm.NewGroupWillBeNested);
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
            // issue #146: в диалог передаём эффективную иконку (как в дереве), а не
            // сохранённый ключ узла — у новой верхнеуровневой группы он пуст.
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons, item.Title, item.EffectiveIconKey, item.EffectiveIconColor,
                item.Node.DisplayMode, showDisplayMode: item.Parent is null);
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