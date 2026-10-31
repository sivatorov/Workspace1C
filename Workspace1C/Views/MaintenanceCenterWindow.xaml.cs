using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Центр обслуживания» (0.3.9.89, Windows/WPF): таблица всех баз со сводкой
    /// состояния (доступность, последняя копия, размер ИБ, кэш, конфигурация, возраст
    /// данных, проверка обновлений), фильтр-переключатель «Только проблемы» и кнопки
    /// «Проверить доступность» / «Открыть базу». Двойной клик по строке — переход к
    /// базе в главном окне (FindInList-механика). Вся логика — в чистой ViewModel
    /// <see cref="MaintenanceCenterViewModel"/>.
    /// </summary>
    public partial class MaintenanceCenterWindow : Window
    {
        private readonly MaintenanceCenterViewModel _vm;

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
            InitializeComponent();
            _vm = new MaintenanceCenterViewModel(infobases, checkAvailability, openBase, freeSpaceWarningGb);
            DataContext = _vm;

            Title = LocalizationManager.T("Maintenance.Title");
            BasesGrid.ItemsSource = _vm.Rows;
            HistoryList.ItemsSource = _vm.HistoryRows;
            // Одиночный клик по строке таблицы → график истории этой базы (п.7, 1.0.0.89).
            BasesGrid.SelectionChanged += OnBasesGridSelectionChanged;

            // Перерисовка графика при смене выбранной базы/периода (п.7, 1.0.0.89).
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            DrawHistoryChart();

            if (!_vm.HasBases)
                HintText.Text = LocalizationManager.T("Maintenance.NoBases");
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MaintenanceCenterViewModel.HistoryChart) ||
                e.PropertyName == nameof(MaintenanceCenterViewModel.HistoryEmptyText) ||
                e.PropertyName == nameof(MaintenanceCenterViewModel.DiskForecastText))
                DrawHistoryChart();
        }

        private void OnHistoryPeriodChanged(object sender, RoutedEventArgs e)
        {
            var days = ReferenceEquals(sender, Period7Radio) ? 7
                : ReferenceEquals(sender, Period90Radio) ? 90
                : 30;
            _vm.SetHistoryPeriod(days);
        }

        private void OnBasesGridSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BasesGrid.SelectedItem is MaintenanceCenterRowViewModel row)
                _vm.SelectHistoryById(row.Infobase?.Id);
        }

        private void OnHistoryChartSizeChanged(object sender, SizeChangedEventArgs e)
            => DrawHistoryChart();

        /// <summary>
        /// Рисует простой линейный график роста размера ИБ на <see cref="Canvas"/>
        /// (без внешних графических библиотек): оси, сетка с подписями Y, полилиния точек.
        /// </summary>
        private void DrawHistoryChart()
        {
            if (HistoryChartCanvas is null)
                return;
            HistoryChartCanvas.Children.Clear();
            var chart = _vm.HistoryChart;
            if (chart is null || chart.Points.Count < 2)
                return;

            double w = Math.Max(60, HistoryChartCanvas.ActualWidth);
            double h = Math.Max(40, HistoryChartCanvas.ActualHeight);
            const double left = 58, right = 12, top = 8, bottom = 20;
            var plotW = w - left - right;
            var plotH = h - top - bottom;
            if (plotW <= 20 || plotH <= 20)
                return;

            // График на кистях темы (аудит UI, п. 2.7): сетка/подписи — TextSecondaryBrush,
            // столбцы — AccentBrush; паритет с Avalonia-версией.
            var lineBrush = TryFindResource("TextSecondaryBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            var textBrush = TryFindResource("TextSecondaryBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            var accentBrush = TryFindResource("AccentBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF));

            // Горизонтальная сетка + подписи минимума/максимума Y.
            foreach (var (f, label) in new[] { (0.0, chart.YMinLabel), (1.0, chart.YMaxLabel) })
            {
                var y = top + (1 - f) * plotH;
                var gridLine = new Line
                {
                    X1 = left, Y1 = y, X2 = left + plotW, Y2 = y,
                    Stroke = lineBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }
                };
                HistoryChartCanvas.Children.Add(gridLine);
                var yLabel = new TextBlock
                {
                    Text = label, FontSize = 10, Foreground = textBrush,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(yLabel, 0);
                Canvas.SetTop(yLabel, y - 7);
                yLabel.Width = left - 4;
                HistoryChartCanvas.Children.Add(yLabel);
            }

            // Абсцисса и подписи дат первого/последнего снимка.
            var axis = new Line { X1 = left, Y1 = top + plotH, X2 = left + plotW, Y2 = top + plotH, Stroke = lineBrush, StrokeThickness = 1 };
            HistoryChartCanvas.Children.Add(axis);
            foreach (var (f, label) in new[] { (0.0, chart.XMinLabel), (1.0, chart.XMaxLabel) })
            {
                var xLabel = new TextBlock { Text = label, FontSize = 10, Foreground = textBrush };
                Canvas.SetLeft(xLabel, f == 0 ? left - 2 : left + plotW - 64);
                Canvas.SetTop(xLabel, top + plotH + 4);
                HistoryChartCanvas.Children.Add(xLabel);
            }

            // Градиентная линия (без точек-узлов при большом числе снимков).
            var polyline = new Polyline
            {
                Stroke = accentBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            for (var i = 0; i < chart.Points.Count; i++)
            {
                var p = chart.Points[i];
                var x = left + p.X * plotW;
                var y = top + (1 - p.Y) * plotH;
                polyline.Points.Add(new System.Windows.Point(x, y));
            }
            HistoryChartCanvas.Children.Add(polyline);

            // Последняя точка — маркер (текущий размер).
            var last = chart.Points[chart.Points.Count - 1];
            // Обводка маркера — фон карточки из темы (аудит UI) вместо фиксированного белого.
            var dot = new Ellipse
            {
                Width = 6, Height = 6,
                Fill = accentBrush,
                Stroke = (TryFindResource("CardBackgroundBrush") as Brush) ?? Brushes.White,
                StrokeThickness = 1
            };
            Canvas.SetLeft(dot, left + last.X * plotW - 3);
            Canvas.SetTop(dot, top + (1 - last.Y) * plotH - 3);
            HistoryChartCanvas.Children.Add(dot);
        }

        private void OnCheckAvailability_Click(object sender, RoutedEventArgs e) => _vm.CheckAvailability();

        private void OnOpenBase_Click(object sender, RoutedEventArgs e) => OpenSelectedBase();

        private void OnGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (BasesGrid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        private void OpenSelectedBase()
        {
            if (BasesGrid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        /// <summary>«Открыть базу»: переход к строке в главном окне и закрытие дашборда.</summary>
        private void OpenBaseAndClose(MaintenanceCenterRowViewModel row)
        {
            _vm.OpenBase(row);
            Close();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}