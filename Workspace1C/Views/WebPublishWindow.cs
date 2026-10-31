#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Публикация на веб-сервере» (выпуск 1.0.0.69, Windows/WPF).
    /// Логика — в <see cref="WebPublishViewModel"/>.
    /// </summary>
    public sealed class WebPublishWindow : Window
    {
        private readonly WebPublishViewModel _vm;

        public WebPublishWindow(WebPublishViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "WebPublish.Title", 920, 660);

            var root = WpfUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("WebPublish.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("WebPublish.Base"),
                WpfUi.BoundCombo(_vm.InfobaseNames, nameof(WebPublishViewModel.SelectedIndex), 420)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("WebPublish.Server"),
                // Issue #63: поле выбора сервера расширено — надписи «Apache 2.4 — служба (конфиг)»
                // не обрезаются (пользователь: «Поле выбора сервера - надо сделать шире»).
                WpfUi.BoundCombo(_vm.ServerOptions, nameof(WebPublishViewModel.ServerIndex), 600)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("WebPublish.Name"),
                WpfUi.BoundTextBox(nameof(WebPublishViewModel.VirtualDirectory), 220),
                WpfUi.BoundText(nameof(WebPublishViewModel.Url))));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("WebPublish.Dir"),
                WpfUi.BoundTextBox(nameof(WebPublishViewModel.PhysicalDirectory), 420)));
            var confRow = WpfUi.Wrap(WpfUi.Label("WebPublish.Conf"),
                WpfUi.BoundTextBox(nameof(WebPublishViewModel.ApacheConfPath), 420));
            confRow.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(WebPublishViewModel.IsApache)));
            form.Children.Add(confRow);
            // Выбор конфиг-файла для webinst: основной conf или include-файл (issue #63).
            var confTargetRow = WpfUi.Wrap(WpfUi.Label("WebPublish.ConfTarget"),
                WpfUi.BoundCombo(_vm.ConfTargets, nameof(WebPublishViewModel.ConfTargetIndex), 420));
            confTargetRow.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(WebPublishViewModel.HasConfTargets))
            {
                Converter = new BooleanToVisibilityConverter()
            });
            form.Children.Add(confTargetRow);
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("WebPublish.ConnStr"),
                WpfUi.BoundTextBox(nameof(WebPublishViewModel.ConnectionString), 420)));
            WpfUi.Place(root, WpfUi.Card(form), 1);

            WpfUi.Place(root, WpfUi.ReadOnlyText(nameof(WebPublishViewModel.LogText)), 2);

            var publish = WpfUi.Button("WebPublish.Publish", async (_, _) => await _vm.PublishAsync(), 160, accent: true);
            var delete = WpfUi.Button("WebPublish.Delete", async (_, _) => await OnDeleteAsync(), 170);
            foreach (var b in new[] { publish, delete })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(WebPublishViewModel.IsIdle)));
            var open = WpfUi.Button("WebPublish.Open", (_, _) => OnOpen(), 150);
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(publish, delete, open, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 3);

            Content = root;
        }

        private async System.Threading.Tasks.Task OnDeleteAsync()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (!dialogs.Confirm(string.Format(LocalizationManager.T("WebPublish.ConfirmDelete"), _vm.VirtualDirectory), Title))
                return;
            await _vm.DeleteAsync();
        }

        private void OnOpen()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _vm.Url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _vm.AppendLog(ex.Message);
            }
        }
    }
}
#endif
