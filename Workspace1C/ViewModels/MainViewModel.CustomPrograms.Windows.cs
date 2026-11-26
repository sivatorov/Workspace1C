#if WINDOWS
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Платформенные хуки группы «Мои программы» (issue #91) для Windows/WPF: хранение
/// режима отображения в настройках (отложенное сохранение, как у остальных настроек),
/// автоперенос «1С:Стартер» при первом запуске, сообщение об ошибке запуска через
/// <c>_dialogs</c> и открытие редактора через <c>ShowDialog</c> с владельцем —
/// главным окном.
/// </summary>
public partial class MainViewModel
{
    private string _customProgramsViewMode = Models.CustomProgramsViewModes.Default;

    /// <summary>Флаг миграции «1С:Стартер» из настроек (issue #91).</summary>
    private bool _customProgramsStarterMigrated;

    private partial string GetCustomProgramsViewMode() => _customProgramsViewMode;

    private partial void SetCustomProgramsViewMode(string mode)
    {
        _customProgramsViewMode = mode;
        ScheduleSaveSettings();
    }

    private partial void ShowCustomProgramLaunchError(string message, string title)
        => _dialogs.ShowError(message, title);

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
            _customProgramsStarterMigrated,
            NativeStarterLocator.FindStarterPath,
            LocalizationManager.T("Launch.CustomPrograms.NativeStarter"));
        if (!result.ShouldPersistFlag)
            return;
        _customProgramsStarterMigrated = true;
        ScheduleSaveSettings();
    }

    /// <summary>
    /// Открывает редактор группы «Мои программы» и после закрытия применяет выбранный
    /// режим отображения и перечитывает список программ.
    /// Issue #158 (1.0.1.123): единственная точка открытия с guard — повторный клик
    /// активирует уже открытый редактор, второе окно не создаётся.
    /// </summary>
    private partial void ExecuteShowCustomProgramsSettings()
    {
        var win = Workspace1C.CustomProgramsWindow.ShowEditor(
            System.Windows.Application.Current?.MainWindow, CustomProgramsViewMode);
        CustomProgramsViewMode = win.ViewMode;
        ReloadCustomPrograms();
    }
}
#endif
