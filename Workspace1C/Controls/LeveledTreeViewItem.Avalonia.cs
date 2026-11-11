#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Themes;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Avalonia-версия контейнера элемента дерева (TreeViewItem). Единый тип контейнеров нужен,
    /// чтобы на них действовал стиль в MainWindow, отключающий стандартную подсветку (фон рисует
    /// карточка строки). Уровень вложенности TreeView вычисляет сам, поэтому ручное свойство Level
    /// из WPF-версии не требуется и удалено.
    /// </summary>
    public class LeveledTreeViewItem : TreeViewItem
    {
        /// <summary>
        /// Тема оформления ищется по типу контрола, а для наследника её в Fluent нет:
        /// без этого шаблон не находится и контрол не отрисовывается вовсе.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();

        private Control? _chevron;

        /// <summary>
        /// Подсказка стрелки раскрытия. Кнопка приходит из нашего шаблона,
        /// поэтому ищется по имени части после его применения. В разметке WPF
        /// подсказка тоже своя на каждое состояние (MainWindow.xaml:1436 и 1439).
        /// </summary>
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _chevron = e.NameScope.Find<Control>("PART_ExpandCollapseChevron");
            if (_chevron is ToggleButton chevron)
                chevron.Theme = ExpanderTheme();
            UpdateChevronTooltip();
        }

        /// <summary>
        /// Шаблон строки списка по разметке WPF (MainWindow.xaml:1432-1441):
        /// колонка кнопки разворота по содержимому и колонка заголовка, а список
        /// вложенных строк без отступа. Штатный шаблон Fluent вместо этого
        /// сдвигает всю строку на каждый уровень вложенности, и тогда колонки
        /// значений вложенных строк уезжают от заголовков, как только ширина
        /// колонки «Название» задана числом.
        /// </summary>
        public static ControlTheme RowTheme()
        {
            var theme = new ControlTheme(typeof(TreeViewItem))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<TreeViewItem>((_, scope) =>
                    {
                        var chevron = new ToggleButton { Name = "PART_ExpandCollapseChevron" };
                        chevron[!ToggleButton.IsCheckedProperty] = new Binding(nameof(TreeViewItem.IsExpanded))
                        {
                            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                            Mode = BindingMode.TwoWay
                        };
                        // Отступ уровня стоит на кнопке разворота, как у автора:
                        // он сдвигает заголовок группы, а строка остаётся у края.
                        chevron[!Layoutable.MarginProperty] = new Binding(nameof(TreeViewItem.Level))
                        {
                            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                            Converter = LevelIndent
                        };
                        chevron.RegisterInNameScope(scope);

                        var header = new ContentPresenter
                        {
                            Name = "PART_HeaderPresenter",
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        header[!ContentPresenter.ContentProperty] =
                            new TemplateBinding(HeaderedItemsControl.HeaderProperty);
                        header[!ContentPresenter.ContentTemplateProperty] =
                            new TemplateBinding(HeaderedItemsControl.HeaderTemplateProperty);
                        header.RegisterInNameScope(scope);
                        Grid.SetColumn(header, 1);

                        var row = new Grid();
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 0 });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.Children.Add(chevron);
                        row.Children.Add(header);

                        var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
                        items[!Visual.IsVisibleProperty] = new TemplateBinding(TreeViewItem.IsExpandedProperty);
                        items[!ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty);
                        items.RegisterInNameScope(scope);

                        var stack = new StackPanel();
                        stack.Children.Add(row);
                        stack.Children.Add(items);
                        return stack;
                    }))
                }
            };

            // У строки базы вложенных нет, и кнопка разворота ей не нужна:
            // у автора она в этом случае Collapsed (MainWindow.xaml:1524).
            theme.Add(new Style(x => x.Nesting().Class(":empty").Template().Name("PART_ExpandCollapseChevron"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) }
            });
            return theme;
        }

        /// <summary>Уровень вложенности в отступ слева: шаг 18, как у автора.</summary>
        public static readonly IValueConverter LevelIndent =
            new FuncValueConverter<int, Thickness>(level =>
                new Thickness(level * Converters.LevelToThicknessConverter.IndentStep, 0, 0, 0));

        /// <summary>
        /// Уровень вложенности в отступ ведущего блока строки базы: отступ
        /// родительской группы плюс ширина кнопки разворота, которой у строки
        /// базы нет (MainWindow.xaml:1155, параметр конвертера «base»).
        /// </summary>
        public static readonly IValueConverter LeadIndent =
            new FuncValueConverter<int, Thickness>(level => new Thickness(LeadIndentFor(level), 0, 0, 0));

        /// <summary>Тот же отступ числом: нужен там, где у панели есть свои отступы по другим сторонам.</summary>
        public static double LeadIndentFor(int level)
            => (level > 0 ? level - 1 : 0) * Converters.LevelToThicknessConverter.IndentStep
                + Converters.LevelToThicknessConverter.ExpanderWidth;


        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsExpandedProperty)
                UpdateChevronTooltip();
        }

        /// <summary>
        /// Оформление кнопки разворота по разметке WPF: рамка 22 на 22 со скруглением
        /// акцентной кистью, при наведении заливка акцентом и белый знак. Штатная
        /// галка Fluent на её месте была расхождением.
        /// Issue #123 (v1.0.1.55): внутри шевроны в стиле 1С вместо «+»/«−» —
        /// квадратные кнопки с плюс/минус выглядели одинаково: свёрнута — шеврон
        /// вправо («IconChevronRight»), развёрнута — шеврон вниз («IconChevronDown»).
        /// </summary>
        private static ControlTheme ExpanderTheme()
        {
            var theme = new ControlTheme(typeof(ToggleButton))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(Layoutable.WidthProperty, 26d),
                    new Setter(Layoutable.HeightProperty, 26d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CursorProperty, new Cursor(StandardCursorType.Hand)),
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToggleButton>((_, scope) =>
                    {
                        // Значки строятся общим помощником: у голого Path со Stretch
                        // контур растягивается по своим границам, и минус, чьи
                        // границы это тонкая полоса, вставал не по центру коробки.
                        // Кисти частей задаются только сеттерами темы ниже, а не
                        // привязкой здесь: ThemeBrushes.Bind ставит значение
                        // приоритетом локального, и стиль состояния, который
                        // слабее, наведение уже не перекрасил бы.
                        var minus = IconHelper.MakeIcon("IconChevronDown", 12, out var minusPath);
                        minus.Name = "ЗнакРазвёрнуто";
                        minusPath.Name = "КонтурРазвёрнуто";
                        var plus = IconHelper.MakeIcon("IconChevronRight", 12, out var plusPath);
                        plus.Name = "ЗнакСвёрнуто";
                        plusPath.Name = "КонтурСвёрнуто";

                        var glyphs = new Panel();
                        glyphs.Children.Add(minus.RegisterInNameScope(scope));
                        glyphs.Children.Add(plus.RegisterInNameScope(scope));
                        minusPath.RegisterInNameScope(scope);
                        plusPath.RegisterInNameScope(scope);

                        var border = new Border
                        {
                            Name = "РамкаРазворота",
                            Width = 22,
                            Height = 22,
                            CornerRadius = new CornerRadius(5),
                            BorderThickness = new Thickness(1.5),
                            Child = glyphs
                        };
                        return border.RegisterInNameScope(scope);
                    }))
                }
            };

            static IBinding Res(string key) => new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);

            // Вид в покое: заливка карточкой, рамка и знак акцентом.
            theme.Add(new Style(x => x.Nesting().Template().Name("РамкаРазворота"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, Res("CardBackgroundBrush")),
                    new Setter(Border.BorderBrushProperty, Res("AccentBrush"))
                }
            });
            foreach (var part in new[] { "КонтурРазвёрнуто", "КонтурСвёрнуто" })
                theme.Add(new Style(x => x.Nesting().Template().Name(part))
                {
                    Setters = { new Setter(Path.FillProperty, Res("AccentBrush")) }
                });

            // Свёрнутой группе плюс, развёрнутой минус. Видимость задаётся только
            // стилями обоих состояний: локальное значение в шаблоне старше стиля,
            // и знак, спрятанный локально, обратно уже не показывался.
            theme.Add(new Style(x => x.Nesting().Class(":checked").Template().Name("ЗнакРазвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, true) }
            });
            theme.Add(new Style(x => x.Nesting().Class(":checked").Template().Name("ЗнакСвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) }
            });
            theme.Add(new Style(x => x.Nesting().Not(y => y.Class(":checked")).Template().Name("ЗнакРазвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) }
            });
            theme.Add(new Style(x => x.Nesting().Not(y => y.Class(":checked")).Template().Name("ЗнакСвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, true) }
            });
            // При наведении заливка акцентом и белый знак, при нажатии заливка
            // акцентом наведения, как в разметке (MainWindow.xaml:1504-1513).
            foreach (var (state, bg) in new[] { (":pointerover", "AccentBrush"), (":pressed", "AccentHoverBrush") })
            {
                var stateClass = state;
                theme.Add(new Style(x => x.Nesting().Class(stateClass).Template().Name("РамкаРазворота"))
                {
                    Setters = { new Setter(Border.BackgroundProperty, Res(bg)) }
                });
                foreach (var part in new[] { "КонтурРазвёрнуто", "КонтурСвёрнуто" })
                    theme.Add(new Style(x => x.Nesting().Class(stateClass).Template().Name(part))
                    {
                        Setters = { new Setter(Path.FillProperty, Brushes.White) }
                    });
            }
            return theme;
        }

        private void UpdateChevronTooltip()
        {
            if (_chevron is null)
                return;
            // Подсказка одна и статичная, как в разметке WPF. Раньше здесь были
            // две меняющиеся по состоянию, это расхождение с версией для Windows.
            ToolTip.SetTip(_chevron, Localization.LocalizationManager.T("Main.ExpandCollapseGroup"));
        }
    }
}
#endif