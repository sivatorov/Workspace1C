#if LINUX
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Публикация на веб-сервере» (выпуск 1.0.0.69, Avalonia/Linux).
    /// Логика — в <see cref="WebPublishViewModel"/>.
    /// </summary>
    public sealed class WebPublishWindow : ModalWindowBase
    {
        private readonly WebPublishViewModel _vm;

        public WebPublishWindow(WebPublishViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("WebPublish.Title");
            Width = 920;
            Height = 660;
            MinWidth = 720;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("WebPublish.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.Base"),
                AvaloniaUi.BoundCombo(_vm.InfobaseNames, nameof(WebPublishViewModel.SelectedIndex), 420)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.Server"),
                // Issue #63: поле выбора сервера расширено — надписи «Apache 2.4 — служба (конфиг)»
                // не обрезаются (пользователь: «Поле выбора сервера - надо сделать шире»).
                AvaloniaUi.BoundCombo(_vm.ServerOptions, nameof(WebPublishViewModel.ServerIndex), 600)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.Name"),
                AvaloniaUi.BoundTextBox(nameof(WebPublishViewModel.VirtualDirectory), 220),
                AvaloniaUi.BoundText(nameof(WebPublishViewModel.Url))));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.Dir"),
                AvaloniaUi.BoundTextBox(nameof(WebPublishViewModel.PhysicalDirectory), 420)));
            var confRow = AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.Conf"),
                AvaloniaUi.BoundTextBox(nameof(WebPublishViewModel.ApacheConfPath), 420));
            confRow.Bind(IsEnabledProperty, new Binding(nameof(WebPublishViewModel.IsApache)));
            form.Children.Add(confRow);
            // Выбор конфиг-файла для webinst: основной conf или include-файл (issue #63).
            var confTargetRow = AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.ConfTarget"),
                AvaloniaUi.BoundCombo(_vm.ConfTargets, nameof(WebPublishViewModel.ConfTargetIndex), 420));
            confTargetRow.Bind(IsVisibleProperty, new Binding(nameof(WebPublishViewModel.HasConfTargets)));
            form.Children.Add(confTargetRow);
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("WebPublish.ConnStr"),
                AvaloniaUi.BoundTextBox(nameof(WebPublishViewModel.ConnectionString), 420)));
            AvaloniaUi.Place(root, AvaloniaUi.Card(form), 1);

            AvaloniaUi.Place(root, AvaloniaUi.ReadOnlyText(nameof(WebPublishViewModel.LogText)), 2);

            var publish = AvaloniaUi.Button("WebPublish.Publish", async (_, _) => await _vm.PublishAsync(), 160, accent: true);
            var delete = AvaloniaUi.Button("WebPublish.Delete", async (_, _) => await OnDeleteAsync(), 170);
            foreach (var b in new[] { publish, delete })
                b.Bind(IsEnabledProperty, new Binding(nameof(WebPublishViewModel.IsIdle)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(publish, delete, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 3);

            Content = root;
        }

        private async Task OnDeleteAsync()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (!dialogs.Confirm(string.Format(LocalizationManager.T("WebPublish.ConfirmDelete"), _vm.VirtualDirectory),
                    Title ?? string.Empty))
                return;
            await _vm.DeleteAsync();
        }
    }
}
#endif
