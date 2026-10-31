#if LINUX
using System;
using System.Collections.Generic;
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
    /// Окно «Статистика использования» (0.3.9.95, Avalonia/Linux): аналитика по истории
    /// запусков всех баз — таблица (число запусков, последний/первый запуск, дней
    /// с последнего запуска), фильтр-переключатель «Только используемые», сводка
    /// и распределение запусков по дням недели. Вся логика — в чистой ViewModel
    /// <see cref="UsageStatisticsViewModel"/>.
    /// </summary>
    public sealed class UsageStatisticsWindow : ModalWindowBase
    {
        private static readonly string[] HeaderKeys =
        {
            "Stats.BaseName", "Stats.LaunchCount", "Stats.LastLaunch",
            "Stats.FirstLaunch", "Stats.DaysSince"
        };

        private static readonly double[] ColumnWidths = { 2, 0.8, 1.2, 1.2, 1.2 };

        private readonly UsageStatisticsViewModel _vm;
        private readonly ListBox _grid = new();

        /// <param name="infobases">Все базы списка (с их историей запусков).</param>
        public UsageStatisticsWindow(IEnumerable<Infobase> infobases)
        {
            Title = LocalizationManager.T("Stats.Title");
            Width = 1080;
            Height = 640;
            MinWidth = 820;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            DataContext = _vm = new UsageStatisticsViewModel(infobases);

            // Верхняя панель: заголовок + фильтр «Только используемые».
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Stats.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var usedOnly = new CheckBox
            {
                Content = LocalizationManager.T("Stats.UsedOnly"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0)
            };
            usedOnly.Bind(CheckBox.IsCheckedProperty, new Binding("UsedOnly")
            {
                Mode = BindingMode.TwoWay
            });

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { title, usedOnly }
            };

            var summary = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(0, 6, 0, 2)
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));

            var topBase = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(0, 0, 0, 10)
            };
            topBase.Bind(TextBlock.TextProperty, new Binding("TopBaseText"));

            // Таблица: шапка колонок и строки-гриды с теми же пропорциями колонок.
            var table = new StackPanel();
            table.Children.Add(BuildColumnHeader());
            _grid.ItemsSource = _vm.Rows;
            _grid.ItemTemplate = new FuncDataTemplate<UsageStatisticsRowViewModel>((_, _) => BuildRow());
            ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);

            // Распределение запусков по дням недели.
            var barsTitle = new TextBlock
            {
                Text = LocalizationManager.T("Stats.WeekdayDistribution"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            // Issue #61: заголовок секции — кисть темы, а не фиксированный серый.
            ThemeBrushes.Bind(barsTitle, TextBlock.ForegroundProperty, "FormLabelBrush");
            var barsHost = new ItemsControl { ItemsSource = _vm.DayOfWeekBars };
            barsHost.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 1)
            });
            var barsPanel = new StackPanel { Children = { barsTitle, barsHost } };
            var barsBox = new Border
            {
                Background = Themes.ThemeBrushes.Get("CardBackgroundColorBrush", "#FFFFFF"),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 12, 0, 0),
                Child = barsPanel
            };

            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { closeButton }
            };

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, header, 0);
            Place(grid, summary, 1);
            Place(grid, topBase, 2);
            Place(grid, table, 3);
            Place(grid, barsBox, 4);
            Place(grid, buttons, 5);
            Content = grid;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
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

        /// <summary>Шапка таблицы: та же сетка колонок, что и у строк.</summary>
        private Control BuildColumnHeader()
        {
            var grid = new Grid
            {
                Margin = new Thickness(4, 0, 4, 4),
                ColumnDefinitions = BuildColumns()
            };
            for (var i = 0; i < HeaderKeys.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = LocalizationManager.T(HeaderKeys[i]),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Opacity = 0.6,
                    Margin = new Thickness(8, 0)
                };
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        /// <summary>Строка таблицы: имя базы и четыре числовых/текстовых колонки.</summary>
        private Control BuildRow()
        {
            var name = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            name.Bind(TextBlock.TextProperty, new Binding("Name"));

            var launchCount = CellText("LaunchCount", bold: true);
            var lastLaunch = CellText("LastLaunchText");
            var firstLaunch = CellText("FirstLaunchText");
            var daysSince = CellText("DaysSinceText");

            var row = new Grid
            {
                ColumnDefinitions = BuildColumns(),
                Children = { name, launchCount, lastLaunch, firstLaunch, daysSince }
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(launchCount, 1);
            Grid.SetColumn(lastLaunch, 2);
            Grid.SetColumn(firstLaunch, 3);
            Grid.SetColumn(daysSince, 4);
            return row;
        }

        private static ColumnDefinitions BuildColumns()
        {
            var columns = new ColumnDefinitions();
            foreach (var width in ColumnWidths)
                columns.Add(width == 0.8
                    ? new ColumnDefinition(new GridLength(90))
                    : new ColumnDefinition(new GridLength(width, GridUnitType.Star)));
            return columns;
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
    }
}
#endif