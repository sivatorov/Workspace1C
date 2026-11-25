#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Мониторинг и оповещения» (выпуск 1.0.0.69, Avalonia/Linux).
    /// Логика — в <see cref="ServerAlertsViewModel"/>.
    /// </summary>
    public sealed class ServerAlertsWindow : ModalWindowBase
    {
        private static readonly double[] ServerColumns = { 40, -2, -1, -1, -1 };
        private static readonly double[] LogColumns = { 40, -3, -1 };

        private readonly ServerAlertsViewModel _vm;

        public ServerAlertsWindow(ServerAlertsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Alerts.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 960;
            Height = 700;
            MinWidth = 760;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, 0, 0, -1, 0, 0, -1, 120, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("Alerts.Intro");
            intro.Margin = new Thickness(0, 0, 0, 8);
            AvaloniaUi.Place(root, intro, 0);

            AvaloniaUi.Place(root, AvaloniaUi.Wrap(
                AvaloniaUi.BoundCheckBox("Alerts.Enabled", nameof(ServerAlertsViewModel.Enabled)),
                AvaloniaUi.Label("Alerts.Interval"), AvaloniaUi.BoundTextBox(nameof(ServerAlertsViewModel.IntervalText), 60),
                AvaloniaUi.Label("Alerts.Repeat"), AvaloniaUi.BoundTextBox(nameof(ServerAlertsViewModel.RepeatText), 60)), 1);

            AvaloniaUi.Place(root, AvaloniaUi.Section("Alerts.ServersSection"), 2);
            AvaloniaUi.Place(root, AvaloniaUi.Header(
                new[] { "Alerts.ColOn", "Alerts.ColServer", "Alerts.ColSessions", "Alerts.ColMemory", "Alerts.ColLocks" },
                ServerColumns), 3);
            var servers = new ListBox { ItemsSource = _vm.Servers };
            servers.ItemTemplate = new FuncDataTemplate<ServerAlertRow>((_, _) =>
            {
                var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(ServerColumns) };
                grid.Children.Add(AvaloniaUi.CellCheck(nameof(ServerAlertRow.Enabled), 0));
                grid.Children.Add(AvaloniaUi.CellBound(nameof(ServerAlertRow.Name), 1, bold: true));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(ServerAlertRow.MaxSessions), 2));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(ServerAlertRow.MaxMemoryMb), 3));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(ServerAlertRow.MaxWaitingLocks), 4));
                return grid;
            });
            AvaloniaUi.Place(root, servers, 4);

            var logsHeader = AvaloniaUi.Section("Alerts.EventLogSection");
            logsHeader.Margin = new Thickness(0, 10, 0, 4);
            AvaloniaUi.Place(root, logsHeader, 5);
            AvaloniaUi.Place(root, AvaloniaUi.Header(
                new[] { "Alerts.ColOn", "Alerts.ColBase", "Alerts.ColMinErrors" }, LogColumns), 6);
            var logs = new ListBox { ItemsSource = _vm.EventLogs };
            logs.ItemTemplate = new FuncDataTemplate<EventLogWatchRow>((_, _) =>
            {
                var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(LogColumns) };
                grid.Children.Add(AvaloniaUi.CellCheck(nameof(EventLogWatchRow.Enabled), 0));
                grid.Children.Add(AvaloniaUi.CellBound(nameof(EventLogWatchRow.Name), 1));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(EventLogWatchRow.MinErrors), 2));
                return grid;
            });
            AvaloniaUi.Place(root, logs, 7);

            var log = AvaloniaUi.ReadOnlyText(nameof(ServerAlertsViewModel.LogText));
            log.Margin = new Thickness(0, 10, 0, 0);
            AvaloniaUi.Place(root, log, 8);

            var save = AvaloniaUi.Button("Common.Save", (_, _) => _vm.Save(), 120, accent: true);
            var check = AvaloniaUi.Button("Alerts.CheckNow", async (_, _) => await _vm.CheckNowAsync(), 170);
            check.Bind(IsEnabledProperty, new Binding(nameof(ServerAlertsViewModel.IsIdle)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(save, check, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 9);

            Content = root;
        }
    }
}
#endif
