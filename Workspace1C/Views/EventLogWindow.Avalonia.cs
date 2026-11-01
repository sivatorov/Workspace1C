#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.EventLog;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Журнал регистрации» (выпуск 1.0.0.69, Avalonia/Linux) — пара WPF-окна
    /// <c>EventLogWindow</c>: таблица событий с отбором, подробности записи и экспорт
    /// в CSV. Логика — в <see cref="EventLogViewModel"/>.
    /// </summary>
    public sealed class EventLogWindow : ModalWindowBase
    {
        private static readonly string[] HeaderKeys =
        {
            "EventLog.Col.Date", "EventLog.Col.Severity", "EventLog.Col.Event", "EventLog.Col.User",
            "EventLog.Col.Computer", "EventLog.Col.Application", "EventLog.Col.Metadata", "EventLog.Col.Comment"
        };

        private static readonly double[] ColumnWidths = { 140, 110, -1.3, -1, -0.8, -0.8, -1, -2 };

        private readonly EventLogViewModel _vm;

        public EventLogWindow(EventLogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            var title = string.IsNullOrWhiteSpace(_vm.Title)
                ? LocalizationManager.T("EventLog.Title")
                : $"{LocalizationManager.T("EventLog.Title")} — {_vm.Title}";
            Title = title;
            Width = 1200;
            Height = 760;
            MinWidth = 820;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, 0, 0, -3, 5, -1, 0);
            root.Margin = new Thickness(16);

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(AvaloniaUi.Text(title, 15, bold: true));
            header.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("EventLog.Base"),
                AvaloniaUi.BoundCombo(_vm.InfobaseNames, nameof(EventLogViewModel.SelectedIndex), 420)));
            header.Children.Add(AvaloniaUi.BoundText(nameof(EventLogViewModel.LocationText)));
            AvaloniaUi.Place(root, header, 0);

            AvaloniaUi.Place(root, AvaloniaUi.Wrap(
                AvaloniaUi.Label("EventLog.Period"),
                AvaloniaUi.BoundCombo(_vm.PeriodOptions, nameof(EventLogViewModel.PeriodIndex)),
                AvaloniaUi.Label("EventLog.Limit"),
                AvaloniaUi.BoundCombo(_vm.LimitOptions, nameof(EventLogViewModel.LimitIndex), 110),
                AvaloniaUi.BoundCheckBox("EventLog.Severity.Error", nameof(EventLogViewModel.ShowError)),
                AvaloniaUi.BoundCheckBox("EventLog.Severity.Warning", nameof(EventLogViewModel.ShowWarning)),
                AvaloniaUi.BoundCheckBox("EventLog.Severity.Info", nameof(EventLogViewModel.ShowInfo)),
                AvaloniaUi.BoundCheckBox("EventLog.Severity.Note", nameof(EventLogViewModel.ShowNote))), 1);

            var userBox = AvaloniaUi.BoundTextBox(nameof(EventLogViewModel.UserFilter), 150);
            var eventBox = AvaloniaUi.BoundTextBox(nameof(EventLogViewModel.EventFilter), 150);
            var textBox = AvaloniaUi.BoundTextBox(nameof(EventLogViewModel.TextFilter), 200);
            foreach (var box in new[] { userBox, eventBox, textBox })
            {
                box.KeyDown += async (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    e.Handled = true;
                    await _vm.LoadAsync();
                };
            }

            var refresh = AvaloniaUi.Button("EventLog.Refresh", async (_, _) => await _vm.LoadAsync(), accent: true);
            refresh.Bind(IsEnabledProperty, new Binding(nameof(EventLogViewModel.IsIdle)));
            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 90);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(EventLogViewModel.IsBusy)));

            AvaloniaUi.Place(root, AvaloniaUi.Wrap(
                AvaloniaUi.Label("EventLog.Col.User"), userBox,
                AvaloniaUi.Label("EventLog.Col.Event"), eventBox,
                AvaloniaUi.Label("EventLog.TextFilter"), textBox,
                refresh, cancel), 2);

            AvaloniaUi.Place(root, AvaloniaUi.Header(HeaderKeys, ColumnWidths), 3);

            var list = new ListBox { ItemsSource = _vm.Rows };
            list.ItemTemplate = new FuncDataTemplate<EventLogRowViewModel>((row, _) => BuildRow(row));
            list.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(EventLogViewModel.SelectedRow)) { Mode = BindingMode.TwoWay });
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AvaloniaUi.Place(root, list, 4);

            AvaloniaUi.Place(root, new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Rows,
                Background = Brushes.Transparent,
                Height = 5
            }, 5);

            AvaloniaUi.Place(root, AvaloniaUi.ReadOnlyText(nameof(EventLogViewModel.DetailsText)), 6);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(AvaloniaUi.Button("EventLog.OpenFolder", (_, _) => OnOpenFolder(), 150));
            buttons.Children.Add(AvaloniaUi.Button("EventLog.ExportCsv", (_, _) => OnExport(), 150));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            bottom.Children.Add(AvaloniaUi.BoundText(nameof(EventLogViewModel.StatusText)));
            AvaloniaUi.Place(root, bottom, 7);

            Content = root;

            Opened += async (_, _) => await _vm.LoadAsync();
            Closed += (_, _) => _vm.Cancel();
        }

        private static Control BuildRow(EventLogRowViewModel? row)
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(ColumnWidths), Height = 26 };
            if (row is null)
                return grid;
            grid.Children.Add(AvaloniaUi.Cell(row.TimeText, 0));
            grid.Children.Add(AvaloniaUi.Cell(row.SeverityText, 1, bold: true, foreground: AvaloniaUi.Brush(row.SeverityColor)));
            grid.Children.Add(AvaloniaUi.Cell(row.EventText, 2));
            grid.Children.Add(AvaloniaUi.Cell(row.User, 3));
            grid.Children.Add(AvaloniaUi.Cell(row.Computer, 4));
            grid.Children.Add(AvaloniaUi.Cell(row.ApplicationText, 5));
            grid.Children.Add(AvaloniaUi.Cell(row.MetadataText, 6));
            grid.Children.Add(AvaloniaUi.Cell(row.CommentText, 7));
            return grid;
        }

        private async void OnOpenFolder()
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            try
            {
                var dialogs = AppServices.GetRequiredService<IDialogService>();
                var folder = dialogs.OpenFolderDialog(LocalizationManager.T("EventLog.ChooseFolder"), _vm.Location.LogDir);
                if (string.IsNullOrWhiteSpace(folder))
                    return;
                var location = LgdJournalLocator.FromDirectory(folder);
                if (!location.IsValid)
                {
                    dialogs.ShowWarning(location.ErrorMessage ?? LocalizationManager.T("EventLog.NotFound"),
                        LocalizationManager.T("EventLog.Title"));
                    return;
                }
                _vm.ChangeLocation(location);
                await _vm.LoadAsync();
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка выбора папки журнала регистрации", ex); } catch { /* лог не роняет обработчик */ }
            }
        }

        private void OnExport()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (_vm.Entries.Count == 0)
            {
                dialogs.ShowInfo(LocalizationManager.T("EventLog.NothingToExport"), LocalizationManager.T("EventLog.Title"));
                return;
            }
            var path = dialogs.SaveFileDialog(LocalizationManager.T("EventLog.ExportCsv"),
                $"EventLog_{DateTime.Now:yyyyMMdd_HHmm}.csv", "CSV (*.csv)|*.csv");
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var count = _vm.ExportCsv(path);
                dialogs.ShowInfo(string.Format(LocalizationManager.T("EventLog.Exported"), count, path),
                    LocalizationManager.T("EventLog.Title"));
            }
            catch (Exception ex)
            {
                dialogs.ShowError(ex.Message, LocalizationManager.T("EventLog.Title"));
            }
        }
    }
}
#endif
