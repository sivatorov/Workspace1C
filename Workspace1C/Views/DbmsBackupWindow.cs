#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Резервные копии СУБД» (выпуск 1.0.0.69, Windows/WPF).
    /// Логика — в <see cref="DbmsBackupViewModel"/>.
    /// </summary>
    public sealed class DbmsBackupWindow : Window
    {
        private readonly DbmsBackupViewModel _vm;

        public DbmsBackupWindow(DbmsBackupViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Dbms.Title", 960, 760);

            // issue #143: добавлен ряд прогресса (этап, таймер, полоса).
            var root = WpfUi.Rows(0, 0, 0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("Dbms.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Dbms.Base"),
                WpfUi.BoundCombo(_vm.InfobaseNames, nameof(DbmsBackupViewModel.SelectedIndex), 380),
                WpfUi.Label("Dbms.Dbms"),
                WpfUi.BoundCombo(_vm.DbmsOptions, nameof(DbmsBackupViewModel.DbmsIndex), 200)));
            // Подсказка о неопределённом типе СУБД (issue #106, v1.0.1.73): профиль
            // больше не подставляет PostgreSQL молча — пользователь видит, чего не хватает.
            var dbmsHint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 6)
            };
            dbmsHint.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(DbmsBackupViewModel.DbmsHint)));
            form.Children.Add(dbmsHint);
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("Dbms.Host"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.Host), 220),
                WpfUi.Label("Dbms.Port"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.Port), 70),
                WpfUi.Label("Dbms.Database"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.Database), 200)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("Dbms.User"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.User), 160),
                WpfUi.Label("Dbms.Password"),
                WpfUi.BoundPassword(_vm, nameof(DbmsBackupViewModel.Password), () => _vm.Password, v => _vm.Password = v)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("Dbms.Output"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.OutputDirectory), 360),
                WpfUi.Button("Common.Browse", (_, _) => Browse(true), 90),
                WpfUi.Label("Dbms.Keep"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.KeepCount), 60)));
            form.Children.Add(WpfUi.Wrap(
                WpfUi.Label("Dbms.Tools"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.ToolsDirectory), 360),
                WpfUi.Button("Common.Browse", (_, _) => Browse(false), 90)));
            WpfUi.Place(root, WpfUi.Card(form), 1);

            var cloud = new StackPanel();
            cloud.Children.Add(WpfUi.Section("Dbms.CloudSection"));
            cloud.Children.Add(WpfUi.Wrap(WpfUi.Label("Dbms.WebDavUrl"),
                WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.WebDavUrl), 420)));
            cloud.Children.Add(WpfUi.Wrap(
                WpfUi.Label("Dbms.User"), WpfUi.BoundTextBox(nameof(DbmsBackupViewModel.WebDavUser), 200),
                WpfUi.Label("Dbms.Password"),
                WpfUi.BoundPassword(_vm, nameof(DbmsBackupViewModel.WebDavPassword), () => _vm.WebDavPassword, v => _vm.WebDavPassword = v)));
            cloud.Children.Add(WpfUi.Label("Dbms.CloudHint", 11));
            WpfUi.Place(root, WpfUi.Card(cloud), 2);

            WpfUi.Place(root, BuildProgressPanel(), 3);
            WpfUi.Place(root, WpfUi.ReadOnlyText(nameof(DbmsBackupViewModel.LogText)), 4);

            // issue #143: проверки до запуска — подключение к СУБД и права на каталог.
            var checkConnection = WpfUi.Button("Dbms.CheckConnection",
                async (_, _) => await _vm.CheckConnectionAsync(), 190);
            var checkDirectory = WpfUi.Button("Dbms.CheckDirectory",
                async (_, _) => await _vm.CheckDirectoryAsync(), 180);
            // issue #143: сохранение — асинхронно и вне UI-потока; окно не «наглухо»
            // зависает, если профиль лежит на медленном/недоступном сетевом пути.
            var save = WpfUi.Button("Common.Save", async (_, _) => await _vm.SaveAsync(), 120);
            var run = WpfUi.Button("Dbms.RunNow", async (_, _) => await _vm.RunAsync(), 190, accent: true);
            foreach (var b in new[] { checkConnection, checkDirectory, save, run })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsIdle)));
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(checkConnection, checkDirectory, save, run, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 5);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
            // Тип СУБД уточняется по кластеру 1С в фоне (issue #106, v1.0.1.73):
            // сохранённый профиль и выбор пользователя приоритетнее. Запуск через
            // Task.Run (issue #143): синхронная часть резолвера не должна выполняться
            // на UI-потоке и блокировать окно.
            _ = Task.Run(() => _vm.ResolveDbmsFromClusterAsync());
            if (!_vm.HasBases)
                _vm.AppendLog(LocalizationManager.T("Dbms.NoBases"));
        }

        /// <summary>
        /// Ряд прогресса операции (issue #143): этап, прошедшее время и полоса.
        /// Полоса «бежит» на этапах без измеримого прогресса (дамп, выгрузка).
        /// </summary>
        private StackPanel BuildProgressPanel()
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var stage = WpfUi.BoundText(nameof(DbmsBackupViewModel.ProgressStageText), 12);
            var elapsed = WpfUi.BoundText(nameof(DbmsBackupViewModel.ElapsedText), 11);
            elapsed.Margin = new Thickness(12, 0, 0, 0);
            header.Children.Add(stage);
            header.Children.Add(elapsed);

            var bar = new ProgressBar
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Margin = new Thickness(0, 4, 0, 0)
            };
            bar.SetBinding(ProgressBar.IsIndeterminateProperty,
                new Binding(nameof(DbmsBackupViewModel.IsProgressIndeterminate)));
            bar.SetBinding(ProgressBar.ValueProperty,
                new Binding(nameof(DbmsBackupViewModel.ProgressPercent)) { Mode = BindingMode.OneWay });

            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(header);
            panel.Children.Add(bar);
            // issue #143: в покое ряд прогресса скрыт — «бегущая жёлтая полоса»
            // больше не анимируется постоянно и не выглядит как зависшая операция.
            panel.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy))
            {
                Converter = new BooleanToVisibilityConverter()
            });
            return panel;
        }

        private void Browse(bool output)
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var folder = dialogs.OpenFolderDialog(LocalizationManager.T(output ? "Dbms.Output" : "Dbms.Tools"),
                output ? _vm.OutputDirectory : _vm.ToolsDirectory);
            if (string.IsNullOrWhiteSpace(folder))
                return;
            if (output) _vm.OutputDirectory = folder; else _vm.ToolsDirectory = folder;
        }
    }
}
#endif
