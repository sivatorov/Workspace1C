#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Окно выбора учётной записи (профиля) по аналогии со списком пользователей 1С.
    /// Показывается при запуске, если в приложении создано несколько профилей.
    /// Для профиля с паролем запрашивается пароль; для незащищённого — вход сразу.
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly IProfileService _profileService;

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

            // На старте главного окна ещё нет, и первым MainWindow приложения
            // становится сам LoginWindow: присваивание Owner самому себе бросает
            // ArgumentException «Невозможно указать себя в свойстве Owner»,
            // и приложение не запускается вовсе.
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !ReferenceEquals(owner, window))
                window.Owner = owner;

            window.ShowDialog();
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
                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                var settings = repository.LoadSettings();
                var mergedScheme = Models.ColorScheme.FromLegacy(
                    settings.ActiveColorScheme, settings.LightColorScheme, settings.DarkColorScheme);
                // Полноценная тема (FullTheme с миграцией из устаревшего Theme), поверх
                // неё — пользовательская цветовая схема (порядок: сначала тема, затем схема).
                var fullTheme = settings.NormalizeThemeSelection();
                Themes.ThemeManager.SetOverrides(settings.IconSetOverride, settings.WindowButtonStyleOverride);
                Themes.ThemeManager.ApplyFullTheme(fullTheme);
                // Сохранённый базовый вариант (Theme="Dark") применяем поверх полноценной
                // темы: тумблер «Светлая/Тёмная» не меняет FullTheme, и без этого выбора
                // вариант терялся при перезапуске (в т.ч. после автообновления). Вариант
                // задаётся до ApplyScheme — тот выбирает палитру по CurrentTheme.
                if (Themes.ThemeFeatureMap.ResolveDarkVariant(settings.Theme) is bool darkVariant)
                    Themes.ThemeManager.ApplyTheme(darkVariant);
                // Оптимизация старта (аудит производительности): ApplyFullTheme/ApplyTheme
                // уже наложили палитру — идентичную схему повторно не накладываем
                // (иначе все DynamicResource-потребители перерисовывались дважды).
                if (!Themes.ThemeManager.CurrentScheme.SamePaletteAs(mergedScheme))
                    Themes.ThemeManager.ApplyScheme(mergedScheme);
            }
            catch (Exception ex)
            {
                // Тема не должна блокировать вход: без неё используется тема по умолчанию,
                // но причина фиксируется в журнале (диагностика темы при входе).
                try { AppServices.GetRequiredService<IAppLogger>().Warn($"LoginWindow: не удалось применить тему при старте: {ex.Message}"); }
                catch { /* журнал недоступен — не блокируем вход */ }
            }
        }

        public LoginWindow(IProfileService profileService)
        {
            InitializeComponent();
            _profileService = profileService;
            ProfilesList.ItemsSource = profileService.Profiles;

            // Если защищённых нет, выбираем первую запись по умолчанию.
            if (ProfilesList.Items.Count > 0)
                ProfilesList.SelectedIndex = 0;

            Loaded += (_, _) => ApplySelectionState();
        }

        private UserProfile? SelectedProfile => ProfilesList.SelectedItem as UserProfile;

        private void OnProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            ApplySelectionState();
        }

        /// <summary>Показывает поле пароля только для защищённого выбранного профиля.</summary>
        private void ApplySelectionState()
        {
            var profile = SelectedProfile;
            var hasPassword = profile?.HasPassword == true;
            PasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
            if (hasPassword)
                PasswordInput.Focus();
        }

        private void OnPasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
                e.Handled = true;
            }
        }

        private void OnLogin_Click(object sender, RoutedEventArgs e)
        {
            TryLogin();
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

                if (profile.HasPassword && !_profileService.VerifyPassword(profile.Id, PasswordInput.Password))
                {
                    ShowError(LocalizationManager.T("Auth.WrongPassword"));
                    PasswordInput.Clear();
                    PasswordInput.Focus();
                    return;
                }

                SelectedProfileId = profile.Id;
                // Вход с паролем защищённого профиля автоматически разблокирует
                // приватные базы (0.3.9.85). Флаг взводит вызывающий код ПОСЛЕ
                // SetCurrentProfile, который сбрасывает его для нового профиля.
                PasswordEntered = profile.HasPassword;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LocalizationManager.T("Auth.LoginError"), ex.Message));
            }
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }
}
#endif