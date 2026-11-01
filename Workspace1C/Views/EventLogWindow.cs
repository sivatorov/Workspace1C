#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.EventLog;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Журнал регистрации» (выпуск 1.0.0.69, Windows/WPF): таблица событий
    /// журнала файловой базы или выбранного каталога 1Cv8Log с отбором по периоду,
    /// важности, пользователю, событию и тексту, подробности выбранной записи
    /// и экспорт в CSV. Логика — в <see cref="EventLogViewModel"/>.
    /// </summary>
    public sealed class EventLogWindow : Window
    {
        private readonly EventLogViewModel _vm;

        public EventLogWindow(EventLogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "EventLog.Title", 1200, 760);
            Title = string.IsNullOrWhiteSpace(_vm.Title)
                ? LocalizationManager.T("EventLog.Title")
                : $"{LocalizationManager.T("EventLog.Title")} — {_vm.Title}";

            var root = WpfUi.Rows(0, 0, 0, -3, 5, -1, 0);
            root.Margin = new Thickness(16);

            // Заголовок: выбор базы (issue #74) и расположение журнала.
            var header = new StackPanel();
            header.Children.Add(WpfUi.Text(Title, 15, bold: true));
            header.Children.Add(WpfUi.Wrap(WpfUi.Label("EventLog.Base"),
                WpfUi.BoundCombo(_vm.InfobaseNames, nameof(EventLogViewModel.SelectedIndex), 420)));
            header.Children.Add(WpfUi.BoundText(nameof(EventLogViewModel.LocationText)));
            header.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, header, 0);

            // Отбор.
            var filters = WpfUi.Wrap(
                WpfUi.Label("EventLog.Period"),
                WpfUi.BoundCombo(_vm.PeriodOptions, nameof(EventLogViewModel.PeriodIndex)),
                WpfUi.Label("EventLog.Limit"),
                WpfUi.BoundCombo(_vm.LimitOptions, nameof(EventLogViewModel.LimitIndex), 110),
                WpfUi.BoundCheckBox("EventLog.Severity.Error", nameof(EventLogViewModel.ShowError)),
                WpfUi.BoundCheckBox("EventLog.Severity.Warning", nameof(EventLogViewModel.ShowWarning)),
                WpfUi.BoundCheckBox("EventLog.Severity.Info", nameof(EventLogViewModel.ShowInfo)),
                WpfUi.BoundCheckBox("EventLog.Severity.Note", nameof(EventLogViewModel.ShowNote)));
            WpfUi.Place(root, filters, 1);

            var userBox = WpfUi.BoundTextBox(nameof(EventLogViewModel.UserFilter), 150);
            var eventBox = WpfUi.BoundTextBox(nameof(EventLogViewModel.EventFilter), 150);
            var textBox = WpfUi.BoundTextBox(nameof(EventLogViewModel.TextFilter), 200);
            foreach (var box in new[] { userBox, eventBox, textBox })
                box.KeyDown += OnFilterKeyDown;

            var refresh = WpfUi.Button("EventLog.Refresh", async (_, _) => await _vm.LoadAsync(), accent: true);
            refresh.SetBinding(IsEnabledProperty, new Binding(nameof(EventLogViewModel.IsIdle)));
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 90);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(EventLogViewModel.IsBusy)));

            var search = WpfUi.Wrap(
                WpfUi.Label("EventLog.Col.User"), userBox,
                WpfUi.Label("EventLog.Col.Event"), eventBox,
                WpfUi.Label("EventLog.TextFilter"), textBox,
                refresh, cancel);
            WpfUi.Place(root, search, 2);

            // Таблица событий.
            var grid = WpfUi.Grid(_vm.Rows);
            var severityColumn = WpfUi.Column("EventLog.Col.Severity", nameof(EventLogRowViewModel.SeverityText), new DataGridLength(110));
            var severityStyle = new Style(typeof(TextBlock), severityColumn.ElementStyle);
            severityStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                new Binding(nameof(EventLogRowViewModel.SeverityColor))));
            severityStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            severityColumn.ElementStyle = severityStyle;

            grid.Columns.Add(WpfUi.Column("EventLog.Col.Date", nameof(EventLogRowViewModel.TimeText), new DataGridLength(140)));
            grid.Columns.Add(severityColumn);
            grid.Columns.Add(WpfUi.Column("EventLog.Col.Event", nameof(EventLogRowViewModel.EventText), new DataGridLength(1.3, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("EventLog.Col.User", nameof(EventLogRowViewModel.User), new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("EventLog.Col.Computer", nameof(EventLogRowViewModel.Computer), new DataGridLength(0.8, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("EventLog.Col.Application", nameof(EventLogRowViewModel.ApplicationText), new DataGridLength(0.8, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("EventLog.Col.Metadata", nameof(EventLogRowViewModel.MetadataText), new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("EventLog.Col.Comment", nameof(EventLogRowViewModel.CommentText), new DataGridLength(2, DataGridLengthUnitType.Star)));
            grid.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(EventLogViewModel.SelectedRow)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(root, grid, 3);

            var splitter = new GridSplitter
            {
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                Background = System.Windows.Media.Brushes.Transparent
            };
            WpfUi.Place(root, splitter, 4);

            WpfUi.Place(root, WpfUi.ReadOnlyText(nameof(EventLogViewModel.DetailsText)), 5);

            // Низ: статус и кнопки.
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(WpfUi.Button("EventLog.OpenFolder", (_, _) => OnOpenFolder(), 150));
            buttons.Children.Add(WpfUi.Button("EventLog.ExportCsv", (_, _) => OnExport(), 150));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            bottom.Children.Add(WpfUi.BoundText(nameof(EventLogViewModel.StatusText)));
            WpfUi.Place(root, bottom, 6);

            Content = root;

            Loaded += async (_, _) => await _vm.LoadAsync();
            Closed += (_, _) => _vm.Cancel();
        }

        private async void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            try
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                // Привязка с UpdateSourceTrigger=PropertyChanged уже передала текст во ViewModel.
                await _vm.LoadAsync();
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка загрузки журнала регистрации по Enter", ex); } catch { /* лог не роняет обработчик */ }
            }
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
