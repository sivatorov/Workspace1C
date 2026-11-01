#if WINDOWS
using System.Linq;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Сценарии резервирования» (Windows/WPF): список сценариев с действиями
/// «Выполнить» (для выбранной ИБ), «Добавить», «Изменить», «Удалить».
/// </summary>
public partial class BackupScenariosWindow : Window
{
    private readonly IBackupScenarioStore _store;
    private readonly IDialogService _dialogs;
    private readonly Infobase? _infobase;
    private readonly MainViewModel? _vm;
    private readonly IReadOnlyList<Infobase>? _batchBases;

    /// <param name="infobase">ИБ, для которой выполняется сценарий (может быть null).</param>
    /// <param name="vm">MainViewModel для выполнения сценария (может быть null).</param>
    /// <param name="batchBases">Базы мультивыделения (0.3.9.90): «Выполнить» прогоняет сценарий по всем.</param>
    public BackupScenariosWindow(
        Infobase? infobase = null,
        MainViewModel? vm = null,
        IReadOnlyList<Infobase>? batchBases = null)
    {
        InitializeComponent();
        _infobase = infobase;
        _vm = vm;
        _batchBases = batchBases;
        _store = AppServices.GetRequiredService<IBackupScenarioStore>();
        _dialogs = AppServices.GetRequiredService<IDialogService>();

        Title = T("Backup.ScenariosTitle");
        RunButton.Content = T("Backup.RunTitle");
        AddButton.Content = T("Common.Add");
        EditButton.Content = T("Common.Edit");
        DeleteButton.Content = T("Common.Delete");
        RunButton.IsEnabled = _infobase is not null || (_batchBases is { Count: > 0 });

        LoadScenarios();
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void LoadScenarios()
    {
        ScenariosList.ItemsSource = _store.LoadAll()
            .Select(s => new BackupScenarioItemViewModel(s))
            .ToList();
    }

    private BackupScenarioItemViewModel? Selected => ScenariosList.SelectedItem as BackupScenarioItemViewModel;

    private void ScenariosList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        EditButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var edit = new BackupScenarioEditWindow();
        // Модальность относительно списка сценариев (issue #291): без владельца окно
        // редактирования могло оказаться под активированным извне главным окном.
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } scenario)
        {
            _store.Save(scenario);
            LoadScenarios();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var edit = new BackupScenarioEditWindow(item.Scenario);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            LoadScenarios();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Backup.DeleteConfirm"), item.Name), T("Backup.DeleteScenario")))
        {
            _store.Delete(item.Id);
            LoadScenarios();
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        // v100120: async void — исключение не должно уходить в глобальный обработчик
        // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
        try
        {
            if (Selected is not { } item || _vm is null)
                return;
            if (_batchBases is { Count: > 0 })
            {
                foreach (var ib in _batchBases)
                    await _vm.RunBackupAsync(ib, item.Scenario);
                return;
            }
            if (_infobase is null)
                return;
            await _vm.RunBackupAsync(_infobase, item.Scenario);
        }
        catch (Exception ex)
        {
            try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка запуска сценария резервирования", ex); } catch { /* лог не роняет обработчик */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif