#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Модальный редактор учётной записи ИТС (issue #333): наименование, логин и пароль.
    /// Скининг — по теме приложения (через <see cref="ModalWindowBase"/>), фокус по умолчанию
    /// на первом поле («Наименование»). Работает на копии данных: результат возвращается
    /// через <see cref="Result"/> при подтверждении; флаг «Основная» из редактора не меняется
    /// (смена основной — только через кнопку «Задать основным» окна списка).
    /// Avalonia/Linux-версия WPF-окна <see cref="ItsAccountEditWindow"/>.
    /// </summary>
    public sealed class ItsAccountEditWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly string _originalId;

        // Поля со стилем ModernTextBox — акцентный контур при фокусе из темы (issue #51).
        private readonly TextBox _nameBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _loginBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly PasswordBox _passwordBox = new PasswordBox { Height = 32 }.Styled(ControlThemes.ModernPasswordBox);

        /// <summary>Готовая учётная запись при подтверждении, иначе <c>null</c>.</summary>
        public ItsAccount? Result { get; private set; }

        /// <param name="model">Редактируемая запись или <c>null</c> для новой.</param>
        public ItsAccountEditWindow(ItsAccount? model = null)
        {
            _originalId = model?.Id ?? string.Empty;
            var isNew = model is null;

            Title = T(isNew ? "ItsAccounts.AddTitle" : "ItsAccounts.EditTitle");
            Width = 460;
            Height = 360;
            MinWidth = 420;
            MinHeight = 320;
            FontSize = 13;
            CanResize = true;

            _nameBox.Text = model?.Name ?? string.Empty;
            _loginBox.Text = model?.Login ?? string.Empty;
            _passwordBox.Password = model?.Password ?? string.Empty;

            Content = BuildRoot();

            // Фокус в первом поле (наименование): отложенно после показа окна — синхронная
            // установка в Opened слетает до активации модального диалога (паттерн #299).
            Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _nameBox.Focus();
                    if (isNew)
                        _nameBox.SelectAll();
                }, DispatcherPriority.Background);
            };
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

            var title = new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            panel.Children.Add(title);

            panel.Children.Add(MakeFieldRow(T("ItsAccounts.Name"), _nameBox));
            panel.Children.Add(MakeFieldRow(T("ItsAccounts.Login"), _loginBox));

            // Просмотр пароля «глазом» (issue #333): кнопка справа от поля; наше поле —
            // TextBox с маской, поэтому показать/скрыть пароль можно сменой символа маски
            // (паттерн окна свойств базы, issue #169/#211).
            var revealed = false;
            var revealButton = new Button
            {
                Content = IconHelper.MakeIcon("IconEye", 16),
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            revealButton.Styled(ControlThemes.IconButton);
            ToolTip.SetTip(revealButton, LocalizationManager.T("Connection.ShowPasswordTooltip"));
            revealButton.Click += (_, _) =>
            {
                revealed = !revealed;
                _passwordBox.PasswordChar = revealed ? '\0' : '•';
            };

            var passwordGrid = new Grid();
            passwordGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            passwordGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            _passwordBox.Margin = new Thickness(0);
            Grid.SetColumn(_passwordBox, 0);
            passwordGrid.Children.Add(_passwordBox);
            Grid.SetColumn(revealButton, 1);
            revealButton.Margin = new Thickness(6, 0, 0, 0);
            passwordGrid.Children.Add(revealButton);

            panel.Children.Add(MakeFieldRow(T("ItsAccounts.Password"), passwordGrid));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var save = new Button { Content = T("Common.Save"), Width = 130, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSaveClick();
            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            return panel;
        }

        private static Control MakeFieldRow(string label, Control editor)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(140, GridUnitType.Pixel)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Margin = new Thickness(0, 4, 0, 4);

            var caption = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private void OnSaveClick()
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _dialogs.ShowWarning(T("ItsAccounts.NameRequired"), T("ItsAccounts.EditTitle"));
                _nameBox.Focus();
                return;
            }

            Result = new ItsAccount
            {
                Id = _originalId,
                Name = name,
                Login = _loginBox.Text?.Trim() ?? string.Empty,
                Password = _passwordBox.Password ?? string.Empty,
                // Флаг «Основная» не меняется редактором: хранилище сохраняет его при правке.
            };
            DialogResult = true;
            Close();
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
        }
    }
}
#endif