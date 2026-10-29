#if LINUX
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Отвечает за запуск информационных баз в различных режимах (Avalonia/Linux).
/// </summary>
public sealed class LaunchViewModel : ViewModelBase
{
    private readonly Func<Infobase?> _getSelected;
    private readonly IOneCLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly IDialogService _dialog;
    private readonly Action _onLaunched;

    public LaunchViewModel(
        Func<Infobase?> getSelected,
        IOneCLauncher launcher,
        IAppLogger logger,
        IDialogService dialog,
        Action onLaunched)
    {
        _getSelected = getSelected;
        _launcher = launcher;
        _logger = logger;
        _dialog = dialog;
        _onLaunched = onLaunched;

        LaunchCommand = new RelayCommand(Launch, _ => _getSelected() is not null);
    }

    public ICommand LaunchCommand { get; }

    /// <summary>
    /// Переопределения запуска Предприятия из блока «Текущая сессия».
    /// Возвращает null, когда переопределять нечего, и тогда запуск идёт
    /// по настройкам самой базы.
    /// </summary>
    public Func<Infobase, LaunchOverrides?>? EnterpriseOverrides { get; set; }

    /// <summary>Запуск Предприятия с учётом переопределений текущей сессии.</summary>
    private bool LaunchEnterprise(Infobase infobase)
    {
        var overrides = EnterpriseOverrides?.Invoke(infobase);
        return overrides is null
            ? _launcher.Launch(infobase, OneCLaunchMode.Enterprise)
            : _launcher.Launch(infobase, OneCLaunchMode.Enterprise,
                overrides.Client, overrides.RunMode, overrides.Architecture);
    }

    public void Launch(object? parameter)
    {
        var selected = _getSelected();
        if (selected is null)
            return;

        // Пользовательские скрипты (функция №8, 0.3.9.98): pre-команда может ждать
        // завершения до 30 секунд, поэтому фактический запуск уходит в ядро.
        _ = LaunchCoreAsync(selected, parameter);
    }

    /// <summary>
    /// Ядро запуска: pre-команда (ожидание с таймаутом, при ошибке — предупреждение
    /// и продолжение) → запуск 1С → post-команда (fire-and-forget при успехе).
    /// </summary>
    private async Task LaunchCoreAsync(Infobase selected, object? parameter)
    {
        try
        {
            var kind = parameter switch
            {
                LaunchKind k => k,
                string s when Enum.TryParse<LaunchKind>(s, true, out var parsed) => parsed,
                _ => LaunchKind.Enterprise
            };

            await RunPreLaunchScriptAsync(selected);

            bool ok = kind switch
            {
                LaunchKind.Configurator =>
                    _launcher.Launch(selected, OneCLaunchMode.Configurator),
                LaunchKind.Thin32 =>
                    _launcher.Launch(selected, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x86),
                LaunchKind.Thick32 =>
                    _launcher.Launch(selected, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x86),
                LaunchKind.Thin64 =>
                    _launcher.Launch(selected, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x64),
                LaunchKind.Thick64 =>
                    _launcher.Launch(selected, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x64),
                _ => LaunchEnterprise(selected)
            };

            if (ok)
            {
                RunPostLaunchScript(selected);
                // issue #80: INFO «запущено баз» — через категорийный гейт CM_BASE_RUN + CM_ON.
                Log.Info(LogCategory.BaseRun, $"Запущена база «{selected.Name}» ({kind})");
                _onLaunched();
            }
            else
            {
                _logger.Warn($"Не удалось запустить базу «{selected.Name}» ({kind})");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка запуска базы «{selected.Name}»", ex);
        }
    }

    /// <summary>Выполняет пользовательскую pre-команду базы (функция №8, 0.3.9.98).</summary>
    private async Task RunPreLaunchScriptAsync(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PreLaunchCommand))
            return;

        var ok = await ExternalCommandRunner.RunAsync(
            ib.PreLaunchCommand, ExternalCommandRunner.DefaultPreCommandTimeoutMs);

        if (ok)
        {
            _logger.Info($"Pre-команда базы «{ib.Name}» выполнена: {ib.PreLaunchCommand}");
        }
        else
        {
            _logger.Warn($"Pre-команда базы «{ib.Name}» завершилась с ошибкой или таймаутом: {ib.PreLaunchCommand}");
            // Предупреждаем, но запуск базы не блокируем — он продолжится.
            _dialog.ShowWarning(
                string.Format(LocalizationManager.T("Launch.PreCommandFailed"), ib.PreLaunchCommand),
                LocalizationManager.T("Launch.CommandsTitle"));
        }
    }

    /// <summary>Запускает пользовательскую post-команду базы без ожидания (функция №8, 0.3.9.98).</summary>
    private void RunPostLaunchScript(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PostLaunchCommand))
            return;

        ExternalCommandRunner.RunDetached(ib.PostLaunchCommand);
        _logger.Info($"Запущена post-команда базы «{ib.Name}»: {ib.PostLaunchCommand}");
    }
}

/// <summary>
/// Переопределения очередного запуска Предприятия: тип клиента, режим форм
/// и разрядность. Пустое значение поля означает «как у базы».
/// </summary>
public sealed record LaunchOverrides(OneCClientType? Client, OneCRunMode? RunMode, OneCArchitecture Architecture);
#endif
