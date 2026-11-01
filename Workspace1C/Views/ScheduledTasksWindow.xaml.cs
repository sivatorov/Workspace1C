#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Задания по расписанию» (Windows/WPF, issue #286): список заданий с действиями
/// «Выполнить сейчас», «Добавить», «Изменить», «Удалить». С 0.3.12.24 (issue #372):
/// отбор по типу действия, поиск по всем полям, переключатель активности в строке,
/// правка по F2 и удаление по DEL; нижняя кнопка «Закрыть» убрана.
/// </summary>
public partial class ScheduledTasksWindow : Window
{
    private readonly IScheduledTaskStore _store;
    private readonly IDialogService _dialogs;
    private readonly SchedulerService _scheduler;
    private readonly List<ScheduledTaskItemViewModel> _allItems = new();
    private ScheduledTaskKind? _filterKind;
    private string _searchText = "";

    public ScheduledTasksWindow()
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<IScheduledTaskStore>();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _scheduler = AppServices.GetRequiredService<SchedulerService>();

        Title = T("Schedule.Title");
        RunButton.Content = T("Schedule.RunNow");
        AddButton.Content = T("Common.Add");
        EditButton.Content = T("Common.Edit");
        CopyButton.Content = T("Common.Copy");
        DeleteButton.Content = T("Common.Delete");

        // Отображение пунктов фильтра задаёт ItemTemplate в XAML (ScheduleFilterOptionTemplate),
        // а выбор «Все типы» ставится через SelectedItem (issue #372): SelectedValuePath/
        // SelectedValue в связке с кастомным шаблоном ModernComboBox показывали в закрытом
        // поле ToString() элемента — длинную строку «KindFilterOption { Value = ..., Text = ... }»
        // вместо «Все типы». SelectionBoxItemTemplate в WPF read-only — WPF сам копирует
        // ItemTemplate в закрытую часть, а ToString() => Text — надёжный fallback.
        var filterOptions = BuildKindFilterOptions();
        KindFilterCombo.ItemsSource = filterOptions;
        KindFilterCombo.SelectedItem = filterOptions[0];
        // Подсказка поиска (placeholder через ToolTip, чтобы не мешать вводу).
        SearchBox.ToolTip = T("Schedule.Filter.Placeholder");

        LoadTasks();
    }

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

    private void LoadTasks()
    {
        _allItems.Clear();
        _allItems.AddRange(_store.LoadAll().Select(t => new ScheduledTaskItemViewModel(
            t,
            // Переключатель активности меняет расписание — планировщик должен узнать
            // об изменении сразу (issue #7), иначе задание будет ждать старого момента.
            activity: item =>
            {
                _store.Save(item.Task);
                _scheduler.Refresh();
            })));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        TasksList.ItemsSource = ScheduledTaskListFilter.Apply(_allItems, _filterKind, _searchText);
    }

    private ScheduledTaskItemViewModel? Selected => TasksList.SelectedItem as ScheduledTaskItemViewModel;

    private void TasksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        EditButton.IsEnabled = has;
        CopyButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void KindFilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _filterKind = (KindFilterCombo.SelectedItem as KindFilterOption)?.Kind;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text ?? "";
        ApplyFilter();
    }

    /// <summary>
    /// Горячие клавиши окна (issue #372, п. 4–5; комментарий 7OH): F2 — правка
    /// выбранной строки, Delete — удаление с подтверждением, Esc — закрытие окна.
    /// Первый Esc, если открыты подсказки, перехватывает глобальный ToolTipCloser
    /// (issue #270) и помечает событие обработанным — сюда Esc приходит «чистым»,
    /// и окно закрывается (как в ActualReleasesWindow и других окнах).
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            Edit_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            Delete_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Закрытие окна по Esc (issue #372, комментарий 7OH): единообразно
            // с ActualReleasesWindow/ConfigTypesEditWindow. Закрываем всегда —
            // пользователь просил именно закрытие окна по Esc.
            e.Handled = true;
            Close();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var edit = new ScheduledTaskEditWindow();
        // Модальность относительно списка заданий (issue #291): без владельца окно
        // редактирования могло оказаться под активированным извне главным окном.
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } task)
        {
            _store.Save(task);
            // Новая запись должна сразу попасть в расписание (issue #7).
            _scheduler.Refresh();
            LoadTasks();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var edit = new ScheduledTaskEditWindow(item.Task);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            // Изменённое расписание (время/дни/повтор) учитываем без перезапуска (issue #7).
            _scheduler.Refresh();
            LoadTasks();
        }
    }

    /// <summary>
    /// Копирует выбранное задание (новый Id, имя «… — копия») и открывает окно
    /// редактирования СРАЗУ (issue #370, комментарий 7OH): копия не записывается
    /// на диск — пользователь видит окно создания новой записи, заполненное данными
    /// исходной строки, и может отредактировать или отказаться (Сохранить не нажат —
    /// копия не появится). Паттерн как в «Пользовательских действиях».
    /// </summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var copy = item.Task.CreateCopy(string.Format(T("Settings.CopyOf"), item.Name));
        var edit = new ScheduledTaskEditWindow(copy);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } savedCopy)
        {
            _store.Save(savedCopy);
            _scheduler.Refresh();
            LoadTasks();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Schedule.DeleteConfirm"), item.Name), T("Schedule.DeleteTitle")))
        {
            _store.Delete(item.Id);
            // Удалённое задание убираем из расписания (issue #7).
            _scheduler.Refresh();
            LoadTasks();
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        // v100120: async void — исключение не должно уходить в глобальный обработчик
        // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
        try
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
            LoadTasks();
        }
        catch (Exception ex)
        {
            try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка запуска задания планировщика вручную", ex); } catch { /* лог не роняет обработчик */ }
        }
    }
}
#endif