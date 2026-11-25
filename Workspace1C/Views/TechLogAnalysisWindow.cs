#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.TechLogParser;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Анализ технологического журнала» (выпуск 1.0.0.86, п.4, Windows/WPF):
    /// топы долгих операций (CALL/DBMSSQL/SDBL), сводка блокировок и группировка
    /// исключений с отбором по базе и пользователю, экспорт в CSV.
    /// Логика — в <see cref="TechLogAnalysisViewModel"/>.
    /// </summary>
    public sealed class TechLogAnalysisWindow : Window
    {
        private readonly TechLogAnalysisViewModel _vm;

        public TechLogAnalysisWindow(TechLogAnalysisViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // Issue #119 (1.0.1.119): стартовый размер уменьшен по таблице 1.4 плана.
            WpfUi.SetupWindow(this, "TechLog.Analysis.Title", 1000, 700);

            var root = WpfUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            WpfUi.Place(root, WpfUi.Wrap(
                WpfUi.Label("TechLog.Analysis.Base"),
                WpfUi.BoundTextBox(nameof(TechLogAnalysisViewModel.BaseFilter), 240),
                WpfUi.Label("TechLog.Analysis.User"),
                WpfUi.BoundTextBox(nameof(TechLogAnalysisViewModel.UserFilter), 160),
                WpfUi.Label("TechLog.Analysis.Period"),
                WpfUi.BoundCombo(_vm.PeriodOptions, nameof(TechLogAnalysisViewModel.PeriodIndex), 150)), 0);

            WpfUi.Place(root, WpfUi.BoundText(nameof(TechLogAnalysisViewModel.SummaryText)), 1);

            var tabs = new TabControl();
            tabs.Items.Add(WpfTab("TechLog.Analysis.Tab.Calls", BuildTopGrid(nameof(TechLogAnalysisViewModel.TopCalls))));
            tabs.Items.Add(WpfTab("TechLog.Analysis.Tab.Sql", BuildTopGrid(nameof(TechLogAnalysisViewModel.TopSql))));
            tabs.Items.Add(WpfTab("TechLog.Analysis.Tab.Sdbl", BuildTopGrid(nameof(TechLogAnalysisViewModel.TopSdbl))));
            tabs.Items.Add(WpfTab("TechLog.Analysis.Tab.Locks", BuildLocksGrid()));
            tabs.Items.Add(WpfTab("TechLog.Analysis.Tab.Exceptions", BuildExceptionsGrid()));
            WpfUi.Place(root, tabs, 2);

            var refresh = WpfUi.Button("TechLog.Analysis.Refresh", async (_, _) => await _vm.LoadAsync(), 140, accent: true);
            refresh.SetBinding(IsEnabledProperty, new Binding(nameof(TechLogAnalysisViewModel.IsIdle)));
            var export = WpfUi.Button("TechLog.Analysis.ExportCsv", (_, _) => OnExport(), 150);
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(refresh, export, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 3);

            Content = root;

            Loaded += async (_, _) => await _vm.LoadAsync();
            Closed += (_, _) => _vm.Cancel();
        }

        private static TabItem WpfTab(string headerKey, object content) => new()
        {
            Header = LocalizationManager.T(headerKey),
            Content = content,
            Margin = new Thickness(4)
        };

        private static DataGrid BuildTopGrid(string itemsPath)
        {
            var grid = WpfUi.Grid(Array.Empty<TopOperationRow>());
            grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Text", nameof(TopOperationRow.Text),
                new DataGridLength(3, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Count", nameof(TopOperationRow.Count), new DataGridLength(80)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.MaxMs", nameof(TopOperationRow.MaxMsText), new DataGridLength(90)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.AvgMs", nameof(TopOperationRow.AvgMsText), new DataGridLength(90)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.TotalMs", nameof(TopOperationRow.TotalMsText), new DataGridLength(100)));
            return grid;
        }

        private static DataGrid BuildLocksGrid()
        {
            var grid = WpfUi.Grid(Array.Empty<LockSummaryRow>());
            grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(TechLogAnalysisViewModel.Locks)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.EventType", nameof(LockSummaryRow.EventType), new DataGridLength(90)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Key", nameof(LockSummaryRow.Key),
                new DataGridLength(3, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Count", nameof(LockSummaryRow.Count), new DataGridLength(70)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.TotalMs", nameof(LockSummaryRow.TotalMsText), new DataGridLength(90)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.MaxMs", nameof(LockSummaryRow.MaxMsText), new DataGridLength(90)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.LastSeen", nameof(LockSummaryRow.LastSeenText), new DataGridLength(140)));
            return grid;
        }

        private static DataGrid BuildExceptionsGrid()
        {
            var grid = WpfUi.Grid(Array.Empty<ExceptionGroupRow>());
            grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(TechLogAnalysisViewModel.Exceptions)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Key", nameof(ExceptionGroupRow.Key),
                new DataGridLength(3, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Count", nameof(ExceptionGroupRow.Count), new DataGridLength(70)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.LastSeen", nameof(ExceptionGroupRow.LastSeenText), new DataGridLength(140)));
            grid.Columns.Add(WpfUi.Column("TechLog.Analysis.Col.Example", nameof(ExceptionGroupRow.FirstContextLine),
                new DataGridLength(2, DataGridLengthUnitType.Star)));
            return grid;
        }

        private void OnExport()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (_vm.BuildCsvDocument().Length == 0)
            {
                dialogs.ShowInfo(LocalizationManager.T("TechLog.Analysis.Export.Empty"), LocalizationManager.T("TechLog.Analysis.Title"));
                return;
            }

            var path = dialogs.SaveFileDialog(LocalizationManager.T("TechLog.Analysis.ExportCsv"),
                $"TechLog_{DateTime.Now:yyyyMMdd_HHmm}.csv", "CSV (*.csv)|*.csv", _vm.LogDirectory);
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var count = _vm.ExportCsv(path);
                dialogs.ShowInfo(string.Format(LocalizationManager.T("TechLog.Analysis.Export.Done"), count, path),
                    LocalizationManager.T("TechLog.Analysis.Title"));
            }
            catch (Exception ex)
            {
                dialogs.ShowError(LocalizationManager.T("TechLog.Analysis.Export.Failed") + " " + ex.Message,
                    LocalizationManager.T("TechLog.Analysis.Title"));
            }
        }
    }
}
#endif