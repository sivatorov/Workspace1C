#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Окно разблокировки приватных баз (0.3.9.85), Avalonia/Linux-версия WPF-окна
    /// <see cref="PrivateBasesUnlockWindow"/>. Режимы: «разблокировать» (одно поле —
    /// пароль профиля) и «задать пароль» (два поля — новый пароль и повтор).
    /// Разблокировка действует на текущую сессию.
    /// </summary>
    public sealed class PrivateBasesUnlockWindow : ModalWindowBase
    {
        private readonly bool _setupMode;
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();

        private readonly Controls.PasswordBox _pwd1 = new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);
        private readonly Controls.PasswordBox _pwd2 = new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);
        private readonly TextBlock _pwd1Label = new();
        private readonly TextBlock _pwd2Label = new();

        /// <summary>Режим «задать пароль»: true, если пароль профиля успешно установлен.</summary>
        public bool PasswordSet { get; private set; }

        /// <summary>Режим «разблокировать»: true, если введён верный пароль профиля.</summary>
        public bool Unlocked { get; private set; }

        /// <param name="setupMode">
        /// true — у профиля нет пароля: окно предлагает задать пароль (два поля);
        /// false — окно запрашивает пароль профиля (одно поле).
        /// </param>
        public PrivateBasesUnlockWindow(bool setupMode = false)
        {
            _setupMode = setupMode;

            Width = 460;
            Height = setupMode ? 340 : 300;
            MinWidth = 440;
            MinHeight = setupMode ? 320 : 280;
            FontSize = 13;
            CanResize = false;
            Content = BuildRoot();

            if (setupMode)
            {
                Title = T("Private.SetupTitle");
                TitleLabel.Text = T("Private.SetupTitle");
                PromptLabel.Text = T("Private.SetupPrompt");
                OkText.Text = T("Common.Save");
                _pwd1Label.Text = T("AppLock.NewPassword");
                _pwd2Label.Text = T("AppLock.ConfirmPassword");
                _pwd2.IsVisible = true;
                _pwd2Label.IsVisible = true;
            }
            else
            {
                Title = T("Private.UnlockTitle");
                TitleLabel.Text = T("Private.UnlockTitle");
                PromptLabel.Text = T("Private.UnlockPrompt");
                OkText.Text = T("Private.Unlock");
                _pwd1Label.Text = T("AppLock.Password");
                _pwd2.IsVisible = false;
                _pwd2Label.IsVisible = false;
            }

            Opened += (_, _) =>
            {
                _pwd1.Focus();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _pwd1.Focus());
            };
        }

        private static string T(string key) => LocalizationManager.T(key);

        private TextBlock TitleLabel { get; } = new()
        {
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        private TextBlock PromptLabel { get; } = new()
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        private TextBlock OkText { get; } = new() { Text = T("Common.Ok") };
        private TextBlock CancelText { get; } = new() { Text = T("Common.Cancel") };

        private Control BuildRoot()
        {
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(TitleLabel);
            stack.Children.Add(PromptLabel);

            _pwd1Label.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(_pwd1Label);
            _pwd1.Margin = new Thickness(0, 0, 0, 8);
            _pwd1.KeyDown += OnPassword_KeyDown;
            stack.Children.Add(_pwd1);

            _pwd2Label.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(_pwd2Label);
            _pwd2.Margin = new Thickness(0, 0, 0, 4);
            _pwd2.KeyDown += OnPassword_KeyDown;
            stack.Children.Add(_pwd2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            // Issue #98 (v1.0.1.25): главная кнопка слева, «Отмена» — последней.
            var ok = new Button { Content = OkText, Width = 160, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += OnOk_Click;
            buttons.Children.Add(ok);

            var cancel = new Button { Content = CancelText, Width = 110 };
            cancel.Click += OnCancel_Click;
            buttons.Children.Add(cancel);

            stack.Children.Add(buttons);
            return stack;
        }

        private void OnOk_Click(object? sender, RoutedEventArgs e)
        {
            var profileService = AppServices.GetRequiredService<IProfileService>();

            if (_setupMode)
            {
                var pwd = _pwd1.Password ?? string.Empty;
                var confirm = _pwd2.Password ?? string.Empty;
                if (string.IsNullOrEmpty(pwd))
                {
                    _dialogs.ShowWarning(T("AppLock.PasswordEmpty"));
                    return;
                }
                if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                {
                    _dialogs.ShowWarning(T("AppLock.PasswordMismatch"));
                    return;
                }

                if (profileService.CurrentProfile is not { } current)
                {
                    _dialogs.ShowError(T("Private.NoProfile"));
                    return;
                }

                // Сохраняем пароль профиля (PBKDF2) и сразу разблокируем приватные базы.
                profileService.SetPassword(current.Id, pwd);
                profileService.MarkPrivateBasesUnlocked();
                PasswordSet = true;
                DialogResult = true;
                Close();
                return;
            }

            var entered = _pwd1.Password ?? string.Empty;
            if (profileService.UnlockPrivateBases(entered))
            {
                Unlocked = true;
                DialogResult = true;
                Close();
            }
            else
            {
                _dialogs.ShowWarning(T("Private.WrongPassword"));
                _pwd1.Clear();
                _pwd1.Focus();
            }
        }

        private void OnCancel_Click(object? sender, RoutedEventArgs e) => Close();

        private void OnPassword_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif