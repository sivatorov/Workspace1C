#if LINUX
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Сценарии резервирования» (Avalonia/Linux): список сценариев с действиями
/// «Выполнить» (для выбранной ИБ), «Добавить», «Изменить», «Удалить».
/// </summary>
public sealed class BackupScenariosWindow : ModalWindowBase
{
    private readonly IBackupScenarioStore _store = AppServices.GetRequiredService<IBackupScenarioStore>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly Infobase? _infobase;
    private readonly MainViewModel? _vm;
    private readonly IReadOnlyList<Infobase>? _batchBases;
    private readonly ListBox _list = new();
    private readonly Button _runButton = new();

    /// <param name="infobase">ИБ, для которой выполняется сценарий (может быть null).</param>
    /// <param name="vm">MainViewModel для выполнения сценария (может быть null).</param>
    /// <param name="batchBases">Базы мультивыделения (0.3.9.90): «Выполнить» прогоняет сценарий по всем.</param>
    public BackupScenariosWindow(
        Infobase? infobase = null,
        MainViewModel? vm = null,
        IReadOnlyList<Infobase>? batchBases = null)
    {
        _infobase = infobase;
        _vm = vm;
        _batchBases = batchBases;
        Title = T("Backup.ScenariosTitle");
        Width = 720;
        Height = 540;
        MinWidth = 560;
        MinHeight = 430;
        FontSize = 13;
        Content = BuildRoot();
        Reload();
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var dock = new DockPanel { Margin = new Avalonia.Thickness(14) };

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        _runButton.Content = T("Backup.RunTitle");
        _runButton.IsEnabled = _infobase is not null || (_batchBases is { Count: > 0 });
        _runButton.Click += async (_, _) => await RunSelectedAsync();

        var add = new Button { Content = T("Common.Add") };
        add.Click += (_, _) => AddScenario();
        var edit = new Button { Content = T("Common.Edit") };
        edit.Click += (_, _) => EditSelected();
        var del = new Button { Content = T("Common.Delete") };
        del.Click += (_, _) => DeleteSelected();
        var close = new Button { Content = T("Common.Close") };
        close.Click += (_, _) => Close();

        // Темизация кнопок окна (issue #291): стили берутся из Controls.axaml/тем
        // Light-Dark, как у остальных окон приложения, а не штатный вид Fluent.
        foreach (var b in new Control[] { _runButton, add, edit, close })
        {
            b.Styled(ControlThemes.ModernButton);
            b.Width = 100;
        }

        // Удаление сценария — опасное действие: DangerButton (P2-1).
        del.Styled(ControlThemes.DangerButton);
        del.Width = 100;

        bottom.Children.Add(_runButton);
        bottom.Children.Add(add);
        bottom.Children.Add(edit);
        bottom.Children.Add(del);
        bottom.Children.Add(close);

        _list.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        DockPanel.SetDock(bottom, Dock.Bottom);

        dock.Children.Add(bottom);
        dock.Children.Add(_list);
        return dock;
    }

    private void Reload()
    {
        _list.ItemsSource = _store.LoadAll()
            .Select(s => new BackupScenarioItemViewModel(s))
            .ToList();
    }

    private BackupScenarioItemViewModel? Selected => _list.SelectedItem as BackupScenarioItemViewModel;

    private void AddScenario()
    {
        var edit = new BackupScenarioEditWindow();
        if (edit.ShowDialogSync(this) && edit.Result is { } scenario)
        {
            _store.Save(scenario);
            Reload();
        }
    }

    private void EditSelected()
    {
        if (Selected is not { } item)
            return;
        var edit = new BackupScenarioEditWindow(item.Scenario);
        if (edit.ShowDialogSync(this) && edit.Result is { } updated)
        {
            _store.Save(updated);
            Reload();
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Backup.DeleteConfirm"), item.Name), T("Backup.DeleteScenario")))
        {
            _store.Delete(item.Id);
            Reload();
        }
    }

    private async System.Threading.Tasks.Task RunSelectedAsync()
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
}
#endif