#if LINUX
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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
    /// Окно «Настроить» (issue #11, Avalonia/Linux): модальный чеклист ВСЕХ служб
    /// системы с галочками (уже выбранные отмечены), поиск-фильтр и «Выбрать все/
    /// Сбросить». По OK <see cref="SelectedServices"/> — отмеченные службы; по отмене —
    /// null. Вся логика — в чистой <see cref="ServicesPickerViewModel"/>.
    /// </summary>
    public sealed class ServicesPickerWindow : ModalWindowBase
    {
        private readonly ServicesPickerViewModel _vm;

        public ServicesPickerWindow()
        {
            Title = LocalizationManager.T("Services.Configure");
            Width = 520;
            Height = 560;
            MinWidth = 420;
            MinHeight = 360;
            FontSize = 13;

            var services = AppServices.GetRequiredService<IServiceControllerService>();
            var store = AppServices.GetRequiredService<IServicesStore>();

            _vm = new ServicesPickerViewModel(
                services,
                store,
                action => Dispatcher.UIThread.Post(action));

            DataContext = _vm;

            // ---- Поиск и кнопки «Выбрать все / Сбросить». ----
            var searchLabel = new TextBlock
            {
                Text = LocalizationManager.T("Services.Picker.Search"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var searchBox = new TextBox { Width = 220, Height = 30 };
            searchBox.Bind(TextBox.TextProperty, new Binding("SearchText", BindingMode.TwoWay));
            searchBox.Styled(ControlThemes.ModernTextBox);

            var selectAll = BuildActionButton(LocalizationManager.T("Services.Picker.SelectAll"),
                () => _vm.SelectAllCommand.Execute(null));
            var selectNone = BuildActionButton(LocalizationManager.T("Services.Picker.SelectNone"),
                () => _vm.SelectNoneCommand.Execute(null));

            var searchRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { searchLabel, searchBox, selectAll, selectNone }
            };

            // ---- Сводка «Выбрано: N из M». ----
            var countText = new TextBlock { FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 6) };
            countText.Bind(TextBlock.TextProperty, new Binding("CountText"));

            // ---- Список всех служб с галочками. ----
            var servicesList = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<ServicePickRow>((row, _) =>
                {
                    var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                    check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(ServicePickRow.IsChecked), BindingMode.TwoWay));
                    var name = new TextBlock
                    {
                        Text = row.DisplayName,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    ToolTip.SetTip(name, row.ServiceName);
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { check, name }
                    };
                    return panel;
                })
            };
            servicesList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 26d),
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            servicesList.Bind(ItemsControl.ItemsSourceProperty, new Binding("FilteredServices"));

            // ---- Кнопки OK / Отмена. ----
            var okButton = BuildActionButton(LocalizationManager.T("Common.Ok"), () =>
            {
                _vm.OkCommand.Execute(null);
                DialogResult = true;
                Close();
            }, "IconCheck");
            okButton.MinWidth = 90;
            var cancelButton = BuildActionButton(LocalizationManager.T("Common.Cancel"), () =>
            {
                DialogResult = false;
                Close();
            }, "IconClose");
            cancelButton.MinWidth = 90;

            var buttonsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                // OK / Отмена — над списком одной строкой (issue #11, 7OH 17.10).
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
                Children = { okButton, cancelButton }
            };

            // ---- Компоновка (остаток — список). ----
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(buttonsRow, Dock.Top);
            DockPanel.SetDock(searchRow, Dock.Top);
            DockPanel.SetDock(countText, Dock.Top);
            root.Children.Add(buttonsRow);
            root.Children.Add(searchRow);
            root.Children.Add(countText);
            root.Children.Add(servicesList);
            Content = root;
        }

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        /// <summary>Результат: отмеченные службы (если подтвердили OK), иначе null.</summary>
        public IReadOnlyList<string>? SelectedServices => _vm.OkPressed ? _vm.SelectedNames : null;

        /// <summary>Кнопка с текстом и стандартным оформлением.</summary>
        private static Button BuildActionButton(string text, System.Action onClick, string? iconKey = null)
        {
            var button = new Button
            {
                // P1a аудита UI: значок кнопки — векторная иконка активного набора,
                // а не текстовый глиф «✔»/«✖».
                Content = iconKey is null
                    ? new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    : IconHelper.IconAndText(iconKey, text, UiMetrics.IconControl, "TextPrimaryBrush"),
                Padding = new Thickness(14, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
#endif