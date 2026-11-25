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
    /// Окно «Регламентные задания ИБ» (выпуск 1.0.0.85, Avalonia/Linux) — пара WPF-окна.
    /// COM-коннектора 1С на Linux нет: окно открывается с пояснением, операции недоступны.
    /// </summary>
    public sealed class ScheduledJobsWindow : ModalWindowBase
    {
        private static readonly double[] JobColumns = { -1.6, -1.1, -0.5, -1.6, -1.1, -1.2 };

        private readonly ScheduledJobsViewModel _vm;

        public ScheduledJobsWindow(ScheduledJobsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Jobs.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 1100;
            Height = 720;
            MinWidth = 900;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            var root = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions = AvaloniaUi.Columns(-0.75, 16, -1.5),
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
            };

            var left = AvaloniaUi.Rows(0, 0, -1, 0);
            AvaloniaUi.Place(left, AvaloniaUi.Section("IbUsers.Bases"), 0);
            AvaloniaUi.Place(left, AvaloniaUi.Wrap(
                AvaloniaUi.Label("Extensions.Search"),
                AvaloniaUi.BoundTextBox(nameof(ScheduledJobsViewModel.SearchText), 170),
                AvaloniaUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                AvaloniaUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90)), 1);
            var targets = new ListBox { ItemsSource = _vm.Targets };
            targets.ItemTemplate = new FuncDataTemplate<ExtensionTargetRow>((_, _) => BuildTargetRow());
            targets.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ScheduledJobsViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(left, targets, 2);
            AvaloniaUi.Place(left, AvaloniaUi.BoundText(nameof(ScheduledJobsViewModel.CheckedSummary)), 3);
            AvaloniaUi.Place(root, left, 0, 0);

            var right = AvaloniaUi.Rows(0, -1.3, 0, -0.5);
            var hint = AvaloniaUi.BoundText(nameof(ScheduledJobsViewModel.PlatformHint), 11);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            AvaloniaUi.Place(right, hint, 0);

            var jobsPanel = AvaloniaUi.Rows(0, 0, 0, -1, 0);
            AvaloniaUi.Place(jobsPanel, AvaloniaUi.Section("Jobs.JobsSection"), 0);
            AvaloniaUi.Place(jobsPanel, AvaloniaUi.Wrap(
                AvaloniaUi.BoundText(nameof(ScheduledJobsViewModel.SelectedBaseText)),
                AvaloniaUi.BoundText(nameof(ScheduledJobsViewModel.JobsSummary))), 1);
            AvaloniaUi.Place(jobsPanel, AvaloniaUi.Header(new[]
            {
                "Jobs.ColName", "Jobs.ColMetadata", "Jobs.ColUse", "Jobs.ColSchedule", "Jobs.ColLastRun", "Jobs.ColError"
            }, JobColumns), 2);
            var jobs = new ListBox { ItemsSource = _vm.Jobs };
            jobs.ItemTemplate = new FuncDataTemplate<ScheduledJobRow>((_, _) => BuildJobRow());
            jobs.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ScheduledJobsViewModel.SelectedJob)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(jobsPanel, jobs, 3);
            var refresh = AvaloniaUi.Button("Jobs.Refresh", async (_, _) => await _vm.RefreshJobsAsync(), 150);
            var enable = AvaloniaUi.Button("Jobs.EnableSelected", async (_, _) => await _vm.SetSelectedUseAsync(true), 150);
            var disable = AvaloniaUi.Button("Jobs.DisableSelected", async (_, _) => await _vm.SetSelectedUseAsync(false), 150);
            var keep = AvaloniaUi.Button("Jobs.AddToKeep", (_, _) => _vm.AddSelectedToKeep(), 190);
            foreach (var b in new[] { refresh, enable, disable })
                b.Bind(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsIdle)));
            AvaloniaUi.Place(jobsPanel, AvaloniaUi.ButtonRow(refresh, enable, disable, keep), 4);
            AvaloniaUi.Place(right, AvaloniaUi.Card(jobsPanel), 1);

            var batch = new StackPanel();
            batch.Children.Add(AvaloniaUi.Section("Jobs.BatchSection"));
            batch.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Jobs.Keep"),
                AvaloniaUi.BoundTextBox(nameof(ScheduledJobsViewModel.KeepText), 560)));
            var disableAll = AvaloniaUi.Button("Jobs.DisableAll", async (_, _) => await OnSetAllAsync(false), 260, accent: true);
            var enableAll = AvaloniaUi.Button("Jobs.EnableAll", async (_, _) => await OnSetAllAsync(true), 240);
            foreach (var b in new[] { disableAll, enableAll })
                b.Bind(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsIdle)));
            batch.Children.Add(AvaloniaUi.ButtonRow(disableAll, enableAll));
            batch.Children.Add(AvaloniaUi.Label("Jobs.BatchHint", 11));
            AvaloniaUi.Place(right, AvaloniaUi.Card(batch), 2);

            AvaloniaUi.Place(right, AvaloniaUi.ReadOnlyText(nameof(ScheduledJobsViewModel.LogText)), 3);
            AvaloniaUi.Place(root, right, 0, 2);

            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsBusy)));
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
            var status = AvaloniaUi.CellBound(nameof(ExtensionTargetRow.Status), 2);
            status.Opacity = 0.75;
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(32, -2, -1), Height = 28 };
            grid.Children.Add(check);
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ExtensionTargetRow.Name), 1, bold: true));
            grid.Children.Add(status);
            return grid;
        }

        private static Control BuildJobRow()
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(JobColumns), Height = 26 };
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.Name), 0, bold: true));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.MetadataName), 1));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.UseText), 2));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.Schedule), 3));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.LastRun), 4));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(ScheduledJobRow.LastError), 5));
            return grid;
        }

        private static IDialogService Dialogs => AppServices.GetRequiredService<IDialogService>();

        private async Task OnSetAllAsync(bool use)
        {
            var title = Title ?? string.Empty;
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("IbUsers.NoChecked"), title);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T(use ? "Jobs.ConfirmEnableAll" : "Jobs.ConfirmDisableAll"), count), title))
                return;
            await _vm.SetAllInCheckedAsync(use);
        }
    }
}
#endif
