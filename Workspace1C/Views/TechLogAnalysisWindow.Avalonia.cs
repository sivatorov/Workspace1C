#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.TechLogParser;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Анализ технологического журнала» (выпуск 1.0.0.86, п.4, Avalonia/Linux) —
    /// пара WPF-окна <c>TechLogAnalysisWindow</c>: топы долгих операций, сводка
    /// блокировок и исключения с отбором, экспорт в CSV. Логика — в
    /// <see cref="TechLogAnalysisViewModel"/>.
    /// </summary>
    public sealed class TechLogAnalysisWindow : ModalWindowBase
    {
        private static readonly string[] TopHeaderKeys =
        {
            "TechLog.Analysis.Col.Text", "TechLog.Analysis.Col.Count", "TechLog.Analysis.Col.MaxMs",
            "TechLog.Analysis.Col.AvgMs", "TechLog.Analysis.Col.TotalMs"
        };

        private static readonly double[] TopWidths = { -3, 80, 90, 90, 100 };

        private static readonly string[] LockHeaderKeys =
        {
            "TechLog.Analysis.Col.EventType", "TechLog.Analysis.Col.Key", "TechLog.Analysis.Col.Count",
            "TechLog.Analysis.Col.TotalMs", "TechLog.Analysis.Col.MaxMs", "TechLog.Analysis.Col.LastSeen"
        };

        private static readonly double[] LockWidths = { 90, -3, 70, 90, 90, 140 };

        private static readonly string[] ExceptionHeaderKeys =
        {
            "TechLog.Analysis.Col.Key", "TechLog.Analysis.Col.Count",
            "TechLog.Analysis.Col.LastSeen", "TechLog.Analysis.Col.Example"
        };

        private static readonly double[] ExceptionWidths = { -3, 70, 140, -2 };

        private readonly TechLogAnalysisViewModel _vm;

        public TechLogAnalysisWindow(TechLogAnalysisViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("TechLog.Analysis.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 1000;
            Height = 700;
            MinWidth = 800;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            AvaloniaUi.Place(root, AvaloniaUi.Wrap(
                AvaloniaUi.Label("TechLog.Analysis.Base"),
                AvaloniaUi.BoundTextBox(nameof(TechLogAnalysisViewModel.BaseFilter), 240),
                AvaloniaUi.Label("TechLog.Analysis.User"),
                AvaloniaUi.BoundTextBox(nameof(TechLogAnalysisViewModel.UserFilter), 160),
                AvaloniaUi.Label("TechLog.Analysis.Period"),
                AvaloniaUi.BoundCombo(_vm.PeriodOptions, nameof(TechLogAnalysisViewModel.PeriodIndex), 150)), 0);

            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(TechLogAnalysisViewModel.SummaryText)), 1);

            var tabs = new TabControl();
            tabs.Items.Add(Tab("TechLog.Analysis.Tab.Calls",
                BuildTopTab(nameof(TechLogAnalysisViewModel.TopCalls))));
            tabs.Items.Add(Tab("TechLog.Analysis.Tab.Sql",
                BuildTopTab(nameof(TechLogAnalysisViewModel.TopSql))));
            tabs.Items.Add(Tab("TechLog.Analysis.Tab.Sdbl",
                BuildTopTab(nameof(TechLogAnalysisViewModel.TopSdbl))));
            tabs.Items.Add(Tab("TechLog.Analysis.Tab.Locks",
                BuildLocksTab()));
            tabs.Items.Add(Tab("TechLog.Analysis.Tab.Exceptions",
                BuildExceptionsTab()));
            AvaloniaUi.Place(root, tabs, 2);

            var refresh = AvaloniaUi.Button("TechLog.Analysis.Refresh", async (_, _) => await _vm.LoadAsync(), 140, accent: true);
            refresh.Bind(IsEnabledProperty, new Binding(nameof(TechLogAnalysisViewModel.IsIdle)));
            var export = AvaloniaUi.Button("TechLog.Analysis.ExportCsv", (_, _) => OnExport(), 150);
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(refresh, export, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 3);

            Content = root;

            Opened += async (_, _) => await _vm.LoadAsync();
            Closed += (_, _) => _vm.Cancel();
        }

        private static TabItem Tab(string headerKey, Control content) => new()
        {
            Header = LocalizationManager.T(headerKey),
            Content = content
        };

        private static Control BuildTopTab(string itemsPath)
        {
            var panel = AvaloniaUi.Rows(0, -1);
            AvaloniaUi.Place(panel, AvaloniaUi.Header(TopHeaderKeys, TopWidths), 0);

            var list = new ListBox();
            list.ItemTemplate = new FuncDataTemplate<TopOperationRow>((row, _) => BuildTopRow(row));
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AvaloniaUi.Place(panel, list, 1);
            return panel;
        }

        private static Control BuildLocksTab()
        {
            var panel = AvaloniaUi.Rows(0, -1);
            AvaloniaUi.Place(panel, AvaloniaUi.Header(LockHeaderKeys, LockWidths), 0);

            var list = new ListBox();
            list.ItemTemplate = new FuncDataTemplate<LockSummaryRow>((row, _) => BuildLockRow(row));
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(TechLogAnalysisViewModel.Locks)));
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AvaloniaUi.Place(panel, list, 1);
            return panel;
        }

        private static Control BuildExceptionsTab()
        {
            var panel = AvaloniaUi.Rows(0, -1);
            AvaloniaUi.Place(panel, AvaloniaUi.Header(ExceptionHeaderKeys, ExceptionWidths), 0);

            var list = new ListBox();
            list.ItemTemplate = new FuncDataTemplate<ExceptionGroupRow>((row, _) => BuildExceptionRow(row));
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(TechLogAnalysisViewModel.Exceptions)));
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AvaloniaUi.Place(panel, list, 1);
            return panel;
        }

        private static Control BuildTopRow(TopOperationRow? row)
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(TopWidths), Height = 26 };
            if (row is null)
                return grid;
            grid.Children.Add(AvaloniaUi.Cell(row.Text, 0));
            grid.Children.Add(AvaloniaUi.Cell(row.Count.ToString(), 1));
            grid.Children.Add(AvaloniaUi.Cell(row.MaxMsText, 2));
            grid.Children.Add(AvaloniaUi.Cell(row.AvgMsText, 3));
            grid.Children.Add(AvaloniaUi.Cell(row.TotalMsText, 4));
            return grid;
        }

        private static Control BuildLockRow(LockSummaryRow? row)
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(LockWidths), Height = 26 };
            if (row is null)
                return grid;
            grid.Children.Add(AvaloniaUi.Cell(row.EventType, 0));
            grid.Children.Add(AvaloniaUi.Cell(row.Key, 1));
            grid.Children.Add(AvaloniaUi.Cell(row.Count.ToString(), 2));
            grid.Children.Add(AvaloniaUi.Cell(row.TotalMsText, 3));
            grid.Children.Add(AvaloniaUi.Cell(row.MaxMsText, 4));
            grid.Children.Add(AvaloniaUi.Cell(row.LastSeenText, 5));
            return grid;
        }

        private static Control BuildExceptionRow(ExceptionGroupRow? row)
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(ExceptionWidths), Height = 26 };
            if (row is null)
                return grid;
            grid.Children.Add(AvaloniaUi.Cell(row.Key, 0));
            grid.Children.Add(AvaloniaUi.Cell(row.Count.ToString(), 1));
            grid.Children.Add(AvaloniaUi.Cell(row.LastSeenText, 2));
            grid.Children.Add(AvaloniaUi.Cell(row.FirstContextLine, 3));
            return grid;
        }

        private void OnExport()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (_vm.BuildCsvDocument().Length == 0)
            {
                dialogs.ShowInfo(LocalizationManager.T("TechLog.Analysis.Export.Empty"), Title ?? string.Empty);
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
                    Title ?? string.Empty);
            }
            catch (Exception ex)
            {
                dialogs.ShowError(LocalizationManager.T("TechLog.Analysis.Export.Failed") + " " + ex.Message,
                    Title ?? string.Empty);
            }
        }
    }
}
#endif