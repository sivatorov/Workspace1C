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
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Workspace1C.Controls;
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
        // issue #92 (v1.0.1.26): IsHitTestVisible=false снят — он блокировал прокрутку
        // журнала колесом и ползунком; выделение строк гасится сбросом SelectedIndex.
        private readonly ListBox _logList = new();
        private ScrollViewer? _logScrollViewer;
        private bool _logStickToBottom = true;
        private readonly PasswordBox _sqlPasswordBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);
        private readonly TextBlock _hint;
        private readonly TextBlock _summary;
        // Поля ввода тематизируются (issue #92, 1.0.1.11: без стиля текст поля
        // рисуется чёрным на тёмном фоне тёмной темы).
        // Поле каталога/файла тематизируется: кроме темы ModernTextBox — прямая
        // привязка Foreground/CaretBrush к кистям темы (issue #92, v100114 —
        // в тёмной схеме текст поля оставался чёрным).
        private readonly TextBox _pathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly Button _runButton = new();
        private readonly Button _browseButton = new();
        private readonly Button _stopButton = new() { IsEnabled = false };
        private CancellationTokenSource? _cts;

        /// <summary>ViewModel окна — для чтения шаблона/пути/вида после закрытия (issue #92).</summary>
        public BatchOperationsViewModel ViewModel => _vm;

        public BatchOperationsWindow(System.Collections.Generic.IEnumerable<Infobase> infobases, BatchOperationKind kind,
            string? initialDirectory = null, string? template = null,
            string? timestampFormat = null,
            Func<Infobase, System.Threading.CancellationToken, Task<DbmsBackupProfile?>>? dbmsProfileResolver = null)
        {
            // Ширина под двухколоночную раскладку (issue #92, v1.0.1.24): список слева,
            // параметры справа.
            Width = 980;
            Height = 680;
            MinWidth = 780;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new BatchOperationsViewModel(infobases, kind);
            // Автоподстановка данных СУБД из кластера (rac) для серверных баз без
            // профиля (issue #92, v100114); null — старое поведение.
            _vm.DbmsProfileResolver = dbmsProfileResolver;
            // Сохранённые каталог и шаблон массовой выгрузки (issue #92).
            if (!string.IsNullOrWhiteSpace(initialDirectory) && _vm.IsDumpKind)
                _vm.PathText = initialDirectory!.Trim();
            if (!string.IsNullOrWhiteSpace(template) && _vm.IsDumpKind)
                _vm.Template = template!.Trim();
            // Формат метки времени токена {Дата} — из настроек приложения
            // «Формат даты в имени файла выгрузки» (issue #92, 1.0.1.11).
            _vm.TimestampFormat = timestampFormat;
            Title = LocalizationManager.T(_vm.Kind switch
            {
                BatchOperationKind.DumpDt => "BatchOps.TitleDumpDt",
                BatchOperationKind.DumpCf => "BatchOps.TitleDumpCf",
                _ => "BatchOps.TitleLoadCf"
            });

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

            // Поиск-фильтр над списком баз (issue #92, v1.0.1.24): подстрока имени
            // без учёта регистра; фильтр влияет только на видимость, выбор сохраняется.
            // v1.0.1.25 (комментарий 7OH): подсказка «Поиск по данным баз», лупа слева
            // (текст начинается правее неё — InnerLeftContent) и кнопка очистки в конце поля.
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

            // issue #92 (v1.0.1.24): список показывает строки ПОД ФИЛЬТРОМ поиска;
            // прогон и выбор идут по полному списку Items в ViewModel.
            _itemsList.ItemsSource = _vm.FilteredItems;
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
            _pathLabel = pathLabel;
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
            // Прямая привязка к кистям темы (как у поля шаблона ниже) — иначе в тёмной
            // схеме текст поля каталога оставался чёрным на тёмном (issue #92).
            ThemeBrushes.Bind(_pathBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_pathBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");

            // Опции массовой выгрузки (issue #92): шаблон имени файла и DT/CF.
            // Видны только у выгрузок; панель пересобирается при смене вида.
            var templateLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.TemplateLabel"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            // Поле шаблона тематизируется — иначе в тёмной теме текст чёрный на тёмном
            // (issue #92, комментарий 7OH от 2026-10-31: «чёрный на тёмном»). Помимо
            // темы ModernTextBox текст и каретка привязываются к кистям темы напрямую
            // (v100114): в тёмной схеме связка DynamicResource в ControlTheme у поля
            // не обновлялась — Bind гарантирует смену цвета вместе со схемой.
            var templateBox = new TextBox
            {
                VerticalContentAlignment = VerticalAlignment.Center
            }.Styled(ControlThemes.ModernTextBox);
            ThemeBrushes.Bind(templateBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(templateBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            ToolTip.SetTip(templateBox, LocalizationManager.T("BatchOps.TemplateHint"));
            templateBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("Template") { Mode = Avalonia.Data.BindingMode.TwoWay });
            _kindDtRadio = new RadioButton
            {
                Content = LocalizationManager.T("BatchOps.KindDtRadio"),
                GroupName = "BatchDumpKind",
                VerticalAlignment = VerticalAlignment.Center
            };
            _kindCfRadio = new RadioButton
            {
                Content = LocalizationManager.T("BatchOps.KindCfRadio"),
                GroupName = "BatchDumpKind",
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _kindDtRadio.IsCheckedChanged += (_, _) => OnKindChecked(BatchOperationKind.DumpDt);
            _kindCfRadio.IsCheckedChanged += (_, _) => OnKindChecked(BatchOperationKind.DumpCf);
            // v1.0.1.25: шаблон — на всю ширину правой колонки, DT/CF — строкой ниже,
            // плюс опция выгрузки запущенных баз (ibcmd не проверяет сеансы).
            var templateRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            Grid.SetColumn(templateLabel, 0);
            Grid.SetColumn(templateBox, 1);
            templateRow.Children.Add(templateLabel);
            templateRow.Children.Add(templateBox);
            var kindRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0),
                Children = { _kindDtRadio, _kindCfRadio }
            };
            var allowRunningCheck = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T("BatchOps.AllowRunningBases"),
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(0, 6, 0, 0)
            };
            ToolTip.SetTip(allowRunningCheck, LocalizationManager.T("BatchOps.AllowRunningBasesHint"));
            allowRunningCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("AllowRunningBases") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var dumpOptionsRow = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Children = { templateRow, kindRow, allowRunningCheck }
            };
            _dumpOptionsRow = dumpOptionsRow;

            // Блок «Перед операцией» (issue #92, v1.0.1.21, пожелание aepkugesi):
            // завершение сеансов и блокировки сеансов/регламентных заданий перед
            // выгрузкой/загрузкой; после операции блокировки снимаются автоматически.
            // Сервис подготовки; null — rac-клиент недоступен (в журнал сообщит VM).
            _vm.PreparationService = BatchOpsPreparationService.CreateDefault(
                line => _vm.WriteLog(line));
            var prepareHeader = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Prepare.Header"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 10, 0, 2)
            };
            var prepareTerminateCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.TerminateSessions"),
                Margin = new Thickness(0, 1)
            };
            prepareTerminateCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("PrepareTerminateSessions") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareDenySessionsCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.DenyNewSessions"),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            prepareDenySessionsCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("PrepareDenyNewSessions") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var preparePermissionLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Prepare.PermissionCode"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 6, 0)
            };
            var preparePermissionBox = new TextBox
            {
                Width = 140,
                VerticalContentAlignment = VerticalAlignment.Center
            }.Styled(ControlThemes.ModernTextBox);
            ThemeBrushes.Bind(preparePermissionBox, TextBox.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(preparePermissionBox, TextBox.CaretBrushProperty, "TextPrimaryBrush");
            ToolTip.SetTip(preparePermissionBox, LocalizationManager.T("BatchOps.Prepare.PermissionCodeHint"));
            preparePermissionBox.Bind(TextBox.TextProperty,
                new Avalonia.Data.Binding("PreparePermissionCode") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareDenyRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1),
                Children = { prepareDenySessionsCheck, preparePermissionLabel, preparePermissionBox }
            };
            var prepareDenyJobsCheck = new CheckBox
            {
                Content = LocalizationManager.T("BatchOps.Prepare.DenyScheduledJobs"),
                Margin = new Thickness(0, 1)
            };
            prepareDenyJobsCheck.Bind(CheckBox.IsCheckedProperty,
                new Avalonia.Data.Binding("PrepareDenyScheduledJobs") { Mode = Avalonia.Data.BindingMode.TwoWay });
            var prepareHint = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Prepare.Hint"),
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
            // Поле «Пароль пользователя СУБД» (issue #92, v1.0.1.26, пожелание 7OH):
            // rac отдаёт логин СУБД (db-user), но не пароль; если у базы нет профиля
            // СУБД — пароль вводится здесь и подставляется в профиль после rac-резолва.
            var sqlPasswordLabel = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.SqlPasswordLabel"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 10, 0, 2)
            };
            ToolTip.SetTip(_sqlPasswordBox, LocalizationManager.T("BatchOps.SqlPasswordHint"));
            _sqlPasswordBox.Width = 260;
            var sqlPasswordHint = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.SqlPasswordHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };
            var sqlPasswordPanel = new StackPanel
            {
                Children = { sqlPasswordLabel, _sqlPasswordBox, sqlPasswordHint }
            };
            var pathStack = new StackPanel { Children = { pathRow, dumpOptionsRow, preparePanel, sqlPasswordPanel } };

            var logHeader = new TextBlock
            {
                Text = LocalizationManager.T("BatchOps.Log"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 6)
            };

            _logList.ItemsSource = _vm.LogLines;
            // issue #92 (v1.0.1.26): журнал только читается — выделение строк гасим.
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
                Children = { _stopButton, _runButton, closeButton } // issue #98: «Закрыть» последней
            };

            // Раскладка issue #92 (v1.0.1.25, комментарий 7OH): список баз с поиском и
            // кнопками «Выделить все/Снять все» — слева, на ВСЮ высоту окна и вдвое уже
            // прежнего (колонки 1* : 2*); справа сверху вниз — подсказка, параметры
            // (высота по содержимому — подписи галочек видны целиком), журнал (остаток
            // высоты), сводка и кнопки.
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

            UpdateKindDependentUi();
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;
        }

        private readonly TextBlock? _pathLabel;
        private StackPanel? _dumpOptionsRow;
        private RadioButton? _kindDtRadio;
        private RadioButton? _kindCfRadio;
        private bool _suppressKindEvents;

        /// <summary>Обновляет заголовок, подсказку и подпись поля пути по текущему виду операции.</summary>
        private void UpdateKindDependentUi()
        {
            Title = LocalizationManager.T(_vm.Kind switch
            {
                BatchOperationKind.DumpDt => "BatchOps.TitleDumpDt",
                BatchOperationKind.DumpCf => "BatchOps.TitleDumpCf",
                _ => "BatchOps.TitleLoadCf"
            });
            _hint.Text = LocalizationManager.T(_vm.Kind switch
            {
                BatchOperationKind.DumpDt => "BatchOps.HintDumpDt",
                BatchOperationKind.DumpCf => "BatchOps.HintDumpCf",
                _ => "BatchOps.HintLoadCf"
            });
            if (_pathLabel is not null)
                _pathLabel.Text = _vm.PathLabel;
            // Опции шаблона и DT/CF видны только у выгрузок (issue #92).
            if (_dumpOptionsRow is not null)
                _dumpOptionsRow.IsVisible = _vm.IsDumpKind;
            _suppressKindEvents = true;
            if (_kindDtRadio is not null)
                _kindDtRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpDt;
            if (_kindCfRadio is not null)
                _kindCfRadio.IsChecked = _vm.Kind == BatchOperationKind.DumpCf;
            _suppressKindEvents = false;
        }

        private void OnKindChecked(BatchOperationKind kind)
        {
            if (_suppressKindEvents || _vm is null || _vm.Kind == kind)
                return;
            _vm.Kind = kind;
            UpdateKindDependentUi();
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
            if (_vm.IsDumpKind)
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
            // Пароль PasswordBox не биндится (безопасность) — забираем на старте прогона.
            _vm.SqlPassword = _sqlPasswordBox.Password ?? string.Empty;
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
            else if (e.PropertyName == nameof(BatchOperationsViewModel.PathLabel) && _pathLabel is not null)
            {
                _pathLabel.Text = _vm.PathLabel;
            }
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
        /// «Умный» автоскролл журнала (issue #92, v1.0.1.26): прокручиваем к последней
        /// строке ТОЛЬКО пока пользователь стоит на конце списка. Прокрутив вверх,
        /// он может спокойно читать ошибки — список не дёргается, пока он не вернётся
        /// к концу. Флаг меняется только при сдвиге СМЕЩЕНИЯ прокрутки; изменение
        /// экстента от добавленных строк его не сбрасывает.
        /// </summary>
        private void HookLogScrollViewer()
        {
            if (_logScrollViewer is not null)
                return;
            _logScrollViewer = _logList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_logScrollViewer is not null)
                _logScrollViewer.ScrollChanged += OnLogScrollChanged;
        }

        private void OnLogScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
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
