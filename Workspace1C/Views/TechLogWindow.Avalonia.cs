#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Технологический журнал» (выпуск 1.0.0.69, Avalonia/Linux).
    /// Логика — в <see cref="TechLogViewModel"/>.
    /// </summary>
    public sealed class TechLogWindow : ModalWindowBase
    {
        private readonly TechLogViewModel _vm;

        public TechLogWindow(TechLogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("TechLog.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 980;
            Height = 680;
            MinWidth = 760;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("TechLog.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("TechLog.Template"),
                AvaloniaUi.BoundCombo(_vm.TemplateOptions, nameof(TechLogViewModel.TemplateIndex), 360)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("TechLog.ConfDir"),
                AvaloniaUi.BoundTextBox(nameof(TechLogViewModel.ConfDirectory), 420),
                AvaloniaUi.Button("Common.Browse", (_, _) => Browse(true), 90)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("TechLog.LogDir"),
                AvaloniaUi.BoundTextBox(nameof(TechLogViewModel.LogDirectory), 420),
                AvaloniaUi.Button("Common.Browse", (_, _) => Browse(false), 90)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("TechLog.History"), AvaloniaUi.BoundTextBox(nameof(TechLogViewModel.HistoryText), 70),
                AvaloniaUi.Label("TechLog.Threshold"), AvaloniaUi.BoundTextBox(nameof(TechLogViewModel.ThresholdText), 90)));
            AvaloniaUi.Place(root, AvaloniaUi.Card(form), 1);

            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(TechLogViewModel.StatusText)), 2);

            var previews = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
                ColumnDefinitions = AvaloniaUi.Columns(-1, 12, -1),
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
            };
            AvaloniaUi.Place(previews, AvaloniaUi.Label("TechLog.Preview"), 0, 0);
            AvaloniaUi.Place(previews, AvaloniaUi.Label("TechLog.Current"), 0, 2);
            AvaloniaUi.Place(previews, AvaloniaUi.ReadOnlyText(nameof(TechLogViewModel.PreviewXml)), 1, 0);
            AvaloniaUi.Place(previews, AvaloniaUi.ReadOnlyText(nameof(TechLogViewModel.CurrentXml)), 1, 2);
            AvaloniaUi.Place(root, previews, 3);

            var enable = AvaloniaUi.Button("TechLog.Enable", (_, _) => Report(_vm.Enable(), "TechLog.Enabled"), 150, accent: true);
            var disable = AvaloniaUi.Button("TechLog.Disable", (_, _) => Report(_vm.Disable(), "TechLog.Disabled"), 150);
            var analyze = AvaloniaUi.Button("TechLog.Analyze", (_, _) => OpenAnalysis(), 160);
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(enable, disable, analyze, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 4);

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
                dialogs.ShowInfo(LocalizationManager.T(okKey), Title ?? string.Empty);
            else
                dialogs.ShowError(error, Title ?? string.Empty);
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
                dialogs.ShowWarning(LocalizationManager.T("TechLog.LogDirRequired"), Title ?? string.Empty);
                return;
            }
            if (!System.IO.Directory.Exists(_vm.LogDirectory))
            {
                dialogs.ShowWarning(LocalizationManager.T("TechLog.Analysis.DirNotFound"), Title ?? string.Empty);
                return;
            }

            var viewModel = new TechLogAnalysisViewModel(_vm.LogDirectory, _vm.ThresholdMs);
            var window = new TechLogAnalysisWindow(viewModel);
            _ = window.ShowDialog(this);
        }
    }
}
#endif
