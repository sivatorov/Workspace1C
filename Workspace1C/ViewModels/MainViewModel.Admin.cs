#if WINDOWS
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команды администрирования ИБ (Этап 6, функция №29 + консоль серверов) для
/// Windows/WPF: быстрый запуск <c>chdbfl.exe</c> для проверки целостности файловой
/// ИБ и запуск внешней консоли администрирования серверов 1С.
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkIntegrityCommand;
    private ICommand? _openServerConsoleCommand;
    private ICommand? _registerServerConsoleComponentsCommand;
    private string _serverConsolePlatformVersion = string.Empty;
    private string _hotkeyCheckIntegrity = "Ctrl+Alt+Q";
    private string _hotkeyServerConsole = "Ctrl+Alt+S";
    private string _hotkeyProcessInspector = "Ctrl+Alt+I";

    /// <summary>
    /// Выбранная версия платформы для внешней консоли администрирования серверов
    /// (issue #135): вариант «8.3.27.1688 (64)». Пусто — новейшая установленная
    /// версия с оснасткой 1CV8Servers.msc.
    /// </summary>
    public string ServerConsolePlatformVersion
    {
        get => _serverConsolePlatformVersion;
        set
        {
            if (SetProperty(ref _serverConsolePlatformVersion, value?.Trim() ?? string.Empty))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Горячая клавиша «Проверка целостности файловой ИБ (chdbfl)».</summary>
    public string HotkeyCheckIntegrity
    {
        get => _hotkeyCheckIntegrity;
        set
        {
            if (SetProperty(ref _hotkeyCheckIntegrity, NormalizeHotkey(value, "Ctrl+Alt+Q")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Горячая клавиша «Консоль администрирования серверов 1С».</summary>
    public string HotkeyServerConsole
    {
        get => _hotkeyServerConsole;
        set
        {
            if (SetProperty(ref _hotkeyServerConsole, NormalizeHotkey(value, "Ctrl+Alt+S")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>
    /// Горячая клавиша открытия «Инспектора процессов 1С» (issue #134, доработка
    /// ArtyoZ, по умолчанию Ctrl+Alt+I).
    /// </summary>
    public string HotkeyProcessInspector
    {
        get => _hotkeyProcessInspector;
        set
        {
            if (SetProperty(ref _hotkeyProcessInspector, NormalizeHotkey(value, "Ctrl+Alt+I")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда проверки целостности файловой ИБ через chdbfl (функция №29).</summary>
    public ICommand CheckIntegrityCommand =>
        _checkIntegrityCommand ??= new RelayCommand(
            ExecuteCheckIntegrity,
            () => SelectedInfobase?.Connection.Type == ConnectionType.File);

    /// <summary>
    /// Команда открытия внешней консоли администрирования серверов 1С.
    /// Консоль не связана с конкретной базой (issue #295), поэтому команда активна всегда —
    /// доступность не зависит от выбранной строки списка (группа, база, пустая область).
    /// </summary>
    public ICommand OpenServerConsoleCommand =>
        _openServerConsoleCommand ??= new RelayCommand(ExecuteOpenServerConsole);

    /// <summary>
    /// Команда регистрации компонентов консоли (radmin.dll, comctrl.dll) для выбранной
    /// версии платформы (issue #135, Windows-only).
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
            _dialogs.ShowError(
                LocalizationManager.T("Admin.CheckIntegrityFailed"),
                LocalizationManager.T("Admin.CheckIntegrityTitle"));
        }
    }

    private void ExecuteOpenServerConsole()
    {
        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        var options = service.GetServerConsolePlatformOptions();
        var candidates = service.GetServerConsoleCandidates();

        // Выбор версии — всегда при нескольких платформах (issue #135): диалог показывает
        // статус оснастки по каждой версии, даже если она не найдена ни в одной из них.
        var selected = PickServerConsoleVersion(options, candidates);
        if (selected is null && ServerConsolePlanner.ShouldShowPlatformPicker(options))
            return; // пользователь отменил выбор версии — ничего не запускаем

        var result = service.OpenServerAdminConsole(
            SelectedInfobase, selected ?? ServerConsolePlatformVersion);
        if (result.Success)
            return;

        // Диалог ошибки показывает диагностику (проверенные каталоги и найденные .msc):
        // ServerConsoleMessages.Format добавляет её к локализованной причине отказа.
        var title = LocalizationManager.T("Admin.ServerConsoleTitle");
        _dialogs.ShowError(ServerConsoleMessages.Format(result), title);

        // Оснастка не найдена или запуск не удался — предлагаем зарегистрировать
        // компоненты консоли (radmin.dll, comctrl.dll), как просил автор issue #135.
        if (result.CanOfferRegistration &&
            _dialogs.Confirm(
                LocalizationManager.T("Admin.ServerConsoleOfferRegister"), title))
        {
            ExecuteRegisterServerConsoleComponents();
        }
    }

    private void ExecuteRegisterServerConsoleComponents()
    {
        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        var options = service.GetServerConsolePlatformOptions();
        var candidates = service.GetServerConsoleCandidates();

        var selected = PickServerConsoleVersion(options, candidates);
        if (selected is null && ServerConsolePlanner.ShouldShowPlatformPicker(options))
            return; // выбор версии отменён

        var version = selected ?? ServerConsolePlatformVersion;
        // regsvr32 с UAC ждёт действия пользователя — не блокируем UI-поток.
        _ = Task.Run(() =>
        {
            var result = service.RegisterServerConsoleComponents(version);
            Application.Current?.Dispatcher.Invoke(() => ShowServerConsoleRegistrationResult(result));
        });
    }

    /// <summary>
    /// Выбор версии платформы для консоли (issue #135). Диалог показывается всегда,
    /// когда установлено больше одной платформы: пользователь видит статус оснастки
    /// по каждой версии, даже если оснастка не найдена ни в одной из них.
    /// Возвращает выбранный вариант («8.3.27.1688 (64)») или null при отмене.
    /// </summary>
    private string? PickServerConsoleVersion(
        IReadOnlyList<ServerConsolePlatformOption> options,
        IReadOnlyList<ServerConsoleCandidate> candidates)
    {
        var variants = BuildServerConsoleVariants(options, candidates);
        if (variants.Count == 0)
            return string.IsNullOrWhiteSpace(ServerConsolePlatformVersion)
                ? null
                : ServerConsolePlatformVersion;

        if (variants.Count == 1)
        {
            var only = variants[0];
            if (string.IsNullOrWhiteSpace(ServerConsolePlatformVersion))
                ServerConsolePlatformVersion = only;
            return ServerConsolePlanner.ChoosePreferredPlatform(options, ServerConsolePlatformVersion)?.Display
                   ?? ServerConsolePlanner.ChoosePreferred(candidates, ServerConsolePlatformVersion)?.Display
                   ?? only;
        }

        var dialog = new Workspace1C.PlatformVersionPickerWindow(
            variants,
            ServerConsolePlatformVersion,
            useProvidedVersionsOnly: true,
            statusByVariant: ServerConsoleMessages.BuildPlatformStatuses(options));
        if (Application.Current?.MainWindow is { } owner)
            dialog.Owner = owner;
        if (dialog.ShowDialog() != true)
            return null;

        var selected = dialog.Result?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selected))
            return null;

        ServerConsolePlatformVersion = selected;
        return selected;
    }

    /// <summary>
    /// Варианты для диалога выбора версии (issue #135): все установленные платформы,
    /// а если их список пуст — платформы с оснасткой.
    /// </summary>
    private static List<string> BuildServerConsoleVariants(
        IReadOnlyList<ServerConsolePlatformOption> options,
        IReadOnlyList<ServerConsoleCandidate> candidates)
    {
        var variants = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in options.Select(o => o.Display).Concat(candidates.Select(c => c.Display)))
        {
            if (string.IsNullOrWhiteSpace(display) || !seen.Add(display))
                continue;
            variants.Add(display);
        }

        return variants;
    }

    private void ShowServerConsoleRegistrationResult(ServerConsoleRegistrationResult result)
    {
        var title = LocalizationManager.T("Admin.ServerConsoleTitle");
        var text = ServerConsoleMessages.BuildRegistrationReport(result);
        if (result.Success)
            _dialogs.ShowInfo(text, title);
        else if (result.BinDir is null)
            _dialogs.ShowError(text, title);
        else
            _dialogs.ShowWarning(text, title);
    }
}
#endif
