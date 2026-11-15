using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Мост группы «Мои программы» (issue #91): кэш пользовательских программ из
/// <see cref="ICustomProgramsStore"/>, запуск программы с сообщением об ошибке без
/// падения интерфейса, переключатель отображения группы «списком / иконками»
/// (<see cref="CustomProgramsViewMode"/> в настройках) и команда открытия редактора.
/// Общий для обеих платформ файл (без #if); диалоги, сохранение режима и открытие окна
/// делегируются платформенным partial-хукам
/// (MainViewModel.CustomPrograms.Windows.cs / *.Avalonia.cs).
/// </summary>
public partial class MainViewModel
{
    private readonly ObservableCollection<CustomProgram> _customPrograms = new();
    private ICommand? _launchCustomProgramCommand;
    private ICommand? _showCustomProgramsSettingsCommand;

    /// <summary>
    /// Программы группы «Мои программы» в порядке отображения. Коллекция заменяется
    /// поштучно при <see cref="ReloadCustomPrograms"/> — панель запуска обновляется
    /// без перестроения всего окна.
    /// </summary>
    public ObservableCollection<CustomProgram> CustomPrograms => _customPrograms;

    /// <summary>Есть ли программы для показа группы на панели запуска.</summary>
    public bool HasCustomPrograms => _customPrograms.Count > 0;

    /// <summary>
    /// Режим отображения группы: <see cref="CustomProgramsViewModes.List"/> («Списком»)
    /// либо <see cref="CustomProgramsViewModes.Icons"/> («Иконками»). Хранится в
    /// <see cref="AppSettings.CustomProgramsViewMode"/>; при изменении панель
    /// перестраивается через уведомления производных признаков.
    /// </summary>
    public string CustomProgramsViewMode
    {
        get => GetCustomProgramsViewMode();
        set
        {
            var normalized = CustomProgramsViewModes.Normalize(value);
            if (string.Equals(GetCustomProgramsViewMode(), normalized, StringComparison.Ordinal))
                return;
            SetCustomProgramsViewMode(normalized);
            OnPropertyChanged(nameof(CustomProgramsViewMode));
            OnPropertyChanged(nameof(CustomProgramsListView));
            OnPropertyChanged(nameof(CustomProgramsIconsView));
        }
    }

    /// <summary>Показывать группу вертикальным списком кнопок («иконка + подпись»).</summary>
    public bool CustomProgramsListView =>
        string.Equals(GetCustomProgramsViewMode(), CustomProgramsViewModes.List, StringComparison.Ordinal);

    /// <summary>Показывать группу кнопками-иконками в строку с переносом.</summary>
    public bool CustomProgramsIconsView =>
        string.Equals(GetCustomProgramsViewMode(), CustomProgramsViewModes.Icons, StringComparison.Ordinal);

    /// <summary>Перечитывает программы из хранилища (после редактора или старта).</summary>
    public void ReloadCustomPrograms()
    {
        IReadOnlyList<CustomProgram> loaded;
        try
        {
            loaded = AppServices.GetRequiredService<ICustomProgramsStore>().LoadAll();
        }
        catch
        {
            // Сервис может отсутствовать в изолированном контексте — группа не критична.
            loaded = Array.Empty<CustomProgram>();
        }

        _customPrograms.Clear();
        foreach (var program in loaded.Where(p => p.Enabled))
            _customPrograms.Add(program);
        OnPropertyChanged(nameof(HasCustomPrograms));
    }

    /// <summary>
    /// Команда запуска пользовательской программы (параметр — <see cref="CustomProgram"/>).
    /// Отсутствующий файл и ошибки запуска показываются локализованным сообщением.
    /// </summary>
    public ICommand LaunchCustomProgramCommand =>
        _launchCustomProgramCommand ??= new RelayCommand(
            p => LaunchCustomProgram(p as CustomProgram),
            p => p is CustomProgram);

    /// <summary>Команда открытия редактора группы «Мои программы».</summary>
    public ICommand ShowCustomProgramsSettingsCommand =>
        _showCustomProgramsSettingsCommand ??= new RelayCommand(_ => ExecuteShowCustomProgramsSettings());

    private void LaunchCustomProgram(CustomProgram? program)
    {
        if (program is null)
            return;
        if (!CustomProgramLauncher.TryLaunch(program, _logger, out var error) &&
            !string.IsNullOrWhiteSpace(error))
        {
            ShowCustomProgramLaunchError(
                error, LocalizationManager.T("Launch.CustomPrograms.ErrorTitle"));
        }
    }

    /// <summary>Чтение режима отображения (платформенное хранилище настроек).</summary>
    private partial string GetCustomProgramsViewMode();

    /// <summary>Запись режима отображения (платформенное хранилище настроек).</summary>
    private partial void SetCustomProgramsViewMode(string mode);

    /// <summary>Сообщение об ошибке запуска программы (платформенный диалог).</summary>
    private partial void ShowCustomProgramLaunchError(string message, string title);

    /// <summary>Открывает редактор группы и перечитывает список (платформенное окно).</summary>
    private partial void ExecuteShowCustomProgramsSettings();
}
