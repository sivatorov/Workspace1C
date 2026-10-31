#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Окно выбора учётной записи (профиля) по аналогии со списком пользователей 1С.
    /// Показывается при запуске, если в приложении создано несколько профилей.
    /// Для профиля с паролем запрашивается пароль; для незащищённого — вход сразу.
    /// </summary>
    public class LoginWindow : ModalWindowBase
    {
        private readonly IProfileService _profileService;
        private readonly ListBox _profilesList;
        private readonly StackPanel _passwordPanel;
        private readonly PasswordBox _passwordInput;
        private readonly TextBlock _errorLabel;

        /// <summary>Идентификатор выбранного профиля (null, если вход не выполнен).</summary>
        public string? SelectedProfileId { get; private set; }

        /// <summary>
        /// true, если вход выполнен с верным паролем защищённого профиля — приватные
        /// базы разблокируются автоматически (0.3.9.85). Для профиля без пароля false.
        /// </summary>
        public bool PasswordEntered { get; private set; }

        /// <summary>
        /// Показывает окно авторизации и, если пользователь вошёл, возвращает идентификатор
        /// выбранного профиля и признак входа с паролем; иначе — (null, false) — приложение
        /// завершает работу. При входе с паролем вызывающий код после
        /// <see cref="IProfileService.SetCurrentProfile"/> должен взвести флаг разблокировки
        /// приватных баз (MarkPrivateBasesUnlocked), так как SetCurrentProfile его сбрасывает.
        /// </summary>
        public static (string? ProfileId, bool PasswordEntered) ShowLogin(IProfileService profileService)
        {
            ApplyActiveTheme();

            var window = new LoginWindow(profileService);
            window.ShowDialogSync();
            return (window.SelectedProfileId, window.PasswordEntered);
        }

        /// <summary>
        /// Применяет сохранённую цветовую схему и вариант темы, чтобы окно входа выглядело так же,
        /// как остальные окна приложения. При запуске тема ещё не применена (она загружается позже,
        /// после выбора профиля), поэтому скиним её здесь (issue #200). При смене пользователя в
        /// работающем приложении повторное применение безвредно — схема уже актуальна.
        /// </summary>
        private static void ApplyActiveTheme()
        {
            try
            {
                var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
                var mergedScheme = Models.ColorScheme.FromLegacy(
                    settings.ActiveColorScheme, settings.LightColorScheme, settings.DarkColorScheme);
                // Полноценная тема (FullTheme с миграцией из устаревшего Theme), поверх
                // неё — пользовательская цветовая схема (порядок: сначала тема, затем схема).
                var fullTheme = settings.NormalizeThemeSelection();
                ThemeManager.SetOverrides(settings.IconSetOverride, settings.WindowButtonStyleOverride);
                ThemeManager.ApplyFullTheme(fullTheme);
                // Сохранённый базовый вариант (Theme="Dark") применяем поверх полноценной
                // темы: тумблер «Светлая/Тёмная» не меняет FullTheme, и без этого выбора
                // вариант терялся при перезапуске (в т.ч. после автообновления). Вариант
                // задаётся до ApplyScheme — тот выбирает палитру по CurrentTheme.
                if (ThemeFeatureMap.ResolveDarkVariant(settings.Theme) is bool darkVariant)
                    ThemeManager.ApplyTheme(darkVariant);
                ThemeManager.ApplyScheme(mergedScheme);
            }
            catch
            {
                // Тема не должна блокировать вход: без неё используется тема по умолчанию.
            }
        }

        public LoginWindow(IProfileService profileService)
        {
            _profileService = profileService;
            Title = LocalizationManager.T("Auth.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            Width = 460;
            Height = 360;
            MinWidth = 400;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var hint = new TextBlock
            {
                Text = LocalizationManager.T("Auth.SelectAccountHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 0.85
            };
            Grid.SetRow(hint, 0);

            _profilesList = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                ItemsSource = profileService.Profiles,
                DisplayMemberBinding = new Avalonia.Data.Binding("Name")
            };
            _profilesList.SelectionChanged += (_, _) => OnSelectionChanged();
            Grid.SetRow(_profilesList, 1);

            _passwordPanel = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 12),
                IsVisible = false
            };
            var passwordLabel = new TextBlock { Text = LocalizationManager.T("Auth.Password") };
            ThemeBrushes.Bind(passwordLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
            _passwordPanel.Children.Add(passwordLabel);
            _passwordInput = new PasswordBox();
            _passwordInput.Styled(ControlThemes.ModernPasswordBox);
            // Отступ из разметки (LoginWindow.xaml), общая тема даёт 10,6.
            _passwordInput.Padding = new Thickness(8, 6);
            _passwordInput.KeyDown += OnPasswordInput_KeyDown;
            _passwordPanel.Children.Add(_passwordInput);
            Grid.SetRow(_passwordPanel, 2);

            // Ошибка на семантической кисти темы (аудит UI, п. 2.2) вместо Brushes.IndianRed.
            _errorLabel = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                IsVisible = false
            };
            ThemeBrushes.Bind(_errorLabel, TextBlock.ForegroundProperty, "ErrorBrush");
            Grid.SetRow(_errorLabel, 3);

            var buttons = BuildLoginButtons();
            Grid.SetRow(buttons, 4);

            grid.Children.Add(hint);
            grid.Children.Add(_profilesList);
            grid.Children.Add(_passwordPanel);
            grid.Children.Add(_errorLabel);
            grid.Children.Add(buttons);

            Content = grid;

            Opened += (_, _) =>
            {
                if (_profilesList.Items.Count > 0)
                    _profilesList.SelectedIndex = 0;
                ApplySelectionState();
            };
        }

        private UserProfile? SelectedProfile =>
            _profilesList.SelectedItem as UserProfile;

        private void OnSelectionChanged()
        {
            _errorLabel.IsVisible = false;
            ApplySelectionState();
        }

        private void ApplySelectionState()
        {
            var hasPassword = SelectedProfile?.HasPassword == true;
            _passwordPanel.IsVisible = hasPassword;
            if (hasPassword)
            {
                _passwordInput.Focus();
                _passwordInput.Clear();
            }
        }

        private void OnPasswordInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
                e.Handled = true;
            }
        }

        private StackPanel BuildLoginButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            // Оформление по разметке (LoginWindow.xaml:62): обе кнопки в стиле
            // основной, у отмены заливка мягкая, у входа акцентная.
            var cancel = BuildCancelButton(110);
            panel.Children.Add(cancel);

            var loginCaption = new TextBlock
            {
                Text = LocalizationManager.T("Auth.Login"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var login = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { IconHelper.MakeIcon("IconOk", 14, "ButtonTextBrush"), loginCaption }
                },
                Width = 130,
                IsDefault = true
            };
            login.Styled(ControlThemes.ModernButton);
            RegisterConfirmCaption(loginCaption, "Auth.Login");
            login.Click += (_, _) => TryLogin();
            panel.Children.Add(login);

            return panel;
        }

        private void TryLogin()
        {
            // Любой сбой при входе (например, ошибка проверки пароля) показываем понятным
            // сообщением в самом окне, а не роняем приложение необработанным исключением.
            try
            {
                var profile = SelectedProfile;
                if (profile == null)
                {
                    ShowError(LocalizationManager.T("Auth.EmptySelection"));
                    return;
                }

                if (profile.HasPassword && !_profileService.VerifyPassword(profile.Id, _passwordInput.Password))
                {
                    ShowError(LocalizationManager.T("Auth.WrongPassword"));
                    _passwordInput.Clear();
                    _passwordInput.Focus();
                    return;
                }

                SelectedProfileId = profile.Id;
                // Вход с паролем защищённого профиля автоматически разблокирует
                // приватные базы (0.3.9.85). Флаг взводит вызывающий код ПОСЛЕ
                // SetCurrentProfile, который сбрасывает его для нового профиля.
                PasswordEntered = profile.HasPassword;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LocalizationManager.T("Auth.LoginError"), ex.Message));
            }
        }

        private void ShowError(string message)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
        }
    }
}
#endif