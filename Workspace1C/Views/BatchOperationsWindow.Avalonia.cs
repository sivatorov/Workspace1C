#if LINUX
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Массовые операции по списку баз» (issue #91, Avalonia/Linux): выгрузка
    /// списка баз в файлы .dt в выбранный каталог и загрузка конфигурации из .cf
    /// по списку баз. Список баз с флажками, путь (каталог/.cf), последовательный
    /// прогон через ibcmd с построчным логом. Вся логика — в чистой ViewModel
    /// <see cref="BatchOperationsViewModel"/>. Третья фича issue (массовая загрузка
    /// внешних отчётов/обработок) сознательно не включена — отложена.
    /// </summary>
    public sealed class BatchOperationsWindow : ModalWindowBase
    {
        private readonly BatchOperationsViewModel _vm;
        private readonly ListBox _itemsList = new();
        private readonly ListBox _logList = new() { IsHitTestVisible = false };
        private readonly TextBlock _hint;
        private readonly TextBlock _summary;
        private readonly TextBox _pathBox = new();
        private readonly Button _runButton = new();
        private readonly Button _browseButton = new();
        private readonly Button _stopButton = new() { IsEnabled = false };
        private CancellationTokenSource? _cts;

        public BatchOperationsWindow(System.Collections.Generic.IEnumerable<Infobase> infobases, BatchOperationKind kind)
        {
            Title = LocalizationManager.T(kind == BatchOperationKind.DumpDt
                ? "BatchOps.TitleDumpDt" : "BatchOps.TitleLoadCf");
            Width = 700;
            Height = 620;
            MinWidth = 580;
            MinHeight = 460;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new BatchOperationsViewModel(infobases, kind);

            ScrollViewer.SetHorizontalScrollBarVisibility(_itemsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(_logList, ScrollBarVisibility.Disabled);

            _hint = new TextBlock
            {
                Text = LocalizationManager.T(kind == BatchOperationKind.DumpDt
                    ? "BatchOps.HintDumpDt" : "BatchOps.HintLoadCf"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.75
            };

            _itemsList.ItemsSource = _vm.Items;
            _itemsList.ItemTemplate = new FuncDataTemplate<BatchOperationsItem>((_, _) => BuildRow());

            // «Выделить все / Снять все».
            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"), () => _vm.SetAll(true)));
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.None"), () => _vm.SetAll(false)));

            // Путь: каталог выгрузки .dt или файл конфигурации .cf.
            var pathLabel = new TextBlock
            {
                Text = _vm.PathLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _pathBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("PathText") { Mode = Avalonia.Data.BindingMode.TwoWay });
            _browseButton.Content = "…";
            _browseButton.MinWidth = 34;
            _browseButton.Styled(ControlThemes.ModernButton);
            _browseButton.Click += async (_, _) => await BrowseAsync();
            var pathRow = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(pathLabel, 0);
            Grid.SetColumn(_pathBox, 1);
            Grid.SetColumn(_browseButton, 2);
            pathRow.Children.Add(pathLabel);
            pathRow.Children.Add(_pathBox);
            pathRow.Children.Add(_browseButton);

            var logHeader = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Log"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 6)
            };

            _logList.ItemsSource = _vm.LogLines;
            _logList.ItemTemplate = new FuncDataTemplate<string>((_, _) =>
            {
                var line = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(2, 1),
                    Opacity = 0.75
                };
                line.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(""));
                return line;
            });

            _summary = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            _summary.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Summary"));

            // «Остановить» + «Закрыть» + «Выполнить».
            _stopButton.Content = IconHelper.IconAndText("IconClose",
                LocalizationManager.T("BatchOps.Stop"), 14, "TextPrimaryBrush");
            _stopButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(_stopButton, Button.BackgroundProperty, "ItemHoverBrush");
            _stopButton.Click += (_, _) => _cts?.Cancel();

            var closeButton = BuildCloseButton();

            _runButton.Content = IconHelper.IconAndText("IconImport",
                LocalizationManager.T("BatchOps.Execute"), 16, "ButtonTextBrush");
            _runButton.Styled(ControlThemes.ModernButton);
            _runButton.Width = 200;
            _runButton.IsDefault = true;
            _runButton.Click += async (_, _) => await RunAsyncSafe();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { _stopButton, closeButton, _runButton }
            };

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, _hint, 0);
            Place(grid, _itemsList, 1);
            Place(grid, bulk, 2);
            Place(grid, pathRow, 3);
            Place(grid, logHeader, 4);
            Place(grid, _logList, 5);
            Place(grid, _summary, 6);
            Place(grid, buttons, 7);
            Content = grid;

            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildSecondaryButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6) };
            button.Styled(Themes.ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), 14, "TextPrimaryBrush"),
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>Строка списка: флажок + имя, пояснение и статус.</summary>
        private Control BuildRow()
        {
            var check = new CheckBox { Margin = new Thickness(2, 4) };
            check.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding("IsChecked") { Mode = Avalonia.Data.BindingMode.TwoWay });
            check.Bind(CheckBox.IsEnabledProperty, new Avalonia.Data.Binding("IsSelectable"));

            var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Title"));
            var subtitle = new TextBlock { FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Subtitle"));
            var status = new TextBlock { FontSize = 11, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
            status.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("StatusText"));

            check.Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { title, subtitle, status }
            };
            return check;
        }

        /// <summary>Выбор пути: каталог выгрузки (.dt) или файл конфигурации (.cf).</summary>
        private async Task BrowseAsync()
        {
            if (StorageProvider is null)
                return;
            if (_vm.Kind == BatchOperationKind.DumpDt)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = LocalizationManager.T("BatchOps.BrowseDirectoryTitle"),
                    AllowMultiple = false
                });
                if (folders.Count > 0)
                    _vm.PathText = folders[0].Path.LocalPath;
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = LocalizationManager.T("BatchOps.BrowseCfTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = new System.Collections.Generic.List<FilePickerFileType>
                    {
                        new FilePickerFileType("cf") { Patterns = new[] { "*.cf" } }
                    }
                });
                if (files.Count > 0)
                    _vm.PathText = files[0].Path.LocalPath;
            }
        }

        private async Task RunAsyncSafe()
        {
            _cts = new CancellationTokenSource();
            try
            {
                await _vm.RunAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Остановка пользователем: строки помечены, окно остаётся открытым.
            }
            catch
            {
                // VM уже пишет ошибки в лог; здесь только страхуемся от падения окна.
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchOperationsViewModel.IsRunning))
            {
                _runButton.IsEnabled = !_vm.IsRunning;
                _browseButton.IsEnabled = !_vm.IsRunning;
                _stopButton.IsEnabled = _vm.IsRunning;
                foreach (var item in _vm.Items)
                    item.IsBusy = _vm.IsRunning;
            }
        }

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.LogLines.Count > 0)
                _logList.ScrollIntoView(_vm.LogLines[_vm.LogLines.Count - 1]);
        }
    }
}
#endif
