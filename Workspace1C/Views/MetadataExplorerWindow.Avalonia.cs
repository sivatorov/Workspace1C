#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Обозреватель метаданных…» (цикл 0.3.9.132–0.3.9.136, этап 2, Avalonia/Linux):
    /// просмотр дерева метаданных конфигурации 1С БЕЗ интерактивного конфигуратора —
    /// источник (база или файл .cf) выгружается в XML через /DumpConfigToFiles,
    /// дерево «Конфигурация → подсистемы → типы → объекты» строится лениво
    /// (<see cref="MetadataTreeNodeViewModel"/>, дети подгружаются при раскрытии узла),
    /// панель справа показывает детали выбранного объекта. Вся логика — в чистой ViewModel
    /// <see cref="MetadataExplorerViewModel"/>; окно открывает окно прогресса на время
    /// выгрузки и удаляет временный каталог в Closed (Dispose).
    /// </summary>
    public sealed class MetadataExplorerWindow : ModalWindowBase
    {
        private readonly MetadataExplorerViewModel _vm;
        private readonly TreeView _tree = new();

        /// <summary>
        /// Команда «Загрузить» ViewModel — для интеграции из отчёта сравнения (этап 5):
        /// кнопка «Обозреватель метаданных…» в <see cref="ConfigDiffResultWindow"/>
        /// вызывает <c>LoadCommand.Execute(null)</c> сразу после открытия окна.
        /// </summary>
        public System.Windows.Input.ICommand LoadCommand => _vm.LoadCommand;

        /// <summary>
        /// issue #365: типовая кнопка максимизации в шапке, как у «Серверов 1С»
        /// (<see cref="ServerMonitorWindow"/>): окно изменяемое, в собственном chrome
        /// рисуем кнопку «развернуть/восстановить» рядом с «закрыть».
        /// </summary>
        protected override bool SupportsMaximizeButton => true;

        /// <param name="bases">Все информационные базы списка (для ComboBox).</param>
        /// <param name="selectedBase">Предвыбранная база (выбранная в главном окне).</param>
        public MetadataExplorerWindow(IReadOnlyList<Infobase> bases, Infobase? selectedBase)
        {
            Title = LocalizationManager.T("MetadataExplorer.Title");
            // issue #365: панель источника перекомпонована в 2 строки (строка 1 — база +
            // платформа, строка 2 — файл .cf + «Обзор…» + «Загрузить»), поэтому окно
            // сжато по горизонтали: Width/MinWidth = 900/800 (было 1380).
            Width = 900;
            Height = 640;
            MinWidth = 800;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            var service = AppServices.GetRequiredService<IMetadataExplorerService>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new MetadataExplorerViewModel(
                bases,
                selectedBase,
                service,
                dialogs,
                action => Dispatcher.UIThread.Post(action));

            DataContext = _vm;
            Closed += (_, _) => _vm.Dispose();

            // ---- Заголовок ----
            var title = new TextBlock
            {
                Text = LocalizationManager.T("MetadataExplorer.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };

            // ---- Панель источника ----
            var sourcePanel = BuildSourcePanel();

            // ---- Шапка конфигурации ----
            var configTitle = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            };
            configTitle.Bind(TextBlock.TextProperty, new Binding("ConfigurationTitle"));

            // ---- Поиск по имени (debounce в VM) и фильтр по типу (этап 3) ----
            var searchPanel = BuildSearchPanel();

            // ---- Статус / ошибка (стек: статус сверху, детали ошибки под ним) ----
            var status = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            };
            status.Bind(TextBlock.TextProperty, new Binding("StatusText"));
            // Ошибка — на семантической кисти темы (аудит UI, п. 2.9).
            var error = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60
            };
            ThemeBrushes.Bind(error, TextBlock.ForegroundProperty, "ErrorBrush");
            error.Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));
            // issue #365: строка ошибки обрезается (MaxHeight=60) — полный текст в тултипе.
            error.Bind(ToolTip.TipProperty, new Binding("ErrorMessage"));
            var statusError = new StackPanel
            {
                Spacing = 0,
                Margin = new Thickness(0, 0, 0, 8),
                Children = { status, error }
            };

            // ---- Дерево + панель деталей ----
            BuildTree();
            var detailsPanel = BuildDetailsPanel();
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            ThemeBrushes.Bind(splitter, TemplatedControl.BackgroundProperty, "BorderBrush");

            var mainGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 300 },
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(360)) { MinWidth = 260 }
                },
                Children = { _tree, splitter, detailsPanel }
            };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(detailsPanel, 2);

            // ---- Кнопки экспорта (этап 4): команды VM (SaveFileDialog через IDialogService) ----
            var exportCsv = BuildActionButton(LocalizationManager.T("MetadataExplorer.ExportCsv"), () =>
            {
                if (_vm.ExportCsvCommand.CanExecute(null))
                    _vm.ExportCsvCommand.Execute(null);
            });
            exportCsv.Width = 140;
            exportCsv.Bind(InputElement.IsEnabledProperty, new Binding("CanExport"));
            var exportTxt = BuildActionButton(LocalizationManager.T("MetadataExplorer.ExportTxt"), () =>
            {
                if (_vm.ExportTxtCommand.CanExecute(null))
                    _vm.ExportTxtCommand.Execute(null);
            });
            exportTxt.Width = 140;
            exportTxt.Bind(InputElement.IsEnabledProperty, new Binding("CanExport"));
            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { exportCsv, exportTxt, closeButton }
            };

            // ---- Подсказка (этап 5: + напоминание о времени выгрузки большой конфигурации) ----
            var hint = new TextBlock
            {
                Text = LocalizationManager.T("MetadataExplorer.Hint")
                    + Environment.NewLine
                    + LocalizationManager.T("MetadataExplorer.Hint.LargeConfig"),
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // issue #365: «выравнивание по верхнему краю» — дерево и панель деталей
            // занимают ВСЮ оставшуюся высоту (строка *), а статус/ошибка — своя
            // естественная высота (Auto). Раньше строки были перепутаны: статус
            // растягивался, а дерево сжималось к нижнему краю по высоте содержимого.
            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children = { title, sourcePanel, configTitle, searchPanel, statusError, mainGrid, buttons, hint }
            };
            Place(grid, title, 0);
            Place(grid, sourcePanel, 1);
            Place(grid, configTitle, 2);
            Place(grid, searchPanel, 3);
            Place(grid, statusError, 4);
            Place(grid, mainGrid, 5);
            Place(grid, buttons, 6);
            Place(grid, hint, 7);

            Content = grid;
        }

        // ===================== Построение UI =====================

        /// <summary>Панель источника: радио «База»/«Файл .cf», ComboBox баз, путь .cf, кнопки.</summary>
        private Control BuildSourcePanel()
        {
            var radioBase = new RadioButton
            {
                Content = LocalizationManager.T("MetadataExplorer.SourceBase"),
                GroupName = "MetadataExplorerSource",
                VerticalAlignment = VerticalAlignment.Center
            };
            radioBase.Bind(ToggleButton.IsCheckedProperty, new Binding("IsSourceBase", BindingMode.TwoWay));

            var radioCf = new RadioButton
            {
                Content = LocalizationManager.T("MetadataExplorer.SourceCf"),
                GroupName = "MetadataExplorerSource",
                VerticalAlignment = VerticalAlignment.Center
            };
            radioCf.Bind(ToggleButton.IsCheckedProperty, new Binding("IsSourceCf", BindingMode.TwoWay));

            var baseCombo = new ComboBox
            {
                Width = 280,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            baseCombo.ItemsSource = _vm.Bases;
            baseCombo.ItemTemplate = new FuncDataTemplate<Infobase>((ib, _) => new TextBlock { Text = ib.Name });
            baseCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedBase", BindingMode.TwoWay));
            baseCombo.Bind(InputElement.IsEnabledProperty, new Binding("IsSourceBase"));

            var cfBox = new TextBox
            {
                Width = 320,
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = LocalizationManager.T("MetadataExplorer.CfPathHint")
            };
            cfBox.Bind(TextBox.TextProperty, new Binding("CfPath", BindingMode.TwoWay)
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            cfBox.Bind(InputElement.IsEnabledProperty, new Binding("IsSourceCf"));

            // issue #365: версия платформы для распаковки .cf — editable ComboBox
            // (как PlatformBox в ConfigDiffSetupWindow): при выборе базы подставляется её
            // платформа, при «Файл .cf» — максимальная установленная; текст можно
            // изменить вручную (Text-привязка срабатывает и при вводе).
            var platformLabel = new TextBlock
            {
                Text = LocalizationManager.T("MetadataExplorer.Platform"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var platformBox = new ComboBox
            {
                Width = 200,
                IsEditable = true,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            platformBox.ItemsSource = _vm.PlatformVersions;
            platformBox.Bind(TextBox.TextProperty, new Binding("SelectedPlatformVersion", BindingMode.TwoWay)
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            platformBox.Bind(InputElement.IsEnabledProperty, new Binding("IsSourceCf"));
            ToolTip.SetTip(platformBox, LocalizationManager.T("MetadataExplorer.PlatformHint"));

            var browseButton = BuildActionButton(LocalizationManager.T("MetadataExplorer.Browse"), () => _vm.BrowseCf());
            browseButton.Width = 100;
            browseButton.Bind(InputElement.IsEnabledProperty, new Binding("IsSourceCf"));

            var loadButton = BuildActionButton(LocalizationManager.T("MetadataExplorer.Load"), () => { });
            loadButton.Click += OnLoad_Click;
            loadButton.Width = 120;
            ThemeBrushes.Bind(loadButton, Button.BackgroundProperty, "AccentBrush");
            ThemeBrushes.Bind(loadButton, Button.ForegroundProperty, "TextOnAccentBrush");

            // issue #365 (перекомпоновка): строка 1 — «Информационная база» + ComboBox баз
            // + выбор платформы; строка 2 — «Файл» + путь .cf + «Обзор…» + «Загрузить».
            // Окно сжато по горизонтали (Width/MinWidth = 900/800).
            var row1 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { radioBase, baseCombo, platformLabel, platformBox }
            };
            var row2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { radioCf, cfBox, browseButton, loadButton }
            };
            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 0),
                Children = { row1, row2 }
            };
        }

        /// <summary>
        /// Панель поиска и фильтра (этап 3): TextBox поиска (debounce в VM), кнопка
        /// «Очистить», ComboBox фильтра по типу («Все типы» + типы выгрузки).
        /// </summary>
        private Control BuildSearchPanel()
        {
            var searchBox = new TextBox
            {
                Width = 280,
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = LocalizationManager.T("MetadataExplorer.Search.Placeholder")
            };
            searchBox.Bind(TextBox.TextProperty, new Binding("SearchText", BindingMode.TwoWay)
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var clearButton = BuildActionButton(
                LocalizationManager.T("MetadataExplorer.Search.Clear"),
                () => _vm.SearchText = string.Empty);
            clearButton.Width = 90;

            var typeFilter = new ComboBox
            {
                Width = 220,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            typeFilter.ItemsSource = _vm.TypeFilterOptions;
            typeFilter.ItemTemplate = new FuncDataTemplate<MetadataTypeFilterOption>(
                (option, _) => new TextBlock { Text = option.DisplayName });
            typeFilter.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("TypeFilter", BindingMode.TwoWay));

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 6),
                Children = { searchBox, clearButton, typeFilter }
            };
        }

        /// <summary>Дерево метаданных: шаблон узла (имя + счётчик), раскрытие → EnsureLoaded.</summary>
        private void BuildTree()
        {
            _tree.ItemsSource = _vm.TreeNodes;
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(MetadataTreeNodeViewModel),
                (item, _) => BuildTreeRow(item as MetadataTreeNodeViewModel),
                item => item is MetadataTreeNodeViewModel node && node.HasChildren
                    ? node.Children
                    : Array.Empty<MetadataTreeNodeViewModel>());

            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var treeItemTheme) == true
                && treeItemTheme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }

            // issue #365: «странно отбор сработал» — скрытый фильтром узел прятал только
            // содержимое строки, а контейнер TreeViewItem оставался пустой строкой.
            // Привязываем видимость контейнера к IsVisible узла.
            _tree.ContainerPrepared += OnTreeContainerPrepared;

            // Раскрытие узла → ленивая подгрузка детей (всплывает от любого вложенного
            // контейнера; дочерние контейнеры готовит сам TreeViewItem).
            _tree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble);

            _tree.SelectionChanged += (_, _) =>
                _vm.SelectedNode = _tree.SelectedItem as MetadataTreeNodeViewModel;
        }

        private void OnTreeContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (e.Container is TreeViewItem item && item.DataContext is MetadataTreeNodeViewModel node)
                item.Bind(Visual.IsVisibleProperty, new Binding("IsVisible"));
        }

        private void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
        {
            if (e.Source is TreeViewItem item &&
                item.DataContext is MetadataTreeNodeViewModel node)
            {
                node.EnsureLoaded();
                // issue #365: после ленивой загрузки детей пересчитываем видимость
                // контейнеров — подсистема, у которой фильтр скрыл всех потомков, прячется.
                _vm.RefreshNodeVisibility(node);
            }
        }

        /// <summary>Строка узла: имя + счётчик «(N)»; узел-заглушка раскрывалки невидим.</summary>
        private Control BuildTreeRow(MetadataTreeNodeViewModel? node)
        {
            if (node is null || node.IsPlaceholder)
                return new TextBlock { Text = string.Empty, Height = 0 };

            var name = new TextBlock
            {
                Text = node.DisplayName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 1)
            };
            // Подсветка совпадения поиска: фон узла по IsMatch (этап 3). DataTrigger
            // в код-билде Avalonia неудобен — подписываемся на смену пометки узла.
            var highlightBrush = new SolidColorBrush(Color.Parse("#06B6D4"));
            void ApplyMatchHighlight()
            {
                name.Background = node.IsMatch ? highlightBrush : Brushes.Transparent;
                name.Foreground = node.IsMatch ? Brushes.White : null;
            }
            ApplyMatchHighlight();
            node.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MetadataTreeNodeViewModel.IsMatch))
                    ApplyMatchHighlight();
            };

            var count = new TextBlock
            {
                Text = node.CountText,
                Opacity = 0.6,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1)
            };
            // Фильтр по типу: узел скрывается, когда его тип не совпадает с TypeFilter.
            row.Bind(Visual.IsVisibleProperty, new Binding("IsVisible"));
            row.Children.Add(name);
            if (node.CountText.Length > 0)
                row.Children.Add(count);
            return row;
        }

        /// <summary>Панель деталей выбранного объекта (поля: имя/синоним/комментарий/счётчики/размер/путь).</summary>
        private Control BuildDetailsPanel()
        {
            var emptyHint = new TextBlock
            {
                Text = LocalizationManager.T("MetadataExplorer.Details.EmptyHint"),
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            };
            emptyHint.Bind(Visual.IsVisibleProperty, new Binding("HasNoDetails"));

            var detailsTitle = new TextBlock
            {
                Text = LocalizationManager.T("MetadataExplorer.Details.Title"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var fields = new StackPanel { Spacing = 6 };
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Name"), "DetailsName", bold: true));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Synonym"), "DetailsSynonym"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Comment"), "DetailsComment"));

            fields.Children.Add(Separator());
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Attributes"), "DetailsAttributesText"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.TabularSections"), "DetailsTabularSectionsText"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Forms"), "DetailsFormsText"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Commands"), "DetailsCommandsText"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Hierarchical"), "DetailsHierarchicalText"));

            fields.Children.Add(Separator());
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Size"), "DetailsSizeText"));
            fields.Children.Add(DetailRow(LocalizationManager.T("MetadataExplorer.Details.Path"), "DetailsRelPath"));

            var detailsStack = new StackPanel();
            detailsStack.Children.Add(detailsTitle);
            detailsStack.Children.Add(fields);
            detailsStack.Bind(Visual.IsVisibleProperty, new Binding("HasDetails"));

            var content = new StackPanel { Spacing = 0 };
            content.Children.Add(emptyHint);
            content.Children.Add(detailsStack);

            var card = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer
                {
                    Content = content,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderBrush");
            return card;
        }

        /// <summary>Строка деталей: подпись (вторичный цвет) + значение.</summary>
        private static Control DetailRow(string label, string bindingPath, bool bold = false)
        {
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Top
            };
            var value = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Top
            };
            value.Bind(TextBlock.TextProperty, new Binding(bindingPath));

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(140)),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                },
                Children = { labelText, value }
            };
            Grid.SetColumn(value, 1);
            return row;
        }

        private static Border Separator() => new()
        {
            Height = 1,
            Margin = new Thickness(0, 4),
            Background = Brushes.Transparent
        };

        // ===================== Общие помощники =====================

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildActionButton(string text, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
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
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>«Загрузить»: окно прогресса (индитерминант, этапы из StageChanged) +
        /// выполнение выгрузки; результат — дерево, ошибки — в статус-строку VM.</summary>
        private async void OnLoad_Click(object? sender, RoutedEventArgs e)
        {
            var progress = new MetadataExplorerProgressWindow();
            progress.SetStage(LocalizationManager.T("MetadataExplorer.Status.Dumping"));
            Action<string> onStage = progress.SetStage;
            _vm.StageChanged += onStage;
            progress.Show(this);
            try
            {
                await _vm.LoadAsync();
            }
            finally
            {
                _vm.StageChanged -= onStage;
                progress.Close();
            }
        }
    }
}
#endif