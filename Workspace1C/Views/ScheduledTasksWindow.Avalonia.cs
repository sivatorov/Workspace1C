#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Задания по расписанию» (Avalonia/Linux, issue #286): список заданий с действиями
/// «Выполнить сейчас», «Добавить», «Изменить», «Удалить». С 0.3.12.24 (issue #372):
/// отбор по типу действия, поиск по всем полям, переключатель активности в строке,
/// правка по F2 и удаление по DEL; нижняя кнопка «Закрыть» убрана. Закрытие по Esc
/// (issue #372, комментарий 7OH) обеспечивает база ModalWindowBase: первый Esc закрывает
/// открытые подсказки, повторный — окно (CloseAsCancel, issues #226/#270).
/// </summary>
public sealed class ScheduledTasksWindow : ModalWindowBase
{
    private readonly IScheduledTaskStore _store = AppServices.GetRequiredService<IScheduledTaskStore>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly SchedulerService _scheduler = AppServices.GetRequiredService<SchedulerService>();
    private readonly ListBox _list = new();
    private readonly TextBox _searchBox = new TextBox();
    private readonly ComboBox _kindFilter = new ComboBox();
    private readonly List<ScheduledTaskItemViewModel> _allItems = new();
    private ScheduledTaskKind? _filterKind;
    private string _searchText = "";

    public ScheduledTasksWindow()
    {
        Title = T("Schedule.Title");
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 440;
        FontSize = 13;
        Content = BuildRoot();
        Reload();
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Вариант фильтра по типу: «Все типы» плюс каждый тип задания.</summary>
    private sealed record KindFilterOption(string Value, string Text, ScheduledTaskKind? Kind)
    {
        /// <summary>Отображение в закрытом комбобоксе без явной привязки (issue #372).</summary>
        public override string ToString() => Text;
    }

    private static IReadOnlyList<KindFilterOption> BuildKindFilterOptions()
    {
        var options = new List<KindFilterOption> { new("", T("Schedule.Filter.AllKinds"), null) };
        foreach (ScheduledTaskKind kind in Enum.GetValues(typeof(ScheduledTaskKind)))
            options.Add(new(kind.ToString(), ScheduledTaskItemViewModel.KindName(kind), kind));
        return options;
    }

    private Control BuildRoot()
    {
        var dock = new DockPanel { Margin = new Avalonia.Thickness(14) };

        // Панель фильтров (issue #372, п. 1–2): отбор по типу и поиск по всем полям.
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };
        _kindFilter.Width = 240;
        var filterOptions = BuildKindFilterOptions();
        _kindFilter.ItemsSource = filterOptions;
        _kindFilter.ItemsPanel = null;
        // Шаблон пункта фильтра (issue #372): один и тот же шаблон идёт и в раскрытый список,
        // и в закрытую часть (SelectionBoxItemTemplate) — иначе закрытый комбобокс рендерит
        // ToString() элемента («KindFilterOption { Value = ..., Text = ... }») вместо названия.
        var filterTemplate = new FuncDataTemplate<KindFilterOption>((item, _) =>
            new TextBlock { Text = item?.Text ?? string.Empty });
        _kindFilter.ItemTemplate = filterTemplate;
        _kindFilter.SelectionBoxItemTemplate = filterTemplate;
        _kindFilter.SelectedItem = filterOptions.FirstOrDefault();
        _kindFilter.SelectionChanged += (_, _) =>
        {
            _filterKind = (_kindFilter.SelectedItem as KindFilterOption)?.Kind;
            ApplyFilter();
        };
        _searchBox.Width = 320;
        ToolTip.SetTip(_searchBox, T("Schedule.Filter.Placeholder"));
        _searchBox.TextChanged += (_, _) =>
        {
            _searchText = _searchBox.Text ?? "";
            ApplyFilter();
        };
        _searchBox.Styled(ControlThemes.ModernTextBox);
        _kindFilter.Styled(ControlThemes.ModernComboBox);
        filterRow.Children.Add(_kindFilter);
        filterRow.Children.Add(_searchBox);
        DockPanel.SetDock(filterRow, Dock.Top);
        dock.Children.Add(filterRow);

        // Нижние кнопки: «Закрыть» убрана (issue #372, п. 3) — закрытие системное / Esc.
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        var run = new Button { Content = T("Schedule.RunNow"), Width = 130 };
        run.Click += async (_, _) => await RunSelectedAsync();
        var add = new Button { Content = T("Common.Add"), Width = 96 };
        add.Click += (_, _) => AddTask();
        var edit = new Button { Content = T("Common.Edit"), Width = 96 };
        edit.Click += (_, _) => EditSelected();
        var copy = new Button { Content = T("Common.Copy"), Width = 96 };
        copy.Click += (_, _) => CopySelected();
        var del = new Button { Content = T("Common.Delete"), Width = 92 };
        del.Click += (_, _) => DeleteSelected();

        // Темизация кнопок окна (issue #291): стили берутся из Controls.axaml/тем
        // Light-Dark, как у остальных окон приложения, а не штатный вид Fluent.
        foreach (var b in new Control[] { run, add, edit, copy })
        {
            b.Styled(ControlThemes.ModernButton);
            b.MinWidth = b.Width;
        }

        // Удаление задания — опасное действие: DangerButton (P2-1).
        del.Styled(ControlThemes.DangerButton);
        del.MinWidth = del.Width;

        bottom.Children.Add(run);
        bottom.Children.Add(add);
        bottom.Children.Add(edit);
        bottom.Children.Add(copy);
        bottom.Children.Add(del);

        // Строка списка (issue #372, п. 6): переключатель активности справа.
        _list.ItemTemplate = new FuncDataTemplate<ScheduledTaskItemViewModel>((vm, _) => BuildListItem(vm));
        _list.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        _list.KeyDown += List_KeyDown;

        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(_list);
        return dock;
    }

    /// <summary>
    /// Горячие клавиши списка (issue #372, п. 4–5): F2 — правка выбранной строки,
    /// Delete — удаление с подтверждением. Esc здесь не обрабатывается: закрытие
    /// окна по Esc даёт база ModalWindowBase (см. комментарий класса).
    /// </summary>
    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            EditSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private Control BuildListItem(ScheduledTaskItemViewModel vm)
    {
        var name = new TextBlock { Text = vm.Name, FontWeight = FontWeight.SemiBold };
        ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        var kind = new TextBlock { Text = vm.KindText };
        var summary = new TextBlock { Text = vm.ScheduleSummary };
        var last = new TextBlock { Text = vm.LastRunSummary, TextTrimming = TextTrimming.CharacterEllipsis };
        ThemeBrushes.Bind(kind, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        ThemeBrushes.Bind(summary, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        ThemeBrushes.Bind(last, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");

        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        kindRow.Children.Add(kind);
        kindRow.Children.Add(summary);

        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(name);
        body.Children.Add(kindRow);
        body.Children.Add(last);

        var active = new CheckBox { IsChecked = vm.IsActive, VerticalAlignment = VerticalAlignment.Center };
        active.Styled(ControlThemes.CacheCleanCheckBox);
        active.IsCheckedChanged += (_, _) =>
        {
            // Изменение активности сразу сохраняется (issue #372, п. 6); планировщик
            // пересчитывает расписание, чтобы учесть включение/выключение (issue #7).
            vm.SetActive(active.IsChecked == true);
            _store.Save(vm.Task);
            _scheduler.Refresh();
        };

        var row = new DockPanel { Margin = new Avalonia.Thickness(3) };
        DockPanel.SetDock(active, Dock.Right);
        row.Children.Add(active);
        row.Children.Add(body);
        return row;
    }

    private void Reload()
    {
        _allItems.Clear();
        _allItems.AddRange(_store.LoadAll()
            .Select(t => new ScheduledTaskItemViewModel(t)));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _list.ItemsSource = ScheduledTaskListFilter.Apply(_allItems, _filterKind, _searchText);
    }

    private ScheduledTaskItemViewModel? Selected => _list.SelectedItem as ScheduledTaskItemViewModel;

    private void AddTask()
    {
        var edit = new ScheduledTaskEditWindow();
        if (edit.ShowDialogSync(this) && edit.Result is { } task)
        {
            _store.Save(task);
            // Новая запись должна сразу попасть в расписание (issue #7).
            _scheduler.Refresh();
            Reload();
        }
    }

    private void EditSelected()
    {
        if (Selected is not { } item)
            return;
        var edit = new ScheduledTaskEditWindow(item.Task);
        if (edit.ShowDialogSync(this) && edit.Result is { } updated)
        {
            _store.Save(updated);
            // Изменённое расписание (время/дни/повтор) учитываем без перезапуска (issue #7).
            _scheduler.Refresh();
            Reload();
        }
    }

    /// <summary>
    /// Копирует выбранное задание (новый Id, имя «… — копия») и открывает окно
    /// редактирования СРАЗУ (issue #370, комментарий 7OH): копия не записывается
    /// на диск — пользователь видит окно создания новой записи, заполненное данными
    /// исходной строки, и может отредактировать или отказаться (Сохранить не нажат —
    /// копия не появится).
    /// </summary>
    private void CopySelected()
    {
        if (Selected is not { } item)
            return;
        var copy = item.Task.CreateCopy(string.Format(T("Settings.CopyOf"), item.Name));
        var edit = new ScheduledTaskEditWindow(copy);
        if (edit.ShowDialogSync(this) && edit.Result is { } savedCopy)
        {
            _store.Save(savedCopy);
            _scheduler.Refresh();
            Reload();
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Schedule.DeleteConfirm"), item.Name), T("Schedule.DeleteTitle")))
        {
            _store.Delete(item.Id);
            // Удалённое задание убираем из расписания (issue #7).
            _scheduler.Refresh();
            Reload();
        }
    }

    private async System.Threading.Tasks.Task RunSelectedAsync()
    {
        if (Selected is not { } item)
            return;
        var result = await _scheduler.RunNowAsync(item.Task);
        if (result is not null)
        {
            if (result.Success)
                _dialogs.ShowInfo(string.Join("\n", result.CreatedFiles), T("Schedule.RunNow"));
            else
                _dialogs.ShowError(result.ErrorMessage ?? T("Schedule.RunFailed"), T("Schedule.RunNow"));
        }
        Reload();
    }
}
#endif