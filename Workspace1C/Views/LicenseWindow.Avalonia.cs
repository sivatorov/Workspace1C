#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Services.Licensing;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Лицензия» (редакция ПРО 2.x, Avalonia/Linux). См. docs/pro/PRO-EDITION.md.</summary>
    public sealed class LicenseWindow : ModalWindowBase
    {
        private readonly LicenseViewModel _vm;
        private readonly Button _buyButton;

        public LicenseWindow(ProFeature? requested = null)
        {
            _vm = new LicenseViewModel(requested);
            DataContext = _vm;
            UpdateTitle();
            Width = 640;
            Height = 620;
            MinWidth = 520;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            // Issue #119 (T6): метка ★ и список функций обновляются без перезапуска
            // после активации/удаления ключа (общее событие EditionService.Changed).
            EditionService.Changed += OnEditionChanged;
            Closed += OnClosed;

            var root = AvaloniaUi.Rows(0, 0, 0, -1, 0, 0);
            root.Margin = new Thickness(16);

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (!string.IsNullOrEmpty(_vm.RequestedText))
                head.Children.Add(AvaloniaUi.Text(_vm.RequestedText, ThemeManager.FontSizeSubtitle, bold: true));
            head.Children.Add(AvaloniaUi.BoundText(nameof(LicenseViewModel.EditionText), 13, secondary: false));
            head.Children.Add(AvaloniaUi.BoundText(nameof(LicenseViewModel.OwnerText)));
            if (!string.IsNullOrEmpty(_vm.CommunityBuildText))
                head.Children.Add(AvaloniaUi.Text(_vm.CommunityBuildText, 12, secondary: true));

            // Понятное пояснение: что даёт ПРО и что остаётся бесплатным.
            var explanation = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            explanation.Children.Add(AvaloniaUi.Section("Pro.License.WhatProHeader"));
            explanation.Children.Add(AvaloniaUi.Text(LocalizationManager.T("Pro.License.WhatPro"), 12));
            var whatFreeHeader = AvaloniaUi.Section("Pro.License.WhatFreeHeader");
            whatFreeHeader.Margin = new Thickness(0, 6, 0, 0);
            explanation.Children.Add(whatFreeHeader);
            explanation.Children.Add(AvaloniaUi.Text(LocalizationManager.T("Pro.License.WhatFree"), 12, secondary: true));
            head.Children.Add(explanation);

            AvaloniaUi.Place(root, head, 0);

            var machine = new StackPanel();
            machine.Children.Add(AvaloniaUi.Label("Pro.MachineCode"));
            var codeBox = new TextBox
            {
                Text = _vm.MachineCodeText,
                IsReadOnly = true,
                FontSize = ThemeManager.FontSizeTitle,
                FontFamily = new FontFamily(ThemeManager.DefaultMonospaceFontFamily),
                Width = 260
            };
            var copy = AvaloniaUi.Button("Pro.CopyCode", async (_, _) =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard is not null)
                    {
                        await clipboard.SetTextAsync(_vm.MachineCode);
                        _vm.StatusText = LocalizationManager.T("Pro.CodeCopied");
                    }
                }
                catch
                {
                    // Буфер обмена недоступен (Wayland без менеджера буфера) — код можно выделить вручную.
                }
            }, 150);
            _buyButton = AvaloniaUi.Button("Pro.Buy", (_, _) =>
            {
                // Пошаговая инструкция покупки (цена, контакты, активация) вместо GitHub.
                new HowToBuyWindow().ShowDialogSync(this);
            }, 110);
            // «Купить» нужна только без ключа (после активации скрывается без перезапуска).
            _buyButton.IsVisible = !_vm.IsPro;
            machine.Children.Add(AvaloniaUi.Wrap(codeBox, copy, _buyButton));
            machine.Children.Add(AvaloniaUi.Label("Pro.HowToBuy"));
            AvaloniaUi.Place(root, AvaloniaUi.Card(machine), 1);

            var keyPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            keyPanel.Children.Add(AvaloniaUi.Label("Pro.KeyLabel"));
            var keyBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 70,
                Margin = new Thickness(0, 4, 0, 6)
            };
            keyBox.Bind(TextBox.TextProperty, new Binding(nameof(LicenseViewModel.KeyText)) { Mode = BindingMode.TwoWay });
            keyPanel.Children.Add(keyBox);
            var activate = AvaloniaUi.Button("Pro.Activate", (_, _) => _vm.Activate(), 140, accent: true);
            var deactivate = AvaloniaUi.Button("Pro.Deactivate", (_, _) => _vm.Deactivate(), 160);
            keyPanel.Children.Add(AvaloniaUi.Wrap(activate, deactivate));
            AvaloniaUi.Place(root, keyPanel, 2);

            var featuresPanel = AvaloniaUi.Rows(0, -1);
            AvaloniaUi.Place(featuresPanel, AvaloniaUi.Section("Pro.FeaturesHeader"), 0);
            var features = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                FontFamily = new FontFamily(ThemeManager.DefaultMonospaceFontFamily)
            };
            features.Bind(TextBox.TextProperty, new Binding(nameof(LicenseViewModel.FeaturesText)) { Mode = BindingMode.OneWay });
            AvaloniaUi.Place(featuresPanel, features, 1);
            AvaloniaUi.Place(root, featuresPanel, 3);

            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(LicenseViewModel.StatusText), 12, secondary: false), 4);

            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 5);

            Content = root;
        }

        /// <summary>
        /// Заголовок окна с меткой ★ при действительном ключе (LicenseSummary.ProMark).
        /// </summary>
        private void UpdateTitle()
        {
            Title = (_vm.IsPro ? LicenseSummary.ProMark + " " : string.Empty)
                + LocalizationManager.T("Pro.Title");
        }

        /// <summary>
        /// Ключ активировали/удалили — обновляет редакцию, список функций, видимость
        /// «Купить» и метку ★ в заголовке без перезапуска окна.
        /// </summary>
        private void OnEditionChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshEdition();
            else
                Dispatcher.UIThread.Post(RefreshEdition);
        }

        private void RefreshEdition()
        {
            _vm.Refresh();
            _buyButton.IsVisible = !_vm.IsPro;
            UpdateTitle();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            EditionService.Changed -= OnEditionChanged;
            Closed -= OnClosed;
        }

        /// <summary>Регистрирует показ окна для <see cref="ProGate"/> (вызывается при старте Avalonia).</summary>
        public static void RegisterGate()
        {
            ProGate.ShowLicenseDialog = feature =>
            {
                void Show()
                {
                    var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    new LicenseWindow(feature).ShowDialogSync(owner);
                }

                if (Dispatcher.UIThread.CheckAccess())
                    Show();
                else
                    Dispatcher.UIThread.Invoke(Show);
            };
        }
    }
}
#endif
