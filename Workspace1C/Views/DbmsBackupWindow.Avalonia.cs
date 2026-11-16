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
            Title = LocalizationManager.T("Dbms.Title");
            Width = 960;
            Height = 760;
            MinWidth = 760;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, 0, -1, 0);
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

            AvaloniaUi.Place(root, AvaloniaUi.ReadOnlyText(nameof(DbmsBackupViewModel.LogText)), 3);

            var save = AvaloniaUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var run = AvaloniaUi.Button("Dbms.RunNow", async (_, _) => await _vm.RunAsync(), 190, accent: true);
            foreach (var b in new[] { save, run })
                b.Bind(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsIdle)));
            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(DbmsBackupViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(save, run, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 4);

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
