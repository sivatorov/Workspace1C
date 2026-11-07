#if LINUX
using System;
using System.Collections.Generic;
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Массовая загрузка cf через Конфигуратор» (issue #106, Avalonia/Linux,
    /// автор aepkugesi): выбор списка баз (группа/мультивыделение — как в пакетных
    /// операциях), файл .cf и опции перед загрузкой (выгрузка базы в .dt, завершение
    /// сеансов, блокировка начала сеансов, блокировка регламентных заданий).
    /// Вся логика — в чистой ViewModel <see cref="MassLoadCfViewModel"/>; выполнение —
    /// <see cref="MassLoadCfService"/> (Конфигуратор /LoadCfg + /UpdateDBCfg).
    /// Раскладка — по образцу окна массовых операций (issue #92).
    /// </summary>
    public sealed class MassLoadCfWindow : ModalWindowBase
    {
        private readonly MassLoadCfViewModel _vm;
        private readonly ListBox _itemsList = new();
        private readonly ListBox _logList = new();
        private ScrollViewer? _logScrollViewer;
        private bool _logStickToBottom = true;
        private readonly TextBlock _hint;
        private readonly TextBlock _summary;
        private readonly TextBox _cfPathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _dumpDirBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _templateBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _permissionBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly RadioButton _sourceCfRadio = new();
        private readonly RadioButton _sourceRepoRadio = new();
        private readonly Button _runButton = new();
        private readonly Button _browseCfButton = new();
        private readonly Button _browseDirButton = new();
        private readonly Button _stopButton = new() { IsEnabled = false };
        private readonly CheckBox _dumpDtCheck;
        private StackPanel? _dumpDtOptionsRow;
        private CancellationTokenSource? _cts;

        /// <summary>ViewModel окна — для чтения значений после закрытия (настройки).</summary>
        public MassLoadCfViewModel ViewModel => _vm;

        public MassLoadCfWindow(IEnumerable<Infobase> infobases,
            string? initialCfPath = null, string? dumpDirectory = null,
            string? dumpTemplate = null, string? timestampFormat = null)
        {
            Width = 980;
            Height = 680;
            MinWidth = 780;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new MassLoadCfViewModel(infobases);
            // Сохранённые значения (как в массовых операциях): файл .cf, каталог и
            // шаблон .dt-выгрузки; формат метки времени токена {Дата}.
            if (!string.IsNullOrWhiteSpace(initialCfPath))
                _vm.CfFilePath = initialCfPath!.Trim();
            if (!string.IsNullOrWhiteSpace(dumpDirectory))
                _vm.DumpDirectory = dumpDirectory!.Trim();
            if (!string.IsNullOrWhiteSpace(dumpTemplate))
                _vm.DumpTemplate = dumpTemplate!.Trim();
            _vm.TimestampFormat = timestampFormat;
            // Сервис подготовки «Перед загрузкой» (issue #106 переиспользует
            // инфраструктуру «Перед операцией» issue #92); null — rac-клиент
            // недоступен, базы с включёнными опциями получат понятную ошибку.
            _vm.Service = new MassLoadCfService(
                BatchOpsPreparationService.CreateDefault(line => _vm.WriteLog(line)),
                line => _vm.WriteLog(line));
            Title = LocalizationManager.T("MassLoadCf.Title");

            ScrollViewer.SetHorizontalScrollBarVisibility(_itemsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(_logList, ScrollBarVisibility.Disabled);

            _hint = new TextBlock
            {
                Text = LocalizationManager.T("MassLoadCf.Hint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.75
            };

            // Поиск-фильтр над списком баз (как в окне массовых операций): подстрока
            // имени без учёта регистра; фильтр влияет только на видимость, выбор
            // сохраняется.
            var searchIcon = new Avalonia.Controls.Shapes.Path
            {
                Data = IconHelper.Geometry("IconSearch"),
                Stretch = Stretch.Uniform,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            ThemeBrushes.Bind(searchIcon, Avalonia.Controls.Shapes.Path.FillProperty, "TextSecondaryBrush");
            var clearIcon = new Avalonia.Controls.Shapes.Path
            {
                Data = IconHelper.Geometry("IconClose"),
                Stretch = Stretch.Uniform,
                Width = 10,
                Height = 10
            };
            ThemeBrushes.Bind(clearIcon, Avalonia.Controls.Shapes.Path.FillProperty, "TextSecondaryBrush");
            var searchClear = new Button
            {
                Content = clearIcon,
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, -4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            ToolTip.SetTip(searchClear, LocalizationManager.T("BatchOps.SearchClear"));
            searchClear.Bind(Visual.IsVisibleProperty,
                new Avalonia.Data.Binding("SearchText") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
            var searchBox = new TextBox
            {
                Watermark = LocalizationManager.T("BatchOps.SearchHint"),
                InnerLeftContent = searchIcon,
                InnerRightContent = searchClear
            }.Styled(ControlThemes.ModernTextBox);
            searchClear.Click += (_, _) =>
            {
                _vm.SearchText = string.Empty;
                searchBox.Focus();
            };
            ThemeBrushes.Bind(searchBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(searchBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            searchBox.Bind(TextBox.TextProperty,
                new Avalonia.Data.Binding("SearchText") { Mode = Avalonia.Data.BindingMode.TwoWay });

            _itemsList.ItemsSource = _vm.FilteredItems;
            _itemsList.ItemTemplate = new FuncDataTemplate<BatchOperationsItem>((_, _) => BuildRow());

            // «Выделить все / Снять все».
            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"), () => _vm.SetAll(true)));
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.None"), () => _vm.SetAll(false)));

            // Файл конфигурации .cf.
            var cfPathLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.PathLabelLoadCf"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _cfPathBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("CfFilePath") { Mode = Avalonia.Data.BindingMode.TwoWay });
            ThemeBrushes.Bind(_cfPathBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_cfPathBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            _browseCfButton.Content = "…";
            _browseCfButton.MinWidth = 34;
            _browseCfButton.Styled(ControlThemes.ModernButton);
            _browseCfButton.Click += async (_, _) => await BrowseCfAsync();
            var cfPathRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(cfPathLabel, 0);
            Grid.SetColumn(_cfPathBox, 1);
            Grid.SetColumn(_browseCfButton, 2);
            cfPathRow.Children.Add(cfPathLabel);
            cfPathRow.Children.Add(_cfPathBox);
            cfPathRow.Children.Add(_browseCfButton);

            // Выгрузка базы в .dt перед загрузкой («как в массовой выгрузке в dt»):
            // флажок + каталог + шаблон имени {Имя}/{Дата}.
            _dumpDtCheck = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("MassLoadCf.DumpDtBefore"),
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(0, 10, 0, 0)
            };
            _dumpDtCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("DumpDtBefore") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var dumpDirLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.PathLabelDumpDt"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _dumpDirBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("DumpDirectory") { Mode = Avalonia.Data.BindingMode.TwoWay });
            ThemeBrushes.Bind(_dumpDirBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_dumpDirBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            _browseDirButton.Content = "…";
            _browseDirButton.MinWidth = 34;
            _browseDirButton.Styled(ControlThemes.ModernButton);
            _browseDirButton.Click += async (_, _) => await BrowseDumpDirAsync();
            var dumpDirRow = new Grid
            {
                Margin = new Thickness(20, 6, 0, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            Grid.SetColumn(dumpDirLabel, 0);
            Grid.SetColumn(_dumpDirBox, 1);
            Grid.SetColumn(_browseDirButton, 2);
            dumpDirRow.Children.Add(dumpDirLabel);
            dumpDirRow.Children.Add(_dumpDirBox);
            dumpDirRow.Children.Add(_browseDirButton);
            var templateLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.TemplateLabel"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            ThemeBrushes.Bind(_templateBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_templateBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            ToolTip.SetTip(_templateBox, MakeWrappingTip(LocalizationManager.T("BatchOps.TemplateHint")));
            _templateBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("DumpTemplate") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var templateRow = new Grid
            {
                Margin = new Thickness(20, 6, 0, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            Grid.SetColumn(templateLabel, 0);
            Grid.SetColumn(_templateBox, 1);
            templateRow.Children.Add(templateLabel);
            templateRow.Children.Add(_templateBox);
            _dumpDtOptionsRow = new StackPanel
            {
                IsVisible = _vm.DumpDtBefore,
                Margin = new Thickness(0, 6, 0, 0),
                Children = { dumpDirRow, templateRow }
            };

            // Блок «Перед загрузкой» (issue #106) — подписи как в блоке
            // «Перед операцией» массовых операций (issue #92).
            var prepareHeader = new TextBlock
            {
                Text = LocalizationManager.T("MassLoadCf.PrepareHeader"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 10, 0, 2)
            };
            var prepareTerminateCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.TerminateSessions"),
                Margin = new Thickness(0, 1)
            };
            prepareTerminateCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("TerminateSessions") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareDenySessionsCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.DenyNewSessions"),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            prepareDenySessionsCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("DenyNewSessions") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var preparePermissionLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Prepare.PermissionCode"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 6, 0)
            };
            ThemeBrushes.Bind(_permissionBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_permissionBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            ToolTip.SetTip(_permissionBox, MakeWrappingTip(
                LocalizationManager.T("BatchOps.Prepare.PermissionCodeHint")));
            _permissionBox.Bind(TextBox.TextProperty,
                new Avalonia.Data.Binding("PermissionCode") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareDenyRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1),
                Children = { prepareDenySessionsCheck, preparePermissionLabel, _permissionBox }
            };
            var prepareDenyJobsCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.DenyScheduledJobs"),
                Margin = new Thickness(0, 1)
            };
            prepareDenyJobsCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("DenyScheduledJobs") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareHint = new TextBlock
            {
                Text = LocalizationManager.T("MassLoadCf.PrepareHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };
            var preparePanel = new StackPanel
            {
                Children =
                {
                    prepareHeader, prepareTerminateCheck, prepareDenyRow, prepareDenyJobsCheck, prepareHint
                }
            };
            // Источник обновления (issue #85, v1.0.1.43): файл поставки .cf или
            // хранилище конфигурации (адрес/логин/пароль — из свойств базы).
            var sourceHeader = new TextBlock
            {
                Text = LocalizationManager.T("MassLoadCf.SourceHeader"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            _sourceCfRadio.Content = LocalizationManager.T("MassLoadCf.SourceCfFile");
            _sourceRepoRadio.Content = LocalizationManager.T("MassLoadCf.SourceRepository");
            _sourceCfRadio.IsChecked = true;
            _sourceCfRadio.Checked += (_, _) =>
            {
                _vm.Source = MassLoadCfSource.CfFile;
                cfPathRow.IsVisible = true;
            };
            _sourceRepoRadio.Checked += (_, _) =>
            {
                _vm.Source = MassLoadCfSource.Repository;
                cfPathRow.IsVisible = false;
            };
            var sourceRow = new StackPanel { Children = { sourceHeader, _sourceCfRadio, _sourceRepoRadio } };

            // Монопольный запуск Предприятия (/M) после успешного обновления
            // (issue #85, v1.0.1.43): блокировки снимаются перед запуском автоматически.
            var monopolCheck = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("MassLaunch.MonopolLaunch"),
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(0, 10, 0, 0)
            };
            monopolCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("LaunchEnterpriseMonopol") { Mode = Avalonia.Data.BindingMode.TwoWay });

            var pathStack = new StackPanel
            {
                Children = { sourceRow, cfPathRow, _dumpDtCheck, _dumpDtOptionsRow, preparePanel, monopolCheck }
            };

            var logHeader = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Log"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 6)
            };

            _logList.ItemsSource = _vm.LogLines;
            _logList.SelectionChanged += (_, _) => _logList.SelectedIndex = -1;
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

            // «Остановить» + «Выполнить» + «Закрыть».
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
                Children = { _stopButton, _runButton, closeButton }
            };

            // Раскладка (issue #92): список баз слева на всю высоту, справа —
            // подсказка, параметры, журнал, сводка и кнопки.
            var leftColumn = new Grid
            {
                MinWidth = 240,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star)
                }
            };
            Grid.SetRow(searchBox, 0);
            leftColumn.Children.Add(searchBox);
            bulk.Margin = new Thickness(0, 8, 0, 6);
            Grid.SetRow(bulk, 1);
            leftColumn.Children.Add(bulk);
            Grid.SetRow(_itemsList, 2);
            leftColumn.Children.Add(_itemsList);

            _logList.MinHeight = 90;
            var rightColumn = new Grid
            {
                Margin = new Thickness(14, 0, 0, 0),
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
            Place(rightColumn, _hint, 0);
            Place(rightColumn, pathStack, 1);
            Place(rightColumn, logHeader, 2);
            Place(rightColumn, _logList, 3);
            Place(rightColumn, _summary, 4);
            Place(rightColumn, buttons, 5);

            var grid = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star))
                }
            };
            Grid.SetColumn(leftColumn, 0);
            grid.Children.Add(leftColumn);
            Grid.SetColumn(rightColumn, 1);
            grid.Children.Add(rightColumn);
            Content = grid;

            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        /// <summary>Многострочная подсказка (как в окне массовых операций).</summary>
        private static TextBlock MakeWrappingTip(string text)
        {
            var tip = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            };
            Themes.ThemeBrushes.Bind(tip, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            return tip;
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

        /// <summary>Выбор файла конфигурации .cf.</summary>
        private async Task BrowseCfAsync()
        {
            if (StorageProvider is null)
                return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.T("BatchOps.BrowseCfTitle"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("cf") { Patterns = new[] { "*.cf" } }
                }
            });
            if (files.Count > 0)
                _vm.CfFilePath = files[0].Path.LocalPath;
        }

        /// <summary>Выбор каталога для .dt-выгрузки перед загрузкой.</summary>
        private async Task BrowseDumpDirAsync()
        {
            if (StorageProvider is null)
                return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationManager.T("BatchOps.BrowseDirectoryTitle"),
                AllowMultiple = false
            });
            if (folders.Count > 0)
                _vm.DumpDirectory = folders[0].Path.LocalPath;
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
                // Страховка от потерянного уведомления — состояние кнопок
                // синхронизируется с IsRunning при ЛЮБОМ исходе прогона.
                Dispatcher.UIThread.Post(ApplyRunButtonsState);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MassLoadCfViewModel.IsRunning))
            {
                // PropertyChanged может прийти с фонового потока — обновление
                // кнопок только на UI-потоке (issue #92, v1.0.1.31).
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(ApplyRunButtonsState);
                    return;
                }
                ApplyRunButtonsState();
            }
            else if (e.PropertyName == nameof(MassLoadCfViewModel.DumpDtBefore) && _dumpDtOptionsRow is not null)
            {
                // Показ/скрытие блока каталога и шаблона .dt-выгрузки по флажку.
                _dumpDtOptionsRow.IsVisible = _vm.DumpDtBefore;
            }
        }

        /// <summary>
        /// Атомарно выставляет состояние кнопок «Выполнить/Остановить/Обзор» и признак
        /// занятости строк по <see cref="MassLoadCfViewModel.IsRunning"/>.
        /// Вызывается только с UI-потока.
        /// </summary>
        private void ApplyRunButtonsState()
        {
            _runButton.IsEnabled = !_vm.IsRunning;
            _browseCfButton.IsEnabled = !_vm.IsRunning;
            _browseDirButton.IsEnabled = !_vm.IsRunning;
            _stopButton.IsEnabled = _vm.IsRunning;
            foreach (var item in _vm.Items)
                item.IsBusy = _vm.IsRunning;
        }

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || _vm.LogLines.Count == 0)
                return;

            HookLogScrollViewer();
            if (_logStickToBottom)
                _logList.ScrollIntoView(_vm.LogLines[_vm.LogLines.Count - 1]);
        }

        /// <summary>
        /// «Умный» автоскролл журнала (как в окне массовых операций): прокручиваем
        /// к последней строке ТОЛЬКО пока пользователь стоит на конце списка.
        /// </summary>
        private void HookLogScrollViewer()
        {
            if (_logScrollViewer is not null)
                return;
            _logScrollViewer = _logList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_logScrollViewer is not null)
                _logScrollViewer.ScrollChanged += OnLogScrollChanged;
        }

        private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (e.OffsetDelta.Y != 0 && _logScrollViewer is not null)
            {
                _logStickToBottom = _logScrollViewer.Offset.Y + _logScrollViewer.Viewport.Height
                    >= _logScrollViewer.Extent.Height - 4;
            }
        }
    }
}
#endif
