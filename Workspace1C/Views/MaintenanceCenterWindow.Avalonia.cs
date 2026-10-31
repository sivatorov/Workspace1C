#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Центр обслуживания» (0.3.9.89, Avalonia/Linux): таблица всех баз со
    /// сводкой состояния (доступность, последняя копия, размер ИБ, кэш, конфигурация,
    /// возраст данных, проверка обновлений), фильтр-переключатель «Только проблемы»
    /// и кнопки «Проверить доступность» / «Открыть базу». Двойной клик по строке —
    /// переход к базе в главном окне (FindInList-механика). Вся логика — в чистой
    /// ViewModel <see cref="MaintenanceCenterViewModel"/>.
    /// </summary>
    public sealed class MaintenanceCenterWindow : ModalWindowBase
    {
        private readonly MaintenanceCenterViewModel _vm;
        private readonly ListBox _grid = new();
        private readonly TextBlock _hint;

        // Секция «История размеров и прогноз диска» (п.7, выпуск 1.0.0.89).
        private readonly RadioButton _period7 = new() { Content = "7" };
        private readonly RadioButton _period30 = new() { Content = "30", IsChecked = true };
        private readonly RadioButton _period90 = new() { Content = "90" };
        private readonly ListBox _historyList = new();
        private readonly TextBlock _historyEmptyText = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _forecastText = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _historyCaption = new() { FontWeight = Avalonia.Media.FontWeight.SemiBold };
        private readonly Canvas _historyChartCanvas = new();
        private readonly TextBlock _diskForecastText = new() { TextWrapping = TextWrapping.Wrap };

        /// <param name="infobases">Все базы списка.</param>
        /// <param name="checkAvailability">Запуск общей проверки доступности (команда главного окна).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        /// <param name="freeSpaceWarningGb">Порог предупреждения «Свободно на диске» в ГБ
        /// (0 — не предупреждать), по умолчанию 10 ГБ (0.3.9.96).</param>
        public MaintenanceCenterWindow(
            IEnumerable<Infobase> infobases,
            Action checkAvailability,
            Action<Infobase> openBase,
            int freeSpaceWarningGb = Services.DiskFreeSpaceHelper.DefaultWarningGb)
        {
            Title = LocalizationManager.T("Maintenance.Title");
            Width = 1080;
            Height = 840;
            MinWidth = 760;
            MinHeight = 660;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new MaintenanceCenterViewModel(
                infobases, checkAvailability, openBase, freeSpaceWarningGb);

            // Верхняя панель: заголовок + фильтр «Только проблемы».
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Maintenance.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var onlyProblems = new CheckBox
            {
                Content = LocalizationManager.T("Maintenance.OnlyProblems"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0)
            };
            onlyProblems.Bind(CheckBox.IsCheckedProperty, new Binding("OnlyProblems")
            {
                Mode = BindingMode.TwoWay
            });

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { title, onlyProblems }
            };

            var summary = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(0, 6, 0, 10)
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));

            // Таблица баз × состояние: шапка с фиксированными колонками и строки.
            _grid.ItemsSource = _vm.Rows;
            _grid.ItemTemplate = new FuncDataTemplate<MaintenanceCenterRowViewModel>((row, _) => BuildRow(row));
            _grid.DoubleTapped += OnGrid_DoubleTapped;
            ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
            // Подсветка ячейки «Свободно на диске» при нехватке места (0.3.9.96).
            _grid.Styles.Add(new Style(x => x.OfType<TextBlock>().Class("freeSpaceProblem"))
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, ThemeResource("ErrorBrush", new SolidColorBrush(Color.Parse("#DC2626")))),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold)
                }
            });

            _hint = new TextBlock
            {
                Text = _vm.HasBases
                    ? LocalizationManager.T("Maintenance.Hint")
                    : LocalizationManager.T("Maintenance.NoBases"),
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // Кнопки: «Проверить доступность», «Открыть базу», «Закрыть».
            var checkButton = BuildActionButton("IconSonar",
                LocalizationManager.T("Maintenance.CheckAvailability"), () => _vm.CheckAvailability());
            var openButton = BuildActionButton("IconShortcut",
                LocalizationManager.T("Maintenance.OpenBase"), OpenSelectedBase);
            openButton.Width = 150;
            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { checkButton, openButton, closeButton }
            };

            var historySection = BuildHistorySection();

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, header, 0);
            Place(grid, summary, 1);
            Place(grid, _grid, 2);
            Place(grid, historySection, 3);
            Place(grid, buttons, 4);
            Place(grid, _hint, 5);
            Content = grid;

            // Перерисовка графика при смене выбранной базы/периода (п.7, 1.0.0.89).
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _historyList.SelectionChanged += OnHistoryListSelectionChanged;
            _grid.SelectionChanged += (_, _) =>
            {
                // Одиночный клик по строке таблицы → график истории этой базы (п.7, 1.0.0.89).
                if (_grid.SelectedItem is MaintenanceCenterRowViewModel row)
                    _vm.SelectHistoryById(row.Infobase?.Id);
            };
            _historyChartCanvas.SizeChanged += (_, _) => DrawHistoryChart();
            foreach (var radio in new[] { _period7, _period30, _period90 })
                radio.IsCheckedChanged += OnHistoryPeriodChanged;
            _historyList.ItemsSource = _vm.HistoryRows;
            DrawHistoryChart();
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildActionButton(string iconKey, string text, System.Action onClick)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, text, 16, "TextPrimaryColorBrush"),
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), 14, "TextPrimaryBrush"),
                Width = 110,
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>Строка таблицы: имя с пояснением и восемь колонок состояния.</summary>
        private Control BuildRow(MaintenanceCenterRowViewModel model)
        {
            var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
            var subtitle = new TextBlock { FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.Bind(TextBlock.TextProperty, new Binding("Subtitle"));

            var nameCell = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 8, 2),
                Children = { name, subtitle }
            };

            var availability = CellText("AvailabilityText", bold: true);
            var lastBackup = CellText("LastBackupText");
            var size = CellText("SizeText");
            // «Свободно на диске» (0.3.9.96): при нехватке места ячейка подсвечивается
            // классом freeSpaceProblem (стиль задан в конструкторе окна).
            var freeSpace = CellText("FreeSpaceDisplay");
            var cache = CellText("CacheText");
            var config = CellText("ConfigurationText");
            var dataAge = CellText("DataAgeText");
            var update = CellText("UpdateText");

            freeSpace.Classes.Set("freeSpaceProblem", model.FreeSpaceIsProblem);
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MaintenanceCenterRowViewModel.FreeSpaceIsProblem))
                    freeSpace.Classes.Set("freeSpaceProblem", model.FreeSpaceIsProblem);
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.3, GridUnitType.Star))
                },
                Children = { nameCell, availability, lastBackup, size, freeSpace, cache, config, dataAge, update }
            };
            Grid.SetColumn(nameCell, 0);
            Grid.SetColumn(availability, 1);
            Grid.SetColumn(lastBackup, 2);
            Grid.SetColumn(size, 3);
            Grid.SetColumn(freeSpace, 4);
            Grid.SetColumn(cache, 5);
            Grid.SetColumn(config, 6);
            Grid.SetColumn(dataAge, 7);
            Grid.SetColumn(update, 8);
            return row;
        }

        private static Control CellText(string property, bool bold = false)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
            };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        // ---- Секция «История размеров и прогноз диска» (п.7, 1.0.0.89) ----

        private Control BuildHistorySection()
        {
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Maintenance.HistoryTitle"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(title, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

            var periodLabel = new TextBlock
            {
                Text = LocalizationManager.T("Maintenance.HistoryPeriod"),
                FontSize = 12,
                Margin = new Thickness(14, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(periodLabel, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            foreach (var radio in new[] { _period7, _period30, _period90 })
            {
                radio.GroupName = "HistoryPeriod";
                radio.Margin = new Thickness(6, 0, 0, 0);
                radio.VerticalAlignment = VerticalAlignment.Center;
            }

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    title, periodLabel, _period7, _period30, _period90
                }
            };

            // Топ баз по росту: имя + темп роста + текущий размер.
            _historyList.Height = 132;
            _historyList.Margin = new Thickness(0, 8, 0, 0);
            _historyList.ItemTemplate = new FuncDataTemplate<MaintenanceHistoryRow>((row, _) =>
            {
                var name = new TextBlock
                {
                    Text = row.Name,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var growth = new TextBlock
                {
                    Text = row.GrowthText,
                    FontSize = 11,
                    Foreground = ThemeResource("ErrorBrush", new SolidColorBrush(Color.Parse("#DC2626")))
                };
                var current = new TextBlock
                {
                    Text = row.CurrentSizeText,
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                ThemeBrushes.Bind(current, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
                var meta = new StackPanel { Orientation = Orientation.Horizontal, Children = { growth, current } };
                var cell = new StackPanel { Children = { name, meta } };
                return cell;
            });

            _historyEmptyText.FontSize = 12;
            _historyEmptyText.Margin = new Thickness(0, 8, 0, 0);
            _historyEmptyText.Bind(TextBlock.TextProperty, new Binding("HistoryEmptyText"));
            ThemeBrushes.Bind(_historyEmptyText, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            _forecastText.FontSize = 12;
            _forecastText.Margin = new Thickness(0, 6, 0, 0);
            _forecastText.Bind(TextBlock.TextProperty, new Binding("ForecastText"));
            ThemeBrushes.Bind(_forecastText, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

            var left = new StackPanel
            {
                Children = { headerRow, _historyList, _historyEmptyText, _forecastText }
            };

            // Правая колонка: график роста + прогноз диска под копиями.
            _historyCaption.HorizontalAlignment = HorizontalAlignment.Center;
            _historyCaption.Bind(TextBlock.TextProperty, new Binding("HistoryChartCaption"));
            ThemeBrushes.Bind(_historyCaption, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
            _historyChartCanvas.Height = 150;
            _historyChartCanvas.Margin = new Thickness(0, 4, 0, 0);
            _historyChartCanvas.Background = Brushes.Transparent;
            _diskForecastText.FontSize = 12;
            _diskForecastText.Margin = new Thickness(0, 4, 0, 0);
            _diskForecastText.Bind(TextBlock.TextProperty, new Binding("DiskForecastText"));
            ThemeBrushes.Bind(_diskForecastText, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");

            var right = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Children = { _historyCaption, _historyChartCanvas, _diskForecastText }
                }
            };
            ThemeBrushes.Bind(right, Border.BorderBrushProperty, "BorderBrush");
            ThemeBrushes.Bind(right, Border.BackgroundProperty, "CardBackgroundBrush");
            right.BorderThickness = new Thickness(1);

            var root = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(10),
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(330, GridUnitType.Pixel)),
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                    },
                    Children = { left, right }
                }
            };
            Grid.SetColumn(right, 1);
            ThemeBrushes.Bind(root, Border.BorderBrushProperty, "BorderBrush");
            root.BorderThickness = new Thickness(1);
            return root;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MaintenanceCenterViewModel.HistoryChart) ||
                e.PropertyName == nameof(MaintenanceCenterViewModel.HistoryEmptyText) ||
                e.PropertyName == nameof(MaintenanceCenterViewModel.DiskForecastText))
                DrawHistoryChart();
        }

        private void OnHistoryListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_historyList.SelectedItem is MaintenanceHistoryRow row)
                _vm.SelectHistory(row);
        }

        private void OnHistoryPeriodChanged(object? sender, RoutedEventArgs e)
        {
            var days = ReferenceEquals(sender, _period7) ? 7
                : ReferenceEquals(sender, _period90) ? 90
                : 30;
            _vm.SetHistoryPeriod(days);
        }

        /// <summary>
        /// Рисует простой линейный график роста ИБ на <see cref="Canvas"/> (без внешних
        /// библиотек): сетка с подписями Y, ось X с датами и полилиния точек.
        /// </summary>
        private void DrawHistoryChart()
        {
            _historyChartCanvas.Children.Clear();
            var chart = _vm.HistoryChart;
            if (chart is null || chart.Points.Count < 2)
                return;

            double w = Math.Max(60, _historyChartCanvas.Bounds.Width);
            double h = Math.Max(40, _historyChartCanvas.Bounds.Height);
            const double left = 58, right = 12, top = 8, bottom = 20;
            var plotW = w - left - right;
            var plotH = h - top - bottom;
            if (plotW <= 20 || plotH <= 20)
                return;

            // График на кистях темы (аудит UI, п. 2.7): сетка/подписи — TextSecondaryBrush,
            // столбцы — AccentBrush; паритет с WPF-версией.
            var lineBrush = ThemeResource("TextSecondaryBrush", new SolidColorBrush(Color.Parse("#B0B0B0")));
            var textBrush = ThemeResource("TextSecondaryBrush", new SolidColorBrush(Color.Parse("#888888")));
            var accentBrush = ThemeResource("AccentBrush", new SolidColorBrush(Color.Parse("#2D6CDF")));

            foreach (var (f, label) in new[] { (0.0, chart.YMinLabel), (1.0, chart.YMaxLabel) })
            {
                var y = top + (1 - f) * plotH;
                _historyChartCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(left, y),
                    EndPoint = new Point(left + plotW, y),
                    Stroke = lineBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 }
                });
                var yLabel = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    Width = left - 4
                };
                Canvas.SetLeft(yLabel, 0);
                Canvas.SetTop(yLabel, y - 7);
                _historyChartCanvas.Children.Add(yLabel);
            }

            _historyChartCanvas.Children.Add(new Line
            {
                StartPoint = new Point(left, top + plotH),
                EndPoint = new Point(left + plotW, top + plotH),
                Stroke = lineBrush,
                StrokeThickness = 1
            });
            foreach (var (f, label) in new[] { (0.0, chart.XMinLabel), (1.0, chart.XMaxLabel) })
            {
                var xLabel = new TextBlock { Text = label, FontSize = 10 };
                Canvas.SetLeft(xLabel, f == 0 ? left - 2 : left + plotW - 64);
                Canvas.SetTop(xLabel, top + plotH + 4);
                _historyChartCanvas.Children.Add(xLabel);
            }

            var polyline = new Polyline
            {
                Stroke = accentBrush,
                StrokeThickness = 2
            };
            foreach (var p in chart.Points)
            {
                polyline.Points.Add(new Point(left + p.X * plotW, top + (1 - p.Y) * plotH));
            }
            _historyChartCanvas.Children.Add(polyline);

            // Обводка маркера — фон карточки из темы (аудит UI) вместо фиксированного белого.
            var last = chart.Points[chart.Points.Count - 1];
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = accentBrush,
                Stroke = ThemeResource("CardBackgroundBrush", new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(dot, left + last.X * plotW - 3);
            Canvas.SetTop(dot, top + (1 - last.Y) * plotH - 3);
            _historyChartCanvas.Children.Add(dot);
        }

        /// <summary>Читает кисть темы по ключу; при отсутствии — запасное значение (аудит UI, п. 2.7).</summary>
        private static Avalonia.Media.IBrush ThemeResource(string key, Avalonia.Media.IBrush fallback)
            => Avalonia.Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true
                && value is Avalonia.Media.IBrush brush
                    ? brush
                    : fallback;

        private void OnGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_grid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        private void OpenSelectedBase()
        {
            if (_grid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        /// <summary>«Открыть базу»: переход к строке в главном окне и закрытие дашборда.</summary>
        private void OpenBaseAndClose(MaintenanceCenterRowViewModel row)
        {
            _vm.OpenBase(row);
            Close();
        }
    }
}
#endif