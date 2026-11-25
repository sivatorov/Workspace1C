#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Технологический журнал» (выпуск 1.0.0.69, Windows/WPF).
    /// Логика — в <see cref="TechLogViewModel"/>.
    /// </summary>
    public sealed class TechLogWindow : Window
    {
        private readonly TechLogViewModel _vm;

        public TechLogWindow(TechLogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // Issue #119 (1.0.1.119): стартовый размер уменьшен по таблице 1.4 плана.
            WpfUi.SetupWindow(this, "TechLog.Title", 980, 680);

            var root = WpfUi.Rows(0, 0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("TechLog.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("TechLog.Template"),
                WpfUi.BoundCombo(_vm.TemplateOptions, nameof(TechLogViewModel.TemplateIndex), 360)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("TechLog.ConfDir"),
                WpfUi.BoundTextBox(nameof(TechLogViewModel.ConfDirectory), 420),
                WpfUi.Button("Common.Browse", (_, _) => Browse(true), 90)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("TechLog.LogDir"),
                WpfUi.BoundTextBox(nameof(TechLogViewModel.LogDirectory), 420),
                WpfUi.Button("Common.Browse", (_, _) => Browse(false), 90)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("TechLog.History"), WpfUi.BoundTextBox(nameof(TechLogViewModel.HistoryText), 70),
                WpfUi.Label("TechLog.Threshold"), WpfUi.BoundTextBox(nameof(TechLogViewModel.ThresholdText), 90)));
            WpfUi.Place(root, WpfUi.Card(form), 1);

            WpfUi.Place(root, WpfUi.BoundText(nameof(TechLogViewModel.StatusText)), 2);

            var previews = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previews.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previews.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            WpfUi.Place(previews, WpfUi.Label("TechLog.Preview"), 0, 0);
            WpfUi.Place(previews, WpfUi.Label("TechLog.Current"), 0, 2);
            WpfUi.Place(previews, WpfUi.ReadOnlyText(nameof(TechLogViewModel.PreviewXml)), 1, 0);
            WpfUi.Place(previews, WpfUi.ReadOnlyText(nameof(TechLogViewModel.CurrentXml)), 1, 2);
            WpfUi.Place(root, previews, 3);

            var enable = WpfUi.Button("TechLog.Enable", (_, _) => Report(_vm.Enable(), "TechLog.Enabled"), 150, accent: true);
            var disable = WpfUi.Button("TechLog.Disable", (_, _) => Report(_vm.Disable(), "TechLog.Disabled"), 150);
            var analyze = WpfUi.Button("TechLog.Analyze", (_, _) => OpenAnalysis(), 160);
            var openLogs = WpfUi.Button("TechLog.OpenLogs", (_, _) => OpenFolder(_vm.LogDirectory), 160);
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(enable, disable, analyze, openLogs, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 4);

            Content = root;
        }

        private void Browse(bool conf)
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var folder = dialogs.OpenFolderDialog(LocalizationManager.T(conf ? "TechLog.ConfDir" : "TechLog.LogDir"),
                conf ? _vm.ConfDirectory : _vm.LogDirectory);
            if (string.IsNullOrWhiteSpace(folder))
                return;
            if (conf) _vm.ConfDirectory = folder; else _vm.LogDirectory = folder;
        }

        private void Report(string? error, string okKey)
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (error is null)
                dialogs.ShowInfo(LocalizationManager.T(okKey), Title);
            else
                dialogs.ShowError(error, Title);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!System.IO.Directory.Exists(path))
                    System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppServices.GetRequiredService<IDialogService>().ShowError(ex.Message, Title);
            }
        }

        /// <summary>
        /// Открывает окно анализа уже собранных логов из каталога журнала
        /// (порог длительности переносится из текущей формы, выпуск 1.0.0.86).
        /// </summary>
        private void OpenAnalysis()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            if (string.IsNullOrWhiteSpace(_vm.LogDirectory))
            {
                dialogs.ShowWarning(LocalizationManager.T("TechLog.LogDirRequired"), Title);
                return;
            }
            if (!System.IO.Directory.Exists(_vm.LogDirectory))
            {
                dialogs.ShowWarning(LocalizationManager.T("TechLog.Analysis.DirNotFound"), Title);
                return;
            }

            var viewModel = new TechLogAnalysisViewModel(_vm.LogDirectory, _vm.ThresholdMs);
            var window = new TechLogAnalysisWindow(viewModel) { Owner = this };
            window.ShowDialog();
        }
    }
}
#endif
