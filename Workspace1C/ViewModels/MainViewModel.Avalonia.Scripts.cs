#if LINUX
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команды и методы функции «Сценарии» (issue #308) для Avalonia/Linux: настройка
/// сценариев запуска скриптов и выполнение выбранного сценария для ИБ.
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showScriptsSettingsCommand;
    private ICommand? _runScriptForSelectedCommand;

    /// <summary>Горячая клавиша «Выполнить скрипт» для выбранной базы (F5).</summary>
    public string HotkeyRunScript => "F5";

    /// <summary>Команда открытия окна «Настройка сценариев».</summary>
    public ICommand ShowScriptsSettingsCommand =>
        _showScriptsSettingsCommand ??= new RelayCommand(ExecuteShowScriptsSettings);

    /// <summary>Команда выполнения сценария для выбранной ИБ (F5).</summary>
    public ICommand RunScriptForSelectedCommand =>
        _runScriptForSelectedCommand ??= new RelayCommand(
            ExecuteRunScriptForSelected,
            () => SelectedInfobase is not null);

    private void ExecuteShowScriptsSettings()
    {
        var win = new Workspace1C.ScriptScenariosWindow();
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Выполняет сценарий: одиночный — сразу, несколько — выбор в окне.</summary>
    private async void ExecuteRunScriptForSelected()
    {
        // v100114: прямой async void (вне обёртки RelayCommand) — исключение не должно
        // ронять приложение: журнал + диалог ошибки.
        try
        {
            await ExecuteRunScriptForSelectedCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка запуска сценария скрипта", ex);
            _dialog.ShowError(ex.Message, LocalizationManager.T("Script.RunTitle"));
        }
    }

    /// <summary>Тело запуска сценария скрипта (см. <see cref="ExecuteRunScriptForSelected"/>).</summary>
    private async System.Threading.Tasks.Task ExecuteRunScriptForSelectedCoreAsync()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
        {
            _dialog.ShowWarning(LocalizationManager.T("Script.NoBaseSelected"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        var store = AppServices.GetRequiredService<IScriptScenarioStore>();
        var scenarios = store.LoadAll();
        if (scenarios.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Script.NoneConfigured"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        if (scenarios.Count == 1)
        {
            await RunScriptAsync(infobase, scenarios[0]);
            return;
        }

        var win = new Workspace1C.ScriptPickWindow(infobase, this);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>
    /// Выполняет сценарий запуска скрипта: подставляет свойства базы в параметры,
    /// запускает через системный shell без ожидания (fire-and-forget) и пишет
    /// команду в историю запусков базы.
    /// </summary>
    public async Task RunScriptAsync(Infobase infobase, ScriptScenario scenario)
    {
        if (infobase is null || scenario is null)
            return;

        // Логин/пароль, которые реально подставятся при запуске базы: у базы без
        // собственной авторизации это общая авторизация (issue #57) — сценарий должен
        // «понимать все свойства базы», в т.ч. логин (issue #67). Контекст платформы
        // даёт токены %full_launch_*%, %full_exe_name%, %platform_version% (issue #67).
        var values = ScriptParameterResolver.BuildValueMap(
            infobase, ResolveEffectiveScriptCredentials(infobase),
            ScriptLaunchContextResolver.Resolve(infobase));
        // Шелл сценария передаётся в сборку тела: для PowerShell разделитель
        // между «cd …» и командой — «;» вместо «&&» (issue #308, замечание @7OH).
        var commandBody = ScriptParameterResolver.BuildCommandLine(
            scenario, values, shell: scenario.Shell, isWindows: OperatingSystem.IsWindows());
        if (string.IsNullOrWhiteSpace(commandBody))
        {
            _dialog.ShowWarning(LocalizationManager.T("Script.EmptyCommand"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        // Полная командная строка с обёрткой выбранного интерпретатора (issue #308, п.9)
        // для лога и истории запусков; тело запускается с указанным shell (без повторной обёртки).
        var (shellFile, shellArgs) =
            ExternalCommandRunner.ResolveShellWrapper(scenario.Shell, commandBody, OperatingSystem.IsWindows());
        var commandLine = shellFile + " " + shellArgs;

        try
        {
            // Запуск без ожидания: пользовательский скрипт может выполняться долго,
            // а результат для приложения не критичен (лог + история запусков).
            // Видимость консольного окна — по свойству «Скрывать окно» сценария (issue #308);
            // на Linux /bin/sh выполняется без терминала, окно зависит от окружения.
            // Рабочая папка — по свойству «Папка запуска» (issue #308, п.7),
            // интерпретатор — по свойству «Интерпретатор» (issue #308, п.9).
            ExternalCommandRunner.RunDetached(
                commandBody,
                // Видимость консольного окна — по свойству «Скрывать окно»: снятая галка
                // (HideWindow=false) даёт видимое окно (issue #308, замечание @7OH 14:43);
                // раньше флаг был инвертирован.
                createNoWindow: scenario.HideWindow,
                workingDirectory: scenario.WorkingDirectory,
                shell: scenario.Shell);

            infobase.AddLaunchHistory("Script:" + scenario.Name, commandLine);
            SaveSilently();
            _logger.Info($"Запущен сценарий «{scenario.Name}» для базы «{infobase.Name}»: {commandLine}");
            // Окно «скрипт запущен» — только при включённой галочке «Уведомлять о запуске»
            // (issue #308): без опций — просто не надо.
            if (scenario.NotifyOnStart)
            {
                _dialog.ShowInfo(
                    string.Format(LocalizationManager.T("Script.Started"), commandLine),
                    LocalizationManager.T("Script.RunTitle"));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось запустить сценарий «{scenario.Name}»: {ex.Message}", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Script.Failed"), scenario.Name, ex.Message),
                LocalizationManager.T("Script.RunTitle"));
        }

        // Системное уведомление (функция №4): только вместе с галочкой «Уведомлять
        // о запуске» (issue #308); приложение может быть свёрнуто, а диалог результата
        // увиден не будет.
        if (scenario.NotifyOnStart)
        {
            try
            {
                AppServices.GetRequiredService<INotificationService>().Show(
                    LocalizationManager.T("App.Title"),
                    string.Format(LocalizationManager.T("Notify.ScriptStarted"), infobase.Name, scenario.Name));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Системное уведомление не показано: {ex.Message}");
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Логин/пароль, которые реально подставятся при запуске базы в режиме
    /// «1С:Предприятие» (собственная авторизация базы/режима или общая — issue #57).
    /// Для сценариев (issue #67): чтобы подстановка %connection.user%/%connection.password%
    /// соответствовала фактическому запуску. null — авторизация не в виде
    /// «Логин+Пароль» (аутентификация ОС/запрос от платформы).
    /// </summary>
    private static (string? User, string? Password)? ResolveEffectiveScriptCredentials(Infobase infobase)
    {
        if (infobase is null)
            return null;

        Workspace1C.Services.InfobaseAuthResolver.Resolve(
            infobase, Workspace1C.Services.OneCLaunchMode.Enterprise,
            out var authMode, out var authUser, out var authPassword,
            Workspace1C.Services.OneCLauncher.CommonEnterpriseAuthorization,
            Workspace1C.Services.OneCLauncher.CommonConfiguratorAuthorization,
            Workspace1C.Services.OneCLauncher.CommonAuthorization);
        return authMode == Workspace1C.Models.AuthenticationMode.Credentials
            ? (authUser, authPassword)
            : null;
    }
}
#endif