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
    /// Окно «Регламентные задания ИБ» (выпуск 1.0.0.85, Windows/WPF): задания
    /// выбранной базы и массовое выключение/включение в отмеченных базах.
    /// Логика — в <see cref="ScheduledJobsViewModel"/>.
    /// </summary>
    public sealed class ScheduledJobsWindow : Window
    {
        private readonly ScheduledJobsViewModel _vm;

        public ScheduledJobsWindow(ScheduledJobsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // Issue #119 (1.0.1.119): стартовый размер уменьшен по таблице 1.4 плана.
            WpfUi.SetupWindow(this, "Jobs.Title", 1100, 720);

            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = WpfUi.Rows(0, 0, -1, 0);
            left.Children.Add(WpfUi.Section("IbUsers.Bases"));
            WpfUi.Place(left, WpfUi.Wrap(WpfUi.Label("Extensions.Search"),
                WpfUi.BoundTextBox(nameof(ScheduledJobsViewModel.SearchText), 170),
                WpfUi.Button("Extensions.CheckAll", (_, _) => _vm.SetAllChecked(true), 90),
                WpfUi.Button("Extensions.UncheckAll", (_, _) => _vm.SetAllChecked(false), 90)), 1);
            var bases = WpfUi.Grid(_vm.Targets);
            bases.Columns.Add(WpfUi.CheckColumn(nameof(ExtensionTargetRow.IsChecked)));
            bases.Columns.Add(WpfUi.Column("Extensions.ColBase", nameof(ExtensionTargetRow.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
            bases.Columns.Add(WpfUi.Column("Extensions.ColStatus", nameof(ExtensionTargetRow.Status), new DataGridLength(1, DataGridLengthUnitType.Star)));
            bases.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(ScheduledJobsViewModel.SelectedTarget)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(left, bases, 2);
            WpfUi.Place(left, WpfUi.BoundText(nameof(ScheduledJobsViewModel.CheckedSummary)), 3);
            WpfUi.Place(root, left, 0, 0);

            var right = WpfUi.Rows(0, -1.3, 0, -0.5);
            var hint = WpfUi.BoundText(nameof(ScheduledJobsViewModel.PlatformHint), 11);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            WpfUi.Place(right, hint, 0);

            var jobsPanel = WpfUi.Rows(0, 0, -1, 0);
            jobsPanel.Children.Add(WpfUi.Section("Jobs.JobsSection"));
            WpfUi.Place(jobsPanel, WpfUi.Wrap(WpfUi.BoundText(nameof(ScheduledJobsViewModel.SelectedBaseText)),
                WpfUi.BoundText(nameof(ScheduledJobsViewModel.JobsSummary))), 1);
            var jobs = WpfUi.Grid(_vm.Jobs);
            jobs.Columns.Add(WpfUi.Column("Jobs.ColName", nameof(ScheduledJobRow.Name), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
            jobs.Columns.Add(WpfUi.Column("Jobs.ColMetadata", nameof(ScheduledJobRow.MetadataName), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
            jobs.Columns.Add(WpfUi.Column("Jobs.ColUse", nameof(ScheduledJobRow.UseText), new DataGridLength(0.5, DataGridLengthUnitType.Star)));
            jobs.Columns.Add(WpfUi.Column("Jobs.ColSchedule", nameof(ScheduledJobRow.Schedule), new DataGridLength(1.6, DataGridLengthUnitType.Star)));
            jobs.Columns.Add(WpfUi.Column("Jobs.ColLastRun", nameof(ScheduledJobRow.LastRun), new DataGridLength(1.1, DataGridLengthUnitType.Star)));
            jobs.Columns.Add(WpfUi.Column("Jobs.ColError", nameof(ScheduledJobRow.LastError), new DataGridLength(1.2, DataGridLengthUnitType.Star)));
            jobs.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(ScheduledJobsViewModel.SelectedJob)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(jobsPanel, jobs, 2);
            var refresh = WpfUi.Button("Jobs.Refresh", async (_, _) => await _vm.RefreshJobsAsync(), 150);
            var enable = WpfUi.Button("Jobs.EnableSelected", async (_, _) => await _vm.SetSelectedUseAsync(true), 150);
            var disable = WpfUi.Button("Jobs.DisableSelected", async (_, _) => await _vm.SetSelectedUseAsync(false), 150);
            var keep = WpfUi.Button("Jobs.AddToKeep", (_, _) => _vm.AddSelectedToKeep(), 190);
            foreach (var b in new[] { refresh, enable, disable })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsIdle)));
            WpfUi.Place(jobsPanel, WpfUi.ButtonRow(refresh, enable, disable, keep), 3);
            WpfUi.Place(right, WpfUi.Card(jobsPanel), 1);

            var batch = new StackPanel();
            batch.Children.Add(WpfUi.Section("Jobs.BatchSection"));
            batch.Children.Add(WpfUi.Wrap(WpfUi.Label("Jobs.Keep"),
                WpfUi.BoundTextBox(nameof(ScheduledJobsViewModel.KeepText), 560)));
            var disableAll = WpfUi.Button("Jobs.DisableAll", async (_, _) => await OnSetAllAsync(false), 260, accent: true);
            var enableAll = WpfUi.Button("Jobs.EnableAll", async (_, _) => await OnSetAllAsync(true), 240);
            foreach (var b in new[] { disableAll, enableAll })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsIdle)));
            batch.Children.Add(WpfUi.ButtonRow(disableAll, enableAll));
            batch.Children.Add(WpfUi.Label("Jobs.BatchHint", 11));
            WpfUi.Place(right, WpfUi.Card(batch), 2);

            WpfUi.Place(right, WpfUi.ReadOnlyText(nameof(ScheduledJobsViewModel.LogText)), 3);
            WpfUi.Place(root, right, 0, 2);

            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 110);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(ScheduledJobsViewModel.IsBusy)));
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

        private async System.Threading.Tasks.Task OnSetAllAsync(bool use)
        {
            var count = _vm.CheckedTargets.Count;
            if (count == 0)
            {
                Dialogs.ShowInfo(LocalizationManager.T("IbUsers.NoChecked"), Title);
                return;
            }
            if (!Dialogs.Confirm(string.Format(LocalizationManager.T(use ? "Jobs.ConfirmEnableAll" : "Jobs.ConfirmDisableAll"), count), Title))
                return;
            await _vm.SetAllInCheckedAsync(use);
        }
    }
}
#endif
