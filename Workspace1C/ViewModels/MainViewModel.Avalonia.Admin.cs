#if LINUX
using System.Threading.Tasks;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команды администрирования ИБ (Этап 6, функция №29 + консоль серверов) для
/// Avalonia/Linux: быстрый запуск <c>chdbfl</c> для проверки целостности файловой
/// ИБ и запуск внешней консоли администрирования серверов 1С (только Windows).
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkIntegrityCommand;
    private ICommand? _openServerConsoleCommand;
    private ICommand? _registerServerConsoleComponentsCommand;

    /// <summary>
    /// Выбранная версия платформы для внешней консоли администрирования серверов
    /// (issue #135): вариант «8.3.27.1688 (64)». Хранится в настройках приложения.
    /// </summary>
    public string ServerConsolePlatformVersion
    {
        get => _settings.ServerConsolePlatformVersion;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_settings.ServerConsolePlatformVersion, normalized, StringComparison.Ordinal))
                return;
            _settings.ServerConsolePlatformVersion = normalized;
            SaveSettingsSilently();
            OnPropertyChanged();
        }
    }

    /// <summary>Горячая клавиша «Проверка целостности файловой ИБ (chdbfl)».</summary>
    public string HotkeyCheckIntegrity => _settings.HotkeyCheckIntegrity;

    /// <summary>Горячая клавиша «Консоль администрирования серверов 1С».</summary>
    public string HotkeyServerConsole => _settings.HotkeyServerConsole;

    /// <summary>Команда проверки целостности файловой ИБ через chdbfl (функция №29).</summary>
    public ICommand CheckIntegrityCommand =>
        _checkIntegrityCommand ??= new RelayCommand(
            ExecuteCheckIntegrity,
            () => SelectedInfobase?.Connection.Type == ConnectionType.File);

    /// <summary>
    /// Команда открытия внешней консоли администрирования серверов 1С.
    /// Консоль не связана с конкретной базой (issue #295), поэтому команда активна всегда.
    /// </summary>
    public ICommand OpenServerConsoleCommand =>
        _openServerConsoleCommand ??= new RelayCommand(ExecuteOpenServerConsole);

    /// <summary>
    /// Команда регистрации компонентов консоли (radmin.dll, comctrl.dll) для выбранной
    /// версии платформы (issue #135). На Linux недоступна — сообщает причину.
    /// </summary>
    public ICommand RegisterServerConsoleComponentsCommand =>
        _registerServerConsoleComponentsCommand ??= new RelayCommand(ExecuteRegisterServerConsoleComponents);

    private void ExecuteCheckIntegrity()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        if (!service.CheckIntegrity(infobase))
        {
            _dialog.ShowError(
                LocalizationManager.T("Admin.CheckIntegrityFailed"),
                LocalizationManager.T("Admin.CheckIntegrityTitle"));
        }
    }

    private void ExecuteOpenServerConsole()
    {
        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        var candidates = service.GetServerConsoleCandidates();
        var candidate = PickServerConsoleCandidate(candidates);

        // Пользователь отменил выбор версии — молча выходим, ничего не запуская.
        if (candidates.Count > 0 && candidate is null)
            return;

        var result = service.OpenServerAdminConsole(
            SelectedInfobase, candidate?.Display ?? ServerConsolePlatformVersion);
        if (result.Success)
            return;

        var title = LocalizationManager.T("Admin.ServerConsoleTitle");
        var message = ServerConsoleMessages.Format(result);
        if (result.ErrorKey == ServerConsolePlanner.ErrorWindowsOnly)
            _dialog.ShowWarning(message, title);
        else
            _dialog.ShowError(message, title);

        // Если причина — сбой запуска, предлагаем зарегистрировать компоненты консоли.
        if (result.ErrorKey == ServerConsolePlanner.ErrorLaunchFailed &&
            _dialog.Confirm(LocalizationManager.T("Admin.ServerConsoleOfferRegister"), title))
        {
            ExecuteRegisterServerConsoleComponents();
        }
    }

    private void ExecuteRegisterServerConsoleComponents()
    {
        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        var candidates = service.GetServerConsoleCandidates();
        var candidate = PickServerConsoleCandidate(candidates);
        if (candidate is null)
        {
            // Нет платформы с оснасткой (или выбор отменён) — показываем точную причину сервиса.
            ShowServerConsoleRegistrationResult(
                service.RegisterServerConsoleComponents(ServerConsolePlatformVersion));
            return;
        }

        var version = candidate.Display;
        // regsvr32 с UAC ждёт действия пользователя — не блокируем UI-поток.
        _ = Task.Run(() =>
        {
            var result = service.RegisterServerConsoleComponents(version);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowServerConsoleRegistrationResult(result));
        });
    }

    /// <summary>
    /// Выбор версии платформы для консоли: единственная версия — без диалога,
    /// несколько — диалог только по установленным версиям с оснасткой.
    /// </summary>
    private ServerConsoleCandidate? PickServerConsoleCandidate(
        IReadOnlyList<ServerConsoleCandidate> candidates)
    {
        if (candidates.Count == 0)
            return null;

        if (!ServerConsolePlanner.ShouldShowPicker(candidates))
        {
            var only = candidates[0];
            if (string.IsNullOrWhiteSpace(ServerConsolePlatformVersion))
                ServerConsolePlatformVersion = only.Display;
            return ServerConsolePlanner.ChoosePreferred(candidates, ServerConsolePlatformVersion) ?? only;
        }

        var dialog = new Workspace1C.PlatformVersionPickerWindow(
            candidates.Select(c => c.Display).ToList(),
            ServerConsolePlatformVersion,
            useProvidedVersionsOnly: true);
        if (!dialog.ShowDialogSync(OwnerWindow()))
            return null;

        var selected = dialog.Result?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selected))
            return null;

        ServerConsolePlatformVersion = selected;
        return ServerConsolePlanner.ChoosePreferred(candidates, selected);
    }

    private void ShowServerConsoleRegistrationResult(ServerConsoleRegistrationResult result)
    {
        var title = LocalizationManager.T("Admin.ServerConsoleTitle");
        var text = ServerConsoleMessages.BuildRegistrationReport(result);
        if (result.Success)
            _dialog.ShowInfo(text, title);
        else if (result.BinDir is null)
            _dialog.ShowError(text, title);
        else
            _dialog.ShowWarning(text, title);
    }
}
#endif
