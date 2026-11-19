#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Workspace1C.Controls;
using Workspace1C.Themes;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания ИБ через CREATEINFOBASE (пустая или из шаблона .cf/.dt).
    /// Поддерживает файловый и клиент-серверный варианты (с параметрами СУБД).
    /// Avalonia/Linux-версия WPF-окна <see cref="CreateInfobaseWindow"/>.
    /// </summary>
    public class CreateInfobaseWindow : ModalWindowBase
    {
        private readonly bool _fromTemplate;
        private readonly IReadOnlyList<string> _platformVersions;
        private readonly IReadOnlyList<Group> _groups;
        private readonly IReadOnlyList<string> _availableServers;
        private string _selectedGroupPath;
        private readonly IDialogService _dialogs;
        private readonly IInfobaseRepository _repository =
            AppServices.GetRequiredService<IInfobaseRepository>();
        private readonly ICreateInfobaseService _createService =
            AppServices.GetRequiredService<ICreateInfobaseService>();

        private readonly ComboBox _typeBox = new();
        private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBlock _groupPathBox = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _platformBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _filePathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _templateBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly StackPanel _filePanel = new() { Spacing = 8 };

        // Поля клиент-серверного варианта.
        private readonly StackPanel _serverPanel = new() { Spacing = 8 };
        // Редактируемый ComboBox: выбор из списка известных серверов 1С + свободный ввод (issue #305).
        // Сервер выбирается одним полем «server:port», как в окне правки свойств базы.
        // IsTextSearchEnabled=false (issue #305, 0.3.9.262): автоподбор при наборе текста выбирал
        // первый совпадающий элемент и через SelectionChanged перезаписывал ручной ввод —
        // «буква L съедалась», вводилось только ocalhost.
        private readonly ComboBox _serverBox = new() { IsEditable = true, IsTextSearchEnabled = false };
        private readonly TextBox _refBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly ComboBox _dbmsBox = new() { IsEditable = true };
        private readonly TextBox _dbServerBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _dbPortBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        // Кликабельная подсказка: клик по «Например localhost» подставляет пример (issue #305).
        private readonly TextBlock _dbServerHint = new()
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        private readonly TextBox _dbNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _dbUserBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly PasswordBox _dbPwdBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);
        private readonly CheckBox _createDbCheck = new();
        private readonly CheckBox _blockJobsCheck = new();
        private readonly CheckBox _forbidSpeechCheck = new();

        private readonly TreeView _templateTree = new() { SelectionMode = SelectionMode.Single, Height = 260 };
        private readonly TextBlock _templateRootsHint = new()
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        private readonly StackPanel _templateLoadingPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
            IsVisible = false
        };
        // Корневой контейнер окна: по нему измеряется нужная высота контента (issue #305).
        private Grid? _rootGrid;
        private int _templateLoadGeneration;
        private bool _closed;
        private List<OneCTemplateService.TemplateInfo> _flatTemplates = new();
        private bool _platformSelectionIsFile = true;
        private string? _filePlatformSelection;
        private string? _clientServerPlatformSelection;

        /// <summary>Доступные значения СУБД для клиент-серверного создания.</summary>
        private static readonly string[] DbmsValues =
        {
            "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase"
        };

        public Infobase? Result { get; private set; }

        public CreateInfobaseWindow(
            bool fromTemplate,
            IEnumerable<string> platformVersions,
            string defaultGroupPath = "",
            IEnumerable<Group>? groups = null,
            IEnumerable<string>? availableServers = null)
        {
            _fromTemplate = fromTemplate;
            _platformVersions = platformVersions?.ToList() ?? new List<string>();
            _groups = groups?.ToList() ?? new List<Group>();
            _availableServers = availableServers?.ToList() ?? new List<string>();
            _selectedGroupPath = defaultGroupPath ?? string.Empty;
            _dialogs = AppServices.GetRequiredService<IDialogService>();

            Title = fromTemplate
                ? LocalizationManager.T("CreateInfobase.TitleFromTemplate")
                : LocalizationManager.T("CreateInfobase.TitleEmpty");
            Width = 560;
            Height = 640;
            MinHeight = 420;
            MaxHeight = 800;
            // Окно без максимизации (issue #3): в Avalonia кнопка разворота в системном
            // заголовке управляется через CanResize — выставляем false (ресайз некритичен).
            CanResize = false;
            // Явная высота вместо SizeToContent.Height: авторазмер мог схлопывать окно в пустой
            // прямоугольник при модальном показе (регресс «странного/пустого окна», issue #305,
            // общий с редактором сценария #308). После показа окно один раз подгоняется под
            // контент (FitHeightToContent) и удерживается в рабочей области; MaxHeight
            // ограничивает рост, MinHeight — слишком сильное сжатие.
            Opened += (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(FitHeightToContent, Avalonia.Threading.DispatcherPriority.Background);

            _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            Closed += (_, _) => _closed = true;

            Content = BuildRoot();
            RefreshPlatformList();
            RememberPlatformSelection(true, _platformBox.Text);
            if (_fromTemplate)
                LoadInstalledTemplates();
        }

        private string HintText => _fromTemplate
            ? LocalizationManager.T("CreateInfobase.HintTemplate")
            : LocalizationManager.T("CreateInfobase.HintEmpty");

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            _rootGrid = grid;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // Область полей растягивается, когда окно фиксированной высоты
            // с деревом шаблонов, и сжимается по содержимому в пустом режиме.
            grid.RowDefinitions.Add(_fromTemplate
                ? new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 200 }
                : new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 4)
            };
            header.Children.Add(new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new HelpLink
            {
                HelpText = LocalizationManager.T("CreateInfobase.HelpText"),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var hint = new TextBlock
            {
                Text = HintText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);

            var fields = new StackPanel { Spacing = 10 };

            // Тип базы
            _typeBox.Items.Clear();
            _typeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.T("CreateInfobase.TypeFile") });
            _typeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.T("CreateInfobase.TypeClientServer") });
            _typeBox.SelectedIndex = 0;
            _typeBox.SelectionChanged += (_, _) => OnTypeChanged();
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.TypeLabel"), _typeBox));

            // Наименование (кнопка копирования перенесена к полю «Имя базы данных», issue #306).
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.NameLabel"), _nameBox));

            // Группа
            var groupRow = new Grid();
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            // Issue #61: подписи полей формы привязываются к FormLabelBrush.
            var gl = new TextBlock { Text = LocalizationManager.T("CreateInfobase.GroupLabel"), VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(gl, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(gl, 0);
            groupRow.Children.Add(gl);
            ToolTip.SetTip(_groupPathBox, LocalizationManager.T("CreateInfobase.GroupTooltip"));
            Grid.SetColumn(_groupPathBox, 1);
            groupRow.Children.Add(_groupPathBox);
            var pickGroup = new Button { Content = LocalizationManager.T("CreateInfobase.ChooseGroup"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickGroup.Styled(ControlThemes.SecondaryButton);
            pickGroup.Padding = new Thickness(10, 4);
            ToolTip.SetTip(pickGroup, LocalizationManager.T("CreateInfobase.ChooseGroupTooltip"));
            pickGroup.Click += (_, _) => OnPickGroup_Click();
            Grid.SetColumn(pickGroup, 2);
            groupRow.Children.Add(pickGroup);
            fields.Children.Add(groupRow);

            // Платформа
            var platRow = new Grid();
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var pl = new TextBlock { Text = LocalizationManager.T("CreateInfobase.PlatformVersionLabel"), VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(pl, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(pl, 0);
            platRow.Children.Add(pl);
            ToolTip.SetTip(_platformBox, LocalizationManager.T("CreateInfobase.SelectedPlatformTooltip"));
            Grid.SetColumn(_platformBox, 1);
            platRow.Children.Add(_platformBox);
            var platButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
            var pickPlatform = new Button { Content = LocalizationManager.T("CreateInfobase.List"), MinWidth = 90 };
            pickPlatform.Styled(ControlThemes.SecondaryButton);
            pickPlatform.Padding = new Thickness(10, 4);
            ToolTip.SetTip(pickPlatform, LocalizationManager.T("CreateInfobase.ListTooltip"));
            pickPlatform.Click += (_, _) => OnPickPlatform_Click();
            platButtons.Children.Add(pickPlatform);
            var editPaths = new Button { Content = LocalizationManager.T("CreateInfobase.Paths"), MinWidth = 70 };
            editPaths.Styled(ControlThemes.SecondaryButton);
            editPaths.Padding = new Thickness(10, 4);
            ToolTip.SetTip(editPaths, LocalizationManager.T("CreateInfobase.PathsTooltip"));
            editPaths.Click += (_, _) => OnEditPlatformPaths_Click();
            platButtons.Children.Add(editPaths);
            Grid.SetColumn(platButtons, 2);
            platRow.Children.Add(platButtons);
            fields.Children.Add(platRow);

            // Файловая база: путь к каталогу.
            var browseFile = new Button { Content = LocalizationManager.T("Common.Browse"), MinWidth = 90 };
            browseFile.Styled(ControlThemes.SecondaryButton);
            browseFile.Padding = new Thickness(10, 4);
            browseFile.Click += (_, _) => OnBrowseFolder_Click();
            var fileRow = new Grid();
            fileRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            fileRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_filePathBox, 0);
            fileRow.Children.Add(_filePathBox);
            Grid.SetColumn(browseFile, 1);
            browseFile.Margin = new Thickness(8, 0, 0, 0);
            fileRow.Children.Add(browseFile);
            _filePanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DirLabel"), fileRow));

            // Клиент-серверная база: сервер 1С, имя базы и параметры СУБД.
            _dbmsBox.Items.Clear();
            foreach (var v in DbmsValues)
                _dbmsBox.Items.Add(new ComboBoxItem { Content = v });
            // Компактный макет (issue #305): две колонки — параметры сервера 1С слева,
            // подключение к СУБД справа; галочки — внизу левой колонки.
            var serverGrid = new Grid { ColumnSpacing = 16 };
            serverGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            serverGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var leftCol = new StackPanel { Spacing = 8 };
            // Список известных серверов 1С из зарегистрированных клиент-серверных
            // баз — выпадающий список поля «Сервер 1С» (issue #305).
            _serverBox.Items.Clear();
            foreach (var s in _availableServers)
                _serverBox.Items.Add(new ComboBoxItem { Content = s });
            // Сервер 1С выбирается одним полем «server:port», как в окне правки
            // свойств базы (issue #305): отдельное поле порта не нужно.
            leftCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.ServerLabel"), _serverBox));
            // Имя базы на сервере (кнопка «скопировать в наименование» — у поля «Наименование», issue #306).
            leftCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.RefLabel"), _refBox));
            leftCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbmsLabel"), _dbmsBox));

            // Галочки создания (как в типовом стартере), внизу левой колонки.
            // Текст оборачивается в TextBlock: у CheckBox в этой версии Avalonia
            // свойства TextWrapping нет, а перенос нужен в узкой колонке.
            _createDbCheck.Content = new TextBlock
            {
                Text = LocalizationManager.T("CreateInfobase.CreateDatabase"),
                TextWrapping = TextWrapping.Wrap
            };
            _createDbCheck.Margin = new Thickness(0, 6, 0, 0);
            leftCol.Children.Add(_createDbCheck);
            _blockJobsCheck.Content = new TextBlock
            {
                Text = LocalizationManager.T("CreateInfobase.BlockScheduledJobs"),
                TextWrapping = TextWrapping.Wrap
            };
            _blockJobsCheck.Margin = new Thickness(0, 6, 0, 0);
            leftCol.Children.Add(_blockJobsCheck);
            // Запрет локального распознавания речи (issue #307), как в типовом стартере.
            _forbidSpeechCheck.Content = new TextBlock
            {
                Text = LocalizationManager.T("CreateInfobase.ForbidSpeechRecognition"),
                TextWrapping = TextWrapping.Wrap
            };
            _forbidSpeechCheck.Margin = new Thickness(0, 6, 0, 0);
            leftCol.Children.Add(_forbidSpeechCheck);

            var rightCol = new StackPanel { Spacing = 8 };
            // Сервер СУБД и порт в одной строке + живая подсказка формата DBSrvr (issue #305).
            var dbServerRow = new Grid();
            dbServerRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            dbServerRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));
            Grid.SetColumn(_dbServerBox, 0);
            dbServerRow.Children.Add(_dbServerBox);
            ToolTip.SetTip(_dbPortBox, LocalizationManager.T("CreateInfobase.DbPortTooltip"));
            Grid.SetColumn(_dbPortBox, 1);
            _dbPortBox.Margin = new Thickness(8, 0, 0, 0);
            dbServerRow.Children.Add(_dbPortBox);
            var dbServerField = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            dbServerField.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            dbServerField.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var dbsLabel = new TextBlock
            {
                Text = LocalizationManager.T("CreateInfobase.DbServerLabel"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dbsLabel, 0);
            dbServerField.Children.Add(dbsLabel);
            dbServerRow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dbServerRow, 1);
            dbServerField.Children.Add(dbServerRow);
            rightCol.Children.Add(dbServerField);
            rightCol.Children.Add(_dbServerHint);
            // «Имя базы данных» с кнопкой «скопировать из имени базы на сервере» (issue #306).
            var dbNameRow = new Grid();
            dbNameRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            dbNameRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_dbNameBox, 0);
            dbNameRow.Children.Add(_dbNameBox);
            var copyRefToDb = new Button
            {
                Content = IconHelper.MakeIcon("IconCopy", 16),
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0)
            };
            copyRefToDb.Styled(ControlThemes.IconButton);
            ToolTip.SetTip(copyRefToDb, LocalizationManager.T("CreateInfobase.CopyRefToDbName"));
            copyRefToDb.Click += (_, _) => CopyRefToDbName();
            Grid.SetColumn(copyRefToDb, 1);
            dbNameRow.Children.Add(copyRefToDb);
            rightCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbNameLabel"), dbNameRow));
            rightCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbUserLabel"), _dbUserBox));
            rightCol.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbPasswordLabel"), _dbPwdBox));

            Grid.SetColumn(leftCol, 0);
            serverGrid.Children.Add(leftCol);
            Grid.SetColumn(rightCol, 1);
            serverGrid.Children.Add(rightCol);
            _serverPanel.Children.Add(serverGrid);

            // Живая подсказка меняется по выбранной/введённой СУБД и значениям сервера/порта.
            _dbmsBox.SelectionChanged += (_, _) => UpdateDbServerHint();
            _dbmsBox.GetObservable(ComboBox.TextProperty)
                .Subscribe(new ValueObserver<string?>(_ => UpdateDbServerHint()));
            _dbServerBox.TextChanged += (_, _) => UpdateDbServerHint();
            _dbPortBox.TextChanged += (_, _) => UpdateDbServerHint();

            // Выбор сервера 1С «server:port» разносится на сервер и порт (issue #305).
            _serverBox.SelectionChanged += (_, _) => SplitSelectedServer();
            // Страховка подстановки (issue #305): при закрытии списка выбранный
            // элемент ещё раз применяется к полю — SelectionChanged мог прийти
            // до готовности редактируемого текста ComboBox.
            _serverBox.DropDownClosed += (_, _) => SplitSelectedServer();
            // Клик по подсказке подставляет пример «localhost» в поле «Сервер СУБД» (issue #305).
            _dbServerHint.PointerPressed += (_, _) => ApplyDbServerHintExample();
            ToolTip.SetTip(_dbServerHint, LocalizationManager.T("CreateInfobase.DbServerHintClick"));

            // Последний тип базы, сервер СУБД и порт из настроек подставляются по умолчанию (issue #305).
            RestoreLastCreateState();
            UpdateDbServerHint();

            // «Сервер СУБД» запоминается и при закрытии окна (issue #305): пользователь мог
            // ввести значение и закрыть окно без создания — сохранение только после успешного
            // создания такой сценарий не покрывало.
            Closed += OnWindowClosed;

            fields.Children.Add(_filePanel);
            fields.Children.Add(_serverPanel);
            _serverPanel.IsVisible = false;

            // Шаблон: дерево установленных поставок из манифестов 1cv8.mft плюс ручной выбор файла.
            if (_fromTemplate)
            {
                var tplPanel = new StackPanel();
                tplPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("CreateInfobase.TemplateManifestsLabel"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                tplPanel.Children.Add(_templateRootsHint);

                // Индетерминантный индикатор держит рендер-цикл занятым и на программном
                // рендере/в виртуализации даёт постоянную перерисовку (issue #153). Там
                // рисуем статичную заполненную полосу, как в главном окне.
                var disableAnimations = Services.LinuxRendering.DisableAnimations;
                _templateLoadingPanel.Children.Add(new ProgressBar
                {
                    Width = 140,
                    Height = 14,
                    IsIndeterminate = !disableAnimations,
                    Value = disableAnimations ? 100 : 0,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _templateLoadingPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("CreateInfobase.Loading"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                tplPanel.Children.Add(_templateLoadingPanel);

                _templateTree.ItemTemplate = new FuncTreeDataTemplate(
                    typeof(object),
                    (item, _) => BuildTemplateRow(item),
                    item => item is OneCTemplateService.TemplateTreeNode n && n.Children.Count > 0
                        ? n.Children
                        : (System.Collections.IEnumerable)System.Array.Empty<object>());
                // В разметке WPF узлы дерева раскрыты по умолчанию (ItemContainerStyle),
                // здесь то же самое стилем на TreeViewItem.
                _templateTree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
                {
                    Setters =
                    {
                        new Setter(TreeViewItem.IsExpandedProperty, true),
                        new Setter(TreeViewItem.PaddingProperty, new Thickness(2, 1))
                    }
                });
                _templateTree.SelectionChanged += (_, _) => OnTemplateSelected();
                _templateTree.Margin = new Thickness(0, 0, 0, 6);
                _templateTree.BorderThickness = new Thickness(1);
                ThemeBrushes.Bind(_templateTree, TemplatedControl.BorderBrushProperty, "BorderColorBrush");
                ThemeBrushes.Bind(_templateTree, TemplatedControl.BackgroundProperty, "CardBackgroundColorBrush");
                // Как в разметке WPF: горизонтальной прокрутки нет, иначе строке дерева
                // достаётся бесконечная ширина и длинный путь не переносится.
                ScrollViewer.SetHorizontalScrollBarVisibility(_templateTree, ScrollBarVisibility.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(_templateTree, ScrollBarVisibility.Auto);
                tplPanel.Children.Add(_templateTree);

                var tplRow = new Grid();
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                ToolTip.SetTip(_templateBox, LocalizationManager.T("CreateInfobase.TemplatePathTooltip"));
                Grid.SetColumn(_templateBox, 0);
                tplRow.Children.Add(_templateBox);
                var tplButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
                var refreshTemplates = new Button { Content = LocalizationManager.T("CreateInfobase.Refresh"), MinWidth = 90 };
                refreshTemplates.Styled(ControlThemes.SecondaryButton);
                refreshTemplates.Padding = new Thickness(10, 4);
                ToolTip.SetTip(refreshTemplates, LocalizationManager.T("CreateInfobase.RefreshTooltip"));
                refreshTemplates.Click += (_, _) => LoadInstalledTemplates();
                tplButtons.Children.Add(refreshTemplates);
                var browseTemplate = new Button { Content = LocalizationManager.T("CreateInfobase.File"), MinWidth = 90 };
                browseTemplate.Styled(ControlThemes.SecondaryButton);
                browseTemplate.Padding = new Thickness(10, 4);
                ToolTip.SetTip(browseTemplate, LocalizationManager.T("CreateInfobase.FileTooltip"));
                browseTemplate.Click += (_, _) => OnBrowseTemplate_Click();
                tplButtons.Children.Add(browseTemplate);
                Grid.SetColumn(tplButtons, 1);
                tplRow.Children.Add(tplButtons);
                tplPanel.Children.Add(tplRow);

                fields.Children.Add(tplPanel);
            }

            var fieldsHost = new ScrollViewer
            {
                Content = fields,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 4, 0)
            };
            Grid.SetRow(fieldsHost, 2);
            grid.Children.Add(fieldsHost);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 16, 0, 0)
            };
            // Оформление и порядок по разметке (CreateInfobaseWindow.xaml:184):
            // зелёное создание шириной 140 слева, красная отмена шириной 130 справа.
            // Кнопка создания закрывает окно сама, только если проверки прошли,
            // поэтому она собирается здесь, а не общим методом базового класса.
            var createCaption = new TextBlock
            {
                Text = LocalizationManager.T("CreateInfobase.Create"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var create = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    // Значок на акцентной кнопке — контрастный TextOnAccentBrush (issue #26).
                    Children = { IconHelper.MakeIcon("IconDatabase", 16, "TextOnAccentBrush"), createCaption }
                },
                Width = 140,
                Height = 36,
                IsDefault = true,
                // Аудит UI P3a: кнопки нижней панели идут после полей формы.
                TabIndex = 900
            };
            create.Styled(ControlThemes.DialogConfirmButton);
            RegisterConfirmCaption(createCaption, "CreateInfobase.Create");
            create.Click += (_, _) => OnCreate_Click();
            buttons.Children.Add(create);
            buttons.Children.Add(BuildCancelActionButton(130));

            Grid.SetRow(buttons, 5);
            grid.Children.Add(buttons);

            return grid;
        }

        private void OnTypeChanged()
        {
            var isFile = _typeBox.SelectedIndex != 1;
            _filePanel.IsVisible = isFile;
            _serverPanel.IsVisible = !isFile;
            // В пределах открытого окна ручной выбор хранится отдельно для каждого типа.
            // При первом переходе используется последняя успешная версия из настроек.
            RememberPlatformSelection(_platformSelectionIsFile, _platformBox.Text);
            _platformSelectionIsFile = isFile;
            RefreshPlatformList(
                replaceSelection: true,
                preferredSelection: GetPlatformSelection(isFile));
            RememberPlatformSelection(isFile, _platformBox.Text);

            // Высота окна пересчитывается под новый набор полей и окно удерживается
            // в рабочей области (issue #305). Отложенный вызов — панели должны сначала
            // перестроиться по новому типу.
            Avalonia.Threading.Dispatcher.UIThread.Post(FitHeightToContent, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Подгоняет высоту окна под содержимое (явно, без <c>SizeToContent.Height</c>) и не даёт
        /// окну уйти за нижний край рабочей области при изменении типа базы (issue #305).
        /// Логика клампинга — чистый <see cref="WindowSizeMath"/> (общий с WPF, покрыт тестами).
        /// </summary>
        private void FitHeightToContent()
        {
            if (_rootGrid is null || !IsVisible)
                return;

            var availableWidth = _rootGrid.Bounds.Width > 0 ? _rootGrid.Bounds.Width : Math.Max(400, Width);
            _rootGrid.Measure(new Size(availableWidth, double.PositiveInfinity));
            var desired = _rootGrid.DesiredSize.Height;
            if (desired <= 0)
                return;

            // Хром (заголовок + рамки/декор) — разница между полной высотой и клиентской областью.
            var chrome = Math.Max(0, ClientSize.Height - (_rootGrid.Bounds.Height > 0 ? _rootGrid.Bounds.Height : desired));
            Height = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);

            // Окно стояло у нижнего края экрана и выросло при смене типа — поднимаем его,
            // чтобы нижняя часть не уходила за экран. Расчёт позиции — чистый
            // WindowSizeMath.FitTop (общий с WPF); координаты в физических пикселях.
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                var wa = screen.WorkingArea;
                var heightPx = (int)(Height * screen.Scaling);
                var newTopPx = (int)WindowSizeMath.FitTop(Position.Y, heightPx, wa.Y, wa.Bottom);
                if (newTopPx != Position.Y)
                    Position = new PixelPoint(Position.X, newTopPx);
            }
        }

        private static Control BuildTemplateRow(object? item)
        {
            var node = item as OneCTemplateService.TemplateTreeNode;
            // Ширину не фиксируем: после отступов дерева фиксированная не влезает
            // и длинный путь уходит под полосу прокрутки.
            var panel = new StackPanel { Margin = new Thickness(2, 3) };
            panel.Children.Add(new TextBlock
            {
                Text = node?.Title ?? item?.ToString() ?? "",
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(node?.Subtitle))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = node!.Subtitle,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 36
                });
            }
            return panel;
        }

        private void OnTemplateSelected()
        {
            if (_templateTree.SelectedItem is OneCTemplateService.TemplateTreeNode { Template: { } t })
            {
                _templateBox.Text = t.FilePath;
                if (string.IsNullOrWhiteSpace(_nameBox.Text) || NameWasSuggested())
                    _nameBox.Text = SuggestNameFromTemplate(t);
            }
        }

        private bool NameWasSuggested()
        {
            var name = _nameBox.Text?.Trim() ?? "";
            return _flatTemplates.Any(t =>
                SuggestNameFromTemplate(t).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static string SuggestNameFromTemplate(OneCTemplateService.TemplateInfo t)
        {
            // Как в стартере 1С: последний сегмент Catalog (без суффиксов демо/пустая)
            var segs = t.CatalogSegments;
            if (segs.Length > 0)
            {
                var leaf = segs[^1]
                    .Replace(LocalizationManager.T("Template.SuffixDemo"), "", StringComparison.OrdinalIgnoreCase)
                    .Replace(LocalizationManager.T("Template.SuffixEmpty"), "", StringComparison.OrdinalIgnoreCase)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(leaf))
                    return leaf;
            }
            if (!string.IsNullOrWhiteSpace(t.ConfigurationName) && t.ConfigurationName != "—")
                return t.ConfigurationName;
            var parts = t.RelativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[^2];
            if (parts.Length == 1)
                return Path.GetFileNameWithoutExtension(parts[0]);
            return Path.GetFileNameWithoutExtension(t.FilePath);
        }

        private void LoadInstalledTemplates()
        {
            // Общий сервис токена отмены не принимает, поэтому уже запущенный обход
            // каталогов доводится до конца, а его результат отбрасывается по номеру
            // поколения. Так же устроено в версии для Windows, только там для этого
            // держится CancellationTokenSource, который ничего не отменяет.
            var generation = ++_templateLoadGeneration;

            // Окно показываем сразу, а сканирование каталогов шаблонов идёт в фоне:
            // на настоящей поставке манифестов около тысячи.
            ShowTemplateLoading(true);
            _templateTree.ItemsSource = null;
            _flatTemplates = new List<OneCTemplateService.TemplateInfo>();

            Task.Run(() =>
            {
                var roots = OneCTemplateService.GetTemplateRootFolders().ToList();
                var primary = roots.Count > 0
                    ? roots[0]
                    : OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
                var primaryExists = Directory.Exists(primary);

                var templates = OneCTemplateService.FindInstalledTemplates().ToList();
                var tree = OneCTemplateService.BuildTemplateTree(templates);
                return (Primary: primary, Roots: (IReadOnlyList<string>)roots, PrimaryExists: primaryExists,
                        Templates: templates, Tree: tree);
            }).ContinueWith(t =>
            {
                // Исключение фоновой задачи читаем всегда, иначе оно остаётся
                // ненаблюдённым и всплывает как UnobservedTaskException.
                var error = t.Exception;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_closed || generation != _templateLoadGeneration)
                        return;

                    ShowTemplateLoading(false);

                    if (error is not null)
                    {
                        _flatTemplates = new List<OneCTemplateService.TemplateInfo>();
                        _templateTree.ItemsSource = null;
                        _templateRootsHint.Text += LocalizationManager.T("CreateInfobase.LoadingFailed");
                        return;
                    }

                    var r = t.Result;
                    _flatTemplates = r.Templates;
                    _templateTree.ItemsSource = r.Tree;
                    UpdateTemplateRootsHint(r.Primary, r.Roots, r.PrimaryExists);

                    if (r.Templates.Count == 0)
                        _templateRootsHint.Text += LocalizationManager.T("CreateInfobase.NoTemplates");
                });
            });
        }

        private void UpdateTemplateRootsHint(string primary, IReadOnlyList<string> roots, bool primaryExists)
        {
            _templateRootsHint.Text =
                string.Format(LocalizationManager.T("CreateInfobase.TemplateRootsDefault"), primary) +
                (primaryExists ? "" : LocalizationManager.T("CreateInfobase.FolderNotCreated")) +
                (roots.Count > 1
                    ? string.Format(LocalizationManager.T("CreateInfobase.AlsoChecked"),
                        string.Join("; ", roots.Where(r =>
                            !r.Equals(primary, StringComparison.OrdinalIgnoreCase))))
                    : "");
        }

        private void ShowTemplateLoading(bool loading)
        {
            _templateLoadingPanel.IsVisible = loading;
            _templateTree.IsEnabled = !loading;
        }

        private void OnEditPlatformPaths_Click()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                // Список версий обновляется после закрытия настроек, как в версии
                // для Windows. Команда главной модели показывает окно без ожидания,
                // поэтому окно открывается здесь напрямую и модально.
                new SettingsWindow(vm).ShowDialogSync(this);
                RefreshPlatformList();
            }
            else
            {
                _dialogs.ShowInfo(
                    LocalizationManager.T("CreateInfobase.NoPlatformsPathsMsg"),
                    LocalizationManager.T("CreateInfobase.PlatformPathsTitle"));
            }
        }

        private static Grid Field(string label, Control control)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            // Issue #61: подпись поля формы — единая кисть FormLabelBrush.
            var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(labelBlock, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private void OnPickGroup_Click()
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: null,
                allowNone: true,
                noneLabel: LocalizationManager.T("Connection.NoGroup"),
                kind: GroupPickerObjectKind.Infobase);
            if (dialog.ShowDialogSync(this))
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? LocalizationManager.T("Connection.NoGroup")
                    : _selectedGroupPath;
            }
        }

        private void RefreshPlatformList(bool replaceSelection = false, string? preferredSelection = null)
        {
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            var platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (platforms.Count == 0)
                platforms = _platformVersions.ToList();

            if (platforms.Count == 0)
            {
                if (replaceSelection)
                    _platformBox.Text = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(preferredSelection))
                preferredSelection = GetPlatformSelection(_typeBox.SelectedIndex != 1);

            // По умолчанию подставляем последнюю успешно использованную версию для текущего
            // типа базы (файловая/клиент-серверная), если она всё ещё установлена. Иначе — самую новую.
            string selected = platforms[0];
            var preferred = platforms.FirstOrDefault(p =>
                string.Equals(p, preferredSelection, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                selected = preferred;
            }
            else
            {
                var saved = GetSavedPlatformVersion();
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    foreach (var p in platforms)
                    {
                        PlatformVersionService.ParseVariant(p, out var clean, out _);
                        var candidate = string.IsNullOrWhiteSpace(clean) ? p : clean;
                        if (string.Equals(candidate, saved, StringComparison.OrdinalIgnoreCase))
                        {
                            selected = p;
                            break;
                        }
                    }
                }
            }

            if (replaceSelection || string.IsNullOrWhiteSpace(_platformBox.Text))
                _platformBox.Text = selected;
        }

        private string? GetPlatformSelection(bool isFile) =>
            isFile ? _filePlatformSelection : _clientServerPlatformSelection;

        private void RememberPlatformSelection(bool isFile, string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform))
                return;

            if (isFile)
                _filePlatformSelection = platform;
            else
                _clientServerPlatformSelection = platform;
        }

        /// <summary>
        /// Возвращает последнюю успешно использованную версию платформы для текущего типа базы.
        /// Тип базы определяется по <see cref="_typeBox"/>; по умолчанию (до инициализации) — файловая.
        /// </summary>
        private string GetSavedPlatformVersion()
        {
            var settings = _repository.LoadSettings();
            var isFile = _typeBox.SelectedIndex != 1;
            return isFile
                ? settings.LastFileCreatePlatformVersion ?? ""
                : settings.LastClientServerCreatePlatformVersion ?? "";
        }

        private void OnPickPlatform_Click()
        {
            RefreshPlatformList();
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            var platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (platforms.Count == 0)
                platforms = _platformVersions.ToList();

            var dlg = new PlatformVersionPickerWindow(platforms, _platformBox.Text ?? "");
            if (dlg.ShowDialogSync(this) && !string.IsNullOrWhiteSpace(dlg.Result))
            {
                _platformBox.Text = dlg.Result;
                RememberPlatformSelection(_typeBox.SelectedIndex != 1, dlg.Result);
            }
        }

        private void OnBrowseFolder_Click()
        {
            var path = _dialogs.OpenFolderDialog(LocalizationManager.T("CreateInfobase.ChooseFolderDescription"));
            if (!string.IsNullOrWhiteSpace(path))
                _filePathBox.Text = path;
        }

        private void OnBrowseTemplate_Click()
        {
            var path = _dialogs.OpenFileDialog(LocalizationManager.T("CreateInfobase.TemplateDialogTitle"),
                $"{LocalizationManager.T("CreateInfobase.FilterTemplates")}|*.cf;*.dt|{LocalizationManager.T("CreateInfobase.FilterConfig")}|*.cf|{LocalizationManager.T("CreateInfobase.FilterDump")}|*.dt|{LocalizationManager.T("Common.AllFiles")}|*.*");
            if (!string.IsNullOrWhiteSpace(path))
                _templateBox.Text = path;
        }

        /// <summary>
        /// Кнопка копирования у поля «Имя базы данных» (issue #306): переносит
        /// значение «Имя базы на сервере» (RefBox) в имя базы данных (DbNameBox).
        /// Пустое значение Ref имя базы данных не затирает.
        /// </summary>
        private void CopyRefToDbName()
        {
            var refName = _refBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(refName))
                _dbNameBox.Text = refName;
        }

        /// <summary>
        /// Восстанавливает последний тип базы, сервер СУБД и порт из настроек (issue #305).
        /// Тип выставляется первым (чтобы клиент-серверные поля были видны), затем —
        /// сервер/порт. Вызывается после построения полей, поэтому <see cref="_typeBox"/>
        /// уже существует; переключение типа на этом шаге не затирает подставляемые поля.
        /// </summary>
        private void RestoreLastCreateState()
        {
            var settings = _repository.LoadSettings();
            _typeBox.SelectedIndex =
                string.Equals(settings.LastCreateDbType, "ClientServer", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            if (!string.IsNullOrWhiteSpace(settings.LastCreateDbServer))
                _dbServerBox.Text = settings.LastCreateDbServer;
            if (!string.IsNullOrWhiteSpace(settings.LastCreateDbPort))
                _dbPortBox.Text = settings.LastCreateDbPort;
        }

        /// <summary>
        /// Закрытие окна: запоминает «Сервер СУБД», порт и тип базы (issue #305). Сохранение
        /// только после успешного создания не покрывало сценарий «ввёл значение и закрыл окно
        /// без создания». Сервер/порт сохраняются только в клиент-серверном режиме; тип —
        /// всегда (файловый режим не затирает ранее сохранённый сервер СУБД).
        /// </summary>
        private void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                var isClientServer = _typeBox.SelectedIndex == 1;
                _createService.SaveLastDbServer(
                    isClientServer ? _dbServerBox.Text?.Trim() ?? "" : "",
                    isClientServer ? _dbPortBox.Text?.Trim() ?? "" : "",
                    isClientServer);
            }
            catch
            {
                // Несохранение последнего сервера СУБД не должно ломать закрытие окна.
            }
        }

        /// <summary>
        /// Выбор сервера 1С из списка (issue #305): строка «server:port» остаётся в поле
        /// целиком, как в окне правки свойств базы; при создании она разнесётся
        /// на сервер и порт. Свободный ввод не затрагивается: при IsTextSearchEnabled=false
        /// событие приходит только от явного выбора элемента списка, а не от автоподбора
        /// по вводимому тексту (0.3.9.262).
        /// </summary>
        private void SplitSelectedServer()
        {
            if (_serverBox.SelectedItem is not ComboBoxItem { Content: string item })
                return;

            _serverBox.Text = item;
            // Сброс выделения откладываем: немедленный SelectedItem=null в редактируемом
            // ComboBox синхронизирует Text обратно и затирает только что подставленную
            // строку («поле оставалось пустым», регресс после 0.3.9.150, issue #305).
            var box = _serverBox;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (box.SelectedItem is not null)
                {
                    // Страховка (issue #305, 0.3.9.295): если сброс выделения всё же
                    // перезаписал поле пустой/старой строкой — восстанавливаем подставленную.
                    var keep = box.Text;
                    box.SelectedItem = null;
                    if (!string.Equals(box.Text, item, StringComparison.Ordinal))
                        box.Text = string.IsNullOrWhiteSpace(box.Text) ? keep : box.Text;
                }
            });
        }

        /// <summary>Клик по подсказке: подставляет пример «localhost» в поле «Сервер СУБД» (issue #305).</summary>
        private void ApplyDbServerHintExample()
        {
            if (string.IsNullOrWhiteSpace(_dbServerBox.Text))
                _dbServerBox.Text = "localhost";
        }

        /// <summary>
        /// Живая подсказка под полем «Сервер СУБД» (issue #305): формат значения
        /// DBSrvr зависит от выбранной СУБД (PostgreSQL — «host port=NNNN» через пробел,
        /// MSSQL Server — «host,NNNN»), а при заполненном сервере показываем,
        /// что именно будет передано в CREATEINFOBASE.
        /// </summary>
        private void UpdateDbServerHint()
        {
            var dbms = _dbmsBox.Text?.Trim() ?? "";
            var hintKey = string.Equals(dbms, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? "CreateInfobase.DbServerHintPostgres"
                : string.Equals(dbms, "MSSQLServer", StringComparison.OrdinalIgnoreCase)
                    ? "CreateInfobase.DbServerHintMssql"
                    : "CreateInfobase.DbServerHintOther";
            var hint = LocalizationManager.T(hintKey);

            var assembled = CreateInfobaseService.BuildDbServerString(
                dbms, _dbServerBox.Text, _dbPortBox.Text);
            if (!string.IsNullOrWhiteSpace(assembled))
                hint += "\n" + string.Format(
                    LocalizationManager.T("CreateInfobase.DbServerPreview"), assembled);

            _dbServerHint.Text = hint;
        }

        /// <summary>
        /// Сообщение об ошибке создания (issue #305): к тексту платформы добавляется понятная
        /// подсказка про порт кластера/агента 1С, если сервер указан с портом — это самая
        /// частая причина «база не создаётся» на сервере с нестандартным портом.
        /// </summary>
        private static string BuildCreateFailedMessage(CreateInfobaseResult result, string serverPort)
        {
            var message = string.Format(
                LocalizationManager.T("CreateInfobase.CreateFailed"), result.ErrorMessage ?? "");
            return string.IsNullOrWhiteSpace(serverPort)
                ? message
                : message + LocalizationManager.T("CreateInfobase.CreateFailedPortHint");
        }

        private void OnCreate_Click()
        {
            // Сервер 1С выбирается одним полем «server:port» — разносим на сервер и порт (issue #305).
            CreateInfobaseService.ParseServerPort(_serverBox.Text, out var serverName, out var serverPortFromName);
            var serverPort = serverPortFromName > 0
                ? serverPortFromName.ToString()
                : "";

            var request = new CreateInfobaseRequest
            {
                Name = _nameBox.Text?.Trim() ?? "",
                FromTemplate = _fromTemplate,
                TemplatePath = _templateBox.Text?.Trim(),
                PlatformVersion = _platformBox.Text?.Trim() ?? "",
                IsFile = _typeBox.SelectedIndex != 1,
                FilePath = _filePathBox.Text?.Trim(),
                Server = serverName,
                ServerPort = serverPort,
                DatabaseName = _refBox.Text?.Trim(),
                Dbms = _dbmsBox.Text?.Trim(),
                DbServer = _dbServerBox.Text?.Trim(),
                DbPort = _dbPortBox.Text?.Trim(),
                DbName = _dbNameBox.Text?.Trim(),
                DbUser = _dbUserBox.Text?.Trim(),
                DbPassword = _dbPwdBox.Password ?? "",
                CreateSqlDatabase = _createDbCheck.IsChecked == true,
                BlockScheduledJobs = _blockJobsCheck.IsChecked == true,
                ForbidSpeechRecognition = _forbidSpeechCheck.IsChecked == true,
                GroupPath = _selectedGroupPath
            };

            var result = _createService.TryCreate(request, confirmVersionMismatch: false);
            switch (result.Kind)
            {
                case CreateInfobaseResultKind.EnterName:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterName"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterTemplateFile:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterTemplateFile"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.NoPlatform:
                    _dialogs.ShowWarning(
                        LocalizationManager.T("CreateInfobase.NoPlatform"),
                        LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterFilePath:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterFilePath"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.EnterServerAndDb:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterServerAndDb"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.VersionMismatch:
                {
                    // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
                    // отличается (по major.minor) от версий, которыми уже работают
                    // клиент-серверные базы на этом же сервере. Создание можно продолжить.
                    // В предупреждении показываются ОБА адреса (issue #305): адрес из ввода
                    // пользователя и полный адрес найденной базы из списка (с портом) —
                    // чтобы было понятно, откуда взялся порт, которого пользователь не вводил.
                    var mismatchServerAddress = string.IsNullOrWhiteSpace(result.IncompatibleExistingServerAddress)
                        ? request.Server
                        : result.IncompatibleExistingServerAddress;
                    var enteredServerAddress = CreateInfobaseService.Format1CServer(serverName, serverPortFromName);
                    var proceed = _dialogs.Confirm(
                        string.Format(
                            LocalizationManager.T("CreateInfobase.VersionMismatchMsg"),
                            request.PlatformVersion, result.IncompatibleExistingVersion,
                            mismatchServerAddress, enteredServerAddress),
                        LocalizationManager.T("CreateInfobase.VersionMismatchTitle"));
                    if (!proceed)
                        return;
                    result = _createService.TryCreate(request, confirmVersionMismatch: true);
                    if (result.Kind == CreateInfobaseResultKind.CreateFailed)
                    {
                        _dialogs.ShowError(BuildCreateFailedMessage(result, serverPort), LocalizationManager.T("CreateInfobase.CreateTitle"));
                        return;
                    }
                    break;
                }
                case CreateInfobaseResultKind.CreateFailed:
                    _dialogs.ShowError(BuildCreateFailedMessage(result, serverPort), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                case CreateInfobaseResultKind.Success:
                    break;
            }

            Result = result.CreatedInfobase;
            DialogResult = true;
            Close();
        }

        /// <summary>Простой наблюдатель значения (для ComboBox.Text и пр.).</summary>
        private sealed class ValueObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;

            public ValueObserver(Action<T> onNext) => _onNext = onNext;

            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _onNext(value);
        }
    }
}
#endif
