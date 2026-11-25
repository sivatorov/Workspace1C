#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Запрос пароля учётной записи для доступа к ключу шифрования сохранённых паролей
    /// (issue #163, план 1.0.1.120, T3). Avalonia/Linux.
    ///
    /// Открывается, когда шифрование включено, а сеансового ключа нет (после перезапуска,
    /// смены записи или фонового обновления). Неверный пароль не закрывает окно и не
    /// меняет данные — можно повторить ввод или отменить действие.
    /// </summary>
    public sealed class SecretUnlockWindow : ModalWindowBase
    {
        private readonly IProfileService _profileService =
            AppServices.GetRequiredService<IProfileService>();

        private readonly string _profileId;
        private readonly Controls.PasswordBox _password =
            new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);

        private readonly TextBlock _error = new()
        {
            IsVisible = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        /// <summary>true, если пароль верен и сеансовый ключ установлен.</summary>
        public bool Unlocked { get; private set; }

        /// <param name="profileName">Имя учётной записи для подсказки в окне.</param>
        public SecretUnlockWindow(string profileName)
        {
            _profileId = _profileService.CurrentProfile?.Id ?? string.Empty;

            Title = LocalizationManager.T("Secrets.UnlockTitle");
            // Issue #163: узкое окно запроса пароля (паритет с WPF 440/520).
            Width = 440;
            Height = 270;
            MinWidth = 400;
            MaxWidth = 520;
            MinHeight = 230;
            CanResize = false;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            root.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationManager.T("Secrets.UnlockPrompt"), profileName),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock { Text = LocalizationManager.T("AppLock.Password") });
            _password.KeyDown += OnPasswordKeyDown;
            root.Children.Add(_password);

            _error.Foreground = ThemeBrushes.Get("ErrorBrush", "#DC2626");
            root.Children.Add(_error);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var ok = new Button { Content = LocalizationManager.T("Secrets.Unlock"), MinWidth = 150 };
            ok.Styled(ControlThemes.ModernButton);
            ok.Click += (_, _) => Submit();
            buttons.Children.Add(ok);

            var cancel = new Button { Content = LocalizationManager.T("Common.Cancel"), MinWidth = 110 };
            cancel.Styled(ControlThemes.ModernButton);
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);

            root.Children.Add(buttons);
            Content = root;

            Opened += (_, _) =>
            {
                _password.Focus();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _password.Focus());
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
            _error.IsVisible = true;
        }

        private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            Submit();
            e.Handled = true;
        }
    }
}
#endif
