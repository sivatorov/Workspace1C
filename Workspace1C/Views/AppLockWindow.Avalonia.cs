#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно временной блокировки приложения паролем (Avalonia/Linux), функция №19 StartManager.
/// Режимы: «установить пароль» (два поля), «разблокировать» (одно поле)
/// и «сменить пароль» — текущий, новый и повтор (issue #294).
/// Пароль сохраняется в виде PBKDF2-хэша в настройках.
/// </summary>
public sealed class AppLockWindow : ModalWindowBase
{
    private readonly MainViewModel _vm;
    private readonly bool _setupMode;
    private readonly bool _changeMode;
    // Поля стилизуются как остальные поля ввода приложения (ModernPasswordBox):
    // карточный фон темы, скругление 8 и акцентный контур при фокусе (issue #294).
    private readonly Controls.PasswordBox _pwd0 = new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);
    private readonly Controls.PasswordBox _pwd1 = new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);
    private readonly Controls.PasswordBox _pwd2 = new Controls.PasswordBox().Styled(ControlThemes.ModernPasswordBox);
    private readonly TextBlock _pwd0Label = new();
    private readonly TextBlock _pwd1Label = new();
    private readonly TextBlock _pwd2Label = new();

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
        _vm = vm;
        _setupMode = setupMode;
        _changeMode = changeMode;

        Width = 460;
        Height = changeMode ? 430 : 340;
        MinWidth = 440;
        MinHeight = changeMode ? 400 : 320;
        FontSize = 13;
        Content = BuildRoot();

        if (changeMode)
        {
            // Смена пароля (issue #294): текущий пароль, новый, повтор.
            Title = T("AppLock.ChangeTitle");
            TitleLabel.Text = T("AppLock.ChangeTitle");
            PromptLabel.Text = T("AppLock.ChangePrompt");
            OkText.Text = T("Common.Save");
            CancelText.Text = T("Common.Cancel");
            _pwd0Label.Text = T("AppLock.CurrentPassword");
            _pwd0Label.IsVisible = true;
            _pwd0.IsVisible = true;
            _pwd2Label.IsVisible = true;
            _pwd2.IsVisible = true;
        }
        else if (setupMode)
        {
            Title = T("AppLock.SetupTitle");
            TitleLabel.Text = T("AppLock.SetupTitle");
            PromptLabel.Text = T("AppLock.SetupPrompt");
            OkText.Text = T("Common.Save");
            CancelText.Text = T("Common.Cancel");
        }
        else
        {
            Title = T("AppLock.LockTitle");
            TitleLabel.Text = T("AppLock.LockTitle");
            PromptLabel.Text = T("AppLock.UnlockPrompt");
            OkText.Text = T("AppLock.Unlock");
            // Режим разблокировки: одно видимое поле с подписью «Пароль».
            _pwd1Label.Text = T("AppLock.Password");
            _pwd1Label.IsVisible = true;
            _pwd1.IsVisible = true;
            _pwd2Label.IsVisible = false;
            _pwd2.IsVisible = false;
            // «Отмена» снова доступна: закрытие окна не снимает блокировку,
            // интерфейс остаётся закрытым оверлеем до верного пароля (issue #294).
            _cancelButton.IsVisible = true;
            UnlockField = _pwd1;
        }

        // В режиме разблокировки окно закрываемое: снять блокировку можно только
        // верным паролем, но само окно не мешает видеть заблокированный интерфейс,
        // и его можно временно убрать — блокировка при этом остаётся (issue #294).

        // Фокус на первое (видимое) поле пароля при открытии окна (issue #294):
        // в режиме установки — «Новый пароль», при разблокировке — единственное поле,
        // при смене — «Текущий пароль». Пост после показа страхует фокус, снятый
        // активацией окна (issue #294).
        Opened += (_, _) =>
        {
            var first = _changeMode ? _pwd0 : _pwd1;
            first.Focus();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => first.Focus());
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

    private Controls.PasswordBox? UnlockField;

    // Кнопка «Отмена» — поле класса, чтобы скрывать её в режиме разблокировки (issue #294).
    private readonly Button _cancelButton = new();

    private Control BuildRoot()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(TitleLabel);
        stack.Children.Add(PromptLabel);

        // Поле «Текущий пароль» видно только в режиме смены пароля (issue #294).
        _pwd0Label.Text = T("AppLock.CurrentPassword");
        _pwd0Label.Margin = new Thickness(0, 0, 0, 4);
        _pwd0Label.IsVisible = false;
        stack.Children.Add(_pwd0Label);
        _pwd0.Margin = new Thickness(0, 0, 0, 8);
        _pwd0.IsVisible = false;
        _pwd0.KeyDown += OnPassword_KeyDown;
        stack.Children.Add(_pwd0);

        _pwd1Label.Text = T("AppLock.NewPassword");
        _pwd1Label.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(_pwd1Label);
        _pwd1.Margin = new Thickness(0, 0, 0, 8);
        _pwd1.KeyDown += OnPassword_KeyDown;
        stack.Children.Add(_pwd1);

        _pwd2Label.Text = T("AppLock.ConfirmPassword");
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
        _cancelButton.Content = CancelText;
        _cancelButton.Width = 110;
        _cancelButton.Click += OnCancel_Click;
        // Issue #98 (v1.0.1.25): главная кнопка слева, «Отмена» — последней.
        var ok = new Button { Content = OkText, Width = 160, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += OnOk_Click;
        buttons.Children.Add(ok);
        buttons.Children.Add(_cancelButton);
        stack.Children.Add(buttons);

        return stack;
    }

    private void OnOk_Click(object? sender, RoutedEventArgs e)
    {
        if (_changeMode)
        {
            // Смена пароля (issue #294): текущий пароль проверяется, новый
            // не пустой и совпадает с повтором — только тогда сохраняется.
            var current = _pwd0.Password ?? string.Empty;
            var pwd = _pwd1.Password ?? string.Empty;
            var confirm = _pwd2.Password ?? string.Empty;
            if (!_vm.VerifyAppLockPassword(current))
            {
                _vm.ShowWarning(T("AppLock.WrongPassword"));
                _pwd0.Clear();
                _pwd0.Focus();
                return;
            }
            if (string.IsNullOrEmpty(pwd))
            {
                _vm.ShowWarning(T("AppLock.PasswordEmpty"));
                return;
            }
            if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
            {
                _vm.ShowWarning(T("AppLock.PasswordMismatch"));
                return;
            }
            _vm.SetAppLockPassword(pwd);
            Close();
            return;
        }

        if (_setupMode)
        {
            var pwd = _pwd1.Password ?? string.Empty;
            var confirm = _pwd2.Password ?? string.Empty;
            if (string.IsNullOrEmpty(pwd))
            {
                _vm.ShowWarning(T("AppLock.PasswordEmpty"));
                return;
            }
            if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
            {
                _vm.ShowWarning(T("AppLock.PasswordMismatch"));
                return;
            }
            _vm.SetAppLockPassword(pwd);
            Close();
            return;
        }

        var field = UnlockField ?? _pwd1;
        var entered = field.Password ?? string.Empty;
        if (_vm.VerifyAppLockPassword(entered))
        {
            Unlocked = true;
            UnlockSucceeded?.Invoke(this, EventArgs.Empty);
            Close();
        }
        else
        {
            _vm.ShowWarning(T("AppLock.WrongPassword"));
            field.Clear();
            field.Focus();
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
#endif