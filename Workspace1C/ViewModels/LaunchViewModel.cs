using System.Windows.Input;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Отвечает за запуск информационных баз в различных режимах.
/// Вынесено из MainViewModel для разделения ответственности (чат 1 / чат 3).
/// </summary>
public sealed class LaunchViewModel : ViewModelBase
{
    private readonly Func<Infobase?> _getSelected;
    private readonly IOneCLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly Action _onLaunched;

    public LaunchViewModel(
        Func<Infobase?> getSelected,
        IOneCLauncher launcher,
        IAppLogger logger,
        Action onLaunched)
    {
        _getSelected = getSelected;
        _launcher = launcher;
        _logger = logger;
        _onLaunched = onLaunched;

        LaunchCommand = new RelayCommand(Launch, _ => _getSelected() is not null);
    }

    public ICommand LaunchCommand { get; }

    public void Launch(object? parameter)
    {
        var selected = _getSelected();
        if (selected is null)
            return;

        var kind = parameter switch
        {
            LaunchKind k => k,
            string s when Enum.TryParse<LaunchKind>(s, true, out var parsed) => parsed,
            _ => LaunchKind.Enterprise
        };

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
            _ => _launcher.Launch(selected, OneCLaunchMode.Enterprise)
        };

        if (ok)
        {
            // issue #80: INFO направления «запущено баз» пишется через категорийный
            // гейт CM_BASE_RUN + CM_ON, а не голым _logger.Info (иначе запись
            // «капает» в журнал при выключенном логировании).
            Log.Info(LogCategory.BaseRun, $"Запущена база «{selected.Name}» ({kind})");
            _onLaunched();
        }
        else
        {
            _logger.Warn($"Не удалось запустить базу «{selected.Name}» ({kind})");
        }
    }
}
