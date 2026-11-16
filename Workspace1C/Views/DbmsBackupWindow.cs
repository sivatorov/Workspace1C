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

            var root = WpfUi.Rows(0, 0, 0, -1, 0);
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

            WpfUi.Place(root, WpfUi.ReadOnlyText(nameof(DbmsBackupViewModel.LogText)), 3);

            var save = WpfUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var run = WpfUi.Button("Dbms.RunNow", async (_, _) => await _vm.RunAsync(), 190, accent: true);
            foreach (var b in new[] { save, run })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsIdle)));
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(save, run, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 4);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
            // Тип СУБД уточняется по кластеру 1С в фоне (issue #106, v1.0.1.73):
            // сохранённый профиль и выбор пользователя приоритетнее.
            _ = _vm.ResolveDbmsFromClusterAsync();
            if (!_vm.HasBases)
                _vm.AppendLog(LocalizationManager.T("Dbms.NoBases"));
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
