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
    /// Окно «Внешние отчёты и обработки» (issue #91, Windows/WPF): загрузка .epf/.erf
    /// в справочник «Дополнительные отчёты и обработки» отмеченных баз.
    /// Логика — в <see cref="ExternalProcessingsViewModel"/>.
    /// </summary>
    public sealed class ExternalProcessingsWindow : Window
    {
        private readonly ExternalProcessingsViewModel _vm;

        public ExternalProcessingsWindow(ExternalProcessingsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "ExtProc.Title", 1180, 760);

            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Слева: базы ----
            var left = WpfUi.Rows(0, 0, -1, 0);
            left.Children.Add(WpfUi.Section("Extensions.Bases"));
            var searchRow = WpfUi.Wrap(WpfUi.Label("Extensions.Search"),
                WpfUi.BoundTextBox(nameof(ExternalProcessingsViewModel.SearchText), 220),
                WpfUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                WpfUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90));
            WpfUi.Place(left, searchRow, 1);
            var grid = WpfUi.Grid(_vm.Targets);
            grid.Columns.Add(WpfUi.CheckColumn(nameof(ExtensionTargetRow.IsChecked)));
            grid.Columns.Add(WpfUi.Column("Extensions.ColBase", nameof(ExtensionTargetRow.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("Extensions.ColStatus", nameof(ExtensionTargetRow.Status), new DataGridLength(1, DataGridLengthUnitType.Star)));
            WpfUi.Place(left, grid, 2);
            WpfUi.Place(left, WpfUi.BoundText(nameof(ExternalProcessingsViewModel.CheckedSummary)), 3);
            WpfUi.Place(root, left, 0, 0);

            // ---- Справа: файлы, загрузка, журнал ----
            var right = WpfUi.Rows(0, -1);

            var filesPanel = new StackPanel();
            filesPanel.Children.Add(WpfUi.Section("ExtProc.FilesSection"));
            var list = WpfUi.StringList(_vm.Files, nameof(ExternalProcessingsViewModel.SelectedFile));
            list.Height = 160;
            list.Margin = new Thickness(0, 6, 0, 6);
            filesPanel.Children.Add(list);
            filesPanel.Children.Add(WpfUi.BoundText(nameof(ExternalProcessingsViewModel.FilesSummary)));
            filesPanel.Children.Add(WpfUi.ButtonRow(
                WpfUi.Button("ExtProc.AddFile", (_, _) => OnAddFile(), 150),
                WpfUi.Button("ExtProc.AddFolder", (_, _) => OnAddFolder(), 150),
                WpfUi.Button("ExtProc.RemoveFile", (_, _) => _vm.RemoveSelectedFile(), 120)));
            filesPanel.Children.Add(WpfUi.Wrap(WpfUi.BoundCheckBox("ExtProc.UpdateExisting", nameof(ExternalProcessingsViewModel.UpdateExisting))));
            var load = WpfUi.Button("ExtProc.Load", async (_, _) => await OnLoadAsync(), 240, accent: true);
            load.SetBinding(IsEnabledProperty, new Binding(nameof(ExternalProcessingsViewModel.IsIdle)));
            filesPanel.Children.Add(WpfUi.ButtonRow(load));
            filesPanel.Children.Add(WpfUi.Label("ExtProc.Hint", 11));
            WpfUi.Place(right, WpfUi.Card(filesPanel), 0);

            WpfUi.Place(right, WpfUi.ReadOnlyText(nameof(ExternalProcessingsViewModel.LogText)), 1);
            WpfUi.Place(root, right, 0, 2);

            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(ExternalProcessingsViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = WpfUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private void OnAddFile()
        {
            var path = Dialogs.OpenFileDialog(LocalizationManager.T("ExtProc.ChooseFile"), LocalizationManager.T("ExtProc.FileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                _vm.AddFiles(new[] { path });
        }

        private void OnAddFolder()
        {
            var folder = Dialogs.OpenFolderDialog(LocalizationManager.T("ExtProc.ChooseFolder"));
            if (!string.IsNullOrWhiteSpace(folder))
                _vm.AddFolder(folder);
        }

        private async System.Threading.Tasks.Task OnLoadAsync()
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.NoChecked"), Title);
                return;
            }
            if (_vm.Files.Count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("ExtProc.NoFiles"), Title);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("ExtProc.Confirm"), _vm.Files.Count, count), Title))
                return;
            await _vm.LoadToCheckedAsync();
        }
    }
}
#endif
