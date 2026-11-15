#if LINUX
namespace Workspace1C.ViewModels;

/// <summary>
/// Платформенные хуки группы «Мои программы» (issue #91) для Linux/Avalonia: режим
/// отображения хранится в общем файле настроек (<c>_settings</c>), сообщение об ошибке
/// запуска — через <c>_dialog</c>, редактор открывается модально через <c>ShowDialogSync</c>.
/// </summary>
public partial class MainViewModel
{
    private partial string GetCustomProgramsViewMode()
        => Models.CustomProgramsViewModes.Normalize(_settings.CustomProgramsViewMode);

    private partial void SetCustomProgramsViewMode(string mode)
    {
        _settings.CustomProgramsViewMode = mode;
        SaveSettingsSilently();
    }

    private partial void ShowCustomProgramLaunchError(string message, string title)
        => _dialog.ShowError(message, title);

    /// <summary>
    /// Открывает редактор группы «Мои программы» и после закрытия применяет выбранный
    /// режим отображения и перечитывает список программ.
    /// </summary>
    private partial void ExecuteShowCustomProgramsSettings()
    {
        var win = new Workspace1C.CustomProgramsWindow(CustomProgramsViewMode);
        win.ShowDialogSync(OwnerWindow());
        CustomProgramsViewMode = win.ViewMode;
        ReloadCustomPrograms();
    }
}
#endif
