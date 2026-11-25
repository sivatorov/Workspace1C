#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Мониторинг и оповещения» (выпуск 1.0.0.69, Windows/WPF).
    /// Логика — в <see cref="ServerAlertsViewModel"/>.
    /// </summary>
    public sealed class ServerAlertsWindow : Window
    {
        private readonly ServerAlertsViewModel _vm;

        public ServerAlertsWindow(ServerAlertsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // Issue #119 (1.0.1.119): стартовый размер уменьшен по таблице 1.4 плана.
            WpfUi.SetupWindow(this, "Alerts.Title", 960, 700);

            var root = WpfUi.Rows(0, 0, 0, -1, 0, -1, 120, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("Alerts.Intro");
            intro.Margin = new Thickness(0, 0, 0, 8);
            WpfUi.Place(root, intro, 0);

            WpfUi.Place(root, WpfUi.Wrap(
                WpfUi.BoundCheckBox("Alerts.Enabled", nameof(ServerAlertsViewModel.Enabled)),
                WpfUi.Label("Alerts.Interval"), WpfUi.BoundTextBox(nameof(ServerAlertsViewModel.IntervalText), 60),
                WpfUi.Label("Alerts.Repeat"), WpfUi.BoundTextBox(nameof(ServerAlertsViewModel.RepeatText), 60)), 1);

            WpfUi.Place(root, WpfUi.Section("Alerts.ServersSection"), 2);
            var servers = WpfUi.Grid(_vm.Servers);
            servers.IsReadOnly = false;
            servers.Columns.Add(WpfUi.CheckColumn(nameof(ServerAlertRow.Enabled)));
            servers.Columns.Add(WpfUi.Column("Alerts.ColServer", nameof(ServerAlertRow.Name), new DataGridLength(2, DataGridLengthUnitType.Star)));
            servers.Columns.Add(WpfUi.EditColumn("Alerts.ColSessions", nameof(ServerAlertRow.MaxSessions), new DataGridLength(1, DataGridLengthUnitType.Star)));
            servers.Columns.Add(WpfUi.EditColumn("Alerts.ColMemory", nameof(ServerAlertRow.MaxMemoryMb), new DataGridLength(1, DataGridLengthUnitType.Star)));
            servers.Columns.Add(WpfUi.EditColumn("Alerts.ColLocks", nameof(ServerAlertRow.MaxWaitingLocks), new DataGridLength(1, DataGridLengthUnitType.Star)));
            foreach (var column in servers.Columns)
                if (column is DataGridTextColumn text && text.Binding is Binding b && b.Path.Path == nameof(ServerAlertRow.Name))
                    text.IsReadOnly = true;
            WpfUi.Place(root, servers, 3);

            var logsHeader = WpfUi.Section("Alerts.EventLogSection");
            logsHeader.Margin = new Thickness(0, 10, 0, 4);
            WpfUi.Place(root, logsHeader, 4);
            var logs = WpfUi.Grid(_vm.EventLogs);
            logs.IsReadOnly = false;
            logs.Columns.Add(WpfUi.CheckColumn(nameof(EventLogWatchRow.Enabled)));
            var nameColumn = WpfUi.Column("Alerts.ColBase", nameof(EventLogWatchRow.Name), new DataGridLength(3, DataGridLengthUnitType.Star));
            nameColumn.IsReadOnly = true;
            logs.Columns.Add(nameColumn);
            logs.Columns.Add(WpfUi.EditColumn("Alerts.ColMinErrors", nameof(EventLogWatchRow.MinErrors), new DataGridLength(1, DataGridLengthUnitType.Star)));
            WpfUi.Place(root, logs, 5);

            var log = WpfUi.ReadOnlyText(nameof(ServerAlertsViewModel.LogText));
            log.Margin = new Thickness(0, 10, 0, 0);
            WpfUi.Place(root, log, 6);

            var save = WpfUi.Button("Common.Save", (_, _) => _vm.Save(), 120, accent: true);
            var check = WpfUi.Button("Alerts.CheckNow", async (_, _) => await _vm.CheckNowAsync(), 170);
            check.SetBinding(IsEnabledProperty, new Binding(nameof(ServerAlertsViewModel.IsIdle)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(save, check, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 7);

            Content = root;
        }
    }
}
#endif
