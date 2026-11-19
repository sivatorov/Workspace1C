#if LINUX
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Пользовательские действия» (Avalonia/Linux): список действий контекстного меню
/// (функция 7, цикл 0.3.9.193–199) с действиями «Добавить», «Изменить», «Удалить».
/// После закрытия окна вызывающий код (MainViewModel) перечитывает кэш действий
/// (<see cref="ViewModels.MainViewModel.ReloadCustomActions"/>), поэтому подменю
/// актуализируется при следующем открытии.
/// </summary>
public sealed class CustomActionsWindow : ModalWindowBase
{
    private readonly ICustomActionsStore _store = AppServices.GetRequiredService<ICustomActionsStore>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly ListBox _list = new();
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new();

    public CustomActionsWindow()
    {
        Title = T("CustomAction.WindowTitle");
        Width = 760;
        Height = 540;
        MinWidth = 600;
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
        add.Click += (_, _) => AddAction();
        _editButton.Content = T("Common.Edit");
        _editButton.Click += (_, _) => EditSelected();
        _deleteButton.Content = T("Common.Delete");
        _deleteButton.Click += (_, _) => DeleteSelected();
        var close = new Button { Content = T("Common.Close") };
        close.Click += (_, _) => Close();

        // Темизация кнопок окна (issue #291): стили берутся из Controls.axaml/тем
        // Light-Dark, как у остальных окон приложения.
        foreach (var b in new Control[] { add, _editButton, close })
        {
            b.Styled(ControlThemes.ModernButton);
            b.Width = 96;
        }

        // Удаление действия — опасное действие: DangerButton (P2-1).
        _deleteButton.Styled(ControlThemes.DangerButton);
        _deleteButton.Width = 96;

        bottom.Children.Add(add);
        bottom.Children.Add(_editButton);
        bottom.Children.Add(_deleteButton);
        bottom.Children.Add(close);

        _list.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _list.DoubleTapped += (_, e) =>
        {
            if (e.Source is not null && Selected is not null)
                EditSelected();
        };
        // Отображение полей строки: имя — полужирным, ниже — область, хоткей и превью команды.
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CustomActionItemViewModel>((item, _) =>
        {
            var name = new TextBlock { Text = item.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold };
            var scope = new TextBlock { Text = item.ScopeDisplay };
            var hotkey = new TextBlock { Text = item.Hotkey };
            var command = new TextBlock
            {
                Text = item.CommandPreview,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };
            foreach (var secondary in new[] { scope, hotkey, command })
                ThemeBrushes.Bind(secondary, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            return new StackPanel
            {
                Margin = new Avalonia.Thickness(3),
                Spacing = 3,
                Children =
                {
                    name,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children = { scope, hotkey }
                    },
                    command
                }
            };
        });
        DockPanel.SetDock(bottom, Dock.Bottom);

        dock.Children.Add(bottom);
        dock.Children.Add(_list);
        return dock;
    }

    /// <summary>
    /// Перечитывает список действий из хранилища. Порядок задаёт хранилище (сортировка
    /// по имени); выделение восстанавливается по Id — после редактирования строки выбор
    /// не сбрасывается, как требует логика окна настроек.
    /// </summary>
    private void Reload()
    {
        var selectedId = Selected?.Id;
        _list.ItemsSource = _store.LoadAll()
            .Select(a => new CustomActionItemViewModel(a, edit: OnEditItem, delete: OnDeleteItem))
            .ToList();

        if (selectedId is not null)
        {
            foreach (var obj in _list.Items)
            {
                if (obj is CustomActionItemViewModel vm && vm.Id == selectedId)
                {
                    _list.SelectedItem = vm;
                    break;
                }
            }
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = Selected is not null;
        _editButton.IsEnabled = has;
        _deleteButton.IsEnabled = has;
    }

    private CustomActionItemViewModel? Selected => _list.SelectedItem as CustomActionItemViewModel;

    private void AddAction()
    {
        // Существующие действия передаются в редактор для валидации конфликтов хоткеев (0.3.9.198).
        var edit = new CustomActionEditWindow(null, _store.LoadAll());
        if (edit.ShowDialogSync(this) && edit.Result is { } created)
        {
            _store.Save(created);
            Reload();
        }
    }

    private void EditSelected()
    {
        if (Selected is { } item)
            OnEditItem(item);
    }

    /// <summary>Открывает редактор действия (кнопка «Изменить», двойной клик, колбэк строки).</summary>
    private void OnEditItem(CustomActionItemViewModel item)
    {
        if (item is null)
            return;
        var edit = new CustomActionEditWindow(item.Action, _store.LoadAll());
        if (edit.ShowDialogSync(this) && edit.Result is { } updated)
        {
            _store.Save(updated);
            Reload();
        }
    }

    private void DeleteSelected()
    {
        if (Selected is { } item)
            OnDeleteItem(item);
    }

    /// <summary>Удаляет действие с подтверждением (кнопка «Удалить», колбэк строки).</summary>
    private void OnDeleteItem(CustomActionItemViewModel item)
    {
        if (item is null)
            return;
        if (_dialogs.Confirm(string.Format(T("CustomAction.DeleteConfirm"), item.Name), T("CustomAction.DeleteTitle")))
        {
            _store.Delete(item.Id);
            Reload();
        }
    }
}
#endif