#if WINDOWS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Дублировать серверную базу» (0.3.9.100, функция №10): имя клона,
    /// целевой сервер 1С / имя базы на сервере (Ref), СУБД и учётные данные,
    /// группа, версия платформы (из источника) и режим копирования
    /// (полная копия / только конфигурация). Выполнение — через
    /// <see cref="ServerCloneService"/> с окном прогресса
    /// <see cref="CloneServerProgressWindow"/>; по успеху возвращает запрос
    /// и подключение клона, запись в список собирает вызывающий.
    /// </summary>
    public partial class CloneServerInfobaseWindow : Window
    {
        private readonly Infobase _source;
        private readonly IReadOnlyList<Group> _groups;
        private string _selectedGroupPath;
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();

        /// <summary>Запрос клонирования (заполняется при успешном выполнении).</summary>
        public ServerCloneRequest? Request { get; private set; }

        /// <summary>Подключение созданного клона (заполняется при успешном выполнении).</summary>
        public ConnectionSettings? ResultConnection { get; private set; }

        public CloneServerInfobaseWindow(
            Infobase source,
            IEnumerable<Group> groups,
            IEnumerable<string> existingRefNames)
        {
            _source = source;
            _groups = groups?.ToList() ?? new List<Group>();
            _selectedGroupPath = source.Group ?? string.Empty;
            InitializeComponent();

            Title = LocalizationManager.T("CloneServer.Title");

            // Префилл: имя клона («— Копия»), сервер с портом, Ref с уникализацией
            // по локально известным базам на том же сервере, флаг блокировки заданий.
            NameBox.Text = ServerClonePlanner.ProposeCloneName(source.Name);
            ServerBox.Text = source.Connection.GetServerWithPort();
            RefBox.Text = ServerClonePlanner.SuggestRefName(source.Connection.DatabaseName, existingRefNames);
            BlockJobsCheck.IsChecked = source.Connection.BlockScheduledJobs;
            PlatformBox.Text = BuildPlatformDisplay(source);

            GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            UpdateModeHint();
            ApplyWorkAreaLimits();
        }

        /// <summary>
        /// Высота окна не должна выходить за рабочую область монитора: на экранах
        /// 768 px с масштабированием фиксированные 640 DIP почти не помещались
        /// (форма длинная — 14 полей). Ограничение общее с другими диалогами
        /// (<see cref="DialogWorkAreaPolicy"/>), содержимое прокручивается.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var workArea = SystemParameters.WorkArea;
            MaxHeight = DialogWorkAreaPolicy.MaxHeight(workArea.Height);
            MinHeight = DialogWorkAreaPolicy.ClampMinHeight(MinHeight, workArea.Height);
            Height = DialogWorkAreaPolicy.ClampHeight(Height, MinHeight, workArea.Height);
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

        private void OnPickGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: null,
                allowNone: true,
                noneLabel: LocalizationManager.T("Connection.NoGroup"),
                kind: GroupPickerObjectKind.Infobase)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? LocalizationManager.T("Connection.NoGroup")
                    : _selectedGroupPath;
            }
        }

        /// <summary>Переключение режима копирования обновляет подсказку.</summary>
        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateModeHint();
        }

        private void UpdateModeHint()
        {
            // Issue #70: RadioButton ModeFullRadio в XAML объявлен с IsChecked="True" и
            // Checked="OnModeChanged"; во время InitializeComponent событие Checked может
            // сработать раньше, чем будет создан TextBlock HintText (объявлен ниже по
            // разметке), что приводило к NullReferenceException при открытии окна.
            if (HintText is null)
                return;
            var full = ModeFullRadio.IsChecked == true;
            HintText.Text = LocalizationManager.T(full
                ? "CloneServer.ModeFullHint"
                : "CloneServer.ModeConfigHint");
        }

        private ServerCloneRequest BuildRequest()
            => new()
            {
                Source = _source,
                CloneName = NameBox.Text?.Trim() ?? "",
                Mode = ModeFullRadio.IsChecked == true
                    ? ServerCloneMode.FullCopy
                    : ServerCloneMode.ConfigurationOnly,
                PlatformVersion = PlatformBox.Text?.Trim() ?? "",
                Server = ServerBox.Text?.Trim() ?? "",
                DatabaseName = RefBox.Text?.Trim() ?? "",
                Dbms = DbmsBox.Text?.Trim() ?? "",
                DbServer = DbServerBox.Text?.Trim() ?? "",
                DbName = DbNameBox.Text?.Trim() ?? "",
                DbUser = DbUserBox.Text?.Trim() ?? "",
                DbPassword = DbPwdBox.Password ?? "",
                CreateSqlDatabase = CreateDbCheck.IsChecked == true,
                BlockScheduledJobs = BlockJobsCheck.IsChecked == true,
                GroupPath = _selectedGroupPath
            };

        private async void OnClone_Click(object sender, RoutedEventArgs e)
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
            var progress = new CloneServerProgressWindow { Owner = this };
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
                DialogResult = true;
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