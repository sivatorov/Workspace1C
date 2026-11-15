#if LINUX
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Платформенные хуки группы «Мои программы» (issue #91) для Linux/Avalonia: режим
/// отображения и флаг автопереноса «1С:Стартер» хранятся в общем файле настроек
/// (<c>_settings</c>), сообщение об ошибке запуска — через <c>_dialog</c>, редактор
/// открывается модально через <c>ShowDialogSync</c>.
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
    /// Автоперенос «1С:Стартер» в группу «Мои программы» (issue #91): при первом
    /// запуске с пустым списком добавляет программу с путём к родному стартеру 1С.
    /// Флаг сохраняется, только если миграция завершена (запись создана или список
    /// уже был настроен); при ненайденном стартере попытка повторится при следующем
    /// запуске.
    /// </summary>
    private partial void EnsureCustomProgramsStarterMigration()
    {
        var result = CustomProgramsStarterMigration.TryMigrate(
            AppServices.TryGetService<ICustomProgramsStore>(),
            _settings.CustomProgramsStarterMigrated,
            NativeStarterLocator.FindStarterPath,
            LocalizationManager.T("Launch.CustomPrograms.NativeStarter"));
        if (!result.ShouldPersistFlag)
            return;
        _settings.CustomProgramsStarterMigrated = true;
        SaveSettingsSilently();
    }

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
