#if LINUX
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Внешние отчёты и обработки» (issue #91, Avalonia/Linux; операции через COM доступны только в Windows): загрузка .epf/.erf
    /// в справочник «Дополнительные отчёты и обработки» отмеченных баз.
    /// Логика — в <see cref="ExternalProcessingsViewModel"/>.
    /// </summary>
    public sealed class ExternalProcessingsWindow : ModalWindowBase
    {
        private readonly ExternalProcessingsViewModel _vm;

        public ExternalProcessingsWindow(ExternalProcessingsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("ExtProc.Title");
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
            var searchRow = AvaloniaUi.Wrap(AvaloniaUi.Label("Extensions.Search"),
                AvaloniaUi.BoundTextBox(nameof(ExternalProcessingsViewModel.SearchText), 220),
                AvaloniaUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                AvaloniaUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90));
            AvaloniaUi.Place(left, searchRow, 1);
            var grid = new ListBox { ItemsSource = _vm.Targets };
            grid.ItemTemplate = new FuncDataTemplate<ExtensionTargetRow>((_, _) => BuildTargetRow());
            AvaloniaUi.Place(left, grid, 2);
            AvaloniaUi.Place(left, AvaloniaUi.BoundText(nameof(ExternalProcessingsViewModel.CheckedSummary)), 3);
            AvaloniaUi.Place(root, left, 0, 0);

            // ---- Справа: файлы, загрузка, журнал ----
            var right = AvaloniaUi.Rows(0, -1);

            var filesPanel = new StackPanel();
            filesPanel.Children.Add(AvaloniaUi.Section("ExtProc.FilesSection"));
            var list = AvaloniaUi.StringList(_vm.Files, nameof(ExternalProcessingsViewModel.SelectedFile));
            list.Height = 160;
            list.Margin = new Thickness(0, 6, 0, 6);
            filesPanel.Children.Add(list);
            filesPanel.Children.Add(AvaloniaUi.BoundText(nameof(ExternalProcessingsViewModel.FilesSummary)));
            filesPanel.Children.Add(AvaloniaUi.ButtonRow(
                AvaloniaUi.Button("ExtProc.AddFile", (_, _) => OnAddFile(), 150),
                AvaloniaUi.Button("ExtProc.AddFolder", (_, _) => OnAddFolder(), 150),
                AvaloniaUi.Button("ExtProc.RemoveFile", (_, _) => _vm.RemoveSelectedFile(), 120)));
            filesPanel.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.BoundCheckBox("ExtProc.UpdateExisting", nameof(ExternalProcessingsViewModel.UpdateExisting))));
            var load = AvaloniaUi.Button("ExtProc.Load", async (_, _) => await OnLoadAsync(), 240, accent: true);
            load.Bind(IsEnabledProperty, new Binding(nameof(ExternalProcessingsViewModel.IsIdle)));
            filesPanel.Children.Add(AvaloniaUi.ButtonRow(load));
            filesPanel.Children.Add(AvaloniaUi.Label("ExtProc.Hint", 11));
            AvaloniaUi.Place(right, AvaloniaUi.Card(filesPanel), 0);

            AvaloniaUi.Place(right, AvaloniaUi.ReadOnlyText(nameof(ExternalProcessingsViewModel.LogText)), 1);
            AvaloniaUi.Place(root, right, 0, 2);

            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(ExternalProcessingsViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var bottom = AvaloniaUi.ButtonRow(cancel, close);
            bottom.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, bottom, 1, 0, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
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
            var row = new Grid { ColumnDefinitions = AvaloniaUi.Columns(32, -2, -1), Height = 28 };
            row.Children.Add(check);
            row.Children.Add(name);
            row.Children.Add(status);
            return row;
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

        private async Task OnLoadAsync()
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("Extensions.NoChecked"), Title ?? string.Empty);
                return;
            }
            if (_vm.Files.Count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("ExtProc.NoFiles"), Title ?? string.Empty);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T("ExtProc.Confirm"), _vm.Files.Count, count), Title ?? string.Empty))
                return;
            await _vm.LoadToCheckedAsync();
        }
    }
}
#endif
