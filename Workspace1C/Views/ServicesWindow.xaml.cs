using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Конвертер строки #RRGGBB в <see cref="Brush"/> для кружка статуса строки службы.
    /// При сбое — серый (как в построителях иконок меню).
    /// </summary>
    public sealed class BrushFromHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string hex && !string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                // Некорректный цвет — серый.
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Окно «Управление службами» (issue #11, Windows/WPF): список выбранных служб
    /// (services.json), команды Запустить/Остановить/Пауза/Восстановить/Перезапустить,
    /// подменю «Все сервисы», «Обновить», «Настроить» (чеклист всех служб) и
    /// «Системный диалог» (services.msc). Вся логика — в чистой ViewModel
    /// <see cref="ServicesWindowViewModel"/>.
    /// </summary>
    public partial class ServicesWindow : Window
    {
        private readonly ServicesWindowViewModel _vm;

        public ServicesWindow()
        {
            InitializeComponent();

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
                action => Application.Current?.Dispatcher.BeginInvoke(action),
                collapsedServiceGroups: settings.ServiceGroupsStateInitialized
                    ? settings.CollapsedServiceGroups
                    : null,
                collapsedGroupsChanged: collapsed =>
                {
                    var s = repository.LoadSettings();
                    s.CollapsedServiceGroups = collapsed.ToList();
                    s.ServiceGroupsStateInitialized = true;
                    repository.SaveSettings(s);
                });

            DataContext = _vm;
            Title = LocalizationManager.T("Services.Title");
            PreviewKeyDown += OnWindowPreviewKeyDown;
            // Выделение, назначенное VM (после загрузки/переноса), синхронизируется с деревом.
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

            // «Настроить» — модальный чеклист служб системы; результат применяет VM.
            _vm.ServicesPickerProvider = ShowServicesPicker;

            // Редактирование групп (issue #11, итерация 2; окно — Задача 3): провайдер
            // открывает модальное окно группы и возвращает готовую группу или null.
            _vm.GroupEditorProvider = ShowGroupEditor;

            // «Перенести в группу» (issue #11, 1.0.0.14): диалог выбора группы.
            _vm.GroupPickerProvider = ShowGroupPicker;

            Loaded += async (_, _) => await _vm.LoadAsync();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                Close();
                e.Handled = true;
            }
        }

        /// <summary>«Настроить»: модальное окно-чеклист; null — отмена.</summary>
        private IReadOnlyList<string>? ShowServicesPicker()
        {
            var window = new ServicesPickerWindow { Owner = this };
            return window.ShowDialog() == true ? window.SelectedServices : null;
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
            var window = new ServiceGroupEditWindow(editingGroup, availableServices) { Owner = this };
            return window.ShowDialog() == true ? window.Result : null;
        }

        /// <summary>
        /// «Перенести в группу» (issue #11, 1.0.0.14): диалог выбора группы для службы.
        /// null — отмена; «» (пусто) — «Без группы» (корневой список); иначе — имя группы.
        /// </summary>
        private string? ShowGroupPicker(string serviceName)
        {
            var window = new ServiceGroupPickerWindow(
                serviceName, _vm.GetGroupNames(), _vm.CurrentGroupOf(serviceName)) { Owner = this };
            return window.ShowDialog() == true ? window.SelectedGroupName : null;
        }

        // ===================== Кнопки =====================

        private void OnConfigure_Click(object sender, RoutedEventArgs e) => _vm.ConfigureCommand.Execute(null);

        private void OnSystemDialog_Click(object sender, RoutedEventArgs e) => _vm.OpenSystemDialogCommand.Execute(null);

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.RefreshCommand.Execute(null);

        private void OnMoveUp_Click(object sender, RoutedEventArgs e) => _vm.MoveUpCommand.Execute(null);

        private void OnMoveDown_Click(object sender, RoutedEventArgs e) => _vm.MoveDownCommand.Execute(null);

        private void OnGrantRights_Click(object sender, RoutedEventArgs e) => _vm.GrantRightsCommand.Execute(null);

        // Группы служб (issue #11, итерация 2): кнопки «Добавить/Изменить/Удалить группу».
        private void OnAddGroup_Click(object sender, RoutedEventArgs e) => _vm.AddGroupCommand.Execute(null);

        private void OnEditGroup_Click(object sender, RoutedEventArgs e) => _vm.EditGroupCommand.Execute(null);

        private void OnDeleteGroup_Click(object sender, RoutedEventArgs e) => _vm.DeleteGroupCommand.Execute(null);

        /// <summary>
        /// Пункт меню с цветной иконкой и стилем скина ModernMenuItem (issue #11, 7OH
        /// 17.10; стили — как в контекстном меню основного окна, #16). Метод экземплярный:
        /// стиль берётся из ресурсов окна (TryFindResource), как в MainWindow.Hotkeys.cs.
        /// </summary>
        private MenuItem CommandItem(string textKey, string icon, string colorHex, ICommand command) => new()
        {
            Header = LocalizationManager.T(textKey),
            Command = command,
            Style = TryFindResource("ModernMenuItem") as Style,
            Icon = new TextBlock
            {
                Text = icon,
                Foreground = Themes.WpfBrushes.FromSpec(colorHex),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        private void OnStart_Click(object sender, RoutedEventArgs e) => _vm.StartCommand.Execute(null);

        private void OnStop_Click(object sender, RoutedEventArgs e) => _vm.StopCommand.Execute(null);

        private void OnPause_Click(object sender, RoutedEventArgs e) => _vm.PauseCommand.Execute(null);

        private void OnContinue_Click(object sender, RoutedEventArgs e) => _vm.ContinueCommand.Execute(null);

        private void OnRestart_Click(object sender, RoutedEventArgs e) => _vm.RestartCommand.Execute(null);

        /// <summary>Подменю «Все сервисы»: Запустить / Остановить / Перезапустить.</summary>
        private void OnAllServices_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            // issue #11 (7OH 18.10): меню собирается в коде и должно подчиняться скинингу —
            // стиль ModernContextMenu (карточный фон, контур, MinWidth 280), как у остальных
            // контекстных меню главного окна (issue #16).
            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Style = TryFindResource("ModernContextMenu") as Style,
            };
            menu.Items.Add(CommandItem("Services.StartAll", "▶", "@SuccessBrush", _vm.StartAllCommand));
            menu.Items.Add(CommandItem("Services.StopAll", "■", "@ErrorBrush", _vm.StopAllCommand));
            menu.Items.Add(new Separator());
            menu.Items.Add(CommandItem("Services.RestartAll", "⟲", "@InfoBrush", _vm.RestartAllCommand));
            menu.IsOpen = true;
        }

        // ===================== Контекстное меню строки =====================

        /// <summary>
        /// Контекстное меню строки: для службы — команды службы; для группы — команды
        /// группы + «Изменить/Удалить группу» + перемещение (issue #11, итерация 2).
        /// Меню перестраивается при каждом открытии — состав зависит от типа строки.
        /// </summary>
        private void OnServicesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Правый клик по строке должен её выбрать (команды действуют на SelectedRow).
            if (e.OriginalSource is DependencyObject source &&
                FindAncestor<TreeViewItem>(source) is { } item &&
                item.DataContext is ServiceListRow row)
            {
                _vm.SelectedRow = row;
                item.IsSelected = true;
            }

            ServicesList.ContextMenu = BuildRowContextMenu();
        }

        /// <summary>Выбор строки в дереве → VM (команды действуют на SelectedRow).</summary>
        private void OnServicesList_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ServiceListRow row && !ReferenceEquals(row, _vm.SelectedRow))
                _vm.SelectedRow = row;
        }

        // ===================== «Свернуть/Развернуть все» и «Перенести в группу» (1.0.0.14) =====================

        private void OnExpandAll_Click(object sender, RoutedEventArgs e) => _vm.ExpandAllCommand.Execute(null);

        private void OnCollapseAll_Click(object sender, RoutedEventArgs e) => _vm.CollapseAllCommand.Execute(null);

        private void OnMoveToGroup_Click(object sender, RoutedEventArgs e) => _vm.MoveToGroupCommand.Execute(null);

        /// <summary>
        /// VM назначила SelectedRow (загрузка, перенос в группу): синхронизируем дерево —
        /// раскрываем родительскую папку и выделяем строку (отложенно, после layout).
        /// </summary>
        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServicesWindowViewModel.SelectedRow))
                Dispatcher.BeginInvoke(new Action(() => SelectTreeRow(_vm.SelectedRow)));
        }

        private void SelectTreeRow(ServiceListRow? row)
        {
            if (row is null || ServicesList.Items.Count == 0)
                return;
            foreach (var node in ServicesList.Items)
            {
                var item = ServicesList.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
                if (TrySelectTreeRow(item, row))
                    return;
            }
        }

        private bool TrySelectTreeRow(TreeViewItem? item, ServiceListRow row)
        {
            if (item is null)
                return false;
            if (ReferenceEquals(item.DataContext, row))
            {
                item.IsSelected = true;
                item.BringIntoView();
                return true;
            }
            // Целевая строка-группа всегда корневая — внутри другой папки её нет.
            if (row is ServiceGroupRow)
                return false;
            // Папку раскрываем ТОЛЬКО если целевая строка-служба находится внутри неё
            // (иначе при выделении разворачиваются все папки — issue #11).
            if (item.DataContext is ServiceGroupRow group &&
                row is ServiceInfoRow serviceRow &&
                !group.Children.Contains(serviceRow))
            {
                return false;
            }
            if (item.HasItems && item.IsExpanded is false)
                item.IsExpanded = true;
            foreach (var child in item.Items)
            {
                var childItem = item.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (TrySelectTreeRow(childItem, row))
                    return true;
            }
            return false;
        }

        private ContextMenu BuildRowContextMenu()
        {
            // issue #11 (7OH 18.10): меню собирается в коде и должно подчиняться скинингу —
            // стиль ModernContextMenu, как у контекстных меню основного окна (issue #16).
            var menu = new ContextMenu
            {
                Style = TryFindResource("ModernContextMenu") as Style,
            };
            if (_vm.SelectedRow is ServiceGroupRow)
            {
                // Меню группы: команды группы + разделитель + изменить/удалить + перемещение.
                menu.Items.Add(CommandItem("Services.Start", "▶", "@SuccessBrush", _vm.StartGroupCommand));
                menu.Items.Add(CommandItem("Services.Stop", "■", "@ErrorBrush", _vm.StopGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Pause", "❚❚", "@WarningBrush", _vm.PauseGroupCommand));
                menu.Items.Add(CommandItem("Services.Continue", ServiceCommandGlyphs.ContinueGlyph, "@InfoBrush", _vm.ContinueGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Restart", "⟲", "@InfoBrush", _vm.RestartGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.EditGroup", "✎", "@PurpleAccentBrush", _vm.EditGroupCommand));
                menu.Items.Add(CommandItem("Services.DeleteGroup", "🗑", "@ErrorBrush", _vm.DeleteGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.MoveUp", "▲", "@InfoBrush", _vm.MoveUpCommand));
                menu.Items.Add(CommandItem("Services.MoveDown", "▼", "@InfoBrush", _vm.MoveDownCommand));
            }
            else
            {
                menu.Items.Add(CommandItem("Services.Start", "▶", "@SuccessBrush", _vm.StartCommand));
                menu.Items.Add(CommandItem("Services.Stop", "■", "@ErrorBrush", _vm.StopCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Pause", "❚❚", "@WarningBrush", _vm.PauseCommand));
                menu.Items.Add(CommandItem("Services.Continue", ServiceCommandGlyphs.ContinueGlyph, "@InfoBrush", _vm.ContinueCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.Restart", "⟲", "@InfoBrush", _vm.RestartCommand));
                menu.Items.Add(new Separator());
                // «Перенести в группу» (issue #11, 1.0.0.14): только для службы.
                menu.Items.Add(CommandItem("Services.MoveToGroup", "⇄", "@InfoBrush", _vm.MoveToGroupCommand));
                menu.Items.Add(new Separator());
                menu.Items.Add(CommandItem("Services.MoveUp", "▲", "@InfoBrush", _vm.MoveUpCommand));
                menu.Items.Add(CommandItem("Services.MoveDown", "▼", "@InfoBrush", _vm.MoveDownCommand));
            }
            return menu;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            for (var node = current; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is T match)
                    return match;
            }
            return null;
        }
    }
}