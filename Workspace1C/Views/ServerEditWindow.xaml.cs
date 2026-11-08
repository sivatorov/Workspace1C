#if WINDOWS
using System;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Модальный редактор записи справочника «Сервера управления» (issue #3): описание,
/// адрес и порт, логин и пароль. Скининг — по теме приложения (DynamicResource),
/// фокус по умолчанию на первом поле («Описание»). Работает на копии данных:
/// результат возвращается через <see cref="Result"/> при подтверждении.
/// </summary>
public partial class ServerEditWindow : Window
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly string _originalId;

    // Флаг показа пароля «глазом» (паттерн ItsAccountEditWindow): WPF-PasswordBox
    // не умеет снимать маску напрямую, поэтому при показе скрываем его и показываем
    // текстовое поле.
    private bool _isPasswordRevealed;

    /// <summary>Готовая запись сервера при подтверждении, иначе <c>null</c>.</summary>
    public ManagementServer? Result { get; private set; }

    /// <param name="model">Редактируемая запись или <c>null</c> для новой.</param>
    public ServerEditWindow(ManagementServer? model = null)
    {
        InitializeComponent();

        _originalId = model?.Id ?? string.Empty;
        var isNew = model is null;
        EditorTitle.Text = isNew
            ? LocalizationManager.T("ServerMonitor.Servers.AddTitle")
            : LocalizationManager.T("ServerMonitor.Servers.EditTitle");
        Title = EditorTitle.Text;

        DescriptionBox.Text = model?.Description ?? string.Empty;
        AddressBox.Text = string.IsNullOrWhiteSpace(model?.Address) ? "localhost" : model!.Address;
        // Порт по умолчанию для нового сервера — 1545 (RAS, issue #3, комментарий 7OH 20.10).
        PortBox.Text = (model is { Port: > 0 } ? model.Port : ManagementServer.DefaultPort).ToString();
        UserBox.Text = model?.User ?? string.Empty;
        if (model is not null)
            PasswordBox.Password = model.Password ?? string.Empty;
        RdpHostBox.Text = model?.RdpHost ?? string.Empty;
        RdpUserBox.Text = model?.RdpUser ?? string.Empty;
        SshTargetBox.Text = RemoteAccessLauncher.FormatSshTarget(model);

        // Фокус в первом поле (описание) — отложенный вызов после показа окна,
        // иначе при ShowDialog() фокус «съедается» до активации окна (паттерн #299).
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    DescriptionBox.Focus();
                    if (isNew)
                        DescriptionBox.SelectAll();
                }));
        };
    }

    /// <summary>Переключает видимость пароля между PasswordBox и полем показа.</summary>
    private void OnShowPasswordToggle_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            PasswordRevealTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordRevealTextBox.Visibility = Visibility.Visible;
            PasswordRevealTextBox.Focus();
            PasswordRevealTextBox.CaretIndex = PasswordRevealTextBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordRevealTextBox.Text ?? string.Empty;
            PasswordRevealTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var description = DescriptionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            _dialogs.ShowWarning(
                LocalizationManager.T("ServerMonitor.Servers.DescriptionRequired"),
                LocalizationManager.T("ServerMonitor.Servers.EditTitle"));
            DescriptionBox.Focus();
            return;
        }

        if (!int.TryParse(PortBox.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            _dialogs.ShowWarning(
                LocalizationManager.T("ServerMonitor.Servers.InvalidPort"),
                LocalizationManager.T("ServerMonitor.Servers.EditTitle"));
            PortBox.Focus();
            return;
        }

        Result = new ManagementServer
        {
            Id = _originalId,
            Description = description,
            Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? "localhost" : AddressBox.Text.Trim(),
            Port = port,
            User = UserBox.Text?.Trim() ?? string.Empty,
            Password = _isPasswordRevealed
                ? (PasswordRevealTextBox.Text ?? string.Empty)
                : (PasswordBox.Password ?? string.Empty),
            RdpHost = RdpHostBox.Text?.Trim() ?? string.Empty,
            RdpUser = RdpUserBox.Text?.Trim() ?? string.Empty,
        };
        RemoteAccessLauncher.ApplySshTarget(Result, SshTargetBox.Text);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif
