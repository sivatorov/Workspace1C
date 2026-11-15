using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Редактор группы «Мои программы» (issue #91): список пользовательских программ с
/// добавлением/удалением и перемещением вверх-вниз, поля имени, исполняемого файла,
/// аргументов, рабочей папки и иконки из файла, а также переключатель отображения
/// группы «списком / иконками». Чистый .NET — используется обеими платформами
/// (WPF и Avalonia).
/// </summary>
public class CustomProgramsViewModel : ViewModelBase
{
    private readonly ICustomProgramsStore? _store;
    private CustomProgram? _selectedProgram;
    private string _viewMode;

    /// <param name="store">Хранилище программ (может быть null — редактирование без сохранения).</param>
    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    public CustomProgramsViewModel(ICustomProgramsStore? store, string? viewMode)
    {
        _store = store;
        _viewMode = CustomProgramsViewModes.Normalize(viewMode);
        Programs = new ObservableCollection<CustomProgram>(
            store?.LoadAll() ?? (IReadOnlyList<CustomProgram>)Array.Empty<CustomProgram>());

        AddCommand = new RelayCommand(_ => AddProgram());
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedProgram is not null);
        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
        ClearIconCommand = new RelayCommand(
            _ => ClearSelectedIcon(), _ => SelectedProgram is not null);

        if (Programs.Count > 0)
            SelectedProgram = Programs[0];
    }

    /// <summary>Программы в порядке отображения на панели запуска.</summary>
    public ObservableCollection<CustomProgram> Programs { get; }

    /// <summary>Выбранная программа (объект правки в форме редактора).</summary>
    public CustomProgram? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            if (!SetProperty(ref _selectedProgram, value))
                return;
            OnPropertyChanged(nameof(HasSelection));
            RaiseCommandsCanExecuteChanged();
        }
    }

    /// <summary>Выбрана ли программа (включение кнопок удаления/перемещения).</summary>
    public bool HasSelection => SelectedProgram is not null;

    /// <summary>
    /// Режим отображения группы: <see cref="CustomProgramsViewModes.List"/> либо
    /// <see cref="CustomProgramsViewModes.Icons"/>; сохраняется в настройках вызывающим кодом.
    /// </summary>
    public string ViewMode
    {
        get => _viewMode;
        set
        {
            var normalized = CustomProgramsViewModes.Normalize(value);
            if (!SetProperty(ref _viewMode, normalized))
                return;
            OnPropertyChanged(nameof(IsListViewMode));
            OnPropertyChanged(nameof(IsIconsViewMode));
        }
    }

    /// <summary>Переключатель «Списком» (радиокнопка редактора).</summary>
    public bool IsListViewMode
    {
        get => _viewMode == CustomProgramsViewModes.List;
        set
        {
            if (value)
                ViewMode = CustomProgramsViewModes.List;
        }
    }

    /// <summary>Переключатель «Иконками» (радиокнопка редактора).</summary>
    public bool IsIconsViewMode
    {
        get => _viewMode == CustomProgramsViewModes.Icons;
        set
        {
            if (value)
                ViewMode = CustomProgramsViewModes.Icons;
        }
    }

    /// <summary>Добавить программу.</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>Удалить выбранную программу.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Переместить выбранную программу выше.</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>Переместить выбранную программу ниже.</summary>
    public RelayCommand MoveDownCommand { get; }

    /// <summary>Сбросить иконку выбранной программы.</summary>
    public RelayCommand ClearIconCommand { get; }

    /// <summary>Можно ли переместить выбранную программу на <paramref name="delta"/> позиций.</summary>
    public bool CanMoveSelected(int delta)
    {
        if (SelectedProgram is null)
            return false;
        var index = Programs.IndexOf(SelectedProgram);
        if (index < 0)
            return false;
        var target = index + delta;
        return target >= 0 && target < Programs.Count;
    }

    /// <summary>Добавляет программу с именем по умолчанию и выбирает её.</summary>
    public void AddProgram()
    {
        var program = new CustomProgram
        {
            Name = LocalizationManager.T("Launch.CustomPrograms.NewProgram"),
            Order = Programs.Count
        };
        Programs.Add(program);
        SelectedProgram = program;
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Удаляет выбранную программу и выбирает соседнюю.</summary>
    public void DeleteSelected()
    {
        if (SelectedProgram is not { } program)
            return;
        var index = Programs.IndexOf(program);
        Programs.Remove(program);
        SelectedProgram = Programs.Count == 0
            ? null
            : Programs[Math.Min(Math.Max(index, 0), Programs.Count - 1)];
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Перемещает выбранную программу на <paramref name="delta"/> позиций.</summary>
    public void MoveSelected(int delta)
    {
        if (SelectedProgram is not { } program)
            return;
        var index = Programs.IndexOf(program);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Programs.Count)
            return;
        Programs.Move(index, target);
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Сбрасывает иконку выбранной программы.</summary>
    public void ClearSelectedIcon()
    {
        if (SelectedProgram is not { } program)
            return;
        program.IconPath = string.Empty;
    }

    /// <summary>
    /// Проверяет обязательные поля (имя и исполняемый файл) и сохраняет список в
    /// хранилище. При ошибке возвращает <c>false</c> и сообщение для пользователя
    /// (локализованный ключ с подстановкой имени программы).
    /// </summary>
    public bool TrySave(out string error)
    {
        error = string.Empty;
        foreach (var program in Programs)
        {
            if (string.IsNullOrWhiteSpace(program.Name))
            {
                error = LocalizationManager.T("Launch.CustomPrograms.ErrorNameRequired");
                return false;
            }
            if (string.IsNullOrWhiteSpace(program.FilePath))
            {
                error = string.Format(
                    LocalizationManager.T("Launch.CustomPrograms.ErrorFileRequiredFor"),
                    program.Name);
                return false;
            }
        }

        // Порядок — по позиции в списке редактора.
        for (var i = 0; i < Programs.Count; i++)
            Programs[i].Order = i;

        try
        {
            _store?.SaveAll(Programs);
        }
        catch (Exception ex)
        {
            error = string.Format(
                LocalizationManager.T("Launch.CustomPrograms.ErrorSave"), ex.Message);
            return false;
        }
        return true;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        DeleteCommand?.RaiseCanExecuteChanged();
        MoveUpCommand?.RaiseCanExecuteChanged();
        MoveDownCommand?.RaiseCanExecuteChanged();
        ClearIconCommand?.RaiseCanExecuteChanged();
    }
}
