#if LINUX
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Настройка сценариев» (Avalonia/Linux): список сценариев запуска скриптов
/// с действиями «Добавить», «Изменить», «Удалить» (issue #308).
/// </summary>
public sealed class ScriptScenariosWindow : ModalWindowBase
{
    private readonly IScriptScenarioStore _store = AppServices.GetRequiredService<IScriptScenarioStore>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly ListBox _list = new();

    public ScriptScenariosWindow()
    {
        Title = T("Script.WindowTitle");
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
        var add = new Button { Content = T("Common.Add") };
        add.Click += (_, _) => AddScenario();
        var edit = new Button { Content = T("Common.Edit") };
        edit.Click += (_, _) => EditSelected();
        var del = new Button { Content = T("Common.Delete") };
        del.Click += (_, _) => DeleteSelected();
        var close = new Button { Content = T("Common.Close") };
        close.Click += (_, _) => Close();

        // Темизация кнопок окна (issue #291): стили берутся из Controls.axaml/тем
        // Light-Dark, как у остальных окон приложения.
        foreach (var b in new Control[] { add, edit, close })
        {
            b.Styled(ControlThemes.ModernButton);
            b.Width = 96;
        }

        // Удаление сценария — опасное действие: DangerButton (P2-1).
        del.Styled(ControlThemes.DangerButton);
        del.Width = 96;

        bottom.Children.Add(add);
        bottom.Children.Add(edit);
        bottom.Children.Add(del);
        bottom.Children.Add(close);

        _list.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        _list.DoubleTapped += (_, e) =>
        {
            if (e.Source is not null && Selected is not null)
                EditSelected();
        };
        DockPanel.SetDock(bottom, Dock.Bottom);

        dock.Children.Add(bottom);
        dock.Children.Add(_list);
        return dock;
    }

    private void Reload()
    {
        _list.ItemsSource = _store.LoadAll()
            .Select(s => new ScriptScenarioItemViewModel(s))
            .ToList();
    }

    private ScriptScenarioItemViewModel? Selected => _list.SelectedItem as ScriptScenarioItemViewModel;

    private void AddScenario()
    {
        var edit = new ScriptScenarioEditWindow();
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
        var edit = new ScriptScenarioEditWindow(item.Scenario);
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
        if (_dialogs.Confirm(string.Format(T("Script.DeleteConfirm"), item.Name), T("Script.DeleteTitle")))
        {
            _store.Delete(item.Id);
            Reload();
        }
    }
}
#endif