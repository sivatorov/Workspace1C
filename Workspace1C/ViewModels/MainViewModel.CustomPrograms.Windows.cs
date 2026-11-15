#if WINDOWS
namespace Workspace1C.ViewModels;

/// <summary>
/// Платформенные хуки группы «Мои программы» (issue #91) для Windows/WPF: хранение
/// режима отображения в настройках (отложенное сохранение, как у остальных настроек),
/// сообщение об ошибке запуска через <c>_dialogs</c> и открытие редактора через
/// <c>ShowDialog</c> с владельцем — главным окном.
/// </summary>
public partial class MainViewModel
{
    private string _customProgramsViewMode = Models.CustomProgramsViewModes.Default;

    private partial string GetCustomProgramsViewMode() => _customProgramsViewMode;

    private partial void SetCustomProgramsViewMode(string mode)
    {
        _customProgramsViewMode = mode;
        ScheduleSaveSettings();
    }

    private partial void ShowCustomProgramLaunchError(string message, string title)
        => _dialogs.ShowError(message, title);

    /// <summary>
    /// Открывает редактор группы «Мои программы» и после закрытия применяет выбранный
    /// режим отображения и перечитывает список программ.
    /// </summary>
    private partial void ExecuteShowCustomProgramsSettings()
    {
        var win = new Workspace1C.CustomProgramsWindow(CustomProgramsViewMode);
        win.Owner = System.Windows.Application.Current?.MainWindow;
        win.ShowDialog();
        CustomProgramsViewMode = win.ViewMode;
        ReloadCustomPrograms();
    }
}
#endif
