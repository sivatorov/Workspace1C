#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Диагностика подключения» (0.3.9.232, Avalonia/Linux, функция 12): карточка
    /// хоста (DNS, IP-адреса, пинг), список портов со состояниями и задержками,
    /// выводы-подсказки и кнопки «Проверить» / «Проверить порты 1С» / «Повторить».
    /// Вся логика — в чистой ViewModel <see cref="NetworkDiagnosticsViewModel"/>;
    /// окно тонкое: привязки и запуск первого прогона.
    /// </summary>
    public sealed class NetworkDiagnosticsWindow : ModalWindowBase
    {
        private readonly NetworkDiagnosticsViewModel _vm;

        public NetworkDiagnosticsWindow(NetworkDiagnosticsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            Title = LocalizationManager.T("Diagnostics.Title");

            // «Проверить порты 1С» открывает диалог портов сервисов (issue #335);
            // после подтверждения — сканирование с выбранными портами.
            _vm.EditPortsRequested += OnEditPortsRequested;
            Width = 860;
            Height = 620;
            MinWidth = 760;
            MinHeight = 460;
            FontSize = 13;
            CanResize = true;

            // Одно поле «Сервер:» (issue #335): редактируемый список — можно ввести адрес
            // вручную (Text → Host) или выбрать известный сервер (SelectedItem → SelectedServer,
            // который подставляет адрес и сохранённый порт).
            var serverCombo = new ComboBox
            {
                Width = 220,
                IsEditable = true,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            serverCombo.Bind(ComboBox.ItemsSourceProperty, new Binding("AvailableServers"));
            serverCombo.Bind(ComboBox.SelectedItemProperty, new Binding("SelectedServer", BindingMode.TwoWay));
            serverCombo.Bind(ComboBox.TextProperty, new Binding("Host", BindingMode.TwoWay));
            serverCombo.Styles.Add(new Style(x => x.OfType<ComboBoxItem>())
            {
                Setters = { new Setter(ComboBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center) }
            });

            var portBox = new TextBox
            {
                Width = 64,
                Padding = new Thickness(6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            portBox.Styled(ControlThemes.ModernTextBox);
            portBox.Bind(TextBox.TextProperty, new Binding("PortText", BindingMode.TwoWay));

            var runButton = BuildActionButton(LocalizationManager.T("Diagnostics.Run"), "IconSearch",
                () => _ = _vm.RunAsync());
            var checkPortsButton = BuildActionButton(LocalizationManager.T("Diagnostics.CheckPorts"), "IconLink",
                () => _ = _vm.CheckPortsAsync());
            var checkRepositoryButton = BuildActionButton(
                LocalizationManager.T("Diagnostics.CheckRepository"), "IconDatabase",
                () => _ = _vm.CheckRepositoryAsync());
            ToolTip.SetTip(checkRepositoryButton,
                LocalizationManager.T("Diagnostics.CheckRepositoryTooltip"));
            var retryButton = BuildActionButton(LocalizationManager.T("Diagnostics.Retry"), "IconRefresh",
                () => _ = _vm.RetryAsync());
            var closeButton = BuildCloseButton();

            // Панель команд (issue #335, 0.3.9.269): поля — на первой строке,
            // кнопки команд — на второй, чтобы при узком окне кнопки не обрезались.
            var fieldsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    Label(LocalizationManager.T("Diagnostics.ServerLabel")),
                    serverCombo,
                    Label(LocalizationManager.T("Diagnostics.Port")),
                    portBox,
                    runButton
                }
            };

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    checkPortsButton,
                    checkRepositoryButton,
                    retryButton,
                    closeButton
                }
            };

            var commandPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    fieldsPanel,
                    buttonsPanel
                }
            };

            // ---- Карточка хоста: DNS, IP-адреса, пинг. ----
            var hostCard = new Border
            {
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 10, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        CardRow("Diagnostics.DnsLabel", "DnsText"),
                        CardRow("Diagnostics.IpAddresses", "IpAddressesText"),
                        CardRow("Diagnostics.PingLabel", "PingText")
                    }
                }
            };
            ThemeBrushes.Bind(hostCard, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(hostCard, Border.BorderBrushProperty, "BorderColorBrush");

            // ---- Список портов (ListBox, как таблицы монитора серверов). ----
            var portsList = new ListBox
            {
                Margin = new Thickness(0, 10, 0, 0),
                ItemTemplate = new FuncDataTemplate<PortDiagnosticRow>((row, _) => BuildPortRow(row))
            };
            portsList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.MinHeightProperty, 34d),
                    new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                }
            });
            portsList.Bind(ListBox.ItemsSourceProperty, new Binding("Ports"));

            // ---- Выводы/подсказки. ----
            var hints = new ItemsControl
            {
                Margin = new Thickness(0, 10, 0, 0),
                ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3)
                })
            };
            hints.Bind(ItemsControl.ItemsSourceProperty, new Binding("Hints"));

            var status = new TextBlock { FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 10, 0, 0) };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));

            var root = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(root, commandPanel, 0);
            Place(root, hostCard, 1);
            Place(root, portsList, 2);
            Place(root, hints, 3);
            Place(root, status, 4);
            Content = root;

            // Автоматический первый прогон (решение п. 8.5 плана).
            _ = _vm.RunAsync();
        }

        /// <summary>Открывает диалог портов 1С и запускает сканирование по подтверждению (issue #335).</summary>
        private void OnEditPortsRequested()
        {
            var dialog = new PortsEditWindow(_vm.BuildPortsForEdit());
            if (!dialog.ShowSync(this) || dialog.Result is not { } ports)
                return;
            _ = _vm.ApplyEditedPortsAndCheckAsync(ports);
        }

        // ===================== Построители =====================

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        /// <summary>Подпись поля панели команд, выровненная по центру строки.</summary>
        private static TextBlock Label(string text) => new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static Control CardRow(string labelKey, string binding)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            var label = new TextBlock { Text = LocalizationManager.T(labelKey), Opacity = 0.65 };
            ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "FormLabelBrush");
            var value = new TextBlock { FontWeight = FontWeight.SemiBold };
            value.Bind(TextBlock.TextProperty, new Binding(binding));
            panel.Children.Add(label);
            panel.Children.Add(value);
            return panel;
        }

        private static Control BuildPortRow(PortDiagnosticRow row)
        {
            var panel = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(new GridLength(90)),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            var cells = new[]
            {
                CellText(row.Port.ToString(), bold: true),
                CellText(row.ServiceName),
                CellText(row.StateText, colorHex: row.IsAvailable ? null : "#DC2626"),
                CellText(row.RttText),
                CellText(row.NoteText)
            };
            for (var i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                panel.Children.Add(cells[i]);
            }
            return panel;
        }

        private static TextBlock CellText(string text, bool bold = false, string? colorHex = null)
        {
            var block = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = colorHex is null ? null : new SolidColorBrush(Color.Parse(colorHex))
            };
            return block;
        }

        private static Button BuildActionButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        // P1b аудита UI: ключ Icon* — векторная иконка активного набора.
                        icon.StartsWith("Icon", StringComparison.Ordinal)
                            ? IconHelper.MakeIcon(icon, UiMetrics.IconControl, "TextPrimaryBrush")
                            : new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
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

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), UiMetrics.IconControl, "TextPrimaryBrush"),
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }
    }
}
#endif