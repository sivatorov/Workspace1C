#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Дублировать серверную базу» (0.3.9.100, функция №10, Avalonia/Linux):
    /// имя клона, целевой сервер 1С / имя базы на сервере (Ref), СУБД и учётные
    /// данные, группа, версия платформы (из источника) и режим копирования.
    /// Avalonia-версия WPF-окна <see cref="CloneServerInfobaseWindow"/>.
    /// </summary>
    public class CloneServerInfobaseWindow : ModalWindowBase
    {
        private readonly Infobase _source;
        private readonly IReadOnlyList<Group> _groups;
        private string _selectedGroupPath;
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();

        private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _serverBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _refBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly ComboBox _dbmsBox = new() { IsEditable = true };
        private readonly TextBox _dbServerBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _dbNameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _dbUserBox = new TextBox().Styled(ControlThemes.ModernTextBox);
        private readonly PasswordBox _dbPwdBox = new PasswordBox().Styled(ControlThemes.ModernPasswordBox);
        private readonly CheckBox _createDbCheck = new() { IsChecked = true };
        private readonly CheckBox _blockJobsCheck = new();
        private readonly TextBox _platformBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBlock _groupPathBox = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly RadioButton _modeFullRadio = new();
        private readonly RadioButton _modeConfigRadio = new();
        private readonly TextBlock _hintText = new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        };

        /// <summary>Запрос клонирования (заполняется при успешном выполнении).</summary>
        public ServerCloneRequest? Request { get; private set; }

        /// <summary>Подключение созданного клона (заполняется при успешном выполнении).</summary>
        public ConnectionSettings? ResultConnection { get; private set; }

        /// <summary>Доступные значения СУБД для клиент-серверного создания.</summary>
        private static readonly string[] DbmsValues =
        {
            "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase"
        };

        public CloneServerInfobaseWindow(
            Infobase source,
            IEnumerable<Group> groups,
            IEnumerable<string> existingRefNames)
        {
            _source = source;
            _groups = groups?.ToList() ?? new List<Group>();
            _selectedGroupPath = source.Group ?? string.Empty;

            Title = LocalizationManager.T("CloneServer.Title");
            Width = 560;
            // Форма длинная (14 полей). Раньше высота подстраивалась под всё
            // содержимое, и на экранах 768 px окно почти не помещалось. Теперь
            // высота ограничена рабочей областью, а поля прокручиваются.
            Height = 560;
            MinHeight = 360;
            CanResize = false;

            // Префилл: имя клона («— Копия»), сервер с портом, Ref с уникализацией
            // по локально известным базам на том же сервере, флаг блокировки заданий.
            _nameBox.Text = ServerClonePlanner.ProposeCloneName(source.Name);
            _serverBox.Text = source.Connection.GetServerWithPort();
            _refBox.Text = ServerClonePlanner.SuggestRefName(source.Connection.DatabaseName, existingRefNames);
            _blockJobsCheck.IsChecked = source.Connection.BlockScheduledJobs;
            _platformBox.Text = BuildPlatformDisplay(source);

            _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            Content = BuildRoot();
            UpdateModeHint();
            ApplyWorkAreaLimits();
        }

        /// <summary>
        /// Issue: высота окна не должна выходить за рабочую область монитора —
        /// общая политика диалогов (<see cref="DialogWorkAreaPolicy"/>), как у
        /// редакторов меню. Содержимое прокручивается, кнопки всегда видны.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
                return;
            var dipHeight = screen.WorkingArea.Height / screen.Scaling;
            MaxHeight = DialogWorkAreaPolicy.MaxHeight(dipHeight);
            MinHeight = DialogWorkAreaPolicy.ClampMinHeight(MinHeight, dipHeight);
            Height = DialogWorkAreaPolicy.ClampHeight(Height, MinHeight, dipHeight);
        }

        /// <summary>
        /// Версия платформы для отображения: чистая версия источника + суффикс
        /// разрядности «(32)/(64)», если архитектура источника задана явно.
        /// </summary>
        private static string BuildPlatformDisplay(Infobase source)
        {
            var version = (source.PlatformVersion ?? string.Empty).Trim();
            if (version.Length == 0)
                return string.Empty;
            return source.Architecture is "32" or "64"
                ? PlatformVersionService.FormatVariant(version, source.Architecture)
                : version;
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            // Заголовок (Auto), прокручиваемые поля (Star), кнопки (Auto):
            // кнопки остаются видимыми, даже если полей больше, чем помещается.
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
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
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var fields = new StackPanel { Spacing = 10 };

            // Режим копирования: полная копия (по умолчанию) / только конфигурация.
            var modePanel = new StackPanel { Spacing = 4 };
            _modeFullRadio.Content = LocalizationManager.T("CloneServer.ModeFull");
            _modeConfigRadio.Content = LocalizationManager.T("CloneServer.ModeConfig");
            _modeFullRadio.IsChecked = true;
            // IsCheckedChanged (вместо устаревшего ToggleButton.Checked) срабатывает и при
            // снятии отметки; подсказка читает текущее состояние, поэтому дубликаты безопасны.
            _modeFullRadio.IsCheckedChanged += (_, _) => UpdateModeHint();
            _modeConfigRadio.IsCheckedChanged += (_, _) => UpdateModeHint();
            modePanel.Children.Add(_modeFullRadio);
            modePanel.Children.Add(_modeConfigRadio);
            fields.Children.Add(Field(LocalizationManager.T("CloneServer.ModeLabel"), modePanel));

            fields.Children.Add(Field(LocalizationManager.T("Clone.NamePrompt"), _nameBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.ServerLabel"), _serverBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.RefLabel"), _refBox));

            _dbmsBox.Items.Clear();
            foreach (var v in DbmsValues)
                _dbmsBox.Items.Add(new ComboBoxItem { Content = v });
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbmsLabel"), _dbmsBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbServerLabel"), _dbServerBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbNameLabel"), _dbNameBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbUserLabel"), _dbUserBox));
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbPasswordLabel"), _dbPwdBox));

            var createDbRow = new Grid();
            createDbRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            createDbRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            // Issue #61: подписи в форме привязываются к FormLabelBrush — не зависят от
            // глобального стиля TextBlock, который может не дойти до программного дерева.
            var cdLabel = new TextBlock { Text = LocalizationManager.T("CreateInfobase.CreateDatabase"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(cdLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(cdLabel, 1);
            createDbRow.Children.Add(cdLabel);
            _createDbCheck.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_createDbCheck, 0);
            createDbRow.Children.Add(_createDbCheck);
            fields.Children.Add(createDbRow);

            var blockJobsRow = new Grid();
            blockJobsRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            blockJobsRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var bjLabel = new TextBlock { Text = LocalizationManager.T("CreateInfobase.BlockScheduledJobs"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(bjLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(bjLabel, 1);
            blockJobsRow.Children.Add(bjLabel);
            _blockJobsCheck.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_blockJobsCheck, 0);
            blockJobsRow.Children.Add(_blockJobsCheck);
            fields.Children.Add(blockJobsRow);

            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.PlatformVersionLabel"), _platformBox));

            // Группа в списке программы (по умолчанию — группа источника).
            var groupRow = new Grid();
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var gl = new TextBlock { Text = LocalizationManager.T("CreateInfobase.GroupLabel"), VerticalAlignment = VerticalAlignment.Center };
            Themes.ThemeBrushes.Bind(gl, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(gl, 0);
            groupRow.Children.Add(gl);
            Grid.SetColumn(_groupPathBox, 1);
            groupRow.Children.Add(_groupPathBox);
            var pickGroup = new Button { Content = LocalizationManager.T("CreateInfobase.ChooseGroup"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickGroup.Styled(ControlThemes.SecondaryButton);
            pickGroup.Padding = new Thickness(10, 4);
            pickGroup.Click += (_, _) => OnPickGroup_Click();
            Grid.SetColumn(pickGroup, 2);
            groupRow.Children.Add(pickGroup);
            fields.Children.Add(groupRow);

            fields.Children.Add(_hintText);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = fields
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cloneCaption = new TextBlock
            {
                Text = LocalizationManager.T("CloneServer.Create"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var clone = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    // Значок на акцентной кнопке — контрастный TextOnAccentBrush (issue #26).
                    Children = { IconHelper.MakeIcon("IconCopy", 16, "TextOnAccentBrush"), cloneCaption }
                },
                Width = 150,
                Height = 36,
                IsDefault = true
            };
            clone.Styled(ControlThemes.DialogConfirmButton);
            RegisterConfirmCaption(cloneCaption, "CloneServer.Create");
            clone.Click += async (_, _) => await OnClone_ClickAsync();
            buttons.Children.Add(clone);
            buttons.Children.Add(BuildCancelActionButton(130));

            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
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

        private void UpdateModeHint()
        {
            // Issue #70: WPF-аналог падал NullReferenceException, когда Checked-событие
            // RadioButton срабатывало до создания TextBlock подсказки; здесь поле создаётся
            // сразу, но guard сохраняет устойчивость к порядку инициализации дерева.
            if (_hintText is null)
                return;
            var full = _modeFullRadio.IsChecked == true;
            _hintText.Text = LocalizationManager.T(full
                ? "CloneServer.ModeFullHint"
                : "CloneServer.ModeConfigHint");
        }

        private ServerCloneRequest BuildRequest()
            => new()
            {
                Source = _source,
                CloneName = _nameBox.Text?.Trim() ?? "",
                Mode = _modeFullRadio.IsChecked == true
                    ? ServerCloneMode.FullCopy
                    : ServerCloneMode.ConfigurationOnly,
                PlatformVersion = _platformBox.Text?.Trim() ?? "",
                Server = _serverBox.Text?.Trim() ?? "",
                DatabaseName = _refBox.Text?.Trim() ?? "",
                Dbms = _dbmsBox.Text?.Trim() ?? "",
                DbServer = _dbServerBox.Text?.Trim() ?? "",
                DbName = _dbNameBox.Text?.Trim() ?? "",
                DbUser = _dbUserBox.Text?.Trim() ?? "",
                DbPassword = _dbPwdBox.Password ?? "",
                CreateSqlDatabase = _createDbCheck.IsChecked == true,
                BlockScheduledJobs = _blockJobsCheck.IsChecked == true,
                GroupPath = _selectedGroupPath
            };

        private async Task OnClone_ClickAsync()
        {
            var request = BuildRequest();

            // Чистая валидация планировщиком (зеркалит CreateInfobaseService).
            switch (ServerClonePlanner.Validate(request))
            {
                case ServerCloneValidationError.EnterName:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterName"), LocalizationManager.T("CloneServer.Title"));
                    return;
                case ServerCloneValidationError.NoPlatform:
                    _dialogs.ShowWarning(LocalizationManager.T("CloneServer.ErrNoPlatform"), LocalizationManager.T("CloneServer.Title"));
                    return;
                case ServerCloneValidationError.EnterServerAndRef:
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterServerAndDb"), LocalizationManager.T("CloneServer.Title"));
                    return;
                case ServerCloneValidationError.MissingDbmsDetails:
                    _dialogs.ShowWarning(LocalizationManager.T("CloneServer.ErrEnterDbmsDetails"), LocalizationManager.T("CloneServer.Title"));
                    return;
            }

            // Окно прогресса (без кнопки «Отмена»): этапы обновляются из фонового потока.
            var progress = new CloneServerProgressWindow();
            progress.SetStage(LocalizationManager.T("CloneServer.StageDump"));
            progress.Show();
            try
            {
                var service = new ServerCloneService();
                var progressAdapter = new Progress<string>(progress.SetStage);
                var connection = await Task.Run(() => service.CloneAsync(request, progressAdapter));

                Request = request;
                ResultConnection = connection;
                progress.Close();
                Close();
            }
            catch (Exception ex)
            {
                progress.Close();
                var message = ex is ServerCloneException sce
                    ? sce.Message
                    : ex.Message;
                if (ex is ServerCloneException { InfobasePossiblyCreated: true })
                    message += "\n\n" + LocalizationManager.T("CloneServer.PartialCreatedWarning");
                _dialogs.ShowError(message, LocalizationManager.T("CloneServer.Title"));
            }
        }
    }
}
#endif