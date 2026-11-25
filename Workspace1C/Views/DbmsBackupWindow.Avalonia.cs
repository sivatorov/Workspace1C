#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Резервные копии СУБД» (выпуск 1.0.0.69, Avalonia/Linux).
    /// Логика — в <see cref="DbmsBackupViewModel"/>.
    /// </summary>
    public sealed class DbmsBackupWindow : ModalWindowBase
    {
        private readonly DbmsBackupViewModel _vm;

        public DbmsBackupWindow(DbmsBackupViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            // issue #143: сторож UI-потока — WARN в dbms-backup-debug.log, если
            // UI-поток не отвечает больше 3 с (с последним этапом операции).
            UiThreadWatchdog.EnsureStarted();
            Title = LocalizationManager.T("Dbms.Title");
            // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
            Width = 960;
            Height = 700;
            MinWidth = 760;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            // issue #143: добавлен ряд прогресса (этап, таймер, полоса).
            var root = AvaloniaUi.Rows(0, 0, 0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("Dbms.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Dbms.Base"),
                AvaloniaUi.BoundCombo(_vm.InfobaseNames, nameof(DbmsBackupViewModel.SelectedIndex), 380),
                AvaloniaUi.Label("Dbms.Dbms"),
                AvaloniaUi.BoundCombo(_vm.DbmsOptions, nameof(DbmsBackupViewModel.DbmsIndex), 200)));
            // Подсказка о неопределённом типе СУБД (issue #106, v1.0.1.73): профиль
            // больше не подставляет PostgreSQL молча — пользователь видит, чего не хватает.
            var dbmsHint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 6),
                Opacity = 0.75
            };
            dbmsHint.Bind(TextBlock.TextProperty,
                new Binding(nameof(DbmsBackupViewModel.DbmsHint)));
            form.Children.Add(dbmsHint);
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Dbms.Host"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.Host), 220),
                AvaloniaUi.Label("Dbms.Port"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.Port), 70),
                AvaloniaUi.Label("Dbms.Database"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.Database), 200)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Dbms.User"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.User), 160),
                AvaloniaUi.Label("Dbms.Password"), AvaloniaUi.BoundPassword(nameof(DbmsBackupViewModel.Password))));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Dbms.Output"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.OutputDirectory), 360),
                AvaloniaUi.Button("Common.Browse", (_, _) => Browse(true), 90),
                AvaloniaUi.Label("Dbms.Keep"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.KeepCount), 60)));
            form.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Dbms.Tools"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.ToolsDirectory), 360),
                AvaloniaUi.Button("Common.Browse", (_, _) => Browse(false), 90)));
            AvaloniaUi.Place(root, AvaloniaUi.Card(form), 1);

            var cloud = new StackPanel();
            cloud.Children.Add(AvaloniaUi.Section("Dbms.CloudSection"));
            cloud.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Dbms.WebDavUrl"),
                AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.WebDavUrl), 420)));
            cloud.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Label("Dbms.User"), AvaloniaUi.BoundTextBox(nameof(DbmsBackupViewModel.WebDavUser), 200),
                AvaloniaUi.Label("Dbms.Password"), AvaloniaUi.BoundPassword(nameof(DbmsBackupViewModel.WebDavPassword))));
            cloud.Children.Add(AvaloniaUi.Label("Dbms.CloudHint", 11));
            AvaloniaUi.Place(root, AvaloniaUi.Card(cloud), 2);

            AvaloniaUi.Place(root, BuildProgressPanel(), 3);
            AvaloniaUi.Place(root, AvaloniaUi.ReadOnlyText(nameof(DbmsBackupViewModel.LogText)), 4);

            // issue #143: проверки до запуска — подключение к СУБД и права на каталог.
            // Каждый клик фиксируется в журнале этапов ДО вызова VM, повторный вход
            // (пока IsBusy ещё не обновил привязку IsEnabled) отсекается здесь.
            var checkConnection = AvaloniaUi.Button("Dbms.CheckConnection", async (_, _) =>
            {
                DbmsBackupTrace.Log("click", "check-connection");
                if (_vm.IsBusy)
                {
                    DbmsBackupTrace.Log("click-ignored", "check-connection");
                    return;
                }
                await _vm.CheckConnectionAsync();
            }, 190);
            var checkDirectory = AvaloniaUi.Button("Dbms.CheckDirectory", async (_, _) =>
            {
                DbmsBackupTrace.Log("click", "check-directory");
                if (_vm.IsBusy)
                {
                    DbmsBackupTrace.Log("click-ignored", "check-directory");
                    return;
                }
                await _vm.CheckDirectoryAsync();
            }, 180);
            // issue #143: сохранение — асинхронно и вне UI-потока; окно не «наглухо»
            // зависает, если профиль лежит на медленном/недоступном сетевом пути.
            var save = AvaloniaUi.Button("Common.Save", async (_, _) =>
            {
                DbmsBackupTrace.Log("click", "save");
                if (_vm.IsBusy)
                {
                    DbmsBackupTrace.Log("click-ignored", "save");
                    return;
                }
                await _vm.SaveAsync();
            }, 120);
            var run = AvaloniaUi.Button("Dbms.RunNow", async (_, _) =>
            {
                DbmsBackupTrace.Log("click", "run");
                if (_vm.IsBusy)
                {
                    DbmsBackupTrace.Log("click-ignored", "run");
                    return;
                }
                await _vm.RunAsync();
            }, 190, accent: true);
            foreach (var b in new[] { checkConnection, checkDirectory, save, run })
                b.Bind(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsIdle)));
            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(checkConnection, checkDirectory, save, run, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 5);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
            // issue #143: окно открывается сразу. Профиль (если не передан) и тип
            // СУБД по кластеру догружаются в фоне — никакой синхронной файловой
            // работы на UI-потоке, окно не «вешается» на старом/сетевом профиле.
            _ = InitializeVmAsync();
            if (!_vm.HasBases)
                _vm.AppendLog(LocalizationManager.T("Dbms.NoBases"));
        }

        /// <summary>
        /// Фоновая догрузка профиля и уточнение типа СУБД (issue #143). Ошибки не
        /// должны ронять окно — только диагностика в журнале.
        /// </summary>
        private async Task InitializeVmAsync()
        {
            try
            {
                await _vm.InitializeAsync();
                await _vm.ResolveDbmsFromClusterAsync();
            }
            catch (OperationCanceledException)
            {
                // Окно закрыли во время загрузки — это нормально.
            }
            catch (Exception ex)
            {
                _vm.AppendLog(ex.Message);
            }
        }

        /// <summary>
        /// Ряд прогресса операции (issue #143): этап, прошедшее время и полоса.
        /// Полоса «бежит» на этапах без измеримого прогресса (дамп, выгрузка).
        /// </summary>
        private StackPanel BuildProgressPanel()
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var stage = AvaloniaUi.BoundText(nameof(DbmsBackupViewModel.ProgressStageText), 12);
            var elapsed = AvaloniaUi.BoundText(nameof(DbmsBackupViewModel.ElapsedText), 11);
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
            bar.Bind(ProgressBar.IsIndeterminateProperty,
                new Binding(nameof(DbmsBackupViewModel.IsProgressIndeterminate)));
            bar.Bind(ProgressBar.ValueProperty,
                new Binding(nameof(DbmsBackupViewModel.ProgressPercent)) { Mode = BindingMode.OneWay });

            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(header);
            panel.Children.Add(bar);
            // issue #143: в покое ряд прогресса скрыт — «бегущая жёлтая полоса»
            // больше не анимируется постоянно и не выглядит как зависшая операция.
            panel.Bind(Visual.IsVisibleProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy)));
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
