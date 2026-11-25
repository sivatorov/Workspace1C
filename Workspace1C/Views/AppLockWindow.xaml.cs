#if WINDOWS
using System;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно временной блокировки приложения паролем (функция №19 StartManager).
    /// Работает в трёх режимах:
    ///  — «установить пароль» (когда пароль ещё не задан): два поля ввода пароля;
    ///  — «разблокировать» (когда пароль уже задан): одно поле для снятия блокировки;
    ///  — «сменить пароль» (issue #294): текущий пароль, новый и его повтор.
    /// Пароль сохраняется в виде PBKDF2-хэша в настройках.
    /// </summary>
    public partial class AppLockWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly bool _setupMode;
        private readonly bool _changeMode;

        /// <summary>true, если блокировка снята (введён верный пароль).</summary>
        public bool Unlocked { get; private set; }

        /// <summary>
        /// Введён верный пароль — владелец снимает блокировку (issue #294).
        /// Закрытие окна без пароля блокировку НЕ снимает: интерфейс остаётся
        /// закрытым оверлеем и окно ввода можно открыть повторно.
        /// </summary>
        public event EventHandler? UnlockSucceeded;

        public AppLockWindow(MainViewModel vm, bool setupMode = false, bool changeMode = false)
        {
            InitializeComponent();
            _vm = vm;
            _setupMode = setupMode;
            _changeMode = changeMode;

            // Issue #164 (1.0.1.121): окно растягивается по содержимому
            // (SizeToContent="Height"), фиксированные высоты 340/430 давали пустое
            // место под кнопками (жалоба 7OH). Границы берутся из общего профиля и
            // клампятся по рабочей области монитора.
            ApplyProfileSize(changeMode
                ? DialogWorkAreaPolicy.AppLockChange
                : DialogWorkAreaPolicy.AppLock);

            if (changeMode)
            {
                // Смена пароля (issue #294): текущий пароль + новый + повтор.
                // Окно выше обычного — в нём три поля ввода (профиль AppLockChange).
                Title = LocalizationManager.T("AppLock.ChangeTitle");
                TitleLabel.Text = LocalizationManager.T("AppLock.ChangeTitle");
                PromptLabel.Text = LocalizationManager.T("AppLock.ChangePrompt");
                OkTextBlock.Text = LocalizationManager.T("Common.Save");
                CancelTextBlock.Text = LocalizationManager.T("Common.Cancel");
                CurrentPasswordLabel.Visibility = Visibility.Visible;
                PasswordBox0.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
                ConfirmLabel.Visibility = Visibility.Visible;
            }
            else if (setupMode)
            {
                Title = LocalizationManager.T("AppLock.SetupTitle");
                TitleLabel.Text = LocalizationManager.T("AppLock.SetupTitle");
                PromptLabel.Text = LocalizationManager.T("AppLock.SetupPrompt");
                OkTextBlock.Text = LocalizationManager.T("Common.Save");
                CancelTextBlock.Text = LocalizationManager.T("Common.Cancel");
                NewPasswordLabel.Text = LocalizationManager.T("AppLock.NewPassword");
                ConfirmLabel.Text = LocalizationManager.T("AppLock.ConfirmPassword");
                PasswordBox2.Visibility = Visibility.Visible;
                ConfirmLabel.Visibility = Visibility.Visible;
            }
            else
            {
                Title = LocalizationManager.T("AppLock.LockTitle");
                TitleLabel.Text = LocalizationManager.T("AppLock.LockTitle");
                PromptLabel.Text = LocalizationManager.T("AppLock.UnlockPrompt");
                OkTextBlock.Text = LocalizationManager.T("AppLock.Unlock");
                // Режим разблокировки: одно видимое поле с подписью «Пароль».
                NewPasswordLabel.Text = LocalizationManager.T("AppLock.Password");
                NewPasswordLabel.Visibility = Visibility.Visible;
                PasswordBox1.Visibility = Visibility.Visible;
                ConfirmLabel.Visibility = Visibility.Collapsed;
                PasswordBox2.Visibility = Visibility.Collapsed;
                // «Отмена» снова доступна: закрытие окна не снимает блокировку,
                // интерфейс остаётся закрытым оверлеем до верного пароля (issue #294).
                CancelButton.Visibility = Visibility.Visible;
            }

            // В режиме разблокировки окно закрываемое: снять блокировку можно только
            // верным паролем, но само окно не мешает видеть заблокированный интерфейс,
            // и его можно временно убрать — блокировка при этом остаётся (issue #294).

            Loaded += (_, _) =>
            {
                // Фокус на первое (видимое) поле пароля: при установке — «Новый пароль»,
                // при разблокировке — единственное поле ввода, при смене — «Текущий
                // пароль». Пост с низким приоритетом страхует фокус, снятый активацией
                // окна при показе (issue #294).
                var first = _changeMode ? PasswordBox0 : PasswordBox1;
                first.Focus();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => first.Focus()));
            };
        }

        /// <summary>
        /// Issue #164 (1.0.1.121): окно блокировки растягивается по содержимому
        /// (<c>SizeToContent="Height"</c>), поэтому вместо фиксированной высоты
        /// 340/430 DIP задаются только границы — из общего профиля
        /// <see cref="DialogWorkAreaPolicy"/> с клампом по рабочей области монитора.
        /// Геометрия — вспомогательная возможность: её сбой не должен мешать
        /// блокировке/разблокировке, поэтому используется безопасный запасной вариант.
        /// </summary>
        private void ApplyProfileSize(DialogWorkAreaPolicy.DialogProfile profile)
        {
            try
            {
                var size = DialogWorkAreaPolicy.Recommend(profile, SystemParameters.WorkArea.Height);
                MinHeight = size.MinHeight;
                MaxHeight = size.MaxHeight;
            }
            catch
            {
                // Не удалось определить рабочую область — берём границы профиля как есть.
                MinHeight = profile.MinHeight;
                MaxHeight = profile.MaxHeight;
            }
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_changeMode)
            {
                // Смена пароля (issue #294): текущий пароль проверяется, новый
                // не пустой и совпадает с повтором — только тогда сохраняется.
                var current = PasswordBox0.Password ?? string.Empty;
                var pwd = PasswordBox1.Password ?? string.Empty;
                var confirm = PasswordBox2.Password ?? string.Empty;
                if (!_vm.VerifyAppLockPassword(current))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.WrongPassword"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    PasswordBox0.Clear();
                    PasswordBox0.Focus();
                    return;
                }
                if (string.IsNullOrEmpty(pwd))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordEmpty"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordMismatch"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _vm.SetAppLockPassword(pwd);
                DialogResult = true;
                return;
            }

            if (_setupMode)
            {
                var pwd = PasswordBox1.Password ?? string.Empty;
                var confirm = PasswordBox2.Password ?? string.Empty;
                if (string.IsNullOrEmpty(pwd))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordEmpty"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordMismatch"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _vm.SetAppLockPassword(pwd);
                DialogResult = true;
                return;
            }

            var entered = PasswordBox1.Password ?? string.Empty;
            if (_vm.VerifyAppLockPassword(entered))
            {
                Unlocked = true;
                UnlockSucceeded?.Invoke(this, EventArgs.Empty);
                // Окно разблокировки показывается немодально (Show), поэтому DialogResult
                // нельзя трогать: WPF бросает InvalidOperationException для немодальных
                // окон. Закрываем окно обычным Close() (issue #294).
                Close();
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("AppLock.WrongPassword"),
                    Title, MessageBoxButton.OK, MessageBoxImage.Error);
                PasswordBox1.Clear();
                PasswordBox1.Focus();
            }
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void OnPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif