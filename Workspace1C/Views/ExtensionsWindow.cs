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
    /// Окно «Расширения конфигурации» (выпуск 1.0.0.69, Windows/WPF): пакетная
    /// установка .cfe в отмеченные базы, список расширений выбранной базы,
    /// выгрузка и удаление расширения. Логика — в <see cref="ExtensionsViewModel"/>.
    /// </summary>
    public sealed class ExtensionsWindow : Window
    {
        private readonly ExtensionsViewModel _vm;

        public ExtensionsWindow(ExtensionsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Extensions.Title", 1180, 760);

            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Слева: базы ----
            var left = WpfUi.Rows(0, 0, -1, 0);
            left.Children.Add(WpfUi.Section("Extensions.Bases"));
            var search = WpfUi.BoundTextBox(nameof(ExtensionsViewModel.SearchText), 220);
            var searchRow = WpfUi.Wrap(WpfUi.Label("Extensions.Search"), search,
                WpfUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                WpfUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90));
            WpfUi.Place(left, searchRow, 1);

            var grid = WpfUi.Grid(_vm.Targets);
            grid.Columns.Add(WpfUi.CheckColumn(nameof(ExtensionTargetRow.IsChecked)));
            grid.Columns.Add(WpfUi.Column("Extensions.ColBase", nameof(ExtensionTargetRow.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("Extensions.ColStatus", nameof(ExtensionTargetRow.Status), new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(ExtensionsViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(left, grid, 2);
            WpfUi.Place(left, WpfUi.BoundText(nameof(ExtensionsViewModel.CheckedSummary)), 3);
            WpfUi.Place(root, left, 0, 0);

            // ---- Справа: список, установка, журнал ----
            // Issue #117 п.3: панель списка расширений перенесена ВЫШЕ панели установки.
            var right = WpfUi.Rows(0, 0, -1);

            var install = new StackPanel();
            install.Children.Add(WpfUi.Section("Extensions.InstallSection"));
            var pathBox = WpfUi.BoundTextBox(nameof(ExtensionsViewModel.CfePath), 340);
            install.Children.Add(WpfUi.Wrap(WpfUi.Label("Extensions.File"), pathBox,
                WpfUi.Button("Common.Browse", (_, _) => OnBrowse(), 90)));
            install.Children.Add(WpfUi.Wrap(WpfUi.Label("Extensions.Name"),
                WpfUi.BoundTextBox(nameof(ExtensionsViewModel.ExtensionName), 260)));
            var installButton = WpfUi.Button("Extensions.Install", async (_, _) => await OnInstallAsync(), 220, accent: true);
            installButton.SetBinding(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsIdle)));
            var useIbcmd = WpfUi.BoundCheckBox("Extensions.UseIbcmd", nameof(ExtensionsViewModel.UseIbcmd));
            useIbcmd.Margin = new Thickness(16, 0, 0, 0);
            install.Children.Add(WpfUi.ButtonRow(installButton, useIbcmd));
            install.Children.Add(WpfUi.Label("Extensions.InstallHint", 11));
            WpfUi.Place(right, WpfUi.Card(install), 1);

            var listPanel = new StackPanel();
            listPanel.Children.Add(WpfUi.Section("Extensions.ListSection"));
            listPanel.Children.Add(WpfUi.BoundText(nameof(ExtensionsViewModel.SelectedBaseText)));
            var list = WpfUi.StringList(_vm.Extensions, nameof(ExtensionsViewModel.SelectedExtension));
            list.Height = 120;
            list.Margin = new Thickness(0, 6, 0, 0);
            listPanel.Children.Add(list);
            var refresh = WpfUi.Button("Extensions.Refresh", async (_, _) => await _vm.RefreshExtensionsAsync(), 140);
            var dump = WpfUi.Button("Extensions.Dump", async (_, _) => await OnDumpAsync(), 140);
            var delete = WpfUi.Button("Extensions.Delete", async (_, _) => await OnDeleteAsync(), 120);
            foreach (var b in new[] { refresh, dump, delete })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsIdle)));
            listPanel.Children.Add(WpfUi.ButtonRow(refresh, dump, delete));
            WpfUi.Place(right, WpfUi.Card(listPanel), 0);

            WpfUi.Place(right, WpfUi.ReadOnlyText(nameof(ExtensionsViewModel.LogText)), 2);
            WpfUi.Place(root, right, 0, 2);

            // ---- Низ ----
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(ExtensionsViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = WpfUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
            // Issue #117 п.3: тихое получение списка расширений выбранной базы
            // при открытии окна (fire-and-forget; ошибки гасятся внутри VM).
            _ = _vm.PrefetchExtensionsAsync();
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private void OnBrowse()
        {
            var path = Dialogs.OpenFileDialog(LocalizationManager.T("Extensions.ChooseFile"),
                LocalizationManager.T("Extensions.FileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                _vm.CfePath = path;
        }

        private async System.Threading.Tasks.Task OnInstallAsync()
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.NoChecked"), Title);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("Extensions.ConfirmInstall"),
                    _vm.ExtensionName, count), Title))
                return;
            await _vm.InstallToCheckedAsync();
        }

        private async System.Threading.Tasks.Task OnDumpAsync()
        {
            if (_vm.SelectedTarget is null || string.IsNullOrWhiteSpace(_vm.ExtensionName))
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.SelectExtension"), Title);
                return;
            }
            var path = Dialogs.SaveFileDialog(LocalizationManager.T("Extensions.Dump"),
                _vm.ExtensionName + ".cfe", LocalizationManager.T("Extensions.FileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                await _vm.DumpSelectedAsync(path);
        }

        private async System.Threading.Tasks.Task OnDeleteAsync()
        {
            if (_vm.SelectedTarget is null || string.IsNullOrWhiteSpace(_vm.ExtensionName))
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.SelectExtension"), Title);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("Extensions.ConfirmDelete"),
                    _vm.ExtensionName, _vm.SelectedTarget.Name), Title))
                return;
            await _vm.DeleteSelectedAsync();
        }
    }
}
#endif
