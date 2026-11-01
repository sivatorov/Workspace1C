#if LINUX
using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Помощники построения окон Avalonia в коде (выпуск 1.0.0.69) — общий набор
    /// для новых окон («Журнал регистрации», «Расширения» и др.), парный
    /// <c>WpfUi</c> для Windows. Стили берутся из <see cref="ControlThemes"/>.
    /// </summary>
    internal static class AvaloniaUi
    {
        public static TextBlock Text(string text, double fontSize = 13, bool bold = false, bool secondary = false)
        {
            // Issue #61: подписи и тексты окон, собранных в коде, привязываются к кисти
            // темы явно (аналог WpfUi.Text). Раньше текст полагался на глобальный стиль
            // TextBlock и оставался ЧЁРНЫМ, если стиль не доходил до программного дерева, —
            // «чёрное на тёмном» в тёмной теме (окно публикации, TechLog, DBMS, Телеграм-бот).
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty,
                secondary ? "TextSecondaryBrush" : "TextPrimaryBrush");
            return block;
        }

        /// <summary>Подпись по ключу локализации. Кисть — «подпись поля формы»
        /// (<c>FormLabelBrush</c>, issue #61): всегда контрастна в обеих темах, включая
        /// стенерированные схемы (вместо вторичной кисти, которая в тёмной теме могла
        /// оказаться тёмной у пользовательских схем).</summary>
        public static TextBlock Label(string key, double fontSize = 12)
        {
            var block = Text(LocalizationManager.T(key), fontSize, secondary: true);
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Подпись поля формы по строке (не по ключу локализации). Кисть —
        /// <c>FormLabelBrush</c> (issue #61): единая точка для подписей полей, собранных
        /// кодом (Field/Place/MakeLabel в окнах), чтобы они не остались чёрными в тёмной
        /// теме и пользовательских схемах.</summary>
        public static TextBlock FieldLabel(string text, double fontSize = 12)
        {
            var block = Text(text, fontSize, secondary: false);
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        public static TextBlock BoundText(string path, double fontSize = 12, bool secondary = true)
        {
            var block = Text(string.Empty, fontSize, secondary: secondary);
            block.Bind(TextBlock.TextProperty, new Binding(path));
            return block;
        }

        public static Button Button(string key, EventHandler<RoutedEventArgs> onClick, double minWidth = 110, bool accent = false)
        {
            var button = new Button
            {
                Content = LocalizationManager.T(key),
                MinWidth = minWidth,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Styled(accent ? ControlThemes.ModernButton : ControlThemes.SecondaryButton);
            button.Click += onClick;
            return button;
        }

        public static TextBox BoundTextBox(string path, double width = 160)
        {
            var box = new TextBox { Width = width, Margin = new Thickness(4, 0, 12, 0) }.Styled(ControlThemes.ModernTextBox);
            box.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            return box;
        }

        public static TextBox BoundPassword(string path, double width = 160)
        {
            var box = BoundTextBox(path, width);
            box.PasswordChar = '•';
            return box;
        }

        public static CheckBox BoundCheckBox(string key, string path)
        {
            var box = new CheckBox
            {
                Content = LocalizationManager.T(key),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            box.Bind(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            return box;
        }

        public static ComboBox BoundCombo(IEnumerable items, string indexPath, double width = 150)
        {
            var combo = new ComboBox
            {
                ItemsSource = items,
                Width = width,
                Margin = new Thickness(4, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            }.Styled(ControlThemes.ModernComboBox);
            combo.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(indexPath) { Mode = BindingMode.TwoWay });
            return combo;
        }

        public static WrapPanel Wrap(params Control[] children)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var child in children)
            {
                if (child.Margin == default)
                    child.Margin = new Thickness(0, 4, 8, 4);
                panel.Children.Add(child);
            }
            return panel;
        }

        /// <summary>Сетка со строками: 0 — Auto, &lt;0 — Star (вес), &gt;0 — пиксели.</summary>
        public static Grid Rows(params double[] heights)
        {
            var grid = new Grid();
            foreach (var h in heights)
            {
                grid.RowDefinitions.Add(new RowDefinition(
                    h == 0 ? GridLength.Auto
                    : h < 0 ? new GridLength(-h, GridUnitType.Star)
                    : new GridLength(h)));
            }
            return grid;
        }

        /// <summary>Сетка с колонками: 0 — Auto, &lt;0 — Star (вес), &gt;0 — пиксели.</summary>
        public static ColumnDefinitions Columns(params double[] widths)
        {
            var columns = new ColumnDefinitions();
            foreach (var w in widths)
            {
                columns.Add(new ColumnDefinition(
                    w == 0 ? GridLength.Auto
                    : w < 0 ? new GridLength(-w, GridUnitType.Star)
                    : new GridLength(w)));
            }
            return columns;
        }

        public static T Place<T>(Grid grid, T control, int row, int column = 0, int columnSpan = 1) where T : Control
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            if (columnSpan > 1)
                Grid.SetColumnSpan(control, columnSpan);
            grid.Children.Add(control);
            return control;
        }

        /// <summary>Ячейка строки таблицы с обрезкой текста.</summary>
        public static TextBlock Cell(string? text, int column, bool bold = false, IBrush? foreground = null)
        {
            var cell = new TextBlock
            {
                Text = text ?? string.Empty,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0),
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
            };
            if (foreground is not null)
                cell.Foreground = foreground;
            else
                ThemeBrushes.Bind(cell, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(cell, column);
            return cell;
        }

        /// <summary>Шапка таблицы с той же сеткой колонок, что и у строк.</summary>
        public static Grid Header(string[] keys, double[] widths)
        {
            var grid = new Grid { ColumnDefinitions = Columns(widths), Margin = new Thickness(4, 0, 4, 4) };
            for (var i = 0; i < keys.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = LocalizationManager.T(keys[i]),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                // Issue #61: шапки таблиц, как и подписи, привязываются к кисти темы.
                ThemeBrushes.Bind(cell, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        /// <summary>Многострочное поле только для чтения с привязкой.</summary>
        public static TextBox ReadOnlyText(string path)
        {
            var box = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                FontFamily = new FontFamily("monospace"),
                FontSize = 12
            };
            box.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });
            return box;
        }

        public static TextBlock Section(string key)
        {
            // Issue #61: заголовки секций, как и подписи полей, привязываются к
            // FormLabelBrush — читаемы в обеих темах и пользовательских схемах.
            var block = Text(LocalizationManager.T(key), 13, bold: true);
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        public static Border Card(Control child)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = child
            };
            ThemeBrushes.Bind(border, Border.BorderBrushProperty, "BorderBrush");
            return border;
        }

        public static StackPanel ButtonRow(params Control[] buttons)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var b in buttons)
                panel.Children.Add(b);
            return panel;
        }

        public static ListBox StringList(IEnumerable items, string selectedPath)
        {
            var list = new ListBox { ItemsSource = items, MinHeight = 80 };
            list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
            return list;
        }

        /// <summary>Ячейка-поле ввода строки таблицы с двусторонней привязкой.</summary>
        public static TextBox CellBox(string path, int column)
        {
            var box = new TextBox { Margin = new Thickness(4, 2), VerticalAlignment = VerticalAlignment.Center };
            box.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            Grid.SetColumn(box, column);
            return box;
        }

        /// <summary>Ячейка-флажок строки таблицы с двусторонней привязкой.</summary>
        public static CheckBox CellCheck(string path, int column)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            box.Bind(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            Grid.SetColumn(box, column);
            return box;
        }

        /// <summary>Ячейка-текст строки таблицы с привязкой.</summary>
        public static TextBlock CellBound(string path, int column, bool bold = false)
        {
            var cell = Cell(null, column, bold);
            cell.Bind(TextBlock.TextProperty, new Binding(path));
            return cell;
        }

        public static IBrush Brush(string hex)
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            // Аудит UI v100114: фолбэк — тематизированная нейтральная кисть, а не
            // фиксированный серый (текст/иконки читаются в обеих темах).
            catch { return Themes.ThemeBrushes.Get("TextSecondaryBrush", "#64748B"); }
        }
    }
}
#endif
