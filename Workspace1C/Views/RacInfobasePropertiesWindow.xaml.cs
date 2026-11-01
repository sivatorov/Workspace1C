using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Свойства информационной базы» монитора «Серверы 1С» (issue #3, 7OH 17.10).
    /// Логика — в <see cref="RacInfobasePropertiesViewModel"/>; окно лишь связывает пароли
    /// (PasswordBox не поддерживает привязку) и при открытии сразу читает полные свойства.
    /// </summary>
    public partial class RacInfobasePropertiesWindow : Window
    {
        private readonly RacInfobasePropertiesViewModel _vm;

        /// <summary>Запрос rac «infobase update» при сохранении (null при отмене).</summary>
        public RacInfobaseUpdateRequest? Result { get; private set; }

        public RacInfobasePropertiesWindow(RacInfobasePropertiesViewModel vm)
        {
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3): ресайз
            // (ResizeMode по умолчанию CanResize) сохраняется, максимизация подавляется.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            _vm = vm;
            DataContext = vm;
            AdminPasswordBox.Password = vm.InfobasePassword;
            Loaded += async (_, _) => await _vm.RefreshAsync();
            // CenterOwner не ограничивает окно (820x660) рабочей областью — на малых экранах
            // часть окна уходила за экран (issue #3, 7OH 19.10): клампим после показа.
            ContentRendered += (_, _) => ClampToWorkArea();
        }

        /// <summary>
        /// Не даёт окну выйти за рабочую область экрана на малых экранах (issue #3):
        /// размер ужимается с запасом 16px, позиция клампится во все 4 стороны
        /// (проверенный чистый <see cref="WindowSizeMath"/>). Рабочая область — экран,
        /// на котором показано окно (по HWND).
        /// </summary>
        private void ClampToWorkArea()
        {
            try
            {
                if (!IsLoaded || !IsVisible)
                    return;

                var work = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
                const double spare = 16;
                var maxWidth = Math.Max(MinWidth, work.Width - spare);
                var maxHeight = Math.Max(MinHeight, work.Height - spare);
                if (Width > maxWidth)
                    Width = maxWidth;
                if (Height > maxHeight)
                    Height = maxHeight;
                Left = WindowSizeMath.FitLeft(Left, Width, work.Left, work.Right);
                Top = WindowSizeMath.FitTop(Top, Height, work.Top, work.Bottom);
            }
            catch
            {
                // Клампинг — вспомогательная возможность; сбой не должен мешать диалогу.
            }
        }

        private void OnAdminPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.InfobasePassword = AdminPasswordBox.Password;

        private void OnDbPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.DbPassword = DbPasswordBox.Password;

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            try
            {
                await _vm.RefreshAsync();
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка обновления свойств ИБ кластера", ex); } catch { /* лог не роняет обработчик */ }
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var request = _vm.BuildRequest(out var error);
            if (request is null)
            {
                MessageBox.Show(this, error ?? string.Empty,
                    LocalizationManager.T("ServerMonitor.Infobase.Props.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = request;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
