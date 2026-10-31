#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно редактирования группы служб (issue #11, итерация 2 — группы служб, релиз
    /// 1.0.0.12; Задача 3, Avalonia/Linux). Форма по требованию 7OH (комментарий к
    /// issue #11 от 13:25): сверху «Сохранить»/«Отмена», ниже «Описание», ниже два
    /// списка — «Управляемый список» и «Все службы» — с кнопками-стрелками между ними
    /// и кнопками «Вверх/Вниз» над управляемым списком. Строится кодом (без XAML),
    /// как ServicesWindow/ServicesPickerWindow; вся логика — в чистой
    /// <see cref="ServiceGroupEditViewModel"/>. Окно модальное; по «Сохранить»
    /// возвращает <see cref="Result"/>, по «Отмена» — null.
    /// </summary>
    public sealed class ServiceGroupEditWindow : ModalWindowBase
    {
        private readonly ServiceGroupEditViewModel _vm;

        /// <param name="editingGroup">Редактируемая группа; null — создание новой.</param>
        /// <param name="availableServices">
        /// Службы общего списка, допустимые для включения (уже отфильтрованы вызывающей
        /// стороной — см. <see cref="ServicesWindowViewModel.AvailableServicesForGroup"/>).
        /// </param>
        public ServiceGroupEditWindow(ServiceGroup? editingGroup, IReadOnlyList<string> availableServices)
        {
            Title = editingGroup is null
                ? LocalizationManager.T("Services.AddGroup")
                : LocalizationManager.T("Services.EditGroup");
            Width = 760;
            Height = 560;
            MinWidth = 640;
            MinHeight = 420;
            FontSize = 13;

            _vm = new ServiceGroupEditViewModel(editingGroup, availableServices);
            DataContext = _vm;
            _vm.CloseRequested += OnVmCloseRequested;

            // ---- Первая строка: «Сохранить» / «Отмена» (7OH 13:25). ----
            var saveButton = BuildActionButton(
                "✔ " + LocalizationManager.T("Services.Save"), () => _vm.SaveCommand.Execute(null));
            saveButton.MinWidth = 110;
            saveButton.Bind(Button.CommandProperty, new Binding("SaveCommand"));
            var cancelButton = BuildActionButton(
                "✖ " + LocalizationManager.T("Services.Cancel"), () => _vm.CancelCommand.Execute(null));
            cancelButton.MinWidth = 110;
            cancelButton.Bind(Button.CommandProperty, new Binding("CancelCommand"));

            var buttonsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 10),
                Children = { saveButton, cancelButton }
            };

            // ---- Поле «Описание» (имя группы, 7OH 13:25). ----
            var nameLabel = new TextBlock
            {
                Text = LocalizationManager.T("Services.GroupName"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var nameBox = new TextBox { Height = 30 };
            nameBox.Bind(TextBox.TextProperty, new Binding("GroupName", BindingMode.TwoWay));
            nameBox.Styled(ControlThemes.ModernTextBox);
            var namePanel = new StackPanel
            {
                Spacing = 0,
                Margin = new Thickness(0, 0, 0, 12),
                Children = { nameLabel, nameBox }
            };

            // ---- Кнопки-иконки ▲/▼ над «Управляемым списком» и стрелки между списками. ----
            var moveUpButton = BuildIconButton("▲", LocalizationManager.T("Services.MoveUp"),
                () => _vm.MoveUpCommand.Execute(null), "@InfoBrush");
            moveUpButton.Bind(Button.CommandProperty, new Binding("MoveUpCommand"));
            var moveDownButton = BuildIconButton("▼", LocalizationManager.T("Services.MoveDown"),
                () => _vm.MoveDownCommand.Execute(null), "@InfoBrush");
            moveDownButton.Bind(Button.CommandProperty, new Binding("MoveDownCommand"));

            var moveLeftButton = BuildIconButton("◀", LocalizationManager.T("Services.MoveLeft"),
                () => _vm.MoveLeftCommand.Execute(null), "@SuccessBrush");
            moveLeftButton.Bind(Button.CommandProperty, new Binding("MoveLeftCommand"));
            var moveRightButton = BuildIconButton("▶", LocalizationManager.T("Services.MoveRight"),
                () => _vm.MoveRightCommand.Execute(null), "@ErrorBrush");
            moveRightButton.Bind(Button.CommandProperty, new Binding("MoveRightCommand"));

            var managedHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
                Children =
                {
                    moveUpButton,
                    moveDownButton,
                    new TextBlock
                    {
                        Text = LocalizationManager.T("Services.ManagedList"),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    }
                }
            };
            var availableHeader = new TextBlock
            {
                Text = LocalizationManager.T("Services.AllAvailable"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // ---- Два списка: слева «Управляемый список», справа «Все службы». ----
            var managedList = new ListBox();
            managedList.Bind(ItemsControl.ItemsSourceProperty, new Binding("MemberServices"));
            managedList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedMember", BindingMode.TwoWay));
            ApplyRowStyle(managedList);

            var availableList = new ListBox();
            availableList.Bind(ItemsControl.ItemsSourceProperty, new Binding("AvailableServices"));
            availableList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedAvailable", BindingMode.TwoWay));
            ApplyRowStyle(availableList);

            var middleButtons = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                // Вертикальное центрирование стрелок между списками (7OH 13:25).
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10, 0),
                Children = { moveLeftButton, moveRightButton }
            };

            var listsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(new GridLength(1, GridUnitType.Star))
                }
            };
            Grid.SetRow(managedHeader, 0);
            Grid.SetColumn(managedHeader, 0);
            Grid.SetRow(availableHeader, 0);
            Grid.SetColumn(availableHeader, 2);
            Grid.SetRow(managedList, 1);
            Grid.SetColumn(managedList, 0);
            Grid.SetRow(middleButtons, 1);
            Grid.SetColumn(middleButtons, 1);
            Grid.SetRow(availableList, 1);
            Grid.SetColumn(availableList, 2);
            listsGrid.Children.Add(managedHeader);
            listsGrid.Children.Add(availableHeader);
            listsGrid.Children.Add(managedList);
            listsGrid.Children.Add(middleButtons);
            listsGrid.Children.Add(availableList);

            // ---- Компоновка (верх — кнопки и описание, остаток — списки). ----
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(buttonsRow, Dock.Top);
            DockPanel.SetDock(namePanel, Dock.Top);
            root.Children.Add(buttonsRow);
            root.Children.Add(namePanel);
            root.Children.Add(listsGrid);
            Content = root;
        }

        /// <summary>Результат «Сохранить»: заполненная группа (имя + члены) или null.</summary>
        public ServiceGroup? Result => _vm.Result;

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        /// <summary>Закрывает модальное окно с результатом: true — сохранено, false — отмена.</summary>
        private void OnVmCloseRequested()
        {
            DialogResult = _vm.IsSaved;
            Close();
        }

        // ===================== Построители =====================

        /// <summary>Кнопка с текстом и стандартным оформлением.</summary>
        private static Button BuildActionButton(string text, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                Padding = new Thickness(14, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Узкая кнопка-иконка с подсказкой (стрелки переноса/порядка, 7OH 13:25).</summary>
        private static Button BuildIconButton(string icon, string tooltip, Action onClick, string iconColorHex)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    Foreground = Themes.ThemeBrushes.FromSpec(iconColorHex)
                },
                Width = 34,
                Height = 30,
                Padding = new Thickness(0)
            };
            ToolTip.SetTip(button, tooltip);
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Единый стиль строк списка (как в ServicesPickerWindow / ServicesWindow).</summary>
        private static void ApplyRowStyle(ListBox list)
        {
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 26d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
        }
    }
}
#endif