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
    /// Модальный редактор записи справочника «Сервера управления» (issue #3): описание,
    /// адрес и порт, логин и пароль. Скининг — по теме приложения (через
    /// <see cref="ModalWindowBase"/>), фокус по умолчанию на первом поле («Описание»).
    /// Работает на копии данных: результат возвращается через <see cref="Result"/>
    /// при подтверждении. Avalonia/Linux-версия WPF-окна <see cref="ServerEditWindow"/>.
    /// </summary>
    public sealed class ServerEditWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly string _originalId;

        // Поля со стилем ModernTextBox — акцентный контур при фокусе из темы (issue #51),
        // как в WPF-разметке (ServerEditWindow.xaml:32-68).
        private readonly TextBox _descriptionBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _addressBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _portBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _userBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly PasswordBox _passwordBox = new PasswordBox { Height = 32 }.Styled(ControlThemes.ModernPasswordBox);
        // Быстрый доступ по RDP/SSH (2.0): пусто — хост из поля «Адрес». Пароли не хранятся.
        private readonly TextBox _rdpHostBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _rdpUserBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);
        private readonly TextBox _sshTargetBox = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center }.Styled(ControlThemes.ModernTextBox);

        /// <summary>Готовая запись сервера при подтверждении, иначе <c>null</c>.</summary>
        public ManagementServer? Result { get; private set; }

        /// <param name="model">Редактируемая запись или <c>null</c> для новой.</param>
        public ServerEditWindow(ManagementServer? model = null)
        {
            _originalId = model?.Id ?? string.Empty;
            var isNew = model is null;

            Title = T(isNew ? "ServerMonitor.Servers.AddTitle" : "ServerMonitor.Servers.EditTitle");
            Width = 480;
            Height = 560;
            MinWidth = 440;
            MinHeight = 540;
            FontSize = 13;
            // Окно фиксированного размера (issue #3): без ресайза и без максимизации.
            CanResize = false;

            _descriptionBox.Text = model?.Description ?? string.Empty;
            _addressBox.Text = string.IsNullOrWhiteSpace(model?.Address) ? "localhost" : model!.Address;
            // Порт по умолчанию для нового сервера — 1545 (RAS, issue #3, комментарий 7OH 20.10).
            _portBox.Text = (model is { Port: > 0 } ? model.Port : ManagementServer.DefaultPort).ToString();
            _userBox.Text = model?.User ?? string.Empty;
            _passwordBox.Password = model?.Password ?? string.Empty;
            _rdpHostBox.Text = model?.RdpHost ?? string.Empty;
            _rdpUserBox.Text = model?.RdpUser ?? string.Empty;
            _sshTargetBox.Text = RemoteAccessLauncher.FormatSshTarget(model);
            ToolTip.SetTip(_rdpHostBox, T("RemoteAccess.RdpHostTooltip"));
            ToolTip.SetTip(_rdpUserBox, T("RemoteAccess.RdpUserTooltip"));
            ToolTip.SetTip(_sshTargetBox, T("RemoteAccess.SshTargetTooltip"));

            Content = BuildRoot();

            // Фокус в первом поле (описание): отложенно после показа окна — синхронная
            // установка в Opened слетает до активации модального диалога (паттерн #299).
            Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _descriptionBox.Focus();
                    if (isNew)
                        _descriptionBox.SelectAll();
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
            // Китая темы: заголовок окна не должен оставаться чёрным в тёмной схеме
            // (issue #61, замечание 7OH «заголовки полей по-прежнему чёрные»).
            Themes.ThemeBrushes.Bind(title, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            panel.Children.Add(title);

            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Servers.Description"), _descriptionBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Servers.Address"), _addressBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Servers.Port"), _portBox));
            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Servers.User"), _userBox));

            // Просмотр пароля «глазом» (паттерн ItsAccountEditWindow): наше поле —
            // TextBox с маской, поэтому показать/скрыть пароль можно сменой символа маски.
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
                // Issue #49: RevealPassword снимает маску в шаблоне TextBox Avalonia 11
                // надёжнее смены PasswordChar; оба значения держим согласованными.
                _passwordBox.RevealPassword = revealed;
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

            panel.Children.Add(MakeFieldRow(T("ServerMonitor.Servers.Password"), passwordGrid));
            panel.Children.Add(MakeFieldRow(T("RemoteAccess.RdpHost"), _rdpHostBox));
            panel.Children.Add(MakeFieldRow(T("RemoteAccess.RdpUser"), _rdpUserBox));
            panel.Children.Add(MakeFieldRow(T("RemoteAccess.SshTarget"), _sshTargetBox));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var save = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
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
            // Подпись Auto с минимумом (issue #55): длинная подпись не обрезается,
            // короткая сохраняет выравнивание по минимальной ширине.
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 130 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Margin = new Thickness(0, 4, 0, 4);

            var caption = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Подписи полей в тёмной теме (issue #61): фиксированный «чёрный» текст
            // нечитаем — привязываем единую кисть подписи формы (FormLabelBrush),
            // как в остальных окнах Avalonia (RacInfobasePropertiesWindow и др.).
            Themes.ThemeBrushes.Bind(caption, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private void OnSaveClick()
        {
            var description = _descriptionBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                _dialogs.ShowWarning(T("ServerMonitor.Servers.DescriptionRequired"), T("ServerMonitor.Servers.EditTitle"));
                _descriptionBox.Focus();
                return;
            }

            if (!int.TryParse(_portBox.Text?.Trim(), out var port) || port is < 1 or > 65535)
            {
                _dialogs.ShowWarning(T("ServerMonitor.Servers.InvalidPort"), T("ServerMonitor.Servers.EditTitle"));
                _portBox.Focus();
                return;
            }

            Result = new ManagementServer
            {
                Id = _originalId,
                Description = description,
                Address = string.IsNullOrWhiteSpace(_addressBox.Text) ? "localhost" : _addressBox.Text.Trim(),
                Port = port,
                User = _userBox.Text?.Trim() ?? string.Empty,
                Password = _passwordBox.Password ?? string.Empty,
                RdpHost = _rdpHostBox.Text?.Trim() ?? string.Empty,
                RdpUser = _rdpUserBox.Text?.Trim() ?? string.Empty,
            };
            RemoteAccessLauncher.ApplySshTarget(Result, _sshTargetBox.Text);
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
