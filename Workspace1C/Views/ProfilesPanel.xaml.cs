#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Панель управления учётными записями (профилями), размещаемая во вкладке «Учётные записи»
    /// окна настроек вместо отдельного окна. Тонкая «view»: вся бизнес-логика (валидация, CRUD,
    /// построение списка, подтверждение удаления) вынесена в <see cref="ProfilesViewModel"/>.
    /// Здесь остаётся только передача пароля из <c>PasswordBox</c>, который не поддерживает
    /// двустороннюю привязку.
    /// </summary>
    public partial class ProfilesPanel : UserControl
    {
        public ProfilesPanel()
        {
            InitializeComponent();
            // DataContext задаётся снаружи (окно настроек), поэтому подключаем
            // запрос пароля при каждой смене контекста (issue #163, T3).
            DataContextChanged += (_, _) => AttachPasswordPrompt();
        }

        /// <summary>
        /// Пароль нельзя привязать через <c>{Binding}</c> (<c>PasswordBox.Password</c>
        /// не является DependencyProperty), поэтому синхронизируем его вручную в ViewModel.
        /// </summary>
        private void OnPassword_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfilesViewModel vm)
                vm.Password = PasswordInput.Password;
        }

        /// <summary>
        /// Подключает к ViewModel окно запроса пароля учётной записи: оно нужно,
        /// когда шифрование включено, а ключа в сеансе нет (после перезапуска/смены).
        /// </summary>
        private void AttachPasswordPrompt()
        {
            if (DataContext is not ProfilesViewModel vm)
                return;

            vm.PasswordPrompt = profileName =>
            {
                var dialog = new SecretUnlockWindow(profileName);
                var owner = Window.GetWindow(this);
                if (owner != null && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                return dialog.ShowDialog() == true && dialog.Unlocked;
            };
        }
    }
}
#endif