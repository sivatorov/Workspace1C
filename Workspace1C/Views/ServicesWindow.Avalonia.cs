#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Управление службами» (issue #11, Avalonia/Linux): список выбранных служб
    /// (services.json), команды через <c>systemctl</c>, подменю «Все сервисы»,
    /// «Обновить» и «Настроить» (чеклист). Системного диалога на Linux нет — кнопка
    /// скрыта (CanOpenSystemDialog == false); пауза/восстановление не поддерживаются
    /// (кнопки неактивны). Вся логика — в чистой ViewModel
    /// <see cref="ServicesWindowViewModel"/> (платформа — Linux).
    /// </summary>
    public sealed class ServicesWindow : ModalWindowBase
    {
        protected override bool SupportsMaximizeButton => true;

        private readonly ServicesWindowViewModel _vm;
        private readonly TreeView _servicesList;

        public ServicesWindow()
        {
            Title = LocalizationManager.T("Services.Title");
            Width = 900;
            Height = 600;
            MinWidth = 760;
            MinHeight = 400;
            FontSize = 13;
            CanResize = true;
            // Окно модальное (issue #11, 7OH 17.10) — в панели задач не показывается.
            ShowInTaskbar = false;

            var services = AppServices.GetRequiredService<IServiceControllerService>();
            var store = AppServices.GetRequiredService<IServicesStore>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var repository = AppServices.GetRequiredService<IInfobaseRepository>();
            var settings = repository.LoadSettings();

            // Состояние сворачивания групп служб запоминается между сеансами (issue #11,
            // пункт 4): при открытии восстанавливается из AppSettings.CollapsedServiceGroups,
            // при изменении — сохраняется туда же (как CollapsedGroups у списка баз).
            // null — состояние ещё не сохранялось (первый запуск): все группы свёрнуты (C1).
            _vm = new ServicesWindowViewModel(
                services,
                store,
                dialogs,
                action => Dispatcher.UIThread.Post(action),
                ServicePlatform.Linux,
                settings.ServiceGroupsStateInitialized
                    ? settings.CollapsedServiceGroups
                    : null,
                collapsed =>
                {
                    var s = repository.LoadSettings();
                    s.CollapsedServiceGroups = collapsed.ToList();
                    s.ServiceGroupsStateInitialized = true;
                    repository.SaveSettings(s);
                });

            DataContext = _vm;
            _vm.ServicesPickerProvider = ShowServicesPicker;

            // Редактирование групп (issue #11, итерация 2; окно — Задача 3): провайдер
            // открывает модальное окно группы и возвращает готовую группу или null.
            _vm.GroupEditorProvider = ShowGroupEditor;

            // «Перенести в группу» (issue #11, 1.0.0.14): диалог выбора группы.
            _vm.GroupPickerProvider = ShowGroupPicker;

            // ---- Первая строка: «Настроить» и «Системный диалог» (скрыт на Linux). ----
            var configureButton = BuildActionButton(
                LocalizationManager.T("Services.Configure"), "⚙", () => _vm.ConfigureCommand.Execute(null), "@TextSecondaryBrush");
            var systemDialogButton = BuildActionButton(
                LocalizationManager.T("Services.SystemDialog"), "🗔", () => _vm.OpenSystemDialogCommand.Execute(null), "@InfoBrush");
            systemDialogButton.IsVisible = _vm.CanOpenSystemDialog;
            // «Выдать права» (issue #11): на Linux выдача прав не поддерживается — скрыта.
            var grantRightsButton = BuildActionButton(
                LocalizationManager.T("Services.GrantRights"), "🛡", () => _vm.GrantRightsCommand.Execute(null), "@PurpleAccentBrush");
            grantRightsButton.IsVisible = _vm.CanGrantRights;

            var topButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 10),
                Children = { configureButton, systemDialogButton, grantRightsButton }
            };

            // ---- Заголовок списка «Службы». ----
            var listTitle = new TextBlock
            {
                Text = LocalizationManager.T("Services.ListTitle"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // ---- Строка кнопок над списком (issue #11, итерация 2 — группы служб). ----
            // Порядок задаёт пользователь (issue #11, 7OH 17.10): «Переместить выше / ниже»
            // работает для строк обоих типов (службы и группы).
            // «Свернуть все»/«Развернуть все» (issue #11; замечание 7OH: глифы ⏷/⏶ не
            // совпадали с главным окном): те же векторные иконки, что в панели главного
            // окна (MainWindow.Avalonia.Columns.cs — "IconExpandAllBox"/"IconCollapseAllBox"),
            // с разделителем после них (перед «Переместить выше/ниже»).
            var expandAllButton = BuildToolbarIconButton("IconExpandAllBox",
                LocalizationManager.T("Services.ExpandAll"),
                () => _vm.ExpandAllCommand.Execute(null));
            expandAllButton.Bind(Button.IsEnabledProperty, new Binding("CanExpandAllGroups"));
            var collapseAllButton = BuildToolbarIconButton("IconCollapseAllBox",
                LocalizationManager.T("Services.CollapseAll"),
                () => _vm.CollapseAllCommand.Execute(null));
            collapseAllButton.Bind(Button.IsEnabledProperty, new Binding("CanExpandAllGroups"));

            var moveUpButton = BuildIconButton("▲", LocalizationManager.T("Services.MoveUp"),
                () => _vm.MoveUpCommand.Execute(null), "@InfoBrush");
            moveUpButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveUp"));
            var moveDownButton = BuildIconButton("▼", LocalizationManager.T("Services.MoveDown"),
                () => _vm.MoveDownCommand.Execute(null), "@InfoBrush");
            moveDownButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveDown"));
            // Группы служб: «Добавить/Изменить/Удалить группу». Кнопки неактивны, пока окно
            // не установит GroupEditorProvider (окно редактирования группы — Задача 3).
            var addGroupButton = BuildIconButton("📦", LocalizationManager.T("Services.GroupAddHint"),
                () => _vm.AddGroupCommand.Execute(null), "@PurpleAccentBrush");
            addGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanAddGroup"));
            var editGroupButton = BuildIconButton("✎", LocalizationManager.T("Services.GroupEditHint"),
                () => _vm.EditGroupCommand.Execute(null), "@PurpleAccentBrush");
            editGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanEditGroup"));
            // «Перенести в группу» (issue #11, 1.0.0.14): рядом с кнопкой правки.
            var moveToGroupButton = BuildIconButton("⇄", LocalizationManager.T("Services.MoveToGroupHint"),
                () => _vm.MoveToGroupCommand.Execute(null), "@InfoBrush");
            moveToGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanMoveToGroup"));
            var deleteGroupButton = BuildIconButton("🗑", LocalizationManager.T("Services.GroupDeleteHint"),
                () => _vm.DeleteGroupCommand.Execute(null), "@ErrorBrush");
            deleteGroupButton.Bind(Button.IsEnabledProperty, new Binding("CanDeleteGroup"));
            var refreshButton = BuildIconButton("⟳", LocalizationManager.T("Services.Refresh"),
                () => _vm.RefreshCommand.Execute(null), "@InfoBrush");
            // Команды — только цветные иконки, без подписей (замечание 7OH 18.10);
            // подписи — подсказками, в контекстном меню подписи остаются.
            // «Восстановить» — треугольник с палочкой справа (ServiceCommandGlyphs.
            // ContinueGlyph): палочку дорисовать к текущему глифу «Запустить».
            var startButton = BuildCommandIconButton(ServiceCommandGlyphs.StartGlyph,
                LocalizationManager.T("Services.Start"),
                () => _vm.StartCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Start));
            startButton.Bind(Button.IsEnabledProperty, new Binding("CanStart"));
            var stopButton = BuildCommandIconButton(ServiceCommandGlyphs.StopGlyph,
                LocalizationManager.T("Services.Stop"),
                () => _vm.StopCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Stop));
            stopButton.Bind(Button.IsEnabledProperty, new Binding("CanStop"));
            var pauseButton = BuildCommandIconButton(ServiceCommandGlyphs.PauseGlyph,
                LocalizationManager.T("Services.Pause"),
                () => _vm.PauseCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Pause));
            pauseButton.Bind(Button.IsEnabledProperty, new Binding("CanPause"));
            var continueButton = BuildCommandIconButton(ServiceCommandGlyphs.ContinueGlyph,
                LocalizationManager.T("Services.Continue"),
                () => _vm.ContinueCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Continue));
            continueButton.Bind(Button.IsEnabledProperty, new Binding("CanContinue"));
            var restartButton = BuildCommandIconButton(ServiceCommandGlyphs.RestartGlyph,
                LocalizationManager.T("Services.Restart"),
                () => _vm.RestartCommand.Execute(null), ServiceCommandGlyphs.ColorHex(ServiceAction.Restart));
            restartButton.Bind(Button.IsEnabledProperty, new Binding("CanRestart"));

            // Подменю «Все сервисы» (Запустить / Остановить / Перезапустить) со стилями
            // скина и цветными иконками — как контекстное меню основного окна (issue #11).
            Button? allServicesButton = null;
            allServicesButton = BuildActionButton(LocalizationManager.T("Services.AllServices"),
                "▾",
                () =>
                {
                    var menu = new ContextMenu().Styled(ControlThemes.ModernContextMenu);
                    AddRowMenuCommand(menu, "Services.StartAll", _vm.StartAllCommand, "▶", "#16A34A");
                    AddRowMenuCommand(menu, "Services.StopAll", _vm.StopAllCommand, "■", "#DC2626");
                    menu.Items.Add(new Separator());
                    AddRowMenuCommand(menu, "Services.RestartAll", _vm.RestartAllCommand, "⟲", "@InfoBrush");
                    menu.Open(allServicesButton!);
                });

            var actionsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    expandAllButton,
                    collapseAllButton,
                    Divider(),
                    moveUpButton,
                    moveDownButton,
                    Divider(),
                    addGroupButton,
                    editGroupButton,
                    moveToGroupButton,
                    deleteGroupButton,
                    Divider(),
                    refreshButton,
                    Divider(),
                    startButton,
                    stopButton,
                    Divider(),
                    pauseButton,
                    continueButton,
                    Divider(),
                    restartButton,
                    Divider(),
                    allServicesButton
                }
            };

            // ---- Подсказка при пустом списке. ----
            var emptyHint = new TextBlock
            {
                Text = LocalizationManager.T("Services.Empty"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                [!IsVisibleProperty] = new Binding("!HasRows")
            };

            // ---- Список служб — иерархический (issue #11, 1.0.0.14): службы вне групп —
            //      корневые строки, группы — папки со службами внутри, как в основном окне. ----
            _servicesList = new TreeView();
            // Папка-группа (дети — службы группы) и строка-служба — по типу элемента.
            _servicesList.DataTemplates.Add(
                new FuncTreeDataTemplate<ServiceGroupRow>((row, _) => BuildRow(row), o => o.Children));
            _servicesList.DataTemplates.Add(
                new FuncDataTemplate<ServiceInfoRow>((row, _) => BuildRow(row)));
            _servicesList.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
            {
                Setters =
                {
                    new Setter(TreeViewItem.MinHeightProperty, 26d),
                    new Setter(TreeViewItem.PaddingProperty, new Thickness(8, 2)),
                    // Раскрытие папок-групп связано с VM (команды «Свернуть/Развернуть все»).
                    new Setter(TreeViewItem.IsExpandedProperty, new Binding("IsExpanded", BindingMode.TwoWay))
                }
            });
            _servicesList.Bind(ItemsControl.ItemsSourceProperty, new Binding("Rows"));
            _servicesList.Bind(TreeView.SelectedItemProperty, new Binding("SelectedRow", BindingMode.TwoWay));

            // ---- Статус-строка окна. ----
            var statusText = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            statusText.Bind(TextBlock.TextProperty, new Binding("StatusText"));

            // ---- Компоновка (порядок: верх — элементы, низ — статус, остаток — список). ----
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(topButtons, Dock.Top);
            DockPanel.SetDock(listTitle, Dock.Top);
            DockPanel.SetDock(actionsRow, Dock.Top);
            DockPanel.SetDock(emptyHint, Dock.Top);
            DockPanel.SetDock(statusText, Dock.Bottom);
            root.Children.Add(topButtons);
            root.Children.Add(listTitle);
            root.Children.Add(actionsRow);
            root.Children.Add(emptyHint);
            root.Children.Add(statusText);
            root.Children.Add(_servicesList);
            Content = root;

            Opened += async (_, _) => await _vm.LoadAsync();
        }

        /// <summary>Показывает окно модально (синхронно, issue #11).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        /// <summary>«Настроить»: модальный чеклист всех служб; null — отмена.</summary>
        private IReadOnlyList<string>? ShowServicesPicker()
        {
            var window = new ServicesPickerWindow();
            if (!window.ShowSync(this))
                return null;
            return window.SelectedServices;
        }

        /// <summary>
        /// «Добавить/Изменить группу» (issue #11, итерация 2; окно — Задача 3): модальное
        /// окно редактирования группы с уже отфильтрованным списком доступных служб;
        /// null — отмена пользователем. Входной список строится VM
        /// (<see cref="ServicesWindowViewModel.AvailableServicesForGroup"/>), окно лишь
        /// показывает его в «Все службы».
        /// </summary>
        private ServiceGroup? ShowGroupEditor(ServiceGroup? editingGroup, IReadOnlyList<string> availableServices)
        {
            var window = new ServiceGroupEditWindow(editingGroup, availableServices);
            if (!window.ShowSync(this))
                return null;
            return window.Result;
        }

        /// <summary>
        /// «Перенести в группу» (issue #11, 1.0.0.14): диалог выбора места для службы.
        /// null — отмена; «» (пусто) — «Без группы»; иначе — имя группы.
        /// </summary>
        private string? ShowGroupPicker(string serviceName)
        {
            var window = new ServiceGroupPickerWindow(
                serviceName, _vm.GetGroupNames(), _vm.CurrentGroupOf(serviceName));
            if (!window.ShowSync(this))
                return null;
            return window.SelectedGroupName;
        }

        // ===================== Построители =====================

        /// <summary>Кнопка действия: значок (цветной, issue #11) + подпись.</summary>
        private static Button BuildActionButton(string text, string icon, Action onClick, string? iconColorHex = null)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = iconColorHex is null ? null : Themes.ThemeBrushes.FromSpec(iconColorHex)
                        },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Узкая кнопка-иконка с подсказкой.</summary>
        private static Button BuildIconButton(string icon, string tooltip, Action onClick, string? iconColorHex = null)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                    Foreground = iconColorHex is null ? null : Themes.ThemeBrushes.FromSpec(iconColorHex)
                },
                Padding = new Thickness(8, 5)
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>
        /// Кнопка команды службы: только цветная иконка, без подписи (замечание 7OH
        /// 18.10: «выключить надписи у кнопок управления состоянием на панели»);
        /// подпись — подсказкой. По ширине чуть больше кнопки «Добавить группу»
        /// (Padding 13,5 и MinWidth 44 против 8,5 у обычной кнопки-иконки).
        /// </summary>
        private static Button BuildCommandIconButton(string icon, string tooltip, Action onClick, string iconColorHex)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse(iconColorHex))
                },
                Padding = new Thickness(13, 5),
                MinWidth = 44
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>
        /// Кнопка-иконка с векторной иконкой из темы для «Свернуть все»/«Развернуть все»
        /// (issue #11, замечание 7OH: глифы ⏷/⏶ не совпадали с главным окном). Использует
        /// те же ключи иконок, что панель главного окна (MainWindow.Avalonia.Columns.cs,
        /// HeaderIconButton), размер 18 и кисть TextSecondaryBrush — как там.
        /// </summary>
        private static Button BuildToolbarIconButton(string iconKey, string tooltip, Action onClick)
        {
            var button = new Button
            {
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(18), "TextSecondaryBrush"),
                Padding = new Thickness(8, 5)
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Вертикальный разделитель в строке кнопок.</summary>
        private static Border Divider() => new()
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(0, 0, 6, 0)
        };

        /// <summary>
        /// Строка списка (служба или группа, issue #11, итерация 2): кружок статуса
        /// (обновляется при смене StatusColorHex) + иконка-индикатор группы (только для
        /// групп) + отображаемое имя + статус; контекстное меню — по типу строки.
        /// </summary>
        private Control BuildRow(ServiceListRow row)
        {
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            void ApplyStatusColor()
            {
                try
                {
                    dot.Fill = new SolidColorBrush(Color.Parse(row.StatusColorHex));
                }
                catch
                {
                    dot.Fill = Brushes.Gray;
                }
            }

            ApplyStatusColor();
            row.PropertyChanged += OnRowPropertyChanged;

            void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ServiceListRow.StatusColorHex))
                    ApplyStatusColor();
            }

            // Иконка-индикатор группы (видна только для строк-групп).
            var groupIcon = new TextBlock
            {
                Text = "📦",
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Themes.ThemeBrushes.Get("PurpleAccentBrush", "#7C3AED"), // fallback
                IsVisible = row.IsGroup
            };

            var name = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceListRow.DisplayName)));

            var status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(12, 0, 0, 0)
            };
            status.Bind(TextBlock.TextProperty, new Binding(nameof(ServiceListRow.StatusText)));

            // Галочка «Временно отключить» (issue #37): служба (или папка-группа) не
            // попадает в подменю управления службами, пока галочка стоит. Изменение
            // сразу сохраняется в services.json (обработчик VM OnRowTemporarilyDisabledChanged).
            var temporarilyDisabledCheck = new CheckBox
            {
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(temporarilyDisabledCheck, LocalizationManager.T("Services.TemporarilyDisable"));
            temporarilyDisabledCheck.Bind(
                CheckBox.IsCheckedProperty,
                new Binding(nameof(ServiceListRow.IsTemporarilyDisabled), BindingMode.TwoWay));

            // Нижний отступ строки-папки (замечание 7OH 19.10): без него высота строки
            // группы чуть меньше высоты обычной строки службы.
            var grid = new Grid
            {
                Margin = row.IsGroup ? new Thickness(0, 0, 0, 2) : new Thickness(0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(groupIcon, 1);
            Grid.SetColumn(name, 2);
            Grid.SetColumn(status, 3);
            Grid.SetColumn(temporarilyDisabledCheck, 4);
            grid.Children.Add(dot);
            grid.Children.Add(groupIcon);
            grid.Children.Add(name);
            grid.Children.Add(status);
            grid.Children.Add(temporarilyDisabledCheck);

            // Правый клик по строке выбирает её (команды действуют на SelectedRow).
            grid.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(grid).Properties.IsRightButtonPressed)
                {
                    _servicesList.SelectedItem = row;
                    _vm.SelectedRow = row;
                }
            };

            // Контекстное меню строки: для службы — команды службы; для группы — команды
            // группы + «Изменить/Удалить группу» + перемещение (issue #11, итерация 2).
            // Пункты с цветными иконками и стиль скина — как контекстное меню основного
            // окна (1.0.0.14; issue #11, замечание 7OH 18.10).
            var menu = new ContextMenu().Styled(ControlThemes.ModernContextMenu);
            if (row.IsGroup)
            {
                AddRowMenuCommand(menu, "Services.Start", _vm.StartGroupCommand, "▶", "@SuccessBrush");
                AddRowMenuCommand(menu, "Services.Stop", _vm.StopGroupCommand, "■", "@ErrorBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.Pause", _vm.PauseGroupCommand, "❚❚", "@WarningBrush");
                AddRowMenuCommand(menu, "Services.Continue", _vm.ContinueGroupCommand, ServiceCommandGlyphs.ContinueGlyph, "@InfoBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.Restart", _vm.RestartGroupCommand, "⟲", "@InfoBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.EditGroup", _vm.EditGroupCommand, "✎", "@PurpleAccentBrush");
                AddRowMenuCommand(menu, "Services.DeleteGroup", _vm.DeleteGroupCommand, "🗑", "@ErrorBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.MoveUp", _vm.MoveUpCommand, "▲", "@InfoBrush");
                AddRowMenuCommand(menu, "Services.MoveDown", _vm.MoveDownCommand, "▼", "@InfoBrush");
            }
            else
            {
                AddRowMenuCommand(menu, "Services.Start", _vm.StartCommand, "▶", "@SuccessBrush");
                AddRowMenuCommand(menu, "Services.Stop", _vm.StopCommand, "■", "@ErrorBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.Pause", _vm.PauseCommand, "❚❚", "@WarningBrush");
                AddRowMenuCommand(menu, "Services.Continue", _vm.ContinueCommand, ServiceCommandGlyphs.ContinueGlyph, "@InfoBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.Restart", _vm.RestartCommand, "⟲", "@InfoBrush");
                menu.Items.Add(new Separator());
                // «Перенести в группу» (issue #11, 1.0.0.14): только для службы.
                AddRowMenuCommand(menu, "Services.MoveToGroup", _vm.MoveToGroupCommand, "⇄", "@InfoBrush");
                menu.Items.Add(new Separator());
                AddRowMenuCommand(menu, "Services.MoveUp", _vm.MoveUpCommand, "▲", "@InfoBrush");
                AddRowMenuCommand(menu, "Services.MoveDown", _vm.MoveDownCommand, "▼", "@InfoBrush");
            }
            grid.ContextMenu = menu;

            return grid;
        }

        private static void AddRowMenuCommand(
            ContextMenu menu, string textKey, System.Windows.Input.ICommand command,
            string? icon = null, string? colorHex = null)
        {
            // Пункт добавляется с командой VM (не биндингом): контекстное меню лежит вне
            // визуального дерева, команда уже резолвлена в конструкторе строки. Иконка —
            // цветной глиф, как в контекстном меню основного окна (issue #11, 1.0.0.14).
            // Стиль скина ModernMenuItem — чтобы меню выглядело «родно» (issue #11, 7OH 18.10).
            var item = new MenuItem { Header = LocalizationManager.T(textKey), Command = command }
                .Styled(ControlThemes.ModernMenuItem);
            if (icon is not null)
            {
                item.Icon = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = colorHex is null ? null : Themes.ThemeBrushes.FromSpec(colorHex)
                };
            }
            menu.Items.Add(item);
        }
    }
}
#endif