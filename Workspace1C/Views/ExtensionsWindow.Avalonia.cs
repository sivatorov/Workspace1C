#if LINUX
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Расширения конфигурации» (выпуск 1.0.0.69, Avalonia/Linux) — пара
    /// WPF-окна: пакетная установка .cfe, список, выгрузка и удаление расширений.
    /// </summary>
    public sealed class ExtensionsWindow : ModalWindowBase
    {
        private readonly ExtensionsViewModel _vm;

        public ExtensionsWindow(ExtensionsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Extensions.Title");
            Width = 1180;
            Height = 760;
            MinWidth = 860;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;

            var root = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions = AvaloniaUi.Columns(-1, 16, -1.2),
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
            };

            // ---- Слева: базы ----
            var left = AvaloniaUi.Rows(0, 0, -1, 0);
            AvaloniaUi.Place(left, AvaloniaUi.Section("Extensions.Bases"), 0);
            AvaloniaUi.Place(left, AvaloniaUi.Wrap(
                AvaloniaUi.Label("Extensions.Search"),
                AvaloniaUi.BoundTextBox(nameof(ExtensionsViewModel.SearchText), 220),
                AvaloniaUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                AvaloniaUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90)), 1);

            var targets = new ListBox { ItemsSource = _vm.Targets };
            targets.ItemTemplate = new FuncDataTemplate<ExtensionTargetRow>((_, _) => BuildTargetRow());
            targets.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ExtensionsViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(left, targets, 2);
            AvaloniaUi.Place(left, AvaloniaUi.BoundText(nameof(ExtensionsViewModel.CheckedSummary)), 3);
            AvaloniaUi.Place(root, left, 0, 0);

            // ---- Справа ----
            // Issue #117 п.3: панель списка расширений перенесена ВЫШЕ панели установки.
            var right = AvaloniaUi.Rows(0, 0, -1);

            var install = new StackPanel();
            install.Children.Add(AvaloniaUi.Section("Extensions.InstallSection"));
            install.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Extensions.File"),
                AvaloniaUi.BoundTextBox(nameof(ExtensionsViewModel.CfePath), 340),
                AvaloniaUi.Button("Common.Browse", (_, _) => OnBrowse(), 90)));
            install.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Extensions.Name"),
                AvaloniaUi.BoundTextBox(nameof(ExtensionsViewModel.ExtensionName), 260)));
            var installButton = AvaloniaUi.Button("Extensions.Install", async (_, _) => await OnInstallAsync(), 220, accent: true);
            installButton.Bind(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsIdle)));
            var useIbcmd = AvaloniaUi.BoundCheckBox("Extensions.UseIbcmd", nameof(ExtensionsViewModel.UseIbcmd));
            useIbcmd.Margin = new Thickness(16, 0, 0, 0);
            install.Children.Add(AvaloniaUi.ButtonRow(installButton, useIbcmd));
            install.Children.Add(AvaloniaUi.Label("Extensions.InstallHint", 11));
            AvaloniaUi.Place(right, AvaloniaUi.Card(install), 1);

            var listPanel = new StackPanel();
            listPanel.Children.Add(AvaloniaUi.Section("Extensions.ListSection"));
            listPanel.Children.Add(AvaloniaUi.BoundText(nameof(ExtensionsViewModel.SelectedBaseText)));
            var list = AvaloniaUi.StringList(_vm.Extensions, nameof(ExtensionsViewModel.SelectedExtension));
            list.Height = 120;
            list.Margin = new Thickness(0, 6, 0, 0);
            listPanel.Children.Add(list);
            var refresh = AvaloniaUi.Button("Extensions.Refresh", async (_, _) => await _vm.RefreshExtensionsAsync(), 140);
            var dump = AvaloniaUi.Button("Extensions.Dump", async (_, _) => await OnDumpAsync(), 140);
            var delete = AvaloniaUi.Button("Extensions.Delete", async (_, _) => await OnDeleteAsync(), 120);
            foreach (var b in new[] { refresh, dump, delete })
                b.Bind(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsIdle)));
            listPanel.Children.Add(AvaloniaUi.ButtonRow(refresh, dump, delete));
            AvaloniaUi.Place(right, AvaloniaUi.Card(listPanel), 0);

            AvaloniaUi.Place(right, AvaloniaUi.ReadOnlyText(nameof(ExtensionsViewModel.LogText)), 2);
            AvaloniaUi.Place(root, right, 0, 2);

            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = AvaloniaUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
            // Issue #117 п.3: тихое получение списка расширений выбранной базы
            // при открытии окна (fire-and-forget; ошибки гасятся внутри VM).
            _ = _vm.PrefetchExtensionsAsync();
        }

        private static Control BuildTargetRow()
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(ExtensionTargetRow.IsChecked)) { Mode = BindingMode.TwoWay });
            var name = AvaloniaUi.Cell(null, 1, bold: true);
            name.Bind(TextBlock.TextProperty, new Binding(nameof(ExtensionTargetRow.Name)));
            var status = AvaloniaUi.Cell(null, 2);
            status.Opacity = 0.75;
            status.Bind(TextBlock.TextProperty, new Binding(nameof(ExtensionTargetRow.Status)));
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(32, -2, -1), Height = 28 };
            grid.Children.Add(check);
            grid.Children.Add(name);
            grid.Children.Add(status);
            return grid;
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private void OnBrowse()
        {
            var path = Dialogs.OpenFileDialog(LocalizationManager.T("Extensions.ChooseFile"),
                LocalizationManager.T("Extensions.FileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                _vm.CfePath = path;
        }

        private async Task OnInstallAsync()
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.NoChecked"), Title ?? string.Empty);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("Extensions.ConfirmInstall"),
                    _vm.ExtensionName, count), Title ?? string.Empty))
                return;
            await _vm.InstallToCheckedAsync();
        }

        private async Task OnDumpAsync()
        {
            if (_vm.SelectedTarget is null || string.IsNullOrWhiteSpace(_vm.ExtensionName))
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.SelectExtension"), Title ?? string.Empty);
                return;
            }
            var path = Dialogs.SaveFileDialog(LocalizationManager.T("Extensions.Dump"),
                _vm.ExtensionName + ".cfe", LocalizationManager.T("Extensions.FileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                await _vm.DumpSelectedAsync(path);
        }

        private async Task OnDeleteAsync()
        {
            if (_vm.SelectedTarget is null || string.IsNullOrWhiteSpace(_vm.ExtensionName))
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.SelectExtension"), Title ?? string.Empty);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("Extensions.ConfirmDelete"),
                    _vm.ExtensionName, _vm.SelectedTarget.Name), Title ?? string.Empty))
                return;
            await _vm.DeleteSelectedAsync();
        }
    }
}
#endif
