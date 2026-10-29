#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Помощники построения окон WPF в коде (выпуск 1.0.0.69): новые окна
    /// («Журнал регистрации», «Расширения» и др.) собираются без XAML, но берут
    /// кисти и стили из активной темы (<c>DynamicResource</c>-ссылки), как
    /// окна с разметкой. Образец — <see cref="ConfigDiffProgressWindow"/>.
    /// </summary>
    internal static class WpfUi
    {
        /// <summary>Общие настройки модального окна: иконка, фон и цвет текста темы.</summary>
        public static void SetupWindow(Window window, string titleKey, double width, double height)
        {
            window.Title = LocalizationManager.T(titleKey);
            window.Width = width;
            window.Height = height;
            window.MinWidth = Math.Min(width, 640);
            window.MinHeight = Math.Min(height, 420);
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = false;
            window.SetResourceReference(Control.BackgroundProperty, "ContentBackgroundBrush");
            window.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            if (Application.Current?.TryFindResource("AppIcon") is ImageSource icon)
                window.Icon = icon;
            if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window) && owner.IsLoaded)
                window.Owner = owner;
        }

        /// <summary>Текстовый блок с цветом текста темы.</summary>
        public static TextBlock Text(string text, double fontSize = 13, bool bold = false, bool secondary = false)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextSecondaryBrush" : "TextPrimaryBrush");
            return block;
        }

        /// <summary>Подпись по ключу локализации. Кисть — «подпись поля формы»
        /// (<c>FormLabelBrush</c>, issue #61): всегда контрастна в обеих темах, включая
        /// стенерированные схемы (вместо вторичной кисти, которая в тёмной теме могла
        /// оказаться тёмной у пользовательских схем).</summary>
        public static TextBlock Label(string key, double fontSize = 12)
        {
            var block = Text(LocalizationManager.T(key), fontSize, secondary: true);
            block.SetResourceReference(TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Подпись поля формы по строке (не по ключу локализации). Кисть —
        /// <c>FormLabelBrush</c> (issue #61): единая точка для подписей полей, собранных
        /// кодом, чтобы они не остались чёрными в тёмной теме и пользовательских схемах.</summary>
        public static TextBlock FieldLabel(string text, double fontSize = 12)
        {
            var block = Text(text, fontSize, secondary: false);
            block.SetResourceReference(TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Текст, привязанный к свойству DataContext.</summary>
        public static TextBlock BoundText(string path, double fontSize = 12, bool secondary = true)
        {
            var block = Text(string.Empty, fontSize, secondary: secondary);
            block.SetBinding(TextBlock.TextProperty, new Binding(path));
            return block;
        }

        /// <summary>
        /// Кнопка в стиле темы с подписью по ключу. Единый контракт (issue #26):
        /// accent — акцентная ModernButton (заливка акцентом, текст по WCAG),
        /// иначе — вторичная SecondaryButton (карточка + контур + текст темы).
        /// </summary>
        public static Button Button(string key, RoutedEventHandler onClick, double minWidth = 110, bool accent = false)
        {
            var button = new Button
            {
                Content = LocalizationManager.T(key),
                MinWidth = minWidth,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var styleKey = accent ? "ModernButton" : "SecondaryButton";
            if (Application.Current?.TryFindResource(styleKey) is Style style)
                button.Style = style;
            button.Click += onClick;
            return button;
        }

        /// <summary>Поле ввода с двусторонней привязкой (обновление при вводе).</summary>
        public static TextBox BoundTextBox(string path, double width = 160)
        {
            var box = new TextBox
            {
                Width = width,
                Margin = new Thickness(4, 0, 12, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2)
            };
            // Фон и контур — кисти темы (issue #63): поля в карточках не должны
            // сливаться с фоном карточки (раньше выглядели «только для чтения»).
            box.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "BorderBrushColor");
            box.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            box.SetBinding(TextBox.TextProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return box;
        }

        /// <summary>
        /// Поле пароля: у <see cref="PasswordBox"/> нет привязываемого свойства, поэтому
        /// значение синхронизируется вручную в обе стороны (ввод → ViewModel, смена
        /// значения во ViewModel → поле).
        /// </summary>
        public static PasswordBox BoundPassword(System.ComponentModel.INotifyPropertyChanged vm, string property,
            Func<string> get, Action<string> set, double width = 160)
        {
            var box = new PasswordBox
            {
                Width = width,
                Margin = new Thickness(4, 0, 12, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
                Password = get() ?? string.Empty
            };
            var updating = false;
            box.PasswordChanged += (_, _) =>
            {
                if (updating) return;
                set(box.Password);
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != property) return;
                var value = get() ?? string.Empty;
                if (box.Password == value) return;
                updating = true;
                box.Password = value;
                updating = false;
            };
            return box;
        }

        /// <summary>Флажок с двусторонней привязкой.</summary>
        public static CheckBox BoundCheckBox(string key, string path)
        {
            var box = new CheckBox
            {
                Content = LocalizationManager.T(key),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            box.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            box.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay
            });
            return box;
        }

        /// <summary>Выпадающий список строк с привязкой выбранного индекса.</summary>
        public static ComboBox BoundCombo(System.Collections.IEnumerable items, string indexPath, double width = 150)
        {
            var combo = new ComboBox
            {
                ItemsSource = items,
                Width = width,
                Margin = new Thickness(4, 0, 12, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            // Кисти темы для читаемости в карточках (issue #63).
            combo.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            combo.SetResourceReference(Control.BorderBrushProperty, "BorderBrushColor");
            combo.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, new Binding(indexPath)
            {
                Mode = BindingMode.TwoWay
            });
            return combo;
        }

        /// <summary>Колонка таблицы с текстом по пути привязки.</summary>
        public static DataGridTextColumn Column(string headerKey, string path, DataGridLength width)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0)));
            return new DataGridTextColumn
            {
                Header = LocalizationManager.T(headerKey),
                Binding = new Binding(path),
                Width = width,
                ElementStyle = style
            };
        }

        /// <summary>Колонка-флажок с двусторонней привязкой (переключение одним щелчком).</summary>
        public static DataGridTemplateColumn CheckColumn(string path, double width = 36)
        {
            var factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            return new DataGridTemplateColumn
            {
                Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory }
            };
        }

        /// <summary>Редактируемая текстовая колонка (таблица с IsReadOnly = false).</summary>
        public static DataGridTextColumn EditColumn(string headerKey, string path, DataGridLength width)
        {
            var column = Column(headerKey, path, width);
            column.Binding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            column.IsReadOnly = false;
            return column;
        }

        /// <summary>Заголовок секции окна. Кисть — <c>FormLabelBrush</c> (issue #61):
        /// заголовки секций, как и подписи полей, читаемы в обеих темах.</summary>
        public static TextBlock Section(string key)
        {
            var block = Text(LocalizationManager.T(key), 13, bold: true);
            block.SetResourceReference(TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }

        /// <summary>Рамка-карточка вокруг группы элементов.</summary>
        public static Border Card(UIElement child)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = child
            };
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            return border;
        }

        /// <summary>Горизонтальный ряд кнопок.</summary>
        public static StackPanel ButtonRow(params UIElement[] buttons)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var b in buttons)
                panel.Children.Add(b);
            return panel;
        }

        /// <summary>Список строк с привязкой выбранного элемента.</summary>
        public static ListBox StringList(System.Collections.IEnumerable items, string selectedPath)
        {
            var list = new ListBox { ItemsSource = items, MinHeight = 80 };
            list.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(selectedPath) { Mode = BindingMode.TwoWay });
            return list;
        }

        /// <summary>Таблица только для чтения в оформлении темы.</summary>
        public static DataGrid Grid(System.Collections.IEnumerable items)
        {
            var grid = new DataGrid
            {
                ItemsSource = items,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeight = 28,
                BorderThickness = new Thickness(1),
                EnableRowVirtualization = true
            };
            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            // issue #26 (комментарий 7OH «Ещё одна новинка с зеброй»): чередующаяся строка
            // должна использовать тематическую кисть ZebraRowBrush (в тёмной теме #334155),
            // а НЕ CardBackgroundBrush — иначе обе строки сливаются (или рождается «белая
            // зебра», когда строка берёт системный White). RowBackground — карточка темы.
            grid.SetResourceReference(DataGrid.RowBackgroundProperty, "CardBackgroundBrush");
            grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "ZebraRowBrush");
            grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            return grid;
        }

        /// <summary>Горизонтальная панель с переносом для полей отбора.</summary>
        public static WrapPanel Wrap(params UIElement[] children)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var child in children)
            {
                if (child is FrameworkElement fe && fe.Margin == default)
                    fe.Margin = new Thickness(0, 4, 8, 4);
                panel.Children.Add(child);
            }
            return panel;
        }

        /// <summary>Добавляет элемент в ячейку сетки.</summary>
        public static T Place<T>(System.Windows.Controls.Grid grid, T element, int row, int column = 0, int columnSpan = 1)
            where T : UIElement
        {
            System.Windows.Controls.Grid.SetRow(element, row);
            System.Windows.Controls.Grid.SetColumn(element, column);
            if (columnSpan > 1)
                System.Windows.Controls.Grid.SetColumnSpan(element, columnSpan);
            grid.Children.Add(element);
            return element;
        }

        /// <summary>Сетка с заданными строками (Auto/Star в пикселях: 0 — Auto, &lt;0 — Star).</summary>
        public static System.Windows.Controls.Grid Rows(params double[] heights)
        {
            var grid = new System.Windows.Controls.Grid();
            foreach (var h in heights)
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = h == 0 ? GridLength.Auto
                        : h < 0 ? new GridLength(-h, GridUnitType.Star)
                        : new GridLength(h)
                });
            }
            return grid;
        }

        /// <summary>Многострочное поле только для чтения (подробности, журнал операций).</summary>
        public static TextBox ReadOnlyText(string path)
        {
            var box = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(6)
            };
            box.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            box.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            box.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });
            return box;
        }
    }
}
#endif
