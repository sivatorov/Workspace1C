#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Лицензия» (редакция ПРО 2.x, Windows/WPF). См. docs/pro/PRO-EDITION.md.</summary>
    public sealed class LicenseWindow : Window
    {
        private readonly LicenseViewModel _vm;

        public LicenseWindow(ProFeature? requested = null)
        {
            _vm = new LicenseViewModel(requested);
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Pro.Title", 640, 620);

            var root = WpfUi.Rows(0, 0, 0, -1, 0, 0);
            root.Margin = new Thickness(16);

            var head = new StackPanel();
            if (!string.IsNullOrEmpty(_vm.RequestedText))
                head.Children.Add(WpfUi.Text(_vm.RequestedText, 14, bold: true));
            head.Children.Add(WpfUi.BoundText(nameof(LicenseViewModel.EditionText), 13, secondary: false));
            head.Children.Add(WpfUi.BoundText(nameof(LicenseViewModel.OwnerText)));
            if (!string.IsNullOrEmpty(_vm.CommunityBuildText))
                head.Children.Add(WpfUi.Text(_vm.CommunityBuildText, 12, secondary: true));
            head.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, head, 0);

            var machine = new StackPanel();
            machine.Children.Add(WpfUi.Label("Pro.MachineCode"));
            var codeBox = new TextBox
            {
                Text = _vm.MachineCodeText,
                IsReadOnly = true,
                FontSize = 18,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Width = 260,
                Margin = new Thickness(0, 4, 8, 0)
            };
            var copy = WpfUi.Button("Pro.CopyCode", (_, _) =>
            {
                try { Clipboard.SetText(_vm.MachineCode); _vm.StatusText = Localization.LocalizationManager.T("Pro.CodeCopied"); }
                catch { /* буфер обмена занят другим приложением */ }
            }, 150);
            machine.Children.Add(WpfUi.Wrap(codeBox, copy));
            machine.Children.Add(WpfUi.Label("Pro.HowToBuy"));
            WpfUi.Place(root, WpfUi.Card(machine), 1);

            var keyPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            keyPanel.Children.Add(WpfUi.Label("Pro.KeyLabel"));
            var keyBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 70,
                Margin = new Thickness(0, 4, 0, 6)
            };
            keyBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(LicenseViewModel.KeyText))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            keyPanel.Children.Add(keyBox);
            var activate = WpfUi.Button("Pro.Activate", (_, _) => _vm.Activate(), 140, accent: true);
            var deactivate = WpfUi.Button("Pro.Deactivate", (_, _) =>
            {
                if (MessageBox.Show(this, Localization.LocalizationManager.T("Pro.DeactivateConfirm"),
                        Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _vm.Deactivate();
            }, 160);
            keyPanel.Children.Add(WpfUi.Wrap(activate, deactivate));
            WpfUi.Place(root, keyPanel, 2);

            var features = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };
            features.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(LicenseViewModel.FeaturesText)) { Mode = System.Windows.Data.BindingMode.OneWay });
            features.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            features.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            var featuresPanel = WpfUi.Rows(0, -1);
            WpfUi.Place(featuresPanel, WpfUi.Section("Pro.FeaturesHeader"), 0);
            WpfUi.Place(featuresPanel, features, 1);
            WpfUi.Place(root, featuresPanel, 3);

            WpfUi.Place(root, WpfUi.BoundText(nameof(LicenseViewModel.StatusText), 12, secondary: false), 4);

            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 5);

            Content = root;
        }

        /// <summary>Регистрирует показ окна для <see cref="ProGate"/> (вызывается при старте WPF).</summary>
        public static void RegisterGate()
        {
            ProGate.ShowLicenseDialog = feature =>
            {
                var app = Application.Current;
                if (app is null)
                    return;
                app.Dispatcher.Invoke(() => new LicenseWindow(feature).ShowDialog());
            };
        }
    }
}
#endif
