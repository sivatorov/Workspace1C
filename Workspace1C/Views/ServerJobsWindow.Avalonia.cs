#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Регламентные задания сервера» (v100112, F1, Linux/Avalonia): список заданий
    /// кластера через rac «job list» с фильтром по информационной базе и действиями
    /// «Запустить сейчас»/«Перезапустить»/«Приостановить»/«Возобновить» (rac «job
    /// pause/resume/disable/enable»), обновление списка. Тонкая обёртка: вся логика —
    /// в чистой ViewModel <see cref="ServerJobsViewModel"/>; окно задаёт только открытие
    /// окна «Детали задания» (<see cref="JobDetailsWindow"/>). Собрано в коде из общих
    /// помощников <see cref="AvaloniaUi"/> — стили/темы без хардкод-цветов.
    /// </summary>
    public sealed class ServerJobsWindow : ModalWindowBase
    {
        private static readonly double[] JobColumns = { -1.8, -1.2, 140, -1.6, 150, -1.4 };

        private static readonly string[] JobColumnKeys =
        {
            "ServerMonitor.Jobs.ColName", "ServerMonitor.Jobs.ColInfobase",
            "ServerMonitor.Jobs.ColState", "ServerMonitor.Jobs.ColSchedule",
            "ServerMonitor.Jobs.ColLastStart", "ServerMonitor.Jobs.ColResult"
        };

        private readonly ServerJobsViewModel _vm;

        public ServerJobsWindow(ServerJobsViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            Title = LocalizationManager.T("ServerMonitor.Jobs.Title");
            Width = 1100;
            Height = 620;
            MinWidth = 820;
            MinHeight = 420;
            FontSize = 13;
            CanResize = true;

            // Окно «Детали задания» открывает платформенное окно (VM не знает о фреймворке).
            _vm.ShowDetails = job => new JobDetailsWindow(job.DetailsText).ShowDialog(this);

            var root = AvaloniaUi.Rows(0, -1, 0);
            root.Margin = new Thickness(16);

            // ---- Панель действий: фильтр по базе + кнопки состояния (доступность от выбора). ----
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };

            var filterLabel = AvaloniaUi.Label("ServerMonitor.Jobs.FilterInfobase");
            filterLabel.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(filterLabel);

            var filterCombo = new ComboBox
            {
                Width = 260,
                VerticalAlignment = VerticalAlignment.Center
            }.Styled(ControlThemes.ModernComboBox);
            filterCombo.Bind(SelectingItemsControl.ItemsSourceProperty, new Binding(nameof(ServerJobsViewModel.Filters)));
            filterCombo.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ServerJobsViewModel.SelectedFilter)) { Mode = BindingMode.TwoWay });
            actions.Children.Add(filterCombo);

            // «!path» — логическое отрицание в путях привязки Avalonia (кнопка активна,
            // пока нет выполняющегося rac-запроса).
            var refresh = AvaloniaUi.Button("ServerMonitor.Refresh", async (_, _) => await _vm.LoadJobsAsync(), 130);
            refresh.Bind(IsEnabledProperty, new Binding("!IsBusy") { Source = _vm });
            actions.Children.Add(refresh);

            var runNow = MakeActionButton("ServerMonitor.Jobs.RunNow", async (_, _) => await _vm.RunNowAsync());
            runNow.Bind(IsEnabledProperty, new Binding(nameof(ServerJobsViewModel.CanRunNow)));
            var restart = MakeActionButton("ServerMonitor.Jobs.Restart", async (_, _) => await _vm.RestartAsync());
            restart.Bind(IsEnabledProperty, new Binding(nameof(ServerJobsViewModel.CanRestart)));
            var pause = MakeActionButton("ServerMonitor.Jobs.Pause", async (_, _) => await _vm.ChangeStateAsync(Models.RacJobAction.Pause));
            pause.Bind(IsEnabledProperty, new Binding(nameof(ServerJobsViewModel.CanPause)));
            var resume = MakeActionButton("ServerMonitor.Jobs.Resume", async (_, _) => await _vm.ChangeStateAsync(Models.RacJobAction.Resume));
            resume.Bind(IsEnabledProperty, new Binding(nameof(ServerJobsViewModel.CanResume)));
            var details = MakeActionButton("ServerMonitor.Jobs.Details", (_, _) => _vm.DetailsCommand.Execute(null));
            foreach (var b in new[] { runNow, restart, pause, resume, details })
                actions.Children.Add(b);

            // ---- Таблица заданий: шапка + список строк (ListBox с шаблоном строки). ----
            var header = AvaloniaUi.Header(JobColumnKeys, JobColumns);
            var jobs = new ListBox { ItemsSource = _vm.VisibleJobs };
            jobs.ItemTemplate = new FuncDataTemplate<RacJobRow>((_, _) => BuildJobRow());
            jobs.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ServerJobsViewModel.SelectedJob)) { Mode = BindingMode.TwoWay });

            // ---- Строка состояния + подсказка при пустом списке. ----
            var status = AvaloniaUi.BoundText(nameof(ServerJobsViewModel.StatusText), 12);
            var emptyHint = AvaloniaUi.BoundText(nameof(ServerJobsViewModel.EmptyHint), 12);
            emptyHint.Bind(IsVisibleProperty,
                new Binding("!HasVisibleJobs") { Source = _vm });
            var statusPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };
            statusPanel.Children.Add(status);
            statusPanel.Children.Add(emptyHint);

            AvaloniaUi.Place(root, actions, 0);
            var table = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            table.Children.Add(header);
            table.Children.Add(jobs);
            AvaloniaUi.Place(root, table, 1);
            AvaloniaUi.Place(root, statusPanel, 2);

            Content = root;

            // Первичная загрузка списка после показа окна — rac вызывается асинхронно,
            // UI-поток не блокируется.
            Opened += async (_, _) => await _vm.LoadJobsAsync();
        }

        private static Button MakeActionButton(string key, EventHandler<Avalonia.Interactivity.RoutedEventArgs> onClick)
        {
            var button = AvaloniaUi.Button(key, onClick, 130);
            return button;
        }

        private static Control BuildJobRow()
        {
            var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(JobColumns), Height = 28 };
            grid.Children.Add(AvaloniaUi.CellBound(nameof(RacJobRow.Name), 0, bold: true));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(RacJobRow.InfobaseName), 1));
            var state = AvaloniaUi.CellBound(nameof(RacJobRow.StateText), 2);
            // Цвет состояния — семантическая кисть темы (без хардкод-цветов): вторичная
            // заливка по умолчанию; точный цвет состояния остаётся в WPF-версии таблицы
            // (здесь состояния различаются текстом, кнопка «Детали» даёт полный разбор).
            ThemeBrushes.Bind(state, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            grid.Children.Add(state);
            grid.Children.Add(AvaloniaUi.CellBound(nameof(RacJobRow.Schedule), 3));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(RacJobRow.LastStartText), 4));
            grid.Children.Add(AvaloniaUi.CellBound(nameof(RacJobRow.ResultText), 5));
            return grid;
        }
    }
}
#endif
