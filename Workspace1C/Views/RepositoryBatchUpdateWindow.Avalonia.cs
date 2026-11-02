#if LINUX
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Обновление из хранилищ» (0.3.9.88, Avalonia/Linux): список баз с
    /// заполненным хранилищем конфигурации, чекбоксы, последовательный прогон
    /// выбранных через конфигуратор в пакетном режиме и построчный лог.
    /// Вся логика — в чистой ViewModel <see cref="RepositoryBatchUpdateViewModel"/>.
    /// </summary>
    public sealed class RepositoryBatchUpdateWindow : ModalWindowBase
    {
        private readonly RepositoryBatchUpdateViewModel _vm;
        private readonly ListBox _itemsList = new();
        private readonly ListBox _logList = new() { IsHitTestVisible = false };
        private readonly TextBlock _hint;
        private readonly TextBlock _summary;
        private readonly Button _updateButton = new();

        public RepositoryBatchUpdateWindow(IEnumerable<Infobase> infobases)
        {
            Title = LocalizationManager.T("RepoUpdate.Title");
            Width = 640;
            Height = 560;
            MinWidth = 520;
            MinHeight = 420;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new RepositoryBatchUpdateViewModel(infobases);

            ScrollViewer.SetHorizontalScrollBarVisibility(_itemsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(_logList, ScrollBarVisibility.Disabled);

            _hint = new TextBlock
            {
                Text = _vm.HasItems
                    ? LocalizationManager.T("RepoUpdate.Hint")
                    : LocalizationManager.T("RepoUpdate.NoBases"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.75
            };

            _itemsList.ItemsSource = _vm.Items;
            _itemsList.ItemTemplate = new FuncDataTemplate<RepositoryBatchItem>((_, _) => BuildRow());

            // «Выделить все / Снять все».
            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"), () => _vm.SetAll(true)));
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.None"), () => _vm.SetAll(false)));

            var logHeader = new TextBlock
            {
                Text = LocalizationManager.T("RepoUpdate.Log"),
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
                line.Bind(TextBlock.TextProperty, new Binding(""));
                return line;
            });

            _summary = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            _summary.Bind(TextBlock.TextProperty, new Binding("Summary"));

            // «Обновить выбранные» + «Закрыть».
            var closeButton = BuildCloseButton();
            _updateButton.Content = IconHelper.IconAndText("IconCloudDownload",
                LocalizationManager.T("RepoUpdate.UpdateSelected"), 16, "ButtonTextBrush");
            _updateButton.Styled(ControlThemes.ModernButton);
            _updateButton.Width = 200;
            _updateButton.IsDefault = true;
            _updateButton.Click += async (_, _) => await RunAsyncSafe();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { _updateButton, closeButton } // issue #98: «Закрыть» последней
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
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, _hint, 0);
            Place(grid, _itemsList, 1);
            Place(grid, bulk, 2);
            Place(grid, logHeader, 3);
            Place(grid, _logList, 4);
            Place(grid, _summary, 5);
            Place(grid, buttons, 6);
            Content = grid;

            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildSecondaryButton(string text, System.Action onClick)
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
            check.Bind(CheckBox.IsCheckedProperty, new Binding("IsChecked") { Mode = BindingMode.TwoWay });
            check.Bind(CheckBox.IsEnabledProperty, new Binding("IsSelectable"));

            var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Bind(TextBlock.TextProperty, new Binding("Title"));
            var subtitle = new TextBlock { FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.Bind(TextBlock.TextProperty, new Binding("Subtitle"));
            var status = new TextBlock { FontSize = 11, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));

            check.Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { title, subtitle, status }
            };
            return check;
        }

        private async Task RunAsyncSafe()
        {
            try
            {
                await _vm.RunAsync();
            }
            catch
            {
                // VM уже пишет ошибки в лог; здесь только страхуемся от падения окна.
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RepositoryBatchUpdateViewModel.IsRunning))
            {
                _updateButton.IsEnabled = !_vm.IsRunning;
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