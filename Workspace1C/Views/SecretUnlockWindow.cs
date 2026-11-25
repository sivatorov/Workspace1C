#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Запрос пароля учётной записи для доступа к ключу шифрования сохранённых паролей
    /// (issue #163, план 1.0.1.120, T3). Windows/WPF.
    ///
    /// Окно открывается, когда шифрование включено, а сеансового ключа нет (после
    /// перезапуска, смены записи или фонового обновления). Неверный пароль не закрывает
    /// окно и не меняет данные — пользователь может повторить ввод или отменить действие.
    /// </summary>
    public sealed class SecretUnlockWindow : Window
    {
        private readonly IProfileService _profileService =
            AppServices.GetRequiredService<IProfileService>();

        private readonly string _profileId;
        private readonly PasswordBox _password = new() { Margin = new Thickness(0, 4, 0, 8) };
        private readonly TextBlock _error;

        /// <summary>true, если пароль верен и сеансовый ключ установлен.</summary>
        public bool Unlocked { get; private set; }

        /// <param name="profileName">Имя учётной записи для подсказки в окне.</param>
        public SecretUnlockWindow(string profileName)
        {
            _profileId = _profileService.CurrentProfile?.Id ?? string.Empty;
            WpfUi.SetupWindow(this, "Secrets.UnlockTitle", 480, 260);
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(14) };

            var prompt = WpfUi.Text(
                string.Format(LocalizationManager.T("Secrets.UnlockPrompt"), profileName), 13);
            prompt.Margin = new Thickness(0, 0, 0, 10);
            root.Children.Add(prompt);

            root.Children.Add(WpfUi.FieldLabel(LocalizationManager.T("AppLock.Password")));
            _password.KeyDown += OnPasswordKeyDown;
            root.Children.Add(_password);

            _error = WpfUi.Text(string.Empty, 12);
            _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            _error.Visibility = Visibility.Collapsed;
            root.Children.Add(_error);

            var ok = WpfUi.Button("Secrets.Unlock", (_, _) => Submit(), 150, accent: true);
            ok.IsDefault = true;
            var cancel = WpfUi.Button("Common.Cancel", (_, _) =>
            {
                Unlocked = false;
                DialogResult = false;
            }, 110);
            cancel.IsCancel = true;

            var row = WpfUi.ButtonRow(ok, cancel);
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(row);

            Content = root;

            Loaded += (_, _) =>
            {
                _password.Focus();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => _password.Focus()));
            };
        }

        private void Submit()
        {
            var entered = _password.Password ?? string.Empty;
            if (string.IsNullOrEmpty(entered))
            {
                ShowError(LocalizationManager.T("Secrets.PasswordRequired"));
                return;
            }

            if (_profileService.TryUnlockPasswordSession(_profileId, entered))
            {
                Unlocked = true;
                DialogResult = true;
                Close();
                return;
            }

            // Неверный пароль: данные не меняются, окно остаётся открытым.
            ShowError(LocalizationManager.T("Secrets.WrongPassword"));
            _password.Clear();
            _password.Focus();
        }

        private void ShowError(string message)
        {
            _error.Text = message;
            _error.Visibility = Visibility.Visible;
        }

        private void OnPasswordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            Submit();
            e.Handled = true;
        }
    }
}
#endif
